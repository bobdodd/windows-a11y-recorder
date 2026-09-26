using System.Text.Json;
using Npgsql;
using Recorder.Database.Evidence;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Database;

/// <summary>
/// The outcome of opening a recording for playback from the database. When
/// <see cref="Archive"/> is null, <see cref="Reason"/> says why the database
/// cannot provide the recording.
/// </summary>
public sealed record DatabasePlaybackResult(
    SessionPlaybackArchive? Archive,
    RecordingStatus? Status,
    string? Reason);

/// <summary>
/// Opens a recording for playback from the database. The timeline is not
/// loaded: it is read through a <see cref="DatabaseSessionTimeline"/> as
/// playback needs it. Only the events that frames, audio tracks, and
/// browser navigation are built from are read when the recording opens,
/// with the payload properties playback reads. Complete records are read one
/// at a time when they are inspected.
/// </summary>
public sealed class DatabasePlaybackReader(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Opens the recording whose session key is the session folder's name.
    /// A recording stored as completed, failed, or interrupted opens with
    /// the events the database holds; one still being recorded does not.
    /// </summary>
    public async Task<DatabasePlaybackResult> OpenAsync(
        string sessionDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));
        var sessionKey = Path.GetFileName(root);

        Guid recordingId;
        RecordingStatus status;
        await using (var command = dataSource.CreateCommand(
            "SELECT recording_id, recording_status_id FROM recordings WHERE session_key = $1"))
        {
            command.Parameters.AddWithValue(sessionKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new DatabasePlaybackResult(
                    null,
                    null,
                    "The database does not hold this recording.");
            }

            recordingId = reader.GetGuid(0);
            status = (RecordingStatus)reader.GetInt16(1);
        }

        if (status == RecordingStatus.Recording)
        {
            return new DatabasePlaybackResult(
                null,
                status,
                "The recording is still being recorded.");
        }

        var archive = await LoadAsync(recordingId, sessionKey, root, cancellationToken)
            .ConfigureAwait(false);
        return new DatabasePlaybackResult(archive, status, null);
    }

    private async Task<SessionPlaybackArchive> LoadAsync(
        Guid recordingId,
        string sessionKey,
        string root,
        CancellationToken cancellationToken)
    {
        var channels = await NamesAsync<short>(
            "SELECT channel_id, name FROM channels", null, cancellationToken).ConfigureAwait(false);
        var eventTypes = await NamesAsync<int>(
            "SELECT event_type_id, name FROM event_types", null, cancellationToken).ConfigureAwait(false);
        var evidenceClasses = await NamesAsync<short>(
            "SELECT evidence_class_id, name FROM evidence_classes", null, cancellationToken)
            .ConfigureAwait(false);
        var collectors = await NamesAsync<int>(
            "SELECT recording_collector_id, instance_id FROM recording_collectors WHERE recording_id = $1",
            recordingId,
            cancellationToken).ConfigureAwait(false);
        var names = new DatabaseEventNames
        {
            SessionKey = sessionKey,
            Channels = channels,
            EventTypes = eventTypes,
            EvidenceClasses = evidenceClasses,
            Collectors = collectors,
            PayloadChannelIds = channels
                .Where(channel => SessionPlaybackArchiveBuilder.ReadsPayload(channel.Value))
                .Select(channel => channel.Key)
                .ToArray()
        };

        // Only the events that frames, audio tracks, and browser navigation
        // are built from are read here; the timeline queries the rest.
        var builder = new SessionPlaybackArchiveBuilder(root, retainEvents: false);
        await using (var command = dataSource.CreateCommand(
            "SELECT max(monotonic_nanoseconds) FROM events WHERE recording_id = $1"))
        {
            command.Parameters.AddWithValue(recordingId);
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is long latest)
            {
                builder.IncludeTimestamp(latest);
            }
        }

        await using (var command = dataSource.CreateCommand(
            $"SELECT {names.Columns} FROM events e " +
            "WHERE e.recording_id = $1 AND e.channel_id = ANY($3) ORDER BY e.event_key"))
        {
            command.Parameters.AddWithValue(recordingId);
            command.Parameters.AddWithValue(names.PayloadChannelIds);
            command.Parameters.AddWithValue(channels
                .Where(channel => SessionPlaybackArchiveBuilder.BuildsFrom(channel.Value))
                .Select(channel => channel.Key)
                .ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var (item, payload) = names.Read(reader);
                builder.AddEvent(
                    item.EventKey,
                    item.EventId,
                    item.EvidenceClass,
                    item.Channel,
                    item.EventType,
                    item.MonotonicNanoseconds,
                    payload);
            }
        }

        var archive = await builder.BuildAsync(
            new DatabaseEventRecordSource(dataSource, recordingId, sessionKey),
            cancellationToken).ConfigureAwait(false);
        return archive with
        {
            Timeline = await DatabaseSessionTimeline.LoadAsync(
                dataSource,
                recordingId,
                names,
                archive.DurationNanoseconds,
                cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<Dictionary<TKey, string>> NamesAsync<TKey>(
        string sql,
        Guid? recordingId,
        CancellationToken cancellationToken)
        where TKey : notnull
    {
        var names = new Dictionary<TKey, string>();
        await using var command = dataSource.CreateCommand(sql);
        if (recordingId is { } id)
        {
            command.Parameters.AddWithValue(id);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names[reader.GetFieldValue<TKey>(0)] = reader.GetString(1);
        }

        return names;
    }
}

/// <summary>
/// Reads an event's complete record from the database and writes it in the
/// recorder's event JSON form. The record states what was stored: quality flags
/// are a set and are listed in the order their names were first stored, and
/// the payload's properties are in the order PostgreSQL keeps jsonb keys.
/// </summary>
public sealed class DatabaseEventRecordSource(
    NpgsqlDataSource dataSource,
    Guid recordingId,
    string sessionKey) : ISessionEventRecordSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ReadEventJson(SessionTimelineEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var eventKey = item.EventKey;

        // A payload is in its event type's evidence table or, for a channel
        // the recorder does not define, in the jsonb payload table.
        var payloadColumn = EvidenceCatalog.ByEventType.TryGetValue((item.Channel, item.EventType), out var table)
            ? $"coalesce((SELECT {EvidenceSql.PayloadExpression(table)}::text FROM {table.Name} t " +
                "WHERE t.recording_id = $1 AND t.event_key = $2), p.payload::text)"
            : "p.payload::text";
        using var command = dataSource.CreateCommand(
            "SELECT sv.name, ec.name, k.collector_type, rc.instance_id, k.producer_version, " +
            "c.name, k.capture_method, e.sequence, e.monotonic_nanoseconds, cm.name, " +
            "td.name, e.native_timestamp_value, tu.name, e.timestamp_uncertainty_nanoseconds, " +
            "e.observed_utc, e.observed_utc_tick_remainder, et.name, " + payloadColumn + ", " +
            "ARRAY(SELECT q.name FROM event_quality_flags f JOIN quality_flags q USING (quality_flag_id) " +
            "WHERE f.recording_id = $1 AND f.event_key = $2 ORDER BY q.quality_flag_id), " +
            "ARRAY(SELECT r.related_event_id FROM event_related_evidence r " +
            "WHERE r.recording_id = $1 AND r.event_key = $2 ORDER BY r.ordinal), " +
            "a.analyzer, a.analyzer_version, a.method, a.confidence_category, " +
            "a.insufficient_evidence_reason, " +
            "ARRAY(SELECT i.interpretation FROM event_analysis_competing_interpretations i " +
            "WHERE i.recording_id = $1 AND i.event_key = $2 ORDER BY i.ordinal) " +
            "FROM events e " +
            "JOIN event_schema_versions sv USING (event_schema_version_id) " +
            "JOIN evidence_classes ec USING (evidence_class_id) " +
            "JOIN recording_collectors rc USING (recording_collector_id) " +
            "JOIN collector_kinds k ON k.collector_kind_id = rc.collector_kind_id " +
            "JOIN channels c ON c.channel_id = e.channel_id " +
            "JOIN clock_mappings cm USING (clock_mapping_id) " +
            "JOIN event_types et ON et.event_type_id = e.event_type_id " +
            "LEFT JOIN timestamp_domains td ON td.timestamp_domain_id = e.native_timestamp_domain_id " +
            "LEFT JOIN timestamp_units tu ON tu.timestamp_unit_id = e.native_timestamp_unit_id " +
            "LEFT JOIN event_payloads_other_channels p ON p.recording_id = $1 AND p.event_key = $2 " +
            "LEFT JOIN event_analysis a ON a.recording_id = $1 AND a.event_key = $2 " +
            "WHERE e.recording_id = $1 AND e.event_key = $2");
        command.Parameters.AddWithValue(recordingId);
        command.Parameters.AddWithValue(eventKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidDataException(
                $"The database no longer holds event {item.EventId}.");
        }

        if (reader.IsDBNull(17))
        {
            throw new InvalidDataException(
                $"The database holds no payload for event {item.EventId}.");
        }

        var channel = reader.GetString(5);
        var sequence = unchecked((ulong)reader.GetInt64(7));
        var observed = reader.GetFieldValue<DateTime>(14);
        using var payload = JsonDocument.Parse(reader.GetString(17));
        var record = new RecorderEvent
        {
            SchemaVersion = reader.GetString(0),
            EventId = RecorderEventFactory.CreateEventId(
                sessionKey,
                reader.GetString(3),
                channel,
                sequence),
            EvidenceClass = reader.GetString(1),
            SessionId = sessionKey,
            CollectorType = reader.GetString(2),
            CollectorInstanceId = reader.GetString(3),
            ProducerVersion = reader.GetString(4),
            Channel = channel,
            CaptureMethod = reader.GetString(6),
            Sequence = sequence,
            MonotonicNanoseconds = reader.GetInt64(8),
            ClockMappingId = reader.GetString(9),
            NativeTimestamp = reader.IsDBNull(10)
                ? null
                : new NativeTimestamp(reader.GetString(10), reader.GetInt64(11), reader.GetString(12)),
            TimestampUncertaintyNanoseconds = reader.IsDBNull(13) ? null : reader.GetInt64(13),
            ObservedUtc = new DateTimeOffset(observed.Ticks + reader.GetInt16(15), TimeSpan.Zero),
            EventType = reader.GetString(16),
            Payload = payload.RootElement,
            QualityFlags = reader.GetFieldValue<string[]>(18),
            RelatedEvidenceIds = reader.GetFieldValue<string[]>(19),
            Analysis = reader.IsDBNull(20)
                ? null
                : new AnalysisProvenance(
                    reader.GetString(20),
                    reader.GetString(21),
                    reader.GetString(22),
                    reader.IsDBNull(23) ? null : reader.GetString(23),
                    reader.GetFieldValue<string[]>(25),
                    reader.IsDBNull(24) ? null : reader.GetString(24))
        };

        if (record.EventId != item.EventId ||
            record.MonotonicNanoseconds != item.MonotonicNanoseconds)
        {
            throw new InvalidDataException(
                $"Database event {eventKey} is no longer event {item.EventId}.");
        }

        return JsonSerializer.Serialize(record, JsonOptions);
    }
}

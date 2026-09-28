using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Recorder.Database.Evidence;
using Recorder.Session;

namespace Recorder.Database;

/// <summary>
/// Stores a recording's event batches, each in one transaction, with binary
/// COPY. When the database refuses a batch because of its content, the
/// events are stored one at a time and the ones it refuses are returned.
/// </summary>
/// <remarks>
/// Several batches may be written at once, each on its own connection and in
/// its own transaction, so a batch is stored whole or not at all. Batches are
/// prepared one at a time: their events are checked and mapped, and each
/// identity not yet stored is claimed by the first batch to refer to it,
/// which writes it. A batch that refers to an identity an earlier batch
/// claimed waits, before it commits, for that batch to finish, and writes the
/// identity itself if that batch did not store it. Foreign keys are checked
/// when a transaction commits, so a batch can write its rows before the
/// identities they refer to are committed.
/// </remarks>
public sealed class PostgresEventBatchTarget(
    NpgsqlDataSource dataSource,
    Guid recordingId,
    WriterTimings? timings = null)
    : IEventBatchTarget
{
    private readonly ReferenceKeys _keys = new(dataSource);
    private readonly Dictionary<string, (int Id, int Kind)> _collectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _clockMappings = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _prepare = new(1, 1);

    // Guards the claims and stored state of identities, and their rows.
    private readonly object _gate = new();
    private EvidenceMapper? _mapper;
    private long _nextAttempt;

    /// <summary>The next unused event key of the recording.</summary>
    public static async Task<long> NextEventKeyAsync(
        NpgsqlDataSource dataSource,
        Guid recordingId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT coalesce(max(event_key) + 1, 0) FROM events WHERE recording_id = $1",
            connection);
        command.Parameters.AddWithValue(recordingId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<IReadOnlyList<StoreRefusal>> WriteAsync(
        EventBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var refusals = new List<StoreRefusal>();
        var rows = new List<(int Index, EventRow Row)>(batch.Events.Count);
        WriteAttempt attempt;
        var batchStarted = Stopwatch.GetTimestamp();
        await _prepare.WaitAsync(cancellationToken).ConfigureAwait(false);
        timings?.Since("batch.prepare-wait", batchStarted);
        attempt = new WriteAttempt(_nextAttempt++);
        try
        {
            var preparing = Stopwatch.GetTimestamp();
            await PrepareAsync(batch, attempt, rows, refusals, cancellationToken).ConfigureAwait(false);
            timings?.Since("batch.prepare", preparing, batch.Events.Count);
        }
        catch
        {
            // Finished before a later batch is prepared, so no later batch
            // waits for identities this one did not get to write.
            attempt.Done.TrySetResult();
            throw;
        }
        finally
        {
            _prepare.Release();
        }

        try
        {
            var writing = Stopwatch.GetTimestamp();
            var result = await WriteRowsAsync(batch, attempt, rows, refusals, cancellationToken).ConfigureAwait(false);
            timings?.Since("batch.write", writing, batch.Events.Count);
            timings?.Since("batch.total", batchStarted, batch.Events.Count);
            return result;
        }
        finally
        {
            attempt.Done.TrySetResult();
        }
    }

    private async Task PrepareAsync(
        EventBatch batch,
        WriteAttempt attempt,
        List<(int Index, EventRow Row)> rows,
        List<StoreRefusal> refusals,
        CancellationToken cancellationToken)
    {
        if (_mapper is null && batch.Events.Count > 0)
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            _mapper = new EvidenceMapper(
                await EvidenceCopy.NextIdentityKeysAsync(connection, recordingId, cancellationToken)
                    .ConfigureAwait(false));
        }

        var resolving = Stopwatch.GetTimestamp();
        for (var index = 0; index < batch.Events.Count; index++)
        {
            var (row, refusal) = await ResolveAsync(batch.Events[index], cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                refusals.Add(new StoreRefusal(index, refusal!));
                continue;
            }

            rows.Add((index, row));
        }

        timings?.Since("prepare.resolve-events", resolving, batch.Events.Count);

        // Claims the identities the batch will write, and names their rows.
        var planning = Stopwatch.GetTimestamp();
        var plan = Plan(rows.Select(item => item.Row), attempt);
        await ResolveNamesAsync(plan.Fresh.Concat(plan.Shared), cancellationToken).ConfigureAwait(false);
        timings?.Since("prepare.plan-identities", planning, plan.FreshRows.Count + plan.SharedRows.Count);
    }

    private async Task<IReadOnlyList<StoreRefusal>> WriteRowsAsync(
        EventBatch batch,
        WriteAttempt attempt,
        List<(int Index, EventRow Row)> rows,
        List<StoreRefusal> refusals,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteCoreAsync(
                attempt,
                rows.Select(item => item.Row).ToArray(),
                batch.Rejections,
                batch.Omissions,
                refusals.Count,
                cancellationToken).ConfigureAwait(false);
            return refusals;
        }
        catch (PostgresException exception) when (!exception.IsTransient)
        {
            if (rows.Count == 0)
            {
                throw;
            }
        }

        // Find the events the database refuses by storing them one at a time.
        foreach (var (index, row) in rows)
        {
            try
            {
                await WriteCoreAsync(attempt, [row], [], [], 0, cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (!exception.IsTransient)
            {
                refusals.Add(new StoreRefusal(index, $"database-refused:{exception.SqlState}"));
            }
        }

        await WriteCoreAsync(attempt, [], batch.Rejections, batch.Omissions, refusals.Count, cancellationToken)
            .ConfigureAwait(false);
        refusals.Sort((left, right) => left.Index.CompareTo(right.Index));
        return refusals;
    }

    /// <summary>
    /// Decides who writes each identity the rows refer to that is not yet
    /// stored. An identity no write has claimed, or whose claiming write has
    /// finished without storing it, is claimed by this write. One claimed by
    /// an earlier write still in progress is waited for. One claimed by a
    /// later write is also written here, since waiting for a later write
    /// could form a cycle; whichever commits first stores it.
    /// </summary>
    private IdentityPlan Plan(IEnumerable<EventRow> rows, WriteAttempt attempt)
    {
        var evidence = rows.Where(row => row.Evidence is not null).Select(row => row.Evidence!).ToArray();
        var fresh = new List<IdentityRow>();
        var shared = new List<IdentityRow>();
        var awaited = new List<IdentityRow>();
        lock (_gate)
        {
            foreach (var identity in EvidenceCopy.UnstoredIdentities(evidence))
            {
                if (identity.Owner == attempt)
                {
                    (attempt.Fresh.Contains(identity) ? fresh : shared).Add(identity);
                }
                else if (identity.Owner is null)
                {
                    identity.Owner = attempt;
                    attempt.Fresh.Add(identity);
                    fresh.Add(identity);
                }
                else if (identity.Owner.Done.Task.IsCompleted)
                {
                    // The claiming write may have committed it without
                    // learning so, so it is written allowing for that.
                    identity.Owner = attempt;
                    shared.Add(identity);
                }
                else if (identity.Owner.Order < attempt.Order)
                {
                    awaited.Add(identity);
                }
                else
                {
                    shared.Add(identity);
                }
            }

            return new IdentityPlan(
                fresh,
                fresh.SelectMany(identity => identity.Rows).ToArray(),
                shared,
                shared.SelectMany(identity => identity.Rows).ToArray(),
                awaited,
                awaited.Select(identity => identity.Owner!.Done.Task).Distinct().ToArray());
        }
    }

    private async Task ResolveNamesAsync(IEnumerable<IdentityRow> identities, CancellationToken cancellationToken)
    {
        EvidenceRow[] rows;
        lock (_gate)
        {
            rows = identities.Where(identity => !identity.Stored).SelectMany(identity => identity.Rows).ToArray();
        }

        await ResolveNamesAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(EventRow? Row, string? Refusal)> ResolveAsync(
        BufferedEvent buffered,
        CancellationToken cancellationToken)
    {
        var record = buffered.Event;
        var kind = await _keys.GetCollectorKindAsync(
            record.CollectorType,
            record.ProducerVersion,
            record.CaptureMethod,
            cancellationToken).ConfigureAwait(false);
        if (!_collectors.TryGetValue(record.CollectorInstanceId, out var collector))
        {
            collector = await ReferenceKeys.GetRecordingCollectorAsync(
                dataSource,
                recordingId,
                record.CollectorInstanceId,
                kind,
                cancellationToken).ConfigureAwait(false);
            _collectors[record.CollectorInstanceId] = collector;
        }

        if (collector.Kind != kind)
        {
            return (null, "event-collector-mismatch");
        }

        EvidenceRows? evidence = null;
        if (EvidenceCatalog.ByEventType.TryGetValue((record.Channel, record.EventType), out var table))
        {
            var mapping = Stopwatch.GetTimestamp();
            try
            {
                using var payload = JsonDocument.Parse(buffered.PayloadJson);
                evidence = _mapper!.Map(table, buffered.EventKey, payload.RootElement);
                timings?.Since("prepare.parse-and-map:" + record.EventType, mapping);
            }
            catch (EvidenceMappingException exception)
            {
                return (null, exception.Reason);
            }
            catch (JsonException)
            {
                return (null, "payload-not-json");
            }

            var naming = Stopwatch.GetTimestamp();
            await ResolveNamesAsync(evidence.Rows, cancellationToken).ConfigureAwait(false);
            timings?.Since("prepare.names", naming, evidence.Rows.Count);
        }
        else if (EventPayloadValidator.IsBuiltInChannel(record.Channel))
        {
            // A built-in payload has evidence tables or is not stored.
            return (null, "event-type-unmapped");
        }

        if (!_clockMappings.TryGetValue(record.ClockMappingId, out var clockMapping))
        {
            clockMapping = await ReferenceKeys.GetClockMappingAsync(
                dataSource,
                recordingId,
                record.ClockMappingId,
                cancellationToken).ConfigureAwait(false);
            _clockMappings[record.ClockMappingId] = clockMapping;
        }

        var flags = new List<int>(record.QualityFlags.Count);
        foreach (var flag in record.QualityFlags.Distinct(StringComparer.Ordinal))
        {
            flags.Add(await _keys.GetAsync(ReferenceKeys.QualityFlags, flag, cancellationToken)
                .ConfigureAwait(false));
        }

        short? domain = null;
        short? unit = null;
        if (record.NativeTimestamp is { } native)
        {
            domain = (short)await _keys.GetAsync(ReferenceKeys.TimestampDomains, native.Domain, cancellationToken)
                .ConfigureAwait(false);
            unit = (short)await _keys.GetAsync(ReferenceKeys.TimestampUnits, native.Unit, cancellationToken)
                .ConfigureAwait(false);
        }

        var ticks = record.ObservedUtc.UtcTicks;
        var remainder = (short)(ticks % 10);
        return (new EventRow(
            buffered,
            collector.Id,
            (short)await _keys.GetAsync(ReferenceKeys.Channels, record.Channel, cancellationToken)
                .ConfigureAwait(false),
            await _keys.GetAsync(ReferenceKeys.EventTypes, record.EventType, cancellationToken)
                .ConfigureAwait(false),
            RecordingEventWriter.EvidenceClassId(record.EvidenceClass),
            (short)await _keys.GetAsync(ReferenceKeys.EventSchemaVersions, record.SchemaVersion, cancellationToken)
                .ConfigureAwait(false),
            clockMapping,
            new DateTime(ticks - remainder, DateTimeKind.Utc),
            remainder,
            domain,
            unit,
            flags,
            evidence), null);
    }

    private async Task ResolveNamesAsync(IEnumerable<EvidenceRow> rows, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            for (var index = 0; index < row.Values.Length; index++)
            {
                if (row.Values[index] is PendingName name)
                {
                    row.Values[index] = await _keys.GetAsync(ReferenceKeys.Names, name.Value, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    private async Task WriteCoreAsync(
        WriteAttempt attempt,
        IReadOnlyList<EventRow> rows,
        IReadOnlyList<WriterRejection> rejections,
        IReadOnlyList<WriterOmission> omissions,
        int refusedCount,
        CancellationToken cancellationToken)
    {
        var opening = Stopwatch.GetTimestamp();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var defer = new NpgsqlCommand("SET CONSTRAINTS ALL DEFERRED", connection, transaction))
        {
            await defer.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        timings?.Since("write.open", opening);

        var stored = new List<IdentityRow>();
        if (rows.Count > 0)
        {
            var plan = Plan(rows, attempt);
            var copying = Stopwatch.GetTimestamp();
            await CopyEventsAsync(connection, rows, cancellationToken).ConfigureAwait(false);
            timings?.Since("copy.events", copying, rows.Count);
            copying = Stopwatch.GetTimestamp();
            await CopyChildrenAsync(connection, rows, cancellationToken).ConfigureAwait(false);
            timings?.Since("copy.event-children", copying, rows.Count);
            await EvidenceCopy.CopyAsync(
                connection,
                recordingId,
                plan.FreshRows.Concat(rows.Where(row => row.Evidence is not null).SelectMany(row => row.Evidence!.Rows)),
                cancellationToken,
                timings).ConfigureAwait(false);

            // The earlier writes this one waits for hold no lock it takes, so
            // they finish whether or not this one waits.
            var waiting = Stopwatch.GetTimestamp();
            await Task.WhenAll(plan.Owners).WaitAsync(cancellationToken).ConfigureAwait(false);
            timings?.Since("write.wait-identity-owners", waiting, plan.Owners.Count);
            EvidenceRow[] shared;
            lock (_gate)
            {
                var unstored = plan.Awaited.Where(identity => !identity.Stored).ToArray();
                shared = [.. plan.SharedRows, .. unstored.SelectMany(identity => identity.Rows)];
                stored.AddRange(plan.Fresh);
                stored.AddRange(plan.Shared);
                stored.AddRange(unstored);
            }

            if (shared.Length > 0)
            {
                var sharing = Stopwatch.GetTimestamp();
                await EvidenceCopy.CopyIgnoringConflictsAsync(connection, recordingId, shared, cancellationToken)
                    .ConfigureAwait(false);
                timings?.Since("write.copy-shared-identities", sharing, shared.Length);
            }
        }

        foreach (var rejection in rejections)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO event_rejections (recording_id, rejection_ordinal, reason, channel, " +
                "event_type, sequence) VALUES ($1, $2, $3, $4, $5, $6) ON CONFLICT DO NOTHING",
                connection,
                transaction);
            command.Parameters.AddWithValue(recordingId);
            command.Parameters.AddWithValue(rejection.Ordinal);
            command.Parameters.AddWithValue(Clean(rejection.Reason)!);
            command.Parameters.AddWithValue((object?)Clean(rejection.Channel) ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)Clean(rejection.EventType) ?? DBNull.Value);
            command.Parameters.AddWithValue(
                rejection.Sequence is { } sequence ? (decimal)sequence : DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var omission in omissions)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO writer_omissions (recording_id, omission_ordinal, " +
                "first_monotonic_nanoseconds, last_monotonic_nanoseconds, event_count) " +
                "VALUES ($1, $2, $3, $4, $5) ON CONFLICT DO NOTHING",
                connection,
                transaction);
            command.Parameters.AddWithValue(recordingId);
            command.Parameters.AddWithValue(omission.Ordinal);
            command.Parameters.AddWithValue(omission.FirstMonotonicNanoseconds);
            command.Parameters.AddWithValue(omission.LastMonotonicNanoseconds);
            command.Parameters.AddWithValue(omission.EventCount);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var refusedTotal = rejections.Count + refusedCount;
        if (refusedTotal > 0)
        {
            await using var command = new NpgsqlCommand(
                "UPDATE recordings SET rejected_event_count = rejected_event_count + $2 " +
                "WHERE recording_id = $1",
                connection,
                transaction);
            command.Parameters.AddWithValue(recordingId);
            command.Parameters.AddWithValue((long)refusedTotal);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Once started, a commit is not cancelled, so the writer knows
        // whether the batch was stored.
        var committing = Stopwatch.GetTimestamp();
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        timings?.Since("write.commit", committing);
        lock (_gate)
        {
            foreach (var identity in stored)
            {
                identity.Stored = true;
                identity.Owner = null;
                identity.Rows.Clear();
                identity.Dependencies.Clear();
            }
        }
    }

    private async Task CopyEventsAsync(
        NpgsqlConnection connection,
        IReadOnlyList<EventRow> rows,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(
            "COPY events (recording_id, event_key, recording_collector_id, channel_id, sequence, " +
            "monotonic_nanoseconds, event_type_id, evidence_class_id, event_schema_version_id, " +
            "clock_mapping_id, observed_utc, observed_utc_tick_remainder, " +
            "native_timestamp_domain_id, native_timestamp_value, native_timestamp_unit_id, " +
            "timestamp_uncertainty_nanoseconds) FROM STDIN (FORMAT BINARY)",
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            var record = row.Buffered.Event;
            importer.StartRow();
            importer.Write(recordingId, NpgsqlDbType.Uuid);
            importer.Write(row.Buffered.EventKey, NpgsqlDbType.Bigint);
            importer.Write(row.CollectorId, NpgsqlDbType.Integer);
            importer.Write(row.ChannelId, NpgsqlDbType.Smallint);
            importer.Write((long)record.Sequence, NpgsqlDbType.Bigint);
            importer.Write(record.MonotonicNanoseconds, NpgsqlDbType.Bigint);
            importer.Write(row.EventTypeId, NpgsqlDbType.Integer);
            importer.Write(row.EvidenceClassId, NpgsqlDbType.Smallint);
            importer.Write(row.SchemaVersionId, NpgsqlDbType.Smallint);
            importer.Write(row.ClockMappingId, NpgsqlDbType.Integer);
            importer.Write(row.ObservedUtc, NpgsqlDbType.TimestampTz);
            importer.Write(row.TickRemainder, NpgsqlDbType.Smallint);
            WriteNullable(importer, row.NativeDomainId, NpgsqlDbType.Smallint);
            WriteNullable(importer, record.NativeTimestamp?.Value, NpgsqlDbType.Bigint);
            WriteNullable(importer, row.NativeUnitId, NpgsqlDbType.Smallint);
            WriteNullable(importer, record.TimestampUncertaintyNanoseconds, NpgsqlDbType.Bigint);
        }

        await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CopyChildrenAsync(
        NpgsqlConnection connection,
        IReadOnlyList<EventRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Any(row => row.QualityFlagIds.Count > 0))
        {
            await using var importer = await connection.BeginBinaryImportAsync(
                "COPY event_quality_flags (recording_id, event_key, quality_flag_id) " +
                "FROM STDIN (FORMAT BINARY)",
                cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                foreach (var flag in row.QualityFlagIds)
                {
                    importer.StartRow();
                    importer.Write(recordingId, NpgsqlDbType.Uuid);
                    importer.Write(row.Buffered.EventKey, NpgsqlDbType.Bigint);
                    importer.Write(flag, NpgsqlDbType.Integer);
                }
            }

            await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        if (rows.Any(row => row.Buffered.Event.RelatedEvidenceIds.Count > 0))
        {
            await using var importer = await connection.BeginBinaryImportAsync(
                "COPY event_related_evidence (recording_id, event_key, ordinal, related_event_id) " +
                "FROM STDIN (FORMAT BINARY)",
                cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                var related = row.Buffered.Event.RelatedEvidenceIds;
                for (var ordinal = 0; ordinal < related.Count; ordinal++)
                {
                    importer.StartRow();
                    importer.Write(recordingId, NpgsqlDbType.Uuid);
                    importer.Write(row.Buffered.EventKey, NpgsqlDbType.Bigint);
                    importer.Write((short)ordinal, NpgsqlDbType.Smallint);
                    importer.Write(related[ordinal], NpgsqlDbType.Text);
                }
            }

            await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        var analysed = rows.Where(row => row.Buffered.Event.Analysis is not null).ToArray();
        if (analysed.Length > 0)
        {
            await using (var importer = await connection.BeginBinaryImportAsync(
                "COPY event_analysis (recording_id, event_key, analyzer, analyzer_version, method, " +
                "confidence_category, insufficient_evidence_reason) FROM STDIN (FORMAT BINARY)",
                cancellationToken).ConfigureAwait(false))
            {
                foreach (var row in analysed)
                {
                    var analysis = row.Buffered.Event.Analysis!;
                    importer.StartRow();
                    importer.Write(recordingId, NpgsqlDbType.Uuid);
                    importer.Write(row.Buffered.EventKey, NpgsqlDbType.Bigint);
                    importer.Write(analysis.Analyzer, NpgsqlDbType.Text);
                    importer.Write(analysis.AnalyzerVersion, NpgsqlDbType.Text);
                    importer.Write(analysis.Method, NpgsqlDbType.Text);
                    WriteNullableText(importer, analysis.ConfidenceCategory);
                    WriteNullableText(importer, analysis.InsufficientEvidenceReason);
                }

                await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }

            if (analysed.Any(row => row.Buffered.Event.Analysis!.CompetingInterpretations.Count > 0))
            {
                await using var importer = await connection.BeginBinaryImportAsync(
                    "COPY event_analysis_competing_interpretations (recording_id, event_key, ordinal, " +
                    "interpretation) FROM STDIN (FORMAT BINARY)",
                    cancellationToken).ConfigureAwait(false);
                foreach (var row in analysed)
                {
                    var interpretations = row.Buffered.Event.Analysis!.CompetingInterpretations;
                    for (var ordinal = 0; ordinal < interpretations.Count; ordinal++)
                    {
                        importer.StartRow();
                        importer.Write(recordingId, NpgsqlDbType.Uuid);
                        importer.Write(row.Buffered.EventKey, NpgsqlDbType.Bigint);
                        importer.Write((short)ordinal, NpgsqlDbType.Smallint);
                        importer.Write(interpretations[ordinal], NpgsqlDbType.Text);
                    }
                }

                await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // The payload of a channel the recorder does not define is stored as
        // jsonb. See Migrations/0008_other_channel_payloads.sql.
        var unmapped = rows.Where(row => row.Evidence is null).ToArray();
        if (unmapped.Length == 0)
        {
            return;
        }

        await using (var importer = await connection.BeginBinaryImportAsync(
            "COPY event_payloads_other_channels (recording_id, event_key, payload) FROM STDIN (FORMAT BINARY)",
            cancellationToken).ConfigureAwait(false))
        {
            foreach (var row in unmapped)
            {
                importer.StartRow();
                importer.Write(recordingId, NpgsqlDbType.Uuid);
                importer.Write(row.Buffered.EventKey, NpgsqlDbType.Bigint);
                importer.Write(row.Buffered.PayloadJson, NpgsqlDbType.Jsonb);
            }

            await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // Rows are written synchronously: the importer buffers them, and an
    // awaited call per column costs more than the write itself.
    private static void WriteNullable<T>(NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type)
        where T : struct
    {
        if (value is { } present)
        {
            importer.Write(present, type);
        }
        else
        {
            importer.WriteNull();
        }
    }

    private static void WriteNullableText(NpgsqlBinaryImporter importer, string? value)
    {
        if (value is null)
        {
            importer.WriteNull();
        }
        else
        {
            importer.Write(value, NpgsqlDbType.Text);
        }
    }

    // PostgreSQL text cannot hold the NUL character.
    private static string? Clean(string? value) => value?.Replace("\0", "\\u0000", StringComparison.Ordinal);

    private sealed record IdentityPlan(
        IReadOnlyList<IdentityRow> Fresh,
        IReadOnlyList<EvidenceRow> FreshRows,
        IReadOnlyList<IdentityRow> Shared,
        IReadOnlyList<EvidenceRow> SharedRows,
        IReadOnlyList<IdentityRow> Awaited,
        IReadOnlyList<Task> Owners);

    private sealed record EventRow(
        BufferedEvent Buffered,
        int CollectorId,
        short ChannelId,
        int EventTypeId,
        short EvidenceClassId,
        short SchemaVersionId,
        int ClockMappingId,
        DateTime ObservedUtc,
        short TickRemainder,
        short? NativeDomainId,
        short? NativeUnitId,
        IReadOnlyList<int> QualityFlagIds,
        EvidenceRows? Evidence);
}

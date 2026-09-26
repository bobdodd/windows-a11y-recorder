using Npgsql;
using NpgsqlTypes;

namespace Recorder.Database;

/// <summary>
/// Stores a recording's event batches, each in one transaction, with binary
/// COPY. When the database refuses a batch because of its content, the
/// events are stored one at a time and the ones it refuses are returned.
/// </summary>
public sealed class PostgresEventBatchTarget(NpgsqlDataSource dataSource, Guid recordingId)
    : IEventBatchTarget
{
    private readonly ReferenceKeys _keys = new(dataSource);
    private readonly Dictionary<string, (int Id, int Kind)> _collectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _clockMappings = new(StringComparer.Ordinal);

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
        for (var index = 0; index < batch.Events.Count; index++)
        {
            var row = await ResolveAsync(batch.Events[index], cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                refusals.Add(new StoreRefusal(index, "event-collector-mismatch"));
                continue;
            }

            rows.Add((index, row));
        }

        try
        {
            await WriteCoreAsync(
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
                await WriteCoreAsync([row], [], [], 0, cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (!exception.IsTransient)
            {
                refusals.Add(new StoreRefusal(index, $"database-refused:{exception.SqlState}"));
            }
        }

        await WriteCoreAsync([], batch.Rejections, batch.Omissions, refusals.Count, cancellationToken)
            .ConfigureAwait(false);
        refusals.Sort((left, right) => left.Index.CompareTo(right.Index));
        return refusals;
    }

    private async Task<EventRow?> ResolveAsync(BufferedEvent buffered, CancellationToken cancellationToken)
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
            return null;
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
        return new EventRow(
            buffered,
            collector.Id,
            (short)await _keys.GetAsync(ReferenceKeys.Channels, record.Channel, cancellationToken)
                .ConfigureAwait(false),
            await _keys.GetAsync(ReferenceKeys.EventTypes, record.EventType, cancellationToken)
                .ConfigureAwait(false),
            PostgresEventWriter.EvidenceClassId(record.EvidenceClass),
            (short)await _keys.GetAsync(ReferenceKeys.EventSchemaVersions, record.SchemaVersion, cancellationToken)
                .ConfigureAwait(false),
            clockMapping,
            new DateTime(ticks - remainder, DateTimeKind.Utc),
            remainder,
            domain,
            unit,
            flags);
    }

    private async Task WriteCoreAsync(
        IReadOnlyList<EventRow> rows,
        IReadOnlyList<WriterRejection> rejections,
        IReadOnlyList<WriterOmission> omissions,
        int refusedCount,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count > 0)
        {
            await CopyEventsAsync(connection, rows, cancellationToken).ConfigureAwait(false);
            await CopyChildrenAsync(connection, rows, cancellationToken).ConfigureAwait(false);
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

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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

        // Transitional: every payload is stored as jsonb until its evidence
        // model has tables. See Migrations/0001_core.sql.
        await using (var importer = await connection.BeginBinaryImportAsync(
            "COPY event_payloads_unmapped (recording_id, event_key, payload) FROM STDIN (FORMAT BINARY)",
            cancellationToken).ConfigureAwait(false))
        {
            foreach (var row in rows)
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
        IReadOnlyList<int> QualityFlagIds);
}

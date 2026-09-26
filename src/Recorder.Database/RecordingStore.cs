using Npgsql;
using Recorder.Contracts;

namespace Recorder.Database;

public enum RecordingStatus : short
{
    Recording = 1,
    Completed = 2,
    Failed = 3,
    Interrupted = 4
}

public sealed record RecordingCaptureSettings(
    bool CaptureKeyboardAndMouse,
    bool CaptureUiAutomation,
    bool CaptureForegroundWindow,
    bool CaptureDesktopFrames,
    int FramesPerSecond,
    bool CaptureMicrophone,
    bool CaptureSystemAudio);

public sealed record RecordingDefinition(
    string SessionKey,
    DateTimeOffset StartedUtc,
    long ClockFrequency,
    long ClockOriginTimestamp,
    string OperatingSystem,
    string Runtime,
    string Architecture,
    RecordingCaptureSettings Capture);

public sealed record CollectorRegistration(
    CollectorDescriptor Descriptor,
    CapabilityStatus? CapabilityStatus,
    IReadOnlyList<string> Limitations,
    CollectorLifecycleState LifecycleState,
    CollectorHealthState HealthState);

public sealed record RecordingCompletion(
    RecordingStatus Status,
    DateTimeOffset? EndedUtc,
    long? DurationNanoseconds,
    long AcceptedEventCount,
    long DroppedEventCount,
    string? Failure);

/// <summary>
/// Creates and updates projects, recordings, and the collectors of a
/// recording. A recording's events are written by <see cref="PostgresEventWriter"/>.
/// </summary>
public sealed class RecordingStore(NpgsqlDataSource dataSource)
{
    private readonly ReferenceKeys _keys = new(dataSource);

    public async Task<Guid> EnsureProjectAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "WITH added AS (INSERT INTO projects (name) VALUES ($1) " +
            "ON CONFLICT (name) DO NOTHING RETURNING project_id) " +
            "SELECT project_id FROM added UNION ALL " +
            "SELECT project_id FROM projects WHERE name = $1 LIMIT 1",
            connection);
        command.Parameters.AddWithValue(name);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Adds a recording in the recording state, with a partition of each
    /// per-recording table, in one transaction.
    /// </summary>
    public async Task<Guid> CreateRecordingAsync(
        Guid projectId,
        RecordingDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid recordingId;
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO recordings (project_id, session_key, recording_status_id, " +
            "started_utc, clock_frequency, clock_origin_timestamp, os_description, " +
            "framework_description, process_architecture, capture_keyboard_and_mouse, " +
            "capture_ui_automation, capture_foreground_window, capture_desktop_frames, " +
            "frames_per_second, capture_microphone, capture_system_audio) " +
            "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16) " +
            "RETURNING recording_id",
            connection,
            transaction))
        {
            var capture = definition.Capture;
            insert.Parameters.AddWithValue(projectId);
            insert.Parameters.AddWithValue(definition.SessionKey);
            insert.Parameters.AddWithValue((short)RecordingStatus.Recording);
            insert.Parameters.AddWithValue(definition.StartedUtc.UtcDateTime);
            insert.Parameters.AddWithValue(definition.ClockFrequency);
            insert.Parameters.AddWithValue(definition.ClockOriginTimestamp);
            insert.Parameters.AddWithValue(definition.OperatingSystem);
            insert.Parameters.AddWithValue(definition.Runtime);
            insert.Parameters.AddWithValue(definition.Architecture);
            insert.Parameters.AddWithValue(capture.CaptureKeyboardAndMouse);
            insert.Parameters.AddWithValue(capture.CaptureUiAutomation);
            insert.Parameters.AddWithValue(capture.CaptureForegroundWindow);
            insert.Parameters.AddWithValue(capture.CaptureDesktopFrames);
            insert.Parameters.AddWithValue(capture.FramesPerSecond);
            insert.Parameters.AddWithValue(capture.CaptureMicrophone);
            insert.Parameters.AddWithValue(capture.CaptureSystemAudio);
            recordingId = (Guid)(await insert.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false))!;
        }

        foreach (var (table, prefix) in await PartitionedTablesAsync(connection, transaction, cancellationToken)
                     .ConfigureAwait(false))
        {
            // Both names come from the database and the recording key, not
            // from user input.
            await using var create = new NpgsqlCommand(
                $"CREATE TABLE {PartitionName(prefix, recordingId)} PARTITION OF {table} " +
                $"FOR VALUES IN ('{recordingId:D}')",
                connection,
                transaction);
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return recordingId;
    }

    /// <summary>
    /// Adds the collectors of a recording, or updates what is known about
    /// them. The writer also adds a collector the first time it sees one of
    /// its events.
    /// </summary>
    public async Task RegisterCollectorsAsync(
        Guid recordingId,
        IEnumerable<CollectorRegistration> collectors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectors);
        foreach (var collector in collectors)
        {
            var descriptor = collector.Descriptor;
            var kind = await _keys.GetCollectorKindAsync(
                descriptor.CollectorType,
                descriptor.ImplementationVersion,
                descriptor.CaptureMethod,
                cancellationToken).ConfigureAwait(false);
            var (recordingCollectorId, registeredKind) = await ReferenceKeys.GetRecordingCollectorAsync(
                dataSource,
                recordingId,
                descriptor.InstanceId,
                kind,
                cancellationToken).ConfigureAwait(false);
            if (registeredKind != kind)
            {
                throw new InvalidOperationException(
                    $"Collector {descriptor.InstanceId} is already registered with a different " +
                    "type, version, or capture method.");
            }

            var channelIds = new List<int>();
            foreach (var channel in descriptor.Channels)
            {
                channelIds.Add(await _keys.GetAsync(ReferenceKeys.Channels, channel, cancellationToken)
                    .ConfigureAwait(false));
            }

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using (var update = new NpgsqlCommand(
                "UPDATE recording_collectors SET implementation = $2, contract_version = $3, " +
                "capability_status = $4, lifecycle_state = $5, health_state = $6 " +
                "WHERE recording_collector_id = $1",
                connection,
                transaction))
            {
                update.Parameters.AddWithValue(recordingCollectorId);
                update.Parameters.AddWithValue(descriptor.Implementation);
                update.Parameters.AddWithValue(descriptor.ContractVersion);
                update.Parameters.AddWithValue(
                    (object?)collector.CapabilityStatus?.ToString() ?? DBNull.Value);
                update.Parameters.AddWithValue(collector.LifecycleState.ToString());
                update.Parameters.AddWithValue(collector.HealthState.ToString());
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var channelId in channelIds)
            {
                await using var channel = new NpgsqlCommand(
                    "INSERT INTO recording_collector_channels (recording_collector_id, channel_id) " +
                    "VALUES ($1, $2) ON CONFLICT DO NOTHING",
                    connection,
                    transaction);
                channel.Parameters.AddWithValue(recordingCollectorId);
                channel.Parameters.AddWithValue((short)channelId);
                await channel.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var clear = new NpgsqlCommand(
                "DELETE FROM recording_collector_limitations WHERE recording_collector_id = $1",
                connection,
                transaction))
            {
                clear.Parameters.AddWithValue(recordingCollectorId);
                await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            for (var ordinal = 0; ordinal < collector.Limitations.Count; ordinal++)
            {
                await using var limitation = new NpgsqlCommand(
                    "INSERT INTO recording_collector_limitations " +
                    "(recording_collector_id, ordinal, limitation) VALUES ($1, $2, $3)",
                    connection,
                    transaction);
                limitation.Parameters.AddWithValue(recordingCollectorId);
                limitation.Parameters.AddWithValue((short)ordinal);
                limitation.Parameters.AddWithValue(collector.Limitations[ordinal]);
                await limitation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CompleteRecordingAsync(
        Guid recordingId,
        RecordingCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "UPDATE recordings SET recording_status_id = $2, ended_utc = $3, " +
            "duration_nanoseconds = $4, accepted_event_count = $5, dropped_event_count = $6, " +
            "failure = $7 WHERE recording_id = $1",
            connection);
        command.Parameters.AddWithValue(recordingId);
        command.Parameters.AddWithValue((short)completion.Status);
        command.Parameters.AddWithValue(
            (object?)completion.EndedUtc?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)completion.DurationNanoseconds ?? DBNull.Value);
        command.Parameters.AddWithValue(completion.AcceptedEventCount);
        command.Parameters.AddWithValue(completion.DroppedEventCount);
        command.Parameters.AddWithValue((object?)completion.Failure ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException($"Recording {recordingId} does not exist.");
        }
    }

    /// <summary>
    /// Marks every recording still in the recording state as interrupted. The
    /// app calls this when it starts, before it begins a new recording, so a
    /// recording left open by an app that ended without stopping it is not
    /// shown as in progress.
    /// </summary>
    public async Task<int> MarkOpenRecordingsInterruptedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "UPDATE recordings SET recording_status_id = $1 WHERE recording_status_id = $2",
            connection);
        command.Parameters.AddWithValue((short)RecordingStatus.Interrupted);
        command.Parameters.AddWithValue((short)RecordingStatus.Recording);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecordingStatus?> GetStatusAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT recording_status_id FROM recordings WHERE recording_id = $1",
            connection);
        command.Parameters.AddWithValue(recordingId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is short status
            ? (RecordingStatus)status
            : null;
    }

    /// <summary>
    /// Removes a recording. Its partitions are dropped rather than deleted
    /// row by row, so removal does not depend on the recording's size.
    /// </summary>
    public async Task DeleteRecordingAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var tables = await PartitionedTablesAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        // Referring tables first. A referenced partition is detached before
        // it is dropped, because PostgreSQL does not drop a partition that a
        // partitioned table's foreign key refers to while it is attached.
        foreach (var (table, prefix) in tables.Reverse())
        {
            var partition = PartitionName(prefix, recordingId);
            await using var exists = new NpgsqlCommand(
                "SELECT to_regclass($1) IS NOT NULL",
                connection,
                transaction);
            exists.Parameters.AddWithValue(partition);
            if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!)
            {
                continue;
            }

            await using var detach = new NpgsqlCommand(
                $"ALTER TABLE {table} DETACH PARTITION {partition}",
                connection,
                transaction);
            await detach.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using var drop = new NpgsqlCommand($"DROP TABLE {partition}", connection, transaction);
            await drop.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var delete = new NpgsqlCommand(
            "DELETE FROM recordings WHERE recording_id = $1",
            connection,
            transaction))
        {
            delete.Parameters.AddWithValue(recordingId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string PartitionName(string prefix, Guid recordingId) =>
        $"{prefix}_{recordingId:N}";

    private static async Task<IReadOnlyList<(string Table, string Prefix)>> PartitionedTablesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT table_name, partition_prefix FROM recording_partitioned_tables ORDER BY partition_order",
            connection,
            transaction);
        var tables = new List<(string, string)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        return tables;
    }
}

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
    RecordingCaptureSettings Capture,
    string? Name = null);

public sealed record CollectorRegistration(
    CollectorDescriptor Descriptor,
    CapabilityStatus? CapabilityStatus,
    IReadOnlyList<string> Limitations,
    CollectorLifecycleState LifecycleState,
    CollectorHealthState HealthState);

/// <summary>A project as the app lists it.</summary>
public sealed record ProjectSummary(Guid ProjectId, string Name, DateTime CreatedUtc, int Recordings);

/// <summary>One of a project's recordings as the app lists it; its name is null when it has none.</summary>
public sealed record ProjectRecordingSummary(Guid RecordingId, string SessionKey, DateTime StartedUtc, long? DurationNanoseconds, RecordingStatus Status, string? Name = null)
{
    /// <summary>Its name and session key, as "Sign in (20261114-…)", or its session key alone.</summary>
    public string Label => RecordingNames.Label(Name, SessionKey);
}

/// <summary>What moving recordings to a project did: how many moved, and those left where they were because the project has a recording of the same name.</summary>
public sealed record RecordingMove(int Moved, IReadOnlyList<string> Refused);

/// <summary>Whether a recording was renamed (recording-names.md).</summary>
public enum RecordingRename
{
    Renamed,
    NameTaken,
    Missing
}

/// <summary>
/// Recordings' readable names (recording-names.md; owner, 2026-11-14): a
/// recording may have a name, unique within its project, and is shown as
/// its name and then its session key, or its session key alone when it has
/// none. The session key stays its identity and its folder's name.
/// </summary>
public static class RecordingNames
{
    /// <summary>A name as typed, with spaces at either end removed; null when nothing is left, for a recording with no name.</summary>
    public static string? Normalize(string? typed) =>
        string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();

    /// <summary>The recording as lists and reports show it: "Sign in (20261114-…)", or the session key alone.</summary>
    public static string Label(string? name, string sessionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        return Normalize(name) is { } named ? $"{named} ({sessionKey})" : sessionKey;
    }
}

public sealed record RecordingCompletion(
    RecordingStatus Status,
    DateTimeOffset? EndedUtc,
    long? DurationNanoseconds,
    long AcceptedEventCount,
    long DroppedEventCount,
    string? Failure);

/// <summary>
/// Creates and updates projects, recordings, and the collectors of a
/// recording. A recording's events are written by <see cref="RecordingEventWriter"/>
/// to its recording file, whose chunk index is stored here.
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

    /// <summary>The projects, by name, each with how many recordings it has (projects.md).</summary>
    public async Task<IReadOnlyList<ProjectSummary>> ProjectsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT p.project_id, p.name, p.created_utc, count(r.recording_id) FROM projects p " +
            "LEFT JOIN recordings r ON r.project_id = p.project_id " +
            "GROUP BY p.project_id, p.name, p.created_utc ORDER BY lower(p.name), p.name",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var projects = new List<ProjectSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            projects.Add(new ProjectSummary(reader.GetGuid(0), reader.GetString(1),
                DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc), (int)reader.GetInt64(3)));
        }

        return projects;
    }

    /// <summary>
    /// Adds a project, or returns null when one has that name already,
    /// names compared as typed with spaces at either end removed.
    /// </summary>
    public async Task<Guid?> CreateProjectAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = ProjectName(name);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "INSERT INTO projects (name) VALUES ($1) ON CONFLICT (name) DO NOTHING RETURNING project_id",
            connection);
        command.Parameters.AddWithValue(trimmed);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as Guid?;
    }

    /// <summary>Renames a project; false when another project has that name, or the project is gone.</summary>
    public async Task<bool> RenameProjectAsync(Guid projectId, string name, CancellationToken cancellationToken = default)
    {
        var trimmed = ProjectName(name);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "UPDATE projects SET name = $2 WHERE project_id = $1 " +
            "AND NOT EXISTS (SELECT 1 FROM projects WHERE name = $2 AND project_id <> $1)",
            connection);
        command.Parameters.AddWithValue(projectId);
        command.Parameters.AddWithValue(trimmed);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <summary>A project's recordings, in the order they were made.</summary>
    public async Task<IReadOnlyList<ProjectRecordingSummary>> RecordingsOfAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT recording_id, session_key, started_utc, duration_nanoseconds, recording_status_id, name FROM recordings " +
            "WHERE project_id = $1 ORDER BY started_utc, session_key",
            connection);
        command.Parameters.AddWithValue(projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var recordings = new List<ProjectRecordingSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            recordings.Add(new ProjectRecordingSummary(reader.GetGuid(0), reader.GetString(1),
                DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc),
                reader.IsDBNull(3) ? null : reader.GetInt64(3), (RecordingStatus)reader.GetInt16(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return recordings;
    }

    /// <summary>
    /// Moves recordings to a project, with their analysis. A recording whose
    /// name the project already has is left where it is, as no two
    /// recordings in a project share a name (recording-names.md); those are
    /// returned by name and session key.
    /// </summary>
    public async Task<RecordingMove> MoveRecordingsAsync(IReadOnlyCollection<Guid> recordingIds, Guid projectId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recordingIds);
        if (recordingIds.Count == 0)
        {
            return new RecordingMove(0, []);
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var refused = new List<string>();
        await using (var taken = new NpgsqlCommand(
            "SELECT r.name, r.session_key FROM recordings r WHERE r.recording_id = ANY($1) AND r.project_id <> $2 " +
            "AND r.name IS NOT NULL AND EXISTS (SELECT 1 FROM recordings o WHERE o.project_id = $2 AND o.name = r.name) " +
            "ORDER BY r.started_utc, r.session_key",
            connection,
            transaction))
        {
            taken.Parameters.AddWithValue(recordingIds.ToArray());
            taken.Parameters.AddWithValue(projectId);
            await using var reader = await taken.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                refused.Add(RecordingNames.Label(reader.GetString(0), reader.GetString(1)));
            }
        }

        int moved;
        await using (var command = new NpgsqlCommand(
            "UPDATE recordings r SET project_id = $2 WHERE r.recording_id = ANY($1) AND r.project_id <> $2 " +
            "AND (r.name IS NULL OR NOT EXISTS (SELECT 1 FROM recordings o WHERE o.project_id = $2 AND o.name = r.name))",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(recordingIds.ToArray());
            command.Parameters.AddWithValue(projectId);
            moved = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new RecordingMove(moved, refused);
    }

    /// <summary>
    /// Whether a recording in the project has the name, other than the one
    /// given; false for no name, which any number of recordings may have.
    /// </summary>
    public async Task<bool> RecordingNameTakenAsync(Guid projectId, string? name, Guid? except = null, CancellationToken cancellationToken = default)
    {
        if (RecordingNames.Normalize(name) is not { } trimmed)
        {
            return false;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM recordings WHERE project_id = $1 AND name = $2 AND recording_id IS DISTINCT FROM $3)",
            connection);
        command.Parameters.AddWithValue(projectId);
        command.Parameters.AddWithValue(trimmed);
        command.Parameters.Add(new NpgsqlParameter { Value = except is { } id ? id : DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Names a recording, or removes its name when the name is empty, so it
    /// is known by its session key again. A name another recording in its
    /// project has is refused. Its folder and session key are unchanged.
    /// </summary>
    public async Task<RecordingRename> RenameRecordingAsync(Guid recordingId, string? name, CancellationToken cancellationToken = default)
    {
        var trimmed = RecordingNames.Normalize(name);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "UPDATE recordings r SET name = $2 WHERE r.recording_id = $1 " +
            "AND ($2::text IS NULL OR NOT EXISTS (SELECT 1 FROM recordings o WHERE o.project_id = r.project_id AND o.name = $2 AND o.recording_id <> $1)) " +
            "RETURNING 1",
            connection);
        command.Parameters.AddWithValue(recordingId);
        command.Parameters.Add(new NpgsqlParameter { Value = trimmed is null ? DBNull.Value : trimmed, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return RecordingRename.Renamed;
        }

        await using var exists = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM recordings WHERE recording_id = $1)", connection);
        exists.Parameters.AddWithValue(recordingId);
        return (bool)(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! ? RecordingRename.NameTaken : RecordingRename.Missing;
    }

    /// <summary>A recording's name, or null when it has none or is not in the catalog.</summary>
    public async Task<string?> RecordingNameAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT name FROM recordings WHERE recording_id = $1", connection);
        command.Parameters.AddWithValue(recordingId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    /// <summary>The project a recording is in, by its session key, or null when it is not in the catalog.</summary>
    public async Task<Guid?> ProjectOfAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT project_id FROM recordings WHERE session_key = $1", connection);
        command.Parameters.AddWithValue(sessionKey);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as Guid?;
    }

    private static string ProjectName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }

    /// <summary>
    /// Adds a recording in the recording state.
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

        // The name is written only when there is one, so that a catalog
        // older than migration 0043 can still be written to by its tests.
        var name = RecordingNames.Normalize(definition.Name);
        Guid recordingId;
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO recordings (project_id, session_key, recording_status_id, " +
            "started_utc, clock_frequency, clock_origin_timestamp, os_description, " +
            "framework_description, process_architecture, capture_keyboard_and_mouse, " +
            "capture_ui_automation, capture_foreground_window, capture_desktop_frames, " +
            "frames_per_second, capture_microphone, capture_system_audio" + (name is null ? "" : ", name") + ") " +
            "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16" + (name is null ? "" : ", $17") + ") " +
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
            if (name is not null)
            {
                insert.Parameters.AddWithValue(name);
            }

            try
            {
                recordingId = (Guid)(await insert.ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false))!;
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == "recordings_project_name")
            {
                throw new InvalidOperationException($"A recording named {name} is already in this project. Give this recording another name, or none.", exception);
            }
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

    /// <summary>
    /// Adds each collector whose events a recording file holds, with the
    /// channels it wrote, when it is not already registered, as the earlier
    /// writer did when it first saw one of a collector's events.
    /// </summary>
    public async Task AddEventCollectorsAsync(
        Guid recordingId,
        IEnumerable<RecordingFiles.EventCollector> collectors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectors);
        foreach (var collector in collectors)
        {
            var kind = await _keys.GetCollectorKindAsync(
                collector.CollectorType,
                collector.ProducerVersion,
                collector.CaptureMethod,
                cancellationToken).ConfigureAwait(false);
            var (recordingCollectorId, _) = await ReferenceKeys.GetRecordingCollectorAsync(
                dataSource,
                recordingId,
                collector.InstanceId,
                kind,
                cancellationToken).ConfigureAwait(false);
            foreach (var channel in collector.Channels)
            {
                var channelId = await _keys.GetAsync(ReferenceKeys.Channels, channel, cancellationToken)
                    .ConfigureAwait(false);
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var command = new NpgsqlCommand(
                    "INSERT INTO recording_collector_channels (recording_collector_id, channel_id) " +
                    "VALUES ($1, $2) ON CONFLICT DO NOTHING",
                    connection);
                command.Parameters.AddWithValue(recordingCollectorId);
                command.Parameters.AddWithValue((short)channelId);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Records where a recording's events are stored: its recording file,
    /// as a path relative to the session folder, not yet finished.
    /// </summary>
    public async Task AddRecordingFileAsync(
        Guid recordingId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "INSERT INTO recording_files (recording_id, path, format) VALUES ($1, $2, 'mcap')",
            connection);
        command.Parameters.AddWithValue(recordingId);
        command.Parameters.AddWithValue(relativePath);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores the index of a recording file's chunks and what the file
    /// holds, replacing any index stored before, in one transaction.
    /// </summary>
    public async Task StoreRecordingFileIndexAsync(
        Guid recordingId,
        bool finished,
        long byteLength,
        long messageCount,
        IReadOnlyList<RecordingFiles.RecordingFileChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var clear = new NpgsqlCommand(
            "DELETE FROM recording_file_chunks WHERE recording_id = $1",
            connection,
            transaction))
        {
            clear.Parameters.AddWithValue(recordingId);
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var importer = await connection.BeginBinaryImportAsync(
            "COPY recording_file_chunks (recording_id, chunk_ordinal, stream, message_start_time, " +
            "message_end_time, message_count, chunk_offset, chunk_length, compressed_size, " +
            "uncompressed_size) FROM STDIN (FORMAT BINARY)",
            cancellationToken).ConfigureAwait(false))
        {
            foreach (var chunk in chunks)
            {
                await importer.StartRowAsync(cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(recordingId, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(chunk.Ordinal, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(chunk.Stream, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(chunk.StartTime, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(chunk.EndTime, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(chunk.MessageCount ?? 0, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(chunk.Offset, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(chunk.Length, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(chunk.CompressedSize, cancellationToken).ConfigureAwait(false);
                await importer.WriteAsync(chunk.UncompressedSize, cancellationToken).ConfigureAwait(false);
            }

            await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var update = new NpgsqlCommand(
            "UPDATE recording_files SET finished = $2, byte_length = $3, message_count = $4, " +
            "chunk_count = $5, message_start_time = $6, message_end_time = $7 WHERE recording_id = $1",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue(recordingId);
            update.Parameters.AddWithValue(finished);
            update.Parameters.AddWithValue(byteLength);
            update.Parameters.AddWithValue(messageCount);
            update.Parameters.AddWithValue(chunks.Count);
            update.Parameters.AddWithValue(
                chunks.Count == 0 ? DBNull.Value : chunks.Min(chunk => chunk.StartTime));
            update.Parameters.AddWithValue(
                chunks.Count == 0 ? DBNull.Value : chunks.Max(chunk => chunk.EndTime));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException($"Recording {recordingId} has no recording file.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The recording file of a recording, relative to its session folder, or null.</summary>
    public async Task<string?> GetRecordingFileAsync(
        Guid recordingId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT path FROM recording_files WHERE recording_id = $1",
            connection);
        command.Parameters.AddWithValue(recordingId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
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
    /// Checks every reference listed in recording_references for one
    /// recording, and returns a description of each reference with rows
    /// that refer to a missing row, or an empty list when every row's
    /// references are present. The writer's tables have no foreign keys
    /// between them, which PostgreSQL would check one inserted row at a
    /// time; each reference is checked here with one set-based query, with
    /// up to half the processors, at most eight, checking at once. A
    /// reference with a null column is not checked, as a foreign key would
    /// not check it. The statistics of the tables the queries read are
    /// gathered first, since those of a recording just written describe
    /// none of its rows. When <paramref name="timings"/> is given, the time of
    /// each query is added to it under the referring table's name.
    /// </summary>
    public async Task<IReadOnlyList<string>> CheckReferencesAsync(
        Guid recordingId,
        WriterTimings? timings = null,
        CancellationToken cancellationToken = default)
    {
        var references = new List<(string Table, string[] Columns, string Referenced, string[] ReferencedColumns)>();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        {
            // The upgrade tests write recordings into the schema an earlier
            // release left, which checked references with foreign keys.
            await using (var present = new NpgsqlCommand(
                "SELECT to_regclass('recording_references') IS NOT NULL",
                connection))
            {
                if (!(bool)(await present.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!)
                {
                    return [];
                }
            }

            await using var command = new NpgsqlCommand(
                "SELECT table_name, columns, referenced_table, referenced_columns FROM recording_references " +
                "ORDER BY table_name, columns, referenced_table",
                connection);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    references.Add((
                        reader.GetString(0),
                        reader.GetFieldValue<string[]>(1),
                        reader.GetString(2),
                        reader.GetFieldValue<string[]>(3)));
                }
            }

            // Most of the tables have no rows in a given recording, and their
            // references need no query; one statement finds the tables that
            // do.
            var tables = references.Select(reference => reference.Table).Distinct(StringComparer.Ordinal).ToList();
            if (tables.Count > 0)
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                await using var occupied = new NpgsqlCommand(
                    string.Join(
                        " UNION ALL ",
                        tables.Select(table =>
                            $"SELECT '{table}'::text WHERE EXISTS (SELECT 1 FROM {Identifier(table)} WHERE recording_id = $1)")),
                    connection)
                {
                    CommandTimeout = 0
                };
                occupied.Parameters.AddWithValue(recordingId);
                var withRows = new HashSet<string>(StringComparer.Ordinal);
                await using (var reader = await occupied.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        withRows.Add(reader.GetString(0));
                    }
                }

                references.RemoveAll(reference => !withRows.Contains(reference.Table));
                timings?.Since("check-references.tables-with-rows", started, withRows.Count);
            }
        }

        // The recording was written moments ago, so the planner's statistics
        // describe none of its rows and estimate about one row for its
        // recording_id in every table. From that estimate the planner chose
        // plans suited to one row: nested loops that compared every row with
        // every row, or, with nested loops disabled, merge joins that read
        // every referenced row of the recording to check a few referring
        // rows. Gathering statistics for the tables the queries read first
        // lets it choose a plan for the rows there are. The measurements
        // behind this are in docs/architecture/session-database.md.
        var analyzed = references
            .Select(reference => reference.Table)
            .Concat(references.Select(reference => reference.Referenced))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var analyzing = System.Diagnostics.Stopwatch.GetTimestamp();
        await Parallel.ForEachAsync(
            analyzed,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
                CancellationToken = cancellationToken
            },
            async (table, token) =>
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                await using var connection = await dataSource.OpenConnectionAsync(token).ConfigureAwait(false);
                await using var analyze = new NpgsqlCommand($"ANALYZE {Identifier(table)}", connection)
                {
                    CommandTimeout = 0
                };
                await analyze.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                timings?.Since("check-references-analyze:" + table, started);
            }).ConfigureAwait(false);
        timings?.Since("check-references.analyze", analyzing, analyzed.Count);

        if (timings is not null)
        {
            await NoteServerActivityAsync(timings, "check-references-activity:start", cancellationToken).ConfigureAwait(false);
            await NoteStatisticsAsync(
                timings,
                [.. references.Select(reference => reference.Table).Concat(references.Select(reference => reference.Referenced)).Distinct(StringComparer.Ordinal)],
                cancellationToken).ConfigureAwait(false);
        }

        var missing = new long[references.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, references.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                var (table, columns, referenced, referencedColumns) = references[index];
                var present = string.Join(" AND ", columns.Select(column => $"c.{Identifier(column)} IS NOT NULL"));
                var matches = string.Join(
                    " AND ",
                    columns.Select((column, position) =>
                        $"p.{Identifier(referencedColumns[position])} = c.{Identifier(column)}"));
                await using var connection = await dataSource.OpenConnectionAsync(token).ConfigureAwait(false);
                var explaining = timings is not null && await LoadAutoExplainAsync(connection, token).ConfigureAwait(false);
                var plans = new List<string>();
                NoticeEventHandler? collect = explaining
                    ? (_, notice) =>
                    {
                        lock (plans)
                        {
                            plans.Add(notice.Notice.MessageText);
                        }
                    }
                    : null;
                if (collect is not null)
                {
                    connection.Notice += collect;
                }

                await using var transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
                if (explaining)
                {
                    // A query that takes longer than the threshold sends its
                    // plan, with the actual rows and buffers of each step and
                    // the time spent reading them, to this connection as a
                    // notice, and not to the server log. Steps are not timed
                    // one by one, which PostgreSQL documents as costly for
                    // every statement, noted or not.
                    await using var explain = new NpgsqlCommand(
                        "SET LOCAL auto_explain.log_min_duration = " + ExplainedCheckMilliseconds + "; " +
                        "SET LOCAL auto_explain.log_analyze = on; " +
                        "SET LOCAL auto_explain.log_timing = off; " +
                        "SET LOCAL auto_explain.log_buffers = on; " +
                        "SET LOCAL auto_explain.log_level = notice; " +
                        "SET LOCAL track_io_timing = on",
                        connection,
                        transaction);
                    await explain.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await using var command = new NpgsqlCommand(
                    $"SELECT count(*) FROM {Identifier(table)} c WHERE c.recording_id = $1 AND {present} " +
                    $"AND NOT EXISTS (SELECT 1 FROM {Identifier(referenced)} p WHERE {matches})",
                    connection,
                    transaction)
                {
                    CommandTimeout = 0
                };
                command.Parameters.AddWithValue(recordingId);
                missing[index] = (long)(await command.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                await transaction.CommitAsync(token).ConfigureAwait(false);
                timings?.Since("check-references:" + table, started);
                if (collect is not null)
                {
                    connection.Notice -= collect;
                    lock (plans)
                    {
                        foreach (var text in plans)
                        {
                            timings!.AddNote(
                                $"check-references-plan:{table}({string.Join(",", columns)})->{referenced}",
                                text);
                        }
                    }
                }
            }).ConfigureAwait(false);

        var failures = new List<string>();
        for (var index = 0; index < references.Count; index++)
        {
            if (missing[index] > 0)
            {
                var (table, columns, referenced, referencedColumns) = references[index];
                failures.Add(
                    $"{missing[index]} rows of {table} ({string.Join(", ", columns)}) refer to rows " +
                    $"missing from {referenced} ({string.Join(", ", referencedColumns)}).");
            }
        }

        return failures;
    }

    /// <summary>
    /// The time in milliseconds a reference check query must take for its
    /// plan to be noted in the writer timings. Tests lower it.
    /// </summary>
    internal int ExplainedCheckMilliseconds { get; init; } = 250;

    // Loads the auto_explain module PostgreSQL ships with into this
    // connection's server process, so a slow check query's plan can be
    // noted. A server without the module, or a user who may not load it,
    // checks the references without noting plans.
    private static async Task<bool> LoadAutoExplainAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var load = new NpgsqlCommand("LOAD 'auto_explain'", connection);
            await load.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (PostgresException)
        {
            return false;
        }
    }

    // Notes what else the server was doing: each other server process that
    // is not idle, with its kind, state, wait, and the start of its query.
    // Autovacuum workers show here with the table they are processing.
    private async Task NoteServerActivityAsync(WriterTimings timings, string name, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT backend_type, coalesce(state, ''), coalesce(wait_event_type || ':' || wait_event, ''), " +
            "coalesce(round(extract(epoch FROM clock_timestamp() - query_start) * 1000)::bigint, -1), " +
            "left(coalesce(query, ''), 200) FROM pg_stat_activity " +
            "WHERE pid <> pg_backend_pid() AND coalesce(state, 'active') <> 'idle' " +
            "AND backend_type IN ('client backend', 'autovacuum worker', 'parallel worker') ORDER BY backend_type, pid",
            connection);
        var lines = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lines.Add(
                    $"{reader.GetString(0)} | {reader.GetString(1)} | {reader.GetString(2)} | " +
                    $"{reader.GetInt64(3)} ms | {reader.GetString(4)}");
            }
        }

        timings.AddNote(name, lines.Count == 0 ? "no other active server process" : string.Join("\n", lines));
    }

    // Notes, for each table a check query reads, how many rows changed since
    // the planner's statistics were last gathered, and when that was.
    private async Task NoteStatisticsAsync(WriterTimings timings, string[] tables, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT relname, n_live_tup, n_mod_since_analyze, " +
            "coalesce(round(extract(epoch FROM clock_timestamp() - greatest(last_analyze, last_autoanalyze)))::bigint, -1) " +
            "FROM pg_stat_user_tables WHERE relname = ANY($1) ORDER BY n_mod_since_analyze DESC",
            connection);
        command.Parameters.AddWithValue(tables);
        var lines = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lines.Add(
                    $"{reader.GetString(0)} | live {reader.GetInt64(1)} | changed since analyze {reader.GetInt64(2)} | " +
                    $"analyzed {reader.GetInt64(3)} s ago");
            }
        }

        timings.AddNote("check-references-statistics:start", string.Join("\n", lines));
    }

    // Table and column names come from the database, not from user input,
    // and are checked to be plain names before they are quoted.
    private static string Identifier(string name) =>
        name.Length > 0 && name.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
            ? "\"" + name + "\""
            : throw new InvalidOperationException($"{name} is not a table or column name the recorder uses.");

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
    /// Removes a recording: its rows in every per-recording table, referring
    /// tables first, and then its recording row, in one transaction.
    /// </summary>
    public async Task DeleteRecordingAsync(Guid recordingId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var tables = await RecordingTablesAsync(connection, transaction, cancellationToken)
            .ConfigureAwait(false);

        // Every row of the recording is removed from every per-recording
        // table, so no row can be left referring to a removed one. The
        // foreign key checks are skipped while those rows are removed:
        // checked, each removed row of a referenced table would look for
        // referring rows through a column no index leads with, and removal
        // would grow with the square of the recording's size. The app's
        // database user is the server's superuser, which may set this.
        await ExecuteAsync(connection, transaction, "SET LOCAL session_replication_role = replica", cancellationToken)
            .ConfigureAwait(false);
        foreach (var table in tables.Reverse())
        {
            // The name comes from the database, not from user input.
            await using var rows = new NpgsqlCommand(
                $"DELETE FROM {table} WHERE recording_id = $1",
                connection,
                transaction);
            rows.Parameters.AddWithValue(recordingId);
            await rows.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // The recording row is removed with the checks and cascades in force,
        // which remove its collectors, clock mappings, rejections, and
        // omissions.
        await ExecuteAsync(connection, transaction, "SET LOCAL session_replication_role = origin", cancellationToken)
            .ConfigureAwait(false);

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

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> RecordingTablesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM recording_tables ORDER BY table_order",
            connection,
            transaction);
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}

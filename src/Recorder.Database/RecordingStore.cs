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
    /// Checks every reference listed in recording_references for one
    /// recording, and returns a description of each reference with rows
    /// that refer to a missing row, or an empty list when every row's
    /// references are present. The writer's tables have no foreign keys
    /// between them, which PostgreSQL would check one inserted row at a
    /// time; each reference is checked here with one set-based query, with
    /// up to half the processors, at most eight, checking at once. A
    /// reference with a null column is not checked, as a foreign key would
    /// not check it. Each query runs with nested loop joins disabled, since
    /// the statistics of a recording just written describe none of its rows. When <paramref name="timings"/> is given, the time of
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
                // The recording was written moments ago, so the planner's
                // statistics do not yet include it and estimate about one
                // row for its recording_id. From that estimate it chooses a
                // nested loop that reads every referenced row of the
                // recording once per referring row. Without nested loops it
                // chooses a hash or merge anti join, which reads each side
                // once. SET LOCAL ends with the transaction, so the pooled
                // connection keeps the default. The measurements behind this
                // are in docs/architecture/session-database.md.
                await using (var plan = new NpgsqlCommand("SET LOCAL enable_nestloop = off", connection, transaction))
                {
                    await plan.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

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

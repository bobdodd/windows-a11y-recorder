using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace Recorder.Analysis;

/// <summary>Task states, as the catalog stores them.</summary>
public static class AnalysisTaskStates
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

/// <summary>Attempt outcomes, as the catalog stores them.</summary>
public static class AttemptOutcomes
{
    public const string Running = "running";
    public const string Accepted = "accepted";
    public const string Failed = "failed";
    public const string StoppedForRecording = "stopped-for-recording";
    public const string Cancelled = "cancelled";
}

/// <summary>A queued or running task, as the worker reads it.</summary>
public sealed record AnalysisTask(
    Guid TaskId,
    Guid RecordingId,
    string Kind,
    int KindVersion,
    long? ScopeStartNanoseconds,
    long? ScopeEndNanoseconds,
    Guid? AfterTaskId,
    string? AfterTaskState,
    IReadOnlyDictionary<string, string> StageProfiles,
    string State,
    bool CancelRequested,
    DateTime QueuedUtc)
{
    /// <summary>The session folder the player gave when queuing the task, or null.</summary>
    public string? SessionDirectory { get; init; }

    /// <summary>
    /// The task this one runs again, whose accepted steps with unchanged
    /// requests are reused, or null when it runs every step itself.
    /// </summary>
    public Guid? RerunOfTaskId { get; init; }
}

/// <summary>A description of the participant's capabilities, and when it was written.</summary>
public sealed record CapabilityDescription(Guid CapabilityId, string Description, DateTime WrittenUtc);

/// <summary>A task as the player lists it.</summary>
public sealed record AnalysisTaskSummary(
    Guid TaskId,
    Guid RecordingId,
    string Kind,
    string State,
    IReadOnlyDictionary<string, string> StageProfiles,
    DateTime QueuedUtc,
    DateTime? FinishedUtc,
    string? Failure,
    DateTime? ReadUtc);

/// <summary>What an attempt was made with, recorded when it starts.</summary>
public sealed record AttemptStart(
    Guid TaskId,
    string StepKey,
    int AttemptNumber,
    ModelProfile Profile,
    string? ModelSha256,
    string ModelSha256Basis,
    string? ServerBuild,
    JsonObject Parameters,
    string PromptTemplate,
    int PromptTemplateVersion,
    string InputSha256,
    IReadOnlyList<string> InputReferences);

/// <summary>How an attempt ended.</summary>
public sealed record AttemptEnd(
    string Outcome,
    string? Failure,
    ModelResponse? Response,
    JsonElement? Output,
    IReadOnlyList<Claim> Claims);

/// <summary>An attempt as a report describes it.</summary>
public sealed record AttemptRecord(
    string StepKey,
    int AttemptNumber,
    string Outcome,
    string? Failure,
    string ProfileName,
    string Model,
    string? ModelFileName,
    string? ModelQuantization,
    string? ModelSha256,
    string ModelSha256Basis,
    string? ServerBuild,
    string Parameters,
    string PromptTemplate,
    int PromptTemplateVersion,
    int? PromptTokens,
    int? CompletionTokens,
    double? PromptMilliseconds,
    double? CompletionMilliseconds)
{
    /// <summary>For a reused attempt, the task whose attempt the model wrote; otherwise null.</summary>
    public Guid? ReusedFromTaskId { get; init; }
}

/// <summary>
/// What an attempt was made with that decides whether its answer can be
/// reused: the request's fingerprint, the model file's hash, the server's
/// build, and the sampling settings with the server's defaults.
/// </summary>
public sealed record StepIdentity(string InputSha256, string ModelSha256, string ServerBuild, JsonObject Parameters);

/// <summary>A failed attempt at a step, for a rerun to say why it was not asked again.</summary>
public sealed record FailedStep(Guid AttemptId, string? Failure);

/// <summary>A step's accepted attempt.</summary>
public sealed record AcceptedStep(Guid AttemptId, string InputSha256, JsonElement Output, IReadOnlyList<Claim> Claims);

/// <summary>
/// The analysis tables in the catalog. Every use opens its own connection.
/// If the database server has stopped, as when the player closes and stops
/// the server it started, the catalog asks for the server to be started
/// again and repeats the use once.
/// </summary>
public sealed class AnalysisCatalog(Func<CancellationToken, Task<NpgsqlDataSource>> connect)
{
    private NpgsqlDataSource? _dataSource;

    public static AnalysisCatalog For(NpgsqlDataSource dataSource) =>
        new(_ => Task.FromResult(dataSource));

    private async Task<T> UseAsync<T>(Func<NpgsqlConnection, Task<T>> use, CancellationToken cancellationToken)
    {
        for (var tried = 0; ; tried++)
        {
            _dataSource ??= await connect(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                return await use(connection).ConfigureAwait(false);
            }
            catch (NpgsqlException exception) when (exception.IsTransient && tried == 0 && !cancellationToken.IsCancellationRequested)
            {
                _dataSource = null;
                NpgsqlConnection.ClearAllPools();
            }
        }
    }

    private Task UseAsync(Func<NpgsqlConnection, Task> use, CancellationToken cancellationToken) =>
        UseAsync<bool>(async connection =>
        {
            await use(connection).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    public Task<Guid> QueueTaskAsync(
        Guid recordingId,
        string kind,
        int kindVersion,
        IReadOnlyDictionary<string, string> stageProfiles,
        Guid? afterTaskId = null,
        long? scopeStartNanoseconds = null,
        long? scopeEndNanoseconds = null,
        string? sessionDirectory = null,
        Guid? rerunOfTaskId = null,
        CancellationToken cancellationToken = default) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO analysis_tasks (recording_id, kind, kind_version, stage_profiles, after_task_id, " +
                "scope_start_nanoseconds, scope_end_nanoseconds, session_directory, rerun_of_task_id) " +
                "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9) RETURNING task_id",
                connection);
            command.Parameters.AddWithValue(recordingId);
            command.Parameters.AddWithValue(kind);
            command.Parameters.AddWithValue(kindVersion);
            command.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(stageProfiles), NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.AddWithValue((object?)afterTaskId ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)scopeStartNanoseconds ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)scopeEndNanoseconds ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)sessionDirectory ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)rerunOfTaskId ?? DBNull.Value);
            return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }, cancellationToken);

    /// <summary>Whether any recording is in progress.</summary>
    public Task<bool> RecordingInProgressAsync(CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM recordings WHERE recording_status_id = 1)",
                connection);
            return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }, cancellationToken);

    /// <summary>The queued and running tasks, oldest first.</summary>
    public Task<IReadOnlyList<AnalysisTask>> OpenTasksAsync(CancellationToken cancellationToken) =>
        ReadTasksAsync("WHERE t.state IN ('queued', 'running') ORDER BY t.queued_utc, t.task_id", null, cancellationToken);

    /// <summary>One task, in whatever state, or null where there is none.</summary>
    public async Task<AnalysisTask?> TaskAsync(Guid taskId, CancellationToken cancellationToken) =>
        (await ReadTasksAsync("WHERE t.task_id = $1", taskId, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    private Task<IReadOnlyList<AnalysisTask>> ReadTasksAsync(string where, Guid? taskId, CancellationToken cancellationToken) =>
        UseAsync<IReadOnlyList<AnalysisTask>>(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT t.task_id, t.recording_id, t.kind, t.kind_version, t.scope_start_nanoseconds, " +
                "t.scope_end_nanoseconds, t.after_task_id, a.state, t.stage_profiles::text, t.state, " +
                "t.cancel_requested, t.queued_utc, t.session_directory, t.rerun_of_task_id FROM analysis_tasks t " +
                "LEFT JOIN analysis_tasks a ON a.task_id = t.after_task_id " + where,
                connection);
            if (taskId is { } id)
            {
                command.Parameters.AddWithValue(id);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var tasks = new List<AnalysisTask>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(new AnalysisTask(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    Profiles(reader.GetString(8)),
                    reader.GetString(9),
                    reader.GetBoolean(10),
                    reader.GetDateTime(11))
                {
                    SessionDirectory = reader.IsDBNull(12) ? null : reader.GetString(12),
                    RerunOfTaskId = reader.IsDBNull(13) ? null : reader.GetGuid(13)
                });
            }

            return tasks;
        }, cancellationToken);

    /// <summary>A recording's tasks, newest first, for the player.</summary>
    public Task<IReadOnlyList<AnalysisTaskSummary>> TasksAsync(Guid recordingId, CancellationToken cancellationToken) =>
        UseAsync<IReadOnlyList<AnalysisTaskSummary>>(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT task_id, recording_id, kind, state, stage_profiles::text, queued_utc, finished_utc, failure, read_utc " +
                "FROM analysis_tasks WHERE recording_id = $1 ORDER BY queued_utc DESC, task_id DESC",
                connection);
            command.Parameters.AddWithValue(recordingId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var tasks = new List<AnalysisTaskSummary>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(new AnalysisTaskSummary(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    Profiles(reader.GetString(4)),
                    reader.GetDateTime(5),
                    reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetDateTime(8)));
            }

            return tasks;
        }, cancellationToken);

    public Task MarkRunningAsync(Guid taskId, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE analysis_tasks SET state = 'running', started_utc = coalesce(started_utc, now()) " +
            "WHERE task_id = $1 AND state IN ('queued', 'running')",
            cancellationToken,
            taskId);

    public Task FinishTaskAsync(Guid taskId, string state, string? failure, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE analysis_tasks SET state = $2, failure = $3, finished_utc = now() WHERE task_id = $1",
            cancellationToken,
            taskId,
            state,
            (object?)failure ?? DBNull.Value);

    /// <summary>
    /// Cancels a task: at once if it is queued, or by asking the worker if
    /// it is running.
    /// </summary>
    public Task RequestCancelAsync(Guid taskId, CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "UPDATE analysis_tasks SET " +
                "state = CASE WHEN state = 'queued' THEN 'cancelled' ELSE state END, " +
                "finished_utc = CASE WHEN state = 'queued' THEN now() ELSE finished_utc END, " +
                "cancel_requested = (state = 'running') " +
                "WHERE task_id = $1 AND state IN ('queued', 'running')",
                connection);
            command.Parameters.AddWithValue(taskId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task<bool> CancelRequestedAsync(Guid taskId, CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT cancel_requested FROM analysis_tasks WHERE task_id = $1",
                connection);
            command.Parameters.AddWithValue(taskId);
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
        }, cancellationToken);

    public Task MarkReadAsync(Guid taskId, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE analysis_tasks SET read_utc = coalesce(read_utc, now()) WHERE task_id = $1",
            cancellationToken,
            taskId);

    /// <summary>
    /// Ends the attempts a worker left running when it stopped, so each is
    /// recorded as failed and its step runs again.
    /// </summary>
    public Task<int> CloseAbandonedAttemptsAsync(CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "UPDATE analysis_attempts SET outcome = 'failed', finished_utc = now(), " +
                "failure = 'The worker stopped during the attempt.' WHERE outcome = 'running'",
                connection);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>
    /// A step's accepted attempt, with its statements in the order the
    /// model wrote them: by the step's sections in the order given, then by
    /// paragraph and statement. A later step built from them is then given
    /// them in the same order whether the step ran or was reused, so that
    /// its request is the same (owner, 2026-10-14; analysis-runner.md,
    /// "Reusing steps when run again").
    /// </summary>
    public Task<AcceptedStep?> AcceptedStepAsync(Guid taskId, string stepKey, IReadOnlyList<string> sections, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sections);
        return UseAsync(async connection =>
        {
            Guid attemptId;
            string inputSha;
            JsonElement output;
            await using (var command = new NpgsqlCommand(
                "SELECT attempt_id, input_sha256, output::text FROM analysis_attempts " +
                "WHERE task_id = $1 AND step_key = $2 AND outcome = 'accepted'",
                connection))
            {
                command.Parameters.AddWithValue(taskId);
                command.Parameters.AddWithValue(stepKey);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                attemptId = reader.GetGuid(0);
                inputSha = reader.GetString(1);
                using var document = JsonDocument.Parse(reader.GetString(2));
                output = document.RootElement.Clone();
            }

            var claims = new List<Claim>();
            await using (var command = new NpgsqlCommand(
                "SELECT section, paragraph, ordinal, claim_kind, text, evidence FROM analysis_claims " +
                "WHERE attempt_id = $1 ORDER BY array_position($2::text[], section), section, paragraph, ordinal",
                connection))
            {
                command.Parameters.AddWithValue(attemptId);
                command.Parameters.AddWithValue(sections.ToArray());
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    claims.Add(new Claim(
                        reader.GetString(0),
                        reader.GetInt32(1),
                        reader.GetInt32(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetFieldValue<string[]>(5)));
                }
            }

            return (AcceptedStep?)new AcceptedStep(attemptId, inputSha, output, claims);
        }, cancellationToken);
    }

    /// <summary>
    /// The evidence references given to a step's accepted attempt, in order,
    /// or null when the step has none accepted. A later task that builds on
    /// the step compares them with its own collation, so that it does not
    /// build on output written from other facts.
    /// </summary>
    public Task<IReadOnlyList<string>?> AcceptedInputReferencesAsync(Guid taskId, string stepKey, CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT input_references FROM analysis_attempts WHERE task_id = $1 AND step_key = $2 AND outcome = 'accepted'",
                connection);
            command.Parameters.AddWithValue(taskId);
            command.Parameters.AddWithValue(stepKey);
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is string[] references ? (IReadOnlyList<string>?)references : null;
        }, cancellationToken);

    /// <summary>
    /// The accepted attempt at a step in an earlier task that was made with
    /// the same request, model file, server build, and sampling settings, so
    /// that a task run again can reuse its answer; or null where there is
    /// none.
    /// </summary>
    public Task<Guid?> ReusableAttemptAsync(Guid taskId, string stepKey, StepIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT attempt_id FROM analysis_attempts WHERE task_id = $1 AND step_key = $2 AND outcome = 'accepted' " +
                "AND input_sha256 = $3 AND model_sha256 = $4 AND server_build = $5 AND parameters = $6",
                connection);
            command.Parameters.AddWithValue(taskId);
            command.Parameters.AddWithValue(stepKey);
            command.Parameters.AddWithValue(identity.InputSha256);
            command.Parameters.AddWithValue(identity.ModelSha256);
            command.Parameters.AddWithValue(identity.ServerBuild);
            command.Parameters.Add(new NpgsqlParameter { Value = identity.Parameters.ToJsonString(), NpgsqlDbType = NpgsqlDbType.Jsonb });
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is Guid id ? (Guid?)id : null;
        }, cancellationToken);
    }

    /// <summary>
    /// The latest failed attempt at a step in an earlier task that was made
    /// with the same request, model file, server build, and sampling
    /// settings, whose output the checks refused: not attempts set aside
    /// because their input changed, ended by the worker stopping, or failed
    /// by the server with no output. Null where there is none.
    /// </summary>
    public Task<FailedStep?> UnchangedFailureAsync(Guid taskId, string stepKey, StepIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT attempt_id, failure FROM analysis_attempts WHERE task_id = $1 AND step_key = $2 AND outcome = 'failed' " +
                "AND input_sha256 = $3 AND model_sha256 = $4 AND server_build = $5 AND parameters = $6 " +
                "AND failure IS DISTINCT FROM 'Its input changed, so the step ran again.' " +
                "AND failure IS DISTINCT FROM 'The worker stopped during the attempt.' " +
                "AND raw_output IS NOT NULL " +
                "ORDER BY attempt_number DESC LIMIT 1",
                connection);
            command.Parameters.AddWithValue(taskId);
            command.Parameters.AddWithValue(stepKey);
            command.Parameters.AddWithValue(identity.InputSha256);
            command.Parameters.AddWithValue(identity.ModelSha256);
            command.Parameters.AddWithValue(identity.ServerBuild);
            command.Parameters.Add(new NpgsqlParameter { Value = identity.Parameters.ToJsonString(), NpgsqlDbType = NpgsqlDbType.Jsonb });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? new FailedStep(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1))
                : null;
        }, cancellationToken);
    }

    /// <summary>
    /// Copies an accepted attempt into another task as that task's accepted
    /// attempt at the step, with its answer, token counts, times, and
    /// statements, pointing to the attempt the model wrote. Returns the
    /// copy's id.
    /// </summary>
    public Task<Guid> ReuseAttemptAsync(Guid sourceAttemptId, Guid taskId, int attemptNumber, CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            Guid copy;
            await using (var command = new NpgsqlCommand(
                "INSERT INTO analysis_attempts (task_id, step_key, attempt_number, started_utc, finished_utc, outcome, failure, " +
                "profile_name, profile, endpoint, model, model_file, quantization, model_sha256, model_sha256_basis, server_build, " +
                "parameters, prompt_template, prompt_template_version, input_sha256, input_references, prompt_tokens, " +
                "completion_tokens, prompt_milliseconds, completion_milliseconds, finish_reason, raw_output, output, reused_from_attempt_id) " +
                "SELECT $2, step_key, $3, now(), now(), 'accepted', NULL, " +
                "profile_name, profile, endpoint, model, model_file, quantization, model_sha256, model_sha256_basis, server_build, " +
                "parameters, prompt_template, prompt_template_version, input_sha256, input_references, prompt_tokens, " +
                "completion_tokens, prompt_milliseconds, completion_milliseconds, finish_reason, raw_output, output, " +
                "coalesce(reused_from_attempt_id, attempt_id) FROM analysis_attempts WHERE attempt_id = $1 AND outcome = 'accepted' " +
                "RETURNING attempt_id",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(sourceAttemptId);
                command.Parameters.AddWithValue(taskId);
                command.Parameters.AddWithValue(attemptNumber);
                copy = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as Guid? ??
                    throw new InvalidOperationException($"Attempt {sourceAttemptId} is not an accepted attempt.");
            }

            await using (var command = new NpgsqlCommand(
                "INSERT INTO analysis_claims (attempt_id, section, paragraph, ordinal, claim_kind, text, evidence) " +
                "SELECT $2, section, paragraph, ordinal, claim_kind, text, evidence FROM analysis_claims WHERE attempt_id = $1",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(sourceAttemptId);
                command.Parameters.AddWithValue(copy);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return copy;
        }, cancellationToken);

    /// <summary>
    /// Sets aside a step's accepted attempt whose input has changed, so the
    /// step runs again. The attempt stays, recorded as failed.
    /// </summary>
    public Task SupersedeAcceptedAsync(Guid attemptId, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE analysis_attempts SET outcome = 'failed', " +
            "failure = 'Its input changed, so the step ran again.' WHERE attempt_id = $1",
            cancellationToken,
            attemptId);

    public Task<int> AttemptCountAsync(Guid taskId, string stepKey, CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT coalesce(max(attempt_number), 0) FROM analysis_attempts WHERE task_id = $1 AND step_key = $2",
                connection);
            command.Parameters.AddWithValue(taskId);
            command.Parameters.AddWithValue(stepKey);
            return (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }, cancellationToken);

    /// <summary>
    /// The number of failed attempts at a step. Attempts stopped for a
    /// recording or cancelled, and accepted attempts set aside because their
    /// input changed, do not count against the limit.
    /// </summary>
    public Task<int> FailedAttemptCountAsync(Guid taskId, string stepKey, CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM analysis_attempts WHERE task_id = $1 AND step_key = $2 AND outcome = 'failed' " +
                "AND failure IS DISTINCT FROM 'Its input changed, so the step ran again.'",
                connection);
            command.Parameters.AddWithValue(taskId);
            command.Parameters.AddWithValue(stepKey);
            return (int)(long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }, cancellationToken);

    public Task<Guid> BeginAttemptAsync(AttemptStart start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        return UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO analysis_attempts (task_id, step_key, attempt_number, started_utc, outcome, " +
                "profile_name, profile, endpoint, model, model_file, quantization, model_sha256, model_sha256_basis, " +
                "server_build, parameters, prompt_template, prompt_template_version, input_sha256, input_references) " +
                "VALUES ($1, $2, $3, now(), 'running', $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17) " +
                "RETURNING attempt_id",
                connection);
            command.Parameters.AddWithValue(start.TaskId);
            command.Parameters.AddWithValue(start.StepKey);
            command.Parameters.AddWithValue(start.AttemptNumber);
            command.Parameters.AddWithValue(start.Profile.Name);
            command.Parameters.Add(new NpgsqlParameter { Value = AnalysisSettingsFile.Describe(start.Profile).ToJsonString(), NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.AddWithValue(start.Profile.Endpoint.ToString());
            command.Parameters.AddWithValue(start.Profile.Model);
            command.Parameters.AddWithValue((object?)start.Profile.File.Name ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)start.Profile.File.Quantization ?? DBNull.Value);
            command.Parameters.AddWithValue((object?)start.ModelSha256 ?? DBNull.Value);
            command.Parameters.AddWithValue(start.ModelSha256Basis);
            command.Parameters.AddWithValue((object?)start.ServerBuild ?? DBNull.Value);
            command.Parameters.Add(new NpgsqlParameter { Value = start.Parameters.ToJsonString(), NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.AddWithValue(start.PromptTemplate);
            command.Parameters.AddWithValue(start.PromptTemplateVersion);
            command.Parameters.AddWithValue(start.InputSha256);
            command.Parameters.AddWithValue(start.InputReferences.ToArray());
            return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }, cancellationToken);
    }

    public Task FinishAttemptAsync(Guid attemptId, AttemptEnd end, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(end);
        return UseAsync(async connection =>
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (var command = new NpgsqlCommand(
                "UPDATE analysis_attempts SET finished_utc = now(), outcome = $2, failure = $3, prompt_tokens = $4, " +
                "completion_tokens = $5, prompt_milliseconds = $6, completion_milliseconds = $7, finish_reason = $8, " +
                "raw_output = $9, output = $10 WHERE attempt_id = $1",
                connection,
                transaction))
            {
                var response = end.Response;
                command.Parameters.AddWithValue(attemptId);
                command.Parameters.AddWithValue(end.Outcome);
                command.Parameters.AddWithValue((object?)end.Failure ?? DBNull.Value);
                command.Parameters.AddWithValue((object?)response?.PromptTokens ?? DBNull.Value);
                command.Parameters.AddWithValue((object?)response?.CompletionTokens ?? DBNull.Value);
                command.Parameters.AddWithValue((object?)response?.PromptMilliseconds ?? DBNull.Value);
                command.Parameters.AddWithValue((object?)response?.CompletionMilliseconds ?? DBNull.Value);
                command.Parameters.AddWithValue((object?)response?.FinishReason ?? DBNull.Value);
                command.Parameters.AddWithValue((object?)response?.Content ?? DBNull.Value);
                command.Parameters.Add(new NpgsqlParameter
                {
                    Value = end.Output is { } output ? output.GetRawText() : DBNull.Value,
                    NpgsqlDbType = NpgsqlDbType.Jsonb
                });
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var claim in end.Claims)
            {
                await using var command = new NpgsqlCommand(
                    "INSERT INTO analysis_claims (attempt_id, section, paragraph, ordinal, claim_kind, text, evidence) " +
                    "VALUES ($1, $2, $3, $4, $5, $6, $7)",
                    connection,
                    transaction);
                command.Parameters.AddWithValue(attemptId);
                command.Parameters.AddWithValue(claim.Section);
                command.Parameters.AddWithValue(claim.Paragraph);
                command.Parameters.AddWithValue(claim.Ordinal);
                command.Parameters.AddWithValue(claim.Kind);
                command.Parameters.AddWithValue(claim.Text);
                command.Parameters.AddWithValue(claim.Evidence.ToArray());
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>A model file's hash, if it was hashed at this size and change time.</summary>
    public Task<string?> FileHashAsync(string path, long length, DateTime lastWriteUtc, CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT sha256 FROM analysis_model_file_hashes WHERE path = $1 AND length = $2 AND last_write_utc = $3",
                connection);
            command.Parameters.AddWithValue(path);
            command.Parameters.AddWithValue(length);
            command.Parameters.AddWithValue(DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc));
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }, cancellationToken);

    public Task SaveFileHashAsync(string path, long length, DateTime lastWriteUtc, string sha256, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "INSERT INTO analysis_model_file_hashes (path, length, last_write_utc, sha256) VALUES ($1, $2, $3, $4) " +
            "ON CONFLICT (path) DO UPDATE SET length = excluded.length, last_write_utc = excluded.last_write_utc, " +
            "sha256 = excluded.sha256, hashed_utc = now()",
            cancellationToken,
            path,
            length,
            DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc),
            sha256);

    /// <summary>Adds a description of the participant's capabilities; the earlier ones are kept.</summary>
    public Task<Guid> SaveCapabilitiesAsync(Guid recordingId, string description, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO participant_capabilities (recording_id, description) VALUES ($1, $2) RETURNING capability_id",
                connection);
            command.Parameters.AddWithValue(recordingId);
            command.Parameters.AddWithValue(description.Trim());
            return (Guid)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }, cancellationToken);
    }

    /// <summary>A recording's capability descriptions, newest first.</summary>
    public Task<IReadOnlyList<CapabilityDescription>> CapabilitiesAsync(Guid recordingId, CancellationToken cancellationToken) =>
        UseAsync<IReadOnlyList<CapabilityDescription>>(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT capability_id, description, written_utc FROM participant_capabilities " +
                "WHERE recording_id = $1 ORDER BY written_utc DESC, capability_id DESC",
                connection);
            command.Parameters.AddWithValue(recordingId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var descriptions = new List<CapabilityDescription>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                descriptions.Add(new CapabilityDescription(reader.GetGuid(0), reader.GetString(1), reader.GetDateTime(2)));
            }

            return descriptions;
        }, cancellationToken);

    /// <summary>The path of a recording's recording file under its session folder, or null when its events are in the evidence tables.</summary>
    public Task<string?> RecordingFilePathAsync(Guid recordingId, CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand("SELECT path FROM recording_files WHERE recording_id = $1", connection);
            command.Parameters.AddWithValue(recordingId);
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }, cancellationToken);

    /// <summary>The recording with a session key, or null.</summary>
    public Task<Guid?> RecordingIdAsync(string sessionKey, CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand("SELECT recording_id FROM recordings WHERE session_key = $1", connection);
            command.Parameters.AddWithValue(sessionKey);
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as Guid?;
        }, cancellationToken);

    /// <summary>A task's report files, oldest first: each file's name, its path under the session folder, and its SHA-256.</summary>
    public Task<IReadOnlyList<(string FileName, string RelativePath, string Sha256)>> ReportsAsync(Guid taskId, CancellationToken cancellationToken) =>
        UseAsync<IReadOnlyList<(string, string, string)>>(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT file_name, relative_path, sha256 FROM analysis_reports WHERE task_id = $1 ORDER BY written_utc, report_id", connection);
            command.Parameters.AddWithValue(taskId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var reports = new List<(string, string, string)>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                reports.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }

            return reports;
        }, cancellationToken);

    /// <summary>Whether any task is waiting to run, for starting the worker when the player opens.</summary>
    public Task<bool> AnyQueuedAsync(CancellationToken cancellationToken) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM analysis_tasks WHERE state IN ('queued', 'running'))", connection);
            return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }, cancellationToken);

    /// <summary>A recording's session key and start, for a report's heading.</summary>
    public Task<(string SessionKey, DateTime StartedUtc)?> RecordingAsync(Guid recordingId, CancellationToken cancellationToken) =>
        UseAsync<(string, DateTime)?>(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT session_key, started_utc FROM recordings WHERE recording_id = $1", connection);
            command.Parameters.AddWithValue(recordingId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? (reader.GetString(0), reader.GetDateTime(1))
                : null;
        }, cancellationToken);

    /// <summary>The attempts of a task, in order, for a report's account of how it was made.</summary>
    public Task<IReadOnlyList<AttemptRecord>> AttemptsAsync(Guid taskId, CancellationToken cancellationToken) =>
        UseAsync<IReadOnlyList<AttemptRecord>>(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT a.step_key, a.attempt_number, a.outcome, a.failure, a.profile_name, a.model, a.model_file, a.quantization, " +
                "a.model_sha256, a.model_sha256_basis, a.server_build, a.parameters::text, a.prompt_template, a.prompt_template_version, " +
                "a.prompt_tokens, a.completion_tokens, a.prompt_milliseconds, a.completion_milliseconds, a.started_utc, o.task_id " +
                "FROM analysis_attempts a LEFT JOIN analysis_attempts o ON o.attempt_id = a.reused_from_attempt_id " +
                "WHERE a.task_id = $1 ORDER BY a.started_utc, a.step_key, a.attempt_number",
                connection);
            command.Parameters.AddWithValue(taskId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var attempts = new List<AttemptRecord>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string? Text(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
                int? Count(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
                double? Milliseconds(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
                attempts.Add(new AttemptRecord(
                    reader.GetString(0), reader.GetInt32(1), reader.GetString(2), Text(3), reader.GetString(4), reader.GetString(5),
                    Text(6), Text(7), Text(8), reader.GetString(9), Text(10), reader.GetString(11), reader.GetString(12), reader.GetInt32(13),
                    Count(14), Count(15), Milliseconds(16), Milliseconds(17))
                {
                    ReusedFromTaskId = reader.IsDBNull(19) ? null : reader.GetGuid(19)
                });
            }

            return attempts;
        }, cancellationToken);

    public Task AddReportAsync(Guid taskId, string fileName, string relativePath, string sha256, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "INSERT INTO analysis_reports (task_id, file_name, relative_path, sha256) VALUES ($1, $2, $3, $4)",
            cancellationToken,
            taskId,
            fileName,
            relativePath,
            sha256);

    private Task ExecuteAsync(string sql, CancellationToken cancellationToken, params object[] values) =>
        UseAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var value in values)
            {
                command.Parameters.AddWithValue(value);
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    private static IReadOnlyDictionary<string, string> Profiles(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}

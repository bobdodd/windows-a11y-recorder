using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Npgsql;
using Recorder.Analysis;
using Recorder.Database;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>A model server that answers as each test scripts it.</summary>
internal sealed class StandInEndpoint : IModelEndpoint
{
    public Func<ModelProfile, string, CancellationToken, Task<ModelResponse>> Answer { get; set; } =
        (_, _, _) => Task.FromResult(Valid("E1"));

    public ConcurrentQueue<string> Calls { get; } = new();

    public ConcurrentQueue<string> Bodies { get; } = new();

    public Task<ModelResponse> CompleteAsync(ModelProfile profile, string body, CancellationToken cancellationToken)
    {
        Calls.Enqueue("complete " + profile.Name);
        Bodies.Enqueue(body);
        return Answer(profile, body, cancellationToken);
    }

    public Task<string?> ServerBuildAsync(ModelProfile profile, CancellationToken cancellationToken) =>
        Task.FromResult<string?>("b11607-ff026f8c0");

    public Task LoadAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        Calls.Enqueue("load " + profile.Name);
        return Task.CompletedTask;
    }

    public Task UnloadAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        Calls.Enqueue("unload " + profile.Name);
        return Task.CompletedTask;
    }

    public static ModelResponse Valid(string reference, string finish = "stop") =>
        new(
            $$"""{ "description": [ [ { "kind": "claim", "text": "A fact.", "evidence": ["{{reference}}"] } ] ] }""",
            finish,
            120,
            40,
            500,
            1500);
}

/// <summary>A task kind for the tests: one model step for each step key.</summary>
internal sealed class StandInKind(params string[] steps) : IAnalysisTaskKind
{
    public const string Name = "stand-in";

    public string Kind => Name;

    public int Version => 1;

    public string Capability { get; init; } = ModelCapabilities.Text;

    public ConcurrentQueue<string> Ran { get; } = new();

    public async Task RunAsync(AnalysisTaskContext context, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            var images = Capability == ModelCapabilities.Image
                ? new[] { new ModelImage("E2", "image/png", [1]) }
                : [];
            await context.RunStepAsync(
                step,
                new ModelRequest("stand-in-template", 3, "Describe.", $"E1: fact for {step}", images, ["description"], new HashSet<string> { "E1", "E2" }),
                cancellationToken);
            Ran.Enqueue($"{context.Task.TaskId}:{step}");
        }
    }
}

internal sealed class StandInLocalServer : ILocalServerHost
{
    public int Starts { get; private set; }

    public Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        Starts++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// The analysis worker with the catalog in a real PostgreSQL server and a
/// stand-in model server. See docs/architecture/analysis-runner.md.
/// </summary>
public sealed class AnalysisWorkerTests(EmbeddedPostgresFixture fixture) : IClassFixture<EmbeddedPostgresFixture>
{
    private static readonly AnalysisWorkerOptions Fast = new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ModelProfile Profile(string name, string model, params string[] capabilities) =>
        new(
            name,
            new Uri("http://192.168.1.50:8080/"),
            model,
            capabilities.Length == 0 ? [ModelCapabilities.Text] : capabilities,
            new ModelFile(model + ".gguf", "Q4_K_M", new string('a', 64), null),
            null,
            GenerationSettings.Default,
            null);

    private static AnalysisSettings SettingsWith(int maxAttempts = 3, params ModelProfile[] profiles) =>
        new(profiles.Length == 0 ? [Profile("This PC", "qwen")] : profiles, new Dictionary<string, string>(), null, maxAttempts);

    private async Task<Guid> RecordingAsync(short status = 2)
    {
        await using var connection = await fixture.Server.DataSource.OpenConnectionAsync(Token);
        await using (var project = new NpgsqlCommand("INSERT INTO projects (name) VALUES ($1) ON CONFLICT (name) DO NOTHING", connection))
        {
            project.Parameters.AddWithValue("Analysis tests");
            await project.ExecuteNonQueryAsync(Token);
        }

        await using var command = new NpgsqlCommand(
            "INSERT INTO recordings (project_id, session_key, recording_status_id, started_utc, clock_frequency, " +
            "clock_origin_timestamp, os_description, framework_description, process_architecture, " +
            "capture_keyboard_and_mouse, capture_ui_automation, capture_foreground_window, capture_desktop_frames, " +
            "frames_per_second, capture_microphone, capture_system_audio) " +
            "SELECT project_id, $2, $3, now(), 10000000, 0, 'Windows', '.NET', 'X64', true, true, true, true, 5, false, false " +
            "FROM projects WHERE name = $1 RETURNING recording_id",
            connection);
        command.Parameters.AddWithValue("Analysis tests");
        command.Parameters.AddWithValue("analysis-" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue(status);
        return (Guid)(await command.ExecuteScalarAsync(Token))!;
    }

    private async Task SetStatusAsync(Guid recording, short status)
    {
        await using var connection = await fixture.Server.DataSource.OpenConnectionAsync(Token);
        await using var command = new NpgsqlCommand("UPDATE recordings SET recording_status_id = $2 WHERE recording_id = $1", connection);
        command.Parameters.AddWithValue(recording);
        command.Parameters.AddWithValue(status);
        await command.ExecuteNonQueryAsync(Token);
    }

    // The tests share one catalog, so each clears the queue first.
    private async Task<AnalysisCatalog> CatalogAsync()
    {
        await using var connection = await fixture.Server.DataSource.OpenConnectionAsync(Token);
        await using var command = new NpgsqlCommand(
            "DELETE FROM analysis_tasks; UPDATE recordings SET recording_status_id = 2 WHERE recording_status_id = 1",
            connection);
        await command.ExecuteNonQueryAsync(Token);
        return AnalysisCatalog.For(fixture.Server.DataSource);
    }

    private static Dictionary<string, string> Text(string profile = "This PC") => new() { [ModelCapabilities.Text] = profile };

    private async Task<List<Dictionary<string, object?>>> RowsAsync(string sql, params object[] values)
    {
        await using var connection = await fixture.Server.DataSource.OpenConnectionAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
        {
            command.Parameters.AddWithValue(value);
        }

        await using var reader = await command.ExecuteReaderAsync(Token);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(Token))
        {
            var row = new Dictionary<string, object?>();
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return rows;
    }

    private async Task<string> StateAsync(Guid task) =>
        (string)(await RowsAsync("SELECT state FROM analysis_tasks WHERE task_id = $1", task))[0]["state"]!;

    private async Task<string?> FailureAsync(Guid task) =>
        (string?)(await RowsAsync("SELECT failure FROM analysis_tasks WHERE task_id = $1", task))[0]["failure"];

    private Task<List<Dictionary<string, object?>>> AttemptsAsync(Guid task) =>
        RowsAsync(
            "SELECT step_key, attempt_number, outcome, failure, profile_name, model, model_sha256, model_sha256_basis, " +
            "server_build, parameters::text AS parameters, prompt_template, prompt_template_version, input_sha256, " +
            "input_references, prompt_tokens, completion_tokens, finish_reason, raw_output, output::text AS output " +
            "FROM analysis_attempts WHERE task_id = $1 ORDER BY step_key, attempt_number",
            task);

    [Fact]
    public async Task A_task_runs_each_step_and_records_what_produced_it()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var task = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = new StandInEndpoint();
        var kind = new StandInKind("visit-1", "visit-2");

        await new AnalysisWorker(catalog, SettingsWith(), [kind], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        var attempts = await AttemptsAsync(task);
        Assert.Equal(2, attempts.Count);
        var first = attempts[0];
        Assert.Equal("visit-1", first["step_key"]);
        Assert.Equal(1, first["attempt_number"]);
        Assert.Equal(AttemptOutcomes.Accepted, first["outcome"]);
        Assert.Equal("This PC", first["profile_name"]);
        Assert.Equal("qwen", first["model"]);
        Assert.Equal(new string('a', 64), first["model_sha256"]);
        Assert.Equal("stated", first["model_sha256_basis"]);
        Assert.Equal("b11607-ff026f8c0", first["server_build"]);
        Assert.Equal("stand-in-template", first["prompt_template"]);
        Assert.Equal(3, first["prompt_template_version"]);
        Assert.Equal(ChatRequest.Sha256(endpoint.Bodies.First()), first["input_sha256"]);
        Assert.Equal(new[] { "E1", "E2" }, (string[])first["input_references"]!);
        Assert.Equal(120, first["prompt_tokens"]);
        Assert.Equal(40, first["completion_tokens"]);
        Assert.Equal("stop", first["finish_reason"]);
        Assert.Equal(StandInEndpoint.Valid("E1").Content, first["raw_output"]);
        Assert.Contains("\"cache_prompt\": false", (string)first["parameters"]!, StringComparison.Ordinal);
        var claims = await RowsAsync(
            "SELECT c.section, c.claim_kind, c.text, c.evidence FROM analysis_claims c " +
            "JOIN analysis_attempts a ON a.attempt_id = c.attempt_id WHERE a.task_id = $1",
            task);
        Assert.Equal(2, claims.Count);
        Assert.All(claims, claim => Assert.Equal(new[] { "E1" }, (string[])claim["evidence"]!));
        Assert.Equal(["load This PC", "complete This PC", "complete This PC", "unload This PC"], endpoint.Calls);
    }

    [Fact]
    public async Task Output_cut_off_at_the_limit_is_run_again_whole_and_the_failed_attempt_kept()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var calls = 0;
        var endpoint = new StandInEndpoint
        {
            Answer = (_, _, _) => Task.FromResult(++calls == 1
                ? StandInEndpoint.Valid("E1", "length") with { Content = "{ \"description\": [ [ { \"kind\"" }
                : StandInEndpoint.Valid("E1"))
        };

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        var attempts = await AttemptsAsync(task);
        Assert.Equal([AttemptOutcomes.Failed, AttemptOutcomes.Accepted], attempts.Select(attempt => attempt["outcome"]));
        Assert.Contains("output limit of 2048 tokens", (string)attempts[0]["failure"]!, StringComparison.Ordinal);
        Assert.Equal("{ \"description\": [ [ { \"kind\"", attempts[0]["raw_output"]);
        Assert.Null(attempts[0]["output"]);
    }

    [Fact]
    public async Task A_step_that_fails_at_every_attempt_fails_its_task_with_the_reason()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = new StandInEndpoint { Answer = (_, _, _) => Task.FromResult(StandInEndpoint.Valid("E7")) };

        await new AnalysisWorker(catalog, SettingsWith(maxAttempts: 2), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(task));
        var failure = await FailureAsync(task);
        Assert.Contains("failed 2 times, the limit", failure, StringComparison.Ordinal);
        Assert.Contains("cites E7, which was not in the input", failure, StringComparison.Ordinal);
        var attempts = await AttemptsAsync(task);
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, attempt => Assert.Equal(AttemptOutcomes.Failed, attempt["outcome"]));
        Assert.Empty(await RowsAsync("SELECT 1 FROM analysis_claims c JOIN analysis_attempts a ON a.attempt_id = c.attempt_id WHERE a.task_id = $1", task));
    }

    [Fact]
    public async Task A_server_error_is_a_failed_attempt()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var calls = 0;
        var endpoint = new StandInEndpoint
        {
            Answer = (_, _, _) => ++calls == 1
                ? throw new ModelEndpointException("The model server answered 500 Internal Server Error: out of memory")
                : Task.FromResult(StandInEndpoint.Valid("E1"))
        };

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        var attempts = await AttemptsAsync(task);
        Assert.Equal("The model server answered 500 Internal Server Error: out of memory", attempts[0]["failure"]);
        Assert.Equal(AttemptOutcomes.Accepted, attempts[1]["outcome"]);
    }

    [Fact]
    public async Task The_worker_waits_while_a_recording_is_in_progress()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync(status: 1);
        var task = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = new StandInEndpoint();
        var statuses = new ConcurrentQueue<string>();
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(Token);
        wait.CancelAfter(TimeSpan.FromMilliseconds(400));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast, status: statuses.Enqueue)
                .RunUntilEmptyAsync(wait.Token));

        Assert.Empty(endpoint.Calls);
        Assert.Contains("Waiting for the recording to end.", statuses);
        Assert.Equal(AnalysisTaskStates.Queued, await StateAsync(task));

        await SetStatusAsync(recording, 2);
        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);
        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
    }

    [Fact]
    public async Task A_recording_starting_stops_the_request_unloads_the_model_and_the_step_runs_again_after()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var task = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = new StandInEndpoint();
        endpoint.Answer = async (_, _, cancellationToken) =>
        {
            await SetStatusAsync(recording, 1);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return StandInEndpoint.Valid("E1");
        };
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(Token);
        wait.CancelAfter(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(wait.Token));

        var attempts = await AttemptsAsync(task);
        Assert.Equal(AttemptOutcomes.StoppedForRecording, Assert.Single(attempts)["outcome"]);
        Assert.Contains("unload This PC", endpoint.Calls);

        await SetStatusAsync(recording, 2);
        endpoint.Answer = (_, _, _) => Task.FromResult(StandInEndpoint.Valid("E1"));
        await new AnalysisWorker(catalog, SettingsWith(maxAttempts: 1), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        Assert.Equal(
            [AttemptOutcomes.StoppedForRecording, AttemptOutcomes.Accepted],
            (await AttemptsAsync(task)).Select(attempt => attempt["outcome"]));
    }

    [Fact]
    public async Task A_queued_task_is_cancelled_at_once_and_a_running_one_at_its_request()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var queued = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        await catalog.RequestCancelAsync(queued, Token);
        Assert.Equal(AnalysisTaskStates.Cancelled, await StateAsync(queued));

        var running = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = new StandInEndpoint();
        endpoint.Answer = async (_, _, cancellationToken) =>
        {
            await catalog.RequestCancelAsync(running, CancellationToken.None);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return StandInEndpoint.Valid("E1");
        };

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1", "visit-2")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Cancelled, await StateAsync(running));
        Assert.Equal(AttemptOutcomes.Cancelled, Assert.Single(await AttemptsAsync(running))["outcome"]);
    }

    [Fact]
    public async Task Tasks_using_the_loaded_model_run_before_the_model_is_switched()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var first = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text("A"), cancellationToken: Token);
        var second = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text("B"), cancellationToken: Token);
        var third = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text("A"), cancellationToken: Token);
        var endpoint = new StandInEndpoint();
        var kind = new StandInKind("visit-1");

        await new AnalysisWorker(catalog, SettingsWith(3, Profile("A", "a"), Profile("B", "b")), [kind], endpoint, options: Fast)
            .RunUntilEmptyAsync(Token);

        Assert.Equal([$"{first}:visit-1", $"{third}:visit-1", $"{second}:visit-1"], kind.Ran);
        Assert.Equal(["load A", "complete A", "complete A", "unload A", "load B", "complete B", "unload B"], endpoint.Calls);
    }

    [Fact]
    public async Task A_task_taken_up_again_reuses_its_accepted_steps_and_the_abandoned_attempt_is_closed()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        using var killed = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var endpoint = new StandInEndpoint();
        endpoint.Answer = async (_, body, cancellationToken) =>
        {
            if (body.Contains("visit-2", StringComparison.Ordinal))
            {
                await killed.CancelAsync();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return StandInEndpoint.Valid("E1");
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1", "visit-2")], endpoint, options: Fast).RunUntilEmptyAsync(killed.Token));
        Assert.Equal(AttemptOutcomes.Running, (await AttemptsAsync(task))[1]["outcome"]);

        var again = new StandInEndpoint();
        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1", "visit-2")], again, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        Assert.Single(again.Bodies);
        Assert.Contains("visit-2", again.Bodies.Single(), StringComparison.Ordinal);
        var attempts = await AttemptsAsync(task);
        Assert.Equal(
            [("visit-1", AttemptOutcomes.Accepted), ("visit-2", AttemptOutcomes.Failed), ("visit-2", AttemptOutcomes.Accepted)],
            attempts.Select(attempt => ((string)attempt["step_key"]!, (string)attempt["outcome"]!)));
        Assert.Equal("The worker stopped during the attempt.", attempts[1]["failure"]);
    }

    [Fact]
    public async Task A_task_after_another_waits_for_it_and_fails_if_it_fails()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var summary = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        var vision = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), afterTaskId: summary, cancellationToken: Token);
        var kind = new StandInKind("visit-1");

        await new AnalysisWorker(catalog, SettingsWith(), [kind], new StandInEndpoint(), options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal([$"{summary}:visit-1", $"{vision}:visit-1"], kind.Ran);

        var failing = await catalog.QueueTaskAsync(recording, "no-such-kind", 1, Text(), cancellationToken: Token);
        var following = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), afterTaskId: failing, cancellationToken: Token);
        await new AnalysisWorker(catalog, SettingsWith(), [kind], new StandInEndpoint(), options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal("This worker has no task kind \"no-such-kind\".", await FailureAsync(failing));
        Assert.Equal("The task it follows did not finish: it was failed.", await FailureAsync(following));
    }

    [Fact]
    public async Task A_task_fails_when_its_profile_or_version_does_not_fit()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var missing = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text("Gone"), cancellationToken: Token);
        var noImage = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, new Dictionary<string, string> { [ModelCapabilities.Image] = "This PC" }, cancellationToken: Token);
        var oldVersion = await catalog.QueueTaskAsync(recording, StandInKind.Name, 2, Text(), cancellationToken: Token);
        var kind = new StandInKind("visit-1") { Capability = ModelCapabilities.Image };

        await new AnalysisWorker(catalog, SettingsWith(), [kind], new StandInEndpoint(), options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal("No model profile was chosen for image when the task was queued.", await FailureAsync(missing));
        Assert.Equal("The model profile \"This PC\" cannot take image.", await FailureAsync(noImage));
        Assert.Equal("The task was queued for version 2 of \"stand-in\"; this worker has version 1.", await FailureAsync(oldVersion));
    }

    [Fact]
    public async Task A_model_file_on_this_PC_is_hashed_once_and_must_match_its_profile()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var path = Path.Combine(Path.GetTempPath(), "recorder-model-" + Guid.NewGuid().ToString("N") + ".gguf");
        await File.WriteAllTextAsync(path, "model weights", Token);
        try
        {
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("model weights")));
            var local = Profile("This PC", "qwen") with
            {
                Endpoint = new Uri("http://127.0.0.1:8091/"),
                File = new ModelFile("m.gguf", "Q4_K_M", hash, null),
                Local = new LocalModel(path, null, "-ngl 99")
            };
            var server = new StandInLocalServer();
            var good = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);

            await new AnalysisWorker(catalog, SettingsWith(3, local), [new StandInKind("visit-1")], new StandInEndpoint(), server, Fast).RunUntilEmptyAsync(Token);

            var attempt = Assert.Single(await AttemptsAsync(good));
            Assert.Equal(hash, attempt["model_sha256"]);
            Assert.Equal("hashed", attempt["model_sha256_basis"]);
            Assert.Equal(1, server.Starts);
            Assert.Single(await RowsAsync("SELECT 1 FROM analysis_model_file_hashes WHERE path = $1 AND sha256 = $2", new FileInfo(path).FullName, hash));

            var wrong = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
            var mismatched = local with { File = local.File with { Sha256 = new string('b', 64) } };
            await new AnalysisWorker(catalog, SettingsWith(3, mismatched), [new StandInKind("visit-1")], new StandInEndpoint(), server, Fast).RunUntilEmptyAsync(Token);

            Assert.Contains($"has SHA-256 {hash}, not {new string('b', 64)}", await FailureAsync(wrong), StringComparison.Ordinal);
            Assert.Empty(await AttemptsAsync(wrong));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Removing_a_recording_removes_its_analysis()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var task = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], new StandInEndpoint(), options: Fast).RunUntilEmptyAsync(Token);

        await new RecordingStore(fixture.Server.DataSource).DeleteRecordingAsync(recording, Token);

        Assert.Empty(await RowsAsync("SELECT 1 FROM analysis_tasks WHERE task_id = $1", task));
        Assert.Empty(await AttemptsAsync(task));
    }

    [Fact]
    public async Task The_catalog_starts_the_server_again_when_its_owner_stopped_it()
    {
        var directory = Path.Combine(Path.GetTempPath(), "recorder-pg-" + Guid.NewGuid().ToString("N"));
        var options = new EmbeddedPostgresOptions(BinaryDirectory(), directory, new PassThroughSecretProtector());
        var owner = await EmbeddedPostgresServer.StartAsync(options, Token);
        var connections = 0;
        EmbeddedPostgresServer? attached = null;
        try
        {
            var catalog = new AnalysisCatalog(async cancellationToken =>
            {
                connections++;
                attached = await EmbeddedPostgresServer.StartAsync(options, cancellationToken);
                return attached.DataSource;
            });
            Assert.False(await catalog.RecordingInProgressAsync(Token));

            await owner.StopAsync(Token);

            Assert.False(await catalog.RecordingInProgressAsync(Token));
            Assert.Equal(2, connections);
        }
        finally
        {
            if (attached is not null)
            {
                await attached.StopAsync(CancellationToken.None);
            }

            await owner.DisposeAsync();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

/// <summary>The HTTP model endpoint against a stand-in server on loopback.</summary>
public sealed class HttpModelEndpointTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class Protector : IApiKeyProtector
    {
        public string Protect(string apiKey) => "p:" + apiKey;

        public string Unprotect(string protectedApiKey) => protectedApiKey[2..];
    }

    private static async Task<(string Method, string Path, string Body, string? Authorization)> ServeOnceAsync(
        HttpListener listener,
        int status,
        string answer)
    {
        var context = await listener.GetContextAsync();
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var seen = (context.Request.HttpMethod, context.Request.Url!.PathAndQuery, body, context.Request.Headers["Authorization"]);
        context.Response.StatusCode = status;
        var bytes = Encoding.UTF8.GetBytes(answer);
        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
        return seen;
    }

    [Fact]
    public async Task A_completion_is_sent_as_built_and_its_answer_read()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var client = new HttpClient();
        var endpoint = new HttpModelEndpoint(client, new Protector());
        var profile = new ModelProfile(
            "Server",
            new Uri($"http://127.0.0.1:{port}"),
            "qwen",
            [ModelCapabilities.Text],
            new ModelFile(null, null, null, null),
            null,
            GenerationSettings.Default,
            "p:key-123");
        var answer = """
            { "choices": [ { "message": { "role": "assistant", "content": "{\"description\": []}" }, "finish_reason": "stop" } ],
              "usage": { "prompt_tokens": 2040, "completion_tokens": 256 },
              "timings": { "prompt_ms": 14812.5, "predicted_ms": 12000.25 } }
            """;

        var serving = ServeOnceAsync(listener, 200, answer);
        var response = await endpoint.CompleteAsync(profile, "{\"model\":\"qwen\"}", Token);
        var seen = await serving;

        Assert.Equal(("POST", "/v1/chat/completions", "{\"model\":\"qwen\"}", "Bearer key-123"), seen);
        Assert.Equal(new ModelResponse("{\"description\": []}", "stop", 2040, 256, 14812.5, 12000.25), response);

        serving = ServeOnceAsync(listener, 503, "{\"error\": \"loading model\"}");
        var failure = await Assert.ThrowsAsync<ModelEndpointException>(() => endpoint.CompleteAsync(profile, "{}", Token));
        await serving;
        Assert.Contains("503", failure.Message, StringComparison.Ordinal);
        Assert.Contains("loading model", failure.Message, StringComparison.Ordinal);

        serving = ServeOnceAsync(listener, 200, "{\"build_info\": \"b11607-ff026f8c0\"}");
        Assert.Equal("b11607-ff026f8c0", await endpoint.ServerBuildAsync(profile, Token));
        Assert.Equal("/props?model=qwen&autoload=false", (await serving).Path);
    }

    [Fact]
    public async Task A_local_profile_is_loaded_and_unloaded_through_the_router()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var client = new HttpClient();
        var endpoint = new HttpModelEndpoint(client, null, loadPoll: TimeSpan.FromMilliseconds(10));
        var profile = LocalProfile(port);

        var serving = ServeAsync(listener, "{\"success\": true}", Models("loading"), Models("loading"), Models("loaded"));
        await endpoint.LoadAsync(profile, Token);
        var seen = await serving;
        Assert.Equal(("POST", "/models/load", "{\"model\":\"qwen3.6-35b-a3b\"}", null), seen[0]);
        Assert.Equal(["/models", "/models", "/models"], seen.Skip(1).Select(request => request.Path));

        var unloading = ServeOnceAsync(listener, 200, "{\"success\": true}");
        await endpoint.UnloadAsync(profile, Token);
        Assert.Equal("/models/unload", (await unloading).Path);
    }

    [Fact]
    public async Task A_model_that_fails_to_load_fails_saying_so()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var client = new HttpClient();
        var endpoint = new HttpModelEndpoint(client, null, loadPoll: TimeSpan.FromMilliseconds(10));

        var serving = ServeAsync(listener, "{\"success\": true}", Models("loading"), Models("unloaded", "\"failed\": true, \"exit_code\": 1"));
        var failure = await Assert.ThrowsAsync<ModelEndpointException>(() => endpoint.LoadAsync(LocalProfile(port), Token));
        await serving;

        Assert.Equal(
            "The model server could not load qwen3.6-35b-a3b; its model process exited with code 1. Its log is llama-server.log in the analysis folder.",
            failure.Message);
    }

    [Fact]
    public async Task A_model_still_loading_at_the_limit_fails_saying_so()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var client = new HttpClient();
        var endpoint = new HttpModelEndpoint(client, null, loadTimeout: TimeSpan.Zero, loadPoll: TimeSpan.FromMilliseconds(10));

        var serving = ServeAsync(listener, "{\"success\": true}", Models("loading"));
        var failure = await Assert.ThrowsAsync<ModelEndpointException>(() => endpoint.LoadAsync(LocalProfile(port), Token));
        await serving;

        Assert.StartsWith("The model server had not loaded qwen3.6-35b-a3b after 0 minutes; its state was loading.", failure.Message, StringComparison.Ordinal);
    }

    private static ModelProfile LocalProfile(int port) =>
        new(
            "This PC",
            new Uri($"http://127.0.0.1:{port}/"),
            "qwen3.6-35b-a3b",
            [ModelCapabilities.Text],
            new ModelFile(null, null, null, null),
            new LocalModel("C:\\q.gguf", null, ""),
            GenerationSettings.Default,
            null);

    // The router's list of models, as llama.cpp's server documents it.
    private static string Models(string state, string? more = null) =>
        "{\"data\": [{\"id\": \"other\", \"status\": {\"value\": \"unloaded\"}}, " +
        "{\"id\": \"qwen3.6-35b-a3b\", \"status\": {\"value\": \"" + state + "\"" + (more is null ? "" : ", " + more) + "}}]}";

    private static async Task<List<(string Method, string Path, string Body, string? Authorization)>> ServeAsync(HttpListener listener, params string[] answers)
    {
        var seen = new List<(string, string, string, string?)>();
        foreach (var answer in answers)
        {
            seen.Add(await ServeOnceAsync(listener, 200, answer));
        }

        return seen;
    }

    [Fact]
    public async Task An_endpoint_outside_the_local_network_is_never_contacted()
    {
        using var client = new HttpClient();
        var endpoint = new HttpModelEndpoint(client, null);
        var profile = new ModelProfile(
            "Outside",
            new Uri("http://8.8.8.8:8080/"),
            "m",
            [ModelCapabilities.Text],
            new ModelFile(null, null, null, null),
            null,
            GenerationSettings.Default,
            null);

        var failure = await Assert.ThrowsAsync<ModelEndpointException>(() => endpoint.CompleteAsync(profile, "{}", Token));

        Assert.Contains("Recordings are not sent outside the local network.", failure.Message, StringComparison.Ordinal);
    }
}

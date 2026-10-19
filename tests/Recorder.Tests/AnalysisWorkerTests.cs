using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    // The answer is given to the watch whole, as one piece of a stream.
    public async Task<ModelResponse> CompleteAsync(
        ModelProfile profile,
        string body,
        Func<string, string?>? watch,
        CancellationToken cancellationToken)
    {
        var response = await CompleteAsync(profile, body, cancellationToken);
        return response.Content is { } content && watch?.Invoke(content) is { } stop
            ? new ModelResponse(content, null, null, null, null, null, stop)
            : response;
    }

    public string Build { get; set; } = "b11607-ff026f8c0";

    public Task<string?> ServerBuildAsync(ModelProfile profile, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(Build);

    public Task<System.Text.Json.Nodes.JsonObject?> ServerSamplingAsync(ModelProfile profile, CancellationToken cancellationToken) =>
        Task.FromResult<System.Text.Json.Nodes.JsonObject?>(new() { ["repeat_last_n"] = 64, ["presence_penalty"] = 0.0 });

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

    /// <summary>Each step's input, so that a test can change one.</summary>
    public Func<string, string> Input { get; init; } = step => $"E1: fact for {step}";

    /// <summary>Each step's request as sent, so that a test can mark one.</summary>
    public Func<string, ModelRequest, ModelRequest> Request { get; init; } = (_, request) => request;

    public async Task RunAsync(AnalysisTaskContext context, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            var images = Capability == ModelCapabilities.Image
                ? new[] { new ModelImage("E2", "image/png", [1]) }
                : [];
            await context.RunStepAsync(
                step,
                Request(step, new ModelRequest("stand-in-template", 3, "Describe.", Input(step), images, ["description"], new HashSet<string> { "E1", "E2" })),
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

    // A profile whose output can differ from one request to the next, so
    // refused output is asked for again.
    private static ModelProfile Varying() =>
        Profile("This PC", "qwen") with { Generation = GenerationSettings.Default with { Temperature = 0.3 } };

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
        Assert.Contains("\"server_defaults\": {\"repeat_last_n\": 64, \"presence_penalty\": 0}", (string)first["parameters"]!, StringComparison.Ordinal);
        var claims = await RowsAsync(
            "SELECT c.section, c.claim_kind, c.text, c.evidence FROM analysis_claims c " +
            "JOIN analysis_attempts a ON a.attempt_id = c.attempt_id WHERE a.task_id = $1",
            task);
        Assert.Equal(2, claims.Count);
        Assert.All(claims, claim => Assert.Equal(new[] { "E1" }, (string[])claim["evidence"]!));
        Assert.Equal(["load This PC", "complete This PC", "complete This PC", "unload This PC"], endpoint.Calls);
    }

    [Fact]
    public async Task Output_cut_off_at_the_limit_is_run_again_whole_where_output_can_differ()
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

        await new AnalysisWorker(catalog, SettingsWith(3, Varying()), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

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

        await new AnalysisWorker(catalog, SettingsWith(2, Varying()), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

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
    public async Task Refused_output_at_temperature_0_is_not_asked_for_again()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var calls = 0;
        var endpoint = new StandInEndpoint
        {
            Answer = (_, _, _) => Task.FromResult(++calls == 1 ? StandInEndpoint.Valid("E7") : StandInEndpoint.Valid("E1"))
        };

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(1, calls);
        Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(task));
        var failure = await FailureAsync(task);
        Assert.StartsWith("Step visit-1 failed: ", failure, StringComparison.Ordinal);
        Assert.Contains("cites E7, which was not in the input", failure, StringComparison.Ordinal);
        Assert.EndsWith("At temperature 0 the same request gives the same output, so it was not asked for again.", failure, StringComparison.Ordinal);
        var attempt = Assert.Single(await AttemptsAsync(task));
        Assert.Equal(StandInEndpoint.Valid("E7").Content, attempt["raw_output"]);
    }

    // An answer that reached the output limit is recovered from as one
    // caught in a loop: sampled at seed 1, 2, and 3 in turn, the first that
    // passes kept (owner, 2026-10-17; analysis-runner.md, "An answer that
    // reached the output limit").
    [Fact]
    public async Task An_answer_that_reached_the_output_limit_is_sampled_again_as_after_a_loop()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var calls = 0;
        var endpoint = new StandInEndpoint
        {
            Answer = (_, _, _) => Task.FromResult(++calls <= 2
                ? StandInEndpoint.Valid("E1", "length") with { Content = "{ \"description\": [ [ { \"kind\"" }
                : StandInEndpoint.Valid("E1"))
        };

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        Assert.Equal(3, endpoint.Bodies.Count);
        Assert.Equal([0d, 0.7, 0.7], endpoint.Bodies.Select(body => Settings(body).GetProperty("temperature").GetDouble()));
        Assert.Equal([1L, 2L], endpoint.Bodies.Skip(1).Select(body => Settings(body).GetProperty("seed").GetInt64()));
        var attempts = await AttemptsAsync(task);
        Assert.Equal([AttemptOutcomes.Failed, AttemptOutcomes.Failed, AttemptOutcomes.Accepted], attempts.Select(attempt => attempt["outcome"]));
    }

    // Where no sample passes, the step fails, as there is no loop to cut
    // the answer at, saying why each failed, and its output is saved.
    [Fact]
    public async Task An_answer_at_the_output_limit_with_no_sample_that_passes_fails_its_step()
    {
        var session = Path.Combine(Path.GetTempPath(), "limit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = await CatalogAsync();
            var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), sessionDirectory: session, cancellationToken: Token);
            const string cut = "{ \"description\": [ [ { \"kind\"";
            var endpoint = new StandInEndpoint { Answer = (_, _, _) => Task.FromResult(StandInEndpoint.Valid("E1", "length") with { Content = cut }) };

            await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

            Assert.Equal(4, endpoint.Bodies.Count);
            Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(task));
            Assert.Equal(
                "Step visit-1 failed: its answer reached the output limit, and none of the answers sampled after it passed (" +
                "at seed 1, The output stopped at the output limit of 2048 tokens.; at seed 2, The output stopped at the output limit of 2048 tokens.; " +
                "at seed 3, The output stopped at the output limit of 2048 tokens.).",
                await FailureAsync(task));
            Assert.Equal(cut, File.ReadAllText(AnalysisPlayer.FailedOutputPath(session, task)));
        }
        finally
        {
            if (Directory.Exists(session))
            {
                Directory.Delete(session, recursive: true);
            }
        }
    }

    // A task whose step failed at the output limit before this, run again,
    // samples without sending the request at temperature 0 again, as on the
    // target machine (2026-10-16, moment 8, layer 2).
    [Fact]
    public async Task A_task_run_again_after_an_answer_at_the_output_limit_samples_without_sending_it_again()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var first = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        var once = new StandInEndpoint { Answer = (_, _, _) => Task.FromResult(StandInEndpoint.Valid("E1", "length") with { Content = "{" }) };
        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], once, options: Fast).RunUntilEmptyAsync(Token);

        // As before 2026-10-17: the step failed at once, with no samples.
        await using (var connection = await fixture.Server.DataSource.OpenConnectionAsync(Token))
        await using (var command = new NpgsqlCommand("DELETE FROM analysis_attempts WHERE task_id = $1 AND attempt_number > 1", connection))
        {
            command.Parameters.AddWithValue(first);
            await command.ExecuteNonQueryAsync(Token);
        }

        var again = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: first, cancellationToken: Token);
        var endpoint = Answers(Answer("A fact at seed 1."));

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(again));
        var body = Assert.Single(endpoint.Bodies);
        Assert.Equal((0.7, 1L), (Settings(body).GetProperty("temperature").GetDouble(), Settings(body).GetProperty("seed").GetInt64()));
    }

    // The output of the step that failed a task is saved in the task's
    // folder, and so is it when the task is run again and fails at once
    // without asking (owner, 2026-10-16).
    [Fact]
    public async Task The_output_of_a_step_that_failed_a_task_is_saved_in_its_folder()
    {
        var session = Path.Combine(Path.GetTempPath(), "failed-output-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = await CatalogAsync();
            var recording = await RecordingAsync();
            var first = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), sessionDirectory: session, cancellationToken: Token);
            var cut = StandInEndpoint.Valid("E7").Content;
            var endpoint = new StandInEndpoint { Answer = (_, _, _) => Task.FromResult(StandInEndpoint.Valid("E7")) };
            await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

            var again = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), sessionDirectory: session, rerunOfTaskId: first, cancellationToken: Token);
            var none = new StandInEndpoint { Answer = (_, _, _) => Task.FromResult(StandInEndpoint.Valid("E1")) };
            await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], none, options: Fast).RunUntilEmptyAsync(Token);

            Assert.Equal(cut, File.ReadAllText(AnalysisPlayer.FailedOutputPath(session, first)));
            Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(again));
            Assert.Empty(none.Bodies);
            Assert.Contains("so it was not asked for again", await FailureAsync(again), StringComparison.Ordinal);
            Assert.Equal(cut, File.ReadAllText(AnalysisPlayer.FailedOutputPath(session, again)));
        }
        finally
        {
            if (Directory.Exists(session))
            {
                Directory.Delete(session, recursive: true);
            }
        }
    }

    // A task run again from a task that failed at once, without asking,
    // finds the failed answer in the task that one ran again, and samples
    // without sending the request at temperature 0 (target machine,
    // 2026-10-16: the vision task run again failed at once, so its own
    // run again would have sent moment 8's request again).
    [Fact]
    public async Task A_failed_answer_is_found_through_the_tasks_run_again_in_turn()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var first = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        var once = new StandInEndpoint { Answer = (_, _, _) => Task.FromResult(StandInEndpoint.Valid("E1", "length") with { Content = "{" }) };
        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], once, options: Fast).RunUntilEmptyAsync(Token);
        await using (var connection = await fixture.Server.DataSource.OpenConnectionAsync(Token))
        await using (var command = new NpgsqlCommand("DELETE FROM analysis_attempts WHERE task_id = $1 AND attempt_number > 1", connection))
        {
            command.Parameters.AddWithValue(first);
            await command.ExecuteNonQueryAsync(Token);
        }

        // The second task, as before 2026-10-17, failed at once and made no attempt.
        var second = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: first, cancellationToken: Token);
        await catalog.FinishTaskAsync(second, AnalysisTaskStates.Failed, "Failed at once.", Token);
        Assert.Empty(await AttemptsAsync(second));
        var third = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: second, cancellationToken: Token);
        var endpoint = Answers(Answer("A fact at seed 1."));

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(third));
        var body = Assert.Single(endpoint.Bodies);
        Assert.Equal((0.7, 1L), (Settings(body).GetProperty("temperature").GetDouble(), Settings(body).GetProperty("seed").GetInt64()));
    }

    // The steps that write the report leave out the presence penalty
    // (owner, 2026-10-17): a step's request leaves out the profile's
    // sampling options it names, and its attempt records it so.
    [Fact]
    public async Task A_step_that_leaves_out_a_sampling_option_neither_sends_nor_records_it()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var penalised = Profile("This PC", "qwen") with
        {
            Generation = GenerationSettings.Default with
            {
                Sampling = GenerationSettings.Default.Sampling.SetItem("presence_penalty", 1.5).SetItem("repeat_last_n", 64)
            }
        };
        var kind = new StandInKind("visit-1", "visit-2")
        {
            Request = (step, request) => step == "visit-2"
                ? request with { SamplingLeftOut = new HashSet<string>(StringComparer.Ordinal) { "presence_penalty" } }
                : request
        };
        var endpoint = new StandInEndpoint();

        await new AnalysisWorker(catalog, SettingsWith(3, penalised), [kind], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        var bodies = endpoint.Bodies.ToArray();
        Assert.Equal(1.5, Settings(bodies[0]).GetProperty("presence_penalty").GetDouble());
        Assert.False(Settings(bodies[1]).TryGetProperty("presence_penalty", out _));
        Assert.Equal(64, Settings(bodies[1]).GetProperty("repeat_last_n").GetDouble());
        var attempts = await AttemptsAsync(task);
        Assert.Contains("\"sampling\": {\"repeat_last_n\": 64, \"presence_penalty\": 1.5}", (string)attempts[0]["parameters"]!, StringComparison.Ordinal);
        Assert.Contains("\"sampling\": {\"repeat_last_n\": 64}", (string)attempts[1]["parameters"]!, StringComparison.Ordinal);
    }

    // The story, its opening, and the executive summary need a statement:
    // an answer with none is refused and recovered from as a loop is
    // (target machine, 2026-10-17: the story of visit 1 came back with
    // every section empty; owner, the same day).
    private static readonly string NoStatement = "{ \"description\": [] }";

    private static StandInKind Needing(params string[] steps) =>
        new(steps) { Request = (_, request) => request with { NeedsStatements = true } };

    [Fact]
    public async Task An_answer_with_no_statement_for_a_step_that_needs_one_is_asked_for_at_seed_1_above_temperature_0()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(NoStatement, Answer("A sampled fact."));

        await new AnalysisWorker(catalog, SettingsWith(), [Needing("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        var bodies = endpoint.Bodies.ToArray();
        Assert.Equal(2, bodies.Length);
        Assert.Equal((0.7, 1L), (Settings(bodies[1]).GetProperty("temperature").GetDouble(), Settings(bodies[1]).GetProperty("seed").GetInt64()));
        var attempts = await AttemptsAsync(task);
        Assert.Equal(AnalysisWorker.EmptyFailure, attempts[0]["failure"]);
        Assert.Equal(AttemptOutcomes.Accepted, attempts[1]["outcome"]);
        Assert.Contains("A sampled fact.", (string)attempts[1]["output"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_with_no_statement_fails_the_step_where_no_answer_sampled_after_it_passes()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(NoStatement);

        await new AnalysisWorker(catalog, SettingsWith(), [Needing("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(task));
        Assert.Equal(4, endpoint.Bodies.Count);
        var failure = (string?)(await RowsAsync("SELECT failure FROM analysis_tasks WHERE task_id = $1", task))[0]["failure"];
        Assert.StartsWith("Step visit-1 failed: its answer gave no statement, and none of the answers sampled after it passed (at seed 1, ", failure, StringComparison.Ordinal);
        Assert.Contains("at seed 3, " + AnalysisWorker.EmptyFailure, failure, StringComparison.Ordinal);
    }

    // The steps that write the report have their wording checked: an
    // answer saying NVDA spoke, or writing in a statement a reference it
    // does not cite, is refused and recovered from as one with no
    // statement is (owner, 2026-10-18).
    private static StandInKind Worded(params string[] steps) =>
        new(steps) { Request = (_, request) => request with { ChecksWording = true } };

    [Fact]
    public async Task An_answer_saying_NVDA_spoke_is_asked_for_at_seed_1_above_temperature_0()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(Answer("NVDA announced the heading."), Answer("NVDA had to announce the heading (inferred)."));

        await new AnalysisWorker(catalog, SettingsWith(), [Worded("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        var bodies = endpoint.Bodies.ToArray();
        Assert.Equal(2, bodies.Length);
        Assert.Equal((0.7, 1L), (Settings(bodies[1]).GetProperty("temperature").GetDouble(), Settings(bodies[1]).GetProperty("seed").GetInt64()));
        var attempts = await AttemptsAsync(task);
        Assert.StartsWith(WordingCheck.Failure + "statement 1 of paragraph 1 of \"description\" says \"NVDA announced\"", (string)attempts[0]["failure"]!, StringComparison.Ordinal);
        Assert.Equal(AttemptOutcomes.Accepted, attempts[1]["outcome"]);
    }

    [Fact]
    public async Task An_answer_writing_a_reference_it_does_not_cite_fails_the_step_where_no_answer_sampled_after_it_passes()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(Answer("The page loaded [E7]."));

        await new AnalysisWorker(catalog, SettingsWith(), [Worded("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(task));
        Assert.Equal(4, endpoint.Bodies.Count);
        var failure = (string?)(await RowsAsync("SELECT failure FROM analysis_tasks WHERE task_id = $1", task))[0]["failure"];
        Assert.StartsWith("Step visit-1 failed: its answer's wording broke the report's rules, and none of the answers sampled after it passed (at seed 1, ", failure, StringComparison.Ordinal);
        Assert.Contains("writes in its text E7, which it does not cite", failure, StringComparison.Ordinal);
    }

    // A claim that cites nothing is refused, and sampled again as a wording
    // fault is; a statement that no evidence was found is of kind none
    // (target machine and owner, 2026-10-19).
    private const string Uncited = "{ \"description\": [ [ { \"text\": \"No facts were given since the moment before.\", \"kind\": \"claim\", \"evidence\": [] } ] ] }";

    [Fact]
    public async Task An_answer_with_a_claim_that_cites_nothing_is_asked_for_at_seed_1_above_temperature_0()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(Uncited, "{ \"description\": [ [ { \"text\": \"No facts were given since the moment before.\", \"kind\": \"none\", \"evidence\": [] } ] ] }");

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        var bodies = endpoint.Bodies.ToArray();
        Assert.Equal(2, bodies.Length);
        Assert.Equal((0.7, 1L), (Settings(bodies[1]).GetProperty("temperature").GetDouble(), Settings(bodies[1]).GetProperty("seed").GetInt64()));
        var attempts = await AttemptsAsync(task);
        Assert.Equal("Statement 1 of paragraph 1 of \"description\"" + ClaimCheck.UncitedFailure, attempts[0]["failure"]);
        Assert.Equal(AttemptOutcomes.Accepted, attempts[1]["outcome"]);
        var kinds = await RowsAsync(
            "SELECT c.claim_kind FROM analysis_claims c JOIN analysis_attempts a ON a.attempt_id = c.attempt_id WHERE a.task_id = $1 AND a.attempt_number = 2", task);
        Assert.Equal("none", kinds.Single()["claim_kind"]);
    }

    [Fact]
    public async Task An_answer_with_a_claim_that_cites_nothing_fails_the_step_where_no_answer_sampled_after_it_passes()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(Uncited);

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(task));
        Assert.Equal(4, endpoint.Bodies.Count);
        var failure = (string?)(await RowsAsync("SELECT failure FROM analysis_tasks WHERE task_id = $1", task))[0]["failure"];
        Assert.StartsWith("Step visit-1 failed: its answer gave a claim that cites no evidence, and none of the answers sampled after it passed (at seed 1, ", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_saying_NVDA_spoke_is_accepted_for_a_step_whose_wording_is_not_checked()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(Answer("NVDA announced the heading."));

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind(["visit-1"])], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        Assert.Single(endpoint.Bodies);
    }

    [Fact]
    public async Task An_answer_with_no_statement_is_accepted_for_a_step_that_does_not_need_one()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(NoStatement);

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        Assert.Single(endpoint.Bodies);
    }

    // Recovering from a loop: analysis-runner.md, "Decisions (2026-10-15)".
    private static string Answer(params string[] texts) =>
        "{ \"description\": [ [ " +
        string.Join(", ", texts.Select(text => text.StartsWith('!')
            ? $"{{ \"text\": {JsonSerializer.Serialize(text[1..])}, \"kind\": \"claim\", \"evidence\": [\"E7\"] }}"
            : $"{{ \"text\": {JsonSerializer.Serialize(text)}, \"kind\": \"claim\", \"evidence\": [\"E1\"] }}")) +
        " ] ] }";

    private static readonly string Looping = Answer("An opening fact.", "A fact.", "A fact.", "A fact.");

    private static StandInEndpoint Answers(params string[] contents)
    {
        var calls = 0;
        return new StandInEndpoint
        {
            Answer = (_, _, _) => Task.FromResult(StandInEndpoint.Valid("E1") with { Content = contents[Math.Min(calls++, contents.Length - 1)] })
        };
    }

    private static JsonElement Settings(string body) => JsonDocument.Parse(body).RootElement.Clone();

    [Fact]
    public async Task An_answer_caught_in_a_loop_is_asked_for_at_seed_1_above_temperature_0_and_that_answer_is_kept()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(Looping, Answer("A sampled fact."));

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        var bodies = endpoint.Bodies.ToArray();
        Assert.Equal(2, bodies.Length);
        Assert.Equal(0, Settings(bodies[0]).GetProperty("temperature").GetDouble());
        var sampled = Settings(bodies[1]);
        Assert.Equal(0.7, sampled.GetProperty("temperature").GetDouble());
        Assert.Equal(1, sampled.GetProperty("seed").GetInt64());
        Assert.Equal(0.8, sampled.GetProperty("top_p").GetDouble());
        Assert.Equal(20, sampled.GetProperty("top_k").GetDouble());
        var attempts = await AttemptsAsync(task);
        Assert.Equal([AttemptOutcomes.Failed, AttemptOutcomes.Accepted], attempts.Select(attempt => attempt["outcome"]));
        Assert.StartsWith("The model said the same statement word for word three times in a row", (string)attempts[0]["failure"]!, StringComparison.Ordinal);
        var parameters = JsonNode.Parse((string)attempts[1]["parameters"]!)!;
        Assert.Equal(0.7, parameters["temperature"]!.GetValue<double>());
        Assert.Equal(1, parameters["seed"]!.GetValue<long>());
        Assert.Equal(LoopRecovery.Sampled, parameters[LoopRecovery.AfterLoopParameter]!.GetValue<string>());
        Assert.Equal(ChatRequest.Sha256(bodies[1]), attempts[1]["input_sha256"]);
        Assert.Equal(["A sampled fact."], (await RowsAsync("SELECT text FROM analysis_claims c JOIN analysis_attempts a ON a.attempt_id = c.attempt_id WHERE a.task_id = $1", task)).Select(row => row["text"]));
    }

    [Fact]
    public async Task After_a_loop_the_seeds_are_tried_in_turn_until_one_answer_passes_the_checks()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(Looping, Looping, Answer("!A fact citing what was not given."), Answer("A fact at seed 3."));

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        Assert.Equal(
            [(0.0, 1L), (0.7, 1L), (0.7, 2L), (0.7, 3L)],
            endpoint.Bodies.Select(body => (Settings(body).GetProperty("temperature").GetDouble(), Settings(body).GetProperty("seed").GetInt64())));
        var attempts = await AttemptsAsync(task);
        Assert.Equal(
            [AttemptOutcomes.Failed, AttemptOutcomes.Failed, AttemptOutcomes.Failed, AttemptOutcomes.Accepted],
            attempts.Select(attempt => attempt["outcome"]));
        Assert.Equal(3, JsonNode.Parse((string)attempts[3]["parameters"]!)!["seed"]!.GetValue<long>());
    }

    [Fact]
    public async Task When_no_answer_sampled_after_a_loop_passes_the_answer_is_kept_up_to_the_loop_and_says_so()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(Looping);

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(task));
        Assert.Equal(4, endpoint.Bodies.Count);
        var attempts = await AttemptsAsync(task);
        Assert.Equal(5, attempts.Count);
        var cut = attempts[4];
        Assert.Equal(AttemptOutcomes.Accepted, cut["outcome"]);
        Assert.Equal(Looping, cut["raw_output"]);
        Assert.Equal(attempts[0]["input_sha256"], cut["input_sha256"]);
        var parameters = JsonNode.Parse((string)cut["parameters"]!)!;
        Assert.Equal(0, parameters["temperature"]!.GetValue<double>());
        Assert.Equal(LoopRecovery.CutShort, parameters[LoopRecovery.AfterLoopParameter]!.GetValue<string>());
        var claims = await RowsAsync("SELECT text, claim_kind FROM analysis_claims WHERE attempt_id = (SELECT attempt_id FROM analysis_attempts WHERE task_id = $1 AND outcome = 'accepted') ORDER BY ordinal", task);
        Assert.Equal(["An opening fact.", "A fact.", LoopRecovery.CutText], claims.Select(row => row["text"]));
        Assert.Equal(Claim.UnknownKind, claims[2]["claim_kind"]);
    }

    [Fact]
    public async Task A_loop_whose_answer_up_to_the_loop_fails_the_checks_fails_the_step()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = Answers(Answer("!An opening fact citing what was not given.", "A fact.", "A fact.", "A fact."));

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(task));
        var failure = await FailureAsync(task);
        Assert.StartsWith(
            "Step visit-1 failed: its answer was caught in a loop, none of the answers sampled after it passed (at seed 1, The model said the same",
            failure, StringComparison.Ordinal);
        Assert.Contains("and its answer up to the loop failed the checks: ", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_task_run_again_after_a_loop_reuses_the_answer_sampled_after_it_and_sends_nothing()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var first = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], Answers(Looping, Looping, Answer("A fact at seed 2.")), options: Fast)
            .RunUntilEmptyAsync(Token);
        var again = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: first, cancellationToken: Token);
        var endpoint = Answers(Looping);

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(again));
        Assert.Empty(endpoint.Bodies);
        var reused = Assert.Single(await RowsAsync("SELECT parameters::text AS parameters, reused_from_attempt_id FROM analysis_attempts WHERE task_id = $1", again));
        Assert.NotNull(reused["reused_from_attempt_id"]);
        Assert.Equal(2, JsonNode.Parse((string)reused["parameters"]!)!["seed"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_task_run_again_after_a_loop_cut_short_reuses_that_answer_and_sends_nothing()
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var first = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], Answers(Looping), options: Fast).RunUntilEmptyAsync(Token);
        var again = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: first, cancellationToken: Token);
        var endpoint = Answers(Answer("Never sent."));

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(again));
        Assert.Empty(endpoint.Bodies);
        var reused = Assert.Single(await RowsAsync("SELECT parameters::text AS parameters, reused_from_attempt_id FROM analysis_attempts WHERE task_id = $1", again));
        Assert.NotNull(reused["reused_from_attempt_id"]);
        Assert.Equal(LoopRecovery.CutShort, JsonNode.Parse((string)reused["parameters"]!)![LoopRecovery.AfterLoopParameter]!.GetValue<string>());
    }

    [Fact]
    public async Task A_task_run_again_after_a_loop_from_before_recovery_samples_without_sending_the_loop_again()
    {
        // A task whose loop failed the step, as before 2026-10-15.
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var first = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], Answers(Looping), options: Fast).RunUntilEmptyAsync(Token);
        await using (var connection = await fixture.Server.DataSource.OpenConnectionAsync(Token))
        {
            foreach (var sql in new[]
            {
                "DELETE FROM analysis_claims WHERE attempt_id IN (SELECT attempt_id FROM analysis_attempts WHERE task_id = $1 AND attempt_number > 1)",
                "DELETE FROM analysis_attempts WHERE task_id = $1 AND attempt_number > 1"
            })
            {
                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue(first);
                await command.ExecuteNonQueryAsync(Token);
            }
        }

        var again = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: first, cancellationToken: Token);
        var endpoint = Answers(Answer("A fact at seed 1."));

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(again));
        var body = Assert.Single(endpoint.Bodies);
        Assert.Equal(0.7, Settings(body).GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task A_trial_reruns_one_step_with_other_sampling_options_and_writes_nothing()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var calls = 0;
        var endpoint = new StandInEndpoint
        {
            Answer = (_, _, _) => Task.FromResult(++calls == 1 ? StandInEndpoint.Valid("E1") : StandInEndpoint.Valid("E7"))
        };
        var kind = new StandInKind("visit-1", "visit-2", "visit-3");
        await new AnalysisWorker(catalog, SettingsWith(), [kind], endpoint, options: Fast).RunUntilEmptyAsync(Token);
        Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(task));
        var before = await AttemptsAsync(task);

        endpoint.Answer = (_, _, _) => Task.FromResult(StandInEndpoint.Valid("E1"));
        while (endpoint.Bodies.TryDequeue(out _))
        {
        }

        var trial = await new AnalysisWorker(catalog, SettingsWith(), [kind], endpoint, options: Fast)
            .TrialAsync(task, "visit-2", new Dictionary<string, double> { ["presence_penalty"] = 1.5 }, Token);

        Assert.Equal("visit-2", trial.StepKey);
        Assert.Null(trial.Failure);
        Assert.Equal(1, trial.Statements);
        var body = Assert.Single(endpoint.Bodies);
        using (var document = JsonDocument.Parse(body))
        {
            Assert.Equal(1.5, document.RootElement.GetProperty("presence_penalty").GetDouble());
        }

        Assert.Equal(1.5, trial.Parameters["sampling"]!["presence_penalty"]!.GetValue<double>());
        Assert.Equal(64, trial.Parameters["server_defaults"]!["repeat_last_n"]!.GetValue<int>());
        Assert.Contains("Outcome: passes the checks.", trial.Describe(), StringComparison.Ordinal);
        // The trial says whether it sends the request the task sent: here
        // with an option on top, and as the task would send it, the same as
        // its failed attempt.
        Assert.NotEqual(trial.RequestSha256, trial.SentSha256);
        var failed = Assert.Single(trial.TaskAttempts);
        Assert.Equal((1, AttemptOutcomes.Failed, trial.RequestSha256), (failed.AttemptNumber, failed.Outcome, failed.InputSha256));
        Assert.Contains($"The task's attempt 1, failed: SHA-256 {trial.RequestSha256}, the same request.", trial.Describe(), StringComparison.Ordinal);
        Assert.Contains($"The request the trial sent: SHA-256 {trial.SentSha256}, with the trial's options.", trial.Describe(), StringComparison.Ordinal);
        // The trial shows what the step was asked (owner, 2026-10-17).
        Assert.Contains("\nThe step's instructions, as sent:\nDescribe.\n", trial.Describe(), StringComparison.Ordinal);
        Assert.Contains("\nThe step's input, as sent:\nE1: fact for visit-2\n", trial.Describe(), StringComparison.Ordinal);
        Assert.Contains("\nThe form the answer was held to, as sent:\n", trial.Describe(), StringComparison.Ordinal);
        Assert.Contains("\"description\"", trial.AnswerForm, StringComparison.Ordinal);
        Assert.Empty(trial.Pictures);
        Assert.Equal(before.Count, (await AttemptsAsync(task)).Count);
        Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(task));

        var unreachable = await Assert.ThrowsAsync<StepFailedException>(() =>
            new AnalysisWorker(catalog, SettingsWith(), [kind], endpoint, options: Fast)
                .TrialAsync(task, "visit-3", new Dictionary<string, double>(), Token));
        Assert.Equal("Step visit-2 has no accepted attempt in this task, so the trial cannot reach step visit-3.", unreachable.Message);
    }

    [Fact]
    public async Task A_trial_held_to_one_line_sends_the_recorders_grammar_in_place_of_the_schema_and_says_so()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = new StandInEndpoint { Answer = (_, _, _) => Task.FromResult(StandInEndpoint.Valid("E1")) };
        var kind = new StandInKind("visit-1");

        var trial = await new AnalysisWorker(catalog, SettingsWith(), [kind], endpoint, options: Fast)
            .TrialAsync(task, "visit-1", new Dictionary<string, double>(), Token, oneLine: true);

        Assert.Null(trial.Failure);
        Assert.True(trial.OneLine);
        var body = Assert.Single(endpoint.Bodies);
        using (var document = JsonDocument.Parse(body))
        {
            Assert.False(document.RootElement.TryGetProperty("response_format", out _));
            Assert.StartsWith("root ::= ", document.RootElement.GetProperty("grammar").GetString(), StringComparison.Ordinal);
        }

        Assert.NotEqual(trial.RequestSha256, trial.SentSha256);
        Assert.Contains("The answer was held to JSON on one line by the recorder's own grammar, sent as grammar in place of the schema.", trial.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_trial_may_set_the_temperature_and_the_seed_but_not_the_other_fields_the_worker_sets()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = new StandInEndpoint { Answer = (_, _, _) => Task.FromResult(StandInEndpoint.Valid("E1")) };
        var kind = new StandInKind("visit-1");

        var trial = await new AnalysisWorker(catalog, SettingsWith(), [kind], endpoint, options: Fast)
            .TrialAsync(task, "visit-1", new Dictionary<string, double> { ["temperature"] = 0.7, ["seed"] = 3, ["top_k"] = 20 }, Token);

        Assert.Null(trial.Failure);
        var body = Assert.Single(endpoint.Bodies);
        using (var document = JsonDocument.Parse(body))
        {
            Assert.Equal(0.7, document.RootElement.GetProperty("temperature").GetDouble());
            Assert.Equal(3, document.RootElement.GetProperty("seed").GetInt64());
            Assert.Equal(20, document.RootElement.GetProperty("top_k").GetDouble());
        }

        Assert.Equal(0.7, trial.Parameters["temperature"]!.GetValue<double>());
        Assert.Equal(3, trial.Parameters["seed"]!.GetValue<long>());
        Assert.NotEqual(trial.RequestSha256, trial.SentSha256);

        foreach (var (option, value, message) in new[]
        {
            ("seed", 1.5, "The seed must be a whole number."),
            ("temperature", -1.0, "The temperature must be at least 0."),
            ("max_tokens", 10.0, "The sampling option \"max_tokens\" is one the worker sets itself.")
        })
        {
            var refused = await Assert.ThrowsAsync<StepFailedException>(() =>
                new AnalysisWorker(catalog, SettingsWith(), [kind], endpoint, options: Fast)
                    .TrialAsync(task, "visit-1", new Dictionary<string, double> { [option] = value }, Token));
            Assert.Equal(message, refused.Message);
        }
    }

    [Fact]
    public async Task Output_that_fails_its_citation_check_at_temperature_0_is_not_asked_for_again()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        var calls = 0;
        var endpoint = new StandInEndpoint { Answer = (_, _, _) => { calls++; return Task.FromResult(StandInEndpoint.Valid("E7")); } };

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(1, calls);
        Assert.Contains("cites E7, which was not in the input", await FailureAsync(task), StringComparison.Ordinal);
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
        // At temperature 0 too: a server failure gave no output, so the request is sent again.
        Assert.Equal(AttemptOutcomes.Accepted, attempts[1]["outcome"]);
    }

    [Fact]
    public async Task A_request_the_server_refused_itself_is_not_sent_again()
    {
        var catalog = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(await RecordingAsync(), StandInKind.Name, 1, Text(), cancellationToken: Token);
        const string Refusal = "The model server answered 400 Bad Request: {\"error\":{\"code\":400,\"message\":\"request (20927 tokens) exceeds the available context size (16384 tokens), try increasing it\"}}";
        var endpoint = new StandInEndpoint { Answer = (_, _, _) => throw new ModelEndpointException(Refusal, refused: true) };

        // Where output could differ too: the refusal is of the request.
        await new AnalysisWorker(catalog, SettingsWith(3, Varying()), [new StandInKind("visit-1")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Single(endpoint.Bodies);
        Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(task));
        Assert.Equal($"Step visit-1 failed: {Refusal} The server refused the request itself, so it was not sent again.", await FailureAsync(task));
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

    // Reusing steps when run again: analysis-runner.md, "Reusing steps when
    // run again".
    private async Task<(AnalysisCatalog Catalog, Guid Recording, Guid First)> RunOnceAsync(StandInEndpoint? endpoint = null)
    {
        var catalog = await CatalogAsync();
        var recording = await RecordingAsync();
        var first = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1", "visit-2")], endpoint ?? new StandInEndpoint(), options: Fast)
            .RunUntilEmptyAsync(Token);
        return (catalog, recording, first);
    }

    [Fact]
    public async Task A_task_run_again_reuses_its_unchanged_steps_and_runs_the_changed_one()
    {
        var (catalog, recording, first) = await RunOnceAsync();
        var again = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: first, cancellationToken: Token);
        var endpoint = new StandInEndpoint();
        var changed = new StandInKind("visit-1", "visit-2") { Input = step => step == "visit-2" ? "E1: another fact for visit-2" : $"E1: fact for {step}" };

        await new AnalysisWorker(catalog, SettingsWith(), [changed], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(again));
        Assert.Contains("another fact for visit-2", Assert.Single(endpoint.Bodies), StringComparison.Ordinal);
        var original = await RowsAsync("SELECT attempt_id, raw_output FROM analysis_attempts WHERE task_id = $1 AND step_key = 'visit-1'", first);
        var reused = await RowsAsync(
            "SELECT attempt_id, step_key, outcome, raw_output, prompt_tokens, reused_from_attempt_id FROM analysis_attempts WHERE task_id = $1 ORDER BY step_key",
            again);
        Assert.Equal(AttemptOutcomes.Accepted, reused[0]["outcome"]);
        Assert.Equal(original[0]["attempt_id"], reused[0]["reused_from_attempt_id"]);
        Assert.Equal(original[0]["raw_output"], reused[0]["raw_output"]);
        Assert.Equal(120, reused[0]["prompt_tokens"]);
        Assert.Single(await RowsAsync("SELECT 1 FROM analysis_claims WHERE attempt_id = $1", reused[0]["attempt_id"]!));
        Assert.Equal(AttemptOutcomes.Accepted, reused[1]["outcome"]);
        Assert.Null(reused[1]["reused_from_attempt_id"]);
        var records = await catalog.AttemptsAsync(again, Token);
        Assert.Equal(first, records.Single(record => record.StepKey == "visit-1").ReusedFromTaskId);
        Assert.Null(records.Single(record => record.StepKey == "visit-2").ReusedFromTaskId);
        Assert.Equal((Guid?)first, (await catalog.TaskAsync(again, Token))!.RerunOfTaskId);
    }

    [Fact]
    public async Task A_task_run_again_after_the_server_build_changed_reuses_nothing()
    {
        var (catalog, recording, first) = await RunOnceAsync();
        var again = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: first, cancellationToken: Token);
        var endpoint = new StandInEndpoint { Build = "b11700-0000000" };

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1", "visit-2")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(2, endpoint.Bodies.Count);
        Assert.Empty(await RowsAsync("SELECT 1 FROM analysis_attempts WHERE task_id = $1 AND reused_from_attempt_id IS NOT NULL", again));
    }

    [Fact]
    public async Task A_task_run_again_after_the_model_file_changed_reuses_nothing()
    {
        var (catalog, recording, first) = await RunOnceAsync();
        var again = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: first, cancellationToken: Token);
        var endpoint = new StandInEndpoint();
        var other = Profile("This PC", "qwen") with { File = new ModelFile("qwen.gguf", "Q4_K_M", new string('b', 64), null) };

        await new AnalysisWorker(catalog, SettingsWith(3, other), [new StandInKind("visit-1", "visit-2")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(2, endpoint.Bodies.Count);
        Assert.Empty(await RowsAsync("SELECT 1 FROM analysis_attempts WHERE task_id = $1 AND reused_from_attempt_id IS NOT NULL", again));
    }

    [Fact]
    public async Task A_task_run_again_from_scratch_reuses_nothing()
    {
        var (catalog, recording, _) = await RunOnceAsync();
        var again = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), cancellationToken: Token);
        var endpoint = new StandInEndpoint();

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1", "visit-2")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(again));
        Assert.Equal(2, endpoint.Bodies.Count);
        Assert.Null((await catalog.TaskAsync(again, Token))!.RerunOfTaskId);
    }

    [Fact]
    public async Task A_step_that_failed_unchanged_at_temperature_0_fails_at_once_when_run_again()
    {
        var refusing = new StandInEndpoint
        {
            Answer = (_, body, _) => Task.FromResult(StandInEndpoint.Valid(body.Contains("visit-2", StringComparison.Ordinal) ? "E7" : "E1"))
        };
        var (catalog, recording, first) = await RunOnceAsync(refusing);
        Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(first));
        var again = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: first, cancellationToken: Token);
        var endpoint = new StandInEndpoint();

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1", "visit-2")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Empty(endpoint.Bodies);
        Assert.Equal(AnalysisTaskStates.Failed, await StateAsync(again));
        Assert.Equal(
            $"Step visit-2 failed in task {first} with the same request, model, server build, and settings, so it was not asked for again: " +
            "at temperature 0 it would give the same output. It failed: Statement 1 of paragraph 1 of \"description\" cites E7, which was not in the input.",
            await FailureAsync(again));
        var attempts = await AttemptsAsync(again);
        Assert.Equal("visit-1", Assert.Single(attempts)["step_key"]);
    }

    [Fact]
    public async Task A_rerun_of_a_rerun_points_to_the_attempt_the_model_wrote()
    {
        var (catalog, recording, first) = await RunOnceAsync();
        var second = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: first, cancellationToken: Token);
        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1", "visit-2")], new StandInEndpoint(), options: Fast).RunUntilEmptyAsync(Token);
        var third = await catalog.QueueTaskAsync(recording, StandInKind.Name, 1, Text(), rerunOfTaskId: second, cancellationToken: Token);
        var endpoint = new StandInEndpoint();

        await new AnalysisWorker(catalog, SettingsWith(), [new StandInKind("visit-1", "visit-2")], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        Assert.Equal(AnalysisTaskStates.Done, await StateAsync(third));
        Assert.Empty(endpoint.Bodies);
        var written = await RowsAsync("SELECT step_key, attempt_id FROM analysis_attempts WHERE task_id = $1 ORDER BY step_key", first);
        var copies = await RowsAsync("SELECT step_key, reused_from_attempt_id FROM analysis_attempts WHERE task_id = $1 ORDER BY step_key", third);
        Assert.Equal(written.Select(row => row["attempt_id"]), copies.Select(row => row["reused_from_attempt_id"]));
        Assert.All(await catalog.AttemptsAsync(third, Token), record => Assert.Equal(first, record.ReusedFromTaskId));
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
        string answer,
        string contentType = "application/json")
    {
        var context = await listener.GetContextAsync();
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var seen = (context.Request.HttpMethod, context.Request.Url!.PathAndQuery, body, context.Request.Headers["Authorization"]);
        context.Response.StatusCode = status;
        var bytes = Encoding.UTF8.GetBytes(answer);
        context.Response.ContentType = contentType;
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
        Assert.False(failure.Refused);

        serving = ServeOnceAsync(listener, 400, "{\"error\":{\"code\":400,\"type\":\"exceed_context_size_error\"}}");
        failure = await Assert.ThrowsAsync<ModelEndpointException>(() => endpoint.CompleteAsync(profile, "{}", Token));
        await serving;
        Assert.True(failure.Refused);

        serving = ServeOnceAsync(listener, 200, "{\"build_info\": \"b11607-ff026f8c0\"}");
        Assert.Equal("b11607-ff026f8c0", await endpoint.ServerBuildAsync(profile, Token));
        Assert.Equal("/props?model=qwen&autoload=false", (await serving).Path);
    }

    private static ModelProfile Server(int port) =>
        new(
            "Server",
            new Uri($"http://127.0.0.1:{port}"),
            "qwen",
            [ModelCapabilities.Text],
            new ModelFile(null, null, null, null),
            null,
            GenerationSettings.Default,
            null);

    // A streamed answer as llama.cpp gives it: the deltas, the chunk with
    // the finish reason, then the usage chunk with the timings.
    private static string Stream(params string[] pieces)
    {
        var text = new StringBuilder();
        foreach (var piece in pieces)
        {
            text.Append("data: ").Append(new System.Text.Json.Nodes.JsonObject
            {
                ["choices"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
                {
                    ["index"] = 0,
                    ["delta"] = new System.Text.Json.Nodes.JsonObject { ["content"] = piece }
                })
            }.ToJsonString()).Append("\n\n");
        }

        text.Append("data: {\"choices\": [ { \"index\": 0, \"delta\": {}, \"finish_reason\": \"stop\" } ]}\n\n");
        text.Append("data: {\"choices\": [], \"usage\": { \"prompt_tokens\": 2965, \"completion_tokens\": 3 }, ");
        text.Append("\"timings\": { \"prompt_ms\": 9000.5, \"predicted_ms\": 120.25 }}\n\n");
        text.Append("data: [DONE]\n\n");
        return text.ToString();
    }

    [Fact]
    public async Task A_streamed_answer_is_read_whole_with_its_finish_reason_usage_and_timings()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var client = new HttpClient();
        var endpoint = new HttpModelEndpoint(client, null);
        var seen = new List<string>();

        var serving = ServeOnceAsync(listener, 200, Stream("{\"descr", "iption\": ", "[]}"), "text/event-stream");
        var response = await endpoint.CompleteAsync(Server(port), "{}", content => { seen.Add(content); return null; }, Token);
        await serving;

        Assert.Equal(new ModelResponse("{\"description\": []}", "stop", 2965, 3, 9000.5, 120.25), response);
        Assert.Equal(["{\"descr", "{\"description\": ", "{\"description\": []}"], seen);
    }

    [Fact]
    public async Task A_streamed_answer_is_stopped_where_the_watch_says()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var client = new HttpClient();
        var endpoint = new HttpModelEndpoint(client, null);

        var serving = ServeOnceAsync(listener, 200, Stream("one ", "two ", "three"), "text/event-stream");
        var response = await endpoint.CompleteAsync(
            Server(port), "{}", content => content.EndsWith("two ", StringComparison.Ordinal) ? "Stopped at two." : null, Token);
        await serving;

        Assert.Equal(new ModelResponse("one two ", null, null, null, null, null, "Stopped at two."), response);
    }

    [Fact]
    public async Task The_server_default_sampling_settings_are_read_from_its_properties()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var client = new HttpClient();
        var endpoint = new HttpModelEndpoint(client, null);

        var serving = ServeOnceAsync(
            listener,
            200,
            "{\"default_generation_settings\": { \"params\": { \"repeat_last_n\": 64, \"presence_penalty\": 0.0 } }, \"build_info\": \"b1\"}");
        var defaults = await endpoint.ServerSamplingAsync(Server(port), Token);
        Assert.Equal("/props?model=qwen&autoload=false", (await serving).Path);

        Assert.Equal("{\"repeat_last_n\":64,\"presence_penalty\":0.0}", defaults!.ToJsonString());
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

        // The unload is answered at once; the worker waits until the model
        // is no longer listed as loaded, as the router counts it till then.
        var unloading = ServeAsync(listener, "{\"success\": true}", Models("loaded"), Models("loaded"), Models("unloaded"));
        await endpoint.UnloadAsync(profile, Token);
        Assert.Equal(["/models/unload", "/models", "/models", "/models"], (await unloading).Select(request => request.Path));
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

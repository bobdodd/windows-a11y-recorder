using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;
using Recorder.Analysis;
using Recorder.Analysis.Summary;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// The recording summary task run by the analysis worker on a recording
/// file the test writes, with the catalog in a real PostgreSQL server and a
/// stand-in model server. See docs/architecture/recording-summary.md.
/// </summary>
public sealed partial class RecordingSummaryTaskTests(EmbeddedPostgresFixture fixture) : IClassFixture<EmbeddedPostgresFixture>, IDisposable
{
    private const long Second = 1_000_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly AnalysisWorkerOptions Fast = new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
    private readonly string _session = Path.Combine(Path.GetTempPath(), "recorder-summary-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_session, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex(@"\[(E\d+)\]")]
    private static partial Regex Reference();

    private static AnalysisSettings Settings() =>
        new(
            [new ModelProfile(
                "This PC",
                new Uri("http://127.0.0.1:8080/"),
                "qwen3.6-35b-a3b",
                [ModelCapabilities.Text],
                new ModelFile("Qwen3.6-35B-A3B-UD-Q4_K_M.gguf", "Q4_K_M", new string('b', 64), null),
                null,
                GenerationSettings.Default,
                null)],
            new Dictionary<string, string>(),
            null,
            3);

    // A navigation-completed record of the tab's page, from the sample.
    private static JsonElement Navigation(string token, string url)
    {
        var node = JsonNode.Parse(EvidenceSamples.Sample("browser.navigation", "navigation-completed"))!;
        node["context"]!["documentToken"] = token;
        node["context"]!["documentId"] = "doc-" + token;
        node["context"]!["browserInstanceId"] = "browser-1";
        node["context"]!["pageId"] = "page-1";
        node["url"] = url;
        node["navigationId"] = "navigation-" + token;
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    // Two page visits, each a document with a DOM walk and Chromium's
    // accessibility records for its title, main landmark, and heading.
    private string WriteRecording(bool frames = false, IEnumerable<(long Time, string Channel, string EventType, string Json)>? extra = null)
    {
        Directory.CreateDirectory(_session);
        var items = new List<(long Time, string Channel, string EventType, JsonElement Payload)>();
        void Page(long at, string token, string url, string title, string heading)
        {
            items.Add((at, "browser.navigation", "navigation-completed", Navigation(token, url)));
            var records = new BrowserStateTests.Records(token, "doc-" + token)
                .Walk("finished-parsing", "first", false,
                    (1, null, "document", "#document", null),
                    (2, 1, "element", "HTML", null),
                    (3, 2, "element", "BODY", null),
                    (5, 3, "element", "MAIN", null),
                    (6, 5, "element", "H1", null),
                    (7, 6, "text", "#text", heading))
                .NamedAccessibility((1, "rootWebArea", title), (5, "main", ""), (6, "heading", heading), (7, "staticText", heading));
            var offset = 1;
            foreach (var item in records.Items)
            {
                items.Add((at + Second / 10 + offset++ * 1000, item.Channel, item.EventType, item.Payload));
            }
        }

        Page(1 * Second, "token-a", "https://shop.example.test/", "Example shop", "Welcome to the shop");
        Page(8 * Second, "token-b", "https://shop.example.test/basket", "Your basket", "Basket");
        if (frames)
        {
            // A captured frame every second from 1.5 s to 15.5 s.
            Directory.CreateDirectory(Path.Combine(_session, "frames", "desktop"));
            for (var index = 0; index < 15; index++)
            {
                var relative = $"frames/desktop/{index:D10}.png";
                File.WriteAllBytes(Path.Combine(_session, relative), FramePicture);
                var node = JsonNode.Parse(EvidenceSamples.Sample("graphics.desktop.frames", "desktop-frame"))!;
                node["path"] = relative;
                node["width"] = 1920;
                node["height"] = 1080;
                using var frame = JsonDocument.Parse(node.ToJsonString());
                items.Add((1_500_000_000 + index * Second, "graphics.desktop.frames", "desktop-frame", frame.RootElement.Clone()));
            }
        }

        foreach (var item in extra ?? [])
        {
            using var document = JsonDocument.Parse(item.Json);
            items.Add((item.Time, item.Channel, item.EventType, document.RootElement.Clone()));
        }

        var collector = Collector("test.summary", ["browser.navigation", "browser.dom", "browser.accessibility", "graphics.desktop.frames",
            .. (extra ?? []).Select(item => item.Channel).Distinct()]);
        var path = Path.Combine(_session, SessionDatabase.RecordingFileName);
        using var target = new RecordingFileBatchTarget(
            path,
            new Dictionary<string, string> { ["sessionKey"] = Path.GetFileName(_session), ["clockFrequency"] = "10000000" },
            new RecordingFileWriterOptions { ChunkBytes = 32 * 1024, ChunkInterval = TimeSpan.Zero });
        var sequences = new Dictionary<string, ulong>();
        var batch = items.OrderBy(item => item.Time).Select((item, index) =>
        {
            sequences.TryGetValue(item.Channel, out var sequence);
            sequences[item.Channel] = sequence + 1;
            var record = Event(Path.GetFileName(_session), collector, sequence + 1, item.Time, item.Channel, item.EventType) with { Payload = item.Payload };
            return new BufferedEvent(index + 1, record, item.Payload.GetRawText());
        }).ToArray();
        Assert.Empty(target.WriteAsync(new EventBatch(batch, [], []), Token).GetAwaiter().GetResult());
        target.Finish();
        var started = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        var manifest = new Recorder.Session.SessionManifest(
            "1.1", Path.GetFileName(_session), "completed", started, started.AddSeconds(20), 20 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64", new Recorder.Session.SessionRecordingConfiguration(true, true, true, true, 5, false, false),
            [], [], batch.Length, 0, null);
        File.WriteAllText(Path.Combine(_session, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
        return path;
    }

    // A picture of one pixel, as a captured frame's file.
    private static readonly byte[] FramePicture = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private async Task<(AnalysisCatalog Catalog, Guid Recording)> CatalogAsync(bool withFile = true)
    {
        await using var connection = await fixture.Server.DataSource.OpenConnectionAsync(Token);
        await using (var clear = new NpgsqlCommand(
            "DELETE FROM analysis_tasks; UPDATE recordings SET recording_status_id = 2 WHERE recording_status_id = 1; " +
            "INSERT INTO projects (name) VALUES ('Summary tests') ON CONFLICT (name) DO NOTHING",
            connection))
        {
            await clear.ExecuteNonQueryAsync(Token);
        }

        await using var command = new NpgsqlCommand(
            "INSERT INTO recordings (project_id, session_key, recording_status_id, started_utc, clock_frequency, " +
            "clock_origin_timestamp, os_description, framework_description, process_architecture, " +
            "capture_keyboard_and_mouse, capture_ui_automation, capture_foreground_window, capture_desktop_frames, " +
            "frames_per_second, capture_microphone, capture_system_audio) " +
            "SELECT project_id, $1, 2, now(), 10000000, 0, 'Windows', '.NET', 'X64', true, true, true, true, 5, false, false " +
            "FROM projects WHERE name = 'Summary tests' RETURNING recording_id",
            connection);
        command.Parameters.AddWithValue(Path.GetFileName(_session));
        var recording = (Guid)(await command.ExecuteScalarAsync(Token))!;
        if (withFile)
        {
            await using var file = new NpgsqlCommand(
                "INSERT INTO recording_files (recording_id, path, format, finished) VALUES ($1, $2, 'mcap', true)", connection);
            file.Parameters.AddWithValue(recording);
            file.Parameters.AddWithValue(SessionDatabase.RecordingFileName);
            await file.ExecuteNonQueryAsync(Token);
        }

        return (AnalysisCatalog.For(fixture.Server.DataSource), recording);
    }

    // The text of a request's user message.
    // Every JSON given to a model is on one line, so as not to spend its
    // context on indentation (owner, 2026-10-18): the request itself is
    // written compact, and no instruction or input holds JSON laid out over
    // lines. The answer's form goes as a grammar, not as text in the context.
    private static void AssertJsonOnOneLine(IEnumerable<string> bodies)
    {
        var indented = new System.Text.RegularExpressions.Regex(@"^\s*([\[\]{}],?|""[^""\n]*""\s*:.*)$", System.Text.RegularExpressions.RegexOptions.Multiline);
        foreach (var body in bodies)
        {
            Assert.DoesNotContain('\n', body);
            using var document = JsonDocument.Parse(body);
            foreach (var message in document.RootElement.GetProperty("messages").EnumerateArray())
            {
                var content = message.GetProperty("content");
                var texts = content.ValueKind == JsonValueKind.String
                    ? [content.GetString()!]
                    : content.EnumerateArray().Where(part => part.GetProperty("type").GetString() == "text").Select(part => part.GetProperty("text").GetString()!).ToList();
                foreach (var text in texts)
                {
                    Assert.False(indented.IsMatch(text), $"JSON laid out over lines in a request: {indented.Match(text).Value}");
                }
            }
        }
    }

    private static string Input(string body)
    {
        using var document = JsonDocument.Parse(body);
        var content = document.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content");
        return content.ValueKind == JsonValueKind.String
            ? content.GetString()!
            : string.Concat(content.EnumerateArray().Where(part => part.GetProperty("type").GetString() == "text").Select(part => part.GetProperty("text").GetString()));
    }

    // Answers each request with one statement in each section, citing the
    // first reference in the request's input.
    private static StandInEndpoint Endpoint()
    {
        var endpoint = new StandInEndpoint();
        endpoint.Answer = (_, body, _) =>
        {
            var input = Input(body);
            var reference = Reference().Match(input).Groups[1].Value;
            var sections = body.Contains("\"audience\"", StringComparison.Ordinal)
                ? RecordingSummaryKind.RecordingSections
                : RecordingSummaryKind.VisitSections;
            var output = new JsonObject();
            foreach (var section in sections)
            {
                output[section] = new JsonArray(new JsonArray(new JsonObject
                {
                    ["kind"] = "claim",
                    ["text"] = $"A statement about {section}.",
                    ["evidence"] = new JsonArray(reference)
                }));
            }

            return Task.FromResult(new ModelResponse(output.ToJsonString(), "stop", 300, 80, 400, 1200));
        };
        return endpoint;
    }

    private async Task<List<Dictionary<string, object?>>> RowsAsync(string sql, Guid id)
    {
        await using var connection = await fixture.Server.DataSource.OpenConnectionAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(id);
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

    [Fact]
    public async Task Summary_describes_each_visit_then_the_recording_and_writes_its_report()
    {
        WriteRecording();
        var (catalog, recording) = await CatalogAsync();
        await catalog.SaveCapabilitiesAsync(recording, "Uses NVDA with speech; no useful vision.", Token);
        var task = await catalog.QueueTaskAsync(
            recording, RecordingSummaryKind.Name, 1, new Dictionary<string, string> { [ModelCapabilities.Text] = "This PC" },
            sessionDirectory: _session, cancellationToken: Token);
        Assert.Equal(recording, await catalog.RecordingIdAsync(Path.GetFileName(_session), Token));
        Assert.Null(await catalog.RecordingIdAsync("not-a-session", Token));
        Assert.True(await catalog.AnyQueuedAsync(Token));

        await new AnalysisWorker(catalog, Settings(), [new RecordingSummaryKind()], Endpoint(), options: Fast).RunUntilEmptyAsync(Token);

        var state = (await RowsAsync("SELECT state, failure FROM analysis_tasks WHERE task_id = $1", task))[0];
        Assert.Equal((AnalysisTaskStates.Done, (object?)null), ((string)state["state"]!, state["failure"]));
        Assert.False(await catalog.AnyQueuedAsync(Token));
        var attempts = await RowsAsync(
            "SELECT step_key, outcome, prompt_template, input_references FROM analysis_attempts WHERE task_id = $1 ORDER BY started_utc", task);
        Assert.Equal(["visit-0001", "visit-0002", "recording"], attempts.Select(row => (string)row["step_key"]!));
        Assert.All(attempts, row => Assert.Equal(AttemptOutcomes.Accepted, row["outcome"]));
        Assert.Equal(RecordingSummaryKind.RecordingTemplate, attempts[2]["prompt_template"]);
        Assert.Contains("E1", (string[])attempts[0]["input_references"]!);
        var claims = await RowsAsync(
            "SELECT c.section, c.evidence FROM analysis_claims c JOIN analysis_attempts a ON a.attempt_id = c.attempt_id WHERE a.task_id = $1", task);
        Assert.Equal(9, claims.Count);

        var report = Assert.Single(await RowsAsync("SELECT file_name, relative_path, sha256 FROM analysis_reports WHERE task_id = $1", task));
        Assert.Equal(RecordingSummaryKind.FileName, report["file_name"]);
        var path = Path.Combine(_session, (string)report["relative_path"]!);
        Assert.Equal(Path.Combine(_session, "analysis", task.ToString("D"), "summary.md"), path);
        var bytes = await File.ReadAllBytesAsync(path, Token);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), report["sha256"]);
        Assert.Equal(
            [((string)report["file_name"]!, (string)report["relative_path"]!, (string)report["sha256"]!)],
            await catalog.ReportsAsync(task, Token));
        var read = ReportDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        Assert.Null(read.Evidence["E1"].Nanoseconds);
        Assert.All(read.Blocks.SelectMany(block => block.References).Distinct(),
            reference => Assert.True(read.Evidence.ContainsKey(reference), reference));
        var text = await File.ReadAllTextAsync(path, Token);
        Assert.StartsWith($"# Summary of recording {Path.GetFileName(_session)}", text, StringComparison.Ordinal);
        Assert.Contains("> Uses NVDA with speech; no useful vision. [E1]", text, StringComparison.Ordinal);
        Assert.Contains("### 1. Example shop, 00:00:01 to 00:00:08", text, StringComparison.Ordinal);
        Assert.Contains("### 2. Your basket, 00:00:08 to", text, StringComparison.Ordinal);
        Assert.Contains("A statement about session. [E1]", text, StringComparison.Ordinal);
        var body = text.Split("## Evidence")[0];
        var list = text.Split("## Evidence")[1].Split("## How this was made")[0];
        Assert.All(Reference().Matches(body).Select(match => match.Groups[1].Value).Distinct(),
            reference => Assert.Contains($"| {reference} |", list, StringComparison.Ordinal));
        Assert.Contains("- Model SHA-256: " + new string('b', 64) + " (as the profile states it, not checked).", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Summary_gives_the_recorders_facts_to_the_model()
    {
        WriteRecording();
        var (catalog, recording) = await CatalogAsync();
        await catalog.QueueTaskAsync(
            recording, RecordingSummaryKind.Name, 1, new Dictionary<string, string> { [ModelCapabilities.Text] = "This PC" },
            sessionDirectory: _session, cancellationToken: Token);
        var endpoint = Endpoint();

        await new AnalysisWorker(catalog, Settings(), [new RecordingSummaryKind()], endpoint, options: Fast).RunUntilEmptyAsync(Token);

        AssertJsonOnOneLine(endpoint.Bodies);
        var first = Input(endpoint.Bodies.First());
        Assert.Contains("The participant's capabilities are unknown", first, StringComparison.Ordinal);
        Assert.Contains("Navigation committed, a new document, https://shop.example.test/.", first, StringComparison.Ordinal);
        Assert.Contains("Title: \"Example shop\".", first, StringComparison.Ordinal);
        Assert.Contains("Landmarks: main.", first, StringComparison.Ordinal);
        Assert.Contains("level 1: \"Welcome to the shop\"", first, StringComparison.Ordinal);
        Assert.Contains("The text of the main landmark, in full: \"Welcome to the shop\"", first, StringComparison.Ordinal);
        Assert.DoesNotContain("Basket", first, StringComparison.Ordinal);
        var last = Input(endpoint.Bodies.Last());
        Assert.Contains("These are inferences, not facts", last, StringComparison.Ordinal);
        Assert.Contains("title \"Your basket\"", last, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Summary_without_a_session_folder_fails_saying_why()
    {
        var (catalog, recording) = await CatalogAsync();
        var task = await catalog.QueueTaskAsync(
            recording, RecordingSummaryKind.Name, 1, new Dictionary<string, string> { [ModelCapabilities.Text] = "This PC" }, cancellationToken: Token);

        await new AnalysisWorker(catalog, Settings(), [new RecordingSummaryKind()], Endpoint(), options: Fast).RunUntilEmptyAsync(Token);

        var state = (await RowsAsync("SELECT state, failure FROM analysis_tasks WHERE task_id = $1", task))[0];
        Assert.Equal(AnalysisTaskStates.Failed, state["state"]);
        Assert.Contains("no session folder", (string)state["failure"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Summary_of_a_recording_without_a_recording_file_fails_saying_why()
    {
        Directory.CreateDirectory(_session);
        var (catalog, recording) = await CatalogAsync(withFile: false);
        var task = await catalog.QueueTaskAsync(
            recording, RecordingSummaryKind.Name, 1, new Dictionary<string, string> { [ModelCapabilities.Text] = "This PC" },
            sessionDirectory: _session, cancellationToken: Token);

        await new AnalysisWorker(catalog, Settings(), [new RecordingSummaryKind()], Endpoint(), options: Fast).RunUntilEmptyAsync(Token);

        var state = (await RowsAsync("SELECT state, failure FROM analysis_tasks WHERE task_id = $1", task))[0];
        Assert.Equal(AnalysisTaskStates.Failed, state["state"]);
        Assert.Contains("recording files only", (string)state["failure"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Capability_descriptions_keep_the_earlier_ones()
    {
        var (catalog, recording) = await CatalogAsync(withFile: false);

        await catalog.SaveCapabilitiesAsync(recording, "First description.", Token);
        await catalog.SaveCapabilitiesAsync(recording, "  Second description.  ", Token);

        Assert.Equal(["Second description.", "First description."], (await catalog.CapabilitiesAsync(recording, Token)).Select(item => item.Description));
    }
}

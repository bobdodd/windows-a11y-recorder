using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using Recorder.Recreation;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// Slice 4h (protocol 0.54): the script-parsed and script-text records the
/// bridge writes, their validation, the scripts of a document at a recording
/// time with their texts by digest, the rows and links the evidence panel
/// shows, and the recorder's answer for a script's text.
/// </summary>
public sealed class RecordedScriptTests : IDisposable
{
    private const string Token = "F8543F87A3AF6713E6DEADA760E49A6C";
    private const string DocumentKey = Token + " browser-1 3440 dom-document-1";
    private const string Inline = "setTimeout(function () {\n  tick();\n}, 1500);\n";
    private const string Module = "export const answer = 42;\r\nconsole.log(answer);\n";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recorded-scripts-" + Guid.NewGuid().ToString("N"));

    public RecordedScriptTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Context(string token = Token, string? world = "\"world-0\"") => $$"""
        {"browserInstanceId":"browser-1","processId":3440,"processType":"renderer","profileId":null,
         "browserContextId":null,"pageId":null,"frameId":null,"documentId":"dom-document-1",
         "executionWorldId":{{world ?? "null"}},"documentToken":"{{token}}"}
        """;

    private static string ResourceContext => """
        {"browserInstanceId":"browser-1","processId":3440,"processType":"renderer","profileId":null,
         "browserContextId":null,"pageId":null,"frameId":null,"documentId":null,
         "executionWorldId":null,"documentToken":null}
        """;

    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Q(string? text) => text is null ? "null" : JsonSerializer.Serialize(text);

    // As the bridge writes it.
    public static string Parsed(
        string scriptId = "12",
        string kind = "classic",
        string? text = Inline,
        string? url = null,
        string? sourceUrl = null,
        string? sourceMapUrl = null,
        int? line = 55,
        int? column = 11,
        string? evalFrom = null,
        bool compileError = false,
        bool textRecorded = true,
        string token = Token,
        string worldKind = "main") => $$"""
        {"context":{{Context(token, worldKind == "main" ? "\"world-0\"" : "\"world-7\"")}},
         "world":{"kind":"{{worldKind}}","blinkWorldId":{{(worldKind == "main" ? 0 : 7)}},"name":null,"stableId":null},
         "scriptId":"{{scriptId}}","kind":"{{kind}}","url":{{Q(url)}},"sourceUrl":{{Q(sourceUrl)}},
         "sourceMapUrl":{{Q(sourceMapUrl)}},"line":{{(line?.ToString() ?? "null")}},"column":{{(column?.ToString() ?? "null")}},
         "evalFromScriptId":{{Q(evalFrom)}},"compileError":{{(compileError ? "true" : "false")}},
         "digest":"{{Digest(text ?? "")}}","size":"{{Encoding.UTF8.GetByteCount(text ?? "")}}",
         "textRecorded":{{(textRecorded ? "true" : "false")}}}
        """;

    public static string ScriptText(string text) => $$"""
        {"context":{{ResourceContext}},"digest":"{{Digest(text)}}","size":"{{Encoding.UTF8.GetByteCount(text)}}",
         "bytes":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes(text))}}"}
        """;

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static List<EventValidationIssue> Validate(string eventType, string json)
    {
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(BrowserEvidenceChannels.Script, eventType, J(json), 100, issues);
        return issues;
    }

    [Theory]
    [InlineData("script-parsed")]
    [InlineData("script-text")]
    public void TheProtocolReadsTheRecordsAsTheBridgeWritesThem(string eventType)
    {
        var json = eventType == "script-parsed" ? Parsed() : ScriptText(Inline);
        BrowserProtocol.ValidateEvidencePayload(BrowserEvidenceChannels.Script, eventType, J(json));

        var payload = JsonNode.Parse(json)!.AsObject();
        payload["undeclared"] = 1;
        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(BrowserEvidenceChannels.Script, eventType, J(payload.ToJsonString())));
    }

    [Fact]
    public void TheValidatorAcceptsTheRecordsAndRefusesMalformedOnes()
    {
        Assert.True(EventPayloadValidator.IsBuiltInChannel(BrowserEvidenceChannels.Script));
        Assert.Empty(Validate("script-parsed", Parsed()));
        Assert.Empty(Validate("script-parsed", Parsed(kind: "module", text: Module, url: "http://127.0.0.1:8765/module.js", line: 1, column: 1)));
        Assert.Empty(Validate("script-parsed", Parsed(scriptId: "20", kind: "eval", evalFrom: "12", line: null, column: null)));
        Assert.Empty(Validate("script-parsed", Parsed(kind: "function", compileError: true, textRecorded: false)));
        Assert.Empty(Validate("script-text", ScriptText(Inline)));
        Assert.NotEmpty(Validate("script-parsed", Parsed(kind: "wasm")));
        Assert.NotEmpty(Validate("script-parsed", Parsed(line: 0)));
        Assert.Contains(Validate("script-parsed", Parsed(evalFrom: "9")), issue => issue.Code == "browser-script-parsed-eval-from");
        var digest = JsonNode.Parse(Parsed())!.AsObject();
        digest["digest"] = "ABC";
        Assert.NotEmpty(Validate("script-parsed", digest.ToJsonString()));
        Assert.Empty(Validate("collector-omission", """{"reason":"browser-queue-full","count":1}"""));
    }

    private static RecorderEvent Record(long nanoseconds, string eventType, string json, ulong sequence = 1) =>
        Event("session-a", Collector("test.browser", BrowserEvidenceChannels.Script), sequence, nanoseconds,
            BrowserEvidenceChannels.Script, eventType) with
        {
            Payload = J(json),
        };

    private static RecordedScripts Read(long at, params (long Time, string Type, string Json)[] records)
    {
        var reader = new RecordedScriptReader(DocumentKey);
        var texts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var sequence = 1UL;
        foreach (var record in records.Where(item => item.Time <= at))
        {
            if (reader.Add(Record(record.Time, record.Type, record.Json, sequence++)))
            {
                var payload = J(record.Json);
                texts[payload.GetProperty("digest").GetString()!] = Convert.FromBase64String(payload.GetProperty("bytes").GetString()!);
            }
        }
        return reader.Build(digest => texts.GetValueOrDefault(digest));
    }

    [Fact]
    public void TheScriptsOfTheDocumentAreThoseRecordedAtOrBeforeTheTime()
    {
        var records = new (long, string, string)[]
        {
            (1_000, "script-text", ScriptText(Inline)),
            (1_000, "script-parsed", Parsed()),
            // Another document's script, and a repeat of the first, are not listed.
            (1_100, "script-parsed", Parsed(scriptId: "13", token: "00000000000000000000000000000000")),
            (1_200, "script-parsed", Parsed(line: 99)),
            // Eval code with its caller, whose text was met before.
            (1_300, "script-parsed", Parsed(scriptId: "20", kind: "eval", evalFrom: "12", line: null, column: null)),
            (2_000, "script-text", ScriptText(Module)),
            (2_000, "script-parsed", Parsed(scriptId: "21", kind: "module", text: Module, url: "http://127.0.0.1:8765/module.js", line: 1, column: 1)),
            (3_000, "script-parsed", Parsed(scriptId: "22", kind: "classic", text: "let x = ;", compileError: true, textRecorded: false)),
        };
        var early = Read(1_500, records);
        Assert.True(early.Recorded);
        Assert.Equal(["12", "20"], early.Scripts.Select(item => item.ScriptId));
        Assert.Equal(55, early.Scripts[0].Line);
        Assert.Equal("12", early.Scripts[1].EvalFromScriptId);
        Assert.Equal(Inline, early.Text(early.Scripts[0].Digest));
        // The same source is one text.
        Assert.Equal(early.Scripts[0].Digest, early.Scripts[1].Digest);
        Assert.False(early.HasText(Digest(Module)));

        var later = Read(3_500, records);
        Assert.Equal(["12", "20", "21", "22"], later.Scripts.Select(item => item.ScriptId));
        Assert.Equal(Module, later.Text(later.Scripts[2].Digest));
        Assert.True(later.Scripts[3].CompileError);
        Assert.False(later.HasText(later.Scripts[3].Digest));
        Assert.Null(later.Text(later.Scripts[3].Digest));
    }

    [Fact]
    public void ATextWhoseBytesDoNotMatchItsDigestIsNotUsed()
    {
        var reader = new RecordedScriptReader(DocumentKey);
        Assert.True(reader.Add(Record(1, "script-text", ScriptText(Inline))));
        reader.Add(Record(1, "script-parsed", Parsed(), 2));
        var scripts = reader.Build(_ => Encoding.UTF8.GetBytes("not the text"));
        Assert.True(scripts.HasText(scripts.Scripts[0].Digest));
        Assert.Null(scripts.Text(scripts.Scripts[0].Digest));
    }

    [Fact]
    public void ARecordingBeforeProtocol054HasNoScripts()
    {
        var reader = new RecordedScriptReader(DocumentKey);
        Assert.Same(RecordedScripts.None, reader.Build(_ => null));
        Assert.False(RecordedScripts.None.Recorded);
        var evidence = RecordedEvidence.Create(RecordedPageTests.State(), "https://example.test/", 1, 1, "basis",
            new RecreationFidelity("not-checked", "", []), []);
        Assert.Contains("protocol 0.54", evidence.ScriptsNotRead);
        Assert.Empty(evidence.Scripts);
        Assert.Empty(evidence.ScriptNotes);
    }

    private static string Compiled(string scriptId, long elementNodeId) => $$"""
        {"context":{{Context(world: null)}},"scriptId":"{{scriptId}}","kind":"classic","elementNodeId":{{elementNodeId}},
         "attributeName":null,"url":null,"line":55,"column":11}
        """;

    [Fact]
    public void TheEvidenceListsTheScriptsJoinedToTheirElements()
    {
        var state = RecordedPageTests.State();
        Assert.True(state.Script.Apply(1, 10, "script-compiled", J(Compiled("12", 14))));
        var scripts = Read(5_000,
            (1_000, "script-text", ScriptText(Inline)),
            (1_000, "script-parsed", Parsed()),
            (1_300, "script-parsed", Parsed(scriptId: "20", kind: "eval", evalFrom: "12", line: null, column: null)),
            (1_400, "script-parsed", Parsed(scriptId: "30", kind: "classic", text: "x", textRecorded: false, worldKind: "isolated")));
        var evidence = RecordedEvidence.Create(state, "https://example.test/", 5_000, 5_000, "basis",
            new RecreationFidelity("not-checked", "", []), [], null, scripts);
        Assert.Null(evidence.ScriptsNotRead);
        Assert.NotEmpty(evidence.ScriptNotes);
        Assert.Equal(3, evidence.Scripts.Count);
        var inline = evidence.Scripts[0];
        Assert.Equal("the page", inline.Owner);
        Assert.Equal("the inline script element", inline.Element);
        Assert.Equal(RecordedPaths.Of(state.Dom!, 14)!.Display, inline.ElementPath!.Display);
        Assert.Equal(Digest(Inline), inline.Digest);
        Assert.Equal(Encoding.UTF8.GetByteCount(Inline), inline.Size);
        var eval = evidence.Scripts[1];
        Assert.Null(eval.Element);
        Assert.Equal("script 12", eval.EvalFrom);
        var isolated = evidence.Scripts[2];
        Assert.StartsWith("an isolated world", isolated.Owner, StringComparison.Ordinal);
        // A script whose text is not recorded has no digest to read it by.
        Assert.Null(isolated.Digest);
    }

    [Fact]
    public void ATimersCallerAndCallbackLinkToTheirLinesInTheRecordedText()
    {
        var script = new ScriptDocumentState();
        script.Apply(1, 1, "script-compiled", J(TimerOriginTests.ScriptCompiled));
        script.Apply(2, 2, "timer-scheduled", J("""
            {"timerId":"timer-1","timerKind":"timeout","requestedDelayMilliseconds":1500,"effectiveDelayMilliseconds":1500,
             "callbackLocation":{"scriptId":"12","url":"http://127.0.0.1:8000/index.html","line":76,"column":16,"functionName":null,"sourceHash":null}}
            """));
        script.Apply(3, 2, "timer-origin", J(TimerOriginTests.TimerOrigin));
        var tree = RecordedPageTests.State().Dom!;
        var scripts = Read(5_000,
            (1_000, "script-text", ScriptText(Inline)),
            (1_000, "script-parsed", Parsed()));
        var origin = TimerOrigins.Of(script.Timers["timer-1"], script, tree, scripts);
        Assert.Equal(new RecordedSourceLink("12", Digest(Inline), 76, 5), origin.CallerSource);
        Assert.Equal(new RecordedSourceLink("12", Digest(Inline), 76, 16), origin.CallbackSource);
        // Without the scripts there is nothing to link to.
        var unlinked = TimerOrigins.Of(script.Timers["timer-1"], script, tree);
        Assert.Null(unlinked.CallerSource);
        Assert.Null(unlinked.CallbackSource);
    }

    private async Task<string> WriteAsync(params (long Time, string Type, string Json)[] records)
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector("test.browser", BrowserEvidenceChannels.Script);
        var events = records.Select((record, index) =>
                Event("session-a", collector, (ulong)index + 1, record.Time, BrowserEvidenceChannels.Script, record.Type) with
                {
                    Payload = J(record.Json),
                })
            .ToArray();
        using (var target = new RecordingFileBatchTarget(
            path,
            new Dictionary<string, string> { ["sessionKey"] = "session-a", ["clockFrequency"] = "10000000" },
            new RecordingFileWriterOptions { ChunkBytes = 1024 }))
        {
            var batch = events.Select((record, index) => new BufferedEvent(index, record, record.Payload.GetRawText())).ToArray();
            Assert.Empty(await target.WriteAsync(new EventBatch(batch, [], []), TestContext.Current.CancellationToken));
            target.Finish();
        }
        return path;
    }

    [Fact]
    public async Task TheScriptsAreReadFromTheRecordingFileAndTheirTextOnlyWhenAsked()
    {
        var path = await WriteAsync(
            (1_000_000_000, "script-text", ScriptText(Inline)),
            (1_000_000_000, "script-parsed", Parsed()),
            (2_000_000_000, "script-text", ScriptText(Module)),
            (2_000_000_000, "script-parsed", Parsed(scriptId: "21", kind: "module", text: Module, url: "http://127.0.0.1:8765/module.js", line: 1, column: 1)));
        using var reader = RecordingFileReader.Open(path);
        var early = RecordingFileScripts.Read(reader, DocumentKey, 1_500_000_000, TestContext.Current.CancellationToken);
        Assert.Equal(["12"], early.Scripts.Select(item => item.ScriptId));
        Assert.Equal(Inline, early.Text(Digest(Inline)));
        Assert.Null(early.Text(Digest(Module)));
        var later = RecordingFileScripts.Read(reader, DocumentKey, 2_500_000_000, TestContext.Current.CancellationToken);
        Assert.Equal(Module, later.Text(Digest(Module)));

        // Through the resources, as the recreation reads them.
        using var resources = RecordingFileResources.Read(path, DocumentKey, 2_500_000_000, TestContext.Current.CancellationToken);
        Assert.Equal(2, resources.Scripts.Scripts.Count);
        Assert.Equal(Inline, resources.Scripts.Text(Digest(Inline)));
    }

    [Fact]
    public async Task TheRecorderAnswersAListedScriptsTextAsPlainText()
    {
        var scripts = Read(5_000,
            (1_000, "script-text", ScriptText(Inline)),
            (1_000, "script-parsed", Parsed()),
            (1_100, "script-text", ScriptText(Module)));
        var content = new RecreationContent("<p>page</p>", FixedRecreation.Create().Evidence, "n0nce")
        {
            Scripts = scripts,
        };
        await using var server = await RecreationServer.StartAsync(content, TestContext.Current.CancellationToken);
        using var client = new HttpClient();
        var response = await client.GetAsync(server.BaseAddress + RecreationServer.ScriptResourcePrefix + Digest(Inline), TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Inline, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // A text no listed script has, and a malformed digest, are refused.
        var unlisted = await client.GetAsync(server.BaseAddress + RecreationServer.ScriptResourcePrefix + Digest(Module), TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, unlisted.StatusCode);
        var malformed = await client.GetAsync(server.BaseAddress + RecreationServer.ScriptResourcePrefix + "not-a-digest", TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, malformed.StatusCode);
    }

    [Fact]
    public void ThePanelListsTheScriptsAndOpensTheirTextAsText()
    {
        var assembly = typeof(RecreationServer).Assembly;
        string Resource(string name)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var panel = Resource("EvidencePanel.panel.js");
        var html = Resource("EvidencePanel.panel.html");
        Assert.Contains("<section id=\"viewer\" hidden></section>", html, StringComparison.Ordinal);
        Assert.Contains("\"Scripts\",", panel, StringComparison.Ordinal);
        Assert.Contains("evidence.scriptsNotRead", panel, StringComparison.Ordinal);
        Assert.Contains("`${recorderBase}script/${digest}`", panel, StringComparison.Ordinal);
        // The text is put into the page as text, never as markup, and closing
        // the viewer returns focus.
        Assert.Contains("element(\"code\", value === \"\" ? \" \" : value)", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", panel, StringComparison.Ordinal);
        Assert.Contains("opener.focus();", panel, StringComparison.Ordinal);
        Assert.Contains("origin.callerSource", panel, StringComparison.Ordinal);
        Assert.Contains("origin.callbackSource", panel, StringComparison.Ordinal);
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;
using Recorder.Recreation;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// Slice 4f (protocol 0.52): who scheduled each timer. The timer-origin and
/// script-compiled records the bridge writes, their validation, the script
/// state that keeps them, and the join the evidence panel shows.
/// </summary>
public sealed class TimerOriginTests
{
    private const string Context = """
        {"browserInstanceId":"browser-1","processId":3440,"processType":"renderer","profileId":null,
         "browserContextId":null,"pageId":null,"frameId":null,"documentId":"dom-document-1",
         "executionWorldId":"world-0","documentToken":null}
        """;

    private const string DocumentContext = """
        {"browserInstanceId":"browser-1","processId":3440,"processType":"renderer","profileId":null,
         "browserContextId":null,"pageId":null,"frameId":null,"documentId":"dom-document-1",
         "executionWorldId":null,"documentToken":"F8543F87A3AF6713E6DEADA760E49A6C"}
        """;

    // As the bridge writes it: an inline script's call, with the callback
    // defined in the same script.
    public static readonly string TimerOrigin = $$"""
        {"context":{{Context}},"timerId":"timer-1",
         "world":{"kind":"main","blinkWorldId":0,"name":null,"stableId":null},
         "stack":[{"scriptId":"12","url":"http://127.0.0.1:8000/index.html","functionName":null,"line":76,"column":5,"isEval":false}],
         "handler":"function"}
        """;

    public static readonly string ScriptCompiled = $$"""
        {"context":{{DocumentContext}},"scriptId":"12","kind":"classic","elementNodeId":30,
         "attributeName":null,"url":null,"line":55,"column":11}
        """;

    public static readonly string AttributeCompiled = $$"""
        {"context":{{DocumentContext}},"scriptId":"14","kind":"event-handler-attribute","elementNodeId":31,
         "attributeName":"onclick","url":"http://127.0.0.1:8000/index.html","line":40,"column":1}
        """;

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("timer-origin")]
    [InlineData("script-compiled")]
    public void TheProtocolReadsTheRecordsAsTheBridgeWritesThem(string eventType)
    {
        var json = eventType == "timer-origin" ? TimerOrigin : ScriptCompiled;
        BrowserProtocol.ValidateEvidencePayload(BrowserEvidenceChannels.Timer, eventType, J(json));

        var payload = JsonNode.Parse(json)!.AsObject();
        payload["undeclared"] = 1;
        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(BrowserEvidenceChannels.Timer, eventType, J(payload.ToJsonString())));
    }

    private static List<EventValidationIssue> Validate(string eventType, string json)
    {
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(BrowserEvidenceChannels.Timer, eventType, J(json), 100, issues);
        return issues;
    }

    [Fact]
    public void TheValidatorAcceptsTheRecords()
    {
        Assert.Empty(Validate("timer-origin", TimerOrigin));
        Assert.Empty(Validate("script-compiled", ScriptCompiled));
        Assert.Empty(Validate("script-compiled", AttributeCompiled));
    }

    [Fact]
    public void ARecordWithNoScriptRunningHasNoWorldAndNoFrames()
    {
        var payload = JsonNode.Parse(TimerOrigin)!.AsObject();
        payload["world"] = null;
        payload["stack"] = new JsonArray();
        payload["context"]!["executionWorldId"] = null;
        Assert.Empty(Validate("timer-origin", payload.ToJsonString()));
    }

    [Fact]
    public void TheValidatorRejectsMalformedRecords()
    {
        var handler = JsonNode.Parse(TimerOrigin)!.AsObject();
        handler["handler"] = "code";
        Assert.Contains(Validate("timer-origin", handler.ToJsonString()), issue => issue.Code == "payload-property-invalid");

        var frames = JsonNode.Parse(TimerOrigin)!.AsObject();
        var stack = new JsonArray();
        for (var index = 0; index < 17; index++)
        {
            stack.Add(JsonNode.Parse("""{"scriptId":"1","url":null,"functionName":null,"line":1,"column":1,"isEval":false}"""));
        }
        frames["stack"] = stack;
        Assert.Contains(Validate("timer-origin", frames.ToJsonString()), issue => issue.Code == "browser-timer-origin-stack");

        // A world named in the record must be the world of its context.
        var world = JsonNode.Parse(TimerOrigin)!.AsObject();
        world["context"]!["executionWorldId"] = "world-3";
        Assert.Contains(Validate("timer-origin", world.ToJsonString()), issue => issue.Code == "browser-execution-world-identity");

        var attribute = JsonNode.Parse(ScriptCompiled)!.AsObject();
        attribute["attributeName"] = "onclick";
        Assert.Contains(Validate("script-compiled", attribute.ToJsonString()), issue => issue.Code == "browser-script-compiled-attribute");

        var missing = JsonNode.Parse(AttributeCompiled)!.AsObject();
        missing["attributeName"] = null;
        Assert.Contains(Validate("script-compiled", missing.ToJsonString()), issue => issue.Code == "browser-script-compiled-attribute");

        var kind = JsonNode.Parse(ScriptCompiled)!.AsObject();
        kind["kind"] = "eval";
        Assert.Contains(Validate("script-compiled", kind.ToJsonString()), issue => issue.Code == "payload-property-invalid");
    }

    private static JsonElement Scheduled(string id) => J($$$"""
        {"timerId":"{{{id}}}","timerKind":"timeout","requestedDelayMilliseconds":1500,"effectiveDelayMilliseconds":1500,
         "callbackLocation":{"scriptId":"12","url":"http://127.0.0.1:8000/index.html","line":76,"column":16,"functionName":null,"sourceHash":null}}
        """);

    [Fact]
    public void TheScriptStateKeepsTheOriginAndTheScriptsAcrossSnapshots()
    {
        var state = new BrowserDocumentState("token-a dom-document-1");
        Assert.True(state.Script.Apply(1, 10, "script-compiled", J(ScriptCompiled)));
        Assert.True(state.Script.Apply(2, 20, "timer-scheduled", Scheduled("timer-1")));
        Assert.True(state.Script.Apply(3, 20, "timer-origin", J(TimerOrigin)));
        // An origin for a timer that is not pending changes nothing.
        Assert.True(state.Script.Apply(4, 30, "timer-origin", J(TimerOrigin.Replace("timer-1", "timer-9"))));

        Assert.Equal("12", state.Script.Scripts.Keys.Single());
        Assert.Equal("timer-1", state.Script.Timers["timer-1"].Origin!.Value.GetProperty("timerId").GetString());
        Assert.False(state.Script.Timers.ContainsKey("timer-9"));

        var read = BrowserStateSnapshot.Read(BrowserStateSnapshot.Serialize(state));
        Assert.Equal("12", read.Script.Scripts.Keys.Single());
        Assert.NotNull(read.Script.Timers["timer-1"].Origin);
    }

    // html > body > (button#31, script#30)
    private static DomDocumentTree Tree()
    {
        var tree = new DomDocumentTree();
        void Add(long id, long? parent, string type, string? name)
        {
            tree.Nodes.Add(id, new DomNode(id) { ParentId = parent, NodeType = type, NodeName = name });
            if (parent is { } p)
            {
                tree.Nodes[p].Children.Add(id);
            }
        }
        Add(1, null, "document", "#document");
        Add(2, 1, "element", "HTML");
        Add(3, 2, "element", "BODY");
        Add(31, 3, "element", "BUTTON");
        Add(30, 3, "element", "SCRIPT");
        return tree;
    }

    private static RecordedTimerOrigin Join(string? origin, params string[] scripts)
    {
        var script = new ScriptDocumentState();
        foreach (var record in scripts)
        {
            script.Apply(1, 1, "script-compiled", J(record));
        }
        script.Apply(2, 2, "timer-scheduled", Scheduled("timer-1"));
        if (origin is not null)
        {
            script.Apply(3, 2, "timer-origin", J(origin));
        }
        return TimerOrigins.Of(script.Timers["timer-1"], script, Tree());
    }

    [Fact]
    public void AnInlineScriptsTimerNamesTheScriptElementAndItsPath()
    {
        var origin = Join(TimerOrigin, ScriptCompiled);

        Assert.Equal("the page", origin.Owner);
        Assert.Equal("the inline script element", origin.Element);
        Assert.Equal("/html[1]/body[1]/script[1]", origin.ElementPath!.Display);
        Assert.Null(origin.ElementNote);
        Assert.Equal("http://127.0.0.1:8000/index.html:76:5, at the top level or in an anonymous function", origin.Caller);
        Assert.Equal("http://127.0.0.1:8000/index.html:76:16", origin.Callback);
        Assert.Equal("function", origin.Handler);
    }

    [Fact]
    public void AnExternalScriptIsNamedByItsAddress()
    {
        var origin = Join(TimerOrigin, ScriptCompiled.Replace("\"url\":null", "\"url\":\"http://127.0.0.1:8000/app.js\""));
        Assert.Equal("the script element loading http://127.0.0.1:8000/app.js", origin.Element);
    }

    [Fact]
    public void AnAttributeHandlerFoundBelowEvalCodeIsNamedWithANote()
    {
        var stack = """
            "stack":[{"scriptId":"20","url":null,"functionName":null,"line":1,"column":1,"isEval":true},
                     {"scriptId":"14","url":"http://127.0.0.1:8000/index.html","functionName":"onclick","line":40,"column":1,"isEval":false}]
            """;
        var record = TimerOrigin[..TimerOrigin.IndexOf("\"stack\"", StringComparison.Ordinal)] + stack + ",\"handler\":\"function\"}";

        var origin = Join(record, ScriptCompiled, AttributeCompiled);

        Assert.Equal("the onclick attribute of the element", origin.Element);
        Assert.Equal("/html[1]/body[1]/button[1]", origin.ElementPath!.Display);
        Assert.Contains("stack frame 2", origin.ElementNote);
        Assert.Contains("eval code", origin.ElementNote);
        Assert.Equal("a script with no address:1:1, at the top level or in an anonymous function, eval code", origin.Caller);
    }

    [Fact]
    public void AnElementRemovedBeforeTheFrameIsNamedByItsNodeId()
    {
        var origin = Join(TimerOrigin, ScriptCompiled.Replace("\"elementNodeId\":30", "\"elementNodeId\":99"));
        Assert.Null(origin.ElementPath);
        Assert.Equal("the inline script element, node 99, which is not in the document at the frame", origin.Element);
    }

    [Fact]
    public void AnIsolatedWorldIsNamedWithItsNameAndId()
    {
        var record = TimerOrigin
            .Replace("\"kind\":\"main\",\"blinkWorldId\":0,\"name\":null,\"stableId\":null",
                "\"kind\":\"isolated\",\"blinkWorldId\":3,\"name\":\"Helper\",\"stableId\":\"abcdefghijklmnop\"")
            .Replace("\"executionWorldId\":\"world-0\"", "\"executionWorldId\":\"world-3\"");
        var origin = Join(record);
        Assert.Equal("an isolated world, such as an extension's content script: Helper, ID abcdefghijklmnop", origin.Owner);
        Assert.Null(origin.Element);
        Assert.Equal("No frame of the stack has a script element or attribute recorded.", origin.ElementNote);
    }

    [Fact]
    public void AnExtensionScriptInThePagesWorldIsNamedByItsAddress()
    {
        var origin = Join(TimerOrigin.Replace("http://127.0.0.1:8000/index.html", "chrome-extension://abc/inject.js"));
        Assert.Equal("an extension's script in the page's own world, chrome-extension://abc/inject.js", origin.Owner);
    }

    [Fact]
    public void ATimerScheduledWithNoScriptRunningSaysSo()
    {
        var payload = JsonNode.Parse(TimerOrigin)!.AsObject();
        payload["world"] = null;
        payload["stack"] = new JsonArray();
        var origin = Join(payload.ToJsonString());
        Assert.Equal("no script was running", origin.Owner);
        Assert.Null(origin.Caller);
        Assert.Null(origin.ElementNote);
    }

    [Fact]
    public void ARecordingBeforeProtocol052SaysTheOwnerIsNotRecorded()
    {
        var origin = Join(null, ScriptCompiled);
        Assert.Equal(TimerOrigins.NotRecorded, origin.Owner);
        Assert.Null(origin.Element);
    }

    [Fact]
    public void ThePanelReadsTheOriginAsScheduledBy()
    {
        var timer = RecordedEvidence.Timer(
            new PendingTimer(Scheduled("timer-1"), 0, null),
            0,
            new RecordedTimerOrigin("the page", "the inline script element", null, null, null, null, "function"));
        var json = JsonSerializer.Serialize(timer, RecreationServer.EvidenceJson);
        Assert.Contains("\"scheduledBy\":{\"owner\":\"the page\"", json);
    }
}

using System.Text.Json;
using Recorder.Collectors.Browser;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// Protocol 0.60's accessibility request records (browser.accessibility):
/// the payload rules, the protocol's typed shapes, and the player's outcomes
/// and summaries, including through the playback index. See
/// docs/architecture/accessibility-requests.md.
/// </summary>
public sealed class AccessibilityRequestTests
{
    private const long Millisecond = 1_000_000;
    private const long Second = 1_000_000_000;

    private const string BrowserContext =
        "{'browserInstanceId':'browser-1','processId':4000,'processType':'browser','profileId':null,'browserContextId':null,'pageId':null,'frameId':null,'documentId':null,'executionWorldId':null,'documentToken':null}";

    private const string FrameContext =
        "{'browserInstanceId':'browser-1','processId':4000,'processType':'browser','profileId':null,'browserContextId':null,'pageId':'frame-1','frameId':'frame-1','documentId':null,'executionWorldId':null,'documentToken':'TOKEN-1'}";

    private const string RendererContext =
        "{'browserInstanceId':'browser-1','processId':4100,'processType':'renderer','profileId':null,'browserContextId':null,'pageId':null,'frameId':null,'documentId':null,'executionWorldId':null,'documentToken':'TOKEN-1'}";

    private const string Heading =
        "{'treeId':'TREE-1','nodeId':652,'role':'heading','name':'Contact us','bounds':{'x':480,'y':290,'width':240,'height':32}}";

    private const string Link =
        "{'treeId':'TREE-1','nodeId':700,'role':'link','name':'Donate','bounds':{'x':480,'y':330,'width':80,'height':20}}";

    private static string J(string json) => json.Replace('\'', '"');

    private static string HitTest(string basis, bool precise, string? answer, int x = 512, int y = 300) =>
        J($"{{'context':{BrowserContext},'rootTreeId':'TREE-1','point':{{'x':{x},'y':{y}}},'basis':'{basis}','preciseRequested':{(precise ? "true" : "false")},'answer':{answer ?? "null"}}}");

    private static string Result(string node) => J($"{{'context':{BrowserContext},'result':{node}}}");

    private static string Sent(string action, int? targetId, string? target, string extra = "'targetRect':null,'targetPoint':null,'horizontalScrollAlignment':'none','verticalScrollAlignment':'none','scrollBehavior':'none','selection':null") =>
        J($"{{'context':{FrameContext},'treeId':'TREE-1','action':'{action}','targetNodeId':{(targetId is { } id ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")},'target':{target ?? "null"},{extra}}}");

    private static string Received(string action, int? targetId) =>
        J($"{{'context':{RendererContext},'treeId':'TREE-1','action':'{action}','targetNodeId':{(targetId is { } id ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")},'anchorNodeId':null,'focusNodeId':null}}");

    private static string Computed(int? node, bool child) =>
        J($"{{'context':{RendererContext},'treeId':'TREE-1','point':{{'x':500,'y':180}},'nodeId':{(node is { } id ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")},'childTree':{(child ? "true" : "false")},'eventToFire':'hover'}}");

    private static List<EventValidationIssue> Validate(string eventType, string json)
    {
        var issues = new List<EventValidationIssue>();
        using var document = JsonDocument.Parse(json);
        EventPayloadValidator.Validate(BrowserEvidenceChannels.Accessibility, eventType, document.RootElement, 2000, issues);
        return issues;
    }

    private static KeyEvidenceRecord Record(string id, long time, string eventType, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new KeyEvidenceRecord(id, time, BrowserEvidenceChannels.Accessibility, eventType, document.RootElement.Clone());
    }

    // --- The payload rules ------------------------------------------------------

    [Fact]
    public void WellFormedRecordsAreValid()
    {
        Assert.Empty(Validate(BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, Heading)));
        Assert.Empty(Validate(BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", false, null)));
        Assert.Empty(Validate(BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(Heading)));
        Assert.Empty(Validate(BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("focus", 652, Heading)));
        Assert.Empty(Validate(BrowserEvidenceEventTypes.AccessibilityActionReceived, Received("focus", 652)));
        Assert.Empty(Validate(BrowserEvidenceEventTypes.AccessibilityHitTestComputed, Computed(652, false)));
        Assert.Empty(Validate(BrowserEvidenceEventTypes.AccessibilityHitTestComputed, Computed(null, false)));
    }

    [Fact]
    public void AHitTestFromThePagesLastResultNamesItAndAskedForAnother()
    {
        Assert.Contains(
            Validate(BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, null)),
            issue => issue.Code == "browser-accessibility-hit-test-answer-missing");
        Assert.Contains(
            Validate(BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", false, Heading)),
            issue => issue.Code == "browser-accessibility-hit-test-precise-inconsistent");
        Assert.NotEmpty(Validate(BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("from-somewhere", true, Heading)));
    }

    [Fact]
    public void ARecordedNodeNamesANode()
    {
        var unnamed = Heading.Replace("'nodeId':652", "'nodeId':0", StringComparison.Ordinal);
        Assert.Contains(
            Validate(BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(unnamed)),
            issue => issue.Code == "browser-accessibility-request-node-invalid");
        var negative = Heading.Replace("'nodeId':652", "'nodeId':-7", StringComparison.Ordinal);
        Assert.Empty(Validate(BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(negative)));
        var badBounds = Heading.Replace("'width':240", "'width':-1", StringComparison.Ordinal);
        Assert.NotEmpty(Validate(BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(badBounds)));
    }

    [Fact]
    public void AnActionsTargetIsTheNodeItsIdNames()
    {
        Assert.Contains(
            Validate(BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("focus", 653, Heading)),
            issue => issue.Code == "browser-accessibility-action-target-inconsistent");
        Assert.Empty(Validate(BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("focus", 653, null)));
        Assert.NotEmpty(Validate(
            BrowserEvidenceEventTypes.AccessibilityActionSent,
            Sent("focus", 652, Heading, "'targetRect':null,'targetPoint':null,'horizontalScrollAlignment':'middle','verticalScrollAlignment':'none','scrollBehavior':'none','selection':null")));
    }

    [Fact]
    public void EachRecordComesFromItsProcess()
    {
        // An action is sent from the browser process to a named frame; the
        // renderer's records name the document.
        var noFrame = Sent("focus", 652, Heading).Replace("'pageId':'frame-1','frameId':'frame-1'".Replace('\'', '"'), "'pageId':null,'frameId':null".Replace('\'', '"'), StringComparison.Ordinal);
        Assert.Contains(
            Validate(BrowserEvidenceEventTypes.AccessibilityActionSent, noFrame),
            issue => issue.Code == "browser-accessibility-request-context-invalid");
        var fromRenderer = HitTest("cached-tree-guess", true, null).Replace("\"processType\":\"browser\"", "\"processType\":\"renderer\"", StringComparison.Ordinal);
        Assert.Contains(
            Validate(BrowserEvidenceEventTypes.AccessibilityHitTest, fromRenderer),
            issue => issue.Code == "browser-accessibility-request-context-invalid");
        var fromBrowser = Received("focus", 652).Replace("\"processType\":\"renderer\"", "\"processType\":\"browser\"", StringComparison.Ordinal);
        Assert.Contains(
            Validate(BrowserEvidenceEventTypes.AccessibilityActionReceived, fromBrowser),
            issue => issue.Code == "browser-accessibility-context-invalid");
    }

    [Fact]
    public void AHitTestContinuesInAChildOnlyFromANode() =>
        Assert.Contains(
            Validate(BrowserEvidenceEventTypes.AccessibilityHitTestComputed, Computed(null, true)),
            issue => issue.Code == "browser-accessibility-hit-test-child-without-node");

    [Fact]
    public void TheProtocolReadsEachRecordAsItsShape()
    {
        foreach (var (eventType, json) in new[]
                 {
                     (BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, Heading)),
                     (BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(Heading)),
                     (BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("setSelection", null, null,
                         "'targetRect':null,'targetPoint':null,'horizontalScrollAlignment':'none','verticalScrollAlignment':'none','scrollBehavior':'none','selection':{'anchorNodeId':652,'anchorOffset':0,'focusNodeId':653,'focusOffset':4}")),
                     (BrowserEvidenceEventTypes.AccessibilityActionReceived, Received("focus", 652)),
                     (BrowserEvidenceEventTypes.AccessibilityHitTestComputed, Computed(88, true))
                 })
        {
            using var document = JsonDocument.Parse(json);
            BrowserProtocol.ValidateEvidencePayload(BrowserEvidenceChannels.Accessibility, eventType, document.RootElement);
        }

        using var extra = JsonDocument.Parse(Result(Heading).Replace("}}", ",\"extra\":1}}", StringComparison.Ordinal));
        Assert.ThrowsAny<JsonException>(() => BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Accessibility, BrowserEvidenceEventTypes.AccessibilityHitTestResult, extra.RootElement));
    }

    // --- The player -------------------------------------------------------------

    [Fact]
    public void AHitTestIsJoinedWithThePagesPreciseResult()
    {
        var requests = new AccessibilityRequests(
        [
            Record("h1", Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", true, Link)),
            Record("h2", Second + 2 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", true, Link)),
            Record("r1", Second + 10 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(Heading)),
            Record("h3", 2 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, Heading)),
            Record("r2", 2 * Second + 5 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(Heading)),
            Record("h4", 5 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", true, null, -5, 12))
        ]);

        var guess = requests.Of("h1")!;
        Assert.Equal(
            "Given at once: link \"Donate\" (node 700), from a guess from Chromium's own copy of the tree, by bounds",
            guess.Text);
        Assert.Contains("Point: 512, 300 on the screen, in physical pixels.", guess.Details);
        Assert.Contains(
            "The page's precise hit test then found heading \"Contact us\" (node 652), 10 ms later: not the object given.",
            guess.Details);
        Assert.Contains(guess.Details, line => line.StartsWith("1 other hit test was asked before it arrived", StringComparison.Ordinal));
        Assert.Contains(guess.Details, line => line.StartsWith("Chromium cannot tell which program asked", StringComparison.Ordinal));

        var precise = requests.Of("h3")!;
        Assert.Equal(
            "Given at once: heading \"Contact us\" (node 652), from the page's last precise result, whose bounds held the point",
            precise.Text);
        Assert.Contains(
            "The page's precise hit test then found heading \"Contact us\" (node 652), 5 ms later: the object given.",
            precise.Details);
        Assert.DoesNotContain(precise.Details, line => line.Contains("other hit test", StringComparison.Ordinal));

        var nothing = requests.Of("h4")!;
        Assert.Equal("Given at once: no object, from a guess from Chromium's own copy of the tree, by bounds", nothing.Text);
        Assert.Contains(nothing.Details, line => line.StartsWith("The page was asked for a precise hit test; no result was recorded within 1000 ms", StringComparison.Ordinal));

        var result = requests.Of("r1")!;
        Assert.Equal("The page's precise hit test found heading \"Contact us\" (node 652)", result.Text);
        Assert.Contains("The latest hit test before it was asked 8 ms earlier, at 512, 300.", result.Details);
    }

    [Fact]
    public void AnActionSentIsJoinedWithItsReceipt()
    {
        var requests = new AccessibilityRequests(
        [
            Record("s1", Second, BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("scrollToMakeVisible", 652, Heading,
                "'targetRect':{'x':0,'y':0,'width':0,'height':0},'targetPoint':null,'horizontalScrollAlignment':'closest-edge','verticalScrollAlignment':'closest-edge','scrollBehavior':'do-not-scroll-if-visible','selection':null")),
            Record("a1", Second + 3 * Millisecond, BrowserEvidenceEventTypes.AccessibilityActionReceived, Received("focus", 652)),
            Record("a2", Second + 4 * Millisecond, BrowserEvidenceEventTypes.AccessibilityActionReceived, Received("scrollToMakeVisible", 652)),
            Record("s2", 3 * Second, BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("doDefault", 900, null))
        ]);

        var scroll = requests.Of("s1")!;
        Assert.Equal("Asked the page to scroll heading \"Contact us\" (node 652) into view", scroll.Text);
        Assert.Contains("Scrolls the whole object into view.", scroll.Details);
        Assert.Contains("Alignment: horizontal closest-edge, vertical closest-edge.", scroll.Details);
        Assert.Contains("Scroll behavior: do-not-scroll-if-visible.", scroll.Details);
        Assert.Contains("The page's renderer received it 4 ms later.", scroll.Details);

        Assert.Equal("The page's renderer was asked to scroll node 652 into view", requests.Of("a2")!.Text);
        Assert.Contains("Sent by the browser process 4 ms earlier.", requests.Of("a2")!.Details);
        Assert.Contains(requests.Of("a1")!.Details, line => line.StartsWith("No sending by the browser process", StringComparison.Ordinal));

        var activate = requests.Of("s2")!;
        Assert.Equal(
            "Asked the page to do the default action of node 900, not in the browser's copy of the tree, such as activating it",
            activate.Text);
        Assert.Contains("No receipt by the page's renderer was recorded within 1000 ms.", activate.Details);
    }

    [Fact]
    public void TheRenderersHitTestSaysWhereItContinues()
    {
        var requests = new AccessibilityRequests(
        [
            Record("c1", Second, BrowserEvidenceEventTypes.AccessibilityHitTestComputed, Computed(652, false)),
            Record("c2", Second, BrowserEvidenceEventTypes.AccessibilityHitTestComputed, Computed(88, true)),
            Record("c3", Second, BrowserEvidenceEventTypes.AccessibilityHitTestComputed, Computed(null, false))
        ]);
        Assert.Equal("The page's hit test at 500, 180 found node 652", requests.Of("c1")!.Text);
        Assert.Equal(
            "The page's hit test at 500, 180 found node 88, which holds a child frame or tree, where it continues",
            requests.Of("c2")!.Text);
        Assert.Equal("The page's hit test at 500, 180 found no object", requests.Of("c3")!.Text);
        Assert.Null(AccessibilityRequests.Empty.Of("c1"));
    }

    [Fact]
    public void TheEventListSummarizesTheRecords()
    {
        string Summary(string eventType, string json)
        {
            using var document = JsonDocument.Parse(json);
            return SessionPlaybackArchiveBuilder.CreateSummary(BrowserEvidenceChannels.Accessibility, eventType, document.RootElement);
        }

        Assert.Equal(
            "accessibility-hit-test: at 512, 300, heading \"Contact us\", last precise result",
            Summary(BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, Heading)));
        Assert.Equal(
            "accessibility-hit-test: at 512, 300, no object, guess from the tree",
            Summary(BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", true, null)));
        Assert.Equal(
            "accessibility-hit-test-result: heading \"Contact us\"",
            Summary(BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(Heading)));
        Assert.Equal(
            "accessibility-action-sent: focus, heading \"Contact us\"",
            Summary(BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("focus", 652, Heading)));
        Assert.Equal(
            "accessibility-action-sent: doDefault, node 900",
            Summary(BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("doDefault", 900, null)));
        Assert.Equal(
            "accessibility-action-received: focus, node 652",
            Summary(BrowserEvidenceEventTypes.AccessibilityActionReceived, Received("focus", 652)));
        Assert.Equal(
            "accessibility-hit-test-computed: at 500, 180, node 88, continues in a child frame or tree",
            Summary(BrowserEvidenceEventTypes.AccessibilityHitTestComputed, Computed(88, true)));
    }

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "test.collector", "Test", "1.0.0", [BrowserEvidenceChannels.Accessibility], "test");

    [Fact]
    public void TheOutcomesComeThroughThePlaybackIndex()
    {
        var records = new List<RecorderEvent>
        {
            RecorderEventFactory.Create("s", Collector, BrowserEvidenceChannels.Accessibility, 1, Second,
                BrowserEvidenceEventTypes.AccessibilityHitTest, JsonDocument.Parse(HitTest("cached-tree-guess", true, Link)).RootElement.Clone()),
            RecorderEventFactory.Create("s", Collector, BrowserEvidenceChannels.Accessibility, 2, Second + 10 * Millisecond,
                BrowserEvidenceEventTypes.AccessibilityHitTestResult, JsonDocument.Parse(Result(Heading)).RootElement.Clone())
        };

        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var key = 0L;
        foreach (var record in records)
        {
            key++;
            builder.Add(key, record with { EventId = $"e{key}" });
        }

        var index = builder.Build();
        Assert.Equal(17, index.Version);
        var kept = Assert.Single(index.Events, item => item.EventType == BrowserEvidenceEventTypes.AccessibilityHitTest);
        Assert.True(kept.Payload.TryGetProperty("answer", out var answer) && answer.TryGetProperty("bounds", out _));

        var archiveBuilder = new SessionPlaybackArchiveBuilder(Path.GetTempPath(), retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(20), 20 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, true),
            [], [], 1, 0, null));

        var outcome = archive.AccessibilityRequests.Of("e1")!;
        Assert.StartsWith("Given at once: link \"Donate\" (node 700)", outcome.Text, StringComparison.Ordinal);
        Assert.Contains(
            "The page's precise hit test then found heading \"Contact us\" (node 652), 10 ms later: not the object given.",
            outcome.Details);
    }
}

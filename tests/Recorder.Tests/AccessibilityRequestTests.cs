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

    private const string Section =
        "{'treeId':'TREE-1','nodeId':640,'role':'sectionWithoutName','name':'','bounds':{'x':400,'y':250,'width':500,'height':400}}";

    private const string FrameDocument =
        "{'treeId':'TREE-2','nodeId':5,'role':'rootWebArea','name':'Frame','bounds':{'x':480,'y':280,'width':300,'height':200}}";

    private static KeyEvidenceRecord KeyDown(string id, long time, int virtualKey, bool injected = false)
    {
        using var document = JsonDocument.Parse(J($"{{'virtualKey':{virtualKey},'up':false,'injected':{(injected ? "true" : "false")}}}"));
        return new KeyEvidenceRecord(id, time, KeyboardHookRecords.Channel, KeyboardHookRecords.KeyEventType, document.RootElement.Clone());
    }

    [Fact]
    public void AHitTestIsJoinedWithThePagesPreciseResult()
    {
        var requests = new AccessibilityRequests(
        [
            Record("h1", Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", true, Link)),
            Record("h2", Second + 30 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", true, Link, 600, 300)),
            Record("r1", Second + 40 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(Heading)),
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
            "The page was asked for a precise hit test; no result arrived before the next request, 30 ms later. A point with no object fires no result.",
            guess.Details);
        Assert.Contains(guess.Details, line => line.StartsWith("Chromium cannot tell which program asked", StringComparison.Ordinal));

        var later = requests.Of("h2")!;
        Assert.Contains(
            "The page's precise hit test then found heading \"Contact us\" (node 652), 10 ms after the request began: not the object given.",
            later.Details);
        Assert.Contains("The object given is a different object from the page's precise result.", later.Details);
        Assert.Contains("The next request, 970 ms later, was given heading \"Contact us\" (node 652).", later.Details);

        var precise = requests.Of("h3")!;
        Assert.Equal(
            "Given at once: heading \"Contact us\" (node 652), from the page's last precise result, whose bounds held the point",
            precise.Text);
        Assert.Contains(
            "The page's precise hit test then found heading \"Contact us\" (node 652), 5 ms after the request began: the object given.",
            precise.Details);
        Assert.DoesNotContain(precise.Details, line => line.StartsWith("The next request", StringComparison.Ordinal));

        var nothing = requests.Of("h4")!;
        Assert.Equal("Given at once: no object, from a guess from Chromium's own copy of the tree, by bounds", nothing.Text);
        Assert.Contains(nothing.Details, line => line.StartsWith("The page was asked for a precise hit test; no result was recorded within 1000 ms", StringComparison.Ordinal));

        var result = requests.Of("r1")!;
        Assert.Equal("The page's precise hit test found heading \"Contact us\" (node 652)", result.Text);
        Assert.Contains(
            "Joined with the request at 00:00:01.030, 1 hit test at 600, 300, the latest before it, 10 ms after the request began.",
            result.Details);
    }

    [Fact]
    public void HitTestsAtOnePointInQuickSuccessionAreOneRequest()
    {
        // A program's accHitTest recursion: the document is given the
        // section, the section the heading. The precise result, the link, is
        // inside the section's bounds, so the program was given a container
        // the pointer had moved into; the next request has the link.
        var requests = new AccessibilityRequests(
        [
            Record("h1", Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, Section, 500, 335)),
            Record("h2", Second + 2 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, Section, 500, 335)),
            Record("h3", Second + 4 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, Section, 500, 335)),
            Record("r1", Second + 9 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(Link)),
            Record("r2", Second + 10 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(Link)),
            Record("h4", Second + 31 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, Link, 502, 336)),
            Record("h5", Second + 60 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, Link, 502, 336)),
            Record("r3", Second + 65 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(FrameDocument)),
            Record("h6", 2 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", true, Section, 600, 380)),
            Record("r4", 2 * Second + 8 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(FrameDocument))
        ]);

        var first = requests.Of("h1")!;
        Assert.Equal(
            "Given at once: sectionWithoutName with no name (node 640), from the page's last precise result, whose bounds held the point; " +
            "one request of 3 hit tests at 500, 335 within 4 ms",
            first.Text);
        Assert.Contains(first.Details, line => line.EndsWith("The last was given sectionWithoutName with no name (node 640).", StringComparison.Ordinal));
        Assert.Contains("2 precise results arrived, all for that object.", first.Details);
        Assert.Contains(
            "The object given is a container the pointer had moved into: its bounds hold those of the precise result.",
            first.Details);
        Assert.Contains("The next request, 31 ms later, was given link \"Donate\" (node 700).", first.Details);

        Assert.Equal(
            "Part of the request at 00:00:01.000: hit test 2 of 3, given sectionWithoutName with no name (node 640)",
            requests.Of("h2")!.Text);
        Assert.Equal(
            "Part of the request at 00:00:01.000: hit test 3 of 3, given sectionWithoutName with no name (node 640)",
            requests.Of("h3")!.Text);

        // A record more than 20 ms after the one before, at the same point,
        // begins another request.
        Assert.DoesNotContain("one request", requests.Of("h4")!.Text, StringComparison.Ordinal);
        Assert.Contains(requests.Of("h4")!.Details, line => line.StartsWith("The page was asked for a precise hit test; no result arrived before the next request, 29 ms later.", StringComparison.Ordinal));
        Assert.Contains("The precise result is in another document's tree, such as a frame's.", requests.Of("h5")!.Details);
        Assert.Contains("The precise result is in another document's tree, such as a frame's.", requests.Of("h6")!.Details);
        Assert.DoesNotContain(requests.Of("h6")!.Details, line => line.StartsWith("The next request", StringComparison.Ordinal));
    }

    [Fact]
    public void TheUnderThePointerRowGivesTheLatestRequestsObject()
    {
        var requests = new AccessibilityRequests(
        [
            Record("h1", Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", true, Section)),
            Record("r1", Second + 5 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(Link)),
            Record("h2", 2 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, Link, 500, 335)),
            Record("r2", 2 * Second + 5 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(Link)),
            Record("h3", 3 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("last-precise-result", true, Link, 501, 335)),
            Record("h4", 4 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", false, Heading, 490, 300))
        ]);

        var start = Assert.Single(requests.RowsAt(Second / 2));
        Assert.Equal(AccessibilityRequests.PointerSetting, start.Setting);
        Assert.Equal("no hit test yet", start.Value);
        Assert.Equal(AssistiveTechnologyTimeline.Group, start.Group);

        var row = Assert.Single(requests.RowsAt(Second + 500 * Millisecond));
        Assert.Equal(
            "sectionWithoutName with no name (node 640), whose document's accessibility tree was not recorded; " +
            "the page found link \"Donate\" (node 700)",
            row.Value);
        Assert.Equal("00:00:01.000", row.WhenSet);
        Assert.True(row.Changed);
        Assert.Equal("link \"Donate\" (node 700); the page found the same", Assert.Single(requests.RowsAt(2 * Second)).Value);
        Assert.Equal("link \"Donate\" (node 700); no precise result", Assert.Single(requests.RowsAt(3 * Second + Millisecond)).Value);
        Assert.False(Assert.Single(requests.RowsAt(9 * Second)).Changed);

        // A step is a request given another object than the one before.
        Assert.Equal([Second, 2 * Second, 4 * Second], requests.ChangeTimesOf(AccessibilityRequests.PointerKey));
        Assert.Null(requests.ChangeTimesOf(MouseActivity.PointerKey));
        Assert.Equal(
            requests.ChangeTimesOf(AccessibilityRequests.PointerKey),
            PropertyChangeSteps.TimesOf(row, WindowsPreferenceTimeline.Empty, MagnifierChangeTimeline.Empty, accessibilityRequests: requests));
        Assert.Empty(AccessibilityRequests.Empty.RowsAt(Second));
        Assert.Null(AccessibilityRequests.Empty.ChangeTimesOf(AccessibilityRequests.PointerKey));
    }

    private static KeyEvidenceRecord TreeNode(string id, long time, int node, string role, string name, string children) =>
        Record(id, time, BrowserEvidenceEventTypes.AccessibilityCheckpointNode, J(
            $"{{'context':{RendererContext},'checkpointId':'c','nodeIndex':0,'accessibilityNodeId':{node},'parentAccessibilityNodeId':null," +
            $"'domNodeId':null,'role':0,'roleName':'{role}','name':'{name}','description':'','serializedProperties':'id={node} {role}{(children.Length == 0 ? "" : " child_ids=" + children)}','focused':false}}"));

    private static string Unnamed(string tree, int node, string role) =>
        $"{{'treeId':'{tree}','nodeId':{node},'role':'{role}','name':'','bounds':{{'x':400,'y':250,'width':500,'height':400}}}}";

    [Fact]
    public void TheRowGivesAnUnnamedObjectsTextAndTheObjectItIsIn()
    {
        var text = "This paragraph is long enough that the row gives only the start of it, and no more.";
        var requests = new AccessibilityRequests(
        [
            TreeNode("n1", Second / 10, 600, "main", "", "640"),
            TreeNode("n2", Second / 10, 640, "sectionWithoutName", "", "641,650"),
            TreeNode("n3", Second / 10, 641, "heading", "Contact us", "642"),
            TreeNode("n4", Second / 10, 642, "staticText", "Contact us", ""),
            TreeNode("n5", Second / 10, 650, "paragraph", "", "651"),
            TreeNode("n6", Second / 10, 651, "staticText", text, ""),
            TreeNode("n7", Second / 10, 660, "genericContainer", "", ""),
            TreeNode("n8", Second / 10, 659, "button", "Forward", "660"),
            Record("c1", Second / 5, BrowserEvidenceEventTypes.AccessibilityHitTestComputed, Computed(640, false)),
            Record("h1", 2 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", true, Section)),
            Record("r1", 2 * Second + 5 * Millisecond, BrowserEvidenceEventTypes.AccessibilityHitTestResult, Result(Unnamed("TREE-1", 650, "paragraph"))),
            // A later serialization of the section, without its heading.
            TreeNode("n9", 3 * Second, 640, "sectionWithoutName", "", "650"),
            Record("h2", 4 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", false, Section, 600, 300)),
            Record("h3", 5 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", false, Unnamed("TREE-1", 660, "genericContainer"), 610, 300)),
            Record("h4", 6 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", false, Unnamed("TREE-1", 777, "genericContainer"), 620, 300)),
            Record("h5", 7 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", false, Unnamed("TREE-2", 5, "genericContainer"), 630, 300)),
            Record("h6", 8 * Second, BrowserEvidenceEventTypes.AccessibilityHitTest, HitTest("cached-tree-guess", false, Heading, 640, 300))
        ]);

        Assert.True(AccessibilityRequests.IsTreeEvidence(BrowserEvidenceChannels.Accessibility, BrowserEvidenceEventTypes.AccessibilityCheckpointNode));
        Assert.False(AccessibilityRequests.IsRequestEvidence(BrowserEvidenceChannels.Accessibility, BrowserEvidenceEventTypes.AccessibilityCheckpointNode));
        string Value(long time) => Assert.Single(requests.RowsAt(time)).Value;
        Assert.Equal(
            "sectionWithoutName with no name (node 640), holding \"Contact us This paragraph is long enough that the row gives…\", " +
            "in main with no name (node 600); the page found paragraph with no name (node 650), " +
            "holding \"This paragraph is long enough that the row gives only the st…\", in sectionWithoutName with no name (node 640)",
            Value(2 * Second));
        Assert.Equal(
            "sectionWithoutName with no name (node 640), holding \"This paragraph is long enough that the row gives only the st…\", " +
            "in main with no name (node 600); no precise result",
            Value(4 * Second));
        Assert.Equal(
            "genericContainer with no name (node 660), holding no text, in button \"Forward\" (node 659); no precise result",
            Value(5 * Second));
        Assert.Equal("genericContainer with no name (node 777), not in the recorded accessibility tree; no precise result", Value(6 * Second));
        Assert.Equal(
            "genericContainer with no name (node 5), whose document's accessibility tree was not recorded; no precise result",
            Value(7 * Second));
        Assert.Equal("heading \"Contact us\" (node 652); no precise result", Value(8 * Second));
    }

    [Fact]
    public void AnActionIsLinkedWithTheKeyBeforeIt()
    {
        var focusExtra = "'targetRect':{'x':0,'y':0,'width':0,'height':0},'targetPoint':null,'horizontalScrollAlignment':'center','verticalScrollAlignment':'center','scrollBehavior':'do-not-scroll-if-visible','selection':null";
        var requests = new AccessibilityRequests(
            [
                Record("s1", Second + 25 * Millisecond, BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("scrollToMakeVisible", 652, Heading, focusExtra)),
                Record("s2", Second + 25 * Millisecond + 100_000, BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("clearAccessibilityFocus", 700, Link)),
                Record("s3", Second + 25 * Millisecond + 200_000, BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("setAccessibilityFocus", 652, Heading)),
                Record("s4", 5 * Second, BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("doDefault", 700, Link)),
                Record("s5", 5 * Second + 450 * Millisecond, BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("focus", 700, Link))
            ],
            [
                KeyDown("k1", Second, 0x48),
                KeyDown("k2", 3 * Second, 0x48),
                KeyDown("k3", 4 * Second, 0x09),
                KeyDown("k4", 4 * Second + 100 * Millisecond, 0x09, injected: true)
            ],
            [new KeptCommand(Second, "h", ["next heading"]), new KeptCommand(3 * Second, "h", ["next heading"])]);

        var scroll = requests.Of("s1")!;
        Assert.Contains("After the key H, 25 ms earlier, which NVDA kept as next heading (h), inferred.", scroll.Details);
        Assert.Contains(
            "Sent with it, as one step: clear Chromium's accessibility focus from link \"Donate\" (node 700); " +
            "move Chromium's accessibility focus, its note of the assistive technology's position, to heading \"Contact us\" (node 652).",
            scroll.Details);
        Assert.Equal(
            "Asked the page to move Chromium's accessibility focus, its note of the assistive technology's position, to heading \"Contact us\" (node 652)",
            requests.Of("s3")!.Text);
        Assert.Equal("Asked the page to clear Chromium's accessibility focus from link \"Donate\" (node 700)", requests.Of("s2")!.Text);
        Assert.Equal(
            "Sent to the page after it: scroll heading \"Contact us\" (node 652) into view; " +
            "clear Chromium's accessibility focus from link \"Donate\" (node 700); " +
            "move Chromium's accessibility focus, its note of the assistive technology's position, to heading \"Contact us\" (node 652).",
            requests.SentAfter("k1"));

        // A key NVDA kept that no action followed says so; one it did not
        // keep says nothing. An injected key is not one pressed.
        Assert.Equal("Nothing was sent to the page in the 500 ms after it.", requests.SentAfter("k2"));
        Assert.Null(requests.SentAfter("k4"));
        Assert.Contains("No key was pressed and no mouse button released in the 500 ms before it.", requests.Of("s4")!.Details);
        Assert.Contains(requests.Of("s5")!.Details, line => line.StartsWith("No key was pressed", StringComparison.Ordinal));
        Assert.Null(requests.SentAfter("k3"));
        Assert.Null(AccessibilityRequests.Empty.SentAfter("k1"));
    }

    private static KeyEvidenceRecord Mouse(string id, long time, string kind, int x, int y, bool injected = false)
    {
        using var document = JsonDocument.Parse(J(
            $"{{'kind':'{kind}','button':'left','x':{x},'y':{y},'injected':{(injected ? "true" : "false")}}}"));
        return new KeyEvidenceRecord(id, time, MouseHookRecords.Channel, MouseHookRecords.EventType, document.RootElement.Clone());
    }

    [Fact]
    public void AnActionIsLinkedWithTheClickBeforeIt()
    {
        // NVDA's Elements List used with the mouse: "Move to" clicked, then
        // the scroll. A click a program injected, as head pointer and eye
        // tracking software can, says so. A key after a click is the later.
        var requests = new AccessibilityRequests(
            [
                Record("s1", Second + 170 * Millisecond, BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("scrollToMakeVisible", 652, Heading)),
                Record("s2", 3 * Second + 40 * Millisecond, BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("doDefault", 700, Link)),
                Record("s3", 5 * Second + 30 * Millisecond, BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("focus", 700, Link))
            ],
            [KeyDown("k1", 5 * Second, 0x09)],
            [],
            [
                Mouse("m1", Second - 180 * Millisecond, "button-down", 1095, 732),
                Mouse("m2", Second, "button-up", 1095, 732),
                Mouse("m3", 3 * Second, "button-up", 500, 340, injected: true),
                Mouse("m4", 4 * Second + 900 * Millisecond, "button-up", 10, 10),
                Mouse("m5", 7 * Second, "button-up", 10, 10)
            ]);

        Assert.Contains("After a left click at 1095, 732, the button released 170 ms earlier.", requests.Of("s1")!.Details);
        Assert.Equal("Sent to the page after it: scroll heading \"Contact us\" (node 652) into view.", requests.SentAfter("m2"));
        Assert.Null(requests.SentAfter("m1"));
        Assert.Contains(
            "After a left click at 500, 340, injected by a program, the button released 40 ms earlier.",
            requests.Of("s2")!.Details);
        Assert.Contains("After the key Tab, 30 ms earlier.", requests.Of("s3")!.Details);
        Assert.Null(requests.SentAfter("m4"));
        Assert.Null(requests.SentAfter("m5"));
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
        Assert.Equal(19, index.Version);
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
            "The page's precise hit test then found heading \"Contact us\" (node 652), 10 ms after the request began: not the object given.",
            outcome.Details);
    }
}

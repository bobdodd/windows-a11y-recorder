using System.Text.Json;
using Recorder.Collectors.Browser;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The screen reader's reading position, inferred at playback
/// (ReadingPositionTimeline): each rule of
/// docs/architecture/screen-reader-activity.md, "The reading position (step
/// 4)", the row, the band, Selected event values, and the playback index.
/// </summary>
public sealed class ReadingPositionTests
{
    private const long Millisecond = 1_000_000;
    private const long Second = 1_000_000_000;

    private const string FrameContext =
        "{'browserInstanceId':'browser-1','processId':4000,'processType':'browser','profileId':null,'browserContextId':null,'pageId':'frame-1','frameId':'frame-1','documentId':null,'executionWorldId':null,'documentToken':'TOKEN-1'}";

    private const string RendererContext =
        "{'browserInstanceId':'browser-1','processId':4100,'processType':'renderer','profileId':null,'browserContextId':null,'pageId':null,'frameId':null,'documentId':null,'executionWorldId':null,'documentToken':'TOKEN-1'}";

    private const string Heading =
        "{'treeId':'TREE-1','nodeId':652,'role':'heading','name':'Contact us','bounds':{'x':480,'y':290,'width':240,'height':32}}";

    private const string Link =
        "{'treeId':'TREE-1','nodeId':700,'role':'link','name':'Donate','bounds':{'x':480,'y':330,'width':80,'height':20}}";

    private static string J(string json) => json.Replace('\'', '"');

    private static KeyEvidenceRecord Record(string id, long time, string eventType, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new KeyEvidenceRecord(id, time, BrowserEvidenceChannels.Accessibility, eventType, document.RootElement.Clone());
    }

    private static KeyEvidenceRecord Sent(string id, long time, string action, string target, int node) =>
        Record(id, time, BrowserEvidenceEventTypes.AccessibilityActionSent, J(
            $"{{'context':{FrameContext},'treeId':'TREE-1','action':'{action}','targetNodeId':{node},'target':{target}," +
            "'targetRect':null,'targetPoint':null,'horizontalScrollAlignment':'none','verticalScrollAlignment':'none','scrollBehavior':'none','selection':null}"));

    // Names the tree's document, as the renderer's record of an action does.
    private static KeyEvidenceRecord Received(string id, long time, int node) =>
        Record(id, time, BrowserEvidenceEventTypes.AccessibilityActionReceived, J(
            $"{{'context':{RendererContext},'treeId':'TREE-1','action':'scrollToMakeVisible','targetNodeId':{node},'anchorNodeId':null,'focusNodeId':null}}"));

    private static KeyEvidenceRecord TreeNode(string id, long time, int node, string role, string name, int? domNode) =>
        Record(id, time, BrowserEvidenceEventTypes.AccessibilityCheckpointNode, J(
            $"{{'context':{RendererContext},'checkpointId':'c','nodeIndex':0,'accessibilityNodeId':{node},'parentAccessibilityNodeId':null," +
            $"'domNodeId':{(domNode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")},'role':0,'roleName':'{role}','name':'{name}','description':'','serializedProperties':'id={node} {role}','focused':false}}"));

    private static KeyEvidenceRecord KeyDown(string id, long time, int virtualKey)
    {
        using var document = JsonDocument.Parse(J($"{{'virtualKey':{virtualKey},'up':false,'injected':false}}"));
        return new KeyEvidenceRecord(id, time, KeyboardHookRecords.Channel, KeyboardHookRecords.KeyEventType, document.RootElement.Clone());
    }

    private static ReadingPositionTimeline Timeline(
        IEnumerable<KeyEvidenceRecord> records,
        IEnumerable<KeyEvidenceRecord> keys,
        IReadOnlyList<KeptCommand> kept,
        IReadOnlyList<ForegroundWindow>? foreground = null,
        IEnumerable<PageFocusChange>? focus = null,
        IEnumerable<PageLoad>? loads = null)
    {
        var requests = new AccessibilityRequests(records, keys, kept);
        return new ReadingPositionTimeline(requests, kept, foreground ?? [], "chrome", focus ?? [], loads ?? [], 30 * Second);
    }

    private static string ValueAt(ReadingPositionTimeline timeline, long time) =>
        Assert.Single(timeline.RowsAt(time)).Value;

    [Fact]
    public void ARequestMovesThePositionToItsTargetFromTheKeysCommand()
    {
        var timeline = Timeline(
            [Received("r0", Second / 10, 652), Sent("s1", Second + 25 * Millisecond, "scrollToMakeVisible", Heading, 652)],
            [KeyDown("k1", Second, 0x48)],
            [new KeptCommand(Second, "h", ["next heading"])]);

        var row = Assert.Single(timeline.RowsAt(Second / 2));
        Assert.Equal("unknown: nothing yet shows where NVDA is reading", row.Value);
        Assert.Equal("at start", row.WhenSet);
        Assert.Equal(AssistiveTechnologyTimeline.Group, row.Group);
        Assert.Equal(ReadingPositionTimeline.Setting, row.Setting);

        Assert.Equal("heading \"Contact us\" (node 652)", ValueAt(timeline, 2 * Second));
        var step = Assert.Single(timeline.Steps);
        Assert.Equal((1, ReadingPositionKind.Known), (step.Rule, step.Kind));
        Assert.Equal(("h (next heading)", "Known", "00:00:01.025"), (step.Cause, step.State, step.When));
        Assert.Equal((-1, 0, 0), (timeline.IndexAt(Second), timeline.IndexAt(Second + 25 * Millisecond), timeline.IndexAt(9 * Second)));
        Assert.Equal(
            "Inferred reading position after it (rule 1 of step 4, rule 1): heading \"Contact us\" (node 652)." + Environment.NewLine +
            "Basis: A request to scroll it into view, 25 ms after NVDA kept next heading (h).",
            timeline.Of("s1"));
        Assert.Equal(timeline.Of("s1"), timeline.Of("k1"));
        Assert.Equal([Second + 25 * Millisecond], timeline.ChangeTimesOf(ReadingPositionTimeline.Key));
        Assert.Null(timeline.ChangeTimesOf(AccessibilityRequests.PointerKey));
    }

    [Fact]
    public void ARequestOnTheSameObjectKeepsItWithWhereWithinItUnknown()
    {
        var timeline = Timeline(
            [Sent("s1", Second, "scrollToMakeVisible", Heading, 652), Sent("s2", 2 * Second, "scrollToMakeVisible", Heading, 652)],
            [],
            []);

        Assert.Equal("heading \"Contact us\" (node 652); where within it is unknown", ValueAt(timeline, 3 * Second));
        Assert.Equal((3, ReadingPositionKind.SameObject), (timeline.Steps[1].Rule, timeline.Steps[1].Kind));
        Assert.Contains("with no key or click in the 500 ms before it", timeline.Steps[0].Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void AFocusRequestAndThePagesFocusMoveThePosition()
    {
        var timeline = Timeline(
            [
                Received("r0", Second / 10, 700),
                TreeNode("n1", Second / 10, 700, "link", "Donate", 41),
                TreeNode("n2", Second / 10, 652, "heading", "Contact us", 42),
                Sent("s1", Second, "focus", Link, 700)
            ],
            [KeyDown("k1", Second - 20 * Millisecond, 0x09)],
            [],
            focus:
            [
                new PageFocusChange("f1", Second + Millisecond, "TOKEN-1", 41),
                new PageFocusChange("f2", 2 * Second, "TOKEN-1", 42),
                new PageFocusChange("f3", 3 * Second, "TOKEN-1", 99)
            ]);

        Assert.Equal("link \"Donate\" (node 700), focused", ValueAt(timeline, Second + 2 * Millisecond));
        Assert.Equal(["s1", "f2", "f3"], timeline.Steps.Select(step => step.EventId));
        Assert.Equal("heading \"Contact us\" (node 652), focused", ValueAt(timeline, 2 * Second));
        Assert.Equal(
            "unknown: the page's focus moved to DOM node 99, which the recorded tree does not name",
            ValueAt(timeline, 3 * Second));
        Assert.All(timeline.Steps, step => Assert.Equal(2, step.Rule));
        Assert.Equal(["Tab, no NVDA command", "the page's focus moved", "the page's focus moved"], timeline.Steps.Select(step => step.Cause));
    }

    [Fact]
    public void AKeptMoveThatSentNothingMakesThePositionUncertain()
    {
        var timeline = Timeline(
            [Sent("s1", Second + 25 * Millisecond, "scrollToMakeVisible", Heading, 652)],
            [KeyDown("k1", Second, 0x48), KeyDown("k2", 2 * Second, 0x48), KeyDown("k3", 3 * Second, 0x46)],
            [
                new KeptCommand(Second, "h", ["next heading"]),
                new KeptCommand(2 * Second, "h", ["next heading"]),
                new KeptCommand(3 * Second, "NVDA+f", ["toggle something"])
            ]);

        Assert.Equal(2, timeline.Steps.Count);
        var step = timeline.Steps[1];
        Assert.Equal((4, ReadingPositionKind.Uncertain, "k2"), (step.Rule, step.Kind, step.EventId));
        Assert.Equal(("h (next heading)", "Uncertain"), (step.Cause, step.State));
        Assert.Equal(
            "uncertain, as next heading (h) at 00:00:02.000 sent nothing: heading \"Contact us\" (node 652)",
            ValueAt(timeline, 4 * Second));
        Assert.Equal(
            [ReadingPositionKind.Known, ReadingPositionKind.Uncertain],
            timeline.Spans.Select(span => span.Kind));
        Assert.Equal((2 * Second, 30 * Second), (timeline.Spans[1].Start, timeline.Spans[1].End));
        Assert.Equal("heading \"Contact us\"", timeline.Spans[0].Label);
    }

    [Fact]
    public void AKeptMoveWithNoPositionMakesNoStep()
    {
        var timeline = Timeline(
            [Sent("s1", 5 * Second, "scrollToMakeVisible", Heading, 652)],
            [KeyDown("k1", Second, 0x48)],
            [new KeptCommand(Second, "h", ["next heading"])]);

        Assert.Single(timeline.Steps);
    }

    [Fact]
    public void APageLoadMakesThePositionUnknown()
    {
        var timeline = Timeline(
            [Sent("s1", Second, "scrollToMakeVisible", Heading, 652)],
            [],
            [],
            loads: [new PageLoad(2 * Second, "p1")]);

        Assert.Equal("unknown: a page was loaded at 00:00:02.000", ValueAt(timeline, 3 * Second));
        Assert.Equal((5, "p1", "a page was loaded", "Unknown"), (timeline.Steps[1].Rule, timeline.Steps[1].EventId, timeline.Steps[1].Cause, timeline.Steps[1].State));
        Assert.Equal((Second, 2 * Second), (Assert.Single(timeline.Spans).Start, timeline.Spans[0].End));
    }

    [Fact]
    public void SayAllAndSelectionMakeThePositionUnknownOnce()
    {
        var timeline = Timeline(
            [Sent("s1", Second, "scrollToMakeVisible", Heading, 652), Sent("s2", 5 * Second, "scrollToMakeVisible", Heading, 652)],
            [KeyDown("k1", 2 * Second, 0x28), KeyDown("k2", 3 * Second, 0x28), KeyDown("k3", 6 * Second, 0x28)],
            [
                new KeptCommand(2 * Second, "NVDA+downArrow", ["say all"]),
                new KeptCommand(3 * Second, "NVDA+downArrow", ["say all"]),
                new KeptCommand(6 * Second, "shift+downArrow", ["select next line"])
            ]);

        Assert.Equal([1, 6, 1, 8], timeline.Steps.Select(step => step.Rule));
        Assert.StartsWith("unknown: say all (NVDA+downArrow) at 00:00:02.000", ValueAt(timeline, 4 * Second), StringComparison.Ordinal);
        Assert.StartsWith("unknown: select next line (shift+downArrow)", ValueAt(timeline, 7 * Second), StringComparison.Ordinal);

        // After say all, the request on the same object is a new position, not the same one.
        Assert.Equal(ReadingPositionKind.Known, timeline.Steps[2].Kind);
    }

    [Fact]
    public void AKeyWhileAnotherWindowIsInFrontLeavesThePosition()
    {
        var timeline = Timeline(
            [Sent("s1", Second, "scrollToMakeVisible", Heading, 652)],
            [KeyDown("k1", 3 * Second, 0x28)],
            [new KeptCommand(3 * Second, "NVDA+downArrow", ["say all"])],
            foreground: [new ForegroundWindow(0, "chrome"), new ForegroundWindow(2 * Second, "nvda")]);

        Assert.Single(timeline.Steps);
        Assert.Equal("heading \"Contact us\" (node 652)", ValueAt(timeline, 4 * Second));
    }

    [Fact]
    public void PageDownFollowsItsRequest()
    {
        var timeline = Timeline(
            [Sent("s1", Second + 20 * Millisecond, "scrollToMakeVisible", Heading, 652)],
            [KeyDown("k1", Second, 0x22)],
            [new KeptCommand(Second, "pageDown", ["next page"])]);

        var step = Assert.Single(timeline.Steps);
        Assert.Equal((9, ReadingPositionKind.Known), (step.Rule, step.Kind));
        Assert.Equal("heading \"Contact us\" (node 652)", step.Value);
        Assert.Contains("named where NVDA's caret went", step.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFocusAndPageRecordsAreRead()
    {
        using var focused = JsonDocument.Parse(J("{'context':{'documentToken':'TOKEN-1'},'outcome':'focused','focusedNodeId':41}"));
        using var other = JsonDocument.Parse(J("{'context':{'documentToken':'TOKEN-9'},'outcome':'focused','focusedNodeId':41}"));
        using var unchanged = JsonDocument.Parse(J("{'context':{'documentToken':'TOKEN-1'},'outcome':'unchanged','focusedNodeId':41}"));
        var pages = new HashSet<string>(["TOKEN-1"]);

        Assert.Equal(new PageFocusChange("f", Second, "TOKEN-1", 41), ReadingPositionTimeline.FocusOf("f", Second, focused.RootElement, pages));
        Assert.Null(ReadingPositionTimeline.FocusOf("f", Second, other.RootElement, pages));
        Assert.Null(ReadingPositionTimeline.FocusOf("f", Second, unchanged.RootElement, pages));
        Assert.True(ReadingPositionTimeline.IsPageUrl("file:///C:/test/index.html"));
        Assert.False(ReadingPositionTimeline.IsPageUrl("chrome://newtab/"));
        Assert.False(ReadingPositionTimeline.IsPageUrl(null));
        Assert.True(ReadingPositionTimeline.IsFocusEvidence(BrowserEvidenceChannels.Interaction, BrowserEvidenceEventTypes.FocusChanged));
        Assert.Empty(ReadingPositionTimeline.Empty.RowsAt(Second));
        Assert.Null(ReadingPositionTimeline.Empty.ChangeTimesOf(ReadingPositionTimeline.Key));
    }

    [Fact]
    public void ThePropertyRowStepsThroughThePositions()
    {
        var timeline = Timeline(
            [Sent("s1", Second, "scrollToMakeVisible", Heading, 652), Sent("s2", 2 * Second, "scrollToMakeVisible", Link, 700)],
            [],
            []);
        var row = Assert.Single(timeline.RowsAt(3 * Second));

        Assert.Equal(
            [Second, 2 * Second],
            PropertyChangeSteps.TimesOf(row, WindowsPreferenceTimeline.Empty, MagnifierChangeTimeline.Empty, readingPosition: timeline));
    }

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "test.collector", "Test", "1.0.0", [BrowserEvidenceChannels.Accessibility, BrowserEvidenceChannels.Interaction, BrowserEvidenceChannels.Navigation], "test");

    [Fact]
    public void ThePositionComesThroughThePlaybackIndex()
    {
        var navigation = J(
            "{'committed':true,'context':{'browserInstanceId':'browser-1','processId':4000,'processType':'browser','pageId':'frame-1','frameId':'frame-1','documentId':'d1','documentToken':'TOKEN-1'}," +
            "'frameType':'primary-main-frame','primaryPage':true,'sameDocument':false,'url':'file:///C:/test/index.html','navigationId':'n1'}");
        var focus = J("{'context':{'documentToken':'TOKEN-1'},'previousNodeId':null,'requestedNodeId':41,'focusedNodeId':41,'outcome':'focused'}");
        var records = new List<RecorderEvent>
        {
            RecorderEventFactory.Create("s", Collector, BrowserEvidenceChannels.Navigation, 1, Second / 10,
                BrowserEvidenceEventTypes.NavigationCompleted, JsonDocument.Parse(navigation).RootElement.Clone()),
            RecorderEventFactory.Create("s", Collector, BrowserEvidenceChannels.Accessibility, 2, Second / 5,
                BrowserEvidenceEventTypes.AccessibilityCheckpointNode, TreeNode("x", 0, 700, "link", "Donate", 41).Payload),
            RecorderEventFactory.Create("s", Collector, BrowserEvidenceChannels.Accessibility, 3, Second / 4,
                BrowserEvidenceEventTypes.AccessibilityActionReceived, Received("x", 0, 652).Payload),
            RecorderEventFactory.Create("s", Collector, BrowserEvidenceChannels.Accessibility, 4, Second,
                BrowserEvidenceEventTypes.AccessibilityActionSent, Sent("x", 0, "scrollToMakeVisible", Heading, 652).Payload),
            RecorderEventFactory.Create("s", Collector, BrowserEvidenceChannels.Interaction, 5, 2 * Second,
                BrowserEvidenceEventTypes.FocusChanged, JsonDocument.Parse(focus).RootElement.Clone())
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
        var kept = Assert.Single(index.Events, item => item.EventType == BrowserEvidenceEventTypes.FocusChanged);
        Assert.True(kept.Payload.TryGetProperty("focusedNodeId", out _));

        var archiveBuilder = new SessionPlaybackArchiveBuilder(Path.GetTempPath(), retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(20), 20 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, true),
            [], [], 1, 0, null));

        Assert.Equal(["e1", "e4", "e5"], archive.ReadingPosition.Steps.Select(step => step.EventId));
        Assert.Equal(["e1", "e4", "e5"], archive.ReadingPosition.Steps.Select(step => step.Event?.EventId));
        Assert.Equal(5, archive.ReadingPosition.Steps[0].Rule);
        Assert.Equal("link \"Donate\" (node 700), focused", Assert.Single(archive.ReadingPosition.RowsAt(3 * Second)).Value);
    }
}

using Recorder.Contracts;
using Recorder.Recreation;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// Slice 4b sub-step 2b-i: the compositor values each node of a recorded
/// document takes in the recreation, chosen from the compositor frames of the
/// document's frame sink presented at or before the frame's composition and
/// joined to nodes through the compositor-animation-started records.
/// </summary>
public sealed class RecordedCompositorValuesTests
{
    private const string SessionId = "session-c";
    private const string DocumentKey = "T1 dom-document-1";
    private const long Frequency = 10_000_000;

    private static string Context(string? token = null) =>
        token is null
            ? """{"browserInstanceId":"b1","processId":5,"documentId":null,"documentToken":null}"""
            : $$"""{"browserInstanceId":"b1","processId":5,"documentId":"dom-document-1","documentToken":"{{token}}"}""";

    private static string Feedback(string token = "T1") =>
        $$"""{"context":{{Context(token)}},"frameSinkId":"6:3","frameToken":"1","requestId":"presentation-request-1","presentedTicks":"10","flags":["vsync"]}""";

    private static string Started(long node, long animation, string models, string token = "T1") =>
        $$"""{"compositorAnimationId":{{animation}},"context":{{Context(token)}},"nodeId":{{node}},"keyframeModels":[{{models}}]}""";

    private static string Model(string element, string elementNamespace, long id) =>
        $$"""{"elementId":"{{element}}","elementIdNamespace":"{{elementNamespace}}","keyframeModelId":{{id}},"targetProperty":"x"}""";

    private static string Ended(long node, long id) =>
        $$"""{"compositorAnimationId":1,"context":{{Context("T1")}},"keyframeModelIds":[{{id}}],"nodeId":{{node}}}""";

    private static string Frame(string token, string changes, string sink = "6:3", long host = 1) =>
        $$"""{"context":{{Context()}},"layerTreeHostId":{{host}},"frameToken":"{{token}}","widget":{"frameSinkId":"{{sink}}","localRootFrameToken":"L1","widgetKind":"frame"},"changes":[{{changes}}]}""";

    private static string Change(string element, string property, string value) =>
        $$"""{"elementId":"{{element}}","property":"{{property}}","value":{{value}}}""";

    private static string Presented(string token, long presentedNanoseconds, long recordNanoseconds, long host = 1, bool failed = false) =>
        $$"""{"context":{{Context()}},"layerTreeHostId":{{host}},"frameToken":"{{token}}","failed":{{(failed ? "true" : "false")}},"highResolutionTicks":true,"presentedTicks":"{{NativeTicks(recordNanoseconds) + (presentedNanoseconds - recordNanoseconds) / 100}}","presentedTimeTicksMicroseconds":null,"widget":{{Widget}}}""";

    private const string Widget = """{"frameSinkId":"6:3","localRootFrameToken":"L1","widgetKind":"frame"}""";

    private static long NativeTicks(long nanoseconds) => 50_000_000 + nanoseconds / 100;

    private const string Rotation = "[0.5,0.8660254037844386,0.0,0.0,-0.8660254037844386,0.5,0.0,0.0,0.0,0.0,1.0,0.0,0.0,0.0,0.0,1.0]";
    private const string Identity = "[1.0,0.0,0.0,0.0,0.0,1.0,0.0,0.0,0.0,0.0,1.0,0.0,0.0,0.0,0.0,1.0]";

    private static (long Time, string Channel, string Type, string Json)[] Records() =>
    [
        (1_000, "browser.presentation", "presentation-feedback", Feedback()),
        (1_100, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorAnimationStarted,
            Started(90, 1, Model("10", "rotate-transform", 1) + "," + Model("11", "primary-transform", 2))),
        (1_200, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorAnimationStarted,
            Started(170, 2, Model("20", "primary-effect", 3))),
        (1_300, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorAnimationStarted,
            Started(256, 3, Model("30", "primary-effect", 4))),
        // Another document's animation, on an element ID of its own.
        (1_400, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorAnimationStarted,
            Started(7, 4, Model("40", "primary-effect", 5), token: "T2")),
        (2_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame,
            Frame("1", string.Join(",",
                Change("10", "transform", Identity),
                Change("20", "opacity", "0.25"),
                Change("30", "backdrop-filter", """[{"numbers":[4.5],"type":"blur"}]"""),
                Change("40", "opacity", "0.5"),
                Change("50", "opacity", "0.75"),
                Change("10", "scroll-offset", """{"x":0.0,"y":0.0}""")))),
        (2_600, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("1", 2_500, 2_600)),
        (3_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame,
            Frame("2", string.Join(",",
                Change("10", "transform", Rotation),
                Change("20", "opacity", "1e-05"),
                Change("30", "filter", """[{"numbers":[0.5],"type":"grayscale"},{"numbers":[1.0,2.0,3.0,0.0,0.0,0.0,1.0],"type":"drop-shadow"}]"""),
                Change("11", "transform", Identity)))),
        // Another frame sink's compositor, presented before the composition.
        (3_100, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame,
            Frame("1", Change("20", "opacity", "0.9"), sink: "9:9", host: 2)),
        (3_200, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("1", 3_150, 3_200, host: 2)),
        (3_600, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("2", 3_500, 3_600)),
        // Drawn but its presentation failed: its value still stood on the
        // compositor through the next presented frame.
        (3_650, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame,
            Frame("2a", Change("10", "transform", Rotation.Replace("0.5,", "0.25,", StringComparison.Ordinal)))),
        (3_660, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("2a", 3_655, 3_660, failed: true)),
        // The fade ends; a rendering update of the document is presented after.
        (4_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorAnimationEnded, Ended(170, 3)),
        (4_500, "browser.presentation", "presentation-feedback", Feedback()),
        // The backdrop filter is released.
        (5_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame,
            Frame("3", Change("30", "backdrop-filter", "null") + "," + Change("11", "transform", "[1.0]"))),
        (5_600, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("3", 5_500, 5_600)),
        // Presented after the compositions the tests choose at.
        (7_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame,
            Frame("4", Change("10", "transform", Identity))),
        (9_600, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("4", 9_500, 9_600)),
    ];

    private static RecordedCompositorValues Choose(long composition)
    {
        var collector = Collector("test.browser", BrowserEvidenceChannels.Compositor, "browser.presentation");
        var chooser = new RecordedCompositorValueChooser(DocumentKey, Frequency);
        var index = 0UL;
        foreach (var record in Records())
        {
            chooser.Add(Event(SessionId, collector, ++index, record.Time, record.Channel, record.Type) with
            {
                Payload = Json(record.Json),
                NativeTimestamp = new NativeTimestamp("qpc", NativeTicks(record.Time), "ticks"),
            });
        }
        return chooser.Choose(composition);
    }

    [Fact]
    public void EachNodeTakesTheLatestValuesAsOfTheLastFramePresented()
    {
        var values = Choose(3_700);
        Assert.Equal(
            "primary-transform 1.0 0.0 0.0 0.0 0.0 1.0 0.0 0.0 0.0 0.0 1.0 0.0 0.0 0.0 0.0 1.0; rotate-transform 0.5 0.8660254037844386 0.0 0.0 -0.8660254037844386 0.5 0.0 0.0 0.0 0.0 1.0 0.0 0.0 0.0 0.0 1.0",
            values.Attribute(90));
        // The number text is the recording's, unchanged, and the other
        // frame sink's compositor is not the document's.
        Assert.Equal("opacity 1e-05", values.Attribute(170));
        Assert.Equal("backdrop-filter blur 4.5; filter grayscale 0.5, drop-shadow 1.0 2.0 3.0 0.0 0.0 0.0 1.0", values.Attribute(256));
        // Another document's element, and an element with no start record.
        Assert.Null(values.Attribute(7));
        Assert.Equal(3, values.ByNode.Count);
        Assert.Contains(values.Notes, note => note.StartsWith("5 compositor values are imposed on 3 elements, as of compositor frame 2", StringComparison.Ordinal));
        Assert.Contains(values.Notes, note => note.StartsWith("2 compositor values have no compositor-animation-started record", StringComparison.Ordinal));
    }

    [Fact]
    public void BeforeAnyPresentedFrameNothingIsImposed()
    {
        var values = Choose(2_499);
        Assert.Empty(values.ByNode);
        Assert.Contains(values.Notes, note => note.StartsWith("No compositor frame of the page's frame sink", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFirstPresentedFrameGivesItsOwnValues()
    {
        var values = Choose(2_500);
        Assert.Equal("opacity 0.25", values.Attribute(170));
        Assert.StartsWith("rotate-transform 1.0 0.0", values.Attribute(90));
    }

    [Fact]
    public void AnEndedAnimationsValueIsDroppedOnceALaterUpdateIsPresented()
    {
        Assert.Equal("opacity 1e-05", Choose(4_400).Attribute(170));
        var values = Choose(4_600);
        Assert.Null(values.Attribute(170));
        Assert.Contains(values.Notes, note => note.StartsWith("1 compositor values belong to animations that had ended", StringComparison.Ordinal));
    }

    [Fact]
    public void AReleasedOrMalformedValueIsNotImposed()
    {
        var values = Choose(6_000);
        Assert.Equal("filter grayscale 0.5, drop-shadow 1.0 2.0 3.0 0.0 0.0 0.0 1.0", values.Attribute(256));
        // Frame 2a was not presented, but its value stood through frame 3;
        // frame 4 was presented after the composition.
        Assert.Contains("rotate-transform 0.25 ", values.Attribute(90), StringComparison.Ordinal);
        Assert.Contains(values.Notes, note => note.StartsWith("1 compositor values were no longer in the drawn tree", StringComparison.Ordinal));
        Assert.Contains(values.Notes, note => note.StartsWith("1 compositor values are of a namespace or form", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyFilterListIsTheKeyAlone()
    {
        var collector = Collector("test.browser", BrowserEvidenceChannels.Compositor, "browser.presentation");
        var chooser = new RecordedCompositorValueChooser(DocumentKey, Frequency);
        var records = new (long, string, string, string)[]
        {
            (1_000, "browser.presentation", "presentation-feedback", Feedback()),
            (1_100, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorAnimationStarted, Started(256, 3, Model("30", "primary-effect", 4))),
            (2_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame, Frame("1", Change("30", "filter", "[]"))),
            (2_600, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("1", 2_500, 2_600)),
        };
        var index = 0UL;
        foreach (var (time, channel, type, json) in records)
        {
            chooser.Add(Event(SessionId, collector, ++index, time, channel, type) with
            {
                Payload = Json(json),
                NativeTimestamp = new NativeTimestamp("qpc", NativeTicks(time), "ticks"),
            });
        }
        Assert.Equal("filter", chooser.Choose(3_000).Attribute(256));
    }

    [Fact]
    public void ADocumentWithoutAPresentationHasNoValues()
    {
        var chooser = new RecordedCompositorValueChooser("T3 dom-document-1", Frequency);
        Assert.Same(RecordedCompositorValues.None, chooser.Choose(3_700));
    }
}

using Recorder.Contracts;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// Slice 4b sub-step 2b-iii change 2: the display latency subtracted from a
/// frame's composition before compositor values and image frames are chosen,
/// one and a half refreshes of the interval recorded with the document's
/// latest presentation feedback, so that the frame presented two refreshes
/// before is chosen.
/// </summary>
public sealed class RecordedDisplayLatencyTests
{
    private const string SessionId = "session-l";
    private const string DocumentKey = "T1 dom-document-1";
    private const long Frequency = 10_000_000;

    private static string Feedback(string interval, string token = "T1") =>
        $$"""{"context":{"browserInstanceId":"b1","processId":5,"documentId":"dom-document-1","documentToken":"{{token}}"},"frameSinkId":"6:3","frameToken":"1","requestId":"presentation-request-1","presentedTicks":"10","flags":["vsync"],"intervalMicroseconds":{{interval}}}""";

    private static RecordedDisplayLatencyReader Reader(params (long Time, string Json)[] records)
    {
        var collector = Collector("test.browser", "browser.presentation");
        var reader = new RecordedDisplayLatencyReader(DocumentKey);
        var index = 0UL;
        foreach (var (time, json) in records)
        {
            reader.Add(Event(SessionId, collector, ++index, time, "browser.presentation", "presentation-feedback") with
            {
                Payload = Json(json),
            });
        }
        return reader;
    }

    [Fact]
    public void TheLatencyIsTwoOfTheLatestRecordedIntervals()
    {
        var reader = Reader(
            (1_000, Feedback("\"16666\"")),
            (5_000, Feedback("\"8333\"")),
            // Another document's interval, and an unreadable one.
            (6_000, Feedback("\"4000\"", token: "T2")),
            (7_000, Feedback("\"x\"")));
        Assert.Null(reader.At(999));
        Assert.Equal(new RecordedDisplayLatency(24_999_000, 16_666), reader.At(1_000));
        Assert.Equal(new RecordedDisplayLatency(24_999_000, 16_666), reader.At(4_999));
        Assert.Equal(new RecordedDisplayLatency(12_499_500, 8_333), reader.At(5_000));
        Assert.Equal(new RecordedDisplayLatency(12_499_500, 8_333), reader.At(9_000));
    }

    [Fact]
    public void ANumberIntervalIsRead()
    {
        Assert.Equal(16_666, Reader((1_000, Feedback("16666"))).At(2_000)!.IntervalMicroseconds);
    }

    [Fact]
    public void TheNoteSaysTheLatencyIsInferred()
    {
        var note = new RecordedDisplayLatency(24_999_000, 16_666).Note;
        Assert.StartsWith("The compositor values, compositor scroll positions, and animated image frames are those of the compositor frame presented 2 display refreshes before the frame's composition, of 16.666 ms each", note, StringComparison.Ordinal);
        Assert.Contains("the last presented at least 25.0 ms, one and a half refreshes, before it", note, StringComparison.Ordinal);
        Assert.Contains("inferred from measurements of one machine's recordings, not recorded", note, StringComparison.Ordinal);
    }

    // The latency moves the choice by its length: a frame presented exactly
    // at the composition less the latency is chosen, and one presented just
    // after it is not.
    private static string Context(string? token = null) =>
        token is null
            ? """{"browserInstanceId":"b1","processId":5,"documentId":null,"documentToken":null}"""
            : $$"""{"browserInstanceId":"b1","processId":5,"documentId":"dom-document-1","documentToken":"{{token}}"}""";

    private const string Widget = """{"frameSinkId":"6:3","localRootFrameToken":"L1","widgetKind":"frame"}""";

    private static long NativeTicks(long nanoseconds) => 50_000_000 + nanoseconds / 100;

    private static string Frame(string token, string opacity) =>
        $$"""{"context":{{Context()}},"layerTreeHostId":1,"frameToken":"{{token}}","widget":{{Widget}},"changes":[{"elementId":"20","property":"opacity","value":{{opacity}}}]}""";

    private static string Presented(string token, long presentedNanoseconds, long recordNanoseconds) =>
        $$"""{"context":{{Context()}},"layerTreeHostId":1,"frameToken":"{{token}}","failed":false,"highResolutionTicks":true,"presentedTicks":"{{NativeTicks(recordNanoseconds) + (presentedNanoseconds - recordNanoseconds) / 100}}","presentedTimeTicksMicroseconds":null,"widget":{{Widget}}}""";

    private static RecordedCompositorValues ChooseWithLatency(long composition)
    {
        var refresh = 16_666_000L;
        (long Time, string Channel, string Type, string Json)[] records =
        [
            (1_000, "browser.presentation", "presentation-feedback",
                $$"""{"context":{{Context("T1")}},"frameSinkId":"6:3","frameToken":"1","requestId":"r1","presentedTicks":"10","flags":["vsync"],"intervalMicroseconds":"16666"}"""),
            (1_100, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorAnimationStarted,
                $$"""{"compositorAnimationId":1,"context":{{Context("T1")}},"nodeId":170,"keyframeModels":[{"elementId":"20","elementIdNamespace":"primary-effect","keyframeModelId":1,"targetProperty":"x"}]}"""),
            (2_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame, Frame("1", "0.1")),
            (100_000_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("1", 100_000_000, 100_000_000)),
            (100_000_100, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame, Frame("2", "0.2")),
            (100_000_000 + refresh, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("2", 100_000_000 + refresh, 100_000_000 + refresh)),
            (100_000_000 + refresh + 100, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame, Frame("3", "0.3")),
            (100_000_000 + 2 * refresh, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("3", 100_000_000 + 2 * refresh, 100_000_000 + 2 * refresh)),
        ];
        var collector = Collector("test.browser", BrowserEvidenceChannels.Compositor, "browser.presentation");
        var chooser = new RecordedCompositorValueChooser(DocumentKey, Frequency);
        var latencies = new RecordedDisplayLatencyReader(DocumentKey);
        var index = 0UL;
        foreach (var record in records)
        {
            var item = Event(SessionId, collector, ++index, record.Time, record.Channel, record.Type) with
            {
                Payload = Json(record.Json),
                NativeTimestamp = new NativeTimestamp("qpc", NativeTicks(record.Time), "ticks"),
            };
            chooser.Add(item);
            latencies.Add(item);
        }
        return chooser.Choose(composition - latencies.At(composition)!.Nanoseconds);
    }

    [Fact]
    public void TheFramePresentedTwoRefreshesBeforeIsChosen()
    {
        const long refresh = 16_666_000;
        const long first = 100_000_000;
        // As on the target machine, compositions fall 0.1 ms or so before a
        // whole number of refreshes after a presentation: frame 1 is the one
        // presented two refreshes before each of these.
        Assert.Equal("opacity 0.1", ChooseWithLatency(first + 2 * refresh - 100_000).Attribute(170));
        Assert.Equal("opacity 0.1", ChooseWithLatency(first + 2 * refresh + 100_000).Attribute(170));
        // One and a half refreshes after frame 2 is presented, frame 2 is
        // chosen; just before it, frame 1 still is.
        Assert.Equal("opacity 0.1", ChooseWithLatency(first + refresh + 24_999_000 - 1_000).Attribute(170));
        Assert.Equal("opacity 0.2", ChooseWithLatency(first + refresh + 24_999_000).Attribute(170));
        Assert.Equal("opacity 0.3", ChooseWithLatency(first + 4 * refresh - 100_000).Attribute(170));
    }
}

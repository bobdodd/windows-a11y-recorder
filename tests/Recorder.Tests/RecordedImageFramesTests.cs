using System.Security.Cryptography;
using System.Text;
using Recorder.Contracts;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using Recorder.Recreation;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// Slice 4b sub-step 2a: the frame each animated image of a recorded
/// document is held at in the recreation, chosen from the compositor frames
/// of the document's frame sink presented at or before the frame's
/// composition, and sent with the image in the recorder's answer.
/// </summary>
public sealed class RecordedImageFramesTests : IDisposable
{
    private const string SessionId = "session-i";
    private const string DocumentKey = "T1 dom-document-1";
    private const long Frequency = 10_000_000;
    private static readonly byte[] ImageBytes = Encoding.ASCII.GetBytes("GIF89a frames");
    private static readonly byte[] StillBytes = Encoding.ASCII.GetBytes("\u0089PNG still");
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recorded-image-frames-" + Guid.NewGuid().ToString("N"));

    public RecordedImageFramesTests() => Directory.CreateDirectory(_directory);

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

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Context(long process = 5, string? token = null) =>
        token is null
            ? $$"""{"browserInstanceId":"b1","processId":{{process}},"documentId":null,"documentToken":null}"""
            : $$"""{"browserInstanceId":"b1","processId":{{process}},"documentId":"dom-document-1","documentToken":"{{token}}"}""";

    private static string Feedback(string sink) =>
        $$"""{"context":{{Context(token: "T1")}},"frameSinkId":"{{sink}}","frameToken":"1","requestId":"presentation-request-1","presentedTicks":"10","flags":["vsync"]}""";

    private static string Resource(string url, string? imageId, byte[] bytes, long process = 5) =>
        $$"""{"context":{{Context(process)}},"url":"{{url}}","responseUrl":null,"status":200,"mimeType":"image/gif","size":"{{bytes.Length}}","digest":"{{Digest(bytes)}}","dataRecorded":true,"imageId":{{(imageId is null ? "null" : $"\"{imageId}\"")}}}""";

    private static string PaintImage(string imageId, string paintImageId, string sequence = "shared") =>
        $$"""{"context":{{Context()}},"imageId":"{{imageId}}","paintImageId":"{{paintImageId}}","sequence":"{{sequence}}","nodeId":{{(sequence == "own" ? "44" : "null")}},"syncTargetPaintImageId":null}""";

    private static string Frame(string token, string sink, long host, string changes) =>
        $$"""{"context":{{Context()}},"layerTreeHostId":{{host}},"frameToken":"{{token}}","sourceFrameNumber":1,"beginFrameTicks":"1","beginFrameTimeTicksMicroseconds":"1","highResolutionTicks":true,"widget":{"frameSinkId":"{{sink}}","localRootFrameToken":"L1","widgetKind":"frame"},"changes":[{{changes}}]}""";

    private static string ImageFrame(string paintImageId, string value) =>
        $$"""{"paintImageId":"{{paintImageId}}","property":"image-frame","value":{{value}}}""";

    // Presented at the given recording time: the presentation's ticks are
    // the record's own native timestamp moved by that time less the record's.
    private static string Presented(string token, long host, long? presentedNanoseconds, long recordNanoseconds) =>
        $$"""{"context":{{Context()}},"layerTreeHostId":{{host}},"frameToken":"{{token}}","failed":{{(presentedNanoseconds is null ? "true" : "false")}},"highResolutionTicks":true,"presentedTicks":{{(presentedNanoseconds is { } presented ? $"\"{(NativeTicks(recordNanoseconds) + (presented - recordNanoseconds) / 100).ToString(System.Globalization.CultureInfo.InvariantCulture)}\"" : "null")}},"presentedTimeTicksMicroseconds":null,"widget":{{Widget}}}""";

    private const string Widget = """{"frameSinkId":"6:3","localRootFrameToken":"L1","widgetKind":"frame"}""";

    private static long NativeTicks(long nanoseconds) => 50_000_000 + nanoseconds / 100;

    // The sequence the tests choose from. Paint image 0 is a.gif's shared
    // sequence; frames 1 to 6 are the document's frame sink's, on host 1.
    private static (long Time, string Channel, string Type, string Json)[] Records() =>
    [
        (100, BrowserEvidenceChannels.Resources, BrowserEvidenceEventTypes.ImageData, Bytes(ImageBytes)),
        (110, BrowserEvidenceChannels.Resources, BrowserEvidenceEventTypes.ImageData, Bytes(StillBytes)),
        (1_000, "browser.presentation", "presentation-feedback", Feedback("6:3")),
        (1_100, BrowserEvidenceChannels.Resources, BrowserEvidenceEventTypes.ImageResource, Resource("https://example.test/a.gif", "0", ImageBytes)),
        (1_150, BrowserEvidenceChannels.Resources, BrowserEvidenceEventTypes.ImageResource, Resource("https://example.test/still.png", "1", StillBytes)),
        // Another renderer's image at the same URL, with an ID of its own.
        (1_200, BrowserEvidenceChannels.Resources, BrowserEvidenceEventTypes.ImageResource, Resource("https://example.test/a.gif", "7", ImageBytes, process: 9)),
        (1_300, BrowserEvidenceChannels.Resources, BrowserEvidenceEventTypes.ImagePaintImage, PaintImage("0", "0")),
        (1_310, BrowserEvidenceChannels.Resources, BrowserEvidenceEventTypes.ImagePaintImage, PaintImage("1", "1")),
        (1_320, BrowserEvidenceChannels.Resources, BrowserEvidenceEventTypes.ImagePaintImage, PaintImage("0", "4", "own")),
        (2_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame, Frame("1", "6:3", 1, ImageFrame("0", "0") + "," + ImageFrame("4", "6"))),
        (2_600, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("1", 1, 2_500, 2_600)),
        (3_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame, Frame("2", "6:3", 1, ImageFrame("0", "1"))),
        (3_600, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("2", 1, 3_500, 3_600)),
        // Drawn but not presented: its value is still the compositor's after it.
        (4_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame, Frame("3", "6:3", 1, ImageFrame("0", "2"))),
        (4_100, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("3", 1, null, 4_100)),
        (5_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame,
            Frame("4", "6:3", 1, """{"elementId":"9","property":"opacity","value":0.5}""")),
        (5_300, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("4", 1, 5_200, 5_300)),
        // Presented after the composition the tests choose at 7 000.
        (6_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame, Frame("5", "6:3", 1, ImageFrame("0", "3"))),
        // Another frame sink's compositor, presented before the composition.
        (6_500, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame, Frame("1", "9:9", 2, ImageFrame("0", "6"))),
        (6_600, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("1", 2, 6_550, 6_600)),
        (7_600, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("5", 1, 7_500, 7_600)),
        // The compositor releases the image.
        (8_000, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame, Frame("6", "6:3", 1, ImageFrame("0", "null"))),
        (8_200, BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented, Presented("6", 1, 8_100, 8_200)),
    ];

    private static string Bytes(byte[] bytes) =>
        $$"""{"context":{{Context()}},"digest":"{{Digest(bytes)}}","size":"{{bytes.Length}}","bytes":"{{Convert.ToBase64String(bytes)}}"}""";

    private static RecorderEvent[] Events()
    {
        var collector = Collector("test.browser", BrowserEvidenceChannels.Resources, BrowserEvidenceChannels.Compositor, "browser.presentation");
        return Records()
            .Select((record, index) =>
                Event(SessionId, collector, (ulong)index + 1, record.Time, record.Channel, record.Type) with
                {
                    Payload = Json(record.Json),
                    NativeTimestamp = new NativeTimestamp("qpc", NativeTicks(record.Time), "ticks"),
                })
            .ToArray();
    }

    private static RecordedImageFrames Choose(long cut, long composition)
    {
        var chooser = new RecordedImageFrameChooser(DocumentKey, Frequency);
        foreach (var record in Events())
        {
            chooser.Add(record);
        }
        return chooser.Choose(cut, composition);
    }

    [Fact]
    public void TheFrameIsTheLatestAsOfTheLastFramePresentedAtOrBeforeTheComposition()
    {
        // Frame 4 is the last presented by 7 000. Frame 3 was not presented,
        // but its value stood on the compositor through frame 4; frame 5 was
        // presented after the composition, and the other frame sink's frame
        // is not the document's.
        var frames = Choose(cut: 6_000, composition: 7_000);
        var frame = frames.Frame("https://example.test/a.gif#part")!;
        Assert.Equal(2, frame.Index);
        Assert.Equal("3", frame.FrameToken);
        Assert.Equal(5_200, frame.PresentedNanoseconds);
        // The still image's paint image never changed frame, and the own
        // sequence's change is not the image's.
        Assert.Null(frames.Frame("https://example.test/still.png"));
        Assert.Single(frames.ByUrl);
        Assert.Contains(frames.Notes, note => note.StartsWith("1 animated image addresses are held at the frame recorded for them, as of compositor frame 4, presented at 0.000 s: https://example.test/a.gif at frame 2, last changed in compositor frame 3.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2_499, null)]
    [InlineData(2_500, 0)]
    [InlineData(3_500, 1)]
    [InlineData(5_199, 1)]
    [InlineData(7_500, 3)]
    public void TheFrameFollowsThePresentations(long composition, int? expected)
    {
        var frame = Choose(cut: 2_000, composition).Frame("https://example.test/a.gif");
        Assert.Equal(expected, frame?.Index);
    }

    [Fact]
    public void BeforeAnyPresentedFrameEveryImageIsHeldAtItsFirstFrame()
    {
        var frames = Choose(cut: 2_000, composition: 2_499);
        Assert.Empty(frames.ByUrl);
        Assert.Contains(frames.Notes, note => note.StartsWith("No compositor frame of the page's frame sink was presented", StringComparison.Ordinal));
    }

    [Fact]
    public void AnImageTheCompositorReleasedIsHeldAtItsFirstFrame()
    {
        var frames = Choose(cut: 6_000, composition: 9_000);
        Assert.Empty(frames.ByUrl);
        Assert.Contains(frames.Notes, note => note.StartsWith("1 animated image addresses had been released by the compositor", StringComparison.Ordinal));
    }

    [Fact]
    public void AnImageIsTheOneTheDocumentsRendererHadAtTheCut()
    {
        // Before the cut reaches a.gif's record, the document's renderer has
        // no image at that URL; another renderer's record is not taken.
        Assert.Null(Choose(cut: 1_099, composition: 7_000).Frame("https://example.test/a.gif"));
        Assert.NotNull(Choose(cut: 1_100, composition: 7_000).Frame("https://example.test/a.gif"));
    }

    [Fact]
    public void ADocumentWithoutAPresentationHasNoImageFrames()
    {
        var chooser = new RecordedImageFrameChooser("T2 dom-document-1", Frequency);
        foreach (var record in Events())
        {
            chooser.Add(record);
        }
        Assert.Same(RecordedImageFrames.None, chooser.Choose(6_000, 7_000));
    }

    private async Task<string> WriteAsync()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var events = Events();
        using var target = new RecordingFileBatchTarget(
            path,
            new Dictionary<string, string> { ["sessionKey"] = SessionId, ["clockFrequency"] = Frequency.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new RecordingFileWriterOptions { ChunkBytes = 1024 });
        var batch = events.Select((record, index) => new BufferedEvent(index, record, record.Payload.GetRawText())).ToArray();
        Assert.Empty(await target.WriteAsync(new EventBatch(batch, [], []), TestContext.Current.CancellationToken));
        target.Finish();
        return path;
    }

    [Fact]
    public async Task TheRecreationSendsTheHeldFrameWithTheImage()
    {
        var path = await WriteAsync();
        var resources = RecordingFileResources.Read(path, DocumentKey, 6_000, TestContext.Current.CancellationToken, compositionNanoseconds: 7_000);
        Assert.NotNull(resources.ImageFramesMilliseconds);
        Assert.Equal(2, resources.ImageFrames.Frame("https://example.test/a.gif")!.Index);
        Assert.Contains(resources.Notes, note => note.Contains("https://example.test/a.gif at frame 2", StringComparison.Ordinal));
        var content = new RecreationContent("<p>page</p>", FixedRecreation.Create().Evidence, "n0nce")
        {
            DocumentUrl = "https://example.test/page",
            Resources = resources,
            FontAddress = RecreationServer.FontAddress("f0nt"),
        };
        await using var server = await RecreationServer.StartAsync(content, TestContext.Current.CancellationToken);

        var image = server.Answer("https://example.test/a.gif", "Image")!;
        Assert.Equal(ImageBytes, image.Body);
        Assert.Contains(new KeyValuePair<string, string>(RecreationServer.ImageFrameHeader, "2"), image.Headers);
        Assert.Equal("X-A11y-Recorder-Image-Frame", RecreationServer.ImageFrameHeader);
        // An image with no recorded frame has no header.
        var still = server.Answer("https://example.test/still.png", "Image")!;
        Assert.DoesNotContain(still.Headers, header => header.Key == RecreationServer.ImageFrameHeader);
    }

    [Fact]
    public async Task WithoutACompositionNoImageFrameIsRead()
    {
        var path = await WriteAsync();
        using var resources = RecordingFileResources.Read(path, DocumentKey, 6_000, TestContext.Current.CancellationToken);
        Assert.Null(resources.ImageFramesMilliseconds);
        Assert.Empty(resources.ImageFrames.ByUrl);
    }
}

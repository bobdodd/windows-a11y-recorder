using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// The frame an animated image of a recorded document is held at in the
/// recreation: the image-frame value of its paint image as of the last
/// compositor frame of the document's frame sink presented at or before the
/// frame's composition, with that compositor frame's token and its
/// presentation time in recording time.
/// </summary>
public sealed record RecordedImageFrame(
    string Url,
    int Index,
    string FrameToken,
    long PresentedNanoseconds);

/// <summary>
/// The held frames of a document's images at a frame, by URL without its
/// fragment, and what the evidence panel says of them.
/// </summary>
public sealed record RecordedImageFrames(
    IReadOnlyDictionary<string, RecordedImageFrame> ByUrl,
    IReadOnlyList<string> Notes)
{
    public static RecordedImageFrames None { get; } =
        new(new Dictionary<string, RecordedImageFrame>(StringComparer.Ordinal), []);

    public RecordedImageFrame? Frame(string url) =>
        ByUrl.TryGetValue(RecordedPageResources.WithoutFragment(url), out var frame) ? frame : null;
}

/// <summary>
/// Chooses the frame each animated image of a document is held at in the
/// recreation (slice 4b, "Sub-step 2a design: animated images held"), from
/// the records it is given: the document's presentation records, the
/// browser's clock synchronizations, the image-resource and
/// image-paint-image records, and the compositor-frame and
/// compositor-frame-presented records (protocol 0.48).
/// </summary>
public sealed class RecordedImageFrameChooser
{
    private readonly string _documentToken;
    private readonly long _recordingFrequency;
    private readonly List<(long Time, string? Instance, long? Process, string FrameSink)> _presentations = [];
    private readonly Dictionary<(string? Instance, long? Process), decimal> _frequencies = [];
    private readonly List<(long Time, string? Instance, long? Process, string Url, string? ResponseUrl, string? ImageId)> _resources = [];
    private readonly Dictionary<(string? Instance, long? Process, string ImageId), string> _sharedPaintImages = [];
    private readonly List<CompositorFrame> _frames = [];
    private readonly Dictionary<(string? Instance, long? Process, long Host, string Token), long> _presented = [];

    private sealed record CompositorFrame(
        string? Instance,
        long? Process,
        string FrameSink,
        long Host,
        string Token,
        IReadOnlyList<(string PaintImageId, int? Index)> ImageFrames);

    /// <param name="documentKey">The document's state key, its token and identity.</param>
    /// <param name="recordingFrequency">The recording's clock frequency, for a process with no clock synchronization record.</param>
    public RecordedImageFrameChooser(string documentKey, long recordingFrequency)
    {
        ArgumentNullException.ThrowIfNull(documentKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordingFrequency, 1);
        _documentToken = documentKey.Split(' ', 2)[0];
        _recordingFrequency = recordingFrequency;
    }

    /// <summary>The channels whose records the chooser reads.</summary>
    public static IReadOnlySet<string> Channels { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "browser.presentation",
        "browser.lifecycle",
        BrowserEvidenceChannels.Resources,
        BrowserEvidenceChannels.Compositor,
    };

    /// <summary>Takes one record, in the order recorded.</summary>
    public void Add(RecorderEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var payload = record.Payload;
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        var context = payload.TryGetProperty("context", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
        var instance = Text(context, "browserInstanceId");
        var process = Int64(context, "processId");
        switch ((record.Channel, record.EventType))
        {
            case ("browser.presentation", "presentation-feedback"):
            case ("browser.presentation", "presentation-swapped"):
                if (Text(context, "documentToken") == _documentToken && Text(payload, "frameSinkId") is { } sink)
                {
                    _presentations.Add((record.MonotonicNanoseconds, instance, process, sink));
                }
                break;
            case ("browser.lifecycle", "browser-clock-synchronized"):
                if (Text(payload, "monotonicFrequency") is { } frequencyText &&
                    decimal.TryParse(frequencyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var frequency) &&
                    frequency > 0)
                {
                    // The process's first synchronization, as the playback
                    // index takes it.
                    _frequencies.TryAdd((Text(payload, "browserInstanceId"), Int64(payload, "processId")), frequency);
                }
                break;
            case (BrowserEvidenceChannels.Resources, BrowserEvidenceEventTypes.ImageResource):
                if (Text(payload, "url") is { } url)
                {
                    _resources.Add((record.MonotonicNanoseconds, instance, process, url, Text(payload, "responseUrl"), Text(payload, "imageId")));
                }
                break;
            case (BrowserEvidenceChannels.Resources, BrowserEvidenceEventTypes.ImagePaintImage):
                if (Text(payload, "sequence") == "shared" &&
                    Text(payload, "imageId") is { } imageId &&
                    Text(payload, "paintImageId") is { } paintImageId)
                {
                    _sharedPaintImages[(instance, process, imageId)] = paintImageId;
                }
                break;
            case (BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFrame):
                if (Int64(payload, "layerTreeHostId") is { } host &&
                    Text(payload, "frameToken") is { } token &&
                    payload.TryGetProperty("widget", out var widget) &&
                    Text(widget, "frameSinkId") is { } frameSink &&
                    payload.TryGetProperty("changes", out var changes) &&
                    changes.ValueKind == JsonValueKind.Array)
                {
                    var images = new List<(string, int?)>();
                    foreach (var change in changes.EnumerateArray())
                    {
                        if (Text(change, "property") != "image-frame" || Text(change, "paintImageId") is not { } paintImage)
                        {
                            continue;
                        }
                        var index = change.TryGetProperty("value", out var frameValue) &&
                                    frameValue.ValueKind == JsonValueKind.Number &&
                                    frameValue.TryGetInt32(out var number) && number >= 0
                            ? number
                            : (int?)null;
                        images.Add((paintImage, index));
                    }
                    _frames.Add(new CompositorFrame(instance, process, frameSink, host, token, images));
                }
                break;
            case (BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorFramePresented):
                if (payload.TryGetProperty("failed", out var failed) && failed.ValueKind == JsonValueKind.False &&
                    Int64(payload, "layerTreeHostId") is { } presentedHost &&
                    Text(payload, "frameToken") is { } presentedToken &&
                    Text(payload, "presentedTicks") is { } ticksText &&
                    decimal.TryParse(ticksText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) &&
                    record.NativeTimestamp is { } native)
                {
                    var key = (instance, process);
                    var processFrequency = _frequencies.TryGetValue(key, out var recorded) ? recorded : _recordingFrequency;
                    // As the playback index maps a presentation time: from
                    // the record's own native timestamp, on the same clock.
                    var offset = decimal.Round(
                        (ticks - native.Value) * 1_000_000_000m / processFrequency,
                        MidpointRounding.AwayFromZero);
                    _presented[(instance, process, presentedHost, presentedToken)] = record.MonotonicNanoseconds + (long)offset;
                }
                break;
        }
    }

    /// <param name="cutNanoseconds">The recording time the document's state is read at.</param>
    /// <param name="compositionNanoseconds">The recording time of the frame's composition.</param>
    public RecordedImageFrames Choose(long cutNanoseconds, long compositionNanoseconds)
    {
        var presentation = _presentations.LastOrDefault(item => item.Time <= compositionNanoseconds);
        if (presentation.FrameSink is null)
        {
            return RecordedImageFrames.None;
        }
        var (instance, process) = (presentation.Instance, presentation.Process);

        // The last compositor frame of the document's frame sink presented
        // at or before the composition, and the image frames of its
        // compositor up to it, each the latest change.
        var last = -1;
        long presentedTime = 0;
        for (var index = 0; index < _frames.Count; index++)
        {
            var frame = _frames[index];
            if (frame.Instance == instance && frame.Process == process && frame.FrameSink == presentation.FrameSink &&
                _presented.TryGetValue((instance, process, frame.Host, frame.Token), out var presented) &&
                presented <= compositionNanoseconds)
            {
                last = index;
                presentedTime = presented;
            }
        }
        if (last < 0)
        {
            if (_frames.Count == 0)
            {
                return RecordedImageFrames.None;
            }
            return new RecordedImageFrames(RecordedImageFrames.None.ByUrl,
                ["No compositor frame of the page's frame sink was presented at or before the frame, so every animated image is held at its first frame."]);
        }
        var chosen = _frames[last];
        var values = new Dictionary<string, (int? Index, string Token)>(StringComparer.Ordinal);
        for (var index = 0; index <= last; index++)
        {
            var frame = _frames[index];
            if (frame.Instance != instance || frame.Process != process || frame.Host != chosen.Host)
            {
                continue;
            }
            foreach (var (paintImage, value) in frame.ImageFrames)
            {
                values[paintImage] = (value, frame.Token);
            }
        }

        // Each image URL's latest image-resource in the document's renderer
        // at or before the state's cut.
        var imageIds = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var resource in _resources)
        {
            if (resource.Time > cutNanoseconds || resource.Instance != instance || resource.Process != process)
            {
                continue;
            }
            imageIds[RecordedPageResources.WithoutFragment(resource.Url)] = resource.ImageId;
            if (resource.ResponseUrl is { } response && response != resource.Url)
            {
                imageIds[RecordedPageResources.WithoutFragment(response)] = resource.ImageId;
            }
        }

        var byUrl = new Dictionary<string, RecordedImageFrame>(StringComparer.Ordinal);
        var released = 0;
        foreach (var (url, imageId) in imageIds)
        {
            if (imageId is null ||
                !_sharedPaintImages.TryGetValue((instance, process, imageId), out var paintImage) ||
                !values.TryGetValue(paintImage, out var value))
            {
                continue;
            }
            if (value.Index is not { } frameIndex)
            {
                released++;
                continue;
            }
            byUrl[url] = new RecordedImageFrame(url, frameIndex, value.Token, presentedTime);
        }

        static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
        var notes = new List<string>();
        var held = byUrl.Values.DistinctBy(frame => (frame.Index, frame.FrameToken, frame.Url)).OrderBy(frame => frame.Url, StringComparer.Ordinal).ToArray();
        if (held.Length > 0)
        {
            notes.Add($"{Count(held.Length)} animated image addresses are held at the frame recorded for them, as of compositor frame {chosen.Token}, presented at {(presentedTime / 1e9).ToString("0.000", CultureInfo.InvariantCulture)} s: "
                + string.Join("; ", held.Select(frame => $"{frame.Url} at frame {Count(frame.Index)}, last changed in compositor frame {frame.FrameToken}"))
                + ". The frame is sent with each image in the X-A11y-Recorder-Image-Frame response header, which DevTools' Network panel shows.");
        }
        if (released > 0)
        {
            notes.Add($"{Count(released)} animated image addresses had been released by the compositor at the frame, so they are held at their first frame.");
        }
        notes.Add("Every other image is drawn at its first frame: a still image has only that one, and an animated image whose frame was not recorded, such as one not drawn as an animation by the frame or one an element animates on its own, is held at it. Nothing in the recreation advances an image.");
        return new RecordedImageFrames(byUrl, notes);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Int64(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var result)
            ? result
            : null;
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// One compositor value a recreation imposes on a node: the element ID's
/// namespace or the property it is written under in the node's
/// data-a11y-recorded-compositor attribute, its text there, and the
/// compositor frame that last changed it.
/// </summary>
public sealed record RecordedCompositorValue(
    string Key,
    string Text,
    string FrameToken);

/// <summary>
/// One paint worklet value a recreation imposes on a node (slice 4b,
/// "Sub-step 2c design: paint worklet colors and clip paths imposed"): the
/// property, "background-color" or "clip-path"; its text in the node's
/// data-a11y-recorded-paint-worklet attribute, without the clip path's
/// recorded origin; the progress it was painted at, as recorded, or null for
/// a paint from the main thread's value; the compositor frame that last
/// changed the progress; and when it was painted.
/// </summary>
public sealed record RecordedPaintWorkletValue(
    string Property,
    string Text,
    string? Progress,
    string FrameToken,
    long PaintedNanoseconds);

/// <summary>
/// The compositor values a document's nodes take in the recreation at a
/// frame (slice 4b, "Sub-step 2b-i design: compositor values imposed"), by
/// node, with the compositor frame they were chosen at and what the evidence
/// panel says of them.
/// </summary>
public sealed record RecordedCompositorValues(
    IReadOnlyDictionary<long, IReadOnlyList<RecordedCompositorValue>> ByNode,
    IReadOnlyList<string> Notes)
{
    public static RecordedCompositorValues None { get; } =
        new(new Dictionary<long, IReadOnlyList<RecordedCompositorValue>>(), []);

    /// <summary>
    /// The drawn scroll position of each scroll node of the document's
    /// compositor at the frame (slice 4b sub-step 2b-ii), by its compositor
    /// element ID as the records write it. A scroll node the compositor no
    /// longer had is left out.
    /// </summary>
    public IReadOnlyDictionary<string, RecordedScrollPosition> ScrollPositions { get; init; } =
        new Dictionary<string, RecordedScrollPosition>();

    /// <summary>
    /// The paint worklet values each node takes at the frame (slice 4b,
    /// sub-step 2c), by node, in the order of their properties.
    /// </summary>
    public IReadOnlyDictionary<long, IReadOnlyList<RecordedPaintWorkletValue>> PaintWorklet { get; init; } =
        new Dictionary<long, IReadOnlyList<RecordedPaintWorkletValue>>();

    /// <summary>
    /// The node's data-a11y-recorded-compositor attribute, or null: its values
    /// in the order of their keys, separated by "; ", each the key and its
    /// numbers separated by single spaces, as the recorder bridge's
    /// ParseRecreationCompositorValues reads them.
    /// </summary>
    public string? Attribute(long nodeId) =>
        ByNode.TryGetValue(nodeId, out var values) && values.Count > 0
            ? string.Join("; ", values.Select(value => value.Text))
            : null;
}

/// <summary>
/// A scroll node's position as the compositor drew it: Blink's scroll offset
/// plus the scroll origin, and the compositor frame that last changed it.
/// </summary>
public sealed record RecordedScrollPosition(double X, double Y, string FrameToken)
{
    /// <summary>
    /// Whether the compositor scrolled the node itself in that frame
    /// (protocol 0.50, sub-step 2b-iii), or null when the record does not
    /// say, as before protocol 0.50.
    /// </summary>
    public bool? IsComposited { get; init; }

    /// <summary>
    /// The reasons Chromium gave for repainting the node on the main thread,
    /// comma separated as recorded, or empty.
    /// </summary>
    public string RepaintReasons { get; init; } = "";
}

/// <summary>
/// Chooses the compositor values each node of a document takes in the
/// recreation (slice 4b, "Sub-step 2b-i design: compositor values imposed"),
/// from the records it is given: the document's presentation records, the
/// browser's clock synchronizations, and the compositor-animation-started,
/// compositor-animation-ended, compositor-frame, and
/// compositor-frame-presented records (protocol 0.48). Transforms,
/// opacities, filters, backdrop filters, and scroll positions are chosen,
/// and, from the paint-worklet-painted records, the native paint worklets'
/// background colors and clip paths (sub-step 2c); image frames are not.
/// </summary>
public sealed class RecordedCompositorValueChooser
{
    private static readonly HashSet<string> TransformNamespaces = new(StringComparer.Ordinal)
    {
        "translate-transform",
        "rotate-transform",
        "scale-transform",
        "primary-transform",
    };

    private static readonly HashSet<string> Properties = new(StringComparer.Ordinal)
    {
        "transform",
        "opacity",
        "filter",
        "backdrop-filter",
    };

    private readonly string _documentToken;
    private readonly long _recordingFrequency;
    private readonly List<(long Time, string? Instance, long? Process, string FrameSink)> _presentations = [];
    private readonly Dictionary<(string? Instance, long? Process), decimal> _frequencies = [];
    private readonly List<Started> _started = [];
    private readonly Dictionary<(string? Instance, long? Process, long KeyframeModel), long> _ended = [];
    private readonly List<CompositorFrame> _frames = [];
    private readonly Dictionary<(string? Instance, long? Process, long Host, string Token), long> _presented = [];
    private readonly List<Painted> _painted = [];

    // The two properties the native paint worklets animate, by the name of
    // their progress in a compositor-frame record.
    private static readonly Dictionary<string, string> ProgressProperties = new(StringComparer.Ordinal)
    {
        ["background-color-progress"] = "background-color",
        ["clip-path-progress"] = "clip-path",
    };

    private sealed record Painted(
        long Time,
        string? Instance,
        long? Process,
        string ElementId,
        string Property,
        double? Progress,
        string? Text);

    // A progress the compositor drew an element at: its number, or null for
    // the main thread's value; Drawn is false when the element was no longer
    // in the drawn tree.
    private sealed record ProgressChange(string ElementId, string Property, bool Drawn, double? Progress, string? ProgressText);

    private sealed record Started(
        long Time,
        string? Instance,
        long? Process,
        string ElementId,
        string Namespace,
        long NodeId,
        long KeyframeModel);

    private sealed record CompositorFrame(
        long Time,
        string? Instance,
        long? Process,
        string FrameSink,
        long Host,
        string Token,
        IReadOnlyList<(string ElementId, string Property, string? Text)> Values,
        IReadOnlyList<(string ElementId, RecordedScrollPosition? Position)> Scrolls)
    {
        public IReadOnlyList<ProgressChange> Progress { get; init; } = [];
    }

    /// <param name="documentKey">The document's state key, its token and identity.</param>
    /// <param name="recordingFrequency">The recording's clock frequency, for a process with no clock synchronization record.</param>
    public RecordedCompositorValueChooser(string documentKey, long recordingFrequency)
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
                    _frequencies.TryAdd((Text(payload, "browserInstanceId"), Int64(payload, "processId")), frequency);
                }
                break;
            case (BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorAnimationStarted):
                if (Text(context, "documentToken") == _documentToken &&
                    Int64(payload, "nodeId") is { } node &&
                    payload.TryGetProperty("keyframeModels", out var models) &&
                    models.ValueKind == JsonValueKind.Array)
                {
                    foreach (var model in models.EnumerateArray())
                    {
                        if (Text(model, "elementId") is { } element &&
                            Text(model, "elementIdNamespace") is { } elementNamespace &&
                            Int64(model, "keyframeModelId") is { } keyframeModel)
                        {
                            _started.Add(new Started(record.MonotonicNanoseconds, instance, process, element, elementNamespace, node, keyframeModel));
                        }
                    }
                }
                break;
            case (BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.CompositorAnimationEnded):
                if (payload.TryGetProperty("keyframeModelIds", out var ended) && ended.ValueKind == JsonValueKind.Array)
                {
                    foreach (var id in ended.EnumerateArray())
                    {
                        if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var keyframeModel))
                        {
                            _ended[(instance, process, keyframeModel)] = record.MonotonicNanoseconds;
                        }
                    }
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
                    var values = new List<(string, string, string?)>();
                    var scrolls = new List<(string, RecordedScrollPosition?)>();
                    var progress = new List<ProgressChange>();
                    foreach (var change in changes.EnumerateArray())
                    {
                        // Sub-step 2c: a native paint worklet's progress, a
                        // number, null for the main thread's value, or a null
                        // value for an element no longer drawn.
                        if (Text(change, "property") is { } progressName &&
                            ProgressProperties.TryGetValue(progressName, out var painted) &&
                            Text(change, "elementId") is { } worklet)
                        {
                            var drawn = change.TryGetProperty("value", out var drawnValue) ? drawnValue : default;
                            if (drawn.ValueKind == JsonValueKind.Object && drawn.TryGetProperty("progress", out var number) &&
                                number.ValueKind is JsonValueKind.Number or JsonValueKind.Null)
                            {
                                progress.Add(number.ValueKind == JsonValueKind.Number
                                    ? new ProgressChange(worklet, painted, true, number.GetDouble(), number.GetRawText())
                                    : new ProgressChange(worklet, painted, true, null, null));
                            }
                            else
                            {
                                progress.Add(new ProgressChange(worklet, painted, false, null, null));
                            }
                            continue;
                        }
                        if (Text(change, "property") == "scroll-offset" && Text(change, "elementId") is { } scroller)
                        {
                            // Slice 4b sub-step 2b-ii: a null value is a
                            // scroll node the compositor no longer had.
                            var drawn = change.TryGetProperty("value", out var position) ? position : default;
                            scrolls.Add(drawn.ValueKind == JsonValueKind.Object &&
                                        drawn.TryGetProperty("x", out var x) && x.ValueKind == JsonValueKind.Number &&
                                        drawn.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number
                                ? (scroller, new RecordedScrollPosition(x.GetDouble(), y.GetDouble(), token)
                                {
                                    IsComposited = drawn.TryGetProperty("isComposited", out var composited) &&
                                                   composited.ValueKind is JsonValueKind.True or JsonValueKind.False
                                        ? composited.GetBoolean()
                                        : null,
                                    RepaintReasons = drawn.TryGetProperty("mainThreadRepaintReasons", out var reasons) &&
                                                     reasons.ValueKind == JsonValueKind.Array
                                        ? string.Join(", ", reasons.EnumerateArray().Select(reason => reason.GetString()))
                                        : "",
                                })
                                : (scroller, null));
                            continue;
                        }
                        if (Text(change, "property") is { } property && Properties.Contains(property) &&
                            Text(change, "elementId") is { } element)
                        {
                            values.Add((element, property, change.TryGetProperty("value", out var changed) ? ValueText(property, changed) : null));
                        }
                    }
                    if (values.Count > 0 || scrolls.Count > 0 || progress.Count > 0)
                    {
                        _frames.Add(new CompositorFrame(record.MonotonicNanoseconds, instance, process, frameSink, host, token, values, scrolls)
                        {
                            Progress = progress,
                        });
                    }
                }
                break;
            case (BrowserEvidenceChannels.Compositor, BrowserEvidenceEventTypes.PaintWorkletPainted):
                if (Text(payload, "elementId") is { } paintedElement &&
                    Text(payload, "property") is { } paintedProperty &&
                    ProgressProperties.ContainsValue(paintedProperty) &&
                    payload.TryGetProperty("progress", out var paintedProgress) &&
                    paintedProgress.ValueKind is JsonValueKind.Number or JsonValueKind.Null)
                {
                    _painted.Add(new Painted(
                        record.MonotonicNanoseconds, instance, process, paintedElement, paintedProperty,
                        paintedProgress.ValueKind == JsonValueKind.Number ? paintedProgress.GetDouble() : null,
                        payload.TryGetProperty("value", out var paintedValue) ? PaintedText(paintedProperty, paintedValue) : Malformed));
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
                    var offset = decimal.Round(
                        (ticks - native.Value) * 1_000_000_000m / processFrequency,
                        MidpointRounding.AwayFromZero);
                    _presented[(instance, process, presentedHost, presentedToken)] = record.MonotonicNanoseconds + (long)offset;
                }
                break;
        }
    }

    /// <param name="compositionNanoseconds">The recording time of the frame's composition.</param>
    public RecordedCompositorValues Choose(long compositionNanoseconds)
    {
        var presentation = _presentations.LastOrDefault(item => item.Time <= compositionNanoseconds);
        if (presentation.FrameSink is null)
        {
            return RecordedCompositorValues.None;
        }
        var (instance, process) = (presentation.Instance, presentation.Process);

        // The last compositor frame of the document's frame sink presented at
        // or before the composition, and the values of its compositor up to
        // it, each the latest change.
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
            return _frames.Any(frame => frame.Instance == instance && frame.Process == process)
                ? new RecordedCompositorValues(RecordedCompositorValues.None.ByNode,
                    ["No compositor frame of the page's frame sink with a transform, opacity, filter, backdrop filter, scroll offset, or paint worklet progress was presented at or before the frame, so every element is drawn from its recorded style and every scroller is at its main thread offset."])
                : RecordedCompositorValues.None;
        }
        var chosen = _frames[last];
        var values = new Dictionary<(string ElementId, string Property), (string? Text, string Token)>();
        var scrollPositions = new Dictionary<string, RecordedScrollPosition>(StringComparer.Ordinal);
        var progressOf = new Dictionary<(string ElementId, string Property), (ProgressChange Change, string Token)>();
        for (var index = 0; index <= last; index++)
        {
            var frame = _frames[index];
            if (frame.Instance != instance || frame.Process != process || frame.Host != chosen.Host)
            {
                continue;
            }
            foreach (var (element, property, text) in frame.Values)
            {
                values[(element, property)] = (text, frame.Token);
            }
            foreach (var change in frame.Progress)
            {
                progressOf[(change.ElementId, change.Property)] = (change, frame.Token);
            }
            foreach (var (element, position) in frame.Scrolls)
            {
                if (position is not null)
                {
                    scrollPositions[element] = position;
                }
                else
                {
                    scrollPositions.Remove(element);
                }
            }
        }

        var byNode = new Dictionary<long, List<RecordedCompositorValue>>();
        int unjoined = 0, ended = 0, absent = 0, unreadable = 0;
        foreach (var ((element, property), (text, token)) in values.OrderBy(item => item.Key.ElementId, StringComparer.Ordinal).ThenBy(item => item.Key.Property, StringComparer.Ordinal))
        {
            // The element's latest start in the document's renderer at or
            // before the composition names its node and namespace.
            var start = _started.LastOrDefault(item =>
                item.Instance == instance && item.Process == process && item.ElementId == element &&
                item.Time <= compositionNanoseconds);
            if (start is null)
            {
                unjoined++;
                continue;
            }
            // A value is dropped when its animation ended and a later
            // rendering update of the document was presented, which holds
            // the main thread's value after it.
            if (_ended.TryGetValue((instance, process, start.KeyframeModel), out var endedTime) &&
                endedTime >= start.Time && endedTime <= compositionNanoseconds &&
                _presentations.Any(item => item.Instance == instance && item.Process == process &&
                                           item.Time > endedTime && item.Time <= compositionNanoseconds))
            {
                ended++;
                continue;
            }
            if (text is null)
            {
                absent++;
                continue;
            }
            var key = property == "transform" ? start.Namespace : property;
            if (property == "transform" && !TransformNamespaces.Contains(key))
            {
                unreadable++;
                continue;
            }
            if (text == Malformed)
            {
                unreadable++;
                continue;
            }
            if (!byNode.TryGetValue(start.NodeId, out var list))
            {
                byNode[start.NodeId] = list = [];
            }
            if (list.Any(item => item.Key == key))
            {
                // Two element IDs of one node under one key, as when a node's
                // layout object was replaced: the later element ID's value is
                // kept, as it was started later.
                list.RemoveAll(item => item.Key == key);
            }
            // A filter list with no operations is the key alone.
            list.Add(new RecordedCompositorValue(key, text.Length == 0 ? key : $"{key} {text}", token));
        }
        foreach (var list in byNode.Values)
        {
            list.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
        }

        // Sub-step 2c: each element's progress, joined to its node as the
        // values are, takes the last painted record of the same renderer,
        // element, property, and progress written at or before the chosen
        // frame.
        var paintWorklet = new Dictionary<long, List<RecordedPaintWorkletValue>>();
        int workletUnjoined = 0, workletEnded = 0, workletAbsent = 0, unpainted = 0, workletUnreadable = 0;
        foreach (var ((element, property), (change, token)) in progressOf.OrderBy(item => item.Key.ElementId, StringComparer.Ordinal).ThenBy(item => item.Key.Property, StringComparer.Ordinal))
        {
            var start = _started.LastOrDefault(item =>
                item.Instance == instance && item.Process == process && item.ElementId == element &&
                item.Time <= compositionNanoseconds);
            if (start is null)
            {
                workletUnjoined++;
                continue;
            }
            if (_ended.TryGetValue((instance, process, start.KeyframeModel), out var endedTime) &&
                endedTime >= start.Time && endedTime <= compositionNanoseconds &&
                _presentations.Any(item => item.Instance == instance && item.Process == process &&
                                           item.Time > endedTime && item.Time <= compositionNanoseconds))
            {
                workletEnded++;
                continue;
            }
            if (!change.Drawn)
            {
                workletAbsent++;
                continue;
            }
            var paint = _painted.LastOrDefault(item =>
                item.Instance == instance && item.Process == process && item.ElementId == element &&
                item.Property == property && item.Progress == change.Progress && item.Time <= chosen.Time);
            if (paint is null)
            {
                unpainted++;
                continue;
            }
            if (paint.Text is null || paint.Text == Malformed)
            {
                workletUnreadable++;
                continue;
            }
            if (!paintWorklet.TryGetValue(start.NodeId, out var list))
            {
                paintWorklet[start.NodeId] = list = [];
            }
            // Two element IDs of one node for one property: the later
            // element ID's value is kept, as it was started later.
            list.RemoveAll(item => item.Property == property);
            list.Add(new RecordedPaintWorkletValue(property, paint.Text, change.ProgressText, token, paint.Time));
        }
        foreach (var list in paintWorklet.Values)
        {
            list.Sort((left, right) => string.CompareOrdinal(left.Property, right.Property));
        }

        static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
        var notes = new List<string>();
        if (byNode.Count > 0)
        {
            notes.Add($"{Count(byNode.Values.Sum(list => list.Count))} compositor values are imposed on {Count(byNode.Count)} elements, as of compositor frame {chosen.Token} of the page's frame sink, presented at {(presentedTime / 1e9).ToString("0.000", CultureInfo.InvariantCulture)} s: "
                + string.Join("; ", byNode.OrderBy(item => item.Key).Select(item =>
                    $"node {item.Key.ToString(CultureInfo.InvariantCulture)}: " + string.Join(", ", item.Value.Select(value => $"{value.Key}, last changed in compositor frame {value.FrameToken}"))))
                + ". They are written on each element in its data-a11y-recorded-compositor attribute, which is shown in the Elements pane but was not an attribute of the recorded page. Transforms, filters, and backdrop filters are imposed on Blink's paint properties, so DevTools' Computed pane shows the recorded style's values for them; the opacity is imposed through the style. An imposed filter whose operations are not those of the recorded style, or a transform the recorded style gives the element no node for, is not imposed, and DevTools' Console names the element.");
        }
        if (unjoined > 0)
        {
            notes.Add($"{Count(unjoined)} compositor values have no compositor-animation-started record of the page at or before the frame naming their element, as for an animation started before the recorder's client connected, so they are not imposed.");
        }
        if (ended > 0)
        {
            notes.Add($"{Count(ended)} compositor values belong to animations that had ended before a later presented rendering update, so the recorded style holds their element's value and they are not imposed.");
        }
        if (absent > 0)
        {
            notes.Add($"{Count(absent)} compositor values were no longer in the drawn tree at the frame, so they are not imposed.");
        }
        if (unreadable > 0)
        {
            notes.Add($"{Count(unreadable)} compositor values are of a namespace or form the recreation does not impose, so they are not imposed.");
        }
        if (paintWorklet.Count > 0)
        {
            notes.Add($"{Count(paintWorklet.Values.Sum(list => list.Count))} paint worklet values are chosen for {Count(paintWorklet.Count)} elements, each as painted at the progress the compositor last drew it at by compositor frame {chosen.Token}: "
                + string.Join("; ", paintWorklet.OrderBy(item => item.Key).Select(item =>
                    $"node {item.Key.ToString(CultureInfo.InvariantCulture)}: " + string.Join(", ", item.Value.Select(value =>
                        $"{value.Property} at progress {value.Progress ?? "none (the main thread's value)"}, last changed in compositor frame {value.FrameToken}, painted at {(value.PaintedNanoseconds / 1e9).ToString("0.000", CultureInfo.InvariantCulture)} s"))))
                + ". They are written on each element in its data-a11y-recorded-paint-worklet attribute, which is shown in the Elements pane but was not an attribute of the recorded page. The background color is imposed through the style, so DevTools' Computed pane shows it; the clip path is imposed on Blink's paint properties, so the Computed pane shows the recorded style's clip path.");
        }
        if (workletUnjoined > 0)
        {
            notes.Add($"{Count(workletUnjoined)} paint worklet progress values have no compositor-animation-started record of the page at or before the frame naming their element, so they are not imposed.");
        }
        if (workletEnded > 0)
        {
            notes.Add($"{Count(workletEnded)} paint worklet progress values belong to animations that had ended before a later presented rendering update, so the recorded style holds their element's value and they are not imposed.");
        }
        if (workletAbsent > 0)
        {
            notes.Add($"{Count(workletAbsent)} paint worklet progress values were no longer in the drawn tree at the frame, so they are not imposed.");
        }
        if (unpainted > 0)
        {
            notes.Add($"{Count(unpainted)} paint worklet progress values have no paint-worklet-painted record of the same element and progress at or before the frame, so their elements are drawn from the recorded style.");
        }
        if (workletUnreadable > 0)
        {
            notes.Add($"{Count(workletUnreadable)} paint worklet values are of a form the recreation does not impose, so they are not imposed.");
        }
        return new RecordedCompositorValues(
            byNode.ToDictionary(item => item.Key, item => (IReadOnlyList<RecordedCompositorValue>)item.Value),
            notes)
        {
            ScrollPositions = scrollPositions,
            PaintWorklet = paintWorklet.ToDictionary(item => item.Key, item => (IReadOnlyList<RecordedPaintWorkletValue>)item.Value),
        };
    }

    // What ValueText gives for a value not of its property's form.
    private const string Malformed = "\u0000";

    // A value's numbers as the recording wrote them, separated by single
    // spaces: a transform's 16 entries or an opacity; a filter's operations,
    // separated by ", ", each its type and numbers, or empty for no
    // operations. Null for a null value, and Malformed for a value not of its
    // property's form.
    private static string? ValueText(string property, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        switch (property)
        {
            case "transform":
                return value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 16 &&
                       value.EnumerateArray().All(entry => entry.ValueKind == JsonValueKind.Number)
                    ? string.Join(" ", value.EnumerateArray().Select(entry => entry.GetRawText()))
                    : Malformed;
            case "opacity":
                return value.ValueKind == JsonValueKind.Number ? value.GetRawText() : Malformed;
            default:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    return Malformed;
                }
                var operations = new List<string>();
                foreach (var operation in value.EnumerateArray())
                {
                    if (Text(operation, "type") is not { } type || type.Length == 0 ||
                        !type.All(character => character is >= 'a' and <= 'z' or '-') ||
                        !operation.TryGetProperty("numbers", out var numbers) ||
                        numbers.ValueKind != JsonValueKind.Array ||
                        !numbers.EnumerateArray().All(entry => entry.ValueKind == JsonValueKind.Number))
                    {
                        return Malformed;
                    }
                    var text = new StringBuilder(type);
                    foreach (var number in numbers.EnumerateArray())
                    {
                        text.Append(' ').Append(number.GetRawText());
                    }
                    operations.Add(text.ToString());
                }
                return string.Join(", ", operations);
        }
    }

    // Points each path verb takes, and whether it takes a conic weight.
    private static readonly Dictionary<string, (int Points, bool Weight)> Verbs = new(StringComparer.Ordinal)
    {
        ["move"] = (1, false),
        ["line"] = (1, false),
        ["quad"] = (2, false),
        ["conic"] = (2, true),
        ["cubic"] = (3, false),
        ["close"] = (0, false),
    };

    private static readonly HashSet<string> FillTypes = new(StringComparer.Ordinal)
    {
        "winding",
        "even-odd",
        "inverse-winding",
        "inverse-even-odd",
    };

    // A painted value's text, its numbers as the recording wrote them,
    // separated by single spaces: a background color's four floats, red,
    // green, blue, and alpha; or a clip path's fill type and then each verb
    // followed by its points' coordinates and, for a conic, its weight.
    // Malformed for a value not of its property's form.
    private static string PaintedText(string property, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return Malformed;
        }
        static bool Numbers(JsonElement array) =>
            array.ValueKind == JsonValueKind.Array && array.EnumerateArray().All(entry => entry.ValueKind == JsonValueKind.Number);
        if (property == "background-color")
        {
            return value.TryGetProperty("color", out var color) && Numbers(color) && color.GetArrayLength() == 4
                ? string.Join(" ", color.EnumerateArray().Select(entry => entry.GetRawText()))
                : Malformed;
        }
        if (Text(value, "fillType") is not { } fill || !FillTypes.Contains(fill) ||
            !value.TryGetProperty("verbs", out var verbs) || verbs.ValueKind != JsonValueKind.Array ||
            !value.TryGetProperty("points", out var points) || !Numbers(points) ||
            !value.TryGetProperty("conicWeights", out var weights) || !Numbers(weights))
        {
            return Malformed;
        }
        var coordinates = points.EnumerateArray().Select(entry => entry.GetRawText()).ToList();
        var conics = weights.EnumerateArray().Select(entry => entry.GetRawText()).ToList();
        int point = 0, weight = 0;
        var text = new StringBuilder(fill);
        foreach (var verb in verbs.EnumerateArray())
        {
            if (verb.ValueKind != JsonValueKind.String || verb.GetString() is not { } name ||
                !Verbs.TryGetValue(name, out var shape) ||
                point + 2 * shape.Points > coordinates.Count ||
                (shape.Weight && weight >= conics.Count))
            {
                return Malformed;
            }
            text.Append(' ').Append(name);
            for (var index = 0; index < 2 * shape.Points; index++)
            {
                text.Append(' ').Append(coordinates[point++]);
            }
            if (shape.Weight)
            {
                text.Append(' ').Append(conics[weight++]);
            }
        }
        return point == coordinates.Count && weight == conics.Count ? text.ToString() : Malformed;
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

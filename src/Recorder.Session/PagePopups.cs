using System.Globalization;
using System.Text.Json;

namespace Recorder.Session;

/// <summary>A rectangle of a page popup record: screen DIPs, or the owner's local root for its visible bounds.</summary>
public sealed record PopupRect(double X, double Y, double Width, double Height)
{
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"({X}, {Y}, {Width}, {Height})");
}

/// <summary>A page popup record or popup widget record, as the playback index keeps it.</summary>
public sealed record PopupRecord(long EventKey, long Time, string EventType, JsonElement Payload);

/// <summary>
/// A page popup open at a frame (slice 4d sub-step 2): the popup's opening
/// record, the browser's popup widget joined to it, and the window rectangle
/// the browser last set for it at or before the time its state is read at.
/// </summary>
/// <param name="DocumentKey">The popup document's state key: its token and document identity.</param>
/// <param name="ProcessId">The renderer process of the popup and its owner.</param>
/// <param name="Kind">The popup's kind, from its owner element.</param>
/// <param name="OwnerDocumentToken">The owner document's token.</param>
/// <param name="OwnerNodeId">The owner element's node.</param>
/// <param name="OpenedTime">The time of the popup's opening record.</param>
/// <param name="FrameSinkId">The joined popup widget's frame sink, or null when no widget record joins.</param>
/// <param name="Window">The window rectangle in screen DIPs.</param>
/// <param name="WindowSource">Which record gave the window rectangle.</param>
/// <param name="WindowTime">The time of that record.</param>
public sealed record PagePopupAtFrame(
    string DocumentKey,
    long? ProcessId,
    string Kind,
    string OwnerDocumentToken,
    long OwnerNodeId,
    long OpenedTime,
    PopupRect OwnerVisibleBoundsInLocalRoot,
    PopupRect OwnerLocalRootRectInScreen,
    PopupRect AnchorRectInScreen,
    double ZoomFactor,
    string? FrameSinkId,
    PopupRect Window,
    string WindowSource,
    long WindowTime)
{
    /// <summary>
    /// True when the owner's visible bounds plus its local root's origin are
    /// the anchor rectangle, as the popup was opened with (a check reported,
    /// not assumed).
    /// </summary>
    public bool AnchorMatchesOwner =>
        OwnerVisibleBoundsInLocalRoot.X + OwnerLocalRootRectInScreen.X == AnchorRectInScreen.X &&
        OwnerVisibleBoundsInLocalRoot.Y + OwnerLocalRootRectInScreen.Y == AnchorRectInScreen.Y;

    /// <summary>
    /// Where the popup's window lies in the owner document's coordinates:
    /// the window rectangle less the owner's local root origin in screen,
    /// plus the root scroll offset at the frame. Its size is the window's.
    /// </summary>
    public PopupRect InDocument(double scrollX, double scrollY) => new(
        Window.X - OwnerLocalRootRectInScreen.X + scrollX,
        Window.Y - OwnerLocalRootRectInScreen.Y + scrollY,
        Window.Width,
        Window.Height);
}

/// <summary>
/// Finds the page popups open at a frame from the page popup and popup
/// widget records, and joins each to the browser's popup widget.
/// </summary>
public static class PagePopups
{
    /// <summary>
    /// The popups opened at or before <paramref name="compositionTime"/> and
    /// not closed by then, whose owner document has the given token. Each
    /// is joined to the opener frame's last popup-widget-created record at
    /// or before its opening, on renderer process and owner frame token,
    /// that no earlier popup joined.
    /// </summary>
    public static IReadOnlyList<PagePopupAtFrame> OpenAt(
        IEnumerable<PopupRecord> records,
        string? ownerDocumentToken,
        long compositionTime)
    {
        var ordered = records.OrderBy(record => record.Time).ThenBy(record => record.EventKey).ToArray();
        var created = ordered.Where(record => record.EventType == "popup-widget-created").ToList();
        var joined = new HashSet<long>();
        var result = new List<PagePopupAtFrame>();
        foreach (var opened in ordered.Where(record => record.EventType == "page-popup-opened"))
        {
            var payload = opened.Payload;
            var key = DomTreeRebuilder.DocumentKey(payload);
            var process = Context(payload, "processId") is { ValueKind: JsonValueKind.Number } value ? value.GetInt64() : (long?)null;
            var frameToken = Text(payload, "ownerFrameToken");
            var widget = created
                .Where(record => record.Time <= opened.Time && !joined.Contains(record.EventKey) &&
                    frameToken is not null && Text(record.Payload, "openerFrameToken") == frameToken &&
                    Number(record.Payload, "rendererProcessId") == process)
                .LastOrDefault();
            if (widget is not null)
            {
                joined.Add(widget.EventKey);
            }
            if (key is null || opened.Time > compositionTime || Text(payload, "ownerDocumentToken") != ownerDocumentToken)
            {
                continue;
            }
            var closed = ordered.Any(record => record.EventType == "page-popup-closed" &&
                record.Time >= opened.Time && record.Time <= compositionTime &&
                DomTreeRebuilder.DocumentKey(record.Payload) == key);
            if (closed)
            {
                continue;
            }
            var sink = widget is null ? null : Text(widget.Payload, "frameSinkId");
            result.Add(new PagePopupAtFrame(
                key,
                process,
                Text(payload, "kind") ?? "other",
                Text(payload, "ownerDocumentToken")!,
                Number(payload, "ownerNodeId") ?? -1,
                opened.Time,
                Rect(payload, "ownerVisibleBoundsInLocalRoot")!,
                Rect(payload, "ownerLocalRootRectInScreen")!,
                Rect(payload, "anchorRectInScreen")!,
                payload.TryGetProperty("zoomFactor", out var zoom) && zoom.ValueKind == JsonValueKind.Number ? zoom.GetDouble() : 1,
                sink,
                Rect(payload, "initialWindowRect")!,
                "page-popup-opened initialWindowRect",
                opened.Time));
        }
        return result;
    }

    /// <summary>
    /// The popup with its window rectangle as last set at or before
    /// <paramref name="stateTime"/>: the browser's rectangle for the
    /// joined widget, from popup-widget-shown viewBounds,
    /// popup-widget-bounds-requested setRect, or popup-widget-screen-rects
    /// viewRect, whichever is latest. Without a joined widget, or before
    /// any such record, the renderer's last page-popup-window-rect, and
    /// before that the rectangle it was opened with.
    /// </summary>
    public static PagePopupAtFrame WithWindowAt(PagePopupAtFrame popup, IEnumerable<PopupRecord> records, long stateTime)
    {
        PopupRect? window = null;
        string? source = null;
        long time = 0;
        foreach (var record in records.Where(record => record.Time >= popup.OpenedTime && record.Time <= stateTime)
            .OrderBy(record => record.Time).ThenBy(record => record.EventKey))
        {
            var payload = record.Payload;
            if (popup.FrameSinkId is { } sink && Text(payload, "frameSinkId") == sink)
            {
                var (rect, name) = record.EventType switch
                {
                    "popup-widget-shown" => (Rect(payload, "viewBounds"), "viewBounds"),
                    "popup-widget-bounds-requested" => (Rect(payload, "setRect"), "setRect"),
                    "popup-widget-screen-rects" => (Rect(payload, "viewRect"), "viewRect"),
                    _ => (null, null),
                };
                if (rect is not null)
                {
                    (window, source, time) = (rect, $"{record.EventType} {name}", record.Time);
                }
            }
            else if (popup.FrameSinkId is null && record.EventType == "page-popup-window-rect" &&
                DomTreeRebuilder.DocumentKey(payload) == popup.DocumentKey &&
                Rect(payload, "windowRect") is { } requested)
            {
                (window, source, time) = (requested, "page-popup-window-rect windowRect", record.Time);
            }
        }
        return window is null ? popup : popup with { Window = window, WindowSource = source!, WindowTime = time };
    }

    /// <summary>The page popup and popup widget records of a playback index.</summary>
    public static IReadOnlyList<PopupRecord> Records(PlaybackIndex index) =>
        [.. index.Events
            .Where(item => PlaybackIndexBuilder.IsPopupRecord(item.Channel, item.EventType) &&
                item.Payload.ValueKind == JsonValueKind.Object)
            .Select(item => new PopupRecord(item.EventKey, item.MonotonicNanoseconds, item.EventType, item.Payload))];

    private static PopupRect? Rect(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var rect) && rect.ValueKind == JsonValueKind.Object
            ? new PopupRect(
                rect.GetProperty("x").GetDouble(),
                rect.GetProperty("y").GetDouble(),
                rect.GetProperty("width").GetDouble(),
                rect.GetProperty("height").GetDouble())
            : null;

    private static JsonElement? Context(JsonElement payload, string name) =>
        payload.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.Object &&
            context.TryGetProperty(name, out var value)
                ? value
                : null;

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

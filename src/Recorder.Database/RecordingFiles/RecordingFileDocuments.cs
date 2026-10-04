using System.Text.Json;
using Recorder.Session;

namespace Recorder.Database.RecordingFiles;

/// <summary>
/// A top-level page document the auditor can choose to recreate at a frame.
/// </summary>
/// <param name="Key">The document's state key.</param>
/// <param name="Url">The URL of the navigation that committed the document.</param>
/// <param name="BrowserInterface">True for a browser interface document, such as a chrome:// page.</param>
/// <param name="Basis">How the document's state is matched to the frame.</param>
public sealed record RecordedDocumentChoice(string Key, string Url, bool BrowserInterface, BrowserStateBasis Basis);

/// <summary>
/// The browser documents of a recording file, for recreating a page at a
/// frame. The documents offered are those committed by a navigation of a
/// primary main frame at or before the frame, which the playback index
/// keeps whole; their state is read as <see cref="RecordingFileBrowserState"/>
/// reads it. The state reader is made on first use, and one call is made at
/// a time.
/// </summary>
public sealed class RecordingFileDocuments
{
    private readonly RecordingFileReader _reader;
    private readonly PlaybackIndex _index;
    private readonly List<(long Time, string Token, string Url)> _navigations = [];
    private readonly string? _filePath;
    private RecordingFileBrowserState? _state;
    private IReadOnlyList<PopupRecord>? _popupRecords;

    /// <param name="reader">The open file. It is not disposed.</param>
    /// <param name="filePath">The file's path, from which a recreation's fonts and images are read with a reader of their own.</param>
    public RecordingFileDocuments(RecordingFileReader reader, PlaybackIndex index, string? filePath = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _filePath = filePath;
        foreach (var item in index.Events)
        {
            if (item.EventType != "navigation-completed" || item.Payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var payload = item.Payload;
            if (Text(payload, "frameType") != "primary-main-frame" ||
                Text(payload, "url") is not { } url ||
                !payload.TryGetProperty("context", out var context) ||
                context.ValueKind != JsonValueKind.Object ||
                Text(context, "documentToken") is not { } token)
            {
                continue;
            }
            _navigations.Add((item.MonotonicNanoseconds, token, url));
        }
        _navigations.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    /// <summary>The state reader, made when first asked for.</summary>
    public RecordingFileBrowserState State => _state ??= new RecordingFileBrowserState(_reader, _index);

    /// <summary>
    /// The top-level documents at the frame, most recently presented first,
    /// and the browser interface documents last.
    /// </summary>
    public IReadOnlyList<RecordedDocumentChoice> At(long frameNanoseconds, CancellationToken cancellationToken = default)
    {
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (time, token, url) in _navigations)
        {
            if (time <= frameNanoseconds)
            {
                urls[token] = url;
            }
        }
        if (urls.Count == 0)
        {
            return [];
        }
        var at = State.AtFrame(frameNanoseconds, new HashSet<string>(), cancellationToken: cancellationToken);
        return at.Documents
            .Select(document => (document, token: document.Key.Split(' ', 2)[0]))
            .Where(item => urls.ContainsKey(item.token))
            .Select(item =>
            {
                var url = urls[item.token];
                return new RecordedDocumentChoice(item.document.Key, url, IsBrowserInterface(url), item.document.Basis);
            })
            .OrderBy(choice => choice.BrowserInterface)
            .ThenByDescending(choice => choice.Basis.PresentedTime ?? long.MinValue)
            .ThenByDescending(choice => choice.Basis.CutTime)
            .ThenBy(choice => choice.Key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The recorded state of one document at the frame, and how it was matched.</summary>
    public BrowserDocumentAt? Document(string key, long frameNanoseconds, CancellationToken cancellationToken = default) =>
        State.AtFrame(frameNanoseconds, new HashSet<string>(StringComparer.Ordinal) { key }, cancellationToken: cancellationToken)
            .Documents
            .FirstOrDefault(document => document.Key == key && document.State is not null);

    /// <summary>
    /// The page popups of a page on the screen at the frame (slice 4d
    /// sub-step 2, and "Popup on screen"): those its document owns, opened
    /// at or before the frame's composition, with a presented rendering
    /// update at or before it, and whose window was not hidden by then (or,
    /// in a recording without the browser's hidden record, not closed by
    /// then), each with its document's state at the frame and its window
    /// rectangle as last set at or before the time that state is read at.
    /// </summary>
    public IReadOnlyList<(PagePopupAtFrame Popup, BrowserDocumentAt? State)> Popups(
        string pageKey,
        long frameNanoseconds,
        CancellationToken cancellationToken = default)
    {
        var records = _popupRecords ??= PagePopups.Records(_index);
        if (records.Count == 0)
        {
            return [];
        }
        var open = PagePopups.OpenAt(records, pageKey.Split(' ', 2)[0], CompositionTime(frameNanoseconds));
        // A popup is on the screen from its first presented rendering
        // update: one with none at or before the composition is not yet
        // drawn, though it is open.
        return [.. open
            .Select(popup => (popup, state: Document(popup.DocumentKey, frameNanoseconds, cancellationToken)))
            .Where(item => IsDrawn(item.state))
            .Select(item => (PagePopups.WithWindowAt(item.popup, records, item.state!.Basis.CutTime), item.state))];
    }

    /// <summary>
    /// True when a popup document's state at a frame is the one after a
    /// presented rendering update, so that the popup was drawn at or before
    /// the frame's composition.
    /// </summary>
    public static bool IsDrawn(BrowserDocumentAt? state) =>
        state?.State is not null && state.Basis is { Basis: "presented", PresentedTime: not null };

    /// <summary>
    /// The fonts and images of a document at the recording time its state
    /// is read at, read with a reader of their own on the file, which they
    /// hold until disposed, so that a recreation can read their bytes after
    /// the recording is closed. None when the file's path is not known.
    /// </summary>
    /// <param name="compositionNanoseconds">The frame's composition time, at which each animated image's frame is chosen, or null for none.</param>
    public RecordedPageResources Resources(
        string key,
        long cutNanoseconds,
        CancellationToken cancellationToken = default,
        long? compositionNanoseconds = null) =>
        _filePath is null
            ? RecordedPageResources.None
            : RecordingFileResources.Read(_filePath, key, cutNanoseconds, cancellationToken, compositionNanoseconds);

    /// <summary>
    /// The recording time of the frame's composition, which the state is
    /// matched to, or the frame time when the index has none.
    /// </summary>
    public long CompositionTime(long frameNanoseconds) =>
        _index.FrameCompositions
            .Where(item => item.FrameNanoseconds == frameNanoseconds)
            .Select(item => (long?)item.CompositedNanoseconds)
            .FirstOrDefault() ?? frameNanoseconds;

    public static bool IsBrowserInterface(string url) =>
        url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("chrome-untrusted://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

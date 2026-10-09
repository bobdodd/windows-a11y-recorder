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
/// A frame of a recorded document at a frame of the recording (slice 5b):
/// the owner element that held it, the document chosen for it and why, that
/// document's address and state at the frame, whether it was recorded in its
/// parent's renderer process, and its own frames. A frame beyond the limits
/// has no document and says why it was left out.
/// </summary>
/// <param name="Owner">The owner element, in its parent document's state.</param>
/// <param name="Choice">The chosen document, or null when none was recorded by the frame's composition.</param>
/// <param name="Url">The address of the navigation that committed the chosen document, or null when none did.</param>
/// <param name="State">The chosen document's state at the frame.</param>
/// <param name="SameProcessAsParent">True when the chosen document was recorded in its parent's renderer process; null when either is not known.</param>
/// <param name="Omitted">Why the frame was left out, when it was.</param>
public sealed record RecordedFrameAt(
    FrameOwnerState Owner,
    FrameDocumentChoice? Choice,
    string? Url,
    BrowserDocumentAt? State,
    bool? SameProcessAsParent,
    IReadOnlyList<RecordedFrameAt> Children,
    string? Omitted);

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
    // Accessibility preferences stage 3: the page of each document committed
    // in a primary main frame, by its document token, as the frame tree node
    // id its navigation's context names it by ("frame-N").
    private readonly Dictionary<string, int> _pages;
    // Slice 5b: every navigation-completed record's time and address, by the
    // document token of the document it committed, in any frame.
    private readonly Dictionary<string, List<long>> _commits = new(StringComparer.Ordinal);
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
            if (Text(payload, "url") is { } committedUrl &&
                payload.TryGetProperty("context", out var committedContext) &&
                committedContext.ValueKind == JsonValueKind.Object &&
                Text(committedContext, "documentToken") is { } committedToken)
            {
                if (!_commits.TryGetValue(committedToken, out var times))
                {
                    _commits[committedToken] = times = [];
                }
                times.Add(item.MonotonicNanoseconds);
            }
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
        _pages = PagesByDocumentToken(index.Events);
    }

    // The page of each document a page commit committed, as the properties
    // panel's "Sent to the page" group finds a page's commits.
    internal static Dictionary<string, int> PagesByDocumentToken(IEnumerable<PlaybackIndexEvent> events)
    {
        var pages = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in events.OrderBy(item => item.MonotonicNanoseconds))
        {
            if (item.EventType == "navigation-completed" &&
                SessionPlaybackArchiveBuilder.PageCommitOf(item.MonotonicNanoseconds, item.Payload) is { } commit &&
                item.Payload.GetProperty("context") is var context &&
                Text(context, "documentToken") is { } token)
            {
                pages[token] = commit.PageFrameTreeNodeId;
            }
        }

        return pages;
    }

    /// <summary>
    /// The page of a top-level document, as the frame tree node id its
    /// primary main frame navigation names it by, which the
    /// <c>browser.preferences</c> records name the page by; null when its
    /// navigation named none. See docs/architecture/accessibility-preferences.md,
    /// "Stage 3".
    /// </summary>
    public int? PageFrameTreeNodeIdOf(string documentKey)
    {
        ArgumentNullException.ThrowIfNull(documentKey);
        return _pages.TryGetValue(documentKey.Split(' ', 2)[0], out var page) ? page : null;
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

    /// <summary>The most frames a recreation is built with (slice 5b).</summary>
    public const int MostFrames = 64;

    /// <summary>The deepest frame a recreation is built with, the top document being at depth 0 (slice 5b).</summary>
    public const int DeepestFrame = 8;

    /// <summary>
    /// The frames of a document at the frame (slice 5b), each with the
    /// document it showed at the frame's composition, chosen as
    /// <see cref="BrowserFrames.Choose"/> says, and that document's own
    /// frames, to a depth of <see cref="DeepestFrame"/> and at most
    /// <see cref="MostFrames"/> frames in all, in document order of their
    /// owners at each level, breadth first. None for a recording before
    /// protocol 0.55, or one whose index does not hold the frame documents.
    /// </summary>
    public IReadOnlyList<RecordedFrameAt> Frames(
        string key,
        BrowserDocumentState state,
        long frameNanoseconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(state);
        if (_index.FrameDocuments.Count == 0 || state.Frames.Owners.Count == 0)
        {
            return [];
        }
        var composition = CompositionTime(frameNanoseconds);
        var commits = _commits.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<long>)item.Value,
            StringComparer.Ordinal);
        var processes = _index.FrameDocuments.ToDictionary(item => item.DocumentKey, item => item.ProcessId, StringComparer.Ordinal);
        var built = 0;
        // Breadth first, so that the limit leaves out the deepest frames.
        var level = new List<(string Key, BrowserDocumentState State, List<RecordedFrameAt> Into)>();
        var top = new List<RecordedFrameAt>();
        level.Add((key, state, top));
        for (var depth = 1; level.Count > 0; depth++)
        {
            var next = new List<(string Key, BrowserDocumentState State, List<RecordedFrameAt> Into)>();
            foreach (var (parentKey, parent, into) in level)
            {
                foreach (var owner in OwnersInOrder(parent))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (depth > DeepestFrame || built >= MostFrames)
                    {
                        into.Add(new RecordedFrameAt(owner, null, null, null, null, [],
                            depth > DeepestFrame
                                ? $"it is deeper than {DeepestFrame} frames"
                                : $"the recreation is built with at most {MostFrames} frames"));
                        continue;
                    }
                    built++;
                    var choice = BrowserFrames.Choose(owner.FrameToken, composition, _index.FrameDocuments, commits);
                    if (choice is null)
                    {
                        into.Add(new RecordedFrameAt(owner, null, null, null, null, [], null));
                        continue;
                    }
                    var childKey = choice.Document.DocumentKey;
                    var found = Document(childKey, frameNanoseconds, cancellationToken);
                    var children = new List<RecordedFrameAt>();
                    bool? sameProcess =
                        processes.GetValueOrDefault(parentKey) is { } parentProcess && choice.Document.ProcessId is { } childProcess
                            ? parentProcess == childProcess
                            : null;
                    into.Add(new RecordedFrameAt(owner, choice, CommittedUrl(childKey, composition), found, sameProcess, children, null));
                    if (found?.State is { } childState)
                    {
                        next.Add((childKey, childState, children));
                    }
                }
            }
            level = next;
        }
        return top;
    }

    // The owners of a document in the document order of the owner elements
    // in its recorded tree, then those not in the tree, by node ID.
    private static IEnumerable<FrameOwnerState> OwnersInOrder(BrowserDocumentState state)
    {
        var order = new Dictionary<long, int>();
        if (state.Dom is { } tree)
        {
            var root = tree.Nodes.Values.FirstOrDefault(node => node.NodeType == "document" && node.ParentId is null);
            var stack = new Stack<long>();
            if (root is not null)
            {
                stack.Push(root.Id);
            }
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!tree.Nodes.TryGetValue(id, out var node))
                {
                    continue;
                }
                order[id] = order.Count;
                for (var index = node.Children.Count - 1; index >= 0; index--)
                {
                    stack.Push(node.Children[index]);
                }
                if (node.ShadowRootId is { } shadow)
                {
                    stack.Push(shadow);
                }
            }
        }
        return state.Frames.Owners.Values
            .OrderBy(owner => order.TryGetValue(owner.OwnerNodeId, out var position) ? position : int.MaxValue)
            .ThenBy(owner => owner.OwnerNodeId);
    }

    // The address of the latest navigation that committed a document at or
    // before a time, or of its first if none was by then.
    private string? CommittedUrl(string documentKey, long time)
    {
        var token = documentKey.Split(' ', 2)[0];
        var commits = new List<(long Time, string Url)>();
        foreach (var item in _index.Events)
        {
            if (item.EventType == "navigation-completed" && item.Payload.ValueKind == JsonValueKind.Object &&
                item.Payload.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.Object &&
                Text(context, "documentToken") == token && Text(item.Payload, "url") is { } address)
            {
                commits.Add((item.MonotonicNanoseconds, address));
            }
        }
        if (commits.Count == 0)
        {
            return null;
        }
        var byThen = commits.Where(item => item.Time <= time).ToList();
        return byThen.Count > 0 ? byThen.MaxBy(item => item.Time).Url : commits.MinBy(item => item.Time).Url;
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

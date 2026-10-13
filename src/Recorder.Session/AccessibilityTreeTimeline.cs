using System.Globalization;

namespace Recorder.Session;

/// <summary>A document committed in a tab: when, its token, its page, and whether it is the page's main frame.</summary>
public sealed record PageDocument(long Time, string DocumentToken, string PageId, bool MainFrame);

/// <summary>What a row of the tree view is.</summary>
public enum AccessibilityTreeRowKind
{
    /// <summary>An object, as a checkpoint last serialized it.</summary>
    Object,

    /// <summary>A child an object lists that no checkpoint serialized by the time.</summary>
    NotRecorded,

    /// <summary>A note under a frame object: its document's tree is not recorded, or not yet.</summary>
    FrameNote,

    /// <summary>A heading for a frame's document tree, when which frame object is its own is not recorded.</summary>
    FrameTree,

    /// <summary>A heading for objects no recorded version lists as a child.</summary>
    Unplaced
}

/// <summary>
/// A row of the tree view: its key, kind, object, label, when its version
/// was recorded and its record, and its children.
/// </summary>
public sealed record AccessibilityTreeRow(
    string Key,
    AccessibilityTreeRowKind Kind,
    string? DocumentToken,
    long? NodeId,
    string Label,
    long? RecordedAt,
    string? EventId,
    IReadOnlyList<AccessibilityTreeRow> Children);

/// <summary>The page's tree at a time: its rows, the number of objects, and what it rests on.</summary>
public sealed class AccessibilityTreeSnapshot
{
    internal AccessibilityTreeSnapshot(long version, string pageToken, int objectCount, IReadOnlyList<AccessibilityTreeRow> roots)
    {
        Version = version;
        PageToken = pageToken;
        ObjectCount = objectCount;
        Roots = roots;
    }

    /// <summary>Changes when the tree does, so a view rebuilt only when it changes.</summary>
    public long Version { get; }

    public string PageToken { get; }

    public int ObjectCount { get; }

    public IReadOnlyList<AccessibilityTreeRow> Roots { get; }

    /// <summary>The keys of the rows from a root down to the row with a key, inclusive; null when it is not in the tree.</summary>
    public IReadOnlyList<string>? PathTo(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var path = new List<string>();
        foreach (var root in Roots)
        {
            if (Find(root, key, path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool Find(AccessibilityTreeRow row, string key, List<string> path)
    {
        path.Add(row.Key);
        if (row.Key == key)
        {
            return true;
        }

        foreach (var child in row.Children)
        {
            if (Find(child, key, path))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }
}

/// <summary>
/// The page's accessibility tree at a time, rebuilt from the accessibility
/// checkpoints (screen-reader-activity.md, "4c, the tree view"): each
/// object as Chromium last serialized it at or before the time, in its
/// children's order, with the page's frame's tree nested under the frame
/// object when the page has one frame. The checkpoints do not record
/// removals, so each object's latest version is the best known.
/// </summary>
public sealed class AccessibilityTreeTimeline
{
    public const string Note =
        "Each object as Chromium last sent it at or before the playhead. Removals are not recorded, so an object may be shown after it was gone.";

    // Deeper than this, a tree is not followed, against a cycle.
    private const int MaximumDepth = 200;

    private readonly AccessibilityRequests _requests;
    private readonly List<PageDocument> _documents;
    private readonly List<long> _changeTimes;

    public AccessibilityTreeTimeline(AccessibilityRequests requests, IEnumerable<PageDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(documents);
        _requests = requests;
        _documents = [.. documents.OrderBy(document => document.Time)];
        _changeTimes = [.. requests.Trees.Values
            .SelectMany(tree => tree.Values)
            .SelectMany(versions => versions)
            .Select(version => version.Time)
            .Concat(_documents.Select(document => document.Time))
            .Order()];
    }

    public static AccessibilityTreeTimeline Empty { get; } = new(AccessibilityRequests.Empty, []);

    // An object of a document as Chromium last serialized it at or before a
    // time; null where none was by then.
    internal AccessibilityRequests.TreeNode? NodeAt(string documentToken, long nodeId, long time)
    {
        if (!_requests.Trees.TryGetValue(documentToken, out var tree) || !tree.TryGetValue(nodeId, out var versions))
        {
            return null;
        }

        AccessibilityRequests.TreeNode? found = null;
        foreach (var candidate in versions)
        {
            if (candidate.Time > time)
            {
                break;
            }

            found = candidate;
        }

        return found;
    }

    /// <summary>Whether the recording holds a page's tree.</summary>
    public bool Recorded => _requests.Trees.Count > 0 && _documents.Any(document => document.MainFrame);

    /// <summary>A row's key for an object of a document.</summary>
    public static string KeyOf(string documentToken, long nodeId) =>
        string.Create(CultureInfo.InvariantCulture, $"{documentToken}:{nodeId}");

    /// <summary>The key of an object of a tree, by the records that name its document; null when none does.</summary>
    public string? KeyOf(TreeObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _requests.DocumentOfTree(item.TreeId) is { } token ? KeyOf(token, item.NodeId) : null;
    }

    /// <summary>The page's tree at a time; null before a page is loaded, or for a page whose tree is not recorded.</summary>
    public AccessibilityTreeSnapshot? At(long time)
    {
        PageDocument? main = null;
        foreach (var document in _documents)
        {
            if (document.Time > time)
            {
                break;
            }

            if (document.MainFrame)
            {
                main = document;
            }
        }

        if (main is null)
        {
            return null;
        }

        var frames = _documents
            .Where(document => !document.MainFrame && document.PageId == main.PageId && document.Time >= main.Time && document.Time <= time)
            .ToList();
        var version = _changeTimes.BinarySearch(time);
        version = version < 0 ? ~version : version + 1;
        while (version < _changeTimes.Count && _changeTimes[version] <= time)
        {
            version++;
        }

        var count = 0;
        var frameObjects = new List<AccessibilityTreeRow>();
        var roots = Build(main.DocumentToken, time, ref count, frameObjects);
        if (roots.Count == 0)
        {
            return null;
        }

        var frameTrees = new List<(PageDocument Document, List<AccessibilityTreeRow> Roots)>();
        foreach (var frame in frames)
        {
            var ignored = new List<AccessibilityTreeRow>();
            frameTrees.Add((frame, Build(frame.DocumentToken, time, ref count, ignored)));
        }

        // One frame object and one frame document: the frame's tree goes
        // under the frame object. Otherwise the frame trees follow the
        // page's, as which is whose is not recorded.
        if (frameObjects.Count == 1 && frameTrees.Count <= 1)
        {
            var host = frameObjects[0];
            var note = frameTrees.Count == 0
                ? Row(host.Key + "/frame", AccessibilityTreeRowKind.FrameNote, "its document's tree is not in the recording")
                : frameTrees[0].Roots.Count == 0
                    ? Row(host.Key + "/frame", AccessibilityTreeRowKind.FrameNote, "its document's tree is not yet in the recording")
                    : null;
            var nested = note is null ? frameTrees[0].Roots : [note];
            roots = [.. roots.Select(root => Replace(root, host.Key, row => row with { Children = [.. row.Children, .. nested] }))];
        }
        else
        {
            foreach (var host in frameObjects)
            {
                var note = Row(host.Key + "/frame", AccessibilityTreeRowKind.FrameNote,
                    "which of the frame trees after the page's is its own is not recorded");
                roots = [.. roots.Select(root => Replace(root, host.Key, row => row with { Children = [.. row.Children, note] }))];
            }

            foreach (var (document, frameRoots) in frameTrees)
            {
                roots.Add(new AccessibilityTreeRow(
                    "frame:" + document.DocumentToken,
                    AccessibilityTreeRowKind.FrameTree,
                    document.DocumentToken,
                    null,
                    frameRoots.Count == 0
                        ? "A frame's document, whose tree is not yet in the recording"
                        : "A frame's document tree",
                    document.Time,
                    null,
                    frameRoots));
            }
        }

        return new AccessibilityTreeSnapshot(version, main.DocumentToken, count, roots);
    }

    // A document's rows at a time: its root, then, under a heading, the
    // objects no recorded version lists as a child, which may have been
    // removed. The frame objects found are added to frameObjects.
    private List<AccessibilityTreeRow> Build(string token, long time, ref int count, List<AccessibilityTreeRow> frameObjects)
    {
        if (!_requests.Trees.TryGetValue(token, out var tree))
        {
            return [];
        }

        var present = new Dictionary<long, AccessibilityRequests.TreeNode>();
        foreach (var (id, versions) in tree)
        {
            AccessibilityRequests.TreeNode? found = null;
            foreach (var candidate in versions)
            {
                if (candidate.Time > time)
                {
                    break;
                }

                found = candidate;
            }

            if (found is not null)
            {
                present[id] = found;
            }
        }

        if (present.Count == 0)
        {
            return [];
        }

        count += present.Count;
        var listed = present.Values.SelectMany(node => node.Children).ToHashSet();
        var tops = present.Keys.Where(id => !listed.Contains(id)).Order().ToList();
        var root = tops.FirstOrDefault(id => present[id].Role == "rootWebArea", tops.Count > 0 ? tops[0] : present.Keys.Min());
        var visited = new HashSet<long>();
        var frames = new List<AccessibilityTreeRow>();
        var rows = new List<AccessibilityTreeRow> { RowOf(token, root, present, time, visited, frames, 0) };
        var unplaced = tops.Where(id => id != root).ToList();
        if (unplaced.Count > 0)
        {
            rows.Add(new AccessibilityTreeRow(
                "unplaced:" + token,
                AccessibilityTreeRowKind.Unplaced,
                token,
                null,
                $"{unplaced.Count.ToString(CultureInfo.InvariantCulture)} objects no recorded version lists as a child; they may have been removed",
                null,
                null,
                [.. unplaced.Select(id => RowOf(token, id, present, time, visited, frames, 1))]));
        }

        frameObjects.AddRange(frames);
        return rows;
    }

    private AccessibilityTreeRow RowOf(
        string token,
        long id,
        Dictionary<long, AccessibilityRequests.TreeNode> present,
        long time,
        HashSet<long> visited,
        List<AccessibilityTreeRow> frames,
        int depth)
    {
        var key = KeyOf(token, id);
        if (!present.TryGetValue(id, out var node))
        {
            return Row(key, AccessibilityTreeRowKind.NotRecorded,
                $"node {id.ToString(CultureInfo.InvariantCulture)}, listed as a child, not in the recording by the playhead") with
            {
                DocumentToken = token,
                NodeId = id
            };
        }

        var children = new List<AccessibilityTreeRow>();
        if (visited.Add(id) && depth < MaximumDepth)
        {
            foreach (var child in node.Children)
            {
                if (!visited.Contains(child))
                {
                    children.Add(RowOf(token, child, present, time, visited, frames, depth + 1));
                }
            }
        }

        var row = new AccessibilityTreeRow(key, AccessibilityTreeRowKind.Object, token, id, LabelOf(token, id, node, time), node.Time, node.EventId, children);
        if (node.HasChildTree)
        {
            frames.Add(row);
        }

        return row;
    }

    // An object's role and name; with no name, the start of the text it
    // holds; then its node.
    private string LabelOf(string token, long id, AccessibilityRequests.TreeNode node, long time)
    {
        var node_ = id.ToString(CultureInfo.InvariantCulture);
        if (node.Name.Trim().Length > 0)
        {
            return $"{node.Role} \"{node.Name.Trim()}\" (node {node_})";
        }

        if (node.Role != "staticText" && _requests.TextOf(token, id, time) is { Length: > 0 } text)
        {
            return $"{node.Role} holding \"{(text.Length > 60 ? text[..60].TrimEnd() + "…" : text)}\" (node {node_})";
        }

        return $"{node.Role} with no name (node {node_})";
    }

    private static AccessibilityTreeRow Row(string key, AccessibilityTreeRowKind kind, string label) =>
        new(key, kind, null, null, label, null, null, []);

    private static AccessibilityTreeRow Replace(AccessibilityTreeRow row, string key, Func<AccessibilityTreeRow, AccessibilityTreeRow> change)
    {
        if (row.Key == key)
        {
            return change(row);
        }

        var children = row.Children.Select(child => Replace(child, key, change)).ToList();
        return children.SequenceEqual(row.Children) ? row : row with { Children = children };
    }
}

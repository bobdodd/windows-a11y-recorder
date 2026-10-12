using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>What an accessibility request record shows: one line, and the records it is joined with.</summary>
public sealed record AccessibilityRequestOutcome(string Text, IReadOnlyList<string> Details);

/// <summary>
/// An action sent to a page: its record, time, action, and target, with the
/// key pressed or the mouse button released in the KeyWindowNanoseconds
/// before it, when there is one, and the command of a kept key.
/// </summary>
public sealed record RequestedAction(
    string EventId,
    long Time,
    string Action,
    string? TreeId,
    long? NodeId,
    string Role,
    string Name,
    string? LinkEventId,
    long? LinkTime,
    string? Click,
    KeptCommand? Command);

/// <summary>An object of a page's accessibility tree: its tree, node, role, and name.</summary>
public sealed record TreeObject(string TreeId, long NodeId, string Role, string Name);

/// <summary>
/// The requests an assistive technology made of a page through the platform
/// accessibility API, and how Chromium answered them, from protocol 0.60's
/// records on the <c>browser.accessibility</c> channel: each record's
/// outcome, for Selected event values, and the Under the pointer (Chromium)
/// row of the Properties panel. Hit test records at one point in quick
/// succession are one request, as a program's <c>accHitTest</c> asks each
/// object it is given in turn; a request is joined with the page's precise
/// results that follow it. An action the browser process sent is joined with
/// the renderer's receipt of it, and with the key pressed or the mouse button
/// released before it. Chromium cannot tell which program made a request.
/// Rule 3, as in
/// docs/architecture/accessibility-requests.md.
/// </summary>
public sealed class AccessibilityRequests
{
    public const int RuleVersion = 3;

    /// <summary>The longest a precise result or a receipt is joined after its request.</summary>
    public const long JoinWindowNanoseconds = 1_000_000_000;

    /// <summary>The longest a hit test record follows the one before at the same point, within one request.</summary>
    public const long RequestGapNanoseconds = 20_000_000;

    /// <summary>The longest an action follows the key or click it is linked with.</summary>
    public const long KeyWindowNanoseconds = 500_000_000;

    /// <summary>Actions sent within this of one another are one step.</summary>
    public const long StepNanoseconds = 1_000_000;

    public const string Channel = BrowserEvidenceChannels.Accessibility;

    /// <summary>The key of the Under the pointer (Chromium) row.</summary>
    public const string PointerKey = "accessibility.pointer";

    public const string PointerSetting = "Under the pointer (Chromium)";

    private const long ChangedWindowNanoseconds = 1_000_000_000;

    private static readonly HashSet<string> EventTypes = new(StringComparer.Ordinal)
    {
        BrowserEvidenceEventTypes.AccessibilityHitTest,
        BrowserEvidenceEventTypes.AccessibilityHitTestResult,
        BrowserEvidenceEventTypes.AccessibilityActionSent,
        BrowserEvidenceEventTypes.AccessibilityActionReceived,
        BrowserEvidenceEventTypes.AccessibilityHitTestComputed
    };

    private const string AnyProgram =
        "Chromium cannot tell which program asked: any program using Microsoft Active Accessibility, " +
        "IAccessible2, or UI Automation can.";

    private readonly Dictionary<string, AccessibilityRequestOutcome> _outcomes = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _sentAfter = new(StringComparer.Ordinal);

    private readonly List<Request> _requests = [];

    private readonly List<long> _requestTimes = [];

    private readonly List<long> _pointerChanges = [];

    // The accessibility tree as Chromium serialized it, by document token and
    // node: each node's versions in time order. Checkpoints are update
    // batches that do not record deletions, so a node's latest version at a
    // time is the best known (accessibility-checkpoint-evidence-model.md).
    private readonly Dictionary<string, Dictionary<long, List<TreeNode>>> _trees = new(StringComparer.Ordinal);

    // The nodes that listed a node as a child, by document token and child.
    private readonly Dictionary<(string Token, long Child), List<(long Time, long Parent)>> _parents = [];

    // The document token of each tree, from the renderer's records, which
    // name both.
    private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

    // The tree of each document token, the reverse of _documents.
    private readonly Dictionary<string, string> _treesOfDocuments = new(StringComparer.Ordinal);

    // The object of each DOM node, by document token and DOM node, from the
    // checkpoints' nodes: its versions in time order.
    private readonly Dictionary<(string Token, long DomNode), List<(long Time, long Node)>> _domNodes = [];

    private readonly List<RequestedAction> _actions = [];
    private readonly Dictionary<long, string> _keyEvents = [];

    private sealed record TreeNode(long Time, string Role, string Name, long[] Children);

    private static readonly System.Text.RegularExpressions.Regex ChildIds =
        new(@"(?:^|\s)child_ids=([0-9,]+)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The most characters of an unnamed object's text the row gives.</summary>
    public const int HeldTextCharacters = 60;

    private sealed record Node(string TreeId, long NodeId, string Role, string Name, string Bounds, Box? Box);

    private sealed record Box(long X, long Y, long Width, long Height)
    {
        public bool Holds(Box other) =>
            X <= other.X && Y <= other.Y && X + Width >= other.X + other.Width && Y + Height >= other.Y + other.Height;
    }

    private sealed record Item(string EventId, long Time, string EventType, JsonElement Payload);

    // A key pressed, or a mouse button released: what an action is linked
    // with. A click has its words, such as "a left click at 1095, 732".
    private sealed record Key(string EventId, long Time, int VirtualKey, string? Click = null);

    // One request: its hit test records, in order, the precise results joined
    // with it, and the time of the next request, when there is one.
    private sealed class Request
    {
        public required List<Item> HitTests { get; init; }

        public required string Point { get; init; }

        public List<Item> Results { get; } = [];

        public long? NextTime { get; set; }

        public Node? Given => NodeOf(HitTests[^1].Payload, "answer");

        public Node? Precise => Results.Count == 0 ? null : NodeOf(Results[^1].Payload, "result");

        public long Time => HitTests[0].Time;
    }

    /// <summary>
    /// The outcomes of the request records. The key records, when given, are
    /// the keyboard hook's and raw input's records the key outcomes come
    /// from; the kept commands are the keys NVDA kept, with its commands; the
    /// mouse records, the mouse hook's and raw input's records.
    /// </summary>
    public AccessibilityRequests(
        IEnumerable<KeyEvidenceRecord> records,
        IEnumerable<KeyEvidenceRecord>? keyRecords = null,
        IReadOnlyList<KeptCommand>? keptCommands = null,
        IEnumerable<KeyEvidenceRecord>? mouseRecords = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        records = records as IReadOnlyCollection<KeyEvidenceRecord> ?? records.ToList();
        ReadTrees(records);
        var items = records
            .Where(record => IsRequestEvidence(record.Channel, record.EventType) &&
                record.Payload.ValueKind == JsonValueKind.Object)
            .OrderBy(record => record.MonotonicNanoseconds)
            .Select(record => new Item(record.EventId, record.MonotonicNanoseconds, record.EventType, record.Payload))
            .ToList();
        Recorded = items.Count > 0;
        var keys = (keyRecords ?? [])
            .Where(record => record.Channel == KeyboardHookRecords.Channel &&
                record.EventType == KeyboardHookRecords.KeyEventType &&
                record.Payload.ValueKind == JsonValueKind.Object &&
                !ReadBoolean(record.Payload, "up") && !ReadBoolean(record.Payload, "injected") &&
                ReadInt64(record.Payload, "virtualKey") is not null)
            .OrderBy(record => record.MonotonicNanoseconds)
            .Select(record => new Key(record.EventId, record.MonotonicNanoseconds, (int)ReadInt64(record.Payload, "virtualKey")!.Value))
            .Concat((mouseRecords ?? [])
                .Where(record => record.Channel == MouseHookRecords.Channel &&
                    record.EventType == MouseHookRecords.EventType &&
                    record.Payload.ValueKind == JsonValueKind.Object &&
                    ReadString(record.Payload, "kind") == MouseHookRecords.ButtonUpKind)
                .Select(record => new Key(record.EventId, record.MonotonicNanoseconds, 0, ClickText(record.Payload))))
            .OrderBy(key => key.Time)
            .ToList();
        var kept = (keptCommands ?? []).GroupBy(command => command.Time).ToDictionary(group => group.Key, group => group.First());

        GroupRequests(items);
        var resultRequests = new Dictionary<string, Request>(StringComparer.Ordinal);
        foreach (var request in _requests)
        {
            foreach (var result in request.Results)
            {
                resultRequests[result.EventId] = request;
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var outcome = item.EventType switch
            {
                BrowserEvidenceEventTypes.AccessibilityHitTestResult => HitTestResult(item, resultRequests.GetValueOrDefault(item.EventId)),
                BrowserEvidenceEventTypes.AccessibilityActionSent => ActionSent(items, index, keys, kept),
                BrowserEvidenceEventTypes.AccessibilityActionReceived => ActionReceived(items, index),
                BrowserEvidenceEventTypes.AccessibilityHitTestComputed => HitTestComputed(item.Payload),
                _ => null
            };
            if (outcome is not null)
            {
                _outcomes[item.EventId] = outcome;
            }
        }

        for (var index = 0; index < _requests.Count; index++)
        {
            var request = _requests[index];
            var next = index + 1 < _requests.Count ? _requests[index + 1] : null;
            _outcomes[request.HitTests[0].EventId] = RequestOutcome(request, next);
            for (var part = 1; part < request.HitTests.Count; part++)
            {
                _outcomes[request.HitTests[part].EventId] = PartOutcome(request, part);
            }

            _requestTimes.Add(request.Time);
            var previous = index > 0 ? _requests[index - 1].Given : null;
            if (index == 0 || !SameNode(previous, request.Given))
            {
                _pointerChanges.Add(request.Time);
            }
        }

        LinkKeys(items, keys, kept);
    }

    public static AccessibilityRequests Empty { get; } = new([]);

    /// <summary>Whether the recording holds request records.</summary>
    public bool Recorded { get; }

    public string Rule => $"rule {RuleVersion.ToString(CultureInfo.InvariantCulture)}";

    public AccessibilityRequestOutcome? Of(string eventId) => _outcomes.GetValueOrDefault(eventId);

    /// <summary>
    /// For a key record or a mouse button's release, what was sent to the
    /// page after it: a line for its Selected event values; null for a key no
    /// action followed and NVDA did not keep, a click no action followed, or
    /// a recording without request records.
    /// </summary>
    public string? SentAfter(string eventId) => _sentAfter.GetValueOrDefault(eventId);

    /// <summary>The actions sent to the page, in time order, each with the key or click before it.</summary>
    public IReadOnlyList<RequestedAction> Actions => _actions;

    /// <summary>The record of the key pressed at a time, which a kept command names by its time.</summary>
    public string? KeyEventAt(long time) => _keyEvents.GetValueOrDefault(time);

    /// <summary>
    /// An object as the Under the pointer (Chromium) row gives it: with no
    /// name, also the text it holds and the object it is in, from the tree
    /// as Chromium last serialized it before the time.
    /// </summary>
    public string DescribeObject(TreeObject item, long time)
    {
        ArgumentNullException.ThrowIfNull(item);
        return DescribeInTree(new Node(item.TreeId, item.NodeId, item.Role, item.Name, "unrecorded", null), time);
    }

    /// <summary>A short label of an object, for a band: its role and name, or the start of the text it holds.</summary>
    public string LabelOf(TreeObject item, long time)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Name.Length > 0)
        {
            return $"{item.Role} \"{item.Name}\"";
        }

        if (_documents.TryGetValue(item.TreeId, out var token) && TextWithin(token, item.NodeId, time) is { Length: > 0 } text)
        {
            return $"{item.Role} \"{(text.Length > 30 ? text[..30].TrimEnd() + "…" : text)}\"";
        }

        return item.Role;
    }

    /// <summary>
    /// The object of a DOM node of a document, as the checkpoints last gave it
    /// before the time; null when the checkpoints do not name it.
    /// </summary>
    public TreeObject? ObjectOfDomNode(string documentToken, long domNode, long time)
    {
        ArgumentNullException.ThrowIfNull(documentToken);
        if (!_domNodes.TryGetValue((documentToken, domNode), out var objects) ||
            !_treesOfDocuments.TryGetValue(documentToken, out var treeId))
        {
            return null;
        }

        long? id = null;
        foreach (var (at, node) in objects)
        {
            if (at > time)
            {
                break;
            }

            id = node;
        }

        id ??= objects[0].Node;
        return NodeAt(documentToken, id.Value, time) is { } found
            ? new TreeObject(treeId, id.Value, found.Role, found.Name)
            : null;
    }

    /// <summary>
    /// The Under the pointer (Chromium) row at a time, in the Screen reader
    /// group: the object the latest request at or before it was given, and
    /// the page's precise result for it; none for a recording without hit
    /// test records.
    /// </summary>
    public IReadOnlyList<PropertyRow> RowsAt(long time)
    {
        if (_requests.Count == 0)
        {
            return [];
        }

        var index = _requestTimes.BinarySearch(time);
        if (index < 0)
        {
            index = ~index - 1;
        }

        if (index < 0)
        {
            return
            [
                new PropertyRow(AssistiveTechnologyTimeline.Group, PointerSetting, "no hit test yet", "at start", null, false, PointerKey)
            ];
        }

        var request = _requests[index];
        var given = request.Given is { } node ? DescribeInTree(node, request.Time) : "no object";
        var precise = request.Precise is not { } found
            ? "no precise result"
            : SameNode(request.Given, found) ? "the page found the same" : $"the page found {DescribeInTree(found, request.Time)}";
        return
        [
            new PropertyRow(
                AssistiveTechnologyTimeline.Group,
                PointerSetting,
                $"{given}; {precise}",
                WindowsPreferenceTimeline.Clock(request.Time),
                request.Time,
                time - request.Time < ChangedWindowNanoseconds,
                PointerKey)
        ];
    }

    /// <summary>
    /// The times the Under the pointer (Chromium) row's object changed, for
    /// stepping: each request given another object than the request before;
    /// null for another key or a recording without hit test records.
    /// </summary>
    public IReadOnlyList<long>? ChangeTimesOf(string key) =>
        key == PointerKey && _requests.Count > 0 ? _pointerChanges : null;

    /// <summary>Whether playback keeps a record for the accessibility tree the Under the pointer (Chromium) row reads.</summary>
    public static bool IsTreeEvidence(string channel, string eventType) =>
        channel == Channel && eventType == BrowserEvidenceEventTypes.AccessibilityCheckpointNode;

    public static bool IsRequestEvidence(string channel, string eventType) =>
        channel == Channel && EventTypes.Contains(eventType);

    // The checkpoints' nodes, and the document token of each tree.
    private void ReadTrees(IEnumerable<KeyEvidenceRecord> records)
    {
        foreach (var record in records.OrderBy(record => record.MonotonicNanoseconds))
        {
            var payload = record.Payload;
            if (payload.ValueKind != JsonValueKind.Object || record.Channel != Channel ||
                !payload.TryGetProperty("context", out var context) || context.ValueKind != JsonValueKind.Object ||
                ReadString(context, "documentToken") is not { } token)
            {
                continue;
            }

            if (record.EventType is BrowserEvidenceEventTypes.AccessibilityHitTestComputed or BrowserEvidenceEventTypes.AccessibilityActionReceived &&
                ReadString(payload, "treeId") is { } treeId)
            {
                _documents.TryAdd(treeId, token);
                _treesOfDocuments.TryAdd(token, treeId);
                continue;
            }

            if (record.EventType != BrowserEvidenceEventTypes.AccessibilityCheckpointNode ||
                ReadInt64(payload, "accessibilityNodeId") is not { } id)
            {
                continue;
            }

            var match = ChildIds.Match(ReadString(payload, "serializedProperties") ?? string.Empty);
            var children = match.Success
                ? match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var child) ? child : -1)
                    .Where(child => child >= 0)
                    .ToArray()
                : [];
            if (!_trees.TryGetValue(token, out var tree))
            {
                _trees[token] = tree = [];
            }

            if (!tree.TryGetValue(id, out var versions))
            {
                tree[id] = versions = [];
            }

            versions.Add(new TreeNode(
                record.MonotonicNanoseconds, ReadString(payload, "roleName") ?? "object", ReadString(payload, "name") ?? string.Empty, children));
            if (ReadInt64(payload, "domNodeId") is { } domNode)
            {
                if (!_domNodes.TryGetValue((token, domNode), out var objects))
                {
                    _domNodes[(token, domNode)] = objects = [];
                }

                objects.Add((record.MonotonicNanoseconds, id));
            }

            foreach (var child in children)
            {
                if (!_parents.TryGetValue((token, child), out var parents))
                {
                    _parents[(token, child)] = parents = [];
                }

                parents.Add((record.MonotonicNanoseconds, id));
            }
        }
    }

    private TreeNode? NodeAt(string token, long id, long time)
    {
        if (!_trees.TryGetValue(token, out var tree) || !tree.TryGetValue(id, out var versions))
        {
            return null;
        }

        TreeNode? found = null;
        foreach (var version in versions)
        {
            if (version.Time > time)
            {
                break;
            }

            found = version;
        }

        return found;
    }

    // The node that lists a node as a child at a time, in its latest version.
    private (long Id, TreeNode Node)? ParentAt(string token, long id, long time)
    {
        if (!_parents.TryGetValue((token, id), out var parents))
        {
            return null;
        }

        for (var index = parents.Count - 1; index >= 0; index--)
        {
            var (at, parent) = parents[index];
            if (at <= time && NodeAt(token, parent, time) is { } node && node.Children.Contains(id))
            {
                return (parent, node);
            }
        }

        return null;
    }

    // The text an object holds: its static text's names, in tree order, up
    // to HeldTextCharacters.
    private string TextWithin(string token, long id, long time)
    {
        var text = new System.Text.StringBuilder();
        var stack = new Stack<long>([id]);
        var visited = new HashSet<long>();
        while (stack.Count > 0 && visited.Count < 500 && text.Length <= HeldTextCharacters)
        {
            var current = stack.Pop();
            if (!visited.Add(current) || NodeAt(token, current, time) is not { } node)
            {
                continue;
            }

            if (node.Role == "staticText" && node.Name.Trim().Length > 0)
            {
                if (text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(node.Name.Trim());
                continue;
            }

            for (var child = node.Children.Length - 1; child >= 0; child--)
            {
                stack.Push(node.Children[child]);
            }
        }

        var all = System.Text.RegularExpressions.Regex.Replace(text.ToString(), @"\s+", " ");
        return all.Length > HeldTextCharacters ? all[..HeldTextCharacters].TrimEnd() + "…" : all;
    }

    // An object as the Under the pointer (Chromium) row gives it: an object
    // with no name also with the text it holds and the object it is in, from
    // the accessibility tree as Chromium last serialized it before the time.
    private string DescribeInTree(Node node, long time)
    {
        var text = Describe(node);
        if (!string.IsNullOrEmpty(node.Name))
        {
            return text;
        }

        if (!_documents.TryGetValue(node.TreeId, out var token) || !_trees.ContainsKey(token))
        {
            return $"{text}, whose document's accessibility tree was not recorded";
        }

        if (NodeAt(token, node.NodeId, time) is null)
        {
            return $"{text}, not in the recorded accessibility tree";
        }

        var held = TextWithin(token, node.NodeId, time);
        text += held.Length == 0 ? ", holding no text" : $", holding \"{held}\"";
        if (ParentAt(token, node.NodeId, time) is { } parent)
        {
            var id = parent.Id.ToString(CultureInfo.InvariantCulture);
            text += string.IsNullOrEmpty(parent.Node.Name)
                ? $", in {parent.Node.Role} with no name (node {id})"
                : $", in {parent.Node.Role} \"{parent.Node.Name}\" (node {id})";
        }

        return text;
    }

    // Hit test records at one point, each within RequestGapNanoseconds of the
    // one before, with no hit test at another point between, are one request.
    // A precise result is joined with the latest request before it, within
    // the join window.
    private void GroupRequests(List<Item> items)
    {
        Request? current = null;
        long last = 0;
        foreach (var item in items)
        {
            if (item.EventType == BrowserEvidenceEventTypes.AccessibilityHitTest)
            {
                var point = PointOf(item.Payload, "point") ?? "an unrecorded point";
                if (current is not null && current.Point == point && item.Time - last <= RequestGapNanoseconds)
                {
                    current.HitTests.Add(item);
                }
                else
                {
                    if (current is not null)
                    {
                        current.NextTime = item.Time;
                    }

                    current = new Request { HitTests = [item], Point = point };
                    _requests.Add(current);
                }

                last = item.Time;
            }
            else if (item.EventType == BrowserEvidenceEventTypes.AccessibilityHitTestResult &&
                current is not null && item.Time - current.Time <= JoinWindowNanoseconds &&
                NodeOf(item.Payload, "result") is not null)
            {
                current.Results.Add(item);
            }
        }
    }

    private static AccessibilityRequestOutcome RequestOutcome(Request request, Request? next)
    {
        var first = request.HitTests[0];
        var payload = first.Payload;
        var answer = NodeOf(payload, "answer");
        var basis = BasisText(payload);
        var count = request.HitTests.Count;
        var text = answer is null
            ? $"Given at once: no object, from {basis}"
            : $"Given at once: {Describe(answer)}, from {basis}";
        if (count > 1)
        {
            text += $"; one request of {count.ToString(CultureInfo.InvariantCulture)} hit tests at {request.Point} " +
                $"within {Milliseconds(request.HitTests[^1].Time - first.Time)}";
        }

        var details = new List<string> { $"Point: {request.Point} on the screen, in physical pixels." };
        var given = request.Given;
        if (count > 1)
        {
            details.Add(
                $"The {count.ToString(CultureInfo.InvariantCulture)} hit tests followed one another at the same point, each within " +
                $"{Milliseconds(RequestGapNanoseconds)} of the one before, as a program's accHitTest asks each object it is given in turn. " +
                $"The last was given {(given is null ? "no object" : Describe(given))}.");
        }

        var asked = request.HitTests.Any(item =>
            item.Payload.TryGetProperty("preciseRequested", out var requested) && requested.ValueKind == JsonValueKind.True);
        if (request.Precise is { } found)
        {
            var results = request.Results.Count;
            var distinct = request.Results
                .Select(item => NodeOf(item.Payload, "result")!)
                .DistinctBy(node => (node.TreeId, node.NodeId))
                .ToList();
            var same = SameNode(given, found);
            details.Add(
                $"The page's precise hit test then found {Describe(found)}, " +
                $"{Milliseconds(request.Results[^1].Time - first.Time)} after the request began: " +
                (same ? "the object given." : "not the object given."));
            if (results > 1)
            {
                details.Add(distinct.Count == 1
                    ? $"{results.ToString(CultureInfo.InvariantCulture)} precise results arrived, all for that object."
                    : $"{results.ToString(CultureInfo.InvariantCulture)} precise results arrived: " +
                      string.Join("; ", distinct.Select(Describe)) + ".");
            }

            if (!same && given is not null)
            {
                if (given.TreeId != found.TreeId)
                {
                    details.Add("The precise result is in another document's tree, such as a frame's.");
                }
                else if (given.Box is { } outer && found.Box is { } inner && outer.Holds(inner))
                {
                    details.Add(
                        "The object given is a container the pointer had moved into: its bounds hold those of the precise result.");
                }
                else
                {
                    details.Add("The object given is a different object from the page's precise result.");
                }

                if (next is not null)
                {
                    details.Add(
                        $"The next request, {Milliseconds(next.Time - request.Time)} later, was given " +
                        $"{(next.Given is { } then ? Describe(then) : "no object")}.");
                }
            }

            details.Add(
                "Chromium does not say which request a result answers: each is joined with the latest request before it.");
        }
        else if (asked)
        {
            details.Add(request.NextTime is { } nextTime && nextTime - first.Time < JoinWindowNanoseconds
                ? $"The page was asked for a precise hit test; no result arrived before the next request, " +
                  $"{Milliseconds(nextTime - first.Time)} later. A point with no object fires no result."
                : $"The page was asked for a precise hit test; no result was recorded within " +
                  $"{Milliseconds(JoinWindowNanoseconds)}. A point with no object fires no result.");
        }
        else
        {
            details.Add("The page was not asked for a precise hit test.");
        }

        details.Add(AnyProgram);
        return new AccessibilityRequestOutcome(text, details);
    }

    private static AccessibilityRequestOutcome PartOutcome(Request request, int part)
    {
        var item = request.HitTests[part];
        var answer = NodeOf(item.Payload, "answer");
        return new AccessibilityRequestOutcome(
            $"Part of the request at {WindowsPreferenceTimeline.Clock(request.Time)}: hit test " +
            $"{(part + 1).ToString(CultureInfo.InvariantCulture)} of {request.HitTests.Count.ToString(CultureInfo.InvariantCulture)}, " +
            $"given {(answer is null ? "no object" : Describe(answer))}",
            [
                $"Point: {request.Point} on the screen, in physical pixels.",
                $"Given from {BasisText(item.Payload)}.",
                "The request's outcome is on its first hit test.",
                AnyProgram
            ]);
    }

    private static AccessibilityRequestOutcome? HitTestResult(Item item, Request? request)
    {
        if (NodeOf(item.Payload, "result") is not { } found)
        {
            return null;
        }

        var details = new List<string>
        {
            $"Kept for the next hit test whose point lies within its bounds, {found.Bounds} on the screen in physical pixels."
        };
        details.Add(request is null
            ? $"No hit test was recorded in the {Milliseconds(JoinWindowNanoseconds)} before it."
            : $"Joined with the request at {WindowsPreferenceTimeline.Clock(request.Time)}, " +
              $"{Count(request.HitTests.Count, "hit test", "hit tests")} at {request.Point}, the latest before it, " +
              $"{Milliseconds(item.Time - request.Time)} after the request began.");
        return new AccessibilityRequestOutcome($"The page's precise hit test found {Describe(found)}", details);
    }

    private static string BasisText(JsonElement payload) =>
        ReadString(payload, "basis") == BrowserAccessibilityRequests.LastPreciseResult
            ? "the page's last precise result, whose bounds held the point"
            : "a guess from Chromium's own copy of the tree, by bounds";

    private static AccessibilityRequestOutcome? ActionSent(
        List<Item> items, int index, List<Key> keys, Dictionary<long, KeptCommand> kept)
    {
        var item = items[index];
        var payload = item.Payload;
        var action = ReadString(payload, "action");
        if (action is null)
        {
            return null;
        }

        var targetId = ReadInt64(payload, "targetNodeId");
        var target = NodeOf(payload, "target") is { } node
            ? Describe(node)
            : targetId is { } id ? $"node {id.ToString(CultureInfo.InvariantCulture)}, not in the browser's copy of the tree" : null;
        var details = new List<string>();
        if (RectOf(payload, "targetRect") is { } rect)
        {
            details.Add(rect == "0, 0, 0 by 0"
                ? "Scrolls the whole object into view."
                : $"Scrolls the part {rect} of the object, in its own coordinates, into view.");
        }

        if (PointOf(payload, "targetPoint") is { } point)
        {
            details.Add($"Point: {point}.");
        }

        var horizontal = ReadString(payload, "horizontalScrollAlignment");
        var vertical = ReadString(payload, "verticalScrollAlignment");
        if (horizontal is not null and not "none" || vertical is not null and not "none")
        {
            details.Add($"Alignment: horizontal {horizontal}, vertical {vertical}.");
        }

        if (ReadString(payload, "scrollBehavior") is { } behavior and not "none")
        {
            details.Add($"Scroll behavior: {behavior}.");
        }

        if (payload.TryGetProperty("selection", out var selection) && selection.ValueKind == JsonValueKind.Object)
        {
            details.Add(
                $"Selection: from node {ReadInt64(selection, "anchorNodeId")} offset {ReadInt64(selection, "anchorOffset")} " +
                $"to node {ReadInt64(selection, "focusNodeId")} offset {ReadInt64(selection, "focusOffset")}.");
        }

        var treeId = ReadString(payload, "treeId");
        Item? received = null;
        for (var next = index + 1; next < items.Count && items[next].Time - item.Time <= JoinWindowNanoseconds; next++)
        {
            if (items[next].EventType == BrowserEvidenceEventTypes.AccessibilityActionReceived &&
                ReadString(items[next].Payload, "treeId") == treeId &&
                ReadString(items[next].Payload, "action") == action &&
                ReadInt64(items[next].Payload, "targetNodeId") == targetId)
            {
                received = items[next];
                break;
            }
        }

        details.Add(received is null
            ? $"No receipt by the page's renderer was recorded within {Milliseconds(JoinWindowNanoseconds)}."
            : $"The page's renderer received it {Milliseconds(received.Time - item.Time)} later.");
        var with = items
            .Where(other => !ReferenceEquals(other, item) && other.EventType == BrowserEvidenceEventTypes.AccessibilityActionSent &&
                Math.Abs(other.Time - item.Time) <= StepNanoseconds)
            .Select(SentText)
            .OfType<string>()
            .ToList();
        if (with.Count > 0)
        {
            details.Add($"Sent with it, as one step: {string.Join("; ", with)}.");
        }

        details.Add(KeyBefore(keys, item.Time) is not { } key
            ? $"No key was pressed and no mouse button released in the {Milliseconds(KeyWindowNanoseconds)} before it."
            : key.Click is { } click
                ? $"After {click}, the button released {Milliseconds(item.Time - key.Time)} earlier."
                : $"After the key {KeyboardHookRecords.KeyName(key.VirtualKey)}, {Milliseconds(item.Time - key.Time)} earlier" +
                  (kept.GetValueOrDefault(key.Time) is { } command
                      ? $", which NVDA kept as {string.Join(" or ", command.Names)} ({command.Gesture}), inferred."
                      : "."));
        details.Add(AnyProgram);
        return new AccessibilityRequestOutcome($"Asked the page to {ActionText(action, target)}", details);
    }

    // A mouse button's release in words: the button, the point, and whether
    // a program injected it, as head pointer and eye tracking software can.
    private static string ClickText(JsonElement payload)
    {
        var button = ReadString(payload, "button") ?? "mouse";
        var point = ReadInt64(payload, "x") is { } x && ReadInt64(payload, "y") is { } y
            ? string.Create(CultureInfo.InvariantCulture, $" at {x}, {y}")
            : string.Empty;
        var injected = ReadBoolean(payload, "injected") ? ", injected by a program" : string.Empty;
        return $"a {button} click{point}{injected}";
    }

    // The latest key pressed or mouse button released at or before a time,
    // within the key window.
    private static Key? KeyBefore(List<Key> keys, long time)
    {
        for (var index = keys.Count - 1; index >= 0; index--)
        {
            if (keys[index].Time <= time)
            {
                return time - keys[index].Time <= KeyWindowNanoseconds ? keys[index] : null;
            }
        }

        return null;
    }

    // What an action sent asks, for lists of actions.
    private static string? SentText(Item item)
    {
        if (ReadString(item.Payload, "action") is not { } action)
        {
            return null;
        }

        var targetId = ReadInt64(item.Payload, "targetNodeId");
        var target = NodeOf(item.Payload, "target") is { } node
            ? Describe(node)
            : targetId is { } id ? $"node {id.ToString(CultureInfo.InvariantCulture)}" : null;
        return ActionText(action, target);
    }

    // For each key and click, the actions sent after it and linked with it:
    // those whose latest key or click, within the key window, it is. A key
    // NVDA kept that no action followed says so.
    private void LinkKeys(List<Item> items, List<Key> keys, Dictionary<long, KeptCommand> kept)
    {
        if (!Recorded)
        {
            return;
        }

        foreach (var key in keys.Where(key => key.Click is null))
        {
            _keyEvents.TryAdd(key.Time, key.EventId);
        }

        var linked = new Dictionary<Key, List<string>>();
        foreach (var item in items.Where(item => item.EventType == BrowserEvidenceEventTypes.AccessibilityActionSent))
        {
            if (ReadString(item.Payload, "action") is { } action)
            {
                var target = NodeOf(item.Payload, "target");
                var before = KeyBefore(keys, item.Time);
                _actions.Add(new RequestedAction(
                    item.EventId,
                    item.Time,
                    action,
                    target?.TreeId ?? ReadString(item.Payload, "treeId"),
                    target?.NodeId ?? ReadInt64(item.Payload, "targetNodeId"),
                    target?.Role ?? "object",
                    target?.Name ?? string.Empty,
                    before?.EventId,
                    before?.Time,
                    before?.Click,
                    before is { Click: null } pressed ? kept.GetValueOrDefault(pressed.Time) : null));
            }

            if (KeyBefore(keys, item.Time) is { } key && SentText(item) is { } text)
            {
                if (!linked.TryGetValue(key, out var list))
                {
                    linked[key] = list = [];
                }

                list.Add(text);
            }
        }

        foreach (var key in keys)
        {
            if (linked.TryGetValue(key, out var sent))
            {
                _sentAfter[key.EventId] = $"Sent to the page after it: {string.Join("; ", sent)}.";
            }
            else if (key.Click is null && kept.ContainsKey(key.Time))
            {
                _sentAfter[key.EventId] = $"Nothing was sent to the page in the {Milliseconds(KeyWindowNanoseconds)} after it.";
            }
        }
    }

    private static AccessibilityRequestOutcome? ActionReceived(List<Item> items, int index)
    {
        var item = items[index];
        var payload = item.Payload;
        var action = ReadString(payload, "action");
        if (action is null)
        {
            return null;
        }

        var targetId = ReadInt64(payload, "targetNodeId");
        var treeId = ReadString(payload, "treeId");
        var details = new List<string>();
        for (var previous = index - 1; previous >= 0 && item.Time - items[previous].Time <= JoinWindowNanoseconds; previous--)
        {
            if (items[previous].EventType == BrowserEvidenceEventTypes.AccessibilityActionSent &&
                ReadString(items[previous].Payload, "treeId") == treeId &&
                ReadString(items[previous].Payload, "action") == action &&
                ReadInt64(items[previous].Payload, "targetNodeId") == targetId)
            {
                details.Add($"Sent by the browser process {Milliseconds(item.Time - items[previous].Time)} earlier.");
                break;
            }
        }

        if (details.Count == 0)
        {
            details.Add($"No sending by the browser process was recorded in the {Milliseconds(JoinWindowNanoseconds)} before it.");
        }

        var target = targetId is { } id ? $"node {id.ToString(CultureInfo.InvariantCulture)}" : null;
        return new AccessibilityRequestOutcome($"The page's renderer was asked to {ActionText(action, target)}", details);
    }

    private static AccessibilityRequestOutcome HitTestComputed(JsonElement payload)
    {
        var point = PointOf(payload, "point") ?? "an unrecorded point";
        var node = ReadInt64(payload, "nodeId");
        var child = payload.TryGetProperty("childTree", out var value) && value.ValueKind == JsonValueKind.True;
        var text = node is not { } id
            ? $"The page's hit test at {point} found no object"
            : child
                ? $"The page's hit test at {point} found node {id.ToString(CultureInfo.InvariantCulture)}, which holds a child frame or tree, where it continues"
                : $"The page's hit test at {point} found node {id.ToString(CultureInfo.InvariantCulture)}";
        return new AccessibilityRequestOutcome(
            text,
            [
                "The point is in the frame's coordinates. The node is of the renderer's tree, whose accessibility checkpoints name it."
            ]);
    }

    private static string ActionText(string action, string? target)
    {
        var on = target ?? "no object";
        return action switch
        {
            "scrollToMakeVisible" => $"scroll {on} into view",
            "focus" => $"focus {on}",
            "blur" => $"remove the focus from {on}",
            "doDefault" => $"do the default action of {on}, such as activating it",
            "setSelection" => $"set the selection, in {on}",
            "scrollToPoint" => $"scroll {on} to a point",
            "setScrollOffset" => $"set the scroll offset of {on}",
            "showContextMenu" => $"show the context menu of {on}",
            "setValue" => $"set the value of {on}",
            "increment" => $"increment {on}",
            "decrement" => $"decrement {on}",
            "loadInlineTextBoxes" => $"load the inline text boxes of {on}",
            "setSequentialFocusNavigationStartingPoint" => $"start the next Tab navigation from {on}",
            "setAccessibilityFocus" => $"move Chromium's accessibility focus, its note of the assistive technology's position, to {on}",
            "clearAccessibilityFocus" => $"clear Chromium's accessibility focus from {on}",
            _ => $"perform {action} on {on}"
        };
    }

    private static string Describe(Node node)
    {
        var id = node.NodeId.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(node.Name)
            ? $"{node.Role} with no name (node {id})"
            : $"{node.Role} \"{node.Name}\" (node {id})";
    }

    private static Node? NodeOf(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Object ||
            ReadString(node, "treeId") is not { } tree || ReadInt64(node, "nodeId") is not { } id)
        {
            return null;
        }

        Box? box = node.TryGetProperty("bounds", out var bounds) && bounds.ValueKind == JsonValueKind.Object &&
            ReadInt64(bounds, "x") is { } x && ReadInt64(bounds, "y") is { } y &&
            ReadInt64(bounds, "width") is { } width && ReadInt64(bounds, "height") is { } height
                ? new Box(x, y, width, height)
                : null;
        return new Node(
            tree, id, ReadString(node, "role") ?? "object", ReadString(node, "name") ?? string.Empty, RectOf(node, "bounds") ?? "unrecorded", box);
    }

    private static bool SameNode(Node? first, Node? second) =>
        first is null ? second is null : second is not null && first.TreeId == second.TreeId && first.NodeId == second.NodeId;

    private static bool ReadBoolean(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.True;

    private static string? PointOf(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var point) && point.ValueKind == JsonValueKind.Object &&
        ReadInt64(point, "x") is { } x && ReadInt64(point, "y") is { } y
            ? $"{x.ToString(CultureInfo.InvariantCulture)}, {y.ToString(CultureInfo.InvariantCulture)}"
            : null;

    private static string? RectOf(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var rect) && rect.ValueKind == JsonValueKind.Object &&
        ReadInt64(rect, "x") is { } x && ReadInt64(rect, "y") is { } y &&
        ReadInt64(rect, "width") is { } width && ReadInt64(rect, "height") is { } height
            ? string.Create(CultureInfo.InvariantCulture, $"{x}, {y}, {width} by {height}")
            : null;

    private static string Milliseconds(long nanoseconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{nanoseconds / 1_000_000.0:0.#} ms");

    private static string Count(int count, string one, string many) =>
        count == 1 ? $"1 {one}" : $"{count.ToString(CultureInfo.InvariantCulture)} {many}";

    private static string? ReadString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static long? ReadInt64(JsonElement value, string property) =>
        value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var number)
            ? number
            : null;
}

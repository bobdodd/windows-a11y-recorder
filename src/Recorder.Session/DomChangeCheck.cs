using System.Text;
using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// Checks the structural DOM change records of a recording (protocol 0.34)
/// against its DOM checkpoints. For each document, the tree is taken from a
/// completed checkpoint and changed by every DOM transition after it, in
/// record order: insertions with their subtrees, removals, removals of all
/// children, attribute and character data changes, shadow root changes, and
/// slot assignments. At the next completed checkpoint of the document, every
/// node of the rebuilt tree is compared with the checkpoint's: its parent,
/// its children in order, its type and name, its attributes, its character
/// data, its shadow root fields, and its slot assignment. The tree is then
/// taken from that checkpoint, so each difference is reported in the
/// interval between the two checkpoints where it arose. A checkpoint walked
/// after a lost DOM record (protocol 0.35, walk reason "after-loss") is not
/// compared, since the rebuilt tree lacks the lost change; the tree is taken
/// from it. A checkpoint at a finished parse is not compared either: the
/// parser's insertions while a document parses are not recorded, and a
/// document parsed again after <c>document.open()</c> removed its children
/// has only its finished-parsing checkpoint as its state.
///
/// Each scroll offset record is compared with the last record of its scroll
/// translation node at the end of its change set, whose translation is the
/// negated scroll position: the scroll origin plus the scroll offset.
/// </summary>
public sealed class DomChangeCheck
{
    /// <summary>The largest difference, in pixels, between a scroll position
    /// and its negated translation that is counted as equal.</summary>
    public const double ScrollTolerance = 0.01;

    private const int MaximumExamples = 20;

    private readonly Dictionary<string, DocumentTree> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckpointTree> _open = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Insertion> _insertions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double X, double Y)> _translations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<JsonElement>> _scrolls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _differences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _examples = new(StringComparer.Ordinal);

    private sealed class DomNode(long id)
    {
        public long Id { get; } = id;
        public long? ParentId { get; set; }
        public string? NodeType { get; set; }
        public string? NodeName { get; set; }
        public List<long> Children { get; } = [];
        public long? ShadowRootId { get; set; }
        public SortedDictionary<string, string?> Attributes { get; } = new(StringComparer.Ordinal);
        public string? Data { get; set; }
        public string? ShadowRootFields { get; set; }
        public string? AssignedNodes { get; set; }
    }

    private sealed class DocumentTree
    {
        public Dictionary<long, DomNode> Nodes { get; } = [];
    }

    private sealed class CheckpointTree(string id, bool afterLoss, bool finishedParsing)
    {
        public string Id { get; } = id;
        public bool AfterLoss { get; } = afterLoss;
        public bool FinishedParsing { get; } = finishedParsing;
        public DocumentTree Tree { get; } = new();
    }

    private sealed class Insertion(string id, DocumentTree tree)
    {
        public string Id { get; } = id;
        public DocumentTree Tree { get; } = tree;
        public int Nodes { get; set; }
        public int Attributes { get; set; }
        public int CharacterData { get; set; }
        public int ShadowRoots { get; set; }
        public int Slots { get; set; }
    }

    public int CheckpointsCompared { get; private set; }
    public int CheckpointsTruncated { get; private set; }
    public int CheckpointsAfterLoss { get; private set; }
    public int CheckpointsAtFinishedParse { get; private set; }
    public int NodesCompared { get; private set; }
    public int NodesMatched { get; private set; }
    public int Insertions { get; private set; }
    public int InsertedNodes { get; private set; }
    public int Removals { get; private set; }
    public int ChildrenRemovals { get; private set; }
    public int AttributeTransitions { get; private set; }
    public int CharacterDataTransitions { get; private set; }
    public int ShadowRootChanges { get; private set; }
    public int SlotAssignmentChanges { get; private set; }
    public int ChangesOutsideTheTree { get; private set; }
    public int ScrollOffsets { get; private set; }
    public int ScrollOffsetsCompared { get; private set; }
    public int ScrollOffsetsMatched { get; private set; }

    // A document is keyed by its token and its document identity: in the
    // recording at revision 70d22d4, two documents of one process, with
    // different document identities, were recorded under one token 3 ms
    // apart.
    private static string? DocumentKey(JsonElement payload)
    {
        var token = LayoutChangeState.DocumentToken(payload);
        if (token is null)
        {
            return null;
        }
        return payload.GetProperty("context").TryGetProperty("documentId", out var id) &&
            id.ValueKind == JsonValueKind.String
                ? token + " " + id.GetString()
                : token;
    }

    /// <summary>The number of differences of each kind.</summary>
    public IReadOnlyDictionary<string, int> Differences => _differences;

    /// <summary>Applies one browser.dom or browser.layout record, in record
    /// order.</summary>
    public void Add(string eventType, JsonElement payload)
    {
        var token = DocumentKey(payload);
        if (token is null)
        {
            return;
        }
        switch (eventType)
        {
            case "dom-checkpoint-started":
                _open[token] = new CheckpointTree(
                    payload.GetProperty("checkpointId").GetString()!,
                    payload.TryGetProperty("walkReason", out var walkReason) &&
                        walkReason.ValueKind == JsonValueKind.String &&
                        walkReason.GetString() == "after-loss",
                    payload.TryGetProperty("reason", out var reason) &&
                        reason.ValueKind == JsonValueKind.String &&
                        reason.GetString() == "finished-parsing");
                break;
            case "dom-checkpoint-node":
                if (_open.TryGetValue(token, out var open))
                {
                    AddNode(open.Tree, payload, null);
                }
                break;
            case "dom-checkpoint-node-attribute":
                if (_open.TryGetValue(token, out open))
                {
                    SetAttribute(open.Tree, payload, open.Id);
                }
                break;
            case "dom-checkpoint-node-character-data":
                if (_open.TryGetValue(token, out open))
                {
                    SetData(open.Tree, payload, "data", open.Id);
                }
                break;
            case "dom-checkpoint-shadow-root":
                if (_open.TryGetValue(token, out open))
                {
                    SetShadowRoot(open.Tree, payload, open.Id);
                }
                break;
            case "dom-checkpoint-slot-assignment":
                if (_open.TryGetValue(token, out open))
                {
                    SetAssignment(open.Tree, payload, open.Id);
                }
                break;
            case "dom-checkpoint-completed":
                if (_open.Remove(token, out open))
                {
                    CompleteCheckpoint(token, open, payload);
                }
                break;
            case "dom-node-inserted":
                StartInsertion(token, payload);
                break;
            case "dom-inserted-node":
                if (FindInsertion(token, payload) is { } insertion)
                {
                    insertion.Nodes++;
                    InsertedNodes++;
                    AddNode(insertion.Tree, payload, insertion.Id);
                }
                break;
            case "dom-inserted-node-attribute":
                if (FindInsertion(token, payload) is { } withAttribute)
                {
                    withAttribute.Attributes++;
                    SetAttribute(withAttribute.Tree, payload, withAttribute.Id);
                }
                break;
            case "dom-inserted-node-character-data":
                if (FindInsertion(token, payload) is { } withData)
                {
                    withData.CharacterData++;
                    SetData(withData.Tree, payload, "data", withData.Id);
                }
                break;
            case "dom-inserted-shadow-root":
                if (FindInsertion(token, payload) is { } withShadowRoot)
                {
                    withShadowRoot.ShadowRoots++;
                    SetShadowRoot(withShadowRoot.Tree, payload, withShadowRoot.Id);
                }
                break;
            case "dom-inserted-slot-assignment":
                if (FindInsertion(token, payload) is { } withSlot)
                {
                    withSlot.Slots++;
                    SetAssignment(withSlot.Tree, payload, withSlot.Id);
                }
                break;
            case "dom-insertion-completed":
                CompleteInsertion(token, payload);
                break;
            case "dom-node-removed":
                Removals++;
                if (Transition(token) is { } removedFrom)
                {
                    Remove(removedFrom, payload);
                }
                break;
            case "dom-children-removed":
                ChildrenRemovals++;
                if (Transition(token) is { } emptied)
                {
                    RemoveChildren(emptied, payload);
                }
                break;
            case "dom-attribute-changed":
                AttributeTransitions++;
                if (Transition(token) is { } withChangedAttribute)
                {
                    ChangeAttribute(withChangedAttribute, payload);
                }
                break;
            case "dom-character-data-changed":
                CharacterDataTransitions++;
                if (Transition(token) is { } withChangedData)
                {
                    SetData(withChangedData, payload, "text", payload.GetProperty("transitionId").GetString()!);
                }
                break;
            case "dom-shadow-root-changed":
                ShadowRootChanges++;
                if (Transition(token) is { } withChangedRoot)
                {
                    SetShadowRoot(withChangedRoot, payload, payload.GetProperty("transitionId").GetString()!);
                }
                break;
            case "dom-slot-assignment-changed":
                SlotAssignmentChanges++;
                if (Transition(token) is { } withChangedSlot)
                {
                    SetAssignment(withChangedSlot, payload, payload.GetProperty("transitionId").GetString()!);
                }
                break;
            case "layout-transform-node":
                AddTranslation(token, payload);
                break;
            case "layout-scroll-offset-changed":
                ScrollOffsets++;
                var changeSet = payload.GetProperty("changeSetId").GetString()!;
                if (!_scrolls.TryGetValue(token + " " + changeSet, out var scrolls))
                {
                    scrolls = [];
                    _scrolls[token + " " + changeSet] = scrolls;
                }
                scrolls.Add(payload.Clone());
                break;
            case "layout-changes-completed":
                CompareScrolls(token, payload.GetProperty("changeSetId").GetString()!);
                break;
        }
    }

    /// <summary>Reports the insertions and checkpoints still open when the
    /// records ended.</summary>
    public void Finish()
    {
        foreach (var insertion in _insertions.Keys)
        {
            Note("insertion-not-completed", insertion);
        }
        _insertions.Clear();
        _open.Clear();
    }

    public string Report()
    {
        var report = new StringBuilder();
        report.AppendLine($"checkpoints compared with the rebuilt tree: {CheckpointsCompared}");
        report.AppendLine($"checkpoints cut, not compared: {CheckpointsTruncated}");
        report.AppendLine($"checkpoints walked after a lost record, not compared: {CheckpointsAfterLoss}");
        report.AppendLine($"checkpoints at a finished parse, not compared: {CheckpointsAtFinishedParse}");
        report.AppendLine($"nodes compared: {NodesCompared}, equal in every field {NodesMatched}");
        report.AppendLine($"insertions: {Insertions}, inserted node records {InsertedNodes}");
        report.AppendLine($"removals: {Removals}");
        report.AppendLine($"removals of all children: {ChildrenRemovals}");
        report.AppendLine($"attribute transitions: {AttributeTransitions}");
        report.AppendLine($"character data transitions: {CharacterDataTransitions}");
        report.AppendLine($"shadow root changes: {ShadowRootChanges}");
        report.AppendLine($"slot assignment changes: {SlotAssignmentChanges}");
        report.AppendLine(
            $"changes to nodes outside the document's tree, not compared: {ChangesOutsideTheTree}");
        report.AppendLine(
            $"scroll offsets: {ScrollOffsets}, compared with their scroll translation {ScrollOffsetsCompared}, equal {ScrollOffsetsMatched}");
        foreach (var (kind, count) in _differences.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            report.AppendLine($"difference {kind}: {count}");
            foreach (var example in _examples[kind])
            {
                report.AppendLine($"  {example}");
            }
        }
        return report.ToString();
    }

    // The document's rebuilt tree, or null before its first completed
    // checkpoint, when there is nothing to change.
    private DocumentTree? Transition(string token) => _documents.GetValueOrDefault(token);

    private static DomNode Node(DocumentTree tree, long id)
    {
        if (!tree.Nodes.TryGetValue(id, out var node))
        {
            node = new DomNode(id);
            tree.Nodes.Add(id, node);
        }
        return node;
    }

    private DomNode? Known(DocumentTree tree, long id, string where)
    {
        if (tree.Nodes.TryGetValue(id, out var node))
        {
            return node;
        }
        Note("node-not-in-tree", $"{where} node {id}");
        return null;
    }

    // A change to a node outside the document's tree: Blink records changes
    // to elements before they are inserted, whose state then arrives with
    // their insertion. It is counted, not reported as a difference.
    private DomNode? InTree(DocumentTree tree, long id)
    {
        if (tree.Nodes.TryGetValue(id, out var node))
        {
            return node;
        }
        ChangesOutsideTheTree++;
        return null;
    }

    // Adds a node record of a checkpoint or of an inserted subtree. The root
    // of an inserted subtree is already placed by its insertion record.
    private void AddNode(DocumentTree tree, JsonElement payload, string? insertionId)
    {
        var id = payload.GetProperty("nodeId").GetInt64();
        var parent = payload.GetProperty("parentNodeId");
        long? parentId = parent.ValueKind == JsonValueKind.Number ? parent.GetInt64() : null;
        var node = Node(tree, id);
        node.NodeType = payload.GetProperty("nodeType").GetString();
        node.NodeName = payload.GetProperty("nodeName").GetString();
        if (insertionId is not null && payload.GetProperty("nodeIndex").GetInt32() == 0)
        {
            if (node.ParentId != parentId)
            {
                Note("inserted-root-parent", $"{insertionId} node {id} parent {parentId}, inserted into {node.ParentId}");
            }
            return;
        }
        node.ParentId = parentId;
        if (parentId is not { } placed)
        {
            return;
        }
        var parentNode = Node(tree, placed);
        if (node.NodeType == "shadow-root")
        {
            parentNode.ShadowRootId = id;
        }
        else
        {
            parentNode.Children.Add(id);
        }
    }

    private static string AttributeKey(JsonElement payload)
    {
        var ns = payload.GetProperty("attributeNamespace");
        var name = payload.GetProperty("attributeName").GetString();
        return ns.ValueKind == JsonValueKind.String && ns.GetString() is { Length: > 0 } space
            ? $"{{{space}}}{name}"
            : name!;
    }

    // A value that was cut is kept as unknown, so it is never counted equal.
    private static string? TextValue(JsonElement payload, string property)
    {
        var value = payload.GetProperty(property);
        var truncated = payload.TryGetProperty(property + "Truncated", out var flag) &&
            flag.ValueKind == JsonValueKind.True;
        return value.ValueKind == JsonValueKind.String && !truncated
            ? value.GetString()
            : "\u0000cut";
    }

    // The node a record names: a transition may name a node outside the
    // tree, a checkpoint or inserted subtree record may not.
    private DomNode? Find(DocumentTree tree, JsonElement payload, string where) =>
        payload.TryGetProperty("transitionId", out _)
            ? InTree(tree, payload.GetProperty("nodeId").GetInt64())
            : Known(tree, payload.GetProperty("nodeId").GetInt64(), where);

    private void SetAttribute(DocumentTree tree, JsonElement payload, string where)
    {
        if (Known(tree, payload.GetProperty("nodeId").GetInt64(), where) is { } node)
        {
            node.Attributes[AttributeKey(payload)] = TextValue(payload, "attributeValue");
        }
    }

    private void ChangeAttribute(DocumentTree tree, JsonElement payload)
    {
        var where = payload.GetProperty("transitionId").GetString()!;
        if (InTree(tree, payload.GetProperty("nodeId").GetInt64()) is not { } node)
        {
            return;
        }
        var key = AttributeKey(payload);
        if (payload.GetProperty("changeType").GetString() == "removed")
        {
            if (!node.Attributes.Remove(key))
            {
                Note("removed-attribute-not-present", $"{where} node {node.Id} {key}");
            }
            return;
        }
        node.Attributes[key] = TextValue(payload, "attributeValue");
    }

    private void SetData(DocumentTree tree, JsonElement payload, string property, string where)
    {
        if (Find(tree, payload, where) is { } node)
        {
            node.Data = TextValue(payload, property);
        }
    }

    private static readonly string[] ShadowRootProperties =
    [
        "hostNodeId", "mode", "delegatesFocus", "slotAssignment", "clonable", "serializable",
        "declarative", "availableToElementInternals", "referenceTarget",
    ];

    private void SetShadowRoot(DocumentTree tree, JsonElement payload, string where)
    {
        if (Find(tree, payload, where) is { } node)
        {
            node.ShadowRootFields = string.Join(
                ",",
                ShadowRootProperties.Select(property => payload.GetProperty(property).GetRawText()));
        }
    }

    private void SetAssignment(DocumentTree tree, JsonElement payload, string where)
    {
        if (Find(tree, payload, where) is { } node)
        {
            node.AssignedNodes = payload.GetProperty("assignedNodesTruncated").GetBoolean()
                ? "\u0000cut"
                : payload.GetProperty("assignedNodeIds").GetRawText();
        }
    }

    // Removes a node's subtree, and its shadow tree, from the rebuilt tree.
    private static void Discard(DocumentTree tree, long id)
    {
        var pending = new Stack<long>();
        pending.Push(id);
        while (pending.Count > 0)
        {
            if (!tree.Nodes.Remove(pending.Pop(), out var node))
            {
                continue;
            }
            foreach (var child in node.Children)
            {
                pending.Push(child);
            }
            if (node.ShadowRootId is { } root)
            {
                pending.Push(root);
            }
        }
    }

    private void Detach(DocumentTree tree, DomNode node, string where)
    {
        if (node.ParentId is { } parentId && tree.Nodes.TryGetValue(parentId, out var parent))
        {
            if (parent.ShadowRootId == node.Id)
            {
                parent.ShadowRootId = null;
            }
            else
            {
                parent.Children.Remove(node.Id);
            }
            Note("inserted-node-still-attached", $"{where} node {node.Id} was a child of {parentId}");
        }
    }

    private void StartInsertion(string token, JsonElement payload)
    {
        Insertions++;
        var id = payload.GetProperty("transitionId").GetString()!;
        if (Transition(token) is not { } tree)
        {
            return;
        }
        var containerId = payload.GetProperty("containerNodeId").GetInt64();
        var nodeId = payload.GetProperty("nodeId").GetInt64();
        if (Known(tree, containerId, id) is not { } container)
        {
            return;
        }
        if (tree.Nodes.TryGetValue(nodeId, out var existing))
        {
            Detach(tree, existing, id);
            Discard(tree, nodeId);
        }
        var node = Node(tree, nodeId);
        node.ParentId = containerId;
        if (payload.GetProperty("insertionKind").GetString() == "shadow-root")
        {
            container.ShadowRootId = nodeId;
        }
        else
        {
            var previous = payload.GetProperty("previousSiblingNodeId");
            var index = 0;
            if (previous.ValueKind == JsonValueKind.Number)
            {
                index = container.Children.IndexOf(previous.GetInt64()) + 1;
                if (index == 0)
                {
                    Note("previous-sibling-not-a-child", $"{id} node {previous.GetInt64()} in {containerId}");
                    index = container.Children.Count;
                }
            }
            container.Children.Insert(index, nodeId);
        }
        _insertions[token + " " + id] = new Insertion(id, tree);
    }

    // Transition numbers are counted in each renderer process, so an
    // insertion is found by its document and its identity.
    private Insertion? FindInsertion(string token, JsonElement payload) =>
        _insertions.GetValueOrDefault(token + " " + payload.GetProperty("insertionId").GetString()!);

    private void CompleteInsertion(string token, JsonElement payload)
    {
        var id = payload.GetProperty("insertionId").GetString()!;
        if (!_insertions.Remove(token + " " + id, out var insertion))
        {
            return;
        }
        var stated = (
            payload.GetProperty("nodeCount").GetInt32(),
            payload.GetProperty("attributeCount").GetInt32(),
            payload.GetProperty("characterDataCount").GetInt32(),
            payload.GetProperty("shadowRootCount").GetInt32(),
            payload.GetProperty("slotCount").GetInt32());
        var recorded = (insertion.Nodes, insertion.Attributes, insertion.CharacterData,
            insertion.ShadowRoots, insertion.Slots);
        if (stated != recorded)
        {
            Note("insertion-count", $"{id} states {stated}, recorded {recorded}");
        }
    }

    private void Remove(DocumentTree tree, JsonElement payload)
    {
        var where = payload.GetProperty("transitionId").GetString()!;
        var containerId = payload.GetProperty("containerNodeId").GetInt64();
        var nodeId = payload.GetProperty("nodeId").GetInt64();
        if (Known(tree, containerId, where) is not { } container)
        {
            return;
        }
        if (!container.Children.Remove(nodeId))
        {
            Note("removed-node-not-a-child", $"{where} node {nodeId} of {containerId}");
        }
        Discard(tree, nodeId);
    }

    private void RemoveChildren(DocumentTree tree, JsonElement payload)
    {
        var where = payload.GetProperty("transitionId").GetString()!;
        if (Known(tree, payload.GetProperty("containerNodeId").GetInt64(), where) is not { } container)
        {
            return;
        }
        foreach (var child in container.Children)
        {
            Discard(tree, child);
        }
        container.Children.Clear();
    }

    private void CompleteCheckpoint(string token, CheckpointTree checkpoint, JsonElement payload)
    {
        if (payload.GetProperty("truncated").GetBoolean() ||
            (payload.TryGetProperty("attributesTruncated", out var attributes) &&
                attributes.ValueKind == JsonValueKind.True))
        {
            CheckpointsTruncated++;
            _documents.Remove(token);
            return;
        }
        if (checkpoint.AfterLoss)
        {
            CheckpointsAfterLoss++;
        }
        else if (checkpoint.FinishedParsing)
        {
            CheckpointsAtFinishedParse++;
        }
        else if (_documents.TryGetValue(token, out var rebuilt))
        {
            Compare(checkpoint, rebuilt);
        }
        _documents[token] = checkpoint.Tree;
    }

    private void Compare(CheckpointTree checkpoint, DocumentTree rebuilt)
    {
        CheckpointsCompared++;
        foreach (var observed in checkpoint.Tree.Nodes.Values)
        {
            NodesCompared++;
            if (!rebuilt.Nodes.TryGetValue(observed.Id, out var node))
            {
                Note("node-missing", $"{checkpoint.Id} node {observed.Id} {observed.NodeName}");
                continue;
            }
            var equal = true;
            void Differ(string field, object? expected, object? actual)
            {
                equal = false;
                Note(field, $"{checkpoint.Id} node {observed.Id} {observed.NodeName}: checkpoint {expected}, rebuilt {actual}");
            }
            if (node.ParentId != observed.ParentId)
            {
                Differ("parent", observed.ParentId, node.ParentId);
            }
            if (!node.Children.SequenceEqual(observed.Children))
            {
                Differ("children", string.Join(" ", observed.Children), string.Join(" ", node.Children));
            }
            if (node.ShadowRootId != observed.ShadowRootId)
            {
                Differ("shadow-root", observed.ShadowRootId, node.ShadowRootId);
            }
            if (node.NodeType != observed.NodeType || node.NodeName != observed.NodeName)
            {
                Differ("type-or-name", $"{observed.NodeType} {observed.NodeName}", $"{node.NodeType} {node.NodeName}");
            }
            if (!node.Attributes.SequenceEqual(observed.Attributes))
            {
                Differ("attributes", Describe(observed.Attributes), Describe(node.Attributes));
            }
            if (node.Data != observed.Data)
            {
                Differ("character-data", Shorten(observed.Data), Shorten(node.Data));
            }
            if (node.ShadowRootFields != observed.ShadowRootFields)
            {
                Differ("shadow-root-fields", observed.ShadowRootFields, node.ShadowRootFields);
            }
            if (node.AssignedNodes != observed.AssignedNodes)
            {
                Differ("slot-assignment", observed.AssignedNodes, node.AssignedNodes);
            }
            if (equal)
            {
                NodesMatched++;
            }
        }
        foreach (var node in rebuilt.Nodes.Values)
        {
            if (!checkpoint.Tree.Nodes.ContainsKey(node.Id))
            {
                Note("node-extra", $"{checkpoint.Id} node {node.Id} {node.NodeName}");
            }
        }
    }

    private static string Describe(SortedDictionary<string, string?> attributes) =>
        string.Join(" ", attributes.Select(entry => $"{entry.Key}={Shorten(entry.Value)}"));

    private static string? Shorten(string? value) =>
        value is { Length: > 60 } ? value[..60] + "..." : value;

    private void AddTranslation(string token, JsonElement payload)
    {
        var id = payload.GetProperty("transformNodeId").GetString()!;
        var matrix = payload.GetProperty("matrix");
        _translations[token + " " + id] = (matrix[12].GetDouble(), matrix[13].GetDouble());
    }

    private void CompareScrolls(string token, string changeSet)
    {
        if (!_scrolls.Remove(token + " " + changeSet, out var scrolls))
        {
            return;
        }
        foreach (var scroll in scrolls)
        {
            var translation = scroll.GetProperty("scrollTranslationNodeId");
            if (translation.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var node = scroll.GetProperty("nodeId").GetInt64();
            if (!_translations.TryGetValue(token + " " + translation.GetString(), out var recorded))
            {
                Note("scroll-translation-not-recorded", $"{changeSet} node {node} {translation.GetString()}");
                continue;
            }
            ScrollOffsetsCompared++;
            var offset = scroll.GetProperty("scrollOffset");
            var origin = scroll.GetProperty("scrollOrigin");
            var x = origin.GetProperty("x").GetDouble() + offset.GetProperty("x").GetDouble();
            var y = origin.GetProperty("y").GetDouble() + offset.GetProperty("y").GetDouble();
            if (Math.Abs(x + recorded.X) <= ScrollTolerance && Math.Abs(y + recorded.Y) <= ScrollTolerance)
            {
                ScrollOffsetsMatched++;
            }
            else
            {
                Note(
                    "scroll-translation",
                    $"{changeSet} node {node}: position ({x}, {y}), translation ({recorded.X}, {recorded.Y})");
            }
        }
    }

    private void Note(string kind, string example)
    {
        _differences[kind] = _differences.GetValueOrDefault(kind) + 1;
        if (!_examples.TryGetValue(kind, out var examples))
        {
            examples = [];
            _examples[kind] = examples;
        }
        if (examples.Count < MaximumExamples)
        {
            examples.Add(example);
        }
    }
}

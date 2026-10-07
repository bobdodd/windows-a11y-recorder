using System.Text.Json;

namespace Recorder.Session;

/// <summary>A node of a document's rebuilt DOM tree.</summary>
public sealed class DomNode(long id)
{
    public long Id { get; } = id;
    public long? ParentId { get; internal set; }
    public string? NodeType { get; internal set; }
    public string? NodeName { get; internal set; }
    public List<long> Children { get; } = [];
    public long? ShadowRootId { get; internal set; }

    /// <summary>
    /// Attributes by name, or by "{namespace}name" for a namespaced
    /// attribute. A value that was cut is <see cref="DomTreeRebuilder.Cut"/>.
    /// </summary>
    public SortedDictionary<string, string?> Attributes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The character data, <see cref="DomTreeRebuilder.Cut"/> when it was
    /// cut, or null for a node with none recorded.
    /// </summary>
    public string? Data { get; internal set; }

    /// <summary>The shadow root's recorded fields, as JSON values joined by commas.</summary>
    public string? ShadowRootFields { get; internal set; }

    /// <summary>The slot's assigned node identities as a JSON array, or <see cref="DomTreeRebuilder.Cut"/>.</summary>
    public string? AssignedNodes { get; internal set; }
}

/// <summary>A document's rebuilt DOM tree: its nodes by identity.</summary>
public sealed class DomDocumentTree
{
    public Dictionary<long, DomNode> Nodes { get; } = [];
}

/// <summary>A DOM checkpoint of one document, as its records were read.</summary>
public sealed class DomCheckpointTree(string id, bool afterLoss, bool finishedParsing, bool startedParsing = false)
{
    public string Id { get; } = id;

    /// <summary>The walk was made after a lost DOM record (walk reason "after-loss").</summary>
    public bool AfterLoss { get; } = afterLoss;

    /// <summary>The walk was requested at a finished parse.</summary>
    public bool FinishedParsing { get; } = finishedParsing;

    /// <summary>
    /// The walk was requested when the document's parser was created
    /// (protocol 0.42), so the parser's changes after it are recorded.
    /// </summary>
    public bool StartedParsing { get; } = startedParsing;

    public DomDocumentTree Tree { get; } = new();
}

/// <summary>
/// Rebuilds the DOM tree of each document from its DOM records (protocol
/// 0.34), in record order. A document's tree is taken from each completed
/// checkpoint and changed by every DOM transition after it: insertions with
/// their subtrees, removals, removals of all children, attribute and
/// character data changes, shadow root changes, and slot assignments. A
/// checkpoint that was cut leaves the document with no tree until its next
/// checkpoint. A document is keyed by its token and its document identity;
/// see <see cref="DocumentKey"/>. Records that do not fit the tree are
/// stated through <see cref="Noted"/>, and are not otherwise acted on.
/// </summary>
public sealed class DomTreeRebuilder
{
    /// <summary>The value kept for a value that was cut, so it is never equal to a recorded one.</summary>
    public const string Cut = "\u0000cut";

    private static readonly string[] ShadowRootProperties =
    [
        "hostNodeId", "mode", "delegatesFocus", "slotAssignment", "clonable", "serializable",
        "declarative", "availableToElementInternals", "referenceTarget",
    ];

    private readonly Dictionary<string, DomDocumentTree> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DomCheckpointTree> _open = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Insertion> _insertions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _openInsertions = new(StringComparer.Ordinal);

    /// <summary>An insertion whose subtree records are being read.</summary>
    public sealed class Insertion(string id, DomDocumentTree tree)
    {
        public string Id { get; } = id;
        public DomDocumentTree Tree { get; } = tree;
        public int Nodes { get; set; }
        public int Attributes { get; set; }
        public int CharacterData { get; set; }
        public int ShadowRoots { get; set; }
        public int Slots { get; set; }
    }

    /// <summary>Called with the kind of a record that does not fit the tree, and an example.</summary>
    public Action<string, string>? Noted { get; set; }

    /// <summary>
    /// Called when a document's checkpoint completes, before its tree is
    /// taken: with the document key, the checkpoint, the tree rebuilt before
    /// it or null, and whether the checkpoint was cut.
    /// </summary>
    public Action<string, DomCheckpointTree, DomDocumentTree?, bool>? CheckpointCompleted { get; set; }

    /// <summary>
    /// Called when an insertion completes, with the completion record and the
    /// insertion's counted records.
    /// </summary>
    public Action<JsonElement, Insertion>? InsertionCompleted { get; set; }

    /// <summary>
    /// Changes to nodes outside the document's tree: Blink records changes to
    /// elements before they are inserted, whose state then arrives with their
    /// insertion.
    /// </summary>
    public int ChangesOutsideTheTree { get; private set; }

    /// <summary>The inserted node records that belonged to an open insertion.</summary>
    public int InsertedNodeRecords { get; private set; }

    /// <summary>The rebuilt tree of each document with a completed checkpoint, by document key.</summary>
    public IReadOnlyDictionary<string, DomDocumentTree> Documents => _documents;

    /// <summary>
    /// A document is keyed by its token and its document identity: in the
    /// recording at revision 70d22d4, two documents of one process, with
    /// different document identities, were recorded under one token 3 ms
    /// apart. Null for a record without a document token.
    /// </summary>
    public static string? DocumentKey(JsonElement payload)
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

    /// <summary>
    /// True while a checkpoint or an insertion of the document has started
    /// and not completed, when its tree is between two records of one
    /// change.
    /// </summary>
    public bool IsOpen(string key) =>
        _open.ContainsKey(key) || _openInsertions.GetValueOrDefault(key) > 0;

    /// <summary>Sets a document's tree, as read from a snapshot.</summary>
    public void SetDocument(string key, DomDocumentTree tree) => _documents[key] = tree;

    /// <summary>Forgets every document and every open change.</summary>
    public void Clear()
    {
        _documents.Clear();
        _open.Clear();
        _insertions.Clear();
        _openInsertions.Clear();
    }

    /// <summary>Forgets a document that is not open, as when its state is kept only as a snapshot.</summary>
    public void Remove(string key)
    {
        if (IsOpen(key))
        {
            throw new InvalidOperationException($"Document {key} is part way through a change.");
        }
        _documents.Remove(key);
        _openInsertions.Remove(key);
    }

    /// <summary>
    /// Applies one browser.dom record. Records of other types are ignored.
    /// Returns the record's document key, or null when it has none.
    /// </summary>
    public string? Apply(string eventType, JsonElement payload)
    {
        if (!eventType.StartsWith("dom-", StringComparison.Ordinal))
        {
            return null;
        }
        var key = DocumentKey(payload);
        if (key is null)
        {
            return null;
        }
        switch (eventType)
        {
            case "dom-checkpoint-started":
                _open[key] = new DomCheckpointTree(
                    payload.GetProperty("checkpointId").GetString()!,
                    payload.TryGetProperty("walkReason", out var walkReason) &&
                        walkReason.ValueKind == JsonValueKind.String &&
                        walkReason.GetString() == "after-loss",
                    payload.TryGetProperty("reason", out var reason) &&
                        reason.ValueKind == JsonValueKind.String &&
                        reason.GetString() == "finished-parsing",
                    reason.ValueKind == JsonValueKind.String &&
                        reason.GetString() == "started-parsing");
                break;
            case "dom-checkpoint-node":
                if (_open.TryGetValue(key, out var open))
                {
                    AddNode(open.Tree, payload, null);
                }
                break;
            case "dom-checkpoint-node-attribute":
                if (_open.TryGetValue(key, out open))
                {
                    SetAttribute(open.Tree, payload, open.Id);
                }
                break;
            case "dom-checkpoint-node-character-data":
                if (_open.TryGetValue(key, out open))
                {
                    SetData(open.Tree, payload, "data", open.Id);
                }
                break;
            case "dom-checkpoint-shadow-root":
                if (_open.TryGetValue(key, out open))
                {
                    SetShadowRoot(open.Tree, payload, open.Id);
                }
                break;
            case "dom-checkpoint-slot-assignment":
                if (_open.TryGetValue(key, out open))
                {
                    SetAssignment(open.Tree, payload, open.Id);
                }
                break;
            case "dom-checkpoint-completed":
                if (_open.Remove(key, out open))
                {
                    CompleteCheckpoint(key, open, payload);
                }
                break;
            case "dom-node-inserted":
                StartInsertion(key, payload);
                break;
            case "dom-inserted-node":
                if (FindInsertion(key, payload) is { } insertion)
                {
                    insertion.Nodes++;
                    InsertedNodeRecords++;
                    AddNode(insertion.Tree, payload, insertion.Id);
                }
                break;
            case "dom-inserted-node-attribute":
                if (FindInsertion(key, payload) is { } withAttribute)
                {
                    withAttribute.Attributes++;
                    SetAttribute(withAttribute.Tree, payload, withAttribute.Id);
                }
                break;
            case "dom-inserted-node-character-data":
                if (FindInsertion(key, payload) is { } withData)
                {
                    withData.CharacterData++;
                    SetData(withData.Tree, payload, "data", withData.Id);
                }
                break;
            case "dom-inserted-shadow-root":
                if (FindInsertion(key, payload) is { } withShadowRoot)
                {
                    withShadowRoot.ShadowRoots++;
                    SetShadowRoot(withShadowRoot.Tree, payload, withShadowRoot.Id);
                }
                break;
            case "dom-inserted-slot-assignment":
                if (FindInsertion(key, payload) is { } withSlot)
                {
                    withSlot.Slots++;
                    SetAssignment(withSlot.Tree, payload, withSlot.Id);
                }
                break;
            case "dom-insertion-completed":
                CompleteInsertion(key, payload);
                break;
            case "dom-node-removed":
                if (Transition(key) is { } removedFrom)
                {
                    Remove(removedFrom, payload);
                }
                break;
            case "dom-children-removed":
                if (Transition(key) is { } emptied)
                {
                    RemoveChildren(emptied, payload);
                }
                break;
            case "dom-attribute-changed":
                if (Transition(key) is { } withChangedAttribute)
                {
                    ChangeAttribute(withChangedAttribute, payload);
                }
                break;
            case "dom-character-data-changed":
                if (Transition(key) is { } withChangedData)
                {
                    SetData(withChangedData, payload, "text", payload.GetProperty("transitionId").GetString()!);
                }
                break;
            case "dom-shadow-root-changed":
                if (Transition(key) is { } withChangedRoot)
                {
                    SetShadowRoot(withChangedRoot, payload, payload.GetProperty("transitionId").GetString()!);
                }
                break;
            case "dom-slot-assignment-changed":
                if (Transition(key) is { } withChangedSlot)
                {
                    SetAssignment(withChangedSlot, payload, payload.GetProperty("transitionId").GetString()!);
                }
                break;
        }
        return key;
    }

    /// <summary>Notes the insertions still open when the records ended, and forgets them and the open checkpoints.</summary>
    public void Finish()
    {
        foreach (var insertion in _insertions.Keys)
        {
            Note("insertion-not-completed", insertion);
        }
        _insertions.Clear();
        _openInsertions.Clear();
        _open.Clear();
    }

    // The document's rebuilt tree, or null before its first completed
    // checkpoint, when there is nothing to change.
    private DomDocumentTree? Transition(string key) => _documents.GetValueOrDefault(key);

    private static DomNode Node(DomDocumentTree tree, long id)
    {
        if (!tree.Nodes.TryGetValue(id, out var node))
        {
            node = new DomNode(id);
            tree.Nodes.Add(id, node);
        }
        return node;
    }

    private DomNode? Known(DomDocumentTree tree, long id, string where)
    {
        if (tree.Nodes.TryGetValue(id, out var node))
        {
            return node;
        }
        Note("node-not-in-tree", $"{where} node {id}");
        return null;
    }

    // A change to a node outside the document's tree is counted, not noted.
    private DomNode? InTree(DomDocumentTree tree, long id)
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
    private void AddNode(DomDocumentTree tree, JsonElement payload, string? insertionId)
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
            : Cut;
    }

    // The node a record names: a transition may name a node outside the
    // tree, a checkpoint or inserted subtree record may not.
    private DomNode? Find(DomDocumentTree tree, JsonElement payload, string where) =>
        payload.TryGetProperty("transitionId", out _)
            ? InTree(tree, payload.GetProperty("nodeId").GetInt64())
            : Known(tree, payload.GetProperty("nodeId").GetInt64(), where);

    private void SetAttribute(DomDocumentTree tree, JsonElement payload, string where)
    {
        if (Known(tree, payload.GetProperty("nodeId").GetInt64(), where) is { } node)
        {
            node.Attributes[AttributeKey(payload)] = TextValue(payload, "attributeValue");
        }
    }

    private void ChangeAttribute(DomDocumentTree tree, JsonElement payload)
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

    private void SetData(DomDocumentTree tree, JsonElement payload, string property, string where)
    {
        if (Find(tree, payload, where) is { } node)
        {
            node.Data = TextValue(payload, property);
        }
    }

    private void SetShadowRoot(DomDocumentTree tree, JsonElement payload, string where)
    {
        if (Find(tree, payload, where) is { } node)
        {
            node.ShadowRootFields = string.Join(
                ",",
                ShadowRootProperties.Select(property => payload.GetProperty(property).GetRawText()));
        }
    }

    private void SetAssignment(DomDocumentTree tree, JsonElement payload, string where)
    {
        if (Find(tree, payload, where) is { } node)
        {
            node.AssignedNodes = payload.GetProperty("assignedNodesTruncated").GetBoolean()
                ? Cut
                : payload.GetProperty("assignedNodeIds").GetRawText();
        }
    }

    // Removes a node's subtree, and its shadow tree, from the rebuilt tree.
    private static void Discard(DomDocumentTree tree, long id)
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

    private void Detach(DomDocumentTree tree, DomNode node, string where)
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

    private void StartInsertion(string key, JsonElement payload)
    {
        var id = payload.GetProperty("transitionId").GetString()!;
        if (Transition(key) is not { } tree)
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
        if (_insertions.TryAdd(key + " " + id, new Insertion(id, tree)))
        {
            _openInsertions[key] = _openInsertions.GetValueOrDefault(key) + 1;
        }
        else
        {
            _insertions[key + " " + id] = new Insertion(id, tree);
        }
    }

    // Transition numbers are counted in each renderer process, so an
    // insertion is found by its document and its identity.
    private Insertion? FindInsertion(string key, JsonElement payload) =>
        _insertions.GetValueOrDefault(key + " " + payload.GetProperty("insertionId").GetString()!);

    private void CompleteInsertion(string key, JsonElement payload)
    {
        var id = payload.GetProperty("insertionId").GetString()!;
        if (!_insertions.Remove(key + " " + id, out var insertion))
        {
            return;
        }
        if (_openInsertions.GetValueOrDefault(key) <= 1)
        {
            _openInsertions.Remove(key);
        }
        else
        {
            _openInsertions[key]--;
        }
        InsertionCompleted?.Invoke(payload, insertion);
    }

    private void Remove(DomDocumentTree tree, JsonElement payload)
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

    private void RemoveChildren(DomDocumentTree tree, JsonElement payload)
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

    private void CompleteCheckpoint(string key, DomCheckpointTree checkpoint, JsonElement payload)
    {
        var truncated = payload.GetProperty("truncated").GetBoolean() ||
            (payload.TryGetProperty("attributesTruncated", out var attributes) &&
                attributes.ValueKind == JsonValueKind.True);
        CheckpointCompleted?.Invoke(key, checkpoint, _documents.GetValueOrDefault(key), truncated);
        if (truncated)
        {
            _documents.Remove(key);
            return;
        }
        _documents[key] = checkpoint.Tree;
    }

    private void Note(string kind, string example) => Noted?.Invoke(kind, example);
}

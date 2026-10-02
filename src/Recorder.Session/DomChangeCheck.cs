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
/// from it. A checkpoint at a finished parse is not compared either, unless
/// the document was walked when its parser was created (protocol 0.42):
/// before, the parser's insertions while a document parses were not
/// recorded, and a document parsed again after <c>document.open()</c>
/// removed its children had only its finished-parsing checkpoint as its
/// state. From protocol 0.42 the parser's changes are recorded from that
/// walk, so the finished-parsing checkpoint is compared with the tree
/// rebuilt from it.
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

    private readonly DomTreeRebuilder _trees = new();
    private readonly Dictionary<string, (double X, double Y)> _translations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<JsonElement>> _scrolls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _differences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _examples = new(StringComparer.Ordinal);
    private readonly HashSet<string> _parsedFromStart = new(StringComparer.Ordinal);

    public DomChangeCheck()
    {
        _trees.Noted = Note;
        _trees.CheckpointCompleted = CompleteCheckpoint;
        _trees.InsertionCompleted = CompleteInsertion;
    }

    public int CheckpointsCompared { get; private set; }
    public int CheckpointsTruncated { get; private set; }
    public int CheckpointsAfterLoss { get; private set; }
    public int CheckpointsAtFinishedParse { get; private set; }
    public int NodesCompared { get; private set; }
    public int NodesMatched { get; private set; }
    public int Insertions { get; private set; }
    public int InsertedNodes => _trees.InsertedNodeRecords;
    public int Removals { get; private set; }
    public int ChildrenRemovals { get; private set; }
    public int AttributeTransitions { get; private set; }
    public int CharacterDataTransitions { get; private set; }
    public int ShadowRootChanges { get; private set; }
    public int SlotAssignmentChanges { get; private set; }
    public int ChangesOutsideTheTree => _trees.ChangesOutsideTheTree;
    public int ScrollOffsets { get; private set; }
    public int ScrollOffsetsCompared { get; private set; }
    public int ScrollOffsetsMatched { get; private set; }

    /// <summary>The number of differences of each kind.</summary>
    public IReadOnlyDictionary<string, int> Differences => _differences;

    /// <summary>Applies one browser.dom or browser.layout record, in record
    /// order.</summary>
    public void Add(string eventType, JsonElement payload)
    {
        var token = _trees.Apply(eventType, payload) ?? DomTreeRebuilder.DocumentKey(payload);
        if (token is null)
        {
            return;
        }
        switch (eventType)
        {
            case "dom-node-inserted":
                Insertions++;
                break;
            case "dom-node-removed":
                Removals++;
                break;
            case "dom-children-removed":
                ChildrenRemovals++;
                break;
            case "dom-attribute-changed":
                AttributeTransitions++;
                break;
            case "dom-character-data-changed":
                CharacterDataTransitions++;
                break;
            case "dom-shadow-root-changed":
                ShadowRootChanges++;
                break;
            case "dom-slot-assignment-changed":
                SlotAssignmentChanges++;
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
    public void Finish() => _trees.Finish();

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

    private void CompleteInsertion(JsonElement payload, DomTreeRebuilder.Insertion insertion)
    {
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
            Note("insertion-count", $"{insertion.Id} states {stated}, recorded {recorded}");
        }
    }

    private void CompleteCheckpoint(string key, DomCheckpointTree checkpoint, DomDocumentTree? rebuilt, bool truncated)
    {
        var parsedFromStart = _parsedFromStart.Remove(key);
        if (truncated)
        {
            CheckpointsTruncated++;
            parsedFromStart = false;
        }
        else if (checkpoint.AfterLoss)
        {
            CheckpointsAfterLoss++;
        }
        else if (checkpoint.FinishedParsing && !(parsedFromStart && rebuilt is not null))
        {
            CheckpointsAtFinishedParse++;
        }
        else if (rebuilt is not null)
        {
            Compare(checkpoint, rebuilt);
        }
        // The parser's changes are recorded from a walk when the parser was
        // created until the parse finishes.
        if (!truncated && (checkpoint.StartedParsing || (parsedFromStart && !checkpoint.FinishedParsing)))
        {
            _parsedFromStart.Add(key);
        }
    }

    private void Compare(DomCheckpointTree checkpoint, DomDocumentTree rebuilt)
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

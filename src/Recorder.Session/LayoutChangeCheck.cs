using System.Text;
using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// Checks the layout change records of a recording against its layout
/// checkpoints (protocol 0.32). The state rebuilt from the change records is
/// compared, at every checkpoint, with each node the checkpoint recorded: the
/// computed style, whether the node has a layout object, and the client
/// rectangle derived from the change records against the checkpoint's
/// observed one. A checkpoint is compared with the state after the change set
/// of the same rendering update, which names it; a checkpoint whose update
/// recorded no change set is compared with the state before the document's
/// next record. The state is rebuilt from change records alone, so once a
/// document's layout is walked after a lost record (protocol 0.35, walk
/// reason "after-loss"), its later checkpoints are counted and not compared.
/// </summary>
public sealed class LayoutChangeCheck
{
    /// <summary>The largest difference, in CSS pixels, between a derived and
    /// an observed rectangle edge that is counted as equal.</summary>
    public const double RectTolerance = 0.05;

    private const int MaximumExamples = 20;

    private readonly LayoutChangeState _state = new();
    private readonly Dictionary<string, Checkpoint> _open = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Checkpoint> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _openChangeSets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _afterLoss = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _differences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _examples = new(StringComparer.Ordinal);

    private sealed class Checkpoint(string id)
    {
        public string Id { get; } = id;
        public List<JsonElement> Nodes { get; } = [];
    }

    public int CheckpointsCompared { get; private set; }
    public int CheckpointsAfterLoss { get; private set; }

    /// <summary>Checkpoints whose change set had not completed when the
    /// records ended, so the state after it is unknown and they are not
    /// compared.</summary>
    public int CheckpointsWithIncompleteChangeSets { get; private set; }

    /// <summary>Checkpoint nodes with no change record, no layout object, and
    /// no computed style: the state a node without a change record has.</summary>
    public int NodesMatchingWithoutRecord { get; private set; }
    public int NodesLockedWithoutRecord { get; private set; }
    public int NodesCompared { get; private set; }
    public int NodesMatched { get; private set; }
    public int RectsCompared { get; private set; }

    /// <summary>The checkpoint nodes whose box fragments were compared (protocol 0.38).</summary>
    public int BoxFragmentsCompared { get; private set; }
    public double LargestRectDifference { get; private set; }
    public int ChangeSets { get; private set; }
    public int ChangedNodeRecords { get; private set; }
    public int TransformNodeRecords { get; private set; }

    /// <summary>The number of differences of each kind.</summary>
    public IReadOnlyDictionary<string, int> Differences => _differences;

    /// <summary>The state rebuilt from the change records.</summary>
    public LayoutChangeState State => _state;

    /// <summary>Applies one browser.layout record, in record order.</summary>
    public void Add(string eventType, JsonElement payload)
    {
        var token = LayoutChangeState.DocumentToken(payload);
        if (token is null)
        {
            return;
        }
        switch (eventType)
        {
            case "layout-checkpoint-started":
                ComparePending(token);
                if (payload.TryGetProperty("walkReason", out var walkReason) &&
                    walkReason.ValueKind == JsonValueKind.String &&
                    walkReason.GetString() == "after-loss")
                {
                    _afterLoss.Add(token);
                }
                _open[token] = new Checkpoint(payload.GetProperty("checkpointId").GetString()!);
                break;
            case "layout-checkpoint-node":
                if (_open.TryGetValue(token, out var open))
                {
                    open.Nodes.Add(payload.Clone());
                }
                break;
            case "layout-checkpoint-completed":
                if (_open.Remove(token, out var completed))
                {
                    _pending[token] = completed;
                }
                break;
            case "layout-changes-started":
                ChangeSets++;
                var named = payload.GetProperty("layoutCheckpointId");
                var namedId = named.ValueKind == JsonValueKind.String ? named.GetString() : null;
                if (!_pending.TryGetValue(token, out var pending) || pending.Id != namedId)
                {
                    ComparePending(token);
                }
                if (namedId is not null)
                {
                    _openChangeSets[token] = namedId;
                }
                _state.Apply(eventType, payload);
                break;
            case "layout-transform-node":
                TransformNodeRecords++;
                _state.Apply(eventType, payload);
                break;
            case "layout-node-changed":
                ChangedNodeRecords++;
                _state.Apply(eventType, payload);
                break;
            case "layout-changes-completed":
                _state.Apply(eventType, payload);
                if (_openChangeSets.Remove(token))
                {
                    ComparePending(token);
                }
                break;
        }
    }

    /// <summary>Compares every checkpoint not yet compared.</summary>
    public void Finish()
    {
        foreach (var token in _pending.Keys.ToList())
        {
            if (_openChangeSets.TryGetValue(token, out var named) && _pending[token].Id == named)
            {
                _pending.Remove(token);
                CheckpointsWithIncompleteChangeSets++;
                continue;
            }
            ComparePending(token);
        }
    }

    /// <summary>A plain-text report of the comparison.</summary>
    public string Report()
    {
        var report = new StringBuilder();
        report.AppendLine($"change sets: {ChangeSets}");
        report.AppendLine($"changed node records: {ChangedNodeRecords}");
        report.AppendLine($"transform node records: {TransformNodeRecords}");
        report.AppendLine($"checkpoints compared: {CheckpointsCompared}");
        report.AppendLine($"checkpoints at or after a walk after a lost record, not compared: {CheckpointsAfterLoss}");
        report.AppendLine(
            $"checkpoints not compared, their change set incomplete at the end: {CheckpointsWithIncompleteChangeSets}");
        report.AppendLine($"checkpoint nodes compared: {NodesCompared}");
        report.AppendLine($"checkpoint nodes matching in every field: {NodesMatched}");
        report.AppendLine(
            $"  of which without a change record, layout object, or style: {NodesMatchingWithoutRecord}");
        report.AppendLine(
            $"checkpoint nodes under a display lock, without a change record, layout object, or style, not compared: {NodesLockedWithoutRecord}");
        report.AppendLine($"rectangles compared: {RectsCompared}");
        report.AppendLine($"box fragments compared: {BoxFragmentsCompared}");
        report.AppendLine($"largest rectangle edge difference: {LargestRectDifference:G6} CSS px");
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

    private void ComparePending(string token)
    {
        if (!_pending.Remove(token, out var checkpoint))
        {
            return;
        }
        if (_afterLoss.Contains(token))
        {
            CheckpointsAfterLoss++;
            return;
        }
        CheckpointsCompared++;
        _state.Documents.TryGetValue(token, out var document);
        foreach (var node in checkpoint.Nodes)
        {
            // A node under a display lock that Blink never styled, such as
            // the content of a closed details element, is never noted. Its
            // checkpoint record states the lock, which only a change record
            // of a styled node states, so it is counted apart and not
            // compared.
            if (node.GetProperty("displayLocked").GetBoolean() &&
                !node.GetProperty("layoutObjectPresent").GetBoolean() &&
                node.GetProperty("computedStyle").ValueKind == JsonValueKind.Null &&
                (!_state.Documents.TryGetValue(token, out var locked) ||
                    !locked.Nodes.ContainsKey(node.GetProperty("nodeId").GetInt64())))
            {
                NodesLockedWithoutRecord++;
                continue;
            }
            NodesCompared++;
            if (CompareNode(checkpoint.Id, document, node))
            {
                NodesMatched++;
            }
        }
    }

    private bool CompareNode(string checkpointId, LayoutDocumentChangeState? document, JsonElement node)
    {
        var nodeId = node.GetProperty("nodeId").GetInt64();
        var where = $"{checkpointId} node {nodeId} {node.GetProperty("nodeName").GetString()}";
        if (document is null || !document.Nodes.TryGetValue(nodeId, out var changed))
        {
            // A node Blink never styled, such as one in a display: none
            // subtree, is never noted; its checkpoint record states no layout
            // object and no computed style, which is what no change record
            // states.
            if (!node.GetProperty("layoutObjectPresent").GetBoolean() &&
                node.GetProperty("computedStyle").ValueKind == JsonValueKind.Null &&
                !node.GetProperty("displayLocked").GetBoolean())
            {
                NodesMatchingWithoutRecord++;
                return true;
            }

            Note("node-not-recorded", where);
            return false;
        }
        var matched = true;
        if (node.GetProperty("layoutObjectPresent").GetBoolean() !=
            changed.GetProperty("layoutObjectPresent").GetBoolean())
        {
            Note("layout-object-presence", where);
            matched = false;
        }
        if (node.GetProperty("displayLocked").GetBoolean() !=
            changed.GetProperty("displayLocked").GetBoolean())
        {
            Note("display-locked", where);
            matched = false;
        }
        var property = StyleDifference(node.GetProperty("computedStyle"), changed.GetProperty("computedStyle"));
        if (property is not null)
        {
            Note("computed-style", $"{where} {property}");
            matched = false;
        }
        // Protocol 0.37: the custom properties, and whether the rebuilt style
        // is whole. A checkpoint of an earlier version states neither.
        if (node.TryGetProperty("customProperties", out var observedCustom))
        {
            var custom = StyleDifference(
                observedCustom,
                changed.TryGetProperty("customProperties", out var changedCustom)
                    ? changedCustom
                    : default);
            if (custom is not null)
            {
                Note("custom-properties", $"{where} {custom}");
                matched = false;
            }
        }
        if (changed.TryGetProperty("computedStyleComplete", out var complete) &&
            complete.ValueKind == JsonValueKind.False)
        {
            Note("computed-style-incomplete", where);
            matched = false;
        }
        // Protocol 0.38: the box fragments, exactly, since the checkpoint and
        // the change record read the same fragments. A checkpoint of an
        // earlier version states none.
        if (node.TryGetProperty("boxFragments", out var observedFragments))
        {
            BoxFragmentsCompared++;
            if (!changed.TryGetProperty("boxFragments", out var changedFragments) ||
                !JsonElement.DeepEquals(observedFragments, changedFragments))
            {
                Note("box-fragments", where);
                matched = false;
            }
        }
        if (node.GetProperty("boundingClientRect") is { ValueKind: JsonValueKind.Object } observed)
        {
            var derived = document.DeriveClientRect(nodeId, out var failure);
            if (derived is null)
            {
                Note("rect-not-derived-" + Kebab(failure.ToString()), where);
                matched = false;
            }
            else
            {
                RectsCompared++;
                var value = derived.Value;
                var difference = new[]
                {
                    Math.Abs(value.X - observed.GetProperty("x").GetDouble()),
                    Math.Abs(value.Y - observed.GetProperty("y").GetDouble()),
                    Math.Abs(value.Width - observed.GetProperty("width").GetDouble()),
                    Math.Abs(value.Height - observed.GetProperty("height").GetDouble()),
                }.Max();
                LargestRectDifference = Math.Max(LargestRectDifference, difference);
                if (difference > RectTolerance)
                {
                    Note(
                        "rect",
                        $"{where} derived ({value.X:G6}, {value.Y:G6}, {value.Width:G6}, {value.Height:G6}) " +
                            $"observed ({observed.GetProperty("x").GetDouble():G6}, " +
                            $"{observed.GetProperty("y").GetDouble():G6}, " +
                            $"{observed.GetProperty("width").GetDouble():G6}, " +
                            $"{observed.GetProperty("height").GetDouble():G6})");
                    matched = false;
                }
            }
        }
        return matched;
    }

    // The first property whose value differs, or null when the styles agree.
    private static string? StyleDifference(JsonElement observed, JsonElement changed)
    {
        if (observed.ValueKind != changed.ValueKind)
        {
            return observed.ValueKind == JsonValueKind.Null ? "(style recorded only in change)" : "(style missing in change)";
        }
        if (observed.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var values = changed.EnumerateObject()
            .ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
        foreach (var entry in observed.EnumerateObject())
        {
            if (!values.Remove(entry.Name, out var value) ||
                value.ValueKind != entry.Value.ValueKind ||
                (value.ValueKind == JsonValueKind.String && value.GetString() != entry.Value.GetString()))
            {
                return entry.Name;
            }
        }
        return values.Count == 0 ? null : values.Keys.First();
    }

    private void Note(string kind, string example)
    {
        _differences[kind] = _differences.GetValueOrDefault(kind) + 1;
        if (!_examples.TryGetValue(kind, out var examples))
        {
            examples = [];
            _examples.Add(kind, examples);
        }
        if (examples.Count < MaximumExamples)
        {
            examples.Add(example);
        }
    }

    private static string Kebab(string name)
    {
        var result = new StringBuilder();
        foreach (var character in name)
        {
            if (char.IsUpper(character) && result.Length > 0)
            {
                result.Append('-');
            }
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }
}

using System.Text;
using System.Text.Json;

namespace Recorder.Session;

/// <summary>
/// Checks the character data records of the DOM checkpoints of a recording
/// (protocol 0.33). In a checkpoint, every text, comment, CDATA section, and
/// processing instruction node is followed by exactly one data record for it,
/// no other node has one, and the completion states how many there were. The
/// DOM checkpoint node record names CDATA sections and processing
/// instructions only by node type "other", so an "other" node is counted as
/// character data when it has a record.
///
/// The check also rebuilds each node's data from the checkpoints and the
/// character data transitions between them, and compares each checkpoint's
/// data, and each transition's previous text, with the rebuilt data. Parser
/// updates are not recorded as transitions, so data a parser appended after
/// a checkpoint is counted as differing, and is not by itself a loss.
/// </summary>
public sealed class DomCharacterDataCheck
{
    private const int MaximumExamples = 20;

    private readonly Dictionary<string, Checkpoint> _open = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _differences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _examples = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<long, string>> _rebuilt = new(StringComparer.Ordinal);

    private sealed class Checkpoint(string id)
    {
        public string Id { get; } = id;
        public long? LastNodeId { get; set; }
        public string? LastNodeType { get; set; }
        public bool LastNodeHasData { get; set; }
        public int DataRecords { get; set; }
    }

    public int CheckpointsChecked { get; private set; }
    public int CheckpointsIncomplete { get; private set; }
    public int TextNodes { get; private set; }
    public int CommentNodes { get; private set; }
    public int OtherNodesWithData { get; private set; }
    public int DataRecords { get; private set; }
    public long DataCharacters { get; private set; }
    public int TruncatedData { get; private set; }
    public int Transitions { get; private set; }
    public int TransitionsCompared { get; private set; }
    public int TransitionsMatched { get; private set; }
    public int CheckpointDataCompared { get; private set; }
    public int CheckpointDataMatched { get; private set; }

    /// <summary>The number of differences of each kind.</summary>
    public IReadOnlyDictionary<string, int> Differences => _differences;

    /// <summary>Applies one browser.dom record, in record order.</summary>
    public void Add(string eventType, JsonElement payload)
    {
        var token = LayoutChangeState.DocumentToken(payload);
        if (token is null)
        {
            return;
        }
        switch (eventType)
        {
            case "dom-checkpoint-started":
                if (_open.Remove(token, out var abandoned))
                {
                    CheckpointsIncomplete++;
                    Note("started-before-completion", abandoned.Id);
                }
                _open[token] = new Checkpoint(payload.GetProperty("checkpointId").GetString()!);
                break;
            case "dom-checkpoint-node":
                if (_open.TryGetValue(token, out var checkpoint))
                {
                    EndNode(checkpoint);
                    checkpoint.LastNodeId = payload.GetProperty("nodeId").GetInt64();
                    checkpoint.LastNodeType = payload.GetProperty("nodeType").GetString();
                    checkpoint.LastNodeHasData = false;
                }
                break;
            case "dom-checkpoint-node-character-data":
                if (_open.TryGetValue(token, out checkpoint))
                {
                    AddData(checkpoint, payload);
                }
                Rebuild(token, payload);
                break;
            case "dom-character-data-changed":
                AddTransition(token, payload);
                break;
            case "dom-checkpoint-completed":
                if (_open.Remove(token, out checkpoint))
                {
                    EndNode(checkpoint);
                    Complete(checkpoint, payload);
                }
                break;
        }
    }

    /// <summary>Counts the checkpoints still open when the records ended.</summary>
    public void Finish()
    {
        CheckpointsIncomplete += _open.Count;
        _open.Clear();
    }

    public string Report()
    {
        var report = new StringBuilder();
        report.AppendLine($"checkpoints checked: {CheckpointsChecked}");
        report.AppendLine($"checkpoints not completed: {CheckpointsIncomplete}");
        report.AppendLine($"text nodes: {TextNodes}");
        report.AppendLine($"comment nodes: {CommentNodes}");
        report.AppendLine($"other nodes with data: {OtherNodesWithData}");
        report.AppendLine($"data records: {DataRecords}");
        report.AppendLine($"data characters recorded: {DataCharacters}");
        report.AppendLine($"data records cut: {TruncatedData}");
        report.AppendLine($"character data transitions: {Transitions}");
        report.AppendLine(
            $"transitions whose previous text was compared with the rebuilt data: {TransitionsCompared}, equal {TransitionsMatched}");
        report.AppendLine(
            $"checkpoint data compared with the data rebuilt before it: {CheckpointDataCompared}, equal {CheckpointDataMatched}");
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

    private Dictionary<long, string> Document(string token)
    {
        if (!_rebuilt.TryGetValue(token, out var nodes))
        {
            nodes = [];
            _rebuilt[token] = nodes;
        }
        return nodes;
    }

    private void Rebuild(string token, JsonElement payload)
    {
        var nodes = Document(token);
        var nodeId = payload.GetProperty("nodeId").GetInt64();
        var data = payload.GetProperty("data").GetString() ?? "";
        if (nodes.TryGetValue(nodeId, out var rebuilt))
        {
            CheckpointDataCompared++;
            if (string.Equals(rebuilt, data, StringComparison.Ordinal))
            {
                CheckpointDataMatched++;
            }
        }
        if (!payload.GetProperty("dataTruncated").GetBoolean())
        {
            nodes[nodeId] = data;
        }
        else
        {
            nodes.Remove(nodeId);
        }
    }

    private void AddTransition(string token, JsonElement payload)
    {
        Transitions++;
        var nodes = Document(token);
        var nodeId = payload.GetProperty("nodeId").GetInt64();
        if (nodes.TryGetValue(nodeId, out var rebuilt) &&
            payload.TryGetProperty("previousText", out var previous) &&
            previous.ValueKind == JsonValueKind.String &&
            !payload.GetProperty("previousTextTruncated").GetBoolean())
        {
            TransitionsCompared++;
            if (string.Equals(rebuilt, previous.GetString(), StringComparison.Ordinal))
            {
                TransitionsMatched++;
            }
        }
        if (payload.TryGetProperty("text", out var text) &&
            text.ValueKind == JsonValueKind.String &&
            !payload.GetProperty("textTruncated").GetBoolean())
        {
            nodes[nodeId] = text.GetString()!;
        }
        else
        {
            nodes.Remove(nodeId);
        }
    }

    private void AddData(Checkpoint checkpoint, JsonElement payload)
    {
        checkpoint.DataRecords++;
        DataRecords++;
        var nodeId = payload.GetProperty("nodeId").GetInt64();
        var data = payload.GetProperty("data").GetString() ?? "";
        DataCharacters += data.Length;
        if (payload.GetProperty("dataTruncated").GetBoolean())
        {
            TruncatedData++;
        }
        if (checkpoint.LastNodeId != nodeId)
        {
            Note("data-not-after-its-node", $"{checkpoint.Id} node {nodeId}");
            return;
        }
        if (checkpoint.LastNodeHasData)
        {
            Note("second-data-record", $"{checkpoint.Id} node {nodeId}");
            return;
        }
        checkpoint.LastNodeHasData = true;
        if (checkpoint.LastNodeType is not ("text" or "comment" or "other"))
        {
            Note("data-for-node-type", $"{checkpoint.Id} node {nodeId} {checkpoint.LastNodeType}");
        }
    }

    private void EndNode(Checkpoint checkpoint)
    {
        if (checkpoint.LastNodeId is not { } nodeId)
        {
            return;
        }
        switch (checkpoint.LastNodeType)
        {
            case "text":
                TextNodes++;
                break;
            case "comment":
                CommentNodes++;
                break;
            case "other" when checkpoint.LastNodeHasData:
                OtherNodesWithData++;
                break;
        }
        if (checkpoint.LastNodeType is "text" or "comment" && !checkpoint.LastNodeHasData)
        {
            Note("node-without-data", $"{checkpoint.Id} node {nodeId} {checkpoint.LastNodeType}");
        }
        checkpoint.LastNodeId = null;
        checkpoint.LastNodeType = null;
        checkpoint.LastNodeHasData = false;
    }

    private void Complete(Checkpoint checkpoint, JsonElement payload)
    {
        CheckpointsChecked++;
        if (!payload.TryGetProperty("characterDataCount", out var count) ||
            count.ValueKind != JsonValueKind.Number)
        {
            Note("completion-without-count", checkpoint.Id);
            return;
        }
        if (count.GetInt32() != checkpoint.DataRecords)
        {
            Note(
                "completion-count",
                $"{checkpoint.Id} states {count.GetInt32()}, recorded {checkpoint.DataRecords}");
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

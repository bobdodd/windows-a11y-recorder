using System.Text.Json;
using Recorder.Session;

namespace Recorder.Tests;

// The layout change state and its check against checkpoints, from generated
// records in the shapes the bridge writes in protocol 0.32.
public sealed class LayoutChangeCheckTests
{
    private const string Token = "F8543F87A3AF6713E6DEADA760E49A6C";

    private static readonly string Context =
        $$"""{"documentId":"dom-document-19","documentToken":"{{Token}}"}""";

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ChangesStarted(int set, string? checkpoint, double offset = 0) => Json($$"""
        {"context":{{Context}},"changeSetId":"layout-changes-{{set}}",
         "layoutCheckpointId":{{(checkpoint is null ? "null" : $"\"{checkpoint}\"")}},
         "viewTransformNodeId":"layout-transform-1",
         "viewPaintOffset":{"x":{{offset}},"y":{{offset}}},"layoutZoomFactor":1.25}
        """);

    private static JsonElement ChangesCompleted(int set) => Json($$"""
        {"context":{{Context}},"changeSetId":"layout-changes-{{set}}","notedNodeCount":1,
         "recordedNodeCount":1,"unchangedNodeCount":0,"transformNodeCount":0}
        """);

    private static JsonElement Transform(int id, int? parent, double[] matrix, bool flattens = false) => Json($$"""
        {"context":{{Context}},"changeSetId":"layout-changes-1","transformNodeId":"layout-transform-{{id}}",
         "parentTransformNodeId":{{(parent is null ? "null" : $"\"layout-transform-{parent}\"")}},
         "matrix":[{{string.Join(",", matrix)}}],"flattensInheritedTransform":{{(flattens ? "true" : "false")}},
         "scrollTranslation":false,"sticky":false}
        """);

    private static double[] Translation(double x, double y) =>
        [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, 0, 1];

    private static JsonElement Changed(long node, int transform, double x, double y, double width, double height,
        string color = "red", double scale = 0.8) => Json($$"""
        {"context":{{Context}},"changeSetId":"layout-changes-1","reasons":["layout"],"nodeId":{{node}},
         "nodeType":"element","nodeName":"DIV","layoutObjectPresent":true,"displayLocked":false,
         "geometry":{"transformNodeId":"layout-transform-{{transform}}",
           "localRect":{"x":{{x}},"y":{{y}},"width":{{width}},"height":{{height}}},
           "clientRectEmpty":false,"localRectMapped":true,"clientRectScale":{{scale}}},
         "computedStyle":{"color":"{{color}}"},"pseudoElement":null,"shadowHostNodeId":null,"shadowRootMode":null}
        """);

    private static JsonElement CheckpointStarted(int id, string walkReason = "check") => Json($$"""
        {"context":{{Context}},"checkpointId":"layout-checkpoint-{{id}}","walkReason":"{{walkReason}}"}
        """);

    private static JsonElement CheckpointNode(int id, long node, double x, double y, double width, double height,
        string color = "red") => Json($$"""
        {"context":{{Context}},"checkpointId":"layout-checkpoint-{{id}}","nodeIndex":0,"nodeId":{{node}},
         "nodeType":"element","nodeName":"DIV","layoutObjectPresent":true,"displayLocked":false,
         "boundingClientRect":{"x":{{x}},"y":{{y}},"width":{{width}},"height":{{height}}},
         "computedStyle":{"color":"{{color}}"} }
        """);

    private static JsonElement CheckpointCompleted(int id) => Json($$"""
        {"context":{{Context}},"checkpointId":"layout-checkpoint-{{id}}"}
        """);

    private static void Checkpoint(LayoutChangeCheck check, int id, params JsonElement[] nodes) =>
        Checkpoint(check, id, "check", nodes);

    private static void Checkpoint(LayoutChangeCheck check, int id, string walkReason, params JsonElement[] nodes)
    {
        check.Add("layout-checkpoint-started", CheckpointStarted(id, walkReason));
        foreach (var node in nodes)
        {
            check.Add("layout-checkpoint-node", node);
        }
        check.Add("layout-checkpoint-completed", CheckpointCompleted(id));
    }

    [Fact]
    public void DerivesTheClientRectThroughAScrollTranslation()
    {
        var state = new LayoutChangeState();
        state.Apply("layout-changes-started", ChangesStarted(1, null));
        state.Apply("layout-transform-node", Transform(1, null, Translation(0, 0)));
        state.Apply("layout-transform-node", Transform(2, 1, Translation(0, -300)));
        state.Apply("layout-node-changed", Changed(42, 2, 10, 400, 150, 25));

        var rect = state.Documents[Token].DeriveClientRect(42, out var failure);

        Assert.Equal(LayoutDerivationFailure.None, failure);
        Assert.Equal(new LayoutDerivedRect(8, 80, 120, 20), rect);

        // A scroll changes the scroll translation alone.
        state.Apply("layout-transform-node", Transform(2, 1, Translation(0, -400)));
        rect = state.Documents[Token].DeriveClientRect(42, out _);
        Assert.Equal(new LayoutDerivedRect(8, 0, 120, 20), rect);
    }

    [Fact]
    public void DerivesTheBoundsOfARotatedRect()
    {
        var state = new LayoutChangeState();
        state.Apply("layout-changes-started", ChangesStarted(1, null));
        state.Apply("layout-transform-node", Transform(1, null, Translation(0, 0)));
        state.Apply("layout-transform-node", Transform(2, 1, [0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 100, 0, 0, 1]));
        state.Apply("layout-node-changed", Changed(42, 2, 0, 0, 10, 20, scale: 1));

        var rect = state.Documents[Token].DeriveClientRect(42, out _);

        Assert.Equal(new LayoutDerivedRect(80, 0, 20, 10), rect);
    }

    [Fact]
    public void SubtractsTheViewPaintOffset()
    {
        var state = new LayoutChangeState();
        state.Apply("layout-changes-started", ChangesStarted(1, null, offset: 5));
        state.Apply("layout-transform-node", Transform(1, null, Translation(40, 40)));
        state.Apply("layout-node-changed", Changed(42, 1, 15, 25, 10, 10, scale: 1));

        var rect = state.Documents[Token].DeriveClientRect(42, out _);

        // The view's own matrix places the view in its parent and is not applied.
        Assert.Equal(new LayoutDerivedRect(10, 20, 10, 10), rect);
    }

    [Fact]
    public void ReportsAChainThatDoesNotReachTheView()
    {
        var state = new LayoutChangeState();
        state.Apply("layout-changes-started", ChangesStarted(1, null));
        state.Apply("layout-transform-node", Transform(3, null, Translation(0, 0)));
        state.Apply("layout-node-changed", Changed(42, 3, 0, 0, 1, 1));
        state.Apply("layout-node-changed", Changed(43, 9, 0, 0, 1, 1));

        Assert.Null(state.Documents[Token].DeriveClientRect(42, out var unreached));
        Assert.Equal(LayoutDerivationFailure.TransformChainDoesNotReachView, unreached);
        Assert.Null(state.Documents[Token].DeriveClientRect(43, out var missing));
        Assert.Equal(LayoutDerivationFailure.TransformNodeNotRecorded, missing);
        Assert.Null(state.Documents[Token].DeriveClientRect(44, out var unrecorded));
        Assert.Equal(LayoutDerivationFailure.NodeNotRecorded, unrecorded);
    }

    [Fact]
    public void ComparesACheckpointWithTheChangeSetOfItsUpdate()
    {
        var check = new LayoutChangeCheck();
        Checkpoint(check, 1, CheckpointNode(1, 42, 8, 80, 120, 20));
        check.Add("layout-changes-started", ChangesStarted(1, "layout-checkpoint-1"));
        check.Add("layout-transform-node", Transform(1, null, Translation(0, 0)));
        check.Add("layout-transform-node", Transform(2, 1, Translation(0, -300)));
        check.Add("layout-node-changed", Changed(42, 2, 10, 400, 150, 25));
        check.Add("layout-changes-completed", ChangesCompleted(1));

        // The next update recorded a checkpoint and no change set, so it is
        // compared with the state before the document's next record.
        Checkpoint(check, 2, CheckpointNode(2, 42, 8, 80, 120, 20));
        check.Add("layout-changes-started", ChangesStarted(2, null));
        check.Add("layout-transform-node", Transform(2, 1, Translation(0, -400)));
        check.Add("layout-changes-completed", ChangesCompleted(2));
        check.Finish();

        Assert.Equal(2, check.CheckpointsCompared);
        Assert.Equal(2, check.NodesMatched);
        Assert.Empty(check.Differences);
    }

    [Fact]
    public void DoesNotCompareADocumentFromAWalkAfterALostRecord()
    {
        var check = new LayoutChangeCheck();
        Checkpoint(check, 1, "first", CheckpointNode(1, 42, 8, 80, 120, 20));
        check.Add("layout-changes-started", ChangesStarted(1, "layout-checkpoint-1"));
        check.Add("layout-transform-node", Transform(1, null, Translation(0, 0)));
        check.Add("layout-transform-node", Transform(2, 1, Translation(0, -300)));
        check.Add("layout-node-changed", Changed(42, 2, 10, 400, 150, 25));
        check.Add("layout-changes-completed", ChangesCompleted(1));

        // The change records of the node's move were lost. The state is
        // rebuilt from change records alone, so neither the walk after the
        // loss nor a later one is compared.
        Checkpoint(check, 2, "after-loss", CheckpointNode(2, 42, 8, 180, 120, 20));
        Checkpoint(check, 3, CheckpointNode(3, 42, 8, 180, 120, 20));
        check.Finish();

        Assert.Equal(1, check.CheckpointsCompared);
        Assert.Equal(2, check.CheckpointsAfterLoss);
        Assert.Empty(check.Differences);
    }

    [Fact]
    public void ReportsEachKindOfDifference()
    {
        var check = new LayoutChangeCheck();
        Checkpoint(
            check,
            1,
            CheckpointNode(1, 42, 8, 81, 120, 20, color: "blue"),
            CheckpointNode(1, 43, 0, 0, 1, 1));
        check.Add("layout-changes-started", ChangesStarted(1, "layout-checkpoint-1"));
        check.Add("layout-transform-node", Transform(1, null, Translation(0, 0)));
        check.Add("layout-transform-node", Transform(2, 1, Translation(0, -300)));
        check.Add("layout-node-changed", Changed(42, 2, 10, 400, 150, 25));
        check.Add("layout-changes-completed", ChangesCompleted(1));
        check.Finish();

        Assert.Equal(0, check.NodesMatched);
        Assert.Equal(1, check.Differences["computed-style"]);
        Assert.Equal(1, check.Differences["rect"]);
        Assert.Equal(1, check.Differences["node-not-recorded"]);
        Assert.Equal(1, check.LargestRectDifference, 6);
        Assert.Contains("layout-checkpoint-1 node 42 DIV color", check.Report());
    }

    [Fact]
    public void MatchesANodeWithoutRecordOrLayoutObjectOrStyle()
    {
        var check = new LayoutChangeCheck();
        var unstyled = Json($$"""
            {"context":{{Context}},"checkpointId":"layout-checkpoint-1","nodeIndex":0,"nodeId":7,
             "nodeType":"element","nodeName":"META","layoutObjectPresent":false,"displayLocked":false,
             "boundingClientRect":null,"computedStyle":null}
            """);
        Checkpoint(check, 1, unstyled, CheckpointNode(1, 42, 0, 0, 1, 1));
        check.Finish();

        Assert.Equal(1, check.NodesMatched);
        Assert.Equal(1, check.NodesMatchingWithoutRecord);
        Assert.Equal(1, check.Differences["node-not-recorded"]);
    }

    [Fact]
    public void DoesNotCompareACheckpointWhoseChangeSetNeverCompleted()
    {
        var check = new LayoutChangeCheck();
        Checkpoint(check, 1, CheckpointNode(1, 42, 8, 80, 120, 20));
        check.Add("layout-changes-started", ChangesStarted(1, "layout-checkpoint-1"));
        check.Finish();

        Assert.Equal(0, check.CheckpointsCompared);
        Assert.Equal(1, check.CheckpointsWithIncompleteChangeSets);
        Assert.Empty(check.Differences);
    }

    [Fact]
    public void AcceptsTheSamplePayloads()
    {
        var check = new LayoutChangeCheck();
        foreach (var (eventType, json) in BrowserLayoutPayloads.All())
        {
            check.Add(eventType, Json(json));
        }
        check.Finish();

        Assert.Equal(2, check.ChangeSets);
        Assert.Equal(3, check.ChangedNodeRecords);
        Assert.Equal(2, check.TransformNodeRecords);
    }
}

// A diagnostic, run only when RECORDER_LAYOUT_CHANGES_FILE names a recording
// file: checks its layout change records against its layout checkpoints and
// writes the report to RECORDER_LAYOUT_CHANGES_REPORT, or to
// layout-changes-report.txt in the temporary directory.
public sealed class LayoutChangeRecordingCheck
{
    [Fact]
    public async Task ChecksTheLayoutChangesOfARecordingFile()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_LAYOUT_CHANGES_FILE") is not { } source)
        {
            return;
        }

        var check = new LayoutChangeCheck();
        using (var reader = Recorder.Database.RecordingFiles.RecordingFileReader.Open(source))
        {
            var records = reader.ReadAll()
                .Where(message => message.Channel.Topic == "browser.layout")
                .Select(message => Recorder.Database.RecordingFiles.RecordingEventCodec.Decode(message.Data.Span))
                .OrderBy(item => item.EventKey);
            foreach (var record in records)
            {
                check.Add(record.Event.EventType, record.Event.Payload);
            }
        }
        check.Finish();
        var reportPath = Environment.GetEnvironmentVariable("RECORDER_LAYOUT_CHANGES_REPORT") ??
            Path.Combine(Path.GetTempPath(), "layout-changes-report.txt");
        await File.WriteAllTextAsync(reportPath, check.Report(), TestContext.Current.CancellationToken);
    }
}

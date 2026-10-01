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
        string color = "red", double scale = 0.8, string quadRects = "null") => Json($$"""
        {"context":{{Context}},"changeSetId":"layout-changes-1","reasons":["layout"],"nodeId":{{node}},
         "nodeType":"element","nodeName":"DIV","layoutObjectPresent":true,"displayLocked":false,
         "geometry":{"transformNodeId":"layout-transform-{{transform}}",
           "localRect":{"x":{{x}},"y":{{y}},"width":{{width}},"height":{{height}}},
           "localQuadRects":{{quadRects}},
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
    public void DerivesARotatedRectFromTheBoundsOfEachQuad()
    {
        // Two lines, 10 by 5 and 4 by 5, under a rotation by 90 degrees: the
        // bounds of the rotated lines, not of their rotated union.
        var state = new LayoutChangeState();
        state.Apply("layout-changes-started", ChangesStarted(1, null));
        state.Apply("layout-transform-node", Transform(1, null, Translation(0, 0)));
        state.Apply("layout-transform-node", Transform(2, 1, [0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 100, 0, 0, 1]));
        state.Apply(
            "layout-node-changed",
            Changed(
                42, 2, 0, 0, 10, 10, scale: 1,
                quadRects: """[{"x":0,"y":0,"width":10,"height":5},{"x":0,"y":5,"width":4,"height":5}]"""));

        var rect = state.Documents[Token].DeriveClientRect(42, out var failure);

        Assert.Equal(LayoutDerivationFailure.None, failure);
        Assert.Equal(new LayoutDerivedRect(90, 0, 10, 10), rect);

        // The same quads under a skew: the second line's corner is not the
        // corner of the union.
        state.Apply("layout-transform-node", Transform(2, 1, [1, 0.5, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]));
        rect = state.Documents[Token].DeriveClientRect(42, out _);
        Assert.Equal(new LayoutDerivedRect(0, 0, 10, 12), rect);
    }

    [Fact]
    public void UnitesQuadRectsAsBlinkUnitesThem()
    {
        // An empty quad does not widen the union.
        var state = new LayoutChangeState();
        state.Apply("layout-changes-started", ChangesStarted(1, null));
        state.Apply("layout-transform-node", Transform(1, null, Translation(0, 0)));
        state.Apply("layout-transform-node", Transform(2, 1, Translation(0, 0)));
        state.Apply(
            "layout-node-changed",
            Changed(
                42, 2, 10, 10, 20, 5, scale: 1,
                quadRects: """[{"x":0,"y":0,"width":0,"height":5},{"x":10,"y":10,"width":20,"height":5}]"""));

        var rect = state.Documents[Token].DeriveClientRect(42, out _);

        Assert.Equal(new LayoutDerivedRect(10, 10, 20, 5), rect);
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

    // A node record with the given style fields (protocol 0.37), at the
    // geometry of Changed(42, 2, 10, 400, 150, 25).
    private static JsonElement ChangedStyle(int set, string style, string custom, bool complete, string removed,
        string? fragments = null) => Json($$"""
        {"context":{{Context}},"changeSetId":"layout-changes-{{set}}","reasons":["style"],"nodeId":42,
         "nodeType":"element","nodeName":"DIV","layoutObjectPresent":true,"displayLocked":false,
         "geometry":{"transformNodeId":"layout-transform-2",
           "localRect":{"x":10,"y":400,"width":150,"height":25},"localQuadRects":null,
           "clientRectEmpty":false,"localRectMapped":true,"clientRectScale":0.8},
         "computedStyle":{{style}},"computedStyleComplete":{{(complete ? "true" : "false")}},
         "customProperties":{{custom}},"removedCustomProperties":{{removed}},
         "pseudoElement":null,"shadowHostNodeId":null,"shadowRootMode":null
         {{(fragments is null ? "" : $",\"boxFragments\":{fragments}")}} }
        """);

    private static JsonElement CheckpointStyleNode(int id, string style, string custom,
        string? fragments = null) => Json($$"""
        {"context":{{Context}},"checkpointId":"layout-checkpoint-{{id}}","nodeIndex":0,"nodeId":42,
         "nodeType":"element","nodeName":"DIV","layoutObjectPresent":true,"displayLocked":false,
         "boundingClientRect":{"x":8,"y":80,"width":120,"height":20},
         "computedStyle":{{style}},"customProperties":{{custom}}
         {{(fragments is null ? "" : $",\"boxFragments\":{fragments}")}} }
        """);

    // Box fragments (protocol 0.38) of one fragment of the given width with
    // one child box.
    private static string Fragments(double width) => $$"""
        {"effectiveZoom":1,"fragments":[{"width":{{width}},"height":20,"breakToken":null,
         "scrollableOverflow":null,"children":[{"kind":"box","x":0,"y":0,"nodeId":43,
         "fragmentIndex":0,"fragment":null}]}],"naturalSize":null}
        """;

    private static void StyleChangeSet(LayoutChangeCheck check, int set, JsonElement node)
    {
        check.Add("layout-changes-started", ChangesStarted(set, $"layout-checkpoint-{set}"));
        if (set == 1)
        {
            check.Add("layout-transform-node", Transform(1, null, Translation(0, 0)));
            check.Add("layout-transform-node", Transform(2, 1, Translation(0, -300)));
        }
        check.Add("layout-node-changed", node);
        check.Add("layout-changes-completed", ChangesCompleted(set));
    }

    [Fact]
    public void MergesStyleChangesIntoTheNodesLastRecord()
    {
        var check = new LayoutChangeCheck();
        Checkpoint(check, 1, CheckpointStyleNode(1,
            """{"color":"red","display":"block"}""", """{"--gap":"4px","--old":"1"}"""));
        StyleChangeSet(check, 1, ChangedStyle(1,
            """{"color":"red","display":"block"}""", """{"--gap":"4px","--old":"1"}""", true, "null"));
        Checkpoint(check, 2, CheckpointStyleNode(2,
            """{"color":"blue","display":"block"}""", """{"--gap":"8px","--accent":"green"}"""));
        StyleChangeSet(check, 2, ChangedStyle(2,
            """{"color":"blue"}""", """{"--gap":"8px","--accent":"green"}""", false, """["--old"]"""));
        check.Finish();

        Assert.Equal(2, check.CheckpointsCompared);
        Assert.Equal(2, check.NodesMatched);
        Assert.Empty(check.Differences);
    }

    [Fact]
    public void TheMergedRecordIsWholeOnlyAfterAWholeRecord()
    {
        var state = new LayoutChangeState();
        state.Apply("layout-changes-started", ChangesStarted(1, null));
        state.Apply("layout-node-changed", ChangedStyle(1,
            """{"color":"blue"}""", """{"--accent":"green"}""", false, "[]"));
        var merged = state.Documents[Token].Nodes[42];
        Assert.False(merged.GetProperty("computedStyleComplete").GetBoolean());
        Assert.Equal("blue", merged.GetProperty("computedStyle").GetProperty("color").GetString());

        state.Apply("layout-node-changed", ChangedStyle(1,
            """{"color":"red","display":"block"}""", """{"--gap":"4px"}""", true, "null"));
        state.Apply("layout-node-changed", ChangedStyle(2,
            """{"display":"none"}""", """{}""", false, """["--gap"]"""));
        merged = state.Documents[Token].Nodes[42];
        Assert.True(merged.GetProperty("computedStyleComplete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, merged.GetProperty("removedCustomProperties").ValueKind);
        Assert.Equal(
            """{"color":"red","display":"none"}""",
            merged.GetProperty("computedStyle").GetRawText());
        Assert.Equal("{}", merged.GetProperty("customProperties").GetRawText());
        Assert.Equal(400, merged.GetProperty("geometry").GetProperty("localRect").GetProperty("y").GetDouble());
    }

    [Fact]
    public void ComparesBoxFragmentsThroughARecordOfStyleChanges()
    {
        var check = new LayoutChangeCheck();
        Checkpoint(check, 1, CheckpointStyleNode(1, """{"color":"red"}""", "{}", Fragments(120)));
        StyleChangeSet(check, 1, ChangedStyle(1, """{"color":"red"}""", "{}", true, "null", Fragments(120)));
        // A record of style changes carries the node's whole box fragments.
        Checkpoint(check, 2, CheckpointStyleNode(2, """{"color":"blue"}""", "{}", Fragments(150)));
        StyleChangeSet(check, 2, ChangedStyle(2, """{"color":"blue"}""", "{}", false, "[]", Fragments(150)));
        check.Finish();

        Assert.Equal(2, check.BoxFragmentsCompared);
        Assert.Equal(2, check.NodesMatched);
        Assert.Empty(check.Differences);
        Assert.Contains("box fragments compared: 2", check.Report());
    }

    [Fact]
    public void ReportsBoxFragmentsThatDiffer()
    {
        var check = new LayoutChangeCheck();
        Checkpoint(check, 1, CheckpointStyleNode(1, """{"color":"red"}""", "{}", Fragments(150)));
        StyleChangeSet(check, 1, ChangedStyle(1, """{"color":"red"}""", "{}", true, "null", Fragments(120)));
        Checkpoint(check, 2, CheckpointStyleNode(2, """{"color":"red"}""", "{}", Fragments(120)));
        StyleChangeSet(check, 2, ChangedStyle(2, """{"color":"red"}""", "{}", false, "[]"));
        check.Finish();

        Assert.Equal(0, check.NodesMatched);
        Assert.Equal(2, check.Differences["box-fragments"]);
    }

    [Fact]
    public void AMergedRecordHoldsTheBoxFragmentsOfTheChanges()
    {
        var state = new LayoutChangeState();
        state.Apply("layout-changes-started", ChangesStarted(1, null));
        state.Apply("layout-node-changed", ChangedStyle(1,
            """{"color":"red"}""", "{}", true, "null", Fragments(120)));
        state.Apply("layout-node-changed", ChangedStyle(2,
            """{"color":"blue"}""", "{}", false, "[]", Fragments(150)));
        var merged = state.Documents[Token].Nodes[42];

        Assert.True(JsonElement.DeepEquals(Json(Fragments(150)), merged.GetProperty("boxFragments")));
    }

    [Fact]
    public void ASnapshotKeepsAMergedRecordAsItIs()
    {
        var state = new LayoutChangeState();
        state.Apply("layout-changes-started", ChangesStarted(1, null));
        state.Apply("layout-node-changed", ChangedStyle(1,
            """{"color":"blue"}""", """{"--accent":"green"}""", false, "[]"));
        var merged = state.Documents[Token].Nodes[42];

        var restored = new LayoutChangeState();
        restored.Apply("layout-changes-started", ChangesStarted(1, null));
        restored.Documents[Token].Load(null, [], [merged], []);

        Assert.Equal(merged.GetRawText(), restored.Documents[Token].Nodes[42].GetRawText());
    }

    [Fact]
    public void ReportsCustomPropertiesAndAStyleNeverRecordedWhole()
    {
        var check = new LayoutChangeCheck();
        Checkpoint(check, 1, CheckpointStyleNode(1, """{"color":"blue"}""", """{"--gap":"4px"}"""));
        StyleChangeSet(check, 1, ChangedStyle(1,
            """{"color":"blue"}""", """{"--gap":"5px"}""", false, "[]"));
        check.Finish();

        Assert.Equal(0, check.NodesMatched);
        Assert.Equal(1, check.Differences["custom-properties"]);
        Assert.Equal(1, check.Differences["computed-style-incomplete"]);
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
    public void CountsAnUnstyledDisplayLockedNodeWithoutRecordApart()
    {
        var check = new LayoutChangeCheck();
        var locked = Json($$"""
            {"context":{{Context}},"checkpointId":"layout-checkpoint-1","nodeIndex":0,"nodeId":7,
             "nodeType":"element","nodeName":"DIV","layoutObjectPresent":false,"displayLocked":true,
             "boundingClientRect":null,"computedStyle":null}
            """);
        Checkpoint(check, 1, locked, CheckpointNode(1, 42, 0, 0, 1, 1));
        check.Finish();

        Assert.Equal(1, check.NodesLockedWithoutRecord);
        Assert.Equal(1, check.NodesCompared);
        Assert.Equal(0, check.NodesMatched);
        Assert.Equal(1, check.Differences["node-not-recorded"]);
        Assert.Contains(
            "checkpoint nodes under a display lock, without a change record, layout object, or style, not compared: 1",
            check.Report());
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
        Assert.Equal(4, check.ChangedNodeRecords);
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

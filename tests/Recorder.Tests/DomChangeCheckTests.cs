using System.Text.Json;
using Recorder.Session;

namespace Recorder.Tests;

// The structural DOM change check, from generated records in the shapes the
// bridge writes in protocol 0.34.
public sealed class DomChangeCheckTests
{
    private const string Context = BrowserDomChangePayloads.ContextJson;

    private static int _transition = 100;

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string NextTransition() => $"dom-transition-{Interlocked.Increment(ref _transition)}";

    // One node of a tree: its identity, parent, type, and name, with optional
    // attributes and character data.
    private sealed record TreeNode(
        long Id,
        long? Parent,
        string Type,
        string Name,
        (string Name, string Value)[]? Attributes = null,
        string? Data = null);

    // The records of a completed checkpoint of the nodes, in walk order.
    private static IEnumerable<(string, JsonElement)> Checkpoint(int checkpoint, params TreeNode[] nodes) =>
        Checkpoint(checkpoint, "check", nodes);

    private static IEnumerable<(string, JsonElement)> Checkpoint(
        int checkpoint, string walkReason, params TreeNode[] nodes)
    {
        var id = $"dom-checkpoint-{checkpoint}";
        yield return ("dom-checkpoint-started", Json($$"""
            {"context":{{Context}},"checkpointId":"{{id}}","reason":"post-mutation","walkReason":"{{walkReason}}","maximumNodes":2147483647}
            """));
        var index = 0;
        foreach (var node in nodes)
        {
            yield return ("dom-checkpoint-node", Json($$"""
                {"context":{{Context}},"checkpointId":"{{id}}","nodeIndex":{{index++}},"nodeId":{{node.Id}},
                 "parentNodeId":{{(node.Parent?.ToString() ?? "null")}},"nodeType":"{{node.Type}}","nodeName":"{{node.Name}}"}
                """));
            if (node.Data is not null)
            {
                yield return ("dom-checkpoint-node-character-data", Json($$"""
                    {"context":{{Context}},"checkpointId":"{{id}}","nodeId":{{node.Id}},
                     "data":{{JsonSerializer.Serialize(node.Data)}},"dataLength":{{node.Data.Length}},
                     "dataTruncated":false,"maximumValueLength":2147483647}
                    """));
            }
            var attributeIndex = 0;
            foreach (var (name, value) in node.Attributes ?? [])
            {
                yield return ("dom-checkpoint-node-attribute", Json($$"""
                    {"context":{{Context}},"checkpointId":"{{id}}","nodeId":{{node.Id}},"attributeIndex":{{attributeIndex++}},
                     "attributeNamespace":null,"attributeName":"{{name}}","attributeValue":{{JsonSerializer.Serialize(value)}},
                     "attributeValueLength":{{value.Length}},"attributeValueTruncated":false,"maximumValueLength":2147483647}
                    """));
            }
        }
        yield return ("dom-checkpoint-completed", Json($$"""
            {"context":{{Context}},"checkpointId":"{{id}}","reason":"post-mutation","nodeCount":{{nodes.Length}},
             "truncated":false,"maximumNodes":2147483647,"attributeCount":0,"attributesTruncated":false,
             "maximumAttributesPerNode":2147483647,"maximumValueLength":2147483647,"coveredTransitionCount":0,
             "coveredTransitionFirstId":null,"coveredTransitionLastId":null,"characterDataCount":0}
            """));
    }

    // An insertion of a subtree, given in walk order with its root first.
    private static IEnumerable<(string, JsonElement)> Insert(
        long container, long? previous, params TreeNode[] subtree)
    {
        var id = NextTransition();
        var kind = subtree[0].Type == "shadow-root" ? "shadow-root" : "child";
        yield return ("dom-node-inserted", Json($$"""
            {"context":{{Context}},"transitionId":"{{id}}","insertionKind":"{{kind}}","containerNodeId":{{container}},
             "nodeId":{{subtree[0].Id}},"previousSiblingNodeId":{{(previous?.ToString() ?? "null")}}}
            """));
        var index = 0;
        var attributes = 0;
        var data = 0;
        foreach (var node in subtree)
        {
            yield return ("dom-inserted-node", Json($$"""
                {"context":{{Context}},"insertionId":"{{id}}","nodeIndex":{{index++}},"nodeId":{{node.Id}},
                 "parentNodeId":{{node.Parent}},"nodeType":"{{node.Type}}","nodeName":"{{node.Name}}"}
                """));
            if (node.Data is not null)
            {
                data++;
                yield return ("dom-inserted-node-character-data", Json($$"""
                    {"context":{{Context}},"insertionId":"{{id}}","nodeId":{{node.Id}},
                     "data":{{JsonSerializer.Serialize(node.Data)}},"dataLength":{{node.Data.Length}},
                     "dataTruncated":false,"maximumValueLength":2147483647}
                    """));
            }
            var attributeIndex = 0;
            foreach (var (name, value) in node.Attributes ?? [])
            {
                attributes++;
                yield return ("dom-inserted-node-attribute", Json($$"""
                    {"context":{{Context}},"insertionId":"{{id}}","nodeId":{{node.Id}},"attributeIndex":{{attributeIndex++}},
                     "attributeNamespace":null,"attributeName":"{{name}}","attributeValue":{{JsonSerializer.Serialize(value)}},
                     "attributeValueLength":{{value.Length}},"attributeValueTruncated":false,"maximumValueLength":2147483647}
                    """));
            }
        }
        yield return ("dom-insertion-completed", Json($$"""
            {"context":{{Context}},"insertionId":"{{id}}","nodeCount":{{subtree.Length}},"attributeCount":{{attributes}},
             "characterDataCount":{{data}},"shadowRootCount":0,"slotCount":0}
            """));
    }

    private static (string, JsonElement) Remove(long container, long node) => ("dom-node-removed", Json($$"""
        {"context":{{Context}},"transitionId":"{{NextTransition()}}","containerNodeId":{{container}},"nodeId":{{node}}}
        """));

    private static (string, JsonElement) RemoveChildren(long container) => ("dom-children-removed", Json($$"""
        {"context":{{Context}},"transitionId":"{{NextTransition()}}","containerNodeId":{{container}}}
        """));

    private static (string, JsonElement) SetAttribute(long node, string name, string? value, string changeType) =>
        ("dom-attribute-changed", Json($$"""
        {"context":{{Context}},"transitionId":"{{NextTransition()}}","nodeId":{{node}},"nodeName":"DIV",
         "attributeNamespace":null,"attributeName":"{{name}}","changeType":"{{changeType}}",
         "attributeValue":{{JsonSerializer.Serialize(value)}},"attributeValueLength":{{(value?.Length.ToString() ?? "null")}},
         "attributeValueTruncated":false,"previousAttributeValue":null,"previousAttributeValueLength":null,
         "previousAttributeValueTruncated":false,"maximumValueLength":2147483647}
        """));

    private static (string, JsonElement) SetText(long node, string text) => ("dom-character-data-changed", Json($$"""
        {"context":{{Context}},"transitionId":"{{NextTransition()}}","nodeId":{{node}},"parentNodeId":null,
         "nodeType":"text","text":{{JsonSerializer.Serialize(text)}},"textLength":{{text.Length}},"textTruncated":false,
         "previousText":"","previousTextLength":0,"previousTextTruncated":false,"maximumValueLength":2147483647}
        """));

    private static readonly TreeNode Document = new(1, null, "document", "#document");
    private static readonly TreeNode Html = new(2, 1, "element", "HTML");
    private static readonly TreeNode Head = new(3, 2, "element", "HEAD");
    private static readonly TreeNode Body = new(10, 2, "element", "BODY");

    private static DomChangeCheck Check(params IEnumerable<(string EventType, JsonElement Payload)>[] parts)
    {
        var check = new DomChangeCheck();
        foreach (var part in parts)
        {
            foreach (var (eventType, payload) in part)
            {
                check.Add(eventType, payload);
            }
        }
        check.Finish();
        return check;
    }

    private static IEnumerable<(string, JsonElement)> One((string, JsonElement) record) => [record];

    [Fact]
    public void RebuildsInsertionsRemovalsMovesAndChangesToMatchTheNextCheckpoint()
    {
        var list = new TreeNode(20, 10, "element", "UL");
        var first = new TreeNode(21, 20, "element", "LI", [("id", "a")]);
        var firstText = new TreeNode(22, 21, "text", "#text", Data: "One");
        var second = new TreeNode(23, 20, "element", "LI");
        var secondText = new TreeNode(24, 23, "text", "#text", Data: "Two");
        var check = Check(
            Checkpoint(1, Document, Html, Head, Body),
            Insert(10, null, list, first, firstText),
            Insert(20, 21, second, secondText),
            // Move the second item before the first.
            One(Remove(20, 23)),
            Insert(20, null, second, secondText),
            One(SetAttribute(21, "id", "b", "changed")),
            One(SetAttribute(21, "class", "done", "added")),
            One(SetText(22, "Uno")),
            Checkpoint(
                2,
                Document,
                Html,
                Head,
                Body,
                list,
                second,
                secondText,
                first with { Attributes = [("id", "b"), ("class", "done")] },
                firstText with { Data = "Uno" }));

        Assert.Empty(check.Differences);
        Assert.Equal(1, check.CheckpointsCompared);
        Assert.Equal(9, check.NodesCompared);
        Assert.Equal(9, check.NodesMatched);
        Assert.Equal(3, check.Insertions);
        Assert.Equal(1, check.Removals);
    }

    [Fact]
    public void TakesAWalkAfterALostRecordAsANewBaseWithoutComparingIt()
    {
        var paragraph = new TreeNode(20, 10, "element", "P");
        // The insertion of the paragraph was lost, so the walk after the loss
        // differs from the rebuilt tree; the next walk is compared with the
        // tree taken from it.
        var check = Check(
            Checkpoint(1, "first", Document, Html, Head, Body),
            Checkpoint(2, "after-loss", Document, Html, Head, Body, paragraph),
            Checkpoint(3, Document, Html, Head, Body, paragraph));

        Assert.Empty(check.Differences);
        Assert.Equal(1, check.CheckpointsAfterLoss);
        Assert.Equal(1, check.CheckpointsCompared);
        Assert.Equal(5, check.NodesMatched);
    }

    // A checkpoint's records with another reason, or of another document
    // under the same token.
    private static IEnumerable<(string, JsonElement)> Rewritten(
        IEnumerable<(string, JsonElement)> records, string from, string to) =>
        records.Select(item => (item.Item1, Json(item.Item2.GetRawText().Replace(from, to))));

    [Fact]
    public void TakesAFinishedParseAfterDocumentOpenAsANewBase()
    {
        var html = new TreeNode(6, 1, "element", "HTML");
        var head = new TreeNode(7, 6, "element", "HEAD");
        var body = new TreeNode(8, 6, "element", "BODY");
        // document.open() removed the document's children, and the parser's
        // insertions while it parsed again are not recorded; the second
        // finished-parsing checkpoint is the document's state.
        var check = Check(
            Rewritten(Checkpoint(1, "first", Document, Html, Head, Body), "\"post-mutation\"", "\"finished-parsing\""),
            One(RemoveChildren(1)),
            Rewritten(Checkpoint(2, "finished-parsing", Document, html, head, body), "\"post-mutation\"", "\"finished-parsing\""),
            Checkpoint(3, Document, html, head, body));

        Assert.Empty(check.Differences);
        Assert.Equal(2, check.CheckpointsAtFinishedParse);
        Assert.Equal(1, check.CheckpointsCompared);
        Assert.Equal(4, check.NodesMatched);
    }

    [Fact]
    public void KeepsTwoDocumentsOfOneTokenApart()
    {
        var other = new TreeNode(30, null, "document", "#document");
        var otherHtml = new TreeNode(31, 30, "element", "HTML");
        var check = Check(
            Checkpoint(1, "first", Document, Html, Head, Body),
            Rewritten(Checkpoint(2, "first", other, otherHtml), "dom-document-19", "dom-document-30"),
            Checkpoint(3, Document, Html, Head, Body),
            Rewritten(Checkpoint(4, other, otherHtml), "dom-document-19", "dom-document-30"));

        Assert.Empty(check.Differences);
        Assert.Equal(2, check.CheckpointsCompared);
        Assert.Equal(6, check.NodesMatched);
    }

    [Fact]
    public void RemovesEveryChildOfAContainer()
    {
        var paragraph = new TreeNode(20, 10, "element", "P");
        var text = new TreeNode(21, 20, "text", "#text", Data: "Loading");
        var check = Check(
            Checkpoint(1, Document, Html, Head, Body, paragraph, text),
            One(RemoveChildren(10)),
            Checkpoint(2, Document, Html, Head, Body));

        Assert.Empty(check.Differences);
        Assert.Equal(4, check.NodesMatched);
    }

    [Fact]
    public void RecordsAnAttachedShadowRootWithItsHost()
    {
        var host = new TreeNode(20, 10, "element", "DIV");
        var root = new TreeNode(21, 20, "shadow-root", "#document-fragment");
        var slot = new TreeNode(22, 21, "element", "SLOT");
        var check = Check(
            Checkpoint(1, Document, Html, Head, Body, host),
            Insert(20, null, root, slot),
            Checkpoint(2, Document, Html, Head, Body, host, root, slot));

        Assert.Empty(check.Differences);
        Assert.Equal(7, check.NodesMatched);
    }

    [Fact]
    public void ReportsEachKindOfDifference()
    {
        var paragraph = new TreeNode(20, 10, "element", "P");
        var check = Check(
            Checkpoint(1, Document, Html, Head, Body, paragraph),
            One(Remove(10, 20)),
            Insert(2, 3, new TreeNode(30, 2, "element", "FOOTER")),
            One(SetAttribute(10, "hidden", "", "added")),
            Checkpoint(
                2,
                Document,
                Html,
                Head,
                Body,
                paragraph,
                new TreeNode(31, 1, "comment", "#comment", Data: "x")));

        // The removed paragraph and the comment the records never inserted.
        Assert.Equal(2, check.Differences["node-missing"]);
        // The footer, which the checkpoint does not hold.
        Assert.Equal(1, check.Differences["node-extra"]);
        Assert.Equal(1, check.Differences["attributes"]);
        Assert.True(check.Differences["children"] >= 2);
        Assert.Contains("node-missing", check.Report(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsARemovalOfANodeThatIsNotAChild()
    {
        var check = Check(
            Checkpoint(1, Document, Html, Head, Body),
            One(Remove(10, 3)));

        Assert.Equal(1, check.Differences["removed-node-not-a-child"]);
    }

    [Fact]
    public void CountsAChangeToANodeBeforeItsInsertionWithoutReportingIt()
    {
        var paragraph = new TreeNode(20, 10, "element", "P", [("class", "new")]);
        var check = Check(
            Checkpoint(1, Document, Html, Head, Body),
            One(SetAttribute(20, "class", "new", "added")),
            Insert(10, null, paragraph),
            Checkpoint(2, Document, Html, Head, Body, paragraph));

        Assert.Empty(check.Differences);
        Assert.Equal(1, check.ChangesOutsideTheTree);
    }

    [Fact]
    public void ReportsAnInsertionIntoAContainerOutsideTheTree()
    {
        var check = Check(
            Checkpoint(1, Document, Html, Head, Body),
            Insert(40, null, new TreeNode(41, 40, "element", "P")));

        Assert.Equal(1, check.Differences["node-not-in-tree"]);
    }

    [Fact]
    public void ReportsAnInsertionWhoseCountsDoNotMatchItsRecords()
    {
        var records = Insert(10, null, new TreeNode(20, 10, "element", "P")).ToList();
        var completed = records[^1].Item2.GetRawText().Replace("\"nodeCount\":1", "\"nodeCount\":2", StringComparison.Ordinal);
        records[^1] = ("dom-insertion-completed", Json(completed));
        var check = Check(Checkpoint(1, Document, Html, Head, Body), records);

        Assert.Equal(1, check.Differences["insertion-count"]);
    }

    [Fact]
    public void ComparesAScrollOffsetWithItsScrollTranslation()
    {
        var check = new DomChangeCheck();
        foreach (var (eventType, json) in BrowserLayoutPayloads.All())
        {
            check.Add(eventType, Json(json));
        }
        check.Finish();

        // The sample scroll translation is recorded with a translation of
        // (0, -300.625), the negated scroll position of the sample offset.
        Assert.Equal(2, check.ScrollOffsets);
        Assert.Equal(1, check.ScrollOffsetsCompared);
        Assert.Equal(1, check.ScrollOffsetsMatched);
        Assert.Empty(check.Differences);
    }

    [Fact]
    public void AcceptsTheSamplePayloads()
    {
        var check = Check(
            Checkpoint(1, Document, Html, Head, Body),
            BrowserDomChangePayloads.All().Select(record => (record.EventType, Json(record.Json))));

        Assert.Equal(2, check.Insertions);
        Assert.Equal(1, check.Removals);
        Assert.Equal(1, check.ChildrenRemovals);
    }

    // Set RECORDER_DOM_CHANGES_FILE to a recording.mcap to check it; the
    // report is written to RECORDER_DOM_CHANGES_REPORT.
    [Fact]
    public async Task ChecksTheDomChangesOfARecordingFile()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_DOM_CHANGES_FILE") is not { } source)
        {
            return;
        }

        var check = new DomChangeCheck();
        using (var reader = Recorder.Database.RecordingFiles.RecordingFileReader.Open(source))
        {
            var records = reader.ReadAll()
                .Where(message => message.Channel.Topic is "browser.dom" or "browser.layout")
                .Select(message => Recorder.Database.RecordingFiles.RecordingEventCodec.Decode(message.Data.Span))
                .OrderBy(item => item.EventKey);
            foreach (var record in records)
            {
                check.Add(record.Event.EventType, record.Event.Payload);
            }
        }
        check.Finish();
        var reportPath = Environment.GetEnvironmentVariable("RECORDER_DOM_CHANGES_REPORT") ??
            Path.Combine(Path.GetTempPath(), "dom-changes-report.txt");
        await File.WriteAllTextAsync(reportPath, check.Report(), TestContext.Current.CancellationToken);
    }
}

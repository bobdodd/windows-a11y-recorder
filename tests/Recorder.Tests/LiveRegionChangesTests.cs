using System.Text.Json;
using Recorder.Analysis.Summary;

namespace Recorder.Tests;

// The page's changes to its live regions, from generated DOM records in the
// shapes the bridge writes (protocol 0.34), and how tiles are grouped.
public sealed class LiveRegionChangesTests
{
    private const string Context = BrowserDomChangePayloads.ContextJson;
    private const string Token = "F8543F87A3AF6713E6DEADA760E49A6C";
    private int _transition = 100;
    private long _time;

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed record TreeNode(long Id, long? Parent, string Type, string Name, (string Name, string Value)[]? Attributes = null, string? Data = null);

    private static IEnumerable<(string, JsonElement)> Checkpoint(params TreeNode[] nodes)
    {
        const string id = "dom-checkpoint-1";
        yield return ("dom-checkpoint-started", Json($$"""
            {"context":{{Context}},"checkpointId":"{{id}}","reason":"post-mutation","walkReason":"check","maximumNodes":2147483647}
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

    private IEnumerable<(string, JsonElement)> Insert(long container, params TreeNode[] subtree)
    {
        var id = $"dom-transition-{++_transition}";
        yield return ("dom-node-inserted", Json($$"""
            {"context":{{Context}},"transitionId":"{{id}}","insertionKind":"child","containerNodeId":{{container}},
             "nodeId":{{subtree[0].Id}},"previousSiblingNodeId":null}
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

    private (string, JsonElement) SetText(long node, string text) => ("dom-character-data-changed", Json($$"""
        {"context":{{Context}},"transitionId":"dom-transition-{{++_transition}}","nodeId":{{node}},"parentNodeId":null,
         "nodeType":"text","text":{{JsonSerializer.Serialize(text)}},"textLength":{{text.Length}},"textTruncated":false,
         "previousText":"","previousTextLength":0,"previousTextTruncated":false,"maximumValueLength":2147483647}
        """));

    // A page with a polite status region holding "Ready" (30, 31), a region
    // with aria-live off (40), and a plain div (50).
    private static readonly TreeNode[] Page =
    [
        new(1, null, "document", "#document"),
        new(2, 1, "element", "HTML"),
        new(10, 2, "element", "BODY"),
        new(30, 10, "element", "DIV", [("role", "status"), ("id", "saved")]),
        new(31, 30, "text", "#text", Data: "Ready"),
        new(40, 10, "element", "DIV", [("aria-live", "off"), ("role", "alert")]),
        new(50, 10, "element", "DIV"),
    ];

    private LiveRegionChanges Read(params IEnumerable<(string EventType, JsonElement Payload)>[] parts)
    {
        var changes = new LiveRegionChanges();
        foreach (var (eventType, payload) in parts.SelectMany(part => part))
        {
            changes.Add(++_time, $"event-{_time}", eventType, payload);
        }

        return changes;
    }

    [Fact]
    public void Text_changed_in_a_status_region_is_counted_with_its_text_verbatim()
    {
        var changes = Read(Checkpoint(Page), [SetText(31, "Saved   at 10:02")]);

        var fact = Assert.Single(changes.Facts);
        Assert.Equal((Token, "polite", "changed", "Saved at 10:02"), (fact.DocumentToken, fact.Politeness, fact.Change, fact.Text));
        Assert.Equal("a div element with id \"saved\" with role \"status\", polite by its role", fact.Region);
        Assert.Equal(
            "The page changed text in a live region, a div element with id \"saved\" with role \"status\", polite by its role, to \"Saved at 10:02\".",
            LiveRegionChanges.Reason(fact));
    }

    [Fact]
    public void Content_added_to_a_region_is_counted_and_content_added_elsewhere_or_to_an_off_region_is_not()
    {
        var changes = Read(
            Checkpoint(Page),
            Insert(30, new TreeNode(60, 30, "element", "SPAN"), new TreeNode(61, 60, "text", "#text", Data: "2 items added")),
            Insert(40, new TreeNode(62, 40, "text", "#text", Data: "Off")),
            Insert(50, new TreeNode(63, 50, "text", "#text", Data: "Plain")));

        var fact = Assert.Single(changes.Facts);
        Assert.Equal(("added", "2 items added"), (fact.Change, fact.Text));
    }

    [Fact]
    public void An_alert_inserted_with_its_text_is_counted_and_a_new_polite_region_is_not()
    {
        var changes = Read(
            Checkpoint(Page),
            Insert(50, new TreeNode(70, 50, "element", "DIV", [("role", "alert")]), new TreeNode(71, 70, "text", "#text", Data: "Card declined")),
            Insert(50, new TreeNode(72, 50, "element", "DIV", [("aria-live", "polite")]), new TreeNode(73, 72, "text", "#text", Data: "Loaded")));

        var fact = Assert.Single(changes.Facts);
        Assert.Equal(("alert", "assertive", "Card declined"), (fact.Change, fact.Politeness, fact.Text));
        Assert.Equal("The page inserted an alert, a div element with role \"alert\", assertive by its role, with the text \"Card declined\".", LiveRegionChanges.Reason(fact));
    }

    [Fact]
    public void Tiles_cover_the_page_the_last_cut_at_its_foot()
    {
        Assert.Equal([(0d, 925d), (925d, 925d), (1850d, 500d)], RecordedMomentPages.Tiles(2350, 925));
    }

    [Fact]
    public void Tiles_are_grouped_by_their_image_tokens_at_least_one_to_a_request()
    {
        PageTile Tile(int number, int width, int height) => new(number, 0, height, width, $"t{number}.png", $"/t{number}.png", width, height);

        Assert.Equal(841, TileGroups.ImageTokens(922, 925));
        var tiles = Enumerable.Range(1, 8).Select(number => Tile(number, 922, 925)).ToList();
        Assert.Equal([4, 4], TileGroups.Group(tiles, 4096).Select(group => group.Count));
        Assert.Equal([1, 1], TileGroups.Group([Tile(1, 3000, 3000), Tile(2, 922, 925)], 4096).Select(group => group.Count));
    }
}

using System.Text.Json;
using Recorder.Session;

namespace Recorder.Tests;

// The character data check of DOM checkpoints, from generated records in the
// shapes the bridge writes in protocol 0.33.
public sealed class DomCharacterDataCheckTests
{
    private const string Context =
        """{"documentId":"dom-document-3","documentToken":"0F4A2E61C3B5D7A9E1F3B5D7A9C1E3F5"}""";

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static (string, JsonElement) Started(int checkpoint) => ("dom-checkpoint-started", Json($$"""
        {"context":{{Context}},"checkpointId":"dom-checkpoint-{{checkpoint}}","reason":"post-mutation",
         "maximumNodes":2147483647}
        """));

    private static (string, JsonElement) Node(int checkpoint, int index, long id, string type, string name) =>
        ("dom-checkpoint-node", Json($$"""
        {"context":{{Context}},"checkpointId":"dom-checkpoint-{{checkpoint}}","nodeIndex":{{index}},
         "nodeId":{{id}},"parentNodeId":null,"nodeType":"{{type}}","nodeName":"{{name}}"}
        """));

    private static (string, JsonElement) Data(int checkpoint, long id, string data) =>
        ("dom-checkpoint-node-character-data", Json($$"""
        {"context":{{Context}},"checkpointId":"dom-checkpoint-{{checkpoint}}","nodeId":{{id}},
         "data":{{JsonSerializer.Serialize(data)}},"dataLength":{{data.Length}},"dataTruncated":false,
         "maximumValueLength":2147483647}
        """));

    private static (string, JsonElement) Completed(int checkpoint, int count) => ("dom-checkpoint-completed", Json($$"""
        {"context":{{Context}},"checkpointId":"dom-checkpoint-{{checkpoint}}","reason":"post-mutation",
         "nodeCount":3,"truncated":false,"maximumNodes":2147483647,"characterDataCount":{{count}}}
        """));

    private static (string, JsonElement) Changed(long id, string previous, string text) =>
        ("dom-character-data-changed", Json($$"""
        {"context":{{Context}},"transitionId":"dom-transition-1","nodeId":{{id}},"parentNodeId":2,
         "nodeType":"text","text":{{JsonSerializer.Serialize(text)}},"textLength":{{text.Length}},
         "textTruncated":false,"previousText":{{JsonSerializer.Serialize(previous)}},
         "previousTextLength":{{previous.Length}},"previousTextTruncated":false,
         "maximumValueLength":2147483647}
        """));

    private static DomCharacterDataCheck Check(params (string EventType, JsonElement Payload)[] records)
    {
        var check = new DomCharacterDataCheck();
        foreach (var (eventType, payload) in records)
        {
            check.Add(eventType, payload);
        }
        check.Finish();
        return check;
    }

    [Fact]
    public void AcceptsACheckpointWhoseCharacterDataFollowsEachNode()
    {
        var check = Check(
            Started(1),
            Node(1, 0, 1, "document", "#document"),
            Node(1, 1, 2, "text", "#text"),
            Data(1, 2, "Save changes"),
            Node(1, 2, 3, "comment", "#comment"),
            Data(1, 3, ""),
            Node(1, 3, 4, "other", "xml-stylesheet"),
            Data(1, 4, "href=\"a.css\""),
            Completed(1, 3));

        Assert.Empty(check.Differences);
        Assert.Equal(1, check.CheckpointsChecked);
        Assert.Equal(1, check.TextNodes);
        Assert.Equal(1, check.CommentNodes);
        Assert.Equal(1, check.OtherNodesWithData);
        Assert.Equal(3, check.DataRecords);
    }

    [Fact]
    public void ReportsATextNodeWithoutData()
    {
        var check = Check(
            Started(1),
            Node(1, 0, 1, "document", "#document"),
            Node(1, 1, 2, "text", "#text"),
            Completed(1, 0));

        Assert.Equal(1, check.Differences["node-without-data"]);
    }

    [Fact]
    public void ReportsDataThatDoesNotFollowItsNode()
    {
        var check = Check(
            Started(1),
            Node(1, 0, 2, "text", "#text"),
            Data(1, 2, "a"),
            Node(1, 1, 3, "element", "P"),
            Data(1, 2, "a"),
            Completed(1, 2));

        Assert.Equal(1, check.Differences["data-not-after-its-node"]);
    }

    [Fact]
    public void ReportsDataForAnElement()
    {
        var check = Check(
            Started(1),
            Node(1, 0, 3, "element", "P"),
            Data(1, 3, "a"),
            Completed(1, 1));

        Assert.Equal(1, check.Differences["data-for-node-type"]);
    }

    [Fact]
    public void ReportsACompletionCountThatDiffersFromTheRecords()
    {
        var check = Check(
            Started(1),
            Node(1, 0, 2, "text", "#text"),
            Data(1, 2, "a"),
            Completed(1, 2));

        Assert.Equal(1, check.Differences["completion-count"]);
    }

    [Fact]
    public void ComparesTransitionsAndLaterCheckpointsWithTheRebuiltData()
    {
        var check = Check(
            Started(1),
            Node(1, 0, 2, "text", "#text"),
            Data(1, 2, "Loading"),
            Completed(1, 1),
            Changed(2, "Loading", "Saved"),
            Started(2),
            Node(2, 0, 2, "text", "#text"),
            Data(2, 2, "Saved"),
            Completed(2, 1),
            Changed(2, "Saving", "Done"));

        Assert.Empty(check.Differences);
        Assert.Equal(2, check.Transitions);
        Assert.Equal(2, check.TransitionsCompared);
        Assert.Equal(1, check.TransitionsMatched);
        Assert.Equal(1, check.CheckpointDataCompared);
        Assert.Equal(1, check.CheckpointDataMatched);
    }

    // Set RECORDER_DOM_TEXT_FILE to a recording.mcap to check it; the report
    // is written to RECORDER_DOM_TEXT_REPORT.
    [Fact]
    public async Task ChecksTheCharacterDataOfARecordingFile()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_DOM_TEXT_FILE") is not { } source)
        {
            return;
        }

        var check = new DomCharacterDataCheck();
        using (var reader = Recorder.Database.RecordingFiles.RecordingFileReader.Open(source))
        {
            var records = reader.ReadAll()
                .Where(message => message.Channel.Topic == "browser.dom")
                .Select(message => Recorder.Database.RecordingFiles.RecordingEventCodec.Decode(message.Data.Span))
                .OrderBy(item => item.EventKey);
            foreach (var record in records)
            {
                check.Add(record.Event.EventType, record.Event.Payload);
            }
        }
        check.Finish();
        var reportPath = Environment.GetEnvironmentVariable("RECORDER_DOM_TEXT_REPORT") ??
            Path.Combine(Path.GetTempPath(), "dom-text-report.txt");
        await File.WriteAllTextAsync(reportPath, check.Report(), TestContext.Current.CancellationToken);
    }
}

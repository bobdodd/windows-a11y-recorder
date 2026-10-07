using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// Slice 5a (protocol 0.55): the frame token a DOM walk names for its
/// document, the frame each owner element holds in a walk and as it changes,
/// their validation, the frames of a document's state and its snapshot, and
/// the join of owners to the documents of their frames, in the same renderer
/// and in another, from a recording file.
/// </summary>
public sealed class FrameOwnerRecordTests : IDisposable
{
    private const string ParentToken = "F8543F87A3AF6713E6DEADA760E49A6C";
    private const string ChildToken = "0A1B2C3D4E5F60718293A4B5C6D7E8F9";
    private const string MainFrame = "11112222333344445555666677778888";
    private const string LocalFrame = "AAAABBBBCCCCDDDDEEEEFFFF00001111";
    private const string RemoteFrame = "99998888777766665555444433332222";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "frame-owners-" + Guid.NewGuid().ToString("N"));

    public FrameOwnerRecordTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Context(string token, string document, int process) => $$"""
        {"browserInstanceId":"browser-1","processId":{{process}},"processType":"renderer","profileId":null,
         "browserContextId":null,"pageId":null,"frameId":null,"documentId":"{{document}}",
         "executionWorldId":null,"documentToken":"{{token}}"}
        """;

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Q(string? text) => text is null ? "null" : JsonSerializer.Serialize(text);

    // As the bridge writes them.
    private static string Started(string context, string id, string? frameToken, bool? main, bool framed = true) =>
        "{\"context\":" + context + ",\"checkpointId\":\"" + id +
        "\",\"reason\":\"post-mutation\",\"walkReason\":\"first\",\"maximumNodes\":10000" +
        (framed ? ",\"frameToken\":" + Q(frameToken) + ",\"mainFrame\":" + (main is null ? "null" : main.Value ? "true" : "false") : "") +
        "}";

    private static string Node(string context, string id, int index, long node, long? parent, string type, string name) =>
        "{\"context\":" + context + ",\"checkpointId\":\"" + id + "\",\"nodeIndex\":" + index + ",\"nodeId\":" + node +
        ",\"parentNodeId\":" + (parent?.ToString() ?? "null") + ",\"nodeType\":\"" + type + "\",\"nodeName\":\"" + name + "\"}";

    private static string Owner(string context, string id, long node, string token, string location) =>
        "{\"context\":" + context + ",\"checkpointId\":\"" + id + "\",\"ownerNodeId\":" + node +
        ",\"frameToken\":\"" + token + "\",\"frameLocation\":\"" + location + "\"}";

    private static string Completed(string context, string id, int nodes, bool truncated = false) =>
        "{\"context\":" + context + ",\"checkpointId\":\"" + id + "\",\"nodeCount\":" + nodes +
        ",\"truncated\":" + (truncated ? "true" : "false") + "}";

    private static string Changed(string context, long node, string? token, string? location) =>
        "{\"context\":" + context + ",\"ownerNodeId\":" + node + ",\"frameToken\":" + Q(token) +
        ",\"frameLocation\":" + Q(location) + "}";

    private static List<EventValidationIssue> Validate(string eventType, string json)
    {
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(BrowserEvidenceChannels.Dom, eventType, J(json), 100, issues);
        return issues;
    }

    private static readonly string Parent = Context(ParentToken, "dom-document-1", 3440);
    private static readonly string Child = Context(ChildToken, "dom-document-2", 5120);

    // The parent: a main frame with two iframes, one in its renderer and
    // one in another.
    private static List<(string Type, string Json)> ParentWalk(bool truncated = false, bool second = true)
    {
        const string id = "dom-checkpoint-1";
        var records = new List<(string, string)>
        {
            ("dom-checkpoint-started", Started(Parent, id, MainFrame, true)),
            ("dom-checkpoint-node", Node(Parent, id, 0, 1, null, "document", "#document")),
            ("dom-checkpoint-node", Node(Parent, id, 1, 2, 1, "element", "HTML")),
            ("dom-checkpoint-node", Node(Parent, id, 2, 3, 2, "element", "BODY")),
            ("dom-checkpoint-node", Node(Parent, id, 3, 4, 3, "element", "IFRAME")),
            ("dom-checkpoint-frame-owner", Owner(Parent, id, 4, LocalFrame, "local")),
        };
        if (second)
        {
            records.Add(("dom-checkpoint-node", Node(Parent, id, 4, 5, 3, "element", "IFRAME")));
            records.Add(("dom-checkpoint-frame-owner", Owner(Parent, id, 5, RemoteFrame, "remote")));
        }
        records.Add(("dom-checkpoint-completed", Completed(Parent, id, second ? 5 : 4, truncated)));
        return records;
    }

    private static List<(string Type, string Json)> ChildWalk()
    {
        const string id = "dom-checkpoint-1";
        return
        [
            ("dom-checkpoint-started", Started(Child, id, RemoteFrame, false)),
            ("dom-checkpoint-node", Node(Child, id, 0, 1, null, "document", "#document")),
            ("dom-checkpoint-completed", Completed(Child, id, 1)),
        ];
    }

    private static BrowserStateBuilder Build(IEnumerable<(string Type, string Json)> records)
    {
        var builder = new BrowserStateBuilder();
        var key = 0L;
        foreach (var (type, json) in records)
        {
            builder.Apply(key, key * 1_000, BrowserEvidenceChannels.Dom, type, J(json));
            key++;
        }
        return builder;
    }

    [Theory]
    [InlineData("dom-checkpoint-started")]
    [InlineData("dom-checkpoint-frame-owner")]
    [InlineData("dom-frame-owner-changed")]
    public void TheProtocolReadsTheRecordsAsTheBridgeWritesThem(string eventType)
    {
        var json = eventType switch
        {
            "dom-checkpoint-started" => Started(Parent, "dom-checkpoint-1", MainFrame, true),
            "dom-checkpoint-frame-owner" => Owner(Parent, "dom-checkpoint-1", 4, LocalFrame, "local"),
            _ => Changed(Parent, 4, null, null),
        };
        BrowserProtocol.ValidateEvidencePayload(BrowserEvidenceChannels.Dom, eventType, J(json));
        if (eventType == "dom-frame-owner-changed")
        {
            BrowserProtocol.ValidateEvidencePayload(BrowserEvidenceChannels.Dom, eventType, J(Changed(Parent, 4, RemoteFrame, "remote")));
        }

        var payload = JsonNode.Parse(json)!.AsObject();
        payload["undeclared"] = 1;
        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(BrowserEvidenceChannels.Dom, eventType, J(payload.ToJsonString())));
    }

    [Fact]
    public void TheValidatorAcceptsTheRecordsAndRefusesMalformedOnes()
    {
        Assert.Equal("0.55", BrowserEvidenceProtocol.CurrentVersion);
        Assert.Empty(Validate("dom-checkpoint-started", Started(Parent, "dom-checkpoint-1", MainFrame, true)));
        Assert.Empty(Validate("dom-checkpoint-started", Started(Child, "dom-checkpoint-1", RemoteFrame, false)));
        // A document with no frame, and a walk recorded before protocol 0.55.
        Assert.Empty(Validate("dom-checkpoint-started", Started(Parent, "dom-checkpoint-1", null, null)));
        Assert.Empty(Validate("dom-checkpoint-started", Started(Parent, "dom-checkpoint-1", null, null, framed: false)));
        Assert.NotEmpty(Validate("dom-checkpoint-started", Started(Parent, "dom-checkpoint-1", MainFrame, null)));
        Assert.NotEmpty(Validate("dom-checkpoint-started", Started(Parent, "dom-checkpoint-1", null, true)));
        Assert.NotEmpty(Validate("dom-checkpoint-started", Started(Parent, "dom-checkpoint-1", "abc", true)));
        Assert.NotEmpty(Validate("dom-checkpoint-started", Started(Parent, "dom-checkpoint-1", LocalFrame.ToLowerInvariant(), true)));

        Assert.Empty(Validate("dom-checkpoint-frame-owner", Owner(Parent, "dom-checkpoint-1", 4, LocalFrame, "local")));
        Assert.Empty(Validate("dom-checkpoint-frame-owner", Owner(Parent, "dom-checkpoint-1", 5, RemoteFrame, "remote")));
        Assert.NotEmpty(Validate("dom-checkpoint-frame-owner", Owner(Parent, "dom-checkpoint-1", 4, LocalFrame, "elsewhere")));
        Assert.NotEmpty(Validate("dom-checkpoint-frame-owner", Owner(Parent, "dom-checkpoint-1", 0, LocalFrame, "local")));
        Assert.NotEmpty(Validate("dom-checkpoint-frame-owner", Owner(Parent, "dom-checkpoint-1", 4, "XYZ", "local")));

        Assert.Empty(Validate("dom-frame-owner-changed", Changed(Parent, 4, LocalFrame, "local")));
        Assert.Empty(Validate("dom-frame-owner-changed", Changed(Parent, 4, null, null)));
        Assert.NotEmpty(Validate("dom-frame-owner-changed", Changed(Parent, 4, LocalFrame, null)));
        Assert.NotEmpty(Validate("dom-frame-owner-changed", Changed(Parent, 4, null, "remote")));
        Assert.NotEmpty(Validate("dom-frame-owner-changed", Changed(Parent, 4, LocalFrame, "nowhere")));
    }

    [Fact]
    public void AWalkGivesTheDocumentsFrameAndTheFrameEachOwnerHolds()
    {
        var builder = Build(ParentWalk().Concat(ChildWalk()));
        var parent = builder.Documents.Values.Single(item => item.DocumentToken == ParentToken);
        var child = builder.Documents.Values.Single(item => item.DocumentToken == ChildToken);

        Assert.True(parent.Frames.Recorded);
        Assert.Equal(MainFrame, parent.Frames.FrameToken);
        Assert.True(parent.Frames.MainFrame);
        Assert.Equal(new FrameOwnerState(4, LocalFrame, "local"), parent.Frames.Owners[4]);
        Assert.Equal(new FrameOwnerState(5, RemoteFrame, "remote"), parent.Frames.Owners[5]);
        Assert.Equal(RemoteFrame, child.Frames.FrameToken);
        Assert.False(child.Frames.MainFrame);
        Assert.Empty(child.Frames.Owners);

        // The remote owner joins to the document of another renderer by the
        // token both renderers give its frame; the local one has no
        // document recorded yet.
        var joins = BrowserFrames.OwnersOf(parent, builder.Documents.Values);
        Assert.Equal([4L, 5L], joins.Select(item => item.Owner.OwnerNodeId));
        Assert.Empty(joins[0].Documents);
        Assert.Same(child, Assert.Single(joins[1].Documents));
        var (owning, owner) = Assert.Single(BrowserFrames.OwnersOfFrame(child, builder.Documents.Values));
        Assert.Same(parent, owning);
        Assert.Equal(5, owner.OwnerNodeId);
    }

    [Fact]
    public void OwnerChangesAndLaterWalksReplaceTheFramesHeld()
    {
        var records = ParentWalk();
        // The remote iframe is removed, and the local one's frame is swapped
        // for a remote one when it navigates to another site.
        records.Add(("dom-frame-owner-changed", Changed(Parent, 5, null, null)));
        records.Add(("dom-frame-owner-changed", Changed(Parent, 4, null, null)));
        records.Add(("dom-frame-owner-changed", Changed(Parent, 4, LocalFrame, "remote")));
        var document = Assert.Single(Build(records).Documents.Values);
        Assert.Equal(new FrameOwnerState(4, LocalFrame, "remote"), Assert.Single(document.Frames.Owners.Values));

        // A whole walk names every owner holding a frame; a cut walk only
        // those it reached.
        var rewalked = ParentWalk(second: false);
        var later = Assert.Single(Build(ParentWalk().Concat(rewalked.Select(item => (item.Type, item.Json.Replace("dom-checkpoint-1", "dom-checkpoint-2"))))).Documents.Values);
        Assert.Equal([4L], later.Frames.Owners.Keys);
        var cut = ParentWalk(truncated: true, second: false);
        var merged = Assert.Single(Build(ParentWalk().Concat(cut.Select(item => (item.Type, item.Json.Replace("dom-checkpoint-1", "dom-checkpoint-2"))))).Documents.Values);
        Assert.Equal([4L, 5L], merged.Frames.Owners.Keys.Order());
    }

    [Fact]
    public void ARecordingBeforeProtocol055NamesNoFrames()
    {
        var records = ParentWalk()
            .Where(item => item.Type != "dom-checkpoint-frame-owner")
            .Select(item => item.Type == "dom-checkpoint-started"
                ? (item.Type, Started(Parent, "dom-checkpoint-1", null, null, framed: false))
                : item)
            .ToList();
        var document = Assert.Single(Build(records).Documents.Values);
        Assert.False(document.Frames.Recorded);
        Assert.Null(document.Frames.FrameToken);
        Assert.Empty(BrowserFrames.OwnersOf(document, [document]));
    }

    [Fact]
    public void TheSnapshotKeepsTheFrames()
    {
        var document = Build(ParentWalk()).Documents.Values.Single();
        var read = BrowserStateSnapshot.Read(BrowserStateSnapshot.Serialize(document));
        Assert.True(read.Frames.Recorded);
        Assert.Equal(MainFrame, read.Frames.FrameToken);
        Assert.True(read.Frames.MainFrame);
        Assert.Equal(document.Frames.Owners.Values.OrderBy(item => item.OwnerNodeId), read.Frames.Owners.Values.OrderBy(item => item.OwnerNodeId));
        Assert.Equal(BrowserStateSnapshot.Serialize(document), BrowserStateSnapshot.Serialize(read));

        // A snapshot written before protocol 0.55 has no frames.
        var old = JsonNode.Parse(BrowserStateSnapshot.Serialize(document))!.AsObject();
        old.Remove("frames");
        var older = BrowserStateSnapshot.Read(JsonSerializer.SerializeToUtf8Bytes(old));
        Assert.False(older.Frames.Recorded);
        Assert.Empty(older.Frames.Owners);
    }

    [Fact]
    public async Task TheFramesOfARecordingFileJoinAcrossRenderers()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector("test.browser", BrowserEvidenceChannels.Dom);
        var records = ParentWalk().Select(item => (Time: 1_000_000_000L, item.Type, item.Json))
            .Concat(ChildWalk().Select(item => (Time: 1_100_000_000L, item.Type, item.Json)))
            .Append((Time: 2_000_000_000L, Type: "dom-frame-owner-changed", Json: Changed(Parent, 5, null, null)))
            .ToArray();
        var events = records.Select((record, index) =>
                Event("session-a", collector, (ulong)index + 1, record.Time + index, BrowserEvidenceChannels.Dom, record.Type) with
                {
                    Payload = J(record.Json),
                })
            .ToArray();
        using (var target = new RecordingFileBatchTarget(
            path,
            new Dictionary<string, string> { ["sessionKey"] = "session-a", ["clockFrequency"] = "10000000" },
            new RecordingFileWriterOptions { ChunkBytes = 1024 }))
        {
            var batch = events.Select((record, index) => new BufferedEvent(index, record, record.Payload.GetRawText())).ToArray();
            Assert.Empty(await target.WriteAsync(new EventBatch(batch, [], []), TestContext.Current.CancellationToken));
            target.Finish();
        }

        using var reader = RecordingFileReader.Open(path);
        var state = new RecordingFileBrowserState(reader, null);
        var before = state.At(1_500_000_000, cancellationToken: TestContext.Current.CancellationToken)
            .Documents.Select(item => item.State!).ToList();
        var parent = before.Single(item => item.DocumentToken == ParentToken);
        var joined = BrowserFrames.OwnersOf(parent, before);
        Assert.Equal(ChildToken, Assert.Single(joined[1].Documents).DocumentToken);
        Assert.Equal(5120, joined[1].Documents[0].ProcessId);

        var after = state.At(2_500_000_000, cancellationToken: TestContext.Current.CancellationToken)
            .Documents.Select(item => item.State!).ToList();
        var removed = after.Single(item => item.DocumentToken == ParentToken);
        Assert.Equal([4L], BrowserFrames.OwnersOf(removed, after).Select(item => item.Owner.OwnerNodeId));
        Assert.Empty(BrowserFrames.OwnersOfFrame(after.Single(item => item.DocumentToken == ChildToken), after));
    }
}

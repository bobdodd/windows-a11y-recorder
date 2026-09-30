using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Recorder.Contracts;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// The recorded state of each browser document: rebuilt from generated
/// records, written as snapshots while recording, and read back at a time
/// or a frame, with the same result with snapshots as without.
/// </summary>
public sealed class BrowserStateTests : IDisposable
{
    private const string SessionId = "session-state";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "browser-state-" + Guid.NewGuid().ToString("N"));

    public BrowserStateTests() => Directory.CreateDirectory(_directory);

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

    private static readonly IReadOnlyDictionary<string, string> Recording =
        new Dictionary<string, string> { ["sessionKey"] = SessionId, ["clockFrequency"] = "10000000" };

    private static string Context(string token, string document = "doc-1", int process = 3440) => $$"""
        {"browserInstanceId":"browser-1","processId":{{process}},"processType":"renderer","profileId":null,
         "browserContextId":null,"pageId":"page-1","frameId":"frame-1","documentId":"{{document}}",
         "executionWorldId":null,"documentToken":"{{token}}"}
        """;

    private static JsonElement J(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    // Generates the records of one document in the shapes the bridge writes.
    private sealed class Records(string token, string document = "doc-1", int process = 3440)
    {
        private readonly string _context = Context(token, document, process);
        private int _transition;
        private int _checkpoint;
        private int _changeSet;

        public List<(string Channel, string EventType, JsonElement Payload)> Items { get; } = [];

        private void Add(string channel, string eventType, string body) =>
            Items.Add((channel, eventType, J($$"""{"context":{{_context}},{{body}}}""")));

        public Records Walk(string reason, string walkReason, bool truncated, params (long Id, long? Parent, string Type, string Name, string? Data)[] nodes)
        {
            var id = $"dom-checkpoint-{token}-{++_checkpoint}";
            Add("browser.dom", "dom-checkpoint-started", $$"""
                "checkpointId":"{{id}}","reason":"{{reason}}","walkReason":"{{walkReason}}","maximumNodes":2147483647
                """);
            var index = 0;
            foreach (var node in nodes)
            {
                Add("browser.dom", "dom-checkpoint-node", $$"""
                    "checkpointId":"{{id}}","nodeIndex":{{index++}},"nodeId":{{node.Id}},
                    "parentNodeId":{{node.Parent?.ToString() ?? "null"}},"nodeType":"{{node.Type}}","nodeName":"{{node.Name}}"
                    """);
                if (node.Data is { } data)
                {
                    Add("browser.dom", "dom-checkpoint-node-character-data", $$"""
                        "checkpointId":"{{id}}","nodeId":{{node.Id}},"data":{{JsonSerializer.Serialize(data)}},
                        "dataLength":{{data.Length}},"dataTruncated":false,"maximumValueLength":2147483647
                        """);
                }
            }
            Add("browser.dom", "dom-checkpoint-completed", $$"""
                "checkpointId":"{{id}}","reason":"{{reason}}","nodeCount":{{nodes.Length}},"truncated":{{(truncated ? "true" : "false")}},
                "attributesTruncated":false,"characterDataCount":0
                """);
            return this;
        }

        public Records Insert(long container, long? previous, long id, string name, string? text = null)
        {
            var transition = $"dom-transition-{++_transition}";
            Add("browser.dom", "dom-node-inserted", $$"""
                "transitionId":"{{transition}}","insertionKind":"child","containerNodeId":{{container}},
                "nodeId":{{id}},"previousSiblingNodeId":{{previous?.ToString() ?? "null"}}
                """);
            Add("browser.dom", "dom-inserted-node", $$"""
                "insertionId":"{{transition}}","nodeIndex":0,"nodeId":{{id}},"parentNodeId":{{container}},
                "nodeType":"{{(text is null ? "element" : "text")}}","nodeName":"{{name}}"
                """);
            if (text is not null)
            {
                Add("browser.dom", "dom-inserted-node-character-data", $$"""
                    "insertionId":"{{transition}}","nodeId":{{id}},"data":{{JsonSerializer.Serialize(text)}},
                    "dataLength":{{text.Length}},"dataTruncated":false,"maximumValueLength":2147483647
                    """);
            }
            Add("browser.dom", "dom-insertion-completed", $$"""
                "insertionId":"{{transition}}","nodeCount":1,"attributeCount":0,"characterDataCount":{{(text is null ? 0 : 1)}},
                "shadowRootCount":0,"slotCount":0
                """);
            return this;
        }

        public Records Attribute(long id, string name, string? value) {
            Add("browser.dom", "dom-attribute-changed", $$"""
                "transitionId":"dom-transition-{{++_transition}}","nodeId":{{id}},"attributeNamespace":null,
                "attributeName":"{{name}}","attributeValue":{{(value is null ? "null" : JsonSerializer.Serialize(value))}},
                "attributeValueTruncated":false,"changeType":"{{(value is null ? "removed" : "set")}}"
                """);
            return this;
        }

        public Records Text(long id, string text)
        {
            Add("browser.dom", "dom-character-data-changed", $$"""
                "transitionId":"dom-transition-{{++_transition}}","nodeId":{{id}},"text":{{JsonSerializer.Serialize(text)}},
                "textTruncated":false,"previousText":null,"previousTextTruncated":false
                """);
            return this;
        }

        public Records Remove(long container, long id)
        {
            Add("browser.dom", "dom-node-removed", $$"""
                "transitionId":"dom-transition-{{++_transition}}","containerNodeId":{{container}},"nodeId":{{id}}
                """);
            return this;
        }

        public Records Layout(params (long Id, double Y)[] nodes)
        {
            var id = $"layout-changes-{token}-{++_changeSet}";
            Add("browser.layout", "layout-changes-started", $$"""
                "changeSetId":"{{id}}","layoutCheckpointId":null,"layoutZoomFactor":1,"viewPaintOffset":{"x":0,"y":0},
                "viewTransformNodeId":"1"
                """);
            Add("browser.layout", "layout-transform-node", """
                "transformNodeId":"1","parentTransformNodeId":null,"matrix":[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1],
                "flattensInheritedTransform":false
                """);
            foreach (var (node, y) in nodes)
            {
                Add("browser.layout", "layout-node-changed", $$"""
                    "changeSetId":"{{id}}","nodeId":{{node}},"transformNodeId":"1","y":{{y}}
                    """);
            }
            Add("browser.layout", "layout-scroll-offset-changed", $$"""
                "changeSetId":"{{id}}","nodeId":1,"scrollOffset":{"x":0,"y":{{_changeSet}}},"scrollOrigin":{"x":0,"y":0},
                "scrollTranslationNodeId":null
                """);
            Add("browser.layout", "layout-changes-completed", $$"""
                "changeSetId":"{{id}}"
                """);
            return this;
        }

        public Records LayoutWalk(string walkReason)
        {
            Add("browser.layout", "layout-checkpoint-started", $$"""
                "checkpointId":"layout-checkpoint-{{token}}-{{++_checkpoint}}","walkReason":"{{walkReason}}"
                """);
            return this;
        }

        public Records Interaction(long? focused)
        {
            var id = $"interaction-{token}-{++_checkpoint}";
            Add("browser.interaction", "interaction-checkpoint-started", $$"""
                "checkpointId":"{{id}}","focusedNodeId":{{focused?.ToString() ?? "null"}}
                """);
            Add("browser.interaction", "interaction-checkpoint-completed", $$"""
                "checkpointId":"{{id}}","textControlCount":0,"truncated":false,"maximumTextControls":64
                """);
            return this;
        }

        public Records Focus(long node)
        {
            Add("browser.interaction", "focus-changed", $$"""
                "previousNodeId":null,"requestedNodeId":{{node}},"focusedNodeId":{{node}},"outcome":"focused"
                """);
            return this;
        }

        // Listener and timer records name the document by its identity, with
        // no token; accessibility records by its token, with no identity.
        private void AddScript(string channel, string eventType, string body) =>
            Items.Add((channel, eventType, J($$"""
                {"context":{"browserInstanceId":"browser-1","processId":{{process}},"processType":"renderer",
                 "documentId":"{{document}}","documentToken":null},{{body}}}
                """)));

        public Records Listener(string id, long node, string eventName)
        {
            AddScript("browser.listener", "listener-registered", $$"""
                "listenerId":"{{id}}","eventName":"{{eventName}}","capture":false,"once":false,"passive":false,
                "registrationKind":"add-event-listener","target":{"kind":"node","nodeId":{{node}},"documentId":"{{document}}"}
                """);
            return this;
        }

        public Records RemoveListener(string id)
        {
            AddScript("browser.listener", "listener-removed", $$"""
                "listenerId":"{{id}}","eventName":"click","capture":false
                """);
            return this;
        }

        public Records Timer(string eventType, string id, string kind, double delay = 50)
        {
            AddScript("browser.timer", eventType, $$"""
                "timerId":"{{id}}","timerKind":"{{kind}}","requestedDelayMilliseconds":{{delay}},"effectiveDelayMilliseconds":{{delay}}
                """);
            return this;
        }

        public Records Accessibility(params (long Dom, string Role)[] nodes)
        {
            var id = $"accessibility-{token}-{++_checkpoint}";
            Items.Add(("browser.accessibility", "accessibility-checkpoint-started", J($$"""
                {"context":{"browserInstanceId":"browser-1","processId":{{process}},"processType":"renderer",
                 "documentId":null,"documentToken":"{{token}}"},"checkpointId":"{{id}}","reason":"renderer-serialization"}
                """)));
            var index = 0;
            foreach (var (dom, role) in nodes)
            {
                Items.Add(("browser.accessibility", "accessibility-checkpoint-node", J($$"""
                    {"context":{"browserInstanceId":"browser-1","processId":{{process}},"processType":"renderer",
                     "documentId":null,"documentToken":"{{token}}"},"checkpointId":"{{id}}","nodeIndex":{{index++}},
                     "accessibilityNodeId":{{dom + 1000}},"domNodeId":{{dom}},"roleName":"{{role}}","name":"n{{dom}}"}
                    """)));
            }
            Items.Add(("browser.accessibility", "accessibility-checkpoint-completed", J($$"""
                {"context":{"browserInstanceId":"browser-1","processId":{{process}},"processType":"renderer",
                 "documentId":null,"documentToken":"{{token}}"},"checkpointId":"{{id}}","nodeCount":{{nodes.Length}},"truncated":false}
                """)));
            return this;
        }

        public Records Omission(string channel)
        {
            Items.Add((channel, "collector-omission", J($$"""
                {"context":{"browserInstanceId":"browser-1","processId":{{process}},"processType":"renderer","documentToken":null},
                 "reason":"evidence-write-failed","count":1}
                """)));
            return this;
        }
    }

    private static readonly (long, long?, string, string, string?)[] Page =
    [
        (1, null, "document", "#document", null),
        (2, 1, "element", "HTML", null),
        (3, 2, "element", "BODY", null),
        (4, 3, "text", "#text", "Hello"),
    ];

    private static BrowserStateBuilder Build(IEnumerable<(string Channel, string EventType, JsonElement Payload)> records, long firstKey = 0)
    {
        var builder = new BrowserStateBuilder();
        var key = firstKey;
        foreach (var (channel, eventType, payload) in records)
        {
            builder.Apply(key, key * 1_000, channel, eventType, payload);
            key++;
        }
        return builder;
    }

    [Fact]
    public void RebuildsTheTreeLayoutAndInteractionOfADocument()
    {
        var records = new Records("token-a")
            .Walk("post-mutation", "first", false, Page)
            .Insert(3, 4, 5, "DIV")
            .Insert(5, null, 6, "#text", "World")
            .Attribute(5, "class", "greeting")
            .Text(4, "Hi")
            .Layout((3, 10), (5, 20))
            .Layout((5, 30))
            .Interaction(5)
            .Focus(3);

        var document = Assert.Single(Build(records.Items).Documents.Values);

        var tree = document.Dom!;
        Assert.Equal([4, 5], tree.Nodes[3].Children);
        Assert.Equal([6], tree.Nodes[5].Children);
        Assert.Equal("greeting", tree.Nodes[5].Attributes["class"]);
        Assert.Equal("Hi", tree.Nodes[4].Data);
        Assert.Equal("World", tree.Nodes[6].Data);
        Assert.Equal(30, document.Layout.Nodes[5].GetProperty("y").GetDouble());
        Assert.Equal(10, document.Layout.Nodes[3].GetProperty("y").GetDouble());
        Assert.Equal(2, document.Layout.ScrollOffsets[1].GetProperty("scrollOffset").GetProperty("y").GetInt32());
        Assert.Equal(1, document.Layout.LayoutZoomFactor);
        Assert.False(document.Layout.IsOpen);
        Assert.Equal(2, document.Interaction.Checkpoint.Count);
        Assert.Equal("focus-changed", Assert.Single(document.Interaction.Changes).EventType);
        Assert.Equal("page-1", document.PageId);
        Assert.Equal(3440, document.ProcessId);
        // The document has not finished parsing, so nodes the parser adds are missing.
        Assert.Equal(BrowserStateCompleteness.Parsing, document.DomCompleteness);
        Assert.Equal(BrowserStateCompleteness.Complete, document.LayoutCompleteness);
        Assert.Equal(BrowserStateCompleteness.Complete, document.InteractionCompleteness);
    }

    [Fact]
    public void StatesHowCompleteEachPartIs()
    {
        var empty = Assert.Single(Build(new Records("token-a").Insert(3, null, 5, "DIV").Items).Documents.Values);
        Assert.Null(empty.Dom);
        Assert.Equal(BrowserStateCompleteness.NotWalked, empty.DomCompleteness);
        Assert.Equal(BrowserStateCompleteness.NotWalked, empty.LayoutCompleteness);
        Assert.Equal(BrowserStateCompleteness.NotWalked, empty.InteractionCompleteness);

        var parsed = Assert.Single(Build(new Records("token-a")
            .Walk("post-mutation", "first", false, Page)
            .Walk("finished-parsing", "finished-parsing", false, Page)
            .Walk("post-mutation", "check", false, Page).Items).Documents.Values);
        Assert.Equal(BrowserStateCompleteness.Complete, parsed.DomCompleteness);

        var cut = Assert.Single(Build(new Records("token-a")
            .Walk("finished-parsing", "first", false, Page)
            .Walk("post-mutation", "check", true, Page).Items).Documents.Values);
        Assert.Null(cut.Dom);
        Assert.Equal(BrowserStateCompleteness.WalkCut, cut.DomCompleteness);

        // A lost record marks every walked part of the process's documents,
        // until the next walk for the DOM and interaction state, and for the
        // rest of the document for the layout state.
        var lost = new Records("token-a")
            .Walk("finished-parsing", "first", false, Page)
            .Layout((3, 10))
            .Interaction(null)
            .Omission("browser.dom")
            .Omission("browser.layout")
            .Omission("browser.interaction");
        var afterLoss = Assert.Single(Build(lost.Items).Documents.Values);
        Assert.Equal(BrowserStateCompleteness.AfterLoss, afterLoss.DomCompleteness);
        Assert.Equal(BrowserStateCompleteness.AfterLoss, afterLoss.LayoutCompleteness);
        Assert.Equal(BrowserStateCompleteness.AfterLoss, afterLoss.InteractionCompleteness);
        lost.Walk("post-mutation", "after-loss", false, Page).Layout((3, 11)).Interaction(null);
        var walked = Assert.Single(Build(lost.Items).Documents.Values);
        Assert.Equal(BrowserStateCompleteness.Complete, walked.DomCompleteness);
        Assert.Equal(BrowserStateCompleteness.AfterLoss, walked.LayoutCompleteness);
        Assert.Equal(BrowserStateCompleteness.Complete, walked.InteractionCompleteness);

        var layoutWalk = Assert.Single(Build(new Records("token-a").Layout((3, 1)).LayoutWalk("after-loss").Items).Documents.Values);
        Assert.Equal(BrowserStateCompleteness.AfterLoss, layoutWalk.LayoutCompleteness);
    }

    [Fact]
    public void KeysADocumentByItsTokenAndDocumentIdentity()
    {
        var first = new Records("token-a", "doc-1").Walk("finished-parsing", "first", false, Page);
        var second = new Records("token-a", "doc-2").Walk("finished-parsing", "first", false, Page[..2]);
        var builder = Build(first.Items.Concat(second.Items));
        Assert.Equal(["token-a doc-1", "token-a doc-2"], builder.Documents.Keys.Order());
        Assert.Equal(4, builder.Documents["token-a doc-1"].Dom!.Nodes.Count);
        Assert.Equal(2, builder.Documents["token-a doc-2"].Dom!.Nodes.Count);
    }

    [Fact]
    public void ASnapshotAndTheRecordsAfterItGiveTheStateOfEveryRecord()
    {
        var records = new Records("token-a")
            .Listener("listener-0", 3, "click")
            .Walk("finished-parsing", "first", false, Page)
            .Layout((3, 10), (4, 12))
            .Interaction(3)
            .Timer("timer-scheduled", "timer-1", "timeout")
            .Accessibility((3, "genericContainer"))
            .Insert(3, 4, 5, "DIV");
        var split = records.Items.Count;
        records.Insert(5, null, 6, "#text", "late \u0000 text \"quoted\"")
            .Attribute(3, "data-x", "1")
            .Attribute(3, "data-x", null)
            .Remove(3, 4)
            .Layout((5, 40))
            .Focus(5)
            .Listener("listener-9", 5, "click")
            .Timer("timer-scheduled", "timer-9", "interval")
            .Timer("timer-fired", "timer-9", "interval")
            .Accessibility((5, "button"))
            .Omission("browser.layout");

        var whole = Build(records.Items);
        var before = Build(records.Items.Take(split));
        var snapshot = BrowserStateSnapshot.Serialize(before.Documents.Values.Single());
        var read = BrowserStateSnapshot.Read(snapshot);
        Assert.Equal(snapshot, BrowserStateSnapshot.Serialize(read));

        var resumed = new BrowserStateBuilder();
        resumed.Load(read);
        var key = 0L;
        foreach (var (channel, eventType, payload) in records.Items)
        {
            // Every record is offered; those the snapshot holds are skipped.
            resumed.Apply(key, key * 1_000, channel, eventType, payload);
            key++;
        }

        Assert.Equal(split, resumed.RecordsSkipped);
        Assert.Equal(
            Encoding.UTF8.GetString(BrowserStateSnapshot.Serialize(whole.Documents.Values.Single())),
            Encoding.UTF8.GetString(BrowserStateSnapshot.Serialize(resumed.Documents.Values.Single())));
    }

    [Fact]
    public void KeepsTheListenersTimersAndAccessibilityOfADocument()
    {
        // A listener and a timer before the document's first DOM record are
        // held until it, and the document's first record is then theirs.
        var records = new Records("token-a")
            .Listener("listener-1", 3, "click")
            .Timer("timer-scheduled", "timer-1", "timeout")
            .Walk("finished-parsing", "first", false, Page)
            .Listener("listener-2", 3, "keydown")
            .Listener("listener-3", 2, "focus")
            .RemoveListener("listener-3")
            .Timer("timer-scheduled", "timer-2", "interval", 1000)
            .Timer("timer-scheduled", "timer-3", "timeout")
            .Timer("timer-fired", "timer-1", "timeout")
            .Timer("timer-fired", "timer-2", "interval", 1000)
            .Timer("timer-scheduled", "timer-4", "animation-frame", 0)
            .Timer("timer-cancelled", "timer-3", "timeout")
            .Accessibility((3, "genericContainer"), (2, "rootWebArea"))
            .Accessibility((3, "button"));

        var builder = Build(records.Items);
        var document = Assert.Single(builder.Documents.Values);

        Assert.Equal(0, document.FirstEventKey);
        Assert.Equal(["listener-1", "listener-2"], document.Script.Listeners.Keys.Order());
        Assert.Equal(["timer-2", "timer-4"], document.Script.Timers.Keys.Order());
        // An interval timer keeps running; its last run is kept.
        Assert.Equal(document.Script.Timers["timer-2"].ScheduledTime + 3_000, document.Script.Timers["timer-2"].LastRunTime);
        Assert.Equal("button", document.Accessibility.Nodes[3].Record.GetProperty("roleName").GetString());
        Assert.Equal("rootWebArea", document.Accessibility.Nodes[2].Record.GetProperty("roleName").GetString());
        Assert.Equal(BrowserStateCompleteness.Complete, document.ScriptCompleteness);
        Assert.Equal(document.Key, builder.KeyOf("browser.listener", records.Items[0].Payload));
        Assert.Equal(document.Key, builder.KeyOf("browser.accessibility", records.Items[^1].Payload));

        records.Omission("browser.timer");
        var lost = Build(records.Items).Documents.Values.Single();
        Assert.Equal(BrowserStateCompleteness.AfterLoss, lost.ScriptCompleteness);
        Assert.Equal(BrowserStateCompleteness.Complete, lost.AccessibilityCompleteness);
    }

    [Fact]
    public void ARecordOfAnotherDocumentOfTheProcessIsNotItsListener()
    {
        var a = new Records("token-a", "doc-1").Walk("finished-parsing", "first", false, Page).Listener("listener-1", 3, "click");
        var b = new Records("token-a", "doc-2").Listener("listener-2", 3, "click");
        var builder = Build(a.Items.Concat(b.Items));
        var document = Assert.Single(builder.Documents.Values);
        Assert.Equal(["listener-1"], document.Script.Listeners.Keys);
    }

    [Fact]
    public void AFrameShowsTheLastUpdatePresentedAtOrBeforeItsComposition()
    {
        (long, long)[] updates = [(100, 90), (200, 150), (260, 240), (400, 300)];

        var presented = RecordingFileBrowserState.FrameBasis(updates, 250);
        Assert.Equal("presented", presented.Basis);
        Assert.Equal(150, presented.CutTime);
        Assert.Equal(200, presented.PresentedTime);

        Assert.Equal(240, RecordingFileBrowserState.FrameBasis(updates, 260).CutTime);

        var none = RecordingFileBrowserState.FrameBasis(updates, 99);
        Assert.Equal("by-time", none.Basis);
        Assert.Equal(99, none.CutTime);
        Assert.Equal("by-time", RecordingFileBrowserState.FrameBasis([], 500).Basis);
    }

    // Records of two documents over 40 s, with a change every 50 ms.
    private static List<(string Channel, string EventType, JsonElement Payload)> Session(out int count)
    {
        var a = new Records("token-a", "doc-1", 3440).Listener("listener-early", 3, "load").Walk("finished-parsing", "first", false, Page).Layout((3, 0)).Interaction(null);
        var b = new Records("token-b", "doc-9", 5120).Walk("finished-parsing", "first", false, Page).Layout((3, 0)).Interaction(null);
        var all = new List<(string, string, JsonElement)>();
        all.AddRange(a.Items);
        all.AddRange(b.Items);
        a.Items.Clear();
        b.Items.Clear();
        for (var step = 0; step < 800; step++)
        {
            var records = step % 3 == 0 ? b : a;
            var node = 100 + step;
            records.Insert(3, null, node, "P");
            if (step % 4 == 0)
            {
                records.Layout((node, step));
            }
            if (step % 7 == 0)
            {
                records.Text(4, $"text {step}");
            }
            if (step % 9 == 0)
            {
                records.Interaction(node).Focus(3);
            }
            if (step % 11 == 0 && step > 20)
            {
                records.Remove(3, node - 11);
            }
            if (step % 5 == 0)
            {
                records.Listener($"listener-{step}", node, "click").Timer("timer-scheduled", $"timer-{step}", "timeout");
            }
            if (step % 10 == 5)
            {
                records.RemoveListener($"listener-{step - 5}").Timer("timer-fired", $"timer-{step - 5}", "timeout")
                    .Accessibility((node, "paragraph"));
            }
            all.AddRange(records.Items);
            records.Items.Clear();
        }
        count = all.Count;
        return all;
    }

    private async Task<string> WriteSessionAsync(string name, long spacing)
    {
        var path = Path.Combine(_directory, name);
        var collector = Collector("test.browser", "browser.dom", "browser.layout", "browser.interaction", "browser.listener", "browser.timer", "browser.accessibility");
        var records = Session(out _);
        using var target = new RecordingFileBatchTarget(path, Recording, new RecordingFileWriterOptions { ChunkBytes = 32 * 1024, ChunkInterval = TimeSpan.Zero });
        for (var start = 0; start < records.Count; start += 200)
        {
            var batch = records.Skip(start).Take(200).Select((item, index) =>
            {
                var key = start + index;
                var record = Event(SessionId, collector, (ulong)key + 1, key * spacing, item.Channel, item.EventType) with
                {
                    Payload = item.Payload
                };
                return new BufferedEvent(key, record, item.Payload.GetRawText());
            }).ToArray();
            Assert.Empty(await target.WriteAsync(new EventBatch(batch, [], []), TestContext.Current.CancellationToken));
        }
        target.Finish();
        Assert.NotNull(target.StateSummary);
        Assert.Null(target.StateSummary!.Stopped);
        Assert.True(target.StateSummary.Snapshots > 0);
        return path;
    }

    // Compares two states document by document, so that no one value holds
    // the whole state of a long recording.
    private static bool SameState(BrowserStateAt first, BrowserStateAt second)
    {
        if (first.Documents.Count != second.Documents.Count)
        {
            return false;
        }
        var left = first.Documents.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var right = second.Documents.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        for (var position = 0; position < left.Length; position++)
        {
            if (!BrowserStateSnapshot.Serialize(left[position].State!).AsSpan()
                    .SequenceEqual(BrowserStateSnapshot.Serialize(right[position].State!)))
            {
                return false;
            }
        }
        return true;
    }

    private static string Describe(BrowserStateAt state) =>
        string.Join("\n", state.Documents
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => Encoding.UTF8.GetString(BrowserStateSnapshot.Serialize(item.State!))));

    // Document a changes every 50 ms for 120 s. Document b, in another
    // process, changes for its first 20 s, has a lost-record notice for its
    // process at 60 s, and changes again from 90 s to 100 s.
    private static List<((string Channel, string EventType, JsonElement Payload) Item, long Time)> IdleSession()
    {
        var a = new Records("token-a", "doc-1", 3440);
        var b = new Records("token-b", "doc-9", 5120);
        var all = new List<((string, string, JsonElement), long)>();
        void Take(Records records, long time)
        {
            all.AddRange(records.Items.Select(item => (item, time)));
            records.Items.Clear();
        }
        Take(a.Walk("finished-parsing", "first", false, Page).Layout((3, 0)).Interaction(null), 0);
        Take(b.Walk("finished-parsing", "first", false, Page).Layout((3, 0)).Interaction(null), 1_000_000);
        for (var step = 1; step <= 2400; step++)
        {
            var time = step * 50_000_000L;
            Take(a.Insert(3, null, 1000 + step, "P").Layout((1000 + step, step)), time);
            if (time <= 20_000_000_000 || (time >= 90_000_000_000 && time <= 100_000_000_000))
            {
                if (step % 4 == 0)
                {
                    Take(b.Insert(3, null, 5000 + step, "LI").Text(4, $"b {step}"), time + 1_000_000);
                }
            }
            if (time == 60_000_000_000)
            {
                Take(b.Omission("browser.layout"), time + 2_000_000);
            }
        }
        return all;
    }

    private async Task<(string Path, RecordingFileStateSummary Summary)> WriteTimedAsync(
        string name,
        List<((string Channel, string EventType, JsonElement Payload) Item, long Time)> records)
    {
        var path = Path.Combine(_directory, name);
        var collector = Collector("test.browser", "browser.dom", "browser.layout", "browser.interaction", "browser.listener", "browser.timer", "browser.accessibility");
        using var target = new RecordingFileBatchTarget(path, Recording, new RecordingFileWriterOptions { ChunkBytes = 32 * 1024, ChunkInterval = TimeSpan.Zero });
        for (var start = 0; start < records.Count; start += 200)
        {
            var batch = records.Skip(start).Take(200).Select((entry, index) =>
            {
                var key = start + index;
                var record = Event(SessionId, collector, (ulong)key + 1, entry.Time, entry.Item.Channel, entry.Item.EventType) with
                {
                    Payload = entry.Item.Payload
                };
                return new BufferedEvent(key, record, entry.Item.Payload.GetRawText());
            }).ToArray();
            Assert.Empty(await target.WriteAsync(new EventBatch(batch, [], []), TestContext.Current.CancellationToken));
        }
        target.Finish();
        Assert.NotNull(target.StateSummary);
        Assert.Null(target.StateSummary!.Stopped);
        return (path, target.StateSummary);
    }

    [Fact]
    public async Task ListenersBeforeADocumentsFirstDomRecordAndOfADepartedDocumentAreRead()
    {
        var token = TestContext.Current.CancellationToken;
        var records = IdleSession();
        // Document c has a listener and a timer 11 s before its first DOM
        // record, after snapshots of the others; b, departed, gains a
        // listener at 80 s, which returns it.
        var c = new Records("token-c", "doc-3", 3440);
        var b = new Records("token-b", "doc-9", 5120);
        var added = new List<((string Channel, string EventType, JsonElement Payload) Item, long Time)>();
        void Take(Records items, long time)
        {
            added.AddRange(items.Items.Select(item => (item, time)));
            items.Items.Clear();
        }
        Take(c.Listener("listener-1", 3, "click").Timer("timer-scheduled", "timer-1", "interval", 1000), 40_000_000_001);
        Take(c.Walk("finished-parsing", "first", false, Page), 51_000_000_001);
        Take(c.Timer("timer-fired", "timer-1", "interval", 1000), 70_000_000_001);
        Take(b.Listener("listener-7", 3, "keydown"), 80_000_000_001);
        var (path, summary) = await WriteTimedAsync(
            "script.mcap",
            [.. records.Concat(added).OrderBy(entry => entry.Time)]);
        Assert.True(summary.Departures >= 1);

        using var reader = RecordingFileReader.Open(path);
        var state = new RecordingFileBrowserState(reader, null);
        foreach (var time in new[] { 45_000_000_000L, 52_000_000_000L, 65_000_000_000L, 75_000_000_000L, 85_000_000_000L, 110_000_000_000L })
        {
            var fast = state.At(time, cancellationToken: token);
            var full = state.At(time, useSnapshots: false, cancellationToken: token);
            Assert.Equal(Describe(full), Describe(fast));
            Assert.Equal("snapshots", fast.Cost.Source);
            // Each document read alone, from its own window of records.
            foreach (var document in full.Documents)
            {
                var alone = state.At(time, new HashSet<string>(StringComparer.Ordinal) { document.Key }, cancellationToken: token);
                Assert.Equal(
                    Encoding.UTF8.GetString(BrowserStateSnapshot.Serialize(document.State!)),
                    Encoding.UTF8.GetString(BrowserStateSnapshot.Serialize(alone.Documents.Single(item => item.Key == document.Key).State!)));
            }
            var documentC = fast.Documents.SingleOrDefault(item => item.Key.StartsWith("token-c", StringComparison.Ordinal));
            if (time > 51_000_000_001)
            {
                Assert.Equal(["listener-1"], documentC!.State!.Script.Listeners.Keys);
                Assert.Equal(time > 70_000_000_001 ? 70_000_000_001 : null, documentC.State.Script.Timers["timer-1"].LastRunTime);
            }
            else
            {
                Assert.Null(documentC);
            }
            var documentB = fast.Documents.Single(item => item.Key.StartsWith("token-b", StringComparison.Ordinal));
            Assert.Equal(time > 80_000_000_001 ? 1 : 0, documentB.State!.Script.Listeners.Count);
        }
    }

    [Fact]
    public async Task ADocumentWithoutRecordsLeavesTheStateThreadAndReturns()
    {
        var token = TestContext.Current.CancellationToken;
        var (path, summary) = await WriteTimedAsync("idle.mcap", IdleSession());

        // b left after 30 s without records, returned for the notice at 60 s,
        // left again, and returned at 90 s.
        Assert.Equal(2, summary.Documents);
        Assert.True(summary.Departures >= 2, $"{summary.Departures} departures");
        Assert.True(summary.Returns >= 2, $"{summary.Returns} returns");
        Assert.True(summary.LargestDepartedBytes > 0);

        using var reader = RecordingFileReader.Open(path);
        var state = new RecordingFileBrowserState(reader, null);
        for (var time = 5_000_000_000L; time <= 125_000_000_000; time += 5_000_000_000)
        {
            var fast = state.At(time, cancellationToken: token);
            var full = state.At(time, useSnapshots: false, cancellationToken: token);
            Assert.Equal(Describe(full), Describe(fast));
            var b = Assert.Single(fast.Documents, item => item.Key.StartsWith("token-b", StringComparison.Ordinal)).State!;
            Assert.Equal(
                time > 60_000_000_000 ? BrowserStateCompleteness.AfterLoss : BrowserStateCompleteness.Complete,
                b.LayoutCompleteness);
        }
    }

    [Fact]
    public async Task IndexRecordsListOnlyTheDocumentsThatChanged()
    {
        var (path, _) = await WriteTimedAsync("listing.mcap", IdleSession());
        using var reader = RecordingFileReader.Open(path);
        var listings = new List<(long Time, string[] Keys)>();
        foreach (var message in reader.ReadAll().Where(message => message.Channel.Topic == RecordingFileStateRecorder.IndexTopic))
        {
            using var json = JsonDocument.Parse(message.Data);
            if (json.RootElement.GetProperty("kind").GetString() == "state-index")
            {
                Assert.Equal("changed", json.RootElement.GetProperty("listing").GetString());
                listings.Add((
                    json.RootElement.GetProperty("time").GetInt64(),
                    [.. json.RootElement.GetProperty("documents").EnumerateArray().Select(item => item.GetProperty("documentKey").GetString()!)]));
            }
        }
        Assert.True(listings.Count > 50);
        // Between b's departure after 50 s and the notice at 60 s, and from
        // its second departure until 90 s, only a is listed.
        var quiet = listings.Where(item => item.Time is > 52_000_000_000 and < 59_000_000_000 or > 62_000_000_000 and < 89_000_000_000).ToArray();
        Assert.NotEmpty(quiet);
        Assert.All(quiet, item => Assert.DoesNotContain(item.Keys, key => key.StartsWith("token-b", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task OnlyTheDocumentsAskedForAreRead()
    {
        var token = TestContext.Current.CancellationToken;
        var (path, _) = await WriteTimedAsync("asked.mcap", IdleSession());
        using var reader = RecordingFileReader.Open(path);
        var state = new RecordingFileBrowserState(reader, null);
        foreach (var time in new[] { 10_000_000_000L, 70_000_000_000, 95_000_000_000, 125_000_000_000 })
        {
            var full = state.At(time, useSnapshots: false, cancellationToken: token);
            var keys = full.Documents.Select(item => item.Key).ToArray();

            var named = state.At(time, load: new HashSet<string>(), cancellationToken: token);
            Assert.Equal(keys, named.Documents.Select(item => item.Key));
            Assert.All(named.Documents, item => Assert.Null(item.State));
            Assert.Equal(0, named.Cost.SnapshotsRead);

            var b = keys.Single(key => key.StartsWith("token-b", StringComparison.Ordinal));
            var one = state.At(time, load: new HashSet<string> { b }, cancellationToken: token);
            Assert.Equal(keys, one.Documents.Select(item => item.Key));
            var read = Assert.Single(one.Documents, item => item.State is not null);
            Assert.Equal(b, read.Key);
            Assert.Equal(
                BrowserStateSnapshot.Serialize(full.Documents.Single(item => item.Key == b).State!),
                BrowserStateSnapshot.Serialize(read.State!));
        }
    }

    [Fact]
    public async Task TheStateReadWithSnapshotsEqualsTheStateReadFromEveryRecord()
    {
        // 25 ms apart, the records span more than 100 s of recording time.
        var path = await WriteSessionAsync("recording.mcap", 25_000_000);

        using var reader = RecordingFileReader.Open(path);
        Assert.Contains(reader.Chunks, chunk => chunk.Stream == "browser-state");
        Assert.DoesNotContain(reader.Chunks, chunk => chunk.Stream == "browser");
        Assert.Contains(reader.Chunks, chunk => chunk.Stream == RecordingFileStateRecorder.SnapshotStream);
        var state = new RecordingFileBrowserState(reader, null);
        Assert.True(state.HasSnapshots);
        var end = reader.Chunks.Max(chunk => chunk.EndTime);
        var compared = 0;
        for (var time = 0L; time <= end + 1; time += end / 23)
        {
            var fast = state.At(time, cancellationToken: TestContext.Current.CancellationToken);
            var full = state.At(time, useSnapshots: false, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("records", full.Cost.Source);
            Assert.Equal(Describe(full), Describe(fast));
            if (time > 30_000_000_000)
            {
                Assert.Equal("snapshots", fast.Cost.Source);
                Assert.True(fast.Cost.SnapshotsRead > 0);
                // At most an interval and a sweep of records are read.
                Assert.True(fast.Cost.ScanStartTime >= time - RecordingFileStateRecorder.SnapshotInterval - 3 * RecordingFileStateRecorder.SweepInterval,
                    $"at {time} the scan started at {fast.Cost.ScanStartTime}");
                Assert.True(fast.Cost.RecordsRead < full.Cost.RecordsRead);
            }
            compared++;
        }
        Assert.True(compared > 20);
    }

    [Fact]
    public async Task AFileCutShortIsReadFromItsSnapshotsBeforeTheCut()
    {
        var path = await WriteSessionAsync("whole.mcap", 25_000_000);
        var cutPath = Path.Combine(_directory, "cut.mcap");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(cutPath, bytes[..(bytes.Length * 6 / 10)], TestContext.Current.CancellationToken);

        using var reader = RecordingFileReader.Open(cutPath);
        Assert.NotNull(reader.Incomplete);
        var state = new RecordingFileBrowserState(reader, null);
        Assert.True(state.HasSnapshots);
        var end = reader.Chunks.Where(chunk => chunk.Stream == "browser-state").Max(chunk => chunk.EndTime);
        foreach (var time in new[] { end / 3, end / 2, end })
        {
            var token = TestContext.Current.CancellationToken;
            Assert.Equal(Describe(state.At(time, useSnapshots: false, cancellationToken: token)), Describe(state.At(time, cancellationToken: token)));
        }
    }

    [Fact]
    public void TheStateThreadStopsWhenItFallsBehindAndPassingNeverWaits()
    {
        var written = new List<string>();
        using var blocked = new ManualResetEventSlim(false);
        var recorder = new RecordingFileStateRecorder(
            (topic, _, _) =>
            {
                blocked.Wait(TestContext.Current.CancellationToken);
                lock (written)
                {
                    written.Add(topic);
                }
            },
            queueLimitBytes: 64 * 1024);
        var collector = Collector("test.browser", "browser.dom");
        var records = Session(out _);
        string? stopped = null;
        var passing = Stopwatch.StartNew();
        for (var start = 0; start < records.Count && stopped is null; start += 50)
        {
            var batch = records.Skip(start).Take(50).Select((item, index) =>
            {
                var key = start + index;
                var record = Event(SessionId, collector, (ulong)key + 1, key * 25_000_000L, item.Channel, item.EventType) with
                {
                    Payload = item.Payload
                };
                return new BufferedEvent(key, record, item.Payload.GetRawText());
            }).ToArray();
            stopped = recorder.Pass(batch);
        }
        passing.Stop();

        Assert.NotNull(stopped);
        Assert.Contains("fell behind", stopped);
        Assert.Equal(stopped, recorder.Stopped);
        Assert.True(passing.Elapsed < TimeSpan.FromSeconds(10));
        blocked.Set();
        var summary = recorder.Complete();
        Assert.Equal(stopped, summary.Stopped);
    }

    // Set RECORDER_STATE_FILE to a recording.mcap to check the rebuilt state
    // of its browser documents; the report is written to
    // RECORDER_STATE_REPORT. A file made before snapshots were recorded is
    // first written again through the batch target, which makes them, to
    // RECORDER_STATE_COPY or a temporary file.
    [Fact]
    public async Task ChecksTheStateOfARecordingFile()
    {
        if (Environment.GetEnvironmentVariable("RECORDER_STATE_FILE") is not { } source)
        {
            return;
        }
        var token = TestContext.Current.CancellationToken;
        var report = new StringBuilder();
        var path = source;
        bool hasSnapshots;
        using (var original = RecordingFileReader.Open(source))
        {
            hasSnapshots = original.Chunks.Any(chunk => chunk.Stream == RecordingFileStateRecorder.IndexStream);
            report.AppendLine($"file: {source}, {new FileInfo(source).Length} bytes, snapshots recorded: {hasSnapshots}");
            if (!hasSnapshots)
            {
                path = Environment.GetEnvironmentVariable("RECORDER_STATE_COPY") ??
                    Path.Combine(_directory, "with-snapshots.mcap");
                var copying = Stopwatch.StartNew();
                var summary = await CopyAsync(original, path, token);
                report.AppendLine($"written again with snapshots in {copying.Elapsed.TotalSeconds:F1} s: {path}, {new FileInfo(path).Length} bytes");
                report.AppendLine($"state thread: {JsonSerializer.Serialize(summary)}");
            }
            else
            {
                // The state thread's own records, written while recording.
                foreach (var message in original.ReadAll())
                {
                    if (message.Channel.Topic != RecordingFileStateRecorder.IndexTopic)
                    {
                        continue;
                    }
                    using var json = JsonDocument.Parse(message.Data);
                    var kind = json.RootElement.GetProperty("kind").GetString();
                    if (kind is "state-summary" or "state-stopped")
                    {
                        report.AppendLine($"state thread, as recorded: {json.RootElement.GetRawText()}");
                    }
                }
                var timings = Path.Combine(Path.GetDirectoryName(source)!, "database-writer-timings.json");
                if (File.Exists(timings))
                {
                    using var json = JsonDocument.Parse(File.ReadAllBytes(timings));
                    foreach (var stage in json.RootElement.GetProperty("stages").EnumerateArray())
                    {
                        if (stage.GetProperty("stage").GetString() == "complete.state")
                        {
                            report.AppendLine($"stopping the state thread: {stage.GetProperty("totalMilliseconds").GetDouble()} ms");
                        }
                    }
                }
            }
        }

        using var reader = RecordingFileReader.Open(path);
        var (index, derived) = RecordingFilePlayback.ReadIndex(reader, token);
        report.AppendLine($"playback index derived: {derived ?? "no"}");
        foreach (var stream in reader.Chunks.GroupBy(chunk => chunk.Stream).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            report.AppendLine(
                $"stream {stream.Key}: {stream.Count()} chunks, {stream.Sum(chunk => chunk.CompressedSize)} bytes compressed, {stream.Sum(chunk => chunk.UncompressedSize)} uncompressed");
        }
        var opening = Stopwatch.StartNew();
        var state = new RecordingFileBrowserState(reader, index);
        report.AppendLine($"opened in {opening.Elapsed.TotalMilliseconds:F0} ms: {state.IndexRecordCount} index records, stopped: {state.StateStopped ?? "no"}");

        var browserChunks = reader.Chunks.Where(chunk => chunk.Stream == "browser-state").ToArray();
        var start = browserChunks.Min(chunk => chunk.StartTime);
        var end = browserChunks.Max(chunk => chunk.EndTime);
        var samples = int.TryParse(Environment.GetEnvironmentVariable("RECORDER_STATE_SAMPLES"), out var count) ? count : 8;
        var reportPath = Environment.GetEnvironmentVariable("RECORDER_STATE_REPORT") ??
            Path.Combine(Path.GetTempPath(), "browser-state-report.txt");
        var differing = 0;
        void Compare(string what, BrowserStateAt fast, BrowserStateAt full)
        {
            var equal = SameState(fast, full);
            if (!equal)
            {
                differing++;
            }
            var bases = string.Join(", ", fast.Documents.GroupBy(item => item.Basis.Basis).Select(group => $"{group.Key} {group.Count()}"));
            var parts = string.Join(", ", fast.Documents
                .GroupBy(item => $"{BrowserStateSnapshot.Name(item.State!.DomCompleteness)}/{BrowserStateSnapshot.Name(item.State!.LayoutCompleteness)}/{BrowserStateSnapshot.Name(item.State!.InteractionCompleteness)}")
                .Select(group => $"{group.Key} {group.Count()}"));
            report.AppendLine(
                $"{what}: {(equal ? "equal" : "DIFFERENT")}; {fast.Documents.Count} documents, {fast.Documents.Sum(item => item.State!.Dom?.Nodes.Count ?? 0)} DOM nodes, {fast.Documents.Sum(item => item.State!.Layout.Nodes.Count)} layout nodes; basis {bases}; dom/layout/interaction {parts}");
            report.AppendLine(
                $"  with snapshots {fast.Cost.Milliseconds:F0} ms ({fast.Cost.Source}, {fast.Cost.SnapshotsRead} snapshots, {fast.Cost.HeldStatesRead} held, {fast.Cost.ChunksRead} chunks, {fast.Cost.RecordsRead} records read, {fast.Cost.RecordsApplied} applied, scan from {fast.Cost.ScanStartTime}); from every record {full.Cost.Milliseconds:F0} ms ({full.Cost.ChunksRead} chunks, {full.Cost.RecordsRead} records)");
            // Written after each sample, so that a check that fails part way
            // leaves what it found.
            File.WriteAllText(reportPath, report.ToString());
        }
        var none = new HashSet<string>();
        for (var sample = 0; sample <= samples; sample++)
        {
            var time = start + (end - start) * sample / samples;
            Compare($"time {time}", state.At(time, cancellationToken: token), state.At(time, useSnapshots: false, cancellationToken: token));
            var named = state.At(time, load: none, cancellationToken: token);
            report.AppendLine($"  documents named without their state: {named.Documents.Count} in {named.Cost.Milliseconds:F0} ms");
            File.WriteAllText(reportPath, report.ToString());
        }
        var frames = index.FrameCompositions;
        for (var sample = 0; sample < Math.Min(samples, frames.Count); sample++)
        {
            var frame = frames[(int)((long)(frames.Count - 1) * sample / Math.Max(1, samples - 1))];
            var full = state.AtFrame(frame.FrameNanoseconds, useSnapshots: false, cancellationToken: token);
            Compare(
                $"frame {frame.FrameNanoseconds} composed {frame.CompositedNanoseconds}",
                state.AtFrame(frame.FrameNanoseconds, cancellationToken: token),
                full);

            // The documents presented in the second before the composition
            // stand in for those the frame shows, which slice 3 chooses.
            var named = state.AtFrame(frame.FrameNanoseconds, load: none, cancellationToken: token);
            var shown = named.Documents
                .Where(item => item.Basis.PresentedTime >= frame.CompositedNanoseconds - 1_000_000_000)
                .Select(item => item.Key)
                .ToHashSet(StringComparer.Ordinal);
            var asked = state.AtFrame(frame.FrameNanoseconds, load: shown, cancellationToken: token);
            var same = asked.Documents.Where(item => item.State is not null).All(item =>
                full.Documents.SingleOrDefault(other => other.Key == item.Key)?.State is { } other &&
                BrowserStateSnapshot.Serialize(item.State!).AsSpan().SequenceEqual(BrowserStateSnapshot.Serialize(other)));
            if (!same)
            {
                differing++;
            }
            report.AppendLine(
                $"  documents named without their state: {named.Documents.Count} in {named.Cost.Milliseconds:F0} ms; " +
                $"{shown.Count} presented in the second before, read in {asked.Cost.Milliseconds:F0} ms ({asked.Cost.SnapshotsRead} snapshots, {asked.Cost.HeldStatesRead} held, {asked.Cost.ChunksRead} chunks, {asked.Cost.RecordsApplied} applied): {(same ? "equal" : "DIFFERENT")}");
            File.WriteAllText(reportPath, report.ToString());
        }
        report.AppendLine($"samples with a different state: {differing}");

        var cutPath = Path.Combine(_directory, "cut-short.mcap");
        await using (var input = File.OpenRead(path))
        await using (var output = File.Create(cutPath))
        {
            var length = input.Length / 2;
            var buffer = new byte[1 << 20];
            while (length > 0)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length)), token);
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                length -= read;
            }
        }
        using (var cutReader = RecordingFileReader.Open(cutPath))
        {
            var cutState = new RecordingFileBrowserState(cutReader, null);
            var cutEnd = cutReader.Chunks.Where(chunk => chunk.Stream == "browser-state").Max(chunk => chunk.EndTime);
            Compare($"cut short at half, time {cutEnd}", cutState.At(cutEnd, cancellationToken: token), cutState.At(cutEnd, useSnapshots: false, cancellationToken: token));
        }
        File.Delete(cutPath);

        await File.WriteAllTextAsync(reportPath, report.ToString(), token);
        Assert.Equal(0, differing);
    }

    // Writes every event of a file again, in event key order, through the
    // batch target, which makes the state thread's snapshots.
    private static async Task<RecordingFileStateSummary?> CopyAsync(RecordingFileReader reader, string path, CancellationToken token)
    {
        var recording = reader.Metadata["recording"];
        var streams = reader.Chunks
            .Where(chunk => chunk.Stream is not ("recorder" or RecordingFileStateRecorder.SnapshotStream or RecordingFileStateRecorder.IndexStream))
            .GroupBy(chunk => chunk.Stream)
            .Select(group => Events(reader, group).GetEnumerator())
            .ToList();
        var queue = new PriorityQueue<IEnumerator<StoredEvent>, long>();
        foreach (var stream in streams)
        {
            if (stream.MoveNext())
            {
                queue.Enqueue(stream, stream.Current.EventKey);
            }
        }
        using var target = new RecordingFileBatchTarget(path, recording);
        var batch = new List<BufferedEvent>(2000);
        while (queue.TryDequeue(out var stream, out _))
        {
            var stored = stream.Current;
            batch.Add(new BufferedEvent(stored.EventKey, stored.Event, stored.Event.Payload.GetRawText()));
            if (stream.MoveNext())
            {
                queue.Enqueue(stream, stream.Current.EventKey);
            }
            if (batch.Count == 2000)
            {
                await target.WriteAsync(new EventBatch([.. batch], [], []), token);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            await target.WriteAsync(new EventBatch([.. batch], [], []), token);
        }
        target.Finish();
        return target.StateSummary;
    }

    private static IEnumerable<StoredEvent> Events(RecordingFileReader reader, IEnumerable<RecordingFileChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            foreach (var message in reader.ReadChunk(chunk))
            {
                if (RecordingFileBatchTarget.IsEventTopic(message.Channel.Topic))
                {
                    yield return RecordingEventCodec.Decode(message.Data.Span);
                }
            }
        }
    }
}

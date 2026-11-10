using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 7 of docs/architecture/android-implementation-plan.md, step 7b:
// snapshots of the phone's accessibility tree, as the companion app sends
// them (AccessibilityTrees.java), read and placed on the session clock with
// a fake companion connection and a fake phone clock.
public sealed class AndroidAccessibilityTreeTests
{
    private const string Serial = "58191FDCQ00ABZ";

    // As in AndroidAccessibilityTests: the fake phone clock is this far
    // ahead of the session clock.
    private const long PhoneOffset = 5_000_000_000_000;

    private const long Second = 1_000_000_000;

    private const long Millisecond = 1_000_000;

    /// <summary>
    /// A snapshot line as AccessibilityTrees.java writes it on Android 17,
    /// with the "index" RecorderLink.java adds: the Settings window with
    /// three items, a root, its child and the child's child, which the
    /// child labels; and the status bar, whose items were not taken.
    /// </summary>
    internal static string TreeLine(
        long index = 1,
        long startedPhoneNanoseconds = 5_002_000_000_000,
        long takingNanoseconds = 6 * Millisecond,
        bool everyWindow = true,
        string reason = "requested",
        string? problem = null) =>
        Tree(index, startedPhoneNanoseconds, takingNanoseconds, everyWindow, reason, problem).ToJsonString();

    private static JsonObject Tree(
        long index,
        long startedPhoneNanoseconds,
        long takingNanoseconds,
        bool everyWindow,
        string reason,
        string? problem)
    {
        var changes = reason == "requested" ? 0 : 2;
        var firstChange = changes == 0 ? null : (long?)(startedPhoneNanoseconds / Millisecond - 3);
        return new JsonObject
        {
            ["type"] = "accessibility-tree",
            ["reasons"] = new JsonArray(reason),
            ["everyWindow"] = everyWindow,
            ["changes"] = changes,
            ["firstChangeEventTimeMilliseconds"] = firstChange,
            ["lastChangeEventTimeMilliseconds"] = firstChange is null ? null : firstChange + 1,
            ["startedPhoneNanoseconds"] = startedPhoneNanoseconds,
            ["windows"] = new JsonArray(
                Window(512, 1, "TYPE_APPLICATION", "Settings", 1, Items()),
                Window(7, 3, "TYPE_SYSTEM", "Status bar", 2, null)),
            ["problem"] = problem,
            ["finishedPhoneNanoseconds"] = startedPhoneNanoseconds + takingNanoseconds,
            ["index"] = index
        };
    }

    private static JsonObject Window(int id, int type, string typeName, string title, int layer, JsonArray? items) =>
        new()
        {
            ["id"] = id,
            ["type"] = type,
            ["typeName"] = typeName,
            ["layer"] = layer,
            ["title"] = title,
            ["bounds"] = new JsonObject { ["left"] = 0, ["top"] = 0, ["right"] = 1080, ["bottom"] = 2404 },
            ["active"] = id == 512,
            ["focused"] = id == 512,
            ["accessibilityFocused"] = false,
            ["pictureInPicture"] = false,
            ["displayId"] = 0,
            ["parentId"] = null,
            ["transitionTimeMilliseconds"] = 4_998_000,
            ["items"] = items,
            ["truncated"] = false,
            ["missingChildren"] = 0
        };

    // The event line's source item, three times, each in its place.
    private static JsonArray Items()
    {
        var source = JsonNode.Parse(AndroidAccessibilityTests.EventLine())!["source"]!;
        JsonObject Item(int index, int? parent, int depth, int[] labeledBy, int? traversalAfter)
        {
            var item = source.DeepClone().AsObject();
            item["index"] = index;
            item["parent"] = parent;
            item["depth"] = depth;
            item["labeledBy"] = new JsonArray([.. labeledBy.Select(label => (JsonNode?)label)]);
            item["labeledByOutside"] = 0;
            item["traversalBefore"] = null;
            item["traversalAfter"] = traversalAfter;
            return item;
        }

        return new JsonArray(
            Item(0, null, 0, [], null),
            Item(1, 0, 1, [], null),
            Item(2, 1, 2, [1], 0));
    }

    // The protocol

    [Fact]
    public void Protocol_LeavesASnapshotToBeReadLater_AndReadTreeReadsEveryField()
    {
        var line = TreeLine(index: 4, everyWindow: false, reason: "content-change");
        var message = Assert.IsType<CompanionAccessibilityTreeLine>(CompanionProtocol.Parse(line));
        Assert.Equal(line, message.Line);

        var (tree, problem) = CompanionProtocol.ReadTree(message.Line);
        Assert.Null(problem);
        Assert.NotNull(tree);
        Assert.Equal(4, tree.Index);
        Assert.Equal(["content-change"], tree.Reasons);
        Assert.False(tree.EveryWindow);
        Assert.Equal(2, tree.Changes);
        Assert.Equal(5_001_997, tree.FirstChangeEventTimeMilliseconds);
        Assert.Equal(5_001_998, tree.LastChangeEventTimeMilliseconds);
        Assert.Equal(5_002_000_000_000, tree.StartedPhoneNanoseconds);
        Assert.Equal(5_002_006_000_000, tree.FinishedPhoneNanoseconds);
        Assert.Null(tree.Problem);
        Assert.Equal(Encoding.UTF8.GetByteCount(line), tree.MessageBytes);

        Assert.Equal(2, tree.Windows.Count);
        var settings = tree.Windows[0];
        Assert.Equal(512, settings.Id);
        Assert.Equal("TYPE_APPLICATION", settings.TypeName);
        Assert.Equal("Settings", settings.Title);
        Assert.Equal(new AndroidNodeBounds(0, 0, 1080, 2404), settings.Bounds);
        Assert.True(settings.Active);
        Assert.Null(settings.ParentId);
        Assert.Equal(4_998_000, settings.TransitionTimeMilliseconds);
        Assert.False(settings.Truncated);
        Assert.Equal(3, settings.Items!.Count);
        var labelled = settings.Items[2];
        Assert.Equal(2, labelled.Index);
        Assert.Equal(1, labelled.Parent);
        Assert.Equal(2, labelled.Depth);
        Assert.Equal([1], labelled.LabeledBy);
        Assert.Null(labelled.TraversalBefore);
        Assert.Equal(0, labelled.TraversalAfter);
        // The fields an item shares with an event's source.
        Assert.Equal("com.android.settings:id/item", labelled.ViewId);
        Assert.Equal("partial", labelled.Checked);
        Assert.Null(settings.Items[0].Parent);

        Assert.Null(tree.Windows[1].Items);
    }

    [Fact]
    public void Protocol_ASnapshotItCannotRead_IsAProblemNotAFailure()
    {
        var line = "{\"type\":\"accessibility-tree\",\"index\":\"two\"}";
        Assert.IsType<CompanionAccessibilityTreeLine>(CompanionProtocol.Parse(line));

        var (tree, problem) = CompanionProtocol.ReadTree(line);

        Assert.Null(tree);
        Assert.False(string.IsNullOrEmpty(problem));
    }

    // The collector

    [Fact]
    public async Task Collector_PlacesEachSnapshotHalfwayThroughItsTaking_AndCountsThem()
    {
        var trees = new FakeTrees();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidAccessibilityTreeCollector(Serial, trees, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await trees.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // From before the start, so not kept.
        trees.Send(Snapshot(1, Second / 2, 6));
        trees.Send(Snapshot(2, 2 * Second, 6));
        trees.Send(Snapshot(3, 3 * Second, 2, everyWindow: false, reason: "content-change"));
        // The fourth never arrived.
        trees.Send(Snapshot(5, 3 * Second + 500 * Millisecond, 4, reason: "window-change", problem: "IllegalStateException: window gone"));
        trees.Send(new ReceivedAccessibilityTree("{\"type\":\"accessibility-tree\"", 4 * Second));
        var kilobytes = (Encoding.UTF8.GetByteCount(TreeLine(reason: "window-change", problem: "IllegalStateException: window gone")) / 1024.0)
            .ToString("0", CultureInfo.InvariantCulture);
        var expected =
            "Accessibility tree snapshots: 3 (at most 2 in one second), 2 of every window, " +
            $"the latest with 2 windows and 3 items, the largest 3 items and {kilobytes} KB, " +
            "the app took a median 4.0 ms to take each and at most 6.0 ms, 1 stopped early as the phone changed, " +
            "1 missing, 1 could not be read, median delay from phone to recorder 10.0 ms.";
        await WaitUntilAsync(() => collector.Summary == expected, () => collector.Summary);
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        var records = sink.Records.ToList();
        var validator = new EventRecordValidator("session-1");
        foreach (var record in records)
        {
            Assert.Empty(validator.Validate(record));
        }

        var kept = records.Where(record => record.EventType == AndroidAccessibilityRecords.TreeEventType).ToList();
        Assert.Equal([2L, 3, 5], kept.Select(record => record.Payload.GetProperty("index").GetInt64()));
        Assert.All(kept, record => Assert.Equal(AndroidAccessibilityRecords.TreeChannel, record.Channel));
        Assert.All(kept, record => Assert.Equal(AndroidCompanionRecords.FittedClockMapping, record.ClockMappingId));
        Assert.All(kept, record => Assert.Equal([AndroidAccessibilityRecords.PhoneSnapshotTimeFlag], record.QualityFlags));

        var first = kept[0];
        Assert.Equal(2 * Second + 3 * Millisecond, first.MonotonicNanoseconds);
        Assert.Equal(2 * Second + 3 * Millisecond + PhoneOffset, first.NativeTimestamp!.Value);
        Assert.Equal(AndroidCompanionRecords.ClockDomain, first.NativeTimestamp.Domain);
        Assert.Equal(FakePhoneClock.Uncertainty + 3 * Millisecond, first.TimestampUncertaintyNanoseconds);
        Assert.Equal(2 * Second + 16 * Millisecond, first.Payload.GetProperty("receivedAtNanoseconds").GetInt64());
        Assert.True(first.Payload.GetProperty("messageBytes").GetInt64() > 0);
        var windows = first.Payload.GetProperty("windows");
        Assert.Equal(3, windows[0].GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, windows[1].GetProperty("items").ValueKind);

        var unreadable = Assert.Single(records, record => record.EventType == AndroidAccessibilityRecords.UnavailableTreeEventType);
        Assert.Equal(4 * Second, unreadable.MonotonicNanoseconds);
        Assert.StartsWith(
            "A snapshot of the phone's accessibility tree could not be read: ",
            unreadable.Payload.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Collector_WithoutAPhoneClock_PlacesSnapshotsAsTheyArrive()
    {
        var trees = new FakeTrees();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidAccessibilityTreeCollector(Serial, trees, clock: null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await trees.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => collector.Summary == "Accessibility tree snapshots: none yet.", () => collector.Summary);

        trees.Send(Snapshot(1, 2 * Second, 6));
        await WaitUntilAsync(() => collector.Summary?.StartsWith("Accessibility tree snapshots: 1 ", StringComparison.Ordinal) == true, () => collector.Summary);
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.EndsWith(", placed by the time each arrived, as there is no phone clock.", collector.Summary);
        var record = Assert.Single(sink.Records, record => record.EventType == AndroidAccessibilityRecords.TreeEventType);
        Assert.Equal(2 * Second + 16 * Millisecond, record.MonotonicNanoseconds);
        Assert.Equal([AndroidScreenRecords.ReceiptTimeFlag], record.QualityFlags);
        Assert.Null(record.TimestampUncertaintyNanoseconds);
        Assert.Empty(new EventRecordValidator("session-1").Validate(record));
    }

    [Fact]
    public async Task Collector_NoCompanionConnection_RecordsWhyAndGoesOn()
    {
        var trees = new FakeTrees(connected: false);
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidAccessibilityTreeCollector(Serial, trees, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded, () => collector.HealthState.ToString());
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Null(collector.Summary);
        Assert.Equal(
            "The phone's accessibility tree could not be recorded: the companion app is not connected, so they could not be asked for. " +
            "The android.companion line says why. The recording goes on without it.",
            collector.HealthReason);
        var record = Assert.Single(sink.Records, record => record.EventType == AndroidAccessibilityRecords.UnavailableTreeEventType);
        Assert.Equal(AndroidAccessibilityRecords.TreeChannel, record.Channel);
        Assert.Empty(new EventRecordValidator("session-1").Validate(record));
    }

    [Fact]
    public async Task Collector_ConnectionEnds_RecordsItAndKeepsWhatCame()
    {
        var trees = new FakeTrees();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidAccessibilityTreeCollector(Serial, trees, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await trees.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        trees.Send(Snapshot(1, 2 * Second, 6));
        trees.End();
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded, () => collector.HealthState.ToString());
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.StartsWith("Accessibility tree snapshots: 1 (at most 1 in one second), 1 of every window,", collector.Summary);
        Assert.Equal(
            "The phone's accessibility tree stopped being recorded during the recording: the connection to the companion app ended: " +
            "the phone was unplugged, adb stopped, or the companion app was turned off. The recording goes on without it.",
            collector.HealthReason);
        var lost = Assert.Single(sink.Records, record => record.EventType == AndroidAccessibilityRecords.UnavailableTreeEventType);
        Assert.True(lost.Payload.GetProperty("connectionLost").GetBoolean());
        Assert.Single(sink.Records, record => record.EventType == AndroidAccessibilityRecords.TreeEventType);
    }

    [Fact]
    public async Task Collector_SnapshotsWaitingAtStop_AreKept()
    {
        var trees = new FakeTrees();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidAccessibilityTreeCollector(Serial, trees, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await trees.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        for (var index = 1; index <= 20; index++)
        {
            trees.Send(Snapshot(index, Second + index * 50 * Millisecond, 2, everyWindow: false, reason: "content-change"));
        }

        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(20, sink.Records.Count(record => record.EventType == AndroidAccessibilityRecords.TreeEventType));
        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
    }

    // The validator

    [Theory]
    [InlineData("windows/0/items/1/index", "5", "android-accessibility-tree-item-out-of-place")]
    [InlineData("windows/0/items/1/parent", "2", "android-accessibility-tree-item-out-of-place")]
    [InlineData("windows/0/items/0/parent", "0", "android-accessibility-tree-item-out-of-place")]
    [InlineData("windows/0/items/2/labeledBy", "[7]", "android-accessibility-tree-link-outside")]
    [InlineData("windows/0/items/2/traversalAfter", "3", "android-accessibility-tree-link-outside")]
    [InlineData("windows/0/items/2/depth", "-1", "payload-property-invalid")]
    [InlineData("windows/0/items/2/checked", "\"maybe\"", "payload-property-invalid")]
    [InlineData("windows/0/typeName", "3", "payload-property-invalid")]
    [InlineData("windows/1/missingChildren", "-1", "payload-property-invalid")]
    [InlineData("reasons", "[\"guess\"]", "android-accessibility-tree-reason-unknown")]
    [InlineData("finishedPhoneNanoseconds", "5001000000000", "android-accessibility-tree-finished-early")]
    [InlineData("index", "0", "payload-property-invalid")]
    public void Validator_RejectsABadSnapshot(string path, string value, string code)
    {
        var payload = Payload();
        Assert.Empty(Validate(payload));

        JsonNode node = payload;
        var parts = path.Split('/');
        foreach (var part in parts[..^1])
        {
            node = int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var place) ? node[place]! : node[part]!;
        }

        node[parts[^1]] = JsonNode.Parse(value);

        Assert.Contains(Validate(payload), issue => issue.Code == code);
    }

    [Fact]
    public void Validator_ReportsOnlyTheFirstItemOutOfPlace()
    {
        var payload = Payload();
        foreach (var item in payload["windows"]![0]!["items"]!.AsArray())
        {
            item!["index"] = 9;
        }

        Assert.Single(Validate(payload), issue => issue.Code == "android-accessibility-tree-item-out-of-place");
    }

    // The payload the collector writes for a snapshot line.
    private static JsonObject Payload()
    {
        var tree = CompanionProtocol.ReadTree(TreeLine()).Tree!;
        var text = JsonSerializer.Serialize(
            tree with { ReceivedAtNanoseconds = 2 * Second },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return JsonNode.Parse(text)!.AsObject();
    }

    private static IReadOnlyList<EventValidationIssue> Validate(JsonObject payload)
    {
        var descriptor = CollectorDescriptor.Create(
            AndroidAccessibilityTreeCollector.Type,
            nameof(AndroidAccessibilityTreeCollector),
            "1.0.0",
            [AndroidAccessibilityRecords.TreeChannel],
            "companion-accessibility-service");
        var record = RecorderEventFactory.Create(
            "session-1",
            descriptor,
            AndroidAccessibilityRecords.TreeChannel,
            1,
            2 * Second,
            AndroidAccessibilityRecords.TreeEventType,
            JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone(),
            []);
        return new EventRecordValidator("session-1").Validate(record);
    }

    /// <summary>
    /// A snapshot started at a session time, as the fake phone clock maps
    /// it, that took some milliseconds and was read by the recorder 10 ms
    /// after it was finished.
    /// </summary>
    private static ReceivedAccessibilityTree Snapshot(
        long index,
        long sessionStarted,
        int takingMilliseconds,
        bool everyWindow = true,
        string reason = "requested",
        string? problem = null)
    {
        var taking = takingMilliseconds * Millisecond;
        var line = TreeLine(index, sessionStarted + PhoneOffset, taking, everyWindow, reason, problem);
        return new ReceivedAccessibilityTree(line, sessionStarted + taking + 10 * Millisecond);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, Func<string?> state)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out; the state is: {state()}");
            }

            await Task.Delay(10);
        }
    }

    private sealed class FakeTrees(bool connected = true) : IPhoneAccessibilityTrees
    {
        private readonly Channel<ReceivedAccessibilityTree> _channel = Channel.CreateUnbounded<ReceivedAccessibilityTree>();

        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Send(ReceivedAccessibilityTree received) => Assert.True(_channel.Writer.TryWrite(received));

        public void End() => _channel.Writer.Complete();

        public Task<ChannelReader<ReceivedAccessibilityTree>?> RequestAccessibilityTreesAsync(CancellationToken cancellation)
        {
            Asked.TrySetResult();
            return Task.FromResult(connected ? _channel.Reader : null);
        }
    }

    private sealed class FakePhoneClock(bool sampled) : IPhoneClock
    {
        public const long Uncertainty = 1_500_000;

        public Task<bool> FirstSample { get; } = Task.FromResult(sampled);

        public PhoneTimeMapping? Map(long phoneNanoseconds) =>
            sampled ? new PhoneTimeMapping(phoneNanoseconds - PhoneOffset, Uncertainty, 1) : null;
    }

    private sealed class ListSink : IRecorderEventSink
    {
        private readonly List<RecorderEvent> _records = [];

        public IReadOnlyList<RecorderEvent> Records
        {
            get
            {
                lock (_records)
                {
                    return [.. _records];
                }
            }
        }

        public bool TryWrite(RecorderEvent record)
        {
            lock (_records)
            {
                _records.Add(record);
            }

            return true;
        }
    }
}

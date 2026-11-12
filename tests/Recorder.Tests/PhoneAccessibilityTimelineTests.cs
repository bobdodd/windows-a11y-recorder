using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 7 of docs/architecture/android-implementation-plan.md, step 7c:
// the phone's tree snapshots and accessibility focus events kept in the
// playback index, and read back as the tree at a time, the item with
// accessibility focus, and the screen's size for its outline. The records
// are made from the step 7a and 7b lines (AndroidAccessibilityTests and
// AndroidAccessibilityTreeTests): a Settings window whose row holds
// "Network & internet", and the status bar.
public sealed class PhoneAccessibilityTimelineTests : IDisposable
{
    private const long Second = 1_000_000_000;

    private const long Millisecond = 1_000_000;

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "android.accessibility", "AndroidAccessibilityCollector", "1",
        [AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.TreeChannel, AndroidCompanionRecords.DisplayChannel],
        "test");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "phone-accessibility-" + Guid.NewGuid().ToString("N"));

    public PhoneAccessibilityTimelineTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static long At(double seconds) => (long)Math.Round(seconds * Second);

    // A Settings item, from the step 7a event line's source.
    private static JsonObject Item(int index, int? parent, int depth, string className, string? text, bool focused, (int Left, int Top, int Right, int Bottom) bounds)
    {
        var item = JsonNode.Parse(AndroidAccessibilityTests.EventLine())!["source"]!.DeepClone().AsObject();
        item["className"] = className;
        item["text"] = text;
        item["accessibilityFocused"] = focused;
        item["bounds"] = new JsonObject { ["left"] = bounds.Left, ["top"] = bounds.Top, ["right"] = bounds.Right, ["bottom"] = bounds.Bottom };
        item["index"] = index;
        item["parent"] = parent;
        item["depth"] = depth;
        item["labeledBy"] = new JsonArray();
        item["labeledByOutside"] = 0;
        item["traversalBefore"] = null;
        item["traversalAfter"] = null;
        return item;
    }

    // The Settings window's items: the page, its "Network & internet" row,
    // and the row's text, with the row moved up by scrolled pixels.
    private static JsonArray SettingsItems(int scrolled, bool rowFocused, bool textFocused) =>
        new(
            Item(0, null, 0, "android.widget.FrameLayout", null, false, (0, 0, 1080, 2404)),
            Item(1, 0, 1, "android.widget.LinearLayout", null, rowFocused, (0, 420 - scrolled, 1080, 620 - scrolled)),
            Item(2, 1, 2, "android.widget.TextView", "Network & internet", textFocused, (40, 440 - scrolled, 800, 500 - scrolled)));

    // A snapshot as the step 7b collector stores it; the status bar's
    // items are taken when everyWindow is true.
    private static AndroidAccessibilityTree Tree(long index, bool everyWindow, JsonArray settings)
    {
        var tree = JsonNode.Parse(AndroidAccessibilityTreeTests.TreeLine(
            index: index,
            everyWindow: everyWindow,
            reason: everyWindow ? "requested" : "content-change"))!.AsObject();
        var windows = tree["windows"]!.AsArray();
        windows[0]!["items"] = settings;
        if (everyWindow)
        {
            var clock = Item(0, null, 0, "android.widget.TextView", "12:00", false, (40, 0, 200, 60));
            clock["windowId"] = 7;
            windows[1]!["items"] = new JsonArray(clock);
        }

        var (read, problem) = CompanionProtocol.ReadTree(tree.ToJsonString());
        Assert.Null(problem);
        return read!;
    }

    // An accessibility focus event, given or cleared, whose source is the
    // "Network & internet" row or its text.
    private static AndroidAccessibilityEvent Focus(bool cleared, bool text, int scrolled)
    {
        var line = JsonNode.Parse(AndroidAccessibilityTests.EventLine(
            typeName: cleared ? "TYPE_VIEW_ACCESSIBILITY_FOCUS_CLEARED" : "TYPE_VIEW_ACCESSIBILITY_FOCUSED"))!.AsObject();
        line["eventType"] = cleared ? PhoneAccessibilityTimeline.FocusClearedEventType : PhoneAccessibilityTimeline.FocusedEventType;
        var source = line["source"]!.AsObject();
        source["accessibilityFocused"] = !cleared;
        if (text)
        {
            source["className"] = "android.widget.TextView";
            source["text"] = "Network & internet";
            source["bounds"] = new JsonObject { ["left"] = 40, ["top"] = 440 - scrolled, ["right"] = 800, ["bottom"] = 500 - scrolled };
        }

        var message = Assert.IsType<CompanionAccessibilityEvent>(CompanionProtocol.Parse(line.ToJsonString()));
        Assert.Null(message.Problem);
        return message.Event!;
    }

    // At 0.5 s the screen is upright; at 1 s the row is given the focus; at
    // 2 s every window is taken; at 2.5 s the row's text is given the
    // focus; at 3 s the list has scrolled up 320 pixels and Settings is
    // taken again; at 4 s the phone is turned; at 5 s the text's focus is
    // cleared, with its bounds after the scroll.
    private static List<(long Time, string Channel, string EventType, object Payload)> Records() =>
    [
        (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
            new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
        (At(1), AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.EventType, Focus(false, false, 0)),
        (At(2), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType,
            Tree(1, true, SettingsItems(0, true, false))),
        (At(2.5), AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.EventType, Focus(false, true, 0)),
        (At(3), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType,
            Tree(2, false, SettingsItems(320, false, true))),
        (At(4), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
            new AndroidDisplayState(AndroidCompanionRecords.ChangedReason, 90, 1080, 2404)),
        (At(5), AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.EventType, Focus(true, true, 320)),
    ];

    private SessionPlaybackArchive FromIndex(out PlaybackIndex index, out FakeRecords recording)
    {
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        recording = new FakeRecords();
        var sequence = 0UL;
        foreach (var (time, channel, eventType, payload) in Records())
        {
            sequence++;
            var record = RecorderEventFactory.Create("s", Collector, channel, sequence, time, eventType, payload);
            recording.Add(time, record);
            builder.Add((long)sequence, record);
        }

        index = builder.Build();
        var archiveBuilder = new SessionPlaybackArchiveBuilder(_folder, retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        return archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(10), 10 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, false, 5, true, true),
            [], [], 1, 0, null));
    }

    [Fact]
    public void Index_KeepsSnapshotsWithoutTheirItems_AndFocusAndDisplayRecordsWhole()
    {
        FromIndex(out var index, out _);

        Assert.Equal(27, index.Version);
        var trees = index.Events.Where(item => item.Channel == AndroidAccessibilityRecords.TreeChannel).ToArray();
        Assert.Equal(2, trees.Length);
        var windows = trees[0].Payload.GetProperty("windows").EnumerateArray().ToArray();
        Assert.All(windows, window => Assert.False(window.TryGetProperty("items", out _)));
        Assert.Equal([true, true], windows.Select(window => window.GetProperty("itemsTaken").GetBoolean()));
        Assert.Equal([3, 1], windows.Select(window => window.GetProperty("itemCount").GetInt32()));
        Assert.Equal("Settings", windows[0].GetProperty("title").GetString());
        var later = trees[1].Payload.GetProperty("windows").EnumerateArray().ToArray();
        Assert.Equal([true, false], later.Select(window => window.GetProperty("itemsTaken").GetBoolean()));

        var focus = index.Events.Where(item => item.Channel == AndroidAccessibilityRecords.EventsChannel).ToArray();
        Assert.Equal(3, focus.Length);
        Assert.Equal(420, focus[0].Payload.GetProperty("source").GetProperty("bounds").GetProperty("top").GetInt32());
        Assert.Equal(2, index.Events.Count(item => item.Channel == AndroidCompanionRecords.DisplayChannel));
    }

    [Fact]
    public void Compact_OfACompactSnapshot_IsTheSame()
    {
        var payload = RecorderEventFactory.Create(
            "s", Collector, AndroidAccessibilityRecords.TreeChannel, 1, 0,
            AndroidAccessibilityRecords.TreeEventType, Tree(1, true, SettingsItems(0, true, false))).Payload;

        var compact = PhoneAccessibilityTimeline.Compact(payload);

        Assert.Equal(compact.GetRawText(), PhoneAccessibilityTimeline.Compact(compact).GetRawText());
        Assert.Equal("requested, 2 windows, 4 items", PhoneAccessibilityTimeline.Summary(payload));
        Assert.Equal("requested, 2 windows, 4 items", PhoneAccessibilityTimeline.Summary(compact));
    }

    [Fact]
    public void TreeAt_BeforeTheFirstSnapshot_IsNull_ButTheFocusIsKnown()
    {
        var phone = FromIndex(out _, out var recording).PhoneAccessibility;

        Assert.True(phone.TreesRecorded);
        Assert.True(phone.FocusRecorded);
        Assert.Equal(2, phone.SnapshotCount);
        Assert.Null(phone.TreeAt(At(1.5), recording));
        Assert.Null(phone.FocusAt(At(0.9)));
        var focus = phone.FocusAt(At(1.5))!;
        Assert.Equal("LinearLayout containing \"Network & internet\"", focus.Item.Label);
        Assert.Equal(new PhoneBounds(0, 420, 1080, 620), focus.Item.Bounds);
    }

    [Fact]
    public void TreeAt_ReadsEachWindowsItems_AndFindsTheFocusedItemByTheSnapshotsFlag()
    {
        var phone = FromIndex(out _, out var recording).PhoneAccessibility;

        var tree = phone.TreeAt(At(2), recording)!;

        Assert.Equal(2, tree.WindowCount);
        Assert.Equal(4, tree.ItemCount);
        Assert.Empty(tree.Problems);
        var settings = tree.Snapshot.Roots[0];
        Assert.Equal("window:512", settings.Key);
        Assert.Equal(AccessibilityTreeRowKind.PhoneWindow, settings.Kind);
        Assert.Equal("Window \"Settings\" (TYPE_APPLICATION, active, focused, window 512)", settings.Label);
        Assert.Equal(At(2), settings.RecordedAt);
        var page = Assert.Single(settings.Children);
        Assert.Equal("FrameLayout containing \"Network & internet\"", page.Label);
        var row = Assert.Single(page.Children);
        Assert.Equal("window:512/0/0", row.Key);
        Assert.Equal(AccessibilityTreeRowKind.PhoneItem, row.Kind);
        Assert.Equal("LinearLayout containing \"Network & internet\"", row.Label);
        Assert.Equal("TextView \"Network & internet\"", Assert.Single(row.Children).Label);
        using (var record = JsonDocument.Parse(row.Record!))
        {
            Assert.Equal(1, record.RootElement.GetProperty("index").GetInt32());
            Assert.Equal("com.android.settings:id/item", record.RootElement.GetProperty("viewId").GetString());
        }

        Assert.Equal("Window \"Status bar\" (TYPE_SYSTEM, window 7)", tree.Snapshot.Roots[1].Label);
        Assert.Equal("TextView \"12:00\"", Assert.Single(tree.Snapshot.Roots[1].Children).Label);

        Assert.Equal("window:512/0/0", tree.FocusKey);
        Assert.Equal(new PhoneBounds(0, 420, 1080, 620), tree.FocusBounds);
        Assert.Equal(["window:512", "window:512/0", "window:512/0/0"], tree.Snapshot.PathTo(tree.FocusKey!));
    }

    [Fact]
    public void TreeAt_ASnapshotFromBeforeTheFocus_IsMatchedByTheItemsFields_WithoutNewerBounds()
    {
        var phone = FromIndex(out _, out var recording).PhoneAccessibility;

        // The 2 s snapshot still flags the row, but the text was given the
        // focus at 2.5 s, after it.
        var tree = phone.TreeAt(At(2.5), recording)!;

        Assert.Equal("window:512/0/0/0", tree.FocusKey);
        Assert.Null(tree.FocusBounds);
        Assert.Equal(new PhoneBounds(40, 440, 800, 500), phone.FocusAt(At(2.5))!.Item.Bounds);
    }

    [Fact]
    public void TreeAt_AfterTheListScrolled_GivesTheItemsNewBounds_AndOtherWindowsKeepTheirLastItems()
    {
        var phone = FromIndex(out _, out var recording).PhoneAccessibility;

        var tree = phone.TreeAt(At(3.5), recording)!;

        Assert.Equal("window:512/0/0/0", tree.FocusKey);
        Assert.Equal(new PhoneBounds(40, 120, 800, 180), tree.FocusBounds);
        Assert.Equal(At(3), tree.Snapshot.Roots[0].RecordedAt);
        // The 3 s snapshot did not take the status bar's items: they are the
        // 2 s snapshot's.
        Assert.Equal(At(2), tree.Snapshot.Roots[1].RecordedAt);
        Assert.Equal("TextView \"12:00\"", Assert.Single(tree.Snapshot.Roots[1].Children).Label);
        Assert.Equal(4, tree.ItemCount);
    }

    [Fact]
    public void FocusCleared_ByTheSameItemAfterItMoved_LeavesNoFocus()
    {
        var phone = FromIndex(out _, out var recording).PhoneAccessibility;

        Assert.NotNull(phone.FocusAt(At(4.9)));
        Assert.Null(phone.FocusAt(At(5)));
        var tree = phone.TreeAt(At(5), recording)!;
        Assert.Null(tree.FocusKey);
        Assert.Null(tree.FocusBounds);
    }

    [Fact]
    public void TreeAt_ReadsEachSnapshotOnce_AsThePlayheadMoves()
    {
        var phone = FromIndex(out _, out var recording).PhoneAccessibility;

        for (var time = At(2); time < At(4); time += 100 * Millisecond)
        {
            Assert.NotNull(phone.TreeAt(time, recording));
        }

        Assert.Equal(2, recording.Reads);
        Assert.Same(phone.TreeAt(At(3.1), recording), phone.TreeAt(At(3.2), recording));
    }

    [Fact]
    public void TreeAt_WithoutTheRecording_SaysEachWindowsItemsCouldNotBeRead()
    {
        var phone = FromIndex(out _, out _).PhoneAccessibility;

        var tree = phone.TreeAt(At(2), null)!;

        Assert.Equal(2, tree.WindowCount);
        Assert.Equal(0, tree.ItemCount);
        Assert.Equal(2, tree.Problems.Count);
        var note = Assert.Single(tree.Snapshot.Roots[0].Children);
        Assert.Equal(AccessibilityTreeRowKind.NotRecorded, note.Kind);
        Assert.StartsWith("this window's items could not be read", note.Label, StringComparison.Ordinal);
        Assert.Null(tree.FocusKey);
    }

    [Fact]
    public void ScreenAt_IsTheDisplaysSizeAsItWasTurned()
    {
        var phone = FromIndex(out _, out _).PhoneAccessibility;

        Assert.Equal((1080, 2404), phone.ScreenAt(At(0.1)));
        Assert.Equal((1080, 2404), phone.ScreenAt(At(3.9)));
        Assert.Equal((2404, 1080), phone.ScreenAt(At(4)));
    }

    [Fact]
    public void Summaries_NameTheEventAndItsItem()
    {
        var archive = FromIndex(out _, out _);
        var focus = archive.PhoneAccessibility.FocusAt(At(1))!.Event;
        // A window's row selects the snapshot that took its items.
        var tree = archive.TimelineEvents[archive.PhoneAccessibility.TreeAt(At(2), null)!.Snapshot.Roots[0].EventId!];

        Assert.Equal(
            "accessibility-event: TYPE_VIEW_ACCESSIBILITY_FOCUSED, LinearLayout containing \"Network & internet\"",
            archive.Describe(focus));
        Assert.Equal("accessibility-tree: requested, 2 windows, 4 items", archive.Describe(tree));
    }

    // The recording's records, as the player reads one from the file.
    private sealed class FakeRecords : ISessionEventRecordSource
    {
        private readonly Dictionary<long, string> _records = [];

        public int Reads { get; private set; }

        public void Add(long time, RecorderEvent record) =>
            _records[time] = JsonSerializer.Serialize(new { payload = record.Payload });

        public string ReadEventJson(SessionTimelineEvent item)
        {
            Reads++;
            return _records[item.MonotonicNanoseconds];
        }
    }
}

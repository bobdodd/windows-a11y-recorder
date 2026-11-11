using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 1 of docs/architecture/android-checks.md, "Build order": a phone
// recording's apps, visits, screens and states. The first case follows the
// measured windows and events of test 64 (session
// 20261111-002648-06be83ce21ec47309bd4bc68b4ae1cb4: Settings, then "Safety
// and emergency" sliding in); the others are made up, one rule each.
public sealed class PhoneScreenTimelineTests : IDisposable
{
    private const long Second = 1_000_000_000;

    private const string Settings = "com.android.settings";

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "android.accessibility", "AndroidAccessibilityCollector", "1",
        [AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.TreeChannel, AndroidCompanionRecords.DisplayChannel],
        "test");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "phone-screens-" + Guid.NewGuid().ToString("N"));

    private readonly List<(SessionTimelineEvent Event, JsonElement Payload)> _trees = [];

    private readonly List<(SessionTimelineEvent Event, JsonElement Payload)> _states = [];

    private long _key;

    public PhoneScreenTimelineTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static long At(double seconds) => (long)Math.Round(seconds * Second);

    private SessionTimelineEvent Event(double seconds, string channel) =>
        new(++_key, "e" + _key, "observed", channel, AndroidAccessibilityRecords.EventType, At(seconds), string.Empty);

    // A window of a compact snapshot; evidence is given when its items were taken.
    private static JsonObject Window(
        int id, string? title, int left = 0, int top = 0, int right = 1080, int bottom = 2404,
        int layer = 0, bool active = true, string typeName = "TYPE_APPLICATION",
        string? package = Settings, string? heading = null, string[]? panes = null, string? signature = null,
        long? transition = null)
    {
        var window = new JsonObject
        {
            ["id"] = id,
            ["type"] = typeName == "TYPE_APPLICATION" ? 1 : 3,
            ["typeName"] = typeName,
            ["layer"] = layer,
            ["title"] = title,
            ["bounds"] = new JsonObject { ["left"] = left, ["top"] = top, ["right"] = right, ["bottom"] = bottom },
            ["active"] = active,
            ["transitionTimeMilliseconds"] = transition,
            ["itemsTaken"] = package is not null,
            ["itemCount"] = package is null ? 0 : 3,
        };
        if (package is not null)
        {
            window[PhoneScreenEvidence.Field] = new PhoneScreenEvidence(package, heading, panes ?? [], signature, signature is null ? 0 : 4).ToJson();
        }

        return window;
    }

    private void Snapshot(double seconds, params JsonObject[] windows)
    {
        var list = new JsonArray();
        foreach (var window in windows)
        {
            list.Add(window);
        }

        var payload = new JsonObject { ["windows"] = list };
        _trees.Add((Event(seconds, AndroidAccessibilityRecords.TreeChannel), JsonSerializer.SerializeToElement(payload)));
    }

    private void WindowState(double seconds, int windowId, string className, string? text, int contentChangeTypes = 0, string package = Settings)
    {
        var payload = new JsonObject
        {
            ["eventType"] = PhoneScreenTimeline.WindowStateChangedEventType,
            ["eventTypeName"] = "TYPE_WINDOW_STATE_CHANGED",
            ["packageName"] = package,
            ["className"] = className,
            ["windowId"] = windowId,
            ["text"] = text is null ? new JsonArray() : new JsonArray(text),
            ["contentChangeTypes"] = contentChangeTypes,
        };
        _states.Add((Event(seconds, AndroidAccessibilityRecords.EventsChannel), JsonSerializer.SerializeToElement(payload)));
    }

    private PhoneScreenTimeline Screens(double duration = 40) =>
        new(_trees, _states, _ => (1080, 2404), At(duration));

    // The status bar, a system window listed in every snapshot.
    private static JsonObject StatusBar() =>
        Window(1321, null, bottom: 161, layer: 1, active: false, typeName: "TYPE_SYSTEM", package: "com.android.systemui", panes: ["Status bar"]);

    [Fact]
    public void Test64_TheNewWindowStartsASecondVisit_AtItsWindowStateEvent_AndOnlyItsLastBoundsAreSettled()
    {
        Snapshot(1.21, StatusBar(), Window(1316, "Settings", signature: "1144a107a741ac61"));
        Snapshot(16.39, StatusBar(), Window(1316, "Settings", package: null));
        // Between the two activities no application window is listed.
        Snapshot(16.40, StatusBar());
        WindowState(16.51, 1343, "com.android.settings.SubSettings", "Safety and emergency");
        WindowState(16.52, 1343, "com.android.settings.SubSettings", "Safety and emergency");
        Snapshot(16.57, StatusBar(), Window(1343, "Safety and emergency", left: 191, heading: "Safety and emergency", signature: "07bf8db751286e75", transition: 317207179));
        Snapshot(16.66, StatusBar(), Window(1343, "Safety and emergency", left: 191, heading: "Safety and emergency", signature: "07bf8db751286e75", transition: 317207179));
        Snapshot(16.69, StatusBar(), Window(1343, "Safety and emergency", left: 18, heading: "Safety and emergency", signature: "07bf8db751286e75", transition: 317207316));
        Snapshot(17.00, StatusBar(), Window(1343, "Safety and emergency", heading: "Safety and emergency", signature: "07bf8db751286e75", transition: 317207638));
        Snapshot(17.02, StatusBar(), Window(1343, "Safety and emergency", heading: "Safety and emergency", signature: "07bf8db751286e75", transition: 317207638));

        var screens = Screens(33.769);

        var app = Assert.Single(screens.Apps);
        Assert.Equal(Settings, app.Package);
        Assert.Equal(2, screens.Visits.Count);
        var (first, second) = (screens.Visits[0], screens.Visits[1]);
        Assert.Equal((At(1.21), At(16.51), PhoneVisitCause.FirstWindow), (first.Start, first.End, first.Cause));
        Assert.Equal((At(16.51), At(33.769), PhoneVisitCause.NewWindow), (second.Start, second.End, second.Cause));
        Assert.Equal("Settings (com.android.settings)", first.Screen.Label);
        Assert.Equal(PhoneTitleSource.WindowTitle, first.Screen.TitleSource);
        Assert.Null(first.Screen.Activity);
        Assert.Equal("Safety and emergency (com.android.settings)", second.Screen.Label);
        Assert.Equal("com.android.settings.SubSettings", second.Screen.Activity);
        Assert.Equal(5, second.Snapshots.Count);
        Assert.Equal([At(17.00), At(17.02)], second.SettledSnapshots.Select(item => item.MonotonicNanoseconds));
        Assert.Equal(2, first.SettledSnapshots.Count);
        Assert.Empty(first.States);
        Assert.Empty(second.States);
        Assert.Same(second, screens.VisitAt(At(16.51)));
        Assert.Same(first, screens.VisitAt(At(16.50)));
        Assert.Null(screens.VisitAt(At(1)));
    }

    [Fact]
    public void ASnapshotWithTheFinalBounds_ButALaterTransitionTime_IsNotSettled()
    {
        Snapshot(1, Window(60, "Settings", transition: 100));
        Snapshot(2, Window(60, "Settings", transition: 200));
        Snapshot(3, Window(60, "Settings", transition: 200));

        var visit = Assert.Single(Screens(4).Visits);

        Assert.Equal([At(2), At(3)], visit.SettledSnapshots.Select(item => item.MonotonicNanoseconds));
    }

    [Fact]
    public void AWindowStateEventInTheSameWindow_WithOtherText_StartsAVisitToAnotherScreen()
    {
        Snapshot(1, Window(5, null, package: "com.example.mail"));
        WindowState(2, 5, "com.example.mail.MainActivity", "Inbox", package: "com.example.mail");
        Snapshot(2.1, Window(5, null, package: "com.example.mail"));
        WindowState(3, 5, "com.example.mail.MainActivity", "Sent", package: "com.example.mail");
        Snapshot(3.1, Window(5, null, package: "com.example.mail"));

        var screens = Screens();

        Assert.Equal(3, screens.Visits.Count);
        Assert.Equal([PhoneVisitCause.FirstWindow, PhoneVisitCause.WindowStateChanged, PhoneVisitCause.WindowStateChanged],
            screens.Visits.Select(visit => visit.Cause));
        Assert.Equal(At(3), screens.Visits[2].Start);
        Assert.Equal(["untitled screen 1", "Inbox", "Sent"], screens.Screens.Select(screen => screen.Name));
        Assert.Equal(PhoneTitleSource.WindowStateText, screens.Screens[1].TitleSource);
    }

    [Fact]
    public void ASmallerWindowAbove_AndADialogByItsClass_AreStatesOfTheScreen_NotVisits()
    {
        Snapshot(1, Window(10, "Accounts"));
        Snapshot(2, Window(10, "Accounts", active: false), Window(11, "Remove account?", left: 100, top: 900, right: 980, bottom: 1400, layer: 1));
        Snapshot(3, Window(10, "Accounts"));
        WindowState(4, 12, "android.app.AlertDialog", "Sign out?");
        Snapshot(4.1, Window(10, "Accounts", active: false), Window(12, null, layer: 1));
        Snapshot(5, Window(10, "Accounts"));

        var screens = Screens(6);

        var visit = Assert.Single(screens.Visits);
        Assert.Equal(2, visit.States.Count);
        Assert.Equal(("window", "Remove account?", At(2), At(3)), (visit.States[0].Kind, visit.States[0].Name, visit.States[0].Start, visit.States[0].End));
        Assert.Equal(("dialog", "Sign out?", At(4.1), At(5)), (visit.States[1].Kind, visit.States[1].Name, visit.States[1].Start, visit.States[1].End));
        Assert.Equal(PhoneTitleSource.WindowStateText, visit.States[1].NamedBy);
        Assert.Null(visit.States[0].Package);
    }

    [Fact]
    public void APane_IsAStateFromItsTitle_AndItsWindowStateEventStartsNoVisit()
    {
        Snapshot(1, Window(20, "Shop"));
        WindowState(2, 20, "android.widget.FrameLayout", "Filters", contentChangeTypes: 0x10);
        Snapshot(2.1, Window(20, "Shop", panes: ["Filters"]));
        Snapshot(3, Window(20, "Shop"));

        var screens = Screens(4);

        var visit = Assert.Single(screens.Visits);
        var state = Assert.Single(visit.States);
        Assert.Equal(("pane", "Filters", PhoneTitleSource.PaneTitle, At(2.1), At(3)), (state.Kind, state.Name, state.NamedBy, state.Start, state.End));
    }

    [Fact]
    public void AnotherAppInFront_StartsAVisitToItsScreen_AndAReturnIsANewVisitToTheSameScreen()
    {
        WindowState(0.9, 30, "com.android.settings.Settings", "Settings");
        Snapshot(1, Window(30, "Settings"));
        WindowState(1.9, 31, "com.google.android.apps.nexuslauncher.NexusLauncherActivity", "Home", package: "com.google.android.apps.nexuslauncher");
        Snapshot(2, Window(31, null, package: "com.google.android.apps.nexuslauncher"));
        WindowState(2.9, 32, "com.android.settings.Settings", "Settings");
        Snapshot(3, Window(32, "Settings"));

        var screens = Screens(4);

        Assert.Equal(3, screens.Visits.Count);
        Assert.Equal([PhoneVisitCause.FirstWindow, PhoneVisitCause.AnotherApp, PhoneVisitCause.AnotherApp], screens.Visits.Select(visit => visit.Cause));
        Assert.Equal([Settings, "com.google.android.apps.nexuslauncher"], screens.Apps.Select(app => app.Package));
        Assert.Equal(2, screens.Screens.Count);
        Assert.Same(screens.Visits[0].Screen, screens.Visits[2].Screen);
        Assert.True(screens.Visits[2].IsReturn);
        Assert.False(screens.Visits[0].IsReturn);
        Assert.Equal(At(0.9), screens.Visits[0].Start);
    }

    [Fact]
    public void TheWindowTheRecordingBeganIn_IsTheSameScreen_AsALaterVisitWithItsActivity()
    {
        // No window-state event for the first window, so its activity is
        // unknown; the later visit's event gives it.
        Snapshot(1, Window(40, "Settings"));
        WindowState(2, 41, "com.android.settings.SubSettings", "Display");
        Snapshot(2.1, Window(41, "Display"));
        WindowState(3, 40, "com.android.settings.Settings", "Settings");
        Snapshot(3.1, Window(40, "Settings"));

        var screens = Screens(4);

        Assert.Equal(3, screens.Visits.Count);
        Assert.Equal(2, screens.Screens.Count);
        Assert.Same(screens.Visits[0].Screen, screens.Visits[2].Screen);
        Assert.Equal("com.android.settings.Settings", screens.Visits[0].Screen.Activity);
    }

    [Fact]
    public void AScreenWithoutATitle_IsNamedByItsFirstHeading_OrByItsActivity_KeptApartByItsViewIds()
    {
        WindowState(0.9, 50, "com.example.app.MainActivity", null, package: "com.example.app");
        Snapshot(1, Window(50, null, package: "com.example.app", heading: "Welcome"));
        WindowState(1.9, 51, "com.example.app.DetailActivity", null, package: "com.example.app");
        Snapshot(2, Window(51, null, package: "com.example.app", signature: "aaaa"));
        WindowState(2.9, 52, "com.example.app.DetailActivity", null, package: "com.example.app");
        Snapshot(3, Window(52, null, package: "com.example.app", signature: "bbbb"));
        WindowState(3.9, 53, "com.example.app.DetailActivity", null, package: "com.example.app");
        Snapshot(4, Window(53, null, package: "com.example.app", signature: "aaaa"));

        var screens = Screens(5);

        Assert.Equal(4, screens.Visits.Count);
        Assert.Equal(["Welcome", "DetailActivity, untitled screen 1", "DetailActivity, untitled screen 2"], screens.Screens.Select(screen => screen.Name));
        Assert.Equal(PhoneTitleSource.FirstHeading, screens.Screens[0].TitleSource);
        Assert.Same(screens.Visits[1].Screen, screens.Visits[3].Screen);
        Assert.Equal("untitled, kept apart by its view IDs", screens.Screens[1].NamedBy);
    }

    [Fact]
    public void Evidence_TakesAHeadingsTextFromInsideIt_TheRootsPackage_AndViewIdsInAnyOrder()
    {
        static JsonObject Item(int index, int? parent, string? viewId, string? text = null, bool heading = false, string? pane = null) => new()
        {
            ["index"] = index,
            ["parent"] = parent,
            ["packageName"] = "com.example.app",
            ["viewId"] = viewId,
            ["text"] = text,
            ["contentDescription"] = null,
            ["paneTitle"] = pane,
            ["heading"] = heading,
            ["visibleToUser"] = true,
        };

        var items = JsonSerializer.SerializeToElement(new JsonArray(
            Item(0, null, "root", pane: "Filters"),
            Item(1, 0, "toolbar", heading: true),
            Item(2, 1, "title", "Orders"),
            Item(3, 0, "list", "Not a heading")));
        var reordered = JsonSerializer.SerializeToElement(new JsonArray(
            Item(0, null, "list"),
            Item(1, 0, "title"),
            Item(2, 0, "toolbar"),
            Item(3, 0, "root")));

        var evidence = PhoneScreenEvidence.Of(items);

        Assert.Equal(("com.example.app", "Orders", 4), (evidence.Package, evidence.FirstHeading, evidence.ViewIdCount));
        Assert.Equal(["Filters"], evidence.PaneTitles);
        Assert.Equal(PhoneScreenEvidence.Of(reordered).ViewIdSignature, evidence.ViewIdSignature);
        Assert.Equal(16, evidence.ViewIdSignature!.Length);
    }

    [Fact]
    public void Index_KeepsWindowStateEventsWhole_AndSnapshotsWithTheirScreenEvidence_ForTheArchivesVisits()
    {
        var tree = JsonNode.Parse(AndroidAccessibilityTreeTests.TreeLine(index: 1, everyWindow: true, reason: "requested"))!.AsObject();
        var (snapshot, problem) = CompanionProtocol.ReadTree(tree.ToJsonString());
        Assert.Null(problem);
        var line = JsonNode.Parse(AndroidAccessibilityTests.EventLine(typeName: "TYPE_WINDOW_STATE_CHANGED", text: "Network & internet"))!.AsObject();
        line["eventType"] = PhoneScreenTimeline.WindowStateChangedEventType;
        line["contentChangeTypes"] = 0;
        line["className"] = "com.android.settings.SubSettings";
        var message = Assert.IsType<CompanionAccessibilityEvent>(CompanionProtocol.Parse(line.ToJsonString()));
        Assert.Null(message.Problem);

        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        builder.Add(1, RecorderEventFactory.Create("s", Collector, AndroidAccessibilityRecords.EventsChannel, 1, At(0.5),
            AndroidAccessibilityRecords.EventType, message.Event!));
        builder.Add(2, RecorderEventFactory.Create("s", Collector, AndroidAccessibilityRecords.TreeChannel, 2, At(1),
            AndroidAccessibilityRecords.TreeEventType, snapshot!));
        var index = builder.Build();

        var kept = Assert.Single(index.Events, item => item.Channel == AndroidAccessibilityRecords.EventsChannel);
        Assert.Equal("com.android.settings.SubSettings", kept.Payload.GetProperty("className").GetString());
        var window = index.Events.Single(item => item.Channel == AndroidAccessibilityRecords.TreeChannel)
            .Payload.GetProperty("windows")[0];
        Assert.Equal(Settings, PhoneScreenEvidence.Read(window)!.Package);

        var archiveBuilder = new SessionPlaybackArchiveBuilder(_folder, retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(10), 10 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, false, 5, true, true),
            [], [], 1, 0, null));

        var visit = Assert.Single(archive.PhoneScreens.Visits);
        Assert.Equal(Settings, visit.Package);
        Assert.Equal(At(0.5), visit.Start);
        Assert.Equal("com.android.settings.SubSettings", visit.Activity);
    }
}

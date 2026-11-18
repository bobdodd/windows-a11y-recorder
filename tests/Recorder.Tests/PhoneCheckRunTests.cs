using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 3 of docs/architecture/android-checks.md, "Build order": the checks
// run on a phone recording, on each visit's settled snapshots, with the
// findings merged by visit, check, level and item. The recording is made
// up: a Settings window with a 40 dp "Save" button, its items taken twice
// and then listed without being taken again, at the Pixel 10 Pro XL's
// measured 390 dpi and 1080 by 2404 pixels.
public sealed class PhoneCheckRunTests : IDisposable
{
    private const long Second = 1_000_000_000;

    private const string Settings = "com.android.settings";

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "android.accessibility", "AndroidAccessibilityCollector", "1",
        [AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.TreeChannel, AndroidCompanionRecords.DisplayChannel, AndroidTouchRecords.TouchChannel,
            AndroidSettingsRecords.Channel, AndroidTouchRecords.KeyChannel],
        "test");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "phone-checks-" + Guid.NewGuid().ToString("N"));

    public PhoneCheckRunTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static long At(double seconds) => (long)Math.Round(seconds * Second);

    // The Settings window's page, its "Save" button, 98 pixels square
    // (40 dp), moved down by moved pixels, and its heading.
    private static JsonNode Items(int moved) =>
        JsonSerializer.SerializeToNode(
            new AndroidTreeNode[]
            {
                new()
                {
                    Index = 0, WindowId = 512, PackageName = Settings, ClassName = "android.widget.FrameLayout",
                    Bounds = new AndroidNodeBounds(0, 0, 1080, 2404), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true,
                },
                new()
                {
                    Index = 1, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "android.widget.Button",
                    ViewId = "com.android.settings:id/save", Text = "Save",
                    Bounds = new AndroidNodeBounds(100, 200 + moved, 198, 298 + moved), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true, Enabled = true, Clickable = true, Focusable = true,
                },

                // The page's title, marked as a heading, so the heading checks find nothing.
                new()
                {
                    Index = 2, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "android.widget.TextView",
                    Text = "Settings", Bounds = new AndroidNodeBounds(100, 60, 600, 130), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true, Heading = true,
                },
            },
            JsonSerializerOptions.Web)!;

    // A snapshot whose Settings window holds items, or is listed without
    // its items being taken when items is null.
    // With statusBarBottom, the status bar window is only that tall.
    private static AndroidAccessibilityTree Tree(long index, JsonNode? items, int? statusBarBottom = null)
    {
        var tree = JsonNode.Parse(AndroidAccessibilityTreeTests.TreeLine(
            index: index,
            everyWindow: index == 1,
            reason: index == 1 ? "requested" : "content-change"))!.AsObject();
        tree["windows"]![0]!["items"] = items;
        if (statusBarBottom is { } bottom)
        {
            tree["windows"]![1]!["bounds"]!["bottom"] = bottom;
        }

        var (read, problem) = CompanionProtocol.ReadTree(tree.ToJsonString());
        Assert.Null(problem);
        return read!;
    }

    // Another app's window in front, the Clock's, whose only item is its page.
    private static AndroidAccessibilityTree OtherApp(long index)
    {
        const string clock = "com.google.android.deskclock";
        var tree = JsonNode.Parse(AndroidAccessibilityTreeTests.TreeLine(index: index, everyWindow: true))!.AsObject();
        var window = tree["windows"]![0]!.AsObject();
        window["id"] = 600;
        window["title"] = "Clock";
        window["items"] = JsonSerializer.SerializeToNode(
            new AndroidTreeNode[]
            {
                new()
                {
                    Index = 0, WindowId = 600, PackageName = clock, ClassName = "android.widget.FrameLayout",
                    Bounds = new AndroidNodeBounds(0, 0, 1080, 2404), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true,
                },
            },
            JsonSerializerOptions.Web);
        var (read, problem) = CompanionProtocol.ReadTree(tree.ToJsonString());
        Assert.Null(problem);
        return read!;
    }

    // With returning, the Clock app is in front from 2 seconds, and
    // Settings again from 4, so Settings has two visits.
    private SessionPlaybackArchive Archive(out FakeRecords recording, bool returning = false)
    {
        List<(long Time, string Channel, string EventType, object Payload)> records = returning
            ?
            [
                (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
                    new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
                (At(1), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(1, Items(0))),
                (At(2), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, OtherApp(2)),
                (At(4), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(3, Items(10))),
                (At(6), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(4, null)),
            ]
            :
            [
                (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
                    new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
                (At(1), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(1, Items(0))),
                (At(3), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(2, Items(10))),
                (At(5), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(3, null)),
            ];
        return ArchiveOf(records, out recording);
    }

    private SessionPlaybackArchive ArchiveOf(List<(long Time, string Channel, string EventType, object Payload)> records, out FakeRecords recording)
    {
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        recording = new FakeRecords();
        var sequence = 0UL;
        foreach (var (time, channel, eventType, payload) in records)
        {
            sequence++;
            var record = RecorderEventFactory.Create("s", Collector, channel, sequence, time, eventType, payload);
            recording.Add(time, record);
            builder.Add((long)sequence, record);
        }

        var archiveBuilder = new SessionPlaybackArchiveBuilder(_folder, retainEvents: false);
        archiveBuilder.AddIndex(builder.Build());
        var started = DateTimeOffset.UtcNow;
        return archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(10), 10 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, false, 5, true, true),
            [], [], 1, 0, null));
    }

    // The phone's own 390 dpi for the whole recording.
    private static PhoneDeviceTimeline Device() =>
        new(
            [(0L, AndroidDeviceRecords.DensityEventType, JsonSerializer.SerializeToElement(
                new AndroidDisplayDensity("58191FDCQ00ABZ", AndroidDeviceRecords.StartReason, 390, null, [], null, 0, 0),
                JsonSerializerOptions.Web))],
            [],
            10 * Second);

    [Fact]
    public void Run_ChecksEachTakingOnce_AndMergesTheItemsFindingsAcrossThem()
    {
        var archive = Archive(out var recording);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(run.Problems);
        Assert.Equal((3, 2, 0), (run.SnapshotsChecked, run.WindowsChecked, run.WithoutDensity));
        var finding = Assert.Single(run.Findings);
        Assert.Equal((PhoneCheckNames.TouchTarget, PhoneCheckLevel.Error), (finding.Check, finding.Level));
        Assert.Equal("id:com.android.settings:id/save#0", finding.Identity);
        Assert.Same(finding, Assert.Single(run.For(Assert.Single(archive.PhoneScreens.Screens))));

        // The snapshot at 5 seconds shows the items taken at 3 seconds, so
        // is not another time.
        Assert.Equal([At(1), At(3)], finding.Times.Select(time => time.Time));
        Assert.Equal([At(1), At(3)], finding.Times.Select(time => time.TakenBy.MonotonicNanoseconds));
        Assert.Equal([200, 210], finding.Times.Select(time => time.Bounds.Top));
        Assert.All(finding.Times, time => Assert.Equal("window:512/0/0", time.TreeKey));
        Assert.Same(finding, Assert.Single(run.For(Assert.Single(archive.PhoneScreens.Visits))));
        Assert.Equal(
            "Error: Button \"Save\", 40 by 40 dp, too narrow and too short; ATF needs 48 by 48 dp; seen 2 times, first at 00:00:01.000",
            finding.Text);

        // Step 10: the focusable "Save" button, once for each taking, and
        // Settings' heading is text that is not focusable.
        Assert.Equal((2, 0), (run.Clickable, run.ClickableSetAside));
        Assert.Empty(run.TextFocusablePackages);

        // Step 11: the fixture has no field, invalid item, expandable item or slider.
        Assert.Equal((0, 0, 0, 0, 0), (run.Fields, run.Invalid, run.Expandable, run.ExpandableNotRecorded, run.Sliders));

        // Step 16: "Save" and "Settings", once for each taking.
        Assert.Equal(4, run.SequenceTexts);
    }

    // Step 10: clickable but not focusable (WCAG 2.1.1), from the tree.
    [Fact]
    public void ClickableNotFocusable_WarnsOnEachEnabledClickableItemNotFocusable_AndSetsAsideThoseInAFocusableControl()
    {
        const string Button = "android.widget.Button";
        var window = new PhoneCheckWindow(512, 1,
        [
            Control(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout", clickable: false),
            Control(1, 0, (0, 0, 500, 100), Button, text: "Save", focusable: true),
            Control(2, 0, (0, 100, 500, 200), "android.view.View", "Delete"),
            Control(3, 0, (0, 200, 1080, 600), "android.view.View", "Card", focusable: true),
            Control(4, 3, (10, 210, 300, 300), Button, "Share"),
            Control(5, 0, (0, 600, 500, 700), Button, "Archive", clickable: false, longClickable: true),
            Control(6, 0, (0, 700, 500, 800), Button, "Send", enabled: false),
            Control(7, 0, (0, 800, 500, 900), Button, "Hidden", visible: false),
            Control(8, 0, (0, 900, 500, 1000), "android.widget.TextView", text: "Messages", clickable: false),
            Control(9, 0, (0, 1000, 500, 1100), "android.widget.ImageView", "Logo", clickable: false),
        ]);

        var measured = PhoneFocusChecks.Measure(window);

        Assert.Equal([2, 5], measured.Results.Select(result => result.Item));
        Assert.All(measured.Results, result => Assert.Equal(
            (PhoneCheckNames.ClickableNotFocusable, PhoneCheckLevel.Warning), (result.Check, result.Level)));
        Assert.Equal(
            "it is clickable but not focusable, and no item it is in is focusable and clickable, so a keyboard or switch user may not reach it; " +
            "Android makes a clickable view focusable unless the app says not; a likely failure, until a keyboard test confirms it",
            measured.Results[0].Measured);
        Assert.StartsWith("it is long clickable but not focusable", measured.Results[1].Measured, StringComparison.Ordinal);

        // Checked: 1, 2, 3 and 5; 4 is inside the focusable card; 6 is not
        // enabled and 7 not shown. The text and the description that are not
        // controls are not focusable, as in a view-based app.
        Assert.Equal((4, 1, 2, 0), (measured.Clickable, measured.SetAside, measured.TextItems, measured.FocusableTextItems));
        Assert.Equal("Clickable but not focusable (WCAG 2.1.1)", PhoneCheckNames.Title(PhoneCheckNames.ClickableNotFocusable));
        var order = PhoneCheckNames.All.ToList();
        Assert.Equal(order.IndexOf(PhoneCheckNames.TraversalOrder) + 1, order.IndexOf(PhoneCheckNames.ClickableNotFocusable));
    }

    // A tree like the client's app's (measured, tests 16 and 18): every item
    // with text is focusable, clickable or not, as Flutter's bridge marks
    // them.
    [Fact]
    public void ClickableNotFocusable_CountsTextThatIsFocusable_AsAFlutterTreeHasIt()
    {
        var window = new PhoneCheckWindow(512, 1,
        [
            Control(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout", clickable: false, focusable: true),
            Control(1, 0, (0, 0, 1080, 100), "android.view.View", "Welcome, Bob Dodd", clickable: false, focusable: true),
            Control(2, 0, (0, 100, 500, 200), "android.widget.Button", "Sleep Goals", focusable: true),
        ]);

        var measured = PhoneFocusChecks.Measure(window);

        Assert.Empty(measured.Results);
        Assert.Equal((1, 0, 1, 1), (measured.Clickable, measured.SetAside, measured.TextItems, measured.FocusableTextItems));
    }

    // Step 11: a field labelled only by its hint fails WCAG 3.3.2, as the
    // hint is not shown once text is typed (owner, 2026-11-13); text above it
    // is quoted, not taken as its label (labeledBy only). As the client's
    // Sleep Diary notes field was measured: hint, no labeledBy, "Notes
    // (optional)" 39 pixels above it.
    [Fact]
    public void Fields_HintOnlyIsNoLabel_AndTheTextAboveIsQuoted()
    {
        const string Edit = "android.widget.EditText";
        var window = new PhoneCheckWindow(512, 1,
        [
            Control(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout", clickable: false),
            Control(1, 0, (59, 2159, 365, 2215), "android.view.View", "Notes (optional)", clickable: false, focusable: true),
            Control(2, 0, (59, 2254, 1022, 2404), Edit, editable: true, focusable: true) with { Hint = "Fell asleep quickly, woke up once..." },
            Control(3, 0, (0, 100, 1080, 200), "android.widget.TextView", text: "Name", clickable: false),
            Control(4, 0, (0, 200, 1080, 300), Edit, editable: true, labeledBy: [3]) with { Hint = "Your name" },
            Control(5, 0, (0, 300, 1080, 400), Edit, editable: true, visible: false),
        ]);

        var measured = PhoneFieldChecks.Measure(window, new PhoneCheckContext(2.625f, 1080, 2400));

        var result = Assert.Single(measured.Results);
        Assert.Equal((PhoneCheckNames.FieldNoLabel, PhoneCheckLevel.Error, 2), (result.Check, result.Level, result.Item));
        Assert.Equal(
            "its only label is its hint, \"Fell asleep quickly, woke up once...\", which is not shown once text is typed, " +
            "and no item is linked to it as its label (labeledBy); the text just above it, \"Notes (optional)\", is not linked to it as its label",
            result.Measured);
        Assert.Equal((2, 0, 0, 0, 0), (measured.Fields, measured.Invalid, measured.Expandable, measured.ExpandableNotRecorded, measured.Sliders));
        Assert.Equal("Field with no label (WCAG 3.3.2)", PhoneCheckNames.Title(PhoneCheckNames.FieldNoLabel));
        var order = PhoneCheckNames.All.ToList();
        Assert.Equal(order.IndexOf(PhoneCheckNames.ClickableNotFocusable) + 1, order.IndexOf(PhoneCheckNames.FieldNoLabel));
    }

    // A field with no hint, label or description has no label (3.3.2) and
    // no name (4.1.2): two discrete issues. Text further above it than the
    // field is tall is not quoted.
    [Fact]
    public void Fields_WithNothing_HaveNoLabelAndNoName()
    {
        var window = new PhoneCheckWindow(512, 1,
        [
            Control(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout", clickable: false),
            Control(1, 0, (0, 0, 1080, 90), "android.widget.TextView", text: "Email", clickable: false),
            Control(2, 0, (0, 191, 1080, 291), "android.widget.EditText", editable: true),
        ]);

        var results = PhoneFieldChecks.Run(window, new PhoneCheckContext(2.625f, 1080, 2400));

        Assert.Equal(
            [(PhoneCheckNames.FieldNoLabel, PhoneCheckLevel.Error), (PhoneCheckNames.FieldNoName, PhoneCheckLevel.Error)],
            results.Select(result => (result.Check, result.Level)));
        Assert.Equal("it has no label: no hint, and no item linked to it as its label (labeledBy)", results[0].Measured);
        Assert.Equal(
            "it has no name: no item linked to it as its label, no hint and no content description, so a screen reader can say only what is typed in it",
            results[1].Measured);
    }

    // Marked invalid with no error text is a warning (3.3.1); an expandable
    // item whose state is undefined (API 36) and has no state description
    // is a warning (4.1.2), and one whose state was not recorded (before
    // Android 16) is counted, not checked; a slider with no range is an
    // error (4.1.2), "Range info required" (owner).
    [Fact]
    public void States_InvalidWithNoError_ExpandableWithNoState_AndSliderWithNoRange()
    {
        static IReadOnlyList<AndroidNodeAction> Actions(params int[] ids) => [.. ids.Select(id => new AndroidNodeAction(id, null))];
        var window = new PhoneCheckWindow(512, 1,
        [
            Control(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout", clickable: false),
            Control(1, 0, (0, 0, 1080, 100), "android.widget.EditText", editable: true, labeledBy: [0]) with { ContentInvalid = true },
            Control(2, 0, (0, 100, 1080, 200), "android.widget.EditText", editable: true, labeledBy: [0]) with { ContentInvalid = true, Error = "Enter a date" },
            Control(3, 0, (0, 200, 1080, 300), "android.widget.Button", "Details") with { Actions = Actions(16, PhoneFieldChecks.ActionExpand), ExpandedState = 0 },
            Control(4, 0, (0, 300, 1080, 400), "android.widget.Button", "More") with { Actions = Actions(PhoneFieldChecks.ActionCollapse), ExpandedState = 3 },
            Control(5, 0, (0, 400, 1080, 500), "android.widget.Button", "Older") with { Actions = Actions(PhoneFieldChecks.ActionExpand) },
            Control(6, 0, (0, 500, 1080, 600), "android.widget.Button", "Said") with
            {
                Actions = Actions(PhoneFieldChecks.ActionExpand), ExpandedState = 0, StateDescription = "Collapsed",
            },
            Control(7, 0, (0, 600, 1080, 700), "android.widget.SeekBar", "Volume, 50%", clickable: false, focusable: true),
            Control(8, 0, (0, 700, 1080, 800), "android.widget.SeekBar", "Font size", clickable: false) with
            {
                RangeInfo = new AndroidRangeInfo(1, 0, 6, 1),
            },
            Control(9, 0, (0, 800, 1080, 900), "android.view.View", clickable: false) with { Actions = Actions(PhoneFieldChecks.ActionSetProgress) },
        ]);

        var measured = PhoneFieldChecks.Measure(window, new PhoneCheckContext(2.625f, 1080, 2400));

        Assert.Equal(
            [
                (PhoneCheckNames.InvalidNoError, PhoneCheckLevel.Warning, 1),
                (PhoneCheckNames.ExpandableNoState, PhoneCheckLevel.Warning, 3),
                (PhoneCheckNames.SliderNoRange, PhoneCheckLevel.Error, 7),
                (PhoneCheckNames.SliderNoRange, PhoneCheckLevel.Error, 9),
            ],
            measured.Results.Select(result => (result.Check, result.Level, result.Item)));
        Assert.StartsWith("it is marked invalid, but the tree gives it no error text", measured.Results[0].Measured, StringComparison.Ordinal);
        Assert.StartsWith("it can be expanded, but its expanded state is undefined", measured.Results[1].Measured, StringComparison.Ordinal);
        Assert.Equal(
            "it is a slider with no range information, so its value, minimum and maximum are not given to a screen reader; " +
            "its value may be in its words, \"Volume, 50%\", but not as a range",
            measured.Results[2].Measured);
        Assert.Equal(
            "it is a slider with no range information, so its value, minimum and maximum are not given to a screen reader",
            measured.Results[3].Measured);
        Assert.Equal((2, 2, 4, 1, 3), (measured.Fields, measured.Invalid, measured.Expandable, measured.ExpandableNotRecorded, measured.Sliders));
    }

    // The owner, 2026-11-12: a general AA or AAA audit level guides the
    // checks. At AAA the 40 dp "Save" button also fails WCAG 2.5.5's 44 dp,
    // as one more finding; at AA (the test above) it does not.
    [Fact]
    public void Run_AtAAA_AlsoChecksTargetsTo44dp()
    {
        var archive = Archive(out var recording);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            cancellationToken: TestContext.Current.CancellationToken, level: PhoneAuditLevel.AAA);

        Assert.Equal(PhoneAuditLevel.AAA, run.Level);
        Assert.Equal([PhoneCheckNames.TouchTargetEnhanced, PhoneCheckNames.TouchTarget], run.Findings.Select(finding => finding.Check).Order(StringComparer.Ordinal));
        var finding = Assert.Single(run.Findings, finding => finding.Check == PhoneCheckNames.TouchTargetEnhanced);
        Assert.Equal(PhoneCheckLevel.Error, finding.Level);
        Assert.Equal(
            "Error: Button \"Save\", 40.2 by 40.2 dp, too narrow and too short; WCAG 2.5.5 needs 44 by 44 dp; seen 2 times, first at 00:00:01.000",
            finding.Text);
    }

    // A list that scrolls with a 16.4 dp tall "Reset" button in it, against
    // the list's bottom edge, or moved up so it is seen whole.
    private static JsonNode ListItems(bool atEdge) =>
        JsonSerializer.SerializeToNode(
            new AndroidTreeNode[]
            {
                new()
                {
                    Index = 0, WindowId = 512, PackageName = Settings, ClassName = "android.widget.FrameLayout",
                    Bounds = new AndroidNodeBounds(0, 0, 1080, 2404), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true,
                },
                new()
                {
                    Index = 1, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "androidx.recyclerview.widget.RecyclerView",
                    Bounds = new AndroidNodeBounds(0, 0, 1080, 1000), Checked = "unchecked", Scrollable = true,
                    Actions = [new AndroidNodeAction(0x1000, null)], ImportantForAccessibility = true, VisibleToUser = true,
                },
                new()
                {
                    Index = 2, Parent = 1, Depth = 2, WindowId = 512, PackageName = Settings, ClassName = "android.widget.Button",
                    ViewId = "com.android.settings:id/reset", Text = "Reset",
                    Bounds = atEdge ? new AndroidNodeBounds(100, 960, 600, 1000) : new AndroidNodeBounds(100, 500, 600, 540), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true, Enabled = true, Clickable = true, Focusable = true,
                },
            },
            JsonSerializerOptions.Web)!;

    // The owner, 2026-11-16: a target not run against the edge of a list
    // that scrolls in one snapshot, but seen whole in another of the same
    // visit, is judged by that sighting, so its not-run finding goes.
    [Fact]
    public void Run_ATargetCutOffInOneSnapshot_ButSeenWholeInAnother_IsJudgedWhole()
    {
        var archive = ArchiveOf(
        [
            (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
                new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
            (At(1), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(1, ListItems(atEdge: true))),
            (At(3), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(2, ListItems(atEdge: false))),
            (At(5), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(3, null)),
        ], out var recording);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            cancellationToken: TestContext.Current.CancellationToken, level: PhoneAuditLevel.AAA);

        var enhanced = Assert.Single(run.Findings, finding => finding.Check == PhoneCheckNames.TouchTargetEnhanced);
        Assert.Equal(PhoneCheckLevel.Error, enhanced.Level);
        Assert.Equal(1, run.TargetsSeenWhole);

        // ATF's own check is left as ATF has it.
        Assert.Contains(run.Findings, finding => finding.Check == PhoneCheckNames.TouchTarget && finding.Level == PhoneCheckLevel.NotRun);
    }

    // The owner, 2026-11-12: findings belong to the visit they were seen
    // in, as the screen's content can change between visits. Settings,
    // then the Clock app, then Settings again: the same button failing on
    // both visits to Settings is two findings, one for each.
    [Fact]
    public void Run_KeepsEachVisitsFindingsApart_ForTheSameItem()
    {
        var archive = Archive(out var recording, returning: true);
        var settings = archive.PhoneScreens.Visits.Where(visit => visit.Screen.Package == Settings).ToList();
        Assert.Equal(2, settings.Count);
        Assert.Same(settings[0].Screen, settings[1].Screen);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording, cancellationToken: TestContext.Current.CancellationToken);

        var findings = run.Findings.Where(finding => finding.Screen == settings[0].Screen).ToList();
        Assert.Equal(2, findings.Count);
        Assert.Equal(findings, run.For(settings[0].Screen));
        Assert.Same(findings[0], Assert.Single(run.For(settings[0])));
        Assert.Same(findings[1], Assert.Single(run.For(settings[1])));
        Assert.All(findings, finding => Assert.Equal("id:com.android.settings:id/save#0", finding.Identity));
        Assert.Equal([settings[0], settings[1]], findings.Select(finding => finding.Visit));
        Assert.EndsWith("seen once, at 00:00:01.000", findings[0].Text, StringComparison.Ordinal);
        Assert.EndsWith("seen once, at 00:00:04.000", findings[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ItemsTakenAfterTheDensityChangedInTheVisit_AreNotTouchChecked()
    {
        var archive = Archive(out var recording);

        // Display size set to 440 dpi at 2 seconds, between the two
        // takings, as in test 9; the window was not replaced.
        var device = new PhoneDeviceTimeline(
            [(0L, AndroidDeviceRecords.DensityEventType, JsonSerializer.SerializeToElement(
                new AndroidDisplayDensity("58191FDCQ00ABZ", AndroidDeviceRecords.StartReason, 390, null, [], null, 0, 0),
                JsonSerializerOptions.Web))],
            [(At(2), AndroidSettingsRecords.ChangeEventType, JsonSerializer.SerializeToElement(
                new AndroidSettingChanged("58191FDCQ00ABZ", "secure/display_density_forced", "440", null, AndroidSettingsRecords.NoticeSource, null, null, At(2)),
                JsonSerializerOptions.Web))],
            10 * Second);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, device, recording, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((0, 2), (run.WithoutDensity, run.DensityChanged));
        var finding = Assert.Single(run.Findings);
        Assert.Equal([At(1)], finding.Times.Select(time => time.Time));
    }

    [Fact]
    public void Run_WithoutTheDensity_SkipsTheTouchChecks_AndCountsTheSnapshots()
    {
        var archive = Archive(out var recording);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, PhoneDeviceTimeline.Empty, recording, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(run.Findings);
        Assert.Equal(3, run.WithoutDensity);
    }

    [Fact]
    public void Run_WithoutTheRecordsSource_FindsNothing()
    {
        var archive = Archive(out _);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(run.Findings);
        Assert.Equal(0, run.SnapshotsChecked);
    }

    // Step 4c: the text contrast check, from a measurement of the same
    // recording's settled snapshots at 1 and 3 seconds, made up: "Save" in
    // #777777 on white, 4.48:1, on both, and "Sale 50%" drawn in a picture
    // in #959595, 2.995:1, and black text, on the first.
    [Fact]
    public void TextContrast_MergesALinesFindingsAcrossSnapshots_AtTheChosenLevel()
    {
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var window = new PhoneCheckWindow(512, 1, Items(0).Deserialize<AndroidTreeNode[]>(JsonSerializerOptions.Web)!);
        var save = window.Items[1];
        var page = window.Items[0];
        PhoneTextLine Line(PhoneCheckItem? item, PhoneTextPlace place, string ocr, uint foreground, int top) =>
            new(item, item ?? page, place, item?.Text, ocr, new PhoneBounds(110, top, 190, top + 55),
                new PhoneSwatch(0xFFFFFF, foreground, 0, 2, 2), 39);
        // Each picture is taken 250 ms after its snapshot settled.
        PhoneContrastSnapshot Snapshot(int settled, params PhoneTextLine[] lines)
        {
            var at = visit.SettledSnapshots[settled];
            return new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Screenshot, at.MonotonicNanoseconds + 250_000_000, "p.png", "p.png"),
                "Windows OCR, en-US", lines);
        }

        var measured = PhoneContrastRun.Of(
        [
            Snapshot(0,
                Line(save, PhoneTextPlace.AppTextPlacedByOcr, "Save", 0x777777, 220),
                Line(null, PhoneTextPlace.FoundByOcr, "Sale 50%", 0x959595, 600),
                Line(null, PhoneTextPlace.FoundByOcr, "Open", 0x000000, 700)),
            Snapshot(1, Line(save, PhoneTextPlace.AppTextPlacedByOcr, "Save", 0x777777, 230)),
        ]);

        var aa = PhoneTextContrastRun.Run(measured, archive.PhoneScreens, Device(), PhoneAuditLevel.AA);

        Assert.Equal((4, 1), (aa.Lines, aa.Passed));
        Assert.Equal(4, aa.LinesOn(visit.Screen));
        Assert.Equal(PhoneCheckNames.TextContrastAa, aa.Check);
        Assert.Equal([PhoneCheckLevel.Error, PhoneCheckLevel.Warning], aa.Findings.Select(finding => finding.Level));
        var found = aa.Findings[0];
        Assert.StartsWith("'Sale 50%' in ", found.Label, StringComparison.Ordinal);
        Assert.Contains("text found by OCR (Windows OCR, en-US), in no item's text", found.Times[0].Measured, StringComparison.Ordinal);
        var line = aa.Findings[1];
        Assert.Equal(save.Identity + "\u001Fline 1", line.Identity);
        Assert.Equal([220, 230], line.Times.Select(time => time.Bounds.Top));
        Assert.All(line.Times, time => Assert.Equal(save.TreeKey, time.TreeKey));

        // A finding's times are its pictures', so the player shows what was measured.
        Assert.Equal(
            visit.SettledSnapshots.Take(2).Select(at => at.MonotonicNanoseconds + 250_000_000),
            line.Times.Select(time => time.Time));
        Assert.Equal(
            "Warning: Button \"Save\", 4.48:1, #777777 on #FFFFFF; fails unless the text is large: 4.5:1 is needed, 3:1 for large text; " +
            "the words are 16 dp tall in OCR's box; the text's size and weight are not known; from the phone's screenshot; " +
            "the app's text, placed on the screen by OCR (Windows OCR, en-US); seen 2 times, first at 00:00:01.250",
            line.Text);

        // At AAA the same measurement is two errors.
        var aaa = PhoneTextContrastRun.Run(measured, archive.PhoneScreens, Device(), PhoneAuditLevel.AAA);

        Assert.Equal(PhoneCheckNames.TextContrastAaa, aaa.Check);
        Assert.All(aaa.Findings, finding => Assert.Equal(PhoneCheckLevel.Error, finding.Level));
        Assert.Equal(2, aaa.Findings.Count);
        Assert.Contains("below 4.5:1, the limit for large text", aaa.Findings[1].Times[0].Measured, StringComparison.Ordinal);
    }

    // Step 5: the non-text contrast check, from the same measurement of the
    // bottom bar's tabs on two settled snapshots of the visit.
    [Fact]
    public void NonTextContrast_MergesAControlsFindingsInAVisit_AndCountsTheControls()
    {
        var (lines, measures) = PhoneNonTextContrastTests.TabsMeasured();
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        PhoneContrastSnapshot Snapshot(int settled)
        {
            var at = visit.SettledSnapshots[settled];
            return new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Screenshot, at.MonotonicNanoseconds + 250_000_000, "p.png", "p.png"),
                "Windows OCR, en-US", lines, measures);
        }

        var run = PhoneNonTextContrastRun.Run(PhoneContrastRun.Of([Snapshot(0), Snapshot(1)]), archive.PhoneScreens);

        Assert.Equal((8, 0), (run.Controls, run.Passed));
        Assert.Equal(8, run.ControlsIn(visit));
        Assert.Equal(4, run.Findings.Count);
        Assert.All(run.Findings, finding => Assert.Equal(PhoneCheckNames.NonTextContrast, finding.Check));
        var home = run.Findings[0];
        Assert.Equal(PhoneCheckLevel.Error, home.Level);
        Assert.Equal(2, home.Times.Count);
        Assert.EndsWith("; a state needs 3:1; from the phone's screenshot", home.Times[0].Measured, StringComparison.Ordinal);
        Assert.Equal("Non-text contrast (WCAG 1.4.11)", PhoneCheckNames.Title(home.Check));
    }

    // Step 8: images of text. A window with a picture of a title and its
    // subtitle, described as both; a picture with a TextView drawn over it;
    // a picture with an icon OCR reads as two letters; and a picture with no
    // description whose words are found in no item.
    private static AndroidTreeNode ImageNode(
        int index, int? parent, (int Left, int Top, int Right, int Bottom) bounds, string className,
        string? description = null, string? text = null) =>
        new()
        {
            Index = index,
            Parent = parent,
            ClassName = className,
            Bounds = new AndroidNodeBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            ContentDescription = description,
            Text = text,
            ImportantForAccessibility = true,
            VisibleToUser = true,
            Enabled = true,
            Checked = "unchecked",
        };

    // A test can add an item beside the images, move the sale's line, or
    // add lines OCR read outside every image.
    private static (PhoneCheckWindow Window, PhoneTextLine[] Lines) PicturesOfText(
        AndroidTreeNode? extra = null, (int Left, int Top, int Right, int Bottom)? sale = null, params string[] outside)
    {
        var window = new PhoneCheckWindow(512, 1,
        [
            ImageNode(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout"),
            ImageNode(1, 0, (59, 324, 1022, 724), "android.widget.ImageView", "Sleep Hygiene\nSteps to take for a good night's sleep."),
            ImageNode(2, 0, (59, 724, 1022, 1124), "android.widget.ImageView"),
            ImageNode(3, 0, (100, 800, 900, 900), "android.widget.TextView", text: "Daily tips"),
            ImageNode(4, 0, (59, 1124, 1022, 1523), "android.widget.ImageButton", "Settings"),
            ImageNode(5, 0, (59, 1523, 1022, 1923), "android.widget.ImageView"),
            .. extra is null ? Array.Empty<AndroidTreeNode>() : [extra],
        ]);
        var items = window.Items;
        PhoneTextLine Line(PhoneCheckItem? owner, PhoneCheckItem container, PhoneTextPlace place, string ocr, int left, int top, int right, int bottom) =>
            new(owner, container, place, owner?.Node.ContentDescription ?? owner?.Text, ocr, new PhoneBounds(left, top, right, bottom), null, bottom - top);
        var box = sale ?? (100, 1600, 500, 1642);
        return (window,
        [
            Line(items[1], items[1], PhoneTextPlace.AppTextPlacedByOcr, "Sleep Hygiene", 100, 400, 500, 442),
            Line(items[1], items[1], PhoneTextPlace.AppTextPlacedByOcr, "Steps to take for a good night's sleep.", 100, 460, 900, 489),
            Line(items[3], items[3], PhoneTextPlace.AppTextPlacedByOcr, "Daily tips", 100, 820, 400, 860),
            Line(null, items[4], PhoneTextPlace.FoundByOcr, "Q:", 500, 1300, 560, 1340),
            Line(null, items[5], PhoneTextPlace.FoundByOcr, "Sale 50%", box.Left, box.Top, box.Right, box.Bottom),
            .. outside.Select((text, index) => Line(null, items[0], PhoneTextPlace.FoundByOcr, text, 100, 2000 + (index * 80), 700, 2040 + (index * 80))),
        ]);
    }

    // The images-of-text run of the pictures on the first settled snapshot
    // of the recording's one visit, from the phone's screenshot.
    private PhoneImagesOfTextRun ImagesOfTextRun(
        (PhoneCheckWindow Window, PhoneTextLine[] Lines) pictures,
        PhoneDeviceTimeline? device = null,
        TalkBackSpeechTimeline? speech = null,
        PhoneAuditLevel level = PhoneAuditLevel.AA)
    {
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var at = visit.SettledSnapshots[0];
        var measured = PhoneContrastRun.Of(
        [
            new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Screenshot, at.MonotonicNanoseconds, "p.png", "p.png"),
                "Windows OCR, en-US", pictures.Lines, null, pictures.Window),
        ]);
        return PhoneImagesOfTextRun.Run(measured, archive.PhoneScreens, device ?? Device(), level, speech);
    }

    // The recording's one visit's app, detected as drawn with Flutter at the stop.
    private PhoneDeviceTimeline FlutterDevice()
    {
        var package = Assert.Single(Archive(out _).PhoneScreens.Visits).Screen.Package;
        var app = new AndroidAppVersion(package, true, "1.0", 1, 24, 35, null, null, [])
        {
            Framework = new AndroidAppFramework("apk-entries", "detected", ["Flutter"], ["lib/arm64-v8a/libflutter.so"], 1, 1, null),
        };
        return new(
            [
                (0L, AndroidDeviceRecords.DensityEventType, JsonSerializer.SerializeToElement(
                    new AndroidDisplayDensity("58191FDCQ00ABZ", AndroidDeviceRecords.StartReason, 390, null, [], null, 0, 0),
                    JsonSerializerOptions.Web)),
                (9 * Second, AndroidDeviceRecords.AppVersionsEventType, JsonSerializer.SerializeToElement(
                    new AndroidAppVersions("58191FDCQ00ABZ", [app], [], 9 * Second, 9 * Second), JsonSerializerOptions.Web)),
            ],
            [],
            10 * Second);
    }

    [Fact]
    public void ImagesOfText_WarnsOfWordsInAnImageNoItemsTextHolds_AndMergesThemInAVisit()
    {
        var (window, lines) = PicturesOfText();
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        PhoneContrastSnapshot Snapshot(int settled, PhonePictureKind kind, string? engine = "Windows OCR, en-US")
        {
            var at = visit.SettledSnapshots[settled];
            return new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(kind, at.MonotonicNanoseconds + 250_000_000, "p.png", "p.png"),
                engine, lines, null, window);
        }

        var measured = PhoneContrastRun.Of([Snapshot(0, PhonePictureKind.Frame), Snapshot(1, PhonePictureKind.Screenshot)]);
        var run = PhoneImagesOfTextRun.Run(measured, archive.PhoneScreens, Device(), PhoneAuditLevel.AA);

        // Four images on each of two snapshots: the TextView's words are the
        // app's text, and "Q" is too few letters.
        Assert.Equal((2, 8, 4), (run.SnapshotsRead, run.Images, run.Passed));
        Assert.True(run.Read(visit));
        Assert.Equal(8, run.ImagesIn(visit));
        Assert.Equal(2, run.Findings.Count);
        Assert.All(run.Findings, finding => Assert.Equal((PhoneCheckNames.ImageOfText, PhoneCheckLevel.Warning), (finding.Check, finding.Level)));
        Assert.Equal(run.Findings, run.For(visit));
        var described = run.Findings[0];
        Assert.Equal(window.Items[1].Identity, described.Identity);
        Assert.Equal([new PhoneBounds(59, 324, 1022, 724), new PhoneBounds(59, 324, 1022, 724)], described.Times.Select(time => time.Bounds));
        Assert.All(described.Times, time => Assert.Equal(window.Items[1].TreeKey, time.TreeKey));
        Assert.Equal(
            "OCR (Windows OCR, en-US) read 2 lines of text inside the image: \"Sleep Hygiene\", \"Steps to take for a good night's sleep.\"; " +
            "the words are 11.9 to 17.2 dp tall in OCR's box; its description holds them; " +
            "a logotype, a picture with significant other visual content and an image that can be customized are not judged; approximate, from the video",
            described.Times[0].Measured);
        Assert.EndsWith("; from the phone's screenshot", described.Times[1].Measured, StringComparison.Ordinal);
        Assert.Contains("(other values at other times)", described.Text, StringComparison.Ordinal);
        Assert.Contains("\"Sale 50%\"; the words are 17.2 dp tall in OCR's box; it has no description;", run.Findings[1].Times[0].Measured, StringComparison.Ordinal);
        Assert.Equal("Image of text (WCAG 1.4.5)", PhoneCheckNames.Title(described.Check));

        // At AAA each is a 1.4.9 warning as well, after the 1.4.5 ones.
        var aaa = PhoneImagesOfTextRun.Run(measured, archive.PhoneScreens, Device(), PhoneAuditLevel.AAA);

        Assert.Equal(
            [PhoneCheckNames.ImageOfText, PhoneCheckNames.ImageOfText, PhoneCheckNames.ImageOfTextAaa, PhoneCheckNames.ImageOfTextAaa],
            aaa.Findings.Select(finding => finding.Check));
        Assert.Contains("1.4.9 allows an image of text only as pure decoration or where essential", aaa.Findings[2].Times[0].Measured, StringComparison.Ordinal);
        Assert.Equal("Image of text (WCAG 1.4.9, AAA)", PhoneCheckNames.Title(aaa.Findings[2].Check));

        // With no OCR there is nothing to read.
        var unread = PhoneImagesOfTextRun.Run(PhoneContrastRun.Of([Snapshot(0, PhonePictureKind.Frame, engine: null)]), archive.PhoneScreens, Device(), PhoneAuditLevel.AA);

        Assert.Equal((0, 0), (unread.SnapshotsRead, unread.Findings.Count));
        Assert.False(unread.Read(visit));
    }

    // Step 19: text shown as a heading. A window of made-up text: a large
    // title, body text, a line in capitals, a field's label in capitals
    // above its field, large text in a button, a marked heading, a line in
    // capitals with a digit, large text in three lines and a large line of
    // eleven words.
    private static (PhoneCheckWindow Window, PhoneTextLine[] Lines) HeadingLooks()
    {
        AndroidTreeNode Node(int index, int? parent, (int Left, int Top, int Right, int Bottom) bounds, string className, string? text = null,
            bool clickable = false, bool editable = false, bool heading = false) =>
            ImageNode(index, parent, bounds, className, text: text) with { Clickable = clickable, Editable = editable, Heading = heading };
        const string body = "Plain body text that runs across the screen in one long line of small letters, longer than every other line here.";
        var window = new PhoneCheckWindow(512, 1,
        [
            Node(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout"),
            Node(1, 0, (60, 200, 1020, 300), "android.widget.TextView", "Your week"),
            Node(2, 0, (60, 320, 1020, 420), "android.widget.TextView", body),
            Node(3, 0, (60, 500, 1020, 560), "android.widget.TextView", "Settings"),
            Node(4, 0, (60, 700, 600, 740), "android.widget.TextView", "Email"),
            Node(5, 0, (60, 760, 1020, 860), "android.widget.EditText", editable: true),
            Node(6, 0, (60, 1000, 1020, 1100), "android.widget.Button", clickable: true),
            Node(7, 6, (80, 1010, 1000, 1090), "android.widget.TextView", "Start now"),
            Node(8, 0, (60, 1200, 1020, 1300), "android.widget.TextView", "Marked title", heading: true),
            Node(9, 0, (60, 1400, 1020, 1450), "android.widget.TextView", "Top 10"),
            Node(10, 0, (60, 1500, 1020, 1800), "android.widget.TextView", "One two three"),
            Node(11, 0, (60, 1900, 1020, 2000), "android.widget.TextView", "One two three four five six seven eight nine ten eleven"),
        ]);
        var items = window.Items;
        PhoneTextLine Line(int item, string ocr, int top, int ink) =>
            new(items[item], items[item], PhoneTextPlace.AppTextPlacedByOcr, items[item].Text, ocr,
                new PhoneBounds(80, top, 1000, top + ink + 10), new PhoneSwatch(0xFFFFFF, 0x000000, 21, 2, 2), ink + 6, ink);
        return (window,
        [
            Line(1, "Your week", 210, 60),
            Line(2, body, 330, 30),
            Line(3, "SETTINGS", 510, 30),
            Line(4, "EMAIL", 700, 30),
            Line(7, "Start now", 1015, 60),
            Line(8, "Marked title", 1210, 60),
            Line(9, "TOP 10", 1405, 30),
            Line(10, "One", 1510, 60),
            Line(10, "two", 1590, 60),
            Line(10, "three", 1670, 60),
            Line(11, "One two three four five six seven eight nine ten eleven", 1910, 60),
        ]);
    }

    private PhoneHeadingLookRun HeadingLookRun((PhoneCheckWindow Window, PhoneTextLine[] Lines) screen, string? engine = "Windows OCR, en-US")
    {
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var measured = PhoneContrastRun.Of([.. visit.SettledSnapshots.Take(2).Select(at =>
            new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Screenshot, at.MonotonicNanoseconds, "p.png", "p.png"),
                engine, screen.Lines, null, screen.Window))]);
        return PhoneHeadingLookRun.Run(measured, archive.PhoneScreens);
    }

    [Fact]
    public void HeadingLook_WarnsOfLargeTextAndALineInCapitals_NotMarkedAsHeadings_AndLeavesOutControlsAndFieldLabels()
    {
        var (window, lines) = HeadingLooks();
        var run = HeadingLookRun((window, lines));
        var visit = run.Findings[0].Visit;

        // Nine items on each of two snapshots; the button's text and the
        // field's label are counted once in the visit.
        Assert.Equal((2, 18), (run.SnapshotsRead, run.Items));
        Assert.Equal(18, run.ItemsIn(visit));
        Assert.True(run.Read(visit));
        Assert.Equal((1, 1), (run.InControls, run.FieldLabels));
        Assert.Equal([window.Items[1].Identity, window.Items[3].Identity], run.Findings.Select(finding => finding.Identity));
        Assert.All(run.Findings, finding => Assert.Equal((PhoneCheckNames.TextShownAsHeading, PhoneCheckLevel.Warning), (finding.Check, finding.Level)));
        Assert.All(run.Findings, finding => Assert.Equal(2, finding.Times.Count));
        Assert.Equal(
            "its letters are 2 times as tall as the screen's body text (60 pixels against 30 on the picture), in 1 line, and is not marked as a heading; " +
            "whether it is shown as a heading for the content after it is not judged; from the phone's screenshot",
            run.Findings[0].Times[0].Measured);
        Assert.StartsWith("it is in capitals, \"SETTINGS\", in 1 line,", run.Findings[1].Times[0].Measured, StringComparison.Ordinal);
        Assert.Equal("Text shown as a heading, not marked as one (WCAG 1.3.1)", PhoneCheckNames.Title(PhoneCheckNames.TextShownAsHeading));

        // One question for each finding, with the box of the item's lines.
        Assert.Equal(2, run.Questions.Count);
        Assert.Equal(new PhoneBounds(80, 210, 1000, 280), run.Questions[0].Lines);
        Assert.Equal("Your week", run.Questions[0].Text);
        Assert.Same(run.Questions[1], run.QuestionOf(run.Findings[1]));

        // With no OCR, nothing is placed on the picture.
        var unread = HeadingLookRun((window, lines), engine: null);

        Assert.Equal((0, 0), (unread.SnapshotsRead, unread.Findings.Count));
        Assert.False(unread.Read(visit));
    }

    [Fact]
    public void HeadingLook_Judged_GivesAnswersLevelsAndDropsTheNoteOfWhatIsNotJudged()
    {
        var run = HeadingLookRun(HeadingLooks());
        var inference = new PhoneInference(PhoneCheckLevel.Error, "a model inferred it is a heading", Inferred: true, "Model", Cloud: false);

        var judged = run.Judged(new Dictionary<PhoneHeadingQuestion, PhoneInference> { [run.Questions[0]] = inference }, "1 answered");

        Assert.Equal([PhoneCheckLevel.Error, PhoneCheckLevel.Warning], judged.Findings.Select(finding => finding.Level));
        Assert.Same(inference, judged.Findings[0].Inference);
        Assert.DoesNotContain("is not judged", judged.Findings[0].Times[0].Measured, StringComparison.Ordinal);
        Assert.Contains("is not judged", judged.Findings[1].Times[0].Measured, StringComparison.Ordinal);
        Assert.Null(judged.QuestionOf(judged.Findings[0]));
        Assert.NotNull(judged.QuestionOf(judged.Findings[1]));
        Assert.Equal(("1 answered", 1, 1), (judged.ModelNote, judged.InControls, judged.FieldLabels));
    }

    // The request is the same for the same picture and text; picture B is
    // the screen across, from a line's height above the text to 30% of the
    // screen's height below it.
    [Fact]
    public void HeadingLook_RequestIsTheSameForTheSameText_AndKeptAnswersJudgeIt()
    {
        var run = HeadingLookRun(HeadingLooks());
        var rgb = new uint[1080 * 2400];
        Array.Fill(rgb, 0x203040u);
        var pixels = new PhonePixels(1080, 2400, rgb);

        var requests = run.Questions.Select(question => Recorder.Analysis.Android.AndroidHeadingLook.RequestOf(question, pixels)).ToList();
        var again = Recorder.Analysis.Android.AndroidHeadingLook.RequestOf(run.Questions[0], new PhonePixels(1080, 2400, [.. rgb]));

        Assert.Equal(requests[0].Key, again.Key);
        Assert.NotEqual(requests[0].Key, requests[1].Key);
        Assert.Matches("^text-shown-as-heading-[0-9a-f]{16}$", requests[0].Key);
        Assert.Equal(Recorder.Analysis.ModelUses.VisionChecks, requests[0].Request.Use);
        Assert.True(requests[0].Request.ReusedAcrossTasks);
        Assert.Equal(Recorder.Analysis.Android.AndroidHeadingLook.Answers, requests[0].Request.Answers);
        Assert.Equal((540, 1200), PngSize(requests[0].Request.Images[0].Content));
        Assert.Equal((1080, 860), PngSize(requests[0].Request.Images[1].Content));
        Assert.Contains("The outlined text, as the app gives it: \"Your week\".", requests[0].Request.Input, StringComparison.Ordinal);

        static JsonElement Output(string answer) =>
            JsonSerializer.SerializeToElement(new { answer, reason = "The pictures show it." });
        var kept = new Dictionary<string, Recorder.Analysis.KeptAnswer>
        {
            [requests[0].StepKey(1)] = new(Output("heading"), "stated", """{"temperature": 0}"""),
            [requests[1].StepKey(1)] = new(Output("not-heading"), "service", """{"temperature": null}"""),
        };

        var judged = Recorder.Analysis.Android.AndroidHeadingLook.FromKept(run, requests, kept, "Haiku");

        Assert.Equal([PhoneCheckLevel.Error, PhoneCheckLevel.Warning], judged.Findings.Select(finding => finding.Level));
        Assert.StartsWith("Haiku, a local model, inferred from the pictures that the text is shown as a heading", judged.Findings[0].Inference!.Text, StringComparison.Ordinal);
        Assert.Equal("2 pieces of text left by the rules are for Haiku; 1 piece of text answered, 1 piece of text not yet asked", judged.ModelNote);

        kept[requests[1].StepKey(2)] = new(Output("not-heading"), "service", """{"temperature": null}""");
        var both = Recorder.Analysis.Android.AndroidHeadingLook.FromKept(run, requests, kept, "Haiku");

        Assert.Equal(PhoneCheckLevel.Info, both.Findings[1].Level);
        Assert.True(both.Findings[1].Inference!.Cloud);

        static (int Width, int Height) PngSize(byte[] png) =>
            ((png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19], (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);
    }

    // Step 20: images with no text alternative. A window of made-up items:
    // an image with a description, an image that is a control, an image in
    // a control with a name, a background under a button with a name, a
    // hairline 3 pixels high, and an image beside a line of text.
    private static PhoneCheckWindow ImageAlternatives()
    {
        AndroidTreeNode Node(int index, int? parent, (int Left, int Top, int Right, int Bottom) bounds, string className,
            string? description = null, string? text = null, bool clickable = false) =>
            ImageNode(index, parent, bounds, className, description, text) with { Clickable = clickable };
        return new PhoneCheckWindow(512, 1,
        [
            Node(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout"),
            Node(1, 0, (59, 200, 259, 400), "android.widget.ImageView", description: "Logo"),
            Node(2, 0, (300, 200, 400, 300), "android.widget.ImageView", clickable: true),
            Node(3, 0, (0, 500, 1080, 700), "android.widget.LinearLayout", clickable: true),
            Node(4, 3, (140, 520, 600, 620), "android.widget.TextView", text: "Profile"),
            Node(5, 3, (20, 520, 120, 620), "android.widget.ImageView"),
            Node(6, 0, (0, 800, 1080, 1200), "android.widget.ImageView"),
            Node(7, 0, (100, 850, 980, 1150), "android.widget.Button", text: "Start", clickable: true),
            Node(8, 0, (0, 1300, 1080, 1303), "android.widget.ImageView"),
            Node(9, 0, (0, 1400, 1080, 1600), "android.widget.LinearLayout"),
            Node(10, 9, (40, 1420, 200, 1580), "android.widget.ImageView"),
            Node(11, 9, (240, 1460, 900, 1540), "android.widget.TextView", text: "Battery 80%"),
        ]);
    }

    // The run of two settled snapshots of the recording's one visit, from
    // the phone's screenshot, with lines OCR read outside every image.
    private PhoneImageAlternativeRun ImageAlternativeRun(PhoneDeviceTimeline? device = null, string? engine = "Windows OCR, en-US", params string[] outside)
    {
        var window = ImageAlternatives();
        var lines = outside
            .Select((text, index) => new PhoneTextLine(null, window.Items[0], PhoneTextPlace.FoundByOcr, null, text,
                new PhoneBounds(100, 1700 + (index * 80), 700, 1740 + (index * 80)), null, 40))
            .ToArray();
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var measured = PhoneContrastRun.Of([.. visit.SettledSnapshots.Take(2).Select(at =>
            new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Screenshot, at.MonotonicNanoseconds, "p.png", "p.png"),
                engine, lines, null, window))]);
        return PhoneImageAlternativeRun.Run(measured, archive.PhoneScreens, device ?? Device());
    }

    [Fact]
    public void ImageAlternative_WarnsOfAnImageAScreenReaderSaysNothingFor_AndLeavesOutTheRulesExceptions()
    {
        var run = ImageAlternativeRun();
        var window = ImageAlternatives();
        var finding = Assert.Single(run.Findings);

        // Six images on each of two snapshots, each left out once in the visit.
        Assert.Equal((2, 12), (run.SnapshotsRead, run.Images));
        Assert.Equal(12, run.ImagesIn(finding.Visit));
        Assert.True(run.Read(finding.Visit));
        Assert.Equal((1, 1, 1, 1, 1, 0), (run.Named, run.Controls, run.InNamedControls, run.Covered, run.Thin, run.OutOfStep));
        Assert.Equal(window.Items[10].Identity, finding.Identity);
        Assert.Equal((PhoneCheckNames.ImageNoTextAlternative, PhoneCheckLevel.Warning), (finding.Check, finding.Level));
        Assert.Equal(2, finding.Times.Count);
        Assert.StartsWith("an image of 160 by 160 pixels (", finding.Times[0].Measured, StringComparison.Ordinal);
        Assert.EndsWith(
            " dp) with no description, text or label, that no control with a name holds, so a screen reader says nothing for it; " +
            "whether it gives information is not judged; from the phone's screenshot",
            finding.Times[0].Measured, StringComparison.Ordinal);
        Assert.Equal("Image with no text alternative (WCAG 1.1.1)", PhoneCheckNames.Title(PhoneCheckNames.ImageNoTextAlternative));

        // One question, with the text beside the image.
        var question = Assert.Single(run.Questions);
        Assert.Equal((new PhoneBounds(40, 1420, 200, 1580), "Battery 80%"), (question.Bounds, question.Nearby));
        Assert.Equal("Logo | Profile | Start | [the outlined image] | Battery 80%", question.ScreenText);
        Assert.Same(question, run.QuestionOf(finding));

        // Rule 2 needs the density: without it the hairline is asked about.
        var unknown = ImageAlternativeRun(PhoneDeviceTimeline.Empty);

        Assert.Equal(0, unknown.Thin);
        Assert.Equal([window.Items[8].Identity, window.Items[10].Identity], unknown.Findings.Select(item => item.Identity));
        Assert.StartsWith("an image of 1,080 by 3 pixels with no description", unknown.Findings[0].Times[0].Measured, StringComparison.Ordinal);

        // Rule 1 measures the share under the button: 880 by 300 of 1,080 by 400.
        Assert.Equal(880.0 * 300 / (1080 * 400), PhoneImageAlternativeRun.CoveredShare(window.Items[6], window), 2);
        Assert.Equal(0, PhoneImageAlternativeRun.CoveredShare(window.Items[10], window));

        // With no OCR, the pictures cannot be told in step.
        var unread = ImageAlternativeRun(engine: null);

        Assert.Equal((0, 0), (unread.SnapshotsRead, unread.Findings.Count));
    }

    // Rule 3: an image on a picture out of step with the tree is a check
    // not run, not a question.
    [Fact]
    public void ImageAlternative_DoesNotAskAboutAnImageOnAPictureOutOfStepWithTheTree()
    {
        var run = ImageAlternativeRun(outside: ["Inbox", "Promotions", "Meeting at noon"]);

        var finding = Assert.Single(run.Findings);
        Assert.Equal(PhoneCheckLevel.NotRun, finding.Level);
        Assert.Equal(2, run.OutOfStep);
        Assert.Contains(
            "not judged, as the picture is out of step with the tree: OCR read 3 lines outside every image that no item's text holds, more than the 0 text items it found where the tree places them; ",
            finding.Times[0].Measured, StringComparison.Ordinal);
        Assert.Empty(run.Questions);
        Assert.Null(run.QuestionOf(finding));

        // Two lines are too few to say so.
        Assert.Equal(PhoneCheckLevel.Warning, Assert.Single(ImageAlternativeRun(outside: ["Inbox", "Promotions"]).Findings).Level);
    }

    // What a screen reader says on a long screen is kept round the image's
    // place, and marks what is left out.
    [Fact]
    public void ImageAlternative_ScreenText_KeepsTheItemsNearestTheImage_OnALongScreen()
    {
        var nodes = new List<AndroidTreeNode> { ImageNode(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout", null, null) };
        for (var index = 1; index <= 200; index++)
        {
            nodes.Add(ImageNode(index, 0, (0, index * 10, 1080, (index * 10) + 9), "android.widget.TextView", null, $"Row {index:000} of the list"));
            if (index == 150)
            {
                nodes.Add(ImageNode(1000, 0, (0, 1505, 100, 1509), "android.widget.ImageView", null, null));
            }
        }

        var window = new PhoneCheckWindow(1, 1, nodes);
        var image = window.Items.Single(item => item.Node.Index == 1000);

        var text = PhoneImageAlternativeRun.ScreenText(window, image);

        Assert.True(text.Length <= PhoneImageAlternativeRun.MostScreenCharacters);
        Assert.StartsWith("... | Row ", text, StringComparison.Ordinal);
        Assert.EndsWith(" | Row 200 of the list", text, StringComparison.Ordinal);
        Assert.Contains("Row 150 of the list | [the outlined image] | Row 151 of the list", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Row 001 ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageAlternative_Judged_GivesTheAnswersLevel_AndDropsTheNoteOfWhatIsNotJudged()
    {
        var run = ImageAlternativeRun();
        var inference = new PhoneInference(PhoneCheckLevel.Info, "a model inferred it is decorative", Inferred: true, "Model", Cloud: false);

        var judged = run.Judged(new Dictionary<PhoneImageAlternativeQuestion, PhoneInference> { [run.Questions[0]] = inference }, "1 answered");

        var finding = Assert.Single(judged.Findings);
        Assert.Equal(PhoneCheckLevel.Info, finding.Level);
        Assert.Same(inference, finding.Inference);
        Assert.DoesNotContain("is not judged", finding.Times[0].Measured, StringComparison.Ordinal);
        Assert.Null(judged.QuestionOf(finding));
        Assert.Equal(("1 answered", 1, 1, 12), (judged.ModelNote, judged.Covered, judged.Thin, judged.ImagesIn(finding.Visit)));
    }

    // Rule 4: the request has the screen and a close-up enlarged to 192
    // pixels on the image's shorter side; the answer is kept against the
    // image's pixels, the text beside it and what a screen reader says on
    // the screen, which match within a
    // tolerance, so an answer given for the same image elsewhere is used.
    [Fact]
    public void ImageAlternative_RequestIsSignedByTheImagesPixelsAndNearbyText_AndKeptAnswersJudgeIt()
    {
        var run = ImageAlternativeRun();
        var question = Assert.Single(run.Questions);
        var rgb = new uint[1080 * 2400];
        Array.Fill(rgb, 0x203040u);
        var pixels = new PhonePixels(1080, 2400, rgb);

        var request = Recorder.Analysis.Android.AndroidImageAlternative.RequestOf(question, pixels);
        var again = Recorder.Analysis.Android.AndroidImageAlternative.RequestOf(question, new PhonePixels(1080, 2400, [.. rgb]));

        Assert.Equal(request.Key, again.Key);
        Assert.Matches("^image-text-alternative-[0-9a-f]{16}$", request.Key);
        Assert.Equal(Recorder.Analysis.ModelUses.VisionChecks, request.Request.Use);
        Assert.True(request.Request.ReusedAcrossTasks);
        Assert.Same(request.Signature, request.Request.Signature);
        Assert.Equal(Recorder.Analysis.Android.AndroidImageAlternative.Answers, request.Request.Answers);
        Assert.Equal((540, 1200), PngSize(request.Request.Images[0].Content));
        Assert.Equal((297, 307), PngSize(request.Request.Images[1].Content));
        Assert.Contains("The text of the items beside it, as the app gives it: \"Battery 80%\".", request.Request.Input, StringComparison.Ordinal);
        Assert.Contains(
            "What a screen reader says on this screen, item by item in the app's order, with [the outlined image] at the image's place: " +
            "\"Logo | Profile | Start | [the outlined image] | Battery 80%\".", request.Request.Input, StringComparison.Ordinal);

        // The same image a little different, as a video frame is, is the
        // same; much different, other text beside it, another screen's
        // text, or another size is not.
        byte[] Signed(uint colour, string nearby, PhoneBounds? bounds = null, string? screen = null)
        {
            var changed = (uint[])rgb.Clone();
            for (var y = 1420; y < 1580; y++)
            {
                for (var x = 40; x < 200; x++)
                {
                    changed[(y * 1080) + x] = colour;
                }
            }

            return Recorder.Analysis.Android.AndroidImageAlternative.Signature(new PhonePixels(1080, 2400, changed), bounds ?? question.Bounds, nearby, screen ?? question.ScreenText);
        }

        var same = Signed(0x233343, "Battery 80%");
        Assert.True(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(request.Signature, same));
        Assert.False(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(request.Signature, Signed(0x405060, "Battery 80%")));
        Assert.False(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(request.Signature, Signed(0x203040, "Battery 20%")));
        Assert.False(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(
            request.Signature, Signed(0x203040, "Battery 80%", new PhoneBounds(40, 1420, 200, 1579))));
        Assert.False(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(
            request.Signature, Signed(0x203040, "Battery 80%", screen: "Logo | Settings | Start | [the outlined image] | Battery 80%")));

        static JsonElement Output(string answer) =>
            JsonSerializer.SerializeToElement(new { answer, reason = "The pictures show it." });
        var elsewhere = Guid.NewGuid();
        var signed = new List<Recorder.Analysis.SignedAnswer>
        {
            new(Guid.NewGuid(), elsewhere, "image-text-alternative-0123456789abcdef-1", same,
                new(Output("informative"), "service", """{"temperature": null}""")),
        };

        // A model that is not repeatable needs both runs' answers.
        var one = Recorder.Analysis.Android.AndroidImageAlternative.FromKept(run, [request], signed, "Haiku");

        Assert.Equal(PhoneCheckLevel.Warning, Assert.Single(one.Findings).Level);
        Assert.Equal("1 image left by the rules is for Haiku; 0 images answered, 1 image not yet asked", one.ModelNote);

        signed.Add(new(Guid.NewGuid(), elsewhere, "image-text-alternative-0123456789abcdef-2", same,
            new(Output("informative"), "service", """{"temperature": null}""")));
        var both = Recorder.Analysis.Android.AndroidImageAlternative.FromKept(run, [request], signed, "Haiku");

        var finding = Assert.Single(both.Findings);
        Assert.Equal(PhoneCheckLevel.Error, finding.Level);
        Assert.True(finding.Inference!.Cloud);
        Assert.StartsWith(
            "Haiku, a cloud model, inferred from the pictures twice for the same image, by its pixels, the text beside it and what a screen reader says on the screen, that the image gives information",
            finding.Inference.Text, StringComparison.Ordinal);

        static (int Width, int Height) PngSize(byte[] png) =>
            ((png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19], (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);
    }

    // Step 21: icons and descriptions. A window of made-up items: an icon
    // that is a control with a name, an image with a description inside a
    // row a screen reader stops on, an image with no description, and an
    // image with a description that is not important for accessibility.
    private static PhoneCheckWindow DescribedImages()
    {
        AndroidTreeNode Node(int index, int? parent, (int Left, int Top, int Right, int Bottom) bounds, string className,
            string? description = null, string? text = null, bool clickable = false, bool important = true) =>
            ImageNode(index, parent, bounds, className, description, text) with { Clickable = clickable, ImportantForAccessibility = important };
        return new PhoneCheckWindow(512, 1,
        [
            Node(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout"),
            Node(1, 0, (20, 100, 140, 220), "android.widget.ImageView", description: "Navigate up", clickable: true),
            Node(2, 0, (0, 400, 1080, 600), "android.widget.LinearLayout", clickable: true),
            Node(3, 2, (20, 420, 180, 580), "android.widget.ImageView", description: "Sunny"),
            Node(4, 2, (220, 460, 900, 540), "android.widget.TextView", text: "18 degrees"),
            Node(5, 0, (300, 100, 400, 200), "android.widget.ImageView"),
            Node(6, 0, (500, 100, 600, 200), "android.widget.ImageView", description: "Hidden", important: false),
        ]);
    }

    private PhoneDescribedImageRun DescribedImageRun(string? engine = "Windows OCR, en-US", params string[] outside) =>
        DescribedImageRunOf(DescribedImages(), engine, outside);

    private PhoneDescribedImageRun DescribedImageRunOf(PhoneCheckWindow window, string? engine, string[] outside)
    {
        var lines = outside
            .Select((text, index) => new PhoneTextLine(null, window.Items[0], PhoneTextPlace.FoundByOcr, null, text,
                new PhoneBounds(100, 1700 + (index * 80), 700, 1740 + (index * 80)), null, 40))
            .ToArray();
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var measured = PhoneContrastRun.Of([.. visit.SettledSnapshots.Take(2).Select(at =>
            new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Screenshot, at.MonotonicNanoseconds, "p.png", "p.png"),
                engine, lines, null, window))]);
        return PhoneDescribedImageRun.Run(measured, archive.PhoneScreens);
    }

    // Rules 2 and 8: an image with a description of its own that a screen
    // reader is given is asked about, once in a visit; no finding is made
    // before a model's answer.
    [Fact]
    public void DescribedImages_AsksAboutEachDescribedImageOnceInAVisit_AndFindsNothingWithoutAModel()
    {
        var run = DescribedImageRun();
        var window = DescribedImages();

        Assert.Equal((2, 2, 1, 0), (run.SnapshotsRead, run.Images, run.Controls, run.OutOfStep));
        Assert.Empty(run.Findings);
        Assert.Equal(2, run.Unanswered);
        Assert.Collection(run.Questions,
            icon =>
            {
                Assert.Equal((window.Items[1].Identity, true, "Navigate up"), (icon.Identity, icon.IsControl, icon.Description));
                Assert.Equal((PhoneCheckNames.IconContradictsName, PhoneDescriptionSpeaker.Image), (icon.Check, icon.SpokenBy));
                Assert.Equal("an image that is a control, named \"Navigate up\"; from the phone's screenshot", icon.Time.Measured);
                Assert.Contains("[the outlined image]", icon.ScreenText, StringComparison.Ordinal);
            },
            image =>
            {
                Assert.Equal((window.Items[3].Identity, false, "Sunny"), (image.Identity, image.IsControl, image.Description));
                Assert.Equal((PhoneCheckNames.DescriptionNotMatchingImage, PhoneDescriptionSpeaker.Holder), (image.Check, image.SpokenBy));
                Assert.Contains("18 degrees", image.Spoken, StringComparison.Ordinal);
                Assert.Equal("an image that is not a control, with the description \"Sunny\"; from the phone's screenshot", image.Time.Measured);
            });
        Assert.Equal("Icon that contradicts its control's name (WCAG 1.1.1)", PhoneCheckNames.Title(PhoneCheckNames.IconContradictsName));
        Assert.Equal("Description that does not match its image (WCAG 1.1.1)", PhoneCheckNames.Title(PhoneCheckNames.DescriptionNotMatchingImage));

        // With no OCR, the pictures cannot be told in step.
        var unread = DescribedImageRun(engine: null);

        Assert.Equal((0, 0, 0), (unread.SnapshotsRead, unread.Images, unread.Questions.Count));
    }

    // Rule 3: an image on a picture out of step with the tree is counted,
    // not asked about.
    [Fact]
    public void DescribedImages_DoesNotAskAboutAnImageOnAPictureOutOfStepWithTheTree()
    {
        var run = DescribedImageRun(outside: ["Inbox", "Promotions", "Meeting at noon"]);

        Assert.Equal((2, 4), (run.Images, run.OutOfStep));
        Assert.Empty(run.Questions);
    }

    // Rule 7: only a contradiction, a mismatch, cannot tell or two answers
    // make a finding; the rest are counted.
    [Fact]
    public void DescribedImages_Judged_MakesFindingsOnlyOfWarnings_AndCountsTheRest()
    {
        var run = DescribedImageRun();
        var info = new PhoneInference(PhoneCheckLevel.Info, "fits", Inferred: true, "Model", Cloud: false);
        var warning = new PhoneInference(PhoneCheckLevel.Warning, "does not match", Inferred: true, "Model", Cloud: true);

        var fits = run.Judged(new Dictionary<PhoneDescribedImageQuestion, PhoneDescribedImageAnswer>
        {
            [run.Questions[0]] = new(PhoneDescribedImageOutcome.NotAnIcon, info),
            [run.Questions[1]] = new(PhoneDescribedImageOutcome.Fits, info),
        }, "2 answered");

        Assert.Empty(fits.Findings);
        Assert.Equal((1, 1, 0, "2 answered"), (fits.Fit, fits.NotIcons, fits.Unanswered, fits.ModelNote));
        Assert.Equal(2, fits.Inferences.Count());

        var mismatch = run.Judged(new Dictionary<PhoneDescribedImageQuestion, PhoneDescribedImageAnswer>
        {
            [run.Questions[1]] = new(PhoneDescribedImageOutcome.Finding, warning),
        }, null);

        var finding = Assert.Single(mismatch.Findings);
        Assert.Equal((PhoneCheckNames.DescriptionNotMatchingImage, PhoneCheckLevel.Warning), (finding.Check, finding.Level));
        Assert.Same(warning, finding.Inference);
        Assert.Equal(2, finding.Times.Count);
        Assert.Equal(1, mismatch.Unanswered);
        Assert.Same(finding, Assert.Single(mismatch.For(finding.Visit)));
    }

    // Rules 4 to 6: a control's image has the kind question and the icon
    // question; an image that is not a control the description question,
    // with what a screen reader says for the row that holds it; kept
    // answers judge them, both runs needed for a model that is not
    // repeatable.
    [Fact]
    public void DescribedImages_RequestsAndKeptAnswersJudgeEachImage()
    {
        var run = DescribedImageRun();
        var rgb = new uint[1080 * 2400];
        Array.Fill(rgb, 0x203040u);
        var pixels = new PhonePixels(1080, 2400, rgb);
        var icon = Recorder.Analysis.Android.AndroidDescribedImages.RequestsOf(run.Questions[0], pixels);
        var image = Recorder.Analysis.Android.AndroidDescribedImages.RequestsOf(run.Questions[1], pixels);

        Assert.Matches("^image-kind-[0-9a-f]{16}$", icon.Kind!.Key);
        Assert.Matches("^icon-name-[0-9a-f]{16}$", icon.Main.Key);
        Assert.Null(image.Kind);
        Assert.Matches("^image-description-[0-9a-f]{16}$", image.Main.Key);
        Assert.Equal(Recorder.Analysis.Android.AndroidDescribedImages.Kinds, icon.Kind.Request.Answers);
        Assert.Equal(Recorder.Analysis.Android.AndroidDescribedImages.IconAnswers, icon.Main.Request.Answers);
        Assert.Equal(Recorder.Analysis.Android.AndroidDescribedImages.DescriptionAnswers, image.Main.Request.Answers);
        Assert.Equal(Recorder.Analysis.ModelUses.VisionChecks, icon.Main.Request.Use);
        Assert.True(icon.Main.Request.ReusedAcrossTasks);
        Assert.Equal(2, icon.Main.Request.Images.Count);
        Assert.Contains("Its name, which a screen reader says for it, is \"Navigate up\".", icon.Main.Request.Input, StringComparison.Ordinal);
        Assert.Contains("the control's name, \"Navigate up\"", icon.Main.Request.Input, StringComparison.Ordinal);
        Assert.Contains("The outlined image's text alternative, as the app gives it, is \"Sunny\".", image.Main.Request.Input, StringComparison.Ordinal);
        Assert.Contains("18 degrees", image.Main.Request.Input, StringComparison.Ordinal);
        Assert.EndsWith("then give the answer.", image.Main.Request.Input, StringComparison.Ordinal);

        // The kind and icon questions are of the same image, so are kept
        // against the same signature; the image that is not a control is not.
        Assert.True(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(icon.Kind.Signature, icon.Main.Signature));
        Assert.False(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(icon.Main.Signature, image.Main.Signature));

        static JsonElement Output(string answer) =>
            JsonSerializer.SerializeToElement(new { answer, reason = "The pictures show it." });
        var elsewhere = Guid.NewGuid();
        Recorder.Analysis.SignedAnswer Answer(string key, int step, byte[] signature, string answer) =>
            new(Guid.NewGuid(), elsewhere, $"{key}-0123456789abcdef-{step}", signature,
                new(Output(answer), "service", """{"temperature": null}"""));
        IReadOnlyDictionary<string, IReadOnlyList<Recorder.Analysis.SignedAnswer>> Signed(
            string kind, string iconAnswer, string description, bool both = true) =>
            new Dictionary<string, IReadOnlyList<Recorder.Analysis.SignedAnswer>>
            {
                [Recorder.Analysis.Android.AndroidDescribedImages.KindTemplate] = both
                    ? [Answer("image-kind", 1, icon.Kind.Signature, kind), Answer("image-kind", 2, icon.Kind.Signature, kind)]
                    : [Answer("image-kind", 1, icon.Kind.Signature, kind)],
                [Recorder.Analysis.Android.AndroidDescribedImages.IconTemplate] =
                    [Answer("icon-name", 1, icon.Main.Signature, iconAnswer), Answer("icon-name", 2, icon.Main.Signature, iconAnswer)],
                [Recorder.Analysis.Android.AndroidDescribedImages.DescriptionTemplate] =
                    [Answer("image-description", 1, image.Main.Signature, description), Answer("image-description", 2, image.Main.Signature, description)],
            };

        // Both agree the control's image is a logo, so its name is not
        // judged against it; the description matches.
        var logo = Recorder.Analysis.Android.AndroidDescribedImages.FromKept(run, [icon, image], Signed("logo", "symbol-for-something-else", "matches"), "Haiku");

        Assert.Empty(logo.Findings);
        Assert.Equal((1, 1, 0), (logo.Fit, logo.NotIcons, logo.Unanswered));
        Assert.True(logo.Inferences.All(inference => inference.Cloud && inference.Inferred));
        Assert.Equal(
            "2 described images left by the rules are for Haiku; 2 described images answered: 1 judged to fit, not confirmed, " +
            "1 judged not to be icons, so not judged against their names, and 0 warnings", logo.ModelNote);

        // An icon judged a symbol for something else, and a description
        // that does not match, are warnings for a person to verify.
        var contradicts = Recorder.Analysis.Android.AndroidDescribedImages.FromKept(run, [icon, image], Signed("icon", "symbol-for-something-else", "does-not-match"), "Haiku");

        Assert.Equal(
            [(PhoneCheckNames.IconContradictsName, PhoneCheckLevel.Warning), (PhoneCheckNames.DescriptionNotMatchingImage, PhoneCheckLevel.Warning)],
            contradicts.Findings.Select(finding => (finding.Check, finding.Level)));
        Assert.All(contradicts.Findings, finding => Assert.EndsWith("; a person should verify this", finding.Inference!.Text, StringComparison.Ordinal));
        Assert.StartsWith(
            "Haiku, a cloud model, inferred from the pictures twice for the same image, by its pixels, its description and what a screen reader says on the screen, that the icon is the usual symbol",
            contradicts.Findings[0].Inference!.Text, StringComparison.Ordinal);

        // A kind with one run of two is not yet an answer for a model that
        // is not repeatable, so the control is not judged.
        var half = Recorder.Analysis.Android.AndroidDescribedImages.FromKept(run, [icon, image], Signed("icon", "fits-the-name", "matches", both: false), "Haiku");

        Assert.Empty(half.Findings);
        Assert.Equal((1, 1), (half.Fit, half.Unanswered));
        Assert.EndsWith(", 1 described image not yet asked", half.ModelNote, StringComparison.Ordinal);
    }

    // Rule 7's levels, for the icon and the description questions.
    [Theory]
    [InlineData(0, "fits-the-name", PhoneCheckLevel.Info, "that the icon fits the control's name, \"Navigate up\", not confirmed")]
    [InlineData(0, "symbol-for-something-else", PhoneCheckLevel.Warning, "; a person should verify this")]
    [InlineData(0, "cannot-tell", PhoneCheckLevel.Warning, "could not tell from the pictures twice whether the icon fits the control's name, \"Navigate up\"")]
    [InlineData(1, "matches", PhoneCheckLevel.Info, "that the description, \"Sunny\", matches what the image shows, not confirmed")]
    [InlineData(1, "does-not-match", PhoneCheckLevel.Warning, "that the description, \"Sunny\", does not match what the image shows")]
    [InlineData(1, "cannot-tell", PhoneCheckLevel.Warning, "; check manually")]
    public void DescribedImages_Interpret_GivesEachAgreedAnswersLevel(int question, string answer, PhoneCheckLevel level, string says)
    {
        var asked = DescribedImageRun().Questions[question];

        var inference = Recorder.Analysis.Android.AndroidDescribedImages.Interpret(new(answer, "First."), new(answer, "Second."), "Haiku", cloud: false, asked);

        Assert.Equal((level, true, false), (inference.Level, inference.Inferred, inference.Cloud));
        Assert.StartsWith("Haiku, a local model,", inference.Text, StringComparison.Ordinal);
        Assert.Contains(says, inference.Text, StringComparison.Ordinal);

        var split = Recorder.Analysis.Android.AndroidDescribedImages.Interpret(new(answer, "First."), new("cannot-tell" == answer ? "matches" : "cannot-tell", "Second."), "Haiku", cloud: false, asked);

        Assert.Equal(PhoneCheckLevel.Warning, split.Level);
        Assert.Contains("gave two answers", split.Text, StringComparison.Ordinal);
        Assert.EndsWith("; check manually", split.Text, StringComparison.Ordinal);
    }

    // Step 22: screen titles. A window of made-up items: a heading, a
    // line of text, a button and an image from the top of the screen down
    // to the given place; its title is a pane title, or the window's title.
    private static PhoneCheckWindow TitledScreen(string? paneTitle, string? windowTitle = null, int imageBottom = 600)
    {
        AndroidTreeNode Node(int index, int? parent, (int Left, int Top, int Right, int Bottom) bounds, string className,
            string? description = null, string? text = null, bool clickable = false, bool heading = false, string? pane = null) =>
            ImageNode(index, parent, bounds, className, description, text) with { Clickable = clickable, Heading = heading, PaneTitle = pane };
        return new PhoneCheckWindow(512, 1,
        [
            Node(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout", pane: paneTitle),
            Node(1, 0, (40, 100, 1040, 200), "android.widget.TextView", text: "Recent notes", heading: true),
            Node(2, 0, (0, 300, 1080, imageBottom), "android.widget.ImageView"),
            Node(3, 0, (40, 2000, 1040, 2100), "android.widget.TextView", text: "Shopping list"),
            Node(4, 0, (40, 2200, 500, 2300), "android.widget.Button", text: "Add a note", clickable: true),
        ], windowTitle);
    }

    private (PhoneScreenTitleRun Run, PhoneVisit Visit) ScreenTitleRun(PhoneCheckWindow window, string? engine = "Windows OCR, en-US", params string[] ocr)
    {
        var lines = ocr
            .Select((text, index) => new PhoneTextLine(null, window.Items[0], PhoneTextPlace.FoundByOcr, null, text,
                new PhoneBounds(100, 700 + (index * 80), 700, 740 + (index * 80)), null, 40))
            .ToArray();
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var measured = PhoneContrastRun.Of([.. visit.SettledSnapshots.Take(2).Select(at =>
            new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Screenshot, at.MonotonicNanoseconds, "p.png", "p.png"),
                engine, lines, null, window))]);
        return (PhoneScreenTitleRun.Run(measured, archive.PhoneScreens), visit);
    }

    // Rules 2 and 3: a title that is exactly a heading, a line OCR read or
    // an item a screen reader says, letters and digits compared, describes
    // its screen with no model; a title only within such text is asked
    // about, once for the screen, from its first snapshot.
    [Theory]
    [InlineData("Recent notes", PhoneTitleMatch.Heading)]
    [InlineData("recent-notes!", PhoneTitleMatch.Heading)]
    [InlineData("Notes for today", PhoneTitleMatch.OcrLine)]
    [InlineData("Add a note", PhoneTitleMatch.SpokenItem)]
    [InlineData("Notes", PhoneTitleMatch.None)]
    public void ScreenTitles_ATitleThatIsTheScreensOwnTextIsNotAsked(string title, PhoneTitleMatch match)
    {
        var (run, visit) = ScreenTitleRun(TitledScreen(title), ocr: ["Notes for today"]);

        Assert.Equal((2, 1, 0), (run.SnapshotsRead, run.Screens, run.Untitled));
        var screen = run.Of(visit.Screen);
        Assert.NotNull(screen);
        Assert.Equal((match, title.Trim(), visit), (screen.Value.Match, screen.Value.Title, screen.Value.Visit));
        Assert.Equal(match == PhoneTitleMatch.None ? PhoneScreenTitleState.Asked : PhoneScreenTitleState.Matched, screen.Value.State);
        Assert.Equal(match == PhoneTitleMatch.None ? 1 : 0, run.Questions.Count);
        Assert.Equal(match == PhoneTitleMatch.None ? 0 : 1, run.Matched);
        Assert.Empty(run.Findings);
    }

    // Rules 2 and 4: an untitled screen is left to "No screen title"; an
    // asked screen is given what a screen reader says and OCR's text, and
    // its picture only where images cover at least half of it; with no
    // OCR, nothing is checked.
    [Fact]
    public void ScreenTitles_AsksWithTheScreensTextAndItsPictureOnlyWhereImagesCoverHalfOfIt()
    {
        var (untitled, _) = ScreenTitleRun(TitledScreen(null));

        Assert.Equal((1, 1, 0), (untitled.Screens, untitled.Untitled, untitled.Questions.Count));

        var (text, visit) = ScreenTitleRun(TitledScreen(null, "Acme Notes"), ocr: ["Shopping list", "Recent notes"]);
        var question = Assert.Single(text.Questions);

        Assert.Equal(("Acme Notes", "window title", null), (question.Title, question.TitleSource, question.PageTitle));
        Assert.Equal("Recent notes | Shopping list | Add a note", question.Spoken);
        Assert.Equal("Shopping list / Recent notes", question.Ocr);
        Assert.Equal(visit, question.Visit);
        Assert.Equal("its window title, \"Acme Notes\"", question.Time.Measured);
        Assert.InRange(question.ImageShare, 0.12, 0.14);
        Assert.False(question.Pictured);
        Assert.Equal((1, 0), (text.TextQuestions, text.PicturedQuestions));

        var (pictured, _) = ScreenTitleRun(TitledScreen(null, "Acme Notes", imageBottom: 1500));

        Assert.True(Assert.Single(pictured.Questions).Pictured);
        Assert.Equal((0, 1), (pictured.TextQuestions, pictured.PicturedQuestions));

        var (unread, _) = ScreenTitleRun(TitledScreen("Notes"), engine: null);

        Assert.Equal((0, 0, 0), (unread.SnapshotsRead, unread.Screens, unread.Questions.Count));
        Assert.Equal("Screen title does not describe its screen (WCAG2ICT 2.4.2)", PhoneCheckNames.Title(PhoneCheckNames.TitleNotDescriptive));
    }

    // Rule 5, option A: in Chrome, the web page's title is judged.
    [Theory]
    [InlineData("com.android.chrome", "Chrome: Acme Notes help", "Acme Notes help")]
    [InlineData("com.chrome.beta", "Chrome: Sign in", "Sign in")]
    [InlineData("com.android.chrome", "Chrome", null)]
    [InlineData("com.android.chrome", "Chrome: ", null)]
    [InlineData("com.example.notes", "Chrome: Acme Notes help", null)]
    public void ScreenTitles_PageTitle_IsChromesWebPageTitle(string package, string title, string? page) =>
        Assert.Equal(page, PhoneScreenTitleRun.PageTitle(package, title));

    // Rule 4's share of a screen images cover, on a grid.
    [Fact]
    public void ScreenTitles_ImageShare_CountsOverlappingImagesOnce()
    {
        var frame = new PhoneBounds(0, 0, 1000, 2000);

        Assert.Equal(0, PhoneScreenTitleRun.ImageShare(frame, []));
        Assert.Equal(0.5, PhoneScreenTitleRun.ImageShare(frame, [new PhoneBounds(0, 0, 1000, 1000), new PhoneBounds(0, 500, 1000, 1000)]), 2);
        Assert.Equal(1, PhoneScreenTitleRun.ImageShare(frame, [new PhoneBounds(-10, -10, 1010, 2010)]), 2);
        Assert.Equal(PhoneTitleMatch.None, PhoneScreenTitleRun.Match("...", ["..."], [], []));
    }

    // Rule 6: only an answer that the title does not describe its screen,
    // cannot tell or two answers makes a finding; the rest are counted.
    [Fact]
    public void ScreenTitles_Judged_MakesFindingsOnlyOfWarnings_AndCountsTheRest()
    {
        var (run, _) = ScreenTitleRun(TitledScreen(null, "Acme Notes"));
        var question = Assert.Single(run.Questions);
        var info = new PhoneInference(PhoneCheckLevel.Info, "describes", Inferred: true, "Model", Cloud: false);
        var warning = new PhoneInference(PhoneCheckLevel.Warning, "does not describe", Inferred: true, "Model", Cloud: true);

        var describes = run.Judged(new Dictionary<PhoneScreenTitleQuestion, PhoneScreenTitleAnswer>
        {
            [question] = new(PhoneScreenTitleOutcome.Describes, info),
        }, "1 answered");

        Assert.Empty(describes.Findings);
        Assert.Equal((1, 0, "1 answered"), (describes.Described, describes.Unanswered, describes.ModelNote));
        Assert.Single(describes.Inferences);

        var judged = run.Judged(new Dictionary<PhoneScreenTitleQuestion, PhoneScreenTitleAnswer>
        {
            [question] = new(PhoneScreenTitleOutcome.Finding, warning),
        }, null);

        var finding = Assert.Single(judged.Findings);
        Assert.Equal((PhoneCheckNames.TitleNotDescriptive, PhoneCheckLevel.Warning, "Acme Notes"), (finding.Check, finding.Level, finding.Label));
        Assert.Same(warning, finding.Inference);
        Assert.Same(finding, Assert.Single(judged.For(question.Visit)));
        Assert.Equal(1, judged.Matched + judged.Questions.Count);
    }

    // Rules 4, 5 and 8: a text question is for the model for text-based
    // checks with no picture, a pictured one for vision-based checks with
    // the whole screen; kept answers judge them, by their signatures.
    [Fact]
    public void ScreenTitles_RequestsAndKeptAnswersJudgeEachScreen()
    {
        var (run, _) = ScreenTitleRun(TitledScreen(null, "Acme Notes"), ocr: ["Recent notes"]);
        var (pictured, _) = ScreenTitleRun(TitledScreen(null, "Acme Notes", imageBottom: 1500));
        var rgb = new uint[1080 * 2400];
        Array.Fill(rgb, 0x203040u);
        var pixels = new PhonePixels(1080, 2400, rgb);
        var text = Recorder.Analysis.Android.AndroidScreenTitles.RequestOf(run.Questions[0], null);
        var picture = Recorder.Analysis.Android.AndroidScreenTitles.RequestOf(pictured.Questions[0], pixels);

        Assert.Matches("^screen-title-[0-9a-f]{16}$", text.Request.Key);
        Assert.Equal((Recorder.Analysis.ModelUses.TextChecks, Recorder.Analysis.Android.AndroidScreenTitles.TextTemplate, 0),
            (text.Request.Request.Use, text.Request.Request.PromptTemplate, text.Request.Request.Images.Count));
        Assert.Equal((Recorder.Analysis.ModelUses.VisionChecks, Recorder.Analysis.Android.AndroidScreenTitles.PicturedTemplate, 1),
            (picture.Request.Request.Use, picture.Request.Request.PromptTemplate, picture.Request.Request.Images.Count));
        Assert.Equal((text.Use, picture.Use), (Recorder.Analysis.ModelUses.TextChecks, Recorder.Analysis.ModelUses.VisionChecks));
        Assert.Equal(Recorder.Analysis.Android.AndroidScreenTitles.Answers, text.Request.Request.Answers);
        Assert.True(text.Request.Request.ReusedAcrossTasks);
        Assert.Contains("has the title \"Acme Notes\", which a screen reader is given as the screen's window title.", text.Request.Request.Input, StringComparison.Ordinal);
        Assert.Contains("item by item in the app's order: \"Recent notes | Shopping list | Add a note\"", text.Request.Request.Input, StringComparison.Ordinal);
        Assert.Contains("which may misread some words: \"Recent notes\"", text.Request.Request.Input, StringComparison.Ordinal);
        Assert.DoesNotContain("Picture A", text.Request.Request.Input, StringComparison.Ordinal);
        Assert.Contains("Picture A is the whole screen.", picture.Request.Request.Input, StringComparison.Ordinal);
        Assert.EndsWith("then give the answer.", text.Request.Request.Input, StringComparison.Ordinal);

        // The same texts are the same screen with no picture; different
        // OCR text is not.
        var again = Recorder.Analysis.Android.AndroidScreenTitles.RequestOf(run.Questions[0], null);
        var (otherOcr, _) = ScreenTitleRun(TitledScreen(null, "Acme Notes"), ocr: ["Archive"]);
        var other = Recorder.Analysis.Android.AndroidScreenTitles.RequestOf(otherOcr.Questions[0], null);

        Assert.True(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(text.Request.Signature, again.Request.Signature));
        Assert.False(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(text.Request.Signature, other.Request.Signature));
        Assert.False(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(text.Request.Signature, picture.Request.Signature));

        static JsonElement Output(string answer) =>
            JsonSerializer.SerializeToElement(new { answer, reason = "The title is only the app's name." });
        var elsewhere = Guid.NewGuid();
        Recorder.Analysis.SignedAnswer Answer(int step, byte[] signature, string answer) =>
            new(Guid.NewGuid(), elsewhere, $"screen-title-0123456789abcdef-{step}", signature,
                new(Output(answer), "service", """{"temperature": null}"""));
        Recorder.Analysis.Android.ScreenTitleKept Kept(string answer) =>
            new([Answer(1, text.Request.Signature, answer), Answer(2, text.Request.Signature, answer)], "Haiku");

        var findings = Recorder.Analysis.Android.AndroidScreenTitles.FromKept(run, [text], Kept("does-not-describe"), null);

        var finding = Assert.Single(findings.Findings);
        Assert.Equal((PhoneCheckNames.TitleNotDescriptive, PhoneCheckLevel.Warning), (finding.Check, finding.Level));
        Assert.StartsWith(
            "Haiku, a cloud model, inferred from what a screen reader says on the screen and the text OCR read twice for the same screen, by its title, what a screen reader says and the text OCR read, that the screen's title, \"Acme Notes\", does not describe its screen",
            finding.Inference!.Text, StringComparison.Ordinal);
        Assert.EndsWith("; a person should verify this", finding.Inference.Text, StringComparison.Ordinal);
        Assert.Equal(
            "1 screen title left by the rules is for Haiku; 1 screen title answered: 0 judged to describe their screens, not confirmed, and 1 warning",
            findings.ModelNote);

        var describes = Recorder.Analysis.Android.AndroidScreenTitles.FromKept(run, [text], Kept("describes-the-screen"), null);

        Assert.Empty(describes.Findings);
        Assert.Equal(1, describes.Described);

        // With no kept answers, or no model, nothing is judged (rule 7).
        var none = Recorder.Analysis.Android.AndroidScreenTitles.FromKept(run, [text], null, null);

        Assert.Equal((0, 1), (none.Answers.Count, none.Unanswered));
        Assert.EndsWith(", 1 screen title not yet asked", none.ModelNote, StringComparison.Ordinal);

        var noModel = Recorder.Analysis.Android.AndroidScreenTitles.FromKept(run, [text], null, null, textNone: "the project's settings choose no model for text-based checks");

        Assert.Empty(noModel.Findings);
        Assert.EndsWith(
            "; 1 screen title left by the rules was not judged, as no model may be used for text-based checks: the project's settings choose no model for text-based checks",
            noModel.ModelNote, StringComparison.Ordinal);
        Assert.DoesNotContain("not yet asked", noModel.ModelNote, StringComparison.Ordinal);
    }

    // Rule 6's levels, and the web page's title in Chrome's.
    [Theory]
    [InlineData("describes-the-screen", PhoneCheckLevel.Info, "that the screen's title, \"Acme Notes\", describes its screen, not confirmed")]
    [InlineData("does-not-describe", PhoneCheckLevel.Warning, "; a person should verify this")]
    [InlineData("cannot-tell", PhoneCheckLevel.Warning, "could not tell from what a screen reader says on the screen and the text OCR read twice whether the screen's title, \"Acme Notes\", describes its screen")]
    public void ScreenTitles_Interpret_GivesEachAgreedAnswersLevel(string answer, PhoneCheckLevel level, string says)
    {
        var (run, _) = ScreenTitleRun(TitledScreen(null, "Acme Notes"));
        var asked = run.Questions[0];

        var inference = Recorder.Analysis.Android.AndroidScreenTitles.Interpret(new(answer, "First."), new(answer, "Second."), "Haiku", cloud: false, asked);

        Assert.Equal((level, true, false), (inference.Level, inference.Inferred, inference.Cloud));
        Assert.StartsWith("Haiku, a local model,", inference.Text, StringComparison.Ordinal);
        Assert.Contains(says, inference.Text, StringComparison.Ordinal);

        var split = Recorder.Analysis.Android.AndroidScreenTitles.Interpret(new(answer, "First."), new(answer == "cannot-tell" ? "does-not-describe" : "cannot-tell", "Second."), "Haiku", cloud: false, asked);

        Assert.Equal(PhoneCheckLevel.Warning, split.Level);
        Assert.Contains("gave two answers", split.Text, StringComparison.Ordinal);

        var repeatable = new Recorder.Analysis.ClosedAnswerOf(answer, "First.");
        var chrome = Recorder.Analysis.Android.AndroidScreenTitles.Interpret(repeatable, repeatable, "Haiku", cloud: true,
            asked with { PageTitle = "Acme Notes help" });

        Assert.Contains("the web page's title, \"Acme Notes help\", in the screen's title,", chrome.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(" twice", chrome.Text, StringComparison.Ordinal);
    }

    // Step 23: decorative images. A window of made-up items: an icon that
    // is a control; an image in a row with other text; an image that is
    // all its row says; an image a screen reader stops on alone; and an
    // image in a row whose own description is all a screen reader says.
    private static PhoneCheckWindow DecorativeImages()
    {
        AndroidTreeNode Node(int index, int? parent, (int Left, int Top, int Right, int Bottom) bounds, string className,
            string? description = null, string? text = null, bool clickable = false) =>
            ImageNode(index, parent, bounds, className, description, text) with { Clickable = clickable };
        return new PhoneCheckWindow(512, 1,
        [
            Node(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout"),
            Node(1, 0, (20, 100, 140, 220), "android.widget.ImageView", description: "Navigate up", clickable: true),
            Node(2, 0, (0, 400, 1080, 600), "android.widget.LinearLayout", clickable: true),
            Node(3, 2, (20, 420, 180, 580), "android.widget.ImageView", description: "Sunny"),
            Node(4, 2, (220, 460, 900, 540), "android.widget.TextView", text: "18 degrees"),
            Node(5, 0, (0, 700, 1080, 900), "android.widget.LinearLayout", clickable: true),
            Node(6, 5, (20, 720, 180, 880), "android.widget.ImageView", description: "Search"),
            Node(7, 0, (300, 1000, 700, 1200), "android.widget.ImageView", description: "Flourish"),
            Node(8, 0, (0, 1300, 1080, 1500), "android.widget.LinearLayout", description: "Weather card", clickable: true),
            Node(9, 8, (20, 1320, 180, 1480), "android.widget.ImageView", description: "Rain"),
        ]);
    }

    private PhoneDecorativeImageRun DecorativeImageRun(params string[] outside) =>
        PhoneDecorativeImageRun.Run(DescribedImageRunOf(DecorativeImages(), "Windows OCR, en-US", outside));

    // Rules 2, 3 and 7: an image whose description a screen reader does not
    // say, a control's image, and an image that is all its row says are
    // not asked about; an image said with other text, or alone, is; no
    // finding is made before a model's answer.
    [Fact]
    public void DecorativeImages_AsksOnlyAboutImagesSaidWithOtherTextOrAlone_AndFindsNothingWithoutAModel()
    {
        var run = DecorativeImageRun();

        Assert.Equal((5, 1, 1, 1), (run.Images, run.NotSpoken, run.Controls, run.ItemNames));
        Assert.Equal(["Sunny", "Flourish"], run.Questions.Select(question => question.Description));
        Assert.Equal([PhoneDescriptionSpeaker.Holder, PhoneDescriptionSpeaker.Image], run.Questions.Select(question => question.SpokenBy));
        Assert.Empty(run.Findings);
        Assert.Equal(2, run.Unanswered);
        Assert.Equal("Decorative image announced by a screen reader (WCAG 1.1.1)", PhoneCheckNames.Title(PhoneCheckNames.DecorativeAnnounced));
        var visit = run.Questions[0].Visit;
        Assert.Equal((5, 2, true), (run.ImagesIn(visit), run.AskedIn(visit), run.Read(visit)));
        Assert.Equal(
            [PhoneDecorativeState.Control, PhoneDecorativeState.Asked, PhoneDecorativeState.ItemName, PhoneDecorativeState.Asked, PhoneDecorativeState.NotSpoken],
            run.In(visit).Select(image => image.State));

        // Rule 8: an image on a picture out of step with the tree is not asked about.
        var outOfStep = DecorativeImageRun("Inbox", "Promotions", "Meeting at noon");

        Assert.True(outOfStep.OutOfStep > 0);
        Assert.Empty(outOfStep.Questions);
    }

    // Rule 3's comparison, letters and digits only.
    [Theory]
    [InlineData(false, PhoneDescriptionSpeaker.Holder, "Cloudy", "Cloudy, 7°", PhoneDecorativeState.Asked)]
    [InlineData(false, PhoneDescriptionSpeaker.Holder, "Voice search", "Voice search.", PhoneDecorativeState.ItemName)]
    [InlineData(false, PhoneDescriptionSpeaker.Image, "Flourish", "Flourish", PhoneDecorativeState.Asked)]
    [InlineData(true, PhoneDescriptionSpeaker.Image, "Navigate up", "Navigate up", PhoneDecorativeState.Control)]
    [InlineData(false, PhoneDescriptionSpeaker.Holder, "Rain", "Weather card", PhoneDecorativeState.NotSpoken)]
    [InlineData(false, PhoneDescriptionSpeaker.Nothing, "Rain", "", PhoneDecorativeState.NotSpoken)]
    public void DecorativeImages_StateOf_ComparesLettersAndDigits(bool control, PhoneDescriptionSpeaker spokenBy, string description, string spoken, PhoneDecorativeState state) =>
        Assert.Equal(state, PhoneDecorativeImageRun.StateOf(control, spokenBy, description, spoken));

    // Rule 5: only decorative, cannot tell or two answers make a finding;
    // informative is counted.
    [Fact]
    public void DecorativeImages_Judged_MakesFindingsOnlyOfWarnings_AndCountsTheRest()
    {
        var run = DecorativeImageRun();
        var info = new PhoneInference(PhoneCheckLevel.Info, "informative", Inferred: true, "Model", Cloud: false);
        var warning = new PhoneInference(PhoneCheckLevel.Warning, "decorative", Inferred: true, "Model", Cloud: true);

        var judged = run.Judged(new Dictionary<PhoneDescribedImageQuestion, PhoneDecorativeAnswer>
        {
            [run.Questions[0]] = new(PhoneDecorativeOutcome.Informative, info),
            [run.Questions[1]] = new(PhoneDecorativeOutcome.Finding, warning),
        }, "2 answered");

        var finding = Assert.Single(judged.Findings);
        Assert.Equal((PhoneCheckNames.DecorativeAnnounced, PhoneCheckLevel.Warning, "ImageView described as \"Flourish\""), (finding.Check, finding.Level, finding.Label));
        Assert.Same(warning, finding.Inference);
        Assert.Equal(2, finding.Times.Count);
        Assert.Equal((1, 0, "2 answered"), (judged.Informative, judged.Unanswered, judged.ModelNote));
        Assert.Equal(2, judged.Inferences.Count());
        Assert.Same(finding, Assert.Single(judged.For(finding.Visit)));
        Assert.Equal((1, 1), (judged.Controls, judged.ItemNames));
    }

    // Rules 4 and 6: the question marks the image's own description as
    // the image's, and kept answers judge each image.
    [Fact]
    public void DecorativeImages_RequestsAndKeptAnswersJudgeEachImage()
    {
        var run = DecorativeImageRun();
        var rgb = new uint[1080 * 2400];
        Array.Fill(rgb, 0x203040u);
        var pixels = new PhonePixels(1080, 2400, rgb);
        var held = Recorder.Analysis.Android.AndroidDecorativeImages.RequestOf(run.Questions[0], pixels);
        var alone = Recorder.Analysis.Android.AndroidDecorativeImages.RequestOf(run.Questions[1], pixels);

        Assert.Matches("^image-decorative-[0-9a-f]{16}$", held.Key);
        Assert.Equal(Recorder.Analysis.Android.AndroidDecorativeImages.Answers, held.Request.Answers);
        Assert.Equal((Recorder.Analysis.ModelUses.VisionChecks, true, 2), (held.Request.Use, held.Request.ReusedAcrossTasks, held.Request.Images.Count));
        Assert.Contains("The outlined image's text alternative, as the app gives it, is \"Sunny\".", held.Request.Input, StringComparison.Ordinal);
        Assert.Contains("the words \"Sunny\" in it are the image's own text alternative, not other text.", held.Request.Input, StringComparison.Ordinal);
        Assert.Contains("18 degrees", held.Request.Input, StringComparison.Ordinal);
        Assert.Contains("A screen reader stops on the image itself", alone.Request.Input, StringComparison.Ordinal);
        Assert.EndsWith("then give the answer.", held.Request.Input, StringComparison.Ordinal);
        Assert.False(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(held.Signature, alone.Signature));

        static JsonElement Output(string answer) =>
            JsonSerializer.SerializeToElement(new { answer, reason = "The pictures show it." });
        var elsewhere = Guid.NewGuid();
        Recorder.Analysis.SignedAnswer Answer(string key, int step, byte[] signature, string answer) =>
            new(Guid.NewGuid(), elsewhere, $"{key}-{step}", signature,
                new(Output(answer), "service", """{"temperature": null}"""));
        IReadOnlyList<Recorder.Analysis.SignedAnswer> Signed(string first, string second) =>
        [
            Answer("image-decorative-0123456789abcdef", 1, held.Signature, first), Answer("image-decorative-0123456789abcdef", 2, held.Signature, first),
            Answer("image-decorative-fedcba9876543210", 1, alone.Signature, second), Answer("image-decorative-fedcba9876543210", 2, alone.Signature, second),
        ];
        var requests = Recorder.Analysis.Android.AndroidDecorativeImages.Requests(run, _ => pixels);

        Assert.Equal(2, requests.Count);

        var judged = Recorder.Analysis.Android.AndroidDecorativeImages.FromKept(run, requests, Signed("informative", "decorative"), "Haiku");

        var finding = Assert.Single(judged.Findings);
        Assert.Equal((PhoneCheckNames.DecorativeAnnounced, PhoneCheckLevel.Warning, "ImageView described as \"Flourish\""), (finding.Check, finding.Level, finding.Label));
        Assert.StartsWith(
            "Haiku, a cloud model, inferred from the pictures twice for the same image, by its pixels, its description and what a screen reader says on the screen, that the image a screen reader announces as \"Flourish\" is decorative",
            finding.Inference!.Text, StringComparison.Ordinal);
        Assert.EndsWith("; a person should verify this", finding.Inference.Text, StringComparison.Ordinal);
        Assert.Equal(1, judged.Informative);
        Assert.Equal("2 images left by the rules are for Haiku; 2 images answered: 1 judged informative, not confirmed, and 1 warning", judged.ModelNote);

        // With no kept answers nothing is judged (rule 7).
        var none = Recorder.Analysis.Android.AndroidDecorativeImages.FromKept(run, requests, [], "Haiku");

        Assert.Equal((0, 2), (none.Answers.Count, none.Unanswered));
        Assert.EndsWith(", 2 images not yet asked", none.ModelNote, StringComparison.Ordinal);
        Assert.Equal(
            "2 images left by the rules were not judged, as no model may be used for vision-based checks: no model",
            Recorder.Analysis.Android.AndroidDecorativeImages.NotAsked(2, "no model"));
    }

    // Rule 5's levels.
    [Theory]
    [InlineData("informative", PhoneCheckLevel.Info, "that the image a screen reader announces as \"Sunny\" is informative, not confirmed")]
    [InlineData("decorative", PhoneCheckLevel.Warning, "is decorative, so a screen reader should not announce it")]
    [InlineData("cannot-tell", PhoneCheckLevel.Warning, "could not tell from the pictures twice whether the image a screen reader announces as \"Sunny\" is informative or decorative")]
    public void DecorativeImages_Interpret_GivesEachAgreedAnswersLevel(string answer, PhoneCheckLevel level, string says)
    {
        var asked = DecorativeImageRun().Questions[0];

        var inference = Recorder.Analysis.Android.AndroidDecorativeImages.Interpret(new(answer, "First."), new(answer, "Second."), "Haiku", cloud: false, asked);

        Assert.Equal((level, true, false), (inference.Level, inference.Inferred, inference.Cloud));
        Assert.StartsWith("Haiku, a local model,", inference.Text, StringComparison.Ordinal);
        Assert.Contains(says, inference.Text, StringComparison.Ordinal);

        var split = Recorder.Analysis.Android.AndroidDecorativeImages.Interpret(new(answer, "First."), new(answer == "cannot-tell" ? "decorative" : "cannot-tell", "Second."), "Haiku", cloud: false, asked);

        Assert.Equal(PhoneCheckLevel.Warning, split.Level);
        Assert.Contains("gave two answers", split.Text, StringComparison.Ordinal);
        Assert.EndsWith("; check manually", split.Text, StringComparison.Ordinal);
    }

    // Step 24: a heading that does not describe its section. A window of
    // made-up items: a marked heading that is the window's title, a list
    // that scrolls down, holding two marked headings, "Recent notes" and
    // "Options", with what is under each, a line drawn above "Recent
    // notes" though after it in the tree, an image and an item not on
    // screen; and a tab bar below the list. With no list, every item is
    // in the root.
    private static PhoneCheckWindow SectionsScreen(bool scrolling = true, int imageBottom = 420)
    {
        AndroidTreeNode Node(int index, int? parent, (int Left, int Top, int Right, int Bottom) bounds, string className,
            string? text = null, bool clickable = false, bool heading = false, bool visible = true) =>
            ImageNode(index, parent, bounds, className, null, text) with { Clickable = clickable, Heading = heading, VisibleToUser = visible };
        var list = scrolling ? 2 : 0;
        var listNode = Node(2, 0, (0, 300, 1080, 2000), "androidx.recyclerview.widget.RecyclerView");
        if (scrolling)
        {
            listNode = listNode with { Scrollable = true, Actions = [new AndroidNodeAction(0x1000, null)] };
        }

        return new PhoneCheckWindow(512, 1,
        [
            Node(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout"),
            Node(1, 0, (40, 100, 1040, 200), "android.widget.TextView", "Acme Notes", heading: true),
            listNode,
            Node(3, list, (40, 320, 1040, 400), "android.widget.TextView", "Recent notes", heading: true),
            Node(4, list, (40, 300, 1040, 315), "android.widget.TextView", "Pinned"),
            Node(5, list, (0, 400, 1080, imageBottom), "android.widget.ImageView"),
            Node(6, list, (40, 420, 1040, 500), "android.widget.TextView", "Shopping list"),
            Node(7, list, (40, 520, 1040, 600), "android.widget.TextView", "Gift ideas"),
            Node(8, list, (40, 700, 1040, 780), "android.widget.TextView", "Options", heading: true),
            Node(9, list, (40, 800, 1040, 880), "android.widget.TextView", "Send feedback"),
            Node(10, list, (0, 0, 0, 0), "android.widget.TextView", "Archive", clickable: true, visible: false),
            Node(11, 0, (0, 2100, 540, 2400), "android.widget.Button", "Notes", clickable: true),
        ], "Acme Notes");
    }

    private static PhoneTextLine[] SectionLines(PhoneCheckWindow window) =>
    [
        .. new (string Text, int Top)[]
        {
            ("Acme Notes", 110), ("Recent notes", 330), ("Shopping list", 430), ("Gift ideas", 530), ("Options", 710), ("Send feedback", 810), ("Notes", 2200),
        }.Select(line => new PhoneTextLine(null, window.Items[0], PhoneTextPlace.FoundByOcr, null, line.Text, new PhoneBounds(60, line.Top, 900, line.Top + 50), null, 40)),
    ];

    private (PhoneContrastRun Measured, SessionPlaybackArchive Archive, PhoneVisit Visit) SectionsMeasured(
        PhoneCheckWindow window, PhoneTextLine[] lines, string? engine = "Windows OCR, en-US")
    {
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var measured = PhoneContrastRun.Of([.. visit.SettledSnapshots.Take(2).Select(at =>
            new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Screenshot, at.MonotonicNanoseconds, "p.png", "p.png"),
                engine, lines, null, window))]);
        return (measured, archive, visit);
    }

    private PhoneHeadingSectionRun SectionsRun(bool scrolling = true, int imageBottom = 420, string? engine = "Windows OCR, en-US")
    {
        var window = SectionsScreen(scrolling, imageBottom);
        var (measured, archive, _) = SectionsMeasured(window, SectionLines(window), engine);
        return PhoneHeadingSectionRun.Run(measured, archive.PhoneScreens, null);
    }

    // Rules 2 to 5 and 7: each heading once a screen; the window's title is
    // left to the screen title check; a section is what a screen reader
    // says after its heading in the heading's scrolling area, not above the
    // heading, the items not on screen, and the lines OCR read down to the
    // next heading or the area's bottom; the last section of an area that
    // can scroll down may continue.
    [Fact]
    public void HeadingSections_TakeEachSectionInItsHeadingsScrollingArea()
    {
        var run = SectionsRun();

        Assert.Equal((2, 3, 0), (run.SnapshotsRead, run.Marked, run.Unmarked));
        Assert.Equal((1, 0, 0), (run.ScreenTitles, run.EmptySections, run.NotJudgedHeadings));
        Assert.Equal(["Recent notes", "Options"], run.Questions.Select(question => question.Heading));
        var (recent, options) = (run.Questions[0], run.Questions[1]);

        Assert.Equal(("Shopping list | Gift ideas", "", "Shopping list / Gift ideas"), (recent.Spoken, recent.OffScreen, recent.Ocr));
        Assert.Equal((true, "Acme Notes", false, false), (recent.Marked, recent.ScreenTitle, recent.MayContinue, recent.PartlyFromTree));
        Assert.Equal(("Send feedback", "Archive", "Send feedback"), (options.Spoken, options.OffScreen, options.Ocr));
        Assert.True(options.MayContinue);
        Assert.True(options.PartlyFromTree);
        Assert.Equal(
            "a heading over a section of 1 item a screen reader says on screen and 1 line OCR read; part of the section was judged from the accessibility tree only, as 1 item in it is not on screen; the section may continue below the screen",
            options.Time.Measured);
        Assert.Equal((2, 0), (run.TextQuestions, run.PicturedQuestions));
        Assert.Empty(run.Findings);
        Assert.Equal("Heading does not describe its section (WCAG 2.4.6)", PhoneCheckNames.Title(PhoneCheckNames.HeadingNotDescriptive));

        // With no scrolling area, the tab bar is in the last section, and
        // it cannot continue.
        var flat = SectionsRun(scrolling: false);
        var last = flat.Questions[^1];

        Assert.Equal(("Send feedback | Notes", "Send feedback / Notes", false), (last.Spoken, last.Ocr, last.MayContinue));

        // With no OCR, nothing is checked.
        var unread = SectionsRun(engine: null);

        Assert.Equal((0, 0), (unread.SnapshotsRead, unread.Entries.Count));
    }

    // Rule 6: the screen's picture is given where images cover at least
    // half of the section.
    [Fact]
    public void HeadingSections_AreGivenThePictureWhereImagesCoverHalfOfTheSection()
    {
        var run = SectionsRun(imageBottom: 600);

        Assert.True(run.Questions[0].Pictured);
        Assert.False(run.Questions[1].Pictured);
        Assert.Equal((1, 1), (run.TextQuestions, run.PicturedQuestions));
    }

    // Rule 2: text shown as a heading is a heading only once the model
    // judged it one; until then, and where judged not one, it is not
    // checked. Text judged not a heading does not end a section.
    [Fact]
    public void HeadingSections_TakeTextShownAsAHeadingOnlyWhereJudgedOne()
    {
        var (window, lines) = HeadingLooks();
        var (measured, archive, _) = SectionsMeasured(window, lines);
        var look = PhoneHeadingLookRun.Run(measured, archive.PhoneScreens);
        var unjudged = PhoneHeadingSectionRun.Run(measured, archive.PhoneScreens, look);

        Assert.Equal((1, 0, 2), (unjudged.Marked, unjudged.Unmarked, unjudged.NotJudgedHeadings));
        Assert.Equal(["Marked title"], unjudged.Questions.Select(question => question.Heading));

        var heading = new PhoneInference(PhoneCheckLevel.Error, "a heading", Inferred: true, "Model", Cloud: false);
        var notHeading = new PhoneInference(PhoneCheckLevel.Info, "not a heading", Inferred: true, "Model", Cloud: false);
        var judged = look.Judged(new Dictionary<PhoneHeadingQuestion, PhoneInference>
        {
            [look.Questions[0]] = heading,
        }, null);
        var one = unjudged.WithHeadingLook(judged);

        Assert.Equal(["Your week", "Marked title"], one.Questions.Select(question => question.Heading));
        Assert.False(one.Questions[0].Marked);
        Assert.Equal(PhoneHeadingVerdict.NotJudged, Assert.Single(one.Entries, entry => entry.State == PhoneHeadingSectionState.NotJudgedHeading).Verdict);
        Assert.DoesNotContain("Start now", one.Questions[0].Spoken, StringComparison.Ordinal);
        Assert.StartsWith("text shown as a heading, not marked as one, over a section", one.Questions[0].Time.Measured, StringComparison.Ordinal);

        var both = unjudged.WithHeadingLook(look.Judged(new Dictionary<PhoneHeadingQuestion, PhoneInference>
        {
            [look.Questions[0]] = heading,
            [look.Questions[1]] = notHeading,
        }, null));

        Assert.Equal(PhoneHeadingVerdict.NotHeading, Assert.Single(both.Entries, entry => entry.State == PhoneHeadingSectionState.NotJudgedHeading).Verdict);
        Assert.Contains("Settings", both.Questions[0].Spoken, StringComparison.Ordinal);
        Assert.Contains("Start now", both.Questions[0].Spoken, StringComparison.Ordinal);
    }

    // Rules 8 to 10: requests for the model for text-based checks, or
    // vision-based where pictured; kept answers judge them by signature.
    [Fact]
    public void HeadingSections_RequestsAndKeptAnswersJudgeEachHeading()
    {
        var run = SectionsRun();
        var pictured = SectionsRun(imageBottom: 600);
        var rgb = new uint[1080 * 2400];
        Array.Fill(rgb, 0x203040u);
        var pixels = new PhonePixels(1080, 2400, rgb);
        var text = Recorder.Analysis.Android.AndroidHeadingSections.RequestOf(run.Questions[1], null);
        var picture = Recorder.Analysis.Android.AndroidHeadingSections.RequestOf(pictured.Questions[0], pixels);

        Assert.Matches("^heading-section-[0-9a-f]{16}$", text.Request.Key);
        Assert.Equal((Recorder.Analysis.ModelUses.TextChecks, Recorder.Analysis.Android.AndroidHeadingSections.TextTemplate, 0),
            (text.Request.Request.Use, text.Request.Request.PromptTemplate, text.Request.Request.Images.Count));
        Assert.Equal((Recorder.Analysis.ModelUses.VisionChecks, Recorder.Analysis.Android.AndroidHeadingSections.PicturedTemplate, 1),
            (picture.Request.Request.Use, picture.Request.Request.PromptTemplate, picture.Request.Request.Images.Count));
        Assert.Equal((text.Use, picture.Use), (Recorder.Analysis.ModelUses.TextChecks, Recorder.Analysis.ModelUses.VisionChecks));
        Assert.Equal(Recorder.Analysis.Android.AndroidHeadingSections.Answers, text.Request.Request.Answers);
        Assert.True(text.Request.Request.ReusedAcrossTasks);
        var input = text.Request.Request.Input;
        Assert.Contains("has the title \"Acme Notes\".", input, StringComparison.Ordinal);
        Assert.Contains("\"Options\" is marked as a heading for a screen reader.", input, StringComparison.Ordinal);
        Assert.Contains("item by item in the app's order: \"Send feedback\"", input, StringComparison.Ordinal);
        Assert.Contains("not on screen, which a screen reader reaches by scrolling, in the app's order: \"Archive\"", input, StringComparison.Ordinal);
        Assert.Contains("so it may continue below the screen.", input, StringComparison.Ordinal);
        Assert.DoesNotContain("Picture A", input, StringComparison.Ordinal);
        Assert.Contains("Picture A is the whole screen, with the heading outlined.", picture.Request.Request.Input, StringComparison.Ordinal);
        Assert.EndsWith("then give the answer.", input, StringComparison.Ordinal);

        // The same texts are the same heading; another section's are not.
        var again = Recorder.Analysis.Android.AndroidHeadingSections.RequestOf(run.Questions[1], null);
        var other = Recorder.Analysis.Android.AndroidHeadingSections.RequestOf(run.Questions[0], null);

        Assert.True(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(text.Request.Signature, again.Request.Signature));
        Assert.False(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(text.Request.Signature, other.Request.Signature));
        Assert.False(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(text.Request.Signature, picture.Request.Signature));

        static JsonElement Output(string answer) =>
            JsonSerializer.SerializeToElement(new { answer, reason = "It would fit any section." });
        var elsewhere = Guid.NewGuid();
        Recorder.Analysis.SignedAnswer Answer(int step, byte[] signature, string answer) =>
            new(Guid.NewGuid(), elsewhere, $"heading-section-0123456789abcdef-{step}", signature,
                new(Output(answer), "service", """{"temperature": null}"""));
        Recorder.Analysis.Android.ScreenTitleKept Kept(string answer) =>
            new([Answer(1, text.Request.Signature, answer), Answer(2, text.Request.Signature, answer)], "Haiku");

        var findings = Recorder.Analysis.Android.AndroidHeadingSections.FromKept(run, [text], Kept("does-not-describe"), null);

        var finding = Assert.Single(findings.Findings);
        Assert.Equal((PhoneCheckNames.HeadingNotDescriptive, PhoneCheckLevel.Warning, "Options"), (finding.Check, finding.Level, finding.Label));
        Assert.StartsWith(
            "Haiku, a cloud model, inferred from what a screen reader and OCR give of the section twice for the same heading, by the heading and its section's text, that the heading \"Options\" does not describe its section",
            finding.Inference!.Text, StringComparison.Ordinal);
        Assert.EndsWith("; a person should verify this", finding.Inference.Text, StringComparison.Ordinal);
        Assert.Contains("judged from the accessibility tree only", finding.Text, StringComparison.Ordinal);
        Assert.Same(finding, Assert.Single(findings.For(finding.Visit)));
        Assert.Equal(
            "2 headings left by the rules are for Haiku; 1 heading answered: 0 judged to describe their sections, not confirmed, and 1 warning, 1 heading not yet asked",
            findings.ModelNote);

        var describes = Recorder.Analysis.Android.AndroidHeadingSections.FromKept(run, [text], Kept("describes-the-section"), null);

        Assert.Empty(describes.Findings);
        Assert.Equal(1, describes.Described);
        Assert.Single(describes.Inferences);

        // With no model, nothing is judged (rule 9).
        var noModel = Recorder.Analysis.Android.AndroidHeadingSections.FromKept(run, [text], null, null, textNone: "the project's settings choose no model for text-based checks");

        Assert.Empty(noModel.Findings);
        Assert.EndsWith(
            "; 2 headings left by the rules were not judged, as no model may be used for text-based checks: the project's settings choose no model for text-based checks",
            noModel.ModelNote, StringComparison.Ordinal);
    }

    // Rule 8's levels.
    [Theory]
    [InlineData("describes-the-section", PhoneCheckLevel.Info, "that the heading \"Options\" describes its section, not confirmed")]
    [InlineData("does-not-describe", PhoneCheckLevel.Warning, "; a person should verify this")]
    [InlineData("cannot-tell", PhoneCheckLevel.Warning, "could not tell from what a screen reader and OCR give of the section twice whether the heading \"Options\" describes its section")]
    public void HeadingSections_Interpret_GivesEachAgreedAnswersLevel(string answer, PhoneCheckLevel level, string says)
    {
        var asked = SectionsRun().Questions[1];

        var inference = Recorder.Analysis.Android.AndroidHeadingSections.Interpret(new(answer, "First."), new(answer, "Second."), "Haiku", cloud: false, asked);

        Assert.Equal((level, true, false), (inference.Level, inference.Inferred, inference.Cloud));
        Assert.StartsWith("Haiku, a local model,", inference.Text, StringComparison.Ordinal);
        Assert.Contains(says, inference.Text, StringComparison.Ordinal);

        var split = Recorder.Analysis.Android.AndroidHeadingSections.Interpret(new(answer, "First."), new(answer == "cannot-tell" ? "does-not-describe" : "cannot-tell", "Second."), "Haiku", cloud: false, asked);

        Assert.Equal(PhoneCheckLevel.Warning, split.Level);
        Assert.Contains("gave two answers", split.Text, StringComparison.Ordinal);
    }

    // Step 25: text on screen not in the tree. A window of made-up items:
    // a title, two lines of text, an image, a custom view that draws text
    // with none of its own, a small button and an item not shown; and the
    // lines OCR read in no item's text, one of each kind the rules sort.
    private static PhoneCheckWindow DrawnTextScreen(params (int Index, string Text)[] more)
    {
        AndroidTreeNode Node(int index, int? parent, (int Left, int Top, int Right, int Bottom) bounds, string className,
            string? text = null, bool clickable = false, bool visible = true) =>
            ImageNode(index, parent, bounds, className, null, text) with { Clickable = clickable, VisibleToUser = visible };
        return new PhoneCheckWindow(512, 1,
        [
            Node(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout"),
            Node(1, 0, (40, 100, 1040, 200), "android.widget.TextView", "Daily summary"),
            Node(2, 0, (40, 300, 1040, 380), "android.widget.TextView", "Steps today"),
            Node(3, 0, (40, 400, 1040, 480), "android.widget.TextView", "Sleep score"),
            Node(4, 0, (0, 600, 1080, 900), "android.widget.ImageView"),
            Node(5, 0, (0, 1000, 1080, 1700), "com.example.ChartView"),
            Node(6, 0, (40, 2100, 540, 2300), "android.widget.Button", "Start", clickable: true),
            Node(7, 0, (0, 0, 0, 0), "android.widget.TextView", "Weekly goal reached", visible: false),
            .. more.Select(each => Node(each.Index, 0, (40, 1800, 1040, 1880), "android.widget.TextView", each.Text)),
        ], "Daily summary");
    }

    private static PhoneTextLine DrawnLine(PhoneCheckWindow window, string text, int container, int top) =>
        new(null, window.Items[container], PhoneTextPlace.FoundByOcr, null, text, new PhoneBounds(60, top, 900, top + 50), null, 40);

    private (PhoneTextNotInTreeRun Run, PhoneVisit Visit) DrawnTextRun(Func<PhoneCheckWindow, PhoneTextLine[]>? placed = null, string? engine = "Windows OCR, en-US")
    {
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var first = DrawnTextScreen();
        var second = DrawnTextScreen((8, "Resting heart rate"));
        PhoneTextLine[] firstLines =
        [
            DrawnLine(first, "Steps today", 5, 1020),
            DrawnLine(first, "Daily streak kept", 5, 1100),
            DrawnLine(first, "Weekly goal reached", 5, 1300),
            DrawnLine(first, "Resting heart rate", 5, 1500),
            DrawnLine(first, "OK", 5, 1600),
            DrawnLine(first, "Sale today", 4, 700),
            DrawnLine(first, "Begin now", 6, 2150),
            .. placed?.Invoke(first) ?? [],
        ];
        PhoneTextLine[] secondLines = [DrawnLine(second, "Daily streak kept", 5, 1100)];
        var settled = visit.SettledSnapshots.Take(2).ToList();
        PhoneContrastSnapshot Snapshot(int at, PhoneCheckWindow window, PhoneTextLine[] lines) =>
            new(visit, settled[at], archive.PhoneAccessibility.TakenBy(settled[at], visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Screenshot, settled[at].MonotonicNanoseconds, $"p{at}.png", $"p{at}.png"),
                engine, lines, null, window);
        var measured = PhoneContrastRun.Of([Snapshot(0, first, firstLines), Snapshot(1, second, secondLines)]);
        return (PhoneTextNotInTreeRun.Run(measured, archive.PhoneScreens), visit);
    }

    // Rules 1, 3 to 7 and 2c: each line once a visit; inside an image, too
    // short, held by a visible item elsewhere, a small item's own text
    // misread, or held by an item shown in another snapshot of the visit,
    // is set aside; the rest are asked, with whether an item not shown
    // holds the same text.
    [Fact]
    public void TextNotInTree_SortsEachLineOnceAVisit()
    {
        var (run, visit) = DrawnTextRun();

        Assert.Equal((2, 0, 0), (run.SnapshotsRead, run.OutOfStepSnapshots, run.OutOfStepLines));
        Assert.Equal(7, run.Entries.Count);
        Assert.Equal((0, 1, 1, 1, 1, 1), (run.Captions, run.InImages, run.ShortLines, run.HeldElsewhere, run.Misreads, run.OtherSnapshots));
        Assert.Equal(["Daily streak kept", "Weekly goal reached"], run.Questions.Select(question => question.Ocr));
        var (drawn, hidden) = (run.Questions[0], run.Questions[1]);
        Assert.Equal(("com.example.ChartView", null, false), (drawn.ContainerClass, drawn.ContainerText, drawn.HeldByHidden));
        Assert.True(hidden.HeldByHidden);
        Assert.EndsWith("\u001Focr dallystreakkept", drawn.Identity, StringComparison.Ordinal);
        Assert.Contains("Daily summary", drawn.ScreenText, StringComparison.Ordinal);
        Assert.Contains("that no item of the accessibility tree holds, though an item marked not shown holds the same text", hidden.Time.Measured, StringComparison.Ordinal);
        Assert.True(run.Read(visit));
        Assert.Empty(run.Findings);
        Assert.Equal("Text on screen not in the accessibility tree (WCAG 1.1.1, 4.1.2)", PhoneCheckNames.Title(PhoneCheckNames.TextNotInTree));

        // With no OCR, nothing is checked.
        var (unread, _) = DrawnTextRun(engine: null);

        Assert.Equal((0, 0), (unread.SnapshotsRead, unread.Entries.Count));
    }

    // Rule 2b: a picture is out of step where the tree has at least 3 text
    // items and OCR found fewer than half of them where the tree places
    // them; its lines are counted, not judged.
    [Fact]
    public void TextNotInTree_LeavesOutAPictureOutOfStepWithTheTree()
    {
        PhoneTextLine Placed(PhoneCheckWindow window, int item, PhoneTextPlace place) =>
            new(window.Items[item], null, place, window.Items[item].Node.Text, null, window.Items[item].Bounds, null, 40);
        var (outOfStep, _) = DrawnTextRun(window =>
            [.. new[] { 1, 2, 3 }.Select(item => Placed(window, item, item == 1 ? PhoneTextPlace.AppTextPlacedByOcr : PhoneTextPlace.AppTextOnItemBounds))]);

        Assert.Equal((1, 7), (outOfStep.OutOfStepSnapshots, outOfStep.OutOfStepLines));
        Assert.Equal(["Daily streak kept"], outOfStep.Questions.Select(question => question.Ocr));

        var (inStep, _) = DrawnTextRun(window =>
            [.. new[] { 1, 2, 3 }.Select(item => Placed(window, item, item == 3 ? PhoneTextPlace.AppTextOnItemBounds : PhoneTextPlace.AppTextPlacedByOcr))]);

        Assert.Equal(0, inStep.OutOfStepSnapshots);
        Assert.Equal(2, inStep.Questions.Count);
    }

    // Rules 8 to 10: the request gives the screen and a close-up, the line,
    // its item and what a screen reader says; kept answers judge each line
    // by signature; drawn text is a warning, covered and not text counted.
    [Fact]
    public void TextNotInTree_RequestsAndKeptAnswersJudgeEachLine()
    {
        var (run, _) = DrawnTextRun();
        var rgb = new uint[1080 * 2400];
        Array.Fill(rgb, 0x203040u);
        var pixels = new PhonePixels(1080, 2400, rgb);
        var drawn = Recorder.Analysis.Android.AndroidTextNotInTree.RequestOf(run.Questions[0], pixels);
        var hidden = Recorder.Analysis.Android.AndroidTextNotInTree.RequestOf(run.Questions[1], pixels);

        Assert.Matches("^text-not-in-tree-[0-9a-f]{16}$", drawn.Key);
        Assert.Equal((Recorder.Analysis.ModelUses.VisionChecks, Recorder.Analysis.Android.AndroidTextNotInTree.Template, 2),
            (drawn.Request.Use, drawn.Request.PromptTemplate, drawn.Request.Images.Count));
        Assert.Equal(Recorder.Analysis.Android.AndroidTextNotInTree.Answers, drawn.Request.Answers);
        var input = drawn.Request.Input;
        Assert.Contains("OCR read the outlined line as \"Daily streak kept\"", input, StringComparison.Ordinal);
        Assert.Contains("inside an item of class com.example.ChartView that has no text of its own", input, StringComparison.Ordinal);
        Assert.DoesNotContain("marks as not shown", input, StringComparison.Ordinal);
        Assert.Contains("marks as not shown", hidden.Request.Input, StringComparison.Ordinal);
        Assert.EndsWith("then give the answer.", input, StringComparison.Ordinal);
        Assert.False(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(drawn.Signature, hidden.Signature));
        Assert.True(Recorder.Analysis.Android.AndroidImageAlternative.SameImage(
            drawn.Signature, Recorder.Analysis.Android.AndroidTextNotInTree.RequestOf(run.Questions[0], pixels).Signature));

        static JsonElement Output(string answer) =>
            JsonSerializer.SerializeToElement(new { answer, reason = "The words are drawn on the chart." });
        var elsewhere = Guid.NewGuid();
        Recorder.Analysis.SignedAnswer Answer(string key, int step, byte[] signature, string answer) =>
            new(Guid.NewGuid(), elsewhere, $"text-not-in-tree-{key}-{step}", signature,
                new(Output(answer), "service", """{"temperature": null}"""));
        IReadOnlyList<Recorder.Analysis.SignedAnswer> Kept(string first, string second) =>
        [
            Answer("0123456789abcdef", 1, drawn.Signature, first), Answer("0123456789abcdef", 2, drawn.Signature, first),
            Answer("fedcba9876543210", 1, hidden.Signature, second), Answer("fedcba9876543210", 2, hidden.Signature, second),
        ];

        var judged = Recorder.Analysis.Android.AndroidTextNotInTree.FromKept(run, [(run.Questions[0], drawn), (run.Questions[1], hidden)], Kept("drawn-text", "covered"), "Haiku");

        var finding = Assert.Single(judged.Findings);
        Assert.Equal((PhoneCheckNames.TextNotInTree, PhoneCheckLevel.Warning, "'Daily streak kept'"), (finding.Check, finding.Level, finding.Label));
        Assert.StartsWith(
            "Haiku, a cloud model, inferred from the pictures twice for the same line, by its pixels, its text and item and what a screen reader says on the screen, that the text \"Daily streak kept\" is shown to users where a screen reader cannot reach it",
            finding.Inference!.Text, StringComparison.Ordinal);
        Assert.EndsWith("; a person should verify this", finding.Inference.Text, StringComparison.Ordinal);
        Assert.Same(finding, Assert.Single(judged.For(finding.Visit)));
        Assert.Equal((1, 0, 0), (judged.Covered, judged.NotText, judged.Unanswered));
        Assert.Equal(2, judged.Inferences.Count());
        Assert.Equal(
            "2 lines left by the rules are for Haiku; 2 lines answered: 1 judged covered and 0 judged not text, not confirmed, and 1 warning",
            judged.ModelNote);

        var notText = Recorder.Analysis.Android.AndroidTextNotInTree.FromKept(run, [(run.Questions[0], drawn)], Kept("not-text", "covered"), "Haiku");

        Assert.Empty(notText.Findings);
        Assert.Equal((1, 1), (notText.NotText, notText.Unanswered));
        Assert.EndsWith(", 1 line not yet asked", notText.ModelNote, StringComparison.Ordinal);
        Assert.Equal(
            "2 lines left by the rules were not judged, as no model may be used for vision-based checks: no model",
            Recorder.Analysis.Android.AndroidTextNotInTree.NotAsked(2, "no model"));
    }

    // Rule 9's levels.
    [Theory]
    [InlineData("drawn-text", PhoneCheckLevel.Warning, "is shown to users where a screen reader cannot reach it")]
    [InlineData("covered", PhoneCheckLevel.Info, "is behind a dialog, sheet or dimmed layer, or covered, not confirmed")]
    [InlineData("not-text", PhoneCheckLevel.Info, "that the text \"Daily streak kept\" is not text, not confirmed")]
    [InlineData("cannot-tell", PhoneCheckLevel.Warning, "could not tell from the pictures twice whether the text \"Daily streak kept\" is drawn where a screen reader cannot reach it")]
    public void TextNotInTree_Interpret_GivesEachAgreedAnswersLevel(string answer, PhoneCheckLevel level, string says)
    {
        var asked = DrawnTextRun().Run.Questions[0];

        var inference = Recorder.Analysis.Android.AndroidTextNotInTree.Interpret(new(answer, "First."), new(answer, "Second."), "Haiku", cloud: false, asked);

        Assert.Equal((level, true, false), (inference.Level, inference.Inferred, inference.Cloud));
        Assert.StartsWith("Haiku, a local model,", inference.Text, StringComparison.Ordinal);
        Assert.Contains(says, inference.Text, StringComparison.Ordinal);

        var split = Recorder.Analysis.Android.AndroidTextNotInTree.Interpret(new(answer, "First."), new(answer == "cannot-tell" ? "drawn-text" : "cannot-tell", "Second."), "Haiku", cloud: false, asked);

        Assert.Equal(PhoneCheckLevel.Warning, split.Level);
        Assert.Contains("gave two answers", split.Text, StringComparison.Ordinal);
        Assert.EndsWith("; check manually", split.Text, StringComparison.Ordinal);
    }

    // The body text is the height most of the characters have.
    [Fact]
    public void HeadingLook_BodyHeight_IsTheMedianInkHeightByCharacters()
    {
        PhoneTextLine Line(string text, int ink) => new(null, null, PhoneTextPlace.FoundByOcr, null, text, new PhoneBounds(0, 0, 10, 10), null, ink, ink);

        Assert.Equal(30, PhoneHeadingLookRun.BodyHeight([Line("Title", 60), Line("A longer line of body text", 30), Line("Hi", 20)]));
        Assert.Equal(60, PhoneHeadingLookRun.BodyHeight([Line("A long large line of text", 60), Line("Small", 30)]));
        Assert.Equal(0, PhoneHeadingLookRun.BodyHeight([Line("Unmeasured", 0)]));
    }

    // A description that holds some of the words, and words split between
    // two images go to the smaller one they are in.
    [Fact]
    public void ImagesOfText_SaysHowMuchTheDescriptionHolds_AndGivesWordsToTheSmallestImage()
    {
        var window = new PhoneCheckWindow(512, 1,
        [
            ImageNode(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout"),
            ImageNode(1, 0, (0, 0, 1080, 1000), "android.widget.ImageView", "Sunset over a lake"),
            ImageNode(2, 0, (100, 100, 500, 300), "android.widget.ImageView", "Summer offer"),
        ]);
        var lines = new PhoneTextLine[]
        {
            new(null, window.Items[2], PhoneTextPlace.FoundByOcr, null, "Summer sale", new PhoneBounds(120, 150, 480, 190), null, 40),
            new(null, window.Items[1], PhoneTextPlace.FoundByOcr, null, "Lake Ontario", new PhoneBounds(600, 800, 1000, 840), null, 40),
        };

        var inside = PhoneImagesOfTextRun.InImages(window.Items.Skip(1).ToList(), lines);

        Assert.Equal("Summer sale", Assert.Single(inside[window.Items[2]]).OcrText);
        Assert.Equal("Lake Ontario", Assert.Single(inside[window.Items[1]]).OcrText);

        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var at = visit.SettledSnapshots[0];
        var measured = PhoneContrastRun.Of(
        [
            new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Screenshot, at.MonotonicNanoseconds, "p.png", "p.png"),
                "Windows OCR, en-US", lines, null, window),
        ]);
        var run = PhoneImagesOfTextRun.Run(measured, archive.PhoneScreens, Device(), PhoneAuditLevel.AA);

        Assert.Equal(2, run.Findings.Count);
        Assert.Contains("its description holds some of them", run.Findings[0].Times[0].Measured, StringComparison.Ordinal);
        Assert.Contains("its description holds some of them", run.Findings[1].Times[0].Measured, StringComparison.Ordinal);
    }

    // The images the rules leave are the model's questions, one for each
    // image whatever its two findings at AAA, in the findings' order.
    [Fact]
    public void ImagesOfText_LeavesAQuestionForEachImageTheRulesDoNotSettle()
    {
        var pictures = PicturesOfText();
        var run = ImagesOfTextRun(pictures, level: PhoneAuditLevel.AAA);

        Assert.Equal(4, run.Findings.Count);
        Assert.Equal(2, run.Questions.Count);
        var described = run.Questions[0];
        Assert.Equal(pictures.Window.Items[1].Identity, described.Identity);
        Assert.Equal(new PhoneBounds(59, 324, 1022, 724), described.Bounds);
        Assert.Equal("OCR read 2 lines of text inside the image: \"Sleep Hygiene\", \"Steps to take for a good night's sleep.\"", described.Ocr);
        Assert.Equal("Sleep Hygiene\nSteps to take for a good night's sleep.", described.Description);
        Assert.Equal(PhonePictureKind.Screenshot, described.Picture.Kind);
        Assert.Null(run.Questions[1].Description);
        Assert.Equal(described, run.QuestionOf(run.Findings[0]));
        Assert.Equal(described, run.QuestionOf(run.Findings.Single(finding =>
            finding.Check == PhoneCheckNames.ImageOfTextAaa && finding.Identity == described.Identity)));
        Assert.Equal((0, 0, 0, 0, 0), (run.CaptionLines, run.EdgeLines, run.NeighbourLines, run.OutOfStep, run.ManualChecks));
        Assert.Null(run.ModelNote);
    }

    // Rule 1: a line TalkBack was saying is its caption, not the image's.
    [Fact]
    public void ImagesOfText_LeavesOutALineTalkBackWasSaying()
    {
        var archive = Archive(out _);
        var at = Assert.Single(archive.PhoneScreens.Visits).SettledSnapshots[0].MonotonicNanoseconds;
        var said = Math.Max(0, at - Second);
        var speech = new TalkBackSpeechTimeline(
        [
            (new SessionTimelineEvent(1, "u1", "observed", AndroidTalkBackRecords.SpeechChannel, AndroidTalkBackRecords.UtteranceStartedEventType, said, "Sale 50%"),
                JsonSerializer.SerializeToElement(new { utteranceId = "u1", text = "Sale 50%" })),
        ],
            10 * Second);

        var run = ImagesOfTextRun(PicturesOfText(), speech: speech);

        Assert.Equal(1, run.CaptionLines);
        var finding = Assert.Single(run.Findings);
        Assert.Equal(PicturesOfText().Window.Items[1].Identity, finding.Identity);

        // TalkBack's speech of more than ten seconds before is not its caption.
        var early = new TalkBackSpeechTimeline(
        [
            (new SessionTimelineEvent(1, "u1", "observed", AndroidTalkBackRecords.SpeechChannel, AndroidTalkBackRecords.UtteranceStartedEventType, at - (11 * Second), "Sale 50%"),
                JsonSerializer.SerializeToElement(new { utteranceId = "u1", text = "Sale 50%" })),
        ],
            10 * Second);
        if (at >= 11 * Second)
        {
            Assert.Equal(0, ImagesOfTextRun(PicturesOfText(), speech: early).CaptionLines);
        }
    }

    // Rule 2: a picture out of step with the tree is not judged; its
    // images are a check not run, not a pass and not a question.
    [Fact]
    public void ImagesOfText_DoesNotJudgeAPictureOutOfStepWithTheTree()
    {
        var run = ImagesOfTextRun(PicturesOfText(outside: ["Good morning Alex", "Your sleep score", "Weekly report ready"]));

        Assert.Equal(2, run.OutOfStep);
        Assert.Equal(2, run.Findings.Count);
        Assert.All(run.Findings, finding => Assert.Equal(PhoneCheckLevel.NotRun, finding.Level));
        Assert.StartsWith(
            "not judged, as the picture is out of step with the tree: OCR read 3 lines outside every image that no item's text holds, more than the 1 text item it found where the tree places them; ",
            run.Findings[0].Times[0].Measured, StringComparison.Ordinal);
        Assert.Empty(run.Questions);

        // Two lines are too few to say so.
        var two = ImagesOfTextRun(PicturesOfText(outside: ["Good morning Alex", "Your sleep score"]));

        Assert.Equal(0, two.OutOfStep);
        Assert.All(two.Findings, finding => Assert.Equal(PhoneCheckLevel.Warning, finding.Level));
    }

    // Rule 3: a line not wholly inside the image, and a line an item
    // beside the image holds, are left out.
    [Fact]
    public void ImagesOfText_LeavesOutLinesPastTheEdgeAndLinesAnItemBesideHolds()
    {
        var past = ImagesOfTextRun(PicturesOfText(sale: (900, 1600, 1100, 1642)));

        Assert.Equal(1, past.EdgeLines);
        Assert.Single(past.Findings);

        var beside = ImagesOfTextRun(PicturesOfText(extra: ImageNode(6, 0, (59, 1930, 1022, 1990), "android.widget.TextView", text: "Sale 50% today")));

        Assert.Equal(1, beside.NeighbourLines);
        Assert.Single(beside.Findings);
    }

    // Rule 4: in an app drawn with Flutter, an image whose description
    // holds every word is a manual check, not a question.
    [Fact]
    public void ImagesOfText_MakesADescribedImageInAFlutterAppAManualCheck()
    {
        var pictures = PicturesOfText();
        var run = ImagesOfTextRun(pictures, FlutterDevice());

        Assert.Equal(1, run.ManualChecks);
        Assert.Equal(2, run.Findings.Count);
        Assert.EndsWith(
            "the app draws its own text with Flutter, as detected at the recording's stop, and the image's description holds every word, " +
            "so whether the words are drawn in the image cannot be told from the recording: check manually; from the phone's screenshot",
            run.Findings[0].Times[0].Measured, StringComparison.Ordinal);
        Assert.Null(run.QuestionOf(run.Findings[0]));
        Assert.Equal(pictures.Window.Items[5].Identity, Assert.Single(run.Questions).Identity);
    }

    // The model's answers judge their images' findings, and say they are
    // inferred; every other finding is as it was.
    [Fact]
    public void ImagesOfText_JudgedByTheModelsAnswers_SaysTheyAreInferred()
    {
        var run = ImagesOfTextRun(PicturesOfText(), level: PhoneAuditLevel.AAA);
        var sale = run.Questions[1];
        var answer = Recorder.Analysis.Android.AndroidImagesOfText.Interpret(
            new Recorder.Analysis.ClosedAnswerOf("could-be-text", "Plain white words on a flat colour."),
            new Recorder.Analysis.ClosedAnswerOf("could-be-text", "Words in a plain typeface on a flat colour."),
            "Claude Haiku 5.5", cloud: true);

        var judged = run.Judged(new Dictionary<PhoneImageOfTextQuestion, PhoneInference> { [sale] = answer }, "2 images were for the model.");

        Assert.Equal("2 images were for the model.", judged.ModelNote);
        Assert.Equal(run.Questions, judged.Questions);
        var ofSale = judged.Findings.Where(finding => finding.Identity == sale.Identity).ToList();
        Assert.Equal(2, ofSale.Count);
        Assert.All(ofSale, finding =>
        {
            Assert.Equal(PhoneCheckLevel.Error, finding.Level);
            Assert.Same(answer, finding.Inference);
            Assert.DoesNotContain("not judged", finding.Times[0].Measured, StringComparison.Ordinal);
            Assert.Contains("; " + answer.Text + "; seen once", finding.Text, StringComparison.Ordinal);
            Assert.Null(judged.QuestionOf(finding));
        });
        Assert.StartsWith("Claude Haiku 5.5, a cloud model, inferred from the pictures twice that the text could be shown as real text", answer.Text, StringComparison.Ordinal);
        var other = judged.Findings.Where(finding => finding.Identity != sale.Identity).ToList();
        Assert.Equal(2, other.Count);
        Assert.All(other, finding => Assert.Equal((PhoneCheckLevel.Warning, (PhoneInference?)null), (finding.Level, finding.Inference)));
        Assert.Equal(run.Questions[0], judged.QuestionOf(other[0]));
    }

    // The request is the same for the same picture and words, so that an
    // answer kept by it is reused; the screen is at half size, outlined.
    [Fact]
    public void ImagesOfText_RequestIsTheSameForTheSameImage()
    {
        var run = ImagesOfTextRun(PicturesOfText());
        var rgb = new uint[1080 * 2400];
        Array.Fill(rgb, 0x203040u);
        var pixels = new PhonePixels(1080, 2400, rgb);

        var first = Recorder.Analysis.Android.AndroidImagesOfText.RequestOf(run.Questions[1], pixels);
        var again = Recorder.Analysis.Android.AndroidImagesOfText.RequestOf(run.Questions[1], new PhonePixels(1080, 2400, [.. rgb]));
        var other = Recorder.Analysis.Android.AndroidImagesOfText.RequestOf(run.Questions[0], pixels);

        Assert.Equal(first.Key, again.Key);
        Assert.NotEqual(first.Key, other.Key);
        Assert.Matches("^image-of-text-[0-9a-f]{16}$", first.Key);
        Assert.Equal(first.Key + "-2", first.StepKey(2));
        Assert.Equal(Recorder.Analysis.ModelUses.VisionChecks, first.Request.Use);
        Assert.True(first.Request.ReusedAcrossTasks);
        Assert.Equal(Recorder.Analysis.Android.AndroidImagesOfText.Answers, first.Request.Answers);
        Assert.Equal(["A", "B"], first.Request.Images.Select(image => image.Reference));
        Assert.Equal(first.Request.Images[0].Content, again.Request.Images[0].Content);
        Assert.Equal((540, 1200), PngSize(first.Request.Images[0].Content));
        Assert.Equal((963, 400), PngSize(first.Request.Images[1].Content));
        Assert.Contains("OCR read inside the image: OCR read 1 line of text inside the image: \"Sale 50%\"", first.Request.Input, StringComparison.Ordinal);
        Assert.Contains("The image has no description for a screen reader.", first.Request.Input, StringComparison.Ordinal);

        static (int Width, int Height) PngSize(byte[] png) =>
            ((png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19], (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);
    }

    // Step 26: a web image is an image to the check. A WebView holds a
    // web image whose alt text the browser gave as its text, and an
    // ImageView beside it; OCR read words inside each that no item holds.
    private static (PhoneCheckWindow Window, PhoneTextLine[] Lines) WebPictures(string? altText = "Spring offer", string? altDescription = null)
    {
        var window = new PhoneCheckWindow(512, 1,
        [
            ImageNode(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout"),
            ImageNode(1, 0, (0, 200, 1080, 1800), "android.webkit.WebView"),
            ImageNode(2, 1, (59, 324, 1022, 724), "android.widget.Image", altDescription, altText),
            ImageNode(3, 0, (59, 1900, 1022, 2300), "android.widget.ImageView"),
        ]);
        PhoneTextLine[] lines =
        [
            new(null, window.Items[2], PhoneTextPlace.FoundByOcr, null, "Sale 50%", new PhoneBounds(100, 400, 500, 442), null, 42),
            new(null, window.Items[3], PhoneTextPlace.FoundByOcr, null, "Free delivery", new PhoneBounds(100, 2000, 600, 2042), null, 42),
        ];
        return (window, lines);
    }

    // Rules 1, 2 and 4: the web image's words are a finding, as the
    // ImageView's are; its alt text, given as its text, is what its
    // question and its measurement name; and only its finding is web
    // content.
    [Fact]
    public void ImagesOfText_CountsAWebImage_ReadsItsAltText_AndMarksItsFindingWeb()
    {
        var pictures = WebPictures();
        var run = ImagesOfTextRun(pictures);

        Assert.Equal(2, run.Findings.Count);
        var web = run.Findings.Single(finding => finding.Identity == pictures.Window.Items[2].Identity);
        var view = run.Findings.Single(finding => finding.Identity == pictures.Window.Items[3].Identity);
        Assert.True(web.Web);
        Assert.False(view.Web);
        Assert.Contains("its alt text holds none of them", web.Times[0].Measured, StringComparison.Ordinal);
        Assert.Contains("it has no description", view.Times[0].Measured, StringComparison.Ordinal);
        var question = run.QuestionOf(web)!;
        Assert.True(question.Web);
        Assert.Equal("Spring offer", question.Description);
        Assert.False(run.QuestionOf(view)!.Web);
    }

    // Rule 2: a web image's alt text may be its text, its description, or
    // both; the same words given twice are given once.
    [Theory]
    [InlineData("Sale 50%", null, "Sale 50%", "its alt text holds them")]
    [InlineData(null, "Sale 50%", "Sale 50%", "its alt text holds them")]
    [InlineData("Sale 50%", "Sale 50%", "Sale 50%", "its alt text holds them")]
    [InlineData("Sale", "Half price", "Sale Half price", "its alt text holds some of them")]
    [InlineData(null, null, null, "it has no alt text")]
    public void ImagesOfText_TakesAWebImagesAltTextFromItsTextOrDescription(string? text, string? description, string? alt, string says)
    {
        var pictures = WebPictures(text, description);
        var run = ImagesOfTextRun(pictures);

        var web = run.Findings.Single(finding => finding.Identity == pictures.Window.Items[2].Identity);
        Assert.Contains(says, web.Times[0].Measured, StringComparison.Ordinal);
        Assert.Equal(alt, run.QuestionOf(web)!.Description);
    }

    // Rule 3: a web image's question says it is on a web page, gives its
    // alt text, and adds the answer that it had not loaded; an ImageView's
    // question and answers are as they were.
    [Fact]
    public void ImagesOfText_AsksOfAWebImageWhetherItHadLoaded_AndOfAnImageViewAsBefore()
    {
        var pictures = WebPictures();
        var run = ImagesOfTextRun(pictures);
        var rgb = new uint[1080 * 2400];
        Array.Fill(rgb, 0x203040u);
        var pixels = new PhonePixels(1080, 2400, rgb);
        var web = run.Questions.Single(question => question.Web);
        var view = run.Questions.Single(question => !question.Web);

        var ofWeb = Recorder.Analysis.Android.AndroidImagesOfText.RequestOf(web, pixels);
        var ofView = Recorder.Analysis.Android.AndroidImagesOfText.RequestOf(view, pixels);

        Assert.Equal(Recorder.Analysis.Android.AndroidImagesOfText.WebAnswers, ofWeb.Request.Answers);
        Assert.Contains("not-loaded", ofWeb.Request.Answers!);
        Assert.Contains("The image is on a web page shown in the app.", ofWeb.Request.Input, StringComparison.Ordinal);
        Assert.Contains("The image's alt text for a screen reader: \"Spring offer\"", ofWeb.Request.Input, StringComparison.Ordinal);
        Assert.Contains("- not-loaded: the image has not loaded", ofWeb.Request.Input, StringComparison.Ordinal);
        Assert.Equal(Recorder.Analysis.Android.AndroidImagesOfText.Answers, ofView.Request.Answers);
        Assert.DoesNotContain("web page", ofView.Request.Input, StringComparison.Ordinal);
        Assert.DoesNotContain("not-loaded", ofView.Request.Input, StringComparison.Ordinal);
        Assert.Contains("The image has no description for a screen reader.", ofView.Request.Input, StringComparison.Ordinal);
        Assert.Equal(
            Recorder.Analysis.Android.AndroidImagesOfText.Question("App", "Screen", "OCR read", null),
            Recorder.Analysis.Android.AndroidImagesOfText.Question("App", "Screen", "OCR read", null, web: false));
    }

    // Rule 3: a web image both answers agree had not loaded is not run,
    // and its finding keeps its web mark.
    [Fact]
    public void ImagesOfText_AWebImageThatHadNotLoaded_IsNotRun()
    {
        var pictures = WebPictures();
        var run = ImagesOfTextRun(pictures);
        var web = run.Questions.Single(question => question.Web);
        var answer = Recorder.Analysis.Android.AndroidImagesOfText.Interpret(
            new Recorder.Analysis.ClosedAnswerOf("not-loaded", "A broken-image icon with its alt text."),
            new Recorder.Analysis.ClosedAnswerOf("not-loaded", "The browser's stand-in for an image."),
            "Claude Haiku 5.5", cloud: true);

        var judged = run.Judged(new Dictionary<PhoneImageOfTextQuestion, PhoneInference> { [web] = answer }, null);

        var finding = judged.Findings.Single(finding => finding.Identity == web.Identity);
        Assert.Equal(PhoneCheckLevel.NotRun, finding.Level);
        Assert.True(finding.Web);
        Assert.Contains("had not loaded", finding.Inference!.Text, StringComparison.Ordinal);
    }

    // Answers kept by their requests judge the run for the Checks tab: a
    // repeatable model's one answer, a cloud model's two.
    [Fact]
    public void ImagesOfText_FromKeptAnswers_JudgesTheImagesAnswered()
    {
        var run = ImagesOfTextRun(PicturesOfText());
        var rgb = new uint[1080 * 2400];
        var pixels = new PhonePixels(1080, 2400, rgb);
        var requests = run.Questions.Select(question => Recorder.Analysis.Android.AndroidImagesOfText.RequestOf(question, pixels)).ToList();
        static JsonElement Output(string answer) =>
            JsonSerializer.SerializeToElement(new { answer, reason = "The pictures show it." });
        var kept = new Dictionary<string, Recorder.Analysis.KeptAnswer>
        {
            [requests[0].StepKey(1)] = new(Output("logotype"), "stated", """{"temperature": 0}"""),
            [requests[1].StepKey(1)] = new(Output("could-be-text"), "service", """{"temperature": null}"""),
        };

        var judged = Recorder.Analysis.Android.AndroidImagesOfText.FromKept(run, requests, kept, "Haiku");

        Assert.Equal(PhoneCheckLevel.Info, judged.Findings[0].Level);
        Assert.StartsWith("Haiku, a local model, inferred from the pictures that the text is part of a logo", judged.Findings[0].Inference!.Text, StringComparison.Ordinal);
        Assert.Equal(PhoneCheckLevel.Warning, judged.Findings[1].Level);
        Assert.Equal("2 images left by the rules are for Haiku; 1 image answered, 1 image not yet asked", judged.ModelNote);

        kept[requests[1].StepKey(2)] = new(Output("could-be-text"), "service", """{"temperature": null}""");
        var both = Recorder.Analysis.Android.AndroidImagesOfText.FromKept(run, requests, kept, "Haiku");

        Assert.Equal(PhoneCheckLevel.Error, both.Findings[1].Level);
        Assert.True(both.Findings[1].Inference!.Cloud);
        Assert.Equal("2 images left by the rules are for Haiku; 2 images answered", both.ModelNote);
    }

    // Step 9: Label in Name, on made-up controls. Items 2 to 17 are each a
    // case; OCR's lines are placed as the contrast measurement places them.
    private static AndroidTreeNode Control(
        int index, int? parent, (int Left, int Top, int Right, int Bottom) bounds, string className,
        string? description = null, string? text = null, bool clickable = true, bool checkable = false, bool editable = false,
        IReadOnlyList<int>? labeledBy = null, bool focusable = false, bool longClickable = false, bool enabled = true, bool visible = true) =>
        new()
        {
            Index = index,
            Parent = parent,
            ClassName = className,
            Bounds = new AndroidNodeBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            ContentDescription = description,
            Text = text,
            ImportantForAccessibility = true,
            VisibleToUser = visible,
            Enabled = enabled,
            Clickable = clickable,
            LongClickable = longClickable,
            Focusable = focusable,
            Checkable = checkable,
            Editable = editable,
            LabeledBy = labeledBy ?? [],
            Checked = "unchecked",
        };

    private static (PhoneCheckWindow Window, PhoneTextLine[] Lines) Labelled()
    {
        const string Button = "android.widget.Button";
        const string Text = "android.widget.TextView";
        var window = new PhoneCheckWindow(512, 1,
        [
            Control(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout", clickable: false),
            Control(1, 0, (0, 0, 1080, 100), Button, "Search for items"),
            Control(2, 1, (10, 10, 300, 90), Text, text: "Search", clickable: false),
            Control(3, 0, (0, 100, 1080, 200), Button, "Find items"),
            Control(4, 3, (10, 110, 300, 190), Text, text: "Search", clickable: false),
            Control(5, 0, (0, 200, 1080, 300), "android.widget.ImageView", "Open your settings"),
            Control(6, 0, (0, 300, 1080, 400), Button, "Data privacy"),
            Control(7, 0, (0, 400, 400, 500), Text, text: "Remember me", clickable: false),
            Control(8, 0, (400, 400, 500, 500), "android.widget.CheckBox", "Keep me signed in", checkable: true, labeledBy: [7]),
            Control(9, 0, (0, 500, 1080, 600), Button, "Name", text: "Name (required)"),
            Control(10, 0, (0, 600, 100, 700), Button, "Bold", text: "B"),
            Control(11, 0, (0, 700, 1080, 800), Button, "Notifications"),
            Control(12, 0, (0, 800, 1080, 1000), "android.view.View", "Card"),
            Control(13, 12, (10, 810, 300, 990), Button, text: "Delete"),
            Control(14, 0, (0, 1000, 1080, 1100), "android.widget.EditText", "Email", text: "bob@example.com", editable: true),
            Control(15, 0, (0, 1100, 1080, 1200), Button, "Sleep Goals"),
            Control(16, 15, (10, 1110, 500, 1190), "android.view.View", "Sleep Goals", clickable: false),
            Control(17, 0, (0, 1200, 1080, 1300), Button, "Sleep Goals"),
        ]);
        var items = window.Items;
        PhoneTextLine Ocr(PhoneCheckItem? owner, PhoneCheckItem container, string words, int top) =>
            new(owner, container, owner is null ? PhoneTextPlace.FoundByOcr : PhoneTextPlace.AppTextPlacedByOcr, owner?.Node.ContentDescription,
                words, new PhoneBounds(20, top, 400, top + 40), null, 40);
        return (window,
        [
            Ocr(items[2], items[2], "Search", 30),
            Ocr(items[5], items[5], "Settings", 230),
            Ocr(items[6], items[6], "Privacy Data", 330),
            Ocr(items[11], items[11], "Notifcations", 730),
            Ocr(items[16], items[16], "Sleep Goals", 1130),
            Ocr(null, items[17], "Sleep Goals Today", 1230),
        ]);
    }

    [Fact]
    public void LabelInName_FindsEachDiscreteIssue_FromTheTreeAndFromOcr()
    {
        var (window, lines) = Labelled();
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var at = visit.SettledSnapshots[0];
        var measured = PhoneContrastRun.Of(
        [
            new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Frame, at.MonotonicNanoseconds + 250_000_000, "p.png", "p.png"),
                "Windows OCR, en-US", lines, null, window),
        ]);

        var run = PhoneLabelInNameRun.Run(measured, archive.PhoneScreens);

        // Checked: 1, 3, 5, 6, 8, 9, 11, 15 and 17. Not read: "B" is too
        // short, the card's only text is its button's, and a field's text
        // is what was typed.
        Assert.Equal((9, 4, 3), (run.Controls, run.Passed, run.Unread));
        Assert.True(run.UsedOcr);
        Assert.Equal(9, run.ControlsIn(visit));
        string Measured(int index, string check) =>
            Assert.Single(run.Findings, finding => finding.Identity == window.Items[index].Identity && finding.Check == check).Times[0].Measured;

        Assert.Equal(
            "its name \"Find items\" does not contain the text inside it, \"Search\": \"Search\" is not in the name; the label is the app's text, from the tree",
            Measured(3, PhoneCheckNames.LabelNotInName));
        Assert.Equal(
            "its name \"Open your settings\" contains the text inside it, \"Settings\", but does not start with it; a best practice is to have the label's text at the start of the name; " +
            "the label was read by OCR (Windows OCR, en-US), as the tree has no text for it, approximate, from the video",
            Measured(5, PhoneCheckNames.LabelNotAtStart));
        Assert.Contains(
            "the text inside it, \"Privacy Data\": its words are all in the name, but not together in this order",
            Measured(6, PhoneCheckNames.LabelNotInName), StringComparison.Ordinal);
        Assert.StartsWith(
            "its name \"Keep me signed in\" does not contain the text of the item it is labelled by, \"Remember me\": \"Remember\" is not in the name",
            Measured(8, PhoneCheckNames.LabelNotInName), StringComparison.Ordinal);
        Assert.Contains("\"Today\" is not in the name", Measured(17, PhoneCheckNames.LabelNotInName), StringComparison.Ordinal);
        Assert.Equal(5, run.Findings.Count);
        Assert.All(run.Findings, finding => Assert.Equal(
            finding.Check == PhoneCheckNames.LabelNotInName ? PhoneCheckLevel.Error : PhoneCheckLevel.Warning, finding.Level));

        // Errors first, then the warning.
        Assert.Equal(
            [PhoneCheckNames.LabelNotInName, PhoneCheckNames.LabelNotInName, PhoneCheckNames.LabelNotInName, PhoneCheckNames.LabelNotInName, PhoneCheckNames.LabelNotAtStart],
            run.Findings.Select(finding => finding.Check));
        Assert.Equal(window.Items[5].Bounds, run.Findings[4].Times[0].Bounds);
        Assert.Equal("Label not in name (WCAG 2.5.3)", PhoneCheckNames.Title(PhoneCheckNames.LabelNotInName));
    }

    [Fact]
    public void LabelInName_MergesAControlsFindingsInAVisit_FromTwoScreenshots()
    {
        var (window, lines) = Labelled();
        var archive = Archive(out _);
        var visit = Assert.Single(archive.PhoneScreens.Visits);
        PhoneContrastSnapshot Snapshot(int settled)
        {
            var at = visit.SettledSnapshots[settled];
            return new PhoneContrastSnapshot(
                visit, at, archive.PhoneAccessibility.TakenBy(at, visit.WindowId)!,
                new PhoneContrastPicture(PhonePictureKind.Screenshot, at.MonotonicNanoseconds + 250_000_000, "p.png", "p.png"),
                "Windows OCR, en-US", lines, null, window);
        }

        var run = PhoneLabelInNameRun.Run(PhoneContrastRun.Of([Snapshot(0), Snapshot(1)]), archive.PhoneScreens);

        Assert.Equal((18, 8, 6), (run.Controls, run.Passed, run.Unread));
        Assert.Equal(5, run.Findings.Count);
        Assert.All(run.Findings, finding => Assert.Equal(2, finding.Times.Count));
        var settings = Assert.Single(run.Findings, finding => finding.Check == PhoneCheckNames.LabelNotAtStart);
        Assert.EndsWith(
            "the label was read by OCR (Windows OCR, en-US), as the tree has no text for it, from the phone's screenshot",
            settings.Times[1].Measured, StringComparison.Ordinal);
        Assert.Equal(visit.SettledSnapshots[1].MonotonicNanoseconds + 250_000_000, settings.Times[1].Time);
    }

    [Theory]
    [InlineData("Search", "Search for items", false, 0)]
    [InlineData("Settings", "Open your settings", false, 2)]
    [InlineData("First Name:", "first name", false, 0)]
    [InlineData("Next\u2026", "Next", false, 0)]
    [InlineData("Name (required)", "Name", false, 0)]
    [InlineData("good night's sleep", "Steps to take for a good night\u2019s sleep.", false, 5)]
    [InlineData("Privacy & Data", "Privacy Data", false, 0)]
    [InlineData("Notifcations", "Notifications", true, 0)]
    [InlineData("Notifcations", "Notifications", false, null)]
    [InlineData("Bob Dodd", "BobDodd", true, 0)]
    [InlineData("Search", "Find items", false, null)]
    [InlineData("Data Privacy", "Privacy Data", false, null)]
    [InlineData("Sleep Goals", "Sleep your Goals", false, null)]
    public void LabelInName_Match_IgnoresCaseAndPunctuation_AndNeedsTheWordsTogetherInOrder(string label, string name, bool ocr, int? expected)
    {
        Assert.Equal(expected, PhoneLabelInNameRun.Match(new PhoneLabelInNameRun.Label(label, ocr, !ocr, false), name));
    }

    // Step 6: two screens of one app with the same window title, each with
    // its own fixed text, as in the client's app, where every screen's
    // window title was "Nova SleepCare" (measured). Each visit's screen
    // gets its own error, apart from the screens' other findings.
    [Fact]
    public void Run_ScreensSharingATitle_AreEachAnError_ApartFromNoHeading()
    {
        static JsonNode Screen(string text) =>
            JsonSerializer.SerializeToNode(
                new AndroidTreeNode[]
                {
                    new()
                    {
                        Index = 0, WindowId = 512, PackageName = Settings, ClassName = "android.widget.FrameLayout",
                        Bounds = new AndroidNodeBounds(0, 0, 1080, 2404), Checked = "unchecked",
                        ImportantForAccessibility = true, VisibleToUser = true,
                    },
                    new()
                    {
                        Index = 1, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "android.widget.TextView",
                        Text = text, Bounds = new AndroidNodeBounds(100, 60, 600, 130), Checked = "unchecked",
                        ImportantForAccessibility = true, VisibleToUser = true,
                    },
                },
                JsonSerializerOptions.Web)!;
        var archive = ArchiveOf(
        [
            (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
                new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
            (At(1), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(1, Screen("Welcome"))),
            (At(4), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(2, Screen("Sleep Diary"))),
        ], out var recording);
        Assert.Equal(2, archive.PhoneScreens.Screens.Count);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording, cancellationToken: TestContext.Current.CancellationToken);

        var shared = run.Findings.Where(finding => finding.Check == PhoneCheckNames.TitleShared).ToList();
        Assert.Equal(2, shared.Count);
        Assert.Equal(archive.PhoneScreens.Visits, shared.Select(finding => finding.Visit));
        Assert.All(shared, finding => Assert.Equal(PhoneCheckLevel.Error, finding.Level));
        Assert.Equal(
            "its window title 'Settings' is also the title of 1 other screen checked in this recording: 'Sleep Diary'",
            shared[0].Times[0].Measured);
        Assert.EndsWith("'Welcome'", shared[1].Times[0].Measured, StringComparison.Ordinal);
        Assert.Equal("window:512/0", shared[0].Times[0].TreeKey);

        // No heading is its own finding, one for each visit.
        Assert.Equal(2, run.Findings.Count(finding => finding.Check == PhoneCheckNames.NoHeading));
        Assert.DoesNotContain(run.Findings, finding => finding.Check == PhoneCheckNames.TitleMissing);
        Assert.Equal("Screen title shared with other screens (WCAG2ICT 2.4.2)", PhoneCheckNames.Title(PhoneCheckNames.TitleShared));
    }

    // Step 12, status messages (WCAG 4.1.3): Settings' page, with a
    // "Saving" text shown at 2 seconds and gone at 2.5, as a status message
    // is, in a live region when live; with hiddenBefore, the page's items
    // taken at 1 second already hold it, not shown.
    // A finger down at a time and up 50 ms later.
    private static IEnumerable<(long Time, string Channel, string EventType, object Payload)> Touch(double at)
    {
        static AndroidTouchReport Report(string action) =>
            new("/dev/input/event1", 0, 0, 0, 1080, 2404, [new AndroidTouchPointer(0, 1, action, 1, 2, 540, 1200, 540, 1200, 40, 300, 200)]);
        yield return (At(at), AndroidTouchRecords.TouchChannel, AndroidTouchRecords.TouchEventType, Report(AndroidTouchRecords.DownAction));
        yield return (At(at + 0.05), AndroidTouchRecords.TouchChannel, AndroidTouchRecords.TouchEventType, Report(AndroidTouchRecords.UpAction));
    }

    // The phone's settings read at the start, with Time to take action for
    // messages as given (null: Default, not set).
    private static (long Time, string Channel, string EventType, object Payload) SettingsRead(string? timeout) =>
        (At(0.1), AndroidSettingsRecords.Channel, AndroidSettingsRecords.SnapshotEventType,
            new AndroidSettingsSnapshot("58191FDCQ00ABZ", AndroidSettingsRecords.StartReason,
                new Dictionary<string, string?> { [PhoneStatusChecks.MessageTimeoutSetting] = timeout }, [], At(0.05), At(0.1)));

    // With newContent, a "Saved" text is shown as "Saving" goes; with
    // settings, the phone's settings are recorded, Time to take action for
    // messages being timeout.
    private SessionPlaybackArchive Brief(out FakeRecords recording, bool live = false, bool hiddenBefore = false,
        double? touchAt = 0.2, bool announced = false, bool newContent = false, bool settings = false, string? timeout = null)
    {
        JsonNode Page(bool saving, bool shown)
        {
            var items = Items(0).AsArray();
            if (saving)
            {
                items.Add(JsonSerializer.SerializeToNode(
                    new AndroidTreeNode
                    {
                        Index = 3, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "android.widget.TextView",
                        Text = "Saving", Bounds = new AndroidNodeBounds(100, 400, 600, 470), Checked = "unchecked",
                        ImportantForAccessibility = true, VisibleToUser = shown, LiveRegion = live ? 1 : 0,
                    },
                    JsonSerializerOptions.Web));
            }

            return items;
        }

        JsonNode Saved()
        {
            var items = Page(false, false).AsArray();
            items.Add(JsonSerializer.SerializeToNode(
                new AndroidTreeNode
                {
                    Index = 3, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "android.widget.TextView",
                    Text = "Saved", Bounds = new AndroidNodeBounds(100, 600, 600, 670), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true,
                },
                JsonSerializerOptions.Web));
            return items;
        }

        List<(long Time, string Channel, string EventType, object Payload)> records =
        [
            (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
                new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
            (At(1), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(1, Page(hiddenBefore, false))),
            (At(2), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(2, Page(true, true))),
            (At(2.5), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(3, newContent ? Saved() : Page(false, false))),
            (At(6), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(4, null)),
        ];
        if (touchAt is { } touch)
        {
            records.AddRange(Touch(touch));
        }

        if (settings)
        {
            records.Add(SettingsRead(timeout));
        }

        if (announced)
        {
            records.Add((At(2.1), AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.EventType, new
            {
                eventType = PhoneAnnouncementTimeline.AnnouncementEventType, eventTypeName = "TYPE_ANNOUNCEMENT",
                packageName = Settings, className = "android.widget.TextView", windowId = 512, text = new[] { "Saving" },
            }));
        }

        return ArchiveOf([.. records.OrderBy(record => record.Time)], out recording);
    }

    [Fact]
    public void Status_TextGoneByItself_IsAnError_AndAnAppWithNoLiveRegionIsAWarning()
    {
        var archive = Brief(out var recording);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            cancellationToken: TestContext.Current.CancellationToken, touches: archive.PhoneTouches, announcements: archive.PhoneAnnouncements);

        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var finding = Assert.Single(run.Findings, finding => finding.Check == PhoneCheckNames.StatusNotAnnounced);
        Assert.Equal((PhoneCheckLevel.Error, visit), (finding.Level, finding.Visit));
        var seen = Assert.Single(finding.Times);
        Assert.Equal((At(2), At(2), "window:512/0/2"), (seen.Time, seen.TakenBy.MonotonicNanoseconds, seen.TreeKey));
        Assert.Equal(
            "Error: TextView \"Saving\", it was shown and went away by itself 0.50 seconds later, with no touch between, as a status message does; " +
            "it is not in a live region, no announcement was made and no titled pane appeared with it, so a screen reader is not told of it; seen once, at 00:00:02.000",
            finding.Text);
        Assert.Equal((true, 1, 0, 0), (run.StatusRun, run.BriefMessages, run.BriefExposed, run.LiveRegionItems));

        // The whole app, once, shown at its first visit.
        var app = Assert.Single(run.AppFindings);
        Assert.Equal((PhoneCheckNames.NoLiveRegion, PhoneCheckLevel.Warning, visit), (app.Check, app.Level, app.Visit));
        Assert.StartsWith("in its 1 visit in this recording, no item of the app was in a live region, and it made no announcement and showed no toast; look for status messages", app.Times[0].Measured, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Findings, finding => finding.Check == PhoneCheckNames.NoLiveRegion);
        Assert.Equal("Status message not announced (WCAG 4.1.3)", PhoneCheckNames.Title(PhoneCheckNames.StatusNotAnnounced));
        Assert.Equal("No live region in the app (WCAG 4.1.3)", PhoneCheckNames.Title(PhoneCheckNames.NoLiveRegion));
    }

    // Not an error: in a live region, or announced, which also clears the
    // app's warning; taken away by a touch; already in the window, hidden;
    // and with no touches recorded the brief message check does not run.
    [Theory]
    [InlineData("live", true, 1, 1, false)]
    [InlineData("announced", true, 1, 1, false)]
    [InlineData("touched", true, 0, 0, true)]
    [InlineData("hidden before", true, 0, 0, true)]
    [InlineData("no touches", false, 0, 0, true)]
    public void Status_ExposedTouchedOrAlreadyThere_IsNoError(string variant, bool run_, int messages, int exposed, bool appWarning)
    {
        var archive = Brief(out var recording,
            live: variant == "live",
            hiddenBefore: variant == "hidden before",
            touchAt: variant switch { "touched" => 2.2, "no touches" => null, _ => 0.2 },
            announced: variant == "announced");

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            cancellationToken: TestContext.Current.CancellationToken, touches: archive.PhoneTouches, announcements: archive.PhoneAnnouncements);

        Assert.DoesNotContain(run.Findings, finding => finding.Check == PhoneCheckNames.StatusNotAnnounced);
        Assert.Equal((run_, messages, exposed), (run.StatusRun, run.BriefMessages, run.BriefExposed));
        Assert.Equal(appWarning, run.AppFindings.Count == 1);
        Assert.Equal(variant == "announced" ? 1 : 0, run.Announcements);
        Assert.Equal(variant == "live" ? 1 : 0, run.LiveRegionItems);
    }

    // Step 13, timing (WCAG 2.2.1): "Saving" goes by itself after half a
    // second with nothing new shown, as a message on a timer does: a
    // warning when Time to take action is Default or not recorded, as the
    // app may keep it up longer when it is set, and an error when it was
    // set and the app did not keep it up for that long.
    [Theory]
    [InlineData(false, null, PhoneCheckLevel.Warning, "; the phone's Time to take action was not recorded, so whether the app keeps it up longer")]
    [InlineData(true, null, PhoneCheckLevel.Warning, "; the phone's Time to take action was Default, so whether the app keeps it up longer")]
    [InlineData(true, "0", PhoneCheckLevel.Warning, "; the phone's Time to take action was Default, so whether the app keeps it up longer")]
    [InlineData(true, "10000", PhoneCheckLevel.Error, ", though the phone's Time to take action was 10 seconds, so the app did not keep it up for the time the user chose")]
    [InlineData(true, "60000", PhoneCheckLevel.Error, ", though the phone's Time to take action was 1 minute, so the app did not keep it up for the time the user chose")]
    public void Timing_TextGoneByItself_IsAWarningAtDefault_AndAnErrorWhenTheAppIgnoredTimeToTakeAction(
        bool settings, string? timeout, PhoneCheckLevel level, string said)
    {
        var archive = Brief(out var recording, settings: settings, timeout: timeout);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            cancellationToken: TestContext.Current.CancellationToken, touches: archive.PhoneTouches, announcements: archive.PhoneAnnouncements,
            settings: archive.PhoneSettings);

        var visit = Assert.Single(archive.PhoneScreens.Visits);
        var finding = Assert.Single(run.Findings, finding => finding.Check == PhoneCheckNames.TextGoneByItself);
        Assert.Equal((level, visit), (finding.Level, finding.Visit));
        var seen = Assert.Single(finding.Times);
        Assert.Equal((At(2), "window:512/0/2"), (seen.Time, seen.TreeKey));
        Assert.StartsWith(
            "it was shown and went away by itself 0.50 seconds later, with no touch between and nothing new shown as it went, as a message on a timer does" + said,
            seen.Measured, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Findings, finding => finding.Check == PhoneCheckNames.TextGoneAsContentChanged);
        Assert.Equal((1, 1, settings), (run.TimedMessages, run.TimedAlone, run.SettingsRecorded));
        Assert.Equal(timeout is "10000" or "60000" ? long.Parse(timeout, CultureInfo.InvariantCulture) : null, run.TimeoutMilliseconds);
        Assert.Equal("Text gone by itself (WCAG 2.2.1)", PhoneCheckNames.Title(PhoneCheckNames.TextGoneByItself));
    }

    // As "Saving" goes, "Saved" is shown: it may have gone as the saving
    // finished, but it went without warning and the recording cannot show
    // that it was not on a timer, so a warning, even with Time to take
    // action set.
    [Theory]
    [InlineData(null)]
    [InlineData("10000")]
    public void Timing_TextGoneAsNewContentAppeared_IsAWarning(string? timeout)
    {
        var archive = Brief(out var recording, newContent: true, settings: true, timeout: timeout);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            cancellationToken: TestContext.Current.CancellationToken, touches: archive.PhoneTouches, announcements: archive.PhoneAnnouncements,
            settings: archive.PhoneSettings);

        var finding = Assert.Single(run.Findings, finding => finding.Check == PhoneCheckNames.TextGoneAsContentChanged);
        Assert.Equal(PhoneCheckLevel.Warning, finding.Level);
        Assert.Equal(
            "it was shown and went away 0.50 seconds later, with no touch between, as new content appeared, as when loading finishes; " +
            "it went without warning, and the recording cannot show that it was not on a timer",
            Assert.Single(finding.Times).Measured);
        Assert.DoesNotContain(run.Findings, finding => finding.Check == PhoneCheckNames.TextGoneByItself);
        Assert.Equal((1, 0), (run.TimedMessages, run.TimedAlone));
        Assert.Equal("Text gone as new content appeared (WCAG 2.2.1)", PhoneCheckNames.Title(PhoneCheckNames.TextGoneAsContentChanged));
    }

    // Taken away by a touch, or with no touches recorded: not looked at.
    [Theory]
    [InlineData(2.2)]
    [InlineData(null)]
    public void Timing_TextTakenAwayByATouch_OrWithNoTouches_IsNotATextGone(double? touchAt)
    {
        var archive = Brief(out var recording, touchAt: touchAt, settings: true);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            cancellationToken: TestContext.Current.CancellationToken, touches: archive.PhoneTouches, announcements: archive.PhoneAnnouncements,
            settings: archive.PhoneSettings);

        Assert.DoesNotContain(run.Findings, finding => finding.Check is PhoneCheckNames.TextGoneByItself or PhoneCheckNames.TextGoneAsContentChanged);
        Assert.Equal(0, run.TimedMessages);
    }

    // Settings' page with a clock reading "10:00" at 2 seconds, "10:01" at 4
    // and "10:02" at 6; a touch at touchAt.
    private SessionPlaybackArchive Clock(out FakeRecords recording, double touchAt)
    {
        JsonNode Page(string time)
        {
            var items = Items(0).AsArray();
            items.Add(JsonSerializer.SerializeToNode(
                new AndroidTreeNode
                {
                    Index = 3, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "android.widget.TextView",
                    Text = time, Bounds = new AndroidNodeBounds(100, 400, 600, 470), Checked = "unchecked",
                    ImportantForAccessibility = true, VisibleToUser = true,
                },
                JsonSerializerOptions.Web));
            return items;
        }

        List<(long Time, string Channel, string EventType, object Payload)> records =
        [
            (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
                new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
            (At(2), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(1, Page("10:00"))),
            (At(4), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(2, Page("10:01"))),
            (At(6), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(3, Page("10:02"))),
            (At(8), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(4, null)),
            .. Touch(touchAt),
        ];
        return ArchiveOf([.. records.OrderBy(record => record.Time)], out recording);
    }

    // Step 13, timing (WCAG 2.2.2): text changing in place twice with no
    // touch is auto-updating, a warning, and its old text going is not also
    // a text gone; a touch in the second before a change makes it the
    // touch's, leaving one change, not enough.
    [Theory]
    [InlineData(0.2, 2)]
    [InlineData(5.5, 0)]
    public void Timing_TextChangingInPlaceWithNoTouch_IsAutoUpdating(double touchAt, int times)
    {
        var archive = Clock(out var recording, touchAt);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            cancellationToken: TestContext.Current.CancellationToken, touches: archive.PhoneTouches, announcements: archive.PhoneAnnouncements);

        var found = run.Findings.Where(finding => finding.Check == PhoneCheckNames.TextUpdatingByItself).ToList();
        Assert.Equal(times == 0 ? 0 : 1, found.Count);
        Assert.Equal(1, run.UpdatedItems);
        if (times > 0)
        {
            var finding = found[0];
            Assert.Equal(PhoneCheckLevel.Warning, finding.Level);
            Assert.Equal([At(4), At(6)], finding.Times.Select(time => time.Time));
            Assert.Equal(
                "its text changed 2 times with no touch, here from \"10:00\" to \"10:01\", as auto-updating content does; " +
                "look for a way to pause, stop or hide it, or to set how often it updates, unless the updating is essential",
                finding.Times[0].Measured);
            Assert.Equal("window:512/0/2", finding.Times[0].TreeKey);
            Assert.DoesNotContain(run.Findings, finding => finding.Check is PhoneCheckNames.TextGoneByItself or PhoneCheckNames.TextGoneAsContentChanged);
        }

        Assert.Equal("Text updating by itself (WCAG 2.2.2)", PhoneCheckNames.Title(PhoneCheckNames.TextUpdatingByItself));
    }

    // Step 13, timing (WCAG 2.2.2), from the video: frames a tenth of the
    // screen's size every 0.1 seconds from 1.5 to 8 seconds, a box of each
    // changing colour from one to the next, in the page, or in the status
    // bar; a touch at 0.2 seconds, and another at extraTouch.
    [Theory]
    [InlineData("page", null, true)]
    [InlineData("status bar", null, false)]
    [InlineData("page", 4.0, false)]
    public void Timing_PictureChangingForMoreThanFiveSecondsWithNoTouch_IsMoving(string where, double? extraTouch, bool found)
    {
        List<(long Time, string Channel, string EventType, object Payload)> records =
        [
            (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
                new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
            (At(1), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(1, Items(0), statusBarBottom: 100)),
            (At(9), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(2, null, statusBarBottom: 100)),
            .. Touch(0.2),
            .. extraTouch is { } touch ? Touch(touch) : [],
        ];
        var archive = ArchiveOf([.. records.OrderBy(record => record.Time)], out var recording);
        var frames = Enumerable.Range(0, 66)
            .Select(index => new SessionVideoFrame(At(1.5 + index / 10.0), $"f{index}.png", $"f{index}.png", 108, 240))
            .ToList();
        var (top, bottom) = where == "page" ? (120, 160) : (0, 8);
        PhonePixels Load(string path)
        {
            var odd = int.Parse(path.AsSpan(1, path.Length - 5), CultureInfo.InvariantCulture) % 2 == 1;
            var rgb = new uint[108 * 240];
            for (var y = top; y < bottom; y++)
            {
                for (var x = 40; x < 80; x++)
                {
                    rgb[y * 108 + x] = odd ? 0xFFFFFFu : 0x000000u;
                }
            }

            return new PhonePixels(108, 240, rgb);
        }

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            PhoneSnapshotPictures.OfFrames(frames, archive.PhoneAccessibility), TestContext.Current.CancellationToken,
            touches: archive.PhoneTouches, announcements: archive.PhoneAnnouncements, loadPicture: Load);

        Assert.Empty(run.Problems);
        Assert.True(run.MotionRun);
        var moving = run.Findings.Where(finding => finding.Check == PhoneCheckNames.MovingByItself).ToList();
        Assert.Equal(found ? 1 : 0, moving.Count);
        Assert.Equal(found ? 1 : 0, run.MovingRuns);
        if (found)
        {
            var finding = moving[0];
            Assert.Equal((PhoneCheckLevel.Warning, "window:512/0"), (finding.Level, finding.Times[0].TreeKey));
            var seen = Assert.Single(finding.Times);
            Assert.Equal(At(1.5), seen.Time);
            Assert.Equal(new PhoneBounds(400, 1202, 761, 1563), seen.Bounds);
            Assert.StartsWith(
                "from the video, so approximate: the picture in the outlined box kept changing for 6.5 seconds with no touch, ",
                seen.Measured, StringComparison.Ordinal);
        }

        Assert.Equal("Moving by itself, from the video (WCAG 2.2.2)", PhoneCheckNames.Title(PhoneCheckNames.MovingByItself));
    }

    // Step 14, changes of context (WCAG 3.2.1 and 3.2.2): Settings' page
    // with its "Save" button, a "Wi-Fi" switch and a "Name" field, and at
    // 3 seconds a dialog, "Allow?", over it, taking the focus (or not,
    // as a list of suggestions does not), or with screen, the page's title
    // changed to "Wi-Fi", a new screen.
    private static JsonNode ContextItems()
    {
        var items = Items(0).AsArray();
        items.Add(JsonSerializer.SerializeToNode(
            new AndroidTreeNode
            {
                Index = 3, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "android.widget.Switch",
                Text = "Wi-Fi", Bounds = new AndroidNodeBounds(700, 500, 900, 600), Checked = "unchecked",
                ImportantForAccessibility = true, VisibleToUser = true, Enabled = true, Clickable = true, Focusable = true, Checkable = true,
            },
            JsonSerializerOptions.Web));
        items.Add(JsonSerializer.SerializeToNode(
            new AndroidTreeNode
            {
                Index = 4, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "android.widget.EditText",
                Text = "Name", Bounds = new AndroidNodeBounds(100, 800, 1000, 900), Checked = "unchecked",
                ImportantForAccessibility = true, VisibleToUser = true, Enabled = true, Clickable = true, Focusable = true, Editable = true,
            },
            JsonSerializerOptions.Web));

        // A Settings row, clicked to change the switch inside it, which
        // is not clickable itself.
        items.Add(JsonSerializer.SerializeToNode(
            new AndroidTreeNode
            {
                Index = 5, Parent = 0, Depth = 1, WindowId = 512, PackageName = Settings, ClassName = "android.widget.LinearLayout",
                Bounds = new AndroidNodeBounds(0, 1900, 1080, 2100), Checked = "unchecked",
                ImportantForAccessibility = true, VisibleToUser = true, Enabled = true, Clickable = true, Focusable = true,
            },
            JsonSerializerOptions.Web));
        items.Add(JsonSerializer.SerializeToNode(
            new AndroidTreeNode
            {
                Index = 6, Parent = 5, Depth = 2, WindowId = 512, PackageName = Settings, ClassName = "android.widget.Switch",
                Bounds = new AndroidNodeBounds(900, 1950, 1000, 2050), Checked = "unchecked",
                ImportantForAccessibility = true, VisibleToUser = true, Enabled = true, Checkable = true,
            },
            JsonSerializerOptions.Web));
        return items;
    }

    // With alone, the dialog is listed, untitled, without the window under
    // it, as Android listed Settings' Quick Settings shortcut dialog in test
    // 48's recording.
    private static AndroidAccessibilityTree ContextTree(long index, bool dialog, bool dialogFocused = true, string? title = null, bool alone = false)
    {
        var tree = JsonNode.Parse(AndroidAccessibilityTreeTests.TreeLine(
            index: index,
            everyWindow: index == 1,
            reason: index == 1 ? "requested" : "content-change"))!.AsObject();
        var windows = tree["windows"]!.AsArray();
        windows[0]!["items"] = ContextItems();
        windows[1]!["bounds"]!["bottom"] = 100;
        if (title is not null)
        {
            windows[0]!["title"] = title;
        }

        if (dialog)
        {
            var over = windows[0]!.DeepClone().AsObject();
            over["id"] = 520;
            over["title"] = "Allow?";
            over["layer"] = 3;
            over["bounds"] = new JsonObject { ["left"] = 100, ["top"] = 1000, ["right"] = 980, ["bottom"] = 1600 };
            over["active"] = dialogFocused;
            over["focused"] = dialogFocused;
            over["items"] = JsonSerializer.SerializeToNode(
                new AndroidTreeNode[]
                {
                    new()
                    {
                        Index = 0, WindowId = 520, PackageName = Settings, ClassName = "android.widget.FrameLayout",
                        Bounds = new AndroidNodeBounds(100, 1000, 980, 1600), Checked = "unchecked",
                        ImportantForAccessibility = true, VisibleToUser = true,
                    },
                },
                JsonSerializerOptions.Web);
            windows[0]!["active"] = !dialogFocused;
            windows[0]!["focused"] = !dialogFocused;
            windows.Add(over);
            if (alone)
            {
                // Untitled, as the Quick Settings shortcut dialog was.
                over["title"] = null;
                windows.RemoveAt(0);
            }
        }

        var (read, problem) = CompanionProtocol.ReadTree(tree.ToJsonString());
        Assert.Null(problem);
        return read!;
    }

    // A finger down at a place and up 50 ms later.
    private static IEnumerable<(long Time, string Channel, string EventType, object Payload)> TouchAt(double at, int x, int y)
    {
        AndroidTouchReport Report(string action) =>
            new("/dev/input/event1", 0, 0, 0, 1080, 2404, [new AndroidTouchPointer(0, 1, action, 1, 2, x, y, x, y, 40, 300, 200)]);
        yield return (At(at), AndroidTouchRecords.TouchChannel, AndroidTouchRecords.TouchEventType, Report(AndroidTouchRecords.DownAction));
        yield return (At(at + 0.05), AndroidTouchRecords.TouchChannel, AndroidTouchRecords.TouchEventType, Report(AndroidTouchRecords.UpAction));
    }

    // An accessibility event from one of the page's items, by its bounds.
    private static (long Time, string Channel, string EventType, object Payload) InputEvent(
        double at, int type, string name, string className, int left, int top, int right, int bottom, string text) =>
        (At(at), AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.EventType, new
        {
            eventType = type, eventTypeName = name, packageName = Settings, className, windowId = 512, text = new[] { text },
            source = new
            {
                windowId = 512, className, packageName = Settings, text,
                bounds = new { left, top, right, bottom },
                checkable = className.EndsWith("Switch", StringComparison.Ordinal),
                editable = className.EndsWith("EditText", StringComparison.Ordinal),
            },
        });

    private static (long Time, string Channel, string EventType, object Payload) Key(double at, string key) =>
        (At(at), AndroidTouchRecords.KeyChannel, AndroidTouchRecords.KeyEventType,
            new AndroidHardwareKey("/dev/input/event3", "Keyboard", key, "down", 0, At(at)));

    private PhoneCheckRun ContextRun(string action, out SessionPlaybackArchive archive, string change = "dialog")
    {
        List<(long Time, string Channel, string EventType, object Payload)> records =
        [
            (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
                new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
            (At(1), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, ContextTree(1, false)),
            (At(3), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, change switch
            {
                "screen" => ContextTree(2, false, title: "Wi-Fi"),
                "suggestions" => ContextTree(2, true, dialogFocused: false),
                "alone" => ContextTree(2, true, alone: true),
                _ => ContextTree(2, true),
            }),
            (At(6), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(3, null, statusBarBottom: 100)),
        ];
        if (action != "no touches")
        {
            records.AddRange(TouchAt(0.2, 540, 1200));
        }

        if (change == "alone")
        {
            // The dialog's window-state event, which marks it a dialog.
            records.Add((At(2.95), AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.EventType, new
            {
                eventType = PhoneScreenTimeline.WindowStateChangedEventType, eventTypeName = "TYPE_WINDOW_STATE_CHANGED",
                packageName = Settings, className = "androidx.appcompat.app.AlertDialog", windowId = 520, text = new[] { "Quick Settings shortcut", "Got it" },
            }));
        }

        const int ScreenReaderTouch = PhoneInputTimeline.TouchInteractionStartEventType;
        switch (action)
        {
            case "switch":
            case "suggestions":
                records.AddRange(TouchAt(2.5, 800, 550));
                break;
            case "late switch":
                records.AddRange(TouchAt(1.2, 800, 550));
                break;
            case "switch then save":
                records.AddRange(TouchAt(2.5, 800, 550));
                records.AddRange(TouchAt(2.8, 150, 250));
                break;
            case "field":
                records.AddRange(TouchAt(2.5, 500, 850));
                break;
            case "save":
                records.AddRange(TouchAt(2.5, 150, 250));
                break;
            case "save clicked":
                records.AddRange(TouchAt(2.5, 150, 250));
                records.Add(InputEvent(2.56, PhoneInputTimeline.ClickedEventType, "TYPE_VIEW_CLICKED", "android.widget.Button", 100, 200, 198, 298, "Save"));
                break;
            case "switch clicked":
                records.AddRange(TouchAt(2.5, 800, 550));
                records.Add(InputEvent(2.56, PhoneInputTimeline.ClickedEventType, "TYPE_VIEW_CLICKED", "android.widget.Switch", 700, 500, 900, 600, "Wi-Fi"));
                break;
            case "row":
                records.AddRange(TouchAt(2.5, 300, 2000));
                break;
            case "tab":
                records.Add(Key(2.55, "KEY_TAB"));
                records.Add(InputEvent(2.6, PhoneInputTimeline.FocusedEventType, "TYPE_VIEW_FOCUSED", "android.widget.EditText", 100, 800, 1000, 900, "Name"));
                break;
            case "volume":
                records.Add(Key(2.55, "KEY_VOLUMEDOWN"));
                break;
            case "typed":
            case "no touches":
                records.Add(InputEvent(2.55, PhoneInputTimeline.TextChangedEventType, "TYPE_VIEW_TEXT_CHANGED", "android.widget.EditText", 100, 800, 1000, 900, "Nam"));
                break;
            case "talkback":
            case "talkback double tap":
                records.AddRange(TouchAt(2.3, 540, 1200));
                records.Add((At(2.301), AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.EventType,
                    new { eventType = ScreenReaderTouch, eventTypeName = "TYPE_TOUCH_INTERACTION_START", windowId = -1 }));
                records.Add(InputEvent(2.55, PhoneAccessibilityTimeline.FocusedEventType, "TYPE_VIEW_ACCESSIBILITY_FOCUSED", "android.widget.Button", 100, 200, 198, 298, "Save"));
                if (action == "talkback double tap")
                {
                    records.AddRange(TouchAt(2.7, 540, 1200));
                    records.Add((At(2.701), AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.EventType,
                        new { eventType = ScreenReaderTouch, eventTypeName = "TYPE_TOUCH_INTERACTION_START", windowId = -1 }));
                }

                break;
        }

        archive = ArchiveOf([.. records.OrderBy(record => record.Time)], out var recording);
        return PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            cancellationToken: TestContext.Current.CancellationToken, touches: archive.PhoneTouches, announcements: archive.PhoneAnnouncements,
            inputs: archive.PhoneInputs);
    }

    // A change of setting followed by a change of context, with no other
    // touch or key between: a warning, as the user may have been told.
    [Theory]
    [InlineData("switch", "dialog", "window:512/0/2", "a tap changed its setting (it was not checked), and 0.45 seconds later, with no further touch or key, the window 'Allow?' appeared")]
    [InlineData("switch clicked", "dialog", "window:512/0/2", "a tap changed its setting (it was not checked), and 0.45 seconds later, with no further touch or key, the window 'Allow?' appeared")]
    [InlineData("row", "dialog", "window:512/0/4", "a tap changed the setting of its Switch (it was not checked), and 0.45 seconds later, with no further touch or key, the window 'Allow?' appeared")]
    [InlineData("switch", "alone", "window:512/0/2", "a tap changed its setting (it was not checked), and 0.45 seconds later, with no further touch or key, an untitled window appeared and took the focus")]
    [InlineData("typed", "dialog", "window:512/0/3", "text was typed in it, and 0.45 seconds later, with no further touch or key, the window 'Allow?' appeared")]
    [InlineData("switch", "screen", "window:512/0/2", "a tap changed its setting (it was not checked), and 0.45 seconds later, with no further touch or key, the screen 'Wi-Fi' opened")]
    public void Context_SettingChangedThenAChangeOfContext_IsAWarning(string action, string change, string treeKey, string said)
    {
        var run = ContextRun(action, out var archive, change);

        Assert.Empty(run.Problems);
        var finding = Assert.Single(run.Findings, finding => finding.Check.StartsWith("context-", StringComparison.Ordinal));
        Assert.Equal((PhoneCheckNames.ContextOnInput, PhoneCheckLevel.Warning, archive.PhoneScreens.Visits[0]), (finding.Check, finding.Level, finding.Visit));
        var seen = Assert.Single(finding.Times);
        Assert.Equal(treeKey, seen.TreeKey);
        Assert.Equal(
            said + ", a change of context on input, which is allowed only when the user was told of it before using the item; look for such advice",
            seen.Measured);
        Assert.Equal((true, 1, 0, 0, 1), (run.ContextRun, run.ContextChanges, run.FocusMoves, run.ScreenReaderFocusMoves, run.SettingChanges));
        Assert.Equal("Change of context on input (WCAG 3.2.2)", PhoneCheckNames.Title(PhoneCheckNames.ContextOnInput));
    }

    // Focus from the keyboard or a tap on a field, or TalkBack's focus moved
    // by a gesture, followed by a change of context with nothing activated:
    // an error, each its own check.
    [Theory]
    [InlineData("field", "context-change-on-focus", "window:512/0/3", "a tap on the field gave it the focus, and 0.45 seconds later, with nothing activated, the window 'Allow?' appeared, a change of context on focus")]
    [InlineData("tab", "context-change-on-focus", "window:512/0/3", "the Tab key moved the focus to it, and 0.45 seconds later, with nothing activated, the window 'Allow?' appeared, a change of context on focus")]
    [InlineData("talkback", "context-change-on-screen-reader-focus", "window:512/0/0", "TalkBack's focus moved to it, and 0.45 seconds later, with no double tap, touch or key, the window 'Allow?' appeared, a change of context on a screen reader's focus")]
    public void Context_FocusThenAChangeOfContext_IsAnError(string action, string check, string treeKey, string said)
    {
        var run = ContextRun(action, out _);

        var finding = Assert.Single(run.Findings, finding => finding.Check.StartsWith("context-", StringComparison.Ordinal));
        Assert.Equal((check, PhoneCheckLevel.Error), (finding.Check, finding.Level));
        var seen = Assert.Single(finding.Times);
        Assert.Equal((treeKey, said), (seen.TreeKey, seen.Measured));
        Assert.Equal(action == "talkback" ? (0, 1) : (1, 0), (run.FocusMoves, run.ScreenReaderFocusMoves));
        Assert.Equal("Change of context on focus (WCAG 3.2.1)", PhoneCheckNames.Title(PhoneCheckNames.ContextOnFocus));
        Assert.Equal("Change of context on TalkBack focus (WCAG 3.2.1)", PhoneCheckNames.Title(PhoneCheckNames.ContextOnScreenReaderFocus));
    }

    // Not a finding: a button tapped (activation); the phone's volume key;
    // the setting changed 1.8 seconds before; another touch between;
    // TalkBack's double tap between; a window that did not take the focus;
    // and with no touches recorded the checks do not run.
    [Theory]
    [InlineData("save", "dialog", true, 0, 0, 0)]
    [InlineData("save clicked", "dialog", true, 0, 0, 0)]
    [InlineData("volume", "dialog", true, 0, 0, 0)]
    [InlineData("late switch", "dialog", true, 0, 0, 1)]
    [InlineData("switch then save", "dialog", true, 0, 0, 1)]
    [InlineData("talkback double tap", "dialog", true, 0, 1, 0)]
    [InlineData("suggestions", "suggestions", true, 0, 0, 1)]
    [InlineData("no touches", "dialog", false, 0, 0, 0)]
    public void Context_ActivatedLateOrNoChange_IsNoFinding(string action, string change, bool ran, int focus, int reader, int settings)
    {
        var run = ContextRun(action, out _, change);

        Assert.DoesNotContain(run.Findings, finding => finding.Check.StartsWith("context-", StringComparison.Ordinal));
        Assert.Equal((ran, 0, focus, reader, settings), (run.ContextRun, run.ContextChanges, run.FocusMoves, run.ScreenReaderFocusMoves, run.SettingChanges));
    }

    // Step 15, orientation (WCAG 1.3.4): the phone, upright, is turned
    // sideways at 3 seconds and back at 6. With the screen staying portrait
    // and Auto-rotate on, that is a warning, at the turn; a screen that
    // turns half a second after the phone is not, nor is any turn with
    // Auto-rotate off, or one shorter than the hold time.
    [Theory]
    [InlineData(false, "1", 2, true)]
    [InlineData(true, "1", 2, false)]
    [InlineData(false, "0", 2, false)]
    [InlineData(false, "1", 5, false)]
    public void Orientation_PhoneTurnedAndScreenNot_IsRestricted(bool screenTurns, string autoRotate, int hold, bool found)
    {
        static AndroidDisplayState Display(int rotation) =>
            new(rotation == 0 ? AndroidCompanionRecords.InitialReason : AndroidCompanionRecords.ChangedReason, rotation, 1080, 2404);
        List<(long Time, string Channel, string EventType, object Payload)> records =
        [
            (At(0.1), AndroidSettingsRecords.Channel, AndroidSettingsRecords.SnapshotEventType,
                new AndroidSettingsSnapshot("58191FDCQ00ABZ", AndroidSettingsRecords.StartReason,
                    new Dictionary<string, string?> { [PhoneOrientationChecks.AutoRotateSetting] = autoRotate }, [], At(0.05), At(0.1))),
            (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType, Display(0)),
            (At(0.6), AndroidCompanionRecords.OrientationChannel, AndroidCompanionRecords.OrientationEventType,
                new AndroidPhoneOrientation("first", 0, 3)),
            (At(1), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(1, Items(0))),
            (At(3), AndroidCompanionRecords.OrientationChannel, AndroidCompanionRecords.OrientationEventType,
                new AndroidPhoneOrientation("changed", 90, 72)),
            (At(6), AndroidCompanionRecords.OrientationChannel, AndroidCompanionRecords.OrientationEventType,
                new AndroidPhoneOrientation("changed", 0, 21)),
            (At(9), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(2, null)),
            .. screenTurns
                ? new[]
                {
                    (At(3.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType, (object)Display(90)),
                    (At(6.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType, (object)Display(0)),
                }
                : [],
        ];
        var archive = ArchiveOf([.. records.OrderBy(record => record.Time)], out var recording);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            cancellationToken: TestContext.Current.CancellationToken, settings: archive.PhoneSettings,
            orientation: archive.PhoneOrientation, orientationHoldSeconds: hold);

        Assert.Empty(run.Problems);
        Assert.True(run.OrientationRun);
        Assert.Equal((2, hold), (run.OrientationTurns, run.OrientationHoldSeconds));
        var restricted = run.Findings.Where(finding => finding.Check == PhoneCheckNames.OrientationRestricted).ToList();
        Assert.Equal(found ? 1 : 0, restricted.Count);
        Assert.Equal(found ? 1 : 0, run.OrientationNotFollowed);
        if (found)
        {
            var finding = restricted[0];
            Assert.Equal(PhoneCheckLevel.Warning, finding.Level);
            var time = Assert.Single(finding.Times);
            Assert.Equal(At(3), time.Time);
            Assert.Equal("window:512/0", time.TreeKey);
            Assert.Equal(
                $"the phone was held sideways for {3.0.ToString("0.0", CultureInfo.CurrentCulture)} seconds with Auto-rotate on, and the screen stayed portrait, " +
                "as when the app restricts its display to one orientation; look for whether that orientation is essential, as for a piano or a bank cheque",
                time.Measured);
        }

        Assert.Equal("Orientation restricted (WCAG 1.3.4)", PhoneCheckNames.Title(PhoneCheckNames.OrientationRestricted));
    }

    // Without how the phone was held, from a companion app older than
    // protocol 7, the orientation check is not looked for, and says why.
    [Fact]
    public void Orientation_NotRecorded_IsNotLookedFor()
    {
        var archive = Archive(out var recording);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording,
            cancellationToken: TestContext.Current.CancellationToken, settings: archive.PhoneSettings, orientation: archive.PhoneOrientation);

        Assert.False(run.OrientationRun);
        Assert.Contains("older than protocol 7", run.OrientationNotRun, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Findings, finding => finding.Check == PhoneCheckNames.OrientationRestricted);
    }

    // The recording's records, as the player reads one from the file.
    private sealed class FakeRecords : ISessionEventRecordSource
    {
        private readonly Dictionary<long, string> _records = [];

        public void Add(long time, RecorderEvent record) =>
            _records[time] = JsonSerializer.Serialize(new { payload = record.Payload });

        public string ReadEventJson(SessionTimelineEvent item) => _records[item.MonotonicNanoseconds];
    }
}

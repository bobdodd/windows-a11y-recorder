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
            AndroidSettingsRecords.Channel],
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

    private static (PhoneCheckWindow Window, PhoneTextLine[] Lines) PicturesOfText()
    {
        var window = new PhoneCheckWindow(512, 1,
        [
            ImageNode(0, null, (0, 0, 1080, 2400), "android.widget.FrameLayout"),
            ImageNode(1, 0, (59, 324, 1022, 724), "android.widget.ImageView", "Sleep Hygiene\nSteps to take for a good night's sleep."),
            ImageNode(2, 0, (59, 724, 1022, 1124), "android.widget.ImageView"),
            ImageNode(3, 0, (100, 800, 900, 900), "android.widget.TextView", text: "Daily tips"),
            ImageNode(4, 0, (59, 1124, 1022, 1523), "android.widget.ImageButton", "Settings"),
            ImageNode(5, 0, (59, 1523, 1022, 1923), "android.widget.ImageView"),
        ]);
        var items = window.Items;
        PhoneTextLine Line(PhoneCheckItem? owner, PhoneCheckItem container, PhoneTextPlace place, string ocr, int left, int top, int right, int bottom) =>
            new(owner, container, place, owner?.Node.ContentDescription ?? owner?.Text, ocr, new PhoneBounds(left, top, right, bottom), null, bottom - top);
        return (window,
        [
            Line(items[1], items[1], PhoneTextPlace.AppTextPlacedByOcr, "Sleep Hygiene", 100, 400, 500, 442),
            Line(items[1], items[1], PhoneTextPlace.AppTextPlacedByOcr, "Steps to take for a good night's sleep.", 100, 460, 900, 489),
            Line(items[3], items[3], PhoneTextPlace.AppTextPlacedByOcr, "Daily tips", 100, 820, 400, 860),
            Line(null, items[4], PhoneTextPlace.FoundByOcr, "Q:", 500, 1300, 560, 1340),
            Line(null, items[5], PhoneTextPlace.FoundByOcr, "Sale 50%", 100, 1600, 500, 1642),
        ]);
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

    // The recording's records, as the player reads one from the file.
    private sealed class FakeRecords : ISessionEventRecordSource
    {
        private readonly Dictionary<long, string> _records = [];

        public void Add(long time, RecorderEvent record) =>
            _records[time] = JsonSerializer.Serialize(new { payload = record.Payload });

        public string ReadEventJson(SessionTimelineEvent item) => _records[item.MonotonicNanoseconds];
    }
}

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
        [AndroidAccessibilityRecords.EventsChannel, AndroidAccessibilityRecords.TreeChannel, AndroidCompanionRecords.DisplayChannel],
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
                    ImportantForAccessibility = true, VisibleToUser = true, Clickable = true, Focusable = true,
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
    private static AndroidAccessibilityTree Tree(long index, JsonNode? items)
    {
        var tree = JsonNode.Parse(AndroidAccessibilityTreeTests.TreeLine(
            index: index,
            everyWindow: index == 1,
            reason: index == 1 ? "requested" : "content-change"))!.AsObject();
        tree["windows"]![0]!["items"] = items;
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

    // The recording's records, as the player reads one from the file.
    private sealed class FakeRecords : ISessionEventRecordSource
    {
        private readonly Dictionary<long, string> _records = [];

        public void Add(long time, RecorderEvent record) =>
            _records[time] = JsonSerializer.Serialize(new { payload = record.Payload });

        public string ReadEventJson(SessionTimelineEvent item) => _records[item.MonotonicNanoseconds];
    }
}

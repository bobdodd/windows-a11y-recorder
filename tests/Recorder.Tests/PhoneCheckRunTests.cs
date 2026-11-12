using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 3 of docs/architecture/android-checks.md, "Build order": the checks
// run on a phone recording, on each visit's settled snapshots, with the
// findings merged by screen, check, level and item. The recording is made
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

    // The Settings window's page and its "Save" button, 98 pixels square
    // (40 dp), moved down by moved pixels.
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

    private SessionPlaybackArchive Archive(out FakeRecords recording)
    {
        List<(long Time, string Channel, string EventType, object Payload)> records =
        [
            (At(0.5), AndroidCompanionRecords.DisplayChannel, AndroidCompanionRecords.DisplayEventType,
                new AndroidDisplayState(AndroidCompanionRecords.InitialReason, 0, 1080, 2404)),
            (At(1), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(1, Items(0))),
            (At(3), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(2, Items(10))),
            (At(5), AndroidAccessibilityRecords.TreeChannel, AndroidAccessibilityRecords.TreeEventType, Tree(3, null)),
        ];
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

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), recording, TestContext.Current.CancellationToken);

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
        Assert.Equal(1, finding.VisitCount);
        Assert.Equal(
            "Error: Button \"Save\", 40 by 40 dp, too narrow and too short; ATF needs 48 by 48 dp; seen 2 times on 1 visit, first at 00:00:01.000",
            finding.Text);
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

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, device, recording, TestContext.Current.CancellationToken);

        Assert.Equal((0, 2), (run.WithoutDensity, run.DensityChanged));
        var finding = Assert.Single(run.Findings);
        Assert.Equal([At(1)], finding.Times.Select(time => time.Time));
    }

    [Fact]
    public void Run_WithoutTheDensity_SkipsTheTouchChecks_AndCountsTheSnapshots()
    {
        var archive = Archive(out var recording);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, PhoneDeviceTimeline.Empty, recording, TestContext.Current.CancellationToken);

        Assert.Empty(run.Findings);
        Assert.Equal(3, run.WithoutDensity);
    }

    [Fact]
    public void Run_WithoutTheRecordsSource_FindsNothing()
    {
        var archive = Archive(out _);

        var run = PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, Device(), null, TestContext.Current.CancellationToken);

        Assert.Empty(run.Findings);
        Assert.Equal(0, run.SnapshotsChecked);
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

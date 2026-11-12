using System.Text.Json;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 2 of docs/architecture/android-checks.md: the phone's display density
// and the versions of its apps, read with a fake adb at the start and the
// stop, and the periods and words the Checks tab shows.
public sealed class AndroidDeviceTests
{
    private const string Serial = "58191FDCQ00ABZ";

    private const long Second = 1_000_000_000;

    private const string Nova = "com.example.nova";

    // What dumpsys package prints for one package, shortened, in the order
    // Settings.dumpPackagesLPr prints it: other sections first, then
    // "Packages:", then an older copy under "Hidden system packages:".
    private const string NovaDump = """
        Activity Resolver Table:
          Non-Data Actions:
              android.intent.action.MAIN:
                1a2b3c com.example.nova/.MainActivity filter 4d5e6f

        Key Set Manager:
          [com.example.nova]
              Signing KeySets: 51

        Packages:
          Package [com.example.nova] (a1b2c3):
            appId=10234
            pkg=Package{d4e5f6 com.example.nova}
            codePath=/data/app/~~x==/com.example.nova-y==
            versionCode=412 minSdk=26 targetSdk=35
            minExtensionVersions=[]
            versionName=3.4.1
            timeStamp=2026-11-10 14:02:11
            lastUpdateTime=2026-11-10 14:02:12
            installerPackageName=com.android.shell
            User 0: ceDataInode=123 installed=true hidden=false

        Queries:
          system apps queryable: false

        Hidden system packages:
          Package [com.example.nova] (e7f8a9):
            versionCode=100 minSdk=26 targetSdk=33
            versionName=1.0
        """;

    // Reading what adb prints

    [Fact]
    public void ParseDensity_ReadsThePhysicalDensity_AndAnOverride()
    {
        var own = AndroidDeviceCollector.ParseDensity("Physical density: 480\n");
        Assert.Equal((480, (int?)null, 480, (string?)null), (own.PhysicalDpi!.Value, own.OverrideDpi, own.InUseDpi!.Value, own.Problem));
        Assert.Equal(["Physical density: 480"], own.Lines);

        var set = AndroidDeviceCollector.ParseDensity("Physical density: 480\r\nOverride density: 420\r\n");
        Assert.Equal((480, 420, 420), (set.PhysicalDpi!.Value, set.OverrideDpi!.Value, set.InUseDpi!.Value));

        var odd = AndroidDeviceCollector.ParseDensity("Remote exception: android.os.DeadObjectException\n");
        Assert.Null(odd.InUseDpi);
        Assert.Equal("wm density printed no physical density.", odd.Problem);
    }

    [Fact]
    public void ParseVersion_TakesTheInstalledPackage_NotTheHiddenSystemCopy()
    {
        var app = AndroidDeviceCollector.ParseVersion(Nova, NovaDump);

        Assert.True(app.Found);
        Assert.Equal("3.4.1", app.VersionName);
        Assert.Equal(412, app.VersionCode);
        Assert.Equal(26, app.MinSdk);
        Assert.Equal(35, app.TargetSdk);
        Assert.Equal("2026-11-10 14:02:12", app.LastUpdateTime);
        Assert.Equal("com.android.shell", app.Installer);
        Assert.Equal(
            [
                "Package [com.example.nova] (a1b2c3):",
                "versionCode=412 minSdk=26 targetSdk=35",
                "versionName=3.4.1",
                "lastUpdateTime=2026-11-10 14:02:12",
                "installerPackageName=com.android.shell"
            ],
            app.Lines);
    }

    [Fact]
    public void ParseVersion_APackageNotPrinted_IsNotFound()
    {
        var app = AndroidDeviceCollector.ParseVersion("com.example.other", NovaDump);

        Assert.False(app.Found);
        Assert.Null(app.VersionName);
        Assert.Empty(app.Lines);

        // A name that only starts the same is another package.
        Assert.False(AndroidDeviceCollector.ParseVersion("com.example.nov", NovaDump).Found);
    }

    // The collector

    [Fact]
    public async Task Collector_ReadsTheDensityAtStartAndStop_AndEachAppSeen()
    {
        var adb = new FakeAdb();
        var sink = new ListSink();
        var clock = new ManualClock(Second);
        var seen = new FakePackages([Nova, "com.android.systemui", "bad name; reboot"]);
        await using var collector = new AndroidDeviceCollector(adb, Serial, seen);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.Summary is not null, () => collector.Summary);
        clock.Now = 9 * Second;
        await collector.StopAsync(new SessionBoundary(9 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(
            "Phone: density 480 dpi at the start and the stop; 1 of 3 apps' versions read at the stop.",
            collector.Summary);
        Assert.Equal(CollectorHealthState.Degraded, collector.HealthState);
        Assert.Contains("\"bad name; reboot\" was not read, as it is not a package name", collector.HealthReason, StringComparison.Ordinal);
        Assert.DoesNotContain(adb.Commands, command => command.Contains("reboot", StringComparison.Ordinal));
        var validator = new EventRecordValidator("session-1");
        foreach (var record in sink.Records)
        {
            Assert.Empty(validator.Validate(record));
        }

        var records = sink.Records.Where(record => record.Channel == AndroidDeviceRecords.Channel).ToList();
        Assert.Equal(
            [AndroidDeviceRecords.DensityEventType, AndroidDeviceRecords.DensityEventType, AndroidDeviceRecords.AppVersionsEventType, AndroidDeviceRecords.UnavailableEventType],
            records.Select(record => record.EventType));
        Assert.Equal(AndroidDeviceRecords.StartReason, records[0].Payload.GetProperty("reason").GetString());
        Assert.Equal(480, records[0].Payload.GetProperty("physicalDpi").GetInt32());
        Assert.Equal(AndroidDeviceRecords.StopReason, records[1].Payload.GetProperty("reason").GetString());
        var apps = records[2].Payload.GetProperty("apps");
        Assert.Equal(2, apps.GetArrayLength());
        Assert.Equal("3.4.1", apps[0].GetProperty("versionName").GetString());
        Assert.False(apps[1].GetProperty("found").GetBoolean());

        var device = new PhoneDeviceTimeline(
            records.Select(record => (record.MonotonicNanoseconds - Second, record.EventType, record.Payload)),
            [],
            8 * Second);
        var period = Assert.Single(device.Densities);
        Assert.Equal((0L, 8 * Second, 480, PhoneDensitySource.Physical, false), (period.Start, period.End, period.Dpi, period.Source, period.FoundAtStop));
        Assert.Equal(3.0, period.PixelsPerDp);
        Assert.Equal("00:00:00.000 to 00:00:08.000", period.When);
        Assert.Equal("version 3.4.1 (code 412), targets API 35", device.DescribeVersion(Nova));
        Assert.Equal("not found at the stop", device.DescribeVersion("com.android.systemui"));
        Assert.Equal("version not read", device.DescribeVersion("com.example.unseen"));
    }

    [Fact]
    public async Task Collector_AdbFails_RecordsWhyAndGoesOn()
    {
        var adb = new FakeAdb(fails: true);
        var sink = new ListSink();
        var clock = new ManualClock(Second);
        await using var collector = new AndroidDeviceCollector(adb, Serial, new FakePackages([]));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        clock.Now = 2 * Second;
        await collector.StopAsync(new SessionBoundary(2 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(CollectorHealthState.Degraded, collector.HealthState);
        Assert.Contains("adb could not run wm density: adb: device offline.", collector.HealthReason, StringComparison.Ordinal);
        var records = sink.Records.Where(record => record.Channel == AndroidDeviceRecords.Channel).ToList();
        var validator = new EventRecordValidator("session-1");
        Assert.All(records, record => Assert.Empty(validator.Validate(record)));
        Assert.Equal(2, records.Count(record => record.EventType == AndroidDeviceRecords.UnavailableEventType));
        Assert.Empty(new PhoneDeviceTimeline(records.Select(record => (record.MonotonicNanoseconds, record.EventType, record.Payload)), [], Second).Densities);
    }

    // The density's periods

    [Fact]
    public void Timeline_DisplaySizeChanges_StartPeriods_AndAResetReturnsToThePhonesOwn()
    {
        var device = new[]
        {
            Density(0, AndroidDeviceRecords.StartReason, 480, null),
            Density(10 * Second, AndroidDeviceRecords.StopReason, 480, null)
        };
        var settings = new[]
        {
            Change(3 * Second, "420", AndroidSettingsRecords.NoticeSource),
            Change(6 * Second, "", AndroidSettingsRecords.NoticeSource)
        };

        var timeline = new PhoneDeviceTimeline(device, settings, 10 * Second);

        Assert.Equal(
            [
                (0L, 3 * Second, 480, PhoneDensitySource.Physical),
                (3 * Second, 6 * Second, 420, PhoneDensitySource.DisplaySize),
                (6 * Second, 10 * Second, 480, PhoneDensitySource.Physical)
            ],
            timeline.Densities.Select(period => (period.Start, period.End, period.Dpi, period.Source)));
        Assert.Equal(420, timeline.DensityAt(4 * Second)!.Dpi);
        Assert.Equal(2.625, timeline.Densities[1].PixelsPerDp);
        Assert.Equal("version not recorded", timeline.DescribeVersion(Nova));
    }

    [Fact]
    public void Timeline_DisplaySizeSetToThePhonesOwnDensity_IsThePhonesOwn()
    {
        var timeline = new PhoneDeviceTimeline(
            [Density(0, AndroidDeviceRecords.StartReason, 480, null)],
            [Change(2 * Second, "540", AndroidSettingsRecords.NoticeSource), Change(4 * Second, "480", AndroidSettingsRecords.NoticeSource)],
            6 * Second);

        Assert.Equal(
            [PhoneDensitySource.Physical, PhoneDensitySource.DisplaySize, PhoneDensitySource.Physical],
            timeline.Densities.Select(period => period.Source));
    }

    [Fact]
    public void Timeline_ADensityOnlyTheStopShows_StartsAPeriodThere()
    {
        var timeline = new PhoneDeviceTimeline(
            [
                Density(0, AndroidDeviceRecords.StartReason, 480, null),
                Density(10 * Second, AndroidDeviceRecords.StopReason, 480, 540)
            ],
            [],
            10 * Second);

        Assert.Equal(2, timeline.Densities.Count);
        var last = timeline.Densities[1];
        Assert.Equal((10 * Second, 540, PhoneDensitySource.DisplaySize, true), (last.Start, last.Dpi, last.Source, last.FoundAtStop));
    }

    [Fact]
    public void Settings_AResetDisplaySize_IsThePhonesOwnSize()
    {
        var setting = AndroidSettingsRecords.Find("secure/display_density_forced")!;

        Assert.Equal("the phone's own size (reset)", AndroidSettingValues.Describe(setting, ""));
        Assert.Equal("420 dpi", AndroidSettingValues.Describe(setting, "420"));
    }

    private static (long, string, JsonElement) Density(long time, string reason, int physical, int? forced) =>
        (time, AndroidDeviceRecords.DensityEventType, JsonSerializer.SerializeToElement(
            new AndroidDisplayDensity(Serial, reason, physical, forced, [], null, time, time),
            JsonSerializerOptions.Web));

    private static (long, string, JsonElement) Change(long time, string? value, string source) =>
        (time, AndroidSettingsRecords.ChangeEventType, JsonSerializer.SerializeToElement(
            new AndroidSettingChanged(Serial, "secure/display_density_forced", value, null, source, null, null, time),
            JsonSerializerOptions.Web));

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

    private sealed class FakePackages(IReadOnlyList<string> packages) : IPhonePackagesSeen
    {
        public IReadOnlyList<string> PackagesSeen => packages;
    }

    /// <summary>A phone at 480 dpi with the Nova app installed and nothing else printed.</summary>
    private sealed class FakeAdb(bool fails = false) : IAdb
    {
        private readonly List<string> _commands = [];

        public string Path => "adb.exe";

        public IReadOnlyList<string> Commands
        {
            get
            {
                lock (_commands)
                {
                    return [.. _commands];
                }
            }
        }

        public Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation)
        {
            lock (_commands)
            {
                _commands.Add(string.Join(' ', arguments));
            }

            if (fails)
            {
                return Task.FromResult(new AdbResult(1, "", "adb: device offline"));
            }

            return Task.FromResult(arguments switch
            {
                ["-s", Serial, "shell", "wm", "density"] => new AdbResult(0, "Physical density: 480\n", ""),
                ["-s", Serial, "shell", "dumpsys", "package", Nova] => new AdbResult(0, NovaDump.Replace("\n", "\r\n", StringComparison.Ordinal), ""),
                ["-s", Serial, "shell", "dumpsys", "package", _] => new AdbResult(0, "Activity Resolver Table:\n\nKey Set Manager:\n", ""),
                _ => new AdbResult(1, "", $"unexpected command: {string.Join(' ', arguments)}")
            });
        }

        public Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation) => Task.FromResult(0);

        public IAdbRunning Start(IReadOnlyList<string> arguments, Action<string> onLine) =>
            throw new InvalidOperationException("not used");
    }

    /// <summary>A session clock that reads what the test sets.</summary>
    private sealed class ManualClock(long now) : ISessionClock
    {
        private long _now = now;

        public long Now
        {
            get => Interlocked.Read(ref _now);
            set => Interlocked.Exchange(ref _now, value);
        }

        public long Frequency => 1_000_000_000;

        public long OriginTimestamp => 0;

        public DateTimeOffset OriginUtc { get; } = DateTimeOffset.UtcNow;

        public long GetTimestamp() => Now;

        public long GetElapsedNanoseconds() => Now;
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

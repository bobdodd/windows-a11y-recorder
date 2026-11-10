using System.Diagnostics;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 5 of docs/architecture/android-implementation-plan.md, first step:
// getevent's output read into touch reports and hardware keys, against
// check 4's touches on the Pixel (TestData/android-touch-check4.txt), lines
// laid out as getevent.c prints them, a fake adb, a fake phone clock and a
// fake display.
public sealed class AndroidTouchTests
{
    private const string Serial = "58191FDCQ00ABZ";
    private const string Screen = "/dev/input/event3";
    private const string Keys = "/dev/input/event1";

    // The phone clock in the fake is this far ahead of the session clock,
    // so that check 4's first event is 0.598 s into the recording.
    private const long PhoneOffset = 146_202_000_000_000;

    // Ranges that give check 4's positions (TestData/README.md); not the
    // phone's own.
    private static readonly AndroidAxisRange XRange = new(0, 13439);
    private static readonly AndroidAxisRange YRange = new(0, 29919);

    // getevent -lp as getevent.c prints it, for a key device, a touch pad
    // without the direct property and a touch screen. The names and ranges
    // are made up for the test.
    private static readonly string[] Devices =
    [
        "add device 1: /dev/input/event1",
        "  name:     \"gpio-keys\"",
        "  events:",
        "    KEY (0001): KEY_VOLUMEDOWN        KEY_VOLUMEUP          KEY_POWER            ",
        "  input props:",
        "    <none>",
        "add device 2: /dev/input/event5",
        "  name:     \"virtual-touchpad\"",
        "  events:",
        "    ABS (0003): ABS_MT_POSITION_X     : value 0, min 0, max 1000, fuzz 0, flat 0, resolution 0",
        "                ABS_MT_POSITION_Y     : value 0, min 0, max 1000, fuzz 0, flat 0, resolution 0",
        "  input props:",
        "    INPUT_PROP_POINTER",
        "add device 3: /dev/input/event3",
        "  name:     \"fts_ts\"",
        "  events:",
        "    KEY (0001): BTN_TOUCH            ",
        "    ABS (0003): ABS_MT_SLOT           : value 0, min 0, max 9, fuzz 0, flat 0, resolution 0",
        "                ABS_MT_TOUCH_MAJOR    : value 0, min 0, max 3000, fuzz 0, flat 0, resolution 0",
        "                ABS_MT_POSITION_X     : value 0, min 0, max 13439, fuzz 0, flat 0, resolution 0",
        "                ABS_MT_POSITION_Y     : value 0, min 0, max 29919, fuzz 0, flat 0, resolution 0",
        "                ABS_MT_TRACKING_ID    : value 0, min 0, max 65535, fuzz 0, flat 0, resolution 0",
        "                ABS_MT_PRESSURE       : value 0, min 0, max 255, fuzz 0, flat 0, resolution 0",
        "  input props:",
        "    INPUT_PROP_DIRECT"
    ];

    // Reading getevent

    [Fact]
    public void Devices_ChoosesTheDirectTouchScreenWithItsRanges()
    {
        var devices = GeteventDevices.Parse(Devices);

        Assert.Equal([Keys, "/dev/input/event5", Screen], devices.Select(device => device.Path));
        Assert.Equal("gpio-keys", devices[0].Name);
        Assert.Empty(devices[0].Axes);
        var screen = GeteventDevices.TouchScreen(devices);
        Assert.NotNull(screen);
        Assert.Equal(Screen, screen.Path);
        Assert.Equal("fts_ts", screen.Name);
        Assert.Equal(new GeteventAxis(0, 0, 13439), screen.Axes["ABS_MT_POSITION_X"]);
        Assert.Equal(new GeteventAxis(0, 0, 29919), screen.Axes["ABS_MT_POSITION_Y"]);
        Assert.Equal(new GeteventAxis(0, 0, 9), screen.Axes["ABS_MT_SLOT"]);
        Assert.Contains("INPUT_PROP_DIRECT", screen.Properties);
    }

    [Fact]
    public void Devices_WithoutTheDirectProperty_TakesTheFirstWithPositions()
    {
        var devices = GeteventDevices.Parse(Devices.Where(line => !line.Contains("INPUT_PROP_DIRECT", StringComparison.Ordinal)));

        Assert.Equal("/dev/input/event5", GeteventDevices.TouchScreen(devices)?.Path);
        Assert.Null(GeteventDevices.TouchScreen(GeteventDevices.Parse(Devices.Take(6))));
    }

    [Fact]
    public void Line_IsReadWithAndWithoutItsDevice()
    {
        var plain = GeteventEvent.TryParse("[  146202.598005] EV_ABS       ABS_MT_POSITION_X    00001d56            ");
        Assert.Equal(new GeteventEvent(146_202_598_005, null, "EV_ABS", "ABS_MT_POSITION_X", "00001d56"), plain);
        Assert.Equal(7510, plain!.Number);
        Assert.Equal(146_202_598_005_000, plain.PhoneNanoseconds);

        var named = GeteventEvent.TryParse("[  146202.843093] /dev/input/event3: EV_ABS       ABS_MT_TRACKING_ID   ffffffff            \r");
        Assert.Equal(Screen, named!.Device);
        Assert.Equal(-1, named.Number);

        var key = GeteventEvent.TryParse("[     512.000001] /dev/input/event1: EV_KEY       KEY_VOLUMEDOWN       DOWN                ");
        Assert.Equal("DOWN", key!.Value);
        Assert.Null(key.Number);

        Assert.Null(GeteventEvent.TryParse("add device 4: /dev/input/event6"));
        Assert.Null(GeteventEvent.TryParse("  name:     \"uinput\""));
    }

    [Fact]
    public void Tracker_Check4_GivesTheThreeTapsAtThePositionsTheCheckReported()
    {
        var tracker = new TouchTracker();
        var reports = new List<IReadOnlyList<TrackedPointer>>();
        foreach (var line in File.ReadLines(Check4Path()))
        {
            var input = GeteventEvent.TryParse(line);
            Assert.NotNull(input);
            if (tracker.Add(input) is { } pointers)
            {
                reports.Add(pointers);
            }
        }

        var downs = reports.SelectMany(report => report).Where(pointer => pointer.Action == AndroidTouchRecords.DownAction).ToList();
        var ups = reports.SelectMany(report => report).Where(pointer => pointer.Action == AndroidTouchRecords.UpAction).ToList();
        Assert.Equal([0x3a24, 0x3a25, 0x3a26], downs.Select(pointer => pointer.TrackingId));
        Assert.Equal([0x3a24, 0x3a25, 0x3a26], ups.Select(pointer => pointer.TrackingId));
        Assert.All(reports, report => Assert.Single(report));
        Assert.All(reports.SelectMany(report => report), pointer => Assert.Equal(0, pointer.Slot));
        // Each within half a pixel of the position the check reported:
        // x 603, y 171; x 654, y 1188; x 613, y 2155.
        Assert.Equal(
            [(603.5, 171.0), (654.1, 1187.8), (612.6, 2154.9)],
            downs.Select(pointer => TouchGeometry.Natural(pointer.X, pointer.Y, XRange, YRange, 1080, 2404)));
        Assert.Equal(0x31, downs[0].Pressure);
        Assert.Equal(0x507, downs[0].TouchMajor);
        Assert.Equal(0x313, downs[0].TouchMinor);
    }

    [Fact]
    public void Tracker_TwoFingers_FollowsEachSlot()
    {
        var tracker = new TouchTracker();
        string[] lines =
        [
            Event(1.0, "EV_ABS", "ABS_MT_SLOT", 0), Event(1.0, "EV_ABS", "ABS_MT_TRACKING_ID", 10),
            Event(1.0, "EV_ABS", "ABS_MT_POSITION_X", 100), Event(1.0, "EV_ABS", "ABS_MT_POSITION_Y", 200),
            Event(1.0, "EV_ABS", "ABS_MT_SLOT", 1), Event(1.0, "EV_ABS", "ABS_MT_TRACKING_ID", 11),
            Event(1.0, "EV_ABS", "ABS_MT_POSITION_X", 300), Event(1.0, "EV_ABS", "ABS_MT_POSITION_Y", 400),
            Report(1.0),
            // Only the second finger moves, and only in y.
            Event(1.1, "EV_ABS", "ABS_MT_POSITION_Y", 450), Report(1.1),
            // The first finger lifts; the second stays.
            Event(1.2, "EV_ABS", "ABS_MT_SLOT", 0), Event(1.2, "EV_ABS", "ABS_MT_TRACKING_ID", -1), Report(1.2),
            Event(1.3, "EV_ABS", "ABS_MT_SLOT", 1), Event(1.3, "EV_ABS", "ABS_MT_TRACKING_ID", -1), Report(1.3),
            Report(1.4)
        ];

        var reports = lines.Select(line => tracker.Add(GeteventEvent.TryParse(line)!)).Where(report => report is not null).ToList();

        Assert.Equal(4, reports.Count);
        Assert.Equal(
            [new TrackedPointer(0, 10, "down", 100, 200, null, null, null), new TrackedPointer(1, 11, "down", 300, 400, null, null, null)],
            reports[0]);
        Assert.Equal(
            [new TrackedPointer(0, 10, "move", 100, 200, null, null, null), new TrackedPointer(1, 11, "move", 300, 450, null, null, null)],
            reports[1]);
        Assert.Equal(
            [new TrackedPointer(0, 10, "up", 100, 200, null, null, null), new TrackedPointer(1, 11, "move", 300, 450, null, null, null)],
            reports[2]);
        Assert.Equal([new TrackedPointer(1, 11, "up", 300, 450, null, null, null)], reports[3]);
    }

    [Theory]
    [InlineData(0, 100.0, 200.0, 1080, 2404)]
    [InlineData(90, 200.0, 980.0, 2404, 1080)]
    [InlineData(180, 980.0, 2204.0, 1080, 2404)]
    [InlineData(270, 2204.0, 100.0, 2404, 1080)]
    public void Geometry_TurnsThePositionByTheRotation(int rotation, double x, double y, int width, int height)
    {
        // A point 100 pixels from the left and 200 from the top of the
        // upright screen (TouchInputMapper.cpp, rotateAndScale).
        Assert.Equal((x, y, width, height), TouchGeometry.Shown(100, 200, 1080, 2404, rotation));
    }

    // The collector

    [Fact]
    public async Task Collector_RecordsEveryLineEachTouchAndEachKey()
    {
        var adb = new FakeAdb();
        var sink = new ListSink();
        var clock = new SessionClock();
        var display = new FakeDisplay((0, new AndroidDisplayState("initial", 0, 1080, 2404)));
        await using var collector = new AndroidTouchCollector(adb, Serial, new FakePhoneClock(true), display);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        var getevent = await adb.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(["-s", Serial, "shell", "-tt", "getevent", "-lt"], getevent.Arguments);
        var check4 = File.ReadLines(Check4Path()).Select(line => line.Insert(line.IndexOf(']', StringComparison.Ordinal) + 2, Screen + ": ")).ToList();
        foreach (var line in check4)
        {
            // As a terminal on the phone ends its lines.
            getevent.Line(line + "\r");
        }

        getevent.Line("");
        getevent.Line("[  146210.100000] /dev/input/event1: EV_KEY       KEY_VOLUMEDOWN       DOWN                ");
        getevent.Line("[  146210.100000] /dev/input/event1: EV_SYN       SYN_REPORT           00000000            ");
        getevent.Line("[  146210.200000] /dev/input/event1: EV_KEY       KEY_VOLUMEDOWN       UP                  ");
        getevent.Line("[  146210.200000] /dev/input/event1: EV_SYN       SYN_REPORT           00000000            ");
        getevent.Line("add device 4: /dev/input/event6");
        await WaitUntilAsync(() => sink.Records.Count(record => record.Channel == AndroidTouchRecords.KeyChannel) == 2);
        Assert.Matches(
            "^Touches: 3 touches, \\d+ reports, last up at x \\d+, y \\d+ of 1080 by 2404, 1 hardware key press, median delay from phone to recorder -?[\\d.]+ ms, touch screen /dev/input/event3 \"fts_ts\" with x 0 to 13439 and y 0 to 29919\\.$",
            collector.Summary);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(getevent.Stopped);
        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        Assert.Null(collector.HealthReason);
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));

        // The devices at the start, then every line, a report at a time.
        var raw = sink.Records.Where(record => record.Channel == AndroidTouchRecords.RawChannel).ToList();
        var devices = raw[0];
        Assert.Equal(AndroidTouchRecords.DevicesEventType, devices.EventType);
        Assert.Equal(Screen, devices.Payload.GetProperty("touchDevice").GetString());
        Assert.Equal(13439, devices.Payload.GetProperty("x").GetProperty("maximum").GetInt32());
        Assert.Equal(1080, devices.Payload.GetProperty("screenWidth").GetInt32());
        Assert.Equal(Devices.Length, devices.Payload.GetProperty("lines").GetArrayLength());
        var lines = raw.Skip(1).ToList();
        Assert.All(lines, record => Assert.Equal(AndroidTouchRecords.LinesEventType, record.EventType));
        Assert.Equal(check4.Count + 5, lines.Sum(record => record.Payload.GetProperty("lines").GetArrayLength()));
        Assert.Equal(check4.Count(line => line.Contains("SYN_REPORT", StringComparison.Ordinal)) + 3, lines.Count);
        Assert.Equal(check4[0], lines[0].Payload.GetProperty("lines")[0].GetString());
        Assert.Equal(146_202_598_005, lines[0].Payload.GetProperty("phoneMicroseconds").GetInt64());
        Assert.Equal(146_202_598_005_000 - PhoneOffset, lines[0].MonotonicNanoseconds);
        var added = lines[^1];
        Assert.Equal("add device 4: /dev/input/event6", added.Payload.GetProperty("lines")[0].GetString());
        Assert.Contains(AndroidScreenRecords.ReceiptTimeFlag, added.QualityFlags);

        // Each report with a finger on the screen, placed by its phone time.
        var touches = sink.Records.Where(record => record.Channel == AndroidTouchRecords.TouchChannel).ToList();
        var first = touches[0];
        Assert.Equal(AndroidTouchRecords.TouchEventType, first.EventType);
        Assert.Equal(146_202_598_005_000 - PhoneOffset, first.MonotonicNanoseconds);
        Assert.Equal(146_202_598_005_000, first.NativeTimestamp!.Value);
        Assert.Equal(AndroidCompanionRecords.FittedClockMapping, first.ClockMappingId);
        Assert.Equal(FakePhoneClock.Uncertainty, first.TimestampUncertaintyNanoseconds);
        Assert.Equal([AndroidTouchRecords.PhoneInputTimeFlag], first.QualityFlags);
        var pointer = first.Payload.GetProperty("pointers")[0];
        Assert.Equal("down", pointer.GetProperty("action").GetString());
        Assert.Equal(7510, pointer.GetProperty("rawX").GetInt32());
        Assert.Equal(603.5, pointer.GetProperty("x").GetDouble());
        Assert.Equal(171.0, pointer.GetProperty("y").GetDouble());
        Assert.Equal(49, pointer.GetProperty("pressure").GetInt32());
        Assert.Equal(0, first.Payload.GetProperty("rotation").GetInt32());
        Assert.Equal(3, touches.Count(record => record.Payload.GetProperty("pointers")[0].GetProperty("action").GetString() == "down"));
        Assert.Equal(3, touches.Count(record => record.Payload.GetProperty("pointers")[0].GetProperty("action").GetString() == "up"));

        // Each hardware key, named as getevent -lp named its device.
        var keys = sink.Records.Where(record => record.Channel == AndroidTouchRecords.KeyChannel).ToList();
        Assert.Equal(["down", "up"], keys.Select(record => record.Payload.GetProperty("action").GetString()));
        Assert.Equal("KEY_VOLUMEDOWN", keys[0].Payload.GetProperty("key").GetString());
        Assert.Equal("gpio-keys", keys[0].Payload.GetProperty("deviceName").GetString());
        Assert.Equal(146_210_100_000_000 - PhoneOffset, keys[0].MonotonicNanoseconds);
    }

    [Fact]
    public async Task Collector_TurnsEachTouchByTheRotationAtItsPhoneTime()
    {
        var adb = new FakeAdb();
        var sink = new ListSink();
        var clock = new SessionClock();
        var display = new FakeDisplay(
            (0, new AndroidDisplayState("initial", 0, 1080, 2404)),
            (146_204_000_000_000, new AndroidDisplayState("changed", 90, 1080, 2404)));
        await using var collector = new AndroidTouchCollector(adb, Serial, new FakePhoneClock(true), display);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        var getevent = await adb.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        foreach (var (time, id) in new[] { (146_203.0, 1), (146_205.0, 2) })
        {
            getevent.Line(Event(time, "EV_ABS", "ABS_MT_TRACKING_ID", id, Screen));
            getevent.Line(Event(time, "EV_ABS", "ABS_MT_POSITION_X", 1244, Screen));
            getevent.Line(Event(time, "EV_ABS", "ABS_MT_POSITION_Y", 2489, Screen));
            getevent.Line(Report(time, Screen));
            getevent.Line(Event(time + 0.1, "EV_ABS", "ABS_MT_TRACKING_ID", -1, Screen));
            getevent.Line(Report(time + 0.1, Screen));
        }

        await WaitUntilAsync(() => sink.Records.Count(record => record.Channel == AndroidTouchRecords.TouchChannel) == 4);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        var touches = sink.Records.Where(record => record.Channel == AndroidTouchRecords.TouchChannel).ToList();
        // 1244 and 2489 are 100 and 200 pixels on the upright screen.
        var upright = touches[0].Payload;
        Assert.Equal(0, upright.GetProperty("rotation").GetInt32());
        Assert.Equal(100, Math.Round(upright.GetProperty("pointers")[0].GetProperty("x").GetDouble()));
        Assert.Equal(200, Math.Round(upright.GetProperty("pointers")[0].GetProperty("y").GetDouble()));
        var turned = touches[2].Payload;
        Assert.Equal(90, turned.GetProperty("rotation").GetInt32());
        Assert.Equal(2404, turned.GetProperty("screenWidth").GetInt32());
        Assert.Equal(1080, turned.GetProperty("screenHeight").GetInt32());
        Assert.Equal(100, Math.Round(turned.GetProperty("pointers")[0].GetProperty("naturalX").GetDouble()));
        Assert.Equal(200, Math.Round(turned.GetProperty("pointers")[0].GetProperty("x").GetDouble()));
        Assert.Equal(980, Math.Round(turned.GetProperty("pointers")[0].GetProperty("y").GetDouble()));
    }

    [Fact]
    public async Task Collector_WithoutAPhoneClockOrDisplay_PlacesByReceiptUpright()
    {
        var adb = new FakeAdb();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidTouchCollector(adb, Serial, new FakePhoneClock(false), null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        var getevent = await adb.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        getevent.Line(Event(1.0, "EV_ABS", "ABS_MT_TRACKING_ID", 1, Screen));
        getevent.Line(Event(1.0, "EV_ABS", "ABS_MT_POSITION_X", 1244, Screen));
        getevent.Line(Event(1.0, "EV_ABS", "ABS_MT_POSITION_Y", 2489, Screen));
        getevent.Line(Report(1.0, Screen));
        await WaitUntilAsync(() => sink.Records.Any(record => record.Channel == AndroidTouchRecords.TouchChannel));

        Assert.EndsWith(", placed by the time each line arrived, as there is no phone clock, touch screen /dev/input/event3 \"fts_ts\" with x 0 to 13439 and y 0 to 29919.", collector.Summary);
        var touch = sink.Records.Single(record => record.Channel == AndroidTouchRecords.TouchChannel);
        Assert.Equal([AndroidScreenRecords.ReceiptTimeFlag, AndroidTouchRecords.RotationUnknownFlag], touch.QualityFlags);
        Assert.NotEqual(AndroidCompanionRecords.FittedClockMapping, touch.ClockMappingId);
        Assert.Equal(1080, touch.Payload.GetProperty("screenWidth").GetInt32());
        var validator = new EventRecordValidator("session-1");
        Assert.Empty(validator.Validate(touch));
    }

    [Fact]
    public async Task Collector_NoTouchScreen_KeepsTheKeysAndSaysWhy()
    {
        var adb = new FakeAdb(Devices.Take(6).ToArray());
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidTouchCollector(adb, Serial, new FakePhoneClock(true), null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        var getevent = await adb.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        getevent.Line("[  146210.000001] /dev/input/event1: EV_KEY       KEY_POWER            DOWN                ");
        await WaitUntilAsync(() => sink.Records.Any(record => record.Channel == AndroidTouchRecords.KeyChannel));

        Assert.Equal(CollectorHealthState.Degraded, collector.HealthState);
        Assert.Equal(
            "The phone's touches could not be recorded: no touch screen was found in what \"getevent -lp\" listed. The recording goes on without them.",
            collector.HealthReason);
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
        Assert.Contains(sink.Records, record => record.EventType == AndroidTouchRecords.UnavailableEventType);
    }

    [Fact]
    public async Task Collector_GeteventEnds_SaysTheTouchesStopped()
    {
        var adb = new FakeAdb();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidTouchCollector(adb, Serial, new FakePhoneClock(true), null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        var getevent = await adb.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        getevent.Exit(1);
        await WaitUntilAsync(() => collector.HealthReason is not null);

        Assert.Equal(
            "The phone's touches stopped being recorded during the recording. getevent on the phone ended with exit code 1: the phone was unplugged, adb stopped, or the phone ended it. The recording goes on without them.",
            collector.HealthReason);
        var unavailable = sink.Records.Single(record => record.EventType == AndroidTouchRecords.UnavailableEventType);
        Assert.True(unavailable.Payload.GetProperty("connectionLost").GetBoolean());
    }

    [Fact]
    public async Task Collector_DevicesCannotBeListed_SaysWhyAndDoesNotStartGetevent()
    {
        var adb = new FakeAdb(listFails: true);
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidTouchCollector(adb, Serial, new FakePhoneClock(true), null);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.HealthReason is not null);

        Assert.Equal(
            "The phone's touches could not be recorded: adb could not list the phone's input devices: error: device '58191FDCQ00ABZ' not found. The recording goes on without them.",
            collector.HealthReason);
        Assert.False(adb.Started.Task.IsCompleted);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.Equal(CollectorLifecycleState.Stopped, collector.LifecycleState);
    }

    [Fact]
    public void Validator_RefusesATouchWithAnUnknownActionOrRotation()
    {
        var validator = new EventRecordValidator("session-1");
        var descriptor = CollectorDescriptor.Create(AndroidTouchCollector.Type, "AndroidTouchCollector", "1.0.0", [AndroidTouchRecords.TouchChannel], "adb-shell-getevent");
        var report = new AndroidTouchReport(Screen, 1, 2, 45, 1080, 2404, [new AndroidTouchPointer(0, 1, "hover", 1, 2, 3, 4, 3, 4, null, null, null)]);
        var record = RecorderEventFactory.Create("session-1", descriptor, AndroidTouchRecords.TouchChannel, 1, 0, AndroidTouchRecords.TouchEventType, report);

        var issues = validator.Validate(record);

        Assert.Contains(issues, issue => issue.Path == "#/payload/rotation");
        Assert.Contains(issues, issue => issue.Path == "#/payload/pointers/0/action");
    }

    private static string Check4Path() => Path.Combine(AppContext.BaseDirectory, "TestData", "android-touch-check4.txt");

    private static string Event(double seconds, string type, string code, int value, string? device = null) =>
        $"[{seconds,15:F6}] {(device is null ? "" : device + ": ")}{type,-12} {code,-20} {unchecked((uint)value):x8}            ";

    private static string Report(double seconds, string? device = null) => Event(seconds, "EV_SYN", "SYN_REPORT", 0, device);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "Timed out.");
            await Task.Delay(20);
        }
    }

    /// <summary>A phone clock exactly <see cref="PhoneOffset"/> ahead of the session clock.</summary>
    private sealed class FakePhoneClock(bool sampled) : IPhoneClock
    {
        public const long Uncertainty = 1_500_000;

        public Task<bool> FirstSample { get; } = Task.FromResult(sampled);

        public PhoneTimeMapping? Map(long phoneNanoseconds) =>
            sampled ? new PhoneTimeMapping(phoneNanoseconds - PhoneOffset, Uncertainty, 1) : null;
    }

    private sealed class FakeDisplay(params (long Time, AndroidDisplayState State)[] states) : IPhoneDisplay
    {
        private readonly PhoneDisplayHistory _history = Fill(states);

        public AndroidDisplayState? DisplayAt(long phoneNanoseconds) => _history.DisplayAt(phoneNanoseconds);

        private static PhoneDisplayHistory Fill((long Time, AndroidDisplayState State)[] states)
        {
            var history = new PhoneDisplayHistory();
            foreach (var (time, state) in states)
            {
                history.Add(time, state);
            }

            return history;
        }
    }

    private sealed class FakeAdb(string[]? devices = null, bool listFails = false) : IAdb
    {
        public string Path => "adb.exe";

        public TaskCompletionSource<FakeGetevent> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation)
        {
            var command = string.Join(' ', arguments);
            if (command == $"-s {Serial} shell getevent -lp")
            {
                return Task.FromResult(listFails
                    ? new AdbResult(1, "", $"error: device '{Serial}' not found")
                    : new AdbResult(0, string.Join("\n", devices ?? Devices) + "\n", ""));
            }

            if (command == $"-s {Serial} shell wm size")
            {
                return Task.FromResult(new AdbResult(0, "Physical size: 1080x2404\n", ""));
            }

            return Task.FromResult(new AdbResult(1, "", $"unexpected command: {command}"));
        }

        public Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation) => Task.FromResult(0);

        public IAdbRunning Start(IReadOnlyList<string> arguments, Action<string> onLine)
        {
            var running = new FakeGetevent([.. arguments], onLine);
            Started.TrySetResult(running);
            return running;
        }
    }

    private sealed class FakeGetevent(string[] arguments, Action<string> onLine) : IAdbRunning
    {
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string[] Arguments { get; } = arguments;

        public bool Stopped { get; private set; }

        public Task<int> Exited => _exited.Task;

        public void Line(string line) => onLine(line);

        public void Exit(int code) => _exited.TrySetResult(code);

        public ValueTask StopAsync()
        {
            Stopped = true;
            _exited.TrySetResult(-1);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

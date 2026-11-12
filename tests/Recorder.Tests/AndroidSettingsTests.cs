using System.Text.Json;
using System.Threading.Channels;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 8 of docs/architecture/android-implementation-plan.md, step 8a: the
// phone's settings, read with a fake adb at the start and the stop and on
// each of the companion app's notices, placed on the session clock with a
// fake phone clock, and shown in the properties panel's rows.
public sealed class AndroidSettingsTests
{
    private const string Serial = "58191FDCQ00ABZ";

    // The phone clock in the fake is this far ahead of the session clock.
    private const long PhoneOffset = 5_000_000_000_000;

    private const long Second = 1_000_000_000;

    private const string Inversion = "secure/accessibility_display_inversion_enabled";

    private static readonly AndroidSetting InversionSetting = AndroidSettingsRecords.Find(Inversion)!;

    // The protocol

    [Fact]
    public void Protocol_AsksForEachSetting_AndReadsANotice()
    {
        using var request = JsonDocument.Parse(CompanionProtocol.SettingsRequest(
            [InversionSetting, AndroidSettingsRecords.Find("system/font_scale")!]));
        Assert.Equal("settings", request.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            ["secure accessibility_display_inversion_enabled", "system font_scale"],
            request.RootElement.GetProperty("settings").EnumerateArray()
                .Select(setting => setting.GetProperty("namespace").GetString() + " " + setting.GetProperty("name").GetString()));

        Assert.Equal(
            new CompanionSettingChanged("secure", "accessibility_display_inversion_enabled", 123),
            CompanionProtocol.Parse("{\"type\":\"setting-changed\",\"namespace\":\"secure\",\"name\":\"accessibility_display_inversion_enabled\",\"time\":123}"));
        Assert.Throws<FormatException>(() => CompanionProtocol.Parse("{\"type\":\"setting-changed\",\"namespace\":\"secure\",\"time\":123}"));
    }

    [Fact]
    public void Settings_AreKnownByIdAndByNamespaceAndName()
    {
        Assert.Equal(34, AndroidSettingsRecords.All.Count);
        Assert.Equal(AndroidSettingsRecords.All.Count, AndroidSettingsRecords.All.Select(setting => setting.Id).Distinct().Count());
        Assert.Same(InversionSetting, AndroidSettingsRecords.Find("secure", "accessibility_display_inversion_enabled"));
        Assert.Equal("Color inversion", InversionSetting.Label);
        Assert.Equal(AndroidSettingsRecords.ColorGroup, InversionSetting.Group);
        Assert.Null(AndroidSettingsRecords.Find("secure/no_such_setting"));
    }

    // Reading what adb prints

    [Fact]
    public void ParseList_ReadsEachLine_NullAsNotSet_AndALineBreakInAValue()
    {
        var values = AndroidSettingsCollector.ParseList(
            "accessibility_enabled=1\r\nenabled_accessibility_services=null\r\nlong_text=first\r\nsecond\r\nempty=\r\n");

        Assert.Equal("1", values["accessibility_enabled"]);
        Assert.Null(values["enabled_accessibility_services"]);
        Assert.Equal("first\nsecond", values["long_text"]);
        Assert.Equal("", values["empty"]);
        Assert.Equal(4, values.Count);
    }

    [Theory]
    [InlineData(Inversion, "1", "on")]
    [InlineData(Inversion, "0", "off")]
    [InlineData(Inversion, null, "not set")]
    [InlineData("secure/accessibility_magnification_mode", "1", "full screen (1)")]
    [InlineData("secure/accessibility_display_daltonizer", "-1", "off (-1)")]
    [InlineData("secure/accessibility_display_daltonizer", "12", "red-green, deuteranomaly (12)")]
    [InlineData("secure/ui_night_mode", "2", "on (2)")]
    [InlineData("system/font_scale", "1.15", "115 percent")]
    [InlineData("secure/font_weight_adjustment", "300", "bold (300)")]
    [InlineData("secure/display_density_forced", null, "the phone's own size (not set)")]
    [InlineData("secure/display_density_forced", "", "the phone's own size (reset)")]
    [InlineData("secure/display_density_forced", "480", "480 dpi")]
    [InlineData("secure/accessibility_interactive_ui_timeout_ms", "10000", "10000 ms")]
    [InlineData("global/window_animation_scale", "0", "off (0 times)")]
    [InlineData("system/master_balance", "0", "centered")]
    [InlineData("secure/tts_default_rate", "150", "150 (1.5 times normal)")]
    [InlineData("secure/enabled_accessibility_services", "", "none")]
    [InlineData(
        "secure/enabled_accessibility_services",
        "com.google.android.marvin.talkback/com.google.android.marvin.talkback.TalkBackService:" +
        "io.github.bobdodd.a11yrecorder.companion/io.github.bobdodd.a11yrecorder.companion.RecorderAccessibilityService",
        "TalkBackService, RecorderAccessibilityService")]
    public void Describe_GivesTheValueInWords(string id, string? value, string expected)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, AndroidSettingValues.Describe(AndroidSettingsRecords.Find(id)!, value));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    // The collector

    [Fact]
    public async Task Collector_RecordsInversionOnThenOff_AtTheNoticesTimes()
    {
        var adb = new FakeAdb();
        var changes = new FakeChanges();
        var sink = new ListSink();
        var clock = new ManualClock(Second);
        await using var collector = new AndroidSettingsCollector(adb, Serial, changes, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.Summary is not null, () => collector.Summary);
        var asked = await changes.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(AndroidSettingsRecords.All, asked);

        clock.Now = 2 * Second + 50_000_000;
        adb.Set(InversionSetting, "1");
        changes.Send(Notice(InversionSetting, 2 * Second));
        await WaitUntilAsync(() => collector.Summary?.Contains("1 change,", StringComparison.Ordinal) == true, () => collector.Summary);
        clock.Now = 4 * Second + 50_000_000;
        adb.Set(InversionSetting, "0");
        changes.Send(Notice(InversionSetting, 4 * Second));
        await WaitUntilAsync(() => collector.Summary?.Contains("2 changes", StringComparison.Ordinal) == true, () => collector.Summary);
        clock.Now = 6 * Second;
        await collector.StopAsync(new SessionBoundary(6 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(
            "Phone settings: 34 read at the start, 2 changes, latest Color inversion off, read again at the stop.",
            collector.Summary);
        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        Assert.Null(collector.HealthReason);
        var records = sink.Records.Where(record => record.Channel == AndroidSettingsRecords.Channel).ToList();
        var validator = new EventRecordValidator("session-1");
        foreach (var record in sink.Records)
        {
            Assert.Empty(validator.Validate(record));
        }

        Assert.Equal(
            [AndroidSettingsRecords.SnapshotEventType, AndroidSettingsRecords.ChangeEventType, AndroidSettingsRecords.ChangeEventType, AndroidSettingsRecords.SnapshotEventType],
            records.Select(record => record.EventType));
        var start = records[0];
        Assert.Equal(AndroidSettingsRecords.StartReason, start.Payload.GetProperty("reason").GetString());
        Assert.Equal("0", start.Payload.GetProperty("settings").GetProperty(Inversion).GetString());
        Assert.Equal(JsonValueKind.Null, start.Payload.GetProperty("settings").GetProperty("secure/display_density_forced").ValueKind);
        Assert.Equal(34, start.Payload.GetProperty("settings").EnumerateObject().Count());

        var on = records[1];
        Assert.Equal(2 * Second, on.MonotonicNanoseconds);
        Assert.Equal(Inversion, on.Payload.GetProperty("setting").GetString());
        Assert.Equal("1", on.Payload.GetProperty("value").GetString());
        Assert.Equal("0", on.Payload.GetProperty("previous").GetString());
        Assert.Equal(AndroidSettingsRecords.NoticeSource, on.Payload.GetProperty("source").GetString());
        Assert.Equal(AndroidCompanionRecords.FittedClockMapping, on.ClockMappingId);
        Assert.Equal(2 * Second + PhoneOffset, on.NativeTimestamp!.Value);
        Assert.Contains(AndroidSettingsRecords.PhoneNoticeTimeFlag, on.QualityFlags);
        Assert.Equal(4 * Second, records[2].MonotonicNanoseconds);
        Assert.Equal("0", records[2].Payload.GetProperty("value").GetString());
        Assert.Equal(AndroidSettingsRecords.StopReason, records[3].Payload.GetProperty("reason").GetString());

        // The panel's Color inversion row, before, during and after.
        var timeline = new PhoneSettingsTimeline(records.Select(record => (record.MonotonicNanoseconds, record.EventType, record.Payload)));
        Assert.True(timeline.Recorded);
        PropertyRow Row(long time) => timeline.RowsAt(time).Single(row => row.Setting == "Color inversion");
        Assert.Equal(("off", "at start", false), (Row(Second + 1).Value, Row(Second + 1).WhenSet, Row(Second + 1).Changed));
        Assert.Equal(("on", "00:00:02.000", true), (Row(2 * Second + 1).Value, Row(2 * Second + 1).WhenSet, Row(2 * Second + 1).Changed));
        Assert.False(Row(3 * Second + 500_000_000).Changed);
        Assert.Equal(("off", "00:00:04.000"), (Row(5 * Second).Value, Row(5 * Second).WhenSet));
        Assert.Equal(AndroidSettingsRecords.ColorGroup, Row(5 * Second).Group);
        Assert.Equal(AndroidSettingsRecords.All.Count, timeline.RowsAt(5 * Second).Count);
        Assert.Equal([2 * Second, 4 * Second], timeline.ChangeTimesOf(PhoneSettingsTimeline.KeyPrefix + Inversion));
        Assert.Empty(timeline.ChangeTimesOf(PhoneSettingsTimeline.KeyPrefix + "system/font_scale")!);
        Assert.Equal(
            [2 * Second, 4 * Second],
            PropertyChangeSteps.TimesOf(Row(5 * Second), WindowsPreferenceTimeline.Empty, MagnifierChangeTimeline.Empty, phoneSettings: timeline));
    }

    [Fact]
    public async Task Collector_NoticeWithoutAChange_IsCountedNotRecorded()
    {
        var adb = new FakeAdb();
        var changes = new FakeChanges();
        var sink = new ListSink();
        var clock = new ManualClock(Second);
        await using var collector = new AndroidSettingsCollector(adb, Serial, changes, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.Summary is not null, () => collector.Summary);

        // Turned on and off again before the recorder read it.
        clock.Now = 2 * Second;
        changes.Send(Notice(InversionSetting, 2 * Second));
        await WaitUntilAsync(() => collector.Summary?.Contains("1 notice with no change", StringComparison.Ordinal) == true, () => collector.Summary);
        clock.Now = 3 * Second;
        await collector.StopAsync(new SessionBoundary(3 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(
            "Phone settings: 34 read at the start, 0 changes, 1 notice with no change, read again at the stop.",
            collector.Summary);
        Assert.DoesNotContain(sink.Records, record => record.EventType == AndroidSettingsRecords.ChangeEventType);
    }

    [Fact]
    public async Task Collector_WithoutTheCompanion_FindsTheChangeAtTheStop()
    {
        var adb = new FakeAdb();
        var sink = new ListSink();
        var clock = new ManualClock(Second);
        await using var collector = new AndroidSettingsCollector(adb, Serial, new FakeChanges(connected: false), new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded, () => collector.HealthState.ToString());
        Assert.Contains("the companion app is not connected", collector.HealthReason, StringComparison.Ordinal);

        adb.Set(AndroidSettingsRecords.Find("system/font_scale")!, "1.3");
        clock.Now = 5 * Second;
        await collector.StopAsync(new SessionBoundary(5 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        var records = sink.Records.Where(record => record.Channel == AndroidSettingsRecords.Channel).ToList();
        var validator = new EventRecordValidator("session-1");
        Assert.All(records, record => Assert.Empty(validator.Validate(record)));
        var found = Assert.Single(records, record => record.EventType == AndroidSettingsRecords.ChangeEventType);
        Assert.Equal(5 * Second, found.MonotonicNanoseconds);
        Assert.Equal("system/font_scale", found.Payload.GetProperty("setting").GetString());
        Assert.Equal(AndroidSettingsRecords.StopSource, found.Payload.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, found.Payload.GetProperty("noticePhoneNanoseconds").ValueKind);
        Assert.Contains(AndroidSettingsRecords.FoundAtStopFlag, found.QualityFlags);
        Assert.Contains(records, record => record.EventType == AndroidSettingsRecords.UnavailableEventType);
        Assert.EndsWith(", read again at the stop, which found 1 more.", collector.Summary, StringComparison.Ordinal);

        var timeline = new PhoneSettingsTimeline(records.Select(record => (record.MonotonicNanoseconds, record.EventType, record.Payload)));
        var row = timeline.RowsAt(5 * Second).Single(row => row.Setting == "Font size");
        Assert.Equal("00:00:05.000, found at the stop", row.WhenSet);
        Assert.Equal("100 percent", timeline.RowsAt(4 * Second).Single(row => row.Setting == "Font size").Value);
    }

    [Fact]
    public async Task Collector_WithoutAClockSample_PlacesAChangeAtItsReceipt()
    {
        var adb = new FakeAdb();
        var changes = new FakeChanges();
        var sink = new ListSink();
        var clock = new ManualClock(Second);
        await using var collector = new AndroidSettingsCollector(adb, Serial, changes, new FakePhoneClock(false));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.Summary is not null, () => collector.Summary);

        adb.Set(InversionSetting, "1");
        changes.Send(Notice(InversionSetting, 2 * Second, receivedAt: 3 * Second));
        await WaitUntilAsync(() => collector.Summary?.Contains("1 change,", StringComparison.Ordinal) == true, () => collector.Summary);
        clock.Now = 4 * Second;
        await collector.StopAsync(new SessionBoundary(4 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        var change = Assert.Single(sink.Records, record => record.EventType == AndroidSettingsRecords.ChangeEventType);
        Assert.Equal(3 * Second, change.MonotonicNanoseconds);
        Assert.Contains(AndroidScreenRecords.ReceiptTimeFlag, change.QualityFlags);
        Assert.Empty(new EventRecordValidator("session-1").Validate(change));
    }

    [Fact]
    public async Task Collector_SettingsThatCannotBeRead_AreSaidAndDegrade()
    {
        var adb = new FakeAdb(listFails: true);
        var sink = new ListSink();
        var clock = new ManualClock(Second);
        await using var collector = new AndroidSettingsCollector(adb, Serial, new FakeChanges(), new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(Second, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded, () => collector.HealthState.ToString());
        clock.Now = 2 * Second;
        await collector.StopAsync(new SessionBoundary(2 * Second, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Null(collector.Summary);
        Assert.Contains("could not be read", collector.HealthReason, StringComparison.Ordinal);
        Assert.Contains("device offline", collector.HealthReason, StringComparison.Ordinal);
        var records = sink.Records.Where(record => record.Channel == AndroidSettingsRecords.Channel).ToList();
        var unavailable = Assert.Single(records);
        Assert.Equal(AndroidSettingsRecords.UnavailableEventType, unavailable.EventType);
        Assert.Empty(new EventRecordValidator("session-1").Validate(unavailable));
        Assert.False(new PhoneSettingsTimeline(records.Select(record => (record.MonotonicNanoseconds, record.EventType, record.Payload))).Recorded);
    }

    // The records' shape

    [Fact]
    public void Validator_RefusesAChangeWithANumberForItsValue()
    {
        var descriptor = CollectorDescriptor.Create(AndroidSettingsCollector.Type, "test", "1.0", [AndroidSettingsRecords.Channel], "test");
        var record = RecorderEventFactory.Create(
            "session-1",
            descriptor,
            AndroidSettingsRecords.Channel,
            1,
            2 * Second,
            AndroidSettingsRecords.ChangeEventType,
            JsonDocument.Parse(
                "{\"serial\":\"" + Serial + "\",\"setting\":\"" + Inversion + "\",\"value\":1,\"previous\":\"0\",\"source\":\"notice\"," +
                "\"noticePhoneNanoseconds\":1,\"noticeReceivedAtNanoseconds\":2,\"readAtNanoseconds\":3}").RootElement.Clone(),
            []);

        Assert.NotEmpty(new EventRecordValidator("session-1").Validate(record));
    }

    [Fact]
    public void Timeline_WithoutSettings_HasNoRows_AndNoTimes()
    {
        var timeline = PhoneSettingsTimeline.Empty;

        Assert.False(timeline.Recorded);
        Assert.Empty(timeline.RowsAt(Second));
        Assert.Null(timeline.ChangeTimesOf(PhoneSettingsTimeline.KeyPrefix + Inversion));
    }

    private static ReceivedSettingChange Notice(AndroidSetting setting, long sessionTime, long? receivedAt = null) =>
        new(new CompanionSettingChanged(setting.Namespace, setting.Name, sessionTime + PhoneOffset), receivedAt ?? sessionTime + 20_000_000);

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

    /// <summary>A phone whose settings are their defaults, with color inversion off and the font at 100 percent.</summary>
    private sealed class FakeAdb(bool listFails = false) : IAdb
    {
        private readonly Dictionary<string, string?> _values = AndroidSettingsRecords.All.ToDictionary(
            setting => setting.Id,
            setting => setting.Id switch
            {
                "system/font_scale" => "1.0",
                "secure/display_density_forced" => null,
                _ when setting.Kind == AndroidSettingKind.Switch => "0",
                _ => (string?)null
            });

        public string Path => "adb.exe";

        public void Set(AndroidSetting setting, string? value)
        {
            lock (_values)
            {
                _values[setting.Id] = value;
            }
        }

        public Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation)
        {
            lock (_values)
            {
                if (arguments is ["-s", Serial, "shell", "settings", "list", var table])
                {
                    if (listFails)
                    {
                        return Task.FromResult(new AdbResult(1, "", "adb: device offline"));
                    }

                    var lines = _values
                        .Where(pair => pair.Key.StartsWith(table + "/", StringComparison.Ordinal) && pair.Value is not null)
                        .Select(pair => pair.Key[(table.Length + 1)..] + "=" + pair.Value)
                        .Append("some_other_setting=7");
                    return Task.FromResult(new AdbResult(0, string.Join("\n", lines) + "\n", ""));
                }

                if (arguments is ["-s", Serial, "shell", "settings", "get", var settingNamespace, var name])
                {
                    return Task.FromResult(new AdbResult(0, (_values.GetValueOrDefault(settingNamespace + "/" + name) ?? "null") + "\n", ""));
                }
            }

            return Task.FromResult(new AdbResult(1, "", $"unexpected command: {string.Join(' ', arguments)}"));
        }

        public Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation) => Task.FromResult(0);

        public IAdbRunning Start(IReadOnlyList<string> arguments, Action<string> onLine) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class FakeChanges(bool connected = true) : IPhoneSettingChanges
    {
        private readonly Channel<ReceivedSettingChange> _channel = Channel.CreateUnbounded<ReceivedSettingChange>();

        public TaskCompletionSource<IReadOnlyList<AndroidSetting>> Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Send(ReceivedSettingChange received) => Assert.True(_channel.Writer.TryWrite(received));

        public Task<ChannelReader<ReceivedSettingChange>?> RequestSettingChangesAsync(
            IReadOnlyList<AndroidSetting> settings,
            CancellationToken cancellation)
        {
            Asked.TrySetResult(settings);
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

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 2 of docs/architecture/android-implementation-plan.md: the companion
// app's protocol, the phone clock's fit, the installation and the collector,
// against a fake app on a loopback socket and a fake adb.
public sealed class AndroidCompanionTests
{
    private const string Serial = "58191FDCQ00ABZ";

    private const string TalkBack =
        "com.google.android.marvin.talkback/com.google.android.marvin.talkback.TalkBackService";

    // The clock fit

    [Fact]
    public void Exchange_MidpointsAndBound()
    {
        var exchange = new PhoneClockExchange(1_000, 50_400, 50_600, 2_000);

        Assert.Equal(50_500, exchange.PhoneMidpoint);
        Assert.Equal(1_500, exchange.SessionMidpoint);
        Assert.Equal(400, exchange.ErrorBound);
    }

    [Fact]
    public void Mapper_FollowsOffsetAndDrift()
    {
        var mapper = new PhoneClockMapper();
        // The phone's clock is 7 s ahead and runs 50 parts per million fast.
        for (var i = 0; i < 40; i++)
        {
            var session = 1_000_000_000L + i * 250_000_000L;
            var phone = 7_000_000_000L + (long)(session * 1.00005);
            var (segment, discontinuity) = mapper.Add(new PhoneClockExchange(session - 200_000, phone, phone, session + 200_000));
            Assert.Equal(1, segment);
            Assert.False(discontinuity);
        }

        var target = 5_000_000_000L;
        var mapped = mapper.Map(7_000_000_000L + (long)(target * 1.00005))!;
        Assert.InRange(mapped.SessionNanoseconds, target - 2, target + 2);
        Assert.Equal(200_000, mapped.UncertaintyNanoseconds);
        Assert.Equal(1, mapped.Segment);
        Assert.Equal(40, mapper.SampleCount);
        Assert.Equal(200_000, mapper.LargestErrorBound);
    }

    [Fact]
    public void Mapper_PhoneSleep_StartsSegment()
    {
        var mapper = new PhoneClockMapper();
        mapper.Add(new PhoneClockExchange(1_000_000_000, 10_000_000_000, 10_000_000_000, 1_000_400_000));
        mapper.Add(new PhoneClockExchange(2_000_000_000, 11_000_000_000, 11_000_000_000, 2_000_400_000));
        // The phone slept for 30 s: its clock moved 1 s while the session's moved 31.
        var (segment, discontinuity) = mapper.Add(new PhoneClockExchange(33_000_000_000, 12_000_000_000, 12_000_000_000, 33_000_400_000));

        Assert.Equal(2, segment);
        Assert.True(discontinuity);
        Assert.Equal(2, mapper.SegmentCount);
        Assert.Equal(1_500_200_000, mapper.Map(10_500_000_000)!.SessionNanoseconds);
        Assert.Equal(1, mapper.Map(10_500_000_000)!.Segment);
        Assert.Equal(33_500_200_000, mapper.Map(12_500_000_000)!.SessionNanoseconds);
        Assert.Equal(2, mapper.Map(12_500_000_000)!.Segment);
    }

    [Fact]
    public void Mapper_SlowSample_IsNotADiscontinuity()
    {
        var mapper = new PhoneClockMapper();
        mapper.Add(new PhoneClockExchange(1_000_000_000, 10_000_000_000, 10_000_000_000, 1_000_400_000));
        // A slow reply: 100 ms each way, so its bound covers the difference.
        var (_, discontinuity) = mapper.Add(new PhoneClockExchange(2_000_000_000, 11_000_000_000, 11_000_000_000, 2_200_000_000));

        Assert.False(discontinuity);
        Assert.Null(new PhoneClockMapper().Map(1));
    }

    [Fact]
    public void Mapper_Uncertainty_IsLargerBoundEitherSide()
    {
        var mapper = new PhoneClockMapper();
        mapper.Add(new PhoneClockExchange(1_000_000_000, 10_000_000_000, 10_000_000_000, 1_000_200_000));
        mapper.Add(new PhoneClockExchange(2_000_000_000, 11_000_000_000, 11_000_000_000, 2_003_000_000));
        mapper.Add(new PhoneClockExchange(3_000_000_000, 12_000_000_000, 12_000_000_000, 3_000_200_000));

        Assert.Equal(1_500_000, mapper.Map(10_500_000_000)!.UncertaintyNanoseconds);
        Assert.Equal(1_500_000, mapper.Map(11_500_000_000)!.UncertaintyNanoseconds);
        Assert.Equal(100_000, mapper.Map(12_500_000_000)!.UncertaintyNanoseconds);
    }

    // The protocol

    [Fact]
    public void Protocol_ReadsEachMessage()
    {
        var hello = Assert.IsType<CompanionHello>(CompanionProtocol.Parse(
            "{\"type\":\"hello\",\"protocol\":1,\"versionName\":\"1.1.0\",\"versionCode\":685,\"androidRelease\":\"17\",\"sdk\":37,\"time\":123}"));
        Assert.Equal(new CompanionHello(1, "1.1.0", 685, "17", 37, 123), hello);
        Assert.Equal(
            new CompanionClockReply(4, 10, 12),
            CompanionProtocol.Parse("{\"type\":\"clock\",\"id\":4,\"received\":10,\"sent\":12}"));
        Assert.Equal(
            new CompanionDisplay("changed", 90, 1344, 2992, 55),
            CompanionProtocol.Parse("{\"type\":\"display\",\"reason\":\"changed\",\"rotation\":90,\"modeWidth\":1344,\"modeHeight\":2992,\"time\":55}"));
        Assert.Equal(new CompanionError("not JSON"), CompanionProtocol.Parse("{\"type\":\"error\",\"message\":\"not JSON\"}"));
        Assert.Equal(new CompanionUnknown("tree"), CompanionProtocol.Parse("{\"type\":\"tree\"}"));
        Assert.Equal("{\"type\":\"clock\",\"id\":17}\n", CompanionProtocol.ClockRequest(17));
    }

    [Fact]
    public void Protocol_ReadsAccessibilityVolume()
    {
        Assert.Equal(
            "{\"type\":\"accessibility-volume\",\"minimumPercent\":80}\n",
            CompanionProtocol.AccessibilityVolumeRequest(80));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompanionProtocol.AccessibilityVolumeRequest(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompanionProtocol.AccessibilityVolumeRequest(101));

        var raised = Assert.IsType<CompanionAccessibilityVolume>(CompanionProtocol.Parse(VolumeLine("requested", true, 5, 20, "raised", time: 77)));
        Assert.Equal("requested", raised.Reason);
        Assert.Equal(80, raised.MinimumPercent);
        Assert.True(raised.Separate);
        Assert.Equal([TalkBack], raised.Services);
        Assert.Equal((25, 5, 20, 20), (raised.Max, raised.Before, raised.Target, raised.After));
        Assert.Equal("raised", raised.Outcome);
        Assert.Null(raised.Detail);
        Assert.Equal(77, raised.Time);

        var failed = Assert.IsType<CompanionAccessibilityVolume>(CompanionProtocol.Parse(
            "{\"type\":\"accessibility-volume\",\"reason\":\"requested\",\"minimumPercent\":80,\"separate\":false,\"services\":[],\"outcome\":\"failed\",\"detail\":\"No audio service.\",\"time\":3}"));
        Assert.Null(failed.Max);
        Assert.Null(failed.After);
        Assert.Equal("No audio service.", failed.Detail);

        Assert.Throws<FormatException>(() => CompanionProtocol.Parse(
            "{\"type\":\"accessibility-volume\",\"reason\":\"requested\",\"minimumPercent\":80,\"separate\":\"yes\",\"services\":[],\"outcome\":\"failed\",\"time\":3}"));
    }

    [Theory]
    [InlineData("raised", 5, 20, "Accessibility volume raised from 5 of 25 steps (20%) to 20 of 25 steps (80%)")]
    [InlineData("already", 22, 22, "Accessibility volume already 22 of 25 steps (88%), at least the 80% asked for")]
    [InlineData("not-separate", 9, 9, "Accessibility volume not changed: no service that uses it, such as TalkBack, is on, so it is the media volume")]
    [InlineData("refused", 5, 5, "Accessibility volume could not be raised: Android left it at 5 of 25 steps (20%)")]
    public void Volume_IsDescribed(string outcome, int before, int after, string expected)
    {
        var volume = (CompanionAccessibilityVolume)CompanionProtocol.Parse(VolumeLine("requested", outcome != "not-separate", before, after, outcome));

        Assert.Equal(expected, AndroidCompanionCollector.DescribeVolume(volume));
    }

    [Fact]
    public void Protocol_RejectsBadLines()
    {
        Assert.Throws<FormatException>(() => CompanionProtocol.Parse("hello"));
        Assert.Throws<FormatException>(() => CompanionProtocol.Parse("[1]"));
        Assert.Throws<FormatException>(() => CompanionProtocol.Parse("{\"type\":\"clock\",\"id\":4}"));
        Assert.Throws<FormatException>(() => CompanionProtocol.Parse("{\"type\":\"clock\",\"id\":\"4\",\"received\":1,\"sent\":2}"));
    }

    // The connection

    [Fact]
    public async Task Connection_ReadsGreetingDisplayAndClock()
    {
        await using var app = new FakeApp();
        await using var connection = await app.ConnectAsync();

        Assert.Equal("1.1.0", connection.Hello.VersionName);
        Assert.Equal(685, connection.Hello.VersionCode);
        var display = await connection.Displays.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new CompanionDisplay("initial", 0, 1344, 2992, display.Display.Time), display.Display);

        var exchange = await connection.ExchangeClockAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(exchange.RecorderReceive >= exchange.RecorderSend);
        Assert.True(exchange.PhoneSend >= exchange.PhoneReceive);
        Assert.InRange(exchange.PhoneMidpoint - exchange.SessionMidpoint, FakeApp.PhoneOffset - 50_000_000, FakeApp.PhoneOffset + 50_000_000);
    }

    [Fact]
    public async Task Connection_NothingListening_SaysSo()
    {
        // adb accepts the forwarded connection and closes it when nothing
        // on the phone listens on the socket.
        await using var app = new FakeApp(greeting: null);

        var exception = await Assert.ThrowsAsync<CompanionUnavailableException>(() => app.ConnectAsync());

        Assert.Contains("Nothing on the phone answered", exception.Message);
    }

    [Fact]
    public async Task Connection_OtherProtocol_IsRefused()
    {
        // The companion app from before android-checks.md, step 4a.
        await using var app = new FakeApp(greeting: FakeApp.Hello(protocol: 5));

        var exception = await Assert.ThrowsAsync<CompanionUnavailableException>(() => app.ConnectAsync());

        Assert.Contains("speaks protocol 5; this recorder speaks 6", exception.Message);
    }

    [Fact]
    public async Task Link_ForwardsAPortAndRemovesIt()
    {
        await using var app = new FakeApp();
        var adb = new ScriptedAdb(arguments => string.Join(' ', arguments) switch
        {
            "-s 58191FDCQ00ABZ forward tcp:0 localabstract:a11yrecorder_companion" => new AdbResult(0, app.Port + "\n", ""),
            var command when command == $"-s 58191FDCQ00ABZ forward --remove tcp:{app.Port}" => new AdbResult(0, "", ""),
            var command => new AdbResult(1, "", "unexpected: " + command)
        });

        await using (var connection = await CompanionLink.OpenAsync(adb, Serial, CompanionLink.StopwatchNanoseconds, CancellationToken.None))
        {
            Assert.Equal(CompanionProtocol.Version, connection.Hello.Protocol);
        }

        Assert.Contains($"-s {Serial} forward --remove tcp:{app.Port}", adb.Commands);
    }

    [Fact]
    public async Task Link_ForwardFails_SaysWhy()
    {
        var adb = new ScriptedAdb(_ => new AdbResult(1, "", "adb: error: device '58191FDCQ00ABZ' not found"));

        var exception = await Assert.ThrowsAsync<CompanionUnavailableException>(
            () => CompanionLink.OpenAsync(adb, Serial, CompanionLink.StopwatchNanoseconds, CancellationToken.None));

        Assert.Contains("not found", exception.Message);
    }

    // Installation

    [Fact]
    public void InstalledVersionCode_IsTheCompanionsOnly()
    {
        Assert.Equal(685, CompanionApp.ParseInstalledVersionCode(
            "package:io.github.bobdodd.a11yrecorder.companion.test versionCode:1\npackage:io.github.bobdodd.a11yrecorder.companion versionCode:685\n"));
        Assert.Null(CompanionApp.ParseInstalledVersionCode(""));
    }

    [Fact]
    public void WithCompanion_KeepsTalkBack()
    {
        Assert.Equal(TalkBack + ":" + CompanionProtocol.ServiceComponent, CompanionApp.WithCompanion(TalkBack));
        Assert.Equal(CompanionProtocol.ServiceComponent, CompanionApp.WithCompanion(""));
        Assert.Equal(CompanionProtocol.ServiceComponent, CompanionApp.WithCompanion("null"));
        Assert.Equal(
            TalkBack + ":" + CompanionProtocol.ServiceComponent,
            CompanionApp.WithCompanion(CompanionProtocol.ServiceComponent + ":" + TalkBack));
        Assert.True(CompanionApp.IsOn(TalkBack + ":" + CompanionProtocol.ServiceComponent));
        Assert.False(CompanionApp.IsOn(TalkBack));
    }

    [Fact]
    public async Task InstallAndTurnOn_KeepsOtherServices()
    {
        var services = TalkBack;
        var adb = new ScriptedAdb(arguments =>
        {
            var command = string.Join(' ', arguments);
            if (command == $"-s {Serial} install -r C:\\app.apk")
            {
                return new AdbResult(0, "Performing Streamed Install\nSuccess\n", "");
            }

            if (command == $"-s {Serial} shell settings get secure enabled_accessibility_services")
            {
                return new AdbResult(0, services + "\n", "");
            }

            if (command.StartsWith($"-s {Serial} shell settings put secure enabled_accessibility_services '", StringComparison.Ordinal))
            {
                services = command.Split('\'')[1];
                return new AdbResult(0, "", "");
            }

            return new AdbResult(1, "", "unexpected: " + command);
        });

        var result = await CompanionApp.InstallAndTurnOnAsync(adb, Serial, "C:\\app.apk", CancellationToken.None);

        Assert.True(result.Succeeded, result.Text);
        Assert.Equal(TalkBack + ":" + CompanionProtocol.ServiceComponent, services);
        Assert.Contains(
            $"-s {Serial} shell settings put secure enabled_accessibility_services '{TalkBack}:{CompanionProtocol.ServiceComponent}' && settings put secure accessibility_enabled 1",
            adb.Commands);
    }

    [Fact]
    public async Task Install_OtherSigningKey_RemovesThenInstalls()
    {
        var installs = 0;
        var adb = new ScriptedAdb(arguments =>
        {
            var command = string.Join(' ', arguments);
            if (command.StartsWith($"-s {Serial} install -r", StringComparison.Ordinal))
            {
                installs++;
                return installs == 1
                    ? new AdbResult(1, "Performing Streamed Install\n", "adb: failed to install C:\\app.apk: Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: Existing package io.github.bobdodd.a11yrecorder.companion signatures do not match newer version; ignoring!]\n")
                    : new AdbResult(0, "Success\n", "");
            }

            return command switch
            {
                "-s 58191FDCQ00ABZ uninstall io.github.bobdodd.a11yrecorder.companion" => new AdbResult(0, "Success\n", ""),
                "-s 58191FDCQ00ABZ shell settings get secure enabled_accessibility_services" => new AdbResult(0, CompanionProtocol.ServiceComponent + "\n", ""),
                _ => new AdbResult(0, "", "")
            };
        });

        var result = await CompanionApp.InstallAndTurnOnAsync(adb, Serial, "C:\\app.apk", CancellationToken.None);

        Assert.True(result.Succeeded, result.Text);
        Assert.Equal(2, installs);
        Assert.Contains($"-s {Serial} uninstall {AndroidTools.CompanionPackage}", adb.Commands);
    }

    [Fact]
    public async Task Install_Failure_IsReported()
    {
        var adb = new ScriptedAdb(_ => new AdbResult(1, "", "adb: failed to install C:\\app.apk: Failure [INSTALL_FAILED_USER_RESTRICTED: Install canceled by user]"));

        var result = await CompanionApp.InstallAndTurnOnAsync(adb, Serial, "C:\\app.apk", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("INSTALL_FAILED_USER_RESTRICTED", result.Text);
    }

    // The checks panel's companion line

    [Theory]
    [InlineData("", TalkBack, "Companion app: not installed. Choose \"Install companion app\".", CompanionAction.Install)]
    [InlineData("package:io.github.bobdodd.a11yrecorder.companion versionCode:600", TalkBack, "Companion app: version code 600 is installed, older than the recorder's 685.", CompanionAction.Install)]
    [InlineData("package:io.github.bobdodd.a11yrecorder.companion versionCode:685", TalkBack, "Companion app: installed (version code 685), but off. Choose \"Turn on companion app\".", CompanionAction.TurnOn)]
    [InlineData("package:io.github.bobdodd.a11yrecorder.companion versionCode:685", TalkBack + ":" + CompanionProtocol.ServiceComponent, "Companion app: installed, on, and answering (version 1.1.0, version code 685, protocol 1).", CompanionAction.None)]
    public async Task Checks_ReportTheCompanion(string listed, string services, string expected, CompanionAction action)
    {
        var adb = PhoneAdb(listed, services);
        var checks = new AndroidDeviceChecks(adb, new NoServer(), _ => true, Bundled(), new FixedProbe());

        var report = await checks.CheckAsync(CancellationToken.None);

        Assert.Contains(expected, report.Text);
        Assert.Equal(action, report.Companion);
        Assert.Equal(Serial, report.Serial);
        Assert.Equal(action == CompanionAction.None, report.CompanionReady);
        Assert.Contains("TalkBack: on.", report.Text);
    }

    [Fact]
    public async Task Checks_OnButNotAnswering_OffersInstall_AndAsksOnce()
    {
        var adb = PhoneAdb("package:io.github.bobdodd.a11yrecorder.companion versionCode:685", CompanionProtocol.ServiceComponent);
        var probe = new FixedProbe(new CompanionUnavailableException("Nothing on the phone answered."));
        var checks = new AndroidDeviceChecks(adb, new NoServer(), _ => true, Bundled(), probe);

        var report = await checks.CheckAsync(CancellationToken.None);
        await checks.CheckAsync(CancellationToken.None);

        Assert.Contains("Companion app: installed (version code 685) and on, but not answering: Nothing on the phone answered.", report.Text);
        Assert.Equal(CompanionAction.Install, report.Companion);
        Assert.False(report.CompanionReady);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task Checks_RecordersCopyMissing_OffersNothing()
    {
        var adb = PhoneAdb("", TalkBack);
        var bundled = new BundledCompanion("C:\\missing.apk", "", null, "The companion app is missing. Install the recorder again.");
        var checks = new AndroidDeviceChecks(adb, new NoServer(), _ => true, bundled, new FixedProbe());

        var report = await checks.CheckAsync(CancellationToken.None);

        Assert.Contains("Companion app: not installed, and the recorder's own copy cannot be used. The companion app is missing.", report.Text);
        Assert.Equal(CompanionAction.None, report.Companion);
    }

    // The collector

    [Fact]
    public async Task Collector_RecordsConnectionClockAndDisplay()
    {
        await using var app = new FakeApp();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidCompanionCollector(
            new ScriptedAdb(_ => new AdbResult(1, "", "")),
            Serial,
            (now, cancellation) => app.ConnectAsync(now));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.Mapper.SampleCount >= 3);
        await app.SendAsync("{\"type\":\"display\",\"reason\":\"changed\",\"rotation\":90,\"modeWidth\":1344,\"modeHeight\":2992,\"time\":" +
            (CompanionLink.StopwatchNanoseconds() + FakeApp.PhoneOffset) + "}");
        await WaitUntilAsync(() => sink.Records.Count(record => record.Channel == AndroidCompanionRecords.DisplayChannel) >= 2);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        Assert.True(
            System.Text.RegularExpressions.Regex.IsMatch(collector.Summary!, @"^Companion app 1\.1\.0: \d+ clock samples, largest error bound \d+\.\d ms, screen rotated 90 degrees, 1344 by 2992\.$"),
            collector.Summary);
        var records = sink.Records.ToList();
        var validator = new EventRecordValidator("session-1");
        foreach (var record in records)
        {
            Assert.Empty(validator.Validate(record));
        }

        var connected = Assert.Single(records, record => record.EventType == AndroidCompanionRecords.ConnectedEventType);
        Assert.Equal(685, connected.Payload.GetProperty("versionCode").GetInt64());
        Assert.Contains("receipt-time-stamp", connected.QualityFlags);

        var samples = records.Where(record => record.EventType == AndroidCompanionRecords.ClockSampleEventType).ToList();
        Assert.True(samples.Count >= 3);
        Assert.All(samples, sample => Assert.Equal(AndroidCompanionRecords.ClockDomain, sample.NativeTimestamp!.Domain));

        var displays = records.Where(record => record.Channel == AndroidCompanionRecords.DisplayChannel).ToList();
        Assert.Equal(["initial", "changed"], displays.Select(record => record.Payload.GetProperty("reason").GetString()));
        Assert.All(displays, display => Assert.Equal(AndroidCompanionRecords.FittedClockMapping, display.ClockMappingId));
        var turned = displays[1];
        Assert.Equal(90, turned.Payload.GetProperty("rotation").GetInt32());
        // The fit puts the phone time back on the session clock, within
        // the sample's bound and the test's own scheduling.
        var expected = turned.NativeTimestamp!.Value - FakeApp.PhoneOffset - clock.OriginNanoseconds();
        Assert.InRange(turned.MonotonicNanoseconds, expected - 50_000_000, expected + 50_000_000);
    }

    [Fact]
    public async Task Collector_AsksForAccessibilityEvents_AndPassesThemOn()
    {
        await using var app = new FakeApp();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidCompanionCollector(
            new ScriptedAdb(_ => new AdbResult(1, "", "")),
            Serial,
            (now, cancellation) => app.ConnectAsync(now));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        Assert.Null(await collector.RequestAccessibilityEventsAsync(CancellationToken.None));

        await collector.StartAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.Mapper.SampleCount >= 1);
        var events = await collector.RequestAccessibilityEventsAsync(CancellationToken.None);
        Assert.NotNull(events);
        await app.EventsAsked.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await app.SendAsync(AndroidAccessibilityTests.EventLine(index: 1));
        await app.SendAsync("{\"type\":\"accessibility-event\",\"index\":\"two\"}");
        await app.SendAsync(AndroidAccessibilityTests.EventLine(index: 3));
        var first = await events.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var second = await events.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var third = await events.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        // An event the recorder cannot read does not end the connection.
        Assert.Equal(1, first.Event!.Index);
        Assert.Null(second.Event);
        Assert.NotNull(second.Problem);
        Assert.Equal(3, third.Event!.Index);
        Assert.True(third.ReceivedAt >= first.ReceivedAt);
        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);

        // The reader completes when the connection ends.
        await events.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Null(await collector.RequestAccessibilityEventsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Collector_AsksForAccessibilityTrees_AndPassesThemOn()
    {
        await using var app = new FakeApp();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidCompanionCollector(
            new ScriptedAdb(_ => new AdbResult(1, "", "")),
            Serial,
            (now, cancellation) => app.ConnectAsync(now));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        Assert.Null(await collector.RequestAccessibilityTreesAsync(CancellationToken.None));

        await collector.StartAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.Mapper.SampleCount >= 1);
        var trees = await collector.RequestAccessibilityTreesAsync(CancellationToken.None);
        Assert.NotNull(trees);
        await app.TreesAsked.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await app.SendAsync(AndroidAccessibilityTreeTests.TreeLine(index: 1));
        // Not read until the tree collector reads it, so a bad one does not
        // end the connection either.
        await app.SendAsync("{\"type\":\"accessibility-tree\",\"index\":\"two\"}");
        await app.SendAsync(AndroidAccessibilityTreeTests.TreeLine(index: 3));
        var first = await trees.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var second = await trees.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var third = await trees.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(1, CompanionProtocol.ReadTree(first.Line).Tree!.Index);
        Assert.NotNull(CompanionProtocol.ReadTree(second.Line).Problem);
        Assert.Equal(3, CompanionProtocol.ReadTree(third.Line).Tree!.Index);
        Assert.True(third.ReceivedAt >= first.ReceivedAt);
        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);

        // The reader completes when the connection ends.
        await trees.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Null(await collector.RequestAccessibilityTreesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Collector_AsksForSettingChanges_AndPassesThemOn()
    {
        await using var app = new FakeApp();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidCompanionCollector(
            new ScriptedAdb(_ => new AdbResult(1, "", "")),
            Serial,
            (now, cancellation) => app.ConnectAsync(now));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        Assert.Null(await collector.RequestSettingChangesAsync(AndroidSettingsRecords.All, CancellationToken.None));

        await collector.StartAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.Mapper.SampleCount >= 1);
        var changes = await collector.RequestSettingChangesAsync(AndroidSettingsRecords.All, CancellationToken.None);
        Assert.NotNull(changes);
        var request = await app.SettingsAsked.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        using (var asked = JsonDocument.Parse(request))
        {
            var settings = asked.RootElement.GetProperty("settings").EnumerateArray().ToList();
            Assert.Equal(AndroidSettingsRecords.All.Count, settings.Count);
            Assert.Contains(settings, setting =>
                setting.GetProperty("namespace").GetString() == "secure" &&
                setting.GetProperty("name").GetString() == "accessibility_display_inversion_enabled");
        }

        await app.SendAsync("{\"type\":\"setting-changed\",\"namespace\":\"secure\",\"name\":\"accessibility_display_inversion_enabled\",\"time\":42}");
        var first = await changes.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(new CompanionSettingChanged("secure", "accessibility_display_inversion_enabled", 42), first.Change);
        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        await changes.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Null(await collector.RequestSettingChangesAsync(AndroidSettingsRecords.All, CancellationToken.None));
    }

    [Fact]
    public async Task Collector_AsksForAccessibilityVolume_AndRecordsEachAnswer()
    {
        await using var app = new FakeApp(volumeReply: percent =>
            VolumeLine("requested", true, 5, 20, "raised", percent: percent, time: CompanionLink.StopwatchNanoseconds() + FakeApp.PhoneOffset));
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidCompanionCollector(
            new ScriptedAdb(_ => new AdbResult(1, "", "")),
            Serial,
            (now, _) => app.ConnectAsync(now),
            AndroidCompanionCollector.AccessibilityVolumePercent);
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);
        // Asked only when the sound collector says the capture has begun, and once.
        await WaitUntilAsync(() => collector.Mapper.SampleCount >= 2);
        Assert.Empty(app.VolumeRequests);
        collector.RaiseAccessibilityVolume();
        collector.RaiseAccessibilityVolume();
        await WaitUntilAsync(() => Volumes(sink).Count >= 1);
        Assert.EndsWith(", Accessibility volume raised from 5 of 25 steps (20%) to 20 of 25 steps (80%).", collector.Summary);
        // TalkBack is turned off during the recording.
        await app.SendAsync(VolumeLine("services-changed", false, 20, 20, "not-separate", time: CompanionLink.StopwatchNanoseconds() + FakeApp.PhoneOffset));
        await WaitUntilAsync(() => Volumes(sink).Count >= 2);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal([80], app.VolumeRequests);
        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        Assert.Contains("Accessibility volume not changed: no service that uses it", collector.Summary);
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
        var volumes = Volumes(sink);
        Assert.Equal(["requested", "services-changed"], volumes.Select(record => record.Payload.GetProperty("reason").GetString()));
        Assert.Equal(["raised", "not-separate"], volumes.Select(record => record.Payload.GetProperty("outcome").GetString()));
        Assert.All(volumes, volume => Assert.Equal(AndroidCompanionRecords.ClockDomain, volume.NativeTimestamp!.Domain));
        Assert.Equal(20, volumes[0].Payload.GetProperty("after").GetInt32());
        Assert.Equal(TalkBack, volumes[0].Payload.GetProperty("services")[0].GetString());
    }

    [Fact]
    public async Task Collector_WithoutAPercent_DoesNotAsk()
    {
        await using var app = new FakeApp();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidCompanionCollector(
            new ScriptedAdb(_ => new AdbResult(1, "", "")),
            Serial,
            (now, _) => app.ConnectAsync(now));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);

        await collector.StartAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.Mapper.SampleCount >= 2);
        collector.RaiseAccessibilityVolume();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Empty(app.VolumeRequests);
        Assert.Empty(Volumes(sink));
        Assert.DoesNotContain("Accessibility volume", collector.Summary);
    }

    [Theory]
    [InlineData("{\"reason\":\"requested\",\"minimumPercent\":80,\"separate\":true,\"services\":[\"a/b\"],\"max\":25,\"before\":20,\"target\":20,\"after\":20,\"outcome\":\"raised\",\"detail\":null}", "android-volume-order")]
    [InlineData("{\"reason\":\"requested\",\"minimumPercent\":80,\"separate\":true,\"services\":[\"a/b\"],\"max\":25,\"before\":5,\"target\":30,\"after\":5,\"outcome\":\"refused\",\"detail\":null}", "android-volume-order")]
    [InlineData("{\"reason\":\"requested\",\"minimumPercent\":80,\"separate\":true,\"services\":[\"a/b\"],\"max\":null,\"before\":null,\"target\":null,\"after\":null,\"outcome\":\"already\",\"detail\":null}", "android-volume-steps")]
    [InlineData("{\"reason\":\"requested\",\"minimumPercent\":180,\"separate\":true,\"services\":[\"a/b\"],\"max\":25,\"before\":22,\"target\":20,\"after\":22,\"outcome\":\"already\",\"detail\":null}", "android-volume-percent")]
    public async Task VolumeRecord_BadValues_AreRejected(string payload, string code)
    {
        await using var collector = new AndroidCompanionCollector(new ScriptedAdb(_ => new AdbResult(1, "", "")), Serial);
        var record = RecorderEventFactory.Create(
            "session-1",
            collector.Descriptor,
            AndroidCompanionRecords.CompanionChannel,
            1,
            1_000,
            AndroidCompanionRecords.AccessibilityVolumeEventType,
            JsonDocument.Parse(payload).RootElement,
            []);

        Assert.Contains(new EventRecordValidator("session-1").Validate(record), issue => issue.Code == code);
    }

    private static List<RecorderEvent> Volumes(ListSink sink) =>
        [.. sink.Records.Where(record => record.EventType == AndroidCompanionRecords.AccessibilityVolumeEventType)];

    /// <summary>An Accessibility volume answer with 25 steps and a target of 20.</summary>
    private static string VolumeLine(string reason, bool separate, int before, int after, string outcome, int percent = 80, long time = 1) =>
        $"{{\"type\":\"accessibility-volume\",\"reason\":\"{reason}\",\"minimumPercent\":{percent},\"separate\":{(separate ? "true" : "false")}," +
        $"\"services\":[{(separate ? "\"" + TalkBack + "\"" : "")}],\"max\":25,\"before\":{before},\"target\":20,\"after\":{after},\"outcome\":\"{outcome}\",\"time\":{time}}}";

    [Fact]
    public async Task Collector_CompanionUnreachable_RecordsWhyAndGoesOn()
    {
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidCompanionCollector(
            new ScriptedAdb(_ => new AdbResult(1, "", "")),
            Serial,
            (_, _) => throw new CompanionUnavailableException("Nothing on the phone answered."));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);

        var started = await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        var stopped = await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(started.Accepted);
        Assert.True(stopped.Accepted);
        Assert.Equal(CollectorHealthState.Degraded, collector.HealthState);
        Assert.Contains("could not be reached: Nothing on the phone answered.", collector.HealthReason);
        var unavailable = Assert.Single(sink.Records, record => record.EventType == AndroidCompanionRecords.UnavailableEventType);
        Assert.False(unavailable.Payload.GetProperty("connectionLost").GetBoolean());
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
    }

    [Fact]
    public async Task Collector_ConnectionLost_RecordsIt()
    {
        await using var app = new FakeApp();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidCompanionCollector(
            new ScriptedAdb(_ => new AdbResult(1, "", "")),
            Serial,
            (now, _) => app.ConnectAsync(now));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.Mapper.SampleCount >= 1);

        await app.DropAsync();
        await WaitUntilAsync(() => collector.HealthState == CollectorHealthState.Degraded);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        var lost = Assert.Single(sink.Records, record => record.EventType == AndroidCompanionRecords.UnavailableEventType);
        Assert.True(lost.Payload.GetProperty("connectionLost").GetBoolean());
        Assert.Contains("ended during the recording", collector.HealthReason);
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "Timed out.");
            await Task.Delay(20);
        }
    }

    private static BundledCompanion Bundled() => new("C:\\app.apk", "1.1.0", 685, null);

    private static ScriptedAdb PhoneAdb(string listed, string services) =>
        new(arguments =>
        {
            var command = string.Join(' ', arguments);
            if (command == "version")
            {
                return new AdbResult(0, "Android Debug Bridge version 1.0.41\nVersion 37.0.1-14710014\n", "");
            }

            if (command == "devices -l")
            {
                return new AdbResult(0, $"List of devices attached\n{Serial}         device usb:1-4 product:mustang model:Pixel_10_Pro_XL device:mustang transport_id:1\n\n", "");
            }

            if (command == $"-s {Serial} shell {AndroidDeviceChecks.DetailsScript}")
            {
                return new AdbResult(
                    0,
                    $"@model\nPixel 10 Pro XL\n@release\n17\n@build\nCP3A.260905.009\n@patch\n2026-09-05\n@size\nPhysical size: 1080x2404\n@services\n{services}\n@companion\n{listed}\n@end\n",
                    "");
            }

            return new AdbResult(1, "", "unexpected: " + command);
        });

    /// <summary>A fake adb that answers each command with what the test gives.</summary>
    private sealed class ScriptedAdb(Func<IReadOnlyList<string>, AdbResult> answer) : IAdb
    {
        public string Path => "C:\\adb.exe";

        public List<string> Commands { get; } = [];

        public Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation)
        {
            lock (Commands)
            {
                Commands.Add(string.Join(' ', arguments));
            }

            return Task.FromResult(answer(arguments));
        }

        public Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation) => Task.FromResult(0);
    }

    private sealed class NoServer : IAdbServerProbe
    {
        public Task<AdbServerInfo?> ReadAsync(CancellationToken cancellation) => Task.FromResult<AdbServerInfo?>(null);
    }

    private sealed class FixedProbe(CompanionUnavailableException? failure = null) : ICompanionProbe
    {
        public int Calls { get; private set; }

        public Task<CompanionHello> HelloAsync(string serial, CancellationToken cancellation)
        {
            Calls++;
            return failure is null
                ? Task.FromResult(new CompanionHello(1, "1.1.0", 685, "17", 37, 1))
                : Task.FromException<CompanionHello>(failure);
        }
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

    /// <summary>
    /// The companion app's side of the protocol on a loopback port, with a
    /// phone clock <see cref="PhoneOffset"/> ahead of the stopwatch.
    /// </summary>
    private sealed class FakeApp : IAsyncDisposable
    {
        public const long PhoneOffset = 9_000_000_000_000;

        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly string? _greeting;
        private readonly Func<int, string>? _volumeReply;
        private readonly List<int> _volumeRequests = [];
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource<NetworkStream> _client = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _write = new(1, 1);
        private readonly TaskCompletionSource _eventsAsked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _treesAsked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _settingsAsked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _serving;

        public FakeApp(string? greeting = "default", Func<int, string>? volumeReply = null)
        {
            _greeting = greeting == "default" ? Hello() : greeting;
            _volumeReply = volumeReply;
            _listener.Start();
            _serving = Task.Run(ServeAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>Completes when the recorder asks for the accessibility events.</summary>
        public Task EventsAsked => _eventsAsked.Task;

        /// <summary>Completes when the recorder asks for the tree snapshots.</summary>
        public Task TreesAsked => _treesAsked.Task;

        /// <summary>Completes with the request when the recorder asks for notices of settings changes.</summary>
        public Task<string> SettingsAsked => _settingsAsked.Task;

        /// <summary>The minimum percents the recorder asked for, in order.</summary>
        public IReadOnlyList<int> VolumeRequests
        {
            get
            {
                lock (_volumeRequests)
                {
                    return [.. _volumeRequests];
                }
            }
        }

        public static string Hello(int protocol = CompanionProtocol.Version) =>
            $"{{\"type\":\"hello\",\"protocol\":{protocol},\"versionName\":\"1.1.0\",\"versionCode\":685,\"androidRelease\":\"17\",\"sdk\":37,\"time\":{Now()}}}";

        public async Task<CompanionConnection> ConnectAsync(Func<long>? now = null)
        {
            var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(IPAddress.Loopback, Port);
            return await CompanionConnection.ConnectAsync(
                client.GetStream(),
                now ?? CompanionLink.StopwatchNanoseconds,
                TimeSpan.FromSeconds(5),
                CancellationToken.None,
                () =>
                {
                    client.Dispose();
                    return ValueTask.CompletedTask;
                });
        }

        public async Task SendAsync(string line)
        {
            var stream = await _client.Task;
            await WriteAsync(stream, line);
        }

        public async Task DropAsync() => (await _client.Task).Socket.Close();

        private static long Now() => CompanionLink.StopwatchNanoseconds() + PhoneOffset;

        private async Task WriteAsync(NetworkStream stream, string line)
        {
            await _write.WaitAsync();
            try
            {
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                await stream.WriteAsync(bytes);
            }
            finally
            {
                _write.Release();
            }
        }

        private async Task ServeAsync()
        {
            try
            {
                using var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                var stream = socket.GetStream();
                if (_greeting is null)
                {
                    return;
                }

                await WriteAsync(stream, _greeting);
                await WriteAsync(stream, $"{{\"type\":\"display\",\"reason\":\"initial\",\"rotation\":0,\"modeWidth\":1344,\"modeHeight\":2992,\"time\":{Now()}}}");
                _client.TrySetResult(stream);
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    var received = Now();
                    using var request = JsonDocument.Parse(line);
                    if (request.RootElement.GetProperty("type").GetString() == "accessibility-volume")
                    {
                        var percent = request.RootElement.GetProperty("minimumPercent").GetInt32();
                        lock (_volumeRequests)
                        {
                            _volumeRequests.Add(percent);
                        }

                        if (_volumeReply is not null)
                        {
                            await WriteAsync(stream, _volumeReply(percent));
                        }

                        continue;
                    }

                    if (request.RootElement.GetProperty("type").GetString() == "accessibility-events")
                    {
                        _eventsAsked.TrySetResult();
                        continue;
                    }

                    if (request.RootElement.GetProperty("type").GetString() == "accessibility-trees")
                    {
                        _treesAsked.TrySetResult();
                        continue;
                    }

                    if (request.RootElement.GetProperty("type").GetString() == "settings")
                    {
                        _settingsAsked.TrySetResult(request.RootElement.GetRawText());
                        continue;
                    }

                    var id = request.RootElement.GetProperty("id").GetInt64();
                    await WriteAsync(stream, $"{{\"type\":\"clock\",\"id\":{id},\"received\":{received},\"sent\":{Now()}}}");
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                // The test ended or dropped the connection.
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            if (_serving is not null)
            {
                await _serving;
            }

            _stop.Dispose();
        }
    }
}

internal static class SessionClockTestExtensions
{
    /// <summary>The session clock's origin on the stopwatch, in nanoseconds, as <see cref="CompanionLink.StopwatchNanoseconds"/> counts.</summary>
    public static long OriginNanoseconds(this ISessionClock clock) =>
        (long)(clock.OriginTimestamp * (1_000_000_000.0 / clock.Frequency));
}

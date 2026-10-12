using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The mouse hook (input.mouse-hook): the message, wheel, and pen or touch
/// rules; finding that the hook was removed, from raw input's buttons; the
/// payload rules; NVDA's [mouse] settings; and the player's outcomes and
/// rows, including through the playback index. See
/// docs/architecture/screen-reader-activity.md, "The mouse hook".
/// </summary>
public sealed class MouseHookTests
{
    private const long Millisecond = 1_000_000;
    private const long Second = 1_000_000_000;
    private const int NvdaProcess = 20008;

    // --- The contract -----------------------------------------------------------

    [Theory]
    [InlineData(0x0200, 0, "move", null)]
    [InlineData(0x0201, 0, "button-down", "left")]
    [InlineData(0x0205, 0, "button-up", "right")]
    [InlineData(0x0207, 0, "button-down", "middle")]
    [InlineData(0x020B, 0x00010000, "button-down", "x1")]
    [InlineData(0x020C, 0x00020000, "button-up", "x2")]
    [InlineData(0x020C, 0, "button-up", null)]
    [InlineData(0x020A, 0x00780000, "wheel", null)]
    [InlineData(0x020E, 0, "horizontal-wheel", null)]
    [InlineData(0x0300, 0, "other", null)]
    public void AMessageHasItsKindAndButton(int message, int mouseData, string kind, string? button) =>
        Assert.Equal((kind, button), MouseHookRecords.Describe(message, mouseData));

    [Fact]
    public void AWheelsDeltaIsTheSignedHighWord()
    {
        Assert.Equal(120, MouseHookRecords.WheelDelta("wheel", 0x00780000));
        Assert.Equal(-120, MouseHookRecords.WheelDelta("wheel", unchecked((int)0xFF880000)));
        Assert.Equal(-240, MouseHookRecords.WheelDelta("horizontal-wheel", unchecked((int)0xFF100000)));
        Assert.Null(MouseHookRecords.WheelDelta("move", 0x00780000));
    }

    [Fact]
    public void ThePenOrTouchSignatureIsReadUnderItsMask()
    {
        Assert.Equal("touch", MouseHookRecords.PenOrTouch(0xFF515780));
        Assert.Equal("pen", MouseHookRecords.PenOrTouch(0xFF515700));
        Assert.Equal("pen", MouseHookRecords.PenOrTouch(0xFF51571F));
        Assert.Null(MouseHookRecords.PenOrTouch(0));
        Assert.Null(MouseHookRecords.PenOrTouch(0xFF515600));
    }

    [Fact]
    public void RawButtonsAreMatchedWithLeftAndRightAsOne()
    {
        // Left down and right up in one raw record, then the X buttons.
        Assert.Equal(
            [(MouseHookRecords.LeftOrRightButton, false), (MouseHookRecords.LeftOrRightButton, true)],
            MouseHookRecords.RawButtons(0x0001 | 0x0008));
        Assert.Equal(
            [(MouseHookRecords.X1Button, false), (MouseHookRecords.X2Button, true)],
            MouseHookRecords.RawButtons(0x0040 | 0x0200));
        Assert.Empty(MouseHookRecords.RawButtons(0x0400));
        Assert.Equal((MouseHookRecords.LeftOrRightButton, false), MouseHookRecords.HookButton("button-down", "right"));
        Assert.Null(MouseHookRecords.HookButton("move", null));
        Assert.Null(MouseHookRecords.HookButton("button-up", null));
    }

    // --- Finding the hook lost --------------------------------------------------

    private static HookLossDetector Detector() =>
        new(KeyboardHookRecords.JoinWindowMilliseconds * Millisecond);

    [Fact]
    public void AButtonInBothRecordsIsNotALossEvenWithTheButtonsSwapped()
    {
        // Raw input gives the physical left button; with the buttons swapped
        // the hook may give it as the right one. They are one for the check.
        var detector = Detector();
        var left = MouseHookRecords.RawButtons(0x0001).Single();
        detector.ObserveRaw(101 * Millisecond, left.Button, 0, left.Up, fromDevice: true);
        var right = MouseHookRecords.HookButton("button-down", "right")!.Value;
        detector.ObserveHook(100 * Millisecond, right.Button, right.Up, injected: false);
        Assert.Null(detector.Poll(1000 * Millisecond));
    }

    [Fact]
    public void ARawButtonFromAMouseWithNoHookCallIsALoss()
    {
        var detector = Detector();
        detector.ObserveHook(100 * Millisecond, MouseHookRecords.MiddleButton, up: false, injected: false);
        detector.ObserveRaw(101 * Millisecond, MouseHookRecords.MiddleButton, 0, up: false, fromDevice: true);
        detector.ObserveRaw(2000 * Millisecond, MouseHookRecords.LeftOrRightButton, 0, up: false, fromDevice: true);
        var loss = detector.Poll(3000 * Millisecond);
        Assert.NotNull(loss);
        Assert.Equal(2000 * Millisecond, loss.UnmatchedRawKeyAt);
        Assert.Equal(MouseHookRecords.LeftOrRightButton, loss.UnmatchedScanCode);
        Assert.Equal(100 * Millisecond, loss.LastHookKeyAt);
    }

    [Fact]
    public void AnInjectedClickIsNotChecked()
    {
        // Raw input records injected input with no device.
        var detector = Detector();
        detector.ObserveHook(100 * Millisecond, MouseHookRecords.LeftOrRightButton, up: false, injected: true);
        detector.ObserveRaw(101 * Millisecond, MouseHookRecords.LeftOrRightButton, 0, up: false, fromDevice: false);
        Assert.Null(detector.Poll(1000 * Millisecond));
    }

    // --- The payload rules ------------------------------------------------------

    private static List<EventValidationIssue> Validate(string eventType, string json)
    {
        using var document = JsonDocument.Parse(json.Replace('\'', '"'));
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(MouseHookRecords.Channel, eventType, document.RootElement, 2000, issues);
        return issues;
    }

    private const string Move =
        "{'installation':1,'message':512,'kind':'move','button':null,'x':640,'y':-12,'mouseData':0,'wheelDelta':null,'flags':0,'injected':false,'lowerIntegrityInjected':false,'penOrTouch':null,'extraInformation':0,'eventTimeMilliseconds':5}";

    private const string Installed =
        "{'installation':2,'reason':'refresh','installed':true,'problem':null,'previousInstallation':1,'previousEvents':30,'previousMaxCallbackMicroseconds':12.5,'eventsDropped':0,'lastHookEventAt':null,'unmatchedRawButtonAt':null,'unmatchedButton':null}";

    [Fact]
    public void AnEventAndAnInstallationPass()
    {
        Assert.Empty(Validate(MouseHookRecords.EventType, Move));
        Assert.Empty(Validate(MouseHookRecords.InstalledEventType, Installed));
        Assert.Empty(Validate(MouseHookRecords.EventType,
            "{'installation':1,'message':522,'kind':'wheel','button':null,'x':1,'y':2,'mouseData':-7864320,'wheelDelta':-120,'flags':3,'injected':true,'lowerIntegrityInjected':true,'penOrTouch':null,'extraInformation':0,'eventTimeMilliseconds':5}"));
        Assert.Empty(Validate(MouseHookRecords.EventType,
            "{'installation':1,'message':513,'kind':'button-down','button':'left','x':1,'y':2,'mouseData':0,'wheelDelta':null,'flags':0,'injected':false,'lowerIntegrityInjected':false,'penOrTouch':'touch','extraInformation':4283520896,'eventTimeMilliseconds':5}"));
    }

    [Theory]
    [InlineData("'kind':'move','button':null", "'kind':'button-down','button':null", "hook-mouse-message-inconsistent")]
    [InlineData("'button':null", "'button':'left'", "hook-mouse-message-inconsistent")]
    [InlineData("'wheelDelta':null", "'wheelDelta':120", "hook-mouse-message-inconsistent")]
    [InlineData("'injected':false", "'injected':true", "hook-mouse-flag-inconsistent")]
    [InlineData("'penOrTouch':null", "'penOrTouch':'pen'", "hook-mouse-flag-inconsistent")]
    public void AnEventThatContradictsItselfIsRejected(string from, string to, string code)
    {
        var issues = Validate(MouseHookRecords.EventType, Move.Replace(from, to, StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Code == code);
    }

    [Fact]
    public void AnInstallationNamesTheRawButtonOfALossAndOnlyThen()
    {
        Assert.Contains(
            Validate(MouseHookRecords.InstalledEventType, Installed.Replace("'refresh'", "'hook-lost'", StringComparison.Ordinal)),
            issue => issue.Code == "hook-installed-loss-inconsistent");
        Assert.Empty(Validate(MouseHookRecords.InstalledEventType, Installed
            .Replace("'refresh'", "'hook-lost'", StringComparison.Ordinal)
            .Replace("'unmatchedRawButtonAt':null,'unmatchedButton':null", "'unmatchedRawButtonAt':1500,'unmatchedButton':1", StringComparison.Ordinal)));
        Assert.Empty(Validate(MouseHookRecords.InstalledEventType, Installed.Replace("'refresh'", "'keyboard-hook-lost'", StringComparison.Ordinal)));
        Assert.Contains(
            Validate(MouseHookRecords.InstalledEventType, Installed.Replace("'installed':true", "'installed':false", StringComparison.Ordinal)),
            issue => issue.Code == "hook-installed-problem-inconsistent");
    }

    [Fact]
    public void TheKeyboardHookHasAReasonForALostMouseHook()
    {
        using var document = JsonDocument.Parse(
            "{'installation':3,'reason':'mouse-hook-lost','installed':true,'problem':null,'previousInstallation':2,'previousKeys':3,'previousMaxCallbackMicroseconds':12.5,'keysDropped':0,'lastHookKeyAt':null,'unmatchedRawKeyAt':null,'unmatchedScanCode':null}"
                .Replace('\'', '"'));
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(KeyboardHookRecords.Channel, KeyboardHookRecords.InstalledEventType, document.RootElement, 2000, issues);
        Assert.Empty(issues);
    }

    // --- NVDA's settings --------------------------------------------------------

    [Fact]
    public void NvdasMouseSettingsAreReadAsWritten()
    {
        var folder = Directory.CreateTempSubdirectory("nvda-mouse-").FullName;
        try
        {
            File.WriteAllLines(Path.Combine(folder, "nvda.ini"),
            [
                "[keyboard]",
                "    NVDAModifierKeys = 7",
                "[mouse]",
                "    ignoreInjectedMouseInput = True",
                "    mouseTextUnit = line"
            ]);
            var reading = NvdaSettings.Read(folder);
            Assert.Equal("True", reading.IgnoreInjectedMouseInput);
            Assert.Equal("line", reading.MouseTextUnit);
            Assert.Null(reading.EnableMouseTracking);
            Assert.Equal("7", reading.NvdaModifierKeys);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData("True", false, true)]
    [InlineData("yes", false, true)]
    [InlineData("OFF", true, false)]
    [InlineData("0", true, false)]
    [InlineData("maybe", true, null)]
    public void ABooleanSettingIsReadAsConfigobjReadsIt(string? written, bool defaultValue, bool? expected) =>
        Assert.Equal(expected, NvdaSettings.Boolean(written, defaultValue));

    [Fact]
    public void ASettingsRecordWithoutTheMouseSettingsStillPasses()
    {
        using var document = JsonDocument.Parse(
            "{'product':'NVDA','processId':6692,'copy':'unknown','configFolder':null,'read':false,'problem':'x','keyboardLayout':null,'nvdaModifierKeys':null,'multiPressTimeout':null,'autoPassThroughOnFocusChange':null,'autoPassThroughOnCaretMove':null,'trapNonCommandGestures':null,'enableOnPageLoad':null,'profiles':[],'profileTriggers':false,'customGestures':[],'gesturesProblem':null}"
                .Replace('\'', '"'));
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(AssistiveTechnologyRecords.Channel, NvdaSettings.SettingsEventType, document.RootElement, 2000, issues);
        Assert.Empty(issues);
    }

    // --- The player -------------------------------------------------------------

    private static JsonElement J(string json)
    {
        using var document = JsonDocument.Parse(json.Replace('\'', '"'));
        return document.RootElement.Clone();
    }

    private static string Settings(string mouse) =>
        $"{{'product':'NVDA','processId':{NvdaProcess},'copy':'portable','configFolder':'C:\\\\NVDA\\\\userConfig','read':true,'problem':null," +
        "'keyboardLayout':null,'nvdaModifierKeys':'7','multiPressTimeout':null,'autoPassThroughOnFocusChange':null,'autoPassThroughOnCaretMove':null," +
        "'trapNonCommandGestures':null,'enableOnPageLoad':null,'profiles':[],'profileTriggers':false,'customGestures':[],'gesturesProblem':null" +
        mouse + "}";

    private const string Defaults =
        ",'enableMouseTracking':null,'ignoreInjectedMouseInput':null,'mouseTextUnit':null,'reportObjectRoleOnMouseEnter':null,'audioCoordinatesOnMouseMove':null,'reportMouseShapeChanges':null";

    // NVDA from 1 s to 20 s, with the given settings.
    private static AssistiveTechnologyTimeline Nvda(string? mouse) => new(
    [
        new AssistiveTechnologyRecord(0, AssistiveTechnologyRecords.WatchEventType,
            J("{'products':['NVDA'],'executables':['nvda.exe'],'modules':['nvdahelperremote.dll'],'browserExecutablePath':null,'soundProblem':null}")),
        new AssistiveTechnologyRecord(Second, AssistiveTechnologyRecords.ProcessStartedEventType,
            J($"{{'product':'NVDA','role':'screen-reader','processId':{NvdaProcess},'runningAtStart':false,'productVersion':'2026.2'}}")),
        .. mouse is null
            ? Array.Empty<AssistiveTechnologyRecord>()
            : [new AssistiveTechnologyRecord(Second + Millisecond, NvdaSettings.SettingsEventType, J(Settings(mouse)))],
        new AssistiveTechnologyRecord(20 * Second, AssistiveTechnologyRecords.ProcessExitedEventType,
            J($"{{'product':'NVDA','role':'screen-reader','processId':{NvdaProcess}}}"))
    ]);

    private int _id;

    private KeyEvidenceRecord Hook(long time, int message, int x, int y, bool injected = false, int mouseData = 0, long extra = 0)
    {
        var (kind, button) = MouseHookRecords.Describe(message, mouseData);
        var flags = injected ? MouseHookRecords.InjectedFlag : 0;
        var delta = MouseHookRecords.WheelDelta(kind, mouseData);
        var payload = JsonSerializer.SerializeToElement(new
        {
            installation = 1, message, kind, button, x, y, mouseData, wheelDelta = delta, flags, injected,
            lowerIntegrityInjected = false, penOrTouch = MouseHookRecords.PenOrTouch(extra), extraInformation = extra,
            eventTimeMilliseconds = time / Millisecond
        });
        return new KeyEvidenceRecord($"hook-{++_id}", time, MouseHookRecords.Channel, MouseHookRecords.EventType, payload);
    }

    private KeyEvidenceRecord Raw(long time, int buttonFlags = 0, int deltaX = 0, long device = 0x10045) =>
        new($"raw-{++_id}", time, "input.mouse", "raw-mouse", JsonSerializer.SerializeToElement(new
        {
            deviceHandle = device, movementMode = "relative", deltaX, deltaY = 0, buttonFlags, buttonData = 0,
            rawButtons = 0, extraInformation = 0, cursorX = 0, cursorY = 0, foregroundProcessId = 1
        }));

    [Fact]
    public void AMoveFromAMouseIsJoinedWithRawInputAndNotedByNvda()
    {
        var hook = Hook(2 * Second, MouseHookRecords.MouseMove, 640, 480);
        var mouse = new MouseActivity([hook, Raw(2 * Second + 2 * Millisecond, deltaX: 3)], Nvda(Defaults));
        var outcome = mouse.Of(hook.EventId)!;
        Assert.Equal(
            "Move to (640, 480), from a device; NVDA notes the position, inferred; it reads what is under the pointer at the latest position the next time it runs its queue, so not every move is read",
            outcome.Text);
        Assert.Contains("Raw input: device 0x10045, 2.0 ms after the hook's record", outcome.Details);
        Assert.Contains(
            $"NVDA (process {NvdaProcess}): mouse tracking on, NVDA's default, ignore mouse input from other applications off, NVDA's default, as read at its start",
            outcome.Details);
    }

    [Fact]
    public void AnInjectedClickIsIgnoredWhereNvdaIgnoresInjectedInput()
    {
        var down = Hook(3 * Second, MouseHookRecords.LeftButtonDown, 10, 20, injected: true);
        var mouse = new MouseActivity([down], Nvda(Defaults.Replace("'ignoreInjectedMouseInput':null", "'ignoreInjectedMouseInput':'True'", StringComparison.Ordinal)));
        var outcome = mouse.Of(down.EventId)!;
        Assert.Equal(
            "Left button down at (10, 20), injected by software; NVDA ignores it, inferred: injected, and NVDA ignores mouse input from other applications",
            outcome.Text);
        Assert.DoesNotContain(outcome.Details, detail => detail.StartsWith("Raw input", StringComparison.Ordinal));
        Assert.Contains("NVDA also ignores injected mouse events while it makes its own mouse clicks; that is not recorded.", outcome.Details);
    }

    [Fact]
    public void AClickStopsSpeechAndAnythingIsIgnoredWithTrackingOff()
    {
        var down = Hook(3 * Second, MouseHookRecords.RightButtonDown, 10, 20);
        var wheel = Hook(4 * Second, MouseHookRecords.MouseWheel, 10, 20, mouseData: unchecked((int)0xFF100000));
        var on = new MouseActivity([down, wheel], Nvda(Defaults));
        Assert.EndsWith("; NVDA stops speech, inferred", on.Of(down.EventId)!.Text, StringComparison.Ordinal);
        Assert.Equal(
            "Wheel 2 notches toward the user at (10, 20), from a device; NVDA notes the position and does nothing more with it, inferred",
            on.Of(wheel.EventId)!.Text);
        Assert.Contains("Raw input: no record of it from a device within 50 ms", on.Of(down.EventId)!.Details);

        var off = new MouseActivity([down], Nvda(Defaults.Replace("'enableMouseTracking':null", "'enableMouseTracking':'False'", StringComparison.Ordinal)));
        Assert.EndsWith("; NVDA ignores it, inferred: NVDA's mouse tracking is off", off.Of(down.EventId)!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutNvdaOrItsMouseSettingsTheOutcomeSaysSo()
    {
        var before = Hook(Second / 2, MouseHookRecords.MouseMove, 1, 2);
        var touch = Hook(5 * Second, MouseHookRecords.LeftButtonUp, 1, 2, extra: 0xFF515780);
        var mouse = new MouseActivity([before, touch], Nvda(mouse: null));
        Assert.Equal("Move to (1, 2), from a device", mouse.Of(before.EventId)!.Text);
        Assert.Equal("Left button up at (1, 2), from a touch screen; NVDA running, its mouse settings not recorded", mouse.Of(touch.EventId)!.Text);

        // A settings record made before the mouse settings were read.
        var older = new MouseActivity([touch], Nvda(string.Empty));
        Assert.EndsWith("; NVDA running, its mouse settings not recorded", older.Of(touch.EventId)!.Text, StringComparison.Ordinal);
        Assert.Equal("not known: NVDA's mouse settings were not recorded", older.NvdaTrackingAt(5 * Second));
    }

    [Fact]
    public void ThePointerRowStepsThroughEveryEvent()
    {
        var records = new[]
        {
            Hook(2 * Second, MouseHookRecords.MouseMove, 100, 200),
            Hook(2 * Second + 8 * Millisecond, MouseHookRecords.MouseMove, 101, 200),
            Hook(3 * Second, MouseHookRecords.LeftButtonDown, 101, 200, injected: true)
        };
        var mouse = new MouseActivity(records, Nvda(Defaults));
        Assert.Equal([2 * Second, 2 * Second + 8 * Millisecond, 3 * Second], mouse.ChangeTimesOf(MouseActivity.PointerKey));
        Assert.Equal([Second, 20 * Second], mouse.ChangeTimesOf(MouseActivity.NvdaTrackingKey));

        var rows = mouse.RowsAt(2500 * Millisecond);
        Assert.Equal("move to (101, 200), from a device", rows[0].Value);
        Assert.Equal(2 * Second + 8 * Millisecond, rows[0].SetAt);
        Assert.Equal(
            "tracking on (on, NVDA's default), does not ignore mouse input from other applications (off, NVDA's default), as read at NVDA's start",
            rows[1].Value);
        Assert.Equal("no mouse event yet", mouse.RowsAt(Second).Single(row => row.Key == MouseActivity.PointerKey).Value);
        Assert.Equal("NVDA not running", mouse.RowsAt(21 * Second)[1].Value);
        var position = PropertyChangeSteps.Locate(mouse.ChangeTimesOf(MouseActivity.PointerKey)!, 2500 * Millisecond);
        Assert.Equal("2/3", position.Shown);
        Assert.Empty(MouseActivity.Empty.RowsAt(Second));
    }

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "test.collector", "Test", "1.0.0", [MouseHookRecords.Channel, "input.mouse", AssistiveTechnologyRecords.Channel], "test");

    [Fact]
    public void TheOutcomesAndRowsComeThroughThePlaybackIndex()
    {
        var hook = Hook(2 * Second, MouseHookRecords.LeftButtonDown, 640, 480);
        var raw = Raw(2 * Second + Millisecond, buttonFlags: 0x0001);
        var records = new List<RecorderEvent>
        {
            RecorderEventFactory.Create("s", Collector, AssistiveTechnologyRecords.Channel, 1, 0, AssistiveTechnologyRecords.WatchEventType,
                J("{'products':['NVDA'],'executables':['nvda.exe'],'modules':['nvdahelperremote.dll'],'browserExecutablePath':null,'soundProblem':null}")),
            RecorderEventFactory.Create("s", Collector, AssistiveTechnologyRecords.Channel, 2, Second, AssistiveTechnologyRecords.ProcessStartedEventType,
                J($"{{'product':'NVDA','role':'screen-reader','processId':{NvdaProcess},'runningAtStart':false,'productVersion':'2026.2'}}")),
            RecorderEventFactory.Create("s", Collector, AssistiveTechnologyRecords.Channel, 3, Second + Millisecond, NvdaSettings.SettingsEventType,
                J(Settings(Defaults))),
            RecorderEventFactory.Create("s", Collector, MouseHookRecords.Channel, 4, hook.MonotonicNanoseconds, hook.EventType, hook.Payload),
            RecorderEventFactory.Create("s", Collector, "input.mouse", 5, raw.MonotonicNanoseconds, raw.EventType, raw.Payload)
        };

        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var key = 0L;
        foreach (var record in records)
        {
            key++;
            builder.Add(key, record with { EventId = $"e{key}" });
        }

        var index = builder.Build();
        Assert.Equal(16, index.Version);
        var kept = Assert.Single(index.Events, item => item.EventType == MouseHookRecords.EventType);
        Assert.True(kept.Payload.TryGetProperty("extraInformation", out _));

        var archiveBuilder = new SessionPlaybackArchiveBuilder(Path.GetTempPath(), retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(20), 20 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, true),
            [], [], 1, 0, null));

        Assert.True(archive.Mouse.Recorded);
        var outcome = archive.Mouse.Of("e4")!;
        Assert.Equal("Left button down at (640, 480), from a device; NVDA stops speech, inferred", outcome.Text);
        Assert.Contains("Raw input: device 0x10045, 1.0 ms after the hook's record", outcome.Details);
        Assert.Contains(archive.Mouse.RowsAt(3 * Second), row => row.Setting == MouseActivity.PointerSetting);
    }

    [Fact]
    public void TheEventListSummarizesTheRecords()
    {
        string Summary(string eventType, string json)
        {
            using var document = JsonDocument.Parse(json.Replace('\'', '"'));
            return SessionPlaybackArchiveBuilder.CreateSummary(MouseHookRecords.Channel, eventType, document.RootElement);
        }

        Assert.Equal("hook-mouse: move, (640, -12)", Summary(MouseHookRecords.EventType, Move));
        Assert.Equal(
            "hook-mouse: left button-down, (1, 2), touch, injected",
            Summary(MouseHookRecords.EventType,
                "{'installation':1,'message':513,'kind':'button-down','button':'left','x':1,'y':2,'mouseData':0,'wheelDelta':null,'flags':1,'injected':true,'lowerIntegrityInjected':false,'penOrTouch':'touch','extraInformation':4283520896,'eventTimeMilliseconds':5}"));
        Assert.Equal("hook-mouse-installed: refresh, installation 2", Summary(MouseHookRecords.InstalledEventType, Installed));
    }
}

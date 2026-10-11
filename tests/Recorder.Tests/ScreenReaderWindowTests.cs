using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// NVDA's own windows in front, such as the Elements List, with the element
/// focused in each. See docs/architecture/screen-reader-activity.md, "2c-3,
/// NVDA's windows".
/// </summary>
public sealed class ScreenReaderWindowTests
{
    private const long Millisecond = 1_000_000;
    private const long Second = 1_000_000_000;
    private const int NvdaProcess = 20008;

    private static JsonElement J(string json)
    {
        using var document = JsonDocument.Parse(json.Replace('\'', '"'));
        return document.RootElement.Clone();
    }

    private static int _id;

    private static KeyEvidenceRecord Hook(long time, int virtualKey, bool up, bool extended = false) =>
        new($"hook-{++_id}", time, KeyboardHookRecords.Channel, KeyboardHookRecords.KeyEventType,
            J($"{{'installation':1,'virtualKey':{virtualKey},'scanCode':{virtualKey},'flags':0,'up':{(up ? "true" : "false")},'extended':{(extended ? "true" : "false")}," +
              "'injected':false,'lowerIntegrityInjected':false,'altDown':false,'extraInformation':0,'eventTimeMilliseconds':1}"));

    // NVDA+F7 with Insert, kept: the hook's records alone.
    private static IEnumerable<KeyEvidenceRecord> NvdaF7(long time) =>
    [
        Hook(time, 0x2D, up: false, extended: true), Hook(time + 20 * Millisecond, 0x76, up: false),
        Hook(time + 90 * Millisecond, 0x76, up: true), Hook(time + 120 * Millisecond, 0x2D, up: true, extended: true)
    ];

    private static readonly AssistiveTechnologyTimeline Nvda = new(
    [
        new AssistiveTechnologyRecord(0, AssistiveTechnologyRecords.WatchEventType,
            J("{'products':['NVDA'],'executables':['nvda.exe'],'modules':['nvdahelperremote.dll'],'browserExecutablePath':null,'soundProblem':null}")),
        new AssistiveTechnologyRecord(0, AssistiveTechnologyRecords.ProcessStartedEventType,
            J($"{{'product':'NVDA','role':'screen-reader','processId':{NvdaProcess},'runningAtStart':true,'productVersion':'2026.2'}}")),
        new AssistiveTechnologyRecord(228 * Second, AssistiveTechnologyRecords.ProcessExitedEventType,
            J($"{{'product':'NVDA','role':'screen-reader','processId':{NvdaProcess}}}"))
    ]);

    private static ForegroundWindow Chrome(long time) => new(time, "chrome", 4000, "Home | CNIB - Chromium");

    private static ForegroundWindow NvdaWindow(long time, string? title) => new(time, "nvda", NvdaProcess, title);

    // Recording 20261010-211432, from its foreground window and focus records.
    private static ScreenReaderWindowTimeline Windows(IEnumerable<KeyEvidenceRecord>? keys = null)
    {
        var foreground = new[]
        {
            Chrome(2 * Second),
            NvdaWindow(92_768 * Millisecond, "NVDA"), NvdaWindow(92_819 * Millisecond, "Elements List"),
            new ForegroundWindow(105_010 * Millisecond, "Perplexity", 900, "Perplexity"),
            Chrome(116 * Second),
            NvdaWindow(221_969 * Millisecond, "NVDA"), NvdaWindow(221_978 * Millisecond, null),
            NvdaWindow(223_483 * Millisecond, "Exit NVDA")
        };
        var focus = new[]
        {
            new FocusedElement(93_009 * Millisecond, NvdaProcess, "ControlType.TreeItem", "Contact Us"),
            new FocusedElement(93_016 * Millisecond, NvdaProcess, "ControlType.TreeItem", "Contact Us"),
            new FocusedElement(99_960 * Millisecond, NvdaProcess, "ControlType.TreeItem", "Phone us by number: 1-800-563-2642"),
            new FocusedElement(104_886 * Millisecond, 17100, "ControlType.Edit", "Search mail"),
            new FocusedElement(222_552 * Millisecond, NvdaProcess, "ControlType.MenuItem", "Exit"),
            new FocusedElement(223_684 * Millisecond, NvdaProcess, "ControlType.ComboBox", "What would you like to do?")
        };
        var dispositions = new KeyDispositions(keys ?? NvdaF7(92_766 * Millisecond - 20 * Millisecond), Nvda);
        return new ScreenReaderWindowTimeline(foreground, focus, Nvda, dispositions.KeptCommands, 300 * Second);
    }

    [Fact]
    public void NvdasWindowsInFrontAreSpansNamedByTheirTitles()
    {
        var windows = Windows();
        Assert.Equal(
            [
                ("Elements List", 92_768 * Millisecond, 105_010 * Millisecond),
                ("NVDA menu", 221_969 * Millisecond, 223_483 * Millisecond),
                ("Exit NVDA", 223_483 * Millisecond, 228 * Second)
            ],
            windows.Spans.Select(span => (span.Name, span.Start, span.End)));

        // NVDA's main frame, in front for 51 ms, is folded into the list.
        Assert.DoesNotContain(windows.Spans, span => span.Name == ScreenReaderWindowTimeline.MainFrameTitle);
        Assert.Equal(
            "left the front at 00:01:45.010, when \"Perplexity\" (Perplexity) came to the front",
            windows.Spans[0].EndBasis);
        Assert.Equal(
            "left the front at 00:03:43.483, when \"Exit NVDA\" (nvda) came to the front",
            windows.Spans[1].EndBasis);
        Assert.Equal("NVDA exited at 00:03:48.000", windows.Spans[2].EndBasis);
    }

    [Fact]
    public void TheRowGivesTheWindowItsOpenerAndTheElementFocusedInIt()
    {
        var windows = Windows();
        Assert.Equal(
            "Elements List, in front from 00:01:32.768, opened by NVDA+f7 (elements list) at 00:01:32.766, inferred; " +
            "focus: tree item \"Contact Us\" at 00:01:33.009",
            windows.ValueAt(95 * Second));
        Assert.EndsWith("focus: tree item \"Phone us by number: 1-800-563-2642\" at 00:01:39.960", windows.ValueAt(104_900 * Millisecond), StringComparison.Ordinal);
        Assert.Equal("NVDA menu, in front from 00:03:41.969; focus: menu item \"Exit\" at 00:03:42.552", windows.ValueAt(223 * Second));
        Assert.Equal("none in front", windows.ValueAt(50 * Second));
        Assert.StartsWith("none in front; Elements List left the front at 00:01:45.010", windows.ValueAt(110 * Second), StringComparison.Ordinal);
        // Each element focused in a window is a step, the repeat of the same
        // element at once given once.
        Assert.Equal(
            [
                92_768 * Millisecond, 93_009 * Millisecond, 99_960 * Millisecond, 105_010 * Millisecond,
                221_969 * Millisecond, 222_552 * Millisecond, 223_483 * Millisecond, 223_684 * Millisecond, 228 * Second
            ],
            windows.ChangeTimes());
        var row = Assert.Single(windows.RowsAt(95 * Second));
        Assert.Equal((AssistiveTechnologyTimeline.Group, ScreenReaderWindowTimeline.Setting, ScreenReaderWindowTimeline.Key), (row.Group, row.Setting, row.Key));
    }

    [Fact]
    public void ACommandOpensOnlyItsOwnWindowAndOnlyJustBefore()
    {
        // NVDA+F7 two seconds before: too long before to have opened it.
        Assert.Null(Windows(NvdaF7(90 * Second)).Spans[0].Opener);

        // NVDA+F7 does not open the NVDA menu.
        var windows = Windows(NvdaF7(221_900 * Millisecond));
        Assert.All(windows.Spans, span => Assert.Null(span.Opener));
    }

    [Fact]
    public void AnUntitledWindowWithNoMenuIsNamedAsSuch()
    {
        var windows = new ScreenReaderWindowTimeline(
            [NvdaWindow(10 * Second, ""), Chrome(12 * Second)],
            [new FocusedElement(11 * Second, NvdaProcess, "ControlType.Button", "OK")],
            Nvda, [], 20 * Second);
        Assert.Equal("NVDA window (untitled)", Assert.Single(windows.Spans).Name);

        // A window of another process, though named nvda, is not NVDA's.
        var other = new ScreenReaderWindowTimeline(
            [new ForegroundWindow(10 * Second, "nvda", 1, "Elements List"), Chrome(12 * Second)], [], Nvda, [], 20 * Second);
        Assert.Empty(other.Spans);
        Assert.Empty(ScreenReaderWindowTimeline.Empty.RowsAt(0));
    }

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "test.collector", "Test", "1.0.0", ["window.foreground", "accessibility.uia.events", AssistiveTechnologyRecords.Channel], "test");

    [Fact]
    public void ThePlayerFindsTheWindowsThroughThePlaybackIndex()
    {
        // As the player opens a recording: from its playback index, which
        // kept the foreground window records without their process ID and
        // title before version 13, and no focus records.
        var records = new List<(string Channel, long Time, string EventType, JsonElement Payload)>
        {
            (AssistiveTechnologyRecords.Channel, 0, AssistiveTechnologyRecords.WatchEventType,
                J("{'products':['NVDA'],'executables':['nvda.exe'],'modules':['nvdahelperremote.dll'],'browserExecutablePath':'C:\\\\A11yRecorder\\\\chrome.exe','soundProblem':null}")),
            (AssistiveTechnologyRecords.Channel, Millisecond, AssistiveTechnologyRecords.ProcessStartedEventType,
                J($"{{'product':'NVDA','role':'screen-reader','processId':{NvdaProcess},'runningAtStart':true,'productVersion':'2026.2'}}")),
            ("window.foreground", 2 * Millisecond, "foreground-window",
                J("{'windowHandle':1,'processId':4000,'processName':'chrome','title':'Home | CNIB','className':'Chrome_WidgetWin_1'}")),
            ("window.foreground", 5 * Second, "foreground-window",
                J($"{{'windowHandle':2,'processId':{NvdaProcess},'processName':'nvda','title':'Elements List','className':'#32770'}}")),
            ("accessibility.uia.events", 6 * Second, "focus-changed",
                J($"{{'eventId':'AutomationElementIdentifiers.AutomationFocusChangedEvent','changeType':null,'runtimeId':null,'newValue':null," +
                  $"'element':{{'processId':{NvdaProcess},'name':'Contact Us','controlType':'ControlType.TreeItem'}}}}")),
            ("window.foreground", 8 * Second, "foreground-window",
                J("{'windowHandle':1,'processId':4000,'processName':'chrome','title':'Home | CNIB','className':'Chrome_WidgetWin_1'}"))
        };

        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var key = 0L;
        foreach (var (channel, time, eventType, payload) in records)
        {
            key++;
            builder.Add(key, RecorderEventFactory.Create("s", Collector, channel, (ulong)key, time, eventType, payload) with { EventId = $"e{key}" });
        }

        var archiveBuilder = new SessionPlaybackArchiveBuilder(Path.GetTempPath(), retainEvents: false);
        archiveBuilder.AddIndex(builder.Build());
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(20), 20 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, true),
            [], [], 1, 0, null));

        var span = Assert.Single(archive.ScreenReaderWindows.Spans);
        Assert.Equal(("Elements List", 5 * Second, 8 * Second), (span.Name, span.Start, span.End));
        Assert.EndsWith("focus: tree item \"Contact Us\" at 00:00:06.000", archive.ScreenReaderWindows.ValueAt(7 * Second), StringComparison.Ordinal);

        // The strip's events, for stepping on the timeline: the window
        // coming to the front, the element focused in it, and the window
        // that replaced it.
        Assert.Equal(
            [("foreground-window", 5 * Second), ("focus-changed", 6 * Second), ("foreground-window", 8 * Second)],
            archive.ScreenReaderWindows.Events.Select(item => (item.EventType, item.MonotonicNanoseconds)));
    }
}

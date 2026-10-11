using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// NVDA's browse or focus mode, inferred from the keys it kept or passed,
/// and unknown where the recording does not show it. See
/// docs/architecture/screen-reader-activity.md, "2c-2, browse or focus mode".
/// </summary>
public sealed class ScreenReaderModeTests
{
    private const long Millisecond = 1_000_000;
    private const long Second = 1_000_000_000;

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

    private static KeyEvidenceRecord Raw(long time, int virtualKey, bool up) =>
        new($"raw-{++_id}", time, "input.keyboard", "raw-keyboard",
            J($"{{'deviceHandle':65601,'makeCode':{virtualKey},'flags':{(up ? 1 : 0)},'virtualKey':{virtualKey},'message':{(up ? 257 : 256)},'extraInformation':0}}"));

    private static KeyEvidenceRecord Page(long time) =>
        new($"page-{++_id}", time, KeyDispositions.DispatchChannel, KeyDispositions.DispatchStartedEventType,
            J("{'eventName':'keydown','trusted':true}"));

    private static readonly AssistiveTechnologyTimeline Nvda = new(
    [
        new AssistiveTechnologyRecord(0, AssistiveTechnologyRecords.ProcessStartedEventType,
            J("{'product':'NVDA','role':'screen-reader','processId':6688,'runningAtStart':true,'productVersion':'2026.2'}"))
    ]);

    // A key NVDA kept: the hook's records alone.
    private static IEnumerable<KeyEvidenceRecord> Kept(long time, int virtualKey, bool extended = false) =>
        [Hook(time, virtualKey, up: false, extended), Hook(time + 80 * Millisecond, virtualKey, up: true, extended)];

    // A key that passed and the page received.
    private static IEnumerable<KeyEvidenceRecord> Typed(long time, int virtualKey) =>
    [
        Hook(time, virtualKey, up: false), Raw(time + Millisecond, virtualKey, up: false), Page(time + 3 * Millisecond),
        Hook(time + 80 * Millisecond, virtualKey, up: true), Raw(time + 81 * Millisecond, virtualKey, up: true)
    ];

    private static ScreenReaderModeTimeline Mode(
        IEnumerable<KeyEvidenceRecord> records,
        IEnumerable<long>? loads = null,
        IEnumerable<ForegroundWindow>? foreground = null)
    {
        var keys = new KeyDispositions(records, Nvda);
        return new ScreenReaderModeTimeline(
            keys.ModeEvidence,
            foreground ?? [new ForegroundWindow(0, "chrome")],
            "chrome",
            loads ?? [],
            Nvda.ScreenReaderRuns("NVDA"));
    }

    [Fact]
    public void KeptLettersAreBrowseModeAndTypedLettersFocusMode()
    {
        // Recording 20261010-211432: H kept from 51.8 s, typing from 181.4 s.
        var mode = Mode([.. Kept(51 * Second, 0x48), .. Kept(54 * Second, 0x44), .. Typed(181 * Second, 0x48), .. Typed(182 * Second, 0x49)]);
        Assert.Equal(
            [ScreenReaderModeKind.Browse, ScreenReaderModeKind.Changed, ScreenReaderModeKind.Focus],
            mode.Spans.Select(span => span.Kind));
        Assert.Equal("browse mode, inferred from H kept (next heading) at 00:00:51.000 to D kept (next landmark) at 00:00:54.000", mode.ValueAt(52 * Second));
        Assert.StartsWith("changed between 00:00:54.000 and 00:03:01.000, from browse mode to focus mode, inferred", mode.ValueAt(100 * Second), StringComparison.Ordinal);
        Assert.Equal("focus mode, inferred from H passed, received by the page at 00:03:01.000 to I passed, received by the page at 00:03:02.000", mode.ValueAt(181_500 * Millisecond));
        Assert.Equal("unknown", mode.ValueAt(10 * Second));
        Assert.Equal("unknown; last seen focus mode, I passed, received by the page at 00:03:02.000", mode.ValueAt(200 * Second));
    }

    [Fact]
    public void APageLoadEndsTheInference()
    {
        var mode = Mode([.. Kept(10 * Second, 0x48), .. Typed(30 * Second, 0x48)], loads: [20 * Second]);
        Assert.Equal([ScreenReaderModeKind.Browse, ScreenReaderModeKind.Focus], mode.Spans.Select(span => span.Kind));
        Assert.Equal("unknown; a page loaded at 00:00:20.000", mode.ValueAt(25 * Second));
    }

    [Fact]
    public void NvdaSpaceSwitchesTheModeAndEscapeReturnsToBrowseMode()
    {
        var space = new List<KeyEvidenceRecord>
        {
            Hook(20 * Second, 0x2D, up: false, extended: true), Hook(20 * Second + 30 * Millisecond, 0x20, up: false),
            Hook(20 * Second + 90 * Millisecond, 0x20, up: true), Hook(20 * Second + 120 * Millisecond, 0x2D, up: true, extended: true)
        };
        var mode = Mode([.. Kept(10 * Second, 0x48), .. space, .. Kept(40 * Second, 0x1B), .. Kept(50 * Second, 0x4B)]);
        Assert.Equal(
            [ScreenReaderModeKind.Browse, ScreenReaderModeKind.Focus, ScreenReaderModeKind.Browse],
            mode.Spans.Select(span => span.Kind));
        Assert.StartsWith("focus mode, inferred from NVDA+Space kept at 00:00:20.030", mode.ValueAt(30 * Second), StringComparison.Ordinal);
        Assert.StartsWith("browse mode, inferred from Escape kept at 00:00:40.000", mode.ValueAt(45 * Second), StringComparison.Ordinal);
        Assert.DoesNotContain(mode.Spans, span => span.Kind == ScreenReaderModeKind.Changed);
    }

    [Fact]
    public void KeysInAnotherWindowOrWithModifiersDoNotCount()
    {
        // Recording 20261010-211432 at 99.95 s: Up arrows in NVDA's Elements List.
        var foreground = new[] { new ForegroundWindow(0, "chrome"), new ForegroundWindow(90 * Second, "nvda"), new ForegroundWindow(105 * Second, "chrome") };
        var up = new List<KeyEvidenceRecord> { Hook(100 * Second, 0x26, up: false, extended: true), Raw(100 * Second + Millisecond, 0x26, up: false) };
        var ctrl = new List<KeyEvidenceRecord>
        {
            Hook(110 * Second, 0xA2, up: false), Raw(110 * Second + Millisecond, 0xA2, up: false),
            Hook(110 * Second + 20 * Millisecond, 0x4C, up: false), Raw(110 * Second + 21 * Millisecond, 0x4C, up: false), Page(110 * Second + 23 * Millisecond)
        };
        var mode = Mode([.. up, .. ctrl], foreground: foreground);
        Assert.Empty(mode.Spans);
        Assert.Equal("unknown", mode.ValueAt(100 * Second));

        // A key that passed but no page received, as in the browser's toolbar.
        var toolbar = Mode([Hook(5 * Second, 0x09, up: false), Raw(5 * Second + Millisecond, 0x09, up: false)]);
        Assert.Empty(toolbar.Spans);
    }

    [Fact]
    public void TheRowHasChangesToStepThrough()
    {
        var mode = Mode([.. Kept(10 * Second, 0x48), .. Kept(12 * Second, 0x48)], loads: [20 * Second]);
        Assert.Equal([10 * Second, 12 * Second + 1], mode.ChangeTimesOf(ScreenReaderModeTimeline.Key));
        var row = Assert.Single(mode.RowsAt(11 * Second));
        Assert.Equal(("Screen reader", "NVDA mode"), (row.Group, row.Setting));
        Assert.Equal(mode.ChangeTimesOf(row.Key), PropertyChangeSteps.TimesOf(
            row, WindowsPreferenceTimeline.Empty, MagnifierChangeTimeline.Empty, screenReaderMode: mode));
        Assert.Empty(ScreenReaderModeTimeline.Empty.RowsAt(0));
    }

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "test", "Test", "1",
        ["input.keyboard", KeyboardHookRecords.Channel, AssistiveTechnologyRecords.Channel, "window.foreground"], "test");

    [Fact]
    public void ThePlayerFindsTheModeThroughThePlaybackIndex()
    {
        // As the player opens a recording: from its playback index, which
        // keeps only the records playback reads. The foreground window
        // records were missing from version 11, so no key counted.
        var records = new List<(string Channel, long Time, string EventType, JsonElement Payload)>
        {
            (AssistiveTechnologyRecords.Channel, 0, AssistiveTechnologyRecords.WatchEventType,
                J("{'products':['NVDA'],'executables':['nvda.exe'],'modules':['nvdahelperremote.dll'],'browserExecutablePath':'C:\\\\A11yRecorder\\\\chrome.exe','soundProblem':null}")),
            (AssistiveTechnologyRecords.Channel, Millisecond, AssistiveTechnologyRecords.ProcessStartedEventType,
                J("{'product':'NVDA','role':'screen-reader','processId':6688,'runningAtStart':true,'productVersion':'2026.2'}")),
            ("window.foreground", 2 * Millisecond, "foreground-window",
                J("{'windowHandle':1,'processId':4000,'processName':'chrome','title':'Home | CNIB','className':'Chrome_WidgetWin_1'}"))
        };
        foreach (var record in Kept(10 * Second, 0x48).Concat(Kept(12 * Second, 0x44)))
        {
            records.Add((record.Channel, record.MonotonicNanoseconds, record.EventType, record.Payload));
        }

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

        var span = Assert.Single(archive.ScreenReaderMode.Spans);
        Assert.Equal((ScreenReaderModeKind.Browse, 10 * Second, 12 * Second), (span.Kind, span.Start, span.End));
        Assert.StartsWith("browse mode, inferred from H kept", archive.ScreenReaderMode.ValueAt(11 * Second), StringComparison.Ordinal);
    }
}

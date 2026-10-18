using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Analysis.Summary;
using Recorder.Collectors.Windowing;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// The settings in use, for the report's appendices and its executive
// summary: each setting at the start, whether it is the default, and each
// change. See docs/architecture/recording-summary.md, "The executive
// summary and the settings in use".
public sealed class SettingsReaderTests
{
    private const long Millisecond = 1_000_000;
    private const long Second = 1_000_000_000;
    private const int NvdaProcess = 5120;

    private static JsonElement J(string json)
    {
        using var document = JsonDocument.Parse(json.Replace('\'', '"'));
        return document.RootElement.Clone();
    }

    private static JsonElement J(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    // The settings read from the given records, which the playback index
    // keeps, as the analysis reads them, each with its event.
    private static RecordedSettings Read(
        IReadOnlyList<WindowsPreferenceRecord>? windows = null,
        IReadOnlyList<BrowserPreferenceRecord>? browser = null,
        IReadOnlyList<AssistiveTechnologyRecord>? assistive = null,
        IReadOnlyList<SessionVideoFrame>? frames = null)
    {
        var events = new List<PlaybackIndexEvent>();
        void Add(string channel, long time, string eventType, JsonElement payload) =>
            events.Add(new PlaybackIndexEvent(events.Count + 1, $"event-{events.Count + 1}", "observed", channel, eventType, time, payload));
        foreach (var record in windows ?? [])
        {
            Add(WindowsPreferenceSettings.Channel, record.MonotonicNanoseconds, record.EventType, record.Payload);
        }

        foreach (var record in browser ?? [])
        {
            Add(BrowserPreferenceSettings.Channel, record.MonotonicNanoseconds, record.EventType, record.Payload);
        }

        foreach (var record in assistive ?? [])
        {
            Add(AssistiveTechnologyRecords.Channel, record.MonotonicNanoseconds, record.EventType, record.Payload);
        }

        var archive = Archive(
            windows is null ? null : new WindowsPreferenceTimeline(windows),
            browser is null ? null : new BrowserPreferenceTimeline(browser),
            assistive is null ? null : new AssistiveTechnologyTimeline(assistive),
            frames);
        return SettingsReader.Read(archive, events, null);
    }

    private static SessionPlaybackArchive Archive(
        WindowsPreferenceTimeline? windows,
        BrowserPreferenceTimeline? browser,
        AssistiveTechnologyTimeline? assistive,
        IReadOnlyList<SessionVideoFrame>? frames)
    {
        var started = DateTimeOffset.UtcNow;
        var manifest = new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(20), 20 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, true),
            [], [], 2, 0, null);
        return new SessionPlaybackArchive(Path.GetTempPath(), manifest, 20 * Second, [], frames ?? [], [], [])
        {
            WindowsPreferences = windows ?? WindowsPreferenceTimeline.Empty,
            BrowserPreferences = browser ?? BrowserPreferenceTimeline.Empty,
            AssistiveTechnology = assistive ?? AssistiveTechnologyTimeline.Empty
        };
    }

    // Every Windows setting read, the integers as 1, with the caret width
    // as given.
    private static List<WindowsPreferenceRecord> Windows(long caretWidth, params WindowsPreferenceRecord[] more)
    {
        var readings = new Dictionary<string, WindowsPreferenceReading>(StringComparer.Ordinal);
        foreach (var setting in WindowsPreferenceSettings.All)
        {
            readings[setting.Name] = WindowsPreferenceReading.Read(setting.Kind switch
            {
                WindowsPreferenceKind.Boolean => JsonValue.Create(false),
                WindowsPreferenceKind.Integer => JsonValue.Create(setting.Name == "caretWidth" ? caretWidth : 1L),
                WindowsPreferenceKind.Number => JsonValue.Create(1.0),
                WindowsPreferenceKind.Text => JsonValue.Create("none"),
                _ => new JsonArray(new JsonObject
                {
                    ["deviceName"] = @"\\.\DISPLAY1",
                    ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = 1920, ["height"] = 1080 },
                    ["isPrimary"] = true,
                    ["dpiX"] = 96,
                    ["dpiY"] = 96
                })
            });
        }

        var snapshot = WindowsPreferencePayloads.Snapshot("start", readings, WindowsPreferenceSettings.UiSettingsEvents.ToDictionary(name => name, _ => true));
        return [new WindowsPreferenceRecord(0, WindowsPreferenceSettings.SnapshotEventType, J(snapshot)), .. more];
    }

    private static WindowsPreferenceRecord WindowsChange(long time, string setting, JsonNode before, JsonNode after) =>
        new(time, WindowsPreferenceSettings.ChangeEventType, J(WindowsPreferencePayloads.Change(
            setting, WindowsPreferenceReading.Read(before), WindowsPreferenceReading.Read(after),
            new WindowsPreferenceNotice("setting-change", 0x1043, null, null))));

    [Fact]
    public void Of_the_Windows_settings_only_the_caret_widths_default_is_known()
    {
        var at = Read(Windows(1));
        Assert.Null(at.WindowsNote);
        var caret = Assert.Single(at.Windows, row => row.Setting == "Caret width");
        Assert.Equal((DefaultState.Default, "1 pixel"), (caret.Default, caret.DefaultValue));
        Assert.Equal(SettingsReader.WindowsAppendix, caret.Appendix);
        Assert.All(at.Windows.Where(row => row.Setting != "Caret width"), row => Assert.Equal(DefaultState.Unknown, row.Default));
        Assert.Equal("default unknown", LayeredReport.DefaultText(Assert.Single(at.Windows, row => row.Setting == "Menu fade")));

        var wider = Assert.Single(Read(Windows(3)).Windows, row => row.Setting == "Caret width");
        Assert.Equal(DefaultState.NotDefault, wider.Default);
        Assert.Equal("not the default, which is 1 pixel", LayeredReport.DefaultText(wider));
    }

    [Fact]
    public void A_Windows_setting_changed_is_a_change_with_its_values_before_and_after()
    {
        var at = Read(Windows(1, WindowsChange(3 * Second, "menuFade", JsonValue.Create(false), JsonValue.Create(true))));
        var change = Assert.Single(at.Changes);
        Assert.Equal((3 * Second, "Menu fade", "off", "on", (long?)null), (change.Time, change.Setting, change.Before, change.After, change.End));
        Assert.Equal("Windows, Motion", change.Group);
        Assert.Equal("00:00:03.000 Windows, Motion, Menu fade: changed from off to on.", LayeredReport.ChangeText(change));
    }

    [Fact]
    public void Without_settings_records_each_part_says_they_are_unknown()
    {
        var at = Read();
        Assert.Empty(at.AllRows);
        Assert.Empty(at.Changes);
        Assert.Contains("so they are unknown", at.WindowsNote, StringComparison.Ordinal);
        Assert.Contains("so they are unknown", at.BrowserNote, StringComparison.Ordinal);
        Assert.Contains("so what was running is unknown", at.AssistiveNote, StringComparison.Ordinal);
    }

    [Fact]
    public void A_browser_settings_default_is_Chromiums_own_answer()
    {
        var snapshot = J(
            "{'context':{'browserInstanceId':'browser-1','processId':4000,'processType':'browser','profileId':null,'browserContextId':null,'pageId':null,'frameId':null,'documentId':null,'executionWorldId':null,'documentToken':null}," +
            "'profileDirectory':'C:\\\\P','newProfile':false,'preferences':{'defaultFontSize':{'value':20,'isDefault':false,'problem':null},'defaultFixedFontSize':{'value':13,'isDefault':true,'problem':null}," +
            "'caretBrowsing':{'value':null,'isDefault':null,'problem':'not registered'}}}");
        var at = Read(browser: [new BrowserPreferenceRecord(Second, BrowserPreferenceSettings.SnapshotEventType, snapshot)]);

        Assert.Null(at.BrowserNote);
        var size = Assert.Single(at.Browser, row => row.Setting == "Font size");
        Assert.Equal(DefaultState.NotDefault, size.Default);
        Assert.All(at.Browser, row => Assert.Equal(SettingsReader.BrowserAppendix, row.Appendix));
        Assert.Contains(at.Browser, row => row.Default == DefaultState.Default && !row.Value.EndsWith(" (default)", StringComparison.Ordinal));
        Assert.Equal((Second, "event-1"), (size.Source.Time, size.Source.EventId));
    }

    // NVDA running from 1 s, its settings read at once: a setting not
    // written is at its default, one written as its default in another
    // spelling is too, and one written otherwise is not.
    private static List<AssistiveTechnologyRecord> Nvda(string written, string? later = null) =>
    [
        new AssistiveTechnologyRecord(0, AssistiveTechnologyRecords.WatchEventType,
            J("{'products':['NVDA'],'executables':['nvda.exe'],'modules':['nvdahelperremote.dll'],'browserExecutablePath':null,'soundProblem':null}")),
        new AssistiveTechnologyRecord(Second, AssistiveTechnologyRecords.ProcessStartedEventType,
            J($"{{'product':'NVDA','role':'screen-reader','processId':{NvdaProcess},'runningAtStart':false,'productVersion':'2026.2'}}")),
        new AssistiveTechnologyRecord(Second + Millisecond, NvdaSettings.SettingsEventType, J(NvdaSettingsRecord(NvdaProcess, written))),
        .. later is null
            ? Array.Empty<AssistiveTechnologyRecord>()
            :
            [
                new AssistiveTechnologyRecord(10 * Second, AssistiveTechnologyRecords.ProcessExitedEventType,
                    J($"{{'product':'NVDA','role':'screen-reader','processId':{NvdaProcess}}}")),
                new AssistiveTechnologyRecord(12 * Second, AssistiveTechnologyRecords.ProcessStartedEventType,
                    J("{'product':'NVDA','role':'screen-reader','processId':6100,'runningAtStart':false,'productVersion':'2026.2'}")),
                new AssistiveTechnologyRecord(12 * Second + Millisecond, NvdaSettings.SettingsEventType, J(NvdaSettingsRecord(6100, later)))
            ]
    ];

    private static string NvdaSettingsRecord(int process, string written) =>
        $"{{'product':'NVDA','processId':{process},'copy':'portable','configFolder':'C:\\\\NVDA\\\\userConfig','read':true,'problem':null," +
        "'keyboardLayout':null,'nvdaModifierKeys':null,'multiPressTimeout':null,'autoPassThroughOnCaretMove':null," +
        "'trapNonCommandGestures':null,'enableOnPageLoad':null,'profiles':[],'profileTriggers':false,'customGestures':[],'gesturesProblem':null" +
        written + "}";

    [Fact]
    public void NVDAs_settings_say_which_are_its_defaults()
    {
        var at = Read(assistive: Nvda(",'autoPassThroughOnFocusChange':'on','enableMouseTracking':'0'"));
        var nvda = at.Assistive.Where(row => row.Group == "NVDA's settings").ToList();
        Assert.Equal("portable", Assert.Single(nvda, row => row.Setting == "Copy").Value);

        var focus = Assert.Single(nvda, row => row.Setting == "[virtualBuffers] autoPassThroughOnFocusChange");
        Assert.Equal(("on", DefaultState.Default), (focus.Value, focus.Default));
        var mouse = Assert.Single(nvda, row => row.Setting == "[mouse] enableMouseTracking");
        Assert.Equal(("0", DefaultState.NotDefault, "true"), (mouse.Value, mouse.Default, mouse.DefaultValue));
        Assert.Equal("not the default, which is true", LayeredReport.DefaultText(mouse));
        var caret = Assert.Single(nvda, row => row.Setting == "[virtualBuffers] autoPassThroughOnCaretMove");
        Assert.Equal(("false, not written in nvda.ini", DefaultState.Default), (caret.Value, caret.Default));
        Assert.Equal(DefaultState.Default, Assert.Single(nvda, row => row.Setting == "Configuration profiles").Default);
        Assert.Equal((Second + Millisecond, "event-3"), (focus.Source.Time, focus.Source.EventId));
        Assert.Contains(at.Changes, change => change.Group == "Assistive technology" && change.Setting == "NVDA");
    }

    [Fact]
    public void NVDAs_modifier_keys_are_named()
    {
        // 7 alone did not say Caps Lock was added (target report, 2026-10-18).
        Assert.Equal("7: Caps Lock, numpad Insert and extended Insert", SettingsReader.ShownNvdaValue("NVDAModifierKeys", "7"));
        Assert.Equal("6: numpad Insert and extended Insert", SettingsReader.ShownNvdaValue("NVDAModifierKeys", "6"));
        Assert.Equal("1: Caps Lock", SettingsReader.ShownNvdaValue("NVDAModifierKeys", "1"));
        Assert.Equal("9", SettingsReader.ShownNvdaValue("NVDAModifierKeys", "9"));
        Assert.Equal("7", SettingsReader.ShownNvdaValue("linesPerPage", "7"));
    }

    [Fact]
    public void An_NVDA_setting_that_differs_when_NVDA_starts_again_is_a_change()
    {
        var at = Read(assistive: Nvda(",'enableMouseTracking':null", ",'enableMouseTracking':'False'"));
        var change = Assert.Single(at.Changes, change => change.Group == "NVDA's settings");
        Assert.Equal((12 * Second + Millisecond, "[mouse] enableMouseTracking", "true, not written in nvda.ini", "False", "when NVDA started again"),
            (change.Time, change.Setting, change.Before, change.After, change.Detail));
        Assert.Equal(3, at.Changes.Count(item => item.Group == "Assistive technology"));
    }

    private static SessionVideoFrame Frame(long time, double level, int x, int y) =>
        new(time, $"frames/desktop/{time}.png", $"/f/{time}.png", 1920, 1080, 0, 0, new FullscreenMagnification(level, x, y));

    // At 200 percent: the view pans from 0.5 s to 1.4 s, each move less than
    // a second after the one before; stays put; then moves once at 3.5 s,
    // more than a second after, a second period.
    [Fact]
    public void A_period_of_panning_gives_where_the_view_started_and_ended_and_the_furthest_it_reached()
    {
        var at = Read(frames:
        [
            Frame(0, 2, 0, 0), Frame(500 * Millisecond, 2, 100, 0), Frame(Second, 2, 200, 50), Frame(1400 * Millisecond, 2, 150, 50),
            Frame(3 * Second, 2, 150, 50), Frame(3500 * Millisecond, 2, 0, 0)
        ]);

        var pans = at.Changes.Where(change => change.Setting == "Panning").ToList();
        Assert.Equal(2, pans.Count);
        Assert.Equal((500 * Millisecond, (long?)(1400 * Millisecond)), (pans[0].Time, pans[0].End));
        Assert.Equal("the part from (0, 0) to (960, 540), 960 by 540 pixels", pans[0].Before);
        Assert.Equal("the part from (150, 50) to (1110, 590), 960 by 540 pixels", pans[0].After);
        Assert.EndsWith("the furthest the view reached: left 0, right 1160, top 0, bottom 590, in the screen's pixels", pans[0].Detail, StringComparison.Ordinal);
        Assert.Equal(["frame:0", $"frame:{1400 * Millisecond}"], pans[0].Sources.Select(source => source.Key));
        Assert.Equal((3500 * Millisecond, (long?)(3500 * Millisecond)), (pans[1].Time, pans[1].End));
        Assert.Equal(pans[0].After, pans[1].Before);
        Assert.StartsWith("00:00:00.500 to 00:00:01.400 Magnifier, Panning: the view moved from the part from (0, 0)", LayeredReport.ChangeText(pans[0]), StringComparison.Ordinal);

        var shown = Assert.Single(at.Assistive, row => row.Setting == "Part of the screen shown");
        Assert.Equal("the part from (0, 0) to (960, 540), 960 by 540 pixels", shown.Value);
    }

    // The level changed twice less than a second apart is one change, from
    // its level before the first to its level after the last; changed
    // again later, another.
    [Fact]
    public void Changes_of_the_level_less_than_a_second_apart_are_one_change()
    {
        var at = Read(frames:
        [
            Frame(0, 2, 0, 0), Frame(5 * Second, 3, 0, 0), Frame(5500 * Millisecond, 4, 0, 0), Frame(8 * Second, 2, 0, 0)
        ]);

        var levels = at.Changes.Where(change => change.Setting == "Full screen level").ToList();
        Assert.Equal([5 * Second, 8 * Second], levels.Select(change => change.Time));
        Assert.Equal(levels[0].After, levels[1].Before);
        Assert.Equal(levels[0].Before, levels[1].After);
        Assert.Equal([$"frame:{5 * Second}", $"frame:{5500 * Millisecond}"], levels[0].Sources.Select(source => source.Key));
        Assert.DoesNotContain(at.Changes, change => change.Setting == "Panning");
    }
}

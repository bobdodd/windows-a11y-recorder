using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Analysis.Summary;

/// <summary>Whether a setting's value is its default.</summary>
public enum DefaultState
{
    /// <summary>The value is the default.</summary>
    Default,

    /// <summary>The value is not the default.</summary>
    NotDefault,

    /// <summary>The default is not known, or the value was not read.</summary>
    Unknown,

    /// <summary>Not a setting: a value read, such as what was sent to the page or a product running.</summary>
    NotASetting
}

/// <summary>
/// The record a setting's row or change is read from, as the evidence list
/// gives it: its time, the key it is registered by, so that rows read from
/// one record share one reference, what it is, and its record.
/// </summary>
public sealed record SettingSource(long? Time, string Key, string Text, string? EventId);

/// <summary>
/// A setting at the start, a row of an appendix: its appendix letter, its
/// group and name as the properties panel gives them, its value, whether it
/// is the default, the default where it is known, and the record read.
/// </summary>
public sealed record SettingRow(
    string Appendix,
    string Group,
    string Setting,
    string Value,
    DefaultState Default,
    string? DefaultValue,
    SettingSource Source);

/// <summary>
/// A change during the recording, found by the recorder: when, until when
/// for a period of panning, what changed, its value before and after,
/// anything more, such as how far a period of panning reached, and the
/// records it is read from.
/// </summary>
public sealed record SettingChange(
    long Time,
    long? End,
    string Group,
    string Setting,
    string Before,
    string After,
    string? Detail,
    IReadOnlyList<SettingSource> Sources);

/// <summary>
/// The accessibility and assistive technology settings of a recording, as
/// the recorder reads them for the report's appendices and story: the
/// Windows settings at the start (appendix A), the browser's (B), the
/// assistive technology's (C), and every change (D). Where a part was not
/// recorded, its rows are empty and its note says why. See
/// docs/architecture/recording-summary.md, "The executive summary and the
/// settings in use".
/// </summary>
public sealed record RecordedSettings(
    IReadOnlyList<SettingRow> Windows,
    string? WindowsNote,
    IReadOnlyList<SettingRow> Browser,
    string? BrowserNote,
    IReadOnlyList<SettingRow> Assistive,
    string? AssistiveNote,
    IReadOnlyList<SettingChange> Changes)
{
    public static RecordedSettings None { get; } = new(
        [], "The recording's Windows settings were not read.",
        [], "The recording's browser settings were not read.",
        [], "The recording's assistive technology was not read.",
        []);

    public IEnumerable<SettingRow> AllRows => [.. Windows, .. Browser, .. Assistive];
}

/// <summary>
/// Reads a recording's settings from the same records the player's
/// properties panel shows, and the Magnifier's view from each captured
/// frame's reading.
/// </summary>
public static class SettingsReader
{
    /// <summary>
    /// Moves of the Magnifier's view, or changes of its level, less than
    /// this apart are one period of panning, or one change of level (the
    /// recorder's choice, recording-summary.md, "The executive summary and
    /// the settings in use").
    /// </summary>
    public const long MagnifierGapNanoseconds = 1_000_000_000;

    public const string WindowsAppendix = "A";
    public const string BrowserAppendix = "B";
    public const string AssistiveAppendix = "C";

    private const string MagnifierGroup = WindowsPreferenceTimeline.MagnifierGroup;

    // The Windows settings whose default Microsoft documents, with the
    // documented value: SystemParametersInfoW, SPI_SETCARETWIDTH, "The
    // default and minimum value is 1"
    // (https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow).
    // Microsoft documents no default for the others, so theirs are unknown.
    private static readonly Dictionary<string, (long Value, string Shown)> WindowsDefaults = new(StringComparer.Ordinal)
    {
        ["caretWidth"] = (1, "1 pixel")
    };

    // NVDA's settings outside the reporting settings, with their defaults
    // from NVDA 2026.2's configuration specification
    // (https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/config/configSpec.py),
    // and the member of the settings record each is written as.
    private static readonly (string Section, string Key, string Member, string Default)[] NvdaOtherSettings =
    [
        ("keyboard", "keyboardLayout", "keyboardLayout", NvdaSettings.DefaultKeyboardLayout),
        ("keyboard", "NVDAModifierKeys", "nvdaModifierKeys", NvdaSettings.DefaultNvdaModifierKeys.ToString(CultureInfo.InvariantCulture)),
        ("keyboard", "multiPressTimeout", "multiPressTimeout", NvdaSettings.DefaultMultiPressTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture)),
        ("virtualBuffers", "autoPassThroughOnFocusChange", "autoPassThroughOnFocusChange", "true"),
        ("virtualBuffers", "autoPassThroughOnCaretMove", "autoPassThroughOnCaretMove", "false"),
        ("virtualBuffers", "trapNonCommandGestures", "trapNonCommandGestures", "true"),
        ("virtualBuffers", "enableOnPageLoad", "enableOnPageLoad", "true"),
        ("mouse", "enableMouseTracking", "enableMouseTracking", "true"),
        ("mouse", "ignoreInjectedMouseInput", "ignoreInjectedMouseInput", "false"),
        ("mouse", "mouseTextUnit", "mouseTextUnit", NvdaSettings.DefaultMouseTextUnit),
        ("mouse", "reportObjectRoleOnMouseEnter", "reportObjectRoleOnMouseEnter", "false"),
        ("mouse", "audioCoordinatesOnMouseMove", "audioCoordinatesOnMouseMove", "false"),
        ("mouse", "reportMouseShapeChanges", "reportMouseShapeChanges", "false")
    ];

    /// <param name="events">
    /// The recording's settings records as the playback index keeps them,
    /// for NVDA's settings reads, the values sent to each page, and each
    /// record's event, which the archive's own events do not hold when it is
    /// read from the database or a recording file.
    /// </param>
    /// <param name="firstPageTime">The time of the first page visit's start, for the values first sent to the page; null where there is none.</param>
    public static RecordedSettings Read(SessionPlaybackArchive archive, IEnumerable<PlaybackIndexEvent> events, long? firstPageTime)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(events);
        var records = events
            .Where(item => item.Channel is WindowsPreferenceSettings.Channel or BrowserPreferenceSettings.Channel or AssistiveTechnologyRecords.Channel)
            .OrderBy(item => item.MonotonicNanoseconds)
            .ToList();
        var byTime = new Dictionary<(string Channel, long Time), List<PlaybackIndexEvent>>();
        foreach (var item in records)
        {
            if (item.Channel is WindowsPreferenceSettings.Channel or BrowserPreferenceSettings.Channel or AssistiveTechnologyRecords.Channel)
            {
                var key = (item.Channel, item.MonotonicNanoseconds);
                if (!byTime.TryGetValue(key, out var list))
                {
                    list = [];
                    byTime[key] = list;
                }

                list.Add(item);
            }
        }

        string? EventIdOf(string channel, long time, string? eventType = null) =>
            byTime.TryGetValue((channel, time), out var list)
                ? list.FirstOrDefault(item => eventType is null || item.EventType == eventType)?.EventId
                : null;

        var changes = new List<SettingChange>();
        var (windows, windowsNote) = ReadWindows(archive, EventIdOf, changes);
        var (browser, browserNote) = ReadBrowser(archive, records, firstPageTime, EventIdOf, changes);
        var (assistive, assistiveNote) = ReadAssistive(archive, records, EventIdOf, changes);
        ReadMagnifier(archive, changes);
        return new RecordedSettings(
            windows, windowsNote, browser, browserNote, assistive, assistiveNote,
            [.. changes.OrderBy(change => change.Time).ThenBy(change => change.Group, StringComparer.Ordinal).ThenBy(change => change.Setting, StringComparer.Ordinal)]);
    }

    private static (List<SettingRow>, string?) ReadWindows(
        SessionPlaybackArchive archive, Func<string, long, string?, string?> eventIdOf, List<SettingChange> changes)
    {
        var timeline = archive.WindowsPreferences;
        if (!timeline.Recorded || timeline.StartTime is not { } start)
        {
            return ([], "The recording holds no record of the Windows settings, so they are unknown.");
        }

        var source = new SettingSource(start, "settings:windows-start",
            "Recorded: the Windows settings read at the start of the recording (system.preferences, windows-preferences).",
            eventIdOf(WindowsPreferenceSettings.Channel, start, WindowsPreferenceSettings.SnapshotEventType));
        var rows = new List<SettingRow>();
        foreach (var row in timeline.RowsAt(start, null).Where(row => row.Group != MagnifierGroup))
        {
            var (state, shown) = WindowsDefault(row.Key, timeline.ReadingAt(row.Key, start));
            rows.Add(new SettingRow(WindowsAppendix, row.Group, row.Setting, row.Value, state, shown, source));
        }

        foreach (var setting in WindowsPreferenceSettings.All)
        {
            foreach (var time in timeline.ChangeTimesOf(setting.Name))
            {
                var before = timeline.RowsAt(time - 1, null).FirstOrDefault(row => row.Key == setting.Name);
                var after = timeline.RowsAt(time, null).FirstOrDefault(row => row.Key == setting.Name);
                if (before is null || after is null || before.Value == after.Value)
                {
                    continue;
                }

                changes.Add(new SettingChange(time, null, "Windows, " + after.Group, after.Setting, before.Value, after.Value, null,
                    [new SettingSource(time, $"settings:windows-change:{time.ToString(CultureInfo.InvariantCulture)}",
                        $"Recorded: a change of a Windows setting, {after.Setting} (system.preferences, windows-preference-changed).",
                        eventIdOf(WindowsPreferenceSettings.Channel, time, WindowsPreferenceSettings.ChangeEventType))]));
            }
        }

        return (rows, null);
    }

    private static (DefaultState, string?) WindowsDefault(string setting, JsonElement? reading)
    {
        if (reading is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("value", out var read) || read.ValueKind == JsonValueKind.Null)
        {
            return (DefaultState.Unknown, null);
        }

        if (!WindowsDefaults.TryGetValue(setting, out var known))
        {
            return (DefaultState.Unknown, null);
        }

        return read.TryGetInt64(out var number) && number == known.Value
            ? (DefaultState.Default, known.Shown)
            : (DefaultState.NotDefault, known.Shown);
    }

    private static (List<SettingRow>, string?) ReadBrowser(
        SessionPlaybackArchive archive, IReadOnlyList<PlaybackIndexEvent> records, long? firstPageTime, Func<string, long, string?, string?> eventIdOf, List<SettingChange> changes)
    {
        var timeline = archive.BrowserPreferences;
        if (!timeline.HasRecords)
        {
            return ([], "The recording holds no record of the browser's settings, so they are unknown.");
        }

        var rows = new List<SettingRow>();
        string? note = null;
        if (timeline.StartTime is { } start)
        {
            var source = new SettingSource(start, "settings:browser-start",
                "Recorded: the browser's settings read when its profile was loaded (browser.preferences, browser-preferences).",
                eventIdOf(BrowserPreferenceSettings.Channel, start, BrowserPreferenceSettings.SnapshotEventType));
            foreach (var row in timeline.RowsAt(start).Where(row => row.Group == BrowserPreferenceTimeline.BrowserGroup))
            {
                var (state, value) = row.Key == BrowserPreferenceTimeline.DefaultZoomKey
                    ? ZoomDefault(row.Value)
                    : BrowserDefault(timeline.IsDefaultAt(row.Key[BrowserPreferenceTimeline.BrowserKeyPrefix.Length..], start), row.Value);
                rows.Add(new SettingRow(BrowserAppendix, row.Group, row.Setting, value, state, null, source));
            }
        }
        else
        {
            note = "The recording holds no record of the browser's settings when its profile was loaded, so they are unknown.";
        }

        // The values sent to the first page visited, as first sent.
        if (firstPageTime is { } pageTime)
        {
            foreach (var row in timeline.RowsAt(pageTime).Where(row => row.Group == BrowserPreferenceTimeline.PageGroup))
            {
                var source = row.SetAt is { } sentAt && row.Key != BrowserPreferenceTimeline.PageKey
                    ? new SettingSource(sentAt, $"settings:browser-sent:{sentAt.ToString(CultureInfo.InvariantCulture)}",
                        "Recorded: the values the browser sent to the page (browser.preferences, web-preferences-sent).",
                        eventIdOf(BrowserPreferenceSettings.Channel, sentAt, BrowserPreferenceSettings.SentEventType))
                    : new SettingSource(pageTime, "settings:browser-first-page",
                        "Derived by the recorder: the first page visited, whose values sent are given.", null);
                rows.Add(new SettingRow(BrowserAppendix, row.Group, row.Setting, row.Value, DefaultState.NotASetting, null, source));
            }
        }

        // Each change of a browser setting and of the default zoom.
        foreach (var key in BrowserPreferenceSettings.Browser.Select(setting => BrowserPreferenceTimeline.BrowserKeyPrefix + setting.Name)
            .Append(BrowserPreferenceTimeline.DefaultZoomKey))
        {
            foreach (var time in timeline.ChangeTimesOf(key) ?? [])
            {
                var before = timeline.RowsAt(time - 1).FirstOrDefault(row => row.Key == key);
                var after = timeline.RowsAt(time).FirstOrDefault(row => row.Key == key);
                if (before is null || after is null || before.Value == after.Value)
                {
                    continue;
                }

                changes.Add(new SettingChange(time, null, "Browser", after.Setting, before.Value, after.Value, null,
                    [new SettingSource(time, $"settings:browser-change:{time.ToString(CultureInfo.InvariantCulture)}",
                        $"Recorded: a change of a browser setting, {after.Setting} (browser.preferences).",
                        eventIdOf(BrowserPreferenceSettings.Channel, time, null))]));
            }
        }

        // A value sent to the page again while the same page is shown; a
        // new page's first values are not a change.
        foreach (var sent in records.Where(item =>
            item.Channel == BrowserPreferenceSettings.Channel && item.EventType == BrowserPreferenceSettings.SentEventType))
        {
            var time = sent.MonotonicNanoseconds;
            var before = timeline.RowsAt(time - 1).Where(row => row.Group == BrowserPreferenceTimeline.PageGroup).ToList();
            var after = timeline.RowsAt(time).Where(row => row.Group == BrowserPreferenceTimeline.PageGroup).ToList();
            var pageBefore = before.FirstOrDefault(row => row.Key == BrowserPreferenceTimeline.PageKey);
            var pageAfter = after.FirstOrDefault(row => row.Key == BrowserPreferenceTimeline.PageKey);
            if (pageBefore?.SetAt is null || pageAfter is null || pageBefore.SetAt != pageAfter.SetAt)
            {
                continue;
            }

            foreach (var row in after.Where(row => row.Key != BrowserPreferenceTimeline.PageKey))
            {
                var was = before.FirstOrDefault(item => item.Key == row.Key);
                if (was is null || was.Value == row.Value || was.SetAt is null || row.SetAt != time)
                {
                    continue;
                }

                changes.Add(new SettingChange(time, null, "Sent to the page", row.Setting, was.Value, row.Value, $"on {pageAfter.Value}",
                    [new SettingSource(time, $"settings:browser-sent:{time.ToString(CultureInfo.InvariantCulture)}",
                        "Recorded: the values the browser sent to the page (browser.preferences, web-preferences-sent).", sent.EventId)]));
            }
        }

        return (rows, note);
    }

    private static (DefaultState, string) BrowserDefault(bool? isDefault, string value)
    {
        const string suffix = " (default)";
        return isDefault switch
        {
            true => (DefaultState.Default, value.EndsWith(suffix, StringComparison.Ordinal) ? value[..^suffix.Length] : value),
            false => (DefaultState.NotDefault, value),
            _ => (DefaultState.Unknown, value)
        };
    }

    // The zoom map starts at 100 percent; with no default set, or one of
    // 100 percent, the default zoom is the browser's own.
    private static (DefaultState, string) ZoomDefault(string value) =>
        value.Contains("no default set", StringComparison.Ordinal) || value.StartsWith("100%", StringComparison.Ordinal)
            ? (DefaultState.Default, value)
            : (DefaultState.NotDefault, value);

    private static (List<SettingRow>, string?) ReadAssistive(
        SessionPlaybackArchive archive, IReadOnlyList<PlaybackIndexEvent> records, Func<string, long, string?, string?> eventIdOf, List<SettingChange> changes)
    {
        var rows = new List<SettingRow>();
        var timeline = archive.AssistiveTechnology;
        string? note = null;
        if (!timeline.Recorded)
        {
            note = "The recording holds no assistive technology records, so what was running is unknown.";
        }
        else
        {
            // Each product's rows at the start, but its sound, which is
            // activity rather than a setting.
            foreach (var row in timeline.RowsAt(0))
            {
                if (row.Key.EndsWith(".sound", StringComparison.Ordinal))
                {
                    continue;
                }

                var source = row.SetAt is { } at
                    ? new SettingSource(at, $"settings:at:{at.ToString(CultureInfo.InvariantCulture)}",
                        "Recorded: an assistive technology record (system.assistive-technology).",
                        eventIdOf(AssistiveTechnologyRecords.Channel, at, null))
                    : new SettingSource(0, "settings:at-start",
                        "Recorded: the assistive technology watched from the start of the recording (system.assistive-technology).", null);
                rows.Add(new SettingRow(AssistiveAppendix, row.Group, row.Setting, row.Value, DefaultState.NotASetting, null, source));
            }

            // NVDA's settings as first read, and those that differ each
            // time it starts again with others.
            var product = AssistiveTechnologyRecords.Nvda.Product;
            var reads = records
                .Where(item => item.Channel == AssistiveTechnologyRecords.Channel && item.EventType == NvdaSettings.SettingsEventType)
                .Select(item => (Event: item, Settings: timeline.ScreenReaderAt(product, item.MonotonicNanoseconds)?.Settings))
                .Where(read => read.Settings is { ValueKind: JsonValueKind.Object })
                .ToList();
            List<SettingRow>? previous = null;
            foreach (var (read, settings) in reads)
            {
                var time = read.MonotonicNanoseconds;
                var source = new SettingSource(time, $"settings:nvda:{time.ToString(CultureInfo.InvariantCulture)}",
                    "Recorded: NVDA's settings, read from its configuration folder when it was seen running (system.assistive-technology, assistive-technology-settings).",
                    read.EventId);
                var nvdaRows = NvdaRows(settings!.Value, source);
                if (previous is null)
                {
                    rows.AddRange(nvdaRows);
                }
                else
                {
                    foreach (var row in nvdaRows)
                    {
                        if (previous.FirstOrDefault(item => item.Setting == row.Setting) is { } was && was.Value != row.Value)
                        {
                            changes.Add(new SettingChange(time, null, "NVDA's settings", row.Setting, was.Value, row.Value, "when NVDA started again", [source]));
                        }
                    }
                }

                previous = nvdaRows;
            }

            if (reads.Count == 0)
            {
                note = "NVDA's settings were not read during the recording, so they are unknown.";
            }

            // Each product starting or exiting during the recording.
            foreach (var name in timeline.RowsAt(0).Where(row => row.Key.EndsWith(".running", StringComparison.Ordinal)).Select(row => row.Setting).Distinct())
            {
                foreach (var (time, reason) in timeline.ScreenReaderRuns(name))
                {
                    var key = AssistiveTechnologyTimeline.RunningKey(name);
                    var before = timeline.RowsAt(time - 1).FirstOrDefault(row => row.Key == key);
                    var after = timeline.RowsAt(time).FirstOrDefault(row => row.Key == key);
                    changes.Add(new SettingChange(time, null, "Assistive technology", name, before?.Value ?? "unknown", after?.Value ?? "unknown", reason,
                        [new SettingSource(time, $"settings:at:{time.ToString(CultureInfo.InvariantCulture)}",
                            "Recorded: an assistive technology record (system.assistive-technology).",
                            eventIdOf(AssistiveTechnologyRecords.Channel, time, null))]));
                }
            }
        }

        // The Magnifier at the first captured frame.
        if (archive.Frames.Count > 0)
        {
            var frame = archive.Frames[0];
            var source = FrameSource(frame);
            foreach (var row in WindowsPreferenceTimeline.MagnifierRows(frame))
            {
                rows.Add(new SettingRow(AssistiveAppendix, "Magnifier", row.Setting, row.Value, DefaultState.NotASetting, null, source));
            }

            rows.Add(new SettingRow(AssistiveAppendix, "Magnifier", "Part of the screen shown",
                MagnifiedView.VisibleRegion(frame) is { } region ? Region(region) : "the whole screen, not magnified",
                DefaultState.NotASetting, null, source));
        }

        return (rows, note);
    }

    private static List<SettingRow> NvdaRows(JsonElement settings, SettingSource source)
    {
        const string group = "NVDA's settings";
        string? Text(string member) =>
            settings.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        var rows = new List<SettingRow>
        {
            new(AssistiveAppendix, group, "Copy", Text("copy") ?? "unknown", DefaultState.NotASetting, null, source),
            new(AssistiveAppendix, group, "Configuration folder", Text("configFolder") ?? "unknown", DefaultState.NotASetting, null, source)
        };
        var read = settings.TryGetProperty("read", out var readValue) && readValue.ValueKind == JsonValueKind.True;
        if (!read)
        {
            rows.Add(new(AssistiveAppendix, group, "Configuration", "not read: " + (Text("problem") ?? "no reason recorded"), DefaultState.Unknown, null, source));
        }

        var all = NvdaOtherSettings
            .Concat(NvdaSettings.ReportingSettings.Select(setting => (setting.Section, setting.Key, Member: setting.Key, setting.Default)));
        foreach (var (section, key, member, defaultValue) in all)
        {
            var label = $"[{section}] {key}";
            if (!read)
            {
                rows.Add(new(AssistiveAppendix, group, label, "not read", DefaultState.Unknown, defaultValue, source));
                continue;
            }

            var written = Text(member);
            if (written is null)
            {
                rows.Add(new(AssistiveAppendix, group, label, defaultValue + ", not written in nvda.ini", DefaultState.Default, defaultValue, source));
                continue;
            }

            rows.Add(new(AssistiveAppendix, group, label, written,
                SameNvdaValue(written, defaultValue) ? DefaultState.Default : DefaultState.NotDefault, defaultValue, source));
        }

        if (read)
        {
            var profiles = settings.TryGetProperty("profiles", out var list) && list.ValueKind == JsonValueKind.Array
                ? [.. list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
                : new List<string>();
            rows.Add(new(AssistiveAppendix, group, "Configuration profiles", profiles.Count == 0 ? "none" : string.Join(", ", profiles),
                profiles.Count == 0 ? DefaultState.Default : DefaultState.NotDefault, "none", source));
            var triggers = settings.TryGetProperty("profileTriggers", out var trigger) && trigger.ValueKind == JsonValueKind.True;
            rows.Add(new(AssistiveAppendix, group, "Profile triggers", triggers ? "present (profileTriggers.ini)" : "none",
                triggers ? DefaultState.NotDefault : DefaultState.Default, "none", source));
            var gestures = settings.TryGetProperty("customGestures", out var custom) && custom.ValueKind == JsonValueKind.Array
                ? [.. custom.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(item =>
                    $"{Member(item, "gesture")} for {Member(item, "script")} in {Member(item, "section")}")]
                : new List<string>();
            var gesturesProblem = Text("gesturesProblem");
            rows.Add(new(AssistiveAppendix, group, "Custom key commands",
                gesturesProblem is not null ? "not read: " + gesturesProblem : gestures.Count == 0 ? "none" : string.Join("; ", gestures),
                gesturesProblem is not null ? DefaultState.Unknown : gestures.Count == 0 ? DefaultState.Default : DefaultState.NotDefault, "none", source));
        }

        return rows;
    }

    private static string Member(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "unknown";

    // A value as configobj reads it against its default: booleans in any
    // of their spellings, numbers and words as written.
    private static bool SameNvdaValue(string written, string defaultValue)
    {
        if (defaultValue is "true" or "false")
        {
            return NvdaSettings.Boolean(written, defaultValue == "true") is { } value && value == (defaultValue == "true");
        }

        return string.Equals(written.Trim().Trim('"'), defaultValue, StringComparison.OrdinalIgnoreCase);
    }

    // The Magnifier's changes from each captured frame's reading: its level,
    // each run of changes less than the gap apart one change; its color
    // effect; and its periods of panning, the view moving at one level,
    // each move less than the gap after the one before, with the part of
    // the screen shown at the start and the end and the furthest reached.
    private static void ReadMagnifier(SessionPlaybackArchive archive, List<SettingChange> changes)
    {
        SessionVideoFrame? previous = null;
        (SessionVideoFrame First, SessionVideoFrame Last, string Before)? level = null;
        (SessionVideoFrame Start, SessionVideoFrame First, SessionVideoFrame Last, FrameRegion From, double Left, double Top, double Right, double Bottom)? pan = null;

        void CloseLevel()
        {
            if (level is { } run)
            {
                var after = LevelText(run.Last);
                if (after != run.Before)
                {
                    var sources = new List<SettingSource> { FrameSource(run.First) };
                    if (!ReferenceEquals(run.Last, run.First))
                    {
                        sources.Add(FrameSource(run.Last));
                    }

                    changes.Add(new SettingChange(run.First.MonotonicNanoseconds, null, "Magnifier", "Full screen level", run.Before, after, null, sources));
                }

                level = null;
            }
        }

        void ClosePan()
        {
            if (pan is { } run)
            {
                var to = MagnifiedView.VisibleRegion(run.Last)!.Value;
                var detail = string.Create(CultureInfo.InvariantCulture,
                    $"at {LevelText(run.Last)}; the furthest the view reached: left {run.Left:0}, right {run.Right:0}, top {run.Top:0}, bottom {run.Bottom:0}, in the screen's pixels");
                changes.Add(new SettingChange(run.First.MonotonicNanoseconds, run.Last.MonotonicNanoseconds, "Magnifier", "Panning",
                    Region(run.From), Region(to), detail, [FrameSource(run.Start), FrameSource(run.Last)]));
                pan = null;
            }
        }

        foreach (var frame in archive.Frames)
        {
            if (frame.Magnification is null)
            {
                continue;
            }

            if (previous is { } before)
            {
                var time = frame.MonotonicNanoseconds;
                var levelChanged = before.Magnification!.Level != frame.Magnification.Level;
                var moved = before.Magnification.X != frame.Magnification.X || before.Magnification.Y != frame.Magnification.Y;
                if (level is { } open && time - open.Last.MonotonicNanoseconds >= MagnifierGapNanoseconds)
                {
                    CloseLevel();
                }

                if (pan is { } panning && time - panning.Last.MonotonicNanoseconds >= MagnifierGapNanoseconds)
                {
                    ClosePan();
                }

                if (levelChanged)
                {
                    ClosePan();
                    level = level is { } run ? (run.First, frame, run.Before) : (frame, frame, LevelText(before));
                }
                else if (moved && MagnifiedView.VisibleRegion(before) is { } from && MagnifiedView.VisibleRegion(frame) is { } region)
                {
                    if (pan is { } run)
                    {
                        pan = (run.Start, run.First, frame, run.From,
                            Math.Min(run.Left, region.X), Math.Min(run.Top, region.Y),
                            Math.Max(run.Right, region.X + region.Width), Math.Max(run.Bottom, region.Y + region.Height));
                    }
                    else
                    {
                        pan = (before, frame, frame, from,
                            Math.Min(from.X, region.X), Math.Min(from.Y, region.Y),
                            Math.Max(from.X + from.Width, region.X + region.Width), Math.Max(from.Y + from.Height, region.Y + region.Height));
                    }
                }

                var effectBefore = EffectText(before);
                var effectAfter = EffectText(frame);
                if (before.ColorEffect is not null && frame.ColorEffect is not null && effectBefore != effectAfter)
                {
                    changes.Add(new SettingChange(time, null, "Magnifier", "Full screen color effect", effectBefore, effectAfter, null, [FrameSource(frame)]));
                }
            }

            previous = frame;
        }

        CloseLevel();
        ClosePan();
    }

    private static string LevelText(SessionVideoFrame frame) =>
        WindowsPreferenceTimeline.MagnifierRows(frame).First(row => row.Key == WindowsPreferenceTimeline.MagnifierKeyPrefix + MagnifierChanges.Level).Value;

    private static string EffectText(SessionVideoFrame frame) =>
        WindowsPreferenceTimeline.MagnifierRows(frame).First(row => row.Key == WindowsPreferenceTimeline.MagnifierKeyPrefix + MagnifierChanges.ColorEffect).Value;

    /// <summary>A part of the screen, in the screen's pixels.</summary>
    public static string Region(FrameRegion region) => string.Create(CultureInfo.InvariantCulture,
        $"the part from ({region.X:0}, {region.Y:0}) to ({region.X + region.Width:0}, {region.Y + region.Height:0}), {region.Width:0} by {region.Height:0} pixels");

    /// <summary>A captured frame as the evidence list gives it, keyed as the moments' frames are, so that one frame has one reference.</summary>
    public static SettingSource FrameSource(SessionVideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var captured = new CapturedFrame(frame.MonotonicNanoseconds, frame.Path, frame.AbsolutePath, frame.Width, frame.Height, frame.Magnification?.Level);
        return new SettingSource(frame.MonotonicNanoseconds, $"frame:{frame.MonotonicNanoseconds.ToString(CultureInfo.InvariantCulture)}",
            RecordingSummaryVisionKind.FrameText(captured), null);
    }
}

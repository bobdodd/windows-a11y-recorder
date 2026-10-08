using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>A <c>system.preferences</c> record of a recording, as playback keeps it.</summary>
public sealed record WindowsPreferenceRecord(long MonotonicNanoseconds, string EventType, JsonElement Payload);

/// <summary>
/// One row of the player's properties panel: the group and the setting, its
/// value at the frame shown, and when it was set: at the start of the
/// recording, at the recording time of its last change, or with the frame.
/// <see cref="Changed"/> is true when it was set at the frame's time or in
/// the second before it. <see cref="Key"/> names what the row shows, for
/// stepping through its changes: the setting's name, or a Magnifier part
/// prefixed with <see cref="WindowsPreferenceTimeline.MagnifierKeyPrefix"/>.
/// </summary>
public sealed record PropertyRow(
    string Group,
    string Setting,
    string Value,
    string WhenSet,
    long? SetAt,
    bool Changed,
    string Key = "")
{
    /// <summary>The when-set column: the time, and "changed" in text when changed.</summary>
    public string WhenSetShown => Changed ? $"{WhenSet}, changed" : WhenSet;

    /// <summary>What a screen reader reads for the row: setting, value, when set.</summary>
    public string Spoken => Changed
        ? $"{Setting}, {Value}, {WhenSet}, changed"
        : $"{Setting}, {Value}, {WhenSet}";
}

/// <summary>
/// The Windows settings in effect at each time of a recording, from its
/// <c>windows-preferences</c> and <c>windows-preference-changed</c> records,
/// and the rows of the properties panel. See
/// docs/architecture/accessibility-preferences.md, "The properties panel".
/// </summary>
public sealed class WindowsPreferenceTimeline
{
    public const string MagnifierGroup = "Magnifier";
    public const string MagnifierKeyPrefix = "magnifier.";
    private const long ChangedWindowNanoseconds = 1_000_000_000;

    private static readonly Dictionary<string, (string Group, string Label)> Labels = new(StringComparer.Ordinal)
    {
        ["monitors"] = ("Display", "Monitors and scale"),
        ["textScaleFactor"] = ("Display", "Text size"),
        ["appsUseLightTheme"] = ("Display", "App light theme"),
        ["transparencyEffects"] = ("Display", "Transparency effects"),
        ["colorFilterActive"] = ("Display", "Color filter"),
        ["colorFilterType"] = ("Display", "Color filter type"),
        ["highContrast"] = ("Display", "Contrast theme"),
        ["highContrastScheme"] = ("Display", "Contrast theme name"),
        ["accentColor"] = ("Display", "Accent color"),
        ["animationsEnabled"] = ("Motion", "Animation effects"),
        ["uiEffects"] = ("Motion", "User interface effects"),
        ["menuAnimation"] = ("Motion", "Menu animation"),
        ["menuFade"] = ("Motion", "Menu fade"),
        ["comboBoxAnimation"] = ("Motion", "Combo box animation"),
        ["cursorWidth"] = ("Pointer and focus", "Cursor width"),
        ["cursorHeight"] = ("Pointer and focus", "Cursor height"),
        ["caretWidth"] = ("Pointer and focus", "Caret width"),
        ["caretBlinkTime"] = ("Pointer and focus", "Caret blink time"),
        ["focusBorderWidth"] = ("Pointer and focus", "Focus border width"),
        ["focusBorderHeight"] = ("Pointer and focus", "Focus border height"),
        ["keyboardCues"] = ("Pointer and focus", "Always underline access keys"),
        ["autoHideScrollBars"] = ("Pointer and focus", "Hide scroll bars automatically"),
        ["messageDuration"] = ("Pointer and focus", "Show notifications for"),
        ["stickyKeys"] = ("Keyboard and mouse assistance", "Sticky keys"),
        ["filterKeys"] = ("Keyboard and mouse assistance", "Filter keys"),
        ["toggleKeys"] = ("Keyboard and mouse assistance", "Toggle keys"),
        ["mouseKeys"] = ("Keyboard and mouse assistance", "Mouse keys")
    };

    // The filter types Chromium's comment gives for FilterType,
    // ui/native_theme/os_settings_provider_win.cc, lines 217 to 231.
    private static readonly string[] FilterTypes =
        ["greyscale", "invert", "greyscale inverted", "deuteranopia", "protanopia", "tritanopia"];

    private readonly Dictionary<string, JsonElement> _start = new(StringComparer.Ordinal);
    private readonly List<(long Time, string Setting, JsonElement Reading)> _changes = [];

    public WindowsPreferenceTimeline(IEnumerable<WindowsPreferenceRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ordered = records.OrderBy(record => record.MonotonicNanoseconds).ToList();
        // The first windows-preferences record gives the values at the start.
        var start = ordered.FirstOrDefault(record =>
            record.EventType == WindowsPreferenceSettings.SnapshotEventType &&
            record.Payload.ValueKind == JsonValueKind.Object &&
            record.Payload.TryGetProperty("settings", out var settings) &&
            settings.ValueKind == JsonValueKind.Object);
        if (start is not null)
        {
            StartTime = start.MonotonicNanoseconds;
            foreach (var setting in start.Payload.GetProperty("settings").EnumerateObject())
            {
                _start[setting.Name] = setting.Value.Clone();
            }
        }

        foreach (var record in ordered.Where(record => record.EventType == WindowsPreferenceSettings.ChangeEventType))
        {
            if (record.Payload.ValueKind == JsonValueKind.Object &&
                record.Payload.TryGetProperty("setting", out var name) && name.ValueKind == JsonValueKind.String &&
                record.Payload.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object &&
                current.TryGetProperty(name.GetString()!, out var reading))
            {
                _changes.Add((record.MonotonicNanoseconds, name.GetString()!, reading.Clone()));
            }
        }
    }

    public static WindowsPreferenceTimeline Empty { get; } = new([]);

    /// <summary>Whether the recording holds the Windows settings at its start.</summary>
    public bool Recorded => _start.Count > 0;

    /// <summary>The time of the record of the values at the start, or null.</summary>
    public long? StartTime { get; }

    /// <summary>The recording times of the changes, in order.</summary>
    public IReadOnlyList<long> ChangeTimes => [.. _changes.Select(change => change.Time)];

    /// <summary>The recording times of one setting's changes, in order, without repeats.</summary>
    public IReadOnlyList<long> ChangeTimesOf(string setting) =>
        [.. _changes.Where(change => change.Setting == setting).Select(change => change.Time).Distinct()];

    /// <summary>
    /// The rows of the panel at a time: the Magnifier group from the frame
    /// shown, then each Windows setting from the most recent record at or
    /// before the time.
    /// </summary>
    public IReadOnlyList<PropertyRow> RowsAt(long time, SessionVideoFrame? frame, MagnifierChangeTimeline? magnifier = null)
    {
        var rows = new List<PropertyRow>(MagnifierRows(frame, magnifier, time));
        foreach (var setting in WindowsPreferenceSettings.All)
        {
            var (group, label) = Labels[setting.Name];
            if (!Recorded)
            {
                rows.Add(new PropertyRow(group, label, "not recorded", string.Empty, null, false, setting.Name));
                continue;
            }

            JsonElement? reading = _start.TryGetValue(setting.Name, out var startReading) ? startReading : null;
            long? setAt = null;
            foreach (var change in _changes)
            {
                if (change.Time > time)
                {
                    break;
                }

                if (change.Setting == setting.Name)
                {
                    reading = change.Reading;
                    setAt = change.Time;
                }
            }

            var whenSet = setAt is { } at
                ? Clock(at)
                : setting.Name == WindowsPreferenceSettings.CaretBlinkTime ? "at start, read once" : "at start";
            rows.Add(new PropertyRow(
                group,
                label,
                reading is { } value ? Describe(setting, value) : "not recorded",
                whenSet,
                setAt,
                setAt is { } changedAt && time - changedAt < ChangedWindowNanoseconds,
                setting.Name));
        }

        return rows;
    }

    /// <summary>
    /// The Magnifier rows, read from the frame shown, as the participant's
    /// view is (docs/architecture/magnified-view-playback.md). With the
    /// recording's Magnifier change records, each row is set at its last
    /// change at or before <paramref name="time"/>, or at the start;
    /// without them, with the frame.
    /// </summary>
    public static IReadOnlyList<PropertyRow> MagnifierRows(
        SessionVideoFrame? frame,
        MagnifierChangeTimeline? magnifier = null,
        long time = 0)
    {
        const string withFrame = "with this frame";
        PropertyRow Row(string label, string part, string value)
        {
            var key = MagnifierKeyPrefix + part;
            if (frame is null)
            {
                return new(MagnifierGroup, label, value, string.Empty, null, false, key);
            }

            if (magnifier is not { Recorded: true })
            {
                return new(MagnifierGroup, label, value, withFrame, null, false, key);
            }

            var setAt = PropertyChangeSteps.InEffect(magnifier.Times(part), time);
            return new(
                MagnifierGroup,
                label,
                value,
                setAt is { } at ? Clock(at) : "at start",
                setAt,
                setAt is { } changedAt && time - changedAt < ChangedWindowNanoseconds,
                key);
        }

        if (frame is null)
        {
            return
            [
                Row("Full screen level", MagnifierChanges.Level, "no frame"),
                Row("Full screen position", MagnifierChanges.Position, "no frame"),
                Row("Full screen color effect", MagnifierChanges.ColorEffect, "no frame")
            ];
        }

        string level, position;
        if (frame.Magnification is { } magnification)
        {
            var percent = Math.Round(magnification.Level * 100).ToString(CultureInfo.CurrentCulture);
            level = MagnifiedView.IsMagnified(frame) ? $"{percent} percent" : $"{percent} percent, not magnified";
            position = string.Create(CultureInfo.CurrentCulture, $"{magnification.X}, {magnification.Y}");
        }
        else
        {
            // The frame holds no reading: an older recording, or one the
            // reading failed for, whose problem is in the frame's record.
            level = "not recorded with this frame";
            position = level;
        }

        var effect = frame.ColorEffect is { } colorEffect
            ? ColorEffect.IsIdentity(colorEffect) ? "none"
            : ColorEffect.IsInversion(colorEffect) ? "inverted colors"
            : "a color effect"
            : "not recorded with this frame";
        return
        [
            Row("Full screen level", MagnifierChanges.Level, level),
            Row("Full screen position", MagnifierChanges.Position, position),
            Row("Full screen color effect", MagnifierChanges.ColorEffect, effect)
        ];
    }

    /// <summary>A setting's reading as the panel shows it.</summary>
    public static string Describe(WindowsPreferenceSetting setting, JsonElement reading)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (reading.ValueKind != JsonValueKind.Object || !reading.TryGetProperty("value", out var value))
        {
            return "not recorded";
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return reading.TryGetProperty("problem", out var problem) && problem.ValueKind == JsonValueKind.String
                ? $"not read: {problem.GetString()}"
                : "none";
        }

        var culture = CultureInfo.CurrentCulture;
        return setting.Name switch
        {
            "monitors" => DescribeMonitors(value),
            "textScaleFactor" => $"{Math.Round(value.GetDouble() * 100).ToString(culture)} percent",
            "colorFilterType" => value.GetInt64() is var type && type >= 0 && type < FilterTypes.Length
                ? $"{FilterTypes[type]} ({type.ToString(culture)})"
                : value.GetInt64().ToString(culture),
            "highContrastScheme" => value.GetString() is { Length: > 0 } name ? name : "none",
            "caretBlinkTime" => value.GetInt64() == uint.MaxValue
                ? "does not blink"
                : $"{value.GetInt64().ToString(culture)} ms",
            "messageDuration" => $"{value.GetInt64().ToString(culture)} seconds",
            "cursorWidth" or "cursorHeight" => $"{value.GetDouble().ToString(culture)} pixels",
            "caretWidth" or "focusBorderWidth" or "focusBorderHeight" =>
                value.GetInt64() == 1 ? "1 pixel" : $"{value.GetInt64().ToString(culture)} pixels",
            _ => setting.Kind switch
            {
                WindowsPreferenceKind.Boolean => value.GetBoolean() ? "on" : "off",
                WindowsPreferenceKind.Integer => value.GetInt64().ToString(culture),
                WindowsPreferenceKind.Number => value.GetDouble().ToString(culture),
                _ => value.ToString()
            }
        };
    }

    private static string DescribeMonitors(JsonElement monitors)
    {
        var culture = CultureInfo.CurrentCulture;
        var parts = new List<string>();
        foreach (var monitor in monitors.EnumerateArray())
        {
            var bounds = monitor.GetProperty("bounds");
            var dpi = monitor.TryGetProperty("dpiX", out var dpiX) && dpiX.ValueKind == JsonValueKind.Number
                ? $"{Math.Round(dpiX.GetInt64() * 100 / 96.0).ToString(culture)} percent ({dpiX.GetInt64().ToString(culture)} DPI)"
                : "scale not read";
            var primary = monitor.TryGetProperty("isPrimary", out var isPrimary) && isPrimary.ValueKind == JsonValueKind.True
                ? ", primary"
                : string.Empty;
            parts.Add($"{monitor.GetProperty("deviceName").GetString()}: " +
                $"{bounds.GetProperty("width").GetInt32().ToString(culture)} by " +
                $"{bounds.GetProperty("height").GetInt32().ToString(culture)}, {dpi}{primary}");
        }

        return parts.Count == 0 ? "none" : string.Join("; ", parts);
    }

    // A recording time as the player's clock shows it.
    internal static string Clock(long nanoseconds)
    {
        var value = TimeSpan.FromTicks(Math.Max(0, nanoseconds) / 100);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}");
    }
}

using System.Text.Json.Nodes;
using Recorder.Contracts;

namespace Recorder.Collectors.Windowing;

/// <summary>
/// One reading of a Windows setting: its value, or null and the reason it
/// could not be read.
/// </summary>
public sealed record WindowsPreferenceReading(JsonNode? Value, string? Problem)
{
    public static WindowsPreferenceReading Read(JsonNode? value) => new(value, null);

    public static WindowsPreferenceReading Failed(string problem) => new(null, problem);

    /// <summary>Whether two readings hold the same value and problem.</summary>
    public bool SameAs(WindowsPreferenceReading other) =>
        string.Equals(Problem, other.Problem, StringComparison.Ordinal) &&
        JsonNode.DeepEquals(Value, other.Value);

    public JsonObject ToJson() => new()
    {
        ["value"] = Value?.DeepClone(),
        ["problem"] = Problem
    };
}

/// <summary>
/// Builds the <c>system.preferences</c> payloads and finds the settings
/// whose readings differ, so that only a differing value is recorded as a
/// change. See docs/architecture/accessibility-preferences.md.
/// </summary>
public static class WindowsPreferencePayloads
{
    /// <summary>
    /// The settings whose readings differ, in the payload's order. The caret
    /// blink rate is read at the start only and is never a change.
    /// </summary>
    public static IReadOnlyList<string> Changed(
        IReadOnlyDictionary<string, WindowsPreferenceReading> previous,
        IReadOnlyDictionary<string, WindowsPreferenceReading> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var changed = new List<string>();
        foreach (var setting in WindowsPreferenceSettings.All)
        {
            if (setting.Name == WindowsPreferenceSettings.CaretBlinkTime ||
                !previous.TryGetValue(setting.Name, out var before) ||
                !current.TryGetValue(setting.Name, out var after))
            {
                continue;
            }

            if (!before.SameAs(after))
            {
                changed.Add(setting.Name);
            }
        }

        return changed;
    }

    /// <summary>The <c>windows-preferences</c> payload, with every setting.</summary>
    public static JsonObject Snapshot(
        string reason,
        IReadOnlyDictionary<string, WindowsPreferenceReading> readings,
        IReadOnlyDictionary<string, bool> uiSettingsEvents)
    {
        var settings = new JsonObject();
        foreach (var setting in WindowsPreferenceSettings.All)
        {
            settings[setting.Name] = readings.TryGetValue(setting.Name, out var reading)
                ? reading.ToJson()
                : WindowsPreferenceReading.Failed("not read").ToJson();
        }

        var events = new JsonObject();
        foreach (var name in WindowsPreferenceSettings.UiSettingsEvents)
        {
            events[name] = uiSettingsEvents.TryGetValue(name, out var present) && present;
        }

        return new JsonObject
        {
            ["reason"] = reason,
            ["uiSettingsEvents"] = events,
            ["settings"] = settings
        };
    }

    /// <summary>The <c>windows-preference-changed</c> payload for one setting.</summary>
    public static JsonObject Change(
        string setting,
        WindowsPreferenceReading previous,
        WindowsPreferenceReading current,
        WindowsPreferenceNotice notice) => new()
        {
            ["setting"] = setting,
            ["previous"] = new JsonObject { [setting] = previous.ToJson() },
            ["current"] = new JsonObject { [setting] = current.ToJson() },
            ["notice"] = notice.ToJson()
        };
}

/// <summary>
/// The notice that led to a reading: a <c>WM_SETTINGCHANGE</c> with its
/// uiAction and area, a registry key's change, a UISettings event, or a
/// <c>WM_DISPLAYCHANGE</c> or <c>WM_DPICHANGED</c>.
/// </summary>
public sealed record WindowsPreferenceNotice(string Kind, long? UiAction = null, string? Area = null, string? Source = null)
{
    public JsonObject ToJson() => new()
    {
        ["kind"] = Kind,
        ["uiAction"] = UiAction,
        ["area"] = Area,
        ["source"] = Source
    };
}

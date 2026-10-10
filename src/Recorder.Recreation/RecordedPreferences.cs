using System.Globalization;
using System.Text;
using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Recreation;

/// <summary>
/// The recorded page values a recreation gives its page (accessibility
/// preferences stage 3): the text of the root element's
/// data-a11y-recorded-preferences attribute, which the instrumented renderer
/// reads in recreation mode (chromium/recorder_bridge/recreation_preferences.h
/// gives its form), and the evidence panel's notes on them. See
/// docs/architecture/accessibility-preferences.md, "Stage 3".
/// </summary>
public static class RecordedPreferences
{
    public const string AttributeName = "data-a11y-recorded-preferences";

    /// <summary>
    /// The attribute's text: each field as "field NAME TYPE VALUE" and each
    /// color as "color MAP NAME AARRGGBB",
    /// separated by "; ". A value not of its field's kind, and a color not
    /// written "#AARRGGBB", is left out. Empty when there is nothing to
    /// apply.
    /// </summary>
    public static string AttributeText(BrowserPageValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return AttributeText(values, null)!;
    }

    /// <summary>
    /// The attribute's text for the page's values, when recorded, and the
    /// recorded layout zoom factor the recreation is laid out at, when
    /// known, as a "layoutZoom" entry; null when there is neither.
    /// </summary>
    public static string? AttributeText(BrowserPageValues? values, double? layoutZoomFactor)
    {
        var entries = new List<string>();
        foreach (var field in values?.Fields ?? [])
        {
            if (Field(field.Setting, field.Value) is { } entry)
            {
                entries.Add(entry);
            }
        }

        // The recorded browser zoom level is not given (revised with the
        // owner on 2026-10-09): its effect on the page is in the recorded
        // layout zoom factor.

        if (layoutZoomFactor is { } layoutZoom && double.IsFinite(layoutZoom) && layoutZoom > 0)
        {
            entries.Add("layoutZoom " + layoutZoom.ToString("R", CultureInfo.InvariantCulture));
        }

        if (values is null)
        {
            return entries.Count == 0 ? null : string.Join("; ", entries);
        }

        foreach (var map in values.ColorMaps)
        {
            if (!BrowserPreferenceSettings.ColorMapNames.Contains(map.Name))
            {
                continue;
            }

            foreach (var name in BrowserPreferenceSettings.RendererColorNames)
            {
                if (map.Colors.TryGetValue(name, out var color) && IsArgbColor(color))
                {
                    entries.Add($"color {map.Name} {name} {color[1..].ToUpperInvariant()}");
                }
            }
        }

        return string.Join("; ", entries);
    }

    /// <summary>
    /// Percent-encodes a text value: every UTF-8 byte other than an ASCII
    /// letter, a digit, "-", ".", "_", and "~" as "%" and two hexadecimal
    /// digits.
    /// </summary>
    public static string Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var encoded = new StringBuilder();
        foreach (var value in Encoding.UTF8.GetBytes(text))
        {
            var character = (char)value;
            if (character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_' or '~')
            {
                encoded.Append(character);
            }
            else
            {
                encoded.Append('%').Append(value.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return encoded.ToString();
    }

    private static string? Field(BrowserPreferenceSetting setting, JsonElement value) =>
        setting.Kind switch
        {
            BrowserPreferenceKind.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                $"field {setting.Name} b {(value.GetBoolean() ? "true" : "false")}",
            BrowserPreferenceKind.Integer when value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) =>
                $"field {setting.Name} i {number.ToString(CultureInfo.InvariantCulture)}",
            BrowserPreferenceKind.Number when value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var real) &&
                double.IsFinite(real) =>
                $"field {setting.Name} n {real.ToString("R", CultureInfo.InvariantCulture)}",
            BrowserPreferenceKind.Text when value.ValueKind == JsonValueKind.String =>
                $"field {setting.Name} t {Encode(value.GetString()!)}",
            _ => null
        };

    private static bool IsArgbColor(string text) =>
        text.Length == 9 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);

    /// <summary>The evidence panel's notes on the values the recreation gives the page.</summary>
    public static IReadOnlyList<string> Notes(BrowserPageValues? values)
    {
        if (values is null || (!values.PreferencesRecorded && values.Zoom is null && !values.ColorMapsRecorded))
        {
            return
            [
                "The page's preferences were not recorded, as in a recording made before protocol 0.56 or without the browser, so the recreation is given the recreation browser's own preferences and color maps, those of the viewing machine: its media queries, native controls, scroll bars, and system colors may differ from the participant's."
            ];
        }

        var notes = new List<string>();
        if (!values.PreferencesRecorded)
        {
            notes.Add("No preferences sent to a page were recorded, so the recreation is given the recreation browser's own, those of the viewing machine.");
        }
        else if (values.Fields.Count == 0)
        {
            notes.Add("No preferences were recorded as sent to this page by the frame, so the recreation is given the recreation browser's own, those of the viewing machine.");
        }
        else
        {
            var listed = string.Join(
                "; ",
                values.Fields.Select(field =>
                    $"{field.Setting.Label}: {BrowserPreferenceTimeline.DescribeValue(field.Setting, field.Value)}, from the record at {Seconds(field.Time)} s"));
            notes.Add($"The page is given the recorded values of the {values.Fields.Count.ToString(CultureInfo.InvariantCulture)} preferences last sent to it at or before the frame, in place of the recreation browser's: {listed}.");
            var missing = BrowserPreferenceSettings.Page
                .Where(setting => values.Fields.All(field => field.Setting.Name != setting.Name))
                .Select(setting => setting.Label)
                .ToList();
            if (missing.Count > 0)
            {
                notes.Add($"These preferences were not recorded as sent to the page, so the recreation browser's own are used: {string.Join(", ", missing)}.");
            }
        }

        if (values.Zoom is { } zoom && zoom.Source != "none")
        {
            var percent = (zoom.Factor * 100).ToString("0.#", CultureInfo.InvariantCulture);
            notes.Add($"The page's recorded zoom at the frame was {percent} percent, from the record at {Seconds(zoom.Time)} s. It is not applied as a zoom level: its effect on the page's layout is in the recorded layout zoom factor the page is laid out at, and the page reports the recreation browser's own zoom level.");
        }

        if (!values.ColorMapsRecorded)
        {
            notes.Add("The page's color maps were not recorded, as in a recording made before protocol 0.57, so the recreation is given the recreation browser's own light, dark, and forced colors maps, those of the viewing machine: system colors, native controls, and scroll bars, and a contrast theme's colors, may differ from the participant's.");
        }
        else
        {
            foreach (var name in BrowserPreferenceSettings.ColorMapNames)
            {
                var map = values.ColorMaps.FirstOrDefault(item => item.Name == name);
                notes.Add(map is null
                    ? $"No {MapLabel(name)} color map was recorded as sent to the page by the frame, so the recreation browser's own is used."
                    : $"The page is given the recorded {MapLabel(name)} color map, {map.Colors.Count.ToString(CultureInfo.InvariantCulture)} colors, from the record at {Seconds(map.Time)} s.");
            }
        }

        notes.Add($"The values are written in the root element's {AttributeName} attribute, which DevTools' Elements pane shows but which was not an attribute of the recorded page. The instrumented renderer reads it and applies the values in place of those the recreation browser sends, at the first application and at every later send.");
        notes.Add("Only the listed preferences are applied; anything else Chromium gives a page is the recreation browser's. A child frame the recreation browser shows in the page's renderer process, and the page's popups, which the recreation draws in the page, share the page's values; a child frame it puts in a renderer process of its own, such as a frame of another site, is given the recreation browser's own preferences. Windows effects on the screen, such as Magnifier and color filters, are not part of the page; the participant's view shows them.");
        return notes;
    }

    /// <summary>
    /// The evidence panel's notes on the browser theme the recreation
    /// browser's own window is given (protocol 0.59).
    /// </summary>
    public static IReadOnlyList<string> ThemeNotes(IReadOnlyList<BrowserThemeValue>? theme)
    {
        if (theme is null || theme.Count == 0)
        {
            return
            [
                "The participant's browser theme was not recorded, as in a recording made before protocol 0.56 or without the browser, so the recreation browser's window, its tabs and toolbar, shows its own default theme."
            ];
        }

        var notes = new List<string>();
        var listed = string.Join(
            "; ",
            theme.Select(value =>
                $"{value.Setting.Label}: {BrowserPreferenceTimeline.DescribeReading(value.Setting, value.Reading)}, from the record at {Seconds(value.Time)} s"));
        notes.Add($"The recreation browser's own window, its tabs and toolbar, is given the participant's browser theme at the frame, written into its new profile before it starts: {listed}. The theme is set as the recreation opens, so it is the frame's; the page's own colors are given separately, from its recorded color maps.");
        var missing = BrowserPreferenceSettings.ThemeNames
            .Where(name => theme.All(value => value.Setting.Name != name))
            .Select(name => BrowserPreferenceSettings.FindBrowser(name)!.Label)
            .ToList();
        if (missing.Count > 0)
        {
            notes.Add($"These theme settings were not recorded, as in a recording made before protocol 0.59, so the recreation browser's defaults are used: {string.Join(", ", missing)}.");
        }

        if (theme.FirstOrDefault(value => value.Setting.Name == "colorScheme")?.Value is { } scheme &&
            scheme.TryGetInt64(out var mode) && mode == 0)
        {
            notes.Add("The browser color mode was system, so the recreation browser's window follows the viewing machine's Windows light or dark mode, which may not be the participant's.");
        }

        if (theme.FirstOrDefault(value => value.Setting.Name == "themeId")?.Value is { ValueKind: JsonValueKind.String } id &&
            id.GetString() is { Length: > 0 } installed)
        {
            notes.Add($"The participant's browser had an installed theme, {installed}, which the recreation cannot install, as it has no network access, so its window shows the browser's default theme in place of it, with the recorded color mode.");
        }

        return notes;
    }

    private static string MapLabel(string name) => name switch
    {
        "forcedColors" => "forced colors",
        _ => name
    };

    private static string Seconds(long? nanoseconds) =>
        nanoseconds is { } value ? (value / 1e9).ToString("0.000", CultureInfo.InvariantCulture) : "an unknown time";
}

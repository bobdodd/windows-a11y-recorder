using System.Globalization;

namespace Recorder.Contracts;

/// <summary>A custom key command from NVDA's <c>gestures.ini</c>: its section, script, and gesture.</summary>
public sealed record NvdaCustomGesture(string Section, string Script, string Gesture);

/// <summary>
/// The NVDA settings the key commands depend on, as written in NVDA's
/// configuration folder: each value as the file writes it, or null where it
/// is not written and NVDA's default applies. Read only; nothing is changed.
/// See docs/architecture/screen-reader-activity.md, "2c-1, the commands".
/// </summary>
public sealed record NvdaSettingsReading(
    string ConfigFolder,
    bool Read,
    string? Problem,
    string? KeyboardLayout,
    string? NvdaModifierKeys,
    string? MultiPressTimeout,
    string? AutoPassThroughOnFocusChange,
    string? AutoPassThroughOnCaretMove,
    string? TrapNonCommandGestures,
    string? EnableOnPageLoad,
    IReadOnlyList<string> Profiles,
    bool ProfileTriggers,
    IReadOnlyList<NvdaCustomGesture> CustomGestures,
    string? GesturesProblem)
{
    // NVDA's [mouse] settings, as written or null where NVDA's default
    // applies. See docs/architecture/screen-reader-activity.md, "The mouse
    // hook".
    public string? EnableMouseTracking { get; init; }
    public string? IgnoreInjectedMouseInput { get; init; }
    public string? MouseTextUnit { get; init; }
    public string? ReportObjectRoleOnMouseEnter { get; init; }
    public string? AudioCoordinatesOnMouseMove { get; init; }
    public string? ReportMouseShapeChanges { get; init; }

    // The settings that decide what NVDA reports in a browse mode document,
    // by key (NvdaSettings.ReportingSettings), as written or null where
    // NVDA's default applies. See docs/architecture/screen-reader-activity.md,
    // "What NVDA had to announce".
    public IReadOnlyDictionary<string, string?> Reporting { get; init; } = new Dictionary<string, string?>();
}

/// <summary>
/// Reads NVDA's settings: <c>nvda.ini</c>, the names of the profiles in
/// <c>profiles</c>, whether <c>profileTriggers.ini</c> exists, and
/// <c>gestures.ini</c>. The files are in configobj's format: top-level
/// sections in square brackets, and <c>key = value</c> lines
/// (https://raw.githubusercontent.com/nvaccess/nvda/master/source/config/configSpec.py).
/// </summary>
public static class NvdaSettings
{
    public const string SettingsEventType = "assistive-technology-settings";

    /// <summary>
    /// The settings that decide what NVDA reports in a browse mode document,
    /// with NVDA's defaults, from NVDA 2026.2's configuration specification
    /// (https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/config/configSpec.py):
    /// what is reported of links, headings, lists, landmarks, frames, and the
    /// rest; the formatting reported, which the model does not cover and so
    /// notes when it is on; descriptions and shortcut keys; the line length
    /// and screen layout of browse mode; and annotations. Each is recorded as
    /// a member of the settings record named by its key.
    /// </summary>
    public static IReadOnlyList<(string Section, string Key, string Default)> ReportingSettings { get; } =
    [
        ("documentFormatting", "reportLinks", "true"),
        ("documentFormatting", "reportLinkType", "true"),
        ("documentFormatting", "reportGraphics", "true"),
        ("documentFormatting", "reportHeadings", "true"),
        ("documentFormatting", "reportLists", "true"),
        ("documentFormatting", "reportBlockQuotes", "true"),
        ("documentFormatting", "reportGroupings", "true"),
        ("documentFormatting", "reportLandmarks", "true"),
        ("documentFormatting", "reportArticles", "false"),
        ("documentFormatting", "reportFrames", "true"),
        ("documentFormatting", "reportFigures", "true"),
        ("documentFormatting", "reportClickable", "true"),
        ("documentFormatting", "reportTables", "true"),
        ("documentFormatting", "includeLayoutTables", "false"),
        ("documentFormatting", "detectFormatAfterCursor", "false"),
        ("documentFormatting", "reportFontName", "false"),
        ("documentFormatting", "reportFontSize", "false"),
        ("documentFormatting", "fontAttributeReporting", "0"),
        ("documentFormatting", "reportEmphasis", "false"),
        ("documentFormatting", "reportHighlight", "true"),
        ("documentFormatting", "reportSuperscriptsAndSubscripts", "false"),
        ("documentFormatting", "reportColor", "false"),
        ("documentFormatting", "reportAlignment", "false"),
        ("documentFormatting", "reportLineSpacing", "false"),
        ("documentFormatting", "reportStyle", "false"),
        ("documentFormatting", "reportLineNumber", "false"),
        ("documentFormatting", "reportLineIndentation", "0"),
        ("documentFormatting", "reportParagraphIndentation", "false"),
        ("presentation", "reportKeyboardShortcuts", "true"),
        ("presentation", "reportObjectDescriptions", "true"),
        ("virtualBuffers", "maxLineLength", "100"),
        ("virtualBuffers", "useScreenLayout", "true"),
        ("virtualBuffers", "autoSayAllOnPageLoad", "true"),
        ("annotations", "reportDetails", "true"),
        ("annotations", "reportAriaDescription", "true")
    ];

    /// <summary>The settings read, as section and key of nvda.ini.</summary>
    public static IReadOnlyList<(string Section, string Key)> Keys { get; } =
    [
        ("keyboard", "keyboardLayout"),
        ("keyboard", "NVDAModifierKeys"),
        ("keyboard", "multiPressTimeout"),
        ("virtualBuffers", "autoPassThroughOnFocusChange"),
        ("virtualBuffers", "autoPassThroughOnCaretMove"),
        ("virtualBuffers", "trapNonCommandGestures"),
        ("virtualBuffers", "enableOnPageLoad"),
        ("mouse", "enableMouseTracking"),
        ("mouse", "ignoreInjectedMouseInput"),
        ("mouse", "mouseTextUnit"),
        ("mouse", "reportObjectRoleOnMouseEnter"),
        ("mouse", "audioCoordinatesOnMouseMove"),
        ("mouse", "reportMouseShapeChanges"),
        .. ReportingSettings.Select(setting => (setting.Section, setting.Key))
    ];


    // The defaults of NVDA's configuration specification, for the player.
    public const string DefaultKeyboardLayout = "desktop";
    public const int DefaultNvdaModifierKeys = 6;
    public const int DefaultMultiPressTimeoutMilliseconds = 500;
    public const bool DefaultTrapNonCommandGestures = true;

    // The [mouse] defaults: "enableMouseTracking = boolean(default=True)",
    // "ignoreInjectedMouseInput = boolean(default=false)",
    // "mouseTextUnit = string(default="paragraph")"
    // (https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/config/configSpec.py).
    public const bool DefaultEnableMouseTracking = true;
    public const bool DefaultIgnoreInjectedMouseInput = false;
    public const string DefaultMouseTextUnit = "paragraph";
    public const bool DefaultReportObjectRoleOnMouseEnter = false;
    public const bool DefaultAudioCoordinatesOnMouseMove = false;
    public const bool DefaultReportMouseShapeChanges = false;

    /// <summary>
    /// A boolean setting as configobj reads it, with the default where it is
    /// not written; null where it is written as something configobj does not
    /// read as a boolean. configobj takes "on", "1", "true", and "yes" as
    /// true and "off", "0", "false", and "no" as false, in any case
    /// (https://raw.githubusercontent.com/DiffSK/configobj/master/src/configobj/validate.py,
    /// is_boolean).
    /// </summary>
    public static bool? Boolean(string? written, bool defaultValue) =>
        written is null
            ? defaultValue
            : written.Trim().ToLowerInvariant() switch
            {
                "true" or "on" or "yes" or "1" => true,
                "false" or "off" or "no" or "0" => false,
                _ => null
            };

    // NVDAModifierKeys: "1: CapsLock, 2: NumpadInsert, 4: ExtendedInsert".
    public const int CapsLockModifier = 1;
    public const int NumpadInsertModifier = 2;
    public const int ExtendedInsertModifier = 4;

    /// <summary>
    /// The configuration folder: <c>userConfig</c> in a portable copy's
    /// folder, or <c>nvda</c> in the user's application data for an installed
    /// copy (https://download.nvaccess.org/releases/2026.2/documentation/userGuide.html).
    /// Null where the copy is unknown.
    /// </summary>
    public static string? ConfigFolder(string executablePath, string copy, string applicationData) => copy switch
    {
        "portable" => Path.GetDirectoryName(executablePath) is { } folder ? Path.Combine(folder, "userConfig") : null,
        "installed" => Path.Combine(applicationData, "nvda"),
        _ => null
    };

    public static NvdaSettingsReading Read(string configFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFolder);
        var values = new Dictionary<(string, string), string>();
        string? problem = null;
        var read = false;
        try
        {
            var path = Path.Combine(configFolder, "nvda.ini");
            if (File.Exists(path))
            {
                foreach (var (section, key, value) in Entries(File.ReadAllLines(path)))
                {
                    if (Keys.Contains((section, key)))
                    {
                        values[(section, key)] = value;
                    }
                }

                read = true;
            }
            else
            {
                problem = "nvda.ini was not found; NVDA's defaults apply.";
                read = Directory.Exists(configFolder);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = "nvda.ini could not be read: " + exception.Message;
        }

        var profiles = new List<string>();
        var triggers = false;
        try
        {
            var folder = Path.Combine(configFolder, "profiles");
            if (Directory.Exists(folder))
            {
                profiles.AddRange(Directory.GetFiles(folder, "*.ini")
                    .Select(file => Path.GetFileNameWithoutExtension(file))
                    .Order(StringComparer.Ordinal));
            }

            triggers = File.Exists(Path.Combine(configFolder, "profileTriggers.ini"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = (problem is null ? string.Empty : problem + " ") + "The profiles could not be listed: " + exception.Message;
        }

        var gestures = new List<NvdaCustomGesture>();
        string? gesturesProblem = null;
        try
        {
            var path = Path.Combine(configFolder, "gestures.ini");
            if (File.Exists(path))
            {
                gestures.AddRange(Gestures(File.ReadAllLines(path)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            gesturesProblem = "gestures.ini could not be read: " + exception.Message;
        }

        string? Value(string section, string key) => values.GetValueOrDefault((section, key));
        return new NvdaSettingsReading(
            configFolder,
            read,
            problem,
            Value("keyboard", "keyboardLayout"),
            Value("keyboard", "NVDAModifierKeys"),
            Value("keyboard", "multiPressTimeout"),
            Value("virtualBuffers", "autoPassThroughOnFocusChange"),
            Value("virtualBuffers", "autoPassThroughOnCaretMove"),
            Value("virtualBuffers", "trapNonCommandGestures"),
            Value("virtualBuffers", "enableOnPageLoad"),
            profiles,
            triggers,
            gestures,
            gesturesProblem)
        {
            EnableMouseTracking = Value("mouse", "enableMouseTracking"),
            IgnoreInjectedMouseInput = Value("mouse", "ignoreInjectedMouseInput"),
            MouseTextUnit = Value("mouse", "mouseTextUnit"),
            ReportObjectRoleOnMouseEnter = Value("mouse", "reportObjectRoleOnMouseEnter"),
            AudioCoordinatesOnMouseMove = Value("mouse", "audioCoordinatesOnMouseMove"),
            ReportMouseShapeChanges = Value("mouse", "reportMouseShapeChanges"),
            Reporting = ReportingSettings.ToDictionary(setting => setting.Key, setting => Value(setting.Section, setting.Key))
        };
    }

    /// <summary>
    /// The key and value lines of a configobj file under its top-level
    /// sections; lines of nested sections (<c>[[...]]</c>) and comments are
    /// skipped, and quotes around a value removed.
    /// </summary>
    public static IEnumerable<(string Section, string Key, string Value)> Entries(IEnumerable<string> lines)
    {
        string? section = null;
        var nested = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("[[", StringComparison.Ordinal))
            {
                nested = true;
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                nested = false;
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (section is null || nested || equals <= 0)
            {
                continue;
            }

            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            yield return (section, line[..equals].Trim(), value);
        }
    }

    /// <summary>
    /// The custom key commands of gestures.ini: each script's gestures,
    /// comma separated, under its section; a script of <c>None</c> unbinds
    /// the gestures.
    /// </summary>
    public static IEnumerable<NvdaCustomGesture> Gestures(IEnumerable<string> lines)
    {
        foreach (var (section, script, value) in Entries(lines))
        {
            foreach (var gesture in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return new NvdaCustomGesture(section, script, gesture.Trim('"', '\''));
            }
        }
    }

    /// <summary>The NVDA modifier keys of a setting as written, with the default where it is not, or null where it is not a number from 1 to 7.</summary>
    public static int? ModifierKeys(string? written) =>
        written is null
            ? DefaultNvdaModifierKeys
            : int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value is >= 1 and <= 7
                ? value
                : null;
}

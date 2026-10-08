namespace Recorder.Contracts;

/// <summary>The JSON type of a Windows setting's value.</summary>
public enum WindowsPreferenceKind
{
    Boolean,
    Integer,
    Number,
    Text,

    /// <summary>
    /// A list of monitors, each with its device name, bounds, whether it is
    /// the primary monitor, and its effective DPI.
    /// </summary>
    Monitors
}

/// <summary>
/// A Windows setting the <c>system.preferences</c> channel records: its
/// payload name, the JSON type of its value, and how it is read. See
/// docs/architecture/accessibility-preferences.md, "Windows settings".
/// </summary>
public sealed record WindowsPreferenceSetting(string Name, WindowsPreferenceKind Kind, string ReadBy);

/// <summary>The Windows settings recorded, in the order the payload lists them.</summary>
public static class WindowsPreferenceSettings
{
    public const string Channel = "system.preferences";
    public const string SnapshotEventType = "windows-preferences";
    public const string ChangeEventType = "windows-preference-changed";

    /// <summary>The setting read at the start of a recording only.</summary>
    public const string CaretBlinkTime = "caretBlinkTime";

    /// <summary>The UISettings change events the collector subscribes to.</summary>
    public static IReadOnlyList<string> UiSettingsEvents { get; } =
    [
        "advancedEffectsEnabledChanged",
        "animationsEnabledChanged",
        "autoHideScrollBarsChanged",
        "textScaleFactorChanged",
        "colorValuesChanged"
    ];

    public static IReadOnlyList<WindowsPreferenceSetting> All { get; } =
    [
        // Display.
        new("monitors", WindowsPreferenceKind.Monitors, "get-dpi-for-monitor"),
        new("textScaleFactor", WindowsPreferenceKind.Number, "ui-settings"),
        new("appsUseLightTheme", WindowsPreferenceKind.Boolean, "registry"),
        new("transparencyEffects", WindowsPreferenceKind.Boolean, "ui-settings"),
        new("colorFilterActive", WindowsPreferenceKind.Boolean, "registry"),
        new("colorFilterType", WindowsPreferenceKind.Integer, "registry"),
        new("highContrast", WindowsPreferenceKind.Boolean, "system-parameters-info"),
        new("highContrastScheme", WindowsPreferenceKind.Text, "system-parameters-info"),
        new("accentColor", WindowsPreferenceKind.Text, "ui-settings"),
        // Motion.
        new("animationsEnabled", WindowsPreferenceKind.Boolean, "ui-settings"),
        new("uiEffects", WindowsPreferenceKind.Boolean, "system-parameters-info"),
        new("menuAnimation", WindowsPreferenceKind.Boolean, "system-parameters-info"),
        new("menuFade", WindowsPreferenceKind.Boolean, "system-parameters-info"),
        new("comboBoxAnimation", WindowsPreferenceKind.Boolean, "system-parameters-info"),
        // Pointer and focus.
        new("cursorWidth", WindowsPreferenceKind.Number, "ui-settings"),
        new("cursorHeight", WindowsPreferenceKind.Number, "ui-settings"),
        new("caretWidth", WindowsPreferenceKind.Integer, "system-parameters-info"),
        new(CaretBlinkTime, WindowsPreferenceKind.Integer, "get-caret-blink-time"),
        new("focusBorderWidth", WindowsPreferenceKind.Integer, "system-parameters-info"),
        new("focusBorderHeight", WindowsPreferenceKind.Integer, "system-parameters-info"),
        new("keyboardCues", WindowsPreferenceKind.Boolean, "system-parameters-info"),
        new("autoHideScrollBars", WindowsPreferenceKind.Boolean, "ui-settings"),
        new("messageDuration", WindowsPreferenceKind.Integer, "ui-settings"),
        // Keyboard and mouse assistance.
        new("stickyKeys", WindowsPreferenceKind.Boolean, "system-parameters-info"),
        new("filterKeys", WindowsPreferenceKind.Boolean, "system-parameters-info"),
        new("toggleKeys", WindowsPreferenceKind.Boolean, "system-parameters-info"),
        new("mouseKeys", WindowsPreferenceKind.Boolean, "system-parameters-info")
    ];

    private static readonly Dictionary<string, WindowsPreferenceSetting> ByNameMap =
        All.ToDictionary(setting => setting.Name, StringComparer.Ordinal);

    public static WindowsPreferenceSetting? Find(string name) => ByNameMap.GetValueOrDefault(name);
}

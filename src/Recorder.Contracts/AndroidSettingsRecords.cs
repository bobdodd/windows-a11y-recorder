namespace Recorder.Contracts;

/// <summary>
/// The records of the phone settings collector, slice 8, step 8a of
/// docs/architecture/android-implementation-plan.md: the phone's
/// accessibility-related settings when the recording starts and stops, and
/// each change between, as Android's <c>settings</c> command prints them.
///
/// The recorder reads the values over adb with <c>settings list</c> and
/// <c>settings get</c>, which run as the shell user. The companion app does
/// not read them: from Android 12, an app that targets it cannot read a
/// hidden setting that is not marked readable, and some of these, such as
/// <c>reduce_bright_colors_activated</c>, are not (Settings.java). The
/// companion app only says when one changed, from a content observer on
/// each setting's address, and the recorder then reads it. A value is
/// recorded as text, as the command prints it, and null when the phone
/// holds no value for it (the command prints "null").
/// </summary>
public static class AndroidSettingsRecords
{
    public const string Channel = "preferences.android";

    /// <summary>Every setting in <see cref="All"/>, read at the start or the stop.</summary>
    public const string SnapshotEventType = "android-settings";

    /// <summary>A setting whose value differs from the last one recorded.</summary>
    public const string ChangeEventType = "android-setting-changed";

    /// <summary>Why the settings, or their changes, are missing from part or all of a recording.</summary>
    public const string UnavailableEventType = "android-settings-unavailable";

    public const string StartReason = "start";

    public const string StopReason = "stop";

    /// <summary>A change the companion app said when it happened.</summary>
    public const string NoticeSource = "notice";

    /// <summary>A change found only by the read at the stop, with no notice of when it happened.</summary>
    public const string StopSource = "stop";

    /// <summary>The record's time is the phone time of the companion app's notice.</summary>
    public const string PhoneNoticeTimeFlag = "phone-notice-time";

    /// <summary>The record's time is the read at the stop: the change happened at some time before it.</summary>
    public const string FoundAtStopFlag = "found-at-stop";

    public const string SecureNamespace = "secure";

    public const string SystemNamespace = "system";

    public const string GlobalNamespace = "global";

    public static IReadOnlyList<string> Namespaces { get; } = [SecureNamespace, SystemNamespace, GlobalNamespace];

    public const string ServicesGroup = "Phone accessibility";
    public const string MagnificationGroup = "Phone magnification";
    public const string ColorGroup = "Phone color and contrast";
    public const string TextGroup = "Phone text and display";
    public const string MotionGroup = "Phone motion";
    public const string TimingGroup = "Phone interaction and timing";
    public const string AudioGroup = "Phone audio and captions";
    public const string SpeechGroup = "Phone text-to-speech";

    /// <summary>
    /// The settings recorded, in the order the properties panel shows them.
    /// Each name is the one in AOSP's
    /// frameworks/base/core/java/android/provider/Settings.java (main
    /// branch, read on 2026-11-11), and each meaning is that file's comment
    /// for it, or the class its constants are in, as named in
    /// <see cref="AndroidSettingKind"/>. A name a phone does not hold is
    /// recorded as null and shown as not set.
    /// </summary>
    public static IReadOnlyList<AndroidSetting> All { get; } =
    [
        new(SecureNamespace, "accessibility_enabled", ServicesGroup, "Accessibility in use", AndroidSettingKind.Switch),
        new(SecureNamespace, "enabled_accessibility_services", ServicesGroup, "Services on", AndroidSettingKind.Components),
        new(SecureNamespace, "touch_exploration_enabled", ServicesGroup, "Explore by touch", AndroidSettingKind.Switch),
        new(SecureNamespace, "accessibility_button_targets", ServicesGroup, "Accessibility button", AndroidSettingKind.Components),
        new(SecureNamespace, "accessibility_shortcut_target_service", ServicesGroup, "Accessibility shortcut", AndroidSettingKind.Components),
        new(SecureNamespace, "accessibility_qs_targets", ServicesGroup, "Quick Settings tiles", AndroidSettingKind.Components),

        new(SecureNamespace, "accessibility_display_magnification_enabled", MagnificationGroup, "Triple tap to magnify", AndroidSettingKind.Switch),
        new(SecureNamespace, "accessibility_magnification_mode", MagnificationGroup, "Magnification area", AndroidSettingKind.MagnificationMode),
        new(SecureNamespace, "accessibility_display_magnification_scale", MagnificationGroup, "Magnification scale", AndroidSettingKind.Times),
        new(SecureNamespace, "accessibility_magnification_follow_typing_enabled", MagnificationGroup, "Follow typing", AndroidSettingKind.Switch),

        new(SecureNamespace, "accessibility_display_inversion_enabled", ColorGroup, "Color inversion", AndroidSettingKind.Switch),
        new(SecureNamespace, "accessibility_display_daltonizer_enabled", ColorGroup, "Color correction", AndroidSettingKind.Switch),
        new(SecureNamespace, "accessibility_display_daltonizer", ColorGroup, "Color correction mode", AndroidSettingKind.Daltonizer),
        new(SecureNamespace, "high_text_contrast_enabled", ColorGroup, "High contrast text", AndroidSettingKind.Switch),
        new(SecureNamespace, "reduce_bright_colors_activated", ColorGroup, "Extra dim", AndroidSettingKind.Switch),
        new(SecureNamespace, "reduce_bright_colors_level", ColorGroup, "Extra dim intensity", AndroidSettingKind.Percent),
        new(SecureNamespace, "ui_night_mode", ColorGroup, "Dark theme", AndroidSettingKind.NightMode),

        new(SystemNamespace, "font_scale", TextGroup, "Font size", AndroidSettingKind.ScalePercent),
        new(SecureNamespace, "font_weight_adjustment", TextGroup, "Bold text", AndroidSettingKind.FontWeight),
        new(SecureNamespace, "display_density_forced", TextGroup, "Display size", AndroidSettingKind.Density),

        new(GlobalNamespace, "window_animation_scale", MotionGroup, "Window animation scale", AndroidSettingKind.Times),
        new(GlobalNamespace, "transition_animation_scale", MotionGroup, "Transition animation scale", AndroidSettingKind.Times),
        new(GlobalNamespace, "animator_duration_scale", MotionGroup, "Animator duration scale", AndroidSettingKind.Times),

        new(SecureNamespace, "accessibility_interactive_ui_timeout_ms", TimingGroup, "Timeout for controls", AndroidSettingKind.Milliseconds),
        new(SecureNamespace, "accessibility_non_interactive_ui_timeout_ms", TimingGroup, "Timeout for messages", AndroidSettingKind.Milliseconds),
        new(SecureNamespace, "long_press_timeout", TimingGroup, "Touch and hold delay", AndroidSettingKind.Milliseconds),
        new(SecureNamespace, "accessibility_autoclick_enabled", TimingGroup, "Autoclick", AndroidSettingKind.Switch),
        new(SecureNamespace, "accessibility_large_pointer_icon", TimingGroup, "Large mouse pointer", AndroidSettingKind.Switch),

        new(SecureNamespace, "accessibility_captioning_enabled", AudioGroup, "Captions", AndroidSettingKind.Switch),
        new(SystemNamespace, "master_mono", AudioGroup, "Mono audio", AndroidSettingKind.Switch),
        new(SystemNamespace, "master_balance", AudioGroup, "Audio balance", AndroidSettingKind.Balance),

        new(SecureNamespace, "tts_default_synth", SpeechGroup, "Preferred engine", AndroidSettingKind.Text),
        new(SecureNamespace, "tts_default_rate", SpeechGroup, "Speech rate", AndroidSettingKind.Hundredths),
        new(SecureNamespace, "tts_default_pitch", SpeechGroup, "Pitch", AndroidSettingKind.Hundredths)
    ];

    private static readonly Dictionary<string, AndroidSetting> ById =
        All.ToDictionary(setting => setting.Id, StringComparer.Ordinal);

    /// <summary>A recorded setting by its id, namespace/name, or null.</summary>
    public static AndroidSetting? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>A recorded setting by its namespace and name, or null.</summary>
    public static AndroidSetting? Find(string settingNamespace, string name) =>
        ById.GetValueOrDefault(settingNamespace + "/" + name);
}

/// <summary>How a setting's value is shown.</summary>
public enum AndroidSettingKind
{
    /// <summary>1 on, 0 off.</summary>
    Switch,

    /// <summary>A colon-separated list of component and class names, shown by their short names.</summary>
    Components,

    /// <summary>A number of times, as a magnification or animation scale.</summary>
    Times,

    /// <summary>A factor shown as a percent, as the font scale 1.15 is 115 percent.</summary>
    ScalePercent,

    /// <summary>A percent from 0 to 100.</summary>
    Percent,

    /// <summary>A whole number of milliseconds.</summary>
    Milliseconds,

    /// <summary>
    /// Settings.Secure.ACCESSIBILITY_MAGNIFICATION_MODE_*: 1 full screen,
    /// 2 part of the screen (a window), 3 either, switched between.
    /// </summary>
    MagnificationMode,

    /// <summary>
    /// The comment on ACCESSIBILITY_DISPLAY_DALTONIZER: -1 off, 0
    /// grayscale, 11 protanomaly, 12 deuteranomaly, 13 tritanomaly.
    /// </summary>
    Daltonizer,

    /// <summary>UiModeManager.MODE_NIGHT_*: 0 automatic, 1 off, 2 on, 3 custom.</summary>
    NightMode,

    /// <summary>The adjustment to font weight; 300 is bold, the difference between normal and bold.</summary>
    FontWeight,

    /// <summary>Dots per inch; not set is the phone's own density.</summary>
    Density,

    /// <summary>From -1, all left, through 0, centered, to 1, all right.</summary>
    Balance,

    /// <summary>A rate or pitch where 100 is normal.</summary>
    Hundredths,

    /// <summary>Text as it is.</summary>
    Text
}

/// <summary>A phone setting the recorder records, and how the properties panel shows it.</summary>
/// <param name="Namespace">secure, system or global, the table Android keeps it in.</param>
/// <param name="Name">Its name in that table.</param>
public sealed record AndroidSetting(
    string Namespace,
    string Name,
    string Group,
    string Label,
    AndroidSettingKind Kind)
{
    /// <summary>namespace/name, as the records name it.</summary>
    public string Id => Namespace + "/" + Name;
}

/// <summary>
/// Every recorded setting at the start or the stop, by id
/// (namespace/name), each null when the phone holds no value for it.
/// </summary>
/// <param name="Reason"><see cref="AndroidSettingsRecords.StartReason"/> or <see cref="AndroidSettingsRecords.StopReason"/>.</param>
/// <param name="Problems">Why a table could not be read, one sentence each; its settings are then missing from <paramref name="Settings"/>.</param>
/// <param name="ReadStartedAtNanoseconds">The session time the first read started.</param>
/// <param name="ReadEndedAtNanoseconds">The session time the last read ended.</param>
public sealed record AndroidSettingsSnapshot(
    string Serial,
    string Reason,
    IReadOnlyDictionary<string, string?> Settings,
    IReadOnlyList<string> Problems,
    long ReadStartedAtNanoseconds,
    long ReadEndedAtNanoseconds);

/// <summary>A setting whose value differs from the last one recorded.</summary>
/// <param name="Setting">Its id, namespace/name.</param>
/// <param name="Value">The value read, or null when the phone holds none.</param>
/// <param name="Previous">The last value recorded.</param>
/// <param name="Source"><see cref="AndroidSettingsRecords.NoticeSource"/> or <see cref="AndroidSettingsRecords.StopSource"/>.</param>
/// <param name="NoticePhoneNanoseconds">The phone's CLOCK_MONOTONIC when the companion app was told of the change; null for one found at the stop.</param>
/// <param name="NoticeReceivedAtNanoseconds">The session time the recorder read the notice; null for one found at the stop.</param>
/// <param name="ReadAtNanoseconds">The session time the value was read.</param>
public sealed record AndroidSettingChanged(
    string Serial,
    string Setting,
    string? Value,
    string? Previous,
    string Source,
    long? NoticePhoneNanoseconds,
    long? NoticeReceivedAtNanoseconds,
    long ReadAtNanoseconds);

/// <summary>Why the settings, or their changes, are missing from part or all of a recording.</summary>
/// <param name="ConnectionLost">True when the companion app's notices ended during the recording.</param>
public sealed record AndroidSettingsUnavailable(string Serial, string Reason, bool ConnectionLost);

/// <summary>A phone setting's value as the properties panel and the status line show it.</summary>
public static class AndroidSettingValues
{
    /// <summary>
    /// The value in words, with the number Android keeps after a named
    /// choice, such as "full screen (1)". A value that cannot be read as its
    /// kind is shown as it is.
    /// </summary>
    public static string Describe(AndroidSetting setting, string? value)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (value is null)
        {
            return setting.Kind == AndroidSettingKind.Density ? "the phone's own size (not set)" : "not set";
        }

        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        if (value.Length == 0)
        {
            return setting.Kind is AndroidSettingKind.Components or AndroidSettingKind.Text ? "none" : "empty";
        }

        var isNumber = double.TryParse(value, System.Globalization.NumberStyles.Float, invariant, out var number);
        var isWhole = long.TryParse(value, System.Globalization.NumberStyles.Integer, invariant, out var whole);
        string Named(string? name) => name is null ? value : $"{name} ({value})";
        return setting.Kind switch
        {
            AndroidSettingKind.Switch => value switch { "1" => "on", "0" => "off", _ => value },
            AndroidSettingKind.Components => Components(value),
            AndroidSettingKind.Times when isNumber => number == 0
                ? "off (0 times)"
                : $"{number.ToString("0.##", culture)} times",
            AndroidSettingKind.ScalePercent when isNumber => $"{Math.Round(number * 100).ToString(culture)} percent",
            AndroidSettingKind.Percent when isNumber => $"{number.ToString("0.##", culture)} percent",
            AndroidSettingKind.Milliseconds when isWhole => $"{whole.ToString(culture)} ms",
            AndroidSettingKind.MagnificationMode when isWhole => Named(whole switch
            {
                0 => "none",
                1 => "full screen",
                2 => "part of the screen",
                3 => "full or part of the screen, switched between",
                _ => null
            }),
            AndroidSettingKind.Daltonizer when isWhole => Named(whole switch
            {
                -1 => "off",
                0 => "grayscale",
                11 => "red-green, protanomaly",
                12 => "red-green, deuteranomaly",
                13 => "blue-yellow, tritanomaly",
                _ => null
            }),
            AndroidSettingKind.NightMode when isWhole => Named(whole switch
            {
                0 => "automatic",
                1 => "off",
                2 => "on",
                3 => "on a custom schedule",
                _ => null
            }),
            AndroidSettingKind.FontWeight when isWhole => whole switch
            {
                0 => "normal (0)",
                300 => "bold (300)",
                > 0 => $"heavier by {whole.ToString(culture)}",
                _ => $"lighter by {(-whole).ToString(culture)}"
            },
            AndroidSettingKind.Density when isWhole => $"{whole.ToString(culture)} dpi",
            AndroidSettingKind.Balance when isNumber => number switch
            {
                0 => "centered",
                < 0 => $"{Math.Round(-number * 100).ToString(culture)} percent left",
                _ => $"{Math.Round(number * 100).ToString(culture)} percent right"
            },
            AndroidSettingKind.Hundredths when isWhole =>
                $"{whole.ToString(culture)} ({(whole / 100.0).ToString("0.##", culture)} times normal)",
            _ => value
        };
    }

    /// <summary>
    /// A colon-separated list of components, as package/class, and of
    /// feature class names, by their class names: TalkBack's
    /// com.google.android.marvin.talkback/com.google.android.marvin.talkback.TalkBackService
    /// is TalkBackService.
    /// </summary>
    public static string Components(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var names = value
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry =>
            {
                var slash = entry.IndexOf('/', StringComparison.Ordinal);
                var className = slash >= 0 ? entry[(slash + 1)..] : entry;
                var dot = className.LastIndexOf('.');
                return dot >= 0 && dot < className.Length - 1 ? className[(dot + 1)..] : className;
            })
            .ToArray();
        return names.Length == 0 ? "none" : string.Join(", ", names);
    }
}

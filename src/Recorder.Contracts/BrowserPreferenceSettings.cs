namespace Recorder.Contracts;

/// <summary>The JSON type of a browser preference or page field's value.</summary>
public enum BrowserPreferenceKind
{
    Boolean,
    Integer,
    Number,
    Text,

    /// <summary>A list of text values.</summary>
    TextList
}

/// <summary>
/// A value the <c>browser.preferences</c> channel records: its name in the
/// payload, the JSON type of its value, the Chromium name it is read from,
/// and the label the player shows. See
/// docs/architecture/accessibility-preferences.md, "Stage 2".
/// </summary>
public sealed record BrowserPreferenceSetting(
    string Name,
    BrowserPreferenceKind Kind,
    string Source,
    string Label);

/// <summary>
/// The records of protocol 0.56 (accessibility preferences, stage 2): the
/// listed browser preferences of a profile, their changes, the preferences
/// each page's view is sent, and each zoom level change.
/// </summary>
public static class BrowserPreferenceSettings
{
    public const string Channel = "browser.preferences";
    public const string SnapshotEventType = "browser-preferences";
    public const string ChangeEventType = "browser-preference-changed";
    public const string SentEventType = "web-preferences-sent";
    public const string ZoomEventType = "zoom-level-changed";

    /// <summary>
    /// The color maps a page's view is sent (protocol 0.57, accessibility
    /// preferences stage 3). See docs/architecture/accessibility-preferences.md,
    /// "Stage 3".
    /// </summary>
    public const string ColorMapsEventType = "color-maps-sent";

    /// <summary>The points at which a page's view is sent color maps.</summary>
    public static IReadOnlyList<string> ColorMapSendPoints { get; } =
        ["view-created", "color-providers"];

    /// <summary>The color maps Chromium gives each page, by their record names.</summary>
    public static IReadOnlyList<string> ColorMapNames { get; } =
        ["light", "dark", "forcedColors"];

    /// <summary>
    /// The colors of each map, by their RendererColorId names
    /// (ui/color/color_id.mojom), in the enumeration's order.
    /// </summary>
    public static IReadOnlyList<string> RendererColorNames { get; } =
    [
        "kColorCssSystemActiveText", "kColorCssSystemBtnFace", "kColorCssSystemBtnText",
        "kColorCssSystemField", "kColorCssSystemFieldText", "kColorCssSystemGrayText",
        "kColorCssSystemHighlight", "kColorCssSystemHighlightText", "kColorCssSystemHotlight",
        "kColorCssSystemLinkText", "kColorCssSystemMenuHilight", "kColorCssSystemScrollbar",
        "kColorCssSystemVisitedText", "kColorCssSystemWindow", "kColorCssSystemWindowText",
        "kColorMenuBackground", "kColorMenuItemBackgroundSelected", "kColorMenuSeparator",
        "kColorOverlayScrollbarFill", "kColorOverlayScrollbarFillHovered",
        "kColorOverlayScrollbarStroke", "kColorOverlayScrollbarStrokeHovered",
        "kColorWebNativeControlAccent", "kColorWebNativeControlAccentDisabled",
        "kColorWebNativeControlAccentHovered", "kColorWebNativeControlAccentPressed",
        "kColorWebNativeControlAutoCompleteBackground", "kColorWebNativeControlBorder",
        "kColorWebNativeControlBorderDisabled", "kColorWebNativeControlBorderHovered",
        "kColorWebNativeControlBorderPressed", "kColorWebNativeControlButtonBorder",
        "kColorWebNativeControlButtonBorderDisabled", "kColorWebNativeControlButtonBorderHovered",
        "kColorWebNativeControlButtonBorderPressed", "kColorWebNativeControlButtonFill",
        "kColorWebNativeControlButtonFillDisabled", "kColorWebNativeControlButtonFillHovered",
        "kColorWebNativeControlButtonFillPressed", "kColorWebNativeControlCheckboxBackground",
        "kColorWebNativeControlCheckboxBackgroundDisabled", "kColorWebNativeControlFill",
        "kColorWebNativeControlFillDisabled", "kColorWebNativeControlFillHovered",
        "kColorWebNativeControlFillPressed", "kColorWebNativeControlLightenLayer",
        "kColorWebNativeControlProgressValue",
        "kColorWebNativeControlScrollbarArrowBackgroundDisabled",
        "kColorWebNativeControlScrollbarArrowBackgroundHovered",
        "kColorWebNativeControlScrollbarArrowBackgroundPressed",
        "kColorWebNativeControlScrollbarArrowForeground",
        "kColorWebNativeControlScrollbarArrowForegroundDisabled",
        "kColorWebNativeControlScrollbarArrowForegroundHovered",
        "kColorWebNativeControlScrollbarArrowForegroundPressed",
        "kColorWebNativeControlScrollbarCorner", "kColorWebNativeControlScrollbarThumb",
        "kColorWebNativeControlScrollbarThumbHovered",
        "kColorWebNativeControlScrollbarThumbOverlayMinimalMode",
        "kColorWebNativeControlScrollbarThumbPressed", "kColorWebNativeControlScrollbarTrack",
        "kColorWebNativeControlSlider", "kColorWebNativeControlSliderBorder",
        "kColorWebNativeControlSliderBorderHovered", "kColorWebNativeControlSliderBorderPressed",
        "kColorWebNativeControlSliderDisabled", "kColorWebNativeControlSliderHovered",
        "kColorWebNativeControlSliderPressed"
    ];

    /// <summary>The points at which a page's view is sent preferences.</summary>
    public static IReadOnlyList<string> SendPoints { get; } =
        ["view-created", "web-preferences", "renderer-preferences"];

    /// <summary>The scopes of a zoom level change, as HostZoomMap names them.</summary>
    public static IReadOnlyList<string> ZoomModes { get; } =
        ["host", "scheme-and-host", "temporary", "default"];

    /// <summary>
    /// The browser preferences of a profile, read from its preference store.
    /// The source is the Chromium preference name.
    /// </summary>
    public static IReadOnlyList<BrowserPreferenceSetting> Browser { get; } =
    [
        new("standardFontFamily", BrowserPreferenceKind.Text, "webkit.webprefs.fonts.standard.Zyyy", "Standard font"),
        new("fixedFontFamily", BrowserPreferenceKind.Text, "webkit.webprefs.fonts.fixed.Zyyy", "Fixed-width font"),
        new("serifFontFamily", BrowserPreferenceKind.Text, "webkit.webprefs.fonts.serif.Zyyy", "Serif font"),
        new("sansSerifFontFamily", BrowserPreferenceKind.Text, "webkit.webprefs.fonts.sansserif.Zyyy", "Sans-serif font"),
        new("cursiveFontFamily", BrowserPreferenceKind.Text, "webkit.webprefs.fonts.cursive.Zyyy", "Cursive font"),
        new("fantasyFontFamily", BrowserPreferenceKind.Text, "webkit.webprefs.fonts.fantasy.Zyyy", "Fantasy font"),
        new("mathFontFamily", BrowserPreferenceKind.Text, "webkit.webprefs.fonts.math.Zyyy", "Math font"),
        new("defaultFontSize", BrowserPreferenceKind.Integer, "webkit.webprefs.default_font_size", "Font size"),
        new("defaultFixedFontSize", BrowserPreferenceKind.Integer, "webkit.webprefs.default_fixed_font_size", "Fixed-width font size"),
        new("minimumFontSize", BrowserPreferenceKind.Integer, "webkit.webprefs.minimum_font_size", "Minimum font size"),
        new("minimumLogicalFontSize", BrowserPreferenceKind.Integer, "webkit.webprefs.minimum_logical_font_size", "Minimum logical font size"),
        new("colorScheme", BrowserPreferenceKind.Integer, "browser.theme.color_scheme2", "Browser color mode"),
        new("focusHighlight", BrowserPreferenceKind.Boolean, "settings.a11y.focus_highlight", "Focus highlight"),
        new("requestedPageColors", BrowserPreferenceKind.Integer, "settings.a11y.requested_page_colors", "Page colors"),
        new("pageColorsOnlyOnIncreasedContrast", BrowserPreferenceKind.Boolean, "settings.a11y.apply_page_colors_only_on_increased_contrast", "Page colors only with increased contrast"),
        new("pageColorsBlockList", BrowserPreferenceKind.TextList, "settings.a11y.page_colors_block_list", "Sites without page colors"),
        new("caretBrowsing", BrowserPreferenceKind.Boolean, "settings.a11y.caretbrowsing.enabled", "Caret browsing")
    ];

    /// <summary>
    /// The fields of the preferences a page's view is sent. The source is the
    /// WebPreferences or RendererPreferences member. A font family is the one
    /// for the common script, Zyyy, or empty when none is set.
    /// </summary>
    public static IReadOnlyList<BrowserPreferenceSetting> Page { get; } =
    [
        new("standardFontFamily", BrowserPreferenceKind.Text, "WebPreferences.standard_font_family_map", "Standard font"),
        new("fixedFontFamily", BrowserPreferenceKind.Text, "WebPreferences.fixed_font_family_map", "Fixed-width font"),
        new("serifFontFamily", BrowserPreferenceKind.Text, "WebPreferences.serif_font_family_map", "Serif font"),
        new("sansSerifFontFamily", BrowserPreferenceKind.Text, "WebPreferences.sans_serif_font_family_map", "Sans-serif font"),
        new("cursiveFontFamily", BrowserPreferenceKind.Text, "WebPreferences.cursive_font_family_map", "Cursive font"),
        new("fantasyFontFamily", BrowserPreferenceKind.Text, "WebPreferences.fantasy_font_family_map", "Fantasy font"),
        new("mathFontFamily", BrowserPreferenceKind.Text, "WebPreferences.math_font_family_map", "Math font"),
        new("defaultFontSize", BrowserPreferenceKind.Integer, "WebPreferences.default_font_size", "Font size"),
        new("defaultFixedFontSize", BrowserPreferenceKind.Integer, "WebPreferences.default_fixed_font_size", "Fixed-width font size"),
        new("minimumFontSize", BrowserPreferenceKind.Integer, "WebPreferences.minimum_font_size", "Minimum font size"),
        new("minimumLogicalFontSize", BrowserPreferenceKind.Integer, "WebPreferences.minimum_logical_font_size", "Minimum logical font size"),
        new("prefersReducedMotion", BrowserPreferenceKind.Boolean, "WebPreferences.prefers_reduced_motion", "prefers-reduced-motion: reduce"),
        new("prefersReducedTransparency", BrowserPreferenceKind.Boolean, "WebPreferences.prefers_reduced_transparency", "prefers-reduced-transparency: reduce"),
        new("invertedColors", BrowserPreferenceKind.Boolean, "WebPreferences.inverted_colors", "inverted-colors: inverted"),
        new("textTrackTextSize", BrowserPreferenceKind.Text, "WebPreferences.text_track_text_size", "Caption text size"),
        new("textTrackFontFamily", BrowserPreferenceKind.Text, "WebPreferences.text_track_font_family", "Caption font"),
        new("inForcedColors", BrowserPreferenceKind.Boolean, "WebPreferences.in_forced_colors", "forced-colors: active"),
        new("isForcedColorsDisabled", BrowserPreferenceKind.Boolean, "WebPreferences.is_forced_colors_disabled", "Forced colors disabled for the page"),
        new("preferredRootScrollbarColorScheme", BrowserPreferenceKind.Text, "WebPreferences.preferred_root_scrollbar_color_scheme", "Scrollbar color scheme"),
        new("preferredColorScheme", BrowserPreferenceKind.Text, "WebPreferences.preferred_color_scheme", "prefers-color-scheme"),
        new("preferredContrast", BrowserPreferenceKind.Text, "WebPreferences.preferred_contrast", "prefers-contrast"),
        new("focusRingColor", BrowserPreferenceKind.Text, "RendererPreferences.focus_ring_color", "Focus ring color"),
        new("hasCaretBlinkInterval", BrowserPreferenceKind.Boolean, "RendererPreferences.caret_blink_interval", "Caret blink interval set"),
        new("caretBlinkIntervalMilliseconds", BrowserPreferenceKind.Number, "RendererPreferences.caret_blink_interval", "Caret blink interval"),
        new("caretBrowsingEnabled", BrowserPreferenceKind.Boolean, "RendererPreferences.caret_browsing_enabled", "Caret browsing"),
        new("useOverlayScrollbar", BrowserPreferenceKind.Boolean, "RendererPreferences.use_overlay_scrollbar", "Overlay scrollbars"),
        new("captionFontFamily", BrowserPreferenceKind.Text, "RendererPreferences.caption_font_family_name", "Caption bar font"),
        new("captionFontHeight", BrowserPreferenceKind.Integer, "RendererPreferences.caption_font_height", "Caption bar font height"),
        new("smallCaptionFontFamily", BrowserPreferenceKind.Text, "RendererPreferences.small_caption_font_family_name", "Small caption font"),
        new("smallCaptionFontHeight", BrowserPreferenceKind.Integer, "RendererPreferences.small_caption_font_height", "Small caption font height"),
        new("menuFontFamily", BrowserPreferenceKind.Text, "RendererPreferences.menu_font_family_name", "Menu font"),
        new("menuFontHeight", BrowserPreferenceKind.Integer, "RendererPreferences.menu_font_height", "Menu font height"),
        new("statusFontFamily", BrowserPreferenceKind.Text, "RendererPreferences.status_font_family_name", "Status font"),
        new("statusFontHeight", BrowserPreferenceKind.Integer, "RendererPreferences.status_font_height", "Status font height"),
        new("messageFontFamily", BrowserPreferenceKind.Text, "RendererPreferences.message_font_family_name", "Message font"),
        new("messageFontHeight", BrowserPreferenceKind.Integer, "RendererPreferences.message_font_height", "Message font height")
    ];

    private static readonly Dictionary<string, BrowserPreferenceSetting> BrowserByName =
        Browser.ToDictionary(setting => setting.Name, StringComparer.Ordinal);

    private static readonly Dictionary<string, BrowserPreferenceSetting> PageByName =
        Page.ToDictionary(setting => setting.Name, StringComparer.Ordinal);

    public static BrowserPreferenceSetting? FindBrowser(string name) =>
        BrowserByName.GetValueOrDefault(name);

    public static BrowserPreferenceSetting? FindPage(string name) =>
        PageByName.GetValueOrDefault(name);
}

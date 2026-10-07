# Recording Accessibility Preferences

## Status

Proposed 2026-10-07, agreed 2026-10-07 with the decisions under
"Decisions", not built. It accompanies
[assistive technology detection](assistive-technology-detection.md), whose
approach was agreed on 2026-10-07, and takes over that design's Windows
settings, which are recorded here.

## Purpose

A recording states the accessibility-related preferences of Windows and of
the browser in effect when it starts, and each change during it, such as a
participant changing a font size, setting dark mode, or turning off
animation. The analysis needs them to say what the participant saw, why a
page looked or moved as it did, and when a participant changed a setting
to cope with a page.

Preferences are recorded at two levels, kept apart:

- The setting: the value of a Windows setting or a browser preference, as
  the participant set it.
- The effect on the page: the values the browser sends to the page's
  renderer, from which it evaluates media features such as
  `prefers-color-scheme`, chooses fonts and sizes, and decides on forced
  colors. A setting the browser does not follow has no effect, and an
  effect may come from more than one setting, so the two levels are both
  recorded.

The page-recreation design already requires the recorded "media features"
for the recreation and notes, under "The environment", that "Media features
are not recorded and take the browser's values"
([page recreation](page-recreation.md)). The browser level here records
them; applying them to the recreation is a separate change to that design.

## What is recorded today

- `popup-widget-shown` holds `windowsAnimationSettings`, five animation
  settings read when a native popup is shown (protocol 0.47,
  [page recreation](page-recreation.md)). They are not recorded at the
  start or on change.
- Nothing else. No Windows setting is recorded at the start, no change is
  recorded, and no browser preference is recorded.
- By default each recording starts the instrumented Chromium with a new
  profile, `%LOCALAPPDATA%\Windows A11y Recorder\BrowserProfiles\<session
  id>`, unless a profile directory is given
  (`src/Recorder.Collectors.Browser/BrowserEvidenceReceiver.cs`, lines 128
  to 139). The browser's own preferences therefore start at Chromium's
  defaults, not at the participant's, unless a profile is given or the
  participant changes them during the recording. A recording states which
  profile was used and whether it was new.

## Windows settings

Each setting is read at the start and stop of a recording and on each
notice of a change; a value that differs from the last recorded one is
written as a change. Where Chromium reads a setting, the checkout file and
lines are given (Chromium 156.0.8065.0 on the target machine).

| Setting | Read from | Notice of change | Basis |
|---|---|---|---|
| App dark mode | `AppsUseLightTheme` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` | registry notification | Chromium sets dark mode from it, `ui/native_theme/os_settings_provider_win.cc`, lines 205 to 210 |
| Transparency effects | `EnableTransparency` under the same key; `UISettings.AdvancedEffectsEnabled` | registry notification; `AdvancedEffectsEnabledChanged` | Chromium sets reduced transparency from it, lines 212 to 214; [UISettings](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings) |
| Color filters | `Active` and `FilterType` under `HKCU\Software\Microsoft\ColorFiltering` | registry notification | Chromium, lines 217 to 231, whose comment gives the filter types 0 to 5: greyscale, invert, greyscale inverted, deuteranopia, protanopia, tritanopia |
| Contrast themes | `SPI_GETHIGHCONTRAST`, flag `HCF_HIGHCONTRASTON`, and the theme name | `WM_SETTINGCHANGE` | Chromium sets forced colors from it, lines 30 to 36 |
| Animation effects | `SPI_GETCLIENTAREAANIMATION`; `UISettings.AnimationsEnabled` | `WM_SETTINGCHANGE`; `AnimationsEnabledChanged` | Chromium sets reduced motion from it, `ui/gfx/animation/animation_win.cc`, lines 38 to 48 |
| Other animation settings | `SPI_GETUIEFFECTS`, `SPI_GETMENUANIMATION`, `SPI_GETMENUFADE`, `SPI_GETCOMBOBOXANIMATION` | `WM_SETTINGCHANGE` | as recorded today with popups |
| Always show scrollbars | `DynamicScrollbars` under `HKCU\Control Panel\Accessibility`; `UISettings.AutoHideScrollBars` | registry notification; `AutoHideScrollBarsChanged` | Chromium sets overlay scrollbars from it, lines 254 to 260 |
| Text size | `UISettings.TextScaleFactor` | `TextScaleFactorChanged` | Chromium applies it through `UwpTextScaleFactor`, `ui/display/win/screen_win.cc`, line 87 |
| Display scale | each monitor's DPI | `WM_DPICHANGED`, sent "when the effective dots per inch (dpi) for a window has changed" ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged)) | |
| Caret blink rate | `GetCaretBlinkTime` | none | Chromium's comment, lines 147 to 155, notes Windows has no way to monitor its changes; it is read at the start of a recording only (see "Decisions") |
| Caret width, focus border width and height, keyboard cues, cursor size, message duration | `SystemParametersInfo`; `UISettings.CursorSize` and `MessageDuration` | `WM_SETTINGCHANGE` | registered as Windows settings in `ATs` (see [assistive technology detection](assistive-technology-detection.md)) |
| Sticky, filter, toggle, and mouse keys | `SystemParametersInfo` | `WM_SETTINGCHANGE` | as above |
| Accent color | `UISettings` | its change event | Chromium, `ui/color/win/accent_color_observer.cc`, lines 21 to 31 |

Notices of change:

- `WM_SETTINGCHANGE` is "sent to all top-level windows when the
  SystemParametersInfo function changes a system-wide setting", with
  `wParam` "the value of the uiAction parameter"; `lParam` "does not
  usually indicate which specific system parameter changed"
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange)).
  On any such message, every `SystemParametersInfo` setting is read again.
- `RegNotifyChangeKeyValue` "detects a single change", after which it is
  called again
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regnotifychangekeyvalue)),
  as Chromium does (`os_settings_provider_win.cc`, lines 234 to 252).
- `UISettings` change events are not all available on every Windows 10
  version; `AnimationsEnabledChanged` was added in version 2004
  ([UISettings](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings)).
  Where an event is missing, the property is read on `WM_SETTINGCHANGE`.

A notice is not itself evidence of a change: only a value that differs is
recorded as one, with the notice that led to its reading.

## Browser preferences

### The participant's preferences

Read from the profile at the bridge's start and on each change, through
the preference service of the browser process. Only a stated list of
preferences is recorded, not the profile's whole preference store, which
also holds browsing data. The proposed list, from `chrome/common/pref_names.h`
in the checkout:

- Font sizes: `webkit.webprefs.default_font_size`,
  `webkit.webprefs.default_fixed_font_size`,
  `webkit.webprefs.minimum_font_size`, and the minimum logical font size
  (lines 352 to 358), and the font families under `webkit.webprefs.fonts`.
- Browser color scheme: `browser.theme.color_scheme2` (line 704).
- Focus highlight: `settings.a11y.focus_highlight` (lines 816 to 817).
- Page colors, "an accessibility feature that simulates forced colors mode
  at the browser level": `settings.a11y.requested_page_colors`,
  `settings.a11y.apply_page_colors_only_on_increased_contrast`, and
  `settings.a11y.page_colors_block_list` (lines 830 to 843).
- Caret browsing: `settings.a11y.caretbrowsing.enabled` (lines 2887 to
  2889).
- Zoom: the default zoom level, and each change of a host's or a tab's
  zoom level through `HostZoomMap::AddZoomLevelChangedCallback`
  (`content/public/browser/host_zoom_map.h`, line 190).

### The effect on the page

Chromium computes the preferences a page is given in
`WebContentsImpl::OnWebPreferencesChanged`, which calls
`SetWebPreferences(ComputeWebPreferences(...))`, and `SetWebPreferences`
sends them to every renderer of the tab
(`content/browser/web_contents/web_contents_impl.cc`, lines 4178 to 4225
and 9189 to 9199). Recording at `SetWebPreferences` gives, for each tab,
what its pages were given, whatever setting it came from.

From `blink::web_pref::WebPreferences`
(`third_party/blink/public/common/web_preferences/web_preferences.h`):

- `standard_font_family_map` and the fixed, serif, sans-serif, cursive,
  fantasy, and math maps (lines 51 to 58);
- `default_font_size`, `default_fixed_font_size`, `minimum_font_size`,
  `minimum_logical_font_size` (lines 59 to 62);
- `prefers_reduced_motion`, `prefers_reduced_transparency`,
  `inverted_colors` (lines 126 to 128);
- `text_track_text_size` and `text_track_font_family` (lines 226 to 228);
- `in_forced_colors` and `is_forced_colors_disabled` (lines 374 and 379);
- `preferred_root_scrollbar_color_scheme`, `preferred_color_scheme`, and
  `preferred_contrast` (lines 390 to 402).

From `blink::RendererPreferences`
(`third_party/blink/public/common/renderer_preferences/renderer_preferences.h`):
`focus_ring_color` (line 45), `caret_blink_interval` (line 51),
`use_overlay_scrollbar` (line 59), `caret_browsing_enabled` (line 93), and
the system font names and heights (lines 72 to 84). Where Chromium sends
these to the renderer is to be found in the checkout before the design is
built.

The first record for a tab holds every listed field; later records hold
the fields that changed.

## Proposed records

On a new desktop channel, `system.preferences`:

- `windows-preferences`, at the start and stop: every setting above, each
  with its value or null and the reason it could not be read.
- `windows-preference-changed`: the setting, the old and new values, and
  the notice that led to the reading (`setting-change` with its `uiAction`
  and area, `registry`, `ui-settings`, or `dpi-changed`).

On the browser channel, at a protocol change:

- `browser-preferences`, at the bridge's start: the profile directory,
  whether it was new, and each listed preference.
- `browser-preference-changed`: the preference and its new value.
- `zoom-level-changed`: the host or tab, the level, and the kind of change.
- `web-preferences-sent`: the tab, and the listed `WebPreferences` and
  `RendererPreferences` fields.

The analysis layer then joins a change to its cause where the evidence
allows, such as the Settings app in the foreground, the browser's settings
page, or a keyboard shortcut, and to the page's response in the recorded
style and layout changes; both are inferences with their basis.

## Decisions

Agreed 2026-10-07:

1. The lists of Windows settings and browser preferences above are enough
   to start with. A setting is added to a list by a change to this design.
2. A setting that `UISettings` provides is read through `UISettings`, with
   its change event, not from the registry where Windows happens to store
   it, so that the recorder follows the supported interface as Windows
   changes. The registry and `SystemParametersInfo` are used only for
   settings `UISettings` does not provide. Where a `UISettings` change
   event is missing on the running version of Windows, the property is
   read again on `WM_SETTINGCHANGE`; the record states which way it was
   read.
3. The caret blink rate is read once, at the start of a recording. A
   change during a recording is not expected and is not watched for; the
   record states that the value is from the start.
4. The recreation must be correct for the recorded instant. Every recorded
   preference that changes how the page is drawn or laid out, such as the
   value of a media query, forced colors, a font family or size, or the
   zoom level, is applied to the recreation as the page was given it at
   that instant. Applying them is a change to
   [page recreation](page-recreation.md), designed with the browser part
   of this work; a preference the recreation cannot apply is listed as a
   difference, not left at the recreation browser's value.
5. A recording may use a browser profile prepared for the test account,
   since participants test on a test platform, usually with test accounts.
   The recorder's existing `ProfileDirectory` option
   (`src/Recorder.Collectors.Browser/BrowserEvidenceReceiverOptions.cs`,
   line 19), not yet offered in the session settings, is offered there.
   The recording records the profile directory and whether it was new,
   and still reads only the stated preferences from it, not its browsing
   data.

## Required tests

- Unit tests of the payload contracts and validators; of comparing values
  so that only a differing value is recorded as a change; and of the
  stated list, so that no preference outside it is recorded.
- Integration tests on the target machine: each Windows setting changed
  in turn while the collector runs, each producing one change record with
  the right old and new values and the way it was read; the caret blink
  rate recorded once at the start.
- Bridge tests in a Chromium build: each listed browser preference changed
  through the preference service, and a zoom change, each producing one
  record; `web-preferences-sent` recorded after a Windows dark mode change.
- A system test on the target machine, with a fixture page that responds
  to `prefers-color-scheme`, `prefers-reduced-motion`, `forced-colors`,
  `prefers-contrast`, and the default font size: during one recording,
  dark mode, animation effects, text size, a contrast theme, and a color
  filter are changed in Windows, and the font size and zoom in the
  browser. The recording shows each change with its time, the
  preferences sent to the page, and the page's recorded style changes
  that follow. The recreation at a frame after each change is drawn with
  the preferences in effect at that frame, and its media queries evaluate
  as they did in the recording.
- A recording with a prepared test profile, showing its preferences
  recorded at the start and its browsing data not recorded.

# Recording Accessibility Preferences

## Status

Proposed 2026-10-07, agreed 2026-10-07 with the decisions under
"Decisions", not built. The build stages, the properties panel, and a
correction to reading the display scale were proposed 2026-10-08, not
built (see "Build stages", "Showing the settings in the player", and
"Display scale"). The build stages and the display scale reading were
agreed 2026-10-08, and the properties panel as proposed later that day.
Stage 1, the Windows settings and the properties panel, was built
2026-10-08 (see "Stage 1 as built"), and the Magnifier change records,
stepping, and visible splitter focus the same day, not yet tested on the
target machine (see "Visible focus and stepping as built"); stages 2 and
3 were then not built.
The owner checked the stepping keys on the target machine 2026-10-08, and
change buttons and counts on the panel's rows were agreed and built the
same day; the owner checked them by mouse and keyboard on the target
machine 2026-10-08, and they remain to check with a head pointer and eye
tracking (see "Change buttons and counts"). Stage 2 was built and
checked on the target machine 2026-10-08, and stage 3 proposed the same
day, then agreed and built that day, not yet run on the target machine
(see "Stage 3: the recreation"). Listed in
[outstanding work](analysis-outstanding-work.md). It accompanies
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
| Display scale | each monitor's DPI | see "Display scale" | |
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

### Display scale

Proposed and agreed 2026-10-08, not built. The table first gave `WM_DPICHANGED` as
the notice. That message is "Sent when the effective dots per inch (dpi)
for a window has changed", when "The window is moved to a new monitor that
has a different DPI" or "The DPI of the monitor hosting the window
changes", and "is only relevant for PROCESS_PER_MONITOR_DPI_AWARE
applications or DPI_AWARENESS_PER_MONITOR_AWARE threads"
([Microsoft](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged)).
The collector's own window is on one monitor, so it would not be told of
a change on another. Instead:

- The collector's window thread is per-monitor DPI aware, for which
  `GetDpiForMonitor` gives "The actual DPI value set by the user for that
  display"
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)).
- Each monitor's DPI, with its device name and bounds, is read at the
  start and stop and again on `WM_DISPLAYCHANGE`, which "is sent to all
  windows when the display resolution has changed"
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-displaychange)),
  on `WM_SETTINGCHANGE`, and on `WM_DPICHANGED`. Whether a scale change
  on the target machine sends any of them is checked by the integration
  test; if none is sent, the design is changed then.

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
- Added 2026-10-10, at protocol 0.59: the browser's theme,
  `browser.theme.user_color2` (line 711), `browser.theme.color_variant2`
  (line 717), `browser.theme.is_grayscale2` (line 727), and
  `extensions.theme.id` (line 682), in the owner's checkout of Chromium
  156.0.8065.0. See "The browser theme".
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
  and area, `registry`, `ui-settings`, `display-change`, or
  `dpi-changed`; `stop` for a difference found by the reading at the
  stop).

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

## Build stages

Proposed and agreed 2026-10-08, not built. The work is built in three stages, each
tested on the target machine before the next:

1. Windows settings: the `system.preferences` collector, its two records
   with their validators, schema, and database columns, and the
   properties panel showing them and Windows Magnifier's level, position,
   and color effect. A test script changes each setting it can change
   and put back through the interface Windows provides, and asks the
   owner to change the others by hand; it then checks each change record
   against what it set. The same run tests whether Windows color filters
   and a contrast theme appear in the captured desktop frames, as
   Magnifier's lens view does and its full screen view does not; if a
   frame does not show them, showing them in the participant's view is
   designed then.
2. Browser preferences: the four browser records in the instrumented
   Chromium at a protocol change, the `ProfileDirectory` option in the
   session settings, and the browser section of the properties panel.
   This stage needs a Chromium build.
3. Recreation: applying the recorded preferences to the recreation, a
   change to [page recreation](page-recreation.md) designed in that
   stage.

## Showing the settings in the player

Logged 2026-10-07. Designed 2026-10-08 in "The properties panel",
proposed and agreed 2026-10-08, not built.

- The player needs properties panels that show the participant's
  accessibility settings at the current frame, for reviewers who do not
  use a screen reader as well as those who do.
- Windows Magnifier's current full screen level and position, recorded
  with each desktop frame by
  [magnified view playback](magnified-view-playback.md), are shown there.
  Until then they are only in the video frame's help text, which only a
  screen reader reports.
- The owner decided against a line of text under the video for the level
  and position: the properties panels are the solution.

### The properties panel

- A collapsible region, "Properties", to the right of the video, 320
  pixels wide by default with a keyboard reachable splitter, like the
  details region in [player layout](player-layout.md). On the target
  machine's 1920 by 1080 screen the video is limited by height, so the
  panel takes width the video does not use when the side panel is
  collapsed. A "Properties" toggle button sits in the transport row
  beside "Details" and "Settings"; Ctrl+Shift+P toggles it. It is open
  by default, is hidden in the frame-only view, and whether it is open
  and its width are kept in `player-layout.json`.
- It shows the values in effect at the current frame, in groups, each a
  list of rows with three columns: the setting, its value, and when it
  was last set ("at start", or the recording time of its last change).
  The groups are Magnifier (level, position, color effect), Display
  (each monitor's scale, text size, app dark mode, transparency, color
  filter, contrast theme, accent color), Motion (animation effects and
  the other animation settings), Pointer and focus (cursor size, caret
  width and blink rate, focus border, keyboard cues, scrollbars, message
  duration), and Keyboard and mouse assistance (sticky, filter, toggle,
  and mouse keys). Stage 2 adds a Browser group.
- A value that could not be read shows "not read" and its reason. Before
  a reading exists, as in a recording made before this work, a row shows
  "not recorded". A setting changed at the current frame's time or in
  the second before it is marked "changed" in its row, in text, not by
  color alone.
- The rows are a read-only list a screen reader reads as "setting, value,
  when set". Moving through playback updates the values without
  announcing them; a change is reported through the timeline event, as
  other events are. Selecting a row's "when set" moves the playhead to
  that change.
- Each change is an event on the timeline, in the `system.preferences`
  channel, so the existing playback filters show or hide it.
- The magnification and color effect in the Magnifier group are read from
  the frame shown, as the participant's view is; the other groups from
  the most recent `windows-preferences` or `windows-preference-changed`
  record at or before the frame's time.

## Stage 1 as built

Built 2026-10-08, not yet tested on the target machine.

### The collector

- `WindowsPreferencesCollector`
  (`src/Recorder.Collectors.Windowing/WindowsPreferencesCollector.cs`) is
  added to every recording; unlike the foreground window collector, it
  has no session setting to turn it off. Its readings are small and are taken only at
  the start, at the stop, and after a notice.
- Its notices come to a hidden top-level window (`WS_POPUP` with
  `WS_EX_TOOLWINDOW`) on a thread of its own that is per-monitor DPI
  aware. A message-only window is not used: Microsoft's
  [Window Features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)
  states that message-only windows do not receive broadcast messages,
  and `WM_SETTINGCHANGE` is broadcast. The window takes
  `WM_SETTINGCHANGE`, `WM_DISPLAYCHANGE`, and `WM_DPICHANGED`.
- A second thread waits on `RegNotifyChangeKeyValue` for the
  `Themes\Personalize` and `ColorFiltering` keys of the current user, and
  opens a missing key again after each `WM_SETTINGCHANGE`, since
  `ColorFiltering` does not exist before a filter is first set.
  `UISettings` events are subscribed where `ApiInformation.IsEventPresent`
  reports them; the start record lists which were present.
- After any notice every setting is read again, and each setting whose
  reading differs from the last is recorded as one change, so a notice
  that changes nothing gives no record. The caret blink time is read
  once, at the start.
- The start record is written before the collector reports that it is
  running.

### The records

- `windows-preferences`: `{reason, uiSettingsEvents, settings}`, where
  `reason` is `start` or `stop`, `uiSettingsEvents` holds a flag for each
  `UISettings` event, and `settings` holds every setting of
  `src/Recorder.Contracts/WindowsPreferenceSettings.cs` as
  `{value, problem}`.
- `windows-preference-changed`: `{setting, previous, current, notice}`,
  where `previous` and `current` each hold the one setting as
  `{value, problem}`, and `notice` is `{kind, uiAction, area, source}`.
- A reading holds a value of the setting's type and a null problem, or a
  null value and the problem. Only a text value may be null without a
  problem, as the contrast theme's name is when Windows gives none.
- The monitors setting is a list of `{deviceName, bounds, isPrimary,
  dpiX, dpiY}`, the scale from `GetDpiForMonitor` with
  `MDT_EFFECTIVE_DPI`.
- Migration `0021_windows_preferences.sql` adds the two tables and a
  table of monitors for each reading that holds them; the validator
  (`src/Recorder.Session/EventPayloadValidator.cs`) refuses a change of
  a setting outside the list or of the caret blink time.

### The player

- The panel is `src/Recorder.App/MainWindow.Properties.cs`, with its rows
  from `src/Recorder.Session/WindowsPreferenceTimeline.cs`. Its rows are
  updated in place, so the row a keyboard or screen reader user is on
  keeps its place during playback.
- A row set during the recording shows the recording time in its "When
  set" column; Enter or a double click on that row moves the playhead to
  the change. A row changed at the frame's time or in the second before
  it shows ", changed" after the time, and is semibold.
- The records are on their own timeline lane, and a "Windows settings"
  check box in the timeline filters shows or hides them. The playback
  index keeps them whole; its version is 6, so the index of an older
  recording is built again.

### The checks so far

- A probe of the collector on the target machine on 2026-10-08 read every
  setting, with a value for each except dark mode and the color filter,
  whose registry keys did not exist. The probe ran as the account the
  sandbox commands use, not the owner's account, which is why those keys
  were missing and why its change of a setting gave no change record.
  The change records are therefore to be checked in a run in the owner's
  account.
- `scripts/Test-AccessibilityPreferences.ps1` changes each setting it can
  change through `SystemParametersInfo`, or through the registry with a
  `WM_SETTINGCHANGE`, and puts it back, then asks the owner to change
  text size, a color filter, a contrast theme, and the display scale and
  put each back. It checks one change record for each change the script
  made, with the values before and after, and a record for each change
  the owner made. While the color filter and the contrast theme are on
  it shows a colored card and measures the saturation and brightness of
  the recorded frames, so a grayscale filter in the frames shows as a
  saturation near 0. Its analysis was checked on the target machine with
  a synthetic recording, including a case it must fail.

### The first run on the target machine

Run 2026-10-08 in the owner's account, results in
`C:\Users\Public\Downloads\accessibility-preferences-test-20261008-115513`.

- One start and one stop record; every `UISettings` event present; no
  change record of the caret blink time.
- Each of the 22 changes the script made gave exactly one change record
  with the values before and after: through `SystemParametersInfo`
  with its `uiAction` (menu animation, menu fade, combo box animation,
  keyboard cues, client area animation, caret width, focus border width,
  message duration, sticky keys), and through the registry notice for
  dark mode and transparency.
- Text size: one record each way, from the `textScaleFactorChanged`
  event. Display scale: one record each way, the monitor's scale 96 to
  120 DPI and back, from `WM_SETTINGCHANGE` with `uiAction` 0x9F.
- Contrast theme: on was read from a `WM_SETTINGCHANGE` with area
  `ImmersiveColorSet`, with the theme's name, accent color, and dark mode
  changing with it; off from the `colorValuesChanged` event.
- Color filter: no record when the filter was turned on or off. The
  `ColorFiltering` key did not exist at the start; it existed at the next
  `WM_SETTINGCHANGE`, from the contrast theme 24 seconds after the filter
  was turned off, when `Active` was read as 0 and `FilterType` did not
  exist. Turning the filter on or off gave no notice the collector
  receives, so a key created during a recording is not read until some
  other notice comes. Not yet fixed.
- A text size change from 100 to 125 percent was recorded at 16:07:55
  UTC, 42 seconds before the display scale change, with no change back
  before the stop. No step asked for it; the owner confirmed it was made
  by hand, and put text size back to 100 percent after the run.
- The cards for the color filter and the contrast theme were not shown,
  through a fault in the script: PowerShell names are not case
  sensitive, and the variable holding the card's times was the `-Card`
  switch itself. Fixed in the script; whether the filter and the theme
  appear in the frames is still to be measured.

### A key created during a recording

Proposed and agreed 2026-10-08, built 2026-10-08, not yet tested on the
target machine.

- While a watched key does not exist, the collector watches its parent
  key (`HKCU\Software\Microsoft` for `ColorFiltering`, and
  `...\CurrentVersion\Themes` for `Personalize`) with
  `RegNotifyChangeKeyValue` and `REG_NOTIFY_CHANGE_NAME`, which Microsoft's
  [RegNotifyChangeKeyValue](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regnotifychangekeyvalue)
  page describes as "Notify the caller if a subkey is added or deleted",
  and not its subtree. When it is signalled and the key exists, the key
  is read, giving a `registry` change record, and watched as other keys
  are. Opening the key again after each `WM_SETTINGCHANGE` is kept.
- A value missing from an existing key stays a reading with its
  problem, as `FilterType` was; no default is assumed, since Microsoft
  does not document these values.
- In the test script, a start reading with a problem is listed, not
  failed: a key that has never been created, as `ColorFiltering` before a
  filter is first used, is a state of the machine, not a fault.
- A watched key that cannot be watched again, as one deleted during the
  recording, is closed, and its parent is watched until it is created
  again; before, a failed watch stopped the registry watches.
- Required test: on the target machine, the color filter step of the
  test script run again (`-SkipCode -ManualSteps 'color filter','contrast
  theme'`), with the `ColorFiltering` key removed first so that it is
  created during the recording. The unit test proposed with this change
  was not written: the watch is of the Windows registry itself, which the
  sandbox build cannot provide, and the account the sandbox commands use
  on the target machine is refused writing under `HKCU`, so the check is
  the owner's run.
- The first rerun, 2026-10-08, ran no step: `powershell -File` passed
  `-ManualSteps 'color filter','contrast theme'` as one string, which
  matched no step. The script now splits the names on commas, stops on a
  name that is not a step or when nothing would run, and lists the steps
  it will run; checked on the target machine through `powershell -File`.

### The rerun with the key created during the recording

Run 2026-10-08 on 94927e7 with the script of 63372bc, results in
`C:\Users\Public\Downloads\accessibility-preferences-test-20261008-123754`,
with the `ColorFiltering` key absent at the start.

- Color filter on: `colorFilterActive` null to true, from a `registry`
  notice of the `ColorFiltering` key, so the key created during the
  recording was read. Off: true to false, from the same notice. The
  owner chose the Grayscale filter; `FilterType` was still reported as
  not existing, so the recorded values do not say which filter was on.
- Contrast theme on and off: `highContrast` from `colorValuesChanged`,
  with the accent color and dark mode changing with it.
- The color filter is not in the recorded frames: the frames while the
  card was shown have a mean saturation of 0.982, the same as the
  baseline card, while the owner saw the card in gray. As with the
  Magnifier's color effects, the filter is applied after the image the
  frames are taken from, so the player has to apply it from the recorded
  values, and needs to know which filter was on.
- The card does not test a contrast theme: it paints fixed colors, which
  a contrast theme does not change, and the owner saw no change in it.
  Whether a contrast theme is in the frames is to be measured from a
  window that follows the theme's colors.
- `colorFilterType` "null to null" is listed as a change because its
  problem changed, from the key not existing to the value not existing;
  the list shows the values only.

### The panel on the target machine

Checked by the owner 2026-10-08 on 94927e7.

- The panel's values follow playback as the recorded settings change,
  and Ctrl+Shift+P shows and hides it.
- The splitter cannot be used from the keyboard without a screen
  reader: it takes focus, but a 5 pixel splitter shows no visible focus,
  so a keyboard user who does not hear it named cannot tell it is
  there. The other two splitters of the player are built the same way.
- That Enter on a row moves to its change was not known: it is given
  only in the list's help text, which only a screen reader reports.

### Visible focus and the Enter key

Proposed 2026-10-08 and agreed the same day; built 2026-10-08, not yet
tested on the target machine (see "Visible focus and stepping as built").

- Each splitter of the player (recorder controls and playback, video and
  properties, playback and details) shows its focus: while it has
  keyboard focus it is drawn in the focus color, 2 pixels wider on each
  side, with the text "Arrow keys resize" in a small label beside it.
  Its arrow keys already move it by `KeyboardIncrement`.
- A row holds only the change in effect at the frame shown, the last at
  or before it, so Enter moves only to that change, and pressed again
  stays there; the earlier and later changes of a setting were reachable
  only from the timeline and Details. The owner asked for stepping
  through a setting's changes from the panel (2026-10-08).
- On a row, Ctrl+Left moves the playback to the change of that setting
  before the one in effect, and Ctrl+Right to the next change after the
  time shown. Enter keeps moving to the change in effect. The row stays
  selected, as rows are updated in place, and the move is announced with
  the value and time, for example "Text size 125 percent, set at
  00:04:12."; at either end it is announced instead, "No earlier change
  of text size." or "No later change of text size.", and the playback
  does not move. A row at "at start" has no change in effect, so
  Ctrl+Left announces no earlier change and Ctrl+Right moves to its
  first change.
- The owner agreed to stepping and to the visible splitter focus
  (2026-10-08), adding that the changes stepped through are events, and
  must be in the timeline with a filter. The Windows settings changes are
  already events (`system.preferences`), in timeline lane 7 with the
  "Windows settings" filter. The Magnifier's changes are not: its level,
  position, and color effect are held only as readings of each frame.
- Magnifier changes become events, recorded where the readings are taken:
  `DesktopFrameCollector.CaptureFrame` reads the level and position
  (`MagGetFullscreenTransform`) and the color effect
  (`MagGetFullscreenColorEffect`) as each frame is captured. When a
  frame's reading of any of them differs from the previous frame's, the
  collector writes a `magnifier-changed` event on a new channel,
  `graphics.magnifier`, at the frame's time, holding the previous and
  current readings, the names of what changed, the frame's sequence
  number, and the previous frame's time. The change happened between the
  two frames; the event says when it was seen, not when it was made. The
  first frame's readings are the start, as the frames already hold them,
  and give no event. Panning gives an event for each frame while the
  view moves.
- The new channel has a catalog entry, a migration, payload validation,
  and samples, as `system.preferences` has; the timeline has a lane for
  it, "Magnifier", and the filters a check box, "Magnifier changes".
- The Magnifier rows of the panel step through these events, as the
  settings rows step through theirs, and show the time of the change in
  effect, so Enter on them moves to it. A recording made before the
  events has none, so its Magnifier rows announce "This recording has no
  Magnifier change records." and do not move; their values still come
  from the frames.
- The Properties panel has a line of text under its list: "Enter: the
  change that set the value. Ctrl+Left and Ctrl+Right: the setting's
  previous and next change." The list's help text says the same.
- Required tests: unit tests of the collector's change detection from a
  series of readings (no change, each reading changing alone, a reading
  becoming unavailable), of the new event's validation and catalog, and
  of the previous and next change of a setting and of a Magnifier row,
  including no change, the first and
  last change, two changes at the same time, and a time between changes;
  a UI test on Windows that each splitter, reached by Tab, shows the
  focus drawing and resizes by its arrow keys, and that Enter, Ctrl+Left
  and Ctrl+Right on a row move the playback as described; on the target
  machine, the owner's check of all of them by keyboard with no screen
  reader.

### Visible focus and stepping as built

Built 2026-10-08 as proposed above, with these details and differences:

- The change records are written by `MagnifierChangeTracker`
  (`src/Recorder.Contracts/MagnifierChanges.cs`), which holds the previous
  frame's readings and compares them with `MagnifierChanges.Compare`; the
  collector calls it after each frame record, on the capture thread, and
  resets it at the start of each recording. A change in a reading's
  problem text, such as a reading becoming unavailable, counts as a change
  of what it reads: the level and position together, or the color effect.
  A frame with no reader on either side, as on a system without the
  Magnifier API, gives no record.
- What changed is held as three booleans, `changed.level`,
  `changed.position`, and `changed.colorEffect`, not as a list of names as
  proposed, because the evidence catalog has no column type for a list of
  text. The validator refuses a record with none true
  (`magnifier-change-empty`) and one whose flags do not match its two
  readings (`magnifier-change-inconsistent`).
- The catalog table is `magnifier_changes`, migration 22
  (`src/Recorder.Database/Migrations/0022_magnifier_changes.sql`). The
  playback index keeps the records whole, as it keeps the settings
  records; its version is 7, so an existing index is rebuilt.
- The timeline lane is lane 8, "Magnifier"; the lane for other channels
  moves to 9. The Details list summarizes a record by what changed and its
  new value, for example "magnifier-changed: level 200 percent".
- A recording with no Magnifier change records cannot be told apart from
  one made after this build in which the Magnifier never changed. In
  both, the Magnifier rows show "with this frame" and Ctrl+Left and
  Ctrl+Right announce "This recording has no Magnifier change records."
  With records, a row with no change in effect shows "at start".
- A settings row of a recording with no settings records announces "This
  recording has no Windows settings records." Changes of a setting at the
  same time count as one change.
- The panel takes Enter, Ctrl+Left, and Ctrl+Right before the list does,
  as the list otherwise uses the arrow keys to move and scroll.
- The splitters' label is a tooltip, "Arrow keys resize", shown when the
  splitter gets keyboard focus (`ToolTipService.ShowsToolTipOnKeyboardFocus`)
  and on mouse hover, rather than a separate label beside it. The focused
  splitter is drawn in the system highlight color, so it follows a
  contrast theme. It is 4 pixels wider, with margins that keep its layout
  size, so the panels do not move.
- Checks in the sandbox: unit tests of the comparison (no change, each
  part alone, a reading becoming unavailable, no reader), of the tracker
  (first frame, unchanged frames, a change, reset), of the validator with
  an inconsistent, an empty, and an incomplete record, of stepping (no
  change, before the first, at the first, between, at a change, ties,
  after the last), of the Magnifier and settings rows' keys and times, and
  of the index and archive keeping and summarizing the records. The full
  suite gives 1,332 passed, with the 12 known failures that need Windows.
- Checks on the target machine by the sandbox account: a run of the
  collector for 3 seconds with Magnifier off gave 16 frames, both
  channels declared, readings in the same form as before, and no change
  records. `scripts/Test-MagnifierChanges.ps1`, which checks a recording's
  change records against its frames, passed a generated set of records
  and found each of three faults placed in a second set: a missing
  record, wrong flags, and a step with no record.
- To check by the owner: a Magnifier recording with
  `scripts/Test-MagnifierChanges.ps1`; then, in the player with no screen
  reader, the Magnifier lane and filter, Enter, Ctrl+Left and Ctrl+Right
  on rows, and the splitters reached by Tab.


## Stage 2: browser preferences

Proposed 2026-10-08 and agreed the same day, with the "Sent to the page"
group following the tab last navigated and the hooks in `chrome/browser`
agreed; built 2026-10-08 (see "As built" below), not yet checked on the
target machine. The owner decided 2026-10-08 to start stage 2 with the stage 1 gaps left open: the Magnifier change
records on a real Magnifier recording, applying a Windows color filter in
the participant's view (with which filter was on, which the recording does
not say), and whether a contrast theme is in the frames. They stay listed
in [outstanding work](analysis-outstanding-work.md). Line numbers below
are from the owner's checkout, Chromium 156.0.8065.0.

### Where the page is given its preferences

Agreed 2026-10-08: the values sent to the page are recorded where
`RenderViewHostImpl` sends them, not at
`WebContentsImpl::SetWebPreferences` as proposed under "The effect on the
page". `SetWebPreferences` (`web_contents_impl.cc`, lines 9189 to 9199)
covers only later changes; a page's first values are sent when its view
is created in a renderer, which happens again when a navigation moves the
page to another renderer process. In
`content/browser/renderer_host/render_view_host_impl.cc`:

- `CreateRenderView` (line 410): `params->renderer_preferences` and
  `params->web_preferences` (lines 450 to 451), the values the view starts
  with.
- `SendWebPreferencesToRenderer` (lines 900 to 907), the later
  `WebPreferences`, including those of a page restored from the
  back-forward cache, which `SetWebPreferences` passes over.
- `SendRendererPreferencesToRenderer` (lines 909 to 916), the later
  `RendererPreferences`, reached from `WebContentsImpl::SyncRendererPrefs`
  (lines 4236 to 4242).

### The records

Protocol 0.56, on the browser channel:

- `web-preferences-sent`, at each of the three points: the page
  (`pageFrameTreeNodeId`, as the navigation records name it), the renderer
  process, which point sent it (`view-created`, `web-preferences`, or
  `renderer-preferences`), and the listed fields of "The effect on the
  page". A view's first record holds every listed field; later records
  for the same view hold the fields that differ from what that view was
  last sent, and a record with no difference is not written.
- `browser-preferences`, when the profile's preferences are loaded
  (`ProfileImpl::OnPrefsLoaded`, `chrome/browser/profiles/profile_impl.cc`,
  line 1224): the profile directory, whether the profile was new, the
  default zoom level, and each listed preference of "The participant's
  preferences" with its value, or that it is not set and the default
  used. `pref_names.h` lines: font sizes 352 to 358, color scheme 704,
  focus highlight 816, page colors 832 to 843.
- `browser-preference-changed`, from a `PrefChangeRegistrar` on the same
  list: the preference, its previous and new value.
- `zoom-level-changed`, from `HostZoomMap::AddZoomLevelChangedCallback`
  (`content/public/browser/host_zoom_map.h`, line 190): the mode of
  `ZoomLevelChangeMode` (line 44), the host or scheme and host, the level,
  and its percentage; and a change of the default zoom level.
- No other preference and no browsing data is read from the profile. The
  hooks in `chrome/browser` add a dependency on the bridge to that target,
  as `content/browser` already has.

### The recorder

- Typed contracts, validation, and samples for the four records, a
  database migration (23), and catalog entries, as for the other browser
  records.
- The session settings offer the existing `ProfileDirectory` option as
  "Browser profile folder", empty for a new profile per recording, with a
  folder picker; the recording records the folder, as
  `browser-preferences` does.

### The player

- A "Browser" group in the Properties panel, after the Windows groups:
  each listed preference and the default zoom level, set at the start or
  at its last change, with the change buttons and counts of "Change
  buttons and counts".
- A "Sent to the page" group: the listed fields last sent to the page of
  the most recent committed primary main frame navigation at or before
  the time shown, with its address in the group's first row. A recording
  with more than one tab shows the tab last navigated; showing the tab in
  the foreground is left until the foreground tab is recorded.
- The changes are events, in a timeline lane "Browser settings" with a
  filter "Browser settings", holding `browser-preference-changed`,
  `zoom-level-changed`, and the `web-preferences-sent` records after a
  view's first.

### Required tests

- Unit tests: the contracts and validators of the four records, including
  a record with no field, an unlisted preference, and a later record with
  an unchanged field; the panel's values at a time from a start record and
  changes; the Browser lane and filter.
- Integration script tests (`chromium/test_integrate.py`): each hook
  applied once to the checkout's code and refused on code it does not
  match.
- On the target machine, with a Chromium build and a fixture page that
  responds to `prefers-color-scheme`, `prefers-reduced-motion`,
  `forced-colors`, `prefers-contrast`, and the default font size: during
  one recording, the browser's font size, zoom, and color scheme are
  changed, then Windows dark mode, animation effects, and a contrast
  theme. Each choice made in the browser gives one change record, and
  each change that reaches the page a `web-preferences-sent` record with
  the values the page's style changes show. Then a recording with a
  prepared profile folder, its preferences recorded at the start and its
  browsing data not recorded, with page colors stored in the folder
  beforehand. Page colors moved from the first recording to the prepared
  profile 2026-10-08, as the build has no setting that changes them while
  it runs (see "Page colors").

### As built

Built 2026-10-08 to the design above, with these differences and limits.

- The records are on a channel of their own, `browser.preferences`, not
  on `browser.navigation`, so that the timeline lane and filter
  "Browser settings" hold them alone. The lane therefore also holds the
  `browser-preferences` record and each view's first `web-preferences-sent`
  record, not only the changes; the design listed only the changes.
- `browser-preferences` is recorded from `ProfileImpl::DoFinalInit`, which
  `OnPrefsLoaded` reaches once the profile's preferences are read, just
  after the profile's own `PrefChangeRegistrar` is given its first
  preference. The same registrar watches the listed preferences, so the
  watching ends with the profile. A listed preference the build does not
  register is recorded with the problem "not registered" and not watched.
- The default zoom level is not in `browser-preferences`, as it is not a
  preference of the listed store but of the zoom map: each change of the
  default, including the profile's own default as Chromium applies it, is
  a `zoom-level-changed` record with the mode `default`. The zoom map
  starts at 100 percent and records no change when the level set is the
  one it has, so a recording with no such record had a default of 100
  percent. The player shows "100% (no default set)" for it.
- A font family is recorded for the common script, `Zyyy`, only; the
  per-script families are not recorded.
- The caret blink interval is recorded as two fields,
  `hasCaretBlinkInterval` and `caretBlinkIntervalMilliseconds`, the
  second 0 when the first is false, as `RendererPreferences` holds an
  optional interval.
- A view is identified by the address of its `RenderViewHostImpl`, as
  decimal text (`viewId`), which is unique while the view exists and may
  be reused after it is destroyed; the bridge forgets a view's last values
  when it is created again, as its first record then holds every field.
- A view's first record holds every listed field, and the validator
  refuses one that does not. Whether a later record holds only the fields
  that differ is the bridge's comparison with the view's last values,
  checked by the integration script tests and the target machine check,
  not by the recorder's validation, which sees one record at a time.
- The Windows font fields (`captionFontFamily` to `messageFontHeight`)
  are read under `BUILDFLAG(IS_WIN)`, as `RendererPreferences` holds them
  only on Windows.
- `requestedPageColors` is shown as its stored number, "value 1" for
  example, as the meaning of each number was not confirmed in the
  owner's checkout when this was built. It was confirmed after (see
  "Page colors"); the player still shows the number. `colorScheme` is shown as system, light, or dark,
  from `ThemeService::BrowserColorScheme`
  (`chrome/browser/themes/theme_service.h`: `kSystem` 0, `kLight` 1,
  `kDark` 2).
- The "Sent to the page" group's rows have no change buttons: its page
  changes with each navigation, so a row's changes are not one setting's
  changes through the recording. Its Page row shows the address of the
  page and when it was loaded; a field not yet sent to that page reads
  "not sent yet". The group always has the same rows, so the rows do not
  move as playback crosses a navigation.
- A recording with no browser preference records, made before protocol
  0.56 or without the browser, shows one Browser row, "Browser
  preferences", "not recorded".
- The session settings' "Browser profile folder" must name an existing
  folder; empty is a new profile for the recording, removed after it, as
  before. A folder given is kept.
- The playback index is version 8, keeping the `browser.preferences`
  records whole; an older index is derived again.

Where each part is:

- Bridge: `RecordBrowserWebPreferencesSent`, `RecordBrowserPreferences`,
  `RecordBrowserPreferenceChanged`, and `RecordBrowserZoomLevelChanged`
  in `chromium/recorder_bridge/browser_bridge.cc`.
- Hooks, by `chromium/integrate.py`: `render_view_host_impl.cc`
  (`CreateRenderView`, before `CreateView`; `SendWebPreferencesToRenderer`
  and `SendRendererPreferencesToRenderer`, after each send),
  `host_zoom_map_impl.cc` (each of the four places that notify the zoom
  level change callbacks, for a host, a scheme and host, a page that uses
  the default level in `SetDefaultZoomLevelInternal`, and a temporary
  level, each just before the callbacks run; and `SetDefaultZoomLevel`,
  after its level is set),
  `profile_impl.cc` (`DoFinalInit`), and the dependency of
  `source_set("misc")` in `chrome/browser/profiles/BUILD.gn` on the
  bridge.
- Recorder: `BrowserPreferenceSettings`
  (`src/Recorder.Contracts/BrowserPreferenceSettings.cs`), the payload
  records in `BrowserEvidenceContracts.cs`, their validators in
  `EventPayloadValidator.cs`, migration
  `0023_browser_preferences.sql`, and the catalog entries.
- Player: `BrowserPreferenceTimeline`
  (`src/Recorder.Session/BrowserPreferenceTimeline.cs`), the page commits
  read by `SessionPlaybackArchiveBuilder.PageCommitOf`, and the Browser
  rows' change times through `PropertyChangeSteps.TimesOf`.

Checks in the sandbox:

- Unit tests (`tests/Recorder.Tests/BrowserPreferencesTests.cs`): the
  samples valid; refused, a change of an unlisted preference, a
  preferences record with an unlisted preference, a later send with no
  field, a first send without every field, an unlisted field or send
  point, and an unknown zoom mode; the Browser rows at the start and
  after changes, the default zoom row, the Sent to the page rows before a
  page, after its first send, after a later send of one field, and after
  a second page; the same number of rows at every time; the change times
  of a row and its count; a page commit read only from a committed
  cross-document primary main frame navigation; the summaries; and the
  playback index keeping the records whole. The database tests, run
  against PostgreSQL 18, store and read back every sample through the
  new tables.
- Integration script tests: each hook applied once to copies of the
  owner's checkout files and refused on code it does not match.
- Not checked in the sandbox: the bridge and hooks compiled, which needs
  the owner's build; the Browser settings lane and filter, which are in
  the player, not in the test project; and the panel's rendering.

To check on the target machine, with the fixture page
`tests/fixtures/accessibility-preferences/index.html` and
`scripts/Test-BrowserPreferences.ps1`: the recording of "Required tests"
above, then the prepared profile recording (`-ProfileFolder`), and in
the player the Browser and Sent to the page rows following playback,
the Browser rows' change buttons by mouse, and the Browser settings lane
and filter.

### The first run on the target machine

Run by the owner 2026-10-08 with Chromium built at a5e1e91: the
recording of "Required tests" (results
`browser-preferences-test-20261008-202129`) and the prepared profile
recording (`browser-preferences-test-20261008-205032`). Every change
made in the browser was recorded, and every Windows change reached the
fixture page with the values its style showed. The script marked four of
sixteen steps failed; none was a fault of the records.

- Font size: four choices made in the menu, Large, Very small, Large,
  then Medium, gave four change records of `defaultFontSize` (16 to 20,
  20 to 9, 9 to 20, 20 to 16), each with a change of
  `defaultFixedFontSize` (13 to 17, 17 to 6, 6 to 17, 17 to 13), as
  Chromium's font size menu sets both, and a send of both to the fixture
  page. The script expected exactly one change record a step.
- Default zoom: four choices, 125, 50, 125, then 100 percent, gave four
  `default` records, each with a `host` record for each page that uses
  the default, the fixture page and the settings page. The script
  expected exactly one.
- Page zoom: Ctrl and plus gave one `host` record of the fixture page at
  110 percent, and Ctrl and 0 one at 100. The script showed the first as
  110.00000000000001 percent, the double's full digits; the player rounds
  it.
- Browser color mode: the owner made the change while the script was at
  the page colors step, so that step listed the color scheme changes and
  the color mode step none. The records: device to dark (0 to 2) at
  00:36:17 UTC, dark to light (2 to 1) at 00:36:31, light to device (1 to
  0) at 00:37:42. The first sent nothing to the page, as Windows was in
  dark mode and the page already had dark; the second sent light, the
  third dark.
- Page colors: no setting found, answered n, no record. See "Page
  colors".
- Windows dark mode, animation effects, and the contrast theme: each
  change and restore sent its field with the expected value
  (`preferredColorScheme` light and dark, `prefersReducedMotion` true and
  false, `inForcedColors` true with `preferredContrast` more, then false
  with no preference), and the owner saw each change on the page.
- Opening the Customize Chrome side panel gave four `temporary` zoom
  records of `customize-chrome-side-panel.top-chrome` at 100 percent:
  Chromium's own panel, recorded as any other page.
- The prepared profile run: one `browser-preferences` record, from the
  prepared folder's `Default`, not new, with `defaultFontSize` 20 and not
  the default; no default zoom record, so 100 percent; one first send to
  the fixture page.
- The script's line "First default zoom record" showed the first change,
  125 percent, which read as the start value; the start was 100 percent.

Changed in `scripts/Test-BrowserPreferences.ps1` after the run: a step
passes with at least one change record of its preference, or at least
one zoom record of its mode, as each choice in a menu is a change;
`defaultFixedFontSize` is listed as the font size step's companion, not
as another change; other changes in a step's time are listed with their
values; zoom percentages are rounded to one decimal; the summary gives
the default zoom at the start, from a default record before the first
step or 100 percent, and lists every default record; the page colors step
is removed; and the prepared profile run checks page colors stored with
`scripts/Set-PageColors.ps1` (`-PreparedPageColors`).

The first recording analysed again 2026-10-08 with the changed script
and `-ResultsPath`, from the events already exported: 13 of 16 steps
pass. Failed: the page colors change and restore, a step of that run, as
no page colors record could be made; and the browser color mode change,
as the change was made in the page colors step's time, where it is
listed under otherChanges ("colorScheme 0 to 2; colorScheme 2 to 1").
The default zoom at the start reads 100 percent, with no record before
the first step.

Page colors by a prepared profile, run by the owner 2026-10-08 (results
`browser-preferences-test-20261008-212241`): `Set-PageColors.ps1` stored
2, Dusk, in `a11y-test-profile`, and the prepared profile run with
`-PreparedPageColors 2` passed all eight checks. The `browser-preferences`
record held `requestedPageColors` 2, not the default, with
`defaultFontSize` 20 from before; the fixture page's first values had
`inForcedColors` true. The folder was set back to 0 after. Not checked:
what the page showed, as the script does not ask, and a page colors
change during a recording, which the build cannot make.

The player, checked by the owner 2026-10-08 on the first recording: the
Browser rows follow playback, with their change buttons and counts. On
the page colors recording the Browser rows have no buttons, as designed,
as nothing changed during it; the prepared values are its start.

### Page colors

Found in the owner's checkout 2026-10-08. `PageColorsController`
(`chrome/browser/accessibility/page_colors_controller.h` and `.cc`)
watches `settings.a11y.requested_page_colors` and
`settings.a11y.apply_page_colors_only_on_increased_contrast` and sets
the web theme's forced colors, color scheme, and contrast from them. The
numbers, from its `PageColors`: 0 no preference, the default (the page
follows Windows); 1 off (never forced, contrast no preference); 2 Dusk,
3 Desert, 4 Night Sky, 5 Aquatic, 6 White (each forced, with contrast
more, in the colors of that Windows contrast theme; Dusk, Night Sky, and
Aquatic dark, Desert and White light). With the second preference true,
its default false, page colors apply only while Windows asks for more
contrast.

Nothing in the build changes the preference while it runs: no source in
`chrome/browser/resources/settings` or `chrome/browser/ui` names it,
`settings_private`'s `prefs_util.cc` does not list it, so the settings
pages cannot set it, and no policy maps to it. In Microsoft Edge, built
on Chromium, it is Settings, Accessibility, Page colors
([iTechGuides](https://www.itechguides.com/how-to-change-the-accent-color-in-microsoft-edge-chromium/)).

So it is tested by the prepared profile: `scripts/Set-PageColors.ps1`
stores a number in the folder's `Default\Preferences` while Chromium is
closed, and the prepared profile run with `-PreparedPageColors` checks
that the number is recorded at the start and that the fixture page's
first values have forced colors on, from 2, or off, for 1. A change
during a recording is not tested on the target machine. Its record
comes from the same profile registrar as the font size and color mode
changes, which were, and its effect on the page from the same web theme
as the contrast theme, which was.

## Change buttons and counts

Proposed by the owner 2026-10-08 after checking 7ae9f19 on the target
machine, refined and agreed the same day; built 2026-10-08 and checked
by the owner by mouse and keyboard on the target machine the same day
(see "The owner's check of 45a500c").

### The owner's check of 7ae9f19

- The player running was the 7ae9f19 build, from its package folder.
- A single click on a row selects it and does nothing more, as built;
  this was taken at first for a fault, as the panel did not say that the
  keys act on the selected row.
- Ctrl+Left and Ctrl+Right step through a selected row's changes as
  designed.
- The owner found the panel not intuitive: it does not show how many
  changes a setting has, and stepping needs the keyboard. The panel must
  work by mouse alone, for head pointer and eye tracking use.

### The design

- A row whose setting has no change in the recording is shown as before,
  with no buttons and no count. This includes every row of a recording
  with no records for its group, such as the Magnifier rows of a
  recording made before the Magnifier change records.
- A row whose setting has one change or more ends with a previous change
  button, a next change button, and a count, `i/n`, in a column of its
  own, "Changes". The owner proposed buttons for more than one change;
  showing them for a single change as well, so that the sole change can
  be reached by mouse, was agreed.
- `n` is the number of changes of that setting in the recording, from its
  start; the value at the start is not a change, and changes at the same
  time are one change. `i` is the change in effect at the playhead, its
  last change at or before it, counted from 1; before the first change it
  is 0, so `0/n`, and at or after the last, `n/n`.
- Previous moves the playhead to the nearest change strictly before it,
  and Next to the nearest strictly after it. Between the second and third
  of four changes the row shows `2/4`, Previous moves to the second
  change, and Next to the third; at the second change, Previous moves to
  the first. A button with no change in its direction is disabled.
- Ctrl+Left and Ctrl+Right move the same way. This changes Ctrl+Left
  from the earlier design, in which it moved to the change before the one
  in effect, so that the keys and the buttons agree. Enter and a double
  click still move to the change in effect.
- Each move is announced as before, with the value and the time.

### As built

- The count and the moves come from `PropertyChangeSteps.Locate`
  (`src/Recorder.Session/MagnifierChangeTimeline.cs`), which gives a
  `PropertyChangePosition`: the change in effect, the count, and the
  previous and next change. `PropertyChangeSteps.Previous` now gives the
  last change strictly before the time.
- The buttons are 44 by 44 pixels, the target size of WCAG 2.5.5, and
  show the Segoe UI Symbol triangles. They are named, and have tooltips,
  "Previous color filter change" and "Next color filter change", for
  example; a disabled button still shows its tooltip. The count has a
  fixed minimum width, so the buttons stay in place as it changes, and
  reads in full as "Change 2 of 4", or "Before the first of 4 changes";
  the row's name ends with the same words, for example "Color filter, on,
  00:00:12.000, change 2 of 4".
- Each button acts on its own row, without the row being selected, and
  does not take keyboard focus, so a click leaves the focus and the
  selection where they were. They are therefore not Tab stops: from the
  keyboard the same moves are Ctrl+Left and Ctrl+Right on the row, and
  speech and screen reader users can still invoke the buttons through UI
  Automation.
- The rows with buttons are taller than the others. Whether a row has
  buttons is fixed for a recording, so rows do not move during playback;
  the buttons are enabled and disabled in place.
- The panel's default width is 480 pixels, up from 320, so the new column
  is in view without scrolling sideways, and its largest width 800. The
  layout file holds the width as `propertiesPanelWidth`; a width saved as
  `propertiesWidth` by an earlier build is read as the default.
- The hint under the list reads: "The buttons at the end of a row step
  through that setting's changes. The count is the change in effect, of
  all its changes from the start. Keys: Ctrl+Left and Ctrl+Right step;
  Enter goes to the change that set the value." The list's help text says
  the same.
- Checks in the sandbox: unit tests of the count and moves with no
  change, before the first change, at the first, between changes, at a
  change, at and after the last, ties, and a single change; the stepping
  tests updated to the new previous change; a layout test that an earlier
  saved width is read as the default and the new one is kept. The player
  is built with the Windows targeting pack; its rendering cannot be
  checked in the sandbox.
- To check by the owner, by mouse alone, on a recording with settings
  and Magnifier changes: the buttons and counts on the rows with changes
  and none on the others; `0/n` before a first change; Previous and Next
  at each end disabled; the counts following playback; and the moves
  announced with a screen reader.

### The owner's check of 45a500c

Checked by the owner 2026-10-08 on 45a500c.

- The previous and next change buttons work with the mouse.
- Ctrl+Left and Ctrl+Right work from the keyboard.
- Not yet checked: use with a head pointer and with eye tracking, as the
  equipment was not available. The details of the check, such as each
  item of "To check by the owner" above, were not reported separately.

## Stage 3: the recreation

Proposed 2026-10-08, for agreement before it is built; agreed by the owner
the same day, with its three proposals: the color maps recorded at
protocol 0.57, the recorded values applied in the renderer's recreation
mode, and the values carried in a root element attribute. Built
2026-10-08 (see "As built" below). Stage 3 applies
the recorded preferences to the recreation, as "Build stages" and
[page recreation](page-recreation.md), "The environment", require: every
recorded preference that changes how the page is drawn or laid out is
applied as the page was given it at the instant, and one that cannot be
is listed as a difference.

### What the recreation already shows

The recreation draws each recorded node from its recorded computed style
and recorded boxes, lines, and glyphs (see
[page recreation](page-recreation.md), "Slice 3 revision"). Those values
were worked out by Chromium with the participant's preferences, so the
recorded nodes already have the font sizes and families the settings
gave them, their colors as resolved under the color scheme and forced
colors in effect, and their geometry at the zoom in effect.

### What it does not

1. Media queries. Blink evaluates `@media` rules in the recreation with
   the recreation's own values, so DevTools' Styles pane can show a
   `prefers-color-scheme: dark` rule as not applying to a node whose
   recorded style is dark, and `matchMedia()` in the Console answers for
   the recreation, not the participant.
2. What Blink draws from the preferences rather than from a computed
   style: native form controls and scroll bars (the color scheme, the
   root scroll bar's color scheme, overlay scroll bars), the system
   colors of forced colors mode where Blink resolves them itself, the
   backplate it draws behind text in forced colors mode, the color of an
   `outline-style: auto` focus ring, and caret browsing.
3. The zoom factor. The recreation emulates the recorded viewport and
   device pixel ratio only, so `devicePixelRatio`, the `resolution` media
   feature, and whatever layout Blink still works out itself, such as
   SVG, are at the recreation's zoom.
4. The recreation browser gives the page the viewing machine's own
   preferences today: an auditor whose Windows is in dark mode opens a
   recreation that is given dark, whatever the participant had.

### Which values

- The page's values at the instant: the first `web-preferences-sent`
  record of the recorded page's view, with each later one for it merged
  over it, up to the frame's basis. The document's page is the frame tree
  node of its primary main frame navigation, as the panel's "Sent to the
  page" group finds it.
- The zoom: the latest `zoom-level-changed` record at or before the
  instant for the tab (`temporary`), else for the page's host or scheme
  and host, else for the default, else 100 percent.
- The color maps, recorded from this stage (below).

### The color maps (a new record)

Chromium gives each page three maps of 67 colors, light, dark, and forced
colors (`RendererColorId`, `ui/color/color_id.mojom`): the CSS system
colors, such as `Canvas` and `ButtonText`, and the colors of native
controls and scroll bars. It sends them when a view is created
(`render_view_host_impl.cc`, line 573) and when they change
(`WebContentsImpl::HandleColorRelatedStateChanges`,
`web_contents_impl.cc`, lines 13075 to 13110), after the web preferences.
The forced colors map holds the participant's contrast theme colors.

They are not recorded today, so a recreation in forced colors can only
take another palette: the recreation browser's own, from the viewing
machine, or the generic one DevTools' forced colors emulation draws
(`Page::EmulateForcedColors`, `page.cc`, line 592). Proposed: a record
`color-maps-sent` on `browser.preferences` at protocol 0.57, at those two
points, holding each map that differs from the view's last, as
`web-preferences-sent` holds the fields that differ.

### How they are applied

In the recreation mode of the instrumented Chromium, in the renderer, as
the recorded styles and geometry are:

- The values are carried on the recreated document's root element in a
  `data-a11y-recorded-preferences` attribute, as the other recorded
  values are carried on their elements; DevTools' Elements pane shows it,
  and the evidence panel says it was not an attribute of the recorded
  page.
- `WebViewImpl::UpdateWebPreferences` (`web_view_impl.cc`, line 3927)
  and `WebViewImpl::UpdateRendererPreferences` (line 3789) replace each
  recorded field with its recorded value before applying the
  preferences, at the first application and at every later send from the
  recreation browser, so a change on the viewing machine cannot undo it.
- `Page::UpdateColorProviders` (`page.cc`) takes the recorded maps in
  place of those sent.
- The page's zoom is set to the recorded level; the place Blink takes the
  zoom level from the browser is found in the checkout when this is
  built.

Media queries, `matchMedia()`, DevTools' rule matching, native controls,
scroll bars, the focus ring, and the system colors then follow the
participant's values. DevTools' emulation (`Emulation.setEmulatedMedia`)
is not used: it sets media features only, its forced colors are the
generic palette, and it has no font, focus ring, caret, or zoom setting.

A child frame's document shares its page's values, as in Chromium. A
popup's page, such as an open select list, is given its owner page's
values.

### The evidence panel

The panel lists the values applied and the time of the record each came
from. A recording made before protocol 0.56, or without the browser,
says the page's preferences were not recorded and the recreation's own
were used; one made before 0.57 says the same of the color maps.

### Limits

- Only the listed fields are applied; a preference Chromium gives the
  page outside them is the recreation's.
- Windows-level effects on the screen, such as Magnifier and color
  filters, are not part of the page; the participant's view shows them.

### Required tests

- Unit tests: the page's values at a time, from its first send and later
  ones; a document's page; the zoom chosen from host, temporary, and
  default records; the `color-maps-sent` contract and validator; the
  attribute written; the panel's lines, including a recording before
  0.56 and one before 0.57.
- Integration script tests (`chromium/test_integrate.py`): each new hook
  applied once and refused on code it does not match.
- On the target machine: a recording of the fixture page with the font
  size, dark mode, a contrast theme, reduced motion, and a zoom of 125
  percent each changed in turn, then a recreation at a frame after each,
  with the viewing machine's settings the opposite: `matchMedia()` in the
  Console answers as recorded, the scroll bars and controls have the
  recorded scheme or contrast colors, and the Styles pane shows the
  matching `@media` rules applying.

### As built

Built 2026-10-08 to the design above, with these decisions and limits.

- `color-maps-sent` (protocol 0.57) is recorded by the bridge's
  `RecordBrowserColorMapsSent`, from two hooks `integrate.py` adds to
  `content/browser`: in `RenderViewHostImpl::CreateRenderView`, after
  `params->color_provider_colors` is set (point `view-created`), and in
  `WebContentsImpl::HandleColorRelatedStateChanges`, after
  `UpdateColorProviders` is broadcast (point `color-providers`). A view's
  first record holds the three maps; a later one only the maps that differ
  from the view's last, each whole, and no record is written when none
  differs. Each map holds the 67 `RendererColorId` values by name, as
  `#AARRGGBB`; a value the bridge has no name for is left out, and the
  validator refuses a map that lacks a listed name.
- The values are carried in `data-a11y-recorded-preferences` on the served
  document's `<html>` element. Its text is a list of entries separated by
  `; `: `field NAME TYPE VALUE` (`b`, `i`, `n`, or `t`, text
  percent-encoded), `zoom LEVEL`, and `color MAP NAME AARRGGBB`
  (`chromium/recorder_bridge/recreation_preferences.h`, which the
  recorder's `RecordedPreferences` writes and the renderer reads). Text
  that does not follow the form gives no values at all, so a damaged
  attribute leaves the recreation browser's own values rather than some of
  the recorded ones.
- The renderer reads the attribute only in recreation mode and only from
  the outermost main frame's document. The trigger is
  `HTMLHtmlElement::InsertedByParser`, the first moment the served root
  and its attribute exist, before anything is laid out: it applies the web
  preferences, renderer preferences, color maps, and zoom again, now with
  the recorded values. `WebViewImpl::UpdateWebPreferences` and
  `UpdateRendererPreferences` replace each recorded field after every
  later send, `Page::UpdateColorProviders` takes the recorded maps in
  place of those sent, and `WebFrameWidgetImpl::SetZoomInternal` sets the
  recorded level after its test override, so the recreation browser
  cannot undo them. The auditor's own zoom keys therefore do not zoom the
  recreation.
- The builder script copies the attribute from the served root to the
  recorded root that replaces it, as the renderer reads it again at each
  later send.
- The page's values are found as "Which values" says, at the time the
  recreated state is read at (its basis), and the document's page is the
  frame tree node id of its page commit, as the properties panel finds it
  (`RecordingFileDocuments.PageFrameTreeNodeIdOf`).
- The zoom is chosen as `HostZoomMapImpl` chooses it: the temporary level,
  else the scheme and host's, else the host's, else the default, else
  level 0. A temporary record names its host but not its tab, so one is
  taken only from the page's own commit on; a host record made because
  the host follows the default gives way to the default.
- The recreation's viewport is emulated at the recorded CSS size times the
  zoom factor, 1.2 to the power of the level, with a device scale factor
  of the recorded device pixel ratio over the factor, and its window is
  sized for that, so the zoomed page has the recorded CSS size and
  `devicePixelRatio`. The note that browser zoom is not set is written
  only when no zoom was recorded.
- Corrected 2026-10-09, found by reviewing the target machine script after
  the owner's first recording: Chromium's `devicePixelRatio` includes the
  browser zoom (`LocalFrame::DevicePixelRatio` is the layout zoom factor),
  so a layout checkpoint's size and ratio are those of the zoom at the
  checkpoint, which may be older than the frame's. The viewport is now
  emulated at the checkpoint's CSS size times the zoom at the checkpoint,
  with a device scale factor of its ratio over that zoom, and the
  renderer's zoom at the frame then gives the page the CSS size and
  `devicePixelRatio` it had, as the same window zoomed. A checkpoint before
  any zoom record counts as 100 percent. The evidence panel says when the
  two zooms differ, with the size and ratio shown. The script's expected
  `devicePixelRatio` is worked out the same way.
- The evidence panel lists each value with the time of its record, the
  zoom and where it came from, each map, that the attribute was not part
  of the recorded page, and the limits: a recording before 0.56, or before
  0.57 for the maps, says the recreation browser's own values were used.
- A child frame the recreation browser puts in a renderer process of its
  own, such as a frame of another site, is given the recreation browser's
  own values: its process has the page's main frame only as a remote
  frame, with no attribute to read. A child frame in the page's process,
  and the popups, which the recreation draws in the page, share the
  page's values.
- Tests: `RecreationPreferencesTests` (the contract and validator, the
  page's values from its first and later sends, the maps, the zoom from
  host, scheme and host, temporary, and default records, the attribute
  and its encoding, the served markup, the panel's notes for a recording
  before 0.56 and one before 0.57, the emulated viewport, and a
  document's page); the database round trip of the new samples; the
  bridge header's own test, `recreation_preferences_test.cc`; and
  `RecreationPreferencesIntegrationTests` in
  `chromium/test_integrate.py`, each hook applied once and refused on
  code it does not match. Not yet run: the check on the target machine,
  with `scripts/Test-RecreationPreferences.ps1`.
- The owner's recording of 2026-10-09, with Windows text size at 200
  percent and then 100 percent, played correctly, but a recreation at a
  frame at 200 percent ended in Chromium's "Aw, Snap!" page with
  `STATUS_BREAKPOINT`, a renderer stopping on a failed check. The build's
  checks include DCHECKs (`is_debug = false` without `is_official_build`).
  In this Chromium the text size multiplies the device scale factor
  (`GetScaleFactorsForDPI` in `ui/display/win/screen_win.cc`), so a page
  at 200 percent records twice the `devicePixelRatio`, and Windows' system
  font heights, recorded in `RendererPreferences`, change too. The cause is
  not yet known. Each recreation browser now writes Chromium's log
  (`--enable-logging --log-file`) to
  `%LOCALAPPDATA%\Windows A11y Recorder\recreations\logs`, named for the
  recreation and kept after it closes (the newest 20), so a failed check
  is reported with its file, line, and stack.
- The log of the next try (2026-10-09) gave the cause. The renderer
  stopped at `DCHECK_EQ(box_fragment->Size(), Size())` in
  `FragmentItem::RecalcInkOverflow` (`fragment_item.cc:1049`), with an
  inline box's item 1440 by 1781.89 and its box fragment 720 by 1781.89.
  The recorded geometry is Blink's own layout units
  (`RectInContainerFragment`), which include the layout zoom factor:
  the screen's scale factor, here multiplied by the text size, times the
  browser zoom (`WebFrameWidgetImpl::SetZoomInternal`). The recording's
  page was laid out at a layout zoom of 2 at 200 percent text size, so
  the recreation, whose box was half the recorded size, was laid out at
  1. Why it was is not settled: in this checkout DevTools' emulated
  device scale factor is the one `ZoomFactorForViewportLayout` gives the
  layout zoom (`web_view_impl.h`), except on a page with a `text-scale`
  meta tag, where the text size is divided out, so the emulated factor of
  2 should have given 2. Either way, where the recreation's layout zoom
  differs from the recorded one, the recorded rectangles are in other
  units from the recreation's own layout: the fragment items hook gives an
  inline box's item its recorded size while its box keeps Blink's, which
  the check refuses. The fix, in "The recorded layout zoom" below, does
  not depend on how emulation sets the layout zoom, as the renderer is
  given the recorded one.

### The recorded layout zoom

Proposed and agreed 2026-10-09; built, not yet checked on the target
machine.

1. A recreation is laid out at the recorded layout zoom factor. The owner's
   recording of 2026-10-09 showed that the layout checkpoint is not enough:
   its only checkpoints of the page were before 7.3 s, at a layout zoom of
   2, while Windows' text size was set back to 100 percent at 19.0 s, after
   which every layout change set named a layout zoom of 1. So the layout
   zoom at the frame is that of the document's latest layout change set
   (`layoutZoomFactor` of `layout-changes-started`), or the checkpoint's at
   the frame's zoom when there is none. The builder writes it in the root
   attribute as `layoutZoom FACTOR`, also for a recording with no browser
   preferences, and in recreation mode `WebFrameWidgetImpl::SetZoomInternal`
   uses it in place of the layout zoom factor it works out, so Blink's
   layout units, font sizes, and glyph advances are those recorded. The
   browser zoom entry stays, for the zoom level the page reports.
2. The viewport. The screen's scale factor at the frame, with Windows' text
   size, is the frame's layout zoom over the browser zoom. Where it differs
   from the checkpoint's, the window is taken to have kept its size in
   screen pixels, as a text size change leaves it, so the emulated width
   and height are scaled by the checkpoint's factor over the frame's, and
   the device scale factor emulated is the frame's (`RecreationViewport`).
   For the recording above, a frame after 19.0 s is shown at 1864 by 818
   CSS pixels at a ratio of 1, where the checkpoint gave 932 by 409 at 2.
   That size is inferred, not recorded, and the evidence panel's notes say
   so. How DevTools' emulation combines with the imposed layout zoom is not
   yet measured, so the evidence panel's Viewport as shown section reads
   `innerWidth`, `innerHeight`, and `devicePixelRatio` from the page once it
   has painted and lists them beside the values the page is meant to have,
   saying when they differ by more than a CSS pixel or 0.001 of the ratio.
   The owner's check of cc61845 (2026-10-09): recreation works at frames
   during and after 200 percent text size, but a frame at 200 percent
   showed scroll bars the recording did not have, in a window of the right
   size. Taken to mean that the emulated device scale factor, 2, did not
   reach the recreation's renderer, on a screen at a factor of 1, so that
   the 932 pixel wide page, laid out at the imposed layout zoom of 2, was
   466 CSS pixels wide; the same would explain the layout zoom of 1 that
   stopped the renderer. From the next revision no device scale factor is
   emulated (`deviceScaleFactor` 0, which turns the override off): the
   blank tab's `devicePixelRatio` gives the viewing machine's own scale
   factor, and the page and window are sized at the recorded window's size
   in screen pixels, the CSS size times the layout zoom, over that factor
   (`RecreationViewport.WindowWidthAt`), 1864 by 818 for that frame. The
   renderer's imposed layout zoom then gives the recorded CSS size and
   `devicePixelRatio`, which the Viewport as shown section checks.
3. Whatever the zoom, an inline box's item keeps its box fragment's size,
   at the recorded offset, and the console says, for the block, that an
   inline box it lays out was recorded at another size, so a mismatch is
   reported, not a renderer crash. `integrate.py` upgrades a checkout that
   holds the earlier fragment items hook.

Revised with the owner on 2026-10-09, after the check of ae9f785 was set
aside: the frame is exactly its recorded size, and the page is the page.

1. The frame. The recreation's page area is the recorded frame's size in
   screen pixels, the latest layout checkpoint's CSS size times its
   `devicePixelRatio`, in the viewing machine's device-independent pixels
   at its own scale factor (the blank tab's `devicePixelRatio`). Windows'
   text size and the browser zoom do not change the window's size, only
   how the page inside it is laid out, so nothing else scales it.
2. Nothing is emulated: `Emulation.setDeviceMetricsOverride` is no longer
   sent, and the recorded browser zoom level is no longer applied. Its
   effect on the page is in the recorded layout zoom factor, which the
   renderer is still given, as the recorded geometry is in its units. The
   page's CSS size is the frame's screen size over that layout zoom, and
   its `devicePixelRatio` that layout zoom. `integrate.py` removes the zoom
   level hook from `WebFrameWidgetImpl::SetZoomInternal` in a checkout that
   holds it (`remove_blink_frame_widget_zoom`), and the builder no longer
   writes the `zoom` entry; the renderer still reads one, from older
   recreations, and `RecorderRecordedZoomLevel` stays defined, called by
   nothing. The evidence panel notes the recorded zoom, and that it is not
   applied as a zoom level.
3. A frame the screen cannot hold. It was proposed that the page then keep
   its recorded size and the window scroll. Without emulation the page's
   size is the window's, so a window the screen cannot hold gives a
   smaller page, which the Viewport as shown section reports as a
   difference. Keeping the recorded size there would need the size alone
   to be emulated; that is not built, and is for the owner to decide.
   Decided 2026-10-10: built as below, drawn smaller to fit.

The target machine script (`scripts/Test-RecreationPreferences.ps1`) now
expects, at each step, the latest layout change set's layout zoom as the
page's `devicePixelRatio` and the frame's screen size over it as its
`innerWidth` and `innerHeight`, which its Console line reads.

The owner reported on 2026-10-09 that with revision e3f7c8f the pages of
the recording of that day render correctly. At a frame before the text
size change, the evidence panel's Viewport as shown section gave 932 by
409 CSS pixels at a device pixel ratio of 2 and a layout zoom of 2, both
meant and shown, so the page's size and ratio are the recorded ones. At
a frame after it, the section gave 1864 by 818 CSS pixels at a device
pixel ratio of 1 and a layout zoom of 1, both meant and shown: the same
frame in screen pixels, laid out at the later text size.

Agreed with the owner on 2026-10-10, option A of the fallback proposed for
item 3 above: a frame the screen cannot hold is drawn smaller, whole.

1. When. The window is sized as before, then the blank tab's page area is
   read back until it holds the frame or has settled (five readings 100 ms
   apart that agree, or the last of 30). When it is a pixel or more short
   of the frame in either direction, the fallback is used; otherwise
   nothing is emulated (`RecreationControl.Fit`).
2. What. `Emulation.setDeviceMetricsOverride` with the frame's size in the
   viewing machine's device-independent pixels, no device scale factor
   (`deviceScaleFactor` 0, so the earlier problem of an emulated factor
   that did not reach the renderer does not arise), and
   `dontSetVisibleSize`, so the window's page view keeps its size. In the
   checkout, the renderer's widget then takes the emulated size as its own
   (`ScreenMetricsEmulator::Apply`), and without `dontSetVisibleSize` the
   page view would be resized to it and cut off by the window
   (`WebContentsImpl::SetDeviceEmulationSize`).
3. The scale. The page is drawn at the largest scale the page area holds,
   rounded down to 0.0001 and no less than 0.01, through the command's
   `scale`, which DevTools' device mode uses to fit a large device in its
   window. The layout is the recorded one; only the drawing is smaller.
   The scale is set for the window as the recreation opened it.
4. The panel. The recorder serves the fit as `window.json`
   (`RecreationWindowFit`), and the Viewport as shown section lists the
   page area, the frame's size, and the scale, and announces them, or says
   that the emulation was refused, with DevTools' answer, and that the page
   is then the window's size.

Not yet measured on the target machine: whether a mouse click lands where
it is drawn at a scale below 1, and whether resizing the window keeps the
emulated size. The check is a recording made with Chromium maximized,
whose window and its frame the screen cannot hold.

The owner's check of 92e3911 (2026-10-10) found that the fallback was not
reached, for a reason before it: the recorded viewport was out of date.
The recording (session 20261010-134925-48ca99c3ea4c4f609be438bda1525365)
holds one layout checkpoint of the page, at 2.09 s, with a viewport of 929
by 925 CSS pixels at a device pixel ratio of 1; the owner then maximized
the window, and the change set at 8.04 s laid the root element out 1905
CSS pixels wide, the 1920 pixel screen less the page's scroll bar. Change
sets recorded the layout zoom but not the viewport, so a frame after 8 s
was shown at 929 by 925, which the window held, with the recorded layout
1905 wide in it, so with scroll bars the recording did not have. The
Viewport as shown section reported no difference, as it compares the page
with the recorded viewport.

Agreed with the owner on 2026-10-10:

1. Each layout change set records the viewport and the device pixel ratio
   (`viewport` and `devicePixelRatio` in `layout-changes-started`, protocol
   0.58), read as the layout checkpoint reads them. A Chromium rebuild is
   needed; `integrate.py` upgrades a change set helper patched at 0.57.
2. The recreation is shown at the latest viewport at the frame, the
   checkpoint's or a change set's (`RecordedViewport.FromChangeSet`), and
   the evidence panel's notes say which.
3. A recording before 0.58 holds only the checkpoint's. When the page's
   root element was last laid out more than a CSS pixel wider than that
   viewport (`RecordedPage.RootLaidOutWidth`), the Viewport as shown
   section and the notes warn that the viewport may be out of date. The
   size is not inferred from the root: its width leaves out the scroll bar,
   and its height is the content's, not the window's. A root set wider
   than its window gives the same warning.
4. A change set is recorded only when something on the page lays out
   differently, so a resize that changes nothing in the layout, such as a
   taller window for a page whose layout does not depend on its height, is
   not followed until the next change set.

The target machine script expects the latest of the walk's and the change
sets' viewports at each step.

The owner's check of 444df8b (2026-10-10), a recording with the window
maximized after the page loaded: a frame before the maximize was shown at
929 by 925 CSS pixels, and one after at 1920 by 953, each at a device
pixel ratio of 1, meant and shown alike, so the recreation follows the
resize. The frame after it did not use the fallback: no line said the
screen could not hold it, so the window was given its whole page area of
1920 by 953. The owner reported that the whole page showed, with its
scroll bars, so the fallback was rightly not used: Windows let the window
be that large, its invisible borders off the screen. Resizing the
recreation window kept the page's recorded layout, and its scroll bars
reached the whole page.

Still not checked on the target machine: the fallback itself, drawn at a
scale below 1, and whether a click lands where it is drawn then. It is
reached only when Windows cannot give the window the frame's page area,
as when the recording's screen was larger than the viewing machine's.
- The script's first analysis (2026-10-09) stopped formatting a time:
  Windows PowerShell chose `Math.Max(int, int)` for a literal 0, which a
  recording's nanoseconds overflow. Both arguments are now `long`, and the
  analysis can be run again on exported events with `-EventsPath` alone.
- The first build in the owner's checkout (9971bbb, 2026-10-09) stopped
  in `recreation_preferences.h`: Chromium's `-Wunsafe-buffer-usage`
  refuses indexing a C array, which its percent-encoding did with a table
  of hexadecimal digits. The digits are now worked out instead. The header
  is now also checked with clang and `-Wunsafe-buffer-usage -Werror`
  before a package is made, which reproduced the two errors on the old
  header and passes on the new one.

### The check on the target machine

`scripts/Test-RecreationPreferences.ps1` runs the check "Required tests"
lists, in two runs. The first is a recording of the fixture page in which
the font size, Windows dark mode, animation effects, a contrast theme, and
the default zoom are each changed and put back; the script checks the
`color-maps-sent` records and writes `inspect.csv`, the player time to
inspect after each change with the recorded values there. The second, with
`-Recreation`, sets the viewing machine's dark mode and animation effects
to the opposite of each recorded value, and for each change asks the owner
to open the recreation at its time, paste a line into DevTools' Console,
which reads `matchMedia()` for the color scheme, forced colors, and
reduced motion, the default font size, the `Canvas` system color, and
`devicePixelRatio`, and to say whether the Styles pane shows the matching
`@media` rules and whether the scroll bars have the recorded colors.

## The browser theme

### The owner's finding

The owner's recording of 2026-10-10 (session
20261010-152204-f290f94ccb7e47bfaba601c5583d213a) changed the browser's
theme several times. The theme colors the browser's own window, its tabs
and toolbar, as well as what it gives the page, and the recreation
browser's window did not show the changes. The recording held only their
effect on the page: a `color-maps-sent` record for each open page at
27.4 s, 38.7 s, and 47.6 s, in each of which the light and dark maps'
`kColorMenuBackground`, `kColorMenuItemBackgroundSelected`, and
`kColorMenuSeparator` changed, to warm tints and back. Of the theme's
settings only the light, dark, or device mode, `colorScheme`, was listed,
and it stayed at the device's. The theme's color, its style, grayscale,
and an installed theme were not recorded, so they were not in the
Properties panel, and the timeline's color map records said only
"color-providers, light dark". A recreation's browser starts with a new
profile, so its window had the default theme.

### The design

Agreed with the owner on 2026-10-10, with the timeline addition:

1. Four browser preferences join the list at protocol 0.59, read and
   watched as the others are: `userColor` (`browser.theme.user_color2`),
   `colorVariant` (`browser.theme.color_variant2`), `grayscaleTheme`
   (`browser.theme.is_grayscale2`), and `themeId` (`extensions.theme.id`).
   They are named in `chrome/common/pref_names.h` and registered by
   `ThemeService::RegisterProfilePrefs`
   (`chrome/browser/themes/theme_service.cc`), in the owner's checkout,
   Chromium 156.0.8065.0. With `colorScheme`, already listed, they are the
   browser's theme. A snapshot before 0.59 does not hold them, so the
   validator and the database take them as optional there; their rows say
   "not recorded" for an older recording.
2. They are in the Properties panel's Browser group, with the change
   buttons and counts of the other rows, and their changes are on the
   timeline. The theme color is an SkColor kept in an integer preference,
   shown as `#RRGGBB`, and transparent, its default, as no color chosen.
   The color style is named from `ui::mojom::BrowserColorVariant`
   (`ui/base/mojom/themes.mojom`): system, tonal spot, neutral, vibrant,
   expressive.
3. The timeline addition: a later `color-maps-sent` record names, for
   each map it holds, the colors that differ from the map last sent to the
   view (`changedColors`, protocol 0.59), so its summary says how many
   changed, in which maps, and which, such as "3 colors changed in light
   and dark: menu background, menu item background selected, menu
   separator". The timeline reads records out of order, so the record
   itself names the changes, which the bridge already compares to choose
   the maps it records. A record before 0.59 is summarized as before.
4. A recreation writes the theme at the frame into the recreation
   browser's new profile before it starts, as `browser.theme.color_scheme2`,
   `user_color2`, `color_variant2`, and `is_grayscale2`, so its window is
   drawn as the participant's was. Chromium's one-time copy of the older
   synced theme preferences (`MigrateSyncingThemePrefsToNonSyncingIfNeeded`,
   `chrome/browser/themes/theme_syncable_service.cc`) copies only values a
   profile has set, so it leaves the written values in place.
5. An installed theme, from the Chrome Web Store, cannot be installed in
   the recreation, which has no network access. (Revised the same day, in
   "The owner's check of 7ca9900": `user_color_theme_id` is the identity
   of the theme color, not of an installed theme.) Its identity is recorded,
   and the recreation leaves the theme color, its style, and grayscale at
   their defaults, as the participant's window did not show them, so it
   draws the browser's default theme with the recorded color mode, and the
   evidence panel says so.
6. The evidence panel lists the theme values given, with the time of each
   record, names the theme settings a recording did not hold, and notes
   that with the color mode "system" the recreation's window follows the
   viewing machine's Windows light or dark mode, which may not be the
   participant's.

The theme is set as the recreation opens, so it is the frame's, as every
recreation is of one frame.

### As built

Protocol 0.59: `kRecorderBrowserPreferences` in `profile_impl.cc` gains the
four preferences, and `integrate.py` extends a list patched before 0.59 in
place. `RecordBrowserColorMapsSent` (`browser_bridge.cc`) adds
`changedColors` to a later record. The recorder's list,
`BrowserPreferenceSettings.Browser`, marks the four as added in 0.59
(`AddedIn`), and `0025_browser_theme.sql` adds their columns and the
changed colors' tables. `BrowserPreferenceTimeline.ThemeAt` gives a
recreation the theme at the frame, `RecreationBrowser.ThemePreferences`
writes it into the profile, and `RecordedPreferences.ThemeNotes` writes the
evidence panel's notes.

### The owner's check of 7ca9900

The owner's recording of 2026-10-10 (session
20261010-180245-2a609db02ff243cfa5b0110c2050b465) recorded the theme: a
theme color chosen at 35.0 s, `userColor` -806210 (`#F3B2BE`) with the
neutral style, and chosen again at 46.2 s with the tonal spot style, each
with a `color-maps-sent` record naming the menu colors that changed. The
recreation's window stayed at the default theme, its toolbar black, in the
dark mode of the viewing machine's Windows, which the recorded "system"
mode follows. The cause: choosing a color sets `extensions.theme.id` to
`user_color_theme_id` (`ThemeService::kUserColorThemeID`,
`chrome/browser/themes/theme_service.cc`, line 288), which the recreation
took for an installed theme, so it left the color out. And
`ThemeService::InitFromPrefs` (line 957) draws the theme color only when
the theme in use has that identity, so the color alone would not have been
drawn either.

Fixed the same day: with `user_color_theme_id` recorded, the recreation
writes that identity into the profile with the color, its style, and
grayscale. With the default theme's identity, empty, it writes the mode and
grayscale, as the default theme draws no color. `autogenerated_theme_id`
(`kAutogeneratedThemeID`, line 287), a color theme built from
`autogenerated.theme.color`, which is not recorded, is drawn as the default
theme with the recorded mode, and the evidence panel says so, as for an
installed theme. The Properties row is renamed "Browser theme in use", and
shows "default theme", "theme color", "generated color theme", or the
installed theme's identity.

Not yet checked on the target machine: that a recreation at a frame after
a theme color is chosen draws the browser's window with it.

## Decisions

Agreed 2026-10-10, the browser theme (see "The browser theme"): the four
theme preferences are recorded at protocol 0.59 and shown in the
Properties panel and the timeline; a later color map record names its
changed colors; a recreation gives the recreation browser's window the
recorded theme, and with an installed theme, the default theme with the
recorded color mode, saying so.

Agreed 2026-10-08, stage 3 (see "Stage 3: the recreation"):

1. The color maps are recorded, as `color-maps-sent` at protocol 0.57.
2. The recorded values are applied in the instrumented Chromium's
   recreation mode, in the renderer, not by DevTools' emulation.
3. They are carried in the recreated document's root element, in its
   `data-a11y-recorded-preferences` attribute.

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
   that instant. Revised 2026-10-09: the zoom level is applied through its
   effect on layout, the recorded layout zoom factor, not as a zoom level
   ("The recorded layout zoom"). Applying them is a change to
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

Added 2026-10-08 with the properties panel, proposed: unit tests of the
values in effect at a time, from a start record and changes, including a
recording with none; and a check on the target machine that the panel's
values follow playback, its toggle and splitter work by keyboard, and a
screen reader reads each row.

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

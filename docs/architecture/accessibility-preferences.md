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
target machine (see "Visible focus and stepping as built"); stages 2 and 3 are not built.
The owner checked the stepping keys on the target machine 2026-10-08, and
change buttons and counts on the panel's rows were agreed and built the
same day, not yet tested on the target machine (see "Change buttons and
counts"). Listed in
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


## Change buttons and counts

Proposed by the owner 2026-10-08 after checking 7ae9f19 on the target
machine, refined and agreed the same day; built 2026-10-08, not yet
tested on the target machine.

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

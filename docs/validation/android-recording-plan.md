# Recording an Android device: validation plan

Written 2026-11-08 for slice 0 of
[the Android implementation plan](../architecture/android-implementation-plan.md),
the tools, with tests 5 to 9 added the same day for slice 1, the phone
found and checked, and tests 10 to 14 for slice 2, the companion app and
the phone clock. Later slices add their tests here. Each test checks one
behaviour in at most five steps, with what to see on the screen.

What was checked before this plan, in the Linux build environment:

- The solution builds with no warnings, and the 9 tests of the Android
  tools pass: Platform-Tools unpacked, an earlier install replaced, another
  revision refused with the earlier install kept, files outside the
  archive's `platform-tools` folder refused, an archive without `adb.exe`
  refused, and the status of Platform-Tools, the scrcpy server and the
  companion app.
- `scripts/Build-CompanionApp.ps1` built the companion app under
  PowerShell 7 with the Linux JDK and Build-Tools. `aapt2 dump badging`
  shows package `io.github.bobdodd.a11yrecorder.companion`, minimum SDK 30,
  target SDK 37 and an accessibility service; `apksigner verify` and
  `zipalign -c` passed. Answering anything but `yes` at the licence
  question stopped the build.
- The real `platform-tools_r37.0.1-win.zip` matches the SHA-256 in the
  recorder, has all 15 entries inside `platform-tools/`, has the four files
  the installer requires, and says `Pkg.Revision=37.0.1`.

None of that shows the build under Windows PowerShell 5.1, the Android
setup window, adb running on Windows, or the app installing on the
Pixel. Tests 1 to 4 do.

## Before the tests

On the development PC, as for every installer build: the instrumented
Chromium build, the .NET 10 SDK and Inno Setup. Every PowerShell window is
a normal one, not "Run as administrator". The Pixel is needed only for
test 4.

## Test 1: build the installer (development PC)

1. Open PowerShell and allow scripts in this window:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   ```

2. Get `main`:

   ```powershell
   $repo = Join-Path $env:USERPROFILE "windows-a11y-recorder"
   git -C $repo checkout main
   git -C $repo pull --ff-only
   ```

3. Build the installer (the Android files add about 340 MB of downloads
   the first time):

   ```powershell
   & (Join-Path $repo "scripts\Build-Installer.ps1")
   ```

4. When it asks "Type yes to accept it and continue", read the licence at
   the address it gives, then type `yes` and press Enter.
5. Check the end of the output.

**On screen:** under `== Android files`, the line `Bundled scrcpy server
5.0.1.`, then `COMPANION APP BUILT` with the app's path, `1.1.0, version
code` and a number, its SHA-256, and `Signing certificate SHA-256` with a
64-character value. The first build also shows `Making the companion app's
signing key in` with a folder ending `Windows A11y Recorder
Build\companion-signing`. The script finishes with `INSTALLER BUILT`. Any
red error text means it failed; send the whole output.

## Test 2: what the installer brings for Android

1. Run the new installer from `artifacts\installer\output` and go through
   its pages to Finish. Installing over the earlier install keeps the
   recordings, settings and models.
2. In the recorder, open the "Android" menu (Alt, then the arrow keys to
   it).
3. Choose "Set up Android recording...".
4. Read the window.

**On screen:** the window "Set up Android recording". Under "Android SDK
Platform-Tools": "Platform-Tools is not installed. It will be installed in
C:\Users\User\AppData\Local\Windows A11y Recorder\android\platform-tools."
The "Download and install Platform-Tools 37.0.1" button is unavailable.
Under "Included with the recorder": "The scrcpy server 5.0.1, for the
phone's screen and sound, is in place and checked:" and "The companion app
1.1.0, version code ..., for the phone's accessibility evidence, is in
place and checked:", each with a path in
`...\AppData\Local\Programs\Windows A11y Recorder\android\`.

## Test 3: download Platform-Tools

In the window from test 2:

1. Choose "Open the licence in the browser", and read it.
2. Check "I have read and accept the Android Software Development Kit
   License Agreement".
3. Choose "Download and install Platform-Tools 37.0.1".
4. Wait for the status line to stop changing.
5. Read the window.

**On screen:** in step 1, the browser opens
developer.android.com/studio/terms. After step 2 the download button is
available. At the end, the status line and the Platform-Tools line both
read "Platform-Tools 37.0.1 installed in
C:\Users\User\AppData\Local\Windows A11y Recorder\android\platform-tools.",
and the line under it begins "adb runs on this PC and reports: Android
Debug Bridge version". Any other status line is a failure; send it.

## Test 4: the companion app installs on the Pixel

The recorder installs the app itself from slice 2. This test only shows
the app the build makes is one Android accepts.

1. Connect the Pixel by USB, unlocked, with USB debugging on.
2. In PowerShell, install the app with the downloaded adb:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   $adb = Join-Path $env:LOCALAPPDATA "Windows A11y Recorder\android\platform-tools\adb.exe"
   $apk = Join-Path $env:LOCALAPPDATA "Programs\Windows A11y Recorder\android\a11yrecorder-companion.apk"
   & $adb devices
   & $adb install -r $apk
   ```

3. On the Pixel, open Settings, then Accessibility.
4. Find "A11y Recorder companion" in the list of apps, and open it. Do
   not turn it on.

**On screen:** `adb devices` lists `58191FDCQ00ABZ` followed by `device`,
and `adb install` ends with `Success`. If the Pixel asks to allow USB
debugging from this PC, allow it and run the two `& $adb` lines again. On
the Pixel, the companion's page shows the description "Records
accessibility evidence for Windows A11y Recorder while a recording runs
over USB. It does nothing on the phone and sends nothing over the
internet." with its switch off.

## Results

- 2026-11-08, development PC: test 1 passed (installer
  `WindowsA11yRecorder-Setup-2f1f913.exe` built, with the `android` folder
  holding the companion app, its description, the scrcpy server and its
  licence). Tests 2 and 3 passed: the owner found the Android menu and
  Platform-Tools downloaded and installed. Test 4 passed: the companion
  app installed on the Pixel and "A11y Recorder companion" appeared in
  Settings, Accessibility, left off.
- Found during test 2: the menu bar is easy to miss. The owner looked for
  the Android menu in the side panel's sections before finding the thin
  strip at the top of the window. Making the menu bar much more visible is
  a later change, not part of the Android work. The menu's access key,
  Alt+D, is also used by "Desktop frames"; it gets a letter of its own in
  slice 1.

## Part 1: the phone, found and checked

What was checked before these tests, in the Linux build environment: the
solution builds with no warnings, and 15 new tests of the checks pass
against a fake adb that answers with what the Pixel printed in the
feasibility checks: a connected phone with TalkBack on and off, another
accessibility service on its own, a changed screen resolution, no phone,
a phone not yet allowed, an offline phone, two devices, no adb, another
program's adb server of another version and of the same version, the
recorder's own server already running, and adb's version reply. None of
that shows the panel on Windows, a real adb server, or the real phone.
Tests 5 to 9 do.

### Test 5: build and install the new version (development PC)

1. Close the recorder. Open PowerShell and run:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   $repo = Join-Path $env:USERPROFILE "windows-a11y-recorder"
   git -C $repo pull --ff-only
   & (Join-Path $repo "scripts\Build-Installer.ps1")
   ```

2. Run the newest installer in `artifacts\installer\output` and go
   through its pages. On the last page, uncheck "Start the recorder and
   set up the analysis models", then choose Finish.
3. Start the recorder from the Start menu.
4. Read the top of the side panel.

**On screen:** the build ends with `INSTALLER BUILT`, and the licence
question does not come back, as it was accepted in test 1. In the
recorder, above "Capture channels", a group "Recording target" with two
choices, "This PC's desktop" (chosen) and "Android device".

### Test 6: the phone's checks

1. Connect the Pixel by USB, unlocked, with TalkBack off.
2. In "Recording target", choose "Android device".
3. Wait 5 seconds, then read the side panel.
4. On the Pixel, turn on TalkBack (Settings, Accessibility, TalkBack).
5. Wait 5 seconds, then read the side panel again.

**On screen:** "Capture channels" and "Instrumented Chromium" are gone,
and "Android device checks" shows:

- "adb: ready, version 1.0.41 (Platform-Tools 37.0.1)."
- "adb server: the recorder's own."
- "Phone: Pixel 10 Pro XL, connected and allowed (serial
  58191FDCQ00ABZ)."
- "System: Android 17, build CP3A.260905.009, security patch
  2026-09-05." (later values if the phone has updated)
- "Screen: 1080 by 2404 pixels."
- "TalkBack: off.", with nothing after it on that line and no "Display
  speech output" line, as TalkBack off is not a problem.

After step 5, "TalkBack: on." and under it "Display speech output: the
recorder cannot read this setting." with where to turn it on.

Under them, "Recording the phone is not built yet; for now this panel
checks it." (From part 2 the panel also has a "Companion app" line and
another note under the checks; test 10 checks them.) If "adb server" says another program's server is running,
note which program and send the line.

### Test 7: the phone unplugged and plugged in again

With the panel from test 6 showing:

1. Unplug the USB cable from the phone.
2. Within 5 seconds, read the panel.
3. Plug the cable in again and unlock the phone.
4. Within 5 seconds, read the panel.

**On screen:** after step 1, "Phone: not found." followed by "Step 1" to
"Step 4": USB debugging on, the cable carrying data, "File transfer"
under "Use USB for", and "Allow USB debugging?" accepted. The lines for
the system, screen and TalkBack are gone. After step 3, the lines of test
6 are back.

### Test 8: the menu's access key

1. Choose "This PC's desktop" in "Recording target".
2. Press Alt+D.
3. Press Escape twice, then press Alt+T.

**On screen:** after step 2 the Android menu is open, showing "Set up
Android recording...". After step 3 the menu is closed and "Desktop
frames" in "Capture channels" has changed between checked and unchecked;
its letter "t" is underlined while Alt is held. Press Alt+T again to put
it back.

### Test 9: another program's adb server

The adb that came with scrcpy for the feasibility checks is the same
version as the recorder's, so the recorder uses its server rather than
stopping it.

1. Choose "This PC's desktop" in "Recording target".
2. In PowerShell, stop the recorder's adb server and start scrcpy's:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   $repo = Join-Path $env:USERPROFILE "windows-a11y-recorder"
   & (Join-Path $env:LOCALAPPDATA "Windows A11y Recorder\android\platform-tools\adb.exe") kill-server
   & (Join-Path $repo "artifacts\android-checks\tools\scrcpy-win64-v5.0.1\adb.exe") start-server
   ```

3. In the recorder, choose "Android device" and wait 5 seconds.
4. Read the "adb server" line.

**On screen:** in step 2, "* daemon started successfully". In the panel,
"adb server: another program's adb server is running (from
C:\Users\User\windows-a11y-recorder\artifacts\android-checks\tools\scrcpy-win64-v5.0.1\adb.exe).
It is the same version as the recorder's, so the recorder uses it and
leaves it running.", and the phone's lines as in test 6. The case of a
server of another version, which the recorder leaves alone, is covered
only by the tests in the build environment, as no adb of another version
is on the PC.
- 2026-11-08, part 1: test 5 passed; the recorder showed "Recording
  target" with both choices. In test 6 every line was as expected except
  "TalkBack: off. Turn it on in Settings, Accessibility, TalkBack.",
  which was right, as TalkBack was off. The owner does not always use
  TalkBack: magnification, visual layout and colour are tested without
  it. So TalkBack off is now reported without asking for it to be turned
  on, the "Display speech output" reminder shows only with TalkBack on,
  and test 6 checks both. The "adb server" line found the server started
  by scrcpy's adb for the feasibility checks, of the same version, and
  used it, which is what test 9 sets up; test 9 is not needed again.
- 2026-11-08, part 1: test 7 passed. Unplugged, the panel changed to
  "Phone: not found." with the steps; plugged in again, the phone's
  lines came back. TalkBack, turned on while the phone was unplugged,
  showed as on once it was back.
- 2026-11-08, part 1, installer built from `e580566`: test 6 passed. The
  panel followed TalkBack being turned off and on, and with TalkBack on
  showed "TalkBack: on." with the "Display speech output" reminder under
  it. Test 8 passed: Alt+D opened the Android menu, and Alt+T checked and
  unchecked "Desktop frames". All of part 1's tests have passed.

## Part 2: the companion app and the phone clock

What was checked before these tests, in the Linux build environment: the
solution builds with no warnings; `scripts/Build-CompanionApp.ps1` built
the companion app, version 1.1.0, with Java's warnings treated as errors;
and 26 new tests pass. They cover the clock fit (an offset with drift,
the phone sleeping, a slow answer that is not a discontinuity, the
uncertainty between samples), the messages of protocol 1, a connection
to a fake companion app over a local socket (the greeting, the first
display and a clock answer; nothing listening; a greeting of another
protocol), the `adb forward` and its removal, installing and turning on with TalkBack kept, an app signed with
another key, the panel's companion line in each state, and a recording
with the app answering, with the app not reachable, and with the
connection lost part way. None of that shows the app on Android 17, the
real socket and `adb forward`, the phone's clock, or the panel on
Windows. Tests 10 to 14 do.

The Pixel already has the companion app from test 4, made by an older
build and left off, so the recorder offers to update it.

### Test 10: build and install the new version (development PC)

1. Close the recorder. Open PowerShell and run:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   $repo = Join-Path $env:USERPROFILE "windows-a11y-recorder"
   git -C $repo pull --ff-only
   & (Join-Path $repo "scripts\Build-Installer.ps1")
   ```

2. Run the newest installer in `artifacts\installer\output` and go
   through its pages. On the last page, uncheck "Start the recorder and
   set up the analysis models", then choose Finish.
3. Connect the Pixel by USB, unlocked, with TalkBack on or off.
4. Start the recorder from the Start menu, and in "Recording target"
   choose "Android device".
5. Wait 5 seconds, then read "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT` and, before that,
`COMPANION APP BUILT` with `1.1.0, version code` and a number. In the
panel, the lines of test 6, and a line "Companion
app: version code" with a number, "is installed, older than the
recorder's" with a larger number, and "Choose "Install companion app" to
update it." Under the checks, a button "Install companion app", and the
note "A recording of the phone records the companion app's connection,
the phone's clock and its display's rotation and size. The phone's
screen, sound, touches and accessibility evidence come in later parts."

### Test 11: install and turn on the companion app

With the panel from test 10 showing, and TalkBack as it was there:

1. Choose "Install companion app".
2. Wait until the text under the button changes from "Installing the
   companion app on the phone." to a result, about 10 seconds.
3. Wait 5 seconds more, then read "Android device checks".
4. On the Pixel, open Settings, then Accessibility.

**On screen:** under the button, "The companion app is installed and
on." The button is gone, and the panel shows "Companion app: installed,
on, and answering (version 1.1.0, version code" with the larger number
from test 10 ", protocol 1)." The TalkBack line is as it was in test
10. On the Pixel, "A11y Recorder companion" shows as on, and TalkBack as
it was. If the
result is anything else, or the phone shows a message about the
companion app, send its words; this is where the plan's risk about
Android's restricted settings is settled.

### Test 12: a recording of 30 seconds

With the panel from test 11 showing:

1. Choose "Start recording".
2. Wait 30 seconds.
3. Read "Collector status" under "Current session".
4. Press on the start of the "android.companion" line, drag to its end,
   and press Ctrl+C.
5. Choose "Stop recording".

**On screen:** in step 3 "Collector status" has one line, wrapped to
the width of the side panel with no scroll bar, starting
"android.companion: Running, Healthy." and going on "Companion app
1.1.0:" with a number of clock samples, about 120 (four a second),
"largest error bound" with a number of milliseconds, and "screen rotated
0 degrees, 1080 by 2404." No desktop or browser lines. In step 4 the
line's text is highlighted as it is dragged over, and stays highlighted
while the recording goes on; paste it into the reply. Send the line, as
its error bound is the first measure of how closely the phone's clock is
placed on the recording's.

### Test 13: the phone rotated during a recording

1. On the Pixel, turn on Auto-rotate (swipe down twice from the top of
   the screen), and open Chrome.
2. In the recorder, choose "Start recording".
3. Turn the phone on its side, wait 5 seconds, and read the
   "android.companion" line under "Current session".
4. Turn the phone upright, wait 5 seconds, and read the line again.
5. Choose "Stop recording".

**On screen:** in step 3 the line ends "screen rotated 90 degrees, 1080
by 2404." or "screen rotated 270 degrees, 1080 by 2404.", depending on
which way the phone was turned. In step 4 it ends "screen rotated 0
degrees, 1080 by 2404." If the size in step 3 reads "2404 by 1080", send
the line: the plan expects the size not to change with rotation, but has
not checked it.

### Test 14: the cable unplugged during a recording

1. In the recorder, choose "Start recording", and wait 10 seconds.
2. Unplug the USB cable from the phone.
3. Within 5 seconds, read the "android.companion" line under "Current
   session".
4. Choose "Stop recording".
5. Plug the cable in again and unlock the phone.

**On screen:** in step 3 the line starts "android.companion: Running,
Degraded. The connection to the phone's companion app ended during the
recording:" followed by the reason, then "The recording goes on without
the phone's clock." and "Companion app 1.1.0:" with the clock samples
taken before the cable was unplugged; that number no longer goes up.
"Stop recording" ends the recording as usual. After step 5, within 10
seconds, the panel shows "Companion app: installed, on, and answering"
again.

### Results of part 2

- 2026-11-08, installer built from `4174f35`: test 10 passed; the panel
  offered to update the companion app installed in test 4. Test 11
  passed with TalkBack off: "Install companion app" installed the app
  and turned it on, the panel then showed "installed, on, and answering
  (version 1.1.0, version code 686, protocol 1)", and the Pixel's
  Accessibility settings showed the companion on. So Android 17 accepts
  `settings put secure enabled_accessibility_services` for an app
  installed with `adb install`, and the plan's restricted settings risk
  does not arise for the recorder's way of turning it on. Keeping
  TalkBack on while the companion is turned on is checked only by the
  build environment's tests; the owner turns TalkBack on only for
  screen reader tests, so it is checked on the Pixel the first time one
  is recorded.
- Found during test 12: the collector status was a separate list, with
  a horizontal scroll bar, whose lines could not be selected or copied,
  against the owner's rules that all text can be (player-layout.md,
  "Selecting and copying text") and that there are no horizontal scroll
  bars unless they cannot be avoided. It is now wrapping text under
  "Collector status:", like the lines above it, and is not refreshed
  while some of it is selected, so a selection made during a recording
  stays. Step 4 of test
  12 checks it.
- 2026-11-08, test 12 passed, judged from the recording itself, as the
  line could not yet be copied and had gone once the recording stopped.
  The recording (`20261109-015157-ebdd361f964e4632b5ce5134c1e9b5f3`,
  90.8 seconds) has one collector, `android.companion`, Healthy, and
  366 events: the companion's greeting (version 1.1.0, version code 686,
  protocol 1, Android 17, SDK 37), one display state (rotation 0, 1080
  by 2404), 362 clock samples and the two lifecycle records. The samples
  came every 0.246 seconds (median; at most 0.263), all in one segment
  with no discontinuity. Error bounds: median 2.4 ms, 95% under 5.1 ms,
  largest 18.7 ms. Round trips: median 5.0 ms, largest 37.5 ms; the
  phone held each request for 0.15 ms (median). A line fitted to all
  the samples, weighted as the recorder weights them, passes within
  every sample's own error bound, and has the phone's clock running 36
  parts per million faster than the PC's, about 3.3 ms over the
  recording. Which of the two clocks is off is not known.
- 2026-11-08, installer built from `d2c7952`: the collector status
  wrapped as text, and its line was selected and copied during a
  recording: "android.companion: Running, Healthy. Companion app 1.1.0:
  279 clock samples, largest error bound 29.1 ms, screen rotated 0
  degrees, 1080 by 2404." That recording (91.3 seconds) has 366 clock
  samples in one segment, median error bound 2.4 ms, largest 29.1 ms,
  and no rotation, as the phone was not turned in it.
- 2026-11-08, test 13 passed. The line followed the phone as it was
  turned in Chrome. The recording
  (`20261109-022011-9ed2af3da77045c996838c2acc2cadd8`, 22.8 seconds of
  clock samples) has the initial display state and five changes:
  rotation 270 at 7.6 s, 0 at 13.6 s, 90 at 16.3 s, 0 at 19.4 s and 270
  at 20.6 s, each 1080 by 2404, each placed by the clock fit with an
  uncertainty of 1.8 to 7.7 ms. So the size does not follow rotation, as
  the plan expected.
- 2026-11-08, test 14 passed. With the cable unplugged, the line showed
  the phone as disconnected. The recording
  (`20261109-022229-b8e37a794389454dbc750d9e7e74710c`, 37.7 seconds,
  completed) has 26 clock samples up to 6.3 s, then at 6.5 s
  `companion-unavailable` with `connectionLost` true, and the
  collector is recorded as Degraded. Its reason was "The companion app
  closed the connection.", which was wrong: the cable was unplugged.
  Through `adb forward` the recorder cannot tell an unplugged cable from
  adb stopping or the app being turned off, so the reason now names all
  three. Step 5, the companion line after plugging in again, was not
  reported, so its return is not checked on the Pixel. All of part 2's
  other checks have passed.

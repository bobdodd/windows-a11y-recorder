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
  three. The line copied after the cable was unplugged ended "Companion app
  1.1.0: 26 clock samples, largest error bound 6.1 ms, screen rotated 0
  degrees, 1080 by 2404.", matching the recording. In step 5 the
  companion line came back after the cable was plugged in again. All of
  part 2's tests have passed.

## Part 3, step 3a: the phone's screen stored

Part 3 is built in three steps (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 3"). Step 3a records the phone's screen as the H.264 stream the
phone sends, `android\screen.h264` in the recording's folder, with an
event for each packet. The player does not show it yet; that is step
3c. The phone's sound is not recorded yet.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and 24 new tests pass. They cover
the codec id, session and packet headers read from bytes written out by
hand from scrcpy 5.0.1's source; a server that is missing, that stops
before it connects, or that reports a disabled or failed stream; the
order of the adb commands (push, reverse, start, reverse removed); and a
recording with packets placed by the phone's clock, with a rotation,
with no phone clock, with the stream ending part way, and with the
server not starting. None of that shows the server on Android 17, the
real `adb reverse`, the phone's packet times on the companion's clock,
or the stream's size and rate. Tests 15 to 19 do.

### Test 15: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked.
4. Start the recorder from the Start menu, and in "Recording target"
   choose "Android device".
5. Wait 5 seconds, then read the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. The panel shows
"Companion app: installed, on, and answering (version 1.1.0", as in
test 11. The note reads "A recording of the phone records the companion
app's connection, the phone's clock, its display's rotation and size,
and the phone's screen as a video stream. The phone's screen is not yet
shown in the player."

### Test 16: a recording of 30 seconds with the phone's screen

With the panel from test 15 showing, and the phone unlocked:

1. Choose "Start recording".
2. Wait 30 seconds.
3. Read "Collector status" under "Current session".
4. Press on the start of the "android.screen" line, drag to its end,
   and press Ctrl+C.
5. Choose "Stop recording".

**On screen:** in step 3 "Collector status" has two lines: the
"android.companion" line of test 12, and a line starting
"android.screen: Running, Healthy. Phone screen:" with a number of
packets, a number of MB, a size, and "median delay from phone to
recorder" with a number of milliseconds. Paste the line into the reply.
The size is expected to be "1080 by 2404". scrcpy rounds the size down
to a multiple the phone's encoder needs, which is not known for the
Pixel, so a size a few pixels smaller is not a failure, but is
recorded. The median delay is the first measure of whether the phone's
packet times are on the companion's clock: a few tens of milliseconds
says they are; a number below zero, or of seconds or more, says they
are not. If the line ends "placed by the time each packet arrived, as
there is no phone clock", or says Degraded, send it as it is.

### Test 17: the stream in the recording's folder

1. Open File Explorer, and go to Documents, then "Windows A11y
   Recorder".
2. Open the newest folder, the recording of test 16.
3. Open the folder `android`.

**On screen:** two files, `screen.h264`, with a size close to the MB of
the line sent in test 16, and `scrcpy-server.log`.

### Test 18: the phone rotated during a recording

1. On the Pixel, turn on Auto-rotate (swipe down twice from the top of
   the screen), and open Chrome.
2. In the recorder, choose "Start recording", and wait 5 seconds.
3. Turn the phone on its side, wait 5 seconds, and read the
   "android.screen" line under "Current session".
4. Choose "Stop recording".
5. Turn the phone upright.

**On screen:** in step 3 the line has "2 capture sessions, now" and the
size of test 16 with its two numbers swapped, for example "2404 by
1080". If the size reads otherwise, send the line.

### Test 19: the cable unplugged during a recording

1. In the recorder, choose "Start recording", and wait 10 seconds.
2. Unplug the USB cable from the phone.
3. Within 5 seconds, read the "android.screen" line under "Current
   session".
4. Choose "Stop recording".
5. Plug the cable in again and unlock the phone.

**On screen:** in step 3 the line starts "android.screen: Running,
Degraded. The phone's screen stopped being recorded during the
recording. The screen stream ended:" followed by the reason, then "The
recording goes on without it." and "Phone screen:" with the packets
recorded before the cable was unplugged; that number no longer goes up.
The "android.companion" line is Degraded too, as in test 14. "Stop
recording" ends the recording as usual.

### Results of part 3, step 3a

- 2026-11-09, installer built from `a05bb8a`: test 15 passed, after
  "Install companion app" updated the companion from version code 690
  to 695. The version code goes up with every build of the installer,
  so the panel offers the update after each one, even when the app has
  not changed.
- 2026-11-09, test 16 passed, read from a screenshot: "android.screen:
  Running, Healthy. Phone screen: 114 packets, 1.7 MB, 1080 by 2404,
  median delay from phone to recorder 36.0 ms." The owner could not
  copy the line, as it changes while it is being selected; that is
  followed up separately. The recording
  (`20261109-135903-c2b69908eabf4ad6a2c1fbd6fb45bd76`, 113.5 seconds,
  completed) has one `video-stream`, one `video-session` (1080 by 2404,
  so scrcpy did not round the size down) and 125 `video-packet` events:
  one configuration packet and 124 pictures, the first a key frame.
  The packets' byte offsets run without a gap to the end of
  `android/screen.h264` (1,864,028 bytes), and its SHA-256 in the
  manifest matches the file. ffmpeg decoded all 124 pictures with no
  error: H.264 High profile, 1080 by 2404, limited range, BT.709. The
  pictures show the phone's home screen, its clock going from 8:59 to
  9:00 as the recording did.
- The delay from a packet's mapped phone time to the time it arrived
  was 15.6 to 68.6 ms, median 36.9 ms, with the clock fit's
  uncertainty 2.0 ms (median, largest 20.0 ms). A delay that small and
  never below zero supports the inference that the packets' times are
  on the companion's CLOCK_MONOTONIC; it does not prove it.
- The phone sends far fewer packets than expected. After a change it
  repeated the picture every 100 ms for about a second, then sent
  nothing while the screen stayed still: gaps of 44.3, 28.8 and 15.3
  seconds. scrcpy asks the encoder to repeat after 100 ms; that the
  Pixel's encoder stops repeating is seen here, not documented. A gap
  in the packets is therefore a screen that did not change, not lost
  evidence, and the player in step 3c shows the last picture until the
  next one.
- Test 17 was checked from the recording's folder on the development
  PC: `android` holds `screen.h264` (1.86 MB) and `scrcpy-server.log`,
  whose one line is "[server] INFO: Device: [Google] google Pixel 10
  Pro XL (Android 17)".
- 2026-11-09, test 18 passed, read from a screenshot: "android.screen:
  Running, Healthy. Phone screen: 226 packets, 1.7 MB, 2 capture
  sessions, now 2404 by 1080, median delay from phone to recorder
  25.6 ms." The recording
  (`20261109-140901-503a19342a294848894f51c30c01dbf7`, completed) has
  the companion's rotation to 270 degrees at 8.898 s and the stream's
  second capture session, 2404 by 1080, at 9.066 s, 168 ms later, each
  session beginning with a configuration packet and a key frame. The
  byte offsets run without a gap to the end of `android/screen.h264`
  (1,673,449 bytes), whose SHA-256 matches the manifest. ffmpeg decoded
  all 235 pictures with no error, 13 at 1080 by 2404 and 222 at 2404 by
  1080. Delay from phone to recorder: median 26.2 ms, 14.8 to 83.0 ms,
  none below zero; no packet time had to be held back.
- 2026-11-09, test 19 passed. With the cable unplugged both lines
  showed Degraded, and this time they could be copied. The recording
  (`20261109-141258-21c9169603174fe4b1f24224fb836f0e`, completed, both
  collectors Degraded) has `companion-unavailable` at 12.807 s and
  `video-unavailable` at 12.808 s, both with `connectionLost` true. Its
  12 packets end at 2.1 s, as the screen did not change after that; they
  run without a gap to the end of `android/screen.h264` (328,619 bytes,
  SHA-256 matching the manifest), and ffmpeg decoded its 11 pictures
  with no error. Two parts of the screen's reason were wrong. "the
  phone's server closed the stream" named one cause where, as for the
  companion in test 14, adb cannot tell an unplugged cable from adb or
  the server stopping; it now names all three. And "The server said:"
  quoted the server's greeting, an INFO line naming the device, which
  explains nothing; a reason now quotes only the server's warnings,
  errors and lines with no level, while the log keeps every line.
- 2026-11-09, the owner: copying the collector status fails while its
  text is changing during a recording. The owner wants the way text is
  copied in the recorder replaced later, so it is left as it is for now,
  and tests read changing lines from the screen or a screenshot.

## Part 3, step 3b: the phone's screen as pictures

Step 3b decodes the phone's stream while it is recorded and writes each
picture that differs from the one before as a PNG file in
`frames\android` in the recording's folder, with a record for each. The
player does not show them yet; that is step 3c.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and 23 new tests pass. A 24th,
of Windows' decoder against a stream the phone's size, runs only on
Windows and has not been run yet. The 23 cover the colour conversion (white, black, grey, and BT.709's red, green and
blue in limited range; BT.601 in full range), cutting the decoder's
padded picture to the session's size, PNG files read back pixel for
pixel, and, with a fake decoder, frames made in order, repeats left out,
a new decoder at each capture session, a decoder that holds pictures
back, and a decoder that cannot be made or that fails. Then, on the
development PC, a check program made 235 frames from test 18's stream
with Windows' decoder in 1.9 seconds; two of them were compared with
FFmpeg's decoding of the same pictures and are the same in every pixel
(see the implementation plan, "Built, step 3b"). None of that shows the
frames made during a recording on the Pixel, the processor load and disk
use while the phone's screen moves, or the frames after a rotation.
Tests 20 to 23 do.

### Test 20: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. If the panel offers "Install companion app", choose it, and wait until
   the panel shows "Companion app: installed, on, and answering".
5. Read the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. In step 5 the
panel shows "Companion app: installed, on, and answering (version
1.1.0", and the note reads "A recording of the phone records the
companion app's connection, the phone's clock, its display's rotation
and size, and the phone's screen as a video stream and as a picture for
each change. The phone's screen is not yet shown in the player."

### Test 21: a recording with the phone's screen moving

With the panel from test 20 showing, and the phone unlocked on its home
screen:

1. Choose "Start recording", and wait 5 seconds.
2. On the phone, open Settings, and scroll its list up and down with
   your finger for 10 seconds.
3. Put the phone down without touching it, and wait 10 seconds.
4. Take a screenshot of "Collector status" under "Current session".
5. Choose "Stop recording".

**On screen:** in step 4 the "android.screen" line reads
"android.screen: Running, Healthy. Phone screen:" with a number of
packets, a number of MB, "1080 by 2404", a number of frames, and the
median delay. The number of frames is expected to be close to the number
of packets: one less for each configuration packet. If the line says
"packets waiting to be decoded" after the 10 seconds without touching
the phone, decoding is behind; that is not a failure, but is recorded.
Send the screenshot. "Stop recording" may take a few seconds longer than
before if frames are still being made.

### Test 22: the pictures in the recording's folder

1. Open File Explorer, and go to Documents, then "Windows A11y
   Recorder".
2. Open the newest folder, the recording of test 21.
3. Open the folder `frames`, then the folder `android` inside it. (The
   recording's folder also has an `android` folder of its own, which
   holds the stream, `screen.h264`; that is not the one.)
4. Open the first file, `0000000001.png`.
5. Close it, and open the last file in the folder.

**On screen:** in step 3 the folder holds PNG files named by number,
from `0000000001.png` up, as many as the frames in the line of test 21,
and no file ending `.tmp`. The first picture, in step 4, is the phone's
home screen, upright, 1080 by 2404, its colours as on the phone. The
last, in step 5, is the Settings list where scrolling stopped. Send the
folder's total size: select all its files, and read the size in the
Details pane, or in Properties.

### Test 23: the phone rotated during a recording

1. On the Pixel, turn on Auto-rotate (swipe down twice from the top of
   the screen), and open Chrome.
2. In the recorder, choose "Start recording", and wait 5 seconds.
3. Turn the phone on its side, wait 5 seconds, then choose "Stop
   recording".
4. Turn the phone upright.
5. In File Explorer, open the newest recording's folder, then `frames`,
   then `android` inside it, and in the View menu choose "Large icons".

**On screen:** in step 5 the first pictures are tall and show Chrome
upright; the last pictures are wide and show Chrome in landscape,
upright as the phone was held. No picture is stretched or cut off.

### Results of part 3, step 3b

- 2026-11-09, installer built from `a94bc6c`: test 20 passed. "Install
  companion app" updated the companion to version code 700, and the note
  read as expected, followed by its earlier last sentence, "Its sound,
  touches and accessibility evidence come in later parts."
- 2026-11-09, test 21 passed, read from a screenshot: "android.screen:
  Running, Healthy. Phone screen: 3070 packets, 26.5 MB, 1080 by 2404,
  3069 frames, median delay from phone to recorder 26.3 ms." No packets
  were waiting to be decoded, so decoding kept up with scrolling. The
  same screenshot shows "Events: 6,403 accepted, 0 dropped. Database:
  6," cut off at the panel's right edge instead of wrapping; that is
  noted for the later work on the panel, not changed here.
- 2026-11-09, test 22 passed. The first step at first opened the
  recording's own `android` folder, which holds the stream; the steps
  of tests 22 and 23 now say "the `android` folder inside" `frames`.
  Checked from the recording's folder on the development PC
  (`20261109-151338-aaebf1d4650e49118ef48e5fb1e4c170`): `frames\android`
  holds 3,080 PNG files and no `.tmp` file, 11 more than the screenshot
  in test 21, as frames went on being made until "Stop recording". The
  owner saw the first, the home screen, upright with the phone's
  colours, and the last, the Settings list where scrolling stopped.
- The 3,080 pictures use 2,244 MB for about 76 seconds of recording,
  against 26.5 MB for the stream they were decoded from: about 2.9 MB
  for a picture of the home screen and 0.57 MB for the last pictures of
  Settings. At that rate an hour of a moving screen would use more than
  100 GB. No picture was left out as a repeat, as expected from the
  check on the development PC. The stream holds every picture the PNG
  files do, so how many PNG files to make, and when, is a question of
  disk use and speed, not of evidence. The owner decided on
  2026-11-09 to keep the frames as step 3b makes them, one for each
  picture that differs from the one before: disk space is cheap, and
  analysis uses only what differs, as for the desktop.
- 2026-11-09, test 23 passed. The owner saw every picture in the right
  orientation, tall then wide, and pictures of the phone's turning
  animation between them. Checked from the recording
  (`20261109-153003-26acc41259cf41d6a09c4f7f78e3990f`): 131 pictures,
  1 to 13 at 1080 by 2404, 14 to 82 at 2404 by 1080 (14 is black, the
  first picture of the turn), and 83 to 131 at 1080 by 2404 again, 83
  showing the screen still on its side within the tall picture. The
  last 49 pictures were made in the last 1.4 seconds, so the phone was
  turned upright again before its screen stopped being recorded; the
  pictures follow the phone in both turns. None is stretched or cut
  off. All of step 3b's tests have passed.

## Part 3, step 3c: the phone's screen in the player and the MP4 export

Step 3c shows the phone's pictures in the player, in the desktop's
place, and saves them as the picture of an MP4 export: as large as
fits, upright as the phone was held, at 60 frames a second.

What was checked before these tests: in the Linux build environment the
solution builds with no warnings and the tests pass, 4 of them new. On
the development PC a check program opened test 23's recording with the
recorder's own code, and found its 131 pictures, tall, then wide from
6.076 seconds, then tall again from 15.656 seconds. It saved that
recording as MP4 at 2304 by 2304 at 60 and at 120 frames a second, and
Windows decoded all of them (see the implementation plan, "Built, step
3c"). None of that shows the player or the export window in the app, or
what the MP4 looks like in Media Player. Tests 24 to 28 do.

### Test 24: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. If the panel offers "Install companion app", choose it, and wait until
   the panel shows "Companion app: installed, on, and answering".
5. Read the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. In step 5 the
panel shows "Companion app: installed, on, and answering (version
1.1.0", and the note reads "A recording of the phone records the
companion app's connection, the phone's clock, its display's rotation
and size, and the phone's screen as a video stream and as a picture for
each change, which the player shows and an MP4 export saves. Its sound,
touches and accessibility evidence come in later parts."

### Test 25: test 23's recording in the player

1. In the recorder, choose "Open recording...".
2. Open the recording of test 23,
   `20261109-153003-26acc41259cf41d6a09c4f7f78e3990f`.
3. Drag the playback slider to 00:00:03.000, and look at the picture.
4. Drag the playback slider to 00:00:10.000, and look at the picture.

**On screen:** in step 3 the picture is tall and shows Chrome upright,
filling the frame area's height. In step 4 it is wide and shows Chrome
in landscape, upright as the phone was held, filling the frame area's
width. Neither is stretched or cut off. The recording was made before
this version, and its stored index holds no phone frames, so pictures
shown at all mean its index was made again on opening.

### Test 26: the MP4 export window for the phone

With the recording of test 25 open:

1. Choose "Save as MP4..." (Alt+4).
2. Read the window.

**On screen:** the window's heading is "Save the recording as MP4",
then "The whole recording, 0:18, as H.264 video of 2304 by 2304 pixels
at 60 frames a second.", followed by the sentence about each recorded
frame being shown until the next, then "The phone sends a picture
whenever its screen changes, up to 120 a second. Of pictures less than
a sixtieth of a second apart, only the last before each video frame is
shown; the recording keeps them all." The next line is "Picture: the
phone's screen as recorded, each picture as large as fits, upright as
the phone was held." Under "File": a path ending
`\Videos\20261109-153003-26acc41259cf41d6a09c4f7f78e3990f.mp4`. All
the text wraps within the window, with no scroll bar across it.

### Test 27: the MP4 of the phone

In the window from test 26:

1. Choose "Save MP4", and wait for the status line to stop changing.
2. Read the status line.
3. Open the MP4 in Media Player, drag to 0:03, and pause.
4. Drag to 0:10, and pause.

**On screen:** in step 2 the status line begins "Saved
C:\Users\User\Videos\20261109-153003-26acc41259cf41d6a09c4f7f78e3990f.mp4:
2304 by 2304 pixels," and includes "Encoded with the Windows encoder".
Any line beginning "The MP4 could not be saved" is a failure; send it.
In step 3 the video is square and shows Chrome tall and upright in the
middle, filling the height, black on both sides. In step 4 it shows
Chrome wide, in landscape, upright, filling the width, black above and
below. Neither is stretched.

### Test 28: three apps on the phone

With the Pixel upright on its home screen, and the recorder's target
still "Android device":

1. Choose "Start recording", and wait 5 seconds.
2. On the phone, open Settings, wait 5 seconds, and go back to the home
   screen.
3. Open Chrome, wait 5 seconds, and go back to the home screen.
4. Open Clock, wait 5 seconds, then in the recorder choose "Stop
   recording".
5. When the recording has opened in the player, choose Play, and watch
   the picture until the end.

**On screen:** in step 5 the picture shows, in order, the home screen,
Settings, the home screen, Chrome, the home screen and Clock, each
upright and filling the frame area's height, changing as the time
moves on.

### Results of part 3, step 3c

- 2026-11-09, installer built from `697f624`: test 24 passed. The note
  read as expected.
- 2026-11-09, test 25 as first written asked for the status line at the
  bottom of the window just after opening, which was to end "The
  recording file's playback index is version 19". It did not show that:
  the line showed the nearest event, "No matching event before this
  position." at the start, and "00:00:02.822 | clock.android |
  clock-sample" at 3 seconds. The message about the recording being
  opened is replaced by the nearest event straight after opening, so it
  cannot be read; that is noted for the later work on the player, not
  changed here. The test now checks the re-indexing by the pictures
  being shown, as the stored index holds none.
- 2026-11-09, test 25, as revised, passed. At 3 seconds the picture was
  tall, with Chrome upright filling the frame area's height; at 10
  seconds it was wide, with Chrome in landscape filling its width.
- 2026-11-09, test 26: the window's paragraph and "Picture:" line read
  exactly as expected, copied from the window. The file's path is
  checked by test 27's status line, which names it.
- 2026-11-09, test 27 passed. The status line read "Saved
  C:\Users\User\Videos\20261109-153003-26acc41259cf41d6a09c4f7f78e3990f.mp4:
  2304 by 2304 pixels, no sound. Encoded with the Windows encoder,
  graphics card allowed." No sound is expected until part 4. In Media
  Player, at 0:03 Chrome was tall and upright, filling the height with
  black on both sides, and at 0:10 wide and in landscape, filling the
  width with black above and below. The owner did not notice a
  horizontal scroll bar in the export window; its scroll viewer has
  horizontal scrolling turned off in the code (`ExportMp4Window`), so
  none can appear.
- 2026-11-09, test 28 passed. Played back, the picture showed the home
  screen, Settings, the home screen, Chrome, the home screen and Clock in
  order, each upright and filling the frame area's height. All of step
  3c's tests have passed, and with them slice 3's target test: the
  three apps here, and the rotation in tests 25 and 27.

## Part 4, step 4a: the phone's sound recorded and played on the PC

Part 4 is built in three steps (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 4"). Step 4a records the phone's whole sound as
`audio\device.wav` in the recording's folder, with an event for each
packet, shows its level while recording with a message when it is too
quiet, and plays it on the PC as it arrives, because while the sound is
captured the phone does not play it itself. A recording of the phone
now also records the PC's microphone, as `audio\microphone.wav`. The
player does not label the phone's sound yet, and the MP4 export does
not include it; that is step 4b.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and the tests pass, 13 of them
new. They cover the audio socket's codec id (sound, the phone unable to
capture, closed at once), read from bytes written out by hand from
scrcpy 5.0.1's source; the level and the quiet threshold at check 1's
2.9% and 32.0%; a recording with packets placed by the phone's clock,
stored and passed to a stand-in player; the quiet message; no phone
clock; no PC playback device; the screen stream not started; and the
cable unplugged part way. None of that shows the Pixel's sound
arriving, its packet times on the companion's clock, the level the
Pixel sends, or the sound playing on the PC. Tests 29 to 32 do.

TalkBack is needed for test 30 only; turn it off after test 30.

### Test 29: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. If the panel offers "Install companion app", choose it, and wait until
   the panel shows "Companion app: installed, on, and answering".
5. Read the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. In step 5 the
panel shows "Companion app: installed, on, and answering (version
1.1.0", and the note reads "A recording of the phone records the
companion app's connection, the phone's clock, its display's rotation
and size, and the phone's screen as a video stream and as a picture for
each change, which the player shows and an MP4 export saves. It also
records the phone's sound, shows its level while recording and plays it
on this PC, and records this PC's microphone. The phone's sound in the
player and in an MP4 export, touches and accessibility evidence come in
later parts."

### Test 30: the phone's sound level while recording, with TalkBack

Before the test, turn on TalkBack (Settings, Accessibility, TalkBack),
and go to the phone's home screen. Leave the Accessibility volume as it
is.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, swipe right 3 times, slowly, then at once take a
   screenshot of "Collector status" under "Current session".
3. Press the phone's volume-up key, open the volume panel's settings,
   and set "Accessibility volume" to about 80%. If it is already there,
   move it down and back up to about 80%.
4. Swipe right 3 times, slowly, then at once take a screenshot of
   "Collector status".
5. Choose "Stop recording", and turn TalkBack off.

**On screen:** in both screenshots "Collector status" has four lines:
"android.companion", "android.screen", a line starting "android.sound:
Running, Healthy." and the line "windows.audio: Running, Healthy". The
"android.sound" line has "Phone sound:" with a number of packets, a
number of MB, "loudest in the last 10 seconds" with a percentage of full
scale, "median delay from phone to recorder" with a number of
milliseconds, and ends "playing on" with the name of the PC's speakers
or headphones. In step 2 the line is expected to have "The phone's sound
is very quiet. Raise the phone's Accessibility volume now." after
"Healthy.", and a percentage below 10% (check 1 found 2.9% before the
volume was changed during the capture). In step 4 that message is gone
and the percentage is 10% or more (check 1 found 32.0%). If the step 2
line has no message and its percentage is already 10% or more, that is
not a failure, but is recorded: it would mean the volume set before the
capture counts after all. The median delay checks the sound's times: a
few tens of milliseconds says the packets are on the companion's clock;
a number below zero, or of seconds or more, says they are not. If the
line says "not played on this PC", "placed by the time each packet
arrived", or Degraded, send it as it is. Send both screenshots.

### Test 31: the sound files in the recording's folder

1. Open File Explorer, and go to Documents, then "Windows A11y
   Recorder".
2. Open the newest folder, the recording of test 30.
3. Open the folder `audio`.
4. Open `device.wav` in Media Player, and read its length, shown at the
   right end of the time bar.
5. Close it, and do the same with `microphone.wav`.

**On screen:** in step 3 the folder holds two files, `device.wav` and
`microphone.wav`. In step 5 the microphone's length is close to the
length of the recording of test 30, within a few seconds. In step 4 the
phone's sound is expected to be about 5 to 6 seconds shorter than that:
test 30's lines show its packets covering about 5.5 seconds less than
the recording's duration (994 packets of 1024 samples at 48 kHz, 21.2
seconds, at 27 seconds; 5,136 packets, 109.6 seconds, at 1 minute 55
seconds), so it starts that much after the recording does. Write down
both lengths.

### Test 32: the cable unplugged during a recording

1. In the recorder, choose "Start recording", and wait 10 seconds.
2. Unplug the USB cable from the phone.
3. Within 5 seconds, take a screenshot of "Collector status" under
   "Current session".
4. Choose "Stop recording".
5. Plug the cable in again and unlock the phone.

**On screen:** in step 3 the "android.sound" line starts "android.sound:
Running, Degraded. The phone's sound stopped being recorded during the
recording. The sound stream ended:" followed by the reason, then "The
recording goes on without it." and "Phone sound:" with the packets
recorded before the cable was unplugged. The "android.screen" and
"android.companion" lines are Degraded too, as in tests 19 and 14, and
"windows.audio: Running, Healthy" is unchanged. "Stop recording" ends
the recording as usual. Send the screenshot.

### Results of part 4, step 4a

- 2026-11-09, installer built from `70795cc`: test 29 passed. The note
  read as expected, copied from the panel.
- 2026-11-09, test 30: both screenshots showed the four lines expected,
  each "Running, Healthy", with the "android.sound" line ending "playing
  on Speakers (Realtek(R) Audio)." At 27 seconds, after the first swipes
  and before the volume was changed, the line read "loudest in the last
  10 seconds 36% of full scale" with no quiet message. At 1 minute 55
  seconds, after the volume was set to about 80% and the second swipes,
  it read 14%, again with no message. So the quiet message was not seen
  on the Pixel: the volume set before the capture did count here,
  unlike check 1, where the level as found was 2.9% and raising the
  volume to the maximum before the run left it at 2.9%. Why the level
  fell from 36% to 14% after the volume was set to about 80% is not
  known; one explanation, not checked, is that the slider was above
  80% before. The median delay from phone to recorder was 3.1 ms and
  2.9 ms, against 28.9 ms and 30.9 ms for the screen: positive and
  small, so the sound's packets are on the companion's clock. That a
  packet's time is its last sample's fits a delay of a few milliseconds
  (if it were the first sample's, the delay would be at least the
  packet's 21.3 ms), but that remains inference. The packet counts
  cover about 5.5 seconds less than the duration in both screenshots,
  so the phone's sound starts about 5.5 seconds after the recording;
  test 31 checks the files' lengths.

## Part 4, step 4c: the Accessibility volume raised by the recorder

Step 4c makes the recorder set the phone's Accessibility volume, as the
owner decided on 2026-11-09 (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 4", "Built, step 4c"). adb's shell cannot set it, but the
companion app can, as an accessibility service that is on. When the
first sound packet arrives, the companion app raises the Accessibility
volume to at least 80% if TalkBack, or another service that uses it, is
on, never lowers it, and leaves it there when the recording ends. With
no such service on, the Accessibility volume is the media volume, and
the app changes nothing. The companion's status line ends with what it
found and did. The companion app now speaks protocol 2, so it has to be
installed again.

Android keeps a volume for each output, and while the sound is captured
the phone plays to the capture instead of its speaker, so the volume is
set once the capture is running. That this is why check 1 saw no change
from a volume set before the run, and why test 30 started at 36% (a
volume left from an earlier capture), is inference from Android's
source. A consequence, also inference: after a recording the phone's
own speaker volume is as it was, and the 80% stays with the capture.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, the companion app compiles with
warnings as errors, and the tests pass, with 5 new tests (11 cases) and
2 changed. They
cover reading the app's answer and writing the request, the status
line's words for each outcome, a recording against a stand-in app that
answers "raised" and then "not-separate" when TalkBack is turned off,
the request sent once and only when the sound collector says the
capture has begun, no request without a percent, the sound collector
saying so at the first packet only, and the record's checks. None of
that shows Android accepting the companion's change on the Pixel, or
the volume being that of the capture. Tests 33 to 36 do.

TalkBack is needed for tests 34 and 36 only; each says when to turn it
on and off.

### Test 33: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. The panel offers "Install companion app". Choose it, and wait until
   the panel shows "Companion app: installed, on, and answering".
5. Read the companion line and the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. Before step 4 the
companion line says the installed version code is older than the
recorder's. In step 5 it reads "Companion app: installed, on, and
answering (version 1.1.0, version code" with a number, and ends
"protocol 2)." The note reads "A recording of the phone records the
companion app's connection, the phone's clock, its display's rotation
and size, and the phone's screen as a video stream and as a picture for
each change, which the player shows and an MP4 export saves. It also
records the phone's sound, shows its level while recording and plays it
on this PC, and records this PC's microphone. When TalkBack is on, the
companion app raises the phone's Accessibility volume to at least 80% as
the recording starts and leaves it there. The phone's sound in the
player and in an MP4 export, touches and accessibility evidence come in
later parts."

### Test 34: a lowered Accessibility volume raised to 80%

Before the test, turn on TalkBack, and go to the phone's home screen.

1. In the recorder, choose "Start recording", and wait 10 seconds.
2. Press the phone's volume-up key, open the volume panel's settings,
   set "Accessibility volume" to about 30%, then choose "Stop
   recording".
3. Choose "Start recording", and wait 10 seconds.
4. Take a screenshot of "Collector status" under "Current session".
5. Press the phone's volume-up key, open the volume panel's settings,
   and read where the "Accessibility volume" slider is. Then choose
   "Stop recording", and leave TalkBack on.

**On screen:** in step 4 the "android.companion" line starts
"android.companion: Running, Healthy." and ends "Accessibility volume
raised from" a number of steps, "of" the largest step, "steps" and a
percentage near 30%, "to" a number of steps with a percentage of 80% or
more, for example "raised from 8 of 25 steps (32%) to 20 of 25 steps
(80%)." In step 5 the slider is at about 80%. If the line says "could
not be raised", "not changed" or "could not be read", send it as it
is. Send the screenshot, and write down where the slider was.

### Test 35: the volume left alone with TalkBack off

Before the test, turn TalkBack off.

1. In the recorder, choose "Start recording", and wait 10 seconds.
2. Take a screenshot of "Collector status" under "Current session".
3. Choose "Stop recording".

**On screen:** in step 2 the "android.companion" line starts
"android.companion: Running, Healthy." and ends "Accessibility volume
not changed: no service that uses it, such as TalkBack, is on, so it is
the media volume." Send the screenshot.

### Test 36: TalkBack turned on during a recording

Before the test, check that TalkBack is off.

1. In the recorder, choose "Start recording", and wait 10 seconds.
2. On the phone, open Settings, Accessibility, TalkBack, turn on "Use
   TalkBack", and choose "Allow" if asked.
3. Wait 5 seconds, then take a screenshot of "Collector status" under
   "Current session".
4. Choose "Stop recording".
5. Turn TalkBack off.

**On screen:** in step 3 the "android.companion" line no longer ends
"not changed". It ends either "Accessibility volume already" with a
number of steps and a percentage of 80% or more, then "at least the 80%
asked for", or "Accessibility volume raised from" and "to" a number of
steps with a percentage of 80% or more. "already" is what is expected
after test 34, if the 80% stays with the capture. Send the screenshot.

### Results of part 4, step 4c

- 2026-11-09, test 33 was first run on an installer built from
  `70795cc`, as the step 4c commits had not been pushed: the panel showed
  version code 713, protocol 1, and the old note. Not a failure of step
  4c. After the push of `9bff6bc`, test 33 was run again: the note read
  as expected, copied from the panel.
- 2026-11-10, test 34: passed. The first recording
  (`20261110-010325-cb14d6c0308f463880b65eebffdc6a1a`) found the volume
  "already 12 of 15 steps (80%)", the level test 30 had left during its
  capture. Its `device.wav` peaks at 31.7% of full scale at 6 seconds and
  26.6% at 28 seconds, then 0.8% to 5.6% from 30 to 38 seconds, after the
  owner lowered the volume to about 30%. The second recording
  (`20261110-010817-dcc254d7d611414a80dcf7858839add3`), at 20 seconds:
  "android.companion: Running, Healthy." ending "Accessibility volume
  raised from 5 of 15 steps (33%) to 12 of 15 steps (80%)." The slider
  read 80% during it. So Android accepted the companion's change, and a
  volume set during one capture was what the next capture found, which
  fits the volume staying with the capture. That it is kept apart from
  the phone's speaker volume is still inference. The phone has 15 steps,
  not the 25 of the examples.
- 2026-11-10, test 35: passed. With TalkBack off, recording
  `20261110-011117-9d9fdfe22e8a4b06aa0d9f14b15d2bbb` at 17 seconds:
  "android.companion: Running, Healthy." ending "Accessibility volume
  not changed: no service that uses it, such as TalkBack, is on, so it is
  the media volume."
- 2026-11-10, test 36: passed. TalkBack was turned on about 10 seconds
  into recording `20261110-011358-7ba2f05eef0e4abe86e263877803339c`; at
  22 seconds the line was "android.companion: Running, Healthy." ending
  "Accessibility volume already 12 of 15 steps (80%), at least the 80%
  asked for.", and the "android.sound" line read "loudest in the last 10
  seconds 36% of full scale". So the app heard TalkBack being turned on
  and checked again, and the 80% from test 34 was still the capture's.
  Step 4c is complete.

## Part 4, step 4b: the phone's sound in the player and the MP4 export

Step 4b names the phone's sound in the player and saves it in an MP4
(see [the implementation plan](../architecture/android-implementation-plan.md),
"Slice 4", "Built, step 4b"). A recording of a phone has no system
sound, so the phone's sound takes its place: it is drawn on the system
sound's lane, in the system sound's green, directly under the
microphone's pink lane, where step 4a drew it in grey on the bottom
lane. The system sound's slider and filter are labelled "Phone sound"
while such a recording is open, and the list of lanes at the right of
the status line at the bottom of the window ends "Mic | Phone sound".
The MP4 export offers "Phone sound" where it offers the system sound,
mixed at the slider's level.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings and the tests pass, with 1 new
test. It writes phone sound packet records, with the phone's time and
the time they were read, to a recording file, reads them back as the
export does, and gives them to the export's sound mixer. None of that
shows the player's labels and lane, or an MP4 made from a phone
recording. Tests 37 to 41 do. The player itself cannot be run in the
Linux build environment.

The tests use test 34's second recording,
`20261110-010817-dcc254d7d611414a80dcf7858839add3`, which has the
phone's sound with TalkBack at 80% and the PC's microphone. TalkBack is
not needed.

### Test 37: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. If the panel offers "Install companion app", choose it, and wait
   until the panel shows "Companion app: installed, on, and answering".
5. Read the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. In step 5 the
note reads "A recording of the phone records the companion app's
connection, the phone's clock, its display's rotation and size, and the
phone's screen as a video stream and as a picture for each change, which
the player shows and an MP4 export saves. It also records the phone's
sound, shows its level while recording and plays it on this PC, and
records this PC's microphone. When TalkBack is on, the companion app
raises the phone's Accessibility volume to at least 80% as the recording
starts and leaves it there. The player shows and plays the phone's sound
as "Phone sound", in the system sound's place, and an MP4 export saves
it. Touches and accessibility evidence come in later parts."

### Test 38: the phone's sound lane and slider

1. In the recorder, choose "Open recording...".
2. Open `20261110-010817-dcc254d7d611414a80dcf7858839add3`.
3. Take a screenshot of the whole recorder window.

**On screen:** in the timeline, directly under the pink band of the
microphone's marks, there is a green band of marks from a few seconds
after the start to the end of the recording. (The grey marks on the
bottom lane are the companion app's and the screen stream's records,
which are under "Other"; test 39 shows the phone's sound is not among
them.) In the row of volume sliders under the picture, the second
slider's label reads "Phone sound:" where a desktop recording reads
"System audio:". At the right of the status line at the bottom of the
window: "Keyboard | Mouse | UIA | Window | Frames | Mic | Phone sound".
Send the screenshot.

### Test 39: the "Phone sound" filter

With the recording of test 38 open:

1. In "Playback event filters", find the check box that reads "Phone
   sound", where a desktop recording reads "System audio".
2. Read the line under the filters, "events shown".
3. Uncheck "Phone sound".
4. Read the line again, and look at the timeline.
5. Check "Phone sound" again.

**On screen:** in step 2 the line reads the same number twice, "N of N
events shown". In step 4 the first number is smaller, and the green band
is gone from the timeline, while the pink band stays. Write down both
lines.

### Test 40: the slider's level in the export window

With the recording of test 38 open:

1. Drag the "Phone sound" slider until the text beside it reads "-12
   dB".
2. Choose "Save as MP4..." (Alt+4).
3. Read the check boxes under "Sound".
4. Close the window without saving.
5. Drag the "Phone sound" slider back to "0 dB".

**On screen:** in step 3 there are two check boxes, both checked:
"Microphone, mixed at 0 dB" and "Phone sound, mixed at -12 dB". There
is no "System sound" check box, and no sentence about a separate screen
reader sound. Send a screenshot of the window.

### Test 41: the MP4 with the phone's sound

With the recording of test 38 open, and both sliders at "0 dB":

1. Choose "Save as MP4..." (Alt+4).
2. Choose "Save MP4", and wait for the status line to stop changing.
3. Take a screenshot of the window.

**On screen:** the status line begins "Saved
C:\Users\User\Videos\20261110-010817-dcc254d7d611414a80dcf7858839add3.mp4:"
and includes "3 sound tracks: mixed, then microphone, phone sound." and
"Encoded with the Windows encoder". A line "Phone sound: no buffer
records" means the export did not read the phone's packet records; a
line beginning "The MP4 could not be saved" is a failure. A line
"Phone sound:" about silences is not a failure. Send the screenshot.

### Results of part 4, step 4b

- 2026-11-10, test 37: the first build, from `9f4fa9d`, failed with
  MC3000 at `MainWindow.xaml` line 367: the note's text attribute had
  bare double quotes around "Phone sound". The quotes were added after
  the last build in the Linux build environment, which does report the
  error, and committed without building again. After `2564bc8` wrote
  them as `&quot;`, test 37 passed: the note read as expected, copied
  from the panel.
- 2026-11-10, test 38: passed. With the recording open (2:57.603 long),
  the second slider read "Phone sound:", the list at the right of the
  status line read "Keyboard | Mouse | UIA | Window | Frames | Mic |
  Phone sound", and a green band ran directly under the pink
  microphone band, both from near the start to the end. The test as
  first written also expected no grey band on the bottom lane; that was
  wrong, as the companion app's and the screen stream's records are
  drawn there, and the screenshot showed them. The text above is
  corrected. The recording's `device.wav` holds 173.3 s and its
  `microphone.wav` 173.1 s. The picture was black at 00:00:00.000,
  before the first frame; this was not looked into.
- 2026-11-10, test 39: passed. With every filter checked the line read
  "15,324 of 15,324 events shown"; with "Phone sound" unchecked,
  "7,200 of 15,324 events shown", 8,124 fewer. `device.wav` holds
  33,267,714 bytes after a 44-byte header, which is 8,122 packets of
  4,096 bytes, and with the stream's start and stop records that is
  8,124. That every packet was a full 4,096 bytes and the header 44
  bytes is inference from the file's size, but the match says the
  filter covers the phone's sound records and nothing else.
- 2026-11-10, test 40: passed. With the "Phone sound" slider at -12 dB,
  the export window's "Sound" section had two checked boxes,
  "Microphone, mixed at 0 dB" and "Phone sound, mixed at -12 dB", and no
  "System sound" box. The screenshot showed only that section, so the
  absence of the screen reader sentence below it was not seen.
- 2026-11-10, test 41: passed. The status line read "Saved
  C:\Users\User\Videos\20261110-010817-dcc254d7d611414a80dcf7858839add3.mp4:
  1034 by 2304 pixels, 3 sound tracks: mixed, then microphone, phone
  sound. Encoded with the Windows encoder, graphics card allowed.", with
  no "Phone sound:" line, so the export read the phone's packet records
  and found no silences to insert. The whole window was in the
  screenshot, and it had no sentence about a separate screen reader
  sound, which completes test 40's check. Step 4b is complete, and with
  it slice 4.

## Part 5, step 5a: the phone's touches and hardware keys recorded

Part 5 is built in two steps (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 5"). Step 5a records the phone's raw input events from
`getevent` for the whole recording: every line it prints, each report
of the touch screen with each finger's position in screen pixels, and
each press of a hardware key, such as volume or power. While recording,
a new line under "Collector status", starting "android.touch", counts
the touches and key presses and gives the last touch's place. The
player does not draw the touches yet; that is step 5b.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and the tests pass, 16 of them
new. They read check 4's touches on the Pixel, saved as a test file,
and give its three taps within half a pixel of the positions check 4
reported, using axis ranges chosen to fit, as check 4 did not save the
Pixel's own. They also cover the device list as `getevent -lp` prints
it, two fingers at once, the four rotations, a recording with a stand-in
adb and phone clock, no phone clock, no touch screen, the devices not
listed, and getevent ending. None of that shows the Pixel's own device
list and ranges, its lines arriving through adb while recording, or
their times on the companion's clock. Tests 42 to 44 do.

TalkBack is not needed, and is best left off: with TalkBack on, a tap
selects rather than opens, but the tests' taps are on empty parts of
the home screen either way.

### Test 42: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. If the panel offers "Install companion app", choose it, and wait
   until the panel shows "Companion app: installed, on, and answering".
5. Read the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. In step 5 the note
ends "The player shows and plays the phone's sound as "Phone sound", in
the system sound's place, and an MP4 export saves it. It records every
touch on the phone's screen and every hardware key, such as volume and
power, with each finger's position in screen pixels. The player does
not yet draw the touches. Accessibility evidence comes in later parts."

### Test 43: three taps and a volume key while recording

Before the test, go to the phone's home screen.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, tap once on an empty part of the wallpaper, away from
   icons and widgets, near the top of the screen. Wait 2 seconds, and
   do the same in the middle of the screen, then just above the row of
   app icons at the bottom.
3. Press the phone's volume-down key once, briefly.
4. While it is still recording, take a screenshot of "Collector status"
   under "Current session".
5. Choose "Stop recording", and set the volume back if the key changed
   it.

**On screen:** "Collector status" has a line starting "android.touch:
Running, Healthy. Touches: 3 touches,", then a number of reports, then
"last up at x", a number, "y", a number, "of 1080 by 2404", then "1
hardware key press", "median delay from phone to recorder" with a number
of milliseconds, and it ends "touch screen", a device such as
`/dev/input/event3`, the device's name in quotes, and "with x 0 to" a
number "and y 0 to" a number. The last place is that of the bottom tap:
its y is near the bottom, about 2000 or more. The delay checks the
touches' times: a few milliseconds to a few tens says they are on the
companion's clock; a number below zero, or of seconds or more, says
they are not. "placed by the time each line arrived", Degraded, or more
or fewer than 3 touches is a failure; send the line as it is. Send the
screenshot.

### Test 44: two fingers at once

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone's home screen, put two fingers on empty parts of the
   wallpaper, about 3 cm apart side by side, and keep them there, still.
3. While both fingers are still on the screen and it is still
   recording, take a screenshot of "Collector status". (If the phone
   shows a menu for the home screen, leave it; it does not matter.)
4. Lift both fingers.
5. Choose "Stop recording", and close the phone's menu if it opened.

**On screen:** in step 3 the "android.touch" line reads "Touches: 2
touches," and "last", either "down" or "move", "at x", a number, "y", a
number, "of 1080 by 2404 with 2 fingers". "with 2 fingers" says the
recorder followed both fingers at once, each in its own slot. A line
without it, or with "1 touch", is a failure; send it as it is. Send the
screenshot.

### Results of part 5, step 5a

- 2026-11-10, test 42: passed. The installer built from `246d2ba`, and
  the note under "Android device checks" read as expected, copied from
  the panel.
- 2026-11-10, test 43: passed, with one touch more than the test asked
  for. The screenshot was taken just after "Stop recording" rather than
  while recording, so the lines read Disposed instead of Running; the
  numbers are the same. The "android.touch" line read "Healthy.
  Touches: 4 touches, 338 reports, last up at x 736, y 2218 of 1080 by
  2404, 1 hardware key press, median delay from phone to recorder 5.4
  ms, touch screen /dev/input/event1 "synaptics_tcm_touch" with x 0 to
  13439 and y 0 to 29919." The recording file
  (`20261110-135423-765414caa56a49558eea381fd1bf393b`, copied from the
  PC) shows the three taps 2.1 and 2.6 seconds apart, at 11.1, 13.2 and
  15.8 s into the recording: x 608, y 303; x 685, y 1299; and x 713,
  y 2230. The extra touch came first, at 6.3 s: a finger that moved
  from x 845, y 737 to x 791, y 997 in half a second, before the taps.
  That it was a touch by the tester, such as one to wake the screen, is
  inference; it is a real touch, recorded as one. The volume key was
  one press, a down and an up 0.16 s apart, from "gpio_keys". The
  Pixel's touch screen ranges, 0 to 13439 and 0 to 29919, are the same
  as the ranges the tests chose to fit check 4. Each touch record was
  placed by the phone's clock, read 3 to 31 ms after its phone time.
- 2026-11-10, test 44: passed. With two fingers held on the home screen,
  21 seconds into the recording, the "android.touch" line read
  "Running, Healthy. Touches: 2 touches, 1867 reports, last move at
  x 301, y 1459 of 1080 by 2404 with 2 fingers, 0 hardware key presses,
  median delay from phone to recorder 4.0 ms", so both fingers were
  followed at once, each in its own slot. The panel kept sending
  reports while the fingers were still: 1,867 reports in at most about
  16 seconds, over 100 a second, each kept as a record. Step 5a is
  complete.

## Part 5, step 5b: the phone's touches drawn in the player

Step 5b draws the phone's touches on its picture in the player (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 5", "Built, step 5b"). While a finger is down, a yellow circle
with a black edge is drawn where it is, with a yellow line behind it
for where it went in the last second. With two or more fingers, each
circle holds a number, 1 for the finger that went down first. A
recording of a phone has no keyboard or mouse, so the touches are
drawn on the mouse's lane of the timeline, the second from the top, in
blue, and the hardware keys on the keyboard's, the top one. While such
a recording is open, those filters read "Touches" and "Phone keys", and
the list of lanes at the right of the status line at the bottom of the
window begins "Phone keys | Touches".

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and the tests pass, 7 of them new.
They keep touch reports in the playback index and read them back as the
fingers on the screen at a time: three taps, each one finger at its
place until it is lifted, a two-finger swipe with two numbered fingers
moving together, the numbers in the order the fingers went down, the
path over the last second, and positions scaled to a smaller picture
and to a sideways one. None of that shows the circles drawn on the
Pixel's picture, or a sideways touch drawn where it was made. Tests 45
to 47 do. The player itself cannot be run in the Linux build
environment.

In tests 46 and 47 a touch is found on the timeline by selecting its
marks: selecting a mark pauses the player and moves it to the mark's
time, so the picture shows the fingers as they were then. A touch's
marks are a short run of blue marks on the second lane. The last mark
of each touch is the finger being lifted, which is drawn without a
circle, so if the picture shows no circle after selecting a mark, press
the Left arrow key once: with the timeline focused, it selects the
lane's mark before.

TalkBack is not needed, and is best left off.

### Test 45: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. If the panel offers "Install companion app", choose it, and wait
   until the panel shows "Companion app: installed, on, and answering".
5. Read the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. In step 5 the note
ends "It records every touch on the phone's screen and every hardware
key, such as volume and power, with each finger's position in screen
pixels. The player draws each finger on the phone's picture as a yellow
circle, numbered when there is more than one, with its path, and shows
the touches and keys as "Touches" and "Phone keys" in the mouse's and
keyboard's places. Accessibility evidence comes in later parts."

### Test 46: three taps and a two-finger swipe

Before the test, go to the phone's home screen, held upright.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, tap once on an empty part of the wallpaper near the
   top of the screen. Wait 2 seconds, and do the same in the middle of
   the screen, then just above the row of app icons at the bottom. Wait
   2 seconds.
3. Put two fingers side by side, about 3 cm apart, on the wallpaper
   just above the app icons, and slide both slowly up together to the
   middle of the screen, taking about 2 seconds, then lift them. If the
   phone opens its list of apps, leave it.
4. Choose "Stop recording", and wait until the recording has opened in
   the player.
5. In the timeline, select a mark of each touch in turn on the second
   lane, as described above, and after each take a screenshot of the
   whole recorder window: the first tap, the second, the third, and
   then the middle of the longer run of marks that is the swipe.

**On screen:** the second lane from the top of the timeline has four
runs of blue marks: three short ones about 2 seconds apart, and a
longer one. The filter that reads "Mouse" for a desktop recording reads
"Touches", the one that reads "Keyboard" reads "Phone keys", and the
status line ends "Phone keys | Touches | UIA | Window | Frames | Mic |
Phone sound". In the first three screenshots the phone's picture has
one yellow circle with a black edge, with no number, near the top, in
the middle, and just above the app icons, where the taps were made. In
the fourth it has two circles side by side, numbered 1 and 2, each with
a yellow line running down below it, the way the fingers came. The two
need not be at the same height: fingers seldom go down level, and
finger 1 is the one that went down first, wherever it is. A circle at a different place than the touch, or none,
is a failure; send the screenshot. Send the screenshots.

### Test 47: a tap with the phone sideways

This test checks that a touch made with the phone turned is drawn where
it was made on the turned picture. Before the test, turn on Auto-rotate
in the phone's quick settings, and open Chrome on any page, with the
phone upright.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. Turn the phone sideways, with its top to the left, and wait until
   Chrome has turned to fill the wide screen, then 3 seconds more.
3. Tap Chrome's menu button, the three dots at the top right of the
   wide screen, once. Leave the menu open, and do not touch the screen
   again.
4. Choose "Stop recording", and wait until the recording has opened in
   the player.
5. In the timeline, select a mark of the last run of marks on the
   second lane, the tap, as described before test 45, and take a
   screenshot of the whole recorder window.

**On screen:** the phone's picture is wide, showing Chrome sideways as
it was on the phone, and one yellow circle is drawn on Chrome's menu
button, the three dots at the top right. A circle anywhere else, such
as at the bottom left or on the left edge, says the turn is wrong; send
the screenshot either way. After the test, close the menu, turn the
phone upright, and set Auto-rotate back as it was.

### Results of part 5, step 5b

- 2026-11-10, test 45: passed. The installer built from `291760d`, and
  the note under "Android device checks" read as expected, copied from
  the panel, up to "Accessibility evidence". The copied text stopped
  there, without "comes in later parts."; that the copy was cut short,
  rather than the note, is inference, as the words are in the same
  sentence of the note's text.
- 2026-11-10, test 46: passed, with three touches more than the test
  asked for, made while setting up the phone, and circles that are
  small at the size the picture was shown. In recording
  `20261110-143257-1fdbc6c5536b401aaf61426e21d9641a`, with only the
  "Touches" filter and the screen reader sound checked, the second lane
  had seven runs of blue marks: three in the first few seconds, the
  three taps 2 to 3 seconds apart, at 20.6, 23.8 and 26.0 s, and the
  swipe, from about 38 to 43 s. The tester confirmed that the first
  three were their own touches, setting up the phone before step 2.
  The filters read "Phone keys" and "Touches", and the status line
  ended "Phone keys | Touches | UIA | Window | Frames | Mic | Phone
  sound". Selecting each tap drew one
  circle without a number near the top, in the middle, and just above
  the bottom row of icons. Selecting the middle of the swipe, at
  40.85 s, drew two circles numbered 1 and 2, each with a yellow line
  below it. They were not at the same height, as the test's check said:
  finger 1, on the right, was higher than finger 2. The tester
  confirmed that this is how the fingers were placed: swiping with the
  index and middle fingers, they seldom go down level, so the check was
  wrong, not the drawing, and it no longer asks for the same height.
  The picture was shown about 157 pixels wide, so each circle, 80 of
  the phone's 1,080 pixels across, was about 12 pixels across on the
  PC's screen, and hard to see. The circles were made larger after this
  test (tests 48 and 49).
- 2026-11-10, test 47: passed. The recording opened, 0:19.832 long, and
  the tap's marks were at about 9.88 s. Selecting the tap, at
  00:00:09.883, showed the picture wide, with Chrome sideways as it was
  on the phone, its buttons along the left edge of the picture and the
  phone's top to the left, and Chrome's menu open. One yellow circle
  was drawn on the menu button, the three dots at the top right. With
  only the "Touches" filter checked, the list showed 64 of the
  recording's 2,819 events, in one run of marks. This shows that the
  turn the player applies, taken from Android 11's `TouchInputMapper`,
  holds on the Pixel's Android 17 for this turn, top to the left. The
  other two turns were not tested.

## Part 5, step 5b: larger circles

Test 46 showed the circles small when the phone's picture is shown
small: about 12 pixels across on the PC's screen with the picture about
157 pixels wide. The circles are still about a fingertip across on the
phone's picture, but now no smaller than 24 of the PC's
device-independent pixels across, with their rings and the paths at
least 2 thick, however small the picture is shown (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 5", "Built, step 5b, larger circles"). They are drawn again at
the right size when the picture's size changes. At 100% display
scaling, a device-independent pixel is one pixel of the screen. The
recordings are not changed: the player draws the circles as it shows a
picture, so test 46's recording shows the new size without being
recorded again.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and step 5b's touch tests still
pass. Neither shows the circles' size on the PC's screen; test 49 does.

### Test 48: build and install the new version (development PC)

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

**On screen:** the build ends with `INSTALLER BUILT`, and the installer
runs to its last page.

### Test 49: test 46's recording with the larger circles

The phone is not needed.

1. Start the recorder from the Start menu, with its window the same
   size as in test 46.
2. Choose "Open recording...", and open the recording of test 46,
   `20261110-143257-1fdbc6c5536b401aaf61426e21d9641a`.
3. In the timeline, select a mark of the first of the three taps, at
   about 20.6 s on the second lane, as described before test 45, and
   take a screenshot of the whole recorder window.
4. Select a mark in the middle of the swipe, at about 40.85 s, and take
   a screenshot of the whole recorder window.

**On screen:** in step 3 the circle is in the same place as in test 46's
first screenshot, and about twice as wide as it was there. In step 4
the two circles are about twice as wide as in test 46's swipe
screenshot, and the numbers 1 and 2 inside them can be read. Send the
screenshots.

## Part 6, step 6a: what TalkBack said, recorded

Part 6 is built in two steps (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 6"). Step 6a runs `logcat` on the phone for the whole recording,
keeping only lines with TalkBack's tag, and reads from them what
TalkBack said: each utterance, each word the speech engine reached, and
each utterance cut off. TalkBack writes what it says to its log only
when its log output level is Verbose, which the recorder cannot set.
While recording, a new line under "Collector status", starting
"android.talkback", counts the utterances and gives the latest. The
player shows the records only in the inspector; the "TalkBack said"
lane is step 6b.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and the tests pass, 9 of them
new. They read check 3's TalkBack lines from the Pixel, saved as a test
file, and find its six utterances ("Session", "O SCANada" and
"Speedtest", each followed by its usage hint), the three usage hints
cut off, and its 42 words. They also cover a recording with a stand-in
adb and phone clock, a line from before the recording, no speech lines,
no phone clock, logcat not starting, and logcat ending. None of that
shows logcat's lines arriving through adb while recording, or their
times on the companion's clock. Tests 50 to 52 do.

### Test 50: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. If the panel offers "Install companion app", choose it, and wait
   until the panel shows "Companion app: installed, on, and answering".
5. Read the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. In step 5 the note
ends "It records every line TalkBack writes to the phone's log, and
what TalkBack said, read from those lines, when TalkBack's log output
level is Verbose. Accessibility evidence comes in later parts."

### Test 51: a recording with TalkBack off

TalkBack stays off, as it usually is.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. While it is still recording, take a screenshot of "Collector status"
   under "Current session".
3. Choose "Stop recording".

**On screen:** in step 2 "Collector status" has a line starting
"android.talkback: Running, Healthy. TalkBack: no speech lines yet,",
then a number of TalkBack log lines, then "If TalkBack is on, set "Log
output level" to "Verbose" in TalkBack's settings, Advanced settings,
Developer settings." Degraded, or no "android.talkback" line, is a
failure; send the line as it is. Send the screenshot.

### Test 52: three swipes with TalkBack on

Before the test, on the phone, open Settings, Accessibility, TalkBack,
Settings, Advanced settings, Developer settings, and set "Log output
level" to "Verbose". Then turn TalkBack on, and go to the home screen.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, swipe right with one finger, three times, waiting 2
   seconds after each swipe.
3. While it is still recording, take a screenshot of "Collector status"
   under "Current session".
4. Choose "Stop recording", and take a screenshot of the line under
   "Current session".
5. Turn TalkBack off. If you prefer, set its "Log output level" back as
   well.

**On screen:** in step 3 "Collector status" has a line starting
"android.talkback: Running, Healthy. TalkBack:", then a number of
utterances, at least 3, a number "interrupted", a number of "words
reached", at least 1, then "latest" and in quotes the name of an item
TalkBack moved to, or the start of its usage hint, such as "Double-tap
to activate.", then a number of TalkBack log lines, and it ends "median
delay from phone to recorder" with a number of milliseconds. The delay
checks the lines' times: a few milliseconds to a few hundred says they
are on the companion's clock; a number below zero, or of seconds or
more, says they are not. "no speech lines yet", "placed by the time each
line arrived", or Degraded is a failure; send the line as it is. In step
4 the line reads "Recording completed and stored. TalkBack's log output
level is still Verbose, which writes what is on the phone's screen to
the phone's log. If you prefer, set it back in TalkBack's settings,
Advanced settings, Developer settings, "Log output level"." Send the
screenshots.

### Results of part 6, step 6a

Run on the development PC with the Pixel on 2026-11-10.

- **Test 50: passed.** The note under "Android device checks" ended
  with the sentence about TalkBack's log lines and "Accessibility
  evidence comes in later parts."
- **Test 51: passed.** With TalkBack off, the line read
  "android.talkback: Running, Healthy. TalkBack: no speech lines yet, 0
  TalkBack log lines.", then the request to set "Log output level" to
  "Verbose".
- **Test 52: failed.** With TalkBack on (the companion's line gave
  Accessibility volume "already 12 of 15 steps (80%)") and three swipes
  recorded as 3 touches, the line still read "no speech lines yet, 0
  TalkBack log lines", during the recording and after Stop, and Stop
  gave no reminder.
- **Diagnostics.** A first diagnostic ran the recorder's logcat command
  on its own during three more swipes, then read the phone's log: it
  held 1,905 lines of TalkBack's process, read with `--pid`, 1,600 of
  them printed with the tag `talkback`, but `logcat -d -s talkback:V`
  found none. A second, run on the phone itself, counted 1,179 lines
  containing " talkback: " with `grep`, and 0 with each of
  `-s talkback:V`, `talkback:V *:S` and `-s talkback`; the bytes of
  such a line showed a plain tag, the 8 letters of `talkback`. So
  TalkBack wrote at Verbose, and logcat's own tag filter, which the
  recorder's command used, printed none of its lines. Why it misses is
  not known (see
  [the implementation plan](../architecture/android-implementation-plan.md),
  "Slice 6", "Fix, step 6a").

## Part 6, step 6a: TalkBack's lines matched on the PC

The recorder now runs logcat with no filter and keeps the lines with
the tag `talkback` itself; other apps' lines are read but not kept. The
status line also counts every line read: "N TalkBack log lines out of N
read from the phone's log".

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and the 9 TalkBack tests pass,
now with another app's line that is read and not kept. They do not show
logcat's whole log arriving through adb from the Pixel; tests 53 and 54
do.

### Test 53: build and install the new version (development PC)

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

**On screen:** the build ends with `INSTALLER BUILT`, and the installer
runs to its last page.

### Test 54: three swipes with TalkBack on, again

Before the test, on the phone, check that TalkBack's "Log output level"
is still "Verbose" (Settings, Accessibility, TalkBack, Settings,
Advanced settings, Developer settings). Then turn TalkBack on, and go
to the home screen. Connect the Pixel by USB, unlocked, start the
recorder from the Start menu, and in "Recording target" choose "Android
device".

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, swipe right with one finger, three times, waiting 2
   seconds after each swipe.
3. While it is still recording, take a screenshot of "Collector status"
   under "Current session".
4. Choose "Stop recording", and take a screenshot of the line under
   "Current session".
5. Turn TalkBack off. If you prefer, set its "Log output level" back as
   well.

**On screen:** in step 3 "Collector status" has a line starting
"android.talkback: Running, Healthy. TalkBack:", then a number of
utterances, at least 3, a number "interrupted", a number of "words
reached", at least 1, then "latest" and in quotes the name of an item
TalkBack moved to, or the start of its usage hint, such as "Double-tap
to activate.", then a number of TalkBack log lines "out of" a larger
number "read from the phone's log", and it ends "median delay from
phone to recorder" with a number of milliseconds. A few milliseconds to
a few hundred says the lines are on the companion's clock; a number
below zero, or of seconds or more, says they are not. "no speech lines
yet" is a failure: then the two counts say whether lines arrived but
were not TalkBack's, or no lines arrived. "placed by the time each line
arrived", or Degraded, is also a failure. Send the line as it is in
either case. In step 4 the line reads "Recording completed and stored.
TalkBack's log output level is still Verbose, which writes what is on
the phone's screen to the phone's log. If you prefer, set it back in
TalkBack's settings, Advanced settings, Developer settings, "Log output
level"." Send the screenshots.

### Results of part 6, step 6a fix

Run on the development PC with the Pixel on 2026-11-10.

- **Test 53: passed.** The new version was built and installed, and the
  recorder started from the Start menu for test 54.
- **Test 54: passed.** In step 3, 20 seconds into the recording, the
  line read "android.talkback: Running, Healthy. TalkBack: 5
  utterances, 2 interrupted, 15 words reached, latest "Double-tap to
  activate", 1006 TalkBack log lines out of 3300 read from the phone's
  log, median delay from phone to recorder 6.8 ms." The same recording
  had 3 touches. After Stop, at 1 minute 9 seconds, the line read
  "android.talkback: Disposed, Healthy." with the same utterance, cut-off
  and word counts, 1074 TalkBack log lines out of 4363 read, and the
  same median delay, and the status began "Recording completed and
  stored. TalkBack's log output level is still Verbose, ...". So
  logcat's whole log arrives through adb from the Pixel, the recorder
  keeps TalkBack's lines from it, and they are placed on the
  companion's clock with a median delay of 6.8 ms. Whether the 5
  utterances are the three items swiped to and their usage hints is for
  step 6b's lane to show.

## Part 6, step 6b: what TalkBack said, shown in the player

In a recording of the phone, the player now shows what TalkBack said in
the Screen reader lane's place, and the filter that reads "Screen
reader" for a desktop recording reads "TalkBack said". Under that lane,
just above the timeline's bottom lane, a teal strip shows each
utterance from its start to the next one, or to its interruption, with
its text where it fits, cut short with "…" where it does not. An
utterance cut off is striped, and its text ends ", interrupted"; where
no text fits, the block reads "interrupted" or nothing. Clicking a
block selects it, and the properties panel's Screen reader group gives
"TalkBack said", the utterance's text in quotation marks, and "Words
reached", the words the speech engine reached in it with their count.
TalkBack's log says when speech started and when it was cut off, not
when it ended, so a block shows how long the utterance was the latest
TalkBack started, not how long it was heard (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Built, step 6b").

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and the tests pass, 7 of them
new. They read check 3's TalkBack lines with step 6a's reader and find
the six utterances, "Session", "O SCANada" and "Speedtest", each
followed by its usage hint, the two hints cut off by the next swipe,
the words reached in each, the blocks' starts and ends, and the
properties panel's rows. They do not show the strip or the rows drawn
on Windows; tests 55 and 56 do.

### Test 55: build and install the new version (development PC)

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

**On screen:** the build ends with `INSTALLER BUILT`, and the installer
runs to its last page.

### Test 56: three swipes shown as what TalkBack said

Before the test, on the phone, check that TalkBack's "Log output level"
is still "Verbose" (Settings, Accessibility, TalkBack, Settings,
Advanced settings, Developer settings). Then turn TalkBack on, and go
to the home screen. Connect the Pixel by USB, unlocked, start the
recorder from the Start menu, and in "Recording target" choose "Android
device". After the test, turn TalkBack off.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, swipe right with one finger, three times, waiting 3
   seconds after each swipe.
3. Choose "Stop recording", wait until the recording has opened in the
   player, and take a screenshot of the whole recorder window.
4. In the timeline, click the first teal block in the strip just above
   the bottom lane. Set "Timeline zoom" to 4×, and take a screenshot of
   the whole recorder window.
5. Click the next block to the right, and take a screenshot of the
   whole recorder window.

**On screen:** in step 3 the filter that reads "Screen reader" for a
desktop recording reads "TalkBack said". Just above the bottom lane of
the timeline is a strip of teal blocks in three groups, each starting
at a run of blue touch marks on the second lane from the top, which are
the swipes. Each group's first block is the item TalkBack moved to, and
the block after it, its usage hint, is striped when the next swipe cut
it off; the last group's hint is solid and runs to the end. The lane
just above the strip has marks at the same times. In step 4 the first
block reads the name of the item TalkBack moved to first, perhaps cut
short with "…", and in the properties panel, under "Screen reader",
"TalkBack said" reads that name in quotation marks and "Words reached"
reads its words and their count, such as "Session (1 word)". In step 5
the block is striped and reads "Double-tap" or as much of it as fits,
then ", interrupted"; "TalkBack said" reads "“Double-tap to activate."
and more, then "”, interrupted at" and a time, and "Words reached"
starts "Double tap to activate" and ends with a count of words. No
strip, a block marked interrupted that the next swipe did not cut off,
or rows that do not match the block selected, is a failure. Send the
screenshots.

### Results of part 6, step 6b

Run on the development PC with the Pixel on 2026-11-10, recording
`20261110-191812-a8b5c937d33d489e82011c272213215d`, 19.5 seconds.

- **Test 55: passed.** The new version was built and installed, and the
  note under "Android device checks" ended with step 6b's sentence on
  "TalkBack said".
- **Test 56: passed, with the expected result corrected.** The test
  expected three app names, each followed by its usage hint. On this
  home screen, TalkBack's first three items were the date and weather
  widget, which have no usage hint, so the strip had three blocks, not
  three pairs: "Home screen 1 of 3. Home. Window Home, interrupted",
  "Tue, Nov 10" and "Partly cloudy day, 12Â°C". The status line agreed:
  3 utterances, 1 interrupted, 11 words reached. Each block started
  just after a swipe's run of touch marks. The first was striped from
  the end of its text to the start of the second swipe, where TalkBack
  cut it off: TalkBack stopped speaking when the finger touched the
  screen, about 0.8 seconds before it started the next item. Clicking
  the first block selected its `utterance-started` record, talkback_1,
  and moved the playhead there. The test did not say to scroll the
  properties panel, where the Screen reader group comes after the
  Magnifier, Display, keyboard and browser rows, all "not recorded" for
  a phone; three further steps were given. With "Tue, Nov 10"
  (talkback_2, caused by `TYPE_VIEW_ACCESSIBILITY_FOCUSED`, the phone's
  green focus box on the date in the picture) selected, "TalkBack said"
  read "“Tue, Nov 10”" with no interruption, set at 00:00:11.252, change
  2 of 3, and "Words reached" read "Tue, Nov 10 (1 word)": the speech
  engine reported one range for the whole date. The filter read
  "TalkBack said". The record was placed with an uncertainty of 5.4 ms
  and arrived 4.3 ms after the phone wrote it.
- **Found: the degree sign read wrongly.** "12°C" was stored and shown
  as "12Â°C", in the status line while recording as well as in the
  player, so the text is wrong as it is read from adb, not as the
  player draws it. The recorder starts adb without naming the encoding
  of its output; "Â°" is the two UTF-8 bytes of "°" read as Windows'
  Western European code page, which suggests that the phone's UTF-8
  text is being decoded with Windows' code page. This is inferred from
  the characters; the code page used has not been read on the PC.
  Not fixed yet.
- **Noted, not changed:** in a phone recording the TalkBack rows are
  below many "not recorded" Windows rows; the properties panel's change
  count ("2/3") is partly hidden at its right edge, with a horizontal
  scroll bar; and the filter "Screen reader sound" is cut off at the
  left panel's edge.

## Part 6, step 6b: adb's output read as UTF-8

The recorder now reads the output of every adb command as UTF-8, the
encoding the phone writes, so "12°C" from TalkBack's log is no longer
stored as "12Â°C" (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Fix, step 6b").

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and the tests pass, 2 of them
new: adb is started with UTF-8 named for its output and errors, and a
line with "12°C" reads back unchanged. On Linux .NET reads UTF-8
anyway, so they do not show the fix on Windows; test 58 does.

### Test 57: build and install the new version (development PC)

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

**On screen:** the build ends with `INSTALLER BUILT`, and the installer
runs to its last page.

### Test 58: the weather's degree sign

Before the test, on the phone, check that TalkBack's "Log output level"
is still "Verbose" (Settings, Accessibility, TalkBack, Settings,
Advanced settings, Developer settings). Then turn TalkBack on, and go
to the home screen. Connect the Pixel by USB, unlocked, start the
recorder from the Start menu, and in "Recording target" choose "Android
device". After the test, turn TalkBack off.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, swipe right with one finger, three times, waiting 3
   seconds after each swipe.
3. While it is still recording, take a screenshot of "Collector status"
   under "Current session".
4. Choose "Stop recording", wait until the recording has opened in the
   player, and take a screenshot of the whole recorder window.

**On screen:** in step 3 the line starting "android.talkback: Running,
Healthy." gives "latest" and, in quotes, the weather item, such as
"Partly cloudy day, 12°C", with the temperature followed by "°C". In
step 4 the last teal block in the strip just above the timeline's
bottom lane reads the same. "Â" before the degree sign in either is a
failure. If the third item is not the weather, say what it was and
send the screenshots anyway. Send the screenshots.

### Results of part 6, step 6b fix

Run on the development PC with the Pixel on 2026-11-10.

- **Test 57: passed.** The installer `WindowsA11yRecorder-Setup-2419123.exe`
  was built from `2419123` at 4:03 PM.
- **Test 58: passed, by the owner's own run.** In the run following the
  test's steps (recording
  `20261110-214532-0754b6659aee44f4b0e4c6861a6dbd64`), the status line's
  latest utterance at 32 seconds was a usage hint, "Actions available,
  use Tap with 3 fingers to view", with 5 utterances, 3 interrupted and
  21 words reached, so it did not show the weather. In a further run of
  the owner's own, a block in the TalkBack said strip read "Mostly
  clear. 9°", with the degree sign and no "Â". So text from the phone
  outside ASCII is now read as it was written.

## Part 7, step 7a: the phone's accessibility events

The companion app now sends the recorder every accessibility event its
service receives, with the item each came from, and the recorder stores
them (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 7", "Built, step 7a"). While recording, a new line under
"Collector status", starting "android.accessibility", counts the events
and names the item last given accessibility focus. The player shows the
records only in the inspector; the tree and the events lane are steps
7b and 7c. The companion app now speaks protocol 3, so it has to be
installed again.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, the companion app compiles with
warnings as errors, and the tests pass, 2,122 in all, with 13 new tests
(18 cases) and 1 changed. They read an event as the companion app writes
it, with every field, and cover a recording with a stand-in companion
connection and phone clock: the events' times, an event from before the
recording, an event that cannot be read, no phone clock, no companion
connection, the connection ending, and the record's checks; and a
stand-in companion app sent the request and passing the events on. None
of that shows the app reading events on the Pixel, or their times on the
companion's clock. Tests 59 and 60 do.

### Test 59: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. The panel offers "Install companion app". Choose it, and wait until
   the panel shows "Companion app: installed, on, and answering".
5. Read the companion line and the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. Before step 4 the
companion line says the installed version code is older than the
recorder's. In step 5 it reads "Companion app: installed, on, and
answering (version 1.1.0, version code" with a number, and ends
"protocol 3)." The note ends "It also records every accessibility event
the phone sends to accessibility services, with the item each one came
from. The accessibility tree and the player's view of the events come in
later parts."

### Test 60: three swipes through Settings with TalkBack on

Before the test, on the phone, open Settings, scroll to the top, then
turn TalkBack on. Leave the recorder open from test 59, with "Android
device" chosen. After the test, turn TalkBack off.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, swipe right with one finger, three times, waiting 2
   seconds after each swipe.
3. While it is still recording, take a screenshot of "Collector status"
   under "Current session".
4. Choose "Stop recording".

**On screen:** in step 3 "Collector status" has a line starting
"android.accessibility: Running, Healthy. Accessibility events:", then a
number of events, a number of content changes with "at most" a number
"in one second", a number of accessibility focus events, at least 3, and
a number of window changes. Then "latest accessibility focus on" and, in
quotes, the words of the third item TalkBack moved to, such as its name
followed by its summary; then "the app took a median" a number of
milliseconds "to read each", and it ends "median delay from phone to
recorder" with a number of milliseconds. The delay checks the events'
times: a few milliseconds to a few hundred says they are on the
companion's clock; a number below zero, or of seconds or more, says they
are not. "none yet", "placed by the time each event arrived", Degraded,
or no "android.accessibility" line is a failure; send the line as it is.
"an item with no text" in place of the quoted words is not a failure,
but say so. Send the screenshot.

### Results of part 7, step 7a

Run on the development PC with the Pixel on 2026-11-10.

- **Test 59: passed.** The note under "Android device checks" ended
  with the sentence about accessibility events, and the panel showed
  the companion app installed and on. The recorder refuses a companion
  app that speaks protocol 2, so the app answering means the new one
  was installed.
- **Test 60: passed.** At 37 seconds the line read
  "android.accessibility: Running, Healthy. Accessibility events: 16,
  including 0 content changes (at most 0 in one second), 4 accessibility
  focus events and 1 window change, latest accessibility focus on
  "Current weather", the app took a median 1.8 ms to read each, median
  delay from phone to recorder 28.0 ms." The delay is within the range
  that shows the events on the companion's clock. It is larger than the
  touches' 9.2 ms and TalkBack's lines' 4.4 ms in the same recording;
  where the time goes is not yet known. The swipes were on the home screen
  rather than in Settings, as the owner turns TalkBack on with a
  shortcut; "Current weather" is an item of its weather widget. That
  does not change the result, which needed only three moves of
  TalkBack's focus. With no content changes, the rate step 7b needs is
  not yet measured.

## Part 7, step 7b: snapshots of the phone's accessibility tree

The companion app now takes snapshots of the phone's accessibility tree
and the recorder stores them (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 7", "Built, step 7b"). A snapshot of every window is taken as the
recording starts and on each window change, and a snapshot of a window
when its content changes. While recording, a new line under "Collector
status", starting "android.accessibility-tree", counts the snapshots and
gives their size and the time each took. The player shows the records
only in the inspector; the tree view is step 7c. The companion app now
speaks protocol 4, so it has to be installed again.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, the companion app compiles with
warnings as errors, and the tests pass, 2,142 in all, with 10 new tests
(21 cases) and 1 changed. They read a snapshot as
the companion app writes it, with two windows and items linked by
parent, label and traversal, and cover a recording with a stand-in
companion connection and phone clock: the snapshots' times, one from
before the recording, a missing one, one that cannot be read, no phone
clock, no companion connection, the connection ending, and the record's
checks; and a stand-in companion app sent the request and passing the
snapshots on. None of that shows the app taking snapshots on the Pixel,
how large they are, how often they come or how long each takes. Tests
61 and 62 do, and test 62 also gives the rate of content changes that
step 7a could not measure.

### Test 61: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. The panel offers "Install companion app". Choose it, and wait until
   the panel shows "Companion app: installed, on, and answering".
5. Read the companion line and the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. Before step 4 the
companion line says the installed version code is older than the
recorder's. In step 5 it reads "Companion app: installed, on, and
answering (version 1.1.0, version code" with a number, and ends
"protocol 4)." The note ends "It also records every accessibility event
the phone sends to accessibility services, with the item each one came
from, and snapshots of the phone's accessibility tree: every window's
items as the recording starts and when a window changes, and a window's
items again when its content changes. The player's view of the events
and the tree comes in a later part."

### Test 62: scrolling Settings with TalkBack on

Before the test, on the phone, open Settings, scroll to the top, then
turn TalkBack on. Leave the recorder open from test 61, with "Android
device" chosen. After the test, turn TalkBack off.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, swipe up with two fingers, three times, waiting 2
   seconds after each swipe, so that the Settings list scrolls down.
3. While it is still recording, take a screenshot of "Collector status"
   under "Current session".
4. Choose "Stop recording".

**On screen:** in step 2 the Settings list moves up after each swipe.
In step 3 "Collector status" has a line starting
"android.accessibility-tree: Running, Healthy. Accessibility tree
snapshots:", then a number of snapshots, at least 2, with "at most" a
number "in one second"; a number "of every window", at least 1; "the
latest with" a number of windows and a number of items, at least 1
each; "the largest" a number of items and a number of KB; then "the app
took a median" a number of milliseconds "to take each and at most" a
number of milliseconds; and it ends "median delay from phone to
recorder" with a number of milliseconds. The delay checks the
snapshots' times: a few milliseconds to a few hundred says they are on
the companion's clock; a number below zero, or of seconds or more, says
they are not. The "android.accessibility" line above it gives a number
of content changes, with "at most" a number "in one second". "none
yet", "could not be read", "missing", "placed by the time each
arrived", Degraded, or no "android.accessibility-tree" line is a
failure; send the line as it is. "cut short" or "stopped early" is not
a failure, but say so. Send the screenshot.

### Results of part 7, step 7b

Run on the development PC with the Pixel on 2026-11-10.

- **Test 61: passed.** The note under "Android device checks" ended
  with the sentence about snapshots of the accessibility tree, and the
  panel showed the companion app installed and on. The recorder refuses
  a companion app that speaks protocol 3, so the app answering means the
  new one was installed.
- **Test 62: passed.** The line read "android.accessibility-tree:
  Running, Healthy. Accessibility tree snapshots: 11 (at most 5 in one
  second), 2 of every window, the latest with 4 windows and 162 items,
  the largest 209 items and 274 KB, the app took a median 302.8 ms to
  take each and at most 374.1 ms, median delay from phone to recorder
  28.9 ms." The "android.accessibility" line above it read
  "Accessibility events: 64, including 33 content changes (at most 18
  in one second), 1 accessibility focus event and 1 window change", with
  the latest accessibility focus on "Search Settings".
  - **The snapshots' times.** The 28.9 ms delay is within the range that
    shows the snapshots on the companion's clock, and close to the
    events' 14.5 ms and 7a's 28.0 ms.
  - **The rate of content changes.** This is the rate step 7a could not
    measure: 33 content changes in a short recording, at most 18 in one
    second, while scrolling Settings.
  - **Coalescing.** The 2 snapshots of every window are the one at the
    start and the one for the window change. The other 9 covered the 33
    content changes, so changes that arrived while a snapshot was taken
    were covered by the next, as designed.
  - **The time each took.** A median 302.8 ms, at most 374.1 ms, for up
    to 209 items. That time covers reading the windows and items and
    building the message, not writing it out. Where the time goes is not
    yet known. It means a snapshot shows the tree as it was over about
    a third of a second, and content that changed and changed back
    within that time may not appear in any snapshot. The events in
    `accessibility.android.events` still record each change.
  - **Size.** The largest snapshot was 274 KB for 209 items, about 1.3 KB
    an item.

## Part 7, step 7c: the phone's accessibility tree in the player

The player now shows the phone's accessibility events and tree (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 7", "Built, step 7c"). In a recording of the phone, the events
and snapshots are on the third lane from the top, the one UI Automation
has for a desktop recording, named "Accessibility" in the legend under
the timeline, and the filter that reads "UI Automation" for a desktop
recording reads "Accessibility events". The "Accessibility tree" tab
shows the phone's tree at the playhead: a row for each window, with its
items under it, and the item with accessibility focus marked
"accessibility focus" in bold, with a purple bar on its left. "Show
accessibility focus" expands the tree down to that item and scrolls to
it. Clicking an item shows its fields in "Selected event values". On
the phone's picture, a yellow rectangle with a black edge is drawn
around the item with accessibility focus. The companion app is
unchanged, but the installer builds it with a new version code, so the
panel offers to install it again.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and the tests pass, 2,155 in all,
with 11 new. They make records from the step 7a and 7b lines, with a
Settings window whose row holds "Network & internet", and check what the
playback index keeps, the tree before and at each snapshot, the item
with accessibility focus found in the tree, its newer bounds after the
list scrolled, a window's items kept from an earlier snapshot, the focus
cleared, the tree read without the recording, the screen's size as it
was turned, and the event list's text. None of that shows the lane, the
tree or the outline drawn on Windows, or that they match what TalkBack
spoke on the Pixel. Tests 63 to 67 do.

### Test 63: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. The panel offers "Install companion app". Choose it, and wait until
   the panel shows "Companion app: installed, on, and answering".
5. Read the companion line and the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. In step 5 the
companion line reads "Companion app: installed, on, and answering
(version 1.1.0, version code" with a number, and ends "protocol 4)."
The note ends "The player shows the events and the snapshots as
"Accessibility events" in UI Automation's place, draws a yellow outline
on the phone's picture around the item with accessibility focus, and
shows the tree at the playhead in the Accessibility tree tab, with that
item marked "accessibility focus"."

### Test 64: three swipes through Settings and opening a page

Before the test, on the phone, open Settings, scroll to the top, then
turn TalkBack on. Leave the recorder open from test 63, with "Android
device" chosen. After the test, turn TalkBack off. Tests 65 to 67 use
the recording this test makes.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, swipe right with one finger, three times, waiting 3
   seconds after each swipe.
3. On the phone, double-tap with one finger to open the item TalkBack
   is on, and wait 5 seconds.
4. Choose "Stop recording", wait until the recording has opened in the
   player, and take a screenshot of the whole recorder window.
5. In the recorder's left panel, scroll down until you can see the
   "Playback event filters" box, and take a screenshot of the whole
   recorder window.

**On screen:** in step 2 TalkBack moves to the next item of Settings
after each swipe, and in step 3 that item's page opens. In step 4 the
legend under the timeline reads "Phone keys | Touches | Accessibility |
Window | Frames | Mic | Phone sound". The touches are the larger blue
squares in the top row of the timeline, one for each swipe and two for
the double-tap; a touch to wake the screen adds one more before them.
The third lane from the top has orange marks at each of them. The thin
light-blue ticks just above the teal strip are TalkBack's log lines, not
touches. In step 5 the filters include "Accessibility events" and no
"UI Automation". No marks on the third lane, or "UIA" in the legend, is
a failure. Send the screenshots.

### Test 65: the first item TalkBack spoke

The recording from test 64 is open in the player.

1. In the timeline, click the first teal block in the strip just above
   the bottom lane, and take a screenshot of the whole recorder window.
2. Click the "Accessibility tree" tab, choose "Show accessibility
   focus", and take a screenshot of the whole recorder window.
3. Click once on the words of the row marked "accessibility focus", not
   on the arrow button to their left, and take a screenshot of the whole
   recorder window.

**On screen:** in step 1 the properties panel, under "Screen reader",
has "TalkBack said" with the name of the first item TalkBack moved to
(the item under the finger, if a touch woke the screen),
and on the phone's picture a yellow rectangle with a black edge is
drawn around that item. In step 2 the tab's line starts with a number
of windows and a number of items, and the tree is open down to a row
with "accessibility focus" in bold under it and a purple bar on its
left. The row's first line names a kind of item, such as
"LinearLayout", then the name TalkBack said in quotation marks, after
"containing" when the item has no text of its own. In step 3 "Selected
event values" starts with the row's first line, then "An item of the
snapshot of the phone's tree taken at" and a time, then the item's
fields, among them "index" and "bounds". No rectangle, a rectangle
around another item, no row marked "accessibility focus", or the line
"No item is known to have accessibility focus at the playhead." is a
failure. Send the screenshots.

### Test 66: the outline follows the third swipe

The recording from test 64 is open in the player, with the
"Accessibility tree" tab chosen.

1. In the top row of the timeline, find the blue square of the third
   swipe: the third square, or the fourth if a touch woke the screen.
   Click the first teal block in the strip just above the bottom lane
   that starts to the right of it, and take a screenshot of the whole
   recorder window.
2. Choose "Show accessibility focus", and take a screenshot of the
   whole recorder window.

**On screen:** in step 1 "TalkBack said" in the properties panel, or the
status line at the bottom of the window, has the name of the item TalkBack moved
to after the third swipe, and on the phone's picture the yellow
rectangle is around that item. In step 2 the row marked "accessibility
focus" names that item. A rectangle still around the item of test 65,
or a marked row that names a different item, is a failure. Send the
screenshots.

### Test 67: the page opened, in the tree

The recording from test 64 is open in the player, with the
"Accessibility tree" tab chosen.

1. Drag the playback slider under the phone's picture all the way to
   the right, and take a screenshot of the whole recorder window.
2. In the tree, click the arrow button of the row for the page you
   opened, the one starting "Window" with the page's name in quotation
   marks and "TYPE_APPLICATION, active", and take a screenshot of the
   whole recorder window.

**On screen:** in step 1 the phone's picture shows the page opened in
test 64. The tree has a row for each window, each starting "Window",
each with "items taken at" and a time under it. One of them names the
page, with "TYPE_APPLICATION, active, focused, has accessibility focus,
window" and a number in brackets. In step 2 the rows under that window
include the page's title or its first items, as the picture shows them.
A tree still showing only the Settings list from before the double-tap
is a failure. Send the screenshots.

### Results of part 7, step 7c

Run on the development PC with the Pixel on 2026-11-10. The tests above
are as corrected after the run: test 64 gained step 5, and the wording
of tests 65 to 67 was made exact where it was unclear.

- **Test 63: passed.** The panel showed "Companion app: installed, on,
  and answering (version 1.1.0, version code 749, protocol 4)", and the
  note ended with the sentence about "Accessibility events", the yellow
  outline and the Accessibility tree tab.
- **Test 64: passed.** The legend read "Phone keys | Touches |
  Accessibility | Window | Frames | Mic | Phone sound", and the filters
  showed "Phone keys", "Touches" and "Accessibility events", with no "UI
  Automation", 6,596 of 6,596 events shown. The third lane had orange
  marks at every touch, and others between them for content changes.
  There were five touches, not four: the owner touched the screen to wake
  it before the swipes, and TalkBack put accessibility focus on the item
  under the finger, "Digital Wellbeing". The recording was 33.85 seconds.
  The plan as first written left the filter check unreachable (the
  filters are in the left panel, below the note), and called the touches
  "groups"; both are corrected above.
- **Test 65: passed.** At the first utterance, 00:00:02.727, "Digital
  Wellbeing. Screen time, app timers, bedtime schedules. 23 of 29", the
  yellow rectangle was around "Digital Wellbeing", and "Show
  accessibility focus" opened the tree ("4 windows, 209 items") down to
  "LinearLayout containing "Digital Wellbeing", "Screen time, app timers,
  bedtime schedules"", marked "accessibility focus" with the purple bar.
  Clicking its words showed the label, "An item of the snapshot of the
  phone's tree taken at 00:00:02.573", and the item's fields, with
  `"accessibilityFocused": true`, bounds from 1177 to 1356 pixels down
  the 2404-pixel screen (where the rectangle was drawn), and
  `"rowIndex": 22`, the 23rd row, as TalkBack said "23 of 29". The first
  click landed off the words and showed nothing new; step 3 now says
  where to click.
- **Test 66: passed.** After the third swipe, at 00:00:13.306, the
  status line read "utterance-started: Safety and emergency. Emergency
  SOS, medical info, alerts. 26 of 29", the rectangle was around "Safety
  and emergency", and the marked row was "LinearLayout containing
  "Safety and emergency", "Emergency SOS, medical info, alerts"". The
  utterance's record gives its cause as
  `TYPE_VIEW_ACCESSIBILITY_FOCUSED` on the phone 29 ms before it
  started. The phone's own speech caption at the bottom of the picture
  still showed the previous item, "Magic Cue", at that moment; the phone
  seems to redraw it a little after the speech starts (an inference, not
  measured).
- **Test 67: passed.** At the end, 00:00:33.769, the picture showed the
  "Safety and emergency" page with the rectangle around "Navigate up",
  and the tree read "4 windows, 134 items", each window with "items
  taken at 00:00:17.020", the snapshot of the window change. The page's
  window read "Window "Safety and emergency" (TYPE_APPLICATION, active,
  focused, has accessibility focus, window 1343)", and its rows held
  "FrameLayout containing "Safety and emergency", "Navigate up", "Safety
  and emergency"" and the layouts under it. The first window row is a
  system window holding "FrameLayout containing "Tap a feature to start
  using it:", "TalkBack"", which the first wording of step 2 led to.
  - **Found, not fixed: text contrast.** The tree's row text, and the
    properties panel's text, are drawn in the player's light text colour
    (#CDCCCA, measured as 205, 204, 202) on those controls' white
    background, a contrast of about 1.6:1 against WCAG's 4.5:1. The
    window's styles give all text that colour since commit 208af19
    (2026-10-14); controls that keep Windows' white background inherit
    it. This is not new in step 7c and is the same for desktop
    recordings. The owner noted that it is throughout the interface; it
    is left for a contrast pass over the whole interface.

## Part 8, step 8a: the phone's settings

The recorder now records the phone's accessibility settings (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 8", "Built, step 8a"): 34 of them, such as color inversion,
magnification, font size and the accessibility services on. It reads
them as the recording starts and stops, and again each time the
companion app says one was written. While recording, a new line under
"Collector status", starting "android.settings", counts the changes. In
the player, the properties panel shows the phone's settings in groups
whose names start "Phone", in place of the Magnifier and Windows rows,
and the filter that reads "Windows settings" for a desktop recording
reads "Phone settings". The companion app now speaks protocol 5, so it
has to be installed again.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, the companion app compiles with
warnings as errors, and the tests pass, 2,179 of them, with the 10 that
need Windows failing as before. The new tests cover the request and the
notices, what `adb shell settings list` prints, each setting's value in
words, and a recording with a stand-in adb, companion connection and
phone clock: color inversion turned on and then off, with the panel's
row before, at and after each change; a notice with no change; no
companion, with the change found at the stop; and settings that cannot
be read. None of that shows the companion app's notices arriving on the
Pixel, or the panel drawn on Windows. Tests 68 to 71 do.

### Test 68: build and install the new version (development PC)

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
3. Connect the Pixel by USB, unlocked. Start the recorder from the Start
   menu, and in "Recording target" choose "Android device".
4. The panel offers "Install companion app". Choose it, and wait until
   the panel shows "Companion app: installed, on, and answering".
5. Read the companion line and the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. In step 5 the
companion line reads "Companion app: installed, on, and answering
(version 1.1.0, version code" with a number, and ends "protocol 5)."
The note ends "It records the phone's accessibility settings, such as
color inversion, magnification, font size and the services on, as the
recording starts and stops and each time one changes, and the
properties panel shows them at the playhead, in the Windows settings'
place, with "Phone settings" as their filter." "protocol 4" is a
failure. Send a screenshot of the panel.

### Test 69: color inversion on and off while recording

Before the test, check that TalkBack is off. On the phone, open
Settings, then Accessibility, then "Color and motion", then "Color
inversion", so that the "Use color inversion" switch is on the screen,
and off. Leave the recorder open from test 68, with "Android device"
chosen. Tests 70 and 71 use the recording this test makes.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, tap "Use color inversion" to turn it on, and wait 5
   seconds.
3. Tap "Use color inversion" again to turn it off, and wait 5 seconds.
4. While it is still recording, take a screenshot of "Collector status"
   under "Current session".
5. Choose "Stop recording", and wait until the recording has opened in
   the player.

**On screen:** in step 2 the phone's own screen turns to inverted
colors, and in step 3 it turns back. In step 4 "Collector status" has a
line starting "android.settings: Running, Healthy. Phone settings: 34
read at the start, 2 changes, latest Color inversion off". It may go on
with a number of "notices with no change". No "android.settings" line,
Degraded, "0 changes", or "1 change" is a failure; send the line as it
is. More than 2 changes, or a latest change that is not color
inversion, is not a failure, but say so. Send the screenshot.

### Test 70: the Color inversion row at the two times

The recording from test 69 is open in the player, at its start.

1. In the "Properties" panel to the right of the phone's picture,
   scroll down until you can see the group "Phone color and contrast",
   and take a screenshot of the whole recorder window.
2. On the "Color inversion" row, click the right-pointing triangle
   button ("Next color inversion change"), and take a screenshot of the
   whole recorder window.
3. Click the same button again, and take a screenshot of the whole
   recorder window.

**On screen:** in step 1 the "Color inversion" row reads "off" and, in
"When set", "at start", and it has two triangle buttons at its end. In
step 2 the playhead moves to about 5 seconds after the start, and the
row, in bold, reads "on", with a time of about 00:00:05 in "When set",
followed by ", changed". The phone's picture shows the "Use color
inversion" switch on, or a frame before it moves. In step 3 the row
reads "off", with a time about 5 seconds later than step 2's, followed
by ", changed". "not set", "not read", "found at the stop", no triangle
buttons, or a second time no later than the first, is a failure. The
change count to the right of the buttons may be cut off at the panel's
edge; that is a known fault, not part of this test. Send the
screenshots.

### Test 71: the phone's groups and filter

The recording from test 69 is open in the player.

1. In the "Properties" panel, scroll to the top, and take a screenshot
   of the whole recorder window.
2. In the recorder's left panel, scroll down until you can see the
   "Playback event filters" box, and take a screenshot of the whole
   recorder window.

**On screen:** in step 1 the first group is "Phone accessibility", and
its rows include "Accessibility in use" reading "on" and "Services on"
reading "RecorderAccessibilityService". Below come the groups "Phone
magnification" and "Phone color and contrast". There is no "Magnifier"
or "Display" group. Groups further down, which are not the phone's
settings, may have rows that read "not recorded"; they are not part of
this test. In step 2 the filters include "Phone settings" and no
"Windows settings". Send the screenshots.

## Results of part 8, step 8a

Run on the development PC and the Pixel on 2026-11-11, from commit
2340ed8. All four tests passed, and step 8a is complete.

- **Test 68 passed.** "The companion app is installed and on.", then
  "Companion app: installed, on, and answering (version 1.1.0, version
  code 753, protocol 5)." The note ends with the new sentence.
- **Test 69 passed.** While recording: "android.settings: Running,
  Healthy. Phone settings: 34 read at the start, 2 changes, latest Color
  inversion off.", with no notices with no change and no failed reads.
  Every other collector was Running and Healthy. The touch line counted
  3 touches for the two taps; touches are not part of this test.
- **Test 70 passed, with one wrong expectation in the test.** At the
  start the "Color inversion" row read "not set", not "off", with "at
  start" and the two buttons. "not set" means the phone's secure table
  had no value for the setting, most likely because the switch had never
  been used on this phone; the test was wrong to list it as a failure.
  Android reading a missing value as off is understood, not checked in
  its source here. Settings that were probably never used, such as
  "Color correction", "High contrast text" and "Bold text", read "not
  set" the same way. The first step went to "on", "00:00:12.609,
  changed", "1/2", in bold, with the playback clock at 00:00:12.609; the
  second to "off", "00:00:22.395, changed", "2/2", with the next button
  greyed out. The times are later than the test's "about 5 seconds"
  because the recording starts a few seconds after "Start recording" is
  chosen; the row's time and the clock agree.
- **Observed in test 70.** At each change the phone's picture still
  showed the switch in its old position, so the setting was written
  before the switch was redrawn (an inference from two screenshots, not
  a measurement). The picture was not drawn in inverted colours, though
  the phone's screen was, which fits color inversion being applied at
  the display's output, after the screen is captured (an inference). The
  black picture at 00:00:00.000 is the fault noted before.
- **Test 71 passed.** The panel starts with "Phone accessibility":
  "Accessibility in use" on, "Services on"
  "RecorderAccessibilityService", "Explore by touch" off, "Accessibility
  button" "TalkBackService" (the owner's TalkBack shortcut), then "Phone
  magnification" and "Phone color and contrast", with no Magnifier or
  Display group. The filters show "Phone settings" and "Magnifier
  changes", and no "Windows settings".
- **Noted, not changed.** "RecorderAccessibilityService" breaks in the
  middle of the word, as it has no spaces. The owner may later choose
  whether a switch with no value reads "off (not set)" in place of "not
  set".

## Part 8, step 8b: the one-timeline check

Step 8b checks that the phone's sources fall on one timeline (see
[the implementation plan](../architecture/android-implementation-plan.md),
"Slice 8", "One-timeline check"). In one recording a double tap with
TalkBack opens an app, and five times are compared on the session
clock: the finger's touch-down, the accessibility event for the new
window, the first picture that changes, the start of what TalkBack said,
and the start of the phone's sound. The owner chose (2026-11-11) that
the comparison is not built into the recorder: the owner makes the
recording, and the recording's folder is read from the development PC
and compared outside the recorder. Nothing new is built, so this test
uses the version installed for test 68.

### Test 72: a double tap opens an app

Before the test, on the phone, turn TalkBack on, and check that its "Log
output level" is "Verbose" (TalkBack's settings, then "Advanced
settings", then "Developer settings"). Go to the home screen, so that an
app's icon, such as Chrome's, is on the screen. In the recorder, keep
"Android device" chosen.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, tap the app's icon once, and wait 3 seconds.
3. Double tap the app's icon, and wait 5 seconds.
4. While it is still recording, take a screenshot of "Collector status"
   under "Current session".
5. Choose "Stop recording", and wait until the recording has opened in
   the player.

**On screen:** in step 2 TalkBack's focus outline goes around the icon.
In step 3 the app opens. In step 4 every line reads "Running, Healthy";
the "android.talkback" line reads "TalkBack:" with at least 2
utterances, not "no speech lines yet"; and the "android.touch" line
counts at least 3 touches. Send the screenshot, and say which app was
opened. The comparison is then made from the recording's folder.

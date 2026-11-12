# Validation plan: Android checks, steps 1 and 2 (screens and visits; density and versions)

These tests are step 1 of [Android checks](../architecture/android-checks.md),
"Build order": the player dividing a phone recording into apps, screens,
visits and states, shown in a new "Checks" tab and a strip on the
timeline. No check of the app is built yet, so the tab lists no
findings.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings; the new tests pass, one for each
rule, the first made from test 64's measured windows and events; and
test 64's own recording, read by a throwaway program, gave the two
screens and two visits that test 2 expects. For the second build
(2026-11-12), the same program read the client's recording and gave the
rows test 4 expects, and test 64's again, with the settled counts test 2
now expects.

TalkBack is not needed for these tests; leave it off.

## Test 1: build and install the new version (development PC)

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

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 2: test 64's recording in the Checks tab

The recording of test 64 of the Android recording plan is
`20261111-002648-06be83ce21ec47309bd4bc68b4ae1cb4` (Settings, then
"Safety and emergency").

1. Choose "Open recording...", and open that recording.
2. In the tabs under the timeline, click "Checks".
3. Take a screenshot of the whole recorder window.

**On screen:** the line beside "Show current visit" reads "1 app, 2
screens, 2 visits. Before the first visit." The list has five rows, in
this order:

- "App com.android.settings: 2 screens, 2 visits"
- "Screen 1: Settings (com.android.settings), named by its window
  title, activity not given by a window-state event, 1 visit"
- "Visit 1, 00:00:01.208 to 00:00:16.511, started by the recording's
  first app window; 7 snapshots, 4 settled"
- "Screen 2: Safety and emergency (com.android.settings), named by its
  window title, activity SubSettings, 1 visit"
- "Visit 2, 00:00:16.511 to" the recording's end time, ", started by a
  new window; 5 snapshots, 1 settled"

The app and screen rows are in bold, and each level is indented further
than the one above it. Long rows wrap; there is no horizontal
scrollbar. Under the timeline's teal strip, where TalkBack's speech is,
there is a brown strip with two blocks: "Settings" from about 1 second
and "Safety and emergency" from about 16.5 seconds. Send the
screenshot.

## Test 3: a visit's row moves the playhead

The recording from test 2 is open, with the "Checks" tab chosen.

1. Click the row starting "Visit 2".
2. Take a screenshot of the whole recorder window.

**On screen:** the time under the phone's picture starts
"00:00:16.511". The "Visit 2" row is in bold with a purple bar on its
left, and the line beside "Show current visit" ends "Visit 2, to Safety
and emergency (com.android.settings), at the playhead." The timeline's
playhead is at the left edge of the "Safety and emergency" block. Send
the screenshot.

## Results of the first build (2026-11-12)

Tests 1 to 3 passed on the development PC, with these differences, none
a fault: in test 2 the status line named Visit 1, as the playhead was at
00:00:10.785 when the recording opened, not before the first visit; and
the Checks tab was open in its own window. Visit 1 showed "7 snapshots,
7 settled" and Visit 2 "5 snapshots, 2 settled", as expected then.

Test 4 recorded the client's app (session
`20261112-005243-5528d67531dd4441a50b2c2985350c59`; the owner's list:
Home, Learn, Understanding Melatonin, Diary, the Good morning card).
Test 5 failed: the Checks tab showed 2 apps, 2 screens and 3 visits,
all four of the app's screens as one screen, "Nova SleepCare". The
cause, measured in the recording, and the rules added for it are in
[Android checks](../architecture/android-checks.md), "Measured in the
client's app". The tests below are for the second build. Test 2's
settled counts are lower than in the first build because of the half
second rule.

## Test 4: the client's app in the Checks tab (second build)

Test 1 has been run again with the second build.

1. Choose "Open recording...", and open
   `20261112-005243-5528d67531dd4441a50b2c2985350c59`.
2. If the Checks window is closed, click the "Checks" tab.
3. Take a screenshot of the whole recorder window. If the list does not
   fit, scroll it down and take another, until every row has been in a
   screenshot.

**On screen:** the line beside "Show current visit" begins "2 apps, 5
screens, 6 visits." The list has these rows, in this order:

- "App com.pss.novasleepcare: 4 screens, 5 visits"
- "Screen 1: Welcome, Bob Dodd (com.pss.novasleepcare), named by its
  first fixed text, as 4 screens share the title 'Nova SleepCare',
  activity not given by a window-state event, 1 visit"
- "Visit 1, 00:00:02.060 to 00:00:08.949, started by the recording's
  first app window; 1 snapshot, 1 settled"
- "Screen 2: Sleep Learning Library (com.pss.novasleepcare), named by its
  first fixed text, as 4 screens share the title 'Nova SleepCare',
  activity not given by a window-state event, 1 visit"
- "Visit 2, 00:00:08.949 to 00:00:13.925, started by a change of first
  fixed text; 49 snapshots, 1 settled"
- "Screen 3: Sleep Learning (com.pss.novasleepcare), named by its first
  fixed text, as 4 screens share the title 'Nova SleepCare', activity
  not given by a window-state event, 1 visit"
- "Visit 3, 00:00:13.925 to 00:00:19.678, started by a change of first
  fixed text; 42 snapshots, 2 settled"
- "Screen 4: Sleep Diary (com.pss.novasleepcare), named by its first
  fixed text, as 4 screens share the title 'Nova SleepCare', activity
  MainActivity, 2 visits"
- "Visit 4, 00:00:19.678 to 00:00:42.189, started by a change of first
  fixed text; 52 snapshots, 4 settled"
- "In pane 'Dialog', first fixed text 'Last Night's Sleep',
  00:00:23.157 to 00:00:41.307"
- "Visit 6, 00:00:44.135 to 00:01:00.793, started by another app coming
  to the front, a return; 3 snapshots, 1 settled"
- "App com.google.android.apps.nexuslauncher: 1 screen, 1 visit"
- "Screen 5: Home (com.google.android.apps.nexuslauncher), named by its
  window title, activity NexusLauncherActivity, 1 visit"
- "Visit 5, 00:00:42.189 to 00:00:44.135, started by another app coming
  to the front; 12 snapshots, 2 settled"

The brown timeline strip has six blocks: "Welcome, Bob Dodd" from about
2 seconds, "Sleep Learning Library" from about 9, "Sleep Learning" from
about 14, "Sleep Diary" from about 20, "Home" from about 42 and "Sleep
Diary" from about 44, each label cut short where its block is too
narrow. Send the screenshots.

## Test 5: a visit started by a change of first fixed text

The recording from test 4 is open, with the Checks list showing.

1. Click the row starting "Visit 3".
2. Take a screenshot of the whole recorder window.

**On screen:** the time beside the position slider starts
"00:00:13.925". The "Visit 3" row has the purple bar on its left, and the line
beside "Show current visit" ends "Visit 3, to Sleep Learning
(com.pss.novasleepcare), at the playhead." Send the screenshot.

## Results of the second build (2026-11-12)

Tests 4 and 5 passed on the development PC with commit fa342f1. In test
4 every row matched, in order; the status line named Visit 6, as the
playhead was at 00:00:57.020; the timeline strip's last two blocks were
behind the Checks window. In test 5, at 00:00:13.925 the phone's
picture still showed Sleep Learning Library while the accessibility
tree already held the module's items (seen in the screenshot); the
picture was not one of the test's checks.

## Step 2: the display density and the apps' versions

These tests are step 2 of [Android checks](../architecture/android-checks.md),
"As built: step 2": the phone's display density, read as a recording
starts and stops and followed through "Display size" changes, and the
version of each app seen, read as the recording stops. Recordings made
before step 2 hold neither, so tests 7 to 9 make new ones. From this
build, the Checks list of an older recording, such as those of tests 2
and 4, starts with the row "Phone density not recorded: the recording
was made before the recorder read it.", and each app row reads "App",
the package, ", version not recorded:" and the screens and visits;
tests 2 and 4 give the rows as they were before step 2.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings and the new tests pass, with
`wm density` and `dumpsys package` output written from AOSP's source.
What the Pixel prints has not been seen yet; these tests are the first
time.

TalkBack is not needed for these tests; leave it off.

## Test 6: build and install the third build (development PC)

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
4. Wait until the panel shows "Companion app: installed, on, and
   answering", and read the note under "Android device checks".

**On screen:** the build ends with `INSTALLER BUILT`. In step 4 the note
ends "It reads the phone's display density as the recording starts and
stops, and the version of each app it saw as the recording stops, and
the Checks tab shows them." Send a screenshot of the panel.

## Test 7: a short recording of the client's app

Before the test, open the client's app, Nova SleepCare, on the phone, on
its Home screen. Leave the recorder open from test 6, with "Android
device" chosen. Test 8 uses the recording this test makes.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, tap the LEARN tab at the bottom of the app, and wait 5
   seconds.
3. While it is still recording, take a screenshot of "Collector status"
   under "Current session".
4. Choose "Stop recording", and wait until the recording has opened in
   the player.

**On screen:** in step 3 "Collector status" has a line reading
"android.device: Running, Healthy. Phone: density" then a number, then
"dpi at the start." No "android.device" line, or Degraded, is a failure;
send the line as it is. Send the screenshot.

## Test 8: the density and the app's version in the Checks tab

The recording from test 7 is open in the player.

1. If the Checks window is closed, click the "Checks" tab, and take a
   screenshot of the whole recorder window.
2. On the phone, open Settings, then Apps, then the line starting "See
   all", then Nova SleepCare.
3. Scroll to the bottom of the app's page, and take a screenshot of the
   phone showing the version line.

**On screen:** in step 1 the list's first row reads "Phone density",
then the number test 7 showed, then "dpi, so 1 dp is", then that number
divided by 160 (3 for 480), then "pixels, the phone's own density,
00:00:00.000 to" the recording's end time. The next row starts "App
com.pss.novasleepcare, version", then a version and "(code" with a
number, then "), targets API" with a number, then ":" and the screens
and visits. "version not recorded", "version not read" or "not found at
the stop" is a failure. In step 3 the version on the phone is the same
as the one in the Checks row. Send both screenshots.

## Test 9: a Display size change during a recording

Before the test, on the phone, open Settings, then Display, then
"Display size and text", so that the "Display size" slider is on the
screen. Leave the recorder open, with "Android device" chosen.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, tap the plus button at the right end of the "Display
   size" slider once, and wait 5 seconds.
3. Tap the minus button at the left end of the same slider once, and
   wait 5 seconds.
4. Choose "Stop recording", and wait until the recording has opened in
   the player.
5. If the Checks window is closed, click the "Checks" tab, and take a
   screenshot of the whole recorder window.

**On screen:** in step 2 everything on the phone's screen gets larger,
and in step 3 it returns to its size before. In step 5 the list's first
three rows start "Phone density": the first with test 7's number and
"the phone's own density, 00:00:00.000 to" a time about 5 seconds in;
the second with a larger number and "set by Display size", from that
time to a time about 5 seconds later; the third with test 7's number
again and "the phone's own density", from that time to the recording's
end. One density row only, or a row with "found only by the read at the
stop", is a failure; send the screenshot as it is.

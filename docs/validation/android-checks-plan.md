# Validation plan: Android checks, step 1 (screens and visits)

These tests are step 1 of [Android checks](../architecture/android-checks.md),
"Build order": the player dividing a phone recording into apps, screens,
visits and states, shown in a new "Checks" tab and a strip on the
timeline. No check of the app is built yet, so the tab lists no
findings.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings; the new tests pass, one for each
rule, the first made from test 64's measured windows and events; and
test 64's own recording, read by a throwaway program, gave the two
screens and two visits that test 2 expects. The player itself has not
been seen; these tests do that.

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
  first app window; 7 snapshots, 7 settled"
- "Screen 2: Safety and emergency (com.android.settings), named by its
  window title, activity SubSettings, 1 visit"
- "Visit 2, 00:00:16.511 to" the recording's end time, ", started by a
  new window; 5 snapshots, 2 settled"

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

## Test 4: recording the client's app

Before the test, connect the Pixel by USB, unlocked, and on the phone
open the client's app on its first screen. In the recorder, in
"Recording target", choose "Android device". Write down, in order, each
screen you open and each dialog, menu or sheet you open in step 3.

1. Choose "Start recording", and wait 5 seconds.
2. On the phone, open three of the app's screens one after another,
   waiting 5 seconds on each.
3. On the third screen, open one dialog, menu or bottom sheet, wait 5
   seconds, and close it.
4. Press Back until the app's first screen shows again, waiting 5
   seconds on it.
5. Choose "Stop recording", and wait until the recording has opened in
   the player.

**On screen:** the recording opens in the player with the phone's
picture.

## Test 5: the client's app in the Checks tab

The recording from test 4 is open.

1. Click the "Checks" tab.
2. Take a screenshot of the whole recorder window. If the list does not
   fit, scroll it down and take another, until every row has been in a
   screenshot.

**On screen:** an "App" row with the client's app's package. Under it a
"Screen" row for each screen you opened, with the screen's name. Back
passes through the screens again, so each screen shown again has more
than one visit, and every visit after its first says "a return"; the
first screen's row says at least "2 visits". The third screen has an
indented row starting "In" naming the dialog, menu or sheet of step 3. A screen missing from the
list, two screens shown as one, or no "In" row is a failure; the
screenshots tell us which rule to change, so send them with the list of
screens you wrote down.

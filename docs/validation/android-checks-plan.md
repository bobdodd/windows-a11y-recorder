# Validation plan: Android checks, steps 1 to 3 (screens and visits; density and versions; checks of the tree)

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

## Results of the third build (2026-11-12)

Run on the development PC with commit efdc2fd.

- Test 7 passed: "android.device: Running, Healthy. Phone: density 390
  dpi at the start." `wm density` on the Pixel 10 Pro XL printed
  "Physical density: 390", the format read from AOSP's source.
- Test 8 passed (session
  `20261112-030333-009c95699b2d4296a2737858af3f1a5e`): "Phone density
  390 dpi, so 1 dp is 2.438 pixels, the phone's own density,
  00:00:00.000 to 00:00:45.754" and "App com.pss.novasleepcare, version
  1.0.5 (code 65), targets API 36: 2 screens, 2 visits". The phone's App
  info showed version 1.0.5. The collector read 3 of 3 apps' versions.
- Test 9 (session `20261112-031136-99159de7fe1843c7bd920047ecea5ade`)
  showed two density rows, not three: "Phone density 390 dpi ... the
  phone's own density, 00:00:00.000 to 00:00:17.105" and "Phone density
  440 dpi, so 1 dp is 2.75 pixels, set by Display size, 00:00:17.105 to
  00:00:37.484". Measured in the recording's export: two touches, a
  scroll from 10.746 to 12.059 s and a tap at x 948, y 1616 from 16.495
  to 16.670 s (the plus button's side); one `display_density_forced`
  change, to "440", by notice at 17.106 s; and the read at the stop
  printed "Physical density: 390" and "Override density: 440". So the
  phone was still at 440 dpi when the recording stopped, and no touch was
  recorded for step 3; the two rows match what the phone did.
- Test 9 again, the other way round, starting at 440 dpi (session
  `20261112-031508-262e4d97af4847dca17d45d0131b4814`): passed, with three
  rows: "Phone density 440 dpi, so 1 dp is 2.75 pixels, set by Display
  size, 00:00:00.000 to 00:00:12.661", "Phone density 390 dpi, so 1 dp is
  2.438 pixels, the phone's own density, 00:00:12.661 to 00:00:20.152"
  and "Phone density 440 dpi ... set by Display size, 00:00:20.152 to
  00:00:38.320". Measured in the export: the reads at the start and the
  stop printed "Physical density: 390" and "Override density: 440"; a tap
  at x 136, y 1770 (the minus side) at 12.07 s, then
  `display_density_forced` changed from "440" to an empty value by notice
  at 12.661 s; a tap at x 967, y 1535 (the plus side) at 19.51 s, then it
  changed from empty to "440" at 20.152 s. So the Pixel writes an empty
  value when Display size returns to the phone's own, as read in AOSP's
  `DisplayWindowSettings.setForcedDensity`. Each change gave the page a
  new window and a new visit (3 visits).
- In test 9 the change of density gave the Settings page a new window:
  Visit 2 of "Display size & text" starts at 00:00:17.328, "started by a
  new window, a return". This density change started a new visit to the
  same screen; whether other configuration changes, such as turning the
  phone, do the same is inference until tested.

## Step 3: the checks of the tree and the findings

These tests are step 3 of [Android checks](../architecture/android-checks.md),
"As built: step 3": checks rewritten from Google's Accessibility Test
Framework for Android (ATF), and WCAG 2.5.8's 24dp level, run on each
screen's settled snapshots when a recording is opened, with each screen's
findings listed under it in the Checks tab. Choosing a finding moves the
playhead to it and outlines its item on the phone's picture. The tests
use recordings already made, so no phone is needed.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings and the new tests pass, and a
throwaway program ran the checks on the recordings of tests 4 and 9
and gave the rows and times these tests expect. Test 9's recording first
gave six errors for Display size buttons measured at the wrong density;
the rule that fixed it is in "As built: step 3", "The density changing
during a visit".

## Test 10: build and install the fourth build (development PC)

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

## Test 11: the client's app's findings in the Checks tab

1. Choose "Open recording...", and open
   `20261112-005243-5528d67531dd4441a50b2c2985350c59`.
2. If the Checks window is closed, click the "Checks" tab.
3. Wait 5 seconds, then take a screenshot of the whole recorder window.
4. Scroll the list until the row starting "Screen 3: Sleep Learning" is
   at its top, and take another screenshot.

**On screen:** in step 3 the line under "Show current visit" reads "2
apps, 5 screens, 6 visits. Before the first visit. Checks of the tree: 4
findings (2 errors, 1 warning, 1 info), from 11 settled snapshots, in",
a number, "seconds. The touch target checks did not run on 11 settled
snapshots, as no density was recorded for them." Under Screen 1's visit
row is the row "No findings from the checks of the tree on this
screen." In step 4, after Screen 3's visit row, there are these rows in
this order: "Speakable text present (ATF): 1 error" in bold; "Error: View
with no text, a screen reader would focus it, but it has no text,
content description, label from another item, or text from the items in
it; seen once, at 00:00:14.354"; "Duplicate speakable text (ATF): 1
info" in bold; and "Info: View described as "2", speaks "2", as 1 other
item does; none is clickable; seen once, at 00:00:14.354". Each finding
row is indented further than its bold row. Long rows wrap; there is no
horizontal scrollbar. Send both screenshots.

## Test 12: choosing a finding

The recording from test 11 is open, with the list as step 4 left it.

1. Click the row starting "Error: View with no text".
2. Take a screenshot of the whole recorder window.

**On screen:** the time under the phone's picture starts
"00:00:14.354". On the phone's picture a thick cyan rectangle with
black on both sides surrounds an item, with a thin gap between it and
the item. The line under "Show current visit" ends "Showing time 1 of 1
of the chosen finding, outlined on the phone's picture while its items
are shown." "Previous time" and "Next time" are both greyed out, as the
finding was seen once. In the "Accessibility tree" tab, or its window, a
row starting "View" is highlighted as selected. Send the screenshot.

## Test 13: stepping through a finding's times

1. Choose "Open recording...", and open
   `20261112-031508-262e4d97af4847dca17d45d0131b4814` (test 9, Display
   size), then, if the Checks window is closed, click the "Checks" tab.
2. Under the bold row "Touch target size (ATF, 48dp): 2 not run", click
   the row starting "Not run: LinearLayout containing "Outline text"",
   and take a screenshot of the whole recorder window.
3. Click "Next time", and take a screenshot.
4. Click "Next time" again, and take a screenshot.

**On screen:** in step 2 the line under "Show current visit" includes
"Checks of the tree: 3 findings (3 not run), from 6 settled snapshots",
and no row in the list starts "Error". In steps 2, 3 and 4 the time
under the phone's picture starts "00:00:02.933", "00:00:12.887" and
"00:00:20.419", the line ends "Showing time 1 of 3", "2 of 3" and "3 of
3" followed by "of the chosen finding, outlined on the phone's picture
while its items are shown.", and a thick cyan rectangle with black on both sides, about as wide
as the screen and short outlines the item. In step 4 "Next time" is
greyed out and "Previous time" is not. Send the three screenshots.

## Results of the fourth build (2026-11-12)

Run on the development PC with commit 5006d0c.

- Test 11 passed: the line read "2 apps, 5 screens, 6 visits. Before
  the first visit. Checks of the tree: 4 findings (2 errors, 1 warning,
  1 info), from 11 settled snapshots, in 0.1 seconds. The touch target
  checks did not run on 11 settled snapshots, as no density was
  recorded for them."; Screens 1 and 2 had "No findings from the checks
  of the tree on this screen."; Screen 3's rows were as expected, in
  bold and indented; Screen 4 had the Button with no text in pane
  'Dialog' and Screen 5 the launcher widget's class warning, the four
  findings the throwaway program gave. No horizontal scrollbar.
- Test 12, first run: a click on a row's text did nothing; only a
  click on its background chose it (owner: "The lines are hard to click
  on. it has to be the background, not the text"). Chosen that way, the
  line ended "Showing time 1 of 1 of the chosen finding, outlined on the
  phone's picture while its items are shown.", the time was
  00:00:14.354, and "Previous time" and "Next time" were greyed out. The
  Checks window covered the phone's picture and the "Accessibility tree"
  tab, so the outline and the tree row were not seen. Fixed in
  `SelectableText.cs` (see player-layout.md, "Selecting and copying
  text"); the same fix applies to every list and tree whose rows hold
  selectable text, such as the reading steps and the accessibility tree.
- Test 12, second run (commit 98ee439): a click on the words chose the
  row. The line ended "Showing time 1 of 1 of the chosen finding,
  outlined on the phone's picture while its items are shown.", the
  time was 00:00:14.354, and both buttons were greyed out. The item is
  the app's logo beside "Sleep Learning", 68 to 127 by 154 to 212 on the
  phone: measured in the screenshot, the outline's #00E5FF ran round it,
  about 12 pixels across, too small to find. The "Accessibility tree"
  window showed the row "View with no text" selected, on #E6E0F0, 1.29
  to 1 against the white rows round it (owner: "it fails non-text
  contrast of 3:1, I suggest a darker border on the highlight"). Both
  changed: the outline as "As built: step 3" says, and the tree's
  selected row as player-contrast.md says.
- Test 12, third run (commit 57a45be): passed. The app's logo beside
  "Sleep Learning" had the thick cyan outline with black on both sides
  and a gap from the logo, and the tree's row "View with no text", in
  the window "Nova SleepCare" with items taken at 00:00:14.354, had its
  dark border (owner: "Both are much clearer now").
- Test 13 (commit 57a45be): passed. The line included "Checks of the
  tree: 3 findings (3 not run), from 6 settled snapshots, in 0.0
  seconds"; the times were 00:00:02.933, 00:00:12.887 and 00:00:20.419
  with "Showing time 1 of 3", "2 of 3" and "3 of 3"; the outline ran
  the screen's width round the "Outline text" row at the bottom of the
  list; at 3 of 3 "Next time" was greyed out and "Previous time" was
  not. The tree selected the row "LinearLayout containing "Outline
  text"" with its dark border. At 00:00:20.419 the phone's picture was
  mid-way through the change of Display size, two layouts blended.

## Test 14: tab windows stay in front of the player

Built with the commit after 2a01d5e. Any recording can be open.

1. Tear out the "Checks" tab with the button beside its name, and move
   its window so it overlaps the player's window.
2. Click on the player's window, on the empty space beside "Timeline
   zoom".
3. Take a screenshot of the whole screen.
4. Minimise the player's window, then restore it from the taskbar, and
   take another screenshot.

**On screen:** in step 3 the Checks window is still in front of the
player's window. In step 4 the Checks window went down with the player
and came back with it, in front of it.


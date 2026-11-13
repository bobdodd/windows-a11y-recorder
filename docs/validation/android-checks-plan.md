# Validation plan: Android checks, steps 1 to 4 (screens and visits; density and versions; checks of the tree; measuring the video; the phone's screenshots)

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

Result, 2026-11-12, commit 4a37d75: test 14 passed (owner: "those
smaller windows stay in front").


## Test 15: the Checks list's chosen row keeps a dark edge

Built with the commit after 9562de8.

1. Choose "Open recording...", and open
   `20261112-005243-5528d67531dd4441a50b2c2985350c59`.
2. If the Checks window is closed, click the "Checks" tab.
3. Scroll the list until the row starting "Error: View with no text" is
   in view, and click it.
4. Press Tab once, so that the list no longer has the focus.
5. Take a screenshot of the Checks list.

**On screen:** in step 5 the row starting "Error: View with no text" has
a pale purple background with a dark purple edge round it, 2 pixels
wide. A pale grey row with no edge is a failure. Send the screenshot.

Result, 2026-11-12, commit c3c9066: test 15 passed. With the focus on
"Show current visit", the row kept its pale purple background and dark
edge (screenshot f12).

## Step 4: measuring the video against the phone's screenshots

Before any contrast check is built, the colours of the recorder's frames
are compared with screenshots taken on the phone at the same moments
(android-checks.md, "Step 4: measuring the video first"). Test 16 makes
the recording and the screenshots; the comparison is done from the
files, so there is nothing to check on screen beyond the script's lines.

## Test 16: a recording with the phone's own screenshots

The current build is used; nothing new is installed. Before the test,
open Nova SleepCare on the phone, on its Home screen, and leave the
recorder open with "Android device" chosen. Open a PowerShell window
beside the recorder and paste the script below, but do not press Enter
yet. The script takes a screenshot on the phone about every 3 seconds,
20 in all, into a new folder in Documents.

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
$adb = Join-Path $env:LOCALAPPDATA "Windows A11y Recorder\android\platform-tools\adb.exe"
$out = Join-Path ([Environment]::GetFolderPath("MyDocuments")) ("phone-screenshots-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $out | Out-Null
$times = Join-Path $out "times.txt"
for ($i = 1; $i -le 20; $i++) {
    $name = "shot-{0:D2}.png" -f $i
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    & $adb shell screencap -p /data/local/tmp/a11y-shot.png
    & $adb pull /data/local/tmp/a11y-shot.png (Join-Path $out $name) | Out-Null
    $clock.Stop()
    $stamp = Get-Date -Format "HH:mm:ss.fff"
    Add-Content -Path $times -Value "$name $stamp $($clock.ElapsedMilliseconds) ms"
    "Shot $i saved at $stamp, took $($clock.ElapsedMilliseconds) ms"
    Start-Sleep -Seconds 2
}
& $adb shell rm /data/local/tmp/a11y-shot.png
"DONE: 20 screenshots in $out"
```

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. In PowerShell, press Enter to run the script.
3. On the phone, keep each screen still, and change it only after these
   lines appear in PowerShell: after "Shot 4", tap LEARN; after "Shot
   8", tap DIARY; after "Shot 12", tap PROFILE; after "Shot 16", tap
   HOME.
4. When PowerShell shows the line starting "DONE", choose "Stop
   recording" in the recorder, and wait until the recording has opened
   in the player.
5. Take a screenshot of the PowerShell window.

**On screen:** in step 5 PowerShell shows 20 lines from "Shot 1 saved
at" to "Shot 20 saved at", each with a time and "took" a number of ms,
then "DONE: 20 screenshots in" and the folder. Red error lines are a
failure; send them as they are. Send the screenshot; the recording and
the screenshots are then read from the PC for the comparison.

Result, 2026-11-12: test 16 passed; 20 screenshots, 565 to 989 ms each
(screenshot f13). The recording is
`20261112-154851-32c7b61a5552453bbf5febffcca978c7`; what the comparison
found is in android-checks.md, "Step 4: what the measurement found".

## Step 4a: the phone's own screenshots

These tests are step 4a of [Android checks](../architecture/android-checks.md),
"As built: step 4a, the phone's screenshots": the companion app takes a
screenshot of the phone's screen as the recording starts and each time a
window has had no change for half a second, and the recorder saves each
as a PNG beside the frames. The companion app now speaks protocol 6, so
it has to be installed again.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, the companion app compiles with
warnings as errors, and the tests pass, 2,309 of them, with the 10 that
need Windows failing as before. None of that shows Android taking the
screenshots on the Pixel. Tests 17 and 18 do.

## Test 17: build and install the fifth build (development PC)

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
   the companion line stops changing.
5. Take a screenshot of the panel.

**On screen:** the build ends with `INSTALLER BUILT`. In step 5 the
companion line reads "Companion app: installed, on, and answering
(version 1.1.0, version code" with a number, and ends "protocol 6)."
"protocol 5" is a failure. If the line says the service is off instead,
that is a result too: Android may ask for the service to be turned on
again because its configuration changed. Send the screenshot either way.

## Test 18: screenshots while recording

Before the test, open Nova SleepCare on the phone, on its Home screen,
with TalkBack off, and leave the recorder open with "Android device"
chosen, as test 17 left it.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, tap LEARN and wait 5 seconds, then tap DIARY and wait
   5 seconds.
3. Take a screenshot of the recorder window with the line starting
   "android.screenshots" under "Collector status" in view.
4. Choose "Stop recording", and wait until the recording has opened in
   the player.
5. Open PowerShell and run the script below, then take a screenshot of
   the PowerShell window.

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
Add-Type -AssemblyName System.Drawing
$root = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Windows A11y Recorder"
$latest = Get-ChildItem -LiteralPath $root -Directory | Sort-Object Name | Select-Object -Last 1
$folder = Join-Path $latest.FullName "screenshots\android"
$shots = @(Get-ChildItem -LiteralPath $folder -Filter "*.png" | Sort-Object Name)
"Recording: $($latest.Name)"
"Screenshots: $($shots.Count)"
foreach ($shot in $shots) {
    $image = [System.Drawing.Image]::FromFile($shot.FullName)
    "$($shot.Name): $($image.Width) x $($image.Height), $([math]::Round($shot.Length / 1KB)) KB"
    $image.Dispose()
}
```

**On screen:** in step 3 the line reads "android.screenshots: Running,
Healthy. Phone screenshots:" followed by a number of at least 3, then
"(1 at the start," and, further on, "the phone took a median", a number,
"ms from asking to the PNG". A line with "refused by Android" or "could
not be read" is a failure; send it as it is. In step 5 PowerShell shows
the recording's name, "Screenshots:" with a number of at least 3, and a
line for each file, from "0000000001.png", each "1080 x 2404" and a
number of KB. Red error lines are a failure. Send both screenshots; the
screenshots are then read from the PC for step 4b.

## Results of the fifth build (2026-11-12)

- Test 17 passed: the companion line read "Companion app: installed, on,
  and answering (version 1.1.0, version code 782, protocol 6)." The
  service stayed on after the companion app was installed again, and
  the panel's note ends with the sentence about the phone's screenshots.
- Test 18, the files: the recording (20261112-171107) has 5 phone
  screenshots, each 1080 x 2404, in sRGB with no conversion, all listed
  in the manifest with their hashes, and 5 `android-screenshot` records.
  The first was taken as the recording started (Home); the second and
  third as LEARN and DIARY settled; the fourth and fifth only as the
  status bar's clock changed (the images differ only there, at 12:11,
  12:12 and 12:13), as the rule of a screenshot for each window that
  settles gives. For all 5, Android's time fell within the call, so the
  inference that it uses the companion's clock held on the Pixel; the
  phone took 338 to 412 ms from asking to the PNG. No refusals.
- Test 18 passed. With the panel scrolled down, after the stop, the
  line read "android.screenshots: Disposed, Healthy. Phone screenshots:
  5 (1 at the start, 4 as a window settled), a median 126 KB each, in
  sRGB IEC61966-2.1, the phone took a median 376 ms from asking to the
  PNG, median delay from PNG to recorder 24 ms." The 376 ms agrees with
  the companion's times in the records.

## Step 4b: measuring the colours and finding the text

Step 4b of [Android checks](../architecture/android-checks.md), "As
built: step 4b, measuring the colours and finding the text", measures
the colours of each line of text on the phone's pictures but reports
nothing yet, so it has no test on the phone. It was checked on the
development PC with test 16's recording and its 20 `screencap`
screenshots, and with test 18's recording; the results are in that
section. Step 4c's tests check the findings on the phone.

## Step 4c: the text contrast check

Step 4c of [Android checks](../architecture/android-checks.md), "As
built: step 4c, the text contrast check", holds each line of text step
4b measures to WCAG's limits, at AA (1.4.3) or AAA (1.4.6) as chosen in
the Checks tab, and lists what fails under each screen.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and the tests pass, 2,347 of
them, with the 10 that need Windows failing as before. A throwaway
program ran the check on test 16's and test 18's recordings, with the
words Windows' OCR read from their pictures on the development PC, and
gave the numbers these tests expect. None of that shows the recorder
running Windows' OCR itself, or the setting and the rows. Tests 19 to
22 do. The companion app has not changed, and TalkBack is not needed;
leave it off.

## Test 19: build and install the sixth build (development PC)

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

## Test 20: the text contrast findings of test 18's recording

1. Choose "Open recording...", and open
   `20261112-171107-88cfc05214a34a3682222e7fb25d840d` (test 18).
2. If the Checks window is closed, click the "Checks" tab.
3. Wait 10 seconds, then take a screenshot of the whole recorder window.
4. Scroll the list to the row starting "Text contrast at AA" under
   Screen 1, and take a screenshot; then do the same under Screen 2 and
   Screen 3.

**On screen:** in step 3, above the list, "Text contrast level:" is
followed by "AA (WCAG 1.4.3)", chosen, and "AAA (WCAG 1.4.6)". The line
under "Show current visit" includes "Text contrast at AA (WCAG 1.4.3):
2 findings (1 error, 1 warning), from 57 lines of text measured, 55
passed, on 3 settled snapshots (3 from the phone's screenshots, 0 from
the video), in", a number, "seconds. The text was found with Windows
OCR, en-US." In step 4: under Screen 1, the bold row "Text contrast at
AA (WCAG 1.4.3): 1 error, from 29 lines measured", then a row starting
"Error: Button described as "3", 2.38:1, #E25825 on #F3CB51; below 3:1,
the limit for large text"; under Screen 2, the row "Text contrast at AA
(WCAG 1.4.3): no findings, from 15 lines measured."; under Screen 3,
the bold row "Text contrast at AA (WCAG 1.4.3): 1 warning, from 13
lines measured", then a row starting "Warning: View described as
"RECENT SLEEP ENTRIES", 3.49:1, #778588 on #F6F6E2; fails unless the
text is large". Long rows wrap; there is no horizontal scrollbar. A
line saying "OCR was not used" is a result too; send the screenshots
either way.

## Results of the sixth build, tests 19 and 20 (2026-11-12)

- Test 19 passed: the recorder was built, installed and opened.
- Test 20 passed: the line under "Show current visit" read "Text
  contrast at AA (WCAG 1.4.3): 2 findings (1 error, 1 warning), from 57
  lines of text measured, 55 passed, on 3 settled snapshots (3 from the
  phone's screenshots, 0 from the video), in 0.8 seconds. The text was
  found with Windows OCR, en-US." Each screen's rows were as expected,
  and long rows wrapped with no horizontal scrollbar. Windows' OCR in
  the recorder read the same lines as the PowerShell OCR the numbers
  were worked out from.
- Fault found: the labels "AA (WCAG 1.4.3)" and "AAA (WCAG 1.4.6)" were
  drawn in near-black on the dark panel, as these two buttons did not
  set their text colour as the recorder's other radio buttons do. Fixed
  by giving them the same colour, #CDCCCA; to be checked in the next
  build.

## Test 21: choosing a line of a contrast finding

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7` (test 16), then,
   if the Checks window is closed, click the "Checks" tab, and wait 10
   seconds.
2. Take a screenshot of the whole recorder window.
3. Scroll the list to the row starting "Warning: Button described as
   "Sleep Goals Update your goals", line 2 of 2", and click it.
4. Take a screenshot of the whole recorder window.

**On screen:** in step 2 the line under "Show current visit" includes
"Text contrast at AA (WCAG 1.4.3): 10 findings (4 errors, 6 warnings),
from 110 lines of text measured, 99 passed, on 5 settled snapshots (0
from the phone's screenshots, 5 from the video), in", a number,
"seconds. The text was found with Windows OCR, en-US." In step 4 the
time under the phone's picture starts "00:00:42.594". On the phone's
picture, in the Profile screen's list, a thick cyan rectangle with
black on both sides surrounds the grey words "Update your goals" only,
not "Sleep Goals" above them. The line ends "Showing time 1 of 1 of the
chosen finding, outlined on the phone's picture while its items are
shown." The row reads, after "line 2 of 2,", "'Update your goals',
3.83:1, #7F8385 on #FFFFFF". The numbers in this test come from the
video's frames; if one differs a little, that is a result; send the
screenshots either way.

## Results of test 21 (2026-11-12)

- Test 21 passed, with the seventh build (the label colour fix). The
  labels "AA (WCAG 1.4.3)" and "AAA (WCAG 1.4.6)" were light grey and
  readable. The line read "Text contrast at AA (WCAG 1.4.3): 10
  findings (4 errors, 6 warnings), from 110 lines of text measured, 99
  passed, on 5 settled snapshots (0 from the phone's screenshots, 5
  from the video), in 1.3 seconds. The text was found with Windows OCR,
  en-US." Choosing the "Update your goals" row went to 00:00:42.594, and
  the row's numbers were as worked out from the frames.
- The Checks window covered the phone's picture in the first
  screenshots, as the test did not say to keep the picture in view;
  with the window moved, the cyan outline surrounded "Update your goals"
  only. Seen at that size, the outline's thickness covers the line
  above, "Sleep Goals".
- Test 22, step 1: choosing "AAA (WCAG 1.4.6)" gave "Text contrast at
  AAA (WCAG 1.4.6): 11 findings (10 errors, 1 warning), from 110 lines
  of text measured, 98 passed, on 5 settled snapshots (0 from the
  phone's screenshots, 5 from the video), in 1.3 seconds.", and the line
  no longer ended with "Showing time". Fault found: the cyan outline of
  the cleared finding stayed on the phone's picture, as the picture was
  not drawn again when the level changed. Fixed: changing the level now
  draws the picture again and greys out the time buttons when it clears
  a contrast finding; to be checked in the next build.
- Found by the owner with the eighth build: choosing the badge "3"
  error ("seen 2 times on 2 visits, first at 00:00:01.608") showed no
  picture, and its row did not say which visits. The time was when the
  snapshot settled, before the video's first frame at 00:00:01.651; the
  frame measured was at 00:00:02.655. Fixed: a contrast finding's times
  are now its pictures' times (00:00:02.655 and 00:01:06.223 for the
  badge; 00:00:47.633 for "Update your goals", not 00:00:42.594). The
  badge was seen on visits 1 and 6.

## Test 22: the AAA level, kept after a restart

The recording from test 21 is open.

1. Above the list, click "AAA (WCAG 1.4.6)", wait 5 seconds, and take a
   screenshot of the whole recorder window.
2. Close the recorder, and start it again from the Start menu.
3. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7` again, then, if
   the Checks window is closed, click the "Checks" tab.
4. Wait 10 seconds, and take a screenshot of the whole recorder window.

**On screen:** in steps 1 and 4 "AAA (WCAG 1.4.6)" is chosen, and the
line under "Show current visit" includes "Text contrast at AAA (WCAG
1.4.6): 11 findings (10 errors, 1 warning), from 110 lines of text
measured, 98 passed, on 5 settled snapshots (0 from the phone's
screenshots, 5 from the video)", and does not end with "Showing time".
Send both screenshots.

## Step 4c: findings under each visit

After test 22 the owner found that the badge "3" finding, "seen 2 times
on 2 visits, first at 00:00:01.608", did not say which visits, and that
choosing it showed no picture. Two changes followed (see "As built:
step 4c" and decision 8 in [Android checks](../architecture/android-checks.md)):
a contrast finding's times are the times of the pictures measured, and
every finding, of the tree checks and of the contrast check, belongs to
one visit and is listed under it. Checked in the Linux build
environment: the solution builds with no warnings, the new test of two
visits to one screen passes with the other checks' tests, and the
throwaway program gives the rows below from test 16's recording.

## Test 23: findings under each visit, with their pictures

Build and install as in test 19 first. If "AA (WCAG 1.4.3)" is not
chosen in the Checks tab, click it.

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7` (test 16). If the
   Checks window is closed, click the "Checks" tab, then move it so the
   phone's picture is fully in view. Wait 10 seconds.
2. Scroll the list so the row starting "Visit 1, 00:00:01.608" is at the
   top, and take a screenshot of the whole screen.
3. Click the row starting "Error: Button described as "3"" just below
   it, and take a screenshot of the whole screen.
4. Scroll to the row starting "Visit 6, 00:00:54.359", click the row
   starting "Error: Button described as "3"" below it, and take a
   screenshot of the whole screen.

**On screen:** the line under "Show current visit" includes "Text
contrast at AA (WCAG 1.4.3): 11 findings (5 errors, 6 warnings), from
110 lines of text measured, 99 passed". In step 2, below the Visit 1
row, in this order: "No findings from the checks of the tree in this
visit." in bold; "Text contrast at AA (WCAG 1.4.3): 1 error, from 29
lines measured" in bold; and a row starting "Error: Button described as
"3", 2.41:1" and ending "seen once, at 00:00:02.655", indented further.
Then the row starting "Visit 6". In step 3 the time under the phone's
picture starts "00:00:02.655", the picture shows the Home screen, not
black, and a cyan outline surrounds the badge "3" at its top right. In
step 4 the row ends "seen once, at 00:01:06.223", the time starts
"00:01:06.223", and the outline surrounds the badge again. In steps 3
and 4 "Previous time" and "Next time" are greyed out. Send the three
screenshots.

## Results of test 23 (2026-11-12)

Passed on the development PC with the build of 90da2c0. The owner sent the
screenshots of steps 3 and 4; the one of step 3 also shows the rows of
step 2. The line under "Show current visit" included "Text contrast at
AA (WCAG 1.4.3): 11 findings (5 errors, 6 warnings), from 110 lines of
text measured, 99 passed". Below the Visit 1 row came "No findings from
the checks of the tree in this visit." and "Text contrast at AA (WCAG
1.4.3): 1 error, from 29 lines measured", both in bold, then the error
for the badge "3", 2.41:1, #E15926 on #F5CC53, ending "seen once, at
00:00:02.655". Choosing it showed the Home screen at 00:00:02.655 with
the cyan outline around the badge, and the line ended "Showing time 1 of
1 of the chosen finding, outlined on the phone's picture while its items
are shown." Under Visit 6 the same rows ended "seen once, at
00:01:06.223", and choosing it showed the Home screen at 00:01:06.223
with the outline around the badge. "Previous time" and "Next time" were
greyed out in both. The row of the visit at the playhead is in bold
(Visit 6 in step 4), as before.

## Test 24: changing the level, and keeping it after a restart

The recording from test 23 is open, with AA chosen.

1. Scroll the list to the row starting "Warning: Button described as
   "Sleep Goals Update your goals", line 2 of 2" (under Screen 3's Visit
   5), click it, and take a screenshot of the whole screen.
2. Above the list, click "AAA (WCAG 1.4.6)", wait 5 seconds, and take a
   screenshot of the whole screen.
3. Close the recorder, start it again from the Start menu, and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7` again with "Open
   recording...". If the Checks window is closed, click the "Checks"
   tab.
4. Wait 10 seconds, and take a screenshot of the whole screen.

**On screen:** in step 1 the time under the phone's picture starts
"00:00:47.633", and a cyan outline surrounds "Update your goals". In
step 2 "AAA (WCAG 1.4.6)" is chosen, there is no cyan outline on the
phone's picture, "Previous time" and "Next time" are greyed out, and the
line under "Show current visit" includes "Text contrast at AAA (WCAG
1.4.6): 12 findings (11 errors, 1 warning)" and does not end with
"Showing time". In step 4 "AAA (WCAG 1.4.6)" is still chosen, and the
line includes the same "Text contrast at AAA (WCAG 1.4.6): 12 findings
(11 errors, 1 warning)". Send the three screenshots.

## Results of test 24, steps 1 and 2 (2026-11-12)

Passed on the development PC with the build of 90da2c0. In step 1,
choosing the warning for "Sleep Goals Update your goals", line 2 of 2,
3.83:1, #7F8385 on #FFFFFF, showed the Profile screen at 00:00:47.633
with the cyan outline around "Update your goals", and the line ended
"Showing time 1 of 1 of the chosen finding, outlined on the phone's
picture while its items are shown." In step 2, after "AAA (WCAG 1.4.6)"
was chosen, there was no outline, "Previous time" and "Next time" were
greyed out, and the line included "Text contrast at AAA (WCAG 1.4.6):
12 findings (11 errors, 1 warning), from 110 lines of text measured, 98
passed" and ended with the OCR sentence, not "Showing time". Visit 5
listed "Text contrast at AAA (WCAG 1.4.6): 8 errors, from 24 lines
measured", with the same item now an error, below 4.5:1. In step 4, after
the restart, "AAA (WCAG 1.4.6)" was still chosen and the line included
"Text contrast at AAA (WCAG 1.4.6): 12 findings (11 errors, 1 warning)".
Test 24 passed.

## Found in test 24: the app's loading screen names a screen

The line in step 1 named Visit 5's screen by two symbols. The owner
pointed out that they are the sun and moon of the app's loading screen
("Loading your personal sleep learning space."). The recording's tree
shows it, read with the throwaway program:

- At 00:00:31.049, on the way from the Learn tab to the Diary tab, the
  app's window shows the loading screen, with the content descriptions
  "\u2600\uFE0F\U0001F319" (sun and crescent moon), "Loading your
  personal sleep learning space." and "Version 1.2". Its first fixed
  text is the sun and moon, so a visit starts (Visit 3) and the screen
  is named by them (Screen 3). At 00:00:31.326 the tree shows "Sleep
  Diary" and Visit 4 starts. Visit 3 has no settled snapshot.
- At 00:00:42.156, on the way to the Profile tab, the loading screen
  comes again, and Visit 5 starts as a return to Screen 3. At
  00:00:42.502 the tree shows the Profile screen, but every text on it
  ("Bob Dodd", the email address, "0 DAY STREAK" and so on) is inside an
  android.widget.ScrollView, so it has no first fixed text. A snapshot
  with no first fixed text starts no visit (a rule meant for screens
  half drawn), so the Profile screen stays in Visit 5, named by the
  loading screen's symbols.
- Choosing Screen 4's Visit 4 shows the loading screen, as the owner
  saw: the tree shows "Sleep Diary" at 00:00:31.326, but the video's
  frame then is the loading screen's, from 00:00:31.322; the first frame
  with "Sleep Diary" is from 00:00:31.340 (times as the player shows
  them, which cuts off, not rounds, below a millisecond).

The owner chose "Its own screen" for the loading screen, and "Its
first text" for a screen whose every text is in a scrolling list
(decision 10 in [Android checks](../architecture/android-checks.md)).

## Decision 10: loading screens and screens with no fixed text

Built as in "When a visit starts" (rule 5) and "What names a screen" in
[Android checks](../architecture/android-checks.md): when the first
fixed text goes away for half a second or more and the window still has
text, a visit starts, named by its first text. Visits, and the findings
from the tree, are shown at the picture of their settled snapshot. The
playback index is now version 29, so a recording opened again is read
again. Checked in the Linux build environment: the solution builds with
no warnings, three new tests pass with the other tests of screens,
checks and contrast, and the throwaway program gives the rows below
from test 16's recording, with a frame from each visit's time showing
its screen.

## Test 25: build and install the seventh build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 26: the loading screen and the Profile screen are separate screens

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7` (test 16). If the
   Checks window is closed, click the "Checks" tab, then move it so the
   phone's picture is fully in view. Wait 10 seconds, and take a
   screenshot of the whole screen.
2. Scroll the list so the row starting "Screen 3:" is at the top, and
   take a screenshot of the whole screen.
3. Scroll the list so the row starting "Screen 5:" is at the top, and
   take a screenshot of the whole screen.

**On screen:** in step 1 the line under "Show current visit" begins "1
app, 5 screens, 7 visits." In step 2 the Screen 3 row starts "Screen 3:"
followed by the sun and moon symbols and "(com.pss.novasleepcare),
named by its first fixed text, as 5 screens share the title 'Nova
SleepCare'", and ends "2 visits". Below it, the row starting "Visit 3,
00:00:31.048 to 00:00:31.326, started by a change of first fixed text;"
and, under it, "Not checked, as no snapshot of this visit settled.";
then the row starting "Visit 5, 00:00:42.155 to 00:00:42.502, started
by a change of first fixed text, a return;" and the same "Not checked"
row. In step 3 the Screen 5 row starts "Screen 5: Bob Dodd
(com.pss.novasleepcare), named by its first text, as it has no fixed
text and 5 screens share the title 'Nova SleepCare'" and ends "1
visit"; below it is the row "Visit 6, 00:00:42.502 to 00:00:54.359,
started by the first fixed text going away; 9 snapshots, 1 settled".
Send the three screenshots.

## Results of tests 25 and 26 (2026-11-12)

Passed on the development PC with the build of 707c1ce, with AAA still
chosen from test 24 (the level does not change these rows). The line
under "Show current visit" began "1 app, 5 screens, 7 visits." The
Screen 3 row read "Screen 3:", the sun and moon, "(com.pss.novasleepcare),
named by its first fixed text, as 5 screens share the title 'Nova
SleepCare', activity not given by a window-state event, 2 visits", with
Visit 3 (00:00:31.048 to 00:00:31.326, 27 snapshots, 0 settled) and
Visit 5 (00:00:42.155 to 00:00:42.502, a return, 37 snapshots, 0
settled), each with "Not checked, as no snapshot of this visit
settled." The Screen 5 row read "Screen 5: Bob Dodd
(com.pss.novasleepcare), named by its first text, as it has no fixed
text and 5 screens share the title 'Nova SleepCare', activity not given
by a window-state event, 1 visit", and below it "Visit 6, 00:00:42.502
to 00:00:54.359, started by the first fixed text going away; 9
snapshots, 1 settled". The touch target finding for "Help & FAQ Get
Support" under it ended "seen once, at 00:00:47.633".

The screenshot of step 2 showed the time 00:00:31.322, where the
expected rows of test 27 said 00:00:31.323: the player cuts off times
below a millisecond, and the throwaway program had rounded them (the
frame is at 31.322606502 s). Test 27's times were corrected before it
was run: 00:00:42.154 (frame at 42.154903473 s) and 00:00:31.322.

## Test 27: a visit is shown at its picture

The recording from test 26 is open. Keep the phone's picture in view.

1. Scroll the list to the row starting "Visit 4, 00:00:31.326", click
   it, and take a screenshot of the whole screen.
2. Scroll to the row starting "Visit 3, 00:00:31.048", click it, and
   take a screenshot of the whole screen.
3. Scroll to the row starting "Not run: Button described as "Help & FAQ
   Get Support"" (under Visit 6), click it, and take a screenshot of the
   whole screen.

**On screen:** in step 1 the time under the phone's picture starts
"00:00:42.154", and the picture shows "Sleep Diary" at the top, not the
sun and moon. In step 2 the time starts "00:00:31.322", and the picture
shows the sun and moon over "Loading your personal sleep learning
space.". In step 3 the row ends "seen once, at 00:00:47.633", the time
starts "00:00:47.633", and the picture shows the Profile screen with
"Bob Dodd" at the top. Send the three screenshots.

## Results of test 27 (2026-11-12)

Passed on the development PC with the build of 707c1ce, AAA chosen.
Choosing Visit 4 showed the Sleep Diary screen, not the loading screen,
at 00:00:42.154. Choosing Visit 3 showed the sun and moon over "Loading
your personal sleep learning space." at 00:00:31.322. Choosing the
touch target finding for "Help & FAQ Get Support" showed the Profile
screen with "Bob Dodd" at the top at 00:00:47.633; its row ended "seen
once, at 00:00:47.633", and the cyan outline was drawn at the bottom of
the list, just above the bottom bar, where the item is cut off by the
list, as the finding says. Decision 10 is checked on the PC.

## Step 5: non-text contrast, measured first (2026-11-12)

Tests 16 and 18's recordings were measured on the development PC with
a throwaway program running the recorder's code; what it measured, and
the owner's questions, are in the architecture document's "Step 5:
measuring non-text contrast first". The owner's answers are in its
"Step 5: the owner's decisions", and the check built to them in "As
built: step 5". Checked in the Linux build environment: the solution
builds with no warnings, and the new tests pass with the others. The
throwaway program, run with the check, gives the rows below from test
16's recording.

## Test 28: build and install the eighth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 29: the non-text contrast findings of test 16's recording

Keep the phone's picture in view throughout.

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7` (test 16). If the
   Checks window is closed, click the "Checks" tab, then move it so the
   phone's picture is fully in view. Wait 10 seconds, and take a
   screenshot of the whole screen.
2. Scroll the list so the row starting "Visit 2," is at the top, and
   take a screenshot of the whole screen.
3. Click the row starting "Error: View described as "LEARN", taken to
   show its state" below it, and take a screenshot of the whole screen.
4. Scroll the list to the row starting "Visit 4, 00:00:31.326", click
   the row starting "Error: View described as "WEEK VIEW", taken to show
   its state" below it, and take a screenshot of the whole screen.

**On screen:** in step 1 the line under "Show current visit" includes
"Non-text contrast (WCAG 1.4.11), at AA, its only level: 37 findings (6
errors, 31 info), from 50 controls measured, 13 passed; graphics with
no item in the tree are not checked." In step 2, below the Visit 2 row,
the bold row "Non-text contrast (WCAG 1.4.11): 1 error, 4 info, from 8
controls measured", then a row starting "Error: View described as
"LEARN", taken to show its state, as it is filled #F6E19B and the 3
others in its row are not: 1.18:1 against #F5CC53" and ending "seen
once, at 00:00:28.202". In step 3 the time under the phone's picture
starts "00:00:28.202", and a cyan outline surrounds the LEARN tab at the
bottom. In step 4 the bold row above it reads "Non-text contrast (WCAG
1.4.11): 2 errors, 4 info, from 8 controls measured", the WEEK VIEW row
includes "1.98:1 against #FFFFFF, the other's fill, and 1.83:1 against
#F7F7E3 around it", the time starts "00:00:42.154", and the outline
surrounds WEEK VIEW. Long rows wrap; there is no horizontal scrollbar.
Send the four screenshots.

## Results of tests 28 and 29 (2026-11-12)

Passed on the development PC with the build of 3a9ecf6, AAA still chosen
for text (the level does not change non-text contrast). The line under
"Show current visit" included "Non-text contrast (WCAG 1.4.11), at AA,
its only level: 37 findings (6 errors, 31 info), from 50 controls
measured, 13 passed; graphics with no item in the tree are not
checked." Under Visit 2 the bold row read "Non-text contrast (WCAG
1.4.11): 1 error, 4 info, from 8 controls measured", and the LEARN row
"Error: View described as "LEARN", taken to show its state, as it is
filled #F6E19B and the 3 others in its row are not: 1.18:1 against
#F5CC53, the others' fill and the colour around it; a state needs 3:1;
approximate, from the video; seen once, at 00:00:28.202". Choosing it
showed the Sleep Learning Library screen at 00:00:28.202 with the cyan
outline round the LEARN tab. Under Visit 4 the bold row read "2 errors,
4 info, from 8 controls measured"; choosing the WEEK VIEW row ("1.98:1
against #FFFFFF, the other's fill, and 1.83:1 against #F7F7E3 around
it") showed the Sleep Diary screen at 00:00:42.154 with the outline
round WEEK VIEW. Long rows wrapped, with no horizontal scrollbar. Step 5
is checked on the PC.

## Step 6: titles and headings (2026-11-12)

The owner's decisions, what the recordings to hand measured, and the
checks built to them are in the architecture document's "Step 6: titles
and headings". Checked in the Linux build environment: the solution
builds with no warnings, and the new tests pass with the others. The
throwaway program, run with the checks, gives the rows below from test
16's recording.

## Test 30: build and install the ninth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 31: the title and heading findings of test 16's recording

Keep the phone's picture in view throughout.

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7` (test 16). If the
   Checks window is closed, click the "Checks" tab, then move it so the
   phone's picture is fully in view. Wait 10 seconds, and take a
   screenshot of the whole screen.
2. Scroll the list so the row starting "Visit 2," is at the top, and
   take a screenshot of the whole screen.
3. Click the row starting "Error: FrameLayout containing "Sleep Learning
   Library"" under the bold row "Screen title shared with other
   screens", and take a screenshot of the whole screen.

**On screen:** in step 1 the line under "Show current visit" includes
"Checks of the tree: 11 findings (10 errors, 1 not run), from 7 settled
snapshots". In step 2, below the Visit 2 row, the bold row "Screen title
shared with other screens (WCAG2ICT 2.4.2): 1 error", then a row ending
"its window title 'Nova SleepCare' is also the title of 3 other screens
checked in this recording: 'Welcome, Bob Dodd', 'Sleep Diary', 'Bob
Dodd'; seen once, at 00:00:28.202", then the bold row "No heading (WCAG
1.3.1): 1 error", then a row ending "no item on the screen is marked as
a heading; seen once, at 00:00:28.202". In step 3 the time under the
phone's picture starts "00:00:28.202", the Sleep Learning Library screen
is shown, and a cyan outline runs round the edge of the whole picture.
Long rows wrap; there is no horizontal scrollbar. Send the three
screenshots.

## Results of tests 30 and 31 (2026-11-12)

Passed on the development PC with the build of 3a162e4, AAA still chosen
for text. The line under "Show current visit" included "Checks of the
tree: 11 findings (10 errors, 1 not run), from 7 settled snapshots".
Under Visit 2 the bold row read "Screen title shared with other screens
(WCAG2ICT 2.4.2): 1 error", then the row ending "its window title 'Nova
SleepCare' is also the title of 3 other screens checked in this
recording: 'Welcome, Bob Dodd', 'Sleep Diary', 'Bob Dodd'; seen once, at
00:00:28.202", then "No heading (WCAG 1.3.1): 1 error" and the row
ending "no item on the screen is marked as a heading; seen once, at
00:00:28.202". Visit 1 showed the same two errors. Choosing the shared
title row showed the Sleep Learning Library screen at 00:00:28.202 with
the cyan outline round the whole picture. Long rows wrapped, with no
horizontal scrollbar. Step 6 is checked on the PC.

## Step 7: the audit level (2026-11-12)

The owner's decision, the standards and the check built to them are in
the architecture document's "The audit level". "Text contrast level:"
is now "Audit level:", with "AA" and "AAA"; at AAA, targets are also
held to 44 by 44 dp (WCAG 2.5.5), and the line under "Show current
visit" says the level and what it holds the checks to. Checked in the
Linux build environment: the solution builds with no warnings, and the
new tests pass with the others. The throwaway program gives, for test
16's recording, 11 findings of the tree at AA and 12 at AAA, the one
added being on visit 6.

## Test 32: build and install the tenth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 33: the audit level and the 44dp check, on test 16's recording

Keep the phone's picture in view throughout. AAA was chosen for text
contrast before this build.

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7` (test 16). If the
   Checks window is closed, click the "Checks" tab, then move it so the
   phone's picture is fully in view. Wait 10 seconds, and take a
   screenshot of the whole screen.
2. Scroll the list so the row starting "Visit 6," is at the top, and
   take a screenshot of the whole screen.
3. Above the list, click "AA", wait 10 seconds, scroll the list so the
   row starting "Visit 6," is at the top again, and take a screenshot of
   the whole screen.

**On screen:** in step 1, "Audit level:" is shown above the list with
"AAA" chosen, and the line under "Show current visit" includes "Audit
level AAA: text contrast is held to WCAG 1.4.6, and targets to 44 by 44
dp (WCAG 2.5.5) as well as 2.5.8; 2.5.5's exceptions (an equivalent
target, a target in text, one sized by Android, an essential
presentation) are not judged." and "Checks of the tree: 12 findings (10
errors, 2 not run), from 7 settled snapshots". In step 2, below the
Visit 6 row, are the bold row "Touch target under 44dp (WCAG 2.5.5,
AAA): 1 not run" and, under it, a row starting "Not run: Button
described as "Help & FAQ Get Support", 345.4 by 23 dp, too short; WCAG
2.5.5 needs 44 by 44 dp". In step 3, "AA" is chosen, the line includes
"Audit level AA: text contrast is held to WCAG 1.4.3, and targets to
2.5.8." and "Checks of the tree: 11 findings (10 errors, 1 not run),
from 7 settled snapshots", and there is no row starting "Touch target
under 44dp" below the Visit 6 row. Long rows wrap; there is no
horizontal scrollbar. Send the three screenshots.

## Results of tests 32 and 33 (2026-11-12)

Passed on the development PC with the build of 4cacc70, as recorded by
the owner. At AAA the line under "Show current visit" included "Audit
level AAA: text contrast is held to WCAG 1.4.6, and targets to 44 by 44
dp (WCAG 2.5.5) as well as 2.5.8; 2.5.5's exceptions (an equivalent
target, a target in text, one sized by Android, an essential
presentation) are not judged." and "Checks of the tree: 12 findings (10
errors, 2 not run), from 7 settled snapshots"; below the Visit 6 row
were "Touch target under 44dp (WCAG 2.5.5, AAA): 1 not run" and the row
starting "Not run: Button described as "Help & FAQ Get Support", 345.4
by 23 dp, too short; WCAG 2.5.5 needs 44 by 44 dp". At AA the line
included "Audit level AA: text contrast is held to WCAG 1.4.3, and
targets to 2.5.8." and "11 findings (10 errors, 1 not run)", with no
44dp row under Visit 6. Text contrast gave 11 findings at AA and 12 at
AAA, as in test 24. Long rows wrapped, with no horizontal scrollbar. The
screenshots show "AA" chosen when the recording was first opened, where
"AAA" was expected from the earlier setting; the owner recorded the test
as passed. The audit level is checked on the PC.

## Step 8: images of text (2026-11-12)

The owner's decision, the standards and the check built to them are in
the architecture document's "Step 8: images of text". An image in the
tree with words read by OCR inside it that no item's text holds is a
warning to "Image of text (WCAG 1.4.5)", and at AAA to "Image of text
(WCAG 1.4.9, AAA)" as well. Checked in the Linux build environment: the
solution builds with no warnings, and the new tests pass with the
others. The throwaway program gives, for test 16's recording, 4
warnings on visit 2 at AA and 8 at AAA, and none on other visits.

## Test 34: build and install the eleventh build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 35: the images of text findings of test 16's recording

Keep the phone's picture in view throughout.

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7` (test 16). If the
   Checks window is closed, click the "Checks" tab, then move it so the
   phone's picture is fully in view. Above the list, click "AA", wait 10
   seconds, and take a screenshot of the whole screen.
2. Scroll the list so the row starting "Visit 2," is at the top, then
   scroll down until the bold row starting "Image of text" is in view,
   and take a screenshot of the whole screen.
3. Click the row starting "Warning: ImageView described as "Sleep
   Hygiene" below it, and take a screenshot of the whole screen.
4. Above the list, click "AAA", wait 10 seconds, scroll the list so the
   row starting "Visit 2," is at the top, then scroll down until the bold
   row starting "Image of text (WCAG 1.4.9, AAA)" is in view, and take a
   screenshot of the whole screen.

**On screen:** in step 1 the line under "Show current visit" includes
"Audit level AA: text contrast is held to WCAG 1.4.3, targets to 2.5.8,
and images of text to 1.4.5." and "Image of text (WCAG 1.4.5): 4
findings (4 warnings), from 4 images read by OCR, 0 passed". In step 2,
below the Visit 2 row and its other checks, the bold row "Image of text
(WCAG 1.4.5): 4 warnings, from 4 images read by OCR", then 4 rows
starting "Warning: ImageView described as", for "Understanding
Melatonin", "Sleep Hygiene", "Sleeping Environment" and "Emotion, Health
and Sleep"; each includes "of text inside the image:", "its description
holds them" and "approximate, from the video", and ends "seen once, at
00:00:28.202". In step 3 the
time under the phone's picture starts "00:00:28.202", and a cyan outline
surrounds the second card, Sleep Hygiene. In step 4 the line includes
"Audit level AAA:" and "images of text to 1.4.9 as well as 1.4.5", and
below the 4 rows of step 2 are the bold row "Image of text (WCAG 1.4.9,
AAA): 4 warnings, from 4 images read by OCR" and 4 rows starting
"Warning: ImageView described as", each including "1.4.9 allows an
image of text only as pure decoration or where essential". Long rows
wrap; there is no horizontal scrollbar. Send the four screenshots.

## Results of tests 34 and 35 (2026-11-12)

Passed on the development PC with the build of 9be5eb0, as recorded by
the owner. At AA the line under "Show current visit" included "Audit
level AA: text contrast is held to WCAG 1.4.3, targets to 2.5.8, and
images of text to 1.4.5." and "Image of text (WCAG 1.4.5): 4 findings (4
warnings), from 4 images read by OCR, 0 passed". Below the Visit 2 row
were the bold row "Image of text (WCAG 1.4.5): 4 warnings, from 4 images
read by OCR" and the 4 rows for Understanding Melatonin, Sleep Hygiene,
Sleeping Environment and Emotion, Health and Sleep, each including "of
text inside the image:", "its description holds them" and "approximate,
from the video", and ending "seen once, at 00:00:28.202". Clicking the
Sleep Hygiene row showed 00:00:28.202 (read from the status bar under
the player) with a cyan outline around that card. At AAA the line
included "images of text to 1.4.9 as well as 1.4.5" and "Image of text
(WCAG 1.4.5) and Image of text (WCAG 1.4.9, AAA): 8 findings (8
warnings)", and the bold row "Image of text (WCAG 1.4.9, AAA): 4
warnings, from 4 images read by OCR" had 4 rows, each including "1.4.9
allows an image of text only as pure decoration or where essential".
Long rows wrapped, with no horizontal scrollbar. Images of text are
checked on the PC.

## Step 9: Label in Name (2026-11-12)

The owner's decisions, the standards and the check built to them are in
the architecture document's "Step 9: Label in Name". A control's
content description is checked against the text inside it, and against
the text of any item it is labelled by: a name without the label is an
error to "Label not in name (WCAG 2.5.3)", and a name with the label
not at its start is a warning to "Label not at the start of the name
(WCAG 2.5.3, best practice)". Checked in the Linux build environment:
the solution builds with no warnings, and the new tests pass with the
others. The throwaway program gives, for test 16's recording, 47
controls checked, 47 passed and 3 with no label read, so no findings;
the PC test shows the rows for a visit with no findings and the line
under "Show current visit". The findings' own rows are covered by the
tests in the build environment, as the client's app has none.

## Test 36: build and install the twelfth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 37: the Label in Name rows of test 16's recording

Keep the phone's picture in view throughout.

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7` (test 16). If the
   Checks window is closed, click the "Checks" tab, then move it so the
   phone's picture is fully in view. Wait 10 seconds, and take a
   screenshot of the whole screen.
2. Scroll the list so the row starting "Visit 1," is at the top, then
   scroll down until the row starting "Label in name" is in view, and
   take a screenshot of the whole screen.
3. Scroll down until the rows from the one starting "Visit 3," to the
   one starting "Visit 5," are in view, and take a screenshot of the
   whole screen.

**On screen:** in step 1 the line under "Show current visit" includes
"Label in name (WCAG 2.5.3): no findings, from 47 controls checked, 47
passed, 3 with no label read; the label is all the text inside a
control, and the text of any item it is labelled by, from the tree's
text and, where it has none, OCR's, against its content description;
text beside a control is not taken as its label." In step 2, below the
Visit 1 row and its other checks, after the rows starting "Image of
text", is the row "Label in name (WCAG 2.5.3): no findings, from 11
controls checked." In step 3, between the Visit 3 and Visit 5 rows,
is the row "Not checked, as no snapshot of this visit settled.", and no
row starting "Label in name". Long rows wrap; there is
no horizontal scrollbar. Send the three screenshots.

## Results of tests 36 and 37 (2026-11-12)

Passed on the development PC with the build of c229844, as recorded by
the owner, with AAA chosen (2.5.3 is level A, so the same at both
levels). The line under "Show current visit" included "Label in name
(WCAG 2.5.3): no findings, from 47 controls checked, 47 passed, 3 with
no label read; the label is all the text inside a control, and the text
of any item it is labelled by, from the tree's text and, where it has
none, OCR's, against its content description; text beside a control is
not taken as its label." Under Visit 1, after "Image of text (WCAG
1.4.5): no image in this visit.", was the row "Label in name (WCAG
2.5.3): no findings, from 11 controls checked.", with Visit 7's row
next; Visit 2's row read "no findings, from 8 controls checked.".
Between the Visit 3 and Visit 5 rows was "Not checked, as no snapshot of
this visit settled.", with no row starting "Label in name". Long rows
wrapped, with no horizontal scrollbar. Label in Name is checked on the
PC.

## Step 10: clickable but not focusable (2026-11-12)

The owner's decision, what Android and Flutter say, and the check built
to them are in the architecture document's "Step 10: clickable but not
focusable". Each enabled clickable item shown on a settled snapshot
that is not focusable, and not inside a focusable clickable item, is a
warning to "Clickable but not focusable (WCAG 2.1.1)". Checked in the
Linux build environment: the solution builds with no warnings, and the
new tests pass with the others. The throwaway program gives, for test
16's recording, 50 clickable items checked, 0 set aside and none not
focusable, with every item with text focusable in the client's app, so
no findings and the line naming a Flutter-style tree. The findings' own
rows are covered by the tests in the build environment, as the client's
app has none.

## Test 38: build and install the thirteenth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 39: the clickable but not focusable line for test 16's recording

Keep the phone's picture in view throughout.

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7` (test 16). If the
   Checks window is closed, click the "Checks" tab, then move it so the
   phone's picture is fully in view. Wait 10 seconds, and take a
   screenshot of the whole screen.

**On screen:** the line under "Show current visit" includes "Clickable
but not focusable (WCAG 2.1.1), one of them: no findings, from 50
clickable items checked, 0 set aside inside a focusable clickable item;
each is a warning, until a keyboard test confirms it." and "In
com.pss.novasleepcare, every item with text is focusable, as in an app
drawn with Flutter, whose tree marks focusable what a screen reader can
reach, so the tree does not show which items a keyboard reaches." Long
rows wrap; there is no horizontal scrollbar. Send the screenshot.

## Results of tests 38 and 39 (2026-11-13)

Passed on the development PC with the build of 1565150, as recorded by
the owner, with AAA chosen (2.1.1 is level A, so the same at both
levels). The build ended with `INSTALLER BUILT` and the recorder
opened. The line under "Show current visit" included "Clickable but not
focusable (WCAG 2.1.1), one of them: no findings, from 50 clickable
items checked, 0 set aside inside a focusable clickable item; each is a
warning, until a keyboard test confirms it." and "In
com.pss.novasleepcare, every item with text is focusable, as in an app
drawn with Flutter, whose tree marks focusable what a screen reader can
reach, so the tree does not show which items a keyboard reaches." Long
rows wrapped, with no horizontal scrollbar. Clickable but not focusable
is checked on the PC.

## Step 11: fields and states (2026-11-13)

The owner's decisions, what the standards, Android and Flutter say, and
the checks built to them are in the architecture document's "Step 11:
fields and states". Five checks, each one discrete issue: a field with
no label (3.3.2), a field with no name (4.1.2), an item marked invalid
with no error text (3.3.1), an expandable item with no expanded state
(4.1.2) and a slider with no range information (4.1.2). Checked in the
Linux build environment: the solution builds with no warnings, and the
new tests pass with the others. The throwaway program gives, for the
recording of the client's app and the home screen
(`20261112-005243-5528d67531dd4441a50b2c2985350c59`), one finding, the
Sleep Diary notes field's, in visit 4. Its copy of the recording has
no phone video, so it gave the snapshot's own time, 00:00:23.368, not
the time of the snapshot's picture that the recorder shows.

## Test 40: build and install the fourteenth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 41: the field with no label in the client's Sleep Diary

Keep the phone's picture in view throughout.

1. Choose "Open recording...", and open
   `20261112-005243-5528d67531dd4441a50b2c2985350c59`. If the Checks
   window is closed, click the "Checks" tab, then move it so the phone's
   picture is fully in view. Wait 10 seconds, and take a screenshot of
   the whole screen.
2. Scroll the list so the row starting "Visit 4," is at the top, then
   scroll down until the row starting "Field with no label" is in view,
   and take a screenshot of the whole screen.

**On screen:** in step 1 the line under "Show current visit" includes
"Fields and states, five of them: 1 finding (1 error), from 1 field, 0
marked invalid, 0 expandable items and 0 sliders checked; a field's
label is only an item linked to it as its label (labeledBy), as a hint
is not shown once text is typed." In step 2, below the Visit 4 row, is
the row "Field with no label (WCAG 3.3.2): 1 error", and below it a row
that includes "its only label is its hint, "Fell asleep quickly, woke
up once...", which is not shown once text is typed, and no item is
linked to it as its label (labeledBy); the text just above it, "Notes
(optional)", is not linked to it as its label" and "seen once, at
00:00:41.101". There is no row starting "Field with no name". Long rows
wrap; there is no horizontal scrollbar. Send the two screenshots.

Corrected on 2026-11-13, after the owner's run: the time was first
written as "00:00:23.368", the snapshot's own time from the throwaway
program, whose copy of the recording has no phone video. A finding is
shown at the picture of its snapshot, the last video frame before the
window was next listed, as for the other checks, and that is
00:00:41.101, as on the owner's screenshot.

## Results of tests 40 and 41 (2026-11-13)

Passed on the development PC with the build of 7be8e24, as recorded by
the owner, with AAA chosen (3.3.1, 3.3.2 and 4.1.2 are level A, so the
same at both levels). The build ended with `INSTALLER BUILT` and the
recorder opened. The line under "Show current visit" included "Fields
and states, five of them: 1 finding (1 error), from 1 field, 0 marked
invalid, 0 expandable items and 0 sliders checked; a field's label is
only an item linked to it as its label (labeledBy), as a hint is not
shown once text is typed." Under visit 4 the row "Field with no label
(WCAG 3.3.2): 1 error" was followed by the finding "Error: EditText with
no text, its only label is its hint, "Fell asleep quickly, woke up
once...", which is not shown once text is typed, and no item is linked
to it as its label (labeledBy); the text just above it, "Notes
(optional)", is not linked to it as its label, in pane 'Dialog'; seen
once, at 00:00:41.101", with the field outlined on the phone's picture
when chosen, and no row starting "Field with no name". Long rows
wrapped, with no horizontal scrollbar. The time differed from the one
first written, which was the test's error (see the correction under
test 41). The checks of fields and states are checked on the PC.

## Step 12: status messages (2026-11-13)

The owner's decisions, what the standards, Android and Flutter say, and
the checks built to them are in the architecture document's "Step 12:
status messages". Two checks, each one discrete issue: text shown and
gone by itself within 5 seconds, with no touch between, that is not in
a live region, announced or in a titled pane, is an error; an app with
no live region, announcement or toast in the whole recording is a
warning, listed under the app's row. Checked in the Linux build
environment: the solution builds with no warnings, and the new tests
pass with the others. The throwaway program gives, for test 16's
recording (`20261112-154851-32c7b61a5552453bbf5febffcca978c7`), three
brief messages not exposed, in visits 2, 3 and 5, the first gone 0.41
seconds after it was shown, and the warning for `com.pss.novasleepcare`
in its 7 visits.

## Test 42: build and install the fifteenth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 43: status messages in test 16's recording

Keep the phone's picture in view throughout.

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7`. If the Checks
   window is closed, click the "Checks" tab, then move it so the phone's
   picture is fully in view. Wait 10 seconds, and take a screenshot of
   the whole screen.
2. Scroll the list so the row starting "App com.pss.novasleepcare" is
   at the top, and take a screenshot of the whole screen.
3. Scroll the list so the row starting "Visit 2," is at the top, then
   scroll down until the row starting "Status message not announced" is
   in view, and take a screenshot of the whole screen.

**On screen:** in step 1 the line under "Show current visit" includes
"Status messages (WCAG 4.1.3), two checks: 4 findings (3 errors, 1
warning); 3 brief messages (text shown and gone within 5 seconds, with
no touch between), 0 of them in a live region, announced or in a titled
pane that appeared with them; 0 items in a live region, 0 announcements
and 0 toasts in the recording." In step 2, below the App row, is the
row "No live region in the app (WCAG 4.1.3): 1 warning", and below it a
row that includes "in its 7 visits in this recording, no item of the
app was in a live region, and it made no announcement and showed no
toast". In step 3, below the Visit 2 row, is the row "Status message not
announced (WCAG 4.1.3): 1 error", and below it a row that includes
"View described as "Loading your personal sleep learning space.", it
was shown with "☀️🌙" and they went away by themselves 0.41 seconds
later, with no touch between". Long rows wrap; there is no horizontal
scrollbar. Send the three screenshots.

## Results of tests 42 and 43 (2026-11-13)

Passed on the development PC with the build of 393f268, as recorded by
the owner, with AAA chosen (4.1.3 is level AA, so it runs at both
levels). The line under "Show current visit" included "Status messages
(WCAG 4.1.3), two checks: 4 findings (3 errors, 1 warning); 3 brief
messages (text shown and gone within 5 seconds, with no touch between),
0 of them in a live region, announced or in a titled pane that appeared
with them; 0 items in a live region, 0 announcements and 0 toasts in the
recording." Below the row "App com.pss.novasleepcare, version 1.0.5
(code 65), targets API 36: 5 screens, 7 visits" was the row "No live
region in the app (WCAG 4.1.3): 1 warning", and below it the finding
"Warning: FrameLayout containing "Welcome, Bob Dodd", "3", "How did you
sleep last night?", in its 7 visits in this recording, no item of the
app was in a live region, and it made no announcement and showed no
toast; look for status messages, such as saved, sent, loading, progress
or error messages, that should be in a live region; seen once, at
00:00:02.655". Under visit 2 ("45 snapshots, 1 settled") the row "Status
message not announced (WCAG 4.1.3): 1 error" was followed by the
finding "Error: View described as "Loading your personal sleep learning
space.", it was shown with "☀️🌙" and they went away by themselves 0.41
seconds later, with no touch between, as a status message does; it is
not in a live region, no announcement was made and no titled pane
appeared with it, so a screen reader is not told of it; seen once, at
00:00:22.575", with the loading text outlined on the phone's picture
when chosen. Long rows wrapped, with no horizontal scrollbar. The
status message checks are checked on the PC.

## Step 13: timing (2026-11-13)

The owner's decisions, what the standards and Android say, and the
checks built to them are in the architecture document's "Step 13:
timing". Four checks, each one discrete issue: text gone by itself with
nothing new shown (2.2.1) is a warning when the phone's "Time to take
action" is Default and an error when it was set and the app did not
keep the text up for it; text gone as new content appeared (2.2.1),
text updating in place by itself (2.2.2) and content moving by itself,
from the phone's video (2.2.2), are warnings. Checked in the Linux build
environment: the solution builds with no warnings, and the new tests
pass with the others. The throwaway program gives, for test 16's
recording (`20261112-154851-32c7b61a5552453bbf5febffcca978c7`), three
"Text gone as new content appeared" warnings, in visits 2, 3 and 5, the
first gone 0.41 seconds after it was shown, and nothing from the other
three checks.

## Test 44: build and install the sixteenth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 45: timing in test 16's recording

Keep the phone's picture in view throughout.

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7`. If the Checks
   window is closed, click the "Checks" tab, then move it so the phone's
   picture is fully in view. Wait 10 seconds, and take a screenshot of
   the whole screen.
2. Scroll the list so the row starting "Visit 2," is at the top, then
   scroll down until the row starting "Text gone as new content
   appeared" is in view, and take a screenshot of the whole screen.

**On screen:** in step 1 the line under "Show current visit" includes
"Timing (WCAG 2.2.1 and 2.2.2), four checks: 3 findings (3 warnings);
Time to take action was Default, so texts gone within 5 seconds were
looked at: 3 texts with no touch between, 0 of them gone with nothing
new shown as they went; 0 items whose text changed in place with no
touch; 0 parts of the phone's video that kept changing for more than 5
seconds with no touch (from the video, so approximate)." In step 2,
below the Visit 2 row, is the row "Text gone as new content appeared
(WCAG 2.2.1): 1 warning", and below it a row that includes "View
described as "Loading your personal sleep learning space.", it was shown
with "☀️🌙" and they went away 0.41 seconds later, with no touch
between, as new content appeared, as when loading finishes; it went
without warning, and the recording cannot show that it was not on a
timer". Long rows wrap; there is no horizontal scrollbar. Send the two
screenshots.

## Results of tests 44 and 45 (2026-11-13)

Passed on the development PC with the build of 9af870b, as recorded by
the owner, with AAA chosen (2.2.1 and 2.2.2 are level A, so they run at
both levels). The line under "Show current visit" included "Timing (WCAG
2.2.1 and 2.2.2), four checks: 3 findings (3 warnings); Time to take
action was Default, so texts gone within 5 seconds were looked at: 3
texts with no touch between, 0 of them gone with nothing new shown as
they went; 0 items whose text changed in place with no touch; 0 parts of
the phone's video that kept changing for more than 5 seconds with no
touch (from the video, so approximate)." Under visit 2 ("45 snapshots, 1
settled"), after the row "Status message not announced (WCAG 4.1.3): 1
error" and its finding, the row "Text gone as new content appeared
(WCAG 2.2.1): 1 warning" was followed by the finding "Warning: View
described as "Loading your personal sleep learning space.", it was shown
with "☀️🌙" and they went away 0.41 seconds later, with no touch between,
as new content appeared, as when loading finishes; it went without
warning, and the recording cannot show that it was not on a timer; seen
once, at 00:00:22.575", with the loading text outlined on the phone's
picture when chosen. Long rows wrapped, with no horizontal scrollbar.
The timing checks are checked on the PC.

## Step 14: changes of context (2026-11-13)

The owner's decisions, what the standards say, and the checks built to
them are in the architecture document's "Step 14: changes of context".
Three checks, each one discrete issue: a change of context after the
focus moved by a key or a tap on a field (3.2.1) and after TalkBack's
focus moved by a gesture with no double tap (3.2.1) are errors; a
change of context after a setting changed, such as a switch tapped or
text typed (3.2.2), is a warning. Checked in the Linux build
environment: the solution builds with no warnings, and the new tests
pass with the others. The throwaway program gives no finding for any
recording to hand, as none has a key, typing, or a tap on a field,
switch, check box or slider; test 48 makes one that should have a
finding.

## Test 46: build and install the seventeenth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 47: changes of context in test 16's recording

Keep the phone's picture in view throughout.

1. Choose "Open recording...", and open
   `20261112-154851-32c7b61a5552453bbf5febffcca978c7`. If the Checks
   window is closed, click the "Checks" tab, then move it so the phone's
   picture is fully in view. Wait 10 seconds, and take a screenshot of
   the whole screen.

**On screen:** the line under "Show current visit" includes "Changes of
context (WCAG 3.2.1 and 3.2.2), three checks: no findings; 0 focus
moves by a key or a tap on a field, 0 TalkBack focus moves after a
gesture or key, and 0 setting changes (typing, checking, choosing or
moving a slider); 0 changes of context (a new screen or app, a window
that took the focus, a pane, or the focus moving by itself) within 1.5
seconds of one, with nothing touched, pressed or clicked between." Long
rows wrap; there is no horizontal scrollbar. Send the screenshot.

## Test 48: a switch that opens a message, in Settings

Keep the phone's picture in view throughout.

Before the test, make sure TalkBack is off. On the phone, open
Settings, then Accessibility, then Select to Speak, so that the "Select
to Speak shortcut" row and its switch, which is off, are on the screen.
Leave the recorder open from test 47, with "Android device" chosen.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. On the phone, tap the switch at the right end of the "Select to
   Speak shortcut" row once, and wait 5 seconds.
3. In the message that appears, tap its button ("Got it" when Select to
   Speak is already allowed, as on the owner's phone; "Deny" if it asks
   to let Select to Speak have full control), and wait 5 seconds.
4. Choose "Stop recording", and wait until the recording has opened in
   the player.
5. If the Checks window is closed, click the "Checks" tab, then move it
   so the phone's picture is fully in view. Wait 10 seconds, scroll the list until the row starting
   "Change of context on input" is in view, and take a screenshot of the
   whole screen.

**On screen:** in step 2 a message appears, and in step 3 it goes. In step 5 the line under "Show current visit"
includes "Changes of context (WCAG 3.2.1 and 3.2.2), three checks: 1
finding (1 warning);" and "1 setting change"; the list has the row
"Change of context on input (WCAG 3.2.2): 1 warning", and below it a row
starting "Warning:" that includes "seconds later, with no further touch
or key," and ends its measurement with "a change of context on input,
which is allowed only when the user was told of it before using the
item; look for such advice". No such row is a failure; send the
screenshot as it is. Long rows wrap; there is no horizontal scrollbar.
Send the screenshot.

## Results of test 48 (2026-11-13)

Failed on the development PC with the build of 8adb57f (session
`20261113-200327-fdc058f1017a4496b2158acb591c6db4`): the Checks window
had no "Change of context on input" row. The test's own steps were also
wrong for the owner's phone: Select to Speak was already allowed, so no
permission message appeared; the switch turned on and a "Quick Settings
shortcut" dialog with "Got it" appeared instead, and the owner tapped
"Got it", leaving the switch on. Measured on the recording: the tap was
seen as a change of setting, and the dialog took the focus 0.23 seconds
after it, but Android listed the dialog's window, untitled, without the
Settings window under it, so the dialog was not a state of the visit,
and the first build did not count it as a change of context. Fixed: a
window of an app's that appears and takes the focus is a change of
context whether or not the window under it is listed (see the
architecture document's "Step 14: changes of context"). With the fix,
the recording gives one warning. Steps 2 and 3 above are corrected.

## Test 49: build and install the eighteenth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 50: the change of context in test 48's recording

Keep the phone's picture in view throughout.

1. Choose "Open recording...", and open
   `20261113-200327-fdc058f1017a4496b2158acb591c6db4`. If the Checks
   window is closed, click the "Checks" tab, then move it so the phone's
   picture is fully in view. Wait 10 seconds, scroll the list until the
   row starting "Change of context on input" is in view, and take a
   screenshot of the whole screen.

**On screen:** the line under "Show current visit" includes "Changes of
context (WCAG 3.2.1 and 3.2.2), three checks: 1 finding (1 warning); 0
focus moves by a key or a tap on a field, 0 TalkBack focus moves after
a gesture or key, and 1 setting change (typing, checking, choosing or
moving a slider); 1 change of context (a new screen or app, a window
that took the focus, a pane, or the focus moving by itself) within 1.5
seconds of one, with nothing touched, pressed or clicked between." The
list has the row "Change of context on input (WCAG 3.2.2): 1 warning",
and below it the row "Warning: Switch described as "Shortcut settings",
a tap changed its setting (it was not checked), and 0.23 seconds later,
with no further touch or key, an untitled window appeared and took the
focus, a change of context on input, which is allowed only when the
user was told of it before using the item; look for such advice; seen
once, at" a time from 00:00:08.983 to 00:00:09.209 (the first picture
between the tap and the dialog). Long rows wrap; there is no horizontal
scrollbar. Send the screenshot.

## Results of tests 49 and 50 (2026-11-13)

Test 50 passed on the development PC with the build of e67814e, as
recorded by the owner (with AA chosen; 3.2.1 and 3.2.2 are level A, so
they run at both levels). The line under "Show current visit" included
"Changes of context (WCAG 3.2.1 and 3.2.2), three checks: 1 finding (1
warning); 0 focus moves by a key or a tap on a field, 0 TalkBack focus
moves after a gesture or key, and 1 setting change (typing, checking,
choosing or moving a slider); 1 change of context (a new screen or app,
a window that took the focus, a pane, or the focus moving by itself)
within 1.5 seconds of one, with nothing touched, pressed or clicked
between." The row "Change of context on input (WCAG 3.2.2): 1 warning"
was followed by "Warning: Switch described as "Shortcut settings", a tap
changed its setting (it was not checked), and 0.23 seconds later, with
no further touch or key, an untitled window appeared and took the
focus, a change of context on input, which is allowed only when the
user was told of it before using the item; look for such advice; seen
once, at 00:00:09.203", with the phone's picture showing the "Quick
Settings shortcut" dialog. The test as first written expected
00:00:08.983, the tap's time; the player shows a finding at the first
picture between the action and the change, the phone's screenshot of
the dialog, so the test's text is corrected and the result is a pass.
Long rows wrapped, with no horizontal scrollbar. Test 49's build is
taken as passed, as the recorder ran it. Test 47's screenshot was not
sent. The changes of context checks are checked on the PC.

## Step 15: orientation (2026-11-13)

The owner's decisions, what the standard and Android say, and the check
built to them are in the architecture document's "Step 15:
orientation". One check, a warning at both audit levels: the phone held
turned, with Auto-rotate on, for at least the time set in the Checks
tab's "Phone turned for:" (2 seconds by default), with the screen not
turning with it. How the phone is held comes from a new companion app,
protocol 7, which reads the phone's own sensor, so the companion app
must be installed again (test 52), and the recordings to hand, made
without it, give "not looked for". Checked in the Linux build
environment: the solution builds with no warnings, and the new tests
pass with the others. The companion app's Java cannot be built there;
it is built by the installer's build on the PC.

Tests 53 and 54 are the first to turn the phone. They need Auto-rotate
on: with it off, the check is not looked for.

## Test 51: build and install the nineteenth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 52: install the companion app for protocol 7

1. Connect the Pixel by USB, unlocked. In the recorder, in "Recording
   target", choose "Android device".
2. The panel offers "Install companion app". Choose it, and wait until
   the companion line stops changing.
3. Take a screenshot of the panel.

**On screen:** in step 3 the companion line reads "Companion app:
installed, on, and answering (version 1.1.0, version code" with a
number, and ends "protocol 7)." "protocol 6" is a failure. If the line
says the service is off instead, that is a result too; send the
screenshot either way.

## Test 53: the phone turned on the home screen

Keep the phone's picture in view throughout.

Before the test, make sure TalkBack is off. On the phone, swipe down
twice from the top of the screen and make sure the "Auto-rotate" tile is
on, then go to the home screen. Hold the phone upright. Leave the
recorder open from test 52, with "Android device" chosen.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. Turn the phone a quarter turn onto its side, hold it still on its
   side for 5 seconds, then turn it back upright and wait 5 seconds.
3. Choose "Stop recording", and wait until the recording has opened in
   the player.
4. If the Checks window is closed, click the "Checks" tab, then move it
   so the phone's picture is fully in view. Wait 10 seconds, scroll the
   list until the row starting "Orientation restricted" is in view, and
   take a screenshot of the whole screen.

**On screen:** in step 2, note whether the home screen turned with the
phone; Pixel's home screen may stay upright, which is what this test
expects. In step 4 "Phone turned for:" shows "2 seconds", and the line
under "Show current visit" includes "Orientation (WCAG 1.3.4), one
check: 1 finding (1 warning); the phone was turned between upright and
sideways 2 times, from the companion app's own sensor". The list has
the row "Orientation restricted (WCAG 1.3.4): 1 warning", and below it
a row starting "Warning:" that includes "the phone was held sideways
for" and "seconds with Auto-rotate on, and the screen stayed portrait".
If the home screen turned in step 2, there is no such row; say so and
send the screenshot as it is. "not looked for" on the line is a
failure; send the screenshot with the line in view. Long rows wrap;
there is no horizontal scrollbar. Send the screenshot.

## Test 54: the phone turned in Settings

Keep the phone's picture in view throughout.

Before the test, make sure TalkBack is off and the "Auto-rotate" tile is
on, as in test 53. On the phone, open Settings, on its first screen.
Hold the phone upright. Leave the recorder open from test 53.

1. In the recorder, choose "Start recording", and wait 5 seconds.
2. Turn the phone a quarter turn onto its side, hold it still on its
   side for 5 seconds, then turn it back upright and wait 5 seconds.
3. Choose "Stop recording", and wait until the recording has opened in
   the player.
4. If the Checks window is closed, click the "Checks" tab, then move it
   so the phone's picture is fully in view. Wait 10 seconds, and take a
   screenshot of the whole screen.

**On screen:** in step 2 Settings turns sideways with the phone, and
upright again. In step 4 the line under "Show current visit" includes
"Orientation (WCAG 1.3.4), one check: no findings; the phone was turned
between upright and sideways 2 times, from the companion app's own
sensor, and the screen stayed as it was, with Auto-rotate on, for 2
seconds or more after 0 turns within a visit; it is looked for only
while Auto-rotate is on." The list has no row starting "Orientation
restricted". Long rows wrap; there is no horizontal scrollbar. Send the
screenshot.

## Results of tests 51 to 53 (2026-11-13)

Tests 51 and 52 passed on the development PC with the build of 329940d:
the companion line read "Companion app: installed, on, and answering
(version 1.1.0, version code 824, protocol 7)." So the companion app's
Java builds, and its accessibility service reads the phone's sensor
while in the background, which was the feasibility question.

Test 53 passed (session
`20261113-214438-2e4d9f29c24e4576b249934b206a16e1`): the home screen
stayed upright, "Phone turned for:" showed "2 seconds", and the list had
"Orientation restricted (WCAG 1.3.4): 1 warning" with "Warning:
FrameLayout containing "At a glance", "Fri, Nov 13", "2°C", the phone
was held sideways for 6.6 seconds with Auto-rotate on, and the screen
stayed portrait, ... seen once, at 00:00:08.622". The line said the
phone was turned "3 times", where the test expected 2. Measured on the
recording: upright at 0.2 seconds, sideways (60 degrees) at 8.623,
upright (30 degrees) at 15.263, then, from 18.842 to 19.622, as the
phone was put down, readings flicked between flat, upright and sideways
(72 and 110 degrees) for under half a second each. That brief sideways
reading is the third turn; it was far shorter than the hold time, so it
gave no finding. The count is of every change the sensor reported, not
of turns held. The owner chose to leave the count as it is, so test 53's
expected line should be read as "2 times or more" when the phone moves
while being put down.

## Result of test 54 (2026-11-13)

Test 54 passed (session
`20261113-215419-44a271ec63364cfeab5bfbc72a4fb1f6`): the line read
"Orientation (WCAG 1.3.4), one check: no findings; the phone was turned
between upright and sideways 3 times, from the companion app's own
sensor, and the screen stayed as it was, with Auto-rotate on, for 2
seconds or more after 0 turns within a visit; it is looked for only
while Auto-rotate is on.", and the list had no row starting
"Orientation restricted". Measured on the recording: the sensor read
sideways at 7.615 seconds and the display turned to 270 at 7.898; it
read upright at 14.256 and the display turned back to 0 at 14.545. The
third turn is a sideways reading of under half a second from 20.876, as
the phone was put down, as in test 53. Step 15 has its acceptance on the
development PC.

## Step 16: white space used for layout (2026-11-13)

The owner's decisions, what the standard says, and the checks built to
them are in the architecture document's "Step 16: white space used for
layout". Three checks of WCAG 1.3.2, each an error at both audit
levels, from the tree's text: letters of a word spaced apart (F32),
columns made with white space (F33), and a line break in mid-sentence.
They read the trees already recorded, so no new companion app is
needed. Measured on 45 phone recordings to hand first: none of their
493 distinct texts gives a finding. Checked in the Linux build
environment: the solution builds with no warnings, and the new tests
pass with the others.

The phone shows each of the three on a test page, the plain text file
`docs/validation/fixtures/white-space-layout.txt`, which Chrome shows
as it is, read from GitHub:

```
W e l c o m e
Name      Hours
Bob       7.5
Ann       8
This sentence is broken
across two lines for its layout.
```

## Test 55: build and install the twentieth build (development PC)

As test 19: close the recorder, run the PowerShell of test 19's step 1,
run the newest installer with "Start the recorder and set up the
analysis models" unchecked, and start the recorder from the Start menu.

**On screen:** the build ends with `INSTALLER BUILT`, and the recorder
opens.

## Test 56: the test page in Chrome

Keep the phone's picture in view throughout.

Before the test, make sure TalkBack is off. Connect the Pixel by USB,
unlocked. Leave the recorder open from test 55, with "Android device"
chosen in "Recording target".

1. In PowerShell on the PC, run:

   ```powershell
   $adb = Join-Path $env:USERPROFILE "windows-a11y-recorder\artifacts\android-checks\tools\scrcpy-win64-v5.0.1\adb.exe"
   & $adb shell am start -a android.intent.action.VIEW -d "https://raw.githubusercontent.com/bobdodd/windows-a11y-recorder/main/docs/validation/fixtures/white-space-layout.txt" com.android.chrome
   ```

2. On the phone, Chrome shows a page of 6 lines starting "W e l c o m
   e". In the recorder, choose "Start recording", and wait 10 seconds.
3. Choose "Stop recording", and wait until the recording has opened in
   the player.
4. If the Checks window is closed, click the "Checks" tab, then move it
   so the phone's picture is fully in view. Wait 10 seconds, scroll the
   list until the row starting "Letters spaced apart" is in view, and
   take a screenshot of the whole screen.

**On screen:** in step 2 the page shows the 6 lines above. In step 4 the
line under "Show current visit" includes "White space used for layout
(WCAG 1.3.2), three checks: 3 findings (3 errors)". The list has, under
Chrome's screen, the rows "Letters spaced apart with white space (WCAG
1.3.2, F32): 1 error", "Columns made with white space (WCAG 1.3.2, F33):
1 error" and "Line break in mid-sentence (WCAG 1.3.2): 1 error", each
with a row below it starting "Error:"; the first includes "7 single
letters separated by spaces", the second "3 of the 6 lines", and the
third "between "This sentence is broken" and "across two lines for"".
If none of the rows is there, say so and send the screenshot as it is:
Chrome may give the page's lines as separate items, which the recording
will show. Long rows wrap; there is no horizontal scrollbar. Send the
screenshot.

# Validation plan: colours and contrast in the recorder's window

These tests check the changes in
[colours and contrast](../architecture/player-contrast.md) on the
development PC. They use the recording of the phone made in test 64 of
the [Android recording plan](android-recording-plan.md),
`20261111-002648-06be83ce21ec47309bd4bc68b4ae1cb4`. Take every screenshot
of the whole screen; the contrast is measured from them.

What was checked before these tests, in the Linux build environment:
the solution builds with no warnings, and 2,153 tests pass, with 1
skipped and the 10 that need Windows failing there as before. No test
was added: nothing there draws the window, so none of that shows the
colours. Tests 1 to 5 do.

## Test 1: build, install, and open the recording (development PC)

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
3. Start the recorder from the Start menu, choose "Open recording...",
   and open `20261111-002648-06be83ce21ec47309bd4bc68b4ae1cb4`.
4. When the recording has opened in the player, take a screenshot of
   the whole screen.

**On screen:**
- The build ends with `INSTALLER BUILT`.
- In step 4 the menu bar is a lighter strip than the window, with a grey
  line under it, and "Analysis" and "Android" are in semibold, each in a
  box with a grey edge.
- The group boxes, such as "Properties" and "Selected event values",
  have grey edges that stand out from the dark panels.
- The splitters between the panels are grey bars, including the one
  between the tabs and "Selected event values".
- The properties panel's text, such as "Magnifier", "Full screen level"
  and "not recorded", is black on white.
- Pale grey text on white anywhere is a failure.

Send the screenshot.

## Test 2: the tree and the timeline

The recording from test 1 is open in the player.

1. In the timeline, click the first teal block in the strip just above
   the bottom lane, the one that reads "Digital Wellbeing".
2. Click the "Accessibility tree" tab, choose "Show accessibility
   focus", and take a screenshot of the whole screen.

**On screen:**
- The tree's rows, such as "LinearLayout containing "Digital
  Wellbeing"…", are black on white.
- The lines under the window rows, such as "items taken at", are dark
  grey on white.
- "accessibility focus" is bold and black, with the purple bar on the
  row's left.
- The teal blocks in the timeline are a slightly brighter teal than
  before, with white text.
- Pale grey text on white is a failure.

Send the screenshot.

## Test 3: the tree in its own window

The recording from test 1 is open in the player, with the "Accessibility
tree" tab chosen as test 2 left it.

1. Choose the button beside the "Accessibility tree" tab's name that
   opens it in its own window, and take a screenshot of the whole
   screen.
2. Close the "Accessibility tree" window.

**On screen:** in step 1 the window's tree has the same black text on
white as in test 2, and the line "4 windows, 209 items." above it is
light grey on the dark background. Pale grey text on white is a
failure. Send the screenshot.

## Test 4: the menus

The recording from test 1 is open in the player.

1. Click "Analysis" in the menu bar, and take a screenshot of the whole
   screen.
2. Press Escape. Click "Android" in the menu bar, and take a screenshot
   of the whole screen.
3. Press Escape.

**On screen:** in step 1 the menu lists "Summarise this recording" and
the other items in black on Windows' light menu, in normal weight, not
semibold. In step 2 the menu lists "Set up Android recording..." the
same way. Send the screenshots.

## Test 5: the other tabs

The recording from test 1 is open in the player.

1. Click the "NVDA had to announce" tab, and take a screenshot of the
   whole screen.
2. Click the "Reading position steps" tab, and take a screenshot of the
   whole screen.
3. Click the "Browser navigation correlation" tab, and take a screenshot
   of the whole screen.
4. Click the "Analysis" tab, and take a screenshot of the whole screen.

**On screen:**
- In each, text on the dark tab is light grey, and text in a white list
  or text box is black.
- A recording of the phone has no NVDA announcements, reading steps or
  browser navigations, so those lists are empty.
- In step 1 the line above the list starts "This recording holds no
  reading position steps", in light grey on the dark tab.
- In step 2 the line above the list starts "This recording holds no
  requests to the page", in light grey on the dark tab.
- In step 3 the white box reads "This recording contains no browser
  navigation evidence." in black.
- Pale grey text on white is a failure.

Send the screenshots.

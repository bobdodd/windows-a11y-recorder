# MP4 export: validation plan

Written 2026-11-07 for the export described in
[the MP4 export design](../architecture/mp4-export.md). Not yet run in
the app. Each test checks one behaviour in at most five steps, with what to
see on the screen.

What was checked before this plan:

- In the Linux build environment: the solution builds with no warnings;
  the 19 export tests pass, and the evidence table tests pass with the
  screen reader samples.
- On the development PC (Windows 10 build 19045), with a test program
  using the export code outside the app: an MP4 of generated frames and
  sound, and one of the recording `20261015-181011-b7dc...`, each with
  H.264 video and three AAC sound tracks, checked frame by frame and
  sound by sound in the build environment. See "Checks made" in the
  design.

None of that shows the export window, the third slider, or a recording
with a screen reader track. Tests 1 to 6 do.

## Before the tests

Build and install the new revision as in tests 1 and 2 of
[the installer validation plan](windows-installer-plan.md), on the
development PC. Installing over the earlier install keeps the recordings,
settings and models. Every PowerShell window is a normal one, not "Run as
administrator".

## Test 1: the export window (development PC)

1. Start the recorder and choose "Open recording...".
2. Open the recording from 2026-10-15 18:10, `20261015-181011-b7dc...`.
3. Choose "Save as MP4..." (Alt+4).
4. Read the window.

**On screen:** in the player, the volume row has a third slider, "Screen
reader:", after "System audio:". The window's heading is "Save the recording as
MP4", then "The whole recording, 0:20, as H.264 video of 1920 by
1080 pixels at 20 frames a second." Under "Sound": "Microphone, mixed at
0 dB" and "System sound, mixed at 0 dB" (or the levels of the player's
sliders), both checked, then the paragraph beginning "This recording has
no separate screen reader sound". Under "File": a path ending
`\Videos\20261015-181011-b7dc5bf4a30d4329ab68a7e28eb2a984.mp4`.

## Test 2: save the MP4

In the window from test 1:

1. Choose "Save MP4".
2. Wait for the status line to stop changing.
3. Read the status line.

**On screen:** while saving, the progress bar fills and the status line
reads "Saving the MP4: ... of ..., ...%". At the end: "Saved
C:\Users\User\Videos\20261015-....mp4: 1920 by 1080 pixels, 3 sound
tracks: mixed, then microphone, system sound. Encoded with the Windows
encoder, graphics card allowed." Lines about silences may follow. Any
line beginning "The MP4 could not be saved" is a failure; send it.

## Test 3: the video matches the player

1. In File Explorer, open the Videos folder, right-click the MP4, choose
   Properties, then the Details tab.
2. Read "Length".
3. Open the MP4 in Media Player and drag to 0:10. Pause.
4. In the recorder, drag the playback slider to 00:00:10.000 and pause.
5. Compare the two pictures.

**On screen:** "Length" is the recording's length, to the second, as the
player's time shows after "/". The two pictures at 0:10 show the same
screen: the same windows, pointer position and text.

## Test 4: Stop leaves no file

1. In the recorder, choose "Save as MP4..." again.
2. In "File", change the name to `stopped-test.mp4`.
3. Choose "Save MP4", and choose "Stop" before the progress bar is half full.
4. In File Explorer, open the Videos folder.

**On screen:** the status line reads "Stopped. No MP4 was saved." The
Videos folder has no `stopped-test.mp4` and no `stopped-test.mp4.partial`.

## Test 5: the whole screen picture

Needs a recording made with full screen Magnifier zoomed in, such as one
from the magnifier checks of 2026-10-07.

1. Open that recording.
2. Turn on "Whole screen".
3. Choose "Save as MP4...", read the "Picture:" line, and choose "Save MP4".
4. Open the MP4 in Media Player at a moment when Magnifier was zoomed in.

**On screen:** the "Picture:" line begins "Picture: the whole screen as
captured, with the participant's view outlined". The video shows the
whole screen, unmagnified, with the outline the player draws around the
participant's view.

## Test 6: a screen reader track (Windows build 20348 or later)

Needs a PC on Windows 11 or another build of 20348 or later, such as the
Alienware if it runs Windows 11. On Windows 10 the screen reader cannot be
separated, and the recording has no screen reader track.

1. Open PowerShell and check the build:

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
   [Environment]::OSVersion.Version.Build
   ```

2. Start NVDA, then start the recorder with "System audio" checked and
   choose "Start recording".
3. Use Tab a few times in any window so that NVDA speaks, then choose
   "Stop recording".
4. Open the new recording and choose "Save as MP4...".
5. Choose "Save MP4" and read the status line.

**On screen:** in step 1, a number of 20348 or more. In step 4, the window
lists "Screen reader, mixed at 0 dB" after "System sound", and has no
paragraph about no separate screen reader sound. In step 5: "4 sound
tracks: mixed, then microphone, system sound, screen reader."

## After the tests

To check where the sound was placed in the MP4s of tests 2 and 6, say
where they are; they can be read on the PC and checked track by track in
the build environment, as the test program's were.

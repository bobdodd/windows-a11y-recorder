# Magnified View Playback, Target Machine Runs, 2026-10-07

## Question

Whether the build of [magnified view playback](../architecture/magnified-view-playback.md)
records Windows Magnifier's full screen transform with each desktop frame,
and whether the participant's view drawn from it matches what was on the
screen.

## Method

- Target machine: Windows 10 Pro 22H2, build 19045, one 1920 by 1080
  monitor. Recorder package ad5687f.
- `scripts/Test-MagnifiedPlayback.ps1` ran from 03:05:18 to 03:06:32 UTC
  during recording `20261008-030447-d7fa391dbe1343c79c6b68e5d4ee87a5`
  (837 desktop frames), with phases of 8 seconds: no Magnifier; full
  screen; pan; two steps of zoom; inverted colors; lens; docked; closed.
  In each phase it sampled the transform and the full screen color effect
  from its own process every 250 ms and took a GDI screenshot.
- The owner reported that each phase looked as its name says, and that
  the screen stayed magnified after the recording was stopped with the
  full screen view on.
- The script stopped when no session ID was typed. The recording was
  found as the newest in the recordings folder, its events exported in
  the sandbox, and the script's analysis run on the target machine with
  the exported events.

## Results

- Readings: 47 of 837 frames held a transform; 790 held the problem
  "MagGetFullscreenTransform failed with error 21." The readings held were
  scattered through the recording. Every one matched the script's
  samples: level 2 at offset 480, 270 in the full screen phase, level 4 at
  227, 122 in the zoomed phase.
- Cause, tested on the target machine with a small program: the reading
  succeeded on the thread that called `MagInitialize`, and failed with
  error 21 on another thread, and on another thread after its own
  `MagInitialize`. The build read on whichever pool thread ran each
  frame.
- The frames nearest each screenshot all held the problem, so the
  script's comparison could not test the participant's view. Two frames
  that held level 2 at 480, 270, frames 220 and 282, were compared with the
  full screen screenshot in the same way, 160 by 90 grey thumbnails:

| Frame | Participant's view against the screenshot | Frame as captured against the screenshot |
|---|---|---|
| 220 | 0.11 | 15.69 |
| 282 | 0.09 | 15.57 |

- Pan: the transform stayed at 480, 270 and the pan screenshot was the
  same file content as the full screen one. The script moved the pointer
  with `SetCursorPos`, which Magnifier did not follow.
- Inverted colors: the script read a full screen color effect other than
  the identity during the phase, and the captured frame differed from
  the screenshot by 201.66. Windows Magnifier's inverted colors use the
  full screen color effect, and the capture does not show them.
- Lens, docked and closed: the frames matched the screenshots, 0.41 to
  0.49.
- Stopping the recording left the full screen view on.

## Changes

- The frame loop runs on one dedicated thread, so `MagInitialize` and
  every reading are on the same thread; the reader refuses a reading on
  another thread.
- The script moves the pointer with mouse input, which Magnifier
  follows, and uses the newest recording when no session ID is typed.

## Second run

- Recorder package faa2890, with the changes above. The script ran at
  03:20 UTC during recording
  `20261008-032022-78482e571ba348ec8bbc8032385e385f` (608 desktop frames)
  and found the recording itself when no session ID was typed.
- Every one of the 608 frames held a transform, and the levels in each
  phase matched the script's samples.
- The participant's view drawn from the frame nearest each screenshot,
  against that screenshot, 160 by 90 grey thumbnails, tolerance 3:

| Phase | Frame | Transform | Participant's view | Frame as captured |
|---|---|---|---|---|
| No Magnifier | 177 | level 1 | 0.41 | 0.41 |
| Full screen | 231 | level 2 at 480, 270 | 0.11 | 17.16 |
| Pan | 272 | level 2 at 960, 527 | 0.34 | 17.09 |
| Zoomed | 318 | level 4 at 1419, 783 | 0.37 | 22.32 |
| Inverted colors | 361 | level 4 at 889, 500 | 234.07 | 230.87 |
| Lens | 410 | level 1 | 0.41 | 0.41 |
| Docked | 454 | level 1 | 0.49 | 0.49 |
| Closed | 508 | level 1 | 0.41 | 0.41 |

- Each frame was within 95 ms of its screenshot.
- Every phase but inverted colors is within tolerance. Inverted colors
  fails as expected: the color effect read was not the identity and is
  not recorded.
- The owner reported that each phase looked as its name says and that
  the screen stayed magnified after the recording was stopped with the
  full screen view on.
- The player checks (the toggle, the outline, announcements and help
  text) were not reported with this run.

## Player

- The owner reported that the player showed the full screen, pan, and
  inverted colors phases unmagnified, that the grid never changed size
  except inside the lens and the docked view, and that "Whole screen"
  stayed disabled.
- Cause: a recording file is played through its playback index, which
  keeps only the payload properties playback reads
  (`SessionPlaybackArchiveBuilder.PayloadProperties`).
  `fullscreenMagnification`, `x`, and `y` were not among them, so every
  frame reached the player with no reading. The archive builder test fed
  whole payloads to the builder and did not pass through the index.
- Fix: the three properties are added, and the playback index is version
  4, so the index stored in a recording made before it is derived again
  when the recording is opened. Opened with the fix in the sandbox, with
  empty files standing in for its frames, recording
  `20261008-032022-78482e571ba348ec8bbc8032385e385f` gave 608 frames, all
  with a reading, the toggle offered, and the part seen at full screen
  480, 270, 960 by 540; at the pan 960, 527; zoomed 1419, 783, 480 by 270;
  inverted 889, 500, 480 by 270; and the whole frame at level 1.
- A test now plays frames from a recording file through its index
  (`PlaysEachFramesMagnificationReadingFromARecordingFile`); it fails
  without the fix.

- With package 9ce9cc0, the owner reported on the same recording that the
  full screen, pan, and zoomed phases show the grid magnified; that
  "Whole screen" is enabled and shows the frame as captured with the
  yellow outline; and that the lens and docked phases look as recorded.
  The owner's summary: it appears correct. The help text was not checked
  with a screen reader in this run.

## Third run: the color effect, 2026-10-08

- Recorder package 799b744, recording the color effect with each frame.
  The extended script ran during recording
  `20261008-133442-7062eda5083c42e3b3940a50ff9bb5a2` (764 frames, every one
  with a transform and a color effect), with inverted colors phases at
  level 4, at 100 percent, in the lens view, and in the docked view.
- Every frame's matrix matched the script's samples in its phase.
- The matrix Windows Magnifier set for inverted colors, read by the
  script and recorded in the frames, was
  -1 0 0 0 0 / 0 -1 0 0 0 / 0 0 -1 0 0 / 0 0 0 1 0 / 1 1 1 0 1: each color
  channel 1 less itself, alpha kept, under the row vector reading.
- The participant's view against each screenshot, without and with the
  frame's color effect applied (160 by 90 grey thumbnails, tolerance 3):

| Phase | Level | Effect in the frame | Without | With | Frame as captured |
|---|---|---|---|---|---|
| Inverted | 4 | inversion | 234.07 | 0.32 | 230.87 |
| Inverted at 100 percent | 1 | inversion | 230.79 | 0.41 | 230.79 |
| Inverted in the lens view | 1 | identity | 0.41 | 0.41 | 0.41 |
| Inverted in the docked view | 1 | identity | 0.49 | 0.49 | 0.49 |

- Full screen, pan, zoomed, lens, docked, closed and no Magnifier were
  within tolerance, with the identity in every frame; the frames before
  the inverted phases matched as in the second run (0.11 to 0.41).
- In the lens and docked views Magnifier left the full screen effect at
  the identity, and the capture showed the inversion: the screenshots of
  those phases had 21.12 and 23.71 percent of sampled pixels dark,
  against 13.12 and 12.87 without inversion, and the frames as captured
  matched them.
- The owner reported that every phase looked as its name says and that
  the screen stayed magnified after stopping.
- Answers to the design's open questions: the effect is set only by the
  full screen view, at any level including 100 percent, and the capture
  does not show it; in the lens and docked views it is not set and the
  capture shows the inversion. The player therefore applies an effect
  other than the identity whenever a frame holds one.
- Applying the matrix to a 1920 by 1080 frame, timed on the target
  machine with a self-contained build of `ColorEffect.ApplyBgra`: 3.6 ms
  for the inversion, by its per-channel path, and 23.2 ms for Microsoft's
  grayscale example, by the general path.

## The player's color effect, 2026-10-08

- Recorder package 523e905, the third run's recording opened in the
  player. The owner reported all four cases correct: inverted at 400
  percent shown zoomed and inverted with the help text naming inverted
  colors; inverted at 100 percent shown inverted over the whole frame with
  the whole screen toggle available; the inverted lens and docked views
  shown as captured; and with the whole screen toggle on, the frame as
  captured with the help text naming the colors the participant saw.
- Showing the level and position visibly is part of the properties
  panels logged with [accessibility preferences](../architecture/accessibility-preferences.md).

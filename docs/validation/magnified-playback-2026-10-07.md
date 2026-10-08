# Magnified View Playback, First Run, 2026-10-07

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

## Open

- A second run with the change, for the readings, the pan and the second
  level.
- Recording the color effect is proposed, not built.

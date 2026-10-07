# Playback of the Magnified View

## Status

Proposed 2026-10-07, not agreed and not built.

## Purpose

An auditor reviewing a recording needs to see each moment as the
participant saw it. With Windows Magnifier's full screen view on, the
recorder's desktop frames show the unmagnified screen, while the lens and
docked views are captured as seen
([Magnifier capture validation](../validation/magnifier-capture-2026-10-07.md)).
The owner's requirement (2026-10-07):

- Playback shows the participant's view by default, with the recorded
  magnification applied.
- A toggle shows the whole screen, as captured, for debugging.
- The magnification level and position are therefore recorded over time.

The desktop frames stay as captured. The participant's view is drawn from
them at playback and is not stored, so the evidence is unchanged and the
whole screen remains available.

## What Windows reports

- `MagGetFullscreenTransform` returns the full screen magnification level,
  1.0 meaning none, and the offsets of the magnified view
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-maggetfullscreentransform)).
- The offset is "for the upper-left corner of the magnified view", and is
  "relative to the upper-left corner of the primary monitor, in
  unmagnified coordinates"; with several monitors, the offsets are adjusted
  by "the upper-left corner of the virtual screen and the magnification
  factor"
  ([Microsoft, MagSetFullscreenTransform](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-magsetfullscreentransform)).
- The participant therefore saw the part of the screen starting at the
  offset, of the screen's width and height divided by the level, drawn at
  the screen's size. The validation agrees: at level 2 and offset 3, 2 on a
  1920 by 1080 screen, the GDI screenshot showed the top-left quarter.
- Neither page describes a notice of change, so the transform is read at
  intervals.
- The validation read the transform from another process, which reported
  Windows Magnifier's level and offset. A third-party magnifier is not
  tested.

## Recording

A new record on the `system.assistive-technology` channel proposed in
[assistive technology detection](assistive-technology-detection.md):

- `fullscreen-magnification`: the level and the x and y offsets, at the
  start of a recording and whenever a reading differs from the last
  recorded one, with the time of the reading. A failed reading is written
  once, as null with the reason, until a reading succeeds.
- The transform is read at a fixed interval, proposed as 16 milliseconds,
  on its own thread. A pan between two desktop frames is recorded even
  though the screen, and so the capture, does not change.
- The reading uses the same DPI awareness as desktop capture, so that its
  offsets and the frame's pixels are in the same units.
- `MagGetFullscreenColorEffect` reads the full screen color effect
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-maggetfullscreencoloreffect)).
  Whether Windows Magnifier's inverted colors use it, and whether the
  capture shows them, is to be tested first; if they do and it does not,
  the effect is recorded in the same record and applied in the same way.

## Playback

- The player draws, at the playback position, the desktop frame at or
  before it, with the transform at or before it. The player draws again
  when either changes, not only when the frame changes.
- Participant's view, the default: the part of the frame given by the
  transform, scaled to the frame's size. At level 1, or where no transform
  is recorded, it is the whole frame.
- Whole screen: the frame as captured.
- The toggle is a labelled control beside the playback controls, reachable
  by keyboard, with its state announced: "Participant's view" or "Whole
  screen". When the recording holds no magnification above level 1, the
  toggle is disabled and says so.
- The frame's help text, which already names the frame and its time,
  gains the view shown and, in the participant's view, the level and
  offset in effect.
- The cursor is drawn by capture at its position on the unmagnified
  screen, so cropping and scaling the frame places and enlarges it as
  Magnifier did; whether its size then matches Magnifier's is to be
  checked.
- Recordings without these records play as now.

## Questions to settle

- The reading interval.
- Whether the whole screen view outlines the part the participant saw.
- How several monitors are handled: which monitor's frame the transform
  applies to, using the offset adjustment Microsoft describes. Only one
  monitor is tested.

## Required tests

- Unit tests of the payload contract and validator; of writing only
  changed readings; and of the player's choice of transform at a position
  and the part of the frame it gives, including offsets at the screen's
  edges and level 1.
- An integration test on the target machine: the recorder's readings
  during a scripted run of Magnifier match readings taken by the test
  script.
- A system test on the target machine, extending
  `scripts/Test-MagnifierCapture.ps1`: in a recording with Magnifier's
  full screen view at two levels and a pan, the participant's view at each
  phase matches the GDI screenshot of that phase within the comparison's
  tolerance, and the whole screen view matches the captured frame; the
  lens and docked phases are unchanged by the toggle.

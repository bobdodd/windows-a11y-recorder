# Playback of the Magnified View

## Status

Proposed 2026-10-07, agreed 2026-10-07, not built. Listed in
[outstanding work](analysis-outstanding-work.md).

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
- The validation read the transform from another process, which reported
  Windows Magnifier's level and offset. A third-party magnifier is not
  tested.

## Recording

Revised 2026-10-07: the transform is read with each desktop frame, not on
a timer of its own. Desktop frames are captured at a fixed rate, 5 a
second by default, whether or not the screen changes
(`src/Recorder.Collectors.Graphics/DesktopFrameCollector.cs`, lines 35 to
40 and 242), so every frame can hold the transform in effect when it was
taken. A pan between two frames is not seen, as nothing else on screen
between two frames is.

- `desktop-frame` gains `fullscreenMagnification`: the level and the x
  and y offsets, read with `MagGetFullscreenTransform` as the frame is
  captured, or null with the reason when the reading fails.
- The reading uses the same DPI awareness as desktop capture, so that its
  offsets and the frame's pixels are in the same units.
- `MagGetFullscreenColorEffect` reads the full screen color effect
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-maggetfullscreencoloreffect)).
  Whether Windows Magnifier's inverted colors use it, and whether the
  capture shows them, is to be tested first; if they do and it does not,
  the effect is added to the frame in the same way.

## Playback

- Participant's view, the default: the part of the frame given by its own
  transform, scaled to the frame's size (see "Several monitors" for the
  part). At level 1, or for a frame with no transform, it is the whole
  frame.
- Whole screen: the frame as captured, with the part the participant saw
  outlined when the level is above 1 (agreed 2026-10-07). The outline is
  drawn by the player over the frame and is not part of the evidence.
- The toggle is a labelled control beside the playback controls, reachable
  by keyboard, with its state announced: "Participant's view" or "Whole
  screen". When no frame of the recording holds a level above 1, the
  toggle is disabled and says so.
- The frame's help text, which already names the frame and its time,
  gains the view shown and, in the participant's view, the level and
  offset.
- The cursor is drawn by capture at its position on the unmagnified
  screen, so cropping and scaling the frame places and enlarges it as
  Magnifier did; whether its size then matches Magnifier's is to be
  checked.
- Recordings without the field play as now.

## Several monitors

Designed 2026-10-07; testing waits for a setup with more than one monitor.

- A desktop frame is one image of the virtual screen, the bounding
  rectangle of all monitors. Its record holds the virtual screen's
  upper-left corner and size (`x`, `y`, `width`, `height`) and each
  monitor's rectangle in the same coordinates (`monitorFrames`)
  (`src/Recorder.Collectors.Graphics/DesktopFrameCollector.cs`, lines 278
  to 281, 341 to 345, and 388 to 394).
- "The primary monitor contains the origin (0,0)", and parts of the
  virtual screen to its left or above it have negative coordinates
  ([Microsoft, the virtual screen](https://learn.microsoft.com/en-us/windows/win32/gdi/the-virtual-screen)).
  The transform's offset is relative to the primary monitor's upper-left
  corner, so it is in the frame record's coordinates as it stands.
- Model: a point P of the screen shows the unmagnified content at the
  offset plus P divided by the level. This is an inference from
  Microsoft's statement that a view starting left of the primary monitor
  needs offsets adjusted by "the upper-left corner of the virtual screen
  and the magnification factor"; it reduces to the one-monitor case
  validated on 2026-10-07. The participant's view is then the rectangle of
  the frame starting at the offset plus the virtual screen's upper-left
  corner divided by the level, of the frame's width and height divided by
  the level, scaled to the frame's size; the whole screen view outlines
  the same rectangle. Content outside the virtual screen, including the
  gaps of a layout that is not a rectangle, is drawn black.
- Whether Windows Magnifier's full screen view follows this model on
  several monitors, or magnifies each monitor apart, is not documented.
  The frame records hold the transform and every monitor's rectangle, so a
  different rule found by the test changes the player only, and recordings
  already made play correctly under it.
- Monitors at different display scales are not tested; the frame and the
  transform are both read in physical pixels.

## Required tests

- Unit tests of the payload contract and validator, and of the part of
  the frame the player gives for a transform, including offsets at the
  screen's edges and level 1.
- An integration test on the target machine: the frames' readings during
  a scripted run of Magnifier match readings taken by the test script.
- A system test with two monitors, one to the left of the primary, when
  a setup is available: the participant's view at each phase matches a
  GDI screenshot of the virtual screen, confirming or replacing the model
  above.
- A system test on the target machine, extending
  `scripts/Test-MagnifierCapture.ps1`: in a recording with Magnifier's
  full screen view at two levels and a pan, the participant's view at each
  phase matches the GDI screenshot of that phase within the comparison's
  tolerance, and the whole screen view matches the captured frame; the
  lens and docked phases are unchanged by the toggle.

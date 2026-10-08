# Playback of the Magnified View

## Status

Proposed 2026-10-07, agreed 2026-10-07. Built 2026-10-07; recording and the participant's view confirmed on
the target machine, except inverted colors; the player confirmed by the
owner after a fix to the playback index
([validation](../validation/magnified-playback-2026-10-07.md)). The
color effect, in "Color effect", proposed and agreed 2026-10-08; its
recording built 2026-10-08, its playback waiting for the target machine
test. Listed in
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
  offsets and the frame's pixels are in the same units: both are made by
  the recorder's process, in its frame loop.
- Built as `FullscreenMagnificationReader`
  (`src/Recorder.Collectors.Graphics/FullscreenMagnificationReader.cs`).
  It calls `MagInitialize` when a recording's frame loop starts and
  `MagUninitialize` when it ends, and only reads the transform; it never
  sets it. The object always holds `level`, `x`, `y`, and `problem`:
  either the three values and a null problem, or three nulls and the
  problem. The level is rounded to six decimal places, the digits a
  float holds.
- The frame loop runs on one dedicated thread, which calls
  `MagInitialize`, reads every frame's transform, and calls
  `MagUninitialize`. Microsoft does not state a thread rule, but on the
  target machine the API answered only on the thread that called
  `MagInitialize` and failed with error 21 on any other, including a
  thread that called `MagInitialize` itself; the first build read on pool
  threads and 790 of 837 frames held that problem
  ([validation](../validation/magnified-playback-2026-10-07.md)). The
  reader refuses a reading on another thread, as a problem.
- The database's evidence tables map the object to columns of
  `desktop_frames`, added by migration 0019, so the database writer
  accepts every record; recordings with a recording file do not write
  them.
- `MagGetFullscreenColorEffect` reads the full screen color effect
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-maggetfullscreencoloreffect)).
  On the target machine, Windows Magnifier's inverted colors set it to a
  matrix other than the identity, and the captured frames do not show
  them ([validation](../validation/magnified-playback-2026-10-07.md)).
  Recording the effect with each frame, and applying it in the
  participant's view, is proposed in "Color effect", not built.

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
  toggle is disabled and says so. Built as the "Whole screen" toggle
  button at the end of the transport row; on, it shows the whole screen.
  Each recording opens in the participant's view.
- Showing the level and position visibly is part of the properties panels
  for accessibility settings, logged in
  [accessibility preferences](accessibility-preferences.md), "Showing the
  settings in the player"; the owner decided against a line of text
  under the video (2026-10-07). Until then they are in the frame's help
  text only.
- The playback index of a recording file keeps `fullscreenMagnification`,
  `x`, and `y` of each desktop frame, from index version 4; an older
  stored index is derived again when the recording is opened.
- Built in `src/Recorder.Session/MagnifiedView.cs` (the part of the
  frame) and `src/Recorder.App/MainWindow.Magnification.cs` (drawing):
  the part seen is drawn from the frame as captured, scaled to the video
  area; the outline is a yellow line over a black one so it shows on
  light and dark content. A level within 0.000001 of 1 counts as none. A
  reading that failed plays the frame as captured.
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

## Color effect

Proposed and agreed 2026-10-08. Built 2026-10-08: the recording, the
validator, the database columns (migration 0020, with number arrays added
to the evidence catalog), the playback index (version 5), the frame's
effect in the playback archive, and the matrix applied to pixels
(`ColorEffect` in `src/Recorder.Session/MagnifiedView.cs`), with their unit
tests. Not built: applying it in the player and the help text, which wait
for the questions under "Open" to be settled by the test.

### What is known

- `MagGetFullscreenColorEffect` gives the full screen magnifier's color
  transformation matrix, or the identity matrix when no effect is set
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-maggetfullscreencoloreffect)).
  The matrix is `float transform[5][5]`, whose values are "for red, blue,
  green, alpha, and color translation"
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/magnification/ns-magnification-magcoloreffect)).
- Microsoft does not state how the matrix is applied. Its grayscale
  example
  ([Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/magnification/nf-magnification-magsetfullscreencoloreffect))
  has the rows 0.3, 0.3, 0.3; 0.6, 0.6, 0.6; and 0.1, 0.1, 0.1 in its first
  three columns. A grey of 0.3 red, 0.6 green, and 0.1 blue follows only
  if a pixel is the row vector (red, green, blue, alpha, 1) multiplied by
  the matrix, so that row i holds what input channel i adds to each
  output, and the fifth row is the translation. This design takes that
  reading; the target machine test confirms it with the matrix Windows
  Magnifier sets.
- In the second run
  ([validation](../validation/magnified-playback-2026-10-07.md)), the
  script read an effect other than the identity during the inverted
  colors phase, and the participant's view of frame 361 differed from the
  screenshot by 235.49. Inverting every channel of that view, 255 minus
  each value, gave 0.41, and 0.42 to 0.49 for each color channel
  compared separately. The script did not log the matrix's values.
- The reading uses the same `MagInitialize` as the transform, so it is
  made on the frame loop's thread, after the transform.

### Recording

- `desktop-frame` gains `fullscreenColorEffect`: `matrix`, the 25 values
  of `transform` in the order Windows holds them, row by row, each
  rounded to six decimal places; and `problem`. Either the matrix and a
  null problem, or a null matrix and the problem. It is recorded for every
  frame, the identity included, so that a frame without it is
  distinguishable from a frame with no effect; at about 200 bytes a frame
  and 5 frames a second, it adds about 3.6 MB an hour.
- The validator requires exactly 25 finite numbers or null, and the same
  consistency as `fullscreenMagnification`.
- The database's evidence tables map it to columns of `desktop_frames`, a
  double precision array and a text column, by a new migration.
- The playback index keeps `fullscreenColorEffect`, at index version 5,
  so an older stored index is derived again when a recording is opened.
  A test plays it through a recording file, as for the magnification.

### Playback

- The participant's view applies the frame's matrix to the part of the
  frame seen: each pixel's red, green, blue, and alpha, as 0 to 1, and 1,
  multiplied by the matrix as above, clamped to 0 to 1. The identity, a
  missing effect, and a failed reading leave the frame as captured.
- The whole screen view stays the frame as captured, with the outline:
  it is the evidence as the capture holds it.
- The "Whole screen" toggle is offered when any frame is magnified or has
  an effect other than the identity.
- The frame's help text adds "colors inverted" when the matrix is the
  one Windows Magnifier sets for inverted colors, confirmed by the test,
  and "a color effect" for any other matrix other than the identity. The
  properties panels logged with
  [accessibility preferences](accessibility-preferences.md) show it
  visibly.
- The matrix is applied in software, to the pixels of the part seen,
  when the frame is displayed. Its cost per frame is measured on the
  target machine; if it is too slow for playback, a GPU effect is
  designed then.

### Open, settled by the test before playback is built

- Whether the effect is set when Magnifier inverts colors in the lens and
  docked views, which the capture already shows as seen. If it is, the
  player cannot tell from the matrix alone whether to apply it, and the
  rule is designed from what the test finds.
- Whether the effect is set when inverted colors are on with the full
  screen view at 100 percent.

### Out of scope

Windows color filters and high contrast are system settings, not
Magnifier's, and belong to
[accessibility preferences](accessibility-preferences.md), including
whether the capture shows them.

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
- A system test on the target machine, `scripts/Test-MagnifiedPlayback.ps1`,
  a new script based on `scripts/Test-MagnifierCapture.ps1`, which is
  kept as validated: in a recording with Magnifier's full screen view at
  two levels and a pan, the participant's view at each phase matches the
  GDI screenshot of that phase within the comparison's tolerance, and the
  whole screen view matches the captured frame; the lens and docked
  phases are unchanged by the toggle, their frames holding level 1. The
  script also reads the full screen color effect during an inverted
  colors phase, and checks that stopping a recording with the full
  screen view on leaves Magnifier as it was. The comparison is the mean
  absolute difference of 160 by 90 grey thumbnails, as in the capture
  validation, where the toolbar and phase label alone gave 2.5; the
  tolerance is 3. Its analysis was checked in Windows PowerShell on the
  target machine against a synthetic recording with known screenshots
  before the owner's run: the participant's views scored 0.11 and 0.04,
  the frames as captured 13.82 and 25.6.

For the color effect:

- Unit tests of the payload and validator; of applying a matrix to
  pixels, with the identity, a full inversion, Microsoft's grayscale
  example, translation, and clamping; and of playing the effect from a
  recording file through its playback index.
- On the target machine, `scripts/Test-MagnifiedPlayback.ps1` extended
  (built 2026-10-08) to log the 25 values with each sample; to add inverted colors phases
  in the lens view, the docked view, and the full screen view at 100
  percent; and to compare the participant's view with the effect applied
  against the screenshot of each phase, within the same tolerance of 3.
  The frames' matrices must match the script's. The script draws each
  phase's participant's view both without and with the frame's effect,
  applied with a GDI+ color matrix, which takes the same row vector
  reading, and reports which matched; its analysis was checked in Windows
  PowerShell on the target machine against synthetic frames, where the
  effect applied gave 0.03 and 0 for inverted phases missing from the
  capture, and was not needed where the capture already showed it.
- The time to apply the matrix to a frame in the player, measured on the
  target machine.

Built tests (2026-10-07): `MagnifiedViewTests` (the part of the frame,
including level 1, the screen's edges, a fractional level, and a monitor
left of the primary; the payload validator), an archive builder test of
each frame's reading and corner, the evidence samples with a reading and a
failed one, and the migration tests.

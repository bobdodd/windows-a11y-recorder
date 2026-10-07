# Windows Magnifier and Desktop Capture, 2026-10-07

## Question

Whether the recorder's desktop frames show what a participant sees when
Windows Magnifier is on, in its full screen, lens, and docked views.

## Method

- Target machine: Windows 10 Pro 22H2, build 19045.6466, one 1920 by 1080
  monitor. Recorder package c9f2591; desktop capture has not changed
  since. The recorder captures each monitor with Windows Graphics Capture
  (`src/Recorder.Collectors.Graphics/WindowsGraphicsCaptureBackend.cs`,
  line 269).
- `scripts/Test-MagnifierCapture.ps1` ran during recording
  `20261007-223910-ec23227086fc45ccb222b2bad88e253b`. It showed a
  full-screen labelled grid with the phase name in a corner, held the
  cursor at the centre, and ran five phases of 8 seconds: no Magnifier;
  full screen (Magnifier started, then Ctrl+Alt+F); lens (Ctrl+Alt+L);
  docked (Ctrl+Alt+D); closed (Win+Esc).
- In each phase the script sampled `MagGetFullscreenTransform` from its
  own process every 250 ms, and took its own screenshot with
  `Graphics.CopyFromScreen`, which copies from the screen with GDI.
- Each image was compared with the no-Magnifier image of the same kind,
  as the mean absolute difference of 160 by 90 grey thumbnails on a scale
  of 0 to 255. On test images an identical grid scores 0 and one drawn at
  twice the size about 7.
- The owner reported seeing each of the three views in its phase.

## Results

| Phase | Recorded frames | Recorded frame difference | GDI screenshot difference | Full screen transform read |
|---|---|---|---|---|
| Full screen | 515 to 555 | 2.5 in every frame from 516 to 556 | 28.11 | level 2, offset 3, 2 |
| Lens | 557 to 597 | 4.68 | 4.68 | level 1 |
| Docked | 599 to 639 | 5.06 | 5.04 | level 1 |
| Closed | 651 to 692 | 0.16 | 0.16 | level 1 |

Differences are computed with the script's 160 by 90 thumbnails for the
full screen column of recorded frames, and with 480 by 270 thumbnails for
the middle frame of each phase in the other cells.

- Full screen: every recorded frame shows the unmagnified grid, with only
  Magnifier's toolbar added; its difference of 2.5 is the toolbar and the
  phase label. The GDI screenshot of the same phase shows the grid at 200
  percent, the top-left quarter of the screen, as the participant saw it.
- Lens and docked: the recorded frames and the GDI screenshots agree, and
  both show the magnified lens or docked pane over the unmagnified screen,
  as the participant saw it.
- The transform read from another process followed Magnifier: level 1
  before it started, 2 with offsets 480, 270 then 28, 16 then 3, 2 within
  a second of the full screen view starting, and 1 in the lens and docked
  views, apart from one reading of 1.038 at the change to the lens.

## Conclusions

- The recorder's desktop frames do not show Windows Magnifier's full
  screen view. They show the unmagnified screen, so a recording made with
  full screen magnification does not show what the participant saw.
- They do show the lens and docked views, which Magnifier draws in
  windows of its own.
- GDI capture of the screen shows the full screen view as seen.
- `MagGetFullscreenTransform` read by another process reports Magnifier's
  full screen level and offset, so the visible part of the screen can be
  recorded over time. This answers the open question in
  [assistive technology detection](../architecture/assistive-technology-detection.md)
  for Windows Magnifier; a third-party magnifier is not tested.
- The plan's statement that full-display capture "can include ...
  magnification" ([prototype plan](../prototype-plan.md), "Screen and window video")
  holds for the lens and docked views only.

## Limits

- One machine, one monitor, Windows Magnifier only, at 200 percent; the
  cursor was held still, so following the cursor was not exercised.
- Whether the recorded cursor position matches the magnified view was not
  examined.

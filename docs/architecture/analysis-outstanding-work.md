# Analysis and Inference: Outstanding Work

Recorded 2026-10-07, during the planning phase that follows release 1.0.0.
Four designs are agreed and none is built. The owner's
decision is to stay in planning for now, so each item below waits for a
decision to start building. Each names where it is designed; the accepted workflow
applies when it is built: build, test in the sandbox, package and transfer
to the target machine, run there, record the result, and push on
approval.

## Magnified view playback

Designed in [magnified view playback](magnified-view-playback.md), agreed
2026-10-07, after the
[Magnifier capture validation](../validation/magnifier-capture-2026-10-07.md)
showed that desktop frames hold Windows Magnifier's full screen view
unmagnified.

To build:

- Recorder: `desktop-frame` gains `fullscreenMagnification`, the level
  and offsets read with `MagGetFullscreenTransform` as each frame is
  captured, or null with the reason, read with the same DPI awareness as
  capture; its payload contract and validator.
- Player: the participant's view as the default, drawn from each frame's
  own transform; the whole screen view with the outline of the part the
  participant saw; the toggle, labelled, reachable by keyboard, with its
  state announced, disabled when no frame is magnified; the frame's help
  text naming the view and the transform.
- Several monitors: the model in "Several monitors", built now.

To test:

- Unit tests of the payload, and of the part of the frame for a
  transform, at the screen's edges and at level 1.
- On the target machine: `scripts/Test-MagnifierCapture.ps1` extended so
  that the participant's view matches the GDI screenshot of each phase,
  at two levels and a pan.
- Whether Windows Magnifier's inverted colors use the full screen color
  effect and are missing from the capture; if so, the effect is added to
  the frame.
- Whether the cursor's size in the participant's view matches
  Magnifier's.
- Deferred until a setup is available: two monitors, one to the left of
  the primary, to confirm or replace the model; monitors at different
  display scales.

## Screen reader activity

Designed in [screen reader activity](screen-reader-activity.md), agreed
2026-10-07. The owner accepted a low-level keyboard hook on
2026-10-07.

- To build: the keyboard hook collector, with reinstalling when a screen
  reader starts and when the hook is lost; the key dispositions; the
  screen reader window episodes; the command data for NVDA; the player's
  screen reader lane.
- To settle: the items in its "Questions to settle".
- To test: as in its "Required tests".

## Assistive technology detection

Designed in [assistive technology detection](assistive-technology-detection.md),
approach agreed 2026-10-07.

- To settle before building: the items in its "Questions to settle":
  whether to add a low-level hook for injected input, how often modules
  of watched processes are listed, whether Chromium's detection and
  accessibility mode are recorded over the bridge, and the known list's
  contents and home.
- First step proposed: a test script for the target machine that checks
  the untested signals, with the portable NVDA 2026.2 started and stopped:
  NVDA's module in the instrumented Chromium, its audio session, the
  UIAccess flag readable without elevation, and the null device handle of
  injected raw input.
- Keys, tested 2026-10-07 in
  [screen reader keys](../validation/screen-reader-keys-2026-10-07.md):
  the keys NVDA keeps as commands are missing from raw input and are seen
  only by a low-level keyboard hook installed after NVDA's; NVDA's
  injected keys have no device handle in raw input. Designed in
  [screen reader activity](screen-reader-activity.md).
- Magnification: Windows Magnifier's transform is readable from another
  process (tested 2026-10-07); a third-party magnifier is not tested.

## Accessibility preferences

Designed in [accessibility preferences](accessibility-preferences.md),
agreed 2026-10-07 with the decisions in its "Decisions".

- To build: the `system.preferences` collector, reading through
  `UISettings` where it provides a setting; the browser preference,
  zoom, and `web-preferences-sent` records at a protocol change, with
  the `RendererPreferences` hook point found in the checkout first; the
  `ProfileDirectory` option offered in the session settings.
- Page recreation: applying the recorded preferences that change how a
  page is drawn or laid out, designed with this work, as revised in
  [page recreation](page-recreation.md), "The environment".
- To test: as in its "Required tests", including the fixture page that
  responds to the preferences.

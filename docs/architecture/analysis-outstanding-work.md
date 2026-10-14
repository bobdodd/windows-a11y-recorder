# Analysis and Inference: Outstanding Work

Recorded 2026-10-07, during the planning phase that follows release 1.0.0.
Five designs are agreed; the player layout and magnified view playback
are built. Each item
not built waits for a decision to start building. Each names where it is designed; the accepted workflow
applies when it is built: build, test in the sandbox, package and transfer
to the target machine, run there, record the result, and push on
approval.

## Magnified view playback

Designed in [magnified view playback](magnified-view-playback.md), agreed
2026-10-07, after the
[Magnifier capture validation](../validation/magnifier-capture-2026-10-07.md)
showed that desktop frames hold Windows Magnifier's full screen view
unmagnified.

Built 2026-10-07. The first run on the target machine
([validation](../validation/magnified-playback-2026-10-07.md)) found the
readings failing on threads other than the initializing one, now fixed,
and inverted colors missing from the capture. The second run, with the
fix, held a reading in every frame, and the participant's view matched
the screen in every phase but inverted colors. The player showed no frame
magnified, because the playback index dropped the readings; after the fix
the owner confirmed the player.

- Recorder: `desktop-frame` gains `fullscreenMagnification`, read with
  `MagGetFullscreenTransform` as each frame is captured; its payload
  contract, validator, and database columns (migration 0019).
- Player: the participant's view as the default; the "Whole screen"
  toggle with the outline; the frame's help text naming the view and the
  transform; the model in "Several monitors".
- Unit tests of the payload and of the part of the frame for a
  transform.

To test, with `scripts/Test-MagnifiedPlayback.ps1` on the target machine:

- The participant's view matches the GDI screenshot of each phase, at two
  levels and a pan, and the frames' readings match the script's.
- Inverted colors use the full screen color effect and are missing from
  the capture; recording the effect with each frame and applying it in
  the participant's view is designed in its "Color effect", agreed and
  built 2026-10-08, and confirmed on the target machine, recording and
  playback.
- That stopping a recording with the full screen view on leaves
  Magnifier as it was.
- Whether the cursor's size in the participant's view matches
  Magnifier's.
- Deferred until a setup is available: two monitors, one to the left of
  the primary, to confirm or replace the model; monitors at different
  display scales.

## Player layout

Designed in [player layout](player-layout.md), agreed 2026-10-07.
Decided: the details panels are open by default, and the
frame-only view is part of the design.

- Built 2026-10-07: the collapsible details region and its splitter, the
  collapsible side panel, the frame-only view, the remembered layout, and
  the selected event summary in the status line.
- Run on the target machine 2026-10-07: the owner reported that it
  appears to work. Individual results from its "Required tests" were not
  reported.

## Screen reader activity

Designed in [screen reader activity](screen-reader-activity.md), agreed
2026-10-07. The owner accepted a low-level keyboard hook on
2026-10-07.

- Built 2026-10-10: tracking NVDA, its processes, its module in the
  browser, and its periods of sound, with the properties panel's Screen
  reader rows, as in its "Tracking NVDA". First run on the target machine
  2026-10-10 ([validation](../validation/nvda-tracking-2026-10-10.md));
  two defects fixed and checked by a further run the same day.
- Built 2026-10-10: step 2a, the recorder's keyboard hook, installed again
  every second while a screen reader runs and when the hook is lost, as in
  its "The keyboard hook". First run on the target machine the same day
  ([validation](../validation/keyboard-hook-2026-10-10.md)). Input to an
  elevated window, and on the secure desktop, reaches neither the hook nor
  raw input. Built 2026-10-10 and run on the target machine the same day:
  the records and the player's band and row for when input could not be
  recorded, as in its "Input the recorder cannot receive"; the secure
  desktop of a permission prompt is still to check. Recording elevated
  programs is ruled out by the owner.
- Built and tested on the target machine 2026-10-10: the key outcomes
  (2b), as in its "Key disposition"
  ([validation](../validation/keyboard-hook-2026-10-10.md), "The key
  outcomes in the player"). The windows of rule 1 stand until a test
  shows otherwise.
- To build, in the order agreed on 2026-10-10: NVDA's commands, the mode band, and the screen reader window episodes
  (2c); a low-level mouse hook, for NVDA's speaking of the content under
  the pointer; then the test of NVDA's requests to Chromium, and the
  reading position.
- Built 2026-10-12 and checked on the target machine the same day: step
  4a, the reading position (a Properties row, a timeline strip, and the
  basis in Selected event values), as in its "4a as built". Built
  and checked on the target machine 2026-10-12: 4b, the list of steps, as
  in its "4b as built"; the owner found it works functionally. Built and
  checked on the target machine on 2026-10-12: 4c, the tree view, as in its
  "4c, the tree view, as built". Built 2026-10-12 and checked on the target
  machine with NVDA's Speech Viewer on 2026-10-13: what NVDA had to
  announce at each step, as in its "What NVDA had to announce".
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

- Stage 1, the `system.preferences` collector and the properties panel,
  built 2026-10-08; to test on the target machine with
  `scripts/Test-AccessibilityPreferences.ps1` in the owner's account, and
  the panel by keyboard and screen reader. First run 2026-10-08: every
  change recorded except the color filter, whose key is created during
  the recording; the fix, watching the missing key's parent, built
  2026-10-08. The rerun recorded the color filter on and off; the
  contrast theme card remains untested. The panel on the target machine:
  values follow playback; the splitters showed no focus.
- Magnifier change records (`graphics.magnifier`, with a timeline lane
  and filter), stepping through a row's changes with Ctrl+Left and
  Ctrl+Right, a hint under the Properties list, and visible splitter
  focus: agreed and built 2026-10-08; to test on the target machine with
  `scripts/Test-MagnifierChanges.ps1` and by keyboard with no screen
  reader.
- The owner's check of 7ae9f19 on the target machine (2026-10-08): the
  stepping keys work, but the panel did not show how many changes a
  setting has, and stepping needed the keyboard. Previous and next change
  buttons and a count on each row with changes, for pointer, head pointer,
  and eye tracking use: agreed and built 2026-10-08; checked by the
  owner by mouse and keyboard on the target machine 2026-10-08 on
  45a500c; to check with a head pointer and eye tracking.
- Stage 2, the browser preference, zoom, and `web-preferences-sent`
  records at protocol 0.56, the `ProfileDirectory` option in the session
  settings, and the Browser groups of the properties panel: proposed and
  agreed 2026-10-08, built 2026-10-08 (see "As built" in its "Stage 2").
  Built in the owner's checkout and run on the target machine 2026-10-08
  (see "The first run on the target machine" in its "Stage 2"): every
  change recorded; the script's step checks corrected after and the
  recording analysed again, 13 of 16 steps passing, the others explained
  there. Page colors checked by a prepared profile
  (`scripts/Set-PageColors.ps1`) 2026-10-08, as the build has no setting
  that changes them while it runs. The player's Browser rows and
  change buttons checked by the owner the same day. The timeline's
  Browser settings lane and filter accepted by the owner as they are.
  Open: naming the stored
  `requestedPageColors` numbers, now known, in the player; the font families of
  scripts other than the common one; and showing the foreground tab in the
  "Sent to the page" group once the foreground tab is recorded.
- Stage 3, the recorded preferences applied to the recreation, with a
  `color-maps-sent` record at protocol 0.57: proposed 2026-10-08, agreed
  and built 2026-10-08 (see "As built" in "Stage 3: the recreation" in
  [accessibility preferences](accessibility-preferences.md)). To build in
  the owner's checkout and check on the target machine with
  `scripts/Test-RecreationPreferences.ps1`.
- The recreation's viewport after a window resize, with `viewport` and
  `devicePixelRatio` in `layout-changes-started` at protocol 0.58, and the
  fallback that draws a frame the screen cannot hold smaller: agreed and
  built 2026-10-10 ("The recorded layout zoom" in
  [accessibility preferences](accessibility-preferences.md)). To build in
  the owner's checkout and check on the target machine with a recording
  whose Chromium window is maximized after the page loads.
- The browser theme at protocol 0.59: its color, color style, grayscale,
  and installed theme recorded, shown in the Properties panel and the
  timeline, a color map record naming its changed colors, and the
  recreation browser's window given the recorded theme: agreed and built
  2026-10-10 ("The browser theme" in
  [accessibility preferences](accessibility-preferences.md)). To build in
  the owner's checkout and check on the target machine with a recording
  in which the theme is changed.
- Accessibility requests at protocol 0.60 (step 3 of the NVDA work): hit
  tests with the object given at once and the page's precise result, and
  actions as sent and received, in the event list and Selected event
  values: agreed and built 2026-10-12
  ([accessibility requests](accessibility-requests.md)). Built and run on
  the target machine 2026-10-12: the probe passed, and an NVDA session was
  recorded on the test page and cnib.ca. The fuller player view (rule 2:
  actions linked with keys, hit tests grouped into requests, the Under the
  pointer (Chromium) row) was agreed and built after it, and checked with
  a recording with arrow keys on the test page the same day. Rule 3 links
  actions with mouse clicks too. Open: arrow keys are not named as NVDA
  commands.
- Left open by the owner's decision 2026-10-08, to start stage 2 first:
  the Magnifier change records on a real Magnifier recording
  (`scripts/Test-MagnifierChanges.ps1`); applying a Windows color filter
  in the participant's view, and recording which filter is on; whether a
  contrast theme is in the desktop frames.
- Page recreation: applying the recorded preferences that change how a
  page is drawn or laid out, designed with this work, as revised in
  [page recreation](page-recreation.md), "The environment"; built as
  stage 3, 2026-10-08. A child frame in a renderer process of its own is
  still given the recreation browser's values.
- Player: properties panels for the participant's accessibility
  settings at the current frame, including Magnifier's current level and
  position and color effect; designed 2026-10-08 in its "The properties
  panel", agreed 2026-10-08, built 2026-10-08 with the Windows settings.
- Build order: three stages, Windows settings with the properties panel,
  then browser preferences, then the recreation, agreed 2026-10-08 in
  its "Build stages".
- To test: as in its "Required tests", including the fixture page that
  responds to the preferences.

## The whole screen's accessibility tree

Raised by the owner on 2026-10-12, for later work, not designed. The
recorder works outside Chromium too, so it should record the whole
screen's accessibility tree, not only Chromium's, and track the changes in
it. This would let a test follow a task through every program it uses:
for example, a website that sends a two-factor code or a notification by
email, with the participant reading the email in a mail program, so the
whole circle is recorded.

What is known so far:

- The objects the player names, such as link "Home" (node 28), are
  Chromium's own accessibility tree nodes, not UI Automation's or
  IAccessible2's.
- Chromium gives a page element's UI Automation runtime ID as the frame
  and the element's DOM node ID, or a generated unique number when it has
  no DOM node, and its IAccessible2 unique ID as that generated number,
  negated
  ([Chromium ax_platform_node_win.cc](https://raw.githubusercontent.com/chromium/chromium/main/ui/accessibility/platform/ax_platform_node_win.cc)).
  The accessibility checkpoints record each node's DOM node ID, so
  Chromium's nodes might be matched to UI Automation runtime IDs.
- To check: whether the recording holds what Chromium uses for the frame
  part of the runtime ID, and how often the UI Automation records
  (`accessibility.uia.events`) hold runtime IDs; in the one record looked
  at, the field was null.

## Analysis runner

Designed in [analysis runner](analysis-runner.md), proposed 2026-10-13 and
agreed by the owner the same day, to be tried as it stands. The
owner decided that analysis runs detached from the player with completed
tasks announced, that stages may use specialised models, and that the
model is a setting, since the PC is expected to be upgraded and a server
on the local network is expected within a few weeks. The network server's
security is deferred until the architecture is settled. A trial on the
target machine the same day ran Qwen3.6-35B-A3B at 4-bit in llama.cpp
b11607 at 25 to 29 tokens per second writing; the results are in the
design.

- Built 2026-10-13: step 1, the runner, with no player changes; see the
  design's "Implementation status".
- Built 2026-10-13: step 2, the recording summary's stage 1; see the
  summary design's "Implementation status".
- Built 2026-10-14: step 3, the player: the Analysis menu, the Analysis
  tab, the reading view, the notifications, the capability description,
  and the model profiles; see the design's "Implementation status".
- Steps 1 to 3 passed their system test on the target machine on
  2026-10-14: the six tests, with the mouse alone, and the same report
  from Run again. Five faults were found and fixed on the way; see
  [the validation](../validation/analysis-runner-2026-10-14.md).
- Built 2026-10-14: step 4a, the summary with vision from the moments'
  captured frames; see the summary design's "Stage 2 implementation
  status". Its first run on the target machine, on 2026-10-14, stopped at
  the ninth moment of the first visit, at the output limit; the moment
  prompt was changed to version 2 the same day (the summary design's
  decision 6). Its second run stopped at the tenth moment, the model
  repeating its statements; sampling options, a stop at the first repeated
  statement, and trials of one step were built the same day (the runner
  design's decision 4). It passed on the target machine on 2026-10-14
  with build f1b2229, the presence penalty set for the image profile
  only: all 12 steps were accepted at the first attempt, and moment 10
  gave the same 369 output tokens as its trial.
- Built 2026-10-14: text in the player can be selected and copied; see
  [player layout](player-layout.md), "Selecting and copying text". Not
  yet tested on the target machine.
- To do, in the agreed order: the system test of step 4a, then step 4b,
  the recreated pages.

## Recording summary

Designed in [recording summary](recording-summary.md), proposed and agreed
2026-10-13. Stage 1 built the same day, and passed its system test on
the target machine on 2026-10-14. Stage 2's step 4a, the moments and their
captured frames, built on 2026-10-14; step 4b, the recreated pages, not
built. The first task kind for the analysis runner, chosen by
the owner: a summary of each page visited and of the site tested, from the
recorder's collation and a model's text analysis, then enhanced with vision
analysis of recreated pages and captured frames at the moments of each
visit, each stage writing a Markdown file. The owner answered its four
questions the same day, and two more: fragment changes are judged from
the page's structure, to recognise skip links and back to top links, and
the participant's capabilities are an input, with the capability model
from the owner's PhD work to follow later.

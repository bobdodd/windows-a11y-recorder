# Player Layout

## Status

Proposed 2026-10-07, agreed 2026-10-07, not built. Listed in
[outstanding work](analysis-outstanding-work.md).

## The problem

The player's video is small. In `src/Recorder.App/MainWindow.xaml` the
player column (lines 306 to 313) stacks five rows and only the first, the
video, stretches. Under it are the transport and volume rows, the timeline
(96 pixels, line 446) with its zoom and pan bars, the "Browser navigation
correlation" and "Selected event values" panels (lines 505 to 588, about
250 pixels with their lists, text boxes, and headers), and the status
line. To the left, a 340 pixel side panel holds the capture settings,
playback filters, storage, marker, and session status (line 37).

On the target machine's 1920 by 1080 screen, an estimate from those sizes
puts the video at about 500 pixels high; it is limited by height, since
the column is about 1540 pixels wide. Without the two panels it would be
about 750 pixels high. These are estimates from the layout, to be measured
when built.

## Decisions (2026-10-07)

- The details panels are open by default: an auditor sees the events
  before playing the view.
- A frame-only view is part of this design, not deferred.

## Design

### The details region

- The "Browser navigation correlation" and "Selected event values"
  panels become one collapsible region, "Details", open by default.
- A toggle button labelled "Details" sits at the start of the transport
  row, so it stays visible in every layout. Ctrl+Shift+D toggles it.
- While it is open, a horizontal splitter between the video and the
  timeline region lets the auditor give the details more or less height;
  the panels' fixed maximum heights are removed so that the space goes to
  their lists and text.
- While it is collapsed, the status line shows a one-line summary of the
  selected timeline event (its time, lane, and type); selecting an event
  does not open the region.

### The side panel

- The side panel collapses to a narrow strip holding one button,
  "Settings", that opens it again. Ctrl+Shift+S toggles it.
- It is open when no recording is loaded and while recording, as now,
  since its controls are needed then. Opening a recording for playback
  leaves it as the auditor last set it.

### The frame-only view

- F11 shows only the video, the transport row, and the timeline with its
  zoom and pan bars. The side panel, the details region, the volume row,
  and the status line are hidden. F11 or Escape returns to the layout as
  it was.
- The window keeps its title bar and the taskbar stays visible; the view
  changes the layout inside the window only.
- The magnified view toggle and its outline, from
  [magnified view playback](magnified-view-playback.md), sit in the
  transport row and so remain available.

### Remembered layout

- Whether the details region and the side panel are open, and the
  details region's height, are kept per Windows user in
  `%LOCALAPPDATA%\Windows A11y Recorder\player-layout.json` and restored at
  start. A missing or unreadable file gives the defaults: both open. The
  frame-only view is not remembered; the player always starts in the
  normal layout.

### Accessibility

- Each toggle is a toggle button whose name and state are exposed to UI
  Automation: "Details, expanded" or "collapsed", "Settings, expanded" or
  "collapsed", and the frame-only view as "Frame only, on" or "off".
- When a region collapses while keyboard focus is inside it, focus moves
  to its toggle. Entering the frame-only view keeps focus where it was if
  that control is still shown, and otherwise moves it to the video's
  play button; leaving it restores the previous focus where possible.
- Each change of layout is announced through the existing polite status
  text, for example "Details hidden" or "Frame only view. Press F11 or
  Escape to return".
- The shortcuts do not clash with the player's existing keys: Space for
  play and pause, Ctrl with plus, minus, and 0 for the timeline zoom
  (`MainWindow.xaml.cs`, lines 538 to 580), the timeline's arrow, Home,
  and End keys, and the Alt access keys of the labelled controls.
- The splitter is reachable by keyboard and named, like the existing
  splitters.

## Required tests

- Unit tests of reading and writing the layout file, including a missing,
  empty, and malformed file, and of the toggles' state and names.
- UI tests on Windows of each toggle by mouse and by its shortcut; of
  focus moving out of a collapsing region; of the frame-only view entered
  and left with F11 and with Escape, during playback and paused; and of
  the layout restored at the next start.
- A check on the target machine with Narrator or NVDA that each toggle
  announces its name and state and each change of layout is announced.
- A measurement on the target machine of the video's size in each layout,
  recorded with the result.

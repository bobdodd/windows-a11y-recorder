# Page Recreation: Outstanding Work

Recorded 2026-10-07, after slice 5 (the documents of frames, steps 5a, 5b,
and 5c) passed its system tests on the target machine and was pushed to
`origin/recreation` at 0633726. Each item names where it is stated in
[page recreation](page-recreation.md) or in the code. Nothing here is
designed or agreed yet; each item needs a design, agreed before it is
built, as the other slices were.

## Frame inspection

### The precision check (background guard)

- Slice 5's scope, as decided 2026-09-29, names "the documents of
  `iframe`, `frame`, and `frameset` elements in the recreation, the
  precision check, and the evidence panel" ("Slices", item 5).
- The check is a background guard ("The check is a background guard",
  decided 2026-09-30, and "The background guard"): the recreation's own
  layout checkpoint is compared with the recording, the auditor sees only a
  notification when they differ, and the differences go to the recorder's
  diagnostics. It was to be built after slice 4.
- It is not built for any document. Every document's evidence, the top
  document's and each frame's, has fidelity "not-checked"
  (`RecordedPage.cs`, `Content` and `WriteFrames`).

### Frames that are not built

Settled 2026-10-06 ("Slice 5", "Settled") and listed in "The recreation
(step 5b)", "Not built". Each is named in the notes and the Frames table;
its document's recorded evidence is shown in the panel, but its content is
not drawn:

- A frame whose owner is sandboxed without `allow-scripts`, since its
  builder cannot run.
- A frame whose owner has a `csp` attribute, whose required policy the
  served document may not meet.
- `data:` and `blob:` frames, and any address that is not `http` or
  `https`.
- The documents of `object`, `embed`, and `fencedframe` owners.
- An `about:blank` frame whose owner is sandboxed without
  `allow-same-origin`, which its parent's builder cannot reach.
- The frames of a frame that is not built: they are not listed
  (`RecordedPage.cs`, `WriteFrames`, the `default` case).

### Scripts shared by two documents of one process

- Found while building 5c ("5c as built", "Found while building"). V8
  reports a top-level script at each instantiation, including one reused
  from its compilation cache (`v8/src/codegen/compiler.cc`, lines 4018 to
  4027 and 4646 to 4663), but the recorder records each script ID once per
  renderer process (`chromium/recorder_bridge/browser_bridge.cc`,
  `ClaimScriptParsed`, lines 8019 to 8025).
- So a script that a second document of the process runs has no record
  in that document: in the 5a recording, the second frame at
  `child.html?name=twin` has no script. The panel's scripts note says so.
- Recording a script once in each document changes the protocol.

### Selecting nodes in frames

- Of two frames of another origin with one address, only the first that
  DevTools finds can be selected from the panel, since an extension
  reaches such a frame by its address only ("Build plan for 5c"). The
  panel says so. This is tested in the sandbox only, as agreed
  2026-10-07; the fixture was not extended with such a pair.
- A frame owner in a closed shadow root cannot be reached by Select, as
  any node in a closed shadow root cannot (the panel's `findNode` reaches
  open shadow roots only).
- A frame that is not built has no Select on its paths.

### Limits and coverage

- The limits of a depth of 8 and 64 frames in one recreation are tested by
  unit tests only; the frames fixture does not reach them.
- An `iframe` whose document is an SVG or other XML document is not in the
  fixture, and how the writer and builder handle such a frame is not
  tested.
- Each frame's times compare documents through each document's
  `performance.timeOrigin`, which carries the wall clock read when the
  document's timing object is made
  (`third_party/blink/renderer/core/timing/performance.cc`, lines 129 to
  133 and 350 to 356). The panel says so; no other clock is available to
  page script.

## SVG

Found in the design doc on 2026-10-07. These are not specific to frames.

- **Namespaces are not recorded.** An element is built in the SVG or
  MathML namespace by inference from an `svg` or `math` ancestor ("Slice 3b
  design", "Building the document exactly"); the notes say so. The design says recording the namespace is
  added to slice 4, and it was not.
- **SVG layout takes no recorded geometry.** SVG is laid out by Blink from
  its recorded style, not from recorded box fragments or line items
  ("Slice 3 revision", "Limits").
- **SVG animation elements** (`animate`, `animateTransform`) are not run
  in the recreation ("Slice 4b", "What the recreation does") and are not
  listed in the panel ("Slice 4g", "Limits"). Slice 4b's required tests ("Slice 4b", "Required tests") planned an SVG `animateTransform` in
  the animation fixture, to check that the recorded style and fragments
  hold its effect; no result of that check is recorded.
- **Animated SVG images** are not covered by the image frame recording:
  an SVG image is not a `BitmapImage`, and its animation is not the
  controller's ("Slice 4b", "Sub-step 1c design").
- **SVG `url()` filters** are recorded with no numbers and are not
  imposed; the Console names the element ("Slice 4b", "Sub-step 2b-i
  design").
- **SVG documents in frames:** an `object` or `embed` showing an SVG is
  not built (above), and an `iframe` whose document is an SVG file is not
  tested.
- A copy made by an SVG `use` element does not take the inferred display
  of an element with no layout object; this is by design, since the
  copy's layout object was not the one recorded ("Slice 4a", "Elements
  without a layout object").

## Elsewhere, for reference

Recorded elsewhere and not repeated here: text by content hash (agreed,
deferred, "Text by content hash"); the remaining validation of animation,
script, and style sheet recreation and of long recordings, kept in the
project's notes.

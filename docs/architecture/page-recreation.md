# Recreating the Page at a Captured Frame

## Status

Slice 1 (character data in DOM checkpoints, protocol 0.33) is merged, and
the character data check passed in the recordings at protocols 0.35 and 0.36
on the target Windows machine; see "Slice 5 status" in
[change-driven recording](change-driven-recording.md). Slice 2 (rebuilding
the recorded state at any captured frame) is implemented on the
`frame-state` branch as designed in "Slice 2 design", with the differences
listed in "Slice 2 implementation". Its hour recording on the target
machine did not meet the design's limits, and the revision in "Revision
after the hour recording" is implemented and waits for a second hour
recording. Slices 3 to 5 are designed in "Slice 3 design", proposed
2026-09-29 and agreed the same day; it revises the decision below. Slice
3a is implemented on the `recreation` branch and not yet tested on the
target machine; see "Slice 3a implementation".
Recording text by content hash is agreed and deferred; see "Text by content
hash".

## Purpose

An auditor reviewing a recording needs to inspect the page as it was at a
chosen captured frame: its elements, attributes, text, and styles, with the
tools auditors already use. The recorder is an evidence logger, not an
analyzer. A recreation is a view of recorded evidence, and it must not be
presented as the page itself.

## Requirement: the page exactly as drawn at the frame

Stated by the owner on 2026-10-02:

> When I open a frame in the recording, I expect to see a pixel-perfect
> rendering of that frame's whole page. If there is animation, I will see
> the page at that point in the animation. If a select is open and the
> second item in the select is selected, that is exactly what I will see.
> It needs to be precise and specific because it impacts visual
> accessibility testing

This governs every slice below. A recreation shows the whole page, not only
the part in the viewport, drawn as it was drawn at the frame, with every
transient state the frame shows. Nothing in the recreation moves on from
the frame: animations, transitions, animated images, video, and carets
show the state they had when the frame was drawn, and stay there. A
difference from the frame is a defect of the recording or the recreation,
not an accepted limit.

What does not yet meet it (proposed 2026-10-02, to be agreed before work
on it starts). Each item says what the recording would need; none is yet
verified against Chromium's source.

- Animated images. Sub-step 3 answers an animated image with its bytes,
  and it then animates in the recreation from its first frame. The
  recording does not hold which frame of each animated image was drawn at
  each frame, and the recreation does not hold an image at a frame.
- CSS animations and transitions. The recorded computed style is that of
  the latest layout walk at or before the frame, which is not necessarily
  the style drawn at the frame while an animation runs. Animations that
  run on the compositor may not change the style the walk reads at all.
  The recreation must also not run the recorded page's own animations
  from the time it opens.
- An open select. The recording does not hold whether a select's list was
  open, which item was highlighted, or where the list was drawn. See
  "Slice 4d" below: on Windows the list is a page popup whose document is
  recorded, but not joined to its select, placed, or presented.
- Other transient states: hover (the pointer's position), active and focus
  rings, the text caret and its blink phase, selection highlight, and
  scrollbar state. Focus, selection, and scroll offsets are recorded and
  applied; whether they are drawn as at the frame is not checked.
- Style sheets, which are not recorded. Pseudo-elements take no recorded
  style, and rules that depend on state (such as `:hover`) are lost.
- Iframes, canvas, video, and other media, which are not recorded or not
  built.
- Drawing differences between the recording machine and the playback
  machine, such as font smoothing settings and the graphics device.
- A page drawn before its first DOM walk. The DOM is recorded from the
  finished-parsing walk, but the page is drawn while it parses, so a frame
  before that walk cannot be recreated. Slice 4c proposes recording the
  DOM from the start of parsing.
- Checking. The recorded screen frame is the reference for the viewport;
  nothing compares the recreation with it yet.

## Decision

This decision is revised by "Slice 3 design": the rebuilt page opens in the
instrumented Chromium rather than Chrome, with the recorded style sheets and
resources, and is checked node by node against the recording. The text
below is kept as the record of the first decision.

The player writes the page rebuilt for the chosen frame as an HTML document
and opens it in Chrome, in a profile of its own, so that every Chrome
DevTools panel can be used on it.

The alternative considered was a DevTools back end of the recorder's own.
The DevTools front end talks to its target over the Chrome DevTools Protocol
([Chrome DevTools Protocol](https://chromedevtools.github.io/devtools-protocol/)),
and a protocol domain is implemented by a back end that the front end does
not depend on
([DevTools protocol, devtools-frontend](https://chromium.googlesource.com/devtools/devtools-frontend/+/main/docs/devtools-protocol.md)).
Such a back end could answer `DOM`, `CSS`, and `Accessibility` requests from
the recorded evidence, so every value DevTools showed would be a recorded
one. It was not chosen because nothing would be drawn, and each panel the
auditor uses would need its part of the protocol written for it.

The consequence of the decision is stated wherever the recreation is shown:
Chrome computes styles, layout, and the accessibility tree again from the
rebuilt document. Where the result differs from the recording, DevTools shows
Chrome's result, not the evidence. The player keeps the recorded values for
comparison.

## What a recreation is built from

For each document shown at the frame:

- the DOM: nodes, attributes, character data (from protocol 0.33), shadow
  roots, and slot assignments, from the latest DOM checkpoint at or before
  the frame and the attribute and character data transitions after it;
- the computed style of each element and pseudo-element, the 283 recorded
  properties, from the layout checkpoints and change records;
- form control values and selection, from the interaction checkpoints; and
- the scroll offset of the view, from the layout checkpoint.

The state is matched to a frame through the presentation records where a
checkpoint was presented, and by time otherwise, and the basis is stated.

## What a recreation lacks

- Images, fonts, video, canvas, and other resources. The network records hold
  metadata, not bodies. The rebuilt document blocks all network access, so
  that the current version of a resource is never shown as if it were
  evidence.
- Style sheets as written. Each element carries its recorded computed style
  inline, so rules, cascade, and selectors are not visible, and properties
  outside the 283 recorded take their initial or inherited values. Text
  inside `style` elements is recorded from protocol 0.33 and is kept in the
  document, but it is not applied, since the recorded computed styles
  already reflect it.
- Script behavior. Scripts are not run, and event handler attributes are kept
  as attributes but disabled.
- The document type's public and system identifiers, which are not recorded,
  so the rendering mode the page had is not known.
- The documents of iframes, which the first recreation leaves out.

## Slices

Each slice is tested on the target Windows machine before the next.

1. Browser: record the data of every text, comment, CDATA section, and
   processing instruction node in DOM checkpoints (protocol 0.33). Unit tests
   of the payload contract and validation, and of a check that every such
   node has one data record after its node record and that each completion
   states their number; integration tests of the Chromium patch, including
   the upgrade of a checkout patched for protocol 0.32; system test: a
   recording on the target machine passes the check, and the data of later
   checkpoints equals the data rebuilt from earlier checkpoints and the
   character data transitions, except where a parser appended text.
2. App: rebuild the recorded state of each document at any captured frame.
   Unit tests of the rebuild from generated sequences; integration tests
   comparing the rebuilt state with every full checkpoint of recorded files;
   system test: time to rebuild at the middle and end of a long recording.
3. Revised, with slices 4 and 5, in "Slice 3 design". As first proposed:
   App: write the rebuilt
   page for the current frame and open it in Chrome,
   with the notice of what the recreation lacks. Unit tests of the document
   writer, including escaping of recorded text and attribute values, and of
   disabled scripts and blocked network access; system test on the target
   machine: open a frame of a recording and inspect it in DevTools.

## Slice 2 design: the recorded state at any captured frame

Proposed and agreed 2026-09-29. The record names, counts, and sizes
below are from recording 20260929-172645-baa5818d82194001acf0edaa560b335c
(protocol 0.36, 92.6 s) unless stated.

### What slice 2 adds, and what it does not change

The app gains a reader that returns, for a time in a recording, the recorded
state of each browser document at that time, and for a captured desktop
frame, the state that frame shows, with the basis of the match. Nothing
recorded changes, no browser change is needed, and the protocol stays at
0.36. The player shows nothing new in this slice; slice 3 writes the state
as a page. The recording file gains derived records, the snapshots, which
the reader never needs for a correct answer, only for a fast one.

### One reading of state

The DOM, character data, and layout checks each rebuild a document's state
from its checkpoints and change records, in three separate classes
(`DomChangeCheck`, `DomCharacterDataCheck`, and `LayoutChangeState`). Slice 2
moves the rebuild into one class, `BrowserDocumentState`, which applies
records in order and exposes the state, and the checks compare that state
with each full walk. The rebuild the reader returns is then the one the
checks have compared with every full walk of every recording checked, and a
fix to either is a fix to both. The checks' reports on the recordings at
f409513, 70d22d4, and 66030a7 must be unchanged by the move.

### What the state holds

For each document, identified by its renderer process, document token, and
document identity, as the checks now identify it:

- The DOM tree: each node's parent, children in order, type, name,
  attributes, character data, shadow root fields, and slot assignment.
- For each node with a layout record: its computed style values, whether it
  has a layout object, whether it is display locked, its geometry, and the
  viewport rectangle derived from the geometry and the transform nodes, in
  CSS pixels.
- The transform nodes, the view's transform node, paint offset, and layout
  zoom factor, and each scroll offset.
- The interaction state: focus, selection, and each text control's value
  and selection, from the latest interaction checkpoint and the interaction
  changes after it.
- For each part (DOM, layout, interaction), the event key and time of the
  record that last changed it, so that a value shown later can be traced to
  its record.
- Completeness, for each part, as one of:
  - complete: taken from a full walk and changed by every record since;
  - parsing: the document has not finished parsing, and the parser's
    insertions are not recorded, so nodes the parser added since the last
    walk are missing;
  - after loss: a record of the channel was lost after the last walk, and
    the state lacks that change until the next walk;
  - not walked: no full walk of the document was recorded yet, so the part
    has no state.

A document's last record time is kept. No record states that a document was
discarded, so the reader returns every document with a record at or before
the time, with its last record time, and does not guess which are still
shown. Choosing the document a frame shows is part of the frame match below.

### Matching a captured frame

A desktop frame has the time the Windows compositor composed it. For each
document, the frame shows the last rendering update of that document whose
`presentation-feedback` reports a presentation at or before that
composition. The state returned is the state after that update's change set
or checkpoint, and the basis is `presented`. A document with no presented
update at or before the frame is matched by time, the state at the
composition time, with the basis `by time`. The DOM and interaction records
after the presented update and before the composition are applied to the
DOM and interaction parts only when the basis is `by time`, since a
presented frame was drawn before them; the reader states which.

The presentation records join to the change set or checkpoint of their
update through `layoutChangeSetId` or `layoutCheckpointId` (protocol 0.35),
as the playback index already joins them for presented checkpoints.

### Snapshots

The state at a time can always be rebuilt from a document's first full walk
and every record after it, but the time taken grows with the recording. In
the recording above, the DOM, layout, and interaction channels hold 149,182
records, 366 MB of JSON, in 92.6 s, about 4 MB per second; reading them
with the MCAP project's Python reader took 1.4 s on the development
machine, not the target machine. An hour at that rate is about 14 GB.

The app therefore writes snapshots as it records:

1. After the writer accepts a batch, it passes the batch's DOM, layout,
   interaction, and presentation records to a state thread through a
   bounded queue. The thread waits on the queue and does no work while it
   is empty. It applies each record to the state of its document.
2. When 10 s of recording time have passed since a document's last
   snapshot and the document has changed since, the state thread writes a
   snapshot of that document: its whole state, and the event key of the last
   record applied, and passes it to the writer as a record of its own. A
   snapshot is also written for each changed document when recording stops.
3. Snapshots are messages on the topic `recorder.state`, in a stream of
   their own, `state`, so that reading the browser records never
   decompresses a snapshot. They are marked as derived records. The playback
   index lists each snapshot's document, time, event key, and position.
4. If the queue is full, the state thread stops applying records and writes
   no snapshot for the rest of the recording, and a `recorder.state` record
   states when and why. No evidence is lost: the file still holds every
   record, and the reader rebuilds without the snapshots after that time.
   The writer never waits for the state thread.

To rebuild a document at time t, the reader takes the document's last
snapshot at or before t, or its first full walk if it has none, and applies
the document's records from the snapshot's event key to t. With a snapshot
every 10 s, that is at most about 10 s of records, about 40 MB of DOM,
layout, and interaction JSON at the rate above.

The browser records are one stream, and the DOM, layout, and interaction
records are about 40% of its bytes in the recording above; dispatch records
alone are 456 MB of the 916 MB. The reader decompresses every browser chunk
in the range it reads. Slice 2 therefore also writes the DOM, layout,
interaction, and presentation channels in a stream of their own,
`browser-state`, so a rebuild decompresses only those. Recordings made
before this slice are read as before, from the `browser` stream.

A snapshot holds the rebuilt state, so it is checked in the same way as the
rebuild: the tests rebuild each document at each snapshot's time without
snapshots and compare.

### The alternative considered

Snapshots could be made after recording stops, or when a recording is
first opened, by reading the file once. That adds no work while recording,
but makes stopping or first opening take time proportional to the
recording, which the app must avoid: at least the time to read the DOM,
layout, and interaction records, 1.4 s for the 92.6 s above with the Python
reader on the development machine. It remains the way a recording without
snapshots, or one whose state thread stopped, is given them later, as a
background task the player does not wait for.

### To be settled

- Memory. The state thread holds the whole state of every document of the
  recording that has not been discarded, and nothing records a discard, so
  it holds every document until its renderer exits. Its memory is to be
  measured on the target machine, and if it grows without bound, documents
  with no record for a set time are dropped from the thread, with a final
  snapshot, and rebuilt by the reader from that snapshot if they change
  again.
- The snapshot interval, 10 s, to be set from the measured rebuild time.
- Whether the time taken to rebuild at a frame has a required limit. The
  proposed limit is 1 s on the target machine for any frame of a one-hour
  recording.

### Required tests

- Unit: the document state rebuilt from generated sequences of checkpoints
  and change records, compared with the state those sequences describe,
  including a lost record, a document that is parsing, and a document with
  no walk; each completeness state; the frame match for a presented update,
  an update presented after the frame, and a document with no presentation;
  the snapshot writer and reader; the state thread's behaviour when its
  queue is full, and that the writer never waits for it.
- Integration: every check's report on each recording file checked in
  slice 5 is unchanged by the move to one state class; the state rebuilt
  with snapshots equals the state rebuilt without them, at every snapshot
  and at times between; a recording written with the `browser-state`
  stream plays back as it did with one browser stream.
- System, on the target Windows machine: a recording of at least an hour
  with snapshots, reporting the state thread's memory and time, the size of
  the snapshots, the time to stop, and the time to rebuild at frames at the
  start, middle, and end; and that hover and scrolling are as smooth as in
  the recording at 66030a7.

## Slice 2 implementation

Implemented 2026-09-29 on the `frame-state` branch. The measurements below
were made on the development machine with a debug build, not on the target
machine, and say nothing about the target machine's times.

### Classes

- `DomTreeRebuilder` (in `Recorder.Session`) holds the DOM rebuild that
  `DomChangeCheck` did: checkpoints, insertion sets, and changes, applied in
  record order. `DomChangeCheck` now uses it, and compares its tree with
  every full walk as before.
- `LayoutDocumentChangeState` gained a public `Apply` for layout records,
  the layout zoom factor, the transform records as recorded, and the scroll
  offsets.
- `BrowserDocumentState` holds one document's DOM, layout, and interaction
  state, the completeness of each part, and the event key and time of the
  record that last changed each part. `BrowserStateBuilder` applies DOM,
  layout, interaction, and presentation records to the documents they
  name.
- `BrowserStateSnapshot` writes and reads a document's state as JSON,
  format version 1. Nodes are in identifier order and recorded payloads are
  kept as recorded.
- `RecordingFileStateRecorder` is the state thread. `RecordingFileBrowserState`
  is the reader, with `At` for a time and `AtFrame` for a captured frame.

### Differences from the design

- `DomCharacterDataCheck` keeps its own per-node data map. Its check is of
  record order, that each text node's data record follows its node record,
  which the rebuilt tree does not keep. The character data the state holds
  is the `DomTreeRebuilder`'s, which `DomChangeCheck` compares with every
  full walk. The reports of the DOM, layout, and character data checks on
  the recordings at f409513, 70d22d4, and 66030a7 are unchanged by the
  move.
- Completeness has a fifth value, walk cut: a full walk of the part was
  started and its completion was not recorded, so its nodes are those
  recorded before the cut.
- The playback index does not list the snapshots. The playback index is
  written when recording stops, so a file cut short has none. The state
  thread instead writes a state index record on the topic
  `recorder.state-index`, in a stream of its own, `state-index`, at each
  sweep in which it wrote a snapshot or a document changed. Each lists, for
  every document (since the revision below, for every document whose entry
  changed), its last record time, its latest snapshot's event key,
  time, and position, and the time of its first record not in that
  snapshot. The reader reads only these records when it opens a file, so a
  file cut short is read from the snapshots before the cut.
- The state thread checks for due snapshots once a second of recording
  time. A document part way through a checkpoint, insertion set, or change
  set is not snapshotted until it completes.
- The queue to the state thread is not bounded by count: it holds up to
  256 MB of estimated record bytes. Passing a batch never waits. When the
  limit is passed, the thread stops as designed, and a record on
  `recorder.state` states when and why.
- The writer's own omissions, records it could not write, are reported on
  the reader's result for the whole file, with their times, not for each
  document, since an omission does not name a document.
- The presentation records are also in the `browser-state` stream, since
  the state thread reads them.
- The frame match reads the presented updates from the playback index's
  presented checkpoints, by document token, which is the join the design
  names.
- A document whose last presented update is long before the frame, such as
  a document not drawn since, is read from the snapshot before that update,
  so the time to rebuild grows with the number of such documents. The
  reader holds the state of such a document at its cut between calls, up
  to 512 MB, so each is rebuilt once.
- There is no player change in this slice, as designed.

### Tests

- Unit (`BrowserStateTests`): generated sessions of DOM, layout, and
  interaction records over several documents, rebuilt with and without
  snapshots and compared at many times; each completeness value, including
  a lost record, a parsing document, a document with no walk, and a cut
  walk; the frame match for a presented update, an update presented after
  the frame, and a document with no presentation; snapshot writing and
  reading; the state thread stopping when its queue limit is passed while
  the writer does not wait; and a file cut short, read from its snapshots
  before the cut.
- Integration (`ChecksTheStateOfARecordingFile`, run when
  `RECORDER_STATE_FILE` names a recording file): writes the file again with
  the state thread, then compares the state with and without snapshots at
  times and frames spread over the recording, and in a copy cut short at
  half. It reports the state thread's summary, the size of each stream, and
  the time of each rebuild. The DOM, layout, and character data check
  reports on the recordings at f409513, 70d22d4, and 66030a7 are compared
  with those made before the move.
- System, on the target Windows machine: a first, short recording has been
  checked; see "First recording on the target machine". The recording of
  at least an hour is not yet made.

### Measurements on the development machine

Recording 20260929-172645-baa5818d82194001acf0edaa560b335c (protocol 0.36,
92.6 s), written again with the state thread:

- The state thread applied 153,363 records in 0.55 s and wrote 139
  snapshots in 0.99 s. The snapshots are 93 MB of JSON, 3.0 MB compressed;
  the file grew from 28.5 MB to 30.4 MB.
- At 8 times and 8 frames spread over the recording, and in the copy cut
  short at half, the state rebuilt with snapshots equals the state rebuilt
  from every record.
- At the end, with 125 documents, a rebuild from snapshots took 0.23 s and
  one from every record took 2.1 s. At the other sampled times and frames,
  a rebuild from snapshots took 0.16 s to 0.69 s, excluding the first,
  which includes the time to load the code.

These are not the target machine's times, and do not show that a one-hour
recording meets the proposed 1 s limit; the system test on the target
machine does.

### First recording on the target machine

Recording 20260929-201652-b1261415cb204312943567e88e1305b2 (70.5 s,
17.1 MB), made at 37f65ec with the state thread, and checked with a
Release build:

- The state thread did not stop. The snapshots are 1.56 MB compressed and
  54 MB uncompressed, about 9% of the file; the state index records are
  65 KB compressed.
- At 8 times, 8 frames, and in a copy cut short at half, the state rebuilt
  with snapshots equals the state rebuilt from every record.
- A rebuild from snapshots took 50 ms to 394 ms at the sampled times and
  87 ms to 339 ms at the sampled frames. A rebuild from every record took
  up to 904 ms. The one rebuild over 1 s, 1,060 ms at 11.8 s, was before
  the first snapshot and was the first call, which includes loading the
  code.
- Hover and scrolling were reported to be as responsive as in the
  recording at 66030a7, and stopping to take no noticeable time.
- The report at 37f65ec did not give the state thread's time or its
  largest queue for a recording made with the state thread, only for a
  file written again by the check. From the commit after e118748 on, it also reports the
  state thread's summary and stop record as recorded in the file, and the
  time taken to stop the state thread from the writer timings beside it.
  The writer timings of this recording give 24.5 ms to stop the state
  thread.

A recording of 70.5 s says nothing about the state thread's memory or the
rebuild time in a recording of an hour.

### Hour recording on the target machine

Recording 20260929-204938-6b2c1ec82ed94783950f60a7939184a7 (60.6 min,
938 MB), made at 8e2c539 and checked at 693ea98 with a Release build. The
check at 8e2c539 failed with an out-of-memory error in the check's own
comparison, which joined the whole state into one string; at 693ea98 it
compares document by document.

Correct:

- At 9 times, 8 frames, and in a copy cut short at half, the state rebuilt
  with snapshots equals the state rebuilt from every record.
- The state thread never stopped. It applied 4,646,791 records in 7.5 s
  and wrote 7,666 snapshots in 26.4 s of processor time; its largest queue
  was 30 MB, and stopping it took 131 ms.

Not acceptable:

- The recording has 6,719 documents, and the state thread holds every one
  of them to the end. After stopping, the app, still open, used 20.1 GB of
  memory, with 38.2 GB committed and a peak working set of 25.7 GB. The
  batch target keeps the state thread's documents after `Complete`, which
  may explain why the memory was still held after stopping; this is not
  measured. Garbage collection paused the app for 45 s in total. Closing
  the app did not return control to the shell, and Ctrl+C did not end it;
  the cause is not known.
- Each state index record lists every document seen so far, so the index
  records grow with documents times sweeps: 830 MB uncompressed, 120 MB
  compressed, and opening the file takes 4.0 s to read them.
- Snapshots are 6.3 GB uncompressed and 114 MB compressed.
- A rebuild at a time loads a snapshot of every document with a record
  before it: 3.6 s at the end, with 6,719 documents, against 48 s from
  every record.
- A rebuild at a frame took 2.3 s to 25 s. Documents whose last presented
  update is long before the frame are each read from the records before
  their cut, and these are spread over the whole hour, so the reader reads
  from 5.6 s into the recording.

The proposed limit of 1 s at any frame of a one-hour recording is not met.

### Revision after the hour recording

Proposed and agreed 2026-09-29, after the hour recording above:

1. A document with no record for 30 s of recording time is given a final
   snapshot, if it changed since its last, and leaves the state thread,
   which keeps only that snapshot, compressed. A record of the document
   after that reloads it from the snapshot. A lost-record notice for its
   process reloads it too, so that it is marked. When recording stops, the
   state thread releases every document.
2. A state index record lists only the documents whose entry changed since
   the previous index record: a new document, a new last record time, a new
   snapshot, or a new first record after the snapshot. The reader builds
   each document's history of entries when it opens the file. A file whose
   index records list every document is read the same way.
3. The reader returns, for a time or a frame, every document's key and
   basis without reading its state, and reads the state only of the
   documents asked for. Choosing the documents a frame shows is left to
   slice 3.
4. Required tests: unit tests of a document leaving and returning to the
   state thread, including a lost-record notice while it is out, and of
   index records that list only changes; the comparison of the state with
   and without snapshots, for the documents asked for; system test on the
   target machine, a recording of about an hour, with the app's memory
   bounded through the hour and after stopping, the file opened in under
   1 s, and the documents of a frame read in under 1 s. The system test
   waits until further work is done.

Implemented 2026-09-29 on the `frame-state` branch:

- `RecordingFileStateRecorder` sends a document away after
  `IdleInterval`, 30 s of recording time without a record, keeping its
  snapshot compressed with Zstandard, and reads it back before applying a
  record of it or a lost-record notice for its process. Its summary adds
  the departures, returns, the most documents held whole at once, and the
  most compressed bytes of departed documents held at once. `Complete`
  releases every document.
- Index records carry `"listing": "changed"`.
- `RecordingFileBrowserState.At` and `AtFrame` take the set of document
  keys to read, or null for every document. A document not asked for is
  returned with its key and basis and a null state. A document asked for
  is returned when it has a state at its cut.
- Unit tests: a document leaving and returning, including for a
  lost-record notice while it is away, with the state equal with and
  without snapshots at every 5 s; index records that do not list a
  document while it is away; documents named without their state, and one
  document read alone, equal to the same document read with every other.
  The state check reports the time to name the documents at each sample,
  and at each frame the time to read the documents presented in the
  second before the composition, which stand in for the documents the
  frame shows until slice 3 chooses them.

On the development machine, with a debug build, recording
20260929-172645-baa5818d82194001acf0edaa560b335c written again: 86
departures and 2 returns; at most 64 of the 125 documents held whole at
once; at most 1.2 MB of compressed departed documents; index records
90 KB against 883 KB before the revision; every sample equal with and
without snapshots. These are not the target machine's figures, and a
92.6 s recording says nothing about an hour.

## Slice 3 design: inspecting the whole page at a recorded instant

Proposed and agreed 2026-09-29. This section revises the first
decision and the third slice as first proposed, and divides the work into
slices 3 to 5.

### Why the first decision is revised

An auditor who finds an issue at a captured frame needs to explore the
whole page as it was at that instant to understand the issue: its layout,
its interactive elements, and its timers, including the parts of the page
that were not on screen. The captured frame holds only what was on screen,
and nothing recorded holds pixels of the rest of the page, so the whole page
has to be rendered again.

Two things follow. First, a rendering is exact only where it is checked,
so the recreation is compared node by node with the recording, and every
difference is shown. Second, a recreation built only from the DOM and the
recorded computed style cannot match, since layout also depends on style
sheets, fonts, images, the environment, and element states that are not
recorded now; recording them is slice 4.

An inspector serving recorded values to the DevTools front end over the
captured frame, proposed earlier the same day, was not chosen: the captured
frame does not hold the whole page.

Browser extensions and bookmarklets are not a requirement (decided
2026-09-29). Automated test tools read the recorded state through the
reader of slice 2, and later through an interface of their own, which is
not part of these slices.

### Requirements

1. The auditor chooses a captured frame in the player and opens the
   recreation of the page at that frame.
2. The recreation is a page in the instrumented Chromium, in a profile of
   its own, with DevTools open, so every DevTools panel can be used on the
   whole page.
3. The recreation is written from the state slice 2 returns at the frame,
   with the recorded style sheets and resources (from slice 4), at the
   recorded viewport size, device pixel ratio, zoom, and media features, in
   the recorded compatibility mode (from slice 4).
4. The page's own scripts do not run, and event handler attributes are kept
   in the DOM but do not run. Network access is blocked, so that the current
   version of a resource is never shown as if it were evidence.
5. Focus and hover are set to their recorded state through
   `CSS.forcePseudoState`, which enforces a pseudo state on an element
   ([CSS domain](https://chromedevtools.github.io/devtools-protocol/tot/CSS/)),
   and the environment through `Emulation.setDeviceMetricsOverride` and
   `Emulation.setEmulatedMedia`
   ([Emulation domain](https://chromedevtools.github.io/devtools-protocol/tot/Emulation/)).
6. The recreation is written in its final state; the recorded changes are
   not replayed into it, since a replay would start CSS transitions and
   animations the page had finished. The page is shown only after it and
   its resources have loaded.
7. Precision is checked, not assumed (revised 2026-09-30: the check is a
   background guard; see "The check is a background guard"). The instrumented Chromium takes a
   layout checkpoint of the recreation with the code that recorded the
   page, and every node is compared with the recording: its bounding
   rectangle exactly, and each of the 283 recorded computed-style
   properties exactly. Layout checkpoints hold every element and every text
   node with a layout object, on screen or not, and hold the bounding box
   only, not line boxes or fragments; see
   [instrumented Chromium](instrumented-chromium.md). The check is therefore
   exact at the level of recorded boxes and styles, and states that limit.
8. A difference is listed, never hidden by forcing the recorded value.
9. A panel of our own in DevTools, the evidence panel, shows at the frame:
   the pending script timers with
   their type, delay, time scheduled, and time remaining; the running CSS
   animations and transitions with their name or property, start, duration,
   and progress; the interactive elements with their path (see "Paths
   through shadow roots"), registered listeners and event types,
   focusability, and recorded role; and form control values, focus, and
   selection. Selecting a row selects the node in the Elements panel. The
   panel reads records; it does not judge whether an element is accessible.
10. The recreation, DevTools, and the evidence panel can each be moved to
    any display and maximized there (agreed 2026-09-29).
11. Every view states that it is a recreation, the frame, the recording
    time, and the basis of the state (presented or by time).
12. What the auditor does in the recreation changes the recreation, not the
    evidence; reloading returns it to the recorded instant.
13. The recreation is served only on the loopback interface, with a random
    port and a token of its own in its address, and closes with the player.

### Slices

Each slice is tested on the target Windows machine before the next.

3. App: the user interface, first with fixed content. Step 3a: the player
   opens a fixed page, built in code, in the instrumented Chromium with
   DevTools and the evidence panel showing fixed rows, and settles how the
   evidence panel is added (see "To be settled"). Step 3b: the page is
   written from the slice 2 state at a chosen frame, with the precision
   check. Until slice 4, differences are expected and are shown as they
   are.
4. Browser, in a new protocol version: style sheets as Blink parsed them
   and every change script makes to them, including constructed and
   adopted sheets; font and image bytes, recorded once for each content
   hash; the environment (viewport, device pixel ratio, zoom, media
   features, and compatibility mode); hover state; and CSS animations and
   transitions. App: the recreation uses them.
5. Iframes and frames: the documents of `iframe`, `frame`, and `frameset`
   elements in the recreation, the precision check, and the evidence panel
   (decided 2026-09-29).

The timers, listeners, and accessibility checkpoint in the evidence panel
are added to the slice 2 state in slice 3b where they are already
recorded, and in slice 4 for animations and transitions.

### Required tests

- Slice 3a: unit tests of the fixed page writer and the evidence panel's
  data; a system test on the target machine: open the recreation, use the
  Elements panel and the evidence panel, move each window to another
  display and maximize it, and close the player leaving no process running.
- Slice 3b: unit tests of the document writer, including escaping of
  recorded text and attribute values, scripts and event handlers that do
  not run, blocked network access, and paths through shadow roots; unit
  tests of the precision check on generated differences; integration tests
  writing recreations from recorded files and checking them; a system test
  on the target machine at frames of a recording, including the time to
  open the recreation and the differences found.
- Slices 4 and 5: stated in their designs.

### Paths through shadow roots

Recommended and accepted 2026-09-29. XPath has no step into a shadow root,
so a node's path is a list of XPath expressions, one for each tree scope
from the document to the node. The first is evaluated from the document;
each later one from the shadow root of the element the previous one
selects. Each expression uses positional steps only, such as
`/html[1]/body[1]/div[3]`, since an `id` or other attribute may be
repeated or changed and a position in the recorded tree is not. An element
outside the HTML namespace is selected by a local name test, such as
`*[local-name()='svg'][1]`, and a text node by `text()[n]`. The evidence
panel shows the list joined by `/#shadow-root(open)` or
`/#shadow-root(closed)`, as recorded, for example
`/html[1]/body[1]/my-card[1]/#shadow-root(open)/div[1]/button[2]`, and
copies either that form or the list itself. A path follows the DOM tree,
not the tree as slots render it: a slotted node is found under its host's
light DOM, and its assigned slot is shown with it.

### To be settled

- How the evidence panel is added to DevTools. A DevTools extension panel
  ([chrome.devtools.panels](https://developer.chrome.com/docs/extensions/reference/api/devtools/panels))
  needs only an extension of our own loaded in the recreation's profile. A
  patch to the DevTools front end in the Chromium checkout needs no
  extension but is rebuilt with the browser. Slice 3a tries the extension
  first.
- How the page's scripts are kept from running while the evidence panel
  and the precision check can still act on the page, for example a content
  security policy that allows no page script, which slice 3a tests.

## Slice 3a implementation

Implemented 2026-09-29 on the `recreation` branch, which starts from
`frame-state`.

### Parts

- `Recorder.Recreation`, a new project:
  - `RecreationContent` and its records: the page, and the evidence the
    panel shows, including `NodePath`, a path as its list of scopes and the
    mode of each shadow root crossed, shown as agreed in "Paths through
    shadow roots".
  - `FixedRecreation`: the fixed page and evidence written in code. The page
    has a header and navigation, a form, a custom element with a
    declarative open shadow root, an SVG image, and 60 paragraphs below the
    first screen, so the whole page can be explored. It also has a script
    and an event handler attribute that change the title if they run. The
    evidence panel states that none of it is evidence.
  - `RecreationServer`: Kestrel on 127.0.0.1 at a random port, answering
    only under a random 256-bit token and only to requests whose Host header
    names that address and port. The page is served with a content security
    policy that allows no script, so neither the page's scripts nor its
    event handler attributes run, and allows nothing from another origin.
  - `RecreationBrowser`: writes the profile and the evidence panel, and
    opens the page in the instrumented Chromium without the recorder
    bootstrap, so it records nothing. DevTools opens for the page
    (`--auto-open-devtools-for-tabs`) in a window of its own, set by the
    profile's DevTools dock preference, so the page and DevTools can each
    be moved to any display and maximized there. Closing kills the browser
    and its child processes and removes the profile.
  - `RecreationSession`: the server and browser of one recreation.
  - The evidence panel, an unpacked extension of our own
    (`EvidencePanel\`), loaded with `--load-extension` and
    `--disable-extensions-except`. Google removed `--load-extension` from
    Chrome branded builds only, from Chrome 137
    ([Chromium extensions group](https://groups.google.com/a/chromium.org/g/chromium-extensions/c/1-g8EFx2BBY/m/8IEC1RCVAQAJ)),
    and the instrumented build is not branded. The panel reads the evidence
    from the server and inserts every value as text, never as markup, since
    recorded values are page content. Selecting a row selects the node in
    the Elements panel through `chrome.devtools.inspectedWindow.eval` and
    `inspect()`.
- The player: "Open recreation with fixed content", in the Instrumented
  Chromium group, opens the fixed page; a recreation already open is closed
  first, and closing the player closes it.

### Differences from the design

- The evidence panel is a tab in the DevTools window, so it moves with
  DevTools rather than in a window of its own.
- The recreation states that it is a recreation in its window title, which
  is the page's own, and in the evidence panel, not in the page, since text
  added to the page would change its layout.
- Blink's `document.evaluate` does not take a shadow root as its context
  node: it fails with "The node provided is '#document-fragment', which is
  not a valid context node type", found on the development machine. The
  panel's path resolver therefore matches the first step of each scope
  after the first against the shadow root's children itself, and evaluates
  the rest of the scope from the node that step selects. Closed shadow
  roots cannot be reached from page script, so the panel reports that it
  cannot select a node inside one; slice 3b settles how DevTools selects
  such a node.

### Tests

- Unit tests: paths through shadow roots and their validation; the fixed
  page and evidence; the server's token, Host check, methods, and headers,
  including the content security policy; the evidence panel's files, its
  address of the evidence, and that it inserts no markup; the browser's
  arguments, which include no recorder bootstrap or remote debugging
  switch (slice 3b adds the remote debugging port; see "Leaving the
  recreation"), and the undocked DevTools preference; and that no recreation is
  opened, and no profile left, without the browser.
- Integration test, run when `RECORDER_RECREATION_CHROMIUM` names a Chromium
  executable: the fixed recreation is opened without a window, a click on
  the button with the event handler leaves the title unchanged, the page's
  script has not changed it, and every path in the evidence selects the
  node it names through the panel's own resolver. On the development
  machine it passed with a Chromium build of the test framework's own; it
  has not yet run with the instrumented build.
- System test on the target machine, still to be run: open the recreation
  from the player; see the page and DevTools in windows of their own; move
  each to another display and maximize it; see the Evidence tab and its
  rows; select rows, including the buttons inside the shadow root, and see
  each node selected in the Elements panel; see that the title is "Fixed
  recreation" after clicking Save; scroll to the end of the page; close the
  player and see that no Chromium process of the recreation remains.

### On the target machine

2026-09-29, at b9c4d8b: the seven recreation tests passed with the
instrumented build as `RECORDER_RECREATION_CHROMIUM`, including the
integration test. At aab4934 the integration test's checks had passed, but
removing its profile failed while Chromium's child processes still held a
file in it, so the test now retries the removal. The fixed recreation opened
from the player and was reported to run very well. Of the system test's
checks: selecting Open and Close, inside the shadow root, from the evidence
panel selected them in the Elements panel; after the player closed, no
Chromium process of the recreation remained. Moving the page and DevTools
to other displays was not tested, since only one display was connected,
and remains to be tested.

## Slice 3b design: the recorded page at a chosen frame

Proposed and agreed 2026-09-29. Slice 3b replaces the fixed content of
slice 3a with the page recorded at a frame the auditor chooses, checks the
recreation against the recording, and fills the evidence panel from the
recording. What slice 4 records, and the documents of frames (slice 5), are
not part of it.

### Choosing the frame and the document

- The player gains "Inspect page at this frame" beside the frame controls,
  enabled when an open recording has browser state.
- The documents at the frame come from the slice 2 reader with no state
  loaded, as "Only the documents asked for" allows. The candidates are the
  documents of primary main frames: the document token of each is matched
  to a `navigation-completed` record whose `frameType` is
  `primary-main-frame`, which also gives its URL. Documents of the
  browser's own interface, such as `chrome://webui-toolbar.top-chrome/`,
  are primary main frames of their own in the recordings and are listed
  last, marked as browser interface.
- The player lists the candidates with their URL and the time each was
  last presented, the most recently presented first and selected, and
  opens the one chosen. The choice is the auditor's; the player does not
  infer which document the frame shows.

### Building the document exactly

The recorded DOM is not written as HTML markup, since the HTML parser does
not return every tree a script can build: for example, it moves content out
of tables ("foster parenting",
[HTML standard, parsing](https://html.spec.whatwg.org/multipage/parsing.html#foster-parent))
and closes a `p` element when a block starts inside it. Instead:

- The server returns a short document: the recorded document type if the
  recording has one (its name only, since its identifiers are not
  recorded), so the rendering mode is standards mode with a document type
  and quirks mode without; a data block holding the recorded tree as JSON,
  which is not run; and a builder script.
- The content security policy allows only the builder, by a nonce new for
  each recreation: `script-src 'nonce-...'`. The recorded `script` elements
  are built as elements and do not run, and event handler attributes and
  `javascript:` URLs do not run, as in slice 3a.
- The builder runs before the first rendering. It builds the recorded tree
  with DOM calls: each node with its recorded type, name, attributes, and
  character data, in recorded order; each shadow root with `attachShadow`
  and its recorded mode, `delegatesFocus`, `slotAssignment`, `clonable`,
  `serializable`, and `referenceTarget`; the nodes assigned to each manually
  assigned slot. It then replaces the served document's element with the
  built one and removes itself and the data block, so the Elements panel
  shows only the recorded tree.
- User agent shadow roots, such as those of `input` elements, are made by
  the browser, not the builder, and are compared like every other node.
- Element namespaces are not recorded. An element whose recorded name is in
  capitals is in the HTML namespace, since an element's name is given in
  capitals only for an element in the HTML namespace in an HTML document
  ([DOM standard](https://dom.spec.whatwg.org/#element-html-uppercased-qualified-name));
  an `svg` or `math` element and its
  descendants are in the SVG or MathML namespace. This is an inference, and
  the evidence panel says so; recording the namespace is added to slice 4.
- A value cut in the recording, marked by the slice 2 reader, cannot be
  built; the node is built with the part recorded and is listed as a
  difference with the reason "cut in the recording".
- After building: the recorded text control values and selections are set,
  the recorded focus is set with focus emulation, so it holds while
  DevTools has the keyboard, and the recorded scroll offset of each
  scrolling node is set.

### The environment

Before the page is opened, over the DevTools protocol connection of the
recreation browser: the viewport size and device pixel ratio of the latest
layout checkpoint of the document at or before the frame, through
`Emulation.setDeviceMetricsOverride`. A layout zoom factor other than 1 is
listed as a difference, since browser zoom is not set this way. Media
features are not recorded and take the browser's values; recording them is
in slice 4. The viewport size is recorded only in layout checkpoints, so a
window resized after the latest checkpoint is recreated at the older size;
the check will show it, and recording the viewport size with each change is
added to slice 4.

### Leaving the recreation

A recreation is a static copy of a recorded page, but its links and forms
are live elements: a followed link would load the live site in the
recreation's tab, in place of the recorded page. On the target machine a
recreation of a recording of https://cnib.ca did so. The recreation must not
leave the recorded page, and nothing is added to the page to stop it, since
a listener added by the builder would show in DevTools' Event Listeners
pane as though recorded.

The recorder holds the recreation browser over its DevTools protocol
connection instead:

- The browser opens a blank tab and a DevTools protocol port that it
  chooses on the loopback interface. The recorder connects, attaches to
  every tab before it runs (`Target.setAutoAttach` with
  `waitForDebuggerOnStart`), and only then opens the page.
- In every tab, every document request is paused (`Fetch.enable` for
  documents at the request stage). A request for the recreation's own
  address continues; any other is refused as a stopped navigation
  (`Fetch.failRequest` with `Aborted`), so no error page replaces the
  recreation. A tab opened by a refused request, such as a link with
  `target="_blank"`, is closed.
- Each refused navigation is recorded with its address and time and listed
  in the evidence panel, which announces it: "Navigation to ... was
  blocked; the recreation does not leave the recorded page."
- Form submission is already refused by the page's content security policy
  (`form-action 'none'`), before any request is made.

The same connection sets the viewport of "The environment" and focus
emulation (`Emulation.setFocusEmulationEnabled`), so the recorded focus
holds while DevTools has the keyboard.

### Recording the recreation to check it

The recreation browser is the instrumented Chromium with its recorder
bootstrap, as when recording, so its bridge emits the same records for the
recreated page with the same code. The recorder receives them in memory with
its browser evidence receiver; they are not written to any recording. The
receiver launches the browser with the arguments of slice 3a added: the
profile, the evidence panel, DevTools, and a remote debugging port on the
loopback interface for the protocol connection above.

The records of the recreated document, found by its URL, are applied with
the slice 2 state builder, so both sides of the check are rebuilt by the
same code. Records of DevTools and the browser's interface are ignored.

### The check

The check runs once the recreation is settled: the builder has finished,
the page's load event has fired, and no layout change set has been recorded
for 1 s after a presentation of the page. The evidence panel shows
"checking" until then. It compares the recorded state at the frame with the
recreation's state:

- Nodes are matched by position: both trees are walked in the same order,
  light DOM children and then each shadow root. Where the trees differ, the
  difference is listed and the nodes below it are not compared further.
- DOM: node type, name, attributes, character data, shadow root fields, and
  slot assignments, exactly.
- Layout: for each node, whether it has a layout object, whether a display
  lock prevents its layout, each of the 283 recorded computed-style
  properties as text, and its geometry: the local rectangle, the local
  quads, whether its client rectangle is empty, and its client rectangle
  derived from its transform nodes by the slice 2 code, all exactly. The
  transform node identities differ between the two, so only the rectangles
  they give are compared.
- Scroll offsets of each scrolling node, and the focused node, selection,
  and text control values, exactly.

The panel shows the result as a summary first, the number of nodes that
differ for each property, so that one cause, such as a style sheet that was
not recorded, is seen as one line, and then each difference with its node,
its recorded value, and its recreated value. The check describes the
recreation as it was settled; what the auditor changes afterwards is not
checked again.

Until slice 4, differences are expected wherever the page used style sheets
from files, web fonts, or images, and wherever script changed a style sheet.

### The check is a background guard

Decided by the owner on 2026-09-30: "This check, if it exists at all, is in
the background and the most the user gets is a toast warning that the
rendering is off compared the the recording. In practice it should almost
NEVER happen or the tool is useless."

A recreation is therefore required to match the recording, and the check is
a guard against the recorder failing that requirement, not a feature of the
evidence panel. It changes the design above and the proposal below:

- The check runs in the background. The auditor sees nothing of it unless
  the recreation differs from the recording, and then only a notification
  in the player that the recreation differs from the recording.
- The panel has no fidelity section and lists no differences.
- The differences are written to the recorder's diagnostics, for fixing the
  recorder, and are not evidence.
- Until slice 4 records style sheets, fonts, and images, the recreation
  cannot match the recording on most pages, and the check would warn on
  almost every recreation. The check is therefore built after slice 4, and
  slice 4 is where the requirement is met.

The proposal below is kept as the design of what the check compares and
when it runs; its parts on the panel are replaced by this section.

### Building the check (proposed)

Proposed on 2026-09-30, for agreement before it is built. Its result is
shown as "The check is a background guard" states.

The recreation browser. The recorder's own receiver
(`BrowserEvidenceReceiver`) and launcher (`ChromiumLauncher`) start the
recreation browser, as they start the browser when recording, so it has the
bootstrap, its own pipe name and authentication token, the protocol version
query, and the refusal to start from an elevated process. The launcher gains
the arguments of slice 3a (the evidence panel, DevTools in its own window, a
new window) and a remote debugging port of 0, which the browser chooses and
writes to the profile, as now. The protocol connection of "Leaving the
recreation" is unchanged. The recreation browser then records; the slice 3a
statement that it records nothing no longer holds.

The records. The receiver is given an event sink that keeps nothing on disk
and writes to no recording: each record is applied at once with the slice 2
state builder, in the order received. The recreated document is the one
whose `navigation-completed` record has the recreation's address. Records of
any other document, such as the blank tab, DevTools, and the evidence
panel's pages, and of the browser's interface, are dropped when received.
The records of the recreated document are not kept once applied; only its
state is.

When the check runs. It runs every time a page is opened with "Inspect page
at this frame", once the recreation is settled. The recreation is settled
when:

- the recreated document's DOM, layout, and interaction checkpoints of its
  first walk have completed;
- the page's load event has fired (`Page.loadEventFired` on the protocol
  connection); and
- 1 s has passed with no layout change set of the document, counted from
  its latest `presentation-feedback` record.

The wait is a timer reset by each record that applies, not a poll. If the
recreation is not settled within 30 s, the check runs on the state it has,
and the panel says it was not settled and which condition was not met.

After the check. The bridge goes on sending records while the recreation is
open, since the pipe stays connected; they are dropped when received. The
check is not run again when the auditor changes the page.

The comparison. Both sides are slice 2 states, so both are read by the same
code:

- DOM nodes are matched by position, as the design above states: the
  document's children, then each node's children, then its shadow root.
  Where the node type or name of a matched pair differs, or a node has no
  counterpart, the difference is listed and the nodes below it are not
  compared.
- Identities are not compared, since they are new in the recreation: node,
  document, checkpoint, change set, and transform node identities, the
  document token, and node indexes. A value that names a node, such as a
  shadow host, a slot's assigned nodes, the focused node, and a selection's
  anchor and focus, is compared through the node match.
- The document's URL is not compared, since the recreation's address is its
  own; the panel already shows the recorded one.
- Layout records are matched through the DOM match; a pseudo-element by its
  originating node and pseudo-element type. Each field of the layout record
  of the design above is compared as recorded, exactly: layout object,
  display lock, bounding client rectangle, each computed-style property as
  text, and the client rectangle derived from the transform nodes.
- The viewport, device pixel ratio, and layout zoom factor, the scroll
  offset of each scrolling node, and the interaction state: focused node,
  selection, and each text control's value and selection.

The result. The panel's Fidelity section reads "checking" until the result
is ready, then "checked" with the number of nodes compared and differing. It
lists the summary first, one line for each property with the number of
nodes that differ, then every difference, grouped by property, each with
its node's path, the recorded value, and the recreated value. Every
difference is listed; none is dropped. The panel reads the result from the
recorder, as it reads the blocked navigations, and announces when it is
ready. If the check cannot run, for example because the browser's protocol
version does not match, the section says "not checked" and why.

Required tests:

- Unit tests of the comparison on states built from records: equal states
  give no difference; each kind of difference above is found and counted
  once; nodes below a structural difference are not compared; identities
  and the document URL are not compared; values that name nodes are
  compared through the match; pseudo-elements are matched by originating
  node and type.
- Unit tests of the settling rule, on records given in order, and of the
  sink: records of other documents are dropped, and records after the check
  are dropped.
- Integration test, with the instrumented Chromium, which is available only
  on the target machine: a generated recreation is opened, settles, and its
  check reports no DOM difference.
- System test on the target machine: a page of a recording is inspected,
  the panel shows "checking", then the summary and the differences.

### The evidence panel from the recording

- The recreation: the frame, recording time, basis, document URL, and
  document key, and the notice that the page is a recreation.
- Pending timers: the timers of the document scheduled and neither fired
  (for a timeout) nor cancelled at the frame, with their kind, requested
  and effective delay, time scheduled, and time remaining to their next
  run, counted from their last run for an interval timer.
- Interactive elements: the nodes with an event listener registered and not
  removed at the frame, with their event types and options, and the nodes
  the latest accessibility checkpoint at or before the frame records as
  focusable, with its role and name and the time of that checkpoint. The
  panel reads the records; it does not decide what is interactive.
- Focus, selection, and text control values, from the interaction state.
- Selecting a node inside a closed shadow root is done by the recorder over
  its protocol connection, since page script cannot reach it; the method is
  settled in the first step of the slice.

The slice 2 state and its snapshots gain, for each document, its URL, its
registered listeners, its pending timers, and its latest accessibility
checkpoint, and the snapshot version is raised. Recordings made before are
not supported.

### Added to slice 4

Found while designing slice 3b: element namespaces; the checked state of
check boxes and radio buttons and the selected options of `select`
elements, which are properties, not attributes, and are not recorded; and
the viewport size with each change, not only in layout checkpoints.

Found while building slice 3b (see "Found while building"), and added with
the owner's agreement on 2026-09-30:

- A DOM walk of each document no later than its first presentation, so
  that a page can be recreated at any frame that shows it. In the
  recording of 2026-09-29 a product page was drawn at 65.52 s and first
  walked at 66.13 s.
- The contents of `template` elements. The records have no field for them,
  so the six `template` elements of that recording have no recorded
  children.

### Required tests

- Unit tests: the tree data written for the builder, including namespaces,
  shadow root fields, manual slot assignment, and cut values; the choice of
  candidate documents; pending timers and registered listeners at a frame,
  including interval timers, cancellation, and removal; the matching of
  nodes by position and the comparison of each property, on generated
  states with generated differences; the summary of differences.
- Integration tests, with `RECORDER_RECREATION_CHROMIUM` set: the builder
  builds generated trees, including trees the HTML parser cannot return,
  and the DOM the browser then holds equals the tree given; no page script,
  handler, or `javascript:` URL runs; and, with a recorded file, a
  recreation at a frame of it is opened, received, and checked.
- System test on the target machine: record a short session on a page with
  forms, a shadow root, and scrolled content; open the recreation at
  several frames; see the check's summary and differences, the timers, the
  listeners, and the focus; select nodes, including one inside a closed
  shadow root; state the time from choosing a frame to the page shown and
  to the check completed.

## Slice 3 revision: rendering from recorded values (proposed)

Proposed on 2026-09-30, for agreement before it is built.

### The decision

The owner, on 2026-09-30: "I would much prefer the rendering to use the
exact numbers. I really wanted you to use the libraries inside of chromium
to render the page, not to use the default existing browser view." Asked
whether this meant a recreation mode in Blink that imposes the recorded
styles and geometry, with DevTools working on the page, the owner answered
"Yes, I think so", and that more is to be recorded from Blink for it.

This reverses requirement 8 of the slice 3 design, "A difference is listed,
never hidden by forcing the recorded value", which was a proposal of this
design, not a requirement of the owner. Requirement 7 changes with it: the
recorded values are no longer checked against a computed rendering; they
are the rendering. The page is still a page in the instrumented Chromium,
in its own profile, with DevTools and the evidence panel, the navigation
held as in "Leaving the recreation", and the viewport emulated.

### What a recreation mode is

Normally Blink works out an element's computed style from the style sheets
(the cascade), then its boxes from the style (layout), then draws the boxes
(paint). In the recreation mode of the instrumented Chromium, for each node
of the recreated document that the recording holds:

- Style: the node's computed style is the recorded one. Blink resolves the
  element's style as usual and then sets each recorded property to its
  recorded value, at the end of `StyleResolver::ResolveStyle`
  (`core/css/resolver/style_resolver.cc`, line 1377 in the checkout on the
  target machine). DevTools' Computed pane then shows the recorded values,
  since it reads that style.
- Layout: the node's boxes are the recorded ones. Where Blink lays out a
  block, in `BlockNode::Layout` (`core/layout/block_node.cc`, line 406), the
  recreation mode builds the layout result from the recorded fragments: each
  box fragment's size and offset, and, for a block holding lines, its
  fragment items, the line boxes, text runs, and inline boxes
  (`core/layout/inline/fragment_item.h`, line 124). The layout algorithms are
  not run for it. DevTools' box model and element outlines then show the
  recorded geometry, since they read those fragments.
- Text: each recorded text run is drawn from its recorded glyphs, positions,
  and font, as a shaping result (`platform/fonts/shaping/shape_result.h`,
  line 134), not shaped again, when the recorded font is available.
- Paint: Blink paints from that style and those fragments with its own
  code, so borders, backgrounds, text, transforms, clips, and scrolling are
  drawn as Blink draws them.
- Animations and transitions are not started: the recorded style is the
  style at the instant, animated values included.

The DOM is built by the recreation mode in the renderer, not by the builder
script, so that each recreated node is known to be the recorded node it is
built from. The page then holds no script of the recorder's, and the
builder script and its data block are removed.

### What is recorded in addition

The layout checkpoints and change sets record, in a new protocol version:

- Every property `getComputedStyle()` lists (Blink's computable properties,
  `CSSComputedStyleDeclaration::ComputableProperties`,
  `core/css/css_computed_style_declaration.cc`, line 103), not only the 283
  of the list, and the custom properties.
- For each layout box, each of its box fragments: size, offset in its
  parent fragment, and the fragment's break token position when it is one
  of several, as in columns.
- For each block holding lines, its fragment items: each line box, each
  text run with its range of the node's text, and each inline box, with
  its rectangle.
- For each text run, its glyphs as shaped: glyph identifiers, advances and
  offsets, and the font: family, typeface name, size, and synthetic bold or
  italic, with a content hash of the font file when slice 4 records font
  files.
- For each scrolling box, its scrollable overflow rectangle; the scroll
  offsets are already recorded.
- For each replaced element, such as an image, its intrinsic size; its
  pixels come with slice 4.

These are recorded when they change, as the layout change sets record
changed nodes now, so an idle page repeats nothing. What this costs to
record is measured on the target machine before the recording format is
fixed.

### A read-only snapshot

Replaced on 2026-10-03 by "Input refused (agreed)" under slice 4d:
the recreation takes no input except the right-click for Inspect.

The owner, on 2026-09-30: "the page is a read-only snapshot, I didn't think
there was anything to edit except the user typing in a text field or text
area." This replaces requirement 12 of the slice 3 design. In the recreation
mode:

- Typing into a text field or text area changes its value; that control's
  text is laid out by Blink as it changes.
- Scrolling changes scroll offsets only, and is allowed.
- Every other change to the DOM or to styles is refused, whether it comes
  from DevTools' Elements and Styles panes, from script run in the console,
  or from the page's own controls, such as check boxes, radio buttons,
  `select` elements, and `details` elements.
- Reloading returns the page to the recorded instant.

### Limits

- Values that are not recorded are still worked out by Blink: Blink's
  internal style state that `getComputedStyle()` does not report, and paint
  details, such as antialiasing, that no record holds.
- A text run whose recorded font is not available is shaped again with the
  font Blink chooses, inside its recorded rectangle; the evidence panel
  lists those runs. Font files are recorded in slice 4; a system font of the
  machine the recording was made on is available when the recreation is
  opened on that machine.
- Images are drawn only once slice 4 records them; until then a replaced
  element keeps its recorded size and draws nothing.
- Layout that is not block or inline layout, such as SVG, is laid out from
  its recorded style as Blink lays it out. Whether table, flex, grid, and
  multi-column layouts, which Blink lays out as blocks with their own
  algorithms, can take recorded fragments in the same way is found by the
  first step below.

### The background guard

The check of "The check is a background guard" remains, after slice 4, as a
guard against the recreation mode failing: the recreation's own layout
checkpoint is compared with the recording, and a difference is a fault of
the recorder.

### Slices

1. A feasibility step on the target machine: in the instrumented Chromium,
   impose a recorded computed style and a recorded box size and position on
   the elements of a small fixed page, and a recorded glyph run on its text,
   and confirm that DevTools' Computed pane and box model show them and that
   Blink paints them. Table, flex, grid, and multi-column content is
   included, to find which layouts take recorded fragments.
2. Recording: the additions above, in a new protocol version, with its cost
   measured on the target machine.
3. The recreation mode: the DOM built in the renderer, the recorded styles
   imposed.
4. The recorded geometry and glyph runs imposed.

Slice 4 of the plan (style sheets, fonts, images, and the environment)
follows. Style sheets are then needed not for the styles, which are
recorded, but so that DevTools' Styles pane shows which rules applied.

### The feasibility step (proposed)

Delivered in three parts, each built and checked on the target machine
before the next is designed in detail:

- 1a, recorded styles, below.
- 1b, recorded box fragments: sizes and offsets of blocks, and of table,
  flex, grid, and multi-column content.
- 1c, recorded lines and glyph runs.

The step uses the switch the recreation mode keeps,
`--a11y-recorder-recreation`. The browser passes it to each renderer it
starts, in the child process hook that passes the recorder bootstrap, and
does so whether or not a recorder is connected, so the step runs without
one. Without the switch nothing changes.

In the step only, the recorded values are written in the test page itself,
in an attribute of each element, since the connection that will carry them
is not built yet: `data-a11y-recorded-style` holds the recorded computed
style as CSS declarations. The attribute has effect only under the switch.
The test page, `chromium/recreation_spike/styles.html`, gives each element
a style sheet value and a different recorded value, for layout, color,
font, and visibility properties.

1a, recorded styles. At the end of `StyleResolver::MatchAllRules`
(`core/css/resolver/style_resolver.cc`, line 1278), under the switch, an
element's recorded declarations are parsed as CSS and added as the last
author declarations, marked important and as element-attached, so they win
over every style sheet rule, the element's own `style` attribute, and
animations. Blink then builds the computed style from them as from any
declaration, so inherited and dependent values follow. The step records
what DevTools' Styles pane shows for them.

Checked on the target machine, with the page opened under the switch and
without it:

- With the switch, DevTools' Computed pane and `getComputedStyle()` give
  the recorded value of each property, and Blink paints with them; without
  it, the style sheet values.
- Changing the style sheet in DevTools' Styles pane does not change a
  recorded property.

Tests: unit tests of the integration script's new patch and of the switch
passed to renderers; the check above as the system test.

Built on the `recreation` branch: the switch in
`chromium/recorder_bridge/recorder_switches.h`, passed to renderers in
`AppendRecorderBootstrapToChildProcess` before the bootstrap is looked for;
`IsRecreationMode()` in the bridge; the hook, written by
`patch_blink_style_resolver` in `chromium/integrate.py`; and the test page.
The patch was applied to the checkout's `style_resolver.cc`, copied from the
target machine, and applied again without change. The unit tests of the
patch and of the switch check the text of the hook and of the bridge; they
do not compile it. Whether it compiles and behaves as intended is found by
the build and the check on the target machine.

On the target machine, with revision 06dcc58, as reported on 2026-09-30:

- The build first stopped at the V8 context snapshot step. The generator,
  run by hand with its output written elsewhere, exited with 0, and the
  build was run again; its failure was most likely the output file held
  open by a running instrumented Chromium, which was not confirmed.
- With the switch, every recorded property compared as equal in
  `getComputedStyle()`; without it, every one gave the style sheet value;
  the two pages looked different accordingly.
- DevTools' Computed pane showed the recorded `width` of `#box`, 237.5px,
  and its recorded `background-color`, rgb(0, 90, 160). Expanded,
  `background-color` listed only the style sheet's rgb(200, 0, 0) from
  `#box` at styles.html line 19. DevTools names no source for the recorded
  value, since the recorded declarations are not a rule its style
  inspection knows.
- With the switch, after the `#box` rule's width was changed to 300px in
  the Styles pane, `getComputedStyle()` still gave the recorded 237.5px.
- The Styles pane for `#box` showed the user agent style sheet's `div`
  rule, `display: block`, and no block holding the recorded declarations.
  Of the `#box` rule's declarations, none was shown as overridden until
  its width was changed to 300px; the width was then shown struck
  through, and the height and background color were not. Why the edit
  changed this was not examined.

Not yet reported: the same edit without the switch.

#### 1a addition: recorded styles in DevTools (proposed)

The check found that DevTools shows the recorded values as the computed
style but names no source for them, and shows the style sheet declarations
they override as if they were in effect. The addition makes the recorded
declarations a source of their own in DevTools' style inspection. Blink's
side of DevTools only is changed; the DevTools front end is not.

Under the switch, `InspectorCSSAgent::getMatchedStylesForNode`
(`core/inspector/inspector_css_agent.cc`, line 1461) adds, for an element
with recorded declarations, one more matched rule after all the others:

- Its selector text is `Recorded style`, so the Styles pane shows a block
  under that name.
- Its declarations are the recorded ones, each marked important, as
  imposed in style resolution.
- It has no style sheet, so DevTools offers no editing of it, as fits a
  read-only snapshot.
- Its origin is the author origin, the one Blink gives it in the cascade.

The DevTools front end orders a node's styles by the order Blink gives and
then by importance
(`front_end/core/sdk/CSSMatchedStyles.ts`), so it should show the block
first, its declarations in effect, and each style sheet declaration of a
recorded property struck through; the Computed pane should list the block
as the source of each recorded value. The same block is added for each
ancestor in the inherited entries, so an inherited recorded value is shown
as inherited from that ancestor. These are expectations from reading the
front end's source, to be confirmed on the target machine.

In the step the block's declarations are read from the same attribute as in
style resolution; when the recorded state comes over its connection, both
read it there.

Not changed by the addition: a style sheet rule can still be edited in the
Styles pane. An edit has no effect on a recorded property. When every
computed property is recorded, as the recreation will record them, no edit
has an effect on the page.

Checked on the target machine, with the test page under the switch:

- For `#box`, the Styles pane shows the `Recorded style` block first, with
  its width, height and background color, and the `#box` rule's width,
  height and background color struck through.
- The block cannot be edited.
- The Computed pane lists the block as the source of each recorded value.
- For `#child`, the Styles pane shows the block of `#parent` as inherited,
  with its color.
- Without the switch, no block is shown.

Tests: a unit test of the integration script's new patch; the check above
as the system test.

Built on the `recreation` branch: `patch_blink_inspector_css_agent` in
`chromium/integrate.py` writes the helper `RecorderRecordedStyleMatch`, its
call after the element's matched rules, and its call for each ancestor's
inherited entry. The patch was applied to the checkout's
`inspector_css_agent.cc`, copied from the target machine, and applied again
without change; the protocol names it uses were read from the checkout's
generated `css.h`. As with 1a, the unit test checks the text of the patch,
not its compilation. DevTools shows each recorded declaration with
`!important`, as Blink reports an important declaration.

On the target machine, with revision 979b17e, as reported on 2026-09-30,
the build succeeded and every check above passed: the Styles pane showed
the `Recorded style` block for `#box` first, with its three recorded
values, and the `#box` rule's width, height and background color struck
through; the block could not be edited; the Computed pane listed the block
as the source of the recorded background color; for `#child`, the Styles
pane showed the block of `div#parent` as inherited, with its color; and
without the switch no block was shown.

#### 1b, recorded box fragments (proposed)

Proposed on 2026-09-30, for agreement before it is built.

A box fragment is the rectangle Blink's layout produces for one box: its
border-box size, and its offset in the fragment that holds it, its parent
fragment. Paint, hit testing, `getBoundingClientRect()`, and DevTools'
element highlight and box model are all read from fragments. 1b makes the
recorded size and offset of a box the ones Blink uses.

Where. Every layout algorithm, for block, flex, grid, table, and
multi-column layout alike, ends by turning its fragment builder into a box
fragment, in `BoxFragmentBuilder::ToBoxFragment`
(`core/layout/box_fragment_builder.cc`, line 673 in the checkout on the
target machine). Under the switch, at the start of that function, before
the builder is finalized:

- The box's own size: when its element has a recorded fragment, the
  builder's inline and block sizes are set to the recorded width and
  height.
- Its children's offsets: for each child fragment whose element has a
  recorded fragment, the child's offset in the builder is set to the
  recorded offset.

Blink then finalizes the fragment from these values as from its own, so
overflow and scrolling follow.

This differs from the earlier text of this design, which had the layout
algorithms not run for a box with recorded fragments. Here the algorithms
still run and their sizes and offsets are replaced at the one place every
algorithm passes through. Building a layout result without an algorithm
would need a new algorithm that lays out children itself and would lose the
data that tables, flex, and grid add to their fragments, which paint and
DevTools use. Whether replacing sizes and offsets is enough is what 1b
finds out.

In 1b only, as in 1a, the recorded values are written in the test page, in
an attribute of each element: `data-a11y-recorded-fragment="x y width
height"`, the border-box offset in the parent fragment and the border-box
size, in CSS pixels. They are multiplied by the element's effective zoom to
give Blink's layout units.

Not covered by 1b, left to Blink's own layout:

- A box broken into several fragments, as across columns, and any box laid
  out under block fragmentation: a recorded fragment is one fragment, so
  only a fragment that is the whole of its box takes it.
- Line boxes and the boxes inside lines, such as inline blocks: these are
  1c.
- A writing mode other than horizontal, left to right. Blink's builder
  holds logical offsets and the recorded offsets are physical; they are
  equal only in that writing mode, and 1b does not convert between them.
- Anonymous boxes, which have no element to carry a recorded fragment; the
  test page has none between a recorded box and its parent.
- Data that algorithms add to their fragments beside sizes and offsets,
  such as a table's column positions and a grid's tracks, which Blink uses
  to paint collapsed table borders and DevTools uses for its grid overlay.

The test page, `chromium/recreation_spike/boxes.html`, gives each case a
recorded fragment that differs from what its style sheet produces:

- Block boxes in normal flow, the second recorded above the first.
- A float and an absolutely positioned box.
- Flex items and grid items at recorded offsets and sizes.
- A table's cells, rows, and the table itself.
- Boxes inside a multi-column container, whose parent fragment is a
  column; these are checked by eye, since the column has no element.

Checked on the target machine, with the page opened under the switch and
without it:

- With the switch, for each recorded element except those in columns,
  `getBoundingClientRect()` gives its parent element's rectangle moved by
  the recorded offset, with the recorded size; a Console snippet, given
  with the build, compares them. Without it, Blink's own layout.
- DevTools' element highlight and box model show the recorded rectangle,
  and the page is painted there.
- What DevTools' Layout pane overlays show for the flex and grid
  containers, and how the table's borders and the columns' content are
  painted, is reported.

Tests: a unit test of the integration script's new patch; the check above
as the system test.

Built on the `recreation` branch: `patch_blink_box_fragment_builder` in
`chromium/integrate.py` writes the helper `RecorderRecordedFragment` and
the hook at the start of `ToBoxFragment`, and
`chromium/recreation_spike/boxes.html` is the test page. The patch was
applied to the checkout's `box_fragment_builder.cc`, copied from the target
machine, and applied again without change. As with 1a, the unit test checks
the text of the patch, not its compilation. In an unmodified Chromium in
the development sandbox, the comparison snippet reported Blink's own layout
for every compared element, as expected without the switch. The builder's
scrollable overflow and the data it gathers from children as they are
added, such as their bounds for anchor queries, are not recomputed from the
recorded offsets.

On the target machine, with revision 5ea654e, as reported on 2026-09-30,
everything checked tallied with the recorded values. The cells of the
table's second row were painted slightly to the right of those of the
first. This came from the test page, not from Blink: the second row was
recorded at an offset of 2 pixels in its section, and the first row, which
has no recorded fragment, was placed by Blink at 0. The page now records
the second row at 0.

#### 1c, recorded lines and glyph runs (proposed)

Proposed on 2026-09-30, for agreement before it is built.

A block holding text has, beside its box fragment, a list of fragment
items: for each line, a line item, followed by an item for each text run
and each inline box on the line. A text item holds its range of the block's
text, its rectangle, and its shaping result: the glyphs to draw, each with
its glyph identifier, the character it belongs to, and its advance. Paint,
hit testing, selection, `Range.getClientRects()`, and DevTools read these.
1c makes the recorded items and glyphs the ones Blink uses.

Where. Blink lays out the lines with its own line breaking and shaping, as
it does now, and then converts the items' offsets to physical ones, in
`FragmentItemsBuilder::ConvertToPhysical`
(`core/layout/inline/fragment_items_builder.cc`, line 377 in the checkout
on the target machine). Under the switch, at the end of that function, for
a block whose element has recorded items, in a horizontal, left to right
writing mode:

- Each line item takes the rectangle recorded for the line of the same
  index.
- Each text item takes the rectangle recorded for its text range, and, when
  glyphs are recorded for that range, a shaping result built from them,
  with the item's font.
- Each inline box item, such as an inline block, whose element has a
  recorded fragment takes that rectangle.

Rectangles of items are in the block's coordinates, as Blink holds them.
The patch adds to Blink two setters on `FragmentItem`
(`core/layout/inline/fragment_item.h`), for an item's rectangle and a text
item's shaping result, and a function on `ShapeResult`
(`platform/fonts/shaping/shape_result.h`, line 134) that builds a shaping
result from given glyphs and advances with a given font, after the pattern
of its `CreateForSpaces`.

In 1c only, the recorded values are written in attributes of the block's
element:

- `data-a11y-recorded-lines`: "x y width height" for each line, separated
  by semicolons.
- `data-a11y-recorded-text`: "start end x y width height" for each text
  item, the range being offsets in the block's text.
- `data-a11y-recorded-glyphs`: "start end" and then, for each character of
  the range, a glyph and its advance in CSS pixels. A recording holds glyph
  identifiers of the font; a page cannot know them, so in 1c each glyph is
  given as a code point, "U+0058", and Blink's glyph for it in the item's
  font is used.

Not covered by 1c, left to Blink:

- Line breaking. Recorded items are matched to Blink's items by line index
  and text range, so when Blink breaks the text differently from the
  recording, the unmatched items keep Blink's values; the check reports
  whether that happens on the test page. Imposing the recorded breaks is a
  later step, if needed.
- Glyph offsets, the shifts of a glyph from its pen position, which are
  zero in most Latin text, and text drawn with more than one font, as with
  font fallback: a recorded run is drawn with the item's primary font.
- Writing modes other than horizontal, left to right, as in 1b.
- Ink overflow and other values Blink derives from items after this point
  are worked out from the recorded values; values derived before it, such
  as the line's baseline, are not.

The test page, `chromium/recreation_spike/lines.html`, has a block of two
lines separated by a line break, with the second line recorded above the
first, the first line's text recorded at a different offset, and the second
line's glyphs recorded as other characters with fixed advances; a paragraph
holding an inline block with a recorded fragment; and a paragraph with no
recorded values, which is unchanged.

Checked on the target machine, with the page opened under the switch and
without it:

- With the switch, for each recorded text item, `Range.getBoundingClientRect()`
  over its text gives the block's rectangle moved by the recorded offset,
  with the recorded size; a Console snippet, given with the build, compares
  them. Without it, Blink's own layout.
- The lines are painted in the recorded places, and the second line shows
  the recorded glyphs at their recorded advances.
- Selecting text on the recorded lines with the mouse highlights the
  recorded glyph positions.

Tests: unit tests of the integration script's new patches; the check above
as the system test.

Built on the `recreation` branch: in `chromium/integrate.py`,
`patch_blink_fragment_item_header` adds the setters,
`patch_blink_shape_result_header` and `patch_blink_shape_result` add
`ShapeResult::CreateFromRecordedGlyphs`, and
`patch_blink_fragment_items_builder` writes the attribute readers and the
hook at the end of `ConvertToPhysical`; `chromium/recreation_spike/lines.html`
is the test page. Each patch was applied to its file copied from the
checkout on the target machine, and applied again without change. As with
1a and 1b, the unit tests check the text of the patches, not their
compilation. In an unmodified Chromium in the development sandbox, the
comparison snippet reported Blink's own layout for every compared item, as
expected without the switch.

On the target machine, revision f23a582 did not compile: the hook passed a
braced initializer list to WTF's `Vector::push_back`, which cannot deduce
its argument type from one. The hook now names the type, and a checkout
holding the earlier hook is upgraded to it.

On the target machine, with revision acc629a, as reported on 2026-09-30,
the build succeeded and the checks passed: with the switch, the comparison
snippet reported every recorded text item and the inline block as equal to
its recorded rectangle; the lines, the recorded glyphs, and the inline block
were painted as recorded, and the paragraph without recorded values was
unchanged; and selecting the recorded second line highlighted its recorded
glyph positions.

With 1c, the feasibility step is complete: recorded styles, box fragments,
lines, text items, and glyphs can each be imposed in Blink under the
switch, and DevTools shows the recorded styles as their source.

### Stage 2: recording for the recreation (agreed; 2a, 2b, and 2c built and measured)

Proposed on 2026-09-30 and agreed by the user the same day, with changed
style values only and packed glyph arrays. 2a is built and not yet tested
on the target machine; see "2a as built" below. This is slice 2
of "Slices" above: the recorded additions, in a new protocol version, with
their cost measured on the target machine.

#### What is recorded, and when

The additions are recorded in the layout change sets of
`change-driven-recording.md`, for the nodes those change sets already note,
and in the full walks of slice 5 there, for every node walked. Nothing is
noted that is not noted now: a box that receives a new layout result is
noted with the objects its fragment items name and the objects of its child
fragments, which covers each record below. As now, a record equal to the
node's last record is not sent again.

1. Computed style. Every property `getComputedStyle()` lists
   (`CSSComputedStyleDeclaration::ComputableProperties`,
   `core/css/css_computed_style_declaration.cc`, line 103), read at run time
   rather than from the fixed list of 283, and every custom property with
   its value. The list read is recorded in each change set's start record,
   so a value is a name and a value, as now. After a node's first record, a
   record holds only the properties whose values differ from the node's last
   record, and the custom properties added, changed, or removed; the bridge
   keeps a hash of each value for this. A node whose document the bridge has
   stopped tracking, as it does beyond 64 documents, is recorded in full
   again, so a dropped hash costs a repeated record and never a lost one.

2. Box fragments, in a new record, `layout-box-fragments`, for each noted
   node with a layout box: each of its physical fragments
   (`LayoutBox::PhysicalFragments()`, `core/layout/layout_box.h`, line 583),
   in order, with:
   - its border-box size;
   - its child links (`PhysicalBoxFragment::Children()`): for each child,
     its node, or, for an anonymous box or a column, its kind and its index
     among the links, the child's fragment index, and its offset;
   - its scrollable overflow rectangle, when it has one
     (`PhysicalBoxFragment::ScrollableOverflow()`);
   - for a replaced element, such as an image, its natural size.
   Offsets are recorded at the parent, as the recreation imposes them in
   1b. A box broken across columns has one entry for each fragment, with its
   break position.

3. Fragment items, in the same record, for each fragment that holds lines
   (`PhysicalBoxFragment::Items()`): the block's text content as laid out,
   recorded when it changes, and each item in order, with its type (line,
   text, generated text, or box), its rectangle in the fragment, and, for a
   text item, its range of the text content, and for a box item, its node.

4. Glyph runs, for each text item, from its shaping result
   (`ShapeResultView::ForEachGlyph`, `platform/fonts/shaping/
   shape_result_view.h`, line 137), which reports each glyph with its font,
   so text drawn with fallback fonts is recorded as several runs: for each
   run, its font (family, typeface name, size, and synthetic bold or
   italic), and for each glyph its identifier, the character it belongs to,
   its advance, and its offset. Glyphs are written as packed little-endian
   arrays encoded in base64 (2 bytes for an identifier and for a character
   index, 4 for an advance and for each offset), not as lists of numbers, so
   a glyph costs 14 bytes before encoding.

The protocol version becomes 0.37. A recording made before keeps its
records and has none of these.

#### What the app does with them

The recorder's validator accepts the new records and fields; the browser
state of slice 2 of the plan keeps, for each node, its latest computed
style, applying the changed properties of each record to the last, its box
fragments, and, for a block, its fragment items and glyph runs; and the
state snapshots, at a new snapshot format version, hold them. The player
shows nothing new yet; the recreation reads them from stage 3.

#### Sub-steps

Each is built, checked on the target machine, and measured before the next:

- 2a, computed style: every computable property and the custom properties,
  with changed properties only after a node's first record.
- 2b, box fragments: sizes, child offsets, scrollable overflow, and natural
  sizes.
- 2c, fragment items and glyph runs.

#### Checks and measurement

- At each layout checkpoint the change check of `change-driven-recording.md`
  already compares the state rebuilt from change records with the
  checkpoint. It is extended to the additions: the rebuilt computed style,
  fragments, items, and glyphs of each node equal those of the full walk.
- For each sub-step, a recording on the target machine of the same pages,
  made with the earlier package and with the new one, compares the bytes
  written per channel and per minute, the bytes per change set, and the
  rendering update time the bridge already reports. What it costs is
  recorded in this document before the next sub-step.

#### Limits

- Values Blink holds outside fragments and items, such as a table's column
  positions and a grid's tracks, are not recorded; 1b found that the
  recreation does not need them for the cases tested.
- Fonts are recorded by name; font files come with slice 4 of the plan.
- A transform or opacity animation running on the compositor is not
  recorded, as now.

#### 2a as built

Protocol 0.37. The differences from the design above are marked.

- The checkpoint helper in `chromium/integrate.py` reads the property list
  from `CSSComputedStyleDeclaration::ComputableProperties` for the
  document's execution context, and keeps the names in a static list. The
  fixed list of 283 is kept in the script
  (`LEGACY_FIXED_LIST_BLINK_LAYOUT_CHECKPOINT_HELPER`) only to recognise and
  upgrade a checkout patched before 0.37.
- Custom properties are read with
  `ComputedStyleCSSValueMapping::GetVariables` and the document's property
  registry, which reads each name `ComputedStyle::GetVariableNames` holds
  as `getComputedStyle()` resolves it, and are recorded by name in
  code-unit order. (`ComputedStyleCSSValueMapping::Get`, which reads one
  name, is private in the reference checkout.) The names include the custom
  properties an element inherits, so an element under a page that defines
  many of them records each one in its first record.
- Each checkpoint node and change record states `customProperties`, an
  object of name and value, or null when `computedStyle` is null.
- Difference from the design: the property list is recorded in each layout
  checkpoint's start record (`styleProperties`), as before, and not in each
  change set's start record. A node's first change record holds every
  property by name, and every later record names the properties it holds,
  so the list adds nothing a reader needs, and repeating it would add
  several kilobytes to every change set.
- After a node's first record, the bridge (`LayoutChangeFilter::
  ReduceToStyleChanges` in `chromium/recorder_bridge/layout_changes.h`)
  keeps a hash of each style value and of each custom property, and a
  change record holds only the values that differ from the node's last
  record, with `computedStyleComplete` false and
  `removedCustomProperties` listing the custom properties the node no
  longer has. A first record, and a record after a record of the
  `browser.layout` channel was lost, holds every value, with
  `computedStyleComplete` true and `removedCustomProperties` null. A node
  whose document the bridge stopped tracking is recorded in full again.
  `computedStyleComplete` is null when `computedStyle` is null.
- The app's validator checks these fields together: a record of changes
  must state its custom properties and removals, and a complete record
  must not list removals. The layout change state merges a record of
  changes into the node's last record, so the state and its snapshots hold
  each node's whole style; a merged record states that it is complete only
  when the record it was merged into was. The snapshot format is
  unchanged, since it holds node records as they are.
- The layout change check compares the rebuilt custom properties with the
  checkpoint's, as `custom-properties`, and counts a rebuilt style that was
  never recorded whole as `computed-style-incomplete`.

The cost of 2a is measured on the target machine before 2b is built. The
app test `RecordingCostReport` (`tests/Recorder.Tests/RecordingCostReport.cs`),
run with `RECORDER_COST_FILE` naming a recording file, reports the bytes of
each channel, over the recording and per minute, before chunk compression,
and the bytes of each layout record type and of an average change set. The
rendering update time is read from the bridge's "Recorder evidence cost"
lines in the recording's Chromium log.

#### 2a results

Two recordings on the target machine on 2026-10-01, with the check setting
off, of the same pages (the same twelve navigation addresses, on
`www.cnib.ca` and the pages it embeds): one at revision acc629a (protocol
0.36), 1.53 minutes, and one at revision d3e1598 (protocol 0.37), 0.86
minutes. A third recording at d3e1598 held no browser evidence and no
Chromium log, and is not used. Neither recording dropped an event. The
bytes are those of the records before chunk compression, from
`RecordingCostReport`; the times are the sums of the bridge's "Recorder
evidence cost" lines.

| | acc629a (0.36) | d3e1598 (0.37) |
| --- | --- | --- |
| Properties listed per element | 283 | 480 |
| `browser.layout` bytes | 110,175,252 | 93,899,552 |
| `layout-node-changed` records, bytes | 14,402, 103,675,974 | 15,591, 80,865,359 |
| `layout-checkpoint-node` records, bytes per record | 1,114, 4,163 | 1,255, 9,422 |
| Change sets, bytes per change set | 342, 308,120 | 202, 405,249 |
| `recorder.state` (snapshots) bytes | 82,519,732 | 99,578,451 |
| Bridge time in change sets: calls, total, mean, largest | 1,360, 0.307 s, 226 us, 31.1 ms | 530, 1.080 s, 2,038 us, 157.6 ms |
| Layout checkpoint walks: count, total, largest | 21, 0.076 s, 25.7 ms | 18, 0.290 s, 98.9 ms |

The 0.37 change records split as follows: 3,439 whole records (a node's
first, 17,424 bytes each, with 480 values and 59.4 custom properties on
average); 8,963 records of changes only (1,733 bytes each, with 0.7 style
values and 0.4 custom properties on average); and 3,189 records with no
style (1,697 bytes each). The checkpoint nodes held 138.8 custom properties
on average.

The layout change check compared 18 checkpoints and 1,255 nodes of the
0.37 recording, and every node matched in every field, custom properties
included; the 0.36 recording's 21 checkpoints and 1,114 nodes also all
matched. With the check setting off, a checkpoint is a document's first
walk, so these comparisons do not yet exercise the merging of records of
changes into later states; a recording with the check setting on does.

What this shows, within these two recordings:

- For the same pages, `browser.layout` held 15 percent fewer bytes,
  although each element lists 480 properties instead of 283 and its custom
  properties: a later record of a node holds only what changed.
- The snapshots grew by 21 percent, since they hold each node's whole
  style.
- The bridge's time in change sets rose from a mean of 226 us to 2,038 us
  per call, and its largest single call from 31.1 ms to 157.6 ms; these
  calls run on the renderer's main thread at the end of a rendering
  update. The bridge's lines do not time Blink's reading of the values
  before the call, so the whole cost to a rendering update is larger than
  this and is not measured.
- The recordings differ in length, so per-minute rates are not compared.

The owner, on 2026-10-01, on the main-thread time: "for now, let's carry
on and come back to the optimization later. There may be more to do, so we
can deal with it in one session." The time of 2a is to be measured in full
and reduced after 2b and 2c, together with theirs.

#### 2b design (agreed)

Proposed on 2026-10-01 and agreed by the owner the same day, with both
changes from the outline. It fills in item 2 of "What is recorded, and
when" above, with two changes from it, marked.

Where it is recorded. Change from the outline: the fragments are a new
field of the node records the change sets and checkpoints already write,
`boxFragments`, not a new `layout-box-fragments` record. In the 2a
recording a record with almost no fields, `layout-changes-completed`, took
1,292 bytes, so a record of its own for each box would add about that much
to every box recorded; and the bridge's comparison with the node's last
record, which decides whether a record is sent, then covers the fragments
with no second cache. A node record is sent when its fragments differ from
its last record, as when its style or geometry differs; the whole
`boxFragments` is sent each time, not only what changed.

Protocol version. Change from the outline: 2b is protocol 0.38, not 0.37,
since 0.37 was built and recorded for 2a, and a live connection needs an
exact version match.

When a node is read. A box that receives a new layout result is already
noted, with the objects of its child fragments
(`RecorderNoteLayoutResult`); a new size, child offset, scrollable
overflow, or natural size comes with a new layout result, so nothing new is
noted. The checkpoint walk reads every node, as now.

What `boxFragments` holds, for a node whose layout object is a layout box
(`LayoutObject::IsBox`), and null otherwise (a text node, an inline box
such as a `span`, which 2c covers through its block's items, and a node
without a layout object):

- `effectiveZoom`: the box's `ComputedStyle::EffectiveZoom`.
- `fragments`: each of the box's physical fragments
  (`LayoutBox::PhysicalFragments`, `core/layout/layout_box.h`, line 583),
  in order. Each holds:
  - `width` and `height`: its border-box size (`PhysicalFragment::Size`).
  - `breakToken`: null for a fragment that ends the box; otherwise the
    position its next fragment continues from
    (`PhysicalBoxFragment::GetBreakToken`): `consumedBlockSize`,
    `sequenceNumber`, and `atBlockEnd`.
  - `scrollableOverflow`: its scrollable overflow rectangle
    (`PhysicalBoxFragment::ScrollableOverflow`) when it has one
    (`HasScrollableOverflow`), and null otherwise.
  - `children`: each child link (`PhysicalBoxFragment::PostLayoutChildren`,
    the latest generation of each child), in order, with `x` and `y`, its
    offset in this fragment (`PhysicalFragmentLink::Offset`), and `kind`:
    - `box`: a box with a DOM node, an element or pseudo-element, named by
      `nodeId`, with `fragmentIndex`, which of that node's fragments it
      is. That node's own record holds its fragment.
    - `anonymous`: a box Blink generated with no node, such as an
      anonymous block, and `column` and `page`: a column or page
      fragment of a multi-column or paged box. These have no record of
      their own, so the child holds its fragment, `fragment`, in the same
      form, nested.
    - `line`: a line box, if a fragment holds one as a child; a block's
      lines are otherwise its fragment items, which 2c records.
- `naturalSize`: for a replaced element, such as an image, its natural
  dimensions (`LayoutReplaced::ComputeNaturalSizingInfo`,
  `core/layout/natural_sizing_info.h`): `width`, `height`, `hasWidth`,
  `hasHeight`, `aspectRatioWidth`, and `aspectRatioHeight`; null for
  other boxes.

Units. Every length is the value Blink holds, a layout unit (1/64 of a
pixel) written as a number, which is exact; physical, not logical; and
zoomed, as Blink lays out, so a length in CSS pixels is the value divided by
`effectiveZoom`. Recording Blink's own values keeps them exact for the
recreation, which gives them back to Blink.

What the app does. The validator checks the field's shape and that a
`box` child names a node and the others do not; the contracts gain its
records; the layout change state keeps it with the node, a record of style
changes carrying the node's current `boxFragments` as it carries its
geometry; snapshots hold it, as they hold node records as they are. The
layout change check compares each checkpoint node's `boxFragments` with the
rebuilt one, as `box-fragments`, exactly, since both are read from the same
fragments.

Cost. Measured as for 2a, with a recording of the same pages at 0.37 and
0.38, and the check setting on in one recording so the comparison covers
nodes recorded after their first record.

Limits:

- 1b imposes one fragment per box in horizontal, left-to-right writing;
  the record holds every fragment and its break position, and physical
  offsets in any writing mode, so the recreation can take more cases
  later without a new recording.
- Data layout algorithms keep beside sizes and offsets, such as a grid's
  tracks, are not recorded, as above.
- A child link's fragment is read in its latest generation; a fragment
  Blink holds from an earlier generation is not recorded.

Required tests:

- Unit: the integration script's tests of the new reading in both the
  checkpoint and the change set; the bridge's serialization of the field
  and its part in the comparison with the last record; the validator's
  checks; the layout change state and check with fragments, including a
  record of style changes; the contract round trip.
- System, on the target machine: the two recordings above, with the check
  setting on in the 0.38 one and no `box-fragments` differences.

#### 2b as built

Built on 2026-10-01; not yet compiled or recorded on the target machine.

- Integration script (`chromium/integrate.py`): the layout checkpoint
  helper gains `RecorderReadBoxFragment` and `RecorderReadBoxFragments`,
  called in the checkpoint walk after a node's shadow fields, and the
  change set's node reader calls the same reader at its end. A checkout
  patched at 0.37 has its change-set definition recognised and replaced;
  the helper region is rewritten whole, as before. New includes:
  `block_break_token.h`, `layout_replaced.h`, `natural_sizing_info.h`, and
  `physical_fragment_link.h`.
- Bridge (`chromium/recorder_bridge`): `LayoutBoxFragments`,
  `LayoutBoxFragment`, and `LayoutFragmentChild` in `layout_changes.h`, a
  field of `LayoutCheckpointNode`, so the checkpoint and the change set
  share them; the node hash covers every field, so a node whose fragments
  differ from its last record is recorded again. `SetLayoutNodeFields`
  writes `boxFragments`, and the size estimate counts it.
- App: contracts `BrowserLayoutBoxFragments`, `BrowserLayoutBoxFragment`,
  `BrowserLayoutFragmentChild`, `BrowserLayoutBreakToken`, and
  `BrowserLayoutNaturalSize`. Validator rules and errors:
  `browser-layout-box-fragments-without-box`,
  `browser-layout-fragment-child-node-inconsistent`,
  `browser-layout-fragment-child-fragment-inconsistent`, and
  `browser-layout-break-token-inconsistent`. The layout change state needs
  no change, since a record of style changes is merged by copying every
  other field, `boxFragments` included. The layout change check compares
  the field exactly and reports `box-fragments compared`. Snapshots hold
  node records as they are, so they hold the field without a format
  change.

Two details added in building, beyond the design:

- `breakToken` also states `breakBefore`, whether the token is a break
  before the box rather than inside it. Blink states no sequence number
  for such a token (`BlockBreakToken::SequenceNumber` requires one that is
  not), so `sequenceNumber` is null exactly then.
- A `box` child whose fragment is not among its node's fragments has a
  null `fragmentIndex`.

No database migration, as for 2a: the database tables are not written for
recordings that have a recording file.

#### 2b results

Recorded by the owner on 2026-10-01 on the target machine with a3c21b4, on
the same pages as the 2a recording: two recordings at 0.38, one without the
check setting (20261001-200852) and one with it at every 100 updates
(20261001-201008). The 0.37 recording of 2a (20261001-185400) is the
baseline. Bytes are those of the records before chunk compression.

| | 0.37 | 0.38 | 0.38, check on |
| --- | --- | --- | --- |
| Minutes recorded | 0.85 | 0.99 | 1.21 |
| `browser.layout` bytes | 93.9 MB | 101.3 MB | 106.1 MB |
| Change sets | 202 | 229 | 257 |
| Bytes per `layout-node-changed` record | 5,187 | 5,343 | 4,885 |
| Bytes per checkpoint node record | 9,422 | 9,368 | 9,372 |
| Bridge change-set time, mean | 2,038 µs | 1,767 µs | 1,380 µs |
| Bridge change-set time, largest | 157.6 ms | 158.4 ms | 157.0 ms |
| Walk field reading, mean per walk | 56 µs | 83 µs | 85 µs |
| Walk field reading, largest | 280 µs | 620 µs | 571 µs |

- Size. In the 0.38 recording without the check, the `boxFragments` JSON
  of change records was 2.83 MB of their 87.8 MB (3.2%), in 9,463 records
  holding 9,765 fragments and 15,267 child links; in checkpoint records it
  was 90 KB of 12.1 MB (0.7%), for 348 boxes. The recordings differ in
  length and in the number of change sets, so the totals are not compared
  as a rate.
- Time. The bridge's time per change set did not rise; its differences
  are within those between recordings of the same build. Blink's reading of
  the fragments in a change set happens before the bridge is called and is
  not timed. In a walk it is part of the node-fields phase, whose mean rose
  from 56 to 83 µs per walk.
- Layout check. In both 0.38 recordings every compared checkpoint node
  matched, box fragments included: 1,288 of 1,288 nodes, 706 of them with
  a change record whose box fragments were compared, and 1,265 of 1,265
  nodes, 674 compared. The largest rectangle difference was
  1.3 × 10⁻⁵ CSS px.
- Limit. Every checkpoint in both recordings was a document's first walk,
  so the comparison covers each node's first record only, not a record
  after it, nor a record of style changes merged into an earlier one. The
  check setting walks a document at every Nth layout checkpoint it
  requests; the busiest document had 79 change sets, and no check walk was
  recorded. A recording with a smaller interval is needed to cover later
  records.

A third 0.38 recording, by the owner on 2026-10-01 with the same build and
pages and the check setting at every 10 updates (20261001-202215), covers
later records:

- 35 checkpoints: 20 first walks and 15 check walks, all compared.
- 19,516 of 19,516 compared checkpoint nodes matched in every field,
  16,092 of them with a change record whose box fragments were compared
  (null ones included). The largest rectangle difference was
  2.5 × 10⁻⁴ CSS px.
- The change records held 9,934 records of style changes alone
  (`computedStyleComplete` false), merged into the nodes' earlier records;
  no compared node's rebuilt style was incomplete and none differed. This
  is the first recording on the target machine to check the 2a merging.

#### 2c design (agreed)

Proposed on 2026-10-01 and agreed by the owner the same day, with its three
changes from the outline. It fills in items 3 and 4 of "What is recorded,
and when" above, with the changes marked.

Where it is recorded. As in 2b, in the node records: each fragment in a
block's `boxFragments` gains `items`, and the block's `boxFragments` gains
`textContent`. A block is noted when it receives a new layout result, with
the objects its items name, so nothing new is noted.

Protocol version. Change from the outline: 0.39, since 0.37 and 0.38 are
built and recorded.

The text content. `textContent` is the block's text as laid out
(`FragmentItems::NormalText`, `core/layout/inline/fragment_items.h`, line
47), which item offsets index, and `firstLineText`, the text for
`::first-line` when Blink holds one (`FirstLineText`), and null otherwise.
Both are recorded whole, with no length limit. As the outline says, they
are recorded when they change: the bridge keeps a hash of each node's last
text, and a record whose text equals it states `textContent` and
`firstLineText` null with `textContentUnchanged` true. The app's layout
change state then keeps the node's earlier text, as it keeps earlier style
values in 2a, and after a lost record the bridge forgets its hashes, so the
next record holds the text again.

The items. For each fragment with items (`PhysicalBoxFragment::Items`),
`items` lists them in Blink's order, a pre-order list in which a line or
box item is followed by its descendants (`FragmentItem::DescendantsCount`,
`core/layout/inline/fragment_item.h`, line 280). It is null for a fragment
without items. Each item holds:

- `type`: `line`, `text`, `generated-text`, or `box`
  (`FragmentItem::Type`, line 151).
- `x`, `y`, `width`, `height`: its rectangle in the fragment
  (`RectInContainerFragment`, line 239), in layout units.
- `descendantsCount`, for a line or box item.
- `nodeId`: the node of its layout object, or null for one with no node,
  such as an anonymous box. For a text item it is the text node, for a box
  item the inline box or atomic inline.
- For a text or generated-text item: `start` and `end`, its range of the
  text content (`TextOffset`); `firstLineStyle`
  (`UsesFirstLineStyle`); `direction`, `ltr` or `rtl`
  (`ResolvedDirection`); `hiddenForPaint` (`IsHiddenForPaint`); and its
  glyph runs.
- For a generated-text item, such as an ellipsis, a list marker, or a
  hyphen, which is not part of the text content: `generatedText`, its
  text (`GeneratedText`).

The glyph runs. For each text item, its shaping result
(`FragmentItem::TextShapeResult`) is read with
`ShapeResultView::ForEachGlyph`
(`platform/fonts/shaping/shape_result_view.h`, line 137), which reports
each glyph with its font. A run is a sequence of glyphs with the same font,
orientation, and rotation, so text drawn with fallback fonts gives several
runs. Each run holds:

- `font`: `family` (`FontPlatformData::FontFamilyName`), `postScriptName`
  (the typeface's), `size` (`FontPlatformData::size`), `syntheticBold`, and
  `syntheticItalic`.
- `horizontal` and `rotation`: the glyphs' orientation and their rotation
  in vertical text, as the callback reports them.
- `glyphs`: a packed little-endian array, encoded in base64, of 18 bytes
  per glyph: the glyph identifier (2 bytes), the character index as Blink
  reports it, an index into the text content (4 bytes), the glyph's
  position along the run, the total advance before it (4 bytes, a float),
  and its offset, x and y (4 bytes each, floats).

Changes from the outline in the glyphs. First, a character index takes 4
bytes, not 2: it indexes the block's text content, which can be longer than
65,535 code units, and a limit on it would drop glyphs. Second, the
callback reports the total advance before each glyph rather than its own
advance, so that is what is recorded; a glyph's advance is the difference
from the next glyph's, and the last glyph's is the item's width less its
position. A glyph costs 18 bytes before encoding, 24 after.

Units. Item rectangles are layout units, physical and zoomed, as in 2b.
Glyph positions and offsets are the floats Blink reports, in zoomed
pixels. SVG text items are scaled as Blink holds them and are recorded as
they are.

What the app does. The validator checks the shapes, that every item's
range lies within the text content, that each glyphs field decodes to a
whole number of 18-byte glyphs, and that `textContentUnchanged` comes only
with null text; the contracts gain the records; the layout change state
keeps a node's text when a record states it unchanged; snapshots hold the
merged records as they are. The layout change check compares each
checkpoint node's `boxFragments`, items and text included, exactly, as for
2b; a rebuilt node whose text was never recorded whole is noted as
`text-content-incomplete`.

Cost. Measured as for 2b: one 0.39 recording of the same pages without the
check setting, against the 2b recording without it, and one with the check
at every 10 updates.

Limits:

- Fonts are recorded by name, as above; font files come with slice 4 of the
  plan.
- Ink overflow, text decorations, and emphasis marks are not recorded;
  paint computes them from style and the recorded glyphs.
- A ruby annotation's items are recorded as Blink holds them; the
  recreation of ruby is not designed.

Required tests:

- Unit: the integration script's tests of the item and glyph reading in
  the checkpoint and the change set; the bridge's packing of glyphs, its
  text hashes, and their part in the comparison with the last record; the
  validator's checks; the layout change state's keeping of unchanged text;
  the check with items and text; the contract round trip.
- System, on the target machine: the two recordings above, with no
  `box-fragments` or `text-content-incomplete` differences in the one with
  the check.

#### 2c as built

Built on 2026-10-01 at protocol 0.39 and tested on the target machine the
same day; see "2c results". As designed, with these additions and details:

- Text of an anonymous block. A block's loose text beside its child blocks,
  as in `<div>text<p>para</p></div>`, is laid out in an anonymous block,
  which has no node and whose fragment is held by its parent's child link
  (2b). Its items index its own text, not the node's, so each fragment held
  by a child link that holds items records `textContent` and
  `firstLineText` itself, whole and with no unchanged marker. A node's own
  fragments state both null, their text being the node's in
  `boxFragments`. The node's text hash covers only the node's own text;
  the record's hash covers both, so a changed anonymous text still sends
  the record.
- Generated text has no range. Blink holds a generated-text item's text
  apart from the text content (`FragmentItem::GeneratedTextItem`,
  `core/layout/inline/fragment_item.h`, line 81), so only a text item
  states `start` and `end`; a generated-text item states them null and
  states `generatedText`.
- Conversion. Text and names are converted to UTF-8 with an unpaired
  surrogate replaced by U+FFFD
  (`Utf8ConversionMode::kStrictReplacingErrors`), which keeps every UTF-16
  offset the items state.
- Where the packing happens. Blink's reading fills plain glyph records,
  each with its identifier, character index, total advance, and offset,
  and the bridge packs them as designed when it writes the record. The
  integration script lets Blink call only the bridge's exported entry
  points, so the packing stays in the bridge, where its unit test is.
- The app reads a run's glyphs with `BrowserLayoutGlyphs.Unpack` in the
  contracts, which refuses a run that is not whole 18-byte glyphs.
- Validator errors: `browser-layout-fragment-item-inconsistent` (an item
  without what its type states, or spanning past the list),
  `browser-layout-fragment-item-range-outside-text`,
  `browser-layout-glyphs-invalid`, and
  `browser-layout-text-content-inconsistent` (text stated where it does not
  belong, missing where items are, or stated with the unchanged marker).
  A range is checked against the text it indexes when the record holds
  it, and not when the text is left out as unchanged.
- The layout change check notes `text-content-incomplete` instead of
  comparing the box fragments when the rebuilt record still marks its text
  unchanged.
- Blink's garbage-collection plugin refuses a raw pointer to a font in an
  ordinary structure, so the glyph reading, which keeps the current run's
  font, is marked `STACK_ALLOCATED()`; it lives only for one
  `ForEachGlyph` call.

#### 2c results

Recorded by the owner on 2026-10-01 on the target machine with ff1a2a2, on
the same pages as 2b: one recording without the check setting
(20261002-005805) and one with it at every 10 updates (20261002-005927).
The 2b recordings without the check (20261001-200852) and with it at every
10 updates (20261001-202215) are the baselines. Bytes are those of the
records before chunk compression.

| | 2b | 2c | 2b, check | 2c, check |
| --- | --- | --- | --- | --- |
| Minutes recorded | 0.99 | 1.08 | 1.12 | 1.04 |
| Change sets | 229 | 262 | 288 | 244 |
| Bytes per change set | 388,515 | 397,691 | | 407,847 |
| Bytes per `layout-node-changed` record | 5,343 | 5,234 | 5,214 | 5,504 |
| Bytes per checkpoint node record | 9,368 | 9,730 | 9,658 | 9,938 |
| Bridge change-set time, mean | 1,767 µs | 1,968 µs | | 2,001 µs |
| Bridge change-set time, largest | 158.4 ms | 166.5 ms | | 166.6 ms |
| Walk field reading, mean per walk | 83 µs | 236 µs | | 2,405 µs |
| Walk field reading, largest | 620 µs | 1,580 µs | | 21.3 ms |

The blank cells are those not computed for the 2b check recording.

- Size. In the 2c recording without the check, the change records held
  15,827 items in 102.7 MB: their JSON without glyph runs was 3.50 M
  characters, and their 6,004 glyph runs 4.31 M, of which the base64 of
  142,344 glyphs was 3.42 M. Text took 38 K characters; 3,157 records left
  their text out as unchanged. Items and runs together are about 7.6% of
  the change-record bytes. The bytes per change set rose 2.4% over 2b; the
  recordings differ in length and in their change sets, so this is not a
  rate.
- Time. The bridge's mean time per change set rose from 1,767 to
  1,968 µs, which includes packing and encoding the glyphs; Blink's reading
  of items and glyphs in a change set is not timed. In a walk, reading them
  is part of the node-fields phase. Its mean rose from 83 to 236 µs per
  walk over first walks, and in the check recording, whose 14 check walks
  read whole documents, to 2,405 µs, with a largest walk of 21.3 ms. The
  check walks run only with the check setting.
- Layout check. Every compared checkpoint node matched in every field,
  items, glyphs, and text included: 1,412 of 1,412 nodes over 20 first
  walks without the check, and 18,632 of 18,632 over 20 first and 14 check
  walks with it, 15,253 of them with box fragments compared (null ones
  included). There was no `text-content-incomplete` note, and the 2,868
  records in the check recording that left their text out had it put back
  from earlier records. The largest rectangle difference was
  2.5 × 10⁻⁴ CSS px.
- Limits. The pages are those of 2b only. The glyph runs were checked as
  equal between the change records and the walks, not against what was
  painted, which stage 3 does by rendering them.


#### Required tests

- Unit tests of the bridge's new records against the record contract, and
  of the encoding of glyph arrays.
- Unit tests of the validator and of the browser state for the new records,
  including a computed style rebuilt from changed properties and a
  snapshot holding fragments and glyphs.
- Integration tests of the integration script's new hooks.
- The extended change check on recordings from the target machine, as the
  system test.

### Stage 3: a recorded frame rendered from recorded values (agreed; built)

Proposed on 2026-10-01 and agreed by the owner the same day ("yes I do"). The owner, on
2026-10-01: "I would prefer to experience the rendering of a frame rather
than worry about optimization. Let's build the tool, at least to the point
where I can make qualitative judgments on responsiveness." Stage 3 is
therefore the shortest path from the player to a recorded frame drawn by
Blink from the recorded values, on the paths built already, with the
recorded styles, box fragments, items, and glyphs all imposed. It takes
slices 3 and 4 of "Slices" above together, and leaves building the DOM in
the renderer for later.

What the owner does. In the player, at a frame, "Inspect page at this
frame" lists the pages and opens the one chosen, as in slice 3b. The
recreation browser now starts the instrumented Chromium with
`--a11y-recorder-recreation`, so the page is drawn from the recorded
values, with DevTools open on it.

How it is built:

- The DOM is built by the builder script of slice 3b, unchanged, with the
  recorded text control values, selection, focus, and scroll offsets.
- The values come from the document's layout change state at the frame
  (`LayoutDocumentChangeState`), which holds each node's latest record
  with its style changes and unchanged text merged in, as the change check
  uses it.
- They reach Blink as in the feasibility step, in attributes the builder
  sets on each element before it is inserted, read only under the switch:
  `data-a11y-recorded-style`, the recorded computed style and custom
  properties as CSS declarations, read by the 1a hook unchanged; and
  `data-a11y-recorded-layout`, the node's recorded `boxFragments` JSON as
  recorded, replacing the attributes of 1b and 1c. Text nodes carry
  nothing: a text's items are in its block's record.
- The 1b hook, at the start of `BoxFragmentBuilder::ToBoxFragment`, takes
  the box's size from its recorded fragment, and each child's offset from
  the recorded child link at the same index, when the builder holds as
  many children as the record holds links. The fragment is the record's
  first for a box that is not fragmented; a fragmented box keeps Blink's
  sizes, as in 1b.
- The 1c hook, at the end of `FragmentItemsBuilder::ConvertToPhysical`,
  takes each item's rectangle from the recorded item at the same index,
  when Blink's items have the recorded types in the recorded order and the
  block's text content equals the recorded text; otherwise the block keeps
  Blink's items.
- Glyphs: a text item with one recorded run whose font has the PostScript
  name and size of the item's primary font is drawn from the recorded glyph
  identifiers and the advances between their recorded positions, with
  `ShapeResult::CreateFromRecordedGlyphs` changed to take glyph
  identifiers rather than code points. Any other item keeps Blink's
  shaping inside its recorded rectangle.
- Blink then paints from these with its own code.

How responsiveness is shown. The player times each part of opening a
frame: reading the state at the frame, writing the page, starting or
reusing the recreation browser, building the DOM, and the first paint
after it, read from the page's `requestAnimationFrame` after the build.
The times are listed in the evidence panel's notes and written to the
app's log, beside the owner's own judgment.

What the owner can judge: the time from choosing the page to seeing it,
scrolling and DevTools on the recreated page, and how closely the drawing
matches the frame's screenshot.

Limits of this stage:

- The recorded values are visible as two attributes on each element in
  DevTools' Elements pane, and attribute selectors see them. Moving them
  off the DOM is the later step that builds the DOM in the renderer.
- Images draw nothing, and web fonts are not available, so text in a web
  font keeps Blink's shaping with a fallback font in its recorded
  rectangle; fonts and images come with slice 4 of the plan. On the
  machine the recording was made on, system fonts are those recorded.
- Glyph offsets are not imposed, and text drawn with several fonts, as
  with font fallback, keeps Blink's shaping.
- Only horizontal, left to right writing modes take recorded fragments and
  items, as in 1b and 1c.
- A block whose recorded items or text do not match Blink's, and a box
  whose children do not match its record, keep Blink's layout; how many do
  is counted by the hooks and listed in the panel, so a mismatch is seen,
  not hidden.
- The fidelity guard stays as designed, after slice 4.

Required tests:

- Unit: the attributes the page writer produces from a layout change
  state, including merged style changes and a node without a record; the
  recreation browser's switch; the integration script's tests of the
  changed hooks, including the matching rules and the font test for
  glyphs; the timing notes.
- Integration, with `RECORDER_RECREATION_CHROMIUM` set: a generated page
  with recorded values is opened, and the box sizes, child offsets, and
  item rectangles read back over the DevTools protocol equal the recorded
  ones.
- System, on the target machine: a frame of the cnib recording opened from
  the player, judged by the owner, with the panel's timings and counts.

#### Stage 3 as built

Built on 2026-10-01, not yet run on the target machine.

- The page writer (`RecordedPage`) gives each element of the tree data a
  `recordedStyle` and a `recordedLayout`, from the element's record in the
  document's layout change state, or null without one. The style is each
  computed style property with a value, then each custom property, written
  `name: value;`; the layout is the record's `boxFragments` JSON unchanged.
  The builder sets them as `data-a11y-recorded-style` and
  `data-a11y-recorded-layout` before the element is inserted.
- The recreation browser passes `--a11y-recorder-recreation`. It still has
  no recorder bootstrap, so it records nothing.
- The 1a style hook is unchanged. The 1b and 1c helpers and hooks, and
  `ShapeResult::CreateFromRecordedGlyphs`, are replaced; the integration
  script upgrades a checkout that holds the feasibility versions, and stops
  with an error if feasibility text is left after the upgrade. The
  feasibility attributes `data-a11y-recorded-fragment`, `-lines`, `-text`,
  and `-glyphs` are no longer read, so the spike pages `boxes.html` and
  `lines.html` no longer show recorded values.
- Revision 44736a8 did not compile: in a `BoxFragmentBuilder` member the
  `Node` class is hidden by the builder's `Node()` method. The box hook
  declares the node with `auto`, and the integration script upgrades the
  44736a8 hook.
- The hooks parse the attribute with Blink's JSON parser
  (`platform/json`) each time they run for a box or block.
- Box fragments: the box takes the size of its only recorded fragment, and
  child link `i` gives the offset of builder child `i` when the counts are
  equal.
- Items: a block with an element reads its element's record; an anonymous
  block reads the fragment of the first anonymous child link of its
  parent's record whose `textContent` equals the block's text. The items
  are imposed when their count, types in order, text ranges, and the text
  match.
- Glyphs: the packed glyphs are decoded with Blink's `Base64Decode`. A
  glyph's advance is the next glyph's recorded position less its own, and
  the last glyph's is the recorded item width less its position. Each glyph
  keeps its recorded character index, so a glyph may stand for more than
  one character. The PostScript name is compared as Skia reports it for the
  typeface, and the size within 0.001.
- A box or block whose recorded values were not imposed is reported by a
  warning in the document's console, naming the node so that DevTools can
  reveal it. Repeated messages are not shown again. The panel does not
  count them; its notes say where they are.
- Timings: the recorder times closing the previous recreation, reading the
  state, writing the page, starting the browser to its DevTools port, and
  attaching to the tab, and serves them as `timings.json`. The builder
  records, from the page's time origin, when it started, read the tree,
  built the DOM, finished the first style and layout, which it forces once
  for the measure, finished, and when a task posted from the next animation
  frame ran, after that frame's paint. The panel shows both under "Time to
  open the recreation". The recorder has no log of its own, so the times
  are not written to one.

- The window (added on 2026-10-01, after the owner found the window too
  narrow to show the page and its scroll bar): before the viewport is
  emulated, the recorder reads the window's frame from the blank tab as
  `outerWidth - innerWidth` and `outerHeight - innerHeight`, and sets the
  window's size to the recorded viewport plus that frame with
  `Browser.setWindowBounds`. The viewport is still emulated after, so a
  window the screen cannot hold keeps the recorded layout. The owner
  reported on 2026-10-01 that with revision 2fa7988 the window shows the
  whole page.

Tests at this stage:

- Unit: the window size for a recorded viewport and frame; the tree data's
  recorded style and layout, after a record of
  changes is merged, and none for a text node or an element without a
  record; the notes; the browser's switch; the server's `timings.json`;
  the integration script's hooks, their upgrade from the feasibility
  versions, and the refusal of a feasibility hook it cannot upgrade. The
  integration script's 170 tests and the bridge's C++ test pass; the .NET
  tests pass except those that need Windows or a database. One snapshot
  test, `AFileCutShortIsReadFromItsSnapshotsBeforeTheCut`, failed once in
  the full run and passed when run alone twice; it does not touch the
  recreation.
- Integration: `TheRecreationModeImposesTheRecordedBoxFragmentsAndItems`
  runs when `RECORDER_RECREATION_CHROMIUM` names the instrumented Chromium;
  it has not been run yet.
- System: not yet run.

### Slice 4a: fonts, images, and children matched by node (proposed)

Asked for by the owner on 2026-10-01, after the first stage 3 recreation of
the recording 20261002-005927. Its Console listed 918 boxes and blocks that
kept Blink's layout: 580 text items in another font, 190 boxes whose number
of children differed from the recording, 89 blocks whose items differed,
and 59 blocks with no recorded items. Of about 13,200 glyph runs in that
recording, 12,741 name the family "." with no PostScript name: the page's
web fonts, whose names Blink's typefaces do not give. A recreation without
them falls back to another font, breaks lines elsewhere, and so holds a
different number of line boxes, which undoes the recorded offsets of every
child of the box. This step records the fonts and images, uses them in the
recreation, and matches children to the recording one by one.

#### What is recorded (protocol 0.40)

1. Font files. Each typeface a glyph run uses, web font or installed, is
   identified by the SHA-256 digest of its font file as Blink holds it: the
   bytes Skia's `SkTypeface::openStream` returns, which for a web font are
   the file after Blink's sanitizer, as Blink draws with it, since Blink
   discards the downloaded bytes once decoded (`FontResource::ClearData`).
   Each glyph run gains `fontFile`: the digest, the collection index
   `openStream` returns, and the typeface's variation position, axis tag
   and value. A renderer records a `font-file` record, the digest and the
   bytes, the first time it meets a digest. The digest is kept for each
   typeface, so a file is read and digested once in a renderer.
2. Font faces. When a `FontFace` of a document finishes loading, from an
   `@font-face` rule or from script, a `font-face-loaded` record holds the
   document, a face number unique in the renderer, its family and its
   descriptors as Blink serializes them (style, weight, stretch,
   unicode-range, feature settings, display, ascent, descent and line-gap
   overrides, size adjust), its source's URL when it has one or its local
   font name, and the digest of the font file of the source it loaded
   from. A face leaving the document's set of faces, as when its style
   sheet is removed, records `font-face-removed` with the face number.
3. Images. When an image resource finishes loading, before Blink clears
   its encoded bytes (`ImageResource::Finish`), an `image-resource` record
   holds the URL requested, the response's URL, status, and MIME type, and
   the digest of the encoded bytes. A renderer records an `image-data`
   record, the digest and the bytes, the first time it meets a digest. This
   covers `img`, `picture`, `input type=image`, SVG `image`, video posters,
   and CSS images, which all load through image resources. Multipart images
   and images that fail their integrity check are not recorded.
4. The records go on a new topic, `browser.resources`. A record is as large
   as what it holds; the protocol's frames already allow 2 GB.

The digest uses the SHA-256 of `//crypto`, which the bridge gains as a
dependency. Reading, digesting, and copying run on the renderer's main
thread, once for each file or image in a renderer; what they cost is
measured on the target machine with the recordings of this step.

#### What the recreation does with them

1. The page is served at its recorded address. The recorder navigates the
   recreation's tab to the recorded document URL, and answers that
   document's request itself through `Fetch.fulfillRequest`
   ([Fetch domain](https://chromedevtools.github.io/devtools-protocol/tot/Fetch/)),
   with the page it writes now and its content security policy. Every
   relative URL of the page then resolves as it did when recorded. The
   builder and the tree data are written into the page, the builder with
   the policy's nonce, so nothing else is fetched to build it. Reloading
   asks for the same document, and is answered the same way.
2. Every other request of the tab is paused at the request stage and
   answered from the recording or refused, so nothing reaches the network:
   - an image at a URL the recording holds, loaded at or before the frame,
     is answered with its last recorded status, MIME type, and bytes;
   - a font file is answered by its digest, at an address of the recorder's
     own (`https://a11y-recorder.invalid/<token>/font/<digest>`), which the
     policy's `connect-src` allows;
   - anything else, such as a style sheet, which is not recorded until a
     later step, is refused. DevTools' Network panel lists what was
     answered and what was refused.
3. Before it builds the DOM, the builder adds to the document's set of
   faces each face the document had loaded at the frame and not removed,
   as a `FontFace` made from the recorded family, descriptors, and the
   bytes of its font file
   ([FontFace constructor](https://developer.mozilla.org/en-US/docs/Web/API/FontFace/FontFace)),
   and waits for them to load. Blink then matches the recorded
   `font-family` values to the same faces as when recorded, so line
   breaks, line heights, and baselines come from the recorded fonts, not
   only the glyphs. The time this takes is a step of "Time to open the
   recreation".
4. A glyph run's recorded glyphs are used when the font Blink chose for
   the text has the recorded font file's digest and size. The recreation
   digests a typeface once, as the recording does. Installed fonts are
   matched the same way, so a playback machine with a different version of
   a font reports the text as in another font, not drawn with wrong glyphs.
5. Images draw from their recorded bytes, from the first frame of an
   animated image. Image boxes already take their recorded size.

#### Children matched by node

The box hook gives each child of a box the offset of the recorded child
link that is the same child, not the link at the same index:

1. A child box with an element takes the offset of the link of kind "box"
   whose node is that element's recorded node. The builder adds the
   recorded node to `data-a11y-recorded-layout`, as `node`.
2. Line boxes take the offsets of the recorded line links in order, when
   the box holds as many lines as recorded.
3. Anonymous boxes take the offsets of the recorded anonymous links in
   order, when the box holds as many as recorded.
4. A child that matches no link keeps the offset Blink gave it, and is
   reported once, naming the reason: no recorded link for its node, or a
   different number of lines or of anonymous boxes. The box's own size is
   imposed in every case, as now.

So an extra line, or an image showing its alternative text, no longer
undoes the offsets of the box's other children.

#### Limits

- Style sheets are still not recorded, so a face that the page declared
  but had not loaded at the frame is not added, and DevTools' Styles pane
  still shows no rules. Faces load in the recreation from recorded bytes,
  so a face that was still loading when recorded is shown loaded if its
  file had been recorded by the frame.
- A font installed on the recording machine and not the playback machine
  is not added to the recreation: installed fonts are matched by digest
  and reported when they differ. Their files are recorded, so adding them
  is possible later.
- Images not recorded: multipart images, images that failed their
  integrity check, and images loaded before recording started in a
  renderer that kept them in its memory cache. `data:` URLs are not
  requests, so they draw from the recorded attribute.
- Media other than images (video, audio) is not recorded.
- The `font-face-removed` record covers faces removed from a document's
  set; a face whose family or descriptors script changes after loading is
  recorded as it was when it loaded.

#### Sub-steps

1. Children matched by node (no new recording needed), and the recreation
   served at its recorded address, with every request answered or refused.
2. Protocol 0.40: font files, font faces, and images recorded, with their
   cost measured on the target machine, as for stage 2.
3. The recreation uses them: faces added before the build, images answered
   from the recording, glyphs matched by digest.

Each sub-step is tested on the target Windows machine before the next.

#### Sub-step 1 as built

Agreed by the owner on 2026-10-01.

Children matched by node:

- The builder's `data-a11y-recorded-layout` attribute now starts with the
  element's recorded node, as `{"node":<id>,` followed by the recorded
  `boxFragments` members. The box hook reads the node from the start of a
  child's attribute without parsing the rest.
- The box hook indexes the recorded child links by kind: links of kind
  "box" by `nodeId`, and "line" and "anonymous" links in order. A node with
  more than one link matches none. Each child of the builder then takes the
  offset of its own link: a box with a node by its recorded node, a line
  box or an anonymous box by its order among its kind when the counts of
  that kind are equal.
- The Console reasons are now: a different number of line boxes, or of
  anonymous boxes, named on the parent; and, named on the child, no child
  link for its recorded node, or no recorded node, as for a
  pseudo-element or an element without a layout record.
- The integration script upgrades the stage 3 box hook and its helper, and
  the 44736a8 hook, to these, and refuses a checkout that still holds the
  stage 3 text.
- Revision dcdb84c did not compile: Blink's `String` names the method
  `starts_with`, not `StartsWith`. The helper calls `starts_with` on the
  attribute, and the integration script upgrades the dcdb84c helper.

The page served at its recorded address:

- `RecreationContent.DocumentUrl` holds the recorded document's URL when
  it is an absolute http or https URL. The recreation's tab is navigated
  to it, and every request of every tab is paused at the request stage.
- The recorded document's address, without its fragment, is answered with
  `Fetch.fulfillRequest` with the page, its content security policy, and
  the headers the loopback server sends. An http address is also answered
  at https, and the browser is started with
  `--disable-features=HttpsUpgrades`, so that it does not upgrade the
  navigation first.
- Another navigation of a tab's main frame, whose frame ID is the tab's
  target ID, is refused and listed in the evidence panel, as before. Any
  other request, such as an image, a style sheet, or an iframe's document,
  is refused with `BlockedByClient` and counted; DevTools' Network panel
  lists it. Nothing is continued to the network.
- The builder script is written into the page, inside the script element
  its nonce allows, and runs on `DOMContentLoaded`, as a deferred script
  would; the loopback server no longer serves `builder.js`. The page is
  refused if the builder ever holds `</script` or `<!--`.
- A recorded address that is not http or https, such as `about:blank`, is
  served from the loopback server as before, with a note saying that its
  relative URLs do not resolve as recorded.

Tests at this sub-step:

- Unit: the recorded node at the start of the layout attribute; which
  addresses are served, and which requests are answered; the page and
  policy of the answer; the builder written into the page; the box hook's
  matching, its upgrade from the stage 3 hook, and the check that it names
  no `Node` type, which `BoxFragmentBuilder::Node()` hides. The
  integration script's 172 tests pass; the .NET tests pass except the four
  that need Windows.
- Integration, run in this environment with a stock headless Chromium:
  `TheRecordedPageIsServedAtItsRecordedAddress` (the tab shows the
  recorded address; a relative image address resolves against it and is
  refused; a link away is refused; reloading builds the page again), and
  `TheRecreationDoesNotLeaveThePage`,
  `TheBuilderBuildsTheRecordedTreeExactlyAndRunsNoPageScript`, and
  `TheFixedRecreationRunsNoPageScriptAndEveryPathSelectsItsNode` still
  pass. The box hook needs the instrumented Chromium and is checked on the
  target machine.

#### Sub-step 1 on the target machine

Reported by the owner on 2026-10-02 for recording
20261002-005927-d604cba519d04477ad353f0b9cf5f287, page
`https://www.cnib.ca/en/event`, built from revision f23b5d0:

- The address bar showed the recorded address, and DevTools' Network panel
  listed the page's images as blocked.
- No improvement in the page's appearance was seen.
- The Console listed 580 text items in another font or in more than one
  glyph run, 124 boxes with a different number of line boxes, 89 blocks
  whose items differed, 62 children with no recorded node, 59 blocks with
  no recorded items, and 2 boxes with a different number of anonymous
  boxes.

Measured in the running recreation through its DevTools port:

- All 1623 elements with a layout record had their recorded border box
  size, and every child box with a recorded node was at its recorded
  offset from its parent, within 0.5 pixels. The box layout is the
  recorded one.
- 357 elements in the body had no recorded style and were drawn, in 56
  subtrees. Among them are `#header-collapsible`, which holds a second
  search form and menu, `#block-octheme-search`, a `div.hidden`, and a
  `div.col-12.col-md-10` in each event listing. The recording holds a
  layout record for these elements with `layoutObjectPresent` false and no
  computed style: they were not rendered at the frame. The recreation
  writes no recorded style for them, so the user agent's style applies and
  they are drawn over the recorded content.

The remaining differences seen are text in another font, which wraps
differently, and images, which slice 4a's later sub-steps address.

#### Elements without a layout object

Proposed on 2026-10-02 and agreed by the owner the same day.

- An element whose layout record at the frame has `layoutObjectPresent`
  false is written with a `data-a11y-recorded-no-layout-object` attribute.
  Its value is `none` when no element or text below it, in its subtree or
  its shadow trees, had a layout object at the frame, and `contents` otherwise, as for an element
  styled `display: contents`. The value is an inference from the recorded
  layout objects, not a recorded style; the recording holds no computed
  style for such an element.
- In the recreation mode, style resolution adds `display` with that value
  as an important declaration, after the recorded style, so the element
  takes no box, as at the frame.
- DevTools' Styles pane lists the declaration as a rule of its own named
  "No layout object recorded", separate from "Recorded style", so that the
  inference is not shown as a recorded value.
- The evidence panel states how many elements were given each value, and
  why.
- An element with no layout record at all at the frame is unchanged.
- This needs a Chromium build, and is tested on the target machine with
  the same recording.

Required tests: unit tests of the attribute and its value for an element
with and without a rendered descendant, through a shadow root, and for an
element with no layout record; integration script tests of the style and
inspector hooks; a browser integration test that such an element takes no
box in the instrumented build.

As built:

- `RecordedPage.NoLayoutObjectDisplays` works out the values from each
  node's latest layout record, without recursion, so a deep tree cannot
  exhaust the stack. The tree data carries the value as `noLayoutObject`,
  and the builder writes the attribute before the element is inserted.
- The style hook replaces the stage 1a hook, which the integration script
  upgrades. Both the recorded style and the inferred display go into the
  one set of important declarations, the inferred display last, so it
  replaces a recorded display.
- A copy of an element in a user agent shadow tree, as an svg `use`
  element makes, does not take the inferred display, in style resolution
  or in DevTools: the copy's layout object was not the one recorded.
- DevTools' rule is added after the recorded style's, by a helper and a
  matched rule of its own, and is not reported for ancestors, since
  `display` is not inherited.
- Tests: the unit test covers `none`, `contents` through an open and a
  closed shadow root, an element without a record, and the evidence
  panel's note. The integration script's 173 tests pass. The .NET tests
  pass except the four that need Windows. The stage 3 browser test, run
  with the instrumented build, now also checks that such an element takes
  no box; it runs on the target machine.

On the target machine, reported by the owner on 2026-10-02 for revision
55e3125: the instrumented Chromium built and the recreation ran, and fewer
elements that were not rendered at the frame appeared on the page.

#### Sub-step 2 as built

Asked for by the owner on 2026-10-02: "We should probably move on to fonts
and images". Protocol 0.40, on the topic `browser.resources`.

Records:

| Record | Holds |
| --- | --- |
| `font-file` | the digest, the size, and the bytes in base64 |
| `font-face-added` | the document and the face number |
| `font-face-loaded` | the document, the face number, the family, the descriptors, the source, and the face's font file by digest and index |
| `font-face-removed` | the document and the face number |
| `image-resource` | the URL requested, the response's URL, status, and MIME type, the size and digest of the bytes, and `dataRecorded` |
| `image-data` | the digest, the size, and the bytes in base64 |

Each glyph run gains `fontFile`: the digest, the collection index, and the
variation position as a list of axis tags and values, or null when Skia
gives no readable file for the typeface. A digest is the SHA-256 of the
bytes in lowercase hexadecimal, from `crypto::hash::Sha256`.

How it is read:

- Font files are read in Blink with `SkTypeface::openStream`, once for each
  typeface in a renderer. The bridge keeps each typeface's digest and index
  by `SkTypeface::uniqueID()`, which Skia does not reuse within a process,
  and records a `font-file` record the first time the renderer meets a
  digest. A typeface whose record could not be queued is left unmet, so its
  file is read again the next time a run uses it.
- The bytes of a `font-file` or `image-data` record are copied on the main
  thread and encoded to base64 on the writer thread, through the queue the
  layout records use.
- A face's records come from `FontFace`. `FontFace::SetLoadStatus` records
  `font-face-loaded` when the status becomes loaded. `FontFaceCache`, which
  holds a document's set of faces, records `font-face-added` in
  `AddFontFace`, and `font-face-removed` in `RemoveFontFace`, which also
  serves `ClearCSSConnected`, and in `ClearAll` for the faces of style
  sheets. The face number is a member patched into `FontFace`, assigned
  from a counter in the bridge when the face is first recorded.
- The descriptors are the strings of `FontFace`'s getters. The source is
  `CSSFontFace::FrontSource()`, the source the face loaded from: `url` with
  its URL, `data-url`, `binary` from script, or `local`. The face's font
  file is read from the typeface its `FontCustomPlatformData` decoded,
  through an accessor patched into that class, since the member is private.
  A `local` source has no such data, and its `fontFile` is null.
- Images are recorded in `ImageResource::Finish`, before `ClearData`, when
  the resource is neither multipart nor failed its integrity check, and its
  URL is not a `data:` URL.

Differences from the design above:

- `font-face-added` is added. A face that script loads and never adds to
  `document.fonts` loads without joining the document's set, so the loaded
  record alone does not say which faces the document had. A face is in the
  set from its added record to its removed record; the loaded record holds
  what it is.
- A `local` source does not record the local font name: Blink keeps it in a
  private member with no accessor, and the glyph runs that use the face
  hold the installed font's file by digest.
- `ClearAll` records the removal of the faces of style sheets only, since
  the cache keeps no list of the faces script added, apart from their
  families.
- `image-resource` states `dataRecorded`, false when the image's bytes
  could not be queued, so a missing `image-data` record is stated rather
  than inferred.
- Records of images and font files name no document: Blink shares image
  resources between the documents of a renderer through its memory cache,
  and font files between faces.

Not done in this sub-step: the instrumented Chromium integration test of a
generated page with a web font and an image, in "Required tests" below.
What is recorded is checked on the target machine with a recording of the
page of recording 20261002-005927, with its cost.

Tests: the bridge's change hash covers the font file, index, and variation
position; the integration script's tests cover the font file reader, the
face and cache hooks, the image hook, and their call shapes against the
bridge; the .NET tests cover each record against the record contract and
the typed contracts, and a glyph run's font file.

#### Sub-step 2 on the target machine

Recording 20261002-162024 at revision 881c098, made by the owner on
2026-10-02 of the same pages as recording 20261002-005927, with the same
values entered.

- `browser.resources` holds 602 records, 20,481 KB before chunk
  compression: 13 `font-file`, 199 `font-face-added`, 46
  `font-face-loaded`, 199 `font-face-removed`, 89 `image-resource`, and 56
  `image-data`.
- Every `font-file` and `image-data` record's bytes decode to its stated
  size and digest. The 13 font files are 11 distinct files, since each
  renderer records its own; 4,192 KB in all, each an OpenType file (10 with
  TrueType outlines, 1 with CFF outlines). The 56 images are 8,609 KB.
- All 5,291 glyph runs in the recording's layout records carry a
  `fontFile`, naming 10 digests, each of which has a `font-file` record.
- All 46 loaded faces have a `url` source and a font file with a
  `font-file` record. Their families are Stag Sans Web (30), OpenSans (8),
  Plakkaat (4), PrefsFramework-Icons (3), and Google Sans (1).
- All 89 images loaded with status 200 and had their bytes recorded: 34
  JPEG, 32 GIF, 16 PNG, and 7 SVG, from 89 URLs.
- A document's faces are removed and added again when its style sheets
  change: in one document, 19 faces were added at 9.05 s, all 19 removed
  at 22.5 s and 19 new faces added, which loaded within 0.06 s. Every face
  is removed when its document closes.

Cost, from the bridge's "Recorder evidence cost" lines, which cover only
part of the recording (95 of the 199 removals): `RecordFontFile`, which
digests a file and queues its bytes, 16 calls, 5.3 ms in all, the largest
0.79 ms; `RecordBlinkImageResource` 89 calls, 6.4 ms, the largest 2.8 ms;
the face records 340 calls, 3.6 ms. Blink's reading of a font file and its
copy of an image's bytes happen before these calls and are not measured.

#### Sub-step 3 as built

Asked for by the owner on 2026-10-02, after sub-step 2 on the target
machine.

What the app reads:

- `RecordingFileResources` (`src/Recorder.Database/RecordingFiles/`) reads
  the `browser.resources` records of the recording file up to the
  recording time the document's state is read at. It reads only the chunks
  that hold records of that channel, through each chunk's message index.
- A face is named by its browser instance, renderer, and face number. It is
  in the document's set from its `font-face-added` record to its
  `font-face-removed` record, matched to the document by its state key.
  Faces are kept in the order they were added. A face in the set is added
  to the recreation when it has a `font-face-loaded` record at or before
  the frame, a font file at collection index 0, and a `font-file` record
  for that digest.
- An image is the latest `image-resource` record for its URL at or before
  the frame. It is found by the URL requested or by the response's URL,
  each without its fragment. A record whose `dataRecorded` is false removes
  the image for its URL.
- The bytes of a font file or an image are not decoded until the
  recreation asks for them. The resources hold a reader of their own on
  the file, so the recreation can still read bytes after the recording is
  closed in the player; the recreation's server disposes it. Bytes that do
  not match their digest are not used.
- Reading takes a step of its own in "Time to open the recreation":
  "Reading the page's fonts and images from the recording".

What the recreation does:

- The builder's data gains `fontFaces`. Each entry holds the family, the
  recorded descriptors, the digest, and the face's address at the
  recorder: `https://a11y-recorder.invalid/<token>/font/<digest>`, with a
  token new for each recreation.
- Before it builds the tree, the builder reads each distinct file once with
  `fetch`. It then makes each face with
  `new FontFace(family, bytes, descriptors)` and adds it to
  `document.fonts`, in the recorded order, and waits for every face to
  load. A face that fails is listed in the builder's notes and in DevTools'
  Console. The time is shown in the evidence panel as "The recorded font
  faces were added and loaded".
- Requests are answered by resource type:
  - the page's own address, for a `Document` request, with the page;
  - an `Image` request, with the image's latest recorded status, MIME
    type, and bytes;
  - a `Fetch` or `XHR` request at the font address, for a digest one of
    the faces names, with the file's bytes and
    `Access-Control-Allow-Origin: *`. The instrumented Chromium on the
    target machine reports the builder's `fetch()` as `XHR` in
    `Fetch.requestPaused`; the stock Chromium of the integration tests
    reports it as `Fetch`.
    The page's origin is not the recorder's, so the builder's read is a
    cross-origin request.
  - Anything else is refused, as before.
  An answer is made off the thread that reads the DevTools connection,
  since it may read the recording file.
- The recorded page's content security policy allows `connect-src` for the
  font address only. It allows `img-src` for any http or https address, in
  addition to `'self'` and `data:`, since the recorder answers or refuses
  every request of the tab and nothing reaches the network. Without it, the
  policy would stop an image of another origin before the recorder could
  answer it.
- In Blink, a text item takes its recorded glyphs when the font Blink chose
  for it has the recorded size and the recorded font file's digest and
  collection index. This replaces the PostScript name comparison of stage 3.
  The recreation mode's bridge digests each typeface's file once, as the
  recording does (`RecordFontFile` keeps the digest and records nothing
  when there is no recorder connection in the recreation mode). The
  integration script upgrades the stage 3 helper to this one.

Found while building:

- A web font's recorded file is the file Blink decoded: the output of the
  OpenType Sanitizer, after WOFF2 decompression. The recreation's face is
  sanitized again when it loads, so its digest matches the recorded one
  only if sanitizing is idempotent for the file. The 8 web font files of
  recording 20261002-162024 were run through the sanitizer of the Python
  package `opentype-sanitizer` 9.2.0 in this environment. All 8 came out
  byte for byte unchanged, with the same digest. The 3 installed font
  files, which Blink does not sanitize, changed. That package's sanitizer
  version is not necessarily Chromium's, so the target machine is the
  check.

Differences from the design above:

- A face whose font file is not the first of a collection is not added,
  since a `FontFace` made from bytes takes the first font. A face from a
  `local()` source is not added, since its local name is not recorded.
  The evidence panel's notes count both, with the faces that had not
  loaded at the frame and the images recorded without their bytes.
- Images and font files are matched across the whole recording, not only
  within the document, since their records name no document (sub-step 2).
- An animated image is answered with its recorded bytes and animates in
  the recreation, from its first frame. This does not meet the
  requirement: the image must show the frame drawn at the recorded frame.
  See "Requirement: the page exactly as drawn at the frame".

Tests at this sub-step:

- Unit: the faces and images chosen for a frame, from a recording file
  (removed faces, another document's faces, faces not loaded, faces added
  after the frame, an image found by its response's URL, and an image
  without bytes); bytes that do not match their digest; the answers by
  resource type and the policy; the faces in the builder's data; the
  integration script's helper, its digest comparison, and its upgrade from
  the stage 3 helper.
- Integration, run in this environment with a stock headless Chromium:
  `TheRecordedImageAndFontFaceAreUsed`. A page served at its recorded
  address draws a recorded image, and refuses an image that was not
  recorded. With `RECORDER_RECREATION_FONT_FILE` naming a font file (here
  DejaVu Sans), the face is loaded before the tree is built. The glyph
  comparison needs the instrumented Chromium, and is checked on the target
  machine.

#### Sub-step 3 on the target machine

Recording 20261002-162024, on the CNIB events page at revision e12b61b,
reported by the owner 2026-10-02: the images are drawn, and the web fonts
are not; the headings, in Stag Sans Web, are drawn in a fallback font.

Read in the open recreation through its DevTools port: `document.fonts`
was empty, and the builder's notes held "not added: Failed to fetch" for
each of the five faces. Each font request was refused by the recorder
(`net::ERR_BLOCKED_BY_CLIENT`), and `Fetch.requestPaused` gave its
resource type as `XHR`, not the `Fetch` the server required. The same
recording's five faces were read and answered in the development
environment with the type `Fetch`. Revision after e12b61b answers either
type at the font address.

With the fonts answered (revision cf9fa2d), the owner reported the page
as close to the recording when nothing animates and no select is open,
with one difference: the text "Reset" of a button shown only while the
preferences panel is open is drawn over the "Show" of the "Show
Preferences" button.

The button (`button#reset`) has `style="display: none;"` at the frame.
Read from the recording in the development environment, at each frame of
the two documents of the page: the button and its `span` have a latest
layout record stating no layout object (change sets 53 and 120), and its
text node "Reset" has a latest record stating one (change sets 44 and
118), from before the button was hidden. The recreation therefore gave
the button `display: contents`, as an element with no layout object over
a node that had one ("Elements without a layout object"), and drew its
text.

The recording is at fault, not the inference: a node's change record is
written only when the node is noted, and a text node was noted only when
its layout object's style was set, not when its layout object was
destroyed. When the button became `display: none`, Blink destroyed the
text node's layout object in `Node::DetachLayoutTree`
(`third_party/blink/renderer/core/dom/node.cc`), and nothing noted it.
From the revision after cf9fa2d, `Node::DetachLayoutTree` notes every
element and text node whose layout object it destroys, so the next
change set records it with no layout object. A recording made before it
keeps the stale record.

That revision (00c20cc) was not enough. In a recording made with it on
the target machine (20261002-194407), the text node "Reset" still had a
latest record stating a layout object, at each frame of three documents
of the site. Noting the node was not the fault: the change set left out
any noted text node without a layout object, as the checkpoint does, and
the bridge refused a text node change record without one. A checkpoint
is a full walk, so a text node it leaves out has no layout object; a
change set states only what changed, so leaving the node out kept its
earlier record. Protocol 0.41 records, in a change set, a noted text node
without a layout object, stating that it has none; the bridge and the
recorder's validation accept such a record for a change set and still
refuse it in a checkpoint.

#### Required tests

- Unit tests: the font-file, font-face, and image records against the
  record contract, including the once-per-digest rule; the resources the
  app chooses for a frame (last image record for a URL at or before the
  frame, faces loaded and not removed); the answer chosen for a paused
  request; the tree data's recorded node; the integration script's new
  hooks and their upgrade from those of stage 3.
- Integration tests in the instrumented Chromium: a generated page with a
  web font and an image is recorded, and its records hold the font file
  and image bytes with matching digests; the recreation of that page draws
  its text with the recorded glyphs and the recorded font, and its image
  from the recorded bytes; a box whose child count differs keeps the
  recorded offsets of the children that match; no request of the
  recreation reaches the network.
- System test on the target machine: a recording of a page with web fonts
  and images is inspected, and the Console's list of boxes not imposed is
  compared with that of stage 3.

### Slice 4b: the frame's moment, held (proposed)

Proposed 2026-10-02, after the owner agreed to start the work in
"Requirement: the page exactly as drawn at the frame" with time held in
the recreation and the animation state recorded. Revised 2026-10-03, at
the owner's request, after slice 4d: the protocol is renumbered (0.45 to
0.47 were taken by slice 4d's defects), popup widgets are included, and the
two questions it left open are settled from the Chromium source. Nothing
below is built.

#### What Chromium does that the recording misses

Read in the Chromium checkout on the target machine.

- An animation running on the compositor is not ticked on Blink's main
  thread at each frame. `Animation::TimeToEffectChange`
  (`third_party/blink/renderer/core/animation/animation.cc`) returns zero,
  which asks for service at the next frame, only for an animation that is
  not on the compositor (`!HasActiveAnimationsOnCompositor()`). For one on
  the compositor, it returns the time to its next change of phase. The
  style the layout walk reads for an element so animated is therefore not
  the value drawn while the animation runs.
- The compositor ticks its animations at each begin frame, in
  `LayerTreeHostImpl::AnimateInternal` (`cc/trees/layer_tree_host_impl.cc`),
  at the begin frame's time, on the active tree
  (`AnimateLayers(monotonic_time, /* is_active_tree */ true)`). Each value
  reaches the trees through `cc::ElementAnimations`
  (`cc/animation/element_animations.cc`): `OnTransformAnimated`,
  `OnOpacityAnimated`, `OnFilterAnimated`, `OnBackdropFilterAnimated`, and
  `OnScrollOffsetAnimated`, each for the active list, the pending list, or
  both.
- A scroll the compositor handles itself (a wheel or touch scroll, or a
  smooth scroll) changes the active tree's scroll offset before the main
  thread hears of it.
- A background color or clip path animation run as a native paint
  worklet is not a property of the trees. Its animation gives only a
  progress value, kept for the pending tree alone
  (`ElementAnimations::OnFloatAnimated`, `NATIVE_PROPERTY`: "only
  dispatched from the pending tree"), and passed to the paint worklet
  through `LayerTreeHostImpl::OnCustomPropertyMutated`. The drawn color is
  interpolated from the progress when the worklet paints, in
  `BackgroundColorPaintDefinition::Paint`
  (`third_party/blink/renderer/modules/csspaint/nativepaint/background_color_paint_definition.cc`,
  `Sample`), and the clip path likewise in
  `clip_path_paint_definition.cc`.
- An animated image's frame is chosen by the compositor, not by Blink.
  `cc::ImageAnimationController` (`cc/trees/image_animation_controller.h`)
  advances each animated image when a sync tree is made
  (`AnimateForSyncTree`), keeps the frame for that tree's lifetime, makes
  it the active tree's at `DidActivate`, and gives it by `PaintImage::Id`
  and tree (`GetFrameIndexForImage`). Blink makes the image's `PaintImage`
  in `BitmapImage::CreatePaintImage`
  (`third_party/blink/renderer/platform/graphics/bitmap_image.cc`).
- The frame drawn is the active tree as it is at
  `LayerTreeHostImpl::DrawLayers`, after the begin frame's animations and
  any activation. `DrawLayers` makes the compositor frame
  (`GenerateCompositorFrame`), takes its frame token, and submits it; a
  draw with no damage submits nothing. Viz reports the frame's
  presentation by that token, to `LayerTreeHostImpl::DidPresentCompositorFrame`.
- A frame the compositor draws for these changes alone has no rendering
  update on the main thread, so it has no presentation record: the
  presentation records follow rendering updates (protocol 0.35), and each
  names the compositor frame token of the frame that carried its update.

So at a captured frame, the recording holds the main thread's state after
its last presented rendering update, which already includes the values of
animations Blink ticks on the main thread (subject to the check in
"Required tests" below), but not the compositor's values drawn after it.

#### Settled from the source

- Where a compositor frame is recorded: at frame submission, in
  `DrawLayers`, with the token of the frame submitted. Activation is not
  enough: the begin frame's animations are applied to the active tree
  after it, and a frame drawn without a new tree has no activation.
- How a paint worklet animation is recorded: the drawn value where the
  worklet paints it. `Paint` records the element, the progress it was
  given, and the value it drew (the color as four floats; the clip path as
  the path it drew). The compositor frame records the progress of each
  paint worklet property of the tree it drew, so the drawn value of a
  frame is the one painted from the same element and progress. Recording
  the worklet's input and interpolating in the app is not taken: it would
  repeat Blink's interpolation outside Blink.

#### What is recorded (protocol 0.48)

On a new topic, `browser.compositor`, for each compositor: the tab's
widget, each page popup's widget (named by its frame sink, as in slice 4d
sub-step 1b), and later each out of process iframe's.

| Record | Where | Holds |
| --- | --- | --- |
| `compositor-animation-started` | Blink, main thread, when an animation starts on the compositor | the document, the target node, the compositor element ID and namespace, the animated properties, and the compositor animation ID |
| `compositor-animation-ended` | Blink, main thread, when it is cancelled or finishes there | the compositor animation ID |
| `compositor-frame` | cc, compositor thread, in `DrawLayers`, for each submitted frame in which an animated value, a compositor scroll offset, a paint worklet progress, or an image's frame changed on the active tree since the last recorded frame | the frame sink and frame token, the begin frame's time, and each change: element ID, property, and value |
| `compositor-frame-presented` | cc, at `DidPresentCompositorFrame`, for a recorded frame | the frame sink, the frame token, and the presentation time, on the clock of the existing presentation records, or the failure flag |
| `paint-worklet-painted` | Blink, in a native paint definition's `Paint`, on the worklet's thread | the element ID, the property (background color or clip path), the progress given, and the value drawn |
| `image-paint-image` | Blink, when an image resource's Blink image gets its paint image ID | the image's URL and digest (as `image-resource`) and its `PaintImage::Id` |

Values are written as the compositor holds them: a transform as its 16
matrix entries, an opacity as a number, filters as their operations and
numbers, a scroll offset as x and y, an image's frame as its index, a
paint worklet's progress as a number, and a painted color as its four
floats. Nothing is rounded. A frame with no change is not recorded, so a
page with nothing moving adds no records.

#### Which state a captured frame shows

For each document, the frame shows the main thread's state after the
last presented rendering update at or before the frame's composition, as
now, and then the compositor's values of the last `compositor-frame` of
the same frame sink presented at or before the composition, together with
every earlier compositor frame's values not yet replaced. A value is
dropped when its animation ends and a later rendering update was
presented. A popup takes its own frame sink's compositor frames. The basis
line in the evidence panel names both the rendering update and the
compositor frame.

#### What the recreation does

- Nothing moves. In the recreation mode Blink starts no CSS animation or
  transition: the recorded computed style holds the animation and
  transition properties, and they are not run. No page script runs, so no
  Web Animation is made. SVG animation elements are not run either; the
  recorded style and box fragments hold their effect on style and
  geometry, and whether they hold all of it (an animated `transform`
  attribute, for one) is checked in the required tests. The text caret,
  video, and other transient states are not part of this slice; see the
  requirement's list.
- An element with a recorded compositor value at the frame takes it in
  place of the recorded style value of that property: the transform as a
  `matrix3d()` of the recorded entries, the opacity, the filter, or the
  scroll offset. An element with a paint worklet value takes the painted
  value: the background color as the recorded color, the clip path as the
  recorded path.
- An animated image is held at its recorded frame. The recorder answers
  the image with its bytes and, in a response header of its own, the frame
  index at the frame. In the recreation mode Blink puts the index in the
  bridge by the image's paint image ID, and `ImageAnimationController`
  gives that index for the image and never advances it. An image whose
  frame was not recorded is held at its first frame, and the Console says
  so.
- The evidence panel names, for each imposed value, the compositor frame
  it came from and its presentation time.

#### Limits

- An animation that is not on the compositor and does not cause a
  rendering update at each frame would be missed. Whether one exists is
  checked in the required tests.
- A frame sink other than the tab's and the popups', such as an out of
  process iframe's, is recorded the same way but not shown until iframes
  are recreated.
- The window fade of a popup is the Windows compositor's, not Chromium's,
  and stays as "Window fade of a popup" states.
- `compositor-frame` and `paint-worklet-painted` are written off the main
  thread; the bridge's send from those threads is checked when building,
  as the records must not wait on the main thread.

#### To be settled

- The cost on the target machine, measured as for stage 2, on a page with
  a running composited animation, a background color animation, and an
  animated image.

#### Sub-steps

1. Record (protocol 0.48), with its cost measured on the target machine.
2. The recreation holds time: no animation or transition run, compositor
   and paint worklet values imposed, animated images held at their
   recorded frame.

Each sub-step is tested on the target machine before the next.

#### Required tests

- Unit tests: each new record against the record contract; the values the
  app chooses for a frame from a sequence of compositor frames and their
  presentations, including a popup's frame sink and a paint worklet value
  joined by element and progress; the image frame header; the integration
  script's hooks and their call shapes against the bridge.
- Integration tests in the instrumented Chromium, on a generated page with
  a composited transform animation, a background color animation, a
  main-thread animation of a non-composited property (width), a smooth
  scroll, and an animated image: the main-thread animation causes a
  recorded rendering update at each frame it changes; the recording holds
  the compositor values, paint worklet values, scroll offsets, and image
  frames, joined to their presentations; the recreation at a chosen frame
  imposes the values recorded for it; and two screenshots of the
  recreation taken a second apart are identical. The page also has an SVG
  `animateTransform`, to check that its effect is held.
- System test on the target machine: a recording of a page with running
  animations and an animated image is opened at several frames, and each
  recreation is compared with the captured frame.

### Slice 4c: the DOM from the start of parsing (agreed, built)

Proposed and agreed 2026-10-02.

#### Found on the target machine

In the recording 20261002-204153, the document of
`https://www.cnib.ca/en/event` was first recorded at 19.742 s. Its first
DOM walk is the finished-parsing walk, at 22.630 s, of 6900 nodes. Read in
steps of 5 ms, the frames from 20.170 s to 23.745 s take as their basis a
presentation of the document's state at 20.072 s to 22.305 s (ten
presentations), all before that walk; the next presentation, of its state
at 23.722 s, is the basis from 23.750 s. Its layout records begin at
20.350 s. At those frames the player says the page cannot be recreated.
The owner opened the frame at 23.026 s and received that message. This is
the gap found on 2026-09-29 ("Found while building" under "Slice 3b
implementation"), and it breaks the requirement at the top of this
document: the page was drawn, so it must be recreated.

#### Why it happens

The DOM is recorded only from the finished-parsing walk:
`RecorderRecordsDomChanges` in the integration script is false while
`Document::Parsing()`, so no structural change made during parsing is
recorded, and a parser change to character data is recorded only once the
document is no longer parsing ("Insertions and removals" in
[change-driven recording](change-driven-recording.md)). The mutation
delivery hook also queues a post-mutation checkpoint only for a document
that `HasFinishedParsing()`. Layout and presentation are recorded from the
document's first rendering update, which Blink makes while it parses, so
the page is drawn, and its layout recorded, before its DOM is.

#### What is recorded (protocol 0.42)

Read against Chromium's main branch at 65f3c73 (2026-10-02); line numbers
in the checkout on the target machine may differ.

- A DOM walk when parsing starts. `Document::ImplicitOpen` creates the
  parser and sets the parsing state to `kParsing`
  ([document.cc](https://source.chromium.org/chromium/chromium/src/+/65f3c73180c4fb3d4960843c373be998b0aba64a:third_party/blink/renderer/core/dom/document.cc;l=4131)). A hook after that call
  requests a DOM checkpoint with `reason` `started-parsing`, which the
  bridge always walks, as it walks `finished-parsing`. For a navigation the
  document then holds no children, so the walk records the document node
  alone; for `document.open()` it records what the document holds.
- Every change made while the document parses, as after parsing. The
  parser's insertions reach the existing hook:
  `ContainerNode::ParserAppendChild` and `ParserInsertBefore` call
  `NotifyNodeInserted` with `ChildrenChangeSource::kParser`
  ([container_node.cc](https://source.chromium.org/chromium/chromium/src/+/65f3c73180c4fb3d4960843c373be998b0aba64a:third_party/blink/renderer/core/dom/container_node.cc;l=1282),
  [line 683](https://source.chromium.org/chromium/chromium/src/+/65f3c73180c4fb3d4960843c373be998b0aba64a:third_party/blink/renderer/core/dom/container_node.cc;l=683)), `ParserRemoveChild` calls
  `ChildrenChanged` with a removal
  ([line 1111](https://source.chromium.org/chromium/chromium/src/+/65f3c73180c4fb3d4960843c373be998b0aba64a:third_party/blink/renderer/core/dom/container_node.cc;l=1111)), and `ChildrenChanged`
  calls `Document::NotifyChangeChildren`
  ([line 1474](https://source.chromium.org/chromium/chromium/src/+/65f3c73180c4fb3d4960843c373be998b0aba64a:third_party/blink/renderer/core/dom/container_node.cc;l=1474)), where the recorder's hook
  is. `RecorderRecordsDomChanges` drops its `!Parsing()` condition, so they
  are recorded as `dom-node-inserted` and `dom-node-removed` with the
  inserted subtree, as now. A node the parser builds outside the document,
  such as a fragment, is still recorded when it is inserted.
- Parser text appended to a connected node during parsing.
  `CharacterData::ParserAppendData` sets the whole new value with
  `kUpdateFromParser`
  ([character_data.cc](https://source.chromium.org/chromium/chromium/src/+/65f3c73180c4fb3d4960843c373be998b0aba64a:third_party/blink/renderer/core/dom/character_data.cc;l=77)). The character data
  hook records it as `dom-character-data-changed` while the document parses
  too; a node that is not connected is still skipped.
- Post-mutation checkpoints while parsing. The mutation delivery hook
  queues the document while it parses, so the transitions of a parse are
  closed by checkpoints as later ones are. The document already has a walk,
  so the bridge does not walk them, except after a loss or at the check
  interval, as now.
- The finished-parsing walk stays and is always walked. When the document
  has a `started-parsing` walk, the app's check compares it with the state
  rebuilt from that walk and the changes after it, which it does not do
  now (`DomTreeRebuilder` takes a finished-parsing checkpoint as the state
  without comparing it). A difference is reported as for any other walk.

The recorder's validation accepts `started-parsing` as a DOM checkpoint
`reason`. The state reader needs no new rule: the state at a frame is
rebuilt from the latest walk at or before it and the changes after it, and
the `started-parsing` walk is the earliest.

#### Limits

- Each parser append records the whole value of the text node, so a text
  node the parser appends to n times is recorded n times. Whether that
  matters on real pages is measured on the target machine.
- The records made during parsing add main-thread work while the page
  loads, about one record set for each parsed node in addition to the
  finished-parsing walk. The cost is measured as for stage 2; optimizing it
  stays deferred, as agreed.
- A frame drawn before the document's `started-parsing` walk (none is
  expected for a document that is parsed) still cannot be recreated, and
  the player still says so.
- Fonts and images are taken as they were at the frame's basis: an
  image's latest image-resource record at or before it, and the faces the
  document had loaded and not removed. An image Blink drew is decoded from
  loaded bytes, so an image the screen shows should have its record by
  then. Two cases may not hold, and are settled below rather than
  accepted.

#### To be settled

- Whether `ImplicitOpen` is reached for every document that is parsed,
  including the XML parser and the initial empty document, or whether the
  walk is better made at the document's first change. Settled by reading
  the source on the target machine before the hook is written.
- Whether the document's token and its navigation's correlation are
  available at `ImplicitOpen`, so the walk is joined to the committed
  navigation as the finished-parsing walk is.
- An image drawn while its bytes are still arriving. Blink can decode and
  draw part of an image before it finishes loading; the recording holds
  an image only once it has finished, so the recreation would draw
  nothing where the screen shows part of it. Settled by reading how Blink
  paints a partly loaded image, and what of it the recording would need.
- Text whose web font has not loaded. During a face's block or swap
  period Blink draws the text invisible or in a fallback font. Whether the
  recorded glyph runs, imposed in the recreation, give what the screen
  showed is settled by reading Blink's font loading and checked on the
  target machine.

Agreed by the owner on 2026-10-02, with these two cases added and the
recreation compared with the screen image in the system test.

#### Required tests

- Unit tests: the integration script's new hook and changed conditions,
  and the upgrade of each changed definition from the one a checkout at
  protocol 0.41 holds; the bridge's walk schedule for `started-parsing`
  and for post-mutation requests during parsing; the validation of the new
  reason; the state at a time between a `started-parsing` walk and the
  finished-parsing walk, rebuilt from parser insertions, removals, and
  appends; the check of a finished-parsing walk against that state.
- Integration test in the instrumented Chromium: a generated page served
  slowly in parts, so that it is presented before parsing finishes; at a
  presentation before the finished-parsing walk, the rebuilt DOM equals
  the DOM Blink held, and the check of the finished-parsing walk reports
  no difference.
- System test on the target machine: the recording of the CNIB events
  page is opened at frames between the document's first presentation and
  its finished-parsing walk, and the finished-parsing check reports no
  difference. Each recreation is compared with the captured screen image
  of its frame, including which images are drawn, and how far, and the
  font of each heading; a difference is reported as a defect, not
  accepted.

#### Settled before building

- `ImplicitOpen` is reached for every parsed document. On the target
  machine's checkout, `DocumentLoader::CreateParserPostCommit` calls
  `Document::OpenForNavigation` (`core/loader/document_loader.cc`, line
  3535), which calls `ImplicitOpen` (`core/dom/document.cc`, lines 4069 to
  4105); `document.open()` calls it too
  ([document.cc](https://source.chromium.org/chromium/chromium/src/+/65f3c73180c4fb3d4960843c373be998b0aba64a:third_party/blink/renderer/core/dom/document.cc;l=4047)). `ImplicitOpen` creates the
  parser with `Document::CreateParser`, an HTML parser for an HTML
  document and an XML parser otherwise
  ([document.cc](https://source.chromium.org/chromium/chromium/src/+/65f3c73180c4fb3d4960843c373be998b0aba64a:third_party/blink/renderer/core/dom/document.cc;l=3697)), so both start with the walk.
- The parser is created after the navigation commits:
  `DocumentLoader::StartLoadingResponse` checks that the loader's state is
  at least `kCommitted` before it calls `CreateParserPostCommit`
  (`core/loader/document_loader.cc`, lines 2189 to 2215 on the target
  machine's checkout). The walk names the document's token, as every DOM
  checkpoint does; whether the app joins it to its navigation is checked
  on the target machine.
- The exception: with `kStreamlineRendererInit`, the initial empty
  document of a main frame is given its `html`, `head`, and `body`
  elements without a parser (the same function, lines 2197 to 2209). It
  has no `started-parsing` walk; its first change is walked as `first` at
  the next mutation delivery, as before.
- `Document::Parsing()` is true only in the state `kParsing`, and
  `HasFinishedParsing()` only in `kFinishedParsing`; the state between
  them is `kInDOMContentLoaded` (`core/dom/document.h`, lines 1142 to 1145
  on the target machine's checkout). The mutation hooks no longer test
  either.

#### As built (protocol 0.42)

- `patch_blink_document_started_parsing` in the integration script adds
  the walk after `SetParsingState(kParsing)` in `ImplicitOpen`, for an
  active document, with a declaration of the walk before `ImplicitOpen`.
- `RecorderRecordsDomChanges` is true for an active document while the
  recorder is connected, in every parsing state; the character data hook
  skips a parser update only for a node that is not connected; the
  document's change hook queues a mutation delivery in every parsing
  state, and the delivery skips only an inactive document. A checkout
  patched at protocol 0.41 is upgraded by replacing each changed condition.
- `FullWalkSchedule` always walks `started-parsing`, as `finished-parsing`,
  and names the request as its `walkReason` when it is not the document's
  first walk, after a loss, or a check.
- The recorder accepts `started-parsing` as the reason of a DOM checkpoint
  and of an interaction checkpoint that follows one, and as a `walkReason`
  only for that request. A document walked when its parser was created is
  complete while it parses (`ParserChangesRecorded`), where before it was
  marked as parsing, with nodes missing. `DomChangeCheck` compares a
  finished-parsing checkpoint with the tree rebuilt from a
  `started-parsing` walk and the changes after it.
- Not yet done: the integration test in the instrumented Chromium, which
  needs a Chromium build; it is replaced, for this sub-step, by the system
  test on the target machine and by the change check run on its recording.

#### On the target machine (c5bc790)

The recording of 2026-10-03 02:35 UTC holds 35 `started-parsing` walks,
and the change check compared each of the 35 finished-parsing walks with
the tree rebuilt from its document's `started-parsing` walk: 13,642 nodes
compared, all equal in every field. The CNIB events page has a complete
DOM at every frame from 15.2 seconds, its first walk at 15.182 seconds.
The owner reported that the recreated frames look correct, except those
with a running animation or an open select, which are drawn without them
(slices 4b and 4d).

### Slice 4d: open select lists and other page popups (agreed)

Proposed 2026-10-02, after the owner's report that frames with an open
select show it closed, and agreed the same day. Slice 4d is built before slice 4b, so it takes
protocol 0.43 and slice 4b moves to protocol 0.44. Sub-step 1b, agreed on
2026-10-03, takes protocol 0.44, and slice 4b moves to protocol 0.45.

#### What Chromium does

Read in the Chromium checkout on the target machine; line numbers are
those of that checkout, under `third_party/blink/renderer/`.

- A select drawn as a menu list opens its list in a page popup, not in
  its own document. `ChromeClientImpl::OpenPopupMenu` makes an
  `InternalPopupMenu` unless external popup menus are used
  (`core/page/chrome_client_impl.cc`, lines 1022 to 1031), and
  `InternalPopupMenu::Show` opens a page popup for it
  (`core/html/forms/internal_popup_menu.cc`, lines 711 to 714). Date,
  time, and colour pickers are page popups too.
- The page popup is a page of its own, with its own frame and document,
  and its own widget. `WebPagePopupImpl` makes its frame (line 431 of
  `core/exported/web_page_popup_impl.cc`), has its client write the
  document (line 473), and installs it synchronously (line 475). Its
  compositing is made on its own `WidgetBase`, with no frame widget input
  handler (lines 508 to 522).
- `InternalPopupMenu::WriteDocument` writes the list as a script
  configuration: the options and their labels and styles, the selected
  index, the select's base style, the anchor rectangle in screen
  coordinates from the select's visible bounds in its local root, the zoom
  factor, and the scale factor (`internal_popup_menu.cc`, from line 323).
  The list itself is a listbox `select` of size 20 made by
  `list_picker.js` (`core/html/forms/resources/list_picker.js`, from line
  61).
- The popup places its own window: `PagePopupController::setWindowRect`
  (`core/page/page_popup_controller.cc`, line 120) reaches
  `WebPagePopupImpl::SetWindowRect`, which sets the widget's pending window
  rectangle and asks the browser for the popup's bounds (lines 688 to
  721). The rectangle may extend beyond the owner's window.
- The highlighted item is the selected option of the popup's listbox.
  Pointer hover sets `selected` on an option (`list_picker.js`, lines 160
  to 162 and 231 to 239), and the arrow keys move the listbox's selection; a change is
  sent to the owner with `setValue`, which calls
  `HTMLSelectElement::ProvisionalSelectionChanged`
  (`internal_popup_menu.cc`, lines 672 to 678). Every change of an
  option's selectedness, in the popup or in the page, passes through
  `HTMLOptionElement::SetSelectedState`
  (`core/html/forms/html_option_element.cc`, line 386), which sets no
  attribute, so no DOM transition records it.
- The popup is closed by `WebPagePopupImpl::ClosePopup` (line 1101), from
  the renderer, or by `Close` (line 1069), from the browser; the owner is
  told by `InternalPopupMenu::DidClosePopup` (`internal_popup_menu.cc`,
  lines 680 to 685).

#### What the recording already holds

From the recording of 2026-10-03 02:35 UTC on the target machine, read in
the sandbox: four popup documents, at 20.861, 22.439, 23.099, and 40.954
seconds. Each has its DOM from a `started-parsing` walk, including the
configuration above (for the list at 23.099 seconds: 15 options, selected
index 0, anchor rectangle x 516, y 471, width 262, height 48, zoom and
scale factor 1), its listeners, its input dispatches, and its focus. The
popup open from 23.093 to 25.698 seconds also has a layout checkpoint and
13 layout change sets. Every presentation request of a popup document is
recorded as `no-widget`: the presentation request looks for the frame
widget of the frame's local root (`RecorderRequestLayoutPresentation` in
`chromium/integrate.py`), and a popup has none.

So the recording holds what the popup drew, but not which select owns
it, where its window was, which option was highlighted after it opened,
or which captured frame shows which state.

#### What is recorded (protocol 0.43)

On `browser.interaction`:

| Record | Where | Holds |
| --- | --- | --- |
| `page-popup-opened` | `WebPagePopupImpl`'s constructor, after the popup document is installed and its first window rectangle is set | the popup's document identity, its kind (`select-list`, `date-time`, `color`, or `other`, from the owner element), the owner element's node ID and its document's identity, the owner's visible bounds in its local root, the owner's local root view and the anchor rectangle in screen coordinates as `WebPagePopupImpl` computes them, the first window rectangle, and the zoom factor |
| `page-popup-window-rect` | `WebPagePopupImpl::SetWindowRect`, at each call, and `WebPagePopupImpl::SetScreenRects`, at each call | the popup, and either the window rectangle the popup asked for (`requested`, after the emulation is reversed, and whether it was deferred until the popup was shown) or the widget and window rectangles the browser placed it at (`placed`) |
| `page-popup-closed` | `WebPagePopupImpl::ClosePopup`, or `WebPagePopupImpl::Close` when its cancel did not reach `ClosePopup` | the popup, and whether the renderer or the browser closed it |
| `option-selectedness-changed` | `HTMLOptionElement::SetSelectedState`, when the state changes | the option's node ID, its select's node ID when it has one, and the new state, for every document, popup or page |

All rectangles are in screen DIPs, as `WebPagePopupImpl` holds them.

The selectedness record also covers a closed select in the page, whose
drawn text is its selected option's label, and a listbox select in the
page.

On `browser.presentation`: a layout checkpoint or change set of a popup
document requests its presentation from the popup's own `WidgetBase`
layer tree host, with a swap promise that records as a frame widget's
does, so the popup's frames are joined to captured frames as the page's
are. The widget identity names the popup's frame and has the new
`widgetKind` `page-popup`; a frame widget's has `frame`. Its frame sink ID
is null: the browser assigns a popup's frame sink, and the renderer is not
told it (`WebPagePopupImpl` and `WidgetBase` hold no frame sink ID). The
join to captured frames uses the request identity and presentation time,
not the frame sink. A popup's widget is not a frame widget, so its request records
`isMainFrameWidget` false.

#### Which state a captured frame shows

A popup is open at a captured frame when its `page-popup-opened` record is
at or before the frame's composition and no `page-popup-closed` record
is. Its state is chosen as a document's state is now: the last presented
rendering update of the popup's widget at or before the composition. Its
window is the last `page-popup-window-rect` at or before that update.

#### What the recreation does

- The page is recreated as now, with each option's recorded selectedness
  imposed.
- An open popup is drawn over the recreated page at its recorded window
  rectangle, mapped into the recreation's coordinates by the owner's
  recorded visible bounds and anchor rectangle. Its document is built
  from the recorded DOM with recorded values imposed, as a page's is, and
  its scripts are not run, so nothing in it changes.
- The basis line in the evidence panel names the popup and its rendering
  update.

#### Limits

- A select drawn with `appearance: base-select` puts its list in the
  page's own top layer, not in a page popup. Whether its open state is
  recorded is checked in the required tests.
- With external popup menus (macOS, and Android), the list is drawn by
  the platform; not covered. The recorder runs on Windows.
- Part of a popup window outside the captured screen area cannot be
  compared with the capture.

#### To be settled

- How the popup is drawn in the recreation: as a page popup of the
  recreation's own, opened at the mapped rectangle, which draws it with
  the same code as at recording, or as a layer of the recreated page,
  which keeps the whole frame in one document that DevTools can inspect.
- Whether the date, time, and colour pickers need anything beyond the
  records above.

#### Settled before building

Read in the same checkout, 2026-10-02.

- The presentation request reaches the popup's widget through its page.
  `ChromeClient::IsPopup` (`core/page/chrome_client.h`, line 137) is
  overridden in core only by `PagePopupChromeClient`
  (`core/exported/web_page_popup_impl.cc`, line 203), which holds its
  `WebPagePopupImpl` until `ChromeDestroyed` clears it. A function in
  `web_page_popup_impl.cc` checks `IsPopup` on the frame's page, reaches
  the `WebPagePopupImpl` through that client, and queues the swap promise
  on its `WidgetBase` layer tree host; the request in
  `RecorderRequestLayoutPresentation` calls it before recording
  `no-widget`. A popup that is closing, or whose widget has no layer tree
  host, is recorded as `no-widget` or `not-compositing` as a frame is.
- `WebPagePopupImpl` is reference counted rather than garbage collected,
  so the swap promise reaches the widget for presentation feedback by a
  weak pointer to the `WidgetBase` (`platform/widget/widget_base.h`, line
  395), used only on the main thread.
- The popup's first window rectangle is set while the document is
  installed, before the popup is shown, and is kept as `initial_rect_`
  (lines 713 to 721); `page-popup-window-rect` records it with
  `deferred` true, and `page-popup-opened` repeats it.
- `Close` from the browser normally reaches `ClosePopup` through the
  popup client's cancel (lines 1069 to 1099); when it does not, `Close`
  destroys the page itself, and the closed record is written there.

#### Sub-steps

1. Record (protocol 0.43): the four records and the popup's
   presentations, with the cost measured on the target machine.
1b. Record (protocol 0.44): the popup window's bounds as the browser set
   them, and its frame sink, from the browser process.
2. The recreation imposes option selectedness and draws an open popup at
   its recorded place.

Each sub-step is tested on the target machine before the next.

The order and scope agreed by the owner on 2026-10-03 ("Yes, I agree let's
follow that plan"): sub-step 1b, then sub-step 2, then slice 4b, with 4b
designed for page widgets and popup widgets alike. Anything else that the
comparison of sub-step 2 with the captured screen finds is recorded here as
a defect against that comparison, not added as a further step, unless it
blocks slice 4b.

#### Sub-step 1 on the target machine

Built as 76c4812 (2a3939b, with the owner's input type read through
`TextControlElement`, since `HTMLInputElement` declares
`FormControlTypeAsString` private). The owner recorded the CNIB events page
on 2026-10-03, recording `20261003-130512-129cb663dbd8408b9797344a44fc4a81`
(113.5 s, 192,866 events accepted, none dropped), opening its selects and
moving the highlight. What the recording holds:

- Ten popups, each a `select-list` owned by one page document, each with
  one `page-popup-opened`, two `page-popup-window-rect` records with
  `source` `requested` (the first `deferred`, the second not, with the same
  rectangle), and one `page-popup-closed`. All ten were closed by the
  `renderer`. No `placed` rectangle was recorded (see below).
- 193 `option-selectedness-changed` records: 154 in the ten popup
  documents, each with the popup listbox's select, written by the picker's
  script (`ListPicker`, `update_`, `highlightOption_`), and the rest in page
  documents, including the owner select's change when the list closed with
  a new value (`handleMouseUp_`). Fifteen page records state no select:
  the option was not in a select when its selectedness changed, four with
  no script location and eleven set by the page's script.
- Presentations of the popup documents: 98 `presentation-requested`, all
  queued, with `widgetKind` `page-popup`, a null `frameSinkId`, and
  `isMainFrameWidget` false; 96 swapped with feedback, and 2 not swapped.
  Four requests in page documents still record `no-widget`.

No placed rectangle reaches a popup. `WidgetBase::SetPendingWindowRect`
(`platform/widget/widget_base.cc`, lines 1971 to 1976) stores a popup's
requested rectangle as its widget and window rectangles, with the comment
"Popups don't get size updates back from the browser so just store the set
values". `WidgetBase::UpdateScreenRects` (lines 571 to 580) sets the
rectangles itself when its client does not handle them, and
`WebPagePopupImpl::SetScreenRects` is not called on that path. So the
renderer's own account of a popup's place is the requested rectangle; where
the browser put the popup's window on screen is not in the renderer.
Whether it differs from the request is checked against the captured screen
images in sub-step 2.

The bridge's cost lines in the Chromium log time each new call on the
renderer's main thread (count, mean, largest):

| Call | Count | Mean | Largest |
| --- | --- | --- | --- |
| `RecordBlinkPagePopupOpened` | 10 | 23.0 us | 32 us |
| `RecordBlinkPagePopupWindowRect` | 20 | 13.2 us | 21 us |
| `RecordBlinkPagePopupClosed` | 10 | 10.6 us | 19 us |
| `RecordBlinkOptionSelectednessChanged` | 193 | 18.3 us | 124 us |
| `BeginBlinkPresentationRequest`, all widgets | 409 | 14.2 us | 89 us |

The presentation request count in the cost lines (409) is below the
recorded count (413), as are the swap and feedback counts (376 against
380): the last interval of each renderer is not logged before it exits. As
before, the lines do not time Blink's work before each call.

#### Sub-step 1b design (agreed)

The owner, on 2026-10-03: "I want us to be as precise as we can be because
real tests will inspect the rendered frames."

What the browser does with a popup's rectangle. Read in the Chromium
checkout on the target machine, under `content/browser/` unless named:

- The renderer asks to show the popup with `ShowPopup`, and to move it
  with `SetPopupBounds`. `RenderWidgetHostImpl::ShowPopup`
  (`renderer_host/render_widget_host_impl.cc`, lines 2990 to 3002) passes
  the rectangle through `ClampPopupBoundsToDisplay` (lines 383 to 413),
  which limits its width and height to the display's work area;
  `SetPopupBounds` (lines 2806 to 2818) does the same after
  `ConstrainPopupBounds`, and ignores the request while a screen rectangle
  update is unacknowledged.
- `WebContentsImpl::ShowCreatedWidget`
  (`web_contents/web_contents_impl.cc`, lines 6114 to 6203) transforms the
  rectangle for nested web contents, applies `ConstrainPopupBounds`
  (lines 6096 to 6112), which moves a popup whose top is above the top of
  the main frame's view down to it when `kLimitPopupWidgetHostPosition` is
  enabled, may refuse the popup, and calls
  `RenderWidgetHostViewAura::InitAsPopup`.
- `InitAsPopup` (`renderer_host/render_widget_host_view_aura.cc`, lines
  454 to 530) makes a menu window, sets an owned window anchor that flips
  in y, and parents it with `ParentWindowWithContext`. On Windows a menu
  window gets a top-level widget of its own
  (`ui/views/widget/desktop_aura/desktop_native_widget_aura.cc`, lines
  257 to 269 and 108 to 160), without a standard frame but with the
  system's drop shadow (lines 126 to 132).
- `RenderWidgetHostImpl::SendScreenRects` (lines 700 to 736) reads the
  view's bounds and its top-level window's bounds in screen and sends them
  to the renderer's widget, one update at a time; the renderer's
  `WidgetBase::UpdateScreenRects` stores them
  (`third_party/blink/renderer/platform/widget/widget_base.cc`, lines 571
  to 580).
- The browser allocates the popup widget's routing ID, which the renderer
  does not receive (`renderer_host/render_frame_host_impl.cc`, lines
  11622 to 11629); the widget's frame sink is made from it. A web view
  holds one page popup at a time: `WebViewImpl::OpenPagePopup` cancels the
  open one first (`third_party/blink/renderer/core/exported/web_view_impl.cc`,
  lines 1079 to 1087).

So the browser can change the popup's size and position, and Windows draws
a shadow outside it; only the browser process holds the result.

What is recorded (protocol 0.44), from the browser process, on
`browser.interaction`:

| Record | Where | Holds |
| --- | --- | --- |
| `popup-widget-created` | `RenderFrameHostImpl::CreateNewPopupWidget`, after the widget is made | the renderer's process ID, the opener frame's token, and the popup widget's frame sink |
| `popup-widget-shown` | `WebContentsImpl::ShowCreatedWidget`, after `InitAsPopup`, or where it refuses the popup | the frame sink; the rectangle and anchor as received, after the transform, and after `ConstrainPopupBounds`; whether it was refused and why |
| `popup-widget-bounds-requested` | `RenderWidgetHostImpl::SetPopupBounds` | the frame sink, the rectangle requested, the rectangle set, or that it was ignored |
| `popup-widget-screen-rects` | `RenderWidgetHostImpl::SendScreenRects`, for a popup widget, when it sends | the frame sink; the view and window bounds in screen, in DIPs; the native window's rectangle from `GetWindowRect`, in physical pixels; and the device scale factor |

`page-popup-opened` gains `ownerFrameToken`, the token of the owner's
frame, which the browser knows as the opener frame's token. A popup is
joined to its widget by the renderer's process, the owner's frame token,
and order: the opener frame's next `popup-widget-created` after the
renderer's `page-popup-opened`, since a web view holds one popup at a
time. A popup whose join is not one to one is reported, not guessed.

The renderer's `page-popup-window-rect` record with `source` `placed`, from
`WebPagePopupImpl::SetScreenRects`, is removed with its hook: Chromium does
not call it on this path (see "Sub-step 1 on the target machine"), and the
browser's record replaces it.

What it gives:

- The popup's place on screen is the window's bounds as the browser set
  them, and in physical pixels as Windows holds them, which is what the
  captured screen images are in.
- The popup's frame sink, which its presentation records leave null, is
  named, so slice 4b joins a popup's compositor frames as a page's are.

Limits of 1b:

- The system's drop shadow is drawn by Windows outside the window; it is
  not drawn by Blink, so the recreation does not draw it. Its presence in
  the captured image is noted in the comparison, as a difference that is
  not the page's.
- A popup moved by Windows after `SendScreenRects` without a bounds change
  reaching the view would not be recorded; none is expected, and the
  native rectangle at each send would show a difference.

Required tests for 1b: unit tests of each new record against the record
contract and of the join, including a popup without a widget and a widget
without a popup; the integration script's tests of the browser hooks and
of the removed hook's upgrade; and, on the target machine, a recording of
the CNIB events page with the selects opened, in which each popup joins to
one widget, with its screen rectangles and its cost.

The owner agreed the design on 2026-10-03 ("yes please").

#### Sub-step 1b as built

Built as designed, with these details settled in the code:

- `popup-widget-created` carries the opener frame's navigation context, as
  the frame cookie records do. The other three records carry the browser
  process's context, with no page, frame, or document, and are joined to the
  created record by `frameSinkId`.
- `popup-widget-shown` names its `outcome`: `shown`, `window-not-active`,
  `not-visible`, or `permission-exclusion`. `ShowCreatedWidget` refuses for
  an inactive window before it transforms the rectangle, so that refusal
  carries only the received rectangle and anchor. It returns without a
  record when it has no view for the widget, since there is then no widget
  to name. The transformed rectangle is kept before `ConstrainPopupBounds`
  replaces it, so both are recorded.
- `popup-widget-screen-rects` records the native window's client area as
  well as its rectangle (`nativeWindowRect`, `nativeClientRect`), both in
  screen pixels, or both null when Windows does not answer. The bridge reads
  them from the HWND of the view's window tree host with `GetWindowRect`,
  `GetClientRect`, and `ClientToScreen`, when the record is made, after the
  screen rectangles are sent. Reading them in the bridge keeps `windows.h`
  out of `render_widget_host_impl.cc`.
- `page-popup-window-rect` loses `source` and `widgetRect`; it is always a
  requested rectangle. The integration script rewrites a checkout patched by
  0.43: the owner record's helper, the requested rectangle's helper and hook,
  and the removal of the `SetScreenRects` hook.

Tests run in the sandbox: the integration script's tests, including the
browser hooks written once, failing when an anchor is absent, recording a
refusal before the widget is destroyed, and the 0.43 upgrade; the hooks
applied twice to copies of the three browser files and of the 0.43-patched
`web_page_popup_impl.cc` from the target machine, with no change on the
second run and every bridge call matching the header; and the record
contract tests for each new record and the changed ones. The join is not
yet product code: it is checked by hand against the recording made on the
target machine, which is still to be made, and its required unit tests,
including a popup without a widget and a widget without a popup, come with
the code that first uses it, in sub-step 2.

#### Sub-step 1b on the target machine

Recording `20261003-143108-ab7e18edb49d4a63bace38a455bf5c55`, made by the
owner on 2026-10-03 with the fdb1bdb package, of the CNIB events page with
its selects opened ten times. Read from the recording and its diagnostic
logs:

- Ten `page-popup-opened`, ten `page-popup-closed`, twenty
  `page-popup-window-rect`, and ten of each of the four popup widget
  records. Every `popup-widget-shown` has the outcome `shown`; none was
  refused.
- The join is one to one. All ten popups have the same renderer process and
  owner frame token, and the k-th `page-popup-opened` for that pair matches
  the k-th `popup-widget-created`, each with its own frame sink (`6:18` to
  `6:27`).
- The created record comes before the popup's opened record, by 24 to
  33 ms: the browser makes the widget when the renderer asks for it, and the
  renderer records the popup once its document is installed. The design's
  join, "the opener frame's next `popup-widget-created` after the
  renderer's `page-popup-opened`", has the order backwards. The join is
  corrected to: the opener frame's last `popup-widget-created` before the
  `page-popup-opened`, which with one popup per web view at a time is the
  same pairing as the k-th with the k-th.
- For every popup the rectangle the browser received, transformed,
  constrained, and gave the view equals the renderer's first window
  rectangle, and the received anchor equals the renderer's anchor. The one
  bounds request per popup was set unchanged. The one screen rectangle send
  per popup has view, window, native window, and native client rectangles
  all equal, at a device scale factor of 1.0. On this machine, at this
  scale, the browser did not move or resize any popup, and the native window
  has no frame outside its client area.
- The popups' presentation records still have a null `frameSinkId`, as in
  0.43; slice 4b joins them through the created record's frame sink.

Cost, from the browser process's lines in `diagnostics/browser-bridge.log`
(the browser process writes its cost lines there, not to
`diagnostics/chromium.log`):

| Bridge function | Calls | Mean | Largest |
| --- | --- | --- | --- |
| `RecordBrowserPopupWidgetCreated` | 10 | 139.3 us | 407 us |
| `RecordBrowserPopupWidgetShown` | 10 | 29.8 us | 31 us |
| `RecordBrowserPopupWidgetBoundsRequested` | 10 | 19.5 us | 22 us |
| `RecordBrowserPopupWidgetScreenRects` | 10 | 15.7 us | 22 us |

The created record's cost is split: six calls took about 18 to 20 us (two of
them known only as a sum of 40 us within one report) and four took 270 to
407 us. The cause of the longer calls is not measured.

Not tested by this recording: a refused popup, a popup the browser moved or
clamped, a nested web contents, and a device scale factor other than 1.

#### Sub-step 2 design (agreed)

Agreed by the owner on 2026-10-03: "yes please".

The owner, on 2026-10-03, settling how the popup is drawn: "My requirement
is that devtools work. within the limitations of a snapshot in time. so
right-clicking on an element or popup should allow me to inspect it in the
elements tab. Beyond that requirement you are free to choose the
implementation".

Why a page popup of the recreation's own cannot meet it. Read in the
Chromium checkout on the target machine: `WebPagePopupImpl` makes the
popup's frame with an `EmptyLocalFrameClient` (line 389 of
`core/exported/web_page_popup_impl.cc`, used at line 431) and gives its
page a `PagePopupChromeClient`, an `EmptyChromeClient` (line 175). The
popup's frame is therefore not a `WebLocalFrameImpl`, which is what a
DevTools agent and a context menu are attached to, so its document is not
in the Elements tab and a right-click in it offers no Inspect.

So the popup is drawn as part of the recreated page:

- The popup's document is rebuilt, by the builder as the page's is, inside
  an `iframe` that the builder adds to the recreated page, from the popup
  document's recorded DOM with its recorded styles, fragments, and glyphs
  imposed, as for any document. Its scripts are not run. DevTools shows it
  as the iframe's document, under the iframe, and a right-click on an item
  inspects that item.
- The iframe is the one element the recreation adds that the recording
  does not hold. It is shown with the Popover API (`popover="manual"`,
  `showPopover()`), so it is drawn in the page's top layer, above the page,
  and is not a child box of any recorded box; the page's recorded layout
  and its children matched by node are left as they are. Its inline style
  removes the popover's user-agent border, padding, margin, and
  background, and it carries `data-a11y-recorder-popup` with the popup's
  kind and owner, so it is told apart from recorded nodes in the Elements
  tab. That a top-layer element leaves the recorded boxes unchanged is
  checked in the required tests, not assumed.
- Its place: the recorded window rectangle, less the owner's local root
  rectangle in screen, is the popup's position in the owner's viewport;
  adding the root scroll offset at the frame gives its position in the
  page, where it is set with `position: absolute`, so it stays by its
  select when the snapshot is scrolled. Its size is the window
  rectangle's. In the recording of 2026-10-03, the owner's visible bounds
  plus its local root's origin equal the anchor rectangle for every popup
  (for the first, 403 + 69 = 472 and 95 + 384 = 479), so the two records
  agree; a popup where they do not is reported in the panel.
- Which popup, and which of its states: as in "Which state a captured
  frame shows". The page list in the player stops listing popup documents
  as pages of their own; a popup is opened with its owner's page.
- Selectedness: each option's recorded selectedness at the frame is set
  by the builder, in the page and in the popup, so the select shows its
  value at the frame and the popup's listbox its highlighted option.
- The evidence panel names the popup, its owner, its rendering update,
  and its window rectangle, and the notes say that the iframe is the
  recreation's.

Limits of sub-step 2:

- The popup's window may extend beyond the owner's window on screen; in
  the recreation it lies over the page, and where it extends past the
  page's end it may enlarge the page's scrollable area. Whether it does is
  checked.
- With a zoom factor other than 1, the popup is laid out in its own zoom;
  the size mapping is then checked against the capture before it is
  relied on. The recording of 2026-10-03 has zoom 1 throughout.
- The popup is placed at its window's bounds as the browser set them
  (sub-step 1b), not at the rectangle it asked for.
- Inspecting the popup's document shows the recorded document in an
  iframe, not in a popup window: that is the one difference DevTools
  shows from the page as it was.

#### Required tests

- Unit tests: each new record against the record contract; the
  integration script's hooks and their upgrades; the choice of popup
  state and window for a frame from a sequence of records; the mapping of
  the window rectangle into the recreation's coordinates; selectedness
  imposed on the rebuilt state.
- Integration tests in the instrumented Chromium, on a generated page
  with a select: the select is opened by keyboard and by pointer, the
  highlight is moved by the arrow keys and by hover, and the list is
  closed by Escape, by Enter, and by a click outside; the recording holds
  each opening, window rectangle, selectedness change, presentation, and
  closing, joined to the select; the recreation at each chosen frame
  shows the list open or closed, with the highlighted option, at the
  recorded place. Over the DevTools protocol, the popup's items are found in
  the Elements tree under the added iframe, an item hit at its drawn
  position is the recorded item, and the page's recorded boxes are
  unchanged by the added iframe.
- System test on the target machine: the CNIB events page is recorded
  with each of its selects opened and the highlight moved, and the
  recreation at frames with the list open, after the highlight moves, and
  after it closes with a new value, is compared with the captured screen
  image of each frame; a difference is reported as a defect.

#### Sub-step 2 as built

Built as designed, with these details settled in the code:

- `PagePopups` (Recorder.Session) finds the popups open at a frame: those
  whose `page-popup-opened` names the page's document as owner, at or
  before the frame's composition, with no `page-popup-closed` of the same
  popup document by then. Each popup is joined to the opener frame's last
  `popup-widget-created` at or before its opening, on renderer process and
  owner frame token, that no earlier popup joined. A widget record that no
  popup joins is unused.
- The popup document's state is read at the frame as any document's is,
  so it is the state after the popup's last presented rendering update at
  or before the frame's composition. Its window rectangle is the browser's
  latest for the joined widget at or before the time that state is read
  at: `popup-widget-shown` `viewBounds`, `popup-widget-bounds-requested`
  `setRect` (an ignored request, with a null `setRect`, sets none), or
  `popup-widget-screen-rects` `viewRect`. A popup with no joined widget
  takes its last `page-popup-window-rect`, and before one the rectangle it
  was opened with; the notes say so.
- The root scroll offset added to the place is the recorded scroll offset
  of the page's document node, the same value the builder scrolls to.
- The playback index keeps the page popup and popup widget records with
  their whole payloads, and its version is 2, so a recording indexed by an
  earlier build has its index derived again when opened. The interaction
  state keeps each option's latest selectedness for the life of the
  document, since no checkpoint holds it, and the snapshot format is 3, so
  snapshots of an earlier build are not used and the state is rebuilt from
  the records.
- The builder sets recorded unselections before selections, so that in a
  single select, where a selection unselects the other options, the
  result is the recorded state. An option with no record keeps the
  selectedness its attributes give.
- The popup's markup is served to its iframe as `srcdoc`, with the page's
  script nonce, so the popup's builder runs under the page's content
  security policy, which a `srcdoc` document inherits. That the policy's
  `frame-src 'none'` does not block a `srcdoc` iframe is relied on, and is
  to be confirmed on the target machine. The popup is given no recorded
  font faces: its text is drawn from its recorded glyphs only where Blink
  chooses the recorded font file for it, as for any text.
- The evidence panel's notes name each popup's kind, owner element,
  opening time, joined widget, document, how its state was matched, its
  window rectangle and the record that gave it, and its place in the page.
  They report whether the owner's visible bounds plus its local root's
  origin are the anchor rectangle, and say that the iframe is the
  recreation's. A popup with no DOM walk at or before the frame is named
  and not drawn.

Not built: the owner select's own drawing while its list is open is left
as Blink draws a closed select with the recorded value.

Found while building: the browser's `popup-widget-created` record carries
the opener frame's navigation context, whose document token is the page's,
so the state builder would have made a document of it, keyed by that
token and the navigation's document identity, which the page list, matched
by token, would have offered as a second entry for the page. Popup widget
records, and any interaction record with a browser process context, now
make no document. Popup documents themselves were not offered as pages,
since the list holds only documents committed by a primary main frame's
navigation.

Also found while building: with the larger snapshots of format 3, the
existing test that reads a recording file cut at 60 % of its bytes failed,
because the cut file held a state index record naming a snapshot whose
chunk was cut off. A file cut short can do this whenever an index chunk is
written before the snapshot chunk it names. The state reader now uses the
index only up to the first record that names a snapshot not in the file,
and reads the documents from the records after it.

Tests run in the sandbox: unit tests of which popups are open at a
composition time; the join, including a popup without a widget, widgets
of another frame or process, a widget created after the popup, and a
second popup taking the next unjoined widget; the choice of window
rectangle at a state time; the mapping into the page and the anchor
check; selectedness kept across checkpoints and in snapshots; the tree
data with selectedness and a popup, whose markup carries the page's
nonce; a popup without a DOM walk; no document from a popup widget
record; and the playback index keeping popup records whole. Read against
the recording made on the target machine on 2026-10-03 with a measurement
that is not committed, each of the ten popups was found open at a frame
150 ms after its opening, joined to its own widget (6:18 to 6:27), with a
DOM walk and a presented state, its window from a
`popup-widget-bounds-requested` `setRect`, and the anchor check passing.

Not run in the sandbox: the integration tests in the instrumented Chromium
and the system test, which need Chromium; they are to be run on the target
machine.

#### Sub-step 2 on the target machine

With revision ff7860a, as the owner reported on 2026-10-03: "The recording
is rendering well, the selects display and I can inspect the <option>
elements. Those options are actually selectable, so we should probably
catch the clicks/selects like we did for the other interactive elements.
But it works well".

The read-only snapshot of 2026-09-30 refused changes from the page's own
controls, but only navigation is refused so far (slice 3b, "Leaving the
recreation"). A click on an option in the popup's iframe selects it, as it
would in any listbox.

#### Input refused (agreed)

The owner, on 2026-10-03, replacing the read-only snapshot of 2026-09-30:
"The point of this rendering is that it is a snapshot in time, including
state (which should be shown including visible focus outlines). The only
interaction that should be working on that rendering is right click for
"inspect"".

So the recreation takes no input except the right-click that opens the
context menu with Inspect. Typing in a text field, which the read-only
snapshot of 2026-09-30 allowed, is refused, as are clicks, keys, the wheel,
touch, and hovering. The recorded focus, with the focus outline the
recorded style gives, stays where it was recorded.

Where it is done: in the recreation's renderer, under the recreation
switch, where the widget receives each input event from the browser,
before Blink's compositor-thread scrolling or the page sees it. Not with a
listener the builder adds, which DevTools' Event Listeners pane would show
as though recorded (the reason navigation is refused over the DevTools
protocol in slice 3b); not with `pointer-events` or `inert`, which would
change the imposed style or the accessibility tree; and not with the
DevTools protocol's `Input.setIgnoreInputEvents`, which drops every event
in the browser, the right-click with them, so no context menu opens.

What passes:

- Mouse events of the right button, and the context menu they open, so
  that Inspect is offered on the element under the pointer, in the page
  and in a recreated popup's iframe.
- While DevTools' element picker is on, the mouse events the DevTools
  overlay takes before the page, so that picking an element by pointer
  still works. The overlay consumes them; none reaches the page.
- The browser's own keys, such as the DevTools shortcuts, which the
  browser handles before the renderer.

Everything else is dropped: left and middle button events, mouse moves
outside the picker (so no hover), the wheel, touch, gestures, and keys.
With them go scrolling, text entry, focus changes, selection changes,
control state changes, and links within the page. Navigation stays
refused over the DevTools protocol, as a second guard.

What DevTools does is unchanged: it acts on the snapshot as its tools
allow, such as scrolling a node into view from the Elements tab, and its
changes are not refused by this step.

How: each place the widget receives an input event, for the main thread
and the compositor thread, gains a check that returns the event as
consumed without dispatching it, when the switch is on and the event is not
one that passes. The places are found by name in the Chromium checkout on
the target machine before the patch is written (the widget's input handler
manager, where events from the browser arrive, and the frame widget's input
handling, after the DevTools overlay); their lines are recorded here then.

Reporting: the evidence panel's notes say that the recreation takes no
input except the right-click and the DevTools element picker. A dropped
event is not written to the Console, since mouse moves alone would fill
it.

Not recorded: no protocol change. The integration script gains the hooks,
so Chromium is rebuilt.

Limits:

- With scrolling refused, the window shows the page at its recorded
  scroll position only; the rest of the page is reached through DevTools.
- Script run in DevTools' Console can still change the page, as it can
  change any node; refusing it is not part of this step.

Required tests:

- Unit tests: the integration script's hooks, written once, unchanged when
  applied twice, and failing when an anchor is absent.
- Integration test in the instrumented Chromium, on a generated page with
  a select, a list box, a check box, a details element, a text field, a
  link within the page, a scrollable area, and a recorded focus,
  recreated: trusted left clicks, keys, wheel, and mouse moves leave the
  DOM, every control's state, the selection, the focus, and every scroll
  offset as recorded, and no `select` popup opens; a right click opens
  the context menu; the DevTools element picker selects the element under
  the pointer; the same holds in a recreated popup's iframe.
- System test on the target machine: in a recreation of the CNIB events
  page with a list open, clicks, keys, and the wheel change nothing, the
  recorded focus outline stays, and right-click and Inspect select the
  element clicked.

#### Input refused as built

Agreed by the owner on 2026-10-03 ("yes please"). The places were read in
the Chromium checkout on the target machine before the patch was written.

Compositor thread. `InputHandlerProxy::RouteToTypeSpecificHandler`
(`third_party/blink/renderer/platform/widget/input/input_handler_proxy.cc`,
line 837 in the checkout) is where every event the browser sends to a
frame widget is handled on the compositor thread, queued scroll gestures
included, before the wheel, scroll, pinch, and touch handlers run (line
878 onward). Under the recreation switch it now returns, before any of
them: `DID_NOT_HANDLE` for a mouse event, so that the event goes to the
main thread and no scrollbar is dragged on the compositor thread; and
`DROP_EVENT` for every other event, so that keys, the wheel, touch, and
gestures reach neither the compositor's scrolling nor the page. The
platform component, `component("platform")` in
`third_party/blink/renderer/platform/BUILD.gn`, which lists that file,
gains the recorder bridge as a dependency.

Main thread. `WebFrameWidgetImpl::HandleInputEvent`
(`third_party/blink/renderer/core/frame/web_frame_widget_impl.cc`, line
3482) first gives the event to the DevTools agent (lines 3511 to 3516),
which takes the element picker's events and returns. The hook follows the
point where the current input event is set (line 3530), before pointer
lock, the mouse-down handling, and `WidgetEventHandler::HandleInputEvent`
(line 3588). Under the recreation switch, a right-button mouse event of the
type that shows a context menu (mouse up when the page setting
`ShowContextMenuOnMouseUp` is true, as on Windows, otherwise mouse down,
the rule of `HandleMouseDown` at line 1209 and `HandleMouseUp` at line
1272) is passed to `MouseContextMenu` (line 1229), and the widget returns
the event as handled; every other event is returned as suppressed. The
page therefore receives no mouse down, mouse up, or mouse move: no focus,
selection, hover, or control change.

What the context menu does. `MouseContextMenu` calls
`EventHandler::SendContextMenuEvent` (`core/input/event_handler.cc`, line
2177), which performs an active hit test and dispatches the `contextmenu`
event to the element under the pointer; no page script runs to see it. It
changes no selection. The hit test can set Blink's hover and active state
on the element under the pointer; whether that state changes what is drawn,
under the imposed recorded style, is to be seen in the system test.

Found while reading, not part of this step: text committed by an input
method (`ImeCommitText` and `ImeSetComposition` on the frame widget)
reaches Blink through the widget's input method interface, not as an
input event, so these hooks do not refuse it. It inserts text only into a
focused editable element; whether to refuse it is for the owner to decide.

Tests as built:

- Unit tests (`RecreationInputIntegrationTests` in
  `chromium/test_integrate.py`): each hook is written once, a second run
  leaves the file unchanged, and a missing anchor fails the run; the
  compositor check precedes the scroll handling, and the main-thread check
  follows the DevTools agent and precedes the widget's own handling. The
  three patches were also run against copies of the target machine's files
  and checked against the bridge's signatures.
- Unit test: the evidence notes say the recreation takes no input except
  the right-click.
- Integration test (`TheRecreationTakesNoInput`, run only with the
  instrumented Chromium): on a generated page, trusted left clicks on a
  link, a link within the page, a check box, a select, a list box, a
  summary, and a text field, a typed key, and the wheel over the page and
  over a scrollable area leave the address, the DOM, every control's state,
  the selection, the focus, and every scroll offset as built; a right click
  dispatches the context menu event once. The navigation test,
  `TheRecreationDoesNotLeaveThePage`, now starts its navigations by script
  with a user gesture, since clicks are refused. The DevTools element picker
  and the recreated popup's iframe are left to the system test, as the
  test's DevTools client does not read protocol events.

#### Input refused only in the recreation (proposed)

Reported by the owner on 2026-10-03: "You have stopped user interaction on
devtools".

Cause, read in the bridge and the hooks. The recreation switch is passed
to every renderer process (`AppendRecorderBootstrapToChildProcess`), and
`IsRecreationMode()` is true in all of them. Both input hooks of "Input
refused as built" test only `IsRecreationMode()`, so they refuse input in
every renderer: the recreated page's, and also the DevTools front end's
(a `devtools://` page), the evidence panel's (a `chrome-extension://` page
inside DevTools), and any browser page drawn by a renderer (`chrome://`).

Proposed fix: input is refused only in a widget showing recorded content,
that is, a widget whose local root document is not a browser page. A
browser page is one whose URL scheme is `devtools`, `chrome`,
`chrome-untrusted`, or `chrome-extension`.

- Main thread: the hook in `WebFrameWidgetImpl::HandleInputEvent` refuses
  only when the widget's local root document is not a browser page; for a
  browser page the event is handled as in any Chromium.
- Compositor thread: `InputHandlerProxy` cannot read a document, so the
  bridge keeps one flag per renderer process, set on the main thread when
  a browser page's parser is created (`Document::ImplicitOpen`, the place
  of protocol 0.42's walk), before the page can be drawn or take input.
  The compositor hook drops events only in a process without the flag.
  This rests on Chromium putting browser pages and extensions in processes
  of their own, apart from web content; that is to be confirmed in the
  Chromium source before the patch is written, and the design revised if
  it does not hold.
- Recreation, evidence panel, and recording: unchanged.

Limits:

- Recorded content at a browser page's address (a recording of a
  `chrome://` page) would take input in its recreation. Recordings are of
  web pages, so this is stated, not handled.

Required tests:

- Unit tests (`chromium/test_integrate.py`): the main-thread hook tests
  the local root's scheme; the compositor hook tests the process flag; the
  parser hook sets it only for the four schemes; each hook is written once
  and a missing anchor fails the run.
- Unit test of the bridge's scheme decision, without a Chromium build.
- Integration test (run with the instrumented Chromium, as
  `TheRecreationTakesNoInput`): the recreated page still takes no input
  but the right-click, as now.
- System test on the target machine: in a recreation, DevTools' panels
  take clicks, typing, and scrolling, the evidence panel scrolls, and the
  page itself still takes only the right-click for Inspect.

#### Popup on screen (agreed)

Reported by the owner on 2026-10-03, with d9461fc, on recording
20261003-143108: three of the four selects show their lists in the
recreation, and the day-of-week select at 38.653 s does not.

What the recording shows. The day-of-week list (popup document
dom-document-11093) was opened by a mouse press at 37.849 s; its
`page-popup-opened` record is at 37.919 s, the browser's
`popup-widget-shown` at 37.930 s, and its first presented rendering update
at 38.095 s. A mouse press outside the list at 38.529 s closed it:
`page-popup-closed`, closed by the renderer, at 38.531 s. The browser
records nothing of the popup's window after that. The captured frames
around it:

| Frame | Composed | List in the captured image | Open by the current rule |
| --- | --- | --- | --- |
| 180, 37.910 s | 37.929 s | no | yes |
| 181, 38.112 s | 38.129 s | yes | yes |
| 182, 38.316 s | 38.345 s | yes | yes |
| 183, 38.519 s | 38.545 s | yes | no |
| 184, 38.723 s | 38.745 s | no | no |

So the current rule, an open record at or before the frame's composition
and no close record by then (sub-step 2 as built), is wrong at both ends.
The renderer's records are not the times the list is on the screen: the
list is drawn after it is opened, and its window leaves the screen after
the renderer closes it. Frame 183 was composed 14 ms after the close
record and still shows the list; frame 180 was composed before the
browser showed the window and does not. The position near the window's
right edge plays no part: the list's window rectangle, (1369, 519, 263,
242), lies inside the page's local root, (449, 87, 1240, 925). The same
holds at the reopening: frame 187, 39.321 s, composed at 39.345 s, is
open by the current rule with no presented rendering update of the
popup yet.

Proposed rule: a popup is shown at a captured frame when it has a
presented rendering update at or before the frame's composition (its
first frame on the screen) and its window had not left the screen by the
composition.

What is recorded (protocol 0.45): on `browser.interaction`, a new
`popup-widget-hidden` record in the browser process when the popup's
window is hidden or destroyed, with the popup's frame sink ID, so that it
joins the widget records as `popup-widget-shown` does, and which of the
two happened. The place is found by name in the Chromium checkout on the
target machine before the patch is written (the popup's
`RenderWidgetHostViewAura`, where its window is hidden or destroyed), and
its line is recorded here then. A recording without the record, such as
20261003-143108, closes a popup at its `page-popup-closed` record, as now,
and the evidence panel says so.

What the recreation does: unchanged, at the frames the rule chooses. The
evidence panel's popup line names the presented update and the window
record the choice rests on.

Limits:

- The Windows compositor composes on its own schedule, so a frame
  composed within a few milliseconds of the window's removal may show
  either state. Where the hidden record and the composition are within
  one display interval of each other, the evidence panel says that the
  frame is at the edge.

Required tests:

- Unit tests: the rule at each end (no presented update, so not shown; a
  presented update at or before the composition, shown; hidden before the
  composition, not shown; no hidden record, closed at the close record);
  the new record parsed and joined on frame sink ID; the integration
  hook written once, unchanged when applied twice, failing when its
  anchor is absent.
- Integration test in the instrumented Chromium: a select's list opened
  and closed records one `popup-widget-hidden` after its
  `page-popup-closed`, with the frame sink ID of its `popup-widget-shown`.
- System test on the target machine: in a new recording, each frame
  either side of a list's opening and closing shows the list in the
  recreation when, and only when, the captured image does.

#### Popup on screen as built

Agreed by the owner on 2026-10-03 ("yes build it").

Where the record is made. In the target machine's checkout,
`content/browser/renderer_host/render_widget_host_view_aura.cc` hides a
popup view's window in two places: `RenderWidgetHostViewAura::Hide`, line
533 (`window_->Hide();`), and `RenderWidgetHostViewAura::CleanUpHostObservers`,
line 3015, which `RenderWidgetHostViewBase::DestroyOrDefer`
(`render_widget_host_view_base.cc`, line 845) calls before the view is
destroyed, the path a closed popup takes. `InitAsPopup`, line 454, makes
the window with `WINDOW_TYPE_MENU`. The integration script's
`patch_content_render_widget_host_view` adds the bridge include, a helper
that reads the popup's HWND from its window tree host, and a hook at each
place: when the view is a popup (`WidgetType::kPopup`) and its window was
shown (`TargetVisibility`), it records after `window_->Hide()`, with the
cause `hidden` or `destroyed`. A view already hidden records nothing, so a
popup hidden and then destroyed has one record, and the destructor's
second clean-up, which finds no window, has none.

The record (protocol 0.45): `popup-widget-hidden` on `browser.interaction`,
with the browser process's context, `frameSinkId` written `clientId:sinkId`
as the other popup widget records are, `cause`, and `nativeWindowVisible`,
which `RecordBrowserPopupWidgetHidden` reads with `IsWindowVisible` on the
HWND when the record is made, or null when there is no window. It states
whether the native window was off the screen when the record was made,
rather than assuming that hiding the Aura window hides it at once. The
recorder's contract is `BrowserPopupWidgetHiddenPayload`, and its
validator requires the browser process's context and one of the two
causes.

The rule, in the recorder:

- `PagePopups.OpenAt` closes a popup at the first `popup-widget-hidden`
  of its joined widget's frame sink after it opened; a recording without
  one closes it at its `page-popup-closed`, as before. It keeps both times
  and the frame's composition time with the popup.
- `RecordingFileDocuments.Popups` keeps only the popups whose document
  state at the frame is the one after a presented rendering update
  (`IsDrawn`), so a popup opened but not yet drawn by the composition is
  not shown.
- The evidence panel's popup line adds, after the joined widget, either
  the time the window was hidden and the composition, or that the
  recording holds no hidden record and the popup is taken as open until
  its close record. It still names the presented update its state follows.
  Where the hidden record follows the composition by at most one 60 Hz
  display interval (16.667 ms), it says that the frame is at the edge and
  the captured image may show either state.

Limits as built:

- The edge interval is fixed at one 60 Hz display interval, not read from
  the display of the recording.
- A popup hidden shortly before a composition is not shown, and so has no
  line in the evidence panel; the edge is stated only for a shown popup.
- Recording 20261003-143108 has no hidden records, so its frame 183 still
  does not show the day-of-week list; a new recording is needed.

Tests run: Python unit tests of the integration script (the hooks written
once, unchanged when applied twice, failing when an anchor is absent, the
shown check before the hide and the record after it), 197 in all; the
patch was also applied to a copy of the target machine's file and checked
against the bridge's signatures. .NET unit tests (970 passed, with the 4 known ChromiumLauncherTests failures that need Windows): the rule at each end
(`PagePopupTests`), the edge text, the drawn check, and the record's
contract and validator. Not done: the integration test in the instrumented
Chromium, which needs a recording run of it, as the earlier recording
additions did; what is recorded is checked on the target machine with a
new recording, alongside the system test.

#### Layout of a walked rendering update (agreed)

Reported by the owner on 2026-10-03, with 9d22e68, on recording
20261003-193544: each list is shown, but in some frames just after a list
opens its highlighted option is mid grey in the recreation, and it becomes
blue a few frames later; the captured images show it blue throughout.

What the recording shows. The recorded style of the highlighted option is
blue, `rgb(25, 103, 210)`, in every record of it. The first list, popup
document dom-document-9994, had its first rendering update walked in full
(layout-checkpoint-12, completed at 22.6195 s), and that update's
presentation request, at 22.6195 s, names the checkpoint. The same update
then recorded its layout change set, layout-changes-62, from 22.633 s, with
the list's nodes and their styles. The bridge makes no presentation request
for a change set in a walked update (`RecordBlinkLayoutChanges` returns 0
for it), so the update is presented through its checkpoint, and the state
read for it is cut at the checkpoint's completion, before its change set.
Layout state is built from change sets only, not from checkpoints, so for
the frames at 22.697 s to 23.285 s the popup's state has its DOM and no
layout record at all: none of its elements carries a recorded style, the
recreation's Blink computes the styles itself. The grey is most likely the
colour Blink gives a selected option in a list that is not focused, as
the recreated list is not; that is inferred, not checked. The next presented update, at 23.405 s, follows a change set, and
from there the option is blue. The list opened at 35.510 s,
dom-document-10762, shows the same at 35.689 s.

The same cut applies to every walked update of any document (its first
update, the end of parsing, an update after a lost record, and the check
interval): the frame presented by that update is given the layout of the
change set before it.

Proposed fix. A change set read in the same rendering update as the
checkpoint it names is part of that update, and the presented state
includes it:

- Recording (protocol 0.46): `layout-changes-started` gains
  `checkpointUpdate`, true when the change set is the one the bridge reads
  for the update its named checkpoint recorded (the update state the
  checkpoint left had not yet been read by a change set), and false
  otherwise. The bridge already decides this: it is the condition under
  which `RecordBlinkLayoutChanges` makes no presentation request.
- Playback: a presented checkpoint whose update has such a change set is
  cut at the change set's completion, not the checkpoint's; its presented
  time is unchanged. A recording without the field is read as now.
- Evidence panel: unchanged; the basis it states is the presented update.

Limits:

- A walked update whose change set the Blink hook did not record (an
  early return in `RecorderRecordLayoutChanges`) is still presented through
  its checkpoint alone, and the frame's state still lacks its layout; the
  recording shows this by the absence of a change set with
  `checkpointUpdate` true.
- Recording 20261003-193544 has no such field, so its frames keep the grey
  highlight; a new recording is needed.

Required tests:

- Unit tests: the bridge field true only for the change set of the
  checkpoint's own update; the contract and the validator; the playback
  index cutting a presented checkpoint at its update's change set, and at
  the checkpoint when there is none.
- Integration test in the instrumented Chromium: a generated page's first
  update records a checkpoint and a change set with `checkpointUpdate`
  true, and a later update a change set with it false.
- System test on the target machine: in a new recording, each frame just
  after a list opens shows its highlighted option in the colour the
  captured image shows.

#### Layout of a walked rendering update as built

Agreed by the owner on 2026-10-03 ("yes please").

- Bridge: `RecordBlinkLayoutChanges` writes `checkpointUpdate` on
  `layout-changes-started` from `IsCheckpointUpdateChangeSet` in
  `full_walks.h`, given whether the document's last update was walked and
  not yet read by a change set (`Update::kWalked`) and the checkpoint the
  change set names. It is the condition under which the function returns 0
  and so makes no presentation request for the change set.
- Contract and validator: `BrowserLayoutChangesStartedPayload` gains
  `CheckpointUpdate`; the validator requires the field and, when it is
  true, a named checkpoint.
- Playback: the playback index keeps the completion time of each change
  set marked `checkpointUpdate`, by browser instance, process, and the
  checkpoint it names, and a presented checkpoint with one is cut at that
  time rather than at the checkpoint's completion. Its presented time is
  unchanged. The database playback reader, which the recreation does not
  use, is not changed.

Tests run: the C++ test of `IsCheckpointUpdateChangeSet` (true only with
an unread walked update and a named checkpoint); .NET unit tests of the
playback index (`WalkedUpdateLayoutTests`: cut at the change set's
completion with the presented time unchanged; cut at the checkpoint with
the field false, absent, or naming another checkpoint) and of the
validator, 974 passed with the 4 known ChromiumLauncherTests failures that
need Windows; 197 Python tests of the integration script. Not done: the
integration test in the instrumented Chromium,
for the reason given under "Popup on screen as built"; what is recorded is
checked on the target machine with a new recording.

#### Window fade of a popup (agreed)

Reported by the owner on 2026-10-03, with 93e31d6, on recording
20261003-203229: at 38.684 s the captured image shows the open list
slightly translucent, with the page visible through it, and the
recreation shows the list opaque.

What the recording shows. The list's document is dom-document-10975. Its
window was shown at 38.530 s (`popup-widget-shown`) and its first update
was presented at 38.605 s. Opacity is among the 480 recorded style
properties, and it is 1 in every node record of the document. The
captured image of the frame at 38.694 s (frames/desktop/0000000183.png,
captured in about 60 ms from about 38.66 s) shows the page's text through
the list; the next, at 38.901 s (0000000184.png), shows the list opaque.

Where the translucency comes from (read from the Chromium source, not
measured). The list's window is a top-level Windows popup window of its
own (`DesktopNativeWidgetTopLevelHandler::CreateParentWindow`, type menu).
Chromium's own fade of a menu window (`wm::VisibilityController`, 150 ms
by default for menus in `window_animations.cc`) is installed only for a
widget created translucent (`DesktopNativeWidgetAura::InitNativeWidget`),
which a list with an opaque background is not. Chromium leaves the
window's DWM transitions enabled: it sets
`DWMWA_TRANSITIONS_FORCEDISABLED` only when a widget's visibility
animations are turned off (`HWNDMessageHandler::SetVisibilityChangedAnimationsEnabled`).
The fade is therefore most likely a transition of the Windows desktop
compositor, applied after Chromium presents the window. The window's
opacity during a DWM transition is not reported to the application, and
DWM documents the attribute only as enabling or disabling transitions
([DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)).

Agreed approach (option 2, 2026-10-03): the browser under test is not
changed, the recreation keeps drawing the list as recorded, opaque, and
the recording and the evidence panel state what is known.

- Recording (protocol 0.47): `popup-widget-shown` gains
  `windowsAnimationSettings`, read with `SystemParametersInfo` in the
  browser process as the window is shown: `clientAreaAnimation`
  (`SPI_GETCLIENTAREAANIMATION`, the "Animation effects" setting),
  `uiEffects` (`SPI_GETUIEFFECTS`), `menuAnimation`
  (`SPI_GETMENUANIMATION`), `menuFade` (`SPI_GETMENUFADE`), and
  `comboBoxAnimation` (`SPI_GETCOMBOBOXANIMATION`), each true, false, or
  null when the call fails
  ([SystemParametersInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)).
  Which of these governs a DWM transition is not documented, so all are
  recorded and none is interpreted.
- Evidence panel: for each popup drawn at a frame, the note gains how
  long before the frame's composition its window was shown, the recorded
  settings, and that the window's opacity at the capture is not recorded,
  so the captured image may show the window part way through a fade that
  the recreation, which draws the popup opaque as its recorded styles
  state, does not show.
- Recreation: unchanged.

Limits:

- No duration of the fade is recorded, so the panel cannot say which
  frames the fade reaches; it states the interval since the window was
  shown on every frame of an open popup, and the reader compares it with
  the captured image.
- A fade when the window is hidden, if there is one, is not marked; the
  recreation stops drawing the popup at its hidden record ("Popup on
  screen").
- A setting changed while a popup is open is not recorded until the next
  popup is shown.
- Recording 20261003-203229 has no settings field; its panel states that
  the settings were not recorded.

Required tests:

- Unit tests: the bridge's settings value from given call results,
  including a failed call; the contract and the validator; the panel note
  with settings, with the interval since showing, and for a recording
  without the field.
- Integration test in the instrumented Chromium: opening a list records
  `popup-widget-shown` with the five settings.
- System test on the target machine: in a new recording, the settings in
  the panel match the Windows settings, and the panel note appears on the
  frame just after a list opens.

#### Window fade of a popup as built

Agreed by the owner on 2026-10-03 ("yes please").

- Bridge: `RecordBrowserPopupWidgetShown` sets `windowsAnimationSettings`
  from `WindowsAnimationSettingsValue`, which reads the five settings with
  `SystemParametersInfoW` through `ReadWindowsAnimationSettings` in
  `animation_settings.h`; a failed call is null. The record is written for
  every outcome, so a refused popup carries the settings too.
- Contract and validator: `BrowserPopupWidgetShownPayload` gains
  `WindowsAnimationSettings` (`BrowserWindowsAnimationSettings`, five
  nullable booleans); the validator requires the object and each of its
  fields, a boolean or null.
- Playback: `PagePopups.OpenAt` joins each popup to the first
  `popup-widget-shown` record of its widget with the outcome "shown" after
  its opening, and gives `WindowShownTime`, `WindowsAnimationSettings`,
  and `FadeBasis`, the panel text. The RecordedPage note of each drawn
  popup includes `FadeBasis` after `OnScreenBasis`. On recording
  20261003-203229 the note for the frame at 38.694 s reads "its window was
  shown at 38.530 s, 191.4 ms before the frame's composition at 38.721 s;
  the Windows animation settings were not recorded; the window's opacity
  at the capture is not recorded, ...".
- Recreation: unchanged.

Tests run: the C++ test of `ReadWindowsAnimationSettings` (each setting
its own field; a failed read null, the others kept), compiled and run by
the Python suite with a test that the bridge reads the five settings and
sets the field; .NET unit tests of the validator (the object and its
fields required, a string rejected, null accepted) and of `PagePopups`
(`FadeBasis` with settings and the interval, without settings, and
without a shown record): 977 passed, with the 4 known
ChromiumLauncherTests failures that need Windows, and 199 Python tests.
Not done: the integration test in the instrumented Chromium, for the
reason given under "Popup on screen as built"; what is recorded is
checked on the target machine with a new recording.

### To be settled

- How the recorded state reaches the renderer of the recreation: over the
  recorder's own connection to the bridge, or served on the loopback
  interface and read by the browser process. Settled in the first step;
  stage 3 above uses attributes on the built DOM for now.
- The recreation mode is a switch of the instrumented Chromium that also
  records, so that one build is kept (agreed 2026-09-30).

### Required tests

- Unit tests of the recording additions against the record contract, and of
  the recorded state the recorder sends to the recreation mode.
- Integration tests in the instrumented Chromium: for a generated page,
  every imposed computed-style value, box fragment, fragment item, and glyph
  run read back from Blink equals the value imposed; a change to the DOM or
  a style is refused; typing into a text field changes only its value and
  its own text layout; reloading returns the recorded values.
- System test on the target machine: a recorded page is inspected, and
  DevTools' Computed pane and box model show the recorded values of chosen
  nodes.

## Slice 3b implementation

In progress on the `recreation` branch. This section records what is built
so far and where it differs from the design.

### Built so far

- The slice 2 state (`Recorder.Session`) keeps, for each document, its
  registered listeners, its pending timers, the latest accessibility data
  of each DOM node, and the viewport of its latest layout checkpoint. The
  listener, timer, and accessibility channels are state channels, written
  to the `browser-state` stream, and the snapshot format is version 2.
  Recordings made before are read without these parts, as the design
  allows.
- `InteractionDocumentState.Current()` gives the focused node, the
  selection, and each text control's value and selection, from the latest
  interaction checkpoint and the changes after it.
- The recorded page (`Recorder.Recreation`): `RecordedPage` writes the
  short document of the design, with the recorded tree as JSON in a data
  block that cannot end early, since `<` is escaped, and the builder script
  (`Builder\builder.js`) allowed by a nonce new for each recreation.
  `RecordedEvidence` fills the evidence panel from the state, and
  `RecordedPaths` gives each node's path through its shadow roots.
- `RecordingFileDocuments` (`Recorder.Database`) lists the candidate
  documents at a frame and reads the chosen one's state.
- The player's "Inspect page at this frame" lists the pages at the frame
  shown and opens the one chosen in the recreation browser of slice 3a.
- The DevTools protocol connection (`DevToolsConnection`,
  `RecreationControl`), as in "Leaving the recreation": navigations away
  from the recreation are refused and listed in the panel, and the
  recreation's tab is given the recorded viewport and focus emulation. The
  panel's notes state the viewport used and the time of the layout
  checkpoint it came from.

Not yet built: the recreation browser with the recorder bootstrap and the
in-memory receiver, the check, and selecting a node inside a closed shadow
root. The check is built after slice 4 (see "The check is a background
guard").

### Differences from the design

- The document's URL comes from the playback index's `navigation-completed`
  record, which the index keeps whole, not from the state or its snapshots.
- The accessibility records are update batches, each naming only the nodes
  that changed (see accessibility-checkpoint-evidence-model.md), not
  checkpoints of the whole tree. The state therefore keeps, for each DOM
  node, the latest record that named it and the time of its batch. Removals
  of accessibility nodes are not recorded, so a node's data can be older
  than the frame.
- "Focusable" is read from the FOCUSABLE state in Chromium's
  `serializedProperties` text as recorded. That text is Chromium's
  diagnostic form, not a field of the record contract; the panel says so.
- Listener, timer, and accessibility records can come before the first DOM
  record of their document: 98 listener records in the recording of
  2026-09-29 did. They are held until that record and then applied, and
  the document's first record time moves to the earliest of them.
- The recording gives an attribute's namespace and local name, not its
  prefix. The builder gives `xlink`, `xml`, and `xmlns` attributes the
  prefixes the HTML parser gives foreign attributes
  ([HTML standard, adjust foreign attributes](https://html.spec.whatwg.org/multipage/parsing.html#adjust-foreign-attributes)).
- A pending timer's time remaining is counted from the recording time of
  the state used, the cut of the frame's basis, not from the frame's time.
- Running animations and transitions are not read from the recording yet;
  the panel says so rather than listing none.
- Chromium's layout zoom factor includes the device pixel ratio, so the
  panel notes a possible browser zoom when the recorded factor differs from
  the recorded ratio, not when it differs from 1.
- A tab waiting for the debugger answers its release only after its first
  request has been paused and handled, so the connection sends a new tab's
  commands in order without waiting for their answers.

### Found while building

- A page can be drawn before its first DOM walk. In the recording of
  2026-09-29, a product page's first presentation was at 65.52 s and its
  first DOM walk, at finished parsing, at 66.13 s; its layout and
  interaction checkpoints began at 65.42 s. At a frame between the two the
  page cannot be recreated, and the player says so. Added to slice 4.
- Template contents are not recorded: the records have no field for them,
  and the six `template` elements of that recording have no recorded
  children. Added to slice 4.

### Tests

- Unit tests: the listeners, timers, accessibility data, and viewport of a
  document, including records before its first DOM record and records of
  another document of the same process; snapshots of version 2 and the
  records after them giving the state of every record; the tree data
  written for the builder, including namespaces, shadow root fields, manual
  slot assignment, cut values, and a title that holds `</script>`; paths
  through open and closed shadow roots, SVG, text, and comments, and none
  into a user agent shadow root; the evidence read from a state.
- Integration test, with `RECORDER_RECREATION_CHROMIUM` set: a generated
  tree the HTML parser cannot return (a `div` directly in a `table`, a `p`
  in a `p`), with a document type, SVG with `xlink` attributes, an open
  shadow root with a manually assigned slot, a closed shadow root, a
  script, an event handler attribute, and a `javascript:` link, is built,
  and the DOM Chromium then holds, read through every shadow root over the
  DevTools protocol, equals the tree given. No script, handler, or link
  runs; the value, caret, and focus of the text control are set; every
  path in the evidence selects its node through the panel's resolver,
  except the one inside the closed shadow root, which reports that it
  cannot be reached. The protocol leaves out text nodes of white space
  only, so the comparison leaves them out on both sides. On the
  development machine this passed with a Chromium build of the test
  framework's own.
- On the development machine, the same comparison was run on two
  documents of the recording of 2026-09-29, at 28.2 s and 94.0 s: the
  built DOM equalled the recorded tree in every node compared (627 and 729
  lines), and each of the 65 and 80 interactive element paths selected an
  element of the recorded name. This was a measurement, not a committed
  test.
- Unit test: only the recreation's own address, DevTools, and a blank tab
  may be loaded.
- Integration test, with `RECORDER_RECREATION_CHROMIUM` set: a recreation
  with a link, a link with `target="_blank"`, and a form is opened through
  the recorder's own session, without a window, at a recorded viewport of
  800 by 600. The page's inner size is 800 by 600; a real click on each
  link and on the form's button leaves the page's address and its whole
  markup unchanged; both links are listed as refused, the second as in a
  new tab, and that tab is closed; the form makes no request. On the
  development machine this passed with a Chromium build of the test
  framework's own.

### On the target machine

With revision f1ea0a7, on a recording of https://cnib.ca, as reported:

- Links to other pages were blocked, and links within the page, such as
  skip links, worked. A link within the page loads no document, so it is
  not paused.
- The recreation's inner size and device pixel ratio were 929 by 925 and
  1, as the panel's note gave them from the page's latest layout
  checkpoint, recorded at 18.543 s.
- After a click into DevTools, `document.activeElement` in the recreation
  was the element the recording had focused.

The recording has no link that opens a new tab and no form, so neither
could be checked on it; both are covered only by the integration test on
the development machine.

## Text by content hash (agreed, deferred)

Slice 1 records each character data node's data in every checkpoint. Script
and style text can be long and rarely changes between checkpoints, and page
text is repeated in every checkpoint of its document, so the same strings
are recorded many times. The agreed design, to be implemented after slice 4
of [change-driven recording](change-driven-recording.md), which removes the
repeated walks of unchanged nodes:

1. A text is identified by the SHA-256 digest of its UTF-8 bytes. The
   renderer records a text record, holding the digest, the text, and its
   length, the first time it meets the text, and the checkpoint and change
   records refer to the digest. A digest needs no identity assigned across
   renderer processes, is the same for the same text in any tab, document,
   or recording, and lets the app check each text record by computing the
   digest again.
2. The renderer keeps, for each node, the string object whose digest it
   computed. Blink replaces a node's string object when its data changes,
   and a kept reference prevents the object's memory from being reused, so
   an unchanged object means unchanged data, and the digest is computed
   again only for new data.
3. The app writes each text once per recording, rejects a text record whose
   digest does not match its text, and ignores the repeated records of
   other renderers. The playback index maps each digest to the position of
   its text record.
4. After any loss between the renderer and the file, the renderer forgets
   which texts it has recorded and records them again. A reference whose
   text the file does not hold is reported as missing text, not guessed.

To be settled: whether attribute values, which also repeat (SVG path data,
`data:` URLs, inline styles, class lists), use the same records, and whether
short texts, whose digest is longer than the text, are recorded inline.

## Slice 1

The DOM checkpoint helper in `document.cc` records, after the node record of
each `CharacterData` node, a `dom-checkpoint-node-character-data` record
with the node's data, and passes the number of such records to the
completion. The record is described in the
[DOM checkpoint evidence model](dom-checkpoint-evidence-model.md). The
integration script replaces the helper of a checkout patched for protocols
0.29 to 0.32.

`DomCharacterDataCheck` checks a recording file: each text and comment node
of a checkpoint has exactly one data record directly after it, no element,
document, or shadow root has one, and each completion's count equals the
records. It also rebuilds each node's data from the checkpoints and
transitions, and compares each transition's previous text and each later
checkpoint's data with it.

The payload validator accepts a completion without `characterDataCount`,
because the database evidence tables, which are not written for a recording
that has a recording file, have no column for it and no table for the data
records. The check above reports a completion without it.

The check run on a recording made with protocol 0.32 found 246,202 text
nodes and 7,652 comment nodes in 386 checkpoints, none with data.

Recording the data adds volume in proportion to the page's text, including
script and style text. That volume has not been measured.

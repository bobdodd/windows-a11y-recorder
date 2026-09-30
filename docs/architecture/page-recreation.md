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

### To be settled

- How the recorded state reaches the renderer of the recreation: over the
  recorder's own connection to the bridge, or served on the loopback
  interface and read by the browser process. Settled in the first step.
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

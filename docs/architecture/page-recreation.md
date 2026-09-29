# Recreating the Page at a Captured Frame

## Status

Slice 1 (character data in DOM checkpoints, protocol 0.33) is merged, and
the character data check passed in the recordings at protocols 0.35 and 0.36
on the target Windows machine; see "Slice 5 status" in
[change-driven recording](change-driven-recording.md). Slice 2 (rebuilding
the recorded state at any captured frame) is implemented on the
`frame-state` branch as designed in "Slice 2 design", with the differences
listed in "Slice 2 implementation", and is not yet tested on the target
Windows machine. Slice 3 is proposed and not implemented.
Recording text by content hash is agreed and deferred; see "Text by content
hash".

## Purpose

An auditor reviewing a recording needs to inspect the page as it was at a
chosen captured frame: its elements, attributes, text, and styles, with the
tools auditors already use. The recorder is an evidence logger, not an
analyzer. A recreation is a view of recorded evidence, and it must not be
presented as the page itself.

## Decision

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
3. App: write the rebuilt page for the current frame and open it in Chrome,
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
  every document, its last record time, its latest snapshot's event key,
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

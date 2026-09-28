# Change-Driven Browser Capture and Recording Files

## Status

Slices 1 (the recording file) and 2 (playback from the file) are
implemented on branch `recording-object-store` and were each tested in one
recording on the target Windows machine; see "Slice 1 status" and "Slice 2
status". The content limits are removed and await their test there; see "No
limit to content". The browser capture below is proposed and not
implemented. The Blink locations below were read from the Chromium checkout
on the target Windows machine, version 156.0.8065.0 (`chrome/VERSION`), and
must be read again if the checkout changes.

## Problem

The recorder is slow in use: hovering over a button is sluggish while a page
is recorded, stopping a recording takes noticeable time, and opening one can
take over 10 seconds. Measurements from Windows recordings made on the
unmerged `layout-keyframes` branch (revisions 6e24c8b and 77adcbd; see
`docs/architecture/layout-checkpoint-keyframes.md` and
`docs/architecture/session-database.md` on that branch) show two separate
causes.

1. The browser finds changes by recording everything and comparing. After
   every rendering update in which style or layout work happened, the layout
   hook walks the whole document and computes the recorded computed-style
   values of every element, and the DOM hook walks the document after each
   mutation delivery, up to a limit of 512 nodes, beyond which the checkpoint
   is marked truncated and the remaining nodes are not recorded. The work is
   proportional to the size of the page, not to what changed. In a 216 s recording the renderer's main thread
   spent up to 62% of a 5 s interval in recorder work, and one layout
   checkpoint held it for 259.6 ms; a frame at 60 Hz has about 16.7 ms.
   Computing style values took 12.5 ms per layout checkpoint on average, and
   layout keyframes did not reduce it, because the comparison ran after the
   values were computed.
2. The store splits every object into rows of many tables. Mapping, per-row
   foreign key checks, indexes, and the write-ahead log are paid during
   capture, a reference check across the tables is paid when recording
   stops (12 to 17 s in the measured recordings), and joins are paid when a
   recording is opened. In a 44.4 s recording, the deferred foreign key
   checks alone took 31.3 s summed over the writing connections, 56% of the
   time spent writing batches, and the app used about 136 s of processor
   time. A 216 s recording wrote 4.19 GB of write-ahead log.

Neither cause is solved by recording less. The recorder is an evidence
logger and records everything it finds.

## Decision

1. The browser records what Blink recalculated, not the whole page. Blink
   already visits only the elements whose style must be recalculated and the
   boxes that must be laid out. The recorder notes those nodes as Blink
   processes them and records them once the rendering update ends.
2. The bridge keeps a cache of the latest record of every node of each
   document, records a node only when its new record differs from the cached
   one, and writes a snapshot of the whole cache at a fixed interval, so any
   moment of a recording can be rebuilt from the snapshot before it and the
   changes after it. A snapshot comes from memory and costs Blink nothing.
3. Each recording is stored in its own append-only file of compressed,
   time-indexed chunks of records, with every record's payload kept as the
   bytes the collector sent. PostgreSQL holds the catalog of projects and
   recordings and an index of each recording's chunks and snapshots.

## Where Blink decides what changed

### Style

- `Element::RecalcOwnStyle` (`core/dom/element.cc`, line 5703) computes an
  element's new style, compares it with the old one through
  `ComputedStyle::ComputeDifference` (line 5791), and stores it through
  `SetComputedStyle` (line 5854). `ComputedStyle::Difference::kEqual` means
  the computed style is the same, even if the object is replaced
  (`core/style/computed_style.h`).
- `Element::SetComputedStyle` is an inline setter (`core/dom/element.h`,
  line 996). Every element style assignment passes through it, including
  pseudo-elements (`element.cc` lines 10962 and 11190), column pseudo-elements
  (9491), scroll markers (9516), container query recalculation
  (`core/css/style_engine.cc`, line 3737), and styles ensured for elements
  outside the rendered tree (`element.cc`, line 10512). Some assignments are
  temporary: `core/dom/pseudo_element.cc` sets an adjusted style and later
  restores the original (lines 522 and 528).
- `Text::RecalcTextStyle` (`core/dom/text.cc`, line 429) updates a text
  node's style and text on its layout object.

### Layout

- `LayoutBox::SetCachedLayoutResult` and `LayoutBox::SetLayoutResult`
  (`core/layout/layout_box.cc`, lines 2523 and 2563) store the result of
  laying out a box. `LayoutBox::ReplaceLayoutResult` (line 2620) returns at
  once when the result is the one already stored, which is how a box whose
  cached layout was reused is recognised.
- A box's position is held in its parent's fragment, so a child moved by its
  parent's layout is found through the parent's new result, even when the
  child itself was not laid out again. Text positions are held in the
  fragment items of the block that contains them.

### Scrolling

- `PaintLayerScrollableArea::UpdateScrollOffset`
  (`core/paint/paint_layer_scrollable_area.cc`, line 428) stores a new scroll
  offset and returns at once when the offset is unchanged.
- A scroll handled by the compositor reaches the main thread through
  `ScrollableArea::DidCompositorScroll` (`core/scroll/scrollable_area.cc`,
  line 1112), which calls `SetScrollOffset` with
  `mojom::blink::ScrollType::kCompositor` (line 1148).

### DOM tree

- `ContainerNode::ChildrenChanged` (`core/dom/container_node.h`, line 371)
  is called with a `ChildrenChange` (line 275) for each insertion and
  removal of children. Attribute and character data changes are already
  recorded on the current protocol. Recording insertions and removals lets the DOM be recorded
  in full, as a first walk and then its changes.

## No limit to nodes

Requirement: the recorder records every node of every document, with no
limit to the number of nodes. A checkpoint that stops part way records part
of a page, which is not evidence of the page.

The limits were removed ahead of the browser slices. The DOM checkpoint
stopped at 512 nodes (`kRecorderMaximumDomCheckpointNodes` in
`chromium/integrate.py`), the interaction checkpoint at 512 text controls
(`kRecorderMaximumInteractionTextControls`), and the layout and accessibility
checkpoints at 100,000 nodes (`kRecorderMaximumLayoutCheckpointNodes` and
`kRecorderMaximumAccessibilityCheckpointNodes`). Each bound is now
2147483647, the largest value the protocol's 32-bit counts hold, so a walk
stops only where its count cannot grow. The protocol's `maximumNodes`,
`maximumTextControls`, and `truncated` fields are unchanged. A checkout
patched with the earlier limits has them replaced when `integrate.py` runs.

Until slices 3 to 5, every DOM checkpoint after a mutation delivery and every
layout checkpoint walks the whole document, so recording a large page costs
more than it did with the limits. That cost is what the change-driven design
removes, and it is to be measured on the target machine.

## No limit to content

Requirement: the recorder records every value whole and every attribute,
header, and cookie. A value cut part way records part of what the page held.

The recording measured under "Slice 2 status" (57.2 s, revision 82036fc)
held 807 `dom-checkpoint-node-attribute` records whose value was cut at 4096
UTF-16 code units, and no other cut, from its recording file read with the
Python `mcap` library.

The content limits were removed:

- DOM attribute values in checkpoints and attribute and character-data
  changes (`kRecorderMaximumDomValueLength`), an element's attributes in a
  checkpoint (`kRecorderMaximumDomAttributesPerNode`), text-control values
  in change records and interaction checkpoints
  (`kRecorderMaximumTextControlValueLength` and
  `kRecorderMaximumInteractionValueLength`), and pseudo-element generated text
  (`kRecorderMaximumGeneratedTextLength`), were cut at 4096 code units or 64
  attributes. Each bound is now 2147483647, the largest value the protocol's
  32-bit counts and lengths hold. The `maximum...` and `...Truncated` fields
  are unchanged. A checkout patched with the earlier limits has them replaced
  when `integrate.py` runs.
- Network header lists (`kMaximumNetworkHeadersPerRecord`) and cookie lists
  (`kMaximumCookiesPerRecord`) were cut at 256 entries per record. Each bound
  is now 2147483647.
- WebSocket and EventSource message text, event data, and close reasons were
  kept up to 4096 code units from the first 65536 bytes. The whole message is
  now read and kept, with credentials withheld as before. The payload
  validator no longer rejects a text over 4096 code units.
- A frame on the recorder pipe was at most 4 MiB, and a larger record was not
  sent. The recorder now states a maximum of 2,147,483,591 bytes, the largest
  array .NET allocates, which a frame is read into. The queue of evidence waiting to be written stays at
  64 MiB; it accepts a single larger record once it is empty.

What remains bounded: a record whose serialized form exceeds that maximum is
not sent and is reported lost, as a record over 4 MiB was before. A kept-active
presentation request records at most 16 `DidNotSwap` calls
(`kMaximumPresentationNotSwappedRecords`), with the full count in its
terminal record; this is a limit on records, not on content, and is
unchanged.

Recording values whole makes records larger. What that costs on a large page
is to be measured on the target machine.

## How capture works

1. During style recalculation, layout, and scrolling, the hooks only add the
   node to a per-document set of changed nodes. They compute nothing.
2. When the rendering update ends, where the layout hook runs now
   (`LocalFrameView::UpdateLifecyclePhases`, after the lifecycle observers
   are told the update finished), the recorder reads the final state of each
   noted node: its computed-style values if its style changed, its geometry
   and the layout-dependent style values if its layout result changed, and
   the scroll offset if it scrolled. Style may be recalculated more than once
   in one update, so reading at the end records the state the frame was
   drawn from.
3. The bridge compares each record with its cache and records only the ones
   that differ. A style Blink recalculated to the same values is not
   recorded again.
4. DOM insertions and removals are recorded as they happen. A removed node
   leaves the cache.
5. The first rendering update of a document, and any update after a record
   was lost, walks the whole document as the hook does now, and the result
   is recorded as a snapshot.
6. Every snapshot interval, the bridge records the whole cache as a
   snapshot, from memory.

## Recording file

The recording file is an MCAP file ([MCAP specification](https://mcap.dev/spec)):
records are written in batches called chunks, each compressed, with its start
and end time and a checksum; a message index after each chunk locates each
record by time; and a summary at the end locates every chunk.

MCAP was adopted, rather than a format of the recorder's own with the same
structure, so that a recording can be read and checked with tools the
recorder does not provide. The MCAP project publishes readers for C++, Go,
Python, TypeScript, Swift, and Rust under the MIT license
([foxglove/mcap](https://github.com/foxglove/mcap)), and a command-line tool
that summarizes a file and checks it against the specification
([MCAP CLI](https://mcap.dev/guides/cli)). No .NET library is published by
the project, so the recorder's writer and reader
(`src/Recorder.Database/RecordingFiles`) are written from the specification.
Chunks are compressed with zstd, one of the compressions the specification
names.

How the recorder uses MCAP:

- One file per recording, `recording.mcap`, in the session folder.
- A metadata record named `recording` holds the recording identifier,
  session key, start time, and the clock frequency and origin needed to
  align timestamps.
- One MCAP channel for each recorder channel and collector instance, with
  the collector type, instance, producer version, and capture method as the
  channel's metadata. The MCAP topic is the recorder channel. Messages have
  no MCAP schema; the registry's `json` message encoding
  ([MCAP registry](https://mcap.dev/spec/registry)) is used.
- Each message is the recorder event's whole envelope as JSON, with the
  payload kept as the text the collector sent, and the event key the writer
  gave it. The MCAP log time is the event's session nanoseconds and the MCAP
  sequence is the low 32 bits of the event's sequence.
- A chunk holds the messages of one stream. Within a stream, messages are in
  the order they were accepted; chunks of different streams overlap in time.
  The MCAP CLI's check reports this overlap as warnings, one per message
  whose time is earlier than a message of an earlier chunk, and passes the
  file.
- A chunk is written when it reaches 4 MiB of records, or when 1 s has
  passed since its first record, whichever comes first, so a quiet stream is
  on disk within about a second.
- The writer's rejections and omissions are messages on the topic
  `recorder.writer` in a fourth stream, `recorder`.

The design this section proposed before slice 1:

- Header: format version, recording identifier, and the clock information
  needed to align timestamps.
- Chunks, in three streams, so that reading one does not decompress the
  others:
  - browser: DOM, layout, accessibility, interaction, network, dispatch,
    and snapshots;
  - desktop: keyboard, mouse, UI Automation, foreground windows, collector
    lifecycle, and clock synchronization;
  - media: the events describing desktop frames and audio.
- Each record has a fixed header (stream, channel, event type, schema
  version, sequence, timestamp, payload length) and its payload, kept as the
  bytes the collector sent.
- A per-chunk index, with the snapshots in the chunk marked.
- A summary written when recording stops, listing every chunk with its
  stream, time range, and position, and the channel and schema definitions.
- A footer giving the position of the summary.

The app appends finished chunks and never rewrites earlier bytes. If the app
stops during a recording, every complete chunk remains readable, and a reader
rebuilds the summary by reading the chunks. Desktop frames and audio stay in
media files of their own, referred to by the media stream.

PostgreSQL keeps projects, recordings, their status, the location of each
recording file, and a copy of each recording's chunk and snapshot index. The
recording file is the evidence; the index can be rebuilt from it.

## Questions to settle before the browser slices

- Geometry. The current layout records hold viewport rectangles, which
  change for every node when the page scrolls. Recording positions as Blink
  holds them, relative to the parent fragment, with scroll offsets, keeps
  the recorded evidence proportional to the change, but deriving the
  viewport rectangle at playback also needs transforms and sticky and fixed
  positioning. Whether those can be recorded as changes in the same way, or
  whether viewport rectangles of noted nodes are recorded as well, is to be
  decided from the Blink source.
- Compositor animations. An animation of transform or opacity started on the
  compositor (`Animation::StartAnimationOnCompositor`,
  `core/animation/animation.cc`, line 853) changes what is drawn without a
  main-thread style recalculation each frame. Its start, timing, and
  keyframes may need to be recorded so playback can compute each frame.
- Temporary and ensured styles. Which `SetComputedStyle` calls describe
  rendered state, and which are temporary or outside the rendered tree.
- Skipped subtrees. How content-visibility and display locks, which leave
  subtrees out of style and layout, appear in the changes.
- Cache memory. One record per node of each document per renderer, measured
  on large pages.

## Validation form

Until the change-driven records are shown to be complete, a validation form
walks the whole document at every Nth rendering update, as the hook does
now, compares the walk with the cache, and records every difference with the
node and field. A difference means a change Blink made that the hooks did
not note. The form is off in normal recording.

## Required tests

- Unit: the change cache, comparison, snapshot, and loss handling, rebuilt
  from generated sequences and compared with full states; the recording
  file writer and reader, including a file cut short at every byte of its
  last chunk.
- Integration: the Chromium integration tests of the bridge, and an app test
  that records to a file, closes it, reopens it, and reads back every
  record's payload unchanged.
- System, on the target Windows machine: a recording in the validation form
  with no differences; hover, scroll, animation, and navigation passes; the
  renderer's longest recorder work per update; time to stop; time to open
  and to reach an arbitrary moment; and a recording of at least one hour.

## Slices

1. Recording file: writer and reader, and the app writing every event to the
   file with the catalog and index in PostgreSQL, with no browser change.
2. Playback from the file.
3. Change-driven style and layout capture in Blink, with the validation form.
4. Snapshots from the cache, DOM insertions and removals, and scroll
   offsets.
5. The full walk kept only for a document's first update and after a loss.

## Slice 2 design: playback from the file

Opening a recording reads what the player shows when it opens, not every
event: the file's summary, and one attachment the recorder writes when it
finishes the file. A timeline lookup reads the chunks that can hold its
answer.

### What is read when a recording opens

- The summary: channels and the chunk index. For each chunk, the chunk
  index gives its time range and the position of its message index for
  each channel it holds.
- The attachment `playback-index` (media type `application/json`), written
  after the last chunk and listed in the summary's attachment index. It
  holds what the recorder derives from the events as it writes them:
  - each channel's event count;
  - which time buckets of each channel hold an event, as a bitmap;
  - each desktop frame event: its time, image path, size, and the earliest
    composition time of its monitors;
  - each audio stream start: its time, stream, and path;
  - the browser navigation events, with the properties navigation
    correlation reads;
  - the counts navigation correlation needs from every other browser event,
    described below;
  - the presented layout checkpoints, computed with the joins and
    arithmetic of the database reader's query.

The attachment is derived data. When it is missing, because the recording
was not finished, or when its counts are not exact, the reader derives the
same data by reading every chunk, which takes time proportional to the
recording. It first reads the browser chunks for the navigation starts,
then every chunk, so each browser event is counted in its segment as it is
read and no event is held. The same code derives the index in both cases,
and the tests compare the two.

The writer's rejection and omission records are not timeline events, as in
the database.

### Occupancy buckets

The occupancy grid's bucket width becomes the smallest power of two, in
nanoseconds, for which 262,144 buckets cover the recording. The recorder
does not know the duration until it stops, so it keeps each channel's
bitmap at the smallest power-of-two width that covers the latest time seen,
and merges pairs of buckets when time passes the end. A grid kept this way
equals the grid built at the end. A recording uses more than 131,072
buckets, so at the player's greatest zoom, 32 times on a timeline up to
4,096 pixels wide, a bucket is still no wider than one pixel column. The
same width is used for recordings read from the database.

### Navigation counts

Navigation correlation counts, for each navigation, the DOM and
accessibility checkpoints and nodes, dispatches, listener invocations, and
related records of the navigation's document, from its start until the next
navigation of the same frame or page. Every such window starts at the start
of a navigation and ends at the start of another, or after the last event.
The recorder therefore keeps, for the other browser events, only a count per
segment between navigation starts, for each browser instance, process,
document, document token, event type counted, and truncation. Correlation
reads each count as that many events at the segment's start.

Events arrive nearly in time order, but not exactly: collectors deliver in
batches. The recorder holds each browser event's time for 30 s of recording
time before counting it into its segment. A navigation start that arrives
inside a segment after an event of that segment later than it was counted
makes the counts inexact. The attachment says so, and the reader derives
the counts by reading the chunks instead.

### Timeline lookups

A lookup for the event at or before a time, the next or previous event,
the first or last event, or the nearest event, for a set of channels:

1. Takes the chunks that hold one of the channels, from the chunk index.
2. Visits them from the one whose time range is closest to the answer,
   reads each visited chunk's message index for those channels, and stops
   when no remaining chunk can hold a better answer.
3. Decompresses the chunk that holds the answer and reads that message.

Events with the same time are ordered by event key, as in the database. The
chunks and message indexes most recently read are kept, so stepping through
nearby events reads the file once. At the rate of the recording measured
below, an hour holds about 24,000 chunks, and a lookup walks that list in
memory.

An event's complete record is the message's `event` object as written, with
its payload text unchanged.

### Tests required

- Unit: the index derived while writing equals the index derived by reading
  the file, for events delivered out of time order, and a navigation start
  delivered late enough to make the counts inexact is detected; the
  occupancy bitmap kept at a growing width equals the one built at the end.
- Integration: playback of a recording file matches playback of the same
  events built in memory: every timeline lookup, channel counts, occupancy,
  frames, audio tracks, navigations with their first frames, and every
  complete record. The same for a file cut short inside its last chunk.
- System, on the target Windows machine: time to open, and to reach an
  arbitrary moment, for the recording measured below and for a recording of
  at least an hour.

## Slice 2 status

Implemented. Tested in the sandbox, and in one recording on the target
Windows machine (see "Windows evidence" in this section).

- The recorder writes the `playback-index` attachment when it finishes the
  file. The player opens a recording file from its summary and the
  attachment, and says in its status when the file was read without its
  summary or its index was derived from the chunks.
- A recording made at revision ae28866 or d063eaa has no attachment, so its
  index is derived when it opens.
- The recording file is kept open while its recording is shown, and closed
  when another recording is opened.

Sandbox evidence, 2026-09-28:

- Tests: playback from a recording file equals playback of the same events
  from the database for frames, audio tracks, and navigations with their
  first frames, and equals playback built in memory for the timeline order,
  channel counts, occupancy, and every complete record; 300 random timeline
  lookups over 4,000 events in three streams, with equal times and events
  out of time order, give the answers of the in-memory timeline; the index
  written with the file equals the index derived from its chunks; a
  navigation start delivered 59 s late makes the counts inexact and the
  derived counts equal those built in memory; a file cut short inside its
  last chunk opens with its surviving events; the occupancy bitmap equals
  the grid built from the events at every duration it grows to.
- The Windows recording described under "Windows evidence" below, copied to
  the sandbox, read in a Debug build on the sandbox's two processors: opening
  with its index derived took 8.6 s. The same events written again with the
  index: finishing the file took 97 ms, of which building and writing the
  1.2 MB attachment took 58 ms, and opening took 17 ms. 200 lookups of the
  nearest event, the next event, and its complete record took a median of
  7.9 ms and at most 25.7 ms. `mcap doctor` of MCAP CLI v0.3.0 passed the
  file with its attachment, with the time-order warnings described above,
  and `mcap list attachments` listed the attachment. These are figures from
  one run in the sandbox, not a measurement on the target machine.

Windows evidence, 2026-09-28, revision 82036fc, one recording of 57.2 s with
instrumented Chromium and every collector on:

- 556,276 events accepted and written, none dropped, from `manifest.json`.
- Stopping: finishing the file took 81.1 ms, of which building and writing
  the 1.4 MB playback index took 62.7 ms, and storing the chunk index took
  52.3 ms, from `database-writer-timings.json`.
- `recording.mcap` is 59.8 MB, holding 1.39 GB of records in 417 chunks.
  `mcap doctor` passed it, and `mcap list attachments` lists the playback
  index. The index states exact browser counts.
- The user reported that opening this recording was near instantaneous, and
  that earlier recordings, whose index is derived when they open, took from
  3 to 4 s up to 30 s. Neither was timed by the app.
- The user reported: "What is currently sluggish is the browser changes, as
  expected".

## Slice 1 status

Implemented. Tested in the sandbox, and in one recording on the target
Windows machine (see "Windows evidence" below).

- The app writes every accepted event to the recording file. PostgreSQL
  holds the recording, its collectors and channels, the file's location in
  `recording_files`, and its chunk index in `recording_file_chunks`
  (migration `0013_recording_files.sql`; version 12 was used by the unmerged `layout-keyframes` branch). No event is written to the
  evidence tables.
- The writer keeps its checks, queue, spill file, retries, rejections, and
  omissions, and writes one batch at a time so the file is in the order the
  events were accepted.
- A database outage during recording no longer loses or delays events: the
  file does not depend on the database. What the database could not store
  is reported with the recording's database status.
- The player could not open a recording made this way until slice 2, and
  said so.

Sandbox evidence, 2026-09-28:

- Unit tests of the writer and reader: every event read back with its
  envelope and payload text unchanged, including escapes, a NUL character,
  a duplicate key, and exponent notation; chunks grouped by stream; a file
  cut short at every byte from the start of its last chunk read up to its
  last whole chunk; a chunk or summary whose bytes changed detected; a
  batch written again adding nothing twice; a quiet stream written within
  the chunk interval.
- App tests with a real PostgreSQL server: a recording written to its file
  with its chunk index in the database, and a recording during a server
  outage completing with every event in its file.
- A file of 1,000,000 generated events (788 MB of records, 82 MB on disk)
  passed `mcap doctor` of MCAP CLI v0.3.0 and was summarized by `mcap info`,
  and was read in full by the Python `mcap` library, version 1.5.0. Writing
  it, with encoding and compression, took 17.7 s on the sandbox's two
  processors, about 56,000 events per second. The recordings measured in
  `session-database.md` averaged about 3,600 events per second. These are
  figures from one run of generated events, not a measurement on the target
  machine.

Windows evidence, 2026-09-28, revision ae28866, one recording of 50.5 s with
instrumented Chromium and every collector on, including the node limits
removed:

- 480,867 events accepted and written, none dropped, spilled, or rejected.
- Stopping: finishing the file took 12.7 ms and storing its chunk index
  48.9 ms, from `database-writer-timings.json`. The reference check that took
  12 to 17 s in the measured database recordings is no longer run.
- The writer spent 3.4 s adding events to chunks and 2.2 s compressing and
  writing them, summed over the recording.
- `recording.mcap` is 44.6 MB, holding 1.08 GB of records in 333 chunks.
  `mcap doctor` of MCAP CLI v0.3.0 passed it, with one warning for each of
  471,161 messages whose time is earlier than a message of an earlier chunk
  of another stream, as described above.
- Read with the Python `mcap` library: 191 DOM checkpoints of up to 1,784
  nodes, 118 layout checkpoints of up to 863 nodes, 57 accessibility
  checkpoints of up to 617 nodes, and 309 interaction checkpoints, none
  truncated, each stating the bound 2147483647. 157 of the 191 DOM
  checkpoints hold more than 512 nodes, so under the earlier limit they
  would have been cut.
- The player reports that it does not read recording files yet, as intended
  for this slice.

This is one recording; hover responsiveness was not measured.

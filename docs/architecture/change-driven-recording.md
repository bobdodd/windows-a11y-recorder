# Change-Driven Browser Capture and Recording Files

## Status

Proposed. Nothing in this document is implemented. The Blink locations below
were read from the Chromium checkout on the target Windows machine, version
156.0.8065.0 (`chrome/VERSION`), and must be read again if the checkout
changes.

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
  in full, as a first walk and then its changes, without the 512-node limit.

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

The layout follows the MCAP container specification
([MCAP](https://mcap.dev/spec)): records are written in batches called
chunks, each compressed, with its start and end time and a checksum; a
message index after each chunk locates each record by time; and a summary at
the end locates every chunk. Whether to adopt MCAP itself or a format of the
recorder's own with the same structure is decided in the first slice.

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

# Change-Driven Browser Capture and Recording Files

## Status

Slices 1 (the recording file) and 2 (playback from the file) are
implemented on branch `recording-object-store` and were each tested in one
recording on the target Windows machine; see "Slice 1 status" and "Slice 2
status". The content limits are removed and await their test there; see "No
limit to content". Slice 3 (change-driven style and layout capture,
protocol 0.32) is implemented and was measured in recordings on the target
Windows machine; see "Slice 3 design". Slice 4 (DOM insertions and
removals, and scroll offsets, protocol 0.34) is implemented and was checked
in one recording on the target Windows machine, which found two kinds of
change it did not record; their hooks are implemented and not yet tested
there. See "Slice 4 status". Slice 5 (full walks only where they are
needed, protocol 0.35) is implemented and was measured in two recordings on
the target Windows machine, one with the check setting off and one with it
on. The one rectangle difference that recording found is explained: a
record held one rectangle for a node united from several quads. From
protocol 0.36 the record also holds the bounds of each quad, and one
recording at 0.36 with the check setting on found no difference. See "Slice
5 status". The
Blink locations below were read from the Chromium checkout on the target
Windows machine, version 156.0.8065.0 (`chrome/VERSION`), and must be read
again if the checkout changes.

## Problem

The recorder is slow in use: hovering over a button is sluggish while a page
is recorded, stopping a recording takes noticeable time, and opening one can
take over 10 seconds. Measurements from Windows recordings made on the
`layout-keyframes` branch (revisions 6e24c8b and 77adcbd; see
`docs/architecture/layout-checkpoint-keyframes.md` and
`docs/architecture/session-database.md` on that branch) show two separate
causes. That branch was never merged and was deleted on 2026-09-29, when
this design was merged, so those revisions and that branch's versions of
the two documents may no longer be reachable in the repository; the
measurements this document relies on are restated below.

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
not sent and is reported lost, as a record over 4 MiB was before.

A kept-active presentation request recorded at most 16 `DidNotSwap` calls
(`kMaximumPresentationNotSwappedRecords`), with the full count in its
terminal record. With slice 3 the bound is 2147483647, so every call is
recorded. The recorder's own diagnostic limits, which bound its cost report
and log and not the evidence, are unchanged.

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

- Geometry (settled for slice 3; see "Slice 3 design"). The current layout records hold viewport rectangles, which
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
4. DOM insertions and removals, and scroll offsets.
5. The full walk kept only for a document's first update, a finished parse,
   after a loss, and at the check setting's interval. Snapshots move to the
   app in a later slice.

## Slice 3 design: change-driven style and layout capture

Decided 2026-09-28 from the Blink source of the Chromium checkout on the
target Windows machine, version 156.0.8065.0. Line numbers below are from that
checkout. Protocol 0.32.

### What slice 3 adds, and what it does not change

Slice 3 adds change records to `browser.layout` beside the full layout
checkpoints. The checkpoints are unchanged and are still recorded at every
rendering update whose style or layout counters changed. They are the
reference the change records are checked against: a recording made with
slice 3 holds both, so the state rebuilt from the change records can be
compared with every checkpoint of the same recording.

Recording therefore costs more with slice 3 than before it, not less. The
gain comes when slices 4 and 5 remove the walk from ordinary updates, and
only once the comparison shows the change records complete.

### Geometry

A change record holds the geometry Blink holds, not the rectangle
`getBoundingClientRect` returns. That rectangle is relative to the viewport,
so it changes for every node under a scroller whenever the scroller scrolls,
and for every descendant of an element whose transform changes, although
Blink recalculated neither the node's style nor its layout. Keeping it
current would mean reading every such node again, which is the cost this
design removes.

Blink positions each object relative to a transform node of the paint
property tree: `FragmentData::PaintOffset()` is the offset "from the origin
of the transform node of the fragment's property tree state"
(`core/paint/fragment_data.h`, lines 31 to 35). Scrolling, CSS transforms,
sticky positioning, and fixed positioning are transform nodes
(`platform/graphics/paint/transform_paint_property_node.h`). A scroll
changes one scroll translation node, not the objects under it.

A `layout-node-changed` record states:

- `transformNodeId`: the transform node of the object's local border box
  properties (`FirstFragment().LocalBorderBoxProperties().Transform()`), or,
  for an object without them, of the nearest container that has them, as
  `LayoutObject::GetPropertyContainer` finds it (`core/layout/layout_object.cc`,
  line 2705).
- `localRect`: the rectangle `getBoundingClientRect` is built from, before
  its zoom adjustment (`Element::GetBoundingClientRectNoLifecycleUpdateNoAdjustment`,
  `core/dom/element.cc`, line 3579, and the union of `AbsoluteQuads` for a
  text node, as the checkpoint reads it), mapped from the viewport's
  transform node into the record's transform node with
  `GeometryMapper::SourceToDestinationProjection`
  (`platform/graphics/paint/geometry_mapper.h`, line 43). The bounding box of
  the mapped corners is recorded. When the projection rotates or skews
  (`gfx::Transform::Preserves2dAxisAlignment` is false), each quad
  `Element::ClientQuads` unites is mapped instead and their bounding boxes
  are united: for an SVG element other than the root and a foreign object,
  its object bounding box mapped with `LocalToAbsoluteQuad`, and otherwise
  the object's `AbsoluteQuads`. So
  the rectangle derived back in viewport space is the one Blink united and
  not the bounds of its bounds. `localRectMapped` is false, and the rect is
  null, when the projection is not invertible.
- `localQuadRects` (protocol 0.36): when `Element::ClientQuads` unites more
  than one quad, such as the lines of a text node or the fragments of an
  inline box, the bounding box of each quad mapped into the record's
  transform node, in Blink's order; null otherwise. The quads are read
  whatever the projection, because the record is not repeated when only a
  transform node above the object changes, and a later rotation or skew
  makes the viewport rectangle the union of the bounds of each mapped quad,
  which `localRect` alone does not determine. The union follows
  `gfx::RectF::Union`, which skips an empty rectangle unless the union so
  far is empty.
- `clientRectEmpty`: true, with the rect null, when the rectangle is empty,
  which `getBoundingClientRect` returns without adjusting it.
- `clientRectScale`: the factor `getBoundingClientRect` multiplies by to
  convert to CSS pixels for this object.

A `layout-transform-node` record states one transform node: its identity,
its parent (null at the viewport's node, where every chain the records name
stops), its matrix with its origin applied (`MatrixWithOriginApplied()`, 16 values
in column-major order),
whether it flattens the transform it inherits, whether it is a scroll
translation, and whether it is sticky. A transform node is recorded when a
node record refers to it or to a node below it, and again whenever its state
differs from its last record. Every transform node a document's records have
named is read again at every update of the document, because a compositor
scroll updates a scroll translation without any object being noted.

The viewport's transform node is the transform of the `LayoutView`'s local
border box properties. The `layout-changes-started` record names it, with the
`LayoutView`'s paint offset in it.

The rectangle `getBoundingClientRect` would return is derived at playback:
the local rectangle's corners are mapped up the chain by each node's
`MatrixWithOriginApplied` (`transform_paint_property_node.h`, line 202) to
the viewport's node, the paint offset of the view is subtracted, the
bounding box is taken, and it is multiplied by `clientRectScale`. A derived
rectangle is labelled as derived.

### Noting what changed

Each hook adds a node to a set of noted nodes and computes nothing else, and
only while the recorder is connected (`IsRecorderActive`). A node is noted
when:

- an element's computed style is set: after the calls of
  `SetComputedStyle` in `core/dom/element.cc`, in `RecalcOwnStyle`
  (line 5854), `EnsureComputedStyle` (line 10512), the first-letter and other
  pseudo-element styles (lines 10962 and 11190), and the column and scroll
  marker pseudo-elements (lines 9491 and 9516); and in
  `StyleEngine`'s highlight recalculation for size containers
  (`core/css/style_engine.cc`, line 3737);
- a text node's style is recalculated: `Text::RecalcTextStyle`
  (`core/dom/text.cc`, line 429);
- a box receives a new layout result: `LayoutBox::AppendLayoutResult` and
  `LayoutBox::ReplaceLayoutResult` (`core/layout/layout_box.cc`); with the
  box, every object its fragment items name, since the positions of text and
  inline boxes are held in the fragment items of the block that contains
  them, and every object of the new fragment's child fragments. A child whose
  own layout result is reused can still change: the resolved values of
  `right`, `bottom`, and the margins depend on the size of the box it is
  placed in;
- the pre-paint walk builds an object's paint properties:
  `PrePaintTreeWalk::WalkInternal`, after `UpdateForSelf`
  (`core/paint/pre_paint_tree_walk.cc`, line 685). The walk visits objects
  whose paint properties or paint offset need updating
  (`NeedsTreeBuilderContextUpdate`, line 411), and a change of paint offset
  forces an update of the object's subtree (`paint_property_tree_builder.cc`,
  around line 4190).

The setter in `core/dom/element.h` is not patched, because a change to that
header rebuilds most of Blink. Noting too much costs only a record that is
then found unchanged.

Anonymous layout objects have no node and are not recorded, as in the
checkpoint.

### Recording the changes

The changes are recorded in the existing hook at the end of a rendering
update (`LocalFrameView::UpdateLifecyclePhases`), for every document whose
lifecycle reached paint clean, after its checkpoint. For each document:

- `layout-changes-started`: the change set's identity (`layout-changes-N`),
  the identity of the checkpoint recorded in the same update, or null, the
  viewport's transform node, the view's paint offset, and the frame's layout
  zoom factor. Each node record maps property names to values, so the
  property list is not repeated.
- A `layout-transform-node` record for each new or changed transform node.
- A `layout-node-changed` record for each noted node still connected to the
  document whose record differs from the node's last record: its identity,
  type, and name, its pseudo-element and shadow fields, whether it has a
  layout object and is display locked, its geometry, and its computed style,
  read the same way as the checkpoint reads them.
- `layout-changes-completed`: the counts of nodes noted, nodes recorded,
  noted nodes whose record was unchanged, and transform nodes recorded.

Nothing is recorded for a document with no noted node and no changed
transform node. The bridge keeps a 64-bit hash of each node's and each
transform node's last record, per renderer process, to find unchanged
records. It keeps them for the 64 most recently recorded documents; the next
record of a node of a document it has dropped is sent in full again, so
dropping costs a repeated record and never a lost one.

### Checking the change records

An app test reads a recording file and rebuilds each document's state from
its change records in record order. At each layout checkpoint it compares,
for every node in the checkpoint, the rebuilt computed style with the
checkpoint's, and the derived rectangle with the checkpoint's
`boundingClientRect`, and reports every node that differs or was never
recorded, with the field. The checkpoint of an update is compared with the
state after that update's change set, and a checkpoint whose update recorded
no change set with the state before the document's next record. Rectangle
edges that differ by at most 0.05 CSS pixels are counted as equal, and the
largest difference is reported.

The check is `LayoutChangeCheck` (`src/Recorder.Session/LayoutChangeCheck.cs`),
with the rebuilt state in `LayoutChangeState`. It runs on a recording file as
the test `ChecksTheLayoutChangesOfARecordingFile` when
`RECORDER_LAYOUT_CHANGES_FILE` names the file, and writes its report to
`RECORDER_LAYOUT_CHANGES_REPORT`, or to `layout-changes-report.txt` in the
temporary directory. The derivation composes each chain from the view's node
down, flattening the accumulated transform where a node flattens the
transform it inherits, as `GeometryMapper` does. Its correctness on real
pages is what the check measures.

A node with no change record whose checkpoint record states no layout object,
no computed style, and no display lock is counted as matching: Blink never
styled it (an element in a `display: none` subtree, for example), so it was
never noted, and that is the state no record states. A checkpoint whose
change set had not completed when the records end is not compared, and is
counted.

### First measurement

One recording made on the target Windows machine on September 28, 2026 with
commit e09625a, of pages scrolled and navigated by hand. It had 184 layout
checkpoints and 177 change sets; the last change set was cut off when the
recording stopped, so its checkpoint was not compared. Of 105,189 checkpoint
node records compared, 105,128 matched in every field, 17,348 of them nodes
with no change record, layout object, or style. All 80,868 rectangles
matched; the largest edge difference was 0.000245 CSS pixels.

The 61 nodes that differed all differed in a resolved value that depends on
the containing box's size: `right` (11 of the 20 examples listed), `bottom`
(4), and `margin-right` (5). The nodes had kept their own layout results
while the box they are placed in was laid out again, so no hook noted them.
Commit e09625a noted only the box whose layout result was set; commit
63ef82e also notes the objects of its child fragments.

A second recording, made the same evening with the package of commit
63ef82e, gave the same result: 362 checkpoints and 228,434 node records
compared, every one of 166,904 rectangles matching (largest edge difference
0.00049 CSS pixels), and 66 differences, all in `right`, `bottom`, or
`margin-right`. That build did not contain the fix. The integration writes
the change-set definition into `local_frame_view.cc` only when the file holds
none, and the checkout keeps its patched sources between builds, so the
definition from commit e09625a stayed in place. The integration now replaces
an earlier definition it recognises, as it does for its other helpers.

A third recording, made with the package of commit 484e1c8, which did
contain the fix, had no computed-style differences: 337 checkpoints and
225,675 node records compared, 225,643 matching in every field. The 32 that
differed were two SVG elements (a `g` and its `path`) whose rectangle is
rotated in the view by a running animation. The derived rectangle was about
twice the observed one: the recorder had mapped the bounds of their
viewport rectangle into the rotated space, and the check mapped the bounds
of that back. For an SVG element the quad-by-quad mapping had not applied,
because it was limited to box and text objects. It now maps an SVG
element's object bounding box, as `Element::ClientQuads` does. That change
has not been measured. Three recordings of a few pages are not evidence of
completeness on other pages.

### Limits

- A transform or opacity animation running on the compositor changes what is
  drawn without a main-thread update, so its frames are recorded by neither
  the checkpoint nor the change records.
- A fragmented object (in columns or pages) records the transform node of
  its first fragment, as `getBoundingClientRect` unites the quads of all
  fragments.
- DOM removals are recorded from slice 4, in `browser.dom`; the layout
  state keeps a removed node's last record.
- A change record lost after the bridge has hashed it is not sent again
  until the node changes. Slice 5 handles loss.

## Slice 4 design: DOM insertions and removals, and scroll offsets

Proposed 2026-09-29 and agreed the same day, with snapshots moved to slice 5.
Written from the Blink source of the Chromium checkout on the target Windows
machine, version 156.0.8065.0. Line numbers are from that checkout;
`document.cc` and `element.cc` there already hold the recorder's earlier
patches. Protocol 0.34.

### What slice 4 adds, and what it does not change

Slice 4 records each change to the structure of the DOM as Blink makes it,
and each scroll offset Blink stores, beside the full DOM and layout
checkpoints. As in slice 3, the checkpoints are unchanged and are the
reference: an app check rebuilds each document's DOM from its first
checkpoint and the change records after it, and compares the result with
every later checkpoint of the same recording. Recording costs more with
slice 4, not less, until slice 5 removes the walks.

Attribute and character data changes are already recorded as transitions
(`dom-attribute-changed` and `dom-character-data-changed`). With the
structural changes below, every field a DOM checkpoint records has a change
record, so the check can compare the whole checkpoint.

Every structural record below is a DOM transition: it has a `transitionId`
of the form `dom-transition-N` from the same sequence as the attribute and
character data transitions, and the next checkpoint of its document counts
it in the range of transitions it covers.

Change records are made for a document that is active and no longer parsing
(`!Document::Parsing()`, `core/dom/document.h`, line 1144), while the
recorder is connected. The finished-parsing checkpoint is recorded when the
parsing state becomes `kInDOMContentLoaded` (`core/dom/document.cc`, near
line 8573), before the `DOMContentLoaded` handlers run, so a change made by
a handler follows the checkpoint and is recorded. `HasFinishedParsing()`,
the condition of the existing mutation hook, is false during those handlers
and would miss their changes. The finished-parsing checkpoint is the state
the changes apply to.

### Insertions and removals

Every change to a child list passes through
`ContainerNode::ChildrenChanged` (`core/dom/container_node.cc`, line 1472),
which first calls `Document::NotifyChangeChildren` with the container and
the `ChildrenChange` (`core/dom/container_node.h`, line 275). The recorder's
hook is already in `NotifyChangeChildren`, so the change records are made
there, synchronously, in the order Blink makes the changes. Changes to a
container that is not connected are not recorded: a detached tree is
recorded in full when it is inserted.

- An insertion (`kElementInserted`, `kNonElementInserted`) is recorded as
  `dom-node-inserted` with `insertionKind` `"child"`: the container, the
  inserted node, and its previous sibling as it is when the record is made,
  or null. When several nodes are inserted at once, Blink inserts all of
  them and then calls `ChildrenChanged` once for each in order
  (`ContainerNode::DidInsertNodeVector`, line 427), so the previous sibling
  of each is the node recorded before it. An insertion is not recorded when
  the node is no longer a child of the container by the time the record is
  made; its removal, and its insertion elsewhere, are recorded instead.
- The inserted subtree follows, walked as a checkpoint walks the document:
  `dom-inserted-node` for each node, in the checkpoint's order and with its
  fields; `dom-inserted-node-character-data`; `dom-inserted-node-attribute`
  for each attribute; `dom-inserted-shadow-root` for a hosted shadow root,
  followed by its shadow tree; and `dom-inserted-slot-assignment` for each
  slot, with the assignment as Blink holds it. Each names the insertion by
  `insertionId`, the insertion's `transitionId`. `dom-insertion-completed`
  closes the subtree with its counts. A node moved from one place to another
  is a removal and an insertion, and its subtree is recorded again. Every
  value is recorded whole, with the bound 2147483647 stated, as in the
  checkpoint.
- A removal (`kElementRemoved`, `kNonElementRemoved`) is recorded as
  `dom-node-removed`: the container and the removed node.
- The removal of all children (`kAllChildrenRemoved`, used by
  `textContent` and `innerHTML` assignment, line 1165) is recorded as
  `dom-children-removed`: the container. The removed nodes are listed by
  Blink only for some containers
  (`ChildrenChangedAllChildrenRemovedNeedsList`), so the record does not
  list them; they are the container's children in the rebuilt state.
- `kTextChanged` is not recorded as a structural change: the change to the
  node's data is a character data transition. The transition hook skipped
  every change made by the parser (`CharacterData::ParserAppendData`,
  `core/dom/character_data.cc`, line 79). From protocol 0.34 it records a
  parser change to a connected node once the document is no longer parsing,
  as a `dom-character-data-changed` transition, since the finished-parsing
  checkpoint does not hold it. A parser change during parsing, or to a node
  that is not connected, is still skipped.
- `kFinishedBuildingDocumentFragmentTree` is sent for a fragment built by
  the parser, which is not connected, and is not recorded; the fragment's
  nodes are recorded when they are inserted.

A `ChildrenChanged` override can change the DOM before it calls the base
method. The check reports any checkpoint whose tree differs from the
rebuilt one, which is how such an order would be found.

### Style attributes changed through the CSSOM

A style attribute changed through `element.style` or Blink's own inline
style setters is not written to the attribute when it changes. Both call
`Element::InvalidateStyleAttribute` (`core/dom/element.cc`, line 13392):
`element.style` from `InlineCSSStyleDeclaration::DidMutate`
(`core/css/inline_css_style_declaration.cc`, line 50), and the setters from
`Element::InlineStyleChanged` (line 12713). In `element.cc` it is the only
function that marks the attribute dirty (line 13395). The path of the typed
OM (`element.attributeStyleMap`) was not read, and is covered only if it
calls the same function. Blink writes a dirty attribute when it is next
read, and that write does not call
`DidModifyAttribute`, so the attribute hooks do not see it. The first
Windows recording of slice 4 showed 24 elements whose checkpoint held a
style attribute the change records did not.

While a connected element's changes are recorded, the recorder writes the
attribute after each change, as `getAttribute()` would, and records the
change as `dom-attribute-changed` with the text before and after. The write
is Blink's own lazy write, so it runs no attribute callback and queues no
mutation record. Because the recorder writes the attribute at every change,
the text the element holds before a change is the text before it.
`Element::InlineStyleChanged` (line 12713) reads the held text for its
mutation observers after it calls `InvalidateStyleAttribute`, so within it
the attribute is written, and the change recorded, only after the
observers' record is queued; they see the old value they see without the
recorder. No DOM checkpoint is queued for the change: the change record is
the evidence. The cost is one serialization of the element's inline style
per change, which Blink also pays when a mutation observer watches the
element's style attribute.

### Shadow roots and slots

- A shadow root attached to a connected host is recorded as
  `dom-node-inserted` with `insertionKind` `"shadow-root"`, the host as its
  container and no previous sibling, from `Element::CreateAndAttachShadowRoot`
  (`core/dom/element.cc`, line 6843), after the root is inserted into its
  host. The root and its tree follow as an inserted subtree, the root's
  fields in `dom-inserted-shadow-root`. A shadow root is never detached. A
  root attached to a host that is not connected is recorded with the host's
  subtree when it is inserted.
- A change of a shadow root's reference target is recorded as
  `dom-shadow-root-changed`, with every field of the checkpoint's shadow
  root record, from `ShadowRoot::setReferenceTarget`
  (`core/dom/shadow_root.cc`, line 559).
- `Element::AttachShadowRootInternal` (`core/dom/element.cc`, line 8044) and
  a declarative shadow root (line 7973) set the root's flags, among them
  `availableToElementInternals`, after the root is attached and its
  insertion recorded. The root is recorded again as `dom-shadow-root-changed`
  once they are set. The first Windows recording of slice 4 showed 17
  shadow roots whose checkpoint held `availableToElementInternals` true while
  their insertion held false.
- Slot assignments are recomputed lazily in `SlotAssignment::RecalcAssignment`
  (`core/dom/slot_assignment.cc`, line 232), which appends each assigned
  node to its slot (lines 289 and 312). At its end, every slot of the shadow
  root is recorded as `dom-slot-assignment-changed` with its assigned
  nodes, whether or not they changed. The recorder keeps no copy of a
  slot's previous assignment to compare with, and a recalculation is made
  only when Blink has marked the root for one (line 233). A
  checkpoint reads the assignments as Blink holds them, without a
  recalculation, so the check compares the same state.

### Scroll offsets

Every scroll offset Blink stores passes through
`PaintLayerScrollableArea::UpdateScrollOffset`
(`core/paint/paint_layer_scrollable_area.cc`, line 428), which returns at
once when the offset is unchanged. A scroll handled by the compositor
reaches it through `ScrollableArea::DidCompositorScroll`. The hook, after
the offset is stored (line 454), notes the scroller's node (the document,
for the frame's own scroller) and computes nothing else. At the end of the
rendering update, with the layout change set of slice 3, each noted
scroller still connected is recorded as `layout-scroll-offset-changed`:

- `scrollOffset`, the offset `PaintLayerScrollableArea` holds;
- `webExposedScrollOffset`, the value `scrollLeft` and `scrollTop` divide by
  `effectiveZoom` (`Element::scrollLeft`, `core/dom/element.cc`, line
  2769), which page recreation needs to restore a scrolled element;
- `scrollOrigin`, the position of offset zero;
- `effectiveZoom`, the scroller's effective zoom;
- `scrollTranslationNodeId`, the transform node the offset moves, read in
  the same change set, or null when the scroller has none.

A change set is recorded when only an offset was stored, and its completion
record counts the offsets (`scrollOffsetCount`). The check compares each
record with the last record of its scroll translation node at the end of
the same change set. That translation is the negated scroll position, the
scroll origin plus the offset
(`FragmentPaintPropertyTreeBuilder::UpdateScrollTranslation`,
`core/paint/paint_property_tree_builder.cc`, line 3804).

### Checking the change records

An app check (`DomChangeCheck`) reads a recording file and, for each
document, rebuilds its DOM from a checkpoint and the change records after
it, in record order. At the next DOM checkpoint it compares, for every node,
the parent, the position among its siblings, the type and name,
the attributes, the character data, the shadow root fields, and the slot
assignments, and reports every node that differs, is missing, or is extra,
with the field. The DOM is then taken from that checkpoint, so each
difference is reported in the interval between two checkpoints where it
arose. The DOM change records and the checkpoint of a mutation delivery are
made on the renderer's main thread and sent in that order, so the checkpoint
is compared with the state after every change record before it. A
checkpoint that was cut is not compared, and the rebuilding starts again at
the next whole one. The scroll comparison above is reported with it.

### Required tests

- Unit: rebuilding a DOM from a checkpoint and generated insertions,
  removals, removals of all children, moves, shadow root attachments, and
  slot changes, compared with the expected tree; the check's report of
  each kind of difference; payload validation of each new record type.
- Integration: the Chromium integration tests of each new hook and of the
  bridge functions, and an app test that records the new records to a file
  and reads them back unchanged.
- System, on the target Windows machine: a recording of pages that insert,
  move, and remove content and scroll elements, checked with no
  differences, and the renderer's longest recorder work per update.

## Slice 5 design: full walks only where they are needed

Proposed 2026-09-29 and agreed the same day, with snapshots moved to the app
in a later slice. Two points were decided by the user the same day: a
renderer that connects again walks each of its documents in full, and the
full walks that check the change records are a setting in the app. The
Blink and bridge locations are from the integration script and the bridge in
this repository at revision 66ad1c8. The sections below describe what was
implemented (protocol 0.35), which differs from the proposal in the points
noted.

### What slice 5 changes, and what it does not change

Slices 3 and 4 record every change beside the full walks they were checked
against. The recording at revision f409513 showed that, on the pages visited,
the change records hold what the walks hold, and that the walks, not the
change records, take the renderer's time: 23.1 s of 63.2 s on the busiest
renderer's main thread for the walks, 0.50 s for the change records. Slice 5
stops the walks that the change records make redundant. Every change record
is kept. The evidence recorded that is not a checkpoint (interaction state,
presentation timing, accessibility checkpoints, events, and navigation) is
recorded at least as often as before.

### When a document is walked in full

The bridge decides, per document, whether a DOM or layout checkpoint request
from Blink is walked (`FullWalkSchedule` in
`chromium/recorder_bridge/full_walks.h`). A walked checkpoint's start record
keeps `reason`, the request Blink made, and gains `walkReason`, why it was
walked. The proposal named the reasons `first-change` and `after-loss` as
values of `reason`; `reason` is unchanged instead, and `walkReason` carries
the bridge's decision.

- DOM: a `finished-parsing` request is always walked, since it is the state
  the DOM change records of a document apply to; its `walkReason` is
  `first`, `after-loss`, `check`, or `finished-parsing`. A `post-mutation`
  request is walked when the document has no DOM walk yet (`first`), after a
  loss (`after-loss`), or at the check interval (`check`), and is otherwise
  not walked. The proposal walked a document before its first change was
  recorded. Instead, a change made before the document's first walk is
  recorded as it happens, and the walk at the next mutation delivery, with
  `walkReason` `first`, gives the state later changes apply to.
- Layout: a rendering update whose counters are unchanged is not walked, as
  before. Otherwise the update is walked when the document has no layout
  walk yet (`first`), after a loss (`after-loss`), or at the check interval
  (`check`), and is otherwise not walked.
- After a loss: when a record on `browser.dom` or `browser.layout` could not
  be written, the bridge holds the count for an omission record
  (`HoldOmittedEvidence`), and counts the loss for that channel. A document
  whose last walk on the channel was before the latest loss is walked at its
  next request, with `walkReason` `after-loss`, and the change records
  continue from that walk.
- A renderer that connects again is a new process with new documents, so
  each of its documents is walked at its first request under the rules
  above.

The bridge tracks at most 4,096 documents per renderer and forgets the least
recently used; a forgotten document that makes a request again is walked as
`first`.

### The check setting

The app's browser capture settings gain "Check change records against a
full walk", off by default, and "Walk every N updates", 100 by default, a
whole number from 1. When the setting is on, every Nth mutation delivery of
a document records a `post-mutation` DOM checkpoint, and every Nth rendering
update with changed counters records a layout checkpoint, as the validation
form above describes. The value reaches the bridge in the bootstrap message
as `fullWalkInterval` (0 when off), and each child process reads it from the
child bootstrap. The session manifest's recording configuration holds it as
`browserFullWalkInterval`, so a reader of the recording knows which
checkpoints to expect. `DomChangeCheck` and `LayoutChangeCheck` compare only
where a recording has a checkpoint after the first. A checkpoint walked
after a loss is counted and not compared: the DOM check takes the tree from
it, and the layout check, whose state is rebuilt from change records alone,
compares none of the document's later checkpoints. The DOM check also takes
a checkpoint at a finished parse as the state without comparing it, and keys
each document by its token and its document identity; see "Slice 5 status".

### Presentation timing and interaction state

Both were tied to checkpoints, and would have stopped with them:

- A `presentation-requested` record was made for each completed layout
  checkpoint (`RecorderRequestLayoutPresentation`), and the playback index
  joins presentation feedback to layout checkpoints to place each rendering
  update on the recording's timeline. From protocol 0.35, a rendering update
  that is not walked always records a layout change set, even one with no
  changed records, and the request is made after the change set. The record
  names either `layoutCheckpointId` or `layoutChangeSetId`, and the other is
  null. The playback index joins both. A change set of an update that was
  walked is not presented again, since its checkpoint was.
- An interaction checkpoint follows each DOM and layout checkpoint. From
  protocol 0.35 one also follows each layout change set of an update that
  was not walked, naming it as `sourceChangeSetId` with a null
  `sourceCheckpointId`, and each mutation delivery that is not walked, with
  no source record and the reason `post-mutation`.
- Blink passes the change set's source to the presentation and interaction
  hooks as a sequence number with bit 63 set
  (`kLayoutChangeSetSourceBit`), which the bridge decodes to the change set
  identity.

With the setting off, a document whose rendering updates had been walked at
every update now records presentation requests and interaction checkpoints
at least as often, and an update with changes but no new counters, which
before recorded no checkpoint, also records them.

The database evidence tables are not written for a recording that has a
recording file. Migration 0014 adds their columns for the new fields, so the
database writer still maps every member of every record.

### Snapshots

The agreed design (decision 2 above) had the bridge write a snapshot of its
cache at a fixed interval. The bridge's cache holds a hash of each node's
last record, not the record, so a snapshot from it would need every record
held in each renderer, and would add the snapshot's bytes to the pipe, which
the recording at f409513 showed is already the busiest part of the path: the
writer thread wrote for 44.0 s of 63.2 s. The app receives every record, and
the checks already rebuild a document's state from them. Snapshots are made
by the app, from the records it writes, in a later slice, and slice 5 makes
none.

### Required tests

- Unit: the integration script's tests of each changed hook; the bridge's
  choice of when to walk (first request, check interval, after a loss, a
  finished parse, eviction) from generated sequences; payload validation of
  the new fields; the playback index joining presentation feedback to
  change sets; the checks with walks after a loss.
- System, on the target Windows machine: a recording with the setting off,
  with the renderer's walk time and queue waits compared with the recording
  at f409513, and hover responsiveness reported by the user; and a
  recording with the setting on, checked with no differences.

The proposal also listed an integration test in which the app records with
the setting on; the system recording with the setting on covers it on the
target machine, since the app's browser capture runs only on Windows.

## Slice 5 status

Measured at revision 70d22d4 in recording
20260929-161448-562df6cd19f74ad3b4bb2b72666a77b4 on the target Windows
machine, with the check setting off (`browserFullWalkInterval` 0 in the
manifest). The user reported that the pages were much smoother and faster
than at f409513, and that hover effects were smooth.

The busiest renderer, from its `Recorder evidence cost` lines in
`diagnostics/chromium.log`, compared with the busiest renderer of the
recording at f409513:

| Measure | f409513 (63.2 s) | 70d22d4 (50.1 s) |
| --- | --- | --- |
| Layout walks | 123, 13.29 s, max 355.7 ms | 22, 0.19 s, max 67.8 ms |
| DOM walks | 102, 9.80 s, max 305.9 ms | 26, 0.17 s, max 51.4 ms |
| Queue waits | 131,226, 18.57 s | 0 |
| Writer thread | 446,016 writes, 43.99 s, 845.7 MB | 84,913 writes, 14.15 s, 289.6 MB |
| Layout change records | 322 calls, 0.50 s | 1,428 calls, 0.25 s |
| Interaction checkpoints | 225 | 782 |
| Presentation requests | 123 | 538 |

The two recordings visited pages chosen by the user and are not the same
session, so the table compares the cost of the recorder's work, not the same
page activity. Interaction checkpoints and presentation requests were
recorded more often than before, as the design requires.

The checks of the recording file:

- Layout: 614 change sets and 25 checkpoints, the first walk of each
  document; 2,021 of 2,021 checkpoint nodes equal, largest rectangle
  difference 1.29e-05 CSS px.
- Character data: 67 checkpoints, 5,044 data records, none cut; 56 of 56
  transitions and 5 of 5 checkpoints equal to the rebuilt data.
- DOM: the first run of the check reported 52 differences, in six pairs of
  small documents (`#document`, `HTML`, `HEAD`, `BODY`) of one renderer.
  Two causes, both in the check:
  - Two documents with different document identities (for example
    `dom-document-123` and `dom-document-127`) were recorded under one
    document token in the same process, 3 ms apart. The check keyed a
    document by its token alone and compared one document's walk with the
    other's tree. It now keys a document by its token and its identity.
  - A document whose children were removed (`dom-children-removed` on the
    document node) was then parsed again, and its next finished-parsing
    walk held new `HTML`, `HEAD`, and `BODY` nodes. Structural changes are
    recorded only while a document is not parsing
    (`RecorderRecordsDomChanges` in the integration script), so the parser's
    insertions were not recorded and the finished-parsing walk is the
    document's state. The check now takes a finished-parsing checkpoint as
    the state without comparing it.
  With both corrections, the recording reports no differences: 58
  checkpoints at a finished parse and 9 first walks, none with a previous
  walk of the same document to compare with. Scroll offsets: 120 of 120
  equal. The recording at f409513, checked again with the corrected check,
  still compares 167,005 of 167,005 nodes equal in 140 checkpoints.

With the setting off, the DOM check has nothing to compare, as designed; the
recording with the setting on is the check of the change records.

### With the check setting on

Recording 20260929-162727-a2048fd871de4516905fe46510212bd8, at revision
70d22d4 with `browserFullWalkInterval` 100 in the manifest. The walks, by
the reasons recorded:

| Channel | first | finished-parsing | check | after-loss |
| --- | --- | --- | --- | --- |
| DOM, `finished-parsing` requests | 149 | 1 | 0 | 0 |
| DOM, `post-mutation` requests | 19 | 0 | 2 | 0 |
| Layout, `rendering-update` requests | 71 | 0 | 5 | 0 |

No record was lost, and no push to the queue waited.

- DOM: 2 checkpoints compared with the rebuilt tree, 3,956 of 3,956 nodes
  equal; 150 checkpoints at a finished parse; scroll offsets 113 of 113
  equal.
- Character data: 171 checkpoints, 19,732 data records, none cut; 121 of
  121 transitions and 2,017 of 2,017 checkpoints equal to the rebuilt data.
- Layout: 1,250 change sets and 76 checkpoints. The first run reported 39
  differences:
  - 38 were nodes in closed `details` elements, for example a `DIV` under a
    `DETAILS` in a `DETAILS-MODAL` element. Each had no layout object and no
    computed style, and its checkpoint record stated `displayLocked` true.
    Blink never styled them, so no change record noted them until the
    element was opened. The check counted a node without a change record as
    matching only when it was not display locked. It now counts a display
    locked node with no layout object, no style, and no change record apart,
    as not compared (38 in this recording, none in the recordings at
    70d22d4 with the setting off and at f409513). A display locked node with
    a change record is still compared.
  - 1 was a rectangle difference: the text node "Regular price", 119.516 by
    78 px in its transform node's space, under a transform node whose
    matrix changed at every rendering update. The matrix has 3D terms, but
    maps a point of the plane as a rotation by about 0.3 degrees. The
    checkpoint at a `check` walk observed a height of 78.4004 CSS px and the
    change records derive 78.6151; the position and width agree. Blink
    unites the bounds of each line's rotated quad, whose bottom depends on
    where the last line ends; the record held only the union of the lines,
    whose rotated bottom corner is that of a full-width line. The observed
    height corresponds to a last line about 77.9 px wide; the recording
    holds no line widths to confirm it. The record is not repeated when only
    a transform above the node changes, so any text or inline box of more
    than one line under a later rotation or skew was affected. Protocol 0.36
    records the bounds of each quad (`localQuadRects`, under "Geometry")
    and the check derives the rectangle from them; see "At protocol
    0.36".
  With the display lock correction, 10,469 of 10,470 compared nodes are
  equal, and the largest rectangle edge difference is 0.2147 CSS px, from
  that node.

### At protocol 0.36

Recording 20260929-172645-baa5818d82194001acf0edaa560b335c, at revision
66030a7 with `browserFullWalkInterval` 100 and protocol 0.36 in every
connection. It is a different session from the one above, so its counts are
not a comparison of the same activity. No record was lost, and no push to
the queue waited.

- Every one of the 19,097 `layout-node-changed` records with geometry states
  `localQuadRects`; 682 state the bounds of more than one quad.
- DOM: 4 checkpoints compared with the rebuilt tree, 7,751 of 7,751 nodes
  equal; scroll offsets 396 of 396 equal.
- Character data: 118 checkpoints, 17,548 data records, none cut; 40 of 40
  transitions and 3,852 of 3,852 checkpoints equal to the rebuilt data.
- Layout: 1,506 change sets and 56 checkpoints (50 first walks, 6 `check`
  walks). 8,975 of 8,975 compared nodes are equal; 30 display locked nodes
  without a record were not compared. The largest rectangle edge difference
  is 0.0011 CSS px.
- The `check` walks observed 6 text nodes of two quads under a transform
  chain that rotates or skews. Derived from the bounds of each quad, their
  largest edge difference is 0.0011 CSS px; derived from the union alone,
  as before protocol 0.36, it would have been up to 1.03 CSS px.

The cost log measures the bridge's part of recording a change set
(`RecordBlinkLayoutChanges`: 2,968 calls, 0.675 s in total, at most
16.4 ms), not Blink's reading of each node's quads, so the cost of reading
the quads is not measured.

## Slice 4 status

Implemented. Checked in two recordings on the target Windows machine, at
revisions 90b3b95 and f409513 (see "Windows evidence" in this section). The
hooks for style attributes changed through the CSSOM and for shadow root
flags were added in f409513 after the first found them missing. The record
shapes are unchanged, so the protocol stays 0.34.

Sandbox evidence, 2026-09-29:

- Chromium integration: the integration script's tests of each new hook and
  of the bridge functions pass. The patched Blink files have not been
  compiled; these Blink names were written without their headers and are
  confirmed only by the Windows build: `SlotAssignment::Slots` called inside
  the class, the return type of `GetWebExposedScrollOffset`,
  `LayoutBox::GetScrollableArea`, `ObjectPaintProperties::ScrollTranslation`,
  and `Node::IsShadowRoot`.
- App: payload validation and evidence ingest accept every new record shape
  and reject an undeclared member in each; a recording file returns the new
  records unchanged; `DomChangeCheck` rebuilds generated insertions,
  removals, moves, removals of all children, attribute and character data
  changes, and shadow root attachments to equal the next checkpoint, reports
  each kind of difference, and compares a scroll offset with its scroll
  translation.
- Not measured in the sandbox: the records of real pages and the renderer's
  recorder work per update.

Windows evidence, 2026-09-29, revision 90b3b95, one recording of 66.0 s with
instrumented Chromium and every collector on:

- The Chromium build first failed on a local variable in the bridge that
  shadowed another (revision a59569b); 90b3b95 renamed it and built. The
  five Blink names above compiled.
- `DomChangeCheck`: 196 DOM checkpoints compared with the tree rebuilt from
  the one before and the change records; 243,461 of 243,502 nodes equal in
  every field. The 41 that differ are 24 style attributes changed through
  the CSSOM and 17 shadow roots whose `availableToElementInternals` was set
  after their insertion was recorded; both are described above and
  recorded from the next revision. 56 scroll offsets, each equal to the
  negated translation of its scroll translation node. 1,495 change records
  named a node outside the document's tree, such as an attribute set on an
  element before it was inserted; the element's state arrives with its
  insertion, so the check counts them without comparing them.
- `LayoutChangeCheck`: 192 layout checkpoints, 117,456 nodes, all equal, the
  largest rectangle edge difference 0.000245 CSS px. This is the first
  Windows measurement of the SVG fix in revision b25a54b.
- `DomCharacterDataCheck`: 236 DOM checkpoints; 144,309 of 144,309 text,
  comment, and other character data compared equal to the data rebuilt
  before them. This is the first Windows measurement of protocol 0.33.
- The user reported that hovering over a button is still very slow. The
  renderer's cost log for the busiest renderer, over 60.0 s: 183 full
  layout checkpoints took 17.7 s of its main thread, at most 352.0 ms each;
  179 full DOM checkpoints took 14.9 s, at most 267.7 ms; and 192,698 waits
  for space in the full evidence queue took 23.5 s, at most 26.8 ms each.
  The slice 3 and 4 change records took 0.58 s. The evidence writer thread
  was busy for 57.6 s of the 60.0 s. Slices 3 and 4 add records beside the
  full checkpoints and remove none; slice 5 is the slice that stops them.

Windows evidence, 2026-09-29, revision f409513, one recording of 66.0 s with
instrumented Chromium and every collector on:

- `DomChangeCheck`: 140 DOM checkpoints compared; 167,005 of 167,005 nodes
  equal in every field. The recording holds 21 style attribute changes and
  15 shadow root changes; the recording at 90b3b95 held no style attribute
  changes. 81 scroll offsets, each equal to the negated translation of its
  scroll translation node. 817 change records named a node outside the
  document's tree and were counted without comparison.
- `LayoutChangeCheck`: 200 layout checkpoints, 103,432 nodes, all equal, the
  largest rectangle edge difference 0.000489 CSS px.
- `DomCharacterDataCheck`: 98,705 of 98,705 character data comparisons
  equal.
- The busiest renderer's cost log, over 63.2 s: 123 full layout checkpoints
  took 13.3 s of its main thread, at most 355.7 ms; 102 full DOM checkpoints
  took 9.8 s, at most 305.9 ms; and 131,226 waits for space in the evidence
  queue took 18.6 s. The change records took 0.50 s.
- This is one recording of the pages the user visited. Kinds of change those
  pages did not make are not tested by it. One candidate not yet read: SVG
  attributes changed through their DOM properties, which Blink also marks
  dirty and writes when read (`Element::SynchronizeAttribute`,
  `core/dom/element.cc`, line 2291); no hook covers that write.

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
  (migration `0013_recording_files.sql`; version 12 was used by the
  `layout-keyframes` branch, never merged and since deleted). No event is
  written to the evidence tables.
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

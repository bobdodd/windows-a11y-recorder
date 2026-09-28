# Layout Checkpoint Keyframes and Changes

## Status

Proposed. Nothing in this document is implemented. The measurement below was
taken from one recording; the design is written so the next slices can be
reviewed before any code changes, and it states what must hold before it
replaces the current full checkpoints.

## Problem

Every layout checkpoint records every element, laid-out text node, and
pseudo-element of its document, with geometry and 283 computed-style values
for each element. A checkpoint is recorded for every rendering update in which
style or layout work happened, so a page that changes little between updates
records nearly the same nodes again and again. Layout checkpoint nodes are the
largest single kind of evidence: 143,539 of the 445,875 events in the
recording measured below, about 32%.

The recorder must stay responsive with many long recordings. Any design that
reduces the volume must keep every moment of a recording quick to open, so a
one-hour recording cannot depend on a single baseline at its start.

## Use: recreating the page at any frame

The layout evidence will be used to recreate the web page as it was at any
frame of the recording. That use sets requirements the design must meet,
beyond reducing volume:

1. Exact state. The layout state rebuilt for any checkpoint is exactly the
   state a full checkpoint would have recorded: the same nodes, in the same
   order, with the same values. Order matters, since it is the composed-tree
   order a recreation lays out and paints in.
2. Any moment, quickly. The state at a checkpoint in the middle or at the end
   of a long recording is rebuilt with bounded work, and stepping forward
   frame by frame costs only the changes between frames.
3. Addressed by frame. Every checkpoint, keyframe or change, keeps its start,
   completion, interaction snapshot, and presentation request records, so the
   rendered-frame correlation still leads from a captured desktop frame to the
   checkpoint whose state it could show. Only node rows are left out.
4. Every document. A page is a main document and the documents of its frames,
   each with its own checkpoints. A recreation at a frame needs, for each
   document shown, its latest checkpoint at or before that frame, so each
   document's chain must be rebuildable on its own.
5. Honest gaps. A checkpoint whose chain is broken by a loss is reported as
   not rebuildable, and the next keyframe restores it. No state is guessed.
6. One reading of state. Consumers such as the player, a recreation tool, and
   analysis read rebuilt checkpoints through one interface in the app, and do
   not handle chains themselves.

The layout evidence is only part of what a recreation needs. It joins DOM
structure, attributes, and text from the DOM evidence by node identity, and it
does not carry resources such as images, fonts, or canvas content. Whether the
recorded evidence is enough to recreate a page is not assessed here.

## Measurement

The recording is the Windows run of revision edf2d45 (84.7 s). The query ran
against the stored rows with the database's read-only role.

- 263 layout checkpoints for 23 documents, 143,539 nodes, 546 on average and
  1,586 at most. No checkpoint was truncated at the 100,000-node limit.
- Each of the 240 checkpoints that had a previous checkpoint for the same
  document was compared with that previous checkpoint. Nodes were matched by
  DOM node identifier and pseudo-element type; no identifier and type appeared
  twice in one checkpoint.
- Fields compared: node type and name, whether a layout object was present,
  display lock, whether a rectangle was present, the rectangle, the shared
  computed style, the pseudo-element fields, and the containing shadow root.

| Nodes of the 240 later checkpoints | Count | Share |
|---|---|---|
| Identical in every compared field | 127,206 | 90.0% |
| Rectangle changed | 10,597 | 7.5% |
| Not present in the previous checkpoint | 3,139 | 2.2% |
| Computed style changed | 871 | 0.6% |
| Another field changed | 192 | 0.1% |
| Total | 141,279 | |

A node can change in more than one field, so the changed rows overlap. A
further 122 nodes of previous checkpoints were absent from the next one.
Computed styles were already stored once per distinct style: 1,380 styles
served every node of the recording.

### Limits of the measurement

- One recording of ordinary browsing. A page that animates layout, or a long
  scroll, changes more nodes per checkpoint.
- Matching assumes a DOM node identifier names the same node for its
  lifetime, which is how Blink assigns them; it was not verified separately.
- Rectangles were compared for exact equality. A value that differs in the
  last bit counts as changed, so the share of identical nodes is a lower
  bound for any tolerance an analysis might use.
- Only stored fields were compared. The cost in the browser of producing a
  change record was not measured.

## Design

### Keyframes

A keyframe is a checkpoint recorded in full, exactly as today. The browser
records a keyframe for a document:

- for the document's first checkpoint;
- for the first checkpoint after a navigation or a change of document;
- when the interval since the document's last keyframe has passed;
- after any checkpoint that was truncated at the node limit; and
- after any loss that breaks the chain that the renderer knows of: a record
  the bridge could not queue or write.

A loss after the renderer, such as a record the app could not store, is known
only to the app, which marks the document's chain broken until its next
keyframe.

The interval is a setting in time, not in checkpoints, so the work to open any
moment is bounded however often the page renders. A starting value of 5 s is
proposed and is to be decided by measurement.

### Changes

Every other checkpoint is a change checkpoint. It is compared, node by node,
with the previous checkpoint of the same document, and records:

- every node that is new, as a full node row;
- every node in which any recorded field other than its position differs, as
  a full node row, not only the fields that differ;
- the identifier and pseudo-element type of every node that is no longer
  present; and
- when the nodes' order differs from the previous checkpoint's, or nodes were
  added or removed, the order of every node, as one record listing each
  node's identifier and pseudo-element type in composed-tree order.

A node's position, `nodeIndex`, is not compared node by node. One node added
near the start of a document moves every later node's position, and comparing
positions would record all of those nodes again. The order record states the
positions instead: a rebuilt node's `nodeIndex` is its place in the latest
order record, or in the keyframe's own order when no order record has
followed it. The order record lists about 546 keys for the measured
checkpoints, against 546 full rows in a keyframe.

A change checkpoint's start record names its keyframe and the checkpoint it
was compared with. Its completion record states the number of nodes the
document held, so the rebuilt node count can be checked against it.

Full rows keep the current node table and its columns: a node row reads the
same whether it came from a keyframe or a change. Removals need one new table.
Unchanged nodes are not recorded.

The comparison uses exact equality of every recorded value, so rebuilding a
checkpoint gives exactly the rows a full checkpoint would have recorded. The
evidence the browser captures does not change; only how it is stored does.

### Where the comparison runs

The comparison runs in the renderer, which holds the previous checkpoint of
each document it records. At 546 nodes per checkpoint, keeping each node's
recorded values costs memory in proportion to the document, released when the
document goes away. Comparing in the renderer reduces the records queued,
serialized, written to the pipe, parsed, and stored. Comparing in the app
instead would reduce only what is stored, and would still carry every node
through the pipe.

The comparison does not reduce the traversal: to know that a node is
unchanged, the renderer must still read its geometry and style. The style
reuse, below and in the layout and computed-style evidence model, already
reduced the style reading.

### Rebuilding a checkpoint

To open a checkpoint, the app reads the document's latest keyframe at or
before it and applies each change checkpoint after that keyframe up to the one
requested: new and changed rows replace rows with the same identifier and
pseudo-element type, removed identifiers delete rows, and the latest order
record gives every row its position. The work is bounded by the keyframe
interval, not by the recording's length. An index on the document and
checkpoint order finds the keyframe with one lookup.

The rebuild is one operation of the app's database layer, which returns the
full rows of a checkpoint, the same columns a full checkpoint's rows hold, so
the player, a recreation, and analysis read the same state. Stepping forward
applies the next change checkpoint to the state already rebuilt. A checkpoint
after a broken chain returns no rows and says why.

### Why a change is a whole row

A changed node is recorded with all of its values, not only those that
changed. A rebuilt row is then one row from one checkpoint, so its provenance
is a single record, and a reader never merges values from different times.
The measured change rows are 10% of the nodes, so recording whole rows costs
little over recording only the changed values.

### Comparing in the renderer with the style reuse

The renderer already keeps each element's previous style reading for the
style reuse. The comparison uses the same readings: an element that kept its
style object has equal values that do not depend on layout by construction, so
only its values that depend on layout, its rectangle, and its other fields are
compared. An element with a new style object is compared value by value.

## Expected volume

With a keyframe once in every K checkpoints of a document and a share c of
nodes changing, the stored share of the current node rows is about

\[ \frac{1}{K} + c\left(1 - \frac{1}{K}\right) \]

With the measured \( c = 0.10 \): about 19% for \( K = 10 \) and 13% for
\( K = 30 \), and never below the 10% that changed. In the measured recording
that would remove roughly 115,000 of 445,875 events. Preparing layout node
records took 4.0 s of the 10.5 s the app spent preparing events, and commits
took about 70 ms per thousand events, so both would fall roughly in
proportion. These are estimates from one recording, not measurements of the
design.

## Required before it replaces full checkpoints

- A unit-level test that rebuilding every checkpoint of a recorded sequence
  gives exactly the rows the full checkpoints held, including additions,
  removals, pseudo-elements, shadow trees, and truncation.
- An integration-level test in which the browser records both forms for the
  same page, and every rebuilt checkpoint equals its full checkpoint.
- A system-level Windows recording that measures the volume, the time to open
  checkpoints at the middle and end of a long recording, and the renderer's
  cost, against the current full checkpoints.
- The keyframe interval, set from that recording.

## Implementation slices

Each slice is tested on Windows before the next.

1. Browser: record keyframes and change checkpoints, the removal and order
   records, and a protocol version for them, with a setting that records
   every checkpoint in full as now. Integration-level test that rebuilding
   every checkpoint from the records gives exactly the rows the full
   checkpoints give for the same pages.
2. App: store keyframe and change checkpoints, removals, and order records
   in normalized tables, and rebuild any checkpoint through the database
   layer. Unit-level test of the rebuild, including additions, removals,
   reordering, pseudo-elements, shadow trees, truncation, and loss.
3. Player: open layout state through the rebuild. System-level Windows
   recording that measures the volume, the time to open checkpoints at the
   middle and end, the renderer's cost, and that rebuilt checkpoints equal
   full checkpoints recorded alongside them; the keyframe interval set from
   it.

## Open questions

- Rectangles are relative to the viewport, so a scroll that coincides with
  style or layout work changes every rectangle, and 7.5% of the measured
  nodes changed their rectangle. Whether most of that was scrolling was not
  measured. Recording rectangles relative to the document would change what
  is recorded and is not part of this design.
- A rendering update that only scrolls produces no layout checkpoint, so the
  recorded rectangles do not follow a scroll the compositor handled alone. A
  recreation of a frame during such a scroll needs the scroll offset of that
  frame, which the layout evidence does not record. This is independent of
  keyframes.

- Whether the interval should also bound the number of change checkpoints, for
  a page that renders very often.

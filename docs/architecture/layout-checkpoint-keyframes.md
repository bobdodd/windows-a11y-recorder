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
- after any loss that breaks the chain: a record the bridge could not queue
  or write, or a checkpoint the recorder did not receive in full.

The interval is a setting in time, not in checkpoints, so the work to open any
moment is bounded however often the page renders. A starting value of 5 s is
proposed and is to be decided by measurement.

### Changes

Every other checkpoint is a change checkpoint. It is compared, node by node,
with the previous checkpoint of the same document, and records:

- every node that is new, as a full node row;
- every node in which any recorded field differs, as a full node row, not only
  the fields that differ; and
- the identifier and pseudo-element type of every node that is no longer
  present.

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
unchanged, the renderer must still read its geometry and style. The cost of
the traversal is a separate slice.

### Rebuilding a checkpoint

To open a checkpoint, the player reads the document's latest keyframe at or
before it and applies each change checkpoint after that keyframe up to the one
requested: new and changed rows replace rows with the same identifier and
pseudo-element type, and removed identifiers delete rows. The work is bounded
by the keyframe interval, not by the recording's length. An index on the
document and checkpoint order lets the player find the keyframe with one
lookup.

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

## Open questions

- The measurement compared styles by the stored shared style, which is equal
  exactly when every value is equal; the renderer has no such key and compares
  the values. Whether a cheaper test in the renderer, such as Blink's own
  style object being unchanged, is exact enough is not known.
- Whether the interval should also bound the number of change checkpoints, for
  a page that renders very often.
- How the player shows a checkpoint whose chain is broken by loss before the
  next keyframe.

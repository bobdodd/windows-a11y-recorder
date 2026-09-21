# Accessibility Checkpoint Evidence Model

## Purpose

Protocol 0.17 records the accessibility data that Chromium renderers serialize
for delivery to the browser process. It adds accessibility evidence to the
existing browser, renderer, frame, document, navigation, and DOM correlation
spine.

The initial proof of concept records serialized AX update batches. It does not
claim that each batch is a complete accessibility-tree snapshot. A later
analysis can reconstruct state by applying ordered batches for one correlated
document, provided the archive contains no relevant omission.

## Capture boundary

The integration hook runs in
`RenderAccessibilityImpl::SendAccessibilitySerialization()` after Chromium's
accessibility annotators have added their attributes and before the renderer
sends the updates and events to the browser process.

The recorder launches instrumented Chromium with
`--force-renderer-accessibility` so the proof-of-concept fixture produces
accessibility serialization without depending on an attached assistive
technology.

Each serialization operation emits records on the
`browser.accessibility` channel:

- `accessibility-checkpoint-started`
- zero or more `accessibility-checkpoint-node` records
- `accessibility-checkpoint-completed`

The `reason` is `renderer-serialization`.

## Recorded fields

Every record carries:

- the recorder browser-instance identity;
- the renderer operating-system process identity;
- the renderer process type;
- Chromium's document token; and
- a renderer-scoped accessibility checkpoint identity.

The start and completion records report:

- the number of serialized AX tree updates;
- the number of accompanying AX events;
- the maximum number of nodes the recorder will emit; and
- on completion, the emitted node count and explicit truncation state.

Each node record reports:

- its zero-based index within the recorded batch;
- Chromium's AX node identity;
- the parent AX node identity when the parent occurs in the same batch;
- the associated DOM node identity when Chromium supplies one;
- Chromium's numeric accessibility role;
- Chromium's role name for that role;
- accessible name and description;
- Chromium's readable serialized AX properties; and
- whether the node identity matches `AXTreeData::focus_id` when its serialized
  update carries tree data.

AX node identity `0` is invalid. Negative identities are retained because
Chromium assigns them to generated renderer nodes, including inline text boxes.
The same rule applies to a recorded parent AX node identity.

The readable serialized properties deliberately retain the broader role, state,
attribute, and relationship vocabulary while the structured protocol grows.
They are observed Chromium output, not a recorder interpretation.

## Role identity

The role is recorded twice, and only one of the two is a durable identity.

`role` is the numeric `ax::mojom::Role` value. Its ordinals are assigned by
declaration order in Chromium's accessibility enumeration, so the same number
can denote different roles in different Chromium versions. It is retained
because it is what the renderer held, but it must not be used to identify a role
across versions.

`roleName` is Chromium's own role token for that value, taken from
`ui::ToString(ax::mojom::Role)`. Consumers, verification, and analysis identify
a role by this field.

The serialized properties also contain a role token, because Chromium's
`AXNodeData` debug string emits the role as a bare word following the node
identity, as in `id=32 button COLLAPSED FOCUSABLE`. That string is a diagnostic
representation whose shape Chromium may change at any time. It is not a field
contract, and no consumer should parse a role out of it. The protocol 0.17
reference run failed its accessibility assertion for exactly this reason: the
verifier searched the serialized properties for `role=button`, a form Chromium
never emits, while the correct evidence was present in the node record.

A node record is not emitted without a role name, so a node reaching the archive
with an absent or empty `roleName` is a defect rather than an observation about
the page.

## Correlation rules

An accessibility checkpoint maps to a committed browser navigation only when
all three values match:

1. browser instance;
2. Chromium document token; and
3. renderer operating-system process.

The join is independent of archive arrival order. Browser-process navigation
and renderer-process accessibility records are written concurrently, so either
record may appear first.

Checkpoint identity is renderer scoped. Consumers must not join checkpoints by
the checkpoint string alone. They must include browser instance and renderer
process, and should include the document token.

Parent identity is local to the serialized update batch in protocol 0.17. A
missing `parentAccessibilityNodeId` does not establish that the node is a tree
root. Its parent may be unchanged and therefore absent from that incremental
batch.

Focus identity is also bounded by each serialized update. A false `focused`
value does not establish that the node was unfocused when its update carried no
tree data. Reconstructing current focus across updates requires the later
ordered-state reconstruction slice.

## Volume and limits

The proof of concept permits up to 100,000 node records for each serialization
operation. This is an explicit safety boundary, not a storage optimization. The
completion record reports the limit and whether the recorder reached it.

No deduplication, compression, state coalescing, or archive compaction is
performed by this slice. Demonstrable capture fidelity takes priority over
recording size. Storage optimization belongs to a later archival workflow.

## Supported claims

A complete, correlated checkpoint proves that the renderer serialized the
recorded AX node data for the identified document at that boundary. It can
support observations about the serialized role, name, description, focus
identity when the batch carries tree data, DOM mapping, and readable properties
of the nodes in that batch.

The evidence does not by itself prove:

- that the batch represents the complete accessibility tree;
- that an assistive technology received or announced the update;
- that the browser process accepted the update;
- that the platform accessibility API exposed the same state;
- that an unchanged ancestor or sibling was absent from the tree;
- that accessibility state caused a DOM, navigation, input, or visual change;
- that focus was presented to the user; or
- that the rendered pixels matched the accessibility representation.

Those claims require later state reconstruction, browser-process
acknowledgement, platform accessibility evidence, causal correlation, layout,
paint, or rendered-frame evidence.

## Deterministic validation

The Blink fixture verifier requires:

- at least one renderer accessibility checkpoint correlated with the committed
  main fixture document;
- exact browser-instance, document-token, and renderer-process agreement;
- one completion for each selected start;
- matching start and completion limits, update counts, and event counts;
- a completion node count equal to the emitted node records;
- contiguous node indices within each batch;
- no truncation for the fixture; and
- at least one button node with one of the fixture's known accessible names.

Passing these checks validates the protocol shape, ingest assumptions, and
cross-process correlation for the fixture. It does not convert an incremental
serialization batch into a complete accessibility-tree snapshot.

## Next dependent slices

1. Reconstruct document accessibility state by applying ordered update batches.
2. Record deleted node identities and tree-reset boundaries explicitly.
3. Record browser-process receipt or acknowledgement of renderer updates.
4. Correlate accessibility changes with DOM transitions and event dispatch.
5. Add layout, paint, and rendered-frame checkpoints using the same document
   identity spine.

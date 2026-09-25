# DOM Checkpoint Evidence Model

## Purpose

Protocol 0.12 introduced the first directly observed DOM-state boundary. Blink
records a bounded structural checkpoint after `Document::FinishedParsing`
reaches the parser-stop boundary.

Protocol 0.13 adds coalesced post-mutation checkpoints. A structural child-list
change queues its affected document through Blink's mutation observer agent
microtask machinery. The delivery pass deduplicates documents and emits at
most one checkpoint for each active, parser-complete document represented in
that pass. Page script does not need to create a JavaScript `MutationObserver`.

Protocol 0.14 adds Chromium's document token to every DOM checkpoint context.
The browser process records the same token and the hosting renderer process ID
on committed navigation evidence. This creates an explicit, deterministic join
between renderer `dom-document-N` identity and browser
`document-navigation-N` identity without treating either namespace as globally
stable.

Protocol 0.28 extends the traversal to the composed tree. Every shadow root, of
any mode, is recorded after its host, followed by its shadow tree, and each slot
is recorded with its assigned nodes. The
[shadow DOM and pseudo-element evidence model](shadow-dom-evidence-model.md)
specifies those records.

Protocol 0.29 follows each DOM checkpoint with a snapshot of the document's
focus, selection, active descendant, and text-control state, named by the
checkpoint's identity and recorded in the same synchronous call. The
[interaction-state checkpoint evidence model](interaction-state-checkpoint-evidence-model.md)
specifies those records.

Together, these boundaries provide deterministic document-tree observations
that can be correlated with renderer process and Blink document identity. They
do not claim that a tree was painted, exposed through an accessibility API, or
unchanged after the checkpoint.

## Evidence records

All records use the `browser.dom` channel and renderer-process
`BrowserContext`.

### Checkpoint start

`dom-checkpoint-started` contains:

- `context`: the browser instance, renderer process, and Blink document
  identity, including Chromium's document token;
- `checkpointId`: a process-local opaque checkpoint identity;
- `reason`: `finished-parsing` or `post-mutation`; and
- `maximumNodes`: the maximum number of node records the checkpoint may emit.

### Checkpoint node

`dom-checkpoint-node` contains:

- `context`: the same renderer document context as the checkpoint start;
- `checkpointId`: the checkpoint being streamed;
- `nodeIndex`: a zero-based position in composed-tree preorder traversal;
- `nodeId`: Blink's stable DOM node identity;
- `parentNodeId`: the parent node identity, or null for the document root. A
  shadow root's parent is its host;
- `nodeType`: `document`, `element`, `text`, `comment`, `shadow-root`, or
  `other`; and
- `nodeName`: Blink's node name, such as `#document`, `HTML`, or `BODY`.

The node record does not contain text content, element IDs, classes, attribute
names, attribute values, form values, URLs, styles, geometry, or rendered
pixels.

### Checkpoint completion

`dom-checkpoint-completed` contains:

- `context`: the renderer document context;
- `checkpointId`: the completed checkpoint;
- `reason`: the same `finished-parsing` or `post-mutation` value used by the
  start record;
- `nodeCount`: the number of node records emitted;
- `truncated`: whether additional nodes existed after the limit was reached;
- `maximumNodes`: the limit used by this checkpoint; and
- from protocol 0.16, `coveredTransitionCount`, `coveredTransitionFirstId`, and
  `coveredTransitionLastId`, which state the attribute and character-data
  transitions recorded for this document since its previous completed
  checkpoint. The attribute and text evidence model specifies them; and
- from protocol 0.28, `shadowRootCount` and `slotCount`, the numbers of shadow
  root and slot assignment records the checkpoint emitted.

The current implementation uses a 512-node limit, which from protocol 0.28
covers shadow-tree nodes as well. A truncated checkpoint is
valid evidence of a partial preorder prefix. It must not be interpreted as the
complete document tree.

## Ordering and identity

Node records are emitted synchronously in preorder between the start and
completion records. For a complete checkpoint, `nodeIndex` values are
contiguous from zero through `nodeCount - 1`. Each non-root node identifies its
direct parent by Blink DOM node ID.

Checkpoint IDs are unique only within one Chromium process. Consumers must
correlate them with browser instance ID and renderer process ID. Node IDs are
scoped to the identified Blink document and must not be compared across
documents or browser instances.

The renderer-side `dom-document-N` identity and browser-process
`document-navigation-N` identity remain separate namespaces. Protocol 0.14
maps them only when browser instance ID, Chromium document token, and renderer
process ID all agree. A cross-document commit replaces the active mapping for
its frame. A later checkpoint carrying the prior token is stale and must not be
joined to the replacement document. A same-document commit must retain the
active token, document identity, and renderer process.

## Claims the evidence supports

The evidence can establish:

- that Blink reached the parser-complete boundary for an identified renderer
  document;
- that Blink observed at least one structural child-list change for an active,
  parser-complete document before a `post-mutation` checkpoint;
- that multiple qualifying child-list changes queued before one delivery pass
  were represented by one checkpoint for that document;
- the observed preorder structural prefix at that boundary;
- stable node-to-parent relationships within the checkpoint;
- the observed node type and node name for each emitted node; and
- whether the checkpoint was complete within the configured node limit; and
- which committed browser-process document, page, and frame correspond to the
  renderer document when the complete correlation tuple matches.

## Claims the evidence does not support

The evidence does not establish:

- every individual mutation or mutation record;
- which node operation caused a structural difference;
- an exact mutation count within a coalesced delivery pass;
- attribute or character-data changes that do not change a child list;
- element attributes, DOM text, form values, style, layout, or geometry;
- pseudo-element or isolated-world structure, and slot assignment at a moment
  Blink had not yet recalculated it;
- accessibility-tree state or platform accessibility exposure;
- paint, compositing, presentation, visibility, focus, or user perception;
- load completion or network completion;
- that a structural difference represents a distinct user-visible view.

## Deterministic validation

The reference fixture verifier selects checkpoints whose renderer document
matches the existing listener and dispatch evidence. It requires:

- one correlated parser-complete start and completion pair;
- one later post-mutation start and completion pair;
- distinct checkpoint identities and chronological ordering;
- matching checkpoint, browser instance, renderer process, and document
  identities;
- equal start and completion limits;
- a completion count equal to the emitted node-record count;
- no truncation for the small fixture;
- contiguous preorder indices;
- exactly one root `#document` node with no parent;
- one `HTML` element parented by that document node; and
- one `BODY` element with a recorded parent;
- exactly two additional nodes after the fixture appends one `DIV` containing
  one text node; and
- exactly one new `DIV` node identity in the post-mutation checkpoint.

Passing this fixture validates bounded parser-complete and coalesced
post-mutation structural checkpoints. It does not validate large-document
truncation, attribute or character-data mutation triggers, cross-origin frame
traversal, or sustained high-volume operation.
Shadow roots and slot assignments are validated by the separate shadow DOM
fixture that the
[shadow DOM and pseudo-element evidence model](shadow-dom-evidence-model.md)
describes.

The parser-complete portion was validated on September 19, 2026 at source
checkpoint `79caba1`. The archive contained 2,236 events and 81 artifacts, with
zero dropped records and zero network-service crashes. Ten parser-complete
checkpoints were observed across the documents created during the run. The
checkpoint correlated to the main fixture document contained 36 nodes and was
not truncated. Protocol 0.13 post-mutation validation completed on September
20, 2026, and protocol 0.14 document-token correlation was validated on the
reference Windows platform the same day at revision `4b3d436`, across 9,006
events and 81 artifacts. See the
[dated validation record](../validation/blink-dom-checkpoint-2026-09-19.md).

## Next dependent slices

Protocol 0.14 reference-platform validation is complete, so the next DOM work
should add:

1. bounded attribute and character-data evidence with a privacy policy, which
   is specified in the
   [DOM attribute and text evidence model](dom-attribute-and-text-evidence-model.md);
2. accessibility checkpoints correlated to the same document boundary; and
3. style, layout, paint, and rendered-frame checkpoints as separate evidence
   channels rather than inferred properties of DOM structure.

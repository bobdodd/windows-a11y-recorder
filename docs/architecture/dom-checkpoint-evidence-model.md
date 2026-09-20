# DOM Checkpoint Evidence Model

## Purpose

Protocol 0.12 introduces the first directly observed DOM-state boundary. Blink
records a bounded structural checkpoint after `Document::FinishedParsing`
reaches the parser-stop boundary. This provides a deterministic document-tree
observation that can be correlated with renderer process and Blink document
identity without claiming that the tree was painted, exposed through an
accessibility API, or unchanged after parsing.

This slice is deliberately narrower than a DOM snapshot. It establishes the
streaming contract, stable node relationships, bounded resource use, and
explicit truncation needed before post-mutation checkpoints are added.

## Evidence records

All records use the `browser.dom` channel and renderer-process
`BrowserContext`.

### Checkpoint start

`dom-checkpoint-started` contains:

- `context`: the browser instance, renderer process, and Blink document
  identity;
- `checkpointId`: a process-local opaque checkpoint identity;
- `reason`: `finished-parsing`; and
- `maximumNodes`: the maximum number of node records the checkpoint may emit.

### Checkpoint node

`dom-checkpoint-node` contains:

- `context`: the same renderer document context as the checkpoint start;
- `checkpointId`: the checkpoint being streamed;
- `nodeIndex`: a zero-based position in preorder traversal;
- `nodeId`: Blink's stable DOM node identity;
- `parentNodeId`: the parent node identity, or null for the document root;
- `nodeType`: `document`, `element`, `text`, `comment`, or `other`; and
- `nodeName`: Blink's node name, such as `#document`, `HTML`, or `BODY`.

The node record does not contain text content, element IDs, classes, attribute
names, attribute values, form values, URLs, styles, geometry, or rendered
pixels.

### Checkpoint completion

`dom-checkpoint-completed` contains:

- `context`: the renderer document context;
- `checkpointId`: the completed checkpoint;
- `reason`: `finished-parsing`;
- `nodeCount`: the number of node records emitted;
- `truncated`: whether additional nodes existed after the limit was reached;
  and
- `maximumNodes`: the limit used by this checkpoint.

The current implementation uses a 512-node limit. A truncated checkpoint is
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

The renderer-side `dom-document-N` identity and the browser-process
`document-navigation-N` identity remain separate namespaces in protocol 0.12.
This slice does not claim a direct mapping between them.

## Claims the evidence supports

The evidence can establish:

- that Blink reached the parser-complete boundary for an identified renderer
  document;
- the observed preorder structural prefix at that boundary;
- stable node-to-parent relationships within the checkpoint;
- the observed node type and node name for each emitted node; and
- whether the checkpoint was complete within the configured node limit.

## Claims the evidence does not support

The evidence does not establish:

- the DOM state before parser completion or after later script mutations;
- element attributes, DOM text, form values, style, layout, or geometry;
- shadow-tree, pseudo-element, or isolated-world structure beyond what the
  chosen traversal exposes;
- accessibility-tree state or platform accessibility exposure;
- paint, compositing, presentation, visibility, focus, or user perception;
- load completion or network completion;
- a direct mapping to browser-process navigation document identity; or
- that a structural difference represents a distinct user-visible view.

## Deterministic validation

The reference fixture verifier selects the checkpoint whose renderer document
matches the existing listener and dispatch evidence. It requires:

- one correlated start and completion pair;
- matching checkpoint, browser instance, renderer process, and document
  identities;
- equal start and completion limits;
- a completion count equal to the emitted node-record count;
- no truncation for the small fixture;
- contiguous preorder indices;
- exactly one root `#document` node with no parent;
- one `HTML` element parented by that document node; and
- one `BODY` element with a recorded parent.

Passing this fixture validates the bounded parser-complete structural
checkpoint. It does not validate large-document truncation, dynamic mutation
checkpoints, shadow DOM, cross-origin frame traversal, or sustained
high-volume operation.

The September 19, 2026 reference run passed these checks at source checkpoint
`79caba1`. The archive contained 2,236 events and 81 artifacts, with zero
dropped records and zero network-service crashes. Ten parser-complete
checkpoints were observed across the documents created during the run. The
checkpoint correlated to the main fixture document contained 36 nodes and was
not truncated. See the
[dated validation record](../validation/blink-dom-checkpoint-2026-09-19.md).

## Next dependent slices

The next DOM work should add:

1. a deterministic post-mutation checkpoint trigger;
2. explicit checkpoint-to-navigation document correlation;
3. bounded attribute evidence with a privacy policy;
4. accessibility checkpoints correlated to the same document boundary; and
5. style, layout, paint, and rendered-frame checkpoints as separate evidence
   channels rather than inferred properties of DOM structure.

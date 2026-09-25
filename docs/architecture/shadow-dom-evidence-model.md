# Shadow DOM and Pseudo-Element Evidence Model

## Purpose

Protocol 0.28 extends three existing record families to the composed tree:

- DOM checkpoints record every shadow root, of any mode, with its host and
  options, and every slot with the nodes Blink has assigned to it.
- Layout checkpoints record the elements and laid-out text inside shadow trees
  and every pseudo-element Blink has created, with its generated text.
- Dispatch paths record, for each path entry, the tree scope Blink dispatched
  it in and the part of the path a listener in that scope sees.

Before protocol 0.28 each family covered the light DOM only. A page built from
custom elements, and every form control, which Blink renders through a
user-agent shadow root, was therefore only partly recorded.

The records are evidence. The recorder does not interpret them, compare them
with any expectation, or flag any value.

## Shadow roots in DOM checkpoints

The DOM checkpoint traversal, shared by the `finished-parsing` and
`post-mutation` checkpoints, visits the composed tree in preorder: each node,
then the shadow root it hosts and that shadow tree, then its own children. Open,
closed, and user-agent shadow roots are all visited. A shadow root is recorded
as a `dom-checkpoint-node` with `nodeType` `shadow-root`, node name
`#document-fragment`, and its host as `parentNodeId`, so the node records alone
give the composed structure.

Each shadow root is followed by a `dom-checkpoint-shadow-root` record:

- `nodeId`: the shadow root's DOM node identity.
- `hostNodeId`: the host element's DOM node identity.
- `mode`: `open`, `closed`, or `user-agent`.
- `delegatesFocus`, `clonable`, `serializable`: the options the root was
  attached with.
- `slotAssignment`: `named` or `manual`.
- `declarative`: whether the root was created from a declarative
  `<template shadowrootmode>`.
- `availableToElementInternals`: whether the host's `ElementInternals` can
  reach the root.
- `referenceTarget`: the root's reference target, or null when none is set.

Each slot inside a shadow tree is followed by a
`dom-checkpoint-slot-assignment` record:

- `nodeId`: the slot's DOM node identity.
- `assignedNodeIds`: the assigned nodes in order, as DOM node identities. An
  entry is null for a node Blink has not given an identity.
- `assignedNodeCount`: the number of assigned nodes Blink held.
- `assignedNodesTruncated` and `maximumAssignedNodes`: the list is limited to
  512 entries.
- `assignmentCurrent`: false when Blink had marked the shadow root's slot
  assignment for recalculation.

`dom-checkpoint-completed` adds `shadowRootCount` and `slotCount`, the numbers
of shadow-root and slot-assignment records the checkpoint emitted.

Slot assignment is read as Blink holds it. Blink recalculates assignment
lazily, before style recalculation or when script asks for it, and the recorder
never requests the recalculation, because doing so would change the state being
recorded. A checkpoint taken between a change and the next recalculation
therefore reports `assignmentCurrent` false, and its assigned list is the one
from before the change. Consumers must not treat such a list as the current
assignment.

## Shadow trees and pseudo-elements in layout checkpoints

The layout checkpoint traversal visits the same composed tree: each node, then
its pseudo-elements, then the shadow tree it hosts, then its own children.
Shadow roots are not recorded as layout nodes, since they have no box, but they
are counted.

Every layout node record carries:

- `shadowHostNodeId` and `shadowRootMode`: the host and mode of the shadow tree
  that contains the node, or null for a node in the document tree.
- `pseudoElement`: null for an element or text node.

A pseudo-element is recorded with `nodeType` `pseudo-element`, its DOM node
identity, and its node name, such as `::before`. Its `pseudoElement` member
holds:

- `originatingNodeId`: the element, or the enclosing pseudo-element, that the
  pseudo-element belongs to.
- `pseudoType`: the pseudo-element's name, such as `::before`, `::after`,
  `::marker`, `::first-letter`, `::backdrop`, `::scroll-marker`, a scroll
  button, a column, or a view-transition pseudo-element.
- `generatedText`: the text laid out inside the pseudo-element, after
  `text-transform`, including the text of nested pseudo-elements.
- `generatedTextLength` and `generatedTextTruncated`: the text is limited to
  4096 characters.

Only pseudo-elements Blink has already created are recorded. The traversal
creates none, so a pseudo-element whose style gives it no content is absent.
Pseudo-elements carry the geometry and computed styles an element record
carries.

`layout-checkpoint-completed` adds `pseudoElementCount` and `shadowRootCount`,
the numbers of pseudo-element records emitted and shadow roots traversed.

## Dispatch path scopes

Blink dispatches an event along one composed path and gives each listener a
view of it that depends on the listener's tree scope: nodes inside a closed
shadow tree the listener cannot see are removed, and targets are retargeted to
the nearest visible host. Protocol 0.28 records that per-scope view.

`dispatch-started` adds `pathScopes`, one entry for each `composedPath` entry,
in the same order:

- `treeScopeRootNodeId`: the document or shadow root whose tree scope Blink
  dispatched the entry in.
- `shadowRootMode`: `open`, `closed`, or `user-agent` for an entry in a shadow
  tree, null in the document tree.
- `targetNodeId` and `relatedTargetNodeId`: the target and related target a
  listener at that entry sees, after retargeting, or null when there is none.
- `visiblePathIndexes`: the indexes into `composedPath` of the entries
  `composedPath()` returns to a listener at that entry.
- `unmatchedVisibleTargetCount`: the number of visible targets that matched no
  recorded path entry.

The window entry is scoped to the top of the path. The visible indexes are the
values Blink computes for `composedPath()`. Blink caches them per tree scope,
and the recorder asks for the same cache a listener's call fills, so recording
them does not change what the page's own calls return.

`composedPath` itself remains Blink's full path, including nodes inside closed
shadow trees that no page listener outside them can see.

## Limits

- The DOM checkpoint's 512-node limit now covers shadow-tree nodes, so a page
  whose controls have large user-agent shadow trees reaches the limit sooner.
- A pseudo-element has a DOM node identity in layout records but does not
  appear in DOM checkpoints, since it is not a DOM node.
- Slot assignment is not recalculated for recording, so an assignment can be
  stale, as described above.
- Shadow roots inside worker scopes do not exist, and event targets that are not
  nodes, other than the window, are outside this slice.

## Supported claims

The records support these statements about a recorded session:

- At a recorded DOM checkpoint, the named element hosted a shadow root of the
  recorded mode and options, and the shadow tree held the recorded nodes.
- At a recorded DOM checkpoint whose slot record reports a current assignment,
  the named slot was assigned the recorded nodes in the recorded order.
- At a recorded layout checkpoint, the named node inside a shadow tree, or the
  named pseudo-element, had the recorded geometry, styles, and generated text.
- For a recorded dispatch, a listener at a given path entry saw the recorded
  target, related target, and composed path.

They do not support statements about the page's reasons for a shadow tree, the
text of a pseudo-element that was not laid out, or slot assignment at a moment
no checkpoint recorded.

## Deterministic validation

The required test levels are:

- Unit tests of the integration script: the DOM, layout, and dispatch hooks
  apply once and are idempotent, earlier light-tree hooks are migrated in
  place, the hooks call the bridge with the declared arities, and the hooks
  contain no call that forces slot assignment, style, or layout.
- Contract tests of the recorder: every record shape the bridge writes is
  accepted, an undeclared member is rejected at ingest and by archive
  validation, and archive validation rejects a slot record whose count and list
  disagree, a pseudo-element record without its pseudo-element member or with
  inconsistent generated-text lengths, a node whose shadow host and mode
  disagree, counts above the node count, and path scopes whose length or
  indexes do not match the path.
- An end-to-end run of the instrumented browser: the validation run serves a
  fixture page from the loopback HTTP listener in a foreground tab. The page
  attaches an open shadow root with a named and a default slot and `::before`
  content, a closed shadow root with manual slot assignment that delegates
  focus, and holds an input, a paragraph with `::before` and `::after` content,
  and a list item with a marker. After two animation frames it appends a text
  node, which makes a post-mutation checkpoint, reads back each slot's assigned
  node count, and then clicks a button inside the closed shadow root, with
  listeners on the button, its host, and the document reporting the length of
  the composed path each saw. The verifier requires, for the fixture document,
  that every DOM checkpoint's counts match its records, that the
  finished-parsing checkpoint holds the three shadow roots with their modes,
  options, and parents, that some checkpoint holds all three slots with a
  current assignment of the expected nodes, that a layout checkpoint holds the
  four pseudo-elements with their originating elements and text and holds nodes
  in the open, closed, and user-agent shadow trees, and that the dispatch's
  path scopes give each listener the scope, target, and visible path length the
  page reported.

The fixture shows that the logger emits records; it does not evaluate the
page's use of shadow DOM or generated content.

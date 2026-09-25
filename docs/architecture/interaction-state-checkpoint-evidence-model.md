# Interaction-State Checkpoint Evidence Model

## Purpose

Protocol 0.24 records changes to focus, selection, active descendant, and
text-control state as they are committed. Reconstructing the state at a given
moment from those records depends on every change having been recorded, and
the change records do not cover every way the state can move. A selection
Blink adjusts after a mutation without a call to `FrameSelection::SetSelection`
is outside them, and so is the value a control holds before its first recorded
change.

Protocol 0.29 records the interaction state Blink holds for a document at each
DOM checkpoint and each layout checkpoint. A snapshot states the state
directly, so a later analysis can read it at a checkpoint without replaying
change records, and can check a replay against it.

The records are evidence. The recorder does not interpret them, compare them
with any expectation, or flag any value.

## Capture boundary

A snapshot is recorded immediately after the checkpoint it belongs to has
emitted its completion record, in the same synchronous call, so no page script
and no other Blink task runs between the two:

- after every DOM checkpoint, whose reason is `finished-parsing` or
  `post-mutation`, from the helper in
  `third_party/blink/renderer/core/dom/document.cc`; and
- after every layout checkpoint, whose reason is `rendering-update`, from the
  helper in `third_party/blink/renderer/core/frame/local_frame_view.cc`.

A checkpoint the bridge declined to start produces no snapshot. In particular,
a rendering update in which Blink resolved no style and performed no layout
produces neither a layout checkpoint nor a snapshot.

Accessibility checkpoints do not carry a snapshot. Their hook runs in the
content layer on the serialized accessibility tree rather than on the Blink
document, and the serialized tree data already reports its focus identity.

The snapshot reads only state Blink already holds. It never requests a style
recalculation, a layout, or a lifecycle update. Blink reads a focused text
control's selection offsets under a scope that forbids lifecycle transitions,
and reads an unfocused control's offsets from the control's cached values.

Each snapshot emits records on the `browser.interaction` channel:

- `interaction-checkpoint-started`
- zero or more `interaction-checkpoint-text-control` records
- `interaction-checkpoint-completed`

## Recorded fields

Every record carries the document context the DOM and layout checkpoint
records use: the recorder browser-instance identity, the renderer process, the
document's DOM node identity as `documentId`, and the document token.
`executionWorldId` is always null, because no script is running when the
snapshot is taken. Every record also carries the snapshot identity,
`interaction-checkpoint-N`, which is unique within one renderer process.

### Snapshot start

- `sourceCheckpointId`: the DOM or layout checkpoint the snapshot belongs to,
  such as `dom-checkpoint-4` or `layout-checkpoint-9`.
- `sourceChannel`: `browser.dom` or `browser.layout`, the channel of the
  source checkpoint.
- `reason`: the source checkpoint's reason, `finished-parsing`,
  `post-mutation`, or `rendering-update`.
- `documentHasFocus`: the value `document.hasFocus()` would return, which is
  whether the document's frame is focused within a focused page.
- `focusedNodeId`: the element Blink holds as the document's focused element,
  or null. This is the element itself, which may be inside a shadow tree. It is
  not retargeted as `document.activeElement` is; the DOM checkpoint's shadow
  tree records give the hosts needed to retarget it.
- `focusVisible`: whether the focused element matches `:focus-visible` by the
  rule Blink's selector matching applies. False when no element is focused.
- `activeDescendantNodeId`: the element the focused element's
  `aria-activedescendant` resolves to, through either the content attribute or
  an element set by reflection, or null.
- `lastFocusType`: Blink's record of how focus last moved in the document,
  using the focus type names of `focus-changed`: `none`, `script`, `forward`,
  `backward`, `spatial-navigation`, `mouse`, `access-key`, or `page`.
- `selectionType`: `none`, `caret`, or `range`, for the selection of the
  document's frame.
- `anchorNodeId`, `anchorOffset`, `focusNodeId`, and `focusOffset`: the
  selection's anchor and focus container nodes and offsets in the DOM tree,
  as `selection-changed` reports them. All four are null when the selection
  type is `none`. Inside a text control they are nodes of the control's
  user-agent shadow tree.
- `directional`: whether the selection is directional.
- `maximumTextControls`: 512.
- `maximumValueLength`: 4096.

### Snapshot text control

One record for each text control in the document, visited in the composed-tree
order the DOM checkpoint uses, including controls inside shadow trees of any
mode. A text control is an `input` whose type presents a text field, or a
`textarea`.

- `textControlIndex`: the control's position in the snapshot, from zero.
- `nodeId`: the control's Blink DOM node identity.
- `controlType`: the form control type, such as `text`, `password`, `search`,
  or `textarea`.
- `value`, `valueLength`, and `valueTruncated`: the control's current value,
  bounded to 4096 UTF-16 code units, with its full length and whether it was
  cut. Values are recorded verbatim, including the values of password fields,
  under the policy that already applies to text-control change records, DOM
  attribute values, and character data.
- `selectionStart`, `selectionEnd`, and `selectionDirection`: the control's own
  selection, as `selectionStart`, `selectionEnd`, and `selectionDirection`
  report it to script. An unfocused control reports the selection it keeps for
  when it is next focused.

### Snapshot completion

- `textControlCount`: the number of text-control records emitted.
- `truncated`: whether the traversal stopped at `maximumTextControls`.
- `maximumTextControls`: 512.

## Correlation rules

A snapshot joins to its source checkpoint by browser instance, renderer
process, document token, and `sourceCheckpointId`. Checkpoint identities are
renderer-process scoped, so the checkpoint string alone is not a join key.

A snapshot maps to a frame and a committed navigation through its source
checkpoint's document token, by the rule the DOM checkpoint evidence model
defines: browser instance, document token, and renderer process must all
agree. A snapshot carrying a token whose document has been replaced in its
frame is stale and must not be joined to the replacement.

Node identities are the Blink DOM node identities the DOM checkpoint,
mutation, layout, and interaction change records carry, and are scoped to the
identified document.

## Claims the evidence supports

A complete snapshot can establish, for the identified document at the moment
its source checkpoint completed:

- which element Blink held as focused, whether it matched `:focus-visible`, and
  what its active descendant resolved to;
- whether the document had focus;
- the selection of the document's frame, as DOM positions; and
- the value and selection of every text control, within the stated bounds.

## Claims the evidence does not support

The evidence does not by itself establish:

- the state between two snapshots, which the change records cover only for
  the changes they record;
- that focus or a focus indicator was painted or presented to the user;
- that an assistive technology received the focus or selection;
- the selected text, which is not recorded;
- that a truncated value, or a truncated set of text controls, is complete;
- the state of editable content that is not a text control, such as a
  `contenteditable` element, beyond the frame selection's positions in it; or
- the state of a document for which no checkpoint was recorded, such as one
  whose rendering is throttled.

## Volume and limits

One snapshot is recorded for each DOM and layout checkpoint. The text-control
traversal is bounded at 512 controls and each value at 4096 UTF-16 code units.
No deduplication is performed: consecutive snapshots with identical state are
all recorded, because the snapshot's value is that it states the state at its
checkpoint.

## Deterministic validation

The Blink validation run's interaction fixture ends with a step that focuses
the listbox, whose active descendant the fixture has already set, changes the
listbox's inline outline offset so the next rendering update resolves style,
and waits for two animation frames while the listbox keeps focus. The verifier
requires, for the fixture document:

- a snapshot for the document's `finished-parsing` DOM checkpoint, which
  reports no focused element and both text controls with empty values;
- a snapshot for a layout checkpoint, recorded after that step, whose focused
  node and active descendant are the listbox and the element the fixture
  assigned by reflection, and whose text controls report the values the
  fixture set and typed, `set by script` and `nXs set by script`;
The verifier also requires, for every snapshot in the archive, not only the
fixture document's:

- that its source checkpoint is present with the same browser instance,
  renderer process, document identity, and document token, and that the
  snapshot's reason matches it;
- that no record of it reports an execution world; and
- exactly one completion for its start, with matching limits, a text-control
  count equal to the emitted records, contiguous indices, and no truncation.

Passing these checks shows that the logger emits snapshots with the stated
shape and correlation for the fixture. It does not evaluate the page's focus
or text handling.

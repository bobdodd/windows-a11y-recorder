# Layout and Computed-Style Checkpoint Evidence Model

## Purpose

Protocol 0.25 records the layout geometry and a defined list of computed styles
that Blink holds for a document after a rendering update. The records add
geometry and style to the existing browser, renderer, document, and DOM
correlation spine, so a later analysis can say where an element was drawn and
how it was styled at a known point in the session.

The records are evidence. The recorder does not interpret them, compare them
with any expectation, or flag any value.

## Capture boundary

The hook runs in `LocalFrameView::UpdateLifecyclePhases()` in
`third_party/blink/renderer/core/frame/local_frame_view.cc`. When a lifecycle
update targeted the paint-clean state, Chromium notifies its lifecycle
observers that the update finished. Immediately after that notification loop,
the hook visits every local frame view that is not throttled and records a
checkpoint for its document when all of the following hold:

- the document is active and has a layout view;
- the document's lifecycle is in the paint-clean state, so style and layout are
  current; and
- Blink has resolved style for at least one element, or performed at least one
  layout, since the previous checkpoint recorded for that document.

The last condition uses two counters Blink already keeps: the style engine's
count of element style resolutions and the frame view's layout count. The
first checkpoint for a document is always recorded. A later rendering update
in which neither counter changed produces no checkpoint, so an idle page, or a
page that only scrolls or repaints, does not repeat identical records.

The hook reads only the style and layout Blink has already produced. It never
requests a style recalculation, a layout, or a lifecycle update, so recording
cannot change when the page's own style and layout work happens. The
integration tests reject a hook that contains a forcing call.

Each checkpoint emits records on the `browser.layout` channel:

- `layout-checkpoint-started`
- zero or more `layout-checkpoint-node` records
- `layout-checkpoint-completed`

The `reason` is `rendering-update`: a lifecycle update that targeted the
paint-clean state, whatever requested it.

## Recorded fields

Every record carries the document context the DOM checkpoint records use: the
recorder browser-instance identity, the renderer process, the document's DOM
node identity as `documentId`, and the document token. `executionWorldId` is
always null, because no script is running when the hook runs. Every record
also carries the checkpoint identity, `layout-checkpoint-N`, which is unique
within one renderer process.

### Checkpoint start

- `previousCheckpointId`: the document's previous layout checkpoint, or null
  for the first.
- `styleResolutionCount` and `layoutCount`: the two cumulative counters named
  above, saturated at the largest 32-bit signed integer.
- `viewport`: the viewport width and height in CSS pixels, as media queries
  see it, including any scroll bar.
- `scrollOffset`: the layout viewport's scroll offset in CSS pixels.
- `devicePixelRatio` and `layoutZoomFactor`: the frame's device pixel ratio
  and the zoom factor Blink applies to layout, which together relate CSS
  pixels to device pixels.
- `maximumNodes`: 100000.
- `styleProperties`: the computed-style properties every element record
  reports, in recorded order.

### Checkpoint node

Nodes are visited in light-DOM tree order from the document. An element is
always recorded. A text node is recorded only when it has a layout object.
Comments, processing instructions, and the document node are not recorded.

- `nodeIndex`: the node's position in the checkpoint, from zero.
- `nodeId`: the Blink DOM node identity, the same identity the DOM checkpoint,
  mutation, and interaction records carry.
- `nodeType`: `element` or `text`.
- `nodeName`: the DOM node name.
- `layoutObjectPresent`: whether the node has a layout object, which is to say
  whether Blink generated a box for it.
- `displayLocked`: whether an ancestor's display lock, such as
  `content-visibility: hidden` or an unrevealed `hidden="until-found"`
  element, prevents layout of the node.
- `boundingClientRect`: for a node with a layout object, the rectangle in CSS
  pixels relative to the viewport. For an element it is the value
  `getBoundingClientRect()` returns, read without a lifecycle update. For a
  text node it is the union of the text's layout quads, adjusted for scroll and
  zoom in the same way. Null for a node without a layout object.
- `computedStyle`: for an element with a current computed style, an object
  holding each listed property's resolved value as the CSS text
  `getComputedStyle()` would report, in list order. A property Blink produced
  no value for is present with a null value. Null for a text node, for an
  element with no computed style, and for an element whose style was computed
  only on demand inside a `display: none` subtree.

### Checkpoint completion

- `nodeCount`: the number of node records emitted.
- `truncated`: whether the traversal stopped at `maximumNodes`.
- `maximumNodes`: 100000.

## Recorded computed-style properties

The list is fixed in the integration script, recorded in every checkpoint
start, and checked by the integration tests against this document and the
verifier. It holds longhand properties only, each supported by the reference
Chromium without a runtime flag, so every value is available on every element
Blink styles.

| Properties | Recorded because |
| --- | --- |
| `display`, `visibility`, `opacity`, `content-visibility` | They determine whether a box is generated, drawn, or skipped. |
| `position`, `top`, `right`, `bottom`, `left`, `z-index`, `float` | They determine how a box is placed and stacked relative to others. |
| `box-sizing`, `width`, `height`, `min-width`, `min-height`, `max-width`, `max-height` | They state the sizes the page asked for, which the rectangle alone does not show. |
| `overflow-x`, `overflow-y`, `clip`, `clip-path`, `text-overflow` | They determine whether content outside a box is clipped, hidden, or elided. |
| `transform`, `filter` | They change where and how a box is drawn without changing its layout. |
| `margin-top`, `margin-right`, `margin-bottom`, `margin-left` | They give the spacing around a box. |
| `padding-top`, `padding-right`, `padding-bottom`, `padding-left` | They give the spacing inside a box. |
| `border-top-width`, `border-right-width`, `border-bottom-width`, `border-left-width` | They give each border's thickness. |
| `border-top-style`, `border-right-style`, `border-bottom-style`, `border-left-style` | They give whether each border is drawn and how. |
| `border-top-color`, `border-right-color`, `border-bottom-color`, `border-left-color` | They give each border's color. |
| `outline-style`, `outline-width`, `outline-color`, `outline-offset` | They describe an outline, which is drawn outside layout and is commonly used as a focus indicator. |
| `box-shadow`, `text-shadow` | They describe drawn effects that can serve as boundaries or indicators. |
| `color`, `background-color`, `background-image` | They give text and background colors, and whether an image lies behind the text. |
| `font-family`, `font-size`, `font-weight`, `font-style`, `line-height` | They describe the text's face, size, and line spacing. |
| `letter-spacing`, `word-spacing`, `text-transform`, `text-decoration-line`, `text-align`, `text-indent` | They describe how text is spaced, cased, decorated, and aligned. |
| `white-space-collapse`, `text-wrap-mode` | They determine how white space is kept and whether text wraps. |
| `direction`, `writing-mode` | They give the text direction and orientation. |
| `cursor`, `pointer-events` | They describe the pointer presentation and whether a box receives pointer events. |
| `animation-name`, `animation-duration`, `transition-property`, `transition-duration` | They show whether a box was animating or transitioning, which explains checkpoints recorded frame by frame. |

Colors are reported as Blink resolves them, normally in `rgb()` or `rgba()`
form. Lengths are reported in CSS pixels where Blink resolves them; for a box
with a layout object, properties such as `width` and `height` report the used
value, as `getComputedStyle()` does.

## Correlation rules

- A checkpoint belongs to the document named by its context's document token
  and DOM node identity in its renderer process. The navigation records carry
  the same document token, so a checkpoint can be joined to the committed
  navigation and its URL.
- Node records and the completion belong to the start with the same checkpoint
  identity in the same renderer process.
- `previousCheckpointId` links the checkpoints of one document in order. The
  state of a document at any moment between two checkpoints is the earlier
  checkpoint's state, because no style resolution or layout happened in
  between that reached a paint-clean update.
- Node identities join to the DOM checkpoint and mutation records of the same
  document, which carry element attributes such as `id`.

## Volume and limits

- Each checkpoint records every element and laid-out text node, up to 100000
  nodes, with all listed properties for each element. A checkpoint is recorded
  for every rendering update in which style or layout work happened, so a page
  that animates a property through style or layout produces a full checkpoint
  on every such frame. Archive size grows with document size multiplied by the
  number of changed updates. Whether an animation Chromium runs on the
  compositor resolves style on the main thread on every frame has not been
  measured.
- Only the light DOM is traversed. Nodes inside shadow roots, including
  user-agent shadow roots such as those inside form controls, are not
  recorded. Pseudo-elements such as `::before`, `::after`, and `::marker` are
  not recorded.
- Documents in frames whose rendering is throttled, such as offscreen frames,
  and documents that are not painted, such as those in background tabs, produce
  no checkpoints until they are rendered.
- A frame in another renderer process records its own checkpoints in that
  process. Frame offsets are not combined, so each document's rectangles are
  relative to its own viewport.
- The rectangle is the bounding box only. The individual line boxes of an
  inline element that wraps, and the boxes of a fragmented element, are not
  recorded separately.
- An element with `display: contents` has no layout object and so no
  rectangle, although `getBoundingClientRect()` would return an empty
  rectangle for it.
- A node under a display lock can report a rectangle from its last layout
  before the lock, because locked content is not laid out again.
- Styles computed on demand for elements inside a `display: none` subtree are
  not recorded, because they exist only when a script or tool asks for them.
- A script's own `getComputedStyle()` or geometry calls can resolve style or
  perform layout, which changes the counters. The next paint-clean update then
  produces a checkpoint even if nothing visible changed. A forced layout is
  observed only at that next paint-clean update, not when the script forced
  it.
- Computed-style values are recorded verbatim. `background-image` can carry a
  URL, which follows the same policy as URLs in DOM attribute values.

## Supported claims

The records support these statements about a recorded session:

- At a recorded checkpoint, the named element had, or did not have, a
  generated box, and its viewport-relative bounding rectangle was the recorded
  value.
- At a recorded checkpoint, the named element's resolved value for each listed
  property was the recorded value.
- Between two consecutive checkpoints of a document, no style resolution or
  layout reached a paint-clean update.
- The viewport size, scroll offset, device pixel ratio, and layout zoom factor
  in effect for the checkpoint.

They do not support statements about properties outside the list, shadow or
pseudo-element content, per-line geometry, or the exact moment within a frame
at which a script changed a value.

## Deterministic validation

The required test levels are:

- Unit tests of the integration script: the patch applies once and is
  idempotent, a missing anchor fails the integration, the hook contains no call
  that forces style, layout, or a lifecycle update, and the property list in
  the hook matches this document and the verifier.
- Contract tests of the recorder: every record shape the bridge writes is
  accepted, an undeclared member is rejected at ingest and by archive
  validation, and archive validation rejects a rectangle without a layout
  object, a text node with a computed style, a negative size, a non-string
  style value, a duplicated property name, a checkpoint that names
  itself as previous, and a node count above the maximum.
- An end-to-end run of the instrumented browser: the validation run serves a
  fixture page from the loopback HTTP listener and opens it in a foreground tab
  after the listener fixture has been hidden. The page holds a paragraph, a
  200 by 50 pixel box, and an element with `display: none`. The harness waits
  two animation frames, widens the box to 320 pixels, and then changes only its
  color, waiting two animation frames after each change and reading the box's
  rectangle, the viewport size, and the box's color back from the page. The
  verifier requires, for the fixture document, that every checkpoint is
  complete and consistent, that each names its predecessor, that no two
  consecutive checkpoints report the same counters, that the property list
  matches the defined list, that three checkpoints in order show the box in
  its three states with rectangles and colors equal to what the page reported,
  that the viewport agrees with the page's reported size to within one pixel,
  and that the settled checkpoint holds the `display: none` element without a
  rectangle and a laid-out text node without a computed style.

The fixture shows that the logger emits records; it does not evaluate the
page's layout or styling.

## Next dependent slices

- Shadow-root and pseudo-element traversal, if later analysis needs them.
- Per-fragment and per-line geometry.
- Rendered-frame and compositor correlation identifiers, so a checkpoint can be
  joined to the frame that displayed it.

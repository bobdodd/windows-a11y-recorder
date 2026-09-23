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
  `getComputedStyle()` would report. The object has one member for each
  listed property; its member order is not significant, and the checkpoint
  start's `styleProperties` list gives the recorded order. A property Blink
  produced no value for is present with a null value. Null for a text node, for an
  element with no computed style, and for an element whose style was computed
  only on demand inside a `display: none` subtree.

### Checkpoint completion

- `nodeCount`: the number of node records emitted.
- `truncated`: whether the traversal stopped at `maximumNodes`.
- `maximumNodes`: 100000.

## Recorded computed-style properties

The list holds 283 properties. It is fixed in the integration script, which
generates the compiled property array from it, recorded in every checkpoint
start, and checked by the integration tests against this document and the
verifier. Every listed property is one the reference Chromium reports through
`getComputedStyle()` in a default build. The first 75 were chosen for
geometry, visibility, and basic text presentation. The remaining 208 cover
color and forced colors, text decoration, text layout, reading order and
interaction, sizing and containment, transforms, scrolling, and SVG
presentation, which were added so that later analysis does not depend on
values the logger did not keep.

Three kinds of entry need care when reading the values:

- `text-decoration` is a shorthand. Blink reports it as a combined value; its
  longhands are also listed.
- Legacy `-webkit-` properties such as `-webkit-writing-mode` and
  `-webkit-text-fill-color` are listed as Blink exposes them, alongside the
  standard properties they overlap with.
- These properties are enabled by runtime features that are on by default in
  the reference build: `caret-animation`, `caret-shape`, `dynamic-range-limit`,
  `forced-color-adjust`, `frame-sizing`, `margin-trim`, `overlay`,
  `ruby-overhang`, `scroll-axis-lock`, `scroll-initial-target`,
  `scroll-marker-group`, `scroll-target-group`, `scrollbar-color`,
  `scrollbar-width`, `text-decoration-skip-spaces`, `text-fit`, and
  `window-drag`. The hook reads them by identifier regardless of the feature
  state. What Blink reports for one of them in a build with its feature turned
  off has not been observed.

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
| `accent-color`, `caret-color`, `-webkit-tap-highlight-color`, `-webkit-text-fill-color` | They give the colors of form-control accents, the text caret, the tap highlight, and text fill, which can differ from `color`. |
| `color-scheme`, `forced-color-adjust`, `print-color-adjust`, `dynamic-range-limit` | They state which color schemes an element supports, whether forced colors are applied to it, and how its colors are adjusted for output. |
| `mix-blend-mode`, `background-blend-mode`, `isolation`, `backdrop-filter` | They change the colors actually drawn by blending with, or filtering, what lies behind. |
| `text-decoration`, `text-decoration-color`, `text-decoration-style`, `text-decoration-thickness`, `text-decoration-skip-ink`, `text-decoration-skip-spaces`, `-webkit-text-decorations-in-effect` | They describe how a text decoration is drawn and which decorations apply, including those inherited from ancestors. |
| `text-underline-offset`, `text-underline-position` | They give where an underline is placed. |
| `-webkit-text-stroke-color`, `-webkit-text-stroke-width` | They describe an outline drawn around glyphs. |
| `text-emphasis-color`, `text-emphasis-position`, `text-emphasis-style` | They describe emphasis marks drawn beside text. |
| `word-break`, `overflow-wrap`, `line-break`, `-webkit-line-break`, `hyphens`, `hyphenate-character`, `hyphenate-limit-chars`, `text-wrap-style` | They determine where lines break and whether words are hyphenated. |
| `text-align-last`, `text-justify`, `vertical-align`, `alignment-baseline`, `baseline-shift`, `baseline-source`, `dominant-baseline`, `text-anchor` | They determine how text and inline boxes are aligned. |
| `tab-size`, `text-autospace`, `text-spacing-trim`, `initial-letter`, `text-box-edge`, `text-box-trim`, `text-fit` | They change the spacing and sizing of text beyond letter and word spacing. |
| `unicode-bidi`, `-webkit-rtl-ordering`, `text-orientation`, `-webkit-text-orientation`, `text-combine-upright`, `-webkit-text-combine`, `-webkit-writing-mode` | They determine bidirectional ordering and how characters are set in vertical text. |
| `ruby-align`, `ruby-overhang`, `ruby-position`, `-webkit-ruby-position` | They determine how ruby annotations are placed. |
| `-webkit-line-clamp`, `orphans`, `widows` | They limit the number of lines shown or kept together. |
| `content`, `quotes` | They give generated content and quotation marks where Blink reports them for the element. |
| `-webkit-text-security` | It states whether text is drawn as masking characters. |
| `reading-flow`, `reading-order` | They change the order in which items are read and navigated relative to source order. |
| `interactivity`, `user-select`, `-webkit-user-modify`, `-webkit-user-drag`, `touch-action`, `resize` | They determine whether content is interactive, selectable, editable, draggable, resizable, and which touch gestures it handles. |
| `interest-delay-start`, `interest-delay-end` | They give the delays before interest in an element is shown or lost. |
| `appearance`, `field-sizing`, `caret-animation`, `caret-shape` | They determine how form controls and the text caret are presented and sized. |
| `speak` | It states whether an element's content is to be spoken. |
| `app-region`, `window-drag` | They mark regions that act as part of an application window frame. |
| `aspect-ratio`, `object-fit`, `object-position`, `object-view-box`, `image-orientation`, `image-rendering`, `zoom`, `interpolate-size` | They determine the sizing and scaling of boxes and replaced content such as images. |
| `contain`, `contain-intrinsic-size`, `contain-intrinsic-width`, `contain-intrinsic-height`, `container-name`, `container-type`, `will-change`, `buffered-rendering` | They state containment and rendering hints that can change what is laid out and when. |
| `clear`, `shape-outside`, `shape-margin`, `shape-image-threshold`, `margin-trim` | They determine how content flows around floats and shapes and how edge margins are trimmed. |
| `overflow-anchor`, `overflow-clip-margin` | They affect scroll anchoring and how far content can paint before it is clipped. |
| `anchor-name`, `anchor-scope`, `position-anchor`, `position-area`, `position-try-fallbacks`, `position-try-order`, `position-visibility`, `overlay` | They determine where anchor-positioned boxes such as popovers are placed, whether they are shown, and whether a box is in the top layer. |
| `list-style-type`, `list-style-position`, `list-style-image`, `counter-increment`, `counter-reset`, `counter-set` | They determine list markers and counter values. |
| `table-layout`, `caption-side`, `empty-cells` | They determine table layout and presentation. |
| `break-before`, `break-after`, `break-inside` | They determine page and column breaks. |
| `frame-sizing` | It states how an embedded frame is sized to its content. |
| `rotate`, `scale`, `translate`, `transform-origin`, `transform-box`, `transform-style`, `perspective`, `perspective-origin`, `backface-visibility` | They change where and how a box is drawn in two or three dimensions, alongside `transform`. |
| `offset-path`, `offset-distance`, `offset-position`, `offset-anchor`, `offset-rotate` | They place a box along a motion path. |
| `scroll-behavior`, `scroll-snap-type`, `scroll-snap-align`, `scroll-snap-stop`, `scroll-axis-lock`, `scroll-initial-target` | They determine how a scroll container scrolls, where it snaps, and where it starts. |
| `scroll-margin-top`, `scroll-margin-right`, `scroll-margin-bottom`, `scroll-margin-left`, `scroll-margin-block-start`, `scroll-margin-block-end`, `scroll-margin-inline-start`, `scroll-margin-inline-end` | They give the margins used when a box is scrolled into view. |
| `scroll-padding-top`, `scroll-padding-right`, `scroll-padding-bottom`, `scroll-padding-left`, `scroll-padding-block-start`, `scroll-padding-block-end`, `scroll-padding-inline-start`, `scroll-padding-inline-end` | They give the insets of a scroll container's optimal viewing region, such as the space under a fixed header. |
| `overscroll-behavior-x`, `overscroll-behavior-y`, `overscroll-behavior-block`, `overscroll-behavior-inline` | They determine what happens when scrolling reaches a boundary. |
| `scrollbar-width`, `scrollbar-color`, `scrollbar-gutter` | They determine scrollbar size, color, and reserved space. |
| `scroll-marker-group`, `scroll-target-group`, `scroll-timeline-name`, `scroll-timeline-axis` | They link scroll containers to scroll markers and to scroll-driven timelines. |
| `fill`, `fill-opacity`, `fill-rule`, `stroke`, `stroke-opacity`, `stroke-width`, `stroke-dasharray`, `stroke-dashoffset`, `stroke-linecap`, `stroke-linejoin`, `stroke-miterlimit`, `paint-order`, `vector-effect` | They determine how SVG shapes and text are filled and stroked. |
| `stop-color`, `stop-opacity`, `flood-color`, `flood-opacity`, `lighting-color`, `color-interpolation`, `color-interpolation-filters`, `color-rendering` | They give SVG gradient, filter, and color-processing colors. |
| `marker-start`, `marker-mid`, `marker-end`, `clip-rule`, `shape-rendering` | They determine SVG markers, clipping rules, and shape rendering. |
| `cx`, `cy`, `r`, `rx`, `ry`, `x`, `y`, `d` | They give SVG geometry. `d` carries the whole path data, which can be long. |

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
- Computed-style values are recorded verbatim. `background-image`,
  `list-style-image`, `shape-outside`, and `content` can carry a URL, which
  follows the same policy as URLs in DOM attribute values.
- Each node is one record, and a record whose serialized form exceeds the 4 MiB
  protocol message limit is not sent. The bridge counts it and reports a
  `browser-evidence-write-failed` omission on the channel instead. Values such
  as a long SVG path in `d` or a data URL in `background-image` make this
  possible; it has not been observed.
- An element's record holds 283 values rather than the first list's 75. The
  volume with the full list is measured in the validation plan.

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

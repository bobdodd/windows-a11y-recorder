# Frame and Page Identity Evidence Model

## Purpose

Protocol 0.11 extends `browser.navigation` evidence from the primary main frame
to every navigation observed by `WebContentsImpl`. It supplies the page and
frame hierarchy needed to attribute later document, DOM, accessibility, and
view evidence without treating every frame as a tab or every main frame as the
currently displayed page.

Chromium distinguishes subframes, primary main frames, prerender main frames,
fenced-frame roots, and guest main frames. It also distinguishes a frame's
direct parent from the parent or outer document that owns an embedded frame
tree:

- [NavigationHandle API](https://source.chromium.org/chromium/chromium/src/+/main:content/public/browser/navigation_handle.h)
- [FrameType definition](https://source.chromium.org/chromium/chromium/src/+/main:content/public/browser/frame_type.h)
- [RenderFrameHost hierarchy API](https://source.chromium.org/chromium/chromium/src/+/main:content/public/browser/render_frame_host.h)
- [Chromium frame-tree model](https://source.chromium.org/chromium/chromium/src/+/main:docs/frame_trees.md)

## Evidence fields

Protocol 0.11 preserves the protocol 0.10 navigation fields and adds:

- `frameType`: `subframe`, `primary-main-frame`,
  `prerender-main-frame`, `fenced-frame-root`, or `guest-main-frame`.
- `primaryPage`: whether the navigating frame belongs to the primary page
  currently associated with the `WebContents`.
- `parentFrameId`: the direct parent frame for a subframe, otherwise null.
- `parentOrOuterDocumentFrameId`: the direct parent for an ordinary subframe,
  the outer owning document for an embedded frame-tree root, or null when
  neither relationship exists.

The existing context identifiers have the following protocol 0.11 meanings:

- `context.pageId`: the root `FrameTreeNodeId` for the page containing the
  navigating frame.
- `context.frameId`: the navigating frame's `FrameTreeNodeId`.
- `context.documentId`: the navigation identity that created the committed
  document hosted by that frame. It remains null before commit and after an
  uncommitted completion.

All identifiers are opaque recorder strings. Numeric suffixes must not be
interpreted or compared across browser instances.

## Identity derivation

For a main-frame navigation, the target frame-tree node is also the page root.
For a subframe navigation, the recorder follows the direct parent to its main
frame and uses that main frame's tree-node identity as `pageId`.

`primaryPage` is true for the primary main frame and for descendants whose page
root is the primary main frame. It is false for prerendered pages, fenced-frame
pages, guest pages, and their descendants.

The page and frame identifiers remain stable when a frame commits a new
document. The document identifier changes on a cross-document commit and
remains stable across same-document navigation.

## Relationship rules

- A `subframe` has a non-null `parentFrameId`.
- For an ordinary subframe, `parentFrameId` and
  `parentOrOuterDocumentFrameId` identify the same direct parent.
- A main frame has a null `parentFrameId`.
- A `primary-main-frame` has `primaryPage` set to true.
- A fenced-frame root may have a null `parentFrameId` and a non-null
  `parentOrOuterDocumentFrameId`.
- A prerender or guest main frame may have no owning document represented in
  this protocol boundary.

The archive validator rejects contradictions for relationships it can
determine from one record. Cross-record validation is performed by the
deterministic fixture verifier.

## Claims the evidence supports

The evidence can establish:

- which page and frame a recorded navigation belongs to;
- whether that frame belongs to the primary page;
- whether Chromium classified it as a subframe or a specific main-frame type;
- the direct parent of an ordinary subframe;
- the outer owning document when Chromium exposes one;
- stable page identity across main-frame and child-frame navigation;
- distinct document identity for independently committed frame documents; and
- start-to-completion correlation for each navigation attempt.

## Claims the evidence does not support

The evidence does not establish:

- that a non-primary page became visible or was activated;
- that a navigation produced a distinct user-visible view;
- that a frame was visually presented, focused, or accessible;
- a DOM, accessibility-tree, layout, paint, or screenshot state;
- a complete ancestry chain beyond the recorded direct relationship;
- the cause of a navigation; or
- load completion after navigation commit.

## Validation boundary

The deterministic protocol 0.11 fixture contains a same-origin child frame.
Validation requires:

- correlated start and committed completion records for the child frame;
- `frameType` equal to `subframe`;
- `primaryPage` equal to true;
- the same page ID as the primary main-frame navigation;
- a distinct frame ID and committed document ID;
- the primary frame ID as both `parentFrameId` and
  `parentOrOuterDocumentFrameId`; and
- preservation of all protocol 0.10 navigation assertions.

Passing this fixture validates primary-page child-frame identity. Prerender,
fenced-frame, guest-page, nested-frame, cross-origin-frame, and non-primary
page execution remain implemented classifications but are not validated by
this fixture.

## Next dependent slices

Protocol 0.11 supplies the hierarchy needed for:

1. Document lifecycle and load milestones.
2. DOM and accessibility checkpoints keyed to page, frame, and document.
3. Explicit default-action-to-navigation causal correlation.
4. View-visit analysis combining navigation and observed-state evidence.

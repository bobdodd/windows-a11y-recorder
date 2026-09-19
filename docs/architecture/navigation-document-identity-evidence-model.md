# Navigation and Document Identity Evidence Model

## Purpose

Protocol 0.10 records primary-main-frame navigation boundaries in Chromium's
browser process. It establishes the page, frame, navigation, and committed
document identities needed to correlate later browser evidence without treating
every URL change as a new document or a new user-visible view.

The instrumentation boundaries are
`WebContentsImpl::DidStartNavigation()` and
`WebContentsImpl::DidFinishNavigation()`. Chromium documents
`NavigationHandle::GetNavigationId()` as unique to one navigation and
`RenderFrameHost::GetNavigationId()` as the navigation that created the current
document. The latter does not change after same-document navigation:

- [NavigationHandle API](https://source.chromium.org/chromium/chromium/src/+/main:content/public/browser/navigation_handle.h)
- [RenderFrameHost document navigation identity](https://source.chromium.org/chromium/chromium/src/+/main:content/public/browser/render_frame_host.h)
- [WebContents navigation boundaries](https://source.chromium.org/chromium/chromium/src/+/main:content/browser/web_contents/web_contents_impl.cc)

## Scope

The slice records only navigations in the primary main frame. Subframe,
prerender, fenced-frame, portal, and non-primary page navigations remain outside
protocol 0.10.

The `browser.navigation` channel emits:

- `navigation-started` when `WebContentsImpl` receives the start boundary.
- `navigation-completed` when `WebContentsImpl` receives the finish boundary.

Every record includes:

- `context.browserInstanceId`: the recorder-assigned browser instance.
- `context.processId` and `context.processType`: the browser process that
  observed the navigation.
- `context.pageId`: an identity for the primary page, derived from the stable
  primary-main-frame tree node.
- `context.frameId`: Chromium's browser-global `FrameTreeNodeId`, prefixed as
  a recorder identifier.
- `context.documentId`: null at start and for an uncommitted finish. A committed
  finish uses the navigation ID that created the current `RenderFrameHost`
  document.
- `navigationId`: Chromium's unique navigation ID, prefixed as a recorder
  identifier.
- `url`: the URL observed at that boundary.
- `navigationKind`: `cross-document` or `same-document`.
- `rendererInitiated`: whether Chromium classified the navigation as initiated
  by renderer content.
- `sameDocument`: Chromium's same-document classification.

Start records carry null `committed`, `errorPage`, `netErrorCode`, and `outcome`
values because those facts are not established at that boundary.

Completion records add:

- `committed`: whether the navigation committed.
- `errorPage`: whether the committed result is an error page.
- `netErrorCode`: Chromium's terminal network error code.
- `outcome`: `committed`, `committed-error-page`, or `not-committed`.

## Identity rules

Each `navigationId` identifies exactly one navigation attempt. Start and
completion records correlate by browser instance, page ID, frame ID, and
navigation ID.

`pageId` remains stable for the life of the owning `WebContents`.
`frameId` remains stable for the life of the primary frame tree node. A
cross-document commit may replace the current `RenderFrameHost`, while the
frame ID remains stable.

`documentId` identifies the committed document hosted by the current
`RenderFrameHost`. Same-document history updates and fragment navigations
receive a new navigation ID but retain the existing document ID. A
cross-document commit receives the document ID derived from the navigation
that created the committed document.

Identifiers are opaque correlation values. Their numeric suffixes have no
meaning outside their originating browser instance. The page and frame IDs
currently share the primary-main-frame tree node as their Chromium identity
source, but remain separate protocol fields because later subframe and
non-primary-page coverage will distinguish their scopes. They must not be
compared across browser restarts.

## Claims the evidence supports

A correlated start and completion pair proves that Chromium observed a
primary-main-frame navigation attempt and its terminal state. A committed
record proves whether Chromium classified that commit as cross-document or
same-document. Stable page, frame, and document IDs across a same-document
commit prove that the URL or history changed without replacing the committed
document.

The evidence does not prove:

- that a navigation produced a distinct user-visible view;
- that the DOM, layout, accessibility tree, focus, or pixels changed;
- that a URL change was meaningful to the participant;
- that a committed page became fully loaded, interactive, or presented;
- that a renderer-initiated navigation was caused by a particular listener,
  default action, or script callback; or
- that two different URLs represent different application states.

Those claims require later checkpoint, DOM, accessibility, rendering, and
causal-correlation slices.

## Privacy and data handling

Navigation URLs are evidence and may contain sensitive paths, fragments, or
query parameters. They remain subject to the session's local-first storage,
access control, retention, and deletion policy. Protocol 0.10 does not capture
request bodies, response bodies, credentials, authorization headers, cookie
values, or browser history outside navigations observed during the recording.

Analysis and export surfaces must treat URLs as potentially sensitive. A future
policy layer may redact selected query parameters or fragments, but it must
preserve an explicit quality flag whenever redaction reduces correlation
fidelity.

## Deterministic validation

The Blink fixture produces:

- the initial cross-document commit to the fixture;
- at least one same-document navigation from the fixture;
- distinct navigation IDs for those commits;
- one stable page ID and frame ID across both commits;
- one stable committed document ID across the same-document commit;
- a start record that precedes each selected completion; and
- browser-process provenance for all selected navigation records.

Passing these checks establishes the protocol 0.10 navigation and document
identity contract. It does not establish view equivalence or prove any
resulting visual, DOM, or accessibility change.

## Next dependent slices

Protocol 0.10 provides the identity spine for later work:

1. Subframe and non-primary page identity.
2. Document lifecycle and load milestones.
3. DOM and accessibility checkpoints keyed to committed document identity.
4. Default-action to navigation correlation using an explicit causal ID.
5. View-visit analysis that combines navigation evidence with observed state
   changes and explicit uncertainty.

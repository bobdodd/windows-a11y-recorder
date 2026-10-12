# Accessibility Requests

## Status

Step 3 of the NVDA work in
[screen reader activity](screen-reader-activity.md). The owner chose, on
2026-10-12, hooks built into the instrumented Chromium rather than a trace
first, and a test on [cnib.ca](https://cnib.ca) and on a test page in the
repository. The hook design was agreed the same day, with the fuller player
view held until after the target run. Built as protocol 0.60; to build in
the owner's checkout and to run on the target machine. Listed in
[outstanding work](analysis-outstanding-work.md).

## The problem

What NVDA announces in a web page depends on what it asked the browser for
and what the browser answered. Until now the recorder held the keys, NVDA's
inferred commands and mode, and the page's accessibility checkpoints, but
not the requests themselves. An accessibility checkpoint gives only how many
events an update carried, not which request caused it.

Two requests matter most:

- **What is under the pointer.** With mouse tracking on, NVDA asks Windows
  for the object at the pointer, which calls `accHitTest` on each object in
  turn ([NVDA's IAccessibleHandler](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/IAccessibleHandler/__init__.py),
  [NVDA's mouseHandler.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/mouseHandler.py)).
  `accHitTest` takes screen coordinates and answers with the object itself,
  a child id, or a child object
  ([Microsoft, IAccessible::accHitTest](https://learn.microsoft.com/en-us/windows/win32/api/oleacc/nf-oleacc-iaccessible-acchittest)).
- **Scrolling into view.** In browse mode, moving to an object, with H, D,
  the arrows and so on, calls `scrollIntoView`; the keyboard focus moves only
  in focus mode or on activation
  ([NVDA's browseMode.py](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/browseMode.py)).
  For Chromium, NVDA's `scrollIntoView` is IAccessible2's `scrollTo` with
  "anywhere"
  ([NVDA's IAccessible objects](https://raw.githubusercontent.com/nvaccess/nvda/release-2026.2/source/NVDAObjects/IAccessible/__init__.py),
  [IAccessible2, Accessible2.idl](https://raw.githubusercontent.com/LinuxA11y/IAccessible2/master/api/Accessible2.idl)).

## What Chromium does with them

The owner's checkout, 156.0.8065.0, has the same code as Chromium's current
source at every point below.

- **A hit test is answered at once, then checked.**
  `BrowserAccessibilityManager::CachingAsyncHitTest` sends a precise hit
  test to the page's renderer, then answers at once in one of two ways
  ([browser_accessibility_manager.cc](https://raw.githubusercontent.com/chromium/chromium/main/ui/accessibility/platform/browser_accessibility_manager.cc)):
  - with the page's last precise result, when the point is inside that
    result's bounds;
  - otherwise with a search of the browser process's own copy of the tree by
    bounds, which the source says is "generally right for simple pages but
    wrong in cases of z-index, overflow, and other more complicated
    layouts".

  The precise result arrives later, in `CacheHitTestResult`, and is kept
  for the next hit test. So an assistive technology can be given one object
  first and a different one on its next request at the same point. The
  renderer's hit test is in `RenderAccessibilityImpl::HitTest`
  ([render_accessibility_impl.cc](https://raw.githubusercontent.com/chromium/chromium/main/content/renderer/accessibility/render_accessibility_impl.cc)).
  A hit test that finds a frame continues in that frame's document.
- **Every action passes one point in the browser process.**
  `RenderFrameHostImpl::AccessibilityPerformAction` sends each action to the
  frame's renderer, which performs it in
  `RenderAccessibilityImpl::PerformAction`. IAccessible2's
  `scrollToSubstring` goes there directly, without
  `BrowserAccessibility::AccessibilityPerformAction`
  ([browser_accessibility.cc](https://raw.githubusercontent.com/chromium/chromium/main/ui/accessibility/platform/browser_accessibility.cc),
  [ax_platform_node_win.cc](https://raw.githubusercontent.com/chromium/chromium/main/ui/accessibility/platform/ax_platform_node_win.cc)),
  so the action is recorded in `RenderFrameHostImpl`, which every action
  passes. A hit test's request to the renderer leaves that function before
  it is sent as an action, so it is not recorded twice.

## Decisions of 2026-10-12

- Hooks in Chromium now, not a trace first. A trace would name each call and
  its time, but not its target object or point
  ([tracing switches](https://raw.githubusercontent.com/chromium/chromium/main/components/tracing/common/tracing_switches.cc)).
- Records in both processes. The browser process sees what was asked and
  what was answered; the renderer ties a request to the page's document and
  to the accessibility checkpoints already recorded.
- Chromium cannot tell which program made a request, only that it came
  through a platform accessibility interface. The records and the player
  say so. During a test with NVDA, NVDA is the only assistive technology
  running, but any other program using these interfaces can ask too.
- The player names each request in the event list and gives its outcome in
  Selected event values. A fuller view, such as tying a request to the key
  that caused it, waits until the target run shows what NVDA does.
- The test page is in the repository, `tests/fixtures/accessibility-requests`,
  with headings, landmarks, enough content to scroll, a sticky header over
  content, a same-site frame, a button and a link.

## The records

All on the `browser.accessibility` channel. Points and bounds are integers.
A node is `{treeId, nodeId, role, name, bounds}`, its role as Chromium names
it (`ui::ToString`, `ax_enum_util.cc`) and its bounds as Chromium compares
them with the point. Node id 0 is no node; negative ids are valid.

| Event type | Process | Hook | Payload |
|---|---|---|---|
| `accessibility-hit-test` | browser | `CachingAsyncHitTest`, at each answer | `rootTreeId`, `point` (screen, physical pixels), `basis` (`last-precise-result` or `cached-tree-guess`), `preciseRequested`, `answer` (a node or null) |
| `accessibility-hit-test-result` | browser | `CacheHitTestResult` | `result` (a node) |
| `accessibility-action-sent` | browser | `RenderFrameHostImpl::AccessibilityPerformAction`, before it is sent | the frame's context, `treeId`, `action`, `targetNodeId`, `target` (a node, or null when the browser's copy of the tree does not hold it), `targetRect`, `targetPoint`, `horizontalScrollAlignment`, `verticalScrollAlignment`, `scrollBehavior`, `selection` |
| `accessibility-action-received` | renderer | `RenderAccessibilityImpl::PerformAction` | the document's context, `treeId`, `action`, `targetNodeId`, `anchorNodeId`, `focusNodeId` |
| `accessibility-hit-test-computed` | renderer | `RenderAccessibilityImpl::HitTest` | the document's context, `treeId`, `point` (the frame's coordinates), `nodeId` (or null for no object), `childTree`, `eventToFire` |

Scroll alignments are `none`, `center`, `top`, `bottom`, `left`, `right`, or
`closest-edge`; scroll behaviors `none`, `do-not-scroll-if-visible`, or
`scroll-if-visible`. Chromium has no names for these, so the bridge names
them from `ax_enums.mojom`.

The recorder's checks refuse:

- a hit test from the page's last result without an answer, or one that
  says no precise test was asked;
- an action's target that is not the node its id names;
- a hit test continuing in a child frame without a node;
- a node with id 0;
- a record from the wrong process.

The integration script (`chromium/integrate.py`) patches
`browser_accessibility_manager.cc`, the `ui/accessibility/platform` build
file, `render_frame_host_impl.cc`, and `render_accessibility_impl.cc`;
`0031_accessibility_requests.sql` adds the five tables.

## The player

The event list summarizes each record, for example
`accessibility-hit-test: at 512, 300, heading "Contact us", guess from the tree`.
Selected event values begins with the record's outcome (rule 1,
`AccessibilityRequests`):

- **A hit test:** what was given at once and from what, then the page's
  precise result, joined as the first `accessibility-hit-test-result` within
  1 second after it, and whether it is the same object (the same tree and
  node). When other hit tests were asked before that result arrived, it says
  the result may answer one of them, since Chromium does not say which
  request a result answers. No result within 1 second is said too: a point
  with no object fires none.
- **A precise result:** the object found, its bounds, and the latest hit
  test before it.
- **An action sent:** what it asked, in words ("scroll heading "Contact us"
  (node 652) into view"), its alignment, rectangle or point, and selection,
  and whether the renderer received it, joined as the first
  `accessibility-action-received` with the same tree, action, and target
  within 1 second.
- **An action received, and the renderer's hit test:** what the renderer
  was asked or found, and the browser's sending of it.

Every outcome of a request says that Chromium cannot tell which program
asked. The playback index keeps these records whole (version 17).

## Limits

- Which program made a request is not recorded; Chromium does not know.
- A precise result is joined to the hit test before it by time; with
  several hit tests in flight, the player says it cannot tell which one it
  answers.
- The renderer's hit test point is in the frame's coordinates, not the
  screen's.
- What NVDA spoke is not recorded. The records show what NVDA asked and was
  told, which supports what it had to announce.

## Tests

- **Unit tests** (`tests/Recorder.Tests/AccessibilityRequestTests.cs`, with
  samples in `EvidenceSamples` for the database tests in
  `EvidenceTableTests`): the payload rules, each record read as its typed
  shape, the joins and outcomes, the summaries, and the outcomes through the
  playback index. The integration script's tests
  (`AccessibilityRequestIntegrationTests` in `chromium/test_integrate.py`)
  patch excerpts of the 156 sources.
- **An integration test on Windows** (`tests/AccessibilityRequestProbe`):
  it runs the recorder's browser receiver in its own process, which launches
  the rebuilt Chromium on the test page served from a local port, and asks
  as an assistive technology does. It sends:
  - hit tests through Microsoft Active Accessibility, recursing through
    `accHitTest` as NVDA does, at the button, at a header link with content
    under it, and at a button in the frame;
  - `accDoDefaultAction` on the button and `accSelect` to focus the link;
  - IAccessible2 `scrollTo` with "anywhere" on a heading far down the page;
  - UI Automation's `ScrollIntoView` on another heading
    ([Microsoft, ScrollIntoView](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationscrollitempattern-scrollintoview)),
    reported as not run if Chromium's provider has no ScrollItem pattern.

  It checks each record, that the second hit test at a point is given the
  page's last precise result, that the renderer found the same node, that
  the hit test in the frame went from the page's document into the
  frame's, that every record passes the recorder's checks, and that the
  player gives each an outcome.
- **A target run with NVDA**, mouse tracking on, on the test page and on
  [cnib.ca](https://cnib.ca):
  - heading (H), landmark (D), arrow and Tab navigation;
  - the Elements List (NVDA+F7): "Move to" a heading and "Activate" a link;
  - activating the button with Enter in browse mode;
  - the mouse moved slowly over the sticky header when the page is
    scrolled, over the frame, and over the content.

## First run of the probe, 2026-10-12

On the target machine, against Chromium rebuilt at 8f4e017: 27 of 32 checks
passed. All five failures were the probe's own faults; the records were as
designed, and every record passed the recorder's checks. 80 request records
came from 7 requests.

- **The precise result is the text inside a control.** For the button
  "Show message", the link "Home", and the frame's button, Chromium's
  precise hit test found the `staticText` inside it, of the same name, not
  the button or link. The probe expected the control. On the next request
  at the same point, Chromium gave that text from the page's last precise
  result.
- **Requests too close to the probe's mark.** The probe told its two
  requests at a point apart at the exact time of the second. A browser
  record's time is mapped from the browser's clock, and here fell up to
  about 2 ms from the probe's, so records of the second request were
  counted with the first.
- **The sticky header, as expected.** With the page scrolled, a hit test on
  the header's "Home" link was given, at once, the unnamed section of
  content under the header, from Chromium's guess by bounds. The page's
  precise result was the text "Home". The player says "not the object
  given".
- **One request, several records.** Recursing through `accHitTest`, as NVDA
  does, made three hit test records at each point, each with its own
  precise result. So the player said "other hit tests were asked before it
  arrived" for most of them, as designed.
- **Scrolling also moves Chromium's accessibility focus.** IAccessible2
  `scrollTo` and UI Automation `ScrollIntoView` each sent
  `scrollToMakeVisible`, centered and not scrolling if visible, with
  `setAccessibilityFocus` on the same object. The second one also sent
  `clearAccessibilityFocus` on the object before.
- **Clocks of two processes.** One precise result, in the browser process,
  was recorded 0.1 ms before the renderer hit test that produced it. So
  the order of records from different processes within about a millisecond
  is not reliable.

The probe now tells its two requests apart halfway through the pause
between them. It also accepts the control or its text as the precise
result, and names the role found.

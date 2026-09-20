# Instrumented Chromium Architecture

## Decision

Browser source instrumentation is a required part of Windows A11y Recorder. A browser extension, content script, CDP client, or external automation driver cannot provide the complete and authoritative handler, dispatch, default-action, and scheduling evidence required by the product.

The instrumented browser is shipped and launched as part of the same application. The participant uses it normally. The recorder does not drive it through Selenium, WebDriver, or Python.

## Required evidence

### Event listeners

Record registration and removal for:

- `addEventListener` listeners.
- Inline event-handler attributes.
- `on*` event-handler properties.
- Listeners installed in isolated execution worlds.
- Internal Blink listeners that participate in page interaction.
- Listener type, capture, passive, once, target, callback identity, execution world, and source location.

The record must identify listeners that exist even if they are not exposed by a public JavaScript inspection API.

### Event dispatch

Assign an identifier to each dispatch and record:

- Input provenance when the event originated from keyboard, mouse, touch, pen, accessibility action, or browser command.
- Original target and retargeted target.
- Composed path.
- Capture, target, and bubble phases.
- Listeners considered and invoked.
- Listener start, completion, exception, and duration.
- Calls that stop propagation or prevent the default action.
- Browser default action considered, performed, suppressed, or failed.
- Resulting focus, activation, navigation, DOM mutation, and accessibility changes.

This supports direct comparison of pointer and keyboard paths. A later analyzer can identify a pointer-triggered activation with no corresponding focusable or keyboard-triggerable path without reconstructing the answer from JavaScript source text alone.

### Time and scheduling

Record:

- Timeout and interval creation, cancellation, requested delay, effective delay, and firing.
- Animation-frame and idle-callback scheduling and execution.
- Task source, callback location, nesting, queue delay, execution duration, and throttling.
- Page visibility, lifecycle, freezing, backgrounding, and timer-clamping context.
- Browser-owned delayed actions that change the page or user experience.

The recording must preserve both requested and observed timing. Analysis can then distinguish application intent from delays introduced by the browser, system load, throttling, or recorder overhead.

### Page and accessibility state

At policy-controlled checkpoints, record:

- Original response source where available.
- Live serialized DOM.
- Shadow DOM, frame, and execution-world boundaries.
- Computed styles and layout geometry needed for later analysis.
- Browser accessibility tree and node-to-DOM mappings.
- Focus, selection, active descendant, and text-editing state.
- Rendered frame or compositor correlation identifiers.

Snapshots must carry document, frame, navigation, and checkpoint identifiers so later analysis does not combine incompatible states.

### Cookies and network

Cookie evidence includes:

- Operation: read, write, delete, send, receive, or block.
- Cookie name.
- Domain, path, SameSite, Secure, HttpOnly, partitioning, expiry class, and source API where available.
- Request, response, document, frame, and navigation correlation.
- Whether the operation succeeded or was blocked and the reason.

Cookie values are never recorded. Authorization values, saved credentials, request bodies, and response bodies are not recorded.

Network evidence includes request and response metadata, initiator, resource type, redirect chain, status, cache behavior, timing, and cookie names associated with the transaction.

## Process architecture

```text
Recorder.App.exe
  launches and supervises

Recorder.CaptureHost.exe
  owns session time and archive
  accepts authenticated browser evidence

Instrumented Chromium browser process
  distributes a per-session IPC capability
  correlates navigation, network, and child processes

Instrumented renderer processes
  observe Blink listeners, dispatch, timers, DOM, layout, and accessibility

GPU and utility processes
  provide rendering, compositor, network, and process correlation where required
```

Each producer has its own sequence and Chromium monotonic timestamp. The receiver maps that clock to the recorder session clock and records uncertainty. Cross-process relationships use stable browser instance, process, page, frame, document, navigation, dispatch, listener, timer, request, and checkpoint identifiers.

## Evidence bridge

The bridge is a private, versioned, local IPC protocol rather than a remote debugging port. Its requirements are:

- Per-session authentication established by the recorder when launching the browser.
- No listening network socket.
- Strict message size and schema limits.
- Bounded producer queues with explicit dropped-record summaries.
- Browser-process batching so instrumentation does not block input dispatch.
- Source-side removal of cookie values, authorization values, and prohibited bodies.
- Clock synchronization anchors from every Chromium process.
- Process start, restart, crash, navigation, and shutdown records.
- Graceful degradation that remains visible in the archive.

The C# contracts in `Recorder.Contracts/BrowserEvidenceContracts.cs` define the first recorder-side vocabulary. They will be mirrored by Chromium-side C++ protocol structures rather than shared through a managed runtime.

The initial implementation is in `chromium/recorder_bridge`. The recorder starts
the browser with `--a11y-recorder-bootstrap=stdin`, writes the pipe name,
protocol version, browser instance identifier, message limit, and authentication
token through redirected standard input, and then closes the stream. The token
is not placed in command-line arguments, environment variables, or a file. The
launcher also removes inherited environment variables whose names indicate
tokens, secrets, passwords, authorization data, API keys, access keys, or
private keys.

The recorder and instrumented browser must run without administrator
elevation. On Windows, Chromium relaunches an elevated browser process at
standard user integrity. That relaunch breaks the inherited standard-input
bootstrap boundary and leaves the original elevated process with a normal exit
code. The managed launcher therefore rejects an elevated recorder process with
an actionable diagnostic. It does not add Chromium's
`--do-not-de-elevate` switch because retaining administrator rights in a web
browser would violate the product's least-privilege requirement.

Protocol version 0.3 propagates the recorder capability from the browser to
eligible renderer processes through a browser-owned, read-only shared-memory
region. GPU and utility processes are deliberately excluded because the
current evidence hooks run only in Blink renderers. Chromium's Windows
child-launch path inherits the region handle. Because the bridge is linked
into more than one Chromium module, the browser publishes Chromium's opaque
serialized handle metadata in
an internal process environment marker rather than relying on module-local
static storage. The child-launch hook reads that metadata, explicitly inherits
the handle, and removes the marker from the child environment. Child command
lines contain only the serialized handle metadata and a non-secret Chromium
child process identifier, never the pipe authentication token. Each
participating process maps the region during early startup, validates its
process type and identifiers, authenticates its own named-pipe connection, and
establishes an independent clock mapping. Lifecycle records correlate the
browser instance, OS process ID, browser OS process ID, Chromium child process
ID, and process type.

Version 0.3 also adds listener `currentTarget` evidence and populates the
ordered Node `composedPath` captured from Blink's dispatch path. Receivers
continue to accept archived 0.2 dispatch payloads that omit `currentTarget`,
but live 0.3 connections require an exact protocol-version match.

Protocol version 0.4 adds correlated Node default-event-handler decisions.
The renderer records whether Blink invoked the Node handler, suppressed it
after `preventDefault()`, found the event already handled, or rejected an
ineligible untrusted event. An invocation record describes entry into Blink's
handler boundary and does not by itself claim a resulting visible state
change. Live 0.4 connections require an exact protocol-version match.

Protocol version 0.5 adds correlated lifecycle evidence for window
`setTimeout` and `setInterval` timers. A stable process-local timer identifier
relates the accepted schedule to callback entry and, for a live interval, an
explicit `clearTimeout` or `clearInterval` cancellation. Scheduling evidence
records the accepted requested delay, Blink's effective delay, and nesting
level. The timer event timestamp is the observed callback-entry time; it does
not claim callback completion or resulting page effects. Throttling is null,
page lifecycle state is `unknown`, and callback location is null until those
facts have dedicated instrumentation. Implicit one-shot retirement and
execution-context destruction are not reported as explicit cancellation.
Animation frames, idle callbacks, worker timers, and browser-process task
scheduling remain outside this slice. Live 0.5 connections require an exact
protocol-version match.

Protocol version 0.6 adds correlated lifecycle evidence for web-exposed
`requestAnimationFrame` callbacks. A stable process-local identifier relates
each accepted callback schedule to either callback entry or an explicit
`cancelAnimationFrame` cancellation. Delay fields are null and nesting level is
zero because animation-frame callbacks are not delay-based DOM timers. The
callback-entry record does not claim callback completion, frame presentation,
or resulting page effects. Internal Blink frame callbacks, execution-context
destruction, idle callbacks, worker scheduling, throttling, lifecycle state,
and callback location remain outside this slice. Live 0.6 connections require
an exact protocol-version match.

Protocol version 0.7 adds correlated lifecycle evidence for web-exposed
`requestIdleCallback` callbacks. A stable process-local identifier relates
each accepted callback schedule to either callback entry or an explicit
`cancelIdleCallback` cancellation. A supplied timeout is recorded as the
requested and effective delay; both delay fields are null when the caller
omits the option. Callback entry includes Blink's observed
`IdleDeadline.didTimeout` value. The record proves entry at the callback
boundary, not callback completion or resulting page effects.
Execution-context destruction, worker scheduling, throttling, lifecycle state,
and callback location remain outside this slice. Live 0.7 connections require
an exact protocol-version match.

Protocol version 0.8 records Blink's observed page lifecycle state at accepted
schedule, callback-entry, and explicit-cancellation boundaries for DOM timers,
animation-frame callbacks, and idle callbacks. Values are `visible`, `hidden`,
`frozen`, or `unknown`. The state is an observation at each boundary, not an
inference about why execution was delayed. The `throttled` field remains null
until a dedicated scheduler hook can report an actual throttling decision.
Worker scheduling, callback location, queue delay, execution duration, and
implicit cancellation remain outside this slice. Live 0.8 connections require
an exact protocol-version match.

Protocol version 0.9 adds authoritative queue-level wake-up deferral evidence
from Blink's `TaskQueueThrottler::GetNextAllowedWakeUp()` boundary. A
`browser.scheduler` `wake-up-deferred` record preserves the queue
classification, scheduler throttling type, desired and allowed wake-ups,
positive deferral, ready-task state, and block type. The record is
renderer-process scoped because this scheduler boundary has no document or DOM
timer identity. Timer `throttled` fields therefore remain null, and nearby
timer and scheduler records must not be treated as a causal one-to-one match.
The complete claim and correlation rules are defined in the
[scheduler decision evidence model](scheduler-decision-evidence-model.md).
Live 0.9 connections require an exact protocol-version match.

Protocol version 0.10 adds primary-main-frame navigation start and completion
evidence from Chromium's browser-process `WebContentsImpl` boundaries. Records
preserve stable page and frame identity, a unique navigation identity, and the
committed document identity supplied by `RenderFrameHost`. Same-document
navigations receive a new navigation ID while preserving the document ID.
Completion records distinguish successful commits, committed error pages, and
uncommitted attempts. This evidence does not claim that a navigation produced
a distinct view, changed the DOM or accessibility tree, or reached a loaded or
presented state. The complete identity and correlation rules are defined in the
[navigation and document identity evidence model](navigation-document-identity-evidence-model.md).
Live 0.10 connections require an exact protocol-version match.

Protocol version 0.11 extends navigation evidence to subframes and non-primary
main frames. Each record identifies the target frame type, whether it belongs
to the primary page, the root page identity, the direct parent frame when one
exists, and the parent or outer document across embedded frame-tree
boundaries. The deterministic fixture validates a same-origin child frame in
the primary page. Prerender, fenced-frame, guest-page, nested-frame, and
cross-origin execution remain outside the validated fixture scope. The
complete rules are defined in the
[frame and page identity evidence model](frame-and-page-identity-evidence-model.md).
Live 0.11 connections require an exact protocol-version match.

Protocol version 0.12 adds a bounded structural checkpoint when Blink finishes
parsing a document. The renderer emits a start record, preorder node records,
and a completion record with explicit node-count, limit, and truncation fields.
This first DOM slice includes node and parent identities, node type, and node
name. It intentionally excludes text, attributes, mutation history, style,
layout, accessibility, paint, and rendered pixels. The complete contract is
defined in the
[DOM checkpoint evidence model](dom-checkpoint-evidence-model.md).

Protocol version 0.13 adds coalesced post-mutation checkpoints for structural
child-list changes. `Document::NotifyChangeChildren` queues the affected
document into Blink's mutation observer agent microtask machinery. Each
delivery pass deduplicates documents and emits at most one bounded checkpoint
for each active, parser-complete document. Page script does not need to create
a JavaScript `MutationObserver`. Live 0.13 connections require an exact
protocol-version match.

Protocol version 0.14 adds explicit browser-renderer document correlation.
Committed browser navigation records and renderer DOM checkpoints carry the
same Chromium document token. Navigation completions also identify the hosting
renderer process. Correlation requires browser instance, document token, and
renderer process to match. Cross-document commits replace the active frame
mapping, while same-document commits must preserve it. Live 0.14 connections
require an exact protocol-version match.

Chromium's Windows renderer and other lockdown sandbox tokens cannot open a
named pipe created with the managed `CurrentUserOnly` option. The recorder
therefore creates each browser-evidence pipe through the native
`CreateNamedPipe` API with an explicit self-relative security descriptor. Its
protected DACL grants duplex access only to the current logon SID and
Chromium's `S-1-0-0` lockdown restricting SID. Its mandatory label is untrusted
integrity, `S-1-16-0`, so an untrusted renderer can write to it.
If a noninteractive Windows token has no logon SID, the descriptor uses that
token's current-user SID instead.
The pipe name remains unpredictable, and every process must still authenticate
with the per-session secret before the receiver accepts or persists evidence.
The descriptor does not grant access to Everyone, Authenticated Users, or
other machine sessions.

The native target depends on Chromium `//base` and must be compiled and tested
inside a Chromium source checkout. On September 18, 2026, the bridge was
compiled into Chromium 156.0.8065.0 on the reference Windows platform. The
instrumented browser then completed authentication and clock synchronization
with the recorder. The finalized session archive contained one
`browser-connected` record followed by one `browser-clock-synchronized` record,
with no rejected connection, dropped record, or archive-validation issue.
Details and reproducible commands are in the
[validation record](../validation/chromium-connection-2026-09-18.md).

Successful connections are persisted on the `browser.lifecycle` channel:

- `browser-connected` is emitted only after the hello message and
  authentication token are validated.
- `browser-clock-synchronized` is emitted only after the clock exchange
  completes and the recorder sends `ready`.
- Child-process hello messages are accepted only for renderer processes and
  must include positive browser parent and Chromium child process identifiers.
- Neither record contains the authentication token.
- A failed authentication or handshake produces
  `browser-connection-rejected` instead of a successful lifecycle sequence.

## Instrumentation sequence

1. Implement browser lifecycle, process identity, IPC authentication, clock mapping, and omission records.
2. Instrument listener registration and removal.
3. Instrument dispatch phases, listener invocation, propagation control, cancellation, and default actions.
4. Instrument timeout, interval, animation-frame, and idle-callback lifecycle
   evidence, including page lifecycle state at the observed boundaries,
   followed by scheduler-throttling evidence.
5. Add document, DOM, style, layout, accessibility, and rendered-frame
   checkpoints. The bounded parser-complete DOM structure checkpoint is the
   first implemented part of this stage.
6. Add cookie operations and network metadata with prohibited values removed at source.
7. Add browser-chrome and compositor correlation needed by test scenarios.
8. Package the browser and recorder as one installable application.

Each stage requires fixture pages and archive-level tests before the next stage begins.

## Initial fixture tests

- A `div` with only a click listener is activated by pointer and is unreachable by keyboard.
- A custom control listens for Enter but not Space.
- A keydown listener cancels the browser default action.
- A listener stops propagation before an ancestor activation handler.
- A control is removed and replaced after a timeout.
- A timeout is delayed by main-thread work.
- Backgrounding causes timer throttling before a time-sensitive prompt changes.
- A cookie is read before a consent notice is exposed.
- A cookie is written before consent interaction.
- A consent-state cookie is read to decide whether the notice should be shown.
- A listener is attached in shadow DOM or an isolated world.
- A native browser default action occurs without a page listener.

Expected records must be asserted by stable identifiers and relationships, not only by event counts.

## Maintenance rule

The Chromium fork should remain narrow. Instrumentation hooks call a small recorder-owned evidence layer and avoid unrelated product changes. Upstream revisions are integrated on a controlled cadence, and every rebase runs the fixture suite to detect moved hooks, missing paths, changed scheduling behavior, and protocol incompatibility.

## Existing-checkout upgrades

The integration script is applied repeatedly to the same Chromium checkout, so it must both skip work that is already present and rewrite hook bodies that an earlier protocol revision wrote. Recorder-owned files such as the bridge sources are copied wholesale on every run and therefore always match the current protocol. Hooks patched in place inside upstream Chromium sources do not, because presence guards keyed on a symbol name treat an older hook body as already integrated.

Every protocol revision that changes the arguments of a bridge entry point must therefore keep the previous hook body as a named template and replace it with the current body before the presence guards run. Without that replacement the checkout retains an older call shape while the copied bridge header advances, and the Chromium build fails with argument-count errors at the stale call sites rather than at integration time.

The integration tests cover this by patching fixtures that already contain the previous revision's hook bodies, asserting that the current bodies replace them, and asserting that a second run changes nothing.

## Bridge signature verification

A presence guard cannot distinguish a superseded hook body from a current one, so the protocol revision that omits a replacement template is not detected by the integration script itself. Integration therefore verifies argument counts directly.

The script parses the declared parameter count of every exported entry point in the recorder bridge header, then compares that count against every call site it can observe. Before any file is modified it checks the hook templates it is about to write. After all patching completes it re-reads every Chromium source it inspected, including files a presence guard left untouched, and checks the call sites those files actually contain. A disagreement fails integration with the file, line, entry point, observed argument count, and declared parameter count. Calling an entry point the bridge does not declare fails the same way.

Integration also fails when a template describing a superseded call shape is declared but never referenced by an in-place upgrade. That is the shape of the omission itself: the previous body is preserved for reference while nothing replaces it in an existing checkout.

Two limits are deliberate. Argument counting is textual, so a template holding only the leading arguments of a call it rewrites in place cannot be checked in isolation; the assembled Chromium source is checked instead. Argument counts are compared, not types, so a revision that changes a parameter's type without changing the count is not detected by this check and still relies on the Chromium build.

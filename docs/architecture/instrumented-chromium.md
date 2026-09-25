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

Protocol 0.24 implements the change records for focus, selection, active
descendant, and text-editing state on the `browser.interaction` channel. The
record types and their limits are described under protocol version 0.24 below.
Protocol 0.29 adds checkpoint-time snapshots of that state, described under
protocol version 0.29 below.

Protocol 0.25 implements layout geometry and a defined list of computed styles
on the `browser.layout` channel, recorded after every rendering update in which
style or layout work happened. The record types and their limits are described
under protocol version 0.25 below and in
[the layout and computed-style checkpoint evidence model](layout-and-style-checkpoint-evidence-model.md).

### Cookies and network

Cookie evidence includes:

- Operation: read, write, delete, send, receive, or block.
- Cookie name.
- Domain, path, SameSite, Secure, HttpOnly, partitioning, expiry class, and source API where available.
- Request, response, document, frame, and navigation correlation.
- Whether the operation succeeded or was blocked and the reason.

Protocol 0.23 implements the cookie part of this evidence on the
`browser.cookie` channel. The record types and their limits are described under
protocol version 0.23 below.

Cookie values are never recorded. Authorization values, saved credentials, request bodies, and response bodies are not recorded.

Network evidence includes request and response metadata, initiator, resource type, redirect chain, status, cache behavior, timing, and cookie names associated with the transaction.

Protocol 0.26 implements network metadata on the `browser.network` channel.
Header names are recorded with their values, except that the values of cookie
and authorization headers, and of headers whose name or value marks them as a
credential, are withheld at source. The record types and their limits are
described under protocol version 0.26 below and in
[the network metadata evidence model](network-metadata-evidence-model.md).

Protocol 0.27 adds WebSocket, EventSource, and WebTransport records to the
same channel. Handshake cookies are listed by name, message text is kept with
any part that looks like a credential withheld, and binary message content is
not recorded. The record types and their limits are described under protocol
version 0.27 below and in
[the realtime network evidence model](realtime-network-evidence-model.md).

Protocol 0.28 extends DOM checkpoints, layout checkpoints, and dispatch paths to
shadow trees and pseudo-elements. The record types and their limits are
described under protocol version 0.28 below and in
[the shadow DOM and pseudo-element evidence model](shadow-dom-evidence-model.md).

Protocol 0.29 records the interaction state a document holds after each DOM
checkpoint and each layout checkpoint. The record types and their limits are
described under protocol version 0.29 below and in
[the interaction-state checkpoint evidence model](interaction-state-checkpoint-evidence-model.md).

Protocol 0.30 records the compositor frame and presentation time of each
layout checkpoint. The record types are described under protocol version 0.30
below and in
[the rendered-frame correlation evidence model](rendered-frame-correlation-evidence-model.md).

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

The recorder keeps one pending pipe instance at a time and creates the next one
after a connection is accepted, so a process that opens the pipe while another
process is being accepted is told the pipe is busy. Chromium starts several
renderers at once, which makes that contention ordinary rather than a sign that
the recorder has gone away. A single open attempt therefore cost a whole
process: the bridge hook fails the process when initialization fails, so a
renderer that lost the race exited, its evidence was absent from the archive,
and nothing else in the session recorded that it had existed. A process now
waits for a free instance until a 15 second deadline expires, retrying after
both a busy pipe and a pipe that is momentarily absent, and treats every other
error, an access denial above all, as a pipe it will never be allowed to open.
A failure reports the Windows error and the time waited, because a bare message
cannot distinguish contention that outlasted the deadline from a descriptor
that excludes the caller. A connection that had to wait is recorded in the
bridge startup log, and validation fails when any process reports a bridge
initialization or connection failure, since the archive can otherwise validate
and the deterministic verifier can otherwise pass with a process missing.

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

Protocol version 0.15 adds bounded attribute and character-data evidence. The
checkpoint hooks in `Document::FinishedParsing` and the mutation delivery pass
emit `dom-checkpoint-node-attribute` for each attribute of each element node
they record. `Element::DidAddAttribute`, `Element::DidModifyAttribute`, and
`Element::DidRemoveAttribute` route through one file-local recorder helper that
records `dom-attribute-changed`, and `CharacterData::SetDataAndUpdate` records
`dom-character-data-changed` for updates that did not come from the parser.
Both transition hooks queue the mutated document through the existing recorder
checkpoint path, because neither mutation changes a child list. Live 0.15
connections require an exact protocol-version match.

Protocol version 0.16 reverses the direction of the join between a transition
and the tree state around it. In 0.15 the bridge reserved the identity of the
checkpoint that the current delivery pass was expected to produce, and the
transition named it. Validation showed that promise cannot be kept: a document
can be created, mutated, and discarded before any delivery pass produces a
checkpoint, which left 200 of 452 transitions naming absent evidence, and an
unconsumed reservation was reused by every later transition in the same
document. In 0.16 each transition carries its own `transitionId` from a
per-renderer sequence, and each completed checkpoint reports
`coveredTransitionCount`, `coveredTransitionFirstId`, and
`coveredTransitionLastId`. No record can name evidence the archive does not
contain, and an uncovered transition is stated by omission. Both counters live
in the bridge, so no Blink hook signature or body changed. Live 0.16 connections
require an exact protocol-version match.

Protocol version 0.17 records the accessibility updates and events the renderer
is about to send from
`RenderAccessibilityImpl::SendAccessibilitySerialization()`. Each operation
emits a start record, zero or more AX node records, and a completion record with
explicit update, event, node-limit, node-count, and truncation fields. Records
carry Chromium's document token and renderer process identity for correlation
with committed navigation. The launcher uses
`--force-renderer-accessibility` for deterministic proof-of-concept capture.
These records are incremental serialization batches and do not claim to be
complete accessibility-tree snapshots. The complete contract is defined in the
[accessibility checkpoint evidence model](accessibility-checkpoint-evidence-model.md).
Live 0.17 connections require an exact protocol-version match.

Protocol version 0.18 records listener and dispatch evidence for EventTargets
that are not Nodes, beginning with the window. Every target reference now
reports its kind, its Blink interface name as observed rather than as a
cross-version identity, and a process-local target identifier for a target that
is not a Node, and reports a null node identifier
where no DOM node exists. The window entry at the end of a composed path is
taken from Blink's `WindowEventContext`, which exists exactly when Blink will
run window listeners for that event, so the recorded path ends where Blink's
path ends. A dispatch whose original target is not a Node does not pass through
`EventDispatcher::Dispatch` and is not recorded by this increment, and worker
global scopes remain outside it. Protocol 0.31 records both.

Protocol version 0.19 reports how each listener entered Blink's listener map.
Blink funnels an `addEventListener` call, an inline `on*` content attribute, and
an `on*` property assignment through `EventTarget::AddEventListenerInternal`, so
the call site does not distinguish them and the form is read from the listener
object Blink created: a content-attribute event handler is an inline attribute,
any other event handler came from a property assignment, and a listener that is
neither arrived through `addEventListener`. `EventTarget::SetAttributeEventListener`
replaces the callback of an existing attribute registration in place and
returns, so neither the add hook nor the remove hook runs on that path and the
archive would otherwise keep reporting the form of a callback Blink no longer
holds. That path emits a `listener-callback-replaced` record which keeps the
listener identity and reports the form of the replacing callback. A form outside
the schema is normalized to `add-event-listener`, because an out-of-schema value
would fail archive validation for the whole session rather than for one record.

Protocol version 0.20 reports where each listener record came from.
`CaptureSourceLocation(ExecutionContext*)` is called in each listener hook and
returns Blink's own `SourceLocation`, whose `Url`, `ScriptId`, `LineNumber`,
`ColumnNumber`, and `Function` accessors are written into the record's
`location`. Because the capture happens at the hook, the location describes the
call that registered, removed, or replaced the listener and not the definition
site of the callback. A registration Blink performs while no script is running,
such as one an inline attribute creates during parsing, reports the parsing
location Blink can supply, which the reference run showed to be the parser
position when the listener was created rather than the position of the attribute
text, and reports a null location only when Blink can supply nothing.
`SourceLocation` states that a zero line or column means unknown, so a zero line,
column, or script identifier and an empty URL or function name are each recorded
as null. `sourceHash` is always null, because the recorder does not read script
text and cannot report a hash it did not compute. The capture walks the top of
the JavaScript stack for every listener record, and no listener record is
suppressed to avoid that cost.

Protocol version 0.21 reports the world each listener callback belongs to.
`JSBasedEventListener::GetWorldForInspector()` returns the `DOMWrapperWorld` the
callback was created in, which is the world the registration was made from and
not whichever world is current when the record is written. The record's `world`
reports `kind` from the world type, `blinkWorldId` from `GetWorldId()`, and
`name` and `stableId` from `NonMainWorldHumanReadableName()` and
`NonMainWorldStableId()`. Those two accessors assert that the world is not the
main world, so they are called only for a world other than the main world and a
main-world registration reports both as null. The context's `executionWorldId`
repeats the same world as `world-<blinkWorldId>`, so records from one world can
be grouped without reading the payload. An `EventListener` that is not a
`JSBasedEventListener`, such as one Blink installed itself, belongs to no world:
that record reports a null `world` and a null `executionWorldId` rather than
claiming the main world. Blink classifies both isolated and inspector-isolated
worlds as isolated, so the inspector's worlds are tested first and reported as
`inspector-isolated`. A world type the recorder does not name is reported as
`other`, because an out-of-schema value would fail archive validation for the
whole session.

Protocol version 0.22 states browser evidence that was lost rather than
captured. Two paths can lose a record. A renderer's own pipe write can fail,
which previously produced a line in the bridge log and nothing in the archive,
and the recorder's bounded event sink can refuse a record, which previously
appeared only as degraded collector health and a count in the manifest. Neither
loss was visible to a reader of the archive, who saw a gap indistinguishable
from an event that never happened. Both reporters now hold the number of lost
records per channel and emit a `collector-omission` record on that same channel
as soon as a write succeeds again, carrying the reason, the count, and the
browser process context when the reporter knows which process lost them. The
bridge holds its counts behind a lock, because a renderer writes evidence from
more than one thread, and it reports the loss immediately before its next
successful write on that channel, so the omission precedes the first record that
survived. An omission record is not itself captured evidence, so a failure to
write the omission returns the held count unchanged rather than counting the
omission as one more lost record.

This reporting is bounded by where it runs. A process that loses records and
then exits, or whose pipe never recovers, never gets to report the loss, and no
reporter inside that process can fix that. An archive that reports no omission
is therefore evidence of no observed loss rather than proof that nothing was
lost. The reference run treats any stated loss as a failed run, because a
reference run must be lossless, while the verifier reports the omission counts
without failing, since a stated omission is a true account of what happened.

Protocol version 0.23 records cookie operations on the `browser.cookie` channel.
Cookie values are never recorded. Every record carries cookie names and the
non-value facts of the operation, and the value is dropped inside the hook or
the bridge before a record is built.

Six record types are emitted:

- `document-cookie-read`: a `document.cookie` read. Written from
  `CookieJar::Cookies` and from the refusal branches of `Document::cookie`. It
  carries the outcome (`returned`, `not-attempted-no-cookie-url`,
  `cookie-manager-call-failed`, `refused-no-window-or-cookies-disabled`, or
  `refused-security-error`), whether the names came from the cookie manager or
  from the renderer's cookie cache, and the names returned.
- `document-cookie-write`: a `document.cookie` write. Written from
  `CookieJar::SetCookie` and from the refusal branches of `Document::setCookie`.
  It carries the outcome, the cookie name, and the attributes the written string
  named (Domain, Path, SameSite, Partitioned, and whether Expires, Max-Age,
  Secure, or HttpOnly were present), with the attribute names in the order they
  were written.
- `cookie-store-request`: a Cookie Store API call (`get`, `getAll`, `set`, or
  `delete`). It carries a request identifier, the method, the context kind
  (`window`, `service-worker`, or `other`), the outcome (`sent-to-cookie-manager`
  or `threw`), the requested name and URL when given, and for a write the
  requested attributes.
- `cookie-store-result`: the resolution of a Cookie Store request that was sent,
  paired to its request by `requestId`. A read reports the names the cookie
  manager returned. A write reports whether the browser reported success.
- `cookie-store-change`: a change delivered to a Cookie Store change
  subscription, with the name, domain, path, Chromium's change cause, and
  whether a change event was dispatched to the page.
- `cookie-access`: a cookie access the browser process observed, from
  `RenderFrameHostImpl::NotifyCookiesAccessed` (`observer` is `frame`) and from
  `NavigationRequest::NotifyCookiesAccessed` (`observer` is `navigation`). This
  covers cookies sent with and set by HTTP responses, including Set-Cookie
  headers. It carries the access type (`read` or `change`), the URL, the frame
  and top-frame origins, the request identifier when Chromium supplies one,
  whether the frame is ad tagged, and for each cookie its name, domain, path,
  SameSite, Secure, HttpOnly, host-only, partitioned, persistent, and expired
  flags, whether it was included, and Chromium's exclusion, warning, and
  exemption reasons.

The renderer records (`document-cookie-read`, `document-cookie-write`, and
`cookie-store-request`) also report the script location of the call and the
JavaScript world current at the call, in the same `location` and `world` shapes
the listener records use, and repeat the world as the context's
`executionWorldId`. These are the only records outside the listener channel
that report a world.

The recorded facts are bounded as follows:

- The network service skips a consecutive duplicate access and batches its
  notifications, so a `cookie-access` record is not a one-to-one count of
  requests.
- An excluded cookie is reported only when Chromium reports it to the
  observer.
- Service worker and shared worker cookie observers are not hooked, so their
  network cookie accesses produce no `cookie-access` record.
- A Cookie Store call made from a service worker has no document, so its
  document fields are null.
- Exclusion, warning, and exemption reasons are Chromium's debug names. They
  are not a cross-version contract.
- A nameless cookie whose value contains `=` cannot be told apart from a named
  cookie, so the name reported for it is the text before the first `=`.
- `navigator.cookieEnabled` is not recorded.
- Name and cookie lists are capped at 256 entries. The record reports the full
  count and whether the list was truncated.
- A `cookie-store-result` record has no document context. It is correlated with
  its request by `requestId`.
- A `document-cookie-write` outcome of `sent-to-cookie-manager` states that the
  write was sent. Whether the browser stored it is stated by the matching
  `cookie-access` record, when Chromium reports one.
- `secure`, `httpOnly`, `maxAgePresent`, and `attributeNames` appear only in a
  `document-cookie-write` record. The Cookie Store attribute shape omits them,
  so a Cookie Store write that set a maximum age is not distinguished from one
  that did not.
- A Cookie Store `delete` reports the attributes of the expiring write Blink
  builds for it (expiry at time zero and SameSite strict), not attributes the
  script supplied.
- A Cookie Store `get` result lists every name the cookie manager returned,
  although Blink resolves the promise with only the first.
- Whether a `cookie-access` came from a network request or from a script call
  is not recorded.
- The location and world reported are those current when the call is made. A
  call made while no script context is entered reports a null location and a
  null world rather than the main world.

The validation run serves a second fixture page over HTTP on the loopback
interface, because cookie APIs refuse a file URL and a Set-Cookie header needs
an HTTP response. The page writes and reads `document.cookie`, calls each Cookie
Store method with a change listener registered, and fetches one response that
sets a cookie and one request that sends it. The verifier requires a record of
each of those operations and requires that no record in the session contains
the value the fixture cookies carried. The fixture shows that the logger emits
records; it does not evaluate the page's cookie use.

Protocol version 0.24 records focus, selection, text-control value, and
element-reflected active descendant changes on the `browser.interaction`
channel. Each record reports the state Blink holds once the change is
committed. Node identities are Blink DOM node ids, the same identities the DOM
checkpoint and mutation records carry, so a record can be joined to the DOM
structure of the same document. Every record carries the document context the
cookie records use, and reports the script location and JavaScript world of the
script that made the change, in the `location` and `world` shapes the listener
records use, with the world repeated as the context's `executionWorldId`. Both
are null for a change no script made, such as a key press, a click, or typed
text.

Four record types are emitted:

- `focus-changed`: one request to change a document's focused element, written
  on every return from `Document::SetFocusedElement` after its early checks. It carries the node that
  held focus before, the node focus was requested for, the node that holds
  focus afterwards, and an outcome derived from those three: `focused` when the
  requested node holds focus, `cleared` when no node was requested and none
  holds focus, `not-focused` when a node was requested and none holds focus,
  and `redirected` when a node other than the requested one holds focus. It also carries Blink's focus type
  (`none`, `script`, `forward`, `backward`, `spatial-navigation`, `mouse`,
  `access-key`, or `page`), the focus trigger (`script` or `user-gesture`),
  `preventScroll`, `focusVisible` when the request stated it, and the node the
  focused element's `aria-activedescendant` resolved to at that moment.
  The focus trigger is Blink's `FocusParams::focus_trigger` as passed to the
  call, not a judgement of whether the user caused the change. Blink passes
  `user-gesture` for sequential keyboard navigation, but its ordinary mouse
  focus path leaves the default, `script`, so a mouse click that focuses a
  control is recorded with focus type `mouse` and trigger `script`. Whether a
  page script made the call is shown by the record's script location and
  world, which are null for a change made by input.
- `selection-changed`: the selection a frame holds once
  `FrameSelection::SetSelection` commits a change. It carries whether the user
  or the system set it, the selection type (`none`, `caret`, or `range`), the
  anchor and focus container nodes and offsets, and whether the selection is
  directional. When the anchor is inside a text control it also carries the
  control's node and the control's own selection start, end, and direction.
- `text-control-value-changed`: a text control's value after a value set or a
  user edit. Written from `HTMLInputElement::SetValue`,
  `HTMLTextAreaElement::SetValue`, `TextFieldInputType::SubtreeHasChanged`, and
  `HTMLTextAreaElement::SubtreeHasChanged`. It carries the control's node, its
  form control type, the source (`value-set` or `user-edit`), the value, and the
  control's selection start, end, and direction after the change.
- `active-descendant-reference-set`: an element assigned to
  `ariaActiveDescendantElement`, written from `Element::SetElementAttribute` for
  that attribute. Element reflection leaves the referenced element out of the
  attribute state that the DOM records report, so this record is the only
  account of the reference. It carries the element's node and the referenced
  node.

The recorded facts are bounded as follows:

- Text-control values are recorded verbatim, including the values of password
  fields, under the policy that already applies to DOM attribute values and
  character data. A value is bounded to 4096 UTF-16 code units. The record
  reports the full length and whether the value was truncated.
- Focus cleared while a document shuts down is not recorded.
- A focus request for the element that already holds focus, for an element in
  another document, or for an element being removed returns before the hook
  and produces no record.
- A selection that Blink adjusts because the DOM around it was mutated or
  removed, without a call to `FrameSelection::SetSelection`, is not recorded.
- A `selection-changed` record reports positions in the DOM tree. It does not
  carry the selected text.
- `aria-activedescendant` set as an ID-referencing attribute is reported by the
  DOM attribute records rather than by this channel. A `focus-changed` record
  reports what the reference resolved to only at the moment of the focus
  change.
- Element reflection for other element and element-array attributes, such as
  `ariaControlsElements` and `ariaLabelledByElements`, is not recorded.
- Text-control values are not included in DOM checkpoints, and these records do
  not trigger a DOM checkpoint.
- A focus change in a document whose DOM node identity is not yet assigned
  produces no record.
- Before protocol 0.29, checkpoint-time snapshots of focus, selection, and
  text-editing state were not recorded, and the state at a given moment had to
  be reconstructed from the change records. From protocol 0.29 a snapshot
  follows each DOM and layout checkpoint; the state between two checkpoints is
  still covered only by the change records.

The validation run serves a third fixture page from the loopback HTTP listener
the cookie fixture uses, opened in a foreground tab after the listener fixture
has been hidden, because DevTools input reaches only a page that has painted.
The page's script focuses
a button, and a Tab key press sent as DevTools input moves focus to a text
field. Text typed through DevTools input changes the field, the page's script
sets the field and a textarea by value, focuses the textarea, and selects part
of it, and typed text replaces the selection. The script then assigns an
active descendant to a listbox by element reflection, focuses the listbox, and
blurs it. The verifier requires a record of each of those changes, requires the
script location and main world on the changes made by script, and requires no
script origin on the changes made by input. The fixture shows that the logger
emits records; it does not evaluate the page's focus handling.

Protocol version 0.25 records layout geometry and computed styles on the
`browser.layout` channel. A hook in `LocalFrameView::UpdateLifecyclePhases`
runs after each lifecycle update that reached the paint-clean state and, for
every local frame view that is not throttled, records a checkpoint when Blink
has resolved element style or performed layout for the document since its
previous checkpoint. The hook reads only style and layout Blink has already
produced and never forces either. Three record types are emitted:

- `layout-checkpoint-started`: the checkpoint identity, the document's previous
  checkpoint, Blink's style-resolution and layout counters, the viewport size
  and scroll offset in CSS pixels, the device pixel ratio, the layout zoom
  factor, the node limit of 100000, and the list of recorded computed-style
  properties.
- `layout-checkpoint-node`: one element, or one text node that has a layout
  object, in light-DOM tree order, with its Blink DOM node identity, whether it
  has a layout object, whether a display lock prevents its layout, its
  viewport-relative bounding rectangle, and, for an element with a current
  computed style, the resolved value of each listed property.
- `layout-checkpoint-completed`: the node count and whether the node limit
  truncated the checkpoint.

The recorded facts are bounded as follows:

- Only the 283 listed properties are recorded. The list and the reason for
  each property are in the evidence model.
- Shadow-tree content and pseudo-elements are recorded from protocol 0.28,
  within the limits given in
  [the shadow DOM and pseudo-element evidence model](shadow-dom-evidence-model.md).
- The rectangle is the bounding box only; line boxes and fragments are not
  recorded separately.
- Documents that are not painted, such as those in background tabs, and frames
  whose rendering is throttled produce no checkpoints until they are rendered.
- A page that changes style or layout on every frame produces a full
  checkpoint on every frame.
- A layout a script forces is observed at the next paint-clean update, not at
  the moment it was forced.

The validation run serves a fourth fixture page from the loopback HTTP listener
the cookie fixture uses, opened in a foreground tab after the interaction
fixture, because only a painted document produces layout checkpoints. The page
widens a box and then changes only its color, reporting the box's rectangle,
the viewport size, and the box's color after each change. The verifier requires
complete, linked checkpoints for the fixture document whose recorded values
agree with what the page reported. The fixture shows that the logger emits
records; it does not evaluate the page's layout or styling.

Protocol version 0.26 records network metadata on the `browser.network`
channel. Hooks in Blink's frame and worker resource load observers and in its
resource fetcher record renderer loads; hooks in the browser's network service
DevTools observer record the headers the network service reports sending and
receiving; and a hook after the navigation-completed record records each
finished navigation's request and response. Eight record types are emitted:

- `request-will-be-sent`: a request or a redirect of it, with its URL, method,
  resource type, initiator, fetch mode and cache mode, priority, referrer, the
  headers Blink holds, the redirect response for a redirect, and the script
  that was current.
- `response-received`: the response metadata, headers, and load timing.
- `request-finished`: the encoded and decoded lengths and the finish time.
- `request-failed`: the network error and the kind of failure.
- `memory-cache-hit`: one use of a resource Blink already held in memory.
- `request-headers-sent`: the headers the network service sent, and the
  cookies it attached or excluded, by name.
- `response-headers-received`: the status and headers the network service
  received, and the cookies the response set or tried to set, by name.
- `navigation-response`: a finished navigation's redirect chain, request
  headers, response head, and navigation timing.

The recorded facts are bounded as follows:

- No request or response body is recorded.
- `Cookie`, `Set-Cookie`, `Set-Cookie2`, `Authorization`, and
  `Proxy-Authorization` values are withheld, as are the values of headers whose
  name holds a credential word or whose value begins with an HTTP
  authentication scheme or contains a JSON Web Token. The name and the reason
  are recorded.
- A header list holds at most 256 headers.
- URLs are recorded in full, including query strings.
- Requests the browser makes for itself are not recorded.
- Wire headers are not recorded for worker loads made through a factory that
  has no DevTools observer.

While the recorder is connected, Blink and the navigation loader assign a
DevTools request identifier to each request that has none, so that the network
service reports wire headers for it. This is the one network hook that changes
Chromium's behaviour rather than only reading it; without a recorder
connection the browser behaves as stock.

The validation run serves a fifth fixture page from the loopback HTTP listener
the cookie fixture uses, reached through a redirect in a background tab after
the layout fixture. The page sends a fetch with credential-bearing and plain
headers, follows a redirected fetch, fetches from a closed port, loads one
cacheable script into the page and again into a new frame, and fetches from a dedicated worker. The verifier
requires records for each of those loads, linked by identifier, with every
credential value absent from the session and a fixture cookie listed by name.
The fixture shows that the logger emits records; it does not evaluate the
page's network use.

Protocol version 0.27 records the realtime channels a page opens on the
`browser.network` channel. Hooks in Blink's WebSocket channel, EventSource,
and WebTransport implementations, at the points where Blink reports each
channel to DevTools, emit thirteen record types:

- `websocket-created`, `websocket-handshake-request`,
  `websocket-handshake-response`, `websocket-message-sent`,
  `websocket-message-received`, `websocket-close-requested`,
  `websocket-error`, and `websocket-closed`.
- `event-source-message`: each event a stream dispatches.
- `web-transport-created`, `web-transport-established`,
  `web-transport-close-requested`, and `web-transport-closed`.

The recorded facts are bounded as follows:

- Handshake headers follow the protocol 0.26 header value rules, and the
  handshake records list the cookies sent and set by name.
- Message text, event data, last event identifiers, and close reasons are kept
  up to 4096 UTF-16 code units from the first 65536 bytes. A part that looks
  like a credential, such as a JSON Web Token, an HTTP authentication
  credential, or the value of a field whose name holds a credential word, is
  replaced by `[withheld]` and its offset and reason are recorded.
- Binary message content is not recorded; its length is.
- WebTransport stream and datagram data is not observed.

The network service removes cookie headers from the WebSocket handshake it
reports to the renderer unless the renderer has raw header access. In a
recording browser, a hook in the network service reports those headers with
every value replaced instead, so the names reach the renderer and no value
leaves the network service. The browser process marks the network service
utility process with a non-secret switch while the recorder is connected;
without it the network service behaves as stock. `Authorization` headers stay
removed.

The network logging fixture page also opens a WebSocket to the loopback
listener, whose handshake sets a cookie, exchanges text and binary messages
with it, and closes it; reads two events from an event stream; and creates a
WebTransport session to a closed port and closes it while connecting. The
generated credential values it sends and receives must be absent from the
session. The fixture has no HTTP/3 server, so WebTransport establishment is not
covered by the validation run.

Protocol version 0.28 extends three record families to the composed tree:

- DOM checkpoints visit open, closed, and user-agent shadow roots after their
  hosts. Each shadow root is a `dom-checkpoint-node` of type `shadow-root`
  parented by its host, followed by a `dom-checkpoint-shadow-root` record with
  its mode and options. Each slot is followed by a
  `dom-checkpoint-slot-assignment` record with its assigned nodes and whether
  Blink held that assignment as current. The completion adds shadow root and
  slot counts.
- Layout checkpoints record elements and laid-out text inside shadow trees,
  with the host and mode of their tree, and every pseudo-element Blink has
  created, as a `pseudo-element` node with its originating node, type, and
  generated text up to 4096 characters. The completion adds pseudo-element and
  shadow root counts.
- `dispatch-started` adds `pathScopes`, one per composed path entry, with the
  entry's tree scope root and mode, its retargeted target and related target,
  and the path indexes `composedPath()` returns to a listener there.

The hooks read what Blink holds. They never request a slot assignment
recalculation, create a pseudo-element, or force style or layout. The visible
path indexes come from the per-scope cache Blink fills for `composedPath()`, so
the values match what a page listener receives.

A shadow DOM logging fixture page attaches an open shadow root with named and
default slots, a closed shadow root with manual slot assignment, and holds an
input, `::before` and `::after` content, and a list marker. It then clicks a
button inside the closed shadow root. The verifier requires the three shadow
roots with their modes and options, current assignments of the expected nodes,
the four pseudo-elements with their text, nodes in each kind of shadow tree,
and path scopes that give each listener the visible path length the page
reported. The fixture shows that the logger emits records; it does not
evaluate the page's use of shadow DOM.

Protocol version 0.29 follows each DOM checkpoint and each layout checkpoint
with a snapshot of the document's interaction state on the
`browser.interaction` channel, in the same synchronous call:

- `interaction-checkpoint-started` names the source checkpoint, its channel
  and reason, and records whether the document has focus, the element Blink
  holds as focused, whether it matches `:focus-visible`, the element its
  active descendant resolves to, how focus last moved, and the frame
  selection's type, positions, and directionality.
- `interaction-checkpoint-text-control` records each text control in
  composed-tree order, including controls in shadow trees of any mode, with
  its type, its value up to 4096 UTF-16 code units, and its selection.
- `interaction-checkpoint-completed` records the number of text controls and
  whether the traversal stopped at its limit of 512.

Every record carries the renderer document context with no execution world.
Accessibility checkpoints carry no snapshot. The snapshot reads only state
Blink already holds and never requests style, layout, or a selection update.
Values are recorded verbatim, including password fields, as the change records
are. No deduplication is performed.

The interaction fixture page's final step focuses its listbox, whose active
descendant is already set, changes an inline style so the next rendering
update resolves style, and waits two animation frames with the listbox still
focused. The verifier requires a snapshot of the fixture document's
`finished-parsing` DOM checkpoint with no focused element and empty text
controls, and a snapshot of a later layout checkpoint with the listbox focused,
its active descendant resolved, and the values the fixture set and typed. For
every snapshot in the capture it requires a recorded source checkpoint of the
same document and reason, one completion, and a text-control count equal to
the records. The fixture shows that the logger emits snapshots; it does not
evaluate the page's focus handling. The full model is in
[the interaction-state checkpoint evidence model](interaction-state-checkpoint-evidence-model.md).

Protocol version 0.30 follows each layout checkpoint, after its completion
record and before its interaction snapshot, with a `presentation-requested`
record on the `browser.presentation` channel. The layout checkpoint helper
asks the frame's local-root `WebFrameWidgetImpl` to queue a recorder
`cc::SwapPromise` on its `LayerTreeHost`; queuing never requests a commit, a
frame, or a lifecycle update. When the frame has no local-root widget, or the
widget does not composite, the request states that and nothing is queued.
The promise then reports:

- `presentation-not-swapped` for each `DidNotSwap`, with the reason and
  whether the promise was broken (`swap-fails`, `commit-no-update`) or kept
  for a later frame, capped at 16 kept records per request;
- `presentation-swapped` at `DidSwap`, with the compositor frame token read
  in `WillSwap`; and
- `presentation-feedback` when viz reports presentation of that token, with
  the presentation time, refresh interval, feedback flags, and the viz
  receive, draw, and swap times.

Every record carries the document context, the request identity, the layout
checkpoint identity on the request, and the widget's frame sink identity and
local-root frame token. Frame tokens, tick values, and microsecond values are
decimal strings. The Blink validation requires the interaction fixture's
held-focus layout checkpoint to be queued, swapped with a nonzero token, and
presented without the `failure` flag no earlier than its swap, and checks the
joins, single outcome, and frame-token order of every request. The fixture
shows that the logger emits the records; it does not show what any frame
displayed. The full model is in
[the rendered-frame correlation evidence model](rendered-frame-correlation-evidence-model.md).

Protocol version 0.31 records the listener dispatches that do not pass
through Blink's Node event dispatcher, and names the execution context of
every listener and dispatch record. `EventTarget::DispatchEventInternal`,
`LocalDOMWindow::DispatchEvent(Event&, EventTarget*)`, and
`IDBEventDispatcher::Dispatch` each open a dispatch record when no hook has
opened one for the same event, so a dispatch to a non-Node target, a window's
load and pageshow dispatch with the document as target, and an IndexedDB
request, transaction, and database dispatch are recorded with their path and
listener invocations. Every listener and dispatch record carries a `scope` in
the shape network records use, with the context kind, the worker's DevTools
token, and the global object URL. A record in a dedicated, shared, or service
worker, or in a worklet, names no document, and only a non-Node target can
occur there. Web Serial's dispatch path is not recorded, and worklet records
are produced but not validated. The full model is in
[the worker and non-Node dispatch evidence model](worker-and-non-node-dispatch-evidence-model.md).

Live 0.31 connections require an exact protocol-version match.

The recorder's managed payload contracts are part of the protocol surface, not a
convenience. Evidence ingest deserializes every payload into a typed record and
rejects unmapped members, and the receive loop treats a rejection as a failed
connection and closes that process's pipe. A bridge field with no matching
contract property therefore costs the rest of that renderer's evidence for the
whole session instead of failing one record. The first 0.16 build renamed a
transition field and added three checkpoint completion fields without updating
the contracts, and the capture recorded 203 events where the comparable 0.15
capture recorded 18,097, with four rejected connections and 221 failed evidence
writes. Ingest of each DOM payload shape is now covered by a platform-neutral
test, because the receiver test that exercises a live pipe does not run on every
platform.

Bounded values are truncated with `String::substr(0, limit)`. Blink's
`WTF::String` has no `Left` method in Chromium 156, and the first 0.15 hook
bodies used one, which failed to compile in `character_data.cc`. The corrected
bodies are carried as `INTERMEDIATE_` templates and replaced explicitly, because
the presence guards key on symbol names and would otherwise leave an
uncompilable body in an already-patched checkout. A body-level defect needs the
same migration treatment as a signature change.

Changing `CompleteBlinkDomCheckpoint` from seven to eleven arguments required
keeping the protocol 0.14 hook bodies as named historical templates in
`chromium/integrate.py`, because the presence guards key on symbol names and
would otherwise leave a stale call site in an already-patched checkout. The
integration-time signature checks added at revision `b7fd67b` enforce this.

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

Successful connections and browser exits are persisted on the
`browser.lifecycle` channel:

- `browser-connected` is emitted only after the hello message and
  authentication token are validated.
- `browser-clock-synchronized` is emitted only after the clock exchange
  completes and the recorder sends `ready`.
- Child-process hello messages are accepted only for renderer processes and
  must include positive browser parent and Chromium child process identifiers.
- `browser-exited` is emitted when a browser that completed startup exits,
  observed from the operating system rather than reported by the browser. It
  names the browser instance and process, the exit code as a signed integer
  and in the unsigned hexadecimal form Windows status codes are written in,
  the exit time Windows reports or null when it cannot, and
  `requestedByRecorder`. That flag is true only when the exit followed the
  recorder's own request to stop the browser. An exit the recorder did not
  request leaves the rest of the session without browser evidence, so the
  collector's health becomes `Degraded` with a stated reason, which the
  application shows in its collector list and announces once through a UI
  Automation notification. A browser that exits during startup is reported by
  the launch failure instead.
- Neither connection record contains the authentication token.
- A failed authentication or handshake produces no lifecycle record. The
  rejection is stated as a `collector-omission` record with the reason
  `browser-connection-rejected` on the `browser.listener` channel.
- The three lifecycle payloads are closed shapes the archive validator checks, as are
  the three accessibility checkpoint payloads. An accessibility checkpoint must
  name a renderer process and the Chromium document token it serialized, and
  carries no DOM document node identity, because the serialization is not taken
  at a DOM checkpoint.

Two diagnostic files support explaining a browser that closes during a
recording. Chromium's own log is written to `diagnostics\chromium.log` for every
launched browser. Chromium on Windows keeps crash reports in a `Crashpad`
folder inside its user data directory
([install_util.cc](https://source.chromium.org/chromium/chromium/src/+/main:chrome/install_static/install_util.cc)),
and Crashpad's Windows database writes each report as a `.dmp` file in its
`reports` folder
([crash_report_database_win.cc](https://chromium.googlesource.com/crashpad/crashpad/+/refs/heads/main/client/crash_report_database_win.cc)).
The recorder copies that folder's files into
`diagnostics\browser-crash-reports` after the browser has exited and before
the per-session profile is deleted. A failed copy is stated as a
`collector-omission` record with the reason `browser-crash-report-copy-failed`.
Crash reports are an exception to the privacy policy's rules on cookie values
and secrets, as the [privacy and data-handling
policy](../security/privacy-and-data-handling-policy.md) states. Whether this
build's crash handler writes reports at all has not yet been confirmed on the
reference platform.

## Instrumentation sequence

1. Implement browser lifecycle, process identity, IPC authentication, clock mapping, and omission records.
2. Instrument listener registration and removal.
3. Instrument dispatch phases, listener invocation, propagation control, cancellation, and default actions.
4. Instrument timeout, interval, animation-frame, and idle-callback lifecycle
   evidence, including page lifecycle state at the observed boundaries,
   followed by scheduler-throttling evidence.
5. Add document, DOM, style, layout, accessibility, and rendered-frame
   checkpoints. The bounded parser-complete DOM structure checkpoint is the
   first implemented part of this stage. Focus, selection, active descendant,
   and text-editing change records are implemented in protocol 0.24, and
   checkpoint-time snapshots of that state in protocol 0.29. Layout geometry
   and computed-style checkpoints are implemented in protocol 0.25.
   Rendered-frame checkpoints remain outstanding; their design is in
   `rendered-frame-correlation-evidence-model.md`.
6. Add cookie operations and network metadata with prohibited values removed at
   source. Cookie operations are implemented in protocol 0.23. Network metadata
   is implemented in protocol 0.26, and WebSocket, EventSource, and
   WebTransport records in protocol 0.27. Shadow-tree and pseudo-element
   content in DOM and layout checkpoints, and shadow-adjusted dispatch paths,
   are implemented in protocol 0.28.
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

A listener hook is upgraded by rewriting the region it introduced rather than by matching a remembered copy of its text. The script locates the single call to the bridge entry point, takes the innermost block that encloses it, and replaces that whole block with the current body. The block must contain no other bridge call and must open either on its own line or on a condition the script wrote, so a bridge call sitting directly in an upstream Chromium function body is refused instead of deleted. A checkout holding a body no revision of this script records is therefore still upgraded, and the script does not have to anticipate every shape it has ever written.

Hooks that are not upgraded this way must keep the previous hook body as a named template and replace it with the current body before the presence guards run. Without that replacement the checkout retains an older call shape while the copied bridge header advances, and the Chromium build fails with argument-count errors at the stale call sites rather than at integration time.

The integration tests cover this by patching fixtures that already contain the previous revision's hook bodies, asserting that the current bodies replace them, and asserting that a second run changes nothing.

## Bridge signature verification

A presence guard cannot distinguish a superseded hook body from a current one, so the protocol revision that omits a replacement template is not detected by the integration script itself. Integration therefore verifies argument counts directly.

The script parses the declared parameter count of every exported entry point in the recorder bridge header, then compares that count against every call site it can observe. Before any file is modified it checks the hook templates it is about to write. After all patching completes it re-reads every Chromium source it inspected, including files a presence guard left untouched, and checks the call sites those files actually contain. A disagreement fails integration with the file, line, entry point, observed argument count, and declared parameter count. Calling an entry point the bridge does not declare fails the same way.

Integration also fails when a template describing a superseded call shape is declared but never referenced by an in-place upgrade. That is the shape of the omission itself: the previous body is preserved for reference while nothing replaces it in an existing checkout.

Two limits are deliberate. Argument counting is textual, so a template holding only the leading arguments of a call it rewrites in place cannot be checked in isolation; the assembled Chromium source is checked instead. Argument counts are compared, not types, so a revision that changes a parameter's type without changing the count is not detected by this check and still relies on the Chromium build.

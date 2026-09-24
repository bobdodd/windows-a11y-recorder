# Blink Evidence Validation Plan

## Purpose

This document defines the first seven Blink evidence implementation slices and
the Windows validation required before any can be described as complete.
The first validated slice records accepted Node listener registrations and the
start of Node event dispatches. The second implemented slice correlates
listener removal and invocation with dispatch completion. The third implemented
slice records the ordered Node path and each invoked listener's current target.
The fourth implemented slice records decisions at Blink's Node
`DefaultEventHandler` boundary. The fifth implemented slice records correlated
window `setTimeout` and `setInterval` scheduling, callback entry, and explicit
interval cancellation.
The sixth implemented slice records web-exposed animation-frame scheduling,
callback entry, and explicit cancellation.
The seventh implemented slice records web-exposed idle-callback scheduling,
callback entry with `didTimeout`, and explicit cancellation.
It does not claim complete listener or dispatch coverage.

## Implemented hooks

### Listener registration

The integration script patches
`third_party/blink/renderer/core/dom/events/event_target.cc` inside
`EventTarget::AddEventListenerInternal`. Evidence is emitted only after Blink's
listener map accepts the registration and returns a
`RegisteredEventListener`.

The current hook records:

- The renderer process and browser instance.
- A process-local, monotonically allocated listener identifier for the
  accepted registration.
- The event name.
- The Blink document and DOM node identifiers.
- The target tag name and HTML `id`, when present.
- The resolved capture, passive, and once options.

- The kind of EventTarget the registration is on, its Blink interface name,
  and, for a target that is not a Node, a process-local target identifier.

The hook records registrations on Nodes and on EventTargets that are not Nodes,
including the window. Worker and worklet global scopes remain outside this
slice because their execution contexts are not document-scoped. Inline
attributes and `on*` properties are reported by the registration form described
below, the call that made the registration is reported by the registration
location described below, and the world the registration was made from is
reported by the execution-world identity described below.

### Listener removal and invocation

The second slice retains a process-local correlation from each accepted
`RegisteredEventListener` to its listener identifier. A successful
`removeEventListener` operation emits `listener-removed` with the identifier
allocated at registration. The ordinary Blink listener loop preserves that
identifier before automatic `once` removal, then emits `listener-invoked`
after the callback returns.

The invocation record therefore captures the callback's current target, which
may be a Node or the window, and the resulting
`defaultPrevented`, propagation-stopped, and immediate-propagation-stopped
state. The hook does not yet instrument Blink animation triggers, worker
global scopes, inline attributes, or event-handler properties.

### Dispatch start

The integration script patches
`third_party/blink/renderer/core/dom/events/event_dispatcher.cc` inside
`EventDispatcher::Dispatch`. Evidence is emitted after Blink establishes the
event target and before capture-phase listener invocation.

The current hook records:

- A monotonically allocated dispatch identifier.
- The renderer process and browser instance.
- The event name and trusted flag.
- The original target's document and DOM node identifiers.
- The target tag name and HTML `id`, when present.
- The dispatch phase as `none`, because listener-phase processing has not
  started.

The third slice traverses Blink's established `NodeEventContexts` before
capture-phase processing starts. It records those Nodes in Blink path order,
from the original target through its ancestors. Each listener invocation also
records the Node on which Blink invoked that listener as `currentTarget`.

After the Node contexts, the hook appends the window taken from Blink's own
`WindowEventContext`, which is present exactly when Blink will run window
listeners for that event. The recorded path therefore ends where Blink's path
ends. A dispatch whose original target is not a Node does not reach
`EventDispatcher::Dispatch` at all, so events such as `XMLHttpRequest`
progress events remain outside this slice. A separate representation of
shadow-adjusted targets is also still outstanding, so closed shadow-root
behavior remains outside the validated claim.

### Dispatch completion

After Blink calculates its `DispatchEventResult`, the second slice emits
`dispatch-completed` with the same dispatch identifier as `dispatch-started`.
Blink may clear propagation flags while finishing dispatch. The bridge
therefore preserves the cumulative default-prevention and propagation state
observed after each listener and combines it with the state still present when
dispatch returns. The completion record includes that cumulative state and one
of these outcomes:

- `not-canceled`
- `canceled-by-event-handler`
- `canceled-by-default-event-handler`
- `canceled-before-dispatch`

Full retargeted paths, callback timing, and omission handling for bridge
backpressure remain outstanding.

### Default-event-handler decisions

The fourth slice instruments the gate around Blink's Node
`DefaultEventHandler` calls in `EventDispatcher::DispatchEventPostProcess`.
Each `default-action` record uses the active dispatch identifier and records
the Node whose handler is involved. Its `defaultAction` value is
`blink-default-event-handler`, and its outcome is one of:

- `invoked`
- `suppressed-by-event-handler`
- `already-handled`
- `ineligible-untrusted-event`

`invoked` means Blink entered `DefaultEventHandler` for that Node. It does not
by itself assert that the handler changed browser or document state. A
correlated `dispatch-completed` outcome of
`canceled-by-default-event-handler` provides separate evidence that Blink
marked the event handled during default processing.

### DOM timer lifecycle

The fifth slice patches
`third_party/blink/renderer/core/scheduler/dom_timer.cc`. It records a timer
only after Blink assigns a positive timeout identifier and accepts the
schedule. A stable, process-local timer identifier correlates:

- `timer-scheduled`, with the timer kind, accepted requested delay, effective
  delay, and nesting level.
- `timer-fired`, immediately before Blink enters the JavaScript callback.
- `timer-cancelled`, when `clearTimeout` or `clearInterval` removes a live
  timer.

The event timestamp on `timer-fired` is the observed callback-entry time.
Neither this record nor a later cancellation claims that the callback
completed or changed browser or document state. A one-shot timeout is retired
from the bridge when it fires and does not produce a cancellation record.
Context destruction is also not represented as explicit cancellation.

This increment covers window timers only. It does not cover worker timers,
idle callbacks, or browser-process task scheduling.
Throttling remains null, page lifecycle state is `unknown`, and callback
location remains null so the archive does not infer facts that the hook does
not observe.

### Animation-frame callback lifecycle

The sixth slice patches
`third_party/blink/renderer/core/dom/frame_request_callback_collection.cc`.
It records web-exposed callbacks after Blink assigns a positive callback
identifier and accepts the request. A stable, process-local timer identifier
correlates:

- `timer-scheduled`, with timer kind `animation-frame`.
- `timer-fired`, immediately before Blink enters the JavaScript callback.
- `timer-cancelled`, when `cancelAnimationFrame` removes a live callback.

The delay fields are null and nesting level is zero because
`requestAnimationFrame` is not a delay-based DOM timer. Callback entry does not
claim callback completion, frame presentation, or resulting page effects.
Internal Blink callbacks and implicit removal caused by execution-context
destruction are not reported by this slice.

### Idle-callback lifecycle

The seventh slice patches
`third_party/blink/renderer/core/scheduler/scripted_idle_task_controller.cc`.
It records web-exposed callbacks after Blink assigns a positive callback
identifier and posts the idle and timeout tasks. A stable, process-local timer
identifier correlates:

- `timer-scheduled`, with timer kind `idle-callback` and the requested timeout
  when one was supplied.
- `timer-fired`, immediately before Blink enters the JavaScript callback, with
  the observed `IdleDeadline.didTimeout` value.
- `timer-cancelled`, when `cancelIdleCallback` removes a live callback.

Delay fields are null when the timeout option is omitted. Nesting level is zero.
Callback entry does not claim callback completion or resulting page effects.
Implicit removal caused by execution-context destruction is not reported by
this slice.

### Event-target references

Every listener and dispatch record describes its EventTarget with one shape.
The shape reports:

- `kind`, one of `node`, `window`, or `other`.
- `interfaceName`, the token Blink reports for the target's own interface. It
  is recorded as observed and is not a cross-version identity: the reference
  checkout reports `DOMWindow` for a window, while current Chromium returns
  `event_target_names::kWindow` from `DOMWindow::InterfaceName`. Consumers
  must identify a target by its kind, not by this token.
- `targetId`, a process-local identifier for a target that is not a Node, and
  null for a Node.
- `documentId`, the document the target belongs to. For a target that is not a
  Node, this is the document of its local DOM window.
- `nodeId`, present only for a Node and null otherwise.
- `backendNodeId`, `tagName`, `elementId`, and `classes`, as before.

A Node therefore reads exactly as it did in protocol 0.17 apart from the three
new fields. A target identifier is minted from the address Blink uses for the
target in that renderer process. It is stable for the lifetime of that process
and must never be compared across processes. A record is emitted only when the
document is known; a Node additionally requires a known node identifier.

The archive validator enforces this rule rather than inferring it: a `node`
target must carry a node identifier, and a `window` or `other` target must
carry a target identifier and no node identifier.

### Registration form

Every listener record reports `registrationKind`, the form in which the listener
entered Blink's listener map:

- `add-event-listener`, an `addEventListener` call.
- `inline-attribute`, an inline `on*` content attribute in markup.
- `event-handler-property`, an `on*` IDL attribute assignment.
- `native`, reserved for a listener Blink installs itself. The recorder does not
  emit it yet.

Blink routes all three script-reachable forms through
`EventTarget::AddEventListenerInternal`, so the call site cannot distinguish
them. The form is therefore read from the listener object Blink created: a
listener that reports itself as an event handler for a content attribute is an
inline attribute, any other event handler came from an `on*` property
assignment, and a listener that is neither arrived through `addEventListener`.
This is a reading of Blink's own listener classification, not an independent
record of the call that created it.

A form outside this set is normalized to `add-event-listener` rather than
written through, because archive validation rejects an out-of-schema enumeration
value and a rejected payload costs the rest of that renderer's evidence for the
session rather than one record.

### Replaced callbacks

`EventTarget::SetAttributeEventListener` replaces the callback of an existing
attribute registration in place and returns, without adding or removing a
listener. Assigning an `on*` property over a listener that an inline attribute
or an earlier assignment established therefore runs neither the add hook nor
the remove hook, and the archive would keep reporting the form of a callback
Blink no longer holds.

That path emits `listener-callback-replaced` on `browser.listener`, with the
same payload shape as a registration. The record keeps the listener identity,
because Blink keeps the registration, and reports the form of the callback Blink
now holds. A consumer reconstructing a listener's state must apply these records
in order alongside registrations and removals.

What this does not establish: the previous callback is not identified, the
record does not say what the callback was replaced with beyond its form, and a
listener Blink installs itself is still not distinguished from one a script
added.

### Registration location

Each listener hook calls `CaptureSourceLocation(ExecutionContext*)` and writes
the `Url`, `ScriptId`, `LineNumber`, `ColumnNumber`, and `Function` that Blink's
`SourceLocation` reports into the record's `location`. The capture happens where
the record is written, so the location describes the call that registered,
removed, or replaced the listener, not where the callback function was defined.

`SourceLocation` states that a zero line or column means unknown, so a zero
line, column, or script identifier and an empty URL or function name are each
recorded as null, and a location with nothing observed in any field is recorded
as a null location rather than an object of nulls. `sourceHash` is always null,
because the recorder does not read script text.

What this does not establish: the definition site of a callback is not recorded,
a location is only as good as the top frame Blink reports and falls back to a
parsing position for a registration made while no script was running, no script
text or hash is recorded, and an eval or inline script reports whatever URL Blink attributes to
it rather than a file on disk.

### Execution-world identity

Each listener hook reads the world of the callback through
`JSBasedEventListener::GetWorldForInspector()`, which returns the
`DOMWrapperWorld` the callback was created in. The record's `world` reports the
world kind, the numeric identifier `DOMWrapperWorld::GetWorldId()` returns, and
the values `NonMainWorldHumanReadableName()` and `NonMainWorldStableId()` return.
Because the world is read from the callback rather than from the world current at
the hook, it is the world the registration was made from and not the world that
happened to be running when the record was written. The record's context repeats
the same world as `executionWorldId` in the form `world-<blinkWorldId>`, so
records from one world can be grouped without reading the payload, and archive
validation rejects a record whose two readings disagree.

Both name accessors assert that the world is not the main world, so they are
called only for a world other than the main world and a main-world registration
reports both as null. Blink classifies isolated worlds and the inspector's
isolated worlds alike as isolated, so the inspector's worlds are tested first and
reported as `inspector-isolated`. An `EventListener` that is not a
`JSBasedEventListener`, which includes a listener Blink installed itself, belongs
to no world: that record reports a null world and a null `executionWorldId`
rather than claiming the main world. A world type the recorder does not name is
reported as `other`, because an out-of-schema value would fail archive validation
for the whole session rather than for one record.

What this does not establish: the origin, the content security policy, and the
extension or client that owns an isolated world are not recorded, only the
identity Blink holds for it. A stable identifier is stable within a Blink
installation rather than across builds. Nothing here records which world a
dispatch or an invocation ran in, only the world each listener registration,
removal, and callback replacement was made from.

A page's own script always runs in the main world, so a fixture cannot register a
listener from another world by itself. The validation script therefore creates a
world through the DevTools `Page.createIsolatedWorld` command on the fixture's
main frame and evaluates a registration in it on `#isolated-world-target`, an
element the document's own script never touches. Blink creates a DevTools world
as an inspector isolated world, so that registration is the recorded
`inspector-isolated` case, and the world's human readable name is the name the
command asked for. The script also reads the marker the isolated script set back
from the main world and requires it to be undefined, so a registration is only
accepted as isolated when the two worlds were actually separate.

What that validation does not establish: an isolated world an embedder or an
extension creates, which Blink classifies as `isolated` rather than
`inspector-isolated`, is still not exercised, and neither is a worker or worklet
world or a shadow realm. A DevTools world has no stable identifier, so the
recorded `stableId` is null in this run and the field is unexercised.

## Component boundary

The recorder bridge is a Chromium component with exported entry points. This
ensures renderer startup and Blink core use one process-local bridge client in
component builds. The bridge does not depend on `//content/public/common`;
Chromium process switch values are isolated as compatibility constants so
Blink core does not acquire an upward content-layer dependency.

Pipe writes are serialized per process. This prevents frames from different
renderer threads from interleaving. The current hook writes synchronously and
is therefore a correctness prototype. A bounded producer queue, batching, and
explicit omission records are required before expanding instrumentation to
high-volume event classes.

## Deterministic fixture

`tests/fixtures/blink-listener-dispatch.html` installs capture and bubble
listeners on `#propagation-root` and two listeners on `#pointer-only`. It
installs a `click` listener on the window, and registers and removes a window
`resize` listener. `#inline-handler` carries an inline `onclick` content
attribute, `#property-handler` receives an `onclick` property assignment that is
then reassigned, and `#inline-handler` receives an `onclick` assignment over its
attribute registration, so the three registration forms and both in-place
callback replacements are exercised. Each of those handlers calls
`stopPropagation()`, so the recorded window click invocations stay
deterministic. `tests/fixtures/blink-listener-registration.js` registers a
`click` listener on `#external-script-handler` from inside
`registerExternalScriptListener`, so a recorded registration location names a
script the document does not share and an enclosing function the fixture fixes. It
installs a 125-millisecond interval that clears itself after one callback, then
requests two animation-frame callbacks, explicitly cancels one, and invokes
two idle callbacks, explicitly cancels the 5,000-millisecond callback, and
keeps the main thread busy long enough for the 1-millisecond callback to enter
with `didTimeout` true. It then invokes
`HTMLElement.click()` from a 250-millisecond timeout. The target
listeners call `preventDefault()`, remove the named listener, and call
`stopPropagation()`. The expanded fixture must produce:

- At least one `browser.listener` `listener-registered` record for the
  `pointer-only` element and `click` event.
- A `listener-registered` record with `registrationKind` `inline-attribute` for
  `#inline-handler` and `event-handler-property` for `#property-handler`.
- A `listener-callback-replaced` record for each of those two elements, carrying
  the listener identity of the registration it replaced and the
  `event-handler-property` form of the replacing callback.
- At least one `browser.dispatch` `dispatch-started` record for the same
  document and node.
- One correlated `listener-removed` record.
- One correlated `listener-invoked` record with phase `at-target` and
  `defaultPrevented` true.
- One correlated `dispatch-completed` record with outcome
  `canceled-by-event-handler`.
- A composed Node path beginning with `#pointer-only` and containing
  `#propagation-root`.
- One `listener-registered` record whose target kind is `window`, carrying a
  non-empty interface name, a target identifier, and no node identifier.
- One correlated window `resize` registration and removal that report the same
  listener identifier and the same target identifier as each other, and the
  same target identifier as the window `click` registration.
- A `dispatch-started` record for `#default-action-link` whose composed path
  ends at the window exactly once. The `#pointer-only` click stops propagation
  at its target, so the link click is the only click that reaches the window.
- One `listener-invoked` record whose current target is the window, whose
  phase is `bubbling`, and whose original target is a Node.
- The same interface name and target identifier on the window listener target
  and the window entry in the composed path.
- A capturing invocation whose current target is `#propagation-root`.
- At-target invocations whose current target is `#pointer-only`.
- No bubbling invocation for `#propagation-root` after propagation is stopped.
- A `default-action` record showing that `preventDefault()` suppressed the
  default handler for `#pointer-only`.
- A `default-action` record showing that Blink invoked the default handler for
  `#default-action-link`, followed by a correlated completion with outcome
  `canceled-by-default-event-handler`.
- One scheduled 250-millisecond timeout and exactly one correlated callback
  entry for its stable timer identifier.
- One scheduled 125-millisecond interval, exactly one correlated callback
  entry, and one later `explicit-clear` cancellation for its stable timer
  identifier.
- No cancellation record for the one-shot timeout.
- Two `animation-frame` schedules with null delay values and zero nesting.
- Exactly one correlated animation-frame callback entry.
- Exactly one distinct correlated cancellation with reason
  `explicit-cancel-animation-frame`.
- Two `idle-callback` schedules with timeout values 1 and 5,000 milliseconds.
- Exactly one correlated idle-callback entry whose `didTimeout` value is true.
- Exactly one distinct correlated cancellation with reason
  `explicit-cancel-idle-callback`.
- Renderer process context and stable non-empty listener and dispatch
  identifiers across the lifecycle, plus stable non-empty timer identifiers
  across each timer lifecycle.

Because `HTMLElement.click()` dispatches a synthetic event, the expected
`trusted` value is false.

### Page-lifecycle phase

The fixture does not schedule its page-lifecycle timers when it loads. A page's
visibility during load depends on when Chromium shows its window, so a
parse-time schedule recorded whichever visibility state the desktop happened to
be in and made the lifecycle assertions depend on timing luck. Instead the
fixture exposes `window.recorderScheduleLifecycleEvidence()`, which schedules
the 3,000-millisecond lifecycle timeout, the 5,000-millisecond lifecycle
interval, and the 3,500-millisecond timeout that clears that interval, and
returns the visibility state it observed.

`Run-BlinkValidation.ps1` drives that phase in a fixed order: it activates the
fixture target, calls `Page.bringToFront`, waits for the page's own
`document.visibilityState` to report `visible`, calls the scheduling function
and requires the returned state to be `visible`, and then opens the background
`about:blank` target that hides the fixture. The schedule is therefore recorded
while the page reports visible and the callbacks enter while it is hidden, by
construction rather than by timing. If the page does not report visible within
the bounded wait, the run fails immediately with a message naming a minimized,
occluded, or inactive-desktop window as the cause, instead of failing later in
the verifier. The capture duration default is 25 seconds so that a slow launch
still leaves room for the 3.5-second lifecycle phase.

## Windows validation procedure

Run the complete validation from a standard, non-elevated PowerShell window:

```powershell
.\scripts\Run-BlinkValidation.ps1 `
  -ChromiumSource C:\Users\User\chromium-dev\chromium\src
```

The script tests and applies the integration, normalizes copied-file
timestamps, builds Chromium, runs the managed test suite, captures the
deterministic fixture, creates an isolated world in the fixture frame over the
DevTools endpoint and registers a listener in it, schedules the fixture's
page-lifecycle timers while the page reports visible and then hides it,
requires a valid archive, and runs
`Verify-BlinkEvidence.ps1`. Its final output includes the session path and
archive-validation counts. Preserve that output in the dated validation
record.

## Current validation state

Validation completed successfully on the reference Windows machine on
September 19, 2026, using repository commit `08b75fb`. The complete script
exited with code 0 after:

- Passing the Chromium integration tests.
- Building the instrumented Chromium executable.
- Passing the managed recorder test suite.
- Launching Chromium from a standard, non-elevated PowerShell session.
- Connecting instrumented renderer processes through the authenticated pipe.
- Recording the fixture's `click` listener registration for `#pointer-only`.
- Recording the fixture's programmatic `click` dispatch start for the same
  document and node.
- Validating the completed archive.
- Confirming that Chromium reported no network-service crashes.

The validated session is:

`C:\Users\Public\Documents\A11yRecorderBlinkValidation\20260919-143342-bd566cd94efe4ba79d20e0c67b90e5dc`

The final validation summary reported:

- `ARCHIVE_VALID=True`
- `EVENTS_VALIDATED=371`
- `ARTIFACTS_VALIDATED=81`
- `NETWORK_SERVICE_CRASHES=0`
- `VALIDATION_EXIT_CODE=0`

Earlier fixture attempts exposed two independent integration defects. First,
the managed named-pipe descriptor did not allow Chromium's restricted,
untrusted renderer token to connect. The final descriptor grants the current
logon SID and Chromium lockdown restricting SID access, applies an untrusted
mandatory label, and does not grant Everyone access. Second, distributing the
bootstrap capability to GPU and utility processes destabilized Chromium's
network-service utility process. Commit `08b75fb` restricts distribution and
accepted child connections to renderers, which are the only children with
implemented evidence hooks.

This result validates the initial Blink listener-registration and
dispatch-start slice.

## Correlated lifecycle validation result

The correlated listener-removal, listener-invocation, and dispatch-completion
slice completed the reference Windows procedure on September 19, 2026, using
repository commit `1d8d8ae`. The complete script exited with code 0 after:

- Passing the Chromium integration tests.
- Building the instrumented Chromium executable.
- Passing the managed recorder test suite.
- Connecting the instrumented renderer through the authenticated pipe.
- Correlating the fixture's listener registration, removal, and invocation by
  one stable listener identifier.
- Correlating dispatch start, listener invocation, and dispatch completion by
  one stable dispatch identifier.
- Verifying the `at-target` invocation phase, the callback's
  `preventDefault()` result, and the `canceled-by-event-handler` outcome.
- Validating the completed archive.
- Confirming that Chromium reported no network-service crashes.

The validated session is:

`C:\Users\Public\Documents\A11yRecorderBlinkValidation\20260919-151419-5b0096dbccf84166812e7635f9043246`

The final validation summary reported:

- `ARCHIVE_VALID=True`
- `EVENTS_VALIDATED=478`
- `ARTIFACTS_VALIDATED=81`
- `NETWORK_SERVICE_CRASHES=0`
- `VALIDATION_EXIT_CODE=0`

Two failed candidate builds exposed installer compatibility defects before the
successful run. The first retained the eight-argument listener-registration
call from the previously integrated slice while replacing the bridge header
with its new nine-argument declaration. The installer now migrates existing
listener-registration and dispatch-start hooks. The second used `.Get()` on
Blink's raw `Event*`; the final hook uses the pointer directly and migrates the
invalid installed form. Integration tests cover both upgrade paths.

This result validates listener removal, listener invocation, and dispatch
completion for the deterministic Node fixture. At this point, complete composed
paths, default-action detail, timers, cookies, DOM or accessibility snapshots,
network evidence, compositor evidence, and rendering evidence remained
unvalidated.

## Propagation-path validation result

The ordered Node propagation-path slice completed the reference Windows
procedure on September 19, 2026, using repository commit `8e3bacc`. The
complete script exited with code 0 after:

- Passing the Chromium integration tests.
- Building the instrumented Chromium executable.
- Passing the managed recorder test suite.
- Connecting the instrumented renderer through the authenticated pipe.
- Recording a five-Node composed path beginning with `#pointer-only` and
  containing `#propagation-root`.
- Recording one capturing invocation on `#propagation-root`.
- Recording three relevant invocations in total, including two at-target
  invocations on `#pointer-only`.
- Confirming that `stopPropagation()` prevented the ancestor bubble listener
  from running.
- Preserving cumulative propagation-stop state in the correlated
  `dispatch-completed` record.
- Validating the completed archive.
- Confirming that Chromium reported no network-service crashes.

The validated session is:

`C:\Users\Public\Documents\A11yRecorderBlinkValidation\20260919-173036-d2ebf59aabe7413d93a32cbb72b7be96`

The final validation summary reported:

- `ListenerRecords=2`
- `DispatchRecords=1`
- `InvocationRecords=3`
- `CompletionRecords=1`
- `RemovalRecords=1`
- `ComposedPathNodes=5`
- `RootCaptureInvocations=1`
- `RootBubbleInvocations=0`
- `DispatchOutcome=canceled-by-event-handler`
- `ARCHIVE_VALID=True`
- `EVENTS_VALIDATED=476`
- `ARTIFACTS_VALIDATED=81`
- `NETWORK_SERVICE_CRASHES=0`
- `VALIDATION_EXIT_CODE=0`

The capture host accepted 476 records and dropped none. The archive validator,
version 1.2, reported no issues.

The first propagation candidate correctly recorded
`propagationStopped=true` after the second target listener and correctly
omitted the ancestor bubble invocation. Blink cleared the propagation flag
before the dispatch-completion hook ran, however, so the initial completion
record incorrectly contained `propagationStopped=false`. Commit `8e3bacc`
preserves the cumulative state observed after listener callbacks and combines
it with any state still present when dispatch returns.

This result validates ordered Node paths, Node current targets, capture and
at-target phases, stopped ancestor bubbling, and cumulative propagation state
for the deterministic light-DOM fixture. At this point, Window and other
non-Node targets, shadow-adjusted targets, closed shadow roots, default-action
detail, timers, cookies, DOM or accessibility snapshots, network evidence,
compositor evidence, and rendering evidence remained outside the validated
scope.

## Default-action validation result

The Node default-event-handler slice completed the reference Windows procedure
on September 19, 2026, using repository commit `b808dcd`. The complete script
exited with code 0 after:

- Passing the Chromium integration tests.
- Building the protocol 0.4 instrumented Chromium executable.
- Passing the managed recorder test suite.
- Connecting the instrumented renderer through the authenticated pipe.
- Recording one `suppressed-by-event-handler` default-action decision for
  `#pointer-only` after its click listener called `preventDefault()`.
- Recording one `invoked` default-action decision for
  `#default-action-link`.
- Correlating the invoked decision with a `dispatch-completed` outcome of
  `canceled-by-default-event-handler`.
- Validating the completed archive.
- Confirming that Chromium reported no network-service crashes.

The validated session is:

`C:\Users\Public\Documents\A11yRecorderBlinkValidation\20260919-175746-0c49590d41c0426aaf239e0a0a0c20f9`

The final validation summary reported:

- `ListenerRecords=2`
- `DispatchRecords=1`
- `InvocationRecords=3`
- `CompletionRecords=1`
- `RemovalRecords=1`
- `SuppressedDefaultActions=1`
- `InvokedDefaultActions=1`
- `HandledDefaultActionCompletions=1`
- `ComposedPathNodes=5`
- `RootCaptureInvocations=1`
- `RootBubbleInvocations=0`
- `DispatchOutcome=canceled-by-event-handler`
- `ARCHIVE_VALID=True`
- `EVENTS_VALIDATED=597`
- `ARTIFACTS_VALIDATED=81`
- `NETWORK_SERVICE_CRASHES=0`
- `VALIDATION_EXIT_CODE=0`

The capture host accepted 597 records and dropped none. The archive validator
reported no issues.

This result validates correlated Blink Node default-event-handler decisions for
the deterministic light-DOM fixture. An `invoked` record proves that Blink
entered the identified Node's `DefaultEventHandler`; it does not by itself
prove a visible browser or document-state change. Window and other non-Node
targets, shadow-adjusted targets, closed shadow roots, timers, cookies, DOM or
accessibility snapshots, network evidence, compositor evidence, and rendering
evidence remain outside the validated scope.

## DOM timer validation result

The window timeout and interval slice completed the reference Windows
procedure on September 19, 2026, using repository commit `d68be02`. The
complete script exited with code 0 after:

- Passing the Chromium integration tests.
- Building the protocol 0.5 instrumented Chromium executable.
- Passing the managed recorder test suite.
- Connecting the instrumented renderer through the authenticated pipe.
- Correlating one 250-millisecond timeout schedule with one callback entry.
- Correlating one 125-millisecond interval schedule with one callback entry
  and one later explicit cancellation.
- Validating the completed archive.
- Confirming that Chromium reported no network-service crashes.

The validated session is:

`C:\\Users\\Public\\Documents\\A11yRecorderBlinkValidation\\20260919-182516-118b0821a08e4cda839c646d10f74202`

The final validation summary reported:

- `ScheduledTimeouts=1`
- `FiredTimeouts=1`
- `ScheduledIntervals=1`
- `FiredIntervals=1`
- `CancelledIntervals=1`
- `TimeoutTimerId=timer-2`
- `IntervalTimerId=timer-1`
- `ARCHIVE_VALID=True`
- `EVENTS_VALIDATED=632`
- `ARTIFACTS_VALIDATED=81`
- `NETWORK_SERVICE_CRASHES=0`
- `VALIDATION_EXIT_CODE=0`

The capture host accepted 632 records and dropped none. Archive validator
version 1.2 reported no issues. Timer correlation includes browser instance,
renderer process, timer kind, and timer ID because timer identifiers are
process-local.

This result validates accepted scheduling, callback entry, and explicit
interval cancellation for the deterministic window-timer fixture. Callback
completion, callback effects, worker timers, animation frames, idle callbacks,
throttling, page lifecycle state, callback source location, and
context-destruction cancellation remain outside the validated scope. The
[dated validation record](blink-dom-timers-2026-09-19.md) documents the
environment, defects found, evidence, and limits.

## Animation-frame validation result

The web-exposed animation-frame slice completed the reference Windows
procedure on September 19, 2026, using repository commit `a66142e`. The
complete script exited with code 0 after:

- Passing five Chromium integration tests.
- Building the protocol 0.6 instrumented Chromium executable.
- Passing the managed recorder test suite.
- Connecting the instrumented renderer through the authenticated pipe.
- Correlating two accepted callback schedules with one callback entry and one
  explicit cancellation.
- Validating the completed archive.
- Confirming that Chromium reported no network-service crashes.

The validated session is:

`C:\\Users\\Public\\Documents\\A11yRecorderBlinkValidation\\20260919-184823-de48e2b9fa7c474da0a7e4945824538d`

The final validation summary reported:

- `ScheduledAnimationFrames=2`
- `FiredAnimationFrames=1`
- `CancelledAnimationFrames=1`
- `FiredAnimationFrameId=timer-2`
- `CancelledAnimationFrameId=timer-1`
- `RendererProcessId=20040`
- `DocumentId=dom-document-3`
- `ARCHIVE_VALID=True`
- `EVENTS_VALIDATED=1594`
- `ARTIFACTS_VALIDATED=81`
- `NETWORK_SERVICE_CRASHES=0`
- `VALIDATION_EXIT_CODE=0`

The fired schedule and callback-entry records share one process-local timer
identifier. The cancelled schedule and explicit-cancellation records share a
second identifier. Correlation also requires the browser instance, renderer
process, and timer kind.

This result validates accepted scheduling, callback entry, and explicit
cancellation for the deterministic web-exposed animation-frame fixture. A
`timer-fired` record proves callback entry, not callback completion, frame
presentation, or resulting browser or document-state changes. Internal Blink
callbacks, execution-context destruction, idle callbacks, worker scheduling,
throttling, page lifecycle state, callback source location, sustained
high-volume operation, and omission handling under backpressure remain outside
the validated scope. The
[dated validation record](blink-animation-frames-2026-09-19.md) documents the
environment, defects found, evidence, and limits.

## Idle-callback validation result

The web-exposed idle-callback slice completed the reference Windows procedure
on September 19, 2026, using repository commit `86a26f8`. The complete script
exited with code 0 after:

- Passing six Chromium integration tests.
- Building the protocol 0.7 instrumented Chromium executable.
- Passing the managed recorder test suite.
- Connecting the instrumented renderer through the authenticated pipe.
- Correlating two accepted callback schedules with one timed-out callback
  entry and one explicit cancellation.
- Validating the completed archive.
- Confirming that Chromium reported no network-service crashes.

The validated session is:

`C:\\Users\\Public\\Documents\\A11yRecorderBlinkValidation\\20260919-193958-7d487516d0e24b03b39c08895f682175`

The final validation summary reported:

- `ScheduledIdleCallbacks=2`
- `FiredIdleCallbacks=1`
- `CancelledIdleCallbacks=1`
- `FiredIdleCallbackDidTimeout=True`
- `FiredIdleCallbackId=timer-4`
- `CancelledIdleCallbackId=timer-3`
- `RendererProcessId=21476`
- `DocumentId=dom-document-3`
- `ARCHIVE_VALID=True`
- `EVENTS_VALIDATED=629`
- `ARTIFACTS_VALIDATED=81`
- `NETWORK_SERVICE_CRASHES=0`
- `VALIDATION_EXIT_CODE=0`

The fired schedule and callback-entry records share one process-local timer
identifier. The cancelled schedule and explicit-cancellation records share a
second identifier. Correlation also requires the browser instance, renderer
process, document, and timer kind.

This result validates accepted scheduling, timed-out callback entry, and
explicit cancellation for the deterministic web-exposed idle-callback
fixture. A `timer-fired` record proves callback entry and records Blink's
`didTimeout` value. It does not prove callback completion or resulting browser
or document-state changes. Idle callbacks without timeout options,
execution-context destruction, worker scheduling, throttling, page lifecycle
state, callback source location, sustained high-volume operation, and omission
handling under backpressure remain outside the validated scope. The
[dated validation record](blink-idle-callbacks-2026-09-19.md) documents the
environment, evidence, and limits.

## Non-Node event-target validation result

The non-Node event-target slice completed the reference Windows procedure on
September 21, 2026, using repository commit `8fae574`. The complete script
exited with code 0 after:

- Passing 41 Chromium integration tests.
- Building the protocol 0.18 instrumented Chromium executable.
- Passing the managed recorder test suite, 95 tests with no failures.
- Connecting the instrumented renderer through the authenticated pipe.
- Recording listener registration, removal, and invocation for the fixture
  document's window, and a composed path that ends at that window.
- Validating the completed archive.
- Confirming that Chromium reported no network-service crashes.

The validated session is:

`C:\\Users\\Public\\Downloads\\A11yRecorderWindowEvidence\\sessions\\20260921-195100-55f92d53399840e483117e40ec97b9d2`

The final validation summary reported:

- `WindowInterfaceName=DOMWindow`
- `WindowTargetId=event-target-3`
- `WindowListenerId=listener-6`
- `WindowClickInvocations=1`
- `LinkComposedPathEntries=5`
- `RendererProcessId=15816`
- `DocumentId=dom-document-9`
- `ARCHIVE_VALID=True`
- `EVENTS_VALIDATED=42176`
- `ARTIFACTS_VALIDATED=111`
- `NETWORK_SERVICE_CRASHES=0`
- `VALIDATION_EXIT_CODE=0`

The window registration, its correlated removal, and the window entry in the
`#default-action-link` composed path share one process-local target identifier
and carry no node identifier. The window listener invocation reports the
`bubbling` phase and a Node original target.

This result validates that a listener registered on an EventTarget that is not
a Node is recorded, that its removal correlates with its registration, that it
is invoked within a recorded dispatch, and that a composed path ends where
Blink's own path ends. The interface name is recorded as observed and is not a
cross-version identity: this checkout reports `DOMWindow`, while current
Chromium returns `event_target_names::kWindow` from
`DOMWindow::InterfaceName`. Worker and worklet global scopes, inline event
attributes, `on*` handler properties, isolated-world identity, listener source
location, shadow-adjusted targets, and dispatches whose original target is
never a Node remain outside the validated scope.

Two earlier runs of this slice failed, and both failures were in the harness
rather than the recorded evidence. The first recorded every intended record and
failed a fixture assertion that required the window's interface name to equal
`Window`. The second failed archive validation with `event-time-regressed`
after the UI Automation observation queue overflowed and dropped 1,062
observations: the closing omission record was stamped with the stop boundary
captured before the collector's queued evidence had drained, 0.74 ms before the
last drained observation it was written after, which made the session status
`failed` and the capture host exit with code 3 after a complete capture of
42,949 records.

## Registration-form validation result

Reference platform, September 21, 2026, at repository revision `e107a34`.
Protocol 0.19. Recorded on the same Windows 10.0.19045 host and instrumented
Chromium build as the earlier results in this document, with a 20-second
capture.

The run passed: 45 integration tests, 99 managed tests, a valid session archive,
42,233 events and 110 artifacts validated, no network-service crashes, and exit
code 0.

Observed for this slice:

| Field | Value |
| --- | --- |
| `InlineAttributeListenerId` | `listener-1` |
| `InlineAttributeRegistrationKind` | `inline-attribute` |
| `EventHandlerPropertyListenerId` | `listener-9` |
| `EventHandlerPropertyRegistrationKind` | `event-handler-property` |
| `InlineAttributeReplacements` | 1 |
| `PropertyListenerReplacements` | 1 |
| `ReplacedCallbackRegistrationKind` | `event-handler-property` |
| `WindowClickInvocations` | 1 |

What this establishes: a listener created by an inline `on*` content attribute
is recorded as `inline-attribute`, one created by an `on*` property assignment
as `event-handler-property`, and the `addEventListener` registration on
`#pointer-only` still as `add-event-listener`. Each replacement record carries
the listener identity of the registration whose callback Blink swapped, one for
the registration an inline attribute established and one for a registration an
earlier assignment established, and reports the form of the callback Blink then
held. The window click invocation count is unchanged from the previous run,
because each new fixture handler stops propagation at its target, so the added
fixture work did not disturb the non-Node assertions.

What this does not establish: the recorded form is a reading of Blink's own
listener classification rather than an independent record of the call that
created the listener, the previous callback in a replacement is not identified,
a listener Blink installs itself is still not distinguished from one a script
added, and `native` is defined but never emitted. Listener source location,
isolated-world identity, and worker global scopes remain outstanding, and the
455 recorded transitions with 200 uncovered are the same population described
under protocol 0.16 rather than a result of this slice.

## Registration-location validation result

Reference platform, September 21, 2026, at repository revision `7355f71`.
Protocol 0.20. Recorded on the same Windows 10.0.19045 host and instrumented
Chromium build as the earlier results in this document, with a 20-second
capture.

The run passed on the first attempt, with no harness failures: 47 integration
tests, 102 managed tests, a valid session archive, 40,663 events and 110
artifacts validated, no network-service crashes, exit code 0, and 40,663 accepted
records with none dropped.

Observed for this slice:

| Field | Value |
| --- | --- |
| `ExternalScriptListenerId` | `listener-3` |
| `ExternalScriptLocationUrl` | the `blink-listener-registration.js` file URL |
| `ExternalScriptLocationFunction` | `registerExternalScriptListener` |
| `ExternalScriptLocationLine` | 9 |
| `ExternalScriptLocationColumn` | 18 |
| `ExternalScriptLocationScriptId` | `3` |
| `RegistrationLocationUrl` | the `blink-listener-dispatch.html` file URL |
| `RegistrationLocationLine` | 88 |
| `RegistrationLocationColumn` | 12 |
| `RegistrationLocationFunction` | null |
| `RemovalLocationFunction` | `handleClick` |
| `RemovalLocationLine` | 85 |
| `ReplacementLocationFunction` | null |
| `ReplacementLocationLine` | 141 |
| `InlineAttributeLocationUrl` | the `blink-listener-dispatch.html` file URL |
| `InlineAttributeLocationLine` | 23 |

What this establishes: a registration made from a separate script file reports
that file rather than the document that loaded it, and reports the enclosing
function the call was made from. Each record reports its own call: the
registration on `#pointer-only` reports line 88, which is the
`addEventListener` call in the fixture document, and the removal of the same
listener reports line 85 inside `handleClick`, which is the
`removeEventListener` call. Blink's line and column numbering is 1-based in
these records: `blink-listener-registration.js` line 9 column 18 is the first
character of `addEventListener` in the call the fixture makes there, and
`blink-listener-dispatch.html` line 88 column 12 is the first character of
`addEventListener` in the call the document script makes. A registration made
from a top-level script reports a null function name rather than an invented one.
A registration Blink creates while parsing an inline attribute still reports a
location: the inline attribute on `#inline-handler` reported the document URL and
line 23, which is the line the element's start tag closes on, one line after the
`onclick` attribute itself, so the recorded line is the parser position when
Blink created the listener and not the position of the attribute text.

What this does not establish: the definition site of a callback is still not
recorded, only the call that changed the listener. A location is only as good as
the top frame Blink reports, so a registration made through a wrapper reports the
wrapper. No script text or hash is recorded, and `sourceHash` is null in every
record. The verifier requires the line and column to be present but does not
assert their exact values, so the 1-based numbering above is an observation from
this run rather than an assertion the suite enforces. A registration with no
observable location was not exercised, because every registration in the fixture
had either a script stack or a parser position. The 448 recorded transitions with
200 uncovered are the same population described under protocol 0.16 and are not a
result of this slice.

## JavaScript world validation result

Reference platform, September 22, 2026, at repository revision `d1e713f`.
Protocol 0.21. Recorded on the same Windows 10.0.19045 host and instrumented
Chromium build as the earlier results in this document, with a 20-second
capture.

The run passed after three harness failures that were all in the validation
scripts and fixtures rather than in the recorder: four managed archive fixtures
predated the required `world` property, the verifier read a variable that is
bound further down the script, and the verifier read a context property on
channels that carry no context, which strict mode rejects. The passing run
reported 50 integration tests, 109 managed tests, a valid session archive,
30,051 events and 92 artifacts validated, no network-service crashes, and exit
code 0.

Observed for this slice:

| Field | Value |
| --- | --- |
| `ListenerChannelRecords` | 203 |
| `ListenerWorldRecords` | 179 |
| `IsolatedWorldListenerId` | `listener-22` |
| `IsolatedWorldKind` | `inspector-isolated` |
| `IsolatedWorldBlinkId` | 536870914 |
| `IsolatedWorldName` | `A11yRecorderValidationWorld` |
| `IsolatedWorldStableId` | null |
| `IsolatedWorldExecutionWorldId` | `world-536870914` |
| `RegistrationWorldKind` | `main` |
| `RegistrationBlinkWorldId` | 0 |
| `RegistrationExecutionWorldId` | `world-0` |

What this establishes: a registration made outside the main world is recorded
with a world identity that distinguishes it from the page's own registrations.
The world created through `Page.createIsolatedWorld` is recorded as
`inspector-isolated` rather than `isolated`, which matches Blink creating it
through `EnsureInspectorIsolatedWorldWithName`, and it carries the requested
human readable name through to the record. Its numeric identifier is far above
the main world's 0 and above the range Blink uses for embedder isolated worlds,
so a world identifier must be treated as an opaque number for grouping rather
than a small index. The identifier appears in `executionWorldId` in the
`world-<blinkWorldId>` form, so listener records can be grouped by world without
reading the payload. Every registration, removal, and callback replacement made
by the page's own script reported the main world with identifier 0 and null name
and stable identifier, and no record outside the listener channel reported a
world identity. The isolated world's listener was recorded against the same
document as the page's registrations, so a world identity does not fragment
document correlation. The main world could not read the marker the isolated
world set, which confirms the registration was made from a genuinely separate
world rather than from the main world under a different name.

What this does not establish: 24 of the 203 listener channel records reported no
world, which is the expected reporting for records that do not observe a
callback world, but which world each of those records would have belonged to is
not evidence this run produces. A stable identifier was null in every record, so
the stable identifier path is still unexercised. Embedder and extension
`isolated` worlds, worker and worklet worlds, and shadow realms remain
unexercised, as does a world Blink classifies as a type the recorder does not
name. Nothing here records the world a dispatch or an invocation ran in. The
world name is read from the inspector's own naming of the world, so a world
created without a name would report a null name and is not covered.

## Transition coverage accounting

Earlier results in this document repeated the uncovered transition count as a
caveat, noting that it was the same population first described under protocol
0.16 and not a result of the slice being reported. The verifier now asserts the
structure of that population instead, so a future result states a bound rather
than restating a caveat.

Every recorded transition is accounted for as one of three cases. A transition
inside a range one of its document's completed delivery passes claimed is
covered. A transition in a document that completed no pass covering transitions
is uncovered because no pass ever ran there. A transition after the last
transition its document's passes claimed is uncovered because no later pass ran
before the capture ended. The two uncovered classes are required to sum to the
reported total, so a transition cannot be dropped from the accounting.

Two cases fail a run. A transition that lies inside the span its own document
already claimed is a hole in the coverage rather than a fact about the page, and
two passes in one document that claim the same transition are an overlap. Both
describe coverage the passes report incorrectly, which is the condition a bare
count could not separate from ordinary uncovered evidence.

A run now reports `CoveredTransitions`, `UncoveredTransitions`,
`UncoveredTransitionsWithoutPass`, `UncoveredTransitionsAfterLastPass`, and
`UncoveredTransitionDocuments`.

### Measured result

Reference platform, September 23, 2026, at repository revision `326d755`.
Protocol 0.21. Recorded on the same Windows 10.0.19045 host and instrumented
Chromium build as the earlier results in this document, with a 15-second capture
that validated 31,542 events and 85 artifacts and reported no network-service
crashes.

| Value | Result |
| --- | --- |
| `RecordedTransitions` | 448 |
| `CoveredTransitions` | 248 |
| `UncoveredTransitions` | 200 |
| `UncoveredTransitionsWithoutPass` | 200 |
| `UncoveredTransitionsAfterLastPass` | 0 |
| `UncoveredTransitionDocuments` | 3 |

The latest measurement of these values is recorded under the page-lifecycle
determinism result at the end of this document.

What this establishes: the 200 uncovered transitions carried unchanged since
protocol 0.16 are now a measured population rather than a described one. Every
one of them is in a document that completed no delivery pass, and they come from
three documents, which is the explanation the evidence model document gives for
this population. No transition was uncovered after its document's last pass in
this run, no document reported a hole inside a span its own passes claimed, and
no two passes claimed the same transition.

What this does not establish: the counts themselves are not asserted, because
how many transitions a capture produces and how many documents Blink creates and
discards are properties of the run rather than contracts. The classification
states where a transition sits relative to its document's passes, and it does
not claim why a document produced no pass, since the verifier does not read
navigation records for the documents involved. The hole and overlap conditions
have not been observed in any run, so they are assertions that no measurement has
yet exercised.

## Evidence loss accounting

Protocol 0.22 makes a lost browser record visible in the archive. Before it, a
renderer whose pipe write failed wrote a line to the bridge log and nothing to
the archive, and a record the recorder's bounded event sink refused appeared
only as degraded collector health and a count in the session manifest. In both
cases a reader of the archive saw a gap that looked exactly like an event that
never happened. Each reporter now holds the number of lost records per channel
and states it on that channel with a `collector-omission` record, carrying the
reason, the count, and the process context when the reporter knows which process
lost them.

A run reports `OMITTED_EVIDENCE_RECORDS` from the omission records, and
`SINK_REFUSED_EVENTS` from the manifest count of records the event sink refused.
A lost record in either value fails the run, because a reference run must be
lossless. `OTHER_OMISSION_RECORDS` counts omissions that do not report a lost
record, such as a rejected connection, and is reported without failing the run.
The verifier reports `EvidenceOmissionRecords`, `OmittedEvidenceRecords`, and
`EvidenceOmissionReasons` and does not fail, because a stated omission is a true
account of what happened and whether such a run can serve as a reference is a
decision for the run.

### Measured result

Reference platform, September 23, 2026, at repository revision `17d442a`.
Protocol 0.22. Recorded on the same Windows 10.0.19045 host and instrumented
Chromium build as the earlier results in this document, with a 15-second capture
that validated 37,656 events and 91 artifacts and reported no network-service
crashes.

| Value | Result |
| --- | --- |
| `OMITTED_EVIDENCE_RECORDS` | 0 |
| `SINK_REFUSED_EVENTS` | 0 |
| `OTHER_OMISSION_RECORDS` | 0 |
| `EvidenceOmissionRecords` | 0 |
| `OmittedEvidenceRecords` | 0 |
| `EvidenceOmissionReasons` | none |

The latest measurement of the three run-level loss values is recorded under the
page-lifecycle determinism result at the end of this document.

The recorder reported 37,656 records accepted and none dropped, which agrees
with the manifest count the run reads, and every existing measurement in this
document held, including 448 recorded transitions with 248 covered and 200
uncovered across three documents.

What this establishes: the protocol bump is live and lossless. The bridge and
the recorder agreed on 0.22 at connect, the new browser omission contract did
not reject any record on ingest, and no renderer lost the rest of its evidence,
which is the failure mode a new record type risks. The run reports evidence loss
as three explicit zeros rather than by the absence of a symptom.

What this does not establish: no run has lost a record, so neither reporting
path has been exercised by a real loss. The run-script accounting was exercised
in isolation against synthetic archives covering a write failure, a sink
refusal, an omission without a count, an omission reason that is not a loss, and
an omission on a channel outside the browser collector, and the two counting
paths in the bridge and the receiver are covered only by unit assertions. The
accounting is also bounded by where it runs: a process that loses records and
then exits, or whose pipe never recovers, never reports the loss, so an archive
with no omission record is evidence of no observed loss rather than proof that
nothing was lost.

## Page-lifecycle determinism validation result

Reference platform, September 23, 2026, at repository revision `eebbc7f`.
Protocol 0.22. Recorded on the same Windows 10.0.19045 host and instrumented
Chromium build as the earlier results in this document, with a 25-second capture
that validated 41,972 events and 143 artifacts and reported no network-service
crashes. The complete script exited with code 0. This is the first run in which
the harness scheduled the fixture's page-lifecycle timers itself, and the first
in which the archive validator checked the `browser.lifecycle` and
`browser.accessibility` payload shapes against a real archive.

The validated session is:

`C:\Users\Public\Documents\A11yRecorderBlinkValidation\20260923-155014-33fd84573bf14503947fe7e5bee9c09a`

| Value | Result |
| --- | --- |
| `LifecycleTimeoutScheduledState` | `visible` |
| `LifecycleTimeoutFiredState` | `hidden` |
| `LifecycleIntervalScheduledState` | `visible` |
| `LifecycleIntervalCancelledState` | `hidden` |
| `LifecycleThrottlingObserved` | none |
| `SchedulerDeferrals` | 8 |
| `AccessibilityCheckpoints` | 2 |
| `AccessibilityCheckpointNodes` | 58 |
| `AccessibilityCheckpointsTruncated` | 0 |
| `RecordedTransitions` | 449 |
| `CoveredTransitions` | 249 |
| `UncoveredTransitions` | 200 |
| `UncoveredTransitionsWithoutPass` | 200 |
| `UncoveredTransitionsAfterLastPass` | 0 |
| `UncoveredTransitionDocuments` | 3 |
| `OMITTED_EVIDENCE_RECORDS` | 0 |
| `SINK_REFUSED_EVENTS` | 0 |
| `OTHER_OMISSION_RECORDS` | 0 |

What this establishes: the page-lifecycle precondition now holds because the
harness produces it rather than because the desktop happened to supply it. The
harness reported the fixture page visible before it scheduled anything, and the
recorded schedule states, callback-entry states, and cancellation state are the
four the assertions require. The stricter archive validation for the two
previously unchecked browser channels passed against an archive that contains
both lifecycle and accessibility records, so the closed payload shapes match
what the bridge and the receiver actually emit. The transition accounting held
at the longer capture duration, with the uncovered population unchanged at 200
across three documents and nothing uncovered after a document's last pass.

What this does not establish: a run whose window cannot be brought to the front
has not been observed, so the early failure path is covered only by the bounded
wait and its message. The recorded counts are properties of this run rather than
contracts, and the higher event and artifact totals follow from the 25-second
capture rather than from any new evidence type. Nothing here measures how
Chromium decides page visibility during load, only that the harness no longer
depends on that decision.

## Cookie operation logging

Protocol 0.23 records cookie operations on the `browser.cookie` channel. The run
script serves a second fixture page from a loopback HTTP listener it starts for
the run, because cookie APIs refuse the listener fixture's file URL and a
Set-Cookie header needs an HTTP response. The page is opened in a background
tab so the listener fixture stays in the foreground and its page-lifecycle
evidence is unaffected, and it schedules no timers. The harness passes the page
a value generated for the run, and the page writes and reads `document.cookie`,
calls the Cookie Store `set`, `get`, `getAll`, and `delete` methods with a
change listener registered, and fetches one response that sets a cookie and one
request that sends it. The page document itself is served with a Set-Cookie
header.

The verifier requires a `document-cookie-write` and a `document-cookie-read`
record with the fixture's names, a `cookie-store-request` for each of the four
methods paired by `requestId` with a resolved `cookie-store-result`, two
dispatched `cookie-store-change` records for the Cookie Store cookie, a
navigation `cookie-access` record for the document's Set-Cookie header, and
frame `cookie-access` records for the fetch that set a cookie and the fetch that
sent it. The script-call records must report the fixture as their location and
the main world. No line of the session's event file may contain the run's
cookie value. The world check that previously rejected any world identity
outside the listener channel now also admits the three cookie record types that
are written at a script's call.

These checks show that the logger emitted a record for each operation the page
performed, with names and without values. They do not evaluate the page's cookie
use.

### Measured result

Reference platform, September 23, 2026, at repository revision `4df0e4f`.
The branch was rebased when it was merged, so the same tree is commit `23da555`
on `main`. Protocol 0.23. Recorded on the same Windows 10.0.19045 host and instrumented
Chromium build as the earlier results in this document, rebuilt with the cookie
hooks, with a 25-second capture that validated 43,695 events and 136 artifacts
and reported no network-service crashes. The recorder accepted 43,695 records
and dropped none. The complete script exited with code 0.

The validated session is:

`C:\Users\Public\Downloads\A11yRecorderCookieLogging\sessions\20260923-175231-685e0a6d327f4688899a3fccd31a4ccf`

| Value | Result |
| --- | --- |
| `CookieRecords` | 26 |
| `DocumentCookieWriteOutcome` | `sent-to-cookie-manager` |
| `DocumentCookieReadServedFrom` | `cookie-manager` |
| `DocumentCookieReadNames` | `a11y_recorder_response`, `a11y_recorder_document` |
| `CookieStoreRequestIds` | `cookie-store-request-1` to `cookie-store-request-4` |
| `CookieStoreChangeCauses` | `inserted`, `expired-overwrite` |
| `NavigationCookieAccessNames` | `a11y_recorder_response` |
| `FrameCookieChangeUrl` | `http://127.0.0.1:53690/set-cookie` |
| `FrameCookieReadUrl` | `http://127.0.0.1:53690/echo` |
| `RecordsContainingCookieValue` | 0 |
| `OMITTED_EVIDENCE_RECORDS` | 0 |
| `SINK_REFUSED_EVENTS` | 0 |
| `OTHER_OMISSION_RECORDS` | 0 |

What this establishes: all six cookie hooks applied to the reference Chromium
tree, built, and emitted records that the recorder accepted under the closed
0.23 payload shapes. Each operation the fixture performed produced its record:
the `document.cookie` write and read, a paired request and result for each of
the four Cookie Store methods, change deliveries for the Cookie Store write and
delete, the navigation access for the document's Set-Cookie header, and the
frame accesses for the fetch that set a cookie and the fetch that sent it. The
run's cookie value appeared in no line of the event file. Every earlier
measurement in this document held, including the world check with the cookie
call records admitted, the page-lifecycle states, and 200 uncovered transitions
across three documents with none after a document's last pass. The listener
fixture's page-lifecycle evidence was unaffected by the background cookie tab.

What this does not establish: the fixture exercises only the main world, a
window context, and first-party cookies on a loopback origin, so records for a
Cookie Store call from a service worker, a cookie call from an isolated world,
a third-party or partitioned cookie, and an excluded cookie with its reasons
have not been observed in a real run. The refusal outcomes and the
renderer-cache read path are covered only by the source patches and their
tests. The harness's own report of the page's Cookie Store changes printed as a
type name in this run, because Windows PowerShell 5.1 does not enumerate a
parsed JSON array; the verifier reads the changes from the archive, so the
result does not depend on that report, and the parsing was corrected after the
run.

## Interaction-state logging

Protocol 0.24 records focus, selection, text-control value, and
element-reflected active descendant changes on the `browser.interaction`
channel. The run script serves a third fixture page at `/interaction` from the
loopback HTTP listener the cookie fixture uses. DevTools key and text input
reaches a page only after its widget has painted, and a tab opened in the
background never paints, so the page runs in a foreground tab. It is opened
after the listener fixture has been hidden behind the background target, so it
hides that target rather than the listener fixture, and the background target
is activated again before the tab closes so the listener fixture stays hidden.
The harness requires the page to report `visible` and waits two animation
frames before sending input. The page schedules no timers and registers no
listeners. The harness makes the page change interaction state in this order:

1. The page's script focuses a button with `preventScroll`.
2. A Tab key press sent as DevTools input moves focus to a text field.
3. Text typed through DevTools input changes the field's value.
4. The page's script sets the field's and a textarea's values, focuses the
   textarea, and selects part of its text with `setSelectionRange`.
5. Text typed through DevTools input replaces the selection.
6. The page's script assigns `ariaActiveDescendantElement` on a listbox,
   focuses the listbox, and blurs it.

The harness reads the page's state back after each step and stops the run if a
step did not take effect, so a missing record is not confused with a step that
never happened.

The verifier identifies the fixture document from the script focus record,
whose location names the fixture page, and then requires, within that
document, a `focus-changed` record for the script focus, for the Tab key press
with the `forward` focus type, the `user-gesture` trigger, and the button as
the previous node, for the listbox focus with the referenced option as its
active descendant, and for the blur with the `cleared` outcome. It requires a
`text-control-value-changed` record for each typed value with the `user-edit`
source and for each script value with the `value-set` source, a
`selection-changed` record with the textarea's selection offsets, and an
`active-descendant-reference-set` record. The records of changes made by script
must report a script location and the main world. The records of changes made
by input must report no location and no world. The world check that rejects a
world identity outside the listener channel and the cookie call records now
also admits the interaction records, which report the world of the script that
made a change.

These checks show that the logger emitted a record for each change the page
made. They do not evaluate the page's focus handling, labelling, or keyboard
support.

### Measured result

Reference platform, September 23, 2026, at repository revision `8469bbe`.
The branch was rebased when it was merged, so the same tree is commit `ec7e030`
on `main`. Protocol 0.24. Recorded on the same Windows 10.0.19045 host and instrumented
Chromium build as the earlier results in this document, rebuilt with the
interaction hooks, with a 25-second capture that validated 26,232 events and
135 artifacts and reported no network-service crashes. The recorder accepted
26,232 records and dropped none. The complete script exited with code 0.

The validated session is:

`C:\Users\Public\Downloads\A11yRecorderInteractionLogging\sessions\20260923-205955-320e37dd1deb4b26869ffd61177169e9`

| Value | Result |
| --- | --- |
| `InteractionRecords` | 16 |
| `InteractionDocumentId` | `dom-document-28` |
| `ScriptFocusNodeId` | 46 |
| `TabFocusNodeId` | 49 |
| `TypedFieldValue` | `typed` |
| `ScriptTextareaNodeId` | 51 |
| `TextareaSelection` | 1-4 |
| `TypedTextareaValue` | `nXs set by script` |
| `ActiveDescendantNodeId` | 58 |
| `ListboxFocusOutcome` | `focused` |
| `BlurOutcome` | `cleared` |
| `CookieRecords` | 27 |
| `RecordsContainingCookieValue` | 0 |
| `OMITTED_EVIDENCE_RECORDS` | 0 |
| `SINK_REFUSED_EVENTS` | 0 |
| `OTHER_OMISSION_RECORDS` | 0 |

What this establishes: the interaction hooks applied to the reference Chromium
tree, built, and emitted records that the recorder accepted under the closed
0.24 payload shapes. Each change the fixture made produced its record: the
script focus, the Tab key focus move, the typed field value, the script values,
the textarea selection, the typed replacement of that selection, the
element-reflected active descendant, the listbox focus that reported it, and
the blur. Every earlier measurement in this document held, including the cookie
records with no cookie value in the event file, the page-lifecycle states, and
200 uncovered transitions across three documents with none after a document's
last pass.

The first attempt, at revision `61e0372`, stopped at the harness step that
checks the Tab key press, before verification. That session holds the script
focus record for the fixture document but no `keydown` dispatch there, so the
key never reached the page. The fixture tab had been opened in the background,
and revision `8469bbe` runs it in a foreground tab as described above. The same
session also holds interaction records from Chromium's own WebUI pages, such as
the omnibox popup, because the hooks log every renderer document.

What this does not establish: the fixture exercises only the main world, a
single top-level document, light DOM controls, and one Tab press, so records
for focus moves across frames or shadow roots, focus by pointer, IME
composition, `contenteditable` editing, and selection changes outside a text
control have not been observed in a real run. The explanation that background
input was dropped because the tab never painted is inferred from the missing
dispatch record and the passing foreground run, not measured directly.

## Layout and computed-style logging

Protocol 0.25 records layout geometry and computed styles on the
`browser.layout` channel. The run script serves a fourth fixture page at
`/layout` from the loopback HTTP listener the cookie fixture uses, and opens it
in a foreground tab after the interaction fixture, while the listener fixture
page is still hidden behind the background target. Layout checkpoints are only
recorded for a document whose rendering update reaches the paint-clean state,
and a background tab is not painted, so the tab must be in the foreground. The
background target is activated again before the tab closes. The page schedules
no timers and registers no listeners. It holds a paragraph of text, a box
styled 200 by 50 CSS pixels in `rgb(0, 0, 128)`, and a `span` with
`display: none`. The harness calls three page functions in order:

1. `settle` waits two animation frames.
2. `widen` sets the box's width to 320 pixels and waits two animation frames.
3. `recolor` sets only the box's color to `rgb(128, 0, 0)` and waits two
   animation frames.

After its frames, each function reports the box's `getBoundingClientRect()`
values, the window's `innerWidth` and `innerHeight`, and the box's computed
color. The harness stops the run if the widened box is not 320 pixels wide or
the recolored box is not dark red, so a missing record is not confused with a
step that never happened. The reports are passed to the verifier.

The verifier identifies the fixture document from the committed main-frame
navigation to the `/layout` URL and selects the layout records with that
navigation's document token and renderer process. It requires at least three
checkpoints, and for every checkpoint of the fixture document it requires one
completion whose node count equals the node records emitted, no truncation,
node indexes from zero without gaps, the `rendering-update` reason, a node
limit of 100000, a `previousCheckpointId` naming the document's preceding
checkpoint, counters that differ from the preceding checkpoint's, and the
defined property list, in order in the start record and as exactly the keys
of each element's computed style, whose member order JSON does not preserve.
It then finds, in order, a checkpoint with the box 200 pixels wide in navy, a
later one with the box 320 pixels wide in navy,
and a later one with the box 320 pixels wide in dark red, all with the same
node identity. For each of those three it requires the recorded rectangle to
equal the page's report to within 0.01 pixels, the recorded viewport to equal
the page's `innerWidth` and `innerHeight` to within one pixel, and the recorded
color to equal the page's. In the settled checkpoint it requires the
`display: none` element without a layout object or rectangle and at least one
laid-out text node with a rectangle and no computed style.

These checks show that the logger emitted complete layout checkpoints and that
the recorded geometry and styles agree with what the page itself reported.
They do not evaluate the page's layout or styling.

### Measured result

Reference platform, September 23, 2026, at repository revision `841eff1`.
The branch was rebased when it was merged, so the same tree is commit `1d1eaf1`
on `main`. Protocol 0.25. Recorded on the same Windows 10.0.19045 host and instrumented
Chromium build as the earlier results in this document, rebuilt with the
layout hook, with a 25-second capture that validated 57,355 events and 137
artifacts and reported no network-service crashes. The recorder accepted
57,355 records and dropped none. The complete script exited with code 0.

The validated session is:

`C:\Users\Public\Downloads\A11yRecorderLayoutLogging\sessions\20260923-223319-5211199eac264a59901ca54b33f3e34d`

| Value | Result |
| --- | --- |
| `LayoutRecords` | 42 |
| `LayoutCheckpoints` | 3 |
| `LayoutBoxNodeId` | 83 |
| `SettledCheckpointId` | `layout-checkpoint-8` |
| `WidenedCheckpointId` | `layout-checkpoint-9` |
| `RecoloredCheckpointId` | `layout-checkpoint-10` |
| `SettledNodeCount` | 12 |
| `SettledBoxRect` | 8,50 200x50 |
| `WidenedBoxWidth` | 320 |
| `RecoloredBoxColor` | `rgb(128, 0, 0)` |
| `Viewport` | 929x925 |
| `DevicePixelRatio` | 1 |
| `LayoutZoomFactor` | 1 |
| `InteractionRecords` | 16 |
| `CookieRecords` | 28 |
| `RecordsContainingCookieValue` | 0 |
| `OMITTED_EVIDENCE_RECORDS` | 0 |
| `SINK_REFUSED_EVENTS` | 0 |
| `OTHER_OMISSION_RECORDS` | 0 |

What this establishes: the layout hook applied to the reference Chromium tree,
built, and emitted records that the recorder accepted under the closed 0.25
payload shapes. The fixture document produced exactly three checkpoints, one
for each state the page created, each complete, untruncated, and linked to its
predecessor, and the recorded box rectangle, viewport size, and box color in
each agreed with what the page reported through `getBoundingClientRect()`,
`innerWidth`, `innerHeight`, and `getComputedStyle()`. Every earlier
measurement in this document held, including the interaction records, the
cookie records with no cookie value in the event file, and 200 uncovered
transitions across three documents with none after a document's last pass.

Three earlier attempts did not complete. Revision `e459624` did not compile,
because Blink builds with unsafe buffer usage as an error and the hook indexed
its property array. Revision `e8cf1aa` iterated the array instead, but the
integration inserts the helper only when it is absent, so the tree kept the
indexed loop and failed the same way; revision `5de598b` upgrades that helper
in place. That run captured and recorded all four fixtures, and the verifier
then stopped on three faults of its own, corrected in revision `841eff1`: it
read an execution world from a collector omission record on the listener
channel, it required the computed-style members in list order although the
bridge's JSON serialization sorts them, and its layout section overwrote a
variable the final summary reads. The verifier as corrected accepted the
`5de598b` session before the `841eff1` run.

The omission record in the `5de598b` session reported
`ephemeral-browser-profile-delete-failed`: the recorder could not delete the
temporary browser profile after Chromium stopped. The `841eff1` run reported no
such record. The cause of the single failure has not been determined.

What this does not establish: the fixture exercises one light-DOM box in one
top-level document at a device pixel ratio and zoom of 1, so records for
shadow-root content, subframes, fragmented or wrapped inline boxes, zoomed or
high-density displays, display-locked content, and documents near the node
limit have not been observed in a real run. The hook logs every rendered
document: the `5de598b` session held 2,420 layout records, of which 42 belonged
to the fixture document and the rest to the other documents Chromium rendered
during the capture. The volume of the `841eff1` session was not counted.

### Measured result with 283 properties

The `841eff1` run recorded the first 75 properties. Revision `eef1313`, commit
`c901b60` on `main` after the rebase, extends the list to 283, and the complete script was run again on the same host,
September 23, 2026, rebuilt with the extended list. The 25-second capture
validated 56,653 events and 136 artifacts and reported no network-service
crashes. The recorder accepted 56,653 records and dropped none. The complete
script exited with code 0, with every value in the earlier table reproduced
except `LayoutBoxNodeId`, which was 96, and no omission records of any kind.

The validated session is:

`C:\Users\Public\Downloads\A11yRecorderLayoutLogging\sessions\20260923-231346-d199fb1628534347b38d1968bfb2206e`

The verifier required every element's computed style to hold exactly the 283
names. The table compares that session with the `5de598b` session, which
recorded 75 properties; both event files were read in full.

| Value | 75 properties (`5de598b`) | 283 properties (`eef1313`) |
| --- | --- | --- |
| Layout records | 2,420 | 2,372 |
| Layout channel bytes | 3,640,072 | 4,790,667 |
| Elements with a recorded computed style | 229 | 209 |
| Mean serialized computed style per element, bytes | 1,742 | 7,166 |
| Largest node record, bytes | 3,128 | 8,555 |
| Property values reported as null | 0 | 0 |
| Event file bytes | 75,182,900 | 75,518,188 |

In these sessions the layout channel is about 5 and 6 percent of the event
file; the DOM and UI Automation channels are the largest. Most element records
carry no computed style: of 2,158 element records in the `eef1313` session,
1,949 had none. They include every element whose own `display` is `none`, such
as `HEAD`, `SCRIPT`, and the fixture's hidden `SPAN`, and the SVG `g` and
`path` elements inside the icon sets of Chromium's own pages. Blink keeps no
computed style for those elements after a rendering update, and the hook does
not request one. The sessions are short and their pages small, so these
figures do not predict the volume of a styled application page.

## Network metadata logging

Protocol 0.26 records network metadata on the `browser.network` channel. The
run script serves a fifth fixture page at `/network` from the loopback HTTP
listener the cookie fixture uses, and opens `/network-start` in a background
tab after the layout fixture. `/network-start` answers with a 302 redirect to
`/network`, so the page's own navigation has a redirect chain. The page does
not need to be painted. The listener fixture's cookies are still set in the
profile, so requests to the shared origin carry a `Cookie` header.

For each run the script generates three request credential values and one
response credential value, and binds and releases a loopback port so that a
connection to it is refused. The harness then calls one page function, which:

1. fetches `/network/data` with an `Authorization` header holding `Bearer` and
   the first value, an `X-Api-Key` header holding the second, an
   `X-Fixture-Scheme` header holding `Bearer` and the third, and an
   `X-Fixture-Plain` header holding `network-fixture-plain`; the response
   carries `X-Fixture-Response: network-fixture-response` and an
   `X-Session-Id` header holding the response credential value;
2. fetches `/network/hop`, which redirects to `/network/data?hop=1`;
3. fetches the closed port in `no-cors` mode and catches the rejection;
4. adds a script element for `/network/cached.js`, served with
   `Cache-Control: max-age=600`, waits for it to load, and does so again, so
   the second load is served from Blink's memory cache; and
5. starts a dedicated worker from `/network/worker.js`, which fetches
   `/network/worker-data` and posts the text back.

The page reports the data status, whether the second fetch was redirected, the
refused fetch's outcome, how many times the cached script ran, and the worker's
text. The verifier stops if any of those differs from what the steps should
produce, so a missing record is not confused with a step that never happened.

The verifier requires that no line of the session's event log contains any of
the four credential values or the cookie fixture's value, and that every
`Cookie`, `Set-Cookie`, `Set-Cookie2`, `Authorization`, and
`Proxy-Authorization` header in every network record has its value withheld
with the `credential-header` reason. For the first fetch it requires a
window-scope `request-will-be-sent` record with a request identifier in which
`Authorization` is withheld as `credential-header`, `X-Api-Key` as
`credential-name`, `X-Fixture-Scheme` as `credential-value`, and
`X-Fixture-Plain` keeps its value; `response-received` and `request-finished`
records with the same inspector identifier and document, the response from the
loader with status 200, `X-Fixture-Response` kept, `X-Session-Id` withheld, and
a decoded body of 12 bytes; and `request-headers-sent` and
`response-headers-received` records with the same request identifier, in which
`Authorization` and `Cookie` are withheld, `X-Fixture-Plain` is kept, the
cookie list names `a11y_recorder_response`, and `X-Session-Id` is withheld. It
requires a redirect record for `/network/data?hop=1` carrying the 302 response
and its `Location` value, and a finish for the same load; a `request-failed`
record for the closed port with a negative network error; a `memory-cache-hit`
record for `/network/cached.js` with status 200; a dedicated-worker
`request-will-be-sent` record for `/network/worker-data` naming the worker
script and a worker token, and its finish; and a committed
`navigation-response` record for `/network` whose redirect chain is
`/network-start` then `/network`, with a status 200 response and timing.

These checks show that the logger emitted linked network records and withheld
credential values. They do not evaluate the page's network use. No measured
result has been recorded yet.

# Blink Evidence Validation Plan

## Purpose

This document defines the first five Blink evidence implementation slices and
the Windows validation required before any can be described as complete.
The first validated slice records accepted Node listener registrations and the
start of Node event dispatches. The second implemented slice correlates
listener removal and invocation with dispatch completion. The third implemented
slice records the ordered Node path and each invoked listener's current target.
The fourth implemented slice records decisions at Blink's Node
`DefaultEventHandler` boundary. The fifth implemented slice records correlated
window `setTimeout` and `setInterval` scheduling, callback entry, and explicit
interval cancellation.
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

The hook currently records only EventTargets that are Nodes. Window, worker,
and other non-Node EventTargets remain outside this slice. Inline attributes,
`on*` properties, isolated-world identity, and source location remain
outstanding.

### Listener removal and invocation

The second slice retains a process-local correlation from each accepted
`RegisteredEventListener` to its listener identifier. A successful
`removeEventListener` operation emits `listener-removed` with the identifier
allocated at registration. The ordinary Blink listener loop preserves that
identifier before automatic `once` removal, then emits `listener-invoked`
after the callback returns.

The invocation record therefore captures the callback's current target and
resulting
`defaultPrevented`, propagation-stopped, and immediate-propagation-stopped
state. The hook does not yet instrument Blink animation triggers, non-Node
targets, inline attributes, or event-handler properties.

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

This first propagation increment does not yet include Window, non-Node event
targets, or a separate representation of shadow-adjusted targets. Closed
shadow-root behavior therefore remains outside the validated claim.

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
animation frames, idle callbacks, or browser-process task scheduling.
Throttling remains null, page lifecycle state is `unknown`, and callback
location remains null so the archive does not infer facts that the hook does
not observe.

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
installs a 125-millisecond interval that clears itself after one callback, then
invokes `HTMLElement.click()` from a 250-millisecond timeout. The target
listeners call `preventDefault()`, remove the named listener, and call
`stopPropagation()`. The expanded fixture must produce:

- At least one `browser.listener` `listener-registered` record for the
  `pointer-only` element and `click` event.
- At least one `browser.dispatch` `dispatch-started` record for the same
  document and node.
- One correlated `listener-removed` record.
- One correlated `listener-invoked` record with phase `at-target` and
  `defaultPrevented` true.
- One correlated `dispatch-completed` record with outcome
  `canceled-by-event-handler`.
- A composed Node path beginning with `#pointer-only` and containing
  `#propagation-root`.
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
- Renderer process context and stable non-empty listener and dispatch
  identifiers across the lifecycle, plus stable non-empty timer identifiers
  across each timer lifecycle.

Because `HTMLElement.click()` dispatches a synthetic event, the expected
`trusted` value is false.

## Windows validation procedure

Run the complete validation from a standard, non-elevated PowerShell window:

```powershell
.\scripts\Run-BlinkValidation.ps1 `
  -ChromiumSource C:\Users\User\chromium-dev\chromium\src
```

The script tests and applies the integration, normalizes copied-file
timestamps, builds Chromium, runs the managed test suite, captures the
deterministic fixture, requires a valid archive, and runs
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

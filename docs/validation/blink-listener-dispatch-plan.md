# Blink Listener and Dispatch Validation Plan

## Purpose

This document defines the first two Blink evidence implementation slices and
the Windows validation required before either can be described as complete.
The first validated slice records accepted Node listener registrations and the
start of Node event dispatches. The second implemented slice correlates
listener removal and invocation with dispatch completion. It does not claim
complete listener or dispatch coverage.

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

The invocation record therefore captures the callback's resulting
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

The initial composed-path value is empty rather than presenting partial path
data as complete evidence.

### Dispatch completion

After Blink calculates its `DispatchEventResult`, the second slice emits
`dispatch-completed` with the same dispatch identifier as `dispatch-started`.
The record includes the final default-prevention and propagation state and one
of these outcomes:

- `not-canceled`
- `canceled-by-event-handler`
- `canceled-by-default-event-handler`
- `canceled-before-dispatch`

Full retargeted and composed paths, default-action detail, callback timing,
and omission handling for bridge backpressure remain outstanding.

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

`tests/fixtures/blink-listener-dispatch.html` installs one named click listener
on `#pointer-only`, invokes `HTMLElement.click()` after 250 milliseconds,
calls `preventDefault()`, and removes the listener inside the callback. The
expanded fixture must produce:

- At least one `browser.listener` `listener-registered` record for the
  `pointer-only` element and `click` event.
- At least one `browser.dispatch` `dispatch-started` record for the same
  document and node.
- One correlated `listener-removed` record.
- One correlated `listener-invoked` record with phase `at-target` and
  `defaultPrevented` true.
- One correlated `dispatch-completed` record with outcome
  `canceled-by-event-handler`.
- Renderer process context and stable non-empty listener and dispatch
  identifiers across the lifecycle.

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
completion for the deterministic Node fixture. Complete composed paths,
default-action detail, timers, cookies, DOM or accessibility snapshots,
network evidence, compositor evidence, and rendering evidence remain
unvalidated.

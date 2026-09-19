# Blink Listener and Dispatch Validation Plan

## Purpose

This document defines the first Blink evidence implementation slice and the
Windows validation required before it can be described as complete. The slice
records accepted Node listener registrations and the start of Node event
dispatches. It does not claim complete listener or dispatch coverage.

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
`on*` properties, listener removal, isolated-world identity, and source
location also remain outstanding.

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
data as complete evidence. Full retargeted and composed paths, listener
invocation, propagation changes,
default-action handling, completion outcome, and timing remain outstanding.

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

`tests/fixtures/blink-listener-dispatch.html` installs one click listener on
`#pointer-only` and invokes `HTMLElement.click()` after 250 milliseconds. This
must produce:

- At least one `browser.listener` `listener-registered` record for the
  `pointer-only` element and `click` event.
- At least one `browser.dispatch` `dispatch-started` record for the same
  document and node.
- Renderer process context, stable non-empty listener and dispatch
  identifiers, and phase `none`.

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
dispatch-start slice. It does not validate listener removal, listener
invocation, complete composed paths, default actions, timers, cookies, DOM or
accessibility snapshots, network evidence, compositor evidence, or rendering
evidence.

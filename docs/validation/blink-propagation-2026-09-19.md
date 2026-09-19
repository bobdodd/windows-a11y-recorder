# Blink Propagation Validation

## Purpose

This record documents the successful reference-machine validation of the third
Blink evidence slice. The slice records the ordered Node propagation path,
listener current targets and phases, and cumulative propagation state across a
correlated dispatch lifecycle.

## Environment

- Validation date: September 19, 2026
- Operating system: Windows 10 22H2, build 19045, x64
- .NET SDK: 10.0.401
- .NET runtime: 10.0.12
- Chromium version: 156.0.8065.0
- Chromium output: `out\A11yRecorder\chrome.exe`
- Validated source checkpoint: `8e3bacc`
- Browser evidence protocol: 0.3

## Procedure

The validation ran `scripts\Run-BlinkValidation.ps1` from a standard,
non-elevated PowerShell session. The script:

1. Tested the portable Chromium integration script.
2. Applied the integration to the reference Chromium checkout.
3. Built the instrumented Chromium executable.
4. Ran the managed recorder test suite.
5. Captured the deterministic Blink fixture for 15 seconds.
6. Ran archive validation and the fixture-specific evidence verifier.
7. Checked the Chromium log for network-service crashes.

The fixture installed capture and bubble listeners on
`#propagation-root` and two listeners on `#pointer-only`. The target listeners
called `preventDefault()`, removed the primary listener, and called
`stopPropagation()`.

## Result

The complete validation script exited with code 0. The session was:

`C:\Users\Public\Documents\A11yRecorderBlinkValidation\20260919-173036-d2ebf59aabe7413d93a32cbb72b7be96`

The capture host accepted 476 records and dropped none. Archive validator
version 1.2 accepted 476 events and 81 artifacts with no issues.

The fixture verifier reported:

- Listener records: 2
- Dispatch-start records: 1
- Listener-invocation records: 3
- Dispatch-completion records: 1
- Listener-removal records: 1
- Renderer process ID: 26184
- Document ID: `dom-document-3`
- Original target node ID: 4
- Listener ID: `listener-3`
- Dispatch ID: `dispatch-7`
- Trusted dispatch: false
- Primary invocation phase: `at-target`
- Composed path Nodes: 5
- Root capture invocations: 1
- Root bubble invocations: 0
- Dispatch outcome: `canceled-by-event-handler`
- Network-service crashes: 0

These results establish that the recorded path begins with the original target,
contains the expected ancestor, associates each invocation with its current
Node and phase, and reflects the stopped ancestor bubble traversal. Listener
and dispatch identifiers remain stable across registration, invocation,
removal, dispatch start, and dispatch completion.

## Defect found during validation

Candidate `ab12be7` connected the renderer and correctly recorded
`propagationStopped=true` after the second target listener. It also correctly
recorded no ancestor bubble invocation. Its dispatch-completion record
contained `propagationStopped=false`, however, because Blink cleared the event
flag before the completion hook ran.

Checkpoint `8e3bacc` corrected this by retaining cumulative
default-prevention and propagation flags in the bridge dispatch state after
each listener callback. Dispatch completion combines those retained values
with any flags still present when Blink returns. The successful run validated
that correction.

## Validated scope

This run validates:

- Ordered paths for Node entries in Blink's established event path.
- Original Node target and per-listener Node current target.
- Capturing and at-target listener phases.
- Correlation across listener and dispatch lifecycle records.
- `preventDefault()` and `stopPropagation()` state observed after callbacks.
- Cumulative propagation state in dispatch completion.
- Omission of the ancestor bubble invocation after propagation stops.
- Renderer authentication, clock synchronization, session finalization, and
  archive validation for this fixture.

## Scope boundary

This run does not validate Window or other non-Node EventTargets,
shadow-adjusted targets, closed shadow roots, listener exceptions or duration,
default-action detail, timers, cookies, DOM or accessibility snapshots,
network evidence, compositor evidence, rendering evidence, or sustained
high-volume operation. These remain explicit future slices.

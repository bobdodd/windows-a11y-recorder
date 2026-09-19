# Blink Animation-Frame Validation

## Purpose

This record documents the successful reference-machine validation of the sixth
Blink evidence slice. The slice records accepted web-exposed
`requestAnimationFrame` schedules, callback entry, and explicit
`cancelAnimationFrame` cancellation.

## Environment

- Validation date: September 19, 2026
- Operating system: Windows 10 22H2, build 19045, x64
- .NET SDK: 10.0.401
- .NET runtime: 10.0.12
- Chromium version: 156.0.8065.0
- Chromium output: `out\\A11yRecorder\\chrome.exe`
- Validated source checkpoint: `a66142e`
- Browser evidence protocol: 0.6

## Procedure

The validation ran `scripts\\Run-BlinkValidation.ps1` from a standard,
non-elevated PowerShell session. The script:

1. Tested the portable Chromium integration script.
2. Applied the integration to the reference Chromium checkout.
3. Built the instrumented Chromium executable.
4. Ran the managed recorder test suite.
5. Captured the deterministic Blink fixture for 15 seconds.
6. Ran archive validation and the fixture-specific evidence verifier.
7. Checked the Chromium log for network-service crashes.

The fixture requested two web-exposed animation-frame callbacks and explicitly
cancelled one. The remaining callback entered normally. The fixture also
repeated the previously validated listener, dispatch, propagation,
default-handler, timeout, and interval scenarios.

## Result

The complete validation script exited with code 0. The session was:

`C:\\Users\\Public\\Documents\\A11yRecorderBlinkValidation\\20260919-184823-de48e2b9fa7c474da0a7e4945824538d`

Archive validator version 1.2 accepted 1,594 events and 81 artifacts with no
issues. Chromium reported no network-service crashes.

The fixture verifier reported:

- Scheduled animation frames: 2
- Fired animation frames: 1
- Cancelled animation frames: 1
- Fired animation-frame ID: `timer-2`
- Cancelled animation-frame ID: `timer-1`
- Scheduled timeouts: 1
- Fired timeouts: 1
- Scheduled intervals: 1
- Fired intervals: 1
- Cancelled intervals: 1
- Renderer process ID: 20040
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

The fired callback's schedule and callback-entry records share one timer
identifier. The cancelled callback's schedule and explicit-cancellation
records share a second timer identifier. Their monotonic timestamps preserve
schedule, callback-entry, and cancellation order.

## Defect found during validation

The first candidate located callback entry through an exact multiline
`FireAnimationFrame` DevTools trace macro. The macro name and formatting differ
in the reference Chromium revision, so integration rejected the source before
building.

The corrected installer locates `ExecuteFrameCallbacksImpl` and inserts the
hook immediately before its timestamp-selection branch. It also retains a
guarded fallback for an older body containing one direct callback invocation.
Regression tests cover both source shapes and require exactly one insertion.

## Validated scope

This run validates:

- Two accepted web-exposed animation-frame schedules.
- Stable process-local animation-frame correlation.
- Callback-entry evidence for one scheduled callback.
- Explicit cancellation of the other scheduled callback.
- Null requested and effective delay values.
- Zero nesting level for animation-frame lifecycle records.
- Renderer authentication, clock synchronization, session finalization, and
  archive validation for this fixture.

## Scope boundary

A `timer-fired` record with timer kind `animation-frame` proves entry at
Blink's animation-frame callback boundary. It does not prove callback
completion, frame presentation, compositor activity, or a resulting browser
or document-state change.

This run does not validate internal Blink frame callbacks, implicit removal
caused by execution-context destruction, idle callbacks, worker scheduling,
throttling, page lifecycle state, callback source location, sustained
high-volume animation-frame traffic, or omission handling under backpressure.
Window and other non-Node event targets, shadow-adjusted targets, closed shadow
roots, cookies, DOM or accessibility snapshots, network evidence, compositor
evidence, and rendering evidence also remain outside this slice.

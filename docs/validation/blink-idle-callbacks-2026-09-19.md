# Blink Idle-Callback Validation

## Purpose

This record documents the successful reference-machine validation of the
seventh Blink evidence slice. The slice records accepted web-exposed
`requestIdleCallback` schedules, callback entry with Blink's observed
`IdleDeadline.didTimeout` value, and explicit `cancelIdleCallback`
cancellation.

## Environment

- Validation date: September 19, 2026
- Operating system: Windows 10 22H2, build 19045, x64
- .NET SDK: 10.0.401
- .NET runtime: 10.0.12
- Chromium version: 156.0.8065.0
- Chromium output: `out\\A11yRecorder\\chrome.exe`
- Validated source checkpoint: `86a26f8`
- Browser evidence protocol: 0.7

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

The fixture requested two web-exposed idle callbacks. It explicitly cancelled
the callback with a 5,000-millisecond timeout. It kept the main thread busy
long enough for the callback with a 1-millisecond timeout to enter with
`didTimeout` true. The fixture also repeated the previously validated listener,
dispatch, propagation, default-handler, DOM timer, and animation-frame
scenarios.

## Result

The complete validation script exited with code 0. The session was:

`C:\\Users\\Public\\Documents\\A11yRecorderBlinkValidation\\20260919-193958-7d487516d0e24b03b39c08895f682175`

Archive validator version 1.2 accepted 629 events and 81 artifacts with no
issues. The capture host dropped no records. Chromium reported no
network-service crashes.

The fixture verifier reported:

- Scheduled idle callbacks: 2
- Fired idle callbacks: 1
- Cancelled idle callbacks: 1
- Fired callback `didTimeout`: true
- Fired idle-callback ID: `timer-4`
- Cancelled idle-callback ID: `timer-3`
- Scheduled animation frames: 2
- Fired animation frames: 1
- Cancelled animation frames: 1
- Scheduled timeouts: 1
- Fired timeouts: 1
- Scheduled intervals: 1
- Fired intervals: 1
- Cancelled intervals: 1
- Renderer process ID: 21476
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

## Validated scope

This run validates:

- Two accepted web-exposed idle-callback schedules with explicit timeout
  options.
- Stable process-local idle-callback correlation.
- Timed-out callback-entry evidence for one scheduled callback.
- Blink's observed `didTimeout` value on callback entry.
- Explicit cancellation of the other scheduled callback.
- The supplied timeout as requested and effective delay.
- Zero nesting level for idle-callback lifecycle records.
- Renderer authentication, clock synchronization, session finalization, and
  archive validation for this fixture.

## Scope boundary

A `timer-fired` record with timer kind `idle-callback` proves entry at Blink's
idle-callback boundary. Its `didTimeout` value reports why Blink entered the
callback. It does not prove callback completion or a resulting browser or
document-state change.

This run does not validate idle callbacks without timeout options, implicit
removal caused by execution-context destruction, worker scheduling,
throttling, page lifecycle state, callback source location, sustained
high-volume idle-callback traffic, or omission handling under backpressure.
Window and other non-Node event targets, shadow-adjusted targets, closed shadow
roots, cookies, DOM or accessibility snapshots, network evidence, compositor
evidence, and rendering evidence also remain outside this slice.

# Blink Page-Lifecycle Scheduling Validation

## Purpose

This record documents the successful reference-machine validation of the
eighth Blink evidence slice. The slice records Blink's observed page lifecycle
state at scheduling, callback-entry, and explicit-cancellation boundaries
without inferring scheduler throttling from elapsed delay.

## Environment

- Validation date: September 19, 2026
- Operating system: Windows 10 22H2, build 19045, x64
- .NET SDK: 10.0.401
- .NET runtime: 10.0.12
- Chromium version: 156.0.8065.0
- Chromium output: `out\\A11yRecorder\\chrome.exe`
- Validated source checkpoint: `22fb60b`
- Browser evidence protocol: 0.8

## Procedure

The validation ran from a standard, non-elevated PowerShell session. The
workflow:

1. Tested the portable Chromium integration script, including upgrade of the
   previously installed scheduling hooks.
2. Applied the integration to the reference Chromium checkout.
3. Built the instrumented Chromium executable.
4. Ran the managed recorder test suite.
5. Started a 15-second capture of the deterministic Blink fixture.
6. Used a loopback-only DevTools port to activate a second tab and place the
   fixture page in the hidden state.
7. Ran archive validation and the fixture-specific evidence verifier.
8. Checked the Chromium log for network-service crashes.

PowerShell initially treated expected native Chromium stderr received from the
background capture job as a terminating `RemoteException`. The capture itself
completed and finalized normally. The evidence verifier was then run directly
against that completed session. The validation harness was corrected
separately so native stderr is displayed without masking the capture result.

## Result

The validated session was:

`C:\\Users\\Public\\Documents\\A11yRecorderBlinkValidation\\20260919-200840-ffa0e3287ef84c65bd71845186f01115`

Archive validator version 1.2 accepted 655 events and 81 artifacts with no
issues. The capture host dropped no records. Chromium reported no
network-service crashes.

The fixture verifier reported:

- Lifecycle timeout state at scheduling: `visible`
- Lifecycle timeout state at callback entry: `hidden`
- Lifecycle interval state at scheduling: `visible`
- Lifecycle interval state at explicit cancellation: `hidden`
- Lifecycle timeout timer ID: `timer-9`
- Lifecycle interval timer ID: `timer-5`
- Scheduled timeouts: 1
- Fired timeouts: 1
- Scheduled intervals: 1
- Fired intervals: 1
- Cancelled intervals: 1
- Scheduled animation frames: 2
- Fired animation frames: 1
- Cancelled animation frames: 1
- Scheduled idle callbacks: 2
- Fired idle callbacks: 1
- Cancelled idle callbacks: 1
- Fired idle callback `didTimeout`: true
- Renderer process ID: 19852
- Document ID: `dom-document-3`
- Original target node ID: 4
- Listener ID: `listener-3`
- Dispatch ID: `dispatch-9`
- Dispatch outcome: `canceled-by-event-handler`
- Network-service crashes: 0

The timeout's schedule and callback-entry records share one stable
process-local timer identifier. The interval's schedule and
explicit-cancellation records share a second identifier. The observed state
changed from `visible` at both schedules to `hidden` at the later boundaries.

## Validated scope

This run validates:

- Page lifecycle state at accepted DOM timer scheduling.
- Page lifecycle state at timeout callback entry.
- Page lifecycle state at explicit interval cancellation.
- Stable process-local correlation across the visible-to-hidden transition.
- Preservation of prior listener, dispatch, default-handler, DOM timer,
  animation-frame, and idle-callback evidence.
- Renderer authentication, clock synchronization, session finalization, and
  archive validation for this fixture.

## Scope boundary

The lifecycle state reports Blink's observation at an instrumented boundary.
It does not prove that backgrounding caused a particular delay, that Blink
applied timer throttling, or that the callback completed. The protocol
therefore keeps `throttled` null.

This run does not validate a `frozen` transition, scheduler throttling
decisions, workers, callback source location, queue delay, execution duration,
implicit cancellation, or lifecycle changes outside the instrumented
scheduling boundaries. Window and other non-Node event targets,
shadow-adjusted targets, cookies, DOM or accessibility snapshots, network
evidence, compositor evidence, and rendering evidence also remain outside this
slice.

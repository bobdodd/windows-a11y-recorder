# Blink Scheduler-Decision Validation

## Purpose

This record documents the successful reference-machine validation of the
ninth Blink evidence slice. The slice records an authoritative Blink
task-queue decision when the scheduler moves an allowed wake-up later than its
desired wake-up.

The evidence is queue-scoped. It does not attribute the decision to a
particular DOM timeout, interval, animation-frame callback, or idle callback.

## Environment

- Validation date: September 19, 2026
- Operating system: Windows 10 22H2, build 19045, x64
- Chromium output: `out\\A11yRecorder\\chrome.exe`
- Validated source checkpoint: `459b002`
- Browser evidence protocol: 0.9

## Procedure

The validation ran from a standard, non-elevated PowerShell session. The
workflow:

1. Tested the portable Chromium integration script, including migration of
   previously installed scheduler hooks.
2. Applied the integration to the reference Chromium checkout.
3. Built the instrumented Chromium executable.
4. Ran the managed recorder test suite.
5. Started a 15-second capture of the deterministic Blink fixture.
6. Used a loopback-only DevTools port to place the fixture page in the hidden
   state and exercise hidden-page scheduling.
7. Ran archive validation and the fixture-specific evidence verifier.
8. Checked the Chromium log for network-service crashes.

## Result

The complete validation script exited with code 0. The validated session was:

`C:\\Users\\Public\\Documents\\A11yRecorderBlinkValidation\\20260919-212050-34c8bb75142749378c3d98a3b843ceb1`

Archive validation accepted 658 events and 81 artifacts. The capture host
dropped no records, and Chromium reported no network-service crashes.

The fixture verifier reported:

- Scheduler queue name: `frame-throttleable`
- Scheduler throttling type: `background-intensive`
- Scheduler decision boundary: `task-queue-throttler`
- Lifecycle timeout state at callback entry: `hidden`
- Lifecycle interval state at scheduling: `visible`
- Scheduled timeouts: 1
- Fired timeouts: 1
- Scheduled intervals: 1
- Fired intervals: 1
- Scheduled animation frames: 2
- Scheduled idle callbacks: 2
- Cancelled idle callbacks: 1
- Renderer process ID: 2392
- Document ID: `dom-document-2`
- Original target node ID: 4
- Listener ID: `listener-4`
- Dispatch ID: `dispatch-9`
- Dispatch outcome: `canceled-by-event-handler`
- Network-service crashes: 0

The scheduler record proves that Blink's task-queue throttler deferred an
allowed wake-up for the `frame-throttleable` queue in the identified renderer.
It also preserves Blink's `background-intensive` classification at the
decision boundary.

## Validated scope

This run validates:

- A positive wake-up deferral decision at
  `TaskQueueThrottler::GetNextAllowedWakeUp()`.
- Queue classification as `frame-throttleable`.
- Scheduler classification as `background-intensive`.
- The fixed `task-queue-throttler` decision boundary.
- Preservation of queue-scoped context without an invented document or timer
  correlation.
- Preservation of prior listener, dispatch, default-handler, DOM timer,
  animation-frame, idle-callback, and page-lifecycle evidence.
- Renderer authentication, clock synchronization, session finalization, and
  archive validation for this fixture.

## Scope boundary

This run does not prove that a particular timer caused the scheduler decision
or that a particular callback was delayed by it. Protocol 0.9 has no task
identity that can be joined to a process-local timer identity. Timer
`throttled` values therefore remain null.

The run does not validate task-to-timer correlation, worker scheduler
decisions, callback source locations, task execution duration, frozen-page
transitions, high-volume scheduler traffic, or omission handling under
backpressure. Window and other non-Node event targets, shadow-adjusted
targets, cookies, DOM or accessibility snapshots, network evidence,
compositor evidence, and rendering evidence also remain outside this slice.

# Blink DOM Timer Validation

## Purpose

This record documents the successful reference-machine validation of the fifth
Blink evidence slice. The slice records accepted window `setTimeout` and
`setInterval` schedules, callback entry, and explicit cancellation of a live
timer.

## Environment

- Validation date: September 19, 2026
- Operating system: Windows 10 22H2, build 19045, x64
- .NET SDK: 10.0.401
- .NET runtime: 10.0.12
- Chromium version: 156.0.8065.0
- Chromium output: `out\\A11yRecorder\\chrome.exe`
- Validated source checkpoint: `d68be02`
- Browser evidence protocol: 0.5

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

The fixture scheduled a 125-millisecond interval and a 250-millisecond
timeout. The interval cleared itself during its first callback. The timeout
initiated the existing listener, propagation, and default-handler fixture
actions.

## Result

The complete validation script exited with code 0. The session was:

`C:\\Users\\Public\\Documents\\A11yRecorderBlinkValidation\\20260919-182516-118b0821a08e4cda839c646d10f74202`

The capture host accepted 632 records and dropped none. Archive validator
version 1.2 accepted 632 events and 81 artifacts with no issues. Chromium
reported no network-service crashes.

The fixture verifier reported:

- Listener records: 2
- Dispatch-start records: 1
- Listener-invocation records: 3
- Dispatch-completion records: 1
- Listener-removal records: 1
- Suppressed default actions: 1
- Invoked default actions: 1
- Handled default-action completions: 1
- Scheduled timeouts: 1
- Fired timeouts: 1
- Scheduled intervals: 1
- Fired intervals: 1
- Cancelled intervals: 1
- Timeout timer ID: `timer-2`
- Interval timer ID: `timer-1`
- Renderer process ID: 22968
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

The timeout schedule and callback-entry records share one timer identifier.
The interval schedule, callback-entry, and explicit-clear records share a
second timer identifier. Their monotonic timestamps preserve schedule,
callback-entry, and cancellation order.

## Defects found during validation

The first candidate used a file-global anchor for an `is_interval`
declaration. Chromium contains that declaration in both `DOMTimer::Stop()` and
`DOMTimer::Fired()`, so integration rejected the ambiguous source.

The second candidate combined the declaration with an exact trace-macro
spelling. That formatting differed in the reference Chromium revision. The
final installer first locates `DOMTimer::Fired()` and searches for the
declaration only after that function begins. The integration regression fixture
contains the competing declaration and a deliberately different trace-macro
form.

The first successful capture contained the expected interval cancellation, but
the verifier correlated timer IDs without process identity. Timer identifiers
are process-local, and an unrelated renderer had also allocated `timer-1`.
The final verifier correlates browser instance, renderer process, timer kind,
and timer ID. It passed both the original 620-event diagnostic archive and the
final 632-event validation archive.

## Validated scope

This run validates:

- Accepted window timeout and interval scheduling.
- Requested and effective delay values for the deterministic fixture.
- Timer nesting level at scheduling.
- Stable process-local timer correlation.
- Callback-entry evidence for one timeout and one interval.
- Explicit interval cancellation after its first callback entry.
- Absence of an explicit cancellation record for the fired one-shot timeout.
- Renderer authentication, clock synchronization, session finalization, and
  archive validation for this fixture.

## Scope boundary

A `timer-fired` record proves entry at Blink's timer callback boundary. It does
not prove callback completion or a resulting browser or document-state change.
The current hook does not observe throttling, page lifecycle state, or callback
source location, so those fields remain null or `unknown`.

This run does not validate worker timers, animation frames, idle callbacks,
browser-process task scheduling, context-destruction cancellation, background
throttling, sustained high-volume timer traffic, or omission handling under
backpressure. Window and other non-Node event targets, shadow-adjusted targets,
closed shadow roots, cookies, DOM or accessibility snapshots, network
evidence, compositor evidence, and rendering evidence also remain outside this
slice.

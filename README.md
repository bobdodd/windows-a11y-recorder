# Windows Accessibility Session Recorder

A Windows-first research tool for recording accessibility and usability test sessions as synchronized raw evidence.

The project is intended to produce one standalone application that works with NVDA, JAWS, Narrator, and other Windows assistive technologies without requiring screen-reader add-ons, browser extensions, or external browser drivers. It combines platform-level Windows capture with a bundled instrumented Chromium build. Later analysis will infer user behavior while preserving the distinction between observation and interpretation.

## Status

The first development baseline is
[`v0.1.0`](https://github.com/bobdodd/windows-a11y-recorder/releases/tag/v0.1.0).
It is a source release for research and continued development, not an
end-user production release. See the [changelog](CHANGELOG.md) for the
baseline scope and known limitations.

The repository contains a working technical prototype. The managed solution
currently implements:

- A shared session clock, append-only event writer, manifest finalization, and
  archive validation.
- Raw keyboard and mouse input capture.
- Foreground-window and process observation.
- Desktop frame capture with Windows Graphics Capture and a GDI fallback.
- Microphone and system-audio capture.
- Windows UI Automation event capture.
- An accessible WPF recorder and synchronized session player.
- An authenticated local bridge that launches an instrumented Chromium build,
  synchronizes its clock, and records explicit browser connection lifecycle
  evidence.
- A proof-of-concept WPF capture path for choosing the instrumented Chromium
  executable and starting website, launching it with the recording, and
  filtering its recorded evidence during playback.

The Chromium browser-process bridge was built and exercised end to end on the
reference Windows platform on September 18, 2026. The resulting archive was
valid, contained the expected connection and clock-synchronization records,
accepted 92 records, and dropped none. See the
[Chromium connection validation record](docs/validation/chromium-connection-2026-09-18.md).

Renderer capability propagation and nine narrow Blink evidence slices were
validated end to end on the reference Windows platform on September 19, 2026.
The latest validated archive contained listener lifecycle, dispatch lifecycle,
the ordered Node propagation path, current targets, listener phases,
cumulative propagation-stop state, Node default-event-handler decisions, and
window timeout, interval, animation-frame, and idle-callback lifecycles. DOM
timer records also preserved the observed page lifecycle state across a
visible-to-hidden transition. Protocol 0.9 additionally recorded an
authoritative task-queue wake-up deferral for the `frame-throttleable` queue
with Blink's `background-intensive` classification. The latest validated
archive passed structural validation across 658 events and 81 artifacts and
recorded no network-service crashes. See the
[Blink scheduler-decision validation record](docs/validation/blink-scheduler-decisions-2026-09-19.md).

Protocol 0.10 has been validated on the reference Windows platform. It adds
browser-process evidence for primary-main-frame navigation starts and
completions, with stable page, frame, navigation, and committed-document
identity across cross-document and same-document commits. The validated
archive passed structural validation across 1,860 events and 81 artifacts and
recorded no network-service crashes. See the
[Blink navigation and document-identity validation record](docs/validation/blink-navigation-identity-2026-09-19.md)
and the
[navigation and document identity evidence model](docs/architecture/navigation-document-identity-evidence-model.md).

Protocol 0.11 has been validated on the reference Windows platform. It
extends navigation evidence to child frames and non-primary page types with
explicit frame classification, primary-page membership, root page identity,
direct parent identity, and parent-or-outer-document identity. The deterministic
fixture validates one same-origin child frame while leaving prerender,
fenced-frame, guest-page, nested-frame, and cross-origin execution outside the
validated scope. The archive passed structural validation across 2,026 events
and 81 artifacts and recorded no network-service crashes. See the
[frame and page identity evidence model](docs/architecture/frame-and-page-identity-evidence-model.md).

Protocol 0.12 has been validated on the reference Windows platform. It
implements a bounded, parser-complete DOM structural checkpoint.
Each checkpoint streams a start record, preorder node records, and a completion
record with an explicit node limit and truncation state. The initial slice
records node identity, parent identity, node type, and node name. It does not
record text content, attributes, mutation history, style, layout, accessibility,
paint, or rendered pixels. The validated archive contained 2,236 events and 81
artifacts, with zero dropped records and zero network-service crashes. The
selected fixture checkpoint contained 36 nodes and was not truncated. See the
[DOM checkpoint validation record](docs/validation/blink-dom-checkpoint-2026-09-19.md)
and the
[DOM checkpoint evidence model](docs/architecture/dom-checkpoint-evidence-model.md).

Protocol 0.13 has been validated on the reference Windows platform. It adds a
coalesced post-mutation checkpoint after Blink delivers structural child-list
changes. The deterministic fixture appends one element containing one text
node and requires one later checkpoint for the same renderer document with a
distinct checkpoint identity and the expected two-node structural difference.
The validated archive contained 8,985 events and 81 artifacts, with zero
dropped records and zero network-service crashes.

Protocol 0.14 has been validated on the reference Windows platform. It adds Chromium's
shared document token to committed browser navigations and renderer DOM
checkpoints, plus the hosting renderer process ID to committed navigation
records. The deterministic verifier requires stable mappings across
same-document navigation, distinct main-frame and subframe tokens, and rejects
stale or process-mismatched checkpoint mappings. The validated archive
contained 9,007 events and 81 artifacts, with zero dropped records and zero
network-service crashes.

Protocol 0.15 has been validated on the reference Windows platform. It adds bounded
attribute and character-data evidence. Checkpoints now report the attribute
state of every element node they record, and accepted attribute and
character-data mutations are recorded as transitions that name the specific
attribute or text a coalesced checkpoint cannot recover. Values are recorded
verbatim up to a reported length limit, and every record states whether it was
truncated. The deterministic verifier asserts exact case-sensitive equality
against the fixture's known before and after values for an enumerated change, a
reference change, a name change, a live-region text change, an attribute
removal, and an over-length value. The validated archive contained 18,097
events and 81 artifacts, with zero dropped records and zero network-service
crashes.

Validation of 0.15 also measured two limits of the transition-to-checkpoint
join. A transition named the checkpoint its delivery pass was expected to
produce, and 200 of the 452 transitions in the validated archive named a
reservation that no checkpoint completed. Checkpoint identity is numbered per
renderer, so 49 completed checkpoints used 32 distinct identity strings and a
join requires the browser instance, renderer process, and document to match as
well. Both limits are recorded in the DOM attribute and text evidence model.

Protocol 0.16 is validated on the reference platform, at revision `aabeae1`,
with 18,184 events and 81 artifacts recorded, no network service crashes, no
collector omissions, and clean archive validation. It reverses the direction of
that join. Each transition now carries its own `transitionId`, and
each completed checkpoint reports the count and the first and last identity of
the transitions it covers for its document. The reason is that the earlier
direction was unresolvable by construction: the three documents accounting for
all 200 unjoined transitions appeared in the archive only as transitions, with
no checkpoint and no committed navigation, so they were created, mutated, and
discarded before any delivery pass produced a checkpoint. A checkpoint can only
name transitions that already happened, so no record references absent
evidence, and a transition no checkpoint covers is stated by omission. The
deterministic verifier requires one checkpoint in the fixture document to cover
all six fixture transitions, requires every completed checkpoint to state a
coherent coverage range, and reports how many recorded transitions no
checkpoint covered.

The validated run recorded 444 transitions, of which 200 were covered by no
checkpoint, and the fixture's state checkpoint covered 8. The uncovered figure
is the same population that produced 200 dangling references under 0.15: three
short-lived documents that are mutated and discarded before any delivery pass
produces a checkpoint. Under 0.16 the archive states that absence rather than
naming evidence it does not contain.

Protocol 0.17 is implemented as a proof of concept and awaits reference-platform
build and capture validation. It records the AX update batches Chromium
renderers serialize for the browser process and correlates them to committed
navigations by browser instance, document token, and renderer process. Each
batch reports its update count, event count, node count, 100,000-node limit, and
truncation state. Node records include AX and DOM identities, role, accessible
name and description, focused state, and Chromium's readable serialized
properties. The launcher forces renderer accessibility for deterministic
fixture capture, strict managed ingest covers all three payload shapes, and
playback reports correlated accessibility checkpoint and node counts. These
records are incremental serialization batches, not complete tree snapshots.
See the
[accessibility checkpoint evidence model](docs/architecture/accessibility-checkpoint-evidence-model.md).

Protocol 0.18 is implemented as a proof of concept and awaits reference-platform
build and capture validation. It records listener and dispatch evidence for
EventTargets that are not Nodes, beginning with the window. Every listener and
dispatch target reference reports its kind, the Blink interface name the target
reports for itself, and a process-local target identifier when the target is not
a Node, and reports no node identifier where no DOM node exists. The window
entry at the end of a composed path comes from Blink's own window event context,
so a recorded path ends where Blink's path ends. Worker global scopes, inline
attributes, event-handler properties, isolated-world identity, source location,
and dispatches whose original target is never a Node remain outstanding.

## Project goals

- Capture raw keyboard and mouse evidence.
- Investigate touch, pen, touchpad, and HID capture.
- Record display or window video.
- Record participant microphone audio.
- Isolate screen-reader audio by process where Windows permits it.
- Observe Windows UI Automation events and selected accessibility snapshots.
- Record complete browser listener registration, event dispatch, browser default actions, timers, DOM state, accessibility state, cookie operations, and rendering evidence.
- Store synchronized evidence in an open, versioned session archive.
- Provide an accessible timeline viewer.
- Infer screen-reader behavior only after recording, with provenance and confidence.

## Initial constraints

- Windows 10 and Windows 11 x64 are the prototype platforms.
- The first version must not require screen-reader plugins.
- Browser testing uses the instrumented Chromium build shipped with the application.
- The application must not use Selenium, WebDriver, or another external browser-control process.
- Capture is local-first.
- Raw evidence remains immutable.
- Derived and inferred records are stored separately.
- Missing or uncertain evidence must be reported explicitly.

## Implementation sequence

Completed:

1. Define the event envelope, session manifest, privacy model, and archive
   format.
2. Implement Raw Input, display capture, audio capture, foreground-window
   observation, and UI Automation observation.
3. Build the core recorder and accessible synchronized timeline viewer.
4. Implement and validate the authenticated recorder-to-Chromium browser
   process connection and clock synchronization.
5. Implement recorder capability propagation, independent authentication, and
   clock synchronization for Chromium renderer processes. GPU and utility
   processes remain excluded until they have dedicated evidence hooks.
6. Build and validate the initial Blink Node listener-registration and
   dispatch-start hooks on the reference Chromium Windows build.
7. Correlate and validate Node listener removal and invocation with dispatch
   completion on the reference Chromium Windows build.
8. Record and validate the ordered Blink Node propagation path, each invoked
   listener's current target and phase, and cumulative propagation-stop state.
9. Record and validate Node default-event-handler decisions without claiming
   unobserved browser or document effects.
10. Record and validate accepted window timeout and interval schedules,
    callback entry, and explicit cancellation with process-local correlation.
11. Record and validate accepted web-exposed `requestAnimationFrame`
    schedules, callback entry, and explicit `cancelAnimationFrame`
    cancellation with process-local correlation.
12. Record and validate accepted web-exposed `requestIdleCallback` schedules,
    callback entry with `didTimeout`, and explicit `cancelIdleCallback`
    cancellation with process-local correlation.
13. Record and validate observed page lifecycle state at scheduling,
    callback-entry, and explicit-cancellation boundaries, including a
    visible-to-hidden DOM timer transition.

Remaining:

1. Extend Blink and browser-process evidence to shadow-adjusted and non-Node
   dispatch paths, scheduler throttling decisions, cookies, DOM,
   accessibility, network, and rendering.
2. Record representative NVDA, JAWS, and Narrator sessions.
3. Add evidence correlation and screen-reader behavior analysis.
4. Investigate touch and gesture coverage on representative hardware.

## Build and test

The managed solution requires the .NET 10 SDK and targets Windows 10 version
2004 or later:

```powershell
dotnet restore .\windows-a11y-recorder.slnx
dotnet test .\windows-a11y-recorder.slnx --configuration Release
```

Run the Chromium integration-script test separately:

```powershell
python .\chromium\test_integrate.py
```

The first Blink listener-registration and dispatch-start slice has a complete
reference-machine validation command. Run it from a standard, non-elevated
PowerShell window after the Chromium checkout has been prepared:

```powershell
.\scripts\Run-BlinkValidation.ps1 `
  -ChromiumSource C:\Users\User\chromium-dev\chromium\src
```

The Chromium build requires substantial disk space and the Visual Studio C++
toolchain. Run its setup from Windows PowerShell:

```powershell
.\chromium\setup-windows.ps1
```

The setup script checks out Chromium, applies the recorder bridge, generates
`out\A11yRecorder`, and builds the `chrome` target.

## Documentation

- [Release changelog](CHANGELOG.md)
- [Prototype plan](docs/prototype-plan.md)
- [Recommended Windows implementation stack](docs/architecture/windows-implementation-stack.md)
- [Instrumented Chromium architecture](docs/architecture/instrumented-chromium.md)
- [Prototype architecture](docs/architecture/prototype-architecture.md)
- [Collector contracts](docs/architecture/collector-contracts.md)
- [Reference Windows test platform](docs/architecture/reference-platform.md)
- [Threat model](docs/security/threat-model.md)
- [Privacy and data-handling policy](docs/security/privacy-and-data-handling-policy.md)
- [Chromium connection validation record](docs/validation/chromium-connection-2026-09-18.md)
- [Blink evidence validation plan](docs/validation/blink-listener-dispatch-plan.md)
- [Blink DOM timer validation record](docs/validation/blink-dom-timers-2026-09-19.md)

## Repository visibility

This repository is private while the architecture, privacy controls, and prototype are being developed.

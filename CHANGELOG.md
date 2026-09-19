# Changelog

This project uses semantic version numbers for product releases. Protocol and
archive schema versions are compatibility contracts and advance independently
from the product version.

## Unreleased

### Added

- Added protocol 0.7 evidence for accepted web-exposed
  `requestIdleCallback` schedules, callback entry with the observed
  `IdleDeadline.didTimeout` value, and explicit `cancelIdleCallback`
  cancellation.
- Added deterministic idle-callback fixture coverage, archive contract
  coverage, idempotent Chromium integration tests, and end-to-end verification
  assertions for one timed-out callback and one explicitly cancelled callback.
- Added protocol 0.6 evidence for accepted web-exposed
  `requestAnimationFrame` schedules, callback entry, and explicit
  `cancelAnimationFrame` cancellation.
- Added deterministic animation-frame fixture coverage, archive contract
  coverage, idempotent Chromium integration tests, and end-to-end verification
  assertions for one fired and one cancelled callback.

- Browser-to-child recorder capability propagation for renderer processes
  using inherited read-only shared memory.
- Independent child-process authentication and clock synchronization.
- Browser parent process and Chromium child process identifiers in lifecycle
  evidence.
- Initial Blink Node listener-registration evidence with resolved capture,
  passive, and once options.
- Initial Blink dispatch-start evidence with stable document and node
  references.
- Correlated listener-removal, listener-invocation, and dispatch-completion
  evidence with stable listener and dispatch identifiers.
- Ordered Blink Node propagation paths, listener current targets and phases,
  and cumulative propagation-stop state.
- Correlated Blink Node default-event-handler evidence that distinguishes
  handler invocation from event-handler suppression, prior handling, and
  ineligible untrusted events without claiming a visible browser effect.
- Correlated Blink window timeout and interval evidence for accepted
  scheduling, callback entry, and explicit cancellation, with stable timer
  identifiers and conservative unknown values for unobserved throttling,
  lifecycle state, and callback location.
- A deterministic listener and dispatch fixture plus an archive evidence
  verifier for Windows validation.

### Changed

- Made timer delay values nullable in the native bridge so animation-frame
  evidence does not claim a requested or effective delay.
- Restricted inherited Chromium recorder bootstrap distribution to renderer
  processes. GPU and utility processes are excluded until dedicated evidence
  hooks require them, preventing the recorder from destabilizing Chromium's
  network-service utility process.
- Made deterministic Blink validation fail explicitly if Chromium reports a
  network-service crash, and allowed 15 seconds by default for clean browser
  startup and fixture dispatch.
- Validated renderer bootstrap, listener registration, dispatch start, and
  archive integrity end to end on the reference Windows platform with 371
  events, 81 artifacts, and zero network-service crashes.
- Browser launch now rejects an elevated recorder process with an actionable
  diagnostic. This preserves Chromium's least-privilege boundary and avoids
  losing the inherited standard-input bootstrap during Chromium's Windows
  de-elevation relaunch.
- Browser launch now fails with Chromium's exit code when the process
  terminates during the startup-stability window instead of leaving the
  browser collector incorrectly marked healthy.
- Advanced the browser evidence protocol to version 0.2.
- Restricted accepted browser evidence connections to browser and renderer
  process types with required process correlation metadata.
- Replaced the child-launch handoff's module-local static dependency with a
  browser-process metadata marker so `chrome.dll` and `content/browser` share
  the same read-only shared-memory capability. The marker contains no
  authentication token and is removed from child environments.
- Added opt-in Chromium file logging through
  `A11Y_RECORDER_CHROMIUM_LOG_FILE` for diagnosing early browser and child
  process startup failures.
- Added opt-in native bridge tracing through
  `A11Y_RECORDER_BRIDGE_LOG_FILE` for failures that occur before Chromium's
  normal logging system is initialized, including child-launch hook entry and
  early-return diagnostics.
- Moved Windows child bootstrap attachment to Chromium's shared child-launch
  sequence and made integration remove both historical forms of the obsolete
  platform-local hook.
- Converted the native recorder bridge to an exported Chromium component so
  renderer startup and Blink core share one process-local client.
- Serialized evidence-pipe writes within each Chromium process.
- Advanced the browser evidence protocol to version 0.3 for `currentTarget`
  and populated `composedPath` evidence.
- Advanced the browser evidence protocol to version 0.4 for strict
  default-event-handler decision evidence.
- Advanced the browser evidence protocol to version 0.5 for correlated window
  DOM timer lifecycle evidence and nullable throttling state.
- Advanced the browser evidence protocol to version 0.7 for correlated
  web-exposed idle-callback lifecycle evidence and nullable `didTimeout`.
- Preserved propagation flags observed during listener execution because Blink
  may clear them before the dispatch-completion hook runs.
- Validated the propagation slice end to end on the reference Windows platform
  with 476 events, 81 artifacts, zero dropped records, and zero network-service
  crashes.
- Validated protocol 0.4 default-event-handler evidence end to end on the
  reference Windows platform with 597 events, 81 artifacts, zero dropped
  records, and zero network-service crashes.
- Validated protocol 0.5 window timeout and interval evidence end to end on the
  reference Windows platform with 632 events, 81 artifacts, zero dropped
  records, and zero network-service crashes.
- Validated protocol 0.6 web-exposed animation-frame evidence end to end on the
  reference Windows platform with 1,594 events, 81 artifacts, one fired
  callback, one explicitly cancelled callback, and zero network-service
  crashes.
- Replaced the animation-frame callback hook's formatting-sensitive DevTools
  trace anchor with a callback-invocation anchor and regression coverage for
  current and older Chromium source shapes.

## 0.1.0 - 2026-09-18

The first development baseline of the Windows Accessibility Session Recorder.
This source release establishes the tested recorder, session, playback, and
instrumented Chromium connection foundation for subsequent evidence
instrumentation.

### Included

- A shared monotonic session clock, append-only event storage, manifest
  finalization, and archive validation.
- Raw keyboard and mouse input capture.
- Foreground-window and process observation.
- Desktop frame capture using Windows Graphics Capture with a GDI fallback.
- Microphone and system-audio capture.
- Windows UI Automation event capture.
- An accessible WPF recording and synchronized playback interface.
- An authenticated local Chromium bridge with bootstrap, protocol framing,
  browser-process connection, clock synchronization, and lifecycle evidence.
- Versioned event, manifest, and browser evidence contracts.
- Automated Windows build and test coverage in GitHub Actions.

### Validation baseline

- Windows 10 22H2, build 19045, x64.
- .NET SDK 10.0.401 and runtime 10.0.12.
- Chromium 156.0.8065.0 built with the Chromium Clang toolchain.
- Instrumented Chromium connection recording completed with 92 accepted
  records and no dropped records.
- The resulting session completed archive validation with 77 artifacts and no
  validation issues.
- The focused browser receiver tests passed, 2 of 2.
- The release commit passed the repository's Windows CI workflow.

See the
[Chromium connection validation record](docs/validation/chromium-connection-2026-09-18.md)
for the detailed environment, procedure, evidence, and scope boundary.

### Known limitations

- This is a source baseline and does not include packaged application or
  Chromium binaries.
- Child-process capability distribution is not implemented.
- Renderer and Blink evidence hooks are not implemented.
- Listener registration, dispatch, browser default-action, timer, cookie,
  DOM, accessibility, network, and compositor evidence are not yet recorded.
- Representative NVDA, JAWS, and Narrator validation remains outstanding.
- Touch and gesture coverage remains under investigation.

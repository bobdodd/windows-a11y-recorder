# Changelog

This project uses semantic version numbers for product releases. Protocol and
archive schema versions are compatibility contracts and advance independently
from the product version.

## Unreleased

### Added

- Added protocol 0.14 deterministic browser-renderer document correlation.
  Committed navigation evidence and renderer DOM checkpoints now carry
  Chromium's shared document token, and committed navigation records identify
  the hosting renderer process.
- Added strict validation for committed-navigation correlation fields and DOM
  checkpoint document tokens.
- Added deterministic validation of same-document identity stability,
  main-frame and subframe token separation, and rejection of stale or
  process-mismatched DOM checkpoint mappings.
- Added protocol 0.13 coalesced post-mutation DOM checkpoints. Structural
  child-list changes queue affected documents through Blink's mutation
  delivery microtask machinery without requiring page-created observers.
- Added deterministic verification of one later checkpoint for the same
  renderer document, distinct checkpoint identity, chronological ordering,
  and the expected element-plus-text structural difference.
- Serialized browser evidence sequence allocation with event-sink submission
  across concurrent browser and renderer connections, preventing later
  sequence numbers from overtaking earlier records under checkpoint load.
- Declared the existing `browser.navigation` and new `browser.dom` channels in
  the instrumented-browser collector descriptor and session manifest.
- Updated the
  [DOM checkpoint evidence model](docs/architecture/dom-checkpoint-evidence-model.md)
  with the post-mutation trigger, coalescing semantics, and explicit limits.
- Added protocol 0.12 `browser.dom` evidence for a bounded structural
  checkpoint at Blink's parser-complete boundary.
- Added streamed checkpoint start, preorder node, and completion records with
  stable node and parent identities, explicit node limits, and truncation
  state. Text content and attributes remain excluded.
- Added strict archive validation, idempotent Chromium integration coverage,
  deterministic fixture verification, and a
  [DOM checkpoint evidence model](docs/architecture/dom-checkpoint-evidence-model.md).
- Validated protocol 0.12 end to end on the reference Windows platform across
  2,236 events and 81 artifacts with no dropped records or network-service
  crashes. The selected fixture checkpoint contained 36 nodes and was not
  truncated.
- Added protocol 0.11 frame and page identity for all navigation boundaries,
  including explicit Chromium frame type, primary-page membership, root page
  identity, direct parent identity, and parent-or-outer-document identity.
- Added a deterministic same-origin child-frame fixture, strict relationship
  validation, integration coverage, and an explicit
  [frame and page identity evidence model](docs/architecture/frame-and-page-identity-evidence-model.md).
- Added protocol 0.10 `browser.navigation` evidence for primary-main-frame
  navigation starts and completions at Chromium's browser-process
  `WebContentsImpl` boundaries.
- Added stable page, frame, navigation, and committed-document correlation,
  including explicit cross-document and same-document classification.
- Added a deterministic same-document fixture, strict archive validation,
  idempotent Chromium integration coverage, and an explicit
  [navigation and document identity evidence model](docs/architecture/navigation-document-identity-evidence-model.md).
- Validated protocol 0.10 end to end on the reference Windows platform across
  1,860 events and 81 artifacts with no network-service crashes. The run
  preserved page, frame, and committed-document identity while assigning
  distinct navigation identities to cross-document and same-document commits.
- Added protocol 0.9 `browser.scheduler` evidence for authoritative
  task-queue wake-up deferral decisions at Blink's task-queue throttler
  boundary.
- Added a deterministic hidden-page scheduler fixture, archive contract
  coverage, idempotent Chromium integration tests, and an explicit
  [scheduler decision evidence model](docs/architecture/scheduler-decision-evidence-model.md).
- Preserved timer `throttled` values as null because protocol 0.9 does not
  claim task-to-timer causality.
- Validated protocol 0.9 scheduler-decision evidence end to end on the
  reference Windows platform. The validated archive accepted 658 events and
  81 artifacts, recorded a `frame-throttleable` queue decision with
  `background-intensive` throttling at the `task-queue-throttler` boundary,
  dropped no records, and reported no network-service crashes.
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

### Fixed

- Fixed deterministic Blink validation aborting when a native tool wrote
  progress output to stderr. Python's unittest runner reports progress and its
  summary on stderr even when every test passes, which PowerShell converted
  into a terminating error under a redirected or transcribed run. Step success
  is now decided only by the process exit code.
- Fixed protocol 0.14 integration of Chromium checkouts already patched at
  protocol 0.13. Committed-navigation, parser-complete DOM checkpoint, and
  post-mutation DOM checkpoint hook bodies are now replaced in place instead
  of being treated as already integrated, which previously left stale call
  shapes against the updated bridge header and failed the instrumented
  Chromium build with argument-count errors.
- Added integration coverage that patches fixtures carrying the protocol 0.13
  hook bodies and asserts both the upgrade to document-identity hook bodies
  and unchanged output on a second run.

### Changed

- Validated protocol 0.14 browser-renderer document correlation end to end on
  the reference Windows platform at revision `4b3d436`, with committed
  navigation document identity, parser-complete DOM checkpoints, and coalesced
  post-mutation DOM checkpoints verified across 9,006 events and 81 artifacts,
  a valid session archive, and zero network-service crashes.
- Made deterministic lifecycle validation wait for an explicit fixture-ready
  signal before moving the page into the background, preventing the hidden
  transition from racing listener registration.
- Unified protocol 0.11 page and frame identity in the canonical `frame-N`
  namespace. Main-frame records now carry equal `pageId` and `frameId`
  values, while subframe records reuse their root main frame's identity as
  `pageId`.
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
- Advanced the browser evidence protocol to version 0.9 for queue-level
  scheduler wake-up deferral evidence.
- Advanced the browser evidence protocol to version 0.10 for browser-process
  navigation and committed-document identity evidence.
- Advanced the browser evidence protocol to version 0.11 for subframe and
  non-primary page identity.
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
- Validated protocol 0.7 web-exposed idle-callback evidence end to end on the
  reference Windows platform with 629 events, 81 artifacts, one timed-out
  callback entry, one explicitly cancelled callback, and zero network-service
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

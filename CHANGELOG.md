# Changelog

## Unreleased

- Restore instrumented Chromium capture to the WPF application as a
  proof-of-concept. The recorder can select a Chromium executable and starting
  website, launch it through the authenticated evidence receiver, record its
  output with the other session channels, and filter browser evidence during
  playback.
- Keep the Chromium executable, Browse button, and starting website operable
  while recording configuration is editable. The capture checkbox controls
  whether the configured browser is launched, not whether its fields can be
  edited.

This project uses semantic version numbers for product releases. Protocol and
archive schema versions are compatibility contracts and advance independently
from the product version.

## Unreleased

### Added

- Made the bridge's protocol rejection name both versions. A mismatch was
  reported as "Recorder protocol version is not supported.", which does not say
  which version the application sent or which one the browser requires, so an
  operator could not tell which half of the pair was stale. The rejection now
  names the received version and the required version and states that the two
  were built from different revisions. The received value arrives from outside
  the process, so it is bounded to 32 characters and reduced to printable ASCII,
  and the data-handling policy records it as the single bootstrap field the
  bridge diagnostic log may contain.
- Made the browser report the evidence protocol version it was built with, and
  made the recorder refuse a mismatched pair before launching it. Started with
  `--a11y-recorder-print-protocol-version`, the browser writes
  `a11y-recorder-protocol-version=<version>` to standard output and exits with
  `0xA11C` before any window or profile work, and the recorder rejects the
  session with `instrumented-browser-protocol-mismatch` when that version
  differs from the one it speaks, naming both versions and the executable. The
  binary is asked rather than a file placed beside it, because a manifest can be
  separated from the executable it claims to describe. A browser built before
  the query existed ignores an unknown switch and would start normally, so the
  query passes `--no-startup-window`, uses a throwaway profile, is bounded by a
  timeout, and terminates the process tree on expiry; its version is then
  unknown, which is not read as agreement and does not block a session, since
  the bridge still rejects a mismatched bootstrap and names both versions.
  `integrate.py` fails the build if the patched hook does not answer the query,
  and `scripts/Test-BrowserProtocolQuery.ps1` drives a real browser to check
  that the answer, the exit code, and the absence of a started browser all hold.
- Added `scripts/Test-BridgeFailureReporting.ps1`, which provokes a bridge
  initialization failure in the instrumented browser instead of waiting for one.
  A successful recording proves nothing about failure reporting, so the check
  starts the browser twice with no pipe server: once with an unsupported
  bootstrap protocol version, which is the shape of a published application that
  is older than the browser it starts, and once with the supported version, which
  reaches the connection attempt. Both must exit with the bridge initialization
  failure code and record a reason behind the marker the recorder searches for,
  and the two reasons must differ, so a reason that identifies nothing fails the
  check. The expected exit code, marker text, and supported protocol version are
  read from the native and managed sources, so the check also fails if the two
  sides of that contract ever disagree.

### Fixed

- Made a failed browser bridge report itself. A recorder-launched browser whose
  bridge could not initialize returned `content::RESULT_CODE_NORMAL_EXIT`, so the
  recorder saw exit code 0 inside its startup window and could report only
  "exited during startup with exit code 0". A failed bridge was therefore
  indistinguishable from a browser that started and closed, and the reason was
  reachable only by preparing `A11Y_RECORDER_BRIDGE_LOG_FILE` before the run,
  which the application never did. The instrumented browser now exits with
  `kBridgeInitializationFailureExitCode`, the recorder sets a per-session bridge
  log under `diagnostics\browser-bridge.log` for every browser it launches while
  preserving a value set by a validation harness, and the launch failure names
  the bridge failure and quotes the recorded reason. When no reason was recorded,
  the message says so and gives the path rather than implying one. `integrate.py`
  migrates an already-patched checkout onto the current hook body by replacing
  the whole region the hook introduces, because matching an earlier body
  verbatim requires anticipating every shape ever written: the first attempt at
  this change carried a verbatim copy of the previous body and failed on a
  checkout patched two revisions earlier, whose body predated the bridge
  diagnostic as well as the exit code. Integration still fails if the resulting
  hook would not return the failure code.
- Recorded Chromium's role name on every protocol 0.17 accessibility node and
  moved role verification onto that field. The first reference run of the
  accessibility slice failed because the verifier searched the readable
  serialized properties for `role=button`, which Chromium never emits: its
  `AXNodeData` debug string writes the role as a bare token, as in
  `id=32 button COLLAPSED FOCUSABLE`. The assertion could not have passed on any
  build, while the underlying capture was correct. Node records now carry
  `roleName` from `ui::ToString`, the managed payload contract carries the field
  in the same change so ingest does not reject the payload, and the verifier
  requires a role name on every node. The numeric role is retained as observed
  renderer state but is not a cross-version identity, and no consumer parses a
  role out of the debug string.
- Updated the recorder's managed browser payload contracts to the protocol 0.16
  fields. Evidence ingest rejects unmapped members and the receive loop closes
  the pipe of a process whose payload is rejected, so the stale contracts cost
  the rest of each renderer's evidence: the first 0.16 capture recorded 203
  events where the comparable 0.15 capture recorded 18,097, with four rejected
  connections and 221 failed evidence writes. Ingest of every DOM payload shape
  is now covered by a platform-neutral test, since the receiver test that
  exercises a live pipe does not run on every platform.
- Made the deterministic verifier's checkpoint-to-document correlation
  independent of record arrival order. A renderer finishes parsing before the
  browser process records the commit, and the merged archive can carry either
  order, so the previous single pass failed a correct archive whenever the
  renderer won that race. Correlation now resolves against every committed
  document identity, which keeps the process-mismatch and ambiguity checks
  without depending on a timing artifact.

### Changed

- Reversed the join between an attribute or character-data transition and the
  DOM checkpoint around it, as protocol 0.16. A transition now carries its own
  `transitionId`, and a completed checkpoint reports the count and the first and
  last identity of the transitions it covers for that document. Protocol 0.15
  had the transition name the checkpoint its delivery pass was expected to
  produce; reference-platform validation recorded 200 of 452 transitions naming
  a checkpoint that was never produced, because a document can be created,
  mutated, and discarded before any delivery pass runs. A checkpoint can only
  name transitions that already happened, so no record references absent
  evidence, and a transition that no checkpoint covers is stated by omission.
  The archive validator rejects a half-stated coverage range, and the reference
  verifier reports how many recorded transitions no checkpoint covered.

### Added

- Added protocol 0.15 bounded attribute and character-data evidence. Renderer
  DOM checkpoints now emit the attribute state of every element they record,
  and the recorder bridge accepts attribute and character-data transitions that
  name the specific attribute or text a coalesced checkpoint cannot recover.
  Values are recorded verbatim up to a reported length limit, and every record
  states whether it was truncated so a partial observation is never mistaken
  for a complete one.
- Added strict validation of attribute and character-data evidence, including
  the change-type invariant that an added attribute has no previous value and a
  removed attribute has no current value, and the truncation invariant that a
  reported length exceeds the recorded string only when truncation is declared.
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
- Fixed native progress output appearing as a PowerShell error block in
  otherwise passing validation transcripts. Step output now merges stderr into
  the success stream and renders each record as text, so unittest progress dots
  and build-tool notices read as plain lines. A nonzero exit code still fails
  the step.
- Added a proposed
  [DOM attribute and text evidence model](docs/architecture/dom-attribute-and-text-evidence-model.md)
  for protocol 0.15. Attribute values and character data are recorded verbatim,
  bounded only by a per-record length limit that reports truncation, so observed
  DOM state can be compared against what assistive technology exposed. The
  document also specifies attribute-transition records, which survive checkpoint
  coalescing, and the deterministic validation that must assert exact string
  equality with known fixture values. Nothing is implemented, and one decision
  is recorded as open.
- Added recorder bridge signature verification to Chromium integration.
  Integration now parses the declared parameter count of every exported bridge
  entry point and fails when a hook template or an already-patched Chromium
  source calls one with a different number of arguments. Presence guards keyed
  on a symbol name cannot distinguish a superseded hook body from a current
  one, so this reports the mismatch at integration time with the file, line,
  and expected argument count instead of surfacing it as a Chromium build
  failure. Integration also fails when a superseded hook template is declared
  but never wired into an in-place upgrade.

### Changed

- Validated recorder bridge signature verification end to end on the reference
  Windows platform at revision `b7fd67b`. Integration reported no disagreement
  between any call site in the already-patched checkout and the bridge header,
  and the run verified 9,007 events and 81 artifacts, a valid session archive,
  and zero network-service crashes.
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

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

- Record worker and non-Node dispatch evidence. Protocol 0.31 records
  listener and dispatch evidence in dedicated, shared, and service worker
  global scopes, for dispatches to EventTargets that are not Nodes, for a
  window's load and pageshow dispatches with the document as target, and for
  IndexedDB's request, transaction, and database propagation. Every listener
  and dispatch record carries a `scope` naming its execution context kind,
  worker token, and global object URL, in the shape network records use, and
  a record in a worker or worklet scope names no document. The session
  validator rejects a record whose scope and document identities disagree,
  and the Blink validation adds a fixture page that exercises each path and
  joins a dedicated worker's listener records to its network records by
  token. Web Serial's dispatch path is not recorded, and worklet records are
  not validated. See
  [the worker and non-Node dispatch evidence model](docs/architecture/worker-and-non-node-dispatch-evidence-model.md).
  The Blink validation passed on Windows on September 25, 2026, at commit
  `5f1d72f`, with 29,083 validated events, 235 validated artifacts, and no
  omitted browser records. The worker fixture page recorded 38 listener
  registrations, and the verifier checked 20 of them from registration through
  invocation to dispatch completion. The load dispatch's original target was
  the document, the IndexedDB put travelled `IDBRequest`, `IDBTransaction`,
  `IDBDatabase` in one dispatch, the three workers carried distinct tokens,
  and the dedicated worker's fetch carried its listener records' token.
- Record rendered-frame correlation evidence. Protocol 0.30 follows every
  layout checkpoint with a presentation request on the compositor of the
  frame's local-root widget, and records on the `browser.presentation`
  channel whether a compositor frame carried the following commit, its frame
  sink and frame token, and the presentation time and flags viz reported for
  it, or that no frame was produced and why. Queuing the request never asks
  for a commit or a frame. WGC desktop frames now record, per monitor, the
  time the Windows compositor rendered the copied image and the time the
  recorder dequeued it, on the session clock. The Blink verifier checks the
  joins, outcomes, and frame-token order, and requires the interaction
  fixture's held-focus layout checkpoint to be presented; the app-session
  verifier checks composition times against dequeue times, requires a
  candidate captured frame for at least one presented checkpoint, and reports
  the measured distributions. See
  [the rendered-frame correlation evidence model](docs/architecture/rendered-frame-correlation-evidence-model.md).
  The Blink validation passed on Windows on September 25, 2026, at commit
  `712f4c4`, with 67,527 validated events, 186 validated artifacts, no omitted
  browser records, and 37 presentation requests, all queued: 35 presented,
  none with the failure flag, 2 broken, none unresolved, across 6 frame sinks.
  The fixture's held-focus checkpoint was presented 17.8 ms after its swap,
  flagged `vsync`. The application-launched session run passed the same day
  at the same commit, with 30,544 validated events and 46 validated
  artifacts. Of its 12 presented checkpoints, 11 had a candidate captured
  frame, 83 to 183 ms after presentation (median 167 ms). The 44 captured
  images, on one monitor, were composed 350 to 768 ms before the capture
  poll (median 370 ms), and none needed more than one dequeue attempt.
- Record interaction state at each checkpoint. Protocol 0.29 follows every DOM
  checkpoint and every layout checkpoint with a snapshot on the
  `browser.interaction` channel of whether the document has focus, the element
  Blink holds as focused, whether it matches `:focus-visible`, its active
  descendant, how focus last moved, the frame selection, and the value and
  selection of each text control, including controls in shadow trees. Values
  are bounded to 4096 UTF-16 code units and the traversal to 512 controls. The
  snapshot never forces style or layout. The Blink validation fixture holds
  focus on a listbox across a rendering update, and the verifier checks the
  snapshots of its parsed and final states and the completeness and source
  correlation of every snapshot in the capture. The Blink validation passed on
  Windows on September 25, 2026, at commit `1ca3cd0`, with 67,720 validated
  events, 186 validated artifacts, no omitted browser records, and 134
  interaction snapshots, 15 of them for the interaction fixture document.
- Validate a session started from the recorder application.
  `scripts/Run-AppSessionValidation.ps1` starts the application, records a
  fixture page with the application's own Chromium launch, injects marked
  Windows mouse and keyboard input, and stops and reloads the session through
  the application. `scripts/Verify-AppSessionEvidence.ps1` checks that the
  raw input, browser dispatch, focus, text edit, UI Automation,
  foreground-window, and desktop-frame records of that input agree, and
  reports the measured delays between them.
  The run passed on Windows on September 25, 2026, at commit `afaf0c9`, with
  31,010 validated events, no refused events, and no omitted browser records.
  It passed again at commit `0d55eee` with a protocol 0.29 browser build, with
  31,693 validated events, no refused events, and no omitted browser records.
  The session held 56 interaction snapshots, one for each of its 41 DOM
  checkpoints and 15 layout checkpoints.
- Record shadow DOM and pseudo-element content. Protocol 0.28 extends DOM
  checkpoints to the composed tree: every open, closed, and user-agent shadow
  root is recorded as a node under its host, with its mode, focus delegation,
  slot assignment mode, and other options, and every slot is recorded with its
  assigned nodes as Blink currently holds them and whether that assignment is
  current. Layout checkpoints record elements and laid-out text inside shadow
  trees with the host and mode of their tree, and every pseudo-element Blink
  has created, such as `::before`, `::after`, and `::marker`, with its
  geometry, computed styles, and generated text up to 4096 characters. Each
  dispatch path entry records its tree scope, retargeted target and related
  target, and the path indexes `composedPath()` returns to a listener there.
  Nothing is recalculated or created for recording. The validation run adds a
  shadow DOM fixture page, and the verifier requires the shadow roots, slot
  assignments, pseudo-elements, shadow-tree layout nodes, and per-scope path
  views it holds. The default capture duration is 35 seconds, up from 25, to
  cover the added page.
- Record WebSocket, EventSource, and WebTransport channels on the
  `browser.network` channel. Protocol 0.27 records each WebSocket's creation,
  handshake request and response, messages sent and received, close request,
  failure, and closure; each EventSource event; and each WebTransport session's
  creation, establishment, close request, and closure. Handshake cookies are
  listed by name. Message text, event data, and close reasons are kept up to
  4096 UTF-16 code units, with JSON Web Tokens, HTTP authentication credentials,
  and the values of credential-named fields replaced at source by a
  `[withheld]` marker whose offset and reason are recorded. Binary message
  content is not recorded. In a recording browser, the network service reports
  WebSocket handshake cookie headers to the renderer with every value replaced,
  so the names are available and no value leaves the network service. The
  validation run extends the network fixture page with a WebSocket exchange,
  an event stream, and a WebTransport session closed while connecting, and the
  verifier requires linked records for each and no generated credential value
  anywhere in the session.
- Record network metadata on a new `browser.network` channel. Protocol 0.26
  records each renderer request and redirect, response, finish, failure, and
  memory cache use, with the initiating document or worker and script; the
  request and response headers the network service reports on the wire, with
  the cookies attached or set listed by name; and each finished navigation's
  redirect chain, headers, response head, and timing. No body is recorded.
  Header values are kept except those of cookie and authorization headers and
  of headers whose name or value marks them as a credential, which are
  withheld at source with the reason recorded. While the recorder is connected,
  requests without a DevTools request identifier are given one so that wire
  headers are reported. The validation run serves a fixture page that makes
  credential-bearing, redirected, failing, cached, and worker requests, and the
  verifier requires linked records for each and no credential value anywhere
  in the session.
- Record layout geometry and computed styles on a new `browser.layout`
  channel. Protocol 0.25 records a checkpoint after every rendering update in
  which Blink resolved element style or performed layout for a document, with
  the viewport size, scroll offset, device pixel ratio, and zoom, and, for each
  element and laid-out text node up to 100000 nodes, whether it has a layout
  object, whether a display lock prevents its layout, its viewport-relative
  bounding rectangle, and, for elements, the resolved values of 283 defined
  computed-style properties. The hook reads only what Blink has already
  computed. The validation run serves a fixture page that widens a box and
  changes its color in a foreground tab, and the verifier requires complete,
  linked checkpoints whose values agree with what the page reported.
- Record focus, selection, active descendant, and text-editing changes on a new
  `browser.interaction` channel. Protocol 0.24 records each focus change with
  the previous, requested, and resulting focused nodes, an outcome derived from
  them, the focus type and trigger, and the active descendant the focused
  element resolved to; each selection a frame commits, with the control's own
  offsets when the selection is in a text control; text-control values after a
  value set or a user edit, bounded to 4096 UTF-16 code units; and elements
  assigned as an active descendant through element reflection. Changes made by
  script report the script's source location and JavaScript world. The
  validation run serves a fixture page that makes each of these changes by
  script and by DevTools input, and the verifier requires a record for each.
- Record cookie operations on a new `browser.cookie` channel. Protocol 0.23
  records `document.cookie` reads and writes with their outcome and the names
  read or the name and attributes written, Cookie Store API requests paired
  with their results by request identifier, Cookie Store change deliveries,
  and the cookie accesses the browser process observes for frames and
  navigations, including Set-Cookie response headers, with each cookie's
  attributes and Chromium's inclusion reasons. Script calls report the source
  location and JavaScript world of the call. Cookie values are never recorded.
  The validation run serves a loopback HTTP fixture page that uses each of
  these paths, and the verifier requires a record for each and requires that no
  record contains the fixture's cookie value.
- Report browser evidence that was lost instead of leaving a gap in the archive.
  Protocol 0.22 carries a `collector-omission` record on any browser channel,
  with the reason records were lost, how many were lost, and the browser process
  context when the reporter knows which process lost them. A renderer whose own
  pipe write failed holds the count per channel and states it on that channel as
  soon as a write succeeds again, where before the loss appeared only as a line
  in the bridge log. A record the recorder's event sink refused is stated the
  same way once the sink accepts records again, where before it was visible only
  as degraded collector health, which the archive does not carry. A loss a
  process never gets to report, because it is shutting down or its pipe never
  recovers, still goes unstated in the archive, which no reporter inside that
  process can fix.

### Changed

- Show when the app is busy. Starting a recording, stopping and verifying it,
  and loading or validating a recording for playback now set the wait cursor,
  show a working indicator with text in the player status bar, and raise UI
  Automation notification events, so a screen reader announces that the task
  has started and asks the user to wait, then announces the outcome. The
  indicator animates only when Windows animations are on. Error dialogs appear
  after the wait cursor clears. Seeking is not covered, because its delay is a
  blocking timeline redraw that needs to be made fast instead.
- Read the event log once when a recording stops and once when a recording is
  opened. Stopping previously parsed every record to validate the archive and
  then parsed every record again to load the player, and opening a recording
  with validation did the same. A `SessionPlaybackArchiveBuilder` now receives
  each record the validator parses, and the player uses the archive it builds.
  The app asks the coordinator to prepare playback when it stops a recording;
  the capture host does not, so headless runs do not hold the playback
  archive in memory. A record the validator cannot parse makes the prepared
  archive fail to build, as the reader fails on that record, and the app then
  loads the archive itself and reports the failure. Validation outcomes,
  reports, and the loaded archive are unchanged. On the 46 MB protocol 0.31
  validation event log, the two reads took 1.65 s and the single read 1.07 s
  on a warm Linux run.
- Hash each session artifact once at finalization. The recorder previously
  hashed every artifact when writing the terminal manifest, then reread and
  hashed every artifact again when validating it, and hashed every artifact
  twice more when validation failed and the manifest was rewritten and
  validated again. Finalization now builds the artifact inventory once, reuses
  it for any rewrite, and validates with hash verification skipped, because
  the manifest hashes were computed from the same files moments earlier.
  Skipped verification still checks each artifact's path, presence, size, and
  hash format. Opening a recording in the player still rereads every artifact
  and verifies its hash. Validator 1.3 reports `artifactHashesVerified` in
  `diagnostics/archive-validation.json`, and a report written at finalization
  states `false`. A recording with a 920 MB event log and 273 MB of frames
  previously read about 1.2 GB twice for hashing when stopped.
- State UI Automation queue loss per episode, protect focus changes and
  automation events from floods, and read element properties with each
  event. A protocol 0.30 run with Microsoft Solitaire open received up to
  1,588 UI Automation observations per second, mostly name changes on its
  text elements. The 4,096-slot queue refused 3,226 observations, including
  a focus change the application session verifier requires, and the archive
  stated the loss only as one total at stop. The collector now writes one
  `collector-omission` record for each run of refused observations, timed at
  the last refusal, with `firstDroppedAtNanoseconds`,
  `lastDroppedAtNanoseconds`, and `droppedByObservationType`. It keeps 512
  slots that only focus changes and automation events may use. It subscribes
  with a cache request, so UI Automation supplies the thirteen recorded
  element properties with each event instead of the processor reading them
  one call at a time, and each element snapshot states `propertySource` as
  `event-cache` or `current-read`. The validator accepts the earlier total
  form and checks the new fields for consistency. The application session
  validation now starts a UI Automation load source that raises 2,000 name
  changes per second on text elements while the recording runs, and fails if
  its mean rate is below 1,500 per second, since an application on the
  desktop floods UI Automation only in some states. The first run under that
  load raised 18,049 name changes at a mean of 2,003.7 per second; the
  recorder received every one raised while its collector ran, with no drop
  episode and every observation read from the event cache. See
  `docs/architecture/uia-overload-evidence.md`.
- Copy the newest arrived Windows Graphics Capture frame instead of the
  oldest queued one. The protocol 0.30 application-launched session run
  measured every copied image as 350 to 768 ms old at the poll (median
  370 ms, at 5 frames per second), consistent with a two-buffer pool that
  stays full between polls. The recorder now takes each frame as it arrives,
  keeps only the newest, and releases the rest, on a pool of three buffers.
  When no frame arrived since the previous poll it copies the previous image
  again with its original timing. Desktop frame records state
  `frameSelection` as `newest-arrived`, and each monitor image states how
  many arrived frames were released before it and whether it was reused. The
  archive validator accepts earlier archives without these fields. The
  application-launched session verifier checks per-monitor image order and
  reuse, and reports image age separately for new and reused images. See
  [the WGC newest-frame selection note](docs/architecture/wgc-newest-frame-selection.md).
  Stop rejecting a monitor image whose composition time is later than its
  dequeue time. The first Windows run failed on that protocol 0.30 rule: every
  image's `SystemRelativeTime` was 12.2 to 15.5 ms after the recorder took the
  frame, on a 60 Hz grid, with an exact clock conversion. The
  `desktop-monitor-frame-composed-after-dequeue` error code is retired; both
  times are still recorded. Validated on Windows at `9ab8c5f`: all 43 images
  were new, `capturedAt` minus composition time fell from a median of 370 ms
  to -7 ms (range -17.7 to 4.2 ms), 14 of 14 presented checkpoints had a
  candidate image, and the median candidate lag after presentation fell from
  167 ms to 117 ms.
- Schedule the validation fixture's page-lifecycle timers from the harness
  instead of at page load. A page's visibility while it loads depends on when
  Chromium shows its window, so the fixture used to record its lifecycle
  schedule in whichever visibility state the desktop happened to be in, and a
  run whose window was occluded during load failed in the verifier with a
  message about a timeout that was not scheduled while the fixture was visible.
  The fixture now exposes a scheduling function, and the validation script
  activates the fixture target, raises it, requires the page's own reported
  visibility, schedules the timers, and only then hides the page. The schedule
  is recorded while visible and the callbacks enter while hidden by
  construction. A page that cannot be made visible now fails immediately with a
  message naming the window state as the cause. The default capture duration is
  25 seconds so a slow launch still leaves room for the lifecycle phase.
- Validate the payloads on the `browser.lifecycle` and `browser.accessibility`
  channels. Both channels carried records the archive validator did not check,
  so a malformed payload on either one passed validation silently. The two
  lifecycle payloads and the three accessibility checkpoint payloads are now
  closed shapes, an accessibility checkpoint must name a renderer process and
  the Chromium document token it serialized, and a `collector-omission` record
  on either channel is checked like the omissions on the other browser channels.
  The validator states only what the bridge and the receiver already guarantee,
  because a validator stricter than its emitters would fail whole runs.
- Correct the documented account of a rejected browser connection. A failed
  authentication or handshake writes no lifecycle record. The receiver states
  the rejection as a `collector-omission` record with the reason
  `browser-connection-rejected` on the `browser.listener` channel, which is what
  the code does and what the archive contains.
- Fail a reference validation run that lost evidence. The run reports
  `OMITTED_EVIDENCE_RECORDS`, `SINK_REFUSED_EVENTS`, and
  `OTHER_OMISSION_RECORDS`, and a lost record in either of the first two fails
  the run, because a reference run must be lossless. An omission that does not
  report a lost record, such as a rejected connection, is reported without
  failing the run. The verifier reports `EvidenceOmissionRecords`,
  `OmittedEvidenceRecords`, and `EvidenceOmissionReasons` without failing,
  because a stated omission is a true account of what happened.
- Stop printing a capture-job error record that reports nothing, including the
  one that carries its own category line as its message. A blank line the
  browser writes to standard error becomes a native-command error record, and
  because an error record cannot carry an empty message the remoting wrapper
  substitutes the record's category line, so the record arrives with the message
  `NotSpecified: (:String) [], RemoteException`. The earlier suppression tested
  for a blank message and therefore printed this record, which reads like a
  failure in a run that passed. A record whose message is blank, or is its own
  category line, is now counted and the count is reported.
- Stop printing a capture-job error record that carries no message. Such a
  record renders as a bare category line, which reads like a failure in a run
  that passed and made a real failure harder to see. A record with no message is
  counted and the count is reported, so nothing is dropped without being stated.
- Account for every recorded DOM transition in the reference verifier instead of
  reporting a bare uncovered count. Each uncovered transition is classified as
  one in a document that completed no delivery pass, or one recorded after the
  last transition its own document's passes claimed, and the two classes are
  required to sum to the reported total. A transition inside the span its
  document already claimed is a hole in the coverage, and two passes claiming
  one transition are an overlap, and both now fail a run. A run reports
  `CoveredTransitions`, `UncoveredTransitionsWithoutPass`,
  `UncoveredTransitionsAfterLastPass`, and `UncoveredTransitionDocuments`. A
  reference run measured 448 recorded transitions with 248 covered and all 200
  uncovered transitions in the three documents that completed no pass, which
  confirms by measurement the explanation previously given in prose.
- Read the background DevTools target the same way as the target list. Windows
  PowerShell 5.1 does not enumerate a JSON array from `Invoke-RestMethod`, and
  the created target identifier is now required to be a scalar string, so a
  response shape other than a single target fails where it happens instead of
  activating a target that does not exist.

### Fixed

- Keep the UI Automation collector running when an event's element is gone
  before its cached properties are read. The cached read did not catch
  `ElementNotAvailableException`, so one such event ended the record stream
  and failed the session; a Blink validation run on September 25, 2026 at
  commit `dabe8ff` recorded no UI Automation events after 5.75 seconds of a
  48.8 second session. The element is now read again, and the record states
  `element-not-available` when that read also fails.
- Include `browser.accessibility` records in the playback Browser filter.
  They were shown only under Other.

- Wait for a free recorder pipe instance instead of failing a process that found
  the pipe busy. The recorder keeps one pending pipe instance at a time, so a
  process that opened the pipe while another process was being accepted was told
  the pipe was busy, and a single open attempt gave up: the bridge hook fails a
  process whose initialization failed, so that renderer exited, its evidence was
  absent from the archive, and nothing else in the session recorded that it had
  existed. A reference run recorded exactly this, with one renderer reporting
  that it could not connect while every sibling connected. A process now waits
  for a free instance until a 15 second deadline expires, retrying after both a
  busy pipe and a pipe that is momentarily absent, and reports the Windows error
  and the time waited when it gives up, because the previous message could not
  distinguish contention from a pipe the caller may never open. A connection
  that had to wait is recorded in the bridge startup log, and validation now
  fails when any process reports a bridge initialization or connection failure,
  which the archive validator and the deterministic verifier cannot detect
  because a process that never connected leaves no record to check.
- Stamp a collector's closing records with the session clock read when they are
  emitted rather than with the stop boundary captured before the collector's
  queued evidence was drained. A validation run whose UI Automation observation
  queue overflowed dropped 1,062 observations and reported the omission at
  23,439,242,300 ns, 0.74 ms before the last drained observation it was written
  after, so archive validation rejected the session with `event-time-regressed`,
  the session status became `failed`, and the capture host exited with code 3
  after a complete capture of 42,949 records. The stop boundary is retained as a
  floor, so a closing record can never precede the boundary or the evidence that
  preceded it, and the rule is shared by the UI Automation, foreground-window,
  desktop-frame, and audio collectors instead of restated at each call site. The
  audio collector already read the clock at emission time and now uses the
  shared rule.

### Added

- Recorded the JavaScript world each listener callback belongs to, as protocol
  0.21. Each `browser.listener` record carries a `world` with the world kind,
  Blink's numeric world identifier, and the human readable name and stable
  identifier Blink holds for a world other than the main world, and repeats the
  world as `executionWorldId` on the record's context. The archive previously
  reported no world on any record, so a listener an extension or the inspector
  registered from an isolated world could not be told apart from one the page
  registered itself. The world is read from the callback object rather than from
  the world current at the hook, so it is the world the registration was made
  from. A listener Blink installed itself is not script based and belongs to no
  world, which is reported as a null world rather than as the main world. Blink
  holds a name and a stable identifier only for a world other than the main
  world, so both are null for a main-world registration. A world type the
  recorder does not name is reported as `other`, and a record whose world and
  context disagree fails archive validation. A page's own script always runs in
  the main world, so validation now creates a world over the DevTools endpoint
  and registers a listener in it, and the evidence verifier requires that
  registration to report an inspector isolated world whose identity its context
  repeats. Validation recorded that world with the identifier 536870914, far
  above the main world's 0 and above the range Blink uses for embedder isolated
  worlds, so a consumer must treat a world identifier as an opaque number for
  grouping rather than as a small index.
- Recorded how each listener entered Blink's listener map, as protocol 0.19.
- Recorded where every listener registration, removal, and callback
  replacement came from, as protocol 0.20. Each `browser.listener` record
  carries the script URL, script identifier, line, column, and function name
  Blink reports at the hook, with null for any fact Blink did not observe.
  Blink accepts an `addEventListener` call, an inline `on*` content attribute,
  and an `on*` property assignment through one internal registration path, so
  every registration was previously reported as `add-event-listener` and the
  archive could not distinguish a handler written in markup from one added by
  script. The form is now read from the listener object Blink created rather
  than from the call site, and a registration reports `add-event-listener`,
  `inline-attribute`, or `event-handler-property`. A form outside the schema is
  normalized to `add-event-listener`, because an out-of-schema value would fail
  archive validation for the whole session rather than for one record.
- Recorded a replaced listener callback as `listener-callback-replaced`.
  Assigning an `on*` property over a registration an inline attribute or an
  earlier assignment established makes Blink swap that registration's callback
  and return, so neither the add hook nor the remove hook runs and the archive
  would keep reporting the form of a callback Blink no longer holds. The record
  carries the unchanged listener identity together with the form of the callback
  Blink now holds. The deterministic Blink fixture exercises an inline `on*`
  content attribute, an `on*` property assignment, and a reassignment over an
  existing attribute registration.
- Recorded listener and dispatch evidence for EventTargets that are not Nodes,
  beginning with the window, as protocol 0.18. The Blink listener hooks
  previously recorded a registration, removal, or invocation only when the
  EventTarget was a Node, so a `window.addEventListener` call produced no
  evidence at all and a composed path stopped one entry short of where Blink's
  own path ends. Every listener and dispatch target reference now reports a
  `kind` of `node`, `window`, or `other`, the Blink interface name the target
  reports for itself, recorded as observed rather than as a cross-version
  identity because that token has changed between Chromium revisions, and a
  process-local `targetId` for a target that is not a Node, and reports a null `nodeId` where no DOM node exists. The window entry
  in a composed path is taken from Blink's own `WindowEventContext`, which is
  present exactly when Blink will run window listeners for that event, so the
  recorded path ends where Blink's does rather than where the recorder guesses.
  A target identifier is minted from the address Blink uses for that target in
  that renderer process; it is valid for the lifetime of that process and must
  never be compared across processes. The archive validator enforces the
  identity rule for each kind instead of inferring it, and the deterministic
  fixture now registers a window `click` listener plus a window `resize`
  listener it then removes. Worker global scopes, inline attributes,
  event-handler properties, isolated-world identity, source location, and
  dispatches whose original target is never a Node, such as `XMLHttpRequest`
  progress events, remain outstanding.
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

### Changed

- Upgraded a listener hook in an existing Chromium checkout by rewriting the
  region the hook introduced instead of matching a remembered copy of its text.
  The integration script located the innermost block enclosing the single bridge
  call and replaced it, so a checkout holding a body no revision of the script
  records is still upgraded and the script no longer has to carry a copy of
  every body it has ever written. A block containing another bridge call, or one
  that opens on an upstream Chromium function signature, is refused rather than
  replaced.

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

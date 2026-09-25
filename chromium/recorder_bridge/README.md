# Chromium Recorder Bridge

This directory is copied into the Chromium source checkout as
`//chromium/recorder_bridge`. It mirrors version `0.30` of the recorder-side
protocol implemented by `Recorder.Collectors.Browser`.

Run the integration and build from a Windows PowerShell prompt:

```powershell
.\chromium\setup-windows.ps1
```

The integration script copies this directory into the checkout, adds it to the
`chrome_main_delegate` and Windows `content/browser` targets, and initializes
the connection from `ChromeMainDelegate::BasicStartupComplete`. The browser
process completes its recorder connection before normal startup can launch
renderer processes. Child bootstrap attachment runs in Chromium's shared
`LaunchOnLauncherThread` sequence immediately before platform-specific launch
preparation. The integration script removes the obsolete Windows-local hook
when updating an existing checkout. The instrumented browser:

1. Recognizes `--a11y-recorder-bootstrap=stdin`.
2. Reads the one-line secret bootstrap from inherited standard input.
3. Authenticates to the recorder and completes clock synchronization.
4. Copies the child bootstrap into a browser-owned read-only shared-memory
   region. It publishes only Chromium's opaque serialized handle metadata to
   the browser process environment so the child-launch hook can cross linked
   module boundaries without relying on duplicated static storage.
5. Starts renderer process bridge connections using the inherited capability.
   GPU and utility processes are deliberately excluded because the current
   evidence hooks run only in Blink renderers. The network service utility
   process receives only the non-secret
   `--a11y-recorder-recording-network-service` switch, which tells it to report
   WebSocket handshake cookie headers by name, and no capability. Renderer command lines contain
   only shared-memory handle metadata and the non-secret Chromium child process
   identifier. The internal metadata environment marker is removed from each
   child environment.
6. Authenticates and synchronizes each participating process independently.
7. Must route browser and child-process evidence through bounded, non-blocking
   queues to `RecorderPipeClient`.
8. Must report queue overflow and disconnected intervals as omission records.

The current code implements and integrates the browser-process bootstrap,
child-process capability distribution, per-process authentication and clock
synchronization, framing, and evidence serialization. Blink hooks record Node
listener lifecycles, dispatch lifecycles, ordered Node event paths, listener
phases and current targets, Node default-event-handler decisions, window
`setTimeout`, `setInterval`, web-exposed `requestAnimationFrame` and
`requestIdleCallback` lifecycles, page-lifecycle state at callback boundaries,
and authoritative task-queue wake-up deferral decisions.
The browser process also records primary-main-frame navigation starts and
completions with stable page, frame, navigation, and committed-document
identifiers.
Protocol 0.11 extends those navigation records to subframes and non-primary
main-frame types, including primary-page membership, root page identity, and
direct parent or outer-document relationships.
Protocol 0.12 adds bounded parser-complete DOM structural checkpoints.
Protocol 0.13 adds coalesced post-mutation checkpoints for structural
child-list changes. The renderer streams node and parent identities, node
types, and node names, then reports the observed node count and whether the
512-node limit truncated the checkpoint. Text content and attributes are not
recorded.
Protocol 0.14 records Chromium's document token in committed browser
navigations and renderer DOM checkpoints, together with the renderer process
ID on committed navigation records. Consumers join the two document identity
namespaces only when browser instance, token, and renderer process all match.

Protocol 0.15 adds bounded attribute and character-data evidence. Checkpoints
emit the attribute state of every element node they record, and accepted
attribute and character-data mutations are recorded as transitions. Values are
recorded verbatim up to 4096 UTF-16 code units, with the full length and an
explicit truncation flag on every value, and at most 64 attributes per node.
An attribute or text mutation queues its document for a checkpoint.

Protocol 0.16 changes how a transition and a checkpoint are related. A
transition carries its own `transitionId`, and a completed checkpoint reports
the count and the first and last identity of the transitions it covers for that
document. Protocol 0.15 had the transition name the checkpoint its delivery pass
was expected to produce, which validation showed can name a checkpoint that is
never produced.

Protocol 0.17 records renderer accessibility serialization batches before
Chromium sends them to the browser process. Each batch has start, node, and
completion records with the document token, update and event counts, a
100,000-node limit, and explicit truncation. Node records include AX and DOM
identities, the numeric role and Chromium's role name for it, name, description,
whether the node identity matches the focus identity when its serialized update
carries tree data, and Chromium's readable serialized properties. Consumers
identify a role by the role name, because numeric role ordinals shift between
Chromium versions and the serialized properties are a debug representation
rather than a field contract. AX identity zero is invalid, while negative AX
identities are retained for Chromium-generated renderer nodes. These batches
are incremental updates, not complete accessibility-tree snapshots.

Protocol 0.18 replaces the node reference in listener and dispatch records with
an event-target reference that also describes EventTargets that are not Nodes.
Each reference reports a `kind` of `node`, `window`, or `other`, the Blink
interface name the target reports for itself, and, for a target that is not a
Node, a process-local `targetId` minted from the address Blink uses for that
target. The interface name is Blink's own token for that build and is not a
cross-version identity, so a consumer identifies a target by its kind. A Node keeps its `nodeId`; a target that is not a Node reports null
there. Window listener registrations, removals, invocations, and the window
entry at the end of a composed path are recorded from Blink's own
`WindowEventContext`, so a recorded path ends where Blink's path ends. Worker
global scopes are not covered. A target identifier is process-local and must
never be compared across processes.

Protocol 0.19 reports how each listener entered Blink's listener map.
`registrationKind` is `add-event-listener`, `inline-attribute`, or
`event-handler-property`, and the value is read from the listener object Blink
created rather than from the call site, because an `addEventListener` call, an
inline `on*` content attribute, and an `on*` property assignment all reach the
same internal registration path. A listener whose object is a content-attribute
event handler is an inline attribute, any other event handler came from a
property assignment, and a listener that is neither arrived through
`addEventListener`. Assigning an `on*` property over an existing attribute
registration makes Blink swap that registration's callback and return, without
adding or removing a listener, so a `listener-callback-replaced` record on
`browser.listener` reports the unchanged listener identity together with the form
of the callback Blink now holds. Without that record the archive would keep
describing a callback Blink no longer holds. A value outside the schema is
normalized to `add-event-listener` rather than written through, because an
out-of-schema value fails archive validation for the whole session.

Protocol 0.20 reports where each listener record came from. Every
`browser.listener` record carries a `location` with the script URL, script
identifier, line, column, and enclosing function name that Blink's capture helper
reports at the hook, and a null `sourceHash`, because the recorder does not read
script text. The location describes the call that registered, removed, or
replaced the listener, not the definition site of the callback. Blink represents
an unobserved URL as an empty string and an unobserved identifier, line, or
column as zero; each is carried through as null, and a record with nothing
observed in any field reports a null location rather than an object of nulls. A
registration made while no script was running still reports a location when
Blink can supply a parsing position.
Capturing a location walks the top of the JavaScript stack on every listener
record, which is a cost this build accepts for evidence completeness.

Protocol 0.21 reports the world each listener callback belongs to. Every
`browser.listener` record carries a `world` with `kind`, `blinkWorldId`, `name`,
and `stableId`, and repeats the world as `executionWorldId` on the record's
context. The world is read from the callback through
`JSBasedEventListener::GetWorldForInspector`, so it is the world the registration
was made from rather than the world current when the record was written. A
listener that is not script based, such as one Blink installed itself, belongs to
no world and reports a null world rather than the main world. Blink holds a human
readable name and a stable identifier only for a world other than the main world,
so both are null for a main-world registration. A world type the recorder does
not name is reported as `other` rather than as one it is not.

Timer evidence covers accepted scheduling, callback entry, and explicit
`clearTimeout`, `clearInterval`, or `cancelAnimationFrame` cancellation. It
does not claim callback completion, page effects, frame presentation, source
location, worker timers, or task-to-timer scheduler correlation. Timer
`throttled` values remain null. Queue-level deferral evidence is emitted
separately on `browser.scheduler`.

For an explicit local diagnostic run, set
`A11Y_RECORDER_CHROMIUM_LOG_FILE` to an absolute file path before starting the
capture host. The launcher then adds Chromium's `--enable-logging` and
`--log-file` switches. This option is disabled by default because Chromium logs
can contain tested URLs and other local browsing details. Diagnostic logs are
not session evidence and must not contain the recorder authentication token.

A browser process whose bridge cannot initialize exits with
`kBridgeInitializationFailureExitCode`, declared in `recorder_switches.h` as
`0xA11B`. The value sits outside Chromium's own result-code range so the
recorder can tell a failed bridge from a browser that exited normally. Reporting
this failure as a normal exit code is a defect: the recorder observes only the
exit code within its startup window, so a normal code makes a failed session
indistinguishable from one the operator closed. Because the hook is inserted
only when absent, an already-patched checkout keeps whatever body its revision
wrote, so `integrate.py` migrates the hook by replacing the whole region it
introduces rather than by matching any earlier body verbatim. Integration still
fails if the resulting hook would not return the failure code, which is a
backstop rather than the mechanism.

Failure reporting is checked by provoking it. `scripts/Test-BridgeFailureReporting.ps1`
starts the instrumented browser with no recorder pipe server, once with an
unsupported bootstrap protocol version and once with the supported version, and
requires both runs to exit with the failure code, to record a reason behind the
marker the recorder searches for, and to record different reasons from each
other. The script reads the expected exit code, marker text, and supported
protocol version from the native and managed sources, so it fails if those
constants ever diverge. A successful recording does not exercise any of this,
which is why the check does not depend on one.

The browser also reports which protocol version it was built with, so a
mismatched pair can be refused before anything is recorded. Started with
`--a11y-recorder-print-protocol-version`, the browser writes
`a11y-recorder-protocol-version=<version>` to standard output and exits with
`kProtocolVersionQueryExitCode`, declared in `recorder_switches.h` as `0xA11C`,
before any window or profile work. The recorder runs this query before every
launch and refuses to start a session when the reported version differs from
`BrowserEvidenceProtocol.CurrentVersion`, naming both versions and the
executable path.

The binary is asked rather than a file placed beside it, because a manifest can
be copied, kept, or moved apart from the executable it claims to describe, while
an answer from the process cannot be about a different binary. A browser built
before the query existed treats the switch as unknown and starts normally, so the
query always passes `--no-startup-window`, uses a throwaway profile directory, is
bounded by a timeout, and terminates the process tree if that timeout expires.
Such a browser leaves its version unknown, which is not treated as agreement and
not treated as a refusal: the bridge still rejects a mismatched bootstrap and
names both versions. `integrate.py` fails the build if the patched hook does not
answer the query, and `scripts/Test-BrowserProtocolQuery.ps1` drives a real
browser to check that it answers with the declared version, exits with the query
exit code, and leaves no browser running.

For failures that occur before Chromium logging is initialized, set
`A11Y_RECORDER_BRIDGE_LOG_FILE` to an absolute file path. The recorder sets this
variable for every browser it launches, defaulting to
`diagnostics\browser-bridge.log` inside the session directory, and reports the
recorded reason in its launch failure message. An environment value set by a
validation harness is preserved. The native bridge
appends only process identifiers, monotonic tick values, bootstrap attachment
stages, child process types, and internal error text. It never writes bootstrap
contents, pipe names, authentication tokens, command lines, URLs, or page data,
with one exception: a rejected protocol version is named in the rejection, next
to the version this browser requires, because either half of the pair may be the
stale one and the reason is useless without saying which. The reported value is
bounded to 32 characters and reduced to printable ASCII, since it arrives from
outside the process.
Sandboxed Chromium children cannot open this diagnostic path directly. Use
`A11Y_RECORDER_CHROMIUM_LOG_FILE` when child startup errors are required;
Chromium passes that log to sandboxed children through an inherited handle,
and the bridge can write early initialization diagnostics to that handle before
Chromium's logging backend starts.

For each accepted connection, the recorder persists two records on the
`browser.lifecycle` channel:

1. `browser-connected` after the hello message and authentication token have
   been validated.
2. `browser-clock-synchronized` after the clock exchange has completed and the
   recorder has sent the ready message.

The connection record contains protocol, browser-instance, process, and
Chromium-version metadata. Child records also contain the browser OS process ID
and Chromium child process ID. No lifecycle record contains the authentication
token. The clock record contains the mapping identifier, Chromium monotonic
frequency, and estimated uncertainty in nanoseconds. A failed authentication or
handshake produces no lifecycle record. The receiver states the rejection as a
`collector-omission` record with the reason `browser-connection-rejected`, which
it writes on the `browser.listener` channel. Both lifecycle payloads are closed
shapes that the archive validator checks, so a record carrying a property the
receiver is not defined to send fails validation rather than entering the
archive unchecked.

Protocol 0.22 reports evidence the bridge could not write. `SendBlinkEvidence`
holds a per-channel count of records whose write failed, guarded by its own lock
because evidence is written from more than one thread, and reports that count on
the same channel as a `collector-omission` record immediately before its next
successful write. The record carries the process context, the reason
`browser-evidence-write-failed`, and the number of records lost. The diagnostic
line in the bridge log is kept, because a process that never writes again cannot
report its own loss, and that log is then the only trace. A failure to write the
omission record returns the held count unchanged, since the omission is not
itself captured evidence.

Protocol 0.23 records cookie operations on the `browser.cookie` channel, with
cookie names and never cookie values. `cookie_text.cc` holds the text readers:
`ReadCookieNames` reads the names from a `document.cookie` string,
`ReadCookieWriteRequest` reads the name and attributes from a written cookie
string, `ReadCookieNameFromSetCookieLine` reads the name from a Set-Cookie line
Chromium could not parse, and `ParseInclusionDebugString` splits Chromium's
inclusion status into included, exclusion reasons, warning reasons, and
exemption reason. Each reader copies only the name and attribute text into its
result, so no value leaves the reader. `cookie_text_test.cc` exercises them and
builds with any C++20 compiler outside Chromium; `test_integrate.py` compiles
and runs it when `g++` or `clang++` is on the path.

The Blink hooks in `cookie_jar.cc`, `document.cc`, and `cookie_store.cc` call
`RecordBlinkDocumentCookieRead`, `RecordBlinkDocumentCookieWrite`,
`RecordBlinkCookieStoreRead`, `RecordBlinkCookieStoreWrite`,
`RecordBlinkCookieStoreReadResult`, `RecordBlinkCookieStoreWriteResult`, and
`RecordBlinkCookieStoreChange`. A Cookie Store write notes its resolver with
`NoteBlinkCookieStoreWriteResolver` just before it is sent, in a per-thread
slot, so the request record and the later result record share a `requestId`.
The browser hooks in `render_frame_host_impl.cc` and `navigation_request.cc`
call `RecordBrowserFrameCookieAccess` and `RecordBrowserNavigationCookieAccess`
with the entries Chromium's `CookieAccessDetails` holds. The record types and
their limits are described in `docs/architecture/instrumented-chromium.md`.

Protocol 0.24 records interaction-state changes on the `browser.interaction`
channel. The Blink hooks in `document.cc`, `frame_selection.cc`,
`html_input_element.cc`, `text_field_input_type.cc`,
`html_text_area_element.cc`, and `element.cc` call `RecordBlinkFocusChanged`,
`RecordBlinkSelectionChanged`, `RecordBlinkTextControlValueChanged`, and
`RecordBlinkActiveDescendantReferenceSet`. The focus hook is a scope object
declared after the early checks of `Document::SetFocusedElement`, so it reports
the focused element on every later return path. The bridge derives the focus
outcome from the previous, requested, and resulting node identities, checks
every enumerated value, and drops a call it cannot represent. Script origin is
read with the same helper the cookie records use. The record types and their
limits are described in `docs/architecture/instrumented-chromium.md`.

Protocol 0.25 records layout geometry and computed styles on the
`browser.layout` channel. The Blink hook in `local_frame_view.cc` runs after
the lifecycle observers are told that a paint-clean update finished, and calls
`BeginBlinkLayoutCheckpoint`, `RecordBlinkLayoutCheckpointNode`, and
`CompleteBlinkLayoutCheckpoint` for each local frame view that is not
throttled. The bridge keeps the last style-resolution and layout counters it
saw for each document and returns no checkpoint when neither changed, so a
checkpoint is only recorded after style or layout work. It rejects
non-finite geometry, negative sizes, and a text node reported with a style.
The record types and their limits are described in
`docs/architecture/layout-and-style-checkpoint-evidence-model.md`.

Protocol 0.26 records network metadata on the `browser.network` channel. The
Blink hooks in `resource_load_observer_for_frame.cc` and
`resource_load_observer_for_worker.cc` call `RecordBlinkNetworkRequest`,
`RecordBlinkNetworkResponse`, `RecordBlinkNetworkFinished`, and
`RecordBlinkNetworkFailed`, and a hook in `resource_fetcher.cc` calls
`RecordBlinkMemoryCacheUse` through the observer for every memory cache use.
The browser hooks in `network_service_devtools_observer.cc` call
`RecordBrowserNetworkRequestHeaders` and `RecordBrowserNetworkResponseHeaders`,
and the hook after the navigation-completed record calls
`RecordBrowserNavigationResponse`. Hooks in `frame_fetch_context.cc`,
`worker_fetch_context.cc`, and `navigation_url_loader_impl.cc` test
`IsRecorderActive` and give a request without a DevTools request identifier the
one DevTools would assign, so that the network service reports its wire
headers. Every header value passes through `network_text.h`, which withholds
cookie and authorization header values and values whose header name or shape
marks them as a credential; `network_text_test.cc` covers the classifier. The
record types and their limits are described in
`docs/architecture/network-metadata-evidence-model.md`.

Protocol 0.27 records WebSocket, EventSource, and WebTransport channels on the
`browser.network` channel. Hooks in `websocket_channel_impl.cc` call
`RecordBlinkWebSocketCreated`, `RecordBlinkWebSocketHandshakeRequest`,
`RecordBlinkWebSocketHandshakeResponse`, `RecordBlinkWebSocketMessage`,
`RecordBlinkWebSocketCloseRequested`, `RecordBlinkWebSocketError`, and
`RecordBlinkWebSocketClosed`; a hook in `event_source.cc` calls
`RecordBlinkEventSourceMessage`; and hooks in `web_transport.cc` call
`RecordBlinkWebTransportCreated`, `RecordBlinkWebTransportEstablished`,
`RecordBlinkWebTransportCloseRequested`, and `RecordBlinkWebTransportClosed`.
Message text, event data, and close reasons pass through
`network_text::ReadMessageText`, which keeps up to 4096 UTF-16 code units and
replaces any part that looks like a credential with `[withheld]`. A hook in
`services/network/websocket.cc` makes the network service of a recording
browser report the WebSocket handshake `Cookie` and `Set-Cookie` headers to
the renderer with every value replaced, using the `cookie_names` source set,
which holds `cookie_text.cc` and `recorder_switches.h` and depends only on the
C++ standard library. `cookie_text_test.cc` covers reading names back from the
replaced headers. The record types and their limits are described in
`docs/architecture/realtime-network-evidence-model.md`.

Protocol 0.28 extends DOM checkpoints, layout checkpoints, and dispatch paths to
the composed tree. The DOM checkpoint helper in `document.cc` visits each
shadow root after its host and calls `RecordBlinkDomCheckpointShadowRoot` and,
for each slot, `RecordBlinkDomCheckpointSlotAssignment`. The layout checkpoint
helper in `local_frame_view.cc` visits shadow trees and the pseudo-elements
Blink has created, and passes the pseudo-element, shadow host, and shadow mode
fields on each `LayoutCheckpointNode`. The dispatch hook in
`event_dispatcher.cc` passes each path entry's tree scope root and mode,
retargeted target and related target, and visible path indexes to
`RecordBlinkDispatchPathNode` and `RecordBlinkDispatchPathWindow`. The record
types and their limits are described in
`docs/architecture/shadow-dom-evidence-model.md`.

Protocol 0.29 adds interaction-state snapshots. `RecorderRecordInteractionCheckpoint`
in `document.cc` is called by the DOM checkpoint helper after
`CompleteBlinkDomCheckpoint` and by the layout checkpoint helper in
`local_frame_view.cc` after `CompleteBlinkLayoutCheckpoint`. It calls
`BeginBlinkInteractionCheckpoint` with the document's focus and frame
selection state, `RecordBlinkInteractionCheckpointTextControl` for each text
control in composed-tree order, and `CompleteBlinkInteractionCheckpoint`. The
record types and their limits are described in
`docs/architecture/interaction-state-checkpoint-evidence-model.md`.

Protocol 0.30 adds rendered-frame correlation. The layout checkpoint helper in
`local_frame_view.cc` calls `RecorderRequestLayoutPresentation` after
`CompleteBlinkLayoutCheckpoint` and before the interaction snapshot. It
resolves the frame's local-root `WebFrameWidgetImpl` and calls the
`RecorderRequestPresentationEvidence` entry point the integration adds to that
class, which calls `BeginBlinkPresentationRequest` and, when the request is
queued, queues a `RecorderPresentationSwapPromise` on the widget's
`LayerTreeHost`. The promise calls `RecordBlinkPresentationNotSwapped`,
`RecordBlinkPresentationSwapped`, and, from a presentation callback registered
on the main thread, `RecordBlinkPresentationFeedback`. The record types are
described in
`docs/architecture/rendered-frame-correlation-evidence-model.md`.

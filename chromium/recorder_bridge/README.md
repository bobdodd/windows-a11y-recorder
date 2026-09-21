# Chromium Recorder Bridge

This directory is copied into the Chromium source checkout as
`//chromium/recorder_bridge`. It mirrors version `0.19` of the recorder-side
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
   evidence hooks run only in Blink renderers. Renderer command lines contain
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
handshake produces `browser-connection-rejected` instead of either successful
lifecycle record.

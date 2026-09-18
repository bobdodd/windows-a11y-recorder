# Instrumented Chromium Architecture

## Decision

Browser source instrumentation is a required part of Windows A11y Recorder. A browser extension, content script, CDP client, or external automation driver cannot provide the complete and authoritative handler, dispatch, default-action, and scheduling evidence required by the product.

The instrumented browser is shipped and launched as part of the same application. The participant uses it normally. The recorder does not drive it through Selenium, WebDriver, or Python.

## Required evidence

### Event listeners

Record registration and removal for:

- `addEventListener` listeners.
- Inline event-handler attributes.
- `on*` event-handler properties.
- Listeners installed in isolated execution worlds.
- Internal Blink listeners that participate in page interaction.
- Listener type, capture, passive, once, target, callback identity, execution world, and source location.

The record must identify listeners that exist even if they are not exposed by a public JavaScript inspection API.

### Event dispatch

Assign an identifier to each dispatch and record:

- Input provenance when the event originated from keyboard, mouse, touch, pen, accessibility action, or browser command.
- Original target and retargeted target.
- Composed path.
- Capture, target, and bubble phases.
- Listeners considered and invoked.
- Listener start, completion, exception, and duration.
- Calls that stop propagation or prevent the default action.
- Browser default action considered, performed, suppressed, or failed.
- Resulting focus, activation, navigation, DOM mutation, and accessibility changes.

This supports direct comparison of pointer and keyboard paths. A later analyzer can identify a pointer-triggered activation with no corresponding focusable or keyboard-triggerable path without reconstructing the answer from JavaScript source text alone.

### Time and scheduling

Record:

- Timeout and interval creation, cancellation, requested delay, effective delay, and firing.
- Animation-frame and idle-callback scheduling and execution.
- Task source, callback location, nesting, queue delay, execution duration, and throttling.
- Page visibility, lifecycle, freezing, backgrounding, and timer-clamping context.
- Browser-owned delayed actions that change the page or user experience.

The recording must preserve both requested and observed timing. Analysis can then distinguish application intent from delays introduced by the browser, system load, throttling, or recorder overhead.

### Page and accessibility state

At policy-controlled checkpoints, record:

- Original response source where available.
- Live serialized DOM.
- Shadow DOM, frame, and execution-world boundaries.
- Computed styles and layout geometry needed for later analysis.
- Browser accessibility tree and node-to-DOM mappings.
- Focus, selection, active descendant, and text-editing state.
- Rendered frame or compositor correlation identifiers.

Snapshots must carry document, frame, navigation, and checkpoint identifiers so later analysis does not combine incompatible states.

### Cookies and network

Cookie evidence includes:

- Operation: read, write, delete, send, receive, or block.
- Cookie name.
- Domain, path, SameSite, Secure, HttpOnly, partitioning, expiry class, and source API where available.
- Request, response, document, frame, and navigation correlation.
- Whether the operation succeeded or was blocked and the reason.

Cookie values are never recorded. Authorization values, saved credentials, request bodies, and response bodies are not recorded.

Network evidence includes request and response metadata, initiator, resource type, redirect chain, status, cache behavior, timing, and cookie names associated with the transaction.

## Process architecture

```text
Recorder.App.exe
  launches and supervises

Recorder.CaptureHost.exe
  owns session time and archive
  accepts authenticated browser evidence

Instrumented Chromium browser process
  distributes a per-session IPC capability
  correlates navigation, network, and child processes

Instrumented renderer processes
  observe Blink listeners, dispatch, timers, DOM, layout, and accessibility

GPU and utility processes
  provide rendering, compositor, network, and process correlation where required
```

Each producer has its own sequence and Chromium monotonic timestamp. The receiver maps that clock to the recorder session clock and records uncertainty. Cross-process relationships use stable browser instance, process, page, frame, document, navigation, dispatch, listener, timer, request, and checkpoint identifiers.

## Evidence bridge

The bridge is a private, versioned, local IPC protocol rather than a remote debugging port. Its requirements are:

- Per-session authentication established by the recorder when launching the browser.
- No listening network socket.
- Strict message size and schema limits.
- Bounded producer queues with explicit dropped-record summaries.
- Browser-process batching so instrumentation does not block input dispatch.
- Source-side removal of cookie values, authorization values, and prohibited bodies.
- Clock synchronization anchors from every Chromium process.
- Process start, restart, crash, navigation, and shutdown records.
- Graceful degradation that remains visible in the archive.

The C# contracts in `Recorder.Contracts/BrowserEvidenceContracts.cs` define the first recorder-side vocabulary. They will be mirrored by Chromium-side C++ protocol structures rather than shared through a managed runtime.

The initial implementation is in `chromium/recorder_bridge`. The recorder starts
the browser with `--a11y-recorder-bootstrap=stdin`, writes the pipe name,
protocol version, browser instance identifier, message limit, and authentication
token through redirected standard input, and then closes the stream. The token
is not placed in command-line arguments, environment variables, or a file. The
launcher also removes inherited environment variables whose names indicate
tokens, secrets, passwords, authorization data, API keys, access keys, or
private keys.

Protocol version 0.2 propagates the recorder capability from the browser to
eligible renderer, GPU, and utility processes through a browser-owned,
read-only shared-memory region. Chromium's Windows child-launch path inherits
the region handle. Because the bridge is linked into more than one Chromium
module, the browser publishes Chromium's opaque serialized handle metadata in
an internal process environment marker rather than relying on module-local
static storage. The child-launch hook reads that metadata, explicitly inherits
the handle, and removes the marker from the child environment. Child command
lines contain only the serialized handle metadata and a non-secret Chromium
child process identifier, never the pipe authentication token. Each
participating process maps the region during early startup, validates its
process type and identifiers, authenticates its own named-pipe connection, and
establishes an independent clock mapping. Lifecycle records correlate the
browser instance, OS process ID, browser OS process ID, Chromium child process
ID, and process type.

The native target depends on Chromium `//base` and must be compiled and tested
inside a Chromium source checkout. On September 18, 2026, the bridge was
compiled into Chromium 156.0.8065.0 on the reference Windows platform. The
instrumented browser then completed authentication and clock synchronization
with the recorder. The finalized session archive contained one
`browser-connected` record followed by one `browser-clock-synchronized` record,
with no rejected connection, dropped record, or archive-validation issue.
Details and reproducible commands are in the
[validation record](../validation/chromium-connection-2026-09-18.md).

Successful connections are persisted on the `browser.lifecycle` channel:

- `browser-connected` is emitted only after the hello message and
  authentication token are validated.
- `browser-clock-synchronized` is emitted only after the clock exchange
  completes and the recorder sends `ready`.
- Child-process hello messages are accepted only for renderer, GPU, and utility
  process types and must include positive browser parent and Chromium child
  process identifiers.
- Neither record contains the authentication token.
- A failed authentication or handshake produces
  `browser-connection-rejected` instead of a successful lifecycle sequence.

## Instrumentation sequence

1. Implement browser lifecycle, process identity, IPC authentication, clock mapping, and omission records.
2. Instrument listener registration and removal.
3. Instrument dispatch phases, listener invocation, propagation control, cancellation, and default actions.
4. Instrument timeout, interval, animation-frame, idle-callback, and lifecycle throttling.
5. Add document, DOM, style, layout, accessibility, and rendered-frame checkpoints.
6. Add cookie operations and network metadata with prohibited values removed at source.
7. Add browser-chrome and compositor correlation needed by test scenarios.
8. Package the browser and recorder as one installable application.

Each stage requires fixture pages and archive-level tests before the next stage begins.

## Initial fixture tests

- A `div` with only a click listener is activated by pointer and is unreachable by keyboard.
- A custom control listens for Enter but not Space.
- A keydown listener cancels the browser default action.
- A listener stops propagation before an ancestor activation handler.
- A control is removed and replaced after a timeout.
- A timeout is delayed by main-thread work.
- Backgrounding causes timer throttling before a time-sensitive prompt changes.
- A cookie is read before a consent notice is exposed.
- A cookie is written before consent interaction.
- A consent-state cookie is read to decide whether the notice should be shown.
- A listener is attached in shadow DOM or an isolated world.
- A native browser default action occurs without a page listener.

Expected records must be asserted by stable identifiers and relationships, not only by event counts.

## Maintenance rule

The Chromium fork should remain narrow. Instrumentation hooks call a small recorder-owned evidence layer and avoid unrelated product changes. Upstream revisions are integrated on a controlled cadence, and every rebase runs the fixture suite to detect moved hooks, missing paths, changed scheduling behavior, and protocol incompatibility.

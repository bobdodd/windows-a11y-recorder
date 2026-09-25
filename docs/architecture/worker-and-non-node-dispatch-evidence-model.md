# Worker and Non-Node Dispatch Evidence Model

Status: implemented as protocol 0.31 and validated on Windows on September 25,
2026, at commit `5f1d72f`.

## Purpose

Protocol 0.18 recorded listeners and dispatches for EventTargets that are not
Nodes, beginning with the window, but only where the dispatch passes through
Blink's Node event dispatcher. Three groups of listener activity were
therefore absent from the archive:

- a dispatch whose original target is not a Node, such as a
  `window.postMessage` message, an `AbortSignal` abort, a `MessagePort`
  message, or an event dispatched on a script-constructed `EventTarget`;
- the load and pageshow events a window dispatches with its document as the
  target, which fire only the window's listeners; and
- every listener and dispatch in a dedicated, shared, or service worker,
  because a worker global scope has no document and every recorded target had
  to name one.

IndexedDB propagates request events to their transaction and database through
its own dispatcher, so those dispatches were absent as well.

This slice records those dispatches and names the execution context of every
listener and dispatch record, so a worker's records can be told apart from a
window's and joined with the network records the same worker issued.

The records are evidence. The recorder does not decide whether a listener's
behaviour is accessible, and does not interpret the events it records.

## Sources

The dispatch paths below were read from the Chromium checkout the recorder
patches, at tag 156.0.8065.0. The functions named are identical on the
Chromium main branch at the time of writing:

- `third_party/blink/renderer/core/dom/events/event_target.cc`
  ([Chromium source](https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/core/dom/events/event_target.cc)):
  `EventTarget::DispatchEventInternal` sets the target, current target, and
  at-target phase, then calls `FireEventListeners`. `Node` overrides it to use
  `EventDispatcher`, and a target that does not override it, or whose override
  delegates to it, is dispatched here.
- `third_party/blink/renderer/core/frame/local_dom_window.cc`
  ([Chromium source](https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/core/frame/local_dom_window.cc)):
  `LocalDOMWindow::DispatchEvent(Event&, EventTarget*)` sets the target to the
  given target, normally the document, and the current target to the window,
  then calls `FireEventListeners` on the window alone.
- `third_party/blink/renderer/modules/indexeddb/idb_event_dispatcher.cc`
  ([Chromium source](https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/modules/indexeddb/idb_event_dispatcher.cc)):
  `IDBEventDispatcher::Dispatch` receives the targets in path order, the
  original target first, and runs a capturing, at-target, and bubbling pass
  over them. `IDBRequest`, `IDBOpenDBRequest`, and `IDBTransaction` call it for
  trusted events and otherwise delegate to `EventTarget::DispatchEventInternal`.
- `third_party/blink/renderer/modules/serial/serial_port.cc`
  ([Chromium source](https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/modules/serial/serial_port.cc)):
  `SerialPort::DispatchEventInternal` calls `FireEventListeners` on its parent
  and itself directly.
- `third_party/blink/renderer/core/dom/events/event.cc`: `Event::composedPath`
  returns only the current target for a dispatch whose current target is not a
  Node when the runtime feature `ComposedPathReturnTargetBeingDispatched` is
  enabled, and otherwise returns the current target only when it is a window.
- `third_party/blink/renderer/core/execution_context/execution_context.h` and
  `third_party/blink/renderer/core/workers/worker_or_worklet_global_scope.h`:
  the context classification predicates and the global scope's DevTools token,
  which the network hooks already record as a worker's token.

A search of the same checkout for callers of `FireEventListeners` finds, besides
the paths above, only the Node dispatcher, its window stage, and
`Node::HandleLocalEvents`, which the Node dispatcher calls. The Serial path is
the one listener path this slice does not record.

## Scope Of Each Record

Every listener record (`browser.listener`) and dispatch record
(`browser.dispatch`) carries a `scope` object in the shape network records
already use:

- `contextKind`: `window`, `dedicated-worker`, `shared-worker`,
  `service-worker`, `worklet`, or `other`, from Blink's own classification of
  the execution context the target belongs to;
- `workerToken`: the global scope's DevTools token, null for a window; and
- `globalObjectUrl`: the global scope's URL, null for a window.

A worker's listener, dispatch, and network records therefore carry the same
worker token, and a later analysis can join them without inferring anything.

## Records Without A Document

A worker or worklet global scope has no document. A record in such a scope
reports a null `context.documentId`, and each of its targets reports a null
`documentId`. Only a target that is not a Node can occur there. A record in a
window scope, and an earlier record that carries no scope, must name its
document on every target, as before. The session validator enforces these
rules under the code `browser-event-scope-inconsistent`.

## Dispatches Outside The Node Dispatcher

Each of the three entry points above opens a dispatch record only when no hook
has already opened one for the same event, and completes only a dispatch it
opened. An IndexedDB database dispatch that delegates to
`EventTarget::DispatchEventInternal`, for example, is recorded once, by
whichever hook met the event first.

The records use the existing event types: `dispatch-started`, one
`listener-invoked` per listener, and `dispatch-completed`.

- The original target is described as a listener target is. A Node original
  target, the document for a window's load event, is described by its node
  identifier alone, as the Node dispatcher describes one.
- The composed path lists the targets Blink fires listeners on, in path order.
  It is one entry for an at-target dispatch and for a window's load or
  pageshow dispatch, the window in the latter case, and the request,
  transaction, and database, in that order, for an IndexedDB request event.
- These paths have no tree scopes, so each path scope reports no tree scope
  root and no related target. It reports the original target's node
  identifier when the original target is a Node.
- `visiblePathIndexes` for an entry is that entry's own index when
  `composedPath()` would return it to a listener on it, and is empty
  otherwise, following the rule in `Event::composedPath` quoted above. The
  full path an IndexedDB event travels is therefore recorded, although a
  listener cannot read it from `composedPath()`.

## Not Covered

- Web Serial. `SerialPort` fires listeners on its parent and itself without a
  path this slice hooks, and validating a hook there needs a serial device.
- Worklet scopes. A worklet global scope is not an EventTarget, but a target
  inside one, such as a `MessagePort` in an audio worklet, is recorded with a
  `worklet` scope. Such records are produced but are not part of this slice's
  validation.
- Scope for default-action records, which only a Node dispatch produces.

## Required Test Levels

- Integration script unit tests: each new patch applies once to the reference
  sources, a second run changes nothing, a checkout patched at protocol 0.30
  converges on the same source, and every bridge call matches the bridge
  header's arity.
- Contract ingest tests: a worker target, a worker listener, a worker
  dispatch, and a window-scoped dispatch are accepted as the bridge writes
  them.
- Session validator tests: a worker listener without a document is accepted;
  a Node target in a worker scope, a worker record that names a document, and
  a target without a document outside a worker scope are rejected.
- Windows end-to-end validation in the instrumented browser, against a
  fixture page that exercises a window load and pageshow listener, a window
  `postMessage` and `dispatchEvent`, an `AbortSignal`, a script-constructed
  `EventTarget`, a `MessageChannel` port, IndexedDB request, transaction, and
  database listeners, and dedicated, shared, and service workers. The run
  checks each dispatch's path, scope, and listener invocations, and checks
  that a worker listener's token equals the token on that worker's network
  records.

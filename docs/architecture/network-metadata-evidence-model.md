# Network Metadata Evidence Model

## Purpose

Protocol 0.26 records request and response metadata for the loads a tested
page makes, on a new `browser.network` channel. The records join the existing
browser, renderer, page, frame, document, navigation, and script-origin
correlation spine, so a later analysis can say which request a document or
worker made, what the browser sent and received for it, how long it took, and
which cookies it carried, at a known point in the session.

The records are evidence. The recorder does not interpret them, compare them
with any expectation, or flag any value.

No record carries a request body, a response body, a cookie value, or the
value of a header that carries a credential. Header names are always recorded.

## Capture boundary

Metadata is read at three kinds of point.

- Renderer loads. Hooks in Blink's resource load observers,
  `ResourceLoadObserverForFrame` in
  `third_party/blink/renderer/core/loader/resource_load_observer_for_frame.cc`
  and `ResourceLoadObserverForWorker` in
  `third_party/blink/renderer/core/loader/resource_load_observer_for_worker.cc`,
  record each request as it is about to be sent and each redirect of it, the
  response, the finish, and any failure, for every load a document or a worker
  makes through Blink's resource fetcher.
  A hook in `ResourceFetcher` in
  `third_party/blink/renderer/platform/loader/fetch/resource_fetcher.cc`
  records each use of a resource Blink already held in its memory cache, for
  which no request leaves the renderer.
- Wire headers. Hooks in `NetworkServiceDevToolsObserver` in
  `content/browser/devtools/network_service_devtools_observer.cc` record the
  request headers the network service reports sending and the response headers
  it reports receiving, with the cookies it attached, excluded, set, or refused.
- Navigations. A hook in `WebContentsImpl::DidFinishNavigation` in
  `content/browser/web_contents/web_contents_impl.cc` records the request and
  response metadata of every finished navigation, immediately after the
  existing `navigation-completed` record.

### Request identifiers

The network service reports wire headers only for a request that carries a
DevTools request identifier, and Chromium assigns one only while DevTools is
attached. While the recorder is connected, hooks in
`third_party/blink/renderer/core/loader/frame_fetch_context.cc`,
`third_party/blink/renderer/core/loader/worker_fetch_context.cc`, and
`content/browser/loader/navigation_url_loader_impl.cc` assign the identifier
DevTools would assign to any request that has none, so wire headers are
reported for recorded requests whether or not DevTools is attached. A request
Blink marks as internal is left without one, as DevTools leaves it.

This is the one network hook that changes Chromium's behaviour rather than
only reading it. It is guarded so that a browser started without the recorder
behaves as stock. While the recorder is connected, the network service does
the reporting work it does when DevTools is attached.

## Records

Every renderer record carries a `scope` naming the context that made the load:
`contextKind` is `window`, `dedicated-worker`, `shared-worker`,
`service-worker`, `worklet`, or `other`; `workerToken` is the worker's DevTools token and
`globalObjectUrl` its script URL, both null for a window. A window record
carries the document context the other renderer channels use; a worker record
carries no document.

`inspectorId` is Blink's identifier for the load, unique within one renderer
process, as an unsigned decimal string. `requestId` is the DevTools request
identifier, which the wire-header records carry too, and is null when the
request has none.

### Request will be sent

`request-will-be-sent` records a request, or a redirect of it. For a redirect,
`redirect` is true, `request` describes the next request, and
`redirectResponse` describes the redirect response.

`request` holds the URL, method, resource type, initiator (type, URL, line,
column, and whether it came from a link preload), whether Blink marks the
request as internal, the fetch destination, mode, credentials mode, redirect
mode, and cache mode, the current and initial priority, the fetch priority
hint, the render-blocking behaviour, the referrer and referrer policy, whether
the request is keepalive, carries a user gesture, is an ad resource, or submits
a form, and the request headers Blink holds.

`location` and `world` report the script that was current when Blink issued
the request, read with the helper the cookie records use, and are null when no
script was running, as for a parser-inserted image. Blink keeps the names and
stable identifiers of isolated worlds only on the main thread, so a request a
worker script makes reports its world kind and identifier without a name or
stable identifier.

### Response received

`response-received` records the response Blink received for a load.
`responseSource` is `loader` for a response from the network, the HTTP cache,
or a service worker, and `memory-cache` for a memory cache use Blink replayed
through the observer, as described under Limits.

`response` holds the request URL and final response URL, status code and text,
MIME type and charset, ALPN protocol and connection information, remote IP
address and port, connection identifier and whether it was reused, whether the
response came from the HTTP cache, from a service worker (and from which of
its sources), from the prefetch cache, or from a web archive, whether the
network was accessed, whether the request carried a cookie, the response type,
the encoded length so far, the expected content length, the response headers,
and the load timing.

The load timing gives the request start as milliseconds before the record was
written, and each phase (proxy, DNS lookup, connect, TLS, service worker start,
ready, fetch, respond-with, router evaluation, and cache lookup, send, headers
received, first non-informational headers, early hints, push, and response
end) as milliseconds after the request start. A phase Chromium did not record
is null.

### Request finished and request failed

`request-finished` records the encoded data length, the decoded body length,
and the finish time as milliseconds before the record was written.

Chromium reports an encoded data length of -1 when no data crossed the
network, as for a `chrome://` or other locally served response. The logger
records that value as null, in both the response and the finish record, rather
than as a byte count.

`request-failed` records the URL, the network error code and its short name,
and whether the failure was a cancellation, a timeout, an access check, a
response block, an opaque-response block, a cancellation after an HTTP error,
or an internal request, whether a copy is in the cache, the blocked reason, and
any CORS error with its failed parameter.

### Memory cache hit

`memory-cache-hit` records one use of a resource Blink already held, with the
request and the cached response. `staticData` is true for a resource Blink
holds as static data, such as a `data:` URL.

### Wire headers

`request-headers-sent` records the headers the network service reports having
sent for a request, with the time they were sent as milliseconds before the
record was written, and the cookies it attached or excluded.
`response-headers-received` records the status code and headers it reports
receiving, and the cookies the response set or tried to set. Cookies are
listed by name and attributes in the form the `browser.cookie` channel uses,
up to its limit per record. A frame record carries the page and frame
identity; a worker record carries `devtoolsAgentId` and no frame identity.

### Navigation response

`navigation-response` records, for every finished navigation, the navigation
identity the navigation channel uses, the request identifier, URL, and method,
whether it committed, produced an error page, stayed in the same document,
became a download, or was served from the back-forward cache, the network
error and its name, the redirect chain, the request headers, the response
head (status, status text, MIME type, whether it came from the cache, remote
address, connection information, and headers), and the navigation timing
(loader start, first and final request and response starts, loader callbacks,
failure, commit messages, and the final request's DNS, connect, and TLS
starts). Times are given in the same way as the load timing.

## Header values

Every header list carries each header's name, its value or null, whether the
value was withheld, the reason, the full header count, and whether the list
was cut. A list holds at most 256 headers. Values are not otherwise bounded.

The bridge withholds a value in three cases, recorded as the reason:

- `credential-header`: the header is `Cookie`, `Set-Cookie`, `Set-Cookie2`,
  `Authorization`, or `Proxy-Authorization`.
- `credential-name`: the header name contains `token`, `secret`, `key`,
  `password`, `session`, `csrf`, `xsrf`, `signature`, or `auth`, other than
  `:authority`, `WWW-Authenticate`, and `Proxy-Authenticate`, which carry a
  host or a challenge.
- `credential-value`: the value begins with an HTTP authentication scheme such
  as `Bearer` or `Basic`, or contains a JSON Web Token.

The archive validator rejects a record in which one of the five credential
headers carries a value.

## Limits

- Only loads that pass through Blink's resource fetcher, and navigations, are
  recorded. Requests the browser process makes for itself, such as safe
  browsing, updates, and extension or DevTools traffic, are not.
- The renderer headers are the headers Blink holds. Headers the network
  service adds later, such as `Cookie`, appear only in the wire-header
  records.
- Wire headers are reported only where the network service was given a DevTools
  observer. Frame loads and navigations always have one; some worker loads are
  made through a factory without one, and their wire headers are not recorded.
- URLs are recorded in full, including query strings. A credential carried in a
  URL is recorded.
- A credential carried in a header whose name and value match none of the rules
  above, such as an unprefixed key under an unrelated header name, is recorded.
- A worker's memory cache hits carry no request identifier.
- When the resource load observer reports interest in all requests, as it
  does while DevTools inspects the page's network activity, or when Blink's
  `SkipCallbacksWhenDevToolsNotOpen` feature is turned off, Blink replays a
  memory cache use through the observer. The use then also produces
  `request-will-be-sent`, `response-received` with `responseSource`
  `memory-cache`, and `request-finished` records, alongside the
  `memory-cache-hit` record.
- Byte counts are recorded as JSON numbers and are exact up to 2^53.
- No body is recorded, so neither the content of a response nor what a script
  did with it is evidence here.

## Validation

The validation run serves a fifth fixture page from the loopback HTTP listener
the cookie fixture uses, opened in a background tab through a redirect. The
page sends a fetch carrying an `Authorization` header, an `X-Api-Key` header,
a header whose value begins with `Bearer`, and a plain header; follows a
redirected fetch; fetches from a closed loopback port; loads one cacheable
script twice; and starts a dedicated worker that sends a fetch. The credential
values are generated for each run.

The verifier requires that no record contains any of the credential values or
the cookie value, that every credential header in every network record is
withheld, that the page's fetch has request, response, finish, and wire-header
records linked by identifier with the plain header's value kept and each
credential value withheld for the expected reason, that the wire request lists
a fixture cookie by name, that the redirect, failure, memory cache hit, and
worker fetch are each recorded, and that the page's navigation response
reports its redirect chain. The fixture shows that the logger emits records;
it does not evaluate the page's network use.

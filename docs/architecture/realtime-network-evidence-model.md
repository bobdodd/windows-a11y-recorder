# Realtime Network Evidence Model

## Purpose

Protocol 0.27 records the realtime channels a tested page opens, WebSocket,
EventSource, and WebTransport, on the `browser.network` channel that protocol
0.26 introduced for request and response metadata. The records join the same
browser, renderer, page, frame, document, worker, and script-origin correlation
spine, so a later analysis can say which document or worker opened a channel,
which cookies its handshake carried and set, what text passed over it, and how
it closed, at a known point in the session.

The records are evidence. The recorder does not interpret them, compare them
with any expectation, or flag any value.

No record carries a cookie value, the value of a header that carries a
credential, a binary message's content, or a part of a message that looks like
a credential.

## Capture boundary

Hooks run in Blink, at the points where Blink already reports each channel to
DevTools through its inspector probes.

- WebSocket. Hooks in `WebSocketChannelImpl` in
  `third_party/blink/renderer/modules/websockets/websocket_channel_impl.cc`
  record the socket's creation, the opening handshake request and response the
  network service reported, each message sent and received, the page's close
  request, a failure, and the closure.
- EventSource. A hook in `EventSource` in
  `third_party/blink/renderer/modules/eventsource/event_source.cc` records each
  event the stream dispatches.
- WebTransport. Hooks in `WebTransport` in
  `third_party/blink/renderer/modules/webtransport/web_transport.cc` record the
  session's creation, its establishment with the response the network service
  reported, the page's close request, and the session's cleanup.

### Handshake cookies

The network service removes the `Cookie` header from the WebSocket handshake
request it reports to the renderer, and the `Set-Cookie`, `Set-Cookie2`, and
`Clear-Site-Data` headers from the response, unless the renderer has raw
header access, which Chromium grants only while DevTools inspects the page's
network activity. Without a change, the renderer could not say which cookies a
handshake carried.

A hook in `services/network/websocket.cc` changes this in a recording browser.
When the network service reports a handshake to a renderer without raw header
access, it reports the `Cookie` request header with every value replaced by
`[withheld]` and each `Set-Cookie` response header reduced to its cookie name
followed by `=[withheld]`, with its attributes dropped. A cookie without a name
is reported as `[withheld]` alone. The original values stay in the network
service. `Authorization` and `Proxy-Authorization` headers stay removed, and
`Set-Cookie2` and `Clear-Site-Data` headers stay removed. When the renderer has
raw header access, the network service reports the headers as stock Chromium
does.

A recording browser is recognized by a switch the browser process adds to the
command line of the network service utility process when the recorder is
connected, or by the recorder bootstrap switch when the network service runs
inside the browser process. A browser started without the recorder behaves as
stock. This is the second network hook that changes what Chromium reports
rather than only reading it.

The bridge then reads the cookie names from those headers and withholds the
header values as it does for every cookie header.

## Records

Every record carries the `scope` the protocol 0.26 renderer records carry,
naming the window or worker that opened the channel, and the matching document
or worker context. The records written at a script's call, which are
`websocket-created`, `websocket-message-sent`, `websocket-close-requested`,
`web-transport-created`, and `web-transport-close-requested`, also carry the
script's source location and JavaScript world when a script was running.

WebSocket and EventSource records carry `inspectorId`, Blink's identifier for
the channel, unique within one renderer process, as an unsigned decimal string.
WebTransport records carry `transportId`, Blink's inspector identifier for the
session, in the same form.

### WebSocket

- `websocket-created`: the URL and the requested subprotocols as the page gave
  them.
- `websocket-handshake-request`: the handshake URL, the headers the network
  service reported, and `cookieNames`, the names in its `Cookie` header, empty
  when the handshake sent no cookie.
- `websocket-handshake-response`: the HTTP version as `major.minor`, the status
  and status text, the remote address, the selected subprotocol and
  extensions, the headers the network service reported, and `setCookieNames`,
  the names of the cookies its `Set-Cookie` headers set.
- `websocket-message-sent` and `websocket-message-received`: the opcode,
  `text` or `binary`, the message length in bytes, and, for a text message
  only, the recorded text. A binary message's content is never recorded.
- `websocket-close-requested`: the close code the page passed to `close()`,
  null when not given, and the reason, empty when not given.
- `websocket-error`: the failure message Blink reported.
- `websocket-closed`: `cause` is `dropped` when the channel closed with a
  closing handshake or a dropped connection, and then carries whether the
  close was clean, the code, and the reason; it is `disconnected` when the
  channel was torn down with its context, and then carries none of them.

### EventSource

- `event-source-message`: the stream URL, the event type, the last event
  identifier, the data length in bytes, and the recorded data.

### WebTransport

- `web-transport-created`: the session URL.
- `web-transport-established`: the maximum datagram size and the response the
  network service reported, with its HTTP version, status, headers, and
  `setCookieNames`. No remote address is available at this point.
- `web-transport-close-requested`: the close code and reason the page passed
  to `close()`. Both are present or both are null.
- `web-transport-closed`: whether the session ended abruptly, and, for a
  session that closed cleanly, the close code and reason.

## Recorded text

Message text, event data, last event identifiers, and close reasons are
recorded as a text object holding the text, whether it was cut, and the parts
that were withheld.

- The text is kept up to 4096 UTF-16 code units. Invalid UTF-8 is replaced by
  U+FFFD. Only the first 65536 bytes are read, and `truncated` is true when the
  text is shorter than the message.
- A part that looks like a credential is replaced by `[withheld]`, and its
  offset in the recorded text, in UTF-16 code units, is recorded with a reason:
  `credential-value` for a JSON Web Token or an HTTP authentication credential
  such as `Bearer` followed by a token, and `credential-name` for the value of
  a field whose name contains one of the credential words the header rules
  use. A field is a JSON member, a query or form pair, or a colon pair.
- A withheld part is never copied into the record.

Handshake and response headers follow the header value rules of the
[network metadata evidence model](network-metadata-evidence-model.md#header-values).

The archive validator rejects text longer than 4096 UTF-16 code units, a
withheld offset that does not mark a `[withheld]` marker in order, a text
message without text or a binary message with text, a `dropped` closure without
its clean flag or a `disconnected` closure with one, a WebTransport close
request with only one of code and reason, and an abrupt WebTransport closure
with a code or reason.

## Limits

- Credential words are matched as substrings, so a field such as `keyboard`
  or `monkey` has its value withheld too. Over-withholding is preferred to
  recording a credential.
- A credential in a field or format none of the rules describe is recorded.
- Text past the first 65536 bytes of a message is not read, so a credential
  that begins within the recorded text and ends past that window is cut at the
  window rather than recognized.
- WebTransport stream and datagram data is not observed.
- `web-transport-closed` is not recorded when the session's context is
  destroyed, because Blink disposes of the session without running its cleanup.
- The `Sec-WebSocket-Key` handshake request header value is withheld as
  `credential-name`, because its name contains `key`. It is a random nonce,
  not a credential. The `Sec-WebSocket-Accept` response header value is kept.
- In a recording browser, the names-only cookie headers are also what DevTools
  and other renderer observers see for a WebSocket handshake while the renderer
  lacks raw header access.
- A cookie without a name whose value contains `=` cannot be told apart from a
  named cookie, as in any `Cookie` header.
- WebTransport sessions need an HTTP/3 server, so establishment is covered by
  the archive validator and receiver tests but not by the Windows validation
  run.

## Validation

The network logging fixture page runs the realtime steps after its fetch steps.
It opens a WebSocket to the loopback fixture server, whose handshake response
sets a cookie; receives the server's greeting; sends a text message holding a
`token` field with a value generated for each run and a four-byte binary
message; receives both echoed back; and closes the socket with code 1000. It
reads a named event whose data holds an `access_token` field with a value
generated for each run, and a plain event, from an event stream. It creates a
WebTransport session to a closed loopback port and closes it while it is still
connecting.

The verifier requires that no record contains either generated value or the
fixture cookie value; that the WebSocket has creation, handshake request and
response, message, close request, and closure records linked by identifier;
that the handshake request lists a fixture cookie by name and the response
lists the cookie it set; that the sent and echoed text keep the plain fields
with the token value withheld at the recorded offset; that binary messages
carry a length and no content; that the named event's credential value is
withheld; that the WebTransport session has creation, close request, and
abrupt closure records and no establishment record; and that the call records
carry the fixture script's main world and a source location. The fixture shows
that the logger emits records; it does not evaluate the page's network use.

Revision `cc62135` passed this validation on Windows on September 24, 2026.
The measured result is in
[the Blink listener dispatch validation plan](../validation/blink-listener-dispatch-plan.md#realtime-network-logging).

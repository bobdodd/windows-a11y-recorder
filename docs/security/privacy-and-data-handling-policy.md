# Privacy and Data-Handling Policy

## Status and scope

- **Status:** Adopted
- **Applies to:** Every channel the recorder captures, the archive, diagnostic
  logs, and anything derived from them
- **Companion document:** [Threat model](threat-model.md)

The recorder is a tool for professional auditors testing websites and
applications with test accounts the client provides. The screen, the browser,
and the tested pages hold test data, not real private or personal information.
No one using the recorder or appearing in a session has an expectation of
privacy in what is recorded.

The recorder may therefore capture anything on any channel, verbatim, including
keystrokes, typed text, text-control and password field values, screen video,
audio, accessibility text, DOM content, page source, URLs, titles, and network
activity. Capture is limited for volume and correctness, never for privacy.

Two kinds of data are never recorded:

- Cookie values.
- Values that are, or look like, API keys and similar secrets.

## Cookie values

No record carries a cookie value, on any channel.

- Capture cookie names, non-value attributes, operation type, result, source context, and timing so consent-related cookie behavior can be analyzed.
- Drop cookie values at source. The instrumented browser reads cookie names from
  `document.cookie` strings, written cookie strings, and unparsed Set-Cookie
  lines inside the hook or the recorder bridge, so the value is discarded before
  a record is built.
- A nameless cookie whose value contains `=` is the one case where value text
  can be reported as a name, because Chromium stores it with an empty name and
  the text before the first `=` cannot be told apart from a name.
- `Cookie` and `Set-Cookie` header values are cookie values. Network records
  report the cookie names a transaction carried and withhold the header
  values.

## API keys and similar secrets

No record carries a value that is, or looks like, an API key or a comparable
secret. This covers:

- The values of `Authorization`, `Proxy-Authorization`, and API-key request
  headers such as `X-API-Key`.
- Bearer tokens, access tokens, refresh tokens, and client secrets, wherever
  they appear.
- Values matching recognizable key formats, such as long high-entropy tokens
  and the prefixed formats that cloud and payment providers issue.
- The recorder's own secrets: bootstrap contents, pipe names, authentication
  tokens, and session capabilities.

A secret is replaced at source by a marker that states a value was withheld and
why, so its absence is explicit rather than a silent gap. The name of the
header, parameter, or field that held it is still recorded.

Test-account passwords and other credentials a tester types into the tested
page are test data, not secrets under this rule, and are recorded like any
other typed text.

## Implementation status

The cookie rule is implemented for every cookie path the recorder observes.

The secret rule is implemented only for the recorder's own secrets, which are
kept out of session evidence and diagnostic logs. The detection of API-key-like
values in captured content is not implemented. Until it is, such a value can
reach the archive through:

- DOM attribute values and character data, including inline script text.
- Text-control values on the `browser.interaction` channel.
- URLs and titles in navigation, document, and script location records.
- Raw keyboard records and screen video.

Network metadata withholds, at source, the values of `Cookie`, `Set-Cookie`,
`Set-Cookie2`, `Authorization`, and `Proxy-Authorization` headers, of headers
whose name contains a credential word such as `key`, `token`, `secret`, or
`auth`, and of header values that begin with an HTTP authentication scheme or
contain a JSON Web Token. Each withheld header is recorded by name with the
reason. A credential can still reach the archive through network records in:

- Request, response, redirect, and referrer URLs, which are recorded in full.
- A header whose name and value match none of those rules.

## Diagnostic logs

Diagnostic logs follow the same two rules. They are not session evidence and
are kept local.

- Opt-in Chromium diagnostic logging is disabled by default. When enabled
  through `A11Y_RECORDER_CHROMIUM_LOG_FILE`, the file can contain tested URLs
  and browsing details.
- Early native bridge tracing through `A11Y_RECORDER_BRIDGE_LOG_FILE` records
  process identifiers, monotonic tick values, startup stages, child process
  types, and internal errors. It never includes bootstrap contents, pipe names,
  authentication tokens, or command lines. A rejected protocol version is
  reported alongside the version the browser requires, bounded in length and
  reduced to printable characters. The recorder sets this path for every
  browser it launches, writing to `diagnostics\browser-bridge.log` inside the
  session directory.

## Changes to this policy

A new channel or field needs no privacy review. It needs a check that it cannot
carry a cookie value or an API-key-like value, or a statement of where it can
and what drops the value.

# DOM Attribute and Text Evidence Model

## Status

Proposed for protocol 0.15. Nothing in this document is implemented. It records
the design decisions that must be settled before the bridge, the Blink hooks, or
the archive validator change.

## Purpose

Protocol 0.12 through 0.14 established a document-tree boundary that carries
node identity, parent identity, node type, and node name, correlated to a
committed browser-process document. That evidence deliberately excludes
attributes and text, which leaves the accessibility questions the recorder
exists to answer outside its reach.

Accessibility state lives almost entirely in attributes. Whether a control
exposes a name, whether `aria-expanded` flipped after activation, whether
`tabindex` made a custom widget reachable, whether `aria-live` was present
before the text inside it changed: none of these are observable from structure.
A structural checkpoint can show that a subtree changed, but not whether a
screen reader had anything to announce.

This slice adds bounded attribute and character-data evidence. Values are
recorded verbatim, bounded only by record-size limits, so that observed DOM
state can be compared directly against what assistive technology exposed.

## Value policy

Attribute values and character data are recorded verbatim. There is no
attribute allowlist, no value class hierarchy, and no digesting or withholding
of text at capture time.

The reasoning is that capture-time withholding is the wrong layer. The archive
already contains display video of the same text, raw keystrokes, and optionally
system audio. Text withheld from a DOM record is still legible in the MP4,
usually more legible than anywhere else, so withholding reduces disclosure by
almost nothing while removing the field an auditor most needs. It also conflicts
with this project's evidence-integrity principle, which requires that raw
evidence is not altered and omissions are not concealed. Sensitivity is handled
where the policy already handles it: purpose limitation and channel selection
before capture, encryption at rest, retention limits, and redacted export
reviewed by a person. Those decisions are reversible at review time by someone
who knows the study. A capture-time exclusion is irreversible and is made by a
tool that does not.

This applies to credential fields as well. Password values entered during a
test session are evidence: whether a correctly typed password was accepted,
whether an incorrect one produced an error, whether that error was exposed to
assistive technology, and whether a password-visibility control actually changed
the exposed value. Test sessions use short-lived test accounts, typically in
sandbox environments, and the typed characters are already in the raw keyboard
record. Excluding the field value would remove evidence about the control while
leaving the characters in the archive anyway.

Not reading field values is distinct from not reading the browser's credential
store. The existing rule against capturing saved passwords, browser history,
cookie values, and authorization values stands: those are browser-held secrets
the tested page never displayed, and nothing in this slice reaches them.

### Residual risk

An operator who records a session against a production system with real personal
data or a real personal credential places that data in the archive. The recorder
cannot distinguish a sandbox from production. This is the same residual risk the
video and keystroke channels already carry, and it is managed by pre-session
purpose and scope configuration, consent, retention, and redaction before
export, not by field-level withholding at capture.

### Bounded record sizes

Attribute values and text are bounded by a per-record length limit, and each
record reports whether its value was truncated. Per-node and per-checkpoint
attribute limits apply in the same way as the existing node limits.

These limits are volume control, not privacy control. Conflating the two is how
an earlier draft of this document justified withholding text. A truncated value
is a partial observation of real evidence and must be reported as such.

## Evidence records

All records use the existing `browser.dom` channel and renderer-process
`BrowserContext`, and carry the protocol 0.14 correlation context including
Chromium's document token.

Every record carries the identifier of the capture limits in force, so an
archive states the record length and attribute limits that produced it rather
than leaving a reader to infer them from truncation flags.

### Attribute state at a checkpoint

`dom-checkpoint-node-attribute` is streamed between a checkpoint start and its
completion, after the node record it describes. It contains the checkpoint
identity, the node identity, the attribute namespace and local name, the
verbatim value, and whether that value was truncated at the record limit.

Emitting attributes as separate records keyed by node identity, rather than
extending `dom-checkpoint-node`, keeps the existing node record and its bridge
signature unchanged. That is a deliberate choice: additive records avoid
touching hooks patched into upstream Chromium sources, and the integration guard
added at revision `b7fd67b` exists precisely because signature changes to those
hooks are the project's most expensive failure mode.

Per-node and per-checkpoint attribute limits apply, with truncation state
reported on the checkpoint completion record in the same way as node limits.

### Attribute transitions

`dom-attribute-changed` records one accepted attribute mutation. It contains the
node identity, the attribute namespace and local name, the change type of added,
removed, or changed, the verbatim current and previous values, and truncation
state for each.

A transition record is not derivable from two checkpoints. Coalescing means one
checkpoint represents an unknown number of changes, so the specific attribute
that flipped is lost. The transition record is what supports the claim that
activating a control changed its exposed state.

### Character-data transitions

`dom-character-data-changed` records one accepted character-data mutation with
the node identity, the verbatim new and previous text, the length of each in
UTF-16 code units, and truncation state for each.

### Trigger integration

Attribute and character-data mutations join the existing post-mutation
machinery, so a qualifying change also queues its document for a structural
checkpoint. The transition records carry the checkpoint identity that the same
delivery pass produced, when one was produced, so a state change and the tree it
occurred in can be joined.

## Claims the evidence supports

The evidence can establish:

- that Blink accepted a specific attribute addition, removal, or change on an
  identified node in an identified document;
- the verbatim attribute value before and after the change, up to the record
  length limit;
- the verbatim text of a changed text node before and after the change, up to
  the same limit;
- the attribute state of nodes observed at a checkpoint, within the configured
  limits; and
- whether an observed attribute or text change occurred in the same delivery
  pass as an observed structural checkpoint.

## Claims the evidence does not support

The evidence does not establish:

- the full content of any value that the record reports as truncated;
- the computed accessibility name or description, which is the result of name
  computation over several sources rather than any single attribute;
- that a platform accessibility API exposed the change, or that a screen reader
  announced it;
- the order of several mutations within one delivery pass beyond the recorded
  sequence of accepted transitions;
- browser-held data the tested page never displayed, such as the credential
  store, browser history, cookie values, or authorization values; or
- that a change was perceivable, painted, or presented.

## Deterministic validation

The reference fixture must exercise, on the document already correlated to
listener and dispatch evidence:

- an enumerated change, by flipping `aria-expanded` on activation;
- a reference change, by pointing `aria-labelledby` at a second element;
- a name change, by replacing an `aria-label`;
- a character-data change inside an `aria-live` region;
- an attribute removal; and
- a value longer than the record limit, to exercise truncation.

The verifier must require correlated context on every record, the expected
change types, and exact string equality between the recorded values and the
fixture's known before and after values. The truncation case must report
`valueTruncated` with a recorded prefix of the expected length rather than a
dropped record.

Archive validation must reject a transition record whose document context does
not match a known document, and reject a record that reports a value length
greater than the recorded value without truncation state.

## Open decisions

One decision remains before implementation starts.

1. Whether attribute state at checkpoints is included in this slice, or whether
   transitions alone are enough until accessibility checkpoints land.
   Transitions only would make this slice smaller and avoid changing
   checkpoint limits.

The per-record length limit is a configuration value rather than a decision,
but it needs choosing with care. It must be large enough that ordinary names,
labels, and live-region text are never truncated, because routine truncation
would lose exactly the evidence this slice exists to capture.

## Next dependent slices

1. Accessibility checkpoints correlated to the same document boundary, which
   consume attribute evidence rather than repeating it.
2. Computed accessibility name and description evidence, which is derived from
   attribute evidence and must be labelled as such.
3. Style, layout, paint, and rendered-frame checkpoints as separate channels.

# DOM Attribute and Text Evidence Model

## Status

Implemented for protocol 0.15. The recorder bridge, the Blink hooks, the archive
validator, and the reference-fixture validation all reflect this document. The
Blink hooks are verified by the integration tests and by integration-time
signature checks; they are pending a full validation run on the reference
platform.

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
completion, after the node record it describes. Its fields are `checkpointId`,
`nodeId`, `attributeIndex`, `attributeNamespace`, `attributeName`,
`attributeValue`, `attributeValueLength`, `attributeValueTruncated`, and
`maximumValueLength`. `attributeIndex` is contiguous from zero within one node,
so a truncated attribute set is visible as a missing tail rather than as a gap.
An empty value is valid and is not the same as an absent attribute.

Emitting attributes as separate records keyed by node identity, rather than
extending `dom-checkpoint-node`, keeps the existing node record and its bridge
signature unchanged. That is a deliberate choice: additive records avoid
touching hooks patched into upstream Chromium sources, and the integration guard
added at revision `b7fd67b` exists precisely because signature changes to those
hooks are the project's most expensive failure mode.

Per-node and per-checkpoint attribute limits apply, with truncation state
reported on the checkpoint completion record in the same way as node limits.
`dom-checkpoint-completed` gains `attributeCount`, `attributesTruncated`,
`maximumAttributesPerNode`, and `maximumValueLength`. The in-force limits are 64
attributes per node and 4096 UTF-16 code units per value.

### Attribute transitions

`dom-attribute-changed` records one accepted attribute mutation. Its fields are
`checkpointId`, `nodeId`, `nodeName`, `attributeNamespace`, `attributeName`,
`changeType`, `attributeValue`, `attributeValueLength`,
`attributeValueTruncated`, `previousAttributeValue`,
`previousAttributeValueLength`, `previousAttributeValueTruncated`, and
`maximumValueLength`.

`changeType` is `added`, `removed`, or `changed`, and it decides which side is
absent: an added attribute has no previous value and a removed attribute has no
current value. The bridge derives the absent side from the change type rather
than accepting it from the caller, so the record cannot contradict itself, and
the archive validator rejects a record that does. The change type itself is
derived in the Blink hook from which value is null rather than from the calling
function, so a modification that upstream reports with a null old or new value is
recorded as an addition or a removal.

A transition record is not derivable from two checkpoints. Coalescing means one
checkpoint represents an unknown number of changes, so the specific attribute
that flipped is lost. The transition record is what supports the claim that
activating a control changed its exposed state.

### Character-data transitions

`dom-character-data-changed` records one accepted character-data mutation. Its
fields are `checkpointId`, `nodeId`, `parentNodeId`, `nodeType`, `text`,
`textLength`, `textTruncated`, `previousText`, `previousTextLength`,
`previousTextTruncated`, and `maximumValueLength`.

Parser-driven character-data updates are excluded. The text a document was
parsed with is already reported by the finished-parsing checkpoint, and
recording every parse-time chunk would queue a checkpoint per chunk during load.
This is a volume decision, and its cost is that text appended by the parser is
observable only as checkpoint state, not as a transition.

### Trigger integration

Attribute and character-data mutations join the existing post-mutation
machinery, so a qualifying change also queues its document for a structural
checkpoint. Neither mutation changes a child list, so nothing else would queue
the document.

The join works by reservation. A transition record is written before the tree it
belongs to has been walked, so the bridge reserves the identity of the
checkpoint that the current delivery pass will produce, keyed by document, and
the next checkpoint started for that document consumes the reservation.
Transitions in one delivery pass therefore share one `checkpointId` with the
checkpoint that followed them.

The residual limit of that design is recorded under claims the evidence does not
support: if a delivery pass ends without producing a checkpoint, a transition
names a checkpoint that does not appear in the archive. The alternative was a
null `checkpointId` and no join at all.

That limit is not marginal. In the first reference-platform validation archive
at revision `8e2ba51`, 200 of 452 transition records named a reservation that
was never completed, spread across three distinct reservations. The reference
fixture's own transitions joined correctly, so the defect is not in the
reservation mechanism itself; documents that mutate without ever producing a
checkpoint are common. A consumer must therefore treat `checkpointId` on a
transition as a claim to be resolved against the archive, not as a guaranteed
reference.

Checkpoint identity is also unique only within a renderer process. The same
archive contained 49 completed checkpoints using 32 distinct identity strings,
because each renderer numbers its own checkpoints. Joining a transition to a
checkpoint requires browser instance, renderer process, and document to match as
well as the identity string.

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
  store, browser history, cookie values, or authorization values;
- the existence of the checkpoint a transition names, in the case where the
  delivery pass ended without producing one, which was 200 of 452 transitions in
  the first validated archive;
- a checkpoint identity that is comparable across renderer processes, since each
  renderer numbers its checkpoints independently; or
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

The verifier requires correlated context on every record, the expected change
types, and exact case-sensitive string equality between the recorded values and
the fixture's known before and after values. The truncation case must report
`attributeValueTruncated` with a recorded prefix of the limit length and the
full length of the original value, rather than a dropped record. The verifier
also requires the removed attribute to be absent from the following checkpoint
state, and requires the six transitions to share one reserved checkpoint
identity whose checkpoint was completed.

Because attribute and character-data mutations now queue checkpoints of their
own, the document no longer produces exactly one post-mutation checkpoint. The
structural checkpoint is identified by the node it added.

Archive validation must reject a transition record whose document context does
not match a known document, and reject a record that reports a value length
greater than the recorded value without truncation state.

## Settled decisions

1. Attribute state at checkpoints is included in this slice, rather than
   deferring it until accessibility checkpoints land.
2. Attribute state is emitted as separate records keyed by node identity rather
   than as additional fields on `dom-checkpoint-node`, so that hook's bridge
   signature is unchanged.
3. Transitions are joined to checkpoints by reserved identity rather than left
   unjoined with a null `checkpointId`.
4. Parser-driven character-data updates are excluded.

The per-record length limit is 4096 UTF-16 code units. It is large enough that
ordinary names, labels, and live-region text are never truncated, because
routine truncation would lose exactly the evidence this slice exists to capture.

## Next dependent slices

1. Accessibility checkpoints correlated to the same document boundary, which
   consume attribute evidence rather than repeating it.
2. Computed accessibility name and description evidence, which is derived from
   attribute evidence and must be labelled as such.
3. Style, layout, paint, and rendered-frame checkpoints as separate channels.

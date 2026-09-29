# Recreating the Page at a Captured Frame

## Status

Slice 1 (character data in DOM checkpoints, protocol 0.33) is implemented on
branch `recording-object-store` and awaits its test on the target Windows
machine. Slices 2 and 3 are proposed and not implemented. Recording text by
content hash is agreed and deferred until slice 4 of change-driven recording;
see "Text by content hash".

## Purpose

An auditor reviewing a recording needs to inspect the page as it was at a
chosen captured frame: its elements, attributes, text, and styles, with the
tools auditors already use. The recorder is an evidence logger, not an
analyzer. A recreation is a view of recorded evidence, and it must not be
presented as the page itself.

## Decision

The player writes the page rebuilt for the chosen frame as an HTML document
and opens it in Chrome, in a profile of its own, so that every Chrome
DevTools panel can be used on it.

The alternative considered was a DevTools back end of the recorder's own.
The DevTools front end talks to its target over the Chrome DevTools Protocol
([Chrome DevTools Protocol](https://chromedevtools.github.io/devtools-protocol/)),
and a protocol domain is implemented by a back end that the front end does
not depend on
([DevTools protocol, devtools-frontend](https://chromium.googlesource.com/devtools/devtools-frontend/+/main/docs/devtools-protocol.md)).
Such a back end could answer `DOM`, `CSS`, and `Accessibility` requests from
the recorded evidence, so every value DevTools showed would be a recorded
one. It was not chosen because nothing would be drawn, and each panel the
auditor uses would need its part of the protocol written for it.

The consequence of the decision is stated wherever the recreation is shown:
Chrome computes styles, layout, and the accessibility tree again from the
rebuilt document. Where the result differs from the recording, DevTools shows
Chrome's result, not the evidence. The player keeps the recorded values for
comparison.

## What a recreation is built from

For each document shown at the frame:

- the DOM: nodes, attributes, character data (from protocol 0.33), shadow
  roots, and slot assignments, from the latest DOM checkpoint at or before
  the frame and the attribute and character data transitions after it;
- the computed style of each element and pseudo-element, the 283 recorded
  properties, from the layout checkpoints and change records;
- form control values and selection, from the interaction checkpoints; and
- the scroll offset of the view, from the layout checkpoint.

The state is matched to a frame through the presentation records where a
checkpoint was presented, and by time otherwise, and the basis is stated.

## What a recreation lacks

- Images, fonts, video, canvas, and other resources. The network records hold
  metadata, not bodies. The rebuilt document blocks all network access, so
  that the current version of a resource is never shown as if it were
  evidence.
- Style sheets as written. Each element carries its recorded computed style
  inline, so rules, cascade, and selectors are not visible, and properties
  outside the 283 recorded take their initial or inherited values. Text
  inside `style` elements is recorded from protocol 0.33 and is kept in the
  document, but it is not applied, since the recorded computed styles
  already reflect it.
- Script behavior. Scripts are not run, and event handler attributes are kept
  as attributes but disabled.
- The document type's public and system identifiers, which are not recorded,
  so the rendering mode the page had is not known.
- The documents of iframes, which the first recreation leaves out.

## Slices

Each slice is tested on the target Windows machine before the next.

1. Browser: record the data of every text, comment, CDATA section, and
   processing instruction node in DOM checkpoints (protocol 0.33). Unit tests
   of the payload contract and validation, and of a check that every such
   node has one data record after its node record and that each completion
   states their number; integration tests of the Chromium patch, including
   the upgrade of a checkout patched for protocol 0.32; system test: a
   recording on the target machine passes the check, and the data of later
   checkpoints equals the data rebuilt from earlier checkpoints and the
   character data transitions, except where a parser appended text.
2. App: rebuild the recorded state of each document at any captured frame.
   Unit tests of the rebuild from generated sequences; integration tests
   comparing the rebuilt state with every full checkpoint of recorded files;
   system test: time to rebuild at the middle and end of a long recording.
3. App: write the rebuilt page for the current frame and open it in Chrome,
   with the notice of what the recreation lacks. Unit tests of the document
   writer, including escaping of recorded text and attribute values, and of
   disabled scripts and blocked network access; system test on the target
   machine: open a frame of a recording and inspect it in DevTools.

## Text by content hash (agreed, deferred)

Slice 1 records each character data node's data in every checkpoint. Script
and style text can be long and rarely changes between checkpoints, and page
text is repeated in every checkpoint of its document, so the same strings
are recorded many times. The agreed design, to be implemented after slice 4
of [change-driven recording](change-driven-recording.md), which removes the
repeated walks of unchanged nodes:

1. A text is identified by the SHA-256 digest of its UTF-8 bytes. The
   renderer records a text record, holding the digest, the text, and its
   length, the first time it meets the text, and the checkpoint and change
   records refer to the digest. A digest needs no identity assigned across
   renderer processes, is the same for the same text in any tab, document,
   or recording, and lets the app check each text record by computing the
   digest again.
2. The renderer keeps, for each node, the string object whose digest it
   computed. Blink replaces a node's string object when its data changes,
   and a kept reference prevents the object's memory from being reused, so
   an unchanged object means unchanged data, and the digest is computed
   again only for new data.
3. The app writes each text once per recording, rejects a text record whose
   digest does not match its text, and ignores the repeated records of
   other renderers. The playback index maps each digest to the position of
   its text record.
4. After any loss between the renderer and the file, the renderer forgets
   which texts it has recorded and records them again. A reference whose
   text the file does not hold is reported as missing text, not guessed.

To be settled: whether attribute values, which also repeat (SVG path data,
`data:` URLs, inline styles, class lists), use the same records, and whether
short texts, whose digest is longer than the text, are recorded inline.

## Slice 1

The DOM checkpoint helper in `document.cc` records, after the node record of
each `CharacterData` node, a `dom-checkpoint-node-character-data` record
with the node's data, and passes the number of such records to the
completion. The record is described in the
[DOM checkpoint evidence model](dom-checkpoint-evidence-model.md). The
integration script replaces the helper of a checkout patched for protocols
0.29 to 0.32.

`DomCharacterDataCheck` checks a recording file: each text and comment node
of a checkpoint has exactly one data record directly after it, no element,
document, or shadow root has one, and each completion's count equals the
records. It also rebuilds each node's data from the checkpoints and
transitions, and compares each transition's previous text and each later
checkpoint's data with it.

The payload validator accepts a completion without `characterDataCount`,
because the database evidence tables, which are not written for a recording
that has a recording file, have no column for it and no table for the data
records. The check above reports a completion without it.

The check run on a recording made with protocol 0.32 found 246,202 text
nodes and 7,652 comment nodes in 386 checkpoints, none with data.

Recording the data adds volume in proportion to the page's text, including
script and style text. That volume has not been measured.

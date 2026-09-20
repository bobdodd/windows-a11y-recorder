# Blink DOM Checkpoint Validation

## Purpose

This record documents the successful reference-machine validation of protocol
0.12. The slice records one bounded structural checkpoint when Blink reaches
the parser-complete boundary for a document.

The checkpoint is streamed as start, preorder node, and completion records. It
captures node identity, direct parent identity, node type, and node name. It
does not capture text content, attributes, style, layout, accessibility state,
paint, or rendered pixels.

## Environment

- Validation date: September 19, 2026
- Operating system: Windows 10 22H2, build 19045, x64
- Chromium output: `out\\A11yRecorder\\chrome.exe`
- Validated source checkpoint: `79caba1`
- Browser evidence protocol: 0.12

## Procedure

The validation ran from a standard, non-elevated PowerShell session. The
workflow:

1. Tested the portable Chromium integration script.
2. Applied the integration to the reference Chromium checkout.
3. Built the instrumented Chromium executable.
4. Ran the managed recorder test suite.
5. Started a 15-second capture of the deterministic Blink fixture.
6. Recorded parser-complete DOM checkpoints for documents created during the
   run.
7. Selected the checkpoint matching the fixture listener document.
8. Ran archive validation and the fixture-specific evidence verifier.
9. Checked the Chromium log for network-service crashes.

## Result

The complete validation script exited with code 0. The validated session was:

`C:\\Users\\Public\\Documents\\A11yRecorderBlinkValidation\\20260920-010839-0408de0e0b334e6aa1af61777aab1cf5`

Archive validation accepted 2,236 events and 81 artifacts. The recorder dropped
zero records, and Chromium reported zero network-service crashes.

The run observed ten parser-complete checkpoints across all documents created
during capture. The fixture verifier selected `dom-checkpoint-2` by matching
browser instance, renderer process, and Blink document identity to the existing
listener evidence. That checkpoint:

- originated in renderer process 4020;
- identified Blink document `dom-document-6`;
- contained 36 node records;
- used contiguous zero-based preorder indices;
- preserved the document-to-HTML parent edge;
- contained one BODY element with a recorded parent;
- reported matching start and completion limits and counts; and
- completed with `truncated` set to false.

The archive was structurally valid, and the prior listener, dispatch,
default-handler, DOM timer, animation-frame, idle-callback, page-lifecycle,
scheduler-decision, and frame/page navigation-identity evidence remained
valid.

## Validated scope

This run validates:

- the `Document::FinishedParsing()` parser-stop instrumentation boundary;
- protocol 0.12 start, node, and completion records;
- process-local checkpoint identity;
- renderer and Blink document provenance;
- bounded preorder traversal;
- stable node and direct-parent identities;
- node type and node name serialization;
- explicit node count, maximum-node limit, and truncation state;
- deterministic archive-level relationship checks; and
- preservation of all previously validated browser evidence slices.

## Scope boundary

This run does not validate large-document truncation, dynamic mutation
checkpoints, shadow DOM, cross-origin frame traversal, browser-process
navigation-document to renderer-document mapping, accessibility snapshots,
style, layout, paint, compositing, rendered pixels, sustained high-volume DOM
capture, or omission handling under backpressure.

The evidence and claim rules remain defined by the
[DOM checkpoint evidence model](../architecture/dom-checkpoint-evidence-model.md).

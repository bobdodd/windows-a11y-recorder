# Blink Navigation and Document-Identity Validation

## Purpose

This record documents the successful reference-machine validation of the
tenth browser evidence slice. The slice records primary-main-frame navigation
starts and completions at Chromium's browser-process `WebContentsImpl`
boundaries.

The evidence distinguishes cross-document and same-document navigation while
preserving scoped page, frame, navigation, and committed-document identity.
It does not treat navigation as proof of a distinct user-visible view.

## Environment

- Validation date: September 19, 2026
- Operating system: Windows 10 22H2, build 19045, x64
- Chromium output: `out\\A11yRecorder\\chrome.exe`
- Validated source checkpoint: `cfdfae9`
- Browser evidence protocol: 0.10

## Procedure

The validation ran from a standard, non-elevated PowerShell session. The
workflow:

1. Tested the portable Chromium integration script.
2. Applied the integration to the reference Chromium checkout.
3. Built the instrumented Chromium executable.
4. Ran the managed recorder test suite.
5. Started a 15-second capture of the deterministic Blink fixture.
6. Exercised a cross-document fixture commit and a same-document history
   update.
7. Ran archive validation and the fixture-specific evidence verifier.
8. Checked the Chromium log for network-service crashes.

## Result

The complete validation script exited with code 0. The validated session was:

`C:\\Users\\Public\\Documents\\A11yRecorderBlinkValidation\\20260919-215344-5cba7065e8b44c46a5bf129ab2a9449b`

Archive validation accepted 1,860 events and 81 artifacts. Chromium reported
no network-service crashes.

The fixture verifier accepted:

- browser-process provenance for selected navigation records;
- committed cross-document and same-document navigation completions;
- a preceding start record correlated to each selected completion;
- stable page and frame identity across the two selected navigations;
- stable committed-document identity across the same-document navigation; and
- distinct navigation identity for the cross-document and same-document
  attempts.

The archive was structurally valid, and all evidence preserved by the prior
listener, dispatch, default-handler, DOM timer, animation-frame, idle-callback,
page-lifecycle, and scheduler-decision slices remained valid.

## Validated scope

This run validates:

- Primary-main-frame navigation boundaries at
  `WebContentsImpl::DidStartNavigation()` and
  `WebContentsImpl::DidFinishNavigation()`.
- Browser-process origin for navigation evidence.
- Correlation of navigation starts and completions.
- Explicit cross-document and same-document classification.
- Stable page and frame identity across the deterministic fixture sequence.
- Preservation of committed-document identity for same-document navigation.
- Distinct navigation identity for separate navigation attempts.
- Renderer authentication, clock synchronization, session finalization, and
  archive validation for the complete fixture.

## Scope boundary

This run does not validate subframe, prerender, fenced-frame, portal, or
non-primary page navigation. It does not prove that navigation changed the DOM,
accessibility tree, pixels, focus, or user-visible view. It also does not
validate load milestones, history traversal, redirects, failed navigation,
committed error pages, browser restarts, high-volume navigation traffic, or
omission handling under backpressure.

The identity and claim rules remain defined by the
[navigation and document identity evidence model](../architecture/navigation-document-identity-evidence-model.md).

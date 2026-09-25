# Rendered-Frame Correlation Evidence Model

Status: design, not yet implemented. Proposed as protocol 0.30.

## Purpose

A layout checkpoint (protocol 0.25) and its interaction snapshot (protocol
0.29) state what Blink held for a document at the end of a rendering update.
A desktop frame (`graphics.desktop.frames`) states what the screen showed when
the recorder copied it. Nothing in the archive currently joins the two. The
only shared quantity is the session clock, and neither side records the time
the relevant pixels reached the compositor:

- the layout checkpoint is timed when Blink finished the rendering update,
  which precedes commit, raster, draw, and presentation of the resulting
  frame; and
- the desktop frame is timed when the recorder polled for it, not when the
  Windows compositor produced it.

This slice records the compositor identity and presentation timing of the
frame that carried each layout checkpoint, and the composition time of every
captured desktop frame. A later analysis can then find the first captured
frame that could show a checkpoint's state, and can state how far apart the
two are.

The records are evidence. The recorder does not decide whether a captured
frame shows a checkpoint's content, and does not compare pixels with layout
geometry.

## Sources

The mechanisms below were read from the Chromium checkout the recorder patches
and from the Windows documentation:

- `cc/trees/swap_promise.h`: `cc::SwapPromise`, with `WillSwap`, `DidSwap`,
  `DidNotSwap`, and the reasons `SWAP_FAILS`, `COMMIT_FAILS`,
  `COMMIT_NO_UPDATE`, and `ACTIVATION_FAILS`.
- `cc/trees/layer_tree_host.h` and `.cc`: `QueueSwapPromise`, which only
  queues the promise and does not request a commit, and `SourceFrameNumber`.
- `third_party/blink/renderer/core/frame/web_frame_widget_impl.cc`:
  `ReportTimeSwapPromise`, the existing Blink promise behind
  `NotifyPresentationTime`. It reads `CompositorFrameMetadata::frame_token` in
  `WillSwap`, registers a presentation callback for that token through
  `WidgetBase::AddPresentationCallback` after `DidSwap`, reports failure on
  `SWAP_FAILS` and `COMMIT_NO_UPDATE`, and stays queued for a later frame on
  the other reasons.
- `components/viz/common/quads/compositor_frame_metadata.h`: frame tokens are
  32-bit, increase per compositor frame sink, and wrap back to 1, so they must
  be compared with `FrameTokenGT`.
- `components/viz/common/frame_timing_details.h`, `ui/gfx/swap_result.h`, and
  `ui/gfx/presentation_feedback.h`: the presentation timestamp, refresh
  interval, and flags (`kVSync`, `kHWClock`, `kHWCompletion`, `kZeroCopy`,
  `kFailure`), and the viz receive, draw, and swap timestamps.
- `base/time/time_win.cc`: when `TimeTicks::IsHighResolution()` is true,
  `TimeTicks` on Windows is the QueryPerformanceCounter value scaled to
  microseconds, with no offset.
- The `Direct3D11CaptureFrame.SystemRelativeTime` reference
  (https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframe.systemrelativetime)
  describes the property as "the QPC (Query Performance Counter) time at which
  the compositor rendered the frame", typed as `TimeSpan`.

The recorder session clock is `Stopwatch.GetTimestamp()` relative to the
session origin (`src/Recorder.Session/SessionClock.cs`), which is QPC on
Windows. The browser bridge already reports QPC ticks, mapped to session time
by `BrowserClockMapper` with a stated uncertainty.

## Capture boundary

### Browser side

The layout checkpoint helper runs in `LocalFrameView::UpdateLifecyclePhases`
after every non-throttled local frame view reaches `kPaintClean`
(`third_party/blink/renderer/core/frame/local_frame_view.cc`). For each layout
checkpoint the bridge started, the helper queues one recorder swap promise on
the `cc::LayerTreeHost` of the frame's local-root widget. A promise queued
there attaches to the next commit from that widget, which, for a rendering
update driven by a main frame, is the commit of the same update.

The recorder promise is a separate `cc::SwapPromise` subclass. It follows the
`ReportTimeSwapPromise` pattern but reports every step as a record instead of
running a caller's callback, and it carries the recorder request identity. The
patch adds one recorder entry point to `WebFrameWidgetImpl`, because the
widget's `WidgetBase`, which owns `AddPresentationCallback`, is private.

Queuing the promise never requests a commit, a frame, or a lifecycle update.
If the widget does not composite, or the frame has no local-root widget, the
request record states that and no promise is queued.

Records are emitted on a new `browser.presentation` channel:

- `presentation-requested`, in the same synchronous call as the layout
  checkpoint completion, before its interaction snapshot;
- `presentation-not-swapped`, once for every `DidNotSwap` the promise
  receives, stating whether the promise was broken or kept for a later frame;
- `presentation-swapped`, when the frame carrying the promise was submitted;
  and
- `presentation-feedback`, when viz reports presentation of that frame.

A request ends with exactly one of: a `presentation-not-swapped` record whose
action is `broken`, or a `presentation-feedback` record. A request that has
neither when the renderer process or session ends is unresolved. The logger
does not record an outcome it did not observe.

### Recorder side

`WindowsGraphicsCaptureBackend.CopyLatestFrame` calls
`Direct3D11CaptureFramePool.TryGetNextFrame()` on a pool of two buffers and
does not read the frame's timestamp. It is extended to return, per monitor,
the frame's `SystemRelativeTime` and the number of attempts before a frame was
available. The desktop frame record gains a `monitorFrames` array with, for
each monitor:

- the monitor handle and bounds;
- `systemRelativeTimeTicks`, the raw `TimeSpan` ticks (100 ns units);
- `compositedAtNanoseconds`, the session time derived from it; and
- `tryGetNextFrameAttempts`.

The existing `capturedAt` poll time is unchanged. A frame captured by the GDI
fallback has no composition time; its `monitorFrames` entries carry null
composition fields and the existing quality flags.

Recording the timestamp does not change which WGC frame is copied. The pool
returns the oldest queued frame, so a captured image can be up to one queued
frame older than the poll suggests. Recording its composition time makes that
visible; changing the dequeue policy is a separate decision.

## Recorded fields

Every `browser.presentation` record carries the layout checkpoint's document
context: recorder browser-instance identity, renderer process, `documentId`,
and document token, plus the request identity `presentation-request-N`,
unique within one renderer process, and the `layoutCheckpointId` it belongs
to. `executionWorldId` is always null.

### Presentation requested

- `sourceFrameNumber`: `LayerTreeHost::SourceFrameNumber()` when queued.
- `isMainFrameWidget`: whether the local root is the outermost main frame.
- `queued`: true, or false with `notQueuedReason` of `no-widget` or
  `not-compositing`.
- `ticks`: bridge QPC ticks at queue time.

### Presentation not swapped

- `reason`: `swap-fails`, `commit-fails`, `commit-no-update`, or
  `activation-fails`.
- `action`: `broken` for `swap-fails` and `commit-no-update`, `kept-active`
  otherwise, matching the promise's return value.
- `ticks`: the timestamp Chromium passed to `DidNotSwap`, converted to QPC.

A `kept-active` record means the promise moved to a later frame, which can
contain changes made after the checkpoint.

### Presentation swapped

- `frameToken`: the compositor frame token from `WillSwap`.
- `swapTicks`: bridge QPC ticks at `DidSwap`.

### Presentation feedback

- `frameToken`: the token the callback was registered for.
- `presentedTicks`: `presentation_feedback.timestamp` converted to QPC ticks,
  or null if the timestamp is null or `TimeTicks` is not high resolution.
- `presentedTimeTicksMicroseconds`: the raw `TimeTicks` value.
- `intervalMicroseconds`: `presentation_feedback.interval`.
- `flags`: the named feedback flags that are set, including `failure`.
- `receivedCompositorFrameTicks`, `drawStartTicks`, `swapStartTicks`,
  `swapEndTicks`: the corresponding `FrameTimingDetails` timestamps converted
  to QPC ticks, each null when Chromium reported a null value.
- `highResolutionTicks`: `TimeTicks::IsHighResolution()`.

The receiver maps every tick field to session nanoseconds with the existing
browser clock mapping and carries its uncertainty.

## Correlation rules

- A request joins its layout checkpoint by browser instance, renderer process,
  and `layoutCheckpointId`. Checkpoint identities are per renderer process.
- Swap and feedback records join their request by browser instance, renderer
  process, and request identity. Frame tokens are compared only within one
  widget, using wrap-aware ordering.
- A checkpoint's presentation time joins desktop frames by session time. On
  the monitor that holds the browser window, the candidate frame is the first
  captured frame whose `compositedAtNanoseconds` is at or after the
  presentation time minus the browser clock uncertainty. Which monitor holds
  the window comes from existing window evidence; when that is unknown, every
  monitor's candidate is reported.
- A checkpoint without feedback, or with a `failure` flag, has no presentation
  time and joins no captured frame by this rule.

## Claims the evidence supports

- Which compositor frame, by token, carried the commit following a layout
  checkpoint, or that no frame was produced and why.
- When Chromium reported that frame as presented, with the flags stating how
  the timestamp was obtained, on the session clock and with its uncertainty.
- When the Windows compositor rendered each captured desktop frame, per
  monitor, on the session clock.
- The interval between a checkpoint's presentation and the composition of
  each captured frame.

## Claims the evidence does not support

- That a captured frame shows the checkpoint's state. Compositor-thread
  scrolling, animation, video, and other windows can change the pixels after
  the commit, and a `kept-active` promise joins a later frame.
- That Chromium's presentation timestamp and the WGC composition time
  describe the same event. Chromium presents to the Windows compositor, which
  composes later, and a timestamp flagged only `vsync` can be an estimate.
- Scan-out or photon time on the physical display.
- That no captured frame between two polls showed the checkpoint. The
  recorder copies at its configured rate and does not keep every composed
  frame.
- Correlation of browser surfaces outside the renderer widget, such as
  browser UI, or of out-of-process iframes beyond their own widget's
  presentation.

## Volume and limits

One request per layout checkpoint, and at most one swapped and one feedback
record per request, plus one not-swapped record per failed attempt. A promise
kept active across many failed commits emits one record for each; the bridge
caps not-swapped records per request and reports the cap in the terminal
record. Desktop frame records grow by one small object per monitor.

## Deterministic validation

Required test levels:

- Unit tests for the contracts, the payload validator for each new record and
  field, the QPC conversion of `TimeTicks` microseconds and of
  `SystemRelativeTime` ticks, and wrap-aware frame-token ordering.
- Unit tests of the Chromium integration script for each new patch anchor.
- A Windows end-to-end Blink validation run and an application-launched
  session run.

The Blink validation run records browser evidence without desktop capture.
Its interaction fixture step changes the listbox outline offset and waits two
animation frames. The verifier requires, for that step's layout checkpoint:

- one queued `presentation-requested` record joined to it;
- a `presentation-swapped` record with a nonzero frame token; and
- a `presentation-feedback` record without the `failure` flag, whose
  presentation time is after the swap time.

For every request in the archive, the verifier requires that swapped,
not-swapped, and feedback records join an existing request; that each request
has at most one terminal outcome; and that frame tokens increase, wrap-aware,
within each widget. It reports the number of unresolved requests rather than
failing on them.

The application-launched session run records desktop frames alongside the
browser. Its verifier requires that every WGC frame reports a composition time
no later than its `capturedAt`, and that at least one presented checkpoint has
a candidate captured frame under the correlation rule. It reports the
distribution of `capturedAt` minus composition time, and of candidate
composition time minus presentation time. Those distributions are
measurements for review, not pass criteria.

Passing these checks shows that the logger emits the records with the stated
shape and joins. It does not show that any captured frame displays a
checkpoint's content.

## Open decisions

- Whether the WGC capture should drain the pool to the newest frame, which
  would change existing capture behavior.
- Whether browser-process frame-token evidence, such as the tokens the
  browser receives for each renderer frame, is needed for out-of-process
  iframe correlation.

# Rendered-Frame Correlation Evidence Model

Status: implemented as protocol 0.30 and validated on Windows.

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
- `third_party/blink/renderer/core/frame/web_frame_widget_impl.h`:
  `GetFrameSinkId()`, public on the widget, identifies the compositor frame
  sink whose frames the tokens number. The browser process holds the same
  value as `RenderWidgetHostImpl::GetFrameSinkId()`
  (`content/browser/renderer_host/render_widget_host_impl.h`).
- `third_party/blink/renderer/core/frame/local_frame.h` and
  `content/public/browser/render_frame_host.h`: a local frame's
  `LocalFrameToken` is the value the browser resolves to a `FrameTreeNodeId`
  with `RenderFrameHost::GetFrameTreeNodeIdForFrameToken`.
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
`Direct3D11CaptureFramePool.TryGetNextFrame()` on a pool of two buffers,
retrying up to 25 times at 10 ms intervals, and previously did not read the
frame's timestamp. It now returns, per monitor, the frame's
`SystemRelativeTime`, the session time at which the pool handed the frame
over, and the number of attempts. The desktop frame record gains a
`monitorFrames` array with, for each monitor:

- `monitorHandle` and the monitor bounds `x`, `y`, `width`, and `height`;
- `systemRelativeTimeTicks`, the raw `TimeSpan` ticks (100 ns units);
- `compositedAtNanoseconds`, the session time derived from it as
  `systemRelativeTimeTicks` times 100 minus the session origin counter value
  scaled to nanoseconds (`CompositionClock` in `Recorder.Contracts`);
- `dequeuedAtNanoseconds`, the session time just after `TryGetNextFrame`
  returned the frame; and
- `tryGetNextFrameAttempts`.

The existing record time (`capturedAt`) is unchanged. It is read before the
pixel buffer is allocated and before the dequeue loop, which can wait up to
250 ms, so a frame composed after `capturedAt` is possible and legitimate. The
dequeue time is recorded because it, not `capturedAt`, is an upper bound on
the composition time.

A frame captured by the GDI fallback has no composition time. Its
`monitorFrames` entries state each monitor and leave the timing fields null.
Archives written before this slice omit `monitorFrames`; the archive validator
accepts that.

Recording the timestamp does not change which WGC frame is copied. The pool
returns the oldest queued frame, so a captured image can be older than the
poll suggests. The first Windows run measured it (see the Windows validation
results below); why it is that large depends in part on whether the capture
stops producing frames while both pool buffers are queued, which has not been
verified. Recording the composition time makes the
age of every image visible.

Decision: the dequeue policy stays unchanged in this slice. The
application-launched session run reports the distribution of `capturedAt`
minus composition time, and that measurement decides whether a later change
should drain the pool to the newest frame. Draining would change what
existing capture records, so it needs its own versioned change and tests.

## Recorded fields

Every `browser.presentation` record carries the layout checkpoint's document
context: recorder browser-instance identity, renderer process, `documentId`,
and document token, plus the request identity `presentation-request-N`,
unique within one renderer process, and the `layoutCheckpointId` it belongs
to. `executionWorldId` is always null.

Every record also carries the identity of the local-root widget the promise
was queued on:

- `frameSinkId`: the widget's `viz::FrameSinkId`, as client and sink
  identifiers. Frame tokens are meaningful only within one frame sink.
- `localRootFrameToken`: the `LocalFrameToken` of the widget's local root.

Both are null only on a request with `notQueuedReason` of `no-widget`. They
are recorded so a later browser-process slice can join renderer widgets to
browser frame and widget identities without changing these records.

### Presentation requested

- `sourceFrameNumber`: `LayerTreeHost::SourceFrameNumber()` when queued, and
  null otherwise.
- `isMainFrameWidget`: `WebFrameWidgetImpl::ForMainFrame()`, null only with
  `no-widget`.
- `queued`: true, or false with `notQueuedReason` of `no-widget` or
  `not-compositing`.
- `highResolutionTicks`: `TimeTicks::IsHighResolution()`.
- `maximumNotSwappedRecords`: the per-request cap on `kept-active` records,
  16.

The queue time is the record's envelope timestamp.

### Presentation not swapped

- `reason`: `swap-fails`, `commit-fails`, `commit-no-update`, or
  `activation-fails`.
- `action`: `broken` for `swap-fails` and `commit-no-update`, `kept-active`
  otherwise, matching the promise's return value.
- `notSwappedIndex` and `notSwappedCount`: the zero-based index of this
  record among the request's not-swapped records, and that index plus one.
- `timestampTicks`: the timestamp Chromium passed to `DidNotSwap`, converted
  to QPC ticks, and `timestampTimeTicksMicroseconds`, the raw value. Both are
  null when Chromium passed a null time.

`kept-active` records beyond the cap are not emitted; a `broken` record is
always emitted.

A `kept-active` record means the promise moved to a later frame, which can
contain changes made after the checkpoint.

### Presentation swapped

- `frameToken`: the compositor frame token from `WillSwap`.
- `notSwappedCount`: the number of not-swapped records before the swap.

The `DidSwap` time is the record's envelope timestamp.

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
- `notSwappedCount`: the number of not-swapped records before the feedback.

### Encoding

- Frame tokens, tick values, microsecond values, and the interval are decimal
  strings, because QPC values and 32-bit unsigned tokens do not all fit the
  JSON integer range the other bridge fields use. The validator requires frame
  tokens in 1 to 4294967295 with no leading zeros.
- `frameSinkId` is the string `client:sink`, each part a 32-bit unsigned
  decimal.
- Every tick field is null unless `highResolutionTicks` is true and Chromium
  reported a nonzero time. A tick field without its raw microsecond value is
  rejected.

A tick field maps to session time through the envelope of the record that
carries it: session nanoseconds equal the record's `monotonicNanoseconds`
plus the difference between the tick value and the record's
`nativeTimestamp.value`, scaled by the frequency in the process's
`browser-clock-synchronized` record. The record's
`timestampUncertaintyNanoseconds` applies.

## Correlation rules

- A request joins its layout checkpoint by browser instance, renderer process,
  and `layoutCheckpointId`. Checkpoint identities are per renderer process.
- Swap and feedback records join their request by browser instance, renderer
  process, and request identity. Frame tokens are compared only within one
  `frameSinkId`, using wrap-aware ordering. Checkpoints from one rendering
  update can share a commit and therefore a token.
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
  presentation time is not before the swap time.

For every request in the archive, the verifier requires that swapped,
not-swapped, and feedback records join an existing request; that each request
has at most one terminal outcome; and that frame tokens never go backwards,
wrap-aware, within each `frameSinkId`. It reports the number of unresolved
requests rather than failing on them.

The application-launched session run records desktop frames alongside the
browser. Its verifier requires that every WGC frame reports, for every
monitor, a composition time no later than its dequeue time, and that at least
one presented checkpoint has a candidate captured frame under the correlation
rule. It reports the distributions of `capturedAt` minus composition time, of
dequeue time minus composition time, and of candidate composition time minus
presentation time, and the number of images that needed more than one dequeue
attempt. Those distributions are
measurements for review, not pass criteria.

Passing these checks shows that the logger emits the records with the stated
shape and joins. It does not show that any captured frame displays a
checkpoint's content.

## Windows validation results

Both runs passed on September 25, 2026, at commit `712f4c4`.

Blink validation: 37 presentation requests, all queued. 35 were presented,
none with the `failure` flag; 2 were broken; none were unresolved; no
`kept-active` records were emitted. Swaps covered 6 frame sinks. The
fixture's held-focus layout checkpoint (`layout-checkpoint-8`, frame sink
`7:10`, token 9) was presented 17.8 ms after its swap, with only the `vsync`
flag.

Application-launched session run, one monitor, 44 captured images:

| Measurement | Minimum | Median | Maximum |
| --- | --- | --- | --- |
| `capturedAt` minus composition time | 350.13 ms | 370.06 ms | 768.42 ms |
| Dequeue time minus composition time | 351.84 ms | 370.80 ms | 770.89 ms |
| Candidate composition minus presentation (11 of 12 presented checkpoints) | 83.28 ms | 166.58 ms | 183.27 ms |

No image needed more than one dequeue attempt.

Reading of the measurement: every copied image was already at least 350 ms
old when the recorder took it, and the first attempt always found a queued
frame. That is consistent with the pool returning its oldest queued frame
while newer compositions wait behind it. It does not establish whether WGC
stops producing frames while both buffers are queued. The candidate lag of 83
to 183 ms is bounded below by that dequeue behavior and the capture rate, not
by the Windows compositor alone. These are measurements from one run on one
machine and are the measurement the dequeue-policy decision in the capture
boundary section was waiting for. No change to the dequeue policy has been
made.

## Decisions

- The WGC dequeue policy is unchanged in this slice; see the recorder-side
  capture boundary.
- Browser-process frame-token evidence is deferred. An out-of-process iframe's
  own widget reports presentation for its frames, so the time-based join
  applies to it, but tying its frames to a browser frame, tab, or window
  requires that later slice. The current Blink fixture's only iframe is
  same-origin and shares the page's widget, so that slice also needs a
  cross-site iframe fixture. The widget identity fields above are what it
  would join on.

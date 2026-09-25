# WGC Newest-Frame Selection

Status: implemented. Windows validation run pending.

## Purpose

Each desktop frame record copies, for every monitor, one frame from a
Windows Graphics Capture (WGC) frame pool. Until this change the recorder
called `Direct3D11CaptureFramePool.TryGetNextFrame()` once per poll on a pool
of two buffers. That returns the oldest queued frame.

The protocol 0.30 application-launched session run (September 25, 2026,
commit `712f4c4`, one monitor, 44 images, capture rate 5 frames per second)
measured the result. Every copied image was composed 350 to 768 ms before
the poll (median 370 ms), about two poll intervals, and every first
`TryGetNextFrame` call found a frame. The measurement is consistent with a
pool that stays full between polls, so the frame copied at each poll dates
from about two polls earlier. The measurement is in
[the rendered-frame correlation evidence model](rendered-frame-correlation-evidence-model.md).

Taking the newest queued frame at each poll, without other changes, would
still copy a frame from about one poll earlier, because the pool would fill
again just after the previous poll. This change keeps the pool from staying
full.

## Mechanism

`WindowsGraphicsCaptureBackend.MonitorCapture` subscribes to the pool's
`FrameArrived` event before it starts the capture session. The pool is
created with `CreateFreeThreaded`, so the handler runs on a pool worker
thread. For each arrival the handler calls `TryGetNextFrame` until it returns
null, records the session time after each call, and offers each frame to a
`NewestArrivalSlot` (`Recorder.Contracts`). The slot holds the newest frame
and releases every frame it replaces, which returns that buffer to the pool.

At a poll the capture thread takes the held frame from the slot, copies it to
the staging texture, and releases it. The pool has three buffers: one for the
frame the slot holds, one for a frame the capture thread may be copying, and
one free.

When no frame has arrived since the previous poll, no newer frame reached the
pool. The capture thread then copies the previous image again from the
staging texture and reports it with that image's original composition and
arrival times. Only the first poll of a monitor waits for an arrival, up to
250 ms as before.

If the handler's `TryGetNextFrame` throws, the handler stops taking frames
and the next poll raises the failure, which moves the collector to the
existing GDI fallback.

## Recorded fields

The desktop frame payload gains `frameSelection`. It is `newest-arrived` for
a WGC frame and null for a GDI fallback frame. Archives written before this
change have no `frameSelection`; their WGC images are the oldest queued frame.

Each `monitorFrames` entry gains:

- `supersededFrameCount`: arrived frames released without being copied since
  the previous capture of this monitor; and
- `reusedPreviousImage`: true when the image is the previous image copied
  again.

Both are null on a GDI fallback frame and absent in earlier archives. The
meanings of existing fields change as follows under `newest-arrived`:

- `dequeuedAtNanoseconds` is the session time at which the arrival handler
  took the frame from the pool, which is normally before `capturedAt`.
- `tryGetNextFrameAttempts` is the number of checks the poll made for an
  arrived frame. It exceeds 1 only while the first poll waits.

A reused image repeats the previous image's `systemRelativeTimeTicks`,
`compositedAtNanoseconds`, and `dequeuedAtNanoseconds`, and has a
`supersededFrameCount` of 0.

## Claims the evidence supports

- Which arrived frame each image came from, as the newest one that reached
  the pool before the poll, with its composition and arrival times.
- How many arrived frames the recorder released unseen between two captures.
- That an image is a repeat of the previous one.

## Claims the evidence does not support

- That the screen did not change between two captures when an image is
  reused. It shows only that no newer frame reached the pool. Whether WGC
  delivers a frame for every composed change has not been verified.
- What a released frame showed. Released frames are not copied, as frames
  were not copied between polls before this change.
- That fresher images show a layout checkpoint's state; see the rendered-frame
  correlation model.

## Deterministic validation

Required test levels:

- Unit tests for `NewestArrivalSlot`: nothing held before an arrival; the
  newest arrival kept with its time; every replaced arrival released exactly
  once and counted; the count restarted by a take; release on close; and
  exactly one arrival held after concurrent offers.
- Unit tests for the archive validator: `frameSelection` values; the selection
  fields required on every monitor of a `newest-arrived` frame and rejected
  without one; `frameSelection` rejected on a GDI fallback frame; a reused
  image with released frames rejected; and earlier archives without the
  fields accepted.
- A Windows application-launched session run.

The application-launched session verifier requires every WGC frame to state
`newest-arrived` and every monitor image to state both selection fields. Per
monitor, in capture order, it requires a new image to be composed after the
previous image, and a reused image to repeat the previous image's composition
and arrival times with no released frames. It reports counts of new and
reused images, released frames, and the distributions of `capturedAt` minus
composition time for new and for reused images, and of arrival minus
composition time. The distributions are measurements for review, not pass
criteria. A reused image's age reflects how long the screen produced no
arrivals, not capture delay.

## Limits

- The handler runs for every arrival, at up to the display refresh rate per
  monitor. It performs no copy; it only takes and releases frames.
- The collector's configured capture rate is unchanged, so the recorder still
  copies at most one image per monitor per poll.

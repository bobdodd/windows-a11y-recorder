# Scheduler Decision Evidence Model

## Purpose

Protocol 0.9 records an authoritative Blink task-queue scheduling decision when
the scheduler moves an allowed wake-up later than its desired wake-up. This
evidence closes the ambiguity left by page-lifecycle observations: a hidden
page or late callback does not, by itself, prove that Chromium throttled a
task.

The instrumentation boundary is
`TaskQueueThrottler::GetNextAllowedWakeUp()`. The record is emitted only when:

\[
\text{allowed wake-up} > \text{desired wake-up}
\]

The implementation follows Chromium's task-queue throttler boundary and queue
ownership model:

- [TaskQueueThrottler implementation](https://source.chromium.org/chromium/chromium/src/+/main:third_party/blink/renderer/platform/scheduler/common/throttling/task_queue_throttler.cc)
- [Main-thread task queue](https://source.chromium.org/chromium/chromium/src/+/main:third_party/blink/renderer/platform/scheduler/main_thread/main_thread_task_queue.h)
- [Frame scheduler implementation](https://source.chromium.org/chromium/chromium/src/+/main:third_party/blink/renderer/platform/scheduler/main_thread/frame_scheduler_impl.h)

## Record shape

The `browser.scheduler` channel emits `wake-up-deferred` with:

- `context`: browser instance, renderer process, and process type. Document,
  frame, page, and execution-world identifiers are null because this decision
  boundary is queue-scoped.
- `queueName` and `queueType`: the owning main-thread queue classification and
  its Chromium enum value.
- `throttlingType`: `foreground-unimportant`, `background`, or
  `background-intensive`.
- `desiredWakeUpTicks` and `allowedWakeUpTicks`: decimal strings containing
  microseconds from Chromium's `TimeTicks` origin. Strings avoid JSON integer
  precision loss. These values are meaningful only within their originating
  process and boot.
- `deferralMilliseconds`: the positive difference between the allowed and
  desired wake-ups.
- `hasReadyTask`: whether the queue already had a ready task at the decision
  boundary.
- `blockType`: `all-tasks` or `new-tasks-only`.
- `decisionBoundary`: the fixed value `task-queue-throttler`.

## Claims the evidence supports

A valid record proves that Blink's task-queue throttler computed a later
allowed wake-up for the named queue in the identified renderer process. It
also preserves the scheduler's throttling classification and block type at
that decision.

The record does not prove that a particular DOM timeout, interval,
animation-frame callback, or idle callback caused the decision. It does not
prove that a deferred task later ran, completed, changed page state, or
produced a rendered frame.

## Correlation rules

Scheduler decisions may be grouped with other evidence by browser instance,
renderer process, and session time. Queue name and queue type further narrow
the scheduling context.

Protocol 0.9 does not carry a task identity that can be joined to a
process-local timer ID. Therefore:

- Do not assign a scheduler decision to an individual timer.
- Do not populate `BrowserTimerPayload.throttled` from nearby scheduler
  records.
- Do not infer document identity from whichever document was active nearby.
- Describe timer lifecycle evidence and scheduler-decision evidence as
  concurrent observations unless a later protocol adds an explicit task
  correlation identifier.

## Deterministic validation

The Blink fixture schedules a short timeout after its document becomes hidden.
The validation harness makes that transition deterministic: it raises the
fixture page, requires the page's own reported visibility, schedules the
page-lifecycle timers from outside the page, and only then hides it. See the
page-lifecycle phase in `docs/validation/blink-listener-dispatch-plan.md`.
Validation independently requires:

- one correlated hidden-page timeout schedule and callback entry;
- at least one positive `frame-throttleable` queue deferral from the same
  renderer process;
- an allowed wake-up greater than its desired wake-up;
- a recognized throttling type and block type;
- a null document identifier on the queue-scoped scheduler record; and
- null `throttled` values on the timer records.

Passing both checks demonstrates that the fixture exercised hidden-page timer
activity and that Blink made an actual queue-level deferral decision. It does
not assert a one-to-one causal relationship between them.

## Future extension

A future task-to-timer correlation slice can add a scheduler task identifier
at task creation and propagate it through queue selection, throttling, and
callback entry. Only that explicit identity would justify setting the timer
payload's `throttled` field or attributing a particular wake-up decision to a
particular callback.

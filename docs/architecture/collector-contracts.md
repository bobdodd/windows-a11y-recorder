# Collector Contracts

## Status and scope

This specification defines the normative boundary between the session coordinator, capture collectors, clock service, archive service, and health service. It applies to every capture collector in the Windows prototype.

Detailed field-level JSON Schemas are intentionally deferred to issue #2. This document defines required semantics that those schemas must preserve.

The key words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** describe requirement strength.

## Design goals

The contract must ensure:

- Collector implementations can change without changing session semantics.
- Slow or failed collectors cannot silently invalidate other channels.
- Capture callbacks remain bounded and non-blocking.
- Every loss, suppression, delay, discontinuity, fallback, and failure is observable.
- Raw observations are not mixed with derived or inferred behavior.
- A partially completed session remains reviewable.
- Native Windows types do not enter shared contracts or archive schemas.

## Collector identity

Every collector instance MUST declare:

- Stable collector type identifier.
- Unique instance identifier within the session.
- Implementation name and version.
- Contract version.
- Produced channel identifiers.
- Capture method for each channel.
- Required Windows capabilities or APIs.
- Required privileges.
- Device, process, display, window, or other target when applicable.
- Whether the collector is required, optional, or fallback under the active policy.

A channel identifier describes a logical evidence stream, not a file path. A collector MAY produce several channels, but failure and omission reporting MUST identify each affected channel.

## Capability declaration

Initialization returns a capability result before the user is allowed to start recording:

```text
Supported
SupportedWithLimitations
Unavailable
DeniedByPolicy
PermissionRequired
Incompatible
```

The result MUST include:

- Collector and channel identifiers.
- Detected target and device metadata.
- Requested and effective configuration.
- Limitations stated as machine-readable codes and readable text.
- Whether fallback is available.
- Whether the result blocks start under the active session policy.

The coordinator MUST NOT infer support from successful process construction. A collector is ready only after it has validated the resources required to begin.

## Lifecycle model

Lifecycle and health are separate state dimensions. A collector can be `Running` while its health is `Degraded`.

### Lifecycle states

```text
Created
  -> Initializing
  -> Ready
  -> Starting
  -> Running
  -> Pausing
  -> Paused
  -> Resuming
  -> Running
  -> Stopping
  -> Stopped
  -> Disposed

Initializing | Starting | Running | Pausing | Paused | Resuming | Stopping
  -> Failed

Failed
  -> Stopping
  -> Stopped
  -> Disposed
```

| State | Meaning |
| --- | --- |
| Created | Instance exists but has not acquired resources. |
| Initializing | Configuration and resources are being validated. |
| Ready | Resources are reserved or known to be acquirable. No evidence is accepted. |
| Starting | Acquisition is starting and clock mappings are being established. |
| Running | Evidence is accepted under the active policy. |
| Pausing | Collector is approaching and recording its pause boundary. |
| Paused | Ordinary evidence is not acquired or accepted. |
| Resuming | Resources and mappings are being re-established. |
| Stopping | No new ordinary evidence is accepted and accepted buffers are draining. |
| Stopped | Terminal records are durable or explicitly declared incomplete. |
| Failed | Collector cannot continue its declared channels. |
| Disposed | Resources are released and the instance cannot restart. |

Every transition MUST emit a lifecycle record. Invalid transition requests MUST return an explicit rejection without changing state.

### Health states

```text
Healthy
Degraded
Failed
Unknown
```

- **Healthy:** The collector is meeting its declared service level with no known omission.
- **Degraded:** Capture continues, but evidence is missing, delayed, less precise, using fallback, or outside tolerance.
- **Failed:** The collector cannot continue one or more declared channels.
- **Unknown:** Health cannot currently be established, including supervisor communication loss.

Health MAY return from `Degraded` to `Healthy` after a recovery record closes every active issue. `Failed` is terminal for that collector instance.

## Coordinator interface

The implementation should expose an asynchronous managed interface with these semantics:

```csharp
public interface ICaptureCollector : IAsyncDisposable
{
    CollectorDescriptor Descriptor { get; }
    CollectorLifecycleState LifecycleState { get; }
    CollectorHealthState HealthState { get; }

    ValueTask<CapabilityResult> InitializeAsync(
        CollectorInitializationContext context,
        CancellationToken cancellationToken);

    ValueTask<CollectorTransitionResult> StartAsync(
        SessionStartBoundary boundary,
        CancellationToken cancellationToken);

    ValueTask<CollectorTransitionResult> PauseAsync(
        SessionPauseBoundary boundary,
        CancellationToken cancellationToken);

    ValueTask<CollectorTransitionResult> ResumeAsync(
        SessionResumeBoundary boundary,
        CancellationToken cancellationToken);

    ValueTask<CollectorStopResult> StopAsync(
        SessionStopBoundary boundary,
        CancellationToken cancellationToken);
}
```

This shape is illustrative. Names may change during implementation, but the semantics in this specification MUST remain.

Methods return only after reaching the reported transition boundary or timing out. Cancellation requests best-effort cancellation and does not imply that resources were released or buffers were durable.

The coordinator MUST:

- Invoke lifecycle methods serially for each collector.
- Supply deadlines and cancellation tokens.
- Record the request, response, duration, and timeout.
- Continue supervising a timed-out collector.
- Never call `DisposeAsync` as a substitute for `StopAsync`.
- Treat an unhandled collector exception as a collector failure, not a capture-host failure.

## Initialization context

The initialization context MUST provide:

- Session identifier.
- Active capture and privacy policy.
- Session clock interface.
- Sequence allocator scoped to each output stream.
- Evidence sink for each declared channel.
- Health and omission sink.
- Logger that excludes raw sensitive payloads by default.
- Memory and queue budget.
- Archive segment allocator.
- Collector deadline policy.
- Capture-host shutdown token.

Collectors MUST NOT receive arbitrary access to the app UI, viewer, analysis components, or another collector's private state.

## Evidence emission contract

Every raw record MUST contain or reference:

- Contract and schema version.
- Session identifier.
- Collector type and instance identifiers.
- Channel identifier.
- Capture method.
- Stream-local sequence number.
- Source-native timestamp and unit when available.
- Mapped monotonic session timestamp.
- Timestamp mapping version and estimated error.
- Process and thread identifiers when meaningful.
- Payload type and payload.
- Quality flags.

Raw records MUST describe observations only. They MUST NOT label user intent, screen-reader commands, usability failures, or conformance.

Examples:

- A keyboard collector records scan code, make or break, flags, device, and context. It does not record "next heading."
- An audio collector records samples, timing, source, and gaps. It does not record a speech transcript.
- A UI Automation collector records an event and selected properties. It does not assert the screen reader's virtual cursor position.

Evidence records become immutable when accepted by the evidence sink. A collector MUST NOT retain a mutable reference and change it after acceptance.

## Timestamp contract

Every collector MUST use the session clock supplied by the coordinator. It MUST NOT define an independent wall-clock ordering system.

A collector with a native clock MUST:

- Preserve the original timestamp.
- Register the native clock domain.
- Supply mapping samples or enough information for the clock service to map it.
- Report resets, discontinuities, missing timestamps, and tolerance violations.

A collector without a native event timestamp MUST sample the session clock as close as possible to observation and set a quality flag describing receipt-time stamping.

UTC timestamps MAY be recorded as anchors. They MUST NOT be used to order raw events.

## Sequence contract

Each output channel receives its own monotonic unsigned sequence. Sequence allocation occurs before queue admission so a dropped record creates a visible sequence gap.

A sequence MUST NOT be reused, including after pause, device restart, fallback, or segment rotation. A new collector instance receives a new instance identifier and starts a new stream sequence.

Persistence order and event time are distinct. The archive service MAY persist records from different streams in any order without changing their event timestamps.

## Queue declaration

Before start, each collector MUST declare for every producer queue:

- Queue identifier.
- Channel identifiers served.
- Capacity in records, buffers, frames, or bytes.
- Estimated maximum memory.
- Full-queue mode.
- Service-level target.
- Degraded and failed thresholds.
- Whether capacity is reserved for boundaries or diagnostics.

Allowed full-queue modes are:

```text
DropWrite
DropOldest
CoalesceRequest
RejectCommand
```

`Wait` is forbidden inside native callbacks, window procedures, UI Automation handlers, audio callbacks, and graphics frame-arrival handlers. It MAY be used by a non-callback producer only when the collector declares and measures the maximum blocking time.

Unbounded queues are forbidden.

## Overflow and omission contract

When a record or buffer is not accepted, the collector MUST update an omission accumulator without depending on the full evidence queue. The accumulator contains:

- Collector instance and channel.
- Omission reason.
- Full-queue policy.
- First and last affected native timestamps.
- First and last affected session timestamps when available.
- First and last missing sequence numbers.
- Count, samples, frames, bytes, or another appropriate quantity.
- Queue capacity and observed high-water mark.
- Whether evidence before or after the omission remains synchronized.

The health service periodically snapshots accumulators into the diagnostics stream. Stop, failure, and checkpoint operations MUST attempt a final snapshot.

A closing omission record MUST NOT be stamped with a timestamp earlier than evidence the collector already emitted on the same channel. A stop boundary is captured before a collector drains the evidence it has already queued, so the last drained record can carry a later timestamp than the boundary, and an omission stamped with the boundary regresses monotonic order within that channel. The archive validator rejects such an archive with `event-time-regressed`, which makes the session status `failed`. A collector therefore reads the session clock when it emits a closing record and retains the stop boundary only as a floor, so the recorded timestamp is an observation of when the closing record was produced rather than an adjustment of a recorded one.

Overflow thresholds:

- The first confirmed loss moves health to `Degraded`.
- Repeated loss beyond the collector's declared failure threshold moves the collector to `Failed` or requests a policy decision.
- A return below the pressure threshold does not erase the omission. It only closes the affected interval and permits health recovery.

## Channel-specific queue rules

### Keyboard and mouse

The Raw Input window procedure MUST only copy the minimum required values, assign time and sequence, attempt a non-blocking enqueue, update omission counters if needed, and return.

The full-queue policy is `DropWrite`. The collector MUST NOT block the input thread or allocate additional unbounded storage.

### Foreground window and process state

The full-queue policy is `DropWrite`. After overflow clears, the collector MUST emit a current-state checkpoint so later analysis can distinguish an unknown transition history from the current observed state.

### Video

The full-queue policy is `DropWrite` for newly arrived frames. The omission record MUST identify the missing frame interval. The encoder MUST NOT silently duplicate a prior frame and present it as newly captured raw evidence.

### Audio

The full-queue policy is `DropOldest` so current capture can continue. Every dropped buffer MUST produce an exact or best-estimate sample gap for that track. Silence MUST NOT be inserted into the raw track without a sidecar record distinguishing synthesized padding from captured silence.

### UI Automation events

The event-handler path uses `DropWrite`. It MUST avoid synchronous tree traversal and property expansion.

Snapshot triggers use `CoalesceRequest`. Coalescing applies to requests, not captured events. The retained request records all trigger identifiers that were merged.

### Annotations and boundaries

Control and annotation records use reserved capacity. If durable acceptance cannot be guaranteed, the command uses `RejectCommand` and the app reports failure to the user. A successful command acknowledgement MUST NOT be returned before durable acceptance.

## Health reporting

Every collector MUST emit:

- Lifecycle transitions.
- Health transitions.
- Periodic heartbeats while running or paused.
- Queue depth and high-water mark.
- Accepted and omitted quantities.
- Callback and processing latency summaries.
- Clock mapping error and drift.
- Device or target changes.
- Fallback activation and deactivation.
- Last successful evidence timestamp.
- Recoverable and terminal errors.

The default heartbeat interval is five seconds. A collector MAY emit more frequently when degraded, but health traffic must remain bounded.

Health records MUST exclude raw keystrokes, audio samples, frame data, and full accessibility text. Diagnostics should identify sensitive items by stream, sequence, time, and category rather than copying the payload.

## Error taxonomy

Every collector error uses one category:

```text
Configuration
Permission
TargetUnavailable
DeviceUnavailable
DeviceChanged
ApiFailure
Timeout
Cancellation
QueueOverflow
ClockDiscontinuity
EncodingFailure
PersistenceFailure
PolicySuppression
SecureDesktop
Unsupported
InternalInvariant
Unknown
```

An error record MUST include:

- Stable error code.
- Category.
- Lifecycle stage.
- Affected channels.
- First occurrence and latest occurrence.
- Occurrence count.
- Recoverability.
- Retry attempted and result.
- Fallback attempted and result.
- Sanitized technical detail.
- User-facing summary.

OS error codes and exception details MAY be retained in diagnostics after removing secrets and raw evidence.

## Timeout and cancellation

Each collector declares deadlines by operation. Initial proposed upper limits are:

| Operation | Deadline |
| --- | --- |
| Initialize | 5 seconds |
| Start | 3 seconds |
| Pause | 500 milliseconds |
| Resume | 3 seconds |
| Stop accepting new evidence | 500 milliseconds |
| Drain event queue | 5 seconds |
| Close media segment | 10 seconds |
| UI Automation event processing | 50 milliseconds in handler path |
| UI Automation property batch | 250 milliseconds |
| UI Automation bounded snapshot | 2 seconds |

A timeout does not prove that underlying Windows work stopped. The collector MUST isolate timed operations so late completion cannot write into a closed stream or mutate terminal state.

UI Automation snapshots MUST have limits for elapsed time, element count, depth, property count, and output bytes. Reaching any limit produces a partial snapshot with explicit truncation metadata.

## Pause semantics

Every collector declares one pause boundary type:

```text
Immediate
EventBoundary
BufferBoundary
FrameBoundary
SegmentBoundary
```

Pause results include:

- Requested pause time.
- Effective pause time.
- Last accepted sequence.
- Last native timestamp.
- Last media timestamp.
- Evidence accepted after request but before boundary.
- Any data discarded during transition.

While paused:

- Participant evidence MUST NOT be captured or queued.
- Health and control records continue.
- Devices MAY remain open if they do not continue participant capture.
- A collector MUST report if Windows or driver behavior prevents a clean pause.

Resume MUST create new timing anchors when device or media clocks may have advanced independently.

## Stop and disposal

`StopAsync` MUST:

1. Establish a stop boundary.
2. Prevent admission of new ordinary evidence.
3. Unregister callbacks or stop acquisition.
4. Drain accepted records within the deadline.
5. Close active media segments.
6. Snapshot omission accumulators.
7. Emit terminal health and lifecycle records.
8. Return the last durable position for every channel.

A stop result identifies complete, partial, timed out, or failed finalization. It MUST NOT report success solely because callbacks were unregistered.

`DisposeAsync` releases resources. It MAY be called after failure or timeout, but it MUST NOT emit ordinary evidence.

## Fallback contract

A collector that supports fallback MUST declare the relationship during initialization:

- Primary channel and method.
- Fallback channel and method.
- Conditions that allow automatic fallback.
- Conditions that require user confirmation.
- Expected loss of precision or scope.

Fallback creates explicit stop and start boundaries for the affected methods. It MUST NOT make two different capture methods appear to be one uninterrupted source.

For process-specific screen-reader audio:

- System loopback is the fallback only when it was included in consent.
- The archive retains both channel identities.
- The health stream records why isolation failed and the interval affected.
- The UI states that audio is no longer isolated.
- Windows 10 22H2 build 19045 declares the process-specific channel unavailable before start and offers consented system loopback instead.

## Collector failure

On an unrecoverable failure, the collector MUST:

- Atomically mark health `Failed`.
- Stop accepting new evidence.
- Record the open omission interval.
- Detach or disable native callbacks.
- Attempt to drain already accepted evidence.
- Return a terminal result to the coordinator.

The coordinator applies the session policy:

- A required collector failure pauses or stops the session pending an explicit decision.
- An optional collector failure allows the session to continue in a visibly degraded state.
- A fallback-capable collector may activate its declared fallback.

No collector may terminate the capture-host process directly.

## Collector restart

The first prototype does not restart a failed collector invisibly. If restart is later enabled:

- It creates a new collector instance identifier.
- It creates new channel segments.
- It records the gap and restart reason.
- It obtains new clock mappings.
- It never reuses sequence numbers.

## Security and privacy requirements

Collectors MUST:

- Acquire only capabilities enabled by the active consent policy.
- Stop participant capture on global pause and secure-desktop detection.
- Avoid logging raw payloads.
- Validate lengths, counts, identifiers, and archive paths.
- Treat external process and UI Automation data as untrusted.
- Avoid code injection and undocumented screen-reader integration.
- Run without elevation in the ordinary path.
- Report inaccessible elevated targets as omissions.

Collectors MUST NOT transmit evidence over the network during capture.

## Test contract

Every collector implementation requires:

- Unit tests for lifecycle transitions and invalid commands.
- Unit tests for sequence allocation and timestamp quality flags.
- Queue saturation tests that verify the declared full-queue mode.
- Tests proving omissions remain reportable when the evidence queue is full.
- Timeout and cancellation tests.
- Device or target loss tests.
- Pause and resume boundary tests.
- Stop and partial-finalization tests.
- Recovery tests against an interrupted archive.
- A 60-minute integration test under representative load.
- A test showing that collector failure does not stop unrelated collectors.

Media and Windows API collectors additionally require repeatable manual scenarios on the reference Windows hardware. Test reports must identify API, device, driver, screen reader, synthesizer, browser, and application versions.

## Initial collector matrix

| Collector | Required by default | Full-queue mode | Pause boundary | Fallback |
| --- | --- | --- | --- | --- |
| Raw keyboard | Yes | DropWrite | EventBoundary | None |
| Raw mouse | Yes | DropWrite | EventBoundary | None |
| Foreground context | Yes | DropWrite | EventBoundary | Current-state checkpoint |
| Display video | Yes | DropWrite | FrameBoundary | Selected window only by new policy |
| Selected-window video | Policy selected | DropWrite | FrameBoundary | Full display only by new policy |
| Microphone | Policy selected | DropOldest | BufferBoundary | None |
| System audio | Policy selected | DropOldest | BufferBoundary | None |
| Process-specific audio | Policy selected and platform supported | DropOldest | BufferBoundary | Consented system audio |
| UI Automation events | Yes | DropWrite | EventBoundary | None |
| UI Automation snapshots | Optional | CoalesceRequest | EventBoundary | Events remain active |
| Annotation receiver | Yes | RejectCommand | Immediate | None |

Consent can disable microphone or playback channels. A consent-disabled channel is `DeniedByPolicy`, not failed or omitted.

## Acceptance checklist

A collector conforms when:

- [ ] Identity, capability, configuration, and required status are declared before start.
- [ ] Every lifecycle transition is valid and recorded.
- [ ] Lifecycle and health states are independent.
- [ ] Native callbacks are bounded and non-blocking.
- [ ] Every queue is bounded and has an explicit full-queue mode.
- [ ] Every dropped item creates a durable omission path.
- [ ] Native and session timestamps are preserved.
- [ ] Sequence gaps reveal rejected evidence.
- [ ] Pause and stop boundaries are measurable.
- [ ] Failure cannot silently invalidate another collector.
- [ ] Fallback is explicit and retains source identity.
- [ ] Diagnostics do not copy raw sensitive payloads.
- [ ] Partial finalization and recovery are testable.
- [ ] No required behavior depends on a screen-reader or browser plugin.

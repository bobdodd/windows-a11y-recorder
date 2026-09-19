[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SessionPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$eventPath = Join-Path $SessionPath "events.ndjson"
if (-not (Test-Path -LiteralPath $eventPath -PathType Leaf)) {
    throw "The session does not contain events.ndjson: $SessionPath"
}

$records = @(
    Get-Content -LiteralPath $eventPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_ | ConvertFrom-Json }
)

$rendererConnections = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.lifecycle" -and
            $_.eventType -eq "browser-connected" -and
            $_.payload.processType -eq "renderer"
        }
)

$listeners = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-registered" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.target.elementId -eq "pointer-only"
        }
)

$dispatches = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dispatch" -and
            $_.eventType -eq "dispatch-started" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.originalTarget.elementId -eq "pointer-only"
        }
)

$invocations = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dispatch" -and
            $_.eventType -eq "listener-invoked" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.originalTarget.elementId -eq "pointer-only"
        }
)

$completions = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dispatch" -and
            $_.eventType -eq "dispatch-completed" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.originalTarget.elementId -eq "pointer-only"
        }
)

$removals = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-removed" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.target.elementId -eq "pointer-only"
        }
)

$suppressedDefaultActions = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dispatch" -and
            $_.eventType -eq "default-action" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.originalTarget.elementId -eq "pointer-only" -and
            $_.payload.defaultAction -eq "blink-default-event-handler" -and
            $_.payload.outcome -eq "suppressed-by-event-handler"
        }
)

$invokedDefaultActions = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dispatch" -and
            $_.eventType -eq "default-action" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.originalTarget.elementId -eq "default-action-link" -and
            $_.payload.currentTarget.elementId -eq "default-action-link" -and
            $_.payload.defaultAction -eq "blink-default-event-handler" -and
            $_.payload.outcome -eq "invoked"
        }
)

$handledDefaultActionCompletions = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dispatch" -and
            $_.eventType -eq "dispatch-completed" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.originalTarget.elementId -eq "default-action-link" -and
            $_.payload.outcome -eq "canceled-by-default-event-handler"
        }
)

$scheduledTimeouts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-scheduled" -and
            $_.payload.timerKind -eq "timeout" -and
            $_.payload.requestedDelayMilliseconds -eq 250 -and
            $_.payload.effectiveDelayMilliseconds -eq 250
        }
)

$scheduledIntervals = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-scheduled" -and
            $_.payload.timerKind -eq "interval" -and
            $_.payload.requestedDelayMilliseconds -eq 125 -and
            $_.payload.effectiveDelayMilliseconds -eq 125
        }
)

if ($rendererConnections.Count -lt 1) {
    throw "No instrumented Chromium renderer connected to the recorder."
}
if ($listeners.Count -lt 1) {
    throw "No click listener registration was recorded for #pointer-only."
}
if ($dispatches.Count -lt 1) {
    throw "No click dispatch start was recorded for #pointer-only."
}
if ($invocations.Count -lt 1) {
    throw "No click listener invocation was recorded for #pointer-only."
}
if ($completions.Count -lt 1) {
    throw "No click dispatch completion was recorded for #pointer-only."
}
if ($removals.Count -lt 1) {
    throw "No click listener removal was recorded for #pointer-only."
}
if ($suppressedDefaultActions.Count -lt 1) {
    throw "No suppressed default handler was recorded for #pointer-only."
}
if ($invokedDefaultActions.Count -lt 1) {
    throw "No invoked default handler was recorded for #default-action-link."
}
if ($handledDefaultActionCompletions.Count -lt 1) {
    throw (
        "The #default-action-link dispatch did not complete as handled by " +
        "a default event handler."
    )
}
if ($scheduledTimeouts.Count -lt 1) {
    throw "The fixture's 250 ms timeout was not recorded as scheduled."
}
if ($scheduledIntervals.Count -lt 1) {
    throw "The fixture's 125 ms interval was not recorded as scheduled."
}

$scheduledTimeout = $scheduledTimeouts[0]
$scheduledInterval = $scheduledIntervals[0]
$firedTimeouts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-fired" -and
            $_.payload.timerId -eq $scheduledTimeout.payload.timerId -and
            $_.payload.timerKind -eq "timeout" -and
            $_.payload.context.browserInstanceId -eq
                $scheduledTimeout.payload.context.browserInstanceId -and
            $_.payload.context.processId -eq
                $scheduledTimeout.payload.context.processId
        }
)
$firedIntervals = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-fired" -and
            $_.payload.timerId -eq $scheduledInterval.payload.timerId -and
            $_.payload.timerKind -eq "interval" -and
            $_.payload.context.browserInstanceId -eq
                $scheduledInterval.payload.context.browserInstanceId -and
            $_.payload.context.processId -eq
                $scheduledInterval.payload.context.processId
        }
)
$cancelledIntervals = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-cancelled" -and
            $_.payload.timerId -eq $scheduledInterval.payload.timerId -and
            $_.payload.timerKind -eq "interval" -and
            $_.payload.context.browserInstanceId -eq
                $scheduledInterval.payload.context.browserInstanceId -and
            $_.payload.context.processId -eq
                $scheduledInterval.payload.context.processId -and
            $_.payload.cancellationReason -eq "explicit-clear"
        }
)
if ($firedTimeouts.Count -ne 1) {
    throw "The fixture timeout did not produce exactly one correlated firing."
}
if ($firedIntervals.Count -ne 1) {
    throw "The fixture interval did not produce exactly one correlated firing."
}
if ($cancelledIntervals.Count -ne 1) {
    throw "The fixture interval did not produce one explicit cancellation."
}
if (
    $firedTimeouts[0].monotonicNanoseconds -lt
    $scheduledTimeout.monotonicNanoseconds
) {
    throw "The fixture timeout fired before its scheduling record."
}
if (
    $firedIntervals[0].monotonicNanoseconds -lt
    $scheduledInterval.monotonicNanoseconds
) {
    throw "The fixture interval fired before its scheduling record."
}
if (
    $cancelledIntervals[0].monotonicNanoseconds -lt
    $firedIntervals[0].monotonicNanoseconds
) {
    throw "The fixture interval was cancelled before its firing record."
}

$listener = $listeners[0].payload
$dispatch = $dispatches[0].payload
$targetInvocations = @(
    $invocations |
        Where-Object {
            $_.payload.currentTarget.elementId -eq "pointer-only"
        }
)
$rootCaptureInvocations = @(
    $invocations |
        Where-Object {
            $_.payload.currentTarget.elementId -eq "propagation-root" -and
            $_.payload.phase -eq "capturing"
        }
)
$rootBubbleInvocations = @(
    $invocations |
        Where-Object {
            $_.payload.currentTarget.elementId -eq "propagation-root" -and
            $_.payload.phase -eq "bubbling"
        }
)
if ($targetInvocations.Count -lt 2) {
    throw "The target listener invocation sequence is incomplete."
}
if ($rootCaptureInvocations.Count -lt 1) {
    throw "No capturing listener invocation was recorded for #propagation-root."
}
if ($rootBubbleInvocations.Count -ne 0) {
    throw "The ancestor bubble listener ran after propagation was stopped."
}

$matchingTargetInvocations = @(
    $targetInvocations |
        Where-Object { $_.payload.listenerId -eq $listeners[0].payload.listenerId }
)
if ($matchingTargetInvocations.Count -lt 1) {
    throw "No target invocation references the primary registered listener."
}
$invocation = $matchingTargetInvocations[0].payload
$completion = $completions[0].payload
$removal = $removals[0].payload

if ($listener.context.processType -ne "renderer") {
    throw "The listener record did not originate in a renderer process."
}
if ($dispatch.context.processType -ne "renderer") {
    throw "The dispatch record did not originate in a renderer process."
}
if ($listener.context.documentId -ne $dispatch.context.documentId) {
    throw "The listener and dispatch document identifiers do not match."
}
if ($listener.target.nodeId -ne $dispatch.originalTarget.nodeId) {
    throw "The listener and dispatch target node identifiers do not match."
}
if ($dispatch.phase -ne "none") {
    throw "The dispatch-started phase was '$($dispatch.phase)', not 'none'."
}
if ([string]::IsNullOrWhiteSpace($listener.listenerId)) {
    throw "The listener record does not contain a listener identifier."
}
if ([string]::IsNullOrWhiteSpace($dispatch.dispatchId)) {
    throw "The dispatch record does not contain a dispatch identifier."
}
if ($listener.registrationKind -ne "add-event-listener") {
    throw "The listener record has an unexpected registration kind."
}
if ($listener.capture -or $listener.passive -or $listener.once) {
    throw "The listener record does not contain the fixture's resolved options."
}
if ($dispatch.trusted) {
    throw "The programmatic fixture dispatch was unexpectedly marked trusted."
}
if ($invocation.dispatchId -ne $dispatch.dispatchId) {
    throw "The listener invocation does not reference the started dispatch."
}
if ($completion.dispatchId -ne $dispatch.dispatchId) {
    throw "The dispatch completion does not reference the started dispatch."
}
if ($invocation.listenerId -ne $listener.listenerId) {
    throw "The invocation does not reference the registered listener."
}
if ($removal.listenerId -ne $listener.listenerId) {
    throw "The removal does not reference the registered listener."
}
if ($invocation.phase -ne "at-target") {
    throw "The click listener invocation phase was not 'at-target'."
}
if ($invocation.currentTarget.elementId -ne "pointer-only") {
    throw "The target invocation currentTarget is incorrect."
}
if ($rootCaptureInvocations[0].payload.currentTarget.elementId -ne
        "propagation-root") {
    throw "The capture invocation currentTarget is incorrect."
}
if (-not $invocation.defaultPrevented) {
    throw "The invocation did not capture the listener's preventDefault call."
}
if ($completion.outcome -ne "canceled-by-event-handler") {
    throw "The dispatch completion has an unexpected outcome."
}
if (-not $completion.defaultPrevented) {
    throw "The completed dispatch did not preserve defaultPrevented."
}
if ($removal.target.nodeId -ne $listener.target.nodeId) {
    throw "The removal target does not match the registration target."
}
$composedPath = @($dispatch.composedPath)
if ($composedPath.Count -lt 2) {
    throw "The dispatch composed path does not contain the target and ancestor."
}
if ($composedPath[0].elementId -ne "pointer-only") {
    throw "The composed path does not begin with the original target."
}
if (@(
        $composedPath |
            Where-Object { $_.elementId -eq "propagation-root" }
    ).Count -ne 1) {
    throw "The composed path does not contain #propagation-root exactly once."
}
if (-not $completion.propagationStopped) {
    throw "The completed dispatch did not preserve stopPropagation()."
}

[pscustomobject]@{
    SessionPath = (Resolve-Path -LiteralPath $SessionPath).Path
    ListenerRecords = $listeners.Count
    DispatchRecords = $dispatches.Count
    InvocationRecords = $invocations.Count
    CompletionRecords = $completions.Count
    RemovalRecords = $removals.Count
    SuppressedDefaultActions = $suppressedDefaultActions.Count
    InvokedDefaultActions = $invokedDefaultActions.Count
    HandledDefaultActionCompletions = $handledDefaultActionCompletions.Count
    ScheduledTimeouts = $scheduledTimeouts.Count
    FiredTimeouts = $firedTimeouts.Count
    ScheduledIntervals = $scheduledIntervals.Count
    FiredIntervals = $firedIntervals.Count
    CancelledIntervals = $cancelledIntervals.Count
    TimeoutTimerId = $scheduledTimeout.payload.timerId
    IntervalTimerId = $scheduledInterval.payload.timerId
    RendererProcessId = $listener.context.processId
    DocumentId = $listener.context.documentId
    TargetNodeId = $listener.target.nodeId
    ListenerId = $listener.listenerId
    DispatchId = $dispatch.dispatchId
    DispatchTrusted = $dispatch.trusted
    InvocationPhase = $invocation.phase
    ComposedPathNodes = $composedPath.Count
    RootCaptureInvocations = $rootCaptureInvocations.Count
    RootBubbleInvocations = $rootBubbleInvocations.Count
    DispatchOutcome = $completion.outcome
} | Format-List

Write-Host (
    "Blink propagation, listener, default-handler, and DOM timer evidence " +
    "verified."
)

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

$navigationStarts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.navigation" -and
            $_.eventType -eq "navigation-started" -and
            $_.payload.url -like "*blink-listener-dispatch.html*" -and
            $_.payload.frameType -eq "primary-main-frame" -and
            $_.payload.primaryPage -eq $true
        }
)

$navigationCompletions = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.navigation" -and
            $_.eventType -eq "navigation-completed" -and
            $_.payload.url -like "*blink-listener-dispatch.html*" -and
            $_.payload.frameType -eq "primary-main-frame" -and
            $_.payload.primaryPage -eq $true -and
            $_.payload.committed -eq $true -and
            $_.payload.errorPage -eq $false -and
            $_.payload.outcome -eq "committed"
        }
)

$subframeNavigationStarts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.navigation" -and
            $_.eventType -eq "navigation-started" -and
            $_.payload.url -like "*blink-subframe.html*" -and
            $_.payload.frameType -eq "subframe" -and
            $_.payload.primaryPage -eq $true
        }
)

$subframeNavigationCompletions = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.navigation" -and
            $_.eventType -eq "navigation-completed" -and
            $_.payload.url -like "*blink-subframe.html*" -and
            $_.payload.frameType -eq "subframe" -and
            $_.payload.primaryPage -eq $true -and
            $_.payload.navigationKind -eq "cross-document" -and
            $_.payload.sameDocument -eq $false -and
            $_.payload.committed -eq $true -and
            $_.payload.errorPage -eq $false -and
            $_.payload.outcome -eq "committed"
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

$domCheckpointStarts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dom" -and
            $_.eventType -eq "dom-checkpoint-started" -and
            $_.payload.reason -eq "finished-parsing"
        }
)
$domCheckpointNodes = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dom" -and
            $_.eventType -eq "dom-checkpoint-node"
        }
)
$domCheckpointCompletions = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dom" -and
            $_.eventType -eq "dom-checkpoint-completed" -and
            $_.payload.reason -eq "finished-parsing"
        }
)
$postMutationCheckpointStarts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dom" -and
            $_.eventType -eq "dom-checkpoint-started" -and
            $_.payload.reason -eq "post-mutation"
        }
)
$postMutationCheckpointCompletions = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dom" -and
            $_.eventType -eq "dom-checkpoint-completed" -and
            $_.payload.reason -eq "post-mutation"
        }
)
# A checkpoint of either reason can cover transitions, so coverage lookups must
# not be restricted to one of them.
$allCheckpointCompletions = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dom" -and
            $_.eventType -eq "dom-checkpoint-completed"
        }
)
$checkpointNodeAttributes = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dom" -and
            $_.eventType -eq "dom-checkpoint-node-attribute"
        }
)
$attributeChanges = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dom" -and
            $_.eventType -eq "dom-attribute-changed"
        }
)
$characterDataChanges = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dom" -and
            $_.eventType -eq "dom-character-data-changed"
        }
)
$accessibilityCheckpointStarts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.accessibility" -and
            $_.eventType -eq "accessibility-checkpoint-started" -and
            $_.payload.reason -eq "renderer-serialization"
        }
)
$accessibilityCheckpointNodes = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.accessibility" -and
            $_.eventType -eq "accessibility-checkpoint-node"
        }
)
$accessibilityCheckpointCompletions = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.accessibility" -and
            $_.eventType -eq "accessibility-checkpoint-completed" -and
            $_.payload.reason -eq "renderer-serialization"
        }
)
$accessibilityCheckpointStarts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.accessibility" -and
            $_.eventType -eq "accessibility-checkpoint-started" -and
            $_.payload.reason -eq "renderer-serialization"
        }
)
$accessibilityCheckpointNodes = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.accessibility" -and
            $_.eventType -eq "accessibility-checkpoint-node"
        }
)
$accessibilityCheckpointCompletions = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.accessibility" -and
            $_.eventType -eq "accessibility-checkpoint-completed" -and
            $_.payload.reason -eq "renderer-serialization"
        }
)

$fixtureDocumentTokens = @(
    (@($navigationCompletions) + @($subframeNavigationCompletions)) |
        ForEach-Object { $_.payload.context.documentToken } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique
)
$activeDocumentsByFrame = @{}
$committedDocumentIdentities = New-Object System.Collections.ArrayList
$mappedDomCheckpoints = @{}
foreach ($record in $records) {
    if (
        $record.channel -eq "browser.navigation" -and
        $record.eventType -eq "navigation-completed" -and
        $record.payload.committed -eq $true
    ) {
        $context = $record.payload.context
        if (
            [string]::IsNullOrWhiteSpace($context.documentToken) -or
            $record.payload.rendererProcessId -le 0
        ) {
            throw "A committed navigation omitted its document token or renderer process."
        }

        $frameKey = "$($context.browserInstanceId)|$($context.frameId)"
        $identity = [pscustomobject]@{
            BrowserInstanceId = $context.browserInstanceId
            FrameId = $context.frameId
            DocumentId = $context.documentId
            DocumentToken = $context.documentToken
            RendererProcessId = $record.payload.rendererProcessId
        }
        if ($record.payload.sameDocument -eq $true) {
            if (-not $activeDocumentsByFrame.ContainsKey($frameKey)) {
                throw "A same-document commit has no active cross-document mapping."
            }
            $active = $activeDocumentsByFrame[$frameKey]
            if (
                $active.DocumentId -ne $identity.DocumentId -or
                $active.DocumentToken -ne $identity.DocumentToken -or
                $active.RendererProcessId -ne $identity.RendererProcessId
            ) {
                throw "A same-document commit changed document or renderer identity."
            }
        } else {
            $activeDocumentsByFrame[$frameKey] = $identity
            [void] $committedDocumentIdentities.Add($identity)
        }
    }

}

# Checkpoint correlation is resolved in a second pass, against every committed
# identity rather than the identity active at the moment the record happened to
# be appended.
#
# A renderer finishes parsing a document before the browser process records the
# commit, and the two channels are merged into one archive, so the order in
# which those records appear is a timing artifact of the run. An earlier version
# of this check consumed records in one pass and therefore required the commit
# to arrive first, which made a correct archive fail whenever the renderer won
# the race. The substantive requirement is that a checkpoint's token resolves to
# exactly one committed document in the same browser instance and renderer
# process, and that is order-independent.
foreach ($record in $records) {
    if (
        $record.channel -ne "browser.dom" -or
        $record.eventType -ne "dom-checkpoint-started" -or
        $record.payload.context.documentToken -notin $fixtureDocumentTokens
    ) {
        continue
    }

    $context = $record.payload.context
    $resolved = @(
        $committedDocumentIdentities |
            Where-Object {
                $_.BrowserInstanceId -eq $context.browserInstanceId -and
                $_.DocumentToken -eq $context.documentToken -and
                $_.RendererProcessId -eq $context.processId
            }
    )
    if ($resolved.Count -ne 1) {
        throw (
            "A DOM checkpoint did not resolve to exactly one committed browser " +
            "document identity. Process-mismatched and ambiguous mappings are " +
            "rejected."
        )
    }
    $mappedDomCheckpoints[$record.payload.checkpointId] = $resolved[0]
}

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
$scheduledLifecycleTimeouts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-scheduled" -and
            $_.payload.timerKind -eq "timeout" -and
            $_.payload.requestedDelayMilliseconds -eq 3000 -and
            $_.payload.effectiveDelayMilliseconds -eq 3000
        }
)
$scheduledLifecycleIntervals = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-scheduled" -and
            $_.payload.timerKind -eq "interval" -and
            $_.payload.requestedDelayMilliseconds -eq 5000 -and
            $_.payload.effectiveDelayMilliseconds -eq 5000
        }
)
$scheduledHiddenTimeouts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-scheduled" -and
            $_.payload.timerKind -eq "timeout" -and
            $_.payload.requestedDelayMilliseconds -eq 25 -and
            $_.payload.effectiveDelayMilliseconds -eq 25 -and
            $_.payload.pageLifecycleState -eq "hidden" -and
            $null -eq $_.payload.throttled
        }
)
$schedulerDeferrals = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.scheduler" -and
            $_.eventType -eq "wake-up-deferred" -and
            $_.payload.queueName -eq "frame-throttleable" -and
            $_.payload.throttlingType -in @(
                "background",
                "background-intensive"
            ) -and
            $_.payload.deferralMilliseconds -gt 0 -and
            $_.payload.blockType -in @("all-tasks", "new-tasks-only") -and
            $_.payload.decisionBoundary -eq "task-queue-throttler"
        }
)

$animationFrameScheduleCandidates = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-scheduled" -and
            $_.payload.timerKind -eq "animation-frame" -and
            $null -eq $_.payload.requestedDelayMilliseconds -and
            $null -eq $_.payload.effectiveDelayMilliseconds -and
            $_.payload.nestingLevel -eq 0
        }
)
$idleCallbackScheduleCandidates = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-scheduled" -and
            $_.payload.timerKind -eq "idle-callback" -and
            $_.payload.requestedDelayMilliseconds -in @(1, 5000) -and
            $_.payload.effectiveDelayMilliseconds -eq
                $_.payload.requestedDelayMilliseconds -and
            $_.payload.nestingLevel -eq 0 -and
            $null -eq $_.payload.didTimeout
        }
)

if ($rendererConnections.Count -lt 1) {
    throw "No instrumented Chromium renderer connected to the recorder."
}
if ($navigationStarts.Count -lt 2) {
    throw "The fixture did not produce cross- and same-document navigation starts."
}
if ($navigationCompletions.Count -lt 2) {
    throw "The fixture did not produce cross- and same-document navigation commits."
}
if ($subframeNavigationStarts.Count -lt 1) {
    throw "The fixture did not produce a child-frame navigation start."
}
if ($subframeNavigationCompletions.Count -lt 1) {
    throw "The fixture did not produce a child-frame navigation commit."
}
$mainCrossDocumentCommit = $navigationCompletions |
    Where-Object { $_.payload.sameDocument -eq $false } |
    Select-Object -First 1
$mainSameDocumentCommit = $navigationCompletions |
    Where-Object { $_.payload.sameDocument -eq $true } |
    Select-Object -First 1
if (-not $mainCrossDocumentCommit -or -not $mainSameDocumentCommit) {
    throw "The fixture did not produce both main-frame navigation kinds."
}
if (
    $mainCrossDocumentCommit.payload.context.documentId -ne
        $mainSameDocumentCommit.payload.context.documentId -or
    $mainCrossDocumentCommit.payload.context.documentToken -ne
        $mainSameDocumentCommit.payload.context.documentToken -or
    $mainCrossDocumentCommit.payload.rendererProcessId -ne
        $mainSameDocumentCommit.payload.rendererProcessId
) {
    throw "The same-document navigation did not preserve its document mapping."
}
if (
    $subframeNavigationCompletions[0].payload.context.documentToken -eq
        $mainCrossDocumentCommit.payload.context.documentToken
) {
    throw "The child frame reused the main-frame document token."
}
$fixtureAccessibilityStarts = @(
    $accessibilityCheckpointStarts |
        Where-Object {
            $_.payload.context.browserInstanceId -eq
                $mainCrossDocumentCommit.payload.context.browserInstanceId -and
            $_.payload.context.processId -eq
                $mainCrossDocumentCommit.payload.rendererProcessId -and
            $_.payload.context.processType -eq "renderer" -and
            $_.payload.context.documentToken -eq
                $mainCrossDocumentCommit.payload.context.documentToken
        }
)
if ($fixtureAccessibilityStarts.Count -lt 1) {
    throw (
        "No renderer accessibility serialization checkpoint correlated with " +
        "the committed fixture document."
    )
}
$fixtureAccessibilityNodes = New-Object System.Collections.ArrayList
$fixtureAccessibilityCompletions = New-Object System.Collections.ArrayList
foreach ($checkpointStart in $fixtureAccessibilityStarts) {
    $checkpointId = $checkpointStart.payload.checkpointId
    if ([string]::IsNullOrWhiteSpace($checkpointId)) {
        throw "An accessibility checkpoint start omitted its identity."
    }
    $checkpointNodes = @(
        $accessibilityCheckpointNodes |
            Where-Object {
                $_.payload.checkpointId -eq $checkpointId -and
                $_.payload.context.browserInstanceId -eq
                    $checkpointStart.payload.context.browserInstanceId -and
                $_.payload.context.processId -eq
                    $checkpointStart.payload.context.processId -and
                $_.payload.context.documentToken -eq
                    $checkpointStart.payload.context.documentToken
            } |
            Sort-Object { $_.payload.nodeIndex }
    )
    $checkpointCompletions = @(
        $accessibilityCheckpointCompletions |
            Where-Object {
                $_.payload.checkpointId -eq $checkpointId -and
                $_.payload.context.browserInstanceId -eq
                    $checkpointStart.payload.context.browserInstanceId -and
                $_.payload.context.processId -eq
                    $checkpointStart.payload.context.processId -and
                $_.payload.context.documentToken -eq
                    $checkpointStart.payload.context.documentToken
            }
    )
    if ($checkpointCompletions.Count -ne 1) {
        throw (
            "An accessibility checkpoint did not have exactly one correlated " +
            "completion."
        )
    }
    $checkpointCompletion = $checkpointCompletions[0]
    if (
        $checkpointCompletion.payload.maximumNodes -ne
            $checkpointStart.payload.maximumNodes -or
        $checkpointCompletion.payload.updateCount -ne
            $checkpointStart.payload.updateCount -or
        $checkpointCompletion.payload.eventCount -ne
            $checkpointStart.payload.eventCount -or
        $checkpointCompletion.payload.nodeCount -ne $checkpointNodes.Count
    ) {
        throw "An accessibility checkpoint reported inconsistent counts or limits."
    }
    if ($checkpointCompletion.payload.truncated) {
        throw "A fixture accessibility checkpoint was unexpectedly truncated."
    }
    for ($index = 0; $index -lt $checkpointNodes.Count; $index++) {
        if ($checkpointNodes[$index].payload.nodeIndex -ne $index) {
            throw "Accessibility checkpoint node indices are not contiguous."
        }
        [void] $fixtureAccessibilityNodes.Add($checkpointNodes[$index])
    }
    [void] $fixtureAccessibilityCompletions.Add($checkpointCompletion)
}
$fixtureAccessibilityButtons = @(
    $fixtureAccessibilityNodes |
        Where-Object {
            $_.payload.serializedProperties -match "(?i)role=button" -and
            $_.payload.name -in @(
                "First disclosure name",
                "Second disclosure name",
                "Fixture disclosure",
                "Fixture disclosure expanded"
            )
        }
)
if ($fixtureAccessibilityButtons.Count -lt 1) {
    throw (
        "The correlated accessibility evidence did not contain the fixture " +
        "button and one of its known accessible names."
    )
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
if ($scheduledLifecycleTimeouts.Count -ne 1) {
    throw "The fixture's 3-second lifecycle timeout was not recorded once."
}
if ($scheduledLifecycleIntervals.Count -ne 1) {
    throw "The fixture's 5-second lifecycle interval was not recorded once."
}
if ($scheduledHiddenTimeouts.Count -ne 1) {
    throw "The fixture's hidden-page 25 ms timeout was not recorded once."
}
if ($schedulerDeferrals.Count -lt 1) {
    throw "No authoritative frame-throttleable wake-up deferral was recorded."
}
$listener = $listeners[0].payload
$domCheckpointStart = $domCheckpointStarts |
    Where-Object {
        $_.payload.context.browserInstanceId -eq
            $listener.context.browserInstanceId -and
        $_.payload.context.processId -eq $listener.context.processId -and
        $_.payload.context.documentId -eq $listener.context.documentId
    } |
    Select-Object -First 1
if (-not $domCheckpointStart) {
    throw "No parser-complete DOM checkpoint was recorded for the fixture document."
}
$domCheckpointId = $domCheckpointStart.payload.checkpointId
if (-not $mappedDomCheckpoints.ContainsKey($domCheckpointId)) {
    throw "The fixture DOM checkpoint has no active browser document mapping."
}
$fixtureDocumentMapping = $mappedDomCheckpoints[$domCheckpointId]
if (
    $fixtureDocumentMapping.DocumentId -ne
        $mainCrossDocumentCommit.payload.context.documentId -or
    $fixtureDocumentMapping.FrameId -ne
        $mainCrossDocumentCommit.payload.context.frameId
) {
    throw "The fixture DOM checkpoint mapped to the wrong committed document."
}
$fixtureDomNodes = @(
    $domCheckpointNodes |
        Where-Object {
            $_.payload.checkpointId -eq $domCheckpointId -and
            $_.payload.context.browserInstanceId -eq
                $listener.context.browserInstanceId -and
            $_.payload.context.processId -eq $listener.context.processId -and
            $_.payload.context.documentId -eq $listener.context.documentId
        } |
        Sort-Object { $_.payload.nodeIndex }
)
$domCheckpointCompletion = $domCheckpointCompletions |
    Where-Object {
        $_.payload.checkpointId -eq $domCheckpointId -and
        $_.payload.context.browserInstanceId -eq
            $listener.context.browserInstanceId -and
        $_.payload.context.processId -eq $listener.context.processId -and
        $_.payload.context.documentId -eq $listener.context.documentId
    } |
    Select-Object -First 1
if (-not $domCheckpointCompletion) {
    throw "The fixture DOM checkpoint did not complete."
}
if ($domCheckpointCompletion.payload.truncated) {
    throw "The fixture DOM checkpoint was unexpectedly truncated."
}
if (
    $domCheckpointCompletion.payload.maximumNodes -ne
        $domCheckpointStart.payload.maximumNodes -or
    $domCheckpointCompletion.payload.nodeCount -ne $fixtureDomNodes.Count
) {
    throw "The fixture DOM checkpoint counts or limits are inconsistent."
}
if ($fixtureDomNodes.Count -lt 4) {
    throw "The fixture DOM checkpoint did not contain the expected structure."
}
for ($index = 0; $index -lt $fixtureDomNodes.Count; $index++) {
    if ($fixtureDomNodes[$index].payload.nodeIndex -ne $index) {
        throw "The fixture DOM checkpoint node indices are not contiguous."
    }
}
$documentNodes = @(
    $fixtureDomNodes |
        Where-Object {
            $_.payload.nodeType -eq "document" -and
            $_.payload.nodeName -eq "#document" -and
            $null -eq $_.payload.parentNodeId
        }
)
if ($documentNodes.Count -ne 1) {
    throw "The fixture DOM checkpoint does not have one root document node."
}
$htmlNodes = @(
    $fixtureDomNodes |
        Where-Object {
            $_.payload.nodeType -eq "element" -and
            $_.payload.nodeName -eq "HTML" -and
            $_.payload.parentNodeId -eq $documentNodes[0].payload.nodeId
        }
)
if ($htmlNodes.Count -ne 1) {
    throw "The fixture DOM checkpoint does not preserve the document-to-HTML edge."
}
$bodyNodes = @(
    $fixtureDomNodes |
        Where-Object {
            $_.payload.nodeType -eq "element" -and
            $_.payload.nodeName -eq "BODY" -and
            $null -ne $_.payload.parentNodeId
        }
)
if ($bodyNodes.Count -ne 1) {
    throw "The fixture DOM checkpoint does not contain one BODY element."
}
$fixturePostMutationStarts = @(
    $postMutationCheckpointStarts |
        Where-Object {
            $_.payload.context.browserInstanceId -eq
                $listener.context.browserInstanceId -and
            $_.payload.context.processId -eq $listener.context.processId -and
            $_.payload.context.documentId -eq $listener.context.documentId
        }
)
if ($fixturePostMutationStarts.Count -lt 1) {
    throw "The fixture document did not produce a post-mutation checkpoint."
}
# Attribute and character-data mutations queue checkpoints of their own, so the
# structural checkpoint is identified by the node it added rather than by being
# the only one.
$parserDivNodeIds = @(
    $fixtureDomNodes |
        Where-Object {
            $_.payload.nodeType -eq "element" -and
            $_.payload.nodeName -eq "DIV"
        } |
        ForEach-Object { $_.payload.nodeId }
)
$postMutationCheckpointStart = $fixturePostMutationStarts |
    Where-Object {
        $candidateId = $_.payload.checkpointId
        @(
            $domCheckpointNodes |
                Where-Object {
                    $_.payload.checkpointId -eq $candidateId -and
                    $_.payload.nodeType -eq "element" -and
                    $_.payload.nodeName -eq "DIV" -and
                    $_.payload.nodeId -notin $parserDivNodeIds
                }
        ).Count -eq 1
    } |
    Select-Object -First 1
if (-not $postMutationCheckpointStart) {
    throw "No post-mutation checkpoint contained the appended fixture node."
}
$postMutationCheckpointId = $postMutationCheckpointStart.payload.checkpointId
if ($postMutationCheckpointId -eq $domCheckpointId) {
    throw "The post-mutation checkpoint reused the parser checkpoint identity."
}
$postMutationDomNodes = @(
    $domCheckpointNodes |
        Where-Object {
            $_.payload.checkpointId -eq $postMutationCheckpointId -and
            $_.payload.context.browserInstanceId -eq
                $listener.context.browserInstanceId -and
            $_.payload.context.processId -eq $listener.context.processId -and
            $_.payload.context.documentId -eq $listener.context.documentId
        } |
        Sort-Object { $_.payload.nodeIndex }
)
$postMutationCheckpointCompletion = $postMutationCheckpointCompletions |
    Where-Object {
        $_.payload.checkpointId -eq $postMutationCheckpointId -and
        $_.payload.context.browserInstanceId -eq
            $listener.context.browserInstanceId -and
        $_.payload.context.processId -eq $listener.context.processId -and
        $_.payload.context.documentId -eq $listener.context.documentId
    } |
    Select-Object -First 1
if (-not $postMutationCheckpointCompletion) {
    throw "The fixture post-mutation checkpoint did not complete."
}
if ($postMutationCheckpointCompletion.payload.truncated) {
    throw "The fixture post-mutation checkpoint was unexpectedly truncated."
}
if (
    $postMutationCheckpointCompletion.payload.maximumNodes -ne
        $postMutationCheckpointStart.payload.maximumNodes -or
    $postMutationCheckpointCompletion.payload.nodeCount -ne
        $postMutationDomNodes.Count
) {
    throw "The post-mutation checkpoint counts or limits are inconsistent."
}
if (
    $postMutationCheckpointStart.monotonicNanoseconds -le
        $domCheckpointCompletion.monotonicNanoseconds
) {
    throw "The post-mutation checkpoint did not follow parser completion."
}
if ($postMutationDomNodes.Count -ne ($fixtureDomNodes.Count + 2)) {
    throw "The fixture mutation did not add exactly one element and text node."
}
for ($index = 0; $index -lt $postMutationDomNodes.Count; $index++) {
    if ($postMutationDomNodes[$index].payload.nodeIndex -ne $index) {
        throw "The post-mutation checkpoint node indices are not contiguous."
    }
}
$postMutationDivNodes = @(
    $postMutationDomNodes |
        Where-Object {
            $_.payload.nodeType -eq "element" -and
            $_.payload.nodeName -eq "DIV" -and
            $_.payload.nodeId -notin @(
                $fixtureDomNodes |
                    Where-Object {
                        $_.payload.nodeType -eq "element" -and
                        $_.payload.nodeName -eq "DIV"
                    } |
                    ForEach-Object { $_.payload.nodeId }
            )
        }
)
if ($postMutationDivNodes.Count -ne 1) {
    throw "The post-mutation checkpoint did not contain one new DIV node."
}

# Attribute and character-data evidence for the fixture document. Every
# assertion below compares against the fixture's known before and after values,
# because a record that carries the right shape and the wrong text is not
# evidence.
$fixtureAttributeChanges = @(
    $attributeChanges |
        Where-Object {
            $_.payload.context.browserInstanceId -eq
                $listener.context.browserInstanceId -and
            $_.payload.context.processId -eq $listener.context.processId -and
            $_.payload.context.documentId -eq $listener.context.documentId
        }
)
$fixtureCharacterDataChanges = @(
    $characterDataChanges |
        Where-Object {
            $_.payload.context.browserInstanceId -eq
                $listener.context.browserInstanceId -and
            $_.payload.context.processId -eq $listener.context.processId -and
            $_.payload.context.documentId -eq $listener.context.documentId
        }
)
foreach ($record in @($fixtureAttributeChanges) + @($fixtureCharacterDataChanges)) {
    if ($record.payload.context.documentToken -notin $fixtureDocumentTokens) {
        throw "A DOM state change record carried an unknown document token."
    }
    if ([string]::IsNullOrWhiteSpace($record.payload.transitionId)) {
        throw "A DOM state change record did not carry its transition identity."
    }
    if ($record.payload.nodeId -le 0) {
        throw "A DOM state change record did not identify its node."
    }
}

$expectedAttributeChanges = @(
    [pscustomobject]@{
        NodeName = "BUTTON"
        AttributeName = "aria-expanded"
        ChangeType = "changed"
        PreviousValue = "false"
        Value = "true"
        Description = "an enumerated change"
    }
    [pscustomobject]@{
        NodeName = "BUTTON"
        AttributeName = "aria-labelledby"
        ChangeType = "changed"
        PreviousValue = "disclosure-name-one"
        Value = "disclosure-name-two"
        Description = "a reference change"
    }
    [pscustomobject]@{
        NodeName = "BUTTON"
        AttributeName = "aria-label"
        ChangeType = "changed"
        PreviousValue = "Fixture disclosure"
        Value = "Fixture disclosure expanded"
        Description = "a name change"
    }
    [pscustomobject]@{
        NodeName = "P"
        AttributeName = "data-removable"
        ChangeType = "removed"
        PreviousValue = "present"
        Value = $null
        Description = "an attribute removal"
    }
)
$matchedAttributeChanges = @()
foreach ($expected in $expectedAttributeChanges) {
    $matches = @(
        $fixtureAttributeChanges |
            Where-Object {
                $_.payload.attributeName -eq $expected.AttributeName -and
                $_.payload.nodeName -eq $expected.NodeName
            }
    )
    if ($matches.Count -ne 1) {
        throw (
            "The fixture did not record exactly one transition for " +
            "$($expected.AttributeName) ($($expected.Description))."
        )
    }
    $observed = $matches[0].payload
    if ($observed.changeType -ne $expected.ChangeType) {
        throw (
            "$($expected.AttributeName) was recorded as " +
            "$($observed.changeType) rather than $($expected.ChangeType)."
        )
    }
    if ($observed.attributeValueTruncated -or
        $observed.previousAttributeValueTruncated) {
        throw "$($expected.AttributeName) was unexpectedly truncated."
    }
    if ($observed.previousAttributeValue -cne $expected.PreviousValue) {
        throw (
            "$($expected.AttributeName) recorded the wrong previous value."
        )
    }
    if ($null -eq $expected.Value) {
        if ($null -ne $observed.attributeValue) {
            throw (
                "$($expected.AttributeName) was removed but recorded a value."
            )
        }
    } else {
        if ($observed.attributeValue -cne $expected.Value) {
            throw "$($expected.AttributeName) recorded the wrong value."
        }
        if ($observed.attributeValueLength -ne $expected.Value.Length) {
            throw (
                "$($expected.AttributeName) recorded an inconsistent length."
            )
        }
    }
    if ($observed.previousAttributeValueLength -ne
        $expected.PreviousValue.Length) {
        throw (
            "$($expected.AttributeName) recorded an inconsistent previous length."
        )
    }
    $matchedAttributeChanges += $matches[0]
}

$truncatedChanges = @(
    $fixtureAttributeChanges |
        Where-Object { $_.payload.attributeName -eq "data-long-value" }
)
if ($truncatedChanges.Count -ne 1) {
    throw "The fixture did not record exactly one over-length attribute."
}
$truncatedChange = $truncatedChanges[0]
$expectedTruncatedLength = 5000
if ($truncatedChange.payload.changeType -ne "added") {
    throw "The over-length attribute was not recorded as an addition."
}
if (-not $truncatedChange.payload.attributeValueTruncated) {
    throw "The over-length attribute did not report truncation."
}
if ($truncatedChange.payload.attributeValueLength -ne
    $expectedTruncatedLength) {
    throw "The over-length attribute did not report its full length."
}
$valueLimit = $truncatedChange.payload.maximumValueLength
if ($valueLimit -le 0 -or $valueLimit -ge $expectedTruncatedLength) {
    throw "The over-length attribute did not report a usable value limit."
}
if ($truncatedChange.payload.attributeValue.Length -ne $valueLimit) {
    throw "The over-length attribute was not recorded as a bounded prefix."
}
if ($truncatedChange.payload.attributeValue -cne ("A" * $valueLimit)) {
    throw "The over-length attribute prefix does not match the fixture value."
}
if ($null -ne $truncatedChange.payload.previousAttributeValue) {
    throw "The added attribute recorded a previous value."
}
$matchedAttributeChanges += $truncatedChange

$liveRegionChanges = @(
    $fixtureCharacterDataChanges |
        Where-Object {
            $_.payload.previousText -ceq "Live region before." -and
            $_.payload.text -ceq "Live region after."
        }
)
if ($liveRegionChanges.Count -ne 1) {
    throw "The fixture did not record exactly one live-region text change."
}
$liveRegionChange = $liveRegionChanges[0].payload
if ($liveRegionChange.nodeType -ne "text") {
    throw "The live-region text change was not recorded on a text node."
}
if ($liveRegionChange.textTruncated -or
    $liveRegionChange.previousTextTruncated) {
    throw "The live-region text change was unexpectedly truncated."
}
if ($liveRegionChange.textLength -ne "Live region after.".Length -or
    $liveRegionChange.previousTextLength -ne "Live region before.".Length) {
    throw "The live-region text change recorded inconsistent lengths."
}
if ($liveRegionChange.parentNodeId -le 0) {
    throw "The live-region text change did not identify its parent element."
}

# The six mutations run as consecutive statements in one task, so one later
# checkpoint in the same document must report that it covers all six. The join
# runs from the checkpoint to the transitions it covers, so a transition never
# names evidence the archive may not contain.
function Get-TransitionSequence {
    param([string] $TransitionId)

    if ($TransitionId -notmatch '^dom-transition-(\d+)$') {
        throw "A transition identity was not in the expected form: $TransitionId"
    }
    return [uint64] $Matches[1]
}

$fixtureTransitionSequences = @(
    @($matchedAttributeChanges) + @($liveRegionChanges) |
        ForEach-Object { Get-TransitionSequence $_.payload.transitionId }
)
if (@($fixtureTransitionSequences | Select-Object -Unique).Count -ne 6) {
    throw "The fixture's six transitions did not carry six distinct identities."
}
$firstFixtureSequence =
    ($fixtureTransitionSequences | Measure-Object -Minimum).Minimum
$lastFixtureSequence =
    ($fixtureTransitionSequences | Measure-Object -Maximum).Maximum

$stateCheckpointCompletion = $allCheckpointCompletions |
    Where-Object {
        $_.payload.context.browserInstanceId -eq
            $listener.context.browserInstanceId -and
        $_.payload.context.processId -eq $listener.context.processId -and
        $_.payload.context.documentId -eq $listener.context.documentId -and
        $_.payload.coveredTransitionCount -ge 6 -and
        $null -ne $_.payload.coveredTransitionFirstId -and
        $null -ne $_.payload.coveredTransitionLastId -and
        (Get-TransitionSequence $_.payload.coveredTransitionFirstId) -le
            $firstFixtureSequence -and
        (Get-TransitionSequence $_.payload.coveredTransitionLastId) -ge
            $lastFixtureSequence
    } |
    Select-Object -First 1
if (-not $stateCheckpointCompletion) {
    throw (
        "No checkpoint in the fixture document reported covering the six " +
        "transitions the fixture recorded."
    )
}
$stateCheckpointId = $stateCheckpointCompletion.payload.checkpointId

# Every completed checkpoint must state a coherent coverage range, and no
# transition may fall inside the range of a checkpoint for another document.
foreach ($completion in $allCheckpointCompletions) {
    $count = $completion.payload.coveredTransitionCount
    if ($null -eq $count -or $count -lt 0) {
        throw "A completed checkpoint did not report its transition coverage."
    }
    $hasFirst = $null -ne $completion.payload.coveredTransitionFirstId
    $hasLast = $null -ne $completion.payload.coveredTransitionLastId
    if ($count -eq 0) {
        if ($hasFirst -or $hasLast) {
            throw (
                "A checkpoint covering no transition named a transition bound."
            )
        }
        continue
    }
    if (-not $hasFirst -or -not $hasLast) {
        throw "A checkpoint covering transitions did not name both bounds."
    }
    if ((Get-TransitionSequence $completion.payload.coveredTransitionFirstId) -gt
        (Get-TransitionSequence $completion.payload.coveredTransitionLastId)) {
        throw "A checkpoint reported an inverted transition coverage range."
    }
}
if ($stateCheckpointCompletion.payload.attributesTruncated) {
    throw "The fixture checkpoint exceeded its per-node attribute limit."
}
if ($stateCheckpointCompletion.payload.maximumAttributesPerNode -le 0 -or
    $stateCheckpointCompletion.payload.maximumValueLength -ne $valueLimit) {
    throw "The fixture checkpoint reported inconsistent attribute limits."
}
$stateCheckpointAttributes = @(
    $checkpointNodeAttributes |
        Where-Object {
            $_.payload.checkpointId -eq $stateCheckpointId -and
            $_.payload.context.browserInstanceId -eq
                $listener.context.browserInstanceId -and
            $_.payload.context.processId -eq $listener.context.processId -and
            $_.payload.context.documentId -eq $listener.context.documentId
        }
)
if ($stateCheckpointCompletion.payload.attributeCount -ne
    $stateCheckpointAttributes.Count) {
    throw "The fixture checkpoint attribute count does not match its records."
}
# Coverage across the whole archive, reported so a run states how much of its
# transition evidence a checkpoint actually accounts for. A transition in a
# document that produced no checkpoint is uncovered, which is a fact about the
# recorded page rather than a defect in the join.
$allTransitions = @(@($attributeChanges) + @($characterDataChanges))
$coverageRanges = @{}
foreach ($completion in $allCheckpointCompletions) {
    if ($completion.payload.coveredTransitionCount -le 0) {
        continue
    }
    $scope = "$($completion.payload.context.processId)|" +
        "$($completion.payload.context.documentId)"
    if (-not $coverageRanges.ContainsKey($scope)) {
        $coverageRanges[$scope] = New-Object System.Collections.ArrayList
    }
    [void] $coverageRanges[$scope].Add(
        [pscustomobject]@{
            First = Get-TransitionSequence $completion.payload.coveredTransitionFirstId
            Last = Get-TransitionSequence $completion.payload.coveredTransitionLastId
        })
}
$uncoveredTransitions = 0
foreach ($transition in $allTransitions) {
    $scope = "$($transition.payload.context.processId)|" +
        "$($transition.payload.context.documentId)"
    $sequence = Get-TransitionSequence $transition.payload.transitionId
    $covered = $false
    if ($coverageRanges.ContainsKey($scope)) {
        foreach ($range in $coverageRanges[$scope]) {
            if ($sequence -ge $range.First -and $sequence -le $range.Last) {
                $covered = $true
                break
            }
        }
    }
    if (-not $covered) {
        ++$uncoveredTransitions
    }
}

$disclosureNodeId = $truncatedChange.payload.nodeId
$expectedCheckpointState = @{
    "aria-expanded" = "true"
    "aria-label" = "Fixture disclosure expanded"
    "aria-labelledby" = "disclosure-name-two"
}
foreach ($attributeName in $expectedCheckpointState.Keys) {
    $matches = @(
        $stateCheckpointAttributes |
            Where-Object {
                $_.payload.nodeId -eq $disclosureNodeId -and
                $_.payload.attributeName -eq $attributeName
            }
    )
    if ($matches.Count -ne 1) {
        throw (
            "The fixture checkpoint did not record one $attributeName " +
            "attribute for the changed element."
        )
    }
    if ($matches[0].payload.attributeValue -cne
        $expectedCheckpointState[$attributeName]) {
        throw (
            "The fixture checkpoint recorded a stale $attributeName value."
        )
    }
    if ($matches[0].payload.attributeValueTruncated) {
        throw "The fixture checkpoint truncated a short attribute value."
    }
}
$checkpointLongValues = @(
    $stateCheckpointAttributes |
        Where-Object {
            $_.payload.nodeId -eq $disclosureNodeId -and
            $_.payload.attributeName -eq "data-long-value"
        }
)
if ($checkpointLongValues.Count -ne 1) {
    throw "The fixture checkpoint did not record the over-length attribute."
}
if (-not $checkpointLongValues[0].payload.attributeValueTruncated -or
    $checkpointLongValues[0].payload.attributeValueLength -ne
        $expectedTruncatedLength -or
    $checkpointLongValues[0].payload.attributeValue.Length -ne $valueLimit) {
    throw (
        "The fixture checkpoint did not report the over-length attribute as " +
        "a bounded prefix of a longer value."
    )
}
$removedInCheckpoint = @(
    $stateCheckpointAttributes |
        Where-Object { $_.payload.attributeName -eq "data-removable" }
)
if ($removedInCheckpoint.Count -ne 0) {
    throw "The fixture checkpoint still reported the removed attribute."
}
$attributeIndicesByNode = @{}
foreach ($record in $stateCheckpointAttributes) {
    $nodeKey = $record.payload.nodeId
    if (-not $attributeIndicesByNode.ContainsKey($nodeKey)) {
        $attributeIndicesByNode[$nodeKey] = @()
    }
    $attributeIndicesByNode[$nodeKey] += $record.payload.attributeIndex
}
foreach ($nodeKey in $attributeIndicesByNode.Keys) {
    $indices = @($attributeIndicesByNode[$nodeKey] | Sort-Object)
    for ($index = 0; $index -lt $indices.Count; $index++) {
        if ($indices[$index] -ne $index) {
            throw (
                "The fixture checkpoint attribute indices are not contiguous " +
                "for one node."
            )
        }
    }
}
$scheduledAnimationFrames = @(
    $animationFrameScheduleCandidates |
        Where-Object {
            $_.payload.context.browserInstanceId -eq
                $listener.context.browserInstanceId -and
            $_.payload.context.processId -eq
                $listener.context.processId -and
            $_.payload.context.documentId -eq
                $listener.context.documentId
        }
)
if ($scheduledAnimationFrames.Count -ne 2) {
    throw (
        "The fixture did not produce exactly two animation-frame schedules; " +
        "observed $($scheduledAnimationFrames.Count)."
    )
}
$scheduledIdleCallbacks = @(
    $idleCallbackScheduleCandidates |
        Where-Object {
            $_.payload.context.browserInstanceId -eq
                $listener.context.browserInstanceId -and
            $_.payload.context.processId -eq
                $listener.context.processId -and
            $_.payload.context.documentId -eq
                $listener.context.documentId
        }
)
if ($scheduledIdleCallbacks.Count -ne 2) {
    throw (
        "The fixture did not produce exactly two idle-callback schedules; " +
        "observed $($scheduledIdleCallbacks.Count)."
    )
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
$scheduledLifecycleTimeout = $scheduledLifecycleTimeouts[0]
$firedLifecycleTimeouts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-fired" -and
            $_.payload.timerId -eq
                $scheduledLifecycleTimeout.payload.timerId -and
            $_.payload.timerKind -eq "timeout" -and
            $_.payload.context.processId -eq
                $scheduledLifecycleTimeout.payload.context.processId
        }
)
$scheduledLifecycleInterval = $scheduledLifecycleIntervals[0]
$cancelledLifecycleIntervals = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-cancelled" -and
            $_.payload.timerId -eq
                $scheduledLifecycleInterval.payload.timerId -and
            $_.payload.timerKind -eq "interval" -and
            $_.payload.context.processId -eq
                $scheduledLifecycleInterval.payload.context.processId -and
            $_.payload.cancellationReason -eq "explicit-clear"
        }
)
$scheduledHiddenTimeout = $scheduledHiddenTimeouts[0]
$firedHiddenTimeouts = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-fired" -and
            $_.payload.timerId -eq
                $scheduledHiddenTimeout.payload.timerId -and
            $_.payload.timerKind -eq "timeout" -and
            $_.payload.context.processId -eq
                $scheduledHiddenTimeout.payload.context.processId -and
            $_.payload.pageLifecycleState -eq "hidden" -and
            $null -eq $_.payload.throttled
        }
)
$animationFrameIds = @(
    $scheduledAnimationFrames |
        ForEach-Object { $_.payload.timerId }
)
$firedAnimationFrames = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-fired" -and
            $_.payload.timerKind -eq "animation-frame" -and
            $_.payload.timerId -in $animationFrameIds -and
            $_.payload.context.processId -eq $listener.context.processId -and
            $_.payload.context.documentId -eq $listener.context.documentId -and
            $null -eq $_.payload.cancellationReason
        }
)
$cancelledAnimationFrames = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-cancelled" -and
            $_.payload.timerKind -eq "animation-frame" -and
            $_.payload.timerId -in $animationFrameIds -and
            $_.payload.context.processId -eq $listener.context.processId -and
            $_.payload.context.documentId -eq $listener.context.documentId -and
            $_.payload.cancellationReason -eq
                "explicit-cancel-animation-frame"
        }
)
$firedIdleCallbacks = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-fired" -and
            $_.payload.timerKind -eq "idle-callback" -and
            $_.payload.requestedDelayMilliseconds -eq 1 -and
            $_.payload.timerId -in @(
                $scheduledIdleCallbacks |
                    ForEach-Object { $_.payload.timerId }
            ) -and
            $_.payload.context.processId -eq $listener.context.processId -and
            $_.payload.context.documentId -eq $listener.context.documentId -and
            $_.payload.didTimeout -eq $true -and
            $null -eq $_.payload.cancellationReason
        }
)
$cancelledIdleCallbacks = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.timer" -and
            $_.eventType -eq "timer-cancelled" -and
            $_.payload.timerKind -eq "idle-callback" -and
            $_.payload.requestedDelayMilliseconds -eq 5000 -and
            $_.payload.timerId -in @(
                $scheduledIdleCallbacks |
                    ForEach-Object { $_.payload.timerId }
            ) -and
            $_.payload.context.processId -eq $listener.context.processId -and
            $_.payload.context.documentId -eq $listener.context.documentId -and
            $_.payload.cancellationReason -eq
                "explicit-cancel-idle-callback" -and
            $null -eq $_.payload.didTimeout
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
if ($firedLifecycleTimeouts.Count -ne 1) {
    throw "The lifecycle timeout did not produce one correlated firing."
}
if ($cancelledLifecycleIntervals.Count -ne 1) {
    throw "The lifecycle interval did not produce one correlated cancellation."
}
if ($firedHiddenTimeouts.Count -ne 1) {
    throw "The hidden-page timeout did not produce one correlated firing."
}
$rendererSchedulerDeferrals = @(
    $schedulerDeferrals |
        Where-Object {
            $_.payload.context.browserInstanceId -eq
                $listener.context.browserInstanceId -and
            $_.payload.context.processId -eq $listener.context.processId -and
            $_.payload.context.processType -eq "renderer" -and
            $null -eq $_.payload.context.documentId
        }
)
if ($rendererSchedulerDeferrals.Count -lt 1) {
    throw "No scheduler deferral was recorded in the fixture renderer."
}
foreach ($schedulerDeferral in $rendererSchedulerDeferrals) {
    $desired = [Int64]::Parse(
        [string] $schedulerDeferral.payload.desiredWakeUpTicks
    )
    $allowed = [Int64]::Parse(
        [string] $schedulerDeferral.payload.allowedWakeUpTicks
    )
    if ($allowed -le $desired) {
        throw "A scheduler deferral did not advance the allowed wake-up."
    }
}
if ($scheduledLifecycleTimeout.payload.pageLifecycleState -ne "visible") {
    throw (
        "The lifecycle timeout was not scheduled while the fixture was visible."
    )
}
if ($firedLifecycleTimeouts[0].payload.pageLifecycleState -ne "hidden") {
    throw "The lifecycle timeout did not enter while the fixture was hidden."
}
if ($scheduledLifecycleInterval.payload.pageLifecycleState -ne "visible") {
    throw (
        "The lifecycle interval was not scheduled while the fixture was visible."
    )
}
if ($cancelledLifecycleIntervals[0].payload.pageLifecycleState -ne "hidden") {
    throw "The lifecycle interval was not cancelled while the fixture was hidden."
}
foreach ($lifecycleRecord in @(
        $scheduledLifecycleTimeout,
        $firedLifecycleTimeouts[0],
        $scheduledLifecycleInterval,
        $cancelledLifecycleIntervals[0]
    )) {
    if ($null -ne $lifecycleRecord.payload.throttled) {
        throw (
            "Lifecycle evidence inferred throttling without observing a " +
            "scheduler policy decision."
        )
    }
}
if ($firedAnimationFrames.Count -ne 1) {
    throw (
        "The fixture did not produce exactly one correlated animation-frame " +
        "callback entry."
    )
}
if ($cancelledAnimationFrames.Count -ne 1) {
    throw (
        "The fixture did not produce exactly one correlated " +
        "cancelAnimationFrame record."
    )
}
if ($firedIdleCallbacks.Count -ne 1) {
    throw (
        "The fixture did not produce exactly one timed-out idle-callback " +
        "entry."
    )
}
if ($cancelledIdleCallbacks.Count -ne 1) {
    throw (
        "The fixture did not produce exactly one correlated " +
        "cancelIdleCallback record."
    )
}
if (
    $firedIdleCallbacks[0].payload.timerId -eq
    $cancelledIdleCallbacks[0].payload.timerId
) {
    throw "The fired and cancelled idle-callback identities were not distinct."
}
if (
    $firedAnimationFrames[0].payload.timerId -eq
    $cancelledAnimationFrames[0].payload.timerId
) {
    throw "The fired and cancelled animation-frame identities were not distinct."
}
$firedAnimationFrameSchedule = $scheduledAnimationFrames |
    Where-Object {
        $_.payload.timerId -eq $firedAnimationFrames[0].payload.timerId
    } |
    Select-Object -First 1
$cancelledAnimationFrameSchedule = $scheduledAnimationFrames |
    Where-Object {
        $_.payload.timerId -eq $cancelledAnimationFrames[0].payload.timerId
    } |
    Select-Object -First 1
if (
    $firedAnimationFrames[0].monotonicNanoseconds -lt
    $firedAnimationFrameSchedule.monotonicNanoseconds
) {
    throw "The animation-frame callback entered before it was scheduled."
}
if (
    $cancelledAnimationFrames[0].monotonicNanoseconds -lt
    $cancelledAnimationFrameSchedule.monotonicNanoseconds
) {
    throw "The animation-frame callback was cancelled before it was scheduled."
}
$firedIdleCallbackSchedule = $scheduledIdleCallbacks |
    Where-Object {
        $_.payload.timerId -eq $firedIdleCallbacks[0].payload.timerId
    } |
    Select-Object -First 1
$cancelledIdleCallbackSchedule = $scheduledIdleCallbacks |
    Where-Object {
        $_.payload.timerId -eq $cancelledIdleCallbacks[0].payload.timerId
    } |
    Select-Object -First 1
if (
    $firedIdleCallbacks[0].monotonicNanoseconds -lt
    $firedIdleCallbackSchedule.monotonicNanoseconds
) {
    throw "The idle callback entered before it was scheduled."
}
if (
    $cancelledIdleCallbacks[0].monotonicNanoseconds -lt
    $cancelledIdleCallbackSchedule.monotonicNanoseconds
) {
    throw "The idle callback was cancelled before it was scheduled."
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

$crossDocumentNavigation = $navigationCompletions |
    Where-Object {
        $_.payload.navigationKind -eq "cross-document" -and
        $_.payload.sameDocument -eq $false
    } |
    Select-Object -First 1
$sameDocumentNavigation = $navigationCompletions |
    Where-Object {
        $_.payload.navigationKind -eq "same-document" -and
        $_.payload.sameDocument -eq $true
    } |
    Select-Object -First 1
if (-not $crossDocumentNavigation) {
    throw "No committed cross-document fixture navigation was recorded."
}
if (-not $sameDocumentNavigation) {
    throw "No committed same-document fixture navigation was recorded."
}
foreach ($navigation in @(
        $crossDocumentNavigation,
        $sameDocumentNavigation
    )) {
    $matchingStart = $navigationStarts |
        Where-Object {
            $_.payload.navigationId -eq $navigation.payload.navigationId -and
            $_.payload.context.pageId -eq $navigation.payload.context.pageId -and
            $_.payload.context.frameId -eq $navigation.payload.context.frameId
        } |
        Select-Object -First 1
    if (-not $matchingStart) {
        throw "A committed navigation has no correlated start record."
    }
    if (
        $matchingStart.monotonicNanoseconds -gt
        $navigation.monotonicNanoseconds
    ) {
        throw "A navigation completion preceded its start record."
    }
}
if (
    $crossDocumentNavigation.payload.context.processType -ne "browser" -or
    $sameDocumentNavigation.payload.context.processType -ne "browser"
) {
    throw "Navigation evidence did not originate in the browser process."
}
if (
    $crossDocumentNavigation.payload.context.pageId -ne
    $sameDocumentNavigation.payload.context.pageId -or
    $crossDocumentNavigation.payload.context.frameId -ne
    $sameDocumentNavigation.payload.context.frameId
) {
    throw "The fixture navigation records did not preserve page and frame identity."
}
if (
    [string]::IsNullOrWhiteSpace(
        $crossDocumentNavigation.payload.context.documentId
    ) -or
    $crossDocumentNavigation.payload.context.documentId -ne
    $sameDocumentNavigation.payload.context.documentId
) {
    throw "Same-document navigation did not preserve committed document identity."
}
if (
    $crossDocumentNavigation.payload.navigationId -eq
    $sameDocumentNavigation.payload.navigationId
) {
    throw "Distinct fixture navigations reused one navigation identifier."
}

$subframeNavigation = $subframeNavigationCompletions[0]
$matchingSubframeStart = $subframeNavigationStarts |
    Where-Object {
        $_.payload.navigationId -eq
            $subframeNavigation.payload.navigationId -and
        $_.payload.context.pageId -eq
            $subframeNavigation.payload.context.pageId -and
        $_.payload.context.frameId -eq
            $subframeNavigation.payload.context.frameId
    } |
    Select-Object -First 1
if (-not $matchingSubframeStart) {
    throw "The child-frame navigation has no correlated start record."
}
if (
    $matchingSubframeStart.monotonicNanoseconds -gt
    $subframeNavigation.monotonicNanoseconds
) {
    throw "The child-frame navigation completion preceded its start record."
}
if (
    $subframeNavigation.payload.context.pageId -ne
    $crossDocumentNavigation.payload.context.pageId
) {
    throw "The child frame did not retain the primary page identity."
}
if (
    $subframeNavigation.payload.context.frameId -eq
    $crossDocumentNavigation.payload.context.frameId
) {
    throw "The child frame reused the primary main-frame identity."
}
if (
    $subframeNavigation.payload.parentFrameId -ne
        $crossDocumentNavigation.payload.context.frameId -or
    $subframeNavigation.payload.parentOrOuterDocumentFrameId -ne
        $crossDocumentNavigation.payload.context.frameId
) {
    throw "The child frame did not identify the primary frame as its owner."
}
if (
    [string]::IsNullOrWhiteSpace(
        $subframeNavigation.payload.context.documentId
    ) -or
    $subframeNavigation.payload.context.documentId -eq
        $crossDocumentNavigation.payload.context.documentId
) {
    throw "The child frame did not receive a distinct document identity."
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
    ScheduledAnimationFrames = $scheduledAnimationFrames.Count
    FiredAnimationFrames = $firedAnimationFrames.Count
    CancelledAnimationFrames = $cancelledAnimationFrames.Count
    ScheduledIdleCallbacks = $scheduledIdleCallbacks.Count
    FiredIdleCallbacks = $firedIdleCallbacks.Count
    CancelledIdleCallbacks = $cancelledIdleCallbacks.Count
    FiredIdleCallbackDidTimeout = $firedIdleCallbacks[0].payload.didTimeout
    LifecycleTimeoutScheduledState =
        $scheduledLifecycleTimeout.payload.pageLifecycleState
    LifecycleTimeoutFiredState =
        $firedLifecycleTimeouts[0].payload.pageLifecycleState
    LifecycleIntervalScheduledState =
        $scheduledLifecycleInterval.payload.pageLifecycleState
    LifecycleIntervalCancelledState =
        $cancelledLifecycleIntervals[0].payload.pageLifecycleState
    LifecycleThrottlingObserved =
        $firedLifecycleTimeouts[0].payload.throttled
    SchedulerDeferrals = $rendererSchedulerDeferrals.Count
    SchedulerQueueName = $rendererSchedulerDeferrals[0].payload.queueName
    SchedulerThrottlingType =
        $rendererSchedulerDeferrals[0].payload.throttlingType
    SchedulerDecisionBoundary =
        $rendererSchedulerDeferrals[0].payload.decisionBoundary
    NavigationStarts = $navigationStarts.Count
    NavigationCompletions = $navigationCompletions.Count
    SubframeNavigationStarts = $subframeNavigationStarts.Count
    SubframeNavigationCompletions = $subframeNavigationCompletions.Count
    PageId = $crossDocumentNavigation.payload.context.pageId
    FrameId = $crossDocumentNavigation.payload.context.frameId
    NavigationDocumentId =
        $crossDocumentNavigation.payload.context.documentId
    CrossDocumentNavigationId =
        $crossDocumentNavigation.payload.navigationId
    SameDocumentNavigationId =
        $sameDocumentNavigation.payload.navigationId
    SubframeNavigationId = $subframeNavigation.payload.navigationId
    SubframeFrameId = $subframeNavigation.payload.context.frameId
    SubframeDocumentId = $subframeNavigation.payload.context.documentId
    SubframeParentFrameId = $subframeNavigation.payload.parentFrameId
    AccessibilityCheckpoints = $fixtureAccessibilityStarts.Count
    AccessibilityCheckpointNodes = $fixtureAccessibilityNodes.Count
    AccessibilityButtonRecords = $fixtureAccessibilityButtons.Count
    AccessibilityCheckpointsTruncated = @(
        $fixtureAccessibilityCompletions |
            Where-Object { $_.payload.truncated }
    ).Count
    DomCheckpoints = $domCheckpointStarts.Count
    DomCheckpointId = $domCheckpointId
    DomCheckpointNodes = $fixtureDomNodes.Count
    DomCheckpointTruncated = $domCheckpointCompletion.payload.truncated
    PostMutationCheckpoints = $fixturePostMutationStarts.Count
    PostMutationCheckpointId = $postMutationCheckpointId
    PostMutationCheckpointNodes = $postMutationDomNodes.Count
    StateCheckpointId = $stateCheckpointId
    StateCheckpointCoveredTransitions =
        $stateCheckpointCompletion.payload.coveredTransitionCount
    RecordedTransitions = $allTransitions.Count
    UncoveredTransitions = $uncoveredTransitions
    HiddenTimeoutTimerId = $scheduledHiddenTimeout.payload.timerId
    TimeoutTimerId = $scheduledTimeout.payload.timerId
    IntervalTimerId = $scheduledInterval.payload.timerId
    FiredAnimationFrameId = $firedAnimationFrames[0].payload.timerId
    CancelledAnimationFrameId = $cancelledAnimationFrames[0].payload.timerId
    FiredIdleCallbackId = $firedIdleCallbacks[0].payload.timerId
    CancelledIdleCallbackId = $cancelledIdleCallbacks[0].payload.timerId
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
    "Blink propagation, listener, default-handler, DOM timer, " +
    "animation-frame, idle-callback, page-lifecycle, and scheduler-decision " +
    "frame/page navigation-identity, parser-complete DOM checkpoint, and " +
    "coalesced post-mutation DOM checkpoint, and correlated renderer " +
    "accessibility serialization checkpoint " +
    "evidence verified."
)

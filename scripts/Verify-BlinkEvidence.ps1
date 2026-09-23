[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SessionPath,

    # The origin the run script served the cookie logging fixture from, ending
    # in a slash.
    [Parameter(Mandatory = $true)]
    [string] $CookieFixtureUri,

    # The value every fixture cookie carried, which no record may contain.
    [Parameter(Mandatory = $true)]
    [string] $CookieValue,

    # The URL the run script served the interaction logging fixture from.
    [Parameter(Mandatory = $true)]
    [string] $InteractionFixtureUri
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

# The window is an EventTarget that is not a Node, so its records carry no node
# identifier and are selected by kind instead. The interface name is Blink's own
# token for the build under test and is not used as a selector.
$windowClickListeners = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-registered" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.target.kind -eq "window"
        }
)

$windowResizeListeners = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-registered" -and
            $_.payload.eventName -eq "resize" -and
            $_.payload.target.kind -eq "window"
        }
)

$windowResizeRemovals = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-removed" -and
            $_.payload.eventName -eq "resize" -and
            $_.payload.target.kind -eq "window"
        }
)

# Blink creates a listener from an inline content attribute, from an on-event
# property assignment, and from an addEventListener call through one internal
# registration path, so each form is selected by the kind the bridge reports.
$inlineAttributeListeners = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-registered" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.target.elementId -eq "inline-handler" -and
            $_.payload.registrationKind -eq "inline-attribute"
        }
)

$eventHandlerPropertyListeners = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-registered" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.target.elementId -eq "property-handler" -and
            $_.payload.registrationKind -eq "event-handler-property"
        }
)

# The external script registers its listener from a file that is not the
# document, so a recorded location that names that file came from the script
# that made the call and not from the document being parsed.
$externalScriptListeners = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-registered" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.target.elementId -eq "external-script-handler"
        }
)

# Reassigning an on-event property over an existing registration replaces the
# callback in place, so Blink reports neither an addition nor a removal.
$inlineAttributeReplacements = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-callback-replaced" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.target.elementId -eq "inline-handler"
        }
)

$propertyListenerReplacements = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-callback-replaced" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.target.elementId -eq "property-handler"
        }
)

$linkDispatches = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dispatch" -and
            $_.eventType -eq "dispatch-started" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.originalTarget.elementId -eq "default-action-link"
        }
)

$windowClickInvocations = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.dispatch" -and
            $_.eventType -eq "listener-invoked" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.originalTarget.elementId -eq "default-action-link" -and
            $_.payload.currentTarget.kind -eq "window"
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
        if (
            [string]::IsNullOrWhiteSpace(
                $checkpointNodes[$index].payload.roleName
            )
        ) {
            throw (
                "An accessibility checkpoint node did not record a role name. " +
                "The numeric role alone is not a stable identity across " +
                "Chromium versions."
            )
        }
        [void] $fixtureAccessibilityNodes.Add($checkpointNodes[$index])
    }
    [void] $fixtureAccessibilityCompletions.Add($checkpointCompletion)
}
# The role is matched on the recorded role name, which is Chromium's own stable
# role token. It is not matched on the numeric role, whose ordinals shift
# between Chromium versions, and not on the serialized properties, which are a
# readable debug representation rather than a field contract.
$fixtureAccessibilityButtons = @(
    $fixtureAccessibilityNodes |
        Where-Object {
            $_.payload.roleName -eq "button" -and
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
if ($windowClickListeners.Count -lt 1) {
    throw "No click listener registration was recorded for the window."
}
if ($windowResizeListeners.Count -lt 1) {
    throw "No resize listener registration was recorded for the window."
}
if ($windowResizeRemovals.Count -lt 1) {
    throw "No resize listener removal was recorded for the window."
}
if ($linkDispatches.Count -lt 1) {
    throw "No click dispatch start was recorded for #default-action-link."
}
if ($windowClickInvocations.Count -lt 1) {
    throw (
        "No click listener invocation with the window as its current target " +
        "was recorded."
    )
}
if ($suppressedDefaultActions.Count -lt 1) {
    throw "No suppressed default handler was recorded for #pointer-only."
}
if ($inlineAttributeListeners.Count -lt 1) {
    throw (
        "No inline content attribute registration was recorded for " +
        "#inline-handler."
    )
}
if ($eventHandlerPropertyListeners.Count -lt 1) {
    throw (
        "No event handler property registration was recorded for " +
        "#property-handler."
    )
}
if ($externalScriptListeners.Count -lt 1) {
    throw (
        "No registration was recorded for #external-script-handler."
    )
}
if ($inlineAttributeReplacements.Count -lt 1) {
    throw (
        "No callback replacement was recorded for the inline content " +
        "attribute registration on #inline-handler."
    )
}
if ($propertyListenerReplacements.Count -lt 1) {
    throw (
        "No callback replacement was recorded for the event handler property " +
        "registration on #property-handler."
    )
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

# The fixture can be loaded by more than one renderer in a single validation
# run, so each window collection is narrowed to the document the fixture's node
# listener was registered in before any identity is compared. Records from a
# second load of the same fixture are a different window in a different process
# and their target identifiers are unrelated.
$fixtureDocumentId = $listener.context.documentId
$fixtureProcessId = $listener.context.processId
$inFixtureDocument = {
    $_.payload.context.documentId -eq $fixtureDocumentId -and
    $_.payload.context.processId -eq $fixtureProcessId
}
$windowClickListeners = @($windowClickListeners | Where-Object $inFixtureDocument)
$windowResizeListeners = @($windowResizeListeners | Where-Object $inFixtureDocument)
$windowResizeRemovals = @($windowResizeRemovals | Where-Object $inFixtureDocument)
$linkDispatches = @($linkDispatches | Where-Object $inFixtureDocument)
$windowClickInvocations = @(
    $windowClickInvocations | Where-Object $inFixtureDocument
)
if ($windowClickListeners.Count -lt 1) {
    throw (
        "No click listener registration was recorded for the window of the " +
        "document the fixture's node listener was registered in."
    )
}
if ($windowResizeListeners.Count -lt 1) {
    throw (
        "No resize listener registration was recorded for the fixture " +
        "document's window."
    )
}
if ($windowResizeRemovals.Count -lt 1) {
    throw (
        "No resize listener removal was recorded for the fixture document's " +
        "window."
    )
}
if ($linkDispatches.Count -lt 1) {
    throw (
        "No #default-action-link click dispatch was recorded in the fixture " +
        "document."
    )
}
if ($windowClickInvocations.Count -lt 1) {
    throw (
        "No window listener invocation was recorded for the fixture " +
        "document's #default-action-link click."
    )
}
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
# Coverage across the whole archive. Every transition is accounted for as one of
# three cases rather than reported as a bare uncovered count. A transition in a
# document that completed no delivery pass is uncovered because no pass ever ran
# there, and a transition recorded after a document's last completed pass is
# uncovered because no later pass ran before the capture ended. Both are facts
# about the recorded page. A transition that lies inside the span the document's
# own passes already claimed is a hole in the coverage rather than a fact about
# the page, so it fails the run, as does a document whose passes claim the same
# transition twice.
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
# Two passes in one document may not claim the same transition, so the ranges of
# a document are required to be disjoint and are read in order below.
$coverageLast = @{}
foreach ($scope in @($coverageRanges.Keys)) {
    $ordered = @($coverageRanges[$scope] | Sort-Object First, Last)
    for ($index = 1; $index -lt $ordered.Count; ++$index) {
        if ($ordered[$index].First -le $ordered[$index - 1].Last) {
            throw (
                "Two delivery passes in one document claimed overlapping " +
                "transition ranges, $($ordered[$index - 1].First) to " +
                "$($ordered[$index - 1].Last) and $($ordered[$index].First) " +
                "to $($ordered[$index].Last)."
            )
        }
    }
    $coverageRanges[$scope] = $ordered
    $coverageLast[$scope] = $ordered[$ordered.Count - 1].Last
}
$uncoveredTransitions = 0
$uncoveredWithoutPass = 0
$uncoveredAfterLastPass = 0
$uncoveredScopes = @{}
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
    if ($covered) {
        continue
    }
    ++$uncoveredTransitions
    $uncoveredScopes[$scope] = $true
    if (-not $coverageRanges.ContainsKey($scope)) {
        ++$uncoveredWithoutPass
        continue
    }
    if ($sequence -gt $coverageLast[$scope]) {
        ++$uncoveredAfterLastPass
        continue
    }
    throw (
        "Transition $($transition.payload.transitionId) is not covered by any " +
        "delivery pass even though its own document covered transitions up to " +
        "$($coverageLast[$scope]), so the coverage its passes report has a hole."
    )
}
if (($uncoveredWithoutPass + $uncoveredAfterLastPass) -ne
    $uncoveredTransitions) {
    throw "The uncovered transitions were not fully accounted for."
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
$inlineAttributeListener = $inlineAttributeListeners[0].payload
$eventHandlerPropertyListener = $eventHandlerPropertyListeners[0].payload
$inlineAttributeReplacement = $inlineAttributeReplacements[0].payload
$propertyListenerReplacement = $propertyListenerReplacements[0].payload
# A replacement carries the identity of the registration whose callback Blink
# swapped, so the record has to reference the same listener rather than a new
# one, and it reports the form of the callback Blink now holds.
if ($inlineAttributeReplacement.listenerId -ne
        $inlineAttributeListener.listenerId) {
    throw (
        "The inline attribute callback replacement does not reference the " +
        "registration it replaced."
    )
}
if ($propertyListenerReplacement.listenerId -ne
        $eventHandlerPropertyListener.listenerId) {
    throw (
        "The property callback replacement does not reference the " +
        "registration it replaced."
    )
}
if ($inlineAttributeReplacement.registrationKind -ne
        "event-handler-property") {
    throw (
        "The inline attribute callback replacement did not report the " +
        "replacing on-event property assignment."
    )
}
if ($propertyListenerReplacement.registrationKind -ne
        "event-handler-property") {
    throw (
        "The property callback replacement did not report an on-event " +
        "property assignment."
    )
}
$externalScriptListener = $externalScriptListeners[0].payload
# The location describes the call that produced the record. The external script
# makes its call from a file the document does not share, inside a named
# function, so both facts are checked against values the fixture fixes. The
# line and column are only required to be reported, because Blink's own
# numbering is not asserted here.
$externalScriptLocation = $externalScriptListener.location
if ($null -eq $externalScriptLocation) {
    throw (
        "The external script registration reported no location."
    )
}
if ($externalScriptLocation.url -notlike "*blink-listener-registration.js") {
    throw (
        "The external script registration reported the location of " +
        "$($externalScriptLocation.url) rather than the script that made " +
        "the call."
    )
}
if ($externalScriptLocation.functionName -ne
        "registerExternalScriptListener") {
    throw (
        "The external script registration reported the enclosing function " +
        "$($externalScriptLocation.functionName)."
    )
}
if ($null -eq $externalScriptLocation.line -or
        $externalScriptLocation.line -lt 1) {
    throw "The external script registration reported no line."
}
if ($null -eq $externalScriptLocation.column -or
        $externalScriptLocation.column -lt 1) {
    throw "The external script registration reported no column."
}
if ([string]::IsNullOrWhiteSpace($externalScriptLocation.scriptId)) {
    throw "The external script registration reported no script identifier."
}
# The recorder does not read script text, so it must not claim a source hash.
if ($null -ne $externalScriptLocation.sourceHash) {
    throw "A listener location reported a source hash it cannot compute."
}
# The document's own script made the pointer-only registration and its removal,
# so both report the document as their script URL. The removal is made from
# inside the named handler, which is a different location from the
# registration, so a record's location has to describe its own call.
$listenerLocation = $listener.location
$removalLocation = $removal.location
foreach ($documentLocation in @($listenerLocation, $removalLocation)) {
    if ($null -eq $documentLocation) {
        throw "A pointer-only listener record reported no location."
    }
    if ($documentLocation.url -notlike "*blink-listener-dispatch.html") {
        throw (
            "A pointer-only listener record reported the location of " +
            "$($documentLocation.url) rather than the fixture document."
        )
    }
}
if ($removalLocation.functionName -ne "handleClick") {
    throw (
        "The listener removal reported the enclosing function " +
        "$($removalLocation.functionName) rather than the handler that made " +
        "the call."
    )
}
if ($removalLocation.line -eq $listenerLocation.line) {
    throw (
        "The listener removal reported the same line as the registration, " +
        "so a record's location does not describe its own call."
    )
}
# Every listener record reports the world its callback belongs to, and Blink
# holds that world on the callback. A listener Blink installed itself is not
# script based and belongs to no world, so it reports a null world and a null
# execution world identity rather than claiming the main world. The fixture's own
# script runs in the main world, which Blink numbers 0 and holds no name or
# stable identifier for, and the validation script registers one listener from a
# world DevTools created, which is the only registration here that can report a
# world other than the main world.
$listenerRecords = @(
    $records |
        Where-Object { $_.channel -eq "browser.listener" }
)
$listenerWorldRecords = @(
    $listenerRecords |
        Where-Object { $null -ne $_.payload.world }
)
if ($listenerWorldRecords.Count -eq 0) {
    throw "No listener record reported the world its callback belongs to."
}
foreach ($listenerRecord in $listenerRecords) {
    $world = $listenerRecord.payload.world
    $recordedWorldId = $listenerRecord.payload.context.executionWorldId
    if ($null -eq $world) {
        if ($null -ne $recordedWorldId) {
            throw (
                "A listener record that observed no world reported the " +
                "execution world identity $recordedWorldId."
            )
        }
        continue
    }
    $expectedWorldId = "world-$($world.blinkWorldId)"
    if ($recordedWorldId -ne $expectedWorldId) {
        throw (
            "A listener record reported world $($world.blinkWorldId) with the " +
            "execution world identity $recordedWorldId rather than " +
            "$expectedWorldId."
        )
    }
    if ($world.kind -eq "main") {
        if ($world.blinkWorldId -ne 0) {
            throw (
                "A main-world listener record reported Blink world " +
                "$($world.blinkWorldId) rather than 0."
            )
        }
        if ($null -ne $world.name -or $null -ne $world.stableId) {
            throw (
                "A main-world listener record reported a name or stable " +
                "identifier Blink holds only for other worlds."
            )
        }
        continue
    }
    # Only the registration the validation script makes over DevTools comes from
    # a world other than the main world, and DevTools worlds are the
    # inspector's.
    if ($world.kind -ne "inspector-isolated") {
        throw (
            "A $($listenerRecord.eventType) record reported the world kind " +
            "$($world.kind), which nothing in this validation creates."
        )
    }
    if ($listenerRecord.payload.target.elementId -ne "isolated-world-target") {
        throw (
            "A non-main-world listener was recorded on " +
            "$($listenerRecord.payload.target.elementId) rather than on the " +
            "element the isolated-world script registers on."
        )
    }
}
# The fixture's own registrations, removals, and callback replacements are all
# made by the document's script, which runs in the main world. These are read
# from the record selections rather than from the single payloads the later
# assertions bind, so this check does not depend on where in this script it runs.
$pageScriptListenerRecords = @(
    $listeners +
    $removals +
    $windowClickListeners +
    $windowResizeListeners +
    $windowResizeRemovals +
    $inlineAttributeListeners +
    $eventHandlerPropertyListeners +
    $externalScriptListeners +
    $inlineAttributeReplacements +
    $propertyListenerReplacements
)
if ($pageScriptListenerRecords.Count -eq 0) {
    throw "No page-script listener records were selected."
}
foreach ($pageScriptRecord in $pageScriptListenerRecords) {
    $pageScriptListener = $pageScriptRecord.payload
    if ($null -eq $pageScriptListener.world) {
        throw (
            "A page-script $($pageScriptRecord.eventType) record for " +
            "$($pageScriptListener.eventName) on " +
            "$($pageScriptListener.target.kind) reported no world."
        )
    }
    if ($pageScriptListener.world.kind -ne "main") {
        throw (
            "A page-script $($pageScriptRecord.eventType) record for " +
            "$($pageScriptListener.eventName) reported the world kind " +
            "$($pageScriptListener.world.kind) rather than main."
        )
    }
}
# The validation script creates a world over DevTools and registers one click
# listener in it on an element no document script touches, so exactly one
# registration must report a world other than the main world. Blink creates a
# DevTools world as an inspector isolated world and sets its human readable name
# to the name the command asked for, and sets no stable identifier for it.
$isolatedWorldRegistrations = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-registered" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.target.elementId -eq "isolated-world-target"
        }
)
if ($isolatedWorldRegistrations.Count -ne 1) {
    throw (
        "$($isolatedWorldRegistrations.Count) isolated-world registrations " +
        "were recorded rather than the one the validation script makes."
    )
}
$isolatedWorldListener = $isolatedWorldRegistrations[0].payload
$isolatedWorld = $isolatedWorldListener.world
if ($null -eq $isolatedWorld) {
    throw "The isolated-world registration reported no world."
}
if ($isolatedWorld.kind -ne "inspector-isolated") {
    throw (
        "The isolated-world registration reported the world kind " +
        "$($isolatedWorld.kind) rather than inspector-isolated."
    )
}
if ($isolatedWorld.blinkWorldId -le 0) {
    throw (
        "The isolated-world registration reported Blink world " +
        "$($isolatedWorld.blinkWorldId), which is the main world or no world."
    )
}
if ($isolatedWorld.name -ne "A11yRecorderValidationWorld") {
    throw (
        "The isolated world reported the name $($isolatedWorld.name) rather " +
        "than the name the validation script asked DevTools for."
    )
}
if ($isolatedWorldListener.context.executionWorldId -ne
        "world-$($isolatedWorld.blinkWorldId)") {
    throw (
        "The isolated-world registration reported the execution world " +
        "identity $($isolatedWorldListener.context.executionWorldId) rather " +
        "than world-$($isolatedWorld.blinkWorldId)."
    )
}
if ($isolatedWorldListener.context.documentId -ne $listener.context.documentId) {
    throw (
        "The isolated-world registration was recorded against a different " +
        "document than the fixture's main-world registrations."
    )
}
# Outside the listener channel, only the cookie records written at a script's
# cookie call observe a world in this protocol: the document.cookie read and
# write records and the Cookie Store request record, which report the world
# current at the call. The interaction records report the world of the script
# that made a change, when one did. A world identity on any other record would
# be a claim the recorder cannot support.
# Not every channel carries a context, and strict mode treats reading an absent
# property as an error, so each step of the path is checked before it is read.
$hasExecutionWorldIdentity = {
    param($Record)

    $payload = $Record.PSObject.Properties["payload"]
    if (-not $payload -or $null -eq $payload.Value) {
        return $false
    }
    $context = $payload.Value.PSObject.Properties["context"]
    if (-not $context -or $null -eq $context.Value) {
        return $false
    }
    $worldId = $context.Value.PSObject.Properties["executionWorldId"]
    if (-not $worldId) {
        return $false
    }
    $null -ne $worldId.Value
}
$nonListenerWorldRecords = @(
    $records |
        Where-Object {
            $_.channel -ne "browser.listener" -and
            -not (
                $_.channel -eq "browser.cookie" -and
                $_.eventType -in @(
                    "document-cookie-read",
                    "document-cookie-write",
                    "cookie-store-request"
                )
            ) -and
            $_.channel -ne "browser.interaction" -and
            (& $hasExecutionWorldIdentity $_)
        }
)
if ($nonListenerWorldRecords.Count -gt 0) {
    throw (
        "$($nonListenerWorldRecords.Count) records outside the listener " +
        "channel, the cookie call records, and the interaction records " +
        "reported an execution world identity."
    )
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
if ($composedPath[0].kind -ne "node") {
    throw "The composed path target was not recorded as a node event target."
}
if (-not $composedPath[0].nodeId) {
    throw "The composed path target does not carry a node identifier."
}

$windowListener = $windowClickListeners[0].payload
if ([string]::IsNullOrWhiteSpace($windowListener.target.interfaceName)) {
    throw "The window listener target has no interface name."
}
if ($windowListener.target.nodeId) {
    throw "The window listener target unexpectedly carries a node identifier."
}
if ($windowListener.target.tagName) {
    throw "The window listener target unexpectedly carries a tag name."
}
if ([string]::IsNullOrWhiteSpace($windowListener.target.targetId)) {
    throw "The window listener target does not carry a target identifier."
}
if ($windowListener.target.documentId -ne $listener.context.documentId) {
    throw (
        "The window listener target does not name the document the fixture " +
        "script ran in."
    )
}
$windowResizeListener = $windowResizeListeners[0].payload
$windowResizeRemoval = $windowResizeRemovals[0].payload
if ($windowResizeRemoval.listenerId -ne $windowResizeListener.listenerId) {
    throw (
        "The window resize removal does not reference the registered window " +
        "listener."
    )
}
if ($windowResizeRemoval.target.targetId -ne
        $windowResizeListener.target.targetId) {
    throw "The window removal target does not match the registration target."
}
if ($windowResizeListener.target.targetId -ne $windowListener.target.targetId) {
    throw (
        "Two listeners registered on the same window reported different " +
        "target identifiers."
    )
}

# Blink appends the window to the end of an event path when the top node event
# context is a document, so the recorded path must end there too.
$linkDispatch = $linkDispatches[0].payload
$linkComposedPath = @($linkDispatch.composedPath)
if ($linkComposedPath.Count -lt 2) {
    throw "The #default-action-link composed path is too short to reach the window."
}
$linkPathWindow = $linkComposedPath[$linkComposedPath.Count - 1]
if ($linkPathWindow.kind -ne "window") {
    throw "The #default-action-link composed path does not end at the window."
}
# The interface name is the token Blink reports for the target in the build
# under test, and that token has changed between Chromium revisions, so it is
# required to be present and consistent rather than equal to a fixed string.
if ([string]::IsNullOrWhiteSpace($linkPathWindow.interfaceName)) {
    throw "The composed path window entry has no interface name."
}
if ($linkPathWindow.interfaceName -ne $windowListener.target.interfaceName) {
    throw (
        "The composed path window entry and the window listener target " +
        "report different interface names."
    )
}
if ($linkPathWindow.nodeId) {
    throw "The composed path window entry unexpectedly carries a node identifier."
}
if ($linkPathWindow.targetId -ne $windowListener.target.targetId) {
    throw (
        "The composed path window entry and the window listener target " +
        "report different target identifiers."
    )
}
if (@($linkComposedPath | Where-Object { $_.kind -eq "window" }).Count -ne 1) {
    throw "The composed path contains the window more than once."
}
$windowClickInvocation = $windowClickInvocations[0].payload
if ($windowClickInvocation.phase -ne "bubbling") {
    throw "The window click invocation phase was not 'bubbling'."
}
if ($windowClickInvocation.listenerId -ne $windowListener.listenerId) {
    throw (
        "The window click invocation does not reference the registered " +
        "window listener."
    )
}
if ($windowClickInvocation.currentTarget.targetId -ne
        $windowListener.target.targetId) {
    throw (
        "The window click invocation current target does not match the " +
        "registered window listener target."
    )
}
if ($windowClickInvocation.originalTarget.kind -ne "node") {
    throw (
        "The window click invocation original target was not recorded as a " +
        "node event target."
    )
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

# A browser omission record states evidence the session lost rather than
# captured. The verifier reports the loss instead of failing on it, because the
# record is a true account of what happened; whether a run that lost evidence
# can serve as a reference run is decided by the run script.
$browserOmissions = @(
    $records |
        Where-Object {
            $_.channel -like "browser.*" -and
            $_.eventType -eq "collector-omission"
        }
)
$omittedRecordCount = 0
foreach ($omission in $browserOmissions) {
    $count = 1
    if ($omission.payload.PSObject.Properties.Name -contains "count") {
        $count = [int] $omission.payload.count
    }
    $omittedRecordCount += $count
}
$omissionReasons = @(
    $browserOmissions |
        ForEach-Object { $_.payload.reason } |
        Sort-Object -Unique
)

# The run script serves a second page that uses document.cookie, the Cookie
# Store API, and Set-Cookie response headers, and passes the page's origin and
# the value every fixture cookie carries. These checks establish that the logger
# emitted a record for each of those operations, with the cookie's name and
# without its value. They say nothing about whether the page's cookie use is
# appropriate.
$cookieRecords = @(
    $records | Where-Object { $_.channel -eq "browser.cookie" }
)
$cookieValueRecords = @(
    Get-Content -LiteralPath $eventPath |
        Where-Object { $_.Contains($CookieValue) }
)
if ($cookieValueRecords.Count -gt 0) {
    throw (
        "$($cookieValueRecords.Count) record(s) contain a fixture cookie " +
        "value. Cookie records carry names and never values."
    )
}

function Select-CookieFixtureRecord {
    param(
        [Parameter(Mandatory = $true)]
        [string] $EventType,

        [Parameter(Mandatory = $true)]
        [scriptblock] $Filter,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $selected = @(
        $cookieRecords |
            Where-Object { $_.eventType -eq $EventType } |
            Where-Object $Filter
    )
    if ($selected.Count -eq 0) {
        throw "No $EventType record was emitted for $Description."
    }
    $selected[0]
}

function Test-CookieFixtureOrigin {
    param(
        [Parameter(Mandatory = $true)]
        $Record,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $payload = $Record.payload
    if ($null -eq $payload.world -or
        $payload.world.kind -ne "main" -or
        $payload.context.executionWorldId -ne
            "world-$($payload.world.blinkWorldId)") {
        throw "The $Description record did not report the main world."
    }
    if ($null -eq $payload.location -or
        -not ([string] $payload.location.url).StartsWith($CookieFixtureUri)) {
        throw (
            "The $Description record did not report a location in the " +
            "cookie logging fixture."
        )
    }
}

$documentCookieWrite = Select-CookieFixtureRecord `
    "document-cookie-write" `
    {
        $_.payload.name -eq "a11y_recorder_document" -and
        $_.payload.outcome -eq "sent-to-cookie-manager" -and
        ([string] $_.payload.cookieUrl).StartsWith($CookieFixtureUri)
    } `
    "the fixture's document.cookie write"
if ($documentCookieWrite.payload.attributes.path -ne "/" -or
    $documentCookieWrite.payload.attributes.sameSite -ne "Lax" -or
    @($documentCookieWrite.payload.attributes.attributeNames).Count -ne 2) {
    throw (
        "The document.cookie write record did not report the Path and " +
        "SameSite attributes the fixture wrote."
    )
}
Test-CookieFixtureOrigin $documentCookieWrite "document.cookie write"

$documentCookieRead = Select-CookieFixtureRecord `
    "document-cookie-read" `
    {
        $_.payload.outcome -eq "returned" -and
        ([string] $_.payload.cookieUrl).StartsWith($CookieFixtureUri) -and
        @($_.payload.cookieNames) -contains "a11y_recorder_document" -and
        @($_.payload.cookieNames) -contains "a11y_recorder_response"
    } `
    "the fixture's document.cookie read"
Test-CookieFixtureOrigin $documentCookieRead "document.cookie read"

$cookieStoreRequests = @()
foreach ($method in @("set", "get", "getAll", "delete")) {
    $request = Select-CookieFixtureRecord `
        "cookie-store-request" `
        {
            $_.payload.method -eq $method -and
            $_.payload.contextKind -eq "window" -and
            $_.payload.outcome -eq "sent-to-cookie-manager" -and
            -not [string]::IsNullOrWhiteSpace($_.payload.requestId) -and
            ($method -eq "getAll" -or
                $_.payload.name -eq "a11y_recorder_store")
        }.GetNewClosure() `
        "the fixture's cookieStore.$method call"
    Test-CookieFixtureOrigin $request "cookieStore.$method request"
    $requestId = $request.payload.requestId
    $result = Select-CookieFixtureRecord `
        "cookie-store-result" `
        {
            $_.payload.requestId -eq $requestId -and
            $_.payload.method -eq $method -and
            $_.payload.outcome -eq "resolved"
        }.GetNewClosure() `
        "the fixture's cookieStore.$method result"
    if ($method -in @("get", "getAll") -and
        @($result.payload.cookieNames) -notcontains "a11y_recorder_store") {
        throw (
            "The cookieStore.$method result record did not name the cookie " +
            "the fixture had set."
        )
    }
    if ($method -in @("set", "delete") -and $result.payload.success -ne $true) {
        throw (
            "The cookieStore.$method result record did not report the " +
            "browser's success."
        )
    }
    $cookieStoreRequests += $request
}

$cookieStoreChanges = @(
    $cookieRecords |
        Where-Object {
            $_.eventType -eq "cookie-store-change" -and
            $_.payload.name -eq "a11y_recorder_store" -and
            $_.payload.contextKind -eq "window" -and
            $_.payload.dispatched -eq $true
        }
)
if ($cookieStoreChanges.Count -lt 2) {
    throw (
        "$($cookieStoreChanges.Count) dispatched Cookie Store change " +
        "record(s) were emitted for the fixture cookie rather than one for " +
        "its insertion and one for its deletion."
    )
}

$navigationCookieAccess = Select-CookieFixtureRecord `
    "cookie-access" `
    {
        $_.payload.observer -eq "navigation" -and
        $_.payload.accessType -eq "change" -and
        ([string] $_.payload.url).StartsWith($CookieFixtureUri) -and
        @(
            $_.payload.cookies |
                Where-Object { $_.name -eq "a11y_recorder_response" }
        ).Count -gt 0
    } `
    "the fixture document's Set-Cookie response header"
$responseCookie = @(
    $navigationCookieAccess.payload.cookies |
        Where-Object { $_.name -eq "a11y_recorder_response" }
)[0]
if ($responseCookie.parsed -ne $true -or $responseCookie.path -ne "/") {
    throw (
        "The navigation cookie-access record did not report the response " +
        "cookie's attributes."
    )
}

$frameCookieChange = Select-CookieFixtureRecord `
    "cookie-access" `
    {
        $_.payload.observer -eq "frame" -and
        $_.payload.accessType -eq "change" -and
        ([string] $_.payload.url).StartsWith("$($CookieFixtureUri)set-cookie") -and
        @(
            $_.payload.cookies |
                Where-Object { $_.name -eq "a11y_recorder_fetch" }
        ).Count -gt 0
    } `
    "the fixture's fetch that received a Set-Cookie header"
$frameCookieRead = Select-CookieFixtureRecord `
    "cookie-access" `
    {
        $_.payload.observer -eq "frame" -and
        $_.payload.accessType -eq "read" -and
        ([string] $_.payload.url).StartsWith("$($CookieFixtureUri)echo") -and
        @(
            $_.payload.cookies |
                Where-Object { $_.name -eq "a11y_recorder_fetch" }
        ).Count -gt 0
    } `
    "the fixture's fetch that sent its cookies"
foreach ($frameAccess in @($frameCookieChange, $frameCookieRead)) {
    if ([string]::IsNullOrWhiteSpace(
            $frameAccess.payload.context.documentId) -or
        [string]::IsNullOrWhiteSpace(
            $frameAccess.payload.context.documentToken)) {
        throw "A frame cookie-access record reported no document identity."
    }
}

[pscustomobject]@{
    CookieRecords = $cookieRecords.Count
    DocumentCookieWriteOutcome = $documentCookieWrite.payload.outcome
    DocumentCookieReadServedFrom = $documentCookieRead.payload.servedFrom
    DocumentCookieReadNames = @($documentCookieRead.payload.cookieNames) -join ", "
    CookieStoreRequestIds = @(
        $cookieStoreRequests | ForEach-Object { $_.payload.requestId }
    ) -join ", "
    CookieStoreChangeCauses = @(
        $cookieStoreChanges | ForEach-Object { $_.payload.cause }
    ) -join ", "
    NavigationCookieAccessNames = @(
        $navigationCookieAccess.payload.cookies | ForEach-Object { $_.name }
    ) -join ", "
    FrameCookieChangeUrl = $frameCookieChange.payload.url
    FrameCookieReadUrl = $frameCookieRead.payload.url
    RecordsContainingCookieValue = $cookieValueRecords.Count
} | Format-List

# The run script serves a third page, on the cookie fixture's origin, whose
# functions move focus, set text-control values and a selection, and set an
# active descendant by element reflection, and it sends a Tab key press and
# typed text as DevTools input. These checks establish that the logger emitted
# a record for each of those changes, with the script origin for a change made
# by script and none for a change made by input. They say nothing about whether
# the page's focus handling or labelling is appropriate.
$interactionRecords = @(
    $records | Where-Object { $_.channel -eq "browser.interaction" }
)

function Select-InteractionFixtureRecord {
    param(
        [Parameter(Mandatory = $true)]
        [string] $EventType,

        [Parameter(Mandatory = $true)]
        [scriptblock] $Filter,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $selected = @(
        $interactionRecords |
            Where-Object { $_.eventType -eq $EventType } |
            Where-Object $Filter
    )
    if ($selected.Count -eq 0) {
        throw "No $EventType record was emitted for $Description."
    }
    $selected[0]
}

function Test-InteractionScriptOrigin {
    param(
        [Parameter(Mandatory = $true)]
        $Record,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $payload = $Record.payload
    if ($null -eq $payload.world -or
        $payload.world.kind -ne "main" -or
        $payload.context.executionWorldId -ne
            "world-$($payload.world.blinkWorldId)") {
        throw "The $Description record did not report the main world."
    }
    if ($null -eq $payload.location) {
        throw "The $Description record did not report a script location."
    }
}

function Test-InteractionInputOrigin {
    param(
        [Parameter(Mandatory = $true)]
        $Record,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $payload = $Record.payload
    if ($null -ne $payload.location -or $null -ne $payload.world -or
        $null -ne $payload.context.executionWorldId) {
        throw (
            "The $Description record reported a script origin for a change " +
            "made by input."
        )
    }
}

# The fixture document is identified by the record of its script focus, whose
# location names the fixture page. Every later record must name the same
# document.
$scriptFocus = Select-InteractionFixtureRecord `
    "focus-changed" `
    {
        $_.payload.focusType -eq "script" -and
        $_.payload.focusTrigger -eq "script" -and
        $_.payload.outcome -eq "focused" -and
        $_.payload.preventScroll -eq $true -and
        $null -ne $_.payload.location -and
        ([string] $_.payload.location.url).StartsWith($InteractionFixtureUri)
    } `
    "the fixture's script focus of its button"
Test-InteractionScriptOrigin $scriptFocus "script focus"
$interactionDocumentId = $scriptFocus.payload.context.documentId
$buttonNodeId = $scriptFocus.payload.focusedNodeId
$fixtureInteractionRecords = @(
    $interactionRecords |
        Where-Object { $_.payload.context.documentId -eq $interactionDocumentId }
)
$interactionRecords = $fixtureInteractionRecords

$tabFocus = Select-InteractionFixtureRecord `
    "focus-changed" `
    {
        $_.payload.focusType -eq "forward" -and
        $_.payload.focusTrigger -eq "user-gesture" -and
        $_.payload.outcome -eq "focused" -and
        $_.payload.previousNodeId -eq $buttonNodeId
    } `
    "the Tab key press that moved focus from the fixture's button"
Test-InteractionInputOrigin $tabFocus "Tab focus"
$fieldNodeId = $tabFocus.payload.focusedNodeId

$typedField = Select-InteractionFixtureRecord `
    "text-control-value-changed" `
    {
        $_.payload.nodeId -eq $fieldNodeId -and
        $_.payload.source -eq "user-edit" -and
        $_.payload.value -eq "typed" -and
        $_.payload.controlType -eq "text"
    } `
    "the text typed into the fixture's field"
Test-InteractionInputOrigin $typedField "typed field value"

$scriptField = Select-InteractionFixtureRecord `
    "text-control-value-changed" `
    {
        $_.payload.nodeId -eq $fieldNodeId -and
        $_.payload.source -eq "value-set" -and
        $_.payload.value -eq "set by script"
    } `
    "the fixture's script value set on its field"
Test-InteractionScriptOrigin $scriptField "script field value"

$scriptNotes = Select-InteractionFixtureRecord `
    "text-control-value-changed" `
    {
        $_.payload.controlType -eq "textarea" -and
        $_.payload.source -eq "value-set" -and
        $_.payload.value -eq "notes set by script"
    } `
    "the fixture's script value set on its textarea"
Test-InteractionScriptOrigin $scriptNotes "script textarea value"
$notesNodeId = $scriptNotes.payload.nodeId

$notesSelection = Select-InteractionFixtureRecord `
    "selection-changed" `
    {
        $_.payload.textControlNodeId -eq $notesNodeId -and
        $_.payload.textControlSelectionStart -eq 1 -and
        $_.payload.textControlSelectionEnd -eq 4 -and
        $_.payload.textControlSelectionDirection -eq "forward" -and
        $_.payload.selectionType -eq "range"
    } `
    "the fixture's setSelectionRange call on its textarea"
Test-InteractionScriptOrigin $notesSelection "textarea selection"

$typedNotes = Select-InteractionFixtureRecord `
    "text-control-value-changed" `
    {
        $_.payload.nodeId -eq $notesNodeId -and
        $_.payload.source -eq "user-edit" -and
        $_.payload.value -eq "nXs set by script"
    } `
    "the text typed over the fixture's textarea selection"
Test-InteractionInputOrigin $typedNotes "typed textarea value"

$activeDescendant = Select-InteractionFixtureRecord `
    "active-descendant-reference-set" `
    { $_.payload.referencedNodeId -gt 0 } `
    "the fixture's ariaActiveDescendantElement assignment"
Test-InteractionScriptOrigin $activeDescendant "active descendant"
$listboxNodeId = $activeDescendant.payload.nodeId

$listboxFocus = Select-InteractionFixtureRecord `
    "focus-changed" `
    {
        $_.payload.focusedNodeId -eq $listboxNodeId -and
        $_.payload.activeDescendantNodeId -eq
            $activeDescendant.payload.referencedNodeId
    } `
    "the fixture's focus of its listbox with an active descendant"
Test-InteractionScriptOrigin $listboxFocus "listbox focus"

$clearedFocus = Select-InteractionFixtureRecord `
    "focus-changed" `
    {
        $_.payload.previousNodeId -eq $listboxNodeId -and
        $_.payload.outcome -eq "cleared" -and
        $null -eq $_.payload.focusedNodeId
    } `
    "the fixture's blur of its listbox"
Test-InteractionScriptOrigin $clearedFocus "blur"

[pscustomobject]@{
    InteractionRecords = $interactionRecords.Count
    InteractionDocumentId = $interactionDocumentId
    ScriptFocusNodeId = $buttonNodeId
    TabFocusNodeId = $fieldNodeId
    TypedFieldValue = $typedField.payload.value
    ScriptTextareaNodeId = $notesNodeId
    TextareaSelection = (
        "$($notesSelection.payload.textControlSelectionStart)-" +
        "$($notesSelection.payload.textControlSelectionEnd)"
    )
    TypedTextareaValue = $typedNotes.payload.value
    ActiveDescendantNodeId = $activeDescendant.payload.referencedNodeId
    ListboxFocusOutcome = $listboxFocus.payload.outcome
    BlurOutcome = $clearedFocus.payload.outcome
} | Format-List

[pscustomobject]@{
    SessionPath = (Resolve-Path -LiteralPath $SessionPath).Path
    EvidenceOmissionRecords = $browserOmissions.Count
    OmittedEvidenceRecords = $omittedRecordCount
    EvidenceOmissionReasons = ($omissionReasons -join ", ")
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
    CoveredTransitions = $allTransitions.Count - $uncoveredTransitions
    UncoveredTransitions = $uncoveredTransitions
    UncoveredTransitionsWithoutPass = $uncoveredWithoutPass
    UncoveredTransitionsAfterLastPass = $uncoveredAfterLastPass
    UncoveredTransitionDocuments = $uncoveredScopes.Count
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
    WindowInterfaceName = $windowListener.target.interfaceName
    WindowTargetId = $windowListener.target.targetId
    WindowListenerId = $windowListener.listenerId
    WindowClickInvocations = $windowClickInvocations.Count
    InlineAttributeListenerId = $inlineAttributeListener.listenerId
    InlineAttributeRegistrationKind = $inlineAttributeListener.registrationKind
    EventHandlerPropertyListenerId = $eventHandlerPropertyListener.listenerId
    EventHandlerPropertyRegistrationKind =
        $eventHandlerPropertyListener.registrationKind
    InlineAttributeReplacements = $inlineAttributeReplacements.Count
    PropertyListenerReplacements = $propertyListenerReplacements.Count
    ReplacedCallbackRegistrationKind =
        $inlineAttributeReplacement.registrationKind
    ExternalScriptListenerId = $externalScriptListener.listenerId
    ExternalScriptLocationUrl = $externalScriptLocation.url
    ExternalScriptLocationFunction = $externalScriptLocation.functionName
    ExternalScriptLocationLine = $externalScriptLocation.line
    ExternalScriptLocationColumn = $externalScriptLocation.column
    ExternalScriptLocationScriptId = $externalScriptLocation.scriptId
    ListenerChannelRecords = $listenerRecords.Count
    ListenerWorldRecords = $listenerWorldRecords.Count
    IsolatedWorldListenerId = $isolatedWorldListener.listenerId
    IsolatedWorldKind = $isolatedWorld.kind
    IsolatedWorldBlinkId = $isolatedWorld.blinkWorldId
    IsolatedWorldName = $isolatedWorld.name
    IsolatedWorldStableId = $isolatedWorld.stableId
    IsolatedWorldExecutionWorldId =
        $isolatedWorldListener.context.executionWorldId
    RegistrationWorldKind = $listener.world.kind
    RegistrationBlinkWorldId = $listener.world.blinkWorldId
    RegistrationExecutionWorldId = $listener.context.executionWorldId
    RegistrationLocationUrl = $listenerLocation.url
    RegistrationLocationLine = $listenerLocation.line
    RegistrationLocationColumn = $listenerLocation.column
    RegistrationLocationFunction = $listenerLocation.functionName
    RemovalLocationFunction = $removalLocation.functionName
    RemovalLocationLine = $removalLocation.line
    ReplacementLocationFunction =
        $inlineAttributeReplacement.location.functionName
    ReplacementLocationLine = $inlineAttributeReplacement.location.line
    InlineAttributeLocationUrl = $inlineAttributeListener.location.url
    InlineAttributeLocationLine = $inlineAttributeListener.location.line
    LinkComposedPathEntries = $linkComposedPath.Count
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
    "Blink propagation, listener, window event-target, default-handler, " +
    "DOM timer, " +
    "animation-frame, idle-callback, page-lifecycle, and scheduler-decision " +
    "frame/page navigation-identity, parser-complete DOM checkpoint, and " +
    "coalesced post-mutation DOM checkpoint, and correlated renderer " +
    "accessibility serialization checkpoint, cookie operation, and " +
    "interaction-state " +
    "evidence verified."
)

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
    [string] $InteractionFixtureUri,

    # The URL the run script served the layout logging fixture from.
    [Parameter(Mandatory = $true)]
    [string] $LayoutFixtureUri,

    # What the layout fixture page reported after each of its steps, as a JSON
    # object whose Settle, Widen, and Recolor members each hold the page's own
    # JSON report of its box rectangle, viewport size, and box color.
    [Parameter(Mandatory = $true)]
    [string] $LayoutFixtureSteps,

    # The URL the run script served the shadow DOM logging fixture from.
    [Parameter(Mandatory = $true)]
    [string] $ShadowFixtureUri,

    # What the shadow DOM fixture page reported, as a JSON object whose Settle
    # member holds the page's count of nodes assigned to each slot and whose
    # Dispatch member holds the composedPath() length each of its click
    # listeners saw.
    [Parameter(Mandatory = $true)]
    [string] $ShadowFixtureSteps,

    # The URL the run script served the network logging fixture page from.
    # The page's data, script, and worker URLs all begin with it.
    [Parameter(Mandatory = $true)]
    [string] $NetworkFixtureUri,

    # The URL the network fixture tab opened, which redirects to the page.
    [Parameter(Mandatory = $true)]
    [string] $NetworkStartUri,

    # The closed loopback URL the network fixture fetched so the request fails.
    [Parameter(Mandatory = $true)]
    [string] $NetworkRefusedUri,

    # What the network fixture page reported, as its own JSON report.
    [Parameter(Mandatory = $true)]
    [string] $NetworkFixtureReport,

    # The URL of the WebTransport session the network logging fixture creates
    # to a closed loopback port.
    [Parameter(Mandatory = $true)]
    [string] $NetworkTransportUri,

    # The credential values the network fixture sent in request headers and
    # received in a response header, none of which any record may contain.
    [Parameter(Mandatory = $true)]
    [string[]] $NetworkSecretValues
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
# the only one. Checkpoint identifiers are only unique within one renderer
# process, so the candidate's nodes are narrowed to the fixture document as
# well as to the checkpoint.
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
                    $_.payload.context.browserInstanceId -eq
                        $listener.context.browserInstanceId -and
                    $_.payload.context.processId -eq $listener.context.processId -and
                    $_.payload.context.documentId -eq $listener.context.documentId -and
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
# Collector omission records share the listener channel, for example when the
# recorder cannot delete the browser's temporary profile, and carry no world.
$listenerRecords = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -ne "collector-omission"
        }
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
# current at the call. The interaction change records report the world of the
# script that made a change, when one did; the interaction checkpoint records
# are taken after a checkpoint rather than at a script's call, and report none. The network request records report the
# world of the script current when Blink issued the request, when one was, and
# the realtime records written at a script's call, which are the WebSocket
# creation, sent message, and close request records and the WebTransport
# creation and close request records, report the world current at that call. A
# world identity on any other record would be a claim the recorder cannot
# support.
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
            -not (
                $_.channel -eq "browser.interaction" -and
                $_.eventType -in @(
                    "focus-changed",
                    "selection-changed",
                    "text-control-value-changed",
                    "active-descendant-reference-set"
                )
            ) -and
            -not (
                $_.channel -eq "browser.network" -and
                $_.eventType -in @(
                    "request-will-be-sent",
                    "websocket-created",
                    "websocket-message-sent",
                    "websocket-close-requested",
                    "web-transport-created",
                    "web-transport-close-requested"
                )
            ) -and
            (& $hasExecutionWorldIdentity $_)
        }
)
if ($nonListenerWorldRecords.Count -gt 0) {
    throw (
        "$($nonListenerWorldRecords.Count) records outside the listener " +
        "channel, the cookie call records, the interaction change records, and " +
        "the network request records, and the realtime call records " +
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
    $records | Where-Object {
            $_.channel -eq "browser.cookie" -and
            $_.eventType -ne "collector-omission"
        }
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
    $records | Where-Object {
            $_.channel -eq "browser.interaction" -and
            $_.eventType -ne "collector-omission"
        }
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

# Protocol 0.29 follows each recorded DOM and layout checkpoint with a snapshot
# of the document's interaction state. These checks establish that every
# snapshot in the capture is complete and names a recorded source checkpoint
# of the same document, and that the fixture document's snapshots hold the
# state the fixture set: nothing focused and empty text controls when parsing
# finished, and, after the fixture's final step, the listbox focused with its
# active descendant and both text controls holding their last values. They say
# nothing about whether that state is appropriate for the page.
$interactionCheckpointEventTypes = @(
    "interaction-checkpoint-started",
    "interaction-checkpoint-text-control",
    "interaction-checkpoint-completed"
)
$interactionCheckpointRecords = @(
    $records | Where-Object {
        $_.channel -eq "browser.interaction" -and
        $_.eventType -in $interactionCheckpointEventTypes
    }
)
$interactionCheckpointStarts = @(
    $interactionCheckpointRecords |
        Where-Object { $_.eventType -eq "interaction-checkpoint-started" }
)
if ($interactionCheckpointStarts.Count -eq 0) {
    throw "No interaction-checkpoint-started record was emitted."
}
# Checkpoint identities are unique only within one renderer process, so records
# are grouped by browser instance, renderer process, and checkpoint identity.
function Get-InteractionCheckpointKey {
    param([Parameter(Mandatory = $true)] $CheckpointRecord)

    $checkpointContext = $CheckpointRecord.payload.context
    (
        "$($checkpointContext.browserInstanceId)|$($checkpointContext.processId)|" +
        "$($CheckpointRecord.payload.checkpointId)"
    )
}

$interactionCheckpointParts = @{}
foreach ($icRecord in $interactionCheckpointRecords) {
    $icId = Get-InteractionCheckpointKey $icRecord
    if (-not $interactionCheckpointParts.ContainsKey($icId)) {
        $interactionCheckpointParts[$icId] = [System.Collections.Generic.List[object]]::new()
    }
    $interactionCheckpointParts[$icId].Add($icRecord)
}
$sourceCheckpointStarts = @{}
foreach ($icRecord in @(
        $records | Where-Object {
            ($_.channel -eq "browser.dom" -and
                $_.eventType -eq "dom-checkpoint-started") -or
            ($_.channel -eq "browser.layout" -and
                $_.eventType -eq "layout-checkpoint-started")
        })) {
    $icKey = (
        "$($icRecord.channel)|$($icRecord.payload.context.browserInstanceId)|" +
        "$($icRecord.payload.context.processId)|$($icRecord.payload.checkpointId)"
    )
    $sourceCheckpointStarts[$icKey] = $icRecord
}

function Test-SameCheckpointDocument {
    param($Left, $Right)

    $Left.browserInstanceId -eq $Right.browserInstanceId -and
    $Left.processId -eq $Right.processId -and
    $Left.documentId -eq $Right.documentId -and
    $Left.documentToken -eq $Right.documentToken
}

foreach ($icStart in $interactionCheckpointStarts) {
    $icId = "$($icStart.payload.checkpointId) in renderer process $($icStart.payload.context.processId)"
    $icContext = $icStart.payload.context
    if ($null -ne $icContext.executionWorldId) {
        throw "Interaction checkpoint $icId reported an execution world."
    }
    $icSourceKey = (
        "$($icStart.payload.sourceChannel)|$($icContext.browserInstanceId)|" +
        "$($icContext.processId)|$($icStart.payload.sourceCheckpointId)"
    )
    if (-not $sourceCheckpointStarts.ContainsKey($icSourceKey)) {
        throw (
            "Interaction checkpoint $icId names source checkpoint " +
            "$($icStart.payload.sourceCheckpointId), which was not recorded."
        )
    }
    $icSource = $sourceCheckpointStarts[$icSourceKey]
    if (-not (Test-SameCheckpointDocument $icContext $icSource.payload.context) -or
        $icStart.payload.reason -ne $icSource.payload.reason) {
        throw (
            "Interaction checkpoint $icId does not match the document or reason " +
            "of its source checkpoint $($icStart.payload.sourceCheckpointId)."
        )
    }
    $icParts = @($interactionCheckpointParts[(Get-InteractionCheckpointKey $icStart)])
    $icStarts = @($icParts | Where-Object { $_.eventType -eq "interaction-checkpoint-started" })
    $icCompletions = @($icParts | Where-Object { $_.eventType -eq "interaction-checkpoint-completed" })
    $icControls = @($icParts | Where-Object { $_.eventType -eq "interaction-checkpoint-text-control" })
    if ($icStarts.Count -ne 1 -or $icCompletions.Count -ne 1) {
        throw (
            "Interaction checkpoint $icId has $($icStarts.Count) start and " +
            "$($icCompletions.Count) completion records."
        )
    }
    $icCompletion = $icCompletions[0]
    if ($icCompletion.payload.maximumTextControls -ne
            $icStart.payload.maximumTextControls -or
        $icCompletion.payload.textControlCount -ne $icControls.Count -or
        $icCompletion.payload.truncated) {
        throw (
            "Interaction checkpoint $icId completed with " +
            "$($icCompletion.payload.textControlCount) text controls against " +
            "$($icControls.Count) records, truncated $($icCompletion.payload.truncated)."
        )
    }
    for ($icIndex = 0; $icIndex -lt $icControls.Count; $icIndex++) {
        if ($icControls[$icIndex].payload.textControlIndex -ne $icIndex) {
            throw "Interaction checkpoint $icId has non-contiguous text-control indexes."
        }
    }
    foreach ($icPart in $icParts) {
        if (-not (Test-SameCheckpointDocument $icContext $icPart.payload.context) -or
            $null -ne $icPart.payload.context.executionWorldId) {
            throw "Interaction checkpoint $icId changes document between records."
        }
    }
}

function Get-InteractionCheckpointControls {
    param([Parameter(Mandatory = $true)] $Start)

    @(
        $interactionCheckpointParts[(Get-InteractionCheckpointKey $Start)] |
            Where-Object { $_.eventType -eq "interaction-checkpoint-text-control" }
    )
}

function Test-InteractionCheckpointValues {
    param($Start, [string] $FieldValue, [string] $NotesValue)

    $controls = Get-InteractionCheckpointControls $Start
    $field = @($controls | Where-Object { $_.payload.nodeId -eq $fieldNodeId })
    $notes = @($controls | Where-Object { $_.payload.nodeId -eq $notesNodeId })
    $controls.Count -eq 2 -and
    $field.Count -eq 1 -and $field[0].payload.value -eq $FieldValue -and
    $field[0].payload.controlType -eq "text" -and
    $notes.Count -eq 1 -and $notes[0].payload.value -eq $NotesValue -and
    $notes[0].payload.controlType -eq "textarea"
}

$fixtureCheckpointStarts = @(
    $interactionCheckpointStarts |
        Where-Object { $_.payload.context.documentId -eq $interactionDocumentId }
)
$parsedCheckpoint = @(
    $fixtureCheckpointStarts | Where-Object {
        $_.payload.sourceChannel -eq "browser.dom" -and
        $_.payload.reason -eq "finished-parsing" -and
        $null -eq $_.payload.focusedNodeId -and
        $null -eq $_.payload.activeDescendantNodeId -and
        (Test-InteractionCheckpointValues $_ "" "")
    }
)
if ($parsedCheckpoint.Count -eq 0) {
    throw (
        "No interaction checkpoint after the fixture document's " +
        "finished-parsing DOM checkpoint reported no focus and empty text " +
        "controls."
    )
}
$heldCheckpoint = @(
    $fixtureCheckpointStarts | Where-Object {
        $_.payload.sourceChannel -eq "browser.layout" -and
        $_.payload.reason -eq "rendering-update" -and
        $_.payload.focusedNodeId -eq $listboxNodeId -and
        $_.payload.activeDescendantNodeId -eq
            $activeDescendant.payload.referencedNodeId -and
        (Test-InteractionCheckpointValues $_ "set by script" "nXs set by script")
    }
)
if ($heldCheckpoint.Count -eq 0) {
    throw (
        "No interaction checkpoint after a layout checkpoint of the fixture " +
        "document reported the listbox focused with its active descendant " +
        "and the text controls' last values."
    )
}

[pscustomobject]@{
    InteractionCheckpoints = $interactionCheckpointStarts.Count
    FixtureInteractionCheckpoints = $fixtureCheckpointStarts.Count
    ParsedCheckpointId = $parsedCheckpoint[0].payload.checkpointId
    ParsedSourceCheckpointId = $parsedCheckpoint[0].payload.sourceCheckpointId
    HeldFocusCheckpointId = $heldCheckpoint[0].payload.checkpointId
    HeldFocusSourceCheckpointId = $heldCheckpoint[0].payload.sourceCheckpointId
    HeldFocusDocumentHasFocus = $heldCheckpoint[0].payload.documentHasFocus
    HeldFocusVisible = $heldCheckpoint[0].payload.focusVisible
    HeldFocusLastFocusType = $heldCheckpoint[0].payload.lastFocusType
} | Format-List

# Protocol 0.30 follows each layout checkpoint with a presentation request on
# the compositor of the frame's local-root widget, and records whether a
# compositor frame carried the following commit, its frame token, and the
# presentation time viz reported for it. These checks establish that every
# presentation record joins a recorded request and layout checkpoint, that no
# request has more than one outcome, that frame tokens do not go backwards
# within one frame sink, and that the fixture's held-focus layout checkpoint
# was carried by a presented frame. They say nothing about what the frame
# showed or whether any captured image displays it.
$prEventTypes = @(
    "presentation-requested",
    "presentation-not-swapped",
    "presentation-swapped",
    "presentation-feedback"
)
$prRecords = @(
    $records | Where-Object {
        $_.channel -eq "browser.presentation" -and
        $_.eventType -in $prEventTypes
    }
)
$prRequests = @($prRecords | Where-Object { $_.eventType -eq "presentation-requested" })
if ($prRequests.Count -eq 0) {
    throw "No presentation-requested record was emitted."
}

# Request identities are unique only within one renderer process.
function Get-PresentationRequestKey {
    param([Parameter(Mandatory = $true)] $PresentationRecord)

    $prRecordContext = $PresentationRecord.payload.context
    (
        "$($prRecordContext.browserInstanceId)|$($prRecordContext.processId)|" +
        "$($PresentationRecord.payload.requestId)"
    )
}

# viz frame tokens are 32-bit and wrap back to 1, so ordering is the sign of
# the 32-bit difference, as FrameTokenGT computes it.
function Test-FrameTokenBefore {
    param([uint64] $Earlier, [uint64] $Later)

    $prDifference = ($Later + 4294967296 - $Earlier) % 4294967296
    $prDifference -ne 0 -and $prDifference -lt 2147483648
}

$prRequestsByKey = @{}
foreach ($prRequest in $prRequests) {
    $prKey = Get-PresentationRequestKey $prRequest
    if ($prRequestsByKey.ContainsKey($prKey)) {
        throw "Presentation request $prKey was recorded more than once."
    }
    $prRequestsByKey[$prKey] = $prRequest
    $prLayoutKey = (
        "browser.layout|$($prRequest.payload.context.browserInstanceId)|" +
        "$($prRequest.payload.context.processId)|" +
        "$($prRequest.payload.layoutCheckpointId)"
    )
    if (-not $sourceCheckpointStarts.ContainsKey($prLayoutKey)) {
        throw (
            "Presentation request $prKey names layout checkpoint " +
            "$($prRequest.payload.layoutCheckpointId), which was not recorded."
        )
    }
    if (-not (Test-SameCheckpointDocument $prRequest.payload.context `
            $sourceCheckpointStarts[$prLayoutKey].payload.context)) {
        throw "Presentation request $prKey does not match its layout checkpoint's document."
    }
}

$prOutcomes = @{}
foreach ($prRecord in @($prRecords | Where-Object { $_.eventType -ne "presentation-requested" })) {
    $prKey = Get-PresentationRequestKey $prRecord
    if (-not $prRequestsByKey.ContainsKey($prKey)) {
        throw "A $($prRecord.eventType) record names request $prKey, which was not recorded."
    }
    $prRequest = $prRequestsByKey[$prKey]
    if (-not $prRequest.payload.queued) {
        throw "Request $prKey was not queued but has a $($prRecord.eventType) record."
    }
    if (-not (Test-SameCheckpointDocument $prRequest.payload.context $prRecord.payload.context) -or
        $prRecord.payload.frameSinkId -ne $prRequest.payload.frameSinkId -or
        $prRecord.payload.localRootFrameToken -ne $prRequest.payload.localRootFrameToken) {
        throw "The $($prRecord.eventType) record of request $prKey changes document or widget."
    }
    if (-not $prOutcomes.ContainsKey($prKey)) {
        $prOutcomes[$prKey] = [System.Collections.Generic.List[object]]::new()
    }
    $prOutcomes[$prKey].Add($prRecord)
}

$prUnresolved = 0
$prBroken = 0
$prPresented = 0
$prFailed = 0
foreach ($prKey in $prRequestsByKey.Keys) {
    $prRequest = $prRequestsByKey[$prKey]
    if (-not $prRequest.payload.queued) {
        continue
    }
    $prParts = @()
    if ($prOutcomes.ContainsKey($prKey)) {
        $prParts = @($prOutcomes[$prKey])
    }
    $prBreaks = @($prParts | Where-Object {
            $_.eventType -eq "presentation-not-swapped" -and
            $_.payload.action -eq "broken"
        })
    $prSwaps = @($prParts | Where-Object { $_.eventType -eq "presentation-swapped" })
    $prFeedback = @($prParts | Where-Object { $_.eventType -eq "presentation-feedback" })
    if (($prBreaks.Count + $prFeedback.Count) -gt 1 -or $prSwaps.Count -gt 1) {
        throw (
            "Request $prKey has $($prBreaks.Count) broken, $($prSwaps.Count) " +
            "swapped, and $($prFeedback.Count) feedback records."
        )
    }
    if ($prFeedback.Count -eq 1 -and
        ($prSwaps.Count -ne 1 -or
            $prFeedback[0].payload.frameToken -ne $prSwaps[0].payload.frameToken)) {
        throw "Request $prKey has feedback that does not follow its swapped frame."
    }
    if ($prBreaks.Count -eq 1) {
        $prBroken++
    }
    elseif ($prFeedback.Count -eq 1) {
        $prPresented++
        if ($prFeedback[0].payload.flags -contains "failure") {
            $prFailed++
        }
    }
    else {
        $prUnresolved++
    }
}

# Tokens can repeat, because checkpoints from one rendering update share a
# commit, but a later swap on one frame sink never carries an earlier token.
$prTokenSequences = 0
$prSwapGroups = @(
    $prRecords |
        Where-Object { $_.eventType -eq "presentation-swapped" } |
        Group-Object { "$($_.payload.context.browserInstanceId)|$($_.payload.frameSinkId)" }
)
foreach ($prGroup in $prSwapGroups) {
    $prOrdered = @(
        $prGroup.Group |
            Sort-Object { [long] $_.nativeTimestamp.value }, { [long] $_.sequence }
    )
    for ($prIndex = 1; $prIndex -lt $prOrdered.Count; $prIndex++) {
        $prPrevious = [uint64] $prOrdered[$prIndex - 1].payload.frameToken
        $prCurrent = [uint64] $prOrdered[$prIndex].payload.frameToken
        if ($prPrevious -ne $prCurrent -and
            -not (Test-FrameTokenBefore $prPrevious $prCurrent)) {
            throw (
                "Frame sink $($prGroup.Name) swapped token $prCurrent after " +
                "token $prPrevious."
            )
        }
    }
    $prTokenSequences++
}

# The fixture's held-focus step changes the listbox outline offset and waits
# two animation frames, so its layout checkpoint must reach the display.
$prFixtureEvidence = $null
foreach ($prHeld in $heldCheckpoint) {
    $prContext = $prHeld.payload.context
    $prMatches = @(
        $prRequests | Where-Object {
            $_.payload.context.browserInstanceId -eq $prContext.browserInstanceId -and
            $_.payload.context.processId -eq $prContext.processId -and
            $_.payload.layoutCheckpointId -eq $prHeld.payload.sourceCheckpointId -and
            $_.payload.queued
        }
    )
    foreach ($prRequest in $prMatches) {
        $prKey = Get-PresentationRequestKey $prRequest
        if (-not $prOutcomes.ContainsKey($prKey)) {
            continue
        }
        $prSwap = @($prOutcomes[$prKey] | Where-Object { $_.eventType -eq "presentation-swapped" })
        $prFeedback = @($prOutcomes[$prKey] | Where-Object { $_.eventType -eq "presentation-feedback" })
        if ($prSwap.Count -eq 1 -and $prFeedback.Count -eq 1 -and
            [uint64] $prSwap[0].payload.frameToken -ne 0 -and
            $prFeedback[0].payload.flags -notcontains "failure" -and
            $null -ne $prFeedback[0].payload.presentedTicks -and
            [long] $prFeedback[0].payload.presentedTicks -ge
                [long] $prSwap[0].nativeTimestamp.value) {
            $prFixtureEvidence = [pscustomobject]@{
                Request = $prRequest
                Swap = $prSwap[0]
                Feedback = $prFeedback[0]
            }
            break
        }
    }
    if ($null -ne $prFixtureEvidence) {
        break
    }
}
if ($null -eq $prFixtureEvidence) {
    throw (
        "No held-focus layout checkpoint of the interaction fixture was " +
        "carried by a swapped frame with presentation feedback, without the " +
        "failure flag, presented no earlier than its swap."
    )
}

$prFrequencyRecord = @(
    $records | Where-Object {
        $_.eventType -eq "browser-clock-synchronized" -and
        $_.payload.clockMappingId -eq $prFixtureEvidence.Feedback.clockMappingId
    }
)
$prSwapToPresentMs = $null
if ($prFrequencyRecord.Count -ge 1) {
    $prSwapToPresentMs = [Math]::Round(
        ([long] $prFixtureEvidence.Feedback.payload.presentedTicks -
            [long] $prFixtureEvidence.Swap.nativeTimestamp.value) * 1000.0 /
            [long] $prFrequencyRecord[0].payload.monotonicFrequency,
        3)
}

[pscustomobject]@{
    PresentationRequests = $prRequests.Count
    QueuedRequests = @($prRequests | Where-Object { $_.payload.queued }).Count
    NotQueuedReasons = (
        @($prRequests | Where-Object { -not $_.payload.queued } |
            ForEach-Object { $_.payload.notQueuedReason } | Sort-Object -Unique) -join ", "
    )
    PresentedRequests = $prPresented
    PresentedWithFailureFlag = $prFailed
    BrokenRequests = $prBroken
    UnresolvedRequests = $prUnresolved
    KeptActiveRecords = @($prRecords | Where-Object {
            $_.eventType -eq "presentation-not-swapped" -and
            $_.payload.action -eq "kept-active"
        }).Count
    FrameSinks = $prTokenSequences
    FixtureLayoutCheckpointId = $prFixtureEvidence.Request.payload.layoutCheckpointId
    FixtureFrameSinkId = $prFixtureEvidence.Request.payload.frameSinkId
    FixtureFrameToken = $prFixtureEvidence.Swap.payload.frameToken
    FixtureFeedbackFlags = ($prFixtureEvidence.Feedback.payload.flags -join ", ")
    FixtureSwapToPresentationMs = $prSwapToPresentMs
} | Format-List

# The run script serves a fourth page, on the cookie fixture's origin, in a
# foreground tab. The page paints, widens a box from 200 to 320 CSS pixels, and
# then changes only the box's color, waiting two animation frames after each
# change and reporting what it sees. These checks establish that the logger
# emitted complete, consistent layout checkpoints for that document and that
# the recorded rectangle, viewport, and computed styles agree with what the page
# reported. They say nothing about whether the page's layout or styling is
# appropriate.
$expectedLayoutStyleProperties = @(
    'display',
    'visibility',
    'opacity',
    'position',
    'top',
    'right',
    'bottom',
    'left',
    'z-index',
    'float',
    'box-sizing',
    'width',
    'height',
    'min-width',
    'min-height',
    'max-width',
    'max-height',
    'overflow-x',
    'overflow-y',
    'clip',
    'clip-path',
    'text-overflow',
    'content-visibility',
    'transform',
    'filter',
    'margin-top',
    'margin-right',
    'margin-bottom',
    'margin-left',
    'padding-top',
    'padding-right',
    'padding-bottom',
    'padding-left',
    'border-top-width',
    'border-right-width',
    'border-bottom-width',
    'border-left-width',
    'border-top-style',
    'border-right-style',
    'border-bottom-style',
    'border-left-style',
    'border-top-color',
    'border-right-color',
    'border-bottom-color',
    'border-left-color',
    'outline-style',
    'outline-width',
    'outline-color',
    'outline-offset',
    'box-shadow',
    'text-shadow',
    'color',
    'background-color',
    'background-image',
    'font-family',
    'font-size',
    'font-weight',
    'font-style',
    'line-height',
    'letter-spacing',
    'word-spacing',
    'text-transform',
    'text-decoration-line',
    'text-align',
    'text-indent',
    'white-space-collapse',
    'text-wrap-mode',
    'direction',
    'writing-mode',
    'cursor',
    'pointer-events',
    'animation-name',
    'animation-duration',
    'transition-property',
    'transition-duration',
    '-webkit-tap-highlight-color',
    '-webkit-text-fill-color',
    'accent-color',
    'backdrop-filter',
    'background-blend-mode',
    'caret-color',
    'color-scheme',
    'dynamic-range-limit',
    'forced-color-adjust',
    'isolation',
    'mix-blend-mode',
    'print-color-adjust',
    '-webkit-text-decorations-in-effect',
    '-webkit-text-stroke-color',
    '-webkit-text-stroke-width',
    'text-decoration',
    'text-decoration-color',
    'text-decoration-skip-ink',
    'text-decoration-skip-spaces',
    'text-decoration-style',
    'text-decoration-thickness',
    'text-emphasis-color',
    'text-emphasis-position',
    'text-emphasis-style',
    'text-underline-offset',
    'text-underline-position',
    '-webkit-line-break',
    '-webkit-line-clamp',
    '-webkit-rtl-ordering',
    '-webkit-ruby-position',
    '-webkit-text-combine',
    '-webkit-text-orientation',
    '-webkit-text-security',
    '-webkit-writing-mode',
    'alignment-baseline',
    'baseline-shift',
    'baseline-source',
    'content',
    'dominant-baseline',
    'hyphenate-character',
    'hyphenate-limit-chars',
    'hyphens',
    'initial-letter',
    'line-break',
    'orphans',
    'overflow-wrap',
    'quotes',
    'ruby-align',
    'ruby-overhang',
    'ruby-position',
    'tab-size',
    'text-align-last',
    'text-anchor',
    'text-autospace',
    'text-box-edge',
    'text-box-trim',
    'text-combine-upright',
    'text-fit',
    'text-justify',
    'text-orientation',
    'text-spacing-trim',
    'text-wrap-style',
    'unicode-bidi',
    'vertical-align',
    'widows',
    'word-break',
    '-webkit-user-drag',
    '-webkit-user-modify',
    'app-region',
    'appearance',
    'caret-animation',
    'caret-shape',
    'field-sizing',
    'interactivity',
    'interest-delay-end',
    'interest-delay-start',
    'reading-flow',
    'reading-order',
    'resize',
    'speak',
    'touch-action',
    'user-select',
    'window-drag',
    'anchor-name',
    'anchor-scope',
    'aspect-ratio',
    'break-after',
    'break-before',
    'break-inside',
    'buffered-rendering',
    'caption-side',
    'clear',
    'contain',
    'contain-intrinsic-height',
    'contain-intrinsic-size',
    'contain-intrinsic-width',
    'container-name',
    'container-type',
    'counter-increment',
    'counter-reset',
    'counter-set',
    'empty-cells',
    'frame-sizing',
    'image-orientation',
    'image-rendering',
    'interpolate-size',
    'list-style-image',
    'list-style-position',
    'list-style-type',
    'margin-trim',
    'object-fit',
    'object-position',
    'object-view-box',
    'overflow-anchor',
    'overflow-clip-margin',
    'overlay',
    'position-anchor',
    'position-area',
    'position-try-fallbacks',
    'position-try-order',
    'position-visibility',
    'shape-image-threshold',
    'shape-margin',
    'shape-outside',
    'table-layout',
    'will-change',
    'zoom',
    'backface-visibility',
    'offset-anchor',
    'offset-distance',
    'offset-path',
    'offset-position',
    'offset-rotate',
    'perspective',
    'perspective-origin',
    'rotate',
    'scale',
    'transform-box',
    'transform-origin',
    'transform-style',
    'translate',
    'overscroll-behavior-block',
    'overscroll-behavior-inline',
    'overscroll-behavior-x',
    'overscroll-behavior-y',
    'scroll-axis-lock',
    'scroll-behavior',
    'scroll-initial-target',
    'scroll-margin-block-end',
    'scroll-margin-block-start',
    'scroll-margin-bottom',
    'scroll-margin-inline-end',
    'scroll-margin-inline-start',
    'scroll-margin-left',
    'scroll-margin-right',
    'scroll-margin-top',
    'scroll-marker-group',
    'scroll-padding-block-end',
    'scroll-padding-block-start',
    'scroll-padding-bottom',
    'scroll-padding-inline-end',
    'scroll-padding-inline-start',
    'scroll-padding-left',
    'scroll-padding-right',
    'scroll-padding-top',
    'scroll-snap-align',
    'scroll-snap-stop',
    'scroll-snap-type',
    'scroll-target-group',
    'scroll-timeline-axis',
    'scroll-timeline-name',
    'scrollbar-color',
    'scrollbar-gutter',
    'scrollbar-width',
    'clip-rule',
    'color-interpolation',
    'color-interpolation-filters',
    'color-rendering',
    'cx',
    'cy',
    'd',
    'fill',
    'fill-opacity',
    'fill-rule',
    'flood-color',
    'flood-opacity',
    'lighting-color',
    'marker-end',
    'marker-mid',
    'marker-start',
    'paint-order',
    'r',
    'rx',
    'ry',
    'shape-rendering',
    'stop-color',
    'stop-opacity',
    'stroke',
    'stroke-dasharray',
    'stroke-dashoffset',
    'stroke-linecap',
    'stroke-linejoin',
    'stroke-miterlimit',
    'stroke-opacity',
    'stroke-width',
    'vector-effect',
    'x',
    'y'
)
$layoutSteps = ConvertFrom-Json $LayoutFixtureSteps
$layoutReports = [ordered]@{
    Settle = ConvertFrom-Json ([string] $layoutSteps.Settle)
    Widen = ConvertFrom-Json ([string] $layoutSteps.Widen)
    Recolor = ConvertFrom-Json ([string] $layoutSteps.Recolor)
}

$layoutCommits = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.navigation" -and
            $_.eventType -eq "navigation-completed" -and
            ([string] $_.payload.url).StartsWith($LayoutFixtureUri) -and
            $_.payload.frameType -eq "primary-main-frame" -and
            $_.payload.committed -eq $true -and
            $_.payload.sameDocument -eq $false
        }
)
if ($layoutCommits.Count -ne 1) {
    throw (
        "$($layoutCommits.Count) committed navigations to the layout logging " +
        "fixture were recorded rather than one."
    )
}
$layoutCommit = $layoutCommits[0]
$layoutDocumentToken = $layoutCommit.payload.context.documentToken
$layoutProcessId = $layoutCommit.payload.rendererProcessId
$layoutDocumentRecords = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.layout" -and
            $_.payload.context.documentToken -eq $layoutDocumentToken -and
            $_.payload.context.processId -eq $layoutProcessId
        }
)
$layoutStarts = @(
    $layoutDocumentRecords |
        Where-Object { $_.eventType -eq "layout-checkpoint-started" }
)
if ($layoutStarts.Count -lt 3) {
    throw (
        "$($layoutStarts.Count) layout checkpoints were emitted for the layout " +
        "logging fixture, fewer than one before and one after each of its " +
        "two changes."
    )
}

# Every checkpoint for the fixture document must be complete: one start, one
# completion, and exactly the node records the completion counts, indexed from
# zero without gaps. Each checkpoint names the document's previous checkpoint,
# and no two consecutive checkpoints report the same counters, because the
# logger only emits a checkpoint when style or layout work has happened.
$layoutCheckpoints = New-Object System.Collections.ArrayList
$previousLayoutStart = $null
foreach ($start in $layoutStarts) {
    $payload = $start.payload
    $checkpointId = $payload.checkpointId
    $expectedPrevious = if ($null -eq $previousLayoutStart) {
        $null
    } else {
        $previousLayoutStart.payload.checkpointId
    }
    if ($payload.previousCheckpointId -ne $expectedPrevious) {
        throw (
            "Layout checkpoint $checkpointId named " +
            "'$($payload.previousCheckpointId)' as its previous checkpoint " +
            "rather than '$expectedPrevious'."
        )
    }
    if ($null -ne $previousLayoutStart -and
        $payload.styleResolutionCount -eq
            $previousLayoutStart.payload.styleResolutionCount -and
        $payload.layoutCount -eq $previousLayoutStart.payload.layoutCount) {
        throw (
            "Layout checkpoint $checkpointId reported the same style and " +
            "layout counters as the checkpoint before it."
        )
    }
    if ($payload.reason -ne "rendering-update" -or
        $payload.maximumNodes -ne 100000) {
        throw (
            "Layout checkpoint $checkpointId reported reason " +
            "'$($payload.reason)' and maximum $($payload.maximumNodes)."
        )
    }
    $reportedProperties = @($payload.styleProperties)
    if ($reportedProperties.Count -ne $expectedLayoutStyleProperties.Count -or
        (@(
            for ($index = 0; $index -lt $reportedProperties.Count; $index++) {
                if ($reportedProperties[$index] -cne
                    $expectedLayoutStyleProperties[$index]) {
                    $index
                }
            }
        ).Count -gt 0)) {
        throw (
            "Layout checkpoint $checkpointId did not list the defined " +
            "computed-style properties in their defined order."
        )
    }
    $nodes = @(
        $layoutDocumentRecords |
            Where-Object {
                $_.eventType -eq "layout-checkpoint-node" -and
                $_.payload.checkpointId -eq $checkpointId
            }
    )
    $layoutCompletions = @(
        $layoutDocumentRecords |
            Where-Object {
                $_.eventType -eq "layout-checkpoint-completed" -and
                $_.payload.checkpointId -eq $checkpointId
            }
    )
    if ($layoutCompletions.Count -ne 1) {
        throw (
            "Layout checkpoint $checkpointId had $($layoutCompletions.Count) " +
            "completions rather than one."
        )
    }
    $layoutCompletion = $layoutCompletions[0].payload
    if ($layoutCompletion.truncated -or $layoutCompletion.nodeCount -ne $nodes.Count) {
        throw (
            "Layout checkpoint $checkpointId completed with " +
            "$($layoutCompletion.nodeCount) nodes, truncated " +
            "$($layoutCompletion.truncated), but $($nodes.Count) node records " +
            "were emitted."
        )
    }
    for ($index = 0; $index -lt $nodes.Count; $index++) {
        $node = $nodes[$index].payload
        if ($node.nodeIndex -ne $index) {
            throw (
                "Layout checkpoint $checkpointId emitted node index " +
                "$($node.nodeIndex) at position $index."
            )
        }
        if ($node.nodeType -eq "element" -and $null -ne $node.computedStyle) {
            # The bridge serializes the style as a JSON object, whose member
            # order is not significant and is not preserved; the start
            # record's list carries the order.
            $styleNames = @(
                $node.computedStyle.PSObject.Properties.Name |
                    Sort-Object -CaseSensitive
            )
            $sortedExpectedNames = @(
                $expectedLayoutStyleProperties | Sort-Object -CaseSensitive
            )
            if (($styleNames -join "|") -cne ($sortedExpectedNames -join "|")) {
                throw (
                    "A layout checkpoint node record in $checkpointId did not " +
                    "report exactly the defined computed-style properties."
                )
            }
        }
    }
    [void] $layoutCheckpoints.Add([pscustomobject]@{
            Start = $payload
            Nodes = $nodes
        })
    $previousLayoutStart = $start
}

function Get-LayoutBoxRecord {
    param(
        [Parameter(Mandatory = $true)]
        $Checkpoint
    )

    @(
        $Checkpoint.Nodes |
            Where-Object {
                $_.payload.nodeType -eq "element" -and
                $_.payload.nodeName -eq "DIV" -and
                $null -ne $_.payload.computedStyle -and
                $_.payload.computedStyle.height -eq "50px"
            }
    )
}

# The box is the only DIV in the page, and its height is fixed at 50 pixels.
# Only an element with a current computed style can match. The three states are found in order: 200 pixels wide and navy, then 320 pixels
# wide and still navy, then 320 pixels wide and dark red.
$layoutStates = @(
    @{
        Name = "Settle"
        Width = "200px"
        Color = "rgb(0, 0, 128)"
    },
    @{
        Name = "Widen"
        Width = "320px"
        Color = "rgb(0, 0, 128)"
    },
    @{
        Name = "Recolor"
        Width = "320px"
        Color = "rgb(128, 0, 0)"
    }
)
$layoutBoxNodeId = $null
$layoutStateCheckpoints = [ordered]@{}
$searchFrom = 0
foreach ($state in $layoutStates) {
    $found = $null
    for ($index = $searchFrom; $index -lt $layoutCheckpoints.Count; $index++) {
        $boxes = @(Get-LayoutBoxRecord $layoutCheckpoints[$index])
        # A checkpoint taken before the parser reached the box holds no record
        # for it.
        if ($boxes.Count -eq 0) {
            continue
        }
        if ($boxes.Count -gt 1) {
            throw (
                "Layout checkpoint $($layoutCheckpoints[$index].Start.checkpointId) " +
                "held $($boxes.Count) records for the fixture box rather than one."
            )
        }
        $box = $boxes[0].payload
        if ($null -ne $layoutBoxNodeId -and $box.nodeId -ne $layoutBoxNodeId) {
            throw "The fixture box changed node identity between checkpoints."
        }
        $layoutBoxNodeId = $box.nodeId
        if ($box.computedStyle.width -eq $state.Width -and
            $box.computedStyle.color -eq $state.Color) {
            $found = [pscustomobject]@{
                Checkpoint = $layoutCheckpoints[$index]
                Box = $box
            }
            $searchFrom = $index + 1
            break
        }
    }
    if ($null -eq $found) {
        throw (
            "No layout checkpoint after the previous state recorded the " +
            "fixture box $($state.Width) wide in $($state.Color) for the " +
            "$($state.Name) step."
        )
    }
    $layoutStateCheckpoints[$state.Name] = $found
}

# The recorded rectangle and viewport must agree with what the page reported
# after the frames that followed each change. The rectangle comes from the same
# Blink geometry the page's getBoundingClientRect call reads. The viewport is
# the media-query viewport, which, like innerWidth and innerHeight, includes
# any scroll bar; the page's values are whole pixels, so one pixel of
# difference is allowed.
foreach ($name in $layoutStateCheckpoints.Keys) {
    $state = $layoutStateCheckpoints[$name]
    $report = $layoutReports[$name]
    $rect = $state.Box.boundingClientRect
    if ($null -eq $rect -or -not $state.Box.layoutObjectPresent) {
        throw "The fixture box record for the $name step had no rectangle."
    }
    foreach ($axis in @("x", "y", "width", "height")) {
        if ([Math]::Abs([double] $rect.$axis - [double] $report.$axis) -gt 0.01) {
            throw (
                "The fixture box record for the $name step reported $axis " +
                "$($rect.$axis), but the page reported $($report.$axis)."
            )
        }
    }
    $viewport = $state.Checkpoint.Start.viewport
    if ([Math]::Abs([double] $viewport.width - [double] $report.innerWidth) -gt 1 -or
        [Math]::Abs([double] $viewport.height - [double] $report.innerHeight) -gt 1) {
        throw (
            "The $name checkpoint reported a viewport of " +
            "$($viewport.width) by $($viewport.height), but the page reported " +
            "$($report.innerWidth) by $($report.innerHeight)."
        )
    }
    if ($state.Box.computedStyle.color -ne $report.color) {
        throw (
            "The fixture box record for the $name step reported color " +
            "$($state.Box.computedStyle.color), but the page reported " +
            "$($report.color)."
        )
    }
}

# The settled checkpoint must also hold the element with display: none, which
# has no layout object and so no rectangle, and a laid-out text node with a
# rectangle and no computed style.
$settledNodes = $layoutStateCheckpoints["Settle"].Checkpoint.Nodes
$hiddenSpans = @(
    $settledNodes |
        Where-Object {
            $_.payload.nodeType -eq "element" -and
            $_.payload.nodeName -eq "SPAN"
        }
)
if ($hiddenSpans.Count -ne 1 -or
    $hiddenSpans[0].payload.layoutObjectPresent -or
    $null -ne $hiddenSpans[0].payload.boundingClientRect) {
    throw (
        "The settled layout checkpoint did not record the display: none " +
        "element once, without a layout object or rectangle."
    )
}
$layoutTextNodes = @(
    $settledNodes |
        Where-Object {
            $_.payload.nodeType -eq "text" -and
            $_.payload.layoutObjectPresent -and
            $null -ne $_.payload.boundingClientRect -and
            $_.payload.boundingClientRect.width -gt 0 -and
            $null -eq $_.payload.computedStyle
        }
)
if ($layoutTextNodes.Count -lt 1) {
    throw "The settled layout checkpoint recorded no laid-out text node."
}

[pscustomobject]@{
    LayoutRecords = $layoutDocumentRecords.Count
    LayoutCheckpoints = $layoutCheckpoints.Count
    LayoutBoxNodeId = $layoutBoxNodeId
    SettledCheckpointId = $layoutStateCheckpoints["Settle"].Checkpoint.Start.checkpointId
    WidenedCheckpointId = $layoutStateCheckpoints["Widen"].Checkpoint.Start.checkpointId
    RecoloredCheckpointId = $layoutStateCheckpoints["Recolor"].Checkpoint.Start.checkpointId
    SettledNodeCount = $settledNodes.Count
    SettledBoxRect = (
        "$($layoutStateCheckpoints['Settle'].Box.boundingClientRect.x)," +
        "$($layoutStateCheckpoints['Settle'].Box.boundingClientRect.y) " +
        "$($layoutStateCheckpoints['Settle'].Box.boundingClientRect.width)x" +
        "$($layoutStateCheckpoints['Settle'].Box.boundingClientRect.height)"
    )
    WidenedBoxWidth = $layoutStateCheckpoints["Widen"].Box.boundingClientRect.width
    RecoloredBoxColor = $layoutStateCheckpoints["Recolor"].Box.computedStyle.color
    Viewport = (
        "$($layoutStateCheckpoints['Settle'].Checkpoint.Start.viewport.width)x" +
        "$($layoutStateCheckpoints['Settle'].Checkpoint.Start.viewport.height)"
    )
    DevicePixelRatio = $layoutStateCheckpoints["Settle"].Checkpoint.Start.devicePixelRatio
    LayoutZoomFactor = $layoutStateCheckpoints["Settle"].Checkpoint.Start.layoutZoomFactor
} | Format-List

# The run script serves a page that builds an open shadow root with a named and
# a default slot, a closed shadow root with manual slot assignment that
# delegates focus, and an input, whose shadow root Blink creates as a
# user-agent root. The page has ::before, ::after, and ::marker content in the
# document tree and ::before content inside the open shadow tree, and it
# dispatches a click from a button inside the closed shadow root. These checks
# establish that the logger recorded each shadow root with its host and mode,
# each slot's assigned nodes, the shadow-tree and pseudo-element layout
# records, and the per-scope view of the dispatch path. They say nothing about
# whether the page's use of shadow DOM or generated content is appropriate.
$shadowSteps = ConvertFrom-Json $ShadowFixtureSteps
# Assigned before wrapping, because Windows PowerShell 5.1 writes a parsed
# JSON array to the pipeline as one object rather than as its entries.
$parsedShadowDispatch = ConvertFrom-Json ([string] $shadowSteps.Dispatch)
$shadowObserved = @($parsedShadowDispatch)
$shadowCommits = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.navigation" -and
            $_.eventType -eq "navigation-completed" -and
            ([string] $_.payload.url).StartsWith($ShadowFixtureUri) -and
            $_.payload.frameType -eq "primary-main-frame" -and
            $_.payload.committed -eq $true -and
            $_.payload.sameDocument -eq $false
        }
)
if ($shadowCommits.Count -ne 1) {
    throw (
        "$($shadowCommits.Count) committed navigations to the shadow DOM " +
        "logging fixture were recorded rather than one."
    )
}
$shadowDocumentToken = $shadowCommits[0].payload.context.documentToken
$shadowProcessId = $shadowCommits[0].payload.rendererProcessId
# Only the DOM, layout, and dispatch channels are read here, and each of their
# records carries a browser context. DOM and layout records carry the
# committed document's token, but dispatch records carry only the renderer's
# document identifier, so the token selects the DOM and layout records and the
# single renderer document identifier they share selects the dispatch records.
$shadowTokenRecords = @(
    $records |
        Where-Object {
            $_.channel -in @("browser.dom", "browser.layout") -and
            $_.payload.context.documentToken -eq $shadowDocumentToken -and
            $_.payload.context.processId -eq $shadowProcessId
        }
)
$shadowRendererDocumentIds = @(
    $shadowTokenRecords |
        ForEach-Object { [string] $_.payload.context.documentId } |
        Sort-Object -Unique
)
if ($shadowRendererDocumentIds.Count -ne 1) {
    throw (
        "The shadow DOM logging fixture's DOM and layout records carried " +
        "$($shadowRendererDocumentIds.Count) renderer document identifiers " +
        "rather than one."
    )
}
$shadowRendererDocumentId = $shadowRendererDocumentIds[0]
$shadowDocumentRecords = @(
    $shadowTokenRecords
    $records |
        Where-Object {
            $_.channel -eq "browser.dispatch" -and
            $_.payload.context.documentId -eq $shadowRendererDocumentId -and
            $_.payload.context.processId -eq $shadowProcessId
        }
)

# Groups the fixture document's DOM checkpoint records by checkpoint, checks
# that each checkpoint's counts match the records it emitted, and maps each id
# attribute value to the node that carried it.
$shadowDomCheckpoints = New-Object System.Collections.ArrayList
foreach ($start in @(
        $shadowDocumentRecords |
            Where-Object {
                $_.channel -eq "browser.dom" -and
                $_.eventType -eq "dom-checkpoint-started"
            }
    )) {
    $checkpointId = $start.payload.checkpointId
    $inCheckpoint = @(
        $shadowDocumentRecords |
            Where-Object {
                $_.channel -eq "browser.dom" -and
                $_.payload.checkpointId -eq $checkpointId
            }
    )
    $nodes = @($inCheckpoint | Where-Object { $_.eventType -eq "dom-checkpoint-node" })
    $roots = @($inCheckpoint | Where-Object { $_.eventType -eq "dom-checkpoint-shadow-root" })
    $slots = @($inCheckpoint | Where-Object { $_.eventType -eq "dom-checkpoint-slot-assignment" })
    $shadowCompletions = @($inCheckpoint | Where-Object { $_.eventType -eq "dom-checkpoint-completed" })
    if ($shadowCompletions.Count -ne 1) {
        throw (
            "Shadow fixture DOM checkpoint $checkpointId had " +
            "$($shadowCompletions.Count) completion records rather than one."
        )
    }
    $shadowCompletion = $shadowCompletions[0].payload
    if ($shadowCompletion.truncated -or
        $shadowCompletion.nodeCount -ne $nodes.Count -or
        $shadowCompletion.shadowRootCount -ne $roots.Count -or
        $shadowCompletion.slotCount -ne $slots.Count) {
        throw (
            "Shadow fixture DOM checkpoint $checkpointId completed with " +
            "$($shadowCompletion.nodeCount) nodes, $($shadowCompletion.shadowRootCount) " +
            "shadow roots, $($shadowCompletion.slotCount) slots, truncated " +
            "$($shadowCompletion.truncated), but emitted $($nodes.Count) node, " +
            "$($roots.Count) shadow root, and $($slots.Count) slot records."
        )
    }
    $ids = @{}
    foreach ($attribute in @(
            $inCheckpoint |
                Where-Object {
                    $_.eventType -eq "dom-checkpoint-node-attribute" -and
                    $_.payload.attributeName -eq "id" -and
                    $null -eq $_.payload.attributeNamespace
                }
        )) {
        $ids[[string] $attribute.payload.attributeValue] = [long] $attribute.payload.nodeId
    }
    $nodesById = @{}
    foreach ($node in $nodes) {
        $nodesById[[long] $node.payload.nodeId] = $node.payload
    }
    [void] $shadowDomCheckpoints.Add([pscustomobject]@{
            Id = $checkpointId
            Reason = $start.payload.reason
            Nodes = $nodesById
            Roots = $roots
            Slots = $slots
            Ids = $ids
        })
}

$shadowParsed = @(
    $shadowDomCheckpoints | Where-Object { $_.Reason -eq "finished-parsing" }
)
if ($shadowParsed.Count -ne 1) {
    throw (
        "$($shadowParsed.Count) finished-parsing DOM checkpoints were " +
        "recorded for the shadow DOM logging fixture rather than one."
    )
}
$shadowParsed = $shadowParsed[0]
$shadowIds = $shadowParsed.Ids
foreach ($name in @(
        "open-host", "open-label", "open-default", "open-bold",
        "open-named-slot", "open-default-slot", "closed-host",
        "closed-assigned", "closed-manual-slot", "closed-button",
        "shadow-input", "shadow-note", "shadow-item"
    )) {
    if (-not $shadowIds.ContainsKey($name)) {
        throw (
            "The shadow fixture's finished-parsing DOM checkpoint recorded no " +
            "node with id '$name'."
        )
    }
}

# Checks that the checkpoint recorded one shadow root on the host with the
# expected mode, focus delegation, and slot assignment, that the root has its
# own node record whose parent is the host, and that the named element inside
# the shadow tree has the root as its parent.
function Test-ShadowRootRecord {
    param(
        [Parameter(Mandatory = $true)] $Checkpoint,
        [Parameter(Mandatory = $true)] [string] $HostId,
        [Parameter(Mandatory = $true)] [string] $Mode,
        [Parameter(Mandatory = $true)] [bool] $DelegatesFocus,
        [Parameter(Mandatory = $true)] [string] $SlotAssignment,
        [string] $ChildId
    )

    $hostNodeId = $Checkpoint.Ids[$HostId]
    $found = @(
        $Checkpoint.Roots |
            Where-Object { $_.payload.hostNodeId -eq $hostNodeId }
    )
    if ($found.Count -ne 1) {
        throw (
            "DOM checkpoint $($Checkpoint.Id) recorded $($found.Count) shadow " +
            "roots on #$HostId rather than one."
        )
    }
    $root = $found[0].payload
    if ($root.mode -ne $Mode -or
        $root.delegatesFocus -ne $DelegatesFocus -or
        $root.slotAssignment -ne $SlotAssignment) {
        throw (
            "DOM checkpoint $($Checkpoint.Id) recorded the shadow root on " +
            "#$HostId as mode '$($root.mode)', delegatesFocus " +
            "$($root.delegatesFocus), slotAssignment '$($root.slotAssignment)'."
        )
    }
    $rootNode = $Checkpoint.Nodes[[long] $root.nodeId]
    if ($null -eq $rootNode -or
        $rootNode.nodeType -ne "shadow-root" -or
        $rootNode.parentNodeId -ne $hostNodeId) {
        throw (
            "DOM checkpoint $($Checkpoint.Id) has no shadow-root node record " +
            "under #$HostId for its shadow root."
        )
    }
    if ($ChildId) {
        $child = $Checkpoint.Nodes[$Checkpoint.Ids[$ChildId]]
        if ($null -eq $child -or $child.parentNodeId -ne $root.nodeId) {
            throw (
                "DOM checkpoint $($Checkpoint.Id) did not record #$ChildId as " +
                "a child of the shadow root on #$HostId."
            )
        }
    }
    $root
}

$openRoot = Test-ShadowRootRecord $shadowParsed "open-host" "open" $false "named" "open-bold"
$closedRoot = Test-ShadowRootRecord $shadowParsed "closed-host" "closed" $true "manual" "closed-button"
$userAgentRoot = Test-ShadowRootRecord $shadowParsed "shadow-input" "user-agent" $false "named"

# The slots are compared in a checkpoint that read every fixture slot while
# Blink held its assignment as current. Slot assignment is recalculated lazily,
# so the finished-parsing checkpoint may read the slots before Blink assigned
# them, and the recorder never requests the recalculation itself. The settle
# step makes a post-mutation checkpoint after two animation frames for this.
$slotExpectations = [ordered]@{
    "open-named-slot" = @("open-label")
    "open-default-slot" = @("open-default")
    "closed-manual-slot" = @("closed-assigned")
}
$staleSlotRecords = 0
$slotCheckpoint = $null
foreach ($checkpoint in $shadowDomCheckpoints) {
    $staleSlotRecords += @(
        $checkpoint.Slots | Where-Object { -not $_.payload.assignmentCurrent }
    ).Count
    foreach ($slot in $checkpoint.Slots) {
        foreach ($assigned in @($slot.payload.assignedNodeIds)) {
            if ($null -ne $assigned -and -not $checkpoint.Nodes.ContainsKey([long] $assigned)) {
                throw (
                    "DOM checkpoint $($checkpoint.Id) assigned node $assigned " +
                    "to a slot but emitted no node record for it."
                )
            }
        }
    }
    $allCurrent = $true
    foreach ($slotId in $slotExpectations.Keys) {
        $slot = @(
            $checkpoint.Slots |
                Where-Object { $_.payload.nodeId -eq $checkpoint.Ids[$slotId] }
        )
        if ($slot.Count -ne 1 -or -not $slot[0].payload.assignmentCurrent) {
            $allCurrent = $false
        }
    }
    if ($allCurrent) {
        $slotCheckpoint = $checkpoint
    }
}
if ($null -eq $slotCheckpoint) {
    throw (
        "No DOM checkpoint of the shadow fixture read all three fixture slots " +
        "with a current assignment."
    )
}
foreach ($slotId in $slotExpectations.Keys) {
    $slot = @(
        $slotCheckpoint.Slots |
            Where-Object { $_.payload.nodeId -eq $slotCheckpoint.Ids[$slotId] }
    )[0].payload
    $expected = @($slotExpectations[$slotId] | ForEach-Object { $slotCheckpoint.Ids[$_] })
    $actual = @($slot.assignedNodeIds)
    if ($slot.assignedNodeCount -ne $expected.Count -or
        $slot.assignedNodesTruncated -or
        ($actual -join ",") -ne ($expected -join ",")) {
        throw (
            "DOM checkpoint $($slotCheckpoint.Id) recorded #$slotId as " +
            "assigned [$($actual -join ', ')] rather than " +
            "[$($expected -join ', ')]."
        )
    }
}

# The layout checks use the latest completed layout checkpoint of the fixture
# document that recorded the closed shadow tree's button.
$shadowLayoutStarts = @(
    $shadowDocumentRecords |
        Where-Object {
            $_.channel -eq "browser.layout" -and
            $_.eventType -eq "layout-checkpoint-started"
        }
)
$shadowLayout = $null
foreach ($start in $shadowLayoutStarts) {
    $checkpointId = $start.payload.checkpointId
    $nodes = @(
        $shadowDocumentRecords |
            Where-Object {
                $_.eventType -eq "layout-checkpoint-node" -and
                $_.payload.checkpointId -eq $checkpointId
            } |
            ForEach-Object { $_.payload }
    )
    $shadowCompletion = @(
        $shadowDocumentRecords |
            Where-Object {
                $_.eventType -eq "layout-checkpoint-completed" -and
                $_.payload.checkpointId -eq $checkpointId
            }
    )
    if ($shadowCompletion.Count -eq 1 -and
        @($nodes | Where-Object { $_.nodeId -eq $shadowIds["closed-button"] }).Count -eq 1) {
        $shadowLayout = [pscustomobject]@{
            Id = $checkpointId
            Nodes = $nodes
            Completion = $shadowCompletion[0].payload
        }
    }
}
if ($null -eq $shadowLayout) {
    throw (
        "No completed layout checkpoint of the shadow fixture recorded the " +
        "button inside the closed shadow root."
    )
}
$layoutPseudoNodes = @(
    $shadowLayout.Nodes | Where-Object { $_.nodeType -eq "pseudo-element" }
)
if ($shadowLayout.Completion.truncated -or
    $shadowLayout.Completion.nodeCount -ne $shadowLayout.Nodes.Count -or
    $shadowLayout.Completion.pseudoElementCount -ne $layoutPseudoNodes.Count -or
    $shadowLayout.Completion.shadowRootCount -lt 3) {
    throw (
        "Shadow fixture layout checkpoint $($shadowLayout.Id) completed with " +
        "$($shadowLayout.Completion.nodeCount) nodes, " +
        "$($shadowLayout.Completion.pseudoElementCount) pseudo-elements, and " +
        "$($shadowLayout.Completion.shadowRootCount) shadow roots, but " +
        "emitted $($shadowLayout.Nodes.Count) node and " +
        "$($layoutPseudoNodes.Count) pseudo-element records."
    )
}
$pseudoExpectations = @(
    @{ Origin = "shadow-note"; Type = "::before"; Text = "Note:" },
    @{ Origin = "shadow-note"; Type = "::after"; Text = "End" },
    @{ Origin = "shadow-item"; Type = "::marker"; Text = $null },
    @{ Origin = "open-bold"; Type = "::before"; Text = "Open" }
)
$openPseudoMode = $null
foreach ($expectation in $pseudoExpectations) {
    $origin = $shadowIds[$expectation.Origin]
    $found = @(
        $layoutPseudoNodes |
            Where-Object {
                $_.pseudoElement.originatingNodeId -eq $origin -and
                $_.pseudoElement.pseudoType -eq $expectation.Type
            }
    )
    if ($found.Count -ne 1) {
        throw (
            "Layout checkpoint $($shadowLayout.Id) recorded $($found.Count) " +
            "$($expectation.Type) records for #$($expectation.Origin) rather " +
            "than one."
        )
    }
    # Surrounding whitespace in generated text is not asserted.
    if ($null -ne $expectation.Text -and
        ([string] $found[0].pseudoElement.generatedText).Trim() -ne $expectation.Text) {
        throw (
            "Layout checkpoint $($shadowLayout.Id) recorded the " +
            "$($expectation.Type) text of #$($expectation.Origin) as " +
            "'$($found[0].pseudoElement.generatedText)'."
        )
    }
    if ($expectation.Origin -eq "open-bold") {
        $openPseudoMode = $found[0].shadowRootMode
    }
}

# Checks the shadow scope a layout node record reports.
function Test-LayoutShadowScope {
    param(
        [Parameter(Mandatory = $true)] [long] $NodeId,
        [Parameter(Mandatory = $true)] [string] $Label,
        [Parameter(Mandatory = $true)] [long] $HostNodeId,
        [Parameter(Mandatory = $true)] [string] $Mode
    )

    $found = @($shadowLayout.Nodes | Where-Object { $_.nodeId -eq $NodeId })
    if ($found.Count -ne 1 -or
        $found[0].shadowHostNodeId -ne $HostNodeId -or
        $found[0].shadowRootMode -ne $Mode) {
        throw (
            "Layout checkpoint $($shadowLayout.Id) did not record $Label once " +
            "in the $Mode shadow tree of node $HostNodeId."
        )
    }
}
Test-LayoutShadowScope $shadowIds["closed-button"] "#closed-button" $shadowIds["closed-host"] "closed"
Test-LayoutShadowScope $shadowIds["open-bold"] "#open-bold" $shadowIds["open-host"] "open"
$userAgentLayoutNodes = @(
    $shadowLayout.Nodes |
        Where-Object {
            $_.shadowRootMode -eq "user-agent" -and
            $_.shadowHostNodeId -eq $shadowIds["shadow-input"]
        }
)
if ($userAgentLayoutNodes.Count -lt 1) {
    throw (
        "Layout checkpoint $($shadowLayout.Id) recorded no node inside the " +
        "input's user-agent shadow tree."
    )
}
foreach ($node in $shadowLayout.Nodes) {
    if ($node.nodeId -eq $shadowIds["shadow-note"] -and $null -ne $node.shadowRootMode) {
        throw "Layout checkpoint $($shadowLayout.Id) placed #shadow-note in a shadow tree."
    }
}

# The dispatch from inside the closed shadow root. Blink's composed path runs
# from the button through its shadow root and host to the window, and each
# path scope reports the part of that path a listener in that scope sees,
# which the page's listeners reported as composedPath() lengths.
$shadowDispatches = @(
    $shadowDocumentRecords |
        Where-Object {
            $_.channel -eq "browser.dispatch" -and
            $_.eventType -eq "dispatch-started" -and
            $_.payload.eventName -eq "click" -and
            $_.payload.originalTarget.elementId -eq "closed-button"
        }
)
if ($shadowDispatches.Count -ne 1) {
    throw (
        "$($shadowDispatches.Count) click dispatches from #closed-button " +
        "were recorded rather than one."
    )
}
$shadowDispatch = $shadowDispatches[0].payload
$shadowPath = @($shadowDispatch.composedPath)
$shadowScopes = @($shadowDispatch.pathScopes)
$observedLength = @{}
foreach ($entry in $shadowObserved) {
    $observedLength[[string] $entry.where] = [int] $entry.composedPathLength
}
if ($shadowDispatch.trusted -ne $false -or
    $shadowPath.Count -ne $observedLength["button"] -or
    $shadowScopes.Count -ne $shadowPath.Count -or
    $shadowPath[0].nodeId -ne $shadowIds["closed-button"] -or
    $shadowPath[2].nodeId -ne $shadowIds["closed-host"] -or
    $shadowPath[$shadowPath.Count - 1].kind -ne "window") {
    throw (
        "The #closed-button dispatch recorded trusted $($shadowDispatch.trusted), " +
        "$($shadowPath.Count) path entries, and $($shadowScopes.Count) path " +
        "scopes; the button's listener saw $($observedLength['button']) " +
        "composed path entries."
    )
}
$shadowDocumentNodes = @(
    $shadowParsed.Nodes.Values | Where-Object { $_.nodeType -eq "document" }
)
if ($shadowDocumentNodes.Count -ne 1) {
    throw "The shadow fixture's finished-parsing DOM checkpoint has no document node."
}
$documentIndex = -1
for ($index = 0; $index -lt $shadowPath.Count; $index++) {
    if ($shadowPath[$index].nodeId -eq $shadowDocumentNodes[0].nodeId) {
        $documentIndex = $index
    }
}
if ($documentIndex -lt 0) {
    throw "The #closed-button dispatch path recorded no document entry."
}
$scopeChecks = @(
    @{ Index = 0; Where = "button"; Root = $closedRoot.nodeId; Mode = "closed"; Target = $shadowIds["closed-button"] },
    @{ Index = 2; Where = "host"; Root = $null; Mode = $null; Target = $shadowIds["closed-host"] },
    @{ Index = $documentIndex; Where = "document"; Root = $null; Mode = $null; Target = $shadowIds["closed-host"] }
)
foreach ($check in $scopeChecks) {
    $scope = $shadowScopes[$check.Index]
    $visible = @($scope.visiblePathIndexes)
    # The closed tree's scopes see the whole path. The document tree's scopes
    # see it from the host onward, since retargeting hides the shadow tree.
    $expectedVisible = @(2..($shadowPath.Count - 1))
    if ($check.Where -eq "button") {
        $expectedVisible = @(0..($shadowPath.Count - 1))
    }
    if ($scope.shadowRootMode -ne $check.Mode -or
        $scope.targetNodeId -ne $check.Target -or
        ($null -ne $check.Root -and $scope.treeScopeRootNodeId -ne $check.Root) -or
        $visible.Count -ne $observedLength[$check.Where] -or
        ($visible -join ",") -ne ($expectedVisible -join ",")) {
        throw (
            "Path scope $($check.Index) of the #closed-button dispatch " +
            "recorded mode '$($scope.shadowRootMode)', target " +
            "$($scope.targetNodeId), and visible indexes " +
            "[$($visible -join ', ')]; the $($check.Where) listener saw " +
            "$($observedLength[$check.Where]) composed path entries."
        )
    }
}

[pscustomobject]@{
    ShadowRecords = $shadowDocumentRecords.Count
    ShadowDomCheckpoints = $shadowDomCheckpoints.Count
    OpenShadowRootNodeId = $openRoot.nodeId
    ClosedShadowRootNodeId = $closedRoot.nodeId
    UserAgentShadowRootNodeId = $userAgentRoot.nodeId
    SlotCheckpointId = $slotCheckpoint.Id
    SlotCheckpointReason = $slotCheckpoint.Reason
    StaleSlotRecords = $staleSlotRecords
    ShadowLayoutCheckpointId = $shadowLayout.Id
    ShadowLayoutPseudoElements = $layoutPseudoNodes.Count
    ShadowLayoutShadowRoots = $shadowLayout.Completion.shadowRootCount
    UserAgentLayoutNodes = $userAgentLayoutNodes.Count
    OpenTreePseudoShadowRootMode = $openPseudoMode
    ShadowDispatchPathEntries = $shadowPath.Count
    ShadowDispatchDocumentIndex = $documentIndex
} | Format-List

# The run script serves a fifth page that sends a fetch with credential-bearing
# and plain request headers, follows a redirected fetch, fails a fetch to a
# closed port, loads one cacheable script twice, and fetches from a dedicated
# worker, after reaching the page itself through a redirect. These checks
# establish that the logger emitted a network record for each of those
# requests, with header values kept except credentials and cookies listed by
# name. They say nothing about whether the page's network use is appropriate.
$networkReport = ConvertFrom-Json $NetworkFixtureReport
if ($networkReport.dataStatus -ne 200 -or
    $networkReport.hopRedirected -ne $true -or
    $networkReport.refused -ne "rejected" -or
    $networkReport.cachedRuns -ne 2 -or
    $networkReport.workerText -ne "worker data") {
    throw "The network fixture page reported: $NetworkFixtureReport"
}
$realtimeReport = $networkReport.realtime
if ($null -eq $realtimeReport -or
    $realtimeReport.greeting -ne "fixture greeting" -or
    $realtimeReport.echoedTextMatches -ne $true -or
    $realtimeReport.echoedBinaryLength -ne 4 -or
    $realtimeReport.closeCode -ne 1000 -or
    $realtimeReport.closeWasClean -ne $true -or
    (@($realtimeReport.events) -join "|") -ne "status:7|message:plain event" -or
    $realtimeReport.transport -ne "rejected") {
    throw "The network fixture page reported realtime results: $NetworkFixtureReport"
}

foreach ($secret in $NetworkSecretValues) {
    $secretRecords = @(
        Get-Content -LiteralPath $eventPath |
            Where-Object { $_.Contains($secret) }
    )
    if ($secretRecords.Count -gt 0) {
        throw (
            "$($secretRecords.Count) record(s) contain a network fixture " +
            "credential value. Credential header values are withheld."
        )
    }
}

$networkRecords = @(
    $records | Where-Object {
            $_.channel -eq "browser.network" -and
            $_.eventType -ne "collector-omission"
        }
)
if ($networkRecords.Count -eq 0) {
    throw "No network record was emitted."
}

# Every header entry in every network record, with the record it came from.
$credentialHeaderNames = @(
    "cookie",
    "set-cookie",
    "set-cookie2",
    "authorization",
    "proxy-authorization"
)
function Get-NetworkHeaderLists {
    param($Payload)

    if ($null -ne $Payload.PSObject.Properties["headers"]) {
        , @($Payload.headers)
    }
    if ($null -ne $Payload.PSObject.Properties["requestHeaders"]) {
        , @($Payload.requestHeaders)
    }
    foreach ($member in @("request", "response", "redirectResponse")) {
        $property = $Payload.PSObject.Properties[$member]
        if ($null -ne $property -and $null -ne $property.Value -and
            $null -ne $property.Value.PSObject.Properties["headers"]) {
            , @($property.Value.headers)
        }
    }
}
foreach ($record in $networkRecords) {
    foreach ($list in @(Get-NetworkHeaderLists $record.payload)) {
        foreach ($header in @($list)) {
            if ($null -eq $header) {
                continue
            }
            $lowered = ([string] $header.name).ToLowerInvariant()
            if ($credentialHeaderNames -contains $lowered -and
                ($null -ne $header.value -or
                    $header.valueRedacted -ne $true -or
                    $header.redactionReason -ne "credential-header")) {
                throw (
                    "A $($record.eventType) record carried header " +
                    "'$($header.name)' without withholding its value."
                )
            }
        }
    }
}

function Find-NetworkHeader {
    param(
        $Headers,

        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $found = @(
        @($Headers) | Where-Object {
            $null -ne $_ -and
            ([string] $_.name).ToLowerInvariant() -eq $Name.ToLowerInvariant()
        }
    )
    if ($found.Count -eq 0) {
        throw "The $Description did not include the $Name header."
    }
    $found[0]
}

function Test-NetworkHeaderRedacted {
    param(
        $Headers,

        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $Reason,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $header = Find-NetworkHeader $Headers $Name $Description
    if ($null -ne $header.value -or $header.valueRedacted -ne $true -or
        $header.redactionReason -ne $Reason) {
        throw (
            "The $Description reported the $Name header with redaction " +
            "'$($header.redactionReason)' rather than withholding its value " +
            "as $Reason."
        )
    }
}

function Test-NetworkHeaderValue {
    param(
        $Headers,

        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string] $Value,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $header = Find-NetworkHeader $Headers $Name $Description
    if ($header.value -ne $Value -or $header.valueRedacted -ne $false) {
        throw (
            "The $Description reported the $Name header value " +
            "'$($header.value)' rather than '$Value'."
        )
    }
}

function Select-NetworkRecord {
    param(
        [Parameter(Mandatory = $true)]
        [string] $EventType,

        [Parameter(Mandatory = $true)]
        [scriptblock] $Filter,

        [Parameter(Mandatory = $true)]
        [string] $Description
    )

    $selected = @(
        $networkRecords |
            Where-Object { $_.eventType -eq $EventType } |
            Where-Object $Filter
    )
    if ($selected.Count -eq 0) {
        throw "No $EventType record was emitted for $Description."
    }
    $selected[0]
}

$networkDataUri = "$NetworkFixtureUri/data"
$networkHopTargetUri = "$NetworkFixtureUri/data?hop=1"

# The page's fetch with credential-bearing headers, from the window.
$dataRequest = Select-NetworkRecord "request-will-be-sent" {
    $_.payload.request.url -eq $networkDataUri -and
    $_.payload.redirect -eq $false -and
    $_.payload.scope.contextKind -eq "window"
} "the network fixture's data fetch"
$dataRequestHeaders = @($dataRequest.payload.request.headers)
Test-NetworkHeaderRedacted $dataRequestHeaders "Authorization" `
    "credential-header" "data fetch request"
Test-NetworkHeaderRedacted $dataRequestHeaders "X-Api-Key" `
    "credential-name" "data fetch request"
Test-NetworkHeaderRedacted $dataRequestHeaders "X-Fixture-Scheme" `
    "credential-value" "data fetch request"
Test-NetworkHeaderValue $dataRequestHeaders "X-Fixture-Plain" `
    "network-fixture-plain" "data fetch request"
if ($dataRequest.payload.request.method -ne "GET" -or
    [string]::IsNullOrWhiteSpace($dataRequest.payload.request.requestId)) {
    throw (
        "The data fetch request reported method " +
        "'$($dataRequest.payload.request.method)' and request ID " +
        "'$($dataRequest.payload.request.requestId)'."
    )
}
$dataInspectorId = $dataRequest.payload.request.inspectorId
$dataRequestId = $dataRequest.payload.request.requestId
$dataDocumentToken = $dataRequest.payload.context.documentToken

$dataResponse = Select-NetworkRecord "response-received" {
    $_.payload.inspectorId -eq $dataInspectorId -and
    $_.payload.context.documentToken -eq $dataDocumentToken
} "the network fixture's data fetch"
if ($dataResponse.payload.response.status -ne 200 -or
    $dataResponse.payload.responseSource -ne "loader") {
    throw (
        "The data fetch response reported status " +
        "$($dataResponse.payload.response.status) from " +
        "'$($dataResponse.payload.responseSource)'."
    )
}
$dataResponseHeaders = @($dataResponse.payload.response.headers)
Test-NetworkHeaderValue $dataResponseHeaders "X-Fixture-Response" `
    "network-fixture-response" "data fetch response"
Test-NetworkHeaderRedacted $dataResponseHeaders "X-Session-Id" `
    "credential-name" "data fetch response"

$dataFinished = Select-NetworkRecord "request-finished" {
    $_.payload.inspectorId -eq $dataInspectorId -and
    $_.payload.context.documentToken -eq $dataDocumentToken
} "the network fixture's data fetch"
if ($dataFinished.payload.decodedBodyLength -ne 12) {
    throw (
        "The data fetch finish reported a decoded body of " +
        "$($dataFinished.payload.decodedBodyLength) bytes rather than 12."
    )
}

# The headers the network service put on the wire for the same request,
# matched by the request ID the renderer record carries.
$dataWireRequest = Select-NetworkRecord "request-headers-sent" {
    $_.payload.requestId -eq $dataRequestId
} "the network fixture's data fetch"
$dataWireHeaders = @($dataWireRequest.payload.headers)
Test-NetworkHeaderRedacted $dataWireHeaders "Authorization" `
    "credential-header" "data fetch wire request"
Test-NetworkHeaderRedacted $dataWireHeaders "Cookie" `
    "credential-header" "data fetch wire request"
Test-NetworkHeaderValue $dataWireHeaders "X-Fixture-Plain" `
    "network-fixture-plain" "data fetch wire request"
$dataWireCookieNames = @(
    @($dataWireRequest.payload.cookies) | ForEach-Object { $_.name }
)
if ($dataWireCookieNames -notcontains "a11y_recorder_response") {
    throw (
        "The data fetch wire request listed the cookies " +
        "'$($dataWireCookieNames -join ', ')' without " +
        "a11y_recorder_response."
    )
}
$dataWireResponse = Select-NetworkRecord "response-headers-received" {
    $_.payload.requestId -eq $dataRequestId
} "the network fixture's data fetch"
if ($dataWireResponse.payload.status -ne 200) {
    throw (
        "The data fetch wire response reported status " +
        "$($dataWireResponse.payload.status)."
    )
}
Test-NetworkHeaderRedacted @($dataWireResponse.payload.headers) `
    "X-Session-Id" "credential-name" "data fetch wire response"

# The redirected fetch: its redirect is recorded as a second request record
# for the same request, carrying the redirect response.
$hopRedirect = Select-NetworkRecord "request-will-be-sent" {
    $_.payload.redirect -eq $true -and
    $_.payload.request.url -eq $networkHopTargetUri -and
    $null -ne $_.payload.redirectResponse -and
    $_.payload.redirectResponse.status -eq 302
} "the network fixture's redirected fetch"
Test-NetworkHeaderValue @($hopRedirect.payload.redirectResponse.headers) `
    "Location" "/network/data?hop=1" "redirect response"
$null = Select-NetworkRecord "request-finished" {
    $_.payload.inspectorId -eq $hopRedirect.payload.request.inspectorId
} "the network fixture's redirected fetch"

# The fetch to a closed loopback port.
$refusedRecord = Select-NetworkRecord "request-failed" {
    $_.payload.url -eq $NetworkRefusedUri
} "the network fixture's refused fetch"
if ($refusedRecord.payload.netError -ge 0) {
    throw (
        "The refused fetch reported network error " +
        "$($refusedRecord.payload.netError)."
    )
}

# The second load of the cacheable script, from a new frame whose document
# finds it only in the memory cache.
$cachedScriptUri = "$NetworkFixtureUri/cached.js"
$cacheHit = Select-NetworkRecord "memory-cache-hit" {
    $_.payload.request.url -eq $cachedScriptUri
} "the network fixture's second script load"
if ($cacheHit.payload.response.status -ne 200) {
    throw (
        "The memory cache hit reported status " +
        "$($cacheHit.payload.response.status)."
    )
}

# The dedicated worker's fetch.
$workerRequest = Select-NetworkRecord "request-will-be-sent" {
    $_.payload.request.url -eq "$NetworkFixtureUri/worker-data" -and
    $_.payload.scope.contextKind -eq "dedicated-worker"
} "the network fixture worker's fetch"
if ($workerRequest.payload.scope.globalObjectUrl -ne
        "$NetworkFixtureUri/worker.js" -or
    [string]::IsNullOrWhiteSpace($workerRequest.payload.scope.workerToken)) {
    throw (
        "The worker fetch reported worker script " +
        "'$($workerRequest.payload.scope.globalObjectUrl)' and token " +
        "'$($workerRequest.payload.scope.workerToken)'."
    )
}
$null = Select-NetworkRecord "request-finished" {
    $_.payload.inspectorId -eq $workerRequest.payload.request.inspectorId -and
    $_.payload.scope.contextKind -eq "dedicated-worker"
} "the network fixture worker's fetch"

# The fixture page's own navigation, reached through a redirect.
$networkNavigation = Select-NetworkRecord "navigation-response" {
    $_.payload.url -eq $NetworkFixtureUri -and
    $_.payload.committed -eq $true
} "the network fixture page navigation"
$networkRedirectChain = @($networkNavigation.payload.redirectChain)
if ($networkRedirectChain.Count -ne 2 -or
    $networkRedirectChain[0] -ne $NetworkStartUri -or
    $networkRedirectChain[1] -ne $NetworkFixtureUri) {
    throw (
        "The network fixture navigation reported the redirect chain " +
        "'$($networkRedirectChain -join ', ')'."
    )
}
if ($null -eq $networkNavigation.payload.response -or
    $networkNavigation.payload.response.status -ne 200 -or
    $null -eq $networkNavigation.payload.timing) {
    throw "The network fixture navigation recorded no response or timing."
}

# The realtime steps. These checks establish that the logger emitted a record
# for each WebSocket, event stream, and WebTransport step the page took, with
# handshake cookies listed by name, credential-named message fields withheld,
# and the call records carrying the world of the fixture's script.
$socketUri = ($NetworkFixtureUri -replace "^http:", "ws:") + "/socket"
function Test-RealtimeCallWorld {
    param($Record, [string] $Description)

    $payload = $Record.payload
    if ($null -eq $payload.world -or $payload.world.kind -ne "main" -or
        $payload.context.executionWorldId -ne
            "world-$($payload.world.blinkWorldId)" -or
        $null -eq $payload.location) {
        throw "The $Description record did not report the main world and a location."
    }
}
function Test-RealtimeText {
    param(
        $Text,
        [string] $Expected,
        [int] $Withheld,
        [string] $Description
    )

    if ($null -eq $Text) {
        throw "The $Description record carried no text."
    }
    $withheldCount = @($Text.withheld).Count
    if (($Expected -and $Text.text -ne $Expected) -or
        $Text.truncated -ne $false -or $withheldCount -ne $Withheld) {
        throw (
            "The $Description record carried the text '$($Text.text)' with " +
            "$withheldCount withheld part(s)."
        )
    }
    foreach ($part in @($Text.withheld)) {
        if ($Text.text.Substring($part.offset).StartsWith("[withheld]") -ne $true) {
            throw "The $Description record placed a withheld marker at the wrong offset."
        }
    }
}

$socketCreated = Select-NetworkRecord "websocket-created" {
    $_.payload.url -eq $socketUri
} "the realtime WebSocket"
if ($socketCreated.payload.requestedProtocols -ne "a11y-recorder-fixture") {
    throw (
        "The WebSocket creation reported the protocols " +
        "'$($socketCreated.payload.requestedProtocols)'."
    )
}
Test-RealtimeCallWorld $socketCreated "WebSocket creation"
$socketId = $socketCreated.payload.inspectorId
$socketDocument = $socketCreated.payload.context.documentToken
$socketFilter = {
    $_.payload.inspectorId -eq $socketId -and
    $_.payload.context.documentToken -eq $socketDocument
}

$socketRequest = Select-NetworkRecord "websocket-handshake-request" `
    $socketFilter "the realtime WebSocket"
$socketCookieNames = @($socketRequest.payload.cookieNames)
if ($socketCookieNames -notcontains "a11y_recorder_response") {
    throw (
        "The WebSocket handshake request listed the cookies " +
        "'$($socketCookieNames -join ', ')' without a11y_recorder_response."
    )
}
Test-NetworkHeaderRedacted @($socketRequest.payload.headers) "Cookie" `
    "credential-header" "WebSocket handshake request"
Test-NetworkHeaderRedacted @($socketRequest.payload.headers) `
    "Sec-WebSocket-Key" "credential-name" "WebSocket handshake request"

$socketResponse = Select-NetworkRecord "websocket-handshake-response" `
    $socketFilter "the realtime WebSocket"
$socketSetCookieNames = @($socketResponse.payload.setCookieNames)
if ($socketResponse.payload.status -ne 101 -or
    $socketResponse.payload.selectedProtocol -ne "a11y-recorder-fixture" -or
    $socketSetCookieNames -notcontains "a11y_recorder_socket") {
    throw (
        "The WebSocket handshake response reported status " +
        "$($socketResponse.payload.status), protocol " +
        "'$($socketResponse.payload.selectedProtocol)', and set cookies " +
        "'$($socketSetCookieNames -join ', ')'."
    )
}
Test-NetworkHeaderRedacted @($socketResponse.payload.headers) "Set-Cookie" `
    "credential-header" "WebSocket handshake response"

$socketMessages = @(
    $networkRecords | Where-Object {
        $_.eventType -in @("websocket-message-sent", "websocket-message-received")
    } | Where-Object $socketFilter
)
$greetingRecord = @(
    $socketMessages | Where-Object {
        $_.eventType -eq "websocket-message-received" -and
        $_.payload.opcode -eq "text" -and
        $_.payload.payload.text -eq "fixture greeting"
    }
)
if ($greetingRecord.Count -ne 1) {
    throw "$($greetingRecord.Count) WebSocket greeting records were emitted."
}
Test-RealtimeText $greetingRecord[0].payload.payload "fixture greeting" 0 `
    "WebSocket greeting"
$sentText = @(
    $socketMessages | Where-Object {
        $_.eventType -eq "websocket-message-sent" -and
        $_.payload.opcode -eq "text"
    }
)
$echoText = @(
    $socketMessages | Where-Object {
        $_.eventType -eq "websocket-message-received" -and
        $_.payload.opcode -eq "text" -and
        $_.payload.payload.text -ne "fixture greeting"
    }
)
if ($sentText.Count -ne 1 -or $echoText.Count -ne 1) {
    throw (
        "$($sentText.Count) sent and $($echoText.Count) echoed WebSocket " +
        "text records were emitted rather than one each."
    )
}
$withheldMessage = '{"type":"auth","token":"[withheld]","room":"lobby"}'
Test-RealtimeText $sentText[0].payload.payload $withheldMessage 1 `
    "WebSocket sent text"
Test-RealtimeText $echoText[0].payload.payload $withheldMessage 1 `
    "WebSocket echoed text"
Test-RealtimeCallWorld $sentText[0] "WebSocket sent text"
$binaryMessages = @(
    $socketMessages | Where-Object { $_.payload.opcode -eq "binary" }
)
if ($binaryMessages.Count -ne 2 -or
    @($binaryMessages | Where-Object {
            $_.payload.payloadLength -ne 4 -or $null -ne $_.payload.payload
        }).Count -ne 0) {
    throw (
        "$($binaryMessages.Count) WebSocket binary records were emitted " +
        "rather than two of four bytes without content."
    )
}

$socketCloseRequest = Select-NetworkRecord "websocket-close-requested" `
    $socketFilter "the realtime WebSocket"
if ($socketCloseRequest.payload.code -ne 1000) {
    throw "The WebSocket close request reported code $($socketCloseRequest.payload.code)."
}
Test-RealtimeText $socketCloseRequest.payload.reason "fixture done" 0 `
    "WebSocket close request"
Test-RealtimeCallWorld $socketCloseRequest "WebSocket close request"
$socketClosed = Select-NetworkRecord "websocket-closed" $socketFilter `
    "the realtime WebSocket"
if ($socketClosed.payload.cause -ne "dropped" -or
    $socketClosed.payload.wasClean -ne $true -or
    $socketClosed.payload.code -ne 1000) {
    throw (
        "The WebSocket closure reported cause " +
        "'$($socketClosed.payload.cause)', clean " +
        "$($socketClosed.payload.wasClean), and code " +
        "$($socketClosed.payload.code)."
    )
}

$eventsUri = "$NetworkFixtureUri/events"
$eventRecords = @(
    $networkRecords | Where-Object {
        $_.eventType -eq "event-source-message" -and $_.payload.url -eq $eventsUri
    }
)
$statusEvents = @($eventRecords | Where-Object { $_.payload.eventType -eq "status" })
$plainEvents = @($eventRecords | Where-Object { $_.payload.eventType -eq "message" })
if ($statusEvents.Count -ne 1 -or $plainEvents.Count -ne 1) {
    throw (
        "$($statusEvents.Count) status and $($plainEvents.Count) message " +
        "event records were emitted rather than one each."
    )
}
Test-RealtimeText $statusEvents[0].payload.data `
    '{"access_token":"[withheld]"}' 1 "event stream status event"
Test-RealtimeText $statusEvents[0].payload.lastEventId "7" 0 `
    "event stream status event"
Test-RealtimeText $plainEvents[0].payload.data "plain event" 0 `
    "event stream message event"

$transportCreated = Select-NetworkRecord "web-transport-created" {
    $_.payload.url -eq $NetworkTransportUri
} "the realtime WebTransport session"
Test-RealtimeCallWorld $transportCreated "WebTransport creation"
$transportId = $transportCreated.payload.transportId
$transportDocument = $transportCreated.payload.context.documentToken
$transportFilter = {
    $_.payload.transportId -eq $transportId -and
    $_.payload.context.documentToken -eq $transportDocument
}
$transportCloseRequest = Select-NetworkRecord "web-transport-close-requested" `
    $transportFilter "the realtime WebTransport session"
if ($transportCloseRequest.payload.code -ne 7) {
    throw (
        "The WebTransport close request reported code " +
        "$($transportCloseRequest.payload.code)."
    )
}
Test-RealtimeText $transportCloseRequest.payload.reason "fixture close" 0 `
    "WebTransport close request"
Test-RealtimeCallWorld $transportCloseRequest "WebTransport close request"
$transportClosed = Select-NetworkRecord "web-transport-closed" `
    $transportFilter "the realtime WebTransport session"
if ($transportClosed.payload.abrupt -ne $true -or
    $null -ne $transportClosed.payload.code -or
    $null -ne $transportClosed.payload.reason) {
    throw "The WebTransport closure was not reported as abrupt without a code."
}
$transportEstablished = @(
    $networkRecords | Where-Object {
        $_.eventType -eq "web-transport-established"
    } | Where-Object $transportFilter
)
if ($transportEstablished.Count -ne 0) {
    throw "A WebTransport session to a closed port was reported as established."
}

[pscustomobject]@{
    WebSocketInspectorId = $socketId
    WebSocketHandshakeCookieNames = $socketCookieNames -join ", "
    WebSocketSetCookieNames = $socketSetCookieNames -join ", "
    WebSocketMessages = $socketMessages.Count
    WebSocketClosedCode = $socketClosed.payload.code
    EventSourceMessages = $eventRecords.Count
    WebTransportId = $transportId
    WebTransportClosedAbrupt = $transportClosed.payload.abrupt
} | Format-List

[pscustomobject]@{
    NetworkRecords = $networkRecords.Count
    DataFetchInspectorId = $dataInspectorId
    DataFetchRequestId = $dataRequestId
    DataFetchWireCookieNames = $dataWireCookieNames -join ", "
    RedirectedFetchInspectorId = $hopRedirect.payload.request.inspectorId
    RefusedFetchNetError = $refusedRecord.payload.netError
    RefusedFetchNetErrorName = $refusedRecord.payload.netErrorName
    MemoryCacheHitUrl = $cacheHit.payload.request.url
    WorkerFetchRequestId = $workerRequest.payload.request.requestId
    NavigationRequestId = $networkNavigation.payload.requestId
    NavigationRedirectChain = $networkRedirectChain -join " -> "
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
    "interaction-state change and checkpoint, layout and computed-style " +
    "checkpoint and its compositor presentation, shadow root, " +
    "slot assignment, pseudo-element, and shadow-scoped dispatch path, and " +
    "network " +
    "metadata and realtime channel evidence verified."
)

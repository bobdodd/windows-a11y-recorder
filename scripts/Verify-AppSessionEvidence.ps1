[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SessionPath,

    # The URL the run script served the app session fixture page from.
    [Parameter(Mandatory = $true)]
    [string] $FixtureUri,

    # What the run script observed and did during the recording, as a JSON
    # object. InputMarker is the value every injected input carried as its
    # extra information. ButtonRect, TextRect, and NextRect are the screen
    # rectangles UI Automation reported for the fixture's controls before
    # they were clicked. ChromiumProcessIds lists the processes started from
    # the instrumented Chromium executable while the recording ran.
    # TypedText is the text the run script typed into the text field.
    [Parameter(Mandatory = $true)]
    [string] $StepsJson
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# The raw input flags that mark a left button press and release.
$leftButtonDown = 0x0001
$leftButtonUp = 0x0002
# Browser dispatch may be recorded slightly before the raw input record, since
# the two are timestamped by different threads that read the same system input
# queue. A dispatch earlier than this, or later than the upper bound, is not
# accepted as the result of the injected input.
$earliestDispatchNanoseconds = -250000000
$latestDispatchNanoseconds = 2000000000

function Get-OptionalProperty {
    param(
        $Value,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if ($null -eq $Value) {
        return $null
    }
    if ($Value.PSObject.Properties.Name -notcontains $Name) {
        return $null
    }
    $Value.$Name
}

function Test-InsideRect {
    param(
        [Parameter(Mandatory = $true)]
        $Rect,

        [Parameter(Mandatory = $true)]
        [int] $X,

        [Parameter(Mandatory = $true)]
        [int] $Y
    )

    $X -ge $Rect.x -and $X -lt ($Rect.x + $Rect.width) -and
        $Y -ge $Rect.y -and $Y -lt ($Rect.y + $Rect.height)
}

$eventPath = Join-Path $SessionPath "events.ndjson"
if (-not (Test-Path -LiteralPath $eventPath -PathType Leaf)) {
    throw "The session does not contain events.ndjson: $SessionPath"
}
$steps = ConvertFrom-Json $StepsJson
$marker = [long] $steps.InputMarker
$chromiumProcessIds = @()
foreach ($processId in $steps.ChromiumProcessIds) {
    $chromiumProcessIds += [int] $processId
}
if ($chromiumProcessIds.Count -eq 0) {
    throw "The run script reported no instrumented Chromium processes."
}

$records = @(
    Get-Content -LiteralPath $eventPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_ | ConvertFrom-Json }
)

# Every Windows collector the app enables by default must have started and
# stopped inside this session.
$collectorTypes = @(
    "windows.raw-input",
    "windows.ui-automation",
    "windows.foreground-window",
    "windows.desktop-frames"
)
foreach ($collectorType in $collectorTypes) {
    $lifecycle = @(
        $records |
            Where-Object {
                $_.channel -eq "collector.lifecycle" -and
                $_.collectorType -eq $collectorType
            }
    )
    $started = @($lifecycle | Where-Object { $_.payload.action -eq "started" })
    $stopped = @($lifecycle | Where-Object { $_.payload.action -eq "stopped" })
    if ($started.Count -ne 1 -or $stopped.Count -ne 1) {
        throw (
            "The $collectorType collector reported $($started.Count) start(s) " +
            "and $($stopped.Count) stop(s); the app session needs one of each."
        )
    }
}

# The fixture page must have been committed in a primary main frame, and its
# renderer process identifies the page's renderer evidence below.
$fixtureCommits = @(
    $records |
        Where-Object {
            $_.channel -eq "browser.navigation" -and
            $_.eventType -eq "navigation-completed" -and
            $_.payload.url -eq $FixtureUri -and
            $_.payload.frameType -eq "primary-main-frame" -and
            $_.payload.committed -eq $true -and
            $_.payload.errorPage -eq $false
        }
)
if ($fixtureCommits.Count -ne 1) {
    throw (
        "Expected one committed navigation to the app session fixture, found " +
        "$($fixtureCommits.Count)."
    )
}
$fixtureCommit = $fixtureCommits[0]
$rendererProcessId = [int] $fixtureCommit.payload.rendererProcessId
if ($chromiumProcessIds -notcontains $rendererProcessId) {
    throw (
        "The fixture's renderer process $rendererProcessId was not started " +
        "from the instrumented Chromium executable the app launched."
    )
}

$rendererRecords = @(
    $records |
        Where-Object {
            $_.channel -like "browser.*" -and
            $null -ne (Get-OptionalProperty $_.payload "context") -and
            (Get-OptionalProperty $_.payload.context "processId") -eq $rendererProcessId
        }
)

# Each renderer channel must have recorded the fixture page.
$cookieWrites = @(
    $rendererRecords |
        Where-Object {
            $_.channel -eq "browser.cookie" -and
            $_.eventType -eq "document-cookie-write" -and
            $_.payload.name -eq "app_session_script"
        }
)
if ($cookieWrites.Count -ne 1) {
    throw "Expected one document.cookie write from the fixture, found $($cookieWrites.Count)."
}

$dataUri = "$($FixtureUri.TrimEnd('/'))/data"
$dataRequests = @(
    $rendererRecords |
        Where-Object {
            $_.channel -eq "browser.network" -and
            $_.eventType -eq "request-will-be-sent" -and
            $_.payload.request.url -eq $dataUri
        }
)
# Renderer network records share an inspector identifier, which is unique
# within one renderer process.
$dataInspectorIds = @($dataRequests | ForEach-Object { $_.payload.request.inspectorId })
$dataCompletions = @(
    $rendererRecords |
        Where-Object {
            $_.channel -eq "browser.network" -and
            $_.eventType -eq "request-finished" -and
            $dataInspectorIds -contains $_.payload.inspectorId
        }
)
if ($dataRequests.Count -ne 1 -or $dataCompletions.Count -ne 1) {
    throw (
        "Expected one recorded and finished request for $dataUri, found " +
        "$($dataRequests.Count) request(s) and $($dataCompletions.Count) " +
        "completion(s)."
    )
}

$shadowRoots = @(
    $rendererRecords |
        Where-Object {
            $_.channel -eq "browser.dom" -and
            $_.eventType -eq "dom-checkpoint-shadow-root" -and
            $_.payload.mode -eq "open"
        }
)
$layoutCompletions = @(
    $rendererRecords |
        Where-Object {
            $_.channel -eq "browser.layout" -and
            $_.eventType -eq "layout-checkpoint-completed"
        }
)
$composedLayouts = @(
    $layoutCompletions |
        Where-Object {
            [int] $_.payload.shadowRootCount -ge 1 -and
                [int] $_.payload.pseudoElementCount -ge 1
        }
)
if ($composedLayouts.Count -eq 0) {
    throw "No fixture layout checkpoint recorded both a shadow root and a pseudo-element."
}
$accessibilityCompletions = @(
    $rendererRecords |
        Where-Object {
            $_.channel -eq "browser.accessibility" -and
            $_.eventType -eq "accessibility-checkpoint-completed"
        }
)
$listenerRegistrations = @(
    $rendererRecords |
        Where-Object {
            $_.channel -eq "browser.listener" -and
            $_.eventType -eq "listener-registered" -and
            (Get-OptionalProperty $_.payload.target "elementId") -eq "app-button"
        }
)
foreach ($check in @(
        @{ Name = "open shadow root"; Count = $shadowRoots.Count },
        @{ Name = "completed layout checkpoint"; Count = $layoutCompletions.Count },
        @{ Name = "completed accessibility checkpoint"; Count = $accessibilityCompletions.Count },
        @{ Name = "listener registration on #app-button"; Count = $listenerRegistrations.Count }
    )) {
    if ($check.Count -lt 1) {
        throw "The fixture renderer recorded no $($check.Name)."
    }
}

# Injected input is found by the marker the run script set as each input's
# extra information, so input a person made during the run is not mistaken
# for it.
$markedMouse = @(
    $records |
        Where-Object {
            $_.channel -eq "input.mouse" -and
            [long] $_.payload.extraInformation -eq $marker
        } |
        Sort-Object monotonicNanoseconds
)
$markedKeyboard = @(
    $records |
        Where-Object {
            $_.channel -eq "input.keyboard" -and
            [long] $_.payload.extraInformation -eq $marker
        } |
        Sort-Object monotonicNanoseconds
)
$markedPresses = @(
    $markedMouse | Where-Object {
        ([int] $_.payload.buttonFlags -band $leftButtonDown) -ne 0
    }
)
$markedReleases = @(
    $markedMouse | Where-Object {
        ([int] $_.payload.buttonFlags -band $leftButtonUp) -ne 0
    }
)
if ($markedPresses.Count -ne 2 -or $markedReleases.Count -ne 2) {
    throw (
        "Expected two injected left button presses and releases, found " +
        "$($markedPresses.Count) press(es) and $($markedReleases.Count) " +
        "release(s)."
    )
}
# Four characters and a Tab, each pressed and released.
if ($markedKeyboard.Count -ne 10) {
    throw "Expected 10 injected key records, found $($markedKeyboard.Count)."
}
$buttonPress = $markedPresses[0]
$textPress = $markedPresses[1]
foreach ($pair in @(
        @{ Name = "#app-button"; Press = $buttonPress; Rect = $steps.ButtonRect },
        @{ Name = "#app-text"; Press = $textPress; Rect = $steps.TextRect }
    )) {
    if (-not (Test-InsideRect $pair.Rect $pair.Press.payload.cursorX $pair.Press.payload.cursorY)) {
        throw (
            "The injected press on $($pair.Name) was recorded at " +
            "($($pair.Press.payload.cursorX), $($pair.Press.payload.cursorY)), " +
            "outside the control's UI Automation rectangle."
        )
    }
    if ($chromiumProcessIds -notcontains [int] $pair.Press.payload.foregroundProcessId) {
        throw (
            "The injected press on $($pair.Name) was recorded with foreground " +
            "process $($pair.Press.payload.foregroundProcessId), which is not " +
            "an instrumented Chromium process."
        )
    }
}

# Returns the node with the given element id that a dispatch reached: its
# original target, or, when the event was dispatched to a node inside the
# element's user-agent shadow tree, the element's entry in the composed path.
function Get-DispatchElement {
    param(
        [Parameter(Mandatory = $true)]
        $Dispatch,

        [Parameter(Mandatory = $true)]
        [string] $ElementId
    )

    if ((Get-OptionalProperty $Dispatch.payload.originalTarget "elementId") -eq $ElementId) {
        return $Dispatch.payload.originalTarget
    }
    foreach ($entry in @(Get-OptionalProperty $Dispatch.payload "composedPath")) {
        if ((Get-OptionalProperty $entry "elementId") -eq $ElementId) {
            return $entry
        }
    }
    $null
}

# Returns the first trusted dispatch of the named event that reached the
# element with the given element id, recorded by the fixture renderer inside the window
# accepted for the input recorded at InputNanoseconds.
function Find-InputDispatch {
    param(
        [Parameter(Mandatory = $true)]
        [string] $EventName,

        [Parameter(Mandatory = $true)]
        [string] $ElementId,

        [Parameter(Mandatory = $true)]
        [long] $InputNanoseconds
    )

    $candidates = @(
        $rendererRecords |
            Where-Object {
                $_.channel -eq "browser.dispatch" -and
                $_.eventType -eq "dispatch-started" -and
                $_.payload.eventName -eq $EventName -and
                $_.payload.trusted -eq $true -and
                $null -ne (Get-DispatchElement $_ $ElementId)
            } |
            Where-Object {
                $offset = [long] $_.monotonicNanoseconds - $InputNanoseconds
                $offset -ge $earliestDispatchNanoseconds -and
                    $offset -le $latestDispatchNanoseconds
            } |
            Sort-Object monotonicNanoseconds
    )
    if ($candidates.Count -eq 0) {
        throw (
            "No trusted $EventName dispatch on #$ElementId was recorded within " +
            "the accepted window of its injected input."
        )
    }
    $candidates[0]
}

$buttonDown = Find-InputDispatch "mousedown" "app-button" $buttonPress.monotonicNanoseconds
$buttonClick = Find-InputDispatch "click" "app-button" $markedReleases[0].monotonicNanoseconds
$textDown = Find-InputDispatch "mousedown" "app-text" $textPress.monotonicNanoseconds
$firstKey = $markedKeyboard[0]
$textKey = Find-InputDispatch "keydown" "app-text" $firstKey.monotonicNanoseconds
$tabPress = $markedKeyboard[$markedKeyboard.Count - 2]
$tabRelease = $markedKeyboard[$markedKeyboard.Count - 1]
$nextKeyUp = Find-InputDispatch "keyup" "app-next" $tabRelease.monotonicNanoseconds

$trustedTextKeys = @(
    $rendererRecords |
        Where-Object {
            $_.channel -eq "browser.dispatch" -and
            $_.eventType -eq "dispatch-started" -and
            $_.payload.eventName -eq "keydown" -and
            $_.payload.trusted -eq $true -and
            $null -ne (Get-DispatchElement $_ "app-text")
        }
)
# The four characters and the Tab keydown are all dispatched to the text field.
if ($trustedTextKeys.Count -ne 5) {
    throw "Expected 5 trusted keydown dispatches on #app-text, found $($trustedTextKeys.Count)."
}

# Focus moves by mouse to the button and the text field, then forward to the
# next button. Node identities come from the dispatch records' targets.
function Find-FocusChange {
    param(
        [Parameter(Mandatory = $true)]
        $Dispatch,

        [Parameter(Mandatory = $true)]
        [string] $ElementId,

        [Parameter(Mandatory = $true)]
        [string] $FocusType
    )

    $element = Get-DispatchElement $Dispatch $ElementId
    $nodeId = $element.nodeId
    $documentId = $element.documentId
    $changes = @(
        $rendererRecords |
            Where-Object {
                $_.channel -eq "browser.interaction" -and
                $_.eventType -eq "focus-changed" -and
                $_.payload.context.documentId -eq $documentId -and
                $_.payload.focusedNodeId -eq $nodeId -and
                $_.payload.focusType -eq $FocusType -and
                $_.payload.focusTrigger -eq "user-gesture" -and
                [long] $_.monotonicNanoseconds -ge (
                    [long] $Dispatch.monotonicNanoseconds + $earliestDispatchNanoseconds
                )
            } |
            Sort-Object monotonicNanoseconds
    )
    if ($changes.Count -eq 0) {
        throw (
            "No $FocusType focus change by user gesture to " +
            "#$ElementId was recorded."
        )
    }
    $changes[0]
}

$buttonFocus = Find-FocusChange $buttonDown "app-button" "mouse"
$textFocus = Find-FocusChange $textDown "app-text" "mouse"
$nextFocus = Find-FocusChange $nextKeyUp "app-next" "forward"

$valueChanges = @(
    $rendererRecords |
        Where-Object {
            $_.channel -eq "browser.interaction" -and
            $_.eventType -eq "text-control-value-changed" -and
            $_.payload.nodeId -eq $textFocus.payload.focusedNodeId -and
            $_.payload.source -eq "user-edit"
        } |
        Sort-Object monotonicNanoseconds
)
# Each typed character produces at least one user edit.
if ($valueChanges.Count -lt $steps.TypedText.Length) {
    throw (
        "Expected at least $($steps.TypedText.Length) user edits of #app-text, found " +
        "$($valueChanges.Count)."
    )
}
$finalValue = $valueChanges[$valueChanges.Count - 1].payload.value
if ($finalValue -ne $steps.TypedText) {
    throw "The last recorded value of #app-text was '$finalValue', not '$($steps.TypedText)'."
}

# UI Automation must report focus reaching the same controls in a Chromium
# process, after the input that moved it.
function Find-UiaFocus {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [long] $AfterNanoseconds
    )

    $events = @(
        $records |
            Where-Object {
                $_.channel -eq "accessibility.uia.events" -and
                $_.eventType -eq "focus-changed" -and
                [long] $_.monotonicNanoseconds -ge $AfterNanoseconds -and
                $_.payload.element.name -eq $Name -and
                $chromiumProcessIds -contains [int] $_.payload.element.processId
            } |
            Sort-Object monotonicNanoseconds
    )
    if ($events.Count -eq 0) {
        $uiaOmissions = @(
            $records |
                Where-Object {
                    $_.channel -eq "accessibility.uia.events" -and
                    $_.eventType -eq "collector-omission"
                }
        )
        throw (
            "UI Automation recorded no focus change to '$Name' after its input. " +
            "The UI Automation collector reported $($uiaOmissions.Count) omission " +
            "record(s) in this session."
        )
    }
    $events[0]
}

$uiaButton = Find-UiaFocus "Record app session click" $buttonPress.monotonicNanoseconds
$uiaText = Find-UiaFocus "App session text" $textPress.monotonicNanoseconds
$uiaNext = Find-UiaFocus "App session next" $tabPress.monotonicNanoseconds

$firstInput = [long] $markedMouse[0].monotonicNanoseconds
$lastInput = [long] $tabRelease.monotonicNanoseconds
$chromiumForeground = @(
    $records |
        Where-Object {
            $_.channel -eq "window.foreground" -and
            [long] $_.monotonicNanoseconds -le $firstInput -and
            $chromiumProcessIds -contains [int] $_.payload.processId
        }
)
if ($chromiumForeground.Count -eq 0) {
    throw "No foreground-window record shows Chromium in front before the injected input."
}
$inputFrames = @(
    $records |
        Where-Object {
            $_.channel -eq "graphics.desktop.frames" -and
            $_.eventType -eq "desktop-frame" -and
            [long] $_.monotonicNanoseconds -ge $firstInput -and
            [long] $_.monotonicNanoseconds -le $lastInput
        }
)
if ($inputFrames.Count -eq 0) {
    throw "No desktop frame was recorded while the injected input ran."
}

function Get-Milliseconds {
    param([long] $From, [long] $To)

    [Math]::Round(($To - $From) / 1000000.0, 2)
}

$omissions = @(
    $records |
        Where-Object { $_.eventType -eq "collector-omission" } |
        ForEach-Object { "$($_.channel):$($_.payload.reason)" } |
        Sort-Object -Unique
)

[pscustomobject]@{
    RendererProcessId = $rendererProcessId
    ChromiumProcesses = $chromiumProcessIds.Count
    InjectedMouseRecords = $markedMouse.Count
    InjectedKeyRecords = $markedKeyboard.Count
    ButtonPressToMouseDownMs = Get-Milliseconds $buttonPress.monotonicNanoseconds $buttonDown.monotonicNanoseconds
    ButtonReleaseToClickMs = Get-Milliseconds $markedReleases[0].monotonicNanoseconds $buttonClick.monotonicNanoseconds
    TextPressToMouseDownMs = Get-Milliseconds $textPress.monotonicNanoseconds $textDown.monotonicNanoseconds
    FirstKeyToKeyDownMs = Get-Milliseconds $firstKey.monotonicNanoseconds $textKey.monotonicNanoseconds
    ButtonPressToUiaFocusMs = Get-Milliseconds $buttonPress.monotonicNanoseconds $uiaButton.monotonicNanoseconds
    TextPressToUiaFocusMs = Get-Milliseconds $textPress.monotonicNanoseconds $uiaText.monotonicNanoseconds
    TabPressToUiaFocusMs = Get-Milliseconds $tabPress.monotonicNanoseconds $uiaNext.monotonicNanoseconds
    ButtonFocusNodeId = $buttonFocus.payload.focusedNodeId
    TextFocusNodeId = $textFocus.payload.focusedNodeId
    NextFocusNodeId = $nextFocus.payload.focusedNodeId
    UserEdits = $valueChanges.Count
    FinalValue = $finalValue
    ShadowRoots = $shadowRoots.Count
    LayoutCheckpoints = $layoutCompletions.Count
    AccessibilityCheckpoints = $accessibilityCompletions.Count
    FramesDuringInput = $inputFrames.Count
    OmissionKinds = ($omissions -join "; ")
} | Format-List

Write-Host (
    "App session collectors, fixture navigation, renderer cookie, network, " +
    "DOM, layout, accessibility, and listener evidence, injected input, " +
    "trusted dispatch, focus, text edits, UI Automation focus, foreground " +
    "window, and desktop frame evidence verified."
)

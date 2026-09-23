[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ChromiumSource,

    [string] $DepotTools = (
        Join-Path $env:USERPROFILE "chromium-dev\depot_tools"
    ),

    [string] $OutputRoot = (
        Join-Path $env:PUBLIC "Documents\A11yRecorderBlinkValidation"
    ),

    [string] $DotnetPath = (
        Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    ),

    [ValidateRange(5, 300)]
    [int] $DurationSeconds = 15
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Description,

        [Parameter(Mandatory = $true)]
        [scriptblock] $Command
    )

    Write-Host "`n== $Description =="

    # Native tools in this pipeline report progress on stderr. Python's
    # unittest runner writes its progress and summary there even on success.
    # With $ErrorActionPreference set to Stop, PowerShell converts that
    # stderr output into a terminating NativeCommandError whenever the
    # script's own output is redirected or transcribed, which aborts a run
    # that has not actually failed. Only the process exit code decides
    # success here.
    #
    # Merging stderr into the success stream and rendering each record as
    # text keeps that progress output in the transcript as plain lines. Left
    # as error records it appears as a NativeCommandError block naming this
    # script and line, which reads like a failure in an otherwise passing run.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $Command 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                Write-Output $_.ToString()
            }
            else {
                Write-Output $_
            }
        }
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Get-PythonCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DepotToolsPath
    )

    $candidates = @(
        (Join-Path $DepotToolsPath "python3.bat"),
        "python3",
        "python"
    )
    foreach ($candidate in $candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if (-not $command) {
            continue
        }

        & $candidate --version *> $null
        if ($LASTEXITCODE -eq 0) {
            return $candidate
        }
    }

    throw (
        "Python was not found. The script checked Depot Tools and PATH. " +
        "Confirm that depot_tools\python3.bat exists."
    )
}

# Reads a property that a DevTools reply may omit. Strict mode treats reading an
# absent property as an error, so every optional field is read through here.
function Get-CdpProperty {
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

# Windows PowerShell 5.1 hands back a faulted task's failure as a
# MethodInvocationException wrapping an AggregateException, which reports the
# wrapper rather than the cause. This unwraps the chain so a failed DevTools
# operation names what actually failed.
function Wait-CdpTask {
    param(
        [Parameter(Mandatory = $true)]
        $Task,

        [int] $TimeoutMilliseconds = 15000,

        [string] $Description = "A DevTools operation"
    )

    $completed = $false
    try {
        $completed = $Task.Wait($TimeoutMilliseconds)
    }
    catch {
        $chain = @()
        $current = $_.Exception
        while ($current) {
            if ($current -is [System.AggregateException]) {
                foreach ($inner in $current.Flatten().InnerExceptions) {
                    $chain += (
                        "{0}: {1}" -f $inner.GetType().FullName, $inner.Message
                    )
                }
                $current = $current.Flatten().InnerExceptions[0].InnerException
                continue
            }
            $chain += ("{0}: {1}" -f $current.GetType().FullName, $current.Message)
            $current = $current.InnerException
        }
        throw ("$Description failed. " + ($chain -join " <- "))
    }
    if (-not $completed) {
        throw "$Description timed out after $TimeoutMilliseconds ms."
    }
    $Task
}

# Windows PowerShell 5.1 returns a JSON array from one HTTP response as a single
# object, so wrapping that response in an array can nest an Object[] inside a
# one-item array. Filtering the nested value reads properties by member
# enumeration, which joins every target's value into one string instead of
# returning one target. Flattening one level before any filtering runs is what
# keeps a filter operating on individual targets.
function Expand-CdpTargets {
    param($Response)

    $flat = New-Object System.Collections.ArrayList
    foreach ($chunk in @($Response)) {
        if ($null -eq $chunk) {
            continue
        }
        if ($chunk -isnot [string] -and
            $chunk -is [System.Collections.IEnumerable]) {
            foreach ($item in $chunk) {
                [void]$flat.Add($item)
            }
        }
        else {
            [void]$flat.Add($chunk)
        }
    }
    $flat.ToArray()
}

function Get-CdpTargetList {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DevToolsBase
    )

    $response = Invoke-WebRequest `
        -Uri "$DevToolsBase/json/list" `
        -UseBasicParsing `
        -TimeoutSec 2
    Expand-CdpTargets (ConvertFrom-Json $response.Content)
}

# Selects the fixture page target. A target is usable only when its own URL and
# WebSocket URL are scalar strings, which rejects a collection that looks like a
# single target under member access. The title gate is what the readiness poll
# relies on, and matching the URL exactly keeps a browser-interface page such as
# chrome://omnibox-popup.top-chrome from being selected.
function Select-CdpFixtureTarget {
    param(
        $Targets,

        [Parameter(Mandatory = $true)]
        [string] $FixtureUri,

        [Parameter(Mandatory = $true)]
        [string] $ReadyTitle
    )

    @(
        $Targets |
            Where-Object {
                (Get-CdpProperty $_ "type") -eq "page" -and
                (Get-CdpProperty $_ "url") -is [string] -and
                (Get-CdpProperty $_ "title") -eq $ReadyTitle -and
                (Get-CdpProperty $_ "webSocketDebuggerUrl") -is [string] -and
                $_.webSocketDebuggerUrl.Length -gt 0 -and
                $_.url.StartsWith($FixtureUri)
            }
    )
}

# Opens a second page and brings it to the front so the fixture page becomes
# hidden, which is what the page-lifecycle evidence needs. The identifier is
# read the same way as the target list rather than through Invoke-RestMethod,
# which does not enumerate a JSON array under Windows PowerShell 5.1, and the
# identifier is required to be a scalar string so that a response shape other
# than a single target fails here instead of producing an activate request for
# a target that does not exist.
function New-CdpBackgroundTarget {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DevToolsBase
    )

    $created = ConvertFrom-Json (
        Invoke-WebRequest `
            -Method Put `
            -Uri "$DevToolsBase/json/new?about%3Ablank" `
            -UseBasicParsing `
            -TimeoutSec 5
    ).Content
    $targetId = Get-CdpProperty $created "id"
    if ($targetId -isnot [string] -or $targetId.Length -eq 0) {
        throw "Opening a background DevTools target returned no identifier."
    }
    $activated = Invoke-WebRequest `
        -Method Put `
        -Uri "$DevToolsBase/json/activate/$targetId" `
        -UseBasicParsing `
        -TimeoutSec 5
    if ($activated.StatusCode -ne 200) {
        throw (
            "Activating the background DevTools target reported status " +
            "$($activated.StatusCode)."
        )
    }
    return $targetId
}

function New-CdpSession {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SocketUrl
    )

    if ($SocketUrl -notmatch '^wss?://[^\s]+$') {
        throw (
            "Unusable DevTools WebSocket URL '$SocketUrl'. More than one " +
            "target was probably selected."
        )
    }

    $socket = New-Object System.Net.WebSockets.ClientWebSocket
    $source = New-Object System.Threading.CancellationTokenSource
    $null = Wait-CdpTask `
        $socket.ConnectAsync([Uri]$SocketUrl, $source.Token) `
        15000 `
        "Connecting to the fixture's DevTools endpoint"
    if ($socket.State -ne [System.Net.WebSockets.WebSocketState]::Open) {
        throw "The DevTools socket state is $($socket.State) rather than Open."
    }

    [pscustomobject]@{
        Socket = $socket
        Source = $source
        NextId = 1
    }
}

function Send-CdpText {
    param(
        [Parameter(Mandatory = $true)]
        $Session,

        [Parameter(Mandatory = $true)]
        [string] $Text
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(, $bytes)
    $null = Wait-CdpTask `
        $Session.Socket.SendAsync(
            $segment,
            [System.Net.WebSockets.WebSocketMessageType]::Text,
            $true,
            $Session.Source.Token) `
        15000 `
        "Sending a DevTools command"
}

function Receive-CdpText {
    param(
        [Parameter(Mandatory = $true)]
        $Session
    )

    $buffer = New-Object byte[] 65536
    $builder = New-Object System.Text.StringBuilder
    while ($true) {
        $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(
            $buffer, 0, $buffer.Length
        )
        $receive = Wait-CdpTask `
            $Session.Socket.ReceiveAsync($segment, $Session.Source.Token) `
            15000 `
            "Reading a DevTools message"
        $result = $receive.Result
        if ($result.MessageType -eq
            [System.Net.WebSockets.WebSocketMessageType]::Close) {
            throw (
                "The browser closed the DevTools socket: " +
                "$($Session.Socket.CloseStatus) " +
                "$($Session.Socket.CloseStatusDescription)"
            )
        }
        [void]$builder.Append(
            [System.Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count)
        )
        if ($result.EndOfMessage) {
            break
        }
    }
    $builder.ToString()
}

# Sends one command and returns its reply. DevTools interleaves events with
# replies on the same socket, so messages are read until the reply carrying this
# command's identifier arrives.
function Invoke-CdpCommand {
    param(
        [Parameter(Mandatory = $true)]
        $Session,

        [Parameter(Mandatory = $true)]
        [string] $Method,

        [hashtable] $Parameters
    )

    $id = $Session.NextId
    $Session.NextId = $id + 1
    $command = @{ id = $id; method = $Method }
    if ($Parameters) {
        $command.params = $Parameters
    }
    Send-CdpText $Session (ConvertTo-Json $command -Depth 10 -Compress)

    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $message = ConvertFrom-Json (Receive-CdpText $Session)
        if ((Get-CdpProperty $message "id") -ne $id) {
            continue
        }
        $failure = Get-CdpProperty $message "error"
        if ($failure) {
            throw (
                "$Method failed: $($failure.message) (code $($failure.code))"
            )
        }
        return (Get-CdpProperty $message "result")
    }

    throw "No DevTools reply to $Method arrived."
}

function Close-CdpSession {
    param($Session)

    if (-not $Session) {
        return
    }
    try {
        if ($Session.Socket.State -eq
            [System.Net.WebSockets.WebSocketState]::Open) {
            $null = Wait-CdpTask `
                $Session.Socket.CloseAsync(
                    [System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,
                    "done",
                    $Session.Source.Token) `
                5000 `
                "Closing the DevTools socket"
        }
    }
    catch {
        Write-Host "Closing the DevTools socket failed: $($_.Exception.Message)"
    }
    finally {
        $Session.Socket.Dispose()
        $Session.Source.Dispose()
    }
}

# Registers a listener in an isolated world so the capture contains a
# registration the page's own script cannot make. The world is created in the
# fixture's main frame, the listener is registered on an element no document
# script touches, and the marker read from the main world afterwards is what
# shows the two worlds were separate rather than one world under another name.
function Add-IsolatedWorldListener {
    param(
        [Parameter(Mandatory = $true)]
        $Session,

        [Parameter(Mandatory = $true)]
        [string] $WorldName
    )

    $null = Invoke-CdpCommand $Session "Page.enable" $null
    $null = Invoke-CdpCommand $Session "Runtime.enable" $null
    $tree = Invoke-CdpCommand $Session "Page.getFrameTree" $null
    $frameId = [string]$tree.frameTree.frame.id
    if ([string]::IsNullOrWhiteSpace($frameId)) {
        throw "The fixture frame tree reported no main frame identifier."
    }

    $world = Invoke-CdpCommand $Session "Page.createIsolatedWorld" @{
        frameId = $frameId
        worldName = $WorldName
        grantUniversalAccess = $false
    }
    $contextId = Get-CdpProperty $world "executionContextId"
    if (-not $contextId) {
        throw "Creating the isolated world returned no execution context."
    }

    $registration = Invoke-CdpCommand $Session "Runtime.evaluate" @{
        contextId = $contextId
        returnByValue = $true
        expression = @'
(function () {
  window.__a11yRecorderIsolatedMarker = "isolated";
  var target = document.getElementById("isolated-world-target");
  if (!target) {
    return "no-target";
  }
  target.addEventListener("click", function () {}, false);
  return "registered";
})()
'@
    }
    $failure = Get-CdpProperty $registration "exceptionDetails"
    if ($failure) {
        throw "The isolated-world registration failed: $($failure.text)"
    }
    $outcome = [string](Get-CdpProperty $registration.result "value")
    if ($outcome -ne "registered") {
        throw (
            "The isolated-world script reported '$outcome' rather than a " +
            "completed registration."
        )
    }

    $mainWorld = Invoke-CdpCommand $Session "Runtime.evaluate" @{
        returnByValue = $true
        expression = "String(window.__a11yRecorderIsolatedMarker)"
    }
    $marker = [string](Get-CdpProperty $mainWorld.result "value")
    if ($marker -ne "undefined") {
        throw (
            "The main world can see the isolated world's marker, so the " +
            "registration was not made from a separate world."
        )
    }

    [pscustomobject]@{
        FrameId = $frameId
        ExecutionContextId = $contextId
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if ($principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )) {
    throw (
        "Run this script in a standard, non-elevated PowerShell window. " +
        "Elevated capture cannot connect to the non-elevated Chromium process."
    )
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$chromiumSource = (
    Resolve-Path -LiteralPath $ChromiumSource
).Path
$depotTools = (Resolve-Path -LiteralPath $DepotTools).Path
$dotnet = (Resolve-Path -LiteralPath $DotnetPath).Path
$python = Get-PythonCommand -DepotToolsPath $depotTools
$autoninja = Join-Path $depotTools "autoninja.bat"
$browser = Join-Path $chromiumSource "out\A11yRecorder\chrome.exe"
$fixture = Join-Path (
    Join-Path $repositoryRoot "tests"
) "fixtures\blink-listener-dispatch.html"
$captureProject = Join-Path (
    Join-Path $repositoryRoot "src"
) "Recorder.CaptureHost\Recorder.CaptureHost.csproj"
$solution = Join-Path $repositoryRoot "windows-a11y-recorder.slnx"
$integrationScript = Join-Path $repositoryRoot "chromium\integrate.py"
$integrationTests = Join-Path $repositoryRoot "chromium\test_integrate.py"
$verifier = Join-Path $PSScriptRoot "Verify-BlinkEvidence.ps1"

foreach ($requiredPath in @(
        $autoninja,
        $fixture,
        $captureProject,
        $solution,
        $integrationScript,
        $integrationTests,
        $verifier
    )) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required file not found: $requiredPath"
    }
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $OutputRoot).Path
$fixtureUri = [Uri]::new($fixture).AbsoluteUri
$bridgeLog = Join-Path $outputRoot "bridge-startup.log"
$chromiumLog = Join-Path $outputRoot "chromium.log"
$debugPortListener = [Net.Sockets.TcpListener]::new(
    [Net.IPAddress]::Loopback,
    0
)
$debugPortListener.Start()
$debugPort = (
    [Net.IPEndPoint] $debugPortListener.LocalEndpoint
).Port
$debugPortListener.Stop()

Invoke-Checked "Testing the Chromium integration script" {
    & $python $integrationTests
}

Invoke-Checked "Applying the Chromium integration" {
    & $python $integrationScript $chromiumSource
}

# Zip timestamps can be ahead of the local system clock and prevent GN from
# regenerating the build. Normalize only files managed by this integration.
$now = Get-Date
$integratedFiles = @(
    (Join-Path $chromiumSource "chrome\app\chrome_main_delegate.cc"),
    (Join-Path $chromiumSource "chrome\BUILD.gn"),
    (
        Join-Path $chromiumSource (
            "content\browser\child_process_launcher_helper.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "content\browser\child_process_launcher_helper_win.cc"
        )
    ),
    (Join-Path $chromiumSource "content\browser\BUILD.gn"),
    (
        Join-Path $chromiumSource (
            "content\renderer\accessibility\render_accessibility_impl.cc"
        )
    ),
    (Join-Path $chromiumSource "content\renderer\BUILD.gn"),
    (
        Join-Path $chromiumSource (
            "content\browser\web_contents\web_contents_impl.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\events\event_target.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\events\event_dispatcher.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\document.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\mutation_observer.h"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\mutation_observer.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\scheduler\dom_timer.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\" +
            "frame_request_callback_collection.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\scheduler\" +
            "scripted_idle_task_controller.cc"
        )
    ),
    (
        Join-Path $chromiumSource "third_party\blink\renderer\core\BUILD.gn"
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\platform\scheduler\common\" +
            "throttling\task_queue_throttler.h"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\platform\scheduler\common\" +
            "throttling\task_queue_throttler.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\platform\scheduler\main_thread\" +
            "main_thread_task_queue.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\platform\scheduler\main_thread\" +
            "frame_scheduler_impl.h"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\platform\scheduler\BUILD.gn"
        )
    )
)
$integratedFiles += Get-ChildItem -LiteralPath (
    Join-Path $chromiumSource "chromium\recorder_bridge"
) -File -Recurse |
    Select-Object -ExpandProperty FullName
$integratedFiles | ForEach-Object {
    $integratedPath = [string] $_
    if (-not (Test-Path -LiteralPath $integratedPath -PathType Leaf)) {
        throw "Integrated file not found: $integratedPath"
    }
    (Get-Item -LiteralPath $integratedPath).LastWriteTime = $now
}

Invoke-Checked "Building instrumented Chromium" {
    & $autoninja -C (
        Join-Path $chromiumSource "out\A11yRecorder"
    ) chrome
}

if (-not (Test-Path -LiteralPath $browser -PathType Leaf)) {
    throw "The Chromium build did not produce $browser."
}

Invoke-Checked "Testing the managed recorder" {
    & $dotnet test $solution --configuration Release
}

$sessionsBefore = @(
    Get-ChildItem -LiteralPath $outputRoot -Directory -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
)

Remove-Item -LiteralPath $bridgeLog -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $chromiumLog -Force -ErrorAction SilentlyContinue
$previousBridgeLog = $env:A11Y_RECORDER_BRIDGE_LOG_FILE
$previousChromiumLog = $env:A11Y_RECORDER_CHROMIUM_LOG_FILE
$captureJob = $null
$env:A11Y_RECORDER_BRIDGE_LOG_FILE = $bridgeLog
$env:A11Y_RECORDER_CHROMIUM_LOG_FILE = $chromiumLog
try {
    Write-Host "`n== Capturing the deterministic Blink fixture =="
    $captureJob = Start-Job -ScriptBlock {
        param(
            $Dotnet,
            $CaptureProject,
            $OutputRoot,
            $DurationSeconds,
            $Browser,
            $FixtureUri,
            $DebugPort,
            $BridgeLog,
            $ChromiumLog
        )
        $env:A11Y_RECORDER_BRIDGE_LOG_FILE = $BridgeLog
        $env:A11Y_RECORDER_CHROMIUM_LOG_FILE = $ChromiumLog
        & $Dotnet run `
            --project $CaptureProject `
            --configuration Release `
            -- `
            --output $OutputRoot `
            --duration-seconds $DurationSeconds `
            --browser-path $Browser `
            --browser-url $FixtureUri `
            --browser-remote-debugging-port $DebugPort
        [pscustomobject]@{
            CaptureExitCode = $LASTEXITCODE
        }
    } -ArgumentList @(
        $dotnet,
        $captureProject,
        $outputRoot,
        $DurationSeconds,
        $browser,
        $fixtureUri,
        $debugPort,
        $bridgeLog,
        $chromiumLog
    )

    $devToolsBase = "http://127.0.0.1:$debugPort"
    $fixtureTarget = $null
    $debugDeadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $debugDeadline) {
        if ($captureJob.State -eq "Failed") {
            break
        }
        # Any failure here means the debugging endpoint is not serving a target
        # list yet, which is expected until Chromium has started listening.
        $candidates = @()
        try {
            $candidates = @(
                Select-CdpFixtureTarget `
                    (Get-CdpTargetList $devToolsBase) `
                    $fixtureUri `
                    "Blink listener and dispatch fixture ready"
            )
        }
        catch {
            $candidates = @()
        }
        if ($candidates.Count -gt 1) {
            Stop-Job $captureJob -ErrorAction SilentlyContinue
            throw (
                "$($candidates.Count) DevTools targets match the fixture, so " +
                "the fixture page cannot be identified."
            )
        }
        if ($candidates.Count -eq 1) {
            $fixtureTarget = $candidates[0]
            break
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $fixtureTarget) {
        Stop-Job $captureJob -ErrorAction SilentlyContinue
        throw (
            "The lifecycle fixture did not report readiness in the DevTools " +
            "target list."
        )
    }

    # A registration made from an isolated world is the only way this fixture
    # can produce a listener record for a world other than the main world,
    # because a page's own script always runs in the main world. The world is
    # created over the same debugging endpoint the readiness poll used.
    $isolatedWorldSession = $null
    try {
        $isolatedWorldSession = New-CdpSession $fixtureTarget.webSocketDebuggerUrl
        $isolatedWorld = Add-IsolatedWorldListener `
            $isolatedWorldSession `
            "A11yRecorderValidationWorld"
        Write-Host (
            "Registered an isolated-world listener in frame " +
            "$($isolatedWorld.FrameId), execution context " +
            "$($isolatedWorld.ExecutionContextId)."
        )
    }
    catch {
        Stop-Job $captureJob -ErrorAction SilentlyContinue
        throw
    }
    finally {
        Close-CdpSession $isolatedWorldSession
    }

    $backgroundTargetId = New-CdpBackgroundTarget $devToolsBase
    Write-Host (
        "Activated background target $backgroundTargetId so the fixture page " +
        "becomes hidden."
    )

    Wait-Job $captureJob | Out-Null
    $captureErrors = @()
    $captureOutput = @(
        Receive-Job `
            $captureJob `
            -ErrorAction SilentlyContinue `
            -ErrorVariable +captureErrors
    )
    # A job's error stream can carry a record whose exception has no message,
    # which renders as a bare category line such as the RemoteException category
    # of the remoting wrapper. In a run that passed, that line reads like a
    # failure. Such a record says nothing, so it is counted rather than printed,
    # and the count is reported so nothing is dropped without being stated.
    $emptyCaptureErrors = 0
    $captureErrors |
        ForEach-Object {
            $errorText = ""
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                $errorText = [string] $_.Exception.Message
            }
            else {
                $errorText = [string] $_
            }
            if ([string]::IsNullOrWhiteSpace($errorText)) {
                ++$emptyCaptureErrors
                return
            }
            Write-Host $errorText
        }
    if ($emptyCaptureErrors -gt 0) {
        Write-Host (
            "The capture job's error stream carried $emptyCaptureErrors " +
            "record(s) with no message, which report nothing and are not " +
            "failures."
        )
    }
    $captureResult = $captureOutput |
        Where-Object {
            $_.PSObject.Properties.Name -contains "CaptureExitCode"
        } |
        Select-Object -Last 1
    $captureOutput |
        Where-Object {
            $_.PSObject.Properties.Name -notcontains "CaptureExitCode"
        } |
        ForEach-Object { Write-Host $_ }
    if (-not $captureResult) {
        throw "The capture job did not report an exit code."
    }
    if ($captureResult.CaptureExitCode -ne 0) {
        throw (
            "Capturing the deterministic Blink fixture failed with exit " +
            "code $($captureResult.CaptureExitCode)."
        )
    }
}
finally {
    if ($captureJob) {
        Remove-Job $captureJob -Force -ErrorAction SilentlyContinue
    }
    if ($null -eq $previousBridgeLog) {
        Remove-Item `
            Env:A11Y_RECORDER_BRIDGE_LOG_FILE `
            -ErrorAction SilentlyContinue
    }
    else {
        $env:A11Y_RECORDER_BRIDGE_LOG_FILE = $previousBridgeLog
    }
    if ($null -eq $previousChromiumLog) {
        Remove-Item `
            Env:A11Y_RECORDER_CHROMIUM_LOG_FILE `
            -ErrorAction SilentlyContinue
    }
    else {
        $env:A11Y_RECORDER_CHROMIUM_LOG_FILE = $previousChromiumLog
    }
}

$session = Get-ChildItem -LiteralPath $outputRoot -Directory |
    Where-Object { $_.FullName -notin $sessionsBefore } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $session) {
    throw "Capture completed but no new session directory was found."
}

$validationPath = Join-Path (
    $session.FullName
) "diagnostics\archive-validation.json"
if (-not (Test-Path -LiteralPath $validationPath -PathType Leaf)) {
    throw "The session does not contain an archive-validation report."
}
$validation = Get-Content -LiteralPath $validationPath -Raw |
    ConvertFrom-Json
if (-not $validation.isValid) {
    $errors = @(
        $validation.issues |
            Where-Object { $_.severity -eq "Error" } |
            ForEach-Object { "$($_.code) at $($_.path)" }
    )
    throw "Archive validation failed: $($errors -join '; ')"
}

try {
    & $verifier -SessionPath $session.FullName
}
catch {
    if (Test-Path -LiteralPath $bridgeLog -PathType Leaf) {
        Write-Host "`nNative recorder bridge trace:"
        Get-Content -LiteralPath $bridgeLog
    }
    if (Test-Path -LiteralPath $chromiumLog -PathType Leaf) {
        Write-Host "`nChromium log tail:"
        Get-Content -LiteralPath $chromiumLog -Tail 250
    }
    throw
}

# A process whose bridge failed to initialize exits, so its evidence is missing
# from the archive and nothing else in the run reports that it existed. The
# archive can still validate and the verifier can still pass, so the bridge
# trace is read here rather than only printed. A utility process reporting that
# it received no bootstrap handle is the expected path for a process with no
# evidence hooks and is not a failure.
$bridgeLines = @()
if (Test-Path -LiteralPath $bridgeLog -PathType Leaf) {
    $bridgeLines = @(Get-Content -LiteralPath $bridgeLog)
}
$bridgeFailures = @(
    $bridgeLines |
        Where-Object {
            $_ -match "bridge pipe connection failed" -or
            $_ -match "bridge initialization failed" -or
            $_ -match "bootstrap parsing failed" -or
            $_ -match "bootstrap read failed"
        }
)
# An evidence write that failed after the recorder closed its end of the pipe is
# an ordinary shutdown race rather than lost evidence, so these are counted and
# reported instead of failing the run.
$bridgeWriteFailures = @(
    $bridgeLines | Where-Object { $_ -match "evidence write failed" }
).Count
if ($bridgeFailures.Count -gt 0) {
    Write-Host "`nNative recorder bridge trace:"
    Get-Content -LiteralPath $bridgeLog
    throw (
        "$($bridgeFailures.Count) process(es) reported a recorder bridge " +
        "failure, so their evidence is missing from this session: " +
        ($bridgeFailures -join "; ")
    )
}
$bridgeConnectWaits = @(
    $bridgeLines | Where-Object { $_ -match "waited for a free pipe instance" }
).Count

$networkServiceCrashes = @(
    Select-String `
        -LiteralPath $chromiumLog `
        -Pattern "Network service crashed or was terminated" `
        -ErrorAction SilentlyContinue
).Count
if ($networkServiceCrashes -gt 0) {
    throw (
        "Chromium reported $networkServiceCrashes network-service crash(es). " +
        "The recorder must not destabilize unrelated utility processes."
    )
}

Write-Host "`nBlink validation completed successfully."
Write-Host "SESSION_PATH=$($session.FullName)"
Write-Host "BRIDGE_LOG=$bridgeLog"
Write-Host "CHROMIUM_LOG=$chromiumLog"
Write-Host "ARCHIVE_VALID=$($validation.isValid)"
Write-Host "EVENTS_VALIDATED=$($validation.eventsValidated)"
Write-Host "ARTIFACTS_VALIDATED=$($validation.artifactsValidated)"
Write-Host "NETWORK_SERVICE_CRASHES=$networkServiceCrashes"
Write-Host "BRIDGE_CONNECT_WAITS=$bridgeConnectWaits"
Write-Host "BRIDGE_WRITE_FAILURES=$bridgeWriteFailures"
Write-Host "BRIDGE_INITIALIZATION_FAILURES=0"

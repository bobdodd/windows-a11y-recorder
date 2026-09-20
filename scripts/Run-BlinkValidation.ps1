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
    & $Command
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
        try {
            $targets = @(
                Invoke-RestMethod `
                    -Uri "$devToolsBase/json/list" `
                    -TimeoutSec 1
            )
            $fixtureTarget = $targets |
                Where-Object {
                    $_.url.StartsWith($fixtureUri) -and
                    $_.title -eq "Blink listener and dispatch fixture ready"
                } |
                Select-Object -First 1
            if ($fixtureTarget) {
                break
            }
        }
        catch {
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

    $backgroundTarget = Invoke-RestMethod `
        -Method Put `
        -Uri "$devToolsBase/json/new?about%3Ablank" `
        -TimeoutSec 5
    Invoke-RestMethod `
        -Method Put `
        -Uri "$devToolsBase/json/activate/$($backgroundTarget.id)" `
        -TimeoutSec 5 |
        Out-Null

    Wait-Job $captureJob | Out-Null
    $captureErrors = @()
    $captureOutput = @(
        Receive-Job `
            $captureJob `
            -ErrorAction SilentlyContinue `
            -ErrorVariable +captureErrors
    )
    $captureErrors |
        ForEach-Object {
            Write-Host $_
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

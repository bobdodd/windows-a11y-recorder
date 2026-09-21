#Requires -Version 5.1
<#
.SYNOPSIS
    Provokes a recorder bridge initialization failure in the instrumented
    browser and checks that the failure is reported so an operator can act on
    it.

.DESCRIPTION
    A failed bridge is observable to the recorder only as an exit code within
    its startup window, and the reason for the failure is only recoverable from
    the bridge diagnostic log. This script drives both halves of that contract
    directly, without a recording session:

      1. The browser must exit with the bridge initialization failure code
         rather than a normal exit code.
      2. The bridge diagnostic log must contain a reason, prefixed by the
         marker the recorder searches for.
      3. The reason must be specific to the failure, so two different failures
         are not reported identically.

    The expected exit code and marker text are read from the native and managed
    sources rather than restated here, so this script fails if the two sides
    ever disagree.

    No pipe server is started. The first case is rejected while the bootstrap is
    parsed, and the second is rejected when the browser cannot reach the
    recorder's pipe.
#>

[CmdletBinding()]
param(
    [string] $ChromiumExecutable = (Join-Path $env:USERPROFILE `
            "chromium-dev\chromium\src\out\A11yRecorder\chrome.exe"),
    [string] $OutputRoot = (Join-Path $env:TEMP "a11y-recorder-bridge-failure"),
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$switchesHeader = Join-Path $repositoryRoot `
    "chromium\recorder_bridge\recorder_switches.h"
$launcherSource = Join-Path $repositoryRoot `
    "src\Recorder.Collectors.Browser\ChromiumLauncher.cs"
$protocolHeader = Join-Path $repositoryRoot `
    "chromium\recorder_bridge\recorder_protocol.h"

function Get-SingleMatch {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [Parameter(Mandatory = $true)] [string] $Pattern,
        [Parameter(Mandatory = $true)] [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Source file not found: $Path"
    }
    $text = Get-Content -LiteralPath $Path -Raw
    $found = [regex]::Matches($text, $Pattern)
    if ($found.Count -ne 1) {
        throw "Expected exactly one $Description in $Path, found $($found.Count)."
    }
    return $found[0].Groups[1].Value
}

# The native exit code and the managed constant must be the same value, and the
# marker the hook writes must be the text the recorder searches for.
$nativeExitCode = [Convert]::ToInt32(
    (Get-SingleMatch -Path $switchesHeader `
        -Pattern 'kBridgeInitializationFailureExitCode\s*=\s*(0x[0-9A-Fa-f]+)' `
        -Description "native bridge failure exit code"), 16)
$managedExitCode = [Convert]::ToInt32(
    (Get-SingleMatch -Path $launcherSource `
        -Pattern 'BridgeInitializationFailureExitCode\s*=\s*(0x[0-9A-Fa-f]+)' `
        -Description "managed bridge failure exit code"), 16)
$marker = Get-SingleMatch -Path $launcherSource `
    -Pattern 'BridgeInitializationFailureMarker\s*=\s*\r?\n?\s*"([^"]+)"' `
    -Description "bridge failure marker"
$supportedProtocol = Get-SingleMatch -Path $protocolHeader `
    -Pattern 'kProtocolVersion\[\]\s*=\s*"([^"]+)"' `
    -Description "supported protocol version"

if ($nativeExitCode -ne $managedExitCode) {
    throw ("The native bridge failure exit code $nativeExitCode does not " +
        "match the managed constant $managedExitCode.")
}

if (-not (Test-Path -LiteralPath $ChromiumExecutable -PathType Leaf)) {
    throw "Instrumented Chromium was not found: $ChromiumExecutable"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

Write-Host "Instrumented browser: $ChromiumExecutable"
Write-Host "Supported protocol version: $supportedProtocol"
Write-Host ("Expected failure exit code: {0} (0x{0:X})" -f $nativeExitCode)
Write-Host "Expected reason marker: $marker"
Write-Host ""

function Invoke-BridgeFailure {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $ProtocolVersion
    )

    $caseRoot = Join-Path $OutputRoot $Name
    if (Test-Path -LiteralPath $caseRoot) {
        Remove-Item -LiteralPath $caseRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $caseRoot | Out-Null
    $profileDirectory = Join-Path $caseRoot "profile"
    New-Item -ItemType Directory -Force -Path $profileDirectory | Out-Null
    $bridgeLog = Join-Path $caseRoot "browser-bridge.log"

    $bootstrap = [ordered] @{
        kind = "a11y-recorder-bootstrap"
        protocolVersion = $ProtocolVersion
        # No pipe server is listening on this name by design.
        pipeName = "a11y-recorder-bridge-failure-$([guid]::NewGuid().ToString('N'))"
        authenticationToken = [guid]::NewGuid().ToString("N")
        browserInstanceId = [guid]::NewGuid().ToString("N")
        maximumMessageBytes = 1048576
    } | ConvertTo-Json -Compress

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $ChromiumExecutable
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.EnvironmentVariables["A11Y_RECORDER_BRIDGE_LOG_FILE"] = $bridgeLog
    # ArgumentList is unavailable on the .NET Framework that Windows PowerShell
    # 5.1 runs on, so the command line is quoted here instead.
    $startInfo.Arguments = @(
        "--a11y-recorder-bootstrap=stdin",
        "--user-data-dir=`"$profileDirectory`"",
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-background-mode",
        "about:blank") -join " "

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.StandardInput.WriteLine($bootstrap)
    $process.StandardInput.Close()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        # Chromium starts child processes, so the tree is ended rather than the
        # one process. Kill(bool) is unavailable on Windows PowerShell 5.1.
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        throw ("Case '$Name': the browser did not exit within " +
            "$TimeoutSeconds seconds, so a failed bridge did not stop it.")
    }

    $reason = $null
    if (Test-Path -LiteralPath $bridgeLog -PathType Leaf) {
        $reason = Get-Content -LiteralPath $bridgeLog |
            Where-Object { $_ -like "*$marker*" } |
            Select-Object -Last 1
    }

    return [pscustomobject] @{
        Name = $Name
        ExitCode = $process.ExitCode
        BridgeLog = $bridgeLog
        Reason = $reason
    }
}

$failures = New-Object System.Collections.Generic.List[string]

# A bootstrap that names an unsupported protocol version is the failure this
# check was written for: a published application older than the browser it
# starts. It is rejected while the bootstrap is parsed.
# Deliberately unlike any real version, and not a substring of a real one, so
# the assertions below cannot pass on a coincidence.
$unsupportedVersion = "0.0-stale-application"
$cases = @(
    (Invoke-BridgeFailure -Name "unsupported-protocol-version" `
        -ProtocolVersion $unsupportedVersion),
    # A supported version with no pipe server reaches the connection attempt, so
    # its reason must differ from the version rejection above.
    (Invoke-BridgeFailure -Name "unreachable-recorder-pipe" `
        -ProtocolVersion $supportedProtocol)
)

foreach ($case in $cases) {
    Write-Host "== $($case.Name) =="
    Write-Host ("Exit code: {0} (0x{0:X})" -f $case.ExitCode)
    Write-Host "Bridge log: $($case.BridgeLog)"
    Write-Host "Reason: $(if ($case.Reason) { $case.Reason } else { '<none recorded>' })"
    Write-Host ""

    if ($case.ExitCode -ne $nativeExitCode) {
        $failures.Add(("$($case.Name): expected exit code $nativeExitCode, " +
            "observed $($case.ExitCode)."))
    }
    if (-not $case.Reason) {
        $failures.Add(("$($case.Name): no reason prefixed by '$marker' was " +
            "recorded in $($case.BridgeLog)."))
    }
}

if ($cases[0].Reason -and $cases[1].Reason -and
    $cases[0].Reason -eq $cases[1].Reason) {
    $failures.Add("Both failures recorded the same reason, so the reason does not identify the failure.")
}
# A mismatch cannot be acted on unless the reason says which version was sent
# and which one the browser requires, because either half may be the stale one.
if ($cases[0].Reason) {
    if ($cases[0].Reason -notlike "*$unsupportedVersion*") {
        $failures.Add(("unsupported-protocol-version: the recorded reason does " +
            "not name the rejected version '$unsupportedVersion': " +
            "$($cases[0].Reason)"))
    }
    if ($cases[0].Reason -notlike "*$supportedProtocol*") {
        $failures.Add(("unsupported-protocol-version: the recorded reason does " +
            "not name the version this browser requires " +
            "('$supportedProtocol'): $($cases[0].Reason)"))
    }
}

if ($failures.Count -gt 0) {
    Write-Host "BRIDGE FAILURE REPORTING IS INCORRECT"
    foreach ($failure in $failures) {
        Write-Host "  $failure"
    }
    exit 1
}

Write-Host "BRIDGE FAILURE REPORTING VERIFIED"
Write-Host ("A failed bridge exits with {0} and records a reason the recorder " -f $nativeExitCode)
Write-Host "can name, and two different failures are reported differently."
exit 0

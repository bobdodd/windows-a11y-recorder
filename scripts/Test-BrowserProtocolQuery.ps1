#Requires -Version 5.1
<#
.SYNOPSIS
    Checks that the instrumented browser reports the evidence protocol version
    it was built with, and reports the same version the recorder speaks.

.DESCRIPTION
    The recorder asks the browser for its protocol version before starting a
    session, so that a recorder and a browser built from different revisions are
    refused before anything is recorded. This check drives that query directly:

      1. The browser must write its version behind the agreed output prefix.
      2. It must exit with the query exit code, which is what distinguishes a
         browser that answered from one that ignored an unknown switch.
      3. The reported version must equal the version declared in both the native
         and managed sources, which are kept in step by hand.
      4. The query must not leave a browser running.

    The expected switch, exit code, prefix, and versions are read from the
    sources rather than restated here, so this check fails if the two sides of
    that contract diverge.
#>

[CmdletBinding()]
param(
    [string] $ChromiumExecutable = (Join-Path $env:USERPROFILE `
            "chromium-dev\chromium\src\out\A11yRecorder\chrome.exe"),
    [string] $OutputRoot = (Join-Path $env:TEMP "a11y-recorder-protocol-query"),
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$switchesHeader = Join-Path $repositoryRoot `
    "chromium\recorder_bridge\recorder_switches.h"
$protocolHeader = Join-Path $repositoryRoot `
    "chromium\recorder_bridge\recorder_protocol.h"
$contracts = Join-Path $repositoryRoot `
    "src\Recorder.Contracts\BrowserEvidenceContracts.cs"
$launcherSource = Join-Path $repositoryRoot `
    "src\Recorder.Collectors.Browser\ChromiumLauncher.cs"

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

$querySwitch = Get-SingleMatch -Path $switchesHeader `
    -Pattern 'kPrintProtocolVersionSwitch\[\]\s*=\s*\r?\n?\s*"([^"]+)"' `
    -Description "native query switch"
$queryExitCode = [Convert]::ToInt32(
    (Get-SingleMatch -Path $switchesHeader `
        -Pattern 'kProtocolVersionQueryExitCode\s*=\s*(0x[0-9A-Fa-f]+)' `
        -Description "native query exit code"), 16)
$outputPrefix = Get-SingleMatch -Path $switchesHeader `
    -Pattern 'kProtocolVersionOutputPrefix\[\]\s*=\s*\r?\n?\s*"([^"]+)"' `
    -Description "native output prefix"
$nativeVersion = Get-SingleMatch -Path $protocolHeader `
    -Pattern 'kProtocolVersion\[\]\s*=\s*"([^"]+)"' `
    -Description "native protocol version"
$managedVersion = Get-SingleMatch -Path $contracts `
    -Pattern 'CurrentVersion\s*=\s*"([^"]+)"' `
    -Description "managed protocol version"
$managedExitCode = [Convert]::ToInt32(
    (Get-SingleMatch -Path $launcherSource `
        -Pattern 'ProtocolVersionQueryExitCode\s*=\s*(0x[0-9A-Fa-f]+)' `
        -Description "managed query exit code"), 16)
$managedPrefix = Get-SingleMatch -Path $launcherSource `
    -Pattern 'ProtocolVersionOutputPrefix\s*=\s*\r?\n?\s*"([^"]+)"' `
    -Description "managed output prefix"

$failures = New-Object System.Collections.Generic.List[string]

if ($nativeVersion -ne $managedVersion) {
    $failures.Add(("The native protocol version $nativeVersion does not " +
        "match the managed version $managedVersion."))
}
if ($queryExitCode -ne $managedExitCode) {
    $failures.Add(("The native query exit code $queryExitCode does not match " +
        "the managed constant $managedExitCode."))
}
if ($outputPrefix -ne $managedPrefix) {
    $failures.Add(("The native output prefix '$outputPrefix' does not match " +
        "the managed prefix '$managedPrefix'."))
}

if (-not (Test-Path -LiteralPath $ChromiumExecutable -PathType Leaf)) {
    throw "Instrumented Chromium was not found: $ChromiumExecutable"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$profileDirectory = Join-Path $OutputRoot "profile"
New-Item -ItemType Directory -Force -Path $profileDirectory | Out-Null
$outputFile = Join-Path $OutputRoot "query-output.txt"
$errorFile = Join-Path $OutputRoot "query-error.txt"

Write-Host "Instrumented browser: $ChromiumExecutable"
Write-Host "Query switch: --$querySwitch"
Write-Host ("Expected query exit code: {0} (0x{0:X})" -f $queryExitCode)
Write-Host "Expected version: $nativeVersion"
Write-Host ""

$process = Start-Process -FilePath $ChromiumExecutable -PassThru -NoNewWindow `
    -RedirectStandardOutput $outputFile -RedirectStandardError $errorFile `
    -ArgumentList @(
        "--$querySwitch",
        "--no-startup-window",
        "--user-data-dir=`"$profileDirectory`"",
        "--no-first-run",
        "--no-default-browser-check")

# Reading the handle caches it while the process is alive. Without this, the exit
# code of a process started through Start-Process is unavailable after it exits
# on Windows PowerShell 5.1, which would report an answered query as unanswered.
$null = $process.Handle

if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    & taskkill.exe /PID $process.Id /T /F | Out-Null
    throw ("The browser did not exit within $TimeoutSeconds seconds, so the " +
        "query did not stop it.")
}
$exitCode = $process.ExitCode
if ($null -eq $exitCode) {
    throw ("The browser exited but its exit code could not be read, so the " +
        "query result cannot be judged.")
}

$output = ""
if (Test-Path -LiteralPath $outputFile -PathType Leaf) {
    $output = (Get-Content -LiteralPath $outputFile -Raw)
}
$reported = $null
$match = [regex]::Match($output, [regex]::Escape($outputPrefix) + '([^\r\n]+)')
if ($match.Success) {
    $reported = $match.Groups[1].Value.Trim()
}

Write-Host ("Exit code: {0} (0x{0:X})" -f $exitCode)
Write-Host "Reported version: $(if ($reported) { $reported } else { '<none>' })"
Write-Host ""

if ($exitCode -ne $queryExitCode) {
    $failures.Add(("Expected exit code $queryExitCode, observed " +
        "$exitCode. A browser that answers the query must exit " +
        "with the query exit code."))
}
if (-not $reported) {
    $failures.Add(("The browser reported no protocol version behind " +
        "'$outputPrefix'. Output was kept at $outputFile."))
}
elseif ($reported -ne $nativeVersion) {
    $failures.Add(("The browser reported protocol version '$reported' but the " +
        "sources declare '$nativeVersion', so this browser was built from a " +
        "different revision."))
}

$survivors = @(
    Get-Process -Name "chrome" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq (Resolve-Path -LiteralPath $ChromiumExecutable).Path }
)
if ($survivors.Count -gt 0) {
    $failures.Add(("The query left $($survivors.Count) browser process(es) " +
        "running, so it can start a browser nobody asked for."))
}

if ($failures.Count -gt 0) {
    Write-Host "PROTOCOL VERSION QUERY IS INCORRECT"
    foreach ($failure in $failures) {
        Write-Host "  $failure"
    }
    exit 1
}

Write-Host "PROTOCOL VERSION QUERY VERIFIED"
Write-Host ("The browser reports protocol version $nativeVersion, exits with " +
    "$queryExitCode, and starts nothing.")
exit 0

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
    [int] $DurationSeconds = 10
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
    foreach ($candidate in @("python3", "python")) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($command) {
            return $command.Source
        }
    }

    throw "Python was not found. Install Python or add python/python3 to PATH."
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
$python = Get-PythonCommand
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
            "third_party\blink\renderer\core\dom\events\event_target.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\events\event_dispatcher.cc"
        )
    ),
    (
        Join-Path $chromiumSource "third_party\blink\renderer\core\BUILD.gn"
    )
)
$integratedFiles += Get-ChildItem -LiteralPath (
    Join-Path $chromiumSource "chromium\recorder_bridge"
) -File -Recurse
$integratedFiles | ForEach-Object {
    (Get-Item -LiteralPath $_).LastWriteTime = $now
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

Invoke-Checked "Capturing the deterministic Blink fixture" {
    & $dotnet run `
        --project $captureProject `
        --configuration Release `
        -- `
        --output $outputRoot `
        --duration-seconds $DurationSeconds `
        --browser-path $browser `
        --browser-url $fixtureUri
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

& $verifier -SessionPath $session.FullName

Write-Host "`nBlink validation completed successfully."
Write-Host "SESSION_PATH=$($session.FullName)"
Write-Host "ARCHIVE_VALID=$($validation.isValid)"
Write-Host "EVENTS_VALIDATED=$($validation.eventsValidated)"
Write-Host "ARTIFACTS_VALIDATED=$($validation.artifactsValidated)"

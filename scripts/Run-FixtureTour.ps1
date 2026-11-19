#Requires -Version 5.1
<#
.SYNOPSIS
    Installs the fixture app on the phone and opens each of its tour
    screens in turn, while the recorder records.

.DESCRIPTION
    1. Finds the recorder's own adb, in
       %LOCALAPPDATA%\Windows A11y Recorder\android\platform-tools, so the
       tour talks to the recorder's adb server rather than starting one of
       another version, which would stop the server under a recording.
    2. Installs the fixture app, built by scripts\Build-FixtureApp.ps1.
    3. Asks you to press Record in the recorder, and waits for Enter.
    4. Opens each screen android\fixtures\expected.json marks "tour", as a
       new task, and holds it for its "seconds".
    5. Says when it has finished, so you can press Stop.

    Screens marked "person" are left for a person to open from the app's
    index. See docs\architecture\android-fixtures.md.

    Runs on Windows PowerShell 5.1 and on PowerShell 7.
#>

[CmdletBinding()]
param(
    # The fixture app to install; by default the one Build-FixtureApp.ps1 wrote.
    [string] $Apk = "",
    # The adb to use; by default the recorder's own.
    [string] $Adb = "",
    # The phone's serial number, when more than one is connected.
    [string] $Serial = "",
    # Only these screens, by their ids in expected.json; by default every tour screen.
    [string[]] $Screens = @(),
    # Skips installing the app, when it is already installed.
    [switch] $NoInstall
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$expectedPath = Join-Path (Join-Path (Join-Path $repositoryRoot "android") "fixtures") "expected.json"
if (-not $Apk) {
    $Apk = Join-Path $repositoryRoot "artifacts\fixture-app\output\a11yrecorder-fixtures.apk"
}
if (-not $Adb) {
    $localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $Adb = Join-Path $localData "Windows A11y Recorder\android\platform-tools\adb.exe"
}
if (-not (Test-Path -LiteralPath $Adb)) {
    throw "The recorder's adb was not found at $Adb. Open the recorder's Android setup to download it, or pass -Adb."
}

$expected = Get-Content -LiteralPath $expectedPath -Raw -Encoding UTF8 | ConvertFrom-Json
$package = $expected.package
$tour = @($expected.screens | Where-Object { $_.how -eq "tour" })
if ($Screens.Count -gt 0) {
    $unknown = @($Screens | Where-Object { $tour.id -notcontains $_ })
    if ($unknown.Count -gt 0) {
        throw "Not tour screens in expected.json: $($unknown -join ', ')."
    }
    $tour = @($tour | Where-Object { $Screens -contains $_.id })
}

$deviceArguments = if ($Serial) { @("-s", $Serial) } else { @() }

function Invoke-Adb {
    param([string[]] $Arguments)
    $output = & $Adb @deviceArguments @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "adb $($Arguments -join ' ') failed with exit code $LASTEXITCODE. $output"
    }
    return $output
}

$devices = @(& $Adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "^\S+\s+device$" })
if ($devices.Count -eq 0) {
    throw "No phone is connected over adb. Connect it as for a recording."
}
if ($devices.Count -gt 1 -and -not $Serial) {
    throw "More than one phone is connected; pass -Serial with one of: $(($devices | ForEach-Object { ($_ -split '\s+')[0] }) -join ', ')."
}

if (-not $NoInstall) {
    if (-not (Test-Path -LiteralPath $Apk)) {
        throw "The fixture app was not found at $Apk. Build it with scripts\Build-FixtureApp.ps1."
    }
    Write-Host "Installing the fixture app."
    Invoke-Adb @("install", "-r", $Apk) | Out-Null
}

$total = ($tour | Measure-Object -Property seconds -Sum).Sum
Write-Host ""
Write-Host "The tour opens $($tour.Count) screens, about $total seconds in all."
Read-Host "Press Record in the recorder, then press Enter here" | Out-Null

# FLAG_ACTIVITY_NEW_TASK | FLAG_ACTIVITY_CLEAR_TASK: each screen is a new
# activity in a new task, so no screen is left behind another.
$flags = "0x10008000"
$number = 0
foreach ($screen in $tour) {
    $number++
    Write-Host ("{0} of {1}: {2} ({3}), {4} seconds" -f $number, $tour.Count, $screen.title, $screen.id, $screen.seconds)
    Invoke-Adb @("shell", "am", "start", "-W", "-f", $flags,
        "-n", "$package/.FixtureActivity", "--es", "screen", $screen.id) | Out-Null
    Start-Sleep -Seconds $screen.seconds
}

Write-Host ""
Write-Host "TOUR FINISHED. Press Stop in the recorder."

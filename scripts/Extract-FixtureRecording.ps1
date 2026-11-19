#Requires -Version 5.1
<#
.SYNOPSIS
    Extracts a fixture from a recording of the fixture app's tour, for the
    rule tests.

.DESCRIPTION
    1. Builds tests\FixtureExtract.
    2. Runs it on the recording: it trims the recording to the fixture app's
       visits, leaving out the sound, the video and the phone's
       notifications; runs the rules on what is left with Windows OCR and no
       model; and writes recording.mcap, manifest.json, the pictures the
       rules read, ocr.json and findings.json to the output folder.
    3. Prints each visit's findings, then three lists headed READ BEFORE
       COMMITTING: the recording's metadata, the packages named, and every
       text kept that is not in the fixture app's source. Read them before
       committing the folder.

    The rule tests in tests\Recorder.Tests\AndroidFixtureRecordingTests.cs
    read every folder under tests\fixtures\android. See
    docs\architecture\android-fixtures.md, "Rule tests".

    Runs on Windows PowerShell 5.1 and on PowerShell 7.
#>

[CmdletBinding()]
param(
    # The recording's folder, or its name in the recorder's recordings
    # folder; by default the newest recording there.
    [string] $Session = "",
    # The fixture folder to write.
    [string] $Output = "",
    # Deletes the fixture folder first, when it is there already.
    [switch] $Replace,
    [string] $SessionsRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Windows A11y Recorder'),
    [string] $DotnetPath = (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe')
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $Output) {
    $Output = Join-Path $repositoryRoot "tests\fixtures\android\tour"
}
if (-not (Test-Path -LiteralPath $DotnetPath)) {
    $DotnetPath = "dotnet"
}

if (-not $Session) {
    $newest = Get-ChildItem -LiteralPath $SessionsRoot -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "recording.mcap") } |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if (-not $newest) {
        throw "No recording was found in $SessionsRoot. Pass -Session."
    }
    $Session = $newest.FullName
}
elseif (-not (Test-Path -LiteralPath $Session)) {
    $Session = Join-Path $SessionsRoot $Session
}
if (-not (Test-Path -LiteralPath (Join-Path $Session "recording.mcap"))) {
    throw "No recording file was found in $Session."
}

if (Test-Path -LiteralPath $Output) {
    if (-not $Replace) {
        throw "$Output is there already. Pass -Replace to write it again."
    }
    Remove-Item -LiteralPath $Output -Recurse -Force
}

Write-Host "Recording: $Session"
Write-Host "Fixture:   $Output"
Write-Host ""
$project = Join-Path $repositoryRoot "tests\FixtureExtract\FixtureExtract.csproj"
& $DotnetPath build $project -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    throw "tests\FixtureExtract did not build."
}

$tool = Join-Path $repositoryRoot "tests\FixtureExtract\bin\Release\net10.0-windows10.0.19041.0\FixtureExtract.dll"
& $DotnetPath $tool $Session $Output
if ($LASTEXITCODE -ne 0) {
    throw "The fixture was not extracted."
}

#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the fixture app, a11yrecorder-fixtures.apk, whose screens hold
    known faults and near misses for testing the recorder's Android checks.

.DESCRIPTION
    Runs Build-CompanionApp.ps1 -App fixtures, so the fixture app is built
    with the companion app's pinned JDK, Build-Tools and SDK platform. The
    downloads are shared with the installer's build when it has made them,
    in artifacts\installer\downloads, so they are not fetched twice.

    The app is written to artifacts\fixture-app\output and signed with its
    own key, kept in %LOCALAPPDATA%\Windows A11y Recorder Build\fixtures-signing.

    See docs\architecture\android-fixtures.md.
#>

[CmdletBinding()]
param(
    [string] $WorkFolder = "",
    [string] $DownloadFolder = "",
    [string] $SigningFolder = "",
    [switch] $AcceptAndroidSdkLicense
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $DownloadFolder) {
    $installerDownloads = Join-Path (Join-Path (Join-Path $repositoryRoot "artifacts") "installer") "downloads"
    if (Test-Path -LiteralPath $installerDownloads) {
        $DownloadFolder = $installerDownloads
    }
}

$arguments = @{ App = "fixtures"; AcceptAndroidSdkLicense = $AcceptAndroidSdkLicense }
if ($WorkFolder) { $arguments.WorkFolder = $WorkFolder }
if ($DownloadFolder) { $arguments.DownloadFolder = $DownloadFolder }
if ($SigningFolder) { $arguments.SigningFolder = $SigningFolder }
& (Join-Path $PSScriptRoot "Build-CompanionApp.ps1") @arguments

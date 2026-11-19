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
    3. Checks that TalkBack is on, as the tour is recorded with it on, and
       waits for you to turn it on if it is not.
    4. Lists the other apps that have notifications on the phone, and asks
       you to clear them: demo mode did not hide their icons on the owner's
       phone (measured, 2026-11-19). Enter checks again; GO records with
       them.
    5. Puts the accessibility button in the navigation bar, as it floated
       over tested lines in a different place on each recording.
    6. Puts the phone's status bar in Android's demo mode: a fixed clock,
       a full battery and no notification icons.
    7. Opens the fixture app's index, so the recording starts in the
       fixture app rather than on the phone's home screen.
    8. Asks you to press Record in the recorder, and waits for Enter.
    9. Opens the index again, the screen android\fixtures\expected.json
       marks "start", then each screen it marks "tour", each as a new
       task, and holds each for its "seconds".
    10. Asks you to press Stop, waits for Enter, and then puts the
       accessibility button back and takes the status bar out of demo
       mode. It does so too if the tour stops early.

    Screens marked "person" are left for a person to open from the app's
    index. The screen marked "start" is the index itself. See docs\architecture\android-fixtures.md.

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

function Get-Setting {
    param([string] $Namespace, [string] $Name)
    return ((Invoke-Adb @("shell", "settings", "get", $Namespace, $Name)) -join "").Trim()
}

function Test-TalkBack {
    $services = Get-Setting "secure" "enabled_accessibility_services"
    $exploring = Get-Setting "secure" "touch_exploration_enabled"
    return ($services -match "com\.google\.android\.marvin\.talkback") -and ($exploring -eq "1")
}

function Send-DemoCommand {
    param([string[]] $Extras)
    Invoke-Adb (@("shell", "am", "broadcast", "-a", "com.android.systemui.demo") + $Extras) | Out-Null
}

while (-not (Test-TalkBack)) {
    Write-Host ""
    Write-Host "TalkBack is off on the phone. The tour is recorded with TalkBack on."
    Read-Host "Turn TalkBack on, then press Enter here" | Out-Null
}

# Other apps' notifications: demo mode did not hide their icons on the
# owner's phone (measured, 2026-11-19), so the owner clears them first
# (owner's choice B). The keys "cmd notification list" prints are
# user|package|id|tag|uid; the system's, TalkBack's and the recorder's own
# apps' are left, as a person may not be able to clear them.
$ownPackages = @("android", "com.android.systemui", "com.google.android.marvin.talkback",
    "io.github.bobdodd.a11yrecorder.companion", $package)

function Get-OtherNotifications {
    $keys = & $Adb @deviceArguments shell cmd notification list 2>&1
    if ($LASTEXITCODE -ne 0) {
        return $null
    }
    $found = @($keys | ForEach-Object {
            $parts = ([string]$_).Split("|")
            if ($parts.Count -ge 5) { $parts[1] }
        } | Where-Object { $_ -and ($ownPackages -notcontains $_) } | Sort-Object -Unique)
    return ,$found
}

while ($true) {
    $others = Get-OtherNotifications
    if ($null -eq $others) {
        Write-Host ""
        Write-Host "The phone's notifications could not be listed."
        Read-Host "Clear the phone's notifications, check that the status bar shows no notification icons, then press Enter here" | Out-Null
        break
    }
    if ($others.Count -eq 0) {
        break
    }
    Write-Host ""
    Write-Host "The phone has notifications from: $($others -join ', '). Their icons would be in the recording."
    $answer = Read-Host "Clear them on the phone, then press Enter here to check again, or type GO to record with them"
    if ($answer -eq "GO") {
        break
    }
}

# FLAG_ACTIVITY_NEW_TASK | FLAG_ACTIVITY_CLEAR_TASK: each screen is a new
# activity in a new task, so no screen is left behind another.
$flags = "0x10008000"
$demoAllowed = Get-Setting "global" "sysui_demo_allowed"
# The accessibility button floats over the screen by default, and was in
# a different place on each of the owner's recordings, over lines the
# checks test (measured, 2026-11-19). For the tour it is put in the
# navigation bar, Settings.Secure.ACCESSIBILITY_BUTTON_MODE_NAVIGATION_BAR
# (0; 1 is the floating menu, 2 the gesture), and put back afterwards
# (owner's choice, 2026-11-19).
$buttonMode = Get-Setting "secure" "accessibility_button_mode"
$start = @($expected.screens | Where-Object { $_.how -eq "start" })[0]
try {
    Invoke-Adb @("shell", "settings", "put", "secure", "accessibility_button_mode", "0") | Out-Null
    # Network icons are left as they are: demo mode's network commands are
    # reported to stop the status bar on recent Android versions.
    Invoke-Adb @("shell", "settings", "put", "global", "sysui_demo_allowed", "1") | Out-Null
    Send-DemoCommand @("-e", "command", "enter")
    Send-DemoCommand @("-e", "command", "clock", "-e", "hhmm", "1200")
    Send-DemoCommand @("-e", "command", "battery", "-e", "level", "100", "-e", "plugged", "false")
    Send-DemoCommand @("-e", "command", "notifications", "-e", "visible", "false")
    Invoke-Adb @("shell", "am", "start", "-W", "-f", $flags, "-n", "$package/.FixtureActivity") | Out-Null

    $total = $start.seconds + ($tour | Measure-Object -Property seconds -Sum).Sum
    Write-Host ""
    Write-Host "The phone shows the fixture app's index, with the status bar's clock at 12:00."
    Write-Host "The accessibility button is in the navigation bar, not floating over the screen."
    Write-Host "The tour opens the index again and $($tour.Count) screens, about $total seconds in all."
    Read-Host "Press Record in the recorder, then press Enter here" | Out-Null

    # The index again, held as the other screens are, so that it is
    # listed and has a screenshot of its own after Record: on the fourth
    # recording its only screenshot came just before its first snapshot
    # (measured, 2026-11-19).
    Write-Host ("The index ({0}), {1} seconds" -f $start.title, $start.seconds)
    Invoke-Adb @("shell", "am", "start", "-W", "-f", $flags, "-n", "$package/.FixtureActivity") | Out-Null
    Start-Sleep -Seconds $start.seconds

    $number = 0
    foreach ($screen in $tour) {
        $number++
        Write-Host ("{0} of {1}: {2} ({3}), {4} seconds" -f $number, $tour.Count, $screen.title, $screen.id, $screen.seconds)
        Invoke-Adb @("shell", "am", "start", "-W", "-f", $flags,
            "-n", "$package/.FixtureActivity", "--es", "screen", $screen.id) | Out-Null
        Start-Sleep -Seconds $screen.seconds
    }

    Write-Host ""
    Write-Host "TOUR FINISHED."
    Read-Host "Press Stop in the recorder, then press Enter here" | Out-Null
}
finally {
    if ($buttonMode -eq "null" -or -not $buttonMode) {
        Invoke-Adb @("shell", "settings", "delete", "secure", "accessibility_button_mode") | Out-Null
    }
    else {
        Invoke-Adb @("shell", "settings", "put", "secure", "accessibility_button_mode", $buttonMode) | Out-Null
    }
    Write-Host "The accessibility button is back where it was."
    Send-DemoCommand @("-e", "command", "exit")
    $restore = if ($demoAllowed -eq "1") { "1" } else { "0" }
    Invoke-Adb @("shell", "settings", "put", "global", "sysui_demo_allowed", $restore) | Out-Null
    Write-Host "The status bar is out of demo mode."
}

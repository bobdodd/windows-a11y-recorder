# Tests the recording of the Windows accessibility-related settings: every
# setting at the start and stop of a recording, and one change record for
# each change, with the reading before and after. See
# docs/architecture/accessibility-preferences.md, "Required tests".
#
# Run in an ordinary PowerShell window (Windows PowerShell 5.1) while the
# recorder is recording with desktop frames on, from the recorder package
# folder:
#   powershell -ExecutionPolicy Bypass -File .\scripts\Test-AccessibilityPreferences.ps1
#
# The script changes each setting it can set from code, waits, and puts it
# back as it was, so each gives two change records:
#   menuAnimation, menuFade, comboBoxAnimation, keyboardCues,
#   animationsEnabled (client area animation), caretWidth,
#   focusBorderWidth, messageDuration, stickyKeys    by SystemParametersInfo
#   appsUseLightTheme, transparencyEffects           by the registry and a
#                                                    WM_SETTINGCHANGE
# It then asks you to make the changes it cannot make from code, and to
# put each back: text size, a color filter, a contrast theme, and the
# display scale. While the color filter and the contrast theme are on it
# shows a colored test card, to measure whether the recorded frames hold
# them.
#
# Then it asks you to stop the recording and for the session ID, exports
# the recording's events, and checks the records.
#
# -ResultsPath and -SessionFolder analyse an earlier run again without
# making the changes. -EventsPath uses events already exported.
# -SkipCode leaves out the changes the script makes, and -ManualSteps
# lists the changes you are asked to make, for example:
#   -SkipCode -ManualSteps 'color filter','contrast theme'
# runs only those two.

param(
    [int]$StepSeconds = 3,
    [int]$CardSeconds = 6,
    [string]$OutputRoot = 'C:\Users\Public\Downloads',
    [string]$SessionId,
    [string]$SessionsRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Windows A11y Recorder'),
    [string]$SessionFolder,
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DotnetPath = (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
    [string]$EventsPath,
    [string]$ResultsPath,
    [switch]$SkipManual,
    [switch]$SkipCode,
    [string[]]$ManualSteps = @('text size', 'color filter', 'contrast theme', 'display scale')
)

$ErrorActionPreference = 'Stop'

# powershell -File passes a list as one string, "color filter,contrast
# theme" (run of 2026-10-08), so the names are split on commas here, and
# a name that is not a step stops the script before anything is changed.
$KnownManualSteps = @('text size', 'color filter', 'contrast theme', 'display scale')
$ManualSteps = @($ManualSteps | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($name in $ManualSteps) {
    if ($KnownManualSteps -notcontains $name) {
        throw "'$name' is not a step you can be asked to make. The steps are: $($KnownManualSteps -join ', ')."
    }
}
if (-not $ResultsPath) {
    $planned = @()
    if (-not $SkipCode) { $planned += 'the changes the script makes' }
    if (-not $SkipManual) { $planned += $ManualSteps }
    if ($planned.Count -eq 0) { throw 'Nothing to test: -SkipCode with no manual steps.' }
    Write-Host ("Steps to run: {0}" -f ($planned -join '; '))
}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -Namespace PrefTest -Name Native -MemberDefinition @'
[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
public static extern bool GetInt(uint action, uint param, ref int value, uint winIni);
[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
public static extern bool SetValue(uint action, uint param, IntPtr value, uint winIni);
[StructLayout(LayoutKind.Sequential)]
public struct STICKYKEYS { public uint cbSize; public uint dwFlags; }
[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
public static extern bool StickyKeys(uint action, uint param, ref STICKYKEYS value, uint winIni);
[DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
public static extern IntPtr Broadcast(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
'@

[void][PrefTest.Native]::SetProcessDPIAware()

function Say([string]$text) {
    Write-Host $text
    if ($script:log) { Add-Content -LiteralPath $script:log -Value $text }
}

function Read-Utc([string]$text) {
    return [DateTime]::Parse($text, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}

# A JSON value as compact text, for comparing readings.
function Get-JsonText($value) {
    if ($null -eq $value) { return 'null' }
    if ($value -is [bool]) { if ($value) { return 'true' } else { return 'false' } }
    return (ConvertTo-Json -InputObject $value -Compress -Depth 8)
}

# --- Frame measurement -------------------------------------------------------

# Mean saturation (0 to 1) and mean brightness (0 to 255) of an image on a
# 160 by 90 thumbnail. The test card is strongly colored, so a grayscale
# color filter in the frame shows as a saturation near 0.
function Measure-Image([string]$path) {
    $image = [System.Drawing.Image]::FromFile($path)
    try {
        $t = New-Object System.Drawing.Bitmap(160, 90)
        $g = [System.Drawing.Graphics]::FromImage($t)
        $g.DrawImage($image, 0, 0, 160, 90)
        $g.Dispose()
        $saturation = 0.0; $brightness = 0.0
        for ($y = 0; $y -lt 90; $y++) {
            for ($x = 0; $x -lt 160; $x++) {
                $c = $t.GetPixel($x, $y)
                $saturation += $c.GetSaturation()
                $brightness += ($c.R + $c.G + $c.B) / 3
            }
        }
        $t.Dispose()
        return [pscustomobject]@{
            Saturation = [math]::Round($saturation / 14400, 3)
            Brightness = [math]::Round($brightness / 14400, 1) }
    }
    finally { $image.Dispose() }
}

# --- Analysis ----------------------------------------------------------------

function Read-Events([string]$events) {
    $preferences = New-Object System.Collections.Generic.List[object]
    $frames = New-Object System.Collections.Generic.List[object]
    foreach ($line in [IO.File]::ReadLines($events)) {
        $isPreference = $line.Contains('"system.preferences"')
        $isFrame = $line.Contains('"eventType":"desktop-frame"')
        if (-not $isPreference -and -not $isFrame) { continue }
        $record = $line | ConvertFrom-Json
        $event = if ($null -ne $record.PSObject.Properties['event']) { $record.event } else { $record }
        $observed = $event.observedUtc
        $utc = if ($observed -is [DateTime]) { $observed.ToUniversalTime() } else { Read-Utc ([string]$observed) }
        if ($isPreference -and $event.channel -eq 'system.preferences') {
            $preferences.Add([pscustomobject]@{ Utc = $utc; EventType = $event.eventType; Payload = $event.payload })
        }
        elseif ($isFrame) {
            $frames.Add([pscustomobject]@{ Utc = $utc; Path = [string]$event.payload.path })
        }
    }
    return [pscustomobject]@{ Preferences = $preferences; Frames = $frames }
}

function Get-Reading($payload, [string]$side) {
    $setting = [string]$payload.setting
    return $payload.$side.$setting
}

function Invoke-Analysis([string]$results, [string]$session, [string]$events) {
    $steps = @(Import-Csv -LiteralPath (Join-Path $results 'steps.csv'))
    $read = Read-Events $events
    $records = $read.Preferences
    $changes = @($records | Where-Object { $_.EventType -eq 'windows-preference-changed' })
    $snapshots = @($records | Where-Object { $_.EventType -eq 'windows-preferences' })
    Say ("Settings records: {0}; snapshots: {1} ({2}); changes: {3}" -f $records.Count, $snapshots.Count,
        (($snapshots | ForEach-Object { $_.Payload.reason }) -join ', '), $changes.Count)
    $checks = New-Object System.Collections.Generic.List[object]
    function Add-Check([string]$name, [bool]$passed, [string]$detail) {
        $checks.Add([pscustomobject]@{ check = $name; passed = $passed; detail = $detail })
    }

    $start = @($snapshots | Where-Object { $_.Payload.reason -eq 'start' })
    $stop = @($snapshots | Where-Object { $_.Payload.reason -eq 'stop' })
    Add-Check 'one start snapshot' ($start.Count -eq 1) "$($start.Count) found"
    Add-Check 'one stop snapshot' ($stop.Count -eq 1) "$($stop.Count) found"
    if ($start.Count -ge 1) {
        $settings = $start[0].Payload.settings
        $problems = @($settings.PSObject.Properties | Where-Object { $null -ne $_.Value.problem } |
            ForEach-Object { "$($_.Name): $($_.Value.problem)" })
        # Listed, not failed: a key never created, as ColorFiltering before
        # a filter is first used, is a state of the machine, not a fault.
        Say ("Start readings with a problem: {0}" -f $(if ($problems.Count) { $problems -join '; ' } else { 'none' }))
        $uiEvents = $start[0].Payload.uiSettingsEvents
        $missing = @($uiEvents.PSObject.Properties | Where-Object { -not $_.Value } | ForEach-Object { $_.Name })
        Add-Check 'every UISettings event present' ($missing.Count -eq 0) ($missing -join ', ')
    }
    $caret = @($changes | Where-Object { $_.Payload.setting -eq 'caretBlinkTime' })
    Add-Check 'no caret blink time change' ($caret.Count -eq 0) "$($caret.Count) found"

    # Each step: the change records that start in its window, which ends at
    # the start of the next step.
    $rows = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $steps.Count; $i++) {
        $step = $steps[$i]
        $from = Read-Utc $step.startUtc
        $to = if ($i + 1 -lt $steps.Count) { Read-Utc $steps[$i + 1].startUtc } else { (Read-Utc $step.endUtc).AddSeconds(5) }
        $inStep = @($changes | Where-Object { $_.Utc -ge $from -and $_.Utc -lt $to })
        $mine = @($inStep | Where-Object { $_.Payload.setting -eq $step.setting })
        $others = @($inStep | Where-Object { $_.Payload.setting -ne $step.setting } | ForEach-Object {
                "{0} {1} to {2}" -f $_.Payload.setting, (Get-JsonText (Get-Reading $_.Payload 'previous').value),
                    (Get-JsonText (Get-Reading $_.Payload 'current').value) })
        $found = @($mine | ForEach-Object {
                "{0} to {1} ({2}{3})" -f (Get-JsonText (Get-Reading $_.Payload 'previous').value),
                    (Get-JsonText (Get-Reading $_.Payload 'current').value), $_.Payload.notice.kind,
                    $(if ($_.Payload.notice.source) { ' ' + $_.Payload.notice.source } elseif ($null -ne $_.Payload.notice.uiAction) { ' 0x{0:X}' -f [long]$_.Payload.notice.uiAction } else { '' }) })
        $passed = $null
        if ($step.kind -eq 'code') {
            $passed = $mine.Count -eq 1 -and
                (Get-JsonText (Get-Reading $mine[0].Payload 'previous').value) -eq $step.expectedFrom -and
                (Get-JsonText (Get-Reading $mine[0].Payload 'current').value) -eq $step.expectedTo
        }
        elseif ($step.kind -eq 'card') {
            # Nothing is changed while the first card is shown.
            $passed = $inStep.Count -eq 0
        }
        else {
            $passed = $mine.Count -ge 1
        }
        $rows.Add([pscustomobject]@{
            step = $step.step; setting = $step.setting; kind = $step.kind
            expected = if ($step.kind -eq 'code') { "$($step.expectedFrom) to $($step.expectedTo)" }
                elseif ($step.kind -eq 'card') { 'no change' } else { $step.note }
            records = $mine.Count; found = ($found -join '; '); passed = $passed
            otherChanges = ($others -join '; ') })
    }

    # The card phases: the recorded frames shown while the card was up.
    $cards = New-Object System.Collections.Generic.List[object]
    $frameDir = Join-Path $session 'frames\desktop'
    foreach ($step in $steps | Where-Object { $_.cardStartUtc }) {
        $cardFrom = Read-Utc $step.cardStartUtc
        $cardTo = Read-Utc $step.cardEndUtc
        $inCard = @($read.Frames | Where-Object { $_.Utc -ge $cardFrom.AddSeconds(1) -and $_.Utc -le $cardTo })
        $measured = @($inCard | Select-Object -Last 3 | ForEach-Object {
                $file = Join-Path $frameDir (Split-Path -Leaf $_.Path)
                if (Test-Path -LiteralPath $file) { Measure-Image $file } })
        $cards.Add([pscustomobject]@{
            card = $step.step; frames = $inCard.Count
            frameSaturation = if ($measured.Count) { [math]::Round(($measured | Measure-Object Saturation -Average).Average, 3) } else { $null }
            frameBrightness = if ($measured.Count) { [math]::Round(($measured | Measure-Object Brightness -Average).Average, 1) } else { $null }
            youSaw = $step.youSaw })
    }

    $checks | Export-Csv -LiteralPath (Join-Path $results 'checks.csv') -NoTypeInformation
    $rows | Export-Csv -LiteralPath (Join-Path $results 'summary.csv') -NoTypeInformation
    $cards | Export-Csv -LiteralPath (Join-Path $results 'cards.csv') -NoTypeInformation
    $text = Join-Path $results 'summary.txt'
    $checks | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Tee-Object -FilePath $text | Write-Host
    $rows | Format-Table step, setting, kind, expected, records, found, passed -AutoSize -Wrap |
        Out-String -Width 260 | Tee-Object -FilePath $text -Append | Write-Host
    $rows | Where-Object { $_.otherChanges } | Format-List step, otherChanges |
        Out-String -Width 260 | Tee-Object -FilePath $text -Append | Write-Host
    $cards | Format-Table -AutoSize | Out-String -Width 220 | Tee-Object -FilePath $text -Append | Write-Host
    Say ('A code step passes with exactly one change record of its setting, holding the expected values ' +
        'before and after. A step you made passes with at least one record of its setting. The cards give ' +
        'the mean saturation (0 to 1) and brightness (0 to 255) of the last recorded frames while each card was shown.')
}

function Export-Events([string]$session, [string]$results) {
    $recording = Join-Path $session 'recording.mcap'
    if (-not (Test-Path -LiteralPath $recording)) { throw "Recording not found: $recording" }
    $exportProject = Join-Path $PackageRoot 'tests\RecordingEventExport\RecordingEventExport.csproj'
    if (-not (Test-Path -LiteralPath $exportProject)) { throw "Event export project not found: $exportProject" }
    $exportOutput = Join-Path $results 'export'
    & $DotnetPath build $exportProject --configuration Release --output $exportOutput --nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The recording event export did not build.' }
    $path = Join-Path $results 'events.jsonl'
    & $DotnetPath (Join-Path $exportOutput 'RecordingEventExport.dll') $recording $path | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The recording events could not be exported.' }
    return $path
}

# A session ID or folder; when none is given, the newest recording.
function Resolve-Session([string]$id) {
    $id = ([string]$id).Trim().Trim('"')
    if (-not $id) {
        $newest = Get-ChildItem -LiteralPath $SessionsRoot -Directory |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'recording.mcap') } |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if (-not $newest) { throw "No recording found in $SessionsRoot" }
        Say "No session ID given; using the newest recording, $($newest.Name)"
        return $newest.FullName
    }
    if (Test-Path -LiteralPath $id) { return $id }
    return Join-Path $SessionsRoot $id
}

if ($ResultsPath) {
    $script:log = Join-Path $ResultsPath 'analysis-log.txt'
    if (-not $SessionFolder) {
        if (-not $SessionId) { throw 'Give -SessionFolder or -SessionId with -ResultsPath.' }
        $SessionFolder = Resolve-Session $SessionId
    }
    if (-not $EventsPath) { $EventsPath = Export-Events $SessionFolder $ResultsPath }
    Invoke-Analysis $ResultsPath $SessionFolder $EventsPath
    return
}

# --- The changes -------------------------------------------------------------

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$out = Join-Path $OutputRoot "accessibility-preferences-test-$stamp"
New-Item -ItemType Directory -Path $out | Out-Null
$script:log = Join-Path $out 'log.txt'
$steps = New-Object System.Collections.Generic.List[object]

# SPIF_UPDATEINIFILE | SPIF_SENDCHANGE: the change is kept in the user's
# profile and WM_SETTINGCHANGE is broadcast, as Settings does.
$SpifUpdateAndSend = [uint32]3
$HwndBroadcast = [IntPtr]0xFFFF
$WmSettingChange = [uint32]0x001A
$SmtoAbortIfHung = [uint32]0x0002

function Add-Step([string]$step, [string]$setting, [string]$kind, [string]$from, [string]$to, [string]$note,
    [DateTime]$startUtc, [DateTime]$endUtc) {
    $steps.Add([pscustomobject]@{
        step = $step; setting = $setting; kind = $kind; expectedFrom = $from; expectedTo = $to; note = $note
        startUtc = $startUtc.ToString('o'); endUtc = $endUtc.ToString('o')
        cardStartUtc = $null; cardEndUtc = $null; youSaw = $null })
}

# A setting set through SystemParametersInfo: its GET and SET actions, the
# value it has, and the value to set; the value is passed in pvParam.
function Test-SystemSetting([string]$setting, [uint32]$get, [uint32]$set, [scriptblock]$next, [switch]$Boolean) {
    $was = 0
    if (-not [PrefTest.Native]::GetInt($get, 0, [ref]$was, 0)) {
        Say ("{0}: could not be read (error {1}); skipped" -f $setting, [Runtime.InteropServices.Marshal]::GetLastWin32Error())
        return
    }
    $new = & $next $was
    $text = { param($v) if ($Boolean) { if ($v -ne 0) { 'true' } else { 'false' } } else { [string]$v } }
    foreach ($pass in @(@('change', $was, $new), @('restore', $new, $was))) {
        $startUtc = [DateTime]::UtcNow
        if (-not [PrefTest.Native]::SetValue($set, 0, [IntPtr][int]$pass[2], $SpifUpdateAndSend)) {
            Say ("{0}: could not be set (error {1})" -f $setting, [Runtime.InteropServices.Marshal]::GetLastWin32Error())
        }
        Start-Sleep -Seconds $StepSeconds
        Add-Step "$setting $($pass[0])" $setting 'code' (& $text $pass[1]) (& $text $pass[2]) '' $startUtc ([DateTime]::UtcNow)
        Say ("{0} {1}: {2} to {3}" -f $setting, $pass[0], (& $text $pass[1]), (& $text $pass[2]))
    }
}

function Test-StickyKeys {
    $keys = New-Object PrefTest.Native+STICKYKEYS
    $keys.cbSize = 8
    if (-not [PrefTest.Native]::StickyKeys(0x003A, 8, [ref]$keys, 0)) { Say 'stickyKeys: could not be read; skipped'; return }
    $was = $keys.dwFlags
    $wasOn = ($was -band 1) -ne 0
    foreach ($pass in @(@('change', ($was -bxor 1), $wasOn, -not $wasOn), @('restore', $was, -not $wasOn, $wasOn))) {
        $startUtc = [DateTime]::UtcNow
        $keys.dwFlags = [uint32]$pass[1]
        if (-not [PrefTest.Native]::StickyKeys(0x003B, 8, [ref]$keys, $SpifUpdateAndSend)) { Say 'stickyKeys: could not be set' }
        Start-Sleep -Seconds $StepSeconds
        $from = if ($pass[2]) { 'true' } else { 'false' }
        $to = if ($pass[3]) { 'true' } else { 'false' }
        Add-Step "stickyKeys $($pass[0])" 'stickyKeys' 'code' $from $to '' $startUtc ([DateTime]::UtcNow)
        Say "stickyKeys $($pass[0]): $from to $to"
    }
}

# A setting held in HKCU\...\Themes\Personalize, changed with the
# WM_SETTINGCHANGE "ImmersiveColorSet" that Settings broadcasts.
function Test-PersonalizeSetting([string]$setting, [string]$valueName) {
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
    $item = Get-ItemProperty -LiteralPath $key -Name $valueName -ErrorAction SilentlyContinue
    if ($null -eq $item) { Say "${setting}: $key\$valueName does not exist; skipped"; return }
    $was = [int]$item.$valueName
    $new = if ($was -ne 0) { 0 } else { 1 }
    foreach ($pass in @(@('change', $was, $new), @('restore', $new, $was))) {
        $startUtc = [DateTime]::UtcNow
        Set-ItemProperty -LiteralPath $key -Name $valueName -Value $pass[2] -Type DWord
        $result = [IntPtr]::Zero
        [void][PrefTest.Native]::Broadcast($HwndBroadcast, $WmSettingChange, [IntPtr]::Zero, 'ImmersiveColorSet', $SmtoAbortIfHung, 2000, [ref]$result)
        Start-Sleep -Seconds $StepSeconds
        $from = if ($pass[1] -ne 0) { 'true' } else { 'false' }
        $to = if ($pass[2] -ne 0) { 'true' } else { 'false' }
        Add-Step "$setting $($pass[0])" $setting 'code' $from $to '' $startUtc ([DateTime]::UtcNow)
        Say "$setting $($pass[0]): $from to $to"
    }
}

# A colored test card, full screen, for the color filter and contrast
# theme phases.
function Show-Card([string]$label) {
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $form = New-Object System.Windows.Forms.Form
    $form.FormBorderStyle = 'None'
    $form.StartPosition = 'Manual'
    $form.Bounds = $screen
    $form.TopMost = $true
    $form.Add_Paint({
        param($s, $e)
        $colors = @('Red', 'Lime', 'Blue', 'Yellow', 'Magenta', 'Cyan', 'OrangeRed', 'DeepSkyBlue')
        $w = [int]($screen.Width / $colors.Count)
        for ($i = 0; $i -lt $colors.Count; $i++) {
            $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromName($colors[$i]))
            $e.Graphics.FillRectangle($brush, $i * $w, 0, $w + 1, $screen.Height)
            $brush.Dispose()
        }
        $font = New-Object System.Drawing.Font('Segoe UI', 28, [System.Drawing.FontStyle]::Bold)
        $e.Graphics.FillRectangle([System.Drawing.Brushes]::White, 0, 0, 640, 60)
        $e.Graphics.DrawString("Card: $label", $font, [System.Drawing.Brushes]::Black, 8, 6)
    })
    $form.Show()
    $startUtc = [DateTime]::UtcNow
    $end = $startUtc.AddSeconds($CardSeconds)
    while ([DateTime]::UtcNow -lt $end) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 100 }
    $form.Close()
    [System.Windows.Forms.Application]::DoEvents()
    return @($startUtc, [DateTime]::UtcNow)
}

# A change you make: the step runs from when you are asked to make it
# until you are asked to put it back.
function Test-ManualSetting([string]$step, [string]$setting, [string]$change, [string]$restore, [switch]$Card) {
    Say ''
    $startUtc = [DateTime]::UtcNow
    [void](Read-Host "$change Then press Enter here")
    # PowerShell names are not case sensitive: the card's times are not
    # held in $card, which is the -Card switch (run of 2026-10-08).
    $cardTimes = $null
    if ($Card) {
        Say "A colored card is shown for $CardSeconds seconds."
        $cardTimes = Show-Card $step
    }
    Add-Step "$step on" $setting 'you' '' '' $change $startUtc ([DateTime]::UtcNow)
    if ($Card) {
        $steps[$steps.Count - 1].cardStartUtc = $cardTimes[0].ToString('o')
        $steps[$steps.Count - 1].cardEndUtc = $cardTimes[1].ToString('o')
        $steps[$steps.Count - 1].youSaw = Read-Host "Did the card look changed by the $step? (y/n)"
    }
    $startUtc = [DateTime]::UtcNow
    [void](Read-Host "$restore Then press Enter here")
    Add-Step "$step off" $setting 'you' '' '' $restore $startUtc ([DateTime]::UtcNow)
}

Say "Writing results to $out"
Say 'Do not change any other setting while the script runs.'
$baseline = Show-Card 'no setting changed'
Add-Step 'baseline card' '' 'card' '' '' '' $baseline[0] $baseline[1]
$steps[$steps.Count - 1].cardStartUtc = $baseline[0].ToString('o')
$steps[$steps.Count - 1].cardEndUtc = $baseline[1].ToString('o')

$flip = { param($v) if ($v -ne 0) { 0 } else { 1 } }
if (-not $SkipCode) {
Test-SystemSetting 'menuAnimation' 0x1002 0x1003 $flip -Boolean
Test-SystemSetting 'menuFade' 0x1012 0x1013 $flip -Boolean
Test-SystemSetting 'comboBoxAnimation' 0x1004 0x1005 $flip -Boolean
Test-SystemSetting 'keyboardCues' 0x100A 0x100B $flip -Boolean
Test-SystemSetting 'animationsEnabled' 0x1042 0x1043 $flip -Boolean
Test-SystemSetting 'caretWidth' 0x2006 0x2007 { param($v) $v + 2 }
Test-SystemSetting 'focusBorderWidth' 0x200E 0x200F { param($v) $v + 1 }
Test-SystemSetting 'messageDuration' 0x2016 0x2017 { param($v) $v + 2 }
Test-StickyKeys
Test-PersonalizeSetting 'appsUseLightTheme' 'AppsUseLightTheme'
Test-PersonalizeSetting 'transparencyEffects' 'EnableTransparency'
}
$steps | Export-Csv -LiteralPath (Join-Path $out 'steps.csv') -NoTypeInformation

if (-not $SkipManual) {
    Say ''
    Say 'Now the changes the script cannot make. Open Settings (Win+U opens Ease of Access) before each.'
    if ($ManualSteps -contains 'text size') { Test-ManualSetting 'text size' 'textScaleFactor' 'In Settings, Ease of Access, Display, set "Make text bigger" to 125 percent and select Apply.' 'Set "Make text bigger" back to where it was and select Apply.' }
    if ($ManualSteps -contains 'color filter') { Test-ManualSetting 'color filter' 'colorFilterActive' 'In Settings, Ease of Access, Color filters, turn on color filters with the Grayscale filter.' 'Turn color filters off.' -Card }
    if ($ManualSteps -contains 'contrast theme') { Test-ManualSetting 'contrast theme' 'highContrast' 'Turn on a contrast theme: in Settings, Ease of Access, High contrast, turn on high contrast.' 'Turn high contrast off.' -Card }
    if ($ManualSteps -contains 'display scale') { Test-ManualSetting 'display scale' 'monitors' 'In Settings, System, Display, change "Change the size of text, apps, and other items" to 125 percent.' 'Set it back to where it was.' }
    $steps | Export-Csv -LiteralPath (Join-Path $out 'steps.csv') -NoTypeInformation
}

Say ''
[void](Read-Host 'Stop the recording in the recorder now, then press Enter here')
if (-not $SessionFolder) {
    if (-not $SessionId) { $SessionId = Read-Host 'Type or paste the session ID shown by the recorder, or press Enter for the newest recording' }
    $SessionFolder = Resolve-Session $SessionId
}
Say "Session folder: $SessionFolder"
if (-not $EventsPath) { $EventsPath = Export-Events $SessionFolder $out }
Invoke-Analysis $out $SessionFolder $EventsPath
Say "Done. Results are in $out"

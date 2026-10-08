# Tests the recording of Windows Magnifier's full screen transform with each
# desktop frame, and the participant's view drawn from it at playback. See
# docs/architecture/magnified-view-playback.md, "Required tests".
#
# Run in an ordinary PowerShell window while the recorder is recording with
# desktop frames on, from the recorder package folder:
#   powershell -ExecutionPolicy Bypass -File .\scripts\Test-MagnifiedPlayback.ps1
#
# The script shows a full-screen test card and drives Magnifier through
# these phases, each PhaseSeconds long, without touching your settings for
# good (the level is put back and colors are inverted back):
#   baseline     no Magnifier, cursor at the centre
#   fullscreen   Magnifier full screen view, cursor at the centre
#   pan          cursor held near the bottom-right corner, so the view pans
#   zoomed       two steps of Win+Plus, cursor still near the corner
#   inverted     Ctrl+Alt+I (colors inverted), then inverted back
#   lens         Ctrl+Alt+L
#   docked       Ctrl+Alt+D
#   closed       Win+Esc
# In each phase it samples MagGetFullscreenTransform and
# MagGetFullscreenColorEffect from its own process every 250 ms, and takes
# a GDI screenshot halfway through, which shows the screen as magnified.
#
# It then turns the full screen view on again and asks you to stop the
# recording, to check that the recorder's reading does not change
# Magnifier when the recording stops. Then it asks for the session ID,
# exports the recording's events, and for each phase compares the
# participant's view drawn from the recorded frame nearest the screenshot
# with that screenshot.
#
# Do not touch the mouse or keyboard during the phases.
#
# -ResultsPath and -SessionFolder analyse an earlier run again without
# running the phases. -EventsPath uses events already exported.

param(
    [int]$PhaseSeconds = 8,
    [string]$OutputRoot = 'C:\Users\Public\Downloads',
    [string]$SessionId,
    [string]$SessionsRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Windows A11y Recorder'),
    [string]$SessionFolder,
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DotnetPath = (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
    [string]$EventsPath,
    [string]$ResultsPath,
    [double]$Tolerance = 3.0,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -Namespace MagPlayTest -Name Native -MemberDefinition @'
[DllImport("Magnification.dll")] public static extern bool MagInitialize();
[DllImport("Magnification.dll")] public static extern bool MagUninitialize();
[DllImport("Magnification.dll")] public static extern bool MagGetFullscreenTransform(out float level, out int x, out int y);
[DllImport("Magnification.dll")] public static extern bool MagGetFullscreenColorEffect([Out] float[] effect);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
'@

[void][MagPlayTest.Native]::SetProcessDPIAware()

function Say([string]$text) {
    Write-Host $text
    if ($script:log) { Add-Content -LiteralPath $script:log -Value $text }
}

function Read-Utc([string]$text) {
    return [DateTime]::Parse($text, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}

# --- Image comparison --------------------------------------------------------

# Mean absolute difference of two images on 160 by 90 grey thumbnails, 0 to
# 255, as in the Magnifier capture validation of 2026-10-07: an identical
# image scores 0, and one drawn at twice the size about 7.
function Get-Thumb([System.Drawing.Image]$image) {
    $t = New-Object System.Drawing.Bitmap(160, 90)
    $g = [System.Drawing.Graphics]::FromImage($t)
    $g.InterpolationMode = 'HighQualityBilinear'
    $g.DrawImage($image, 0, 0, 160, 90)
    $g.Dispose()
    $v = New-Object 'double[]' (160 * 90)
    for ($y = 0; $y -lt 90; $y++) {
        for ($x = 0; $x -lt 160; $x++) {
            $c = $t.GetPixel($x, $y)
            $v[$y * 160 + $x] = ($c.R + $c.G + $c.B) / 3
        }
    }
    $t.Dispose()
    return ,$v
}

function Get-Diff($a, $b) {
    $s = 0.0
    for ($i = 0; $i -lt $a.Length; $i++) { $s += [math]::Abs($a[$i] - $b[$i]) }
    return [math]::Round($s / $a.Length, 2)
}

# The part of a frame the participant saw, as the player computes it
# (src/Recorder.Session/MagnifiedView.cs): the offset plus the virtual
# screen's corner divided by the level, less that corner, and the frame's
# size divided by the level. Null when there is no level above 1.
function Get-SeenRegion($payload) {
    $m = $payload.fullscreenMagnification
    if ($null -eq $m -or $null -eq $m.level -or [double]$m.level -le 1.000001) { return $null }
    $level = [double]$m.level
    $vx = [double]$payload.x; $vy = [double]$payload.y
    return [pscustomobject]@{
        X = [double]$m.x + ($vx / $level) - $vx
        Y = [double]$m.y + ($vy / $level) - $vy
        Width = [double]$payload.width / $level
        Height = [double]$payload.height / $level
    }
}

# The participant's view of a frame, drawn at the frame's size, black where
# the part seen extends beyond the frame.
function New-ParticipantView([string]$framePath, $payload) {
    $source = [System.Drawing.Image]::FromFile($framePath)
    try {
        $view = New-Object System.Drawing.Bitmap($source.Width, $source.Height)
        $g = [System.Drawing.Graphics]::FromImage($view)
        $g.Clear([System.Drawing.Color]::Black)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.PixelOffsetMode = 'Half'
        $region = Get-SeenRegion $payload
        $destination = New-Object System.Drawing.Rectangle(0, 0, $source.Width, $source.Height)
        if ($null -eq $region) {
            $g.DrawImage($source, $destination)
        }
        else {
            $scaleX = $source.Width / [double]$payload.width
            $scaleY = $source.Height / [double]$payload.height
            $sourceRect = New-Object System.Drawing.RectangleF(
                [single]($region.X * $scaleX), [single]($region.Y * $scaleY),
                [single]($region.Width * $scaleX), [single]($region.Height * $scaleY))
            $destinationF = New-Object System.Drawing.RectangleF(0, 0, $source.Width, $source.Height)
            $g.DrawImage($source, $destinationF, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)
        }
        $g.Dispose()
        return $view
    }
    finally {
        $source.Dispose()
    }
}

# --- Analysis ----------------------------------------------------------------

function Invoke-Analysis([string]$results, [string]$session, [string]$events) {
    $phases = Import-Csv -LiteralPath (Join-Path $results 'phases.csv')
    $samplesPath = Join-Path $results 'script-samples.csv'
    $samples = if (Test-Path -LiteralPath $samplesPath) { Import-Csv -LiteralPath $samplesPath } else { @() }
    $frameDir = Join-Path $session 'frames\desktop'
    if (-not (Test-Path -LiteralPath $frameDir)) { throw "No frames folder at $frameDir" }

    # Every desktop frame's payload, by its file name, with the UTC time it
    # was captured: the event's observed time, taken as the frame was
    # stored, less the capture's duration.
    $frames = New-Object System.Collections.Generic.List[object]
    $withReading = 0; $withLevel = 0
    $problems = @{}
    foreach ($line in [IO.File]::ReadLines($events)) {
        if (-not $line.Contains('"eventType":"desktop-frame"')) { continue }
        $record = $line | ConvertFrom-Json
        $event = if ($null -ne $record.PSObject.Properties['event']) { $record.event } else { $record }
        $payload = $event.payload
        $file = Join-Path $frameDir (Split-Path -Leaf $payload.path)
        if (-not (Test-Path -LiteralPath $file)) { continue }
        $m = $payload.fullscreenMagnification
        if ($null -ne $payload.PSObject.Properties['fullscreenMagnification'] -and $null -ne $m) {
            $withReading++
            if ($null -ne $m.level) { $withLevel++ }
            elseif ($m.problem) { $problems[[string]$m.problem] = 1 + [int]$problems[[string]$m.problem] }
        }
        $observed = $event.observedUtc
        $observedUtc = if ($observed -is [DateTime]) { $observed.ToUniversalTime() } else { Read-Utc ([string]$observed) }
        $capturedUtc = $observedUtc.AddTicks(-[long]([double]$payload.captureDurationNanoseconds / 100))
        $frames.Add([pscustomobject]@{
            Path = $file; Name = (Split-Path -Leaf $file); Payload = $payload
            CapturedUtc = $capturedUtc })
    }
    Say ("Desktop frames: {0}; with a magnification reading: {1}; with a level: {2}" -f $frames.Count, $withReading, $withLevel)
    foreach ($problem in $problems.Keys) { Say ("  Reading problem in {0} frames: {1}" -f $problems[$problem], $problem) }
    if ($frames.Count -eq 0) { throw 'The recording holds no desktop frames.' }

    $viewDir = Join-Path $results 'participant-view'
    if (-not (Test-Path -LiteralPath $viewDir)) { New-Item -ItemType Directory -Path $viewDir | Out-Null }
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($p in $phases) {
        $start = Read-Utc $p.startUtc
        $end = Read-Utc $p.endUtc
        $inPhase = $frames | Where-Object { $_.CapturedUtc -ge $start -and $_.CapturedUtc -le $end }
        $frameLevels = ($inPhase | ForEach-Object {
                $m = $_.Payload.fullscreenMagnification
                if ($null -ne $m -and $null -ne $m.level) { [math]::Round([double]$m.level, 3) } else { 'none' }
            } | Sort-Object -Unique) -join ' '
        $scriptLevels = ($samples | Where-Object { $_.phase -eq $p.phase -and $_.ok -eq 'True' } |
            ForEach-Object { [math]::Round([double]$_.level, 3) } | Sort-Object -Unique) -join ' '
        $effects = ($samples | Where-Object { $_.phase -eq $p.phase -and $_.effectOk -eq 'True' } |
            ForEach-Object { $_.effectIdentity } | Sort-Object -Unique) -join ' '

        $row = [ordered]@{
            phase = $p.phase; frame = $null; frameMinusShotMs = $null
            level = $null; x = $null; y = $null; problem = $null
            participantVsShot = $null; capturedVsShot = $null; withinTolerance = $null
            frameLevelsInPhase = $frameLevels; scriptLevelsInPhase = $scriptLevels
            colorEffectIdentity = $effects }
        if ($p.gdiShot -and (Test-Path -LiteralPath $p.gdiShot)) {
            $shotUtc = Read-Utc $p.shotUtc
            $nearest = $frames | Sort-Object { [math]::Abs(($_.CapturedUtc - $shotUtc).TotalMilliseconds) } | Select-Object -First 1
            $m = $nearest.Payload.fullscreenMagnification
            $row.frame = $nearest.Name
            $row.frameMinusShotMs = [math]::Round(($nearest.CapturedUtc - $shotUtc).TotalMilliseconds)
            if ($null -ne $m) { $row.level = $m.level; $row.x = $m.x; $row.y = $m.y; $row.problem = $m.problem }

            $shot = [System.Drawing.Image]::FromFile($p.gdiShot)
            $shotThumb = Get-Thumb $shot
            $shot.Dispose()
            $view = New-ParticipantView $nearest.Path $nearest.Payload
            $view.Save((Join-Path $viewDir ("{0}-{1}" -f $p.phase, $nearest.Name)), [System.Drawing.Imaging.ImageFormat]::Png)
            $row.participantVsShot = Get-Diff (Get-Thumb $view) $shotThumb
            $view.Dispose()
            $captured = [System.Drawing.Image]::FromFile($nearest.Path)
            $row.capturedVsShot = Get-Diff (Get-Thumb $captured) $shotThumb
            $captured.Dispose()
            $row.withinTolerance = $row.participantVsShot -le $Tolerance
        }
        $rows.Add([pscustomobject]$row)
    }

    $rows | Export-Csv -LiteralPath (Join-Path $results 'summary.csv') -NoTypeInformation
    $rows | Format-Table phase, frame, frameMinusShotMs, level, x, y, participantVsShot, capturedVsShot, withinTolerance -AutoSize |
        Out-String -Width 220 | Tee-Object -FilePath (Join-Path $results 'summary.txt') | Write-Host
    $rows | Format-Table phase, frameLevelsInPhase, scriptLevelsInPhase, colorEffectIdentity, problem -AutoSize |
        Out-String -Width 220 | Tee-Object -FilePath (Join-Path $results 'summary.txt') -Append | Write-Host
    Say ("participantVsShot: the participant's view drawn from the recorded frame against the screenshot; " +
        "within tolerance at {0} or less. capturedVsShot: the frame as captured against the screenshot." -f $Tolerance)
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

function Resolve-Session([string]$id) {
    $id = $id.Trim().Trim('"')
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

# --- The phases --------------------------------------------------------------

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$out = Join-Path $OutputRoot "magnified-playback-test-$stamp"
New-Item -ItemType Directory -Path $out | Out-Null
$gdiDir = Join-Path $out 'gdi'
New-Item -ItemType Directory -Path $gdiDir | Out-Null
$script:log = Join-Path $out 'log.txt'

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$cx = [int]($screen.X + $screen.Width / 2)
$cy = [int]($screen.Y + $screen.Height / 2)
$cornerX = [int]($screen.X + $screen.Width - 40)
$cornerY = [int]($screen.Y + $screen.Height - 40)

function Send-Keys([byte[]]$keys) {
    if ($DryRun) { return }
    foreach ($k in $keys) { [MagPlayTest.Native]::keybd_event($k, 0, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 60
    [array]::Reverse($keys)
    foreach ($k in $keys) { [MagPlayTest.Native]::keybd_event($k, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 300
}
$VK_CONTROL = [byte]0x11; $VK_MENU = [byte]0x12; $VK_LWIN = [byte]0x5B; $VK_ESCAPE = [byte]0x1B
$VK_F = [byte]0x46; $VK_L = [byte]0x4C; $VK_D = [byte]0x44; $VK_I = [byte]0x49
$VK_PLUS = [byte]0xBB; $VK_MINUS = [byte]0xBD

function Start-FullscreenMagnifier {
    if ($DryRun) { return }
    if (-not (Get-Process Magnify -ErrorAction SilentlyContinue)) {
        Start-Process "$env:WINDIR\System32\Magnify.exe"
        Start-Sleep -Seconds 2
    }
    Send-Keys @($VK_CONTROL, $VK_MENU, $VK_F)
}

function Stop-Magnifier {
    Send-Keys @($VK_LWIN, $VK_ESCAPE)
    Start-Sleep -Seconds 2
    if (-not $DryRun -and (Get-Process Magnify -ErrorAction SilentlyContinue)) {
        Say 'Win+Esc did not close Magnifier; closing it.'
        Stop-Process -Name Magnify -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
}

# A static test card: a labelled grid, so that the part of the screen shown
# is obvious, and the phase name in a corner.
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Bounds = $screen
$form.BackColor = [System.Drawing.Color]::White
$form.Text = 'Magnified playback test'
$script:phaseName = 'starting'
$form.Add_Paint({
    param($s, $e)
    $g = $e.Graphics
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::Black, 2)
    $font = New-Object System.Drawing.Font('Segoe UI', 14)
    $cell = 120
    for ($x = 0; $x -lt $screen.Width; $x += $cell) {
        for ($y = 0; $y -lt $screen.Height; $y += $cell) {
            $g.DrawRectangle($pen, $x, $y, $cell, $cell)
            $label = '{0}{1}' -f [char](65 + [int]($y / $cell)), [int]($x / $cell)
            $g.DrawString($label, $font, [System.Drawing.Brushes]::Black, $x + 8, $y + 8)
        }
    }
    $g.FillEllipse([System.Drawing.Brushes]::Red, $cx - 20, $cy - 20, 40, 40)
    $g.FillEllipse([System.Drawing.Brushes]::Blue, $cornerX - 60, $cornerY - 60, 40, 40)
    $big = New-Object System.Drawing.Font('Segoe UI', 28, [System.Drawing.FontStyle]::Bold)
    $g.FillRectangle([System.Drawing.Brushes]::Yellow, 0, 0, 520, 60)
    $g.DrawString("Phase: $script:phaseName", $big, [System.Drawing.Brushes]::Black, 8, 6)
})
$form.Show()
[void][MagPlayTest.Native]::SetForegroundWindow($form.Handle)
[void][MagPlayTest.Native]::SetCursorPos($cx, $cy)

$magOk = [MagPlayTest.Native]::MagInitialize()
$samples = New-Object System.Collections.Generic.List[object]
$phases = New-Object System.Collections.Generic.List[object]

function Read-Transform([string]$phase) {
    $lvl = 0.0; $ox = 0; $oy = 0
    $ok = $false; $effectOk = $false; $identity = $null
    if ($magOk) {
        $ok = [MagPlayTest.Native]::MagGetFullscreenTransform([ref]$lvl, [ref]$ox, [ref]$oy)
        $effect = New-Object 'float[]' 25
        $effectOk = [MagPlayTest.Native]::MagGetFullscreenColorEffect($effect)
        if ($effectOk) {
            $identity = $true
            for ($i = 0; $i -lt 25; $i++) {
                $expected = if (($i % 6) -eq 0) { 1.0 } else { 0.0 }
                if ([math]::Abs($effect[$i] - $expected) -gt 0.0001) { $identity = $false }
            }
        }
    }
    $sample = [pscustomobject]@{
        utc = [DateTime]::UtcNow.ToString('o'); phase = $phase; ok = $ok; level = $lvl
        xOffset = $ox; yOffset = $oy; effectOk = $effectOk; effectIdentity = $identity }
    $samples.Add($sample)
    return $sample
}

function Save-GdiShot([string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($screen.X, $screen.Y, 0, 0, $bmp.Size)
    $path = Join-Path $gdiDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return $path
}

function Wait-Phase([string]$name, [int]$cursorX, [int]$cursorY) {
    $script:phaseName = $name
    $form.Invalidate()
    [System.Windows.Forms.Application]::DoEvents()
    $start = [DateTime]::UtcNow
    $end = $start.AddSeconds($PhaseSeconds)
    $mid = $start.AddSeconds($PhaseSeconds / 2)
    $shot = $null; $shotUtc = $null; $shotSample = $null
    while ([DateTime]::UtcNow -lt $end) {
        [System.Windows.Forms.Application]::DoEvents()
        [void][MagPlayTest.Native]::SetCursorPos($cursorX, $cursorY)
        $sample = Read-Transform $name
        if ($null -eq $shot -and [DateTime]::UtcNow -ge $mid) {
            $shotUtc = [DateTime]::UtcNow.ToString('o')
            $shot = Save-GdiShot $name
            $shotSample = $sample
        }
        Start-Sleep -Milliseconds 250
    }
    $phases.Add([pscustomobject]@{
        phase = $name; startUtc = $start.ToString('o'); endUtc = [DateTime]::UtcNow.ToString('o')
        magnifierRunning = [bool](Get-Process Magnify -ErrorAction SilentlyContinue)
        gdiShot = $shot; shotUtc = $shotUtc
        shotLevel = $shotSample.level; shotX = $shotSample.xOffset; shotY = $shotSample.yOffset })
    Say ("{0}: level {1}, offset {2}, {3}" -f $name, $shotSample.level, $shotSample.xOffset, $shotSample.yOffset)
}

Say "Writing results to $out"
Say 'Phases start in 3 seconds. Do not touch the mouse or keyboard.'
Start-Sleep -Seconds 3

Wait-Phase 'baseline' $cx $cy
Start-FullscreenMagnifier
Wait-Phase 'fullscreen' $cx $cy
Wait-Phase 'pan' $cornerX $cornerY
Send-Keys @($VK_LWIN, $VK_PLUS)
Send-Keys @($VK_LWIN, $VK_PLUS)
Wait-Phase 'zoomed' $cornerX $cornerY
Send-Keys @($VK_CONTROL, $VK_MENU, $VK_I)
Wait-Phase 'inverted' $cx $cy
Send-Keys @($VK_CONTROL, $VK_MENU, $VK_I)
Send-Keys @($VK_LWIN, $VK_MINUS)
Send-Keys @($VK_LWIN, $VK_MINUS)
Send-Keys @($VK_CONTROL, $VK_MENU, $VK_L)
Wait-Phase 'lens' $cx $cy
Send-Keys @($VK_CONTROL, $VK_MENU, $VK_D)
Wait-Phase 'docked' $cx $cy
Stop-Magnifier
Wait-Phase 'closed' $cx $cy

$form.Close()
[System.Windows.Forms.Application]::DoEvents()
$phases | Export-Csv -LiteralPath (Join-Path $out 'phases.csv') -NoTypeInformation
$samples | Export-Csv -LiteralPath (Join-Path $out 'script-samples.csv') -NoTypeInformation

# Stopping the recording with the full screen view on: the recorder's
# reader closes when the recording stops, which must leave Magnifier as it
# was.
Start-FullscreenMagnifier
Start-Sleep -Seconds 2
$before = Read-Transform 'stop-check-before'
Say ("Before stopping: level {0}, offset {1}, {2}" -f $before.level, $before.xOffset, $before.yOffset)
[void](Read-Host 'Magnifier full screen is on. Stop the recording in the recorder now, then press Enter here')
Start-Sleep -Seconds 2
$after = Read-Transform 'stop-check-after'
Say ("After stopping: level {0}, offset {1}, {2}" -f $after.level, $after.xOffset, $after.yOffset)
$stillMagnified = Read-Host 'Is the screen still magnified as it was before you stopped the recording? (y/n)'
Say "Still magnified after stopping, as you saw it: $stillMagnified"
Stop-Magnifier
if ($magOk) { [void][MagPlayTest.Native]::MagUninitialize() }
$samples | Export-Csv -LiteralPath (Join-Path $out 'script-samples.csv') -NoTypeInformation

$seen = [ordered]@{}
foreach ($p in 'fullscreen', 'pan', 'zoomed', 'inverted', 'lens', 'docked') {
    $seen[$p] = Read-Host "Did the '$p' phase look as its name says? (y/n)"
}
$seen['stillMagnifiedAfterStop'] = $stillMagnified
$seen | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'what-you-saw.json')

if ($DryRun -and -not $SessionId -and -not $SessionFolder) {
    Say "Dry run finished. Results: $out"
    return
}

if (-not $SessionFolder) {
    if (-not $SessionId) { $SessionId = Read-Host 'Type or paste the session ID shown by the recorder' }
    $SessionFolder = Resolve-Session $SessionId
}
Say "Session folder: $SessionFolder"
if (-not $EventsPath) { $EventsPath = Export-Events $SessionFolder $out }
Invoke-Analysis $out $SessionFolder $EventsPath
Say "Done. Results are in $out"

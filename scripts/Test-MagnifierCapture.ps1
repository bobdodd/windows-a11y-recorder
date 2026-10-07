# Tests whether the recorder's screen capture shows Windows Magnifier's
# magnified view or the unmagnified screen.
#
# Run in an ordinary PowerShell window while the recorder is recording:
#   cd C:\Users\Public\Downloads
#   powershell -ExecutionPolicy Bypass -File .\Test-MagnifierCapture.ps1
#
# The script shows a full-screen test card, then drives Magnifier through
# these phases, each PhaseSeconds long:
#   baseline (no Magnifier), full screen, lens, docked, closed.
# It logs each phase's start and end, samples MagGetFullscreenTransform
# from its own process, and takes its own GDI screenshot in each phase.
# At the end it asks what you saw, then asks for the session folder shown
# by the recorder, picks the recorded frame of each phase, and compares
# each with the baseline. Results go to a new folder in Downloads.
#
# Do not touch the mouse or keyboard during the phases.

param(
    [int]$PhaseSeconds = 8,
    [string]$OutputRoot = 'C:\Users\Public\Downloads',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -Namespace MagTest -Name Native -MemberDefinition @'
[DllImport("Magnification.dll")] public static extern bool MagInitialize();
[DllImport("Magnification.dll")] public static extern bool MagUninitialize();
[DllImport("Magnification.dll")] public static extern bool MagGetFullscreenTransform(out float level, out int x, out int y);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
'@

[void][MagTest.Native]::SetProcessDPIAware()

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$out = Join-Path $OutputRoot "magnifier-test-$stamp"
New-Item -ItemType Directory -Path $out | Out-Null
$gdiDir = Join-Path $out 'gdi'
New-Item -ItemType Directory -Path $gdiDir | Out-Null

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$cx = [int]($screen.X + $screen.Width / 2)
$cy = [int]($screen.Y + $screen.Height / 2)

function Send-Keys([byte[]]$keys) {
    if ($DryRun) { return }
    foreach ($k in $keys) { [MagTest.Native]::keybd_event($k, 0, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 60
    [array]::Reverse($keys)
    foreach ($k in $keys) { [MagTest.Native]::keybd_event($k, 0, 2, [UIntPtr]::Zero) }
}
$VK_CONTROL = [byte]0x11; $VK_MENU = [byte]0x12; $VK_LWIN = [byte]0x5B
$VK_ESCAPE = [byte]0x1B; $VK_F = [byte]0x46; $VK_L = [byte]0x4C; $VK_D = [byte]0x44

# A static test card: a labelled grid, so that magnification is obvious,
# and the phase name in a corner, so that each recorded frame shows its phase.
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Bounds = $screen
$form.BackColor = [System.Drawing.Color]::White
$form.Text = 'Magnifier capture test'
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
    $big = New-Object System.Drawing.Font('Segoe UI', 28, [System.Drawing.FontStyle]::Bold)
    $g.FillRectangle([System.Drawing.Brushes]::Yellow, 0, 0, 520, 60)
    $g.DrawString("Phase: $script:phaseName", $big, [System.Drawing.Brushes]::Black, 8, 6)
})
$form.Show()
[void][MagTest.Native]::SetForegroundWindow($form.Handle)
[void][MagTest.Native]::SetCursorPos($cx, $cy)

$magOk = [MagTest.Native]::MagInitialize()
$samples = New-Object System.Collections.Generic.List[object]
$phases = New-Object System.Collections.Generic.List[object]

function Save-GdiShot([string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($screen.X, $screen.Y, 0, 0, $bmp.Size)
    $path = Join-Path $gdiDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return $path
}

function Wait-Phase([string]$name) {
    $script:phaseName = $name
    $form.Invalidate()
    [System.Windows.Forms.Application]::DoEvents()
    $start = [DateTime]::UtcNow
    $end = $start.AddSeconds($PhaseSeconds)
    $mid = $start.AddSeconds($PhaseSeconds / 2)
    $shot = $null
    while ([DateTime]::UtcNow -lt $end) {
        [System.Windows.Forms.Application]::DoEvents()
        [void][MagTest.Native]::SetCursorPos($cx, $cy)
        if ($magOk) {
            $lvl = 0.0; $ox = 0; $oy = 0
            $ok = [MagTest.Native]::MagGetFullscreenTransform([ref]$lvl, [ref]$ox, [ref]$oy)
            $samples.Add([pscustomobject]@{
                utc = [DateTime]::UtcNow.ToString('o'); phase = $name
                ok = $ok; level = $lvl; xOffset = $ox; yOffset = $oy })
        }
        if ($null -eq $shot -and [DateTime]::UtcNow -ge $mid) { $shot = Save-GdiShot $name }
        Start-Sleep -Milliseconds 250
    }
    $phases.Add([pscustomobject]@{
        phase = $name; startUtc = $start.ToString('o'); endUtc = [DateTime]::UtcNow.ToString('o')
        magnifierRunning = [bool](Get-Process Magnify -ErrorAction SilentlyContinue); gdiShot = $shot })
}

Write-Host "Writing results to $out"
Write-Host 'Phases start in 3 seconds. Do not touch the mouse or keyboard.'
Start-Sleep -Seconds 3

Wait-Phase 'baseline'
if (-not $DryRun) { Start-Process "$env:WINDIR\System32\Magnify.exe" }
Start-Sleep -Seconds 2
Send-Keys @($VK_CONTROL, $VK_MENU, $VK_F)
Wait-Phase 'fullscreen'
Send-Keys @($VK_CONTROL, $VK_MENU, $VK_L)
Wait-Phase 'lens'
Send-Keys @($VK_CONTROL, $VK_MENU, $VK_D)
Wait-Phase 'docked'
Send-Keys @($VK_LWIN, $VK_ESCAPE)
Start-Sleep -Seconds 2
if (-not $DryRun -and (Get-Process Magnify -ErrorAction SilentlyContinue)) {
    Write-Host 'Win+Esc did not close Magnifier; closing it.'
    Stop-Process -Name Magnify -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}
Wait-Phase 'closed'

if ($magOk) { [void][MagTest.Native]::MagUninitialize() }
$form.Close()

$phases | Export-Csv (Join-Path $out 'phases.csv') -NoTypeInformation
$samples | Export-Csv (Join-Path $out 'fullscreen-transform-samples.csv') -NoTypeInformation
Write-Host 'Phases finished. You may stop the recording now.'

$seen = [ordered]@{}
foreach ($p in 'fullscreen', 'lens', 'docked') {
    $seen[$p] = Read-Host "Did you see Magnifier's $p view during the '$p' phase? (y/n)"
}
$seen | ConvertTo-Json | Set-Content (Join-Path $out 'what-you-saw.json')

# Mean absolute difference of two images, on 160 by 90 grey thumbnails, 0 to 255.
function Get-Thumb([string]$path) {
    $src = [System.Drawing.Image]::FromFile($path)
    $t = New-Object System.Drawing.Bitmap(160, 90)
    $g = [System.Drawing.Graphics]::FromImage($t)
    $g.InterpolationMode = 'HighQualityBilinear'
    $g.DrawImage($src, 0, 0, 160, 90)
    $g.Dispose(); $src.Dispose()
    $v = New-Object 'double[]' (160 * 90)
    for ($y = 0; $y -lt 90; $y++) { for ($x = 0; $x -lt 160; $x++) {
        $c = $t.GetPixel($x, $y); $v[$y * 160 + $x] = ($c.R + $c.G + $c.B) / 3 } }
    $t.Dispose()
    return ,$v
}
function Get-Diff($a, $b) {
    $s = 0.0; for ($i = 0; $i -lt $a.Length; $i++) { $s += [math]::Abs($a[$i] - $b[$i]) }
    return [math]::Round($s / $a.Length, 2)
}

$session = Read-Host 'Stop the recording, then paste the session folder or session ID shown by the recorder (or press Enter to skip)'
$rows = @()
$baseRec = $null; $baseGdi = $null
if ($phases[0].gdiShot) { $baseGdi = Get-Thumb $phases[0].gdiShot }
$session = $session.Trim().Trim('"')
if ($session -and -not (Test-Path $session)) {
    # A session ID rather than a folder: look in the default output folder.
    $session = Join-Path ([Environment]::GetFolderPath('MyDocuments')) "Windows A11y Recorder\$session"
}
$frameDir = if ($session) { Join-Path $session 'frames\desktop' } else { $null }
$frames = @()
if ($frameDir -and (Test-Path $frameDir)) {
    $frames = Get-ChildItem $frameDir -Filter *.png | Sort-Object LastWriteTimeUtc
    New-Item -ItemType Directory -Path (Join-Path $out 'recorded') | Out-Null
} elseif ($session) { Write-Host "No frames folder at $frameDir" }

foreach ($p in $phases) {
    $start = [DateTime]::Parse($p.startUtc).ToUniversalTime()
    $end = [DateTime]::Parse($p.endUtc).ToUniversalTime()
    # The latest frame written in the phase after its first second, or the
    # latest before the phase ends.
    $pick = $frames | Where-Object { $_.LastWriteTimeUtc -ge $start.AddSeconds(1) -and $_.LastWriteTimeUtc -le $end } | Select-Object -Last 1
    if (-not $pick) { $pick = $frames | Where-Object { $_.LastWriteTimeUtc -le $end } | Select-Object -Last 1 }
    $recDiff = $null; $recName = $null
    if ($pick) {
        $recName = $pick.Name
        $copy = Join-Path $out ("recorded\{0}-{1}" -f $p.phase, $pick.Name)
        Copy-Item $pick.FullName $copy
        $thumb = Get-Thumb $pick.FullName
        if ($p.phase -eq 'baseline') { $baseRec = $thumb }
        if ($baseRec) { $recDiff = Get-Diff $thumb $baseRec }
    }
    $gdiDiff = $null
    if ($p.gdiShot -and $baseGdi) { $gdiDiff = Get-Diff (Get-Thumb $p.gdiShot) $baseGdi }
    $lv = $samples | Where-Object { $_.phase -eq $p.phase -and $_.ok } | ForEach-Object { $_.level } | Sort-Object -Unique
    $rows += [pscustomobject]@{
        phase = $p.phase; recordedFrame = $recName; recordedDiffFromBaseline = $recDiff
        gdiDiffFromBaseline = $gdiDiff; fullscreenLevelsRead = ($lv -join ' ')
        magnifierRunning = $p.magnifierRunning }
}
$rows | Export-Csv (Join-Path $out 'summary.csv') -NoTypeInformation
$rows | Format-Table -AutoSize | Out-String -Width 200 | Tee-Object (Join-Path $out 'summary.txt')
Write-Host "Done. Results are in $out"
Write-Host 'A difference near 0 means the image matches the unmagnified baseline apart from the phase label.'

# Checks, before any Android recording is built, what an Android phone
# connected by USB gives the recorder. See
# docs/validation/android-feasibility-plan.md for the tests and what to
# see on the screen, and docs/architecture/android-recording.md for why.
#
# Run from an ordinary PowerShell window, not as administrator:
#   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
#   & "C:\Users\User\windows-a11y-recorder\scripts\Test-AndroidFeasibility.ps1" -Check Connect
#
# Checks:
#   Connect     downloads scrcpy 5.0.1 (with its adb), checks the phone is
#               connected and authorized, and prints the phone's details.
#   Sound       records 20 seconds of the phone's sound with scrcpy, from
#               -Source output (default) or -Source playback (with
#               --audio-dup), and prints its levels as a chart.
#   SpeechText  mirrors and records 20 seconds of the phone's screen to an
#               MP4, then opens the MP4.
#   Log         clears the phone's log, waits while you move TalkBack, and
#               prints TalkBack's log lines that contain a word you type.
#   Touch       finds the touch screen and prints each touch in 12 seconds,
#               with its time and position in screen pixels.
#
# Everything is written under artifacts\android-checks in the repository.
# Nothing is installed on the phone and no setting is changed on it.

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Connect', 'Sound', 'SpeechText', 'Log', 'Touch')]
    [string]$Check,
    [ValidateSet('output', 'playback')]
    [string]$Source = 'output'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ScrcpyVersion = '5.0.1'
$ScrcpyZipName = "scrcpy-win64-v$ScrcpyVersion.zip"
$ScrcpyZipUrl = "https://github.com/Genymobile/scrcpy/releases/download/v$ScrcpyVersion/$ScrcpyZipName"
# From the release's SHA256SUMS.txt and doc/windows.md.
$ScrcpyZipSha256 = 'B12A2C4EE8BE317422451FC7DCF8EE20A71B5EA7EF9AD73DDD825A25316227A5'
$TalkBackPackage = 'com.google.android.marvin.talkback'

$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repo 'artifacts\android-checks'
$toolsDir = Join-Path $root 'tools'
$scrcpyDir = Join-Path $toolsDir "scrcpy-win64-v$ScrcpyVersion"
$scrcpy = Join-Path $scrcpyDir 'scrcpy.exe'
$adb = Join-Path $scrcpyDir 'adb.exe'
$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Force -Path $root | Out-Null

function Say([string]$text) { Write-Host $text }
function Fail([string]$text) { Write-Host ''; Write-Host "FAILED: $text"; exit 1 }

# adb writes progress to standard error. Windows PowerShell 5.1 turns that
# into errors under 'Stop', so adb runs under 'Continue' and its output is
# returned as plain lines.
function Invoke-Adb {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $adb @args 2>&1 | ForEach-Object { "$_" }
        return $lines
    } finally {
        $ErrorActionPreference = $saved
    }
}

function Get-Prop([string]$name) {
    $value = (Invoke-Adb shell getprop $name) -join ''
    return $value.Trim()
}

function Install-Scrcpy {
    if (Test-Path $scrcpy) { return }
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    $zip = Join-Path $toolsDir $ScrcpyZipName
    if (-not (Test-Path $zip)) {
        Say "Downloading $ScrcpyZipName from GitHub..."
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -UseBasicParsing -Uri $ScrcpyZipUrl -OutFile $zip
    }
    $hash = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash
    if ($hash -ne $ScrcpyZipSha256) {
        Remove-Item $zip -Force
        Fail "the download's SHA-256 is $hash, not the published $ScrcpyZipSha256. It was deleted; run the check again."
    }
    Expand-Archive -Path $zip -DestinationPath $toolsDir -Force
    if (-not (Test-Path $scrcpy)) { Fail "scrcpy.exe is not in $scrcpyDir after extracting." }
    Say "scrcpy $ScrcpyVersion is in $scrcpyDir"
}

# Exactly one phone, connected and authorized.
function Assert-Phone {
    [void](Invoke-Adb start-server)
    $lines = @(Invoke-Adb devices -l | Where-Object { $_ -match '^\S+\s+(device|unauthorized|offline)\b' })
    if ($lines.Count -eq 0) {
        Fail ('adb sees no phone. Check that the cable carries data, that USB debugging is on ' +
              '(Settings, System, Developer options), and that the phone is unlocked. ' +
              'If it still is not seen, install the Google USB Driver: ' +
              'https://developer.android.com/studio/run/win-usb')
    }
    if ($lines.Count -gt 1) { Fail "adb sees more than one device. Disconnect all but the Pixel:`n$($lines -join "`n")" }
    if ($lines[0] -match '\bunauthorized\b') {
        Fail ('the phone has not allowed this PC yet. On the phone, in "Allow USB debugging?", ' +
              'tick "Always allow from this computer" and choose Allow, then run the check again.')
    }
    if ($lines[0] -match '\boffline\b') { Fail 'the phone is offline. Unplug it, plug it in again and run the check again.' }
}

function Get-TalkBackState {
    $services = ((Invoke-Adb shell settings get secure enabled_accessibility_services) -join '').Trim()
    return [pscustomobject]@{
        On       = ($services -match [regex]::Escape($TalkBackPackage))
        Services = $services
    }
}

function Assert-TalkBackOn {
    if (-not (Get-TalkBackState).On) { Fail 'TalkBack is not on. Turn it on (Settings, Accessibility, TalkBack) and run the check again.' }
}

# Peak level of a 16-bit PCM WAV file, per bin of BinSeconds.
Add-Type -TypeDefinition @'
using System;
using System.IO;
public sealed class WavPeaks {
    public int Rate; public int Channels; public int Bits; public double Seconds;
    public double[] Peaks;
    public static WavPeaks Read(string path, double binSeconds) {
        byte[] b = File.ReadAllBytes(path);
        if (b.Length < 12 || b[0] != 'R' || b[1] != 'I' || b[8] != 'W') throw new InvalidDataException("not a WAV file");
        var r = new WavPeaks();
        int pos = 12, dataStart = -1, dataLength = 0;
        while (pos + 8 <= b.Length) {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int size = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ") {
                r.Channels = BitConverter.ToInt16(b, pos + 10);
                r.Rate = BitConverter.ToInt32(b, pos + 12);
                r.Bits = BitConverter.ToInt16(b, pos + 22);
            } else if (id == "data") {
                dataStart = pos + 8;
                // A file not closed cleanly can give 0 or -1 here.
                dataLength = (size <= 0 || dataStart + size > b.Length) ? b.Length - dataStart : size;
                break;
            }
            pos += 8 + size + (size & 1);
        }
        if (dataStart < 0) throw new InvalidDataException("no data chunk");
        if (r.Bits != 16) throw new InvalidDataException("expected 16-bit samples, found " + r.Bits);
        int frameBytes = 2 * r.Channels;
        long frames = dataLength / frameBytes;
        r.Seconds = (double)frames / r.Rate;
        int perBin = Math.Max(1, (int)(r.Rate * binSeconds));
        int bins = (int)((frames + perBin - 1) / perBin);
        r.Peaks = new double[bins];
        for (long f = 0; f < frames; f++) {
            int bin = (int)(f / perBin);
            for (int c = 0; c < r.Channels; c++) {
                int v = Math.Abs((int)BitConverter.ToInt16(b, dataStart + (int)(f * frameBytes) + 2 * c));
                double p = v / 32768.0;
                if (p > r.Peaks[bin]) r.Peaks[bin] = p;
            }
        }
        return r;
    }
}
'@

function Start-Scrcpy([string[]]$arguments, [string]$name) {
    $env:ADB = $adb
    $outLog = Join-Path $root "$name-$stamp.scrcpy-out.txt"
    $errLog = Join-Path $root "$name-$stamp.scrcpy-err.txt"
    $p = Start-Process -FilePath $scrcpy -ArgumentList $arguments -WorkingDirectory $scrcpyDir `
        -NoNewWindow -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
    # Reading the handle now keeps the exit code readable after the process ends.
    [void]$p.Handle
    return [pscustomobject]@{ Process = $p; Out = $outLog; Err = $errLog }
}

function Show-ScrcpyLogs($run) {
    foreach ($f in @($run.Out, $run.Err)) {
        if ((Test-Path $f) -and (Get-Item $f).Length -gt 0) { Get-Content $f | ForEach-Object { Say "  scrcpy: $_" } }
    }
}

# Prints prompts at fixed times while scrcpy runs. Each prompt is
# @(seconds, text).
function Wait-WithPrompts($run, $prompts, [int]$limitSeconds) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $next = 0
    while (-not $run.Process.HasExited) {
        $t = $clock.Elapsed.TotalSeconds
        while ($next -lt $prompts.Count -and $t -ge $prompts[$next][0]) {
            Say ("[{0,4:N0} s] {1}" -f $prompts[$next][0], $prompts[$next][1])
            $next++
        }
        if ($t -gt $limitSeconds + 30) { $run.Process.Kill(); Fail 'scrcpy did not stop on its own.' }
        Start-Sleep -Milliseconds 200
    }
    $run.Process.WaitForExit()
}

Install-Scrcpy
Assert-Phone

switch ($Check) {

    'Connect' {
        $tb = Get-TalkBackState
        $tbVersion = (Invoke-Adb shell dumpsys package $TalkBackPackage | Where-Object { $_ -match 'versionName=' } | Select-Object -First 1)
        if ($tbVersion) { $tbVersion = ($tbVersion -replace '.*versionName=', '').Trim() } else { $tbVersion = 'not installed' }
        $size = ((Invoke-Adb shell wm size) | Select-Object -First 1)
        $explore = ((Invoke-Adb shell settings get secure touch_exploration_enabled) -join '').Trim()
        Say ''
        Say "Model:            $(Get-Prop ro.product.model)"
        Say "Android:          $(Get-Prop ro.build.version.release) (API level $(Get-Prop ro.build.version.sdk))"
        Say "Build:            $(Get-Prop ro.build.id)"
        Say "Security patch:   $(Get-Prop ro.build.version.security_patch)"
        Say "Screen:           $size"
        Say "TalkBack:         $tbVersion, $(if ($tb.On) { 'on' } else { 'off' })"
        Say "Explore by touch: $explore"
        Say "Accessibility services on: $($tb.Services)"
        Say "scrcpy:           $ScrcpyVersion, adb $((Invoke-Adb version | Select-Object -First 1) -replace 'Android Debug Bridge version ', '')"
        Say ''
        Say 'PASSED: the phone is connected and authorized.'
    }

    'Sound' {
        Assert-TalkBackOn
        $wav = Join-Path $root "sound-$Source-$stamp.wav"
        # --no-window stops only the display; --no-video stops the screen
        # being recorded too, which a WAV file cannot hold.
        $arguments = @('--no-video', '--no-control', '--no-window', "--audio-source=$Source", '--audio-codec=raw', '--require-audio',
                       "--record=`"$wav`"", '--time-limit=20')
        if ($Source -eq 'playback') { $arguments += '--audio-dup' }
        Say "Recording 20 seconds of the phone's sound from '$Source'. Follow the prompts."
        $run = Start-Scrcpy $arguments "sound-$Source"
        Wait-WithPrompts $run @(
            @(0,  'Keep still. Do not touch the phone.'),
            @(7,  'Now swipe right 3 times, slowly, about 2 seconds apart.'),
            @(15, 'Stop. Keep still until the chart appears.')
        ) 20
        if (-not (Test-Path $wav)) { Show-ScrcpyLogs $run; Fail "scrcpy wrote no sound file (exit code $($run.Process.ExitCode))." }
        $levels = [WavPeaks]::Read($wav, 0.5)
        Say ''
        Say ("Sound from '{0}': {1:N1} seconds, {2} Hz, {3} channels. Peak level per half second:" -f $Source, $levels.Seconds, $levels.Rate, $levels.Channels)
        Say ''
        # Phone sound can be quiet, and silence from the phone is exactly 0,
        # so bars are scaled to the loudest half second and any half second
        # above 0.1% counts as sound.
        $max = ($levels.Peaks | Measure-Object -Maximum).Maximum
        $loud = 0
        for ($i = 0; $i -lt $levels.Peaks.Length; $i++) {
            $p = $levels.Peaks[$i]
            $bar = if ($max -gt 0) { '#' * [int][Math]::Round($p / $max * 50) } else { '' }
            if ($p -ge 0.001) { $loud++ }
            Say ("{0,5:N1} s {1,5:N1}% |{2}" -f ($i * 0.5), ($p * 100), $bar)
        }
        Say ''
        Say ("Loudest half second: {0:N1}%. Half seconds with sound above 0.1%: {1} of {2}" -f ($max * 100), $loud, $levels.Peaks.Length)
        Say "The sound file is $wav"
    }

    'SpeechText' {
        Assert-TalkBackOn
        $mp4 = Join-Path $root "speech-text-$stamp.mp4"
        Say 'A window titled "Speech text check" opens on the PC and shows the phone for 20 seconds.'
        $run = Start-Scrcpy @('--no-audio', '--no-control', '--window-title="Speech text check"',
                              "--record=`"$mp4`"", '--time-limit=20') 'speech-text'
        Wait-WithPrompts $run @(
            @(4,  'Now swipe right 3 times, slowly. Compare the text at the bottom of the phone with the window.'),
            @(16, 'Stop. The window closes, then the recorded MP4 opens.')
        ) 20
        if (-not (Test-Path $mp4)) { Show-ScrcpyLogs $run; Fail "scrcpy wrote no MP4 (exit code $($run.Process.ExitCode))." }
        Say "The recording is $mp4"
        Invoke-Item $mp4
    }

    'Log' {
        Assert-TalkBackOn
        $tbPid = (((Invoke-Adb shell pidof $TalkBackPackage) -join ' ').Trim() -split '\s+')[0]
        if (-not $tbPid) { Fail 'TalkBack is on but its process was not found.' }
        [void](Invoke-Adb logcat -c)
        Say 'The phone log is cleared.'
        Say 'Now swipe right 3 times, slowly. You have 15 seconds.'
        for ($s = 15; $s -gt 0; $s -= 5) { Say "  $s seconds left"; Start-Sleep -Seconds 5 }
        Say 'Stop.'
        $all = @(Invoke-Adb logcat -d -v monotonic)
        $mine = @(Invoke-Adb logcat -d -v monotonic --pid=$tbPid)
        $file = Join-Path $root "talkback-log-$stamp.txt"
        $mine | Set-Content -Encoding UTF8 -Path $file
        Say ''
        Say "TalkBack (process $tbPid) wrote $($mine.Count) log lines; the whole log has $($all.Count)."
        $word = Read-Host 'Type one word that TalkBack showed on the phone, then press Enter'
        if (-not $word) { Fail 'no word was typed.' }
        $hits = @($mine | Where-Object { $_ -match [regex]::Escape($word) })
        $elsewhere = @($all | Where-Object { $_ -match [regex]::Escape($word) }).Count - $hits.Count
        Say ''
        Say "TalkBack's lines containing '$word': $($hits.Count). Lines from other processes containing it: $elsewhere."
        $hits | Select-Object -First 15 | ForEach-Object { Say "  $_" }
        Say ''
        Say "TalkBack's whole log is in $file"
    }

    'Touch' {
        $caps = @(Invoke-Adb shell getevent -lp)
        $device = $null; $name = $null; $current = $null; $currentName = $null
        $xr = $null; $yr = $null; $cx = $null; $cy = $null
        foreach ($line in $caps) {
            if ($line -match '^add device \d+:\s+(\S+)') {
                if ($cx -and $cy -and -not $device) { $device = $current; $name = $currentName; $xr = $cx; $yr = $cy }
                $current = $Matches[1]; $currentName = $null; $cx = $null; $cy = $null
            } elseif ($line -match '^\s*name:\s+"(.*)"') {
                $currentName = $Matches[1]
            } elseif ($line -match 'ABS_MT_POSITION_X\s*:.*min (\d+), max (\d+)') {
                $cx = @([int]$Matches[1], [int]$Matches[2])
            } elseif ($line -match 'ABS_MT_POSITION_Y\s*:.*min (\d+), max (\d+)') {
                $cy = @([int]$Matches[1], [int]$Matches[2])
            }
        }
        if ($cx -and $cy -and -not $device) { $device = $current; $name = $currentName; $xr = $cx; $yr = $cy }
        if (-not $device) {
            $caps | Select-Object -First 20 | ForEach-Object { Say "  $_" }
            Fail 'no touch screen was found in "getevent -lp".'
        }
        $sizeLine = ((Invoke-Adb shell wm size) | Where-Object { $_ -match 'Physical size' } | Select-Object -First 1)
        if ($sizeLine -notmatch '(\d+)x(\d+)') { Fail "the screen size could not be read: $sizeLine" }
        $w = [int]$Matches[1]; $h = [int]$Matches[2]
        Say "Touch screen: $device, `"$name`", x $($xr[0])..$($xr[1]), y $($yr[0])..$($yr[1]); screen $w by $h pixels."
        Say ''
        Say 'For 12 seconds: tap once near the top of the screen, once in the middle, once near the bottom.'
        # getevent does not flush its output, so through a pipe its lines
        # would be lost when timeout stops it. -tt gives it a terminal on the
        # phone, which makes its output line buffered.
        $raw = @(Invoke-Adb shell -tt timeout 12 getevent -lt $device)
        $file = Join-Path $root "touch-$stamp.txt"
        $raw | Set-Content -Encoding UTF8 -Path $file
        $touches = New-Object System.Collections.Generic.List[object]
        # The touch screen reports a position only when it changes, so a touch
        # with no X or Y of its own keeps the last one reported.
        $open = $null; $lastX = $null; $lastY = $null
        foreach ($line in $raw) {
            if ($line -notmatch '^\[\s*([\d.]+)\]\s+(\S+)\s+(\S+)\s+(\S+)') { continue }
            $t = [double]$Matches[1]; $code = $Matches[3]; $value = $Matches[4]
            if ($code -eq 'ABS_MT_POSITION_X') { $lastX = [Convert]::ToInt32($value, 16) }
            if ($code -eq 'ABS_MT_POSITION_Y') { $lastY = [Convert]::ToInt32($value, 16) }
            if ($open -and $code -eq 'SYN_REPORT') {
                if ($null -eq $open.X) { $open.X = $lastX }
                if ($null -eq $open.Y) { $open.Y = $lastY }
            }
            if ($code -eq 'ABS_MT_TRACKING_ID') {
                if ($value -ne 'ffffffff') { $open = [pscustomobject]@{ Time = $t; X = $null; Y = $null }; $touches.Add($open) }
                else { $open = $null }
            } elseif ($open -and $code -eq 'ABS_MT_POSITION_X' -and $null -eq $open.X) {
                $open.X = [Convert]::ToInt32($value, 16)
            } elseif ($open -and $code -eq 'ABS_MT_POSITION_Y' -and $null -eq $open.Y) {
                $open.Y = [Convert]::ToInt32($value, 16)
            }
        }
        if ($raw -match 'Permission denied') { Fail "getevent was refused: $(($raw | Where-Object { $_ -match 'Permission denied' } | Select-Object -First 1))" }
        Say ''
        Say "Touches: $($touches.Count)"
        $first = $null
        foreach ($t in $touches) {
            if ($null -eq $first) { $first = $t.Time }
            $px = if ($null -ne $t.X) { [int](($t.X - $xr[0]) * $w / ($xr[1] - $xr[0] + 1)) } else { '?' }
            $py = if ($null -ne $t.Y) { [int](($t.Y - $yr[0]) * $h / ($yr[1] - $yr[0] + 1)) } else { '?' }
            Say ("  at {0,5:N2} s: x {1}, y {2} pixels" -f ($t.Time - $first), $px, $py)
        }
        Say ''
        Say "The raw events are in $file"
    }
}

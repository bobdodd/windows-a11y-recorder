# Tests the Magnifier change records (graphics.magnifier): one record for
# each recorded frame whose Magnifier readings differ from the previous
# frame's, with the frame's sequence number, and none for a frame that does
# not differ. See docs/architecture/accessibility-preferences.md, "Visible
# focus and the Enter key".
#
# Run in an ordinary PowerShell window (Windows PowerShell 5.1) while the
# recorder is recording with desktop frames on, from the recorder package
# folder:
#   powershell -ExecutionPolicy Bypass -File .\scripts\Test-MagnifierChanges.ps1
#
# Start with Magnifier off. The script asks you to turn Magnifier on, zoom
# in, pan, invert the colors and back, and turn Magnifier off, and notes
# the time of each step. Then it asks you to stop the recording and for the
# session ID, exports the recording's events, and checks:
#   - every change record names a recorded frame, and its readings are
#     those of that frame and the frame before;
#   - every pair of frames whose readings differ has exactly one record,
#     and every other pair none;
#   - each step gave a record of the part it changes.
#
# -SessionFolder analyses an earlier recording without the steps, and
# -EventsPath uses events already exported. -StepsPath gives the steps.csv
# of an earlier run, to check its steps again.

param(
    [int]$StepSeconds = 4,
    [string]$OutputRoot = 'C:\Users\Public\Downloads',
    [string]$SessionId,
    [string]$SessionsRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Windows A11y Recorder'),
    [string]$SessionFolder,
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DotnetPath = (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
    [string]$EventsPath,
    [string]$StepsPath
)

$ErrorActionPreference = 'Stop'

$out = Join-Path $OutputRoot ('magnifier-changes-test-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $out | Out-Null
$script:log = Join-Path $out 'log.txt'

function Say([string]$text) {
    Write-Host $text
    Add-Content -LiteralPath $script:log -Value $text
}

function Read-Utc([string]$text) {
    return [DateTime]::Parse($text, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}

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

# --- Comparison, as MagnifierChanges.Compare does it -------------------------

function Get-Text($value) {
    if ($null -eq $value) { return 'null' }
    return [string]$value
}

# A reading is null when the frame has no reader for it (not on Windows),
# and then nothing is compared.
function Compare-Readings($beforeMag, $beforeColor, $afterMag, $afterColor) {
    $level = $false; $position = $false; $color = $false
    if ($null -ne $beforeMag -and $null -ne $afterMag) {
        $problem = (Get-Text $beforeMag.problem) -ne (Get-Text $afterMag.problem)
        $level = $problem -or ((Get-Text $beforeMag.level) -ne (Get-Text $afterMag.level))
        $position = $problem -or ((Get-Text $beforeMag.x) -ne (Get-Text $afterMag.x)) -or
            ((Get-Text $beforeMag.y) -ne (Get-Text $afterMag.y))
    }
    if ($null -ne $beforeColor -and $null -ne $afterColor) {
        $before = (@($beforeColor.matrix) | ForEach-Object { [string]$_ }) -join ','
        $after = (@($afterColor.matrix) | ForEach-Object { [string]$_ }) -join ','
        $color = ((Get-Text $beforeColor.problem) -ne (Get-Text $afterColor.problem)) -or ($before -ne $after)
    }
    return [pscustomobject]@{ level = $level; position = $position; colorEffect = $color }
}

function Get-Flags($changed) {
    return '{0}{1}{2}' -f $(if ($changed.level) { 'L' } else { '-' }),
        $(if ($changed.position) { 'P' } else { '-' }), $(if ($changed.colorEffect) { 'C' } else { '-' })
}

function Get-Json($value) { return (ConvertTo-Json -InputObject $value -Compress -Depth 6) }

function Describe-Mag($mag) {
    if ($null -eq $mag) { return 'no reading' }
    if ($null -ne $mag.problem) { return "not read ($($mag.problem))" }
    return '{0} percent at {1}, {2}' -f ([math]::Round([double]$mag.level * 100)), $mag.x, $mag.y
}

# --- Analysis ----------------------------------------------------------------

function Invoke-Analysis([string]$events, [string]$stepsPath) {
    $frames = New-Object System.Collections.Generic.List[object]
    $changes = New-Object System.Collections.Generic.List[object]
    foreach ($line in [IO.File]::ReadLines($events)) {
        $isFrame = $line.Contains('"desktop-frame"')
        $isChange = $line.Contains('"graphics.magnifier"')
        if (-not $isFrame -and -not $isChange) { continue }
        $record = $line | ConvertFrom-Json
        $event = if ($null -ne $record.PSObject.Properties['event']) { $record.event } else { $record }
        $observed = $event.observedUtc
        $utc = if ($observed -is [DateTime]) { $observed.ToUniversalTime() } else { Read-Utc ([string]$observed) }
        $item = [pscustomobject]@{
            Utc = $utc; Time = [long]$event.monotonicNanoseconds; Sequence = [long]$event.sequence
            EventType = [string]$event.eventType; Payload = $event.payload }
        if ($isFrame -and $event.eventType -eq 'desktop-frame') { $frames.Add($item) }
        elseif ($event.channel -eq 'graphics.magnifier') { $changes.Add($item) }
    }
    $frames = @($frames | Sort-Object Time)
    Say ("Frames: {0}; Magnifier change records: {1}" -f $frames.Count, $changes.Count)

    $checks = New-Object System.Collections.Generic.List[object]
    function Add-Check([string]$name, [bool]$passed, [string]$detail) {
        $checks.Add([pscustomobject]@{ check = $name; passed = $passed; detail = $detail })
    }

    $bySequence = @{}
    for ($i = 0; $i -lt $frames.Count; $i++) { $bySequence[$frames[$i].Sequence] = $i }

    # Each record against its frame and the frame before.
    $recordsBySequence = @{}
    $badRecords = New-Object System.Collections.Generic.List[string]
    foreach ($change in $changes) {
        $p = $change.Payload
        $sequence = [long]$p.frameSequence
        if (-not $recordsBySequence.ContainsKey($sequence)) { $recordsBySequence[$sequence] = 0 }
        $recordsBySequence[$sequence]++
        if (-not $bySequence.ContainsKey($sequence)) { $badRecords.Add("frame $sequence not recorded"); continue }
        $index = $bySequence[$sequence]
        if ($index -eq 0) { $badRecords.Add("frame $sequence is the first"); continue }
        $frame = $frames[$index]; $previous = $frames[$index - 1]
        $problems = @()
        if ($change.Time -ne $frame.Time) { $problems += 'time is not the frame''s' }
        if ([long]$p.previousFrameAt -ne $previous.Time) { $problems += 'previousFrameAt is not the frame before' }
        if ((Get-Json $p.current.fullscreenMagnification) -ne (Get-Json $frame.Payload.fullscreenMagnification) -or
            (Get-Json $p.current.fullscreenColorEffect) -ne (Get-Json $frame.Payload.fullscreenColorEffect)) { $problems += 'current is not the frame''s readings' }
        if ((Get-Json $p.previous.fullscreenMagnification) -ne (Get-Json $previous.Payload.fullscreenMagnification) -or
            (Get-Json $p.previous.fullscreenColorEffect) -ne (Get-Json $previous.Payload.fullscreenColorEffect)) { $problems += 'previous is not the frame before''s readings' }
        $expected = Compare-Readings $previous.Payload.fullscreenMagnification $previous.Payload.fullscreenColorEffect `
            $frame.Payload.fullscreenMagnification $frame.Payload.fullscreenColorEffect
        if ((Get-Flags $p.changed) -ne (Get-Flags $expected)) { $problems += "changed $(Get-Flags $p.changed), expected $(Get-Flags $expected)" }
        if ($problems.Count) { $badRecords.Add("frame ${sequence}: $($problems -join '; ')") }
    }
    Add-Check 'every record matches its frames' ($badRecords.Count -eq 0) (($badRecords | Select-Object -First 5) -join ' | ')

    # Each pair of frames: a record exactly when the readings differ.
    $missing = New-Object System.Collections.Generic.List[string]
    $extra = New-Object System.Collections.Generic.List[string]
    $differing = 0
    for ($i = 1; $i -lt $frames.Count; $i++) {
        $a = $frames[$i - 1]; $b = $frames[$i]
        $expected = Compare-Readings $a.Payload.fullscreenMagnification $a.Payload.fullscreenColorEffect `
            $b.Payload.fullscreenMagnification $b.Payload.fullscreenColorEffect
        $differs = $expected.level -or $expected.position -or $expected.colorEffect
        $count = if ($recordsBySequence.ContainsKey($b.Sequence)) { $recordsBySequence[$b.Sequence] } else { 0 }
        if ($differs) { $differing++ }
        if ($differs -and $count -ne 1) { $missing.Add("frame $($b.Sequence): $count records for $(Get-Flags $expected)") }
        if (-not $differs -and $count -ne 0) { $extra.Add("frame $($b.Sequence): $count records, no difference") }
    }
    Say "Frame pairs whose readings differ: $differing"
    Add-Check 'one record for each differing frame' ($missing.Count -eq 0) (($missing | Select-Object -First 5) -join ' | ')
    Add-Check 'no record for an unchanged frame' ($extra.Count -eq 0) (($extra | Select-Object -First 5) -join ' | ')

    # Each step: a record of the part it changes, from its start to the next step's.
    if ($stepsPath -and (Test-Path -LiteralPath $stepsPath)) {
        $steps = @(Import-Csv -LiteralPath $stepsPath)
        for ($i = 0; $i -lt $steps.Count; $i++) {
            $step = $steps[$i]
            $from = Read-Utc $step.startUtc
            $to = if ($i + 1 -lt $steps.Count) { Read-Utc $steps[$i + 1].startUtc } else { (Read-Utc $step.endUtc).AddSeconds(3) }
            $inStep = @($changes | Where-Object { $_.Utc -ge $from -and $_.Utc -lt $to })
            $ofPart = @($inStep | Where-Object { $_.Payload.changed.($step.part) })
            $last = if ($inStep.Count) { $inStep[-1].Payload.current } else { $null }
            $detail = "$($inStep.Count) records, $($ofPart.Count) of $($step.part)"
            if ($last) { $detail += "; last: $(Describe-Mag $last.fullscreenMagnification)" }
            Add-Check "step: $($step.name)" ($ofPart.Count -ge 1) $detail
        }
    }

    $checks | Export-Csv -LiteralPath (Join-Path $out 'checks.csv') -NoTypeInformation
    Say ''
    foreach ($check in $checks) {
        Say ("{0}  {1}{2}" -f $(if ($check.passed) { 'PASS' } else { 'FAIL' }), $check.check,
            $(if ($check.detail) { " ($($check.detail))" } else { '' }))
    }
    Say ''
    Say "Records:"
    foreach ($change in $changes) {
        $p = $change.Payload
        Say ("  frame {0} {1}: {2}" -f $p.frameSequence, (Get-Flags $p.changed), (Describe-Mag $p.current.fullscreenMagnification))
    }
}

# --- Steps -------------------------------------------------------------------

$stepsPath = if ($StepsPath) { $StepsPath } else { Join-Path $out 'steps.csv' }
if (-not $SessionFolder -and -not $EventsPath) {
    $steps = New-Object System.Collections.Generic.List[object]
    function Step([string]$name, [string]$part, [string]$instruction) {
        Say ''
        Say "Step: $name"
        Say "  $instruction"
        [void](Read-Host '  Press Enter here, then do it within a few seconds')
        $start = [DateTime]::UtcNow
        Start-Sleep -Seconds $StepSeconds
        $steps.Add([pscustomobject]@{ name = $name; part = $part
            startUtc = $start.ToString('o'); endUtc = [DateTime]::UtcNow.ToString('o') })
        $steps | Export-Csv -LiteralPath $stepsPath -NoTypeInformation
    }

    Say 'Start a recording in the recorder with desktop frames on, and with Magnifier off.'
    [void](Read-Host 'Press Enter here when it is recording')
    Step 'turn Magnifier on' 'level' 'Press Windows logo key + Plus to start Magnifier in full screen view.'
    Step 'zoom in' 'level' 'Press Windows logo key + Plus once more.'
    Step 'pan' 'position' 'Move the mouse to each edge of the screen in turn.'
    Step 'invert colors' 'colorEffect' 'Press Ctrl + Alt + I.'
    Step 'colors back' 'colorEffect' 'Press Ctrl + Alt + I again.'
    Step 'turn Magnifier off' 'level' 'Press Windows logo key + Esc.'
    Say ''
    [void](Read-Host 'Stop the recording in the recorder now, then press Enter here')
}

if (-not $EventsPath) {
    if (-not $SessionFolder) {
        if (-not $SessionId) { $SessionId = Read-Host 'Type or paste the session ID shown by the recorder, or press Enter for the newest recording' }
        $SessionFolder = Resolve-Session $SessionId
    }
    Say "Session folder: $SessionFolder"
    $EventsPath = Export-Events $SessionFolder $out
}
Invoke-Analysis $EventsPath $stepsPath
Say "Done. Results are in $out"

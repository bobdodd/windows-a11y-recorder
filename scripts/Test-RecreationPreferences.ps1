# Tests the recreation's recorded page values (protocol 0.57, accessibility
# preferences stage 3): a recording of the fixture page with the font size,
# Windows dark mode, animation effects, a contrast theme, and a default zoom
# of 125 percent each changed in turn, then a recreation at a frame after
# each change, with the viewing machine's settings the opposite. See
# docs/architecture/accessibility-preferences.md, "Stage 3", "Required
# tests".
#
# Two runs, each in an ordinary PowerShell window (Windows PowerShell 5.1),
# from the recorder package folder.
#
# 1. The recording. Start a recording with "Instrumented Chromium evidence"
#    on, the Chromium built at 0.57, the starting website set to the fixture
#    page the script prints, and the browser profile folder empty, then:
#      powershell -ExecutionPolicy Bypass -File .\scripts\Test-RecreationPreferences.ps1
#    The script asks you to make each browser change and put it back, makes
#    the Windows dark mode and animation effects changes itself and puts
#    them back, and asks you to turn a contrast theme on and off. Then it
#    asks you to stop the recording, exports its events, checks the
#    color-maps-sent records, and writes inspect.csv: for each change, the
#    time to inspect in the player and the values the recreation must give.
#
# 2. The recreations. Open the recording in the player, then:
#      powershell -ExecutionPolicy Bypass -File .\scripts\Test-RecreationPreferences.ps1 -Recreation -ResultsPath <folder of run 1>
#    For each change the script sets the viewing machine's Windows dark mode
#    and animation effects to the opposite of the recorded values, asks you
#    to open the recreation at the time it gives, to paste a line into
#    DevTools' Console, and to answer two questions on what DevTools shows.
#    It compares the Console's answer with the recorded values and puts the
#    settings back.
#
# -ResultsPath with -EventsPath, -SessionFolder, or -SessionId and no
# -Recreation analyses run 1 again; -EventsPath uses events already
# exported.

param(
    [int]$StepSeconds = 4,
    [string]$OutputRoot = 'C:\Users\Public\Downloads',
    [string]$SessionId,
    [string]$SessionsRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Windows A11y Recorder'),
    [string]$SessionFolder,
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DotnetPath = (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
    [string]$EventsPath,
    [string]$ResultsPath,
    [switch]$Recreation
)

$ErrorActionPreference = 'Stop'

# Each step: its name, who makes it, the change and the restore.
$AllSteps = @(
    [pscustomobject]@{ name = 'font size'; by = 'you'
        change = 'In the Settings window, search for "font size" and set Font size to Large.'
        restore = 'Set Font size back to Medium (Recommended).' }
    [pscustomobject]@{ name = 'windows dark mode'; by = 'code'; change = ''; restore = '' }
    [pscustomobject]@{ name = 'animation effects'; by = 'code'; change = ''; restore = '' }
    [pscustomobject]@{ name = 'contrast theme'; by = 'you'
        change = 'Turn on a contrast theme: in Settings, Accessibility, Contrast themes, choose Night sky and Apply.'
        restore = 'Set the contrast theme back to None and Apply.' }
    [pscustomobject]@{ name = 'default zoom'; by = 'you'
        change = 'In the Settings window, search for "page zoom" and set Page zoom to 125%.'
        restore = 'Set Page zoom back to 100%.' }
)

Add-Type -Namespace RecreationPrefTest -Name Native -MemberDefinition @'
[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
public static extern bool GetInt(uint action, uint param, ref int value, uint winIni);
[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
public static extern bool SetValue(uint action, uint param, IntPtr value, uint winIni);
[DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
public static extern IntPtr Broadcast(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
'@

function Say([string]$text) {
    Write-Host $text
    if ($script:log) { Add-Content -LiteralPath $script:log -Value $text }
}

function Read-Utc([string]$text) {
    return [DateTime]::Parse($text, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}

# The player's time format. Windows PowerShell picks Math.Max(int, int) for
# a literal 0, which a recording's nanoseconds overflow (run of 2026-10-09),
# so both arguments are long, and the ticks are whole.
function Format-Time([long]$nanoseconds) {
    $value = [TimeSpan]::FromTicks([long][Math]::Floor([Math]::Max([long]0, $nanoseconds) / 100))
    return '{0:00}:{1:00}:{2:00}.{3:000}' -f [int][Math]::Floor($value.TotalHours), $value.Minutes, $value.Seconds, $value.Milliseconds
}

$FixturePath = Join-Path $PackageRoot 'tests\fixtures\accessibility-preferences\index.html'
$FixtureUrl = ([Uri](Resolve-Path -LiteralPath $FixturePath).Path).AbsoluteUri

# --- The viewing machine's settings ------------------------------------------

$SpifUpdateAndSend = [uint32]3
$HwndBroadcast = [IntPtr]0xFFFF
$WmSettingChange = [uint32]0x001A
$SmtoAbortIfHung = [uint32]0x0002
$PersonalizeKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'

# Windows dark mode by the registry value Settings sets and the
# WM_SETTINGCHANGE it broadcasts; 1 is light.
function Get-AppsLight { return [int](Get-ItemProperty -LiteralPath $PersonalizeKey -Name AppsUseLightTheme).AppsUseLightTheme }
function Set-AppsLight([int]$value) {
    Set-ItemProperty -LiteralPath $PersonalizeKey -Name AppsUseLightTheme -Value $value -Type DWord
    $result = [IntPtr]::Zero
    [void][RecreationPrefTest.Native]::Broadcast($HwndBroadcast, $WmSettingChange, [IntPtr]::Zero, 'ImmersiveColorSet', $SmtoAbortIfHung, 2000, [ref]$result)
}

# Animation effects by SPI_GETCLIENTAREAANIMATION and
# SPI_SETCLIENTAREAANIMATION; 1 is on.
function Get-Animation { $value = 0; [void][RecreationPrefTest.Native]::GetInt(0x1042, 0, [ref]$value, 0); return $value }
function Set-Animation([int]$value) { [void][RecreationPrefTest.Native]::SetValue(0x1043, 0, [IntPtr]$value, $SpifUpdateAndSend) }

# --- Analysis of the recording -----------------------------------------------

function Read-Events([string]$events) {
    $records = New-Object System.Collections.Generic.List[object]
    foreach ($line in [IO.File]::ReadLines($events)) {
        if (-not $line.Contains('"browser.preferences"') -and -not $line.Contains('"navigation-completed"') -and
            -not $line.Contains('"layout-checkpoint-started"')) { continue }
        $record = $line | ConvertFrom-Json
        $event = if ($null -ne $record.PSObject.Properties['event']) { $record.event } else { $record }
        if ($event.channel -ne 'browser.preferences' -and $event.eventType -ne 'navigation-completed' -and
            $event.eventType -ne 'layout-checkpoint-started') { continue }
        $observed = $event.observedUtc
        $utc = if ($observed -is [DateTime]) { $observed.ToUniversalTime() } else { Read-Utc ([string]$observed) }
        $records.Add([pscustomobject]@{
            Utc = $utc; Time = [long]$event.monotonicNanoseconds; Channel = $event.channel
            EventType = $event.eventType; Payload = $event.payload })
    }
    return $records
}

# "#AARRGGBB" as getComputedStyle writes an opaque color.
function Get-CssColor([string]$argb) {
    if ($argb -notmatch '^#[0-9A-Fa-f]{8}$') { return '' }
    $r = [Convert]::ToInt32($argb.Substring(3, 2), 16)
    $g = [Convert]::ToInt32($argb.Substring(5, 2), 16)
    $b = [Convert]::ToInt32($argb.Substring(7, 2), 16)
    return "rgb($r, $g, $b)"
}

# The default zoom at a time, as a factor: the last default zoom record at
# or before it, else 100 percent.
function Get-DefaultZoomFactor($zooms, [long]$time) {
    $last = @($zooms | Where-Object { $_.Payload.mode -eq 'default' -and $_.Time -le $time } | Select-Object -Last 1)
    if ($last.Count -eq 0) { return 1.0 }
    return [double]$last[0].Payload.zoomPercent / 100
}

function Invoke-Analysis([string]$results, [string]$events) {
    $records = Read-Events $events
    $checks = New-Object System.Collections.Generic.List[object]
    function Add-Check([string]$name, [bool]$passed, [string]$detail) {
        $checks.Add([pscustomobject]@{ check = $name; passed = $passed; detail = $detail })
    }
    $fixture = @($records | Where-Object {
            $_.EventType -eq 'navigation-completed' -and $_.Payload.committed -and $_.Payload.primaryPage -and
            $_.Payload.frameType -eq 'primary-main-frame' -and
            ([string]$_.Payload.url).EndsWith('accessibility-preferences/index.html') })
    $pages = @($fixture | ForEach-Object { [int](([string]$_.Payload.context.pageId) -replace '^frame-', '') } | Sort-Object -Unique)
    $tokens = @($fixture | ForEach-Object { [string]$_.Payload.context.documentToken } | Sort-Object -Unique)
    Add-Check 'the fixture page was loaded' ($pages.Count -ge 1) "pages $($pages -join ', ')"
    $sends = @($records | Where-Object { $_.EventType -eq 'web-preferences-sent' -and $pages -contains [int]$_.Payload.pageFrameTreeNodeId })
    $maps = @($records | Where-Object { $_.EventType -eq 'color-maps-sent' -and $pages -contains [int]$_.Payload.pageFrameTreeNodeId })
    $allMaps = @($records | Where-Object { $_.EventType -eq 'color-maps-sent' })
    $zooms = @($records | Where-Object { $_.EventType -eq 'zoom-level-changed' })
    $layouts = @($records | Where-Object { $_.EventType -eq 'layout-checkpoint-started' -and $tokens -contains [string]$_.Payload.context.documentToken })
    $changeSets = @($records | Where-Object { $_.EventType -eq 'layout-changes-started' -and $tokens -contains [string]$_.Payload.context.documentToken })
    Say ("Records: {0} color-maps-sent ({1} to the fixture page), {2} sends to it, {3} zoom changes, {4} layout walks of it" -f
        $allMaps.Count, $maps.Count, $sends.Count, $zooms.Count, $layouts.Count)
    $firstMaps = @($maps | Where-Object { $_.Payload.first })
    Add-Check 'the fixture page was sent its first color maps' ($firstMaps.Count -ge 1) "$($firstMaps.Count) first records"
    foreach ($first in $firstMaps) {
        $names = @($first.Payload.maps.PSObject.Properties | ForEach-Object { $_.Name })
        $counts = @($first.Payload.maps.PSObject.Properties | ForEach-Object { @($_.Value.PSObject.Properties).Count })
        Add-Check 'a first record holds three maps of 67 colors' ($names.Count -eq 3 -and @($counts | Where-Object { $_ -ne 67 }).Count -eq 0) "maps $($names -join ', '); colors $($counts -join ', ')"
    }

    $steps = @(Import-Csv -LiteralPath (Join-Path $results 'steps.csv'))
    $inspect = New-Object System.Collections.Generic.List[object]
    foreach ($step in @($steps | Where-Object { $_.step -like '* change' })) {
        $name = $step.step -replace ' change$', ''
        $from = Read-Utc $step.startUtc
        $to = Read-Utc $step.endUtc
        $inStep = @($records | Where-Object { $_.Utc -ge $from -and $_.Utc -le $to })
        $sendsIn = @($inStep | Where-Object { $sends -contains $_ })
        $mapsIn = @($inStep | Where-Object { $maps -contains $_ })
        if ($name -eq 'contrast theme') {
            $forcedMaps = @($mapsIn | Where-Object { $null -ne $_.Payload.maps.PSObject.Properties['forcedColors'] })
            Add-Check 'the contrast theme sent the page a forced colors map' ($forcedMaps.Count -ge 1) "$($mapsIn.Count) color map records in the step"
        }
        # The time to inspect: the end of the step, after the change has
        # reached the page and been drawn.
        $last = @($records | Where-Object { $_.Utc -le $to } | Select-Object -Last 1)
        if ($last.Count -eq 0) { continue }
        $time = $last[0].Time
        # The values at that time: the fields last sent, the latest maps,
        # the latest layout walk, and the latest layout change set.
        $fields = @{}
        foreach ($send in @($sends | Where-Object { $_.Time -le $time })) {
            foreach ($field in $send.Payload.fields.PSObject.Properties) { $fields[$field.Name] = $field.Value }
        }
        $mapAt = @{}
        foreach ($record in @($maps | Where-Object { $_.Time -le $time })) {
            foreach ($map in $record.Payload.maps.PSObject.Properties) { $mapAt[$map.Name] = $map.Value }
        }
        $layout = @($layouts | Where-Object { $_.Time -le $time } | Select-Object -Last 1)
        # The page as the recreation shows it ("The recorded layout zoom" in
        # docs/architecture/accessibility-preferences.md, as revised on
        # 2026-10-09): the frame is the latest layout walk's size in screen
        # pixels, its CSS size times its devicePixelRatio, and the page is
        # laid out at the layout zoom of the latest layout change set, which
        # follows the zoom and text size, or the walk's when there is none.
        # Nothing is emulated, so the page's devicePixelRatio is that layout
        # zoom and its CSS size the screen size over it. The fixture page is
        # a file, so only the default zoom applies to it; it is listed for
        # reference, and is not applied as a zoom level.
        $zoomNow = Get-DefaultZoomFactor $zooms $time
        $changeSet = @($changeSets | Where-Object { $_.Time -le $time } | Select-Object -Last 1)
        $layoutZoom = ''
        $expectedDpr = ''
        $expectedWidth = ''
        $expectedHeight = ''
        if ($changeSet.Count -and $null -ne $changeSet[0].Payload.layoutZoomFactor) { $layoutZoom = [double]$changeSet[0].Payload.layoutZoomFactor }
        elseif ($layout.Count) { $layoutZoom = [double]$layout[0].Payload.layoutZoomFactor }
        if ($layout.Count -and $layoutZoom) {
            $walkDpr = [double]$layout[0].Payload.devicePixelRatio
            $expectedDpr = [Math]::Round($layoutZoom, 4)
            $expectedWidth = [Math]::Round([double]$layout[0].Payload.viewport.width * $walkDpr / $layoutZoom, 2)
            $expectedHeight = [Math]::Round([double]$layout[0].Payload.viewport.height * $walkDpr / $layoutZoom, 2)
        }
        $dark = [string]$fields['preferredColorScheme'] -eq 'dark'
        $forced = [bool]$fields['inForcedColors']
        $canvasMap = if ($forced) { 'forcedColors' } elseif ($dark) { 'dark' } else { 'light' }
        $canvas = if ($mapAt.ContainsKey($canvasMap)) { Get-CssColor ([string]$mapAt[$canvasMap].kColorCssSystemWindow) } else { '' }
        $inspect.Add([pscustomobject]@{
            step = $name
            time = Format-Time $time
            nanoseconds = $time
            dark = $dark
            forced = $forced
            reduced = [bool]$fields['prefersReducedMotion']
            fontSize = "$($fields['defaultFontSize'])px"
            canvas = $canvas
            zoomPercent = [Math]::Round($zoomNow * 100, 2)
            layoutZoom = $layoutZoom
            dpr = $expectedDpr
            width = $expectedWidth
            height = $expectedHeight
            layoutWalk = if ($layout.Count) { Format-Time $layout[0].Time } else { '' }
            walkDpr = if ($layout.Count) { [double]$layout[0].Payload.devicePixelRatio } else { '' }
            sendsInStep = $sendsIn.Count
            mapsInStep = $mapsIn.Count })
    }
    $checks | Export-Csv -LiteralPath (Join-Path $results 'checks.csv') -NoTypeInformation
    $inspect | Export-Csv -LiteralPath (Join-Path $results 'inspect.csv') -NoTypeInformation
    $text = Join-Path $results 'summary.txt'
    $checks | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Tee-Object -FilePath $text | Write-Host
    $inspect | Format-Table -AutoSize | Out-String -Width 220 | Tee-Object -FilePath $text -Append | Write-Host
    Say 'inspect.csv lists, for each change, the player time to inspect and the values the recreation must give there.'
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

# --- Run 2: the recreations --------------------------------------------------

# Pasted into DevTools' Console in the recreation. It adds an element of its
# own for a moment, to read the default font size and the Canvas system
# color, and copies its answer to the clipboard.
$ConsoleLine = "(()=>{const d=document.createElement('div');d.style.cssText='position:absolute;font-size:medium;background:Canvas';" +
    "document.documentElement.append(d);const s=getComputedStyle(d);const r={dark:matchMedia('(prefers-color-scheme: dark)').matches," +
    "forced:matchMedia('(forced-colors: active)').matches,reduced:matchMedia('(prefers-reduced-motion: reduce)').matches," +
    "fontSize:s.fontSize,canvas:s.backgroundColor,dpr:devicePixelRatio,width:innerWidth,height:innerHeight," +
    "attribute:document.documentElement.hasAttribute('data-a11y-recorded-preferences')};d.remove();copy(JSON.stringify(r));return r})()"

function Invoke-Recreations([string]$results) {
    $inspect = @(Import-Csv -LiteralPath (Join-Path $results 'inspect.csv'))
    if ($inspect.Count -eq 0) { throw "inspect.csv in $results lists no change." }
    $rows = New-Object System.Collections.Generic.List[object]
    $wasLight = Get-AppsLight
    $wasAnimation = Get-Animation
    try {
        foreach ($item in $inspect) {
            $dark = $item.dark -eq 'True'
            $reduced = $item.reduced -eq 'True'
            $forced = $item.forced -eq 'True'
            # The viewing machine the opposite of the recorded values.
            Set-AppsLight $(if ($dark) { 1 } else { 0 })
            Set-Animation $(if ($reduced) { 1 } else { 0 })
            Say ''
            Say ("Step '{0}': this machine is now in {1} mode with animation effects {2}." -f $item.step,
                $(if ($dark) { 'light' } else { 'dark' }), $(if ($reduced) { 'on' } else { 'off' }))
            if ($forced) { Say 'The recorded page was in a contrast theme; leave this machine with no contrast theme.' }
            else { Say 'Leave this machine with no contrast theme, so that a forced colors answer can only be the recorded one.' }
            Set-Clipboard -Value $ConsoleLine
            [void](Read-Host ("In the player, go to {0}, click ""Inspect page at this frame"", choose the fixture page, and in the " +
                "recreation press F12 for DevTools. In its Console, paste the line now on the clipboard and press Enter; its answer " +
                "is copied. Then press Enter here") -f $item.time)
            $answer = $null
            try { $answer = (Get-Clipboard -Raw) | ConvertFrom-Json } catch { $answer = $null }
            if ($null -eq $answer -or $null -eq $answer.PSObject.Properties['dark']) {
                Say 'The clipboard does not hold the Console''s answer; this step is recorded as not answered.'
                $rows.Add([pscustomobject]@{ step = $item.step; time = $item.time; passed = $false; detail = 'no answer' })
                continue
            }
            $differences = New-Object System.Collections.Generic.List[string]
            if ([bool]$answer.dark -ne $dark) { $differences.Add("prefers-color-scheme dark $($answer.dark), recorded $dark") }
            if ([bool]$answer.forced -ne $forced) { $differences.Add("forced-colors active $($answer.forced), recorded $forced") }
            if ([bool]$answer.reduced -ne $reduced) { $differences.Add("prefers-reduced-motion reduce $($answer.reduced), recorded $reduced") }
            if ([string]$answer.fontSize -ne $item.fontSize) { $differences.Add("font size $($answer.fontSize), recorded $($item.fontSize)") }
            if ($item.canvas -and [string]$answer.canvas -ne $item.canvas) { $differences.Add("Canvas $($answer.canvas), recorded $($item.canvas)") }
            if ($item.dpr -and [Math]::Abs([double]$answer.dpr - [double]$item.dpr) -gt 0.001) { $differences.Add("devicePixelRatio $($answer.dpr), recorded $($item.dpr)") }
            if ($item.width -and [Math]::Abs([double]$answer.width - [double]$item.width) -gt 1) { $differences.Add("innerWidth $($answer.width), recorded $($item.width)") }
            if ($item.height -and [Math]::Abs([double]$answer.height - [double]$item.height) -gt 1) { $differences.Add("innerHeight $($answer.height), recorded $($item.height)") }
            if (-not [bool]$answer.attribute) { $differences.Add('the root has no data-a11y-recorded-preferences attribute') }
            $styles = Read-Host ("In DevTools' Elements pane, select the body element. Does the Styles pane show the @media rules that " +
                "match the recorded values applying, and not the others (for example @media (prefers-color-scheme: dark) applying " +
                "only when the recorded page was dark)? (y/n)")
            $controls = Read-Host ("Do the recreation's scroll bars have the recorded page's scheme or contrast colors, not this " +
                "machine's? (y/n)")
            $passed = $differences.Count -eq 0 -and $styles -eq 'y' -and $controls -eq 'y'
            $rows.Add([pscustomobject]@{
                step = $item.step; time = $item.time; passed = $passed
                detail = $(if ($differences.Count) { $differences -join '; ' } else { 'Console answers as recorded' })
                stylesPane = $styles; scrollBars = $controls; answer = (Get-Clipboard -Raw) })
            Say ("{0}: {1}" -f $item.step, $(if ($passed) { 'passed' } else { 'not passed: ' + ($differences -join '; ') }))
            [void](Read-Host 'Close the recreation window, then press Enter here')
        }
    }
    finally {
        Set-AppsLight $wasLight
        Set-Animation $wasAnimation
        Say 'This machine''s dark mode and animation effects are put back.'
    }
    $rows | Export-Csv -LiteralPath (Join-Path $results 'recreation-checks.csv') -NoTypeInformation
    $rows | Format-List | Out-String -Width 220 | Tee-Object -FilePath (Join-Path $results 'recreation-summary.txt') | Write-Host
}

if ($Recreation) {
    if (-not $ResultsPath) { throw 'Give -ResultsPath, the folder of the recording run, with -Recreation.' }
    $script:log = Join-Path $ResultsPath 'recreation-log.txt'
    Invoke-Recreations $ResultsPath
    Say "Done. Results are in $ResultsPath"
    return
}

if ($ResultsPath) {
    $script:log = Join-Path $ResultsPath 'analysis-log.txt'
    if (-not $EventsPath) {
        if (-not $SessionFolder) {
            if (-not $SessionId) { throw 'Give -EventsPath, -SessionFolder, or -SessionId with -ResultsPath.' }
            $SessionFolder = Resolve-Session $SessionId
        }
        $EventsPath = Export-Events $SessionFolder $ResultsPath
    }
    Invoke-Analysis $ResultsPath $EventsPath
    return
}

# --- Run 1: the recording ----------------------------------------------------

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$out = Join-Path $OutputRoot "recreation-preferences-test-$stamp"
New-Item -ItemType Directory -Path $out | Out-Null
$script:log = Join-Path $out 'log.txt'

$steps = New-Object System.Collections.Generic.List[object]
function Add-Step([string]$name, [string]$pass, [DateTime]$startUtc) {
    $steps.Add([pscustomobject]@{ step = "$name $pass"; startUtc = $startUtc.ToString('o'); endUtc = [DateTime]::UtcNow.ToString('o') })
    $steps | Export-Csv -LiteralPath (Join-Path $out 'steps.csv') -NoTypeInformation
}

function Invoke-CodeStep($step) {
    if ($step.name -eq 'windows dark mode') {
        $was = Get-AppsLight
        $values = @($(if ($was -ne 0) { 0 } else { 1 }), $was)
        $set = { param($value) Set-AppsLight $value }
    }
    else {
        $was = Get-Animation
        $values = @($(if ($was -ne 0) { 0 } else { 1 }), $was)
        $set = { param($value) Set-Animation $value }
    }
    foreach ($pass in @('change', 'restore')) {
        $startUtc = [DateTime]::UtcNow
        & $set $values[$(if ($pass -eq 'change') { 0 } else { 1 })]
        Start-Sleep -Seconds $StepSeconds
        Add-Step $step.name $pass $startUtc
        Say "$($step.name) $pass made"
    }
}

function Invoke-YourStep($step) {
    Say ''
    $startUtc = [DateTime]::UtcNow
    [void](Read-Host "$($step.change) Then press Enter here")
    Start-Sleep -Seconds $StepSeconds
    Add-Step $step.name 'change' $startUtc
    $startUtc = [DateTime]::UtcNow
    [void](Read-Host "$($step.restore) Then press Enter here")
    Start-Sleep -Seconds $StepSeconds
    Add-Step $step.name 'restore' $startUtc
}

Say "Writing results to $out"
Say "The recording should use the starting website $FixtureUrl and an empty browser profile folder."
Say 'Do not change any other setting while the script runs.'
[void](Read-Host ('In Chromium, press Ctrl+N for a new window and go to chrome://settings there. Then click in the ' +
    'fixture page window and press F5, so it is the page the player shows. Then press Enter here'))
foreach ($step in $AllSteps) {
    if ($step.by -eq 'code') { Invoke-CodeStep $step } else { Invoke-YourStep $step }
}
Say ''
[void](Read-Host 'Stop the recording in the recorder now, then press Enter here')
if (-not $SessionFolder) {
    if (-not $SessionId) { $SessionId = Read-Host 'Type or paste the session ID shown by the recorder, or press Enter for the newest recording' }
    $SessionFolder = Resolve-Session $SessionId
}
Say "Session folder: $SessionFolder"
if (-not $EventsPath) { $EventsPath = Export-Events $SessionFolder $out }
Invoke-Analysis $out $EventsPath
Say "Done. Results are in $out. For the recreations, open the recording in the player and run:"
Say "  powershell -ExecutionPolicy Bypass -File .\scripts\Test-RecreationPreferences.ps1 -Recreation -ResultsPath `"$out`""

# Tests the recording of the browser preferences (protocol 0.56): the
# listed preferences of the profile at its load, one change record for each
# change made in the browser, a zoom record for each zoom change, and the
# values sent to the page after each change that reaches it. See
# docs/architecture/accessibility-preferences.md, "Stage 2", "Required
# tests".
#
# Run in an ordinary PowerShell window (Windows PowerShell 5.1), from the
# recorder package folder, while the recorder is recording with
# "Instrumented Chromium evidence" on, the Chromium built at 0.56, the
# starting website set to the fixture page the script prints, and the
# browser profile folder empty:
#   powershell -ExecutionPolicy Bypass -File .\scripts\Test-BrowserPreferences.ps1
#
# Page colors is not a step: this Chromium has no setting that changes it
# while it runs, so it is tested by the prepared profile run, with the
# number Set-PageColors.ps1 stores in the folder (run of 2026-10-08).
#
# The script asks you to make each browser change and put it back, makes
# Windows dark mode and animation effects changes itself and puts them
# back, and asks you to turn a contrast theme on and off. After each change
# it asks whether the fixture page showed it. Then it asks you to stop the
# recording, exports its events, and checks the records.
#
# The prepared profile run: -ProfileFolder names a profile folder prepared
# beforehand, with its font size set to Large in its settings. Start a
# recording with that folder as the browser profile folder, then run the
# script with -ProfileFolder; it asks you to stop the recording and checks
# that the profile's preferences were recorded at the start, from that
# folder, and that only the listed preferences were. With
# -PreparedPageColors, the number Set-PageColors.ps1 stored in the folder
# beforehand, it also checks that number was recorded and that the fixture
# page's first values had forced colors on (from 2) or off (1).
#
# -ResultsPath and -SessionFolder analyse an earlier run again.
# -EventsPath uses events already exported. -Steps runs only the named
# steps, for example: -Steps 'font size','animation effects'

param(
    [int]$StepSeconds = 3,
    [string]$OutputRoot = 'C:\Users\Public\Downloads',
    [string]$SessionId,
    [string]$SessionsRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Windows A11y Recorder'),
    [string]$SessionFolder,
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DotnetPath = (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
    [string]$EventsPath,
    [string]$ResultsPath,
    [string]$ProfileFolder,
    [int]$PreparedPageColors = -1,
    [string[]]$Steps
)

$ErrorActionPreference = 'Stop'

# Each step: its name, who makes it, the browser preference whose change
# records it expects (or the zoom mode), a preference Chromium changes with
# it (companion), the field of the values sent to the page it expects, the
# change and the restore, and what the fixture page shows when it is made.
$AllSteps = @(
    [pscustomobject]@{ name = 'font size'; by = 'you'; preference = 'defaultFontSize'; companion = 'defaultFixedFontSize'; zoom = ''; field = 'defaultFontSize'
        change = 'In the Settings window, search for "font size" and set Font size to Large.'
        restore = 'Set Font size back to Medium (Recommended).'
        page = 'the Default font size row reads 20px and the bar is wider' }
    [pscustomobject]@{ name = 'default zoom'; by = 'you'; preference = ''; companion = ''; zoom = 'default'; field = ''
        change = 'In the Settings window, search for "page zoom" and set Page zoom to 125%.'
        restore = 'Set Page zoom back to 100%.'
        page = 'the page is larger and the Zoom row reads 1.25 times the display scale' }
    [pscustomobject]@{ name = 'page zoom'; by = 'you'; preference = ''; companion = ''; zoom = 'host'; field = ''
        change = 'Click in the fixture page window and press Ctrl and the plus key once.'
        restore = 'Press Ctrl and 0 in the fixture page window.'
        page = 'the page is larger' }
    [pscustomobject]@{ name = 'browser color mode'; by = 'you'; preference = 'colorScheme'; companion = ''; zoom = ''; field = 'preferredColorScheme'
        change = 'In the Settings window, search for "mode" and set Mode to Dark, or to Light if Windows is in dark mode.'
        restore = 'Set Mode back to Device.'
        page = 'the prefers-color-scheme row changes and the page background changes' }
    [pscustomobject]@{ name = 'windows dark mode'; by = 'code'; preference = ''; companion = ''; zoom = ''; field = 'preferredColorScheme'
        change = ''; restore = ''
        page = 'the prefers-color-scheme row changes' }
    [pscustomobject]@{ name = 'animation effects'; by = 'code'; preference = ''; companion = ''; zoom = ''; field = 'prefersReducedMotion'
        change = ''; restore = ''
        page = 'the prefers-reduced-motion row reads reduce and the spinner stops' }
    [pscustomobject]@{ name = 'contrast theme'; by = 'you'; preference = ''; companion = ''; zoom = ''; field = 'inForcedColors'
        change = 'Turn on a contrast theme: in Settings, Ease of Access, High contrast, turn on high contrast.'
        restore = 'Turn high contrast off.'
        page = 'the forced-colors row reads active' }
)

$Steps = @($Steps | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($name in $Steps) {
    if (@($AllSteps | ForEach-Object { $_.name }) -notcontains $name) {
        throw "'$name' is not a step. The steps are: $(($AllSteps | ForEach-Object { $_.name }) -join ', ')."
    }
}
$Planned = if ($Steps.Count) { @($AllSteps | Where-Object { $Steps -contains $_.name }) } else { $AllSteps }

Add-Type -Namespace BrowserPrefTest -Name Native -MemberDefinition @'
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

function Get-Percent($value) {
    return ([double]$value).ToString('0.#', [Globalization.CultureInfo]::InvariantCulture)
}

function Get-JsonText($value) {
    if ($null -eq $value) { return 'null' }
    if ($value -is [bool]) { if ($value) { return 'true' } else { return 'false' } }
    return (ConvertTo-Json -InputObject $value -Compress -Depth 8)
}

$FixturePath = Join-Path $PackageRoot 'tests\fixtures\accessibility-preferences\index.html'
$FixtureUrl = ([Uri](Resolve-Path -LiteralPath $FixturePath).Path).AbsoluteUri

# --- Analysis ----------------------------------------------------------------

function Read-Events([string]$events) {
    $records = New-Object System.Collections.Generic.List[object]
    foreach ($line in [IO.File]::ReadLines($events)) {
        if (-not $line.Contains('"browser.preferences"') -and -not $line.Contains('"navigation-completed"')) { continue }
        $record = $line | ConvertFrom-Json
        $event = if ($null -ne $record.PSObject.Properties['event']) { $record.event } else { $record }
        if ($event.channel -ne 'browser.preferences' -and $event.eventType -ne 'navigation-completed') { continue }
        $observed = $event.observedUtc
        $utc = if ($observed -is [DateTime]) { $observed.ToUniversalTime() } else { Read-Utc ([string]$observed) }
        $records.Add([pscustomobject]@{ Utc = $utc; Channel = $event.channel; EventType = $event.eventType; Payload = $event.payload })
    }
    return $records
}

function Write-Results([string]$results, $checks, $rows) {
    $checks | Export-Csv -LiteralPath (Join-Path $results 'checks.csv') -NoTypeInformation
    $text = Join-Path $results 'summary.txt'
    $checks | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Tee-Object -FilePath $text | Write-Host
    if ($rows) {
        $rows | Export-Csv -LiteralPath (Join-Path $results 'summary.csv') -NoTypeInformation
        $rows | Format-List | Out-String -Width 260 | Tee-Object -FilePath $text -Append | Write-Host
    }
}

function Invoke-Analysis([string]$results, [string]$events) {
    $records = Read-Events $events
    $checks = New-Object System.Collections.Generic.List[object]
    function Add-Check([string]$name, [bool]$passed, [string]$detail) {
        $checks.Add([pscustomobject]@{ check = $name; passed = $passed; detail = $detail })
    }
    $snapshots = @($records | Where-Object { $_.EventType -eq 'browser-preferences' })
    $changes = @($records | Where-Object { $_.EventType -eq 'browser-preference-changed' })
    $zooms = @($records | Where-Object { $_.EventType -eq 'zoom-level-changed' })
    $sends = @($records | Where-Object { $_.EventType -eq 'web-preferences-sent' })
    Say ("Browser preference records: {0} snapshots, {1} changes, {2} zoom changes, {3} sends" -f
        $snapshots.Count, $changes.Count, $zooms.Count, $sends.Count)
    Add-Check 'one preferences record at the profile load' ($snapshots.Count -eq 1) "$($snapshots.Count) found"
    if ($snapshots.Count -ge 1) {
        $first = $snapshots[0].Payload
        $problems = @($first.preferences.PSObject.Properties | Where-Object { $null -ne $_.Value.problem } |
            ForEach-Object { "$($_.Name): $($_.Value.problem)" })
        Say ("Profile: {0}, new: {1}; readings with a problem: {2}" -f $first.profileDirectory, $first.newProfile,
            $(if ($problems.Count) { $problems -join '; ' } else { 'none' }))
        if ($ProfileFolder) {
            $folder = [IO.Path]::GetFullPath($ProfileFolder).TrimEnd('\')
            Add-Check 'the profile is the prepared folder' ([string]$first.profileDirectory).StartsWith($folder, [StringComparison]::OrdinalIgnoreCase) "$($first.profileDirectory)"
            Add-Check 'the profile was not new' (-not $first.newProfile) "newProfile $($first.newProfile)"
            $size = $first.preferences.defaultFontSize
            Add-Check 'the prepared font size is recorded' ($size.value -eq 20 -and -not $size.isDefault) "value $($size.value), default $($size.isDefault)"
            if ($PreparedPageColors -ge 0) {
                $colors = $first.preferences.requestedPageColors
                Add-Check 'the prepared page colors are recorded' ($colors.value -eq $PreparedPageColors -and -not $colors.isDefault) "value $($colors.value), default $($colors.isDefault)"
            }
        }
        else {
            Add-Check 'the profile was new' ([bool]$first.newProfile) "newProfile $($first.newProfile)"
        }
    }
    # The default zoom at the start: a default record before the first step,
    # or 100 percent, as the zoom map records no change to the level it has.
    $defaults = @($zooms | Where-Object { $_.Payload.mode -eq 'default' })
    $stepsFile = Join-Path $results 'steps.csv'
    $firstStep = if (Test-Path -LiteralPath $stepsFile) { Read-Utc (@(Import-Csv -LiteralPath $stepsFile)[0].startUtc) } else { [DateTime]::MaxValue }
    $before = @($defaults | Where-Object { $_.Utc -lt $firstStep })
    Say ("Default zoom at the start: {0}; default zoom records: {1}" -f
        $(if ($before.Count) { "$(Get-Percent $before[-1].Payload.zoomPercent)%" } else { '100% (no record before the first step)' }),
        $(if ($defaults.Count) { ($defaults | ForEach-Object { "$(Get-Percent $_.Payload.zoomPercent)% at $($_.Utc.ToString('HH:mm:ss'))" }) -join ', ' } else { 'none' }))

    # The fixture page, by the frame tree node id its navigations name it by.
    $fixturePages = @($records | Where-Object {
            $_.EventType -eq 'navigation-completed' -and $_.Payload.committed -and $_.Payload.primaryPage -and
            ([string]$_.Payload.url).EndsWith('accessibility-preferences/index.html') } |
        ForEach-Object { [int](([string]$_.Payload.context.pageId) -replace '^frame-', '') } | Sort-Object -Unique)
    Add-Check 'the fixture page was loaded' ($fixturePages.Count -ge 1) "pages $($fixturePages -join ', ')"
    $firstSends = @($sends | Where-Object { $_.Payload.first -and $fixturePages -contains [int]$_.Payload.pageFrameTreeNodeId })
    Add-Check 'the fixture page was sent its first values' ($firstSends.Count -ge 1) "$($firstSends.Count) first sends"
    if ($ProfileFolder -and $PreparedPageColors -ge 1) {
        $forced = @($firstSends | ForEach-Object { $_.Payload.fields.inForcedColors })
        Add-Check 'the first values had forced colors as the page colors set' (
            $forced.Count -ge 1 -and @($forced | Where-Object { [bool]$_ -ne ($PreparedPageColors -ge 2) }).Count -eq 0) "inForcedColors $(($forced | ForEach-Object { Get-JsonText $_ }) -join ', ')"
    }

    if ($ProfileFolder) { Write-Results $results $checks $null; return }

    $steps = @(Import-Csv -LiteralPath (Join-Path $results 'steps.csv'))
    $rows = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $steps.Count; $i++) {
        $step = $steps[$i]
        $definition = $AllSteps | Where-Object { $step.step -eq "$($_.name) change" -or $step.step -eq "$($_.name) restore" } | Select-Object -First 1
        $companion = if ($definition) { $definition.companion } else { '' }
        $from = Read-Utc $step.startUtc
        $to = if ($i + 1 -lt $steps.Count) { Read-Utc $steps[$i + 1].startUtc } else { (Read-Utc $step.endUtc).AddSeconds(5) }
        $inStep = @($records | Where-Object { $_.Utc -ge $from -and $_.Utc -lt $to })
        $mine = @($inStep | Where-Object { $_.EventType -eq 'browser-preference-changed' -and $step.preference -and $_.Payload.preference -eq $step.preference })
        $companions = @($inStep | Where-Object { $_.EventType -eq 'browser-preference-changed' -and $companion -and $_.Payload.preference -eq $companion })
        $otherChanges = @($inStep | Where-Object { $_.EventType -eq 'browser-preference-changed' -and $_.Payload.preference -ne $step.preference -and $_.Payload.preference -ne $companion } |
            ForEach-Object { "{0} {1} to {2}" -f $_.Payload.preference, (Get-JsonText $_.Payload.previous.($_.Payload.preference).value), (Get-JsonText $_.Payload.current.($_.Payload.preference).value) })
        $zoomed = @($inStep | Where-Object { $_.EventType -eq 'zoom-level-changed' })
        $myZoom = @($zoomed | Where-Object { $step.zoom -and $_.Payload.mode -eq $step.zoom })
        $toPage = @($inStep | Where-Object { $_.EventType -eq 'web-preferences-sent' -and $fixturePages -contains [int]$_.Payload.pageFrameTreeNodeId })
        $withField = @($toPage | Where-Object { $step.field -and $null -ne $_.Payload.fields.PSObject.Properties[$step.field] })
        $passed = $true
        if ($step.preference) { $passed = $passed -and $mine.Count -ge 1 }
        if ($step.zoom) { $passed = $passed -and $myZoom.Count -ge 1 }
        if ($step.field) { $passed = $passed -and $withField.Count -ge 1 }
        $rows.Add([pscustomobject]@{
            step = $step.step
            passed = $passed
            changeRecords = ($mine | ForEach-Object {
                    "{0} to {1}" -f (Get-JsonText $_.Payload.previous.($step.preference).value), (Get-JsonText $_.Payload.current.($step.preference).value) }) -join '; '
            companionChanges = ($companions | ForEach-Object {
                    "{0} {1} to {2}" -f $companion, (Get-JsonText $_.Payload.previous.$companion.value), (Get-JsonText $_.Payload.current.$companion.value) }) -join '; '
            otherChanges = $otherChanges -join '; '
            zoomRecords = ($zoomed | ForEach-Object { "{0} {1} {2}%" -f $_.Payload.mode, $_.Payload.host, (Get-Percent $_.Payload.zoomPercent) }) -join '; '
            sentToFixture = ($toPage | ForEach-Object {
                    $fields = if ($_.Payload.first) { 'all fields' } else { ($_.Payload.fields.PSObject.Properties | ForEach-Object { "$($_.Name)=$(Get-JsonText $_.Value)" }) -join ' ' }
                    "{0}: {1}" -f $_.Payload.point, $fields }) -join '; '
            expectedField = if ($step.field) { "$($step.field) = $(($withField | ForEach-Object { Get-JsonText $_.Payload.fields.($step.field) }) -join ', ')" } else { '' }
            youSaw = $step.youSaw })
    }
    Write-Results $results $checks $rows
    Say ('A step passes with at least one change record of its preference, or at least one zoom record of its mode, ' +
        'and, where it reaches the page, at least one send to the fixture page holding its field. Each choice made ' +
        'in a menu is a change, so a step can hold several. companionChanges are the changes Chromium makes with ' +
        'the step''s preference; otherChanges are other changes made in the step''s time. ' +
        'youSaw is your answer to whether the fixture page showed the change.')
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

function Complete-Run([string]$out) {
    Say ''
    [void](Read-Host 'Stop the recording in the recorder now, then press Enter here')
    if (-not $SessionFolder) {
        if (-not $SessionId) { $SessionId = Read-Host 'Type or paste the session ID shown by the recorder, or press Enter for the newest recording' }
        $script:SessionFolder = Resolve-Session $SessionId
    }
    Say "Session folder: $SessionFolder"
    if (-not $EventsPath) { $script:EventsPath = Export-Events $SessionFolder $out }
    Invoke-Analysis $out $EventsPath
    Say "Done. Results are in $out"
}

if ($ResultsPath) {
    $script:log = Join-Path $ResultsPath 'analysis-log.txt'
    if (-not $SessionFolder) {
        if (-not $SessionId) { throw 'Give -SessionFolder or -SessionId with -ResultsPath.' }
        $SessionFolder = Resolve-Session $SessionId
    }
    if (-not $EventsPath) { $EventsPath = Export-Events $SessionFolder $ResultsPath }
    Invoke-Analysis $ResultsPath $EventsPath
    return
}

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$out = Join-Path $OutputRoot "browser-preferences-test-$stamp"
New-Item -ItemType Directory -Path $out | Out-Null
$script:log = Join-Path $out 'log.txt'

if ($ProfileFolder) {
    Say "Writing results to $out"
    Say "The recording should use the browser profile folder $ProfileFolder, prepared with its font size set to Large."
    Say "In the browser, load the fixture page: $FixtureUrl"
    Complete-Run $out
    return
}

# --- The changes -------------------------------------------------------------

$records = New-Object System.Collections.Generic.List[object]
function Add-Step($step, [string]$pass, [DateTime]$startUtc, [string]$youSaw) {
    $records.Add([pscustomobject]@{
        step = "$($step.name) $pass"; preference = $step.preference; zoom = $step.zoom; field = $step.field
        startUtc = $startUtc.ToString('o'); endUtc = [DateTime]::UtcNow.ToString('o'); youSaw = $youSaw })
    $records | Export-Csv -LiteralPath (Join-Path $out 'steps.csv') -NoTypeInformation
}

$SpifUpdateAndSend = [uint32]3
$HwndBroadcast = [IntPtr]0xFFFF
$WmSettingChange = [uint32]0x001A
$SmtoAbortIfHung = [uint32]0x0002

# A change the script makes: Windows dark mode by the registry value
# Settings sets and the WM_SETTINGCHANGE it broadcasts, and animation
# effects by SPI_SETCLIENTAREAANIMATION. Each is put back as it was.
function Invoke-CodeStep($step) {
    if ($step.name -eq 'windows dark mode') {
        $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
        $was = [int](Get-ItemProperty -LiteralPath $key -Name AppsUseLightTheme).AppsUseLightTheme
        $set = { param($value)
            Set-ItemProperty -LiteralPath $key -Name AppsUseLightTheme -Value $value -Type DWord
            $result = [IntPtr]::Zero
            [void][BrowserPrefTest.Native]::Broadcast($HwndBroadcast, $WmSettingChange, [IntPtr]::Zero, 'ImmersiveColorSet', $SmtoAbortIfHung, 2000, [ref]$result) }
        $values = @($(if ($was -ne 0) { 0 } else { 1 }), $was)
    }
    else {
        $was = 0
        [void][BrowserPrefTest.Native]::GetInt(0x1042, 0, [ref]$was, 0)
        $set = { param($value) [void][BrowserPrefTest.Native]::SetValue(0x1043, 0, [IntPtr][int]$value, $SpifUpdateAndSend) }
        $values = @($(if ($was -ne 0) { 0 } else { 1 }), $was)
    }
    foreach ($pass in @('change', 'restore')) {
        $startUtc = [DateTime]::UtcNow
        & $set $values[$(if ($pass -eq 'change') { 0 } else { 1 })]
        Start-Sleep -Seconds $StepSeconds
        $saw = if ($pass -eq 'change') { Read-Host "Did the fixture page show the change ($($step.page))? (y/n)" } else { '' }
        Add-Step $step $pass $startUtc $saw
        Say "$($step.name) $pass made"
    }
}

function Invoke-YourStep($step) {
    Say ''
    $startUtc = [DateTime]::UtcNow
    [void](Read-Host "$($step.change) Then press Enter here")
    Start-Sleep -Seconds 1
    $saw = Read-Host "Did the fixture page show the change ($($step.page))? (y/n)"
    Add-Step $step 'change' $startUtc $saw
    $startUtc = [DateTime]::UtcNow
    [void](Read-Host "$($step.restore) Then press Enter here")
    Start-Sleep -Seconds 1
    Add-Step $step 'restore' $startUtc ''
}

Say "Writing results to $out"
Say "The recording should use the starting website $FixtureUrl and an empty browser profile folder."
Say 'Do not change any other setting while the script runs.'
[void](Read-Host ('In Chromium, press Ctrl+N for a new window and go to chrome://settings there. Then click in the ' +
    'fixture page window and press F5, so it is the page the player shows. Then press Enter here'))
foreach ($step in $Planned) {
    if ($step.by -eq 'code') { Invoke-CodeStep $step } else { Invoke-YourStep $step }
}
Complete-Run $out

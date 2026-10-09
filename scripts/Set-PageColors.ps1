# Stores a page colors setting in a prepared browser profile folder, for the
# prepared profile run of Test-BrowserPreferences.ps1. The Chromium the
# recorder uses has no setting that changes page colors while it runs: no
# control in its settings pages, and no policy. The preference
# settings.a11y.requested_page_colors is read from the profile's
# Preferences file when the profile loads (run of 2026-10-08). See
# docs/architecture/accessibility-preferences.md, "Page colors".
#
# Close every Chromium window using the folder first, as Chromium writes
# the file again when it closes. Then run, in an ordinary PowerShell window:
#   powershell -ExecutionPolicy Bypass -File .\Set-PageColors.ps1 -ProfileFolder <folder> -PageColors 2
#
# The numbers, from PageColors in
# chrome/browser/accessibility/page_colors_controller.h: 0 no preference
# (the default: forced colors follow Windows), 1 off (never forced),
# 2 Dusk, 3 Desert, 4 Night Sky, 5 Aquatic, 6 White (each forced, in the
# colors of that Windows contrast theme). Dusk, Night Sky and Aquatic are
# dark, Desert and White light.
#
# The file before the change is kept beside it as Preferences.before-page-colors.

param(
    [Parameter(Mandatory = $true)][string]$ProfileFolder,
    [Parameter(Mandatory = $true)][ValidateRange(0, 6)][int]$PageColors
)

$ErrorActionPreference = 'Stop'
$file = Join-Path $ProfileFolder 'Default\Preferences'
if (-not (Test-Path -LiteralPath $file)) {
    throw "No Preferences file at $file. Start Chromium once with --user-data-dir=`"$ProfileFolder`" and close it."
}
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'chrome.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($ProfileFolder.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) -ge 0 })
if ($running.Count) { throw "Chromium is still running with $ProfileFolder. Close it, then run this again." }

$text = [IO.File]::ReadAllText($file)
$preferences = $text | ConvertFrom-Json
foreach ($name in 'settings', 'a11y') {
    $parent = if ($name -eq 'settings') { $preferences } else { $preferences.settings }
    if ($null -eq $parent.PSObject.Properties[$name]) {
        $parent | Add-Member -NotePropertyName $name -NotePropertyValue ([pscustomobject]@{})
    }
}
$a11y = $preferences.settings.a11y
if ($null -eq $a11y.PSObject.Properties['requested_page_colors']) {
    $a11y | Add-Member -NotePropertyName 'requested_page_colors' -NotePropertyValue $PageColors
}
else {
    $a11y.requested_page_colors = $PageColors
}

Copy-Item -LiteralPath $file -Destination "$file.before-page-colors" -Force
$json = $preferences | ConvertTo-Json -Depth 100 -Compress
[IO.File]::WriteAllText($file, $json, (New-Object Text.UTF8Encoding($false)))
$check = ([IO.File]::ReadAllText($file) | ConvertFrom-Json).settings.a11y.requested_page_colors
Write-Host "settings.a11y.requested_page_colors is now $check in $file"

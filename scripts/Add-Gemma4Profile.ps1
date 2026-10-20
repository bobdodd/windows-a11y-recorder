# Downloads Gemma 4 26B A4B (Unsloth's UD-Q4_K_M build, 16,947,541,728
# bytes) beside the model the recorder uses now, checks its SHA-256, and
# adds the profile "Gemma 4 26B A4B" to the analysis settings, for text only,
# with thinking off and Google's sampling. Run in an ordinary PowerShell with
# the player closed. The download resumes if it is run again.
$ErrorActionPreference = 'Stop'
$settingsPath = Join-Path $env:LOCALAPPDATA 'Windows A11y Recorder\analysis-models.json'
$name = 'Gemma 4 26B A4B'
$file = 'gemma-4-26B-A4B-it-UD-Q4_K_M.gguf'
$url = "https://huggingface.co/unsloth/gemma-4-26B-A4B-it-GGUF/resolve/main/$file"
$sha256 = 'f2c28b3dc4776931ac6f879e11f203dec637ea0f14267a86ec8f6165f63f293f'
$size = 16947541728

$settings = Get-Content -Raw -Encoding UTF8 $settingsPath | ConvertFrom-Json
$source = @($settings.profiles | Where-Object { $_.local -and $_.local.imageAddOnPath }) + @($settings.profiles | Where-Object { $_.local }) | Select-Object -First 1
if (-not $source) { throw "No profile in $settingsPath is served by this PC, so there is none to copy." }
Write-Host "Copying the server settings of the profile ""$($source.name)""."
$folder = Split-Path $source.local.modelPath
$path = Join-Path $folder $file

if ((Test-Path $path) -and (Get-Item $path).Length -eq $size) {
    Write-Host "The file is already downloaded: $path"
} else {
    $drive = Get-PSDrive ((Split-Path -Qualifier $folder).TrimEnd(':'))
    $have = if (Test-Path $path) { (Get-Item $path).Length } else { 0 }
    if ($drive.Free -lt ($size - $have + 1GB)) {
        throw ("The drive {0}: has {1:N1} GB free; the download needs {2:N1} GB more, and 1 GB to spare." -f $drive.Name, ($drive.Free / 1GB), (($size - $have) / 1GB))
    }
    Write-Host "Downloading $file, 16.9 GB, to $folder. This takes a while."
    & curl.exe -L --fail --retry 5 -C - -o $path $url
    if ($LASTEXITCODE -ne 0) { throw "The download stopped (curl exit code $LASTEXITCODE). Run the script again to resume it." }
}

Write-Host 'Checking the SHA-256. This takes a few minutes.'
$hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $sha256) { throw "The file's SHA-256 is $hash, not $sha256. Delete $path and run the script again." }
Write-Host 'The SHA-256 matches.'

$gemma = $source | ConvertTo-Json -Depth 20 | ConvertFrom-Json
$gemma.name = $name
$gemma.model = 'gemma-4-26b-a4b'
$gemma.capabilities = @('text')
$gemma.file = [pscustomobject]@{ name = $file; quantization = 'UD-Q4_K_M'; sha256 = $sha256; path = $path }
$gemma.local.modelPath = $path
$gemma.local | Add-Member -NotePropertyName imageAddOnPath -NotePropertyValue $null -Force
# Gemma 4 26B A4B has 30 layers; all their experts stay in memory, and the
# rest goes to the graphics card.
$arguments = [string]$gemma.local.arguments
if ($arguments -match '(-ncmoe|--n-cpu-moe)\s+\d+') {
    $arguments = $arguments -replace '(-ncmoe|--n-cpu-moe)\s+\d+', '-ncmoe 30'
} else {
    $arguments = ($arguments + ' -ncmoe 30').Trim()
}
$gemma.local.arguments = $arguments
$gemma.generation | Add-Member -NotePropertyName thinking -NotePropertyValue $false -Force
# Google's sampling for Gemma 4 (Unsloth's Gemma 4 guide): top_p 0.95,
# top_k 64. The temperature stays the profile's, 0, as for every step.
$gemma.generation | Add-Member -NotePropertyName sampling -NotePropertyValue ([pscustomobject]@{ top_p = 0.95; top_k = 64 }) -Force
if ($gemma.PSObject.Properties['protectedApiKey']) { $gemma.PSObject.Properties.Remove('protectedApiKey') }

$backup = "$settingsPath.before-gemma4"
Copy-Item $settingsPath $backup -Force
$settings.profiles = @($settings.profiles | Where-Object { $_.name -ne $name }) + @($gemma)
$json = $settings | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText($settingsPath, $json, (New-Object Text.UTF8Encoding $false))
Write-Host "Added the profile ""$name"", with the server arguments: $arguments"
Write-Host "The settings as they were are kept in $backup"

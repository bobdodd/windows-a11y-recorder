#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Windows installer, WindowsA11yRecorder-Setup-<revision>.exe.

.DESCRIPTION
    Runs on the development PC, the only PC with the instrumented Chromium
    build. Every step stops the build at the first failure:

      1. Publishes the player (with the analysis worker beside it) and the
         capture host, self-contained for win-x64, into one folder.
      2. Fetches or reuses the pinned PostgreSQL binaries
         (scripts\Get-PostgresBinaries.ps1); the publish copies them to pgsql.
      3. Copies the run-time files of the instrumented Chromium build to
         browser, using Chromium's own archive list, and checks the copy
         answers the recorder's protocol version query
         (scripts\Test-BrowserProtocolQuery.ps1).
      4. Downloads the pinned llama.cpp server and its CUDA run time, checks
         their SHA-256, unpacks them to llama, and runs llama-server --version.
      5. Downloads Microsoft's Visual C++ x64 redistributable and checks it is
         signed by Microsoft.
      6. Writes THIRD-PARTY-NOTICES.txt.
      7. Compiles installer\WindowsA11yRecorder.iss with Inno Setup and writes
         the installer's SHA-256 beside it.

    Downloads are kept in artifacts\installer\downloads, so a second build
    does not download them again. The installer is written to
    artifacts\installer\output.

    See docs\architecture\windows-installer.md.
#>

[CmdletBinding()]
param(
    [string] $ChromiumSource = (Join-Path $env:USERPROFILE "chromium-dev\chromium\src"),
    [string] $ChromiumBuildFolder = "out\A11yRecorder",
    [string] $InnoSetupCompiler = "",
    [string] $WorkFolder = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $WorkFolder) {
    $WorkFolder = Join-Path $repositoryRoot "artifacts\installer"
}
$downloads = Join-Path $WorkFolder "downloads"
$staging = Join-Path $WorkFolder "staging"
$app = Join-Path $staging "app"
$output = Join-Path $WorkFolder "output"

# The llama.cpp build every model trial so far used, from its GitHub release,
# published 2026-10-14. The sizes and SHA-256 values were taken from the
# release's files on 2026-11-06.
$llamaBuild = "b11607"
$llamaDownloads = @(
    @{
        Name   = "llama-b11607-bin-win-cuda-12.4-x64.zip"
        Url    = "https://github.com/ggml-org/llama.cpp/releases/download/b11607/llama-b11607-bin-win-cuda-12.4-x64.zip"
        Size   = 265226865
        Sha256 = "4d539aaae303b6302340c6a5837b8b2de7a15d03c4c550202e41228e907860b3"
    },
    @{
        Name   = "cudart-llama-bin-win-cuda-12.4-x64.zip"
        Url    = "https://github.com/ggml-org/llama.cpp/releases/download/b11607/cudart-llama-bin-win-cuda-12.4-x64.zip"
        Size   = 391443627
        Sha256 = "8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6"
    }
)
$llamaLicenseUrl = "https://raw.githubusercontent.com/ggml-org/llama.cpp/$llamaBuild/LICENSE"

# Microsoft's permanent address for the latest Visual C++ 2015-2022 x64
# redistributable. It changes as Microsoft updates the run time, so it is
# checked by its signature rather than a fixed hash.
$vcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe"

# Files Chromium's archive list names that the recorder does not run.
$chromiumExcluded = @("setup.exe", "interactive_ui_tests.exe")

# Without these the browser cannot start, so a build missing one is refused.
# Other listed files are optional in some build configurations and are only
# reported when absent.
$chromiumRequired = @(
    "chrome.exe", "chrome.dll", "chrome_elf.dll", "icudtl.dat",
    "resources.pak", "chrome_100_percent.pak", "v8_context_snapshot.bin")

function Write-Step {
    param([string] $Text)
    Write-Host ""
    Write-Host "== $Text"
}

# Runs a program and returns its exit code and output. Windows PowerShell
# 5.1 turns a native program's standard error into errors, which stop the
# script under ErrorActionPreference Stop, so the process is run directly.
function Invoke-Program {
    param(
        [Parameter(Mandatory = $true)] [string] $FilePath,
        [string[]] $Arguments = @(),
        [string] $WorkingDirectory = (Get-Location).Path
    )

    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $FilePath
    $info.Arguments = ($Arguments | ForEach-Object {
            if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
        }) -join " "
    $info.WorkingDirectory = $WorkingDirectory
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::Start($info)
    $standardOutput = $process.StandardOutput.ReadToEndAsync()
    $standardError = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output   = $standardOutput.Result + $standardError.Result
    }
}

function Get-Download {
    param(
        [Parameter(Mandatory = $true)] [string] $Url,
        [Parameter(Mandatory = $true)] [string] $Name,
        [long] $Size = 0,
        [string] $Sha256 = ""
    )

    $path = Join-Path $downloads $Name
    if ((Test-Path -LiteralPath $path) -and $Sha256) {
        $existing = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($existing -eq $Sha256) {
            Write-Host "Already downloaded: $Name"
            return $path
        }
        Remove-Item -LiteralPath $path -Force
    }
    elseif (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }

    Write-Host "Downloading $Url"
    $partial = "$path.partial"
    $previousProgress = $ProgressPreference
    $ProgressPreference = "SilentlyContinue"
    try {
        Invoke-WebRequest -Uri $Url -OutFile $partial -UseBasicParsing
    }
    finally {
        $ProgressPreference = $previousProgress
    }

    if ($Size -gt 0) {
        $actualSize = (Get-Item -LiteralPath $partial).Length
        if ($actualSize -ne $Size) {
            Remove-Item -LiteralPath $partial -Force
            throw "$Name is $actualSize bytes, not $Size."
        }
    }
    if ($Sha256) {
        $actual = (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $Sha256) {
            Remove-Item -LiteralPath $partial -Force
            throw "The SHA-256 of $Name is $actual, not $Sha256."
        }
    }
    Move-Item -LiteralPath $partial -Destination $path
    return $path
}

function Find-DotNet {
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    $local = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $local) {
        return $local
    }
    throw ("The .NET SDK was not found in PATH or at $local. Install the " +
        ".NET 10 SDK, then run this script again.")
}

# Every ISCC.exe that could be Inno Setup's: one given with
# -InnoSetupCompiler, one in PATH, the install folder Windows records for
# each Inno Setup installation (machine-wide or for this user, which is
# where winget's per-user installs go), and the default folders.
function Get-InnoSetupCandidates {
    $paths = New-Object System.Collections.Generic.List[string]
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        $paths.Add($command.Source)
    }
    $uninstallKeys = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*")
    foreach ($key in $uninstallKeys) {
        foreach ($entry in Get-ItemProperty -Path $key -ErrorAction SilentlyContinue) {
            $name = $entry.PSObject.Properties["DisplayName"]
            $location = $entry.PSObject.Properties["InstallLocation"]
            if ($name -and $location -and $name.Value -like "Inno Setup*" -and $location.Value) {
                $paths.Add((Join-Path $location.Value "ISCC.exe"))
            }
        }
    }
    foreach ($folder in @(
            (Join-Path $env:LOCALAPPDATA "Programs"),
            ${env:ProgramFiles},
            ${env:ProgramFiles(x86)})) {
        if ($folder -and (Test-Path -LiteralPath $folder)) {
            foreach ($found in Get-ChildItem -LiteralPath $folder -Directory -Filter "Inno Setup*" -ErrorAction SilentlyContinue) {
                $paths.Add((Join-Path $found.FullName "ISCC.exe"))
            }
        }
    }
    return $paths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -Unique
}

function Get-FileMajorVersion {
    param([string] $Path)
    $version = (Get-Item -LiteralPath $Path).VersionInfo
    if ($version.FileMajorPart) {
        return $version.FileMajorPart
    }
    return 0
}

# The setup script uses SetupArchitecture, which Inno Setup 7 added, so an
# older Inno Setup cannot compile it.
function Find-InnoSetup {
    if ($InnoSetupCompiler) {
        if (-not (Test-Path -LiteralPath $InnoSetupCompiler)) {
            throw "Inno Setup's compiler was not found at $InnoSetupCompiler."
        }
        $candidates = @($InnoSetupCompiler)
    }
    else {
        $candidates = @(Get-InnoSetupCandidates)
    }

    $older = New-Object System.Collections.Generic.List[string]
    foreach ($candidate in $candidates) {
        $major = Get-FileMajorVersion -Path $candidate
        if ($major -ge 7) {
            return $candidate
        }
        $older.Add("$candidate (version $major)")
    }

    $install = "winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements"
    if ($older.Count -gt 0) {
        throw ("Inno Setup 7 or later is needed, and only an older version was found: " +
            ($older -join ", ") + ". Install Inno Setup 7 from https://jrsoftware.org/isdl.php, " +
            "or with: $install   then run this script again.")
    }
    throw ("Inno Setup was not found in PATH, in Windows' list of installed programs, " +
        "or in Program Files. Install it with: $install   then run this script again. " +
        "If it is installed in another folder, run this script with " +
        "-InnoSetupCompiler followed by the full path of its ISCC.exe.")
}

function Get-Revision {
    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($git -and (Test-Path -LiteralPath (Join-Path $repositoryRoot ".git"))) {
        $result = Invoke-Program -FilePath $git.Source `
            -Arguments @("-C", $repositoryRoot, "rev-parse", "--short=7", "HEAD")
        if ($result.ExitCode -ne 0) {
            throw "git could not read the revision: $($result.Output)"
        }
        $revision = $result.Output.Trim()
        $status = Invoke-Program -FilePath $git.Source `
            -Arguments @("-C", $repositoryRoot, "status", "--porcelain", "--untracked-files=no")
        if ($status.Output.Trim()) {
            Write-Warning ("The checkout has changes that are not committed. The " +
                "installer is named after $revision with -modified added.")
            $revision = "$revision-modified"
        }
        return $revision
    }
    $file = Join-Path $repositoryRoot "revision.txt"
    if (Test-Path -LiteralPath $file) {
        return (Get-Content -LiteralPath $file -Raw).Trim()
    }
    throw "The revision could not be read from git or from revision.txt."
}

function Get-ProductVersion {
    $props = [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot "Directory.Build.props") -Raw)
    $version = $props.Project.PropertyGroup.VersionPrefix
    if (-not $version) {
        throw "Directory.Build.props has no VersionPrefix."
    }
    return $version.Trim()
}

# Publishes a project into its own folder and merges it into the app folder.
# A file both programs ship must be identical, or one program would run with
# the other's copy.
function Publish-Into {
    param(
        [Parameter(Mandatory = $true)] [string] $DotNet,
        [Parameter(Mandatory = $true)] [string] $Project,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    $folder = Join-Path $staging "publish\$Name"
    $result = Invoke-Program -FilePath $DotNet -WorkingDirectory $repositoryRoot -Arguments @(
        "publish", $Project, "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-o", $folder, "-nologo")
    if ($result.ExitCode -ne 0) {
        Write-Host $result.Output
        throw "dotnet publish failed for $Project."
    }

    $conflicts = New-Object System.Collections.Generic.List[string]
    foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File) {
        $relative = $file.FullName.Substring($folder.Length + 1)
        $target = Join-Path $app $relative
        if (Test-Path -LiteralPath $target) {
            $left = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            $right = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
            if ($left -ne $right) {
                $conflicts.Add($relative)
            }
            continue
        }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    if ($conflicts.Count -gt 0) {
        throw ("$Name ships different copies of files the other programs " +
            "also ship: " + ($conflicts -join ", "))
    }
}

function Copy-Chromium {
    $buildFolder = Join-Path $ChromiumSource $ChromiumBuildFolder
    $listFile = Join-Path $ChromiumSource "infra\archive_config\win-archive-rel.json"
    if (-not (Test-Path -LiteralPath (Join-Path $buildFolder "chrome.exe"))) {
        throw "The instrumented Chromium build was not found at $buildFolder."
    }
    if (-not (Test-Path -LiteralPath $listFile)) {
        throw "Chromium's archive list was not found at $listFile."
    }

    # The first archive is the browser itself; the others are symbols and
    # tools.
    $list = (Get-Content -LiteralPath $listFile -Raw | ConvertFrom-Json).archive_datas[0]
    $browser = Join-Path $app "browser"
    New-Item -ItemType Directory -Force -Path $browser | Out-Null

    $copied = 0
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($name in $list.files) {
        if ($chromiumExcluded -contains $name) {
            continue
        }
        $source = Join-Path $buildFolder $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            if ($chromiumRequired -contains $name) {
                throw "The Chromium build has no $name, which the browser needs to start."
            }
            $missing.Add($name)
            continue
        }
        $target = Join-Path $browser $name
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
        $copied++
    }
    foreach ($pattern in $list.file_globs) {
        $folder = Split-Path -Parent $pattern
        $leaf = Split-Path -Leaf $pattern
        $sourceFolder = if ($folder) { Join-Path $buildFolder $folder } else { $buildFolder }
        if (-not (Test-Path -LiteralPath $sourceFolder)) {
            continue
        }
        foreach ($file in Get-ChildItem -LiteralPath $sourceFolder -Filter $leaf -File) {
            $targetFolder = if ($folder) { Join-Path $browser $folder } else { $browser }
            New-Item -ItemType Directory -Force -Path $targetFolder | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $targetFolder $file.Name)
            $copied++
        }
    }
    foreach ($name in $chromiumRequired) {
        if (-not (Test-Path -LiteralPath (Join-Path $browser $name))) {
            throw "The copied browser has no $name."
        }
    }
    if (-not (Get-ChildItem -LiteralPath (Join-Path $browser "locales") -Filter "*.pak" -ErrorAction SilentlyContinue)) {
        throw "The copied browser has no locales\*.pak files."
    }
    Write-Host "Copied $copied Chromium files from $buildFolder."
    if ($missing.Count -gt 0) {
        Write-Host ("Not in this build, and not needed to start it: " + ($missing -join ", "))
    }
}

function Add-Llama {
    $llama = Join-Path $app "llama"
    New-Item -ItemType Directory -Force -Path $llama | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($item in $llamaDownloads) {
        $archive = Get-Download -Url $item.Url -Name $item.Name -Size $item.Size -Sha256 $item.Sha256
        $zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
        try {
            foreach ($entry in $zip.Entries) {
                if ($entry.FullName.EndsWith("/")) {
                    continue
                }
                $target = Join-Path $llama ($entry.FullName.Replace("/", "\"))
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            }
        }
        finally {
            $zip.Dispose()
        }
    }

    $server = Join-Path $llama "llama-server.exe"
    if (-not (Test-Path -LiteralPath $server)) {
        throw "The llama.cpp archive did not contain llama-server.exe at its top level."
    }
    $result = Invoke-Program -FilePath $server -Arguments @("--version") -WorkingDirectory $llama
    if ($result.ExitCode -ne 0 -or $result.Output -notmatch "version") {
        Write-Host $result.Output
        throw "llama-server.exe --version failed with exit code $($result.ExitCode)."
    }
    $versionLine = ($result.Output -split "`n" | Where-Object { $_ -match "version" } | Select-Object -First 1).Trim()
    Write-Host "Bundled llama.cpp server: $versionLine"
    Set-Content -LiteralPath (Join-Path $llama "llama-cpp-build.txt") -Encoding UTF8 -Value @(
        "llama.cpp $llamaBuild, Windows x64, CUDA 12.4",
        $versionLine,
        ($llamaDownloads | ForEach-Object { "$($_.Name) sha256 $($_.Sha256)" }))
}

function Get-VcRedist {
    $path = Get-Download -Url $vcRedistUrl -Name "vc_redist.x64.exe"
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne "Valid" -or
        $signature.SignerCertificate.Subject -notmatch "O=Microsoft Corporation") {
        Remove-Item -LiteralPath $path -Force
        throw ("vc_redist.x64.exe is not validly signed by Microsoft " +
            "(status $($signature.Status)).")
    }
    $redistVersion = (Get-Item -LiteralPath $path).VersionInfo.ProductVersion
    Write-Host "Visual C++ redistributable $redistVersion, signed by Microsoft."
    Copy-Item -LiteralPath $path -Destination (Join-Path $staging "vc_redist.x64.exe") -Force
}

# The NuGet packages the published programs use, with the licence each
# package declares in its .nuspec.
function Get-PackageNotices {
    $packagesRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
    $seen = @{}
    $lines = New-Object System.Collections.Generic.List[string]
    $assetsFiles = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "src") -Recurse -Filter "project.assets.json" -File
    foreach ($assets in $assetsFiles) {
        $json = Get-Content -LiteralPath $assets.FullName -Raw | ConvertFrom-Json
        foreach ($property in $json.libraries.PSObject.Properties) {
            if ($property.Value.type -ne "package" -or $seen.ContainsKey($property.Name)) {
                continue
            }
            $seen[$property.Name] = $true
            $id, $packageVersion = $property.Name.Split("/")
            $licence = "see the package"
            $nuspec = Join-Path $packagesRoot ("{0}\{1}\{0}.nuspec" -f $id.ToLowerInvariant(), $packageVersion.ToLowerInvariant())
            if (Test-Path -LiteralPath $nuspec) {
                $metadata = ([xml](Get-Content -LiteralPath $nuspec -Raw)).package.metadata
                if ($metadata.PSObject.Properties["license"] -and $metadata.license) {
                    if ($metadata.license -is [string]) {
                        $licence = $metadata.license
                    }
                    elseif ($metadata.license.type -eq "file") {
                        $licence = "the package's " + $metadata.license.'#text'
                    }
                    else {
                        $licence = $metadata.license.'#text'
                    }
                }
                elseif ($metadata.PSObject.Properties["licenseUrl"] -and $metadata.licenseUrl) {
                    $licence = $metadata.licenseUrl
                }
            }
            $lines.Add("  $id $packageVersion, licence: $licence, https://www.nuget.org/packages/$id/$packageVersion")
        }
    }
    return ($lines | Sort-Object)
}

function Write-Notices {
    $licenseText = Join-Path $downloads "llama.cpp-$llamaBuild-LICENSE.txt"
    if (-not (Test-Path -LiteralPath $licenseText)) {
        Get-Download -Url $llamaLicenseUrl -Name "llama.cpp-$llamaBuild-LICENSE.txt" | Out-Null
    }
    Copy-Item -LiteralPath $licenseText -Destination (Join-Path $app "llama\LICENSE-llama.cpp.txt") -Force

    $chromiumLicense = Join-Path $ChromiumSource "LICENSE"
    if (-not (Test-Path -LiteralPath $chromiumLicense)) {
        throw "Chromium's LICENSE was not found at $chromiumLicense."
    }
    Copy-Item -LiteralPath $chromiumLicense -Destination (Join-Path $app "browser\LICENSE-Chromium.txt") -Force

    $runtimeNotices = Get-ChildItem -LiteralPath (Join-Path $staging "publish") -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "THIRD-PARTY-NOTICES.TXT" } | Select-Object -First 1

    $text = New-Object System.Collections.Generic.List[string]
    $text.Add("Windows A11y Recorder: third-party software")
    $text.Add("")
    $text.Add("This installation includes the following software from other authors.")
    $text.Add("Each is used under its own licence, whose text is in the folder named.")
    $text.Add("")
    $text.Add("PostgreSQL (folder pgsql)")
    $text.Add("  The PostgreSQL Licence. pgsql\server_license.txt, and the licences of the")
    $text.Add("  libraries it uses in pgsql\commandlinetools_3rd_party_licenses.txt.")
    $text.Add("  https://www.postgresql.org/about/licence/")
    $text.Add("")
    $text.Add("Chromium (folder browser), built with the recorder's changes in chromium\ of")
    $text.Add("  https://github.com/bobdodd/windows-a11y-recorder")
    $text.Add("  BSD-style licence: browser\LICENSE-Chromium.txt. The licences of the")
    $text.Add("  libraries compiled into Chromium are shown by the browser at chrome://credits.")
    $text.Add("")
    $text.Add("llama.cpp $llamaBuild (folder llama)")
    $text.Add("  MIT licence: llama\LICENSE-llama.cpp.txt. https://github.com/ggml-org/llama.cpp")
    $text.Add("")
    $text.Add("NVIDIA CUDA run time 12.4 (folder llama: cudart64_12.dll, cublas64_12.dll,")
    $text.Add("  cublasLt64_12.dll), as distributed by llama.cpp beside its CUDA build.")
    $text.Add("  Redistributed under the NVIDIA CUDA Toolkit End User License Agreement,")
    $text.Add("  https://docs.nvidia.com/cuda/eula/index.html")
    $text.Add("")
    $text.Add("Microsoft .NET run time (the main folder)")
    $text.Add("  MIT licence. https://github.com/dotnet/runtime/blob/main/LICENSE.TXT")
    if ($runtimeNotices) {
        Copy-Item -LiteralPath $runtimeNotices.FullName -Destination (Join-Path $app "THIRD-PARTY-NOTICES-dotnet.txt") -Force
        $text.Add("  Notices for the components it includes: THIRD-PARTY-NOTICES-dotnet.txt.")
    }
    $text.Add("")
    $text.Add("Microsoft Visual C++ 2015-2022 x64 redistributable")
    $text.Add("  Installed by the installer when missing, under Microsoft's licence terms,")
    $text.Add("  which it shows. https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist")
    $text.Add("")
    $text.Add("NuGet packages used by the recorder's programs:")
    foreach ($line in Get-PackageNotices) {
        $text.Add($line)
    }
    $text.Add("")
    $text.Add("Model files are not part of this installation. Each model you download")
    $text.Add("has its own licence, shown on its Hugging Face page.")
    Set-Content -LiteralPath (Join-Path $app "THIRD-PARTY-NOTICES.txt") -Encoding UTF8 -Value $text
}

# --- Build ---

$dotnet = Find-DotNet
$iscc = Find-InnoSetup
$revision = Get-Revision
$version = Get-ProductVersion
Write-Host "Building the installer for $version, revision $revision."
Write-Host ".NET SDK: $dotnet"
Write-Host "Inno Setup: $iscc"

New-Item -ItemType Directory -Force -Path $downloads, $output | Out-Null
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $app | Out-Null

Write-Step "PostgreSQL"
& (Join-Path $PSScriptRoot "Get-PostgresBinaries.ps1")

Write-Step "Publishing the programs"
Publish-Into -DotNet $dotnet -Project "src\Recorder.App\Recorder.App.csproj" -Name "Recorder.App"
Publish-Into -DotNet $dotnet -Project "src\Recorder.CaptureHost\Recorder.CaptureHost.csproj" -Name "Recorder.CaptureHost"
foreach ($required in @("Recorder.App.exe", "Recorder.AnalysisWorker.exe", "Recorder.CaptureHost.exe",
        "model-catalog.json", "pgsql\bin\postgres.exe")) {
    if (-not (Test-Path -LiteralPath (Join-Path $app $required))) {
        throw "The published folder has no $required."
    }
}
Set-Content -LiteralPath (Join-Path $app "revision.txt") -Value $revision -Encoding ASCII

Write-Step "Instrumented Chromium"
Copy-Chromium
$query = Join-Path $PSScriptRoot "Test-BrowserProtocolQuery.ps1"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $query `
    -ChromiumExecutable (Join-Path $app "browser\chrome.exe") `
    -OutputRoot (Join-Path $WorkFolder "protocol-query")
if ($LASTEXITCODE -ne 0) {
    throw "The copied browser did not pass the protocol version query."
}

Write-Step "llama.cpp server"
Add-Llama

Write-Step "Visual C++ run time"
Get-VcRedist

Write-Step "Licence notices"
Write-Notices

Write-Step "Compiling the installer"
$numericVersion = ($version -split "[^0-9.]")[0]
$result = Invoke-Program -FilePath $iscc -WorkingDirectory $repositoryRoot -Arguments @(
    "/Q",
    "/DAppVersion=$version",
    "/DNumericVersion=$numericVersion",
    "/DRevision=$revision",
    "/DStagingDir=$staging",
    "/DOutputDir=$output",
    (Join-Path $repositoryRoot "installer\WindowsA11yRecorder.iss"))
if ($result.ExitCode -ne 0) {
    Write-Host $result.Output
    throw "Inno Setup failed with exit code $($result.ExitCode)."
}

$setup = Join-Path $output "WindowsA11yRecorder-Setup-$revision.exe"
if (-not (Test-Path -LiteralPath $setup)) {
    throw "Inno Setup finished but $setup was not written."
}
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$setup.sha256" -Encoding ASCII -Value "$hash *$(Split-Path -Leaf $setup)"
$sizeMb = [math]::Round((Get-Item -LiteralPath $setup).Length / 1MB)

Write-Host ""
Write-Host "INSTALLER BUILT"
Write-Host "  $setup ($sizeMb MB)"
Write-Host "  SHA-256 $hash"

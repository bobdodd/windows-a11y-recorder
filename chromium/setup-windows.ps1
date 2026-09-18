param(
    [string]$Root = "$env:USERPROFILE\chromium-dev",
    [string]$RecorderRepository = (Split-Path $PSScriptRoot -Parent),
    [switch]$SkipVisualStudio
)

$ErrorActionPreference = "Stop"
$depotTools = Join-Path $Root "depot_tools"
$chromium = Join-Path $Root "chromium"
$chromiumSrc = Join-Path $chromium "src"
$installer = Join-Path $env:TEMP "vs_buildtools.exe"
$vsWhere = Join-Path ${env:ProgramFiles(x86)} `
    "Microsoft Visual Studio\Installer\vswhere.exe"

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Force -Path $Root | Out-Null

if (-not $SkipVisualStudio) {
    Invoke-WebRequest `
        -Uri "https://aka.ms/vs/stable/vs_buildtools.exe" `
        -OutFile $installer
    $process = Start-Process -FilePath $installer -Wait -PassThru `
        -ArgumentList @(
            "--quiet",
            "--wait",
            "--norestart",
            "--nocache",
            "--add", "Microsoft.VisualStudio.Workload.VCTools",
            "--add", "Microsoft.VisualStudio.Component.VC.ATLMFC",
            "--add", "Microsoft.VisualStudio.Component.Windows11SDK.28000",
            "--includeRecommended"
        )
    if ($process.ExitCode -notin 0, 3010) {
        throw "Visual Studio Build Tools failed with exit code $($process.ExitCode)."
    }
}

if (-not (Test-Path $vsWhere)) {
    throw "Visual Studio Installer did not install vswhere.exe."
}
$visualStudio = & $vsWhere `
    -latest `
    -products Microsoft.VisualStudio.Product.BuildTools `
    -requires Microsoft.VisualStudio.Workload.VCTools `
    -requires Microsoft.VisualStudio.Component.VC.ATLMFC `
    -requires Microsoft.VisualStudio.Component.Windows11SDK.28000 `
    -property installationPath
if (-not $visualStudio) {
    throw "Visual Studio Build Tools is missing the C++ workload, ATL/MFC, or Windows 11 SDK 28000."
}
$env:vs2026_install = $visualStudio
[Environment]::SetEnvironmentVariable(
    "vs2026_install",
    $visualStudio,
    "User"
)

if (-not (Test-Path (Join-Path $depotTools ".git"))) {
    Invoke-Native git clone `
        https://chromium.googlesource.com/chromium/tools/depot_tools.git `
        $depotTools
}

$env:PATH = "$depotTools;$env:PATH"
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
[Environment]::SetEnvironmentVariable(
    "DEPOT_TOOLS_WIN_TOOLCHAIN",
    "0",
    "User"
)

Invoke-Native git config --global core.autocrlf false
Invoke-Native git config --global core.filemode false
Invoke-Native git config --global core.preloadindex true
Invoke-Native git config --global core.fscache true
Invoke-Native git config --global branch.autosetuprebase always
Invoke-Native git config --global core.longpaths true

# Invoke-Native (Join-Path $depotTools "gclient.bat")
if (-not (Test-Path $chromiumSrc)) {
    New-Item -ItemType Directory -Force -Path $chromium | Out-Null
    Push-Location $chromium
    try {
        Invoke-Native (Join-Path $depotTools "fetch.bat") chromium
    }
    finally {
        Pop-Location
    }
}

Push-Location $chromiumSrc
try {
    Invoke-Native (Join-Path $depotTools "gclient.bat") sync --jobs 1
    Invoke-Native (Join-Path $depotTools "python3.bat") `
        (Join-Path $RecorderRepository "chromium\integrate.py") `
        $chromiumSrc
    Invoke-Native (Join-Path $depotTools "gn.bat") `
        gen `
        out\A11yRecorder `
        "--args=is_debug=false is_component_build=false symbol_level=1"
    Invoke-Native (Join-Path $depotTools "autoninja.bat") `
        -C `
        out\A11yRecorder `
        chrome
}
finally {
    Pop-Location
}

Write-Output "Instrumented Chromium: $chromiumSrc\out\A11yRecorder\chrome.exe"

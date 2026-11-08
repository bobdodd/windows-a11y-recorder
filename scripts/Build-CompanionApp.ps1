#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the companion app, a11yrecorder-companion.apk, that the recorder
    installs on an Android phone.

.DESCRIPTION
    The app is built straight from Google's Android SDK tools, without
    Gradle, so every file the build downloads is pinned and checked by its
    SHA-256:

      1. Downloads, or reuses, a pinned Eclipse Temurin JDK 21, the Android
         SDK Build-Tools 37.0.0 and the Android 17 (API 37) SDK platform, and
         unpacks them into the work folder.
      2. Compiles the resources with aapt2 and links them with the manifest.
      3. Compiles the Java source with javac against the platform's
         android.jar, and converts it to Android byte code with d8.
      4. Adds the byte code to the app, aligns it with zipalign and signs it
         with apksigner.

    The Android SDK tools are used under the Android Software Development Kit
    License Agreement. The first build asks you to accept it, and records
    your answer in the work folder. -AcceptAndroidSdkLicense accepts it
    without asking.

    The app is signed with a key made on the first build and kept outside
    the repository, in the signing folder. Android only lets an app be
    updated by one signed with the same key, so keep that folder: a new key
    means removing the app from the phone before the next install.

    Runs on Windows PowerShell 5.1 and on PowerShell 7 on Windows or Linux.

    See docs\architecture\android-implementation-plan.md, part 0.
#>

[CmdletBinding()]
param(
    [string] $WorkFolder = "",
    [string] $DownloadFolder = "",
    [string] $SigningFolder = "",
    [switch] $AcceptAndroidSdkLicense
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

[Net.ServicePointManager]::SecurityProtocol =
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path (Join-Path $repositoryRoot "android") "companion"
if (-not $WorkFolder) {
    $WorkFolder = Join-Path (Join-Path $repositoryRoot "artifacts") "companion"
}
if (-not $DownloadFolder) {
    $DownloadFolder = Join-Path $WorkFolder "downloads"
}
if (-not $SigningFolder) {
    $localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $SigningFolder = Join-Path (Join-Path $localData "Windows A11y Recorder Build") "companion-signing"
}

$onWindows = [IO.Path]::DirectorySeparatorChar -eq '\'
$exe = if ($onWindows) { ".exe" } else { "" }

$packageName = "io.github.bobdodd.a11yrecorder.companion"
$apkName = "a11yrecorder-companion.apk"
$minimumSdk = 30
$targetSdk = 37
$sdkLicenseUrl = "https://developer.android.com/studio/terms"

# The pinned downloads. Google's files and their SHA-1 values are listed in
# https://dl.google.com/android/repository/repository2-3.xml; the JDK's in
# the Adoptium API. The SHA-256 values were computed from the files on
# 2026-11-08.
$jdk = if ($onWindows) {
    @{
        Folder = "jdk"
        Name   = "OpenJDK21U-jdk_x64_windows_hotspot_21.0.13_9.zip"
        Url    = "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.13%2B9/OpenJDK21U-jdk_x64_windows_hotspot_21.0.13_9.zip"
        Size   = 205421281
        Sha256 = "a4dbaa05fca4bd89d84584e58c459bd732efebb1722af80ab13c6516832004f9"
    }
}
else {
    @{
        Folder = "jdk"
        Name   = "OpenJDK21U-jdk_x64_linux_hotspot_21.0.13_9.tar.gz"
        Url    = "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.13%2B9/OpenJDK21U-jdk_x64_linux_hotspot_21.0.13_9.tar.gz"
        Size   = 207858352
        Sha256 = "d7f5d5cf865a2902b70ff549b97586a7d3f234e016f041ab447ea740c7d66d1c"
    }
}
$buildTools = if ($onWindows) {
    @{
        Folder = "build-tools"
        Name   = "build-tools_r37_windows.zip"
        Url    = "https://dl.google.com/android/repository/build-tools_r37_windows.zip"
        Size   = 61077164
        Sha256 = "68075aa319ed8a01cf1a565ed1e61a3c1a801dd49191c35851248dc293c33b1a"
    }
}
else {
    @{
        Folder = "build-tools"
        Name   = "build-tools_r37_linux.zip"
        Url    = "https://dl.google.com/android/repository/build-tools_r37_linux.zip"
        Size   = 66135704
        Sha256 = "01af179347cbcd9c208b7f8171f7b21f6dd1d2f85bcd15e88caa51d5d7b86060"
    }
}
$platform = @{
    Folder = "platform"
    Name   = "platform-37.0_r02.zip"
    Url    = "https://dl.google.com/android/repository/platform-37.0_r02.zip"
    Size   = 67281901
    Sha256 = "840b23e827f96e64aea4c89a1194aac3dc5f6bad37edb231c5c795d890330e8d"
}

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

# Runs a program and stops the build if it fails.
function Invoke-Tool {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [string] $FilePath,
        [string[]] $Arguments = @()
    )

    $result = Invoke-Program -FilePath $FilePath -Arguments $Arguments -WorkingDirectory $WorkFolder
    if ($result.ExitCode -ne 0) {
        Write-Host $result.Output
        throw "$Name failed with exit code $($result.ExitCode)."
    }
    return $result.Output
}

function Get-Sha256 {
    param([string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Download {
    param([Parameter(Mandatory = $true)] [hashtable] $Item)

    $path = Join-Path $DownloadFolder $Item.Name
    if (Test-Path -LiteralPath $path) {
        if ((Get-Sha256 $path) -eq $Item.Sha256) {
            Write-Host "Already downloaded: $($Item.Name)"
            return $path
        }
        Remove-Item -LiteralPath $path -Force
    }

    Write-Host "Downloading $($Item.Url)"
    $partial = "$path.partial"
    $previousProgress = $ProgressPreference
    $ProgressPreference = "SilentlyContinue"
    try {
        Invoke-WebRequest -Uri $Item.Url -OutFile $partial -UseBasicParsing
    }
    finally {
        $ProgressPreference = $previousProgress
    }
    $actualSize = (Get-Item -LiteralPath $partial).Length
    if ($actualSize -ne $Item.Size) {
        Remove-Item -LiteralPath $partial -Force
        throw "$($Item.Name) is $actualSize bytes, not $($Item.Size)."
    }
    $actual = Get-Sha256 $partial
    if ($actual -ne $Item.Sha256) {
        Remove-Item -LiteralPath $partial -Force
        throw "The SHA-256 of $($Item.Name) is $actual, not $($Item.Sha256)."
    }
    Move-Item -LiteralPath $partial -Destination $path
    return $path
}

# Unpacks an archive into its own folder under tools, once. A marker file
# names the archive's SHA-256, so a changed pin unpacks the new archive.
function Expand-Tool {
    param(
        [Parameter(Mandatory = $true)] [hashtable] $Item,
        [Parameter(Mandatory = $true)] [string] $Archive
    )

    # Short folder names keep the JDK's deepest files well inside Windows'
    # 260-character path limit.
    $folder = Join-Path (Join-Path $WorkFolder "tools") $Item.Folder
    $marker = Join-Path $folder ".unpacked-sha256"
    if ((Test-Path -LiteralPath $marker) -and
        ((Get-Content -LiteralPath $marker -Raw).Trim() -eq $Item.Sha256)) {
        return $folder
    }
    if (Test-Path -LiteralPath $folder) {
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    Write-Host "Unpacking $($Item.Name)"
    if ($Item.Name.EndsWith(".tar.gz")) {
        Invoke-Tool -Name "tar" -FilePath "tar" -Arguments @("-xzf", $Archive, "-C", $folder) | Out-Null
    }
    else {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($Archive, $folder)
    }
    Set-Content -LiteralPath $marker -Value $Item.Sha256 -Encoding ASCII
    return $folder
}

# The single folder an archive unpacks to, such as jdk-21.0.13+9.
function Get-OnlyFolder {
    param([string] $Path)
    $folders = @(Get-ChildItem -LiteralPath $Path -Directory)
    if ($folders.Count -ne 1) {
        throw "Expected one folder in $Path, found $($folders.Count)."
    }
    return $folders[0].FullName
}

function Confirm-SdkLicense {
    $record = Join-Path $WorkFolder "android-sdk-license-accepted.txt"
    if (Test-Path -LiteralPath $record) {
        return
    }
    if (-not $AcceptAndroidSdkLicense) {
        Write-Host ""
        Write-Host "The companion app is built with Google's Android SDK Build-Tools and SDK"
        Write-Host "platform, which are used under the Android Software Development Kit License"
        Write-Host "Agreement: $sdkLicenseUrl"
        $answer = Read-Host "Type yes to accept it and continue"
        if ($answer -ne "yes") {
            throw "The Android SDK licence was not accepted, so the companion app was not built."
        }
    }
    Set-Content -LiteralPath $record -Encoding UTF8 -Value @(
        "Android Software Development Kit License Agreement, $sdkLicenseUrl",
        "Accepted by $([Environment]::UserName) on $([DateTime]::UtcNow.ToString('u'))")
}

function Get-ProductVersion {
    $props = [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot "Directory.Build.props") -Raw)
    return $props.Project.PropertyGroup.VersionPrefix.Trim()
}

# Android refuses to replace an app with one of a lower version code, so the
# code is the number of commits on the current branch, which only grows.
function Get-VersionCode {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($git) {
        $result = Invoke-Program -FilePath $git.Source -Arguments @("rev-list", "--count", "HEAD") -WorkingDirectory $repositoryRoot
        if ($result.ExitCode -eq 0 -and $result.Output.Trim() -match '^\d+$') {
            return [int] $result.Output.Trim()
        }
    }
    throw "The version code could not be read from git."
}

# Makes the signing key on the first build. The password is random and kept
# beside the key; the key never leaves this PC.
function Get-SigningKey {
    param([string] $Keytool)

    $keyStore = Join-Path $SigningFolder "companion-signing.p12"
    $passwordFile = Join-Path $SigningFolder "companion-signing-password.txt"
    if ((Test-Path -LiteralPath $keyStore) -and (Test-Path -LiteralPath $passwordFile)) {
        return @{ KeyStore = $keyStore; PasswordFile = $passwordFile }
    }
    if ((Test-Path -LiteralPath $keyStore) -or (Test-Path -LiteralPath $passwordFile)) {
        throw ("$SigningFolder has only one of companion-signing.p12 and " +
            "companion-signing-password.txt. Restore the other, or remove both to make a new key.")
    }

    Write-Host "Making the companion app's signing key in $SigningFolder"
    New-Item -ItemType Directory -Force -Path $SigningFolder | Out-Null
    $bytes = New-Object byte[] 24
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $random.GetBytes($bytes)
    $random.Dispose()
    $password = [Convert]::ToBase64String($bytes).Replace("+", "A").Replace("/", "B")
    [IO.File]::WriteAllText($passwordFile, $password)
    Invoke-Tool -Name "keytool" -FilePath $Keytool -Arguments @(
        "-genkeypair", "-keystore", $keyStore, "-storetype", "PKCS12",
        "-storepass:file", $passwordFile, "-alias", "companion",
        "-keyalg", "RSA", "-keysize", "3072", "-validity", "10000",
        "-dname", "CN=Windows A11y Recorder companion") | Out-Null
    return @{ KeyStore = $keyStore; PasswordFile = $passwordFile }
}

# --- Build ---

New-Item -ItemType Directory -Force -Path $WorkFolder, $DownloadFolder | Out-Null
Confirm-SdkLicense

Write-Step "Tools"
$jdkHome = Get-OnlyFolder (Expand-Tool -Item $jdk -Archive (Get-Download $jdk))
$buildToolsHome = Get-OnlyFolder (Expand-Tool -Item $buildTools -Archive (Get-Download $buildTools))
$platformHome = Get-OnlyFolder (Expand-Tool -Item $platform -Archive (Get-Download $platform))

$java = Join-Path (Join-Path $jdkHome "bin") "java$exe"
$javac = Join-Path (Join-Path $jdkHome "bin") "javac$exe"
$keytool = Join-Path (Join-Path $jdkHome "bin") "keytool$exe"
$aapt2 = Join-Path $buildToolsHome "aapt2$exe"
$zipalign = Join-Path $buildToolsHome "zipalign$exe"
$d8Jar = Join-Path (Join-Path $buildToolsHome "lib") "d8.jar"
$apksignerJar = Join-Path (Join-Path $buildToolsHome "lib") "apksigner.jar"
$androidJar = Join-Path $platformHome "android.jar"
foreach ($file in @($java, $javac, $keytool, $aapt2, $zipalign, $d8Jar, $apksignerJar, $androidJar)) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "A build tool is missing: $file"
    }
}
if (-not $onWindows) {
    # Zip files do not always keep the Linux execute permission.
    Invoke-Tool -Name "chmod" -FilePath "chmod" -Arguments @("+x", $aapt2, $zipalign) | Out-Null
}
Write-Host ((Invoke-Tool -Name "java" -FilePath $java -Arguments @("-version")) -split "`n")[0].Trim()

$versionName = Get-ProductVersion
$versionCode = Get-VersionCode
Write-Host "Companion app $versionName, version code $versionCode."

$build = Join-Path $WorkFolder "build"
if (Test-Path -LiteralPath $build) {
    Remove-Item -LiteralPath $build -Recurse -Force
}
$classes = Join-Path $build "classes"
$dex = Join-Path $build "dex"
New-Item -ItemType Directory -Force -Path $build, $classes, $dex | Out-Null

Write-Step "Resources"
$compiledResources = Join-Path $build "resources.zip"
$linked = Join-Path $build "linked.apk"
Invoke-Tool -Name "aapt2 compile" -FilePath $aapt2 -Arguments @(
    "compile", "--dir", (Join-Path $source "res"), "-o", $compiledResources) | Out-Null
Invoke-Tool -Name "aapt2 link" -FilePath $aapt2 -Arguments @(
    "link", "-I", $androidJar,
    "--manifest", (Join-Path $source "AndroidManifest.xml"),
    "--min-sdk-version", "$minimumSdk", "--target-sdk-version", "$targetSdk",
    "--version-code", "$versionCode", "--version-name", $versionName,
    "-o", $linked, $compiledResources) | Out-Null

Write-Step "Code"
$javaFiles = @(Get-ChildItem -LiteralPath (Join-Path $source "src") -Recurse -Filter "*.java" -File |
        ForEach-Object { $_.FullName })
if ($javaFiles.Count -eq 0) {
    throw "No Java source was found in $source."
}
Invoke-Tool -Name "javac" -FilePath $javac -Arguments (@(
        "-source", "11", "-target", "11", "-Xlint:-options", "-Werror",
        "-classpath", $androidJar, "-d", $classes) + $javaFiles) | Out-Null
$classFiles = @(Get-ChildItem -LiteralPath $classes -Recurse -Filter "*.class" -File |
        ForEach-Object { $_.FullName })
Invoke-Tool -Name "d8" -FilePath $java -Arguments (@(
        "-cp", $d8Jar, "com.android.tools.r8.D8", "--release",
        "--min-api", "$minimumSdk", "--lib", $androidJar, "--output", $dex) + $classFiles) | Out-Null

Write-Step "Packaging and signing"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($linked, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zip, (Join-Path $dex "classes.dex"), "classes.dex") | Out-Null
}
finally {
    $zip.Dispose()
}
$aligned = Join-Path $build "aligned.apk"
Invoke-Tool -Name "zipalign" -FilePath $zipalign -Arguments @("-f", "4", $linked, $aligned) | Out-Null

$key = Get-SigningKey -Keytool $keytool
$outputFolder = Join-Path $WorkFolder "output"
New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$apk = Join-Path $outputFolder $apkName
Invoke-Tool -Name "apksigner sign" -FilePath $java -Arguments @(
    "-jar", $apksignerJar, "sign", "--ks", $key.KeyStore, "--ks-pass", "file:$($key.PasswordFile)",
    "--ks-key-alias", "companion", "--min-sdk-version", "$minimumSdk", "--out", $apk, $aligned) | Out-Null
$verify = Invoke-Tool -Name "apksigner verify" -FilePath $java -Arguments @(
    "-jar", $apksignerJar, "verify", "--print-certs", "--min-sdk-version", "$minimumSdk", $apk)
if ($verify -notmatch "certificate SHA-256 digest:\s*([0-9a-f]{64})") {
    Write-Host $verify
    throw "apksigner verify did not report the signing certificate."
}
$certificate = $Matches[1]

$hash = Get-Sha256 $apk
Set-Content -LiteralPath (Join-Path $outputFolder "companion-app.txt") -Encoding UTF8 -Value @(
    "package $packageName",
    "versionName $versionName",
    "versionCode $versionCode",
    "minSdk $minimumSdk",
    "targetSdk $targetSdk",
    "sha256 $hash",
    "signingCertificateSha256 $certificate")

Write-Host ""
Write-Host "COMPANION APP BUILT"
Write-Host "  $apk"
Write-Host "  $versionName, version code $versionCode"
Write-Host "  SHA-256 $hash"
Write-Host "  Signing certificate SHA-256 $certificate"

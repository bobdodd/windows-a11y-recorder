# Fetches the PostgreSQL server programs the recorder runs as its database,
# into .postgres\pgsql under the repository. The integration tests and the
# app find them there. The folder is not committed.
#
# The archive is the EDB Windows x64 binaries zip, which the PostgreSQL
# download page offers for including PostgreSQL in another application. Only
# the server's programs, libraries, shared files, and licences are kept:
# pgAdmin, StackBuilder, documentation, and headers are not.
[CmdletBinding()]
param(
    [string] $Version = "18.6-1",

    # SHA-256 of the archive for the default version, computed from the file
    # downloaded on 2026-09-25. EDB does not publish a checksum beside the
    # archive, so this detects a changed or corrupted download rather than
    # proving the archive's origin.
    [string] $ExpectedSha256 = "fbe23da234ee31547bf8a36d29dfd81e82b849df2d2b78d2eecb43d360252f8c",

    [string] $Destination = (Join-Path (Split-Path -Parent $PSScriptRoot) ".postgres")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$marker = Join-Path $Destination "version.txt"
$postgres = Join-Path $Destination "pgsql\bin\postgres.exe"
if ((Test-Path $postgres) -and (Test-Path $marker) -and
    ((Get-Content $marker -Raw).Trim() -eq $Version)) {
    Write-Host "PostgreSQL $Version is already in $Destination."
    return
}

$url = "https://get.enterprisedb.com/postgresql/postgresql-$Version-windows-x64-binaries.zip"
$archive = Join-Path ([System.IO.Path]::GetTempPath()) "postgresql-$Version-windows-x64-binaries.zip"

Write-Host "Downloading $url"
$previousProgress = $ProgressPreference
$ProgressPreference = "SilentlyContinue"
try {
    Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing
}
finally {
    $ProgressPreference = $previousProgress
}

$actual = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ExpectedSha256 -and $actual -ne $ExpectedSha256.ToLowerInvariant()) {
    Remove-Item $archive -Force
    throw "The archive's SHA-256 is $actual, not $ExpectedSha256."
}

if (Test-Path $Destination) {
    Remove-Item $Destination -Recurse -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$keep = @("pgsql/bin/", "pgsql/lib/", "pgsql/share/")
$licences = @("pgsql/server_license.txt", "pgsql/commandlinetools_3rd_party_licenses.txt")
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
$count = 0
$bytes = 0L
try {
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName.Replace("\", "/")
        $wanted = ($licences -contains $name) -or
            (($keep | Where-Object { $name.StartsWith($_) }) -and -not $name.EndsWith("/"))
        # StackBuilder and the wxWidgets libraries only it uses.
        $leaf = $name.Substring($name.LastIndexOf("/") + 1)
        if ($leaf -like "stackbuilder*" -or $leaf -like "wx*.dll") {
            $wanted = $false
        }

        if (-not $wanted) {
            continue
        }

        $target = Join-Path $Destination ($name.Replace("/", "\"))
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        $count++
        $bytes += $entry.Length
    }
}
finally {
    $zip.Dispose()
}

Remove-Item $archive -Force
Set-Content -Path $marker -Value $Version -Encoding ASCII
Write-Host ("Extracted {0} files, {1:N0} MB, to {2}." -f $count, ($bytes / 1MB), $Destination)

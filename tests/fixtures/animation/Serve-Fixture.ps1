# Serves the animation fixture folder over http on the loopback interface,
# so that a recording of it has http addresses and its recreation is served
# at its recorded address with its images. Runs in an ordinary,
# non-administrator PowerShell; stop it with Ctrl+C.
#
#   .\Serve-Fixture.ps1 [-Folder C:\Users\Public\Downloads\animation-fixture] [-Port 8765]
#
# Then open http://127.0.0.1:8765/index.html in the instrumented Chromium.

param(
    [string]$Folder = 'C:\Users\Public\Downloads\animation-fixture',
    [int]$Port = 8765
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $Folder).Path
$types = @{
    '.html' = 'text/html; charset=utf-8'
    '.gif'  = 'image/gif'
    '.webp' = 'image/webp'
    '.png'  = 'image/png'
    '.css'  = 'text/css; charset=utf-8'
    '.js'   = 'text/javascript; charset=utf-8'
}

# A TCP listener on 127.0.0.1 needs no URL reservation, unlike HttpListener.
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
Write-Host "Serving $root at http://127.0.0.1:$Port/index.html (Ctrl+C to stop)"

function Send-Response($stream, [int]$status, [string]$reason, [string]$type, [byte[]]$body) {
    $head = "HTTP/1.1 $status $reason`r`nContent-Type: $type`r`nContent-Length: $($body.Length)`r`nCache-Control: no-store`r`nConnection: close`r`n`r`n"
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($head)
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Write($body, 0, $body.Length)
}

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII, $false, 4096, $true)
            $request = $reader.ReadLine()
            while (($line = $reader.ReadLine()) -ne $null -and $line -ne '') { }
            if ($request -notmatch '^(GET|HEAD) (/[^ ?#]*)') {
                Send-Response $stream 400 'Bad Request' 'text/plain' ([byte[]]@())
                continue
            }
            $path = [System.Uri]::UnescapeDataString($Matches[2])
            if ($path -eq '/') { $path = '/index.html' }
            $file = [System.IO.Path]::GetFullPath((Join-Path $root $path.TrimStart('/')))
            $extension = [System.IO.Path]::GetExtension($file).ToLowerInvariant()
            if (-not $file.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $file -PathType Leaf) -or -not $types.ContainsKey($extension)) {
                Send-Response $stream 404 'Not Found' 'text/plain' ([System.Text.Encoding]::ASCII.GetBytes('Not found'))
                Write-Host "404 $path"
                continue
            }
            $body = [System.IO.File]::ReadAllBytes($file)
            if ($Matches[1] -eq 'HEAD') { $body = [byte[]]@() }
            Send-Response $stream 200 'OK' $types[$extension] $body
            Write-Host "200 $path"
        }
        catch {
            Write-Host "Request failed: $($_.Exception.Message)"
        }
        finally {
            $client.Dispose()
        }
    }
}
finally {
    $listener.Stop()
}

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ChromiumSource,

    [string] $DepotTools = (
        Join-Path $env:USERPROFILE "chromium-dev\depot_tools"
    ),

    [string] $OutputRoot = (
        Join-Path $env:PUBLIC "Documents\A11yRecorderBlinkValidation"
    ),

    [string] $DotnetPath = (
        Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    ),

    # The capture has to outlast the fixture's page-lifecycle phase, which is
    # scheduled by this script after the page reports visible and completes
    # 3.5 seconds later. The default leaves room for a slow launch.
    [ValidateRange(5, 300)]
    [int] $DurationSeconds = 25
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Description,

        [Parameter(Mandatory = $true)]
        [scriptblock] $Command
    )

    Write-Host "`n== $Description =="

    # Native tools in this pipeline report progress on stderr. Python's
    # unittest runner writes its progress and summary there even on success.
    # With $ErrorActionPreference set to Stop, PowerShell converts that
    # stderr output into a terminating NativeCommandError whenever the
    # script's own output is redirected or transcribed, which aborts a run
    # that has not actually failed. Only the process exit code decides
    # success here.
    #
    # Merging stderr into the success stream and rendering each record as
    # text keeps that progress output in the transcript as plain lines. Left
    # as error records it appears as a NativeCommandError block naming this
    # script and line, which reads like a failure in an otherwise passing run.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $Command 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                Write-Output $_.ToString()
            }
            else {
                Write-Output $_
            }
        }
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Get-PythonCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DepotToolsPath
    )

    $candidates = @(
        (Join-Path $DepotToolsPath "python3.bat"),
        "python3",
        "python"
    )
    foreach ($candidate in $candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if (-not $command) {
            continue
        }

        & $candidate --version *> $null
        if ($LASTEXITCODE -eq 0) {
            return $candidate
        }
    }

    throw (
        "Python was not found. The script checked Depot Tools and PATH. " +
        "Confirm that depot_tools\python3.bat exists."
    )
}

# Reads a property that a DevTools reply may omit. Strict mode treats reading an
# absent property as an error, so every optional field is read through here.
function Get-CdpProperty {
    param(
        $Value,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if ($null -eq $Value) {
        return $null
    }
    if ($Value.PSObject.Properties.Name -notcontains $Name) {
        return $null
    }
    $Value.$Name
}

# Windows PowerShell 5.1 hands back a faulted task's failure as a
# MethodInvocationException wrapping an AggregateException, which reports the
# wrapper rather than the cause. This unwraps the chain so a failed DevTools
# operation names what actually failed.
function Wait-CdpTask {
    param(
        [Parameter(Mandatory = $true)]
        $Task,

        [int] $TimeoutMilliseconds = 15000,

        [string] $Description = "A DevTools operation"
    )

    $completed = $false
    try {
        $completed = $Task.Wait($TimeoutMilliseconds)
    }
    catch {
        $chain = @()
        $current = $_.Exception
        while ($current) {
            if ($current -is [System.AggregateException]) {
                foreach ($inner in $current.Flatten().InnerExceptions) {
                    $chain += (
                        "{0}: {1}" -f $inner.GetType().FullName, $inner.Message
                    )
                }
                $current = $current.Flatten().InnerExceptions[0].InnerException
                continue
            }
            $chain += ("{0}: {1}" -f $current.GetType().FullName, $current.Message)
            $current = $current.InnerException
        }
        throw ("$Description failed. " + ($chain -join " <- "))
    }
    if (-not $completed) {
        throw "$Description timed out after $TimeoutMilliseconds ms."
    }
    $Task
}

# Windows PowerShell 5.1 returns a JSON array from one HTTP response as a single
# object, so wrapping that response in an array can nest an Object[] inside a
# one-item array. Filtering the nested value reads properties by member
# enumeration, which joins every target's value into one string instead of
# returning one target. Flattening one level before any filtering runs is what
# keeps a filter operating on individual targets.
function Expand-CdpTargets {
    param($Response)

    $flat = New-Object System.Collections.ArrayList
    foreach ($chunk in @($Response)) {
        if ($null -eq $chunk) {
            continue
        }
        if ($chunk -isnot [string] -and
            $chunk -is [System.Collections.IEnumerable]) {
            foreach ($item in $chunk) {
                [void]$flat.Add($item)
            }
        }
        else {
            [void]$flat.Add($chunk)
        }
    }
    $flat.ToArray()
}

function Get-CdpTargetList {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DevToolsBase
    )

    $response = Invoke-WebRequest `
        -Uri "$DevToolsBase/json/list" `
        -UseBasicParsing `
        -TimeoutSec 2
    Expand-CdpTargets (ConvertFrom-Json $response.Content)
}

# Selects the fixture page target. A target is usable only when its own URL and
# WebSocket URL are scalar strings, which rejects a collection that looks like a
# single target under member access. The title gate is what the readiness poll
# relies on, and matching the URL exactly keeps a browser-interface page such as
# chrome://omnibox-popup.top-chrome from being selected.
function Select-CdpFixtureTarget {
    param(
        $Targets,

        [Parameter(Mandatory = $true)]
        [string] $FixtureUri,

        [Parameter(Mandatory = $true)]
        [string] $ReadyTitle
    )

    @(
        $Targets |
            Where-Object {
                (Get-CdpProperty $_ "type") -eq "page" -and
                (Get-CdpProperty $_ "url") -is [string] -and
                (Get-CdpProperty $_ "title") -eq $ReadyTitle -and
                (Get-CdpProperty $_ "webSocketDebuggerUrl") -is [string] -and
                $_.webSocketDebuggerUrl.Length -gt 0 -and
                $_.url.StartsWith($FixtureUri)
            }
    )
}

# Opens a second page and brings it to the front so the fixture page becomes
# hidden, which is what the page-lifecycle evidence needs. The identifier is
# read the same way as the target list rather than through Invoke-RestMethod,
# which does not enumerate a JSON array under Windows PowerShell 5.1, and the
# identifier is required to be a scalar string so that a response shape other
# than a single target fails here instead of producing an activate request for
# a target that does not exist.
function New-CdpBackgroundTarget {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DevToolsBase
    )

    $created = ConvertFrom-Json (
        Invoke-WebRequest `
            -Method Put `
            -Uri "$DevToolsBase/json/new?about%3Ablank" `
            -UseBasicParsing `
            -TimeoutSec 5
    ).Content
    $targetId = Get-CdpProperty $created "id"
    if ($targetId -isnot [string] -or $targetId.Length -eq 0) {
        throw "Opening a background DevTools target returned no identifier."
    }
    $activated = Invoke-WebRequest `
        -Method Put `
        -Uri "$DevToolsBase/json/activate/$targetId" `
        -UseBasicParsing `
        -TimeoutSec 5
    if ($activated.StatusCode -ne 200) {
        throw (
            "Activating the background DevTools target reported status " +
            "$($activated.StatusCode)."
        )
    }
    return $targetId
}

function New-CdpSession {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SocketUrl
    )

    if ($SocketUrl -notmatch '^wss?://[^\s]+$') {
        throw (
            "Unusable DevTools WebSocket URL '$SocketUrl'. More than one " +
            "target was probably selected."
        )
    }

    $socket = New-Object System.Net.WebSockets.ClientWebSocket
    $source = New-Object System.Threading.CancellationTokenSource
    $null = Wait-CdpTask `
        $socket.ConnectAsync([Uri]$SocketUrl, $source.Token) `
        15000 `
        "Connecting to the fixture's DevTools endpoint"
    if ($socket.State -ne [System.Net.WebSockets.WebSocketState]::Open) {
        throw "The DevTools socket state is $($socket.State) rather than Open."
    }

    [pscustomobject]@{
        Socket = $socket
        Source = $source
        NextId = 1
    }
}

function Send-CdpText {
    param(
        [Parameter(Mandatory = $true)]
        $Session,

        [Parameter(Mandatory = $true)]
        [string] $Text
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(, $bytes)
    $null = Wait-CdpTask `
        $Session.Socket.SendAsync(
            $segment,
            [System.Net.WebSockets.WebSocketMessageType]::Text,
            $true,
            $Session.Source.Token) `
        15000 `
        "Sending a DevTools command"
}

function Receive-CdpText {
    param(
        [Parameter(Mandatory = $true)]
        $Session
    )

    $buffer = New-Object byte[] 65536
    $builder = New-Object System.Text.StringBuilder
    while ($true) {
        $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(
            $buffer, 0, $buffer.Length
        )
        $receive = Wait-CdpTask `
            $Session.Socket.ReceiveAsync($segment, $Session.Source.Token) `
            15000 `
            "Reading a DevTools message"
        $result = $receive.Result
        if ($result.MessageType -eq
            [System.Net.WebSockets.WebSocketMessageType]::Close) {
            throw (
                "The browser closed the DevTools socket: " +
                "$($Session.Socket.CloseStatus) " +
                "$($Session.Socket.CloseStatusDescription)"
            )
        }
        [void]$builder.Append(
            [System.Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count)
        )
        if ($result.EndOfMessage) {
            break
        }
    }
    $builder.ToString()
}

# Sends one command and returns its reply. DevTools interleaves events with
# replies on the same socket, so messages are read until the reply carrying this
# command's identifier arrives.
function Invoke-CdpCommand {
    param(
        [Parameter(Mandatory = $true)]
        $Session,

        [Parameter(Mandatory = $true)]
        [string] $Method,

        [hashtable] $Parameters
    )

    $id = $Session.NextId
    $Session.NextId = $id + 1
    $command = @{ id = $id; method = $Method }
    if ($Parameters) {
        $command.params = $Parameters
    }
    Send-CdpText $Session (ConvertTo-Json $command -Depth 10 -Compress)

    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $message = ConvertFrom-Json (Receive-CdpText $Session)
        if ((Get-CdpProperty $message "id") -ne $id) {
            continue
        }
        $failure = Get-CdpProperty $message "error"
        if ($failure) {
            throw (
                "$Method failed: $($failure.message) (code $($failure.code))"
            )
        }
        return (Get-CdpProperty $message "result")
    }

    throw "No DevTools reply to $Method arrived."
}

function Close-CdpSession {
    param($Session)

    if (-not $Session) {
        return
    }
    try {
        if ($Session.Socket.State -eq
            [System.Net.WebSockets.WebSocketState]::Open) {
            $null = Wait-CdpTask `
                $Session.Socket.CloseAsync(
                    [System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,
                    "done",
                    $Session.Source.Token) `
                5000 `
                "Closing the DevTools socket"
        }
    }
    catch {
        Write-Host "Closing the DevTools socket failed: $($_.Exception.Message)"
    }
    finally {
        $Session.Socket.Dispose()
        $Session.Source.Dispose()
    }
}

# Registers a listener in an isolated world so the capture contains a
# registration the page's own script cannot make. The world is created in the
# fixture's main frame, the listener is registered on an element no document
# script touches, and the marker read from the main world afterwards is what
# shows the two worlds were separate rather than one world under another name.
function Add-IsolatedWorldListener {
    param(
        [Parameter(Mandatory = $true)]
        $Session,

        [Parameter(Mandatory = $true)]
        [string] $WorldName
    )

    $null = Invoke-CdpCommand $Session "Page.enable" $null
    $null = Invoke-CdpCommand $Session "Runtime.enable" $null
    $tree = Invoke-CdpCommand $Session "Page.getFrameTree" $null
    $frameId = [string]$tree.frameTree.frame.id
    if ([string]::IsNullOrWhiteSpace($frameId)) {
        throw "The fixture frame tree reported no main frame identifier."
    }

    $world = Invoke-CdpCommand $Session "Page.createIsolatedWorld" @{
        frameId = $frameId
        worldName = $WorldName
        grantUniversalAccess = $false
    }
    $contextId = Get-CdpProperty $world "executionContextId"
    if (-not $contextId) {
        throw "Creating the isolated world returned no execution context."
    }

    $registration = Invoke-CdpCommand $Session "Runtime.evaluate" @{
        contextId = $contextId
        returnByValue = $true
        expression = @'
(function () {
  window.__a11yRecorderIsolatedMarker = "isolated";
  var target = document.getElementById("isolated-world-target");
  if (!target) {
    return "no-target";
  }
  target.addEventListener("click", function () {}, false);
  return "registered";
})()
'@
    }
    $failure = Get-CdpProperty $registration "exceptionDetails"
    if ($failure) {
        throw "The isolated-world registration failed: $($failure.text)"
    }
    $outcome = [string](Get-CdpProperty $registration.result "value")
    if ($outcome -ne "registered") {
        throw (
            "The isolated-world script reported '$outcome' rather than a " +
            "completed registration."
        )
    }

    $mainWorld = Invoke-CdpCommand $Session "Runtime.evaluate" @{
        returnByValue = $true
        expression = "String(window.__a11yRecorderIsolatedMarker)"
    }
    $marker = [string](Get-CdpProperty $mainWorld.result "value")
    if ($marker -ne "undefined") {
        throw (
            "The main world can see the isolated world's marker, so the " +
            "registration was not made from a separate world."
        )
    }

    [pscustomobject]@{
        FrameId = $frameId
        ExecutionContextId = $contextId
    }
}

# Schedules the fixture's page-lifecycle timers while the page is provably
# visible. A page's visibility during load depends on when its window is shown,
# so a fixture that scheduled these timers at parse time recorded whichever
# state the desktop happened to be in. The fixture page is activated and raised
# here, its own reported visibility is required to be visible before anything
# is scheduled, and the caller hides the page immediately afterwards, so the
# schedule is recorded while visible and the callbacks enter while hidden.
function Start-FixtureLifecycleEvidence {
    param(
        [Parameter(Mandatory = $true)]
        $Session,

        [Parameter(Mandatory = $true)]
        [string] $DevToolsBase,

        [Parameter(Mandatory = $true)]
        [string] $TargetId
    )

    $activated = Invoke-WebRequest `
        -Method Put `
        -Uri "$DevToolsBase/json/activate/$TargetId" `
        -UseBasicParsing `
        -TimeoutSec 5
    if ($activated.StatusCode -ne 200) {
        throw (
            "Activating the fixture target reported status " +
            "$($activated.StatusCode)."
        )
    }
    $null = Invoke-CdpCommand $Session "Page.bringToFront" $null

    $state = ""
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $reported = Invoke-CdpCommand $Session "Runtime.evaluate" @{
            returnByValue = $true
            expression = "String(document.visibilityState)"
        }
        $state = [string](Get-CdpProperty $reported.result "value")
        if ($state -eq "visible") {
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if ($state -ne "visible") {
        throw (
            "The fixture page reported visibility '$state' after being " +
            "activated and raised, so page-lifecycle evidence cannot be " +
            "scheduled while visible. A window that is minimized, occluded, " +
            "or on an inactive desktop produces this."
        )
    }

    $scheduled = Invoke-CdpCommand $Session "Runtime.evaluate" @{
        returnByValue = $true
        expression = "String(window.recorderScheduleLifecycleEvidence())"
    }
    $failure = Get-CdpProperty $scheduled "exceptionDetails"
    if ($failure) {
        throw (
            "Scheduling the fixture's page-lifecycle timers failed: " +
            "$($failure.text)"
        )
    }
    $outcome = [string](Get-CdpProperty $scheduled.result "value")
    if ($outcome -ne "visible") {
        throw (
            "The fixture reported '$outcome' when it scheduled its " +
            "page-lifecycle timers rather than scheduling them while visible."
        )
    }
    return $outcome
}

# The page the cookie logging fixture serves. It takes the value it writes as an
# argument rather than holding it, so the value appears in no document text a
# DOM checkpoint could capture, and the verifier can require that no record in
# the session contains it. The page schedules no timers, so it adds nothing to
# the timer records the listener fixture is verified against.
$cookieFixturePage = @'
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Cookie logging fixture loading</title>
</head>
<body>
<p>Cookie logging fixture.</p>
<script>
window.cookieFixtureChanges = [];
cookieStore.addEventListener("change", function (event) {
  for (const cookie of event.changed) {
    window.cookieFixtureChanges.push("changed:" + cookie.name);
  }
  for (const cookie of event.deleted) {
    window.cookieFixtureChanges.push("deleted:" + cookie.name);
  }
});
async function runCookieFixture(value) {
  document.cookie =
      "a11y_recorder_document=" + value + "; Path=/; SameSite=Lax";
  const documentCookieNames = document.cookie
      .split("; ")
      .filter(function (entry) { return entry.length > 0; })
      .map(function (entry) { return entry.split("=")[0]; });
  await cookieStore.set("a11y_recorder_store", value);
  await cookieStore.get("a11y_recorder_store");
  await cookieStore.getAll();
  await fetch("/set-cookie", { cache: "no-store" });
  await fetch("/echo", { cache: "no-store" });
  await cookieStore.delete("a11y_recorder_store");
  return JSON.stringify({
    outcome: "completed",
    documentCookieNames: documentCookieNames
  });
}
document.title = "Cookie logging fixture ready";
</script>
</body>
</html>
'@

# The page the network logging fixture serves, reached through a redirect so
# the navigation response records a redirect chain. Its function sends a fetch
# with credential-bearing and plain request headers, follows a redirected
# fetch, sends a fetch to a closed loopback port so the request fails, loads the
# same cacheable script twice so the second load is served from the memory
# cache, and starts a dedicated worker that sends a fetch of its own. The
# credential values are generated per run and passed in by the run script so
# the verifier can require that no record contains them.
$networkFixturePage = @'
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Network logging fixture loading</title>
</head>
<body>
<p>Network logging fixture</p>
<script>
function networkFixtureScript() {
  return new Promise(function (resolve, reject) {
    const script = document.createElement("script");
    script.src = "/network/cached.js";
    script.onload = function () { resolve(); };
    script.onerror = function () { reject(new Error("script load failed")); };
    document.head.appendChild(script);
  });
}
async function runNetworkFixture(values) {
  const data = await fetch("/network/data", {
    cache: "no-store",
    headers: {
      "Authorization": "Bearer " + values.authorization,
      "X-Api-Key": values.apiKey,
      "X-Fixture-Scheme": "Bearer " + values.scheme,
      "X-Fixture-Plain": "network-fixture-plain"
    }
  });
  const dataText = await data.text();
  const hop = await fetch("/network/hop", { cache: "no-store" });
  const hopText = await hop.text();
  let refused = "resolved";
  try {
    await fetch(values.refusedUrl, { cache: "no-store", mode: "no-cors" });
  } catch (error) {
    refused = "rejected";
  }
  await networkFixtureScript();
  await networkFixtureScript();
  const worker = new Worker("/network/worker.js");
  const workerText = await new Promise(function (resolve, reject) {
    worker.onmessage = function (event) { resolve(event.data); };
    worker.onerror = function () { reject(new Error("worker failed")); };
  });
  worker.terminate();
  return JSON.stringify({
    outcome: "completed",
    dataStatus: data.status,
    dataText: dataText,
    hopRedirected: hop.redirected,
    hopUrl: hop.url,
    hopText: hopText,
    refused: refused,
    cachedRuns: window.networkFixtureCachedRuns,
    workerText: workerText
  });
}
document.title = "Network logging fixture ready";
</script>
</body>
</html>
'@

# The dedicated worker the network logging fixture starts. It sends one fetch
# and posts the response text back to the page.
$networkFixtureWorker = @'
fetch("/network/worker-data", { cache: "no-store" })
  .then(function (response) { return response.text(); })
  .then(function (text) { postMessage(text); });
'@

# Serves the cookie logging fixture over HTTP on the loopback interface. Cookie
# APIs refuse a file URL and a response header is the only way to set a cookie
# from outside script, so the fixture needs an origin the listener fixture's
# file URL cannot provide. The listener runs in its own runspace so the capture
# is not blocked, answers one request per connection, and drops a connection
# that sends no request within two seconds, which is what a speculative
# connection opened by the browser does.
function Start-CookieFixtureServer {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Page,

        [Parameter(Mandatory = $true)]
        [string] $CookieValue,

        # The interaction logging fixture, served at /interaction. It shares
        # the cookie fixture's origin and sets no cookie of its own.
        [Parameter(Mandatory = $true)]
        [string] $InteractionPage,

        # The layout logging fixture, served at /layout. It shares the cookie
        # fixture's origin and sets no cookie of its own.
        [Parameter(Mandatory = $true)]
        [string] $LayoutPage,

        # The network logging fixture, served at /network behind a redirect
        # from /network-start, with its worker, script, and data responses.
        [Parameter(Mandatory = $true)]
        [string] $NetworkPage,

        [Parameter(Mandatory = $true)]
        [string] $NetworkWorker,

        # The value of the credential-named response header the network
        # fixture's data response carries, which no record may contain.
        [Parameter(Mandatory = $true)]
        [string] $NetworkSessionValue
    )

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([Net.IPEndPoint] $listener.LocalEndpoint).Port
    $server = [PowerShell]::Create()
    $null = $server.AddScript({
            param(
                $Listener,
                $Page,
                $CookieValue,
                $InteractionPage,
                $LayoutPage,
                $NetworkPage,
                $NetworkWorker,
                $NetworkSessionValue
            )
            $ascii = [Text.Encoding]::ASCII
            while ($true) {
                try {
                    $client = $Listener.AcceptTcpClient()
                }
                catch {
                    break
                }
                try {
                    $client.ReceiveTimeout = 2000
                    $client.SendTimeout = 2000
                    $stream = $client.GetStream()
                    $reader = New-Object IO.StreamReader(
                        $stream, $ascii, $false, 1024, $true
                    )
                    $requestLine = $reader.ReadLine()
                    while ($true) {
                        $line = $reader.ReadLine()
                        if ($null -eq $line -or $line.Length -eq 0) {
                            break
                        }
                    }
                    $path = ""
                    if ($requestLine -match '^[A-Z]+ (\S+) HTTP/') {
                        $path = $Matches[1]
                    }
                    $status = "404 Not Found"
                    $contentType = "text/plain; charset=utf-8"
                    $body = "not found"
                    $setCookie = $null
                    $cacheControl = "no-store"
                    $extraHeaders = @()
                    if ($path -eq "/") {
                        $status = "200 OK"
                        $contentType = "text/html; charset=utf-8"
                        $body = $Page
                        $setCookie = (
                            "a11y_recorder_response=$CookieValue; " +
                            "Path=/; SameSite=Lax"
                        )
                    }
                    elseif ($path -eq "/set-cookie") {
                        $status = "200 OK"
                        $body = "set"
                        $setCookie = (
                            "a11y_recorder_fetch=$CookieValue; " +
                            "Path=/; SameSite=Strict"
                        )
                    }
                    elseif ($path -eq "/echo") {
                        $status = "200 OK"
                        $body = "echo"
                    }
                    elseif ($path -eq "/interaction") {
                        $status = "200 OK"
                        $contentType = "text/html; charset=utf-8"
                        $body = $InteractionPage
                    }
                    elseif ($path -eq "/layout") {
                        $status = "200 OK"
                        $contentType = "text/html; charset=utf-8"
                        $body = $LayoutPage
                    }
                    elseif ($path -eq "/network-start") {
                        $status = "302 Found"
                        $body = "redirect"
                        $extraHeaders += "Location: /network"
                    }
                    elseif ($path -eq "/network") {
                        $status = "200 OK"
                        $contentType = "text/html; charset=utf-8"
                        $body = $NetworkPage
                    }
                    elseif ($path -eq "/network/data" -or
                            $path -eq "/network/data?hop=1") {
                        $status = "200 OK"
                        $body = "network data"
                        $extraHeaders += (
                            "X-Fixture-Response: network-fixture-response"
                        )
                        $extraHeaders += "X-Session-Id: $NetworkSessionValue"
                    }
                    elseif ($path -eq "/network/hop") {
                        $status = "302 Found"
                        $body = "redirect"
                        $extraHeaders += "Location: /network/data?hop=1"
                    }
                    elseif ($path -eq "/network/cached.js") {
                        # Cacheable, so the page's second load of the same
                        # script is served from Blink's memory cache.
                        $status = "200 OK"
                        $contentType = "text/javascript; charset=utf-8"
                        $cacheControl = "max-age=600"
                        $body = (
                            "window.networkFixtureCachedRuns = " +
                            "(window.networkFixtureCachedRuns || 0) + 1;"
                        )
                    }
                    elseif ($path -eq "/network/worker.js") {
                        $status = "200 OK"
                        $contentType = "text/javascript; charset=utf-8"
                        $body = $NetworkWorker
                    }
                    elseif ($path -eq "/network/worker-data") {
                        $status = "200 OK"
                        $body = "worker data"
                    }
                    $bodyBytes = [Text.Encoding]::UTF8.GetBytes($body)
                    $head = (
                        "HTTP/1.1 $status`r`n" +
                        "Content-Type: $contentType`r`n" +
                        "Content-Length: $($bodyBytes.Length)`r`n" +
                        "Cache-Control: $cacheControl`r`n" +
                        "Connection: close`r`n"
                    )
                    if ($setCookie) {
                        $head += "Set-Cookie: $setCookie`r`n"
                    }
                    foreach ($extraHeader in $extraHeaders) {
                        $head += "$extraHeader`r`n"
                    }
                    $head += "`r`n"
                    $headBytes = $ascii.GetBytes($head)
                    $stream.Write($headBytes, 0, $headBytes.Length)
                    $stream.Write($bodyBytes, 0, $bodyBytes.Length)
                    $stream.Flush()
                }
                catch {
                    # A connection that sent no request or closed early is
                    # dropped. The fixture step fails on its own if the page
                    # it needed was never served.
                }
                finally {
                    $client.Close()
                }
            }
        }).AddArgument($listener).AddArgument($Page).AddArgument(
            $CookieValue
        ).AddArgument($InteractionPage).AddArgument($LayoutPage).AddArgument(
            $NetworkPage
        ).AddArgument($NetworkWorker).AddArgument($NetworkSessionValue)
    $handle = $server.BeginInvoke()

    [pscustomobject]@{
        Listener = $listener
        PowerShell = $server
        Handle = $handle
        BaseUri = "http://127.0.0.1:$port/"
    }
}

function Stop-CookieFixtureServer {
    param($Server)

    if (-not $Server) {
        return
    }
    try {
        $Server.Listener.Stop()
        $null = $Server.Handle.AsyncWaitHandle.WaitOne(5000)
    }
    finally {
        $Server.PowerShell.Dispose()
    }
}

# Opens the cookie logging fixture in a background tab, runs its cookie calls,
# waits for the page to report the Cookie Store changes it was sent, and closes
# the tab. The tab is opened in the background so the listener fixture page
# stays in the foreground and its page-lifecycle evidence is unaffected. This
# step only makes the page use cookies so the logger has operations to record;
# the verifier is what checks the records.
function Invoke-CookieFixture {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DevToolsBase,

        [Parameter(Mandatory = $true)]
        [string] $FixtureUri,

        [Parameter(Mandatory = $true)]
        [string] $CookieValue
    )

    $version = ConvertFrom-Json (
        Invoke-WebRequest `
            -Uri "$DevToolsBase/json/version" `
            -UseBasicParsing `
            -TimeoutSec 5
    ).Content
    $browserSocket = Get-CdpProperty $version "webSocketDebuggerUrl"
    if ($browserSocket -isnot [string] -or $browserSocket.Length -eq 0) {
        throw "The DevTools version reply reported no browser endpoint."
    }

    $browserSession = $null
    $pageSession = $null
    $targetId = $null
    try {
        $browserSession = New-CdpSession $browserSocket
        $created = Invoke-CdpCommand $browserSession "Target.createTarget" @{
            url = $FixtureUri
            background = $true
        }
        $targetId = [string](Get-CdpProperty $created "targetId")
        if ([string]::IsNullOrWhiteSpace($targetId)) {
            throw "Opening the cookie logging fixture returned no target."
        }

        $target = $null
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline) {
            $candidates = @()
            try {
                $candidates = @(
                    Select-CdpFixtureTarget `
                        (Get-CdpTargetList $DevToolsBase) `
                        $FixtureUri `
                        "Cookie logging fixture ready" |
                        Where-Object { (Get-CdpProperty $_ "id") -eq $targetId }
                )
            }
            catch {
                $candidates = @()
            }
            if ($candidates.Count -eq 1) {
                $target = $candidates[0]
                break
            }
            Start-Sleep -Milliseconds 100
        }
        if (-not $target) {
            throw (
                "The cookie logging fixture did not report readiness in the " +
                "DevTools target list."
            )
        }

        $pageSession = New-CdpSession $target.webSocketDebuggerUrl
        $argument = ConvertTo-Json $CookieValue -Compress
        $run = Invoke-CdpCommand $pageSession "Runtime.evaluate" @{
            expression = "runCookieFixture($argument)"
            awaitPromise = $true
            returnByValue = $true
        }
        $failure = Get-CdpProperty $run "exceptionDetails"
        if ($failure) {
            $description = Get-CdpProperty (
                Get-CdpProperty $failure "exception"
            ) "description"
            throw (
                "The cookie logging fixture failed: $($failure.text) " +
                "$description"
            )
        }
        $report = ConvertFrom-Json (
            [string](Get-CdpProperty $run.result "value")
        )
        if ((Get-CdpProperty $report "outcome") -ne "completed") {
            throw "The cookie logging fixture did not report completion."
        }

        # Cookie Store change events arrive on their own channel, so they can
        # follow the delete call's resolution. The page is polled for them
        # rather than asked to wait on a timer of its own.
        $expectedChanges = @(
            "changed:a11y_recorder_store",
            "deleted:a11y_recorder_store"
        )
        $changes = @()
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            $reported = Invoke-CdpCommand $pageSession "Runtime.evaluate" @{
                expression = "JSON.stringify(window.cookieFixtureChanges)"
                returnByValue = $true
            }
            # Windows PowerShell 5.1 returns a parsed JSON array as one object
            # rather than enumerating it, so the parsed value is assigned first
            # and then enumerated into a flat list of change strings.
            $parsedChanges = ConvertFrom-Json (
                [string](Get-CdpProperty $reported.result "value")
            )
            $changes = @($parsedChanges | ForEach-Object { $_ })
            $missing = @(
                $expectedChanges | Where-Object { $changes -notcontains $_ }
            )
            if ($missing.Count -eq 0) {
                break
            }
            Start-Sleep -Milliseconds 100
        }

        [pscustomobject]@{
            TargetId = $targetId
            DocumentCookieNames = @($report.documentCookieNames)
            CookieStoreChanges = $changes
        }
    }
    finally {
        Close-CdpSession $pageSession
        if ($browserSession -and $targetId) {
            try {
                $null = Invoke-CdpCommand $browserSession "Target.closeTarget" @{
                    targetId = $targetId
                }
            }
            catch {
                Write-Host (
                    "Closing the cookie logging fixture tab failed: " +
                    "$($_.Exception.Message)"
                )
            }
        }
        Close-CdpSession $browserSession
    }
}

# The page the interaction logging fixture serves. Its functions move focus,
# set text-control values and a selection, and set an active descendant by
# element reflection, so the capture holds each kind of interaction-state
# record. Key presses and typed text are sent through DevTools input commands,
# which Blink handles as user input rather than as script. The page schedules
# no timers and registers no listeners.
$interactionFixturePage = @'
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Interaction logging fixture loading</title>
</head>
<body>
<p>Interaction logging fixture.</p>
<button id="first" type="button">First</button>
<input id="field" type="text" aria-label="Field">
<textarea id="notes" aria-label="Notes"></textarea>
<div id="choices" role="listbox" tabindex="0" aria-label="Choices">
<div id="choice-one" role="option">One</div>
<div id="choice-two" role="option">Two</div>
</div>
<script>
window.interactionFixture = {
  focusFirst: function () {
    document.getElementById("first").focus({ preventScroll: true });
    return document.activeElement.id;
  },
  activeId: function () {
    return document.activeElement ? document.activeElement.id : "";
  },
  fieldValue: function () {
    return document.getElementById("field").value;
  },
  setValues: function () {
    const field = document.getElementById("field");
    const notes = document.getElementById("notes");
    field.value = "set by script";
    notes.value = "notes set by script";
    notes.focus();
    notes.setSelectionRange(1, 4, "forward");
    return JSON.stringify({
      active: document.activeElement.id,
      start: notes.selectionStart,
      end: notes.selectionEnd
    });
  },
  notesValue: function () {
    return document.getElementById("notes").value;
  },
  setActiveDescendant: function () {
    const choices = document.getElementById("choices");
    choices.ariaActiveDescendantElement =
        document.getElementById("choice-two");
    choices.focus();
    const focused = document.activeElement.id;
    document.activeElement.blur();
    return JSON.stringify({
      focused: focused,
      afterBlur: document.activeElement === document.body ? "body" : "other"
    });
  }
};
document.title = "Interaction logging fixture ready";
</script>
</body>
</html>
'@

# Runs one of the interaction fixture page's functions and returns its value.
function Invoke-InteractionFixtureCall {
    param(
        [Parameter(Mandatory = $true)]
        $Session,

        [Parameter(Mandatory = $true)]
        [string] $Expression
    )

    $run = Invoke-CdpCommand $Session "Runtime.evaluate" @{
        expression = $Expression
        returnByValue = $true
    }
    $failure = Get-CdpProperty $run "exceptionDetails"
    if ($failure) {
        $description = Get-CdpProperty (
            Get-CdpProperty $failure "exception"
        ) "description"
        throw (
            "The interaction logging fixture failed at ${Expression}: " +
            "$($failure.text) $description"
        )
    }
    Get-CdpProperty $run.result "value"
}

# Opens the interaction logging fixture in a foreground tab, moves focus by
# script and by the Tab key, types into a text field and a textarea, sets values
# and a selection by script, sets an active descendant by element reflection,
# and closes the tab. DevTools key and text input reaches a page only once its
# widget has painted, and a tab opened in the background never paints, so the
# tab must be in the foreground. The caller runs this step after the listener
# fixture page has been hidden behind the background target, so bringing this
# tab forward hides the background target rather than the fixture page. The
# background target is activated again before this tab closes, so closing it
# cannot make the fixture page visible. This step only makes the page change
# interaction state so the logger has changes to record; the verifier is what
# checks the records.
function Invoke-InteractionFixture {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DevToolsBase,

        [Parameter(Mandatory = $true)]
        [string] $FixtureUri,

        # The tab to activate before the fixture tab closes.
        [Parameter(Mandatory = $true)]
        [string] $ReturnTargetId
    )

    $version = ConvertFrom-Json (
        Invoke-WebRequest `
            -Uri "$DevToolsBase/json/version" `
            -UseBasicParsing `
            -TimeoutSec 5
    ).Content
    $browserSocket = Get-CdpProperty $version "webSocketDebuggerUrl"
    if ($browserSocket -isnot [string] -or $browserSocket.Length -eq 0) {
        throw "The DevTools version reply reported no browser endpoint."
    }

    $browserSession = $null
    $pageSession = $null
    $targetId = $null
    try {
        $browserSession = New-CdpSession $browserSocket
        $created = Invoke-CdpCommand $browserSession "Target.createTarget" @{
            url = $FixtureUri
            background = $false
        }
        $targetId = [string](Get-CdpProperty $created "targetId")
        if ([string]::IsNullOrWhiteSpace($targetId)) {
            throw "Opening the interaction logging fixture returned no target."
        }

        $target = $null
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline) {
            $candidates = @()
            try {
                $candidates = @(
                    Select-CdpFixtureTarget `
                        (Get-CdpTargetList $DevToolsBase) `
                        $FixtureUri `
                        "Interaction logging fixture ready" |
                        Where-Object { (Get-CdpProperty $_ "id") -eq $targetId }
                )
            }
            catch {
                $candidates = @()
            }
            if ($candidates.Count -eq 1) {
                $target = $candidates[0]
                break
            }
            Start-Sleep -Milliseconds 100
        }
        if (-not $target) {
            throw (
                "The interaction logging fixture did not report readiness in " +
                "the DevTools target list."
            )
        }

        $pageSession = New-CdpSession $target.webSocketDebuggerUrl
        $activated = Invoke-WebRequest `
            -Method Put `
            -Uri "$DevToolsBase/json/activate/$targetId" `
            -UseBasicParsing `
            -TimeoutSec 5
        if ($activated.StatusCode -ne 200) {
            throw (
                "Activating the interaction logging fixture reported status " +
                "$($activated.StatusCode)."
            )
        }
        $null = Invoke-CdpCommand $pageSession "Page.bringToFront" $null
        $visibility = ""
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline) {
            $visibility = [string](Invoke-InteractionFixtureCall $pageSession `
                "String(document.visibilityState)")
            if ($visibility -eq "visible") {
                break
            }
            Start-Sleep -Milliseconds 250
        }
        if ($visibility -ne "visible") {
            throw (
                "The interaction logging fixture reported visibility " +
                "'$visibility' after being activated, so DevTools input " +
                "cannot reach it. A window that is minimized, occluded, or " +
                "on an inactive desktop produces this."
            )
        }
        # A visible page paints on its next frame. Waiting for two animation
        # frames ensures the widget has painted before input is sent.
        $null = Invoke-CdpCommand $pageSession "Runtime.evaluate" @{
            expression = (
                "new Promise(r => requestAnimationFrame(() => " +
                "requestAnimationFrame(() => r(true))))"
            )
            awaitPromise = $true
            returnByValue = $true
        }
        $steps = [ordered]@{}

        $steps.ScriptFocus = Invoke-InteractionFixtureCall $pageSession `
            "interactionFixture.focusFirst()"
        if ($steps.ScriptFocus -ne "first") {
            throw "The fixture's script focus left '$($steps.ScriptFocus)' focused."
        }

        foreach ($type in @("rawKeyDown", "keyUp")) {
            $null = Invoke-CdpCommand $pageSession "Input.dispatchKeyEvent" @{
                type = $type
                key = "Tab"
                code = "Tab"
                windowsVirtualKeyCode = 9
                nativeVirtualKeyCode = 9
            }
        }
        $steps.TabFocus = Invoke-InteractionFixtureCall $pageSession `
            "interactionFixture.activeId()"
        if ($steps.TabFocus -ne "field") {
            throw "The Tab key left '$($steps.TabFocus)' focused, not the field."
        }

        $null = Invoke-CdpCommand $pageSession "Input.insertText" @{
            text = "typed"
        }
        $steps.TypedField = Invoke-InteractionFixtureCall $pageSession `
            "interactionFixture.fieldValue()"
        if ($steps.TypedField -ne "typed") {
            throw "Typing into the field left the value '$($steps.TypedField)'."
        }

        $steps.ScriptValues = Invoke-InteractionFixtureCall $pageSession `
            "interactionFixture.setValues()"
        $values = ConvertFrom-Json ([string] $steps.ScriptValues)
        if ($values.active -ne "notes" -or $values.start -ne 1 -or
            $values.end -ne 4) {
            throw "The fixture's value and selection step reported $($steps.ScriptValues)."
        }

        $null = Invoke-CdpCommand $pageSession "Input.insertText" @{
            text = "X"
        }
        $steps.TypedNotes = Invoke-InteractionFixtureCall $pageSession `
            "interactionFixture.notesValue()"
        if ($steps.TypedNotes -ne "nXs set by script") {
            throw "Typing into the textarea left the value '$($steps.TypedNotes)'."
        }

        $steps.ActiveDescendant = Invoke-InteractionFixtureCall $pageSession `
            "interactionFixture.setActiveDescendant()"
        $descendant = ConvertFrom-Json ([string] $steps.ActiveDescendant)
        if ($descendant.focused -ne "choices" -or
            $descendant.afterBlur -ne "body") {
            throw "The fixture's active descendant step reported $($steps.ActiveDescendant)."
        }

        [pscustomobject]@{
            TargetId = $targetId
            Steps = $steps
        }
    }
    finally {
        Close-CdpSession $pageSession
        try {
            $null = Invoke-WebRequest `
                -Method Put `
                -Uri "$DevToolsBase/json/activate/$ReturnTargetId" `
                -UseBasicParsing `
                -TimeoutSec 5
        }
        catch {
            Write-Host (
                "Activating the background target again failed: " +
                "$($_.Exception.Message)"
            )
        }
        if ($browserSession -and $targetId) {
            try {
                $null = Invoke-CdpCommand $browserSession "Target.closeTarget" @{
                    targetId = $targetId
                }
            }
            catch {
                Write-Host (
                    "Closing the interaction logging fixture tab failed: " +
                    "$($_.Exception.Message)"
                )
            }
        }
        Close-CdpSession $browserSession
    }
}

# The page the layout logging fixture serves. It holds a paragraph of text, a
# box with a fixed size and color, and an element with display: none. Its
# functions widen the box, which needs a new layout, and change only the box's
# color, which needs a style update without a size change. Each function waits
# two animation frames after its change and then reports what the page sees.
# The page schedules no timers and registers no listeners.
$layoutFixturePage = @'
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Layout logging fixture loading</title>
<style>
#layout-box { width: 200px; height: 50px; color: rgb(0, 0, 128); }
#layout-hidden { display: none; }
</style>
</head>
<body>
<p>Layout logging fixture.</p>
<div id="layout-box">Box</div>
<span id="layout-hidden">Hidden</span>
<script>
function layoutFixtureFrames() {
  return new Promise(function (resolve) {
    requestAnimationFrame(function () {
      requestAnimationFrame(function () { resolve(); });
    });
  });
}
function layoutFixtureReport() {
  const box = document.getElementById("layout-box");
  const rect = box.getBoundingClientRect();
  return JSON.stringify({
    x: rect.x,
    y: rect.y,
    width: rect.width,
    height: rect.height,
    innerWidth: window.innerWidth,
    innerHeight: window.innerHeight,
    color: getComputedStyle(box).color
  });
}
window.layoutFixture = {
  settle: function () {
    return layoutFixtureFrames().then(layoutFixtureReport);
  },
  widen: function () {
    document.getElementById("layout-box").style.width = "320px";
    return layoutFixtureFrames().then(layoutFixtureReport);
  },
  recolor: function () {
    document.getElementById("layout-box").style.color = "rgb(128, 0, 0)";
    return layoutFixtureFrames().then(layoutFixtureReport);
  }
};
document.title = "Layout logging fixture ready";
</script>
</body>
</html>
'@

# Runs one of the layout fixture page's functions, each of which returns a
# promise, and returns the value the promise resolves to.
function Invoke-LayoutFixtureCall {
    param(
        [Parameter(Mandatory = $true)]
        $Session,

        [Parameter(Mandatory = $true)]
        [string] $Expression
    )

    $run = Invoke-CdpCommand $Session "Runtime.evaluate" @{
        expression = $Expression
        awaitPromise = $true
        returnByValue = $true
    }
    $failure = Get-CdpProperty $run "exceptionDetails"
    if ($failure) {
        $description = Get-CdpProperty (
            Get-CdpProperty $failure "exception"
        ) "description"
        throw (
            "The layout logging fixture failed at ${Expression}: " +
            "$($failure.text) $description"
        )
    }
    Get-CdpProperty $run.result "value"
}

# Opens the layout logging fixture in a foreground tab, lets it paint, widens
# one element, changes only the color of the same element, and closes the tab.
# The recorder emits layout checkpoints only for a document whose rendering
# update reaches the paint-clean state, and a tab opened in the background never
# paints, so the tab must be in the foreground. Each page function waits two
# animation frames after its change, so the change has been through a rendering
# update before the function returns, and then reports the element's rectangle,
# the viewport size, and the element's color as the page sees them. The caller
# runs this step after the interaction fixture, with the listener fixture page
# still hidden behind the background target, which is activated again before
# this tab closes. This step only makes the page change layout and style so the
# logger has updates to record; the verifier is what checks the records.
function Invoke-LayoutFixture {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DevToolsBase,

        [Parameter(Mandatory = $true)]
        [string] $FixtureUri,

        # The tab to activate before the fixture tab closes.
        [Parameter(Mandatory = $true)]
        [string] $ReturnTargetId
    )

    $version = ConvertFrom-Json (
        Invoke-WebRequest `
            -Uri "$DevToolsBase/json/version" `
            -UseBasicParsing `
            -TimeoutSec 5
    ).Content
    $browserSocket = Get-CdpProperty $version "webSocketDebuggerUrl"
    if ($browserSocket -isnot [string] -or $browserSocket.Length -eq 0) {
        throw "The DevTools version reply reported no browser endpoint."
    }

    $browserSession = $null
    $pageSession = $null
    $targetId = $null
    try {
        $browserSession = New-CdpSession $browserSocket
        $created = Invoke-CdpCommand $browserSession "Target.createTarget" @{
            url = $FixtureUri
            background = $false
        }
        $targetId = [string](Get-CdpProperty $created "targetId")
        if ([string]::IsNullOrWhiteSpace($targetId)) {
            throw "Opening the layout logging fixture returned no target."
        }

        $target = $null
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline) {
            $candidates = @()
            try {
                $candidates = @(
                    Select-CdpFixtureTarget `
                        (Get-CdpTargetList $DevToolsBase) `
                        $FixtureUri `
                        "Layout logging fixture ready" |
                        Where-Object { (Get-CdpProperty $_ "id") -eq $targetId }
                )
            }
            catch {
                $candidates = @()
            }
            if ($candidates.Count -eq 1) {
                $target = $candidates[0]
                break
            }
            Start-Sleep -Milliseconds 100
        }
        if (-not $target) {
            throw (
                "The layout logging fixture did not report readiness in " +
                "the DevTools target list."
            )
        }

        $pageSession = New-CdpSession $target.webSocketDebuggerUrl
        $activated = Invoke-WebRequest `
            -Method Put `
            -Uri "$DevToolsBase/json/activate/$targetId" `
            -UseBasicParsing `
            -TimeoutSec 5
        if ($activated.StatusCode -ne 200) {
            throw (
                "Activating the layout logging fixture reported status " +
                "$($activated.StatusCode)."
            )
        }
        $null = Invoke-CdpCommand $pageSession "Page.bringToFront" $null
        $visibility = ""
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline) {
            $visibility = [string](Invoke-LayoutFixtureCall $pageSession `
                "String(document.visibilityState)")
            if ($visibility -eq "visible") {
                break
            }
            Start-Sleep -Milliseconds 250
        }
        if ($visibility -ne "visible") {
            throw (
                "The layout logging fixture reported visibility " +
                "'$visibility' after being activated, so it cannot paint. " +
                "A window that is minimized, occluded, or " +
                "on an inactive desktop produces this."
            )
        }
        $steps = [ordered]@{}
        foreach ($step in @(
                @{ Name = "Settle"; Call = "layoutFixture.settle()" },
                @{ Name = "Widen"; Call = "layoutFixture.widen()" },
                @{ Name = "Recolor"; Call = "layoutFixture.recolor()" }
            )) {
            $steps[$step.Name] = [string](
                Invoke-LayoutFixtureCall $pageSession $step.Call
            )
        }
        $widened = ConvertFrom-Json $steps.Widen
        $recolored = ConvertFrom-Json $steps.Recolor
        if ($widened.width -ne 320 -or $recolored.color -ne "rgb(128, 0, 0)") {
            throw (
                "The layout logging fixture reported $($steps.Widen) after " +
                "widening and $($steps.Recolor) after recoloring."
            )
        }

        [pscustomobject]@{
            TargetId = $targetId
            Steps = $steps
        }
    }
    finally {
        Close-CdpSession $pageSession
        try {
            $null = Invoke-WebRequest `
                -Method Put `
                -Uri "$DevToolsBase/json/activate/$ReturnTargetId" `
                -UseBasicParsing `
                -TimeoutSec 5
        }
        catch {
            Write-Host (
                "Activating the background target again failed: " +
                "$($_.Exception.Message)"
            )
        }
        if ($browserSession -and $targetId) {
            try {
                $null = Invoke-CdpCommand $browserSession "Target.closeTarget" @{
                    targetId = $targetId
                }
            }
            catch {
                Write-Host (
                    "Closing the layout logging fixture tab failed: " +
                    "$($_.Exception.Message)"
                )
            }
        }
        Close-CdpSession $browserSession
    }
}

# Runs the network logging fixture in a background tab. The tab opens
# /network-start, which redirects to the fixture page, and the fixture function
# is then called with the per-run credential values and the URL of a closed
# loopback port. The page does not need to be painted, so the tab stays in the
# background.
function Invoke-NetworkFixture {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DevToolsBase,

        [Parameter(Mandatory = $true)]
        [string] $StartUri,

        [Parameter(Mandatory = $true)]
        [string] $FixtureUri,

        [Parameter(Mandatory = $true)]
        [hashtable] $Values
    )

    $version = ConvertFrom-Json (
        Invoke-WebRequest `
            -Uri "$DevToolsBase/json/version" `
            -UseBasicParsing `
            -TimeoutSec 5
    ).Content
    $browserSocket = Get-CdpProperty $version "webSocketDebuggerUrl"
    if ($browserSocket -isnot [string] -or $browserSocket.Length -eq 0) {
        throw "The DevTools version reply reported no browser endpoint."
    }

    $browserSession = $null
    $pageSession = $null
    $targetId = $null
    try {
        $browserSession = New-CdpSession $browserSocket
        $created = Invoke-CdpCommand $browserSession "Target.createTarget" @{
            url = $StartUri
            background = $true
        }
        $targetId = [string](Get-CdpProperty $created "targetId")
        if ([string]::IsNullOrWhiteSpace($targetId)) {
            throw "Opening the network logging fixture returned no target."
        }

        $target = $null
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline) {
            $candidates = @()
            try {
                $candidates = @(
                    Select-CdpFixtureTarget `
                        (Get-CdpTargetList $DevToolsBase) `
                        $FixtureUri `
                        "Network logging fixture ready" |
                        Where-Object { (Get-CdpProperty $_ "id") -eq $targetId }
                )
            }
            catch {
                $candidates = @()
            }
            if ($candidates.Count -eq 1) {
                $target = $candidates[0]
                break
            }
            Start-Sleep -Milliseconds 100
        }
        if (-not $target) {
            throw (
                "The network logging fixture did not report readiness in " +
                "the DevTools target list."
            )
        }

        $pageSession = New-CdpSession $target.webSocketDebuggerUrl
        $argument = ConvertTo-Json -Compress -InputObject (
            [pscustomobject] $Values
        )
        $run = Invoke-CdpCommand $pageSession "Runtime.evaluate" @{
            expression = "runNetworkFixture($argument)"
            awaitPromise = $true
            returnByValue = $true
        }
        $failure = Get-CdpProperty $run "exceptionDetails"
        if ($failure) {
            $description = Get-CdpProperty (
                Get-CdpProperty $failure "exception"
            ) "description"
            throw (
                "The network logging fixture failed: $($failure.text) " +
                "$description"
            )
        }
        $reportText = [string](Get-CdpProperty $run.result "value")
        $report = ConvertFrom-Json $reportText
        if ((Get-CdpProperty $report "outcome") -ne "completed") {
            throw "The network logging fixture did not report completion."
        }

        [pscustomobject]@{
            TargetId = $targetId
            Report = $reportText
        }
    }
    finally {
        Close-CdpSession $pageSession
        if ($browserSession -and $targetId) {
            try {
                $null = Invoke-CdpCommand $browserSession "Target.closeTarget" @{
                    targetId = $targetId
                }
            }
            catch {
                Write-Host (
                    "Closing the network logging fixture tab failed: " +
                    "$($_.Exception.Message)"
                )
            }
        }
        Close-CdpSession $browserSession
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if ($principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )) {
    throw (
        "Run this script in a standard, non-elevated PowerShell window. " +
        "Elevated capture cannot connect to the non-elevated Chromium process."
    )
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$chromiumSource = (
    Resolve-Path -LiteralPath $ChromiumSource
).Path
$depotTools = (Resolve-Path -LiteralPath $DepotTools).Path
$dotnet = (Resolve-Path -LiteralPath $DotnetPath).Path
$python = Get-PythonCommand -DepotToolsPath $depotTools
$autoninja = Join-Path $depotTools "autoninja.bat"
$browser = Join-Path $chromiumSource "out\A11yRecorder\chrome.exe"
$fixture = Join-Path (
    Join-Path $repositoryRoot "tests"
) "fixtures\blink-listener-dispatch.html"
$captureProject = Join-Path (
    Join-Path $repositoryRoot "src"
) "Recorder.CaptureHost\Recorder.CaptureHost.csproj"
$solution = Join-Path $repositoryRoot "windows-a11y-recorder.slnx"
$integrationScript = Join-Path $repositoryRoot "chromium\integrate.py"
$integrationTests = Join-Path $repositoryRoot "chromium\test_integrate.py"
$verifier = Join-Path $PSScriptRoot "Verify-BlinkEvidence.ps1"

foreach ($requiredPath in @(
        $autoninja,
        $fixture,
        $captureProject,
        $solution,
        $integrationScript,
        $integrationTests,
        $verifier
    )) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required file not found: $requiredPath"
    }
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $OutputRoot).Path
$fixtureUri = [Uri]::new($fixture).AbsoluteUri
$bridgeLog = Join-Path $outputRoot "bridge-startup.log"
$chromiumLog = Join-Path $outputRoot "chromium.log"
$debugPortListener = [Net.Sockets.TcpListener]::new(
    [Net.IPAddress]::Loopback,
    0
)
$debugPortListener.Start()
$debugPort = (
    [Net.IPEndPoint] $debugPortListener.LocalEndpoint
).Port
$debugPortListener.Stop()

Invoke-Checked "Testing the Chromium integration script" {
    & $python $integrationTests
}

Invoke-Checked "Applying the Chromium integration" {
    & $python $integrationScript $chromiumSource
}

# Zip timestamps can be ahead of the local system clock and prevent GN from
# regenerating the build. Normalize only files managed by this integration.
$now = Get-Date
$integratedFiles = @(
    (Join-Path $chromiumSource "chrome\app\chrome_main_delegate.cc"),
    (Join-Path $chromiumSource "chrome\BUILD.gn"),
    (
        Join-Path $chromiumSource (
            "content\browser\child_process_launcher_helper.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "content\browser\child_process_launcher_helper_win.cc"
        )
    ),
    (Join-Path $chromiumSource "content\browser\BUILD.gn"),
    (
        Join-Path $chromiumSource (
            "content\renderer\accessibility\render_accessibility_impl.cc"
        )
    ),
    (Join-Path $chromiumSource "content\renderer\BUILD.gn"),
    (
        Join-Path $chromiumSource (
            "content\browser\web_contents\web_contents_impl.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\events\event_target.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\events\event_dispatcher.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\document.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\mutation_observer.h"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\mutation_observer.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\scheduler\dom_timer.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\" +
            "frame_request_callback_collection.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\scheduler\" +
            "scripted_idle_task_controller.cc"
        )
    ),
    (
        Join-Path $chromiumSource "third_party\blink\renderer\core\BUILD.gn"
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\loader\cookie_jar.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\dom\element.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\editing\frame_selection.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\html\forms\html_input_element.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\html\forms\text_field_input_type.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\core\html\forms\html_text_area_element.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\modules\cookie_store\" +
            "cookie_store.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\modules\cookie_store\BUILD.gn"
        )
    ),
    (
        Join-Path $chromiumSource (
            "content\browser\renderer_host\render_frame_host_impl.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "content\browser\renderer_host\navigation_request.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\platform\scheduler\common\" +
            "throttling\task_queue_throttler.h"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\platform\scheduler\common\" +
            "throttling\task_queue_throttler.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\platform\scheduler\main_thread\" +
            "main_thread_task_queue.cc"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\platform\scheduler\main_thread\" +
            "frame_scheduler_impl.h"
        )
    ),
    (
        Join-Path $chromiumSource (
            "third_party\blink\renderer\platform\scheduler\BUILD.gn"
        )
    )
)
$integratedFiles += Get-ChildItem -LiteralPath (
    Join-Path $chromiumSource "chromium\recorder_bridge"
) -File -Recurse |
    Select-Object -ExpandProperty FullName
$integratedFiles | ForEach-Object {
    $integratedPath = [string] $_
    if (-not (Test-Path -LiteralPath $integratedPath -PathType Leaf)) {
        throw "Integrated file not found: $integratedPath"
    }
    (Get-Item -LiteralPath $integratedPath).LastWriteTime = $now
}

Invoke-Checked "Building instrumented Chromium" {
    & $autoninja -C (
        Join-Path $chromiumSource "out\A11yRecorder"
    ) chrome
}

if (-not (Test-Path -LiteralPath $browser -PathType Leaf)) {
    throw "The Chromium build did not produce $browser."
}

Invoke-Checked "Testing the managed recorder" {
    & $dotnet test $solution --configuration Release
}

$sessionsBefore = @(
    Get-ChildItem -LiteralPath $outputRoot -Directory -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
)

Remove-Item -LiteralPath $bridgeLog -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $chromiumLog -Force -ErrorAction SilentlyContinue
$previousBridgeLog = $env:A11Y_RECORDER_BRIDGE_LOG_FILE
$previousChromiumLog = $env:A11Y_RECORDER_CHROMIUM_LOG_FILE
$captureJob = $null
# The value every fixture cookie carries. It is generated per run so the
# verifier can require that no record in the session contains it.
$cookieValue = "a11y-recorder-cookie-value-" + [Guid]::NewGuid().ToString("N")
$cookieServer = $null
$cookieFixtureUri = $null
$interactionFixtureUri = $null
$layoutFixtureUri = $null
$layoutFixtureSteps = $null
# The credential values the network logging fixture sends in request headers
# and receives in a response header. They are generated per run so the verifier
# can require that no record in the session contains them.
$networkValues = @{
    authorization = "a11y-recorder-authorization-" +
        [Guid]::NewGuid().ToString("N")
    apiKey = "a11y-recorder-api-key-" + [Guid]::NewGuid().ToString("N")
    scheme = "a11y-recorder-scheme-" + [Guid]::NewGuid().ToString("N")
    refusedUrl = $null
}
$networkSessionValue = "a11y-recorder-session-" +
    [Guid]::NewGuid().ToString("N")
# A loopback port that was bound and then released, so a fetch to it is
# refused and the network logging fixture records a failed request.
$closedPortListener = [Net.Sockets.TcpListener]::new(
    [Net.IPAddress]::Loopback,
    0
)
$closedPortListener.Start()
$closedPort = ([Net.IPEndPoint] $closedPortListener.LocalEndpoint).Port
$closedPortListener.Stop()
$networkValues.refusedUrl = "http://127.0.0.1:$closedPort/network-refused"
$networkStartUri = $null
$networkFixtureUri = $null
$networkFixtureReport = $null
$env:A11Y_RECORDER_BRIDGE_LOG_FILE = $bridgeLog
$env:A11Y_RECORDER_CHROMIUM_LOG_FILE = $chromiumLog
try {
    $cookieServer = Start-CookieFixtureServer `
        $cookieFixturePage `
        $cookieValue `
        $interactionFixturePage `
        $layoutFixturePage `
        $networkFixturePage `
        $networkFixtureWorker `
        $networkSessionValue
    $cookieFixtureUri = $cookieServer.BaseUri
    $interactionFixtureUri = "$($cookieServer.BaseUri)interaction"
    Write-Host "Serving the cookie logging fixture at $cookieFixtureUri"
    $layoutFixtureUri = "$($cookieServer.BaseUri)layout"
    Write-Host "Serving the interaction logging fixture at $interactionFixtureUri"
    Write-Host "Serving the layout logging fixture at $layoutFixtureUri"
    $networkStartUri = "$($cookieServer.BaseUri)network-start"
    $networkFixtureUri = "$($cookieServer.BaseUri)network"
    Write-Host (
        "Serving the network logging fixture at $networkFixtureUri behind " +
        "$networkStartUri"
    )
    Write-Host "`n== Capturing the deterministic Blink fixture =="
    $captureJob = Start-Job -ScriptBlock {
        param(
            $Dotnet,
            $CaptureProject,
            $OutputRoot,
            $DurationSeconds,
            $Browser,
            $FixtureUri,
            $DebugPort,
            $BridgeLog,
            $ChromiumLog
        )
        $env:A11Y_RECORDER_BRIDGE_LOG_FILE = $BridgeLog
        $env:A11Y_RECORDER_CHROMIUM_LOG_FILE = $ChromiumLog
        & $Dotnet run `
            --project $CaptureProject `
            --configuration Release `
            -- `
            --output $OutputRoot `
            --duration-seconds $DurationSeconds `
            --browser-path $Browser `
            --browser-url $FixtureUri `
            --browser-remote-debugging-port $DebugPort
        [pscustomobject]@{
            CaptureExitCode = $LASTEXITCODE
        }
    } -ArgumentList @(
        $dotnet,
        $captureProject,
        $outputRoot,
        $DurationSeconds,
        $browser,
        $fixtureUri,
        $debugPort,
        $bridgeLog,
        $chromiumLog
    )

    $devToolsBase = "http://127.0.0.1:$debugPort"
    $fixtureTarget = $null
    $debugDeadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $debugDeadline) {
        if ($captureJob.State -eq "Failed") {
            break
        }
        # Any failure here means the debugging endpoint is not serving a target
        # list yet, which is expected until Chromium has started listening.
        $candidates = @()
        try {
            $candidates = @(
                Select-CdpFixtureTarget `
                    (Get-CdpTargetList $devToolsBase) `
                    $fixtureUri `
                    "Blink listener and dispatch fixture ready"
            )
        }
        catch {
            $candidates = @()
        }
        if ($candidates.Count -gt 1) {
            Stop-Job $captureJob -ErrorAction SilentlyContinue
            throw (
                "$($candidates.Count) DevTools targets match the fixture, so " +
                "the fixture page cannot be identified."
            )
        }
        if ($candidates.Count -eq 1) {
            $fixtureTarget = $candidates[0]
            break
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $fixtureTarget) {
        Stop-Job $captureJob -ErrorAction SilentlyContinue
        throw (
            "The lifecycle fixture did not report readiness in the DevTools " +
            "target list."
        )
    }

    # A registration made from an isolated world is the only way this fixture
    # can produce a listener record for a world other than the main world,
    # because a page's own script always runs in the main world. The world is
    # created over the same debugging endpoint the readiness poll used.
    $isolatedWorldSession = $null
    try {
        $isolatedWorldSession = New-CdpSession $fixtureTarget.webSocketDebuggerUrl
        $isolatedWorld = Add-IsolatedWorldListener `
            $isolatedWorldSession `
            "A11yRecorderValidationWorld"
        Write-Host (
            "Registered an isolated-world listener in frame " +
            "$($isolatedWorld.FrameId), execution context " +
            "$($isolatedWorld.ExecutionContextId)."
        )
    }
    catch {
        Stop-Job $captureJob -ErrorAction SilentlyContinue
        throw
    }
    finally {
        Close-CdpSession $isolatedWorldSession
    }

    # The cookie logging fixture makes a second page use document.cookie, the
    # Cookie Store API, and Set-Cookie response headers so the capture holds
    # cookie operations for the logger to record.
    try {
        $cookieRun = Invoke-CookieFixture `
            $devToolsBase `
            $cookieFixtureUri `
            $cookieValue
        Write-Host (
            "Ran the cookie logging fixture in background target " +
            "$($cookieRun.TargetId). The page read the cookie names " +
            "$($cookieRun.DocumentCookieNames -join ', ') and observed the " +
            "Cookie Store changes $($cookieRun.CookieStoreChanges -join ', ')."
        )
    }
    catch {
        Stop-Job $captureJob -ErrorAction SilentlyContinue
        throw
    }

    # The page-lifecycle timers are scheduled here, immediately before the page
    # is hidden, so the schedule is recorded while the page reports visible and
    # the callbacks enter while it is hidden. Scheduling them at parse time made
    # both facts depend on when Chromium happened to show the fixture window.
    $lifecycleSession = $null
    try {
        $lifecycleSession = New-CdpSession $fixtureTarget.webSocketDebuggerUrl
        $fixtureTargetId = Get-CdpProperty $fixtureTarget "id"
        if ($fixtureTargetId -isnot [string] -or
                $fixtureTargetId.Length -eq 0) {
            throw "The fixture target list reported no target identifier."
        }
        $lifecycleState = Start-FixtureLifecycleEvidence `
            $lifecycleSession `
            $devToolsBase `
            $fixtureTargetId
        Write-Host (
            "Scheduled the fixture's page-lifecycle timers while the page " +
            "reported $lifecycleState."
        )
    }
    catch {
        Stop-Job $captureJob -ErrorAction SilentlyContinue
        throw
    }
    finally {
        Close-CdpSession $lifecycleSession
    }

    $backgroundTargetId = New-CdpBackgroundTarget $devToolsBase
    Write-Host (
        "Activated background target $backgroundTargetId so the fixture page " +
        "becomes hidden."
    )

    # The interaction logging fixture makes a third page change focus,
    # selection, text-control values, and an element-reflected active
    # descendant so the capture holds interaction-state changes for the logger
    # to record. It runs once the fixture page is hidden, because its tab must
    # be in the foreground for DevTools input to reach it.
    try {
        $interactionRun = Invoke-InteractionFixture `
            $devToolsBase `
            $interactionFixtureUri `
            $backgroundTargetId
        Write-Host (
            "Ran the interaction logging fixture in foreground target " +
            "$($interactionRun.TargetId). The page reported: " +
            (@(
                $interactionRun.Steps.GetEnumerator() |
                    ForEach-Object { "$($_.Key)=$($_.Value)" }
            ) -join "; ")
        )
    }
    catch {
        Stop-Job $captureJob -ErrorAction SilentlyContinue
        throw
    }

    # The layout logging fixture makes a fourth page widen an element and then
    # change only its color, so the capture holds layout checkpoints before and
    # after each change for the logger to record. It runs in the foreground for
    # the same reason as the interaction fixture: only a painted document
    # produces layout checkpoints.
    try {
        $layoutRun = Invoke-LayoutFixture `
            $devToolsBase `
            $layoutFixtureUri `
            $backgroundTargetId
        $layoutFixtureSteps = ConvertTo-Json -Compress -InputObject (
            [pscustomobject] $layoutRun.Steps
        )
        Write-Host (
            "Ran the layout logging fixture in foreground target " +
            "$($layoutRun.TargetId). The page reported: $layoutFixtureSteps"
        )
    }
    catch {
        Stop-Job $captureJob -ErrorAction SilentlyContinue
        throw
    }

    # The network logging fixture makes a fifth page send fetches with
    # credential-bearing headers, follow a redirect, fail a request, reuse a
    # cached script, and fetch from a dedicated worker, so the capture holds
    # network metadata records for the logger to emit.
    try {
        $networkRun = Invoke-NetworkFixture `
            $devToolsBase `
            $networkStartUri `
            $networkFixtureUri `
            $networkValues
        $networkFixtureReport = $networkRun.Report
        Write-Host (
            "Ran the network logging fixture in background target " +
            "$($networkRun.TargetId). The page reported: $networkFixtureReport"
        )
    }
    catch {
        Stop-Job $captureJob -ErrorAction SilentlyContinue
        throw
    }

    Wait-Job $captureJob | Out-Null
    $captureErrors = @()
    $captureOutput = @(
        Receive-Job `
            $captureJob `
            -ErrorAction SilentlyContinue `
            -ErrorVariable +captureErrors
    )
    # A job's error stream can carry a record that reports nothing. A blank line
    # the browser wrote to its standard error becomes a native-command error
    # record, and because an error record cannot carry an empty message, the
    # remoting wrapper substitutes the record's own category line, so the record
    # arrives with the message "NotSpecified: (:String) [], RemoteException".
    # That is not a blank message and reads like a failure in a run that passed,
    # so a record whose message is blank, or is its own category line, is
    # counted rather than printed, and the count is reported so nothing is
    # dropped without being stated.
    $emptyCaptureErrors = 0
    $captureErrors |
        ForEach-Object {
            $errorText = ""
            $categoryText = ""
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                $errorText = [string] $_.Exception.Message
                $categoryText = [string] $_.CategoryInfo
            }
            else {
                $errorText = [string] $_
            }
            if ([string]::IsNullOrWhiteSpace($errorText) -or
                $errorText -eq $categoryText) {
                ++$emptyCaptureErrors
                return
            }
            Write-Host $errorText
        }
    if ($emptyCaptureErrors -gt 0) {
        Write-Host (
            "The capture job's error stream carried $emptyCaptureErrors " +
            "record(s) that report nothing, which are not failures."
        )
    }
    $captureResult = $captureOutput |
        Where-Object {
            $_.PSObject.Properties.Name -contains "CaptureExitCode"
        } |
        Select-Object -Last 1
    $captureOutput |
        Where-Object {
            $_.PSObject.Properties.Name -notcontains "CaptureExitCode"
        } |
        ForEach-Object { Write-Host $_ }
    if (-not $captureResult) {
        throw "The capture job did not report an exit code."
    }
    if ($captureResult.CaptureExitCode -ne 0) {
        throw (
            "Capturing the deterministic Blink fixture failed with exit " +
            "code $($captureResult.CaptureExitCode)."
        )
    }
}
finally {
    if ($captureJob) {
        Remove-Job $captureJob -Force -ErrorAction SilentlyContinue
    }
    Stop-CookieFixtureServer $cookieServer
    if ($null -eq $previousBridgeLog) {
        Remove-Item `
            Env:A11Y_RECORDER_BRIDGE_LOG_FILE `
            -ErrorAction SilentlyContinue
    }
    else {
        $env:A11Y_RECORDER_BRIDGE_LOG_FILE = $previousBridgeLog
    }
    if ($null -eq $previousChromiumLog) {
        Remove-Item `
            Env:A11Y_RECORDER_CHROMIUM_LOG_FILE `
            -ErrorAction SilentlyContinue
    }
    else {
        $env:A11Y_RECORDER_CHROMIUM_LOG_FILE = $previousChromiumLog
    }
}

$session = Get-ChildItem -LiteralPath $outputRoot -Directory |
    Where-Object { $_.FullName -notin $sessionsBefore } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $session) {
    throw "Capture completed but no new session directory was found."
}

$validationPath = Join-Path (
    $session.FullName
) "diagnostics\archive-validation.json"
if (-not (Test-Path -LiteralPath $validationPath -PathType Leaf)) {
    throw "The session does not contain an archive-validation report."
}
$validation = Get-Content -LiteralPath $validationPath -Raw |
    ConvertFrom-Json
if (-not $validation.isValid) {
    $errors = @(
        $validation.issues |
            Where-Object { $_.severity -eq "Error" } |
            ForEach-Object { "$($_.code) at $($_.path)" }
    )
    throw "Archive validation failed: $($errors -join '; ')"
}

try {
    & $verifier `
        -SessionPath $session.FullName `
        -CookieFixtureUri $cookieFixtureUri `
        -CookieValue $cookieValue `
        -InteractionFixtureUri $interactionFixtureUri `
        -LayoutFixtureUri $layoutFixtureUri `
        -LayoutFixtureSteps $layoutFixtureSteps `
        -NetworkFixtureUri $networkFixtureUri `
        -NetworkStartUri $networkStartUri `
        -NetworkRefusedUri $networkValues.refusedUrl `
        -NetworkFixtureReport $networkFixtureReport `
        -NetworkSecretValues @(
            $networkValues.authorization,
            $networkValues.apiKey,
            $networkValues.scheme,
            $networkSessionValue
        )
}
catch {
    if (Test-Path -LiteralPath $bridgeLog -PathType Leaf) {
        Write-Host "`nNative recorder bridge trace:"
        Get-Content -LiteralPath $bridgeLog
    }
    if (Test-Path -LiteralPath $chromiumLog -PathType Leaf) {
        Write-Host "`nChromium log tail:"
        Get-Content -LiteralPath $chromiumLog -Tail 250
    }
    throw
}

# A process whose bridge failed to initialize exits, so its evidence is missing
# from the archive and nothing else in the run reports that it existed. The
# archive can still validate and the verifier can still pass, so the bridge
# trace is read here rather than only printed. A utility process reporting that
# it received no bootstrap handle is the expected path for a process with no
# evidence hooks and is not a failure.
$bridgeLines = @()
if (Test-Path -LiteralPath $bridgeLog -PathType Leaf) {
    $bridgeLines = @(Get-Content -LiteralPath $bridgeLog)
}
$bridgeFailures = @(
    $bridgeLines |
        Where-Object {
            $_ -match "bridge pipe connection failed" -or
            $_ -match "bridge initialization failed" -or
            $_ -match "bootstrap parsing failed" -or
            $_ -match "bootstrap read failed"
        }
)
# An evidence write that failed after the recorder closed its end of the pipe is
# an ordinary shutdown race rather than lost evidence, so these are counted and
# reported instead of failing the run.
$bridgeWriteFailures = @(
    $bridgeLines | Where-Object { $_ -match "evidence write failed" }
).Count
if ($bridgeFailures.Count -gt 0) {
    Write-Host "`nNative recorder bridge trace:"
    Get-Content -LiteralPath $bridgeLog
    throw (
        "$($bridgeFailures.Count) process(es) reported a recorder bridge " +
        "failure, so their evidence is missing from this session: " +
        ($bridgeFailures -join "; ")
    )
}
$bridgeConnectWaits = @(
    $bridgeLines | Where-Object { $_ -match "waited for a free pipe instance" }
).Count

$networkServiceCrashes = @(
    Select-String `
        -LiteralPath $chromiumLog `
        -Pattern "Network service crashed or was terminated" `
        -ErrorAction SilentlyContinue
).Count
if ($networkServiceCrashes -gt 0) {
    throw (
        "Chromium reported $networkServiceCrashes network-service crash(es). " +
        "The recorder must not destabilize unrelated utility processes."
    )
}

# Evidence the recorder lost is now stated by an omission record instead of by a
# silent gap, so a run that lost evidence is reported as a failed run rather
# than passing quietly. The manifest count covers records the event sink
# refused. An omission whose reason is not a lost record, such as a rejected
# connection, is reported without failing the run.
$lossReasons = @(
    "browser-evidence-write-failed",
    "browser-evidence-sink-refused"
)
$manifestPath = Join-Path $session.FullName "manifest.json"
$sinkRefusedEvents = 0
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $sinkRefusedEvents = [int] $manifest.droppedEventCount
}
$omissionLines = @(
    Select-String `
        -LiteralPath (Join-Path $session.FullName "events.ndjson") `
        -Pattern "collector-omission" `
        -SimpleMatch `
        -ErrorAction SilentlyContinue
)
$omittedRecords = 0
$lossOmissionReasons = @()
$otherOmissionRecords = 0
foreach ($omissionLine in $omissionLines) {
    $omission = $omissionLine.Line | ConvertFrom-Json
    if ($omission.channel -notlike "browser.*") {
        continue
    }
    $reason = $omission.payload.reason
    if ($lossReasons -notcontains $reason) {
        $otherOmissionRecords++
        continue
    }
    $count = 1
    if ($omission.payload.PSObject.Properties.Name -contains "count") {
        $count = [int] $omission.payload.count
    }
    $omittedRecords += $count
    $lossOmissionReasons += $reason
}
$lossOmissionReasons = @($lossOmissionReasons | Sort-Object -Unique)
if ($omittedRecords -gt 0 -or $sinkRefusedEvents -gt 0) {
    throw (
        "This run lost evidence: $omittedRecords record(s) reported as " +
        "omitted and $sinkRefusedEvents refused by the event sink. " +
        "Reported reasons: " + ($lossOmissionReasons -join "; ")
    )
}

Write-Host "`nBlink validation completed successfully."
Write-Host "SESSION_PATH=$($session.FullName)"
Write-Host "BRIDGE_LOG=$bridgeLog"
Write-Host "CHROMIUM_LOG=$chromiumLog"
Write-Host "ARCHIVE_VALID=$($validation.isValid)"
Write-Host "EVENTS_VALIDATED=$($validation.eventsValidated)"
Write-Host "ARTIFACTS_VALIDATED=$($validation.artifactsValidated)"
Write-Host "NETWORK_SERVICE_CRASHES=$networkServiceCrashes"
Write-Host "BRIDGE_CONNECT_WAITS=$bridgeConnectWaits"
Write-Host "BRIDGE_WRITE_FAILURES=$bridgeWriteFailures"
Write-Host "BRIDGE_INITIALIZATION_FAILURES=0"
Write-Host "OMITTED_EVIDENCE_RECORDS=$omittedRecords"
Write-Host "SINK_REFUSED_EVENTS=$sinkRefusedEvents"
Write-Host "OTHER_OMISSION_RECORDS=$otherOmissionRecords"

[CmdletBinding()]
param(
    # The instrumented Chromium build. Run-BlinkValidation.ps1 builds it from
    # the chromium directory of the same revision, and this script does not
    # rebuild it. The recorder refuses a build whose protocol version differs.
    [string] $ChromiumPath = (
        Join-Path $env:USERPROFILE "chromium-dev\chromium\src\out\A11yRecorder\chrome.exe"
    ),

    [string] $OutputRoot = (
        Join-Path $env:PUBLIC "Documents\A11yRecorderAppSessionValidation"
    ),

    [string] $DotnetPath = (
        Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    )
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

    # Native tools report progress on stderr. Only the exit code decides
    # success, and stderr lines are written as plain text so a passing step
    # does not read as a failure in the transcript.
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

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class AppSessionInput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseAbsolute = 0x8000;
    private const uint KeyUp = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    // Makes screen coordinates from UI Automation, SendInput, and the recorder
    // all physical pixels. A process that is already DPI aware keeps its mode.
    public static void EnablePerMonitorDpiAwareness()
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
    }

    // Windows only lets the process that owns the foreground move it, so this
    // attaches to that window's input queue for the call. It sends no input,
    // so nothing extra reaches the recorder's input channel.
    public static bool BringToForeground(IntPtr window)
    {
        if (IsIconic(window))
        {
            ShowWindow(window, 9);
        }
        uint unused;
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out unused);
        uint currentThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
            AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
        return GetForegroundWindow() == window;
    }

    public static uint GetRootWindowProcessAt(int x, int y)
    {
        POINT point;
        point.X = x;
        point.Y = y;
        IntPtr root = GetAncestor(WindowFromPoint(point), 2);
        uint processId;
        GetWindowThreadProcessId(root, out processId);
        return processId;
    }

    private static void Send(INPUT input)
    {
        INPUT[] inputs = new INPUT[] { input };
        if (SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) != 1)
        {
            throw new InvalidOperationException(
                "SendInput was refused, error " + Marshal.GetLastWin32Error() + ".");
        }
    }

    private static INPUT Mouse(int dx, int dy, uint flags, long marker)
    {
        INPUT input = new INPUT();
        input.type = InputMouse;
        input.u.mi.dx = dx;
        input.u.mi.dy = dy;
        input.u.mi.dwFlags = flags;
        input.u.mi.dwExtraInfo = new IntPtr(marker);
        return input;
    }

    public static void Click(int x, int y, long marker)
    {
        int left = GetSystemMetrics(76);
        int top = GetSystemMetrics(77);
        int width = GetSystemMetrics(78);
        int height = GetSystemMetrics(79);
        int dx = (int)(((long)(x - left) * 65535) / (width - 1));
        int dy = (int)(((long)(y - top) * 65535) / (height - 1));
        Send(Mouse(dx, dy, MouseMove | MouseAbsolute | MouseVirtualDesk, marker));
        Thread.Sleep(80);
        Send(Mouse(0, 0, MouseLeftDown, marker));
        Thread.Sleep(80);
        Send(Mouse(0, 0, MouseLeftUp, marker));
    }

    public static void Key(ushort virtualKey, long marker)
    {
        INPUT input = new INPUT();
        input.type = InputKeyboard;
        input.u.ki.wVk = virtualKey;
        input.u.ki.wScan = (ushort)MapVirtualKey(virtualKey, 0);
        input.u.ki.dwExtraInfo = new IntPtr(marker);
        Send(input);
        Thread.Sleep(60);
        input.u.ki.dwFlags = KeyUp;
        Send(input);
        Thread.Sleep(120);
    }
}
"@

[AppSessionInput]::EnablePerMonitorDpiAwareness()
Add-Type -AssemblyName System.Windows.Forms
# The displays and system DPI are reported with the result, since the recorder
# application and this script can differ in DPI awareness on scaled displays.
$displays = @(
    [System.Windows.Forms.Screen]::AllScreens | ForEach-Object {
        "$($_.DeviceName) $($_.Bounds.Width)x$($_.Bounds.Height) at " +
            "($($_.Bounds.X), $($_.Bounds.Y))"
    }
) -join "; "
$systemDpi = [AppSessionInput]::GetDpiForSystem()

$automation = [System.Windows.Automation.AutomationElement]
$treeScope = [System.Windows.Automation.TreeScope]

function Find-ById {
    param(
        [Parameter(Mandatory = $true)]
        $Root,

        [Parameter(Mandatory = $true)]
        [string] $AutomationId
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        $automation::AutomationIdProperty,
        $AutomationId
    )
    # UI Automation peers are created as the window lays out, so a control can
    # be missing for a moment after the window opens.
    $deadline = (Get-Date).AddSeconds(10)
    while ($true) {
        $element = $Root.FindFirst($treeScope::Descendants, $condition)
        if ($element) {
            return $element
        }
        if ((Get-Date) -ge $deadline) {
            throw "The recorder window has no element with automation id $AutomationId."
        }
        Start-Sleep -Milliseconds 250
    }
}

function Find-ByNameAndType {
    param(
        [Parameter(Mandatory = $true)]
        $Root,

        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        $ControlType,

        [int] $TimeoutSeconds = 20
    )

    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
                $automation::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition(
                $automation::ControlTypeProperty, $ControlType))
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $element = $Root.FindFirst($treeScope::Descendants, $condition)
        if ($element) {
            return $element
        }
        Start-Sleep -Milliseconds 250
    }
    throw "UI Automation did not expose '$Name' in the fixture page."
}

function Set-TextValue {
    param($Element, [string] $Value)

    $pattern = $Element.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern
    )
    $pattern.SetValue($Value)
}

function Get-TextValue {
    param($Element)

    $Element.GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern
    ).Current.Value
}

function Set-Checked {
    param($Element, [bool] $Checked)

    $pattern = $Element.GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern
    )
    $wanted = if ($Checked) { "On" } else { "Off" }
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        if ($pattern.Current.ToggleState.ToString() -eq $wanted) {
            return
        }
        $pattern.Toggle()
    }
    throw "The check box $($Element.Current.AutomationId) did not reach $wanted."
}

function Invoke-Element {
    param($Element)

    $Element.GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern
    ).Invoke()
}

function Set-WindowState {
    param($Window, [string] $State)

    $Window.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern
    ).SetWindowVisualState(
        [System.Windows.Automation.WindowVisualState]::$State
    )
}

function Wait-Until {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock] $Condition,

        [Parameter(Mandatory = $true)]
        [int] $TimeoutSeconds,

        [Parameter(Mandatory = $true)]
        [string] $Failure
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) {
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw $Failure
}

function Get-ChromiumProcesses {
    param([string] $ExecutablePath)

    @(
        Get-CimInstance Win32_Process -Filter "Name = 'chrome.exe'" |
            Where-Object { $_.ExecutablePath -eq $ExecutablePath }
    )
}

function Get-RectCenter {
    param($Rect)

    [pscustomobject]@{
        X = [int] [Math]::Floor($Rect.X + $Rect.Width / 2)
        Y = [int] [Math]::Floor($Rect.Y + $Rect.Height / 2)
    }
}

function ConvertTo-RectObject {
    param($Rect)

    [pscustomobject]@{
        x = [int] [Math]::Floor($Rect.X)
        y = [int] [Math]::Floor($Rect.Y)
        width = [int] [Math]::Ceiling($Rect.Width)
        height = [int] [Math]::Ceiling($Rect.Height)
    }
}

# Serves the app session fixture page and its data response from a loopback
# port until the listener is stopped.
function Start-AppFixtureServer {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Page
    )

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([Net.IPEndPoint] $listener.LocalEndpoint).Port
    $server = [PowerShell]::Create()
    $null = $server.AddScript({
            param($Listener, $Page)

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
                    if ($path -eq "/app-session") {
                        $status = "200 OK"
                        $contentType = "text/html; charset=utf-8"
                        $body = $Page
                    }
                    elseif ($path -eq "/app-session/data") {
                        $status = "200 OK"
                        $contentType = "application/json"
                        $body = '{"fixture":"app-session"}'
                    }
                    $bodyBytes = [Text.Encoding]::UTF8.GetBytes($body)
                    $head = (
                        "HTTP/1.1 $status`r`n" +
                        "Content-Type: $contentType`r`n" +
                        "Content-Length: $($bodyBytes.Length)`r`n" +
                        "Cache-Control: no-store`r`n" +
                        "Connection: close`r`n`r`n"
                    )
                    $headBytes = $ascii.GetBytes($head)
                    $stream.Write($headBytes, 0, $headBytes.Length)
                    $stream.Write($bodyBytes, 0, $bodyBytes.Length)
                    $stream.Flush()
                }
                catch {
                    # A connection that sent no request or closed early is
                    # dropped. The run fails on its own if the page was never
                    # served.
                }
                finally {
                    $client.Close()
                }
            }
        }).AddArgument($listener).AddArgument($Page)
    $handle = $server.BeginInvoke()

    [pscustomobject]@{
        Listener = $listener
        PowerShell = $server
        Handle = $handle
        BaseUri = "http://127.0.0.1:$port/"
    }
}

function Stop-AppFixtureServer {
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

# The fixture page. It writes a cookie from script, fetches a data response,
# hosts an open shadow root and a generated-content pseudo-element, and holds
# the three controls the injected input reaches. Its title reports readiness
# only after the fetch has completed.
$fixturePage = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>App session fixture</title>
<style>
  body { font: 16px sans-serif; margin: 24px; }
  h1::before { content: "Fixture: "; }
  button, input { font-size: 20px; margin: 12px 0; padding: 8px; display: block; }
  input { width: 320px; }
</style>
</head>
<body>
<h1>App session</h1>
<button id="app-button" type="button">Record app session click</button>
<label for="app-text">App session text</label>
<input id="app-text" type="text" autocomplete="off">
<button id="app-next" type="button">App session next</button>
<div id="app-host"></div>
<script>
  document.getElementById("app-button").addEventListener("click", function () {
    document.body.setAttribute("data-clicked", "true");
  });
  document.getElementById("app-text").addEventListener("input", function () {});
  var root = document.getElementById("app-host").attachShadow({ mode: "open" });
  root.innerHTML = "<span>Shadow content</span>";
  document.cookie = "app_session_script=1; Path=/; SameSite=Lax";
  fetch("/app-session/data").then(function (response) {
    return response.text();
  }).then(function () {
    document.title = "App session fixture ready";
  });
</script>
</body>
</html>
"@

$repository = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repository "windows-a11y-recorder.slnx"
$appProject = Join-Path $repository "src\Recorder.App\Recorder.App.csproj"
$appOutput = Join-Path $repository "artifacts\app-session-validation\app"
$verifier = Join-Path $PSScriptRoot "Verify-AppSessionEvidence.ps1"
$dotnet = $DotnetPath
$chromiumPath = [IO.Path]::GetFullPath($ChromiumPath)
$outputRoot = [IO.Path]::GetFullPath($OutputRoot)
# Every injected input carries this value as its extra information, which the
# raw input collector records, so the verifier can tell it from other input.
# The value spells A11Y in ASCII.
$inputMarker = 0x41313159
$typedText = "a11y"

foreach ($requiredPath in @($chromiumPath, $dotnet, $solution, $appProject)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required path not found: $requiredPath"
    }
}
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

Invoke-Checked "Testing the managed recorder" {
    & $dotnet test $solution --configuration Release
}
Invoke-Checked "Building the recorder app" {
    & $dotnet build $appProject --configuration Release --output $appOutput
}
$appExecutable = Join-Path $appOutput "Recorder.App.exe"
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw "The app build did not produce $appExecutable."
}

foreach ($stale in @(Get-Process -Name "Recorder.App" -ErrorAction SilentlyContinue)) {
    Write-Host "Stopping running recorder process $($stale.Id)"
    Stop-Process -Id $stale.Id -Force -ErrorAction SilentlyContinue
}
foreach ($stale in (Get-ChromiumProcesses $chromiumPath)) {
    Stop-Process -Id $stale.ProcessId -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 1

$sessionsBefore = @(
    Get-ChildItem -LiteralPath $outputRoot -Directory -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
)

$server = $null
$app = $null
try {
    $server = Start-AppFixtureServer $fixturePage
    $fixtureUri = "$($server.BaseUri)app-session"
    Write-Host "Serving the app session fixture at $fixtureUri"

    Write-Host "`n== Recording a session from the recorder app =="
    Write-Host "Do not use the mouse or keyboard until the recording stops."
    # The app is framework-dependent. When .NET is installed outside the
    # default location, as a per-user SDK is, its host finds the runtime only
    # through DOTNET_ROOT and otherwise shows a missing-runtime dialog.
    $dotnetRoot = Split-Path -Parent ([IO.Path]::GetFullPath($dotnet))
    $previousDotnetRoot = $env:DOTNET_ROOT
    $env:DOTNET_ROOT = $dotnetRoot
    try {
        $app = Start-Process -FilePath $appExecutable -PassThru
    }
    finally {
        $env:DOTNET_ROOT = $previousDotnetRoot
    }
    Wait-Until -TimeoutSeconds 30 -Failure "The recorder window did not open." -Condition {
        $app.Refresh()
        $app.HasExited -or $app.MainWindowHandle -ne [IntPtr]::Zero
    }
    if ($app.HasExited) {
        throw "The recorder app exited with code $($app.ExitCode) before showing its window."
    }
    $window = $automation::FromHandle($app.MainWindowHandle)
    if ($window.Current.Name -ne "Windows Accessibility Recorder") {
        throw (
            "The recorder process showed '$($window.Current.Name)' " +
            "($($window.Current.ClassName)) instead of its main window. " +
            "A dialog from the .NET host usually means the runtime was not found."
        )
    }

    Set-TextValue (Find-ById $window "OutputRootTextBox") $outputRoot
    foreach ($checkBox in @(
            "InputCheckBox",
            "AutomationCheckBox",
            "WindowCheckBox",
            "FramesCheckBox",
            "BrowserEvidenceCheckBox"
        )) {
        Set-Checked (Find-ById $window $checkBox) $true
    }
    Set-Checked (Find-ById $window "MicrophoneCheckBox") $false
    Set-Checked (Find-ById $window "SystemAudioCheckBox") $false
    Set-TextValue (Find-ById $window "ChromiumPathTextBox") $chromiumPath
    Set-TextValue (Find-ById $window "BrowserStartUrlTextBox") $fixtureUri

    $stopButton = Find-ById $window "StopButton"
    $statusText = Find-ById $window "StatusTextBlock"
    Invoke-Element (Find-ById $window "StartButton")
    Wait-Until -TimeoutSeconds 60 -Condition { $stopButton.Current.IsEnabled } -Failure (
        "The recording did not start. The app reported: " +
        $statusText.Current.Name
    )
    $sessionPath = Get-TextValue (Find-ById $window "SessionFolderTextBox")
    Write-Host "Recording to $sessionPath"

    # The recorder window is minimized so it cannot cover the fixture page.
    Set-WindowState $window "Minimized"

    $chromiumWindow = $null
    Wait-Until -TimeoutSeconds 60 -Failure (
        "The instrumented Chromium window did not show the ready fixture page."
    ) -Condition {
        foreach ($process in (Get-ChromiumProcesses $chromiumPath)) {
            $condition = New-Object System.Windows.Automation.PropertyCondition(
                $automation::ProcessIdProperty, [int] $process.ProcessId
            )
            foreach ($candidate in $automation::RootElement.FindAll(
                    $treeScope::Children, $condition)) {
                if ($candidate.Current.Name -like "App session fixture ready*") {
                    $script:chromiumWindow = $candidate
                    return $true
                }
            }
        }
        $false
    }

    # The browser process is the one without a --type switch. The app must
    # have launched it with the recorder bootstrap and without a DevTools
    # debugging port, which only the validation capture host adds.
    $chromiumProcesses = Get-ChromiumProcesses $chromiumPath
    $browserProcesses = @(
        $chromiumProcesses | Where-Object { $_.CommandLine -notmatch '--type=' }
    )
    if ($browserProcesses.Count -ne 1) {
        throw "Expected one instrumented Chromium browser process, found $($browserProcesses.Count)."
    }
    $browserCommandLine = $browserProcesses[0].CommandLine
    if ($browserCommandLine -notmatch '--a11y-recorder-bootstrap=stdin') {
        throw "The app launched Chromium without the recorder bootstrap switch."
    }
    if ($browserCommandLine -match '--remote-debugging-port') {
        throw "The app launched Chromium with a DevTools debugging port."
    }

    # The Chromium top-level window is not keyboard focusable through UI
    # Automation, so SetFocus on it throws. The window is brought to the
    # foreground through Win32 instead, retried while Windows settles.
    $chromiumHandle = [IntPtr] $chromiumWindow.Current.NativeWindowHandle
    $chromiumIdsForForeground = @($chromiumProcesses | ForEach-Object { [int] $_.ProcessId })
    Wait-Until -TimeoutSeconds 10 -Failure (
        "The instrumented Chromium window could not be brought to the foreground."
    ) -Condition {
        $null = [AppSessionInput]::BringToForeground($chromiumHandle)
        Start-Sleep -Milliseconds 300
        $foregroundProcessId = [uint32] 0
        $null = [AppSessionInput]::GetWindowThreadProcessId(
            [AppSessionInput]::GetForegroundWindow(),
            [ref] $foregroundProcessId
        )
        $chromiumIdsForForeground -contains [int] $foregroundProcessId
    }
    Start-Sleep -Milliseconds 500

    $buttonElement = Find-ByNameAndType $chromiumWindow "Record app session click" (
        [System.Windows.Automation.ControlType]::Button
    )
    $textElement = Find-ByNameAndType $chromiumWindow "App session text" (
        [System.Windows.Automation.ControlType]::Edit
    )
    $nextElement = Find-ByNameAndType $chromiumWindow "App session next" (
        [System.Windows.Automation.ControlType]::Button
    )
    $buttonRect = ConvertTo-RectObject $buttonElement.Current.BoundingRectangle
    $textRect = ConvertTo-RectObject $textElement.Current.BoundingRectangle
    $nextRect = ConvertTo-RectObject $nextElement.Current.BoundingRectangle
    $chromiumIds = @($chromiumProcesses | ForEach-Object { [int] $_.ProcessId })
    foreach ($target in @(
            @{ Name = "Record app session click"; Rect = $buttonElement.Current.BoundingRectangle },
            @{ Name = "App session text"; Rect = $textElement.Current.BoundingRectangle }
        )) {
        $center = Get-RectCenter $target.Rect
        $owner = [int] [AppSessionInput]::GetRootWindowProcessAt($center.X, $center.Y)
        if ($chromiumIds -notcontains $owner) {
            throw (
                "Another window, from process $owner, covers '$($target.Name)' " +
                "at ($($center.X), $($center.Y))."
            )
        }
    }

    $buttonCenter = Get-RectCenter $buttonElement.Current.BoundingRectangle
    $textCenter = Get-RectCenter $textElement.Current.BoundingRectangle
    [AppSessionInput]::Click($buttonCenter.X, $buttonCenter.Y, $inputMarker)
    Start-Sleep -Milliseconds 700
    [AppSessionInput]::Click($textCenter.X, $textCenter.Y, $inputMarker)
    Start-Sleep -Milliseconds 700
    foreach ($character in $typedText.ToUpperInvariant().ToCharArray()) {
        [AppSessionInput]::Key([uint16] [char] $character, $inputMarker)
    }
    Start-Sleep -Milliseconds 300
    [AppSessionInput]::Key([uint16] 0x09, $inputMarker)
    # Leaves time for the post-input DOM, layout, and accessibility
    # checkpoints to be recorded before the recording stops.
    Start-Sleep -Seconds 3

    $chromiumIds = @(
        @($chromiumIds) + @(
            Get-ChromiumProcesses $chromiumPath | ForEach-Object { [int] $_.ProcessId }
        ) | Sort-Object -Unique
    )

    Set-WindowState $window "Normal"
    Invoke-Element $stopButton
    Wait-Until -TimeoutSeconds 180 -Condition {
        $statusText.Current.Name -like "Recording completed*" -or
            $statusText.Current.Name -like "Recording stopped*"
    } -Failure "The recording did not stop. The app reported: $($statusText.Current.Name)"
    $stopStatus = $statusText.Current.Name
    if ($stopStatus -ne "Recording completed and session files verified.") {
        throw "The app reported: $stopStatus"
    }

    # The app loads the finished session into playback on its own.
    $playbackText = Find-ById $window "PlaybackStatusTextBlock"
    $sessionId = Split-Path -Leaf $sessionPath
    Wait-Until -TimeoutSeconds 120 -Condition {
        $playbackText.Current.Name -like "$sessionId |*" -or
            $playbackText.Current.Name -eq "Recording load failed."
    } -Failure "The app did not load the recording. It reported: $($playbackText.Current.Name)"
    $playbackStatus = $playbackText.Current.Name
    if ($playbackStatus -eq "Recording load failed.") {
        throw "The app could not load the recording it made."
    }
    $navigationList = Find-ById $window "BrowserNavigationListBox"
    $listItems = $navigationList.FindAll(
        $treeScope::Children,
        (New-Object System.Windows.Automation.PropertyCondition(
                $automation::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem))
    )
    $playbackNavigations = $listItems.Count
    if ($playbackNavigations -lt 1) {
        throw "The app's playback listed no browser navigations."
    }

    $window.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern
    ).Close()
    if (-not $app.WaitForExit(60000)) {
        throw "The recorder app did not exit after its window was closed."
    }
    $app = $null
}
finally {
    Stop-AppFixtureServer $server
    if ($app -and -not $app.HasExited) {
        Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
    }
}

$session = Get-ChildItem -LiteralPath $outputRoot -Directory |
    Where-Object { $_.FullName -notin $sessionsBefore } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $session -or $session.FullName -ne [IO.Path]::GetFullPath($sessionPath)) {
    throw "The session the app reported, $sessionPath, is not the new session folder."
}

$validationPath = Join-Path $session.FullName "diagnostics\archive-validation.json"
if (-not (Test-Path -LiteralPath $validationPath -PathType Leaf)) {
    throw "The session does not contain an archive-validation report."
}
$validation = Get-Content -LiteralPath $validationPath -Raw | ConvertFrom-Json
if (-not $validation.isValid) {
    $errors = @(
        $validation.issues |
            Where-Object { $_.severity -eq "Error" } |
            ForEach-Object { "$($_.code) at $($_.path)" }
    )
    throw "Archive validation failed: $($errors -join '; ')"
}

$steps = [pscustomobject]@{
    InputMarker = $inputMarker
    ButtonRect = $buttonRect
    TextRect = $textRect
    NextRect = $nextRect
    ChromiumProcessIds = $chromiumIds
    TypedText = $typedText
} | ConvertTo-Json -Depth 4 -Compress
# Kept beside the session, not in it, so that a failed verification can be
# repeated against the same recording without recording again.
Set-Content -LiteralPath (Join-Path $outputRoot "$($session.Name).steps.json") `
    -Value $steps -Encoding UTF8

& $verifier -SessionPath $session.FullName -FixtureUri $fixtureUri -StepsJson $steps

# Browser evidence the recorder lost fails the run, as in the Blink
# validation. Omissions from other collectors are reported with their counts.
$manifest = Get-Content -LiteralPath (Join-Path $session.FullName "manifest.json") -Raw |
    ConvertFrom-Json
$sinkRefusedEvents = [int] $manifest.droppedEventCount
$lossReasons = @("browser-evidence-write-failed", "browser-evidence-sink-refused")
$browserLost = 0
$otherOmissions = @()
foreach ($omissionLine in @(
        Select-String `
            -LiteralPath (Join-Path $session.FullName "events.ndjson") `
            -Pattern "collector-omission" `
            -SimpleMatch
    )) {
    $omission = $omissionLine.Line | ConvertFrom-Json
    if ($omission.eventType -ne "collector-omission") {
        continue
    }
    $count = 1
    if ($omission.payload.PSObject.Properties.Name -contains "count") {
        $count = [int] $omission.payload.count
    }
    if ($omission.channel -like "browser.*" -and
        $lossReasons -contains $omission.payload.reason) {
        $browserLost += $count
    }
    elseif ($omission.payload.PSObject.Properties.Name -contains "lastDroppedAtNanoseconds") {
        # A UI Automation drop episode: when it happened and what it refused.
        $first = [double] $omission.payload.firstDroppedAtNanoseconds / 1e9
        $last = [double] $omission.payload.lastDroppedAtNanoseconds / 1e9
        $types = @(
            $omission.payload.droppedByObservationType.PSObject.Properties |
                ForEach-Object { "$($_.Name)=$($_.Value)" }
        ) -join ","
        $otherOmissions += (
            "$($omission.channel) $($omission.payload.reason) $count " +
            "at $($first.ToString('0.000'))-$($last.ToString('0.000')) s ($types)"
        )
    }
    else {
        $otherOmissions += "$($omission.channel) $($omission.payload.reason) $count"
    }
}
if ($browserLost -gt 0 -or $sinkRefusedEvents -gt 0) {
    throw (
        "This run lost evidence: $browserLost browser record(s) reported as " +
        "omitted and $sinkRefusedEvents refused by the event sink."
    )
}

Write-Host "`nApp session validation completed successfully."
Write-Host "SESSION_PATH=$($session.FullName)"
Write-Host "APP_STOP_STATUS=$stopStatus"
Write-Host "APP_PLAYBACK_STATUS=$playbackStatus"
Write-Host "APP_PLAYBACK_NAVIGATIONS=$playbackNavigations"
Write-Host "DISPLAYS=$displays"
Write-Host "SYSTEM_DPI=$systemDpi"
Write-Host "ARCHIVE_VALID=$($validation.isValid)"
Write-Host "EVENTS_VALIDATED=$($validation.eventsValidated)"
Write-Host "ARTIFACTS_VALIDATED=$($validation.artifactsValidated)"
Write-Host "SINK_REFUSED_EVENTS=$sinkRefusedEvents"
Write-Host "BROWSER_OMITTED_RECORDS=$browserLost"
Write-Host "OTHER_OMISSIONS=$($otherOmissions -join '; ')"

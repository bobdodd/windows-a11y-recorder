# Tests which keys pressed while a screen reader runs reach the recorder's
# raw input, and which reach the page, and what the recorder captures of the
# screen reader's own windows.
#
# Background: NVDA reads the keyboard through a low-level keyboard hook and
# keeps its own commands (for example H in browse mode) from the
# application. One published measurement on Windows 11 found that a key
# blocked by a low-level hook is also missing from raw input, which is how
# the recorder captures the keyboard. This script checks that on this
# machine.
#
# Run in an ordinary PowerShell window:
#   1. Start NVDA. Do not restart it during the test.
#   2. Start the recorder and start a recording.
#   3. cd C:\Users\Public\Downloads
#      powershell -ExecutionPolicy Bypass -File .\Test-ScreenReaderKeys.ps1
#   4. Follow the prompts: open the page it names in the recorder's
#      Chromium window and do the steps shown on the page, then click
#      Finish in the small "Screen reader key test" window.
#   5. Stop the recording and type the session ID when asked.
#
# The script installs its own low-level keyboard hook after NVDA's, so it
# is called before NVDA and sees every key, and registers for raw input
# itself, as the recorder does. It does not block or change any key. It
# then exports the recording's events, compares each key, and writes the
# results to a new folder in Downloads.
#
# -DryRun checks the script itself: it injects F24 twice, needs neither NVDA
# nor a recording, and with -SessionId also runs the export and analysis on
# that recording.

param(
    [string]$PackageRoot = 'C:\Users\Public\Downloads\wr-c9f2591\windows-a11y-recorder-c9f2591',
    [string]$SessionsRoot = (Join-Path $env:USERPROFILE 'Documents\Windows A11y Recorder'),
    [string]$OutputRoot = 'C:\Users\Public\Downloads',
    [string]$DotnetPath = (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'),
    [int]$Port = 8767,
    [string]$SessionId,
    # An events file already exported from the recording, one JSON event per
    # line; the export is then skipped.
    [string]$EventsPath,
    # Overrides the screen reader's process IDs, for checking the analysis
    # against an earlier recording.
    [int[]]$ScreenReaderProcessId,
    # A script-keys.json written by an earlier run; the keys are read from it
    # instead of being captured, to repeat the analysis.
    [string]$KeysPath,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$results = Join-Path $OutputRoot "screen-reader-keys-$stamp"
New-Item -ItemType Directory -Force -Path $results | Out-Null
$reportPath = Join-Path $results 'report.txt'
function Say([string]$text) {
    Write-Host $text
    Add-Content -LiteralPath $reportPath -Value $text
}

# --- The screen reader -------------------------------------------------------

$screenReaders = @(Get-Process -Name 'nvda' -ErrorAction SilentlyContinue)
if ($screenReaders.Count -eq 0 -and -not $DryRun -and -not $KeysPath -and -not $ScreenReaderProcessId) {
    throw 'NVDA is not running. Start NVDA first, then run this script.'
}
$screenReaderIds = @($screenReaders | ForEach-Object { $_.Id })
if ($ScreenReaderProcessId) { $screenReaderIds = @($ScreenReaderProcessId) }
foreach ($process in $screenReaders) {
    $path = $null
    $version = $null
    try {
        $path = $process.Path
        $version = (Get-Item -LiteralPath $path).VersionInfo.ProductVersion
    }
    catch {
        $path = '(not readable)'
    }
    Say ("Screen reader: nvda process {0}, started {1:o}, {2}, version {3}" -f `
            $process.Id, $process.StartTime, $path, $version)
}

# --- The fixture page --------------------------------------------------------

$page = @'
<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8"><title>Screen reader key test</title>
<style>
body { font: 18px/1.5 system-ui, sans-serif; margin: 2em; max-width: 48em; }
ol li { margin-bottom: .3em; }
.box { border: 1px solid #555; padding: .5em 1em; margin: 1em 0; }
</style></head>
<body>
<header><h1>Screen reader key test</h1></header>
<nav aria-label="Test links">
  <a href="#one">First link</a> | <a href="#two">Second link</a> | <a href="#three">Third link</a>
</nav>
<main>
  <p id="start"><strong>Start here.</strong> Pause about a second between keys.</p>
  <ol>
    <li>Click the words "Start here".</li>
    <li>Press H three times.</li>
    <li>Press 2.</li>
    <li>Press D.</li>
    <li>Press K.</li>
    <li>Press B.</li>
    <li>Press Down Arrow three times.</li>
    <li>Press NVDA+T (Insert+T).</li>
    <li>Press Tab twice.</li>
    <li>Press NVDA+F7 (Insert+F7). In the elements list press Down Arrow twice, then Escape.</li>
    <li>Press E to move to the text field, press Enter, type abc, then press Escape.</li>
    <li>Click Finish in the "Screen reader key test" window.</li>
  </ol>
  <h2 id="one">First section</h2>
  <p>Some text in the first section.</p>
  <button type="button">First button</button>
  <h2 id="two">Second section</h2>
  <p>Some text in the second section.</p>
  <ul><li>List item one</li><li>List item two</li></ul>
  <h3 id="three">A third level heading</h3>
  <div class="box"><label for="field">Text field</label> <input id="field" type="text"></div>
  <button type="button">Second button</button>
</main>
<footer><p>End of the test page.</p></footer>
</body></html>
'@

# --- The capture -------------------------------------------------------------

Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ScreenReaderKeyTest
{
    public class KeyRecord
    {
        public string Source;
        public long UtcTicks;
        public int VirtualKey;
        public int ScanCode;
        public bool Up;
        public bool Injected;
        public int Flags;
        public long Device;
        public long Extra;
    }

    public class CaptureForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public UIntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }
        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }
        [StructLayout(LayoutKind.Sequential)]
        struct RAWKEYBOARD { public ushort MakeCode; public ushort Flags; public ushort Reserved; public ushort VKey; public uint Message; public uint ExtraInformation; }

        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr module, uint threadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("kernel32.dll")] static extern void GetSystemTimePreciseAsFileTime(out long fileTime);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        const int WH_KEYBOARD_LL = 13;
        const int WM_INPUT = 0x00FF;
        const uint RID_INPUT = 0x10000003;
        const uint RIDEV_INPUTSINK = 0x00000100;
        const uint LLKHF_INJECTED = 0x10;
        const uint LLKHF_UP = 0x80;
        // FILETIME counts from 1601; DateTime ticks count from year 1.
        const long FileTimeEpochTicks = 504911232000000000L;

        public readonly List<KeyRecord> Records = new List<KeyRecord>();
        public string HookError;
        public string RawInputError;
        readonly HookProc _proc;
        IntPtr _hook = IntPtr.Zero;

        public CaptureForm(string instructions)
        {
            Text = "Screen reader key test";
            Width = 420;
            Height = 190;
            StartPosition = FormStartPosition.Manual;
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);
            var label = new Label();
            label.Text = instructions;
            label.SetBounds(12, 10, 380, 90);
            Controls.Add(label);
            var finish = new Button();
            finish.Text = "Finish";
            finish.SetBounds(12, 105, 120, 32);
            finish.Click += delegate { Close(); };
            Controls.Add(finish);
            _proc = HookCallback;
        }

        static long UtcTicksNow()
        {
            long fileTime;
            GetSystemTimePreciseAsFileTime(out fileTime);
            return fileTime + FileTimeEpochTicks;
        }

        void Add(KeyRecord record)
        {
            lock (Records) { Records.Add(record); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var device = new RAWINPUTDEVICE();
            device.usUsagePage = 0x01;
            device.usUsage = 0x06;
            device.dwFlags = RIDEV_INPUTSINK;
            device.hwndTarget = Handle;
            if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
            {
                RawInputError = "RegisterRawInputDevices failed: " + Marshal.GetLastWin32Error();
            }
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                HookError = "SetWindowsHookEx failed: " + Marshal.GetLastWin32Error();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            base.OnFormClosed(e);
        }

        IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                var record = new KeyRecord();
                record.Source = "hook";
                record.UtcTicks = UtcTicksNow();
                record.VirtualKey = (int)data.vkCode;
                record.ScanCode = (int)data.scanCode;
                record.Up = (data.flags & LLKHF_UP) != 0;
                record.Injected = (data.flags & LLKHF_INJECTED) != 0;
                record.Flags = (int)data.flags;
                record.Extra = (long)data.dwExtraInfo.ToUInt64();
                Add(record);
            }
            // Never blocks: every key goes on to the next hook unchanged.
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                ReadRawInput(m.LParam);
            }
            base.WndProc(ref m);
        }

        void ReadRawInput(IntPtr handle)
        {
            var ticks = UtcTicksNow();
            uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            uint size = 0;
            GetRawInputData(handle, RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0) { return; }
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(handle, RID_INPUT, buffer, ref size, headerSize) != size) { return; }
                var header = (RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(RAWINPUTHEADER));
                if (header.dwType != 1) { return; }
                var keyboard = (RAWKEYBOARD)Marshal.PtrToStructure(
                    new IntPtr(buffer.ToInt64() + headerSize), typeof(RAWKEYBOARD));
                var record = new KeyRecord();
                record.Source = "raw";
                record.UtcTicks = ticks;
                record.VirtualKey = keyboard.VKey;
                record.ScanCode = keyboard.MakeCode;
                record.Up = (keyboard.Flags & 1) != 0;
                record.Flags = keyboard.Flags;
                record.Device = header.hDevice.ToInt64();
                record.Extra = keyboard.ExtraInformation;
                Add(record);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public static class FixtureServer
    {
        public static void Start(int port, string page)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            var thread = new Thread(delegate ()
            {
                while (true)
                {
                    TcpClient client;
                    try { client = listener.AcceptTcpClient(); } catch { return; }
                    try
                    {
                        client.ReceiveTimeout = 2000;
                        var stream = client.GetStream();
                        var reader = new StreamReader(stream, Encoding.ASCII);
                        var requestLine = reader.ReadLine() ?? "";
                        string line;
                        while (!string.IsNullOrEmpty(line = reader.ReadLine())) { }
                        var parts = requestLine.Split(' ');
                        var path = parts.Length > 1 ? parts[1] : "";
                        var found = path == "/" || path == "/screen-reader-keys";
                        var body = Encoding.UTF8.GetBytes(found ? page : "not found");
                        var head = Encoding.ASCII.GetBytes(
                            "HTTP/1.1 " + (found ? "200 OK" : "404 Not Found") + "\r\n" +
                            "Content-Type: " + (found ? "text/html" : "text/plain") + "; charset=utf-8\r\n" +
                            "Content-Length: " + body.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                        stream.Write(head, 0, head.Length);
                        stream.Write(body, 0, body.Length);
                        stream.Flush();
                    }
                    catch { }
                    finally { client.Close(); }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }
    }
}
'@

$url = "http://127.0.0.1:$Port/screen-reader-keys"
if (-not $KeysPath) { [ScreenReaderKeyTest.FixtureServer]::Start($Port, $page) }

if ($KeysPath) {
    Say "Keys: $KeysPath"
}
elseif ($DryRun) {
    $form = New-Object ScreenReaderKeyTest.CaptureForm 'Dry run: injecting F24 twice.'
    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = 400
    $script:ticks = 0
    $timer.add_Tick({
            $script:ticks++
            if ($script:ticks -eq 2 -or $script:ticks -eq 3) {
                [ScreenReaderKeyTest.CaptureForm]::keybd_event(0x87, 0, 0, [UIntPtr]::new(0x53524B54))
                [ScreenReaderKeyTest.CaptureForm]::keybd_event(0x87, 0, 2, [UIntPtr]::new(0x53524B54))
            }
            if ($script:ticks -ge 6) { $timer.Stop(); $form.Close() }
        })
    $timer.Start()
    [System.Windows.Forms.Application]::Run($form)
    $served = (Invoke-WebRequest -UseBasicParsing -Uri $url).Content
    Say ("Dry run: page served: {0}" -f ($served -match 'Screen reader key test'))
}
else {
    Write-Host ''
    Write-Host "Open this address in the recorder's Chromium window:"
    Write-Host "  $url"
    Write-Host 'Then follow the steps on the page, and click Finish in the small'
    Write-Host '"Screen reader key test" window at the bottom right when done.'
    $form = New-Object ScreenReaderKeyTest.CaptureForm (
        "Keys are being logged (never blocked).`r`n`r`nOpen $url in the recorder's Chromium " +
        'window, do the steps on the page, then click Finish.')
    [System.Windows.Forms.Application]::Run($form)
}

if ($KeysPath) {
    # Windows PowerShell passes a parsed JSON array on as one object; the
    # extra pipeline unrolls it.
    $records = @((Get-Content -LiteralPath $KeysPath -Raw | ConvertFrom-Json) | ForEach-Object { $_ })
}
else {
    if ($form.HookError) { Say "Hook: $($form.HookError)" }
    if ($form.RawInputError) { Say "Raw input: $($form.RawInputError)" }
    $records = @($form.Records.ToArray())
}
$records | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $results 'script-keys.json') -Encoding UTF8
$hook = @($records | Where-Object { $_.Source -eq 'hook' })
$raw = @($records | Where-Object { $_.Source -eq 'raw' })
Say ("Script capture: {0} hook records ({1} injected), {2} raw input records" -f `
        $hook.Count, @($hook | Where-Object { $_.Injected }).Count, $raw.Count)

# Reads a property that a recorded payload may leave out.
function Get-Prop($value, [string]$name) {
    if ($null -eq $value -or $value.PSObject.Properties.Name -notcontains $name) { return $null }
    $value.$name
}

# Times are compared in nanoseconds since 1970, which fit in 64 bits.
$unixEpochTicks = 621355968000000000L

# --- Key names and matching --------------------------------------------------

# Raw input reports the generic Shift, Ctrl, and Alt keys; the hook reports
# left and right. Both are compared as the generic key.
function Get-BaseKey([int]$vk) {
    switch ($vk) {
        0xA0 { 0x10 } 0xA1 { 0x10 } 0xA2 { 0x11 } 0xA3 { 0x11 } 0xA4 { 0x12 } 0xA5 { 0x12 }
        default { $vk }
    }
}
function Get-KeyName([int]$vk) {
    $names = @{
        0x08 = 'Backspace'; 0x09 = 'Tab'; 0x0D = 'Enter'; 0x10 = 'Shift'; 0x11 = 'Ctrl'; 0x12 = 'Alt'
        0x14 = 'CapsLock'; 0x1B = 'Escape'; 0x20 = 'Space'; 0x25 = 'Left'; 0x26 = 'Up'; 0x27 = 'Right'
        0x28 = 'Down'; 0x2D = 'Insert'; 0x2E = 'Delete'; 0x5B = 'Win'; 0x87 = 'F24'
    }
    if ($names.ContainsKey($vk)) { return $names[$vk] }
    if (($vk -ge 0x30 -and $vk -le 0x39) -or ($vk -ge 0x41 -and $vk -le 0x5A)) { return [string][char]$vk }
    if ($vk -ge 0x70 -and $vk -le 0x87) { return 'F' + ($vk - 0x6F) }
    return ('0x{0:X2}' -f $vk)
}

# Pairs each record of $From with the first unused record of $To that has the
# same key, direction, and origin within $WindowNs; returns, for each, the
# match or null. Raw input from injected keys has no device handle, so a
# physical key is never paired with a key the screen reader injected.
function Find-Matches($From, $To, [scriptblock]$TimeOfFrom, [scriptblock]$TimeOfTo, [long]$WindowNs) {
    $used = New-Object 'System.Collections.Generic.HashSet[int]'
    $matched = New-Object System.Collections.Generic.List[object]
    foreach ($a in $From) {
        $ta = & $TimeOfFrom $a
        $found = $null
        for ($i = 0; $i -lt $To.Count; $i++) {
            if ($used.Contains($i)) { continue }
            $b = $To[$i]
            if ($b.Key -ne $a.Key -or $b.Up -ne $a.Up -or $b.Injected -ne $a.Injected) { continue }
            $tb = & $TimeOfTo $b
            if ([Math]::Abs($tb - $ta) -le $WindowNs) { $found = $b; [void]$used.Add($i); break }
        }
        $matched.Add($found)
    }
    return ,$matched
}

$hookKeys = @($hook | ForEach-Object {
        [pscustomobject]@{ Key = (Get-BaseKey $_.VirtualKey); Up = $_.Up; Injected = $_.Injected
            UtcNs = ([long]$_.UtcTicks - $unixEpochTicks) * 100; Extra = $_.Extra }
    })
$scriptRawKeys = @($raw | ForEach-Object {
        [pscustomobject]@{ Key = (Get-BaseKey $_.VirtualKey); Up = $_.Up; UtcNs = ([long]$_.UtcTicks - $unixEpochTicks) * 100
            Device = $_.Device; Injected = ([long]$_.Device -eq 0) }
    })
$inScriptRaw = (Find-Matches $hookKeys $scriptRawKeys { $args[0].UtcNs } { $args[0].UtcNs } 300000000)

if ($DryRun -and -not $SessionId -and -not $KeysPath) {
    for ($i = 0; $i -lt $hookKeys.Count; $i++) {
        Say ("  {0} {1} injected={2} in script raw input={3}" -f (Get-KeyName $hookKeys[$i].Key),
            $(if ($hookKeys[$i].Up) { 'up' } else { 'down' }), $hookKeys[$i].Injected, ($null -ne $inScriptRaw[$i]))
    }
    Say "Results: $results"
    return
}

# --- The recording -----------------------------------------------------------

if (-not $SessionId) {
    Write-Host ''
    $SessionId = (Read-Host 'Stop the recording, then type the session ID shown by the recorder').Trim()
}
Say "Session: $SessionId"
if ($EventsPath) {
    Say "Events: $EventsPath"
}
else {
    $recording = Join-Path (Join-Path $SessionsRoot $SessionId) 'recording.mcap'
    if (-not (Test-Path -LiteralPath $recording)) { throw "Recording not found: $recording" }
    $exportProject = Join-Path $PackageRoot 'tests\RecordingEventExport\RecordingEventExport.csproj'
    $exportOutput = Join-Path $results 'export'
    & $DotnetPath build $exportProject --configuration Release --output $exportOutput --nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The recording event export did not build.' }
    $eventsPath = Join-Path $results 'events.jsonl'
    & $DotnetPath (Join-Path $exportOutput 'RecordingEventExport.dll') $recording $eventsPath | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The recording events could not be exported.' }
}

$recorderRaw = New-Object System.Collections.Generic.List[object]
$pageKeyDowns = New-Object System.Collections.Generic.List[object]
$browserFocus = 0
$browserSelection = 0
$screenReaderUia = New-Object System.Collections.Generic.List[object]
$offsets = New-Object System.Collections.Generic.List[long]
# Lines are picked by their text before parsing: most of a recording's
# events are UI Automation records from other processes.
$ownerPattern = $null
if ($screenReaderIds.Count -gt 0) {
    $ownerPattern = '"element":\{"processId":(' + ($screenReaderIds -join '|') + ')[,}]'
}
$mouseLines = 0
foreach ($line in [IO.File]::ReadLines($eventsPath)) {
    $wanted = $line.Contains('"channel":"input.keyboard"') -or
        $line.Contains('"channel":"browser.interaction"') -or
        ($line.Contains('"channel":"browser.dispatch"') -and $line.Contains('"eventName":"keydown"'))
    if (-not $wanted -and $line.Contains('"channel":"input.mouse"') -and $mouseLines -lt 200) {
        $wanted = $true
        $mouseLines++
    }
    if (-not $wanted -and $null -ne $ownerPattern -and $line.Contains('"channel":"accessibility.uia.events"')) {
        $wanted = $line -match $ownerPattern
    }
    if (-not $wanted) { continue }
    $e = $line | ConvertFrom-Json
    if ($e.channel -like 'input.*') {
        $offsets.Add([long]$e.monotonicNanoseconds - (([DateTimeOffset]$e.observedUtc).UtcTicks - $unixEpochTicks) * 100)
    }
    if ($e.channel -eq 'input.keyboard' -and $e.eventType -eq 'raw-keyboard') {
        $recorderRaw.Add([pscustomobject]@{ Key = (Get-BaseKey $e.payload.virtualKey); Up = (($e.payload.flags -band 1) -ne 0)
                Ns = [long]$e.monotonicNanoseconds; Device = $e.payload.deviceHandle
                Injected = ([long]$e.payload.deviceHandle -eq 0) })
    }
    elseif ($e.channel -eq 'browser.dispatch' -and $e.eventType -eq 'dispatch-started' -and
        (Get-Prop $e.payload 'eventName') -eq 'keydown' -and (Get-Prop $e.payload 'trusted') -eq $true) {
        $pageKeyDowns.Add([pscustomobject]@{ Ns = [long]$e.monotonicNanoseconds })
    }
    elseif ($e.channel -eq 'browser.interaction' -and $e.eventType -eq 'focus-changed') { $browserFocus++ }
    elseif ($e.channel -eq 'browser.interaction' -and $e.eventType -eq 'selection-changed') { $browserSelection++ }
    elseif ($e.channel -eq 'accessibility.uia.events') {
        $element = Get-Prop $e.payload 'element'
        $owner = Get-Prop $element 'processId'
        if ($null -ne $owner -and $screenReaderIds -contains $owner) {
            $screenReaderUia.Add([pscustomobject]@{ Ns = [long]$e.monotonicNanoseconds; Type = $e.eventType
                    Event = (Get-Prop $e.payload 'eventId'); Name = (Get-Prop $element 'name')
                    ControlType = (Get-Prop $element 'controlType') })
        }
    }
}
if ($offsets.Count -eq 0) { throw 'The recording holds no input events to align its clock with.' }
$sorted = @($offsets | Sort-Object)
$offset = $sorted[[int]($sorted.Count / 2)]
Say ("Recording: {0} raw keyboard records, {1} trusted page keydowns, {2} page focus changes, {3} page selection changes, {4} UI Automation records from NVDA" -f `
        $recorderRaw.Count, $pageKeyDowns.Count, $browserFocus, $browserSelection, $screenReaderUia.Count)

$inRecorderRaw = (Find-Matches $hookKeys $recorderRaw.ToArray() { $args[0].UtcNs + $offset } { $args[0].Ns } 300000000)

# A keydown reached the page when a trusted keydown was dispatched from
# 250 ms before to 400 ms after it, each dispatch used once.
$usedDispatch = New-Object 'System.Collections.Generic.HashSet[int]'
$reachedPage = @(foreach ($k in $hookKeys) {
    $hit = $false
    if (-not $k.Up) {
        $t = $k.UtcNs + $offset
        for ($i = 0; $i -lt $pageKeyDowns.Count; $i++) {
            if ($usedDispatch.Contains($i)) { continue }
            $d = $pageKeyDowns[$i].Ns - $t
            if ($d -ge -250000000 -and $d -le 400000000) { $hit = $true; [void]$usedDispatch.Add($i); break }
        }
    }
    $hit
})

# --- The report --------------------------------------------------------------

Say ''
Say 'Each key press (down only), with the modifiers held:'
Say 'time      key                 injected  script-raw  recorder-raw  page'
$held = New-Object 'System.Collections.Generic.HashSet[int]'
$first = if ($hookKeys.Count -gt 0) { $hookKeys[0].UtcNs } else { 0 }
$summary = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $hookKeys.Count; $i++) {
    $k = $hookKeys[$i]
    if ($k.Up) { [void]$held.Remove($k.Key); continue }
    $modifiers = @($held | Where-Object { $_ -ne $k.Key } | ForEach-Object { Get-KeyName $_ })
    $name = (@($modifiers) + (Get-KeyName $k.Key)) -join '+'
    $isModifier = @(0x10, 0x11, 0x12, 0x2D, 0x14, 0x5B) -contains $k.Key
    if (-not $isModifier -or -not $held.Contains($k.Key)) {
        $row = [pscustomobject]@{ Seconds = [Math]::Round(($k.UtcNs - $first) / 1e9, 2); Key = $name; Injected = $k.Injected
            ScriptRaw = ($null -ne $inScriptRaw[$i]); RecorderRaw = ($null -ne $inRecorderRaw[$i]); Page = [bool]$reachedPage[$i] }
        $summary.Add($row)
        Say ('{0,8:N2}  {1,-18}  {2,-8}  {3,-10}  {4,-12}  {5}' -f $row.Seconds, $row.Key, $row.Injected, $row.ScriptRaw, $row.RecorderRaw, $row.Page)
    }
    [void]$held.Add($k.Key)
}
$physical = @($summary | Where-Object { -not $_.Injected })
Say ''
Say ("Physical key presses: {0}; missing from the script's raw input: {1}; missing from the recorder's raw input: {2}; reached the page: {3}" -f `
        $physical.Count, @($physical | Where-Object { -not $_.ScriptRaw }).Count,
    @($physical | Where-Object { -not $_.RecorderRaw }).Count, @($physical | Where-Object { $_.Page }).Count)
Say ('Missing from the recorder: ' + ((@($physical | Where-Object { -not $_.RecorderRaw } | ForEach-Object { $_.Key }) -join ', ')))
Say ''
Say 'UI Automation records from NVDA''s own windows (first 40):'
foreach ($u in @($screenReaderUia | Select-Object -First 40)) {
    Say ('  {0,8:N2}  {1}  {2}  {3}  "{4}"' -f ((($u.Ns - $offset) - $first) / 1e9), $u.Type, $u.Event, $u.ControlType, $u.Name)
}
$summary | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $results 'summary.json') -Encoding UTF8
Say ''
Say "Results: $results"

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Recorder.Contracts;

namespace Recorder.Collectors.Windowing;

public sealed class ForegroundWindowCollector : ICaptureCollector
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const uint WmQuit = 0x0012;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint DwmwaCloaked = 14;

    private readonly object _gate = new();
    private readonly Channel<WindowObservation> _observations;
    private Thread? _hookThread;
    private Task? _processorTask;
    private TaskCompletionSource<bool>? _ready;
    private CollectorInitializationContext? _context;
    private WinEventProc? _callback;
    private uint _hookThreadId;
    private long _eventSequence = -1;
    private long _lifecycleSequence = -1;
    private long _dropped;
    private bool _disposed;

    public ForegroundWindowCollector()
    {
        Descriptor = CollectorDescriptor.Create(
            "windows.foreground-window",
            nameof(ForegroundWindowCollector),
            typeof(ForegroundWindowCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            ["window.foreground"],
            "win32.set-win-event-hook");
        _observations = Channel.CreateBounded<WindowObservation>(
            new BoundedChannelOptions(1_024)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
    }

    public CollectorDescriptor Descriptor { get; }
    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;
    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

    public ValueTask<CapabilityResult> InitializeAsync(
        CollectorInitializationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Created)
            {
                return ValueTask.FromResult(new CapabilityResult(
                    CapabilityStatus.Incompatible,
                    Descriptor.Channels,
                    ["collector-already-initialized"],
                    false,
                    false));
            }

            LifecycleState = CollectorLifecycleState.Initializing;
            _context = context;

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                LifecycleState = CollectorLifecycleState.Failed;
                HealthState = CollectorHealthState.Failed;
                return ValueTask.FromResult(new CapabilityResult(
                    CapabilityStatus.Incompatible,
                    Descriptor.Channels,
                    ["requires-windows-10-2004-or-later"],
                    false,
                    false));
            }

            LifecycleState = CollectorLifecycleState.Ready;
            HealthState = CollectorHealthState.Healthy;
            return ValueTask.FromResult(CapabilityResult.Supported(Descriptor.Channels.ToArray()));
        }
    }

    public async ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Ready)
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot start from {LifecycleState}.");
            }

            LifecycleState = CollectorLifecycleState.Starting;
            _ready = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _processorTask = ProcessObservationsAsync();
            _hookThread = new Thread(HookLoop)
            {
                IsBackground = true,
                Name = "Foreground window WinEvent hook"
            };
            _hookThread.Start();
        }

        try
        {
            await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LifecycleState = CollectorLifecycleState.Failed;
            HealthState = CollectorHealthState.Failed;
            _observations.Writer.TryComplete();
            return CollectorTransitionResult.Reject(
                LifecycleState,
                "foreground-hook-start-failed",
                ex.Message);
        }

        LifecycleState = CollectorLifecycleState.Running;
        EmitLifecycle("started", boundary);
        Enqueue(GetForegroundWindow(), "initial", null, null);
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask<CollectorTransitionResult> StopAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (LifecycleState is CollectorLifecycleState.Stopped or CollectorLifecycleState.Disposed)
            {
                return CollectorTransitionResult.Success(LifecycleState);
            }

            if (LifecycleState is not (CollectorLifecycleState.Running or CollectorLifecycleState.Failed))
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot stop from {LifecycleState}.");
            }

            LifecycleState = CollectorLifecycleState.Stopping;
            if (_hookThreadId != 0)
            {
                PostThreadMessageW(_hookThreadId, WmQuit, nuint.Zero, nint.Zero);
            }
        }

        if (_hookThread is not null)
        {
            await Task.Run(
                () => _hookThread.Join(TimeSpan.FromSeconds(5)),
                cancellationToken).ConfigureAwait(false);
        }

        _observations.Writer.TryComplete();
        if (_processorTask is not null)
        {
            await _processorTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var dropped = Interlocked.Read(ref _dropped);
        if (dropped > 0)
        {
            HealthState = CollectorHealthState.Degraded;
            EmitEvent(
                "collector-omission",
                new { reason = "foreground-window-queue-full", count = dropped },
                CollectorClosingTimestamp.Resolve(_context?.Clock, boundary),
                "evidence-dropped");
        }

        LifecycleState = CollectorLifecycleState.Stopped;
        EmitLifecycle("stopped", boundary);
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (LifecycleState is CollectorLifecycleState.Running or CollectorLifecycleState.Failed)
        {
            var now = _context?.Clock.GetElapsedNanoseconds() ?? 0;
            await StopAsync(
                new SessionBoundary(now, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }

        _disposed = true;
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private void HookLoop()
    {
        nint hook = nint.Zero;

        try
        {
            _hookThreadId = GetCurrentThreadId();
            PeekMessageW(out _, nint.Zero, 0, 0, 0);
            _callback = OnWinEvent;
            hook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                nint.Zero,
                _callback,
                0,
                0,
                WineventOutOfContext | WineventSkipOwnProcess);
            if (hook == nint.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "SetWinEventHook failed.");
            }

            _ready!.TrySetResult(true);

            while (GetMessageW(out var message, nint.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch (Exception ex)
        {
            _ready?.TrySetException(ex);
            LifecycleState = CollectorLifecycleState.Failed;
            HealthState = CollectorHealthState.Failed;
        }
        finally
        {
            if (hook != nint.Zero)
            {
                UnhookWinEvent(hook);
            }
        }
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTimeMilliseconds)
    {
        Enqueue(window, "transition", eventThreadId, eventTimeMilliseconds);
    }

    private void Enqueue(
        nint window,
        string reason,
        uint? eventThreadId,
        uint? nativeEventTimeMilliseconds)
    {
        if (window == nint.Zero || _context is null)
        {
            return;
        }

        if (!_observations.Writer.TryWrite(
                new WindowObservation(
                    window,
                    reason,
                    eventThreadId,
                    nativeEventTimeMilliseconds,
                    _context.Clock.GetElapsedNanoseconds())))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    private async Task ProcessObservationsAsync()
    {
        await foreach (var observation in _observations.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            EmitEvent(
                "foreground-window",
                ReadWindowSnapshot(observation),
                observation.MonotonicNanoseconds);
        }
    }

    private void EmitEvent(
        string eventType,
        object payload,
        long monotonicNanoseconds,
        params string[] qualityFlags)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var sequence = unchecked((ulong)Interlocked.Increment(ref _eventSequence));
        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            "window.foreground",
            sequence,
            monotonicNanoseconds,
            eventType,
            payload,
            qualityFlags));
    }

    private void EmitLifecycle(string action, SessionBoundary boundary)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var sequence = unchecked((ulong)Interlocked.Increment(ref _lifecycleSequence));
        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            "collector.lifecycle",
            sequence,
            boundary.MonotonicNanoseconds,
            "collector-lifecycle",
            new { action, state = LifecycleState.ToString(), boundary.Utc }));
    }

    private static WindowSnapshot ReadWindowSnapshot(WindowObservation observation)
    {
        var window = observation.Window;
        var threadId = GetWindowThreadProcessId(window, out var processId);
        var title = GetWindowString(window, GetWindowTextLengthW, GetWindowTextW);
        var className = GetWindowString(window, _ => 255, GetClassNameW);
        var processPath = TryGetProcessPath(processId);
        var processName = TryGetProcessName(processId);
        var rectangle = GetWindowRect(window, out var rect)
            ? ToRectangle(rect)
            : null;
        var monitor = ReadMonitor(window);
        var dpi = TryGetDpi(window);
        var cloaked = TryGetCloaked(window);

        return new WindowSnapshot(
            observation.Reason,
            observation.EventThreadId,
            observation.NativeEventTimeMilliseconds,
            window.ToInt64(),
            processId,
            threadId,
            processName,
            processPath,
            title,
            className,
            IsWindowVisible(window),
            IsIconic(window),
            IsZoomed(window),
            cloaked,
            dpi,
            rectangle,
            monitor);
    }

    private static string? TryGetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById(checked((int)processId)).ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static string? TryGetProcessPath(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == nint.Zero)
        {
            return null;
        }

        try
        {
            var capacity = 32_768u;
            var value = new StringBuilder(checked((int)capacity));
            return QueryFullProcessImageNameW(process, 0, value, ref capacity)
                ? value.ToString()
                : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string? GetWindowString(
        nint window,
        Func<nint, int> getLength,
        WindowStringReader read)
    {
        var capacity = Math.Max(getLength(window) + 1, 2);
        var value = new StringBuilder(capacity);
        return read(window, value, capacity) > 0 ? value.ToString() : null;
    }

    private static MonitorSnapshot? ReadMonitor(nint window)
    {
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return null;
        }

        var info = new MONITORINFOEXW
        {
            cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>(),
            szDevice = new string('\0', 32)
        };
        if (!GetMonitorInfoW(monitor, ref info))
        {
            return null;
        }

        return new MonitorSnapshot(
            info.szDevice.TrimEnd('\0'),
            ToRectangle(info.rcMonitor),
            ToRectangle(info.rcWork),
            (info.dwFlags & 1) != 0);
    }

    private static uint? TryGetDpi(nint window)
    {
        try
        {
            var dpi = GetDpiForWindow(window);
            return dpi == 0 ? null : dpi;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static bool? TryGetCloaked(nint window)
    {
        var cloaked = 0;
        return DwmGetWindowAttribute(
            window,
            DwmwaCloaked,
            ref cloaked,
            Marshal.SizeOf<int>()) == 0
            ? cloaked != 0
            : null;
    }

    private static RectangleSnapshot ToRectangle(RECT rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);

    private sealed record WindowObservation(
        nint Window,
        string Reason,
        uint? EventThreadId,
        uint? NativeEventTimeMilliseconds,
        long MonotonicNanoseconds);

    private sealed record WindowSnapshot(
        string Reason,
        uint? EventThreadId,
        uint? NativeEventTimeMilliseconds,
        long WindowHandle,
        uint ProcessId,
        uint ThreadId,
        string? ProcessName,
        string? ProcessPath,
        string? Title,
        string? ClassName,
        bool IsVisible,
        bool IsMinimized,
        bool IsMaximized,
        bool? IsCloaked,
        uint? Dpi,
        RectangleSnapshot? Bounds,
        MonitorSnapshot? Monitor);

    private sealed record RectangleSnapshot(
        int X,
        int Y,
        int Width,
        int Height);

    private sealed record MonitorSnapshot(
        string DeviceName,
        RectangleSnapshot Bounds,
        RectangleSnapshot WorkArea,
        bool IsPrimary);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public POINT point;
        public uint privateData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate void WinEventProc(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTimeMilliseconds);

    private delegate int WindowStringReader(
        nint window,
        StringBuilder value,
        int maximumCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(
        out MSG message,
        nint window,
        uint min,
        uint max,
        uint removeMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref MSG message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(
        uint threadId,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(
        nint window,
        StringBuilder value,
        int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(
        nint window,
        StringBuilder value,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out RECT rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint window);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFOEXW info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        uint attribute,
        ref int value,
        int valueSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(
        nint process,
        uint flags,
        StringBuilder executableName,
        ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}

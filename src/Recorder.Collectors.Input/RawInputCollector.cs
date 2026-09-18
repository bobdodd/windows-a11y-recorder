using System.ComponentModel;
using System.Runtime.InteropServices;
using Recorder.Contracts;

namespace Recorder.Collectors.Input;

public sealed class RawInputCollector : ICaptureCollector
{
    private const uint WmInput = 0x00FF;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100;
    private const uint RimTypeMouse = 0;
    private const uint RimTypeKeyboard = 1;
    private const ushort MouseMoveAbsolute = 0x0001;
    private const int GwlpUserData = -21;

    private readonly object _gate = new();
    private Thread? _messageThread;
    private TaskCompletionSource<nint>? _windowReady;
    private nint _window;
    private CollectorInitializationContext? _context;
    private ulong _keyboardSequence;
    private ulong _mouseSequence;
    private ulong _lifecycleSequence;
    private bool _disposed;
    private static readonly WndProc WindowProcedure = WindowProc;

    public RawInputCollector()
    {
        Descriptor = CollectorDescriptor.Create(
            "windows.raw-input",
            nameof(RawInputCollector),
            typeof(RawInputCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            ["input.keyboard", "input.mouse"],
            "win32.raw-input");
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
                    true));
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
                    true));
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
            _windowReady = new TaskCompletionSource<nint>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _messageThread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "Raw Input message loop"
            };
            _messageThread.Start();
        }

        try
        {
            _window = await _windowReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LifecycleState = CollectorLifecycleState.Failed;
            HealthState = CollectorHealthState.Failed;
            return CollectorTransitionResult.Reject(
                LifecycleState,
                "raw-input-start-failed",
                ex.Message);
        }

        LifecycleState = CollectorLifecycleState.Running;
        EmitLifecycle("started", boundary);
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public ValueTask<CollectorTransitionResult> StopAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (LifecycleState is CollectorLifecycleState.Stopped or CollectorLifecycleState.Disposed)
            {
                return ValueTask.FromResult(CollectorTransitionResult.Success(LifecycleState));
            }

            if (LifecycleState is not (CollectorLifecycleState.Running or CollectorLifecycleState.Failed))
            {
                return ValueTask.FromResult(CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot stop from {LifecycleState}."));
            }

            LifecycleState = CollectorLifecycleState.Stopping;
            if (_window != nint.Zero)
            {
                PostMessageW(_window, WmClose, nint.Zero, nint.Zero);
            }
        }

        _messageThread?.Join(TimeSpan.FromSeconds(5));
        LifecycleState = CollectorLifecycleState.Stopped;
        EmitLifecycle("stopped", boundary);
        return ValueTask.FromResult(CollectorTransitionResult.Success(LifecycleState));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (LifecycleState is CollectorLifecycleState.Running or CollectorLifecycleState.Failed)
        {
            var context = _context;
            var now = context is null ? 0 : context.Clock.GetElapsedNanoseconds();
            await StopAsync(new SessionBoundary(now, DateTimeOffset.UtcNow), CancellationToken.None);
        }

        _disposed = true;
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private void MessageLoop()
    {
        try
        {
            var module = GetModuleHandleW(null);
            var className = $"WindowsA11yRecorder.RawInput.{Descriptor.InstanceId}";
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = WindowProcedure,
                hInstance = module,
                lpszClassName = className
            };

            var atom = RegisterClassExW(ref windowClass);
            if (atom == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassExW failed.");
            }

            var handle = GCHandle.Alloc(this);
            try
            {
                var hwndMessage = new nint(-3);
                var window = CreateWindowExW(
                    0,
                    className,
                    className,
                    0,
                    0,
                    0,
                    0,
                    0,
                    hwndMessage,
                    nint.Zero,
                    module,
                    GCHandle.ToIntPtr(handle));
                if (window == nint.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowExW failed.");
                }

                RegisterDevices(window);
                _windowReady!.TrySetResult(window);

                while (GetMessageW(out var message, nint.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessageW(ref message);
                }
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }

                UnregisterClassW(className, module);
            }
        }
        catch (Exception ex)
        {
            _windowReady?.TrySetException(ex);
            LifecycleState = CollectorLifecycleState.Failed;
            HealthState = CollectorHealthState.Failed;
        }
    }

    private void RegisterDevices(nint window)
    {
        RAWINPUTDEVICE[] devices =
        [
            new() { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RidevInputSink, hwndTarget = window },
            new() { usUsagePage = 0x01, usUsage = 0x06, dwFlags = RidevInputSink, hwndTarget = window }
        ];

        if (!RegisterRawInputDevices(
                devices,
                (uint)devices.Length,
                (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "RegisterRawInputDevices failed.");
        }
    }

    private void HandleRawInput(nint rawInputHandle)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        if (GetRawInputData(rawInputHandle, RidInput, nint.Zero, ref size, headerSize) != 0 || size == 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (GetRawInputData(rawInputHandle, RidInput, buffer, ref size, headerSize) != size)
            {
                return;
            }

            var input = Marshal.PtrToStructure<RAWINPUT>(buffer);
            if (input.header.dwType == RimTypeKeyboard)
            {
                EmitKeyboard(input);
            }
            else if (input.header.dwType == RimTypeMouse)
            {
                EmitMouse(input);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void EmitKeyboard(RAWINPUT input)
    {
        var context = _context!;
        var keyboard = input.data.keyboard;
        var sequence = _keyboardSequence++;
        var record = RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            "input.keyboard",
            sequence,
            context.Clock.GetElapsedNanoseconds(),
            "raw-keyboard",
            new
            {
                deviceHandle = input.header.hDevice.ToInt64(),
                makeCode = keyboard.MakeCode,
                flags = keyboard.Flags,
                virtualKey = keyboard.VKey,
                message = keyboard.Message,
                extraInformation = keyboard.ExtraInformation
            },
            "receipt-time-stamp");
        context.EventSink.TryWrite(record);
    }

    private void EmitMouse(RAWINPUT input)
    {
        var context = _context!;
        var mouse = input.data.mouse;
        GetCursorPos(out var cursor);
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId);

        var sequence = _mouseSequence++;
        var record = RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            "input.mouse",
            sequence,
            context.Clock.GetElapsedNanoseconds(),
            "raw-mouse",
            new
            {
                deviceHandle = input.header.hDevice.ToInt64(),
                movementMode = (mouse.usFlags & MouseMoveAbsolute) != 0 ? "absolute" : "relative",
                deltaX = mouse.lLastX,
                deltaY = mouse.lLastY,
                buttonFlags = mouse.usButtonFlags,
                buttonData = mouse.usButtonData,
                rawButtons = mouse.ulRawButtons,
                extraInformation = mouse.ulExtraInformation,
                cursorX = cursor.X,
                cursorY = cursor.Y,
                foregroundProcessId = processId
            },
            "receipt-time-stamp",
            "cursor-position-sampled");
        context.EventSink.TryWrite(record);
    }

    private void EmitLifecycle(string action, SessionBoundary boundary)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            "collector.lifecycle",
            _lifecycleSequence++,
            boundary.MonotonicNanoseconds,
            "collector-lifecycle",
            new { action, state = LifecycleState.ToString(), boundary.Utc }));
    }

    private static nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        RawInputCollector? collector = null;

        if (message == 0x0081)
        {
            var create = Marshal.PtrToStructure<CREATESTRUCTW>(lParam);
            SetWindowLongPtrW(window, GwlpUserData, create.lpCreateParams);
        }

        var userData = GetWindowLongPtrW(window, GwlpUserData);
        if (userData != nint.Zero)
        {
            var handle = GCHandle.FromIntPtr(userData);
            collector = handle.Target as RawInputCollector;
        }

        if (message == WmInput)
        {
            collector?.HandleRawInput(lParam);
            return nint.Zero;
        }

        if (message == WmClose)
        {
            DestroyWindow(window);
            return nint.Zero;
        }

        if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return nint.Zero;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CREATESTRUCTW
    {
        public nint lpCreateParams;
        public nint hInstance;
        public nint hMenu;
        public nint hwndParent;
        public int cy;
        public int cx;
        public int y;
        public int x;
        public int style;
        public nint lpszName;
        public nint lpszClass;
        public uint dwExStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public nint hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public nint hDevice;
        public nuint wParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RAWINPUTUNION
    {
        [FieldOffset(0)] public RAWMOUSE mouse;
        [FieldOffset(0)] public RAWKEYBOARD keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWINPUTUNION data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags;
        public uint ulButtons;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;

        public readonly ushort usButtonFlags => (ushort)(ulButtons & 0xFFFF);
        public readonly ushort usButtonData => (ushort)(ulButtons >> 16);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        RAWINPUTDEVICE[] devices,
        uint numberOfDevices,
        uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        nint rawInput,
        uint command,
        nint data,
        ref uint size,
        uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref MSG message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint newValue);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}

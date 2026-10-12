using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Recorder.Contracts;

namespace Recorder.Collectors.Input;

/// <summary>
/// Records every key from a low-level keyboard hook (<c>WH_KEYBOARD_LL</c>),
/// on <c>input.keyboard-hook</c>, including the keys a screen reader keeps
/// as commands, which never reach raw input. It observes only: every call
/// passes the key on unchanged, and the callback only copies the key into a
/// queue; the records are written from another thread.
/// <list type="bullet">
/// <item>The hook runs in the recorder's own process, on a thread with a
/// message loop, so nothing is loaded into the screen reader.</item>
/// <item>A new hook goes first in the chain, so while a screen reader runs
/// the hook is installed again every second, the new one before the old is
/// removed, so no key is missed; only the newest hook records. It is also
/// installed again when a screen reader starts, and when a key from a
/// keyboard reaches raw input with no call of the hook, which shows that
/// Windows removed it.</item>
/// <item>Each installation is recorded with its reason.</item>
/// </list>
/// Agreed with the owner on 2026-10-07 and 2026-10-10. See
/// docs/architecture/screen-reader-activity.md, "The keyboard hook".
/// <para>
/// It also records every mouse event from a low-level mouse hook
/// (<c>WH_MOUSE_LL</c>), on <c>input.mouse-hook</c>, on the same thread and
/// terms: the mouse hook is installed and installed again with the keyboard
/// hook, each installation recorded, and checked against raw input's mouse
/// buttons. Agreed with the owner on 2026-10-11; see
/// docs/architecture/screen-reader-activity.md, "The mouse hook".
/// </para>
/// </summary>
public sealed class KeyboardHookCollector : ICaptureCollector
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const uint WmTimer = 0x0113;
    private const uint WmQuit = 0x0012;
    private const uint WmInstall = 0x8000 + 1;
    private const int QueueCapacity = 65_536;

    private readonly object _gate = new();
    private readonly object _detectorGate = new();
    private readonly HookLossDetector _detector =
        new(KeyboardHookRecords.JoinWindowMilliseconds * 1_000_000L);
    private readonly HookLossDetector _mouseDetector =
        new(KeyboardHookRecords.JoinWindowMilliseconds * 1_000_000L);
    private readonly Channel<Item> _queue = Channel.CreateBounded<Item>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = true
        });

    private CollectorInitializationContext? _context;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private Task? _writer;
    private TaskCompletionSource<bool>? _ready;
    private long _eventSequence = -1;
    private long _lifecycleSequence = -1;
    private volatile bool _screenReaderRunning;
    private bool _disposed;

    // Owned by the hook thread.
    private Installation? _current;
    private int _installations;
    private long _dropped;
    private long _mouseDropped;

    // Set by the writer when it finds a loss, read by the hook thread.
    private HookLoss? _pendingLoss;
    private HookLoss? _pendingMouseLoss;

    private sealed class Installation(int number, nint handle, HookProc procedure)
    {
        public int Number { get; } = number;
        public nint Handle { get; } = handle;
        public HookProc Procedure { get; } = procedure;
        public long Keys { get; set; }
        public long MaxCallbackTicks { get; set; }

        // The mouse hook installed with it; zero where it could not be.
        public nint MouseHandle { get; init; }
        public HookProc? MouseProcedure { get; init; }
        public long MouseEvents { get; set; }
        public long MouseMaxCallbackTicks { get; set; }
    }

    private abstract record Item;

    private sealed record KeyItem(long At, int Installation, KbdLlHookStruct Key) : Item;

    private sealed record MouseItem(long At, int Installation, MsLlHookStruct Mouse, int Message) : Item;

    private sealed record InstalledItem(long At, object Payload) : Item
    {
        public InstalledItem(long at, MouseInstalled mouse)
            : this(at, (object)mouse)
        {
        }
    }

    public KeyboardHookCollector()
    {
        Descriptor = CollectorDescriptor.Create(
            "windows.keyboard-hook",
            nameof(KeyboardHookCollector),
            typeof(KeyboardHookCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [KeyboardHookRecords.Channel, MouseHookRecords.Channel],
            "win32.low-level-keyboard-hook");
    }

    public CollectorDescriptor Descriptor { get; }
    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;
    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;
    public string? HealthReason { get; private set; }

    /// <summary>A raw input keyboard record, for the check that the hook is still installed.</summary>
    public void ObserveRawKey(RawKeyObservation key)
    {
        lock (_detectorGate)
        {
            _detector.ObserveRaw(key.At, key.ScanCode, key.VirtualKey, key.Up, key.FromDevice);
        }
    }

    /// <summary>A raw input mouse record's buttons, for the check that the mouse hook is still installed.</summary>
    public void ObserveRawMouse(RawMouseObservation mouse)
    {
        lock (_detectorGate)
        {
            foreach (var (button, up) in MouseHookRecords.RawButtons(mouse.ButtonFlags))
            {
                _mouseDetector.ObserveRaw(mouse.At, button, 0, up, mouse.FromDevice);
            }
        }
    }

    /// <summary>
    /// Whether a screen reader runs, from assistive technology tracking:
    /// while it does the hook is installed again every second, and at once
    /// when one starts during the recording.
    /// </summary>
    public void SetScreenReaderRunning(bool running, bool started)
    {
        _screenReaderRunning = running;
        if (started && _hookThreadId != 0)
        {
            PostThreadMessageW(_hookThreadId, WmInstall, 1, 0);
        }
    }

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

            _context = context;
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
            _writer = Task.Run(WriteLoopAsync, CancellationToken.None);
            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _hookThread = new Thread(HookLoop)
            {
                IsBackground = true,
                Name = "Keyboard and mouse hooks"
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
            HealthReason = ex.Message;
            return CollectorTransitionResult.Reject(LifecycleState, "keyboard-hook-start-failed", ex.Message);
        }

        LifecycleState = CollectorLifecycleState.Running;
        EmitLifecycle("started", boundary);
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
        }

        if (_hookThreadId != 0)
        {
            PostThreadMessageW(_hookThreadId, WmQuit, 0, 0);
        }

        if (_hookThread is { } thread)
        {
            await Task.Run(() => thread.Join(TimeSpan.FromSeconds(5)), cancellationToken).ConfigureAwait(false);
        }

        _queue.Writer.TryComplete();
        if (_writer is { } writer)
        {
            await writer.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
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
            await StopAsync(new SessionBoundary(now, DateTimeOffset.UtcNow), CancellationToken.None)
                .ConfigureAwait(false);
        }

        _disposed = true;
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    // --- The hook thread ------------------------------------------------------

    private void HookLoop()
    {
        _hookThreadId = GetCurrentThreadId();

        // Makes the thread's message queue, so posted messages are kept.
        PeekMessageW(out _, nint.Zero, 0, 0, 0);
        if (!Install(KeyboardHookRecords.RecordingStartedReason, KeyboardHookRecords.RecordingStartedReason))
        {
            _ready?.TrySetException(new Win32Exception("The keyboard hook could not be installed."));
            return;
        }

        var timer = SetTimer(nint.Zero, 0, KeyboardHookRecords.RefreshMilliseconds, nint.Zero);
        _ready?.TrySetResult(true);
        while (GetMessageW(out var message, nint.Zero, 0, 0) > 0)
        {
            switch (message.Message)
            {
                case WmTimer when _screenReaderRunning:
                    Install(KeyboardHookRecords.RefreshReason, KeyboardHookRecords.RefreshReason);
                    break;
                case WmTimer:
                    break;
                case WmInstall when message.WParam == 1:
                    Install(KeyboardHookRecords.ScreenReaderStartedReason, KeyboardHookRecords.ScreenReaderStartedReason);
                    break;
                case WmInstall when message.WParam == 2:
                    Install(KeyboardHookRecords.HookLostReason, MouseHookRecords.KeyboardHookLostReason);
                    break;
                case WmInstall when message.WParam == 3:
                    Install(KeyboardHookRecords.MouseHookLostReason, KeyboardHookRecords.HookLostReason);
                    break;
                default:
                    TranslateMessage(ref message);
                    DispatchMessageW(ref message);
                    break;
            }
        }

        if (timer != 0)
        {
            KillTimer(nint.Zero, timer);
        }

        if (_current is { } current)
        {
            UnhookWindowsHookEx(current.Handle);
            if (current.MouseHandle != 0)
            {
                UnhookWindowsHookEx(current.MouseHandle);
            }

            _current = null;
        }
    }

    // Installs new hooks ahead of the current ones, then removes the
    // current ones, so that a key or mouse event is always seen by one of
    // them. The keyboard hook decides: where it cannot be installed the
    // current hooks stay. A mouse hook that cannot be installed is recorded
    // and leaves the keyboard hook recording.
    private bool Install(string reason, string mouseReason)
    {
        var number = ++_installations;
        var at = Now();
        var loss = reason == KeyboardHookRecords.HookLostReason
            ? Interlocked.Exchange(ref _pendingLoss, null)
            : null;
        var mouseLoss = mouseReason == KeyboardHookRecords.HookLostReason
            ? Interlocked.Exchange(ref _pendingMouseLoss, null)
            : null;

        HookProc procedure = null!;
        procedure = (code, wParam, lParam) => OnKey(number, code, wParam, lParam);
        var handle = SetWindowsHookExW(WhKeyboardLl, procedure, GetModuleHandleW(null), 0);
        var previous = _current;
        var dropped = Interlocked.Exchange(ref _dropped, 0);
        var mouseDropped = Interlocked.Exchange(ref _mouseDropped, 0);
        if (handle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            HealthState = CollectorHealthState.Degraded;
            HealthReason = $"The keyboard hook could not be installed: error {error}.";
            Queue(new InstalledItem(at, Payload(number, reason, null, dropped, loss,
                $"SetWindowsHookEx failed with error {error}.")));
            Queue(new InstalledItem(at, MousePayload(number, mouseReason, null, mouseDropped, mouseLoss,
                "The keyboard hook could not be installed, so the mouse hook was not installed again.")));
            return false;
        }

        HookProc mouseProcedure = null!;
        mouseProcedure = (code, wParam, lParam) => OnMouse(number, code, wParam, lParam);
        var mouseHandle = SetWindowsHookExW(WhMouseLl, mouseProcedure, GetModuleHandleW(null), 0);
        string? mouseProblem = null;
        if (mouseHandle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            HealthState = CollectorHealthState.Degraded;
            HealthReason = $"The mouse hook could not be installed: error {error}.";
            mouseProblem = $"SetWindowsHookEx failed with error {error}.";
        }

        _current = new Installation(number, handle, procedure)
        {
            MouseHandle = mouseHandle,
            MouseProcedure = mouseHandle == 0 ? null : mouseProcedure
        };
        if (previous is not null)
        {
            UnhookWindowsHookEx(previous.Handle);
            if (previous.MouseHandle != 0)
            {
                UnhookWindowsHookEx(previous.MouseHandle);
            }
        }

        Queue(new InstalledItem(at, Payload(number, reason, previous, dropped, loss, null)));
        Queue(new InstalledItem(at, MousePayload(
            number, mouseReason, previous is { MouseHandle: not 0 } ? previous : null, mouseDropped, mouseLoss, mouseProblem)));
        return true;
    }

    private static MouseInstalled MousePayload(
        int number,
        string reason,
        Installation? previous,
        long dropped,
        HookLoss? loss,
        string? problem) => new(new
    {
        installation = number,
        reason,
        installed = problem is null,
        problem,
        previousInstallation = previous?.Number,
        previousEvents = previous?.MouseEvents,
        previousMaxCallbackMicroseconds = previous is null
            ? (double?)null
            : Math.Round(previous.MouseMaxCallbackTicks * 1_000_000.0 / Stopwatch.Frequency, 1),
        eventsDropped = dropped,
        lastHookEventAt = loss?.LastHookKeyAt,
        unmatchedRawButtonAt = loss?.UnmatchedRawKeyAt,
        unmatchedButton = loss?.UnmatchedScanCode
    });

    // A mouse hook installation's payload, told apart from the keyboard's.
    private sealed record MouseInstalled(object Payload);

    private static object Payload(
        int number,
        string reason,
        Installation? previous,
        long dropped,
        HookLoss? loss,
        string? problem) => new
    {
        installation = number,
        reason,
        installed = problem is null,
        problem,
        previousInstallation = previous?.Number,
        previousKeys = previous?.Keys,
        previousMaxCallbackMicroseconds = previous is null
            ? (double?)null
            : Math.Round(previous.MaxCallbackTicks * 1_000_000.0 / Stopwatch.Frequency, 1),
        keysDropped = dropped,
        lastHookKeyAt = loss?.LastHookKeyAt,
        unmatchedRawKeyAt = loss?.UnmatchedRawKeyAt,
        unmatchedScanCode = loss?.UnmatchedScanCode
    };

    // The callback: copies the key into the queue and passes it on. Only
    // the newest hook records; an older one, until it is removed, passes on
    // the keys the newest has already seen.
    private nint OnKey(int number, int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _current is { } current && current.Number == number)
        {
            var started = Stopwatch.GetTimestamp();
            var key = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if (!_queue.Writer.TryWrite(new KeyItem(Now(), number, key)))
            {
                Interlocked.Increment(ref _dropped);
            }

            current.Keys++;
            current.MaxCallbackTicks = Math.Max(current.MaxCallbackTicks, Stopwatch.GetTimestamp() - started);
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    // The mouse callback: copies the event into the queue and passes it
    // on, as the keyboard callback does.
    private nint OnMouse(int number, int code, nint wParam, nint lParam)
    {
        if (code >= 0 && _current is { } current && current.Number == number)
        {
            var started = Stopwatch.GetTimestamp();
            var mouse = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            if (!_queue.Writer.TryWrite(new MouseItem(Now(), number, mouse, (int)wParam)))
            {
                Interlocked.Increment(ref _mouseDropped);
            }

            current.MouseEvents++;
            current.MouseMaxCallbackTicks = Math.Max(current.MouseMaxCallbackTicks, Stopwatch.GetTimestamp() - started);
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private void Queue(Item item)
    {
        if (!_queue.Writer.TryWrite(item))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    // --- The writer -------------------------------------------------------------

    private async Task WriteLoopAsync()
    {
        var reader = _queue.Reader;
        while (true)
        {
            while (reader.TryRead(out var item))
            {
                Write(item);
            }

            if (reader.Completion.IsCompleted)
            {
                return;
            }

            CheckForLoss();
            var waiting = reader.WaitToReadAsync().AsTask();
            if (await Task.WhenAny(waiting, Task.Delay(100)).ConfigureAwait(false) == waiting &&
                !await waiting.ConfigureAwait(false))
            {
                while (reader.TryRead(out var item))
                {
                    Write(item);
                }

                return;
            }
        }
    }

    private void Write(Item item)
    {
        switch (item)
        {
            case KeyItem key:
                var flags = (int)key.Key.Flags;
                lock (_detectorGate)
                {
                    _detector.ObserveHook(
                        key.At,
                        (int)key.Key.ScanCode,
                        (flags & KeyboardHookRecords.UpFlag) != 0,
                        (flags & KeyboardHookRecords.InjectedFlag) != 0);
                }

                Emit(KeyboardHookRecords.KeyEventType, new
                {
                    installation = key.Installation,
                    virtualKey = (int)key.Key.VirtualKey,
                    scanCode = (int)key.Key.ScanCode,
                    flags,
                    up = (flags & KeyboardHookRecords.UpFlag) != 0,
                    extended = (flags & KeyboardHookRecords.ExtendedFlag) != 0,
                    injected = (flags & KeyboardHookRecords.InjectedFlag) != 0,
                    lowerIntegrityInjected = (flags & KeyboardHookRecords.LowerIntegrityInjectedFlag) != 0,
                    altDown = (flags & KeyboardHookRecords.AltDownFlag) != 0,
                    extraInformation = (long)key.Key.ExtraInformation,
                    eventTimeMilliseconds = (long)key.Key.Time
                }, key.At);
                break;
            case MouseItem mouse:
                var mouseFlags = (int)mouse.Mouse.Flags;
                var mouseData = unchecked((int)mouse.Mouse.MouseData);
                var (kind, button) = MouseHookRecords.Describe(mouse.Message, mouseData);
                var injected = (mouseFlags & MouseHookRecords.InjectedFlag) != 0;
                if (MouseHookRecords.HookButton(kind, button) is { } hookButton)
                {
                    lock (_detectorGate)
                    {
                        _mouseDetector.ObserveHook(mouse.At, hookButton.Button, hookButton.Up, injected);
                    }
                }

                var extra = (long)mouse.Mouse.ExtraInformation;
                Emit(MouseHookRecords.EventType, new
                {
                    installation = mouse.Installation,
                    message = mouse.Message,
                    kind,
                    button,
                    x = mouse.Mouse.X,
                    y = mouse.Mouse.Y,
                    mouseData,
                    wheelDelta = MouseHookRecords.WheelDelta(kind, mouseData),
                    flags = mouseFlags,
                    injected,
                    lowerIntegrityInjected = (mouseFlags & MouseHookRecords.LowerIntegrityInjectedFlag) != 0,
                    penOrTouch = MouseHookRecords.PenOrTouch(extra),
                    extraInformation = extra,
                    eventTimeMilliseconds = (long)mouse.Mouse.Time
                }, mouse.At, MouseHookRecords.Channel);
                break;
            case InstalledItem { Payload: MouseInstalled mouseInstalled } installed:
                Emit(MouseHookRecords.InstalledEventType, mouseInstalled.Payload, installed.At, MouseHookRecords.Channel);
                break;
            case InstalledItem installed:
                Emit(KeyboardHookRecords.InstalledEventType, installed.Payload, installed.At);
                break;
        }
    }

    private void CheckForLoss()
    {
        HookLoss? loss;
        HookLoss? mouseLoss;
        lock (_detectorGate)
        {
            var now = Now();
            loss = _detector.Poll(now);
            mouseLoss = _mouseDetector.Poll(now);
        }

        if (loss is not null && _hookThreadId != 0)
        {
            Volatile.Write(ref _pendingLoss, loss);
            PostThreadMessageW(_hookThreadId, WmInstall, 2, 0);
        }
        else if (mouseLoss is not null && _hookThreadId != 0)
        {
            Volatile.Write(ref _pendingMouseLoss, mouseLoss);
            PostThreadMessageW(_hookThreadId, WmInstall, 3, 0);
        }
    }

    // --- Events -----------------------------------------------------------------

    private long Now() => _context?.Clock.GetElapsedNanoseconds() ?? 0;

    private void Emit(string eventType, object payload, long monotonicNanoseconds, string channel = KeyboardHookRecords.Channel)
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
            channel,
            sequence,
            monotonicNanoseconds,
            eventType,
            payload,
            "receipt-time-stamp"));
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

    // --- Native -----------------------------------------------------------------

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KbdLlHookStruct
    {
        public readonly uint VirtualKey;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInformation;
    }

    // MSLLHOOKSTRUCT: the point, then the mouse data, flags, time, and
    // extra information.
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MsLlHookStruct
    {
        public readonly int X;
        public readonly int Y;
        public readonly uint MouseData;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int hook, HookProc procedure, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Msg message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out Msg message, nint window, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref Msg message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint procedure);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(nint window, nuint id);
}

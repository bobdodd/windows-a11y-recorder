using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Recorder.Contracts;

namespace Recorder.Collectors.Graphics;

public sealed class DesktopFrameCollector : ICaptureCollector
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint Srccopy = 0x00CC0020;
    private const uint Captureblt = 0x40000000;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;

    private readonly object _gate = new();
    private readonly TimeSpan _frameInterval;
    private CollectorInitializationContext? _context;
    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;
    private string? _framesDirectory;
    private long _frameSequence = -1;
    private long _lifecycleSequence = -1;
    private long _failedFrames;
    private long _gdiFallbackFrames;
    private WindowsGraphicsCaptureBackend? _windowsGraphicsCapture;
    private string? _windowsGraphicsCaptureFailure;
    private bool _disposed;

    public DesktopFrameCollector(int framesPerSecond = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(framesPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(framesPerSecond, 30);
        FramesPerSecond = framesPerSecond;
        _frameInterval = TimeSpan.FromSeconds(1d / framesPerSecond);
        Descriptor = CollectorDescriptor.Create(
            "windows.desktop-frames",
            nameof(DesktopFrameCollector),
            typeof(DesktopFrameCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            ["graphics.desktop.frames"],
            "windows-graphics-capture-with-gdi-fallback");
    }

    public int FramesPerSecond { get; }
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

            var width = GetSystemMetrics(SmCxVirtualScreen);
            var height = GetSystemMetrics(SmCyVirtualScreen);
            if (width <= 0 || height <= 0)
            {
                LifecycleState = CollectorLifecycleState.Failed;
                HealthState = CollectorHealthState.Failed;
                return ValueTask.FromResult(new CapabilityResult(
                    CapabilityStatus.Unavailable,
                    Descriptor.Channels,
                    ["virtual-desktop-bounds-unavailable"],
                    false,
                    true));
            }

            _framesDirectory = Path.Combine(
                context.SessionDirectory,
                "frames",
                "desktop");
            Directory.CreateDirectory(_framesDirectory);
            try
            {
                _windowsGraphicsCapture = WindowsGraphicsCaptureBackend.Create();
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                _windowsGraphicsCaptureFailure =
                    $"{exception.GetType().Name}: {exception.Message}";
            }

            LifecycleState = CollectorLifecycleState.Ready;
            HealthState = CollectorHealthState.Healthy;
            return ValueTask.FromResult(new CapabilityResult(
                CapabilityStatus.SupportedWithLimitations,
                Descriptor.Channels,
                _windowsGraphicsCapture is null
                    ?
                    [
                        "windows-graphics-capture-unavailable",
                        "using-gdi-fallback",
                        "protected-content-may-be-blank",
                        "hardware-overlays-may-not-be-captured"
                    ]
                    :
                    [
                        "protected-content-may-be-blank",
                        "capture-exclusion-may-hide-content"
                    ],
                true,
                false));
        }
    }

    public ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Ready)
            {
                return ValueTask.FromResult(CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot start from {LifecycleState}."));
            }

            LifecycleState = CollectorLifecycleState.Starting;
            _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            _captureTask = Task.Run(
                () => CaptureLoopAsync(_captureCancellation.Token),
                CancellationToken.None);
            LifecycleState = CollectorLifecycleState.Running;
        }

        EmitLifecycle("started", boundary);
        return ValueTask.FromResult(CollectorTransitionResult.Success(LifecycleState));
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
            _captureCancellation?.Cancel();
        }

        if (_captureTask is not null)
        {
            try
            {
                await _captureTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_captureCancellation?.IsCancellationRequested == true)
            {
            }
        }

        var failedFrames = Interlocked.Read(ref _failedFrames);
        if (failedFrames > 0)
        {
            HealthState = CollectorHealthState.Degraded;
            EmitFrameEvent(
                "collector-omission",
                new { reason = "desktop-frame-capture-failed", count = failedFrames },
                CollectorClosingTimestamp.Resolve(_context?.Clock, boundary),
                unchecked((ulong)Interlocked.Increment(ref _frameSequence)),
                "frame-capture-failed");
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

        _captureCancellation?.Dispose();
        _windowsGraphicsCapture?.Dispose();
        _disposed = true;
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_frameInterval);

        do
        {
            var capturedAt = _context!.Clock.GetElapsedNanoseconds();
            var sequence = unchecked((ulong)Interlocked.Increment(ref _frameSequence));
            try
            {
                CaptureFrame(capturedAt, sequence);
            }
            catch (ExternalException)
            {
                Interlocked.Increment(ref _failedFrames);
            }
            catch (IOException)
            {
                Interlocked.Increment(ref _failedFrames);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
        while (!cancellationToken.IsCancellationRequested);
    }

    private void CaptureFrame(long capturedAt, ulong sequence)
    {
        var x = GetSystemMetrics(SmXVirtualScreen);
        var y = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        var stride = checked(width * 4);
        var pixels = GC.AllocateUninitializedArray<byte>(checked(stride * height));
        var startedAt = _context!.Clock.GetElapsedNanoseconds();

        var backend = "windows-graphics-capture";
        string[] qualityFlags = ["hardware-composed-wgc"];
        if (_windowsGraphicsCapture is not null)
        {
            try
            {
                _windowsGraphicsCapture.CapturePixels(
                    x,
                    y,
                    width,
                    height,
                    pixels);
            }
            catch (Exception exception) when (!IsFatal(exception))
            {
                _windowsGraphicsCaptureFailure =
                    $"{exception.GetType().Name}: {exception.Message}";
                _windowsGraphicsCapture.Dispose();
                _windowsGraphicsCapture = null;
                CapturePixels(x, y, width, height, pixels);
                Interlocked.Increment(ref _gdiFallbackFrames);
                backend = "gdi-bitblt";
                qualityFlags =
                [
                    "software-gdi-fallback",
                    "wgc-runtime-failure"
                ];
            }
        }
        else
        {
            CapturePixels(x, y, width, height, pixels);
            Interlocked.Increment(ref _gdiFallbackFrames);
            backend = "gdi-bitblt";
            qualityFlags = ["software-gdi-fallback"];
        }

        var relativePath = Path.Combine(
            "frames",
            "desktop",
            $"{sequence:D10}.png");
        var finalPath = Path.Combine(_context.SessionDirectory, relativePath);
        var temporaryPath = finalPath + ".tmp";
        WritePng(temporaryPath, width, height, stride, pixels);
        File.Move(temporaryPath, finalPath);

        var completedAt = _context.Clock.GetElapsedNanoseconds();
        EmitFrameEvent(
            "desktop-frame",
            new
            {
                path = relativePath.Replace('\\', '/'),
                x,
                y,
                width,
                height,
                stride,
                pixelFormat = "B8G8R8A8",
                encodedFormat = "png",
                byteLength = new FileInfo(finalPath).Length,
                captureDurationNanoseconds = completedAt - startedAt,
                framesPerSecond = FramesPerSecond,
                backend,
                monitorCount = _windowsGraphicsCapture?.MonitorCount,
                fallbackReason = backend == "gdi-bitblt"
                    ? _windowsGraphicsCaptureFailure
                    : null,
                gdiFallbackFrameCount = Interlocked.Read(ref _gdiFallbackFrames)
            },
            capturedAt,
            sequence,
            qualityFlags);
    }

    private static void CapturePixels(
        int x,
        int y,
        int width,
        int height,
        byte[] destination)
    {
        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC failed.");
        }

        var memoryDc = CreateCompatibleDC(screenDc);
        if (memoryDc == nint.Zero)
        {
            ReleaseDC(nint.Zero, screenDc);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateCompatibleDC failed.");
        }

        nint bitmap = nint.Zero;
        nint previousObject = nint.Zero;

        try
        {
            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BiRgb
                }
            };
            bitmap = CreateDIBSection(
                screenDc,
                ref info,
                DibRgbColors,
                out var pixelPointer,
                nint.Zero,
                0);
            if (bitmap == nint.Zero || pixelPointer == nint.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "CreateDIBSection failed.");
            }

            previousObject = SelectObject(memoryDc, bitmap);
            if (!BitBlt(
                    memoryDc,
                    0,
                    0,
                    width,
                    height,
                    screenDc,
                    x,
                    y,
                    Srccopy | Captureblt))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt failed.");
            }

            Marshal.Copy(pixelPointer, destination, 0, destination.Length);
        }
        finally
        {
            if (previousObject != nint.Zero)
            {
                SelectObject(memoryDc, previousObject);
            }

            if (bitmap != nint.Zero)
            {
                DeleteObject(bitmap);
            }

            DeleteDC(memoryDc);
            ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static bool IsFatal(Exception exception)
    {
        return exception is StackOverflowException or
            AccessViolationException;
    }

    private static void WritePng(
        string path,
        int width,
        int height,
        int stride,
        byte[] pixels)
    {
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        encoder.Save(stream);
        stream.Flush(flushToDisk: true);
    }

    private void EmitFrameEvent(
        string eventType,
        object payload,
        long monotonicNanoseconds,
        ulong sequence,
        params string[] qualityFlags)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            "graphics.desktop.frames",
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

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RGBQUAD
    {
        public byte rgbBlue;
        public byte rgbGreen;
        public byte rgbRed;
        public byte rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public RGBQUAD bmiColors;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateDIBSection(
        nint deviceContext,
        ref BITMAPINFO bitmapInfo,
        uint usage,
        out nint pixels,
        nint section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        nint destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        nint source,
        int sourceX,
        int sourceY,
        uint operation);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);
}

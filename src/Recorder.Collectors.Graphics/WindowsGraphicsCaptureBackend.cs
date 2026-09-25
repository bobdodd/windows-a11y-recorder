using System.ComponentModel;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Foundation;
using Recorder.Contracts;
using WinRT;
using WinRT.Interop;
using static Vortice.Direct3D11.D3D11;

namespace Recorder.Collectors.Graphics;

internal sealed class WindowsGraphicsCaptureBackend : IDisposable
{
    private const uint MonitorInfoPrimary = 1;
    private static readonly Guid GraphicsCaptureItemInteropId =
        new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid GraphicsCaptureItemId =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Direct3DDxgiInterfaceAccessId =
        new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid Direct3DDeviceId =
        new("A37624AB-8D5F-4650-9D3E-9EAE3D9BC670");
    private static readonly Guid Direct3DTexture2DId =
        new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    private readonly ID3D11Device _nativeDevice;
    private readonly ID3D11DeviceContext _deviceContext;
    private readonly IDirect3DDevice _winRtDevice;
    private readonly List<MonitorCapture> _monitors;
    private bool _disposed;

    private WindowsGraphicsCaptureBackend(
        ID3D11Device nativeDevice,
        ID3D11DeviceContext deviceContext,
        IDirect3DDevice winRtDevice,
        List<MonitorCapture> monitors)
    {
        _nativeDevice = nativeDevice;
        _deviceContext = deviceContext;
        _winRtDevice = winRtDevice;
        _monitors = monitors;
    }

    public int MonitorCount => _monitors.Count;

    // sessionNanoseconds is read on the capture pool's worker threads when a
    // frame arrives, so it must be safe to call from any thread.
    public static WindowsGraphicsCaptureBackend Create(Func<long> sessionNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(sessionNanoseconds);
        bool isSupported;
        try
        {
            isSupported = GraphicsCaptureSession.IsSupported();
        }
        catch (Exception exception)
        {
            throw StageFailure("check-support", exception);
        }

        if (!isSupported)
        {
            throw new PlatformNotSupportedException(
                "Windows Graphics Capture is not supported in this session.");
        }

        ID3D11Device nativeDevice;
        ID3D11DeviceContext deviceContext;
        try
        {
            var createResult = D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                [
                    FeatureLevel.Level_11_1,
                    FeatureLevel.Level_11_0,
                    FeatureLevel.Level_10_1,
                    FeatureLevel.Level_10_0
                ],
                out nativeDevice,
                out deviceContext);
            createResult.CheckError();
        }
        catch (Exception exception)
        {
            throw StageFailure("create-d3d11-device", exception);
        }

        IDirect3DDevice? winRtDevice = null;
        var monitors = new List<MonitorCapture>();
        try
        {
            try
            {
                winRtDevice = CreateWinRtDevice(nativeDevice);
            }
            catch (Exception exception)
            {
                throw StageFailure("create-winrt-device", exception);
            }

            IReadOnlyList<MonitorDefinition> monitorDefinitions;
            try
            {
                monitorDefinitions = EnumerateMonitors();
            }
            catch (Exception exception)
            {
                throw StageFailure("enumerate-monitors", exception);
            }

            foreach (var monitor in monitorDefinitions)
            {
                try
                {
                    monitors.Add(new MonitorCapture(
                        nativeDevice,
                        deviceContext,
                        winRtDevice,
                        monitor,
                        sessionNanoseconds));
                }
                catch (Exception exception)
                {
                    throw StageFailure("create-monitor-capture", exception);
                }
            }

            if (monitors.Count == 0)
            {
                throw new InvalidOperationException("No display monitors were found.");
            }

            foreach (var monitor in monitors)
            {
                try
                {
                    monitor.Start();
                }
                catch (Exception exception)
                {
                    throw StageFailure("start-monitor-capture", exception);
                }
            }

            return new WindowsGraphicsCaptureBackend(
                nativeDevice,
                deviceContext,
                winRtDevice,
                monitors);
        }
        catch
        {
            foreach (var monitor in monitors)
            {
                monitor.Dispose();
            }

            winRtDevice?.Dispose();
            deviceContext.Dispose();
            nativeDevice.Dispose();
            throw;
        }
    }

    private static InvalidOperationException StageFailure(
        string stage,
        Exception exception)
    {
        return new InvalidOperationException(
            $"WGC stage '{stage}' failed: {exception.Message}",
            exception);
    }

    internal IReadOnlyList<MonitorFrameTiming> CapturePixels(
        int virtualX,
        int virtualY,
        int virtualWidth,
        int virtualHeight,
        byte[] destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Array.Clear(destination);

        var timings = new MonitorFrameTiming[_monitors.Count];
        for (var index = 0; index < _monitors.Count; index++)
        {
            timings[index] = _monitors[index].CopyLatestFrame(
                virtualX,
                virtualY,
                virtualWidth,
                virtualHeight,
                destination);
        }

        return timings;
    }

    // The monitors a GDI fallback frame covers, with no composition timing,
    // so a fallback record lists the same monitors a WGC record would.
    internal static IReadOnlyList<MonitorFrameTiming> DescribeMonitorsWithoutTiming() =>
        EnumerateMonitors()
            .Select(monitor => new MonitorFrameTiming(
                monitor.Handle,
                monitor.X,
                monitor.Y,
                monitor.Width,
                monitor.Height,
                null,
                null,
                null,
                null,
                null))
            .ToArray();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var monitor in _monitors)
        {
            monitor.Dispose();
        }

        _winRtDevice.Dispose();
        _deviceContext.Dispose();
        _nativeDevice.Dispose();
        _disposed = true;
    }

    private static IDirect3DDevice CreateWinRtDevice(ID3D11Device nativeDevice)
    {
        using var dxgiDevice = nativeDevice.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(
            CreateDirect3D11DeviceFromDXGIDevice(
                dxgiDevice.NativePointer,
                out var inspectable));
        try
        {
            using var reference = ComWrappersSupport.GetObjectReferenceForInterface(
                inspectable,
                Direct3DDeviceId,
                requireQI: true);
            return reference.AsInterface<IDirect3DDevice>();
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    private static unsafe GraphicsCaptureItem CreateCaptureItem(nint monitor)
    {
        using var factory = ActivationFactory.Get(
            "Windows.Graphics.Capture.GraphicsCaptureItem",
            GraphicsCaptureItemInteropId);
        var thisPtr = factory.ThisPtr;
        var vtable = *(nint**)thisPtr;
        var createForMonitor =
            (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)vtable[4];
        var itemId = GraphicsCaptureItemId;
        nint itemPointer = nint.Zero;
        var result = createForMonitor(thisPtr, monitor, &itemId, &itemPointer);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            return GraphicsCaptureItem.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    private static IReadOnlyList<MonitorDefinition> EnumerateMonitors()
    {
        var monitors = new List<MonitorDefinition>();
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MONITORINFO
            {
                cbSize = (uint)Marshal.SizeOf<MONITORINFO>()
            };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            monitors.Add(new MonitorDefinition(
                monitor,
                info.rcMonitor.Left,
                info.rcMonitor.Top,
                info.rcMonitor.Right - info.rcMonitor.Left,
                info.rcMonitor.Bottom - info.rcMonitor.Top,
                (info.dwFlags & MonitorInfoPrimary) != 0));
            return true;
        };

        if (!EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "EnumDisplayMonitors failed.");
        }

        return monitors
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(monitor => monitor.Y)
            .ThenBy(monitor => monitor.X)
            .ToArray();
    }

    private sealed class MonitorCapture : IDisposable
    {
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly MonitorDefinition _monitor;
        private readonly GraphicsCaptureItem _item;
        private readonly Direct3D11CaptureFramePool _framePool;
        private readonly GraphicsCaptureSession _session;
        private readonly Func<long> _sessionNanoseconds;
        private readonly TypedEventHandler<Direct3D11CaptureFramePool, object> _frameArrived;

        // The newest arrived frame, written by the pool's worker threads and
        // taken by the capture thread.
        private readonly NewestArrivalSlot<Direct3D11CaptureFrame> _arrivals = new();

        // Guards pool access from the arrival handler against disposal, and
        // the arrival failure the capture thread reports.
        private readonly object _gate = new();
        private Exception? _arrivalFailure;
        private bool _disposed;

        // The last image copied into the staging texture, re-read when no
        // newer frame has arrived. Only the capture thread touches these.
        private ID3D11Texture2D? _stagingTexture;
        private bool _hasPreviousImage;
        private int _previousWidth;
        private int _previousHeight;
        private long _previousSystemRelativeTimeTicks;
        private long _previousDequeuedAt;

        public MonitorCapture(
            ID3D11Device device,
            ID3D11DeviceContext context,
            IDirect3DDevice winRtDevice,
            MonitorDefinition monitor,
            Func<long> sessionNanoseconds)
        {
            _device = device;
            _context = context;
            _monitor = monitor;
            _sessionNanoseconds = sessionNanoseconds;
            _frameArrived = OnFrameArrived;
            try
            {
                _item = CreateCaptureItem(monitor.Handle);
            }
            catch (Exception exception)
            {
                throw StageFailure("create-capture-item", exception);
            }

            if (_item.Size.Width <= 0 ||
                _item.Size.Height <= 0 ||
                _item.Size.Width > 32768 ||
                _item.Size.Height > 32768)
            {
                throw new InvalidOperationException(
                    $"WGC returned invalid monitor dimensions " +
                    $"{_item.Size.Width}x{_item.Size.Height}.");
            }

            try
            {
                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    winRtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    FramePoolBufferCount,
                    _item.Size);
            }
            catch (Exception exception)
            {
                throw StageFailure("create-frame-pool", exception);
            }

            try
            {
                _session = _framePool.CreateCaptureSession(_item);
                _session.IsCursorCaptureEnabled = true;
            }
            catch (Exception exception)
            {
                _framePool.Dispose();
                throw StageFailure("create-capture-session", exception);
            }
        }

        // Three buffers: one for the frame the arrival handler holds, one for
        // the frame the capture thread may be copying, and one free for the
        // next composition.
        private const int FramePoolBufferCount = 3;

        public void Start()
        {
            _framePool.FrameArrived += _frameArrived;
            _session.StartCapture();
        }

        // Runs on a pool worker thread for each arrival. Takes every queued
        // frame and keeps only the newest, so the pool does not stay full and
        // the frame held at a poll is the latest one that reached the pool.
        // Frames replaced here are released without being copied.
        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            lock (_gate)
            {
                if (_disposed || _arrivalFailure is not null)
                {
                    return;
                }

                while (true)
                {
                    Direct3D11CaptureFrame? next;
                    try
                    {
                        next = _framePool.TryGetNextFrame();
                    }
                    catch (Exception exception)
                    {
                        _arrivalFailure = exception;
                        return;
                    }

                    if (next is null)
                    {
                        return;
                    }

                    _arrivals.Offer(next, _sessionNanoseconds());
                }
            }
        }

        public MonitorFrameTiming CopyLatestFrame(
            int virtualX,
            int virtualY,
            int virtualWidth,
            int virtualHeight,
            byte[] destination)
        {
            Direct3D11CaptureFrame? frame = null;
            long dequeuedAt = 0;
            long superseded = 0;
            var attempts = 0;
            while (true)
            {
                attempts++;
                lock (_gate)
                {
                    if (_arrivalFailure is not null)
                    {
                        throw new InvalidOperationException(
                            $"WGC frame arrival failed for monitor {_monitor.Handle}: " +
                                _arrivalFailure.Message,
                            _arrivalFailure);
                    }
                }

                if (_arrivals.Take() is { } arrival)
                {
                    frame = arrival.Item;
                    dequeuedAt = arrival.ArrivedAt;
                    superseded = arrival.ReleasedBeforeTake;
                }

                // Only the first capture waits. Later, no arrival since the
                // previous poll means no newer frame reached the pool, so the
                // previous image is the latest one the recorder received.
                if (frame is not null || _hasPreviousImage || attempts >= 25)
                {
                    break;
                }

                Thread.Sleep(10);
            }

            if (frame is null)
            {
                if (!_hasPreviousImage)
                {
                    throw new InvalidOperationException(
                        $"No WGC frame was available for monitor {_monitor.Handle}.");
                }

                CopyStagingTexture(
                    _previousWidth,
                    _previousHeight,
                    virtualX,
                    virtualY,
                    virtualWidth,
                    virtualHeight,
                    destination);
                return new MonitorFrameTiming(
                    _monitor.Handle,
                    _monitor.X,
                    _monitor.Y,
                    _monitor.Width,
                    _monitor.Height,
                    _previousSystemRelativeTimeTicks,
                    _previousDequeuedAt,
                    attempts,
                    0,
                    true);
            }

            using (frame)
            {
                // The QPC time, in 100 ns TimeSpan ticks, at which the
                // compositor rendered this frame.
                var systemRelativeTimeTicks = frame.SystemRelativeTime.Ticks;
                var size = frame.ContentSize;
                using (var sourceTexture = GetTexture(frame.Surface))
                {
                    EnsureStagingTexture(size.Width, size.Height);
                    _context.CopyResource(_stagingTexture!, sourceTexture);
                }

                _hasPreviousImage = true;
                _previousWidth = size.Width;
                _previousHeight = size.Height;
                _previousSystemRelativeTimeTicks = systemRelativeTimeTicks;
                _previousDequeuedAt = dequeuedAt;
                CopyStagingTexture(
                    size.Width,
                    size.Height,
                    virtualX,
                    virtualY,
                    virtualWidth,
                    virtualHeight,
                    destination);

                return new MonitorFrameTiming(
                    _monitor.Handle,
                    _monitor.X,
                    _monitor.Y,
                    _monitor.Width,
                    _monitor.Height,
                    systemRelativeTimeTicks,
                    dequeuedAt,
                    attempts,
                    superseded,
                    false);
            }
        }

        private void CopyStagingTexture(
            int width,
            int height,
            int virtualX,
            int virtualY,
            int virtualWidth,
            int virtualHeight,
            byte[] destination)
        {
            var mapped = _context.Map(
                _stagingTexture!,
                0,
                MapMode.Read,
                Vortice.Direct3D11.MapFlags.None);
            try
            {
                CopyRows(
                    mapped.DataPointer,
                    checked((int)mapped.RowPitch),
                    width,
                    height,
                    virtualX,
                    virtualY,
                    virtualWidth,
                    virtualHeight,
                    destination);
            }
            finally
            {
                _context.Unmap(_stagingTexture!, 0);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            lock (_gate)
            {
                _disposed = true;
            }

            _arrivals.Dispose();

            _framePool.FrameArrived -= _frameArrived;
            _session.Dispose();
            _framePool.Dispose();
            _stagingTexture?.Dispose();
        }

        private static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
        {
            if (!ComWrappersSupport.TryUnwrapObject(surface, out var surfaceReference))
            {
                throw new InvalidOperationException(
                    "Could not unwrap the Windows Graphics Capture surface.");
            }

            using (surfaceReference)
            {
                var access = surfaceReference.AsInterface<IDirect3DDxgiInterfaceAccess>();
                try
                {
                    access.GetInterface(
                        in Direct3DTexture2DId,
                        out var texturePointer);
                    return new ID3D11Texture2D(texturePointer);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(access);
                }
            }
        }

        private void EnsureStagingTexture(int width, int height)
        {
            if (_stagingTexture is not null)
            {
                var description = _stagingTexture.Description;
                if (description.Width == width && description.Height == height)
                {
                    return;
                }

                _stagingTexture.Dispose();
            }

            _stagingTexture = _device.CreateTexture2D(new Texture2DDescription(
                Format.B8G8R8A8_UNorm,
                checked((uint)width),
                checked((uint)height),
                1,
                1,
                BindFlags.None,
                ResourceUsage.Staging,
                CpuAccessFlags.Read));
        }

        private void CopyRows(
            nint source,
            int sourceStride,
            int capturedWidth,
            int capturedHeight,
            int virtualX,
            int virtualY,
            int virtualWidth,
            int virtualHeight,
            byte[] destination)
        {
            var destinationX = _monitor.X - virtualX;
            var destinationY = _monitor.Y - virtualY;
            var copyWidth = Math.Min(
                Math.Min(capturedWidth, _monitor.Width),
                virtualWidth - destinationX);
            var copyHeight = Math.Min(
                Math.Min(capturedHeight, _monitor.Height),
                virtualHeight - destinationY);
            if (destinationX < 0 ||
                destinationY < 0 ||
                copyWidth <= 0 ||
                copyHeight <= 0)
            {
                throw new InvalidOperationException(
                    "The WGC monitor frame does not fit the virtual desktop bounds.");
            }

            var destinationStride = checked(virtualWidth * 4);
            var rowLength = checked(copyWidth * 4);
            for (var row = 0; row < copyHeight; row++)
            {
                Marshal.Copy(
                    source + checked(row * sourceStride),
                    destination,
                    checked(((destinationY + row) * destinationStride) + (destinationX * 4)),
                    rowLength);
            }
        }
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        void GetInterface(in Guid iid, out nint graphicsInterface);
    }

    internal sealed record MonitorFrameTiming(
        nint Handle,
        int X,
        int Y,
        int Width,
        int Height,
        long? SystemRelativeTimeTicks,
        long? DequeuedAtNanoseconds,
        int? TryGetNextFrameAttempts,
        long? SupersededFrameCount,
        bool? ReusedPreviousImage);

    private sealed record MonitorDefinition(
        nint Handle,
        int X,
        int Y,
        int Width,
        int Height,
        bool IsPrimary);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool MonitorEnumProc(
        nint monitor,
        nint deviceContext,
        nint monitorRectangle,
        nint data);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        nint deviceContext,
        nint clipRectangle,
        MonitorEnumProc callback,
        nint data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MONITORINFO monitorInfo);
}

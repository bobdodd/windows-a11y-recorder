using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Recorder.Collectors.Audio;

/// <summary>
/// Whether a process loopback capture records a process tree's sound or
/// every other program's.
/// </summary>
internal enum ProcessLoopbackMode
{
    // The values are PROCESS_LOOPBACK_MODE's.
    IncludeTargetProcessTree = 0,
    ExcludeTargetProcessTree = 1
}

/// <summary>
/// WASAPI process loopback: the sound one process tree plays, or the sound
/// every program but that tree plays, before it is mixed to the speakers.
/// Windows provides it from build 20348; see
/// docs/architecture/mp4-export.md, "Screen reader sound".
/// </summary>
internal sealed class ProcessLoopbackCapture : IWaveIn
{
    // The first Windows build with process loopback, per
    // AUDIOCLIENT_ACTIVATION_TYPE's documentation.
    public const int MinimumBuild = 20348;

    private const string VirtualDevice = "VAD\\Process_Loopback";
    private const ushort VtBlob = 65;
    private const int ActivationTypeProcessLoopback = 1;
    private const long BufferDuration = 2_000_000; // 200 ms in 100 ns units

    private static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    private readonly AudioClient _client;
    private readonly AutoResetEvent _ready = new(false);
    private Thread? _thread;
    private volatile bool _stopping;
    private bool _disposed;

    private ProcessLoopbackCapture(AudioClient client, WaveFormat format)
    {
        _client = client;
        WaveFormat = format;
    }

    public WaveFormat WaveFormat { get; set; }

    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public static bool IsSupported =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumBuild);

    /// <summary>
    /// Opens a capture of the tree of <paramref name="processId"/>, or of
    /// everything else, as 48 kHz 16-bit stereo, which Windows converts to.
    /// </summary>
    public static ProcessLoopbackCapture Open(int processId, ProcessLoopbackMode mode)
    {
        var format = new WaveFormat(48_000, 16, 2);
        var client = Activate(processId, mode);
        try
        {
            client.Initialize(
                AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback |
                    AudioClientStreamFlags.EventCallback |
                    AudioClientStreamFlags.AutoConvertPcm,
                BufferDuration,
                0,
                format,
                Guid.Empty);
            return new ProcessLoopbackCapture(client, format);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _client.SetEventHandle(_ready.SafeWaitHandle.DangerousGetHandle());
        _stopping = false;
        _thread = new Thread(Capture)
        {
            IsBackground = true,
            Name = "Process loopback capture"
        };
        _client.Start();
        _thread.Start();
    }

    public void StopRecording()
    {
        _stopping = true;
        _ready.Set();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopRecording();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _client.Dispose();
        _ready.Dispose();
        _disposed = true;
    }

    private void Capture()
    {
        Exception? failure = null;
        try
        {
            var capture = _client.AudioCaptureClient;
            var blockAlign = WaveFormat.BlockAlign;
            var buffer = new byte[WaveFormat.AverageBytesPerSecond];
            while (!_stopping)
            {
                _ready.WaitOne(100);
                while (!_stopping && capture.GetNextPacketSize() > 0)
                {
                    var data = capture.GetBuffer(out var frames, out var flags);
                    var length = frames * blockAlign;
                    if (buffer.Length < length)
                    {
                        buffer = new byte[length];
                    }

                    if ((flags & AudioClientBufferFlags.Silent) != 0)
                    {
                        Array.Clear(buffer, 0, length);
                    }
                    else
                    {
                        Marshal.Copy(data, buffer, 0, length);
                    }

                    capture.ReleaseBuffer(frames);
                    if (length > 0)
                    {
                        DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, length));
                    }
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                _client.Stop();
            }
            catch (COMException)
            {
                // Already stopped or the stream is gone; nothing to do.
            }
        }

        RecordingStopped?.Invoke(this, new StoppedEventArgs(failure));
    }

    private static AudioClient Activate(int processId, ProcessLoopbackMode mode)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS: the type, then
        // AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS (process id and mode).
        var parameters = Marshal.AllocHGlobal(12);
        // PROPVARIANT holding a BLOB: vt at 0, cbSize at 8, pBlobData at 16.
        var variant = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.WriteInt32(parameters, 0, ActivationTypeProcessLoopback);
            Marshal.WriteInt32(parameters, 4, processId);
            Marshal.WriteInt32(parameters, 8, (int)mode);
            for (var offset = 0; offset < 24; offset += 8)
            {
                Marshal.WriteInt64(variant, offset, 0);
            }

            Marshal.WriteInt16(variant, 0, unchecked((short)VtBlob));
            Marshal.WriteInt32(variant, 8, 12);
            Marshal.WriteIntPtr(variant, 16, parameters);

            var handler = new CompletionHandler();
            var id = AudioClientId;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync(
                VirtualDevice,
                ref id,
                variant,
                handler,
                out _));
            if (!handler.Completed.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Windows did not finish opening the process loopback capture.");
            }

            return new AudioClient((IAudioClient)handler.Completed.Result);
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(parameters);
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    // Windows calls the handler on one of its threads, so it must be agile.
    [ComVisible(true)]
    private sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        private readonly TaskCompletionSource<object> _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<object> Completed => _completed.Task;

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                activateOperation.GetActivateResult(out var result, out var activated);
                if (result < 0)
                {
                    _completed.TrySetException(Marshal.GetExceptionForHR(result) ??
                        new COMException("Process loopback activation failed.", result));
                }
                else
                {
                    _completed.TrySetResult(activated);
                }
            }
            catch (Exception exception)
            {
                _completed.TrySetException(exception);
            }
        }
    }

    [ComImport]
    [Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject
    {
    }
}

/// <summary>The running screen reader whose sound is recorded on its own.</summary>
internal sealed record ScreenReaderProcess(string Name, int ProcessId)
{
    /// <summary>
    /// NVDA's main process in this Windows session: nvda.exe, the earliest
    /// started when there are several. Null when NVDA is not running.
    /// </summary>
    public static ScreenReaderProcess? FindNvda()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var processes = Process.GetProcessesByName("nvda");
        try
        {
            var candidates = new List<(Process Process, DateTime Started)>();
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId == session && !process.HasExited)
                    {
                        candidates.Add((process, process.StartTime));
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // A process that exited or that cannot be read is not
                    // the running screen reader's.
                }
            }

            return candidates.Count == 0
                ? null
                : new ScreenReaderProcess("NVDA", candidates.MinBy(item => item.Started).Process.Id);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}

using NAudio.CoreAudioApi;
using NAudio.Wave;
using Recorder.Contracts;
using System.Threading.Channels;

namespace Recorder.Collectors.Audio;

public sealed class AudioCollector : ICaptureCollector
{
    private readonly object _gate = new();
    private readonly AudioCaptureOptions _options;
    private readonly List<AudioStreamCapture> _streams = [];
    private CollectorInitializationContext? _context;
    private long _eventSequence = -1;
    private long _lifecycleSequence = -1;
    private bool _disposed;

    public AudioCollector(AudioCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.CaptureMicrophone && !options.CaptureSystemAudio)
        {
            throw new ArgumentException(
                "At least one audio stream must be selected.",
                nameof(options));
        }

        _options = options;
        var channels = new List<string>();
        if (options.CaptureMicrophone)
        {
            channels.Add("audio.microphone");
        }

        if (options.CaptureSystemAudio)
        {
            channels.Add("audio.system");
        }

        Descriptor = CollectorDescriptor.Create(
            "windows.audio",
            nameof(AudioCollector),
            typeof(AudioCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            channels,
            "windows-audio-capture");
    }

    public CollectorDescriptor Descriptor { get; }
    public CollectorLifecycleState LifecycleState { get; private set; } =
        CollectorLifecycleState.Created;
    public CollectorHealthState HealthState { get; private set; } =
        CollectorHealthState.Unknown;

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

            if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            {
                LifecycleState = CollectorLifecycleState.Failed;
                HealthState = CollectorHealthState.Failed;
                return ValueTask.FromResult(new CapabilityResult(
                    CapabilityStatus.Incompatible,
                    Descriptor.Channels,
                    ["requires-windows-10-or-later"],
                    false,
                    false));
            }

            var limitations = new List<string>();
            if (_options.CaptureMicrophone)
            {
                TryPrepareStream(
                    AudioStreamKind.Microphone,
                    CreateMicrophoneCapture,
                    limitations);
            }

            if (_options.CaptureSystemAudio)
            {
                TryPrepareStream(
                    AudioStreamKind.System,
                    static () =>
                    {
                        using var enumerator = new MMDeviceEnumerator();
                        var device = enumerator.GetDefaultAudioEndpoint(
                            DataFlow.Render,
                            Role.Console);
                        return (
                            (IWaveIn)new WasapiLoopbackCapture(device),
                            device.FriendlyName,
                            (string?)null,
                            ReadEndpointVolume(device));
                    },
                    limitations);
            }

            if (_streams.Count == 0)
            {
                LifecycleState = CollectorLifecycleState.Failed;
                HealthState = CollectorHealthState.Failed;
                return ValueTask.FromResult(new CapabilityResult(
                    CapabilityStatus.Unavailable,
                    Descriptor.Channels,
                    limitations,
                    false,
                    true));
            }

            LifecycleState = CollectorLifecycleState.Ready;
            HealthState = limitations.Count == 0
                ? CollectorHealthState.Healthy
                : CollectorHealthState.Degraded;
            return ValueTask.FromResult(new CapabilityResult(
                limitations.Count == 0
                    ? CapabilityStatus.Supported
                    : CapabilityStatus.SupportedWithLimitations,
                _streams.Select(stream => stream.Channel).ToArray(),
                limitations,
                false,
                false));
        }
    }

    public ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            var started = new List<AudioStreamCapture>();

            try
            {
                foreach (var stream in _streams)
                {
                    stream.Start();
                    started.Add(stream);
                    EmitStreamEvent("audio-stream-started", stream, boundary.MonotonicNanoseconds);
                }
            }
            catch (Exception exception)
            {
                foreach (var stream in started)
                {
                    stream.Stop();
                }

                LifecycleState = CollectorLifecycleState.Failed;
                HealthState = CollectorHealthState.Failed;
                return ValueTask.FromResult(CollectorTransitionResult.Reject(
                    LifecycleState,
                    "audio-start-failed",
                    exception.Message));
            }

            LifecycleState = CollectorLifecycleState.Running;
            EmitLifecycle("started", boundary);
            return ValueTask.FromResult(CollectorTransitionResult.Success(LifecycleState));
        }
    }

    public async ValueTask<CollectorTransitionResult> StopAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (LifecycleState is CollectorLifecycleState.Stopped or
                CollectorLifecycleState.Disposed)
            {
                return CollectorTransitionResult.Success(LifecycleState);
            }

            if (LifecycleState is not (CollectorLifecycleState.Running or
                CollectorLifecycleState.Failed))
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot stop from {LifecycleState}.");
            }

            LifecycleState = CollectorLifecycleState.Stopping;
            foreach (var stream in _streams)
            {
                stream.Stop();
            }
        }

        foreach (var stream in _streams)
        {
            await stream.WaitForStopAsync(cancellationToken).ConfigureAwait(false);
            EmitStreamEvent(
                "audio-stream-stopped",
                stream,
                CollectorClosingTimestamp.Resolve(_context?.Clock, boundary));
            if (stream.DroppedBuffers > 0)
            {
                HealthState = CollectorHealthState.Degraded;
                EmitEvent(
                    stream.Channel,
                    "collector-omission",
                    new
                    {
                        reason = "audio-buffer-queue-full",
                        stream = stream.KindName,
                        count = stream.DroppedBuffers
                    },
                    CollectorClosingTimestamp.Resolve(_context?.Clock, boundary),
                    "evidence-dropped");
            }

            stream.Dispose();
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

        if (LifecycleState is CollectorLifecycleState.Running or
            CollectorLifecycleState.Failed)
        {
            var now = _context?.Clock.GetElapsedNanoseconds() ?? 0;
            await StopAsync(
                new SessionBoundary(now, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }

        foreach (var stream in _streams)
        {
            stream.Dispose();
        }

        _disposed = true;
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private void TryPrepareStream(
        AudioStreamKind kind,
        Func<(
            IWaveIn Capture,
            string DeviceName,
            string? Limitation,
            float? EndpointVolumeScalar)> createCapture,
        ICollection<string> limitations)
    {
        try
        {
            var prepared = createCapture();
            if (prepared.Limitation is not null)
            {
                limitations.Add(prepared.Limitation);
            }

            _streams.Add(new AudioStreamCapture(
                kind,
                prepared.Capture,
                prepared.DeviceName,
                prepared.EndpointVolumeScalar,
                _context!,
                OnAudioBuffer,
                OnAudioFailure));
        }
        catch (Exception exception)
        {
            limitations.Add(
                $"{kind.ToString().ToLowerInvariant()}-unavailable:" +
                $"{exception.GetType().Name}:0x{exception.HResult:X8}");
        }
    }

    private static (
        IWaveIn Capture,
        string DeviceName,
        string? Limitation,
        float? EndpointVolumeScalar)
        CreateMicrophoneCapture()
    {
        using var enumerator = new MMDeviceEnumerator();
        Exception? lastFailure = null;

        foreach (var role in new[] { Role.Console, Role.Multimedia, Role.Communications })
        {
            try
            {
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                return (
                    new WasapiCapture(device),
                    device.FriendlyName,
                    null,
                    ReadEndpointVolume(device));
            }
            catch (Exception exception)
            {
                lastFailure = exception;
            }
        }

        foreach (var device in enumerator.EnumerateAudioEndPoints(
                     DataFlow.Capture,
                     DeviceState.Active))
        {
            try
            {
                return (
                    new WasapiCapture(device),
                    device.FriendlyName,
                    null,
                    ReadEndpointVolume(device));
            }
            catch (Exception exception)
            {
                lastFailure = exception;
                device.Dispose();
            }
        }

        if (WaveInEvent.DeviceCount > 0)
        {
            var capabilities = WaveInEvent.GetCapabilities(0);
            return (
                new WaveInEvent
                {
                    DeviceNumber = 0,
                    WaveFormat = new WaveFormat(48_000, 16, 1),
                    BufferMilliseconds = 100,
                    NumberOfBuffers = 3
                },
                capabilities.ProductName,
                "microphone-winmm-fallback",
                null);
        }

        throw new InvalidOperationException(
            "No Windows microphone endpoint could be opened.",
            lastFailure);
    }

    private static float? ReadEndpointVolume(MMDevice device)
    {
        try
        {
            return device.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            return null;
        }
    }

    private void OnAudioBuffer(AudioBufferObservation observation)
    {
        EmitEvent(
            observation.Stream.Channel,
            "audio-buffer",
            observation.Stream.CreateBufferPayload(observation),
            observation.CallbackMonotonicNanoseconds,
            "callback-time",
            "first-sample-time-estimated");
    }

    private void OnAudioFailure(AudioStreamCapture stream, Exception? exception)
    {
        HealthState = CollectorHealthState.Degraded;
        EmitEvent(
            stream.Channel,
            "audio-stream-error",
            new
            {
                stream = stream.KindName,
                errorType = exception?.GetType().FullName,
                message = exception?.Message
            },
            _context?.Clock.GetElapsedNanoseconds() ?? 0,
            "capture-incomplete");
    }

    private void EmitStreamEvent(
        string eventType,
        AudioStreamCapture stream,
        long monotonicNanoseconds)
    {
        EmitEvent(
            stream.Channel,
            eventType,
            stream.CreateStreamPayload(),
            monotonicNanoseconds);
    }

    private void EmitEvent(
        string channel,
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
            channel,
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
}

internal enum AudioStreamKind
{
    Microphone,
    System
}

internal sealed class AudioStreamCapture : IDisposable
{
    private readonly IWaveIn _capture;
    private readonly string _deviceName;
    private readonly float? _endpointVolumeScalar;
    private readonly CollectorInitializationContext _context;
    private readonly Action<AudioBufferObservation> _onBuffer;
    private readonly Action<AudioStreamCapture, Exception?> _onFailure;
    private readonly Channel<AudioPacket> _packets;
    private readonly TaskCompletionSource _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WaveFileWriter? _writer;
    private Task? _writerTask;
    private long _bufferSequence = -1;
    private long _dataBytes;
    private long _droppedBuffers;
    private readonly object _signalGate = new();
    private double _peakAmplitude;
    private double _sumSquares;
    private long _sampleCount;
    private bool _started;
    private bool _disposed;

    public AudioStreamCapture(
        AudioStreamKind kind,
        IWaveIn capture,
        string deviceName,
        float? endpointVolumeScalar,
        CollectorInitializationContext context,
        Action<AudioBufferObservation> onBuffer,
        Action<AudioStreamCapture, Exception?> onFailure)
    {
        Kind = kind;
        _capture = capture;
        _deviceName = deviceName;
        _endpointVolumeScalar = endpointVolumeScalar;
        _context = context;
        _onBuffer = onBuffer;
        _onFailure = onFailure;
        _packets = System.Threading.Channels.Channel.CreateBounded<AudioPacket>(
            new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false
            },
            _ => Interlocked.Increment(ref _droppedBuffers));

        KindName = kind.ToString().ToLowerInvariant();
        Channel = kind == AudioStreamKind.Microphone
            ? "audio.microphone"
            : "audio.system";
        RelativePath = Path.Combine("audio", $"{KindName}.wav")
            .Replace('\\', '/');
        AbsolutePath = Path.Combine(
            context.SessionDirectory,
            RelativePath.Replace('/', Path.DirectorySeparatorChar));

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public AudioStreamKind Kind { get; }
    public string KindName { get; }
    public string Channel { get; }
    public string RelativePath { get; }
    public string AbsolutePath { get; }
    public WaveFormat Format => _capture.WaveFormat;
    public long DroppedBuffers => Interlocked.Read(ref _droppedBuffers);

    public void Start()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AbsolutePath)!);
        _writer = new WaveFileWriter(AbsolutePath, Format);
        _writerTask = ProcessPacketsAsync();
        _capture.StartRecording();
        _started = true;
    }

    public void Stop()
    {
        if (!_started)
        {
            _stopped.TrySetResult();
            return;
        }

        _capture.StopRecording();
    }

    public async Task WaitForStopAsync(CancellationToken cancellationToken)
    {
        await _stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        _packets.Writer.TryComplete();
        if (_writerTask is not null)
        {
            await _writerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public object CreateStreamPayload()
    {
        var levels = ReadSignalLevels();
        return new
        {
            stream = KindName,
            path = RelativePath,
            device = _deviceName,
            endpointVolumeScalar = _endpointVolumeScalar,
            format = CreateFormatPayload(),
            dataBytes = Interlocked.Read(ref _dataBytes),
            buffersObserved = Interlocked.Read(ref _bufferSequence) + 1,
            buffersDropped = DroppedBuffers,
            peakAmplitude = levels.PeakAmplitude,
            peakDbfs = levels.PeakDbfs,
            rmsAmplitude = levels.RmsAmplitude,
            rmsDbfs = levels.RmsDbfs
        };
    }

    public object CreateBufferPayload(AudioBufferObservation observation) => new
    {
        stream = KindName,
        path = RelativePath,
        bufferSequence = observation.BufferSequence,
        dataByteOffset = observation.DataByteOffset,
        byteLength = observation.ByteLength,
        sampleFrames = observation.SampleFrames,
        durationNanoseconds = observation.DurationNanoseconds,
        estimatedFirstSampleMonotonicNanoseconds =
            Math.Max(
                0,
                observation.CallbackMonotonicNanoseconds -
                observation.DurationNanoseconds),
        callbackMonotonicNanoseconds = observation.CallbackMonotonicNanoseconds
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _packets.Writer.TryComplete();
        _writer?.Dispose();
        _writer = null;
        _capture.Dispose();
        _disposed = true;
    }

    private object CreateFormatPayload() => new
    {
        encoding = Format.Encoding.ToString(),
        sampleRate = Format.SampleRate,
        channels = Format.Channels,
        bitsPerSample = Format.BitsPerSample,
        blockAlign = Format.BlockAlign,
        averageBytesPerSecond = Format.AverageBytesPerSecond
    };

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (eventArgs.BytesRecorded <= 0)
        {
            return;
        }

        var callbackTime = _context.Clock.GetElapsedNanoseconds();
        var sequence = Interlocked.Increment(ref _bufferSequence);
        var bytes = GC.AllocateUninitializedArray<byte>(eventArgs.BytesRecorded);
        eventArgs.Buffer.AsSpan(0, eventArgs.BytesRecorded).CopyTo(bytes);
        _packets.Writer.TryWrite(new AudioPacket(sequence, callbackTime, bytes));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is not null)
        {
            _onFailure(this, eventArgs.Exception);
        }

        _stopped.TrySetResult();
    }

    private async Task ProcessPacketsAsync()
    {
        await foreach (var packet in _packets.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var offset = Interlocked.Read(ref _dataBytes);
            ObserveSignal(packet.Bytes);
            _writer!.Write(packet.Bytes, 0, packet.Bytes.Length);
            Interlocked.Add(ref _dataBytes, packet.Bytes.Length);

            var frames = packet.Bytes.Length / Format.BlockAlign;
            var durationNanoseconds = checked(
                (long)((decimal)frames * 1_000_000_000m / Format.SampleRate));
            _onBuffer(new AudioBufferObservation(
                this,
                packet.Sequence,
                offset,
                packet.Bytes.Length,
                frames,
                durationNanoseconds,
                packet.CallbackMonotonicNanoseconds));
        }

        _writer?.Dispose();
        _writer = null;
    }

    private void ObserveSignal(byte[] bytes)
    {
        var sampleBytes = Format.BitsPerSample / 8;
        if (sampleBytes <= 0)
        {
            return;
        }

        var sampleCount = bytes.Length / sampleBytes;
        var peak = 0d;
        var sumSquares = 0d;
        var observed = 0L;
        for (var index = 0; index < sampleCount; index++)
        {
            var offset = index * sampleBytes;
            double sample;
            if (Format.Encoding == WaveFormatEncoding.IeeeFloat &&
                Format.BitsPerSample == 32)
            {
                sample = BitConverter.ToSingle(bytes, offset);
            }
            else if (Format.Encoding == WaveFormatEncoding.Pcm)
            {
                sample = Format.BitsPerSample switch
                {
                    16 => BitConverter.ToInt16(bytes, offset) / 32768d,
                    24 => ReadPcm24(bytes, offset) / 8388608d,
                    32 => BitConverter.ToInt32(bytes, offset) / 2147483648d,
                    _ => double.NaN
                };
            }
            else
            {
                return;
            }

            if (!double.IsFinite(sample))
            {
                continue;
            }

            var amplitude = Math.Abs(sample);
            peak = Math.Max(peak, amplitude);
            sumSquares += sample * sample;
            observed++;
        }

        lock (_signalGate)
        {
            _peakAmplitude = Math.Max(_peakAmplitude, peak);
            _sumSquares += sumSquares;
            _sampleCount += observed;
        }
    }

    private SignalLevels ReadSignalLevels()
    {
        lock (_signalGate)
        {
            if (_sampleCount == 0)
            {
                return new SignalLevels(null, null, null, null);
            }

            var rms = Math.Sqrt(_sumSquares / _sampleCount);
            return new SignalLevels(
                _peakAmplitude,
                ToDecibels(_peakAmplitude),
                rms,
                ToDecibels(rms));
        }
    }

    private static int ReadPcm24(byte[] bytes, int offset)
    {
        var sample = bytes[offset] |
            (bytes[offset + 1] << 8) |
            (bytes[offset + 2] << 16);
        return (sample & 0x800000) == 0
            ? sample
            : sample | unchecked((int)0xFF000000);
    }

    private static double? ToDecibels(double amplitude) =>
        amplitude > 0 ? 20 * Math.Log10(amplitude) : null;
}

internal sealed record AudioPacket(
    long Sequence,
    long CallbackMonotonicNanoseconds,
    byte[] Bytes);

internal sealed record AudioBufferObservation(
    AudioStreamCapture Stream,
    long BufferSequence,
    long DataByteOffset,
    int ByteLength,
    int SampleFrames,
    long DurationNanoseconds,
    long CallbackMonotonicNanoseconds);

internal sealed record SignalLevels(
    double? PeakAmplitude,
    double? PeakDbfs,
    double? RmsAmplitude,
    double? RmsDbfs);

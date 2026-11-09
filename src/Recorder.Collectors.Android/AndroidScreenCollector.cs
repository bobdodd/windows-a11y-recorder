using System.Globalization;
using System.Security.Cryptography;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// Records the phone's screen as scrcpy's server encodes it on the phone
/// (android-implementation-plan.md, slice 3).
///
/// At the start it starts the server and connects to its video socket. The
/// stream is written, as received, to <see cref="AndroidScreenRecords.StreamPath"/>,
/// and each packet gets a record of where its bytes are. A phone the screen
/// cannot be recorded from does not stop the recording: the collector
/// records why, reports itself degraded, and the recording goes on.
///
/// Each picture packet is placed on the session clock by its phone time
/// through the companion collector's clock fit, when there is one, and
/// otherwise at the time the recorder read it. That the server's phone
/// times are CLOCK_MONOTONIC, the clock the companion app samples, is
/// inferred: the server passes on the encoder's presentation times, and the
/// encoder takes them from the frames the display gives it. The delay from
/// a packet's placed time to the time the recorder read it, shown in the
/// status, is how this is checked: it can only be small and positive if the
/// clocks are the same.
///
/// A capture session's record, and a configuration packet's, which has no
/// phone time, are placed at the time of the picture packet that follows
/// them, so the channel's times do not go backward.
///
/// While the stream is recorded, <see cref="PhoneFrames"/> makes a PNG frame
/// of each picture that differs from the one before, on
/// <see cref="AndroidFrameRecords.FramesChannel"/>, at its packet's time.
/// </summary>
public sealed class AndroidScreenCollector : ICaptureCollector
{
    public const string Type = "android.screen";

    /// <summary>How long the stream waits for the companion collector's first clock sample before placing packets by their receipt.</summary>
    public static readonly TimeSpan ClockWait = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The packet's time was the next picture packet's: a capture session or
    /// configuration packet, which comes just before it.
    /// </summary>
    public const string NextPacketTimeFlag = "next-packet-time";

    private readonly string _serial;
    private readonly IPhoneClock? _clock;
    private readonly Func<string, CancellationToken, Task<ScrcpyConnections>> _open;
    private readonly TaskCompletionSource<PhoneSoundSource> _sound = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<int, int, IH264Decoder> _decoders;
    private readonly object _gate = new();
    private readonly List<long> _delays = [];

    private CollectorInitializationContext? _context;
    private ScrcpyVideoConnection? _connection;
    private ScrcpyAudioConnection? _audio;
    private CancellationTokenSource? _running;
    private Task? _reading;
    private ulong _videoSequence;
    private ulong _frameSequence;
    private PhoneFrames? _frames;
    private ulong _lifecycleSequence;
    private long _lastTime = long.MinValue;
    private long _packets;
    private long _bytes;
    private int _sessions;
    private volatile string? _size;
    private bool _stopping;
    private bool _clockless;

    /// <param name="serverPath">The bundled scrcpy server.</param>
    /// <param name="clock">The companion collector's clock fit; null to place every packet at its receipt.</param>
    /// <param name="open">Opens the stream, given the server log's path; tests give fake bytes. By default, <see cref="ScrcpyLink.OpenAsync"/>.</param>
    /// <param name="decoders">Makes an H.264 decoder for a capture session's width and height; by default Windows' Media Foundation decoder.</param>
    public AndroidScreenCollector(
        IAdb adb,
        string serial,
        string serverPath,
        IPhoneClock? clock,
        Func<string, CancellationToken, Task<ScrcpyConnections>>? open = null,
        Func<int, int, IH264Decoder>? decoders = null)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        _serial = serial;
        _clock = clock;
        _open = open ?? ((log, cancellation) => ScrcpyLink.OpenAsync(adb, serial, serverPath, log, cancellation));
        _decoders = decoders ?? ((width, height) => new MediaFoundationH264Decoder(width, height));
        Descriptor = CollectorDescriptor.Create(
            Type,
            nameof(AndroidScreenCollector),
            typeof(AndroidScreenCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AndroidScreenRecords.VideoChannel, AndroidFrameRecords.FramesChannel],
            "scrcpy-server-" + AndroidTools.ScrcpyVersion);
    }

    public CollectorDescriptor Descriptor { get; }

    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;

    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

    public string? HealthReason { get; private set; }

    /// <summary>
    /// The server's audio socket for the sound collector, once this
    /// collector has started, or why there is none: the sound comes from the
    /// same server as the screen.
    /// </summary>
    public Task<PhoneSoundSource> Sound => _sound.Task;

    public string? Summary
    {
        get
        {
            if (_connection is null)
            {
                return null;
            }

            var packets = Interlocked.Read(ref _packets);
            var megabytes = Interlocked.Read(ref _bytes) / 1_000_000.0;
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"Phone screen: {packets} packets, {megabytes:0.0} MB");
            if (_size is { } size)
            {
                var sessions = Volatile.Read(ref _sessions);
                text += sessions > 1
                    ? string.Create(CultureInfo.InvariantCulture, $", {sessions} capture sessions, now {size}")
                    : ", " + size;
            }

            if (_frames is { } frames)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", {frames.Written} frames");
                if (frames.Waiting > 0 && !frames.Failed)
                {
                    text += string.Create(CultureInfo.InvariantCulture, $" ({frames.Waiting} packets waiting to be decoded)");
                }
            }

            if (MedianDelay() is { } delay)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", median delay from phone to recorder {delay / 1_000_000.0:0.0} ms");
            }
            else if (_clockless)
            {
                text += ", placed by the time each packet arrived, as there is no phone clock";
            }

            return text + ".";
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
            return ValueTask.FromResult(new CapabilityResult(
                CapabilityStatus.SupportedWithLimitations,
                Descriptor.Channels,
                ["compressed-on-phone", "secure-windows-record-blank"],
                true,
                false));
        }
    }

    public async ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        CollectorInitializationContext context;
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Ready || _context is null)
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot start from {LifecycleState}.");
            }

            context = _context;
            LifecycleState = CollectorLifecycleState.Starting;
        }

        LifecycleState = CollectorLifecycleState.Running;
        EmitLifecycle("started", boundary.MonotonicNanoseconds, boundary.Utc);

        var streamPath = Path.Combine(context.SessionDirectory, AndroidScreenRecords.StreamPath);
        Directory.CreateDirectory(Path.GetDirectoryName(streamPath)!);
        try
        {
            var connections = await _open(
                Path.Combine(context.SessionDirectory, AndroidScreenRecords.ServerLogPath),
                cancellationToken).ConfigureAwait(false);
            _connection = connections.Video;
            _audio = connections.Audio;
            _sound.TrySetResult(new PhoneSoundSource(_audio, null));
        }
        catch (ScrcpyUnavailableException exception)
        {
            _sound.TrySetResult(new PhoneSoundSource(null, "the phone's screen stream did not start, and the sound comes from the same server. " + exception.Message));
            Unavailable(exception.Message, connectionLost: false);
            return CollectorTransitionResult.Success(LifecycleState);
        }
        catch (Exception exception)
        {
            _sound.TrySetResult(new PhoneSoundSource(null, "the phone's screen stream did not start: " + exception.Message));
            throw;
        }

        Emit(
            context.Clock.GetElapsedNanoseconds(),
            AndroidScreenRecords.StreamEventType,
            new AndroidVideoStream(
                _serial,
                AndroidTools.ScrcpyVersion,
                ScrcpyProtocol.CodecName(_connection.CodecId),
                ScrcpyProtocol.VideoBitRate,
                AndroidScreenRecords.StreamPath),
            qualityFlags: [AndroidScreenRecords.ReceiptTimeFlag]);

        _frames = new PhoneFrames(
            context.SessionDirectory,
            streamPath,
            _decoders,
            context.ArtifactHashes,
            context.Clock.GetElapsedNanoseconds,
            FrameWritten,
            FramesFailed,
            Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
        _running = new CancellationTokenSource();
        var token = _running.Token;
        var connection = _connection;
        _reading = Task.Run(() => ReadAsync(connection, streamPath, token), CancellationToken.None);
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask<CollectorTransitionResult> StopAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boundary);
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
            _stopping = true;
        }

        await CloseAsync().ConfigureAwait(false);
        LifecycleState = CollectorLifecycleState.Stopped;
        EmitLifecycle("stopped", CollectorClosingTimestamp.Resolve(_context?.Clock, boundary), boundary.Utc);
        return CollectorTransitionResult.Success(LifecycleState);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _stopping = true;
        }

        _sound.TrySetResult(new PhoneSoundSource(null, "the phone's screen collector was not started."));
        await CloseAsync().ConfigureAwait(false);
        _running?.Dispose();
        _frames?.Dispose();
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private async Task CloseAsync()
    {
        // Closing the socket ends the server on the phone and the read. The
        // sound collector has closed the audio socket already, as it stops
        // first; closing it again does nothing.
        _running?.Cancel();
        if (_audio is not null)
        {
            await _audio.DisposeAsync().ConfigureAwait(false);
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        if (_reading is not null)
        {
            try
            {
                await _reading.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
        }

        // The frames not yet made are made from the stored stream before the
        // recording is completed.
        if (_frames is { } frames)
        {
            await frames.CompleteAsync().ConfigureAwait(false);
        }
    }

    private async Task ReadAsync(ScrcpyVideoConnection connection, string streamPath, CancellationToken cancellation)
    {
        var context = _context!;
        var ready = await WaitForClockAsync().ConfigureAwait(false);
        _clockless = !ready;
        string? ended = null;
        long offset = 0;
        var session = 0;
        var pending = new List<Pending>();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var file = new FileStream(streamPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16, FileOptions.Asynchronous);
        try
        {
            var buffer = new byte[1 << 20];
            while (true)
            {
                ScrcpyHeader? header;
                try
                {
                    header = await connection.ReadHeaderAsync(cancellation).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or FormatException or ObjectDisposedException)
                {
                    ended = ex is FormatException ? $"the stream could not be read: {ex.Message}" : $"the connection failed: {ex.Message}";
                    break;
                }

                if (header is null)
                {
                    // Through adb an unplugged cable, adb stopping and the server
                    // stopping all look the same here (test 19, 2026-11-09).
                    ended = "the connection closed: the phone was unplugged, adb stopped, or scrcpy's server on the phone stopped.";
                    break;
                }

                if (header is ScrcpySessionHeader started)
                {
                    session++;
                    Interlocked.Increment(ref _sessions);
                    _size = string.Create(CultureInfo.InvariantCulture, $"{started.Width} by {started.Height}");
                    _frames?.Session(started.Width, started.Height);
                    pending.Add(new Pending(
                        AndroidScreenRecords.SessionEventType,
                        new AndroidVideoSession(session, started.Width, started.Height, started.ClientResized, offset),
                        context.Clock.GetElapsedNanoseconds()));
                    continue;
                }

                var packet = (ScrcpyPacketHeader)header;
                if (buffer.Length < packet.Size)
                {
                    buffer = new byte[packet.Size];
                }

                try
                {
                    await connection.ReadPacketAsync(buffer.AsMemory(0, packet.Size), cancellation).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    ended = $"the connection failed inside a packet: {ex.Message}";
                    break;
                }

                var receivedAt = context.Clock.GetElapsedNanoseconds();
                await file.WriteAsync(buffer.AsMemory(0, packet.Size), CancellationToken.None).ConfigureAwait(false);
                // Out of the file's buffer, so the frames can read it back.
                await file.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                hash.AppendData(buffer, 0, packet.Size);
                var record = new AndroidVideoPacket(
                    session,
                    offset,
                    packet.Size,
                    packet.PresentationMicroseconds,
                    packet.Config,
                    packet.KeyFrame,
                    receivedAt);
                offset += packet.Size;
                Interlocked.Increment(ref _packets);
                Interlocked.Add(ref _bytes, packet.Size);

                if (packet.PresentationMicroseconds is not { } pts)
                {
                    pending.Add(new Pending(AndroidScreenRecords.PacketEventType, record, receivedAt));
                    _frames?.Packet(new FramePacket(session, record.ByteOffset, record.Size, true, null, receivedAt, receivedAt, null, null, null, []));
                    continue;
                }

                var placed = EmitPicture(record, pts * 1000, receivedAt, ready, pending);
                _frames?.Packet(placed);
            }

            // What came before a picture that never arrived is placed at its receipt.
            foreach (var item in pending)
            {
                EmitHeld(item.ReceivedAt, item.EventType, item.Payload, [AndroidScreenRecords.ReceiptTimeFlag], null);
            }
        }
        finally
        {
            await file.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            await file.DisposeAsync().ConfigureAwait(false);
            context.ArtifactHashes?.Record(streamPath, offset, hash.GetHashAndReset());
        }

        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }
        }

        Unavailable(
            $"The screen stream ended: {ended}{ScrcpyVideoConnection.Said(connection.ServerLog())}",
            connectionLost: true);
    }

    private FramePacket EmitPicture(AndroidVideoPacket record, long phoneTime, long receivedAt, bool ready, List<Pending> pending)
    {
        var mapped = ready ? _clock?.Map(phoneTime) : null;
        long time;
        List<string> flags = [];
        if (mapped is null)
        {
            time = receivedAt;
            flags.Add(AndroidScreenRecords.ReceiptTimeFlag);
        }
        else
        {
            time = mapped.SessionNanoseconds;
            lock (_delays)
            {
                _delays.Add(receivedAt - time);
            }
        }

        if (time < _lastTime)
        {
            time = _lastTime;
            flags.Add(AndroidCompanionRecords.FitHeldFlag);
        }

        foreach (var item in pending)
        {
            EmitHeld(time, item.EventType, item.Payload, [NextPacketTimeFlag], null);
        }

        pending.Clear();
        _lastTime = time;
        var clockMapping = mapped is null ? null : AndroidCompanionRecords.FittedClockMapping;
        Emit(
            time,
            AndroidScreenRecords.PacketEventType,
            record,
            native: phoneTime,
            uncertainty: mapped?.UncertaintyNanoseconds,
            clockMapping: clockMapping,
            qualityFlags: [.. flags]);
        return new FramePacket(
            record.Session,
            record.ByteOffset,
            record.Size,
            false,
            record.PresentationMicroseconds,
            receivedAt,
            time,
            phoneTime,
            mapped?.UncertaintyNanoseconds,
            clockMapping,
            flags);
    }

    private void FrameWritten(WrittenFrame written)
    {
        var packet = written.Packet;
        Emit(
            packet.Time,
            AndroidFrameRecords.FrameEventType,
            written.Frame,
            native: packet.NativeNanoseconds,
            uncertainty: packet.UncertaintyNanoseconds,
            clockMapping: packet.ClockMapping,
            qualityFlags: [.. written.Flags],
            channel: AndroidFrameRecords.FramesChannel);
    }

    private void FramesFailed(string reason)
    {
        var text = $"The phone's frames stopped being made: {reason} The phone's screen is still recorded as a video stream.";
        HealthState = CollectorHealthState.Degraded;
        HealthReason = text;
        Emit(
            Math.Max(_context?.Clock.GetElapsedNanoseconds() ?? 0, Volatile.Read(ref _lastTime)),
            AndroidFrameRecords.UnavailableEventType,
            new AndroidFramesUnavailable(reason),
            channel: AndroidFrameRecords.FramesChannel);
    }

    private void EmitHeld(long time, string eventType, object payload, string[] flags, long? native)
    {
        if (time < _lastTime)
        {
            time = _lastTime;
        }

        _lastTime = time;
        Emit(time, eventType, payload, native: native, qualityFlags: flags);
    }

    private async Task<bool> WaitForClockAsync()
    {
        if (_clock is null)
        {
            return false;
        }

        var first = await Task.WhenAny(_clock.FirstSample, Task.Delay(ClockWait)).ConfigureAwait(false);
        return first == _clock.FirstSample && await _clock.FirstSample.ConfigureAwait(false);
    }

    private long? MedianDelay()
    {
        lock (_delays)
        {
            if (_delays.Count == 0)
            {
                return null;
            }

            var sorted = _delays.ToArray();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }
    }

    private void Unavailable(string reason, bool connectionLost)
    {
        var text = connectionLost
            ? $"The phone's screen stopped being recorded during the recording. {reason} The recording goes on without it."
            : $"The phone's screen could not be recorded: {reason} The recording goes on without it.";
        HealthState = CollectorHealthState.Degraded;
        HealthReason = text;
        var at = _context?.Clock.GetElapsedNanoseconds() ?? 0;
        if (at < _lastTime)
        {
            at = _lastTime;
        }

        Emit(at, AndroidScreenRecords.UnavailableEventType, new AndroidVideoUnavailable(_serial, reason, connectionLost));
    }

    private void Emit<T>(
        long monotonicNanoseconds,
        string eventType,
        T payload,
        long? native = null,
        long? uncertainty = null,
        string? clockMapping = null,
        string[]? qualityFlags = null,
        string channel = AndroidScreenRecords.VideoChannel)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var next = channel == AndroidFrameRecords.FramesChannel
            ? Interlocked.Increment(ref _frameSequence)
            : Interlocked.Increment(ref _videoSequence);
        var record = RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            channel,
            next,
            monotonicNanoseconds,
            eventType,
            payload,
            qualityFlags ?? []);
        record = record with
        {
            NativeTimestamp = native is { } value
                ? new NativeTimestamp(AndroidCompanionRecords.ClockDomain, value, "ns")
                : null,
            TimestampUncertaintyNanoseconds = uncertainty,
            ClockMappingId = clockMapping ?? record.ClockMappingId
        };
        context.EventSink.TryWrite(record);
    }

    private void EmitLifecycle(string action, long monotonicNanoseconds, DateTimeOffset utc)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var sequence = Interlocked.Increment(ref _lifecycleSequence);
        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            "collector.lifecycle",
            sequence,
            monotonicNanoseconds,
            "collector-lifecycle",
            new { action, state = LifecycleState.ToString(), Utc = utc }));
    }

    private sealed record Pending(string EventType, object Payload, long ReceivedAt);
}

/// <summary>The audio socket the sound collector reads, or why there is none.</summary>
public sealed record PhoneSoundSource(ScrcpyAudioConnection? Connection, string? Reason);

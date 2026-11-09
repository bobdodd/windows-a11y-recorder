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
    private readonly Func<string, CancellationToken, Task<ScrcpyVideoConnection>> _open;
    private readonly object _gate = new();
    private readonly List<long> _delays = [];

    private CollectorInitializationContext? _context;
    private ScrcpyVideoConnection? _connection;
    private CancellationTokenSource? _running;
    private Task? _reading;
    private ulong _videoSequence;
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
    public AndroidScreenCollector(
        IAdb adb,
        string serial,
        string serverPath,
        IPhoneClock? clock,
        Func<string, CancellationToken, Task<ScrcpyVideoConnection>>? open = null)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        _serial = serial;
        _clock = clock;
        _open = open ?? ((log, cancellation) => ScrcpyLink.OpenAsync(adb, serial, serverPath, log, cancellation));
        Descriptor = CollectorDescriptor.Create(
            Type,
            nameof(AndroidScreenCollector),
            typeof(AndroidScreenCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AndroidScreenRecords.VideoChannel],
            "scrcpy-server-" + AndroidTools.ScrcpyVersion);
    }

    public CollectorDescriptor Descriptor { get; }

    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;

    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

    public string? HealthReason { get; private set; }

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
            _connection = await _open(
                Path.Combine(context.SessionDirectory, AndroidScreenRecords.ServerLogPath),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ScrcpyUnavailableException exception)
        {
            Unavailable(exception.Message, connectionLost: false);
            return CollectorTransitionResult.Success(LifecycleState);
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

        await CloseAsync().ConfigureAwait(false);
        _running?.Dispose();
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private async Task CloseAsync()
    {
        // Closing the socket ends the server on the phone and the read.
        _running?.Cancel();
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
                    continue;
                }

                EmitPicture(record, pts * 1000, receivedAt, ready, pending);
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

    private void EmitPicture(AndroidVideoPacket record, long phoneTime, long receivedAt, bool ready, List<Pending> pending)
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
        Emit(
            time,
            AndroidScreenRecords.PacketEventType,
            record,
            native: phoneTime,
            uncertainty: mapped?.UncertaintyNanoseconds,
            clockMapping: mapped is null ? null : AndroidCompanionRecords.FittedClockMapping,
            qualityFlags: [.. flags]);
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
        string[]? qualityFlags = null)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var next = Interlocked.Increment(ref _videoSequence);
        var record = RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            AndroidScreenRecords.VideoChannel,
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

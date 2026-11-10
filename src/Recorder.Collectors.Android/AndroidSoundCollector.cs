using System.Globalization;
using NAudio.Wave;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// Records the phone's sound as scrcpy's server captures it on the phone
/// (android-implementation-plan.md, slice 4).
///
/// The sound comes over the second socket of the server the screen
/// collector starts, so this collector starts after that one and stops
/// before it. The server sends the whole audio output, raw, at 48 kHz, two
/// channels, 16 bits (AudioConfig.java). It is written to
/// <see cref="AndroidSoundRecords.WavePath"/> as received, and each packet
/// gets an <c>audio-buffer</c> record on <see cref="AndroidSoundRecords.Channel"/>
/// in the desktop audio collector's shape, so the player and the export
/// read it as they read the desktop's sound.
///
/// The server times each packet by Android's audio timestamp on
/// CLOCK_MONOTONIC (AudioRecordReader.java, <c>TIMEBASE_MONOTONIC</c>), the
/// clock the companion app samples, so a packet is placed on the session
/// clock through the companion collector's clock fit, and otherwise at the
/// time the recorder read it. That the timestamp is the time of the
/// packet's last sample is inferred: the server reads the timestamp just
/// after reading the packet and passes on its time without its frame
/// position. The delay from a packet's placed time to its receipt, shown in
/// the status, is how this is checked.
///
/// While recording, the sound is also played on the PC, as the phone no
/// longer plays it itself, and its level is shown, with a message when it
/// is too quiet (<see cref="PhoneSoundLevel"/>). A phone whose sound cannot
/// be recorded does not stop the recording: the collector records why,
/// reports itself degraded, and the recording goes on.
/// </summary>
public sealed class AndroidSoundCollector : ICaptureCollector
{
    public const string Type = "android.sound";

    public const string QuietMessage = "The phone's sound is very quiet. Raise the phone's Accessibility volume now.";

    /// <summary>How long the sound waits for the companion collector's first clock sample before placing packets by their receipt.</summary>
    public static readonly TimeSpan ClockWait = TimeSpan.FromSeconds(3);

    /// <summary>How long the server has to start capturing the sound.</summary>
    public static readonly TimeSpan CodecTimeout = TimeSpan.FromSeconds(10);

    private static readonly AndroidSoundFormat Format = new(
        "Pcm",
        ScrcpyProtocol.SoundSampleRate,
        ScrcpyProtocol.SoundChannels,
        ScrcpyProtocol.SoundBitsPerSample,
        ScrcpyProtocol.SoundBlockAlign,
        ScrcpyProtocol.SoundSampleRate * ScrcpyProtocol.SoundBlockAlign);

    private readonly string _serial;
    private readonly Func<Task<PhoneSoundSource>> _source;
    private readonly IPhoneClock? _clock;
    private readonly Action? _captured;
    private readonly Func<IPhoneSoundOutput>? _output;
    private readonly object _gate = new();
    private readonly List<long> _delays = [];
    private readonly PhoneSoundLevel _level = new();

    private CollectorInitializationContext? _context;
    private ScrcpyAudioConnection? _connection;
    private CancellationTokenSource? _running;
    private Task? _reading;
    private IPhoneSoundOutput? _playing;
    private volatile string? _notPlayed;
    private volatile string? _unavailable;
    private volatile bool _receiving;
    private ulong _sequence;
    private ulong _lifecycleSequence;
    private long _lastTime = long.MinValue;
    private long _packets;
    private long _bytes;
    private bool _started;
    private bool _stopping;
    private bool _clockless;

    /// <param name="source">The audio socket, from the screen collector's <see cref="AndroidScreenCollector.Sound"/>.</param>
    /// <param name="clock">The companion collector's clock fit; null to place every packet at its receipt.</param>
    /// <param name="output">Plays the sound on the PC; null not to play it. By default the PC's default playback device.</param>
    /// <param name="captured">
    /// Called once, when the first packet arrives, so the phone is playing
    /// to the capture: the companion collector raises the Accessibility
    /// volume then (slice 4, step 4c).
    /// </param>
    public AndroidSoundCollector(
        string serial,
        Func<Task<PhoneSoundSource>> source,
        IPhoneClock? clock,
        Func<IPhoneSoundOutput>? output,
        Action? captured = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(source);
        _serial = serial;
        _source = source;
        _clock = clock;
        _output = output;
        _captured = captured;
        Descriptor = CollectorDescriptor.Create(
            Type,
            nameof(AndroidSoundCollector),
            typeof(AndroidSoundCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AndroidSoundRecords.Channel],
            "scrcpy-server-" + AndroidTools.ScrcpyVersion);
    }

    /// <summary>The sound of the server the screen collector starts, played on the PC's default playback device.</summary>
    public AndroidSoundCollector(string serial, AndroidScreenCollector screen, IPhoneClock? clock, Action? captured = null)
        : this(serial, () => screen.Sound, clock, () => new WasapiPhoneSoundOutput(), captured)
    {
    }

    public CollectorDescriptor Descriptor { get; }

    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;

    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

    /// <summary>Why the sound is missing, or the quiet message while the sound is too quiet; the app speaks each once.</summary>
    public string? HealthReason =>
        _unavailable ?? (_receiving && _context is { } context && _level.Quiet(context.Clock.GetElapsedNanoseconds()) ? QuietMessage : null);

    public string? Summary
    {
        get
        {
            if (!_receiving || _context is not { } context)
            {
                return null;
            }

            var packets = Interlocked.Read(ref _packets);
            var megabytes = Interlocked.Read(ref _bytes) / 1_000_000.0;
            var text = string.Create(CultureInfo.InvariantCulture, $"Phone sound: {packets} packets, {megabytes:0.0} MB");
            var window = PhoneSoundLevel.Window.TotalSeconds;
            text += _level.Loudest(context.Clock.GetElapsedNanoseconds()) switch
            {
                null => "",
                0 => string.Create(CultureInfo.InvariantCulture, $", silent in the last {window:0} seconds"),
                { } loudest => string.Create(CultureInfo.InvariantCulture, $", loudest in the last {window:0} seconds {Percent(loudest)} of full scale")
            };

            if (MedianDelay() is { } delay)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", median delay from phone to recorder {delay / 1_000_000.0:0.0} ms");
            }
            else if (_clockless)
            {
                text += ", placed by the time each packet arrived, as there is no phone clock";
            }

            if (_playing is { } playing)
            {
                text += ", playing on " + playing.Name;
            }
            else if (_notPlayed is { } reason)
            {
                text += ", not played on this PC: " + reason.TrimEnd('.');
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
                ["level-follows-phone-volume"],
                true,
                false));
        }
    }

    public async ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Ready || _context is null)
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot start from {LifecycleState}.");
            }

            LifecycleState = CollectorLifecycleState.Starting;
        }

        LifecycleState = CollectorLifecycleState.Running;
        EmitLifecycle("started", boundary.MonotonicNanoseconds, boundary.Utc);

        // The screen collector, started just before, has its connections or
        // knows why it has none.
        var sourceTask = _source();
        var source = sourceTask.IsCompleted
            ? await sourceTask.ConfigureAwait(false)
            : new PhoneSoundSource(null, "the phone's screen collector had not started.");
        if (source.Connection is not { } connection)
        {
            Unavailable(source.Reason ?? "there is no connection to scrcpy's server.", connectionLost: false);
            return CollectorTransitionResult.Success(LifecycleState);
        }

        _connection = connection;
        _running = new CancellationTokenSource();
        var token = _running.Token;
        var path = Path.Combine(_context!.SessionDirectory, AndroidSoundRecords.WavePath);
        _reading = Task.Run(() => ReadAsync(connection, path, token), CancellationToken.None);
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
        var closing = CollectorClosingTimestamp.Resolve(_context?.Clock, boundary);
        if (_started)
        {
            EmitStream(AndroidSoundRecords.StreamStoppedEventType, Math.Max(closing, Volatile.Read(ref _lastTime)));
        }

        LifecycleState = CollectorLifecycleState.Stopped;
        EmitLifecycle("stopped", closing, boundary.Utc);
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
        // Closing the socket ends the read; the server stops capturing the
        // sound and goes on with the screen.
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

        _playing?.Dispose();
    }

    private async Task ReadAsync(ScrcpyAudioConnection connection, string path, CancellationToken cancellation)
    {
        var context = _context!;
        try
        {
            await connection.ReadCodecAsync(CodecTimeout, cancellation).ConfigureAwait(false);
        }
        catch (ScrcpyUnavailableException exception)
        {
            if (!Stopping())
            {
                Unavailable(exception.Message, connectionLost: false);
            }

            return;
        }

        var ready = await WaitForClockAsync().ConfigureAwait(false);
        _clockless = !ready;
        StartPlaying();
        _receiving = true;

        string? ended = null;
        long offset = 0;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var writer = new WaveFileWriter(path, new WaveFormat(
            ScrcpyProtocol.SoundSampleRate,
            ScrcpyProtocol.SoundBitsPerSample,
            ScrcpyProtocol.SoundChannels));
        try
        {
            var buffer = new byte[64 * 1024];
            while (true)
            {
                ScrcpyHeader? header;
                try
                {
                    header = await connection.ReadHeaderAsync(cancellation).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or FormatException or ObjectDisposedException)
                {
                    ended = ex is FormatException ? $"the sound could not be read: {ex.Message}" : $"the connection failed: {ex.Message}";
                    break;
                }

                if (header is null)
                {
                    ended = "the connection closed: the phone was unplugged, adb stopped, or scrcpy's server on the phone stopped.";
                    break;
                }

                if (header is not ScrcpyPacketHeader { PresentationMicroseconds: { } pts } packet)
                {
                    // The raw codec sends neither (Streamer.java).
                    ended = "the sound could not be read: the server sent a header the raw sound does not have.";
                    break;
                }

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
                writer.Write(buffer, 0, packet.Size);
                _playing?.Write(buffer, 0, packet.Size);
                _level.Add(receivedAt, buffer.AsSpan(0, packet.Size));
                Place(offset, packet.Size, pts, receivedAt, ready);
                offset += packet.Size;
                if (Interlocked.Increment(ref _packets) == 1)
                {
                    _captured?.Invoke();
                }

                Interlocked.Add(ref _bytes, packet.Size);
            }
        }
        catch (OperationCanceledException)
        {
            ended = null;
        }
        finally
        {
            writer.Dispose();
        }

        if (Stopping() || ended is null)
        {
            return;
        }

        Unavailable($"The sound stream ended: {ended}{ScrcpyVideoConnection.Said(connection.ServerLog())}", connectionLost: true);
    }

    private void Place(long offset, int size, long pts, long receivedAt, bool ready)
    {
        var frames = size / ScrcpyProtocol.SoundBlockAlign;
        var duration = (long)((decimal)frames * 1_000_000_000m / ScrcpyProtocol.SoundSampleRate);
        var phoneTime = pts * 1000;
        var mapped = ready ? _clock?.Map(phoneTime) : null;
        long time;
        List<string> flags = [AndroidSoundRecords.FirstSampleEstimatedFlag];
        if (mapped is null)
        {
            time = receivedAt;
            flags.Add(AndroidScreenRecords.ReceiptTimeFlag);
        }
        else
        {
            time = mapped.SessionNanoseconds;
            flags.Add(AndroidSoundRecords.PhoneAudioTimeFlag);
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

        var first = Math.Max(0, time - duration);
        if (!_started)
        {
            // The player plays the file from its stream's start, so the
            // start is the first sample's time.
            _started = true;
            _lastTime = Math.Max(_lastTime, first);
            EmitStream(AndroidSoundRecords.StreamStartedEventType, _lastTime);
            if (time < _lastTime)
            {
                time = _lastTime;
            }
        }

        _lastTime = time;
        var clockMapping = mapped is null ? null : AndroidCompanionRecords.FittedClockMapping;
        Emit(
            time,
            AndroidSoundRecords.BufferEventType,
            new AndroidSoundBuffer(
                AndroidSoundRecords.StreamName,
                AndroidSoundRecords.WavePath,
                Interlocked.Read(ref _packets),
                offset,
                size,
                frames,
                duration,
                Math.Min(first, time),
                time,
                pts,
                receivedAt),
            native: phoneTime,
            uncertainty: mapped?.UncertaintyNanoseconds,
            clockMapping: clockMapping,
            qualityFlags: [.. flags]);
    }

    private void StartPlaying()
    {
        if (_output is null)
        {
            return;
        }

        try
        {
            _playing = _output();
        }
        catch (Exception ex)
        {
            // Not hearing the phone does not stop it being recorded.
            _notPlayed = ex.Message;
        }
    }

    private bool Stopping()
    {
        lock (_gate)
        {
            return _stopping;
        }
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

    // One decimal below the quiet level, where the owner raises the volume
    // until the figure passes 10%.
    private static string Percent(double fraction) =>
        (fraction * 100).ToString(fraction < PhoneSoundLevel.QuietPeak ? "0.0" : "0", CultureInfo.InvariantCulture) + "%";

    private void Unavailable(string reason, bool connectionLost)
    {
        var text = connectionLost
            ? $"The phone's sound stopped being recorded during the recording. {reason} The recording goes on without it."
            : $"The phone's sound could not be recorded: {reason} The recording goes on without it.";
        HealthState = CollectorHealthState.Degraded;
        _unavailable = text;
        var at = _context?.Clock.GetElapsedNanoseconds() ?? 0;
        if (at < _lastTime)
        {
            at = _lastTime;
        }

        Emit(
            at,
            AndroidSoundRecords.ErrorEventType,
            new AndroidSoundError(AndroidSoundRecords.StreamName, connectionLost ? "connection-lost" : "unavailable", text));
    }

    private void EmitStream(string eventType, long time)
    {
        var (peak, rms) = _level.Overall();
        Emit(
            time,
            eventType,
            new AndroidSoundStream(
                AndroidSoundRecords.StreamName,
                AndroidSoundRecords.WavePath,
                $"Android phone {_serial}, scrcpy {AndroidTools.ScrcpyVersion} audio_source=output",
                null,
                Format,
                Interlocked.Read(ref _bytes),
                Interlocked.Read(ref _packets),
                0,
                peak,
                PhoneSoundLevel.Decibels(peak),
                rms,
                PhoneSoundLevel.Decibels(rms)));
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

        var record = RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            AndroidSoundRecords.Channel,
            Interlocked.Increment(ref _sequence),
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
}

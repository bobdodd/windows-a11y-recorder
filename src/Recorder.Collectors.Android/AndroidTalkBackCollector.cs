using System.Globalization;
using System.Threading.Channels;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// Records what TalkBack said from its own log lines
/// (android-implementation-plan.md, slice 6).
///
/// <c>adb logcat -v monotonic -v usec -T 1 -s talkback:V</c> runs for the
/// whole recording. <c>-s talkback:V</c> keeps only lines with TalkBack's
/// tag, which feasibility check 3 found on 1,189 of the 1,196 lines of
/// TalkBack's process, so the lines follow TalkBack if it restarts as a new
/// process, and other apps' lines are not read from the phone. <c>-T 1</c>
/// starts at the newest line rather than the whole log. Every line is kept
/// on <see cref="AndroidTalkBackRecords.LogChannel"/>, and what TalkBack
/// said is read from them by <see cref="TalkBackSpeechReader"/> onto
/// <see cref="AndroidTalkBackRecords.SpeechChannel"/>.
///
/// The times are CLOCK_MONOTONIC (logprint.cpp, convertMonotonic), the
/// clock the companion app samples, so each record is placed on the
/// session clock through the companion collector's clock fit, and
/// otherwise at the time the recorder read it. A line from before the
/// recording's start is not kept. logcat flushes its output after each
/// read when following the log (logcat.cpp), so the lines are read through
/// a pipe. TalkBack writes its speech only at log level Verbose, which the
/// recorder cannot set; the status line says so until the first speech
/// line arrives. A phone whose log cannot be read does not stop the
/// recording: the collector records why, reports itself degraded, and the
/// recording goes on.
/// </summary>
public sealed class AndroidTalkBackCollector : ICaptureCollector
{
    public const string Type = "android.talkback";

    /// <summary>How long the lines wait for the companion collector's first clock sample before placing them by their receipt.</summary>
    public static readonly TimeSpan ClockWait = TimeSpan.FromSeconds(3);

    /// <summary>The longest latest utterance the status line shows, in characters.</summary>
    public const int StatusTextLength = 60;

    private readonly IAdb _adb;
    private readonly string _serial;
    private readonly IPhoneClock? _clock;
    private readonly object _gate = new();
    private readonly List<long> _delays = [];
    private readonly Dictionary<string, long> _lastTimes = new(StringComparer.Ordinal);
    private readonly TalkBackSpeechReader _reader = new();
    private readonly Channel<(string Line, long ReceivedAt)> _lines =
        Channel.CreateUnbounded<(string, long)>(new UnboundedChannelOptions { SingleReader = true });

    private CollectorInitializationContext? _context;
    private IAdbRunning? _logcat;
    private Task? _running;
    private long _start;
    private volatile string? _unavailable;
    private volatile string? _latest;
    private volatile bool _reading;
    private volatile bool _verbose;
    private ulong _logSequence;
    private ulong _speechSequence;
    private ulong _lifecycleSequence;
    private long _lineCount;
    private long _earlier;
    private long _utterances;
    private long _interrupted;
    private long _words;
    private bool _stopping;
    private bool _clockless;

    /// <param name="clock">The companion collector's clock fit; null to place every line at its receipt.</param>
    public AndroidTalkBackCollector(IAdb adb, string serial, IPhoneClock? clock)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        _adb = adb;
        _serial = serial;
        _clock = clock;
        Descriptor = CollectorDescriptor.Create(
            Type,
            nameof(AndroidTalkBackCollector),
            typeof(AndroidTalkBackCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AndroidTalkBackRecords.LogChannel, AndroidTalkBackRecords.SpeechChannel],
            "adb-logcat");
    }

    public CollectorDescriptor Descriptor { get; }

    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;

    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

    public string? HealthReason => _unavailable;

    /// <summary>
    /// For the end of a recording in which TalkBack wrote at level Verbose:
    /// a reminder that its log level is still Verbose. Null otherwise.
    /// </summary>
    public string? StopReminder => _verbose
        ? "TalkBack's log output level is still Verbose, which writes what is on the phone's screen to the phone's log. " +
          "If you prefer, set it back in TalkBack's settings, Advanced settings, Developer settings, \"Log output level\"."
        : null;

    public string? Summary
    {
        get
        {
            if (!_reading)
            {
                return null;
            }

            var lines = Interlocked.Read(ref _lineCount);
            var utterances = Interlocked.Read(ref _utterances);
            var lineText = string.Create(CultureInfo.InvariantCulture, $"{lines} TalkBack log {(lines == 1 ? "line" : "lines")}");
            string text;
            if (utterances == 0)
            {
                text = $"TalkBack: no speech lines yet, {lineText}. If TalkBack is on, set \"Log output level\" to \"Verbose\" " +
                    "in TalkBack's settings, Advanced settings, Developer settings";
            }
            else
            {
                var interrupted = Interlocked.Read(ref _interrupted);
                var words = Interlocked.Read(ref _words);
                text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"TalkBack: {utterances} {(utterances == 1 ? "utterance" : "utterances")}, {interrupted} interrupted, {words} {(words == 1 ? "word" : "words")} reached");
                if (_latest is { } latest)
                {
                    text += $", latest \"{latest}\"";
                }

                text += ", " + lineText;
            }

            if (MedianDelay() is { } delay)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", median delay from phone to recorder {delay / 1_000_000.0:0.0} ms");
            }
            else if (_clockless)
            {
                text += ", placed by the time each line arrived, as there is no phone clock";
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
                ["talkback-log-level-verbose-required", "talkback-internal-log-lines"],
                true,
                false));
        }
    }

    public ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Ready || _context is null)
            {
                return ValueTask.FromResult(CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot start from {LifecycleState}."));
            }

            LifecycleState = CollectorLifecycleState.Running;
            _start = boundary.MonotonicNanoseconds;
        }

        EmitLifecycle("started", boundary.MonotonicNanoseconds, boundary.Utc);
        _running = Task.Run(RunAsync, CancellationToken.None);
        return ValueTask.FromResult(CollectorTransitionResult.Success(LifecycleState));
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
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private async Task CloseAsync()
    {
        // logcat follows the log until it is stopped.
        IAdbRunning? logcat;
        lock (_gate)
        {
            logcat = _logcat;
            _logcat = null;
        }

        if (logcat is not null)
        {
            await logcat.StopAsync().ConfigureAwait(false);
        }

        _lines.Writer.TryComplete();
        if (_running is not null)
        {
            await _running.ConfigureAwait(false);
        }
    }

    private bool Stopping()
    {
        lock (_gate)
        {
            return _stopping;
        }
    }

    private async Task RunAsync()
    {
        var context = _context!;
        try
        {
            IAdbRunning logcat;
            try
            {
                logcat = _adb.Start(
                    ["-s", _serial, "logcat", "-v", "monotonic", "-v", "usec", "-T", "1", "-s", "talkback:V"],
                    line => _lines.Writer.TryWrite((line, context.Clock.GetElapsedNanoseconds())));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                Unavailable($"adb could not start logcat: {Sentence(ex.Message)}", connectionLost: false);
                return;
            }

            lock (_gate)
            {
                if (_stopping)
                {
                    _ = logcat.StopAsync().AsTask();
                    return;
                }

                _logcat = logcat;
            }

            _ = logcat.Exited.ContinueWith(_ => _lines.Writer.TryComplete(), TaskScheduler.Default);
            var ready = await WaitForClockAsync().ConfigureAwait(false);
            _clockless = !ready;
            _reading = true;
            await foreach (var (line, receivedAt) in _lines.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                Handle(line, receivedAt, ready);
            }

            if (!Stopping())
            {
                var code = logcat.Exited.IsCompleted ? logcat.Exited.Result : (int?)null;
                Unavailable(
                    "logcat on the phone ended" + (code is { } exit ? $" with exit code {exit.ToString(CultureInfo.InvariantCulture)}" : "") +
                    ": the phone was unplugged, adb stopped, or the phone ended it.",
                    connectionLost: true);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Unavailable($"the phone's log could not be read: {Sentence(ex.Message)}", connectionLost: false);
        }
    }

    private void Handle(string line, long receivedAt, bool ready)
    {
        var text = line.TrimEnd('\r', '\n');
        if (text.Trim().Length == 0)
        {
            return;
        }

        var entry = TalkBackLogLine.TryParse(text);
        var (time, mapping, placed) = Place(entry?.PhoneMicroseconds, receivedAt, ready);
        if (entry is not null && mapping is not null && time < _start)
        {
            // From before the recording, such as the newest line -T 1 gives.
            Interlocked.Increment(ref _earlier);
            return;
        }

        Interlocked.Increment(ref _lineCount);
        if (entry?.Level == "V")
        {
            _verbose = true;
        }

        var native = entry?.PhoneMicroseconds * 1000;
        var flags = placed;
        Emit(
            AndroidTalkBackRecords.LogChannel,
            ref _logSequence,
            Hold(AndroidTalkBackRecords.LogChannel, time, ref flags),
            AndroidTalkBackRecords.LineEventType,
            new AndroidTalkBackLine(text, entry?.PhoneMicroseconds, entry?.ProcessId, entry?.ThreadId, entry?.Level, entry?.Tag, entry?.Message, receivedAt),
            native,
            mapping?.UncertaintyNanoseconds,
            mapping is null ? null : AndroidCompanionRecords.FittedClockMapping,
            flags);
        if (entry is null || _reader.Read(entry, receivedAt) is not { } speech)
        {
            return;
        }

        switch (speech.EventType)
        {
            case AndroidTalkBackRecords.UtteranceStartedEventType:
                Interlocked.Increment(ref _utterances);
                _latest = Shortened(((AndroidTalkBackFragment)speech.Payload).Text);
                break;
            case AndroidTalkBackRecords.UtteranceInterruptedEventType:
                Interlocked.Increment(ref _interrupted);
                break;
            case AndroidTalkBackRecords.WordReachedEventType:
                Interlocked.Increment(ref _words);
                break;
        }

        // The delay is measured for speech lines only.
        if (mapping is not null)
        {
            lock (_delays)
            {
                _delays.Add(receivedAt - time);
            }
        }

        var speechFlags = placed;
        Emit(
            AndroidTalkBackRecords.SpeechChannel,
            ref _speechSequence,
            Hold(AndroidTalkBackRecords.SpeechChannel, time, ref speechFlags),
            speech.EventType,
            speech.Payload,
            native,
            mapping?.UncertaintyNanoseconds,
            mapping is null ? null : AndroidCompanionRecords.FittedClockMapping,
            speechFlags);
    }

    /// <summary>The session time of a phone time, by the clock fit, or the time the line was read.</summary>
    private (long Time, PhoneTimeMapping? Mapping, string[] Flags) Place(
        long? phoneMicroseconds,
        long receivedAt,
        bool ready)
    {
        var mapping = ready && phoneMicroseconds is { } micros ? _clock?.Map(micros * 1000) : null;
        return mapping is null
            ? (receivedAt, null, [AndroidScreenRecords.ReceiptTimeFlag])
            : (mapping.SessionNanoseconds, mapping, [AndroidTalkBackRecords.PhoneLogTimeFlag]);
    }

    /// <summary>The time held at the channel's last time, so that its times do not go backward.</summary>
    private long Hold(string channel, long time, ref string[] flags)
    {
        var last = _lastTimes.GetValueOrDefault(channel, long.MinValue);
        if (time < last)
        {
            time = last;
            flags = [.. flags, AndroidCompanionRecords.FitHeldFlag];
        }

        _lastTimes[channel] = time;
        return time;
    }

    private static string Shortened(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= StatusTextLength ? trimmed : trimmed[..(StatusTextLength - 3)].TrimEnd() + "...";
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

    /// <summary>The text ending with a full stop, for a message that does not end with one.</summary>
    private static string Sentence(string text) =>
        text.Length == 0 || text[^1] is '.' or '!' or '?' ? text : text + ".";

    private void Unavailable(string reason, bool connectionLost)
    {
        var text = connectionLost
            ? $"What TalkBack said stopped being recorded during the recording. {reason} The recording goes on without it."
            : $"What TalkBack said could not be recorded: {reason} The recording goes on without it.";
        var at = _context?.Clock.GetElapsedNanoseconds() ?? 0;
        var flags = Array.Empty<string>();
        Emit(
            AndroidTalkBackRecords.LogChannel,
            ref _logSequence,
            Hold(AndroidTalkBackRecords.LogChannel, at, ref flags),
            AndroidTalkBackRecords.UnavailableEventType,
            new AndroidTalkBackUnavailable(_serial, text, connectionLost),
            qualityFlags: flags);

        // After the record, so that whoever sees the reason finds it.
        HealthState = CollectorHealthState.Degraded;
        _unavailable = text;
    }

    private void Emit<T>(
        string channel,
        ref ulong sequence,
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
            channel,
            Interlocked.Increment(ref sequence),
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

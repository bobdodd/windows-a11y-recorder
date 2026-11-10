using System.Globalization;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// Records the phone's accessibility events (android-implementation-plan.md,
/// slice 7, step 7a).
///
/// At the start it asks the companion app, through the companion
/// collector's connection, for every accessibility event its service
/// receives, and stores each on
/// <see cref="AndroidAccessibilityRecords.EventsChannel"/> with every field
/// the event carries, its extra records and the item it came from. The
/// connection used by the recorder's checks does not ask, so it is sent
/// none.
///
/// An event's time is <c>getEventTime()</c>, milliseconds of the uptime
/// clock that <c>System.nanoTime()</c> is based on, so each record is
/// placed on the session clock through the companion collector's clock fit,
/// with a millisecond more uncertainty, and otherwise at the time the
/// recorder read it. An event from before the recording's start is not
/// kept. The status line counts the events, and gives the most content
/// changes in one second, the rate the plan measures before setting any
/// limit on the tree snapshots of step 7b. A phone whose companion app
/// cannot be reached does not stop the recording: the collector records
/// why, reports itself degraded, and the recording goes on.
/// </summary>
public sealed class AndroidAccessibilityCollector : ICaptureCollector
{
    public const string Type = "android.accessibility";

    /// <summary>How long the events wait for the companion collector's first clock sample before placing them by their receipt.</summary>
    public static readonly TimeSpan ClockWait = TimeSpan.FromSeconds(3);

    /// <summary>The longest item text the status line shows, in characters.</summary>
    public const int StatusTextLength = 60;

    /// <summary>An event time in milliseconds is up to this much earlier than the event.</summary>
    public const long MillisecondUncertainty = 1_000_000;

    private const long OneSecond = 1_000_000_000;

    private readonly string _serial;
    private readonly IPhoneAccessibilityEvents _events;
    private readonly IPhoneClock? _clock;
    private readonly object _gate = new();
    private readonly List<long> _delays = [];
    private readonly List<long> _readTimes = [];
    private readonly Queue<long> _lastSecond = new();
    private readonly CancellationTokenSource _closing = new();

    private CollectorInitializationContext? _context;
    private Task? _running;
    private long _start;
    private long _lastTime = long.MinValue;
    private volatile string? _unavailable;
    private volatile string? _latestFocus;
    private volatile bool _reading;
    private ulong _sequence;
    private ulong _lifecycleSequence;
    private long _count;
    private long _contentChanges;
    private long _focusEvents;
    private long _windowChanges;
    private long _unreadable;
    private long _earlier;
    private int _peakPerSecond;
    private bool _stopping;
    private bool _clockless;

    /// <param name="events">The companion collector, which asks its app for the events.</param>
    /// <param name="clock">The companion collector's clock fit; null to place every event at its receipt.</param>
    public AndroidAccessibilityCollector(string serial, IPhoneAccessibilityEvents events, IPhoneClock? clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(events);
        _serial = serial;
        _events = events;
        _clock = clock;
        Descriptor = CollectorDescriptor.Create(
            Type,
            nameof(AndroidAccessibilityCollector),
            typeof(AndroidAccessibilityCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AndroidAccessibilityRecords.EventsChannel],
            "companion-accessibility-service");
    }

    public CollectorDescriptor Descriptor { get; }

    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;

    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

    public string? HealthReason => _unavailable;

    public string? Summary
    {
        get
        {
            if (!_reading)
            {
                return null;
            }

            var count = Interlocked.Read(ref _count);
            if (count == 0)
            {
                var none = "Accessibility events: none yet";
                if (Interlocked.Read(ref _unreadable) is var failed and > 0)
                {
                    none += string.Create(CultureInfo.InvariantCulture, $", {failed} could not be read");
                }

                return none + ".";
            }

            var contentChanges = Interlocked.Read(ref _contentChanges);
            var focus = Interlocked.Read(ref _focusEvents);
            var windows = Interlocked.Read(ref _windowChanges);
            int peak;
            lock (_lastSecond)
            {
                peak = _peakPerSecond;
            }

            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"Accessibility events: {count}, including {contentChanges} {Plural(contentChanges, "content change", "content changes")} " +
                $"(at most {peak} in one second), {focus} accessibility focus {Plural(focus, "event", "events")} " +
                $"and {windows} window {Plural(windows, "change", "changes")}");
            if (_latestFocus is { } latest)
            {
                text += $", latest accessibility focus on {latest}";
            }

            if (Interlocked.Read(ref _unreadable) is var unreadable and > 0)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", {unreadable} could not be read");
            }

            if (Median(_readTimes) is { } read)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", the app took a median {read / 1_000_000.0:0.0} ms to read each");
            }

            if (Median(_delays) is { } delay)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", median delay from phone to recorder {delay / 1_000_000.0:0.0} ms");
            }
            else if (_clockless)
            {
                text += ", placed by the time each event arrived, as there is no phone clock";
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
            return ValueTask.FromResult(CapabilityResult.Supported(Descriptor.Channels.ToArray()));
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
        _closing.Dispose();
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private async Task CloseAsync()
    {
        try
        {
            _closing.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Closed already.
        }

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
        var token = _closing.Token;
        try
        {
            var reader = await _events.RequestAccessibilityEventsAsync(token).ConfigureAwait(false);
            if (reader is null)
            {
                if (!Stopping())
                {
                    Unavailable(
                        "the companion app is not connected, so they could not be asked for. The android.companion line says why.",
                        connectionLost: false);
                }

                return;
            }

            var ready = await WaitForClockAsync(token).ConfigureAwait(false);
            _clockless = !ready;
            _reading = true;
            try
            {
                await foreach (var received in reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    Handle(received, ready);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Stopping: the events that arrived before Stop are kept.
                while (reader.TryRead(out var received))
                {
                    Handle(received, ready);
                }

                return;
            }

            if (!Stopping())
            {
                Unavailable(
                    "the connection to the companion app ended: the phone was unplugged, adb stopped, or the companion app was turned off.",
                    connectionLost: true);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopped before the events were asked for.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Unavailable($"they could not be read: {Sentence(ex.Message)}", connectionLost: false);
        }
    }

    private void Handle(ReceivedAccessibilityEvent received, bool ready)
    {
        if (received.Event is not { } accessibilityEvent)
        {
            Interlocked.Increment(ref _unreadable);
            Unreadable(received);
            return;
        }

        // getEventTime() is in milliseconds of the uptime clock; an event
        // without one is placed by the time the service received it.
        var timed = accessibilityEvent.EventTimeMilliseconds > 0;
        var phone = timed
            ? accessibilityEvent.EventTimeMilliseconds * 1_000_000
            : accessibilityEvent.ReceivedPhoneNanoseconds;
        var mapping = ready ? _clock?.Map(phone) : null;
        long time;
        string[] flags;
        long? uncertainty = null;
        if (mapping is null)
        {
            time = received.ReceivedAt;
            flags = [AndroidScreenRecords.ReceiptTimeFlag];
        }
        else
        {
            time = mapping.SessionNanoseconds;
            flags = [timed ? AndroidAccessibilityRecords.PhoneEventTimeFlag : AndroidAccessibilityRecords.PhoneReceiptTimeFlag];
            uncertainty = mapping.UncertaintyNanoseconds + (timed ? MillisecondUncertainty : 0);
            if (time < _start)
            {
                // From before the recording.
                Interlocked.Increment(ref _earlier);
                return;
            }

            lock (_delays)
            {
                _delays.Add(received.ReceivedAt - time);
            }
        }

        lock (_readTimes)
        {
            _readTimes.Add(accessibilityEvent.HandledPhoneNanoseconds - accessibilityEvent.ReceivedPhoneNanoseconds);
        }

        Count(accessibilityEvent, phone);
        var payload = accessibilityEvent with
        {
            ContentChangeTypeNames = AndroidAccessibilityRecords.Names(
                accessibilityEvent.ContentChangeTypes,
                AndroidAccessibilityRecords.ContentChangeTypes),
            WindowChangeNames = AndroidAccessibilityRecords.Names(
                accessibilityEvent.WindowChanges,
                AndroidAccessibilityRecords.WindowChanges),
            ReceivedAtNanoseconds = received.ReceivedAt
        };
        Emit(
            Hold(time, ref flags),
            AndroidAccessibilityRecords.EventType,
            payload,
            phone,
            uncertainty,
            mapping is null ? null : AndroidCompanionRecords.FittedClockMapping,
            flags);
    }

    private void Count(AndroidAccessibilityEvent accessibilityEvent, long phone)
    {
        Interlocked.Increment(ref _count);
        switch (accessibilityEvent.EventTypeName)
        {
            case AndroidAccessibilityRecords.ContentChangedName:
                Interlocked.Increment(ref _contentChanges);
                lock (_lastSecond)
                {
                    // The content changes in the second up to this one, by
                    // phone time.
                    while (_lastSecond.Count > 0 && _lastSecond.Peek() <= phone - OneSecond)
                    {
                        _lastSecond.Dequeue();
                    }

                    _lastSecond.Enqueue(phone);
                    _peakPerSecond = Math.Max(_peakPerSecond, _lastSecond.Count);
                }

                break;
            case AndroidAccessibilityRecords.AccessibilityFocusedName:
                Interlocked.Increment(ref _focusEvents);
                _latestFocus = Describe(accessibilityEvent);
                break;
            case AndroidAccessibilityRecords.WindowStateChangedName:
            case AndroidAccessibilityRecords.WindowsChangedName:
                Interlocked.Increment(ref _windowChanges);
                break;
        }
    }

    /// <summary>The item an event came from, as the status line names it.</summary>
    public static string Describe(AndroidAccessibilityEvent accessibilityEvent)
    {
        var source = accessibilityEvent.Source;
        var words = new[]
            {
                source?.ContentDescription,
                source?.Text,
                string.Join(" ", accessibilityEvent.Text),
                accessibilityEvent.ContentDescription
            }
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        if (words is not null)
        {
            return "\"" + Shortened(words) + "\"";
        }

        var className = source?.ClassName ?? accessibilityEvent.ClassName;
        var shortName = className is null ? "item" : className[(className.LastIndexOf('.') + 1)..];
        return $"an item with no text ({shortName})";
    }

    private void Unreadable(ReceivedAccessibilityEvent received)
    {
        var flags = new[] { AndroidScreenRecords.ReceiptTimeFlag };
        Emit(
            Hold(received.ReceivedAt, ref flags),
            AndroidAccessibilityRecords.UnavailableEventType,
            new AndroidAccessibilityUnavailable(
                _serial,
                $"An accessibility event from the phone could not be read: {Sentence(received.Problem ?? "no reason given")}",
                false),
            qualityFlags: flags);
    }

    /// <summary>The time held at the channel's last time, so that its times do not go backward.</summary>
    private long Hold(long time, ref string[] flags)
    {
        lock (_gate)
        {
            if (time < _lastTime)
            {
                time = _lastTime;
                flags = [.. flags, AndroidCompanionRecords.FitHeldFlag];
            }

            _lastTime = time;
            return time;
        }
    }

    private static string Shortened(string text)
    {
        var trimmed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length <= StatusTextLength ? trimmed : trimmed[..(StatusTextLength - 3)].TrimEnd() + "...";
    }

    private static string Plural(long count, string one, string many) => count == 1 ? one : many;

    private async Task<bool> WaitForClockAsync(CancellationToken cancellation)
    {
        if (_clock is null)
        {
            return false;
        }

        var first = await Task.WhenAny(_clock.FirstSample, Task.Delay(ClockWait, cancellation)).ConfigureAwait(false);
        return first == _clock.FirstSample && await _clock.FirstSample.ConfigureAwait(false);
    }

    private static long? Median(List<long> values)
    {
        lock (values)
        {
            if (values.Count == 0)
            {
                return null;
            }

            var sorted = values.ToArray();
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
            ? $"The phone's accessibility events stopped being recorded during the recording: {reason} The recording goes on without them."
            : $"The phone's accessibility events could not be recorded: {reason} The recording goes on without them.";
        var at = _context?.Clock.GetElapsedNanoseconds() ?? 0;
        var flags = Array.Empty<string>();
        Emit(
            Hold(at, ref flags),
            AndroidAccessibilityRecords.UnavailableEventType,
            new AndroidAccessibilityUnavailable(_serial, text, connectionLost),
            qualityFlags: flags);

        // After the record, so that whoever sees the reason finds it.
        HealthState = CollectorHealthState.Degraded;
        _unavailable = text;
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
            AndroidAccessibilityRecords.EventsChannel,
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

using System.Globalization;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// Records the companion app's connection, the phone clock and the phone
/// display (android-implementation-plan.md, slice 2).
///
/// At the start it connects to the companion app through adb forward. A
/// phone the app cannot be reached on does not stop the recording: the
/// collector records why, reports itself degraded, and the recording goes on
/// without the phone's clock. While connected it asks for a clock sample
/// every <see cref="SampleInterval"/>, and places each display change on the
/// session clock by the line fitted to the samples so far.
/// </summary>
public sealed class AndroidCompanionCollector : ICaptureCollector, IPhoneClock
{
    public const string Type = "android.companion";

    public static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan ClockTimeout = TimeSpan.FromSeconds(1);

    private readonly IAdb _adb;
    private readonly string _serial;
    private readonly Func<Func<long>, CancellationToken, Task<CompanionConnection>> _open;
    private readonly PhoneClockMapper _mapper = new();
    private readonly object _gate = new();
    private readonly TaskCompletionSource<bool> _firstSample = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CollectorInitializationContext? _context;
    private CompanionConnection? _connection;
    private CancellationTokenSource? _running;
    private Task? _sampling;
    private Task? _displays;
    private Task? _watching;
    private ulong _companionSequence;
    private ulong _clockSequence;
    private ulong _displaySequence;
    private ulong _lifecycleSequence;
    private long _lastDisplayTime = long.MinValue;
    private int _missedSamples;
    private int _discontinuities;
    private volatile string? _lastDisplay;
    private bool _stopping;

    /// <param name="open">Opens the connection; tests give a fake app. By default, <see cref="CompanionLink.OpenAsync"/>.</param>
    public AndroidCompanionCollector(
        IAdb adb,
        string serial,
        Func<Func<long>, CancellationToken, Task<CompanionConnection>>? open = null)
    {
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        _adb = adb;
        _serial = serial;
        _open = open ?? ((now, cancellation) => CompanionLink.OpenAsync(_adb, _serial, now, cancellation));
        Descriptor = CollectorDescriptor.Create(
            Type,
            nameof(AndroidCompanionCollector),
            typeof(AndroidCompanionCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AndroidCompanionRecords.CompanionChannel, AndroidCompanionRecords.ClockChannel, AndroidCompanionRecords.DisplayChannel],
            "adb-forward-localabstract");
    }

    public CollectorDescriptor Descriptor { get; }

    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;

    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

    public string? HealthReason { get; private set; }

    public string? Summary
    {
        get
        {
            // A companion that could not be reached, or was lost, is told by
            // HealthReason; the summary gives what was recorded.
            if (_connection is null)
            {
                return null;
            }

            var hello = _connection.Hello;
            var text = $"Companion app {hello.VersionName}: {_mapper.SampleCount.ToString(CultureInfo.InvariantCulture)} clock samples";
            if (_mapper.LargestErrorBound is { } bound)
            {
                text += $", largest error bound {Milliseconds(bound)} ms";
            }

            if (_lastDisplay is { } shown)
            {
                text += ", " + shown;
            }

            if (_discontinuities > 0)
            {
                text += $", {_discontinuities.ToString(CultureInfo.InvariantCulture)} clock discontinuities";
            }

            if (_missedSamples > 0)
            {
                text += $", {_missedSamples.ToString(CultureInfo.InvariantCulture)} requests unanswered";
            }

            text += ".";

            return text;
        }
    }

    /// <summary>The fit the collector places phone times by.</summary>
    public PhoneClockMapper Mapper => _mapper;

    public Task<bool> FirstSample => _firstSample.Task;

    public PhoneTimeMapping? Map(long phoneNanoseconds) => _mapper.Map(phoneNanoseconds);

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

        try
        {
            _connection = await _open(context.Clock.GetElapsedNanoseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (CompanionUnavailableException exception)
        {
            _firstSample.TrySetResult(false);
            Unavailable(exception.Message, connectionLost: false);
            return CollectorTransitionResult.Success(LifecycleState);
        }

        var hello = _connection.Hello;
        Emit(
            AndroidCompanionRecords.CompanionChannel,
            ref _companionSequence,
            _connection.HelloReceivedAt,
            AndroidCompanionRecords.ConnectedEventType,
            new AndroidCompanionConnected(_serial, hello.Protocol, hello.VersionName, hello.VersionCode, hello.AndroidRelease, hello.Sdk),
            native: hello.Time,
            qualityFlags: ["receipt-time-stamp"]);

        _running = new CancellationTokenSource();
        var token = _running.Token;
        var connection = _connection;
        _sampling = Task.Run(() => SampleAsync(connection, token), CancellationToken.None);
        _displays = Task.Run(() => DisplaysAsync(connection, token), CancellationToken.None);
        _watching = Task.Run(() => WatchAsync(connection), CancellationToken.None);
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

        _running?.Cancel();
        await WaitQuietlyAsync(_sampling).ConfigureAwait(false);
        _firstSample.TrySetResult(_mapper.SampleCount > 0);
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        await WaitQuietlyAsync(_displays).ConfigureAwait(false);
        await WaitQuietlyAsync(_watching).ConfigureAwait(false);

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

        _running?.Cancel();
        await WaitQuietlyAsync(_sampling).ConfigureAwait(false);
        _firstSample.TrySetResult(false);
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        await WaitQuietlyAsync(_displays).ConfigureAwait(false);
        await WaitQuietlyAsync(_watching).ConfigureAwait(false);
        _running?.Dispose();
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private async Task SampleAsync(CompanionConnection connection, CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(SampleInterval);
        try
        {
            do
            {
                PhoneClockExchange exchange;
                try
                {
                    exchange = await connection.ExchangeClockAsync(ClockTimeout, cancellation).ConfigureAwait(false);
                }
                catch (CompanionUnavailableException) when (!connection.Closed.IsCompleted)
                {
                    Interlocked.Increment(ref _missedSamples);
                    continue;
                }
                catch (CompanionUnavailableException)
                {
                    // WatchAsync records the connection's end.
                    return;
                }

                var (segment, discontinuity) = _mapper.Add(exchange);
                if (discontinuity)
                {
                    Interlocked.Increment(ref _discontinuities);
                }

                Emit(
                    AndroidCompanionRecords.ClockChannel,
                    ref _clockSequence,
                    exchange.SessionMidpoint,
                    AndroidCompanionRecords.ClockSampleEventType,
                    new AndroidClockSample(
                        segment,
                        discontinuity,
                        exchange.RecorderSend,
                        exchange.PhoneReceive,
                        exchange.PhoneSend,
                        exchange.RecorderReceive,
                        exchange.ErrorBound),
                    native: exchange.PhoneMidpoint,
                    uncertainty: exchange.ErrorBound);
                _firstSample.TrySetResult(true);
            }
            while (await timer.WaitForNextTickAsync(cancellation).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Stopping.
        }
        finally
        {
            _firstSample.TrySetResult(_mapper.SampleCount > 0);
        }
    }

    private async Task DisplaysAsync(CompanionConnection connection, CancellationToken cancellation)
    {
        // Every display message is recorded, including those that arrive
        // as the recording stops, so this reads until the connection ends.
        await foreach (var received in connection.Displays.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            // The first display arrives with the greeting, before the first
            // clock sample, so it waits for one.
            var mapped = await _firstSample.Task.ConfigureAwait(false) ? _mapper.Map(received.Display.Time) : null;
            var display = received.Display;
            var payload = new AndroidDisplayState(display.Reason, display.Rotation, display.ModeWidth, display.ModeHeight);
            _lastDisplay = string.Create(
                CultureInfo.InvariantCulture,
                $"screen rotated {display.Rotation} degrees, {display.ModeWidth} by {display.ModeHeight}");
            if (mapped is null)
            {
                // No clock sample was taken: the time the recorder read it.
                Emit(
                    AndroidCompanionRecords.DisplayChannel,
                    ref _displaySequence,
                    received.ReceivedAt,
                    AndroidCompanionRecords.DisplayEventType,
                    payload,
                    native: display.Time,
                    qualityFlags: ["receipt-time-stamp"]);
                continue;
            }

            var time = mapped.SessionNanoseconds;
            string[] flags = [];
            if (time < _lastDisplayTime)
            {
                time = _lastDisplayTime;
                flags = [AndroidCompanionRecords.FitHeldFlag];
            }

            _lastDisplayTime = time;
            Emit(
                AndroidCompanionRecords.DisplayChannel,
                ref _displaySequence,
                time,
                AndroidCompanionRecords.DisplayEventType,
                payload,
                native: display.Time,
                uncertainty: mapped.UncertaintyNanoseconds,
                clockMapping: AndroidCompanionRecords.FittedClockMapping,
                qualityFlags: flags);
        }
    }

    private async Task WatchAsync(CompanionConnection connection)
    {
        var reason = await connection.Closed.ConfigureAwait(false);
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }
        }

        Unavailable(reason, connectionLost: true);
    }

    private void Unavailable(string reason, bool connectionLost)
    {
        var text = connectionLost
            ? $"The connection to the phone's companion app ended during the recording: {reason} The recording goes on without the phone's clock."
            : $"The phone's companion app could not be reached: {reason} The recording goes on without the phone's clock.";
        HealthState = CollectorHealthState.Degraded;
        HealthReason = text;
        var at = _context?.Clock.GetElapsedNanoseconds() ?? 0;
        Emit(
            AndroidCompanionRecords.CompanionChannel,
            ref _companionSequence,
            at,
            AndroidCompanionRecords.UnavailableEventType,
            new AndroidCompanionUnavailable(_serial, reason, connectionLost));
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

        var next = Interlocked.Increment(ref sequence);
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

    private static async Task WaitQuietlyAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    private static string Milliseconds(long nanoseconds) =>
        (nanoseconds / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture);
}

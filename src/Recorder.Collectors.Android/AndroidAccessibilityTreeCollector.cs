using System.Globalization;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// Records snapshots of the phone's accessibility tree
/// (android-implementation-plan.md, slice 7, step 7b).
///
/// At the start it asks the companion app, through the companion
/// collector's connection, for a snapshot of every window, and the app
/// sends more as the phone changes: one of every window on each window
/// change, and one of each window whose content changed (AccessibilityTrees.java).
/// The app takes one snapshot at a time, so changes that arrive while one
/// is taken are covered by the next; each says how many it covers. Each is
/// stored on <see cref="AndroidAccessibilityRecords.TreeChannel"/>, placed
/// halfway between when the app started and finished taking it, through
/// the companion collector's clock fit, with half the time it took as more
/// uncertainty; without a fit, at the time the recorder read it.
///
/// The status line counts the snapshots and gives their size and how long
/// they took, the figures the plan measures before setting any limit. A
/// phone whose companion app cannot be reached does not stop the
/// recording: the collector records why, reports itself degraded, and the
/// recording goes on.
/// </summary>
public sealed class AndroidAccessibilityTreeCollector : ICaptureCollector
{
    public const string Type = "android.accessibility-tree";

    private const long OneSecond = 1_000_000_000;

    private readonly string _serial;
    private readonly IPhoneAccessibilityTrees _trees;
    private readonly IPhoneClock? _clock;
    private readonly object _gate = new();
    private readonly List<long> _delays = [];
    private readonly List<long> _takeTimes = [];
    private readonly Queue<long> _lastSecond = new();
    private readonly CancellationTokenSource _closing = new();

    private CollectorInitializationContext? _context;
    private Task? _running;
    private long _start;
    private long _lastTime = long.MinValue;
    private volatile string? _unavailable;
    private volatile bool _reading;
    private ulong _sequence;
    private ulong _lifecycleSequence;
    private long _count;
    private long _everyWindow;
    private long _unreadable;
    private long _earlier;
    private long _truncated;
    private long _problems;
    private long _skipped;
    private long _lastIndex;
    private int _latestWindows;
    private int _latestItems;
    private int _largestItems;
    private long _largestBytes;
    private long _longestTake;
    private int _peakPerSecond;
    private bool _stopping;
    private bool _clockless;

    /// <param name="trees">The companion collector, which asks its app for the snapshots.</param>
    /// <param name="clock">The companion collector's clock fit; null to place every snapshot at its receipt.</param>
    public AndroidAccessibilityTreeCollector(string serial, IPhoneAccessibilityTrees trees, IPhoneClock? clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(trees);
        _serial = serial;
        _trees = trees;
        _clock = clock;
        Descriptor = CollectorDescriptor.Create(
            Type,
            nameof(AndroidAccessibilityTreeCollector),
            typeof(AndroidAccessibilityTreeCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [AndroidAccessibilityRecords.TreeChannel],
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
                var none = "Accessibility tree snapshots: none yet";
                if (Interlocked.Read(ref _unreadable) is var failed and > 0)
                {
                    none += string.Create(CultureInfo.InvariantCulture, $", {failed} could not be read");
                }

                return none + ".";
            }

            string text;
            lock (_gate)
            {
                text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Accessibility tree snapshots: {count} (at most {_peakPerSecond} in one second), " +
                    $"{Interlocked.Read(ref _everyWindow)} of every window, " +
                    $"the latest with {_latestWindows} {Plural(_latestWindows, "window", "windows")} and {_latestItems} {Plural(_latestItems, "item", "items")}, " +
                    $"the largest {_largestItems} {Plural(_largestItems, "item", "items")} and {_largestBytes / 1024.0:0} KB");
            }

            if (Median(_takeTimes) is { } take)
            {
                text += string.Create(
                    CultureInfo.InvariantCulture,
                    $", the app took a median {take / 1_000_000.0:0.0} ms to take each and at most {Interlocked.Read(ref _longestTake) / 1_000_000.0:0.0} ms");
            }

            if (Interlocked.Read(ref _truncated) is var truncated and > 0)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", {truncated} with a window cut short at {AndroidAccessibilityRecords.MaxTreeItems:N0} items");
            }

            if (Interlocked.Read(ref _problems) is var problems and > 0)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", {problems} stopped early as the phone changed");
            }

            if (Interlocked.Read(ref _skipped) is var skipped and > 0)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", {skipped} missing");
            }

            if (Interlocked.Read(ref _unreadable) is var unreadable and > 0)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", {unreadable} could not be read");
            }

            if (Median(_delays) is { } delay)
            {
                text += string.Create(CultureInfo.InvariantCulture, $", median delay from phone to recorder {delay / 1_000_000.0:0.0} ms");
            }
            else if (_clockless)
            {
                text += ", placed by the time each arrived, as there is no phone clock";
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
            var reader = await _trees.RequestAccessibilityTreesAsync(token).ConfigureAwait(false);
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
                // Stopping: the snapshots that arrived before Stop are kept.
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
            // Stopped before the snapshots were asked for.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Unavailable($"they could not be read: {Sentence(ex.Message)}", connectionLost: false);
        }
    }

    private void Handle(ReceivedAccessibilityTree received, bool ready)
    {
        var (read, problem) = CompanionProtocol.ReadTree(received.Line);
        if (read is not { } tree)
        {
            Interlocked.Increment(ref _unreadable);
            Unreadable(received.ReceivedAt, problem);
            return;
        }

        // Halfway through the taking, with half of it as more uncertainty.
        var taking = Math.Max(0, tree.FinishedPhoneNanoseconds - tree.StartedPhoneNanoseconds);
        var phone = tree.StartedPhoneNanoseconds + (taking / 2);
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
            flags = [AndroidAccessibilityRecords.PhoneSnapshotTimeFlag];
            uncertainty = mapping.UncertaintyNanoseconds + (taking - (taking / 2));
            if (time < _start)
            {
                // From before the recording.
                Interlocked.Increment(ref _earlier);
                return;
            }

            var finished = mapping.SessionNanoseconds + (taking - (taking / 2));
            lock (_delays)
            {
                _delays.Add(received.ReceivedAt - finished);
            }
        }

        lock (_takeTimes)
        {
            _takeTimes.Add(taking);
        }

        Count(tree, taking);
        Emit(
            Hold(time, ref flags),
            AndroidAccessibilityRecords.TreeEventType,
            tree with { ReceivedAtNanoseconds = received.ReceivedAt },
            phone,
            uncertainty,
            mapping is null ? null : AndroidCompanionRecords.FittedClockMapping,
            flags);
    }

    private void Count(AndroidAccessibilityTree tree, long taking)
    {
        Interlocked.Increment(ref _count);
        if (tree.EveryWindow)
        {
            Interlocked.Increment(ref _everyWindow);
        }

        if (tree.Problem is not null)
        {
            Interlocked.Increment(ref _problems);
        }

        if (tree.Windows.Any(window => window.Truncated))
        {
            Interlocked.Increment(ref _truncated);
        }

        var items = tree.Windows.Sum(window => window.Items?.Count ?? 0);
        lock (_gate)
        {
            // A gap in the app's count is a snapshot the recorder never saw.
            if (_lastIndex > 0 && tree.Index > _lastIndex + 1)
            {
                _skipped += tree.Index - _lastIndex - 1;
            }

            _lastIndex = Math.Max(_lastIndex, tree.Index);
            _latestWindows = tree.Windows.Count;
            _latestItems = items;
            _largestItems = Math.Max(_largestItems, items);
            _largestBytes = Math.Max(_largestBytes, tree.MessageBytes);
            _longestTake = Math.Max(_longestTake, taking);

            // The snapshots in the second up to this one, by phone time.
            var finished = tree.FinishedPhoneNanoseconds;
            while (_lastSecond.Count > 0 && _lastSecond.Peek() <= finished - OneSecond)
            {
                _lastSecond.Dequeue();
            }

            _lastSecond.Enqueue(finished);
            _peakPerSecond = Math.Max(_peakPerSecond, _lastSecond.Count);
        }
    }

    private void Unreadable(long receivedAt, string? problem)
    {
        var flags = new[] { AndroidScreenRecords.ReceiptTimeFlag };
        Emit(
            Hold(receivedAt, ref flags),
            AndroidAccessibilityRecords.UnavailableTreeEventType,
            new AndroidAccessibilityUnavailable(
                _serial,
                $"A snapshot of the phone's accessibility tree could not be read: {Sentence(problem ?? "no reason given")}",
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

    private static string Plural(long count, string one, string many) => count == 1 ? one : many;

    private async Task<bool> WaitForClockAsync(CancellationToken cancellation)
    {
        if (_clock is null)
        {
            return false;
        }

        var first = await Task.WhenAny(_clock.FirstSample, Task.Delay(AndroidAccessibilityCollector.ClockWait, cancellation)).ConfigureAwait(false);
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
            ? $"The phone's accessibility tree stopped being recorded during the recording: {reason} The recording goes on without it."
            : $"The phone's accessibility tree could not be recorded: {reason} The recording goes on without it.";
        var at = _context?.Clock.GetElapsedNanoseconds() ?? 0;
        var flags = Array.Empty<string>();
        Emit(
            Hold(at, ref flags),
            AndroidAccessibilityRecords.UnavailableTreeEventType,
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
            AndroidAccessibilityRecords.TreeChannel,
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

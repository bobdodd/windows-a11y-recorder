using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using Recorder.Contracts;

namespace Recorder.Collectors.Automation;

/// <summary>
/// A UI Automation event the UI Automation collector recorded, passed on as
/// it is written: its time, type, the process of its element, and the
/// element, for the window snapshot collector.
/// </summary>
public sealed record UiaObservationNotice(
    long MonotonicNanoseconds,
    string EventType,
    string EventId,
    int? ProcessId,
    IUIAutomationElement? Element);

/// <summary>
/// Records the whole UI Automation tree of NVDA's windows, so a report can
/// say what NVDA was showing at any time: when focus enters a window of
/// NVDA's main process, when the window has been quiet for 300 ms after its
/// structure, selection, text, or a watched property changed, and when
/// focus moves to an element the last snapshot did not hold or held in
/// another state. Each read stops at 5,000 elements or 2 seconds, and says
/// so. The trees are read on a thread and UI Automation connection of its
/// own, so the UI Automation collector's queue never waits for them, and
/// recorded under a collector of their own, so their times are in order
/// within it. See docs/architecture/screen-reader-activity.md, "2c-4, what
/// NVDA's windows show".
/// </summary>
public sealed class NvdaWindowSnapshotCollector : ICaptureCollector
{
    private const int NoticeCapacity = 256;
    private const string WatchedProduct = "NVDA";

    private readonly object _gate = new();
    private readonly BlockingCollection<UiaObservationNotice> _notices = new(NoticeCapacity);
    private readonly CancellationTokenSource _stop = new();
    private CollectorInitializationContext? _context;
    private Thread? _thread;
    private volatile int[] _processIds = [];
    private volatile bool _focusInWatched;
    private long _dropped;
    private long _failures;
    private long _eventSequence = -1;
    private long _lifecycleSequence = -1;
    private bool _disposed;

    public NvdaWindowSnapshotCollector()
    {
        Descriptor = CollectorDescriptor.Create(
            WindowSnapshotRecords.CollectorType,
            nameof(NvdaWindowSnapshotCollector),
            typeof(NvdaWindowSnapshotCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            [WindowSnapshotRecords.Channel],
            "windows.ui-automation");
    }

    public CollectorDescriptor Descriptor { get; }
    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;
    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;
    public string? HealthReason { get; private set; }

    /// <summary>
    /// The main processes of a watched product, from assistive technology
    /// tracking; only NVDA's are kept.
    /// </summary>
    public void SetScreenReaderProcesses(string product, IReadOnlyCollection<int> processIds)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        if (string.Equals(product, WatchedProduct, StringComparison.Ordinal))
        {
            _processIds = [.. processIds];
        }
    }

    /// <summary>
    /// An event the UI Automation collector recorded. Events of NVDA's main
    /// process are queued; a focus change elsewhere after one in NVDA's is
    /// queued without its element, as focus leaving NVDA.
    /// </summary>
    public void Observe(UiaObservationNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (LifecycleState != CollectorLifecycleState.Running)
        {
            return;
        }

        var watched = notice.ProcessId is int processId && _processIds.Contains(processId);
        if (notice.EventType == "focus-changed")
        {
            if (!watched && !_focusInWatched)
            {
                return;
            }

            _focusInWatched = watched;
        }
        else if (!watched)
        {
            return;
        }

        if (!_notices.TryAdd(watched ? notice : notice with { Element = null }))
        {
            Interlocked.Increment(ref _dropped);
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
                    true));
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
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Ready)
            {
                return ValueTask.FromResult(CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot start from {LifecycleState}."));
            }

            LifecycleState = CollectorLifecycleState.Running;
            EmitLifecycle("started", boundary);
            _thread = new Thread(ReadLoop) { IsBackground = true, Name = "NVDA window snapshots" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
            return ValueTask.FromResult(CollectorTransitionResult.Success(LifecycleState));
        }
    }

    public async ValueTask<CollectorTransitionResult> StopAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (LifecycleState is CollectorLifecycleState.Stopped or CollectorLifecycleState.Disposed)
            {
                return CollectorTransitionResult.Success(LifecycleState);
            }

            if (LifecycleState != CollectorLifecycleState.Running)
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot stop from {LifecycleState}.");
            }

            LifecycleState = CollectorLifecycleState.Stopping;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        if (_thread is not null)
        {
            // A read in progress ends at its 2 second limit.
            await Task.Run(() => _thread.Join(TimeSpan.FromSeconds(5)), cancellationToken).ConfigureAwait(false);
        }

        var failures = Interlocked.Read(ref _failures);
        if (failures > 0)
        {
            HealthState = CollectorHealthState.Degraded;
            Emit(
                "collector-omission",
                new { reason = WindowSnapshotRecords.HandlingFailedReason, count = failures },
                CollectorClosingTimestamp.Resolve(_context?.Clock, boundary),
                "evidence-dropped");
        }

        var dropped = Interlocked.Read(ref _dropped);
        if (dropped > 0)
        {
            HealthState = CollectorHealthState.Degraded;
            Emit(
                "collector-omission",
                new { reason = WindowSnapshotRecords.NoticesDroppedReason, count = dropped },
                CollectorClosingTimestamp.Resolve(_context?.Clock, boundary),
                "evidence-dropped");
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

        if (LifecycleState == CollectorLifecycleState.Running)
        {
            var now = _context?.Clock.GetElapsedNanoseconds() ?? 0;
            await StopAsync(new SessionBoundary(now, DateTimeOffset.UtcNow), CancellationToken.None).ConfigureAwait(false);
        }

        _notices.Dispose();
        _stop.Dispose();
        _disposed = true;
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private long Now() => _context?.Clock.GetElapsedNanoseconds() ?? 0;

    private void ReadLoop()
    {
        UiaWindowTreeSource source;
        try
        {
            source = new UiaWindowTreeSource(new CUIAutomation8Class());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Any failure here, as UI Automation not being registered,
            // must not end the recorder from this thread.
            HealthState = CollectorHealthState.Degraded;
            HealthReason = "The UI Automation client for NVDA window snapshots could not start.";
            Emit(
                "collector-omission",
                new { reason = WindowSnapshotRecords.ReaderFailedReason },
                Now(),
                "evidence-dropped");
            return;
        }

        var state = new ReadState();
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            var wait = state.ChangedAt is long changed
                ? (int)Math.Clamp((changed + WindowSnapshotRecords.QuietNanoseconds - Now()) / 1_000_000, 0, 500)
                : 500;
            try
            {
                if (_notices.TryTake(out var notice, wait, token))
                {
                    Handle(source, state, notice);
                    continue;
                }

                if (state.ChangedAt is long at && state.Window is { } window &&
                    Now() - at >= WindowSnapshotRecords.QuietNanoseconds)
                {
                    var trigger = state.ChangeTrigger;
                    state.ChangedAt = null;
                    state.ChangeTrigger = null;
                    Snapshot(source, state, window, WindowSnapshotRecords.WindowChangedReason, trigger);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Counted and stated at stop; the thread goes on with the
                // next notice, from no window, so the next focus reads again.
                Interlocked.Increment(ref _failures);
                state.Window = null;
                state.ChangedAt = null;
                state.ChangeTrigger = null;
            }
        }
    }

    private void Handle(UiaWindowTreeSource source, ReadState state, UiaObservationNotice notice)
    {
        if (notice.Element is not { } element)
        {
            // Focus left NVDA: the next focus in it is a new entry.
            state.Window = null;
            state.ChangedAt = null;
            state.ChangeTrigger = null;
            return;
        }

        try
        {
            if (notice.EventType == "focus-changed")
            {
                var top = source.TopLevelOf(element);
                if (top is null)
                {
                    return;
                }

                if (state.Window is not { } window || !source.Same(top, window))
                {
                    state.Window = top;
                    state.ChangedAt = null;
                    state.ChangeTrigger = null;
                    Snapshot(source, state, top, WindowSnapshotRecords.FocusEnteredReason, notice);
                    return;
                }

                var focused = source.Read(source.Cached(element), null, 0, 1);
                if (WindowTreeWalk.Key(focused.RuntimeId) is not { } key)
                {
                    return;
                }

                if (!state.Elements.TryGetValue(key, out var prior))
                {
                    state.ChangedAt = null;
                    state.ChangeTrigger = null;
                    Snapshot(source, state, window, WindowSnapshotRecords.FocusedElementNewReason, notice);
                }
                else if (!WindowTreeWalk.SameState(prior, focused))
                {
                    state.ChangedAt = null;
                    state.ChangeTrigger = null;
                    Snapshot(source, state, window, WindowSnapshotRecords.FocusedElementChangedReason, notice);
                }

                return;
            }

            if (state.Window is not { } current)
            {
                return;
            }

            // A change in another of NVDA's windows waits for focus to go there.
            IUIAutomationElement? changedIn = null;
            try
            {
                changedIn = source.TopLevelOf(element);
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException)
            {
            }

            if (changedIn is not null && !source.Same(changedIn, current))
            {
                return;
            }

            state.ChangedAt = Now();
            state.ChangeTrigger ??= notice;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            // The element went before it could be read; the next event reads again.
        }
    }

    private void Snapshot(
        UiaWindowTreeSource source,
        ReadState state,
        IUIAutomationElement window,
        string reason,
        UiaObservationNotice? trigger)
    {
        var at = Now();
        IUIAutomationElement root;
        try
        {
            root = source.Cached(window);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            root = window;
        }

        var reading = WindowTreeWalk.Read(source, root, Now);
        state.Elements = reading.Elements
            .Select(item => (Key: WindowTreeWalk.Key(item.RuntimeId), Item: item))
            .Where(pair => pair.Key is not null)
            .GroupBy(pair => pair.Key!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Item, StringComparer.Ordinal);
        Emit(
            WindowSnapshotRecords.EventType,
            new
            {
                reason,
                trigger = trigger is null
                    ? null
                    : new
                    {
                        eventType = trigger.EventType,
                        eventId = trigger.EventId,
                        atNanoseconds = trigger.MonotonicNanoseconds
                    },
                processId = trigger?.ProcessId,
                elementCount = reading.Elements.Count,
                truncated = reading.Truncated,
                readNanoseconds = reading.ReadNanoseconds,
                maximumElements = WindowSnapshotRecords.MaximumElements,
                maximumReadNanoseconds = WindowSnapshotRecords.MaximumReadNanoseconds,
                elements = reading.Elements,
                problems = reading.Problems
            },
            at,
            reading.Truncated is null ? [] : ["snapshot-truncated"]);
    }

    private void Emit(string eventType, object payload, long monotonicNanoseconds, params string[] qualityFlags)
    {
        if (_context is not { } context)
        {
            return;
        }

        var sequence = unchecked((ulong)Interlocked.Increment(ref _eventSequence));
        context.EventSink.TryWrite(RecorderEventFactory.Create(
            context.SessionId,
            Descriptor,
            WindowSnapshotRecords.Channel,
            sequence,
            monotonicNanoseconds,
            eventType,
            payload,
            qualityFlags));
    }

    private void EmitLifecycle(string action, SessionBoundary boundary)
    {
        if (_context is not { } context)
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

    private sealed class ReadState
    {
        public IUIAutomationElement? Window { get; set; }

        public Dictionary<string, WindowSnapshotElement> Elements { get; set; } = new(StringComparer.Ordinal);

        public long? ChangedAt { get; set; }

        public UiaObservationNotice? ChangeTrigger { get; set; }
    }
}

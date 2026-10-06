using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using Recorder.Contracts;

namespace Recorder.Collectors.Automation;

// Records UI Automation events from the whole desktop through the native UI
// Automation client, IUIAutomation. The managed client,
// System.Windows.Automation, read each event's arguments in its own callback
// before any handler of the recorder, and ended the process when that read
// threw; see docs/architecture/uia-native-client.md.
public sealed class UiAutomationCollector : ICaptureCollector
{
    private const int ObservationCapacity = 4_096;

    // Slots only focus changes and automation events (invoke, selection, text
    // changed) may use, so a flood of property or structure changes from any
    // process cannot displace them.
    private const int ReservedObservationCapacity = 512;

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _stopRequested = new(false);
    private ReservedCapacityQueue<Observation>? _observations;
    private Thread? _subscriptionThread;
    private Task? _processorTask;
    private TaskCompletionSource<bool>? _ready;
    private CollectorInitializationContext? _context;
    private long _eventSequence = -1;
    private long _lifecycleSequence = -1;
    private long _handlerFaults;
    private bool _disposed;

    public UiAutomationCollector()
    {
        Descriptor = CollectorDescriptor.Create(
            "windows.ui-automation",
            nameof(UiAutomationCollector),
            typeof(UiAutomationCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            ["accessibility.uia.events"],
            "windows.ui-automation");
    }

    public CollectorDescriptor Descriptor { get; }
    public CollectorLifecycleState LifecycleState { get; private set; } = CollectorLifecycleState.Created;
    public CollectorHealthState HealthState { get; private set; } = CollectorHealthState.Unknown;

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

            LifecycleState = CollectorLifecycleState.Initializing;
            _context = context;
            _observations = new ReservedCapacityQueue<Observation>(
                ObservationCapacity,
                ReservedObservationCapacity,
                context.Clock.GetElapsedNanoseconds,
                episode => Observation.ForDropEpisode(episode));

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                LifecycleState = CollectorLifecycleState.Failed;
                HealthState = CollectorHealthState.Failed;
                return ValueTask.FromResult(new CapabilityResult(
                    CapabilityStatus.Incompatible,
                    Descriptor.Channels,
                    ["requires-windows-10-2004-or-later"],
                    false,
                    false));
            }

            LifecycleState = CollectorLifecycleState.Ready;
            HealthState = CollectorHealthState.Healthy;
            return ValueTask.FromResult(CapabilityResult.Supported(Descriptor.Channels.ToArray()));
        }
    }

    public async ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (LifecycleState != CollectorLifecycleState.Ready)
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot start from {LifecycleState}.");
            }

            LifecycleState = CollectorLifecycleState.Starting;
            _ready = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _processorTask = ProcessObservationsAsync();
            _subscriptionThread = new Thread(SubscriptionLoop)
            {
                IsBackground = true,
                Name = "UI Automation subscriptions"
            };
            _subscriptionThread.SetApartmentState(ApartmentState.MTA);
            _subscriptionThread.Start();
        }

        try
        {
            await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LifecycleState = CollectorLifecycleState.Failed;
            HealthState = CollectorHealthState.Failed;
            _observations?.Abandon();
            return CollectorTransitionResult.Reject(
                LifecycleState,
                "uia-start-failed",
                ex.Message);
        }

        LifecycleState = CollectorLifecycleState.Running;
        EmitLifecycle("started", boundary);
        return CollectorTransitionResult.Success(LifecycleState);
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

            if (LifecycleState is not (CollectorLifecycleState.Running or CollectorLifecycleState.Failed))
            {
                return CollectorTransitionResult.Reject(
                    LifecycleState,
                    "invalid-transition",
                    $"Cannot stop from {LifecycleState}.");
            }

            LifecycleState = CollectorLifecycleState.Stopping;
            _stopRequested.Set();
        }

        if (_subscriptionThread is not null)
        {
            await Task.Run(
                () => _subscriptionThread.Join(TimeSpan.FromSeconds(10)),
                cancellationToken).ConfigureAwait(false);
        }

        DropEpisode? unwrittenEpisode = null;
        if (_observations is not null)
        {
            unwrittenEpisode = await _observations.CompleteAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken).ConfigureAwait(false);
        }

        if (_processorTask is not null)
        {
            try
            {
                await _processorTask.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                HealthState = CollectorHealthState.Degraded;
                EmitEvent(
                    "collector-omission",
                    new { reason = "uia-provider-read-timeout" },
                    CollectorClosingTimestamp.Resolve(_context?.Clock, boundary),
                    "snapshot-processing-incomplete");
            }
        }

        // Every admitted observation arrived before the unwritten episode
        // began, so recording it after the processor keeps time order.
        if (unwrittenEpisode is not null)
        {
            EmitDropEpisode(unwrittenEpisode);
        }

        if (_observations?.TotalDropped > 0)
        {
            HealthState = CollectorHealthState.Degraded;
        }

        // Events whose handling failed in the recorder's handler were not
        // recorded; the count is stated once, after every recorded event.
        var handlerFaults = Interlocked.Read(ref _handlerFaults);
        if (handlerFaults > 0)
        {
            HealthState = CollectorHealthState.Degraded;
            EmitEvent(
                "collector-omission",
                new { reason = "uia-event-handler-failed", count = handlerFaults },
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

        if (LifecycleState is CollectorLifecycleState.Running or CollectorLifecycleState.Failed)
        {
            var context = _context;
            var now = context is null ? 0 : context.Clock.GetElapsedNanoseconds();
            await StopAsync(
                new SessionBoundary(now, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }

        _stopRequested.Dispose();
        _disposed = true;
        LifecycleState = CollectorLifecycleState.Disposed;
    }

    private void SubscriptionLoop()
    {
        IUIAutomation? automation = null;
        try
        {
            automation = new CUIAutomation8Class();
            var root = automation.GetRootElement();
            var cacheRequest = automation.CreateCacheRequest();
            cacheRequest.TreeScope = Interop.UIAutomationClient.TreeScope.TreeScope_Element;
            cacheRequest.AutomationElementMode = AutomationElementMode.AutomationElementMode_Full;
            foreach (var property in UiaEvidenceText.SnapshotPropertyIds)
            {
                cacheRequest.AddProperty(property);
            }

            // Each handler is given the request, so UI Automation reads these
            // properties of the sender when it raises the event.
            var handlers = new EventHandlers(this);
            automation.AddFocusChangedEventHandler(cacheRequest, handlers);
            foreach (var eventId in new[]
                     {
                         UiaEvidenceText.InvokedEventId,
                         UiaEvidenceText.ElementSelectedEventId,
                         UiaEvidenceText.TextChangedEventId
                     })
            {
                automation.AddAutomationEventHandler(
                    eventId,
                    root,
                    Interop.UIAutomationClient.TreeScope.TreeScope_Subtree,
                    cacheRequest,
                    handlers);
            }

            automation.AddStructureChangedEventHandler(
                root,
                Interop.UIAutomationClient.TreeScope.TreeScope_Subtree,
                cacheRequest,
                handlers);
            automation.AddPropertyChangedEventHandler(
                root,
                Interop.UIAutomationClient.TreeScope.TreeScope_Subtree,
                cacheRequest,
                handlers,
                UiaEvidenceText.ChangedPropertyIds.ToArray());

            _ready!.TrySetResult(true);
            _stopRequested.Wait();
        }
        catch (Exception ex)
        {
            _ready?.TrySetException(ex);
            LifecycleState = CollectorLifecycleState.Failed;
            HealthState = CollectorHealthState.Failed;
        }
        finally
        {
            RemoveHandlers(automation);
        }
    }

    // Removing every handler waits for handlers already running to return.
    private static void RemoveHandlers(IUIAutomation? automation)
    {
        if (automation is null)
        {
            return;
        }

        try
        {
            automation.RemoveAllEventHandlers();
        }
        catch (COMException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void CountHandlerFault() => Interlocked.Increment(ref _handlerFaults);

    private void Enqueue(
        IUIAutomationElement? element,
        string observationType,
        string eventId,
        string? changeType,
        int[]? runtimeId,
        string? newValue)
    {
        if (element is null || _observations is null)
        {
            return;
        }

        _observations.TryEnqueue(
            observationType,
            IsReservedObservationType(observationType),
            arrivedAt => new Observation(
                element,
                observationType,
                eventId,
                changeType,
                runtimeId,
                newValue,
                arrivedAt,
                null));
    }

    [ComVisible(true)]
    private sealed class EventHandlers(UiAutomationCollector collector) :
        IUIAutomationFocusChangedEventHandler,
        IUIAutomationEventHandler,
        IUIAutomationStructureChangedEventHandler,
        IUIAutomationPropertyChangedEventHandler
    {
        public void HandleFocusChangedEvent(IUIAutomationElement sender) =>
            UiaHandlerGuard.Run(
                () => collector.Enqueue(
                    sender,
                    "focus-changed",
                    UiaEvidenceText.EventName(UiaEvidenceText.FocusChangedEventId),
                    null,
                    null,
                    null),
                collector.CountHandlerFault);

        public void HandleAutomationEvent(IUIAutomationElement sender, int eventId) =>
            UiaHandlerGuard.Run(
                () => collector.Enqueue(
                    sender,
                    "automation-event",
                    UiaEvidenceText.EventName(eventId),
                    null,
                    null,
                    null),
                collector.CountHandlerFault);

        // A provider may raise a structure change without a runtime ID; it
        // arrives as a null array and is recorded as null.
        public void HandleStructureChangedEvent(
            IUIAutomationElement sender,
            StructureChangeType changeType,
            int[] runtimeId) =>
            UiaHandlerGuard.Run(
                () => collector.Enqueue(
                    sender,
                    "structure-changed",
                    UiaEvidenceText.EventName(UiaEvidenceText.StructureChangedEventId),
                    UiaEvidenceText.StructureChangeName((int)changeType),
                    runtimeId,
                    null),
                collector.CountHandlerFault);

        public void HandlePropertyChangedEvent(
            IUIAutomationElement sender,
            int propertyId,
            object newValue) =>
            UiaHandlerGuard.Run(
                () => collector.Enqueue(
                    sender,
                    "property-changed",
                    UiaEvidenceText.PropertyName(propertyId),
                    null,
                    null,
                    UiaEvidenceText.NormalizeValue(propertyId, newValue)),
                collector.CountHandlerFault);
    }

    private static bool IsReservedObservationType(string observationType) =>
        observationType is "focus-changed" or "automation-event";

    private async Task ProcessObservationsAsync()
    {
        await foreach (var observation in _observations!.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (observation.DropEpisode is { } episode)
            {
                EmitDropEpisode(episode);
                continue;
            }

            var snapshot = ReadElementSnapshot(observation.Element!);
            EmitEvent(
                observation.ObservationType,
                new
                {
                    eventId = observation.EventId,
                    changeType = observation.ChangeType,
                    runtimeId = observation.RuntimeId,
                    newValue = observation.NewValue,
                    element = snapshot
                },
                observation.MonotonicNanoseconds,
                snapshot.QualityFlags.ToArray());
        }
    }

    // One record per run of refused observations, timed at the last refusal.
    private void EmitDropEpisode(DropEpisode episode) =>
        EmitEvent(
            "collector-omission",
            new
            {
                reason = "uia-observation-queue-full",
                count = episode.Count,
                firstDroppedAtNanoseconds = episode.FirstDroppedAt,
                lastDroppedAtNanoseconds = episode.LastDroppedAt,
                droppedByObservationType = episode.CountsByKind
            },
            episode.LastDroppedAt,
            "evidence-dropped");

    private void EmitEvent(
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
            "accessibility.uia.events",
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

    // Reads the properties UI Automation cached when it raised the event. A
    // sender delivered without them is read now instead, which the snapshot
    // states, because its values may postdate the event.
    private static ElementSnapshot ReadElementSnapshot(IUIAutomationElement element)
    {
        try
        {
            return ReadSnapshot(
                propertyId => element.GetCachedPropertyValueEx(propertyId, 0),
                "event-cache",
                []);
        }
        catch (COMException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return ReadCurrentElementSnapshot(element);
    }

    private static ElementSnapshot ReadCurrentElementSnapshot(IUIAutomationElement element)
    {
        var qualityFlags = new List<string>();

        try
        {
            return ReadSnapshot(
                propertyId => element.GetCurrentPropertyValueEx(propertyId, 0),
                "current-read",
                qualityFlags);
        }
        catch (COMException ex)
        {
            qualityFlags.Add(UiaEvidenceText.ReadFailureFlag(ex.HResult));
        }
        catch (InvalidOperationException)
        {
            qualityFlags.Add("element-property-read-failed");
        }

        return new ElementSnapshot(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "current-read",
            qualityFlags);
    }

    private static ElementSnapshot ReadSnapshot(
        Func<int, object?> read,
        string propertySource,
        IReadOnlyList<string> qualityFlags)
    {
        var rectangle = UiaEvidenceText.Rectangle(read(UiaEvidenceText.BoundingRectanglePropertyId));
        return new ElementSnapshot(
            UiaEvidenceText.Integer(read(UiaEvidenceText.ProcessIdPropertyId)),
            UiaEvidenceText.Integer(read(UiaEvidenceText.NativeWindowHandlePropertyId)),
            UiaEvidenceText.Text(read(UiaEvidenceText.AutomationIdPropertyId)),
            UiaEvidenceText.Text(read(UiaEvidenceText.NamePropertyId)),
            UiaEvidenceText.Text(read(UiaEvidenceText.ClassNamePropertyId)),
            UiaEvidenceText.Text(read(UiaEvidenceText.FrameworkIdPropertyId)),
            UiaEvidenceText.ControlTypeName(read(UiaEvidenceText.ControlTypePropertyId)),
            UiaEvidenceText.Text(read(UiaEvidenceText.LocalizedControlTypePropertyId)),
            UiaEvidenceText.Boolean(read(UiaEvidenceText.HasKeyboardFocusPropertyId)),
            UiaEvidenceText.Boolean(read(UiaEvidenceText.IsKeyboardFocusablePropertyId)),
            UiaEvidenceText.Boolean(read(UiaEvidenceText.IsEnabledPropertyId)),
            UiaEvidenceText.Boolean(read(UiaEvidenceText.IsOffscreenPropertyId)),
            rectangle is { } box
                ? new RectangleSnapshot(box.X, box.Y, box.Width, box.Height)
                : null,
            propertySource,
            qualityFlags);
    }

    private sealed record Observation(
        IUIAutomationElement? Element,
        string ObservationType,
        string EventId,
        string? ChangeType,
        int[]? RuntimeId,
        string? NewValue,
        long MonotonicNanoseconds,
        DropEpisode? DropEpisode)
    {
        public static Observation ForDropEpisode(DropEpisode episode) =>
            new(null, "collector-omission", "", null, null, null, episode.LastDroppedAt, episode);
    }

    private sealed record RectangleSnapshot(
        double X,
        double Y,
        double Width,
        double Height);

    private sealed record ElementSnapshot(
        int? ProcessId,
        int? NativeWindowHandle,
        string? AutomationId,
        string? Name,
        string? ClassName,
        string? FrameworkId,
        string? ControlType,
        string? LocalizedControlType,
        bool? HasKeyboardFocus,
        bool? IsKeyboardFocusable,
        bool? IsEnabled,
        bool? IsOffscreen,
        RectangleSnapshot? BoundingRectangle,
        string PropertySource,
        IReadOnlyList<string> QualityFlags);
}

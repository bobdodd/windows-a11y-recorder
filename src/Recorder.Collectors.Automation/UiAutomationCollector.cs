using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using Recorder.Contracts;
using UiaAutomation = System.Windows.Automation.Automation;

namespace Recorder.Collectors.Automation;

public sealed class UiAutomationCollector : ICaptureCollector
{
    private const int ObservationCapacity = 4_096;

    // Slots only focus changes and automation events (invoke, selection, text
    // changed) may use, so a flood of property or structure changes from any
    // process cannot displace them.
    private const int ReservedObservationCapacity = 512;

    // The element properties every observation records. They are requested
    // with each event, so UI Automation reads them when it raises the event
    // instead of the processor reading them afterwards, one call each.
    private static readonly AutomationProperty[] SnapshotProperties =
    [
        AutomationElement.ProcessIdProperty,
        AutomationElement.NativeWindowHandleProperty,
        AutomationElement.AutomationIdProperty,
        AutomationElement.NameProperty,
        AutomationElement.ClassNameProperty,
        AutomationElement.FrameworkIdProperty,
        AutomationElement.ControlTypeProperty,
        AutomationElement.LocalizedControlTypeProperty,
        AutomationElement.HasKeyboardFocusProperty,
        AutomationElement.IsKeyboardFocusableProperty,
        AutomationElement.IsEnabledProperty,
        AutomationElement.IsOffscreenProperty,
        AutomationElement.BoundingRectangleProperty
    ];

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _stopRequested = new(false);
    private ReservedCapacityQueue<Observation>? _observations;
    private Thread? _subscriptionThread;
    private Task? _processorTask;
    private TaskCompletionSource<bool>? _ready;
    private CollectorInitializationContext? _context;
    private long _eventSequence = -1;
    private long _lifecycleSequence = -1;
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
        AutomationFocusChangedEventHandler focusHandler = OnFocusChanged;
        AutomationEventHandler automationHandler = OnAutomationEvent;
        StructureChangedEventHandler structureHandler = OnStructureChanged;
        AutomationPropertyChangedEventHandler propertyHandler = OnPropertyChanged;

        try
        {
            var root = AutomationElement.RootElement;
            var cacheRequest = new CacheRequest
            {
                TreeScope = TreeScope.Element,
                AutomationElementMode = AutomationElementMode.Full
            };
            foreach (var property in SnapshotProperties)
            {
                cacheRequest.Add(property);
            }

            // Handlers added while the request is active receive each event's
            // sender with these properties cached.
            using var activeCacheRequest = cacheRequest.Activate();
            UiaAutomation.AddAutomationFocusChangedEventHandler(focusHandler);
            UiaAutomation.AddAutomationEventHandler(
                InvokePattern.InvokedEvent,
                root,
                TreeScope.Subtree,
                automationHandler);
            UiaAutomation.AddAutomationEventHandler(
                SelectionItemPattern.ElementSelectedEvent,
                root,
                TreeScope.Subtree,
                automationHandler);
            UiaAutomation.AddAutomationEventHandler(
                TextPattern.TextChangedEvent,
                root,
                TreeScope.Subtree,
                automationHandler);
            UiaAutomation.AddStructureChangedEventHandler(
                root,
                TreeScope.Subtree,
                structureHandler);
            UiaAutomation.AddAutomationPropertyChangedEventHandler(
                root,
                TreeScope.Subtree,
                propertyHandler,
                AutomationElement.NameProperty,
                AutomationElement.HasKeyboardFocusProperty,
                AutomationElement.IsEnabledProperty,
                ValuePattern.ValueProperty,
                TogglePattern.ToggleStateProperty,
                ExpandCollapsePattern.ExpandCollapseStateProperty);

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
            RemoveHandlers(focusHandler, automationHandler, structureHandler, propertyHandler);
        }
    }

    private static void RemoveHandlers(
        AutomationFocusChangedEventHandler focusHandler,
        AutomationEventHandler automationHandler,
        StructureChangedEventHandler structureHandler,
        AutomationPropertyChangedEventHandler propertyHandler)
    {
        try
        {
            var root = AutomationElement.RootElement;
            UiaAutomation.RemoveAutomationFocusChangedEventHandler(focusHandler);
            UiaAutomation.RemoveAutomationEventHandler(
                InvokePattern.InvokedEvent,
                root,
                automationHandler);
            UiaAutomation.RemoveAutomationEventHandler(
                SelectionItemPattern.ElementSelectedEvent,
                root,
                automationHandler);
            UiaAutomation.RemoveAutomationEventHandler(
                TextPattern.TextChangedEvent,
                root,
                automationHandler);
            UiaAutomation.RemoveStructureChangedEventHandler(root, structureHandler);
            UiaAutomation.RemoveAutomationPropertyChangedEventHandler(
                root,
                propertyHandler);
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs eventArgs)
    {
        Enqueue(
            sender,
            "focus-changed",
            eventArgs.EventId.ProgrammaticName,
            null,
            null,
            null);
    }

    private void OnAutomationEvent(object sender, AutomationEventArgs eventArgs)
    {
        Enqueue(
            sender,
            "automation-event",
            eventArgs.EventId.ProgrammaticName,
            null,
            null,
            null);
    }

    private void OnStructureChanged(object sender, StructureChangedEventArgs eventArgs)
    {
        Enqueue(
            sender,
            "structure-changed",
            AutomationElementIdentifiers.StructureChangedEvent.ProgrammaticName,
            eventArgs.StructureChangeType.ToString(),
            eventArgs.GetRuntimeId(),
            null);
    }

    private void OnPropertyChanged(object sender, AutomationPropertyChangedEventArgs eventArgs)
    {
        Enqueue(
            sender,
            "property-changed",
            eventArgs.Property.ProgrammaticName,
            null,
            null,
            NormalizeValue(eventArgs.NewValue));
    }

    private void Enqueue(
        object sender,
        string observationType,
        string eventId,
        string? changeType,
        int[]? runtimeId,
        string? newValue)
    {
        if (sender is not AutomationElement element || _observations is null)
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
    private static ElementSnapshot ReadElementSnapshot(AutomationElement element)
    {
        try
        {
            var cached = element.Cached;
            return new ElementSnapshot(
                cached.ProcessId,
                cached.NativeWindowHandle,
                cached.AutomationId,
                cached.Name,
                cached.ClassName,
                cached.FrameworkId,
                cached.ControlType?.ProgrammaticName,
                cached.LocalizedControlType,
                cached.HasKeyboardFocus,
                cached.IsKeyboardFocusable,
                cached.IsEnabled,
                cached.IsOffscreen,
                ToRectangle(cached.BoundingRectangle),
                "event-cache",
                []);
        }
        // ElementNotAvailableException is neither of the other two types; the
        // current read below records that the element had gone.
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (COMException)
        {
        }

        return ReadCurrentElementSnapshot(element);
    }

    private static ElementSnapshot ReadCurrentElementSnapshot(AutomationElement element)
    {
        var qualityFlags = new List<string>();

        try
        {
            var current = element.Current;
            var rectangle = current.BoundingRectangle;
            return new ElementSnapshot(
                current.ProcessId,
                current.NativeWindowHandle,
                current.AutomationId,
                current.Name,
                current.ClassName,
                current.FrameworkId,
                current.ControlType?.ProgrammaticName,
                current.LocalizedControlType,
                current.HasKeyboardFocus,
                current.IsKeyboardFocusable,
                current.IsEnabled,
                current.IsOffscreen,
                ToRectangle(rectangle),
                "current-read",
                qualityFlags);
        }
        catch (ElementNotAvailableException)
        {
            qualityFlags.Add("element-not-available");
        }
        catch (InvalidOperationException)
        {
            qualityFlags.Add("element-property-read-failed");
        }
        catch (COMException)
        {
            qualityFlags.Add("uia-provider-error");
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

    private static string? NormalizeValue(object? value) =>
        value switch
        {
            null => null,
            AutomationIdentifier identifier => identifier.ProgrammaticName,
            bool boolean => boolean ? "true" : "false",
            _ => value.ToString()
        };

    private static RectangleSnapshot? ToRectangle(Rect rectangle) =>
        rectangle.IsEmpty
            ? null
            : new RectangleSnapshot(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    private sealed record Observation(
        AutomationElement? Element,
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

using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Automation;
using Recorder.Contracts;
using UiaAutomation = System.Windows.Automation.Automation;

namespace Recorder.Collectors.Automation;

public sealed class UiAutomationCollector : ICaptureCollector
{
    private const int ObservationCapacity = 4_096;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _stopRequested = new(false);
    private readonly Channel<Observation> _observations;
    private Thread? _subscriptionThread;
    private Task? _processorTask;
    private TaskCompletionSource<bool>? _ready;
    private CollectorInitializationContext? _context;
    private long _eventSequence = -1;
    private long _lifecycleSequence = -1;
    private long _observationsDropped;
    private bool _disposed;

    public UiAutomationCollector()
    {
        Descriptor = CollectorDescriptor.Create(
            "windows.ui-automation",
            nameof(UiAutomationCollector),
            typeof(UiAutomationCollector).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            ["accessibility.uia.events"],
            "windows.ui-automation");
        _observations = Channel.CreateBounded<Observation>(
            new BoundedChannelOptions(ObservationCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
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
            _observations.Writer.TryComplete();
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

        _observations.Writer.TryComplete();
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
                    boundary.MonotonicNanoseconds,
                    "snapshot-processing-incomplete");
            }
        }

        if (Interlocked.Read(ref _observationsDropped) > 0)
        {
            HealthState = CollectorHealthState.Degraded;
            EmitEvent(
                "collector-omission",
                new
                {
                    reason = "uia-observation-queue-full",
                    count = Interlocked.Read(ref _observationsDropped)
                },
                boundary.MonotonicNanoseconds,
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
        AutomationFocusChangedEventHandler focusHandler = OnFocusChanged;
        AutomationEventHandler automationHandler = OnAutomationEvent;
        StructureChangedEventHandler structureHandler = OnStructureChanged;
        AutomationPropertyChangedEventHandler propertyHandler = OnPropertyChanged;

        try
        {
            var root = AutomationElement.RootElement;
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
        if (sender is not AutomationElement element || _context is null)
        {
            return;
        }

        var observation = new Observation(
            element,
            observationType,
            eventId,
            changeType,
            runtimeId,
            newValue,
            _context.Clock.GetElapsedNanoseconds());

        if (!_observations.Writer.TryWrite(observation))
        {
            Interlocked.Increment(ref _observationsDropped);
        }
    }

    private async Task ProcessObservationsAsync()
    {
        await foreach (var observation in _observations.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var snapshot = ReadElementSnapshot(observation.Element);
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

    private static ElementSnapshot ReadElementSnapshot(AutomationElement element)
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
        AutomationElement Element,
        string ObservationType,
        string EventId,
        string? ChangeType,
        int[]? RuntimeId,
        string? NewValue,
        long MonotonicNanoseconds);

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
        IReadOnlyList<string> QualityFlags);
}

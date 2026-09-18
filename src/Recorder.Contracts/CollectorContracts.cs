using System.Collections.ObjectModel;

namespace Recorder.Contracts;

public enum CollectorLifecycleState
{
    Created,
    Initializing,
    Ready,
    Starting,
    Running,
    Pausing,
    Paused,
    Resuming,
    Stopping,
    Stopped,
    Failed,
    Disposed
}

public enum CollectorHealthState
{
    Healthy,
    Degraded,
    Failed,
    Unknown
}

public enum CapabilityStatus
{
    Supported,
    SupportedWithLimitations,
    Unavailable,
    DeniedByPolicy,
    PermissionRequired,
    Incompatible
}

public sealed record CollectorDescriptor(
    string CollectorType,
    string InstanceId,
    string Implementation,
    string ImplementationVersion,
    string ContractVersion,
    IReadOnlyList<string> Channels,
    string CaptureMethod)
{
    public static CollectorDescriptor Create(
        string collectorType,
        string implementation,
        string implementationVersion,
        IEnumerable<string> channels,
        string captureMethod) =>
        new(
            collectorType,
            Guid.NewGuid().ToString("N"),
            implementation,
            implementationVersion,
            "1.0",
            new ReadOnlyCollection<string>(channels.ToArray()),
            captureMethod);
}

public sealed record CapabilityResult(
    CapabilityStatus Status,
    IReadOnlyList<string> Channels,
    IReadOnlyList<string> Limitations,
    bool FallbackAvailable,
    bool BlocksSessionStart)
{
    public static CapabilityResult Supported(params string[] channels) =>
        new(CapabilityStatus.Supported, channels, Array.Empty<string>(), false, false);
}

public sealed record CollectorTransitionResult(
    bool Accepted,
    CollectorLifecycleState State,
    string? ErrorCode = null,
    string? Message = null)
{
    public static CollectorTransitionResult Success(CollectorLifecycleState state) =>
        new(true, state);

    public static CollectorTransitionResult Reject(
        CollectorLifecycleState state,
        string code,
        string message) =>
        new(false, state, code, message);
}

public sealed record CollectorInitializationContext(
    string SessionId,
    string SessionDirectory,
    ISessionClock Clock,
    IRecorderEventSink EventSink);

public sealed record SessionBoundary(long MonotonicNanoseconds, DateTimeOffset Utc);

public interface ISessionClock
{
    long Frequency { get; }
    long OriginTimestamp { get; }
    DateTimeOffset OriginUtc { get; }
    long GetTimestamp();
    long GetElapsedNanoseconds();
}

public interface IRecorderEventSink
{
    bool TryWrite(RecorderEvent record);
}

public interface ICaptureCollector : IAsyncDisposable
{
    CollectorDescriptor Descriptor { get; }
    CollectorLifecycleState LifecycleState { get; }
    CollectorHealthState HealthState { get; }

    ValueTask<CapabilityResult> InitializeAsync(
        CollectorInitializationContext context,
        CancellationToken cancellationToken);

    ValueTask<CollectorTransitionResult> StartAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken);

    ValueTask<CollectorTransitionResult> StopAsync(
        SessionBoundary boundary,
        CancellationToken cancellationToken);
}

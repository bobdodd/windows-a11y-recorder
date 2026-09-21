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

/// <summary>
/// Resolves the monotonic timestamp a collector stamps on the records it emits
/// while it is closing, after its in-flight evidence has been drained.
/// </summary>
public static class CollectorClosingTimestamp
{
    /// <summary>
    /// Returns the later of the session stop boundary and the clock read at the
    /// moment of the call.
    /// </summary>
    /// <remarks>
    /// A stop boundary is captured before a collector drains the evidence it
    /// has already queued, so records the collector emits afterwards, such as
    /// an omission that reports dropped evidence, would carry a timestamp
    /// earlier than evidence they are written after. That regresses monotonic
    /// order within the collector's channel, which the archive validator
    /// rejects. Reading the clock at emission time is an observation of when
    /// the closing record was produced rather than an adjustment of a recorded
    /// one, and the boundary is retained as a floor for a collector whose clock
    /// is unavailable or has not advanced.
    /// </remarks>
    public static long Resolve(ISessionClock? clock, SessionBoundary boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);

        if (clock is null)
        {
            return boundary.MonotonicNanoseconds;
        }

        return Math.Max(clock.GetElapsedNanoseconds(), boundary.MonotonicNanoseconds);
    }
}

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

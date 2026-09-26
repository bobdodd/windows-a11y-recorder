using Recorder.Contracts;

namespace Recorder.Coordinator;

public sealed record RecordingOptions
{
    public required string OutputRoot { get; init; }
    public bool CaptureKeyboardAndMouse { get; init; } = true;
    public bool CaptureUiAutomation { get; init; } = true;
    public bool CaptureForegroundWindow { get; init; } = true;
    public bool CaptureDesktopFrames { get; init; } = true;
    public int FramesPerSecond { get; init; } = 5;
    public bool CaptureMicrophone { get; init; }
    public bool CaptureSystemAudio { get; init; }
    public bool CaptureBrowserEvidence { get; init; }
    public string? ChromiumExecutablePath { get; init; }
    public string? BrowserStartUrl { get; init; }
    public int? BrowserRemoteDebuggingPort { get; init; }
}

public enum RecordingSessionState
{
    Idle,
    Starting,
    Recording,
    Stopping,
    Completed,
    Failed
}

public sealed record RecordingSessionStatus(
    RecordingSessionState State,
    string? SessionId,
    string? SessionDirectory,
    DateTimeOffset? StartedUtc,
    TimeSpan Elapsed,
    long AcceptedEvents,
    long DroppedEvents,
    IReadOnlyList<CollectorStatus> Collectors,
    string? Message,
    RecordingDatabaseStatus? Database = null);

/// <summary>
/// The recording's events in the database, while they are also written to
/// the event log. Null when the recording is not written to a database.
/// </summary>
/// <param name="Unavailable">True while the most recent write failed.</param>
/// <param name="Unwritten">
/// Accepted events not written when writing finished. Zero while recording.
/// </param>
/// <param name="Problem">
/// Why the database recording is incomplete, or null when it is not.
/// </param>
public sealed record RecordingDatabaseStatus(
    long Accepted,
    long Written,
    long Rejected,
    long Dropped,
    long Unwritten,
    bool Unavailable,
    string? Problem);

public sealed record CollectorStatus(
    string CollectorType,
    CollectorLifecycleState Lifecycle,
    CollectorHealthState Health,
    CapabilityStatus? Capability,
    IReadOnlyList<string> Limitations,
    string? HealthReason = null);

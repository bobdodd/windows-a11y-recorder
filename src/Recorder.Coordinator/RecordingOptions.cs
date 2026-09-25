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
    string? Message);

public sealed record CollectorStatus(
    string CollectorType,
    CollectorLifecycleState Lifecycle,
    CollectorHealthState Health,
    CapabilityStatus? Capability,
    IReadOnlyList<string> Limitations,
    string? HealthReason = null);

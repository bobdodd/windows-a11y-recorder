namespace Recorder.Contracts;

/// <summary>
/// An assistive technology the <c>system.assistive-technology</c> channel
/// watches for: its product name, its role, the executable names of its
/// main process, and the modules it loads into other processes, all
/// lowercase. See docs/architecture/screen-reader-activity.md, "Tracking
/// NVDA".
/// </summary>
public sealed record KnownAssistiveTechnology(
    string Product,
    string Role,
    IReadOnlyList<string> Executables,
    IReadOnlyList<string> Modules);

/// <summary>
/// The records of the <c>system.assistive-technology</c> channel, written by
/// the assistive technology collector: what it watches, each watched
/// process's start and exit, each of its modules seen in or gone from the
/// instrumented Chromium's processes, and each period of sound from its
/// audio. Agreed with the owner on 2026-10-10; see
/// docs/architecture/screen-reader-activity.md, "Tracking NVDA".
/// </summary>
public static class AssistiveTechnologyRecords
{
    public const string Channel = "system.assistive-technology";

    public const string WatchEventType = "assistive-technology-watch";
    public const string ProcessStartedEventType = "assistive-technology-process-started";
    public const string ProcessExitedEventType = "assistive-technology-process-exited";
    public const string ModuleLoadedEventType = "assistive-technology-module-loaded";
    public const string ModuleUnloadedEventType = "assistive-technology-module-unloaded";
    public const string SoundStartedEventType = "assistive-technology-sound-started";
    public const string SoundEndedEventType = "assistive-technology-sound-ended";

    public static IReadOnlyList<string> EventTypes { get; } =
    [
        WatchEventType, ProcessStartedEventType, ProcessExitedEventType, ModuleLoadedEventType,
        ModuleUnloadedEventType, SoundStartedEventType, SoundEndedEventType
    ];

    /// <summary>A watched product's main process.</summary>
    public const string ScreenReaderRole = "screen-reader";

    /// <summary>
    /// A process a main process started from its own folder, such as NVDA's
    /// <c>nvda_slave.exe</c>.
    /// </summary>
    public const string HelperRole = "helper";

    public static IReadOnlyList<string> Roles { get; } = [ScreenReaderRole, HelperRole];

    /// <summary>A main process, matched by its executable's name.</summary>
    public const string KnownExecutableBasis = "known-executable";

    /// <summary>A helper: a child of a main process, from that process's folder.</summary>
    public const string ChildInFolderBasis = "child-in-folder";

    public static IReadOnlyList<string> Bases { get; } = [KnownExecutableBasis, ChildInFolderBasis];

    /// <summary>
    /// Whether the copy is the installed one, as NVDA decides it
    /// (<c>config.isInstalledCopy</c>): the <c>UninstallDirectory</c> value
    /// of <c>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NVDA</c>
    /// names its folder. Any other copy is portable, and a copy whose folder
    /// could not be read is unknown.
    /// </summary>
    public static IReadOnlyList<string> Copies { get; } = ["installed", "portable", "unknown"];

    /// <summary>What ended a period of sound.</summary>
    public static IReadOnlyList<string> SoundEndings { get; } = ["silence", "process-exited", "stop"];

    /// <summary>NVDA, the first product watched.</summary>
    public static KnownAssistiveTechnology Nvda { get; } = new(
        "NVDA",
        ScreenReaderRole,
        ["nvda.exe"],
        // The module Chromium matches to NVDA in DiscoverAssistiveTech
        // (content/browser/accessibility/browser_accessibility_state_impl_win.cc).
        ["nvdahelperremote.dll"]);

    public static IReadOnlyList<KnownAssistiveTechnology> Known { get; } = [Nvda];

    // How often the collector looks, and when sound counts as sound. A
    // sound period ends when the peak stays below the threshold for the gap,
    // so the pauses between words do not split one utterance.
    public const int ProcessPollMilliseconds = 250;
    public const int ModulePollMilliseconds = 1000;
    public const int SoundSampleMilliseconds = 20;
    public const double SoundThreshold = 0.001;
    public const int SoundGapMilliseconds = 250;

    /// <summary>The known product whose main executable has this file name, or null.</summary>
    public static KnownAssistiveTechnology? ByExecutable(string fileName) =>
        Known.FirstOrDefault(product =>
            product.Executables.Contains(fileName.ToLowerInvariant(), StringComparer.Ordinal));

    /// <summary>The known product that loads a module of this file name, or null.</summary>
    public static KnownAssistiveTechnology? ByModule(string fileName) =>
        Known.FirstOrDefault(product =>
            product.Modules.Contains(fileName.ToLowerInvariant(), StringComparer.Ordinal));

    /// <summary>
    /// Whether a child process's executable is in the folder of the main
    /// process's executable, or below it, which makes it a helper. Paths are
    /// Windows paths, compared without regard to case.
    /// </summary>
    public static bool IsInFolderOf(string childPath, string mainPath) =>
        FolderOf(mainPath) is { } folder &&
        childPath.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a copy is the installed one: <paramref name="installedFolder"/>
    /// is the uninstall key's folder, or null when there is none.
    /// </summary>
    public static string CopyOf(string executablePath, string? installedFolder)
    {
        if (FolderOf(executablePath) is not { } folder)
        {
            return "unknown";
        }

        return installedFolder is not null &&
            string.Equals(folder, installedFolder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            ? "installed"
            : "portable";
    }

    // The folder of a Windows path, without its last separator, or null.
    private static string? FolderOf(string path)
    {
        var last = path.LastIndexOfAny(['\\', '/']);
        return last > 0 ? path[..last].TrimEnd('\\', '/') : null;
    }
}

/// <summary>A sound period's start or end, as <see cref="SoundPeriodTracker"/> finds it.</summary>
public sealed record SoundPeriodChange(
    bool Started,
    long At,
    double Peak,
    long? StartedAt = null,
    long? LastSoundAt = null,
    string? EndedBy = null);

/// <summary>
/// The periods of sound of one process's audio, from peak meter samples: a
/// period starts with the first sample at or above the threshold, and ends
/// when no sample has reached it for the gap, or when the process exits or
/// the recording stops. Used by one thread.
/// </summary>
public sealed class SoundPeriodTracker(double threshold, long gapNanoseconds)
{
    private long? _startedAt;
    private long _lastSoundAt;
    private double _maxPeak;

    public bool Sounding => _startedAt is not null;

    /// <summary>Takes a sample, and returns a period's start or end, or null.</summary>
    public SoundPeriodChange? Observe(long at, double peak)
    {
        if (peak >= threshold)
        {
            _lastSoundAt = at;
            if (_startedAt is null)
            {
                _startedAt = at;
                _maxPeak = peak;
                return new SoundPeriodChange(true, at, peak);
            }

            _maxPeak = Math.Max(_maxPeak, peak);
            return null;
        }

        return _startedAt is not null && at - _lastSoundAt >= gapNanoseconds
            ? End(at, "silence")
            : null;
    }

    /// <summary>Ends a period in progress, or returns null with none.</summary>
    public SoundPeriodChange? End(long at, string endedBy)
    {
        if (_startedAt is not { } startedAt)
        {
            return null;
        }

        _startedAt = null;
        return new SoundPeriodChange(false, at, _maxPeak, startedAt, _lastSoundAt, endedBy);
    }
}

namespace Recorder.Contracts;

/// <summary>
/// The window snapshot record of the <c>accessibility.uia.events</c> channel:
/// the whole UI Automation tree of a window of NVDA's main process, read
/// when focus enters it, after it changes, and when focus moves to an
/// element of it that differs from the last snapshot, so a report can say
/// what NVDA was showing at any time. Agreed with the owner on 2026-10-11
/// (limits of 5,000 elements and 2 seconds, NVDA's windows only); see
/// docs/architecture/screen-reader-activity.md, "2c-4, what NVDA's windows
/// show".
/// </summary>
public static class WindowSnapshotRecords
{
    public const string Channel = "accessibility.uia.events";

    public const string EventType = "window-snapshot";

    /// <summary>The collector type of the snapshot collector.</summary>
    public const string CollectorType = "windows.ui-automation.nvda-window-snapshots";

    /// <summary>The most elements one snapshot reads.</summary>
    public const int MaximumElements = 5_000;

    /// <summary>The longest one snapshot reads for, in nanoseconds: 2 seconds.</summary>
    public const long MaximumReadNanoseconds = 2_000_000_000;

    /// <summary>
    /// How long a window must be quiet after a change, in nanoseconds, before
    /// it is read again, so one burst of changes gives one snapshot: 300 ms.
    /// </summary>
    public const long QuietNanoseconds = 300_000_000;

    /// <summary>Focus entered a window of NVDA's that was not the one last read.</summary>
    public const string FocusEnteredReason = "focus-entered-window";

    /// <summary>The window's structure, selection, text, or a watched property changed.</summary>
    public const string WindowChangedReason = "window-changed";

    /// <summary>Focus moved to an element the last snapshot did not hold.</summary>
    public const string FocusedElementNewReason = "focused-element-not-in-snapshot";

    /// <summary>Focus moved to an element whose state differs from the last snapshot's.</summary>
    public const string FocusedElementChangedReason = "focused-element-changed";

    public static IReadOnlyList<string> Reasons { get; } =
        [FocusEnteredReason, WindowChangedReason, FocusedElementNewReason, FocusedElementChangedReason];

    /// <summary>The snapshot stopped at <see cref="MaximumElements"/>.</summary>
    public const string ElementLimit = "element-limit";

    /// <summary>The snapshot stopped at <see cref="MaximumReadNanoseconds"/>.</summary>
    public const string TimeLimit = "time-limit";

    public static IReadOnlyList<string> Truncations { get; } = [ElementLimit, TimeLimit];

    /// <summary>The omission reason for notices refused while the snapshot queue was full.</summary>
    public const string NoticesDroppedReason = "nvda-window-snapshot-notices-dropped";

    /// <summary>The omission reason for notices whose handling failed, with their count.</summary>
    public const string HandlingFailedReason = "nvda-window-snapshot-handling-failed";

    /// <summary>The omission reason for a snapshot reader that could not start.</summary>
    public const string ReaderFailedReason = "nvda-window-snapshot-reader-failed";
}

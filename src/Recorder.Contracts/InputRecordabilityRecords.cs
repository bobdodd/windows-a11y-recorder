namespace Recorder.Contracts;

/// <summary>
/// The records, on <c>window.foreground</c>, of when keyboard and mouse
/// input could not be recorded: Windows gives input to a window of a
/// process at a higher integrity level than the recorder's to neither its
/// low-level hook nor its raw input, and none on a desktop other than the
/// user's, such as the secure desktop of a permission prompt. Found on the
/// target machine on 2026-10-10
/// (docs/validation/keyboard-hook-2026-10-10.md, "The elevated window").
/// The recorder records only these privilege facts; it takes no input or
/// content from an elevated window. Agreed with the owner on 2026-10-10:
/// docs/architecture/screen-reader-activity.md, "Input the recorder cannot
/// receive".
/// </summary>
public static class InputRecordabilityRecords
{
    public const string Channel = "window.foreground";

    /// <summary>The recorder's own integrity level, once at the start.</summary>
    public const string RecorderIntegrityEventType = "recorder-integrity";

    /// <summary>
    /// The integrity level of the foreground window's process, written with
    /// each <c>foreground-window</c> record, and whether input to it can be
    /// recorded.
    /// </summary>
    public const string ForegroundIntegrityEventType = "foreground-integrity";

    /// <summary>The desktop that receives input, at the start and at each switch.</summary>
    public const string InputDesktopEventType = "input-desktop";

    public static IReadOnlyList<string> EventTypes { get; } =
        [RecorderIntegrityEventType, ForegroundIntegrityEventType, InputDesktopEventType];

    /// <summary>The name of the user's own desktop.</summary>
    public const string DefaultDesktop = "Default";

    public const string StartReason = "start";
    public const string SwitchReason = "switch";
    public const string PollReason = "poll";

    public static IReadOnlyList<string> DesktopReasons { get; } = [StartReason, SwitchReason, PollReason];

    /// <summary>How often the input desktop is checked besides Windows' switch event.</summary>
    public const int DesktopPollMilliseconds = 250;

    public static IReadOnlyList<string> IntegrityLevels { get; } =
        ["untrusted", "low", "medium", "medium-plus", "high", "system", "protected"];

    /// <summary>
    /// The name of an integrity level from its relative identifier
    /// (https://learn.microsoft.com/en-us/windows/win32/secauthz/well-known-sids).
    /// </summary>
    public static string IntegrityLevel(int rid) => rid switch
    {
        < 0x1000 => "untrusted",
        < 0x2000 => "low",
        < 0x2100 => "medium",
        < 0x3000 => "medium-plus",
        < 0x4000 => "high",
        < 0x5000 => "system",
        _ => "protected"
    };

    /// <summary>
    /// Whether input to a window of a process at an integrity level can be
    /// recorded by a recorder at its own: not when the window's level is
    /// higher. Null when the window's level is not known.
    /// </summary>
    public static bool? InputRecordable(int? windowRid, int recorderRid) =>
        windowRid is { } rid ? rid <= recorderRid : null;
}

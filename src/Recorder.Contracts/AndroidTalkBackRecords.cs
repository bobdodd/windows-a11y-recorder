namespace Recorder.Contracts;

/// <summary>
/// The records of the TalkBack collector, slice 6 of
/// docs/architecture/android-implementation-plan.md: every line TalkBack
/// wrote to the phone's log while recording, as <c>logcat -v monotonic</c>
/// printed it, and what TalkBack said, read from those lines by a versioned
/// reader.
///
/// <c>logcat -v monotonic -v usec</c> prints each line's time as seconds
/// and microseconds of CLOCK_MONOTONIC, converted from the wall clock time
/// the log stamped it with (logprint.cpp, convertMonotonic). That is the
/// clock the companion app samples, so each record is placed on the session
/// clock through the companion collector's clock fit, and otherwise at the
/// time the recorder read it.
/// The lines are TalkBack's own log, not a published interface, and their
/// wording can change with any TalkBack version; the raw lines are kept so
/// that a later reader can be run again on them.
/// </summary>
public static class AndroidTalkBackRecords
{
    /// <summary>Every TalkBack line as received, and why the lines are missing from part or all of a recording.</summary>
    public const string LogChannel = "screen-reader.talkback.log";

    /// <summary>What TalkBack said, read from its lines.</summary>
    public const string SpeechChannel = "screen-reader.talkback.speech";

    public const string LineEventType = "talkback-log-line";

    /// <summary>Why TalkBack's lines are missing from part or all of a recording.</summary>
    public const string UnavailableEventType = "talkback-log-unavailable";

    /// <summary>The first fragment TalkBack spoke of an utterance.</summary>
    public const string UtteranceStartedEventType = "utterance-started";

    /// <summary>A later fragment of an utterance that has already started.</summary>
    public const string FragmentStartedEventType = "fragment-started";

    /// <summary>A word the speech engine reached.</summary>
    public const string WordReachedEventType = "word-reached";

    /// <summary>An utterance TalkBack cut off, as when the next swipe interrupts it.</summary>
    public const string UtteranceInterruptedEventType = "utterance-interrupted";

    /// <summary>The record's time is the phone time logcat printed for the line.</summary>
    public const string PhoneLogTimeFlag = "phone-log-time";

    /// <summary>
    /// The version of the reader that made a speech record from the lines.
    /// A reader that reads the lines differently has a new version.
    /// </summary>
    public const int ReaderVersion = 1;

    /// <summary>The log levels logcat prints (logprint.cpp, filterPriToChar).</summary>
    public static IReadOnlyList<string> Levels { get; } = ["V", "D", "I", "W", "E", "F", "S"];
}

/// <summary>
/// One line as logcat printed it, with the parts read
/// from it. The parts are null for a line logcat prints that is not a log
/// entry, such as "--------- beginning of main".
/// </summary>
/// <param name="Line">The line as received, without its line end.</param>
/// <param name="PhoneMicroseconds">The line's time, in microseconds of the phone's CLOCK_MONOTONIC, as printed; logcat prints milliseconds unless asked for microseconds.</param>
/// <param name="ReceivedAtNanoseconds">The session time the recorder read the line.</param>
public sealed record AndroidTalkBackLine(
    string Line,
    long? PhoneMicroseconds,
    int? ProcessId,
    int? ThreadId,
    string? Level,
    string? Tag,
    string? Message,
    long ReceivedAtNanoseconds);

/// <summary>
/// A fragment TalkBack spoke, from its "Speaking fragment" line: the text,
/// the utterance it belongs to, and the event TalkBack gave as its cause.
/// </summary>
/// <param name="UtteranceId">TalkBack's id for the utterance, such as talkback_17.</param>
/// <param name="CauseType">The cause's type, such as EVENT_TYPE_ACCESSIBILITY; null when TalkBack gave none.</param>
/// <param name="CauseSubtype">The cause's subtype, such as TYPE_VIEW_ACCESSIBILITY_FOCUSED; null when TalkBack gave none.</param>
/// <param name="CausePhoneMilliseconds">The cause's time as TalkBack wrote it, in milliseconds; null when TalkBack gave none.</param>
/// <param name="Cause">The cause as TalkBack wrote it, or "null".</param>
public sealed record AndroidTalkBackFragment(
    string UtteranceId,
    string Text,
    string? CauseType,
    string? CauseSubtype,
    long? CausePhoneMilliseconds,
    string Cause,
    long PhoneMicroseconds,
    long ReceivedAtNanoseconds,
    int ReaderVersion);

/// <summary>
/// A word the speech engine reached, from TalkBack's "speak word" line. The
/// line does not name its utterance; it is given the utterance last
/// started, unless that one was interrupted, and is null when none was
/// speaking.
/// </summary>
public sealed record AndroidTalkBackWord(
    string? UtteranceId,
    string Word,
    long PhoneMicroseconds,
    long ReceivedAtNanoseconds,
    int ReaderVersion);

/// <summary>
/// An utterance TalkBack cut off, from its "interrupting current item"
/// line, with the text and fragment start index that line gives.
/// </summary>
/// <param name="FragmentStartIndex">The fragmentStartIndex TalkBack wrote for the item; null when the line had none.</param>
public sealed record AndroidTalkBackInterruption(
    string UtteranceId,
    string? Text,
    int? FragmentStartIndex,
    long PhoneMicroseconds,
    long ReceivedAtNanoseconds,
    int ReaderVersion);

/// <summary>Why TalkBack's lines are missing from part or all of a recording.</summary>
/// <param name="ConnectionLost">True when logcat ended during the recording.</param>
public sealed record AndroidTalkBackUnavailable(string Serial, string Reason, bool ConnectionLost);

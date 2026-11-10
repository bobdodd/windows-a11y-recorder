using System.Globalization;
using System.Text.RegularExpressions;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// One line as <c>logcat -v monotonic</c> prints it: the time in seconds
/// since boot with three or six decimals, the process and thread ids, the
/// level, the tag and the message (logprint.cpp, FORMAT_THREADTIME, the
/// format logcat prints by default, with the time as CLOCK_MONOTONIC).
/// </summary>
public sealed partial record TalkBackLogLine(
    long PhoneMicroseconds,
    int ProcessId,
    int ThreadId,
    string Level,
    string Tag,
    string Message)
{
    /// <summary>The line's parts, or null for a line that is not a log entry, such as "--------- beginning of main".</summary>
    public static TalkBackLogLine? TryParse(string line)
    {
        var match = Entry().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var fraction = match.Groups["fraction"].Value;
        if (fraction.Length > 6 ||
            !long.TryParse(match.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            !long.TryParse(fraction.PadRight(6, '0'), NumberStyles.None, CultureInfo.InvariantCulture, out var micros) ||
            !int.TryParse(match.Groups["pid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ||
            !int.TryParse(match.Groups["tid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var tid))
        {
            return null;
        }

        return new TalkBackLogLine(
            seconds * 1_000_000 + micros,
            pid,
            tid,
            match.Groups["level"].Value,
            match.Groups["tag"].Value.TrimEnd(),
            match.Groups["message"].Value);
    }

    // The tag is printed padded to 8 characters and followed by ": ".
    [GeneratedRegex(@"^\s*(?<seconds>\d+)\.(?<fraction>\d+)\s+(?<pid>\d+)\s+(?<tid>\d+)\s+(?<level>[VDIWEFS])\s+(?<tag>.*?)\s*: (?<message>.*)$")]
    private static partial Regex Entry();
}

/// <summary>
/// Reads what TalkBack said from its log lines, version
/// <see cref="AndroidTalkBackRecords.ReaderVersion"/>, using the line forms
/// feasibility check 3 found on the Pixel with TalkBack 17.1 at log level
/// Verbose (android-recording.md, "What TalkBack said"):
/// <list type="bullet">
/// <item><c>SpeechControllerImpl: Speaking fragment text="...", utteranceId=talkback_17, ..., event=type:... subtype:... displayId:0 time:...</c>
/// for each fragment spoken, with <c>event=null</c> when TalkBack gave no cause, as for a usage hint;</item>
/// <item><c>FeedbackFragmentsIterator: onFragmentRangeStarted ,  speak word = ...</c> for each word the speech engine reached;</item>
/// <item><c>SpeechControllerImpl: clearCurrentAndQueuedUtterancesThatDontIgnoreInterrupts: interrupting current item: {utteranceId:"talkback_16", fragments:[{text:..., ...fragmentStartIndex:14}], ...}</c>
/// when TalkBack cut an utterance off.</item>
/// </list>
/// Every other line is left to the raw lines. These are TalkBack's own log
/// lines, not a published interface, so a TalkBack version that words them
/// differently needs a new version of this reader.
/// </summary>
public sealed partial class TalkBackSpeechReader
{
    private readonly HashSet<string> _started = new(StringComparer.Ordinal);
    private string? _speaking;

    /// <summary>The speech records the line gives: none for most lines, one for the three forms above.</summary>
    public TalkBackSpeech? Read(TalkBackLogLine line, long receivedAtNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(line);
        var version = AndroidTalkBackRecords.ReaderVersion;
        if (Fragment().Match(line.Message) is { Success: true } fragment)
        {
            var id = fragment.Groups["id"].Value;
            var cause = fragment.Groups["cause"].Value;
            var parts = Cause().Match(cause);
            long? causeTime = parts.Success &&
                long.TryParse(parts.Groups["time"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var time)
                    ? time
                    : null;
            var first = _started.Add(id);
            _speaking = id;
            return new TalkBackSpeech(
                first ? AndroidTalkBackRecords.UtteranceStartedEventType : AndroidTalkBackRecords.FragmentStartedEventType,
                new AndroidTalkBackFragment(
                    id,
                    fragment.Groups["text"].Value,
                    parts.Success ? parts.Groups["type"].Value : null,
                    parts.Success ? parts.Groups["subtype"].Value : null,
                    causeTime,
                    cause,
                    line.PhoneMicroseconds,
                    receivedAtNanoseconds,
                    version));
        }

        if (Word().Match(line.Message) is { Success: true } word)
        {
            return new TalkBackSpeech(
                AndroidTalkBackRecords.WordReachedEventType,
                new AndroidTalkBackWord(_speaking, word.Groups["word"].Value, line.PhoneMicroseconds, receivedAtNanoseconds, version));
        }

        if (Interrupted().Match(line.Message) is { Success: true } interrupted)
        {
            var id = interrupted.Groups["id"].Value;
            if (_speaking == id)
            {
                _speaking = null;
            }

            var text = InterruptedText().Match(line.Message);
            var index = StartIndex().Match(line.Message);
            return new TalkBackSpeech(
                AndroidTalkBackRecords.UtteranceInterruptedEventType,
                new AndroidTalkBackInterruption(
                    id,
                    text.Success ? text.Groups["text"].Value : null,
                    index.Success && int.TryParse(index.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var start)
                        ? start
                        : null,
                    line.PhoneMicroseconds,
                    receivedAtNanoseconds,
                    version));
        }

        return null;
    }

    // The text is everything up to the last '", utteranceId=', so that a
    // quotation mark in the text does not end it.
    [GeneratedRegex(@"^SpeechControllerImpl: Speaking fragment text=""(?<text>.*)"", utteranceId=(?<id>[^,\s]+),.*?, event=(?<cause>.*)$")]
    private static partial Regex Fragment();

    [GeneratedRegex(@"^type:(?<type>\S+) subtype:(?<subtype>\S+)(?: displayId:\S+)? time:(?<time>\d+)")]
    private static partial Regex Cause();

    [GeneratedRegex(@"^FeedbackFragmentsIterator: onFragmentRangeStarted ,\s*speak word = (?<word>.*)$")]
    private static partial Regex Word();

    [GeneratedRegex(@"^SpeechControllerImpl: clearCurrentAndQueuedUtterancesThatDontIgnoreInterrupts: interrupting current item: \{utteranceId:""(?<id>[^""]+)""")]
    private static partial Regex Interrupted();

    [GeneratedRegex(@"fragments:\[\{text:(?<text>.*?), earcons:")]
    private static partial Regex InterruptedText();

    [GeneratedRegex(@"fragmentStartIndex:(?<index>\d+)")]
    private static partial Regex StartIndex();
}

/// <summary>A speech record read from a line: its event type and payload.</summary>
public sealed record TalkBackSpeech(string EventType, object Payload);

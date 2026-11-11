using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 6 of docs/architecture/android-implementation-plan.md, step 6b:
// TalkBack's speech records kept in the playback index and read back as
// the utterances the "TalkBack said" strip and the properties panel show.
// The records are made by the step 6a reader from the lines check 3 saved
// on the Pixel with TalkBack 17.1 at log level Verbose
// (TestData/android-talkback-check3.txt): three swipes right on the Home
// screen, to "Session", "O SCANada" and "Speedtest".
public sealed class TalkBackSpeechTimelineTests : IDisposable
{
    private const long Second = 1_000_000_000;

    // Check 3's phone time 145977.000 s is the recording's start, so its
    // first utterance, "Session", starts 3.688 s in.
    private const long PhoneOffset = 145_977_000_000_000;

    private const string Hint = "Double-tap to activate. Double-tap and hold to long press. Actions available, swipe up or swipe down and double-tap to activate.";

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "android.talkback", "AndroidTalkBackCollector", "1",
        [AndroidTalkBackRecords.LogChannel, AndroidTalkBackRecords.SpeechChannel], "test");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "talkback-speech-" + Guid.NewGuid().ToString("N"));

    public TalkBackSpeechTimelineTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static long At(long phoneMicroseconds) => phoneMicroseconds * 1000 - PhoneOffset;

    // The speech records the reader makes of check 3's lines, each at its
    // phone time, as the collector stores them.
    private static List<(long Time, string EventType, object Payload)> Check3Speech()
    {
        var reader = new TalkBackSpeechReader();
        var records = new List<(long, string, object)>();
        foreach (var text in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "android-talkback-check3.txt")))
        {
            if (TalkBackLogLine.TryParse(text) is { } line && reader.Read(line, 0) is { } speech)
            {
                records.Add((At(line.PhoneMicroseconds), speech.EventType, speech.Payload));
            }
        }

        return records;
    }

    private SessionPlaybackArchive FromIndex(IEnumerable<(long Time, string EventType, object Payload)> records, out PlaybackIndex index)
    {
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var sequence = 0UL;
        foreach (var (time, eventType, payload) in records)
        {
            sequence++;
            builder.Add((long)sequence, RecorderEventFactory.Create(
                "s", Collector, AndroidTalkBackRecords.SpeechChannel, sequence, time, eventType, payload));
        }

        index = builder.Build();
        var archiveBuilder = new SessionPlaybackArchiveBuilder(_folder, retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        return archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(20), 20 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, false, 5, true, true),
            [], [], 1, 0, null));
    }

    [Fact]
    public void Index_KeepsEachSpeechRecord_WithItsPayload()
    {
        var speech = Check3Speech();
        FromIndex(speech, out var index);

        Assert.Equal(25, index.Version);
        var kept = index.Events.Where(item => item.Channel == AndroidTalkBackRecords.SpeechChannel).ToArray();
        Assert.Equal(speech.Count, kept.Length);
        var session = kept.First(item => item.EventType == AndroidTalkBackRecords.UtteranceStartedEventType);
        Assert.Equal("Session", session.Payload.GetProperty("text").GetString());
        Assert.Equal("talkback_13", session.Payload.GetProperty("utteranceId").GetString());
        Assert.Equal("TYPE_VIEW_ACCESSIBILITY_FOCUSED", session.Payload.GetProperty("causeSubtype").GetString());
    }

    [Fact]
    public void Check3_GivesSixUtterances_WithTheirWords_AndTheTwoHintsCutOff()
    {
        var talkBack = FromIndex(Check3Speech(), out _).TalkBackSpeech;

        Assert.True(talkBack.Recorded);
        var utterances = talkBack.Utterances;
        Assert.Equal(
            ["talkback_13", "talkback_14", "talkback_15", "talkback_16", "talkback_17", "talkback_18"],
            utterances.Select(item => item.UtteranceId));
        Assert.Equal(["Session", Hint, "O SCANada", Hint, "Speedtest", Hint], utterances.Select(item => item.Text));
        Assert.Equal(
            [At(145_980_688_000), At(145_981_886_000), At(145_983_171_000), At(145_984_902_000), At(145_986_336_000), At(145_987_679_000)],
            utterances.Select(item => item.Start));

        // The next swipe cut off the first two hints; the last was heard to
        // its end, and talkback_12, cut off before "Session", started before
        // the lines and is in no utterance.
        Assert.Equal([false, true, false, true, false, false], utterances.Select(item => item.Interrupted));
        Assert.Equal(At(145_982_828_000), utterances[1].InterruptedAt);
        Assert.Equal(At(145_986_014_000), utterances[3].InterruptedAt);

        // The 11 words before "Session" belong to no utterance shown.
        Assert.Equal([1, 4, 3, 4, 1, 18], utterances.Select(item => item.Words.Count));
        Assert.Equal(["Double", "tap", "to", "activate"], utterances[1].Words.Select(word => word.Word));
        Assert.Equal(["O", "SCA", "Nada"], utterances[2].Words.Select(word => word.Word));
        Assert.Equal(At(145_982_019_000), utterances[1].Words[0].Time);
        Assert.All(utterances, item => Assert.Equal(AndroidTalkBackRecords.UtteranceStartedEventType, item.Event!.EventType));
    }

    [Fact]
    public void Spans_RunToTheNextUtterance_OrToTheInterruption()
    {
        var talkBack = FromIndex(Check3Speech(), out _).TalkBackSpeech;
        var spans = talkBack.Spans;

        Assert.Equal(6, spans.Count);
        Assert.Equal((At(145_980_688_000), At(145_981_886_000)), (spans[0].Start, spans[0].End));
        Assert.Equal((At(145_981_886_000), At(145_982_828_000)), (spans[1].Start, spans[1].End));
        Assert.True(spans[1].Interrupted);
        Assert.Equal((At(145_984_902_000), At(145_986_014_000)), (spans[3].Start, spans[3].End));
        Assert.Equal(20 * Second, spans[5].End);
        Assert.Equal(["Session", Hint, "O SCANada", Hint, "Speedtest", Hint], spans.Select(span => span.Text));
        Assert.Equal("talkback_15", spans[2].Event!.Summary.Split(", ")[^1]);
    }

    [Fact]
    public void Rows_GiveTheUtteranceAtThePlayhead_AndItsWords()
    {
        var archive = FromIndex(Check3Speech(), out _);
        var talkBack = archive.TalkBackSpeech;

        var before = talkBack.RowsAt(1 * Second);
        Assert.Equal(["TalkBack said", "Words reached"], before.Select(row => row.Setting));
        Assert.All(before, row => Assert.Equal("Screen reader", row.Group));
        Assert.Equal(["nothing yet", "none"], before.Select(row => row.Value));

        var session = talkBack.RowsAt(At(145_980_688_000));
        Assert.Equal("\u201CSession\u201D", session[0].Value);
        Assert.Equal("Session (1 word)", session[1].Value);
        Assert.Equal(WindowsPreferenceTimeline.ClockOf(At(145_980_688_000)), session[0].WhenSet);
        Assert.True(session[0].Changed);

        // A hint cut off by the next swipe, with the time it was cut off,
        // and every word the speech engine reached in it.
        var hint = talkBack.RowsAt(At(145_982_000_000));
        Assert.Equal(
            $"\u201C{Hint}\u201D, interrupted at {WindowsPreferenceTimeline.ClockOf(At(145_982_828_000))}",
            hint[0].Value);
        Assert.Equal("Double tap to activate (4 words)", hint[1].Value);

        // Two seconds after "Speedtest" its hint has started.
        Assert.False(talkBack.RowsAt(At(145_986_336_000) + 3 * Second)[0].Changed);
        Assert.Equal($"\u201C{Hint}\u201D", talkBack.RowsAt(At(145_987_679_000))[0].Value);

        // The TalkBack said row steps through the utterances; the words row
        // has no steps of its own.
        Assert.Equal(
            talkBack.Utterances.Select(item => item.Start),
            PropertyChangeSteps.TimesOf(session[0], WindowsPreferenceTimeline.Empty, MagnifierChangeTimeline.Empty, talkBackSpeech: talkBack));
        Assert.Null(talkBack.ChangeTimesOf(TalkBackSpeechTimeline.WordsKey));
    }

    [Fact]
    public void Summaries_GiveTheTextOrTheWord()
    {
        var archive = FromIndex(Check3Speech(), out var index);
        var started = index.Events.First(item => item.EventType == AndroidTalkBackRecords.UtteranceStartedEventType);
        var word = index.Events.First(item => item.EventType == AndroidTalkBackRecords.WordReachedEventType);

        Assert.Equal(
            "utterance-started: Session, talkback_13",
            SessionPlaybackArchiveBuilder.CreateSummary(started.Channel, started.EventType, started.Payload));
        Assert.Equal(
            "word-reached: " + word.Payload.GetProperty("word").GetString(),
            SessionPlaybackArchiveBuilder.CreateSummary(word.Channel, word.EventType, word.Payload));
        Assert.Equal("utterance-started: Session, talkback_13", archive.TalkBackSpeech.Utterances[0].Event!.Summary);
    }

    [Fact]
    public void LaterFragments_JoinTheText_AndStrayRecords_AreLeftOut()
    {
        static AndroidTalkBackFragment Fragment(string id, string text) =>
            new(id, text, null, null, null, "null", 0, 0, AndroidTalkBackRecords.ReaderVersion);

        var talkBack = FromIndex(
            [
                (1 * Second, AndroidTalkBackRecords.UtteranceStartedEventType, Fragment("talkback_3", "Wi-Fi, ")),
                (1 * Second + 100, AndroidTalkBackRecords.FragmentStartedEventType, Fragment("talkback_3", "On")),
                (2 * Second, AndroidTalkBackRecords.WordReachedEventType,
                    new AndroidTalkBackWord(null, "stray", 0, 0, AndroidTalkBackRecords.ReaderVersion)),
                (3 * Second, AndroidTalkBackRecords.UtteranceInterruptedEventType,
                    new AndroidTalkBackInterruption("talkback_2", null, null, 0, 0, AndroidTalkBackRecords.ReaderVersion)),
                // A fragment of an utterance that started before the recording.
                (4 * Second, AndroidTalkBackRecords.FragmentStartedEventType, Fragment("talkback_1", "Earlier"))
            ],
            out _).TalkBackSpeech;

        var utterance = Assert.Single(talkBack.Utterances);
        Assert.Equal("Wi-Fi, On", utterance.Text);
        Assert.Empty(utterance.Words);
        Assert.False(utterance.Interrupted);
        Assert.Equal("none", talkBack.RowsAt(5 * Second)[1].Value);
    }

    [Fact]
    public void ARecordingWithoutSpeech_HasNoRows()
    {
        var talkBack = FromIndex([], out _).TalkBackSpeech;

        Assert.False(talkBack.Recorded);
        Assert.Empty(talkBack.Spans);
        Assert.Empty(talkBack.RowsAt(Second));
        Assert.Null(talkBack.ChangeTimesOf(TalkBackSpeechTimeline.SaidKey));
    }
}

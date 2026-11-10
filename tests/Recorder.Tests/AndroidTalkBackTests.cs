using System.Diagnostics;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 6 of docs/architecture/android-implementation-plan.md, first step:
// TalkBack's log lines read into what it said, against the lines check 3
// saved on the Pixel with TalkBack 17.1 at log level Verbose
// (TestData/android-talkback-check3.txt), a fake adb and a fake phone
// clock.
public sealed class AndroidTalkBackTests
{
    private const string Serial = "58191FDCQ00ABZ";

    // The phone clock in the fake is this far ahead of the session clock,
    // so that check 3's first line is 0.234 s into the recording.
    private const long PhoneOffset = 145_977_000_000_000;

    // The line forms

    [Fact]
    public void Line_IsReadIntoItsParts()
    {
        var line = TalkBackLogLine.TryParse("145980.688123 20192 20192 V talkback: SpeechControllerImpl: Speaking fragment text=\"Session\", utteranceId=talkback_13");

        Assert.NotNull(line);
        Assert.Equal(145_980_688_123, line.PhoneMicroseconds);
        Assert.Equal(20192, line.ProcessId);
        Assert.Equal(20192, line.ThreadId);
        Assert.Equal("V", line.Level);
        Assert.Equal("talkback", line.Tag);
        Assert.Equal("SpeechControllerImpl: Speaking fragment text=\"Session\", utteranceId=talkback_13", line.Message);

        // Milliseconds, as check 3 printed them, and a padded tag.
        Assert.Equal(145_977_234_000, TalkBackLogLine.TryParse("145977.234 20192 20192 D tb      : x: y")!.PhoneMicroseconds);
        Assert.Equal("x: y", TalkBackLogLine.TryParse("145977.234 20192 20192 D tb      : x: y")!.Message);
        Assert.Null(TalkBackLogLine.TryParse("--------- beginning of main"));
        Assert.Null(TalkBackLogLine.TryParse("145977.2345678 20192 20192 D talkback: too many digits"));
    }

    [Fact]
    public void Reader_Check3_GivesTheUtterancesInterruptionsAndWords()
    {
        var reader = new TalkBackSpeechReader();
        var lines = File.ReadLines(Check3Path()).ToList();
        Assert.Equal(1189, lines.Count);
        var speech = lines
            .Select(TalkBackLogLine.TryParse)
            .Select(line => line is null ? null : reader.Read(line, 0))
            .OfType<TalkBackSpeech>()
            .ToList();

        var started = speech
            .Where(item => item.EventType == AndroidTalkBackRecords.UtteranceStartedEventType)
            .Select(item => (AndroidTalkBackFragment)item.Payload)
            .ToList();
        const string Hint = "Double-tap to activate. Double-tap and hold to long press. Actions available, swipe up or swipe down and double-tap to activate. ";
        Assert.Equal(["talkback_13", "talkback_14", "talkback_15", "talkback_16", "talkback_17", "talkback_18"], started.Select(item => item.UtteranceId));
        Assert.Equal(["Session", Hint, "O SCANada", Hint, "Speedtest", Hint], started.Select(item => item.Text));
        Assert.DoesNotContain(speech, item => item.EventType == AndroidTalkBackRecords.FragmentStartedEventType);

        // A focus gives its cause; a usage hint gives none.
        var session = started[0];
        Assert.Equal("EVENT_TYPE_ACCESSIBILITY", session.CauseType);
        Assert.Equal("TYPE_VIEW_ACCESSIBILITY_FOCUSED", session.CauseSubtype);
        Assert.Equal(145_980_640, session.CausePhoneMilliseconds);
        Assert.Equal(145_980_688_000, session.PhoneMicroseconds);
        Assert.Equal(AndroidTalkBackRecords.ReaderVersion, session.ReaderVersion);
        Assert.Null(started[1].CauseType);
        Assert.Null(started[1].CausePhoneMilliseconds);
        Assert.Equal("null", started[1].Cause);

        var interrupted = speech
            .Where(item => item.EventType == AndroidTalkBackRecords.UtteranceInterruptedEventType)
            .Select(item => (AndroidTalkBackInterruption)item.Payload)
            .ToList();
        Assert.Equal(["talkback_12", "talkback_14", "talkback_16"], interrupted.Select(item => item.UtteranceId));
        Assert.All(interrupted, item => Assert.Equal(Hint, item.Text));
        Assert.Equal([105, 14, 14], interrupted.Select(item => item.FragmentStartIndex));

        // Each word goes with the utterance speaking, and with none before
        // the first utterance or after one is cut off.
        var words = speech
            .Where(item => item.EventType == AndroidTalkBackRecords.WordReachedEventType)
            .Select(item => (AndroidTalkBackWord)item.Payload)
            .ToList();
        Assert.Equal(42, words.Count);
        Assert.Equal("long", words[0].Word);
        Assert.Null(words[0].UtteranceId);
        Assert.Contains(words, word => word.UtteranceId == "talkback_14");
        Assert.Contains(words, word => word.UtteranceId == "talkback_18");
        Assert.All(words, word => Assert.NotEqual("talkback_12", word.UtteranceId));
    }

    [Fact]
    public void Reader_ALaterFragmentOfTheSameUtterance_IsAFragment()
    {
        var reader = new TalkBackSpeechReader();
        var first = reader.Read(TalkBackLogLine.TryParse("1.000 1 1 V talkback: SpeechControllerImpl: Speaking fragment text=\"Say \"hi\"\", utteranceId=talkback_3, TtsSpan=null, locale=null, event=null")!, 5);
        var second = reader.Read(TalkBackLogLine.TryParse("1.100 1 1 V talkback: SpeechControllerImpl: Speaking fragment text=\"Button\", utteranceId=talkback_3, TtsSpan=null, locale=null, event=null")!, 6);

        Assert.Equal(AndroidTalkBackRecords.UtteranceStartedEventType, first!.EventType);
        Assert.Equal("Say \"hi\"", ((AndroidTalkBackFragment)first.Payload).Text);
        Assert.Equal(AndroidTalkBackRecords.FragmentStartedEventType, second!.EventType);
        Assert.Null(reader.Read(TalkBackLogLine.TryParse("1.200 1 1 D talkback: Something else")!, 7));
    }

    // The collector

    [Fact]
    public async Task Collector_RecordsEveryLineAndWhatTalkBackSaid()
    {
        var adb = new FakeAdb();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidTalkBackCollector(adb, Serial, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        Assert.Null(collector.StopReminder);

        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        var logcat = await adb.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(["-s", Serial, "logcat", "-v", "monotonic", "-v", "usec", "-T", "1"], logcat.Arguments);

        // The newest line from before the recording, as -T 1 gives it.
        logcat.Line("145976.900000 20192 20192 D talkback: from before the recording");
        logcat.Line("--------- beginning of main");

        // Another app's line, read but not kept.
        logcat.Line("145977.100000 1234 1250 D CCodec: allocate(c2.android.aac.decoder)");
        var check3 = File.ReadLines(Check3Path()).ToList();
        foreach (var line in check3)
        {
            logcat.Line(line + "\r");
        }

        logcat.Line("");
        await WaitUntilAsync(() => sink.Records.Count(record => record.Channel == AndroidTalkBackRecords.LogChannel) == check3.Count + 1);
        Assert.Matches(
            "^TalkBack: 6 utterances, 3 interrupted, 42 words reached, latest \"Double-tap to activate\\. Double-tap and hold to long press\\.\\.\\.\", 1190 TalkBack log lines out of 1192 read from the phone's log, median delay from phone to recorder -?[\\d.]+ ms\\.$",
            collector.Summary);
        Assert.NotNull(collector.StopReminder);
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(logcat.Stopped);
        Assert.Equal(CollectorHealthState.Healthy, collector.HealthState);
        Assert.Null(collector.HealthReason);
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));

        // Every line from the recording, as received, with its parts.
        var lines = sink.Records.Where(record => record.Channel == AndroidTalkBackRecords.LogChannel).ToList();
        Assert.All(lines, record => Assert.Equal(AndroidTalkBackRecords.LineEventType, record.EventType));
        Assert.Equal("--------- beginning of main", lines[0].Payload.GetProperty("line").GetString());
        Assert.Contains(AndroidScreenRecords.ReceiptTimeFlag, lines[0].QualityFlags);
        Assert.Null(lines[0].NativeTimestamp);
        var first = lines[1];
        Assert.Equal(check3[0], first.Payload.GetProperty("line").GetString());
        Assert.Equal(145_977_234_000, first.Payload.GetProperty("phoneMicroseconds").GetInt64());
        Assert.Equal("V", first.Payload.GetProperty("level").GetString());
        Assert.Equal("talkback", first.Payload.GetProperty("tag").GetString());
        Assert.Equal(145_977_234_000_000 - PhoneOffset, first.MonotonicNanoseconds);
        Assert.Equal(145_977_234_000_000, first.NativeTimestamp!.Value);
        Assert.Equal(AndroidCompanionRecords.FittedClockMapping, first.ClockMappingId);
        Assert.Equal(FakePhoneClock.Uncertainty, first.TimestampUncertaintyNanoseconds);
        Assert.Contains(AndroidTalkBackRecords.PhoneLogTimeFlag, first.QualityFlags);
        Assert.DoesNotContain(lines, record => record.Payload.GetProperty("line").GetString()!.Contains("from before", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, record => record.Payload.GetProperty("line").GetString()!.Contains("CCodec", StringComparison.Ordinal));

        // What TalkBack said, at the times of its lines.
        var speech = sink.Records.Where(record => record.Channel == AndroidTalkBackRecords.SpeechChannel).ToList();
        Assert.Equal(6 + 3 + 42, speech.Count);
        var session = speech.First(record => record.EventType == AndroidTalkBackRecords.UtteranceStartedEventType);
        Assert.Equal("Session", session.Payload.GetProperty("text").GetString());
        Assert.Equal("talkback_13", session.Payload.GetProperty("utteranceId").GetString());
        Assert.Equal("TYPE_VIEW_ACCESSIBILITY_FOCUSED", session.Payload.GetProperty("causeSubtype").GetString());
        Assert.Equal(1, session.Payload.GetProperty("readerVersion").GetInt32());
        Assert.Equal(145_980_688_000_000 - PhoneOffset, session.MonotonicNanoseconds);
        Assert.Equal([AndroidTalkBackRecords.PhoneLogTimeFlag], session.QualityFlags);
        var cut = speech.First(record => record.EventType == AndroidTalkBackRecords.UtteranceInterruptedEventType);
        Assert.Equal(105, cut.Payload.GetProperty("fragmentStartIndex").GetInt32());
        var word = speech.First(record => record.EventType == AndroidTalkBackRecords.WordReachedEventType);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, word.Payload.GetProperty("utteranceId").ValueKind);
    }

    [Fact]
    public async Task Collector_NoSpeechLines_SaysToSetTheLogLevel()
    {
        var adb = new FakeAdb();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidTalkBackCollector(adb, Serial, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        var logcat = await adb.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => collector.Summary is not null);
        Assert.Equal(
            "TalkBack: no speech lines yet, 0 TalkBack log lines out of 0 read from the phone's log. If TalkBack is on, set \"Log output level\" to \"Verbose\" in TalkBack's settings, Advanced settings, Developer settings.",
            collector.Summary);
        logcat.Line("145977.900000 1234 1250 D CCodec: allocate(c2.android.aac.decoder)");
        logcat.Line("145978.000000 20192 20192 D talkback: SpeechControllerImpl: clearCurrentAndQueuedUtterancesThatDontIgnoreInterrupts: interrupting current item: {utteranceId:\"talkback_2\"}");
        await WaitUntilAsync(() => sink.Records.Any(record => record.Channel == AndroidTalkBackRecords.SpeechChannel));

        Assert.StartsWith("TalkBack: no speech lines yet, 1 TalkBack log line out of 2 read from the phone's log. ", collector.Summary);
        Assert.Null(collector.StopReminder);
        var cut = sink.Records.Single(record => record.Channel == AndroidTalkBackRecords.SpeechChannel);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cut.Payload.GetProperty("text").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cut.Payload.GetProperty("fragmentStartIndex").ValueKind);
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
    }

    [Fact]
    public async Task Collector_WithoutAPhoneClock_PlacesByReceipt()
    {
        var adb = new FakeAdb();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidTalkBackCollector(adb, Serial, new FakePhoneClock(false));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        var logcat = await adb.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Even a line from long before the recording: without a clock, its
        // time cannot be compared with the start.
        logcat.Line("1.000000 20192 20192 V talkback: FeedbackFragmentsIterator: onFragmentRangeStarted ,  speak word = Home");
        await WaitUntilAsync(() => sink.Records.Any(record => record.Channel == AndroidTalkBackRecords.SpeechChannel));

        Assert.EndsWith(", placed by the time each line arrived, as there is no phone clock.", collector.Summary);
        var word = sink.Records.Single(record => record.Channel == AndroidTalkBackRecords.SpeechChannel);
        Assert.Equal([AndroidScreenRecords.ReceiptTimeFlag], word.QualityFlags);
        Assert.NotEqual(AndroidCompanionRecords.FittedClockMapping, word.ClockMappingId);
        Assert.Equal(1_000_000_000, word.NativeTimestamp!.Value);
        Assert.Equal(word.Payload.GetProperty("receivedAtNanoseconds").GetInt64(), word.MonotonicNanoseconds);
        var validator = new EventRecordValidator("session-1");
        Assert.All(sink.Records, record => Assert.Empty(validator.Validate(record)));
    }

    [Fact]
    public async Task Collector_LogcatEnds_SaysWhatTalkBackSaidStopped()
    {
        var adb = new FakeAdb();
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidTalkBackCollector(adb, Serial, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        var logcat = await adb.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        logcat.Exit(1);
        await WaitUntilAsync(() => collector.HealthReason is not null);

        Assert.Equal(
            "What TalkBack said stopped being recorded during the recording. logcat on the phone ended with exit code 1: the phone was unplugged, adb stopped, or the phone ended it. The recording goes on without it.",
            collector.HealthReason);
        Assert.Equal(CollectorHealthState.Degraded, collector.HealthState);
        var unavailable = sink.Records.Single(record => record.EventType == AndroidTalkBackRecords.UnavailableEventType);
        Assert.True(unavailable.Payload.GetProperty("connectionLost").GetBoolean());
        var validator = new EventRecordValidator("session-1");
        Assert.Empty(validator.Validate(unavailable));
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.Equal(CollectorLifecycleState.Stopped, collector.LifecycleState);
    }

    [Fact]
    public async Task Collector_LogcatCannotStart_SaysWhy()
    {
        var adb = new FakeAdb(startFails: true);
        var sink = new ListSink();
        var clock = new SessionClock();
        await using var collector = new AndroidTalkBackCollector(adb, Serial, new FakePhoneClock(true));
        await collector.InitializeAsync(new CollectorInitializationContext("session-1", Path.GetTempPath(), clock, sink), CancellationToken.None);
        await collector.StartAsync(new SessionBoundary(0, DateTimeOffset.UtcNow), CancellationToken.None);
        await WaitUntilAsync(() => collector.HealthReason is not null);

        Assert.Equal(
            "What TalkBack said could not be recorded: adb could not start logcat: adb.exe was not found. The recording goes on without it.",
            collector.HealthReason);
        var unavailable = sink.Records.Single(record => record.EventType == AndroidTalkBackRecords.UnavailableEventType);
        Assert.False(unavailable.Payload.GetProperty("connectionLost").GetBoolean());
        await collector.StopAsync(new SessionBoundary(clock.GetElapsedNanoseconds(), DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.Equal(CollectorLifecycleState.Stopped, collector.LifecycleState);
    }

    [Fact]
    public void Validator_RefusesALineWithAnUnknownLevelOrSomePartsMissing()
    {
        var validator = new EventRecordValidator("session-1");
        var descriptor = CollectorDescriptor.Create(AndroidTalkBackCollector.Type, "AndroidTalkBackCollector", "1.0.0", [AndroidTalkBackRecords.LogChannel], "adb-logcat");
        var badLevel = RecorderEventFactory.Create(
            "session-1", descriptor, AndroidTalkBackRecords.LogChannel, 1, 0, AndroidTalkBackRecords.LineEventType,
            new AndroidTalkBackLine("x", 1, 2, 3, "Q", "talkback", "m", 0));
        var partial = RecorderEventFactory.Create(
            "session-1", descriptor, AndroidTalkBackRecords.LogChannel, 2, 0, AndroidTalkBackRecords.LineEventType,
            new AndroidTalkBackLine("x", 1, null, 3, "V", "talkback", "m", 0));

        Assert.Contains(validator.Validate(badLevel), issue => issue.Path == "#/payload/level");
        Assert.Contains(validator.Validate(partial), issue => issue.Code == "android-talkback-line-parts");
    }

    private static string Check3Path() => Path.Combine(AppContext.BaseDirectory, "TestData", "android-talkback-check3.txt");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "Timed out.");
            await Task.Delay(20);
        }
    }

    /// <summary>A phone clock exactly <see cref="PhoneOffset"/> ahead of the session clock.</summary>
    private sealed class FakePhoneClock(bool sampled) : IPhoneClock
    {
        public const long Uncertainty = 1_500_000;

        public Task<bool> FirstSample { get; } = Task.FromResult(sampled);

        public PhoneTimeMapping? Map(long phoneNanoseconds) =>
            sampled ? new PhoneTimeMapping(phoneNanoseconds - PhoneOffset, Uncertainty, 1) : null;
    }

    private sealed class FakeAdb(bool startFails = false) : IAdb
    {
        public string Path => "adb.exe";

        public TaskCompletionSource<FakeLogcat> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation) =>
            Task.FromResult(new AdbResult(1, "", $"unexpected command: {string.Join(' ', arguments)}"));

        public Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation) => Task.FromResult(0);

        public IAdbRunning Start(IReadOnlyList<string> arguments, Action<string> onLine)
        {
            if (startFails)
            {
                throw new InvalidOperationException("adb.exe was not found");
            }

            var running = new FakeLogcat([.. arguments], onLine);
            Started.TrySetResult(running);
            return running;
        }
    }

    private sealed class FakeLogcat(string[] arguments, Action<string> onLine) : IAdbRunning
    {
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string[] Arguments { get; } = arguments;

        public bool Stopped { get; private set; }

        public Task<int> Exited => _exited.Task;

        public void Line(string line) => onLine(line);

        public void Exit(int code) => _exited.TrySetResult(code);

        public ValueTask StopAsync()
        {
            Stopped = true;
            _exited.TrySetResult(-1);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ListSink : IRecorderEventSink
    {
        private readonly List<RecorderEvent> _records = [];

        public IReadOnlyList<RecorderEvent> Records
        {
            get
            {
                lock (_records)
                {
                    return [.. _records];
                }
            }
        }

        public bool TryWrite(RecorderEvent record)
        {
            lock (_records)
            {
                _records.Add(record);
            }

            return true;
        }
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;
using Recorder.Database;
using Recorder.Database.RecordingFiles;
using Recorder.Recreation;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// Slice 4g (protocol 0.53): the animation-updated and animation-removed
/// records the bridge writes, their validation, the animations of a document
/// at a recording time, their progress by the Web Animations procedures, and
/// the rows the evidence panel shows.
/// </summary>
public sealed class RecordedAnimationTests : IDisposable
{
    private const string Token = "F8543F87A3AF6713E6DEADA760E49A6C";
    private const string DocumentKey = Token + " browser-1 3440 dom-document-1";
    private const long Frequency = 10_000_000;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "recorded-animations-" + Guid.NewGuid().ToString("N"));

    public RecordedAnimationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Context(string token = Token) => $$"""
        {"browserInstanceId":"browser-1","processId":3440,"processType":"renderer","profileId":null,
         "browserContextId":null,"pageId":null,"frameId":null,"documentId":"dom-document-1",
         "executionWorldId":null,"documentToken":"{{token}}"}
        """;

    private static string N(double? value) =>
        value is { } number ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";

    // As the bridge writes it. The zero time is in counter ticks of a
    // 10 MHz clock.
    public static string Updated(
        string sequence = "7",
        string kind = "css-animation",
        string playState = "running",
        bool pending = false,
        double? playbackRate = 1,
        double? start = 1_000,
        double? current = 0,
        string zeroTicks = "10000000",
        string timelineKind = "document",
        double duration = 2_000,
        double delay = 0,
        double? iterations = 1,
        string direction = "normal",
        string fill = "none",
        string easing = "linear",
        double? progress = 0,
        double? currentIteration = 0,
        string token = Token,
        int? compositor = null) => $$"""
        {"context":{{Context(token)}},"sequenceNumber":"{{sequence}}","kind":"{{kind}}","name":"spin","id":null,
         "targetNodeId":14,"pseudoElement":null,"playState":"{{playState}}","pending":{{(pending ? "true" : "false")}},
         "playbackRate":{{N(playbackRate)}},"startTimeMilliseconds":{{N(start)}},"currentTimeMilliseconds":{{N(current)}},
         "timeline":{"kind":"{{timelineKind}}","zeroTicks":{{(timelineKind == "document" ? $"\"{zeroTicks}\"" : "null")}},
          "zeroTimeTicksMicroseconds":{{(timelineKind == "document" ? "\"1000000\"" : "null")}},"playbackRate":1,
          "sourceNodeId":{{(timelineKind == "document" ? "null" : "12")}},"subjectNodeId":null,
          "axis":{{(timelineKind == "document" ? "null" : "\"vertical\"")}}},
         "effect":{"delayMilliseconds":{{N(delay)}},"endDelayMilliseconds":0,"iterationStart":0,"iterations":{{N(iterations)}},
          "durationMilliseconds":{{N(duration)}},"direction":"{{direction}}","fill":"{{fill}}","easing":"{{easing}}",
          "progress":{{N(progress)}},"currentIteration":{{N(currentIteration)}}},
         "compositorAnimationId":{{N(compositor)}}}
        """;

    public static string Removed(string sequence = "7") => $$"""
        {"context":{{Context()}},"sequenceNumber":"{{sequence}}"}
        """;

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("animation-updated")]
    [InlineData("animation-removed")]
    public void TheProtocolReadsTheRecordsAsTheBridgeWritesThem(string eventType)
    {
        var json = eventType == "animation-updated" ? Updated() : Removed();
        BrowserProtocol.ValidateEvidencePayload(BrowserEvidenceChannels.Animation, eventType, J(json));

        var payload = JsonNode.Parse(json)!.AsObject();
        payload["undeclared"] = 1;
        Assert.ThrowsAny<Exception>(() =>
            BrowserProtocol.ValidateEvidencePayload(BrowserEvidenceChannels.Animation, eventType, J(payload.ToJsonString())));
    }

    [Fact]
    public void TheProtocolReadsAnAnimationWithNoEffectOnAScrollTimeline()
    {
        var payload = JsonNode.Parse(Updated(timelineKind: "scroll", start: null, current: null))!.AsObject();
        payload["effect"] = null;
        BrowserProtocol.ValidateEvidencePayload(BrowserEvidenceChannels.Animation, "animation-updated", J(payload.ToJsonString()));
        Assert.Empty(Validate("animation-updated", payload.ToJsonString()));
    }

    private static List<EventValidationIssue> Validate(string eventType, string json)
    {
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(BrowserEvidenceChannels.Animation, eventType, J(json), 100, issues);
        return issues;
    }

    [Fact]
    public void TheValidatorAcceptsTheRecordsAndRefusesAnUnknownKind()
    {
        Assert.True(EventPayloadValidator.IsBuiltInChannel(BrowserEvidenceChannels.Animation));
        Assert.Empty(Validate("animation-updated", Updated()));
        Assert.Empty(Validate("animation-removed", Removed()));
        Assert.Empty(Validate("animation-updated", Updated(iterations: null, kind: "web-animation", playState: "paused", direction: "alternate-reverse", fill: "both")));
        Assert.NotEmpty(Validate("animation-updated", Updated(kind: "smil")));
        Assert.NotEmpty(Validate("animation-updated", Updated(direction: "sideways")));
        Assert.NotEmpty(Validate("animation-updated", Updated(duration: -1)));
        Assert.Contains(Validate("not-an-animation-record", Removed()), issue => issue.Code == "event-type-unsupported");
    }

    // A record at a recording time, with the native timestamp the bridge's
    // clock gave it: the clock's ticks equal the recording time here, so a
    // zero time of 10,000,000 ticks is at 1 s.
    private static RecorderEvent Record(long nanoseconds, string eventType, string json, ulong sequence = 1) =>
        Event("session-a", Collector("test.browser", BrowserEvidenceChannels.Animation), sequence, nanoseconds,
            BrowserEvidenceChannels.Animation, eventType) with
        {
            Payload = J(json),
            NativeTimestamp = new NativeTimestamp("chromium-monotonic", nanoseconds / 100, "ticks"),
        };

    private static RecordedAnimations At(long nanoseconds, params (long Time, string Type, string Json)[] records)
    {
        var reader = new RecordedAnimationReader(DocumentKey, Frequency);
        foreach (var (time, type, json) in records)
        {
            reader.Add(Record(time, type, json));
        }
        return reader.At(nanoseconds);
    }

    [Fact]
    public void ARunningAnimationsTimeAndProgressAreComputedAtTheTime()
    {
        // The timeline's zero time is at 1 s and the animation started at
        // 1,000 ms on it, so at 2.5 s its current time is 500 ms.
        var animations = At(2_500_000_000, (2_000_000_000, "animation-updated", Updated()));
        var animation = Assert.Single(animations.Animations);
        Assert.True(animations.Recorded);
        Assert.Equal("computed", animation.CurrentTimeBasis);
        Assert.Equal(500, animation.CurrentTimeMilliseconds!.Value, 6);
        Assert.Equal(0.25, animation.Progress!.Value, 6);
        Assert.Equal(0, animation.CurrentIteration);
        Assert.Equal("active", animation.Phase);
        Assert.Equal(2_000_000_000, animation.StartRecordingNanoseconds);
        Assert.Equal(2_000_000_000, animation.RecordedNanoseconds);
    }

    [Fact]
    public void APlaybackRateScalesTheCurrentTime()
    {
        var animation = Assert.Single(At(2_500_000_000, (2_000_000_000, "animation-updated", Updated(playbackRate: 2))).Animations);
        Assert.Equal(1_000, animation.CurrentTimeMilliseconds!.Value, 6);
        Assert.Equal(0.5, animation.Progress!.Value, 6);
    }

    [Fact]
    public void APausedOrPendingAnimationKeepsItsRecordedCurrentTime()
    {
        var paused = Assert.Single(At(5_000_000_000, (2_000_000_000, "animation-updated", Updated(playState: "paused", current: 300))).Animations);
        Assert.Equal("recorded", paused.CurrentTimeBasis);
        Assert.Equal(300, paused.CurrentTimeMilliseconds);
        Assert.Equal(0.15, paused.Progress!.Value, 6);

        var pending = Assert.Single(At(5_000_000_000, (2_000_000_000, "animation-updated", Updated(pending: true, start: null, current: 0))).Animations);
        Assert.Equal("recorded", pending.CurrentTimeBasis);
        Assert.True(pending.Pending);
    }

    [Fact]
    public void TheLatestRecordAtOrBeforeTheTimeIsUsed()
    {
        var records = new[]
        {
            (2_000_000_000L, "animation-updated", Updated()),
            (2_400_000_000L, "animation-updated", Updated(playState: "paused", current: 1_400)),
        };
        Assert.Equal("running", Assert.Single(At(2_300_000_000, records).Animations).PlayState);
        Assert.Equal("paused", Assert.Single(At(2_600_000_000, records).Animations).PlayState);
        // Before its first record the animation is not listed.
        Assert.Empty(At(1_900_000_000, records).Animations);
    }

    [Fact]
    public void IdleRemovedAndOtherDocumentsAnimationsAreNotListed()
    {
        Assert.Empty(At(3_000_000_000,
            (2_000_000_000, "animation-updated", Updated()),
            (2_500_000_000, "animation-updated", Updated(playState: "idle", start: null, current: null))).Animations);
        Assert.Empty(At(3_000_000_000,
            (2_000_000_000, "animation-updated", Updated()),
            (2_500_000_000, "animation-removed", Removed())).Animations);
        Assert.Empty(At(3_000_000_000,
            (2_000_000_000, "animation-updated", Updated(token: "0000000000000000000000000000000A"))).Animations);
        // Removed after the time, it is still listed at the time.
        Assert.Single(At(2_200_000_000,
            (2_000_000_000, "animation-updated", Updated()),
            (2_500_000_000, "animation-removed", Removed())).Animations);
    }

    [Fact]
    public void AFinishedAnimationIsListedOnlyWhileItsFillHoldsIt()
    {
        var held = At(9_000_000_000, (4_000_000_000, "animation-updated", Updated(playState: "finished", current: 2_000, fill: "forwards", progress: 1)));
        var animation = Assert.Single(held.Animations);
        Assert.Equal("after", animation.Phase);
        Assert.Equal(1, animation.Progress);
        Assert.Empty(At(9_000_000_000, (4_000_000_000, "animation-updated", Updated(playState: "finished", current: 2_000, fill: "none", progress: null))).Animations);
    }

    [Fact]
    public void ARecordingWithoutAnimationRecordsSaysSo()
    {
        var reader = new RecordedAnimationReader(DocumentKey, Frequency);
        Assert.False(reader.At(1_000).Recorded);
    }

    private static RecordedAnimationTiming Timing(
        double duration = 1_000,
        double delay = 0,
        double? iterations = 1,
        double iterationStart = 0,
        string direction = "normal",
        string fill = "none") =>
        new(delay, 0, iterationStart, iterations, duration, direction, fill, "linear");

    [Theory]
    [InlineData("normal", 2_250, 2.0, 0.25)]
    [InlineData("reverse", 2_250, 2.0, 0.75)]
    [InlineData("alternate", 1_250, 1.0, 0.75)]
    [InlineData("alternate", 2_250, 2.0, 0.25)]
    [InlineData("alternate-reverse", 1_250, 1.0, 0.25)]
    [InlineData("alternate-reverse", 2_250, 2.0, 0.75)]
    public void EachDirectionGivesItsDirectedProgress(string direction, double local, double iteration, double progress)
    {
        var (phase, current, directed) = RecordedAnimationReader.Progress(Timing(iterations: 4, direction: direction), local, 1);
        Assert.Equal("active", phase);
        Assert.Equal(iteration, current);
        Assert.Equal(progress, directed!.Value, 6);
    }

    [Fact]
    public void DelaysFillsAndIterationStartsFollowTheModel()
    {
        // Before its delay, with no backwards fill, an effect is not in effect.
        Assert.Equal(("before", (double?)null, (double?)null), RecordedAnimationReader.Progress(Timing(delay: 500), 200, 1));
        // With a backwards fill it shows its start.
        var (_, _, backwards) = RecordedAnimationReader.Progress(Timing(delay: 500, fill: "backwards"), 200, 1);
        Assert.Equal(0, backwards);
        // An iteration start of 0.5 starts halfway through the first iteration.
        var (_, startIteration, started) = RecordedAnimationReader.Progress(Timing(iterationStart: 0.5, iterations: 2), 250, 1);
        Assert.Equal(0, startIteration);
        Assert.Equal(0.75, started!.Value, 6);
        // At the end of a fractional count of iterations, held forwards.
        var (endPhase, endIteration, end) = RecordedAnimationReader.Progress(Timing(iterations: 1.5, fill: "forwards"), 5_000, 1);
        Assert.Equal("after", endPhase);
        Assert.Equal(1, endIteration);
        Assert.Equal(0.5, end!.Value, 6);
        // At the end of whole iterations the progress is 1, in the last iteration.
        var (_, lastIteration, last) = RecordedAnimationReader.Progress(Timing(iterations: 2, fill: "both"), 2_000, 1);
        Assert.Equal(1, lastIteration);
        Assert.Equal(1, last);
        // An infinite animation has no last iteration.
        var (_, infinite, _) = RecordedAnimationReader.Progress(Timing(iterations: null), 3_500, 1);
        Assert.Equal(3, infinite);
        // An unresolved local time is not in effect.
        Assert.Equal("none", RecordedAnimationReader.Progress(Timing(), null, 1).Phase);
    }

    [Fact]
    public void TheEvidenceListsTheAnimationsWithTheirTargetsPaths()
    {
        var state = RecordedPageTests.State();
        var animations = At(2_500_000_000, (2_000_000_000, "animation-updated", Updated(compositor: 31)));
        var evidence = RecordedEvidence.Create(state, "https://example.test/", 2_500_000_000, 2_500_000_000, "basis",
            new RecreationFidelity("not-checked", "", []), [], animations);
        Assert.Null(evidence.AnimationsNotRead);
        Assert.NotEmpty(evidence.AnimationNotes);
        var row = Assert.Single(evidence.Animations);
        Assert.Equal("css-animation", row.Kind);
        Assert.Equal("spin", row.Name);
        Assert.Equal(RecordedPaths.Of(state.Dom!, 14)!.Display, row.Target!.Display);
        Assert.Equal(2_000_000_000, row.StartNanoseconds);
        Assert.Equal(0.25, row.Progress!.Value, 6);
        Assert.Equal("computed", row.CurrentTimeBasis);
        Assert.True(row.OnCompositor);
        Assert.Equal("document timeline", row.Timeline);

        var none = RecordedEvidence.Create(state, "https://example.test/", 1, 1, "basis",
            new RecreationFidelity("not-checked", "", []), [], RecordedAnimations.None);
        Assert.Contains("protocol 0.53", none.AnimationsNotRead);
        Assert.Empty(none.Animations);
    }

    [Fact]
    public async Task TheAnimationsAreReadFromTheRecordingFileUpToTheTime()
    {
        var path = Path.Combine(_directory, "recording.mcap");
        var collector = Collector("test.browser", BrowserEvidenceChannels.Animation);
        var records = new (long Time, string Type, string Json)[]
        {
            (2_000_000_000, "animation-updated", Updated()),
            (2_000_000_100, "animation-updated", Updated(sequence: "8", kind: "css-transition")),
            (2_200_000_000, "animation-removed", Removed("8")),
            (3_000_000_000, "animation-updated", Updated(playState: "paused", current: 1_900)),
        };
        var events = records.Select((record, index) =>
                Event("session-a", collector, (ulong)index + 1, record.Time, BrowserEvidenceChannels.Animation, record.Type) with
                {
                    Payload = J(record.Json),
                    NativeTimestamp = new NativeTimestamp("chromium-monotonic", record.Time / 100, "ticks"),
                })
            .ToArray();
        using (var target = new RecordingFileBatchTarget(
            path,
            new Dictionary<string, string> { ["sessionKey"] = "session-a", ["clockFrequency"] = "10000000" },
            new RecordingFileWriterOptions { ChunkBytes = 1024 }))
        {
            var batch = events.Select((record, index) => new BufferedEvent(index, record, record.Payload.GetRawText())).ToArray();
            Assert.Empty(await target.WriteAsync(new EventBatch(batch, [], []), TestContext.Current.CancellationToken));
            target.Finish();
        }

        using var reader = RecordingFileReader.Open(path);
        var early = RecordingFileAnimations.Read(reader, DocumentKey, 2_100_000_000, TestContext.Current.CancellationToken);
        Assert.Equal(["7", "8"], early.Animations.Select(item => item.SequenceNumber));
        var running = early.Animations[0];
        Assert.Equal(100, running.CurrentTimeMilliseconds!.Value, 3);
        var later = RecordingFileAnimations.Read(reader, DocumentKey, 3_500_000_000, TestContext.Current.CancellationToken);
        var paused = Assert.Single(later.Animations);
        Assert.Equal("paused", paused.PlayState);
        Assert.Equal(1_900, paused.CurrentTimeMilliseconds);
    }
}

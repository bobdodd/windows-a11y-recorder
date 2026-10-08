using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The Magnifier change records, their payloads, and stepping through a
/// property's changes from the properties panel. See
/// docs/architecture/accessibility-preferences.md, "Visible focus and the
/// Enter key".
/// </summary>
public sealed class MagnifierChangesTests
{
    private const long Second = 1_000_000_000;

    private static readonly double[] Identity = [.. Enumerable.Range(0, 25).Select(i => i % 6 == 0 ? 1.0 : 0.0)];
    private static readonly double[] Inversion =
    [
        -1, 0, 0, 0, 0,
        0, -1, 0, 0, 0,
        0, 0, -1, 0, 0,
        0, 0, 0, 1, 0,
        1, 1, 1, 0, 1
    ];

    private static MagnificationReading Magnification(double level, int x, int y) => new(level, x, y, null);

    private static ColorEffectReading Effect(double[] matrix) => new([.. matrix], null);

    private static readonly CollectorDescriptor Collector = CollectorDescriptor.Create(
        "windows.desktop-frames", "DesktopFrameCollector", "1", ["graphics.desktop.frames", MagnifierChanges.Channel], "test");

    private static RecorderEvent Record(long time, ulong sequence, object payload) =>
        RecorderEventFactory.Create("s", Collector, MagnifierChanges.Channel, sequence, time, MagnifierChanges.ChangeEventType, payload);

    private static List<EventValidationIssue> Validate(JsonElement payload)
    {
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(MagnifierChanges.Channel, MagnifierChanges.ChangeEventType, payload, 100, issues);
        return issues;
    }

    [Fact]
    public void TheComparisonNamesWhatChanged()
    {
        var plain = Magnification(1, 0, 0);
        var none = Effect(Identity);
        Assert.Empty(MagnifierChanges.Compare(plain, none, Magnification(1, 0, 0), Effect(Identity)));
        Assert.Equal([MagnifierChanges.Level], MagnifierChanges.Compare(plain, none, Magnification(2, 0, 0), none));
        Assert.Equal([MagnifierChanges.Position], MagnifierChanges.Compare(plain, none, Magnification(1, 10, 0), none));
        Assert.Equal([MagnifierChanges.Position], MagnifierChanges.Compare(plain, none, Magnification(1, 0, -4), none));
        Assert.Equal([MagnifierChanges.ColorEffect], MagnifierChanges.Compare(plain, none, plain, Effect(Inversion)));
        Assert.Equal(
            [MagnifierChanges.Level, MagnifierChanges.Position, MagnifierChanges.ColorEffect],
            MagnifierChanges.Compare(plain, none, Magnification(2, 5, 5), Effect(Inversion)));

        // A reading that fails changes what it reads, though the values are null.
        var failed = new MagnificationReading(null, null, null, "MagGetFullscreenTransform failed with error 21.");
        Assert.Equal([MagnifierChanges.Level, MagnifierChanges.Position], MagnifierChanges.Compare(plain, none, failed, none));
        Assert.Equal(
            [MagnifierChanges.ColorEffect],
            MagnifierChanges.Compare(plain, none, plain, new ColorEffectReading(null, "MagGetFullscreenColorEffect failed with error 21.")));

        // No reader on either side: nothing to compare.
        Assert.Empty(MagnifierChanges.Compare(null, null, plain, none));
        Assert.Empty(MagnifierChanges.Compare(plain, none, null, null));
    }

    [Fact]
    public void TheTrackerGivesARecordOnlyWhenAFrameDiffersFromThePrevious()
    {
        var tracker = new MagnifierChangeTracker();
        Assert.Null(tracker.Observe(Second, 1, Magnification(2, 0, 0), Effect(Identity)));
        Assert.Null(tracker.Observe(Second + Second / 5, 2, Magnification(2, 0, 0), Effect(Identity)));

        var payload = tracker.Observe(Second + 2 * Second / 5, 3, Magnification(2, 40, 0), Effect(Identity));
        Assert.NotNull(payload);
        var record = Record(Second + 2 * Second / 5, 3, payload!);
        Assert.Empty(Validate(record.Payload));
        Assert.Equal(3, record.Payload.GetProperty("frameSequence").GetInt64());
        Assert.Equal(Second + Second / 5, record.Payload.GetProperty("previousFrameAt").GetInt64());
        Assert.False(record.Payload.GetProperty("changed").GetProperty("level").GetBoolean());
        Assert.True(record.Payload.GetProperty("changed").GetProperty("position").GetBoolean());
        Assert.Equal(0, record.Payload.GetProperty("previous").GetProperty("fullscreenMagnification").GetProperty("x").GetInt32());
        Assert.Equal(40, record.Payload.GetProperty("current").GetProperty("fullscreenMagnification").GetProperty("x").GetInt32());
        Assert.Equal(25, record.Payload.GetProperty("current").GetProperty("fullscreenColorEffect").GetProperty("matrix").GetArrayLength());

        // After a reset, the next frame is a start again.
        tracker.Reset();
        Assert.Null(tracker.Observe(5 * Second, 1, Magnification(3, 0, 0), Effect(Inversion)));
    }

    [Fact]
    public void TheValidatorRefusesChangesThatDoNotMatchTheReadings()
    {
        var tracker = new MagnifierChangeTracker();
        tracker.Observe(Second, 1, Magnification(1, 0, 0), Effect(Identity));
        var payload = Record(2 * Second, 2, tracker.Observe(2 * Second, 2, Magnification(2, 0, 0), Effect(Identity))!).Payload;
        Assert.Empty(Validate(payload));

        var wrong = JsonNode.Parse(payload.GetRawText())!;
        wrong["changed"]!["position"] = true;
        using (var document = JsonDocument.Parse(wrong.ToJsonString()))
        {
            Assert.Contains(Validate(document.RootElement), issue => issue.Code == "magnifier-change-inconsistent");
        }

        var empty = JsonNode.Parse(payload.GetRawText())!;
        empty["changed"]!["level"] = false;
        using (var document = JsonDocument.Parse(empty.ToJsonString()))
        {
            Assert.Contains(Validate(document.RootElement), issue => issue.Code == "magnifier-change-empty");
        }

        var missing = JsonNode.Parse(payload.GetRawText())!;
        missing["current"]!.AsObject().Remove("fullscreenColorEffect");
        using (var document = JsonDocument.Parse(missing.ToJsonString()))
        {
            Assert.NotEmpty(Validate(document.RootElement));
        }
    }

    [Fact]
    public void SteppingMovesBetweenTheChangesInEffect()
    {
        long[] times = [2 * Second, 5 * Second, 9 * Second];
        IReadOnlyList<long> none = [];

        // No change at all.
        Assert.Null(PropertyChangeSteps.InEffect(none, 4 * Second));
        Assert.Null(PropertyChangeSteps.Previous(none, 4 * Second));
        Assert.Null(PropertyChangeSteps.Next(none, 4 * Second));

        // Before the first change: nothing earlier, the first is next.
        Assert.Null(PropertyChangeSteps.InEffect(times, Second));
        Assert.Null(PropertyChangeSteps.Previous(times, Second));
        Assert.Equal(2 * Second, PropertyChangeSteps.Next(times, Second));

        // At the first change: nothing earlier.
        Assert.Equal(2 * Second, PropertyChangeSteps.InEffect(times, 2 * Second));
        Assert.Null(PropertyChangeSteps.Previous(times, 2 * Second));

        // Between changes: the previous is the one in effect, the nearest
        // strictly before the playhead.
        Assert.Equal(5 * Second, PropertyChangeSteps.InEffect(times, 7 * Second));
        Assert.Equal(5 * Second, PropertyChangeSteps.Previous(times, 7 * Second));
        Assert.Equal(9 * Second, PropertyChangeSteps.Next(times, 7 * Second));

        // At a change, the next is the one after it, so pressing again moves on.
        Assert.Equal(9 * Second, PropertyChangeSteps.Next(times, 5 * Second));
        Assert.Equal(2 * Second, PropertyChangeSteps.Previous(times, 5 * Second));

        // Two changes at the same time are one change.
        long[] tied = [2 * Second, 2 * Second, 5 * Second];
        Assert.Equal(2 * Second, PropertyChangeSteps.Previous(tied, 5 * Second));
        Assert.Null(PropertyChangeSteps.Previous(tied, 2 * Second));
        Assert.Equal(5 * Second, PropertyChangeSteps.Next(tied, 2 * Second));

        // At or after the last change: nothing later.
        Assert.Null(PropertyChangeSteps.Next(times, 9 * Second));
        Assert.Equal(9 * Second, PropertyChangeSteps.Previous(times, 10 * Second));
        Assert.Equal(5 * Second, PropertyChangeSteps.Previous(times, 9 * Second));
    }

    [Fact]
    public void TheCountShowsTheChangeInEffectOfAllTheChanges()
    {
        long[] times = [2 * Second, 5 * Second, 9 * Second, 12 * Second];

        // No change: nothing to count or move to.
        Assert.Equal(new PropertyChangePosition(0, 0, null, null), PropertyChangeSteps.Locate([], 4 * Second));

        // Before the first change: 0 of n, only a next change.
        var before = PropertyChangeSteps.Locate(times, Second);
        Assert.Equal(new PropertyChangePosition(0, 4, null, 2 * Second), before);
        Assert.Equal("0/4", before.Shown);
        Assert.Equal("before the first of 4 changes", before.Spoken);

        // At the first change: 1 of n, nothing earlier.
        Assert.Equal(new PropertyChangePosition(1, 4, null, 5 * Second), PropertyChangeSteps.Locate(times, 2 * Second));

        // After the second change, before the third: 2 of 4; previous goes
        // back to the second, next on to the third.
        var between = PropertyChangeSteps.Locate(times, 7 * Second);
        Assert.Equal(new PropertyChangePosition(2, 4, 5 * Second, 9 * Second), between);
        Assert.Equal("2/4", between.Shown);
        Assert.Equal("change 2 of 4", between.Spoken);

        // At the second change: previous goes to the first.
        Assert.Equal(new PropertyChangePosition(2, 4, 2 * Second, 9 * Second), PropertyChangeSteps.Locate(times, 5 * Second));

        // At and after the last change: n of n, nothing later.
        Assert.Equal(new PropertyChangePosition(4, 4, 9 * Second, null), PropertyChangeSteps.Locate(times, 12 * Second));
        Assert.Equal(new PropertyChangePosition(4, 4, 12 * Second, null), PropertyChangeSteps.Locate(times, 20 * Second));

        // Changes at the same time are one change.
        long[] tied = [2 * Second, 2 * Second, 5 * Second];
        Assert.Equal(new PropertyChangePosition(1, 2, 2 * Second, 5 * Second), PropertyChangeSteps.Locate(tied, 3 * Second));
        Assert.Equal(new PropertyChangePosition(1, 2, null, 5 * Second), PropertyChangeSteps.Locate(tied, 2 * Second));

        // A single change: 0/1 before it, 1/1 at and after it.
        long[] one = [3 * Second];
        var beforeOnly = PropertyChangeSteps.Locate(one, Second);
        Assert.Equal(new PropertyChangePosition(0, 1, null, 3 * Second), beforeOnly);
        Assert.Equal("before its only change", beforeOnly.Spoken);
        Assert.Equal(new PropertyChangePosition(1, 1, null, null), PropertyChangeSteps.Locate(one, 3 * Second));
        Assert.Equal(new PropertyChangePosition(1, 1, 3 * Second, null), PropertyChangeSteps.Locate(one, 4 * Second));
    }

    [Fact]
    public void TheMagnifierRowsAreSetAtTheirChangesAndStepThroughThem()
    {
        var tracker = new MagnifierChangeTracker();
        tracker.Observe(Second, 1, Magnification(1, 0, 0), Effect(Identity));
        var records = new List<(long, JsonElement)>();
        void Frame(long time, ulong sequence, MagnificationReading magnification, ColorEffectReading effect)
        {
            if (tracker.Observe(time, sequence, magnification, effect) is { } payload)
            {
                records.Add((time, Record(time, sequence, payload).Payload));
            }
        }

        Frame(2 * Second, 2, Magnification(2, 0, 0), Effect(Identity));
        Frame(3 * Second, 3, Magnification(2, 0, 0), Effect(Identity));
        Frame(4 * Second, 4, Magnification(2, 100, 0), Effect(Identity));
        Frame(6 * Second, 5, Magnification(3, 100, 0), Effect(Inversion));
        Assert.Equal(3, records.Count);

        var magnifier = new MagnifierChangeTimeline(records);
        Assert.True(magnifier.Recorded);
        Assert.Equal([2 * Second, 6 * Second], magnifier.Times(MagnifierChanges.Level));
        Assert.Equal([4 * Second], magnifier.Times(MagnifierChanges.Position));
        Assert.Equal([6 * Second], magnifier.Times(MagnifierChanges.ColorEffect));

        var frame = new SessionVideoFrame(
            5 * Second, "f.png", "/f.png", 1920, 1080, Magnification: new FullscreenMagnification(2, 100, 0));
        var rows = WindowsPreferenceTimeline.MagnifierRows(frame, magnifier, 5 * Second);
        var level = rows.Single(row => row.Setting == "Full screen level");
        Assert.Equal(2 * Second, level.SetAt);
        Assert.Equal("00:00:02.000", level.WhenSet);
        Assert.Equal(WindowsPreferenceTimeline.MagnifierKeyPrefix + MagnifierChanges.Level, level.Key);
        Assert.Equal("at start", rows.Single(row => row.Setting == "Full screen color effect").WhenSet);
        // Changed means set in the second before the time shown.
        Assert.False(rows.Single(row => row.Setting == "Full screen position").Changed);
        Assert.True(WindowsPreferenceTimeline.MagnifierRows(frame, magnifier, 4 * Second + Second / 2)
            .Single(row => row.Setting == "Full screen position").Changed);

        var times = PropertyChangeSteps.TimesOf(level, WindowsPreferenceTimeline.Empty, magnifier)!;
        Assert.Equal(6 * Second, PropertyChangeSteps.Next(times, 5 * Second));
        Assert.Equal(2 * Second, PropertyChangeSteps.Previous(times, 5 * Second));
        Assert.Null(PropertyChangeSteps.Previous(times, 2 * Second));

        // Without change records the rows are set with the frame, and have none to step through.
        var older = WindowsPreferenceTimeline.MagnifierRows(frame, MagnifierChangeTimeline.Empty, 5 * Second);
        Assert.All(older, row => Assert.Equal("with this frame", row.WhenSet));
        Assert.Null(PropertyChangeSteps.TimesOf(older[0], WindowsPreferenceTimeline.Empty, MagnifierChangeTimeline.Empty));
    }

    [Fact]
    public void TheArchiveKeepsTheRecordsAndSummarizesThem()
    {
        var tracker = new MagnifierChangeTracker();
        tracker.Observe(Second, 1, Magnification(1, 0, 0), Effect(Identity));
        var record = Record(2 * Second, 2, tracker.Observe(2 * Second, 2, Magnification(2, 480, 270), Effect(Inversion))!);

        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        builder.Add(1, record with { EventId = "e1" });
        var index = builder.Build();
        Assert.True(PlaybackIndex.CurrentVersion >= 7);
        var kept = Assert.Single(index.Events, item => item.Channel == MagnifierChanges.Channel);
        Assert.Equal(record.Payload.GetRawText(), kept.Payload.GetRawText());

        var archiveBuilder = new SessionPlaybackArchiveBuilder(Path.GetTempPath(), retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(4), 4 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, true),
            [], [], 1, 0, null));
        Assert.True(archive.MagnifierChanges.Recorded);
        Assert.Equal([2 * Second], archive.MagnifierChanges.Times(MagnifierChanges.Level));
        Assert.True(SessionPlaybackArchiveBuilder.BuildsFrom(MagnifierChanges.Channel));
        Assert.True(SessionPlaybackArchiveBuilder.ReadsPayload(MagnifierChanges.Channel));

        var summary = SessionPlaybackArchiveBuilder.CreateSummary(
            MagnifierChanges.Channel, MagnifierChanges.ChangeEventType, record.Payload);
        Assert.StartsWith("magnifier-changed", summary, StringComparison.Ordinal);
        Assert.Contains("level 200 percent", summary, StringComparison.Ordinal);
        Assert.Contains("position 480, 270", summary, StringComparison.Ordinal);
        Assert.Contains("color effect inverted colors", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSettingsRowsStepThroughTheirOwnChanges()
    {
        static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
        var start = Json("""{"reason":"start","uiSettingsEvents":{},"settings":{"textScaleFactor":{"value":1,"problem":null},"menuFade":{"value":true,"problem":null}}}""");
        JsonElement Change(string setting, string value) =>
            Json($$$"""{"setting":"{{{setting}}}","previous":{"{{{setting}}}":{"value":null,"problem":"x"}},"current":{"{{{setting}}}":{"value":{{{value}}},"problem":null}},"notice":{"kind":"stop","uiAction":null,"area":null,"source":null}}""");
        var timeline = new WindowsPreferenceTimeline(
        [
            new(0, WindowsPreferenceSettings.SnapshotEventType, start),
            new(2 * Second, WindowsPreferenceSettings.ChangeEventType, Change("textScaleFactor", "1.25")),
            new(3 * Second, WindowsPreferenceSettings.ChangeEventType, Change("menuFade", "false")),
            new(5 * Second, WindowsPreferenceSettings.ChangeEventType, Change("textScaleFactor", "1"))
        ]);

        var row = timeline.RowsAt(4 * Second, null).Single(candidate => candidate.Setting == "Text size");
        Assert.Equal("textScaleFactor", row.Key);
        var times = PropertyChangeSteps.TimesOf(row, timeline, MagnifierChangeTimeline.Empty)!;
        Assert.Equal([2 * Second, 5 * Second], times);
        Assert.Equal(2 * Second, PropertyChangeSteps.Previous(times, 4 * Second));
        Assert.Null(PropertyChangeSteps.Previous(times, 2 * Second));
        Assert.Equal(5 * Second, PropertyChangeSteps.Next(times, 4 * Second));
        Assert.Equal(new PropertyChangePosition(1, 2, 2 * Second, 5 * Second), PropertyChangeSteps.Locate(times, 4 * Second));
        Assert.Null(PropertyChangeSteps.TimesOf(row, WindowsPreferenceTimeline.Empty, MagnifierChangeTimeline.Empty));
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Recorder.Contracts;
using Recorder.Database;
using Recorder.Session;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

/// <summary>
/// Playback loaded from the database must match playback loaded from the
/// same recording's session files.
/// </summary>
public sealed class DatabasePlaybackReaderTests(EmbeddedPostgresFixture fixture)
    : IClassFixture<EmbeddedPostgresFixture>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "recorder-playback-" + Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task LoadsTheSamePlaybackAsTheSessionFiles()
    {
        var token = TestContext.Current.CancellationToken;
        var sessionKey = "playback-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_root, sessionKey);
        var events = VariedEvents(sessionKey);
        await WriteSessionFilesAsync(directory, sessionKey, events, token);
        await WriteRecordingAsync(sessionKey, events, RecordingStatus.Completed, token);

        var fromFiles = await SessionArchiveReader.LoadAsync(directory, token);
        var result = await new DatabasePlaybackReader(fixture.Server.DataSource).OpenAsync(directory, token);

        Assert.Null(result.Reason);
        Assert.Equal(RecordingStatus.Completed, result.Status);
        var fromDatabase = Assert.IsType<SessionPlaybackArchive>(result.Archive);
        Assert.NotNull(fromDatabase.RecordSource);
        Assert.Null(fromFiles.RecordSource);
        Assert.Equal(fromFiles.SessionDirectory, fromDatabase.SessionDirectory);
        Assert.Equal(
            JsonSerializer.Serialize(fromFiles.Manifest, JsonOptions),
            JsonSerializer.Serialize(fromDatabase.Manifest, JsonOptions));
        Assert.Equal(fromFiles.DurationNanoseconds, fromDatabase.DurationNanoseconds);
        Assert.Empty(fromDatabase.Events);
        var fileTimeline = await WalkAsync(fromFiles.Timeline, token);
        var databaseTimeline = await WalkAsync(fromDatabase.Timeline, token);
        Assert.Equal(fileTimeline.Select(Shape), databaseTimeline.Select(Shape));
        Assert.Equal(fromFiles.Timeline.Count, fromDatabase.Timeline.Count);
        Assert.Equal(
            fromFiles.Timeline.ChannelCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            fromDatabase.Timeline.ChannelCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(fromFiles.Frames, fromDatabase.Frames);
        Assert.Equal(fromFiles.AudioTracks, fromDatabase.AudioTracks);
        Assert.Equal(fromFiles.BrowserNavigations, fromDatabase.BrowserNavigations);
        Assert.Single(fromDatabase.Frames);
        Assert.Single(fromDatabase.AudioTracks);
        Assert.Single(fromDatabase.BrowserNavigations);
        Assert.Contains(databaseTimeline, item => item.Summary == "session-marker: In the database");

        // Every complete record reads back as the event log's record, with
        // the payload's properties possibly in another order.
        var fileEvents = fromFiles.Events.ToDictionary(item => item.EventId);
        foreach (var item in databaseTimeline)
        {
            Assert.NotNull(item.EventKey);
            Assert.Equal(
                Canonical(fromFiles.ReadEventJson(fileEvents[item.EventId])),
                Canonical(fromDatabase.ReadEventJson(item)));
        }
    }

    [Fact]
    public async Task DoesNotOpenARecordingTheDatabaseDoesNotHoldComplete()
    {
        var token = TestContext.Current.CancellationToken;
        var reader = new DatabasePlaybackReader(fixture.Server.DataSource);

        var missing = await reader.OpenAsync(Path.Combine(_root, "not-in-the-database"), token);
        Assert.Null(missing.Archive);
        Assert.Null(missing.Status);
        Assert.Equal("The database does not hold this recording.", missing.Reason);

        var sessionKey = "interrupted-" + Guid.NewGuid().ToString("N");
        await WriteRecordingAsync(sessionKey, VariedEvents(sessionKey), RecordingStatus.Interrupted, token);
        var interrupted = await reader.OpenAsync(Path.Combine(_root, sessionKey), token);
        Assert.Null(interrupted.Archive);
        Assert.Equal(RecordingStatus.Interrupted, interrupted.Status);
        Assert.Contains("interrupted", interrupted.Reason);
    }

    [Fact]
    public async Task AnswersTimelineLookupsAsTheSessionFilesDo()
    {
        var token = TestContext.Current.CancellationToken;
        var sessionKey = "lookups-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_root, sessionKey);
        string[] channels = ["test.pointer", "accessibility.uia.events", "test.notes"];
        var collector = Collector("test.lookups", channels);
        var random = new Random(1234);
        var time = 0L;
        var sequences = new ulong[channels.Length];
        var events = new List<RecorderEvent>();
        for (var index = 0; index < 3_000; index++)
        {
            // Many events share a time, across and within channels.
            time += random.Next(0, 3) == 0 ? 0 : random.Next(1, 5_000);
            var lane = random.Next(10) == 0 ? 2 : random.Next(2);
            events.Add(Event(sessionKey, collector, sequences[lane]++, time, channels[lane],
                lane == 1 ? "focus-changed" : "marker",
                lane == 1
                    ? Json(EvidenceSamples.Focus($"Control {index}"))
                    : new { name = $"Control {index}", note = $"Note {index}" }));
        }

        await WriteSessionFilesAsync(directory, sessionKey, events, token);
        await WriteRecordingAsync(sessionKey, events, RecordingStatus.Completed, token);
        var fromFiles = (await SessionArchiveReader.LoadAsync(directory, token)).Timeline;
        var fromDatabase = Assert.IsType<SessionPlaybackArchive>(
            (await new DatabasePlaybackReader(fixture.Server.DataSource).OpenAsync(directory, token)).Archive)
            .Timeline;
        Assert.IsType<DatabaseSessionTimeline>(fromDatabase);

        var fileOrder = await WalkAsync(fromFiles, token);
        var databaseOrder = await WalkAsync(fromDatabase, token);
        Assert.Equal(events.Count, databaseOrder.Count);
        Assert.Equal(fileOrder.Select(Shape), databaseOrder.Select(Shape));

        IReadOnlySet<string>[] subsets =
        [
            channels.ToHashSet(),
            new HashSet<string> { channels[0] },
            new HashSet<string> { channels[1], channels[2] },
            new HashSet<string> { channels[2] },
            new HashSet<string>()
        ];
        var duration = time + 1;
        for (var trial = 0; trial < 150; trial++)
        {
            var subset = subsets[trial % subsets.Length];
            var target = random.NextInt64(-10, duration + 10);
            var start = random.NextInt64(0, duration);
            var end = start + random.NextInt64(0, duration / 4);
            var from = random.Next(fileOrder.Count);
            var forward = random.Next(2) == 0;

            Assert.Equal(
                Shape(await fromFiles.AtOrBeforeAsync(target, subset, token)),
                Shape(await fromDatabase.AtOrBeforeAsync(target, subset, token)));
            Assert.Equal(
                Shape(await fromFiles.NearestAsync(target, start, end, subset, token)),
                Shape(await fromDatabase.NearestAsync(target, start, end, subset, token)));
            Assert.Equal(
                Shape(await fromFiles.AdjacentAsync(fileOrder[from], forward, subset, token)),
                Shape(await fromDatabase.AdjacentAsync(databaseOrder[from], forward, subset, token)));
            Assert.Equal(
                Shape(await fromFiles.EndAsync(forward, subset, token)),
                Shape(await fromDatabase.EndAsync(forward, subset, token)));
            Assert.Equal(
                fromFiles.Occupancy.OccupiedColumns(subset, start, end - start + 1, 500),
                fromDatabase.Occupancy.OccupiedColumns(subset, start, end - start + 1, 500));
        }
    }

    // Not a performance test. It records how long the database read and
    // timeline lookups take for a recording of the size seen on Windows, so
    // a change that makes them much slower is visible in the test output.
    [Fact]
    public async Task LoadsALargeRecording()
    {
        var token = TestContext.Current.CancellationToken;
        var sessionKey = "large-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_root, sessionKey);
        var collector = Collector("test.large", "test.pointer", "accessibility.uia.events");
        var padding = new string('x', 2_000);
        var events = new List<RecorderEvent>(100_000);
        for (var index = 0; index < 100_000; index++)
        {
            events.Add(index % 4 == 0
                ? Event(sessionKey, collector, (ulong)index, index * 1_000L, "accessibility.uia.events",
                    "focus-changed", Json(EvidenceSamples.Focus($"Control {index} {padding}")))
                : Event(sessionKey, collector, (ulong)index, index * 1_000L, "test.pointer",
                    "mouse-move", new { x = index, y = index, padding }));
        }

        await WriteSessionFilesAsync(directory, sessionKey, events, token);
        await WriteRecordingAsync(sessionKey, events, RecordingStatus.Completed, token);

        var stopwatch = Stopwatch.StartNew();
        var result = await new DatabasePlaybackReader(fixture.Server.DataSource).OpenAsync(directory, token);
        stopwatch.Stop();

        var archive = Assert.IsType<SessionPlaybackArchive>(result.Archive);
        var timeline = archive.Timeline;
        Assert.Equal(events.Count, timeline.Count);
        Assert.Empty(archive.Events);

        var all = timeline.ChannelCounts.Keys.ToHashSet();
        var uia = new HashSet<string> { "accessibility.uia.events" };
        var lookups = Stopwatch.StartNew();
        for (var index = 0; index < 100; index++)
        {
            var target = index * 997_000L;
            var item = await timeline.AtOrBeforeAsync(target, uia, token);
            Assert.NotNull(item);
            Assert.Equal("accessibility.uia.events", item.Channel);
            Assert.True(item.MonotonicNanoseconds <= target);
            Assert.NotNull(await timeline.NearestAsync(target, 0, archive.DurationNanoseconds, all, token));
        }

        lookups.Stop();
        Assert.Equal(
            "focus-changed",
            (await timeline.AtOrBeforeAsync(4_500, uia, token))?.Summary);
        TestContext.Current.SendDiagnosticMessage(
            $"Opened {timeline.Count:N0} events from the database in {stopwatch.ElapsedMilliseconds:N0} ms; " +
            $"200 lookups took {lookups.ElapsedMilliseconds:N0} ms.");
    }

    private static List<RecorderEvent> VariedEvents(string sessionKey)
    {
        var collector = Collector(
            "test.varied",
            "test.windows",
            "accessibility.uia.events",
            "graphics.desktop.frames",
            "audio.microphone",
            "browser.navigation",
            "session.annotations",
            "test.keys");
        var analyzer = Collector("test.analyzer", "analysis.test");
        var keyboard = Event(sessionKey, collector, 0, 700, "test.keys", "key-down",
            new { virtualKey = 65, scanCode = 30, text = "a" }, "flag-one", "flag-two") with
        {
            NativeTimestamp = new NativeTimestamp("raw-input", 123_456_789, "milliseconds"),
            TimestampUncertaintyNanoseconds = 15_600_000
        };
        return
        [
            Event(sessionKey, collector, 0, 100, "test.windows", "foreground-changed",
                new { title = "Editor", processName = "notepad", processId = 42, bounds = new { x = 1, y = 2 } }),
            Event(sessionKey, collector, 0, 200, "accessibility.uia.events", "focus-changed",
                Json(EvidenceSamples.Focus("Save"))),
            Event(sessionKey, collector, 0, 300, "graphics.desktop.frames", "desktop-frame",
                Json(EvidenceSamples.DesktopFrame("frames/desktop/0000000000.png"))),
            Event(sessionKey, collector, 0, 400, "audio.microphone", "audio-stream-started",
                Json(EvidenceSamples.AudioStarted("microphone", "audio/microphone.wav"))),
            Event(sessionKey, collector, 0, 500, "browser.navigation", "navigation-started",
                Json(EvidenceSamples.Sample("browser.navigation", "navigation-started"))),
            Event(sessionKey, collector, 1, 550, "browser.navigation", "navigation-completed",
                Json(EvidenceSamples.Sample("browser.navigation", "navigation-completed"))),
            Event(sessionKey, collector, 0, 600, "session.annotations", "session-marker",
                new { note = "In the database" }),
            keyboard,
            AnalysisEventFactory.CreateDerived(
                sessionKey,
                analyzer,
                "analysis.test",
                0,
                800,
                "test-analysis",
                new { result = 1.5 },
                [keyboard.EventId],
                "test-method")
        ];
    }

    private static async Task WriteSessionFilesAsync(
        string directory,
        string sessionKey,
        IReadOnlyList<RecorderEvent> events,
        CancellationToken token)
    {
        Directory.CreateDirectory(Path.Combine(directory, "frames", "desktop"));
        Directory.CreateDirectory(Path.Combine(directory, "audio"));
        await File.WriteAllBytesAsync(Path.Combine(directory, "frames", "desktop", "0000000000.png"), [1, 2, 3], token);
        await File.WriteAllBytesAsync(Path.Combine(directory, "audio", "microphone.wav"), [4, 5, 6], token);
        var started = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        var manifest = new SessionManifest(
            "1.1",
            sessionKey,
            "completed",
            started,
            started.AddSeconds(2),
            2_000_000_000,
            10_000_000,
            1,
            "Windows",
            ".NET",
            "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, false),
            [],
            [],
            events.Count,
            0,
            null);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions),
            token);
        var log = new StringBuilder();
        foreach (var record in events)
        {
            log.Append(JsonSerializer.Serialize(record, JsonOptions)).Append('\n');
        }

        await File.WriteAllTextAsync(Path.Combine(directory, "events.ndjson"), log.ToString(), token);
    }

    private async Task WriteRecordingAsync(
        string sessionKey,
        IReadOnlyList<RecorderEvent> events,
        RecordingStatus status,
        CancellationToken token)
    {
        var dataSource = fixture.Server.DataSource;
        var store = new RecordingStore(dataSource);
        var projectId = await store.EnsureProjectAsync("Playback", token);
        var recordingId = await store.CreateRecordingAsync(projectId, Definition(sessionKey), token);
        var writer = new PostgresEventWriter(
            new PostgresEventBatchTarget(dataSource, recordingId),
            sessionKey,
            new PostgresEventWriterOptions
            {
                SpillPath = Path.Combine(_root, sessionKey + ".spill.ndjson"),
                ChannelCapacity = events.Count
            });
        await using (writer)
        {
            foreach (var record in events)
            {
                Assert.True(writer.TryWrite(record));
            }

            var written = await writer.CompleteAsync();
            Assert.Equal(events.Count, written.WrittenCount);
            Assert.Equal(0, written.RejectedCount);
        }

        await store.CompleteRecordingAsync(
            recordingId,
            new RecordingCompletion(status, DateTimeOffset.UtcNow, 2_000_000_000, events.Count, 0, null),
            token);
    }

    // Every event, in timeline order, by stepping from the first.
    private static async Task<List<SessionTimelineEvent>> WalkAsync(
        ISessionTimeline timeline,
        CancellationToken token)
    {
        var channels = timeline.ChannelCounts.Keys.ToHashSet();
        var items = new List<SessionTimelineEvent>();
        for (var item = await timeline.EndAsync(last: false, channels, token);
             item is not null;
             item = await timeline.AdjacentAsync(item, forward: true, channels, token))
        {
            items.Add(item);
        }

        return items;
    }

    private static (string, string, string, string, long, string)? Shape(SessionTimelineEvent? item) =>
        item is null
            ? null
            : (item.EventId, item.EvidenceClass, item.Channel, item.EventType, item.MonotonicNanoseconds, item.Summary);

    // The JSON with every object's properties in name order.
    private static string Canonical(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(document.RootElement, writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());

        static void Write(JsonElement value, Utf8JsonWriter writer)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(property.Name);
                        Write(property.Value, writer);
                    }

                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in value.EnumerateArray())
                    {
                        Write(item, writer);
                    }

                    writer.WriteEndArray();
                    break;
                default:
                    value.WriteTo(writer);
                    break;
            }
        }
    }
}

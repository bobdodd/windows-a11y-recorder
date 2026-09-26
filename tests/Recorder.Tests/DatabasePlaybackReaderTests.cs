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
        Assert.Equal(Timeline(fromFiles), Timeline(fromDatabase));
        Assert.Equal(fromFiles.Frames, fromDatabase.Frames);
        Assert.Equal(fromFiles.AudioTracks, fromDatabase.AudioTracks);
        Assert.Equal(fromFiles.BrowserNavigations, fromDatabase.BrowserNavigations);
        Assert.Single(fromDatabase.Frames);
        Assert.Single(fromDatabase.AudioTracks);
        Assert.Single(fromDatabase.BrowserNavigations);
        Assert.Contains(fromDatabase.Events, item => item.Summary == "marker: In the database");

        // Every complete record reads back as the event log's record, with
        // the payload's properties possibly in another order.
        var fileEvents = fromFiles.Events.ToDictionary(item => item.EventId);
        foreach (var item in fromDatabase.Events)
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

    // Not a performance test. It records how long the database read takes
    // for a recording of the size seen on Windows, so a change that makes it
    // much slower is visible in the test output.
    [Fact]
    public async Task LoadsALargeRecording()
    {
        var token = TestContext.Current.CancellationToken;
        var sessionKey = "large-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_root, sessionKey);
        var collector = Collector("test.large", "input.mouse", "accessibility.uia.events");
        var padding = new string('x', 2_000);
        var events = new List<RecorderEvent>(100_000);
        for (var index = 0; index < 100_000; index++)
        {
            events.Add(index % 4 == 0
                ? Event(sessionKey, collector, (ulong)index, index * 1_000L, "accessibility.uia.events",
                    "focus-changed", new { name = $"Control {index}", controlType = "Button", padding })
                : Event(sessionKey, collector, (ulong)index, index * 1_000L, "input.mouse",
                    "mouse-move", new { x = index, y = index, padding }));
        }

        await WriteSessionFilesAsync(directory, sessionKey, events, token);
        await WriteRecordingAsync(sessionKey, events, RecordingStatus.Completed, token);

        var stopwatch = Stopwatch.StartNew();
        var result = await new DatabasePlaybackReader(fixture.Server.DataSource).OpenAsync(directory, token);
        stopwatch.Stop();

        var archive = Assert.IsType<SessionPlaybackArchive>(result.Archive);
        Assert.Equal(events.Count, archive.Events.Count);
        Assert.Contains(archive.Events, item => item.Summary == "focus-changed: Control 4, Button");
        TestContext.Current.SendDiagnosticMessage(
            $"Loaded {archive.Events.Count:N0} events from the database in {stopwatch.ElapsedMilliseconds:N0} ms.");
    }

    private static List<RecorderEvent> VariedEvents(string sessionKey)
    {
        var collector = Collector(
            "test.varied",
            "window.foreground",
            "accessibility.uia.events",
            "graphics.desktop.frames",
            "audio.microphone",
            "browser.navigation",
            "session.annotations",
            "input.keyboard");
        var analyzer = Collector("test.analyzer", "analysis.test");
        var context = new
        {
            browserInstanceId = "browser-1",
            processId = 1,
            processType = "browser",
            documentId = "document-1",
            documentToken = "TOKEN-1",
            frameId = "frame-1"
        };
        var keyboard = Event(sessionKey, collector, 0, 700, "input.keyboard", "key-down",
            new { virtualKey = 65, scanCode = 30, text = "a" }, "flag-one", "flag-two") with
        {
            NativeTimestamp = new NativeTimestamp("raw-input", 123_456_789, "milliseconds"),
            TimestampUncertaintyNanoseconds = 15_600_000
        };
        return
        [
            Event(sessionKey, collector, 0, 100, "window.foreground", "foreground-changed",
                new { title = "Editor", processName = "notepad", processId = 42, bounds = new { x = 1, y = 2 } }),
            Event(sessionKey, collector, 0, 200, "accessibility.uia.events", "focus-changed",
                new { name = "Save", automationId = "SaveButton", controlType = "Button", isEnabled = true }),
            Event(sessionKey, collector, 0, 300, "graphics.desktop.frames", "desktop-frame",
                new { path = "frames/desktop/0000000000.png", width = 1920, height = 1080, byteLength = 3 }),
            Event(sessionKey, collector, 0, 400, "audio.microphone", "audio-stream-started",
                new { stream = "microphone", path = "audio/microphone.wav", device = "Test microphone" }),
            Event(sessionKey, collector, 0, 500, "browser.navigation", "navigation-started",
                new { context, navigationId = "navigation-1", url = "https://example.test/", primaryPage = true, sameDocument = false }),
            Event(sessionKey, collector, 1, 550, "browser.navigation", "navigation-completed",
                new
                {
                    context,
                    navigationId = "navigation-1",
                    url = "https://example.test/",
                    primaryPage = true,
                    sameDocument = false,
                    committed = true,
                    outcome = "committed",
                    rendererProcessId = 20
                }),
            Event(sessionKey, collector, 0, 600, "session.annotations", "marker",
                new { note = "In the database", text = (string?)null }),
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

    private static List<(string, string, string, string, long, string)> Timeline(SessionPlaybackArchive archive) =>
        archive.Events
            .Select(item => (
                item.EventId,
                item.EvidenceClass,
                item.Channel,
                item.EventType,
                item.MonotonicNanoseconds,
                item.Summary))
            .OrderBy(item => item.EventId, StringComparer.Ordinal)
            .ToList();

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

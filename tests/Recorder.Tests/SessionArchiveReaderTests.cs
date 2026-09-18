using System.Text.Json;
using Recorder.Session;

namespace Recorder.Tests;

public sealed class SessionArchiveReaderTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task LoadsFramesAudioAndLegacyEvents()
    {
        var directory = CreateDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "frames", "desktop"));
            Directory.CreateDirectory(Path.Combine(directory, "audio"));
            await File.WriteAllBytesAsync(
                Path.Combine(directory, "frames", "desktop", "0000000000.png"),
                [1, 2, 3],
                TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(directory, "audio", "system.wav"),
                [4, 5, 6],
                TestContext.Current.CancellationToken);
            await WriteManifestAsync(directory, 2_000_000_000);
            await WriteEventsAsync(
                directory,
                [
                    CreateEvent(
                        "graphics.desktop.frames",
                        "desktop-frame",
                        500_000_000,
                        new
                        {
                            path = "frames/desktop/0000000000.png",
                            width = 1920,
                            height = 1080
                        }),
                    CreateEvent(
                        "audio.system",
                        "audio-stream-started",
                        600_000_000,
                        new
                        {
                            stream = "system",
                            path = "audio/system.wav",
                            device = "test"
                        })
                ]);

            var archive = await SessionArchiveReader.LoadAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.Equal(2_000_000_000, archive.DurationNanoseconds);
            var frame = Assert.Single(archive.Frames);
            Assert.Equal(500_000_000, frame.MonotonicNanoseconds);
            Assert.Equal(1920, frame.Width);
            Assert.True(Path.IsPathFullyQualified(frame.AbsolutePath));
            var audio = Assert.Single(archive.AudioTracks);
            Assert.Equal("system", audio.Stream);
            Assert.Equal(600_000_000, audio.StartNanoseconds);
            Assert.All(
                archive.Events,
                item => Assert.StartsWith("test-session:", item.EventId));
            Assert.All(
                archive.Events,
                item => Assert.Contains("\"channel\":", item.RawJson));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task IgnoresMediaPathsThatEscapeTheArchive()
    {
        var directory = CreateDirectory();
        try
        {
            await WriteManifestAsync(directory, 1_000_000_000);
            await WriteEventsAsync(
                directory,
                [
                    CreateEvent(
                        "graphics.desktop.frames",
                        "desktop-frame",
                        100,
                        new
                        {
                            path = "../outside.png",
                            width = 10,
                            height = 10
                        })
                ]);

            var archive = await SessionArchiveReader.LoadAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.Empty(archive.Frames);
            Assert.Single(archive.Events);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task WriteManifestAsync(string directory, long duration)
    {
        var started = DateTimeOffset.UtcNow.AddSeconds(-2);
        var manifest = new SessionManifest(
            "1.1",
            "test-session",
            "completed",
            started,
            started.AddSeconds(2),
            duration,
            10_000_000,
            1,
            "Windows",
            ".NET",
            "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, true),
            [],
            [],
            2,
            0,
            null);
        await SessionManifestWriter.WriteAsync(
            Path.Combine(directory, "manifest.json"),
            manifest,
            TestContext.Current.CancellationToken);
    }

    private static async Task WriteEventsAsync(
        string directory,
        IReadOnlyList<object> events)
    {
        await using var writer = new StreamWriter(
            Path.Combine(directory, "events.ndjson"));
        foreach (var item in events)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(item, JsonOptions));
        }
    }

    private static object CreateEvent(
        string channel,
        string eventType,
        long timestamp,
        object payload) =>
        new
        {
            schemaVersion = "1.0",
            sessionId = "test-session",
            collectorType = "test.collector",
            collectorInstanceId = "0123456789abcdef0123456789abcdef",
            channel,
            captureMethod = "test",
            sequence = timestamp,
            monotonicNanoseconds = timestamp,
            observedUtc = DateTimeOffset.UtcNow,
            eventType,
            payload,
            qualityFlags = Array.Empty<string>()
        };
}

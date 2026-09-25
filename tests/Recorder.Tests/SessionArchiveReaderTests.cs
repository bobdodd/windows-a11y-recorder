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

            var archive = await LoadBothWaysAsync(directory);

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

            var archive = await LoadBothWaysAsync(directory);

            Assert.Empty(archive.Frames);
            Assert.Single(archive.Events);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CorrelatesNavigationWithRendererDocumentEvidence()
    {
        var directory = CreateDirectory();
        try
        {
            await WriteManifestAsync(directory, 1_000);
            var browserContext = new
            {
                browserInstanceId = "browser-1",
                processId = 1,
                processType = "browser",
                documentId = "document-navigation-1",
                documentToken = "TOKEN-1",
                frameId = "frame-1"
            };
            var rendererContext = new
            {
                browserInstanceId = "browser-1",
                processId = 20,
                processType = "renderer",
                documentId = "dom-document-1",
                documentToken = "TOKEN-1",
                frameId = "frame-1"
            };
            var rendererContextWithoutToken = new
            {
                browserInstanceId = "browser-1",
                processId = 20,
                processType = "renderer",
                documentId = "dom-document-1",
                documentToken = (string?)null,
                frameId = "frame-1"
            };
            await WriteEventsAsync(
                directory,
                [
                    CreateEvent(
                        "browser.navigation",
                        "navigation-started",
                        100,
                        new
                        {
                            context = browserContext,
                            navigationId = "navigation-1",
                            url = "https://example.test/",
                            primaryPage = true,
                            sameDocument = false
                        }),
                    CreateEvent(
                        "browser.navigation",
                        "navigation-completed",
                        150,
                        new
                        {
                            context = browserContext,
                            navigationId = "navigation-1",
                            url = "https://example.test/",
                            primaryPage = true,
                            sameDocument = false,
                            committed = true,
                            outcome = "committed",
                            rendererProcessId = 20
                        }),
                    CreateEvent(
                        "browser.dom",
                        "dom-checkpoint-node",
                        200,
                        new
                        {
                            context = rendererContext,
                            checkpointId = "checkpoint-1",
                            nodeId = 1
                        }),
                    CreateEvent(
                        "browser.dom",
                        "dom-checkpoint-completed",
                        210,
                        new
                        {
                            context = rendererContext,
                            checkpointId = "checkpoint-1",
                            nodeCount = 1,
                            truncated = true
                        }),
                    CreateEvent(
                        "browser.accessibility",
                        "accessibility-checkpoint-started",
                        212,
                        new
                        {
                            context = rendererContext,
                            checkpointId = "accessibility-checkpoint-1"
                        }),
                    CreateEvent(
                        "browser.accessibility",
                        "accessibility-checkpoint-node",
                        214,
                        new
                        {
                            context = rendererContext,
                            checkpointId = "accessibility-checkpoint-1",
                            accessibilityNodeId = 7
                        }),
                    CreateEvent(
                        "browser.accessibility",
                        "accessibility-checkpoint-completed",
                        216,
                        new
                        {
                            context = rendererContext,
                            checkpointId = "accessibility-checkpoint-1",
                            nodeCount = 1,
                            truncated = true
                        }),
                    CreateEvent(
                        "browser.dispatch",
                        "dispatch-started",
                        220,
                        new
                        {
                            context = rendererContextWithoutToken,
                            dispatchId = "dispatch-1",
                            eventName = "click"
                        }),
                    CreateEvent(
                        "browser.listener",
                        "listener-invoked",
                        230,
                        new
                        {
                            context = rendererContextWithoutToken,
                            listenerId = "listener-1",
                            eventName = "click"
                        })
                ]);

            var archive = await LoadBothWaysAsync(directory);

            var navigation = Assert.Single(archive.BrowserNavigations);
            Assert.Equal("https://example.test/", navigation.Url);
            Assert.Equal(
                "document token and renderer document identity",
                navigation.CorrelationBasis);
            Assert.Equal(1, navigation.CheckpointCount);
            Assert.Equal(1, navigation.DomNodeCount);
            Assert.Equal(1, navigation.TruncatedCheckpointCount);
            Assert.Equal(1, navigation.AccessibilityCheckpointCount);
            Assert.Equal(1, navigation.AccessibilityNodeCount);
            Assert.Equal(
                1,
                navigation.TruncatedAccessibilityCheckpointCount);
            Assert.Equal(1, navigation.DispatchCount);
            Assert.Equal(1, navigation.ListenerInvocationCount);
            Assert.Equal(9, navigation.RelatedEventCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentFrameNavigationDoesNotEndAnotherFrameCorrelation()
    {
        var directory = CreateDirectory();
        try
        {
            await WriteManifestAsync(directory, 1_000);
            await WriteEventsAsync(
                directory,
                [
                    CreateEvent(
                        "browser.navigation",
                        "navigation-started",
                        100,
                        new
                        {
                            context = new
                            {
                                browserInstanceId = "browser-1",
                                processId = 1,
                                processType = "browser",
                                frameId = "frame-1",
                                documentToken = "TOKEN-1"
                            },
                            navigationId = "navigation-1",
                            url = "chrome://surface-one/",
                            primaryPage = true
                        }),
                    CreateEvent(
                        "browser.navigation",
                        "navigation-completed",
                        200,
                        new
                        {
                            context = new
                            {
                                browserInstanceId = "browser-1",
                                processId = 1,
                                processType = "browser",
                                frameId = "frame-1",
                                documentToken = "TOKEN-1"
                            },
                            navigationId = "navigation-1",
                            url = "chrome://surface-one/",
                            primaryPage = true,
                            committed = true
                        }),
                    CreateEvent(
                        "browser.navigation",
                        "navigation-started",
                        150,
                        new
                        {
                            context = new
                            {
                                browserInstanceId = "browser-1",
                                processId = 1,
                                processType = "browser",
                                frameId = "frame-2",
                                documentToken = "TOKEN-2"
                            },
                            navigationId = "navigation-2",
                            url = "chrome://surface-two/",
                            primaryPage = true
                        }),
                    CreateEvent(
                        "browser.dom",
                        "dom-checkpoint-completed",
                        300,
                        new
                        {
                            context = new
                            {
                                browserInstanceId = "browser-1",
                                processId = 20,
                                processType = "renderer",
                                frameId = "frame-1",
                                documentId = "dom-document-1",
                                documentToken = "TOKEN-1"
                            },
                            checkpointId = "checkpoint-1",
                            nodeCount = 512,
                            truncated = true
                        })
                ]);

            var archive = await LoadBothWaysAsync(directory);

            var navigation = Assert.Single(
                archive.BrowserNavigations,
                item => item.NavigationId == "navigation-1");
            Assert.Equal(1, navigation.CheckpointCount);
            Assert.Equal(1, navigation.TruncatedCheckpointCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ValidatorReadReportsUnreadableEventLine()
    {
        var directory = CreateDirectory();
        try
        {
            await WriteManifestAsync(directory, 1_000_000_000);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "events.ndjson"),
                "{\"channel\":\n",
                TestContext.Current.CancellationToken);
            var playback = new SessionPlaybackArchiveBuilder(directory);

            var validation = await SessionArchiveValidator.ValidateAsync(
                directory,
                ArchiveValidationOptions.Default,
                playback,
                TestContext.Current.CancellationToken);

            Assert.Contains(
                validation.Issues,
                issue => issue.Code == "event-json-invalid");
            await Assert.ThrowsAsync<InvalidDataException>(
                () => playback.BuildAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Loads the archive with the reader's own read and with the validator's
    // read, asserts the two playback archives are the same, and returns the
    // reader's archive.
    private static async Task<SessionPlaybackArchive> LoadBothWaysAsync(
        string directory)
    {
        var loaded = await SessionArchiveReader.LoadAsync(
            directory,
            TestContext.Current.CancellationToken);
        var playback = new SessionPlaybackArchiveBuilder(directory);
        await SessionArchiveValidator.ValidateAsync(
            directory,
            ArchiveValidationOptions.Default,
            playback,
            TestContext.Current.CancellationToken);
        var built = await playback.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(loaded.SessionDirectory, built.SessionDirectory);
        Assert.Equal(loaded.DurationNanoseconds, built.DurationNanoseconds);
        Assert.Equal(loaded.Events, built.Events);
        Assert.Equal(loaded.Frames, built.Frames);
        Assert.Equal(loaded.AudioTracks, built.AudioTracks);
        Assert.Equal(
            JsonSerializer.Serialize(loaded.BrowserNavigations, JsonOptions),
            JsonSerializer.Serialize(built.BrowserNavigations, JsonOptions));
        Assert.Equal(
            JsonSerializer.Serialize(loaded.Manifest, JsonOptions),
            JsonSerializer.Serialize(built.Manifest, JsonOptions));
        return loaded;
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

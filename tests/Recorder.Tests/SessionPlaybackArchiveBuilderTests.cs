using System.Text.Json;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// Building a playback archive from events as a store reads them: frames,
/// audio tracks, and browser navigation correlation.
/// </summary>
public sealed class SessionPlaybackArchiveBuilderTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly List<string> _events = [];

    [Fact]
    public async Task LoadsFramesAndAudio()
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

            var archive = await LoadAsync(directory);

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
                item => Assert.Contains("\"channel\":", archive.ReadEventJson(item)));
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

            var archive = await LoadAsync(directory);

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

            var archive = await LoadAsync(directory);

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

            var archive = await LoadAsync(directory);

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
    public async Task ClassifiesNavigationsByFrameTypeAndBrowserInterface()
    {
        var directory = CreateDirectory();
        try
        {
            await WriteManifestAsync(directory, 1_000);
            object Started(string navigationId, string frameId, string? frameType, string url) => new
            {
                context = new
                {
                    browserInstanceId = "browser-1",
                    processId = 1,
                    processType = "browser",
                    frameId
                },
                frameType,
                navigationId,
                url,
                primaryPage = true
            };
            await WriteEventsAsync(
                directory,
                [
                    CreateEvent(
                        "browser.navigation",
                        "navigation-started",
                        100,
                        Started("navigation-1", "frame-1", "primary-main-frame", "https://example.test/")),
                    CreateEvent(
                        "browser.navigation",
                        "navigation-started",
                        200,
                        Started("navigation-2", "frame-2", "subframe", "https://video.example.test/embed")),
                    CreateEvent(
                        "browser.navigation",
                        "navigation-started",
                        300,
                        Started("navigation-3", "frame-3", null, "https://legacy.example.test/")),
                    CreateEvent(
                        "browser.navigation",
                        "navigation-started",
                        400,
                        Started("navigation-4", "frame-4", "primary-main-frame", "chrome://omnibox-popup.top-chrome/omnibox_popup_aim.html")),
                    CreateEvent(
                        "browser.navigation",
                        "navigation-started",
                        500,
                        Started("navigation-5", "frame-5", "primary-main-frame", "chrome://settings/")),
                    CreateEvent(
                        "browser.navigation",
                        "navigation-started",
                        600,
                        Started("navigation-6", "frame-6", "prerender-main-frame", "https://next.example.test/"))
                ]);

            var archive = await LoadAsync(directory);

            var page = Assert.Single(archive.BrowserNavigations, item => item.NavigationId == "navigation-1");
            var iframe = Assert.Single(archive.BrowserNavigations, item => item.NavigationId == "navigation-2");
            var legacy = Assert.Single(archive.BrowserNavigations, item => item.NavigationId == "navigation-3");
            Assert.Equal("primary-main-frame", page.FrameType);
            Assert.True(page.IsPageNavigation);
            Assert.Equal("subframe", iframe.FrameType);
            Assert.True(iframe.PrimaryPage);
            Assert.False(iframe.IsPageNavigation);
            Assert.Null(legacy.FrameType);
            Assert.True(legacy.IsPageNavigation);
            var kinds = archive.BrowserNavigations.ToDictionary(item => item.NavigationId, item => item.Kind);
            Assert.Equal(BrowserNavigationKind.Page, kinds["navigation-1"]);
            Assert.Equal(BrowserNavigationKind.Iframe, kinds["navigation-2"]);
            Assert.Equal(BrowserNavigationKind.Page, kinds["navigation-3"]);
            Assert.Equal(BrowserNavigationKind.BrowserUi, kinds["navigation-4"]);
            Assert.Equal(BrowserNavigationKind.Page, kinds["navigation-5"]);
            Assert.Equal(BrowserNavigationKind.OtherFrame, kinds["navigation-6"]);
            Assert.Equal("00:00:00.000 | [Iframe] https://video.example.test/embed", iframe.Label);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Adds the events written for the test to a builder, as the database
    // reader adds the events it reads, and builds the archive.
    private async Task<SessionPlaybackArchive> LoadAsync(string directory)
    {
        var builder = new SessionPlaybackArchiveBuilder(directory);
        var records = new List<string>();
        foreach (var json in _events)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            builder.AddEvent(
                records.Count + 1,
                root.GetProperty("eventId").GetString()!,
                root.GetProperty("evidenceClass").GetString()!,
                root.GetProperty("channel").GetString()!,
                root.GetProperty("eventType").GetString()!,
                root.GetProperty("monotonicNanoseconds").GetInt64(),
                root.GetProperty("payload").Clone());
            records.Add(json);
        }

        return await builder.BuildAsync(
            new RecordList(records),
            TestContext.Current.CancellationToken);
    }

    private sealed class RecordList(IReadOnlyList<string> records) : ISessionEventRecordSource
    {
        public string ReadEventJson(SessionTimelineEvent item) =>
            records[(int)item.EventKey - 1];
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

    private Task WriteEventsAsync(
        string directory,
        IReadOnlyList<object> events)
    {
        Assert.True(Directory.Exists(directory));
        _events.AddRange(events.Select(item => JsonSerializer.Serialize(item, JsonOptions)));
        return Task.CompletedTask;
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
            eventId = $"test-session:{channel}:{timestamp}",
            evidenceClass = "observed",
            sequence = timestamp,
            monotonicNanoseconds = timestamp,
            observedUtc = DateTimeOffset.UtcNow,
            eventType,
            payload,
            qualityFlags = Array.Empty<string>()
        };
}

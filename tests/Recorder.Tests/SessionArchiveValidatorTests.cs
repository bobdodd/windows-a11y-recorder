using System.Security.Cryptography;
using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

public sealed class SessionArchiveValidatorTests
{
    [Fact]
    public async Task AcceptsValidTerminalArchive()
    {
        var directory = await CreateArchiveAsync(
            [CreateEvent(0, 100), CreateEvent(1, 200)]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Equal(2, result.EventsValidated);
            Assert.Equal(1, result.ArtifactsValidated);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DetectsChangedArtifactBytes()
    {
        var directory = await CreateArchiveAsync([CreateEvent(0, 100)]);
        try
        {
            await File.AppendAllTextAsync(
                Path.Combine(directory, "events.ndjson"),
                "{}\n",
                TestContext.Current.CancellationToken);

            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "artifact-size-mismatch");
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "artifact-hash-mismatch");
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "event-count-mismatch");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DetectsSequenceAndTimestampRegression()
    {
        var directory = await CreateArchiveAsync(
            [CreateEvent(2, 200), CreateEvent(1, 100)]);
        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "event-sequence-not-increasing");
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "event-time-regressed");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DetectsUnsafeAndUnlistedArtifactPaths()
    {
        var directory = await CreateArchiveAsync([CreateEvent(0, 100)]);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "unexpected.txt"),
                "not declared",
                TestContext.Current.CancellationToken);
            var manifestPath = Path.Combine(directory, "manifest.json");
            var manifest = JsonSerializer.Deserialize<SessionManifest>(
                await File.ReadAllTextAsync(
                    manifestPath,
                    TestContext.Current.CancellationToken),
                JsonOptions)!;
            var unsafeArtifact = new SessionArtifact("../outside.bin", 0, new string('0', 64));
            await SessionManifestWriter.WriteAsync(
                manifestPath,
                manifest with { Artifacts = [.. manifest.Artifacts, unsafeArtifact] },
                TestContext.Current.CancellationToken);

            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "artifact-path-unsafe");
            Assert.Contains(
                result.Issues,
                issue =>
                    issue.Code == "artifact-not-declared" &&
                    issue.Path == "unexpected.txt");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DetectsInvalidBuiltInChannelPayload()
    {
        var record = CreateEvent(
            0,
            100,
            "input.keyboard",
            "raw-keyboard",
            new
            {
                deviceHandle = 1,
                makeCode = 30,
                flags = 0,
                virtualKey = 65,
                message = 256
            });
        var directory = await CreateArchiveAsync([record]);
        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue =>
                    issue.Code == "payload-property-missing" &&
                    issue.Path.EndsWith("/extraInformation", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DetectsUnknownEventOnBuiltInChannel()
    {
        var record = CreateEvent(
            0,
            100,
            "input.mouse",
            "future-mouse-event",
            new { value = 1 });
        var directory = await CreateArchiveAsync([record]);
        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "event-type-unsupported");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsInstrumentedBrowserListenerEvidence()
    {
        var context = new
        {
            browserInstanceId = "browser-1",
            processId = 1200,
            processType = "renderer",
            profileId = "test-profile",
            browserContextId = "context-1",
            pageId = "page-1",
            frameId = "frame-1",
            documentId = "document-1",
            executionWorldId = "main"
        };
        var target = new
        {
            documentId = "document-1",
            nodeId = 42,
            backendNodeId = "blink-node-42",
            tagName = "div",
            elementId = "custom-button",
            classes = new[] { "button" }
        };
        var location = new
        {
            scriptId = "script-2",
            url = "https://example.test/app.js",
            line = 18,
            column = 4,
            functionName = "activate",
            sourceHash = "sha256:test"
        };
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            new
            {
                context,
                listenerId = "listener-7",
                eventName = "click",
                registrationKind = "add-event-listener",
                target,
                capture = false,
                passive = false,
                once = false,
                location
            });
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsInstrumentedBrowserDispatchStartEvidence()
    {
        var context = new
        {
            browserInstanceId = "browser-1",
            processId = 1200,
            processType = "renderer",
            profileId = (string?)null,
            browserContextId = (string?)null,
            pageId = (string?)null,
            frameId = (string?)null,
            documentId = "dom-document-8",
            executionWorldId = (string?)null
        };
        var target = new
        {
            documentId = "dom-document-8",
            nodeId = 42,
            backendNodeId = (string?)null,
            tagName = "DIV",
            elementId = "pointer-only",
            classes = Array.Empty<string>()
        };
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Dispatch,
            BrowserEvidenceEventTypes.DispatchStarted,
            new
            {
                context,
                dispatchId = "dispatch-1",
                eventName = "click",
                trusted = false,
                originalTarget = target,
                composedPath = Array.Empty<object>(),
                phase = "none",
                listenerId = (string?)null,
                defaultPrevented = false,
                propagationStopped = false,
                immediatePropagationStopped = false,
                defaultAction = (string?)null,
                outcome = (string?)null
            });
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsInstrumentedBrowserDefaultActionEvidence()
    {
        var context = new
        {
            browserInstanceId = "browser-1",
            processId = 1200,
            processType = "renderer",
            profileId = (string?)null,
            browserContextId = (string?)null,
            pageId = (string?)null,
            frameId = (string?)null,
            documentId = "dom-document-8",
            executionWorldId = (string?)null
        };
        var target = new
        {
            documentId = "dom-document-8",
            nodeId = 42,
            backendNodeId = (string?)null,
            tagName = "A",
            elementId = "default-action-link",
            classes = Array.Empty<string>()
        };
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Dispatch,
            BrowserEvidenceEventTypes.DefaultAction,
            new
            {
                context,
                dispatchId = "dispatch-1",
                eventName = "click",
                trusted = false,
                originalTarget = target,
                composedPath = new object[] { target },
                phase = "none",
                listenerId = (string?)null,
                defaultPrevented = false,
                propagationStopped = false,
                immediatePropagationStopped = false,
                defaultAction = "blink-default-event-handler",
                outcome = "invoked",
                currentTarget = target
            });
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsInstrumentedBrowserTimerEvidence()
    {
        var context = new
        {
            browserInstanceId = "browser-1",
            processId = 1200,
            processType = "renderer",
            profileId = (string?)null,
            browserContextId = (string?)null,
            pageId = (string?)null,
            frameId = (string?)null,
            documentId = "dom-document-8",
            executionWorldId = (string?)null
        };
        var scheduled = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Timer,
            BrowserEvidenceEventTypes.TimerScheduled,
            new
            {
                context,
                timerId = "timer-1",
                timerKind = "timeout",
                requestedDelayMilliseconds = 250.0,
                effectiveDelayMilliseconds = 250.0,
                nestingLevel = 1,
                throttled = (bool?)null,
                pageLifecycleState = "unknown",
                callbackLocation = (object?)null,
                cancellationReason = (string?)null
            });
        var fired = CreateEvent(
            1,
            350,
            BrowserEvidenceChannels.Timer,
            BrowserEvidenceEventTypes.TimerFired,
            new
            {
                context,
                timerId = "timer-1",
                timerKind = "timeout",
                requestedDelayMilliseconds = 250.0,
                effectiveDelayMilliseconds = 250.0,
                nestingLevel = 1,
                throttled = (bool?)null,
                pageLifecycleState = "unknown",
                callbackLocation = (object?)null,
                cancellationReason = (string?)null
            });
        var directory = await CreateArchiveAsync([scheduled, fired]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsInstrumentedBrowserAnimationFrameEvidence()
    {
        var context = new
        {
            browserInstanceId = "browser-1",
            processId = 1200,
            processType = "renderer",
            profileId = (string?)null,
            browserContextId = (string?)null,
            pageId = (string?)null,
            frameId = (string?)null,
            documentId = "dom-document-8",
            executionWorldId = (string?)null
        };
        var scheduled = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Timer,
            BrowserEvidenceEventTypes.TimerScheduled,
            new
            {
                context,
                timerId = "timer-1",
                timerKind = "animation-frame",
                requestedDelayMilliseconds = (double?)null,
                effectiveDelayMilliseconds = (double?)null,
                nestingLevel = 0,
                throttled = (bool?)null,
                pageLifecycleState = "unknown",
                callbackLocation = (object?)null,
                cancellationReason = (string?)null,
                didTimeout = (bool?)null
            });
        var fired = CreateEvent(
            1,
            120,
            BrowserEvidenceChannels.Timer,
            BrowserEvidenceEventTypes.TimerFired,
            new
            {
                context,
                timerId = "timer-1",
                timerKind = "animation-frame",
                requestedDelayMilliseconds = (double?)null,
                effectiveDelayMilliseconds = (double?)null,
                nestingLevel = 0,
                throttled = (bool?)null,
                pageLifecycleState = "unknown",
                callbackLocation = (object?)null,
                cancellationReason = (string?)null,
                didTimeout = (bool?)null
            });
        var directory = await CreateArchiveAsync([scheduled, fired]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsInstrumentedBrowserIdleCallbackEvidence()
    {
        var context = new
        {
            browserInstanceId = "browser-1",
            processId = 1200,
            processType = "renderer",
            profileId = (string?)null,
            browserContextId = (string?)null,
            pageId = (string?)null,
            frameId = (string?)null,
            documentId = "dom-document-8",
            executionWorldId = (string?)null
        };
        var scheduled = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Timer,
            BrowserEvidenceEventTypes.TimerScheduled,
            new
            {
                context,
                timerId = "timer-1",
                timerKind = "idle-callback",
                requestedDelayMilliseconds = 50.0,
                effectiveDelayMilliseconds = 50.0,
                nestingLevel = 0,
                throttled = (bool?)null,
                pageLifecycleState = "unknown",
                callbackLocation = (object?)null,
                cancellationReason = (string?)null,
                didTimeout = (bool?)null
            });
        var fired = CreateEvent(
            1,
            150,
            BrowserEvidenceChannels.Timer,
            BrowserEvidenceEventTypes.TimerFired,
            new
            {
                context,
                timerId = "timer-1",
                timerKind = "idle-callback",
                requestedDelayMilliseconds = 50.0,
                effectiveDelayMilliseconds = 50.0,
                nestingLevel = 0,
                throttled = (bool?)null,
                pageLifecycleState = "unknown",
                callbackLocation = (object?)null,
                cancellationReason = (string?)null,
                didTimeout = true
            });
        var directory = await CreateArchiveAsync([scheduled, fired]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsCorrelatedBrowserListenerLifecycleEvidence()
    {
        var context = new
        {
            browserInstanceId = "browser-1",
            processId = 1200,
            processType = "renderer",
            profileId = (string?)null,
            browserContextId = (string?)null,
            pageId = (string?)null,
            frameId = (string?)null,
            documentId = "dom-document-8",
            executionWorldId = (string?)null
        };
        var target = new
        {
            documentId = "dom-document-8",
            nodeId = 42,
            backendNodeId = (string?)null,
            tagName = "DIV",
            elementId = "pointer-only",
            classes = Array.Empty<string>()
        };
        var listener = new
        {
            context,
            listenerId = "listener-1",
            eventName = "click",
            registrationKind = "add-event-listener",
            target,
            capture = false,
            passive = false,
            once = false,
            location = (object?)null
        };
        object Dispatch(
            string phase,
            string? listenerId,
            bool defaultPrevented,
            string? outcome) => new
        {
            context,
            dispatchId = "dispatch-1",
            eventName = "click",
            trusted = false,
            originalTarget = target,
            composedPath = new object[] { target },
            phase,
            listenerId,
            defaultPrevented,
            propagationStopped = false,
            immediatePropagationStopped = false,
            defaultAction = (string?)null,
            outcome,
            currentTarget = listenerId is null ? null : target
        };

        var records = new[]
        {
            CreateEvent(
                0,
                100,
                BrowserEvidenceChannels.Listener,
                BrowserEvidenceEventTypes.ListenerRegistered,
                listener),
            CreateEvent(
                1,
                200,
                BrowserEvidenceChannels.Dispatch,
                BrowserEvidenceEventTypes.DispatchStarted,
                Dispatch("none", null, false, null)),
            CreateEvent(
                2,
                300,
                BrowserEvidenceChannels.Listener,
                BrowserEvidenceEventTypes.ListenerRemoved,
                listener),
            CreateEvent(
                3,
                400,
                BrowserEvidenceChannels.Dispatch,
                BrowserEvidenceEventTypes.ListenerInvoked,
                Dispatch("at-target", "listener-1", true, null)),
            CreateEvent(
                4,
                500,
                BrowserEvidenceChannels.Dispatch,
                BrowserEvidenceEventTypes.DispatchCompleted,
                Dispatch(
                    "none",
                    null,
                    true,
                    "canceled-by-event-handler"))
        };
        var directory = await CreateArchiveAsync(records);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsCookieValuesAtTheArchiveBoundary()
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Cookie,
            BrowserEvidenceEventTypes.CookieOperation,
            new
            {
                context = new
                {
                    browserInstanceId = "browser-1",
                    processId = 1200,
                    processType = "renderer",
                    profileId = "test-profile",
                    browserContextId = "context-1",
                    pageId = "page-1",
                    frameId = "frame-1",
                    documentId = "document-1",
                    executionWorldId = "main"
                },
                operation = "read",
                name = "consent",
                domain = "example.test",
                path = "/",
                sameSite = "Lax",
                secure = true,
                httpOnly = false,
                partitioned = false,
                source = "document-cookie",
                result = "returned",
                blockedReason = (string?)null,
                value = "must-not-be-recorded"
            });
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue =>
                    issue.Code == "payload-property-unexpected" &&
                    issue.Path.EndsWith("/value", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AllowsExtensionChannelWithCustomPayload()
    {
        var record = CreateEvent(
            0,
            100,
            "extension.vendor.telemetry",
            "vendor-sample",
            42);
        var directory = await CreateArchiveAsync([record]);
        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsDerivedEvidenceWithResolvableProvenance()
    {
        var observed = CreateEvent(0, 100);
        var derived = AnalysisEventFactory.CreateDerived(
            "test-session",
            CreateDescriptor("test.analyzer", "analysis"),
            "analysis.focus-path",
            0,
            150,
            "focus-path-segment",
            new { from = "button-a", to = "button-b" },
            [observed.EventId],
            "adjacent keyboard focus observations");
        var directory = await CreateArchiveAsync([observed, derived]);
        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DetectsDuplicateEventIdsAndMissingEvidenceReferences()
    {
        var observed = CreateEvent(0, 100);
        var duplicate = CreateEvent(1, 200) with
        {
            EventId = observed.EventId
        };
        var inferred = AnalysisEventFactory.CreateInferred(
            "test-session",
            CreateDescriptor("test.analyzer", "analysis"),
            "analysis.intent",
            0,
            150,
            "possible-command",
            new { command = "open-menu" },
            ["missing-event-id"],
            "temporal input pattern",
            "medium");
        var directory = await CreateArchiveAsync([observed, inferred, duplicate]);
        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Code == "event-id-duplicate");
            Assert.Contains(result.Issues, issue => issue.Code == "related-evidence-not-found");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DetectsInvalidInferenceConfidence()
    {
        var observed = CreateEvent(0, 100);
        var inferred = AnalysisEventFactory.CreateInferred(
            "test-session",
            CreateDescriptor("test.analyzer", "analysis"),
            "analysis.intent",
            0,
            150,
            "possible-command",
            new { command = "open-menu" },
            [observed.EventId],
            "temporal input pattern",
            "certain");
        var directory = await CreateArchiveAsync([observed, inferred]);
        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "inference-confidence-invalid");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsUnknownEvidenceWithReason()
    {
        var observed = CreateEvent(0, 100);
        var unknown = AnalysisEventFactory.CreateUnknown(
            "test-session",
            CreateDescriptor("test.analyzer", "analysis"),
            "analysis.intent",
            0,
            150,
            "unresolved-action",
            new { candidates = new[] { "open", "activate" } },
            [observed.EventId],
            "candidate comparison",
            "The captured evidence does not distinguish the two commands.");
        var directory = await CreateArchiveAsync([observed, unknown]);
        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsLegacyVersion10Event()
    {
        var legacyEvent = new
        {
            schemaVersion = SessionSchemaVersions.LegacyEvent,
            sessionId = "test-session",
            collectorType = "test.collector",
            collectorInstanceId = "0123456789abcdef0123456789abcdef",
            channel = "test.events",
            captureMethod = "test",
            sequence = 0,
            monotonicNanoseconds = 100,
            observedUtc = DateTimeOffset.UtcNow,
            eventType = "test-event",
            payload = new { value = 0 },
            qualityFlags = Array.Empty<string>()
        };
        var directory = await CreateArchiveFromJsonAsync(
            [JsonSerializer.Serialize(legacyEvent, JsonOptions)]);
        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static RecorderEvent CreateEvent(ulong sequence, long timestamp)
    {
        return CreateEvent(
            sequence,
            timestamp,
            "test.events",
            "test-event",
            new { value = sequence });
    }

    private static RecorderEvent CreateEvent<T>(
        ulong sequence,
        long timestamp,
        string channel,
        string eventType,
        T payload)
    {
        var descriptor = CreateDescriptor(
            "test.collector",
            "test");
        return RecorderEventFactory.Create(
            "test-session",
            descriptor,
            channel,
            sequence,
            timestamp,
            eventType,
            payload);
    }

    private static CollectorDescriptor CreateDescriptor(
        string collectorType,
        string captureMethod) =>
        new(
            collectorType,
            "0123456789abcdef0123456789abcdef",
            collectorType,
            "1.0",
            "1.0",
            ["test.events"],
            captureMethod);

    private static async Task<string> CreateArchiveAsync(
        IReadOnlyList<RecorderEvent> events)
    {
        var lines = events
            .Select(record => JsonSerializer.Serialize(record, JsonOptions))
            .ToArray();
        return await CreateArchiveFromJsonAsync(lines);
    }

    private static async Task<string> CreateArchiveFromJsonAsync(
        IReadOnlyList<string> eventLines)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var eventPath = Path.Combine(directory, "events.ndjson");
        await using (var writer = new StreamWriter(eventPath))
        {
            foreach (var line in eventLines)
            {
                await writer.WriteLineAsync(line);
            }
        }

        var eventBytes = await File.ReadAllBytesAsync(
            eventPath,
            TestContext.Current.CancellationToken);
        var artifact = new SessionArtifact(
            "events.ndjson",
            eventBytes.LongLength,
            Convert.ToHexString(SHA256.HashData(eventBytes)).ToLowerInvariant());
        var started = DateTimeOffset.UtcNow.AddSeconds(-1);
        var manifest = new SessionManifest(
            SessionSchemaVersions.Manifest,
            "test-session",
            "completed",
            started,
            started.AddSeconds(1),
            1_000_000_000,
            10_000_000,
            1,
            "Windows",
            ".NET",
            "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, false, false),
            [],
            [artifact],
            eventLines.Count,
            0,
            null);
        await SessionManifestWriter.WriteAsync(
            Path.Combine(directory, "manifest.json"),
            manifest,
            TestContext.Current.CancellationToken);
        return directory;
    }
}

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
    public async Task AcceptsTimestampOverlapAcrossIndependentClockMappings()
    {
        var directory = await CreateArchiveAsync(
            [
                CreateEvent(0, 200) with
                {
                    ClockMappingId = "chromium:browser-1:100"
                },
                CreateEvent(1, 100) with
                {
                    ClockMappingId = "chromium:browser-1:200"
                }
            ]);
        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(
                result.IsValid,
                JsonSerializer.Serialize(result.Issues, JsonOptions));
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
            executionWorldId = "main",
            documentToken = (string?)null
        };
        var target = new
        {
            kind = "node",
            interfaceName = "HTMLDivElement",
            targetId = (string?)null,
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
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
        };
        var target = new
        {
            kind = "node",
            interfaceName = "HTMLDivElement",
            targetId = (string?)null,
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
    public async Task AcceptsAWindowEventTargetWithoutANodeIdentifier()
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
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
        };
        var window = new
        {
            kind = "window",
            interfaceName = "Window",
            targetId = "event-target-3",
            documentId = "dom-document-8",
            nodeId = (long?)null,
            backendNodeId = (string?)null,
            tagName = (string?)null,
            elementId = (string?)null,
            classes = Array.Empty<string>()
        };
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            new
            {
                context,
                listenerId = "listener-1",
                eventName = "resize",
                registrationKind = "add-event-listener",
                target = window,
                capture = false,
                passive = false,
                once = false,
                location = (object?)null
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
    public async Task RejectsANonNodeEventTargetThatClaimsANodeIdentifier()
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
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
        };
        var window = new
        {
            kind = "window",
            interfaceName = "Window",
            targetId = (string?)null,
            documentId = "dom-document-8",
            nodeId = 42,
            backendNodeId = (string?)null,
            tagName = (string?)null,
            elementId = (string?)null,
            classes = Array.Empty<string>()
        };
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            new
            {
                context,
                listenerId = "listener-1",
                eventName = "resize",
                registrationKind = "add-event-listener",
                target = window,
                capture = false,
                passive = false,
                once = false,
                location = (object?)null
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
                issue => issue.Code == "browser-event-target-identity" &&
                    issue.Message.Contains(
                        "has no nodeId",
                        StringComparison.Ordinal));
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "browser-event-target-identity" &&
                    issue.Message.Contains(
                        "must report its targetId",
                        StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsANodeEventTargetWithoutANodeIdentifier()
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
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
        };
        var node = new
        {
            kind = "node",
            interfaceName = "HTMLDivElement",
            targetId = (string?)null,
            documentId = "dom-document-8",
            nodeId = (long?)null,
            backendNodeId = (string?)null,
            tagName = "DIV",
            elementId = "pointer-only",
            classes = Array.Empty<string>()
        };
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            new
            {
                context,
                listenerId = "listener-1",
                eventName = "click",
                registrationKind = "add-event-listener",
                target = node,
                capture = false,
                passive = false,
                once = false,
                location = (object?)null
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
                issue => issue.Code == "browser-event-target-identity" &&
                    issue.Message.Contains(
                        "must report its nodeId",
                        StringComparison.Ordinal));
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
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
        };
        var target = new
        {
            kind = "node",
            interfaceName = "HTMLAnchorElement",
            targetId = (string?)null,
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
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
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
                pageLifecycleState = "visible",
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
                pageLifecycleState = "hidden",
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
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
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
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
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
    public async Task AcceptsInstrumentedBrowserSchedulerDecisionEvidence()
    {
        var deferred = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Scheduler,
            BrowserEvidenceEventTypes.WakeUpDeferred,
            new
            {
                context = new
                {
                    browserInstanceId = "browser-1",
                    processId = 1200,
                    processType = "renderer",
                    profileId = (string?)null,
                    browserContextId = (string?)null,
                    pageId = (string?)null,
                    frameId = (string?)null,
                    documentId = (string?)null,
                    executionWorldId = (string?)null,
                    documentToken = (string?)null
                },
                queueName = "frame-throttleable",
                queueType = 12,
                throttlingType = "background",
                desiredWakeUpTicks = "123456000",
                allowedWakeUpTicks = "124000000",
                deferralMilliseconds = 544.0,
                hasReadyTask = false,
                blockType = "all-tasks",
                decisionBoundary = "task-queue-throttler"
            });
        var directory = await CreateArchiveAsync([deferred]);

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
    public async Task AcceptsInstrumentedBrowserNavigationEvidence()
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Navigation,
            BrowserEvidenceEventTypes.NavigationCompleted,
            new
            {
                context = new
                {
                    browserInstanceId = "browser-1",
                    processId = 1200,
                    processType = "browser",
                    profileId = (string?)null,
                    browserContextId = (string?)null,
                    pageId = "frame-12",
                    frameId = "frame-12",
                    documentId = "document-navigation-40",
                    executionWorldId = (string?)null,
                    documentToken = "document-token-40"
                },
                parentFrameId = (string?)null,
                parentOrOuterDocumentFrameId = (string?)null,
                frameType = "primary-main-frame",
                primaryPage = true,
                navigationId = "navigation-41",
                url = "file:///fixture.html#same-document-navigation",
                navigationKind = "same-document",
                rendererInitiated = true,
                sameDocument = true,
                committed = true,
                errorPage = false,
                netErrorCode = 0,
                outcome = "committed",
                rendererProcessId = 3400
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
    public async Task RejectsCommittedNavigationWithoutRendererDocumentCorrelation()
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Navigation,
            BrowserEvidenceEventTypes.NavigationCompleted,
            new
            {
                context = new
                {
                    browserInstanceId = "browser-1",
                    processId = 1200,
                    processType = "browser",
                    profileId = (string?)null,
                    browserContextId = (string?)null,
                    pageId = "frame-12",
                    frameId = "frame-12",
                    documentId = "document-navigation-40",
                    executionWorldId = (string?)null,
                    documentToken = (string?)null
                },
                parentFrameId = (string?)null,
                parentOrOuterDocumentFrameId = (string?)null,
                frameType = "primary-main-frame",
                primaryPage = true,
                navigationId = "navigation-40",
                url = "file:///fixture.html",
                navigationKind = "cross-document",
                rendererInitiated = false,
                sameDocument = false,
                committed = true,
                errorPage = false,
                netErrorCode = 0,
                outcome = "committed",
                rendererProcessId = (int?)null
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
                    issue.Code ==
                    "browser-navigation-committed-document-missing");
            Assert.Contains(
                result.Issues,
                issue =>
                    issue.Code ==
                    "browser-navigation-committed-renderer-missing");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsSubframeNavigationWithoutParentIdentity()
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Navigation,
            BrowserEvidenceEventTypes.NavigationCompleted,
            new
            {
                context = new
                {
                    browserInstanceId = "browser-1",
                    processId = 1200,
                    processType = "browser",
                    profileId = (string?)null,
                    browserContextId = (string?)null,
                    pageId = "frame-12",
                    frameId = "frame-13",
                    documentId = "document-navigation-40",
                    executionWorldId = (string?)null,
                    documentToken = "document-token-40"
                },
                parentFrameId = (string?)null,
                parentOrOuterDocumentFrameId = (string?)null,
                frameType = "subframe",
                primaryPage = true,
                navigationId = "navigation-40",
                url = "file:///blink-subframe.html",
                navigationKind = "cross-document",
                rendererInitiated = false,
                sameDocument = false,
                committed = true,
                errorPage = false,
                netErrorCode = 0,
                outcome = "committed",
                rendererProcessId = 3400
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
                    issue.Code ==
                    "browser-navigation-subframe-parent-mismatch");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsInstrumentedBrowserDomCheckpointEvidence()
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
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
        };
        var records = new[]
        {
            CreateEvent(
                0,
                100,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointStarted,
                new
                {
                    context,
                    checkpointId = "dom-checkpoint-1",
                    reason = "finished-parsing",
                    maximumNodes = 512
                }),
            CreateEvent(
                1,
                200,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointNode,
                new
                {
                    context,
                    checkpointId = "dom-checkpoint-1",
                    nodeIndex = 0,
                    nodeId = 8,
                    parentNodeId = (long?)null,
                    nodeType = "document",
                    nodeName = "#document"
                }),
            CreateEvent(
                2,
                300,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointNode,
                new
                {
                    context,
                    checkpointId = "dom-checkpoint-1",
                    nodeIndex = 1,
                    nodeId = 9,
                    parentNodeId = (long?)8,
                    nodeType = "element",
                    nodeName = "HTML"
                }),
            CreateEvent(
                3,
                400,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointNodeAttribute,
                new
                {
                    context,
                    checkpointId = "dom-checkpoint-1",
                    nodeId = 9,
                    attributeIndex = 0,
                    attributeNamespace = (string?)null,
                    attributeName = "lang",
                    attributeValue = "en",
                    attributeValueLength = 2,
                    attributeValueTruncated = false,
                    maximumValueLength = 4096
                }),
            CreateEvent(
                4,
                500,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointCompleted,
                new
                {
                    context,
                    checkpointId = "dom-checkpoint-1",
                    reason = "finished-parsing",
                    nodeCount = 2,
                    truncated = false,
                    maximumNodes = 512,
                    attributeCount = 1,
                    attributesTruncated = false,
                    maximumAttributesPerNode = 64,
                    maximumValueLength = 4096,
                    coveredTransitionCount = 0,
                    coveredTransitionFirstId = (string?)null,
                    coveredTransitionLastId = (string?)null
                }),
            CreateEvent(
                5,
                600,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointStarted,
                new
                {
                    context,
                    checkpointId = "dom-checkpoint-2",
                    reason = "post-mutation",
                    maximumNodes = 512
                }),
            CreateEvent(
                6,
                700,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointNode,
                new
                {
                    context,
                    checkpointId = "dom-checkpoint-2",
                    nodeIndex = 0,
                    nodeId = 8,
                    parentNodeId = (long?)null,
                    nodeType = "document",
                    nodeName = "#document"
                }),
            CreateEvent(
                7,
                800,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCheckpointCompleted,
                new
                {
                    context,
                    checkpointId = "dom-checkpoint-2",
                    reason = "post-mutation",
                    nodeCount = 1,
                    truncated = false,
                    maximumNodes = 512,
                    attributeCount = 0,
                    attributesTruncated = false,
                    maximumAttributesPerNode = 64,
                    maximumValueLength = 4096,
                    coveredTransitionCount = 2,
                    coveredTransitionFirstId = "dom-transition-1",
                    coveredTransitionLastId = "dom-transition-2"
                })
        };
        var directory = await CreateArchiveAsync(records);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid, JsonSerializer.Serialize(result.Issues));
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsInstrumentedBrowserDomStateChangeEvidence()
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
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
        };
        var records = new[]
        {
            CreateEvent(
                0,
                100,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomAttributeChanged,
                new
                {
                    context,
                    transitionId = "dom-transition-1",
                    nodeId = 11,
                    nodeName = "BUTTON",
                    attributeNamespace = (string?)null,
                    attributeName = "aria-expanded",
                    changeType = "changed",
                    attributeValue = "true",
                    attributeValueLength = (int?)4,
                    attributeValueTruncated = false,
                    previousAttributeValue = "false",
                    previousAttributeValueLength = (int?)5,
                    previousAttributeValueTruncated = false,
                    maximumValueLength = 4096
                }),
            CreateEvent(
                1,
                200,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomAttributeChanged,
                new
                {
                    context,
                    transitionId = "dom-transition-2",
                    nodeId = 11,
                    nodeName = "BUTTON",
                    attributeNamespace = (string?)null,
                    attributeName = "hidden",
                    changeType = "removed",
                    attributeValue = (string?)null,
                    attributeValueLength = (int?)null,
                    attributeValueTruncated = false,
                    previousAttributeValue = "",
                    previousAttributeValueLength = (int?)0,
                    previousAttributeValueTruncated = false,
                    maximumValueLength = 4096
                }),
            CreateEvent(
                2,
                300,
                BrowserEvidenceChannels.Dom,
                BrowserEvidenceEventTypes.DomCharacterDataChanged,
                new
                {
                    context,
                    transitionId = "dom-transition-3",
                    nodeId = 14,
                    parentNodeId = (long?)13,
                    nodeType = "text",
                    text = "Two items remaining",
                    textLength = 19,
                    textTruncated = false,
                    previousText = "Three items remaining",
                    previousTextLength = 21,
                    previousTextTruncated = false,
                    maximumValueLength = 4096
                })
        };
        var directory = await CreateArchiveAsync(records);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid, JsonSerializer.Serialize(result.Issues));
            Assert.Empty(result.Issues);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsTruncatedDomAttributeValueThatReportsItsFullLength()
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Dom,
            BrowserEvidenceEventTypes.DomCheckpointNodeAttribute,
            new
            {
                context = CreateRendererDocumentContext(),
                checkpointId = "dom-checkpoint-1",
                nodeId = 11,
                attributeIndex = 0,
                attributeNamespace = (string?)null,
                attributeName = "aria-label",
                attributeValue = new string('a', 16),
                attributeValueLength = 4096,
                attributeValueTruncated = true,
                maximumValueLength = 16
            });
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid, JsonSerializer.Serialize(result.Issues));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsDomAttributeValueLengthWithoutTruncationState()
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Dom,
            BrowserEvidenceEventTypes.DomCheckpointNodeAttribute,
            new
            {
                context = CreateRendererDocumentContext(),
                checkpointId = "dom-checkpoint-1",
                nodeId = 11,
                attributeIndex = 0,
                attributeNamespace = (string?)null,
                attributeName = "aria-label",
                attributeValue = "Save",
                attributeValueLength = 400,
                attributeValueTruncated = false,
                maximumValueLength = 4096
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
                    issue.Code == "browser-dom-text-truncation-inconsistent");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "dom-transition-1", "dom-transition-2")]
    [InlineData(0, "dom-transition-1", null)]
    [InlineData(2, null, null)]
    [InlineData(2, "dom-transition-1", null)]
    public async Task RejectsDomCheckpointWithHalfStatedTransitionCoverage(
        int coveredTransitionCount,
        string? coveredTransitionFirstId,
        string? coveredTransitionLastId)
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Dom,
            BrowserEvidenceEventTypes.DomCheckpointCompleted,
            new
            {
                context = CreateRendererDocumentContext(),
                checkpointId = "dom-checkpoint-1",
                reason = "post-mutation",
                nodeCount = 1,
                truncated = false,
                maximumNodes = 512,
                attributeCount = 0,
                attributesTruncated = false,
                maximumAttributesPerNode = 64,
                maximumValueLength = 4096,
                coveredTransitionCount,
                coveredTransitionFirstId,
                coveredTransitionLastId
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
                    issue.Code == "browser-dom-checkpoint-coverage-inconsistent");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("added", "true", "false")]
    [InlineData("removed", "true", null)]
    [InlineData("changed", null, "false")]
    public async Task RejectsDomAttributeChangeThatContradictsItsChangeType(
        string changeType,
        string? value,
        string? previousValue)
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Dom,
            BrowserEvidenceEventTypes.DomAttributeChanged,
            new
            {
                context = CreateRendererDocumentContext(),
                transitionId = "dom-transition-4",
                nodeId = 11,
                nodeName = "BUTTON",
                attributeNamespace = (string?)null,
                attributeName = "aria-expanded",
                changeType,
                attributeValue = value,
                attributeValueLength = value?.Length,
                attributeValueTruncated = false,
                previousAttributeValue = previousValue,
                previousAttributeValueLength = previousValue?.Length,
                previousAttributeValueTruncated = false,
                maximumValueLength = 4096
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
                    issue.Code == "browser-dom-attribute-change-inconsistent");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsDomCharacterDataChangeWithoutDocumentToken()
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Dom,
            BrowserEvidenceEventTypes.DomCharacterDataChanged,
            new
            {
                context = new
                {
                    browserInstanceId = "browser-1",
                    processId = 3400,
                    processType = "renderer",
                    profileId = (string?)null,
                    browserContextId = (string?)null,
                    pageId = (string?)null,
                    frameId = (string?)null,
                    documentId = "dom-document-8",
                    executionWorldId = (string?)null,
                    documentToken = (string?)null
                },
                transitionId = "dom-transition-5",
                nodeId = 14,
                parentNodeId = (long?)13,
                nodeType = "text",
                text = "Saved",
                textLength = 5,
                textTruncated = false,
                previousText = "Saving",
                previousTextLength = 6,
                previousTextTruncated = false,
                maximumValueLength = 4096
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
                issue => issue.Code == "browser-dom-context-invalid");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static object CreateRendererDocumentContext() =>
        new
        {
            browserInstanceId = "browser-1",
            processId = 1200,
            processType = "renderer",
            profileId = (string?)null,
            browserContextId = (string?)null,
            pageId = (string?)null,
            frameId = (string?)null,
            documentId = "dom-document-8",
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
        };

    [Fact]
    public async Task RejectsDomCheckpointWithoutDocumentToken()
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Dom,
            BrowserEvidenceEventTypes.DomCheckpointStarted,
            new
            {
                context = new
                {
                    browserInstanceId = "browser-1",
                    processId = 3400,
                    processType = "renderer",
                    profileId = (string?)null,
                    browserContextId = (string?)null,
                    pageId = (string?)null,
                    frameId = (string?)null,
                    documentId = "dom-document-8",
                    executionWorldId = (string?)null,
                    documentToken = (string?)null
                },
                checkpointId = "dom-checkpoint-1",
                reason = "finished-parsing",
                maximumNodes = 512
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
                issue => issue.Code == "browser-dom-context-invalid");
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
            executionWorldId = (string?)null,
            documentToken = "document-token-8"
        };
        var target = new
        {
            kind = "node",
            interfaceName = "HTMLDivElement",
            targetId = (string?)null,
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
                    executionWorldId = "main",
                    documentToken = (string?)null
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

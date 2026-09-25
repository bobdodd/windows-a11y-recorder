using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public async Task SkippingHashVerificationKeepsStructuralArtifactChecks()
    {
        var directory = await CreateArchiveAsync([CreateEvent(0, 100)]);
        try
        {
            var eventPath = Path.Combine(directory, "events.ndjson");
            var bytes = await File.ReadAllBytesAsync(
                eventPath,
                TestContext.Current.CancellationToken);
            var index = Array.IndexOf(bytes, (byte)'{');
            bytes[index + 1] = bytes[index + 1] == (byte)' '
                ? (byte)'\t'
                : (byte)' ';
            await File.WriteAllBytesAsync(
                eventPath,
                bytes,
                TestContext.Current.CancellationToken);
            var options = new ArchiveValidationOptions(VerifyArtifactHashes: false);

            var skipped = await SessionArchiveValidator.ValidateAsync(
                directory,
                options,
                TestContext.Current.CancellationToken);
            var verified = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(skipped.ArtifactHashesVerified);
            Assert.Equal(1, skipped.ArtifactsValidated);
            Assert.DoesNotContain(
                skipped.Issues,
                issue => issue.Code == "artifact-hash-mismatch");
            Assert.True(verified.ArtifactHashesVerified);
            Assert.Contains(
                verified.Issues,
                issue => issue.Code == "artifact-hash-mismatch");

            await File.AppendAllTextAsync(
                eventPath,
                "{}\n",
                TestContext.Current.CancellationToken);
            var resized = await SessionArchiveValidator.ValidateAsync(
                directory,
                options,
                TestContext.Current.CancellationToken);
            Assert.Contains(
                resized.Issues,
                issue => issue.Code == "artifact-size-mismatch");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SkippingHashVerificationRejectsMalformedHash()
    {
        var directory = await CreateArchiveAsync([CreateEvent(0, 100)]);
        try
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(
                manifestPath,
                TestContext.Current.CancellationToken))!;
            manifest["artifacts"]![0]!["sha256"] = "not-a-hash";
            await File.WriteAllTextAsync(
                manifestPath,
                manifest.ToJsonString(),
                TestContext.Current.CancellationToken);

            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                new ArchiveValidationOptions(VerifyArtifactHashes: false),
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "artifact-hash-invalid");
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
            executionWorldId = "world-0",
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
                location,
                world = MainWorld()
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
                pathScopes = Array.Empty<object>(),
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
            executionWorldId = "world-0",
            documentToken = "document-token-8"
        };
        var window = new
        {
            kind = "window",
            interfaceName = "DOMWindow",
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
                location = (object?)null,
                world = MainWorld()
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
            interfaceName = "DOMWindow",
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

    // Protocol 0.31 names the execution context of every listener and
    // dispatch record. A worker scope belongs to no document, so its records
    // name none and can hold only a non-Node target, while a record that names
    // no scope, as earlier archives do, must still name its document.
    [Fact]
    public async Task AcceptsAWorkerListenerThatNamesNoDocument()
    {
        var issues = await ValidateListenerScopeAsync(
            contextDocumentId: null,
            targetKind: "other",
            targetDocumentId: null,
            scopeKind: "dedicated-worker");

        Assert.Empty(issues);
    }

    [Fact]
    public async Task RejectsANodeTargetInAWorkerScope()
    {
        var issues = await ValidateListenerScopeAsync(
            contextDocumentId: null,
            targetKind: "node",
            targetDocumentId: null,
            scopeKind: "service-worker");

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-event-scope-inconsistent" &&
                issue.Message.Contains("has no node event target", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAWorkerRecordThatNamesADocument()
    {
        var issues = await ValidateListenerScopeAsync(
            contextDocumentId: "dom-document-8",
            targetKind: "other",
            targetDocumentId: "dom-document-8",
            scopeKind: "shared-worker");

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-event-scope-inconsistent" &&
                issue.Path == "events.ndjson#/payload/context/documentId");
        Assert.Contains(
            issues,
            issue => issue.Code == "browser-event-scope-inconsistent" &&
                issue.Path == "events.ndjson#/payload/target/documentId");
    }

    [Fact]
    public async Task RejectsATargetWithoutADocumentOutsideAWorkerScope()
    {
        var unscoped = await ValidateListenerScopeAsync(
            contextDocumentId: "dom-document-8",
            targetKind: "other",
            targetDocumentId: null,
            scopeKind: null);
        var window = await ValidateListenerScopeAsync(
            contextDocumentId: "dom-document-8",
            targetKind: "other",
            targetDocumentId: null,
            scopeKind: "window");

        Assert.Contains(
            unscoped,
            issue => issue.Code == "browser-event-scope-inconsistent" &&
                issue.Message.Contains("names no scope", StringComparison.Ordinal));
        Assert.Contains(
            window,
            issue => issue.Code == "browser-event-scope-inconsistent" &&
                issue.Message.Contains("window scope", StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<ArchiveValidationIssue>>
        ValidateListenerScopeAsync(
            string? contextDocumentId,
            string targetKind,
            string? targetDocumentId,
            string? scopeKind)
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
            documentId = contextDocumentId,
            executionWorldId = (string?)null,
            documentToken = (string?)null
        };
        var node = targetKind == "node";
        var target = new
        {
            kind = targetKind,
            interfaceName = node ? (string?)null : "DedicatedWorkerGlobalScope",
            targetId = node ? (string?)null : "event-target-5",
            documentId = targetDocumentId,
            nodeId = node ? 12L : (long?)null,
            backendNodeId = (string?)null,
            tagName = node ? "DIV" : (string?)null,
            elementId = (string?)null,
            classes = Array.Empty<string>()
        };
        var payload = new Dictionary<string, object?>
        {
            ["context"] = context,
            ["listenerId"] = "listener-1",
            ["eventName"] = "message",
            ["registrationKind"] = "add-event-listener",
            ["target"] = target,
            ["capture"] = false,
            ["passive"] = false,
            ["once"] = false,
            ["location"] = null,
            ["world"] = null
        };
        if (scopeKind is not null)
        {
            var window = scopeKind == "window";
            payload["scope"] = new
            {
                contextKind = scopeKind,
                workerToken = window ? null : "5B2A9F0E6C1D4A7B8E3F2C1D0A9B8C7D",
                globalObjectUrl = window
                    ? null
                    : "http://127.0.0.1:8123/workers/worker.js"
            };
        }

        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            payload);
        var directory = await CreateArchiveAsync([record]);
        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);
            return result.Issues.ToList();
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
                pathScopes = new object[]
                {
                    new
                    {
                        treeScopeRootNodeId = (long?)null,
                        shadowRootMode = (string?)null,
                        targetNodeId = (long?)null,
                        relatedTargetNodeId = (long?)null,
                        visiblePathIndexes = new[] { 0 },
                        unmatchedVisibleTargetCount = 0
                    }
                },
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
    public async Task AcceptsABrowserOmissionThatStatesLostRecords()
    {
        // An omission record is how the archive states evidence that was lost
        // rather than captured, so it must validate on a browser channel both
        // with the process context that lost the records and without one.
        var context = new
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
        };
        var attributed = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Dispatch,
            BrowserEvidenceEventTypes.Omission,
            new
            {
                context,
                reason = BrowserEvidenceOmissionReasons.EvidenceWriteFailed,
                count = 7
            });
        var unattributed = CreateEvent(
            1,
            200,
            BrowserEvidenceChannels.Timer,
            BrowserEvidenceEventTypes.Omission,
            new
            {
                reason = BrowserEvidenceOmissionReasons.SinkRefusedRecord,
                count = 2
            });
        var directory = await CreateArchiveAsync([attributed, unattributed]);

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
    public async Task RejectsABrowserOmissionThatReportsAnUnknownFact()
    {
        // The omission shape is closed, so a fact no reporter is defined to
        // report fails validation instead of entering the archive unchecked.
        var omission = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.Omission,
            new
            {
                reason = BrowserEvidenceOmissionReasons.SinkRefusedRecord,
                count = 1,
                stream = "microphone"
            });
        var directory = await CreateArchiveAsync([omission]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "payload-property-unexpected");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsBrowserLifecycleEvidence()
    {
        var connected = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Lifecycle,
            BrowserEvidenceEventTypes.Connected,
            new
            {
                protocolVersion = BrowserEvidenceProtocol.CurrentVersion,
                browserInstanceId = "browser-1",
                processId = 4321,
                processType = "renderer",
                chromiumVersion = "142.0.0.0",
                parentProcessId = (int?)1000,
                childProcessId = (int?)4321
            });
        var synchronized = CreateEvent(
            1,
            200,
            BrowserEvidenceChannels.Lifecycle,
            BrowserEvidenceEventTypes.ClockSynchronized,
            new
            {
                protocolVersion = BrowserEvidenceProtocol.CurrentVersion,
                browserInstanceId = "browser-1",
                processId = 1000,
                processType = "browser",
                parentProcessId = (int?)null,
                childProcessId = (int?)null,
                clockMappingId = "chromium:browser-1:1000",
                monotonicFrequency = "10000000",
                uncertaintyNanoseconds = 12_500L
            });
        var directory = await CreateArchiveAsync([connected, synchronized]);

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
    public async Task RejectsBrowserLifecycleEvidenceThatReportsAnUnknownFact()
    {
        // The lifecycle shape is closed, so a fact the receiver is not defined
        // to report fails validation instead of entering the archive unchecked.
        var connected = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Lifecycle,
            BrowserEvidenceEventTypes.Connected,
            new
            {
                protocolVersion = BrowserEvidenceProtocol.CurrentVersion,
                browserInstanceId = "browser-1",
                processId = 4321,
                processType = "renderer",
                chromiumVersion = "142.0.0.0",
                parentProcessId = (int?)1000,
                childProcessId = (int?)4321,
                commandLine = "--headless"
            });
        var directory = await CreateArchiveAsync([connected]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "payload-property-unexpected");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsBrowserAccessibilityCheckpointEvidence()
    {
        var context = new
        {
            browserInstanceId = "browser-1",
            processId = 4321,
            processType = "renderer",
            profileId = (string?)null,
            browserContextId = (string?)null,
            pageId = (string?)null,
            frameId = (string?)null,
            documentId = (string?)null,
            executionWorldId = (string?)null,
            documentToken = "document-token-1"
        };
        var started = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Accessibility,
            BrowserEvidenceEventTypes.AccessibilityCheckpointStarted,
            new
            {
                context,
                checkpointId = "accessibility-checkpoint-1",
                reason = "renderer-serialization",
                maximumNodes = 100_000,
                updateCount = 1,
                eventCount = 0
            });
        var node = CreateEvent(
            1,
            200,
            BrowserEvidenceChannels.Accessibility,
            BrowserEvidenceEventTypes.AccessibilityCheckpointNode,
            new
            {
                context,
                checkpointId = "accessibility-checkpoint-1",
                nodeIndex = 0,
                accessibilityNodeId = 12,
                parentAccessibilityNodeId = (int?)null,
                domNodeId = (int?)null,
                role = 7,
                roleName = "button",
                name = "Search",
                description = string.Empty,
                serializedProperties = "{}",
                focused = false
            });
        var completed = CreateEvent(
            2,
            300,
            BrowserEvidenceChannels.Accessibility,
            BrowserEvidenceEventTypes.AccessibilityCheckpointCompleted,
            new
            {
                context,
                checkpointId = "accessibility-checkpoint-1",
                reason = "renderer-serialization",
                nodeCount = 1,
                truncated = false,
                maximumNodes = 100_000,
                updateCount = 1,
                eventCount = 0
            });
        var directory = await CreateArchiveAsync([started, node, completed]);

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
    public async Task RejectsAnAccessibilityNodeThatReportsAnUnknownFact()
    {
        // The node shape is closed, so a property the bridge is not defined to
        // send fails validation instead of entering the archive unchecked.
        var node = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Accessibility,
            BrowserEvidenceEventTypes.AccessibilityCheckpointNode,
            new
            {
                context = new
                {
                    browserInstanceId = "browser-1",
                    processId = 4321,
                    processType = "renderer",
                    profileId = (string?)null,
                    browserContextId = (string?)null,
                    pageId = (string?)null,
                    frameId = (string?)null,
                    documentId = (string?)null,
                    executionWorldId = (string?)null,
                    documentToken = "document-token-1"
                },
                checkpointId = "accessibility-checkpoint-1",
                nodeIndex = 0,
                accessibilityNodeId = 12,
                parentAccessibilityNodeId = (int?)null,
                domNodeId = (int?)null,
                role = 7,
                roleName = "button",
                name = "Search",
                description = string.Empty,
                serializedProperties = "{}",
                focused = false,
                violation = "missing-accessible-name"
            });
        var directory = await CreateArchiveAsync([node]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "payload-property-unexpected");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsAnAccessibilityCheckpointFromOutsideARenderer()
    {
        // A checkpoint states which renderer document was serialized, so a
        // record that names the browser process instead is not usable evidence.
        var started = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Accessibility,
            BrowserEvidenceEventTypes.AccessibilityCheckpointStarted,
            new
            {
                context = new
                {
                    browserInstanceId = "browser-1",
                    processId = 1000,
                    processType = "browser",
                    profileId = (string?)null,
                    browserContextId = (string?)null,
                    pageId = (string?)null,
                    frameId = (string?)null,
                    documentId = (string?)null,
                    executionWorldId = (string?)null,
                    documentToken = (string?)null
                },
                checkpointId = "accessibility-checkpoint-1",
                reason = "renderer-serialization",
                maximumNodes = 100_000,
                updateCount = 1,
                eventCount = 0
            });
        var directory = await CreateArchiveAsync([started]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue =>
                    issue.Code == "browser-accessibility-context-invalid");
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
                    coveredTransitionLastId = (string?)null,
                    shadowRootCount = 0,
                    slotCount = 0
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
                    coveredTransitionLastId = "dom-transition-2",
                    shadowRootCount = 0,
                    slotCount = 0
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
                coveredTransitionLastId,
                shadowRootCount = 0,
                slotCount = 0
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
    public async Task AcceptsEveryListenerRegistrationFormAtTheArchiveBoundary()
    {
        // Blink registers an addEventListener call, an on-event attribute
        // assignment, and an inline content attribute through one path, and
        // reassigning an on-event attribute over an existing registration
        // replaces the callback without adding or removing a listener. The
        // archive has to carry all four, so a session that reports them is
        // valid.
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
            executionWorldId = "world-0",
            documentToken = "document-token-8"
        };
        var target = new
        {
            kind = "node",
            interfaceName = "HTMLButtonElement",
            targetId = (string?)null,
            documentId = "dom-document-8",
            nodeId = 42,
            backendNodeId = (string?)null,
            tagName = "BUTTON",
            elementId = "pointer-only",
            classes = Array.Empty<string>()
        };
        object Listener(string listenerId, string registrationKind) => new
        {
            context,
            listenerId,
            eventName = "click",
            registrationKind,
            target,
            capture = false,
            passive = false,
            once = false,
            location = (object?)null,
            world = MainWorld()
        };

        var records = new[]
        {
            CreateEvent(
                0,
                100,
                BrowserEvidenceChannels.Listener,
                BrowserEvidenceEventTypes.ListenerRegistered,
                Listener("listener-1", "add-event-listener")),
            CreateEvent(
                1,
                200,
                BrowserEvidenceChannels.Listener,
                BrowserEvidenceEventTypes.ListenerRegistered,
                Listener("listener-2", "inline-attribute")),
            CreateEvent(
                2,
                300,
                BrowserEvidenceChannels.Listener,
                BrowserEvidenceEventTypes.ListenerCallbackReplaced,
                Listener("listener-2", "event-handler-property")),
            CreateEvent(
                3,
                400,
                BrowserEvidenceChannels.Listener,
                BrowserEvidenceEventTypes.ListenerRemoved,
                Listener("listener-2", "event-handler-property"))
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
    public async Task RejectsAListenerRegistrationFormTheSchemaDoesNotDefine()
    {
        // A registration form outside the schema would let an unreviewed
        // reading of how a listener was created reach the archive, so it is
        // rejected at the boundary rather than carried through.
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
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
                    documentId = "dom-document-8",
                    executionWorldId = (string?)null,
                    documentToken = "document-token-8"
                },
                listenerId = "listener-1",
                eventName = "click",
                registrationKind = "on-attribute",
                target = new
                {
                    kind = "node",
                    interfaceName = "HTMLButtonElement",
                    targetId = (string?)null,
                    documentId = "dom-document-8",
                    nodeId = 42,
                    backendNodeId = (string?)null,
                    tagName = "BUTTON",
                    elementId = "pointer-only",
                    classes = Array.Empty<string>()
                },
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
                issue =>
                    issue.Code == "payload-property-invalid" &&
                    issue.Path.EndsWith("/registrationKind", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsAListenerLocationLineTheSchemaDoesNotAllow()
    {
        // A negative line is not a line Blink can report, so it is a defect in
        // the recorder rather than a fact about the session, and it is rejected
        // at the boundary rather than published as evidence.
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
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
                    documentId = "dom-document-8",
                    executionWorldId = (string?)null,
                    documentToken = "document-token-8"
                },
                listenerId = "listener-1",
                eventName = "click",
                registrationKind = "add-event-listener",
                target = new
                {
                    kind = "node",
                    interfaceName = "HTMLButtonElement",
                    targetId = (string?)null,
                    documentId = "dom-document-8",
                    nodeId = 42,
                    backendNodeId = (string?)null,
                    tagName = "BUTTON",
                    elementId = "pointer-only",
                    classes = Array.Empty<string>()
                },
                capture = false,
                passive = false,
                once = false,
                location = new
                {
                    scriptId = "7",
                    url = "file:///fixtures/blink-listener-registration.js",
                    line = -3,
                    column = 5,
                    functionName = "registerExternalScriptListener",
                    sourceHash = (string?)null
                }
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
                    issue.Code == "payload-property-invalid" &&
                    issue.Path.EndsWith("/location/line", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcceptsAListenerRegisteredFromAnIsolatedWorld()
    {
        // An isolated world is the world an extension or the inspector runs
        // script in. The world named on the record and the world named in its
        // context are the same world, so both readings of one registration
        // agree.
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            CreateListenerWithWorld("world-13", "isolated", 13));
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsAListenerWorldItsContextContradicts()
    {
        // A record that names one world on the payload and another in its
        // context gives a consumer two answers to the same question, so the
        // archive is rejected rather than published with the contradiction in
        // it.
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            CreateListenerWithWorld("world-0", "isolated", 13));
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
                    issue.Code == "browser-execution-world-identity" &&
                    issue.Path.EndsWith(
                        "/context/executionWorldId",
                        StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsAListenerWorldWithNoExecutionWorldIdentity()
    {
        // The context identity is what correlates records from one world, so a
        // record that names a world without it cannot be grouped with the rest
        // of that world's evidence.
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            CreateListenerWithWorld(null, "isolated", 13));
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == "browser-execution-world-identity");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsAListenerWorldKindTheSchemaDoesNotAllow()
    {
        // The recorder names the world types Blink has, and reports a type it
        // does not name as other. A kind outside that set is a defect in the
        // recorder rather than a fact about the session.
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            CreateListenerWithWorld("world-13", "extension", 13));
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
                    issue.Code == "payload-property-invalid" &&
                    issue.Path.EndsWith("/world/kind", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A registration made by a page's own script belongs to the main world,
    // which Blink numbers 0 and holds no human readable name or stable
    // identifier for. Every listener record reports the world its callback
    // belongs to, so the fixtures below name the main world rather than
    // leaving the world unreported.
    private static object MainWorld() =>
        new
        {
            kind = "main",
            blinkWorldId = 0,
            name = (string?)null,
            stableId = (string?)null
        };

    // Builds one listener registration whose context world identity, world kind
    // and Blink world identifier the caller chooses, so a test can state the
    // agreement or the contradiction it is about and nothing else.
    private static object CreateListenerWithWorld(
        string? executionWorldId,
        string kind,
        int blinkWorldId) =>
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
                documentId = "dom-document-8",
                executionWorldId,
                documentToken = "document-token-8"
            },
            listenerId = "listener-1",
            eventName = "click",
            registrationKind = "add-event-listener",
            target = new
            {
                kind = "node",
                interfaceName = "HTMLButtonElement",
                targetId = (string?)null,
                documentId = "dom-document-8",
                nodeId = 42,
                backendNodeId = (string?)null,
                tagName = "BUTTON",
                elementId = "pointer-only",
                classes = Array.Empty<string>()
            },
            capture = false,
            passive = false,
            once = false,
            location = (object?)null,
            world = new
            {
                kind,
                blinkWorldId,
                name = "recorder probe",
                stableId = "probe-world"
            }
        };

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
        var listenerContext = new
        {
            browserInstanceId = "browser-1",
            processId = 1200,
            processType = "renderer",
            profileId = (string?)null,
            browserContextId = (string?)null,
            pageId = (string?)null,
            frameId = (string?)null,
            documentId = "dom-document-8",
            executionWorldId = "world-0",
            documentToken = "document-token-8"
        };
        var listener = new
        {
            context = listenerContext,
            listenerId = "listener-1",
            eventName = "click",
            registrationKind = "add-event-listener",
            target,
            capture = false,
            passive = false,
            once = false,
            location = (object?)null,
            world = MainWorld()
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
                pathScopes = new object[]
                {
                    new
                    {
                        treeScopeRootNodeId = (long?)null,
                        shadowRootMode = (string?)null,
                        targetNodeId = (long?)null,
                        relatedTargetNodeId = (long?)null,
                        visiblePathIndexes = new[] { 0 },
                        unmatchedVisibleTargetCount = 0
                    }
                },
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

    public static TheoryData<string, string> CookieRecords()
    {
        var data = new TheoryData<string, string>();
        foreach (var (eventType, json) in BrowserCookiePayloads.All())
        {
            data.Add(eventType, json);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CookieRecords))]
    public async Task AcceptsEveryCookieRecordShape(string eventType, string json)
    {
        var issues = await ValidateCookieRecordAsync(eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(CookieRecords))]
    public async Task RejectsCookieValuesAtTheArchiveBoundary(
        string eventType,
        string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["value"] = "must-not-be-recorded";

        var issues = await ValidateCookieRecordAsync(eventType, payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsCookieValuesInsideCookieAccessEntries()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.CookieAccess)!;
        payload["cookies"]![1]!["value"] = "must-not-be-recorded";

        var issues = await ValidateCookieRecordAsync("cookie-access", payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/cookies/1/value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsACookieCountThatDisagreesWithItsList()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.DocumentCookieRead)!;
        payload["cookieCount"] = 3;

        var issues = await ValidateCookieRecordAsync("document-cookie-read", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-cookie-count");
    }

    [Fact]
    public async Task AcceptsATruncatedCookieListWithALargerCount()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.DocumentCookieRead)!;
        payload["cookieCount"] = 300;
        payload["cookieNamesTruncated"] = true;

        var issues = await ValidateCookieRecordAsync("document-cookie-read", payload);

        Assert.Empty(issues);
    }

    [Fact]
    public async Task RejectsAnUnparsedCookieAccessEntryThatReportsAttributes()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.CookieAccess)!;
        payload["cookies"]![1]!["domain"] = "example.test";

        var issues = await ValidateCookieRecordAsync("cookie-access", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-cookie-access-entry-shape");
    }

    [Fact]
    public async Task RejectsACookieStoreReadThatReportsWriteAttributes()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.CookieStoreWriteRequest)!;
        payload["method"] = "get";

        var issues = await ValidateCookieRecordAsync("cookie-store-request", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-cookie-store-attributes");
    }

    [Fact]
    public async Task RejectsACookieStoreWriteResultThatReportsNames()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.CookieStoreReadResult)!;
        payload["method"] = "set";

        var issues = await ValidateCookieRecordAsync("cookie-store-result", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-cookie-store-result-shape");
    }

    [Fact]
    public async Task RejectsANavigationCookieAccessWithoutItsNavigation()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.CookieAccess)!;
        payload["observer"] = "navigation";

        var issues = await ValidateCookieRecordAsync("cookie-access", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-cookie-access-observer");
    }

    [Fact]
    public async Task RejectsTheRetiredCookieOperationRecord()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.DocumentCookieRead)!;

        var issues = await ValidateCookieRecordAsync("cookie-operation", payload);

        Assert.Contains(issues, issue => issue.Code == "event-type-unsupported");
    }

    private static async Task<IReadOnlyList<ArchiveValidationIssue>>
        ValidateCookieRecordAsync(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Cookie,
            eventType,
            document.RootElement.Clone());
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);
            return result.Issues.ToList();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static TheoryData<string, string> InteractionRecords()
    {
        var data = new TheoryData<string, string>();
        foreach (var (eventType, json) in BrowserInteractionPayloads.All())
        {
            data.Add(eventType, json);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InteractionRecords))]
    public async Task AcceptsEveryInteractionRecordShape(string eventType, string json)
    {
        var issues = await ValidateInteractionRecordAsync(
            eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(InteractionRecords))]
    public async Task RejectsAnUndeclaredInteractionProperty(
        string eventType,
        string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["undeclared"] = 1;

        var issues = await ValidateInteractionRecordAsync(eventType, payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/undeclared", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAFocusOutcomeTheNodesDoNotSupport()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.ScriptFocusChanged)!;
        payload["focusedNodeId"] = 45;

        var issues = await ValidateInteractionRecordAsync("focus-changed", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-focus-outcome-inconsistent");
    }

    [Fact]
    public async Task RejectsAnActiveDescendantWithoutFocus()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.FocusCleared)!;
        payload["activeDescendantNodeId"] = 52;

        var issues = await ValidateInteractionRecordAsync("focus-changed", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-focus-active-descendant-without-focus");
    }

    [Fact]
    public async Task RejectsAnEmptySelectionThatReportsPositions()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.TextControlSelection)!;
        payload["selectionType"] = "none";

        var issues = await ValidateInteractionRecordAsync("selection-changed", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-selection-positions-inconsistent");
    }

    [Fact]
    public async Task RejectsAPartialTextControlSelection()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.TextControlSelection)!;
        payload["textControlSelectionDirection"] = null;

        var issues = await ValidateInteractionRecordAsync("selection-changed", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-selection-text-control-inconsistent");
    }

    [Fact]
    public async Task RejectsAReversedTextControlValueSelection()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.UserEditedValue)!;
        payload["selectionStart"] = 3;
        payload["selectionEnd"] = 1;

        var issues = await ValidateInteractionRecordAsync(
            "text-control-value-changed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-selection-range-reversed");
    }

    [Fact]
    public async Task RejectsATextControlValueLengthThatDisagreesWithTheValue()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.UserEditedValue)!;
        payload["valueLength"] = 4;

        var issues = await ValidateInteractionRecordAsync(
            "text-control-value-changed", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-dom-text-truncation-inconsistent");
    }

    [Fact]
    public async Task RejectsARecordedValueLongerThanItsMaximum()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.UserEditedValue)!;
        payload["maximumValueLength"] = 2;

        var issues = await ValidateInteractionRecordAsync(
            "text-control-value-changed", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-text-control-value-over-maximum");
    }

    [Theory]
    [InlineData("browser.dom", "rendering-update", "dom-checkpoint-3")]
    [InlineData("browser.layout", "post-mutation", "layout-checkpoint-12")]
    public async Task RejectsAnInteractionCheckpointReasonItsSourceDoesNotRecord(
        string sourceChannel,
        string reason,
        string sourceCheckpointId)
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.LayoutCheckpointStarted)!;
        payload["sourceChannel"] = sourceChannel;
        payload["reason"] = reason;
        payload["sourceCheckpointId"] = sourceCheckpointId;

        var issues = await ValidateInteractionRecordAsync(
            "interaction-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-interaction-checkpoint-reason-inconsistent");
    }

    [Theory]
    [InlineData("dom-checkpoint-12")]
    [InlineData("layout-checkpoint-")]
    [InlineData("layout-checkpoint-012")]
    [InlineData("layout-checkpoint-1x")]
    public async Task RejectsAnInteractionCheckpointSourceOfAnotherChannel(
        string sourceCheckpointId)
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.LayoutCheckpointStarted)!;
        payload["sourceCheckpointId"] = sourceCheckpointId;

        var issues = await ValidateInteractionRecordAsync(
            "interaction-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-interaction-checkpoint-source-invalid");
    }

    [Fact]
    public async Task RejectsAMalformedInteractionCheckpointIdentity()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.CheckpointCompleted)!;
        payload["checkpointId"] = "layout-checkpoint-7";

        var issues = await ValidateInteractionRecordAsync(
            "interaction-checkpoint-completed", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-interaction-checkpoint-id-invalid");
    }

    [Fact]
    public async Task RejectsAnInteractionCheckpointActiveDescendantWithoutFocus()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.LayoutCheckpointStarted)!;
        payload["focusedNodeId"] = null;

        var issues = await ValidateInteractionRecordAsync(
            "interaction-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue => issue.Code ==
                "browser-interaction-checkpoint-active-descendant-without-focus");
    }

    [Fact]
    public async Task RejectsVisibleFocusWithoutAFocusedElement()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.DomCheckpointStarted)!;
        payload["focusVisible"] = true;

        var issues = await ValidateInteractionRecordAsync(
            "interaction-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue => issue.Code ==
                "browser-interaction-checkpoint-focus-visible-without-focus");
    }

    [Fact]
    public async Task RejectsAnInteractionCheckpointSelectionWithoutPositions()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.DomCheckpointStarted)!;
        payload["focusOffset"] = null;

        var issues = await ValidateInteractionRecordAsync(
            "interaction-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue => issue.Code ==
                "browser-interaction-checkpoint-selection-inconsistent");
    }

    [Fact]
    public async Task RejectsAnInteractionCheckpointTextControlLengthMismatch()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.CheckpointTextControl)!;
        payload["valueLength"] = 12;

        var issues = await ValidateInteractionRecordAsync(
            "interaction-checkpoint-text-control", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-dom-text-truncation-inconsistent");
    }

    [Fact]
    public async Task RejectsMoreInteractionCheckpointTextControlsThanTheMaximum()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.CheckpointCompleted)!;
        payload["textControlCount"] = 513;

        var issues = await ValidateInteractionRecordAsync(
            "interaction-checkpoint-completed", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-interaction-checkpoint-count-over-maximum");
    }

    [Fact]
    public async Task AcceptsAnInteractionOmissionRecord()
    {
        var payload = JsonNode.Parse("""
            {
              "context": {
                "browserInstanceId": "browser-1",
                "processId": 3440,
                "processType": "renderer",
                "profileId": null,
                "browserContextId": null,
                "pageId": null,
                "frameId": null,
                "documentId": null,
                "executionWorldId": null,
                "documentToken": null
              },
              "reason": "browser-evidence-write-failed",
              "count": 2
            }
            """)!;

        var issues = await ValidateInteractionRecordAsync("collector-omission", payload);

        Assert.Empty(issues);
    }

    public static TheoryData<string, string> LayoutRecords()
    {
        var data = new TheoryData<string, string>();
        foreach (var (eventType, json) in BrowserLayoutPayloads.All())
        {
            data.Add(eventType, json);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LayoutRecords))]
    public async Task AcceptsEveryLayoutRecordShape(string eventType, string json)
    {
        var issues = await ValidateLayoutRecordAsync(eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(LayoutRecords))]
    public async Task RejectsAnUndeclaredLayoutProperty(string eventType, string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["undeclared"] = 1;

        var issues = await ValidateLayoutRecordAsync(eventType, payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/undeclared", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAnUndeclaredViewportProperty()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.FirstCheckpointStarted)!;
        payload["viewport"]!["depth"] = 1;

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/viewport/depth", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsADuplicatedStyleProperty()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.FirstCheckpointStarted)!;
        payload["styleProperties"] = new JsonArray("display", "display");

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-started", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-layout-style-properties-invalid");
    }

    [Fact]
    public async Task RejectsACheckpointThatNamesItselfAsPrevious()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.LaterCheckpointStarted)!;
        payload["previousCheckpointId"] = "layout-checkpoint-2";

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-started", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-layout-checkpoint-previous-self");
    }

    [Fact]
    public async Task RejectsARectangleWithoutALayoutObject()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ElementNode)!;
        payload["layoutObjectPresent"] = false;

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-node", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-layout-rect-inconsistent");
    }

    [Fact]
    public async Task RejectsANegativeRectangleSize()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ElementNode)!;
        payload["boundingClientRect"]!["width"] = -1;

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-node", payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/boundingClientRect/width", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsANonStringStyleValue()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ElementNode)!;
        payload["computedStyle"]!["width"] = 120;

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-node", payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/computedStyle", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsATextNodeWithAComputedStyle()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.TextNode)!;
        payload["computedStyle"] = new JsonObject { ["color"] = "rgb(0, 0, 0)" };

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-node", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-layout-text-node-inconsistent");
    }

    [Fact]
    public async Task RejectsANodeCountAboveTheMaximum()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.CheckpointCompleted)!;
        payload["maximumNodes"] = 5;

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-completed", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-layout-node-count-over-maximum");
    }

    public static TheoryData<string, string> NetworkRecords()
    {
        var data = new TheoryData<string, string>();
        foreach (var (eventType, json) in BrowserNetworkPayloads.All())
        {
            data.Add(eventType, json);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NetworkRecords))]
    public async Task AcceptsEveryNetworkRecordShape(string eventType, string json)
    {
        var issues = await ValidateNetworkRecordAsync(eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(NetworkRecords))]
    public async Task RejectsAnUndeclaredNetworkProperty(string eventType, string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["undeclared"] = 1;

        var issues = await ValidateNetworkRecordAsync(eventType, payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/undeclared", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsARecordedCredentialHeaderValue()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.RequestHeadersSent)!;
        var cookie = payload["headers"]![1]!;
        cookie["value"] = "session=abc";
        cookie["valueRedacted"] = false;
        cookie["redactionReason"] = null;

        var issues = await ValidateNetworkRecordAsync("request-headers-sent", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-network-credential-header-value");
    }

    [Fact]
    public async Task RejectsAWithheldHeaderThatCarriesAValue()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.RequestWillBeSent)!;
        payload["request"]!["headers"]![2]!["value"] = "secret";

        var issues = await ValidateNetworkRecordAsync("request-will-be-sent", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-header-redaction");
    }

    [Fact]
    public async Task RejectsAHeaderCountThatDisagreesWithTheList()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.NavigationResponse)!;
        payload["requestHeaderCount"] = 4;

        var issues = await ValidateNetworkRecordAsync("navigation-response", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-header-count");
    }

    [Fact]
    public async Task RejectsARedirectWithoutARedirectResponse()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.RedirectRequestWillBeSent)!;
        payload["redirectResponse"] = null;

        var issues = await ValidateNetworkRecordAsync("request-will-be-sent", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-redirect-response");
    }

    [Fact]
    public async Task RejectsAWithheldOffsetThatMissesTheMarker()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebSocketMessageSent)!;
        payload["payload"]!["withheld"]![0]!["offset"] = 3;

        var issues = await ValidateNetworkRecordAsync("websocket-message-sent", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-withheld-offset");
    }

    [Fact]
    public async Task RejectsARecordedTextOverTheLimit()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.EventSourceMessage)!;
        payload["data"]!["text"] = new string('a', 4097);

        var issues = await ValidateNetworkRecordAsync("event-source-message", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-text-too-long");
    }

    [Fact]
    public async Task RejectsABinaryMessageThatCarriesText()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebSocketBinaryMessageReceived)!;
        payload["payload"] = JsonNode.Parse(
            """{ "text": "x", "truncated": false, "withheld": [] }""");

        var issues = await ValidateNetworkRecordAsync("websocket-message-received", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-realtime-inconsistent");
    }

    [Fact]
    public async Task RejectsADisconnectedChannelThatReportsACloseCode()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebSocketDisconnected)!;
        payload["code"] = 1006;

        var issues = await ValidateNetworkRecordAsync("websocket-closed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-realtime-inconsistent");
    }

    [Fact]
    public async Task RejectsAnAbruptWebTransportCloseWithACode()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebTransportClosed)!;
        payload["code"] = 0.0;

        var issues = await ValidateNetworkRecordAsync("web-transport-closed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-realtime-inconsistent");
    }

    [Fact]
    public async Task RejectsARecordedSetCookieValueOnAHandshake()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebSocketHandshakeResponse)!;
        var cookie = payload["headers"]![1]!;
        cookie["value"] = "room_pref=blue";
        cookie["valueRedacted"] = false;
        cookie["redactionReason"] = null;

        var issues = await ValidateNetworkRecordAsync("websocket-handshake-response", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-network-credential-header-value");
    }

    [Fact]
    public async Task RejectsANonDecimalTransportId()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebTransportCreated)!;
        payload["transportId"] = "wt-1";

        var issues = await ValidateNetworkRecordAsync("web-transport-created", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-inspector-id-invalid");
    }

    [Fact]
    public async Task RejectsANonDecimalInspectorId()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.RequestFinished)!;
        payload["inspectorId"] = "request-17";

        var issues = await ValidateNetworkRecordAsync("request-finished", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-inspector-id-invalid");
    }

    [Fact]
    public async Task AcceptsAnUnreportedTransferredLengthAndRejectsANegativeOne()
    {
        var response = JsonNode.Parse(BrowserNetworkPayloads.ResponseReceived)!;
        response["response"]!["encodedDataLength"] = null;
        var finished = JsonNode.Parse(BrowserNetworkPayloads.RequestFinished)!;
        finished["encodedDataLength"] = null;

        Assert.Empty(await ValidateNetworkRecordAsync("response-received", response));
        Assert.Empty(await ValidateNetworkRecordAsync("request-finished", finished));

        finished["encodedDataLength"] = -1.0;
        var issues = await ValidateNetworkRecordAsync("request-finished", finished);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/encodedDataLength", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAnUndeclaredTimingPhase()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.ResponseReceived)!;
        payload["response"]!["timing"]!["bodyStart"] = 1.0;

        var issues = await ValidateNetworkRecordAsync("response-received", payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/timing/bodyStart", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAnUnparsedWireCookieWithAttributes()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.ResponseHeadersReceived)!;
        payload["cookies"]![0]!["domain"] = "example.test";

        var issues = await ValidateNetworkRecordAsync("response-headers-received", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-cookie-access-entry-shape");
    }

    [Fact]
    public async Task AcceptsANetworkOmission()
    {
        var payload = JsonNode.Parse(
            """
            { "reason": "browser-evidence-write-failed", "count": 3 }
            """)!;

        var issues = await ValidateNetworkRecordAsync("collector-omission", payload);

        Assert.Empty(issues);
    }

    private static async Task<IReadOnlyList<ArchiveValidationIssue>>
        ValidateNetworkRecordAsync(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Network,
            eventType,
            document.RootElement.Clone());
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);
            return result.Issues.ToList();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static TheoryData<string, string, string> ShadowDomRecords() =>
        new()
        {
            { BrowserEvidenceChannels.Dom, "dom-checkpoint-node", BrowserShadowDomPayloads.ShadowRootNode },
            { BrowserEvidenceChannels.Dom, "dom-checkpoint-shadow-root", BrowserShadowDomPayloads.ShadowRoot },
            { BrowserEvidenceChannels.Dom, "dom-checkpoint-slot-assignment", BrowserShadowDomPayloads.SlotAssignment },
            { BrowserEvidenceChannels.Dispatch, "dispatch-started", BrowserShadowDomPayloads.DispatchStarted }
        };

    [Theory]
    [MemberData(nameof(ShadowDomRecords))]
    public async Task AcceptsEveryShadowDomRecordShape(
        string channel,
        string eventType,
        string json)
    {
        var issues = await ValidateRecordAsync(channel, eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(ShadowDomRecords))]
    public async Task RejectsAnUndeclaredShadowDomProperty(
        string channel,
        string eventType,
        string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["undeclared"] = 1;

        var issues = await ValidateRecordAsync(channel, eventType, payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/undeclared", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAnUnknownShadowRootMode()
    {
        var payload = JsonNode.Parse(BrowserShadowDomPayloads.ShadowRoot)!;
        payload["mode"] = "hidden";

        var issues = await ValidateRecordAsync(
            BrowserEvidenceChannels.Dom,
            "dom-checkpoint-shadow-root",
            payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/mode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsASlotAssignmentWhoseTruncationDisagreesWithItsCount()
    {
        var payload = JsonNode.Parse(BrowserShadowDomPayloads.SlotAssignment)!;
        payload["assignedNodeCount"] = 3;

        var issues = await ValidateRecordAsync(
            BrowserEvidenceChannels.Dom,
            "dom-checkpoint-slot-assignment",
            payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-dom-slot-assignment-inconsistent");
    }

    [Fact]
    public async Task RejectsPathScopesThatDoNotMatchTheComposedPath()
    {
        var payload = JsonNode.Parse(BrowserShadowDomPayloads.DispatchStarted)!;
        payload["pathScopes"]!.AsArray().RemoveAt(4);

        var issues = await ValidateRecordAsync(
            BrowserEvidenceChannels.Dispatch,
            "dispatch-started",
            payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-dispatch-path-scopes-inconsistent");
    }

    [Fact]
    public async Task RejectsAVisiblePathIndexOutsideTheComposedPath()
    {
        var payload = JsonNode.Parse(BrowserShadowDomPayloads.DispatchStarted)!;
        payload["pathScopes"]![2]!["visiblePathIndexes"]!.AsArray().Add(5);

        var issues = await ValidateRecordAsync(
            BrowserEvidenceChannels.Dispatch,
            "dispatch-started",
            payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith(
                    "/pathScopes/2/visiblePathIndexes",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAShadowRootModeWithoutItsScopeRoot()
    {
        var payload = JsonNode.Parse(BrowserShadowDomPayloads.DispatchStarted)!;
        payload["pathScopes"]![0]!["treeScopeRootNodeId"] = null;

        var issues = await ValidateRecordAsync(
            BrowserEvidenceChannels.Dispatch,
            "dispatch-started",
            payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-dispatch-path-scopes-inconsistent");
    }

    [Fact]
    public async Task RejectsAnElementRecordCarryingAPseudoElementDescription()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.PseudoElementNode)!;
        payload["nodeType"] = "element";

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-node", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-layout-pseudo-element-inconsistent");
    }

    [Fact]
    public async Task RejectsAPseudoElementWithoutItsDescription()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.PseudoElementNode)!;
        payload["pseudoElement"] = null;

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-node", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-layout-pseudo-element-inconsistent");
    }

    [Fact]
    public async Task RejectsGeneratedTextLongerThanItsReportedLength()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.PseudoElementNode)!;
        payload["pseudoElement"]!["generatedTextLength"] = 2;

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-node", payload);

        Assert.NotEmpty(issues);
    }

    [Fact]
    public async Task RejectsAShadowHostWithoutAShadowRootMode()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ShadowTreeElementNode)!;
        payload["shadowRootMode"] = null;

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-node", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-layout-shadow-scope-inconsistent");
    }

    [Fact]
    public async Task RejectsMorePseudoElementsThanNodes()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.CheckpointCompleted)!;
        payload["pseudoElementCount"] = 10;

        var issues = await ValidateLayoutRecordAsync("layout-checkpoint-completed", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-layout-pseudo-element-count-over-node-count");
    }

    private static async Task<IReadOnlyList<ArchiveValidationIssue>>
        ValidateRecordAsync(string channel, string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(0, 100, channel, eventType, document.RootElement.Clone());
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);
            return result.Issues.ToList();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<IReadOnlyList<ArchiveValidationIssue>>
        ValidateLayoutRecordAsync(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Layout,
            eventType,
            document.RootElement.Clone());
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);
            return result.Issues.ToList();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static TheoryData<string, string> PresentationRecords()
    {
        var data = new TheoryData<string, string>();
        foreach (var (eventType, json) in BrowserPresentationPayloads.All())
        {
            data.Add(eventType, json);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PresentationRecords))]
    public async Task AcceptsEveryPresentationRecordShape(string eventType, string json)
    {
        var issues = await ValidatePresentationRecordAsync(
            eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(PresentationRecords))]
    public async Task RejectsAnUndeclaredPresentationProperty(
        string eventType,
        string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["undeclared"] = 1;

        var issues = await ValidatePresentationRecordAsync(eventType, payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/undeclared", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("requestId", "presentation-7", "browser-presentation-request-id-invalid")]
    [InlineData("requestId", "presentation-request-07", "browser-presentation-request-id-invalid")]
    [InlineData("layoutCheckpointId", "interaction-checkpoint-12", "browser-presentation-checkpoint-id-invalid")]
    [InlineData("notQueuedReason", "no-widget", "browser-presentation-request-inconsistent")]
    public async Task RejectsAnInconsistentPresentationRequest(
        string property,
        string value,
        string code)
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.QueuedRequest)!;
        payload[property] = value;

        var issues = await ValidatePresentationRecordAsync(
            "presentation-requested", payload);

        Assert.Contains(issues, issue => issue.Code == code);
    }

    [Fact]
    public async Task RejectsARequestWithoutAWidgetThatNamesAFrameNumber()
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.RequestWithoutWidget)!;
        payload["sourceFrameNumber"] = 4;

        var issues = await ValidatePresentationRecordAsync(
            "presentation-requested", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-presentation-request-inconsistent");
    }

    [Theory]
    [InlineData("frameSinkId", "3-2")]
    [InlineData("frameSinkId", "3:")]
    [InlineData("frameSinkId", "4294967296:2")]
    public async Task RejectsAMalformedFrameSinkIdentity(string property, string value)
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.Swapped)!;
        payload[property] = value;

        var issues = await ValidatePresentationRecordAsync(
            "presentation-swapped", payload);

        Assert.Contains(issues, issue => issue.Code == "payload-property-invalid");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4294967296")]
    [InlineData("017")]
    [InlineData("-3")]
    public async Task RejectsAFrameTokenOutsideTheUnsignedRange(string token)
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.Swapped)!;
        payload["frameToken"] = token;

        var issues = await ValidatePresentationRecordAsync(
            "presentation-swapped", payload);

        Assert.Contains(issues, issue => issue.Code == "payload-property-invalid");
    }

    [Theory]
    [InlineData("commit-fails", "broken")]
    [InlineData("activation-fails", "broken")]
    [InlineData("swap-fails", "kept-active")]
    [InlineData("commit-no-update", "kept-active")]
    public async Task RejectsANotSwappedActionChromiumWouldNotTake(
        string reason,
        string action)
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.KeptActive)!;
        payload["reason"] = reason;
        payload["action"] = action;

        var issues = await ValidatePresentationRecordAsync(
            "presentation-not-swapped", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-presentation-not-swapped-action-inconsistent");
    }

    [Fact]
    public async Task RejectsANotSwappedCountThatDoesNotFollowTheIndex()
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.KeptActive)!;
        payload["notSwappedCount"] = 3;

        var issues = await ValidatePresentationRecordAsync(
            "presentation-not-swapped", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-presentation-not-swapped-count-inconsistent");
    }

    [Theory]
    [InlineData("vsync", "vsync")]
    [InlineData("vsync", "tearing")]
    public async Task RejectsFeedbackFlagsChromiumDoesNotDefine(string first, string second)
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.Feedback)!;
        payload["flags"] = new JsonArray(first, second);

        var issues = await ValidatePresentationRecordAsync(
            "presentation-feedback", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-presentation-feedback-flags-invalid");
    }

    [Fact]
    public async Task RejectsCounterTicksFromALowResolutionClock()
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.Feedback)!;
        payload["highResolutionTicks"] = false;

        var issues = await ValidatePresentationRecordAsync(
            "presentation-feedback", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-presentation-ticks-without-high-resolution");
    }

    [Fact]
    public async Task RejectsCounterTicksWithoutChromiumTime()
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.Feedback)!;
        payload["presentedTimeTicksMicroseconds"] = null;

        var issues = await ValidatePresentationRecordAsync(
            "presentation-feedback", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-presentation-ticks-without-time");
    }

    [Fact]
    public async Task AcceptsAPresentationOmission()
    {
        var payload = JsonNode.Parse(
            """{"reason":"browser-evidence-write-failed","count":2}""")!;

        var issues = await ValidatePresentationRecordAsync("collector-omission", payload);

        Assert.Empty(issues);
    }

    private const string WindowsGraphicsCaptureFrame = """
        {
          "path": "frames/desktop/0000000000.png",
          "x": 0,
          "y": 0,
          "width": 1920,
          "height": 1080,
          "stride": 7680,
          "pixelFormat": "B8G8R8A8",
          "encodedFormat": "png",
          "byteLength": 1024,
          "captureDurationNanoseconds": 20000000,
          "framesPerSecond": 2,
          "backend": "windows-graphics-capture",
          "monitorCount": 1,
          "fallbackReason": null,
          "gdiFallbackFrameCount": 0,
          "frameSelection": "newest-arrived",
          "monitorFrames": [
            {
              "monitorHandle": 65537,
              "x": 0,
              "y": 0,
              "width": 1920,
              "height": 1080,
              "systemRelativeTimeTicks": 1000167000,
              "compositedAtNanoseconds": 16700000,
              "dequeuedAtNanoseconds": 31200000,
              "tryGetNextFrameAttempts": 1,
              "supersededFrameCount": 3,
              "reusedPreviousImage": false
            }
          ]
        }
        """;

    [Fact]
    public async Task AcceptsADesktopFrameWithMonitorCompositionTimes()
    {
        var issues = await ValidateDesktopFrameAsync(
            JsonNode.Parse(WindowsGraphicsCaptureFrame)!);

        Assert.Empty(issues);
    }

    [Fact]
    public async Task AcceptsADesktopFrameWrittenBeforeMonitorCompositionTimes()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!.AsObject();
        payload.Remove("frameSelection");
        payload.Remove("monitorFrames");

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Empty(issues);
    }

    [Fact]
    public async Task AcceptsADesktopFrameWrittenBeforeNewestArrivedSelection()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!.AsObject();
        payload.Remove("frameSelection");
        var monitor = payload["monitorFrames"]![0]!.AsObject();
        monitor.Remove("supersededFrameCount");
        monitor.Remove("reusedPreviousImage");

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Empty(issues);
    }

    [Fact]
    public async Task AcceptsAReusedPreviousImage()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        var monitor = payload["monitorFrames"]![0]!;
        monitor["supersededFrameCount"] = 0;
        monitor["reusedPreviousImage"] = true;

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("supersededFrameCount")]
    [InlineData("reusedPreviousImage")]
    public async Task RejectsANewestArrivedFrameWithoutMonitorSelectionFields(
        string nulledProperty)
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["monitorFrames"]![0]![nulledProperty] = null;

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-monitor-frame-selection-inconsistent");
    }

    [Fact]
    public async Task RejectsMonitorSelectionFieldsWithoutAFrameSelection()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!.AsObject();
        payload.Remove("frameSelection");

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-monitor-frame-selection-inconsistent");
    }

    [Fact]
    public async Task RejectsAReusedImageThatReleasedArrivedFrames()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["monitorFrames"]![0]!["reusedPreviousImage"] = true;

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-monitor-frame-selection-inconsistent");
    }

    [Fact]
    public async Task RejectsAFrameSelectionOnAGdiFallbackFrame()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["backend"] = "gdi-bitblt";
        payload["monitorCount"] = null;
        var monitor = payload["monitorFrames"]![0]!;
        foreach (var property in new[]
        {
            "systemRelativeTimeTicks",
            "compositedAtNanoseconds",
            "dequeuedAtNanoseconds",
            "tryGetNextFrameAttempts",
            "supersededFrameCount",
            "reusedPreviousImage"
        })
        {
            monitor[property] = null;
        }

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-frame-selection-inconsistent");
    }

    [Fact]
    public async Task RejectsAnUndeclaredFrameSelection()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["frameSelection"] = "oldest-queued";

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/frameSelection", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AcceptsAGdiFallbackFrameWithoutCompositionTimes()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["backend"] = "gdi-bitblt";
        payload["monitorCount"] = null;
        payload["frameSelection"] = null;
        var monitor = payload["monitorFrames"]![0]!;
        monitor["systemRelativeTimeTicks"] = null;
        monitor["compositedAtNanoseconds"] = null;
        monitor["dequeuedAtNanoseconds"] = null;
        monitor["tryGetNextFrameAttempts"] = null;
        monitor["supersededFrameCount"] = null;
        monitor["reusedPreviousImage"] = null;

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("windows-graphics-capture", "compositedAtNanoseconds")]
    [InlineData("windows-graphics-capture", "dequeuedAtNanoseconds")]
    [InlineData("windows-graphics-capture", "tryGetNextFrameAttempts")]
    [InlineData("gdi-bitblt", null)]
    public async Task RejectsMonitorTimingThatDoesNotMatchTheBackend(
        string backend,
        string? nulledProperty)
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["backend"] = backend;
        if (nulledProperty is not null)
        {
            payload["monitorFrames"]![0]![nulledProperty] = null;
        }

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-monitor-frame-timing-inconsistent");
    }

    [Fact]
    public async Task AcceptsAMonitorImageWhoseCompositionTimeFollowsItsDequeue()
    {
        // Windows reported SystemRelativeTime up to one display refresh after
        // the pool delivered the frame.
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        var composedAt = payload["monitorFrames"]![0]!["compositedAtNanoseconds"]!.GetValue<long>();
        payload["monitorFrames"]![0]!["dequeuedAtNanoseconds"] = composedAt - 15_000_000;

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Empty(issues);
    }

    [Fact]
    public async Task RejectsMonitorFramesThatDoNotMatchTheMonitorCount()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["monitorCount"] = 2;

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-monitor-frame-count-inconsistent");
    }

    [Fact]
    public async Task RejectsAnUndeclaredMonitorFrameProperty()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["monitorFrames"]![0]!["undeclared"] = 1;

        var issues = await ValidateDesktopFrameAsync(payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/monitorFrames/0/undeclared", StringComparison.Ordinal));
    }

    private const string UiaDropEpisode = """
        {
          "reason": "uia-observation-queue-full",
          "count": 7,
          "firstDroppedAtNanoseconds": 40,
          "lastDroppedAtNanoseconds": 100,
          "droppedByObservationType": { "property-changed": 6, "focus-changed": 1 }
        }
        """;

    private const string UiaPropertyChange = """
        {
          "eventId": "AutomationElementIdentifiers.NameProperty",
          "changeType": null,
          "runtimeId": null,
          "newValue": "Next",
          "element": {
            "processId": 42,
            "nativeWindowHandle": 0,
            "automationId": "",
            "name": "Next",
            "className": "Button",
            "frameworkId": "WPF",
            "controlType": "ControlType.Button",
            "localizedControlType": "button",
            "hasKeyboardFocus": false,
            "isKeyboardFocusable": true,
            "isEnabled": true,
            "isOffscreen": false,
            "boundingRectangle": { "x": 0, "y": 0, "width": 10, "height": 10 },
            "propertySource": "event-cache",
            "qualityFlags": []
          }
        }
        """;

    [Fact]
    public async Task AcceptsAUiaDropEpisode()
    {
        var issues = await ValidateUiaRecordAsync(
            "collector-omission",
            JsonNode.Parse(UiaDropEpisode)!);

        Assert.Empty(issues);
    }

    [Fact]
    public async Task AcceptsAUiaQueueFullOmissionWrittenBeforeDropEpisodes()
    {
        var payload = JsonNode.Parse("""
            { "reason": "uia-observation-queue-full", "count": 3226 }
            """)!;

        var issues = await ValidateUiaRecordAsync("collector-omission", payload, 13641458100);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("firstDroppedAtNanoseconds")]
    [InlineData("lastDroppedAtNanoseconds")]
    [InlineData("droppedByObservationType")]
    [InlineData("count")]
    public async Task RejectsAUiaDropEpisodeMissingAField(string removed)
    {
        var payload = JsonNode.Parse(UiaDropEpisode)!.AsObject();
        payload.Remove(removed);

        var issues = await ValidateUiaRecordAsync("collector-omission", payload);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Fact]
    public async Task RejectsAUiaDropEpisodeWhoseCountsDoNotSumToItsCount()
    {
        var payload = JsonNode.Parse(UiaDropEpisode)!;
        payload["count"] = 8;

        var issues = await ValidateUiaRecordAsync("collector-omission", payload);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Fact]
    public async Task RejectsAUiaDropEpisodeThatEndsBeforeItBegins()
    {
        var payload = JsonNode.Parse(UiaDropEpisode)!;
        payload["firstDroppedAtNanoseconds"] = 101;

        var issues = await ValidateUiaRecordAsync("collector-omission", payload);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Fact]
    public async Task RejectsAUiaDropEpisodeNotTimedAtItsLastRefusal()
    {
        var issues = await ValidateUiaRecordAsync(
            "collector-omission",
            JsonNode.Parse(UiaDropEpisode)!,
            101);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Theory]
    [InlineData("selection-changed", 7)]
    [InlineData("property-changed", 0)]
    public async Task RejectsAUiaDropEpisodeWithAnInvalidTypeCount(string type, int value)
    {
        var payload = JsonNode.Parse(UiaDropEpisode)!;
        payload["droppedByObservationType"] = new JsonObject { [type] = value };
        payload["count"] = value;

        var issues = await ValidateUiaRecordAsync("collector-omission", payload);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Fact]
    public async Task RejectsADropEpisodeOnAnotherUiaOmissionReason()
    {
        var payload = JsonNode.Parse(UiaDropEpisode)!;
        payload["reason"] = "uia-provider-read-timeout";

        var issues = await ValidateUiaRecordAsync("collector-omission", payload);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Theory]
    [InlineData("event-cache")]
    [InlineData("current-read")]
    [InlineData(null)]
    public async Task AcceptsEachUiaPropertySource(string? source)
    {
        var payload = JsonNode.Parse(UiaPropertyChange)!;
        if (source is null)
        {
            payload["element"]!.AsObject().Remove("propertySource");
        }
        else
        {
            payload["element"]!["propertySource"] = source;
        }

        var issues = await ValidateUiaRecordAsync("property-changed", payload);

        Assert.Empty(issues);
    }

    [Fact]
    public async Task RejectsAnUndeclaredUiaPropertySource()
    {
        var payload = JsonNode.Parse(UiaPropertyChange)!;
        payload["element"]!["propertySource"] = "guessed";

        var issues = await ValidateUiaRecordAsync("property-changed", payload);

        Assert.NotEmpty(issues);
    }

    private static async Task<IReadOnlyList<ArchiveValidationIssue>>
        ValidateUiaRecordAsync(string eventType, JsonNode payload, long timestamp = 100)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            timestamp,
            "accessibility.uia.events",
            eventType,
            document.RootElement.Clone());
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);
            return result.Issues.ToList();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<IReadOnlyList<ArchiveValidationIssue>>
        ValidateDesktopFrameAsync(JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            "graphics.desktop.frames",
            "desktop-frame",
            document.RootElement.Clone());
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);
            return result.Issues.ToList();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<IReadOnlyList<ArchiveValidationIssue>>
        ValidatePresentationRecordAsync(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Presentation,
            eventType,
            document.RootElement.Clone());
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);
            return result.Issues.ToList();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<IReadOnlyList<ArchiveValidationIssue>>
        ValidateInteractionRecordAsync(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Interaction,
            eventType,
            document.RootElement.Clone());
        var directory = await CreateArchiveAsync([record]);

        try
        {
            var result = await SessionArchiveValidator.ValidateAsync(
                directory,
                TestContext.Current.CancellationToken);
            return result.Issues.ToList();
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

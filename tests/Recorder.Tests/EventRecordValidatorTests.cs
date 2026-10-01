using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The checks each event passes before the database stores it.
/// </summary>
public sealed class EventRecordValidatorTests
{
    [Fact]
    public void AcceptsValidEvents()
    {
        IReadOnlyList<RecorderEvent> events = (
            [CreateEvent(0, 100), CreateEvent(1, 200)]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void DetectsSequenceAndTimestampRegression()
    {
        IReadOnlyList<RecorderEvent> events = (
            [CreateEvent(2, 200), CreateEvent(1, 100)]);
        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "event-sequence-not-increasing");
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "event-time-regressed");
    }

    [Fact]
    public void AcceptsTimestampOverlapAcrossIndependentClockMappings()
    {
        IReadOnlyList<RecorderEvent> events = (
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
        var result = Validate(events);

        Assert.True(
            result.IsValid,
            JsonSerializer.Serialize(result.Issues, JsonOptions));
    }

    [Fact]
    public void DetectsInvalidBuiltInChannelPayload()
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
        IReadOnlyList<RecorderEvent> events = ([record]);
        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "payload-property-missing" &&
                issue.Path.EndsWith("/extraInformation", StringComparison.Ordinal));
    }

    [Fact]
    public void DetectsUnknownEventOnBuiltInChannel()
    {
        var record = CreateEvent(
            0,
            100,
            "input.mouse",
            "future-mouse-event",
            new { value = 1 });
        IReadOnlyList<RecorderEvent> events = ([record]);
        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "event-type-unsupported");
    }

    [Fact]
    public void AcceptsInstrumentedBrowserListenerEvidence()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AcceptsInstrumentedBrowserDispatchStartEvidence()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AcceptsAWindowEventTargetWithoutANodeIdentifier()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void RejectsANonNodeEventTargetThatClaimsANodeIdentifier()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

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

    // Protocol 0.31 names the execution context of every listener and
    // dispatch record. A worker scope belongs to no document, so its records
    // name none and can hold only a non-Node target, while a record that names
    // no scope, as earlier archives do, must still name its document.
    [Fact]
    public void AcceptsAWorkerListenerThatNamesNoDocument()
    {
        var issues = ValidateListenerScope(
            contextDocumentId: null,
            targetKind: "other",
            targetDocumentId: null,
            scopeKind: "dedicated-worker");

        Assert.Empty(issues);
    }

    [Fact]
    public void RejectsANodeTargetInAWorkerScope()
    {
        var issues = ValidateListenerScope(
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
    public void RejectsAWorkerRecordThatNamesADocument()
    {
        var issues = ValidateListenerScope(
            contextDocumentId: "dom-document-8",
            targetKind: "other",
            targetDocumentId: "dom-document-8",
            scopeKind: "shared-worker");

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-event-scope-inconsistent" &&
                issue.Path == "#/payload/context/documentId");
        Assert.Contains(
            issues,
            issue => issue.Code == "browser-event-scope-inconsistent" &&
                issue.Path == "#/payload/target/documentId");
    }

    [Fact]
    public void RejectsATargetWithoutADocumentOutsideAWorkerScope()
    {
        var unscoped = ValidateListenerScope(
            contextDocumentId: "dom-document-8",
            targetKind: "other",
            targetDocumentId: null,
            scopeKind: null);
        var window = ValidateListenerScope(
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

    private static IReadOnlyList<EventValidationIssue> ValidateListenerScope(
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
        IReadOnlyList<RecorderEvent> events = ([record]);
        var result = Validate(events);
        return result.Issues.ToList();
    }

    [Fact]
    public void RejectsANodeEventTargetWithoutANodeIdentifier()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "browser-event-target-identity" &&
                issue.Message.Contains(
                    "must report its nodeId",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsInstrumentedBrowserDefaultActionEvidence()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AcceptsABrowserOmissionThatStatesLostRecords()
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
        IReadOnlyList<RecorderEvent> events = ([attributed, unattributed]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AcceptsBrowserExitAndRejectsItWithoutItsRequester()
    {
        // The exit record states whether the recorder asked for the exit, so
        // a record that omits it cannot be read as either.
        var exited = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Lifecycle,
            BrowserEvidenceEventTypes.Exited,
            new
            {
                browserInstanceId = "browser-1",
                processId = 26860,
                exitCode = unchecked((int)0x80000003),
                exitCodeHex = "0x80000003",
                exitedUtc = (DateTimeOffset?)null,
                requestedByRecorder = false
            });
        var incomplete = CreateEvent(
            1,
            200,
            BrowserEvidenceChannels.Lifecycle,
            BrowserEvidenceEventTypes.Exited,
            new
            {
                browserInstanceId = "browser-1",
                processId = 26860,
                exitCode = 0,
                exitCodeHex = "0x00000000",
                exitedUtc = DateTimeOffset.UtcNow
            });
        IReadOnlyList<RecorderEvent> validEvents = ([exited]);
        IReadOnlyList<RecorderEvent> invalidEvents = ([incomplete]);

        var valid = Validate(validEvents);
        var invalid = Validate(invalidEvents);

        Assert.True(valid.IsValid);
        Assert.Empty(valid.Issues);
        Assert.False(invalid.IsValid);
        Assert.Contains(
            invalid.Issues,
            issue => issue.Code == "payload-property-missing");
    }

    [Fact]
    public void RejectsABrowserOmissionThatReportsAnUnknownFact()
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
        IReadOnlyList<RecorderEvent> events = ([omission]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "payload-property-unexpected");
    }

    [Fact]
    public void AcceptsBrowserLifecycleEvidence()
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
        IReadOnlyList<RecorderEvent> events = ([connected, synchronized]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void RejectsBrowserLifecycleEvidenceThatReportsAnUnknownFact()
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
        IReadOnlyList<RecorderEvent> events = ([connected]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "payload-property-unexpected");
    }

    [Fact]
    public void AcceptsBrowserAccessibilityCheckpointEvidence()
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
        IReadOnlyList<RecorderEvent> events = ([started, node, completed]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void RejectsAnAccessibilityNodeThatReportsAnUnknownFact()
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
        IReadOnlyList<RecorderEvent> events = ([node]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "payload-property-unexpected");
    }

    [Fact]
    public void RejectsAnAccessibilityCheckpointFromOutsideARenderer()
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
        IReadOnlyList<RecorderEvent> events = ([started]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "browser-accessibility-context-invalid");
    }

    [Fact]
    public void AcceptsInstrumentedBrowserTimerEvidence()
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
        IReadOnlyList<RecorderEvent> events = ([scheduled, fired]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AcceptsInstrumentedBrowserAnimationFrameEvidence()
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
        IReadOnlyList<RecorderEvent> events = ([scheduled, fired]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AcceptsInstrumentedBrowserIdleCallbackEvidence()
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
        IReadOnlyList<RecorderEvent> events = ([scheduled, fired]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AcceptsInstrumentedBrowserSchedulerDecisionEvidence()
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
        IReadOnlyList<RecorderEvent> events = ([deferred]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AcceptsInstrumentedBrowserNavigationEvidence()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void RejectsCommittedNavigationWithoutRendererDocumentCorrelation()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

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

    [Fact]
    public void RejectsSubframeNavigationWithoutParentIdentity()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code ==
                "browser-navigation-subframe-parent-mismatch");
    }

    [Fact]
    public void AcceptsInstrumentedBrowserDomCheckpointEvidence()
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
                    walkReason = "first",
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
                    slotCount = 0,
                    characterDataCount = 0
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
                    walkReason = "check",
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
                    slotCount = 0,
                    characterDataCount = 0
                })
        };
        IReadOnlyList<RecorderEvent> events = (records);

        var result = Validate(events);

        Assert.True(result.IsValid, JsonSerializer.Serialize(result.Issues));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AcceptsInstrumentedBrowserDomStateChangeEvidence()
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
        IReadOnlyList<RecorderEvent> events = (records);

        var result = Validate(events);

        Assert.True(result.IsValid, JsonSerializer.Serialize(result.Issues));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AcceptsTruncatedDomAttributeValueThatReportsItsFullLength()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.True(result.IsValid, JsonSerializer.Serialize(result.Issues));
    }

    [Fact]
    public void AcceptsDomCheckpointCharacterDataWithItsFullLength()
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Dom,
            BrowserEvidenceEventTypes.DomCheckpointNodeCharacterData,
            new
            {
                context = CreateRendererDocumentContext(),
                checkpointId = "dom-checkpoint-1",
                nodeId = 12,
                data = "Save changes",
                dataLength = 12,
                dataTruncated = false,
                maximumValueLength = 2147483647
            });
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.True(result.IsValid, JsonSerializer.Serialize(result.Issues));
    }

    [Fact]
    public void RejectsDomCheckpointCharacterDataLengthWithoutTruncationState()
    {
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Dom,
            BrowserEvidenceEventTypes.DomCheckpointNodeCharacterData,
            new
            {
                context = CreateRendererDocumentContext(),
                checkpointId = "dom-checkpoint-1",
                nodeId = 12,
                data = "Save",
                dataLength = 12,
                dataTruncated = false,
                maximumValueLength = 2147483647
            });
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "browser-dom-text-truncation-inconsistent");
    }

    [Fact]
    public void RejectsDomAttributeValueLengthWithoutTruncationState()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "browser-dom-text-truncation-inconsistent");
    }

    [Theory]
    [InlineData(0, "dom-transition-1", "dom-transition-2")]
    [InlineData(0, "dom-transition-1", null)]
    [InlineData(2, null, null)]
    [InlineData(2, "dom-transition-1", null)]
    public void RejectsDomCheckpointWithHalfStatedTransitionCoverage(
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
                slotCount = 0,
                characterDataCount = 0
            });
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "browser-dom-checkpoint-coverage-inconsistent");
    }

    [Theory]
    [InlineData("added", "true", "false")]
    [InlineData("removed", "true", null)]
    [InlineData("changed", null, "false")]
    public void RejectsDomAttributeChangeThatContradictsItsChangeType(
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "browser-dom-attribute-change-inconsistent");
    }

    [Fact]
    public void RejectsDomCharacterDataChangeWithoutDocumentToken()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "browser-dom-context-invalid");
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
    public void RejectsDomCheckpointWithoutDocumentToken()
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
                walkReason = "first",
                maximumNodes = 512
            });
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "browser-dom-context-invalid");
    }

    [Fact]
    public void AcceptsEveryListenerRegistrationFormAtTheArchiveBoundary()
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
        IReadOnlyList<RecorderEvent> events = (records);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void RejectsAListenerRegistrationFormTheSchemaDoesNotDefine()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/registrationKind", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAListenerLocationLineTheSchemaDoesNotAllow()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/location/line", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsAListenerRegisteredFromAnIsolatedWorld()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RejectsAListenerWorldItsContextContradicts()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "browser-execution-world-identity" &&
                issue.Path.EndsWith(
                    "/context/executionWorldId",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAListenerWorldWithNoExecutionWorldIdentity()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "browser-execution-world-identity");
    }

    [Fact]
    public void RejectsAListenerWorldKindTheSchemaDoesNotAllow()
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
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/world/kind", StringComparison.Ordinal));
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
    public void AcceptsCorrelatedBrowserListenerLifecycleEvidence()
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
        IReadOnlyList<RecorderEvent> events = (records);

        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
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
    public void AcceptsEveryCookieRecordShape(string eventType, string json)
    {
        var issues = ValidateCookieRecord(eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(CookieRecords))]
    public void RejectsCookieValuesAtTheArchiveBoundary(
        string eventType,
        string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["value"] = "must-not-be-recorded";

        var issues = ValidateCookieRecord(eventType, payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/value", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsCookieValuesInsideCookieAccessEntries()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.CookieAccess)!;
        payload["cookies"]![1]!["value"] = "must-not-be-recorded";

        var issues = ValidateCookieRecord("cookie-access", payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/cookies/1/value", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsACookieCountThatDisagreesWithItsList()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.DocumentCookieRead)!;
        payload["cookieCount"] = 3;

        var issues = ValidateCookieRecord("document-cookie-read", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-cookie-count");
    }

    [Fact]
    public void AcceptsATruncatedCookieListWithALargerCount()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.DocumentCookieRead)!;
        payload["cookieCount"] = 300;
        payload["cookieNamesTruncated"] = true;

        var issues = ValidateCookieRecord("document-cookie-read", payload);

        Assert.Empty(issues);
    }

    [Fact]
    public void RejectsAnUnparsedCookieAccessEntryThatReportsAttributes()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.CookieAccess)!;
        payload["cookies"]![1]!["domain"] = "example.test";

        var issues = ValidateCookieRecord("cookie-access", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-cookie-access-entry-shape");
    }

    [Fact]
    public void RejectsACookieStoreReadThatReportsWriteAttributes()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.CookieStoreWriteRequest)!;
        payload["method"] = "get";

        var issues = ValidateCookieRecord("cookie-store-request", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-cookie-store-attributes");
    }

    [Fact]
    public void RejectsACookieStoreWriteResultThatReportsNames()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.CookieStoreReadResult)!;
        payload["method"] = "set";

        var issues = ValidateCookieRecord("cookie-store-result", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-cookie-store-result-shape");
    }

    [Fact]
    public void RejectsANavigationCookieAccessWithoutItsNavigation()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.CookieAccess)!;
        payload["observer"] = "navigation";

        var issues = ValidateCookieRecord("cookie-access", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-cookie-access-observer");
    }

    [Fact]
    public void RejectsTheRetiredCookieOperationRecord()
    {
        var payload = JsonNode.Parse(BrowserCookiePayloads.DocumentCookieRead)!;

        var issues = ValidateCookieRecord("cookie-operation", payload);

        Assert.Contains(issues, issue => issue.Code == "event-type-unsupported");
    }

    private static IReadOnlyList<EventValidationIssue> ValidateCookieRecord(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Cookie,
            eventType,
            document.RootElement.Clone());
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);
        return result.Issues.ToList();
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
    public void AcceptsEveryInteractionRecordShape(string eventType, string json)
    {
        var issues = ValidateInteractionRecord(
            eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(InteractionRecords))]
    public void RejectsAnUndeclaredInteractionProperty(
        string eventType,
        string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["undeclared"] = 1;

        var issues = ValidateInteractionRecord(eventType, payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/undeclared", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAFocusOutcomeTheNodesDoNotSupport()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.ScriptFocusChanged)!;
        payload["focusedNodeId"] = 45;

        var issues = ValidateInteractionRecord("focus-changed", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-focus-outcome-inconsistent");
    }

    [Fact]
    public void RejectsAnActiveDescendantWithoutFocus()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.FocusCleared)!;
        payload["activeDescendantNodeId"] = 52;

        var issues = ValidateInteractionRecord("focus-changed", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-focus-active-descendant-without-focus");
    }

    [Fact]
    public void RejectsAnEmptySelectionThatReportsPositions()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.TextControlSelection)!;
        payload["selectionType"] = "none";

        var issues = ValidateInteractionRecord("selection-changed", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-selection-positions-inconsistent");
    }

    [Fact]
    public void RejectsAPartialTextControlSelection()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.TextControlSelection)!;
        payload["textControlSelectionDirection"] = null;

        var issues = ValidateInteractionRecord("selection-changed", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-selection-text-control-inconsistent");
    }

    [Fact]
    public void RejectsAReversedTextControlValueSelection()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.UserEditedValue)!;
        payload["selectionStart"] = 3;
        payload["selectionEnd"] = 1;

        var issues = ValidateInteractionRecord(
            "text-control-value-changed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-selection-range-reversed");
    }

    [Fact]
    public void RejectsATextControlValueLengthThatDisagreesWithTheValue()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.UserEditedValue)!;
        payload["valueLength"] = 4;

        var issues = ValidateInteractionRecord(
            "text-control-value-changed", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-dom-text-truncation-inconsistent");
    }

    [Fact]
    public void RejectsARecordedValueLongerThanItsMaximum()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.UserEditedValue)!;
        payload["maximumValueLength"] = 2;

        var issues = ValidateInteractionRecord(
            "text-control-value-changed", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-text-control-value-over-maximum");
    }

    [Theory]
    [InlineData("browser.dom", "rendering-update", "dom-checkpoint-3")]
    [InlineData("browser.layout", "post-mutation", "layout-checkpoint-12")]
    public void RejectsAnInteractionCheckpointReasonItsSourceDoesNotRecord(
        string sourceChannel,
        string reason,
        string sourceCheckpointId)
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.LayoutCheckpointStarted)!;
        payload["sourceChannel"] = sourceChannel;
        payload["reason"] = reason;
        payload["sourceCheckpointId"] = sourceCheckpointId;

        var issues = ValidateInteractionRecord(
            "interaction-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-interaction-checkpoint-reason-inconsistent");
    }

    [Theory]
    [InlineData("browser.layout", "rendering-update", null, "layout-changes-7")]
    [InlineData("browser.dom", "post-mutation", null, null)]
    public void AcceptsAnInteractionCheckpointAfterAnUpdateThatWasNotWalked(
        string sourceChannel,
        string reason,
        string? sourceCheckpointId,
        string? sourceChangeSetId)
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.LayoutCheckpointStarted)!;
        payload["sourceChannel"] = sourceChannel;
        payload["reason"] = reason;
        payload["sourceCheckpointId"] = sourceCheckpointId;
        payload["sourceChangeSetId"] = sourceChangeSetId;

        Assert.Empty(ValidateInteractionRecord("interaction-checkpoint-started", payload));
    }

    [Theory]
    [InlineData("browser.layout", "rendering-update", null, null, "browser-interaction-checkpoint-source-inconsistent")]
    [InlineData("browser.layout", "rendering-update", "layout-checkpoint-12", "layout-changes-7", "browser-interaction-checkpoint-source-inconsistent")]
    [InlineData("browser.dom", "finished-parsing", null, null, "browser-interaction-checkpoint-source-inconsistent")]
    [InlineData("browser.dom", "post-mutation", null, "layout-changes-7", "browser-interaction-checkpoint-source-inconsistent")]
    [InlineData("browser.layout", "rendering-update", null, "layout-checkpoint-7", "browser-interaction-checkpoint-change-set-invalid")]
    public void RejectsAnInteractionCheckpointWithSourcesItsChannelDoesNotName(
        string sourceChannel,
        string reason,
        string? sourceCheckpointId,
        string? sourceChangeSetId,
        string code)
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.LayoutCheckpointStarted)!;
        payload["sourceChannel"] = sourceChannel;
        payload["reason"] = reason;
        payload["sourceCheckpointId"] = sourceCheckpointId;
        payload["sourceChangeSetId"] = sourceChangeSetId;

        var issues = ValidateInteractionRecord("interaction-checkpoint-started", payload);

        Assert.Contains(issues, issue => issue.Code == code);
    }

    [Theory]
    [InlineData("dom-checkpoint-12")]
    [InlineData("layout-checkpoint-")]
    [InlineData("layout-checkpoint-012")]
    [InlineData("layout-checkpoint-1x")]
    public void RejectsAnInteractionCheckpointSourceOfAnotherChannel(
        string sourceCheckpointId)
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.LayoutCheckpointStarted)!;
        payload["sourceCheckpointId"] = sourceCheckpointId;

        var issues = ValidateInteractionRecord(
            "interaction-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-interaction-checkpoint-source-invalid");
    }

    [Fact]
    public void RejectsAMalformedInteractionCheckpointIdentity()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.CheckpointCompleted)!;
        payload["checkpointId"] = "layout-checkpoint-7";

        var issues = ValidateInteractionRecord(
            "interaction-checkpoint-completed", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-interaction-checkpoint-id-invalid");
    }

    [Fact]
    public void RejectsAnInteractionCheckpointActiveDescendantWithoutFocus()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.LayoutCheckpointStarted)!;
        payload["focusedNodeId"] = null;

        var issues = ValidateInteractionRecord(
            "interaction-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue => issue.Code ==
                "browser-interaction-checkpoint-active-descendant-without-focus");
    }

    [Fact]
    public void RejectsVisibleFocusWithoutAFocusedElement()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.DomCheckpointStarted)!;
        payload["focusVisible"] = true;

        var issues = ValidateInteractionRecord(
            "interaction-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue => issue.Code ==
                "browser-interaction-checkpoint-focus-visible-without-focus");
    }

    [Fact]
    public void RejectsAnInteractionCheckpointSelectionWithoutPositions()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.DomCheckpointStarted)!;
        payload["focusOffset"] = null;

        var issues = ValidateInteractionRecord(
            "interaction-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue => issue.Code ==
                "browser-interaction-checkpoint-selection-inconsistent");
    }

    [Fact]
    public void RejectsAnInteractionCheckpointTextControlLengthMismatch()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.CheckpointTextControl)!;
        payload["valueLength"] = 12;

        var issues = ValidateInteractionRecord(
            "interaction-checkpoint-text-control", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-dom-text-truncation-inconsistent");
    }

    [Fact]
    public void RejectsMoreInteractionCheckpointTextControlsThanTheMaximum()
    {
        var payload = JsonNode.Parse(BrowserInteractionPayloads.CheckpointCompleted)!;
        payload["textControlCount"] = 513;

        var issues = ValidateInteractionRecord(
            "interaction-checkpoint-completed", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-interaction-checkpoint-count-over-maximum");
    }

    [Fact]
    public void AcceptsAnInteractionOmissionRecord()
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

        var issues = ValidateInteractionRecord("collector-omission", payload);

        Assert.Empty(issues);
    }

    public static TheoryData<string, string> DomChangeRecords()
    {
        var data = new TheoryData<string, string>();
        foreach (var (eventType, json) in BrowserDomChangePayloads.All())
        {
            data.Add(eventType, json);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DomChangeRecords))]
    public void AcceptsEveryDomChangeRecordShape(string eventType, string json)
    {
        var issues = ValidateRecord(BrowserEvidenceChannels.Dom, eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(DomChangeRecords))]
    public void RejectsAnUndeclaredPropertyInEveryDomChangeRecord(string eventType, string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["undeclared"] = 1;

        var issues = ValidateRecord(BrowserEvidenceChannels.Dom, eventType, payload);

        Assert.NotEmpty(issues);
    }

    [Fact]
    public void RejectsADomChangeRecordWithoutATransitionIdentity()
    {
        var payload = JsonNode.Parse(BrowserDomChangePayloads.InsertedNode)!;
        payload["insertionId"] = "dom-checkpoint-4";

        var issues = ValidateRecord(BrowserEvidenceChannels.Dom, "dom-inserted-node", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-dom-transition-id-invalid");
    }

    [Fact]
    public void RejectsAShadowRootInsertionWithAPreviousSibling()
    {
        var payload = JsonNode.Parse(BrowserDomChangePayloads.ShadowRootInserted)!;
        payload["previousSiblingNodeId"] = 3;

        var issues = ValidateRecord(BrowserEvidenceChannels.Dom, "dom-node-inserted", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-dom-shadow-root-insertion-sibling");
    }

    [Fact]
    public void RejectsAnAssignmentCurrentFlagOnASlotAssignmentChange()
    {
        var payload = JsonNode.Parse(BrowserDomChangePayloads.SlotAssignmentChanged)!;
        payload["assignmentCurrent"] = true;

        var issues = ValidateRecord(BrowserEvidenceChannels.Dom, "dom-slot-assignment-changed", payload);

        Assert.NotEmpty(issues);
    }

    [Fact]
    public void RejectsAScrollOffsetWithoutItsCoordinates()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ScrollOffsetChanged)!;
        payload["scrollOffset"]!.AsObject().Remove("y");

        var issues = ValidateLayoutRecord("layout-scroll-offset-changed", payload);

        Assert.NotEmpty(issues);
    }

    [Fact]
    public void RejectsALayoutChangeCompletionWithoutItsScrollOffsetCount()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangesCompleted)!;
        payload.AsObject().Remove("scrollOffsetCount");

        var issues = ValidateLayoutRecord("layout-changes-completed", payload);

        Assert.NotEmpty(issues);
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
    public void AcceptsEveryLayoutRecordShape(string eventType, string json)
    {
        var issues = ValidateLayoutRecord(eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(LayoutRecords))]
    public void RejectsAnUndeclaredLayoutProperty(string eventType, string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["undeclared"] = 1;

        var issues = ValidateLayoutRecord(eventType, payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/undeclared", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsALayoutTransformMatrixOfTheWrongLength()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ScrollTransformNode)!;
        payload["matrix"] = new JsonArray(1, 0, 0, 1);

        var issues = ValidateLayoutRecord("layout-transform-node", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/matrix", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsALayoutTransformNodeThatIsItsOwnParent()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ScrollTransformNode)!;
        payload["parentTransformNodeId"] = "layout-transform-2";

        var issues = ValidateLayoutRecord("layout-transform-node", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-layout-transform-node-parent-self");
    }

    [Theory]
    [InlineData("changeSetId", "layout-checkpoint-1", "browser-layout-change-set-id-invalid")]
    [InlineData("changeSetId", "layout-changes-0", "browser-layout-change-set-id-invalid")]
    [InlineData("viewTransformNodeId", "transform-1", "browser-layout-transform-node-id-invalid")]
    [InlineData("layoutCheckpointId", "layout-changes-1", "browser-layout-change-checkpoint-id-invalid")]
    public void RejectsALayoutChangeIdentityOfTheWrongForm(string property, string value, string code)
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangesStarted)!;
        payload[property] = value;

        var issues = ValidateLayoutRecord("layout-changes-started", payload);

        Assert.Contains(issues, issue => issue.Code == code);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"style\", \"style\"]")]
    [InlineData("[\"scroll\"]")]
    public void RejectsInvalidLayoutChangeReasons(string reasons)
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedElementNode)!;
        payload["reasons"] = JsonNode.Parse(reasons);

        var issues = ValidateLayoutRecord("layout-node-changed", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/reasons", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsALocalRectThatWasNotMapped()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedElementNode)!;
        payload["geometry"]!["localRectMapped"] = false;

        var issues = ValidateLayoutRecord("layout-node-changed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-layout-local-rect-inconsistent");
    }

    [Fact]
    public void AcceptsStyleChangesAndCustomProperties()
    {
        Assert.Empty(ValidateLayoutRecord(
            "layout-node-changed",
            JsonNode.Parse(BrowserLayoutPayloads.ChangedElementStyleChanges)!));

        var complete = JsonNode.Parse(BrowserLayoutPayloads.ChangedElementNode)!;
        complete["computedStyleComplete"] = true;
        complete["customProperties"] = JsonNode.Parse("""{"--gap":"4px"}""");
        complete["removedCustomProperties"] = null;
        Assert.Empty(ValidateLayoutRecord("layout-node-changed", complete));

        var checkpoint = JsonNode.Parse(BrowserLayoutPayloads.ElementNode)!;
        checkpoint["customProperties"] = JsonNode.Parse("""{"--gap":"4px"}""");
        Assert.Empty(ValidateLayoutRecord("layout-checkpoint-node", checkpoint));
    }

    [Theory]
    [InlineData("removedCustomProperties", "null", "browser-layout-style-changes-incomplete")]
    [InlineData("customProperties", "null", "browser-layout-style-changes-incomplete")]
    [InlineData("computedStyleComplete", "true", "browser-layout-style-removals-in-complete-style")]
    [InlineData("customProperties", """{"gap":"4px"}""", "payload-property-invalid")]
    [InlineData("removedCustomProperties", """["--gap","--gap"]""", "payload-property-invalid")]
    [InlineData("computedStyleComplete", "\"no\"", "payload-property-invalid")]
    public void RejectsInconsistentStyleChanges(string property, string value, string code)
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedElementStyleChanges)!;
        payload[property] = JsonNode.Parse(value);

        var issues = ValidateLayoutRecord("layout-node-changed", payload);

        Assert.Contains(issues, issue => issue.Code == code);
    }

    [Fact]
    public void RejectsStyleCompletenessWithoutAComputedStyle()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedElementStyleChanges)!;
        payload["computedStyle"] = null;

        var issues = ValidateLayoutRecord("layout-node-changed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-layout-custom-properties-without-style");
        Assert.Contains(issues, issue => issue.Code == "browser-layout-style-completeness-without-style");

        var checkpoint = JsonNode.Parse(BrowserLayoutPayloads.ElementNode)!;
        checkpoint["computedStyle"] = null;
        checkpoint["customProperties"] = JsonNode.Parse("""{"--gap":"4px"}""");
        Assert.Contains(
            ValidateLayoutRecord("layout-checkpoint-node", checkpoint),
            issue => issue.Code == "browser-layout-custom-properties-without-style");
    }

    [Fact]
    public void AcceptsBoxFragmentsInBothNodeRecords()
    {
        Assert.Empty(ValidateLayoutRecord(
            "layout-node-changed",
            JsonNode.Parse(BrowserLayoutPayloads.ChangedBoxedElementNode)!));
        Assert.Empty(ValidateLayoutRecord(
            "layout-checkpoint-node",
            JsonNode.Parse(BrowserLayoutPayloads.BoxedElementNode)!));

        var unboxed = JsonNode.Parse(BrowserLayoutPayloads.ChangedBoxedElementNode)!;
        unboxed["boxFragments"] = null;
        Assert.Empty(ValidateLayoutRecord("layout-node-changed", unboxed));

        // A break before has no sequence number.
        var before = JsonNode.Parse(BrowserLayoutPayloads.ChangedBoxedElementNode)!;
        var token = before["boxFragments"]!["fragments"]![0]!["breakToken"]!;
        token["breakBefore"] = true;
        token["sequenceNumber"] = null;
        Assert.Empty(ValidateLayoutRecord("layout-node-changed", before));
    }

    [Theory]
    [InlineData("children/0/nodeId", "null", "browser-layout-fragment-child-node-inconsistent")]
    [InlineData("children/1/nodeId", "7", "browser-layout-fragment-child-node-inconsistent")]
    [InlineData("children/3/fragmentIndex", "0", "browser-layout-fragment-child-node-inconsistent")]
    [InlineData("children/1/fragment", "null", "browser-layout-fragment-child-fragment-inconsistent")]
    [InlineData("children/0/fragment", """{"width":1,"height":1,"breakToken":null,"scrollableOverflow":null,"children":[]}""", "browser-layout-fragment-child-fragment-inconsistent")]
    [InlineData("children/0/kind", "\"inline\"", "payload-property-invalid")]
    [InlineData("children/1/fragment/width", "-1", "payload-property-invalid")]
    [InlineData("breakToken/sequenceNumber", "null", "browser-layout-break-token-inconsistent")]
    [InlineData("scrollableOverflow/height", "-1", "payload-property-invalid")]
    [InlineData("width", "\"80px\"", "payload-property-invalid")]
    public void RejectsInconsistentBoxFragments(string path, string value, string code)
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedBoxedElementNode)!;
        var parts = path.Split('/');
        var target = payload["boxFragments"]!["fragments"]![0]!;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            target = int.TryParse(parts[index], out var item) ? target[item]! : target[parts[index]]!;
        }
        target[parts[^1]] = JsonNode.Parse(value);

        var issues = ValidateLayoutRecord("layout-node-changed", payload);

        Assert.Contains(issues, issue => issue.Code == code);
    }

    [Fact]
    public void RejectsBoxFragmentsWithoutABox()
    {
        var text = JsonNode.Parse(BrowserLayoutPayloads.TextNode)!;
        text["boxFragments"] = JsonNode.Parse(BrowserLayoutPayloads.BoxFragmentsJson);
        Assert.Contains(
            ValidateLayoutRecord("layout-checkpoint-node", text),
            issue => issue.Code == "browser-layout-box-fragments-without-box");

        var unrendered = JsonNode.Parse(BrowserLayoutPayloads.ChangedBoxedElementNode)!;
        unrendered["layoutObjectPresent"] = false;
        unrendered["geometry"] = null;
        Assert.Contains(
            ValidateLayoutRecord("layout-node-changed", unrendered),
            issue => issue.Code == "browser-layout-box-fragments-without-box");

        var zoom = JsonNode.Parse(BrowserLayoutPayloads.BoxedElementNode)!;
        zoom["boxFragments"]!["effectiveZoom"] = 0;
        Assert.Contains(
            ValidateLayoutRecord("layout-checkpoint-node", zoom),
            issue => issue.Code == "payload-property-invalid");
    }

    [Fact]
    public void AcceptsTheBoundsOfEachQuad()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedElementNode)!;
        payload["geometry"]!["localQuadRects"] = JsonNode.Parse(
            """[{"x":10,"y":338.75,"width":150,"height":12},{"x":10,"y":350.75,"width":90,"height":13}]""");

        Assert.Empty(ValidateLayoutRecord("layout-node-changed", payload));
    }

    [Theory]
    [InlineData("""[{"x":10,"y":338.75,"width":150,"height":25}]""", "browser-layout-local-quad-rects-inconsistent")]
    [InlineData("""[{"x":0,"y":0,"width":-1,"height":1},{"x":0,"y":1,"width":1,"height":1}]""", "payload-property-invalid")]
    [InlineData("""[1,2]""", "payload-property-invalid")]
    public void RejectsInvalidQuadRects(string quadRects, string code)
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedElementNode)!;
        payload["geometry"]!["localQuadRects"] = JsonNode.Parse(quadRects);

        var issues = ValidateLayoutRecord("layout-node-changed", payload);

        Assert.Contains(issues, issue => issue.Code == code);
    }

    [Fact]
    public void RejectsQuadRectsWithoutAMappedRect()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedEmptyTextNode)!;
        payload["geometry"]!["localQuadRects"] = JsonNode.Parse(
            """[{"x":0,"y":0,"width":1,"height":1},{"x":0,"y":1,"width":1,"height":1}]""");

        var issues = ValidateLayoutRecord("layout-node-changed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-layout-local-quad-rects-inconsistent");
    }

    [Fact]
    public void RequiresTheQuadRectsProperty()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedElementNode)!;
        payload["geometry"]!.AsObject().Remove("localQuadRects");

        var issues = ValidateLayoutRecord("layout-node-changed", payload);

        Assert.Contains(issues, issue => issue.Path.EndsWith("/localQuadRects", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnEmptyClientRectWithALocalRect()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedElementNode)!;
        payload["geometry"]!["clientRectEmpty"] = true;

        var issues = ValidateLayoutRecord("layout-node-changed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-layout-local-rect-inconsistent");
    }

    [Fact]
    public void RejectsGeometryForANodeWithoutALayoutObject()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedElementNode)!;
        payload["layoutObjectPresent"] = false;

        var issues = ValidateLayoutRecord("layout-node-changed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-layout-geometry-inconsistent");
    }

    [Fact]
    public void RejectsLayoutChangeCountsThatExceedTheNotedNodes()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangesCompleted)!;
        payload["unchangedNodeCount"] = 3;

        var issues = ValidateLayoutRecord("layout-changes-completed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-layout-change-counts-inconsistent");
    }

    [Fact]
    public void RejectsALayoutCheckpointWalkedForAFinishedParse()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.LaterCheckpointStarted)!;
        payload["walkReason"] = "finished-parsing";

        var issues = ValidateLayoutRecord("layout-checkpoint-started", payload);

        Assert.Contains(issues, issue => issue.Code == "payload-property-invalid" &&
            issue.Path.EndsWith("/walkReason", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("post-mutation", "finished-parsing", true)]
    [InlineData("post-mutation", "check", false)]
    [InlineData("finished-parsing", "finished-parsing", false)]
    [InlineData("finished-parsing", "after-loss", false)]
    public void ChecksTheWalkReasonOfADomCheckpoint(string reason, string walkReason, bool rejected)
    {
        var payload = new JsonObject
        {
            ["context"] = JsonNode.Parse(BrowserLayoutPayloads.LaterCheckpointStarted)!["context"]!.DeepClone(),
            ["checkpointId"] = "dom-checkpoint-3",
            ["reason"] = reason,
            ["walkReason"] = walkReason,
            ["maximumNodes"] = 512
        };

        var issues = ValidateRecord(BrowserEvidenceChannels.Dom, "dom-checkpoint-started", payload);

        Assert.Equal(
            rejected,
            issues.Any(issue => issue.Code == "browser-dom-checkpoint-walk-reason-inconsistent"));
        if (!rejected)
        {
            Assert.Empty(issues);
        }
    }

    [Fact]
    public void RejectsAnUndeclaredLayoutGeometryProperty()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ChangedElementNode)!;
        payload["geometry"]!["boundingClientRect"] = 1;

        var issues = ValidateLayoutRecord("layout-node-changed", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/geometry/boundingClientRect", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnUndeclaredViewportProperty()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.FirstCheckpointStarted)!;
        payload["viewport"]!["depth"] = 1;

        var issues = ValidateLayoutRecord("layout-checkpoint-started", payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/viewport/depth", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsADuplicatedStyleProperty()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.FirstCheckpointStarted)!;
        payload["styleProperties"] = new JsonArray("display", "display");

        var issues = ValidateLayoutRecord("layout-checkpoint-started", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-layout-style-properties-invalid");
    }

    [Fact]
    public void RejectsACheckpointThatNamesItselfAsPrevious()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.LaterCheckpointStarted)!;
        payload["previousCheckpointId"] = "layout-checkpoint-2";

        var issues = ValidateLayoutRecord("layout-checkpoint-started", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-layout-checkpoint-previous-self");
    }

    [Fact]
    public void RejectsARectangleWithoutALayoutObject()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ElementNode)!;
        payload["layoutObjectPresent"] = false;

        var issues = ValidateLayoutRecord("layout-checkpoint-node", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-layout-rect-inconsistent");
    }

    [Fact]
    public void RejectsANegativeRectangleSize()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ElementNode)!;
        payload["boundingClientRect"]!["width"] = -1;

        var issues = ValidateLayoutRecord("layout-checkpoint-node", payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/boundingClientRect/width", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsANonStringStyleValue()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ElementNode)!;
        payload["computedStyle"]!["width"] = 120;

        var issues = ValidateLayoutRecord("layout-checkpoint-node", payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/computedStyle", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsATextNodeWithAComputedStyle()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.TextNode)!;
        payload["computedStyle"] = new JsonObject { ["color"] = "rgb(0, 0, 0)" };

        var issues = ValidateLayoutRecord("layout-checkpoint-node", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-layout-text-node-inconsistent");
    }

    [Fact]
    public void RejectsANodeCountAboveTheMaximum()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.CheckpointCompleted)!;
        payload["maximumNodes"] = 5;

        var issues = ValidateLayoutRecord("layout-checkpoint-completed", payload);

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
    public void AcceptsEveryNetworkRecordShape(string eventType, string json)
    {
        var issues = ValidateNetworkRecord(eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(NetworkRecords))]
    public void RejectsAnUndeclaredNetworkProperty(string eventType, string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["undeclared"] = 1;

        var issues = ValidateNetworkRecord(eventType, payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/undeclared", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsARecordedCredentialHeaderValue()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.RequestHeadersSent)!;
        var cookie = payload["headers"]![1]!;
        cookie["value"] = "session=abc";
        cookie["valueRedacted"] = false;
        cookie["redactionReason"] = null;

        var issues = ValidateNetworkRecord("request-headers-sent", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-network-credential-header-value");
    }

    [Fact]
    public void RejectsAWithheldHeaderThatCarriesAValue()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.RequestWillBeSent)!;
        payload["request"]!["headers"]![2]!["value"] = "secret";

        var issues = ValidateNetworkRecord("request-will-be-sent", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-header-redaction");
    }

    [Fact]
    public void RejectsAHeaderCountThatDisagreesWithTheList()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.NavigationResponse)!;
        payload["requestHeaderCount"] = 4;

        var issues = ValidateNetworkRecord("navigation-response", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-header-count");
    }

    [Fact]
    public void RejectsARedirectWithoutARedirectResponse()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.RedirectRequestWillBeSent)!;
        payload["redirectResponse"] = null;

        var issues = ValidateNetworkRecord("request-will-be-sent", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-redirect-response");
    }

    [Fact]
    public void RejectsAWithheldOffsetThatMissesTheMarker()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebSocketMessageSent)!;
        payload["payload"]!["withheld"]![0]!["offset"] = 3;

        var issues = ValidateNetworkRecord("websocket-message-sent", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-withheld-offset");
    }

    [Fact]
    public void AcceptsARecordedTextOfAnyLength()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.EventSourceMessage)!;
        payload["data"]!["text"] = new string('a', 1_000_000);

        var issues = ValidateNetworkRecord("event-source-message", payload);

        Assert.Empty(issues);
    }

    [Fact]
    public void RejectsABinaryMessageThatCarriesText()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebSocketBinaryMessageReceived)!;
        payload["payload"] = JsonNode.Parse(
            """{ "text": "x", "truncated": false, "withheld": [] }""");

        var issues = ValidateNetworkRecord("websocket-message-received", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-realtime-inconsistent");
    }

    [Fact]
    public void RejectsADisconnectedChannelThatReportsACloseCode()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebSocketDisconnected)!;
        payload["code"] = 1006;

        var issues = ValidateNetworkRecord("websocket-closed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-realtime-inconsistent");
    }

    [Fact]
    public void RejectsAnAbruptWebTransportCloseWithACode()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebTransportClosed)!;
        payload["code"] = 0.0;

        var issues = ValidateNetworkRecord("web-transport-closed", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-realtime-inconsistent");
    }

    [Fact]
    public void RejectsARecordedSetCookieValueOnAHandshake()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebSocketHandshakeResponse)!;
        var cookie = payload["headers"]![1]!;
        cookie["value"] = "room_pref=blue";
        cookie["valueRedacted"] = false;
        cookie["redactionReason"] = null;

        var issues = ValidateNetworkRecord("websocket-handshake-response", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-network-credential-header-value");
    }

    [Fact]
    public void RejectsANonDecimalTransportId()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.WebTransportCreated)!;
        payload["transportId"] = "wt-1";

        var issues = ValidateNetworkRecord("web-transport-created", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-inspector-id-invalid");
    }

    [Fact]
    public void RejectsANonDecimalInspectorId()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.RequestFinished)!;
        payload["inspectorId"] = "request-17";

        var issues = ValidateNetworkRecord("request-finished", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-network-inspector-id-invalid");
    }

    [Fact]
    public void AcceptsAnUnreportedTransferredLengthAndRejectsANegativeOne()
    {
        var response = JsonNode.Parse(BrowserNetworkPayloads.ResponseReceived)!;
        response["response"]!["encodedDataLength"] = null;
        var finished = JsonNode.Parse(BrowserNetworkPayloads.RequestFinished)!;
        finished["encodedDataLength"] = null;

        Assert.Empty(ValidateNetworkRecord("response-received", response));
        Assert.Empty(ValidateNetworkRecord("request-finished", finished));

        finished["encodedDataLength"] = -1.0;
        var issues = ValidateNetworkRecord("request-finished", finished);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/encodedDataLength", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnUndeclaredTimingPhase()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.ResponseReceived)!;
        payload["response"]!["timing"]!["bodyStart"] = 1.0;

        var issues = ValidateNetworkRecord("response-received", payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/timing/bodyStart", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnUnparsedWireCookieWithAttributes()
    {
        var payload = JsonNode.Parse(BrowserNetworkPayloads.ResponseHeadersReceived)!;
        payload["cookies"]![0]!["domain"] = "example.test";

        var issues = ValidateNetworkRecord("response-headers-received", payload);

        Assert.Contains(issues, issue => issue.Code == "browser-cookie-access-entry-shape");
    }

    [Fact]
    public void AcceptsANetworkOmission()
    {
        var payload = JsonNode.Parse(
            """
            { "reason": "browser-evidence-write-failed", "count": 3 }
            """)!;

        var issues = ValidateNetworkRecord("collector-omission", payload);

        Assert.Empty(issues);
    }

    private static IReadOnlyList<EventValidationIssue> ValidateNetworkRecord(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Network,
            eventType,
            document.RootElement.Clone());
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);
        return result.Issues.ToList();
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
    public void AcceptsEveryShadowDomRecordShape(
        string channel,
        string eventType,
        string json)
    {
        var issues = ValidateRecord(channel, eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(ShadowDomRecords))]
    public void RejectsAnUndeclaredShadowDomProperty(
        string channel,
        string eventType,
        string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["undeclared"] = 1;

        var issues = ValidateRecord(channel, eventType, payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-unexpected" &&
                issue.Path.EndsWith("/undeclared", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsAnUnknownShadowRootMode()
    {
        var payload = JsonNode.Parse(BrowserShadowDomPayloads.ShadowRoot)!;
        payload["mode"] = "hidden";

        var issues = ValidateRecord(
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
    public void RejectsASlotAssignmentWhoseTruncationDisagreesWithItsCount()
    {
        var payload = JsonNode.Parse(BrowserShadowDomPayloads.SlotAssignment)!;
        payload["assignedNodeCount"] = 3;

        var issues = ValidateRecord(
            BrowserEvidenceChannels.Dom,
            "dom-checkpoint-slot-assignment",
            payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-dom-slot-assignment-inconsistent");
    }

    [Fact]
    public void RejectsPathScopesThatDoNotMatchTheComposedPath()
    {
        var payload = JsonNode.Parse(BrowserShadowDomPayloads.DispatchStarted)!;
        payload["pathScopes"]!.AsArray().RemoveAt(4);

        var issues = ValidateRecord(
            BrowserEvidenceChannels.Dispatch,
            "dispatch-started",
            payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-dispatch-path-scopes-inconsistent");
    }

    [Fact]
    public void RejectsAVisiblePathIndexOutsideTheComposedPath()
    {
        var payload = JsonNode.Parse(BrowserShadowDomPayloads.DispatchStarted)!;
        payload["pathScopes"]![2]!["visiblePathIndexes"]!.AsArray().Add(5);

        var issues = ValidateRecord(
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
    public void RejectsAShadowRootModeWithoutItsScopeRoot()
    {
        var payload = JsonNode.Parse(BrowserShadowDomPayloads.DispatchStarted)!;
        payload["pathScopes"]![0]!["treeScopeRootNodeId"] = null;

        var issues = ValidateRecord(
            BrowserEvidenceChannels.Dispatch,
            "dispatch-started",
            payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-dispatch-path-scopes-inconsistent");
    }

    [Fact]
    public void RejectsAnElementRecordCarryingAPseudoElementDescription()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.PseudoElementNode)!;
        payload["nodeType"] = "element";

        var issues = ValidateLayoutRecord("layout-checkpoint-node", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-layout-pseudo-element-inconsistent");
    }

    [Fact]
    public void RejectsAPseudoElementWithoutItsDescription()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.PseudoElementNode)!;
        payload["pseudoElement"] = null;

        var issues = ValidateLayoutRecord("layout-checkpoint-node", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-layout-pseudo-element-inconsistent");
    }

    [Fact]
    public void RejectsGeneratedTextLongerThanItsReportedLength()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.PseudoElementNode)!;
        payload["pseudoElement"]!["generatedTextLength"] = 2;

        var issues = ValidateLayoutRecord("layout-checkpoint-node", payload);

        Assert.NotEmpty(issues);
    }

    [Fact]
    public void RejectsAShadowHostWithoutAShadowRootMode()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.ShadowTreeElementNode)!;
        payload["shadowRootMode"] = null;

        var issues = ValidateLayoutRecord("layout-checkpoint-node", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-layout-shadow-scope-inconsistent");
    }

    [Fact]
    public void RejectsMorePseudoElementsThanNodes()
    {
        var payload = JsonNode.Parse(BrowserLayoutPayloads.CheckpointCompleted)!;
        payload["pseudoElementCount"] = 10;

        var issues = ValidateLayoutRecord("layout-checkpoint-completed", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-layout-pseudo-element-count-over-node-count");
    }

    private static IReadOnlyList<EventValidationIssue> ValidateRecord(string channel, string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(0, 100, channel, eventType, document.RootElement.Clone());
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);
        return result.Issues.ToList();
    }

    private static IReadOnlyList<EventValidationIssue> ValidateLayoutRecord(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Layout,
            eventType,
            document.RootElement.Clone());
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);
        return result.Issues.ToList();
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
    public void AcceptsEveryPresentationRecordShape(string eventType, string json)
    {
        var issues = ValidatePresentationRecord(
            eventType, JsonNode.Parse(json)!);

        Assert.Empty(issues);
    }

    [Theory]
    [MemberData(nameof(PresentationRecords))]
    public void RejectsAnUndeclaredPresentationProperty(
        string eventType,
        string json)
    {
        var payload = JsonNode.Parse(json)!;
        payload["undeclared"] = 1;

        var issues = ValidatePresentationRecord(eventType, payload);

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
    public void RejectsAnInconsistentPresentationRequest(
        string property,
        string value,
        string code)
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.QueuedRequest)!;
        payload[property] = value;

        var issues = ValidatePresentationRecord(
            "presentation-requested", payload);

        Assert.Contains(issues, issue => issue.Code == code);
    }

    [Fact]
    public void AcceptsAPresentationRequestAfterALayoutChangeSet()
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.QueuedRequest)!;
        payload["layoutCheckpointId"] = null;
        payload["layoutChangeSetId"] = "layout-changes-4";

        Assert.Empty(ValidatePresentationRecord("presentation-requested", payload));
    }

    [Theory]
    [InlineData(null, null, "browser-presentation-source-inconsistent")]
    [InlineData("layout-checkpoint-12", "layout-changes-4", "browser-presentation-source-inconsistent")]
    [InlineData(null, "layout-checkpoint-4", "browser-presentation-change-set-id-invalid")]
    public void RejectsAPresentationRequestWithoutOneLayoutSource(
        string? checkpointId,
        string? changeSetId,
        string code)
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.QueuedRequest)!;
        payload["layoutCheckpointId"] = checkpointId;
        payload["layoutChangeSetId"] = changeSetId;

        var issues = ValidatePresentationRecord("presentation-requested", payload);

        Assert.Contains(issues, issue => issue.Code == code);
    }

    [Fact]
    public void RejectsARequestWithoutAWidgetThatNamesAFrameNumber()
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.RequestWithoutWidget)!;
        payload["sourceFrameNumber"] = 4;

        var issues = ValidatePresentationRecord(
            "presentation-requested", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-presentation-request-inconsistent");
    }

    [Theory]
    [InlineData("frameSinkId", "3-2")]
    [InlineData("frameSinkId", "3:")]
    [InlineData("frameSinkId", "4294967296:2")]
    public void RejectsAMalformedFrameSinkIdentity(string property, string value)
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.Swapped)!;
        payload[property] = value;

        var issues = ValidatePresentationRecord(
            "presentation-swapped", payload);

        Assert.Contains(issues, issue => issue.Code == "payload-property-invalid");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4294967296")]
    [InlineData("017")]
    [InlineData("-3")]
    public void RejectsAFrameTokenOutsideTheUnsignedRange(string token)
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.Swapped)!;
        payload["frameToken"] = token;

        var issues = ValidatePresentationRecord(
            "presentation-swapped", payload);

        Assert.Contains(issues, issue => issue.Code == "payload-property-invalid");
    }

    [Theory]
    [InlineData("commit-fails", "broken")]
    [InlineData("activation-fails", "broken")]
    [InlineData("swap-fails", "kept-active")]
    [InlineData("commit-no-update", "kept-active")]
    public void RejectsANotSwappedActionChromiumWouldNotTake(
        string reason,
        string action)
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.KeptActive)!;
        payload["reason"] = reason;
        payload["action"] = action;

        var issues = ValidatePresentationRecord(
            "presentation-not-swapped", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-presentation-not-swapped-action-inconsistent");
    }

    [Fact]
    public void RejectsANotSwappedCountThatDoesNotFollowTheIndex()
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.KeptActive)!;
        payload["notSwappedCount"] = 3;

        var issues = ValidatePresentationRecord(
            "presentation-not-swapped", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-presentation-not-swapped-count-inconsistent");
    }

    [Theory]
    [InlineData("vsync", "vsync")]
    [InlineData("vsync", "tearing")]
    public void RejectsFeedbackFlagsChromiumDoesNotDefine(string first, string second)
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.Feedback)!;
        payload["flags"] = new JsonArray(first, second);

        var issues = ValidatePresentationRecord(
            "presentation-feedback", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-presentation-feedback-flags-invalid");
    }

    [Fact]
    public void RejectsCounterTicksFromALowResolutionClock()
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.Feedback)!;
        payload["highResolutionTicks"] = false;

        var issues = ValidatePresentationRecord(
            "presentation-feedback", payload);

        Assert.Contains(
            issues,
            issue => issue.Code == "browser-presentation-ticks-without-high-resolution");
    }

    [Fact]
    public void RejectsCounterTicksWithoutChromiumTime()
    {
        var payload = JsonNode.Parse(BrowserPresentationPayloads.Feedback)!;
        payload["presentedTimeTicksMicroseconds"] = null;

        var issues = ValidatePresentationRecord(
            "presentation-feedback", payload);

        Assert.Contains(
            issues, issue => issue.Code == "browser-presentation-ticks-without-time");
    }

    [Fact]
    public void AcceptsAPresentationOmission()
    {
        var payload = JsonNode.Parse(
            """{"reason":"browser-evidence-write-failed","count":2}""")!;

        var issues = ValidatePresentationRecord("collector-omission", payload);

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
    public void AcceptsADesktopFrameWithMonitorCompositionTimes()
    {
        var issues = ValidateDesktopFrame(
            JsonNode.Parse(WindowsGraphicsCaptureFrame)!);

        Assert.Empty(issues);
    }

    [Fact]
    public void AcceptsADesktopFrameWrittenBeforeMonitorCompositionTimes()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!.AsObject();
        payload.Remove("frameSelection");
        payload.Remove("monitorFrames");

        var issues = ValidateDesktopFrame(payload);

        Assert.Empty(issues);
    }

    [Fact]
    public void AcceptsADesktopFrameWrittenBeforeNewestArrivedSelection()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!.AsObject();
        payload.Remove("frameSelection");
        var monitor = payload["monitorFrames"]![0]!.AsObject();
        monitor.Remove("supersededFrameCount");
        monitor.Remove("reusedPreviousImage");

        var issues = ValidateDesktopFrame(payload);

        Assert.Empty(issues);
    }

    [Fact]
    public void AcceptsAReusedPreviousImage()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        var monitor = payload["monitorFrames"]![0]!;
        monitor["supersededFrameCount"] = 0;
        monitor["reusedPreviousImage"] = true;

        var issues = ValidateDesktopFrame(payload);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("supersededFrameCount")]
    [InlineData("reusedPreviousImage")]
    public void RejectsANewestArrivedFrameWithoutMonitorSelectionFields(
        string nulledProperty)
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["monitorFrames"]![0]![nulledProperty] = null;

        var issues = ValidateDesktopFrame(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-monitor-frame-selection-inconsistent");
    }

    [Fact]
    public void RejectsMonitorSelectionFieldsWithoutAFrameSelection()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!.AsObject();
        payload.Remove("frameSelection");

        var issues = ValidateDesktopFrame(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-monitor-frame-selection-inconsistent");
    }

    [Fact]
    public void RejectsAReusedImageThatReleasedArrivedFrames()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["monitorFrames"]![0]!["reusedPreviousImage"] = true;

        var issues = ValidateDesktopFrame(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-monitor-frame-selection-inconsistent");
    }

    [Fact]
    public void RejectsAFrameSelectionOnAGdiFallbackFrame()
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

        var issues = ValidateDesktopFrame(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-frame-selection-inconsistent");
    }

    [Fact]
    public void RejectsAnUndeclaredFrameSelection()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["frameSelection"] = "oldest-queued";

        var issues = ValidateDesktopFrame(payload);

        Assert.Contains(
            issues,
            issue =>
                issue.Code == "payload-property-invalid" &&
                issue.Path.EndsWith("/frameSelection", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsAGdiFallbackFrameWithoutCompositionTimes()
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

        var issues = ValidateDesktopFrame(payload);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("windows-graphics-capture", "compositedAtNanoseconds")]
    [InlineData("windows-graphics-capture", "dequeuedAtNanoseconds")]
    [InlineData("windows-graphics-capture", "tryGetNextFrameAttempts")]
    [InlineData("gdi-bitblt", null)]
    public void RejectsMonitorTimingThatDoesNotMatchTheBackend(
        string backend,
        string? nulledProperty)
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["backend"] = backend;
        if (nulledProperty is not null)
        {
            payload["monitorFrames"]![0]![nulledProperty] = null;
        }

        var issues = ValidateDesktopFrame(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-monitor-frame-timing-inconsistent");
    }

    [Fact]
    public void AcceptsAMonitorImageWhoseCompositionTimeFollowsItsDequeue()
    {
        // Windows reported SystemRelativeTime up to one display refresh after
        // the pool delivered the frame.
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        var composedAt = payload["monitorFrames"]![0]!["compositedAtNanoseconds"]!.GetValue<long>();
        payload["monitorFrames"]![0]!["dequeuedAtNanoseconds"] = composedAt - 15_000_000;

        var issues = ValidateDesktopFrame(payload);

        Assert.Empty(issues);
    }

    [Fact]
    public void RejectsMonitorFramesThatDoNotMatchTheMonitorCount()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["monitorCount"] = 2;

        var issues = ValidateDesktopFrame(payload);

        Assert.Contains(
            issues, issue => issue.Code == "desktop-monitor-frame-count-inconsistent");
    }

    [Fact]
    public void RejectsAnUndeclaredMonitorFrameProperty()
    {
        var payload = JsonNode.Parse(WindowsGraphicsCaptureFrame)!;
        payload["monitorFrames"]![0]!["undeclared"] = 1;

        var issues = ValidateDesktopFrame(payload);

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
    public void AcceptsAUiaDropEpisode()
    {
        var issues = ValidateUiaRecord(
            "collector-omission",
            JsonNode.Parse(UiaDropEpisode)!);

        Assert.Empty(issues);
    }

    [Fact]
    public void AcceptsAUiaQueueFullOmissionWrittenBeforeDropEpisodes()
    {
        var payload = JsonNode.Parse("""
            { "reason": "uia-observation-queue-full", "count": 3226 }
            """)!;

        var issues = ValidateUiaRecord("collector-omission", payload, 13641458100);

        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("firstDroppedAtNanoseconds")]
    [InlineData("lastDroppedAtNanoseconds")]
    [InlineData("droppedByObservationType")]
    [InlineData("count")]
    public void RejectsAUiaDropEpisodeMissingAField(string removed)
    {
        var payload = JsonNode.Parse(UiaDropEpisode)!.AsObject();
        payload.Remove(removed);

        var issues = ValidateUiaRecord("collector-omission", payload);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Fact]
    public void RejectsAUiaDropEpisodeWhoseCountsDoNotSumToItsCount()
    {
        var payload = JsonNode.Parse(UiaDropEpisode)!;
        payload["count"] = 8;

        var issues = ValidateUiaRecord("collector-omission", payload);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Fact]
    public void RejectsAUiaDropEpisodeThatEndsBeforeItBegins()
    {
        var payload = JsonNode.Parse(UiaDropEpisode)!;
        payload["firstDroppedAtNanoseconds"] = 101;

        var issues = ValidateUiaRecord("collector-omission", payload);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Fact]
    public void RejectsAUiaDropEpisodeNotTimedAtItsLastRefusal()
    {
        var issues = ValidateUiaRecord(
            "collector-omission",
            JsonNode.Parse(UiaDropEpisode)!,
            101);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Theory]
    [InlineData("selection-changed", 7)]
    [InlineData("property-changed", 0)]
    public void RejectsAUiaDropEpisodeWithAnInvalidTypeCount(string type, int value)
    {
        var payload = JsonNode.Parse(UiaDropEpisode)!;
        payload["droppedByObservationType"] = new JsonObject { [type] = value };
        payload["count"] = value;

        var issues = ValidateUiaRecord("collector-omission", payload);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Fact]
    public void RejectsADropEpisodeOnAnotherUiaOmissionReason()
    {
        var payload = JsonNode.Parse(UiaDropEpisode)!;
        payload["reason"] = "uia-provider-read-timeout";

        var issues = ValidateUiaRecord("collector-omission", payload);

        Assert.Contains(issues, issue => issue.Code == "uia-omission-episode-inconsistent");
    }

    [Theory]
    [InlineData("event-cache")]
    [InlineData("current-read")]
    [InlineData(null)]
    public void AcceptsEachUiaPropertySource(string? source)
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

        var issues = ValidateUiaRecord("property-changed", payload);

        Assert.Empty(issues);
    }

    [Fact]
    public void RejectsAnUndeclaredUiaPropertySource()
    {
        var payload = JsonNode.Parse(UiaPropertyChange)!;
        payload["element"]!["propertySource"] = "guessed";

        var issues = ValidateUiaRecord("property-changed", payload);

        Assert.NotEmpty(issues);
    }

    private static IReadOnlyList<EventValidationIssue> ValidateUiaRecord(string eventType, JsonNode payload, long timestamp = 100)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            timestamp,
            "accessibility.uia.events",
            eventType,
            document.RootElement.Clone());
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);
        return result.Issues.ToList();
    }

    private static IReadOnlyList<EventValidationIssue> ValidateDesktopFrame(JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            "graphics.desktop.frames",
            "desktop-frame",
            document.RootElement.Clone());
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);
        return result.Issues.ToList();
    }

    private static IReadOnlyList<EventValidationIssue> ValidatePresentationRecord(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Presentation,
            eventType,
            document.RootElement.Clone());
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);
        return result.Issues.ToList();
    }

    private static IReadOnlyList<EventValidationIssue> ValidateInteractionRecord(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var record = CreateEvent(
            0,
            100,
            BrowserEvidenceChannels.Interaction,
            eventType,
            document.RootElement.Clone());
        IReadOnlyList<RecorderEvent> events = ([record]);

        var result = Validate(events);
        return result.Issues.ToList();
    }

    [Fact]
    public void AllowsExtensionChannelWithCustomPayload()
    {
        var record = CreateEvent(
            0,
            100,
            "extension.vendor.telemetry",
            "vendor-sample",
            42);
        IReadOnlyList<RecorderEvent> events = ([record]);
        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AcceptsDerivedEvidenceWithResolvableProvenance()
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
        IReadOnlyList<RecorderEvent> events = ([observed, derived]);
        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void EventIdsAreUniqueByConstructionAndCitedEvidenceIsNotResolved()
    {
        // An event ID is formed from its stream and sequence, so a reused ID
        // is either malformed or repeats a sequence. Whether cited evidence
        // exists is not checked: it may not have arrived yet.
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
        IReadOnlyList<RecorderEvent> events = ([observed, inferred, duplicate]);
        var result = Validate(events);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("event-id-invalid", issue.Code);
    }

    [Fact]
    public void DetectsInvalidInferenceConfidence()
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
        IReadOnlyList<RecorderEvent> events = ([observed, inferred]);
        var result = Validate(events);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "inference-confidence-invalid");
    }

    [Fact]
    public void AcceptsUnknownEvidenceWithReason()
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
        IReadOnlyList<RecorderEvent> events = ([observed, unknown]);
        var result = Validate(events);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
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

    // Runs one validator over the events in order, as the database writer
    // does for a recording.
    private static ValidationResult Validate(IReadOnlyList<RecorderEvent> events)
    {
        var validator = new EventRecordValidator("test-session");
        var issues = events.SelectMany(validator.Validate).ToList();
        return new ValidationResult(issues.Count == 0, issues);
    }

    private sealed record ValidationResult(
        bool IsValid,
        IReadOnlyList<EventValidationIssue> Issues);
}

using System.Text.Json;
using Recorder.Collectors.Browser;
using Recorder.Contracts;

namespace Recorder.Tests;

// Evidence ingest rejects unmapped members, so a listener or dispatch field the
// bridge emits without a matching contract property closes the pipe and costs
// the rest of that renderer's evidence for the session. Protocol 0.18 replaced
// the node reference in listener and dispatch records with an event-target
// reference that also describes Window and other non-Node targets, so these
// fixtures mirror the JSON the bridge now writes for each kind. Protocol 0.19
// adds the registration form a listener entered Blink with and a record for a
// callback Blink replaced in place, so those shapes are covered here too.
// Protocol 0.21 adds the JavaScript world a listener callback belongs to, which
// a listener record carries as a world object and repeats in its context.
public sealed class BrowserEventTargetPayloadIngestTests
{
    private const string ContextJson = """
        {
          "browserInstanceId": "browser-instance-1",
          "processId": 3440,
          "frameId": "frame-4",
          "documentId": "dom-document-19",
          "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C"
        }
        """;

    private const string NodeTargetJson = """
        {
          "kind": "node",
          "interfaceName": "HTMLButtonElement",
          "targetId": null,
          "documentId": "dom-document-19",
          "nodeId": 91,
          "backendNodeId": null,
          "tagName": "BUTTON",
          "elementId": "pointer-only",
          "classes": []
        }
        """;

    private const string WindowTargetJson = """
        {
          "kind": "window",
          "interfaceName": "DOMWindow",
          "targetId": "event-target-2",
          "documentId": "dom-document-19",
          "nodeId": null,
          "backendNodeId": null,
          "tagName": null,
          "elementId": null,
          "classes": []
        }
        """;

    [Fact]
    public void AcceptsANodeListenerRegistrationAsWritten()
    {
        var payload = Accept<BrowserListenerPayload>(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            $$"""
            {
              "context": {{ContextJson}},
              "listenerId": "listener-7",
              "eventName": "click",
              "registrationKind": "add-event-listener",
              "target": {{NodeTargetJson}},
              "capture": false,
              "passive": false,
              "once": false,
              "location": null
            }
            """);

        Assert.Equal(BrowserEventTargetKinds.Node, payload.Target.Kind);
        Assert.Equal("HTMLButtonElement", payload.Target.InterfaceName);
        Assert.Null(payload.Target.TargetId);
        Assert.Equal(91L, payload.Target.NodeId);
        Assert.Equal("BUTTON", payload.Target.TagName);
    }

    [Fact]
    public void AcceptsAWindowListenerRegistrationAsWritten()
    {
        var payload = Accept<BrowserListenerPayload>(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            $$"""
            {
              "context": {{ContextJson}},
              "listenerId": "listener-8",
              "eventName": "resize",
              "registrationKind": "add-event-listener",
              "target": {{WindowTargetJson}},
              "capture": false,
              "passive": true,
              "once": false,
              "location": null
            }
            """);

        Assert.Equal(BrowserEventTargetKinds.Window, payload.Target.Kind);
        // "DOMWindow" is the token the reference Chromium checkout reports for a
        // window. The value is Blink's own and has changed between revisions, so
        // it is carried through ingest unaltered rather than validated against a
        // fixed set.
        Assert.Equal("DOMWindow", payload.Target.InterfaceName);
        Assert.Equal("event-target-2", payload.Target.TargetId);
        Assert.Null(payload.Target.NodeId);
        Assert.Null(payload.Target.TagName);
    }

    [Fact]
    public void AcceptsAWindowListenerRemovalAsWritten()
    {
        var payload = Accept<BrowserListenerPayload>(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRemoved,
            $$"""
            {
              "context": {{ContextJson}},
              "listenerId": "listener-8",
              "eventName": "resize",
              "registrationKind": "add-event-listener",
              "target": {{WindowTargetJson}},
              "capture": false,
              "passive": true,
              "once": false,
              "location": null
            }
            """);

        Assert.Equal(BrowserEventTargetKinds.Window, payload.Target.Kind);
        Assert.Null(payload.Target.NodeId);
    }

    [Fact]
    public void AcceptsADispatchPathThatEndsAtTheWindowAsWritten()
    {
        var payload = Accept<BrowserDispatchPayload>(
            BrowserEvidenceChannels.Dispatch,
            BrowserEvidenceEventTypes.DispatchStarted,
            $$"""
            {
              "context": {{ContextJson}},
              "dispatchId": "dispatch-3",
              "eventName": "click",
              "trusted": true,
              "originalTarget": {{NodeTargetJson}},
              "composedPath": [{{NodeTargetJson}}, {{WindowTargetJson}}],
              "pathScopes": [
                {
                  "treeScopeRootNodeId": 8,
                  "shadowRootMode": null,
                  "targetNodeId": 91,
                  "relatedTargetNodeId": null,
                  "visiblePathIndexes": [0, 1],
                  "unmatchedVisibleTargetCount": 0
                },
                {
                  "treeScopeRootNodeId": null,
                  "shadowRootMode": null,
                  "targetNodeId": 91,
                  "relatedTargetNodeId": null,
                  "visiblePathIndexes": [0, 1],
                  "unmatchedVisibleTargetCount": 0
                }
              ],
              "currentTarget": null,
              "phase": "none",
              "listenerId": null,
              "defaultPrevented": false,
              "propagationStopped": false,
              "immediatePropagationStopped": false,
              "defaultAction": null,
              "outcome": null
            }
            """);

        Assert.Equal(2, payload.ComposedPath.Count);
        Assert.Equal(
            BrowserEventTargetKinds.Node,
            payload.ComposedPath[0].Kind);
        var window = payload.ComposedPath[^1];
        Assert.Equal(BrowserEventTargetKinds.Window, window.Kind);
        Assert.Equal("event-target-2", window.TargetId);
        Assert.Null(window.NodeId);
    }

    [Fact]
    public void AcceptsAWindowListenerInvocationAsWritten()
    {
        var payload = Accept<BrowserDispatchPayload>(
            BrowserEvidenceChannels.Dispatch,
            BrowserEvidenceEventTypes.ListenerInvoked,
            $$"""
            {
              "context": {{ContextJson}},
              "dispatchId": "dispatch-3",
              "eventName": "click",
              "trusted": true,
              "originalTarget": {{NodeTargetJson}},
              "composedPath": [{{NodeTargetJson}}, {{WindowTargetJson}}],
              "pathScopes": [
                {
                  "treeScopeRootNodeId": 8,
                  "shadowRootMode": null,
                  "targetNodeId": 91,
                  "relatedTargetNodeId": null,
                  "visiblePathIndexes": [0, 1],
                  "unmatchedVisibleTargetCount": 0
                },
                {
                  "treeScopeRootNodeId": null,
                  "shadowRootMode": null,
                  "targetNodeId": 91,
                  "relatedTargetNodeId": null,
                  "visiblePathIndexes": [0, 1],
                  "unmatchedVisibleTargetCount": 0
                }
              ],
              "currentTarget": {{WindowTargetJson}},
              "phase": "bubbling",
              "listenerId": "listener-8",
              "defaultPrevented": false,
              "propagationStopped": false,
              "immediatePropagationStopped": false,
              "defaultAction": null,
              "outcome": "invoked"
            }
            """);

        var currentTarget = Assert.IsType<BrowserEventTargetReference>(
            payload.CurrentTarget);
        Assert.Equal(BrowserEventTargetKinds.Window, currentTarget.Kind);
        Assert.Equal("event-target-2", currentTarget.TargetId);
    }

    [Fact]
    public void AcceptsAnInlineAttributeRegistrationAsWritten()
    {
        var payload = Accept<BrowserListenerPayload>(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            $$"""
            {
              "context": {{ContextJson}},
              "listenerId": "listener-9",
              "eventName": "click",
              "registrationKind": "inline-attribute",
              "target": {{NodeTargetJson}},
              "capture": false,
              "passive": false,
              "once": false,
              "location": null
            }
            """);

        Assert.Equal("inline-attribute", payload.RegistrationKind);
    }

    [Fact]
    public void AcceptsAReplacedAttributeListenerCallbackAsWritten()
    {
        // Assigning an on-event attribute over a registration an inline
        // attribute or an earlier assignment established swaps the callback in
        // place, so Blink reports neither an addition nor a removal. The record
        // keeps the listener identity and carries the form of the callback
        // Blink now holds.
        var payload = Accept<BrowserListenerPayload>(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerCallbackReplaced,
            $$"""
            {
              "context": {{ContextJson}},
              "listenerId": "listener-9",
              "eventName": "click",
              "registrationKind": "event-handler-property",
              "target": {{NodeTargetJson}},
              "capture": false,
              "passive": false,
              "once": false,
              "location": null
            }
            """);

        Assert.Equal("listener-9", payload.ListenerId);
        Assert.Equal("event-handler-property", payload.RegistrationKind);
        Assert.Equal(BrowserEventTargetKinds.Node, payload.Target.Kind);
    }

    [Fact]
    public void AcceptsARegistrationLocationAsWritten()
    {
        // The location describes the call that registered the listener, so the
        // script it names is the script that made the call and not the
        // document that loaded it.
        var payload = Accept<BrowserListenerPayload>(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            $$"""
            {
              "context": {{ContextJson}},
              "listenerId": "listener-10",
              "eventName": "click",
              "registrationKind": "add-event-listener",
              "target": {{NodeTargetJson}},
              "capture": false,
              "passive": false,
              "once": false,
              "location": {
                "scriptId": "7",
                "url": "file:///fixtures/blink-listener-registration.js",
                "line": 9,
                "column": 18,
                "functionName": "registerExternalScriptListener",
                "sourceHash": null
              }
            }
            """);

        var location = Assert.IsType<BrowserScriptLocation>(payload.Location);
        Assert.Equal("7", location.ScriptId);
        Assert.Equal(
            "file:///fixtures/blink-listener-registration.js",
            location.Url);
        Assert.Equal(9, location.Line);
        Assert.Equal(18, location.Column);
        Assert.Equal("registerExternalScriptListener", location.FunctionName);
        // The recorder does not read script text, so it reports no hash.
        Assert.Null(location.SourceHash);
    }

    [Fact]
    public void AcceptsALocationWhoseFactsWereNotAllObserved()
    {
        // Blink reports an unobserved script identifier or function name as
        // absent, and a partly observed location is evidence of where the call
        // came from, so it is carried rather than discarded.
        var payload = Accept<BrowserListenerPayload>(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRemoved,
            $$"""
            {
              "context": {{ContextJson}},
              "listenerId": "listener-10",
              "eventName": "click",
              "registrationKind": "add-event-listener",
              "target": {{NodeTargetJson}},
              "capture": false,
              "passive": false,
              "once": false,
              "location": {
                "scriptId": null,
                "url": "file:///fixtures/blink-listener-dispatch.html",
                "line": 81,
                "column": null,
                "functionName": null,
                "sourceHash": null
              }
            }
            """);

        var location = Assert.IsType<BrowserScriptLocation>(payload.Location);
        Assert.Null(location.ScriptId);
        Assert.Null(location.Column);
        Assert.Null(location.FunctionName);
        Assert.Equal(81, location.Line);
    }

    [Fact]
    public void AcceptsTheIsolatedWorldAListenerWasRegisteredFrom()
    {
        var payload = Accept<BrowserListenerPayload>(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            $$"""
            {
              "context": {
                "browserInstanceId": "browser-instance-1",
                "processId": 3440,
                "frameId": "frame-4",
                "documentId": "dom-document-19",
                "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C",
                "executionWorldId": "world-13"
              },
              "listenerId": "listener-11",
              "eventName": "click",
              "registrationKind": "add-event-listener",
              "target": {{NodeTargetJson}},
              "capture": false,
              "passive": false,
              "once": false,
              "location": null,
              "world": {
                "kind": "isolated",
                "blinkWorldId": 13,
                "name": "recorder probe",
                "stableId": "probe-world"
              }
            }
            """);

        Assert.Equal("world-13", payload.Context.ExecutionWorldId);
        var world = Assert.IsType<BrowserExecutionWorld>(payload.World);
        Assert.Equal(BrowserExecutionWorldKinds.Isolated, world.Kind);
        Assert.Equal(13, world.BlinkWorldId);
        Assert.Equal("recorder probe", world.Name);
        Assert.Equal("probe-world", world.StableId);
    }

    // A main-world registration has neither a human readable name nor a stable
    // identifier, because Blink only holds those for worlds other than the main
    // world. A listener Blink installed itself belongs to no world at all, which
    // the bridge reports as a null world rather than as the main world.
    [Fact]
    public void AcceptsAMainWorldAndAnUnobservedWorldAsWritten()
    {
        var main = Accept<BrowserListenerPayload>(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            $$"""
            {
              "context": {
                "browserInstanceId": "browser-instance-1",
                "processId": 3440,
                "frameId": "frame-4",
                "documentId": "dom-document-19",
                "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C",
                "executionWorldId": "world-0"
              },
              "listenerId": "listener-12",
              "eventName": "click",
              "registrationKind": "event-handler-property",
              "target": {{NodeTargetJson}},
              "capture": false,
              "passive": false,
              "once": false,
              "location": null,
              "world": {
                "kind": "main",
                "blinkWorldId": 0,
                "name": null,
                "stableId": null
              }
            }
            """);

        var mainWorld = Assert.IsType<BrowserExecutionWorld>(main.World);
        Assert.Equal(BrowserExecutionWorldKinds.Main, mainWorld.Kind);
        Assert.Equal(0, mainWorld.BlinkWorldId);
        Assert.Null(mainWorld.Name);
        Assert.Null(mainWorld.StableId);

        var unobserved = Accept<BrowserListenerPayload>(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRemoved,
            $$"""
            {
              "context": {{ContextJson}},
              "listenerId": "listener-12",
              "eventName": "click",
              "registrationKind": "add-event-listener",
              "target": {{NodeTargetJson}},
              "capture": false,
              "passive": false,
              "once": false,
              "location": null,
              "world": null
            }
            """);

        Assert.Null(unobserved.World);
        Assert.Null(unobserved.Context.ExecutionWorldId);
    }

    [Fact]
    public void RejectsAWorldFieldNoContractMaps()
    {
        var payload = $$"""
            {
              "context": {{ContextJson}},
              "listenerId": "listener-13",
              "eventName": "click",
              "registrationKind": "add-event-listener",
              "target": {{NodeTargetJson}},
              "capture": false,
              "passive": false,
              "once": false,
              "location": null,
              "world": {
                "kind": "isolated",
                "blinkWorldId": 13,
                "worldOrigin": "https://example.test",
                "name": null,
                "stableId": null
              }
            }
            """;

        using var document = JsonDocument.Parse(payload);
        Assert.ThrowsAny<JsonException>(() => BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            document.RootElement));
    }

    [Fact]
    public void RejectsAnEventTargetFieldNoContractMaps()
    {
        var payload = $$"""
            {
              "context": {{ContextJson}},
              "listenerId": "listener-8",
              "eventName": "resize",
              "registrationKind": "add-event-listener",
              "target": {
                "kind": "window",
                "interfaceName": "DOMWindow",
                "targetId": "event-target-2",
                "targetScope": "renderer",
                "documentId": "dom-document-19",
                "nodeId": null,
                "backendNodeId": null,
                "tagName": null,
                "elementId": null,
                "classes": []
              },
              "capture": false,
              "passive": true,
              "once": false,
              "location": null
            }
            """;

        using var document = JsonDocument.Parse(payload);
        Assert.ThrowsAny<JsonException>(() => BrowserProtocol.ValidateEvidencePayload(
            BrowserEvidenceChannels.Listener,
            BrowserEvidenceEventTypes.ListenerRegistered,
            document.RootElement));
    }

    // Routes the payload the way the receive loop does, which rejects a field
    // no contract maps, then reads it as the contract type the assertions need.
    // Protocol 0.22 lets a reporter state evidence that was lost instead of
    // leaving a gap. The omission record travels on the channel that lost the
    // records, so every browser channel must ingest it, and the bridge's own
    // omission carries the process context that lost them.
    [Fact]
    public void AcceptsAnOmissionThatNamesTheProcessThatLostRecords()
    {
        var payload = Accept<BrowserOmissionPayload>(
            BrowserEvidenceChannels.Dispatch,
            BrowserEvidenceEventTypes.Omission,
            $$"""
            {
              "context": {{ContextJson}},
              "reason": "browser-evidence-write-failed",
              "count": 7
            }
            """);

        Assert.Equal(
            BrowserEvidenceOmissionReasons.EvidenceWriteFailed,
            payload.Reason);
        Assert.Equal(7, payload.Count);
        var context = Assert.IsType<BrowserContext>(payload.Context);
        Assert.Equal(3440, context.ProcessId);
    }

    // A loss the reporter cannot attribute to one browser process carries no
    // context rather than naming a process it did not observe.
    [Fact]
    public void AcceptsAnOmissionWithNoProcessContext()
    {
        var payload = Accept<BrowserOmissionPayload>(
            BrowserEvidenceChannels.Timer,
            BrowserEvidenceEventTypes.Omission,
            """
            {
              "reason": "browser-evidence-sink-refused",
              "count": 2
            }
            """);

        Assert.Null(payload.Context);
        Assert.Equal(
            BrowserEvidenceOmissionReasons.SinkRefusedRecord,
            payload.Reason);
        Assert.Equal(2, payload.Count);
    }

    private static T Accept<T>(
    string channel,
    string eventType,
    string payload)
{
    using var document = JsonDocument.Parse(payload);
    BrowserProtocol.ValidateEvidencePayload(
        channel,
        eventType,
        document.RootElement);
    return BrowserProtocol.Deserialize<T>(document.RootElement);
}
}

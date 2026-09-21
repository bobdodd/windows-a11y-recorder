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

namespace Recorder.Tests;

// The JSON the bridge writes for the shadow-tree records of protocol 0.28,
// shared by the ingest and archive tests so both check the same shapes. The
// dispatch starts inside a closed shadow root: the path runs from the button in
// the shadow tree, through the shadow root and its host, to the document and
// the window, and each tree scope sees the path composedPath() gives it.
internal static class BrowserShadowDomPayloads
{
    private const string ContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 3440,
          "processType": "renderer",
          "profileId": null,
          "browserContextId": null,
          "pageId": null,
          "frameId": null,
          "documentId": "dom-document-19",
          "executionWorldId": null,
          "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C"
        }
        """;

    public static readonly string ShadowRootNode = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "dom-checkpoint-3",
          "nodeIndex": 5,
          "nodeId": 61,
          "parentNodeId": 60,
          "nodeType": "shadow-root",
          "nodeName": "#document-fragment"
        }
        """;

    public static readonly string ShadowRoot = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "dom-checkpoint-3",
          "nodeId": 61,
          "hostNodeId": 60,
          "mode": "closed",
          "delegatesFocus": true,
          "slotAssignment": "named",
          "clonable": false,
          "serializable": false,
          "declarative": false,
          "availableToElementInternals": false,
          "referenceTarget": null
        }
        """;

    public static readonly string SlotAssignment = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "dom-checkpoint-3",
          "nodeId": 63,
          "assignedNodeIds": [58, 59],
          "assignedNodeCount": 2,
          "assignedNodesTruncated": false,
          "maximumAssignedNodes": 512,
          "assignmentCurrent": true
        }
        """;

    private static string NodeTarget(int nodeId, string interfaceName, string? tagName) => $$"""
        {
          "kind": "node",
          "interfaceName": "{{interfaceName}}",
          "targetId": null,
          "documentId": "dom-document-19",
          "nodeId": {{nodeId}},
          "backendNodeId": null,
          "tagName": {{(tagName is null ? "null" : $"\"{tagName}\"")}},
          "elementId": null,
          "classes": []
        }
        """;

    private const string WindowTarget = """
        {
          "kind": "window",
          "interfaceName": "Window",
          "targetId": "event-target-1",
          "documentId": "dom-document-19",
          "nodeId": null,
          "backendNodeId": null,
          "tagName": null,
          "elementId": null,
          "classes": []
        }
        """;

    public static readonly string DispatchStarted = $$"""
        {
          "context": {{ContextJson}},
          "dispatchId": "dispatch-4",
          "eventName": "click",
          "trusted": true,
          "originalTarget": {{NodeTarget(62, "HTMLButtonElement", "BUTTON")}},
          "composedPath": [
            {{NodeTarget(62, "HTMLButtonElement", "BUTTON")}},
            {{NodeTarget(61, "ShadowRoot", null)}},
            {{NodeTarget(60, "HTMLElement", "X-PANEL")}},
            {{NodeTarget(19, "HTMLDocument", null)}},
            {{WindowTarget}}
          ],
          "pathScopes": [
            {
              "treeScopeRootNodeId": 61,
              "shadowRootMode": "closed",
              "targetNodeId": 62,
              "relatedTargetNodeId": null,
              "visiblePathIndexes": [0, 1, 2, 3, 4],
              "unmatchedVisibleTargetCount": 0
            },
            {
              "treeScopeRootNodeId": 61,
              "shadowRootMode": "closed",
              "targetNodeId": 62,
              "relatedTargetNodeId": null,
              "visiblePathIndexes": [0, 1, 2, 3, 4],
              "unmatchedVisibleTargetCount": 0
            },
            {
              "treeScopeRootNodeId": 19,
              "shadowRootMode": null,
              "targetNodeId": 60,
              "relatedTargetNodeId": null,
              "visiblePathIndexes": [2, 3, 4],
              "unmatchedVisibleTargetCount": 0
            },
            {
              "treeScopeRootNodeId": 19,
              "shadowRootMode": null,
              "targetNodeId": 60,
              "relatedTargetNodeId": null,
              "visiblePathIndexes": [2, 3, 4],
              "unmatchedVisibleTargetCount": 0
            },
            {
              "treeScopeRootNodeId": null,
              "shadowRootMode": null,
              "targetNodeId": 60,
              "relatedTargetNodeId": null,
              "visiblePathIndexes": [2, 3, 4],
              "unmatchedVisibleTargetCount": 0
            }
          ],
          "phase": "none",
          "listenerId": null,
          "defaultPrevented": false,
          "propagationStopped": false,
          "immediatePropagationStopped": false,
          "defaultAction": null,
          "outcome": null
        }
        """;
}

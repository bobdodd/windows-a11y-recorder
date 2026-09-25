namespace Recorder.Tests;

// The JSON the bridge writes for each browser.layout record in protocol 0.28,
// shared by the ingest and archive tests so both check the same shapes.
internal static class BrowserLayoutPayloads
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

    public static readonly string FirstCheckpointStarted = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "layout-checkpoint-1",
          "reason": "rendering-update",
          "previousCheckpointId": null,
          "styleResolutionCount": 14,
          "layoutCount": 1,
          "viewport": { "width": 1280, "height": 720 },
          "scrollOffset": { "x": 0, "y": 0 },
          "devicePixelRatio": 1.25,
          "layoutZoomFactor": 1.25,
          "maximumNodes": 100000,
          "styleProperties": ["display", "width", "color"]
        }
        """;

    public static readonly string LaterCheckpointStarted = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "layout-checkpoint-2",
          "reason": "rendering-update",
          "previousCheckpointId": "layout-checkpoint-1",
          "styleResolutionCount": 15,
          "layoutCount": 2,
          "viewport": { "width": 1280, "height": 720 },
          "scrollOffset": { "x": 0, "y": 240.5 },
          "devicePixelRatio": 1.25,
          "layoutZoomFactor": 1.25,
          "maximumNodes": 100000,
          "styleProperties": ["display", "width", "color"]
        }
        """;

    public static readonly string ElementNode = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "layout-checkpoint-1",
          "nodeIndex": 3,
          "nodeId": 44,
          "nodeType": "element",
          "nodeName": "BUTTON",
          "layoutObjectPresent": true,
          "displayLocked": false,
          "boundingClientRect": { "x": 8, "y": 30.5, "width": 120, "height": 24 },
          "computedStyle": {
            "display": "inline-block",
            "width": "120px",
            "color": "rgb(0, 0, 0)"
          },
          "pseudoElement": null,
          "shadowHostNodeId": null,
          "shadowRootMode": null
        }
        """;

    // An element whose style Blink did not compute, such as one inside a
    // display: none subtree, reports neither a rectangle nor a style.
    public static readonly string UnrenderedElementNode = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "layout-checkpoint-1",
          "nodeIndex": 4,
          "nodeId": 45,
          "nodeType": "element",
          "nodeName": "SPAN",
          "layoutObjectPresent": false,
          "displayLocked": false,
          "boundingClientRect": null,
          "computedStyle": null,
          "pseudoElement": null,
          "shadowHostNodeId": null,
          "shadowRootMode": null
        }
        """;

    public static readonly string StyleValueMissingNode = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "layout-checkpoint-1",
          "nodeIndex": 5,
          "nodeId": 46,
          "nodeType": "element",
          "nodeName": "DIV",
          "layoutObjectPresent": false,
          "displayLocked": true,
          "boundingClientRect": null,
          "computedStyle": { "display": "block", "width": null, "color": "rgb(0, 0, 0)" },
          "pseudoElement": null,
          "shadowHostNodeId": null,
          "shadowRootMode": null
        }
        """;

    public static readonly string TextNode = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "layout-checkpoint-1",
          "nodeIndex": 6,
          "nodeId": 47,
          "nodeType": "text",
          "nodeName": "#text",
          "layoutObjectPresent": true,
          "displayLocked": false,
          "boundingClientRect": { "x": 14, "y": 34, "width": 42.25, "height": 16 },
          "computedStyle": null,
          "pseudoElement": null,
          "shadowHostNodeId": null,
          "shadowRootMode": null
        }
        """;

    // An element inside a closed shadow root records its host and the mode.
    public static readonly string ShadowTreeElementNode = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "layout-checkpoint-1",
          "nodeIndex": 7,
          "nodeId": 49,
          "nodeType": "element",
          "nodeName": "SPAN",
          "layoutObjectPresent": true,
          "displayLocked": false,
          "boundingClientRect": { "x": 8, "y": 60, "width": 30, "height": 16 },
          "computedStyle": { "display": "inline", "width": "auto", "color": "rgb(0, 0, 0)" },
          "pseudoElement": null,
          "shadowHostNodeId": 48,
          "shadowRootMode": "closed"
        }
        """;

    // A ::before pseudo-element carries its originating element, its type,
    // and the text it generated.
    public static readonly string PseudoElementNode = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "layout-checkpoint-1",
          "nodeIndex": 8,
          "nodeId": 50,
          "nodeType": "pseudo-element",
          "nodeName": "::before",
          "layoutObjectPresent": true,
          "displayLocked": false,
          "boundingClientRect": { "x": 8, "y": 30.5, "width": 12, "height": 24 },
          "computedStyle": { "display": "inline", "width": "auto", "color": "rgb(0, 0, 0)" },
          "pseudoElement": {
            "originatingNodeId": 44,
            "pseudoType": "::before",
            "generatedText": "Note: ",
            "generatedTextLength": 6,
            "generatedTextTruncated": false
          },
          "shadowHostNodeId": null,
          "shadowRootMode": null
        }
        """;

    public static readonly string CheckpointCompleted = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "layout-checkpoint-1",
          "reason": "rendering-update",
          "nodeCount": 9,
          "truncated": false,
          "maximumNodes": 100000,
          "pseudoElementCount": 1,
          "shadowRootCount": 1
        }
        """;

    public static IEnumerable<(string EventType, string Json)> All()
    {
        yield return ("layout-checkpoint-started", FirstCheckpointStarted);
        yield return ("layout-checkpoint-started", LaterCheckpointStarted);
        yield return ("layout-checkpoint-node", ElementNode);
        yield return ("layout-checkpoint-node", UnrenderedElementNode);
        yield return ("layout-checkpoint-node", StyleValueMissingNode);
        yield return ("layout-checkpoint-node", TextNode);
        yield return ("layout-checkpoint-node", ShadowTreeElementNode);
        yield return ("layout-checkpoint-node", PseudoElementNode);
        yield return ("layout-checkpoint-completed", CheckpointCompleted);
    }
}

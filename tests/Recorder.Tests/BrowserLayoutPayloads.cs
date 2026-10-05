namespace Recorder.Tests;

// The JSON the bridge writes for each browser.layout record in protocols 0.32
// to 0.34, shared by the ingest and archive tests so both check the same
// shapes.
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
          "walkReason": "first",
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
          "walkReason": "first",
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

    public static readonly string ChangesStarted = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-1",
          "layoutCheckpointId": "layout-checkpoint-1",
          "checkpointUpdate": true,
          "viewTransformNodeId": "layout-transform-1",
          "viewPaintOffset": { "x": 0, "y": 0 },
          "layoutZoomFactor": 1.25
        }
        """;

    public static readonly string ChangesStartedWithoutCheckpoint = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-2",
          "layoutCheckpointId": null,
          "checkpointUpdate": false,
          "viewTransformNodeId": "layout-transform-1",
          "viewPaintOffset": { "x": 2, "y": 2 },
          "layoutZoomFactor": 1.25
        }
        """;

    public static readonly string ViewTransformNode = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-1",
          "transformNodeId": "layout-transform-1",
          "parentTransformNodeId": null,
          "matrix": [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
          "flattensInheritedTransform": true,
          "scrollTranslation": false,
          "sticky": false
        }
        """;

    public static readonly string ScrollTransformNode = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-1",
          "transformNodeId": "layout-transform-2",
          "parentTransformNodeId": "layout-transform-1",
          "matrix": [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -300.625, 0, 1],
          "flattensInheritedTransform": false,
          "scrollTranslation": true,
          "sticky": false
        }
        """;

    public static readonly string ChangedElementNode = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-1",
          "reasons": ["style", "layout"],
          "nodeId": 42,
          "nodeType": "element",
          "nodeName": "DIV",
          "layoutObjectPresent": true,
          "displayLocked": false,
          "geometry": {
            "transformNodeId": "layout-transform-2",
            "localRect": { "x": 10, "y": 338.75, "width": 150, "height": 25 },
            "localQuadRects": null,
            "clientRectEmpty": false,
            "localRectMapped": true,
            "clientRectScale": 0.8
          },
          "computedStyle": { "display": "block", "width": "120px", "color": null },
          "pseudoElement": null,
          "shadowHostNodeId": null,
          "shadowRootMode": null
        }
        """;

    // A later record of the node (protocol 0.37): only the style values that
    // changed since its last record, with a custom property removed.
    public static readonly string ChangedElementStyleChanges = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-2",
          "reasons": ["style"],
          "nodeId": 42,
          "nodeType": "element",
          "nodeName": "DIV",
          "layoutObjectPresent": true,
          "displayLocked": false,
          "geometry": {
            "transformNodeId": "layout-transform-2",
            "localRect": { "x": 10, "y": 338.75, "width": 150, "height": 25 },
            "localQuadRects": null,
            "clientRectEmpty": false,
            "localRectMapped": true,
            "clientRectScale": 0.8
          },
          "computedStyle": { "color": "rgb(0, 0, 255)" },
          "computedStyleComplete": false,
          "customProperties": { "--accent": "green" },
          "removedCustomProperties": ["--gap"],
          "pseudoElement": null,
          "shadowHostNodeId": null,
          "shadowRootMode": null
        }
        """;

    public static readonly string ChangedEmptyTextNode = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-1",
          "reasons": ["layout"],
          "nodeId": 43,
          "nodeType": "text",
          "nodeName": "#text",
          "layoutObjectPresent": true,
          "displayLocked": false,
          "geometry": {
            "transformNodeId": "layout-transform-2",
            "localRect": null,
            "localQuadRects": null,
            "clientRectEmpty": true,
            "localRectMapped": false,
            "clientRectScale": 0.8
          },
          "computedStyle": null,
          "pseudoElement": null,
          "shadowHostNodeId": null,
          "shadowRootMode": null
        }
        """;

    public static readonly string ChangedUnrenderedPseudoElement = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-1",
          "reasons": ["style"],
          "nodeId": 44,
          "nodeType": "pseudo-element",
          "nodeName": "::before",
          "layoutObjectPresent": false,
          "displayLocked": false,
          "geometry": null,
          "computedStyle": { "display": "none", "width": "auto", "color": "rgb(0, 0, 0)" },
          "pseudoElement": {
            "originatingNodeId": 42,
            "pseudoType": "::before",
            "generatedText": "",
            "generatedTextLength": 0,
            "generatedTextTruncated": false
          },
          "shadowHostNodeId": 40,
          "shadowRootMode": "open"
        }
        """;

    public static readonly string ChangesCompleted = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-1",
          "notedNodeCount": 5,
          "recordedNodeCount": 3,
          "unchangedNodeCount": 1,
          "transformNodeCount": 2,
          "scrollOffsetCount": 0
        }
        """;

    // Protocol 0.34.
    public static readonly string ScrollOffsetChanged = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-1",
          "nodeId": 44,
          "scrollOffset": { "x": 0, "y": 300.625 },
          "webExposedScrollOffset": { "x": 0, "y": 300.625 },
          "scrollOrigin": { "x": 0, "y": 0 },
          "effectiveZoom": 1,
          "scrollTranslationNodeId": "layout-transform-2",
          "scrollElementId": "68"
        }
        """;

    public static readonly string ScrollOffsetChangedWithoutTranslation = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-1",
          "nodeId": 45,
          "scrollOffset": { "x": 3, "y": 0 },
          "webExposedScrollOffset": { "x": 3, "y": 0 },
          "scrollOrigin": { "x": -40, "y": 0 },
          "effectiveZoom": 1.25,
          "scrollTranslationNodeId": null
        }
        """;

    // The box fragments of a block (protocol 0.38), in layout units at an
    // effective zoom of 1.25: one fragment that breaks before its next one,
    // with scrollable overflow, a child box with a node, an anonymous block
    // holding its own fragment, and a column.
    public const string BoxFragmentsJson = """
        {
          "effectiveZoom": 1.25,
          "fragments": [
            {
              "width": 187.5,
              "height": 31.25,
              "breakToken": {
                "consumedBlockSize": 31.25,
                "breakBefore": false,
                "sequenceNumber": 0,
                "atBlockEnd": false
              },
              "scrollableOverflow": { "x": 0, "y": 0, "width": 187.5, "height": 40.015625 },
              "children": [
                { "kind": "box", "x": 0, "y": 0, "nodeId": 44, "fragmentIndex": 0, "fragment": null },
                {
                  "kind": "anonymous", "x": 0, "y": 30, "nodeId": null, "fragmentIndex": null,
                  "fragment": {
                    "width": 187.5, "height": 1.25, "breakToken": null,
                    "scrollableOverflow": null, "children": []
                  }
                },
                {
                  "kind": "column", "x": 0, "y": 31.25, "nodeId": null, "fragmentIndex": null,
                  "fragment": {
                    "width": 90, "height": 0, "breakToken": null,
                    "scrollableOverflow": null, "children": []
                  }
                },
                { "kind": "line", "x": 0, "y": 31.25, "nodeId": null, "fragmentIndex": null, "fragment": null }
              ]
            }
          ],
          "naturalSize": null
        }
        """;

    // A checkpoint record of an image with its box fragment and natural size
    // (protocol 0.38).
    public static readonly string BoxedElementNode = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "layout-checkpoint-1",
          "nodeIndex": 5,
          "nodeId": 46,
          "nodeType": "element",
          "nodeName": "IMG",
          "layoutObjectPresent": true,
          "displayLocked": false,
          "boundingClientRect": { "x": 8, "y": 60, "width": 64, "height": 32 },
          "computedStyle": { "display": "inline", "width": "64px", "color": "rgb(0, 0, 0)" },
          "pseudoElement": null,
          "shadowHostNodeId": null,
          "shadowRootMode": null,
          "customProperties": {},
          "boxFragments": {
            "effectiveZoom": 1.25,
            "fragments": [
              { "width": 80, "height": 40, "breakToken": null, "scrollableOverflow": null, "children": [] }
            ],
            "naturalSize": {
              "width": 400, "height": 200, "hasWidth": true, "hasHeight": true,
              "aspectRatioWidth": 400, "aspectRatioHeight": 200
            }
          }
        }
        """;

    // A change record of a block with its box fragments (protocol 0.38).
    public static readonly string ChangedBoxedElementNode = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-1",
          "reasons": ["layout"],
          "nodeId": 42,
          "nodeType": "element",
          "nodeName": "DIV",
          "layoutObjectPresent": true,
          "displayLocked": false,
          "geometry": {
            "transformNodeId": "layout-transform-2",
            "localRect": { "x": 10, "y": 338.75, "width": 150, "height": 25 },
            "localQuadRects": null,
            "clientRectEmpty": false,
            "localRectMapped": true,
            "clientRectScale": 0.8
          },
          "computedStyle": { "display": "block", "width": "150px", "color": null },
          "pseudoElement": null,
          "shadowHostNodeId": null,
          "shadowRootMode": null,
          "computedStyleComplete": true,
          "customProperties": {},
          "removedCustomProperties": null,
          "boxFragments": {{BoxFragmentsJson}}
        }
        """;

    // A paragraph's box fragments with their items, text content, and glyph
    // runs (protocol 0.39): one line holding a text item, an inline box
    // holding a second text item, and a hyphen; and an anonymous block, held
    // by a child link, with its own text and items.
    public const string TextBlockFragmentsJson = """
        {
          "effectiveZoom": 1,
          "fragments": [
            {
              "width": 300, "height": 40, "breakToken": null, "scrollableOverflow": null,
              "children": [
                {
                  "kind": "anonymous", "x": 0, "y": 20, "nodeId": null, "fragmentIndex": null,
                  "fragment": {
                    "width": 300, "height": 20, "breakToken": null, "scrollableOverflow": null,
                    "children": [],
                    "items": [
                      {
                        "type": "line", "x": 0, "y": 0, "width": 300, "height": 20,
                        "descendantsCount": 2, "nodeId": null, "start": null, "end": null,
                        "firstLineStyle": null, "direction": null, "hiddenForPaint": null,
                        "glyphRuns": null, "generatedText": null
                      },
                      {
                        "type": "text", "x": 0, "y": 2, "width": 9, "height": 16,
                        "descendantsCount": null, "nodeId": 52, "start": 0, "end": 1,
                        "firstLineStyle": false, "direction": "ltr", "hiddenForPaint": false,
                        "glyphRuns": [
                          {
                            "font": {
                              "family": "Arial", "postScriptName": "ArialMT", "size": 16,
                              "syntheticBold": false, "syntheticItalic": false
                            },
                            "horizontal": true, "rotation": 0,
                            "glyphs": "UgAAAAAAAAAAAAAAAAAAAAAA"
                          }
                        ],
                        "generatedText": null
                      }
                    ],
                    "textContent": "R",
                    "firstLineText": null
                  }
                }
              ],
              "items": [
                {
                  "type": "line", "x": 0, "y": 0, "width": 300, "height": 20,
                  "descendantsCount": 5, "nodeId": null, "start": null, "end": null,
                  "firstLineStyle": null, "direction": null, "hiddenForPaint": null,
                  "glyphRuns": null, "generatedText": null
                },
                {
                  "type": "text", "x": 0, "y": 2, "width": 17, "height": 16,
                  "descendantsCount": null, "nodeId": 50, "start": 0, "end": 2,
                  "firstLineStyle": true, "direction": "ltr", "hiddenForPaint": false,
                  "glyphRuns": [
                    {
                      "font": {
                        "family": "Arial", "postScriptName": "ArialMT", "size": 16,
                        "syntheticBold": false, "syntheticItalic": false
                      },
                      "horizontal": true, "rotation": 0,
                      "glyphs": "KwAAAAAAAAAAAAAAAAAAAAAATAABAAAAAAAIQQAAAAAAAIC+"
                    }
                  ],
                  "generatedText": null
                },
                {
                  "type": "box", "x": 17, "y": 2, "width": 30, "height": 16,
                  "descendantsCount": 2, "nodeId": 51, "start": null, "end": null,
                  "firstLineStyle": null, "direction": null, "hiddenForPaint": null,
                  "glyphRuns": null, "generatedText": null
                },
                {
                  "type": "text", "x": 17, "y": 2, "width": 30, "height": 16,
                  "descendantsCount": null, "nodeId": 53, "start": 2, "end": 5,
                  "firstLineStyle": true, "direction": "rtl", "hiddenForPaint": false,
                  "glyphRuns": [],
                  "generatedText": null
                },
                {
                  "type": "generated-text", "x": 47, "y": 2, "width": 5, "height": 16,
                  "descendantsCount": null, "nodeId": 53, "start": null, "end": null,
                  "firstLineStyle": true, "direction": "ltr", "hiddenForPaint": false,
                  "glyphRuns": [],
                  "generatedText": "-"
                }
              ],
              "textContent": null,
              "firstLineText": null
            }
          ],
          "naturalSize": null,
          "textContent": "Hiabc",
          "firstLineText": "HIABC",
          "textContentUnchanged": false
        }
        """;

    // A checkpoint record of the paragraph (protocol 0.39).
    public static readonly string TextBlockNode = $$"""
        {
          "context": {{ContextJson}},
          "checkpointId": "layout-checkpoint-1",
          "nodeIndex": 6,
          "nodeId": 49,
          "nodeType": "element",
          "nodeName": "P",
          "layoutObjectPresent": true,
          "displayLocked": false,
          "boundingClientRect": { "x": 8, "y": 100, "width": 300, "height": 40 },
          "computedStyle": { "display": "block", "width": "300px", "color": "rgb(0, 0, 0)" },
          "pseudoElement": null,
          "shadowHostNodeId": null,
          "shadowRootMode": null,
          "customProperties": {},
          "boxFragments": {{TextBlockFragmentsJson}}
        }
        """;

    // A change record of the paragraph (protocol 0.39).
    public static readonly string ChangedTextBlockNode = $$"""
        {
          "context": {{ContextJson}},
          "changeSetId": "layout-changes-1",
          "reasons": ["layout"],
          "nodeId": 49,
          "nodeType": "element",
          "nodeName": "P",
          "layoutObjectPresent": true,
          "displayLocked": false,
          "geometry": {
            "transformNodeId": "layout-transform-2",
            "localRect": { "x": 8, "y": 100, "width": 300, "height": 40 },
            "localQuadRects": null,
            "clientRectEmpty": false,
            "localRectMapped": true,
            "clientRectScale": 1
          },
          "computedStyle": { "display": "block", "width": "300px", "color": "rgb(0, 0, 0)" },
          "pseudoElement": null,
          "shadowHostNodeId": null,
          "shadowRootMode": null,
          "computedStyleComplete": true,
          "customProperties": {},
          "removedCustomProperties": null,
          "boxFragments": {{TextBlockFragmentsJson}}
        }
        """;

    // The paragraph's change record with its text left out as unchanged.
    public static string ChangedTextBlockNodeTextUnchanged()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(ChangedTextBlockNode)!;
        var fragments = node["boxFragments"]!;
        fragments["textContent"] = null;
        fragments["firstLineText"] = null;
        fragments["textContentUnchanged"] = true;
        return node.ToJsonString();
    }

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
        yield return ("layout-checkpoint-node", BoxedElementNode);
        yield return ("layout-checkpoint-node", TextBlockNode);
        yield return ("layout-checkpoint-completed", CheckpointCompleted);
        yield return ("layout-changes-started", ChangesStarted);
        yield return ("layout-changes-started", ChangesStartedWithoutCheckpoint);
        yield return ("layout-transform-node", ViewTransformNode);
        yield return ("layout-transform-node", ScrollTransformNode);
        yield return ("layout-node-changed", ChangedElementNode);
        yield return ("layout-node-changed", ChangedEmptyTextNode);
        yield return ("layout-node-changed", ChangedUnrenderedPseudoElement);
        yield return ("layout-node-changed", ChangedBoxedElementNode);
        yield return ("layout-node-changed", ChangedTextBlockNode);
        yield return ("layout-node-changed", ChangedTextBlockNodeTextUnchanged());
        yield return ("layout-scroll-offset-changed", ScrollOffsetChanged);
        yield return ("layout-scroll-offset-changed", ScrollOffsetChangedWithoutTranslation);
        yield return ("layout-changes-completed", ChangesCompleted);
    }
}

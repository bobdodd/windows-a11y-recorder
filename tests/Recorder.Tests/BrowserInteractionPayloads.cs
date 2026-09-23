namespace Recorder.Tests;

// The JSON the bridge writes for each browser.interaction record in protocol
// 0.24, shared by the ingest and archive tests so both check the same shapes.
internal static class BrowserInteractionPayloads
{
    private const string ScriptContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 3440,
          "processType": "renderer",
          "profileId": null,
          "browserContextId": null,
          "pageId": null,
          "frameId": null,
          "documentId": "dom-document-19",
          "executionWorldId": "world-0",
          "documentToken": "F8543F87A3AF6713E6DEADA760E49A6C"
        }
        """;

    // A change no script made, such as a key press, reports no world.
    private const string UserContextJson = """
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

    private const string LocationJson = """
        {
          "scriptId": "12",
          "url": "https://example.test/app.js",
          "line": 4,
          "column": 17,
          "functionName": "moveFocus",
          "sourceHash": null
        }
        """;

    private const string WorldJson = """
        {
          "kind": "main",
          "blinkWorldId": 0,
          "name": null,
          "stableId": null
        }
        """;

    public static readonly string ScriptFocusChanged = $$"""
        {
          "context": {{ScriptContextJson}},
          "previousNodeId": 31,
          "requestedNodeId": 44,
          "focusedNodeId": 44,
          "outcome": "focused",
          "activeDescendantNodeId": 52,
          "focusType": "script",
          "focusTrigger": "script",
          "preventScroll": true,
          "focusVisible": null,
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string KeyboardFocusChanged = $$"""
        {
          "context": {{UserContextJson}},
          "previousNodeId": 44,
          "requestedNodeId": 47,
          "focusedNodeId": 47,
          "outcome": "focused",
          "activeDescendantNodeId": null,
          "focusType": "forward",
          "focusTrigger": "user-gesture",
          "preventScroll": false,
          "focusVisible": null,
          "location": null,
          "world": null
        }
        """;

    public static readonly string FocusCleared = $$"""
        {
          "context": {{ScriptContextJson}},
          "previousNodeId": 47,
          "requestedNodeId": null,
          "focusedNodeId": null,
          "outcome": "cleared",
          "activeDescendantNodeId": null,
          "focusType": "none",
          "focusTrigger": "script",
          "preventScroll": false,
          "focusVisible": null,
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string TextControlSelection = $$"""
        {
          "context": {{ScriptContextJson}},
          "setBy": "system",
          "selectionType": "range",
          "anchorNodeId": 61,
          "anchorOffset": 1,
          "focusNodeId": 61,
          "focusOffset": 4,
          "directional": false,
          "textControlNodeId": 47,
          "textControlSelectionStart": 1,
          "textControlSelectionEnd": 4,
          "textControlSelectionDirection": "forward",
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string SelectionCleared = $$"""
        {
          "context": {{UserContextJson}},
          "setBy": "user",
          "selectionType": "none",
          "anchorNodeId": null,
          "anchorOffset": null,
          "focusNodeId": null,
          "focusOffset": null,
          "directional": false,
          "textControlNodeId": null,
          "textControlSelectionStart": null,
          "textControlSelectionEnd": null,
          "textControlSelectionDirection": null,
          "location": null,
          "world": null
        }
        """;

    public static readonly string UserEditedValue = $$"""
        {
          "context": {{UserContextJson}},
          "nodeId": 47,
          "controlType": "text",
          "source": "user-edit",
          "value": "abc",
          "valueLength": 3,
          "valueTruncated": false,
          "maximumValueLength": 4096,
          "selectionStart": 3,
          "selectionEnd": 3,
          "selectionDirection": "none",
          "location": null,
          "world": null
        }
        """;

    public static readonly string ScriptSetValue = $$"""
        {
          "context": {{ScriptContextJson}},
          "nodeId": 49,
          "controlType": "textarea",
          "source": "value-set",
          "value": "line one\nline two",
          "valueLength": 17,
          "valueTruncated": false,
          "maximumValueLength": 4096,
          "selectionStart": 17,
          "selectionEnd": 17,
          "selectionDirection": "none",
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static readonly string ActiveDescendantReferenceSet = $$"""
        {
          "context": {{ScriptContextJson}},
          "nodeId": 44,
          "referencedNodeId": 52,
          "location": {{LocationJson}},
          "world": {{WorldJson}}
        }
        """;

    public static IEnumerable<(string EventType, string Json)> All()
    {
        yield return ("focus-changed", ScriptFocusChanged);
        yield return ("focus-changed", KeyboardFocusChanged);
        yield return ("focus-changed", FocusCleared);
        yield return ("selection-changed", TextControlSelection);
        yield return ("selection-changed", SelectionCleared);
        yield return ("text-control-value-changed", UserEditedValue);
        yield return ("text-control-value-changed", ScriptSetValue);
        yield return ("active-descendant-reference-set", ActiveDescendantReferenceSet);
    }
}

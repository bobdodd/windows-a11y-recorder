namespace Recorder.Tests;

// The JSON the bridge writes for each browser.interaction record in protocols
// 0.24 and 0.29, shared by the ingest and archive tests so both check the same shapes.
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

    // Protocol 0.43: a select's list opened in a page popup. The context
    // names the popup's own document.
    private const string PopupContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 3440,
          "processType": "renderer",
          "profileId": null,
          "browserContextId": null,
          "pageId": null,
          "frameId": null,
          "documentId": "dom-document-10394",
          "executionWorldId": null,
          "documentToken": "0B1C2D3E4F5A46B7C8D9E0F1A2B3C4D5"
        }
        """;

    public static readonly string PagePopupOpened = $$"""
        {
          "context": {{PopupContextJson}},
          "kind": "select-list",
          "ownerDocumentId": "dom-document-2486",
          "ownerDocumentToken": "970C31312F7139126377C8D77F4167A7",
          "ownerFrameToken": "2C6A0F9B4E1D47A8B3C5D7E9F1A3B5C7",
          "ownerNodeId": 418,
          "ownerVisibleBoundsInLocalRoot": { "x": 508, "y": 360, "width": 262, "height": 48 },
          "ownerLocalRootRectInScreen": { "x": 8, "y": 111, "width": 1904, "height": 921 },
          "anchorRectInScreen": { "x": 516, "y": 471, "width": 262, "height": 48 },
          "initialWindowRect": { "x": 516, "y": 519, "width": 262, "height": 340 },
          "zoomFactor": 1
        }
        """;

    public static readonly string PagePopupRequestedRect = $$"""
        {
          "context": {{PopupContextJson}},
          "deferred": true,
          "windowRect": { "x": 516, "y": 519, "width": 262, "height": 340 }
        }
        """;

    public static readonly string PagePopupClosed = $$"""
        {
          "context": {{PopupContextJson}},
          "closedBy": "renderer"
        }
        """;

    // Protocol 0.44: the browser's popup widget records. The created record
    // names the opener frame's document; the others carry the browser
    // process's context and join to it by the frame sink.
    private const string OpenerContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 1200,
          "processType": "browser",
          "profileId": null,
          "browserContextId": null,
          "pageId": "frame-1",
          "frameId": "frame-1",
          "documentId": "document-navigation-3",
          "executionWorldId": null,
          "documentToken": "970C31312F7139126377C8D77F4167A7"
        }
        """;

    private const string BrowserProcessContextJson = """
        {
          "browserInstanceId": "browser-1",
          "processId": 1200,
          "processType": "browser",
          "profileId": null,
          "browserContextId": null,
          "pageId": null,
          "frameId": null,
          "documentId": null,
          "executionWorldId": null,
          "documentToken": null
        }
        """;

    public static readonly string PopupWidgetCreated = $$"""
        {
          "context": {{OpenerContextJson}},
          "rendererProcessId": 3440,
          "openerFrameToken": "2C6A0F9B4E1D47A8B3C5D7E9F1A3B5C7",
          "frameSinkId": "4:12"
        }
        """;

    public static readonly string PopupWidgetShown = $$"""
        {
          "context": {{BrowserProcessContextJson}},
          "frameSinkId": "4:12",
          "outcome": "shown",
          "receivedRect": { "x": 516, "y": 519, "width": 262, "height": 340 },
          "receivedAnchorRect": { "x": 516, "y": 471, "width": 262, "height": 48 },
          "transformedRect": { "x": 516, "y": 519, "width": 262, "height": 340 },
          "transformedAnchorRect": { "x": 516, "y": 471, "width": 262, "height": 48 },
          "constrainedRect": { "x": 516, "y": 519, "width": 262, "height": 340 },
          "viewBounds": { "x": 516, "y": 519, "width": 262, "height": 340 }
        }
        """;

    public static readonly string PopupWidgetRefused = $$"""
        {
          "context": {{BrowserProcessContextJson}},
          "frameSinkId": "4:13",
          "outcome": "window-not-active",
          "receivedRect": { "x": 516, "y": 519, "width": 262, "height": 340 },
          "receivedAnchorRect": { "x": 516, "y": 471, "width": 262, "height": 48 },
          "transformedRect": null,
          "transformedAnchorRect": null,
          "constrainedRect": null,
          "viewBounds": null
        }
        """;

    public static readonly string PopupWidgetBoundsRequested = $$"""
        {
          "context": {{BrowserProcessContextJson}},
          "frameSinkId": "4:12",
          "requestedRect": { "x": 516, "y": 519, "width": 262, "height": 300 },
          "setRect": { "x": 516, "y": 519, "width": 262, "height": 300 }
        }
        """;

    public static readonly string PopupWidgetBoundsIgnored = $$"""
        {
          "context": {{BrowserProcessContextJson}},
          "frameSinkId": "4:12",
          "requestedRect": { "x": 516, "y": 519, "width": 262, "height": 300 },
          "setRect": null
        }
        """;

    public static readonly string PopupWidgetScreenRects = $$"""
        {
          "context": {{BrowserProcessContextJson}},
          "frameSinkId": "4:12",
          "viewRect": { "x": 516, "y": 519, "width": 262, "height": 340 },
          "windowRect": { "x": 516, "y": 519, "width": 262, "height": 340 },
          "nativeWindowRect": { "x": 774, "y": 778, "width": 393, "height": 510 },
          "nativeClientRect": { "x": 774, "y": 778, "width": 393, "height": 510 },
          "deviceScaleFactor": 1.5
        }
        """;

    // The highlight moved by an arrow key: no script made the change.
    public static readonly string OptionSelected = $$"""
        {
          "context": {{UserContextJson}},
          "nodeId": 61,
          "selectNodeId": 58,
          "selected": true,
          "location": null,
          "world": null
        }
        """;

    // A snapshot taken after a layout checkpoint while a listbox holds focus
    // and names an active descendant.
    public static readonly string LayoutCheckpointStarted = $$"""
        {
          "context": {{UserContextJson}},
          "checkpointId": "interaction-checkpoint-7",
          "sourceCheckpointId": "layout-checkpoint-12",
          "sourceChangeSetId": null,
          "sourceChannel": "browser.layout",
          "reason": "rendering-update",
          "documentHasFocus": true,
          "focusedNodeId": 44,
          "focusVisible": false,
          "activeDescendantNodeId": 52,
          "lastFocusType": "script",
          "selectionType": "none",
          "anchorNodeId": null,
          "anchorOffset": null,
          "focusNodeId": null,
          "focusOffset": null,
          "directional": false,
          "maximumTextControls": 512,
          "maximumValueLength": 4096
        }
        """;

    // A snapshot taken when parsing finished, before anything was focused.
    public static readonly string DomCheckpointStarted = $$"""
        {
          "context": {{UserContextJson}},
          "checkpointId": "interaction-checkpoint-1",
          "sourceCheckpointId": "dom-checkpoint-3",
          "sourceChangeSetId": null,
          "sourceChannel": "browser.dom",
          "reason": "finished-parsing",
          "documentHasFocus": false,
          "focusedNodeId": null,
          "focusVisible": false,
          "activeDescendantNodeId": null,
          "lastFocusType": "none",
          "selectionType": "caret",
          "anchorNodeId": 21,
          "anchorOffset": 0,
          "focusNodeId": 21,
          "focusOffset": 0,
          "directional": false,
          "maximumTextControls": 512,
          "maximumValueLength": 4096
        }
        """;

    public static readonly string CheckpointTextControl = $$"""
        {
          "context": {{UserContextJson}},
          "checkpointId": "interaction-checkpoint-7",
          "textControlIndex": 0,
          "nodeId": 47,
          "controlType": "text",
          "value": "set by script",
          "valueLength": 13,
          "valueTruncated": false,
          "selectionStart": 13,
          "selectionEnd": 13,
          "selectionDirection": "none"
        }
        """;

    public static readonly string CheckpointCompleted = $$"""
        {
          "context": {{UserContextJson}},
          "checkpointId": "interaction-checkpoint-7",
          "textControlCount": 1,
          "truncated": false,
          "maximumTextControls": 512
        }
        """;

    public static IEnumerable<(string EventType, string Json)> All()
    {
        yield return ("interaction-checkpoint-started", LayoutCheckpointStarted);
        yield return ("interaction-checkpoint-started", DomCheckpointStarted);
        yield return ("interaction-checkpoint-text-control", CheckpointTextControl);
        yield return ("interaction-checkpoint-completed", CheckpointCompleted);
        yield return ("focus-changed", ScriptFocusChanged);
        yield return ("focus-changed", KeyboardFocusChanged);
        yield return ("focus-changed", FocusCleared);
        yield return ("selection-changed", TextControlSelection);
        yield return ("selection-changed", SelectionCleared);
        yield return ("text-control-value-changed", UserEditedValue);
        yield return ("text-control-value-changed", ScriptSetValue);
        yield return ("active-descendant-reference-set", ActiveDescendantReferenceSet);
        yield return ("page-popup-opened", PagePopupOpened);
        yield return ("page-popup-window-rect", PagePopupRequestedRect);
        yield return ("page-popup-closed", PagePopupClosed);
        yield return ("popup-widget-created", PopupWidgetCreated);
        yield return ("popup-widget-shown", PopupWidgetShown);
        yield return ("popup-widget-shown", PopupWidgetRefused);
        yield return ("popup-widget-bounds-requested", PopupWidgetBoundsRequested);
        yield return ("popup-widget-bounds-requested", PopupWidgetBoundsIgnored);
        yield return ("popup-widget-screen-rects", PopupWidgetScreenRects);
        yield return ("option-selectedness-changed", OptionSelected);
    }
}

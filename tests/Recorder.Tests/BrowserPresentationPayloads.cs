namespace Recorder.Tests;

// The JSON the bridge writes for each browser.presentation record in protocol
// 0.30, shared by the ingest and archive tests so both check the same shapes.
internal static class BrowserPresentationPayloads
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

    public static readonly string QueuedRequest = $$"""
        {
          "context": {{ContextJson}},
          "requestId": "presentation-request-7",
          "widgetKind": "frame",
          "frameSinkId": "3:2",
          "localRootFrameToken": "5C1D2A0E8F6B4E3A9D7C1B2A3F4E5D6C",
          "layoutCheckpointId": "layout-checkpoint-12",
          "layoutChangeSetId": null,
          "queued": true,
          "notQueuedReason": null,
          "sourceFrameNumber": 41,
          "isMainFrameWidget": true,
          "highResolutionTicks": true,
          "maximumNotSwappedRecords": 16
        }
        """;

    public static readonly string RequestWithoutWidget = $$"""
        {
          "context": {{ContextJson}},
          "requestId": "presentation-request-8",
          "widgetKind": null,
          "frameSinkId": null,
          "localRootFrameToken": null,
          "layoutCheckpointId": "layout-checkpoint-13",
          "layoutChangeSetId": null,
          "queued": false,
          "notQueuedReason": "no-widget",
          "sourceFrameNumber": null,
          "isMainFrameWidget": null,
          "highResolutionTicks": true,
          "maximumNotSwappedRecords": 16
        }
        """;

    public static readonly string RequestNotCompositing = $$"""
        {
          "context": {{ContextJson}},
          "requestId": "presentation-request-9",
          "widgetKind": "frame",
          "frameSinkId": "3:2",
          "localRootFrameToken": "5C1D2A0E8F6B4E3A9D7C1B2A3F4E5D6C",
          "layoutCheckpointId": "layout-checkpoint-14",
          "layoutChangeSetId": null,
          "queued": false,
          "notQueuedReason": "not-compositing",
          "sourceFrameNumber": null,
          "isMainFrameWidget": false,
          "highResolutionTicks": true,
          "maximumNotSwappedRecords": 16
        }
        """;

    public static readonly string KeptActive = $$"""
        {
          "context": {{ContextJson}},
          "requestId": "presentation-request-7",
          "widgetKind": "frame",
          "frameSinkId": "3:2",
          "localRootFrameToken": "5C1D2A0E8F6B4E3A9D7C1B2A3F4E5D6C",
          "reason": "commit-fails",
          "action": "kept-active",
          "notSwappedIndex": 0,
          "notSwappedCount": 1,
          "timestampTicks": "123456789012",
          "timestampTimeTicksMicroseconds": "12345678901"
        }
        """;

    public static readonly string Broken = $$"""
        {
          "context": {{ContextJson}},
          "requestId": "presentation-request-7",
          "widgetKind": "frame",
          "frameSinkId": "3:2",
          "localRootFrameToken": "5C1D2A0E8F6B4E3A9D7C1B2A3F4E5D6C",
          "reason": "commit-no-update",
          "action": "broken",
          "notSwappedIndex": 1,
          "notSwappedCount": 2,
          "timestampTicks": null,
          "timestampTimeTicksMicroseconds": null
        }
        """;

    public static readonly string Swapped = $$"""
        {
          "context": {{ContextJson}},
          "requestId": "presentation-request-7",
          "widgetKind": "frame",
          "frameSinkId": "3:2",
          "localRootFrameToken": "5C1D2A0E8F6B4E3A9D7C1B2A3F4E5D6C",
          "frameToken": "4294967295",
          "notSwappedCount": 0
        }
        """;

    public static readonly string Feedback = $$"""
        {
          "context": {{ContextJson}},
          "requestId": "presentation-request-7",
          "widgetKind": "frame",
          "frameSinkId": "3:2",
          "localRootFrameToken": "5C1D2A0E8F6B4E3A9D7C1B2A3F4E5D6C",
          "frameToken": "4294967295",
          "presentedTicks": "123456989012",
          "presentedTimeTicksMicroseconds": "12345698901",
          "intervalMicroseconds": "16666",
          "flags": ["vsync", "hw-completion"],
          "receivedCompositorFrameTicks": "123456800000",
          "drawStartTicks": "123456810000",
          "swapStartTicks": "123456820000",
          "swapEndTicks": "123456830000",
          "highResolutionTicks": true,
          "notSwappedCount": 0
        }
        """;

    // Protocol 0.43: a page popup's widget names its frame but no frame sink.
    public static readonly string PagePopupRequest = $$"""
        {
          "context": {{ContextJson}},
          "requestId": "presentation-request-21",
          "widgetKind": "page-popup",
          "frameSinkId": null,
          "localRootFrameToken": "7A2B3C4D5E6F40718293A4B5C6D7E8F9",
          "layoutCheckpointId": null,
          "layoutChangeSetId": "layout-changes-30",
          "queued": true,
          "notQueuedReason": null,
          "sourceFrameNumber": 3,
          "isMainFrameWidget": false,
          "highResolutionTicks": true,
          "maximumNotSwappedRecords": 16
        }
        """;

    public static readonly string PagePopupFeedback = $$"""
        {
          "context": {{ContextJson}},
          "requestId": "presentation-request-21",
          "widgetKind": "page-popup",
          "frameSinkId": null,
          "localRootFrameToken": "7A2B3C4D5E6F40718293A4B5C6D7E8F9",
          "frameToken": "4",
          "presentedTicks": "123457989012",
          "presentedTimeTicksMicroseconds": "12345798901",
          "intervalMicroseconds": "16666",
          "flags": ["vsync"],
          "receivedCompositorFrameTicks": null,
          "drawStartTicks": null,
          "swapStartTicks": null,
          "swapEndTicks": null,
          "highResolutionTicks": true,
          "notSwappedCount": 0
        }
        """;

    public static IEnumerable<(string EventType, string Json)> All()
    {
        yield return ("presentation-requested", PagePopupRequest);
        yield return ("presentation-feedback", PagePopupFeedback);
        yield return ("presentation-requested", QueuedRequest);
        yield return ("presentation-requested", RequestWithoutWidget);
        yield return ("presentation-requested", RequestNotCompositing);
        yield return ("presentation-not-swapped", KeptActive);
        yield return ("presentation-not-swapped", Broken);
        yield return ("presentation-swapped", Swapped);
        yield return ("presentation-feedback", Feedback);
    }
}

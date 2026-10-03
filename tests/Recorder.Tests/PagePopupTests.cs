using System.Text.Json;
using Recorder.Contracts;
using Recorder.Database.RecordingFiles;
using Recorder.Recreation;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 4d sub-step 2: which page popups are open at a frame, the join of
// each to the browser's popup widget, its window rectangle and its place in
// the recreation, and option selectedness imposed by the builder.
public sealed class PagePopupTests
{
    private const string Page = "PAGE-TOKEN";
    private const string Frame = "OWNER-FRAME";

    private static JsonElement J(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Rect(int x, int y, int width, int height) =>
        $"{{\"x\":{x},\"y\":{y},\"width\":{width},\"height\":{height}}}";

    private static string Renderer(string token, string id, int process = 10) =>
        $"{{\"browserInstanceId\":\"b\",\"processId\":{process},\"processType\":\"renderer\",\"documentId\":\"{id}\",\"documentToken\":\"{token}\"}}";

    private const string BrowserContext =
        "{\"browserInstanceId\":\"b\",\"processId\":1,\"processType\":\"browser\",\"documentId\":null,\"documentToken\":null}";

    private static PopupRecord Opened(long key, long time, string token, string owner = Page, string frame = Frame, int process = 10) =>
        new(key, time, "page-popup-opened", J($$"""
            {"context":{{Renderer(token, "dom-document-" + key, process)}},"kind":"select-list",
             "ownerDocumentId":"dom-document-1","ownerDocumentToken":"{{owner}}","ownerFrameToken":"{{frame}}","ownerNodeId":3023,
             "ownerVisibleBoundsInLocalRoot":{{Rect(43, 384, 262, 48)}},"ownerLocalRootRectInScreen":{{Rect(449, 87, 1240, 925)}},
             "anchorRectInScreen":{{Rect(492, 471, 262, 48)}},"initialWindowRect":{{Rect(492, 519, 307, 400)}},"zoomFactor":1}
            """));

    private static PopupRecord Closed(long key, long time, string token, long openedKey) =>
        new(key, time, "page-popup-closed", J($$"""{"context":{{Renderer(token, "dom-document-" + openedKey)}},"closedBy":"renderer"}"""));

    private static PopupRecord Created(long key, long time, string sink, string frame = Frame, int process = 10) =>
        new(key, time, "popup-widget-created", J($$"""
            {"context":{"browserInstanceId":"b","processId":1,"processType":"browser","documentId":"document-navigation-8","documentToken":"{{Page}}"},
             "rendererProcessId":{{process}},"openerFrameToken":"{{frame}}","frameSinkId":"{{sink}}"}
            """));

    private static PopupRecord Shown(long key, long time, string sink, string bounds) =>
        new(key, time, "popup-widget-shown", J($$"""
            {"context":{{BrowserContext}},"frameSinkId":"{{sink}}","outcome":"shown","receivedRect":{{bounds}},
             "receivedAnchorRect":{{Rect(492, 471, 262, 48)}},"viewBounds":{{bounds}}}
            """));

    private static PopupRecord Bounds(long key, long time, string sink, string? set) =>
        new(key, time, "popup-widget-bounds-requested", J($$"""
            {"context":{{BrowserContext}},"frameSinkId":"{{sink}}","requestedRect":{{Rect(1, 2, 3, 4)}},"setRect":{{set ?? "null"}}}
            """));

    private static PopupRecord Screen(long key, long time, string sink, string view) =>
        new(key, time, "popup-widget-screen-rects", J($$"""
            {"context":{{BrowserContext}},"frameSinkId":"{{sink}}","viewRect":{{view}},"windowRect":{{view}},"deviceScaleFactor":1}
            """));

    private static PopupRecord Hidden(long key, long time, string sink, string cause = "destroyed") =>
        new(key, time, "popup-widget-hidden", J($$"""
            {"context":{{BrowserContext}},"frameSinkId":"{{sink}}","cause":"{{cause}}","nativeWindowVisible":false}
            """));

    private static PopupRecord Requested(long key, long time, string token, long openedKey, string rect) =>
        new(key, time, "page-popup-window-rect", J($$"""
            {"context":{{Renderer(token, "dom-document-" + openedKey)}},"deferred":false,"windowRect":{{rect}}}
            """));

    [Fact]
    public void APopupIsOpenFromItsOpeningUntilItsClosingAtTheFramesComposition()
    {
        PopupRecord[] records =
        [
            Created(1, 90, "6:18"),
            Opened(2, 100, "POPUP-A"),
            Closed(3, 200, "POPUP-A", 2),
        ];

        Assert.Empty(PagePopups.OpenAt(records, Page, 99));
        var open = Assert.Single(PagePopups.OpenAt(records, Page, 100));
        Assert.Equal("POPUP-A dom-document-2", open.DocumentKey);
        Assert.Equal("select-list", open.Kind);
        Assert.Equal(3023, open.OwnerNodeId);
        Assert.Single(PagePopups.OpenAt(records, Page, 199));
        Assert.Empty(PagePopups.OpenAt(records, Page, 200));
        // Only the popups the page's document owns.
        Assert.Empty(PagePopups.OpenAt(records, "OTHER-PAGE", 150));
    }

    // "Popup on screen" (protocol 0.45): a popup's window leaves the screen
    // when the browser hides it, after the renderer closes it, so a frame
    // composed between the two shows the popup.
    [Fact]
    public void APopupIsOpenUntilItsWidgetsWindowIsHiddenWhenTheRecordingHoldsIt()
    {
        PopupRecord[] records =
        [
            Created(1, 90, "6:18"),
            Opened(2, 100, "POPUP-A"),
            Closed(3, 200, "POPUP-A", 2),
            Hidden(4, 205, "6:99"),
            Hidden(5, 240, "6:18"),
            Hidden(6, 300, "6:18", "hidden"),
        ];

        var between = Assert.Single(PagePopups.OpenAt(records, Page, 220));
        Assert.Equal(240, between.WindowHiddenTime);
        Assert.Equal(200, between.ClosedTime);
        Assert.Equal(220, between.CompositionTime);
        Assert.Single(PagePopups.OpenAt(records, Page, 239));
        // The first hidden record of the joined widget closes it; another
        // widget's record does not.
        Assert.Empty(PagePopups.OpenAt(records, Page, 240));
        Assert.Empty(PagePopups.OpenAt(records, Page, 250));
    }

    [Fact]
    public void WithoutAHiddenRecordAPopupClosesAtItsCloseRecord()
    {
        PopupRecord[] records =
        [
            Created(1, 90, "6:18"),
            Opened(2, 100, "POPUP-A"),
            Closed(3, 200, "POPUP-A", 2),
        ];

        var open = Assert.Single(PagePopups.OpenAt(records, Page, 150));
        Assert.Null(open.WindowHiddenTime);
        Assert.Equal(200, open.ClosedTime);
        Assert.Contains("no record of its window being hidden", open.OnScreenBasis, StringComparison.Ordinal);
        Assert.Empty(PagePopups.OpenAt(records, Page, 200));
    }

    [Fact]
    public void AFrameComposedWithinOneDisplayIntervalOfTheHiddenRecordIsAtTheEdge()
    {
        PopupRecord[] records =
        [
            Created(1, 90, "6:18"),
            Opened(2, 100, "POPUP-A"),
            Closed(3, 38_531_000_000, "POPUP-A", 2),
            Hidden(4, 38_550_000_000, "6:18"),
        ];

        var edge = Assert.Single(PagePopups.OpenAt(records, Page, 38_545_000_000));
        Assert.Equal(
            "its window was hidden at 38.550 s, after the frame's composition at 38.545 s, within one 60 Hz display interval of it, so the frame is at the edge and the captured image may show either state",
            edge.OnScreenBasis);
        var clear = Assert.Single(PagePopups.OpenAt(records, Page, 38_500_000_000));
        Assert.Equal("its window was hidden at 38.550 s, after the frame's composition at 38.500 s", clear.OnScreenBasis);
    }

    [Fact]
    public void APopupIsDrawnOnlyFromAPresentedRenderingUpdate()
    {
        var state = new BrowserDocumentState("k");
        Assert.False(RecordingFileDocuments.IsDrawn(null));
        Assert.False(RecordingFileDocuments.IsDrawn(new BrowserDocumentAt("k", state, new BrowserStateBasis("by-time", 10, null))));
        Assert.False(RecordingFileDocuments.IsDrawn(new BrowserDocumentAt("k", null, new BrowserStateBasis("presented", 10, 12))));
        Assert.True(RecordingFileDocuments.IsDrawn(new BrowserDocumentAt("k", state, new BrowserStateBasis("presented", 10, 12))));
    }

    [Fact]
    public void EachPopupJoinsTheOpenerFramesLastUnjoinedWidgetCreatedAtOrBeforeIt()
    {
        PopupRecord[] records =
        [
            Created(1, 10, "6:17"),
            Created(2, 20, "6:18"),
            Created(3, 25, "6:99", frame: "ANOTHER-FRAME"),
            Created(4, 26, "6:98", process: 11),
            Opened(5, 30, "POPUP-A"),
            Closed(6, 40, "POPUP-A", 5),
            Opened(7, 50, "POPUP-B"),
            Created(8, 60, "6:19"),
        ];

        Assert.Equal("6:18", Assert.Single(PagePopups.OpenAt(records, Page, 35)).FrameSinkId);
        // The second popup takes the frame's last widget not already joined;
        // a widget created after it, or of another frame or process, joins
        // none.
        Assert.Equal("6:17", Assert.Single(PagePopups.OpenAt(records, Page, 55)).FrameSinkId);
    }

    [Fact]
    public void APopupWithoutAWidgetTakesTheRendererRequestedWindowAndAWidgetWithoutAPopupIsUnused()
    {
        PopupRecord[] records =
        [
            Created(1, 10, "6:30", frame: "UNRELATED"),
            Opened(2, 30, "POPUP-A"),
            Requested(3, 32, "POPUP-A", 2, Rect(492, 519, 307, 452)),
            Requested(4, 80, "POPUP-A", 2, Rect(0, 0, 1, 1)),
        ];

        var open = Assert.Single(PagePopups.OpenAt(records, Page, 50));
        Assert.Null(open.FrameSinkId);
        Assert.Equal(new PopupRect(492, 519, 307, 400), open.Window);
        Assert.Equal("page-popup-opened initialWindowRect", open.WindowSource);

        var at = PagePopups.WithWindowAt(open, records, 50);
        Assert.Equal(new PopupRect(492, 519, 307, 452), at.Window);
        Assert.Equal("page-popup-window-rect windowRect", at.WindowSource);
        Assert.Equal(32, at.WindowTime);
    }

    [Fact]
    public void TheWindowIsTheBrowsersLatestRectangleAtOrBeforeThePopupsState()
    {
        PopupRecord[] records =
        [
            Created(1, 20, "6:18"),
            Opened(2, 30, "POPUP-A"),
            // The renderer's request is not used once a widget is joined.
            Requested(3, 31, "POPUP-A", 2, Rect(9, 9, 9, 9)),
            Shown(4, 32, "6:18", Rect(492, 519, 307, 400)),
            Bounds(5, 40, "6:18", Rect(492, 519, 307, 452)),
            // An ignored request sets no rectangle.
            Bounds(6, 45, "6:18", null),
            Screen(7, 50, "6:18", Rect(492, 520, 307, 452)),
            Bounds(8, 60, "6:99", Rect(1, 1, 1, 1)),
            Screen(9, 90, "6:18", Rect(0, 0, 5, 5)),
        ];
        var open = Assert.Single(PagePopups.OpenAt(records, Page, 70));

        var shown = PagePopups.WithWindowAt(open, records, 35);
        Assert.Equal(new PopupRect(492, 519, 307, 400), shown.Window);
        Assert.Equal("popup-widget-shown viewBounds", shown.WindowSource);

        var bounds = PagePopups.WithWindowAt(open, records, 47);
        Assert.Equal(new PopupRect(492, 519, 307, 452), bounds.Window);
        Assert.Equal("popup-widget-bounds-requested setRect", bounds.WindowSource);
        Assert.Equal(40, bounds.WindowTime);

        var screen = PagePopups.WithWindowAt(open, records, 70);
        Assert.Equal(new PopupRect(492, 520, 307, 452), screen.Window);
        Assert.Equal("popup-widget-screen-rects viewRect", screen.WindowSource);
    }

    [Fact]
    public void TheWindowIsPlacedInThePageLessTheLocalRootOriginPlusTheRootScrollOffset()
    {
        PopupRecord[] records = [Created(1, 20, "6:18"), Opened(2, 30, "POPUP-A"), Bounds(3, 40, "6:18", Rect(492, 519, 307, 452))];
        var open = PagePopups.WithWindowAt(Assert.Single(PagePopups.OpenAt(records, Page, 50)), records, 50);

        Assert.True(open.AnchorMatchesOwner);
        Assert.Equal(new PopupRect(43, 432, 307, 452), open.InDocument(0, 0));
        Assert.Equal(new PopupRect(43.5, 1432, 307, 452), open.InDocument(0.5, 1000));
        Assert.False((open with { AnchorRectInScreen = new PopupRect(492, 470, 262, 48) }).AnchorMatchesOwner);
    }

    [Fact]
    public void OptionSelectednessIsKeptAcrossCheckpointsAndInSnapshots()
    {
        var state = new BrowserDocumentState("token-a dom-document-1");
        state.Interaction.Apply("option-selectedness-changed", J("""{"nodeId":7,"selectNodeId":5,"selected":true}"""));
        state.Interaction.Apply("option-selectedness-changed", J("""{"nodeId":8,"selectNodeId":5,"selected":true}"""));
        state.Interaction.Apply("option-selectedness-changed", J("""{"nodeId":7,"selectNodeId":5,"selected":false}"""));
        state.Interaction.Apply("interaction-checkpoint-started", J("""{"checkpointId":"c","focusedNodeId":null}"""));
        state.Interaction.Apply("interaction-checkpoint-completed", J("""{"checkpointId":"c"}"""));

        Assert.Equal(new Dictionary<long, bool> { [7] = false, [8] = true }, state.Interaction.OptionSelectedness);
        var read = BrowserStateSnapshot.Read(BrowserStateSnapshot.Serialize(state));
        Assert.Equal(new Dictionary<long, bool> { [7] = false, [8] = true }, read.Interaction.OptionSelectedness);
    }

    [Fact]
    public void TheTreeDataCarriesOptionSelectednessAndEachOpenPopup()
    {
        var page = RecordedPageTests.State();
        page.Interaction.Apply("option-selectedness-changed", J("""{"nodeId":33,"selectNodeId":12,"selected":true}"""));
        PopupRecord[] records = [Created(1, 20, "6:18"), Opened(2, 30, "POPUP-A", owner: "token-a"), Bounds(3, 40, "6:18", Rect(492, 519, 307, 452))];
        var open = PagePopups.WithWindowAt(Assert.Single(PagePopups.OpenAt(records, "token-a", 50)), records, 50);
        var popupState = RecordedPageTests.State();

        var content = RecordedPage.Content(page, "about:blank", 50, 50, "basis", null, [new RecordedPopup(open, popupState, "its basis")]);

        var start = content.Html.IndexOf("id=\"recorder-recreation-tree\">", StringComparison.Ordinal) + "id=\"recorder-recreation-tree\">".Length;
        var end = content.Html.IndexOf("</script>", start, StringComparison.Ordinal);
        using var data = JsonDocument.Parse(content.Html[start..end]);
        var root = data.RootElement;
        var selected = Assert.Single(root.GetProperty("optionSelectedness").EnumerateArray());
        Assert.Equal(33, selected[0].GetInt64());
        Assert.True(selected[1].GetBoolean());
        var popup = Assert.Single(root.GetProperty("popups").EnumerateArray());
        Assert.Equal(43, popup.GetProperty("left").GetDouble());
        Assert.Equal(432, popup.GetProperty("top").GetDouble());
        Assert.Equal(307, popup.GetProperty("width").GetDouble());
        Assert.Equal(452, popup.GetProperty("height").GetDouble());
        Assert.Equal("select-list", popup.GetProperty("kind").GetString());
        Assert.Equal(3023, popup.GetProperty("ownerNodeId").GetInt64());
        // The popup's own served markup, whose builder runs by the page's nonce.
        var markup = popup.GetProperty("markup").GetString()!;
        Assert.StartsWith("<!DOCTYPE html><html><head>", markup, StringComparison.Ordinal);
        Assert.Contains($"<script nonce=\"{content.ScriptNonce}\">", markup, StringComparison.Ordinal);
        Assert.Contains(content.Evidence.Notes, note => note.Contains("6:18", StringComparison.Ordinal) &&
            note.Contains("popup-widget-bounds-requested setRect", StringComparison.Ordinal) &&
            note.Contains("(43, 432)", StringComparison.Ordinal));
        Assert.Contains(content.Evidence.Notes, note => note.StartsWith("Check: ", StringComparison.Ordinal));
    }

    [Fact]
    public void APopupWithoutADomWalkIsNotDrawnAndSaysSo()
    {
        PopupRecord[] records = [Opened(2, 30, "POPUP-A", owner: "token-a")];
        var open = Assert.Single(PagePopups.OpenAt(records, "token-a", 50));

        var content = RecordedPage.Content(RecordedPageTests.State(), "about:blank", 50, 50, "basis", null, [new RecordedPopup(open, null, "none")]);

        Assert.Contains("\"popups\":[]", content.Html, StringComparison.Ordinal);
        Assert.Contains(content.Evidence.Notes, note => note.Contains("is not drawn", StringComparison.Ordinal));
    }

    [Fact]
    public void TheBrowsersPopupWidgetRecordsMakeNoDocument()
    {
        var builder = new BrowserStateBuilder();
        var created = Created(1, 20, "6:18");

        Assert.Null(builder.KeyOf("browser.interaction", created.Payload));
        Assert.Null(builder.Apply(1, 20, "browser.interaction", created.EventType, created.Payload));
        Assert.Empty(builder.Documents);
    }

    [Fact]
    public void ThePlaybackIndexKeepsThePopupRecordsWhole()
    {
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var opened = Opened(2, 30, "POPUP-A");
        builder.Add(1, new RecorderEvent
        {
            SchemaVersion = RecorderEvent.CurrentSchemaVersion,
            EventId = "e1",
            EvidenceClass = EvidenceClasses.Observed,
            SessionId = "s",
            CollectorType = "c",
            CollectorInstanceId = "i",
            ProducerVersion = "p",
            Channel = "browser.interaction",
            CaptureMethod = "m",
            Sequence = 1,
            MonotonicNanoseconds = 30,
            ClockMappingId = "k",
            ObservedUtc = DateTimeOffset.UnixEpoch,
            EventType = opened.EventType,
            Payload = opened.Payload,
        });

        var index = builder.Build();
        var kept = Assert.Single(PagePopups.Records(index));
        Assert.Equal(opened.Payload.GetRawText(), kept.Payload.GetRawText());
        Assert.Single(PagePopups.OpenAt(PagePopups.Records(index), Page, 30));
    }
}

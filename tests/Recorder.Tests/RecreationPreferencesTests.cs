using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;
using Recorder.Database.RecordingFiles;
using Recorder.Recreation;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The recorded page values a recreation gives its page (protocol 0.57,
/// accessibility preferences stage 3): the color-maps-sent record and its
/// validation, the page's values at a time, the zoom chosen for it, the
/// attribute written, and the evidence panel's notes. See
/// docs/architecture/accessibility-preferences.md, "Stage 3".
/// </summary>
public sealed class RecreationPreferencesTests
{
    private const long Second = 1_000_000_000;

    private static JsonObject Sample(string eventType, int index = 0) =>
        (JsonObject)JsonNode.Parse(EvidenceSamples.All
            .Where(item => item.Channel == BrowserPreferenceSettings.Channel && item.EventType == eventType)
            .ElementAt(index).Payload)!;

    private static List<EventValidationIssue> Validate(JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(
            BrowserPreferenceSettings.Channel, BrowserPreferenceSettings.ColorMapsEventType, document.RootElement, 100, issues);
        return issues;
    }

    private static BrowserPreferenceRecord Record(long time, string eventType, JsonNode payload) =>
        new(time, eventType, JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone());

    private static JsonObject Zoom(string mode, string host, string scheme, double level, bool followsDefault = false)
    {
        var zoom = Sample(BrowserPreferenceSettings.ZoomEventType);
        zoom["mode"] = mode;
        zoom["host"] = host;
        zoom["scheme"] = scheme;
        zoom["followsDefault"] = followsDefault;
        zoom["zoomLevel"] = level;
        zoom["zoomPercent"] = Math.Pow(1.2, level) * 100;
        return zoom;
    }

    private static JsonObject Sent(int page, int index)
    {
        var sent = Sample(BrowserPreferenceSettings.SentEventType, index);
        sent["pageFrameTreeNodeId"] = page;
        return sent;
    }

    private static JsonObject Maps(int page, bool first)
    {
        var maps = (JsonObject)JsonNode.Parse(EvidenceSamples.ColorMapsSample(first))!;
        maps["pageFrameTreeNodeId"] = page;
        return maps;
    }

    // Page 12, https://example.org/, committed at 2 s: sent everything at
    // 2 s and dark with forced colors at 6 s; its color maps at 2 s and a
    // forced colors map at 6 s. The default zoom is 1 at 3 s and the host
    // example.org 2 at 4 s.
    private static BrowserPreferenceTimeline Recording(params BrowserPreferenceRecord[] more) =>
        new(
            [
                Record(Second, BrowserPreferenceSettings.SnapshotEventType, Sample(BrowserPreferenceSettings.SnapshotEventType)),
                Record(2 * Second, BrowserPreferenceSettings.SentEventType, Sent(12, 0)),
                Record(2 * Second, BrowserPreferenceSettings.ColorMapsEventType, Maps(12, true)),
                Record(3 * Second, BrowserPreferenceSettings.ZoomEventType, Zoom("default", "", "", 1)),
                Record(4 * Second, BrowserPreferenceSettings.ZoomEventType, Zoom("host", "example.org", "", 2)),
                Record(6 * Second, BrowserPreferenceSettings.SentEventType, Sent(12, 1)),
                Record(6 * Second, BrowserPreferenceSettings.ColorMapsEventType, Maps(12, false)),
                .. more
            ],
            [new BrowserPageCommit(2 * Second, 12, "https://example.org/")]);

    [Fact]
    public void TheColorMapSamplesAreValidAndReadIntoTheirContract()
    {
        foreach (var first in new[] { true, false })
        {
            var payload = JsonNode.Parse(EvidenceSamples.ColorMapsSample(first))!;
            Assert.Empty(Validate(payload));
            var element = JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone();
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Preferences, BrowserPreferenceSettings.ColorMapsEventType, element);
            var contract = BrowserProtocol.Deserialize<BrowserColorMapsSentPayload>(element);
            Assert.Equal(first ? 3 : 1, contract.Maps.Count);
            Assert.Equal(67, contract.Maps["forcedColors"].Count);
        }

        Assert.Equal(67, BrowserPreferenceSettings.RendererColorNames.Distinct().Count());
    }

    [Fact]
    public void AColorMapsRecordThatBreaksTheContractIsRefused()
    {
        var incomplete = Maps(12, true);
        ((JsonObject)incomplete["maps"]!).Remove("dark");
        Assert.Contains(Validate(incomplete), issue => issue.Code == "browser-color-maps-sent-incomplete");

        var empty = Maps(12, false);
        empty["maps"] = new JsonObject();
        Assert.Contains(Validate(empty), issue => issue.Code == "browser-color-maps-sent-empty");

        var unknownMap = Maps(12, false);
        ((JsonObject)unknownMap["maps"]!)["sepia"] = new JsonObject();
        Assert.Contains(Validate(unknownMap), issue => issue.Code == "payload-property-unexpected");

        var badColor = Maps(12, false);
        badColor["maps"]!["forcedColors"]!["kColorCssSystemWindow"] = "#FFF";
        Assert.Contains(Validate(badColor), issue => issue.Code == "payload-property-invalid");

        var missingColor = Maps(12, false);
        ((JsonObject)missingColor["maps"]!["forcedColors"]!).Remove("kColorCssSystemWindow");
        Assert.Contains(Validate(missingColor), issue => issue.Code == "payload-property-missing");

        var unknownColor = Maps(12, false);
        unknownColor["maps"]!["forcedColors"]!["kColorNotAColor"] = "#FF000000";
        Assert.Contains(Validate(unknownColor), issue => issue.Code == "payload-property-unexpected");

        var unknownPoint = Maps(12, false);
        unknownPoint["point"] = "web-preferences";
        Assert.NotEmpty(Validate(unknownPoint));
    }

    [Fact]
    public void ThePageIsGivenItsFirstSendWithEachLaterOneMergedOverIt()
    {
        var timeline = Recording();
        var before = timeline.PageValuesAt(12, "https://example.org/", 5 * Second);
        Assert.Equal(BrowserPreferenceSettings.Page.Count, before.Fields.Count);
        Assert.All(before.Fields, field => Assert.Equal(2 * Second, field.Time));
        Assert.Equal("light", before.Fields.Single(field => field.Setting.Name == "preferredColorScheme").Value.GetString());

        var after = timeline.PageValuesAt(12, "https://example.org/", 7 * Second);
        Assert.Equal(BrowserPreferenceSettings.Page.Count, after.Fields.Count);
        var scheme = after.Fields.Single(field => field.Setting.Name == "preferredColorScheme");
        Assert.Equal("dark", scheme.Value.GetString());
        Assert.Equal(6 * Second, scheme.Time);
        Assert.Equal(2 * Second, after.Fields.Single(field => field.Setting.Name == "defaultFontSize").Time);
        Assert.True(after.PreferencesRecorded);
    }

    [Fact]
    public void AnotherPageAndAPageWithoutSendsAreGivenNoFields()
    {
        var timeline = Recording();
        Assert.Empty(timeline.PageValuesAt(30, "https://example.org/", 7 * Second).Fields);
        Assert.Empty(timeline.PageValuesAt(null, "https://example.org/", 7 * Second).Fields);
        Assert.Empty(timeline.PageValuesAt(12, "https://example.org/", Second).Fields);
    }

    [Fact]
    public void TheColorMapsAreTheLastOfEachSentToThePage()
    {
        var timeline = Recording();
        var before = timeline.PageValuesAt(12, "https://example.org/", 5 * Second);
        Assert.Equal(["light", "dark", "forcedColors"], before.ColorMaps.Select(map => map.Name));
        Assert.Equal("#FF000000", before.ColorMaps[2].Colors["kColorCssSystemWindow"]);

        var after = timeline.PageValuesAt(12, "https://example.org/", 7 * Second);
        Assert.Equal(2 * Second, after.ColorMaps[0].Time);
        Assert.Equal(6 * Second, after.ColorMaps[2].Time);
        Assert.Equal("#FFFFFF00", after.ColorMaps[2].Colors["kColorCssSystemWindow"]);
        Assert.True(after.ColorMapsRecorded);
    }

    [Fact]
    public void TheZoomIsTheHostsThenTheDefaultThenNone()
    {
        var timeline = Recording();
        var none = timeline.PageValuesAt(12, "https://example.org/", 2 * Second).Zoom!;
        Assert.Equal(("none", 0.0), (none.Source, none.ZoomLevel));
        Assert.Null(none.Time);

        var byDefault = timeline.PageValuesAt(12, "https://example.org/", 3 * Second).Zoom!;
        Assert.Equal(("default", 1.0, (long?)(3 * Second)), (byDefault.Source, byDefault.ZoomLevel, byDefault.Time));

        var host = timeline.PageValuesAt(12, "https://EXAMPLE.org/page", 5 * Second).Zoom!;
        Assert.Equal(("host", 2.0), (host.Source, host.ZoomLevel));
        Assert.Equal(1.44, host.Factor, 9);

        var otherHost = timeline.PageValuesAt(12, "https://example.com/", 5 * Second).Zoom!;
        Assert.Equal("default", otherHost.Source);
    }

    [Fact]
    public void TheTemporaryZoomComesFirstAndOnlyFromThePagesCommitOn()
    {
        var timeline = Recording(
            Record(Second, BrowserPreferenceSettings.ZoomEventType, Zoom("temporary", "example.org", "", -1)),
            Record(5 * Second, BrowserPreferenceSettings.ZoomEventType, Zoom("scheme-and-host", "example.org", "https", 3)),
            Record(8 * Second, BrowserPreferenceSettings.ZoomEventType, Zoom("temporary", "example.org", "", 4)));
        // The temporary record at 1 s is before the page's commit at 2 s.
        Assert.Equal("host", timeline.PageValuesAt(12, "https://example.org/", 4 * Second).Zoom!.Source);
        var schemeAndHost = timeline.PageValuesAt(12, "https://example.org/", 6 * Second).Zoom!;
        Assert.Equal(("scheme-and-host", 3.0), (schemeAndHost.Source, schemeAndHost.ZoomLevel));
        Assert.Equal("host", timeline.PageValuesAt(12, "http://example.org/", 6 * Second).Zoom!.Source);
        var temporary = timeline.PageValuesAt(12, "https://example.org/", 9 * Second).Zoom!;
        Assert.Equal(("temporary", 4.0), (temporary.Source, temporary.ZoomLevel));
    }

    [Fact]
    public void AHostThatFollowsTheDefaultGivesWayToIt()
    {
        var timeline = Recording(
            Record(5 * Second, BrowserPreferenceSettings.ZoomEventType, Zoom("default", "", "", 1.5)),
            Record(5 * Second, BrowserPreferenceSettings.ZoomEventType, Zoom("host", "example.org", "", 1.5, followsDefault: true)));
        var zoom = timeline.PageValuesAt(12, "https://example.org/", 6 * Second).Zoom!;
        Assert.Equal(("default", 1.5), (zoom.Source, zoom.ZoomLevel));
    }

    [Fact]
    public void ARecordingWithoutBrowserPreferencesGivesNothingToApply()
    {
        var values = BrowserPreferenceTimeline.Empty.PageValuesAt(12, "https://example.org/", Second);
        Assert.Null(values.Zoom);
        Assert.False(values.PreferencesRecorded);
        Assert.False(values.ColorMapsRecorded);
        Assert.Equal(string.Empty, RecordedPreferences.AttributeText(values));
        var note = Assert.Single(RecordedPreferences.Notes(values));
        Assert.Contains("before protocol 0.56", note, StringComparison.Ordinal);
        Assert.Contains("before protocol 0.56", Assert.Single(RecordedPreferences.Notes(null)), StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordingBefore057SaysTheColorMapsWereNotRecorded()
    {
        var timeline = new BrowserPreferenceTimeline(
            [Record(2 * Second, BrowserPreferenceSettings.SentEventType, Sent(12, 0))],
            [new BrowserPageCommit(2 * Second, 12, "https://example.org/")]);
        var values = timeline.PageValuesAt(12, "https://example.org/", 3 * Second);
        Assert.False(values.ColorMapsRecorded);
        Assert.Empty(values.ColorMaps);
        var notes = RecordedPreferences.Notes(values);
        Assert.Contains(notes, note => note.Contains("before protocol 0.57", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.StartsWith("The page is given the recorded values of the", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("100 percent zoom", StringComparison.Ordinal));
        Assert.DoesNotContain("color ", RecordedPreferences.AttributeText(values), StringComparison.Ordinal);
    }

    [Fact]
    public void TheAttributeHoldsEachFieldTheZoomAndEachColor()
    {
        var values = Recording().PageValuesAt(12, "https://example.org/", 7 * Second);
        var text = RecordedPreferences.AttributeText(values);
        var entries = text.Split("; ");
        Assert.Contains("field preferredColorScheme t dark", entries);
        Assert.Contains("field defaultFontSize i 20", entries);
        Assert.Contains("field inForcedColors b true", entries);
        Assert.Contains("field caretBlinkIntervalMilliseconds n 530", entries);
        Assert.Contains("field standardFontFamily t Times%20New%20Roman", entries);
        Assert.Contains("field textTrackTextSize t ", entries);
        Assert.Contains("field focusRingColor t %23FFE59700", entries);
        Assert.Contains("zoom 2", entries);
        Assert.Contains("color light kColorCssSystemWindow FFFFFFFF", entries);
        Assert.Contains("color forcedColors kColorCssSystemWindow FFFFFF00", entries);
        Assert.Equal(BrowserPreferenceSettings.Page.Count + 1 + 3 * 67, entries.Length);
        Assert.Equal(entries.Length, entries.Distinct().Count());
    }

    [Fact]
    public void TextIsPercentEncodedAsTheRendererReadsIt()
    {
        Assert.Equal("Segoe%20UI", RecordedPreferences.Encode("Segoe UI"));
        Assert.Equal("a-b.c_d~e%3B%25", RecordedPreferences.Encode("a-b.c_d~e;%"));
        Assert.Equal("%C3%A9", RecordedPreferences.Encode("\u00e9"));
        Assert.Equal(string.Empty, RecordedPreferences.Encode(string.Empty));
    }

    [Fact]
    public void TheServedRootCarriesTheAttribute()
    {
        var markup = RecordedPage.Markup([], "html", "n0nce", "field defaultFontSize i 20; zoom 1");
        Assert.StartsWith(
            "<!DOCTYPE html><html data-a11y-recorded-preferences=\"field defaultFontSize i 20; zoom 1\"><head>",
            markup,
            StringComparison.Ordinal);
        Assert.StartsWith("<!DOCTYPE html><html><head>", RecordedPage.Markup([], "html", "n0nce"), StringComparison.Ordinal);
        Assert.StartsWith("<!DOCTYPE html><html><head>", RecordedPage.Markup([], "html", "n0nce", string.Empty), StringComparison.Ordinal);
        Assert.Contains("&quot;", RecordedPage.Markup([], "html", "n0nce", "a\"b"), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePanelListsEachValueWithTheTimeOfItsRecord()
    {
        var notes = RecordedPreferences.Notes(Recording().PageValuesAt(12, "https://example.org/", 7 * Second));
        var fields = Assert.Single(notes, note => note.StartsWith("The page is given the recorded values of", StringComparison.Ordinal));
        Assert.Contains("from the record at 6.000 s", fields, StringComparison.Ordinal);
        Assert.Contains("from the record at 2.000 s", fields, StringComparison.Ordinal);
        Assert.Contains(notes, note => note.Contains("host's recorded zoom, 144 percent, zoom level 2, from the host record at 4.000 s", StringComparison.Ordinal));
        Assert.Contains(notes, note => note == "The page is given the recorded forced colors color map, 67 colors, from the record at 6.000 s.");
        Assert.Contains(notes, note => note.Contains("was not an attribute of the recorded page", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("a renderer process of its own", StringComparison.Ordinal));
        Assert.DoesNotContain(notes, note => note.Contains('\u2014'));
    }

    [Fact]
    public void TheViewportIsEmulatedAtTheZoomedSize()
    {
        var viewport = new RecreationViewport(800, 600, 1.8, 1.8) { BrowserZoomFactor = 1.2 };
        Assert.Equal(960, viewport.EmulatedWidth, 9);
        Assert.Equal(720, viewport.EmulatedHeight, 9);
        Assert.Equal(1.5, viewport.EmulatedDeviceScaleFactor, 9);
        Assert.Equal((976, 800), RecreationControl.WindowSize(viewport, 16, 80));

        Assert.Equal(800, viewport.ShownWidth, 9);
        Assert.Equal(1.8, viewport.ShownDevicePixelRatio, 9);

        // Zoomed to 125 percent after a checkpoint at 100 percent: the same
        // window, so a narrower page and a larger devicePixelRatio.
        var later = new RecreationViewport(800, 600, 1.5, 1.5) { BrowserZoomFactor = 1.25, CheckpointZoomFactor = 1.0 };
        Assert.Equal(800, later.EmulatedWidth, 9);
        Assert.Equal(600, later.EmulatedHeight, 9);
        Assert.Equal(1.5, later.EmulatedDeviceScaleFactor, 9);
        Assert.Equal(640, later.ShownWidth, 9);
        Assert.Equal(480, later.ShownHeight, 9);
        Assert.Equal(1.875, later.ShownDevicePixelRatio, 9);
        Assert.Equal((816, 680), RecreationControl.WindowSize(later, 16, 80));

        var plain = new RecreationViewport(800, 600, 1.5, 1.5);
        Assert.Equal((816, 680), RecreationControl.WindowSize(plain, 16, 80));
        Assert.Equal(1.5, plain.EmulatedDeviceScaleFactor, 9);
    }

    [Fact]
    public void ADocumentsPageIsThatOfItsPageCommit()
    {
        PlaybackIndexEvent Navigation(long key, string token, string pageId, string frameType = "primary-main-frame") =>
            new(key, $"e{key}", EvidenceClasses.Observed, "browser.navigation", "navigation-completed", key * Second,
                JsonDocument.Parse(
                    $$$"""{"committed":true,"primaryPage":true,"frameType":"{{{frameType}}}","sameDocument":false,"url":"https://example.org/","context":{"pageId":"{{{pageId}}}","documentToken":"{{{token}}}"}}""")
                    .RootElement.Clone());
        var pages = RecordingFileDocuments.PagesByDocumentToken(
        [
            Navigation(1, "TOKEN-A", "frame-12"),
            Navigation(2, "TOKEN-B", "frame-30"),
            Navigation(3, "TOKEN-C", "frame-40", "subframe"),
            Navigation(4, "TOKEN-D", "page-1")
        ]);
        Assert.Equal(12, pages["TOKEN-A"]);
        Assert.Equal(30, pages["TOKEN-B"]);
        Assert.False(pages.ContainsKey("TOKEN-C"));
        Assert.False(pages.ContainsKey("TOKEN-D"));
    }

    [Fact]
    public void TheSummaryNamesTheMapsSent()
    {
        Assert.Equal(
            "color-maps-sent: color-providers, forcedColors",
            SessionPlaybackArchiveBuilder.CreateSummary(
                BrowserPreferenceSettings.Channel,
                BrowserPreferenceSettings.ColorMapsEventType,
                JsonDocument.Parse(EvidenceSamples.ColorMapsSample(false)).RootElement));
    }
}

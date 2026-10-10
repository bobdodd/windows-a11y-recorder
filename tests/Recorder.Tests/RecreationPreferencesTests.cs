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
        // A zoom of 100 percent from no record is not noted, as nothing of
        // it is applied.
        Assert.DoesNotContain(notes, note => note.Contains("recorded zoom", StringComparison.Ordinal));
        Assert.DoesNotContain("color ", RecordedPreferences.AttributeText(values), StringComparison.Ordinal);
    }

    [Fact]
    public void TheAttributeHoldsEachFieldAndEachColorButNotTheZoom()
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
        // The zoom level is not given: its effect is in the layout zoom.
        Assert.DoesNotContain(entries, entry => entry.StartsWith("zoom ", StringComparison.Ordinal));
        Assert.Contains("color light kColorCssSystemWindow FFFFFFFF", entries);
        Assert.Contains("color forcedColors kColorCssSystemWindow FFFFFF00", entries);
        Assert.Equal(BrowserPreferenceSettings.Page.Count + 3 * 67, entries.Length);
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
        Assert.Contains(notes, note => note.Contains("recorded zoom at the frame was 144 percent, from the record at 4.000 s. It is not applied as a zoom level", StringComparison.Ordinal));
        Assert.Contains(notes, note => note == "The page is given the recorded forced colors color map, 67 colors, from the record at 6.000 s.");
        Assert.Contains(notes, note => note.Contains("was not an attribute of the recorded page", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("a renderer process of its own", StringComparison.Ordinal));
        Assert.DoesNotContain(notes, note => note.Contains('\u2014'));
    }

    [Fact]
    public void TheFrameIsItsRecordedSizeInScreenPixels()
    {
        // The owner's recording of 2026-10-09: a checkpoint at 200 percent
        // text size, 932 by 409 CSS pixels at a ratio of 2, so 1864 by 818
        // screen pixels; then Windows' text size set back to 100 percent,
        // after which every change set names a layout zoom of 1 and no
        // checkpoint was recorded. The frame keeps its size in screen pixels.
        var before = new RecreationViewport(932, 409, 2, 2) { FrameLayoutZoomFactor = 2 };
        var after = new RecreationViewport(932, 409, 2, 2) { FrameLayoutZoomFactor = 1 };
        foreach (var viewport in new[] { before, after })
        {
            Assert.Equal(1864, viewport.ScreenWidth, 9);
            Assert.Equal(818, viewport.ScreenHeight, 9);
            // On the owner's screen, at a scale factor of 1.
            Assert.Equal(1864, viewport.WindowWidthAt(1), 9);
            Assert.Equal(818, viewport.WindowHeightAt(1), 9);
            Assert.Equal((1880, 898), RecreationControl.WindowSize(viewport, 16, 80, 1));
        }
        Assert.Equal(2, before.ShownLayoutZoomFactor, 9);
        Assert.Equal(932, before.ShownWidth, 9);
        Assert.Equal(409, before.ShownHeight, 9);
        Assert.Equal(2, before.ShownDevicePixelRatio, 9);
        Assert.Equal(1, after.ShownLayoutZoomFactor, 9);
        Assert.Equal(1864, after.ShownWidth, 9);
        Assert.Equal(818, after.ShownHeight, 9);
        Assert.Equal(1, after.ShownDevicePixelRatio, 9);

        // On a screen at 1.5, the same frame is fewer device-independent
        // pixels; a scale factor that is not above 0 is taken as 1.
        Assert.Equal(1864 / 1.5, before.WindowWidthAt(1.5), 9);
        Assert.Equal(1864, before.WindowWidthAt(0), 9);
        Assert.Equal(1864, before.WindowWidthAt(double.NaN), 9);

        // A page zoomed to 125 percent on a screen at 1.5: the window is
        // the same, and the page is laid out at the change set's 1.875.
        var zoomed = new RecreationViewport(800, 600, 1.5, 1.5) { FrameLayoutZoomFactor = 1.875 };
        Assert.Equal(1200, zoomed.ScreenWidth, 9);
        Assert.Equal(640, zoomed.ShownWidth, 9);
        Assert.Equal(480, zoomed.ShownHeight, 9);
        Assert.Equal(1.875, zoomed.ShownDevicePixelRatio, 9);
        Assert.Equal((816, 680), RecreationControl.WindowSize(zoomed, 16, 80, 1.5));

        // With no change set, the checkpoint's layout zoom.
        var none = new RecreationViewport(800, 600, 1.5, 1.5);
        Assert.Equal(1.5, none.ShownLayoutZoomFactor, 9);
        Assert.Equal(800, none.ShownWidth, 9);
        Assert.Equal((816, 680), RecreationControl.WindowSize(none, 16, 80, 1.5));
    }

    [Fact]
    public void TheAttributeGivesTheLayoutZoomWithOrWithoutPageValues()
    {
        Assert.Null(RecordedPreferences.AttributeText(null, null));
        Assert.Equal("layoutZoom 2", RecordedPreferences.AttributeText(null, 2.0));
        Assert.Equal("layoutZoom 1.5", RecordedPreferences.AttributeText(null, 1.5));
        Assert.Null(RecordedPreferences.AttributeText(null, 0));
        Assert.Null(RecordedPreferences.AttributeText(null, double.NaN));
        var values = Recording().PageValuesAt(12, "https://example.org/", 7 * Second);
        var entries = RecordedPreferences.AttributeText(values, 2)!.Split("; ");
        Assert.Contains("layoutZoom 2", entries);
        Assert.Contains("field defaultFontSize i 20", entries);
        Assert.Equal(RecordedPreferences.AttributeText(values).Split("; ").Length + 1, entries.Length);
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

    // Protocol 0.59: a theme value as a recreation reads it.
    private static BrowserThemeValue Theme(string name, string value, long time = Second) =>
        new(BrowserPreferenceSettings.FindBrowser(name)!,
            JsonDocument.Parse($$"""{"value":{{value}},"isDefault":false,"problem":null}""").RootElement.Clone(),
            time);

    [Fact]
    public void TheRecreationBrowsersProfileIsGivenTheRecordedTheme()
    {
        IReadOnlyList<BrowserThemeValue> theme =
        [
            Theme("colorScheme", "2"), Theme("userColor", "-1543926", 3 * Second), Theme("colorVariant", "3"),
            Theme("grayscaleTheme", "true"), Theme("themeId", "\"user_color_theme_id\"")
        ];
        var written = RecreationBrowser.ThemePreferences(theme);
        Assert.Equal(
            """{"color_scheme2":2,"user_color2":-1543926,"color_variant2":3,"is_grayscale2":true}""",
            written.ToJsonString());
        Assert.Contains(RecordedPreferences.ThemeNotes(theme), note => note.Contains("given the recorded theme color and its style", StringComparison.Ordinal));

        // The default theme draws no theme color: only the mode and grayscale.
        IReadOnlyList<BrowserThemeValue> plainTheme =
        [
            Theme("colorScheme", "2"), Theme("userColor", "-1543926"), Theme("colorVariant", "3"),
            Theme("grayscaleTheme", "true"), Theme("themeId", "\"\"")
        ];
        Assert.Equal("""{"color_scheme2":2,"is_grayscale2":true}""", RecreationBrowser.ThemePreferences(plainTheme).ToJsonString());
        Assert.Null(RecreationBrowser.ThemePreferenceValues(plainTheme).ThemeId);
        // Before 0.59, only the mode.
        Assert.Equal("""{"color_scheme2":2}""", RecreationBrowser.ThemePreferences([Theme("colorScheme", "2")]).ToJsonString());
        // A generated color theme's color is not recorded.
        IReadOnlyList<BrowserThemeValue> generated = [Theme("colorScheme", "1"), Theme("userColor", "-1543926"), Theme("themeId", "\"autogenerated_theme_id\"")];
        Assert.Equal("""{"color_scheme2":1}""", RecreationBrowser.ThemePreferences(generated).ToJsonString());
        Assert.Contains(RecordedPreferences.ThemeNotes(generated), note => note.Contains("generated color theme, whose color is not recorded", StringComparison.Ordinal));

        var directory = Path.Combine(Path.GetTempPath(), "theme-" + Guid.NewGuid().ToString("N"));
        try
        {
            RecreationBrowser.WriteProfile(directory, theme);
            using var preferences = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "Default", "Preferences")));
            var browserTheme = preferences.RootElement.GetProperty("browser").GetProperty("theme");
            Assert.Equal(-1543926, browserTheme.GetProperty("user_color2").GetInt64());
            // ThemeService draws the color only for the theme color's identity.
            Assert.Equal("user_color_theme_id", preferences.RootElement.GetProperty("extensions").GetProperty("theme").GetProperty("id").GetString());
            Assert.Equal(2, preferences.RootElement.GetProperty("net").GetProperty("network_prediction_options").GetInt32());
            // Without a recorded theme the profile has no theme preferences.
            RecreationBrowser.WriteProfile(directory);
            using var plain = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "Default", "Preferences")));
            Assert.False(plain.RootElement.TryGetProperty("browser", out _));
            Assert.False(plain.RootElement.TryGetProperty("extensions", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AnInstalledThemeLeavesTheDefaultThemeWithTheRecordedMode()
    {
        IReadOnlyList<BrowserThemeValue> theme =
        [
            Theme("colorScheme", "1"), Theme("userColor", "-1543926"), Theme("colorVariant", "3"),
            Theme("grayscaleTheme", "true"), Theme("themeId", "\"abcdefghijklmnopabcdefghijklmnop\"")
        ];
        Assert.Equal("""{"color_scheme2":1}""", RecreationBrowser.ThemePreferences(theme).ToJsonString());
        var notes = RecordedPreferences.ThemeNotes(theme);
        Assert.Contains(notes, note => note.Contains("installed theme, abcdefghijklmnopabcdefghijklmnop", StringComparison.Ordinal));
        Assert.DoesNotContain(notes, note => note.Contains("viewing machine's Windows light or dark mode", StringComparison.Ordinal));
    }

    [Fact]
    public void TheThemeNotesListTheValuesAndWhatWasNotRecorded()
    {
        var notes = RecordedPreferences.ThemeNotes([Theme("colorScheme", "0")]);
        Assert.Contains("Browser color mode: system, from the record at 1.000 s", notes[0], StringComparison.Ordinal);
        Assert.Contains(notes, note => note.StartsWith("These theme settings were not recorded, as in a recording made before protocol 0.59", StringComparison.Ordinal) &&
            note.Contains("Browser theme color, Browser theme color style, Browser grayscale theme, Browser theme in use", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("viewing machine's Windows light or dark mode", StringComparison.Ordinal));
        Assert.StartsWith("The participant's browser theme was not recorded", Assert.Single(RecordedPreferences.ThemeNotes([])), StringComparison.Ordinal);
        Assert.StartsWith("The participant's browser theme was not recorded", Assert.Single(RecordedPreferences.ThemeNotes(null)), StringComparison.Ordinal);
    }


    [Fact]
    public void TheContentCarriesTheThemeAndItsNotes()
    {
        IReadOnlyList<BrowserThemeValue> theme = [Theme("colorScheme", "2"), Theme("userColor", "-1543926")];
        var content = RecordedPage.Content(RecordedPageTests.State(), "about:blank", 50, 50, "basis", browserTheme: theme);
        Assert.Same(theme, content.BrowserTheme);
        Assert.Contains(content.Evidence.Notes, note => note.Contains("Browser theme color: #E8710A", StringComparison.Ordinal));
        var none = RecordedPage.Content(RecordedPageTests.State(), "about:blank", 50, 50, "basis");
        Assert.Contains(none.Evidence.Notes, note => note.StartsWith("The participant's browser theme was not recorded", StringComparison.Ordinal));
    }

}

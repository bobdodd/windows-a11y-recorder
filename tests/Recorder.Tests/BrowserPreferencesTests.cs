using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Browser;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The browser preference records of protocol 0.56, their validation, and
/// the properties panel's Browser and Sent to the page rows. See
/// docs/architecture/accessibility-preferences.md, "Stage 2".
/// </summary>
public sealed class BrowserPreferencesTests
{
    private const long Second = 1_000_000_000;

    private static JsonObject Sample(string eventType, int index = 0) =>
        (JsonObject)JsonNode.Parse(EvidenceSamples.All
            .Where(item => item.Channel == BrowserPreferenceSettings.Channel && item.EventType == eventType)
            .ElementAt(index).Payload)!;

    private static List<EventValidationIssue> Validate(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(BrowserPreferenceSettings.Channel, eventType, document.RootElement, 100, issues);
        return issues;
    }

    private static BrowserPreferenceRecord Record(long time, string eventType, JsonNode payload) =>
        new(time, eventType, JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone());

    private static JsonObject Change(string preference, JsonNode? value)
    {
        var change = Sample(BrowserPreferenceSettings.ChangeEventType);
        change["preference"] = preference;
        change["previous"] = new JsonObject();
        change["current"] = new JsonObject
        {
            [preference] = new JsonObject { ["value"] = value, ["isDefault"] = false, ["problem"] = null }
        };
        return change;
    }

    private static JsonObject DefaultZoom(double percent)
    {
        var zoom = Sample(BrowserPreferenceSettings.ZoomEventType, 1);
        zoom["zoomLevel"] = Math.Log(percent / 100) / Math.Log(1.2);
        zoom["zoomPercent"] = percent;
        return zoom;
    }

    private static JsonObject Sent(int page, bool first, JsonObject fields)
    {
        var sent = Sample(BrowserPreferenceSettings.SentEventType, first ? 0 : 1);
        sent["pageFrameTreeNodeId"] = page;
        if (!first)
        {
            sent["fields"] = fields;
        }

        return sent;
    }

    private static PropertyRow Row(IReadOnlyList<PropertyRow> rows, string group, string setting) =>
        rows.Single(row => row.Group == group && row.Setting == setting);

    // A recording: the preferences at 1 s; focus highlight on at 3 s and
    // the default font size 24 at 5 s; the default zoom 150 percent at 4 s;
    // page 12 loaded at 2 s, sent everything at 2 s and dark at 6 s; page
    // 30 loaded at 7 s and sent everything at 7 s.
    private static BrowserPreferenceTimeline Recording()
    {
        var page30 = Sample(BrowserPreferenceSettings.SentEventType);
        page30["pageFrameTreeNodeId"] = 30;
        return new BrowserPreferenceTimeline(
            [
                Record(Second, BrowserPreferenceSettings.SnapshotEventType, Sample(BrowserPreferenceSettings.SnapshotEventType)),
                Record(2 * Second, BrowserPreferenceSettings.SentEventType, Sent(12, true, [])),
                Record(3 * Second, BrowserPreferenceSettings.ChangeEventType, Change("focusHighlight", true)),
                Record(4 * Second, BrowserPreferenceSettings.ZoomEventType, DefaultZoom(150)),
                Record(4 * Second, BrowserPreferenceSettings.ZoomEventType, Sample(BrowserPreferenceSettings.ZoomEventType)),
                Record(5 * Second, BrowserPreferenceSettings.ChangeEventType, Change("defaultFontSize", 24)),
                Record(6 * Second, BrowserPreferenceSettings.SentEventType,
                    Sent(12, false, new JsonObject { ["preferredColorScheme"] = "dark" })),
                Record(7 * Second, BrowserPreferenceSettings.SentEventType, page30)
            ],
            [
                new BrowserPageCommit(2 * Second, 12, "https://example.org/"),
                new BrowserPageCommit(7 * Second, 30, "https://example.com/")
            ]);
    }

    [Fact]
    public void TheSamplesAreValid()
    {
        foreach (var (channel, eventType, payload) in EvidenceSamples.All.Where(item => item.Channel == BrowserPreferenceSettings.Channel))
        {
            Assert.Empty(Validate(eventType, JsonNode.Parse(payload)!));
            Assert.Equal(BrowserPreferenceSettings.Channel, channel);
        }
    }

    [Fact]
    public void TheBrowserProtocolReadsEverySampleIntoItsContract()
    {
        foreach (var (_, eventType, payload) in EvidenceSamples.All.Where(item => item.Channel == BrowserPreferenceSettings.Channel))
        {
            BrowserProtocol.ValidateEvidencePayload(
                BrowserEvidenceChannels.Preferences, eventType, JsonDocument.Parse(payload).RootElement.Clone());
        }

        Assert.Equal(BrowserPreferenceSettings.Channel, BrowserEvidenceChannels.Preferences);
        Assert.True(EventPayloadValidator.IsBuiltInChannel(BrowserEvidenceChannels.Preferences));
    }

    [Fact]
    public void AChangeOfAnUnlistedPreferenceIsRefused()
    {
        Assert.NotEmpty(Validate(BrowserPreferenceSettings.ChangeEventType, Change("browser.show_home_button", true)));
    }

    [Fact]
    public void ASnapshotWithAnUnlistedPreferenceIsRefused()
    {
        var snapshot = Sample(BrowserPreferenceSettings.SnapshotEventType);
        snapshot["preferences"]!["history"] = new JsonObject { ["value"] = "x", ["isDefault"] = true, ["problem"] = null };
        Assert.NotEmpty(Validate(BrowserPreferenceSettings.SnapshotEventType, snapshot));
    }

    [Fact]
    public void ASendWithNoFieldIsRefused()
    {
        Assert.NotEmpty(Validate(BrowserPreferenceSettings.SentEventType, Sent(12, false, [])));
    }

    [Fact]
    public void AFirstSendWithoutEveryFieldIsRefused()
    {
        var sent = Sample(BrowserPreferenceSettings.SentEventType);
        sent["fields"]!.AsObject().Remove("preferredColorScheme");
        Assert.NotEmpty(Validate(BrowserPreferenceSettings.SentEventType, sent));
    }

    [Fact]
    public void ASendOfAnUnlistedFieldOrPointIsRefused()
    {
        Assert.NotEmpty(Validate(
            BrowserPreferenceSettings.SentEventType,
            Sent(12, false, new JsonObject { ["javascriptEnabled"] = true })));
        var sent = Sent(12, false, new JsonObject { ["preferredColorScheme"] = "dark" });
        sent["point"] = "navigation";
        Assert.NotEmpty(Validate(BrowserPreferenceSettings.SentEventType, sent));
    }

    [Fact]
    public void AZoomChangeOfAnUnknownModeIsRefused()
    {
        var zoom = Sample(BrowserPreferenceSettings.ZoomEventType);
        zoom["mode"] = "page";
        Assert.NotEmpty(Validate(BrowserPreferenceSettings.ZoomEventType, zoom));
    }

    [Fact]
    public void ARecordingWithoutBrowserPreferenceRecordsHasOneRowSayingSo()
    {
        var rows = BrowserPreferenceTimeline.Empty.RowsAt(Second);
        var row = Assert.Single(rows);
        Assert.Equal(BrowserPreferenceTimeline.BrowserGroup, row.Group);
        Assert.Equal("not recorded", row.Value);
        Assert.Null(BrowserPreferenceTimeline.Empty.ChangeTimesOf(row.Key));
    }

    [Fact]
    public void TheBrowserRowsHoldTheValuesAtTheStartAndAfterEachChange()
    {
        var timeline = Recording();
        var start = timeline.RowsAt(2 * Second);
        Assert.Equal("off (default)", Row(start, BrowserPreferenceTimeline.BrowserGroup, "Focus highlight").Value);
        Assert.Equal("at start", Row(start, BrowserPreferenceTimeline.BrowserGroup, "Focus highlight").WhenSetShown);
        Assert.Equal("20 px", Row(start, BrowserPreferenceTimeline.BrowserGroup, "Font size").Value);
        Assert.Equal("system (default)", Row(start, BrowserPreferenceTimeline.BrowserGroup, "Browser color mode").Value);
        Assert.Equal("example.org", Row(start, BrowserPreferenceTimeline.BrowserGroup, "Sites without page colors").Value);
        Assert.StartsWith("not read", Row(start, BrowserPreferenceTimeline.BrowserGroup, "Caret browsing").Value, StringComparison.Ordinal);
        Assert.Equal("100% (no default set)", Row(start, BrowserPreferenceTimeline.BrowserGroup, "Default zoom").Value);

        var later = timeline.RowsAt(5 * Second);
        var focus = Row(later, BrowserPreferenceTimeline.BrowserGroup, "Focus highlight");
        Assert.Equal("on", focus.Value);
        Assert.Equal(3 * Second, focus.SetAt);
        Assert.Equal("24 px", Row(later, BrowserPreferenceTimeline.BrowserGroup, "Font size").Value);
        Assert.True(Row(later, BrowserPreferenceTimeline.BrowserGroup, "Font size").Changed);
        var zoom = Row(later, BrowserPreferenceTimeline.BrowserGroup, "Default zoom");
        Assert.Equal("150%", zoom.Value);
        Assert.Equal(4 * Second, zoom.SetAt);
    }

    [Fact]
    public void TheNumberOfRowsIsTheSameAtEveryTime()
    {
        var timeline = Recording();
        var counts = new[] { 0L, Second, 2 * Second, 6 * Second, 9 * Second }
            .Select(time => timeline.RowsAt(time).Count)
            .Distinct();
        Assert.Single(counts);
    }

    [Fact]
    public void TheChangeTimesAreThoseOfTheRowsPreferenceOnly()
    {
        var timeline = Recording();
        Assert.Equal([3 * Second], timeline.ChangeTimesOf(BrowserPreferenceTimeline.BrowserKeyPrefix + "focusHighlight"));
        Assert.Equal([5 * Second], timeline.ChangeTimesOf(BrowserPreferenceTimeline.BrowserKeyPrefix + "defaultFontSize"));
        Assert.Empty(timeline.ChangeTimesOf(BrowserPreferenceTimeline.BrowserKeyPrefix + "minimumFontSize")!);
        // A host zoom change is not a change of the default.
        Assert.Equal([4 * Second], timeline.ChangeTimesOf(BrowserPreferenceTimeline.DefaultZoomKey));

        var row = Row(timeline.RowsAt(Second), BrowserPreferenceTimeline.BrowserGroup, "Focus highlight");
        var times = PropertyChangeSteps.TimesOf(row, WindowsPreferenceTimeline.Empty, MagnifierChangeTimeline.Empty, timeline);
        Assert.Equal([3 * Second], times);
        Assert.Equal(new PropertyChangePosition(0, 1, null, 3 * Second), PropertyChangeSteps.Locate(times!, Second));
    }

    [Fact]
    public void TheSentToThePageRowsFollowTheLastLoadedPage()
    {
        var timeline = Recording();
        var before = timeline.RowsAt(Second);
        Assert.Equal("no page loaded yet", Row(before, BrowserPreferenceTimeline.PageGroup, "Page").Value);
        Assert.Equal("not sent yet", Row(before, BrowserPreferenceTimeline.PageGroup, "prefers-color-scheme").Value);

        var first = timeline.RowsAt(3 * Second);
        Assert.Equal("https://example.org/", Row(first, BrowserPreferenceTimeline.PageGroup, "Page").Value);
        Assert.Equal("light", Row(first, BrowserPreferenceTimeline.PageGroup, "prefers-color-scheme").Value);
        Assert.Equal("20 px", Row(first, BrowserPreferenceTimeline.PageGroup, "Font size").Value);

        var dark = timeline.RowsAt(6 * Second);
        var scheme = Row(dark, BrowserPreferenceTimeline.PageGroup, "prefers-color-scheme");
        Assert.Equal("dark", scheme.Value);
        Assert.Equal(6 * Second, scheme.SetAt);
        // A later send holds only what changed; the rest stays as first sent.
        Assert.Equal("530 ms", Row(dark, BrowserPreferenceTimeline.PageGroup, "Caret blink interval").Value);

        var next = timeline.RowsAt(8 * Second);
        Assert.Equal("https://example.com/", Row(next, BrowserPreferenceTimeline.PageGroup, "Page").Value);
        Assert.Equal("light", Row(next, BrowserPreferenceTimeline.PageGroup, "prefers-color-scheme").Value);
        Assert.Null(timeline.ChangeTimesOf(BrowserPreferenceTimeline.PageKeyPrefix + "preferredColorScheme"));
    }

    [Fact]
    public void APageCommitIsACommittedCrossDocumentPrimaryMainFrameNavigation()
    {
        JsonElement Navigation(Action<JsonObject>? change = null)
        {
            var payload = new JsonObject
            {
                ["context"] = new JsonObject { ["pageId"] = "frame-12", ["frameId"] = "frame-12" },
                ["frameType"] = "primary-main-frame",
                ["primaryPage"] = true,
                ["sameDocument"] = false,
                ["committed"] = true,
                ["url"] = "https://example.org/"
            };
            change?.Invoke(payload);
            return JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone();
        }

        Assert.Equal(
            new BrowserPageCommit(Second, 12, "https://example.org/"),
            SessionPlaybackArchiveBuilder.PageCommitOf(Second, Navigation()));
        Assert.Null(SessionPlaybackArchiveBuilder.PageCommitOf(Second, Navigation(item => item["sameDocument"] = true)));
        Assert.Null(SessionPlaybackArchiveBuilder.PageCommitOf(Second, Navigation(item => item["committed"] = false)));
        Assert.Null(SessionPlaybackArchiveBuilder.PageCommitOf(Second, Navigation(item => item["frameType"] = "subframe")));
        Assert.Null(SessionPlaybackArchiveBuilder.PageCommitOf(
            Second, Navigation(item => item["context"]!["pageId"] = "page-1")));
    }

    [Fact]
    public void TheSummariesNameTheChange()
    {
        string Summary(string eventType, JsonNode payload) =>
            SessionPlaybackArchiveBuilder.CreateSummary(
                BrowserPreferenceSettings.Channel,
                eventType,
                JsonDocument.Parse(payload.ToJsonString()).RootElement);

        Assert.Equal(
            "browser-preference-changed: focusHighlight, on",
            Summary(BrowserPreferenceSettings.ChangeEventType, Change("focusHighlight", true)));
        Assert.Equal(
            "zoom-level-changed: default, 150%",
            Summary(BrowserPreferenceSettings.ZoomEventType, DefaultZoom(150)));
        Assert.Equal(
            "web-preferences-sent: web-preferences, preferredColorScheme inForcedColors",
            Summary(BrowserPreferenceSettings.SentEventType, Sample(BrowserPreferenceSettings.SentEventType, 1)));
    }

    [Fact]
    public void TheIndexKeepsTheRecordsWholeAndPlaybackReadsThem()
    {
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var snapshot = Sample(BrowserPreferenceSettings.SnapshotEventType);
        var key = 1L;
        foreach (var (time, eventType, payload) in new[]
        {
            (Second, BrowserPreferenceSettings.SnapshotEventType, (JsonNode)snapshot),
            (3 * Second, BrowserPreferenceSettings.ChangeEventType, Change("focusHighlight", true))
        })
        {
            builder.Add(key, new RecorderEvent
            {
                SchemaVersion = RecorderEvent.CurrentSchemaVersion,
                EventId = $"e{key}",
                EvidenceClass = EvidenceClasses.Observed,
                SessionId = "s",
                CollectorType = "browser",
                CollectorInstanceId = "i",
                ProducerVersion = "p",
                Channel = BrowserPreferenceSettings.Channel,
                CaptureMethod = "m",
                Sequence = (ulong)key,
                MonotonicNanoseconds = time,
                ClockMappingId = "k",
                ObservedUtc = DateTimeOffset.UnixEpoch,
                EventType = eventType,
                Payload = JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone(),
            });
            key++;
        }

        var index = builder.Build();
        Assert.Equal(8, PlaybackIndex.CurrentVersion);
        var kept = index.Events.Where(item => item.Channel == BrowserPreferenceSettings.Channel).ToList();
        Assert.Equal(2, kept.Count);
        Assert.Equal(snapshot.ToJsonString(), JsonNode.Parse(kept[0].Payload.GetRawText())!.ToJsonString());

        var archiveBuilder = new SessionPlaybackArchiveBuilder(Path.GetTempPath(), retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(4), 4 * Second, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, true, 5, true, true),
            [], [], 2, 0, null));
        Assert.True(archive.BrowserPreferences.Recorded);
        Assert.Equal("off (default)", Row(archive.BrowserPreferences.RowsAt(2 * Second), BrowserPreferenceTimeline.BrowserGroup, "Focus highlight").Value);
        Assert.Equal("on", Row(archive.BrowserPreferences.RowsAt(3 * Second), BrowserPreferenceTimeline.BrowserGroup, "Focus highlight").Value);
    }
}

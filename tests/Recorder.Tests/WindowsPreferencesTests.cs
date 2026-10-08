using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Windowing;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// The Windows settings records, their payloads, and the properties panel's
/// rows. See docs/architecture/accessibility-preferences.md.
/// </summary>
public sealed class WindowsPreferencesTests
{
    private const long Second = 1_000_000_000;

    private static IReadOnlyDictionary<string, WindowsPreferenceReading> Readings(
        Action<Dictionary<string, WindowsPreferenceReading>>? change = null)
    {
        var readings = new Dictionary<string, WindowsPreferenceReading>(StringComparer.Ordinal);
        foreach (var setting in WindowsPreferenceSettings.All)
        {
            readings[setting.Name] = WindowsPreferenceReading.Read(setting.Kind switch
            {
                WindowsPreferenceKind.Boolean => JsonValue.Create(false),
                WindowsPreferenceKind.Integer => JsonValue.Create(1L),
                WindowsPreferenceKind.Number => JsonValue.Create(1.0),
                WindowsPreferenceKind.Text => JsonValue.Create("none"),
                _ => new JsonArray(new JsonObject
                {
                    ["deviceName"] = @"\\.\DISPLAY1",
                    ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = 1920, ["height"] = 1080 },
                    ["isPrimary"] = true,
                    ["dpiX"] = 96,
                    ["dpiY"] = 96
                })
            });
        }

        change?.Invoke(readings);
        return readings;
    }

    private static List<EventValidationIssue> Validate(string eventType, JsonNode payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(WindowsPreferenceSettings.Channel, eventType, document.RootElement, 100, issues);
        return issues;
    }

    private static JsonObject Snapshot(Action<Dictionary<string, WindowsPreferenceReading>>? change = null) =>
        WindowsPreferencePayloads.Snapshot(
            "start",
            Readings(change),
            WindowsPreferenceSettings.UiSettingsEvents.ToDictionary(name => name, _ => true));

    private static JsonObject Change(string setting, JsonNode? before, JsonNode? after) =>
        WindowsPreferencePayloads.Change(
            setting,
            WindowsPreferenceReading.Read(before),
            WindowsPreferenceReading.Read(after),
            new WindowsPreferenceNotice("setting-change", 0x1043, null, null));

    [Fact]
    public void TheBuiltPayloadsAreValid()
    {
        Assert.Empty(Validate(WindowsPreferenceSettings.SnapshotEventType, Snapshot()));
        Assert.Empty(Validate(
            WindowsPreferenceSettings.SnapshotEventType,
            Snapshot(readings => readings["accentColor"] = WindowsPreferenceReading.Failed("The key does not exist."))));
        Assert.Empty(Validate(
            WindowsPreferenceSettings.ChangeEventType,
            Change("menuAnimation", JsonValue.Create(true), JsonValue.Create(false))));
    }

    [Theory]
    [InlineData("notASetting")]
    [InlineData("caretBlinkTime")]
    public void AChangeOfAnUnknownSettingOrOfTheCaretBlinkTimeIsRefused(string setting)
    {
        var issues = Validate(
            WindowsPreferenceSettings.ChangeEventType,
            Change(setting, JsonValue.Create(1L), JsonValue.Create(2L)));
        Assert.Contains(issues, issue => issue.Code == "windows-preference-unknown");
    }

    [Fact]
    public void AValueOfTheWrongTypeIsRefused()
    {
        Assert.NotEmpty(Validate(
            WindowsPreferenceSettings.ChangeEventType,
            Change("menuAnimation", JsonValue.Create(true), JsonValue.Create("off"))));
        Assert.NotEmpty(Validate(
            WindowsPreferenceSettings.SnapshotEventType,
            Snapshot(readings => readings["textScaleFactor"] = WindowsPreferenceReading.Read(JsonValue.Create("1.25")))));
    }

    [Fact]
    public void AReadingWithAValueAndAProblemOrNeitherIsRefused()
    {
        var both = Snapshot(readings => readings["menuFade"] = new WindowsPreferenceReading(JsonValue.Create(true), "failed"));
        Assert.Contains(
            Validate(WindowsPreferenceSettings.SnapshotEventType, both),
            issue => issue.Code == "windows-preference-reading-inconsistent");
        var neither = Snapshot(readings => readings["menuFade"] = new WindowsPreferenceReading(null, null));
        Assert.Contains(
            Validate(WindowsPreferenceSettings.SnapshotEventType, neither),
            issue => issue.Code == "windows-preference-reading-inconsistent");

        // A contrast theme with no name is read as a null name.
        var noName = Snapshot(readings => readings["highContrastScheme"] = new WindowsPreferenceReading(null, null));
        Assert.Empty(Validate(WindowsPreferenceSettings.SnapshotEventType, noName));
    }

    [Fact]
    public void AMissingOrExtraSettingIsRefused()
    {
        var missing = Snapshot();
        ((JsonObject)missing["settings"]!).Remove("mouseKeys");
        Assert.NotEmpty(Validate(WindowsPreferenceSettings.SnapshotEventType, missing));

        var extra = Change("menuFade", JsonValue.Create(true), JsonValue.Create(false));
        ((JsonObject)extra["current"]!)["menuAnimation"] = new JsonObject { ["value"] = true, ["problem"] = null };
        Assert.NotEmpty(Validate(WindowsPreferenceSettings.ChangeEventType, extra));
    }

    [Fact]
    public void OnlyDifferingReadingsAreChangesAndNeverTheCaretBlinkTime()
    {
        var before = Readings();
        Assert.Empty(WindowsPreferencePayloads.Changed(before, Readings()));

        var after = Readings(readings =>
        {
            readings["caretBlinkTime"] = WindowsPreferenceReading.Read(JsonValue.Create(250L));
            readings["textScaleFactor"] = WindowsPreferenceReading.Read(JsonValue.Create(1.5));
            readings["stickyKeys"] = WindowsPreferenceReading.Failed("SystemParametersInfoW failed with 5.");
            ((JsonArray)readings["monitors"].Value!)[0]!["dpiX"] = 144;
        });
        Assert.Equal(
            ["monitors", "textScaleFactor", "stickyKeys"],
            WindowsPreferencePayloads.Changed(before, after));
    }

    private static WindowsPreferenceRecord Record(long time, string eventType, JsonNode payload) =>
        new(time, eventType, JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone());

    private static PropertyRow Row(IReadOnlyList<PropertyRow> rows, string setting) =>
        Assert.Single(rows, row => row.Setting == setting);

    [Fact]
    public void ThePanelShowsTheValuesInEffectAndWhenEachWasSet()
    {
        var timeline = new WindowsPreferenceTimeline(
        [
            Record(2 * Second, WindowsPreferenceSettings.ChangeEventType,
                WindowsPreferencePayloads.Change(
                    "textScaleFactor",
                    WindowsPreferenceReading.Read(JsonValue.Create(1.0)),
                    WindowsPreferenceReading.Read(JsonValue.Create(1.25)),
                    new WindowsPreferenceNotice("ui-settings", Source: "textScaleFactorChanged"))),
            Record(Second / 10, WindowsPreferenceSettings.SnapshotEventType, Snapshot(readings =>
                readings["accentColor"] = WindowsPreferenceReading.Failed("The key does not exist.")))
        ]);
        Assert.True(timeline.Recorded);

        var before = timeline.RowsAt(Second, null);
        Assert.Equal("100 percent", Row(before, "Text size").Value);
        Assert.Equal("at start", Row(before, "Text size").WhenSet);
        Assert.Null(Row(before, "Text size").SetAt);
        Assert.Equal("not read: The key does not exist.", Row(before, "Accent color").Value);
        Assert.Equal("at start, read once", Row(before, "Caret blink time").WhenSet);
        Assert.Equal("off", Row(before, "Sticky keys").Value);
        Assert.Equal(@"\\.\DISPLAY1: 1920 by 1080, 100 percent (96 DPI), primary", Row(before, "Monitors and scale").Value);

        var at = Row(timeline.RowsAt(2 * Second + Second / 2, null), "Text size");
        Assert.Equal("125 percent", at.Value);
        Assert.Equal("00:00:02.000", at.WhenSet);
        Assert.Equal(2 * Second, at.SetAt);
        Assert.True(at.Changed);
        Assert.Equal("Text size, 125 percent, 00:00:02.000, changed", at.Spoken);

        var later = Row(timeline.RowsAt(4 * Second, null), "Text size");
        Assert.False(later.Changed);
        Assert.Equal("Text size, 125 percent, 00:00:02.000", later.Spoken);
    }

    [Fact]
    public void AnOlderRecordingShowsEachSettingAsNotRecorded()
    {
        var rows = WindowsPreferenceTimeline.Empty.RowsAt(Second, null);
        Assert.False(WindowsPreferenceTimeline.Empty.Recorded);
        Assert.All(
            rows.Where(row => row.Group != WindowsPreferenceTimeline.MagnifierGroup),
            row => Assert.Equal("not recorded", row.Value));
        Assert.Equal(WindowsPreferenceSettings.All.Count + 3, rows.Count);
    }

    [Fact]
    public void TheMagnifierRowsComeFromTheFrameShown()
    {
        double[] inversion = [-1, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1];
        var frame = new SessionVideoFrame(
            Second, "f.png", "/f.png", 1920, 1080,
            Magnification: new FullscreenMagnification(4, 480, 270),
            ColorEffect: new FullscreenColorEffect(inversion));
        var rows = WindowsPreferenceTimeline.MagnifierRows(frame);
        Assert.Equal("400 percent", Row(rows, "Full screen level").Value);
        Assert.Equal("480, 270", Row(rows, "Full screen position").Value);
        Assert.Equal("inverted colors", Row(rows, "Full screen color effect").Value);
        Assert.All(rows, row => Assert.Equal("with this frame", row.WhenSet));

        var plain = WindowsPreferenceTimeline.MagnifierRows(new SessionVideoFrame(Second, "f.png", "/f.png", 1920, 1080));
        Assert.Equal("not recorded with this frame", Row(plain, "Full screen level").Value);
    }

    [Fact]
    public void ThePlaybackIndexKeepsTheSettingsRecordsWholeForTheArchive()
    {
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        var snapshot = Snapshot();
        var change = Change("menuFade", JsonValue.Create(false), JsonValue.Create(true));
        var key = 1L;
        foreach (var (time, eventType, payload) in new[]
        {
            (Second, WindowsPreferenceSettings.SnapshotEventType, (JsonNode)snapshot),
            (3 * Second, WindowsPreferenceSettings.ChangeEventType, change)
        })
        {
            builder.Add(key, new RecorderEvent
            {
                SchemaVersion = RecorderEvent.CurrentSchemaVersion,
                EventId = $"e{key}",
                EvidenceClass = EvidenceClasses.Observed,
                SessionId = "s",
                CollectorType = "windows.preferences",
                CollectorInstanceId = "i",
                ProducerVersion = "p",
                Channel = WindowsPreferenceSettings.Channel,
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
        Assert.Equal(7, PlaybackIndex.CurrentVersion);
        var kept = index.Events.Where(item => item.Channel == WindowsPreferenceSettings.Channel).ToList();
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
        Assert.True(archive.WindowsPreferences.Recorded);
        Assert.Equal("off", Row(archive.WindowsPreferences.RowsAt(2 * Second, null), "Menu fade").Value);
        Assert.Equal("on", Row(archive.WindowsPreferences.RowsAt(3 * Second, null), "Menu fade").Value);
        Assert.Equal(
            "windows-preference-changed: menuFade, on",
            SessionPlaybackArchiveBuilder.CreateSummary(
                WindowsPreferenceSettings.Channel,
                WindowsPreferenceSettings.ChangeEventType,
                JsonDocument.Parse(change.ToJsonString()).RootElement));
    }
}

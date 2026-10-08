using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>A <c>browser.preferences</c> record of a recording, as playback keeps it.</summary>
public sealed record BrowserPreferenceRecord(long MonotonicNanoseconds, string EventType, JsonElement Payload);

/// <summary>
/// A committed, cross-document navigation of a primary main frame: the page
/// it loaded, by the frame tree node id the navigation records name it by,
/// and its address.
/// </summary>
public sealed record BrowserPageCommit(long MonotonicNanoseconds, int PageFrameTreeNodeId, string Url);

/// <summary>
/// The browser preferences in effect at each time of a recording, and the
/// preferences last sent to the page shown, for the properties panel's
/// "Browser" and "Sent to the page" groups. See
/// docs/architecture/accessibility-preferences.md, "Stage 2".
/// </summary>
public sealed class BrowserPreferenceTimeline
{
    public const string BrowserGroup = "Browser";
    public const string PageGroup = "Sent to the page";
    public const string BrowserKeyPrefix = "browser.";
    public const string PageKeyPrefix = "page.";
    public const string DefaultZoomKey = BrowserKeyPrefix + "defaultZoom";
    public const string PageKey = PageKeyPrefix + "address";
    private const long ChangedWindowNanoseconds = 1_000_000_000;

    // ThemeService::BrowserColorScheme,
    // chrome/browser/themes/theme_service.h: kSystem 0, kLight 1, kDark 2.
    private static readonly string[] ColorSchemes = ["system", "light", "dark"];

    private readonly Dictionary<string, JsonElement> _start = new(StringComparer.Ordinal);
    private readonly List<(long Time, string Preference, JsonElement Reading)> _changes = [];
    private readonly List<(long Time, double Percent)> _defaultZoom = [];
    private readonly Dictionary<int, List<(long Time, JsonElement Fields)>> _sent = [];
    private readonly List<BrowserPageCommit> _commits;

    public BrowserPreferenceTimeline(
        IEnumerable<BrowserPreferenceRecord> records,
        IEnumerable<BrowserPageCommit>? commits = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ordered = records
            .Where(record => record.Payload.ValueKind == JsonValueKind.Object)
            .OrderBy(record => record.MonotonicNanoseconds)
            .ToList();
        HasRecords = ordered.Count > 0;
        var start = ordered.FirstOrDefault(record =>
            record.EventType == BrowserPreferenceSettings.SnapshotEventType &&
            record.Payload.TryGetProperty("preferences", out var preferences) &&
            preferences.ValueKind == JsonValueKind.Object);
        if (start is not null)
        {
            StartTime = start.MonotonicNanoseconds;
            foreach (var preference in start.Payload.GetProperty("preferences").EnumerateObject())
            {
                _start[preference.Name] = preference.Value.Clone();
            }
        }

        foreach (var record in ordered)
        {
            var payload = record.Payload;
            switch (record.EventType)
            {
                case BrowserPreferenceSettings.ChangeEventType
                    when payload.TryGetProperty("preference", out var name) && name.ValueKind == JsonValueKind.String &&
                        payload.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object &&
                        current.TryGetProperty(name.GetString()!, out var reading):
                    _changes.Add((record.MonotonicNanoseconds, name.GetString()!, reading.Clone()));
                    break;
                case BrowserPreferenceSettings.ZoomEventType
                    when payload.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String &&
                        mode.GetString() == "default" &&
                        payload.TryGetProperty("zoomPercent", out var percent) && percent.TryGetDouble(out var value):
                    _defaultZoom.Add((record.MonotonicNanoseconds, value));
                    break;
                case BrowserPreferenceSettings.SentEventType
                    when payload.TryGetProperty("pageFrameTreeNodeId", out var page) && page.TryGetInt32(out var pageId) &&
                        payload.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object:
                    if (!_sent.TryGetValue(pageId, out var sends))
                    {
                        sends = [];
                        _sent[pageId] = sends;
                    }

                    sends.Add((record.MonotonicNanoseconds, fields.Clone()));
                    break;
            }
        }

        _commits = [.. (commits ?? []).OrderBy(commit => commit.MonotonicNanoseconds)];
    }

    public static BrowserPreferenceTimeline Empty { get; } = new([]);

    /// <summary>Whether the recording holds any browser preference record.</summary>
    public bool HasRecords { get; }

    /// <summary>Whether the recording holds the browser preferences at the profile's load.</summary>
    public bool Recorded => _start.Count > 0;

    /// <summary>The time of the record of the preferences at the profile's load, or null.</summary>
    public long? StartTime { get; }

    /// <summary>
    /// The recording times of one Browser row's changes, in order, without
    /// repeats, or null for a row the recording has no records of: any row
    /// of a recording without browser preference records, and every
    /// "Sent to the page" row, which has no change buttons.
    /// </summary>
    public IReadOnlyList<long>? ChangeTimesOf(string key)
    {
        if (!HasRecords || !key.StartsWith(BrowserKeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (key == DefaultZoomKey)
        {
            return [.. _defaultZoom.Select(change => change.Time).Distinct()];
        }

        var name = key[BrowserKeyPrefix.Length..];
        return Recorded
            ? [.. _changes.Where(change => change.Preference == name).Select(change => change.Time).Distinct()]
            : null;
    }

    /// <summary>
    /// The rows of the Browser and Sent to the page groups at a time. A
    /// recording without browser preference records has one row saying so.
    /// The number of rows is the same at every time of a recording.
    /// </summary>
    public IReadOnlyList<PropertyRow> RowsAt(long time)
    {
        if (!HasRecords)
        {
            return [new PropertyRow(BrowserGroup, "Browser preferences", "not recorded", string.Empty, null, false, BrowserKeyPrefix + "none")];
        }

        var rows = new List<PropertyRow>();
        foreach (var setting in BrowserPreferenceSettings.Browser)
        {
            var key = BrowserKeyPrefix + setting.Name;
            if (!Recorded)
            {
                rows.Add(new PropertyRow(BrowserGroup, setting.Label, "not recorded", string.Empty, null, false, key));
                continue;
            }

            JsonElement? reading = _start.TryGetValue(setting.Name, out var startReading) ? startReading : null;
            long? setAt = null;
            foreach (var change in _changes)
            {
                if (change.Time > time)
                {
                    break;
                }

                if (change.Preference == setting.Name)
                {
                    reading = change.Reading;
                    setAt = change.Time;
                }
            }

            rows.Add(new PropertyRow(
                BrowserGroup,
                setting.Label,
                reading is { } value ? DescribeReading(setting, value) : "not recorded",
                setAt is { } at ? Clock(at) : "at start",
                setAt,
                IsChanged(setAt, time),
                key));
        }

        long? zoomAt = null;
        double? zoom = null;
        foreach (var change in _defaultZoom)
        {
            if (change.Time > time)
            {
                break;
            }

            zoomAt = change.Time;
            zoom = change.Percent;
        }

        // The zoom map starts at 100 percent and records each change of its
        // default, including the profile's own default as it is applied.
        rows.Add(new PropertyRow(
            BrowserGroup,
            "Default zoom",
            zoom is { } percent ? Percent(percent) : "100% (no default set)",
            zoomAt is { } setZoom ? Clock(setZoom) : "at start",
            zoomAt,
            IsChanged(zoomAt, time),
            DefaultZoomKey));

        rows.AddRange(PageRowsAt(time));
        return rows;
    }

    // The page of the last committed primary main frame navigation at or
    // before the time, with the fields last sent to it. With more than one
    // tab this is the tab last navigated.
    private IEnumerable<PropertyRow> PageRowsAt(long time)
    {
        var commit = _commits.LastOrDefault(item => item.MonotonicNanoseconds <= time);
        yield return commit is null
            ? new PropertyRow(PageGroup, "Page", "no page loaded yet", string.Empty, null, false, PageKey)
            : new PropertyRow(PageGroup, "Page", commit.Url, Clock(commit.MonotonicNanoseconds), commit.MonotonicNanoseconds, false, PageKey);
        var sends = commit is not null && _sent.TryGetValue(commit.PageFrameTreeNodeId, out var found) ? found : null;
        foreach (var setting in BrowserPreferenceSettings.Page)
        {
            JsonElement? value = null;
            long? setAt = null;
            if (sends is not null)
            {
                foreach (var send in sends)
                {
                    if (send.Time > time)
                    {
                        break;
                    }

                    if (send.Fields.TryGetProperty(setting.Name, out var field))
                    {
                        value = field;
                        setAt = send.Time;
                    }
                }
            }

            yield return new PropertyRow(
                PageGroup,
                setting.Label,
                value is { } sent ? DescribeValue(setting, sent) : "not sent yet",
                setAt is { } at ? Clock(at) : string.Empty,
                setAt,
                IsChanged(setAt, time),
                PageKeyPrefix + setting.Name);
        }
    }

    private static bool IsChanged(long? setAt, long time) =>
        setAt is { } at && time - at < ChangedWindowNanoseconds;

    /// <summary>
    /// A browser preference's reading as the panel shows it: its value, with
    /// "(default)" when it is the default, or that it could not be read.
    /// </summary>
    public static string DescribeReading(BrowserPreferenceSetting setting, JsonElement reading)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (reading.ValueKind != JsonValueKind.Object)
        {
            return "not recorded";
        }

        if (reading.TryGetProperty("problem", out var problem) && problem.ValueKind == JsonValueKind.String)
        {
            return $"not read: {problem.GetString()}";
        }

        if (!reading.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return "not recorded";
        }

        var shown = DescribeValue(setting, value);
        return reading.TryGetProperty("isDefault", out var isDefault) && isDefault.ValueKind == JsonValueKind.True
            ? $"{shown} (default)"
            : shown;
    }

    /// <summary>A recorded value as the panel shows it.</summary>
    public static string DescribeValue(BrowserPreferenceSetting setting, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(setting);
        switch (setting.Kind)
        {
            case BrowserPreferenceKind.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return value.GetBoolean() ? "on" : "off";
            case BrowserPreferenceKind.Integer when value.TryGetInt64(out var number):
                if (setting.Name == "colorScheme")
                {
                    return number >= 0 && number < ColorSchemes.Length
                        ? ColorSchemes[number]
                        : $"value {number.ToString(CultureInfo.InvariantCulture)}";
                }

                if (setting.Name == "requestedPageColors")
                {
                    return $"value {number.ToString(CultureInfo.InvariantCulture)}";
                }

                return setting.Name.EndsWith("FontSize", StringComparison.Ordinal)
                    ? $"{number.ToString(CultureInfo.InvariantCulture)} px"
                    : number.ToString(CultureInfo.InvariantCulture);
            case BrowserPreferenceKind.Number when value.TryGetDouble(out var real):
                return setting.Name == "caretBlinkIntervalMilliseconds"
                    ? $"{real.ToString("0.###", CultureInfo.InvariantCulture)} ms"
                    : real.ToString("0.###", CultureInfo.InvariantCulture);
            case BrowserPreferenceKind.Text when value.ValueKind == JsonValueKind.String:
                return value.GetString() is { Length: > 0 } text ? text : "none";
            case BrowserPreferenceKind.TextList when value.ValueKind == JsonValueKind.Array:
                var items = value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .ToList();
                return items.Count == 0 ? "none" : string.Join(", ", items);
            default:
                return value.GetRawText();
        }
    }

    private static string Percent(double percent) =>
        $"{percent.ToString("0.#", CultureInfo.InvariantCulture)}%";

    private static string Clock(long nanoseconds) => WindowsPreferenceTimeline.Clock(nanoseconds);
}

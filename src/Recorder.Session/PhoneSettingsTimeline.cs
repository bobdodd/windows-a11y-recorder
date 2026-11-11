using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// The phone's settings in effect at each time of a recording, from its
/// <c>android-settings</c> and <c>android-setting-changed</c> records
/// (Android slice 8, step 8a), and their rows in the properties panel. In a
/// recording of a phone they take the place of the Magnifier and Windows
/// settings rows, which a phone has none of.
/// </summary>
public sealed class PhoneSettingsTimeline
{
    /// <summary>The start of each row's key, before the setting's id (namespace/name).</summary>
    public const string KeyPrefix = "android-setting:";

    private const long ChangedWindowNanoseconds = 1_000_000_000;

    private readonly Dictionary<string, string?> _start = new(StringComparer.Ordinal);
    private readonly List<(long Time, string Setting, string? Value, bool FoundAtStop)> _changes = [];

    public PhoneSettingsTimeline(IEnumerable<(long Time, string EventType, JsonElement Payload)> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ordered = records.OrderBy(record => record.Time).ToList();
        // The first snapshot gives the values at the start.
        foreach (var record in ordered)
        {
            if (record.EventType == AndroidSettingsRecords.SnapshotEventType &&
                record.Payload.ValueKind == JsonValueKind.Object &&
                record.Payload.TryGetProperty("settings", out var settings) &&
                settings.ValueKind == JsonValueKind.Object)
            {
                foreach (var setting in settings.EnumerateObject())
                {
                    _start[setting.Name] = setting.Value.ValueKind == JsonValueKind.String ? setting.Value.GetString() : null;
                }

                Recorded = true;
                break;
            }
        }

        foreach (var record in ordered)
        {
            if (record.EventType == AndroidSettingsRecords.ChangeEventType &&
                record.Payload.ValueKind == JsonValueKind.Object &&
                record.Payload.TryGetProperty("setting", out var name) && name.ValueKind == JsonValueKind.String &&
                record.Payload.TryGetProperty("value", out var value))
            {
                var foundAtStop = record.Payload.TryGetProperty("source", out var source) &&
                    source.ValueKind == JsonValueKind.String &&
                    source.GetString() == AndroidSettingsRecords.StopSource;
                _changes.Add((
                    record.Time,
                    name.GetString()!,
                    value.ValueKind == JsonValueKind.String ? value.GetString() : null,
                    foundAtStop));
            }
        }
    }

    public static PhoneSettingsTimeline Empty { get; } = new([]);

    /// <summary>Whether the recording holds the phone's settings at its start.</summary>
    public bool Recorded { get; }

    /// <summary>The recording times of one row's changes, in order, without repeats; null for a row that is not a phone setting's.</summary>
    public IReadOnlyList<long>? ChangeTimesOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Recorded || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var id = key[KeyPrefix.Length..];
        return [.. _changes.Where(change => change.Setting == id).Select(change => change.Time).Distinct()];
    }

    /// <summary>The value of a setting at a time, as Android printed it; null when not set or not recorded.</summary>
    public string? ValueAt(string id, long time)
    {
        ArgumentNullException.ThrowIfNull(id);
        var value = _start.GetValueOrDefault(id);
        foreach (var change in _changes)
        {
            if (change.Time > time)
            {
                break;
            }

            if (change.Setting == id)
            {
                value = change.Value;
            }
        }

        return value;
    }

    /// <summary>
    /// The rows of the panel at a time, a row for each setting in
    /// <see cref="AndroidSettingsRecords.All"/>, in its order; none for a
    /// recording without the phone's settings.
    /// </summary>
    public IReadOnlyList<PropertyRow> RowsAt(long time)
    {
        if (!Recorded)
        {
            return [];
        }

        var rows = new List<PropertyRow>(AndroidSettingsRecords.All.Count);
        foreach (var setting in AndroidSettingsRecords.All)
        {
            var key = KeyPrefix + setting.Id;
            if (!_start.TryGetValue(setting.Id, out var value))
            {
                // Its table could not be read at the start; its record says why.
                rows.Add(new PropertyRow(setting.Group, setting.Label, "not read", string.Empty, null, false, key));
                continue;
            }

            long? setAt = null;
            var foundAtStop = false;
            foreach (var change in _changes)
            {
                if (change.Time > time)
                {
                    break;
                }

                if (change.Setting == setting.Id)
                {
                    value = change.Value;
                    setAt = change.Time;
                    foundAtStop = change.FoundAtStop;
                }
            }

            var whenSet = setAt is { } at
                ? foundAtStop ? $"{WindowsPreferenceTimeline.Clock(at)}, found at the stop" : WindowsPreferenceTimeline.Clock(at)
                : "at start";
            rows.Add(new PropertyRow(
                setting.Group,
                setting.Label,
                AndroidSettingValues.Describe(setting, value),
                whenSet,
                setAt,
                setAt is { } changedAt && time - changedAt < ChangedWindowNanoseconds,
                key));
        }

        return rows;
    }
}

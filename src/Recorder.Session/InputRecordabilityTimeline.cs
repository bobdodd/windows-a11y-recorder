using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>A record of when input could not be recorded, as playback keeps it.</summary>
public sealed record InputRecordabilityRecord(long MonotonicNanoseconds, string EventType, JsonElement Payload);

/// <summary>A period when keyboard and mouse input could not be recorded, and why.</summary>
public sealed record UnrecordablePeriod(long Start, long End, string Reason);

/// <summary>
/// When keyboard and mouse input could not be recorded, from the records on
/// <c>window.foreground</c>: while the foreground window's process was at a
/// higher integrity level than the recorder's, or the desktop receiving
/// input was not the user's. For the timeline's band over the keyboard and
/// mouse lanes and the properties panel's Keyboard and mouse row. A window
/// whose level could not be read is shown as unknown, not as a gap. Empty
/// for a recording made before the records. See
/// docs/architecture/screen-reader-activity.md, "Input the recorder cannot
/// receive".
/// </summary>
public sealed class InputRecordabilityTimeline
{
    public const string Group = "Keyboard and mouse";
    public const string Key = "input.recordable";
    private const long ChangedWindowNanoseconds = 1_000_000_000;

    private readonly List<(long Time, string Value, bool? Recordable)> _states = [];

    public InputRecordabilityTimeline(IEnumerable<InputRecordabilityRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        (bool? Recordable, string Why)? window = null;
        (bool Recordable, string Why)? desktop = null;
        foreach (var record in records.OrderBy(record => record.MonotonicNanoseconds))
        {
            var payload = record.Payload;
            if (payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            switch (record.EventType)
            {
                case InputRecordabilityRecords.RecorderIntegrityEventType:
                    Recorded = true;
                    RecorderLevel = Text(payload, "integrityLevel");
                    continue;
                case InputRecordabilityRecords.ForegroundIntegrityEventType:
                    Recorded = true;
                    var name = Text(payload, "processName") ?? "process " + Integer(payload, "processId");
                    window = Boolean(payload, "inputRecordable") switch
                    {
                        true => (true, string.Empty),
                        false => (false, $"{Text(payload, "integrityLevel")} integrity window, {name}"),
                        null => (null, $"integrity of {name} not readable: {Text(payload, "problem")}")
                    };
                    break;
                case InputRecordabilityRecords.InputDesktopEventType:
                    Recorded = true;
                    desktop = Boolean(payload, "inputRecordable") == true
                        ? (true, string.Empty)
                        : (false, Text(payload, "desktopName") is { } desktopName
                            ? $"desktop {desktopName}"
                            : "a desktop the recorder cannot open, such as the secure desktop");
                    break;
                default:
                    continue;
            }

            // The desktop comes first: on another desktop the foreground
            // window does not matter.
            var (value, recordable) = desktop is { Recordable: false } other
                ? ("not recordable: " + other.Why, (bool?)false)
                : window switch
                {
                    { Recordable: false } elevated => ("not recordable: " + elevated.Why, false),
                    { Recordable: null } unknown => ("unknown: " + unknown.Why, (bool?)null),
                    _ => ("recordable", true)
                };
            if (_states.Count == 0 || _states[^1].Value != value)
            {
                _states.Add((record.MonotonicNanoseconds, value, recordable));
            }
        }

        var periods = new List<UnrecordablePeriod>();
        for (var i = 0; i < _states.Count; i++)
        {
            if (_states[i].Recordable == false)
            {
                var end = i + 1 < _states.Count ? _states[i + 1].Time : long.MaxValue;
                periods.Add(new UnrecordablePeriod(_states[i].Time, end, _states[i].Value["not recordable: ".Length..]));
            }
        }

        Unrecordable = periods;
    }

    public static InputRecordabilityTimeline Empty { get; } = new([]);

    /// <summary>Whether the recording holds the records.</summary>
    public bool Recorded { get; }

    /// <summary>The recorder's own integrity level, or null.</summary>
    public string? RecorderLevel { get; }

    /// <summary>
    /// The periods when input could not be recorded, in order; one that had
    /// not ended at the end of the recording ends at <see cref="long.MaxValue"/>.
    /// </summary>
    public IReadOnlyList<UnrecordablePeriod> Unrecordable { get; }

    /// <summary>The panel row at a time.</summary>
    public IReadOnlyList<PropertyRow> RowsAt(long time)
    {
        const string setting = "Input recordable";
        if (!Recorded)
        {
            return [new PropertyRow(Group, setting, "not recorded", string.Empty, null, false, Key)];
        }

        var value = "recordable";
        long? setAt = null;
        for (var i = 0; i < _states.Count && _states[i].Time <= time; i++)
        {
            value = _states[i].Value;
            setAt = i == 0 ? null : _states[i].Time;
        }

        return
        [
            new PropertyRow(
                Group,
                setting,
                value,
                setAt is { } at ? WindowsPreferenceTimeline.Clock(at) : "at start",
                setAt,
                setAt is { } changedAt && time - changedAt < ChangedWindowNanoseconds,
                Key)
        ];
    }

    /// <summary>The times the row's value changed, for stepping; null for another key or no records.</summary>
    public IReadOnlyList<long>? ChangeTimesOf(string key) =>
        Recorded && key == Key ? [.. _states.Skip(1).Select(state => state.Time)] : null;

    private static string? Text(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? Boolean(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.ValueKind == JsonValueKind.True
            : null;

    private static string Integer(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : "unknown";
}

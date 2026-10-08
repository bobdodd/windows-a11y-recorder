using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// The Magnifier change records of a recording (graphics.magnifier,
/// magnifier-changed): when the level, the position, or the color effect
/// was seen to change, at the time of the frame it was seen with. The values
/// themselves are read from the frames. Empty for a recording made before
/// the records. See docs/architecture/accessibility-preferences.md.
/// </summary>
public sealed class MagnifierChangeTimeline
{
    private readonly Dictionary<string, long[]> _times;

    public MagnifierChangeTimeline(IEnumerable<(long Time, JsonElement Payload)> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var times = MagnifierChanges.Parts.ToDictionary(part => part, _ => new SortedSet<long>(), StringComparer.Ordinal);
        foreach (var (time, payload) in records)
        {
            if (payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("changed", out var changed) ||
                changed.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var part in MagnifierChanges.Parts)
            {
                if (changed.TryGetProperty(part, out var flag) && flag.ValueKind == JsonValueKind.True)
                {
                    times[part].Add(time);
                }
            }
        }

        _times = times.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        Recorded = _times.Values.Any(list => list.Length > 0);
    }

    public static MagnifierChangeTimeline Empty { get; } = new([]);

    /// <summary>Whether the recording holds any Magnifier change record.</summary>
    public bool Recorded { get; }

    /// <summary>The times a part (<see cref="MagnifierChanges.Parts"/>) changed, in order.</summary>
    public IReadOnlyList<long> Times(string part) =>
        _times.TryGetValue(part, out var times) ? times : [];
}

/// <summary>
/// Moving through one property's changes from the properties panel: the
/// change in effect at a time is its last change at or before it; the
/// previous change is the one before that, and the next the first after the
/// time. Changes at the same time are one change.
/// </summary>
public static class PropertyChangeSteps
{
    /// <summary>
    /// The times of a panel row's changes, or null when the recording has no
    /// records of them: a Magnifier row of a recording without Magnifier
    /// change records, or a settings row of one without settings records.
    /// </summary>
    public static IReadOnlyList<long>? TimesOf(
        PropertyRow row,
        WindowsPreferenceTimeline settings,
        MagnifierChangeTimeline magnifier)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(magnifier);
        if (row.Key.StartsWith(WindowsPreferenceTimeline.MagnifierKeyPrefix, StringComparison.Ordinal))
        {
            return magnifier.Recorded
                ? magnifier.Times(row.Key[WindowsPreferenceTimeline.MagnifierKeyPrefix.Length..])
                : null;
        }

        return settings.Recorded ? settings.ChangeTimesOf(row.Key) : null;
    }

    /// <summary>The last of the times at or before <paramref name="time"/>, or null.</summary>
    public static long? InEffect(IReadOnlyList<long> times, long time)
    {
        ArgumentNullException.ThrowIfNull(times);
        long? found = null;
        foreach (var at in times)
        {
            if (at > time)
            {
                break;
            }

            found = at;
        }

        return found;
    }

    /// <summary>
    /// The change before the one in effect at <paramref name="time"/>, or
    /// null when none is: there is no change in effect, or it is the first.
    /// </summary>
    public static long? Previous(IReadOnlyList<long> times, long time)
    {
        if (InEffect(times, time) is not { } current)
        {
            return null;
        }

        long? found = null;
        foreach (var at in times)
        {
            if (at >= current)
            {
                break;
            }

            found = at;
        }

        return found;
    }

    /// <summary>The first change after <paramref name="time"/>, or null.</summary>
    public static long? Next(IReadOnlyList<long> times, long time)
    {
        ArgumentNullException.ThrowIfNull(times);
        foreach (var at in times)
        {
            if (at > time)
            {
                return at;
            }
        }

        return null;
    }
}

using System.Globalization;
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
/// previous change is the last strictly before the time, and the next the
/// first strictly after it. Changes at the same time are one change. See
/// docs/architecture/accessibility-preferences.md, "Change buttons and
/// counts".
/// </summary>
public static class PropertyChangeSteps
{
    /// <summary>
    /// The times of a panel row's changes, or null when the recording has no
    /// records of them: a Magnifier row of a recording without Magnifier
    /// change records, or a settings row of one without settings records.
    /// A browser row's times are those of <see cref="BrowserPreferenceTimeline.ChangeTimesOf"/>.
    /// </summary>
    public static IReadOnlyList<long>? TimesOf(
        PropertyRow row,
        WindowsPreferenceTimeline settings,
        MagnifierChangeTimeline magnifier,
        BrowserPreferenceTimeline? browser = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(magnifier);
        if (row.Key.StartsWith(BrowserPreferenceTimeline.BrowserKeyPrefix, StringComparison.Ordinal) ||
            row.Key.StartsWith(BrowserPreferenceTimeline.PageKeyPrefix, StringComparison.Ordinal))
        {
            return (browser ?? BrowserPreferenceTimeline.Empty).ChangeTimesOf(row.Key);
        }

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
    /// The last change strictly before <paramref name="time"/>, or null.
    /// Between two changes it is the change in effect, so the playhead
    /// goes back to the start of the value shown; at a change it is the
    /// one before.
    /// </summary>
    public static long? Previous(IReadOnlyList<long> times, long time)
    {
        ArgumentNullException.ThrowIfNull(times);
        long? found = null;
        foreach (var at in times)
        {
            if (at >= time)
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

    /// <summary>
    /// Where <paramref name="time"/> is among a property's changes: which
    /// change is in effect, counted from the start of the recording, of how
    /// many, and the previous and next change to move to.
    /// </summary>
    public static PropertyChangePosition Locate(IReadOnlyList<long> times, long time)
    {
        ArgumentNullException.ThrowIfNull(times);
        var count = 0;
        var inEffect = 0;
        long? last = null;
        foreach (var at in times)
        {
            if (last == at)
            {
                continue;
            }

            last = at;
            count++;
            if (at <= time)
            {
                inEffect = count;
            }
        }

        return new PropertyChangePosition(inEffect, count, Previous(times, time), Next(times, time));
    }
}

/// <summary>
/// A property row's place among its changes: <see cref="Index"/> is the
/// change in effect, counted from 1 at the start of the recording, or 0
/// before the first; <see cref="Count"/> is how many changes the recording
/// holds. The value at the start of the recording is not a change.
/// </summary>
public sealed record PropertyChangePosition(int Index, int Count, long? Previous, long? Next)
{
    /// <summary>The count as the row shows it, for example "2/4".</summary>
    public string Shown => string.Create(CultureInfo.CurrentCulture, $"{Index}/{Count}");

    /// <summary>The count as a screen reader reads it.</summary>
    public string Spoken => Index == 0
        ? Count == 1
            ? "before its only change"
            : string.Create(CultureInfo.CurrentCulture, $"before the first of {Count} changes")
        : string.Create(CultureInfo.CurrentCulture, $"change {Index} of {Count}");
}

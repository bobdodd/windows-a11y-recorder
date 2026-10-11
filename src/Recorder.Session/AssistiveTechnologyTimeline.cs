using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>A <c>system.assistive-technology</c> record of a recording, as playback keeps it.</summary>
public sealed record AssistiveTechnologyRecord(long MonotonicNanoseconds, string EventType, JsonElement Payload);

/// <summary>
/// The assistive technology records of a recording, for the properties
/// panel's Screen reader rows: for each product watched, whether it was
/// running, whether its module was in the instrumented Chromium's
/// processes, and whether its audio was playing sound, at each time, and the
/// times each changed. Sound is not speech: a period of sound may be speech,
/// a beep, or an earcon. Empty for a recording made before the records. See
/// docs/architecture/screen-reader-activity.md, "Tracking NVDA".
/// </summary>
public sealed class AssistiveTechnologyTimeline
{
    public const string Group = "Screen reader";
    public const string KeyPrefix = "assistive-technology.";
    private const long ChangedWindowNanoseconds = 1_000_000_000;

    private readonly List<string> _products = [];
    private readonly Dictionary<string, Product> _byProduct = new(StringComparer.Ordinal);

    private sealed class Product
    {
        // Main processes' starts (at their record time, or null when running
        // at the start) and exits.
        public List<(long Time, bool AtStart, int ProcessId, string Description)> Starts { get; } = [];
        public List<(long Time, int ProcessId)> Exits { get; } = [];

        // Module changes: +1 seen, -1 gone.
        public List<(long Time, int Change)> Modules { get; } = [];

        // Periods of sound: from the start record to the last sound.
        public List<(long Start, long End)> Sounds { get; } = [];
        public List<long> OpenSounds { get; } = [];
    }

    public AssistiveTechnologyTimeline(IEnumerable<AssistiveTechnologyRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records.OrderBy(record => record.MonotonicNanoseconds))
        {
            var payload = record.Payload;
            if (payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (record.EventType == AssistiveTechnologyRecords.WatchEventType)
            {
                Recorded = true;
                HasBrowser = payload.TryGetProperty("browserExecutablePath", out var browser) &&
                    browser.ValueKind == JsonValueKind.String;
                SoundProblem = Text(payload, "soundProblem");
                if (payload.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array)
                {
                    foreach (var product in products.EnumerateArray())
                    {
                        if (product.GetString() is { } name)
                        {
                            Of(name);
                        }
                    }
                }

                continue;
            }

            if (Text(payload, "product") is not { } productName)
            {
                continue;
            }

            var state = Of(productName);
            var time = record.MonotonicNanoseconds;
            var processId = Integer(payload, "processId");
            switch (record.EventType)
            {
                case AssistiveTechnologyRecords.ProcessStartedEventType
                    when Text(payload, "role") == AssistiveTechnologyRecords.ScreenReaderRole:
                    state.Starts.Add((
                        time,
                        payload.TryGetProperty("runningAtStart", out var atStart) && atStart.ValueKind == JsonValueKind.True,
                        processId,
                        DescribeProcess(payload)));
                    break;
                case AssistiveTechnologyRecords.ProcessExitedEventType
                    when Text(payload, "role") == AssistiveTechnologyRecords.ScreenReaderRole:
                    state.Exits.Add((time, processId));
                    break;
                case AssistiveTechnologyRecords.ModuleLoadedEventType:
                    state.Modules.Add((time, 1));
                    break;
                case AssistiveTechnologyRecords.ModuleUnloadedEventType:
                    state.Modules.Add((time, -1));
                    break;
                case AssistiveTechnologyRecords.SoundStartedEventType:
                    state.OpenSounds.Add(time);
                    break;
                case AssistiveTechnologyRecords.SoundEndedEventType:
                    var started = payload.TryGetProperty("startedAt", out var startedAt) && startedAt.TryGetInt64(out var s) ? s : time;
                    var last = payload.TryGetProperty("lastSoundAt", out var lastSoundAt) && lastSoundAt.TryGetInt64(out var l) ? l : time;
                    state.OpenSounds.Remove(started);
                    state.Sounds.Add((started, Math.Max(started, last)));
                    break;
            }
        }

        // A period whose end was not recorded runs to the end of the
        // recording.
        foreach (var state in _byProduct.Values)
        {
            foreach (var open in state.OpenSounds)
            {
                state.Sounds.Add((open, long.MaxValue));
            }

            state.Sounds.Sort();
        }
    }

    public static AssistiveTechnologyTimeline Empty { get; } = new([]);

    /// <summary>
    /// The products whose screen reader main process was running at a time:
    /// started at or before it, or running at the start, and not exited by
    /// it. For the key outcomes, which attribute a kept key to a running
    /// screen reader as an inference.
    /// </summary>
    public IReadOnlyList<string> ScreenReadersRunningAt(long time)
    {
        var running = new List<string>();
        foreach (var name in _products)
        {
            var state = _byProduct[name];
            foreach (var start in state.Starts)
            {
                if ((start.AtStart || start.Time <= time) &&
                    !state.Exits.Any(exit => exit.ProcessId == start.ProcessId && exit.Time > start.Time && exit.Time <= time))
                {
                    running.Add(name);
                    break;
                }
            }
        }

        return running;
    }

    /// <summary>Whether the recording holds the assistive technology records.</summary>
    public bool Recorded { get; }

    /// <summary>Whether the recording watched an instrumented Chromium's processes.</summary>
    public bool HasBrowser { get; }

    /// <summary>Why the sound could not be measured, or null.</summary>
    public string? SoundProblem { get; }

    /// <summary>The panel rows of each product at a time.</summary>
    public IReadOnlyList<PropertyRow> RowsAt(long time)
    {
        var rows = new List<PropertyRow>();
        var products = Recorded ? _products : [.. AssistiveTechnologyRecords.Known.Select(product => product.Product)];
        foreach (var name in products)
        {
            if (!Recorded)
            {
                rows.Add(new PropertyRow(Group, name, "not recorded", string.Empty, null, false, RunningKey(name)));
                rows.Add(new PropertyRow(Group, $"{name} in the browser", "not recorded", string.Empty, null, false, BrowserKey(name)));
                rows.Add(new PropertyRow(Group, $"{name} audio", "not recorded", string.Empty, null, false, SoundKey(name)));
                continue;
            }

            var state = _byProduct[name];
            rows.Add(RunningRow(name, state, time));
            rows.Add(BrowserRow(name, state, time));
            rows.Add(SoundRow(name, state, time));
        }

        return rows;
    }

    /// <summary>
    /// The times a row's value changed, in order, for stepping: a product's
    /// main process starting during the recording or exiting; its module
    /// seen in or gone from the browser; and each period of sound's start.
    /// Null for a key of no row, or a recording without the records.
    /// </summary>
    public IReadOnlyList<long>? ChangeTimesOf(string key)
    {
        if (!Recorded || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var (name, state) in _byProduct)
        {
            if (key == RunningKey(name))
            {
                return [.. state.Starts.Where(start => !start.AtStart).Select(start => start.Time)
                    .Concat(state.Exits.Select(exit => exit.Time)).Distinct().Order()];
            }

            if (key == BrowserKey(name))
            {
                return [.. ModuleCounts(state).Select(change => change.Time).Distinct()];
            }

            if (key == SoundKey(name))
            {
                return [.. state.Sounds.Select(sound => sound.Start).Distinct()];
            }
        }

        return null;
    }

    public static string RunningKey(string product) => KeyPrefix + product + ".running";

    public static string BrowserKey(string product) => KeyPrefix + product + ".browser";

    public static string SoundKey(string product) => KeyPrefix + product + ".sound";

    private PropertyRow RunningRow(string name, Product state, long time)
    {
        var running = new Dictionary<int, (long Time, bool AtStart, string Description)>();
        long? setAt = null;
        var changes = state.Starts
            .Select(start => (start.Time, Started: true, start.AtStart, start.ProcessId, start.Description))
            .Concat(state.Exits.Select(exit => (exit.Time, Started: false, AtStart: false, exit.ProcessId, Description: string.Empty)))
            .OrderBy(change => change.Time);
        foreach (var (at, started, atStart, processId, description) in changes)
        {
            if (at > time)
            {
                break;
            }

            if (started)
            {
                running[processId] = (at, atStart, description);
                setAt = atStart ? setAt : at;
            }
            else
            {
                running.Remove(processId);
                setAt = at;
            }
        }

        var value = running.Count switch
        {
            0 => "not running",
            1 => "running, " + running.Values.Single().Description,
            _ => string.Create(CultureInfo.InvariantCulture, $"running, {running.Count} copies: ") +
                string.Join("; ", running.Values.OrderBy(process => process.Time).Select(process => process.Description))
        };
        return Row(name, value, setAt, time, RunningKey(name));
    }

    private PropertyRow BrowserRow(string name, Product state, long time)
    {
        var label = $"{name} in the browser";
        if (!HasBrowser)
        {
            return new PropertyRow(Group, label, "no browser in this recording", string.Empty, null, false, BrowserKey(name));
        }

        var count = 0;
        long? setAt = null;
        foreach (var (at, now) in ModuleCounts(state))
        {
            if (at > time)
            {
                break;
            }

            count = now;
            setAt = at;
        }

        var value = count switch
        {
            0 => "its module not loaded",
            1 => "its module loaded in 1 browser process",
            _ => string.Create(CultureInfo.InvariantCulture, $"its module loaded in {count} browser processes")
        };
        return Row(label, value, setAt, time, BrowserKey(name));
    }

    private PropertyRow SoundRow(string name, Product state, long time)
    {
        var label = $"{name} audio";
        if (SoundProblem is not null)
        {
            return new PropertyRow(Group, label, "not measured: " + SoundProblem, string.Empty, null, false, SoundKey(name));
        }

        long? setAt = null;
        var sounding = false;
        foreach (var (start, end) in state.Sounds)
        {
            if (start > time)
            {
                break;
            }

            sounding = time <= end;
            setAt = sounding ? start : end;
        }

        return Row(label, sounding ? "playing sound" : "silent", setAt, time, SoundKey(name));
    }

    private static PropertyRow Row(string label, string value, long? setAt, long time, string key) =>
        new(
            Group,
            label,
            value,
            setAt is { } at ? WindowsPreferenceTimeline.Clock(at) : "at start",
            setAt,
            setAt is { } changedAt && time - changedAt < ChangedWindowNanoseconds,
            key);

    // The number of browser processes holding the module after each change.
    private static IEnumerable<(long Time, int Count)> ModuleCounts(Product state)
    {
        var count = 0;
        foreach (var group in state.Modules.GroupBy(change => change.Time).OrderBy(group => group.Key))
        {
            var before = count;
            count = Math.Max(0, count + group.Sum(change => change.Change));
            if (before != count)
            {
                yield return (group.Key, count);
            }
        }
    }

    // A main process as the row describes it: its version and copy, and
    // when it started by Windows' clock if before the recording.
    private static string DescribeProcess(JsonElement payload)
    {
        var parts = new List<string>();
        if (Text(payload, "productVersion") is { Length: > 0 } version)
        {
            parts.Add("version " + version);
        }

        if (Text(payload, "copy") is { } copy && copy != "unknown")
        {
            parts.Add(copy + " copy");
        }

        if (payload.TryGetProperty("runningAtStart", out var atStart) && atStart.ValueKind == JsonValueKind.True)
        {
            parts.Add(Text(payload, "startedUtc") is { } started &&
                DateTimeOffset.TryParse(started, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var utc)
                ? "started before the recording, at " + utc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
                : "started before the recording");
        }

        parts.Add(string.Create(CultureInfo.InvariantCulture, $"process {Integer(payload, "processId")}"));
        return string.Join(", ", parts);
    }

    private Product Of(string name)
    {
        if (!_byProduct.TryGetValue(name, out var state))
        {
            state = new Product();
            _byProduct[name] = state;
            _products.Add(name);
        }

        return state;
    }

    private static string? Text(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Integer(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}

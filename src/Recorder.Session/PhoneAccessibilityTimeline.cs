using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>An item's bounds on the phone's screen, in the pixels of the screen as it was turned.</summary>
public sealed record PhoneBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// An item of the phone's accessibility tree, as an event's source or a
/// snapshot's item gives it: what the player shows and matches it by.
/// </summary>
public sealed record PhoneItem(
    int WindowId,
    string? ClassName,
    string? ViewId,
    string? UniqueId,
    string? Text,
    string? ContentDescription,
    PhoneBounds Bounds,
    bool AccessibilityFocused,
    string Label);

/// <summary>
/// The item with accessibility focus: the <c>TYPE_VIEW_ACCESSIBILITY_FOCUSED</c>
/// event that gave it the focus, and the event's source item.
/// </summary>
public sealed record PhoneAccessibilityFocus(SessionTimelineEvent Event, PhoneItem Item);

/// <summary>
/// The phone's accessibility tree at a time, as the tree view shows it: a
/// row for each window of the latest snapshot, with the window's items as
/// the latest snapshot that took them gave them.
/// </summary>
public sealed class PhoneTreeAt
{
    internal PhoneTreeAt(
        AccessibilityTreeSnapshot snapshot,
        int windowCount,
        int itemCount,
        string? focusKey,
        PhoneBounds? focusBounds,
        IReadOnlyList<string> problems)
    {
        Snapshot = snapshot;
        WindowCount = windowCount;
        ItemCount = itemCount;
        FocusKey = focusKey;
        FocusBounds = focusBounds;
        Problems = problems;
    }

    public AccessibilityTreeSnapshot Snapshot { get; }

    public int WindowCount { get; }

    public int ItemCount { get; }

    /// <summary>The row of the item with accessibility focus; null when it is not in the tree.</summary>
    public string? FocusKey { get; }

    /// <summary>
    /// That item's bounds in a snapshot taken after it was given the focus,
    /// as when its list has scrolled since; null when no snapshot since
    /// holds it.
    /// </summary>
    public PhoneBounds? FocusBounds { get; }

    /// <summary>Why a window's items could not be read, one for each such window.</summary>
    public IReadOnlyList<string> Problems { get; }
}

/// <summary>
/// The phone's accessibility tree snapshots (accessibility.android.tree)
/// and accessibility focus events (accessibility.android.events), for the
/// player (Android slice 7, step 7c): the tree at a time, the item with
/// accessibility focus, and the phone's screen size for drawing its
/// outline. The playback index keeps each snapshot without its items
/// (<see cref="Compact"/>), so the items are read from the recording when
/// the tree view needs them, and the latest few are kept.
/// </summary>
public sealed class PhoneAccessibilityTimeline
{
    /// <summary><c>AccessibilityEvent.TYPE_VIEW_ACCESSIBILITY_FOCUSED</c>.</summary>
    public const int FocusedEventType = 0x8000;

    /// <summary><c>AccessibilityEvent.TYPE_VIEW_ACCESSIBILITY_FOCUS_CLEARED</c>.</summary>
    public const int FocusClearedEventType = 0x10000;

    public const string Note =
        "Each window's items as the companion app last took them at or before the playhead. A snapshot is taken when a window or its content changes, so a change shows once the snapshot after it is taken.";

    // Snapshots read from the recording that are kept, most recent first.
    private const int CachedSnapshots = 8;

    // Deeper than this, an item's children are not followed, against a cycle.
    private const int MaximumDepth = 500;

    private readonly Snapshot[] _snapshots;
    private readonly long[] _snapshotTimes;
    private readonly long[] _focusTimes;
    private readonly PhoneAccessibilityFocus?[] _focus;
    private readonly long[] _displayTimes;
    private readonly (int Width, int Height)[] _displays;
    private readonly List<(string EventId, IReadOnlyDictionary<int, IReadOnlyList<ItemData>> Windows)> _cache = [];

    // The tree last made, for the snapshot, the focus step and the source it
    // was made from, as the playhead moves many times between snapshots.
    private (int Snapshot, int Focus, ISessionEventRecordSource? Source, PhoneTreeAt Tree)? _last;

    public PhoneAccessibilityTimeline(
        IEnumerable<(SessionTimelineEvent Event, JsonElement Payload)> trees,
        IEnumerable<(SessionTimelineEvent Event, JsonElement Payload)> focus,
        IEnumerable<(long Time, JsonElement Payload)> displays)
    {
        ArgumentNullException.ThrowIfNull(trees);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(displays);

        var snapshots = new List<Snapshot>();
        foreach (var (item, payload) in trees)
        {
            if (ReadSnapshot(item, payload) is { } snapshot)
            {
                snapshots.Add(snapshot);
            }
        }

        _snapshots = [.. snapshots
            .Select((snapshot, index) => (snapshot, index))
            .OrderBy(pair => pair.snapshot.Event.MonotonicNanoseconds)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.snapshot)];
        _snapshotTimes = [.. _snapshots.Select(snapshot => snapshot.Event.MonotonicNanoseconds)];

        // The item with the focus after each focus event: the source of the
        // latest TYPE_VIEW_ACCESSIBILITY_FOCUSED, until a
        // TYPE_VIEW_ACCESSIBILITY_FOCUS_CLEARED from the same item.
        var steps = new List<(long Time, PhoneAccessibilityFocus? Focus)>();
        PhoneAccessibilityFocus? current = null;
        foreach (var (item, payload) in focus
            .Select((pair, index) => (pair, index))
            .OrderBy(pair => pair.pair.Event.MonotonicNanoseconds)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.pair))
        {
            if (payload.ValueKind != JsonValueKind.Object ||
                ReadInt32(payload, "eventType") is not { } type ||
                !payload.TryGetProperty("source", out var source) ||
                ItemOf(source, ReadInt32(payload, "windowId") ?? -1) is not { } sourceItem)
            {
                continue;
            }

            if (type == FocusedEventType)
            {
                // An item with no text of its own, such as a row of
                // Settings, is named by the event's text, which Android
                // takes from the items in it.
                current = new PhoneAccessibilityFocus(item, Containing(sourceItem, EventText(payload)));
            }
            else if (type == FocusClearedEventType && current is not null && SameItem(current.Item, sourceItem))
            {
                current = null;
            }
            else
            {
                continue;
            }

            steps.Add((item.MonotonicNanoseconds, current));
        }

        _focusTimes = [.. steps.Select(step => step.Time)];
        _focus = [.. steps.Select(step => step.Focus)];

        var sizes = new List<(long Time, int Width, int Height)>();
        foreach (var (time, payload) in displays.OrderBy(pair => pair.Time))
        {
            if (payload.ValueKind == JsonValueKind.Object &&
                ReadInt32(payload, "modeWidth") is { } width && width > 0 &&
                ReadInt32(payload, "modeHeight") is { } height && height > 0)
            {
                // The display mode's size is the screen upright; turned a
                // quarter, the screen is as wide as it is tall upright.
                var turned = (ReadInt32(payload, "rotation") ?? 0) is 90 or 270;
                sizes.Add((time, turned ? height : width, turned ? width : height));
            }
        }

        _displayTimes = [.. sizes.Select(size => size.Time)];
        _displays = [.. sizes.Select(size => (size.Width, size.Height))];
    }

    public static PhoneAccessibilityTimeline Empty { get; } = new([], [], []);

    /// <summary>Whether the recording holds a snapshot of the phone's tree.</summary>
    public bool TreesRecorded => _snapshots.Length > 0;

    /// <summary>Whether the recording holds an accessibility focus event.</summary>
    public bool FocusRecorded => _focus.Length > 0;

    public int SnapshotCount => _snapshots.Length;

    /// <summary>Whether a record is a snapshot of the phone's tree.</summary>
    public static bool IsTreeRecord(string channel, string eventType) =>
        channel == AndroidAccessibilityRecords.TreeChannel &&
        eventType == AndroidAccessibilityRecords.TreeEventType;

    /// <summary>Whether a record is an accessibility focus event, given or cleared.</summary>
    public static bool IsFocusRecord(string channel, string eventType, JsonElement payload) =>
        channel == AndroidAccessibilityRecords.EventsChannel &&
        eventType == AndroidAccessibilityRecords.EventType &&
        payload.ValueKind == JsonValueKind.Object &&
        ReadInt32(payload, "eventType") is FocusedEventType or FocusClearedEventType;

    /// <summary>Whether a record is the phone display's rotation and size.</summary>
    public static bool IsDisplayRecord(string channel, string eventType) =>
        channel == AndroidCompanionRecords.DisplayChannel &&
        eventType == AndroidCompanionRecords.DisplayEventType;

    /// <summary>
    /// A snapshot without its items, as the playback index keeps it: each
    /// window with whether its items were taken and how many, and, when
    /// they were, what they say about the screen
    /// (<see cref="PhoneScreenEvidence"/>). A compact snapshot is returned
    /// as it is.
    /// </summary>
    public static JsonElement Compact(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return payload.Clone();
        }

        var compact = new JsonObject();
        foreach (var property in payload.EnumerateObject())
        {
            if (property.Name != "windows" || property.Value.ValueKind != JsonValueKind.Array)
            {
                compact[property.Name] = JsonNode.Parse(property.Value.GetRawText());
                continue;
            }

            var windows = new JsonArray();
            foreach (var window in property.Value.EnumerateArray())
            {
                if (window.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var entry = new JsonObject();
                var taken = false;
                var count = 0;
                foreach (var field in window.EnumerateObject())
                {
                    if (field.Name == "items")
                    {
                        taken = field.Value.ValueKind == JsonValueKind.Array;
                        count = taken ? field.Value.GetArrayLength() : 0;
                        if (taken)
                        {
                            // What the items say about the screen, for
                            // its apps, screens and visits (PhoneScreens.cs),
                            // as the items are not kept.
                            entry[PhoneScreenEvidence.Field] = PhoneScreenEvidence.Of(field.Value).ToJson();
                        }

                        continue;
                    }

                    entry[field.Name] = JsonNode.Parse(field.Value.GetRawText());
                }

                if (!entry.ContainsKey("itemsTaken"))
                {
                    entry["itemsTaken"] = taken;
                    entry["itemCount"] = count;
                }

                windows.Add(entry);
            }

            compact["windows"] = windows;
        }

        return JsonSerializer.SerializeToElement(compact);
    }

    /// <summary>A snapshot's summary for the event list: why it was taken, and its windows and items.</summary>
    public static string? Summary(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("windows", out var windows) ||
            windows.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = 0;
        foreach (var window in windows.EnumerateArray())
        {
            items += window.ValueKind != JsonValueKind.Object ? 0
                : window.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength()
                : ReadInt32(window, "itemCount") ?? 0;
        }

        var reasons = payload.TryGetProperty("reasons", out var reasonList) && reasonList.ValueKind == JsonValueKind.Array
            ? string.Join(" and ", reasonList.EnumerateArray()
                .Where(reason => reason.ValueKind == JsonValueKind.String)
                .Select(reason => reason.GetString()))
            : string.Empty;
        var culture = CultureInfo.InvariantCulture;
        return string.Create(culture,
            $"{(reasons.Length > 0 ? reasons + ", " : string.Empty)}{Count(windows.GetArrayLength(), "window")}, {Count(items, "item")}");
    }

    /// <summary>An accessibility event's summary for the event list: its type and its source item.</summary>
    public static string? EventSummary(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = ReadString(payload, "eventTypeName");
        var source = payload.TryGetProperty("source", out var item)
            && ItemOf(item, ReadInt32(payload, "windowId") ?? -1) is { } sourceItem
                ? Containing(sourceItem, EventText(payload)).Label
                : null;
        return name is null ? source : source is null ? name : $"{name}, {source}";
    }

    /// <summary>The item with accessibility focus at a time; null when none is known to have it.</summary>
    public PhoneAccessibilityFocus? FocusAt(long time)
    {
        var index = LastAtOrBefore(_focusTimes, time);
        return index < 0 ? null : _focus[index];
    }

    /// <summary>
    /// The phone screen's size at a time, in its pixels as it was turned, from
    /// the companion's display records; null when none was recorded by then.
    /// </summary>
    public (int Width, int Height)? ScreenAt(long time)
    {
        var index = LastAtOrBefore(_displayTimes, time);
        return index < 0
            ? _displays.Length > 0 ? _displays[0] : null
            : _displays[index];
    }

    /// <summary>
    /// The snapshot that last took a window's items at or before a
    /// snapshot, the given one when it took them; null when none did. The
    /// checks (android-checks.md, step 3) read a settled snapshot's items
    /// from it.
    /// </summary>
    public SessionTimelineEvent? TakenBy(SessionTimelineEvent snapshot, int windowId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var at = LastAtOrBefore(_snapshotTimes, snapshot.MonotonicNanoseconds);
        for (var index = at; index >= 0 && _snapshotTimes[index] == snapshot.MonotonicNanoseconds; index--)
        {
            if (_snapshots[index].Event.EventId == snapshot.EventId)
            {
                at = index;
                break;
            }
        }

        return TakenFrom(at, windowId);
    }

    /// <summary>
    /// The time of the first snapshot after a time that lists a window,
    /// when the window may have changed; null when none does. The contrast
    /// measurement (android-checks.md, step 4b) takes the picture of a
    /// settled snapshot from before it.
    /// </summary>
    public long? NextListing(long time, int windowId)
    {
        for (var index = LastAtOrBefore(_snapshotTimes, time) + 1; index < _snapshots.Length; index++)
        {
            if (_snapshots[index].Windows.Any(entry => entry.Id == windowId))
            {
                return _snapshotTimes[index];
            }
        }

        return null;
    }

    /// <summary>
    /// The snapshot whose items for a window the tree view shows at a time:
    /// null when the latest snapshot does not list the window, or none took
    /// its items.
    /// </summary>
    public SessionTimelineEvent? TakenAt(long time, int windowId)
    {
        var latest = LastAtOrBefore(_snapshotTimes, time);
        return latest >= 0 && _snapshots[latest].Windows.Any(entry => entry.Id == windowId)
            ? TakenFrom(latest, windowId)
            : null;
    }

    private SessionTimelineEvent? TakenFrom(int latest, int windowId)
    {
        for (var index = latest; index >= 0; index--)
        {
            if (_snapshots[index].Windows.FirstOrDefault(entry => entry.Id == windowId) is { Taken: true })
            {
                return _snapshots[index].Event;
            }
        }

        return null;
    }

    /// <summary>
    /// The tree at a time; null before the first snapshot. The windows'
    /// items are read from <paramref name="source"/>; without one, each
    /// window says so.
    /// </summary>
    public PhoneTreeAt? TreeAt(long time, ISessionEventRecordSource? source)
    {
        var latest = LastAtOrBefore(_snapshotTimes, time);
        if (latest < 0)
        {
            return null;
        }

        var focusStep = LastAtOrBefore(_focusTimes, time);
        if (_last is { } last && last.Snapshot == latest && last.Focus == focusStep && ReferenceEquals(last.Source, source))
        {
            return last.Tree;
        }

        var focus = focusStep < 0 ? null : _focus[focusStep];
        var roots = new List<AccessibilityTreeRow>();
        var problems = new List<string>();
        var items = 0;
        string? focusKey = null;
        PhoneBounds? focusBounds = null;
        foreach (var window in _snapshots[latest].Windows)
        {
            var windowKey = "window:" + window.Id.ToString(CultureInfo.InvariantCulture);
            var taken = -1;
            for (var index = latest; index >= 0; index--)
            {
                if (_snapshots[index].Windows.FirstOrDefault(entry => entry.Id == window.Id) is { Taken: true })
                {
                    taken = index;
                    break;
                }
            }

            var children = new List<AccessibilityTreeRow>();
            long? takenAt = null;
            string? takenEvent = null;
            if (taken < 0)
            {
                children.Add(NoteRow(windowKey + "/note", "no snapshot by the playhead took this window's items"));
            }
            else
            {
                var snapshot = _snapshots[taken];
                takenAt = snapshot.Event.MonotonicNanoseconds;
                takenEvent = snapshot.Event.EventId;
                IReadOnlyList<ItemData>? list = null;
                string? problem = null;
                try
                {
                    list = ItemsOf(snapshot, window.Id, source);
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or InvalidOperationException or
                        JsonException or KeyNotFoundException or UnauthorizedAccessException)
                {
                    problem = exception.Message;
                }

                if (list is null)
                {
                    problem ??= source is null
                        ? "the recording's records cannot be read"
                        : "the snapshot that took them does not hold this window";
                    children.Add(NoteRow(windowKey + "/note", "this window's items could not be read: " + problem));
                    problems.Add($"{WindowName(window)}: {problem}");
                }
                else
                {
                    items += list.Count;
                    var matched = focus is not null && focus.Item.WindowId == window.Id
                        ? Match(list, focus, takenAt.Value)
                        : null;
                    var keys = new Dictionary<int, string>();
                    children.AddRange(RowsOf(list, windowKey, snapshot.Event.EventId, takenAt.Value, keys));
                    if (matched is { } found && keys.TryGetValue(found.Item.Index, out var key))
                    {
                        focusKey = key;
                        focusBounds = found.After ? found.Item.Item.Bounds : null;
                    }
                }
            }

            if (window.Truncated)
            {
                children.Add(NoteRow(windowKey + "/truncated",
                    $"the window had more than {AndroidAccessibilityRecords.MaxTreeItems.ToString("N0", CultureInfo.InvariantCulture)} items; the rest were not taken"));
            }

            if (window.MissingChildren > 0)
            {
                children.Add(NoteRow(windowKey + "/missing",
                    $"{Count(window.MissingChildren, "child")} Android listed could not be read, as when they went away while the snapshot was taken"));
            }

            roots.Add(new AccessibilityTreeRow(
                windowKey,
                AccessibilityTreeRowKind.PhoneWindow,
                null,
                window.Id,
                WindowName(window),
                takenAt,
                takenEvent,
                children));
        }

        var tree = new PhoneTreeAt(
            new AccessibilityTreeSnapshot(latest + 1, "phone", items, roots),
            roots.Count,
            items,
            focusKey,
            focusBounds,
            problems);
        _last = (latest, focusStep, source, tree);
        return tree;
    }

    // The item of a window's list with the focus: when the snapshot was
    // taken after the focus was given, the item it says has accessibility
    // focus; otherwise, or when none does, the item Android gave the same
    // unique id, or else the one with the same class, view id, text and
    // description nearest the source's bounds. After is true when the
    // snapshot was taken after the focus was given, so its bounds are newer.
    private static (ItemData Item, bool After)? Match(IReadOnlyList<ItemData> items, PhoneAccessibilityFocus focus, long takenAt)
    {
        var after = takenAt > focus.Event.MonotonicNanoseconds;
        if (after && items.FirstOrDefault(item => item.Item.AccessibilityFocused) is { } flagged)
        {
            return (flagged, true);
        }

        var source = focus.Item;
        if (source.UniqueId is { Length: > 0 } unique &&
            items.FirstOrDefault(item => item.Item.UniqueId == unique) is { } same)
        {
            return (same, after);
        }

        var nearest = items
            .Where(item =>
                item.Item.ClassName == source.ClassName &&
                item.Item.ViewId == source.ViewId &&
                item.Item.Text == source.Text &&
                item.Item.ContentDescription == source.ContentDescription)
            .OrderBy(item => Distance(item.Item.Bounds, source.Bounds))
            .FirstOrDefault();
        return nearest is null ? null : (nearest, after);
    }

    private static long Distance(PhoneBounds a, PhoneBounds b) =>
        Math.Abs((long)a.Left - b.Left) + Math.Abs((long)a.Top - b.Top) +
        Math.Abs((long)a.Right - b.Right) + Math.Abs((long)a.Bottom - b.Bottom);

    // A window's items as rows, each under its parent, keyed by its place
    // among its parent's children, so a row keeps its key, and its
    // expansion, from one snapshot to the next while the tree's shape does.
    private static List<AccessibilityTreeRow> RowsOf(
        IReadOnlyList<ItemData> items,
        string windowKey,
        string eventId,
        long takenAt,
        Dictionary<int, string> keys)
    {
        var byIndex = new Dictionary<int, ItemData>();
        var children = new Dictionary<int, List<ItemData>>();
        var tops = new List<ItemData>();
        foreach (var item in items)
        {
            byIndex.TryAdd(item.Index, item);
        }

        foreach (var item in items)
        {
            if (item.Parent is { } parent && parent != item.Index && byIndex.ContainsKey(parent))
            {
                if (!children.TryGetValue(parent, out var list))
                {
                    children[parent] = list = [];
                }

                list.Add(item);
            }
            else
            {
                tops.Add(item);
            }
        }

        // The text of the items in an item, depth first, for an item with
        // no text of its own.
        IEnumerable<string?> TextIn(int index, int depth)
        {
            if (depth >= MaximumDepth || !children.TryGetValue(index, out var list))
            {
                yield break;
            }

            foreach (var child in list)
            {
                yield return child.Item.Text ?? child.Item.ContentDescription;
                foreach (var text in TextIn(child.Index, depth + 1))
                {
                    yield return text;
                }
            }
        }

        var visited = new HashSet<int>();
        AccessibilityTreeRow RowOf(ItemData item, string key, int depth)
        {
            keys.TryAdd(item.Index, key);
            var rows = new List<AccessibilityTreeRow>();
            if (visited.Add(item.Index) && depth < MaximumDepth && children.TryGetValue(item.Index, out var list))
            {
                for (var place = 0; place < list.Count; place++)
                {
                    if (!visited.Contains(list[place].Index))
                    {
                        rows.Add(RowOf(list[place], key + "/" + place.ToString(CultureInfo.InvariantCulture), depth + 1));
                    }
                }
            }

            return new AccessibilityTreeRow(
                key,
                AccessibilityTreeRowKind.PhoneItem,
                null,
                item.Index,
                Containing(item.Item, TextIn(item.Index, depth)).Label,
                takenAt,
                eventId,
                rows)
            {
                Record = item.Json
            };
        }

        var result = new List<AccessibilityTreeRow>();
        for (var place = 0; place < tops.Count; place++)
        {
            result.Add(RowOf(tops[place], windowKey + "/" + place.ToString(CultureInfo.InvariantCulture), 0));
        }

        return result;
    }

    // A window's items in a snapshot, read from the recording, or from the
    // snapshots read most recently.
    private IReadOnlyList<ItemData>? ItemsOf(Snapshot snapshot, int windowId, ISessionEventRecordSource? source)
    {
        var at = _cache.FindIndex(entry => entry.EventId == snapshot.Event.EventId);
        IReadOnlyDictionary<int, IReadOnlyList<ItemData>> windows;
        if (at >= 0)
        {
            windows = _cache[at].Windows;
            _cache.RemoveAt(at);
        }
        else
        {
            if (source is null)
            {
                return null;
            }

            using var document = JsonDocument.Parse(source.ReadEventJson(snapshot.Event));
            windows = ReadWindows(document.RootElement.GetProperty("payload"));
        }

        _cache.Insert(0, (snapshot.Event.EventId, windows));
        if (_cache.Count > CachedSnapshots)
        {
            _cache.RemoveAt(_cache.Count - 1);
        }

        return windows.TryGetValue(windowId, out var items) ? items : null;
    }

    private static Dictionary<int, IReadOnlyList<ItemData>> ReadWindows(JsonElement payload)
    {
        var windows = new Dictionary<int, IReadOnlyList<ItemData>>();
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("windows", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return windows;
        }

        foreach (var window in list.EnumerateArray())
        {
            if (window.ValueKind != JsonValueKind.Object ||
                ReadInt32(window, "id") is not { } id ||
                !window.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var read = new List<ItemData>();
            foreach (var item in items.EnumerateArray())
            {
                if (ReadInt32(item, "index") is { } index && ItemOf(item, id) is { } data)
                {
                    read.Add(new ItemData(index, ReadInt32(item, "parent"), data, item.GetRawText()));
                }
            }

            windows.TryAdd(id, read);
        }

        return windows;
    }

    // A window as the tree view names it: its title, type and states.
    private static string WindowName(WindowEntry window)
    {
        var states = new List<string> { window.TypeName };
        if (window.Active)
        {
            states.Add("active");
        }

        if (window.Focused)
        {
            states.Add("focused");
        }

        if (window.AccessibilityFocused)
        {
            states.Add("has accessibility focus");
        }

        var title = window.Title is { Length: > 0 } text ? $"\"{text.Trim()}\"" : "with no title";
        return string.Create(CultureInfo.InvariantCulture,
            $"Window {title} ({string.Join(", ", states)}, window {window.Id})");
    }

    private static Snapshot? ReadSnapshot(SessionTimelineEvent item, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("windows", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var windows = new List<WindowEntry>();
        foreach (var window in list.EnumerateArray())
        {
            if (window.ValueKind != JsonValueKind.Object || ReadInt32(window, "id") is not { } id)
            {
                continue;
            }

            var taken = window.TryGetProperty("items", out var items)
                ? items.ValueKind == JsonValueKind.Array
                : ReadBoolean(window, "itemsTaken");
            windows.Add(new WindowEntry(
                id,
                ReadString(window, "typeName") ?? "TYPE_UNKNOWN",
                ReadString(window, "title"),
                ReadBoolean(window, "active"),
                ReadBoolean(window, "focused"),
                ReadBoolean(window, "accessibilityFocused"),
                taken,
                ReadBoolean(window, "truncated"),
                ReadInt32(window, "missingChildren") ?? 0));
        }

        return new Snapshot(item, windows);
    }

    // An item as an event's source or a snapshot's item gives it; null when
    // it has no bounds.
    private static PhoneItem? ItemOf(JsonElement item, int windowId)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("bounds", out var bounds) ||
            bounds.ValueKind != JsonValueKind.Object ||
            ReadInt32(bounds, "left") is not { } left ||
            ReadInt32(bounds, "top") is not { } top ||
            ReadInt32(bounds, "right") is not { } right ||
            ReadInt32(bounds, "bottom") is not { } bottom)
        {
            return null;
        }

        var className = ReadString(item, "className");
        var text = ReadString(item, "text");
        var description = ReadString(item, "contentDescription");
        return new PhoneItem(
            ReadInt32(item, "windowId") ?? windowId,
            className,
            ReadString(item, "viewId"),
            ReadString(item, "uniqueId"),
            text,
            description,
            new PhoneBounds(left, top, right, bottom),
            ReadBoolean(item, "accessibilityFocused"),
            LabelOf(item, className, text, description));
    }

    // An item's class without its package, its text or else its
    // description, and the states a reader of the tree looks for.
    private static string LabelOf(JsonElement item, string? className, string? text, string? description)
    {
        var shortClass = className is { Length: > 0 } name
            ? name[(name.LastIndexOf('.') + 1)..]
            : "item";
        var words = Shorten(text) is { } shown
            ? $"{shortClass} \"{shown}\""
            : Shorten(description) is { } described
                ? $"{shortClass} described as \"{described}\""
                : shortClass + NoText;
        var states = new List<string>();
        if (ReadBoolean(item, "heading"))
        {
            states.Add("heading");
        }

        if (ReadBoolean(item, "checkable") && ReadString(item, "checked") is { } checkedState)
        {
            states.Add(checkedState);
        }

        if (ReadBoolean(item, "selected"))
        {
            states.Add("selected");
        }

        if (item.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
        {
            states.Add("not enabled");
        }

        if (item.TryGetProperty("visibleToUser", out var visible) && visible.ValueKind == JsonValueKind.False)
        {
            states.Add("not visible");
        }

        return states.Count == 0 ? words : $"{words}, {string.Join(", ", states)}";
    }

    // An event's text, which for an item with no text of its own Android
    // takes from the items in it.
    private static IEnumerable<string?> EventText(JsonElement payload) =>
        payload.TryGetProperty("text", out var texts) && texts.ValueKind == JsonValueKind.Array
            ? texts.EnumerateArray()
                .Where(text => text.ValueKind == JsonValueKind.String)
                .Select(text => text.GetString())
            : [];

    // An item with no text or description of its own named by the text in
    // it: "LinearLayout containing "Network & internet", "Mobile, Wi-Fi""
    // rather than "LinearLayout with no text"; the first three.
    private const string NoText = " with no text";

    private static PhoneItem Containing(PhoneItem item, IEnumerable<string?> texts)
    {
        var at = item.Label.IndexOf(NoText, StringComparison.Ordinal);
        if (at < 0 || Shorten(item.Text) is not null || Shorten(item.ContentDescription) is not null)
        {
            return item;
        }

        var shown = texts
            .Select(Shorten)
            .OfType<string>()
            .Take(3)
            .Select(text => $"\"{text}\"")
            .ToList();
        return shown.Count == 0
            ? item
            : item with { Label = item.Label[..at] + " containing " + string.Join(", ", shown) + item.Label[(at + NoText.Length)..] };
    }

    private static string? Shorten(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var trimmed = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length == 0 ? null : trimmed.Length > 80 ? trimmed[..80].TrimEnd() + "…" : trimmed;
    }

    // Whether a focus cleared event's source is the item with the focus: its
    // unique id when both have one, or else its window, class, view id, text
    // and description. Not its bounds, which change when its list scrolls
    // between the focus and its clearing.
    private static bool SameItem(PhoneItem a, PhoneItem b) =>
        a.UniqueId is { Length: > 0 } && b.UniqueId is { Length: > 0 }
            ? a.UniqueId == b.UniqueId
            : a.WindowId == b.WindowId && a.ClassName == b.ClassName && a.ViewId == b.ViewId &&
              a.Text == b.Text && a.ContentDescription == b.ContentDescription;

    private static AccessibilityTreeRow NoteRow(string key, string label) =>
        new(key, AccessibilityTreeRowKind.NotRecorded, null, null, label, null, null, []);

    private static string Count(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{count:N0} {(count == 1 ? noun : noun == "child" ? "children" : noun + "s")}");

    private static int LastAtOrBefore(long[] times, long time)
    {
        var index = Array.BinarySearch(times, time);
        if (index < 0)
        {
            return ~index - 1;
        }

        while (index + 1 < times.Length && times[index + 1] == time)
        {
            index++;
        }

        return index;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt32(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private sealed record Snapshot(SessionTimelineEvent Event, IReadOnlyList<WindowEntry> Windows);

    private sealed record WindowEntry(
        int Id,
        string TypeName,
        string? Title,
        bool Active,
        bool Focused,
        bool AccessibilityFocused,
        bool Taken,
        bool Truncated,
        int MissingChildren);

    private sealed record ItemData(int Index, int? Parent, PhoneItem Item, string Json);
}

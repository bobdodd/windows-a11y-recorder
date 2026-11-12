using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Contracts;

namespace Recorder.Session;

// Step 1 of docs/architecture/android-checks.md, "Build order": a phone
// recording divided into apps, visits, screens and states, from its tree
// snapshots and its TYPE_WINDOW_STATE_CHANGED events, by fixed rules and
// with no model asked (decisions 1 and 5). The rules are in the plan's
// "Screens and visits" and "As built: step 1".

/// <summary>What named a screen or a state.</summary>
public enum PhoneTitleSource
{
    /// <summary>The window's title (<c>AccessibilityWindowInfo.getTitle</c>).</summary>
    WindowTitle,

    /// <summary>The text of the window's latest <c>TYPE_WINDOW_STATE_CHANGED</c> event.</summary>
    WindowStateText,

    /// <summary>The text of the first heading in the window's items, in the tree's order.</summary>
    FirstHeading,

    /// <summary>
    /// The window's first fixed text: in the tree's order, the first text
    /// that is not a control's, not in a scrolling container and not in a
    /// titled pane. Used when screens share a title, or have none.
    /// </summary>
    FirstFixedText,

    /// <summary>
    /// The window's first text, for a screen with no first fixed text, as
    /// when every text is in a scrolling container (decision 10).
    /// </summary>
    FirstText,

    /// <summary>A pane's title, for a state.</summary>
    PaneTitle,

    /// <summary>Nothing: the screen is named by its activity and a number.</summary>
    Untitled,
}

/// <summary>Why a visit started.</summary>
public enum PhoneVisitCause
{
    /// <summary>The first app window of the recording.</summary>
    FirstWindow,

    /// <summary>A new full-size application window, from the same app.</summary>
    NewWindow,

    /// <summary>A window-state event in the app's window with a different class name or text.</summary>
    WindowStateChanged,

    /// <summary>The app's window title changed.</summary>
    WindowTitleChanged,

    /// <summary>The first fixed text of the app's window changed, in the same window with the same title.</summary>
    FixedTextChanged,

    /// <summary>
    /// The first fixed text of the app's window went away for at least
    /// half a second, in the same window with the same title, while the
    /// window still had text (decision 10).
    /// </summary>
    FixedTextGone,

    /// <summary>Another app's window came to the front.</summary>
    AnotherApp,
}

/// <summary>An app seen in a recording, with its screens in order of first visit.</summary>
public sealed class PhoneApp
{
    internal PhoneApp(string package) => Package = package;

    public string Package { get; }

    public IReadOnlyList<PhoneScreen> Screens => _screens;

    internal readonly List<PhoneScreen> _screens = [];
}

/// <summary>A screen: the identity visits with the same screen key belong to.</summary>
public sealed class PhoneScreen
{
    internal PhoneScreen(int number, string package, string? activity, string? title, PhoneTitleSource titleSource, string? viewIdSignature, string? sharedTitle = null, int sharedBy = 0)
    {
        Number = number;
        Package = package;
        Activity = activity;
        Title = title;
        TitleSource = titleSource;
        ViewIdSignature = viewIdSignature;
        SharedTitle = sharedTitle;
        SharedBy = sharedBy;
    }

    /// <summary>The screen's number in order of first visit, from 1.</summary>
    public int Number { get; }

    public string Package { get; }

    /// <summary>The class name from the window-state event, the activity for an activity window; null when no event gave one.</summary>
    public string? Activity { get; internal set; }

    /// <summary>The screen title; null when untitled.</summary>
    public string? Title { get; }

    public PhoneTitleSource TitleSource { get; }

    /// <summary>For an untitled screen, a hash of the sorted view IDs in the app's window; null when it had none.</summary>
    public string? ViewIdSignature { get; }

    /// <summary>
    /// For a screen named by its first fixed text, the title it shares
    /// with other screens (window title, window-state text or heading);
    /// null when it had none.
    /// </summary>
    public string? SharedTitle { get; }

    /// <summary>For a screen named by its first fixed text under a shared title, how many screens share it.</summary>
    public int SharedBy { get; }

    /// <summary>For an untitled screen, its number among untitled screens; 0 otherwise.</summary>
    public int UntitledNumber { get; internal set; }

    public IReadOnlyList<PhoneVisit> Visits => _visits;

    internal readonly List<PhoneVisit> _visits = [];

    /// <summary>The screen's name: its title, or its activity and a number when it has none.</summary>
    public string Name =>
        Title ?? string.Create(CultureInfo.InvariantCulture,
            $"{(Activity is null ? string.Empty : ShortClass(Activity) + ", ")}untitled screen {UntitledNumber}");

    /// <summary>The screen's name with its app, such as "Safety and emergency (com.android.settings)".</summary>
    public string Label => $"{Name} ({Package})";

    /// <summary>How the screen's title was found, for the player.</summary>
    public string NamedBy => TitleSource switch
    {
        PhoneTitleSource.WindowTitle => "named by its window title",
        PhoneTitleSource.WindowStateText => "named by the text of its window-state event",
        PhoneTitleSource.FirstHeading => "named by its first heading",
        PhoneTitleSource.FirstFixedText when SharedTitle is { } shared => string.Create(CultureInfo.InvariantCulture,
            $"named by its first fixed text, as {SharedBy} screens share the title '{shared}'"),
        PhoneTitleSource.FirstFixedText => "named by its first fixed text, as it has no title",
        PhoneTitleSource.FirstText when SharedTitle is { } shared => string.Create(CultureInfo.InvariantCulture,
            $"named by its first text, as it has no fixed text and {SharedBy} screens share the title '{shared}'"),
        PhoneTitleSource.FirstText => "named by its first text, as it has no title and no fixed text",
        _ when ViewIdSignature is null => "untitled, and with no view IDs, so every untitled visit to this activity is counted as this screen",
        _ => "untitled, kept apart by its view IDs",
    };

    /// <summary>A class name without its package, such as "SubSettings".</summary>
    public static string ShortClass(string className)
    {
        var dot = className.LastIndexOf('.');
        return dot >= 0 && dot + 1 < className.Length ? className[(dot + 1)..] : className;
    }
}

/// <summary>One continuous period a screen was shown.</summary>
public sealed class PhoneVisit
{
    internal PhoneVisit(int number, long start, PhoneVisitCause cause, int windowId, string package, string? activity, string? windowTitle, string? stateText)
    {
        Number = number;
        Start = start;
        Cause = cause;
        WindowId = windowId;
        Package = package;
        Activity = activity;
        WindowTitle = windowTitle;
        StateText = stateText;
    }

    /// <summary>The visit's number in time order, from 1.</summary>
    public int Number { get; }

    public long Start { get; }

    /// <summary>The start of the next visit, or the end of the recording.</summary>
    public long End { get; internal set; }

    public PhoneVisitCause Cause { get; }

    /// <summary>The app's window: the full-size application window the visit was in.</summary>
    public int WindowId { get; }

    public string Package { get; }

    public string? Activity { get; }

    public string? WindowTitle { get; }

    public string? StateText { get; }

    /// <summary>The first heading's text in the visit's settled snapshots, or in any when none settled had one.</summary>
    public string? FirstHeading { get; internal set; }

    public string? ViewIdSignature { get; internal set; }

    /// <summary>The first fixed text of the app's window in the visit: the one that started it, or else the first seen.</summary>
    public string? FixedText { get; internal set; }

    /// <summary>
    /// For a visit with no first fixed text, the window's first text when
    /// the visit started, or else when first seen (decision 10).
    /// </summary>
    public string? FirstText { get; internal set; }

    public PhoneScreen Screen { get; internal set; } = null!;

    /// <summary>Whether this is a return: the screen was visited before.</summary>
    public bool IsReturn => Screen.Visits.Count > 0 && !ReferenceEquals(Screen.Visits[0], this);

    public IReadOnlyList<PhoneScreenState> States => _states;

    internal readonly List<PhoneScreenState> _states = [];

    /// <summary>The snapshots that listed the app's window during the visit.</summary>
    public IReadOnlyList<SessionTimelineEvent> Snapshots => _snapshots;

    internal readonly List<SessionTimelineEvent> _snapshots = [];

    /// <summary>The snapshots in which the app's window had its final bounds for the visit.</summary>
    public IReadOnlyList<SessionTimelineEvent> SettledSnapshots => _settled;

    internal readonly List<SessionTimelineEvent> _settled = [];

    // The app window's listings, for working out which are settled.
    internal readonly List<(SessionTimelineEvent Event, PhoneBounds? Bounds, long? TransitionTime, PhoneScreenEvidence? Evidence)> _listings = [];

    public string When => $"{WindowsPreferenceTimeline.Clock(Start)} to {WindowsPreferenceTimeline.Clock(End)}";

    public string CauseText => Cause switch
    {
        PhoneVisitCause.FirstWindow => "the recording's first app window",
        PhoneVisitCause.NewWindow => "a new window",
        PhoneVisitCause.WindowStateChanged => "a window-state event",
        PhoneVisitCause.WindowTitleChanged => "a change of window title",
        PhoneVisitCause.FixedTextChanged => "a change of first fixed text",
        PhoneVisitCause.FixedTextGone => "the first fixed text going away",
        _ => "another app coming to the front",
    };
}

/// <summary>A dialog, menu, sheet or pane over a visit's screen (decision 6).</summary>
public sealed record PhoneScreenState(long Start, long End, string Kind, string Name, PhoneTitleSource NamedBy, string? Package, int? WindowId, string? FixedText = null)
{
    public string When => $"{WindowsPreferenceTimeline.Clock(Start)} to {WindowsPreferenceTimeline.Clock(End)}";
}

/// <summary>
/// What a window's items say about its screen, worked out when a snapshot
/// is made compact for the playback index (<see cref="PhoneAccessibilityTimeline.Compact"/>),
/// as the items are not kept there.
/// </summary>
public sealed record PhoneScreenEvidence(string? Package, string? FirstHeading, IReadOnlyList<string> PaneTitles, string? ViewIdSignature, int ViewIdCount)
{
    public const string Field = "screenEvidence";

    /// <summary>
    /// The window's first fixed text (<see cref="PhoneTitleSource.FirstFixedText"/>):
    /// in the tree's order, the first visible text or content description
    /// of an item that is not clickable, with no clickable or scrollable
    /// item around it, and in no titled pane. Measured on the client's app
    /// (20261112-005243): one window titled "Nova SleepCare" for every
    /// screen, no headings and no view IDs of its own, but a first fixed
    /// text of its own on each screen.
    /// </summary>
    public string? FixedText { get; init; }

    /// <summary>
    /// For a window with no first fixed text, its first text: in the
    /// tree's order, the first visible text or content description of an
    /// item that is not clickable, with no clickable item around it, and in
    /// no titled pane, though inside a scrolling container. Measured on the
    /// client's app (20261112-154851): its Profile screen has every text
    /// inside an android.widget.ScrollView, so no first fixed text, and
    /// its first text is "Bob Dodd". Null when the window has a first
    /// fixed text.
    /// </summary>
    public string? FirstText { get; init; }

    /// <summary>Each titled pane's first fixed text, by pane title, for panes that have one.</summary>
    public IReadOnlyDictionary<string, string> PaneFixedTexts { get; init; } = new Dictionary<string, string>();

    /// <summary>The evidence in a window's items.</summary>
    public static PhoneScreenEvidence Of(JsonElement items)
    {
        var list = items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToArray()
            : [];
        string? package = null;
        var parents = new Dictionary<int, int?>();
        foreach (var item in list)
        {
            if (ReadInt32(item, "index") is { } index)
            {
                parents[index] = ReadInt32(item, "parent");
            }

            if (package is null && ReadInt32(item, "parent") is null)
            {
                package = ReadString(item, "packageName");
            }
        }

        package ??= list.Select(item => ReadString(item, "packageName")).FirstOrDefault(name => name is not null);

        string? heading = null;
        for (var position = 0; position < list.Length && heading is null; position++)
        {
            var item = list[position];
            if (!ReadBoolean(item, "heading") || !ReadBoolean(item, "visibleToUser"))
            {
                continue;
            }

            heading = TextOf(item);
            if (heading is null && ReadInt32(item, "index") is { } headingIndex)
            {
                // A heading with no text of its own, such as a toolbar's
                // title row, takes the first text inside it.
                for (var later = position + 1; later < list.Length && heading is null; later++)
                {
                    if (ReadInt32(list[later], "index") is { } laterIndex &&
                        IsInside(laterIndex, headingIndex, parents) &&
                        ReadBoolean(list[later], "visibleToUser"))
                    {
                        heading = TextOf(list[later]);
                    }
                }
            }
        }

        // Each item's parent, whether it is a control or a scrolling
        // container (a barrier: no text inside is fixed), and its pane title.
        var barriers = new Dictionary<int, bool>();
        var controls = new Dictionary<int, bool>();
        var paneOf = new Dictionary<int, string?>();
        foreach (var item in list)
        {
            if (ReadInt32(item, "index") is { } index)
            {
                controls[index] = ReadBoolean(item, "clickable");
                barriers[index] = controls[index] || ReadBoolean(item, "scrollable");
                paneOf[index] = Clean(ReadString(item, "paneTitle"));
            }
        }

        string? fixedText = null;
        string? firstText = null;
        var paneFixedTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in list)
        {
            if (ReadInt32(item, "index") is not { } index ||
                !ReadBoolean(item, "visibleToUser") ||
                barriers.GetValueOrDefault(index) ||
                TextOf(item) is not { } text)
            {
                continue;
            }

            // The nearest titled pane around the item, and whether a
            // control, or a scrolling container, is around it.
            string? pane = paneOf.GetValueOrDefault(index);
            var inControl = false;
            var blocked = false;
            var current = parents.GetValueOrDefault(index);
            for (var steps = 0; current is { } ancestor && steps < 500 && !inControl; steps++)
            {
                inControl = controls.GetValueOrDefault(ancestor);
                blocked |= barriers.GetValueOrDefault(ancestor);
                pane ??= paneOf.GetValueOrDefault(ancestor);
                current = parents.GetValueOrDefault(ancestor);
            }

            if (inControl)
            {
                continue;
            }

            if (pane is null)
            {
                firstText ??= text;
            }

            if (blocked)
            {
                continue;
            }

            if (pane is null)
            {
                fixedText ??= text;
            }
            else
            {
                paneFixedTexts.TryAdd(pane, text);
            }
        }

        var panes = list
            .Select(item => Clean(ReadString(item, "paneTitle")))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var viewIds = list
            .Select(item => ReadString(item, "viewId"))
            .OfType<string>()
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var signature = viewIds.Length == 0
            ? null
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', viewIds))))[..16];
        return new PhoneScreenEvidence(package, heading, panes, signature, viewIds.Length)
        {
            FixedText = fixedText,
            FirstText = fixedText is null ? firstText : null,
            PaneFixedTexts = paneFixedTexts,
        };
    }

    public JsonObject ToJson()
    {
        var panes = new JsonArray();
        foreach (var pane in PaneTitles)
        {
            panes.Add(pane);
        }

        var paneTexts = new JsonObject();
        foreach (var (pane, text) in PaneFixedTexts)
        {
            paneTexts[pane] = text;
        }

        return new JsonObject
        {
            ["package"] = Package,
            ["firstHeading"] = FirstHeading,
            ["fixedText"] = FixedText,
            ["firstText"] = FirstText,
            ["paneTitles"] = panes,
            ["paneFixedTexts"] = paneTexts,
            ["viewIdSignature"] = ViewIdSignature,
            ["viewIdCount"] = ViewIdCount,
        };
    }

    public static PhoneScreenEvidence? Read(JsonElement window)
    {
        if (window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty(Field, out var evidence) ||
            evidence.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var panes = evidence.TryGetProperty("paneTitles", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(pane => pane.ValueKind == JsonValueKind.String).Select(pane => pane.GetString()!).ToArray()
            : [];
        var paneTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (evidence.TryGetProperty("paneFixedTexts", out var texts) && texts.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in texts.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    paneTexts[property.Name] = property.Value.GetString()!;
                }
            }
        }

        return new PhoneScreenEvidence(
            ReadString(evidence, "package"),
            ReadString(evidence, "firstHeading"),
            panes,
            ReadString(evidence, "viewIdSignature"),
            ReadInt32(evidence, "viewIdCount") ?? 0)
        {
            FixedText = ReadString(evidence, "fixedText"),
            FirstText = ReadString(evidence, "firstText"),
            PaneFixedTexts = paneTexts,
        };
    }

    private static bool IsInside(int index, int ancestor, Dictionary<int, int?> parents)
    {
        var current = parents.TryGetValue(index, out var parent) ? parent : null;
        for (var steps = 0; current is { } value && steps < 500; steps++)
        {
            if (value == ancestor)
            {
                return true;
            }

            current = parents.TryGetValue(value, out parent) ? parent : null;
        }

        return false;
    }

    private static string? TextOf(JsonElement item) =>
        Clean(ReadString(item, "text")) ?? Clean(ReadString(item, "contentDescription"));

    internal static string? Clean(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    internal static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static int? ReadInt32(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    internal static bool ReadBoolean(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;
}

/// <summary>
/// A phone recording's apps, visits, screens and states (android-checks.md,
/// "Screens and visits"), worked out by fixed rules from the compact tree
/// snapshots and the <c>TYPE_WINDOW_STATE_CHANGED</c> events.
/// </summary>
public sealed class PhoneScreenTimeline
{
    /// <summary><c>AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED</c>.</summary>
    public const int WindowStateChangedEventType = 0x20;

    // CONTENT_CHANGE_TYPE_PANE_TITLE, _PANE_APPEARED and _PANE_DISAPPEARED:
    // a window-state event with these is a pane's, a state, not a visit.
    private const int PaneChangeTypes = 0x8 | 0x10 | 0x20;

    // An application window covering at least this share of the screen can
    // be the app's window; a smaller one above it is a state. A window
    // sliding in, as measured at 16.57 s in test 64 (82%), still counts.
    private const double AppWindowShare = 0.5;

    // A listing is settled only when the app's window is not listed again
    // within this time: in the client's app (20261112-005243) a new screen
    // came in the same window with the same transition time, its snapshots
    // 10 to 20 ms apart for about half a second, one with no text at all.
    private const long QuietNanoseconds = 500_000_000;

    private static readonly string[] StateClassWords = ["Dialog", "BottomSheet", "PopupWindow", "PopupMenu"];

    private readonly long[] _starts;

    public PhoneScreenTimeline(
        IEnumerable<(SessionTimelineEvent Event, JsonElement Payload)> trees,
        IEnumerable<(SessionTimelineEvent Event, JsonElement Payload)> windowStates,
        Func<long, (int Width, int Height)?> screenAt,
        long duration)
    {
        ArgumentNullException.ThrowIfNull(trees);
        ArgumentNullException.ThrowIfNull(windowStates);
        ArgumentNullException.ThrowIfNull(screenAt);

        var steps = new List<(long Time, int Order, Step Step)>();
        var order = 0;
        foreach (var (item, payload) in windowStates)
        {
            if (ReadState(payload) is { } state)
            {
                steps.Add((item.MonotonicNanoseconds, 0, new Step(item, null, state)));
            }
        }

        foreach (var (item, payload) in trees)
        {
            if (ReadWindows(payload) is { } windows)
            {
                // Events at a snapshot's time come before it.
                steps.Add((item.MonotonicNanoseconds, 1, new Step(item, windows, null)));
            }
        }

        var builder = new Builder(screenAt, duration);
        foreach (var (_, _, step) in steps
            .Select(entry => (entry.Time, entry.Order, entry.Step, Index: order++))
            .OrderBy(entry => entry.Time)
            .ThenBy(entry => entry.Order)
            .ThenBy(entry => entry.Index)
            .Select(entry => (entry.Time, entry.Order, entry.Step)))
        {
            if (step.State is { } state)
            {
                builder.Add(step.Event, state);
            }
            else
            {
                builder.Add(step.Event, step.Windows!);
            }
        }

        (Visits, Screens, Apps) = builder.Finish();
        _starts = [.. Visits.Select(visit => visit.Start)];
    }

    public static PhoneScreenTimeline Empty { get; } = new([], [], _ => null, 0);

    /// <summary>Whether the recording has any visit, that is a snapshot with an app's window.</summary>
    public bool Recorded => Visits.Count > 0;

    /// <summary>The visits in time order.</summary>
    public IReadOnlyList<PhoneVisit> Visits { get; }

    /// <summary>The screens in order of first visit.</summary>
    public IReadOnlyList<PhoneScreen> Screens { get; }

    /// <summary>The apps in order of first visit, each with its screens.</summary>
    public IReadOnlyList<PhoneApp> Apps { get; }

    /// <summary>Whether a record is a <c>TYPE_WINDOW_STATE_CHANGED</c> event, which the playback index keeps whole.</summary>
    public static bool IsWindowStateRecord(string channel, string eventType, JsonElement payload) =>
        channel == AndroidAccessibilityRecords.EventsChannel &&
        eventType == AndroidAccessibilityRecords.EventType &&
        payload.ValueKind == JsonValueKind.Object &&
        PhoneScreenEvidence.ReadInt32(payload, "eventType") == WindowStateChangedEventType;

    /// <summary>The visit at a time; null before the first.</summary>
    public PhoneVisit? VisitAt(long time)
    {
        var index = Array.BinarySearch(_starts, time);
        if (index < 0)
        {
            index = ~index - 1;
        }
        else
        {
            while (index + 1 < _starts.Length && _starts[index + 1] == time)
            {
                index++;
            }
        }

        return index < 0 ? null : Visits[index];
    }

    private static WindowState? ReadState(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            PhoneScreenEvidence.ReadInt32(payload, "eventType") != WindowStateChangedEventType ||
            PhoneScreenEvidence.ReadInt32(payload, "windowId") is not { } window)
        {
            return null;
        }

        var text = payload.TryGetProperty("text", out var list) && list.ValueKind == JsonValueKind.Array
            ? PhoneScreenEvidence.Clean(string.Join(" ", list.EnumerateArray()
                .Where(part => part.ValueKind == JsonValueKind.String)
                .Select(part => part.GetString())))
            : null;
        text ??= PhoneScreenEvidence.Clean(PhoneScreenEvidence.ReadString(payload, "contentDescription"));
        var changes = PhoneScreenEvidence.ReadInt32(payload, "contentChangeTypes") ?? 0;
        return new WindowState(
            window,
            PhoneScreenEvidence.ReadString(payload, "packageName"),
            PhoneScreenEvidence.Clean(PhoneScreenEvidence.ReadString(payload, "className")),
            text,
            (changes & PaneChangeTypes) != 0);
    }

    private static List<Window>? ReadWindows(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("windows", out var windows) ||
            windows.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<Window>();
        foreach (var window in windows.EnumerateArray())
        {
            if (PhoneScreenEvidence.ReadInt32(window, "id") is not { } id)
            {
                continue;
            }

            PhoneBounds? bounds = null;
            if (window.TryGetProperty("bounds", out var box) && box.ValueKind == JsonValueKind.Object &&
                PhoneScreenEvidence.ReadInt32(box, "left") is { } left &&
                PhoneScreenEvidence.ReadInt32(box, "top") is { } top &&
                PhoneScreenEvidence.ReadInt32(box, "right") is { } right &&
                PhoneScreenEvidence.ReadInt32(box, "bottom") is { } bottom)
            {
                bounds = new PhoneBounds(left, top, right, bottom);
            }

            list.Add(new Window(
                id,
                PhoneScreenEvidence.ReadString(window, "typeName") == "TYPE_APPLICATION" ||
                    PhoneScreenEvidence.ReadInt32(window, "type") == 1,
                PhoneScreenEvidence.ReadInt32(window, "layer") ?? 0,
                PhoneScreenEvidence.Clean(PhoneScreenEvidence.ReadString(window, "title")),
                bounds,
                window.TryGetProperty("transitionTimeMilliseconds", out var transition) &&
                    transition.ValueKind == JsonValueKind.Number && transition.TryGetInt64(out var milliseconds)
                    ? milliseconds
                    : null,
                PhoneScreenEvidence.ReadBoolean(window, "active"),
                PhoneScreenEvidence.Read(window)));
        }

        return list;
    }

    private static bool IsStateClass(string? className)
    {
        if (className is null)
        {
            return false;
        }

        var name = PhoneScreen.ShortClass(className);
        return StateClassWords.Any(word => name.Contains(word, StringComparison.Ordinal));
    }

    private sealed record Step(SessionTimelineEvent Event, List<Window>? Windows, WindowState? State);

    private sealed record WindowState(int WindowId, string? Package, string? ClassName, string? Text, bool Pane);

    private sealed record Window(int Id, bool Application, int Layer, string? Title, PhoneBounds? Bounds, long? TransitionTime, bool Active, PhoneScreenEvidence? Evidence);

    // A state open in the current visit, keyed by its window or pane title.
    private sealed class OpenState(long start, string kind, string name, PhoneTitleSource namedBy, string? package, int? windowId, string? fixedText)
    {
        public string? FixedText { get; set; } = fixedText;

        public long Start { get; } = start;

        public string Kind { get; } = kind;

        public string Name { get; set; } = name;

        public PhoneTitleSource NamedBy { get; set; } = namedBy;

        public string? Package { get; } = package;

        public int? WindowId { get; } = windowId;
    }

    private sealed class Builder(Func<long, (int Width, int Height)?> screenAt, long duration)
    {
        private readonly List<PhoneVisit> _visits = [];

        // Each window's latest non-pane window-state event, and when its
        // class name and text were first given.
        private readonly Dictionary<int, (WindowState State, long Since)> _states = [];

        // Each window's latest evidence, from the last snapshot that took its items.
        private readonly Dictionary<int, PhoneScreenEvidence> _evidence = [];

        private readonly Dictionary<string, OpenState> _open = [];

        // Every listing of an app's window, in time order, for whether a
        // listing was followed within QuietNanoseconds by another.
        private readonly Dictionary<int, List<long>> _listingTimes = [];

        private PhoneVisit? _current;

        private int _currentLayer;

        // When the current visit's first fixed text went away while the
        // window still had text, and its first text then: a visit starts
        // there if it stays away for QuietNanoseconds (decision 10).
        private (long Start, string FirstText)? _gone;

        public void Add(SessionTimelineEvent item, WindowState state)
        {
            if (state.Pane)
            {
                // A pane's event is a state of the screen, which the
                // snapshot's pane titles give.
                return;
            }

            var time = item.MonotonicNanoseconds;
            var since = _states.TryGetValue(state.WindowId, out var previous) &&
                previous.State.ClassName == state.ClassName && previous.State.Text == state.Text
                    ? previous.Since
                    : time;
            _states[state.WindowId] = (state, since);

            if (_current is { } visit && visit.WindowId == state.WindowId &&
                (state.ClassName != visit.Activity || state.Text != visit.StateText) &&
                !IsStateClass(state.ClassName))
            {
                EndGone(time);
                Begin(time, PhoneVisitCause.WindowStateChanged, state.WindowId, visit.Package, visit.WindowTitle, null, _currentLayer);
            }
        }

        public void Add(SessionTimelineEvent item, List<Window> windows)
        {
            var time = item.MonotonicNanoseconds;
            foreach (var window in windows)
            {
                if (window.Evidence is { } evidence)
                {
                    _evidence[window.Id] = evidence;
                }
            }

            var applications = windows.Where(window => window.Application).ToList();
            if (AppWindow(time, applications) is not { } app)
            {
                // No app window, as between two activities: the visit goes on.
                return;
            }

            var package = (_evidence.TryGetValue(app.Id, out var appEvidence) ? appEvidence.Package : null)
                ?? (_states.TryGetValue(app.Id, out var appState) ? appState.State.Package : null)
                ?? _current?.Package
                ?? "unknown app";
            var fixedText = _evidence.GetValueOrDefault(app.Id)?.FixedText;
            var firstText = _evidence.GetValueOrDefault(app.Id)?.FirstText;
            if (_gone is { } gone && _current is { } goneFrom)
            {
                // The fixed text came back, another came, the window
                // changed, or it has stayed away long enough.
                if (fixedText is not null || app.Id != goneFrom.WindowId || app.Title != goneFrom.WindowTitle ||
                    time - gone.Start >= QuietNanoseconds)
                {
                    EndGone(time);
                }
            }

            if (_current is not { } visit)
            {
                Begin(StartOf(app.Id, time), PhoneVisitCause.FirstWindow, app.Id, package, app.Title, fixedText, app.Layer);
            }
            else if (app.Id != visit.WindowId)
            {
                Begin(StartOf(app.Id, time),
                    package == visit.Package ? PhoneVisitCause.NewWindow : PhoneVisitCause.AnotherApp,
                    app.Id, package, app.Title, fixedText, app.Layer);
            }
            else if (app.Title != visit.WindowTitle)
            {
                Begin(time, PhoneVisitCause.WindowTitleChanged, app.Id, package, app.Title, fixedText, app.Layer);
            }
            else if (fixedText is not null && visit.FixedText is not null && fixedText != visit.FixedText)
            {
                // A new screen in the same window, as in an app drawn by
                // Jetpack Compose. A snapshot with no fixed text, as while
                // a screen is drawn, or a titled pane over the whole
                // window, as a dialog, changes nothing.
                Begin(time, PhoneVisitCause.FixedTextChanged, app.Id, package, app.Title, fixedText, app.Layer);
            }
            else if (fixedText is not null && visit.FixedText is null && visit.Cause == PhoneVisitCause.FixedTextGone)
            {
                // A fixed text again after a visit with none.
                Begin(time, PhoneVisitCause.FixedTextChanged, app.Id, package, app.Title, fixedText, app.Layer);
            }
            else if (fixedText is null && firstText is not null && visit.FixedText is not null && _gone is null)
            {
                // The fixed text went away but the window still has text,
                // as when every text of the next screen is in a scrolling
                // container (the client's Profile screen, 20261112-154851).
                // A visit starts here only if it stays away for
                // QuietNanoseconds, as a screen being drawn can lose its
                // fixed text for a moment.
                _gone = (time, firstText);
            }

            _currentLayer = app.Layer;
            if (_current!.FixedText is null && _current.Cause != PhoneVisitCause.FixedTextGone)
            {
                _current.FixedText = fixedText;
            }

            if (_current.FixedText is null)
            {
                _current.FirstText ??= firstText;
            }

            _current._listings.Add((item, app.Bounds, app.TransitionTime, _evidence.GetValueOrDefault(app.Id)));
            if (!_listingTimes.TryGetValue(app.Id, out var times))
            {
                _listingTimes[app.Id] = times = [];
            }

            times.Add(time);
            _current._snapshots.Add(item);
            UpdateStates(time, app, applications);
        }

        public (IReadOnlyList<PhoneVisit>, IReadOnlyList<PhoneScreen>, IReadOnlyList<PhoneApp>) Finish()
        {
            EndGone(duration);
            End(duration);
            foreach (var visit in _visits)
            {
                Settle(visit);
            }

            // A title (window title, window-state text or first heading)
            // shared by visits with different first fixed texts names no
            // one screen: those visits are named by their fixed texts.
            static (string? Title, PhoneTitleSource Source) BaseTitle(PhoneVisit visit) =>
                visit.WindowTitle is { } windowTitle ? (windowTitle, PhoneTitleSource.WindowTitle)
                : visit.StateText is { } stateText ? (stateText, PhoneTitleSource.WindowStateText)
                : visit.FirstHeading is { } heading ? (heading, PhoneTitleSource.FirstHeading)
                : (null, PhoneTitleSource.Untitled);
            static string? TextOf(PhoneVisit visit) => visit.FixedText ?? visit.FirstText;
            var fixedTexts = _visits
                .Where(visit => TextOf(visit) is not null)
                .GroupBy(visit => (visit.Package, BaseTitle(visit).Title))
                .ToDictionary(group => group.Key, group => group.Select(visit => TextOf(visit)!).Distinct(StringComparer.Ordinal).Count());

            var screens = new List<PhoneScreen>();
            var apps = new List<PhoneApp>();
            var untitled = 0;
            for (var index = 0; index < _visits.Count; index++)
            {
                var visit = _visits[index];
                var (title, source) = BaseTitle(visit);
                string? sharedTitle = null;
                var sharedBy = 0;
                if (TextOf(visit) is { } fixedText &&
                    (title is null || fixedTexts.GetValueOrDefault((visit.Package, title)) > 1))
                {
                    sharedTitle = title;
                    sharedBy = title is null ? 0 : fixedTexts[(visit.Package, title)];
                    (title, source) = (fixedText, visit.FixedText is null ? PhoneTitleSource.FirstText : PhoneTitleSource.FirstFixedText);
                }

                var signature = title is null ? visit.ViewIdSignature : null;

                // The same screen: the same app and title (or, untitled,
                // view IDs), and the same activity, or an activity unknown
                // on either side, as for the window the recording began in.
                var screen = screens.FirstOrDefault(candidate =>
                    candidate.Package == visit.Package &&
                    candidate.Title == title &&
                    candidate.SharedTitle == sharedTitle &&
                    candidate.ViewIdSignature == signature &&
                    (candidate.Activity is null || visit.Activity is null || candidate.Activity == visit.Activity));
                if (screen is null)
                {
                    screen = new PhoneScreen(screens.Count + 1, visit.Package, visit.Activity, title, source, signature, sharedTitle, sharedBy);
                    if (title is null)
                    {
                        screen.UntitledNumber = ++untitled;
                    }

                    screens.Add(screen);
                    var app = apps.FirstOrDefault(candidate => candidate.Package == visit.Package);
                    if (app is null)
                    {
                        app = new PhoneApp(visit.Package);
                        apps.Add(app);
                    }

                    app._screens.Add(screen);
                }
                else
                {
                    screen.Activity ??= visit.Activity;
                }

                visit.Screen = screen;
                screen._visits.Add(visit);
            }

            return (_visits, screens, apps);
        }

        // The app's window: of the application windows that are not
        // dialogs, sheets or popups by their window-state class, those
        // covering at least half the screen, the topmost; else the current
        // one while it is listed; else the largest.
        private Window? AppWindow(long time, List<Window> applications)
        {
            var candidates = applications
                .Where(window => !IsStateClass(_states.TryGetValue(window.Id, out var state) ? state.State.ClassName : null))
                .ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            var area = screenAt(time) is { } screen
                ? (double)screen.Width * screen.Height
                : candidates.Max(window => Area(window.Bounds));
            var large = candidates
                .Where(window => area > 0 && Area(window.Bounds) >= AppWindowShare * area)
                .OrderByDescending(window => window.Layer)
                .ThenByDescending(window => window.Active)
                .FirstOrDefault();
            return large
                ?? candidates.FirstOrDefault(window => window.Id == _current?.WindowId)
                ?? candidates.OrderByDescending(window => Area(window.Bounds)).First();
        }

        // A visit to a new window starts when its window-state event gave
        // its class name and text, if that was after the current visit
        // began, as the event comes before the snapshot that lists it.
        private long StartOf(int windowId, long time) =>
            _states.TryGetValue(windowId, out var state) && state.Since <= time &&
            (_current is null || state.Since > _current.Start)
                ? state.Since
                : time;

        private void Begin(long start, PhoneVisitCause cause, int windowId, string package, string? windowTitle, string? fixedText, int layer)
        {
            var state = _states.TryGetValue(windowId, out var given) ? given.State : null;
            var activity = state?.ClassName;
            var text = state?.Text;
            if (_current is { } current &&
                current.WindowId == windowId && current.Package == package &&
                current.Activity == activity && current.StateText == text && current.WindowTitle == windowTitle &&
                cause != PhoneVisitCause.FixedTextGone &&
                (fixedText is null || current.FixedText == fixedText ||
                 (current.FixedText is null && current.Cause != PhoneVisitCause.FixedTextGone)))
            {
                return;
            }

            End(start);
            _current = new PhoneVisit(_visits.Count + 1, start, cause, windowId, package, activity, windowTitle, text)
            {
                FixedText = fixedText,
            };
            _currentLayer = layer;
            _visits.Add(_current);
        }

        // Ends a wait for the fixed text to come back (_gone): when it was
        // away for QuietNanoseconds until a time, a visit to the window
        // with no fixed text starts where it went away, and takes the
        // snapshots, listings and states of the current visit from then.
        private void EndGone(long time)
        {
            if (_gone is not { } gone || _current is not { } from)
            {
                _gone = null;
                return;
            }

            _gone = null;
            if (time - gone.Start < QuietNanoseconds)
            {
                return;
            }

            var start = gone.Start;
            var snapshots = from._snapshots.Where(item => item.MonotonicNanoseconds >= start).ToList();
            from._snapshots.RemoveAll(item => item.MonotonicNanoseconds >= start);
            var listings = from._listings.Where(listing => listing.Event.MonotonicNanoseconds >= start).ToList();
            from._listings.RemoveAll(listing => listing.Event.MonotonicNanoseconds >= start);
            var states = from._states.Where(state => state.Start >= start).ToList();
            from._states.RemoveAll(state => state.Start >= start);
            var open = _open.Where(pair => pair.Value.Start >= start).ToList();
            foreach (var (key, _) in open)
            {
                _open.Remove(key);
            }

            var layer = _currentLayer;
            Begin(start, PhoneVisitCause.FixedTextGone, from.WindowId, from.Package, from.WindowTitle, null, layer);
            var visit = _current!;
            visit.FirstText = gone.FirstText;
            visit._snapshots.AddRange(snapshots);
            visit._listings.AddRange(listings);
            visit._states.AddRange(states);
            foreach (var (key, state) in open)
            {
                _open[key] = state;
            }
        }

        // Closes the current visit and its open states.
        private void End(long end)
        {
            if (_current is not { } visit)
            {
                return;
            }

            visit.End = Math.Max(visit.Start, end);
            foreach (var open in _open.Values)
            {
                visit._states.Add(new PhoneScreenState(open.Start, visit.End, open.Kind, open.Name, open.NamedBy, open.Package, open.WindowId, open.FixedText));
            }

            _open.Clear();
            visit._states.Sort((a, b) => a.Start.CompareTo(b.Start));
            _current = null;
        }

        // A visit's settled snapshots: each listing of its window with the
        // bounds and transition time of every later listing in the visit,
        // and with no listing of the window, in any visit, within
        // QuietNanoseconds after it. Then its heading and view IDs, from
        // the settled ones first.
        private void Settle(PhoneVisit visit)
        {
            var listings = visit._listings;
            var same = new bool[listings.Count];
            for (var index = listings.Count - 1; index >= 0; index--)
            {
                same[index] = index == listings.Count - 1 ||
                    (same[index + 1] &&
                     listings[index].Bounds == listings[index + 1].Bounds &&
                     listings[index].TransitionTime == listings[index + 1].TransitionTime);
            }

            var settled = new bool[listings.Count];
            for (var index = 0; index < listings.Count; index++)
            {
                var time = listings[index].Event.MonotonicNanoseconds;
                settled[index] = same[index] && !ListedWithin(visit.WindowId, time);
                if (settled[index])
                {
                    visit._settled.Add(listings[index].Event);
                }
            }

            var evidence = listings.Where((_, index) => settled[index]).Select(listing => listing.Evidence)
                .Concat(listings.Select(listing => listing.Evidence))
                .OfType<PhoneScreenEvidence>()
                .ToList();
            visit.FirstHeading = evidence.Select(item => item.FirstHeading).FirstOrDefault(heading => heading is not null);
            visit.ViewIdSignature = evidence.Select(item => item.ViewIdSignature).FirstOrDefault(signature => signature is not null);
        }

        // Whether the window was listed again within QuietNanoseconds after a time.
        private bool ListedWithin(int windowId, long time)
        {
            if (!_listingTimes.TryGetValue(windowId, out var times))
            {
                return false;
            }

            var index = times.BinarySearch(time);
            index = index < 0 ? ~index : index + 1;
            while (index < times.Count && times[index] == time)
            {
                index++;
            }

            return index < times.Count && times[index] - time < QuietNanoseconds;
        }

        // The visit's states at a snapshot: application windows above the
        // app's window, or dialogs by their class, and panes in the app's
        // window by their titles.
        private void UpdateStates(long time, Window app, List<Window> applications)
        {
            var now = new Dictionary<string, (string Kind, string Name, PhoneTitleSource NamedBy, string? Package, int? WindowId, string? FixedText)>();
            foreach (var window in applications.Where(window => window.Id != app.Id))
            {
                var state = _states.TryGetValue(window.Id, out var given) ? given.State : null;
                if (window.Layer <= app.Layer && !IsStateClass(state?.ClassName))
                {
                    continue;
                }

                var evidence = _evidence.GetValueOrDefault(window.Id);
                var (name, namedBy) = window.Title is { } title ? (title, PhoneTitleSource.WindowTitle)
                    : state?.Text is { } text ? (text, PhoneTitleSource.WindowStateText)
                    : evidence?.FirstHeading is { } heading ? (heading, PhoneTitleSource.FirstHeading)
                    : ("untitled", PhoneTitleSource.Untitled);
                var kind = state?.ClassName is { } className && IsStateClass(className)
                    ? KindOf(className)
                    : "window";
                now["window:" + window.Id.ToString(CultureInfo.InvariantCulture)] =
                    (kind, name, namedBy, evidence?.Package ?? state?.Package, window.Id, evidence?.FixedText);
            }

            var appEvidence = _evidence.GetValueOrDefault(app.Id);
            foreach (var pane in appEvidence?.PaneTitles ?? [])
            {
                now["pane:" + pane] = ("pane", pane, PhoneTitleSource.PaneTitle, null, null, appEvidence!.PaneFixedTexts.GetValueOrDefault(pane));
            }

            foreach (var key in _open.Keys.Where(key => !now.ContainsKey(key)).ToList())
            {
                var open = _open[key];
                _current!._states.Add(new PhoneScreenState(open.Start, time, open.Kind, open.Name, open.NamedBy, open.Package, open.WindowId, open.FixedText));
                _open.Remove(key);
            }

            foreach (var (key, state) in now)
            {
                if (_open.TryGetValue(key, out var open))
                {
                    if (open.NamedBy == PhoneTitleSource.Untitled && state.NamedBy != PhoneTitleSource.Untitled)
                    {
                        open.Name = state.Name;
                        open.NamedBy = state.NamedBy;
                    }

                    open.FixedText ??= state.FixedText;
                    continue;
                }

                _open[key] = new OpenState(time, state.Kind, state.Name, state.NamedBy, state.Package == _current!.Package ? null : state.Package, state.WindowId, state.FixedText);
            }
        }

        private static string KindOf(string className)
        {
            var name = PhoneScreen.ShortClass(className);
            return name.Contains("BottomSheet", StringComparison.Ordinal) ? "bottom sheet"
                : name.Contains("Popup", StringComparison.Ordinal) ? "popup"
                : "dialog";
        }

        private static double Area(PhoneBounds? bounds) =>
            bounds is null || bounds.IsEmpty ? 0 : (double)bounds.Width * bounds.Height;
    }
}

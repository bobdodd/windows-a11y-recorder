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
    internal PhoneScreen(int number, string package, string? activity, string? title, PhoneTitleSource titleSource, string? viewIdSignature)
    {
        Number = number;
        Package = package;
        Activity = activity;
        Title = title;
        TitleSource = titleSource;
        ViewIdSignature = viewIdSignature;
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

    public string When => $"{WindowsPreferenceTimeline.Clock(Start)} to {WindowsPreferenceTimeline.Clock(End)}";

    public string CauseText => Cause switch
    {
        PhoneVisitCause.FirstWindow => "the recording's first app window",
        PhoneVisitCause.NewWindow => "a new window",
        PhoneVisitCause.WindowStateChanged => "a window-state event",
        PhoneVisitCause.WindowTitleChanged => "the window title changed",
        _ => "another app came to the front",
    };
}

/// <summary>A dialog, menu, sheet or pane over a visit's screen (decision 6).</summary>
public sealed record PhoneScreenState(long Start, long End, string Kind, string Name, PhoneTitleSource NamedBy, string? Package, int? WindowId)
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
        return new PhoneScreenEvidence(package, heading, panes, signature, viewIds.Length);
    }

    public JsonObject ToJson()
    {
        var panes = new JsonArray();
        foreach (var pane in PaneTitles)
        {
            panes.Add(pane);
        }

        return new JsonObject
        {
            ["package"] = Package,
            ["firstHeading"] = FirstHeading,
            ["paneTitles"] = panes,
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
        return new PhoneScreenEvidence(
            ReadString(evidence, "package"),
            ReadString(evidence, "firstHeading"),
            panes,
            ReadString(evidence, "viewIdSignature"),
            ReadInt32(evidence, "viewIdCount") ?? 0);
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
    private sealed class OpenState(long start, string kind, string name, PhoneTitleSource namedBy, string? package, int? windowId)
    {
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

        // The current visit's snapshots of its window, with the window's
        // bounds and transition time.
        private readonly List<(SessionTimelineEvent Event, PhoneBounds? Bounds, long? TransitionTime, PhoneScreenEvidence? Evidence)> _listings = [];

        private readonly Dictionary<string, OpenState> _open = [];

        private PhoneVisit? _current;

        private int _currentLayer;

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
                Begin(time, PhoneVisitCause.WindowStateChanged, state.WindowId, visit.Package, visit.WindowTitle, _currentLayer);
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
            if (_current is not { } visit)
            {
                Begin(StartOf(app.Id, time), PhoneVisitCause.FirstWindow, app.Id, package, app.Title, app.Layer);
            }
            else if (app.Id != visit.WindowId)
            {
                Begin(StartOf(app.Id, time),
                    package == visit.Package ? PhoneVisitCause.NewWindow : PhoneVisitCause.AnotherApp,
                    app.Id, package, app.Title, app.Layer);
            }
            else if (app.Title != visit.WindowTitle)
            {
                Begin(time, PhoneVisitCause.WindowTitleChanged, app.Id, package, app.Title, app.Layer);
            }

            _currentLayer = app.Layer;
            _listings.Add((item, app.Bounds, app.TransitionTime, _evidence.GetValueOrDefault(app.Id)));
            _current!._snapshots.Add(item);
            UpdateStates(time, app, applications);
        }

        public (IReadOnlyList<PhoneVisit>, IReadOnlyList<PhoneScreen>, IReadOnlyList<PhoneApp>) Finish()
        {
            End(duration);
            var screens = new List<PhoneScreen>();
            var apps = new List<PhoneApp>();
            var untitled = 0;
            for (var index = 0; index < _visits.Count; index++)
            {
                var visit = _visits[index];
                var (title, source) = visit.WindowTitle is { } windowTitle ? (windowTitle, PhoneTitleSource.WindowTitle)
                    : visit.StateText is { } stateText ? (stateText, PhoneTitleSource.WindowStateText)
                    : visit.FirstHeading is { } heading ? (heading, PhoneTitleSource.FirstHeading)
                    : ((string?)null, PhoneTitleSource.Untitled);
                var signature = title is null ? visit.ViewIdSignature : null;

                // The same screen: the same app and title (or, untitled,
                // view IDs), and the same activity, or an activity unknown
                // on either side, as for the window the recording began in.
                var screen = screens.FirstOrDefault(candidate =>
                    candidate.Package == visit.Package &&
                    candidate.Title == title &&
                    candidate.ViewIdSignature == signature &&
                    (candidate.Activity is null || visit.Activity is null || candidate.Activity == visit.Activity));
                if (screen is null)
                {
                    screen = new PhoneScreen(screens.Count + 1, visit.Package, visit.Activity, title, source, signature);
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

        private void Begin(long start, PhoneVisitCause cause, int windowId, string package, string? windowTitle, int layer)
        {
            var state = _states.TryGetValue(windowId, out var given) ? given.State : null;
            var activity = state?.ClassName;
            var text = state?.Text;
            if (_current is { } current &&
                current.WindowId == windowId && current.Package == package &&
                current.Activity == activity && current.StateText == text && current.WindowTitle == windowTitle)
            {
                return;
            }

            End(start);
            _current = new PhoneVisit(_visits.Count + 1, start, cause, windowId, package, activity, windowTitle, text);
            _currentLayer = layer;
            _visits.Add(_current);
        }

        // Closes the current visit: its states, its settled snapshots (each
        // listing of its window with the bounds and transition time of
        // every later listing in the visit), and its heading and view IDs
        // from them.
        private void End(long end)
        {
            if (_current is not { } visit)
            {
                return;
            }

            visit.End = Math.Max(visit.Start, end);
            foreach (var open in _open.Values)
            {
                visit._states.Add(new PhoneScreenState(open.Start, visit.End, open.Kind, open.Name, open.NamedBy, open.Package, open.WindowId));
            }

            _open.Clear();
            var settled = new bool[_listings.Count];
            for (var index = _listings.Count - 1; index >= 0; index--)
            {
                settled[index] = index == _listings.Count - 1
                    ? true
                    : settled[index + 1] &&
                      _listings[index].Bounds == _listings[index + 1].Bounds &&
                      _listings[index].TransitionTime == _listings[index + 1].TransitionTime;
            }

            for (var index = 0; index < _listings.Count; index++)
            {
                if (settled[index])
                {
                    visit._settled.Add(_listings[index].Event);
                }
            }

            var evidence = _listings.Where((_, index) => settled[index]).Select(listing => listing.Evidence)
                .Concat(_listings.Select(listing => listing.Evidence))
                .OfType<PhoneScreenEvidence>()
                .ToList();
            visit.FirstHeading = evidence.Select(item => item.FirstHeading).FirstOrDefault(heading => heading is not null);
            visit.ViewIdSignature = evidence.Select(item => item.ViewIdSignature).FirstOrDefault(signature => signature is not null);
            visit._states.Sort((a, b) => a.Start.CompareTo(b.Start));
            _listings.Clear();
            _current = null;
        }

        // The visit's states at a snapshot: application windows above the
        // app's window, or dialogs by their class, and panes in the app's
        // window by their titles.
        private void UpdateStates(long time, Window app, List<Window> applications)
        {
            var now = new Dictionary<string, (string Kind, string Name, PhoneTitleSource NamedBy, string? Package, int? WindowId)>();
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
                    (kind, name, namedBy, evidence?.Package ?? state?.Package, window.Id);
            }

            foreach (var pane in _evidence.TryGetValue(app.Id, out var appEvidence) ? appEvidence.PaneTitles : [])
            {
                now["pane:" + pane] = ("pane", pane, PhoneTitleSource.PaneTitle, null, null);
            }

            foreach (var key in _open.Keys.Where(key => !now.ContainsKey(key)).ToList())
            {
                var open = _open[key];
                _current!._states.Add(new PhoneScreenState(open.Start, time, open.Kind, open.Name, open.NamedBy, open.Package, open.WindowId));
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

                    continue;
                }

                _open[key] = new OpenState(time, state.Kind, state.Name, state.NamedBy, state.Package == _current!.Package ? null : state.Package, state.WindowId);
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

using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>What an input record of the phone is, for the change of context checks (step 14).</summary>
public enum PhoneInputKind
{
    /// <summary>TYPE_VIEW_CLICKED.</summary>
    Clicked,

    /// <summary>TYPE_VIEW_LONG_CLICKED.</summary>
    LongClicked,

    /// <summary>TYPE_VIEW_SELECTED.</summary>
    Selected,

    /// <summary>TYPE_VIEW_FOCUSED: an item took the input focus.</summary>
    Focused,

    /// <summary>TYPE_VIEW_TEXT_CHANGED.</summary>
    TextChanged,

    /// <summary>TYPE_VIEW_ACCESSIBILITY_FOCUSED: a screen reader's focus moved to an item.</summary>
    AccessibilityFocused,

    /// <summary>TYPE_TOUCH_INTERACTION_START, which Android sends only while a screen reader handles touches.</summary>
    TouchInteractionStart,

    /// <summary>A hardware key pressed (input.android.key, \"down\"), from the phone's own buttons or a keyboard.</summary>
    KeyDown,
}

/// <summary>
/// One input record of the phone: an accessibility event of one of the
/// kinds the change of context checks read, with the window, class, words
/// and bounds of its source item, or a hardware key pressed, with its
/// name, such as KEY_TAB.
/// </summary>
public sealed record PhoneInput(
    long Time,
    PhoneInputKind Kind,
    int? WindowId,
    string? Package,
    string? ClassName,
    string? Text,
    PhoneBounds? Bounds,
    bool Checkable,
    bool Editable,
    string? Key);

/// <summary>
/// The phone's input records through a recording, for the change of
/// context checks (android-checks.md, step 14); the playback index keeps
/// them whole.
/// </summary>
public sealed class PhoneInputTimeline
{
    public const int ClickedEventType = 0x1;
    public const int LongClickedEventType = 0x2;
    public const int SelectedEventType = 0x4;
    public const int FocusedEventType = 0x8;
    public const int TextChangedEventType = 0x10;
    public const int TouchInteractionStartEventType = 0x100000;

    public PhoneInputTimeline(IEnumerable<(long Time, JsonElement Payload)> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var all = new List<PhoneInput>();
        foreach (var (time, payload) in records)
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (PhoneScreenEvidence.ReadInt32(payload, "eventType") is { } type)
            {
                PhoneInputKind? kind = type switch
                {
                    ClickedEventType => PhoneInputKind.Clicked,
                    LongClickedEventType => PhoneInputKind.LongClicked,
                    SelectedEventType => PhoneInputKind.Selected,
                    FocusedEventType => PhoneInputKind.Focused,
                    TextChangedEventType => PhoneInputKind.TextChanged,
                    PhoneAccessibilityTimeline.FocusedEventType => PhoneInputKind.AccessibilityFocused,
                    TouchInteractionStartEventType => PhoneInputKind.TouchInteractionStart,
                    _ => null,
                };
                if (kind is not { } known)
                {
                    continue;
                }

                var source = payload.TryGetProperty("source", out var item) && item.ValueKind == JsonValueKind.Object ? item : (JsonElement?)null;
                var words = new List<string>();
                if (payload.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.Array)
                {
                    words.AddRange(text.EnumerateArray()
                        .Where(part => part.ValueKind == JsonValueKind.String)
                        .Select(part => part.GetString() ?? string.Empty)
                        .Where(part => part.Length > 0));
                }

                if (words.Count == 0 && PhoneScreenEvidence.ReadString(payload, "contentDescription") is { Length: > 0 } description)
                {
                    words.Add(description);
                }

                all.Add(new PhoneInput(
                    time,
                    known,
                    PhoneScreenEvidence.ReadInt32(payload, "windowId"),
                    PhoneScreenEvidence.ReadString(payload, "packageName"),
                    PhoneScreenEvidence.ReadString(payload, "className") ?? (source is { } node ? PhoneScreenEvidence.ReadString(node, "className") : null),
                    words.Count == 0 ? null : string.Join(" ", words),
                    source is { } withBounds ? BoundsOf(withBounds) : null,
                    source is { } checkable && PhoneScreenEvidence.ReadBoolean(checkable, "checkable"),
                    source is { } editable && PhoneScreenEvidence.ReadBoolean(editable, "editable"),
                    null));
            }
            else if (PhoneScreenEvidence.ReadString(payload, "key") is { } key &&
                PhoneScreenEvidence.ReadString(payload, "action") == "down")
            {
                all.Add(new PhoneInput(time, PhoneInputKind.KeyDown, null, null, null, null, null, false, false, key));
            }
        }

        All = [.. all.OrderBy(input => input.Time)];
    }

    public static PhoneInputTimeline Empty { get; } = new([]);

    /// <summary>The input records, in time order.</summary>
    public IReadOnlyList<PhoneInput> All { get; }

    /// <summary>
    /// Whether a record is one the change of context checks read, which the
    /// playback index keeps whole: an accessibility event of a kind in
    /// <see cref="PhoneInputKind"/>, or a hardware key.
    /// </summary>
    public static bool IsInputRecord(string channel, string eventType, JsonElement payload) =>
        (channel == AndroidTouchRecords.KeyChannel && eventType == AndroidTouchRecords.KeyEventType) ||
        (channel == AndroidAccessibilityRecords.EventsChannel &&
            eventType == AndroidAccessibilityRecords.EventType &&
            payload.ValueKind == JsonValueKind.Object &&
            PhoneScreenEvidence.ReadInt32(payload, "eventType") is ClickedEventType or LongClickedEventType or SelectedEventType or
                FocusedEventType or TextChangedEventType or PhoneAccessibilityTimeline.FocusedEventType or TouchInteractionStartEventType);

    internal static PhoneBounds? BoundsOf(JsonElement item) =>
        item.TryGetProperty("bounds", out var box) && box.ValueKind == JsonValueKind.Object &&
        PhoneScreenEvidence.ReadInt32(box, "left") is { } left && PhoneScreenEvidence.ReadInt32(box, "top") is { } top &&
        PhoneScreenEvidence.ReadInt32(box, "right") is { } right && PhoneScreenEvidence.ReadInt32(box, "bottom") is { } bottom
            ? new PhoneBounds(left, top, right, bottom)
            : null;
}

/// <summary>
/// Step 14 of android-checks.md, changes of context (WCAG 3.2.1 On Focus
/// and 3.2.2 On Input, which WCAG2ICT applies to software as written),
/// three checks, each one discrete issue (owner, 2026-11-13):
/// <list type="bullet">
/// <item>a change of context on focus, an error: an item took the focus
/// from the keyboard (Tab or an arrow key) or by a tap on a field, and a
/// change of context followed with nothing activated;</item>
/// <item>a change of context on TalkBack focus, an error: TalkBack's focus
/// moved to an item after a gesture or a key, and a change of context
/// followed with no double tap, touch or key;</item>
/// <item>a change of context on input, a warning: an item's setting was
/// changed (text typed, a checkbox, switch or radio button changed, a
/// choice made in a spinner, or a slider moved) and a change of context
/// followed with no further touch or key; 3.2.2 allows it when the user
/// was told before using the item, which the recording cannot show.</item>
/// </list>
/// A change of context is a new screen or another app coming to the front
/// (a new visit to a different screen), a window over the app that took
/// the focus or a titled pane appearing (a new state of the visit), or the
/// input focus moving to another item by itself. It follows an action when
/// it comes within 1.5 seconds and before any other touch, key or click.
/// The checks run only with touches recorded, as without them a tap that
/// caused the change cannot be told from none.
/// </summary>
public static class PhoneContextChecks
{
    /// <summary>A change of context this soon after the action is taken as the action's.</summary>
    public const long FollowedWithinNanoseconds = 1_500_000_000;

    /// <summary>The input focus taken this soon after a key or a tap on a field is the focus the key or tap gave.</summary>
    public const long FocusGivenWithinNanoseconds = 500_000_000;

    /// <summary>TalkBack's focus moving this soon after a gesture or key ended is taken as the user's.</summary>
    public const long MovedByUserWithinNanoseconds = 1_000_000_000;

    /// <summary>TalkBack's focus moving this soon after a change of context is TalkBack placing it on the new content.</summary>
    public const long PlacedAfterChangeNanoseconds = 1_000_000_000;

    /// <summary>A click event this soon after a finger lifted is that touch's click, not another action.</summary>
    public const long ClickOfTouchNanoseconds = 500_000_000;

    /// <summary>A touch within this long of the start of a touch interaction was handled by a screen reader.</summary>
    public const long ScreenReaderTouchNanoseconds = 300_000_000;

    // A finger that went this far, in screen pixels, did not tap.
    private const double TapMovement = 40;

    // AccessibilityNodeInfo.AccessibilityAction.ACTION_SET_PROGRESS.
    private const int ActionSetProgress = 0x0102003d;

    // The phone's own buttons and keys that do not move the focus or
    // activate anything in an app.
    private static readonly HashSet<string> IgnoredKeys = new(StringComparer.Ordinal)
    {
        "KEY_VOLUMEUP", "KEY_VOLUMEDOWN", "KEY_POWER", "KEY_WAKEUP", "KEY_SLEEP", "KEY_CAMERA",
        "KEY_MUTE", "KEY_LEFTSHIFT", "KEY_RIGHTSHIFT", "KEY_LEFTCTRL", "KEY_RIGHTCTRL",
        "KEY_LEFTALT", "KEY_RIGHTALT", "KEY_LEFTMETA", "KEY_RIGHTMETA", "KEY_CAPSLOCK",
    };

    // The keys that move the focus.
    private static readonly HashSet<string> FocusKeys = new(StringComparer.Ordinal)
    {
        "KEY_TAB", "KEY_UP", "KEY_DOWN", "KEY_LEFT", "KEY_RIGHT",
    };

    // The classes whose click changes their setting (checked or not).
    private static readonly string[] CheckableClasses =
    [
        "CheckBox", "RadioButton", "Switch", "ToggleButton", "CompoundButton", "CheckedTextView", "SwitchCompat", "SwitchMaterial",
    ];

    // The classes whose selection is a setting: a choice made or a value moved.
    private static readonly string[] SettingSelectionClasses = ["Spinner", "SeekBar", "RatingBar", "Slider"];

    public sealed record Result(
        IReadOnlyList<PhoneFinding> Findings,
        bool Run,
        int Changes,
        int FocusMoves,
        int ScreenReaderFocusMoves,
        int SettingChanges,
        IReadOnlyList<string> Problems);

    private enum ActionKind
    {
        /// <summary>A touch, key or click that may activate something; a change after it is its own.</summary>
        Activation,

        /// <summary>The focus moved by a key or a tap on a field (3.2.1).</summary>
        Focus,

        /// <summary>TalkBack's focus moved by the user (3.2.1).</summary>
        ScreenReaderFocus,

        /// <summary>An item's setting changed (3.2.2).</summary>
        Setting,
    }

    private sealed record Action(long Time, ActionKind Kind, int? WindowId, PhoneBounds? Bounds, string? ClassName, string How);

    private sealed record Change(long Time, string What, bool FocusMove, int? WindowId, PhoneBounds? Bounds, string? ClassName);

    public static Result Run(
        PhoneScreenTimeline screens,
        PhoneAccessibilityTimeline trees,
        PhoneTouchTimeline touches,
        PhoneInputTimeline inputs,
        ISessionEventRecordSource source,
        PhoneSnapshotPictures pictures,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(trees);
        ArgumentNullException.ThrowIfNull(touches);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pictures);
        var problems = new List<string>();
        if (!screens.Recorded || !touches.Recorded)
        {
            return new Result([], false, 0, 0, 0, 0, problems);
        }

        var visits = screens.Visits.OrderBy(visit => visit.Start).ToList();
        var changes = Changes(visits, trees);
        var actions = new List<Action>();
        var windows = new Dictionary<string, PhoneCheckWindow?>(StringComparer.Ordinal);
        var screenReaderTouches = inputs.All.Where(input => input.Kind == PhoneInputKind.TouchInteractionStart).Select(input => input.Time).ToList();
        var gestures = touches.Gestures();
        // Each gesture's action, by gesture, so the click event a touch
        // caused is taken as that touch's, not as another action.
        var actionOfGesture = new Dictionary<PhoneGesture, int>();
        foreach (var gesture in gestures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (screenReaderTouches.Any(time => Math.Abs(time - gesture.Down) <= ScreenReaderTouchNanoseconds))
            {
                // A screen reader's gesture: what it does is in the events.
                actionOfGesture[gesture] = actions.Count;
                actions.Add(new Action(gesture.Up, ActionKind.Activation, null, null, null, "a TalkBack gesture"));
                continue;
            }

            if (Touched(gesture, trees, source, windows, problems) is { } touched)
            {
                actionOfGesture[gesture] = actions.Count;
                actions.Add(touched);
            }
            else
            {
                actionOfGesture[gesture] = -1;
            }
        }

        foreach (var input in inputs.All)
        {
            var listed = input.WindowId is { } id ? trees.WindowsAt(input.Time).FirstOrDefault(window => window.Id == id) : null;
            var inApp = listed is null || listed.TypeName == PhoneListedWindow.ApplicationType;
            var name = input.ClassName ?? string.Empty;
            switch (input.Kind)
            {
                case PhoneInputKind.KeyDown when input.Key is { } key && !IgnoredKeys.Contains(key):
                    actions.Add(FocusKeys.Contains(key)
                        ? new Action(input.Time, ActionKind.Focus, null, null, null, $"the {KeyName(key)} key moved the focus to it")
                        : new Action(input.Time, ActionKind.Activation, null, null, null, $"the {KeyName(key)} key"));
                    break;
                case PhoneInputKind.TextChanged when inApp:
                    actions.Add(new Action(input.Time, ActionKind.Setting, input.WindowId, input.Bounds, input.ClassName, "text was typed in it"));
                    break;
                case PhoneInputKind.Selected when inApp && SettingSelectionClasses.Any(part => name.Contains(part, StringComparison.Ordinal)):
                    actions.Add(new Action(input.Time, ActionKind.Setting, input.WindowId, input.Bounds, input.ClassName,
                        name.Contains("Spinner", StringComparison.Ordinal) ? "a choice was made in it" : "its value was changed"));
                    break;
                case PhoneInputKind.Clicked or PhoneInputKind.LongClicked
                    when gestures.LastOrDefault(gesture => gesture.Down <= input.Time && input.Time <= gesture.Up + ClickOfTouchNanoseconds) is { } touch:
                    // The touch's own click: it changed a setting when the
                    // item clicked is checkable, though the touch was not
                    // seen to land on a checkable item.
                    if (inApp && input.Kind == PhoneInputKind.Clicked && IsCheckable(input) &&
                        actionOfGesture.TryGetValue(touch, out var at) && at >= 0 && actions[at].Kind == ActionKind.Activation)
                    {
                        actions[at] = new Action(actions[at].Time, ActionKind.Setting, input.WindowId, input.Bounds, input.ClassName,
                            actions[at].How == "a TalkBack gesture" ? "TalkBack's double tap changed whether it is checked" : "a tap changed whether it is checked");
                    }

                    break;
                case PhoneInputKind.Clicked or PhoneInputKind.LongClicked when inApp:
                    actions.Add(input.Kind == PhoneInputKind.Clicked && IsCheckable(input)
                        ? new Action(input.Time, ActionKind.Setting, input.WindowId, input.Bounds, input.ClassName, "it was clicked, changing whether it is checked")
                        : new Action(input.Time, ActionKind.Activation, input.WindowId, input.Bounds, input.ClassName, "a click"));
                    break;
                case PhoneInputKind.AccessibilityFocused when inApp && MovedByUser(input.Time, gestures, inputs, changes):
                    actions.Add(new Action(input.Time, ActionKind.ScreenReaderFocus, input.WindowId, input.Bounds, input.ClassName, "TalkBack's focus moved to it"));
                    break;
            }
        }

        actions = [.. actions.OrderBy(action => action.Time)];
        // The input focus moving to an item, as a change of context unless
        // it is the focus a key or tap on a field gave.
        var focusMoves = inputs.All
            .Where(input => input.Kind == PhoneInputKind.Focused &&
                (input.WindowId is not { } id || trees.WindowsAt(input.Time).FirstOrDefault(window => window.Id == id) is not { } listed ||
                    listed.TypeName == PhoneListedWindow.ApplicationType))
            .ToList();

        var findings = new Dictionary<(PhoneVisit, string, string), PhoneFinding>();
        int focusTriggers = 0, readerTriggers = 0, settingTriggers = 0;
        var changesFollowed = 0;
        for (var index = 0; index < actions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = actions[index];
            switch (action.Kind)
            {
                case ActionKind.Focus:
                    focusTriggers++;
                    break;
                case ActionKind.ScreenReaderFocus:
                    readerTriggers++;
                    break;
                case ActionKind.Setting:
                    settingTriggers++;
                    break;
                default:
                    continue;
            }

            var until = Math.Min(
                action.Time + FollowedWithinNanoseconds,
                index + 1 < actions.Count ? actions[index + 1].Time : long.MaxValue);
            var given = action.Kind == ActionKind.Focus
                ? focusMoves.FirstOrDefault(input => input.Time >= action.Time && input.Time <= action.Time + FocusGivenWithinNanoseconds)
                : null;
            var followed = changes
                .Where(change => change.Time > action.Time && change.Time <= until)
                .Concat(focusMoves
                    .Where(input => input.Time > action.Time && input.Time <= until && !ReferenceEquals(input, given) &&
                        !(input.Bounds is not null && input.Bounds == (action.Bounds ?? given?.Bounds) && input.ClassName == (action.ClassName ?? given?.ClassName)))
                    .Select(input => new Change(input.Time, $"the input focus moved to {Described(input)}", true, input.WindowId, input.Bounds, input.ClassName)))
                .OrderBy(change => change.FocusMove)
                .ThenBy(change => change.Time)
                .FirstOrDefault();
            if (followed is null ||
                visits.LastOrDefault(visit => visit.Start <= action.Time) is not { } visit ||
                visits.LastOrDefault(visit => visit.Start < followed.Time) != visit)
            {
                continue;
            }

            changesFollowed++;
            var windowId = action.WindowId ?? given?.WindowId ?? visit.WindowId;
            var item = ItemOf(trees, source, windows, windowId, action.Time, action.Bounds ?? given?.Bounds, action.ClassName ?? given?.ClassName, problems);
            if (item is null)
            {
                windowId = visit.WindowId;
                item = ItemOf(trees, source, windows, windowId, action.Time, null, null, problems);
            }

            if (item is not { } found)
            {
                continue;
            }

            var (check, level, what) = action.Kind switch
            {
                ActionKind.Focus => (PhoneCheckNames.ContextOnFocus, PhoneCheckLevel.Error, "with nothing activated"),
                ActionKind.ScreenReaderFocus => (PhoneCheckNames.ContextOnScreenReaderFocus, PhoneCheckLevel.Error, "with no double tap, touch or key"),
                _ => (PhoneCheckNames.ContextOnInput, PhoneCheckLevel.Warning, "with no further touch or key"),
            };
            var seconds = ((followed.Time - action.Time) / 1e9).ToString("0.00", CultureInfo.CurrentCulture);
            var measured = $"{action.How}, and {seconds} seconds later, {what}, {followed.What}";
            measured += action.Kind switch
            {
                ActionKind.Focus => ", a change of context on focus",
                ActionKind.ScreenReaderFocus => ", a change of context on a screen reader's focus",
                _ => ", a change of context on input, which is allowed only when the user was told of it before using the item; look for such advice",
            };
            var key = (visit, check, found.Item.Identity);
            if (!findings.TryGetValue(key, out var finding))
            {
                findings[key] = finding = new PhoneFinding(check, level, visit, found.Item.Identity, found.Item.Label);
            }

            var shownAt = pictures.Between(action.Time, followed.Time)?.Time ?? action.Time;
            finding._times.Add(new PhoneFindingTime(
                shownAt, visit, found.Taking, found.Taking, windowId, found.Item.Bounds, found.Item.TreeKey, measured,
                PhoneCheckRun.StateAt(visit, action.Time)));
        }

        return new Result(
            [.. findings.Values.OrderBy(finding => finding.Times[0].Time)],
            true,
            changesFollowed,
            focusTriggers,
            readerTriggers,
            settingTriggers,
            problems);
    }

    // The changes of context the screens show: a new visit to a different
    // screen, and a window over the app that took the focus, or a titled
    // pane, appearing during a visit.
    private static List<Change> Changes(List<PhoneVisit> visits, PhoneAccessibilityTimeline trees)
    {
        var changes = new List<Change>();
        for (var index = 1; index < visits.Count; index++)
        {
            var (before, visit) = (visits[index - 1], visits[index]);
            if (visit.Screen == before.Screen)
            {
                continue;
            }

            changes.Add(new Change(
                visit.Start,
                visit.Package != before.Package
                    ? $"another app, {visit.Package}, came to the front, with its screen '{visit.Screen.Name}'"
                    : $"the screen '{visit.Screen.Name}' opened",
                false, visit.WindowId, null, null));
        }

        foreach (var visit in visits)
        {
            foreach (var state in visit.States.Where(state => state.Start > visit.Start))
            {
                if (state.WindowId is { } id &&
                    trees.WindowsAt(state.Start).FirstOrDefault(window => window.Id == id) is { } listed &&
                    !listed.Active && !listed.Focused)
                {
                    // A window that did not take the focus, such as a list
                    // of suggestions, changes content, not context.
                    continue;
                }

                var named = state.NamedBy == PhoneTitleSource.Untitled ? ", untitled," : $" '{state.Name}'";
                changes.Add(new Change(state.Start, $"the {state.Kind}{named} appeared", false, state.WindowId, null, null));
            }
        }

        return [.. changes.OrderBy(change => change.Time)];
    }

    // Whether TalkBack's focus moving at a time was the user's: a gesture
    // on the screen or a key ended shortly before, or a finger was on the
    // screen, and it was not TalkBack placing its focus on content a
    // change of context had just shown.
    private static bool MovedByUser(long time, IReadOnlyList<PhoneGesture> gestures, PhoneInputTimeline inputs, List<Change> changes) =>
        (gestures.Any(gesture => gesture.Down <= time && time <= gesture.Up + MovedByUserWithinNanoseconds) ||
            inputs.All.Any(input => input.Kind == PhoneInputKind.KeyDown && input.Key is { } key && !IgnoredKeys.Contains(key) &&
                input.Time <= time && time <= input.Time + MovedByUserWithinNanoseconds)) &&
        !changes.Any(change => change.Time <= time && time <= change.Time + PlacedAfterChangeNanoseconds);

    // What a touch was on: a field (the focus, 3.2.1), a checkable item or a
    // slider (a setting, 3.2.2), the on-screen keyboard (typing, which is
    // not an action here: what it typed is in the text events), or else
    // anything else, which may activate something.
    private static Action? Touched(
        PhoneGesture gesture,
        PhoneAccessibilityTimeline trees,
        ISessionEventRecordSource source,
        Dictionary<string, PhoneCheckWindow?> windows,
        List<string> problems)
    {
        var (x, y) = (gesture.X, gesture.Y);
        if (trees.ScreenAt(gesture.Down) is { } screen && gesture.ScreenWidth > 0 && gesture.ScreenHeight > 0 &&
            (screen.Width != gesture.ScreenWidth || screen.Height != gesture.ScreenHeight))
        {
            x = x * screen.Width / gesture.ScreenWidth;
            y = y * screen.Height / gesture.ScreenHeight;
        }

        var top = trees.WindowsAt(gesture.Down)
            .Where(window => window.Bounds is { } box && Contains(box, x, y))
            .OrderByDescending(window => window.Layer)
            .FirstOrDefault();
        if (top is null)
        {
            return new Action(gesture.Up, ActionKind.Activation, null, null, null, "a touch");
        }

        if (top.TypeName == PhoneListedWindow.InputMethodType)
        {
            return null;
        }

        if (top.TypeName != PhoneListedWindow.ApplicationType)
        {
            return new Action(gesture.Up, ActionKind.Activation, top.Id, null, null, "a touch on the system's bars");
        }

        var window = WindowAt(trees, source, windows, top.Id, gesture.Down, problems);
        var hit = window?.Window.Items
            .Where(item => item.Node.VisibleToUser && item.Node.Enabled && !item.Bounds.IsEmpty && Contains(item.Bounds, x, y) &&
                (item.Node.Clickable || item.Node.LongClickable || item.Node.Checkable || item.Node.Editable || IsSlider(item)))
            .OrderBy(item => (long)item.Bounds.Width * item.Bounds.Height)
            .FirstOrDefault();
        if (hit is null)
        {
            return new Action(gesture.Up, ActionKind.Activation, top.Id, null, null, "a touch");
        }

        var tap = gesture.Moved < TapMovement;
        if (hit.Node.Editable && tap)
        {
            return new Action(gesture.Up, ActionKind.Focus, top.Id, hit.Bounds, hit.Node.ClassName, "a tap on the field gave it the focus");
        }

        if (hit.Node.Checkable && tap)
        {
            var was = hit.Node.Checked == "checked" ? "checked" : "not checked";
            return new Action(gesture.Up, ActionKind.Setting, top.Id, hit.Bounds, hit.Node.ClassName, $"a tap changed its setting (it was {was})");
        }

        // A row that is clicked to change a switch or check box inside
        // it, which is not clickable itself, as in Android's Settings.
        if (tap && window is { } inWindow && inWindow.Window.Items.Where(item => item.Node.Checkable && !item.Node.Clickable && item.Node.VisibleToUser && IsInside(item, hit)).ToList() is [var inner])
        {
            var was = inner.Node.Checked == "checked" ? "checked" : "not checked";
            return new Action(gesture.Up, ActionKind.Setting, top.Id, hit.Bounds, hit.Node.ClassName, $"a tap changed the setting of its {ShortClass(inner.Node.ClassName)} (it was {was})");
        }

        if (IsSlider(hit))
        {
            return new Action(gesture.Up, ActionKind.Setting, top.Id, hit.Bounds, hit.Node.ClassName, "a touch moved its value");
        }

        return new Action(gesture.Up, ActionKind.Activation, top.Id, hit.Bounds, hit.Node.ClassName, "a touch");
    }

    private static bool IsCheckable(PhoneInput input) =>
        input.Checkable || CheckableClasses.Any(part => (input.ClassName ?? string.Empty).EndsWith(part, StringComparison.Ordinal));

    private static bool IsInside(PhoneCheckItem item, PhoneCheckItem ancestor)
    {
        for (var parent = item.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static string ShortClass(string? className) =>
        className is { Length: > 0 } name ? name[(name.LastIndexOf('.') + 1)..] : "item";

    private static bool IsSlider(PhoneCheckItem item) =>
        item.Node.RangeInfo is not null && item.Node.Actions.Any(action => action.Id == ActionSetProgress) ||
        (item.Node.ClassName ?? string.Empty).Contains("SeekBar", StringComparison.Ordinal);

    private static bool Contains(PhoneBounds box, double x, double y) =>
        box.Left <= x && x < box.Right && box.Top <= y && y < box.Bottom;

    // The item an action was on, in the window's items as last taken at
    // its time: the one with the action's bounds and class, or the
    // window's first when none is given; null when the window's items were
    // not taken.
    private static (SessionTimelineEvent Taking, PhoneCheckItem Item)? ItemOf(
        PhoneAccessibilityTimeline trees,
        ISessionEventRecordSource source,
        Dictionary<string, PhoneCheckWindow?> windows,
        int windowId,
        long time,
        PhoneBounds? bounds,
        string? className,
        List<string> problems)
    {
        if (WindowAt(trees, source, windows, windowId, time, problems) is not { } window || window.Window.Items.Count == 0)
        {
            return null;
        }

        if (bounds is null)
        {
            return (window.Taking, window.Window.Items[0]);
        }

        return window.Window.Items.FirstOrDefault(item => item.Bounds == bounds && (className is null || item.Node.ClassName == className)) is { } found
            ? (window.Taking, found)
            : null;
    }

    private static (SessionTimelineEvent Taking, PhoneCheckWindow Window)? WindowAt(
        PhoneAccessibilityTimeline trees,
        ISessionEventRecordSource source,
        Dictionary<string, PhoneCheckWindow?> windows,
        int windowId,
        long time,
        List<string> problems)
    {
        if (trees.TakenAt(time, windowId) is not { } taking)
        {
            return null;
        }

        var key = taking.EventId + "|" + windowId.ToString(CultureInfo.InvariantCulture);
        if (!windows.TryGetValue(key, out var window))
        {
            try
            {
                window = PhoneCheckRun.ReadWindow(source, taking, windowId);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or InvalidOperationException or
                    JsonException or KeyNotFoundException or UnauthorizedAccessException)
            {
                problems.Add($"{WindowsPreferenceTimeline.Clock(taking.MonotonicNanoseconds)}: {exception.Message}");
                window = null;
            }

            windows[key] = window;
        }

        return window is null ? null : (taking, window);
    }

    private static string Described(PhoneInput input)
    {
        var name = input.ClassName is { } className ? PhoneScreen.ShortClass(className) : "an item";
        return input.Text is { Length: > 0 } text
            ? $"{name} \"{(text.Length > 60 ? string.Concat(text.AsSpan(0, 60), "...") : text)}\""
            : name;
    }

    private static string KeyName(string key) => key switch
    {
        "KEY_TAB" => "Tab",
        "KEY_UP" => "Up arrow",
        "KEY_DOWN" => "Down arrow",
        "KEY_LEFT" => "Left arrow",
        "KEY_RIGHT" => "Right arrow",
        "KEY_ENTER" => "Enter",
        "KEY_SPACE" => "Space",
        _ => key.StartsWith("KEY_", StringComparison.Ordinal) ? key[4..] : key,
    };
}

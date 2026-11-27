using System.Globalization;

namespace Recorder.Contracts;

/// <summary>
/// The records of the Android accessibility collector, slice 7 of
/// docs/architecture/android-implementation-plan.md, step 7a: every
/// accessibility event the companion app's service received while
/// recording, with every field the event carries, its extra records, and
/// the item it came from.
///
/// An event's time is <c>AccessibilityEvent.getEventTime()</c>, which
/// Android's AccessibilityManager sets from <c>SystemClock.uptimeMillis()</c>
/// as the app sends it. That clock is the basis of <c>System.nanoTime()</c>,
/// the clock the companion samples, in whole milliseconds; so each record is
/// placed on the session clock through the companion collector's clock fit,
/// with a millisecond more uncertainty, and otherwise at the time the
/// recorder read it.
///
/// Step 7b adds snapshots of the phone's accessibility tree on
/// <see cref="TreeChannel"/>: one of every window when the recording starts
/// and on every window change, and one of each window whose content
/// changed. Each is placed halfway between when the companion started and
/// finished taking it, with half the time it took as more uncertainty.
/// </summary>
public static class AndroidAccessibilityRecords
{
    /// <summary>Every accessibility event, and why the events are missing from part or all of a recording.</summary>
    public const string EventsChannel = "accessibility.android.events";

    public const string EventType = "accessibility-event";

    /// <summary>Why the events are missing from part or all of a recording.</summary>
    public const string UnavailableEventType = "accessibility-events-unavailable";

    /// <summary>Snapshots of the phone's accessibility tree, and why they are missing from part or all of a recording (step 7b).</summary>
    public const string TreeChannel = "accessibility.android.tree";

    public const string TreeEventType = "accessibility-tree";

    /// <summary>Why the tree snapshots are missing from part or all of a recording.</summary>
    public const string UnavailableTreeEventType = "accessibility-tree-unavailable";

    /// <summary>The record's time is halfway between when the companion started and finished taking the snapshot.</summary>
    public const string PhoneSnapshotTimeFlag = "phone-snapshot-time";

    /// <summary>The most items the companion lists for one window (AccessibilityTrees.MAX_ITEMS); more is marked truncated.</summary>
    public const int MaxTreeItems = 10_000;

    /// <summary>Why a snapshot was taken (AccessibilityTrees.java).</summary>
    public static IReadOnlyList<string> TreeReasons { get; } = ["requested", "window-change", "content-change", "focus-change"];

    /// <summary>The record's time is the event's time, as Android set it.</summary>
    public const string PhoneEventTimeFlag = "phone-event-time";

    /// <summary>
    /// The event had no time, so the record's time is when the companion's
    /// service received it.
    /// </summary>
    public const string PhoneReceiptTimeFlag = "phone-receipt-time";

    /// <summary>The event type names the status line counts, as <c>AccessibilityEvent.eventTypeToString</c> gives them.</summary>
    public const string ContentChangedName = "TYPE_WINDOW_CONTENT_CHANGED";

    public const string AccessibilityFocusedName = "TYPE_VIEW_ACCESSIBILITY_FOCUSED";

    public const string WindowStateChangedName = "TYPE_WINDOW_STATE_CHANGED";

    public const string WindowsChangedName = "TYPE_WINDOWS_CHANGED";

    /// <summary>The kinds of content change, by bit (Android, AccessibilityEvent, CONTENT_CHANGE_TYPE_*).</summary>
    public static IReadOnlyList<(int Bit, string Name)> ContentChangeTypes { get; } =
    [
        (0x1, "SUBTREE"),
        (0x2, "TEXT"),
        (0x4, "CONTENT_DESCRIPTION"),
        (0x8, "PANE_TITLE"),
        (0x10, "PANE_APPEARED"),
        (0x20, "PANE_DISAPPEARED"),
        (0x40, "STATE_DESCRIPTION"),
        (0x80, "DRAG_STARTED"),
        (0x100, "DRAG_DROPPED"),
        (0x200, "DRAG_CANCELLED"),
        (0x400, "CONTENT_INVALID"),
        (0x800, "ERROR"),
        (0x1000, "ENABLED"),
        (0x2000, "CHECKED"),
        (0x4000, "EXPANDED"),
        (0x8000, "SUPPLEMENTAL_DESCRIPTION"),
        (0x10000, "SORT_DIRECTION")
    ];

    /// <summary>The kinds of window change, by bit (Android, AccessibilityEvent, WINDOWS_CHANGE_*).</summary>
    public static IReadOnlyList<(int Bit, string Name)> WindowChanges { get; } =
    [
        (0x1, "ADDED"),
        (0x2, "REMOVED"),
        (0x4, "TITLE"),
        (0x8, "BOUNDS"),
        (0x10, "LAYER"),
        (0x20, "ACTIVE"),
        (0x40, "FOCUSED"),
        (0x80, "ACCESSIBILITY_FOCUSED"),
        (0x100, "PARENT"),
        (0x200, "CHILDREN"),
        (0x400, "PIP")
    ];

    /// <summary>The checked states an item can have.</summary>
    public static IReadOnlyList<string> CheckedStates { get; } = ["checked", "unchecked", "partial"];

    /// <summary>The kinds of link in an item's text: a URLSpan, or another ClickableSpan.</summary>
    public static IReadOnlyList<string> TextLinkKinds { get; } = ["url", "clickable"];

    /// <summary>
    /// Whether the companion app asked where a link's characters are on the
    /// screen, and what came back (TextLinks.java): "not-asked", as for an
    /// event's source item; "not-offered", the item does not give them;
    /// "too-long", more characters than one request may ask for;
    /// "not-returned", asked and none came back; "text-changed", the text
    /// changed before they came back; or "located".
    /// </summary>
    public static IReadOnlyList<string> TextLinkLocations { get; } =
        ["not-asked", "not-offered", "too-long", "not-returned", "text-changed", "located"];

    /// <summary>The <see cref="TextLinkLocations"/> value for links whose boxes are given.</summary>
    public const string TextLinkLocated = "located";

    /// <summary>
    /// The names of the bits set, in order of bit; a bit Android may add
    /// later is named by its value, such as "0x20000".
    /// </summary>
    public static IReadOnlyList<string> Names(int bits, IReadOnlyList<(int Bit, string Name)> table)
    {
        var names = new List<string>();
        for (var bit = 0; bit < 32; bit++)
        {
            var value = 1 << bit;
            if ((bits & value) == 0)
            {
                continue;
            }

            var known = table.FirstOrDefault(entry => entry.Bit == value);
            names.Add(known.Name ?? "0x" + ((uint)value).ToString("x", CultureInfo.InvariantCulture));
        }

        return names;
    }
}

/// <summary>
/// One accessibility event as the companion app sent it
/// (AccessibilityEventJson.java), with the names of its change bits and the
/// time the recorder read it. Each field is the event's getter of the same
/// name (Android, AccessibilityEvent and AccessibilityRecord); one the
/// phone's Android version does not have is null.
/// </summary>
public sealed record AndroidAccessibilityEvent
{
    /// <summary>Counts the events sent on the connection, from 1, so a gap shows one missing.</summary>
    public required long Index { get; init; }

    public required int EventType { get; init; }

    /// <summary>As <c>AccessibilityEvent.eventTypeToString</c> gives it, such as TYPE_VIEW_ACCESSIBILITY_FOCUSED.</summary>
    public required string EventTypeName { get; init; }

    /// <summary><c>getEventTime()</c>: milliseconds of the phone's uptime clock, the basis of its CLOCK_MONOTONIC.</summary>
    public required long EventTimeMilliseconds { get; init; }

    /// <summary>The phone's CLOCK_MONOTONIC when the companion's service was given the event.</summary>
    public required long ReceivedPhoneNanoseconds { get; init; }

    /// <summary>The phone's CLOCK_MONOTONIC when the service had read the event and its source.</summary>
    public required long HandledPhoneNanoseconds { get; init; }

    public string? PackageName { get; init; }

    public string? ClassName { get; init; }

    public required int WindowId { get; init; }

    public int? DisplayId { get; init; }

    public IReadOnlyList<string> Text { get; init; } = [];

    public string? ContentDescription { get; init; }

    public string? BeforeText { get; init; }

    public int ContentChangeTypes { get; init; }

    public IReadOnlyList<string> ContentChangeTypeNames { get; init; } = [];

    public int WindowChanges { get; init; }

    public IReadOnlyList<string> WindowChangeNames { get; init; } = [];

    public int? SpeechStateChangeTypes { get; init; }

    public int Action { get; init; }

    public int MovementGranularity { get; init; }

    public int FromIndex { get; init; }

    public int ToIndex { get; init; }

    public int AddedCount { get; init; }

    public int RemovedCount { get; init; }

    public int ItemCount { get; init; }

    public int CurrentItemIndex { get; init; }

    public int ScrollX { get; init; }

    public int ScrollY { get; init; }

    public int MaxScrollX { get; init; }

    public int MaxScrollY { get; init; }

    public int ScrollDeltaX { get; init; }

    public int ScrollDeltaY { get; init; }

    public bool Checked { get; init; }

    public bool Enabled { get; init; }

    public bool Password { get; init; }

    public bool FullScreen { get; init; }

    public bool Scrollable { get; init; }

    public bool? AccessibilityDataSensitive { get; init; }

    /// <summary>The class of <c>getParcelableData()</c>, such as a notification's, or null.</summary>
    public string? ParcelableDataClass { get; init; }

    /// <summary>The event's extra records, such as those of a scroll.</summary>
    public IReadOnlyList<AndroidAccessibilityEventRecord> Records { get; init; } = [];

    /// <summary>The item the event came from, as <c>getSource()</c> gave it as the event was read, or null.</summary>
    public AndroidAccessibilityNode? Source { get; init; }

    /// <summary>The session time the recorder read the event.</summary>
    public long ReceivedAtNanoseconds { get; init; }
}

/// <summary>One of an event's extra records (Android, AccessibilityRecord).</summary>
public sealed record AndroidAccessibilityEventRecord
{
    public string? ClassName { get; init; }

    public int WindowId { get; init; }

    public IReadOnlyList<string> Text { get; init; } = [];

    public string? ContentDescription { get; init; }

    public string? BeforeText { get; init; }

    public int FromIndex { get; init; }

    public int ToIndex { get; init; }

    public int AddedCount { get; init; }

    public int RemovedCount { get; init; }

    public int ItemCount { get; init; }

    public int CurrentItemIndex { get; init; }

    public int ScrollX { get; init; }

    public int ScrollY { get; init; }

    public int MaxScrollX { get; init; }

    public int MaxScrollY { get; init; }

    public bool Checked { get; init; }

    public bool Enabled { get; init; }

    public bool Password { get; init; }

    public bool FullScreen { get; init; }

    public bool Scrollable { get; init; }
}

/// <summary>
/// One item of a window's tree (Android, AccessibilityNodeInfo), without
/// its place in the tree. Each field is the getter of the same name, or
/// close to it: <see cref="ViewId"/> is <c>getViewIdResourceName()</c>,
/// <see cref="Hint"/> <c>getHintText()</c>, <see cref="Tooltip"/>
/// <c>getTooltipText()</c> and <see cref="ShowingHint"/>
/// <c>isShowingHintText()</c>. One the phone's Android version does not
/// have is null.
/// </summary>
public record AndroidAccessibilityNode
{
    public int WindowId { get; init; }

    public string? ClassName { get; init; }

    public string? PackageName { get; init; }

    public string? ViewId { get; init; }

    public string? UniqueId { get; init; }

    public string? Text { get; init; }

    public string? ContentDescription { get; init; }

    public string? StateDescription { get; init; }

    public string? Hint { get; init; }

    public string? PaneTitle { get; init; }

    public string? Tooltip { get; init; }

    public string? Error { get; init; }

    public string? ContainerTitle { get; init; }

    public string? SupplementalDescription { get; init; }

    /// <summary>The item's bounds on the screen, in pixels.</summary>
    public required AndroidNodeBounds Bounds { get; init; }

    public int ChildCount { get; init; }

    public int DrawingOrder { get; init; }

    /// <summary>"checked", "unchecked" or "partial"; see <see cref="AndroidAccessibilityRecords.CheckedStates"/>.</summary>
    public required string Checked { get; init; }

    public int? ExpandedState { get; init; }

    public bool ImportantForAccessibility { get; init; }

    public bool VisibleToUser { get; init; }

    public bool AccessibilityFocused { get; init; }

    public bool Focused { get; init; }

    public bool Focusable { get; init; }

    public bool ScreenReaderFocusable { get; init; }

    public bool Selected { get; init; }

    public bool Checkable { get; init; }

    public bool Enabled { get; init; }

    public bool Clickable { get; init; }

    public bool LongClickable { get; init; }

    public bool ContextClickable { get; init; }

    public bool Scrollable { get; init; }

    public bool Editable { get; init; }

    public bool Password { get; init; }

    public bool Heading { get; init; }

    public bool ShowingHint { get; init; }

    public bool MultiLine { get; init; }

    public bool Dismissable { get; init; }

    public bool ContentInvalid { get; init; }

    public bool TextEntryKey { get; init; }

    public bool? FieldRequired { get; init; }

    public int InputType { get; init; }

    public int MaxTextLength { get; init; }

    public int LiveRegion { get; init; }

    public int TextSelectionStart { get; init; }

    public int TextSelectionEnd { get; init; }

    public IReadOnlyList<AndroidNodeAction> Actions { get; init; } = [];

    public AndroidCollectionInfo? CollectionInfo { get; init; }

    public AndroidCollectionItemInfo? CollectionItemInfo { get; init; }

    public AndroidRangeInfo? RangeInfo { get; init; }

    /// <summary>
    /// The links in the item's text, in the text's order, from the companion
    /// app's protocol 8 (android-checks.md, step 29); empty when the text has
    /// none, and null in a recording made before, which did not record them.
    /// </summary>
    public IReadOnlyList<AndroidTextLink>? Links { get; init; }
}

public sealed record AndroidNodeBounds(int Left, int Top, int Right, int Bottom);

/// <summary>
/// A link in an item's text (TextLinks.java): a ClickableSpan, or a URLSpan
/// with its address, from <see cref="Start"/> up to <see cref="End"/> in the
/// item's <see cref="AndroidAccessibilityNode.Text"/>, counted in UTF-16
/// code units as both Java and .NET count them.
/// </summary>
/// <param name="Kind">"url" or "clickable"; see <see cref="AndroidAccessibilityRecords.TextLinkKinds"/>.</param>
/// <param name="Url">The address, for a "url" link that has one.</param>
/// <param name="Location">
/// Whether the app was asked where the link's characters are on the screen,
/// and what came back; see <see cref="AndroidAccessibilityRecords.TextLinkLocations"/>.
/// </param>
/// <param name="CharactersLocated">
/// How many of the link's characters came back with a place, when the
/// places came back; a character off the screen has none.
/// </param>
/// <param name="Boxes">
/// A box on the screen, in pixels, for each line the located characters
/// are on, joined from their boxes and rounded out to whole pixels; empty
/// unless <see cref="Location"/> is "located".
/// </param>
public sealed record AndroidTextLink(
    int Start,
    int End,
    string Kind,
    string? Url,
    string Location,
    int? CharactersLocated,
    IReadOnlyList<AndroidNodeBounds> Boxes);

/// <summary>An action the item offers: its id, and its label when it has one, as for a custom action.</summary>
public sealed record AndroidNodeAction(int Id, string? Label);

public sealed record AndroidCollectionInfo(int RowCount, int ColumnCount, bool Hierarchical, int SelectionMode);

public sealed record AndroidCollectionItemInfo(
    int RowIndex,
    int RowSpan,
    int ColumnIndex,
    int ColumnSpan,
    bool Selected,
    string? RowTitle,
    string? ColumnTitle);

/// <summary>A range's values; null for one JSON cannot hold, such as infinity.</summary>
public sealed record AndroidRangeInfo(int Type, double? Min, double? Max, double? Current);

/// <summary>Why the accessibility events are missing from part or all of a recording.</summary>
/// <param name="ConnectionLost">True when the events stopped during the recording.</param>
public sealed record AndroidAccessibilityUnavailable(string Serial, string Reason, bool ConnectionLost);

/// <summary>
/// A snapshot of the phone's accessibility tree, as the companion app sent
/// it (AccessibilityTrees.java), with the time the recorder read it and the
/// size of the message (slice 7, step 7b).
///
/// <see cref="Windows"/> is every window <c>getWindows()</c> gave. When
/// <see cref="EveryWindow"/> is true, as when the recording starts and on a
/// window change, each holds its items; otherwise only the windows whose
/// content or focus changed do, and the others' <see cref="AndroidAccessibilityWindow.Items"/>
/// is null. The latest items of a window are those of the latest snapshot
/// that holds them.
/// </summary>
public sealed record AndroidAccessibilityTree
{
    /// <summary>Counts the snapshots sent on the connection, from 1, so a gap shows one missing.</summary>
    public required long Index { get; init; }

    /// <summary>Why it was taken; see <see cref="AndroidAccessibilityRecords.TreeReasons"/>.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>True when every window's items were taken.</summary>
    public bool EveryWindow { get; init; }

    /// <summary>The window, content and focus changes the snapshot covers, all those since the last snapshot began.</summary>
    public int Changes { get; init; }

    /// <summary>The <c>getEventTime()</c> of the first change it covers, or null for none.</summary>
    public long? FirstChangeEventTimeMilliseconds { get; init; }

    /// <summary>The <c>getEventTime()</c> of the last change it covers, or null for none.</summary>
    public long? LastChangeEventTimeMilliseconds { get; init; }

    /// <summary>The phone's CLOCK_MONOTONIC when the companion started taking it.</summary>
    public required long StartedPhoneNanoseconds { get; init; }

    /// <summary>The phone's CLOCK_MONOTONIC when the companion had taken it.</summary>
    public required long FinishedPhoneNanoseconds { get; init; }

    public IReadOnlyList<AndroidAccessibilityWindow> Windows { get; init; } = [];

    /// <summary>Why the snapshot stopped early, such as a window closing while it was taken; null when it did not.</summary>
    public string? Problem { get; init; }

    /// <summary>The size of the companion's message, in bytes of UTF-8.</summary>
    public long MessageBytes { get; init; }

    /// <summary>The session time the recorder read it.</summary>
    public long ReceivedAtNanoseconds { get; init; }
}

/// <summary>
/// One window of a snapshot (Android, AccessibilityWindowInfo), with its
/// items when they were taken. Each field is the getter of the same name;
/// <see cref="ParentId"/> is the id of <c>getParent()</c>.
/// </summary>
public sealed record AndroidAccessibilityWindow
{
    public required int Id { get; init; }

    public int Type { get; init; }

    /// <summary>Such as TYPE_APPLICATION, or TYPE_ and the number for a type Android adds later.</summary>
    public required string TypeName { get; init; }

    public int Layer { get; init; }

    public string? Title { get; init; }

    public required AndroidNodeBounds Bounds { get; init; }

    public bool Active { get; init; }

    public bool Focused { get; init; }

    public bool AccessibilityFocused { get; init; }

    public bool PictureInPicture { get; init; }

    public int DisplayId { get; init; }

    public int? ParentId { get; init; }

    /// <summary><c>getTransitionTimeMillis()</c>, Android 14 (API 34) and later; null before.</summary>
    public long? TransitionTimeMilliseconds { get; init; }

    /// <summary>The window's items in depth-first order, children in order; null when not taken in this snapshot.</summary>
    public IReadOnlyList<AndroidTreeNode>? Items { get; init; }

    /// <summary>True when the window had more than the companion's limit of 10,000 items, and the rest are missing.</summary>
    public bool Truncated { get; init; }

    /// <summary>Children Android listed but could not give, as when they went away while the snapshot was taken.</summary>
    public int MissingChildren { get; init; }
}

/// <summary>
/// One item of a window's tree with its place in it: its place in the
/// window's list, its parent's, and its depth from the root at 0. Labels
/// and traversal links are places in the same list.
/// </summary>
public sealed record AndroidTreeNode : AndroidAccessibilityNode
{
    public required int Index { get; init; }

    /// <summary>The parent's place, or null for the root.</summary>
    public int? Parent { get; init; }

    public int Depth { get; init; }

    /// <summary>The places of the items that label this one: <c>getLabeledByList()</c> from Android 16 (API 36), <c>getLabeledBy()</c> before.</summary>
    public IReadOnlyList<int> LabeledBy { get; init; } = [];

    /// <summary>Labels outside this window's list.</summary>
    public int LabeledByOutside { get; init; }

    /// <summary>The place of <c>getTraversalBefore()</c>, or null for none or one outside the list.</summary>
    public int? TraversalBefore { get; init; }

    /// <summary>The place of <c>getTraversalAfter()</c>, or null for none or one outside the list.</summary>
    public int? TraversalAfter { get; init; }
}

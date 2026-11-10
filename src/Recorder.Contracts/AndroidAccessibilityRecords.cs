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
/// </summary>
public static class AndroidAccessibilityRecords
{
    /// <summary>Every accessibility event, and why the events are missing from part or all of a recording.</summary>
    public const string EventsChannel = "accessibility.android.events";

    public const string EventType = "accessibility-event";

    /// <summary>Why the events are missing from part or all of a recording.</summary>
    public const string UnavailableEventType = "accessibility-events-unavailable";

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
public sealed record AndroidAccessibilityNode
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
}

public sealed record AndroidNodeBounds(int Left, int Top, int Right, int Bottom);

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

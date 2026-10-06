using System.Globalization;

namespace Recorder.Collectors.Automation;

// The text the UI Automation collector records for identifiers and values the
// native client gives as numbers. The names are the programmatic names the
// managed client, System.Windows.Automation, gave for the same identifiers, so
// recordings made through either client name an event, a property, a control
// type, and a value the same way. None of this calls UI Automation.
public static class UiaEvidenceText
{
    public const int StructureChangedEventId = 20002;
    public const int FocusChangedEventId = 20005;
    public const int InvokedEventId = 20009;
    public const int ElementSelectedEventId = 20012;
    public const int TextChangedEventId = 20015;

    public const int BoundingRectanglePropertyId = 30001;
    public const int ProcessIdPropertyId = 30002;
    public const int ControlTypePropertyId = 30003;
    public const int LocalizedControlTypePropertyId = 30004;
    public const int NamePropertyId = 30005;
    public const int HasKeyboardFocusPropertyId = 30008;
    public const int IsKeyboardFocusablePropertyId = 30009;
    public const int IsEnabledPropertyId = 30010;
    public const int AutomationIdPropertyId = 30011;
    public const int ClassNamePropertyId = 30012;
    public const int NativeWindowHandlePropertyId = 30020;
    public const int IsOffscreenPropertyId = 30022;
    public const int FrameworkIdPropertyId = 30024;
    public const int ValuePropertyId = 30045;
    public const int ExpandCollapseStatePropertyId = 30070;
    public const int ToggleStatePropertyId = 30086;

    // UIA_E_ELEMENTNOTAVAILABLE, and the COM errors for a provider whose
    // process or object has gone: RPC_E_DISCONNECTED and
    // RPC_S_SERVER_UNAVAILABLE.
    private static readonly int[] ElementGoneResults =
    [
        unchecked((int)0x80040201),
        unchecked((int)0x80010108),
        unchecked((int)0x800706BA)
    ];

    // UIA_E_INVALIDOPERATION, which the managed client raised as
    // InvalidOperationException.
    private const int InvalidOperationResult = unchecked((int)0x80131509);

    private static readonly Dictionary<int, string> EventNames = new()
    {
        [StructureChangedEventId] = "AutomationElementIdentifiers.StructureChangedEvent",
        [FocusChangedEventId] = "AutomationElementIdentifiers.AutomationFocusChangedEvent",
        [InvokedEventId] = "InvokePatternIdentifiers.InvokedEvent",
        [ElementSelectedEventId] = "SelectionItemPatternIdentifiers.ElementSelectedEvent",
        [TextChangedEventId] = "TextPatternIdentifiers.TextChangedEvent"
    };

    private static readonly Dictionary<int, string> PropertyNames = new()
    {
        [NamePropertyId] = "AutomationElementIdentifiers.NameProperty",
        [HasKeyboardFocusPropertyId] = "AutomationElementIdentifiers.HasKeyboardFocusProperty",
        [IsEnabledPropertyId] = "AutomationElementIdentifiers.IsEnabledProperty",
        [ValuePropertyId] = "ValuePatternIdentifiers.ValueProperty",
        [ToggleStatePropertyId] = "TogglePatternIdentifiers.ToggleStateProperty",
        [ExpandCollapseStatePropertyId] =
            "ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty"
    };

    // The control types the managed client names, in identifier order from
    // 50000. It names none above 50038, and gave no name for those.
    private static readonly string[] ControlTypeNames =
    [
        "Button", "Calendar", "CheckBox", "ComboBox", "Edit", "Hyperlink", "Image",
        "ListItem", "List", "Menu", "MenuBar", "MenuItem", "ProgressBar",
        "RadioButton", "ScrollBar", "Slider", "Spinner", "StatusBar", "Tab",
        "TabItem", "Text", "ToolBar", "ToolTip", "Tree", "TreeItem", "Custom",
        "Group", "Thumb", "DataGrid", "DataItem", "Document", "SplitButton",
        "Window", "Pane", "Header", "HeaderItem", "Table", "TitleBar", "Separator"
    ];

    private static readonly string[] StructureChangeNames =
    [
        "ChildAdded",
        "ChildRemoved",
        "ChildrenInvalidated",
        "ChildrenBulkAdded",
        "ChildrenBulkRemoved",
        "ChildrenReordered"
    ];

    private static readonly string[] ToggleStateNames = ["Off", "On", "Indeterminate"];

    private static readonly string[] ExpandCollapseStateNames =
        ["Collapsed", "Expanded", "PartiallyExpanded", "LeafNode"];

    // The properties each property-changed subscription asks for.
    public static IReadOnlyList<int> ChangedPropertyIds { get; } =
    [
        NamePropertyId,
        HasKeyboardFocusPropertyId,
        IsEnabledPropertyId,
        ValuePropertyId,
        ToggleStatePropertyId,
        ExpandCollapseStatePropertyId
    ];

    // The element properties every observation records.
    public static IReadOnlyList<int> SnapshotPropertyIds { get; } =
    [
        ProcessIdPropertyId,
        NativeWindowHandlePropertyId,
        AutomationIdPropertyId,
        NamePropertyId,
        ClassNamePropertyId,
        FrameworkIdPropertyId,
        ControlTypePropertyId,
        LocalizedControlTypePropertyId,
        HasKeyboardFocusPropertyId,
        IsKeyboardFocusablePropertyId,
        IsEnabledPropertyId,
        IsOffscreenPropertyId,
        BoundingRectanglePropertyId
    ];

    public static string EventName(int eventId) =>
        EventNames.TryGetValue(eventId, out var name)
            ? name
            : eventId.ToString(CultureInfo.InvariantCulture);

    public static string PropertyName(int propertyId) =>
        PropertyNames.TryGetValue(propertyId, out var name)
            ? name
            : propertyId.ToString(CultureInfo.InvariantCulture);

    public static string? ControlTypeName(object? value) =>
        value is int id && id >= 50000 && id - 50000 < ControlTypeNames.Length
            ? "ControlType." + ControlTypeNames[id - 50000]
            : null;

    public static string StructureChangeName(int changeType) =>
        changeType >= 0 && changeType < StructureChangeNames.Length
            ? StructureChangeNames[changeType]
            : changeType.ToString(CultureInfo.InvariantCulture);

    // A changed property's new value, as the managed client's value read as
    // text: an enumeration by its member name, a Boolean in lower case.
    public static string? NormalizeValue(int propertyId, object? value) =>
        value switch
        {
            null => null,
            bool boolean => boolean ? "true" : "false",
            int state when propertyId == ToggleStatePropertyId =>
                Member(ToggleStateNames, state),
            int state when propertyId == ExpandCollapseStatePropertyId =>
                Member(ExpandCollapseStateNames, state),
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    public static string? Text(object? value) => value as string;

    public static int? Integer(object? value) => value is int number ? number : null;

    public static bool? Boolean(object? value) => value is bool flag ? flag : null;

    // UI Automation gives a bounding rectangle as left, top, width, and
    // height. A value of any other shape, including none, is not a rectangle.
    public static (double X, double Y, double Width, double Height)? Rectangle(object? value) =>
        value is double[] { Length: 4 } box
            ? (box[0], box[1], box[2], box[3])
            : null;

    // The quality flag for a failed read of an element's properties, named as
    // the managed client's exception for the same failure was.
    public static string ReadFailureFlag(int resultCode) =>
        ElementGoneResults.Contains(resultCode) ? "element-not-available"
        : resultCode == InvalidOperationResult ? "element-property-read-failed"
        : "uia-provider-error";

    private static string Member(string[] names, int value) =>
        value >= 0 && value < names.Length
            ? names[value]
            : value.ToString(CultureInfo.InvariantCulture);
}

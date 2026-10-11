using System.Runtime.InteropServices;
using Interop.UIAutomationClient;

namespace Recorder.Collectors.Automation;

/// <summary>
/// A window's tree in UI Automation's control view, the view assistive
/// technology is given, read with each element's properties cached in the
/// same call that finds it. Pattern values are recorded only where the
/// element says it has the pattern, so a default value is never taken for
/// a state; a password's value is not recorded.
/// </summary>
public sealed class UiaWindowTreeSource : IWindowTreeSource<IUIAutomationElement>
{
    // UI Automation's property IDs (UIAutomationClient.h).
    private const int RuntimeIdPropertyId = 30000;
    private const int AccessKeyPropertyId = 30007;
    private const int HelpTextPropertyId = 30013;
    private const int IsPasswordPropertyId = 30019;
    private const int IsExpandCollapsePatternAvailablePropertyId = 30028;
    private const int IsSelectionItemPatternAvailablePropertyId = 30036;
    private const int IsTogglePatternAvailablePropertyId = 30041;
    private const int IsValuePatternAvailablePropertyId = 30043;
    private const int ValueIsReadOnlyPropertyId = 30046;
    private const int SelectionItemIsSelectedPropertyId = 30079;

    private static readonly int[] Properties =
    [
        RuntimeIdPropertyId,
        UiaEvidenceText.NamePropertyId,
        UiaEvidenceText.ControlTypePropertyId,
        UiaEvidenceText.LocalizedControlTypePropertyId,
        UiaEvidenceText.AutomationIdPropertyId,
        UiaEvidenceText.ClassNamePropertyId,
        UiaEvidenceText.FrameworkIdPropertyId,
        AccessKeyPropertyId,
        HelpTextPropertyId,
        UiaEvidenceText.IsEnabledPropertyId,
        UiaEvidenceText.IsOffscreenPropertyId,
        UiaEvidenceText.HasKeyboardFocusPropertyId,
        UiaEvidenceText.IsKeyboardFocusablePropertyId,
        IsPasswordPropertyId,
        UiaEvidenceText.BoundingRectanglePropertyId,
        IsValuePatternAvailablePropertyId,
        UiaEvidenceText.ValuePropertyId,
        ValueIsReadOnlyPropertyId,
        IsTogglePatternAvailablePropertyId,
        UiaEvidenceText.ToggleStatePropertyId,
        IsSelectionItemPatternAvailablePropertyId,
        SelectionItemIsSelectedPropertyId,
        IsExpandCollapsePatternAvailablePropertyId,
        UiaEvidenceText.ExpandCollapseStatePropertyId
    ];

    private readonly IUIAutomation _automation;
    private readonly IUIAutomationTreeWalker _walker;
    private readonly IUIAutomationCacheRequest _cache;

    public UiaWindowTreeSource(IUIAutomation automation)
    {
        ArgumentNullException.ThrowIfNull(automation);
        _automation = automation;
        _walker = automation.ControlViewWalker;
        _cache = automation.CreateCacheRequest();
        _cache.TreeScope = TreeScope.TreeScope_Element;
        _cache.AutomationElementMode = AutomationElementMode.AutomationElementMode_Full;
        foreach (var property in Properties)
        {
            _cache.AddProperty(property);
        }
    }

    /// <summary>The element with its properties cached, for <see cref="Read"/>.</summary>
    public IUIAutomationElement Cached(IUIAutomationElement element) => element.BuildUpdatedCache(_cache);

    /// <summary>
    /// The top-level window an element is in: its nearest ancestor, or
    /// itself, that is a top-level Win32 window, whose parent is the desktop
    /// window; or else its ancestor whose parent in UI Automation's tree is
    /// the desktop. A window owned by another, as a dialog opened from a
    /// dialog, is top-level in Win32 but placed under its owner in UI
    /// Automation's tree, so the Win32 parent decides (found by the Windows
    /// probe on 2026-10-11, where a second form was read as part of the
    /// first). Null for the desktop, or an element no longer there.
    /// </summary>
    public IUIAutomationElement? TopLevelOf(IUIAutomationElement element)
    {
        var root = _automation.GetRootElement();
        var raw = _automation.RawViewWalker;
        var current = element;
        for (var depth = 0; depth < 256; depth++)
        {
            if (_automation.CompareElements(current, root) != 0)
            {
                return null;
            }

            if (current.CurrentNativeWindowHandle is var handle && handle != IntPtr.Zero && IsTopLevel(handle))
            {
                return Cached(current);
            }

            var parent = raw.GetParentElement(current);
            if (parent is null)
            {
                return null;
            }

            if (_automation.CompareElements(parent, root) != 0)
            {
                return Cached(current);
            }

            current = parent;
        }

        return null;
    }

    private const uint GaParent = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    private static bool IsTopLevel(IntPtr window) => GetAncestor(window, GaParent) == GetDesktopWindow();

    public bool Same(IUIAutomationElement first, IUIAutomationElement second) =>
        _automation.CompareElements(first, second) != 0;

    public IUIAutomationElement? FirstChild(IUIAutomationElement node) =>
        _walker.GetFirstChildElementBuildCache(node, _cache);

    public IUIAutomationElement? NextSibling(IUIAutomationElement node) =>
        _walker.GetNextSiblingElementBuildCache(node, _cache);

    public WindowSnapshotElement Read(IUIAutomationElement node, int? parent, int depth, int position)
    {
        var flags = new List<string>();
        object? Get(int propertyId)
        {
            try
            {
                return node.GetCachedPropertyValueEx(propertyId, 0);
            }
            catch (System.Runtime.InteropServices.COMException exception)
            {
                flags.Add($"{UiaEvidenceText.PropertyName(propertyId)}:{UiaEvidenceText.ReadFailureFlag(exception.HResult)}");
                return null;
            }
        }

        bool Has(int availableId) => UiaEvidenceText.Boolean(Get(availableId)) == true;
        var isPassword = UiaEvidenceText.Boolean(Get(IsPasswordPropertyId));
        var hasValue = Has(IsValuePatternAvailablePropertyId);
        var rectangle = UiaEvidenceText.Rectangle(Get(UiaEvidenceText.BoundingRectanglePropertyId));
        return new WindowSnapshotElement(
            parent,
            depth,
            position,
            Get(RuntimeIdPropertyId) as int[],
            UiaEvidenceText.Text(Get(UiaEvidenceText.NamePropertyId)),
            UiaEvidenceText.ControlTypeName(Get(UiaEvidenceText.ControlTypePropertyId)),
            UiaEvidenceText.Text(Get(UiaEvidenceText.LocalizedControlTypePropertyId)),
            UiaEvidenceText.Text(Get(UiaEvidenceText.AutomationIdPropertyId)),
            UiaEvidenceText.Text(Get(UiaEvidenceText.ClassNamePropertyId)),
            UiaEvidenceText.Text(Get(UiaEvidenceText.FrameworkIdPropertyId)),
            UiaEvidenceText.Text(Get(AccessKeyPropertyId)),
            UiaEvidenceText.Text(Get(HelpTextPropertyId)),
            UiaEvidenceText.Boolean(Get(UiaEvidenceText.IsEnabledPropertyId)),
            UiaEvidenceText.Boolean(Get(UiaEvidenceText.IsOffscreenPropertyId)),
            UiaEvidenceText.Boolean(Get(UiaEvidenceText.HasKeyboardFocusPropertyId)),
            UiaEvidenceText.Boolean(Get(UiaEvidenceText.IsKeyboardFocusablePropertyId)),
            isPassword,
            rectangle is { } box ? new WindowSnapshotRectangle(box.X, box.Y, box.Width, box.Height) : null,
            hasValue && isPassword != true ? UiaEvidenceText.Text(Get(UiaEvidenceText.ValuePropertyId)) : null,
            hasValue ? UiaEvidenceText.Boolean(Get(ValueIsReadOnlyPropertyId)) : null,
            Has(IsTogglePatternAvailablePropertyId)
                ? UiaEvidenceText.NormalizeValue(UiaEvidenceText.ToggleStatePropertyId, Get(UiaEvidenceText.ToggleStatePropertyId))
                : null,
            Has(IsSelectionItemPatternAvailablePropertyId) ? UiaEvidenceText.Boolean(Get(SelectionItemIsSelectedPropertyId)) : null,
            Has(IsExpandCollapsePatternAvailablePropertyId)
                ? UiaEvidenceText.NormalizeValue(
                    UiaEvidenceText.ExpandCollapseStatePropertyId, Get(UiaEvidenceText.ExpandCollapseStatePropertyId))
                : null,
            flags);
    }
}

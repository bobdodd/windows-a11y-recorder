// The rules of the checks below follow Google's Accessibility Test Framework
// for Android (ATF), at commit c65cab02b2a845c29c3da100d6adefd345a144e3:
// https://github.com/google/Accessibility-Test-Framework-for-Android
//
//   Copyright (C) 2015 Google Inc.
//   Copyright (C) 2017 Google Inc.
//
//   Licensed under the Apache License, Version 2.0 (the "License"); you may
//   not use this file except in compliance with the License. You may obtain
//   a copy of the License at
//
//   http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
//
// Changes: rewritten in C# against a recorded snapshot of one window
// (AndroidTreeNode), not a live AccessibilityNodeInfo hierarchy; only the
// results the recorder lists are produced (not the "not run" result for each
// item a check does not apply to); messages are the recorder's own. Where the
// snapshot lacks a value ATF reads, the code says what it does instead. The
// 24dp level of the touch target check (WCAG 2.5.8) is the recorder's own and
// not part of ATF.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>ATF's result types, in the words the recorder keeps (android-checks.md, "The checks").</summary>
public enum PhoneCheckLevel
{
    Error,
    Warning,
    Info,
    NotRun,
}

/// <summary>The checks the recorder runs on a phone's tree (android-checks.md, "From the tree, exact").</summary>
public static class PhoneCheckNames
{
    public const string TouchTarget = "touch-target-size";
    public const string TouchTargetWcag = "touch-target-24dp";
    public const string SpeakableText = "speakable-text-present";
    public const string DuplicateSpeakableText = "duplicate-speakable-text";
    public const string DuplicateClickableBounds = "duplicate-clickable-bounds";
    public const string EditableContentDescription = "editable-content-description";
    public const string RedundantDescription = "redundant-description";
    public const string ClassName = "class-name";
    public const string TraversalOrder = "traversal-order";

    /// <summary>The text contrast check (step 4c), at AA or at AAA; not one of the tree's checks.</summary>
    public const string TextContrastAa = "text-contrast-aa";
    public const string TextContrastAaa = "text-contrast-aaa";

    /// <summary>The non-text contrast check (step 5), WCAG 2.2's 1.4.11, at AA only; not one of the tree's checks.</summary>
    public const string NonTextContrast = "non-text-contrast";

    /// <summary>The screen's title and headings (step 6, PhoneScreenChecks), each one discrete issue (owner, 2026-11-12).</summary>
    public const string TitleMissing = "screen-title-missing";
    public const string TitleShared = "screen-title-shared";
    public const string NoHeading = "no-heading";
    public const string FirstHeadingLow = "first-heading-low";
    public const string ManyHeadings = "many-headings";

    /// <summary>The checks in the order the Checks tab lists them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        TouchTarget, TouchTargetWcag, SpeakableText, DuplicateSpeakableText, DuplicateClickableBounds,
        EditableContentDescription, RedundantDescription, ClassName, TraversalOrder,
        TitleMissing, TitleShared, NoHeading, FirstHeadingLow, ManyHeadings,
    ];

    /// <summary>A check's name as the Checks tab shows it.</summary>
    public static string Title(string check) => check switch
    {
        TouchTarget => "Touch target size (ATF, 48dp)",
        TouchTargetWcag => "Touch target under 24dp (WCAG 2.5.8)",
        SpeakableText => "Speakable text present (ATF)",
        DuplicateSpeakableText => "Duplicate speakable text (ATF)",
        DuplicateClickableBounds => "Duplicate clickable bounds (ATF)",
        EditableContentDescription => "Editable content description (ATF)",
        RedundantDescription => "Redundant description (ATF)",
        ClassName => "Class name (ATF)",
        TraversalOrder => "Traversal order (ATF)",
        TextContrastAa => "Text contrast at AA (WCAG 1.4.3)",
        TextContrastAaa => "Text contrast at AAA (WCAG 1.4.6)",
        NonTextContrast => "Non-text contrast (WCAG 1.4.11)",
        TitleMissing => "No screen title (WCAG2ICT 2.4.2)",
        TitleShared => "Screen title shared with other screens (WCAG2ICT 2.4.2)",
        NoHeading => "No heading (WCAG 1.3.1)",
        FirstHeadingLow => "First heading far from the top",
        ManyHeadings => "Many headings",
        _ => check,
    };

    public static string Word(PhoneCheckLevel level) => level switch
    {
        PhoneCheckLevel.Error => "error",
        PhoneCheckLevel.Warning => "warning",
        PhoneCheckLevel.Info => "info",
        _ => "not run",
    };
}

/// <summary>
/// One check's result for one item of a window: the check, ATF's result
/// type, the item's place in the window's list, and what was measured, in
/// words such as "40 by 40 dp; ATF needs 48 by 48 dp".
/// </summary>
public sealed record PhoneCheckResult(string Check, PhoneCheckLevel Level, int Item, string Measured);

/// <summary>
/// What a check needs beyond the items: the display density as a scale
/// (dpi / 160, ATF's <c>getDensity()</c>), and the screen's size in pixels
/// as it was turned (ATF's real metrics), or null when not recorded.
/// </summary>
public sealed record PhoneCheckContext(float Density, int? ScreenWidth, int? ScreenHeight);

/// <summary>
/// A window's items prepared for the checks: each with its parent and
/// children, its classes, its row key in the tree view, its identity within
/// the screen (android-checks.md, "Items and findings") and its label.
/// </summary>
public sealed class PhoneCheckWindow
{
    // AccessibilityWindowInfo.TYPE_INPUT_METHOD, ATF's TYPE_INPUT_METHOD.
    internal const int InputMethodWindowType = 2;

    public PhoneCheckWindow(int windowId, int windowType, IReadOnlyList<AndroidTreeNode> nodes, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        WindowId = windowId;
        WindowType = windowType;
        Title = title;
        var items = nodes.Select(node => new PhoneCheckItem(node)).ToList();
        var byIndex = new Dictionary<int, PhoneCheckItem>();
        foreach (var item in items)
        {
            byIndex.TryAdd(item.Node.Index, item);
        }

        var tops = new List<PhoneCheckItem>();
        foreach (var item in items)
        {
            if (item.Node.Parent is { } parent && parent != item.Node.Index && byIndex.TryGetValue(parent, out var found))
            {
                item.Parent = found;
                found._children.Add(item);
            }
            else
            {
                tops.Add(item);
            }
        }

        // The row keys as the tree view makes them (PhoneAccessibilityTimeline.RowsOf).
        var windowKey = "window:" + windowId.ToString(CultureInfo.InvariantCulture);
        var keyed = new HashSet<PhoneCheckItem>();
        void Key(PhoneCheckItem item, string key)
        {
            if (!keyed.Add(item))
            {
                return;
            }

            item.TreeKey = key;
            for (var place = 0; place < item._children.Count; place++)
            {
                Key(item._children[place], key + "/" + place.ToString(CultureInfo.InvariantCulture));
            }
        }

        for (var place = 0; place < tops.Count; place++)
        {
            Key(tops[place], windowKey + "/" + place.ToString(CultureInfo.InvariantCulture));
        }

        // Identity: the view ID, else the class, the text or description
        // and the path from the root; and the item's place among the items
        // with the same, so two alike are not one.
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var node = item.Node;
            string basis;
            if (node.ViewId is { Length: > 0 } viewId)
            {
                basis = "id:" + viewId;
            }
            else
            {
                var path = new List<string>();
                var guard = 0;
                for (var step = item.Parent; step is not null && guard++ < 1000; step = step.Parent)
                {
                    path.Add(step.Node.ViewId is { Length: > 0 } id ? id : step.Node.ClassName ?? "?");
                }

                path.Reverse();
                basis = "path:" + (node.ClassName ?? "?") + "|" + (node.Text ?? node.ContentDescription ?? string.Empty) + "|" + string.Join("/", path);
            }

            seen.TryGetValue(basis, out var count);
            seen[basis] = count + 1;
            item.Identity = basis + "#" + count.ToString(CultureInfo.InvariantCulture);
        }

        Items = items;
    }

    public int WindowId { get; }

    public int WindowType { get; }

    /// <summary>The window's title (<c>AccessibilityWindowInfo.getTitle</c>), or null when it has none.</summary>
    public string? Title { get; }

    /// <summary>The items in the window's list order, depth first.</summary>
    public IReadOnlyList<PhoneCheckItem> Items { get; }
}

/// <summary>A window's item as the checks see it.</summary>
public sealed class PhoneCheckItem
{
    internal readonly List<PhoneCheckItem> _children = [];
    private string? _label;

    internal PhoneCheckItem(AndroidTreeNode node)
    {
        Node = node;
        Classes = PhoneTreeChecks.ClassesOf(node.ClassName);
    }

    public AndroidTreeNode Node { get; }

    public PhoneCheckItem? Parent { get; internal set; }

    public IReadOnlyList<PhoneCheckItem> Children => _children;

    /// <summary>The item's row key in the "Accessibility tree" tab.</summary>
    public string TreeKey { get; internal set; } = string.Empty;

    /// <summary>The item's identity within its screen.</summary>
    public string Identity { get; internal set; } = string.Empty;

    /// <summary>The item's class and its known superclasses (see <see cref="PhoneTreeChecks.ClassesOf"/>).</summary>
    public IReadOnlySet<string> Classes { get; }

    public PhoneBounds Bounds => new(Node.Bounds.Left, Node.Bounds.Top, Node.Bounds.Right, Node.Bounds.Bottom);

    /// <summary>The item as a row names it, as the tree view does: "Button "Save"", "ImageView described as "Back"", "View containing "Sleep"".</summary>
    public string Label => _label ??= PhoneTreeChecks.LabelOf(this);

    internal bool InstanceOf(string className) => Classes.Contains(className);

    // ATF reads no text for an EditText showing its hint
    // (ViewHierarchyElementAndroid: isShowingHintText).
    internal string? Text => Node.ShowingHint ? null : Node.Text;
}

/// <summary>
/// The checks from the tree (android-checks.md, "From the tree, exact"), each
/// following the ATF check it names, run on one window's items.
/// </summary>
public static class PhoneTreeChecks
{
    // AccessibilityNodeInfo.ACTION_SCROLL_FORWARD and ACTION_SCROLL_BACKWARD.
    private const int ScrollForwardAction = 0x1000;
    private const int ScrollBackwardAction = 0x2000;

    // TouchTargetSizeCheck's defaults, in dp.
    private const int TouchTargetMinimum = 48;
    private const int TouchTargetMinimumOnEdge = 32;
    private const int TouchTargetMinimumInKeyboard = 32;

    // WCAG 2.5.8 Target Size (Minimum), with dp for the CSS pixel.
    private const double WcagTargetMinimum = 24;

    private const string AbsListView = "android.widget.AbsListView";
    private const string AdapterView = "android.widget.AdapterView";
    private const string ScrollView = "android.widget.ScrollView";
    private const string HorizontalScrollView = "android.widget.HorizontalScrollView";
    private const string Spinner = "android.widget.Spinner";
    private const string EditText = "android.widget.EditText";
    private const string WebView = "android.webkit.WebView";
    private const string Switch = "android.widget.Switch";
    private const string ToggleButton = "android.widget.ToggleButton";
    private const string ScrollingView = "androidx.core.view.ScrollingView";

    private static readonly string[] ScrollableContainers = [AdapterView, ScrollView, HorizontalScrollView, ScrollingView];

    // ClassNameCheck.VALID_UI_PACKAGE_NAME_PREFIXES.
    private static readonly string[] AndroidUiPackages =
    [
        "android.app", "android.appwidget", "android.inputmethodservice", "android.support",
        "android.view", "android.webkit", "android.widget", "androidx",
    ];

    // RedundantDescriptionCheck's words, from ATF's English strings.xml:
    // button_item_type, checkbox_item_type, checkbox_item_type_separate_words;
    // checked_state, unchecked_state, selected_state, unselected_state;
    // click_action, swipe_action, tap_action.
    private static readonly (string Kind, string[] Words)[] RedundantWords =
    [
        ("type", ["button", "checkbox", "check box"]),
        ("state", ["checked", "unchecked", "selected", "unselected"]),
        ("action", ["click", "swipe", "tap"]),
    ];

    // ATF finds an item's superclasses by loading its class on the phone
    // (AccessibilityHierarchyAndroid.getSuperclassSet). The recording holds
    // only the class name, so the superclasses of Android's own widgets that
    // the checks ask about are listed here, from the Android API reference;
    // an app's own class has only itself. RecyclerView and NestedScrollView
    // implement androidx's ScrollingView.
    private static readonly Dictionary<string, string[]> KnownSuperclasses = new(StringComparer.Ordinal)
    {
        ["android.widget.TextView"] = ["android.view.View"],
        ["android.widget.EditText"] = ["android.widget.TextView", "android.view.View"],
        ["android.widget.AutoCompleteTextView"] = [EditText, "android.widget.TextView", "android.view.View"],
        ["android.widget.MultiAutoCompleteTextView"] = ["android.widget.AutoCompleteTextView", EditText, "android.widget.TextView", "android.view.View"],
        ["android.widget.SearchView$SearchAutoComplete"] = ["android.widget.AutoCompleteTextView", EditText, "android.widget.TextView", "android.view.View"],
        ["android.widget.Button"] = ["android.widget.TextView", "android.view.View"],
        ["android.widget.CompoundButton"] = ["android.widget.Button", "android.widget.TextView", "android.view.View"],
        ["android.widget.CheckBox"] = ["android.widget.CompoundButton", "android.widget.Button", "android.widget.TextView", "android.view.View"],
        ["android.widget.RadioButton"] = ["android.widget.CompoundButton", "android.widget.Button", "android.widget.TextView", "android.view.View"],
        [Switch] = ["android.widget.CompoundButton", "android.widget.Button", "android.widget.TextView", "android.view.View"],
        [ToggleButton] = ["android.widget.CompoundButton", "android.widget.Button", "android.widget.TextView", "android.view.View"],
        ["android.widget.CheckedTextView"] = ["android.widget.TextView", "android.view.View"],
        ["android.widget.ImageView"] = ["android.view.View"],
        ["android.widget.ImageButton"] = ["android.widget.ImageView", "android.view.View"],
        ["android.view.ViewGroup"] = ["android.view.View"],
        ["android.widget.FrameLayout"] = ["android.view.ViewGroup", "android.view.View"],
        ["android.widget.LinearLayout"] = ["android.view.ViewGroup", "android.view.View"],
        ["android.widget.RelativeLayout"] = ["android.view.ViewGroup", "android.view.View"],
        [ScrollView] = ["android.widget.FrameLayout", "android.view.ViewGroup", "android.view.View"],
        [HorizontalScrollView] = ["android.widget.FrameLayout", "android.view.ViewGroup", "android.view.View"],
        [AdapterView] = ["android.view.ViewGroup", "android.view.View"],
        [AbsListView] = [AdapterView, "android.view.ViewGroup", "android.view.View"],
        ["android.widget.ListView"] = [AbsListView, AdapterView, "android.view.ViewGroup", "android.view.View"],
        ["android.widget.GridView"] = [AbsListView, AdapterView, "android.view.ViewGroup", "android.view.View"],
        ["android.widget.ExpandableListView"] = ["android.widget.ListView", AbsListView, AdapterView, "android.view.ViewGroup", "android.view.View"],
        ["android.widget.AbsSpinner"] = [AdapterView, "android.view.ViewGroup", "android.view.View"],
        [Spinner] = ["android.widget.AbsSpinner", AdapterView, "android.view.ViewGroup", "android.view.View"],
        ["android.widget.Gallery"] = ["android.widget.AbsSpinner", AdapterView, "android.view.ViewGroup", "android.view.View"],
        ["android.widget.AbsoluteLayout"] = ["android.view.ViewGroup", "android.view.View"],
        [WebView] = ["android.widget.AbsoluteLayout", "android.view.ViewGroup", "android.view.View"],
        ["android.widget.ProgressBar"] = ["android.view.View"],
        ["android.widget.AbsSeekBar"] = ["android.widget.ProgressBar", "android.view.View"],
        ["android.widget.SeekBar"] = ["android.widget.AbsSeekBar", "android.widget.ProgressBar", "android.view.View"],
        ["android.widget.RatingBar"] = ["android.widget.AbsSeekBar", "android.widget.ProgressBar", "android.view.View"],
        ["androidx.recyclerview.widget.RecyclerView"] = [ScrollingView, "android.view.ViewGroup", "android.view.View"],
        ["androidx.core.widget.NestedScrollView"] = [ScrollingView, "android.widget.FrameLayout", "android.view.ViewGroup", "android.view.View"],
    };

    /// <summary>A class and the superclasses listed for it; empty for no class.</summary>
    public static IReadOnlySet<string> ClassesOf(string? className)
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        if (className is { Length: > 0 })
        {
            classes.Add(className);
            if (KnownSuperclasses.TryGetValue(className, out var supers))
            {
                classes.UnionWith(supers);
            }
        }

        return classes;
    }

    /// <summary>Runs every check on a window's items.</summary>
    public static IReadOnlyList<PhoneCheckResult> Run(PhoneCheckWindow window, PhoneCheckContext context)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(context);
        var results = new List<PhoneCheckResult>();
        if (context.Density > 0)
        {
            TouchTargetSize(window, context, results);
            TouchTargetWcag(window, context, results);
        }

        SpeakableTextPresent(window, results);
        DuplicateSpeakableText(window, results);
        DuplicateClickableBounds(window, results);
        EditableContentDescription(window, results);
        RedundantDescription(window, results);
        ClassName(window, results);
        TraversalOrder(window, results);
        return results;
    }

    // Java's Math.round(float): the nearest whole number, halves up.
    private static int Round(float value) => (int)MathF.Floor(value + 0.5f);

    private static string Dp(int value) => value.ToString(CultureInfo.InvariantCulture);

    // ---- TouchTargetSizeCheck ----------------------------------------------

    internal static void TouchTargetSize(PhoneCheckWindow window, PhoneCheckContext context, List<PhoneCheckResult> results)
    {
        var density = context.Density;
        foreach (var view in window.Items)
        {
            var node = view.Node;
            if (!(node.Clickable || node.LongClickable) || !node.VisibleToUser)
            {
                continue;
            }

            var bounds = view.Bounds;
            var (requiredWidth, requiredHeight) = RequiredSize(view, window, context);
            var actualHeight = Round(bounds.Height / density);
            var actualWidth = Round(bounds.Width / density);
            if (actualWidth >= requiredWidth && actualHeight >= requiredHeight)
            {
                continue;
            }

            // ATF also counts a touch delegate (getTouchDelegateInfo), and an
            // item clipped by an ancestor from its View's own size; the
            // snapshot holds neither, so neither applies, as ATF does for an
            // item whose values are unknown.
            var hasClickableAncestor = HasQualifyingClickableAncestor(view, window, context);
            var isWebContent = HasWebViewAncestor(view);
            var level = hasClickableAncestor || isWebContent ? PhoneCheckLevel.Warning : PhoneCheckLevel.Error;
            var againstScrollableEdge = IsAgainstScrollableEdge(view);
            if (againstScrollableEdge)
            {
                level = PhoneCheckLevel.NotRun;
            }

            var shortBy = actualHeight < requiredHeight && actualWidth < requiredWidth
                ? "too narrow and too short"
                : actualHeight < requiredHeight ? "too short" : "too narrow";
            var measured = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
                $"{Dp(actualWidth)} by {Dp(actualHeight)} dp, {shortBy}; ATF needs {Dp(requiredWidth)} by {Dp(requiredHeight)} dp"));
            if (requiredWidth != TouchTargetMinimum || requiredHeight != TouchTargetMinimum)
            {
                measured.Append(window.WindowType == PhoneCheckWindow.InputMethodWindowType
                    ? " in the keyboard's window"
                    : " against the screen's edge");
            }

            if (hasClickableAncestor)
            {
                measured.Append("; a warning, as it is inside a clickable item of the needed size");
            }

            if (isWebContent)
            {
                measured.Append("; a warning, as it is web content");
            }

            if (againstScrollableEdge)
            {
                measured.Append("; not run, as it is against the edge of a list that scrolls, so may be cut off by it");
            }

            results.Add(new PhoneCheckResult(PhoneCheckNames.TouchTarget, level, node.Index, measured.ToString()));
        }
    }

    // TouchTargetSizeCheck.getMinimumAllowableSizeForView.
    private static (int Width, int Height) RequiredSize(PhoneCheckItem view, PhoneCheckWindow window, PhoneCheckContext context)
    {
        if (window.WindowType == PhoneCheckWindow.InputMethodWindowType)
        {
            return (TouchTargetMinimumInKeyboard, TouchTargetMinimumInKeyboard);
        }

        var bounds = view.Bounds;
        if (context.ScreenWidth is { } width && context.ScreenHeight is { } height)
        {
            var againstSide = bounds.Left == 0 || bounds.Right == width;
            var againstTopOrBottom = bounds.Top == 0 || bounds.Bottom == height;
            return (againstSide ? TouchTargetMinimumOnEdge : TouchTargetMinimum,
                againstTopOrBottom ? TouchTargetMinimumOnEdge : TouchTargetMinimum);
        }

        // ATF without the real metrics takes the smaller size.
        return (Math.Min(TouchTargetMinimumOnEdge, TouchTargetMinimum), Math.Min(TouchTargetMinimumOnEdge, TouchTargetMinimum));
    }

    // TouchTargetSizeCheck.hasQualifyingClickableAncestor. ATF compares the
    // ancestor's bounds in pixels with the size needed in dp, and so does
    // this, to give ATF's result.
    private static bool HasQualifyingClickableAncestor(PhoneCheckItem view, PhoneCheckWindow window, PhoneCheckContext context)
    {
        var clickable = view.Node.Clickable;
        var longClickable = view.Node.LongClickable;
        for (var ancestor = view.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if ((ancestor.Node.Clickable && clickable) || (ancestor.Node.LongClickable && longClickable))
            {
                var (width, height) = RequiredSize(ancestor, window, context);
                var bounds = ancestor.Bounds;
                if (!ancestor.InstanceOf(AbsListView) && bounds.Height >= height && bounds.Width >= width)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasWebViewAncestor(PhoneCheckItem view)
    {
        for (var ancestor = view.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.InstanceOf(WebView))
            {
                return true;
            }
        }

        return false;
    }

    // ViewHierarchyElement.isAgainstScrollableEdge.
    internal static bool IsAgainstScrollableEdge(PhoneCheckItem view)
    {
        if (!view.Node.VisibleToUser)
        {
            return false;
        }

        var bounds = view.Bounds;
        for (var ancestor = view.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            var scrollable = ancestor.Bounds;
            if (HasAction(ancestor, ScrollBackwardAction) && (bounds.Top <= scrollable.Top || bounds.Left <= scrollable.Left))
            {
                return true;
            }

            if (HasAction(ancestor, ScrollForwardAction) && (bounds.Bottom >= scrollable.Bottom || bounds.Right >= scrollable.Right))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAction(PhoneCheckItem item, int action) => item.Node.Actions.Any(entry => entry.Id == action);

    // ---- WCAG 2.5.8, the recorder's own level -------------------------------

    // A target under 24 by 24 dp fails unless a 24dp circle centred on its
    // bounds meets no other target and no other undersized target's circle
    // (WCAG 2.5.8's spacing exception). The targets are the items the touch
    // target check takes: clickable or long-clickable and visible. An item's
    // own ancestors and descendants are not counted as other targets, as one
    // inside another is the same place to touch (inference: WCAG speaks of
    // separate targets). A target against the edge of a list that scrolls is
    // not run, as the touch target size check does.
    internal static void TouchTargetWcag(PhoneCheckWindow window, PhoneCheckContext context, List<PhoneCheckResult> results)
    {
        var density = (double)context.Density;
        var targets = window.Items.Where(item => (item.Node.Clickable || item.Node.LongClickable) && item.Node.VisibleToUser && !item.Bounds.IsEmpty).ToList();
        var radius = WcagTargetMinimum / 2 * density;
        var small = targets
            .Where(item => item.Bounds.Width / density < WcagTargetMinimum || item.Bounds.Height / density < WcagTargetMinimum)
            .ToList();
        foreach (var target in small)
        {
            var bounds = target.Bounds;
            var (cx, cy) = Centre(bounds);
            PhoneCheckItem? meets = null;
            foreach (var other in targets)
            {
                if (ReferenceEquals(other, target) || Related(target, other))
                {
                    continue;
                }

                var circleMeets = small.Contains(other) && Distance(cx, cy, Centre(other.Bounds)) < 2 * radius;
                if (circleMeets || DistanceToRectangle(cx, cy, other.Bounds) < radius)
                {
                    meets = other;
                    break;
                }
            }

            if (meets is null)
            {
                continue;
            }

            var width = bounds.Width / density;
            var height = bounds.Height / density;
            var measured = string.Create(CultureInfo.InvariantCulture,
                $"{width:0.#} by {height:0.#} dp, under 24 by 24 dp, and its 24dp circle meets {meets.Label}");

            // As the touch target size check does, an item against the edge
            // of a list that scrolls may be cut off by it, so its size is
            // not its own.
            var scrollEdge = IsAgainstScrollableEdge(target);
            results.Add(new PhoneCheckResult(
                PhoneCheckNames.TouchTargetWcag,
                scrollEdge ? PhoneCheckLevel.NotRun : PhoneCheckLevel.Error,
                target.Node.Index,
                scrollEdge ? measured + "; not run, as it is against the edge of a list that scrolls, so may be cut off by it" : measured));
        }
    }

    private static bool Related(PhoneCheckItem one, PhoneCheckItem other)
    {
        for (var step = one.Parent; step is not null; step = step.Parent)
        {
            if (ReferenceEquals(step, other))
            {
                return true;
            }
        }

        for (var step = other.Parent; step is not null; step = step.Parent)
        {
            if (ReferenceEquals(step, one))
            {
                return true;
            }
        }

        return false;
    }

    private static (double X, double Y) Centre(PhoneBounds bounds) =>
        ((bounds.Left + bounds.Right) / 2.0, (bounds.Top + bounds.Bottom) / 2.0);

    private static double Distance(double x, double y, (double X, double Y) other) =>
        Math.Sqrt(((x - other.X) * (x - other.X)) + ((y - other.Y) * (y - other.Y)));

    private static double DistanceToRectangle(double x, double y, PhoneBounds bounds)
    {
        var dx = Math.Max(Math.Max(bounds.Left - x, 0), x - bounds.Right);
        var dy = Math.Max(Math.Max(bounds.Top - y, 0), y - bounds.Bottom);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    // ---- SpeakableTextPresentCheck ------------------------------------------

    internal static void SpeakableTextPresent(PhoneCheckWindow window, List<PhoneCheckResult> results)
    {
        foreach (var element in window.Items)
        {
            var node = element.Node;
            if (!node.VisibleToUser || !node.ImportantForAccessibility)
            {
                continue;
            }

            // Web content with no children is not run. ATF's Compose and
            // Flutter exceptions apply only to a hierarchy built from Views,
            // not from accessibility items as here.
            if (element.InstanceOf(WebView) && element.Children.Count == 0)
            {
                continue;
            }

            if (!ShouldFocusView(element))
            {
                continue;
            }

            if (SpeakableText(element).Length == 0)
            {
                results.Add(new PhoneCheckResult(
                    PhoneCheckNames.SpeakableText,
                    PhoneCheckLevel.Error,
                    node.Index,
                    "a screen reader would focus it, but it has no text, content description, label from another item, or text from the items in it"));
            }
        }
    }

    // ---- DuplicateSpeakableTextCheck ----------------------------------------

    internal static void DuplicateSpeakableText(PhoneCheckWindow window, List<PhoneCheckResult> results)
    {
        var byText = new Dictionary<string, List<PhoneCheckItem>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var view in window.Items)
        {
            if (!ShouldFocusView(view))
            {
                continue;
            }

            var text = SpeakableText(view).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (!byText.TryGetValue(text, out var list))
            {
                byText[text] = list = [];
                order.Add(text);
            }

            list.Add(view);
        }

        foreach (var text in order)
        {
            var views = byText[text];
            if (views.Count < 2)
            {
                continue;
            }

            var clickable = views.Where(view => view.Node.Clickable).ToList();
            var first = clickable.Count > 0 ? clickable[0] : views[0];
            var others = views.Count - 1;
            results.Add(new PhoneCheckResult(
                PhoneCheckNames.DuplicateSpeakableText,
                clickable.Count > 0 ? PhoneCheckLevel.Warning : PhoneCheckLevel.Info,
                first.Node.Index,
                string.Create(CultureInfo.InvariantCulture,
                    $"speaks \"{Shorten(text)}\", as {others} other {(others == 1 ? "item does" : "items do")}{(clickable.Count > 0 ? string.Empty : "; none is clickable")}")));
        }
    }

    // ---- DuplicateClickableBoundsCheck --------------------------------------

    internal static void DuplicateClickableBounds(PhoneCheckWindow window, List<PhoneCheckResult> results)
    {
        // ATF's map key is equal for the same bounds and a shared action,
        // clickable or long-clickable, each new item joining the first key it
        // equals, in the order the items were added.
        var groups = new List<(PhoneBounds Bounds, bool Clickable, bool LongClickable, List<PhoneCheckItem> Views)>();
        foreach (var view in window.Items)
        {
            var node = view.Node;
            if (!node.VisibleToUser || !node.ImportantForAccessibility || !(node.Clickable || node.LongClickable))
            {
                continue;
            }

            var bounds = view.Bounds;
            var at = groups.FindIndex(group => group.Bounds == bounds &&
                ((group.Clickable && node.Clickable) || (group.LongClickable && node.LongClickable)));
            if (at < 0)
            {
                groups.Add((bounds, node.Clickable, node.LongClickable, [view]));
            }
            else
            {
                groups[at].Views.Add(view);
            }
        }

        foreach (var group in groups.Where(group => group.Views.Count >= 2))
        {
            var culprit = group.Views[0];
            var others = group.Views.Skip(1).Select(view => view.Label).ToList();
            results.Add(new PhoneCheckResult(
                PhoneCheckNames.DuplicateClickableBounds,
                PhoneCheckLevel.Error,
                culprit.Node.Index,
                string.Create(CultureInfo.InvariantCulture,
                    $"the same bounds as {others.Count} other clickable {(others.Count == 1 ? "item" : "items")}: {string.Join("; ", others.Take(3))}{(others.Count > 3 ? "; and more" : string.Empty)}")));
        }
    }

    // ---- EditableContentDescCheck -------------------------------------------

    internal static void EditableContentDescription(PhoneCheckWindow window, List<PhoneCheckResult> results)
    {
        foreach (var view in window.Items)
        {
            var node = view.Node;
            if (!node.ImportantForAccessibility || !(node.Editable || view.InstanceOf(EditText)))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(node.ContentDescription))
            {
                results.Add(new PhoneCheckResult(
                    PhoneCheckNames.EditableContentDescription,
                    PhoneCheckLevel.Error,
                    node.Index,
                    $"an editable field with the content description \"{Shorten(node.ContentDescription)}\", which a screen reader says instead of what is typed in it"));
            }
        }
    }

    // ---- RedundantDescriptionCheck ------------------------------------------

    // The English words, as the recorder does not record the phone's
    // language; ATF uses the words of the phone's language.
    internal static void RedundantDescription(PhoneCheckWindow window, List<PhoneCheckResult> results)
    {
        foreach (var view in window.Items)
        {
            var node = view.Node;
            if (!node.VisibleToUser || !node.ImportantForAccessibility || string.IsNullOrEmpty(node.ContentDescription))
            {
                continue;
            }

            // ATF gives a result for each word found; the recorder gives one
            // for the item, naming every word.
            var found = RedundantWords
                .SelectMany(entry => entry.Words
                    .Where(word => ContainsWordIgnoreCase(node.ContentDescription, word))
                    .Select(word => $"the {entry.Kind} word \"{word}\""))
                .ToList();
            if (found.Count > 0)
            {
                results.Add(new PhoneCheckResult(
                    PhoneCheckNames.RedundantDescription,
                    PhoneCheckLevel.Warning,
                    node.Index,
                    $"the content description \"{Shorten(node.ContentDescription)}\" has {string.Join(" and ", found)}, which a screen reader says already"));
            }
        }
    }

    // RedundantDescriptionCheck.containsWordIgnoreCase: "(?s).*\b(?i)word\b.*".
    private static bool ContainsWordIgnoreCase(string text, string word) =>
        Regex.IsMatch(text, @"\b" + Regex.Escape(word) + @"\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // ---- ClassNameCheck -----------------------------------------------------

    internal static void ClassName(PhoneCheckWindow window, List<PhoneCheckResult> results)
    {
        foreach (var view in window.Items)
        {
            var node = view.Node;
            if (!node.ImportantForAccessibility || !node.VisibleToUser || node.ClassName is null)
            {
                continue;
            }

            if (node.ClassName.Length == 0)
            {
                results.Add(new PhoneCheckResult(
                    PhoneCheckNames.ClassName,
                    PhoneCheckLevel.Warning,
                    node.Index,
                    "its class name is empty, so a screen reader cannot say what kind of item it is"));
                continue;
            }

            if (!AndroidUiPackages.Any(prefix => node.ClassName.StartsWith(prefix, StringComparison.Ordinal)))
            {
                results.Add(new PhoneCheckResult(
                    PhoneCheckNames.ClassName,
                    PhoneCheckLevel.Warning,
                    node.Index,
                    $"its class {node.ClassName} is not one of Android's own, so a screen reader may not say what kind of item it is"));
            }
        }
    }

    // ---- TraversalOrderCheck ------------------------------------------------

    internal static void TraversalOrder(PhoneCheckWindow window, List<PhoneCheckResult> results)
    {
        var byIndex = window.Items.GroupBy(item => item.Node.Index).ToDictionary(group => group.Key, group => group.First());
        foreach (var view in window.Items)
        {
            var node = view.Node;
            if (!node.VisibleToUser || !node.ImportantForAccessibility)
            {
                continue;
            }

            var before = Chain(view, item => item.Node.TraversalBefore, byIndex);
            if (before is null)
            {
                results.Add(new PhoneCheckResult(PhoneCheckNames.TraversalOrder, PhoneCheckLevel.Warning, node.Index,
                    "its \"traversal before\" links come back to it, a loop"));
                continue;
            }

            var after = Chain(view, item => item.Node.TraversalAfter, byIndex);
            if (after is null)
            {
                results.Add(new PhoneCheckResult(PhoneCheckNames.TraversalOrder, PhoneCheckLevel.Warning, node.Index,
                    "its \"traversal after\" links come back to it, a loop"));
                continue;
            }

            var both = before.Intersect(after).Where(item => !ReferenceEquals(item, view)).ToList();
            if (both.Count > 0)
            {
                results.Add(new PhoneCheckResult(PhoneCheckNames.TraversalOrder, PhoneCheckLevel.Warning, node.Index,
                    $"its \"traversal before\" and \"traversal after\" links both lead to {both[0].Label}, so they conflict"));
            }
        }
    }

    // TraversalOrderCheck.buildNodeChain; null for a cycle.
    private static List<PhoneCheckItem>? Chain(PhoneCheckItem start, Func<PhoneCheckItem, int?> next, Dictionary<int, PhoneCheckItem> byIndex)
    {
        var chain = new List<PhoneCheckItem> { start };
        var at = start;
        while (true)
        {
            if (next(at) is not { } index || !byIndex.TryGetValue(index, out var following))
            {
                return chain;
            }

            if (chain.Contains(following))
            {
                return null;
            }

            chain.Add(following);
            at = following;
        }
    }

    // ---- ViewHierarchyElementUtils ------------------------------------------

    // getSpeakableTextForElement, in English.
    internal static string SpeakableText(PhoneCheckItem element)
    {
        var text = SpeakableTextFromSubtree(element, 0);
        if (element.Node.ImportantForAccessibility && LabeledByItem(element) is { } labeledBy)
        {
            var label = SpeakableElementTextOrLabel(labeledBy);
            if (!string.IsNullOrEmpty(label))
            {
                // ATF's template_labeled_item: "%1$s for %2$s".
                return $"{text} for {label}";
            }
        }

        return text;
    }

    // getLabeledBy: the first label, within this window.
    private static PhoneCheckItem? LabeledByItem(PhoneCheckItem element)
    {
        if (element.Node.LabeledBy.Count == 0)
        {
            return null;
        }

        var index = element.Node.LabeledBy[0];
        var parentWindow = Root(element);
        return Descendants(parentWindow).FirstOrDefault(item => item.Node.Index == index);
    }

    private static PhoneCheckItem Root(PhoneCheckItem item)
    {
        var root = item;
        var guard = 0;
        while (root.Parent is not null && guard++ < 10_000)
        {
            root = root.Parent;
        }

        return root;
    }

    private static IEnumerable<PhoneCheckItem> Descendants(PhoneCheckItem item)
    {
        var stack = new Stack<PhoneCheckItem>();
        stack.Push(item);
        var seen = new HashSet<PhoneCheckItem>();
        while (stack.Count > 0)
        {
            var next = stack.Pop();
            if (!seen.Add(next))
            {
                continue;
            }

            yield return next;
            for (var index = next.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(next.Children[index]);
            }
        }
    }

    private const int MaximumDepth = 500;

    // getSpeakableTextFromElementSubtree, with ATF's separator ", ".
    private static string SpeakableTextFromSubtree(PhoneCheckItem element, int depth)
    {
        if (element.InstanceOf(ToggleButton) || element.InstanceOf(Switch))
        {
            return element.Node.ImportantForAccessibility
                ? DedupeJoin(SwitchState(element), SwitchContent(element))
                : string.Empty;
        }

        var parts = new List<string>();
        var node = element.Node;
        if (node.ImportantForAccessibility)
        {
            if (TreeStatus(element) is { Length: > 0 } status)
            {
                parts.Add(status);
            }

            if (!string.IsNullOrEmpty(node.ContentDescription))
            {
                parts.Add(node.ContentDescription);
                return Join(parts);
            }

            if (HasTrimmedText(element.Text))
            {
                parts.Add(element.Text!);
            }

            if (element.InstanceOf(AbsListView) && element.Children.Count == 0)
            {
                // template_containers_quantity_other with value_listview.
                parts.Add("List showing 0 items.");
            }
        }

        if (depth < MaximumDepth)
        {
            foreach (var child in element.Children)
            {
                if (!IsFocusableOrClickableForAccessibility(child))
                {
                    var childText = SpeakableTextFromSubtree(child, depth + 1);
                    if (childText.Length > 0)
                    {
                        parts.Add(childText);
                    }
                }
            }
        }

        if (node.ImportantForAccessibility && HasTrimmedText(node.Hint))
        {
            parts.Add(node.Hint!);
        }

        return Join(parts);
    }

    private static string Join(List<string> parts) => string.Join(", ", parts.Where(part => part.Length > 0));

    private static bool HasTrimmedText(string? text) => !string.IsNullOrEmpty(text) && text.Trim().Length > 0;

    // getDescriptionForTreeStatus: the state description, or checked or not
    // checked for a checkable item.
    private static string? TreeStatus(PhoneCheckItem element)
    {
        if (element.Node.StateDescription is { } state)
        {
            return state;
        }

        if (element.Node.Checkable)
        {
            return element.Node.Checked == "checked" ? "checked" : "not checked";
        }

        return null;
    }

    private static string? SwitchContent(PhoneCheckItem element)
    {
        if (!string.IsNullOrEmpty(element.Node.ContentDescription))
        {
            return element.Node.ContentDescription;
        }

        return element.Node.StateDescription is not null && HasTrimmedText(element.Text) ? element.Text : null;
    }

    private static string? SwitchState(PhoneCheckItem element)
    {
        if (element.Node.StateDescription is { } state)
        {
            return state;
        }

        if (HasTrimmedText(element.Text))
        {
            return element.Text;
        }

        return element.Node.Checked == "checked" ? "on" : "off";
    }

    private static string DedupeJoin(params string?[] values)
    {
        var parts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value) || !seen.Add(value.ToLowerInvariant()))
            {
                continue;
            }

            parts.Add(value);
        }

        return Join(parts);
    }

    private static string? SpeakableElementTextOrLabel(PhoneCheckItem element)
    {
        if (!element.Node.ImportantForAccessibility)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(element.Node.ContentDescription))
        {
            return element.Node.ContentDescription;
        }

        return HasTrimmedText(element.Text) ? element.Text : null;
    }

    internal static bool ShouldFocusView(PhoneCheckItem view)
    {
        if (!view.Node.VisibleToUser)
        {
            return false;
        }

        if (IsAccessibilityFocusable(view))
        {
            return !HasAnyImportantDescendant(view, 0) || IsSpeakingView(view, 0);
        }

        return (HasText(view) || !string.IsNullOrEmpty(view.Node.StateDescription)) &&
            view.Node.ImportantForAccessibility &&
            !HasFocusableAncestor(view);
    }

    private static bool HasFocusableAncestor(PhoneCheckItem view)
    {
        for (var parent = ImportantAncestor(view); parent is not null; parent = ImportantAncestor(parent))
        {
            if (IsAccessibilityFocusable(parent))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAccessibilityFocusable(PhoneCheckItem view)
    {
        if (!view.Node.VisibleToUser || !view.Node.ImportantForAccessibility)
        {
            return false;
        }

        return IsFocusableOrClickableForAccessibility(view) || (IsChildOfScrollableContainer(view) && IsSpeakingView(view, 0));
    }

    private static bool IsFocusableOrClickableForAccessibility(PhoneCheckItem view)
    {
        var node = view.Node;
        return node.VisibleToUser && node.ImportantForAccessibility &&
            (node.ScreenReaderFocusable || node.Clickable || node.Focusable || node.LongClickable);
    }

    private static bool IsChildOfScrollableContainer(PhoneCheckItem view)
    {
        var parent = ImportantAncestor(view);
        if (parent is null)
        {
            return false;
        }

        if (parent.Node.Scrollable)
        {
            return true;
        }

        return !parent.InstanceOf(Spinner) && ScrollableContainers.Any(parent.InstanceOf);
    }

    private static bool IsSpeakingView(PhoneCheckItem view, int depth)
    {
        if (view.Node.ImportantForAccessibility && (HasText(view) || view.Node.Checkable))
        {
            return true;
        }

        if (depth >= MaximumDepth)
        {
            return false;
        }

        foreach (var child in view.Children)
        {
            if (!child.Node.VisibleToUser || IsAccessibilityFocusable(child))
            {
                continue;
            }

            if (IsSpeakingView(child, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasText(PhoneCheckItem view) =>
        !string.IsNullOrEmpty(view.Text) ||
        !string.IsNullOrEmpty(view.Node.ContentDescription) ||
        !string.IsNullOrEmpty(view.Node.Hint);

    private static PhoneCheckItem? ImportantAncestor(PhoneCheckItem view)
    {
        var parent = view.Parent;
        while (parent is not null && !parent.Node.ImportantForAccessibility)
        {
            parent = parent.Parent;
        }

        return parent;
    }

    private static bool HasAnyImportantDescendant(PhoneCheckItem element, int depth)
    {
        if (depth >= MaximumDepth)
        {
            return false;
        }

        foreach (var child in element.Children)
        {
            if (child.Node.ImportantForAccessibility)
            {
                return true;
            }

            if (child.Children.Count > 0 && HasAnyImportantDescendant(child, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    // ---- Labels -------------------------------------------------------------

    // An item as the tree view names it (PhoneAccessibilityTimeline.LabelOf):
    // its short class with its text or description, or the first three texts
    // in it.
    internal static string LabelOf(PhoneCheckItem item)
    {
        var node = item.Node;
        var shortClass = node.ClassName is { Length: > 0 } name ? name[(name.LastIndexOf('.') + 1)..] : "item";
        if (Shorten(node.Text) is { } shown)
        {
            return $"{shortClass} \"{shown}\"";
        }

        if (Shorten(node.ContentDescription) is { } described)
        {
            return $"{shortClass} described as \"{described}\"";
        }

        var texts = Descendants(item)
            .Skip(1)
            .Select(descendant => Shorten(descendant.Node.Text ?? descendant.Node.ContentDescription))
            .OfType<string>()
            .Take(3)
            .Select(text => $"\"{text}\"")
            .ToList();
        return texts.Count == 0 ? shortClass + " with no text" : $"{shortClass} containing {string.Join(", ", texts)}";
    }

    internal static string? Shorten(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var trimmed = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length == 0 ? null : trimmed.Length > 80 ? trimmed[..80].TrimEnd() + "…" : trimmed;
    }
}

using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// Step 11 of android-checks.md, the checks of fields and states, from the
/// tree alone, each one discrete issue (owner, 2026-11-13):
/// <list type="bullet">
/// <item>a field with no label (WCAG 3.3.2): no item linked to it as its
/// label (labeledBy). A hint is not a label, as it is not shown once text
/// is typed (owner), and text beside a field is not taken as its label
/// ("labeledBy only", owner); the text just above it is quoted as a
/// possible label;</item>
/// <item>a field with no name (WCAG 4.1.2): no label, no hint and no
/// content description;</item>
/// <item>an item marked invalid with no error text (WCAG 3.3.1), a
/// warning, as the error may be shown as text not linked to it;</item>
/// <item>an item that can be expanded or collapsed whose expanded state is
/// undefined, which Android's reference gives for "a non-expandable
/// element" (API 36), with no state description (WCAG 4.1.2), a
/// warning;</item>
/// <item>a slider with no range information (WCAG 4.1.2): "Range info
/// required" (owner), whatever its text or description says.</item>
/// </list>
/// Flutter's Android bridge never sets an item's error or invalid state,
/// adds expand and collapse only with an expanded state, and gives a
/// slider no range (its AccessibilityBridge and BaseRoleConfigurator), so
/// in an app drawn with Flutter the second and third checks find nothing
/// and every slider is a finding.
/// </summary>
public static class PhoneFieldChecks
{
    /// <summary>AccessibilityNodeInfo.ACTION_EXPAND.</summary>
    public const int ActionExpand = 0x40000;

    /// <summary>AccessibilityNodeInfo.ACTION_COLLAPSE.</summary>
    public const int ActionCollapse = 0x80000;

    /// <summary>android.R.id.accessibilityActionSetProgress.</summary>
    public const int ActionSetProgress = 16908349;

    /// <summary>AccessibilityNodeInfo.EXPANDED_STATE_UNDEFINED.</summary>
    public const int ExpandedStateUndefined = 0;

    /// <summary>What one window gives: its findings, and the counts the summary reports.</summary>
    public sealed record Measured(
        IReadOnlyList<PhoneCheckResult> Results, int Fields, int Invalid, int Expandable, int ExpandableNotRecorded, int Sliders);

    /// <summary>Runs the checks on one window.</summary>
    public static IReadOnlyList<PhoneCheckResult> Run(PhoneCheckWindow window, PhoneCheckContext context) =>
        Measure(window, context).Results;

    /// <summary>The checks on one window, with the items each looked at.</summary>
    public static Measured Measure(PhoneCheckWindow window, PhoneCheckContext context)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(context);
        var results = new List<PhoneCheckResult>();
        int fields = 0, invalid = 0, expandable = 0, notRecorded = 0, sliders = 0;
        foreach (var item in window.Items)
        {
            var node = item.Node;
            if (!Shown(item))
            {
                continue;
            }

            if (node.Editable)
            {
                fields++;
                Field(item, window, results);
            }

            if (node.ContentInvalid)
            {
                invalid++;
                if (string.IsNullOrWhiteSpace(node.Error))
                {
                    results.Add(new PhoneCheckResult(
                        PhoneCheckNames.InvalidNoError,
                        PhoneCheckLevel.Warning,
                        node.Index,
                        "it is marked invalid, but the tree gives it no error text, so a screen reader has no error to read with it; " +
                        "the error may be shown as text not linked to it; a likely failure, until it is looked at"));
                }
            }

            var expand = node.Actions.Any(action => action.Id == ActionExpand);
            var collapse = node.Actions.Any(action => action.Id == ActionCollapse);
            if (expand || collapse)
            {
                expandable++;
                if (node.ExpandedState is not { } expanded)
                {
                    notRecorded++;
                }
                else if (expanded == ExpandedStateUndefined && string.IsNullOrWhiteSpace(node.StateDescription))
                {
                    var can = expand && collapse ? "expanded and collapsed" : expand ? "expanded" : "collapsed";
                    results.Add(new PhoneCheckResult(
                        PhoneCheckNames.ExpandableNoState,
                        PhoneCheckLevel.Warning,
                        node.Index,
                        $"it can be {can}, but its expanded state is undefined, which Android gives for a non-expandable element, " +
                        "and it has no state description, so a screen reader may not say whether it is expanded; " +
                        "a likely failure, until it is looked at"));
                }
            }

            if (IsSlider(item))
            {
                sliders++;
                if (node.RangeInfo is null)
                {
                    var said = Said(node) is { } words ? $"; its value may be in its words, {Quote(words)}, but not as a range" : string.Empty;
                    results.Add(new PhoneCheckResult(
                        PhoneCheckNames.SliderNoRange,
                        PhoneCheckLevel.Error,
                        node.Index,
                        $"it is a slider with no range information, so its value, minimum and maximum are not given to a screen reader{said}"));
                }
            }
        }

        return new Measured(results, fields, invalid, expandable, notRecorded, sliders);
    }

    private static void Field(PhoneCheckItem field, PhoneCheckWindow window, List<PhoneCheckResult> results)
    {
        var node = field.Node;
        var labelled = node.LabeledByOutside > 0 || node.LabeledBy.Any(index =>
            window.Items.FirstOrDefault(item => item.Node.Index == index) is { } by &&
            !ReferenceEquals(by, field) && by.Node.VisibleToUser);
        if (labelled)
        {
            return;
        }

        var hint = string.IsNullOrWhiteSpace(node.Hint) ? null : node.Hint.Trim();
        var above = Above(field, window) is { } text
            ? $"; the text just above it, {Quote(text)}, is not linked to it as its label"
            : string.Empty;
        var why = hint is null
            ? "it has no label: no hint, and no item linked to it as its label (labeledBy)"
            : $"its only label is its hint, {Quote(hint)}, which is not shown once text is typed, and no item is linked to it as its label (labeledBy)";
        results.Add(new PhoneCheckResult(PhoneCheckNames.FieldNoLabel, PhoneCheckLevel.Error, node.Index, why + above));

        if (hint is null && string.IsNullOrWhiteSpace(node.ContentDescription))
        {
            results.Add(new PhoneCheckResult(
                PhoneCheckNames.FieldNoName,
                PhoneCheckLevel.Error,
                node.Index,
                "it has no name: no item linked to it as its label, no hint and no content description, so a screen reader can say only what is typed in it"));
        }
    }

    // The nearest shown item ending above the field, across from it, no
    // further above it than the field is tall, with text or a description,
    // and not a control. The field's height, not a distance in dp, as older
    // recordings have no density.
    private static string? Above(PhoneCheckItem field, PhoneCheckWindow window)
    {
        var box = field.Bounds;
        var reach = box.Bottom - box.Top;
        PhoneCheckItem? best = null;
        foreach (var item in window.Items)
        {
            var node = item.Node;
            if (ReferenceEquals(item, field) || !Shown(item) || node.Clickable || node.LongClickable || node.Checkable || node.Editable ||
                Said(node) is null)
            {
                continue;
            }

            var b = item.Bounds;
            var gap = box.Top - b.Bottom;
            if (gap < 0 || gap > reach || b.Right <= box.Left || b.Left >= box.Right)
            {
                continue;
            }

            if (best is null || b.Bottom > best.Bounds.Bottom)
            {
                best = item;
            }
        }

        return best is null ? null : Said(best.Node);
    }

    private static string? Said(AndroidTreeNode node)
    {
        foreach (var words in new[] { node.Text, node.ContentDescription, node.StateDescription })
        {
            if (!string.IsNullOrWhiteSpace(words))
            {
                return words.Trim();
            }
        }

        return null;
    }

    private static bool IsSlider(PhoneCheckItem item)
    {
        var name = item.Node.ClassName ?? string.Empty;
        return name.Contains("SeekBar", StringComparison.Ordinal) ||
            name.Contains("RatingBar", StringComparison.Ordinal) ||
            item.Node.Actions.Any(action => action.Id == ActionSetProgress);
    }

    private static string Quote(string words)
    {
        var one = string.Join(" ", words.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return "\"" + (one.Length > 60 ? string.Concat(one.AsSpan(0, 60), "...") : one) + "\"";
    }

    private static bool Shown(PhoneCheckItem item) =>
        item.Node.VisibleToUser && item.Bounds.Right > item.Bounds.Left && item.Bounds.Bottom > item.Bounds.Top;
}

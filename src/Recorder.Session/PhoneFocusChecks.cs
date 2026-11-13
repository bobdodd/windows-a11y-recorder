using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// Step 10 of android-checks.md, clickable but not focusable (WCAG 2.1.1
/// Keyboard, level A, which WCAG2ICT applies as written), from the tree
/// alone (owner, 2026-11-12). Android makes a view focusable by default
/// when it is clickable or long clickable (FOCUSABLE_AUTO, the View
/// reference), so a clickable item that is not focusable has usually been
/// made so, and a keyboard or switch user may not reach it. A warning, as
/// a likely failure until a keyboard test confirms it. An item inside a
/// focusable clickable item is set aside, as that item takes the focus.
/// Not every toolkit's tree says what the keyboard reaches: Flutter's
/// bridge marks focusable any node with an action, label, value or hint,
/// its "flags that make a node accessibility focusable" (its
/// AccessibilityBridge), so in such an app no clickable item is ever not
/// focusable. The items with text that are not controls show it: a view's
/// text is not focusable by default, so when every one of them is
/// focusable, focusable in that tree is about a screen reader.
/// </summary>
public static class PhoneFocusChecks
{
    /// <summary>What one window gives: its findings, and the counts the summary reports.</summary>
    public sealed record Measured(
        IReadOnlyList<PhoneCheckResult> Results, int Clickable, int SetAside, int TextItems, int FocusableTextItems);

    /// <summary>Runs the check on one window.</summary>
    public static IReadOnlyList<PhoneCheckResult> Run(PhoneCheckWindow window, PhoneCheckContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Measure(window).Results;
    }

    /// <summary>
    /// The check on one window, with the clickable items checked, those set
    /// aside inside a focusable clickable item, and the items with text
    /// that are not controls, with how many of them are focusable.
    /// </summary>
    public static Measured Measure(PhoneCheckWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var results = new List<PhoneCheckResult>();
        int clickable = 0, setAside = 0, textItems = 0, focusableText = 0;
        foreach (var item in window.Items)
        {
            var node = item.Node;
            if (!Shown(item))
            {
                continue;
            }

            if (IsControl(node))
            {
                if (!node.Enabled || !(node.Clickable || node.LongClickable))
                {
                    continue;
                }

                if (InsideFocusableControl(item))
                {
                    setAside++;
                    continue;
                }

                clickable++;
                if (!node.Focusable)
                {
                    var how = node.Clickable ? "clickable" : "long clickable";
                    results.Add(new PhoneCheckResult(
                        PhoneCheckNames.ClickableNotFocusable,
                        PhoneCheckLevel.Warning,
                        node.Index,
                        $"it is {how} but not focusable, and no item it is in is focusable and clickable, so a keyboard or switch user may not reach it; Android makes a clickable view focusable unless the app says not; a likely failure, until a keyboard test confirms it"));
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(node.Text) || !string.IsNullOrWhiteSpace(node.ContentDescription))
            {
                textItems++;
                if (node.Focusable)
                {
                    focusableText++;
                }
            }
        }

        return new Measured(results, clickable, setAside, textItems, focusableText);
    }

    private static bool IsControl(AndroidTreeNode node) =>
        node.Clickable || node.LongClickable || node.Checkable || node.Editable;

    private static bool InsideFocusableControl(PhoneCheckItem item)
    {
        var guard = 0;
        for (var step = item.Parent; step is not null && guard++ < 1000; step = step.Parent)
        {
            if (step.Node.Focusable && (step.Node.Clickable || step.Node.LongClickable))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Shown(PhoneCheckItem item) =>
        item.Node.VisibleToUser && item.Bounds.Right > item.Bounds.Left && item.Bounds.Bottom > item.Bounds.Top;
}

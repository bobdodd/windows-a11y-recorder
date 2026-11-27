using System.Text;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

public sealed record AndroidInteractionWord(string Text, AndroidNodeBounds Bounds, int Line);

public sealed record AndroidInteractionCandidate(string Label, string Evidence,
    IReadOnlyList<AndroidNodeBounds> Boxes, bool? Enabled, int? NodeIndex, string? Role = null);

internal readonly record struct AndroidInteractionPoint(int X, int Y, int Width, int Height, int Rotation);

public static class AndroidInteractionInventory
{
    // Android's legacy AccessibilityNodeInfo action bits and the newer
    // framework resource IDs. Keep unknown IDs and custom labels verbatim.
    private static readonly Dictionary<int, string> ActionNames = new()
    {
        [4] = "Select", [8] = "Clear selection", [16] = "Activate", [32] = "Long press",
        [4096] = "Scroll forward", [8192] = "Scroll backward",
        [16384] = "Copy", [32768] = "Paste", [65536] = "Cut", [131072] = "Set selection",
        [262144] = "Expand", [524288] = "Collapse", [1048576] = "Dismiss", [2097152] = "Set text",
        [0x01020037] = "Scroll to position", [0x01020038] = "Scroll up", [0x01020039] = "Scroll left",
        [0x0102003a] = "Scroll down", [0x0102003b] = "Scroll right", [0x0102003c] = "Context click",
        [0x0102003d] = "Set progress", [0x01020042] = "Move window", [0x01020046] = "Page up",
        [0x01020047] = "Page down", [0x01020048] = "Page left", [0x01020049] = "Page right",
        [0x0102004a] = "Press and hold", [0x01020054] = "IME enter", [0x01020055] = "Drag start",
        [0x01020056] = "Drop", [0x01020057] = "Cancel drag", [0x01020058] = "Show text suggestions",
        [0x0102005e] = "Scroll in direction"
    };

    // Traversal, accessibility bookkeeping, and tooltip display by themselves
    // do not make a node a control. Text spans get their own rows instead.
    private static bool NavigationOnly(int id) => id is
        0 or 1 or 2 or 64 or 128 or 256 or 512 or 1024 or 2048 or 0x01020036 or 0x01020044 or 0x01020045;

    public static IReadOnlyList<AndroidInteractionCandidate> Declared(AndroidLiveWindowSnapshot app)
    {
        if (app.Items is not { } items)
        {
            return [];
        }
        var labels = items.Nodes.ToDictionary(node => node.Index);
        var result = new List<AndroidInteractionCandidate>();
        foreach (var node in items.Nodes)
        {
            if (!node.VisibleToUser || Intersection(node.Bounds, app.Window.Bounds) is not { } box)
            {
                continue;
            }
            var actions = new List<string>();
            void Flag(bool flag, string name)
            {
                if (flag)
                {
                    actions.Add(name);
                }
            }

            Flag(node.Clickable, "Activate");
            Flag(node.LongClickable, "Long press");
            Flag(node.ContextClickable, "Context click");
            Flag(node.Checkable, "Checkable");
            Flag(node.Editable, "Editable");
            Flag(node.Scrollable, "Scrollable");
            Flag(node.Dismissable, "Dismiss");
            Flag(node.RangeInfo is not null && !node.Actions.Any(action => action.Id == 0x0102003d) &&
                (node.Clickable || node.Scrollable || node.Actions.Any(action => !NavigationOnly(action.Id))), "Has range");
            actions.AddRange(node.Actions.Where(action => !NavigationOnly(action.Id)).Select(action =>
                string.IsNullOrWhiteSpace(action.Label)
                    ? ActionNames.GetValueOrDefault(action.Id, $"Action {action.Id}")
                    : $"{action.Label} (action {action.Id})"));
            if (actions.Count > 0)
            {
                var linkedLabels = node.LabeledBy.Select(index => labels.GetValueOrDefault(index))
                    .OfType<AndroidTreeNode>().Where(label => !label.Password).Select(label => First(label.ContentDescription, label.Text));
                var label = node.Password ? First(node.Hint, "Password item") : First(node.ContentDescription, node.Text,
                    string.Join(" ", linkedLabels), node.Hint, node.ViewId, "Unnamed item");
                result.Add(new(label,
                    string.Join(", ", actions.Distinct()), [box], node.Enabled, node.Index, node.ClassName));
            }
            foreach (var link in node.Password ? [] : node.Links ?? [])
            {
                var boxes = link.Boxes.Select(part => Intersection(part, box)).OfType<AndroidNodeBounds>().ToArray();
                if (link.Boxes.Count > 0 && boxes.Length == 0)
                {
                    continue;
                }
                var text = node.Text ?? "";
                var label = link.Start >= 0 && link.End > link.Start && link.End <= text.Length
                    ? text[link.Start..link.End] : "Unnamed inline link";
                result.Add(new(label, boxes.Length > 0 ? "Inline link" : "Inline link; on-screen position not reported",
                    boxes, node.Enabled, node.Index, "Inline link"));
            }
        }
        return result;
    }

    public static IReadOnlyList<AndroidInteractionCandidate> Visual(AndroidLiveWindowSnapshot app,
        IReadOnlyList<AndroidInteractionCandidate> declared, IEnumerable<AndroidInteractionWord> words,
        AndroidNodeBounds picture, IEnumerable<AndroidNodeBounds> occluders)
    {
        var overlays = occluders.ToArray();
        var controls = declared.Select(item => (item.Boxes,
            Words: item.Label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Normalized)
                .Where(word => word.Length > 0).ToHashSet(StringComparer.Ordinal))).ToArray();
        var privateBoxes = app.Items?.Nodes.Where(node => node.Password).Select(node => node.Bounds).ToArray() ?? [];
        var result = new List<AndroidInteractionCandidate>();
        foreach (var line in words.Where(word => Contains(app.Window.Bounds, word.Bounds) && Contains(picture, word.Bounds) &&
                     !overlays.Any(overlay => Intersection(overlay, word.Bounds) is not null) &&
                     !privateBoxes.Any(box => Intersection(box, word.Bounds) is not null)).GroupBy(word => word.Line))
        {
            var run = new List<AndroidInteractionWord>();
            void AddRun()
            {
                if (run.Count == 0)
                {
                    return;
                }
                var bounds = new AndroidNodeBounds(run.Min(word => word.Bounds.Left), run.Min(word => word.Bounds.Top),
                    run.Max(word => word.Bounds.Right), run.Max(word => word.Bounds.Bottom));
                var text = string.Join(" ", run.Select(word => word.Text));
                if (!overlays.Any(overlay => Intersection(overlay, bounds) is not null))
                {
                    result.Add(new(text, "Visible OCR text without a matching declared control label at that location; may be static",
                        [bounds], null, null));
                }
                run.Clear();
            }

            foreach (var word in line)
            {
                var normalized = Normalized(word.Text);
                if (controls.Any(control => control.Words.Contains(normalized) &&
                    control.Boxes.Any(box => CoversMost(box, word.Bounds))))
                {
                    AddRun();
                    continue;
                }
                if (run.Count > 0 && Math.Max(word.Bounds.Left - run[^1].Bounds.Right, run[^1].Bounds.Left - word.Bounds.Right) >
                    1.5 * (word.Bounds.Bottom - word.Bounds.Top))
                {
                    AddRun();
                }
                run.Add(word);
            }
            AddRun();
        }
        return result;
    }

    internal static string Normalized(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit)).ToUpperInvariant();

    internal static AndroidNodeBounds? Intersection(AndroidNodeBounds a, AndroidNodeBounds b)
    {
        var box = new AndroidNodeBounds(Math.Max(a.Left, b.Left), Math.Max(a.Top, b.Top),
            Math.Min(a.Right, b.Right), Math.Min(a.Bottom, b.Bottom));
        return Area(box) > 0 ? box : null;
    }

    internal static bool Contains(AndroidNodeBounds outer, AndroidNodeBounds inner) =>
        Area(inner) > 0 && outer.Left <= inner.Left && outer.Top <= inner.Top && outer.Right >= inner.Right &&
        outer.Bottom >= inner.Bottom;

    private static bool CoversMost(AndroidNodeBounds control, AndroidNodeBounds word) =>
        Intersection(control, word) is { } overlap && Area(overlap) >= Area(word) * .8;

    private static long Area(AndroidNodeBounds box) =>
        Math.Max(0L, (long)box.Right - box.Left) * Math.Max(0L, (long)box.Bottom - box.Top);

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}

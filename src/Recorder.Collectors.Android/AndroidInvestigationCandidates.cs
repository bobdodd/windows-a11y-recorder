using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

public sealed record AndroidVisualInvestigation(IReadOnlyList<AndroidInvestigationCandidate> Candidates,
    IReadOnlyList<AndroidInteractionCandidate> UnresolvedText, bool NeedsVision, bool ShapeLimit)
{
    public IReadOnlyList<AndroidInvestigationCandidate> WithProposals(AndroidInteractionSnapshot snapshot,
        IEnumerable<(AndroidNodeBounds Box, string Label)> proposals)
    {
        var result = Candidates.ToList();
        var screen = snapshot.Frame!;
        var privateBounds = snapshot.App.Items!.Nodes.Where(node => node.Password).Select(node => node.Bounds).ToArray();
        foreach (var (box, label) in proposals.Take(24))
        {
            if (Area(box) > Area(snapshot.App.Window.Bounds) / 3 || box.Right - box.Left < 16 || box.Bottom - box.Top < 16 ||
                !AndroidInteractionInventory.Contains(snapshot.App.Window.Bounds, box) ||
                !AndroidInteractionInventory.Contains(new(0, 0, screen.Width, screen.Height), box) ||
                snapshot.Occluders.Concat(privateBounds).Any(overlay => AndroidInteractionInventory.Intersection(overlay, box) is not null) ||
                result.Where(candidate => candidate.Tap || candidate.Item.Role == "Inline link" || candidate.Item.Enabled == false)
                    .SelectMany(candidate => candidate.Item.Boxes).Any(other => Similar(box, other)))
            {
                continue;
            }
            var read = UnresolvedText.FirstOrDefault(candidate =>
                candidate.Boxes.Any(other => AndroidInteractionInventory.Contains(box, other)));
            result.Add(new(new(read?.Label ?? label, "Possible control proposed by local vision; not Android-declared",
                [box], null, null), true, true));
        }
        return result;
    }

    internal static bool Similar(AndroidNodeBounds a, AndroidNodeBounds b) =>
        AndroidInteractionInventory.Intersection(a, b) is { } overlap &&
        Area(overlap) >= .65 * Math.Min(Area(a), Area(b)) && Math.Max(Area(a), Area(b)) <= 5 * Math.Min(Area(a), Area(b));

    internal static long Area(AndroidNodeBounds box) => (long)(box.Right - box.Left) * (box.Bottom - box.Top);
}

public static class AndroidInvestigationCandidates
{
    public static IReadOnlyList<(AndroidNodeBounds Box, string Label)>? ParseProposals(string content, AndroidNodeBounds crop)
    {
        using var json = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 6 });
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
            !root.TryGetProperty("targets", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 24)
        {
            return null;
        }
        var width = crop.Right - crop.Left;
        var height = crop.Bottom - crop.Top;
        var result = new List<(AndroidNodeBounds, string)>();
        foreach (var target in entries.EnumerateArray())
        {
            if (target.ValueKind != JsonValueKind.Object || target.EnumerateObject().Count() != 2 ||
                !target.TryGetProperty("box", out var box) || box.ValueKind != JsonValueKind.Array || box.GetArrayLength() != 4 ||
                !target.TryGetProperty("label", out var named) || named.ValueKind != JsonValueKind.String || named.GetString() is not { } label ||
                label.Length is 0 or > 80 || label.Any(char.IsControl) || !label.Any(char.IsLetterOrDigit))
            {
                return null;
            }
            var coords = new int[4];
            for (var i = 0; i < coords.Length; i++)
            {
                if (box[i].ValueKind != JsonValueKind.Number || !box[i].TryGetInt32(out coords[i]) || coords[i] is < 0 or > 1000)
                {
                    return null;
                }
            }
            if (coords[0] >= coords[2] || coords[1] >= coords[3])
            {
                return null;
            }
            result.Add((new(crop.Left + (int)((long)coords[0] * width / 1000), crop.Top + (int)((long)coords[1] * height / 1000),
                crop.Left + (int)((long)coords[2] * width / 1000), crop.Top + (int)((long)coords[3] * height / 1000)), label.Trim()));
        }
        return result;
    }

    public static AndroidVisualInvestigation Find(AndroidInteractionSnapshot snapshot, IEnumerable<AndroidInteractionWord> words)
    {
        var declared = AndroidInteractionInventory.Declared(snapshot.App);
        var indices = declared.Where(item => item.Role != "Inline link").Select(item => item.NodeIndex).ToHashSet();
        var nodes = snapshot.App.Items!.Nodes.ToDictionary(item => item.Index);
        var rows = new List<AndroidInvestigationCandidate>();
        foreach (var item in declared)
        {
            var node = nodes[item.NodeIndex!.Value];
            rows.Add(new(item, SampleByTouch: true, Tap: item.Role == "Inline link" ||
                node.Clickable || node.Checkable || node.Editable || node.Actions.Any(action => action.Id == 16) || ApparentRole(node.ClassName)));
        }
        foreach (var node in nodes.Values)
        {
            if (!node.VisibleToUser || indices.Contains(node.Index) ||
                AndroidInteractionInventory.Intersection(node.Bounds, snapshot.App.Window.Bounds) is not { } box)
            {
                continue;
            }
            var apparent = ApparentRole(node.ClassName);
            if (apparent || node.Focusable || node.ScreenReaderFocusable || node.Heading ||
                !string.IsNullOrWhiteSpace(node.Text) || !string.IsNullOrWhiteSpace(node.ContentDescription) ||
                !string.IsNullOrWhiteSpace(node.Hint))
            {
                rows.Add(new(new(AndroidTalkBackExploration.ItemLabel(node), apparent ?
                    "Android reports a control class without a declared activation action" :
                    "Visible Android item with no declared action", [box], node.Enabled, node.Index, node.ClassName), true, apparent));
            }
        }
        if (!snapshot.HasScreenCoordinates || snapshot.Frame is not { } frame)
        {
            return new(rows, [], false, false);
        }
        var occluders = snapshot.Occluders;
        var uncovered = AndroidInteractionInventory.Visual(snapshot.App,
            rows.Select(row => row.Item).ToArray(), words, new(0, 0, frame.Width, frame.Height), occluders);
        var shapes = AndroidInteractionShapes.Find(frame, snapshot.App,
            rows.Where(row => row.Tap).Select(row => row.Item), occluders, out var limited);
        var paired = new HashSet<AndroidInteractionCandidate>();
        foreach (var shape in shapes.OrderBy(item => AndroidVisualInvestigation.Area(item.Boxes[0])))
        {
            var outline = shape.Boxes[0];
            if (uncovered.Any(text => text.Boxes.Any(box => AndroidInteractionInventory.Contains(box, outline))))
            {
                continue; // Letter strokes are not standalone icons.
            }
            var labels = uncovered.Where(text => !paired.Contains(text) &&
                text.Boxes.Any(box => AndroidInteractionInventory.Contains(outline, box))).ToArray();
            foreach (var label in labels)
            {
                paired.Add(label);
            }
            var evidence = labels.Length > 0 ? "Visible label inside a contrast outline; possible control" :
                "Unlabelled contrast outline; possible control or decoration";
            rows.Add(new(shape with { Label = labels.Length == 0 ? shape.Label :
                string.Join(" ", labels.Select(label => label.Label)), Evidence = evidence }, true, true));
        }
        var unresolved = uncovered.Where(item => !paired.Contains(item)).ToArray();
        return new(rows, unresolved, limited || unresolved.Length > 0 ||
            rows.Any(row => row.Item.NodeIndex is null && row.Item.Label == "Unlabelled screen shape") ||
            nodes.Values.Any(node => node.Depth > 0 && node.VisibleToUser && node.ChildCount == 0 &&
                !indices.Contains(node.Index) && !node.Focusable && !node.ScreenReaderFocusable &&
                string.IsNullOrWhiteSpace(node.Text) && string.IsNullOrWhiteSpace(node.ContentDescription) &&
                AndroidVisualInvestigation.Area(node.Bounds) > 2500), limited);
    }

    private static bool ApparentRole(string? role) => role?.Split('.').LastOrDefault() is
        "Button" or "ImageButton" or "ToggleButton" or "Switch" or "CheckBox" or "RadioButton" or "EditText" or "Spinner";
}

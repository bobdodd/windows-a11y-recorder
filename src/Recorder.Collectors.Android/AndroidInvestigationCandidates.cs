using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

public sealed record AndroidVisionMapping(IReadOnlyList<AndroidInvestigationCandidate> Candidates,
    int Matched, int ModelOnly, int TreeOnly, int NotUsed);

public static class AndroidInvestigationCandidates
{
    public const int ProposalLimit = 40;

    public static IReadOnlyList<(AndroidNodeBounds Box, string Label)>? ParseProposals(string content, AndroidNodeBounds crop)
    {
        using var json = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 6 });
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
            !root.TryGetProperty("targets", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > ProposalLimit)
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

    public static AndroidVisionMapping Map(AndroidInteractionSnapshot snapshot,
        IReadOnlyList<(AndroidNodeBounds Box, string Label)> proposals)
    {
        var declared = AndroidInteractionInventory.Declared(snapshot.App);
        var nodes = snapshot.App.Items!.Nodes.ToDictionary(item => item.Index);
        var declaredRows = declared.Select(item =>
        {
            var node = nodes[item.NodeIndex!.Value];
            return new AndroidInvestigationCandidate(item, SampleByTouch: true, Tap: item.Role == "Inline link" ||
                node.Clickable || node.Checkable || node.Editable || node.Actions.Any(action => action.Id == 16));
        }).ToArray();
        var declaredNodes = declared.Where(item => item.Role != "Inline link").Select(item => item.NodeIndex!.Value).ToHashSet();
        // Match against visible nodes, not just nodes that declare an action. A
        // label in the tree may map to a model target without declaring a click.
        var otherRows = nodes.Values.Where(node => !node.Password && node.VisibleToUser && !declaredNodes.Contains(node.Index))
            .Select(node => (Node: node, Bounds: AndroidInteractionInventory.Intersection(node.Bounds, snapshot.App.Window.Bounds)))
            .Where(entry => entry.Bounds is not null && (entry.Node.Depth > 0 || entry.Node.ChildCount == 0))
            .Select(entry => new AndroidInvestigationCandidate(new(AndroidTalkBackExploration.ItemLabel(entry.Node),
                    "Android node has no declared action", [entry.Bounds!], entry.Node.Enabled, entry.Node.Index, entry.Node.ClassName),
                SampleByTouch: true, Tap: false))
            .ToArray();
        var declaredSet = declaredRows.ToHashSet();
        var comparable = declaredRows.Concat(otherRows).Where(row => !nodes[row.Item.NodeIndex!.Value].Password)
            .Select(row =>
            {
                var node = nodes[row.Item.NodeIndex!.Value];
                var names = row.Item.Role == "Inline link" ? [row.Item.Label] :
                    new[] { row.Item.Label, node.Text, node.ContentDescription, node.Hint };
                return (Row: row, Names: names.Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => Normalized(name!)).Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal));
            }).ToArray();
        var protectedAreas = snapshot.Occluders.Concat(nodes.Values.Where(node => node.Password).Select(node => node.Bounds)).ToArray();
        var used = new HashSet<AndroidInvestigationCandidate>();
        var accepted = new List<(AndroidNodeBounds Box, string Name)>();
        var selected = new List<AndroidInvestigationCandidate>();
        var matched = 0;
        var modelOnly = 0;
        var notUsed = 0;
        foreach (var (box, label) in proposals.Take(ProposalLimit))
        {
            var name = Normalized(label);
            if (!snapshot.HasScreenCoordinates || snapshot.Frame is not { } frame ||
                box.Right - box.Left < 10 || box.Bottom - box.Top < 10 ||
                Area(box) > Area(snapshot.App.Window.Bounds) / 3 ||
                !AndroidInteractionInventory.Contains(snapshot.App.Window.Bounds, box) ||
                !AndroidInteractionInventory.Contains(new(0, 0, frame.Width, frame.Height), box) ||
                protectedAreas.Any(other => AndroidInteractionInventory.Intersection(other, box) is not null) ||
                accepted.Any(previous => Duplicate(box, name, previous.Box, previous.Name)))
            {
                notUsed++;
                continue;
            }
            var ranked = comparable.Select(entry => (entry.Row, Score: entry.Row.Item.Boxes
                    .Select(bounds => MatchScore(box, bounds, entry.Names.Contains(name), entry.Row.Tap))
                    .DefaultIfEmpty(0).Max()))
                .Where(entry => entry.Score > 0).OrderByDescending(entry => entry.Score).ToArray();
            var nearDisabled = ranked.Any(entry => entry.Row.Item.Enabled == false);
            if (ranked.Length == 1 || (ranked.Length > 1 && ranked[0].Score - ranked[1].Score >= .12))
            {
                var row = ranked[0].Row;
                if (!used.Add(row))
                {
                    // A repeated proposal for the same uniquely matched node
                    // is never tested a second time.
                    notUsed++;
                    continue;
                }
                var hasAction = declaredSet.Contains(row);
                var target = row.Item.Boxes.Select(bounds => AndroidInteractionInventory.Intersection(box, bounds))
                    .OfType<AndroidNodeBounds>().OrderByDescending(Area).First();
                selected.Add(row with
                {
                    Item = row.Item with
                    {
                        Evidence = $"Model suggestion \"{label}\" matched to Android; {row.Item.Evidence}" +
                            (nearDisabled && row.Item.Enabled != false ? "; nearby plausible tree match is disabled" : ""),
                        Boxes = [target]
                    },
                    VisionLabel = label,
                    // An explicit scroll/long-press-only declaration is not a declaration of a tap.
                    Tap = (row.Tap || !hasAction) && (!nearDisabled || row.Item.Enabled == false)
                });
                matched++;
            }
            else
            {
                var evidence = ranked.Length > 0 ? "Model suggestion; Android tree match was ambiguous" :
                    "Model suggestion; no matching Android tree node";
                if (nearDisabled)
                {
                    evidence += "; a plausible Android match is disabled";
                }
                selected.Add(new(new(label, evidence, [box], null, null), true, !nearDisabled) { VisionLabel = label });
                modelOnly++;
            }
            accepted.Add((box, name));
        }
        var treeOnly = declaredRows.Where(row => !used.Contains(row)).Select(row =>
            row with { Item = row.Item with { Evidence = $"Android tree only; {row.Item.Evidence}" } }).ToArray();
        selected.AddRange(treeOnly);
        return new(selected, matched, modelOnly, treeOnly.Length, notUsed + Math.Max(0, proposals.Count - ProposalLimit));
    }

    private static string Normalized(string label) => AndroidInteractionInventory.Normalized(label);

    private static double MatchScore(AndroidNodeBounds model, AndroidNodeBounds tree, bool sameName, bool actionable)
    {
        if (AndroidInteractionInventory.Intersection(model, tree) is not { } intersection)
        {
            return 0;
        }
        var modelArea = Area(model);
        var treeArea = Area(tree);
        var overlap = Area(intersection);
        var ratio = (double)Math.Min(modelArea, treeArea) / Math.Max(modelArea, treeArea);
        var iou = (double)overlap / (modelArea + treeArea - overlap);
        if (sameName && ratio >= .16 && overlap >= .75 * Math.Min(modelArea, treeArea))
        {
            return 1 + iou + (actionable ? .15 : 0);
        }
        if (iou < (actionable ? .46 : .72))
        {
            return 0;
        }
        return iou + (actionable ? .12 : 0);
    }

    private static bool Duplicate(AndroidNodeBounds first, string firstName, AndroidNodeBounds second, string secondName)
    {
        if (AndroidInteractionInventory.Intersection(first, second) is not { } intersection)
        {
            return false;
        }
        var area = Area(intersection);
        return (double)area / (Area(first) + Area(second) - area) >= (firstName == secondName ? .65 : .88);
    }

    private static long Area(AndroidNodeBounds box) => (long)(box.Right - box.Left) * (box.Bottom - box.Top);
}

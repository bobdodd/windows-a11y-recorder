using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

public sealed record AndroidVisionMapping(IReadOnlyList<AndroidInvestigationCandidate> Candidates,
    int Matched, int ModelOnly, int TreeOnly, int NotUsed, int Duplicates);

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
        var eligible = new List<VisionProposal>();
        var omitted = Math.Max(0, proposals.Count - ProposalLimit);
        foreach (var (box, label) in proposals.Take(ProposalLimit))
        {
            if (!snapshot.HasScreenCoordinates || snapshot.Frame is not { } frame ||
                box.Right - box.Left < 10 || box.Bottom - box.Top < 10 ||
                Area(box) > Area(snapshot.App.Window.Bounds) / 3 ||
                !AndroidInteractionInventory.Contains(snapshot.App.Window.Bounds, box) ||
                !AndroidInteractionInventory.Contains(new(0, 0, frame.Width, frame.Height), box) ||
                protectedAreas.Any(other => AndroidInteractionInventory.Intersection(other, box) is not null))
            {
                omitted++;
                continue;
            }
            var name = Normalized(label);
            var ranked = comparable.Select(entry =>
                {
                    var best = entry.Row.Item.Boxes.Select(bounds =>
                            (Bounds: bounds, Score: MatchScore(box, bounds, entry.Names.Contains(name), entry.Row.Tap)))
                        .OrderByDescending(part => part.Score).FirstOrDefault();
                    return (entry.Row, best.Bounds, best.Score);
                })
                .Where(entry => entry.Score > 0).OrderByDescending(entry => entry.Score).ToArray();
            var match = ranked.Length == 1 || (ranked.Length > 1 && ranked[0].Score - ranked[1].Score >= .12)
                ? new TreeMatch(ranked[0].Row, ranked[0].Bounds!, ranked[0].Score) : null;
            eligible.Add(new(box, label, name, match, ranked.Length > 0 && match is null, ranked.Any(entry => entry.Row.Item.Enabled == false)));
        }

        var accepted = new List<KeptProposal>();
        var duplicates = 0;
        // The best supported whole control should not lose to a fragment just
        // because the model happened to list the fragment first.
        foreach (var proposal in eligible.OrderByDescending(entry => entry.Match?.Row.Tap == true)
            .ThenByDescending(entry => Area(entry.Box)).ThenByDescending(entry => entry.Match?.Score ?? 0))
        {
            // Route an alias to its own known control before considering overlap
            // with any other retained rectangle.
            var duplicate = proposal.Match is { } match
                ? accepted.FirstOrDefault(entry => entry.Choice.Match?.Row == match.Row) : null;
            duplicate ??= accepted.FirstOrDefault(entry => SharesTargetGeometry(entry.Choice, proposal));
            if (duplicate is null)
            {
                accepted.Add(new(proposal));
                continue;
            }
            duplicates++;
            duplicate.DisabledAlias |= proposal.NearDisabled;
            duplicate.ConsequentialAlias |= AndroidInteractionExploration.SensitiveAction(proposal.Label) ||
                (proposal.Match is { } alias && AndroidInteractionExploration.SensitiveAction(alias.Row.Item.Label));
        }

        var used = new HashSet<AndroidInvestigationCandidate>();
        var selected = new List<AndroidInvestigationCandidate>();
        var matched = 0;
        var modelOnly = 0;
        foreach (var entry in accepted)
        {
            var (box, label, _, match, ambiguous, nearDisabled) = entry.Choice;
            var restriction = entry.DisabledAlias ? "an overlapping model suggestion could map to a disabled Android item" :
                entry.ConsequentialAlias ? "an overlapping model suggestion indicates a potentially consequential action" : null;
            var restrictionText = restriction is null ? "" : $"; not tapped because {restriction}";
            if (match is not null)
            {
                var row = match.Row;
                used.Add(row);
                var hasAction = declaredSet.Contains(row);
                var target = AndroidInteractionInventory.Intersection(box, match.Bounds)!;
                selected.Add(row with
                {
                    Item = row.Item with
                    {
                        Evidence = $"Model suggestion \"{label}\" matched to Android; {row.Item.Evidence}" +
                            (nearDisabled && row.Item.Enabled != false ? "; nearby plausible tree match is disabled" : "") + restrictionText,
                        Boxes = [target]
                    },
                    VisionLabel = label,
                    DuplicateTapRestriction = restriction,
                    // An explicit scroll/long-press-only declaration is not a declaration of a tap.
                    Tap = restriction is null && (row.Tap || !hasAction) && (!nearDisabled || row.Item.Enabled == false)
                });
                matched++;
            }
            else
            {
                var evidence = ambiguous ? "Model suggestion; Android tree match was ambiguous" :
                    "Model suggestion; no matching Android tree node";
                if (nearDisabled)
                {
                    evidence += "; a plausible Android match is disabled";
                }
                selected.Add(new(new(label, evidence + restrictionText, [box], null, null), true, !nearDisabled && restriction is null)
                {
                    VisionLabel = label,
                    DuplicateTapRestriction = restriction
                });
                modelOnly++;
            }
        }
        var treeOnly = declaredRows.Where(row => !used.Contains(row)).Select(row =>
            row with { Item = row.Item with { Evidence = $"Android tree only; {row.Item.Evidence}" } }).ToArray();
        selected.AddRange(treeOnly);
        return new(selected, matched, modelOnly, treeOnly.Length, omitted + duplicates, duplicates);
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

    private static bool SharesTargetGeometry(VisionProposal first, VisionProposal second)
    {
        // Keep independently actionable Android targets with distinguishable
        // hit locations, including a small button on a tappable surface.
        if (first.Match is { } a && second.Match is { } b && a.Row.Tap && b.Row.Tap &&
            (!ContainsCenter(a.Bounds, b.Bounds) || !ContainsCenter(b.Bounds, a.Bounds)))
        {
            return false;
        }
        if (AndroidInteractionInventory.Intersection(first.Box, second.Box) is not { } intersection)
        {
            return false;
        }
        var overlap = Area(intersection);
        var aArea = Area(first.Box);
        var bArea = Area(second.Box);
        var smaller = Math.Min(aArea, bArea);
        var larger = Math.Max(aArea, bArea);
        var sameName = first.Name == second.Name;
        var outer = aArea >= bArea ? first.Box : second.Box;
        var inner = aArea >= bArea ? second.Box : first.Box;
        var outerCenter = AndroidInteractionExploration.Center(outer);
        var innerCenter = AndroidInteractionExploration.Center(inner);
        var centersAligned = Math.Abs(outerCenter.X - innerCenter.X) <= .20 * (outer.Right - outer.Left) &&
            Math.Abs(outerCenter.Y - innerCenter.Y) <= .20 * (outer.Bottom - outer.Top);
        return (double)overlap / (aArea + bArea - overlap) >= (sameName ? .50 : .65) ||
            (overlap >= .90 * smaller && (smaller >= .12 * larger ||
                ((sameName || centersAligned) && smaller >= .035 * larger)));
    }

    private static bool ContainsCenter(AndroidNodeBounds outer, AndroidNodeBounds inner)
    {
        var at = AndroidInteractionExploration.Center(inner);
        return at.X >= outer.Left && at.X < outer.Right && at.Y >= outer.Top && at.Y < outer.Bottom;
    }

    private sealed record TreeMatch(AndroidInvestigationCandidate Row, AndroidNodeBounds Bounds, double Score);
    private sealed record VisionProposal(AndroidNodeBounds Box, string Label, string Name, TreeMatch? Match, bool Ambiguous, bool NearDisabled);
    private sealed class KeptProposal(VisionProposal choice)
    {
        public VisionProposal Choice { get; } = choice;
        public bool DisabledAlias { get; set; }
        public bool ConsequentialAlias { get; set; }
    }

    private static long Area(AndroidNodeBounds box) => (long)(box.Right - box.Left) * (box.Bottom - box.Top);
}

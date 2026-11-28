using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>Plans input for the original proposals only. It does not revise model or tree evidence.</summary>
public static class AndroidProposalInputPlan
{
    public static AndroidInvestigationInventory Build(AndroidInteractionSnapshot start,
        IReadOnlyList<AndroidVisionProposal> proposals, AndroidProposalTreeReport tree)
    {
        if (!start.HasScreenCoordinates || start.Frame is not { } frame || start.App.Items is not { } items ||
            tree.Matches.Count != proposals.Count)
        {
            return new([], "Input testing unavailable. The proposals could not be aligned to the starting app tree.");
        }
        var nodes = items.Nodes.ToDictionary(node => node.Index);
        var entries = proposals.Select((proposal, index) =>
        {
            var match = tree.Matches[index];
            var declared = match.Kind == AndroidProposalTreeMatchKind.Matched ? match.Candidates[0].Item : null;
            var box = proposal.ImageBounds(frame.Width, frame.Height);
            var item = declared is null ? new AndroidInteractionCandidate(proposal.Label, "", [box], null, null) :
                declared with { Boxes = [box] };
            // Never enlarge a model-selected location into a larger declared hit area.
            if (match.Kind == AndroidProposalTreeMatchKind.Matched &&
                AndroidInteractionInventory.Intersection(match.Candidates[0].Bounds, box) is { } common)
            {
                item = item with { Boxes = [common] };
            }
            var direct = item.Role == "Inline link" || item.NodeIndex is { } id && AcceptsTouch(nodes[id]);
            var reason = match.Kind == AndroidProposalTreeMatchKind.Ambiguous ? "the Android tree match is ambiguous" :
                item.Enabled == false ? "Android reports disabled" : null;
            return new Entry(new(item, true, reason is null) { ProposalIndex = index, VisionLabel = proposal.Label,
                OriginalProposalBox = box, TapSkipReason = reason }, box, direct);
        }).ToArray();

        // No geometry-only deduplication of the raw report. Refuse redundant
        // physical taps when overlapping suggestions cannot be separated.
        var ranked = entries.OrderByDescending(entry => entry.Direct).ThenBy(entry => Area(entry.Box))
            .ThenBy(entry => entry.Plan.ProposalIndex).ToArray();
        var kept = new List<Entry>();
        foreach (var entry in ranked)
        {
            if (!entry.Plan.Tap)
            {
                continue;
            }
            var overlap = kept.FirstOrDefault(other => Aliases(other, entry));
            if (overlap is not null)
            {
                // Exact repetitions can share one probe and its evidence.
                if (overlap.Box == entry.Box && overlap.Plan.Item.Boxes[0] == entry.Plan.Item.Boxes[0] &&
                    string.Equals(overlap.Plan.VisionLabel, entry.Plan.VisionLabel, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                entry.Plan = entry.Plan with { Tap = false, TapSkipReason =
                    $"overlaps proposal {overlap.Plan.ProposalIndex!.Value + 1} without a clearly separate touch target" };
                continue;
            }
            kept.Add(entry);
        }
        return new(entries.Select(entry => entry.Plan).ToArray(), "Only the original model proposals were selected for point tests.")
        {
            ProposalsOnly = true
        };
    }

    internal static bool AcceptsTouch(AndroidTreeNode node) => node.Clickable || node.Checkable || node.Editable ||
        node.LongClickable || node.ContextClickable || node.Actions.Any(action => action.Id is 16 or 32 or 0x0102003c);

    private static bool Aliases(Entry a, Entry b)
    {
        if (AndroidInteractionInventory.Intersection(a.Box, b.Box) is not { } common)
        {
            return false;
        }
        var aId = a.Plan.Item.NodeIndex;
        var bId = b.Plan.Item.NodeIndex;
        var centerA = AndroidInteractionExploration.Center(a.Plan.Item.Boxes[0]);
        var centerB = AndroidInteractionExploration.Center(b.Plan.Item.Boxes[0]);
        var distinctControls = a.Direct && b.Direct && aId.HasValue && bId.HasValue && aId != bId;
        var distinctLinks = a.Direct && b.Direct && a.Plan.Item.Role == "Inline link" && b.Plan.Item.Role == "Inline link" &&
            !string.Equals(a.Plan.Item.Label, b.Plan.Item.Label, StringComparison.Ordinal);
        var smaller = Area(a.Box) <= Area(b.Box) ? a : b;
        var larger = smaller == a ? b : a;
        var smallerArea = Area(smaller.Box);
        var overlapArea = Area(common);
        // A small badge can straddle a larger proposal's edge instead of sitting fully inside it.
        var nested = overlapArea >= .85 * smallerArea ||
            smallerArea <= Area(larger.Box) / 3 && overlapArea >= .5 * smallerArea;
        if ((distinctControls || distinctLinks) && centerA != centerB)
        {
            // Do not probe a larger declaration at a point also inside the smaller one.
            // A separate declared control is only tested if its chosen point stands apart.
            var largerCenter = smaller == a ? centerB : centerA;
            return nested && AndroidInteractionInventory.Contains(smaller.Box, new(largerCenter.X, largerCenter.Y,
                    largerCenter.X + 1, largerCenter.Y + 1));
        }
        return centerA == centerB || nested ||
            aId.HasValue && aId == bId && a.Plan.Item.Role != "Inline link" && b.Plan.Item.Role != "Inline link";
    }

    private static long Area(AndroidNodeBounds box) => (long)(box.Right - box.Left) * (box.Bottom - box.Top);

    private sealed record Entry(AndroidInvestigationCandidate Candidate, AndroidNodeBounds Box, bool Direct)
    {
        public AndroidInvestigationCandidate Plan { get; set; } = Candidate;
    }
}

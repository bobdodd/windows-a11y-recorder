using Recorder.Contracts;

namespace Recorder.Collectors.Android;

public enum AndroidProposalTreeMatchKind { NoMatch, Matched, Ambiguous }

public sealed record AndroidProposalTreeCandidate(AndroidInteractionCandidate Item, AndroidNodeBounds Bounds);

public sealed record AndroidProposalTreeMatch(AndroidProposalTreeMatchKind Kind,
    IReadOnlyList<AndroidProposalTreeCandidate> Candidates, int PlausibleCount = 0);

public sealed record AndroidProposalTreeReport(string Summary, IReadOnlyList<AndroidProposalTreeMatch> Matches);

/// <summary>Read-only correspondence between a fixed model answer and the tree at invocation.</summary>
public static class AndroidProposalTreeMatching
{
    // The live tree is event-driven, so it can be older than a newer picture on an
    // unchanged screen. Allow a small lead for asynchronous video/tree capture, but
    // don't map a tree update taken substantially after the picture.
    private const long MaximumTreeLead = 500_000_000;
    private const double AmbiguityMargin = .12;
    private const int ShownAlternatives = 3;

    public static AndroidProposalTreeReport Compare(AndroidLivePreviewFrame frame, AndroidLiveTreeSnapshot tree,
        IPhoneDisplay? display, IReadOnlyList<AndroidVisionProposal> proposals, CancellationToken token)
    {
        if (proposals.Count == 0)
        {
            return new("Android tree matching not run. The model proposed no targets.", []);
        }
        if (tree.State != AndroidLiveTreeState.Observed)
        {
            return Unavailable("No readable Android tree was available when the investigation started.");
        }
        if (AndroidTalkBackScope.ActiveApp(tree) is not { } app || app.Items is not { } items || items.Nodes.Count == 0)
        {
            return Unavailable("The starting tree did not contain nodes for a single active app on the phone display.");
        }
        var viewport = new AndroidNodeBounds(0, 0, frame.Width, frame.Height);
        if (frame.PhoneNanoseconds is not > 0 || items.StartedPhoneNanoseconds <= 0 ||
            display?.DisplayAt(frame.PhoneNanoseconds.Value) is not
                { Rotation: 0 or 90 or 180 or 270, ShownWidth: > 0, ShownHeight: > 0 } shown ||
            (shown.ShownWidth, shown.ShownHeight) != (frame.Width, frame.Height) ||
            display.DisplayAt(items.StartedPhoneNanoseconds) is not { } treeDisplay ||
            (treeDisplay.Rotation, treeDisplay.ShownWidth, treeDisplay.ShownHeight) !=
                (shown.Rotation, shown.ShownWidth, shown.ShownHeight) ||
            (app.Window.TransitionTimeMilliseconds is > 0 and var transition &&
                transition > Math.Min(frame.PhoneNanoseconds.Value, items.StartedPhoneNanoseconds) / 1_000_000) ||
            AndroidInteractionInventory.Intersection(viewport, app.Window.Bounds) is null)
        {
            return Unavailable("The starting Android tree could not be aligned to the screenshot's display coordinates.");
        }
        var offset = items.StartedPhoneNanoseconds - frame.PhoneNanoseconds.Value;
        if (offset > MaximumTreeLead)
        {
            return Unavailable("The starting Android tree was taken too far after the selected screenshot.");
        }

        token.ThrowIfCancellationRequested();
        var nodes = new Dictionary<int, AndroidTreeNode>(items.Nodes.Count);
        foreach (var node in items.Nodes)
        {
            if (!nodes.TryAdd(node.Index, node))
            {
                return Unavailable("The starting app tree contained duplicate node identifiers.");
            }
        }
        var entries = AndroidInteractionInventory.Describe(app, includeWithoutActions: true, redactPasswords: false)
            .Select(item =>
            {
                var node = nodes[item.NodeIndex!.Value];
                var names = (item.Role == "Inline link" ? [item.Label] :
                    new[] { item.Label, node.Text, node.ContentDescription, node.Hint })
                    .OfType<string>().Select(AndroidInteractionInventory.Normalized).Where(name => name.Length > 0)
                    .ToHashSet(StringComparer.Ordinal);
                var boxes = item.Boxes.Select(box => AndroidInteractionInventory.Intersection(viewport, box))
                    .OfType<AndroidNodeBounds>().ToArray();
                var directTarget = item.Role == "Inline link" || node.Clickable || node.Checkable || node.Editable ||
                    node.LongClickable || node.Actions.Any(action => action.Id is 16 or 32);
                return (Item: item, Names: names, Boxes: boxes, DirectTarget: directTarget);
            })
            .Where(entry => entry.Boxes.Length > 0).ToArray();

        var matches = new List<AndroidProposalTreeMatch>(proposals.Count);
        foreach (var proposal in proposals)
        {
            token.ThrowIfCancellationRequested();
            var name = AndroidInteractionInventory.Normalized(proposal.Label);
            var box = proposal.ImageBounds(frame.Width, frame.Height);
            var ranked = entries.Select(entry =>
                {
                    // An inline link can have several reported boxes, but counts once.
                    var best = entry.Boxes.Select(part =>
                        (Bounds: part, Score: Score(box, part, entry.Names.Contains(name), entry.DirectTarget)))
                        .MaxBy(part => part.Score);
                    return (entry.Item, best.Bounds, best.Score);
                })
                .Where(entry => entry.Score > 0)
                .OrderByDescending(entry => entry.Score).ThenBy(entry => entry.Item.NodeIndex).ToArray();
            if (ranked.Length == 0)
            {
                matches.Add(new(AndroidProposalTreeMatchKind.NoMatch, []));
                continue;
            }
            var close = ranked.TakeWhile(entry => ranked[0].Score - entry.Score < AmbiguityMargin).ToArray();
            matches.Add(new(close.Length == 1 ? AndroidProposalTreeMatchKind.Matched : AndroidProposalTreeMatchKind.Ambiguous,
                close.Take(ShownAlternatives).Select(entry => new AndroidProposalTreeCandidate(entry.Item, entry.Bounds)).ToArray(),
                close.Length));
        }
        var matched = matches.Count(match => match.Kind == AndroidProposalTreeMatchKind.Matched);
        var ambiguous = matches.Count(match => match.Kind == AndroidProposalTreeMatchKind.Ambiguous);
        var timing = offset == 0 ? "the frame time" : $"{Math.Abs(offset / 1_000_000d):0} ms {(offset < 0 ? "before" : "after")} the frame";
        var summary = $"Android tree: window {app.Window.Id}, node snapshot {items.SnapshotIndex}, started {timing}. " +
            $"{matched} matched; {ambiguous} ambiguous; {matches.Count - matched - ambiguous} without a clear match.";
        if (items.SnapshotIndex != tree.Index)
        {
            summary += $" The current window list (snapshot {tree.Index}) carried forward these nodes.";
        }
        if (items.Truncated || items.MissingChildren > 0 || !string.IsNullOrEmpty(tree.Problem) || tree.EarlierUpdateMissing)
        {
            summary += " The tree feed reports incomplete evidence.";
        }
        return new(summary, matches);
    }

    private static AndroidProposalTreeReport Unavailable(string reason) => new($"Android tree matching unavailable. {reason}", []);

    private static double Score(AndroidNodeBounds model, AndroidNodeBounds tree, bool sameName, bool directTarget)
    {
        if (AndroidInteractionInventory.Intersection(model, tree) is not { } common)
        {
            return 0;
        }
        var a = Area(model);
        var b = Area(tree);
        var overlap = Area(common);
        var ratio = Math.Min(a, b) / Math.Max(a, b);
        var iou = overlap / (a + b - overlap);
        if (sameName && ratio >= .16 && overlap >= .75 * Math.Min(a, b))
        {
            return 1 + iou;
        }
        if (iou >= .6)
        {
            return iou;
        }
        // A small glyph may sit inside a larger declared touch target. Require
        // that it is near the center and large enough not to be a tiny fragment.
        return directTarget && ratio >= .20 && overlap >= .90 * a &&
            Math.Abs((double)model.Left + model.Right - tree.Left - tree.Right) <= .40 * (tree.Right - tree.Left) &&
            Math.Abs((double)model.Top + model.Bottom - tree.Top - tree.Bottom) <= .40 * (tree.Bottom - tree.Top)
            ? .35 + .25 * iou : 0;
    }

    private static double Area(AndroidNodeBounds box) => ((double)box.Right - box.Left) * ((double)box.Bottom - box.Top);
}

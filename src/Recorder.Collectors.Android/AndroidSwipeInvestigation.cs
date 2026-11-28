using NodeKey = Recorder.Collectors.Android.AndroidTalkBackExploration.NodeKey;

namespace Recorder.Collectors.Android;

public sealed partial class AndroidInteractionExploration
{
    private const int SwipeLimit = 24;

    private async Task<AndroidInteractionSnapshot> InvestigateSwipesAsync(InvestigationViewport viewport,
        AndroidInteractionSnapshot phase, AndroidLiveTreeItems original, List<Finding> findings, bool proposalsOnly,
        Action<string> summary, IProgress<string>? progress, CancellationToken token)
    {
        var before = Verify(viewport, phase.Cursor);
        if (!before.App.Window.AccessibilityFocused ||
            before.App.Items!.Nodes.Count(node => node.VisibleToUser && node.AccessibilityFocused) != 1)
        {
            summary("Unavailable. There was no single starting TalkBack focus in the app.");
            return before;
        }

        progress?.Report("Testing TalkBack reachability with right and left swipes. Leave the phone untouched.");
        var right = true;
        var starting = before.App.Items.Nodes.Single(node => node.VisibleToUser && node.AccessibilityFocused);
        var seen = new HashSet<NodeKey> { NodeKey.Of(starting) };
        var matched = 0;
        var observed = 0;
        for (var step = 1; step <= SwipeLimit; step++)
        {
            before = Verify(viewport, before.Cursor);
            var useRight = right;
            if (step == 1 || step % 5 == 0)
            {
                progress?.Report($"TalkBack swipe: {step - 1} completed, {observed} verified focus stops. Leave the phone untouched.");
            }
            var reply = await SendSwipeAsync(before, useRight, token).ConfigureAwait(false);
            if (reply.Status != "dispatched" || reply.Started <= 0)
            {
                var problem = reply.Status == "talkback-unavailable" ?
                    "Android did not verify TalkBack and touch exploration for a navigation swipe." : InputProblem(reply.Status);
                summary($"{step - 1} completed swipes; {observed} focus stops observed. {problem}");
                if (UnsafeReply(reply))
                {
                    throw new InvestigationStop(problem + " Remaining input was not sent.");
                }
                return before;
            }
            summary($"{step} swipes completed; {observed} focus stops observed before interruption.");
            await _settle(token).ConfigureAwait(false);
            var events = Events(before);
            var after = Verify(viewport, before.Cursor);
            var hit = VerifiedFocus(after, before, events, reply.Started, explore: true, allowHover: false);
            var previous = before.App.Items!.Nodes.Where(node => node.VisibleToUser && node.AccessibilityFocused).Take(2).ToArray();
            if (hit is { } same && previous.Length == 1 && NodeKey.Of(same.Node) == NodeKey.Of(previous[0]))
            {
                hit = null;
            }
            var moved = false;
            if (hit is { } focus)
            {
                observed++;
                var targets = FocusTargets(findings, original, focus.Node, proposalsOnly);
                if (targets.Count > 0)
                {
                    matched++;
                    foreach (var target in targets)
                    {
                        target.Swipe.Add($"{(proposalsOnly ? "Matched Android node reached" : "Reached")} " +
                            $"on swipe {step} ({(useRight ? "right" : "left")}); " +
                            $"accessibility focus (event {focus.Event}, tree {focus.Tree}).");
                    }
                }
                moved = seen.Add(NodeKey.Of(focus.Node));
            }

            summary($"{step} swipes completed; {observed} focus stops observed; {matched} matched to reported items." +
                (step == SwipeLimit ? $" Stopped at the {SwipeLimit}-swipe limit." : ""));
            if (!moved)
            {
                if (!right)
                {
                    summary($"{step} swipes completed; {observed} focus stops observed; {matched} matched to reported items. " +
                        "No new verified stop on the last left swipe. This is not proof of a traversal boundary.");
                    return after;
                }
                right = false;
                seen = [];
                var focused = after.App.Items!.Nodes.Where(node => node.VisibleToUser && node.AccessibilityFocused).Take(2).ToArray();
                if (focused.Length == 1)
                {
                    seen.Add(NodeKey.Of(focused[0]));
                }
                else
                {
                    summary($"{step} swipes completed; {observed} focus stops observed; {matched} matched to reported items. " +
                        "No single current TalkBack focus was available for left swipes.");
                    return after;
                }
            }
            before = after;
        }
        return before;
    }

    private async Task<CompanionNavigationReply> SendSwipeAsync(AndroidInteractionSnapshot start, bool right, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var physical = _input();
            if (!_ready() || physical.Version != start.Input.Version || physical.TouchDown)
            {
                throw new InvestigationStop("Live monitoring or sole control of the phone was lost before sending a swipe.");
            }
            token.ThrowIfCancellationRequested();
            var reply = await _investigationSwipe!(right, start.Scope, timeout.Token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return reply;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new InvestigationStop("The swipe timed out and might have delivered input. No further input was requested.");
        }
    }
}

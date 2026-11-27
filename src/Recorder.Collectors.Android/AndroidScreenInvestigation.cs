using System.Diagnostics;
using Recorder.Contracts;
using NodeKey = Recorder.Collectors.Android.AndroidTalkBackExploration.NodeKey;

namespace Recorder.Collectors.Android;

public sealed record AndroidInvestigationCandidate(AndroidInteractionCandidate Item, bool SampleByTouch, bool Tap)
{
    public string? VisionLabel { get; init; }
}

public sealed record AndroidInvestigationInventory(IReadOnlyList<AndroidInvestigationCandidate> Candidates, string Coverage);

public sealed record AndroidInvestigationFinding(AndroidInteractionCandidate Item,
    IReadOnlyList<string> ExploreByTouch, IReadOnlyList<string> Tab, IReadOnlyList<string> DirectTouch)
{
    public AndroidInvestigationCrop? StartingLocation { get; init; }
    public IReadOnlyList<AndroidInvestigationTapPictures> TapPictures { get; init; } = [];
}

public sealed record AndroidInvestigationReport(string Status, string Coverage, string TalkBack, string Tab, string DirectTouch,
    IReadOnlyList<AndroidInvestigationFinding> Findings, string? Restoration, long? Tree, ulong? Frame)
{
    public AndroidLivePreviewFrame? StartingPicture { get; init; }
}

public sealed partial class AndroidInteractionExploration
{
    internal const string ExploreStart = "TYPE_TOUCH_EXPLORATION_GESTURE_START";
    internal const string ExploreEnd = "TYPE_TOUCH_EXPLORATION_GESTURE_END";
    internal const string HoverEnter = "TYPE_VIEW_HOVER_ENTER";
    internal const string InputFocus = "TYPE_VIEW_FOCUSED";
    internal const int ExploreLimit = 44;
    internal const int TabLimit = 40;
    internal const int TapLimit = 36;
    private const int ReportLimit = 500;
    private int _investigating;
    private Func<AndroidTalkBackScope, AndroidInteractionPoint, AndroidInvestigationInput, CancellationToken,
        Task<CompanionNavigationReply>>? _investigationDispatch;
    private Func<IAndroidInvestigationModes>? _investigationModes;
    private Func<bool> _investigationSupported = () => true;
    private Func<CancellationToken, Task> _modeSettle = token => Task.Delay(1200, token);

    internal void ConfigureInvestigation(
        Func<AndroidTalkBackScope, AndroidInteractionPoint, AndroidInvestigationInput, CancellationToken, Task<CompanionNavigationReply>> dispatch,
        Func<IAndroidInvestigationModes> modes, Func<CancellationToken, Task> settleMode)
    {
        _investigationDispatch = dispatch;
        _investigationModes = modes;
        _modeSettle = settleMode;
    }

    /// <summary>One recording-bound investigation of the viewport selected at invocation.</summary>
    public async Task<AndroidInvestigationReport> InvestigateAsync(
        Func<AndroidInteractionSnapshot, CancellationToken, Task<AndroidInvestigationInventory>> inventory,
        Func<string, bool> marker, IProgress<string>? progress, CancellationToken cancellation)
    {
        if (Interlocked.Exchange(ref _investigating, 1) != 0)
        {
            return new("An investigation is already running.", "", "", "", "", [], null, null, null);
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(TimeSpan.FromMinutes(3));
        var token = limit.Token;
        var start = Capture();
        var modes = _investigationModes?.Invoke();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var began = false;
        var coverage = "";
        var talkBack = "Not run.";
        var keyboard = "Not run.";
        var direct = "Not run.";
        var status = "Investigation finished on the selected viewport.";
        string? restoration = null;
        var rows = new List<Finding>();
        var lastCount = Stopwatch.GetTimestamp();
        bool CountDue()
        {
            if (progress is null || Stopwatch.GetElapsedTime(lastCount) < TimeSpan.FromSeconds(4))
            {
                return false;
            }
            lastCount = Stopwatch.GetTimestamp();
            return true;
        }

        try
        {
            if (start?.App.Items is not { } original)
            {
                return new("No live app viewport was available.", "", talkBack, keyboard, direct, [], null, null, null);
            }
            began = marker($"Recorder screen investigation {tag}: started; window {start.App.Window.Id}, " +
                $"tree {original.SnapshotIndex}, frame {start.Frame?.Sequence}.");
            if (!began)
            {
                return new("The recording no longer accepts markers. No input was sent.", "", talkBack, keyboard, direct, [],
                    null, original.SnapshotIndex, start.Frame?.Sequence);
            }
            progress?.Report("Starting screenshot discovery. Leave the phone untouched.");
            var found = await inventory(start, token).ConfigureAwait(false);
            rows.AddRange(found.Candidates.Take(ReportLimit).Select(candidate => new Finding(candidate)));
            coverage = found.Coverage + (found.Candidates.Count > ReportLimit ?
                $" Report capped at {ReportLimit} candidates; {found.Candidates.Count - ReportLimit} additional candidates were not listed or tested." : "");
            if (modes is null || _investigationDispatch is null)
            {
                throw new InvestigationStop("Input controls were unavailable. Only the inventory was produced.");
            }
            if (!_investigationSupported())
            {
                throw new InvestigationStop("The protocol 12 recorder companion is not connected. Only the inventory was produced.");
            }

            var viewport = new InvestigationViewport(start, original);
            var verifiedStart = Verify(viewport, start.Cursor);
            if (FrameChangedOutside(start.Frame!, verifiedStart.Frame!, start.App.Window.Bounds))
            {
                throw new InvestigationStop("The picture changed substantially during discovery. No input was sent.");
            }
            var explorePoints = Points(rows.Where(row => row.Candidate.SampleByTouch))
                .OrderBy(entry => !entry.Row.Candidate.Tap).GroupBy(entry => Center(entry.Box))
                .Select(group => (Box: group.First().Box, Rows: group.Select(entry => entry.Row).Distinct().ToArray())).ToArray();
            var tapPoints = Points(rows.Where(row => row.Candidate.Tap && row.Candidate.Item.Enabled != false))
                // Leave known links and text-entry areas until after buttons;
                // navigation or the soft keyboard can invalidate other targets.
                .OrderBy(entry => TapPriority(entry.Row.Candidate.Item))
                .GroupBy(entry => Center(entry.Box)).Select(group =>
                    (Box: group.First().Box, Rows: group.Select(entry => entry.Row).Distinct().ToArray())).ToArray();

            progress?.Report($"Testing TalkBack Explore by Touch ({Math.Min(ExploreLimit, explorePoints.Length)} point limit). Leave the phone untouched.");
            token.ThrowIfCancellationRequested();
            var mode = await modes.SetTalkBackAsync(true, token).ConfigureAwait(false);
            await _modeSettle(token).ConfigureAwait(false);
            var phase = Verify(viewport);
            if (mode is not null)
            {
                talkBack = "Unavailable. " + mode;
            }
            else
            {
                int sent = 0, observed = 0, reached = 0, skipped = 0;
                talkBack = "Interrupted before a point was tested.";
                foreach (var (box, targets) in explorePoints)
                {
                    if (sent >= ExploreLimit)
                    {
                        break;
                    }
                    if (CountDue())
                    {
                        progress?.Report($"TalkBack Explore by Touch: {sent} sampled, {skipped} skipped, {explorePoints.Length} planned.");
                    }
                    var before = Verify(viewport, phase.Cursor);
                    if (!TryPoint(before, box, out var point))
                    {
                        skipped++;
                        Note(targets, AndroidInvestigationInput.Explore, "Not sampled: point is outside the app or covered.");
                        continue;
                    }
                    var reply = await SendAsync(before, point, AndroidInvestigationInput.Explore, token).ConfigureAwait(false);
                    // Starting a service is asynchronous. Retry only a positive refusal
                    // that cannot have sent any input, and recheck the frozen scope.
                    if (sent == 0 && reply.Status == "talkback-unavailable")
                    {
                        await _modeSettle(token).ConfigureAwait(false);
                        before = Verify(viewport, before.Cursor);
                        reply = await SendAsync(before, point, AndroidInvestigationInput.Explore, token).ConfigureAwait(false);
                    }
                    if (reply.Status != "dispatched" || reply.Started <= 0)
                    {
                        var unavailable = InputProblem(reply.Status);
                        Note(targets, AndroidInvestigationInput.Explore, unavailable);
                        talkBack = $"{sent} completed point samples; {reached} mapped focus observations. {unavailable}";
                        if (reply.Status == "scope-changed")
                        {
                            throw new InvestigationStop(unavailable);
                        }
                        break;
                    }
                    sent++;
                    talkBack = $"{sent} completed point samples; {reached} mapped focus observations. Pass interrupted.";
                    await _settle(token).ConfigureAwait(false);
                    var events = Events(before);
                    phase = Verify(viewport, before.Cursor);
                    var observedExplore = ExploreGesture(events, reply.Started, before.App.Window.Id);
                    if (observedExplore)
                    {
                        observed++;
                    }
                    var focus = observedExplore ? VerifiedFocus(phase, before, events, reply.Started, explore: true) : null;
                    if (focus is { } hit && AndroidInteractionInventory.Contains(hit.Node.Bounds,
                        new(point.X, point.Y, point.X + 1, point.Y + 1)))
                    {
                        reached++;
                        var matched = FocusRow(rows, original, hit.Node);
                        matched.Explore.Add($"Reached at ({point.X}, {point.Y}); event {hit.Event}, tree {hit.Tree}.");
                        Note(targets.Where(row => row != matched), AndroidInvestigationInput.Explore,
                            $"At ({point.X}, {point.Y}), TalkBack reached {matched.Candidate.Item.Label}, not this candidate.");
                    }
                    else
                    {
                        Note(targets, AndroidInvestigationInput.Explore, observedExplore ?
                            $"At ({point.X}, {point.Y}), exploration was observed but no app item focus was verified." :
                            $"At ({point.X}, {point.Y}), Android completed the gesture; touch-exploration events were not verified.");
                    }
                    talkBack = $"{sent} point samples completed; {observed} corroborated as touch exploration; {reached} mapped focus observations." +
                        (skipped > 0 ? $" {skipped} points were outside the app or covered." : "") +
                        (sent + skipped < explorePoints.Length ? $" {explorePoints.Length - sent - skipped} planned points not yet sampled." : "");
                }
                if (sent == 0 && skipped == explorePoints.Length)
                {
                    talkBack = "No verifiable points were available to sample.";
                }
            }

            Verify(viewport, phase.Cursor);
            progress?.Report("Testing keyboard Tab without TalkBack intercepting it.");
            token.ThrowIfCancellationRequested();
            mode = await modes.SetTalkBackAsync(false, token).ConfigureAwait(false);
            await _modeSettle(token).ConfigureAwait(false);
            phase = Verify(viewport);
            if (mode is not null)
            {
                keyboard = direct = "Unavailable. " + mode;
            }
            else
            {
                var seen = new HashSet<NodeKey>();
                int sent = 0, consecutiveUnverified = 0;
                keyboard = "No Tab key sent.";
                for (var step = 1; step <= TabLimit; step++)
                {
                    if (CountDue())
                    {
                        progress?.Report($"Keyboard Tab: {sent} keys sent, {seen.Count} input-focus stops verified.");
                    }
                    var before = Verify(viewport, phase.Cursor);
                    if (!TryPoint(before, before.App.Window.Bounds, out var point, checkOverlay: false))
                    {
                        keyboard = "The app window or display could not be verified for Tab.";
                        break;
                    }
                    var reply = await SendAsync(before, point, AndroidInvestigationInput.Tab, token).ConfigureAwait(false);
                    if (sent == 0 && reply.Status == "direct-touch-unavailable")
                    {
                        await _modeSettle(token).ConfigureAwait(false);
                        before = Verify(viewport, before.Cursor);
                        reply = await SendAsync(before, point, AndroidInvestigationInput.Tab, token).ConfigureAwait(false);
                    }
                    if (reply.Status != "ready" || reply.Started <= 0)
                    {
                        var unavailable = InputProblem(reply.Status);
                        keyboard = $"{sent} Tab keys sent; {seen.Count} items verified. {unavailable}";
                        if (reply.Status == "scope-changed")
                        {
                            throw new InvestigationStop(unavailable);
                        }
                        break;
                    }
                    Verify(viewport, before.Cursor);
                    token.ThrowIfCancellationRequested();
                    // Wait for a command already sent before restoring TalkBack.
                    // The mode implementation has its own short ADB timeout.
                    var keyProblem = await modes.TabAsync(CancellationToken.None).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (keyProblem is { } problem)
                    {
                        throw new InvestigationStop(problem + " No further input was sent.");
                    }
                    sent++;
                    keyboard = $"{sent} Tab keys sent; {seen.Count} items verified. Pass interrupted.";
                    await _settle(token).ConfigureAwait(false);
                    var events = Events(before);
                    phase = Verify(viewport, before.Cursor);
                    if (VerifiedFocus(phase, before, events, reply.Started, explore: false) is { } hit)
                    {
                        var matched = FocusRow(rows, original, hit.Node);
                        matched.Tab.Add($"Reached on Tab {step}; input focus (event {hit.Event}, tree {hit.Tree}).");
                        if (!seen.Add(NodeKey.Of(hit.Node)))
                        {
                            keyboard = $"{sent} Tab keys sent; {seen.Count} items verified. A previous input-focus stop was revisited.";
                            break;
                        }
                        consecutiveUnverified = 0;
                    }
                    else if (++consecutiveUnverified == 2)
                    {
                        keyboard = $"{sent} Tab keys sent; {seen.Count} items verified. Two successive keys did not establish a new input-focus stop.";
                        break;
                    }
                    keyboard = $"{sent} Tab keys sent; {seen.Count} items verified." +
                        (step == TabLimit ? $" Stopped at the {TabLimit}-key limit." : "");
                }

                progress?.Report($"Testing apparent controls with direct taps ({Math.Min(TapLimit, tapPoints.Length)} tap limit). The app can react.");
                int tapped = 0, clicks = 0, visual = 0, skipped = 0;
                direct = "No direct-tap points were available.";
                foreach (var (box, targets) in tapPoints)
                {
                    if (tapped >= TapLimit)
                    {
                        break;
                    }
                    if (CountDue())
                    {
                        progress?.Report($"Direct touch: {tapped} completed, {skipped} skipped, {tapPoints.Length} planned.");
                    }
                    var before = Verify(viewport, phase.Cursor);
                    var at = Center(box);
                    if (rows.Any(row => row.Candidate.Item.Enabled == false &&
                        row.Candidate.Item.Boxes.Any(bounds => at.X >= bounds.Left && at.X < bounds.Right &&
                            at.Y >= bounds.Top && at.Y < bounds.Bottom)))
                    {
                        skipped++;
                        Note(targets, AndroidInvestigationInput.Tap, "Not tapped: the location overlaps a disabled Android item.");
                        continue;
                    }
                    if (targets.Any(row => SensitiveAction(row.Candidate.Item.Label) ||
                        (row.Candidate.VisionLabel is { } label && SensitiveAction(label))))
                    {
                        skipped++;
                        Note(targets, AndroidInvestigationInput.Tap, "Not tapped: label suggests a consequential action.");
                        continue;
                    }
                    if (!TryPoint(before, box, out var point) ||
                        (targets.All(row => row.Candidate.Item.NodeIndex is null || row.Candidate.Item.Role == "Inline link") &&
                         PixelsChanged(start.Frame!, before.Frame!, box)))
                    {
                        skipped++;
                        Note(targets, AndroidInvestigationInput.Tap, "Not tapped: the point was covered, outside the app, or its picture changed.");
                        continue;
                    }
                    var reply = await SendAsync(before, point, AndroidInvestigationInput.Tap, token).ConfigureAwait(false);
                    if (reply.Status != "dispatched" || reply.Started <= 0)
                    {
                        var unavailable = InputProblem(reply.Status);
                        Note(targets, AndroidInvestigationInput.Tap, unavailable);
                        direct = $"{tapped} completed taps; {clicks} matched clicks; {visual} local visual changes. {unavailable}";
                        if (reply.Status == "scope-changed")
                        {
                            throw new InvestigationStop(unavailable);
                        }
                        break;
                    }
                    tapped++;
                    direct = $"{tapped} completed taps; {clicks} matched clicks; {visual} local visual changes. Pass interrupted.";
                    await _settle(token).ConfigureAwait(false);
                    var events = Events(before);
                    var after = Capture();
                    var result = ClickOrPicture(before, after, events, reply.Started, box, point);
                    clicks += result.ClickEvent.HasValue ? 1 : 0;
                    visual += result.AfterFrame.HasValue ? 1 : 0;
                    Note(targets, AndroidInvestigationInput.Tap, $"At ({point.X}, {point.Y}): {result.Evidence}");
                    direct = $"{tapped} completed taps; {clicks} matched clicks; {visual} local visual changes." +
                        (skipped > 0 ? $" {skipped} points skipped." : "") +
                        (tapped + skipped < tapPoints.Length ? $" {tapPoints.Length - tapped - skipped} planned points not tested." : "");
                    phase = Verify(viewport, before.Cursor, afterTap: true, expectedClick: result.ClickEvent);
                    if (FrameChangedOutside(before.Frame!, phase.Frame!, before.App.Window.Bounds, box))
                    {
                        throw new InvestigationStop("The picture changed beyond the tapped target. Remaining points were not tested.");
                    }
                    if (after is not null && result.AfterFrame.HasValue &&
                        !FrameChangedOutside(before.Frame!, after.Frame!, before.App.Window.Bounds, box) &&
                        AndroidInvestigationPictures.TapPair(before, after, box) is { } pictures)
                    {
                        foreach (var row in targets)
                        {
                            row.Pictures.Add(pictures);
                        }
                    }
                }
                if (tapped == 0 && skipped > 0 && skipped == tapPoints.Length)
                {
                    direct = $"All {skipped} direct-tap points were skipped; see the individual reasons below.";
                }
            }
        }
        catch (InvestigationStop exception)
        {
            status = exception.Message;
        }
        catch (OperationCanceledException)
        {
            status = cancellation.IsCancellationRequested ? "Investigation stopped." : "Investigation reached its three-minute limit.";
        }
        catch (Exception ex) when (ex is IOException or CompanionUnavailableException or ObjectDisposedException)
        {
            status = "The phone evidence or command connection was interrupted. No further input was requested.";
        }
        finally
        {
            try
            {
                if (modes is not null)
                {
                    progress?.Report("Verifying restoration of the original TalkBack setting.");
                    restoration = await modes.RestoreAsync().ConfigureAwait(false);
                }
                if (began)
                {
                    // The result text may include live app names. Keep only stage summaries in recording markers.
                    marker($"Recorder screen investigation {tag}: ended. {status} TalkBack: {talkBack} " +
                        $"Keyboard: {keyboard} Direct touch: {direct}" + (restoration is null ? "" : " " + restoration));
                }
            }
            finally
            {
                Volatile.Write(ref _investigating, 0);
            }
        }
        var picture = start is not null && AndroidInvestigationPictures.CanUse(start) ? start : null;
        var findings = rows.Select(row => row.Result(picture)).ToArray();
        return new(status, coverage, talkBack, keyboard, direct, findings, restoration,
            start?.App.Items?.SnapshotIndex, picture?.Frame?.Sequence)
        {
            StartingPicture = findings.Any(finding => finding.StartingLocation is not null) ? picture?.Frame : null
        };
    }

    private AndroidInteractionSnapshot Verify(InvestigationViewport viewport, long? since = null,
        bool afterTap = false, long? expectedClick = null)
    {
        var current = Capture();
        if (!_ready() || current is null || current.Tree.Problem is not null || current.Tree.EarlierUpdateMissing ||
            current.App.Items is not { Truncated: false, MissingChildren: 0 })
        {
            throw new InvestigationStop("A complete live app tree, accessibility event feed and physical input monitor are required. Remaining input was not sent.");
        }
        var input = _input();
        if (input.Version != viewport.Start.Input.Version || input.TouchDown ||
            (input.LastAt > 0 && Stopwatch.GetElapsedTime(input.LastAt) < TimeSpan.FromMilliseconds(400)))
        {
            throw new InvestigationStop("Physical phone input was detected. Remaining input was not sent.");
        }
        if (!current.HasScreenCoordinates || !viewport.Start.HasScreenCoordinates ||
            current.Preview.State != AndroidLivePreviewState.ShowingFrames ||
            current.Preview.StreamSession != viewport.Start.Preview.StreamSession ||
            current.Display?.Rotation != viewport.Start.Display?.Rotation ||
            (current.Frame!.Width, current.Frame.Height) != (viewport.Start.Frame!.Width, viewport.Start.Frame.Height) ||
            !SameWindow(viewport.Start, current.App) || !viewport.Matches(current.App.Items))
        {
            throw new InvestigationStop("The window, display or visible app structure changed. Remaining input was not sent.");
        }
        if (since is { } cursor)
        {
            var batch = _signals.After(cursor);
            if (batch.Gap || batch.Events.Any(entry => entry is null))
            {
                throw new InvestigationStop("Accessibility events were missing. Remaining input was not sent.");
            }
            if (batch.Events.OfType<AndroidAccessibilityEvent>().Any(entry =>
                entry.EventTypeName is AndroidAccessibilityRecords.WindowStateChangedName or AndroidAccessibilityRecords.WindowsChangedName
                    or "TYPE_VIEW_SCROLLED" or "TYPE_VIEW_LONG_CLICKED" or "TYPE_VIEW_TEXT_CHANGED" ||
                (entry.EventTypeName is "TYPE_VIEW_CLICKED" && (!afterTap || entry.Index != expectedClick))))
            {
                throw new InvestigationStop("Android reported a window change, scroll or unexpected action. Remaining input was not sent.");
            }
            return new(current.Tree, current.App, current.Scope, current.Preview, current.Display, current.Input, batch.Cursor);
        }
        return current;
    }

    private AndroidAccessibilityEvent[] Events(AndroidInteractionSnapshot before)
    {
        var batch = _signals.After(before.Cursor);
        if (!_ready() || _input().Version != before.Input.Version || batch.Gap || batch.Events.Any(entry => entry is null))
        {
            throw new InvestigationStop("Other input or missing events made the response uncertain. Remaining input was not sent.");
        }
        return batch.Events.OfType<AndroidAccessibilityEvent>().ToArray();
    }

    private async Task<CompanionNavigationReply> SendAsync(AndroidInteractionSnapshot current, AndroidInteractionPoint point,
        AndroidInvestigationInput input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Do not change the accessibility mode while a dispatched finger
        // may still be against the screen. Stop queues no further gestures.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            var physical = _input();
            if (!_ready() || physical.Version != current.Input.Version || physical.TouchDown)
            {
                throw new InvestigationStop("Live monitoring or sole control of the phone was lost before sending input.");
            }
            token.ThrowIfCancellationRequested();
            var reply = await _investigationDispatch!(current.Scope, point, input, timeout.Token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return reply;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new InvestigationStop("The phone command timed out and might have delivered input. No further input was requested.");
        }
    }

    private static bool TryPoint(AndroidInteractionSnapshot current, AndroidNodeBounds box, out AndroidInteractionPoint point,
        bool checkOverlay = true)
    {
        var (x, y) = Center(box);
        var frame = current.Frame!;
        var area = new AndroidNodeBounds(x - 1, y - 1, x + 2, y + 2);
        point = new(x, y, frame.Width, frame.Height, current.Display!.Rotation);
        return x >= 2 && y >= 2 && x < frame.Width - 2 && y < frame.Height - 2 &&
            AndroidInteractionInventory.Contains(new(0, 0, frame.Width, frame.Height), box) &&
            AndroidInteractionInventory.Contains(current.App.Window.Bounds, area) &&
            (!checkOverlay || !current.Occluders.Any(overlay => AndroidInteractionInventory.Intersection(overlay, area) is not null));
    }

    internal static bool ExploreGesture(IReadOnlyList<AndroidAccessibilityEvent> events, long started, int window)
    {
        // Android constructs these boundaries itself; older or unresolved
        // events may not name a display. A focus hit still needs the exact app.
        var boundaries = events.Where(entry => Timed(entry, started, boundary: true) &&
            (entry.WindowId < 0 || entry.WindowId == window)).ToArray();
        var beginnings = boundaries.Where(entry => entry.EventTypeName == ExploreStart).Take(2).ToArray();
        var endings = boundaries.Where(entry => entry.EventTypeName == ExploreEnd).Take(2).ToArray();
        return beginnings.Length == 1 && endings.Length == 1 && beginnings[0].Index < endings[0].Index;
    }

    private static bool Timed(AndroidAccessibilityEvent item, long started, bool boundary = false) =>
        (item.DisplayId is null or 0 || (boundary && item.DisplayId == -1)) && item.ReceivedPhoneNanoseconds >= started &&
        item.ReceivedPhoneNanoseconds - started <= 1_700_000_000 &&
        item.EventTimeMilliseconds >= (started - 500_000) / 1_000_000;

    internal static (AndroidTreeNode Node, long Event, long Tree)? VerifiedFocus(AndroidInteractionSnapshot after,
        AndroidInteractionSnapshot before, IReadOnlyList<AndroidAccessibilityEvent> events, long started, bool explore)
    {
        if (after.App.Items is not { } items || items.SnapshotIndex <= before.App.Items!.SnapshotIndex ||
            !SameWindow(before, after.App) || !(explore ? after.App.Window.AccessibilityFocused : after.App.Window.Focused))
        {
            return null;
        }
        var focused = items.Nodes.Where(node => explore ? node.AccessibilityFocused : node.Focused).Take(2).ToArray();
        if (focused.Length != 1 || !focused[0].VisibleToUser || focused[0].PackageName != before.PackageName)
        {
            return null;
        }
        var key = NodeKey.Of(focused[0]);
        var matches = events.Where(entry => Timed(entry, started) && entry.WindowId == before.App.Window.Id &&
            entry.PackageName == before.PackageName && entry.Source is not null &&
            entry.ReceivedPhoneNanoseconds <= items.StartedPhoneNanoseconds &&
            (explore ? entry.EventTypeName is AndroidTalkBackSignals.Focus or HoverEnter : entry.EventTypeName == InputFocus))
            .ToArray();
        var preferred = matches.Where(entry => entry.EventTypeName == (explore ? AndroidTalkBackSignals.Focus : InputFocus)).ToArray();
        var candidates = preferred.Length > 0 ? preferred : matches.Where(entry => NodeKey.Of(entry.Source!) == key).ToArray();
        return candidates.Length == 1 && NodeKey.Of(candidates[0].Source!) == key &&
            items.Nodes.Count(node => node.VisibleToUser && NodeKey.Of(node) == key) == 1 ?
            (focused[0], candidates[0].Index, items.SnapshotIndex) : null;
    }

    private static AndroidInteractionTapResult ClickOrPicture(AndroidInteractionSnapshot before, AndroidInteractionSnapshot? after,
        AndroidAccessibilityEvent[] events, long started, AndroidNodeBounds box, AndroidInteractionPoint point)
    {
        var clicks = events.Where(entry => MatchesClick(entry, before.Scope, started, point.X, point.Y)).Take(2).ToArray();
        if (clicks.Length == 1)
        {
            return new($"Android click source covered the point (event {clicks[0].Index}).", clicks[0].Index);
        }
        if (clicks.Length > 1)
        {
            return new("More than one matching click followed the tap; attribution is uncertain.");
        }
        if (after?.Frame is { } picture && after.HasScreenCoordinates && SameWindow(before, after.App) &&
            before.Preview.StreamSession == after.Preview.StreamSession && before.Display?.Rotation == after.Display?.Rotation &&
            picture.Sequence > before.Frame!.Sequence && picture.PhoneNanoseconds >= started + 55_000_000 &&
            PixelsChanged(before.Frame, picture, box))
        {
            return new($"Pixels at the target changed after the tap (frame {picture.Sequence}); no Android click was matched.",
                AfterFrame: picture.Sequence);
        }
        return new("Tap completed; no target response was corroborated. This does not establish that the target is inactive.");
    }

    internal static bool FrameChangedOutside(AndroidLivePreviewFrame before, AndroidLivePreviewFrame after,
        AndroidNodeBounds viewport, AndroidNodeBounds? target = null)
    {
        if (before.Sequence == after.Sequence)
        {
            return false;
        }
        if (before.Width != after.Width || before.Height != after.Height || before.Rgb.Length != after.Rgb.Length ||
            before.Rgb.Length != (long)before.Width * before.Height * 3)
        {
            return true;
        }
        var old = before.Rgb.Span;
        var next = after.Rgb.Span;
        int samples = 0, changed = 0;
        for (var y = Math.Max(0, viewport.Top); y < Math.Min(before.Height, viewport.Bottom); y += 8)
        {
            for (var x = Math.Max(0, viewport.Left); x < Math.Min(before.Width, viewport.Right); x += 8)
            {
                if (target is { } excluded && x >= excluded.Left - 8 && x < excluded.Right + 8 &&
                    y >= excluded.Top - 8 && y < excluded.Bottom + 8)
                {
                    continue;
                }
                var i = (y * before.Width + x) * 3;
                samples++;
                if (Math.Abs(old[i] - next[i]) + Math.Abs(old[i + 1] - next[i + 1]) + Math.Abs(old[i + 2] - next[i + 2]) >= 60)
                {
                    changed++;
                }
            }
        }
        return changed >= 8 && changed * 100 >= samples * 6;
    }

    private static IEnumerable<(Finding Row, AndroidNodeBounds Box)> Points(IEnumerable<Finding> rows) =>
        rows.SelectMany(row => row.Candidate.Item.Boxes.Select(box => (Row: row, Box: box)))
            .OrderBy(entry => entry.Box.Top).ThenBy(entry => entry.Box.Left);

    private static int TapPriority(AndroidInteractionCandidate item) =>
        item.Role == "Inline link" || item.Evidence.Split(", ").Contains("Editable", StringComparer.Ordinal) ? 1 : 0;

    private static Finding FocusRow(List<Finding> rows, AndroidLiveTreeItems original, AndroidTreeNode node)
    {
        var key = NodeKey.Of(node);
        var matching = original.Nodes.Where(item => NodeKey.Of(item) == key).Take(2).ToArray();
        var index = matching.Length == 1 ? matching[0].Index : (int?)null;
        var label = AndroidTalkBackExploration.ItemLabel(node);
        var existing = rows.Find(row => (index.HasValue && row.Candidate.Item.NodeIndex == index &&
            row.Candidate.Item.Role != "Inline link") || row.Observed == key);
        if (existing is not null)
        {
            return existing;
        }
        var result = new Finding(new(new(label, "No matching Android action declaration in the inventory",
            [index.HasValue ? matching[0].Bounds : node.Bounds], node.Enabled, index, node.ClassName), false, false))
        { Observed = key, HasStartingLocation = index.HasValue };
        rows.Add(result);
        return result;
    }

    private static void Note(IEnumerable<Finding> rows, AndroidInvestigationInput input, string detail)
    {
        foreach (var row in rows)
        {
            (input == AndroidInvestigationInput.Explore ? row.Explore : row.Touch).Add(detail);
        }
    }

    private static string InputProblem(string status) => status switch
    {
        "scope-changed" => "The phone could not confirm the active app, focused window or an unobstructed point.",
        "talkback-unavailable" => "TalkBack Explore by Touch was not verified on the phone; no swipe traversal was substituted.",
        "direct-touch-unavailable" => "The phone did not confirm ordinary input mode without touch exploration or magnification.",
        "gesture-cancelled" => "Android cancelled the gesture. Its response is unknown.",
        "connection-lost" => "The phone companion connection was lost.",
        "companion-update-required" => "Update the phone companion to run this investigation.",
        _ => "The companion did not confirm this input.",
    };

    internal static bool SensitiveAction(string label)
    {
        var words = label.Split([' ', '\t', '\r', '\n', ',', '.', ':', ';', '!', '?', '(', ')', '-', '/'],
            StringSplitOptions.RemoveEmptyEntries);
        return words.Any(word => new[] { "delete", "erase", "destroy", "purchase", "buy", "pay", "send",
            "submit", "transfer", "factory", "uninstall", "publish", "checkout" }.Contains(word, StringComparer.OrdinalIgnoreCase));
    }

    private sealed class Finding(AndroidInvestigationCandidate candidate)
    {
        public AndroidInvestigationCandidate Candidate { get; } = candidate;
        public NodeKey? Observed { get; init; }
        public bool HasStartingLocation { get; init; } = true;
        public List<string> Explore { get; } = [];
        public List<string> Tab { get; } = [];
        public List<string> Touch { get; } = [];
        public List<AndroidInvestigationTapPictures> Pictures { get; } = [];

        public AndroidInvestigationFinding Result(AndroidInteractionSnapshot? picture) => new(Candidate.Item,
            Explore.Count > 0 ? Explore : [Candidate.SampleByTouch ? "Not sampled in this run." : "Not sampled for this item."],
            Tab.Count > 0 ? Tab : ["Not verified by Tab in this run."],
            Touch.Count > 0 ? Touch : [Candidate.Item.Enabled == false ? "Not tapped: Android reports disabled." :
                Candidate.Tap ? "Not tested in this run." : "Not a direct-tap candidate."])
        {
            StartingLocation = HasStartingLocation && picture is not null ?
                AndroidInvestigationPictures.Locate(picture, Candidate.Item.Boxes) : null,
            TapPictures = Pictures.ToArray()
        };
    }

    private sealed class InvestigationViewport(AndroidInteractionSnapshot start, AndroidLiveTreeItems original)
    {
        private readonly ViewportNode[] _nodes = Nodes(original);
        private AndroidLiveTreeItems _last = original;
        private bool _matched = true;
        public AndroidInteractionSnapshot Start { get; } = start;

        public bool Matches(AndroidLiveTreeItems items)
        {
            if (!ReferenceEquals(_last, items))
            {
                _matched = _nodes.SequenceEqual(Nodes(items));
                _last = items;
            }
            return _matched;
        }

        // Focus and selected-state changes are expected. Changes to positions, control labels,
        // or which visible items are present require another investigation of the new viewport.
        private static ViewportNode[] Nodes(AndroidLiveTreeItems items) => items.Nodes
            .Where(node => node.VisibleToUser).Select(node =>
            {
                var key = NodeKey.Of(node);
                if (!node.Clickable && !node.Focusable && !node.ScreenReaderFocusable && !node.Heading)
                {
                    key = key with { Text = null, Description = null, Hint = null };
                }
                return new ViewportNode(key, node.Depth, node.Enabled, node.Clickable, node.Focusable, node.Editable);
            }).ToArray();
        private sealed record ViewportNode(NodeKey Key, int Depth, bool Enabled, bool Clickable, bool Focusable, bool Editable);
    }

    private sealed class InvestigationStop(string message) : Exception(message);
}

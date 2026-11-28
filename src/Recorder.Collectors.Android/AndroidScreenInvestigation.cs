using System.Diagnostics;
using Recorder.Contracts;
using NodeKey = Recorder.Collectors.Android.AndroidTalkBackExploration.NodeKey;

namespace Recorder.Collectors.Android;

public sealed record AndroidInvestigationCandidate(AndroidInteractionCandidate Item, bool SampleByTouch, bool Tap)
{
    public string? VisionLabel { get; init; }
    public string? DuplicateTapRestriction { get; init; }
    public int? ProposalIndex { get; init; }
    public AndroidNodeBounds? OriginalProposalBox { get; init; }
    public string? TapSkipReason { get; init; }
}

public sealed record AndroidInvestigationInventory(IReadOnlyList<AndroidInvestigationCandidate> Candidates, string Coverage)
{
    public bool ProposalsOnly { get; init; }
}

public sealed record AndroidInvestigationFinding(AndroidInteractionCandidate Item,
    IReadOnlyList<string> ExploreByTouch, IReadOnlyList<string> Tab, IReadOnlyList<string> DirectTouch)
{
    public int? ProposalIndex { get; init; }
    public IReadOnlyList<string> Swipe { get; init; } = [];
    public AndroidInvestigationCrop? StartingLocation { get; init; }
    public IReadOnlyList<AndroidInvestigationTapPictures> TapPictures { get; init; } = [];
}

public sealed record AndroidInvestigationReport(string Status, string Coverage, string TalkBack, string Tab, string DirectTouch,
    IReadOnlyList<AndroidInvestigationFinding> Findings, string? Restoration, long? Tree, ulong? Frame)
{
    public string Swipe { get; init; } = "Not run.";
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
    private Func<bool, AndroidTalkBackScope, CancellationToken, Task<CompanionNavigationReply>>? _investigationSwipe;
    private Func<IAndroidInvestigationModes>? _investigationModes;
    private Func<bool> _investigationSupported = () => true;
    private Func<CancellationToken, Task> _modeSettle = token => Task.Delay(1200, token);

    internal void ConfigureInvestigation(
        Func<AndroidTalkBackScope, AndroidInteractionPoint, AndroidInvestigationInput, CancellationToken, Task<CompanionNavigationReply>> dispatch,
        Func<IAndroidInvestigationModes> modes, Func<CancellationToken, Task> settleMode,
        Func<bool, AndroidTalkBackScope, CancellationToken, Task<CompanionNavigationReply>>? swipe = null)
    {
        _investigationDispatch = dispatch;
        _investigationModes = modes;
        _modeSettle = settleMode;
        _investigationSwipe = swipe;
    }

    /// <summary>One recording-bound investigation of the viewport selected at invocation.</summary>
    public Task<AndroidInvestigationReport> InvestigateAsync(
        Func<AndroidInteractionSnapshot, CancellationToken, Task<AndroidInvestigationInventory>> inventory,
        Func<string, bool> marker, IProgress<string>? progress, CancellationToken cancellation) =>
        InvestigateFromAsync(Capture(), inventory, marker, progress, cancellation);

    public Task<AndroidInvestigationReport> InvestigateProposalsAsync(AndroidInteractionSnapshot start,
        AndroidInvestigationInventory inventory, Func<string, bool> marker, IProgress<string>? progress, CancellationToken cancellation) =>
        InvestigateFromAsync(start, (_, _) => Task.FromResult(inventory), marker, progress, cancellation);

    private async Task<AndroidInvestigationReport> InvestigateFromAsync(AndroidInteractionSnapshot? start,
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
        var modes = _investigationModes?.Invoke();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var began = false;
        var coverage = "";
        var talkBack = "Not run.";
        var swipe = "Not run.";
        var keyboard = "Not run.";
        var direct = "Not run.";
        var status = "Investigation finished on the selected viewport.";
        string? restoration = null;
        var modeTouched = false;
        var modelProposalsOnly = false;
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
            var found = await inventory(start, token).ConfigureAwait(false);
            modelProposalsOnly = found.ProposalsOnly;
            rows.AddRange(found.Candidates.Take(ReportLimit).Select(candidate => new Finding(candidate)));
            coverage = found.Coverage + (found.Candidates.Count > ReportLimit ?
                $" Report capped at {ReportLimit} candidates; {found.Candidates.Count - ReportLimit} additional candidates were not listed or tested." : "");
            token.ThrowIfCancellationRequested();
            if (rows.Count == 0)
            {
                throw new InvestigationStop("No model proposals were available for input testing. No input was sent.");
            }
            if (modes is null || _investigationDispatch is null)
            {
                throw new InvestigationStop("Input controls were unavailable. Only the inventory was produced.");
            }
            if (!_investigationSupported())
            {
                throw new InvestigationStop("The protocol 12 recorder companion is not connected. Only the inventory was produced.");
            }
            if (!start.FeedsReady || start.Tree.Problem is not null || start.Tree.EarlierUpdateMissing ||
                original.Truncated || original.MissingChildren > 0)
            {
                throw new InvestigationStop("The starting app tree or input evidence feed was incomplete. No input was sent.");
            }

            var viewport = new InvestigationViewport(start, original, modelProposalsOnly ? rows : null);
            var verifiedStart = Verify(viewport, start.Cursor, beforeInput: true);
            var explorePoints = Points(rows.Where(row => row.Candidate.SampleByTouch))
                .OrderBy(entry => !entry.Row.Candidate.Tap).GroupBy(entry => Center(entry.Box))
                .Select(group => (Box: group.First().Box, Rows: group.Select(entry => entry.Row).Distinct().ToArray())).ToArray();
            var tapPoints = Points(rows.Where(row => row.Candidate.Tap && row.Candidate.Item.Enabled != false))
                // Leave known links and text-entry areas until after buttons;
                // navigation or the soft keyboard can invalidate other targets.
                .OrderBy(entry => TapPriority(entry.Row.Candidate.Item))
                .GroupBy(entry => Center(entry.Box)).Select(group =>
                    (Box: group.First().Box, Rows: group.Select(entry => entry.Row).Distinct().ToArray())).ToArray();
            var clickCountLabel = modelProposalsOnly ? "clicks at sampled points" : "matched clicks";

            progress?.Report($"Testing TalkBack Explore by Touch ({Math.Min(ExploreLimit, explorePoints.Length)} point limit). Leave the phone untouched.");
            token.ThrowIfCancellationRequested();
            modeTouched = true;
            var mode = await modes.SetTalkBackAsync(true, token).ConfigureAwait(false);
            await _modeSettle(token).ConfigureAwait(false);
            var phase = Verify(viewport, verifiedStart.Cursor, beforeMode: verifiedStart);
            if (mode is not null)
            {
                talkBack = "Unavailable. " + mode;
                swipe = "Unavailable. " + mode;
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
                    if (ExplorePointProblem(viewport, before, box, targets, out var point) is { } problem)
                    {
                        skipped++;
                        Note(targets, AndroidInvestigationInput.Explore, $"Not sampled: {problem}.");
                        continue;
                    }
                    var reply = await SendAsync(before, point, AndroidInvestigationInput.Explore, token).ConfigureAwait(false);
                    // Starting a service is asynchronous. Retry only a positive refusal
                    // that cannot have sent any input, and recheck the frozen scope.
                    if (sent == 0 && reply.Status == "talkback-unavailable")
                    {
                        await _modeSettle(token).ConfigureAwait(false);
                        before = Verify(viewport, before.Cursor);
                        if (ExplorePointProblem(viewport, before, box, targets, out point) is { } retryProblem)
                        {
                            skipped++;
                            Note(targets, AndroidInvestigationInput.Explore, $"Not sampled: {retryProblem}.");
                            continue;
                        }
                        reply = await SendAsync(before, point, AndroidInvestigationInput.Explore, token).ConfigureAwait(false);
                    }
                    if (reply.Status != "dispatched" || reply.Started <= 0)
                    {
                        var unavailable = InputProblem(reply.Status);
                        Note(targets, AndroidInvestigationInput.Explore, unavailable);
                        talkBack = $"{sent} completed point samples; {reached} mapped focus observations. {unavailable}";
                        if (UnsafeReply(reply))
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
                    var focus = observedExplore ? VerifiedFocus(phase, before, events, reply.Started, explore: true,
                        compareLayer: !modelProposalsOnly) : null;
                    if (focus is { } hit && AndroidInteractionInventory.Contains(hit.Node.Bounds,
                        new(point.X, point.Y, point.X + 1, point.Y + 1)))
                    {
                        var matched = FocusTargets(rows, original, hit.Node, modelProposalsOnly);
                        if (modelProposalsOnly)
                        {
                            matched = matched.Intersect(targets).ToArray();
                        }
                        if (matched.Count > 0)
                        {
                            reached++;
                            foreach (var target in matched)
                            {
                                target.Explore.Add(modelProposalsOnly
                                    ? $"Matched Android node reached at ({point.X}, {point.Y}); event {hit.Event}, tree {hit.Tree}."
                                    : $"Reached at ({point.X}, {point.Y}); event {hit.Event}, tree {hit.Tree}.");
                            }
                        }
                        Note(targets.Except(matched), AndroidInvestigationInput.Explore,
                            $"At ({point.X}, {point.Y}), TalkBack reached {AndroidTalkBackExploration.ItemLabel(hit.Node)} " +
                            $"(event {hit.Event}, tree {hit.Tree}); this proposal was not verified.");
                    }
                    else
                    {
                        Note(targets, AndroidInvestigationInput.Explore, observedExplore ?
                            $"At ({point.X}, {point.Y}), exploration was observed but no app item focus was verified." :
                            $"At ({point.X}, {point.Y}), Android completed the gesture; touch-exploration events were not verified.");
                    }
                    talkBack = $"{sent} point samples completed; {observed} corroborated as touch exploration; {reached} mapped focus observations." +
                        (skipped > 0 ? $" {skipped} points skipped; see the individual reasons below." : "") +
                        (sent + skipped < explorePoints.Length ? $" {explorePoints.Length - sent - skipped} planned points not yet sampled." : "");
                }
                if (sent == 0 && skipped == explorePoints.Length)
                {
                    talkBack = skipped == 0 ? "No exploration points were planned." :
                        $"All {skipped} exploration points were skipped; see the individual reasons below.";
                }
                if (_investigationSwipe is not null)
                {
                    phase = await InvestigateSwipesAsync(viewport, phase, original, rows, modelProposalsOnly,
                        summary => swipe = summary, progress, token).ConfigureAwait(false);
                }
            }

            phase = Verify(viewport, phase.Cursor);
            progress?.Report("Testing keyboard Tab without TalkBack intercepting it.");
            token.ThrowIfCancellationRequested();
            mode = await modes.SetTalkBackAsync(false, token).ConfigureAwait(false);
            await _modeSettle(token).ConfigureAwait(false);
            phase = Verify(viewport, phase.Cursor, beforeMode: phase);
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
                        if (UnsafeReply(reply))
                        {
                            throw new InvestigationStop(unavailable);
                        }
                        break;
                    }
                    before = Verify(viewport, before.Cursor);
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
                    if (VerifiedFocus(phase, before, events, reply.Started, explore: false, compareLayer: !modelProposalsOnly) is { } hit)
                    {
                        foreach (var matched in FocusTargets(rows, original, hit.Node, modelProposalsOnly))
                        {
                            matched.Tab.Add($"{(modelProposalsOnly ? "Matched Android node reached" : "Reached")} on Tab {step}; " +
                                $"input focus (event {hit.Event}, tree {hit.Tree}).");
                        }
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
                    var before = Verify(viewport, phase.Cursor, pictureBaseline: phase.Frame);
                    var at = Center(box);
                    var overlapping = rows.Where(row => (row.Candidate.OriginalProposalBox is { } originalBox
                        ? new[] { originalBox }
                        : row.Candidate.Item.Boxes).Any(bounds =>
                        at.X >= bounds.Left && at.X < bounds.Right && at.Y >= bounds.Top && at.Y < bounds.Bottom));
                    if (overlapping.Select(row => row.Candidate.DuplicateTapRestriction).FirstOrDefault(reason => reason is not null)
                        is { } restriction)
                    {
                        skipped++;
                        Note(targets, AndroidInvestigationInput.Tap, $"Not tapped: {restriction}.");
                        continue;
                    }
                    if (overlapping.Any(row => row.Candidate.Item.Enabled == false))
                    {
                        skipped++;
                        Note(targets, AndroidInvestigationInput.Tap, "Not tapped: the location overlaps a disabled Android item.");
                        continue;
                    }
                    if (overlapping.Any(row => SensitiveAction(row.Candidate.Item.Label) ||
                        (row.Candidate.VisionLabel is { } label && SensitiveAction(label))))
                    {
                        skipped++;
                        Note(targets, AndroidInvestigationInput.Tap, "Not tapped: label suggests a consequential action.");
                        continue;
                    }
                    if (modelProposalsOnly && TreeTapRestriction(before, at) is { } treeRestriction)
                    {
                        skipped++;
                        Note(targets, AndroidInvestigationInput.Tap, $"Not tapped: {treeRestriction}.");
                        continue;
                    }
                    if (!TryPoint(before, box, out var point))
                    {
                        skipped++;
                        Note(targets, AndroidInvestigationInput.Tap, "Not tapped: the point was covered or outside the app.");
                        continue;
                    }
                    var pointProblem = modelProposalsOnly ? viewport.PointProblem(before, box, targets) :
                        targets.All(row => row.Candidate.Item.NodeIndex is null || row.Candidate.Item.Role == "Inline link") &&
                        PixelsChanged(start.Frame!, before.Frame!, box) ? "the picture at this point changed" : null;
                    if (pointProblem is not null)
                    {
                        skipped++;
                        Note(targets, AndroidInvestigationInput.Tap, $"Not tapped: {pointProblem}.");
                        continue;
                    }
                    var reply = await SendAsync(before, point, AndroidInvestigationInput.Tap, token).ConfigureAwait(false);
                    if (reply.Status != "dispatched" || reply.Started <= 0)
                    {
                        var unavailable = InputProblem(reply.Status);
                        Note(targets, AndroidInvestigationInput.Tap, unavailable);
                        direct = $"{tapped} completed taps; {clicks} {clickCountLabel}; {visual} local visual changes. {unavailable}";
                        if (UnsafeReply(reply))
                        {
                            throw new InvestigationStop(unavailable);
                        }
                        break;
                    }
                    tapped++;
                    direct = $"{tapped} completed taps; {clicks} {clickCountLabel}; {visual} local visual changes. Pass interrupted.";
                    await _settle(token).ConfigureAwait(false);
                    var events = Events(before);
                    var after = Capture();
                    var result = ClickOrPicture(before, after, events, reply.Started, box, point, modelProposalsOnly);
                    clicks += result.ClickEvent.HasValue ? 1 : 0;
                    visual += result.AfterFrame.HasValue ? 1 : 0;
                    foreach (var target in targets)
                    {
                        var evidence = modelProposalsOnly ? AttributeClick(result, events, original, target.Candidate) : result.Evidence;
                        target.Touch.Add($"At ({point.X}, {point.Y}): {evidence}");
                    }
                    direct = $"{tapped} completed taps; {clicks} {clickCountLabel}; {visual} local visual changes." +
                        (skipped > 0 ? $" {skipped} points skipped." : "") +
                        (tapped + skipped < tapPoints.Length ? $" {tapPoints.Length - tapped - skipped} planned points not tested." : "");
                    phase = Verify(viewport, before.Cursor, afterTap: true, expectedClick: result.ClickEvent,
                        pictureBaseline: before.Frame, tappedBox: box);
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
                if (modes is not null && modeTouched)
                {
                    progress?.Report("Verifying restoration of the original TalkBack setting.");
                    restoration = await modes.RestoreAsync().ConfigureAwait(false) ??
                        (modelProposalsOnly ? "The original TalkBack setting was verified or no setting change was made." : null);
                }
                if (began)
                {
                    // The result text may include live app names. Keep only stage summaries in recording markers.
                    marker($"Recorder screen investigation {tag}: ended. {status} Swipe: {swipe} Explore by Touch: {talkBack} " +
                        $"Keyboard: {keyboard} Direct touch: {direct}" + (restoration is null ? "" : " " + restoration));
                }
            }
            finally
            {
                Volatile.Write(ref _investigating, 0);
            }
        }
        var picture = !modelProposalsOnly && start is not null && AndroidInvestigationPictures.CanUse(start) ? start : null;
        var findings = rows.Select(row => row.Result(picture)).ToArray();
        return new(status, coverage, talkBack, keyboard, direct, findings, restoration,
            start?.App.Items?.SnapshotIndex, picture?.Frame?.Sequence)
        {
            Swipe = swipe,
            StartingPicture = findings.Any(finding => finding.StartingLocation is not null) ? picture?.Frame : null
        };
    }

    private AndroidInteractionSnapshot Verify(InvestigationViewport viewport, long since,
        bool afterTap = false, long? expectedClick = null, AndroidInteractionSnapshot? beforeMode = null, bool beforeInput = false,
        AndroidLivePreviewFrame? pictureBaseline = null, AndroidNodeBounds? tappedBox = null)
    {
        var current = Capture();
        if (!_ready())
        {
            throw Stop("The accessibility event feed or physical input monitor was unavailable.");
        }
        if (current is null)
        {
            throw Stop("No active app window with a live Android tree was available.");
        }
        if (current.Tree.Problem is not null || current.Tree.EarlierUpdateMissing ||
            current.App.Items is not { Truncated: false, MissingChildren: 0 })
        {
            throw Stop("The live Android tree was incomplete.");
        }
        var input = _input();
        if (input.Version != viewport.Start.Input.Version || input.TouchDown ||
            (input.LastAt > 0 && Stopwatch.GetElapsedTime(input.LastAt) < TimeSpan.FromMilliseconds(400)))
        {
            throw Stop("Physical phone input was detected.");
        }
        if (current.Preview.State != AndroidLivePreviewState.ShowingFrames ||
            current.Preview.StreamSession != viewport.Start.Preview.StreamSession)
        {
            throw Stop("The live phone video stream stopped or changed.");
        }
        if (current.Display is null || viewport.Start.Display is null)
        {
            throw Stop("Phone display geometry was unavailable for the picture.");
        }
        if (current.Display.Rotation != viewport.Start.Display.Rotation ||
            current.Frame?.Width != viewport.Start.Frame?.Width || current.Frame?.Height != viewport.Start.Frame?.Height)
        {
            throw Stop("The phone display rotated or the picture dimensions changed.");
        }
        var startingWindow = viewport.Start.App.Window;
        var activeWindow = current.App.Window;
        if (activeWindow.Id != startingWindow.Id || activeWindow.DisplayId != startingWindow.DisplayId ||
            current.PackageName != viewport.Start.PackageName)
        {
            throw Stop("The active Android app or window changed.");
        }
        if (activeWindow.Bounds != startingWindow.Bounds)
        {
            throw Stop("The active app window moved or resized.");
        }
        if (activeWindow.TransitionTimeMilliseconds != startingWindow.TransitionTimeMilliseconds)
        {
            throw Stop("Android reported a new transition for the active app window.");
        }
        if (viewport.ProposalsOnly && (activeWindow.ParentId != startingWindow.ParentId || activeWindow.Title != startingWindow.Title))
        {
            throw Stop("The active app window's title or parent changed.");
        }
        if (!viewport.ProposalsOnly && activeWindow.Layer != startingWindow.Layer)
        {
            throw Stop("The active app window's stacking layer changed.");
        }
        if (!current.HasScreenCoordinates || !viewport.Start.HasScreenCoordinates)
        {
            throw Stop("The phone picture could not be aligned to the live display and app window.");
        }
        var batch = _signals.After(since);
        if (batch.Gap || batch.Events.Any(entry => entry is null))
        {
            throw Stop("Accessibility events were missing.");
        }
        foreach (var entry in batch.Events.OfType<AndroidAccessibilityEvent>())
        {
            if (IsWindowChange(entry, current, beforeMode, viewport.ProposalsOnly))
            {
                throw Stop("Android reported a window change.");
            }
            if (entry.EventTypeName == "TYPE_VIEW_SCROLLED")
            {
                throw Stop("Android reported scrolling.");
            }
            if (entry.EventTypeName is "TYPE_VIEW_LONG_CLICKED" or "TYPE_VIEW_TEXT_CHANGED" ||
                entry.EventTypeName == "TYPE_VIEW_CLICKED" && (!afterTap || entry.Index != expectedClick))
            {
                throw Stop($"Android reported an unexpected action ({entry.EventTypeName}).");
            }
        }
        if (!viewport.Matches(current.App.Items))
        {
            throw Stop(viewport.ProposalsOnly ? "Most of the original matched controls moved or no longer match the live Android tree." :
                "The visible app structure changed.");
        }
        if ((beforeInput || beforeMode is not null || viewport.ProposalsOnly || afterTap) &&
            FrameChangedOutside(pictureBaseline ?? viewport.Start.Frame!, current.Frame!, activeWindow.Bounds, tappedBox))
        {
            if (beforeInput)
            {
                throw new InvestigationStop("The app picture changed substantially during discovery. No input was sent.");
            }
            throw Stop(beforeMode is not null ? "The app picture changed substantially while switching TalkBack modes." :
                afterTap ? "The picture changed beyond the tapped target." : "The app picture changed substantially during input testing.");
        }
        return new(current.Tree, current.App, current.Scope, current.Preview, current.Display, current.Input, batch.Cursor);

        static InvestigationStop Stop(string reason) => new(reason + " Remaining input was not sent.");
    }

    private static bool IsWindowChange(AndroidAccessibilityEvent entry, AndroidInteractionSnapshot current,
        AndroidInteractionSnapshot? beforeMode, bool proposalsOnly)
    {
        if (entry.EventTypeName is not (AndroidAccessibilityRecords.WindowStateChangedName or AndroidAccessibilityRecords.WindowsChangedName))
        {
            return false;
        }
        if (!proposalsOnly)
        {
            return beforeMode is null || entry.PackageName == current.PackageName;
        }
        // Stack numbering and accessibility focus are not window moves. The host
        // and companion still use the current overlay order to check each point.
        const int layerOrAccessibilityFocus = 0x10 | 0x80;
        if (entry.EventTypeName == AndroidAccessibilityRecords.WindowsChangedName &&
            entry.WindowId == current.App.Window.Id && entry.WindowChanges != 0 &&
            (entry.WindowChanges & ~layerOrAccessibilityFocus) == 0)
        {
            return false;
        }
        if (beforeMode is null || entry.WindowId == current.App.Window.Id || entry.PackageName == current.PackageName)
        {
            return true;
        }
        const int overlayLifecycle = 0x1 | 0x2 | 0x10 | 0x20 | 0x40 | 0x80;
        // Account for accessibility overlays added or removed while we switch
        // TalkBack, but do not suppress changes from another application window.
        return entry.PackageName != AndroidDeviceChecks.TalkBackPackage &&
            !(entry.EventTypeName == AndroidAccessibilityRecords.WindowsChangedName && entry.WindowChanges != 0 &&
              (entry.WindowChanges & ~overlayLifecycle) == 0 &&
              current.Tree.Windows.Concat(beforeMode.Tree.Windows).Any(window => window.Window is
                  { Type: 4, DisplayId: 0 } overlay && overlay.Id == entry.WindowId));
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

    private static string? ExplorePointProblem(InvestigationViewport viewport, AndroidInteractionSnapshot current,
        AndroidNodeBounds box, IReadOnlyList<Finding> targets, out AndroidInteractionPoint point) =>
        !TryPoint(current, box, out point) ? "the point is outside the app or covered" :
        viewport.ProposalsOnly ? viewport.PointProblem(current, box, targets) : null;

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
        AndroidInteractionSnapshot before, IReadOnlyList<AndroidAccessibilityEvent> events, long started, bool explore,
        bool allowHover = true, bool compareLayer = true)
    {
        if (after.App.Items is not { } items || items.SnapshotIndex <= before.App.Items!.SnapshotIndex ||
            !SameWindow(before, after.App, compareLayer) || !(explore ? after.App.Window.AccessibilityFocused : after.App.Window.Focused))
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
            (explore ? entry.EventTypeName == AndroidTalkBackSignals.Focus || allowHover && entry.EventTypeName == HoverEnter
                : entry.EventTypeName == InputFocus))
            .ToArray();
        var preferred = matches.Where(entry => entry.EventTypeName == (explore ? AndroidTalkBackSignals.Focus : InputFocus)).ToArray();
        var candidates = preferred.Length > 0 ? preferred : matches.Where(entry => NodeKey.Of(entry.Source!) == key).ToArray();
        return candidates.Length == 1 && NodeKey.Of(candidates[0].Source!) == key &&
            items.Nodes.Count(node => node.VisibleToUser && NodeKey.Of(node) == key) == 1 ?
            (focused[0], candidates[0].Index, items.SnapshotIndex) : null;
    }

    private static AndroidInteractionTapResult ClickOrPicture(AndroidInteractionSnapshot before, AndroidInteractionSnapshot? after,
        AndroidAccessibilityEvent[] events, long started, AndroidNodeBounds box, AndroidInteractionPoint point,
        bool explicitProposal)
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
        if (explicitProposal && events.Any(entry => entry.EventTypeName == "TYPE_VIEW_CLICKED" && Timed(entry, started)))
        {
            return new("Android reported a click, but its source could not be tied to the sampled point.");
        }
        if (after?.Frame is { } picture && after.HasScreenCoordinates && SameWindow(before, after.App, compareLayer: !explicitProposal) &&
            before.Preview.StreamSession == after.Preview.StreamSession && before.Display?.Rotation == after.Display?.Rotation &&
            picture.Sequence > before.Frame!.Sequence && picture.PhoneNanoseconds >= started + 55_000_000 &&
            PixelsChanged(before.Frame, picture, box))
        {
            if (explicitProposal && FrameChangedOutside(before.Frame, picture, before.App.Window.Bounds, box))
            {
                return new($"The picture changed at and beyond the sampled region (frame {picture.Sequence}); no target activation was verified.");
            }
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

    private static IReadOnlyList<Finding> FocusTargets(List<Finding> findings, AndroidLiveTreeItems original,
        AndroidTreeNode focused, bool proposalsOnly)
    {
        if (!proposalsOnly)
        {
            return [FocusRow(findings, original, focused)];
        }
        var key = NodeKey.Of(focused);
        var nodes = original.Nodes.Where(node => node.VisibleToUser && NodeKey.Of(node) == key).Take(2).ToArray();
        return nodes.Length != 1 ? [] : findings.Where(finding => finding.Candidate.Item.NodeIndex == nodes[0].Index &&
            finding.Candidate.Item.Role != "Inline link").ToArray();
    }

    private static string AttributeClick(AndroidInteractionTapResult result, AndroidAccessibilityEvent[] events,
        AndroidLiveTreeItems original, AndroidInvestigationCandidate candidate)
    {
        if (result.ClickEvent is not { } index || events.FirstOrDefault(item => item.Index == index)?.Source is not { } source)
        {
            return result.Evidence;
        }
        if (candidate.Item.NodeIndex is not { } expected || candidate.Item.Role == "Inline link")
        {
            return $"{result.Evidence} This establishes a click at the sampled point, not the identity of a separate control.";
        }
        var key = NodeKey.Of(source);
        var nodes = original.Nodes.Where(node => node.VisibleToUser && NodeKey.Of(node) == key).Take(2).ToArray();
        if (nodes.Length != 1)
        {
            return $"Android reported a click at the point (event {index}), but the matched Android node was not uniquely identified.";
        }
        return nodes[0].Index == expected ? $"Android reported a click on the matched node (event {index})." :
            $"Android reported a click on another starting node ({nodes[0].Index}, event {index}), not on the matched node.";
    }

    private static string? TreeTapRestriction(AndroidInteractionSnapshot start, (int X, int Y) point)
    {
        var at = new AndroidNodeBounds(point.X, point.Y, point.X + 1, point.Y + 1);
        foreach (var node in start.App.Items!.Nodes.Where(node => node.VisibleToUser &&
            AndroidProposalInputPlan.AcceptsTouch(node) && AndroidInteractionInventory.Contains(node.Bounds, at)))
        {
            if (!node.Enabled)
            {
                return "Android reports a disabled control at the point";
            }
            if (new[] { node.Text, node.ContentDescription, node.Hint }.OfType<string>().Any(SensitiveAction) ||
                node.Actions.Any(action => action.Label is { } label && SensitiveAction(label)))
            {
                return "an Android control at the point suggests a consequential action";
            }
        }
        return null;
    }

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
        "expired-or-busy" => "The phone companion was busy or the input request expired.",
        "companion-update-required" => "Update the phone companion to run this investigation.",
        _ => "The companion did not confirm this input.",
    };

    private static bool UnsafeReply(CompanionNavigationReply reply) => reply.Started > 0 ||
        reply.Status is "scope-changed" or "connection-lost" or "expired-or-busy" or "companion-update-required" or "gesture-cancelled";

    internal static bool SensitiveAction(string label)
    {
        var words = string.Concat(label.Select(character => char.IsLetterOrDigit(character) ? character : ' '))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Any(word => SensitiveWords.Contains(word));
    }

    private static readonly HashSet<string> SensitiveWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete", "erase", "destroy", "purchase", "buy", "pay", "send", "submit", "transfer", "factory", "uninstall",
        "publish", "checkout", "follow", "following", "unfollow", "subscribe", "subscribed", "unsubscribe", "like",
        "liked", "unlike", "share", "repost",
        "post", "favorite", "unfavorite", "block", "unblock", "report", "message", "call", "login", "logout", "approve",
        "accept", "invite", "donate", "upload", "grant", "allow", "confirm", "bookmark", "save", "remove"
    };

    private sealed class Finding(AndroidInvestigationCandidate candidate)
    {
        public AndroidInvestigationCandidate Candidate { get; } = candidate;
        public NodeKey? Observed { get; init; }
        public bool HasStartingLocation { get; init; } = true;
        public List<string> Explore { get; } = [];
        public List<string> Swipe { get; } = [];
        public List<string> Tab { get; } = [];
        public List<string> Touch { get; } = [];
        public List<AndroidInvestigationTapPictures> Pictures { get; } = [];

        public AndroidInvestigationFinding Result(AndroidInteractionSnapshot? picture) => new(Candidate.Item,
            Explore.Count > 0 ? Explore : [Candidate.SampleByTouch ? "Not sampled in this run." : "Not sampled for this item."],
            Tab.Count > 0 ? Tab : ["Not verified by Tab in this run."],
            Touch.Count > 0 ? Touch : [Candidate.TapSkipReason is { } reason ? $"Not tapped: {reason}." :
                Candidate.Item.Enabled == false ? "Not tapped: Android reports disabled." :
                Candidate.Tap ? "Not tested in this run." : "Not a direct-tap candidate."])
        {
            ProposalIndex = Candidate.ProposalIndex,
            Swipe = Swipe.Count > 0 ? Swipe : ["Not verified by TalkBack swipe in this run."],
            StartingLocation = HasStartingLocation && picture is not null ?
                AndroidInvestigationPictures.Locate(picture, Candidate.Item.Boxes) : null,
            TapPictures = Pictures.ToArray()
        };
    }

    private sealed class InvestigationViewport
    {
        private readonly VisibleTree _original;
        private readonly Dictionary<int, NodeKey> _matchedTargets;
        private readonly NodeKey[] _anchors;
        private readonly ViewportNode[] _legacyNodes;
        private AndroidLiveTreeItems _last;
        private VisibleTree _live;
        private bool _matched = true;

        public InvestigationViewport(AndroidInteractionSnapshot start, AndroidLiveTreeItems original, List<Finding>? proposals)
        {
            Start = start;
            ProposalsOnly = proposals is not null;
            _original = _live = new(original);
            _last = original;
            _legacyNodes = ProposalsOnly ? [] : Nodes(original);
            var ids = proposals?.Select(finding => finding.Candidate.Item.NodeIndex).OfType<int>().ToHashSet() ?? [];
            _matchedTargets = _original.Nodes.Where(node => ids.Contains(node.Index))
                .ToDictionary(node => node.Index, NodeKey.Of);
            _anchors = _matchedTargets.Values.Where(key => _original.Unique(key) is not null).ToArray();
        }

        public AndroidInteractionSnapshot Start { get; }
        public bool ProposalsOnly { get; }

        public bool Matches(AndroidLiveTreeItems items)
        {
            if (!ReferenceEquals(_last, items))
            {
                if (ProposalsOnly)
                {
                    _live = new(items);
                    // Node indexes, order and accessibility wrappers are not stable
                    // across snapshots. Count only distinct, originally unique nodes.
                    _matched = _anchors.Length < 2 || _anchors.Count(key => _live.Unique(key) is not null) * 2 >= _anchors.Length;
                }
                else
                {
                    _matched = _legacyNodes.SequenceEqual(Nodes(items));
                }
                _last = items;
            }
            return _matched;
        }

        public string? PointProblem(AndroidInteractionSnapshot current, AndroidNodeBounds box, IReadOnlyList<Finding> targets)
        {
            var (x, y) = Center(box);
            var at = new AndroidNodeBounds(x, y, x + 1, y + 1);
            foreach (var target in targets)
            {
                if (target.Candidate.Item.NodeIndex is not { } id)
                {
                    continue;
                }
                if (!_matchedTargets.TryGetValue(id, out var key) || _original.Unique(key) is not { } original ||
                    _live.Unique(key) is not { } live || !AndroidInteractionInventory.Contains(live.Bounds, at) ||
                    !SamePointState(original, live))
                {
                    return "the starting matched Android node changed or was not uniquely identifiable at the point";
                }
            }

            // Even an unchanged proposed node can acquire a different hit target
            // over the point. Compare actual touch declarations, not focus flags.
            var originalHits = _original.TouchHits(at).ToArray();
            var liveHits = _live.TouchHits(at).ToArray();
            if (!SameBag(originalHits.Select(NodeKey.Of), liveHits.Select(NodeKey.Of)) ||
                liveHits.Any(node => _original.Unique(NodeKey.Of(node)) is not { } original ||
                    _live.Unique(NodeKey.Of(node)) is null || !SamePointState(original, node)) ||
                !SameBag(_original.LinksAt(at), _live.LinksAt(at)))
            {
                return "the Android touch targets at this point changed or were not unique";
            }
            return PixelsChanged(Start.Frame!, current.Frame!, box) ? "the picture at this point changed" : null;
        }

        private static bool SamePointState(AndroidTreeNode a, AndroidTreeNode b) =>
            (a.Enabled, a.Clickable, a.LongClickable, a.ContextClickable, a.Editable, a.Checkable, a.Checked) ==
            (b.Enabled, b.Clickable, b.LongClickable, b.ContextClickable, b.Editable, b.Checkable, b.Checked) &&
            SameBag(a.Actions.Where(action => action.Id is not (1 or 2 or 64 or 128)),
                b.Actions.Where(action => action.Id is not (1 or 2 or 64 or 128)));

        private static bool SameBag<T>(IEnumerable<T> a, IEnumerable<T> b) where T : notnull
        {
            var counts = a.GroupBy(item => item).ToDictionary(group => group.Key, group => group.Count());
            foreach (var item in b)
            {
                if (!counts.TryGetValue(item, out var count))
                {
                    return false;
                }
                if (count == 1)
                {
                    counts.Remove(item);
                }
                else
                {
                    counts[item] = count - 1;
                }
            }
            return counts.Count == 0;
        }

        private sealed class VisibleTree(AndroidLiveTreeItems items)
        {
            private readonly Dictionary<NodeKey, AndroidTreeNode> _uniqueNodes = items.Nodes.Where(node => node.VisibleToUser)
                .GroupBy(NodeKey.Of).Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single());
            public AndroidTreeNode[] Nodes { get; } = items.Nodes.Where(node => node.VisibleToUser).ToArray();

            public AndroidTreeNode? Unique(NodeKey key) => _uniqueNodes.GetValueOrDefault(key);

            public IEnumerable<AndroidTreeNode> TouchHits(AndroidNodeBounds at) => Nodes.Where(node =>
                AndroidProposalInputPlan.AcceptsTouch(node) && AndroidInteractionInventory.Contains(node.Bounds, at));

            public IEnumerable<(NodeKey, int, int, string, string?, AndroidNodeBounds)> LinksAt(AndroidNodeBounds at) =>
                from node in Nodes
                from link in node.Links ?? []
                from box in link.Boxes
                where AndroidInteractionInventory.Contains(box, at)
                select (NodeKey.Of(node), link.Start, link.End, link.Kind, link.Url, box);
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

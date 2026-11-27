using System.Diagnostics;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

internal readonly record struct AndroidTalkBackInput(long Version, long LastAt, bool TouchDown);

internal sealed record AndroidTalkBackScope(AndroidAccessibilityWindow Window, string PackageName)
{
    internal static AndroidLiveWindowSnapshot? ActiveApp(AndroidLiveTreeSnapshot tree)
    {
        var apps = tree.Windows.Where(entry => entry.Window is
            { Active: true, Type: 1, DisplayId: 0, PictureInPicture: false }).Take(2).ToArray();
        return apps.Length == 1 ? apps[0] : null;
    }
}

/// <summary>Small cursor-based buffer of events relevant to exploration, separate from capture.</summary>
internal sealed class AndroidTalkBackSignals
{
    internal const string Focus = AndroidAccessibilityRecords.AccessibilityFocusedName;
    private const int Capacity = 256;
    private readonly object _gate = new();
    private readonly Queue<(long Cursor, AndroidAccessibilityEvent? Event)> _events = new();
    private long _cursor;
    private long _index;

    public long Cursor { get { lock (_gate) { return _cursor; } } }

    public void Observe(AndroidAccessibilityEvent? item)
    {
        lock (_gate)
        {
            if (item is null || (_index > 0 && item.Index != _index + 1))
            {
                Keep(null);
            }
            _index = item?.Index ?? 0;
            if (item is not null && (item.EventTypeName is Focus or "TYPE_VIEW_SCROLLED" or
                AndroidAccessibilityRecords.WindowStateChangedName or "TYPE_VIEW_CLICKED" or "TYPE_VIEW_LONG_CLICKED" or
                "TYPE_VIEW_TEXT_CHANGED" or "TYPE_VIEW_ACCESSIBILITY_FOCUS_CLEARED" ||
                // Window accessibility focus alone is an expected TalkBack response.
                (item.EventTypeName == AndroidAccessibilityRecords.WindowsChangedName && item.WindowChanges != 0x80)))
            {
                Keep(item);
            }
        }
    }

    public (long Cursor, AndroidAccessibilityEvent?[] Events, bool Gap) After(long cursor)
    {
        lock (_gate)
        {
            return (_cursor, _events.Where(item => item.Cursor > cursor).Select(item => item.Event).ToArray(),
                _events.TryPeek(out var first) && first.Cursor > cursor + 1);
        }
    }

    private void Keep(AndroidAccessibilityEvent? item)
    {
        if (_events.Count == Capacity)
        {
            _events.Dequeue();
        }
        _events.Enqueue((++_cursor, item));
    }
}

public sealed record AndroidTalkBackStop(int Gesture, string Direction, string Item, long EventIndex, long SnapshotIndex);
public sealed record AndroidTalkBackCandidate(int NodeIndex, string Item, string Observation);
public sealed record AndroidTalkBackExplorationResult(
    string Reason, int GestureCount, IReadOnlyList<AndroidTalkBackStop> Stops,
    IReadOnlyList<AndroidTalkBackCandidate> Candidates, long? BaselineSnapshot, int UnlistedCandidates,
    bool BothDirectionsTried, string? StartingFocus);

/// <summary>
/// Bounded linear TalkBack traversal of a single observed viewport. A gesture
/// reply is not evidence of reachability; both event and fresh tree are needed.
/// </summary>
public sealed class AndroidTalkBackExploration
{
    private const int MaxGestures = 60;
    private const int MaxCandidates = 500;
    private static readonly TimeSpan InputQuiet = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Settling = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan MoveTimeout = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(45);

    private readonly AndroidLivePreview _preview;
    private readonly AndroidLiveTree _tree;
    private readonly AndroidTalkBackSignals _signals;
    private readonly Func<bool> _available;
    private readonly Func<AndroidTalkBackInput> _input;
    private readonly Func<bool, AndroidTalkBackScope, CancellationToken, Task<CompanionNavigationReply>> _navigate;
    private readonly TimeProvider _time;
    private int _running;

    public AndroidTalkBackExploration(AndroidLivePreview preview, AndroidLiveTree tree,
        AndroidAccessibilityCollector events, AndroidTouchCollector touch, AndroidCompanionCollector companion)
        : this(preview, tree, events.LiveSignals,
            () => events.ExplorationEventsAvailable && touch.ExplorationInputAvailable,
            () => touch.ExplorationInput, companion.NavigateTalkBackAsync, TimeProvider.System) { }

    internal AndroidTalkBackExploration(AndroidLivePreview preview, AndroidLiveTree tree, AndroidTalkBackSignals signals,
        Func<bool> available, Func<AndroidTalkBackInput> input,
        Func<bool, AndroidTalkBackScope, CancellationToken, Task<CompanionNavigationReply>> navigate, TimeProvider time)
    {
        _preview = preview;
        _tree = tree;
        _signals = signals;
        _available = available;
        _input = input;
        _navigate = navigate;
        _time = time;
    }

    public async Task<AndroidTalkBackExplorationResult> RunAsync(
        Func<string, bool> recordMarker, bool singleSwipe, CancellationToken cancellation)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            return new("An exploration is already running.", 0, [], [], null, 0, false, null);
        }

        var stops = new List<AndroidTalkBackStop>();
        AndroidLiveTreeItems? baseline = null;
        var reached = new HashSet<NodeKey>();
        NodeKey? starting = null;
        var attempts = 0;
        var both = false;
        var finishedSweep = false;
        var reason = "Exploration did not start.";
        string? tag = null;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var started = _time.GetTimestamp();
            var cursor = _signals.Cursor;
            var view = _preview.Current;
            var tree = _tree.Current;
            var active = AndroidTalkBackScope.ActiveApp(tree);
            var input = _input();
            if (!Ready(view))
            {
                reason = "Live video, accessibility events and physical input monitoring must be available.";
            }
            else if (input.TouchDown || (input.LastAt != 0 && Stopwatch.GetElapsedTime(input.LastAt) < InputQuiet))
            {
                reason = "Phone input was just detected. Lift your finger, wait a moment, then try again.";
            }
            else if (tree.State != AndroidLiveTreeState.Observed || tree.Problem is not null ||
                active is not { Window.AccessibilityFocused: true, Items: { Truncated: false, MissingChildren: 0 } items } ||
                items.Nodes.FirstOrDefault(node => node.Depth == 0)?.PackageName is not { Length: > 0 } package ||
                SingleFocused(items) is not { VisibleToUser: true } initial)
            {
                reason = "Start with TalkBack focused on an item in the active app and a readable app tree.";
            }
            else
            {
                baseline = items;
                starting = NodeKey.Of(initial);
                var scope = new AndroidTalkBackScope(active.Window, package);
                var viewport = new Viewport(items);
                var passTag = Guid.NewGuid().ToString("N")[..8];
                if (!recordMarker($"Recorder TalkBack exploration {passTag}: started (automated {(singleSwipe ? "verification" : "navigation")})."))
                {
                    reason = "Could not mark the live recording. No swipes were sent.";
                }
                else
                {
                    tag = passTag;
                    // Start forward so a test beginning near the top can collect
                    // evidence before TalkBack tries to leave the app at an edge.
                    var next = true;
                    var seen = new HashSet<NodeKey> { starting };
                    var current = starting;
                    while (attempts < MaxGestures && _time.GetElapsedTime(started) < Limit)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (CheckScope(view, scope, viewport, input.Version) is { } preflight)
                        {
                            reason = preflight;
                            break;
                        }
                        var pending = _signals.After(cursor);
                        if (pending.Gap || pending.Events.Length > 0)
                        {
                            reason = "Uncommanded focus, phone changes or missing evidence between swipes.";
                            break;
                        }
                        var before = _tree.Current.Index ?? baseline.SnapshotIndex;
                        if (!next)
                        {
                            both = true;
                        }
                        // Once sent, wait briefly for the companion to finish the one swipe even
                        // if Stop is pressed. Never send another after cancellation.
                        using var replyLimit = new CancellationTokenSource(MoveTimeout, _time);
                        cancellation.ThrowIfCancellationRequested();
                        var reply = await _navigate(next, scope, replyLimit.Token).ConfigureAwait(false);
                        if (reply.Started > 0)
                        {
                            attempts++;
                        }
                        cancellation.ThrowIfCancellationRequested();
                        if (reply.Status != "dispatched" || reply.Started <= 0)
                        {
                            reason = ReplyReason(reply.Status);
                            break;
                        }
                        var move = await ObserveMoveAsync(view, scope, viewport, input.Version, before,
                            cursor, reply.Started, cancellation).ConfigureAwait(false);
                        cursor = move.Cursor;
                        if (move.Problem is not null)
                        {
                            reason = move.Problem;
                            break;
                        }
                        if (move.Node is { } node)
                        {
                            current = NodeKey.Of(node);
                            reached.Add(current);
                            stops.Add(new(attempts, next ? "Next" : "Previous", ItemLabel(node),
                                move.EventIndex, move.SnapshotIndex));
                        }
                        if (singleSwipe)
                        {
                            reason = move.Node is null
                                ? "No accessibility focus move was observed for the verification swipe."
                                : "One accessibility focus stop matched. Compare it with a physical right swipe from the same starting item.";
                            break;
                        }
                        if (move.Node is not null)
                        {
                            if (seen.Add(current))
                            {
                                continue;
                            }
                        }
                        if (!next)
                        {
                            finishedSweep = stops.Count > 0;
                            reason = move.Node is null
                                ? "No further focus move was observed. A traversal boundary was not proven."
                                : "Focus revisited an item in the reverse pass. A traversal boundary was not proven.";
                            break;
                        }
                        next = false;
                        seen = [current];
                    }
                    if (attempts >= MaxGestures || _time.GetElapsedTime(started) >= Limit)
                    {
                        finishedSweep = false;
                        reason = "Exploration reached its time or gesture limit.";
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            reason = cancellation.IsCancellationRequested ? "Exploration stopped by the recorder operator."
                : "The companion did not confirm the swipe in time.";
        }
        catch (IOException)
        {
            reason = "The connection to the companion was interrupted.";
        }
        finally
        {
            if (tag is not null && !recordMarker(
                $"Recorder TalkBack exploration {tag}: ended; {stops.Count} matched focus stops. {reason}"))
            {
                reason += " The end marker could not be saved.";
            }
            Volatile.Write(ref _running, 0);
        }
        var keys = baseline?.Nodes.Where(node => node.VisibleToUser).ToLookup(NodeKey.Of);
        var candidates = baseline?.Nodes.Where(node => IsCandidate(node) || (node.VisibleToUser &&
            (reached.Contains(NodeKey.Of(node)) || NodeKey.Of(node) == starting)))
            .ToArray() ?? [];
        return new(reason, attempts, stops, candidates.Take(MaxCandidates).Select(node =>
        {
            var key = NodeKey.Of(node);
            var observation = keys![key].Count() != 1 ? "Unknown: indistinguishable tree nodes"
                : reached.Contains(key) ? "Reached"
                : key == starting ? "Starting focus only"
                : finishedSweep ? "Exposed but not reached in this pass"
                : "Unknown: not observed in this partial pass";
            return new AndroidTalkBackCandidate(node.Index, ItemLabel(node), observation);
        }).ToArray(), baseline?.SnapshotIndex, Math.Max(0, candidates.Length - MaxCandidates), both,
            baseline is not null && SingleFocused(baseline) is { } start ? ItemLabel(start) : null);
    }

    private async Task<Move> ObserveMoveAsync(AndroidLivePreviewSnapshot view, AndroidTalkBackScope scope,
        Viewport viewport, long inputVersion, long before, long cursor, long sent, CancellationToken cancellation)
    {
        var started = _time.GetTimestamp();
        AndroidAccessibilityEvent? focus = null;
        long? matchAt = null;
        bool cleared = false;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (CheckScope(view, scope, viewport, inputVersion) is { } problem)
            {
                return new(cursor, Problem: problem);
            }
            var changes = _signals.After(cursor);
            cursor = changes.Cursor;
            if (changes.Gap || changes.Events.Any(change => change is null))
            {
                return new(cursor, Problem: "Some accessibility events were unavailable.");
            }
            foreach (var entry in changes.Events)
            {
                var change = entry!;
                if (change.EventTypeName == "TYPE_VIEW_SCROLLED")
                {
                    return new(cursor, Problem: "The phone scrolled. The scan did not continue beyond the starting viewport.");
                }
                if (change.EventTypeName is not (AndroidTalkBackSignals.Focus or "TYPE_VIEW_ACCESSIBILITY_FOCUS_CLEARED"))
                {
                    return new(cursor, Problem: "A window or app interaction changed during exploration.");
                }
                if (change.WindowId != scope.Window.Id || change.DisplayId is not (null or 0) ||
                    !string.Equals(change.PackageName, scope.PackageName, StringComparison.Ordinal))
                {
                    return new(cursor, Problem: "Accessibility focus moved outside the starting app window.");
                }
                if (change.EventTimeMilliseconds <= 0 || change.EventTimeMilliseconds < sent / 1_000_000 ||
                    change.ReceivedPhoneNanoseconds <= sent)
                {
                    return new(cursor, Problem: "A focus change could not be attributed to the requested swipe.");
                }
                if (change.EventTypeName == "TYPE_VIEW_ACCESSIBILITY_FOCUS_CLEARED")
                {
                    cleared = true;
                    continue;
                }
                if (focus is not null)
                {
                    return new(cursor, Problem: "More than one focus event followed a swipe. The target is uncertain.");
                }
                focus = change;
            }

            var tree = _tree.Current;
            var active = AndroidTalkBackScope.ActiveApp(tree);
            if (focus is not null && active?.Items is { } items && items.SnapshotIndex > before &&
                items.StartedPhoneNanoseconds >= focus.ReceivedPhoneNanoseconds &&
                active.Window.AccessibilityFocused && SingleFocused(items) is { VisibleToUser: true } node &&
                focus.Source is { } source && NodeKey.Of(source) == NodeKey.Of(node) &&
                items.Nodes.Count(candidate => candidate.VisibleToUser && NodeKey.Of(candidate) == NodeKey.Of(node)) == 1)
            {
                matchAt ??= _time.GetTimestamp();
                if (_time.GetElapsedTime(matchAt.Value) >= Settling)
                {
                    return new(cursor, node, focus.Index, items.SnapshotIndex);
                }
            }
            else
            {
                matchAt = null;
            }
            if (_time.GetElapsedTime(started) >= MoveTimeout)
            {
                return new(cursor, Problem: focus is null && !cleared ? null
                    : "A focus change could not be matched to one item in a fresh app tree.");
            }
            await Task.Delay(Poll, _time, cancellation).ConfigureAwait(false);
        }
    }

    private bool Ready(AndroidLivePreviewSnapshot view) => _available() && view.State == AndroidLivePreviewState.ShowingFrames &&
        view.Frame is not null;

    private string? CheckScope(AndroidLivePreviewSnapshot start, AndroidTalkBackScope scope, Viewport viewport,
        long inputVersion)
    {
        var input = _input();
        if (input.TouchDown || input.Version != inputVersion)
        {
            return "Physical phone input was detected. The scan stopped rather than mixing tester and recorder actions.";
        }
        var current = _preview.Current;
        if (!Ready(current))
        {
            return "A live recording feed became unavailable.";
        }
        if (current.StreamSession != start.StreamSession || current.Frame!.Width != start.Frame!.Width ||
            current.Frame.Height != start.Frame.Height)
        {
            return "The phone display changed during exploration.";
        }
        var tree = _tree.Current;
        var active = AndroidTalkBackScope.ActiveApp(tree);
        if (tree.State != AndroidLiveTreeState.Observed || tree.Problem is not null || tree.EarlierUpdateMissing ||
            active?.Items is not { Truncated: false, MissingChildren: 0 } items)
        {
            return "A complete live app tree was not available.";
        }
        if (active.Window.Id != scope.Window.Id || active.Window.Bounds != scope.Window.Bounds ||
            active.Window.TransitionTimeMilliseconds != scope.Window.TransitionTimeMilliseconds ||
            items.Nodes.FirstOrDefault(node => node.Depth == 0)?.PackageName != scope.PackageName ||
            !viewport.Matches(items))
        {
            return "The app window or its visible tree changed during exploration.";
        }
        return null;
    }

    private static AndroidTreeNode? SingleFocused(AndroidLiveTreeItems items)
    {
        var focused = items.Nodes.Where(node => node.AccessibilityFocused).Take(2).ToArray();
        return focused.Length == 1 ? focused[0] : null;
    }

    private sealed class Viewport(AndroidLiveTreeItems baseline)
    {
        private readonly NodeKey[] _baseline = Fingerprint(baseline);
        private AndroidLiveTreeItems _last = baseline;
        private bool _lastMatched = true;

        public bool Matches(AndroidLiveTreeItems items)
        {
            if (!ReferenceEquals(_last, items))
            {
                _lastMatched = Fingerprint(items).SequenceEqual(_baseline);
                _last = items;
            }
            return _lastMatched;
        }

        private static NodeKey[] Fingerprint(AndroidLiveTreeItems items) => items.Nodes
            .Where(node => node.VisibleToUser).Select(NodeKey.Of).ToArray();
    }

    private static bool IsCandidate(AndroidTreeNode node) => node.VisibleToUser &&
        (node.ScreenReaderFocusable || node.Focusable || node.Clickable || node.LongClickable || node.Checkable ||
         node.Editable || node.Heading || !string.IsNullOrWhiteSpace(node.Text) ||
         !string.IsNullOrWhiteSpace(node.ContentDescription) || !string.IsNullOrWhiteSpace(node.Hint));

    private static string ItemLabel(AndroidAccessibilityNode node)
    {
        var role = node.ClassName?.Split('.').LastOrDefault() is { Length: > 0 } name ? name : "item";
        if (node.Password)
        {
            return $"Password item ({role})";
        }
        var text = new[] { node.ContentDescription, node.Text, node.Hint }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (text is null)
        {
            return $"Unlabelled {role}";
        }
        var label = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return $"{(label.Length <= 160 ? label : label[..160] + "…")} ({role})";
    }

    private static string ReplyReason(string status) => status switch
    {
        "talkback-unavailable" => "TalkBack with touch exploration must be on. A second enabled accessibility service may also prevent automated swipes.",
        "scope-changed" => "The companion could not confirm the active app and an unobstructed swipe path.",
        "companion-update-required" => "Update the Android recorder companion to use TalkBack exploration.",
        "connection-lost" => "The Android companion connection was lost.",
        _ => "Android did not confirm the navigation swipe."
    };

    private sealed record Move(long Cursor, AndroidTreeNode? Node = null, long EventIndex = 0,
        long SnapshotIndex = 0, string? Problem = null);

    private sealed record NodeKey(int WindowId, string? Package, string? Class, string? UniqueId, string? ViewId,
        AndroidNodeBounds Bounds, string? Text, string? Description, string? Hint, bool Password)
    {
        public static NodeKey Of(AndroidAccessibilityNode node) => new(node.WindowId, node.PackageName, node.ClassName,
            node.UniqueId, node.ViewId, node.Bounds, node.Password ? null : node.Text,
            node.Password ? null : node.ContentDescription, node.Password ? null : node.Hint, node.Password);
    }
}

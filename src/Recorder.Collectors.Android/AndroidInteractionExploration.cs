using System.Diagnostics;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

public sealed class AndroidInteractionSnapshot
{
    internal AndroidInteractionSnapshot(AndroidLiveTreeSnapshot tree, AndroidLiveWindowSnapshot app,
        AndroidTalkBackScope scope, AndroidLivePreviewSnapshot preview, AndroidDisplayState? display,
        AndroidTalkBackInput input, long cursor)
    {
        Tree = tree;
        App = app;
        Scope = scope;
        Preview = preview;
        Display = display;
        Input = input;
        Cursor = cursor;
    }

    public AndroidLiveTreeSnapshot Tree { get; }
    public AndroidLiveWindowSnapshot App { get; }
    public AndroidLivePreviewFrame? Frame => Preview.Frame;
    public string PackageName => Scope.PackageName;
    public bool HasScreenCoordinates => Frame is { PhoneNanoseconds: > 0 } frame &&
        Display is { Rotation: 0 or 90 or 180 or 270, ShownWidth: > 0, ShownHeight: > 0 } display &&
        (frame.Width, frame.Height) == (display.ShownWidth, display.ShownHeight) &&
        !(App.Window.TransitionTimeMilliseconds is > 0 and var changed && frame.PhoneNanoseconds / 1_000_000 < changed) &&
        !(Tree.Index != App.Items?.SnapshotIndex && Tree.EarlierUpdateMissing);
    public IReadOnlyList<AndroidNodeBounds> Occluders => Tree.Windows
        .Where(entry => entry.Window.DisplayId == 0 && entry.Window.Layer > App.Window.Layer)
        .Select(entry => entry.Window.Bounds).ToArray();
    internal AndroidTalkBackScope Scope { get; }
    internal AndroidLivePreviewSnapshot Preview { get; }
    internal AndroidDisplayState? Display { get; }
    internal AndroidTalkBackInput Input { get; }
    internal long Cursor { get; }
}

public sealed record AndroidInteractionTapResult(string Evidence, long? ClickEvent = null, ulong? AfterFrame = null);

/// <summary>Reads a live viewport independently of TalkBack and optionally tests one confirmed point.</summary>
public sealed class AndroidInteractionExploration
{
    private readonly AndroidLivePreview _preview;
    private readonly AndroidLiveTree _tree;
    private readonly AndroidTalkBackSignals _signals;
    private readonly Func<bool> _ready;
    private readonly Func<AndroidTalkBackInput> _input;
    private readonly Func<long, AndroidDisplayState?> _displayAt;
    private readonly Func<AndroidTalkBackScope, AndroidInteractionPoint, CancellationToken, Task<CompanionNavigationReply>> _dispatch;
    private readonly Func<CancellationToken, Task> _settle;
    private int _probing;

    public AndroidInteractionExploration(AndroidLivePreview preview, AndroidLiveTree tree,
        AndroidAccessibilityCollector events, AndroidTouchCollector touch, AndroidCompanionCollector companion)
        : this(preview, tree, events.LiveSignals,
            () => events.ExplorationEventsAvailable && touch.ExplorationInputAvailable,
            () => touch.ExplorationInput, companion.DisplayAt, companion.ProbeInteractionAsync,
            cancellation => Task.Delay(1100, cancellation)) { }

    internal AndroidInteractionExploration(AndroidLivePreview preview, AndroidLiveTree tree, AndroidTalkBackSignals signals,
        Func<bool> ready, Func<AndroidTalkBackInput> input, Func<long, AndroidDisplayState?> displayAt,
        Func<AndroidTalkBackScope, AndroidInteractionPoint, CancellationToken, Task<CompanionNavigationReply>> dispatch,
        Func<CancellationToken, Task> settle)
    {
        _preview = preview;
        _tree = tree;
        _signals = signals;
        _ready = ready;
        _input = input;
        _displayAt = displayAt;
        _dispatch = dispatch;
        _settle = settle;
    }

    public AndroidInteractionSnapshot? Capture()
    {
        var input = _input();
        var cursor = _signals.Cursor;
        var current = _tree.Current;
        var app = AndroidTalkBackScope.ActiveApp(current);
        if (current.State != AndroidLiveTreeState.Observed ||
            app?.Items?.Nodes.FirstOrDefault()?.PackageName is not { Length: > 0 } package)
        {
            return null;
        }
        var picture = _preview.Current;
        var display = picture.Frame?.PhoneNanoseconds is { } time ? _displayAt(time) : null;
        return new(current, app, new(app.Window, package), picture, display, input, cursor);
    }

    public string? TapProblem(AndroidInteractionSnapshot snapshot, AndroidNodeBounds box)
    {
        if (!snapshot.HasScreenCoordinates || snapshot.Frame is not { } frame)
        {
            return "The picture could not be mapped to phone coordinates. Refresh on a settled screen.";
        }
        if (!_ready())
        {
            return "Live accessibility events and physical touch monitoring are required to send a test tap.";
        }
        var current = Capture();
        var input = _input();
        if (current?.App.Items?.SnapshotIndex != snapshot.App.Items?.SnapshotIndex ||
            current?.Tree.State != AndroidLiveTreeState.Observed || current.Tree.EarlierUpdateMissing ||
            !SameWindow(snapshot, current.App) || current.Preview.StreamSession != snapshot.Preview.StreamSession ||
            current.Frame?.Sequence != frame.Sequence ||
            input.Version != snapshot.Input.Version || input.TouchDown ||
            (input.LastAt > 0 && Stopwatch.GetElapsedTime(input.LastAt) < TimeSpan.FromMilliseconds(400)))
        {
            return "The picture, active window, tree or physical input changed. Refresh the scan before testing.";
        }
        var changes = _signals.After(snapshot.Cursor);
        if (changes.Gap || changes.Events.Any(entry => entry is null ||
            entry.EventTypeName is AndroidAccessibilityRecords.WindowStateChangedName or
                AndroidAccessibilityRecords.WindowsChangedName or "TYPE_VIEW_SCROLLED" or "TYPE_VIEW_CLICKED" or
                "TYPE_VIEW_LONG_CLICKED" or "TYPE_VIEW_TEXT_CHANGED"))
        {
            return "The screen changed or events are missing. Refresh the scan before testing.";
        }
        if (!AndroidInteractionInventory.Contains(new(0, 0, frame.Width, frame.Height), box))
        {
            return "The selected bounds are not fully inside the recorded picture.";
        }
        var (x, y) = Center(box);
        var pointBox = new AndroidNodeBounds(x - 1, y - 1, x + 2, y + 2);
        if (x < 2 || y < 2 || x >= frame.Width - 2 || y >= frame.Height - 2 ||
            !AndroidInteractionInventory.Contains(current.App.Window.Bounds, pointBox) ||
            current.Occluders.Any(overlay => AndroidInteractionInventory.Intersection(overlay, pointBox) is not null))
        {
            return "The test point is outside the app or covered by another window.";
        }
        return null;
    }

    public async Task<AndroidInteractionTapResult> TapAsync(AndroidInteractionSnapshot snapshot, AndroidNodeBounds box,
        int candidateNumber, Func<string, bool> recordMarker, CancellationToken cancellation)
    {
        if (Interlocked.Exchange(ref _probing, 1) != 0)
        {
            return new("A test tap is already in progress.");
        }
        var id = Guid.NewGuid().ToString("N")[..8];
        var began = false;
        AndroidInteractionTapResult result = new("Observation ended before a result could be verified.");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (TapProblem(snapshot, box) is { } problem)
            {
                return result = new(problem);
            }
            var (x, y) = Center(box);
            var before = snapshot.Frame!;
            began = recordMarker($"Recorder interaction probe {id}: started; candidate {candidateNumber}, " +
                $"window {snapshot.App.Window.Id}, frame {before.Sequence}, point ({x}, {y}).");
            if (!began)
            {
                return result = new("The recording no longer accepts annotations. No tap was sent.");
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var reply = await _dispatch(snapshot.Scope,
                new(x, y, before.Width, before.Height, snapshot.Display!.Rotation), timeout.Token).ConfigureAwait(false);
            if (reply.Status != "dispatched" || reply.Started <= 0)
            {
                return result = new(reply.Status switch
                {
                    "direct-touch-unavailable" => "Tap refused. Turn off TalkBack touch exploration and magnification for direct touch.",
                    "scope-changed" => "Tap refused. The phone could not verify the active window, display or test point.",
                    "gesture-cancelled" => "Android cancelled the touch. Activation is unknown.",
                    _ => $"No completed tap was verified ({reply.Status}). Activation is unknown."
                });
            }
            await _settle(cancellation).ConfigureAwait(false);
            var afterTree = _tree.Current;
            var after = _preview.Current;
            var batch = _signals.After(snapshot.Cursor);
            if (!_ready() || _input().Version != snapshot.Input.Version || batch.Gap ||
                batch.Events.Any(change => change is null))
            {
                return result = new("Android completed the tap, but other touch input or missing evidence makes the response uncertain.");
            }
            var started = reply.Started;
            var clicks = batch.Events.OfType<AndroidAccessibilityEvent>()
                .Where(change => MatchesClick(change, snapshot.Scope, started, x, y)).Take(2).ToArray();
            if (clicks.Length == 1)
            {
                return result = new($"Android reported a click with source bounds covering the point (event {clicks[0].Index}).",
                    clicks[0].Index);
            }
            if (clicks.Length > 1)
            {
                return result = new("Multiple click events followed the tap. The affected target is uncertain.");
            }
            if (afterTree.State != AndroidLiveTreeState.Observed ||
                !SameWindow(snapshot, AndroidTalkBackScope.ActiveApp(afterTree)) ||
                batch.Events.OfType<AndroidAccessibilityEvent>().Any(change =>
                    (change.ReceivedPhoneNanoseconds >= started && change.EventTypeName is
                        AndroidAccessibilityRecords.WindowsChangedName or AndroidAccessibilityRecords.WindowStateChangedName
                        or "TYPE_VIEW_SCROLLED")))
            {
                return result = new("The window or viewport changed after the tap. No click source was matched to the point.");
            }
            if (after.StreamSession != snapshot.Preview.StreamSession || after.Frame is not { } next ||
                next.Width != before.Width || next.Height != before.Height ||
                (next.Sequence > before.Sequence && (next.PhoneNanoseconds is not { } time ||
                    _displayAt(time)?.Rotation != snapshot.Display!.Rotation)))
            {
                return result = new("Android completed the tap, but comparable screen evidence is unavailable.");
            }
            // Identical frames are deduplicated; the settled result may first appear just after the 55 ms touch.
            if (next.Sequence > before.Sequence && next.PhoneNanoseconds >= started + 55_000_000 &&
                PixelsChanged(before, next, box))
            {
                return result = new($"Pixels at the test target changed after the tap (frame {next.Sequence}); " +
                    "no Android click source was matched. Review the recording to verify what changed.", AfterFrame: next.Sequence);
            }
            return result = new("No target response was corroborated by a click event or a comparable later picture. " +
                "This does not show that the target is inactive.");
        }
        catch (Exception exception) when (exception is OperationCanceledException or CompanionUnavailableException or ObjectDisposedException)
        {
            return result = new(began ? "Observation stopped or timed out; a tap might have been sent. Activation is unknown."
                : "Observation stopped before a tap was requested.");
        }
        finally
        {
            if (began)
            {
                recordMarker($"Recorder interaction probe {id}: ended. {result.Evidence}");
            }
            Volatile.Write(ref _probing, 0);
        }
    }

    private static bool SameWindow(AndroidInteractionSnapshot before, AndroidLiveWindowSnapshot? after) =>
        after?.Window is { } window && before.App.Window.Id == window.Id &&
        before.App.Window.Bounds == window.Bounds && before.App.Window.Layer == window.Layer &&
        before.App.Window.TransitionTimeMilliseconds == window.TransitionTimeMilliseconds &&
        string.Equals(before.PackageName, after.Items?.Nodes.FirstOrDefault()?.PackageName, StringComparison.Ordinal);

    public static (int X, int Y) Center(AndroidNodeBounds bounds) =>
        (bounds.Left + (bounds.Right - bounds.Left) / 2, bounds.Top + (bounds.Bottom - bounds.Top) / 2);

    internal static bool MatchesClick(AndroidAccessibilityEvent change, AndroidTalkBackScope scope, long started, int x, int y) =>
        change.EventTypeName == "TYPE_VIEW_CLICKED" && change.WindowId == scope.Window.Id && change.DisplayId is null or 0 &&
        string.Equals(change.PackageName, scope.PackageName, StringComparison.Ordinal) &&
        change.ReceivedPhoneNanoseconds >= started && change.ReceivedPhoneNanoseconds - started <= 1_700_000_000 &&
        change.EventTimeMilliseconds >= (started - 500_000) / 1_000_000 &&
        change.Source is { } source && source.WindowId == scope.Window.Id &&
        string.Equals(source.PackageName, scope.PackageName, StringComparison.Ordinal) &&
        AndroidInteractionInventory.Contains(source.Bounds, new(x, y, x + 1, y + 1));

    internal static bool PixelsChanged(AndroidLivePreviewFrame before, AndroidLivePreviewFrame after, AndroidNodeBounds box)
    {
        var (x, y) = Center(box);
        var region = AndroidInteractionInventory.Intersection(box, new(x - 72, y - 40, x + 73, y + 41));
        region = region is null ? null : AndroidInteractionInventory.Intersection(region, new(0, 0, before.Width, before.Height));
        if (region is null || (before.Width, before.Height) != (after.Width, after.Height) ||
            before.Rgb.Length != (long)before.Width * before.Height * 3 || after.Rgb.Length != before.Rgb.Length)
        {
            return false;
        }
        int samples = 0, changed = 0;
        var first = before.Rgb.Span;
        var second = after.Rgb.Span;
        for (var row = region.Top; row < region.Bottom; row += 2)
        {
            for (var col = region.Left; col < region.Right; col += 2)
            {
                var offset = (row * before.Width + col) * 3;
                samples++;
                if (Math.Abs(first[offset] - second[offset]) + Math.Abs(first[offset + 1] - second[offset + 1]) +
                    Math.Abs(first[offset + 2] - second[offset + 2]) >= 30)
                {
                    changed++;
                }
            }
        }
        return changed >= 8 && changed * 100 >= samples * 18;
    }
}

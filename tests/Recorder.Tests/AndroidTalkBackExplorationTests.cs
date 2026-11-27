using System.Text.Json;
using Recorder.Collectors.Android;
using Recorder.Contracts;

namespace Recorder.Tests;

public sealed class AndroidTalkBackExplorationTests
{
    [Fact]
    public async Task Sweep_UsesBothDirections_KeepsOrderAndDoesNotFailUnreachedNodes()
    {
        var rig = new Rig(start: 2);
        var targets = new Queue<int?>([3, null, 2, 1, null]);
        var result = await rig.Run((_, _) =>
        {
            if (targets.Dequeue() is { } target)
            {
                rig.Focus(target);
            }
            return Rig.Dispatched;
        }, cancellation: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { true, true, false, false, false }, rig.Directions);
        Assert.Equal(new[] { "Last (Button)", "Summary (LinearLayout)", "First (Button)" },
            result.Stops.Select(stop => stop.Item));
        Assert.Equal(new long[] { 2, 3, 4 }, result.Stops.Select(stop => stop.SnapshotIndex));
        Assert.True(result.BothDirectionsTried);
        Assert.Contains(result.Candidates, item => item.Item == "Not visited (Button)" &&
            item.Observation == "Exposed but not reached in this pass");
        Assert.Contains("boundary was not proven", result.Reason);
        Assert.Equal(2, rig.Markers.Count);
        Assert.Contains("started (automated navigation)", rig.Markers[0]);
        Assert.Contains("ended; 3 matched focus stops", rig.Markers[1]);
    }

    [Fact]
    public async Task VerificationSendsOneNextSwipeAndDoesNotClaimCoverage()
    {
        var rig = new Rig();
        var result = await rig.Run((_, _) =>
        {
            rig.Focus(2);
            return Rig.Dispatched;
        }, single: true, cancellation: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { true }, rig.Directions);
        Assert.Equal("Next", Assert.Single(result.Stops).Direction);
        Assert.Equal(1, result.GestureCount);
        Assert.Contains("physical right swipe", result.Reason);
        Assert.Contains(result.Candidates, item => item.Observation == "Starting focus only");
        Assert.DoesNotContain(result.Candidates, item => item.Observation.StartsWith("Exposed but not reached"));
    }

    [Fact]
    public async Task InteractionReportReusesOnlyReachedNodesOnTheMatchingViewport()
    {
        var rig = new Rig();
        var result = await rig.Run((_, _) =>
        {
            rig.Focus(3);
            return Rig.Dispatched;
        }, single: true, cancellation: TestContext.Current.CancellationToken);
        var observed = Assert.Single(result.Stops);
        var match = Assert.Single(rig.MatchedStops());
        Assert.Equal(13, match.Key);
        Assert.Equal(observed, match.Value);

        rig.Input = new(1, 0, false);
        Assert.Empty(rig.MatchedStops());
        rig.Input = default;
        rig.Publish(3, changed: true);
        Assert.Empty(rig.MatchedStops());
    }

    [Theory]
    [InlineData("scroll", "scrolled")]
    [InlineData("window", "window or app interaction")]
    [InlineData("click", "window or app interaction")]
    [InlineData("viewport", "visible tree changed")]
    [InlineData("rotation", "display changed")]
    [InlineData("physical", "Physical phone input")]
    [InlineData("events", "events were unavailable")]
    [InlineData("loss", "feed became unavailable")]
    [InlineData("stale", "could not be attributed")]
    [InlineData("different-window", "outside the starting app window")]
    [InlineData("multiple", "More than one focus event")]
    public async Task ScopeOrEvidenceFailureStopsRatherThanSendingAnotherSwipe(string change, string reason)
    {
        var rig = new Rig();
        var result = await rig.Run((_, _) =>
        {
            switch (change)
            {
                case "scroll": rig.Event("TYPE_VIEW_SCROLLED"); break;
                case "window": rig.Event(AndroidAccessibilityRecords.WindowStateChangedName); break;
                case "click": rig.Event("TYPE_VIEW_CLICKED"); break;
                case "viewport": rig.Publish(2, changed: true); break;
                case "rotation":
                    rig.Preview.BeginSession(2);
                    rig.Preview.Publish(2, 1, 800, 500, [0, 0, 0]);
                    break;
                case "physical": rig.Input = new(1, 0, true); break;
                case "events": rig.Signals.Observe(null); break;
                case "loss": rig.Available = false; break;
                case "stale": rig.Event(AndroidTalkBackSignals.Focus, Rig.Node(2), time: 1); break;
                case "different-window": rig.Event(AndroidTalkBackSignals.Focus, Rig.Node(2), window: 8); break;
                case "multiple": rig.Focus(2); rig.Event(AndroidTalkBackSignals.Focus, Rig.Node(3)); break;
            }
            return Rig.Dispatched;
        }, cancellation: TestContext.Current.CancellationToken);

        Assert.Single(rig.Directions);
        Assert.Empty(result.Stops);
        Assert.Contains(reason, result.Reason);
        Assert.DoesNotContain(result.Candidates, item => item.Observation == "Reached");
    }

    [Theory]
    [InlineData("same-bounds-other-name")]
    [InlineData("no-source")]
    [InlineData("old-snapshot")]
    [InlineData("identical-nodes")]
    public async Task EventAndFreshSnapshotMustAgreeOnOneDistinctNode(string change)
    {
        var rig = new Rig(duplicates: change == "identical-nodes");
        var result = await rig.Run((_, _) =>
        {
            rig.Event(AndroidTalkBackSignals.Focus, change switch
            {
                "no-source" => null,
                "same-bounds-other-name" => Rig.Node(2) with { ContentDescription = "Different item" },
                _ => Rig.Node(2)
            });
            rig.Publish(2, old: change == "old-snapshot");
            return Rig.Dispatched;
        }, cancellation: TestContext.Current.CancellationToken);

        Assert.Single(rig.Directions);
        Assert.Empty(result.Stops);
        Assert.Contains("could not be matched", result.Reason);
        if (change == "identical-nodes")
        {
            Assert.Equal(2, result.Candidates.Count(item => item.Observation == "Unknown: indistinguishable tree nodes"));
        }
    }

    [Fact]
    public async Task CancellationDuringSwipePreservesEndMarkerAndNeverSendsAnotherSwipe()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var rig = new Rig();
        var result = await rig.Run((_, _) =>
        {
            stop.Cancel();
            return Rig.Dispatched;
        }, cancellation: stop.Token);
        Assert.Single(rig.Directions);
        Assert.Equal(1, result.GestureCount);
        Assert.Empty(result.Stops);
        Assert.Contains("stopped by the recorder operator", result.Reason);
        Assert.Equal(2, rig.Markers.Count);
    }

    [Fact]
    public async Task ScrollKeepsPreviouslyMatchedStopsAndMarksOtherCandidatesUnknown()
    {
        var rig = new Rig();
        var result = await rig.Run((_, _) =>
        {
            if (rig.Directions.Count == 1)
            {
                rig.Focus(2);
            }
            else
            {
                rig.Event("TYPE_VIEW_SCROLLED");
            }
            return Rig.Dispatched;
        }, cancellation: TestContext.Current.CancellationToken);

        Assert.Equal(2, rig.Directions.Count);
        Assert.Contains("scrolled", result.Reason);
        Assert.Equal("Summary (LinearLayout)", Assert.Single(result.Stops).Item);
        Assert.Contains(result.Candidates, item => item.Item == "Last (Button)" &&
            item.Observation == "Unknown: not observed in this partial pass");
    }

    [Fact]
    public async Task MarkerUnavailableDoesNotDrivePhone()
    {
        var rig = new Rig();
        var result = await rig.Run((_, _) => throw new InvalidOperationException(), acceptMarker: false,
            cancellation: TestContext.Current.CancellationToken);
        Assert.Empty(rig.Directions);
        Assert.Contains("No swipes were sent", result.Reason);
    }

    [Fact]
    public async Task TalkBackRefusedOrExistingPhysicalTouchDoesNotRepeat()
    {
        var rig = new Rig();
        var result = await rig.Run((_, _) => new(1, "talkback-unavailable", 0),
            cancellation: TestContext.Current.CancellationToken);
        Assert.Single(rig.Directions);
        Assert.Equal(0, result.GestureCount);
        Assert.Contains("TalkBack with touch exploration must be on", result.Reason);

        rig.Input = new(1, 0, true);
        result = await rig.Run((_, _) => throw new InvalidOperationException(),
            cancellation: TestContext.Current.CancellationToken);
        Assert.Single(rig.Directions);
        Assert.Contains("Lift your finger", result.Reason);
    }

    [Fact]
    public async Task PasswordTextIsNotDisplayedInCandidatesOrStops()
    {
        var rig = new Rig(password: true);
        var result = await rig.Run((_, _) =>
        {
            rig.Focus(2);
            return Rig.Dispatched;
        }, single: true, cancellation: TestContext.Current.CancellationToken);
        Assert.Equal("Password item (LinearLayout)", Assert.Single(result.Stops).Item);
        Assert.Contains(result.Candidates, item => item.Observation == "Reached" &&
            item.Item == "Password item (LinearLayout)");
        Assert.DoesNotContain("fixture-value", JsonSerializer.Serialize(result));
    }

    [Fact]
    public void ProtocolAsksOnlyForDirectionAndExpectedScopeAndParsesIndependentReply()
    {
        var scope = new AndroidTalkBackScope(Rig.Window, Rig.Package);
        using var request = JsonDocument.Parse(CompanionProtocol.TalkBackNavigationRequest(7, false, scope));
        var root = request.RootElement;
        Assert.Equal("talkback-navigation", root.GetProperty("type").GetString());
        Assert.Equal("previous", root.GetProperty("direction").GetString());
        Assert.Equal(7, root.GetProperty("id").GetInt64());
        Assert.Equal(11, root.GetProperty("windowId").GetInt32());
        Assert.Equal(Rig.Package, root.GetProperty("packageName").GetString());
        Assert.Equal(500, root.GetProperty("bounds").GetProperty("right").GetInt32());
        Assert.Equal(new CompanionNavigationReply(7, "dispatched", 100),
            CompanionProtocol.Parse("""{"type":"talkback-navigation","id":7,"status":"dispatched","started":100}"""));
    }

    private sealed class Rig
    {
        internal const string Package = "org.example.synthetic";
        internal static readonly AndroidAccessibilityWindow Window = new()
        {
            Id = 11, DisplayId = 0, Type = 1, TypeName = "TYPE_APPLICATION",
            Active = true, AccessibilityFocused = true, Bounds = new(0, 0, 500, 800)
        };
        private const long SwipeStart = 2_000_000_000;
        internal static readonly CompanionNavigationReply Dispatched = new(1, "dispatched", SwipeStart);
        internal readonly AndroidLivePreview Preview = new();
        internal readonly AndroidTalkBackSignals Signals = new();
        internal readonly List<string> Markers = [];
        internal readonly List<bool> Directions = [];
        internal AndroidTalkBackInput Input;
        internal bool Available = true;
        private readonly AndroidLiveTree _tree = new();
        private readonly bool _duplicates;
        private readonly bool _password;
        private long _snapshot;
        private long _event;
        private AndroidTalkBackExploration? _exploration;

        internal Rig(int start = 1, bool duplicates = false, bool password = false)
        {
            _duplicates = duplicates;
            _password = password;
            Preview.BeginSession(1);
            Preview.Publish(1, 1, 500, 800, [0, 0, 0]);
            Publish(start);
        }

        internal Task<AndroidTalkBackExplorationResult> Run(Func<bool, AndroidTalkBackScope, CompanionNavigationReply> navigate,
            bool single = false, bool acceptMarker = true, CancellationToken cancellation = default)
        {
            _exploration = new AndroidTalkBackExploration(Preview, _tree, Signals, () => Available, () => Input,
                (next, scope, _) =>
                {
                    Directions.Add(next);
                    return Task.FromResult(navigate(next, scope));
                }, TimeProvider.System);
            return _exploration.RunAsync(marker =>
            {
                Markers.Add(marker);
                return acceptMarker;
            }, single, cancellation);
        }

        internal IReadOnlyDictionary<int, AndroidTalkBackStop> MatchedStops() =>
            _exploration!.MatchedStops(_tree.Current, Preview.Current, Input.Version);

        internal void Focus(int index)
        {
            Event(AndroidTalkBackSignals.Focus, ConfiguredNode(index));
            Publish(index);
        }

        internal void Event(string name, AndroidAccessibilityNode? source = null, int window = 11, long time = 2_005) =>
            Signals.Observe(new AndroidAccessibilityEvent
            {
                Index = ++_event, EventType = 1, EventTypeName = name, EventTimeMilliseconds = time,
                ReceivedPhoneNanoseconds = 2_006_000_000, HandledPhoneNanoseconds = 2_007_000_000,
                WindowId = window, DisplayId = 0, PackageName = Package, Source = source
            });

        internal void Publish(int focus, bool old = false, bool changed = false)
        {
            var items = Enumerable.Range(0, 5).Select(ConfiguredNode).Select(node => node with
            {
                // Change snapshot-local node numbers on every update. They are not a matching key.
                Index = node.Index + (_snapshot == 0 ? 0 : 10),
                AccessibilityFocused = node.Index == focus
            }).ToList();
            if (changed)
            {
                items[3] = items[3] with { Bounds = new(0, 0, 200, 700) };
            }
            if (_duplicates)
            {
                items.Add(items[2] with { Index = 30, AccessibilityFocused = false });
            }
            _tree.Publish(new AndroidAccessibilityTree
            {
                Index = ++_snapshot, EveryWindow = true,
                StartedPhoneNanoseconds = old ? 1_000_000_000 : 2_008_000_000,
                FinishedPhoneNanoseconds = 2_009_000_000,
                Windows = [Window with { Items = items }]
            });
        }

        private AndroidTreeNode ConfiguredNode(int index) =>
            _password && index == 2 ? Node(index) with { Password = true, Text = "fixture-value" } : Node(index);

        internal static AndroidTreeNode Node(int index) => new()
        {
            Index = index, Depth = index == 0 ? 0 : 1, WindowId = 11, VisibleToUser = true,
            PackageName = Package, Checked = "unchecked", Bounds = new(12, index * 50, 200, index * 50 + 45),
            ClassName = index is 0 or 2 ? "android.widget.LinearLayout" : "android.widget.Button",
            Text = index switch { 1 => "First", 2 => "Summary", 3 => "Last", 4 => "Not visited", _ => null },
            Clickable = index is 1 or 3 or 4, ScreenReaderFocusable = index == 2
        };
    }
}

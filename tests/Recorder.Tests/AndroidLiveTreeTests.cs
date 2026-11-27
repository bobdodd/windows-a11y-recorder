using Recorder.Collectors.Android;
using Recorder.Contracts;

namespace Recorder.Tests;

public sealed class AndroidLiveTreeTests
{
    [Fact]
    public void PartialUpdate_KeepsNodesForWindowsStillReported_WithTheirOwnProvenance()
    {
        var feed = new AndroidLiveTree();
        var original = Window(1, nodes: [Node("Original")]) with { Truncated = true, MissingChildren = 3 };
        feed.Publish(Snapshot(1, true, original, Window(2, nodes: [Node("Old")]), Window(3)));
        var originalItems = feed.Current.Windows[0].Items;

        feed.Publish(Snapshot(2, false, Window(1) with { Active = true }, Window(2, nodes: [Node("New")])));

        var latest = feed.Current;
        Assert.Equal(AndroidLiveTreeState.Observed, latest.State);
        Assert.Equal(2, latest.Index);
        Assert.Equal([1, 2], latest.Windows.Select(window => window.Window.Id));
        Assert.True(latest.Windows[0].Window.Active);
        var retained = Assert.IsType<AndroidLiveTreeItems>(latest.Windows[0].Items);
        Assert.Same(originalItems, retained);
        Assert.Equal((1, 100, true, 3), (retained.SnapshotIndex, retained.ReceivedAtNanoseconds, retained.Truncated, retained.MissingChildren));
        var fresh = Assert.IsType<AndroidLiveTreeItems>(latest.Windows[1].Items);
        Assert.Equal((2, 200), (fresh.SnapshotIndex, fresh.ReceivedAtNanoseconds));
        Assert.Equal("New", Assert.Single(fresh.Nodes).Text);
        Assert.All(latest.Windows, window => Assert.Null(window.Window.Items));

        feed.Publish(Snapshot(1, true, Window(3)));
        Assert.Same(latest, feed.Current);
    }

    [Fact]
    public void ExplicitEmptyList_ReplacesOldNodes_AndFullSnapshotDoesNotBorrowMissingLists()
    {
        var feed = new AndroidLiveTree();
        feed.Publish(Snapshot(1, true, Window(1, nodes: [Node("Old")]), Window(2, nodes: [Node("Old")])));
        feed.Publish(Snapshot(2, true, Window(1, nodes: []), Window(2)));

        var current = feed.Current;
        var empty = Assert.IsType<AndroidLiveTreeItems>(current.Windows[0].Items);
        Assert.Empty(empty.Nodes);
        Assert.Equal(2, empty.SnapshotIndex);
        Assert.Null(current.Windows[1].Items);
    }

    [Fact]
    public void PartialUpdate_DoesNotCarryNodesAcrossDisplayTypeOrTransitionChanges()
    {
        var feed = new AndroidLiveTree();
        var nodes = new[] { Node("Previous") };
        feed.Publish(Snapshot(1, true,
            Window(1, nodes: nodes), Window(2, nodes: nodes), Window(3, nodes: nodes)));
        feed.Publish(Snapshot(2, false,
            Window(1) with { DisplayId = 4 },
            Window(2) with { Type = 4 },
            Window(3) with { TransitionTimeMilliseconds = 12 }));

        Assert.All(feed.Current.Windows, window => Assert.Null(window.Items));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GapOrUnreadableUpdate_InvalidatesUnretakenNodes(bool unreadable)
    {
        var feed = new AndroidLiveTree();
        feed.Publish(Snapshot(1, true, Window(1, nodes: [Node("Before the gap")])));
        if (unreadable)
        {
            feed.Unreadable();
            Assert.Equal(AndroidLiveTreeState.Unreadable, feed.Current.State);
            Assert.Single(feed.Current.Windows);
        }

        feed.Publish(Snapshot(unreadable ? 2 : 3, false, Window(1), Window(2, nodes: [Node("Observed")])));
        Assert.True(feed.Current.EarlierUpdateMissing);
        Assert.Null(feed.Current.Windows[0].Items);
        Assert.Equal("Observed", Assert.Single(feed.Current.Windows[1].Items!.Nodes).Text);
    }

    [Fact]
    public void Feed_TellsPartialAndClearsOnDisconnectAndStop()
    {
        var feed = new AndroidLiveTree();
        var partial = Snapshot(1, true, Window(1, nodes: [Node("Observed")])) with { Problem = "Window disappeared." };
        feed.Publish(partial);
        Assert.Equal(partial.Problem, feed.Current.Problem);

        feed.Unavailable("Companion disconnected.");
        var unavailable = feed.Current;
        feed.Publish(Snapshot(2, true, Window(2)));
        feed.Unreadable();
        Assert.Same(unavailable, feed.Current);
        Assert.Empty(unavailable.Windows);
        Assert.Equal(AndroidLiveTreeState.Unavailable, unavailable.State);

        feed.Stop();
        feed.Publish(Snapshot(3, true, Window(3)));
        Assert.Equal(AndroidLiveTreeState.Stopped, feed.Current.State);
        Assert.Empty(feed.Current.Windows);
    }

    [Fact]
    public void Focus_FollowsBothKindsIndependently_AndClearsAfterFocusLeaves()
    {
        var feed = new AndroidLiveTree();
        var textField = Node("Text field") with { Focused = true };
        var label = Node("Label") with { AccessibilityFocused = true, Index = 1 };
        var other = Node("Submit") with { Focusable = true, Selected = true, Index = 2 };
        feed.Publish(Snapshot(1, true, Window(1, [textField, label, other])));
        var focus = feed.Current.FocusFor(feed.Current.Windows[0]);
        Assert.False(focus.FromEarlierSnapshot);
        Assert.Equal(AndroidLiveFocusKind.Input, focus.ForNode(textField));
        Assert.Equal(AndroidLiveFocusKind.Accessibility, focus.ForNode(label));
        Assert.Equal(AndroidLiveFocusKind.None, focus.ForNode(other));

        feed.Publish(Snapshot(2, false, Window(1, [textField with { AccessibilityFocused = true }, label with { AccessibilityFocused = false }, other])));
        var current = feed.Current.Windows[0];
        focus = feed.Current.FocusFor(current);
        Assert.Equal(AndroidLiveFocusKind.Input | AndroidLiveFocusKind.Accessibility, focus.ForNode(current.Items!.Nodes[0]));
        Assert.Equal(AndroidLiveFocusKind.None, focus.ForNode(current.Items.Nodes[1]));

        feed.Publish(Snapshot(3, false, Window(1, [textField with { Focused = false }, label with { AccessibilityFocused = false }, other])));
        current = feed.Current.Windows[0];
        focus = feed.Current.FocusFor(current);
        Assert.All(current.Items!.Nodes, node => Assert.Equal(AndroidLiveFocusKind.None, focus.ForNode(node)));
    }

    [Fact]
    public void Focus_OnCarriedNodes_IsHistoricalAndOnlyMarkedForMatchingWindowFocus()
    {
        var feed = new AndroidLiveTree();
        var node = Node("Text field") with { Focused = true, AccessibilityFocused = true };
        feed.Publish(Snapshot(1, true, Window(1, [node]), Window(2)));

        feed.Publish(Snapshot(2, false, Window(1) with { Focused = true }, Window(2, [])));
        var focus = feed.Current.FocusFor(feed.Current.Windows[0]);
        Assert.True(focus.FromEarlierSnapshot);
        Assert.Equal(AndroidLiveFocusKind.Input, focus.ForNode(node));

        feed.Publish(Snapshot(3, false, Window(1), Window(2) with { Focused = true }));
        Assert.Equal(AndroidLiveFocusKind.None, feed.Current.FocusFor(feed.Current.Windows[0]).ForNode(node));

        feed.Publish(Snapshot(4, false, Window(1, [node])));
        feed.Unreadable();
        Assert.Equal(AndroidLiveFocusKind.None, feed.Current.FocusFor(feed.Current.Windows[0]).ForNode(node));
        feed.Unavailable("Companion disconnected.");
        Assert.Equal(AndroidLiveFocusKind.None, feed.Current.FocusFor(null).ForNode(node));
    }

    private static AndroidAccessibilityTree Snapshot(long index, bool everyWindow, params AndroidAccessibilityWindow[] windows) =>
        new()
        {
            Index = index, EveryWindow = everyWindow, Windows = windows, ReceivedAtNanoseconds = index * 100,
            StartedPhoneNanoseconds = 0, FinishedPhoneNanoseconds = 0
        };

    private static AndroidAccessibilityWindow Window(int id, IReadOnlyList<AndroidTreeNode>? nodes = null) =>
        new() { Id = id, Type = 1, TypeName = "TYPE_APPLICATION", Bounds = new(0, 0, 10, 10), Items = nodes };

    private static AndroidTreeNode Node(string text) =>
        new() { Index = 0, Checked = "unchecked", Bounds = new(0, 0, 10, 10), Text = text };
}

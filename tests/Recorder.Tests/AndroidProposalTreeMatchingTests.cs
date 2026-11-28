using Recorder.Collectors.Android;
using Recorder.Contracts;

namespace Recorder.Tests;

public sealed class AndroidProposalTreeMatchingTests
{
    private const long PictureTime = 2_000_000_000;
    private static readonly AndroidLivePreviewFrame Frame = new(3, 500, 1000, ReadOnlyMemory<byte>.Empty)
        { PhoneNanoseconds = PictureTime };
    private static readonly AndroidDisplayState Portrait = new("test", 0, 500, 1000) { ShownWidth = 500, ShownHeight = 1000 };

    [Fact]
    public void MapsNormalizedBoxesToPhonePixelsWithoutAddingOrDeduplicatingProposals()
    {
        var first = Node(4, "Open", 50, 200, 150, 300) with { Clickable = true, Enabled = false };
        var second = Node(5, "Heading", 200, 400, 350, 500);
        var unproposed = Node(6, "Save", 300, 600, 350, 700) with { Clickable = true };
        var a = new AndroidVisionProposal("Open", new(100, 200, 300, 300));
        var b = new AndroidVisionProposal("Heading", new(400, 400, 700, 500));
        var c = new AndroidVisionProposal("Drawn target", new(0, 800, 100, 900));
        var proposals = new[] { b, a, a, c };

        var report = Compare(Tree([first, second, unproposed]), proposals);

        Assert.Equal([b, a, a, c], proposals);
        Assert.Equal([AndroidProposalTreeMatchKind.Matched, AndroidProposalTreeMatchKind.Matched,
            AndroidProposalTreeMatchKind.Matched, AndroidProposalTreeMatchKind.NoMatch], report.Matches.Select(match => match.Kind));
        var heading = Assert.Single(report.Matches[0].Candidates);
        Assert.Equal("Heading", heading.Item.Label);
        Assert.Empty(heading.Item.Evidence);
        var open = Assert.Single(report.Matches[1].Candidates);
        Assert.Equal(first.Bounds, open.Bounds);
        Assert.Equal("Activate", open.Item.Evidence);
        Assert.False(open.Item.Enabled);
        Assert.Equal(open.Item, Assert.Single(report.Matches[2].Candidates).Item);
        Assert.Contains("3 matched; 0 ambiguous; 1 without a clear match", report.Summary);
    }

    [Fact]
    public void NestedSelectionTextDoesNotDisplaceTheNamedOuterControl()
    {
        var button = Node(1, "Open item", 50, 200, 150, 300) with { Clickable = true, ChildCount = 1 };
        var label = Node(2, "Item name", 72, 221, 130, 280) with { Parent = 1, Depth = 1, Actions = [new(131072, null)] };
        var report = Compare(Tree([button, label]), [new("Open item", new(100, 200, 300, 300))]);
        var candidate = Assert.Single(Assert.Single(report.Matches).Candidates);
        Assert.Equal("Open item", candidate.Item.Label);
        Assert.Equal("Activate", candidate.Item.Evidence);
    }

    [Fact]
    public void EqualGeometryWithNoDecisiveLabelIsAmbiguousEvenWhenOneNodeCanActivate()
    {
        var nodes = Enumerable.Range(1, 4).Select(i =>
            Node(i, $"Fixture {i}", 50, 200, 150, 300) with { Clickable = i == 2 }).ToArray();
        var report = Compare(Tree(nodes), [new("Open menu", new(100, 200, 300, 300))]);
        var match = Assert.Single(report.Matches);
        Assert.Equal(AndroidProposalTreeMatchKind.Ambiguous, match.Kind);
        Assert.Equal(4, match.PlausibleCount);
        Assert.Equal([1, 2, 3], match.Candidates.Select(entry => entry.Item.NodeIndex));
    }

    [Fact]
    public void SmallCenteredIconCanMatchItsDeclaredHitAreaButNotAFullScreenContainer()
    {
        var root = Node(1, "Application", 0, 0, 500, 1000) with { Clickable = true, ChildCount = 1 };
        var icon = Node(2, "More options", 40, 190, 160, 310) with { Parent = 1, Depth = 1, Clickable = true };
        var report = Compare(Tree([root, icon]),
            [new("Show menu", new(140, 220, 260, 280)), new("Other icon", new(500, 250, 550, 280))]);

        Assert.Equal(icon.Bounds, Assert.Single(report.Matches[0].Candidates).Bounds);
        Assert.Equal(AndroidProposalTreeMatchKind.NoMatch, report.Matches[1].Kind);
    }

    [Fact]
    public void InlineLinkUsesTheReportedPartAndDoesNotMatchWhereBoundsAreMissing()
    {
        var text = Node(1, "Fixture help and notes", 40, 190, 450, 315) with
        {
            Links = [
                new(8, 12, "clickable", null, "located", 4, [new(90, 220, 140, 245), new(45, 245, 75, 270)]),
                new(17, 22, "clickable", null, "not-reported", null, [])
            ]
        };
        var report = Compare(Tree([text]), [new("Help", new(90, 245, 150, 270)), new("Notes", new(500, 250, 690, 290))]);
        var match = Assert.Single(report.Matches[0].Candidates);
        Assert.Equal("Inline link", match.Item.Role);
        Assert.Equal("help", match.Item.Label);
        Assert.Equal(text.Links[0].Boxes[1], match.Bounds);
        Assert.Equal(AndroidProposalTreeMatchKind.NoMatch, report.Matches[1].Kind);
    }

    [Fact]
    public void UsesOnlyVisibleNodesInTheActiveAppAndReportsPartialCoverage()
    {
        var hidden = Node(1, "Open", 50, 200, 150, 300) with { VisibleToUser = false, Clickable = true };
        var otherWindow = new AndroidLiveWindowSnapshot(
            new() { Id = 22, Type = 3, TypeName = "TYPE_SYSTEM", Bounds = new(0, 0, 500, 1000) },
            new(6, 0, [hidden with { VisibleToUser = true }], false, 0, PictureTime));
        var tree = Tree([hidden], truncated: true, index: 6, nodeIndex: 5);
        var report = Compare(tree with { Windows = [.. tree.Windows, otherWindow] }, [new("Open", new(100, 200, 300, 300))]);

        Assert.Equal(AndroidProposalTreeMatchKind.NoMatch, Assert.Single(report.Matches).Kind);
        Assert.Contains("node snapshot 5", report.Summary);
        Assert.Contains("carried forward these nodes", report.Summary);
        Assert.Contains("incomplete evidence", report.Summary);
    }

    [Fact]
    public void DiagnosticDoesNotSuppressTreeTextOrDeclarationsForTestData()
    {
        var node = Node(1, "Fixture value", 50, 200, 150, 300) with { Password = true, Editable = true };
        var tree = Tree([node]);
        var report = Compare(tree, [new("Fixture value", new(100, 200, 300, 300))]);

        Assert.Equal("Fixture value", Assert.Single(Assert.Single(report.Matches).Candidates).Item.Label);
        Assert.Equal("Password item", Assert.Single(AndroidInteractionInventory.Declared(tree.Windows[0])).Label);
    }

    [Fact]
    public void CapturedTreeDoesNotChangeWhenTheFeedMovesToAnotherScreen()
    {
        var feed = new AndroidLiveTree();
        var before = Node(1, "First screen", 50, 200, 150, 300);
        var after = before with { Text = "Second screen" };
        var window = Tree([before]).Windows[0].Window;
        feed.Publish(new() { Index = 1, EveryWindow = true, StartedPhoneNanoseconds = PictureTime,
            FinishedPhoneNanoseconds = PictureTime, Windows = [window with { Items = [before] }] });
        var captured = feed.Current;
        feed.Publish(new() { Index = 2, EveryWindow = true, StartedPhoneNanoseconds = PictureTime,
            FinishedPhoneNanoseconds = PictureTime, Windows = [window with { Items = [after] }] });

        var match = Assert.Single(Compare(captured, [new("First screen", new(100, 200, 300, 300))]).Matches);
        Assert.Equal("First screen", Assert.Single(match.Candidates).Item.Label);
    }

    [Theory]
    [InlineData(AndroidLiveTreeState.Waiting)]
    [InlineData(AndroidLiveTreeState.Unreadable)]
    [InlineData(AndroidLiveTreeState.Unavailable)]
    [InlineData(AndroidLiveTreeState.Stopped)]
    public void UnreadableOrMissingTreeIsUnavailableAndNotANegativeMatch(AndroidLiveTreeState state)
    {
        var report = Compare(Tree([]) with { State = state }, [new("Open", new(100, 200, 300, 300))]);
        Assert.Empty(report.Matches);
        Assert.Contains("matching unavailable", report.Summary);
    }

    [Fact]
    public void EmptyModelAnswerDoesNotFallBackToTreeTargets()
    {
        var report = Compare(Tree([Node(1, "Open", 50, 200, 150, 300) with { Clickable = true }]), []);
        Assert.Empty(report.Matches);
        Assert.Contains("model proposed no targets", report.Summary);
    }

    [Fact]
    public void RejectsDisplayRotationMismatchEvenIfDimensionsStillAgree()
    {
        var tree = Tree([Node(1, "Open", 50, 200, 150, 300)], phoneTime: PictureTime - 1);
        var display = new TestDisplay(time => time == PictureTime ? Portrait : Portrait with { Rotation = 180 });
        var report = AndroidProposalTreeMatching.Compare(Frame, tree, display, [new("Open", new(100, 200, 300, 300))],
            TestContext.Current.CancellationToken);
        Assert.Contains("could not be aligned", report.Summary);
        Assert.Empty(report.Matches);
    }

    [Fact]
    public void RejectsTreeSubstantiallyNewerThanImage()
    {
        var tree = Tree([Node(1, "Open", 50, 200, 150, 300)], phoneTime: PictureTime + 600_000_000);
        var report = Compare(tree, [new("Open", new(100, 200, 300, 300))]);
        Assert.Contains("too far after", report.Summary);
        Assert.Empty(report.Matches);
    }

    [Fact]
    public void DuplicateNodeIdentifiersLeaveMatchingUnavailable()
    {
        var node = Node(1, "Open", 50, 200, 150, 300);
        var report = Compare(Tree([node, node]), [new("Open", new(100, 200, 300, 300))]);
        Assert.Contains("duplicate node identifiers", report.Summary);
        Assert.Empty(report.Matches);
    }

    private static AndroidProposalTreeReport Compare(AndroidLiveTreeSnapshot tree, IReadOnlyList<AndroidVisionProposal> proposals) =>
        AndroidProposalTreeMatching.Compare(Frame, tree, new TestDisplay(_ => Portrait), proposals,
            TestContext.Current.CancellationToken);

    private static AndroidLiveTreeSnapshot Tree(IReadOnlyList<AndroidTreeNode> nodes, bool truncated = false,
        long index = 1, long nodeIndex = 1, long phoneTime = PictureTime) =>
        new(AndroidLiveTreeState.Observed, index, 0,
            [new(new() { Id = 11, Type = 1, TypeName = "TYPE_APPLICATION", Active = true, Bounds = new(0, 0, 500, 1000) },
                new(nodeIndex, 0, nodes, truncated, 0, phoneTime))], null, false);

    private static AndroidTreeNode Node(int index, string name, int left, int top, int right, int bottom) =>
        new() { Index = index, Bounds = new(left, top, right, bottom), Text = name, VisibleToUser = true, Enabled = true, Checked = "unchecked" };

    private sealed class TestDisplay(Func<long, AndroidDisplayState> stateAt) : IPhoneDisplay
    {
        public AndroidDisplayState? DisplayAt(long phoneNanoseconds) => stateAt(phoneNanoseconds);
    }
}

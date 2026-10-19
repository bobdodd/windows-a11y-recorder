using System.Text.Json;
using Recorder.Analysis;
using Recorder.Analysis.Summary;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>Builds pages for the summary's tests, node by node in tree order.</summary>
internal sealed class PageBuilder
{
    private readonly List<PageNode> _nodes = [];
    private long _next = 1;

    public long Document { get; }

    public PageBuilder()
    {
        Document = Add(null, "document", "", null, null, null, null);
    }

    public long Add(long? parent, string kind, string tag, Dictionary<string, string?>? attributes, string? role, string? name, string? text)
    {
        var id = _next++;
        _nodes.Add(new PageNode(id, parent, kind, tag, attributes ?? [], role, name, text, []));
        if (parent is { } parentId)
        {
            var index = _nodes.FindIndex(node => node.Id == parentId);
            _nodes[index] = _nodes[index] with { Children = [.. _nodes[index].Children, id] };
        }

        return id;
    }

    public long Element(long parent, string tag, string? role = null, string? name = null, params (string Name, string? Value)[] attributes) =>
        Add(parent, "element", tag, attributes.ToDictionary(pair => pair.Name, pair => pair.Value), role, name, null);

    public long Text(long parent, string text, string? role = null) => Add(parent, "text", "", null, role, role is null ? null : text, text);

    public PageSnapshot Build(string url) => new(url, _nodes);
}

/// <summary>A summary evidence source the tests fill in.</summary>
internal sealed class FakeSummaryEvidence : ISummaryEvidence
{
    public long Duration { get; set; } = 100_000_000_000;

    public List<NavigationCommit> CommitList { get; } = [];

    public IReadOnlyList<NavigationCommit> Commits => CommitList;

    /// <summary>Each document's pages, from the time each was recorded.</summary>
    public Dictionary<string, List<(long Time, PageSnapshot Page)>> Pages { get; } = [];

    public Dictionary<string, List<(long Time, InteractionCurrent State)>> Interactions { get; } = [];

    public PageSnapshot? PageAt(string documentToken, string url, long time) =>
        Pages.TryGetValue(documentToken, out var pages) ? pages.LastOrDefault(item => item.Time <= time).Page : null;

    public InteractionCurrent? InteractionAt(string documentToken, long time) =>
        Interactions.TryGetValue(documentToken, out var states) ? states.LastOrDefault(item => item.Time <= time).State : null;

    public List<TimedFact> KeyList { get; } = [];

    public IReadOnlyList<TimedFact> Keys => KeyList;

    public List<TimedFact> CommandList { get; } = [];

    public IReadOnlyList<TimedFact> Commands => CommandList;

    public List<TimedFact> ClickList { get; } = [];

    public IReadOnlyList<TimedFact> Clicks => ClickList;

    public List<FocusFact> FocusList { get; } = [];

    public IReadOnlyList<FocusFact> Focus => FocusList;

    public List<TimedFact> AnnouncementList { get; } = [];

    public IReadOnlyList<TimedFact> Announcements => AnnouncementList;

    public List<ForegroundFact> ForegroundList { get; } = [];

    public IReadOnlyList<ForegroundFact> Foreground => ForegroundList;

    public string? BrowserProcess { get; set; } = "chrome";

    public List<string> Running { get; } = [];

    public IReadOnlyList<string> AssistiveTechnologyAt(long time) => Running;

    public List<TimedFact> PopupList { get; } = [];

    public IReadOnlyList<TimedFact> Popups => PopupList;

    public List<TimedFact> ClosedList { get; } = [];

    public IReadOnlyList<TimedFact> PopupsClosed => ClosedList;

    public List<NvdaWindowFact> WindowList { get; } = [];

    public IReadOnlyList<NvdaWindowFact> NvdaWindows => WindowList;

    public List<LiveRegionFact> LiveList { get; } = [];

    public IReadOnlyList<LiveRegionFact> LiveRegions => LiveList;

    public List<ScrollFact> ScrollList { get; } = [];

    public IReadOnlyList<ScrollFact> Scrolls => ScrollList;

    public double? Viewport { get; set; } = 800;

    public double? ViewportHeightAt(string documentToken, long time) => Viewport;
}

public sealed class RecordingSummaryTests
{
    private const long Second = 1_000_000_000;

    private static NavigationCommit Commit(long seconds, string url, bool sameDocument = false, string tab = "b/frame-1", string? token = "doc-1", string? id = null) =>
        new(seconds * Second, id ?? $"nav-{seconds}-{tab}", tab, url, sameDocument, token, true, false);

    private static FragmentJudgement Step(NavigationCommit commit) =>
        new(new FragmentStep(commit, PageSnapshot.SplitFragment(commit.Url).Fragment!, "in-page link", "test", "somewhere", null), null);

    // A page with a skip link before the main landmark, headings, a back to
    // top link at the end, and a link into the middle of the content.
    private static (PageSnapshot Page, long Skip, long Top, long Middle) LandmarkPage(string url = "https://example.test/a")
    {
        var page = new PageBuilder();
        var html = page.Element(page.Document, "html", "rootWebArea", "Example page");
        var body = page.Element(html, "body", "genericContainer");
        var skip = page.Element(body, "a", "link", "Skip to content", ("href", "#content"));
        var nav = page.Element(body, "nav", "navigation", "Site");
        var middle = page.Element(nav, "a", "link", "Details", ("href", "#details"));
        var main = page.Element(body, "main", "main", "", ("id", "content"));
        var heading = page.Element(main, "h1", "heading", "Welcome");
        page.Text(heading, "Welcome", "staticText");
        page.Text(main, "Some opening text.", "staticText");
        var section = page.Element(main, "h2", "heading", "Details", ("id", "details"));
        page.Text(section, "Details", "staticText");
        var top = page.Element(main, "a", "link", "Back to top", ("href", "#top"));
        return (page.Build(url), skip, top, middle);
    }

    [Fact]
    public void CrossDocumentCommitsStartVisitsAndEachEndsAtTheTabsNext()
    {
        var visits = VisitDivider.Divide(
            [Commit(1, "https://a.test/"), Commit(5, "https://a.test/next")],
            20 * Second,
            (_, _, _) => throw new InvalidOperationException("no fragment change"));

        Assert.Collection(
            visits,
            visit => Assert.Equal((1, VisitKind.Page, 1 * Second, 5 * Second), (visit.Number, visit.Kind, visit.Start.Time, visit.End)),
            visit => Assert.Equal((2, VisitKind.Page, 5 * Second, 20 * Second), (visit.Number, visit.Kind, visit.Start.Time, visit.End)));
    }

    [Fact]
    public void SameDocumentCommitChangingThePathStartsAVisit()
    {
        var visits = VisitDivider.Divide(
            [Commit(1, "https://a.test/app"), Commit(3, "https://a.test/app/settings", sameDocument: true)],
            10 * Second,
            (_, _, _) => throw new InvalidOperationException());

        Assert.Equal([VisitKind.Page, VisitKind.SameDocument], visits.Select(visit => visit.Kind));
        Assert.Equal("same document", visits[1].Label);
    }

    [Fact]
    public void QueryOnlyChangeIsAVisitAndARepeatedUrlIsNot()
    {
        var visits = VisitDivider.Divide(
            [
                Commit(1, "https://a.test/search?q=a"),
                Commit(2, "https://a.test/search?q=b", sameDocument: true),
                Commit(3, "https://a.test/search?q=b", sameDocument: true)
            ],
            10 * Second,
            (_, _, _) => throw new InvalidOperationException());

        Assert.Equal(2, visits.Count);
        Assert.Equal(VisitKind.SameDocument, visits[1].Kind);
    }

    [Fact]
    public void FragmentChangeJudgedAStepStaysInTheVisit()
    {
        var visits = VisitDivider.Divide(
            [Commit(1, "https://a.test/page"), Commit(2, "https://a.test/page#content", sameDocument: true)],
            10 * Second,
            (commit, _, _) => Step(commit));

        var visit = Assert.Single(visits);
        Assert.Equal("content", Assert.Single(visit.Steps).Fragment);
        Assert.Equal(10 * Second, visit.End);
    }

    [Fact]
    public void FragmentChangeJudgedARouteStartsAVisitWithItsReason()
    {
        var visits = VisitDivider.Divide(
            [Commit(1, "https://a.test/app#/home"), Commit(2, "https://a.test/app#/settings", sameDocument: true)],
            10 * Second,
            (_, _, _) => new FragmentJudgement(null, "the page's main content was replaced after the change"));

        Assert.Equal(VisitKind.FragmentRoute, visits[1].Kind);
        Assert.Equal("the page's main content was replaced after the change", visits[1].RouteReason);
        Assert.Equal(2 * Second, visits[0].End);
    }

    [Fact]
    public void JudgeIsGivenTheTimeUntilTheTabsNextCommit()
    {
        var untils = new List<long>();
        VisitDivider.Divide(
            [
                Commit(1, "https://a.test/page"),
                Commit(2, "https://a.test/page#a", sameDocument: true),
                Commit(3, "https://b.test/", tab: "b/frame-2", token: "doc-2"),
                Commit(6, "https://a.test/other")
            ],
            10 * Second,
            (commit, _, until) =>
            {
                untils.Add(until);
                return Step(commit);
            });

        Assert.Equal([6 * Second], untils);
    }

    [Fact]
    public void ReturnRefersToTheMostRecentEarlierVisitToTheUrl()
    {
        var visits = VisitDivider.Divide(
            [Commit(1, "https://a.test/"), Commit(2, "https://a.test/b"), Commit(3, "https://a.test/"), Commit(4, "https://a.test/b"), Commit(5, "https://a.test/")],
            10 * Second,
            (_, _, _) => throw new InvalidOperationException());

        Assert.Equal([null, null, 1, 2, 3], visits.Select(visit => visit.ReturnOf));
    }

    [Fact]
    public void EachTabHasItsOwnVisits()
    {
        var visits = VisitDivider.Divide(
            [
                Commit(1, "https://a.test/", tab: "b/frame-1"),
                Commit(2, "https://b.test/", tab: "b/frame-2", token: "doc-2"),
                Commit(4, "https://a.test/next", tab: "b/frame-1")
            ],
            10 * Second,
            (_, _, _) => throw new InvalidOperationException());

        Assert.Equal([4 * Second, 10 * Second, 10 * Second], visits.Select(visit => visit.End));
    }

    [Fact]
    public void NoCommitsIsNoVisits()
    {
        Assert.Empty(VisitDivider.Divide([], 10 * Second, (_, _, _) => throw new InvalidOperationException()));
    }

    [Fact]
    public void EmptyFragmentIndicatesTheTop()
    {
        var (page, _, _, _) = LandmarkPage();

        Assert.Equal(IndicatedPartKind.Top, page.IndicatedPart("").Kind);
    }

    [Fact]
    public void TopIndicatesTheTopOnlyWhenNoElementHasThatId()
    {
        var (page, _, _, _) = LandmarkPage();
        var builder = new PageBuilder();
        var html = builder.Element(builder.Document, "html");
        var body = builder.Element(html, "body");
        var named = builder.Element(body, "div", null, null, ("id", "top"));

        Assert.Equal(IndicatedPartKind.Top, page.IndicatedPart("TOP").Kind);
        var element = builder.Build("https://a.test/").IndicatedPart("top");
        Assert.Equal(IndicatedPartKind.Element, element.Kind);
        Assert.Equal(named, element.Element!.Id);
    }

    [Fact]
    public void AnchorNameIsFoundAfterIds()
    {
        var builder = new PageBuilder();
        var body = builder.Element(builder.Element(builder.Document, "html"), "body");
        var anchor = builder.Element(body, "a", null, null, ("name", "part"));
        var byId = builder.Element(body, "div", null, null, ("id", "part"));
        var divName = builder.Element(body, "div", null, null, ("name", "other"));

        var page = builder.Build("https://a.test/");
        Assert.Equal(byId, page.IndicatedPart("part").Element!.Id);
        Assert.Equal(IndicatedPartKind.None, page.IndicatedPart("other").Kind);
        Assert.NotEqual(anchor, byId);
        Assert.NotEqual(divName, byId);
    }

    [Fact]
    public void PercentEncodedIdIsFoundAfterDecoding()
    {
        var builder = new PageBuilder();
        var body = builder.Element(builder.Element(builder.Document, "html"), "body");
        var target = builder.Element(body, "section", null, null, ("id", "café menu"));

        var found = builder.Build("https://a.test/").IndicatedPart("caf%C3%A9%20menu");
        Assert.Equal(target, found.Element!.Id);
        Assert.Contains("percent-decoding", found.Rule, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownFragmentIndicatesNothing()
    {
        var (page, _, _, _) = LandmarkPage();

        Assert.Equal(IndicatedPartKind.None, page.IndicatedPart("missing").Kind);
    }

    [Fact]
    public void LinkBeforeMainPointingToItIsASkipLink()
    {
        var (page, skip, _, _) = LandmarkPage();

        var named = page.NameLink(page.Node(skip)!, page.IndicatedPart("content"));
        Assert.Equal(InPageLinkKind.Skip, named.Kind);
    }

    [Fact]
    public void LinkInMainPointingToTheTopIsABackToTopLink()
    {
        var (page, _, top, _) = LandmarkPage();

        Assert.Equal(InPageLinkKind.BackToTop, page.NameLink(page.Node(top)!, page.IndicatedPart("top")).Kind);
    }

    [Fact]
    public void LinkBeforeMainIntoTheMiddleOfItIsAnInPageLink()
    {
        var (page, _, _, middle) = LandmarkPage();

        Assert.Equal(InPageLinkKind.InPage, page.NameLink(page.Node(middle)!, page.IndicatedPart("details")).Kind);
    }

    [Fact]
    public void WithoutMainTheFirstHeadingIsTheBoundary()
    {
        var builder = new PageBuilder();
        var body = builder.Element(builder.Element(builder.Document, "html"), "body");
        var skip = builder.Element(body, "a", null, null, ("href", "#start"));
        builder.Text(skip, "Skip navigation");
        builder.Element(body, "h1", null, null, ("id", "start"));
        var top = builder.Element(body, "a", null, null, ("href", "#"));

        var page = builder.Build("https://a.test/page");
        Assert.False(page.HasAccessibility);
        Assert.Null(page.Main);
        Assert.Equal(InPageLinkKind.Skip, page.NameLink(page.Node(skip)!, page.IndicatedPart("start")).Kind);
        Assert.Equal(InPageLinkKind.BackToTop, page.NameLink(page.Node(top)!, page.IndicatedPart("")).Kind);
    }

    [Fact]
    public void LinksToResolvesHrefsAgainstTheDocument()
    {
        var builder = new PageBuilder();
        var body = builder.Element(builder.Element(builder.Document, "html"), "body");
        builder.Element(body, "a", null, null, ("href", "#x"));
        builder.Element(body, "a", null, null, ("href", "https://a.test/page?q=1#x"));
        builder.Element(body, "a", null, null, ("href", "https://other.test/page?q=1#x"));

        Assert.Equal(2, builder.Build("https://a.test/page?q=1").LinksTo("x").Count);
    }

    [Fact]
    public void FragmentToAPlaceWithAFocusedLinkIsANamedStep()
    {
        var (page, skip, _, _) = LandmarkPage();
        var evidence = new FakeSummaryEvidence();
        evidence.Pages["doc-1"] = [(0, page)];
        evidence.Interactions["doc-1"] = [(0, new InteractionCurrent(skip, null, new Dictionary<long, TextControlState>()))];

        var judgement = SummaryCollator.JudgeFragment(evidence, Commit(2, "https://example.test/a#content", sameDocument: true), 10 * Second);

        Assert.Null(judgement.RouteReason);
        Assert.Equal("skip link", judgement.Step!.Name);
        Assert.Equal("Skip to content", judgement.Step.LinkName);
        Assert.Contains("focused", judgement.Step.Rule, StringComparison.Ordinal);
    }

    [Fact]
    public void FragmentWithNoPlaceIsARoute()
    {
        var (page, _, _, _) = LandmarkPage();
        var evidence = new FakeSummaryEvidence();
        evidence.Pages["doc-1"] = [(0, page)];

        var judgement = SummaryCollator.JudgeFragment(evidence, Commit(2, "https://example.test/a#/settings", sameDocument: true), 10 * Second);

        Assert.Null(judgement.Step);
        Assert.Contains("no place", judgement.RouteReason, StringComparison.Ordinal);
    }

    [Fact]
    public void FragmentAfterWhichMainIsReplacedIsARoute()
    {
        var (before, _, _, _) = LandmarkPage();
        var builder = new PageBuilder();
        var body = builder.Element(builder.Element(builder.Document, "html", "rootWebArea", "App"), "body", "genericContainer");
        var main = builder.Element(body, "main", "main", "", ("id", "content"));
        builder.Element(main, "h1", "heading", "Settings");
        var after = new PageSnapshot("https://example.test/a", builder.Build("https://example.test/a").InOrder.Select(node => node with { Id = node.Id + 1000, ParentId = node.ParentId + 1000, Children = [.. node.Children.Select(child => child + 1000)] }));
        var evidence = new FakeSummaryEvidence();
        evidence.Pages["doc-1"] = [(0, before), (5 * Second, after)];

        var judgement = SummaryCollator.JudgeFragment(evidence, Commit(2, "https://example.test/a#content", sameDocument: true), 10 * Second);

        Assert.Equal("the page's main content was replaced after the change", judgement.RouteReason);
    }

    [Fact]
    public void FragmentWithoutARecordedDomIsARouteSayingSo()
    {
        var judgement = SummaryCollator.JudgeFragment(new FakeSummaryEvidence(), Commit(2, "https://example.test/a#content", sameDocument: true), 10 * Second);

        Assert.Contains("not recorded", judgement.RouteReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportedCapabilitiesAreLabelledAndCited()
    {
        var collation = SummaryCollator.Collate(new FakeSummaryEvidence(), new ParticipantCapabilities("Uses NVDA; no useful vision.", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));

        var line = Assert.Single(collation.CapabilityLines);
        Assert.StartsWith("[E1] The participant's capabilities, reported by the auditor on 2026-10-01", line, StringComparison.Ordinal);
        Assert.Contains("\"Uses NVDA; no useful vision.\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AbsentCapabilitiesAreUnknownAndObservedTechnologyIsKeptApart()
    {
        var evidence = new FakeSummaryEvidence();
        evidence.Running.Add("NVDA 2026.2");
        evidence.CommitList.Add(Commit(1, "https://example.test/a"));

        var collation = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown);

        Assert.StartsWith("The participant's capabilities are unknown", collation.CapabilityLines[0], StringComparison.Ordinal);
        // Observed running is a fact with a reference, at the start of the
        // recording, so a statement resting on it can cite it.
        Assert.Equal(
            "[E1] Observed running at the start of the recording: NVDA 2026.2. This shows what was running, not what the participant is able to do.",
            collation.CapabilityLines[1]);
        Assert.Equal(0, collation.Evidence.Of("E1")!.Time);
        Assert.Contains("] 00:00:01.000 Observed running at the visit's start: NVDA 2026.2.", collation.Visits[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void VisitFactsGiveTheStructureTextAndWhatWasDone()
    {
        var (page, skip, _, _) = LandmarkPage();
        var evidence = new FakeSummaryEvidence();
        evidence.CommitList.Add(Commit(1, "https://example.test/a"));
        evidence.Pages["doc-1"] = [(0, page)];
        evidence.KeyList.Add(new TimedFact(Second / 2, "key-0", "Enter, passed to the page"));
        evidence.KeyList.Add(new TimedFact(2 * Second, "key-1", "Tab, passed to the page"));
        evidence.KeyList.Add(new TimedFact(3 * Second, "key-2", "Tab, passed to the page"));
        evidence.FocusList.Add(new FocusFact(2 * Second, "focus-1", "doc-1", skip));
        evidence.AnnouncementList.Add(new TimedFact(2 * Second, "focus-1", "NVDA had to announce: Skip to content, link"));

        var collation = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown);

        var text = Assert.Single(collation.Visits).Text;
        Assert.Contains("Navigation committed, a new document, https://example.test/a.", text, StringComparison.Ordinal);
        Assert.Contains("The participant's last input before it: Enter, passed to the page.", text, StringComparison.Ordinal);
        Assert.Contains("Title: \"Example page\".", text, StringComparison.Ordinal);
        Assert.Contains("Landmarks: navigation \"Site\"; main.", text, StringComparison.Ordinal);
        Assert.Contains("level 1: \"Welcome\"", text, StringComparison.Ordinal);
        Assert.Contains("The text of the main landmark, in full: \"Welcome Some opening text. Details\"", text, StringComparison.Ordinal);
        Assert.Contains("Keys pressed during the visit, 2 in all: Tab, passed to the page x2.", text, StringComparison.Ordinal);
        Assert.Contains("The page's focus moved to link \"Skip to content\".", text, StringComparison.Ordinal);
        Assert.Contains("NVDA had to announce: Skip to content, link", text, StringComparison.Ordinal);
        Assert.All(collation.Visits[0].References, reference => Assert.NotNull(collation.Evidence.Of(reference)));
    }

    [Fact]
    public void TypedTextIsEachControlWhoseValueChanged()
    {
        var builder = new PageBuilder();
        var body = builder.Element(builder.Element(builder.Document, "html", "rootWebArea", "Form"), "body", "genericContainer");
        var name = builder.Element(body, "input", "textField", "Name");
        var city = builder.Element(body, "input", "textField", "City");
        var evidence = new FakeSummaryEvidence();
        evidence.CommitList.Add(Commit(1, "https://example.test/form"));
        evidence.Pages["doc-1"] = [(0, builder.Build("https://example.test/form"))];
        evidence.Interactions["doc-1"] =
        [
            (0, new InteractionCurrent(null, null, new Dictionary<long, TextControlState> { [city] = new(city, "text", "Ottawa", null, null, null) })),
            (5 * Second, new InteractionCurrent(null, null, new Dictionary<long, TextControlState>
            {
                [name] = new(name, "text", "Test Person", null, null, null),
                [city] = new(city, "text", "Ottawa", null, null, null)
            }))
        ];

        var text = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown).Visits[0].Text;

        Assert.Contains("Text in textField \"Name\" (type text) at the end of the visit: \"Test Person\".", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Ottawa", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFactIsGivenWithNoLimitOnHowManyOrHowLong()
    {
        var builder = new PageBuilder();
        var main = builder.Element(builder.Element(builder.Element(builder.Document, "html", "rootWebArea", "Long"), "body", "genericContainer"), "main", "main", "");
        var longName = new string('n', 200);
        for (var index = 1; index <= 60; index++)
        {
            builder.Element(main, "h2", "heading", $"Heading {index}");
            builder.Element(main, "button", "button", $"Button {index}");
        }

        builder.Element(main, "h2", "heading", longName);
        var text = string.Concat(Enumerable.Range(1, 400).Select(index => $"Word{index} "));
        builder.Text(main, text, "staticText");
        var evidence = new FakeSummaryEvidence();
        evidence.CommitList.Add(Commit(1, "https://example.test/long"));
        evidence.Pages["doc-1"] = [(0, builder.Build("https://example.test/long"))];
        for (var index = 0; index < 50; index++)
        {
            evidence.ClickList.Add(new TimedFact((2 + index) * Second / 10 * 10, $"click-{index}", $"Click {index}."));
            evidence.AnnouncementList.Add(new TimedFact((2 + index) * Second / 10 * 10, $"say-{index}", $"Announcement {index}."));
        }

        var collation = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown);
        var visit = collation.Visits[0];

        Assert.Contains("Heading 60", visit.Text, StringComparison.Ordinal);
        Assert.Contains("\"Button 60\"", visit.Text, StringComparison.Ordinal);
        Assert.Contains(longName, visit.Text, StringComparison.Ordinal);
        Assert.Contains("Word400", visit.Text, StringComparison.Ordinal);
        Assert.Contains("Click 49.", visit.Text, StringComparison.Ordinal);
        Assert.Contains("Announcement 49.", visit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("left out", visit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("...", visit.Text, StringComparison.Ordinal);
        Assert.Contains("\"Heading 60\"", SummaryCollator.Overview(collation), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecorderWritesTheVisitsStepsEachInputWithWhatFollowedItCounted()
    {
        // The story's steps gave the inputs alone, their responses gathered
        // in another subsection (target report, 2026-10-18); the recorder
        // now writes the steps and counts them (owner, 2026-10-18, the
        // design review's proposal 4).
        var builder = new PageBuilder();
        var html = builder.Element(builder.Document, "html", "rootWebArea", "Timeline");
        var body = builder.Element(html, "body", "genericContainer");
        var skip = builder.Element(body, "a", "link", "Skip to main content");
        var evidence = new FakeSummaryEvidence();
        evidence.CommitList.Add(Commit(1, "https://example.test/timeline"));
        evidence.Pages["doc-1"] = [(0, builder.Build("https://example.test/timeline"))];
        evidence.ScrollList.Add(new ScrollFact(Second, "scroll-1", "doc-1", "1", 0, builder.Document));
        evidence.KeyList.Add(new TimedFact(2 * Second, "key-1", "Tab, passed to the page"));
        evidence.FocusList.Add(new FocusFact(2 * Second + Second / 10, "focus-1", "doc-1", skip));
        evidence.KeyList.Add(new TimedFact(3 * Second, "key-2", "h, kept by NVDA"));
        evidence.CommandList.Add(new TimedFact(3 * Second, null, "NVDA kept h as its command: next heading."));
        evidence.AnnouncementList.Add(new TimedFact(3 * Second + Second / 10, "say-1", "NVDA had to announce (inferred, uncertain): heading, level 2"));
        foreach (var (at, letter) in new[] { (4L, "a"), (5L, "b"), (6L, "c") })
        {
            evidence.KeyList.Add(new TimedFact(at * Second, $"key-{letter}", $"{letter}, passed to the page"));
        }

        evidence.ClickList.Add(new TimedFact(7 * Second, "click-1", "Mouse left button pressed at screen point (5, 5)"));
        // Tab twice, nothing found between: one step, counted.
        evidence.KeyList.Add(new TimedFact(7 * Second + Second / 2, "key-3", "Tab, passed to the page"));
        evidence.KeyList.Add(new TimedFact(8 * Second, "key-4", "Tab, passed to the page"));

        var visit = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown).Visits[0];
        var input = new System.Text.StringBuilder();
        var references = new HashSet<string>(StringComparer.Ordinal);
        var steps = VisitSteps.Of(visit, last: true, recordingEnd: 10 * Second);
        steps.AppendInput(input, references);
        var lines = System.Text.RegularExpressions.Regex.Replace(input.ToString(), @"\[E\d+\] ", "").Split('\n');

        Assert.Equal(
            [
                "The recorder recorded 8 inputs from the participant during the visit, in 5 steps: Tab, 3 presses; h, 1 press; characters typed, 3 keys; the mouse's left button, 1 press.",
                "Before the first input: 00:00:01.000 The page scrolled from a position not recorded in the visit to 0 CSS pixels from the top at 00:00:01.000, 1 position recorded. With a viewport 800 CSS pixels high, CSS pixels 0 to 800 were then in it.",
                "Step 1: 00:00:02.000 Key pressed: Tab, passed to the page.",
                "  then: 00:00:02.100 The page's focus moved to link \"Skip to main content\".",
                "Step 2: 00:00:03.000 Key pressed: h, kept by NVDA.",
                "  then: 00:00:03.000 NVDA kept h as its command: next heading.",
                "  then: 00:00:03.100 NVDA had to announce (inferred, uncertain): heading, level 2",
                "Step 3: 00:00:04.000 Characters typed, 3 keys, from \"a\" to \"c\", passed to the page.",
                "Step 4: 00:00:07.000 Mouse left button pressed at screen point (5, 5)",
                "Step 5: 00:00:07.500 to 00:00:08.000, 2 times: Key pressed: Tab, passed to the page.",
                "The recording ended at 00:00:10.000."
            ],
            lines.Skip(1).Take(11));
        Assert.Equal(2, steps.Steps[4].InputReferences.Count);

        // As the report writes it: the counts, what came before, each step
        // numbered with what followed it, or that nothing did, and the end.
        var report = new System.Text.StringBuilder();
        LayeredReport.AppendSteps(report, steps, references => " " + string.Join(' ', references.Select(_ => "[R]")));
        Assert.Equal(
            "The recorder recorded 8 inputs from the participant during the visit, in 5 steps: Tab, 3 presses; h, 1 press; characters typed, 3 keys; the mouse's left button, 1 press.\n\n" +
            "Before the first input, the recorder found:\n\n" +
            "- 00:00:01.000 The page scrolled from a position not recorded in the visit to 0 CSS pixels from the top at 00:00:01.000, 1 position recorded. With a viewport 800 CSS pixels high, CSS pixels 0 to 800 were then in it. [R]\n\n" +
            "1. 00:00:02.000 Key pressed: Tab, passed to the page. [R]\n" +
            "   - 00:00:02.100 The page's focus moved to link \"Skip to main content\". [R]\n" +
            "2. 00:00:03.000 Key pressed: h, kept by NVDA. [R]\n" +
            "   - 00:00:03.000 NVDA kept h as its command: next heading. [R]\n" +
            "   - 00:00:03.100 NVDA had to announce (inferred, uncertain): heading, level 2 [R]\n" +
            "3. 00:00:04.000 Characters typed, 3 keys, from \"a\" to \"c\", passed to the page. [R]\n" +
            "   - The recorder found nothing after it before the next input.\n" +
            "4. 00:00:07.000 Mouse left button pressed at screen point (5, 5) [R]\n" +
            "   - The recorder found nothing after it before the next input.\n" +
            "5. 00:00:07.500 to 00:00:08.000, 2 times: Key pressed: Tab, passed to the page. [R] [R]\n" +
            "   - The recorder found nothing after it before the next input.\n\n" +
            "The recording ended at 00:00:10.000.\n\n",
            report.ToString());
        Assert.Equal("The visit ended at " + SummaryCollator.Time(visit.Visit.End) + ", when the next visit began.", VisitSteps.Of(visit, last: false, recordingEnd: 10 * Second).Ending);
        Assert.All(System.Text.RegularExpressions.Regex.Matches(input.ToString(), @"\[(E\d+)\]"), match => Assert.Contains(match.Groups[1].Value, references));
        Assert.All(references, reference => Assert.Contains(reference, visit.References));
    }

    [Fact]
    public void TheRecorderListsTheSettingsNotAtTheirDefaultsForTheExecutiveSummary()
    {
        // The design review's proposal 4 (owner, 2026-10-18): the recorder
        // lists them; the model says what they mean.
        SettingRow Row(string group, string setting, string value, DefaultState state, string? defaultValue, string key) =>
            new("A", group, setting, value, state, defaultValue, new SettingSource(0, key, "", null));
        var settings = new RecordedSettings(
            [Row("Display", "Text size", "150%", DefaultState.NotDefault, "100%", "text"), Row("Display", "Theme", "Light", DefaultState.Default, null, "theme")], null,
            [Row("Fonts", "Minimum font size", "0", DefaultState.Unknown, null, "font")], null,
            [Row("NVDA", "Speech rate", "60", DefaultState.NotDefault, null, "rate")], null,
            []);
        var builder = new System.Text.StringBuilder();

        LayeredReport.AppendSettingsNotAtDefault(builder, settings, key => $" [{key}]");

        Assert.Equal(
            "The settings not at their defaults at the start, found by the recorder; Appendices A to C give every setting, and Appendix D every change:\n\n" +
            "- NVDA, Speech rate: 60 (not the default) [rate]\n" +
            "- Display, Text size: 150% (not the default, which is 100%) [text]\n\n",
            builder.ToString());
        builder.Clear();
        LayeredReport.AppendSettingsNotAtDefault(builder, settings with { Windows = [], Assistive = [] }, key => "");
        Assert.Equal("The recorder found no setting at the start that was not at its default. Settings whose default is unknown are in Appendices A to C.\n\n", builder.ToString());
        builder.Clear();
        LayeredReport.AppendSettingsNotAtDefault(builder, RecordedSettings.None, key => "");
        Assert.Equal("The recording holds no record of the settings at the start, so which were not at their defaults is unknown.\n\n", builder.ToString());
    }

    [Fact]
    public void ThePageChangesDuringAVisitAreGivenInTimeOrderApartFromItsOtherFacts()
    {
        var builder = new PageBuilder();
        var html = builder.Element(builder.Document, "html", "rootWebArea", "Changes");
        var body = builder.Element(html, "body", "genericContainer");
        var list = builder.Element(body, "div", "list", "Results");
        var evidence = new FakeSummaryEvidence();
        evidence.CommitList.Add(Commit(1, "https://example.test/changes"));
        evidence.Pages["doc-1"] = [(0, builder.Build("https://example.test/changes"))];
        evidence.LiveList.Add(new LiveRegionFact(6 * Second, "live-1", "doc-1", "status \"Cart\"", "polite", "changed", "2 items"));
        evidence.PopupList.Add(new TimedFact(3 * Second, "popup-1", "A select list opened."));
        evidence.ClosedList.Add(new TimedFact(4 * Second, "popup-2", "A select list closed."));
        evidence.WindowList.Add(new NvdaWindowFact("Elements List", 7 * Second, "nvda-1", 9 * Second, "nvda-2"));
        // The page's own scroller: a record before the visit, then two
        // movements, the pause between them longer than a second; then a
        // scroller in the page, one record.
        evidence.ScrollList.Add(new ScrollFact(Second / 2, "scroll-0", "doc-1", "1", 0, builder.Document));
        evidence.ScrollList.Add(new ScrollFact(2 * Second, "scroll-1", "doc-1", "1", 100, builder.Document));
        evidence.ScrollList.Add(new ScrollFact(2 * Second + Second / 2, "scroll-2", "doc-1", "1", 400, builder.Document));
        evidence.ScrollList.Add(new ScrollFact(5 * Second, "scroll-3", "doc-1", "1", 1_200, builder.Document));
        evidence.ScrollList.Add(new ScrollFact(8 * Second, "scroll-4", "doc-1", "2", 50, list));

        var visit = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown).Visits[0];
        var changes = visit.Changes.Select(line => System.Text.RegularExpressions.Regex.Replace(line, @"^\[E\d+\] ", "")).ToList();

        Assert.Equal(
            [
                "00:00:02.000 The page scrolled from 0 to 400 CSS pixels from the top, from 00:00:02.000 to 00:00:02.500, 2 positions recorded. With a viewport 800 CSS pixels high, CSS pixels 400 to 1200 were then in it.",
                "00:00:03.000 A select list opened.",
                "00:00:04.000 A select list closed.",
                "00:00:05.000 The page scrolled from 400 to 1200 CSS pixels from the top at 00:00:05.000, 1 position recorded. With a viewport 800 CSS pixels high, CSS pixels 1200 to 2000 were then in it.",
                "00:00:06.000 The page changed text in a live region, status \"Cart\", to \"2 items\". Its politeness: polite.",
                "00:00:07.000 NVDA's window \"Elements List\" came to the front.",
                "00:00:08.000 The scroller list \"Results\" scrolled from a position not recorded in the visit to 50 CSS pixels from the top at 00:00:08.000, 1 position recorded.",
                "00:00:09.000 NVDA's window \"Elements List\" left the front."
            ],
            changes);
        Assert.StartsWith(VisitCollation.ChangesHeading + "\n[E", visit.ChangesText, StringComparison.Ordinal);
        Assert.Contains(visit.ChangesText, visit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(visit.Lines, line => line.Contains("select list", StringComparison.Ordinal) || line.Contains("scrolled", StringComparison.Ordinal));
        Assert.All(visit.Changes, line => Assert.Contains(System.Text.RegularExpressions.Regex.Match(line, @"^\[(E\d+)\]").Groups[1].Value, visit.References));
    }

    [Fact]
    public void AVisitWithNoChangesSaysNoneWasRecorded()
    {
        var (page, _, _, _) = LandmarkPage();
        var evidence = new FakeSummaryEvidence();
        evidence.CommitList.Add(Commit(1, "https://example.test/a"));
        evidence.Pages["doc-1"] = [(0, page)];

        var visit = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown).Visits[0];

        Assert.Empty(visit.Changes);
        Assert.EndsWith("\nThe page's changes during the visit: no change to a live region, popup opening or closing, NVDA window, or scroll was recorded.", visit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PageWithoutAccessibilityRecordsIsReadFromTagsAndSaysSo()
    {
        var builder = new PageBuilder();
        var html = builder.Element(builder.Document, "html");
        var title = builder.Element(builder.Element(html, "head"), "title");
        builder.Text(title, "Tag page");
        var body = builder.Element(html, "body");
        var heading = builder.Element(body, "h1");
        builder.Text(heading, "Hello");
        var evidence = new FakeSummaryEvidence();
        evidence.CommitList.Add(Commit(1, "https://example.test/t"));
        evidence.Pages["doc-1"] = [(0, builder.Build("https://example.test/t"))];

        var visit = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown).Visits[0];

        Assert.Equal("Tag page", visit.Title);
        Assert.Contains("roles and names are read from the tags", visit.Text, StringComparison.Ordinal);
        Assert.Contains("level 1: \"Hello\"", visit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PageWithoutARecordedDomSaysItsStructureIsUnknown()
    {
        var evidence = new FakeSummaryEvidence();
        evidence.CommitList.Add(Commit(1, "https://example.test/a"));

        var visit = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown).Visits[0];

        Assert.Contains("The page's DOM is not recorded, so its structure and text are unknown.", visit.Lines);
        Assert.Null(visit.Title);
    }

    [Fact]
    public void OtherProgramsWindowsAreListedAndTheBrowsersAreNot()
    {
        var evidence = new FakeSummaryEvidence { Duration = 30 * Second };
        evidence.ForegroundList.Add(new ForegroundFact(1 * Second, "fg-1", "chrome", "Example"));
        evidence.ForegroundList.Add(new ForegroundFact(5 * Second, "fg-2", "nvda", "NVDA Settings"));
        evidence.ForegroundList.Add(new ForegroundFact(9 * Second, "fg-3", "Chrome", "Example"));

        var window = Assert.Single(SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown).OtherWindows);

        Assert.Equal(("nvda", "NVDA Settings", 5 * Second, 9 * Second), (window.Process, window.Title, window.Start, window.End));
    }

    [Fact]
    public void EvidenceRegisterNumbersEachRecordOnce()
    {
        var register = new EvidenceRegister();

        Assert.Equal("E1", register.Add(1, "a", "event-1"));
        Assert.Equal("E2", register.Add(2, "b", null));
        Assert.Equal("E1", register.Add(3, "c", "event-1"));
        Assert.Equal(2, register.Entries.Count);
    }

    private static AcceptedStep Accepted(params Claim[] claims) =>
        new(Guid.NewGuid(), "input", JsonDocument.Parse("{}").RootElement.Clone(), claims);

    private static AttemptRecord Attempt(string step, string outcome = "accepted", string? failure = null) =>
        new(step, 1, outcome, failure, "This PC", "qwen3.6-35b-a3b", "Qwen3.6-35B-A3B-UD-Q4_K_M.gguf", "Q4_K_M", "abc123", "hashed",
            "b11607-ff026f8c0", "{\"temperature\":0}", RecordingSummaryKind.VisitTemplate, 1, 900, 200, 1200.4, 5300.6);

    [Fact]
    public void ReportCitesEveryReferenceAndListsEachInTheEvidence()
    {
        var (page, _, _, _) = LandmarkPage();
        var evidence = new FakeSummaryEvidence { Duration = 20 * Second };
        evidence.CommitList.Add(Commit(1, "https://example.test/a"));
        evidence.Pages["doc-1"] = [(0, page)];
        evidence.ForegroundList.Add(new ForegroundFact(5 * Second, "fg-2", "notepad", "notes | draft"));
        var collation = SummaryCollator.Collate(evidence, new ParticipantCapabilities("Uses NVDA.", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
        var visit = collation.Visits[0];
        var structure = visit.References.First(reference => collation.Evidence.Of(reference)!.Text.StartsWith("The page as recorded", StringComparison.Ordinal));
        var step = Accepted(
            new Claim("page", 0, 0, Claim.ClaimKind, "The page welcomes visitors.", [structure]),
            new Claim("purpose", 0, 0, Claim.UnknownKind, "What the page is for is unknown.", []),
            new Claim("activity", 0, 0, Claim.ClaimKind, "The participant arrived\nat the page.", ["E2"]));
        var overall = Accepted(
            new Claim("site", 0, 0, Claim.ClaimKind, "An example site.", ["E2"]),
            new Claim("audience", 0, 0, Claim.UnknownKind, "Its audience is unknown.", []),
            new Claim("session", 0, 0, Claim.ClaimKind, "One page was visited.", ["E2"]));

        var report = SummaryReport.Write(
            "2026-10-13-120000",
            collation,
            new ParticipantCapabilities("Uses NVDA.", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)),
            [(visit, step)],
            overall,
            [Attempt("visit-0001"), Attempt("recording")],
            CollationSettings.Default,
            new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc));

        Assert.StartsWith("# Summary of recording 2026-10-13-120000\n\nInferred by qwen3.6-35b-a3b (Q4_K_M), profile \"This PC\", 2026-10-13.", report, StringComparison.Ordinal);
        Assert.Contains("### 1. Example page, 00:00:01 to 00:00:20", report, StringComparison.Ordinal);
        Assert.Contains($"The page welcomes visitors. [{structure}]", report, StringComparison.Ordinal);
        Assert.Contains("The participant arrived at the page. [E2]", report, StringComparison.Ordinal);
        Assert.Contains("> Uses NVDA. [E1]", report, StringComparison.Ordinal);
        Assert.Contains("| E1 | not in the recording | The participant's capabilities", report, StringComparison.Ordinal);
        Assert.Contains("notes \\| draft", report, StringComparison.Ordinal);
        Assert.Contains("- Model SHA-256: abc123 (computed from the file).", report, StringComparison.Ordinal);
        var cited = System.Text.RegularExpressions.Regex.Matches(report.Split("## Evidence")[0], @"\[(E\d+)\]").Select(match => match.Groups[1].Value).Distinct();
        var evidenceList = report.Split("## Evidence")[1].Split("## How this was made")[0];
        Assert.All(cited, reference => Assert.Contains($"| {reference} |", evidenceList, StringComparison.Ordinal));
        Assert.Equal(-1, report.IndexOf('\u2014', StringComparison.Ordinal));
    }

    [Fact]
    public void ReportGivesTheAssistiveTechnologyObservedRunningWithItsReference()
    {
        var evidence = new FakeSummaryEvidence();
        evidence.Running.Add("NVDA 2026.2");
        var collation = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown);

        var report = SummaryReport.Write("key", collation, ParticipantCapabilities.Unknown, [], null, [], CollationSettings.Default, DateTime.UtcNow);

        Assert.Contains(
            "Observed running at the start of the recording: NVDA 2026.2. This shows what was running, not what the participant is able to do. [E1]",
            report,
            StringComparison.Ordinal);
        Assert.DoesNotContain("No assistive technology was observed", report, StringComparison.Ordinal);
        Assert.Contains("| E1 | 00:00:00.000 | Observed running at the start of the recording: NVDA 2026.2 |", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportWithNoVisitsSaysSoAndNamesNoModel()
    {
        var collation = SummaryCollator.Collate(new FakeSummaryEvidence(), ParticipantCapabilities.Unknown);

        var report = SummaryReport.Write("key", collation, ParticipantCapabilities.Unknown, [], null, [], CollationSettings.Default, DateTime.UtcNow);

        Assert.Contains("No model was asked, because no page visits were recorded.", report, StringComparison.Ordinal);
        Assert.Contains("No page visits were recorded", report, StringComparison.Ordinal);
        Assert.Contains("Unknown: no description of the participant's capabilities was given.", report, StringComparison.Ordinal);
        Assert.Contains("No assistive technology was observed running at the start of the recording.", report, StringComparison.Ordinal);
        Assert.DoesNotContain("## The site tested", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportListsTheStepsReusedFromAnEarlierRunAndTheTaskThatWroteThem()
    {
        var collation = SummaryCollator.Collate(new FakeSummaryEvidence(), ParticipantCapabilities.Unknown);
        var source = Guid.Parse("01990000-0000-7000-8000-000000000001");
        AttemptRecord Attempt(string step) => new(step, 1, AttemptOutcomes.Accepted, null, "This PC", "qwen", null, null, null, "unknown", null, "{}",
            "stand-in-template", 1, 120, 40, 500, 1500);
        AttemptRecord[] attempts = [Attempt("visit-0001") with { ReusedFromTaskId = source }, Attempt("recording")];

        var report = SummaryReport.Write("key", collation, ParticipantCapabilities.Unknown, [], null, attempts, CollationSettings.Default, DateTime.UtcNow);

        Assert.Contains($"- Steps reused, unchanged, from an earlier run: 1 of 2. Their answers were written in task {source}: visit-0001.", report, StringComparison.Ordinal);
        Assert.Contains($"| visit-0001 | 1 | accepted, reused from task {source} |", report, StringComparison.Ordinal);
        Assert.Contains("| recording | 1 | accepted |", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheModelsGenerationSettingsAreGivenWithTheStepsThatUsedThem()
    {
        // One line for the whole report hid that the presence penalty is
        // given to the moment descriptions only (target report, 2026-10-18).
        AttemptRecord Attempt(string step, string parameters) => new(step, 1, AttemptOutcomes.Accepted, null, "This PC", "qwen", "q.gguf", "Q4", "aa", "hashed", "b1", parameters,
            "stand-in-template", 1, 120, 40, 500, 1500);
        const string Moment = "{\"seed\": 1, \"images\": 1, \"sampling\": {\"presence_penalty\": 1.5}, \"sections\": [\"screen\"], \"temperature\": 0}";
        const string Page = "{\"seed\": 1, \"images\": 4, \"sampling\": {\"presence_penalty\": 1.5}, \"sections\": [\"page\"], \"temperature\": 0}";
        const string Story = "{\"seed\": 1, \"images\": 0, \"sampling\": {}, \"sections\": [\"arriving\"], \"temperature\": 0}";
        var builder = new System.Text.StringBuilder();
        SummaryReport.AppendModelPerStep(builder,
        [
            Attempt("visit-0001-moment-01", Moment),
            Attempt("visit-0001-moment-01-page-01", Page),
            Attempt("visit-0001-story", Story) with { AttemptNumber = 2 },
            Attempt("visit-0001-story", Story) with { Outcome = AttemptOutcomes.Failed }
        ]);

        Assert.Equal(
            "- Model profile: \"This PC\", model qwen.\n" +
            "- Model file: q.gguf, quantization Q4.\n" +
            "- Model SHA-256: aa (computed from the file).\n" +
            "- Server build: b1.\n" +
            "- Generation settings of each accepted answer, which differ between steps:\n" +
            "  - For visit-0001-moment-01, visit-0001-moment-01-page-01: `{\"seed\": 1, \"sampling\": {\"presence_penalty\": 1.5}, \"temperature\": 0}`.\n" +
            "  - For visit-0001-story: `{\"seed\": 1, \"sampling\": {}, \"temperature\": 0}`.\n",
            builder.ToString());
    }
}

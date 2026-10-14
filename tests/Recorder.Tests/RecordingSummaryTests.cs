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
        Assert.Contains("The opening text of the main landmark: \"Welcome Some opening text. Details\"", text, StringComparison.Ordinal);
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
    public void FactsBeyondTheLimitsAreCountedAsLeftOut()
    {
        var builder = new PageBuilder();
        var main = builder.Element(builder.Element(builder.Element(builder.Document, "html", "rootWebArea", "Long"), "body", "genericContainer"), "main", "main", "");
        for (var index = 1; index <= 5; index++)
        {
            builder.Element(main, "h2", "heading", $"Heading {index}");
        }

        var evidence = new FakeSummaryEvidence();
        evidence.CommitList.Add(Commit(1, "https://example.test/long"));
        evidence.Pages["doc-1"] = [(0, builder.Build("https://example.test/long"))];

        var visit = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown, CollationLimits.Default with { Headings = 3 }).Visits[0];

        Assert.Contains("(2 more headings left out)", visit.Lines);
        Assert.DoesNotContain("Heading 4", visit.Text, StringComparison.Ordinal);
        Assert.Equal(2, visit.LeftOut);
    }

    [Fact]
    public void OpeningTextIsCutAtTheLimitAndCounted()
    {
        var (page, _, _, _) = LandmarkPage();
        var evidence = new FakeSummaryEvidence();
        evidence.CommitList.Add(Commit(1, "https://example.test/a"));
        evidence.Pages["doc-1"] = [(0, page)];

        var visit = SummaryCollator.Collate(evidence, ParticipantCapabilities.Unknown, CollationLimits.Default with { MainTextCharacters = 7 }).Visits[0];

        Assert.Contains("\"Welcome\" (", visit.Text, StringComparison.Ordinal);
        Assert.Contains("more characters left out)", visit.Text, StringComparison.Ordinal);
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
            CollationLimits.Default,
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

        var report = SummaryReport.Write("key", collation, ParticipantCapabilities.Unknown, [], null, [], CollationLimits.Default, DateTime.UtcNow);

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

        var report = SummaryReport.Write("key", collation, ParticipantCapabilities.Unknown, [], null, [], CollationLimits.Default, DateTime.UtcNow);

        Assert.Contains("No model was asked, because no page visits were recorded.", report, StringComparison.Ordinal);
        Assert.Contains("No page visits were recorded", report, StringComparison.Ordinal);
        Assert.Contains("Unknown: no description of the participant's capabilities was given.", report, StringComparison.Ordinal);
        Assert.Contains("No assistive technology was observed running at the start of the recording.", report, StringComparison.Ordinal);
        Assert.DoesNotContain("## The site tested", report, StringComparison.Ordinal);
    }
}

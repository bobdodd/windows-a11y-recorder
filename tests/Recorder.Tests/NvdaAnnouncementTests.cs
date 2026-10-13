using System.Text.Json;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>
/// What NVDA had to announce at each reading position step
/// (NvdaAnnouncementTimeline), by NVDA 2026.2's rules: see
/// docs/architecture/screen-reader-activity.md, "What NVDA had to announce".
/// </summary>
public sealed class NvdaAnnouncementTests
{
    private const long Second = 1_000_000_000;
    private const long Millisecond = 1_000_000;
    private const int NvdaProcess = 4242;
    private const string Url = "file:///C:/t/index.html";

    private static string J(string json) => json.Replace('\'', '"');

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(J(json));
        return document.RootElement.Clone();
    }

    private static KeyEvidenceRecord Node(long node, string role, string name, string properties = "", string children = "")
    {
        var serialized = $"id={node} {role}{(properties.Length == 0 ? "" : " " + properties)}" +
            $"{(name.Length == 0 ? "" : " name=" + name)}{(children.Length == 0 ? "" : " child_ids=" + children)}";
        return new KeyEvidenceRecord($"n{node}", Second, BrowserEvidenceChannels.Accessibility, BrowserEvidenceEventTypes.AccessibilityCheckpointNode, Element(
            $"{{'context':{{'documentToken':'PAGE'}},'checkpointId':'c','nodeIndex':0,'accessibilityNodeId':{node},'parentAccessibilityNodeId':null," +
            $"'domNodeId':null,'role':0,'roleName':'{role}','name':'{name}','description':'','serializedProperties':'{serialized}','focused':false}}"));
    }

    // Names the tree's document, as the renderer's record of an action does.
    private static KeyEvidenceRecord Received() =>
        new("received", Second, BrowserEvidenceChannels.Accessibility, BrowserEvidenceEventTypes.AccessibilityActionReceived, Element(
            "{'context':{'documentToken':'PAGE'},'treeId':'TREE-1','action':'scrollToMakeVisible','targetNodeId':1,'anchorNodeId':null,'focusNodeId':null}"));

    // A page, as the fixture page: a banner with the site's name and a
    // navigation landmark of two links, then a main landmark with a heading, a paragraph and a list of
    // two items, then a content info landmark.
    private static IEnumerable<KeyEvidenceRecord> Page(params KeyEvidenceRecord[] more) =>
    [
        Node(1, "rootWebArea", "Test page", $"FOCUSABLE <#document> name_from=relatedElement url={Url}", "2,9,22"),
        Node(2, "header", "", "<header> aria_role=banner display=flex", "3,5"),
        Node(3, "strong", "", "<strong>", "4"),
        Node(4, "staticText", "Test site"),
        Node(5, "navigation", "Main", "<nav> aria_role=navigation name_from=attribute", "6,8,10"),
        Node(6, "link", "Home", $"FOCUSABLE LINKED <a> name_from=contents url={Url}#top display=inline", "7"),
        Node(7, "staticText", "Home"),
        Node(8, "staticText", " "),
        Node(10, "link", "Away", "FOCUSABLE LINKED VISITED <a> name_from=contents url=https://example.com/ display=inline", "11"),
        Node(11, "staticText", "Away"),
        Node(9, "main", "", "<main> aria_role=main", "12,14,16"),
        Node(12, "heading", "Welcome", "<h1> name_from=contents", "13"),
        Node(13, "staticText", "Welcome"),
        Node(14, "paragraph", "", "<p>", "15"),
        Node(15, "staticText", "Hello there."),
        Node(16, "list", "", "<ul>", "17,19"),
        Node(17, "listItem", "", "<li>", "18"),
        Node(18, "staticText", "One"),
        Node(19, "listItem", "", "<li>", "20"),
        Node(20, "staticText", "Two"),
        Node(22, "footer", "", "<footer> aria_role=contentinfo", "23"),
        Node(23, "paragraph", "", "<p>", "24"),
        Node(24, "staticText", "The end."),
        .. more,
        Received()
    ];

    private static AccessibilityTreeTimeline Tree(IEnumerable<KeyEvidenceRecord> records) =>
        new(new AccessibilityRequests(records), [new PageDocument(Second / 2, "PAGE", "frame-4", true)]);

    // NVDA from the start, with its settings record; reporting holds the
    // reporting settings written, or is null for a record made before they
    // were recorded.
    private static AssistiveTechnologyTimeline Nvda(Dictionary<string, string>? reporting, string? without = null)
    {
        var members = reporting is null
            ? string.Empty
            : string.Concat(NvdaSettings.ReportingSettings.Where(setting => setting.Key != without).Select(setting =>
                $",'{setting.Key}':{(reporting.TryGetValue(setting.Key, out var value) ? $"'{value}'" : "null")}"));
        return new AssistiveTechnologyTimeline(
        [
            new AssistiveTechnologyRecord(0, AssistiveTechnologyRecords.WatchEventType,
                Element("{'products':['NVDA'],'executables':['nvda.exe'],'modules':['nvdahelperremote.dll'],'browserExecutablePath':null,'soundProblem':null}")),
            new AssistiveTechnologyRecord(Millisecond, AssistiveTechnologyRecords.ProcessStartedEventType,
                Element($"{{'product':'NVDA','role':'screen-reader','processId':{NvdaProcess},'runningAtStart':false,'productVersion':'2026.2'}}")),
            new AssistiveTechnologyRecord(2 * Millisecond, NvdaSettings.SettingsEventType, Element(
                $"{{'product':'NVDA','processId':{NvdaProcess},'copy':'portable','configFolder':'C:\\\\NVDA\\\\userConfig','read':true,'problem':null," +
                "'keyboardLayout':null,'nvdaModifierKeys':'7','multiPressTimeout':null,'autoPassThroughOnFocusChange':null,'autoPassThroughOnCaretMove':null," +
                "'trapNonCommandGestures':null,'enableOnPageLoad':null,'profiles':[],'profileTriggers':false,'customGestures':[],'gesturesProblem':null" +
                members + "}"))
        ]);
    }

    private static ReadingPositionStep Step(long time, long node, string role, string name, params string[] commands) =>
        new(time, $"step{time}", ReadingPositionKind.Known, 1, new TreeObject("TREE-1", node, role, name), name, name, "basis")
        {
            Cause = commands.Length == 0 ? "Tab, no NVDA command" : string.Join(" or ", commands),
            CommandNames = commands
        };

    private static ReadingPositionStep Tab(long time, long node, string role, string name) =>
        Step(time, node, role, name) with { Rule = 2, KeyName = "Tab" };

    private static IReadOnlyList<NvdaAnnouncement> Announce(
        IEnumerable<ReadingPositionStep> steps,
        AssistiveTechnologyTimeline? nvda = null,
        IEnumerable<KeyEvidenceRecord>? page = null) =>
        new NvdaAnnouncementTimeline([.. steps], Tree(page ?? Page()), nvda ?? Nvda([]), ScreenReaderModeTimeline.Empty).Announcements;

    [Fact]
    public void ATabToALinkSaysTheContainersEnteredThenItsTextThenItsStatesAndRole()
    {
        var announced = Announce([Tab(2 * Second, 6, "link", "Home"), Tab(3 * Second, 10, "link", "Away")]);

        // speech.py: a named landmark on focus says its name and role; a
        // link's text comes first on focus, then its states before its role.
        Assert.Equal("banner landmark  Main  navigation landmark  Home  same page  link", announced[0].Text);
        // NVDA's mode, and what it last read, are not known before the first.
        Assert.Equal(NvdaAnnouncementKind.Uncertain, announced[0].Kind);
        Assert.Contains(announced[0].Basis, note => note.Contains("what NVDA last read is not known", StringComparison.Ordinal));
        // The containers it is still in are not said again; a link to
        // another page is not "same page".
        Assert.Equal("Away  visited  link", announced[1].Text);
        Assert.Equal("NVDA had to announce (inferred, uncertain): Away  visited  link", announced[1].Line);
    }

    [Fact]
    public void MovingByLineSaysWhatItEntersAndTheListsItLeaves()
    {
        var announced = Announce(
        [
            Step(2 * Second, 12, "heading", "Welcome", "next heading"),
            Step(3 * Second, 15, "staticText", "Hello there.", "next line"),
            Step(4 * Second, 18, "staticText", "One", "next line"),
            Step(5 * Second, 20, "staticText", "Two", "next line"),
            Step(6 * Second, 24, "staticText", "The end.", "next line"),
            Step(7 * Second, 12, "heading", "Welcome", "previous line")
        ]);

        Assert.Equal(
        [
            // Quick navigation: the heading's text first, then its role and level.
            "main landmark  Welcome  heading  level 1",
            "Hello there.",
            "list  with 2 items  One",
            "Two",
            // A landmark is not left aloud by line; a list is.
            "out of list  content info landmark  The end.",
            // By line, a heading's role and level come before its text.
            "main landmark  heading  level 1  Welcome"
        ], announced.Select(item => item.Text));
        Assert.Equal(NvdaAnnouncementKind.Uncertain, announced[0].Kind);
        Assert.Equal(NvdaAnnouncementKind.Inferred, announced[1].Kind);
        Assert.Equal(NvdaAnnouncementKind.Inferred, announced[4].Kind);
        // The heading's level is taken from its tag.
        Assert.Equal(NvdaAnnouncementKind.Uncertain, announced[5].Kind);
    }

    private const string Long =
        "Held every September, Guide Dog Access Awareness Month is a time to raise awareness and understanding of guide dogs, " +
        "the rights of guide dog handlers, and to champion equal access for guide dog teams.";

    // The page with a paragraph of 200 characters after the heading.
    private static IEnumerable<KeyEvidenceRecord> LongPage() => Page(
        Node(9, "main", "", "<main> aria_role=main", "12,40,14,16"),
        Node(40, "paragraph", "", "<p>", "41"),
        Node(41, "staticText", Long));

    [Fact]
    public void ALineLongerThanTheMaximumIsReadInPartsAsNvdaSplitsIt()
    {
        var announced = Announce(
        [
            Step(2 * Second, 12, "heading", "Welcome", "next heading"),
            Step(3 * Second, 41, "staticText", Long, "next line"),
            Step(4 * Second, 41, "staticText", Long, "next line"),
            Step(5 * Second, 41, "staticText", Long, "next line"),
            Step(6 * Second, 15, "staticText", "Hello there.", "next line"),
            Step(7 * Second, 41, "staticText", Long, "previous line")
        ], page: LongPage());

        // storage.cpp, getLineOffsets: at 100 characters, back to the last
        // word's start, as NVDA's Speech Viewer showed on 2026-10-13.
        Assert.Equal(
        [
            "Held every September, Guide Dog Access Awareness Month is a time to raise awareness and",
            "understanding of guide dogs, the rights of guide dog handlers, and to champion equal access for",
            "guide dog teams.",
            "Hello there.",
            "guide dog teams."
        ], announced.Skip(1).Select(item => item.Text));
        Assert.All(announced.Skip(1), item => Assert.Equal(NvdaAnnouncementKind.Inferred, item.Kind));
        Assert.Contains(announced[2].Basis, note => note.Contains("part 2 of 3", StringComparison.Ordinal));
    }

    [Fact]
    public void WhichPartOfALongLineWasReadIsNotKnownWhereTheCaretIsNot()
    {
        var announced = Announce([Step(3 * Second, 41, "staticText", Long, "next line")], page: LongPage());

        // With nothing read before it, the landmark it is in is said first.
        Assert.Equal("main landmark  " + Long, announced[0].Text);
        Assert.Equal(NvdaAnnouncementKind.Uncertain, announced[0].Kind);
        Assert.Contains(announced[0].Basis, note => note.Contains("splits it into 3 parts; which it read is not known", StringComparison.Ordinal));
    }

    [Fact]
    public void PageDownAndUpMoveNvdasLinesPerPageFromItsCaret()
    {
        var announced = Announce(
        [
            Step(2 * Second, 12, "heading", "Welcome", "next heading"),
            Step(3 * Second, 41, "staticText", Long, "next page"),
            Step(4 * Second, 20, "staticText", "Two", "next page"),
            Step(5 * Second, 41, "staticText", Long, "previous page"),
            Step(6 * Second, 12, "heading", "Welcome", "previous page")
        ], Nvda(new Dictionary<string, string> { ["linesPerPage"] = "3" }), LongPage());

        Assert.Equal(
        [
            // Three lines on: the long line's three parts.
            "guide dog teams.",
            // "Hello there.", "One", then "Two".
            "list  with 2 items  Two",
            "out of list  guide dog teams.",
            // Three lines back: the long line's second and first parts, then
            // the heading.
            "heading  level 1  Welcome"
        ], announced.Skip(1).Select(item => item.Text));
        Assert.Equal(NvdaAnnouncementKind.Inferred, announced[1].Kind);
        Assert.Contains(announced[1].Basis, note => note.Contains("moved by page 3 lines", StringComparison.Ordinal));
        Assert.Contains(announced[2].Basis, note => note.Contains("linesPerPage 3", StringComparison.Ordinal));
    }

    [Fact]
    public void PageDownNearTheEndMovesAsManyLinesAsThereAre()
    {
        var announced = Announce(
        [
            Step(2 * Second, 20, "staticText", "Two", "next line"),
            Step(3 * Second, 24, "staticText", "The end.", "next page")
        ], page: Page(Node(22, "contentInfo", "", "<footer> aria_role=contentinfo", "23")));

        // Chromium's role for the page's footer is contentInfo.
        Assert.Equal("out of list  content info landmark  The end.", announced[1].Text);
        Assert.Contains(announced[1].Basis, note => note.Contains("moved by page 1 lines from where its caret was, as many as there were of its 25", StringComparison.Ordinal));
    }

    [Fact]
    public void ARecordingWithoutTheLinesPerPageAssumesTwentyFiveAndSaysSo()
    {
        var announced = Announce(
            [Step(2 * Second, 12, "heading", "Welcome", "next heading"), Step(3 * Second, 24, "staticText", "The end.", "next page")],
            Nvda([], without: "linesPerPage"));

        Assert.Equal(NvdaAnnouncementKind.Uncertain, announced[1].Kind);
        Assert.Contains(announced[1].Basis, note => note.Contains("lines per page was not recorded", StringComparison.Ordinal) &&
            note.Contains("default of 25 is assumed", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRecordedSettingsDecideWhatIsSaid()
    {
        var announced = Announce(
            [Tab(2 * Second, 6, "link", "Home")],
            Nvda(new Dictionary<string, string> { ["reportLinks"] = "False", ["reportLandmarks"] = "False" }));

        // A named landmark still says its name on focus with landmarks off.
        Assert.Equal("Main  Home", announced[0].Text);
        Assert.Contains(announced[0].Basis, note => note.Contains("reportLinks off", StringComparison.Ordinal));
    }

    [Fact]
    public void ARecordingWithoutTheReportingSettingsAssumesNvdasDefaultsAndSaysSo()
    {
        var announced = Announce(
            [Step(2 * Second, 12, "heading", "Welcome", "next heading"), Step(3 * Second, 15, "staticText", "Hello there.", "next line")],
            Nvda(null));

        Assert.Equal("Hello there.", announced[1].Text);
        Assert.Equal(NvdaAnnouncementKind.Uncertain, announced[1].Kind);
        Assert.Contains(announced[1].Basis, note => note.Contains("reporting settings were not recorded", StringComparison.Ordinal) &&
            note.Contains("defaults are assumed", StringComparison.Ordinal));
    }

    [Fact]
    public void ACommandNotModelledForgetsWhatNvdaLastRead()
    {
        var announced = Announce(
        [
            Step(2 * Second, 12, "heading", "Welcome", "next heading"),
            Step(3 * Second, 15, "staticText", "Hello there.", "next character"),
            Step(4 * Second, 15, "staticText", "Hello there.", "next line")
        ]);

        Assert.Equal(NvdaAnnouncementKind.NotModelled, announced[1].Kind);
        Assert.Equal("NVDA had to announce: not modelled yet; next character is not modelled yet", announced[1].Line);
        Assert.Equal(NvdaAnnouncementKind.Uncertain, announced[2].Kind);
        Assert.Contains(announced[2].Basis, note => note.Contains("next character", StringComparison.Ordinal) &&
            note.Contains("not known", StringComparison.Ordinal));
    }

    [Fact]
    public void ARoleNotModelledIsUnknown()
    {
        var announced = Announce(
            [Step(2 * Second, 32, "staticText", "Cell", "next line")],
            page: Page(
                Node(9, "main", "", "<main> aria_role=main", "12,14,16,30"),
                Node(30, "table", "", "<table>", "31"),
                Node(31, "cell", "", "<td>", "32"),
                Node(32, "staticText", "Cell")));

        Assert.Equal(NvdaAnnouncementKind.Unknown, announced[0].Kind);
        Assert.Contains("table", announced[0].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("file:///C:/t/index.html#top", true)]
    [InlineData("https://example.com/a#b", false)]
    [InlineData("file:///C:/t/other.html#top", false)]
    [InlineData("file:///C:/t/index.html#/route", false)]
    public void ALinkIsSamePageAsNvdaDecidesIt(string target, bool same) =>
        Assert.Equal(same, NvdaAnnouncementTimeline.IsSamePage(target, Url));

    [Fact]
    public void TheSettingsRecordWithTheReportingSettingsPasses()
    {
        var reporting = string.Concat(NvdaSettings.ReportingSettings.Select(setting => $",'{setting.Key}':null"));
        var issues = new List<EventValidationIssue>();
        EventPayloadValidator.Validate(AssistiveTechnologyRecords.Channel, NvdaSettings.SettingsEventType, Element(
            "{'product':'NVDA','processId':1,'copy':'portable','configFolder':null,'read':false,'problem':'x','keyboardLayout':null," +
            "'nvdaModifierKeys':null,'multiPressTimeout':null,'autoPassThroughOnFocusChange':null,'autoPassThroughOnCaretMove':null," +
            "'trapNonCommandGestures':null,'enableOnPageLoad':null,'profiles':[],'profileTriggers':false,'customGestures':[],'gesturesProblem':null" +
            reporting + "}"), 2000, issues);
        Assert.Empty(issues);
    }
}

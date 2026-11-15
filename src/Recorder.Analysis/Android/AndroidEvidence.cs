using System.Globalization;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>A finding as seen on one visit, with that visit's evidence reference.</summary>
public sealed record FindingVisit(PhoneFinding Finding, string Reference)
{
    public PhoneVisit Visit => Finding.Visit;
}

/// <summary>
/// A finding as the report numbers it, with its guidance: one check's
/// result for one item, on one visit or, grouped, on each visit to the
/// screen it was seen on (android-report.md, "Findings on more than one
/// visit"), each visit with its own evidence reference.
/// </summary>
/// <param name="Visits">The visits it was seen on, in time order, one for a finding of the app as a whole.</param>
/// <param name="Impact">The impact level once confirmed, or null when the guidance has none.</param>
/// <param name="Rank">The level's rank, 3 for high down to 1 for low, 0 when there is none.</param>
public sealed record NumberedFinding(int Number, IReadOnlyList<FindingVisit> Visits, CheckGuidance? Guidance, GuidanceImpact? Impact, int Rank)
{
    /// <summary>The finding as first seen.</summary>
    public PhoneFinding Finding => Visits[0].Finding;

    /// <summary>The first visit's reference.</summary>
    public string Reference => Visits[0].Reference;

    /// <summary>Whether it was seen on more than one visit, so is grouped under its screen.</summary>
    public bool OnSeveralVisits => Visits.Count > 1;

    /// <summary>The finding's heading: its number, the guidance's title or the check's name, and the item.</summary>
    public string Heading => $"Finding {Number.ToString(CultureInfo.InvariantCulture)}: {Guidance?.Title ?? PhoneCheckNames.Title(Finding.Check)}: {Finding.Label}";
}

/// <summary>
/// A screen as the report gives it (projects.md, "The same screen"): in a
/// report of one recording, that recording's screen; in a project's
/// report, every recording's screen with the same screen key, the app,
/// title and activity by the rule used within a recording (owner,
/// 2026-11-14: "Same screen key"), the recordings in the order they were
/// made.
/// </summary>
public sealed class ReportScreen
{
    internal ReportScreen(int number, PhoneScreen first, string name)
    {
        Number = number;
        First = first;
        Name = name;
        _members.Add(first);
    }

    /// <summary>The screen's number in the report, in order of first visit.</summary>
    public int Number { get; }

    /// <summary>The recording's screen it was first seen as, whose title and how it was named the report gives.</summary>
    public PhoneScreen First { get; }

    public string Package => First.Package;

    /// <summary>Its name: its title, or its activity and a number among the report's untitled screens.</summary>
    public string Name { get; }

    public string Label => $"{Name} ({Package})";

    public string NamedBy => First.NamedBy;

    /// <summary>Each recording's screen, in recording order.</summary>
    public IReadOnlyList<PhoneScreen> Members => _members;

    internal readonly List<PhoneScreen> _members = [];

    /// <summary>Its visits, each recording's in turn, in time order.</summary>
    public IReadOnlyList<PhoneVisit> Visits => [.. _members.SelectMany(member => member.Visits)];

    /// <summary>
    /// Whether a recording's screen has this screen key: the same app,
    /// title, shared title and view IDs, and the same activity, or an
    /// activity unknown on either side, as PhoneScreenTimeline matches
    /// visits within a recording.
    /// </summary>
    public bool Matches(PhoneScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return screen.Package == First.Package &&
            screen.Title == First.Title &&
            screen.SharedTitle == First.SharedTitle &&
            screen.ViewIdSignature == First.ViewIdSignature &&
            (screen.Activity is null || First.Activity is null || screen.Activity == First.Activity);
    }
}

/// <summary>One recording's part in a report: its screens, its device's records and its checks' results.</summary>
public sealed record ReportPart(PhoneScreenTimeline Screens, AndroidAudit Audit, PhoneDeviceTimeline Device);

/// <summary>
/// The report's numbering (android-report.md, "Evidence references"): each
/// app, the counts of findings by WCAG success criterion, the app's
/// findings, then each screen in turn, the findings seen on more than one
/// of its visits, then its visits and each visit's other findings, the
/// findings by impact, level, check and time; each one evidence reference,
/// E1 upward, in that order, and a grouped finding one for each visit.
/// A finding on more than one visit to the same screen is one finding when
/// its check, level and item are the same (owner, 2026-11-14), whatever
/// was measured on each. Every fact a model is given has a reference, so
/// that a claim about it has one to cite (target machine, 2026-11-14: a
/// screen's summary restated how the screen was named, which had none).
/// Both reports and the model's requests use the same numbers, so a
/// model's statement cites the same evidence the report lists. A
/// project's report numbers its recordings' screens as one (projects.md):
/// a screen with the same screen key in several recordings is one screen,
/// and a finding with the same check, level and item on any of its visits
/// in any recording one finding (owner, 2026-11-14: "Group it too"); a
/// finding of the app as a whole likewise across recordings.
/// </summary>
public sealed class AndroidEvidence
{
    private readonly List<(string Reference, long? Time, string Text, int Recording)> _entries = [];
    private readonly Dictionary<string, string> _apps = new(StringComparer.Ordinal);
    private readonly Dictionary<ReportScreen, string> _screens = [];
    private readonly Dictionary<PhoneScreen, ReportScreen> _screenOf = [];
    private readonly Dictionary<PhoneVisit, int> _recordingOf = [];
    private readonly List<ReportScreen> _reportScreens = [];
    private readonly Dictionary<PhoneVisit, string> _visits = [];
    private readonly Dictionary<PhoneVisit, IReadOnlyList<NumberedFinding>> _byVisit = [];
    private readonly Dictionary<PhoneVisit, List<(NumberedFinding Numbered, FindingVisit Seen)>> _onVisit = [];
    private readonly Dictionary<ReportScreen, IReadOnlyList<NumberedFinding>> _repeated = [];
    private readonly List<NumberedFinding> _app = [];
    private readonly List<NumberedFinding> _all = [];

    private AndroidEvidence()
    {
    }

    /// <summary>
    /// Each reference, its time in its recording, or null for one about
    /// the whole recording or project, what it is, and its recording's
    /// number, from 1, or 0 for one about every recording.
    /// </summary>
    public IReadOnlyList<(string Reference, long? Time, string Text, int Recording)> Entries => _entries;

    /// <summary>How many recordings the report covers.</summary>
    public int Recordings { get; private set; }

    /// <summary>The report's screens, in order of first visit.</summary>
    public IReadOnlyList<ReportScreen> Screens => _reportScreens;

    /// <summary>The report's screen a recording's screen is part of.</summary>
    public ReportScreen ScreenOf(PhoneScreen screen) => _screenOf[screen];

    /// <summary>The number, from 1, of the recording a visit is in.</summary>
    public int RecordingOf(PhoneVisit visit) => _recordingOf[visit];

    /// <summary>The reference of the recorder's counts of findings by WCAG success criterion.</summary>
    public string CountsReference { get; private set; } = "";

    /// <summary>An app's reference: its version and framework as the recorder found them.</summary>
    public string AppReference(string package) => _apps[package];

    /// <summary>A screen's reference: its name, app and how the recorder named it.</summary>
    public string ScreenReference(ReportScreen screen) => _screens[screen];

    public string ScreenReference(PhoneScreen screen) => _screens[ScreenOf(screen)];

    /// <summary>The findings of the whole app.</summary>
    public IReadOnlyList<NumberedFinding> App => _app;

    /// <summary>Every finding in number order.</summary>
    public IReadOnlyList<NumberedFinding> All => _all;

    public string VisitReference(PhoneVisit visit) => _visits[visit];

    /// <summary>The findings seen only on this visit.</summary>
    public IReadOnlyList<NumberedFinding> For(PhoneVisit visit) => _byVisit.GetValueOrDefault(visit) ?? [];

    /// <summary>Every finding seen on this visit, those grouped under its screen included, in number order, each with what was seen on this visit.</summary>
    public IReadOnlyList<(NumberedFinding Numbered, FindingVisit Seen)> On(PhoneVisit visit) =>
        _onVisit.TryGetValue(visit, out var on) ? [.. on.OrderBy(pair => pair.Numbered.Number)] : [];

    /// <summary>The screen's findings seen on more than one of its visits.</summary>
    public IReadOnlyList<NumberedFinding> Repeated(ReportScreen screen) => _repeated.GetValueOrDefault(screen) ?? [];

    public IReadOnlyList<NumberedFinding> Repeated(PhoneScreen screen) => Repeated(ScreenOf(screen));

    /// <summary>The screen's findings, those on more than one visit first, each once.</summary>
    public IEnumerable<NumberedFinding> For(ReportScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return Repeated(screen).Concat(screen.Visits.SelectMany(For));
    }

    public IEnumerable<NumberedFinding> For(PhoneScreen screen) => For(ScreenOf(screen));

    /// <summary>A visit's name: "visit 3", or in a project's report "recording 2, visit 3".</summary>
    public string VisitName(PhoneVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        var number = visit.Number.ToString(CultureInfo.InvariantCulture);
        return Recordings > 1 ? $"recording {RecordingOf(visit).ToString(CultureInfo.InvariantCulture)}, visit {number}" : $"visit {number}";
    }

    public static AndroidEvidence Build(PhoneScreenTimeline screens, AndroidAudit audit, AndroidGuidance guidance, PhoneDeviceTimeline device) =>
        Build([new ReportPart(screens, audit, device)], guidance);

    /// <summary>The numbering of a report of one recording or of a project's recordings, in the order they were made.</summary>
    public static AndroidEvidence Build(IReadOnlyList<ReportPart> parts, AndroidGuidance guidance)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(guidance);
        var evidence = new AndroidEvidence { Recordings = parts.Count };
        var project = parts.Count > 1;
        var audits = new Dictionary<PhoneVisit, AndroidAudit>();
        for (var index = 0; index < parts.Count; index++)
        {
            foreach (var visit in parts[index].Screens.Visits)
            {
                evidence._recordingOf[visit] = index + 1;
                audits[visit] = parts[index].Audit;
            }
        }

        // The screens, as one across the recordings by their screen key;
        // an untitled screen numbered among the report's untitled screens.
        var untitled = 0;
        foreach (var part in parts)
        {
            foreach (var screen in part.Screens.Screens)
            {
                var same = project ? evidence._reportScreens.FirstOrDefault(candidate => candidate.Matches(screen)) : null;
                if (same is null)
                {
                    var name = screen.Name;
                    if (screen.Title is null)
                    {
                        untitled++;
                        name = project
                            ? string.Create(CultureInfo.InvariantCulture, $"{(screen.Activity is null ? string.Empty : PhoneScreen.ShortClass(screen.Activity) + ", ")}untitled screen {untitled}")
                            : name;
                    }

                    same = new ReportScreen(evidence._reportScreens.Count + 1, screen, name);
                    evidence._reportScreens.Add(same);
                }
                else
                {
                    same._members.Add(screen);
                }

                evidence._screenOf[screen] = same;
            }
        }

        foreach (var app in evidence._reportScreens.Select(screen => screen.Package).Distinct(StringComparer.Ordinal))
        {
            // A project's app is described in each recording it was seen in, a sentence each.
            var described = project
                ? $"The app {app}. " + string.Join(" ", parts.Select((part, index) => (part, index))
                    .Where(pair => pair.part.Screens.Screens.Any(screen => screen.Package == app))
                    .Select(pair => $"In recording {(pair.index + 1).ToString(CultureInfo.InvariantCulture)}, {pair.part.Device.DescribeVersion(app)}. {pair.part.Device.DescribeFramework(app)}"))
                : $"The app {app}: {parts[0].Device.DescribeVersion(app)}. {parts[0].Device.DescribeFramework(app)}";
            evidence._apps[app] = evidence.Reference(null, described, 0);
        }

        evidence.CountsReference = evidence.Reference(null, "The recorder's counts of findings by WCAG success criterion, as in \"At a glance\"", 0);

        // A finding of the app as a whole, once for the same app, check,
        // level and item across the recordings.
        var appGroups = parts.SelectMany(part => part.Audit.AppFindings)
            .GroupBy(finding => (finding.Screen.Package, finding.Check, finding.Level, finding.Identity))
            .ToDictionary(group => Ordered(group, guidance).First(), group => group.OrderBy(finding => evidence.RecordingOf(finding.Visit)).ThenBy(First).ToList());
        foreach (var first in Ordered(appGroups.Keys, guidance))
        {
            evidence._app.Add(evidence.Add(appGroups[first], guidance, app: true));
        }

        foreach (var screen in evidence._reportScreens)
        {
            var visits = screen.Visits;
            var start = visits.Count == 0 ? (long?)null : visits[0].Start;
            evidence._screens[screen] = evidence.Reference(start,
                $"Screen {screen.Number.ToString(CultureInfo.InvariantCulture)}, {screen.Label}: the screen is {screen.NamedBy}" +
                (project ? $"; seen in {Recordings(screen)}" : ""),
                visits.Count == 0 ? 0 : evidence.RecordingOf(visits[0]));

            // The same check, level and item on more than one visit is one
            // finding, numbered before the visits' own, in the order of its
            // first sight.
            var groups = visits
                .SelectMany(visit => audits[visit].For(visit))
                .GroupBy(finding => (finding.Check, finding.Level, finding.Identity))
                .Where(group => group.Select(finding => finding.Visit).Distinct().Count() > 1)
                .ToDictionary(group => Ordered(group, guidance).First(), group => group
                    .OrderBy(finding => evidence.RecordingOf(finding.Visit)).ThenBy(finding => finding.Visit.Number).ThenBy(First).ToList());
            var grouped = groups.Values.SelectMany(group => group).ToHashSet();
            evidence._repeated[screen] = [.. Ordered(groups.Keys, guidance).Select(first => evidence.Add(groups[first], guidance))];
            foreach (var visit in visits)
            {
                var reference = evidence.Reference(visit.Start,
                    $"{Sentence(evidence.VisitName(visit))} to the screen {screen.Label}, {visit.When}: {visit.CauseText}",
                    evidence.RecordingOf(visit));
                evidence._visits[visit] = reference;
                evidence._byVisit[visit] = [.. Ordered(audits[visit].For(visit).Where(finding => !grouped.Contains(finding)), guidance).Select(finding => evidence.Add([finding], guidance))];
            }
        }

        return evidence;

        string Recordings(ReportScreen screen)
        {
            var numbers = screen.Members.Select(member => member.Visits.Count == 0 ? 0 : evidence.RecordingOf(member.Visits[0]))
                .Where(number => number > 0).Distinct().Select(number => number.ToString(CultureInfo.InvariantCulture)).ToList();
            return numbers.Count == 1 ? "recording " + numbers[0]
                : "recordings " + string.Join(", ", numbers.Take(numbers.Count - 1)) + " and " + numbers[^1];
        }
    }

    private static string Sentence(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static long First(PhoneFinding finding) => finding.Times.Count == 0 ? long.MaxValue : finding.Times[0].Time;

    private NumberedFinding Add(IReadOnlyList<PhoneFinding> findings, AndroidGuidance guidance, bool app = false)
    {
        var number = _all.Count + 1;
        var several = findings.Count > 1;
        var seen = findings
            .Select(finding => new FindingVisit(finding, Reference(First(finding) == long.MaxValue ? finding.Visit.Start : First(finding),
                several
                    ? $"Finding {number.ToString(CultureInfo.InvariantCulture)}, on {(app ? $"recording {RecordingOf(finding.Visit).ToString(CultureInfo.InvariantCulture)}" : VisitName(finding.Visit))}: {finding.Text}"
                    : $"Finding {number.ToString(CultureInfo.InvariantCulture)}: {finding.Text}",
                RecordingOf(finding.Visit))))
            .ToList();
        var check = guidance.For(findings[0].Check);
        var rank = guidance.Rank(check?.Impact);
        var numbered = new NumberedFinding(number, seen, check, guidance.OfRank(rank), rank);
        _all.Add(numbered);

        // A finding of the app as a whole is not drawn on a visit's picture.
        foreach (var visit in app ? [] : seen)
        {
            if (!_onVisit.TryGetValue(visit.Visit, out var on))
            {
                _onVisit[visit.Visit] = on = [];
            }

            on.Add((numbered, visit));
        }

        return numbered;
    }

    private string Reference(long? time, string text, int recording)
    {
        var reference = "E" + (_entries.Count + 1).ToString(CultureInfo.InvariantCulture);
        _entries.Add((reference, time, text, recording));
        return reference;
    }

    // Highest impact first, then errors before warnings before
    // information, then the check's place in the guidance, then the time
    // first seen.
    private static IEnumerable<PhoneFinding> Ordered(IEnumerable<PhoneFinding> findings, AndroidGuidance guidance)
    {
        var order = guidance.Checks.Select((check, index) => (check.Check, index)).ToDictionary(pair => pair.Check, pair => pair.index, StringComparer.Ordinal);
        return findings
            .OrderByDescending(finding => guidance.Rank(guidance.For(finding.Check)?.Impact))
            .ThenBy(finding => finding.Level)
            .ThenBy(finding => order.GetValueOrDefault(finding.Check, int.MaxValue))
            .ThenBy(finding => finding.Times.Count == 0 ? long.MaxValue : finding.Times[0].Time);
    }
}

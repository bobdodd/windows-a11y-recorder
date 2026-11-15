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
/// model's statement cites the same evidence the report lists.
/// </summary>
public sealed class AndroidEvidence
{
    private readonly List<(string Reference, long? Time, string Text)> _entries = [];
    private readonly Dictionary<string, string> _apps = new(StringComparer.Ordinal);
    private readonly Dictionary<PhoneScreen, string> _screens = [];
    private readonly Dictionary<PhoneVisit, string> _visits = [];
    private readonly Dictionary<PhoneVisit, IReadOnlyList<NumberedFinding>> _byVisit = [];
    private readonly Dictionary<PhoneVisit, List<(NumberedFinding Numbered, FindingVisit Seen)>> _onVisit = [];
    private readonly Dictionary<PhoneScreen, IReadOnlyList<NumberedFinding>> _repeated = [];
    private readonly List<NumberedFinding> _app = [];
    private readonly List<NumberedFinding> _all = [];

    private AndroidEvidence()
    {
    }

    /// <summary>Each reference, its time in the recording, or null for one about the whole recording, and what it is.</summary>
    public IReadOnlyList<(string Reference, long? Time, string Text)> Entries => _entries;

    /// <summary>The reference of the recorder's counts of findings by WCAG success criterion.</summary>
    public string CountsReference { get; private set; } = "";

    /// <summary>An app's reference: its version and framework as the recorder found them.</summary>
    public string AppReference(string package) => _apps[package];

    /// <summary>A screen's reference: its name, app and how the recorder named it.</summary>
    public string ScreenReference(PhoneScreen screen) => _screens[screen];

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
    public IReadOnlyList<NumberedFinding> Repeated(PhoneScreen screen) => _repeated.GetValueOrDefault(screen) ?? [];

    /// <summary>The screen's findings, those on more than one visit first, each once.</summary>
    public IEnumerable<NumberedFinding> For(PhoneScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return Repeated(screen).Concat(screen.Visits.SelectMany(For));
    }

    public static AndroidEvidence Build(PhoneScreenTimeline screens, AndroidAudit audit, AndroidGuidance guidance, PhoneDeviceTimeline device)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(guidance);
        ArgumentNullException.ThrowIfNull(device);
        var evidence = new AndroidEvidence();
        foreach (var app in screens.Screens.Select(screen => screen.Package).Distinct(StringComparer.Ordinal))
        {
            evidence._apps[app] = evidence.Reference(null, $"The app {app}: {device.DescribeVersion(app)}. {device.DescribeFramework(app)}");
        }

        evidence.CountsReference = evidence.Reference(null, "The recorder's counts of findings by WCAG success criterion, as in \"At a glance\"");
        foreach (var finding in Ordered(audit.AppFindings, guidance))
        {
            evidence._app.Add(evidence.Add([finding], guidance, app: true));
        }

        foreach (var screen in screens.Screens)
        {
            evidence._screens[screen] = evidence.Reference(screen.Visits.Count == 0 ? null : screen.Visits[0].Start,
                $"Screen {screen.Number.ToString(CultureInfo.InvariantCulture)}, {screen.Label}: the screen is {screen.NamedBy}");

            // The same check, level and item on more than one visit is one
            // finding, numbered before the visits' own, in the order of its
            // first sight.
            var groups = screen.Visits
                .SelectMany(audit.For)
                .GroupBy(finding => (finding.Check, finding.Level, finding.Identity))
                .Where(group => group.Select(finding => finding.Visit).Distinct().Count() > 1)
                .ToDictionary(group => Ordered(group, guidance).First(), group => group.OrderBy(finding => finding.Visit.Number).ThenBy(First).ToList());
            var grouped = groups.Values.SelectMany(group => group).ToHashSet();
            evidence._repeated[screen] = [.. Ordered(groups.Keys, guidance).Select(first => evidence.Add(groups[first], guidance))];
            foreach (var visit in screen.Visits)
            {
                var reference = evidence.Reference(visit.Start,
                    $"Visit {visit.Number.ToString(CultureInfo.InvariantCulture)} to the screen {screen.Label}, {visit.When}: {visit.CauseText}");
                evidence._visits[visit] = reference;
                evidence._byVisit[visit] = [.. Ordered(audit.For(visit).Where(finding => !grouped.Contains(finding)), guidance).Select(finding => evidence.Add([finding], guidance))];
            }
        }

        return evidence;
    }

    private static long First(PhoneFinding finding) => finding.Times.Count == 0 ? long.MaxValue : finding.Times[0].Time;

    private NumberedFinding Add(IReadOnlyList<PhoneFinding> findings, AndroidGuidance guidance, bool app = false)
    {
        var number = _all.Count + 1;
        var several = findings.Count > 1;
        var seen = findings
            .Select(finding => new FindingVisit(finding, Reference(First(finding) == long.MaxValue ? finding.Visit.Start : First(finding),
                several
                    ? $"Finding {number.ToString(CultureInfo.InvariantCulture)}, on visit {finding.Visit.Number.ToString(CultureInfo.InvariantCulture)}: {finding.Text}"
                    : $"Finding {number.ToString(CultureInfo.InvariantCulture)}: {finding.Text}")))
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

    private string Reference(long? time, string text)
    {
        var reference = "E" + (_entries.Count + 1).ToString(CultureInfo.InvariantCulture);
        _entries.Add((reference, time, text));
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

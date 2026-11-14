using System.Globalization;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>A finding as the report numbers it, with its guidance and its evidence reference.</summary>
/// <param name="Impact">The impact level once confirmed, or null when the guidance has none.</param>
/// <param name="Rank">The level's rank, 3 for high down to 1 for low, 0 when there is none.</param>
public sealed record NumberedFinding(int Number, PhoneFinding Finding, CheckGuidance? Guidance, string Reference, GuidanceImpact? Impact, int Rank)
{
    /// <summary>The finding's heading: its number, the guidance's title or the check's name, and the item.</summary>
    public string Heading => $"Finding {Number.ToString(CultureInfo.InvariantCulture)}: {Guidance?.Title ?? PhoneCheckNames.Title(Finding.Check)}: {Finding.Label}";
}

/// <summary>
/// The report's numbering (android-report.md, "Evidence references"): each
/// app, the counts of findings by WCAG success criterion, the app's
/// findings, then each screen in turn, its visits and each visit's findings
/// by impact, level, check and time; each one evidence reference, E1
/// upward, in that order. Every fact a model is given has a reference, so
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

    public IReadOnlyList<NumberedFinding> For(PhoneVisit visit) => _byVisit.GetValueOrDefault(visit) ?? [];

    public IEnumerable<NumberedFinding> For(PhoneScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return screen.Visits.SelectMany(For);
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
            evidence._app.Add(evidence.Add(finding, guidance));
        }

        foreach (var screen in screens.Screens)
        {
            evidence._screens[screen] = evidence.Reference(screen.Visits.Count == 0 ? null : screen.Visits[0].Start,
                $"Screen {screen.Number.ToString(CultureInfo.InvariantCulture)}, {screen.Label}: the screen is {screen.NamedBy}");
            foreach (var visit in screen.Visits)
            {
                var reference = evidence.Reference(visit.Start,
                    $"Visit {visit.Number.ToString(CultureInfo.InvariantCulture)} to the screen {screen.Label}, {visit.When}: {visit.CauseText}");
                evidence._visits[visit] = reference;
                evidence._byVisit[visit] = [.. Ordered(audit.For(visit), guidance).Select(finding => evidence.Add(finding, guidance))];
            }
        }

        return evidence;
    }

    private NumberedFinding Add(PhoneFinding finding, AndroidGuidance guidance)
    {
        var number = _all.Count + 1;
        var reference = Reference(finding.Times[0].Time, $"Finding {number.ToString(CultureInfo.InvariantCulture)}: {finding.Text}");
        var check = guidance.For(finding.Check);
        var rank = guidance.Rank(check?.Impact);
        var numbered = new NumberedFinding(number, finding, check, reference, guidance.OfRank(rank), rank);
        _all.Add(numbered);
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

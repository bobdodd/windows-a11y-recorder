using System.Globalization;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>A finding as the report numbers it, with its guidance and its evidence reference.</summary>
public sealed record NumberedFinding(int Number, PhoneFinding Finding, CheckGuidance? Guidance, string Reference)
{
    /// <summary>The impact score once confirmed, 0 when the guidance has none.</summary>
    public int Impact => Guidance?.Impact ?? 0;

    /// <summary>The finding's heading: its number, the guidance's title or the check's name, and the item.</summary>
    public string Heading => $"Finding {Number.ToString(CultureInfo.InvariantCulture)}: {Guidance?.Title ?? PhoneCheckNames.Title(Finding.Check)}: {Finding.Label}";
}

/// <summary>
/// The report's numbering (android-report.md, "Evidence references"): the
/// app's findings, then each screen's visits in turn, each visit's findings
/// by impact, level, check and time; each visit and each finding one
/// evidence reference, E1 upward, in that order. Both reports and the
/// model's requests use the same numbers, so a model's statement cites the
/// same evidence the report lists.
/// </summary>
public sealed class AndroidEvidence
{
    private readonly List<(string Reference, long Time, string Text)> _entries = [];
    private readonly Dictionary<PhoneVisit, string> _visits = [];
    private readonly Dictionary<PhoneVisit, IReadOnlyList<NumberedFinding>> _byVisit = [];
    private readonly List<NumberedFinding> _app = [];
    private readonly List<NumberedFinding> _all = [];

    private AndroidEvidence()
    {
    }

    public IReadOnlyList<(string Reference, long Time, string Text)> Entries => _entries;

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

    public static AndroidEvidence Build(PhoneScreenTimeline screens, AndroidAudit audit, AndroidGuidance guidance)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(guidance);
        var evidence = new AndroidEvidence();
        foreach (var finding in Ordered(audit.AppFindings, guidance))
        {
            evidence._app.Add(evidence.Add(finding, guidance));
        }

        foreach (var screen in screens.Screens)
        {
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
        var numbered = new NumberedFinding(number, finding, guidance.For(finding.Check), reference);
        _all.Add(numbered);
        return numbered;
    }

    private string Reference(long time, string text)
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
            .OrderByDescending(finding => guidance.For(finding.Check)?.Impact ?? 0)
            .ThenBy(finding => finding.Level)
            .ThenBy(finding => order.GetValueOrDefault(finding.Check, int.MaxValue))
            .ThenBy(finding => finding.Times.Count == 0 ? long.MaxValue : finding.Times[0].Time);
    }
}

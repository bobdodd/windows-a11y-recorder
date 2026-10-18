using System.Text.RegularExpressions;

namespace Recorder.Analysis;

/// <summary>
/// The check of an answer's wording, for the steps that write the report
/// or the knowledge it is written from: that a statement's text holds no
/// reference the statement does not cite, and that it does not say NVDA
/// spoke, that its speech stopped, or that the participant heard it, as
/// the recorder infers what NVDA had to announce and records no speech. An
/// answer that fails is not repaired: it is recovered from by sampling, as
/// an answer with no statement is (owner, 2026-10-18; target report of
/// 88464e7, items 1.1 and 1.3 of report-fixes-2026-10-18.md). A reference
/// in the text that the statement also cites passes here, and is taken out
/// of the text when the report is written.
/// </summary>
public static partial class WordingCheck
{
    /// <summary>How an answer that failed this check is recorded as failing.</summary>
    public const string Failure = "The answer's wording broke the report's rules: ";

    // At most this many faults are named in a failure, the rest counted.
    private const int Named = 3;

    /// <summary>The failure of the claims' wording, or null where it passes.</summary>
    public static string? Check(IReadOnlyList<Claim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var faults = new List<string>();
        foreach (var claim in claims)
        {
            var place = $"statement {claim.Ordinal + 1} of paragraph {claim.Paragraph + 1} of \"{claim.Section}\"";
            var uncited = Reference().Matches(claim.Text).Select(match => match.Value)
                .Where(reference => !claim.Evidence.Contains(reference, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal).ToList();
            if (uncited.Count > 0)
            {
                faults.Add($"{place} writes in its text {string.Join(", ", uncited)}, which it does not cite: \"{Short(claim.Text)}\"");
            }

            if (SpeechAsFact(claim.Text) is { } said)
            {
                faults.Add($"{place} says \"{said}\", as if NVDA's speech were recorded, where what NVDA had to announce is inferred: \"{Short(claim.Text)}\"");
            }
        }

        if (faults.Count == 0)
        {
            return null;
        }

        var named = string.Join("; ", faults.Take(Named));
        return faults.Count > Named
            ? $"{Failure}{named}; and {faults.Count - Named} more."
            : $"{Failure}{named}.";
    }

    /// <summary>
    /// The words of a text that state NVDA's speech as fact, or null: that
    /// NVDA announced, said, spoke, read out, or voiced something, that its
    /// speech stopped, or that someone heard it. A text that says it is
    /// inferred, unknown, or not recorded, or that names NVDA's Speech
    /// Viewer, whose text is recorded, passes.
    /// </summary>
    public static string? SpeechAsFact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Qualified().IsMatch(text))
        {
            return null;
        }

        foreach (var pattern in new[] { NvdaSpoke(), SpokenByNvda(), SpeechStopped(), Heard() })
        {
            if (pattern.Match(text) is { Success: true } match)
            {
                return match.Value;
            }
        }

        return null;
    }

    private static string Short(string text) => text.Length <= 160 ? text : text[..157] + "...";

    [GeneratedRegex(@"\b[EK]\d+\b")]
    private static partial Regex Reference();

    [GeneratedRegex(@"\b(?:inferred|unknown|not known|not recorded|no recording|not a recording|does not show|do not show|cannot be known|speech viewer)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Qualified();

    [GeneratedRegex(@"\bNVDA(?:'s)?\b[^.;,]{0,40}?\b(?:announced|announces|announcing|said|says|saying|spoke|speaks|speaking|read out|reads out|reading out|read aloud|reads aloud|voiced|voices|voicing)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NvdaSpoke();

    [GeneratedRegex(@"\b(?:announced|said|spoken|read|read out|read aloud|voiced)\s+(?:aloud\s+)?by\s+NVDA\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpokenByNvda();

    [GeneratedRegex(@"\b(?:stop(?:s|ped|ping)?|silenc(?:e|es|ed|ing)|interrupt(?:s|ed|ing)?)\s+(?:NVDA(?:'s)?\s+)?(?:speech|speaking)\b|\bNVDA(?:'s)?\s+speech\s+(?:was\s+|is\s+)?(?:stopped|silenced|interrupted|cut off)\b|\bNVDA\s+(?:stopped|stops)\s+speaking\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpeechStopped();

    [GeneratedRegex(@"\bheard\b|\bhearing\s+NVDA\b", RegexOptions.IgnoreCase)]
    private static partial Regex Heard();
}

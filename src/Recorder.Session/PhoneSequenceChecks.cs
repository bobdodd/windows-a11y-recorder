using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// Step 16 of android-checks.md, white space used for layout (WCAG 1.3.2
/// Meaningful Sequence, level A, which WCAG2ICT applies as written), from
/// the tree's text alone, three discrete errors (owner, 2026-11-13):
/// <list type="bullet">
/// <item>F32, the letters of a word spaced apart: a run of 3 or more single
/// letters (2 or more Han, kana or Hangul characters) each separated by one
/// white space character, a space, a no-break space, an ideographic space or
/// a line break, such as "W e l c o m e". An initialism spaced this way,
/// which F32 exempts, is found as well (owner: "3+ letters, all").</item>
/// <item>F33, columns made with spaces: 2 or more lines of one text that each
/// have a gap of 2 or more white space characters, or a tab, between
/// words. A single line with a double space is not one.</item>
/// <item>A line break in mid-sentence (owner: newlines forcing layout fail
/// 1.3.2 as well): a line that ends with no . ! ? : ; or the like,
/// followed by a line that starts with a lowercase letter.</item>
/// </list>
/// The text looked at is what TalkBack reads from each shown item: its text
/// (not the text typed into a field, which is the user's), its content
/// description, its hint, its state description and its pane title. OCR is
/// not used: it reads the picture, and spacing drawn by the font cannot be
/// told from white space in the text.
/// </summary>
public static partial class PhoneSequenceChecks
{
    /// <summary>The fewest single letters in a run for F32, and the fewest Han, kana or Hangul characters.</summary>
    public const int MinimumLetters = 3;

    public const int MinimumIdeographs = 2;

    /// <summary>The fewest lines with a gap for F33.</summary>
    public const int MinimumColumnLines = 2;

    /// <summary>What one window gives: its findings, and how many texts were looked at.</summary>
    public sealed record Measured(IReadOnlyList<PhoneCheckResult> Results, int Texts);

    /// <summary>Runs the checks on one window.</summary>
    public static IReadOnlyList<PhoneCheckResult> Run(PhoneCheckWindow window, PhoneCheckContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Measure(window).Results;
    }

    /// <summary>The checks on one window, with the texts looked at.</summary>
    public static Measured Measure(PhoneCheckWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var results = new List<PhoneCheckResult>();
        var texts = 0;
        foreach (var item in window.Items)
        {
            if (!Shown(item))
            {
                continue;
            }

            // One finding of each check for the item: the first of its texts
            // that shows it.
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (what, text) in TextsOf(item.Node))
            {
                texts++;
                if (!found.Contains(PhoneCheckNames.SpacedLetters) && SpacedLetters(text) is { } spaced)
                {
                    found.Add(PhoneCheckNames.SpacedLetters);
                    results.Add(new PhoneCheckResult(PhoneCheckNames.SpacedLetters, PhoneCheckLevel.Error, item.Node.Index, SpacedMeasured(what, spaced)));
                }

                if (!found.Contains(PhoneCheckNames.SpaceColumns) && Columns(text) is { } columns)
                {
                    found.Add(PhoneCheckNames.SpaceColumns);
                    results.Add(new PhoneCheckResult(PhoneCheckNames.SpaceColumns, PhoneCheckLevel.Error, item.Node.Index, ColumnsMeasured(what, columns)));
                }

                if (!found.Contains(PhoneCheckNames.LineBreakMidSentence) && MidSentenceBreaks(text) is { Count: > 0 } breaks)
                {
                    found.Add(PhoneCheckNames.LineBreakMidSentence);
                    results.Add(new PhoneCheckResult(PhoneCheckNames.LineBreakMidSentence, PhoneCheckLevel.Error, item.Node.Index, BreaksMeasured(what, breaks)));
                }
            }
        }

        return new Measured(results, texts);
    }

    /// <summary>A run of single letters spaced apart: the run as written, how many letters, and the white space between them.</summary>
    public sealed record Spaced(string Run, int Letters, string Joined, string Separators);

    /// <summary>The first run of single letters spaced apart with white space, or null.</summary>
    public static Spaced? SpacedLetters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (Match match in SpacedRun().Matches(text))
        {
            var letters = match.Groups["letter"].Captures.Select(capture => capture.Value).ToList();
            var ideographs = letters.All(letter => IsIdeographic(letter));
            if (letters.Count < (ideographs ? MinimumIdeographs : MinimumLetters))
            {
                continue;
            }

            var separators = match.Groups["gap"].Captures.Select(capture => SeparatorName(capture.Value)).Distinct().ToList();
            return new Spaced(match.Value, letters.Count, string.Concat(letters), Joined(separators));
        }

        return null;
    }

    /// <summary>Lines with a gap between words: how many lines with one, and how many lines in all.</summary>
    public sealed record ColumnLines(int WithGap, int Lines, string First);

    /// <summary>The text's lines with a gap of 2 or more white space characters, or a tab, between words, when there are 2 or more; or null.</summary>
    public static ColumnLines? Columns(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = Lines(text);
        var withGap = lines.Where(line => Gap().IsMatch(line.Trim())).ToList();
        return withGap.Count >= MinimumColumnLines ? new ColumnLines(withGap.Count, lines.Count, withGap[0].Trim()) : null;
    }

    /// <summary>A line break in mid-sentence: the end of the line before and the start of the line after.</summary>
    public sealed record Break(string Before, string After);

    /// <summary>
    /// The line breaks in mid-sentence: a line ending with no sentence or
    /// clause end, followed by a line starting with a lowercase letter.
    /// Breaks inside a run of spaced letters (F32's vertical text) are not
    /// counted again.
    /// </summary>
    public static IReadOnlyList<Break> MidSentenceBreaks(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spacedRuns = SpacedRun().Matches(text)
            .Where(match => SpacedLetters(match.Value) is not null)
            .Select(match => (Start: match.Index, End: match.Index + match.Length))
            .ToList();
        var breaks = new List<Break>();
        foreach (Match lineBreak in LineBreak().Matches(text))
        {
            if (spacedRuns.Any(run => run.Start < lineBreak.Index && lineBreak.Index < run.End))
            {
                continue;
            }

            var before = text[..lineBreak.Index];
            var lastLine = before[(LastBreak(before) + 1)..].TrimEnd();
            var after = text[(lineBreak.Index + lineBreak.Length)..];
            var nextLine = after[..NextBreak(after)].TrimStart();
            if (lastLine.Length == 0 || nextLine.Length == 0 || EndsClause(lastLine))
            {
                continue;
            }

            var first = Rune.GetRuneAt(nextLine, 0);
            if (!Rune.IsLower(first))
            {
                continue;
            }

            breaks.Add(new Break(Tail(lastLine), Head(nextLine)));
        }

        return breaks;
    }

    // What TalkBack reads from an item, with what each is called.
    private static IEnumerable<(string What, string Text)> TextsOf(AndroidTreeNode node)
    {
        // A field's text is what the user typed, not the app's, unless it is
        // showing its hint.
        if (!string.IsNullOrWhiteSpace(node.Text) && (!node.Editable || node.ShowingHint))
        {
            yield return (node.ShowingHint ? "hint" : "text", node.Text);
        }

        if (!string.IsNullOrWhiteSpace(node.ContentDescription))
        {
            yield return ("content description", node.ContentDescription);
        }

        if (!string.IsNullOrWhiteSpace(node.Hint) && !(node.ShowingHint && node.Hint == node.Text))
        {
            yield return ("hint", node.Hint);
        }

        if (!string.IsNullOrWhiteSpace(node.StateDescription))
        {
            yield return ("state description", node.StateDescription);
        }

        if (!string.IsNullOrWhiteSpace(node.PaneTitle))
        {
            yield return ("pane title", node.PaneTitle);
        }
    }

    private static string SpacedMeasured(string what, Spaced spaced) => string.Create(
        CultureInfo.InvariantCulture,
        $"its {what} has {spaced.Letters} single letters separated by {spaced.Separators}, \"{Shown(spaced.Run)}\", so a screen reader may read them one at a time instead of as the word \"{spaced.Joined}\"; F32: white space used to space the letters of a word; an initialism spaced this way, which F32 allows, is found as well");

    private static string ColumnsMeasured(string what, ColumnLines columns) => string.Create(
        CultureInfo.InvariantCulture,
        $"{columns.WithGap} of the {columns.Lines} lines of its {what} have a gap of 2 or more spaces, or a tab, between words, as columns laid out with white space, the first \"{Shown(columns.First)}\"; a screen reader reads across the columns, line by line; F33: white space used to make columns");

    private static string BreaksMeasured(string what, IReadOnlyList<Break> breaks)
    {
        var first = breaks[0];
        var count = breaks.Count == 1 ? "a line break" : string.Create(CultureInfo.InvariantCulture, $"{breaks.Count} line breaks");
        return $"its {what} has {count} in mid-sentence, the first between \"{first.Before}\" and \"{first.After}\": the line ends with no . ! ? : or ;, and the next starts with a lowercase letter, so the break is there for its layout, and a screen reader or larger text keeps it";
    }

    // The white space shown so it can be seen: a line break as "⏎", a tab
    // as "⇥", a no-break or ideographic space as a space.
    private static string Shown(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            builder.Append(c switch
            {
                '\n' or '\r' or '\u2028' or '\u2029' => " ⏎ ",
                '\t' => " ⇥ ",
                _ when char.IsWhiteSpace(c) => ' ',
                _ => c,
            });
        }

        var shown = builder.ToString();
        return shown.Length <= 60 ? shown : shown[..57] + "...";
    }

    private static string SeparatorName(string separator) => separator switch
    {
        "\r\n" or "\n" or "\r" or "\u2028" or "\u2029" => "line breaks",
        " " => "spaces",
        "\u00a0" or "\u202f" => "no-break spaces",
        "\u3000" => "ideographic spaces",
        "\t" => "tabs",
        _ => "other white space",
    };

    private static string Joined(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    private static bool IsIdeographic(string letter)
    {
        var rune = Rune.GetRuneAt(letter, 0);
        var value = rune.Value;
        return value is (>= 0x3040 and <= 0x30FF) or (>= 0x3400 and <= 0x4DBF) or (>= 0x4E00 and <= 0x9FFF) or
            (>= 0xAC00 and <= 0xD7AF) or (>= 0xF900 and <= 0xFAFF) or (>= 0x20000 and <= 0x3134F);
    }

    private static List<string> Lines(string text) =>
        [.. LineBreak().Split(text).Where(line => !string.IsNullOrWhiteSpace(line))];

    private static int LastBreak(string text) => text.LastIndexOfAny(['\n', '\r', '\u2028', '\u2029']);

    private static int NextBreak(string text)
    {
        var at = text.IndexOfAny(['\n', '\r', '\u2028', '\u2029']);
        return at < 0 ? text.Length : at;
    }

    // A line that ends a sentence or clause: . ! ? : ; or an ellipsis, in
    // Latin or CJK form, with any closing quotes or brackets after it.
    private static bool EndsClause(string line)
    {
        var trimmed = line.TrimEnd('"', '\'', ')', ']', '}', '\u2019', '\u201D', '\u00BB', '\u300D', '\u300F', '\uFF09');
        return trimmed.Length > 0 && ".!?:;\u2026\u3002\uFF01\uFF1F\uFF1A\uFF1B".Contains(trimmed[^1], StringComparison.Ordinal);
    }

    private static string Tail(string line)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tail = string.Join(' ', words.TakeLast(4));
        return tail.Length <= 40 ? tail : "..." + tail[^37..];
    }

    private static string Head(string line)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var head = string.Join(' ', words.Take(4));
        return head.Length <= 40 ? head : head[..37] + "...";
    }

    private static bool Shown(PhoneCheckItem item) =>
        item.Node.VisibleToUser && item.Bounds.Right > item.Bounds.Left && item.Bounds.Bottom > item.Bounds.Top;

    // Single letters (a letter with any marks on it), not part of a longer
    // word or number, each separated from the next by one white space
    // character or line break.
    [GeneratedRegex(@"(?<![\p{L}\p{M}\p{N}])(?<letter>\p{L}\p{M}*)(?![\p{L}\p{M}\p{N}])(?:(?<gap>\r\n|[\s\u00a0\u202f\u3000])(?<letter>\p{L}\p{M}*)(?![\p{L}\p{M}\p{N}]))+", RegexOptions.CultureInvariant)]
    private static partial Regex SpacedRun();

    // A gap between words: 2 or more white space characters, or a tab, with
    // no line break in it.
    [GeneratedRegex(@"\S(?:[^\S\r\n\u2028\u2029]{2,}|\t)\S", RegexOptions.CultureInvariant)]
    private static partial Regex Gap();

    [GeneratedRegex(@"\r\n|[\n\r\u2028\u2029]", RegexOptions.CultureInvariant)]
    private static partial Regex LineBreak();
}

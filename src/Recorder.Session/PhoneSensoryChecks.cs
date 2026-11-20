using System.Globalization;
using System.Text.RegularExpressions;

namespace Recorder.Session;

/// <summary>
/// Step 31 of android-checks.md, instructions that rely on sensory
/// characteristics (WCAG 1.3.3, level A), from the words of the shown
/// items' text and content descriptions, with no model and no picture
/// (owner, 2026-11-19). An instruction is a verb of operating
/// (<see cref="Verbs"/>) and the words after it, to the next such verb or
/// the end of the sentence. It relies only on a characteristic when those
/// words hold one of the characteristic's words and none of the window's
/// controls' names. Each characteristic is one discrete issue, and each
/// finding is an error, as no model is involved:
/// <list type="bullet">
/// <item>place (<see cref="Place"/>), F14;</item>
/// <item>shape (<see cref="Shape"/>), F14;</item>
/// <item>colour (<see cref="Colour"/>), F13, and 1.4.1 too.</item>
/// </list>
/// The words before the verb are not looked at, so "To save the list, tap
/// the button on the right" does not name the Save button. "Above" and
/// "below" are not place words, as Understanding 1.3.3 lets them mean
/// before and after in the content. The lists are English.
/// </summary>
public static partial class PhoneSensoryChecks
{
    /// <summary>Verbs of operating a control, in English, as instructions use them.</summary>
    public static IReadOnlySet<string> Verbs { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "tap", "tapping", "press", "pressing", "click", "clicking", "select", "selecting",
        "choose", "choosing", "touch", "touching", "use", "using", "hit", "hitting",
    };

    /// <summary>Words of place, in English; "next to" is two words.</summary>
    public static IReadOnlySet<string> Place { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "right", "left", "top", "bottom", "corner", "beside", "next to", "upper", "lower",
    };

    /// <summary>Words of shape, in English, with their plurals.</summary>
    public static IReadOnlySet<string> Shape { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "round", "circle", "circles", "circular", "square", "squares", "triangle", "triangles", "triangular",
        "oval", "ovals", "star", "stars", "arrow", "arrows",
    };

    /// <summary>Words of colour, in English.</summary>
    public static IReadOnlySet<string> Colour { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "red", "green", "blue", "yellow", "orange", "purple", "pink", "grey", "gray", "black", "white", "brown",
    };

    /// <summary>What one window gives: its findings and the instructions looked at.</summary>
    public sealed record Measured(IReadOnlyList<PhoneCheckResult> Results, int Instructions);

    /// <summary>Runs the checks on one window.</summary>
    public static IReadOnlyList<PhoneCheckResult> Run(PhoneCheckWindow window) => Measure(window).Results;

    /// <summary>The checks on one window, with the instructions looked at.</summary>
    public static Measured Measure(PhoneCheckWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var shown = window.Items.Where(Shown).ToList();
        var controls = shown.Where(IsControl).Select(control => (Control: control, Names: NamesOf(control))).ToList();
        var results = new List<(string Check, PhoneCheckItem Item, string Measured)>();
        var instructions = 0;
        foreach (var item in shown)
        {
            var named = controls
                .Where(each => !ReferenceEquals(each.Control, item) && !IsAncestor(each.Control, item))
                .SelectMany(each => each.Names)
                .ToList();
            foreach (var text in TextsOf(item))
            {
                foreach (var (quoted, words) in Instructions(text))
                {
                    instructions++;
                    if (named.Any(name => Holds(words, name)))
                    {
                        continue;
                    }

                    foreach (var (check, list, what) in Characteristics)
                    {
                        if (Found(words, list) is { } word)
                        {
                            var also = check == PhoneCheckNames.InstructionByColour
                                ? "; WCAG 1.4.1 applies too (F13)"
                                : " (F14)";
                            results.Add((check, item,
                                $"the instruction '{quoted}' identifies a control only by its {what}, '{word}', naming none of the " +
                                $"window's controls by its text or description{also}; {Exception}"));
                        }
                    }
                }
            }
        }

        var merged = results
            .GroupBy(result => (result.Check, result.Item))
            .Select(group => new PhoneCheckResult(group.Key.Check, PhoneCheckLevel.Error, group.Key.Item.Node.Index,
                string.Join(" Also, ", group.Select(result => result.Measured).Distinct(StringComparer.Ordinal))))
            .ToList();
        return new Measured(merged, instructions);
    }

    private const string Exception =
        "a person can set this aside if the instruction names the control in another way, or the word is not about how the control looks or where it is";

    private static readonly (string Check, IReadOnlySet<string> List, string What)[] Characteristics =
    [
        (PhoneCheckNames.InstructionByPlace, Place, "place"),
        (PhoneCheckNames.InstructionByShape, Shape, "shape"),
        (PhoneCheckNames.InstructionByColour, Colour, "colour"),
    ];

    // The instructions in a text: each verb of operating and the words after
    // it, to the next such verb or the end of its sentence, with the text
    // they were found in, from the verb on.
    internal static IEnumerable<(string Quoted, IReadOnlyList<string> Words)> Instructions(string text)
    {
        foreach (var sentence in SentenceEnd().Split(text))
        {
            var words = Word().Matches(sentence).ToList();
            for (var index = 0; index < words.Count; index++)
            {
                if (!Verbs.Contains(Lower(words[index].Value)))
                {
                    continue;
                }

                var end = index + 1;
                while (end < words.Count && !Verbs.Contains(Lower(words[end].Value)))
                {
                    end++;
                }

                var after = words.Skip(index + 1).Take(end - index - 1).Select(match => Lower(match.Value)).ToList();
                if (after.Count == 0)
                {
                    continue;
                }

                var last = words[end - 1];
                var quoted = sentence[words[index].Index..(last.Index + last.Length)];
                yield return (quoted, after);
                index = end - 1;
            }
        }
    }

    internal static IReadOnlyList<string> WordsOf(string text) => [.. Word().Matches(text).Select(match => Lower(match.Value))];

    // The first word of the list in the words, a two-word entry as two words.
    private static string? Found(IReadOnlyList<string> words, IReadOnlySet<string> list)
    {
        for (var index = 0; index < words.Count; index++)
        {
            if (list.Contains(words[index]))
            {
                return words[index];
            }

            if (index + 1 < words.Count && list.Contains(words[index] + " " + words[index + 1]))
            {
                return words[index] + " " + words[index + 1];
            }
        }

        return null;
    }

    // True when the name's words are in the words, together and in order.
    private static bool Holds(IReadOnlyList<string> words, IReadOnlyList<string> name)
    {
        for (var start = 0; start + name.Count <= words.Count; start++)
        {
            var all = true;
            for (var offset = 0; offset < name.Count && all; offset++)
            {
                all = string.Equals(words[start + offset], name[offset], StringComparison.Ordinal);
            }

            if (all)
            {
                return true;
            }
        }

        return false;
    }

    // An item's own text and content description; a field's text is what
    // was typed into it, so not looked at.
    private static IEnumerable<string> TextsOf(PhoneCheckItem item)
    {
        if (!item.Node.Editable && !string.IsNullOrWhiteSpace(item.Node.Text))
        {
            yield return item.Node.Text;
        }

        if (!string.IsNullOrWhiteSpace(item.Node.ContentDescription) &&
            !string.Equals(item.Node.ContentDescription, item.Node.Text, StringComparison.Ordinal))
        {
            yield return item.Node.ContentDescription;
        }
    }

    private static bool IsControl(PhoneCheckItem item) =>
        item.Node.Clickable || item.Node.LongClickable || item.Node.Checkable || item.Node.Editable;

    // A control's names: its content description, its text (not a field's)
    // and its hint, or, with no text or description of its own, those of the
    // items inside it.
    private static List<IReadOnlyList<string>> NamesOf(PhoneCheckItem control)
    {
        var names = new List<string?> { control.Node.ContentDescription, control.Node.Editable ? null : control.Node.Text, control.Node.Hint };
        if (string.IsNullOrWhiteSpace(control.Node.ContentDescription) && (control.Node.Editable || string.IsNullOrWhiteSpace(control.Node.Text)))
        {
            names.AddRange(Descendants(control).SelectMany(inner => new[] { inner.Node.ContentDescription, inner.Node.Editable ? null : inner.Node.Text }));
        }

        return [.. names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => WordsOf(name!)).Where(words => words.Count > 0)];
    }

    private static IEnumerable<PhoneCheckItem> Descendants(PhoneCheckItem item)
    {
        foreach (var child in item.Children)
        {
            yield return child;
            foreach (var inner in Descendants(child))
            {
                yield return inner;
            }
        }
    }

    private static bool IsAncestor(PhoneCheckItem candidate, PhoneCheckItem item)
    {
        for (var parent = item.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static string Lower(string word) => word.ToLower(CultureInfo.InvariantCulture);

    private static bool Shown(PhoneCheckItem item) =>
        item.Node.VisibleToUser && item.Bounds.Right > item.Bounds.Left && item.Bounds.Bottom > item.Bounds.Top;

    // A word: letters and digits, with an apostrophe or hyphen inside.
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['\u2019\-][\p{L}\p{N}]+)*")]
    private static partial Regex Word();

    // The end of a sentence: . ! ? or ; then white space or the end, or a
    // line break.
    [GeneratedRegex(@"[.!?;](?=\s|$)|\r?\n")]
    private static partial Regex SentenceEnd();
}

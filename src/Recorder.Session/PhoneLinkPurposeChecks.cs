using System.Globalization;
using System.Text;

namespace Recorder.Session;

/// <summary>
/// Step 30 of android-checks.md, link purpose (WCAG 2.4.4, level A, and
/// 2.4.9, level AAA), from the links the companion app records in each
/// shown item's text (protocol 8), with no model and no picture (owner,
/// 2026-11-19). Each is one discrete issue:
/// <list type="bullet">
/// <item>A link with no letter or digit in its words: an error under 2.4.4.</item>
/// <item>A link whose words are a non-specific phrase (<see cref="NonSpecific"/>)
/// and that is all of its item's text, so its context is only in other
/// items (F63): an error under 2.4.4.</item>
/// <item>A non-specific link whose item's other words give its context: at
/// AAA an error under 2.4.9 (F84); at AA a warning that 2.4.9 would fail,
/// referring to conformance requirement 5.2.4, as the pass under 2.4.4
/// relies on TalkBack giving the sentence with the link.</item>
/// <item>Two links with the same words and the same other words in their
/// items, to different recorded addresses: an error under 2.4.4.</item>
/// <item>Two links with the same words to different recorded addresses, a
/// discrete issue as a screen reader's list of links gives the same words
/// twice (owner, 2026-11-19): at AAA an error under 2.4.9; at AA a
/// warning, referring to 5.2.4.</item>
/// </list>
/// A link's context is the rest of its own item's text, the same sentence
/// or paragraph (G53, H78); text in other items is not. The exception, a
/// purpose ambiguous to users in general, needs judgement, so each finding
/// says a person can set it aside on that ground.
/// </summary>
public static class PhoneLinkPurposeChecks
{
    /// <summary>
    /// Link words that do not say where a link leads, in English, compared
    /// ignoring case, white space and punctuation or symbols at either end.
    /// The app's language is not recorded, so a link in another language is
    /// judged only by the rules on no words and on the same words.
    /// </summary>
    public static IReadOnlySet<string> NonSpecific { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "click", "click here", "tap", "tap here", "here", "this", "this link", "link", "go", "continue",
        "more", "read more", "learn more", "find out more", "see more", "more info", "more information",
        "details", "more details", "info",
    };

    /// <summary>What one window gives: its findings, the links looked at, and the shown items whose links were not recorded.</summary>
    public sealed record Measured(IReadOnlyList<PhoneCheckResult> Results, int Links, int NotRecorded);

    /// <summary>Runs the checks on one window.</summary>
    public static IReadOnlyList<PhoneCheckResult> Run(PhoneCheckWindow window, PhoneCheckContext context) => Measure(window, context).Results;

    /// <summary>The checks on one window, with the links looked at.</summary>
    public static Measured Measure(PhoneCheckWindow window, PhoneCheckContext context)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(context);
        var aaa = context.Level == PhoneAuditLevel.AAA;
        var links = new List<(PhoneCheckItem Item, PhoneTextLink Link, string Words, string Context)>();
        var notRecorded = 0;
        foreach (var item in window.Items.Where(Shown))
        {
            if (item.Links is not { } recorded)
            {
                if (!string.IsNullOrEmpty(item.Node.Text))
                {
                    notRecorded++;
                }

                continue;
            }

            var text = item.Node.Text ?? string.Empty;
            foreach (var link in recorded)
            {
                links.Add((item, link, Normal(link.Text), Normal(text[..link.Start] + " " + text[link.End..])));
            }
        }

        var results = new List<(string Check, PhoneCheckLevel Level, PhoneCheckItem Item, string Measured)>();
        foreach (var (item, link, words, around) in links)
        {
            var quoted = Quote(link.Text);
            var also = aaa ? "; 2.4.9 (AAA) fails as well" : string.Empty;
            if (!link.Text.Any(char.IsLetterOrDigit))
            {
                results.Add((PhoneCheckNames.LinkNoText, PhoneCheckLevel.Error, item,
                    $"the link {quoted} has no letter or digit in its words, so they say nothing of where it leads{also}; {Exception}"));
                continue;
            }

            if (!NonSpecific.Contains(words))
            {
                continue;
            }

            if (!around.Any(char.IsLetterOrDigit))
            {
                results.Add((PhoneCheckNames.LinkNoContext, PhoneCheckLevel.Error, item,
                    $"the link {quoted} is a non-specific phrase and all of its item's text, so nothing with it says where it leads; " +
                    $"text in other items is not its context (F63){also}; {Exception}"));
                continue;
            }

            results.Add((PhoneCheckNames.LinkNotSpecific, aaa ? PhoneCheckLevel.Error : PhoneCheckLevel.Warning, item,
                $"the link {quoted} is a non-specific phrase; the rest of its item's text gives its context for 2.4.4, " +
                (aaa ? "but its words alone do not say where it leads (F84)" : $"but 2.4.9 (AAA) would fail on its words alone (F84). {Supported}") +
                $"; {Exception}"));
        }

        // The same words to different recorded addresses, each link once
        // for each other link it is confused with.
        var withAddress = links.Where(each => !string.IsNullOrEmpty(each.Link.Url) && each.Words.Any(char.IsLetterOrDigit)).ToList();
        foreach (var each in withAddress)
        {
            var others = withAddress
                .Where(other => !ReferenceEquals(other.Link, each.Link) && other.Words == each.Words &&
                    !string.Equals(other.Link.Url, each.Link.Url, StringComparison.Ordinal))
                .ToList();
            if (others.Count == 0)
            {
                continue;
            }

            var quoted = Quote(each.Link.Text);
            var sameContext = others.Where(other => other.Context == each.Context).ToList();
            if (sameContext.Count > 0)
            {
                var also = aaa ? "; 2.4.9 (AAA) fails as well" : string.Empty;
                results.Add((PhoneCheckNames.LinkSameTextAndContext, PhoneCheckLevel.Error, each.Item,
                    $"the link {quoted} has the same words, and the same other words in its item, as {Others(sameContext)}, " +
                    $"which go to a different address, so neither its words nor its context tell them apart{also}; {Exception}"));
                continue;
            }

            results.Add((PhoneCheckNames.LinkSameText, aaa ? PhoneCheckLevel.Error : PhoneCheckLevel.Warning, each.Item,
                $"the link {quoted} has the same words as {Others(others)}, which go to a different address, " +
                "so a screen reader's list of links gives the same words for both" +
                (aaa ? string.Empty : $"; 2.4.9 (AAA) would fail. {Supported}") +
                $"; {Exception}"));
        }

        // One result for each check and item, its links in the text's order.
        var merged = results
            .GroupBy(result => (result.Check, result.Level, result.Item))
            .Select(group => new PhoneCheckResult(group.Key.Check, group.Key.Level, group.Key.Item.Node.Index,
                string.Join(" Also, ", group.Select(result => result.Measured).Distinct(StringComparer.Ordinal))))
            .ToList();
        return new Measured(merged, links.Count, notRecorded);
    }

    // Why an AA warning refers to 5.2.4 (owner, 2026-11-19).
    private const string Supported =
        "The pass under 2.4.4 relies on TalkBack giving the item's text with the link, which is not known when the link is " +
        "reached by TalkBack's Links reading control or menu (WCAG 5.2.4, accessibility supported)";

    private const string Exception = "a person can set this aside if the link's purpose would be ambiguous to users in general";

    private static string Others(List<(PhoneCheckItem Item, PhoneTextLink Link, string Words, string Context)> others) =>
        string.Join(", ", others.Select(other => $"the link in {other.Item.Label}").Distinct(StringComparer.Ordinal));

    private static string Quote(string text) => "'" + text.Trim() + "'";

    // Lowercase, with white space joined and punctuation or symbols at
    // either end taken off: "Read more..." and "read  more" are one.
    internal static string Normal(string text)
    {
        var joined = new StringBuilder();
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (joined.Length > 0)
            {
                joined.Append(' ');
            }

            joined.Append(word);
        }

        var trimmed = joined.ToString();
        var start = 0;
        var end = trimmed.Length;
        while (start < end && Edge(trimmed[start]))
        {
            start++;
        }

        while (end > start && Edge(trimmed[end - 1]))
        {
            end--;
        }

        return trimmed[start..end].Trim().ToLower(CultureInfo.InvariantCulture);
    }

    private static bool Edge(char character) => char.IsPunctuation(character) || char.IsSymbol(character) || char.IsWhiteSpace(character);

    private static bool Shown(PhoneCheckItem item) =>
        item.Node.VisibleToUser && item.Bounds.Right > item.Bounds.Left && item.Bounds.Bottom > item.Bounds.Top;
}

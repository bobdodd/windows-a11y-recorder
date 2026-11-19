using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

/// <summary>Step 30 of android-checks.md: link purpose, WCAG 2.4.4 and 2.4.9, from the links in items' text.</summary>
public sealed class PhoneLinkPurposeChecksTests
{
    private static readonly PhoneCheckContext Aa = new(390f / 160f, 1080, 2404);

    private static readonly PhoneCheckContext Aaa = Aa with { Level = PhoneAuditLevel.AAA };

    private const string TextView = "android.widget.TextView";

    // A shown TextView with links at the given words of its text: each
    // (words, address), an address of null being a ClickableSpan.
    private static AndroidTreeNode Item(int index, string text, params (string Words, string? Url)[] links) =>
        new()
        {
            Index = index,
            Parent = 0,
            ClassName = TextView,
            Bounds = new AndroidNodeBounds(40, 200 * index, 1040, 200 * index + 100),
            Text = text,
            ImportantForAccessibility = true,
            VisibleToUser = true,
            Focusable = true,
            Checked = "unchecked",
            Links = [.. links.Select(link =>
            {
                var start = text.IndexOf(link.Words, StringComparison.Ordinal);
                return new AndroidTextLink(start, start + link.Words.Length, link.Url is null ? "clickable" : "url", link.Url, "located", link.Words.Length, []);
            })],
        };

    private static AndroidTreeNode Page() => new()
    {
        Index = 0,
        ClassName = "android.widget.LinearLayout",
        Bounds = new AndroidNodeBounds(0, 0, 1080, 2404),
        ImportantForAccessibility = true,
        VisibleToUser = true,
        Checked = "unchecked",
    };

    private static PhoneCheckWindow Window(params AndroidTreeNode[] items) => new(512, 1, [Page(), .. items]);

    private static List<PhoneCheckResult> Run(PhoneCheckWindow window, PhoneCheckContext context) => [.. PhoneLinkPurposeChecks.Run(window, context)];

    [Fact]
    public void DescriptiveLinksInSentences_AsOnTheRecipesScreen_GiveNoFinding()
    {
        var window = Window(
            Item(1, "Read the cooking terms before you start.", ("cooking terms", null)),
            Item(2, "Keep the kitchen safety notes close by.", ("kitchen safety notes", null)));

        Assert.Empty(Run(window, Aaa));
        var measured = PhoneLinkPurposeChecks.Measure(window, Aa);
        Assert.Equal((2, 0), (measured.Links, measured.NotRecorded));
    }

    [Fact]
    public void ALinkWithNoLetterOrDigit_IsAnError_AndSays249FailsAtAaa()
    {
        var window = Window(Item(1, "Recipes \u2192", ("\u2192", "https://example.org/recipes")));

        var aa = Assert.Single(Run(window, Aa));
        Assert.Equal((PhoneCheckNames.LinkNoText, PhoneCheckLevel.Error, 1), (aa.Check, aa.Level, aa.Item));
        Assert.DoesNotContain("2.4.9", aa.Measured, StringComparison.Ordinal);
        var aaa = Assert.Single(Run(window, Aaa));
        Assert.Contains("2.4.9 (AAA) fails as well", aaa.Measured, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonSpecificLink_AloneInItsItem_IsA244Error_WithItsContextOnlyElsewhere()
    {
        var window = Window(
            Item(1, "Soups are served hot."),
            Item(2, "Read more...", ("Read more...", null)));

        var result = Assert.Single(Run(window, Aa));
        Assert.Equal((PhoneCheckNames.LinkNoContext, PhoneCheckLevel.Error, 2), (result.Check, result.Level, result.Item));
        Assert.Equal(
            "the link 'Read more...' is a non-specific phrase and all of its item's text, so nothing with it says where it leads; " +
            "text in other items is not its context (F63); a person can set this aside if the link's purpose would be ambiguous to users in general",
            result.Measured);
    }

    [Fact]
    public void ANonSpecificLink_InASentence_PassesTheContextRule_AndIs249_AWarningAtAa_AnErrorAtAaa()
    {
        var window = Window(Item(1, "To see the full menu, tap here.", ("here", null)));

        var aa = Assert.Single(Run(window, Aa));
        Assert.Equal((PhoneCheckNames.LinkNotSpecific, PhoneCheckLevel.Warning), (aa.Check, aa.Level));
        Assert.Contains("2.4.9 (AAA) would fail on its words alone (F84)", aa.Measured, StringComparison.Ordinal);
        Assert.Contains("WCAG 5.2.4, accessibility supported", aa.Measured, StringComparison.Ordinal);
        var aaa = Assert.Single(Run(window, Aaa));
        Assert.Equal((PhoneCheckNames.LinkNotSpecific, PhoneCheckLevel.Error), (aaa.Check, aaa.Level));
        Assert.DoesNotContain("5.2.4", aaa.Measured, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameWords_ToDifferentAddresses_InDifferentSentences_IsItsOwnCheck_AWarningAtAa_AnErrorAtAaa()
    {
        var window = Window(
            Item(1, "Soups: recipe card", ("recipe card", "https://example.org/soups")),
            Item(2, "Salads: recipe card", ("recipe card", "https://example.org/salads")));

        var aa = Run(window, Aa);
        Assert.Equal([1, 2], aa.Select(result => result.Item));
        Assert.All(aa, result => Assert.Equal((PhoneCheckNames.LinkSameText, PhoneCheckLevel.Warning), (result.Check, result.Level)));
        Assert.Contains("a screen reader's list of links gives the same words for both", aa[0].Measured, StringComparison.Ordinal);
        Assert.Contains("5.2.4", aa[0].Measured, StringComparison.Ordinal);
        Assert.All(Run(window, Aaa), result => Assert.Equal((PhoneCheckNames.LinkSameText, PhoneCheckLevel.Error), (result.Check, result.Level)));
    }

    [Fact]
    public void ANonSpecificLink_UsedTwiceForDifferentAddresses_IsTwoDiscreteIssues()
    {
        var window = Window(
            Item(1, "Soups: Details", ("Details", "https://example.org/soups")),
            Item(2, "Salads: Details", ("Details", "https://example.org/salads")));

        Assert.Equal(
            [(PhoneCheckNames.LinkNotSpecific, 1), (PhoneCheckNames.LinkNotSpecific, 2), (PhoneCheckNames.LinkSameText, 1), (PhoneCheckNames.LinkSameText, 2)],
            Run(window, Aaa).Select(result => (result.Check, result.Item)).Order());
    }

    [Fact]
    public void TheSameWordsAndContext_ToDifferentAddresses_IsA244Error()
    {
        var window = Window(
            Item(1, "Details", ("Details", "https://example.org/soups")),
            Item(2, "details.", ("details", "https://example.org/salads")));

        var results = Run(window, Aa);
        // "Details" is non-specific and alone, so each is also a link with
        // no context: two discrete issues.
        Assert.Equal(
            [(PhoneCheckNames.LinkNoContext, 1), (PhoneCheckNames.LinkNoContext, 2), (PhoneCheckNames.LinkSameTextAndContext, 1), (PhoneCheckNames.LinkSameTextAndContext, 2)],
            results.Select(result => (result.Check, result.Item)).Order());
        Assert.All(results, result => Assert.Equal(PhoneCheckLevel.Error, result.Level));
    }

    [Fact]
    public void TheSameWords_ToTheSameAddress_OrWithNoAddressRecorded_GiveNoFinding()
    {
        var window = Window(
            Item(1, "Read the cooking terms first.", ("cooking terms", "https://example.org/terms")),
            Item(2, "The cooking terms are short.", ("cooking terms", "https://example.org/terms")),
            Item(3, "See the safety notes now.", ("safety notes", null)),
            Item(4, "The safety notes are long.", ("safety notes", null)));

        Assert.Empty(Run(window, Aaa));
    }

    [Fact]
    public void AnItemFromBeforeProtocol8_IsCountedAsNotRecorded_NotJudged()
    {
        var item = Item(1, "Read more") with { Links = null };

        var measured = PhoneLinkPurposeChecks.Measure(Window(item), Aaa);

        Assert.Empty(measured.Results);
        Assert.Equal((0, 1), (measured.Links, measured.NotRecorded));
    }

    [Fact]
    public void AnItemNotShown_IsNotLookedAt()
    {
        var hidden = Item(1, "Read more", ("Read more", null)) with { VisibleToUser = false };

        Assert.Equal(0, PhoneLinkPurposeChecks.Measure(Window(hidden), Aa).Links);
    }

    [Fact]
    public void TheChecksHaveTheirTitles()
    {
        Assert.Equal(
            [
                "Link with no words (WCAG 2.4.4)",
                "Non-specific link with no context (WCAG 2.4.4)",
                "Link text not specific on its own (WCAG 2.4.9, AAA)",
                "Same link text and context for different addresses (WCAG 2.4.4)",
                "Same link text for different addresses (WCAG 2.4.9, AAA)",
            ],
            new[] { PhoneCheckNames.LinkNoText, PhoneCheckNames.LinkNoContext, PhoneCheckNames.LinkNotSpecific, PhoneCheckNames.LinkSameTextAndContext, PhoneCheckNames.LinkSameText }
                .Select(PhoneCheckNames.Title));
    }
}

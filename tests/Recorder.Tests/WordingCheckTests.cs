using Recorder.Analysis;

namespace Recorder.Tests;

// The wording the target report of 88464e7 used, and what passes
// (report-fixes-2026-10-18.md, items 1.1 and 1.3).
public sealed class WordingCheckTests
{
    [Theory]
    [InlineData("NVDA announced the main landmark.", "NVDA announced")]
    [InlineData("With NVDA announcing landmarks, the participant moved on.", "NVDA announcing")]
    [InlineData("NVDA said \"heading, level 2\".", "NVDA said")]
    [InlineData("The heading was read by NVDA.", "read by NVDA")]
    [InlineData("The click stopped NVDA speech.", "stopped NVDA speech")]
    [InlineData("NVDA's speech was interrupted by the click.", "NVDA's speech was interrupted")]
    [InlineData("The participant heard the page title.", "heard")]
    public void Speech_stated_as_fact_is_found(string text, string found) =>
        Assert.Equal(found, WordingCheck.SpeechAsFact(text));

    [Theory]
    [InlineData("NVDA had to announce the main landmark (inferred).")]
    [InlineData("The click is one NVDA treats as a command to stop speech (inferred).")]
    [InlineData("What NVDA said is not recorded.")]
    [InlineData("NVDA's Speech Viewer shows \"heading, level 2\".")]
    [InlineData("NVDA was running, and the settings said to be its defaults were in use.")]
    [InlineData("The participant pressed Tab twice.")]
    public void Speech_qualified_or_not_mentioned_passes(string text) =>
        Assert.Null(WordingCheck.SpeechAsFact(text));

    [Fact]
    public void A_reference_in_the_text_passes_only_where_the_statement_cites_it()
    {
        Assert.Null(WordingCheck.Check([new Claim("actions", 0, 0, Claim.ClaimKind, "The page loaded [E2].", ["E2"])]));
        var failure = WordingCheck.Check([new Claim("actions", 1, 2, Claim.ClaimKind, "The page loaded (E2, E9) and K3.", ["E2"])]);
        Assert.Equal(
            WordingCheck.Failure + "statement 3 of paragraph 2 of \"actions\" writes in its text E9, K3, which it does not cite: \"The page loaded (E2, E9) and K3.\".",
            failure);
    }

    [Fact]
    public void Beyond_three_faults_the_rest_are_counted()
    {
        var claims = Enumerable.Range(0, 5)
            .Select(index => new Claim("actions", 0, index, Claim.ClaimKind, "NVDA announced it.", ["E1"])).ToList();
        Assert.EndsWith("; and 2 more.", WordingCheck.Check(claims), StringComparison.Ordinal);
    }
}

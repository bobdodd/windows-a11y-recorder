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
        var failure = WordingCheck.Check([new Claim("actions", 1, 2, Claim.ClaimKind, "The page loaded (E2, E9).", ["E2"])]);
        Assert.Equal(
            WordingCheck.Failure + "statement 3 of paragraph 2 of \"actions\" writes in its text E9, which it does not cite: \"The page loaded (E2, E9).\".",
            failure);
    }

    [Fact]
    public void A_knowledge_statement_number_in_the_text_fails_even_where_it_is_cited()
    {
        // The 2edb728 run, moment 6, layer 2, 2026-10-18.
        var failure = WordingCheck.Check([new Claim("elaborations", 0, 0, Claim.ClaimKind, "The frame shows eight icons, which elaborates on K52.", ["K52", "E94"])]);
        Assert.Equal(
            WordingCheck.Failure + "statement 1 of paragraph 1 of \"elaborations\" writes in its text the knowledge statement number K52, which belongs in its evidence only: \"The frame shows eight icons, which elaborates on K52.\".",
            failure);
        Assert.Null(WordingCheck.Check([new Claim("elaborations", 0, 0, Claim.ClaimKind, "The frame shows eight icons.", ["K52", "E94"])]));
    }

    [Fact]
    public void A_cited_reference_outside_brackets_fails_as_it_cannot_be_taken_out()
    {
        Assert.Null(WordingCheck.Check([new Claim("actions", 0, 0, Claim.ClaimKind, "The current frame (E94) shows the page.", ["E94"])]));
        Assert.Equal(
            WordingCheck.Failure + "statement 1 of paragraph 1 of \"actions\" writes in its text E94 outside brackets, where it cannot be taken out: \"As E94 shows, the page scrolled.\".",
            WordingCheck.Check([new Claim("actions", 0, 0, Claim.ClaimKind, "As E94 shows, the page scrolled.", ["E94"])]));
    }

    [Fact]
    public void Beyond_three_faults_the_rest_are_counted()
    {
        var claims = Enumerable.Range(0, 5)
            .Select(index => new Claim("actions", 0, index, Claim.ClaimKind, "NVDA announced it.", ["E1"])).ToList();
        Assert.EndsWith("; and 2 more.", WordingCheck.Check(claims), StringComparison.Ordinal);
    }
}

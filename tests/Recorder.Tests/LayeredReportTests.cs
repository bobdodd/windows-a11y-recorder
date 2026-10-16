using System.Text.Json;
using Recorder.Analysis;
using Recorder.Analysis.Summary;

namespace Recorder.Tests;

/// <summary>
/// The layered report's parts that need no recording: the visit's
/// knowledge, the statement forms of chapter 2 in the schema and read back
/// from an answer, and pictures in the reading view's document. See
/// docs/architecture/recording-summary.md, "The layered report".
/// </summary>
public sealed class LayeredReportTests
{
    private static Claim Statement(string section, string text, params string[] evidence) => new(section, 0, 0, Claim.ClaimKind, text, evidence);

    [Fact]
    public void Additions_are_put_under_the_statement_they_first_cite_and_new_ones_after_layer_1()
    {
        var knowledge = Knowledge.FromRecorded([Statement("page", "The page is the home page.", "E1"), Statement("activity", "The participant tabbed.", "E2")]);

        var added = knowledge.Add(
        [
            Statement("elaborations", "A hero image fills the top.", "K1", "E9"),
            Statement("corrections", "Focus did not move when tabbing.", "K2", "K1", "E9"),
            Statement("new", "A cookie notice is open.", "E9"),
            Statement("elaborations", "Cites no statement.", "E9"),
        ], 2, 1);
        knowledge.Add([Statement("elaborations", "The hero image is a carousel.", "K3", "E12")], 3, 1);

        Assert.Equal(
            [(3, "elaborates", (int?)1), (4, "corrects", 2), (5, "new", null), (6, "new", null)],
            added.Select(statement => (statement.Number, statement.Relation, statement.About)));
        Assert.Equal(["E9"], added[1].Claim.Evidence);
        Assert.Equal([1, 3, 7, 2, 4, 5, 6], knowledge.InOrder().Select(statement => statement.Number));

        var input = new System.Text.StringBuilder();
        var references = new HashSet<string>();
        knowledge.Append(input, references, withNumbers: true);
        Assert.Contains("  [K1] (layer 1, the recorded data, page, inferred) The page is the home page. [E1]\n", input.ToString(), StringComparison.Ordinal);
        Assert.Contains("    [K3] (layer 2, the captured frame at moment 1, elaborates K1, inferred) A hero image fills the top. [E9]\n", input.ToString(), StringComparison.Ordinal);
        Assert.Contains("      [K7] (layer 3, the web page at moment 1, elaborates K3, inferred) The hero image is a carousel. [E12]\n", input.ToString(), StringComparison.Ordinal);
        Assert.Contains("K7", references);
        Assert.Contains("E12", references);

        var plain = new System.Text.StringBuilder();
        var cited = new HashSet<string>();
        knowledge.Append(plain, cited, withNumbers: false);
        Assert.DoesNotContain("[K", plain.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("K1", cited);
        Assert.Contains("(layer 2, the captured frame at moment 1, correcting or disagreeing with the statement it follows, inferred) Focus did not move", plain.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_statement_reference_is_K_and_digits_only()
    {
        Assert.True(Knowledge.IsStatementReference("K12"));
        Assert.False(Knowledge.IsStatementReference("K"));
        Assert.False(Knowledge.IsStatementReference("E12"));
        Assert.False(Knowledge.IsStatementReference("K1a"));
    }

    [Fact]
    public void The_schema_has_a_form_first_only_where_forms_are_given()
    {
        var plain = ClaimCheck.Schema(["story"])["properties"]!["story"]!["items"]!["items"]!;
        Assert.Equal(["text", "kind", "evidence"], plain["properties"]!.AsObject().Select(pair => pair.Key));

        var formed = ClaimCheck.Schema(["story"], RecordingSummaryVisionKind.StoryForms)["properties"]!["story"]!["items"]!["items"]!;
        Assert.Equal(["form", "text", "kind", "evidence"], formed["properties"]!.AsObject().Select(pair => pair.Key));
        Assert.Equal(["form", "text", "kind", "evidence"], formed["required"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal(RecordingSummaryVisionKind.StoryForms, formed["properties"]!["form"]!["enum"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public void A_statement_form_is_read_from_the_answer_and_is_a_sentence_where_it_gives_none()
    {
        using var answer = JsonDocument.Parse("""
            {"story": [[{"form": "heading-1", "text": "A", "kind": "claim", "evidence": ["E1"]},
                        {"text": "B", "kind": "claim", "evidence": ["E1"]}],
                       [{"form": "picture", "text": "C", "kind": "claim", "evidence": ["E2"]},
                        {"form": "table", "text": "D", "kind": "claim", "evidence": ["E2"]}]]}
            """);
        var step = new AcceptedStep(Guid.Empty, "", answer.RootElement.Clone(), []);

        Assert.Equal("heading-1", LayeredReport.FormOf(step, new Claim("story", 0, 0, Claim.ClaimKind, "A", ["E1"])));
        Assert.Equal("sentence", LayeredReport.FormOf(step, new Claim("story", 0, 1, Claim.ClaimKind, "B", ["E1"])));
        Assert.Equal("picture", LayeredReport.FormOf(step, new Claim("story", 1, 0, Claim.ClaimKind, "C", ["E2"])));
        Assert.Equal("sentence", LayeredReport.FormOf(step, new Claim("story", 1, 1, Claim.ClaimKind, "D", ["E2"])));
        Assert.Equal("sentence", LayeredReport.FormOf(step, new Claim("story", 5, 0, Claim.ClaimKind, "E", ["E2"])));
    }

    [Fact]
    public void A_picture_on_a_line_of_its_own_is_a_picture_block_with_its_path()
    {
        var document = ReportDocument.Parse(
            "# Report\n\n![Figure 1. The home page (top).](../../analysis/images/a%20b.png)\n\nFigure 1. The home page (top). [E3]\n\nText ![inline](x.png) here.\n");

        var picture = Assert.Single(document.Blocks, block => block.Kind == ReportBlockKind.Image);
        Assert.Equal("Figure 1. The home page (top).", picture.Text);
        Assert.Equal("../../analysis/images/a b.png", picture.Source);
        Assert.Empty(picture.References);
        Assert.Equal(["E3"], document.Blocks.Single(block => block.Text.StartsWith("Figure 1.", StringComparison.Ordinal) && block.Kind == ReportBlockKind.Paragraph).References);
        Assert.Contains(document.Blocks, block => block.Kind == ReportBlockKind.Paragraph && block.Text == "Text ![inline](x.png) here.");
        Assert.Equal("../../analysis/images/a%20b.png", LayeredReport.PicturePath("analysis/images/a b.png"));
    }
}

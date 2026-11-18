using Recorder.Session;
using Xunit;

namespace Recorder.Tests;

public sealed class PhoneChecksSummaryTests
{
    [Fact]
    public void BeforeAnyCheckHasRun_TheSummarySaysTheyAreRunning()
    {
        var sections = PhoneChecksSummary.Build(new PhoneChecksSummaryInput());

        Assert.Equal("Findings by check", sections[0].Heading);
        Assert.Equal("None yet, as the checks are still running", sections[0].Result);
        Assert.Empty(sections[0].Points);
        Assert.Equal("Audit level AA", sections[1].Heading);
        Assert.Null(sections[1].Result);
        Assert.Contains("Text contrast is held to WCAG 1.4.3.", sections[1].Points);
        Assert.Contains(sections, section => section.Heading == "Checks of the tree" && section.Result == "Running");
        Assert.Contains(sections, section => section.Heading == "Contrast and the checks of text OCR read" && section.Result == "Measuring");
    }

    [Fact]
    public void AtAaa_TheAuditSectionHoldsEachLevelOnALineOfItsOwn()
    {
        var audit = PhoneChecksSummary.Build(new PhoneChecksSummaryInput { Level = PhoneAuditLevel.AAA })[1];

        Assert.Equal("Audit level AAA", audit.Heading);
        Assert.Equal(3, audit.Points.Count);
        Assert.Contains("2.5.5's exceptions", audit.HowJudged, StringComparison.Ordinal);
    }

    [Fact]
    public void ChecksThatCouldNotRun_SayWhyAndAreNotStillRunning()
    {
        var sections = PhoneChecksSummary.Build(new PhoneChecksSummaryInput
        {
            TreeProblem = "the tree could not be read.",
            ContrastProblem = "no pictures were recorded.",
        });

        Assert.Equal("No findings", sections[0].Result);
        var tree = Assert.Single(sections, section => section.Heading == "Checks of the tree");
        Assert.Equal("Could not be run", tree.Result);
        Assert.Equal(["the tree could not be read."], tree.Points);
        var contrast = Assert.Single(sections, section => section.Heading == "Contrast and the checks of text OCR read");
        Assert.Equal(["no pictures were recorded."], contrast.Points);
    }

    [Fact]
    public void AModelNote_SplitsAtSemicolonsOutsideBrackets()
    {
        var parts = PhoneChecksSummary.Parts("4 images left by the rules for a model; 4 images answered (2 fit; 2 not); 0 not judged.");

        Assert.Equal(["4 images left by the rules for a model", "4 images answered (2 fit; 2 not)", "0 not judged"], parts);
    }

    [Fact]
    public void NoFindings_IsSaidInWords()
    {
        Assert.Equal("No findings", PhoneChecksSummary.Result([]));
    }
}

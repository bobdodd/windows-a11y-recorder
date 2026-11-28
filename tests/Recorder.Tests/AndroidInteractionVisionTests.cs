using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Analysis;
using Recorder.Collectors.Android;
using Recorder.Contracts;

namespace Recorder.Tests;

public sealed class AndroidInteractionVisionTests
{
    [Fact]
    public void LiveDiscoveryUsesOnlyAnExplicitComplexVisionProfile()
    {
        var simple = VisionProfile("Simple fixture");
        var complex = VisionProfile("Complex fixture");
        var textOnly = VisionProfile("Text fixture") with { Capabilities = [ModelCapabilities.Text] };
        var settings = AnalysisSettings.Empty with { Profiles = [simple, complex, textOnly] };
        var models = ProjectModels.None.With(ModelUses.VisionChecks, simple.Name);
        var (profile, problem) = AndroidInteractionVision.SelectProfile(models, settings);
        Assert.Null(profile);
        Assert.Contains("simple model was not used", problem);
        Assert.Null(AndroidInteractionVision.SelectProfile(models.With(ModelUses.ComplexVisionChecks, ProjectModels.NoModel), settings).Profile);
        Assert.Contains("not offered", AndroidInteractionVision.SelectProfile(
            models.With(ModelUses.ComplexVisionChecks, "Missing fixture"), settings).Problem);
        Assert.Contains("cannot take images", AndroidInteractionVision.SelectProfile(
            models.With(ModelUses.ComplexVisionChecks, textOnly.Name), settings).Problem);

        var selected = AndroidInteractionVision.SelectProfile(models.With(ModelUses.ComplexVisionChecks, complex.Name), settings);
        Assert.Equal(complex, selected.Profile);
        Assert.Null(selected.Problem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestContainsOnePictureNoTreeAndProviderSpecificStructuredOutput(bool anthropic)
    {
        var profile = anthropic ? AnthropicSettings.ProjectProfile(AnthropicSettings.Models[0], "fixture") :
            VisionProfile("Fixture vision");
        var request = JsonNode.Parse(AndroidInteractionVision.Build(profile, [1, 2, 3], 40))!;
        Assert.Equal(profile.Model, request["model"]!.GetValue<string>());
        var messages = request["messages"]!.AsArray();
        var content = messages[^1]!["content"]!.AsArray();
        Assert.Equal("Identify likely touch targets in this Android app screenshot.", content[0]!["text"]!.GetValue<string>());
        Assert.Equal(3, content.Count);
        JsonNode? schema;
        if (anthropic)
        {
            Assert.Single(messages);
            Assert.Null(request["response_format"]);
            Assert.Equal("AQID", content[2]!["source"]!["data"]!.GetValue<string>());
            schema = request["output_config"]!["format"]!["schema"];
        }
        else
        {
            Assert.Equal(2, messages.Count);
            Assert.Null(request["output_config"]);
            Assert.Equal("data:image/png;base64,AQID", content[2]!["image_url"]!["url"]!.GetValue<string>());
            schema = request["response_format"]!["schema"];
        }
        Assert.False(schema!["additionalProperties"]!.GetValue<bool>());
        Assert.Equal("targets", schema["required"]![0]!.GetValue<string>());
        Assert.Equal("integer", schema["properties"]!["targets"]!["items"]!["properties"]!["box"]!["items"]!["type"]!.GetValue<string>());
        Assert.DoesNotContain("known rectangles", request.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        var instructions = (anthropic ? request["system"] : messages[0]!["content"])!.GetValue<string>();
        Assert.Contains("Android app on a phone", instructions, StringComparison.Ordinal);
        Assert.Contains("using the app by touch", instructions, StringComparison.Ordinal);
        Assert.Contains("Count distinct actions, not visual parts", instructions, StringComparison.Ordinal);
        Assert.Contains("compare your boxes for nesting and shared pixels", instructions, StringComparison.Ordinal);
        Assert.Contains("Repeated controls at different locations", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticPreservesOverlappingAndRepeatedProposalsInModelOrder()
    {
        var proposals = AndroidVisionProposals.Parse("""
            {"targets":[{"label":"Icon","box":[250,900,350,950]},{"label":" Whole surface ","box":[200,800,400,975]},
                        {"label":"Icon","box":[250,900,350,950]},{"label":"Near edge","box":[999,0,1000,1]}]}
            """);
        Assert.NotNull(proposals);
        Assert.Equal(4, proposals.Count);
        Assert.Equal(" Whole surface ", proposals[1].Label);
        Assert.Equal(new AndroidNodeBounds(200, 800, 400, 975), proposals[1].NormalizedBox);
        Assert.Equal(proposals[0], proposals[2]);
        Assert.Equal(new AndroidNodeBounds(99, 0, 100, 1), proposals[3].ImageBounds(100, 400));
        Assert.Empty(AndroidVisionProposals.Parse("""{"targets":[]}""")!);
    }

    [Theory]
    [InlineData("""{"targets":[{"label":"Tool","box":[200,100,900,400],"command":"tap"}]}""")]
    [InlineData("""{"targets":[{"label":"Tool","box":[200,100,1001,400]}]}""")]
    [InlineData("""{"targets":[{"label":"Tool","box":[200,100,900,400],"label":"Repeated"}]}""")]
    [InlineData("""{"targets":[{"label":"Tool","box":[200,100,200,400]}]}""")]
    [InlineData("""{"targets":[""")]
    public void RejectsMalformedAndNonLocationOutput(string output) => Assert.Null(AndroidVisionProposals.Parse(output));

    [Fact]
    public void RejectsOutputBeyondTheProposalLimit() =>
        Assert.Null(AndroidVisionProposals.Parse(JsonSerializer.Serialize(new
        {
            targets = Enumerable.Repeat(new { label = "Tool", box = new[] { 200, 100, 900, 400 } },
                AndroidVisionProposals.Limit + 1)
        })));

    [Theory]
    [InlineData("stop", null, true)]
    [InlineData("end_turn", null, true)]
    [InlineData("length", null, false)]
    [InlineData("max_tokens", null, false)]
    [InlineData("refusal", null, false)]
    [InlineData("stop", "Stopped", false)]
    public void RejectsIncompleteOrRefusedAnswers(string finish, string? stopped, bool expected) =>
        Assert.Equal(expected, AndroidInteractionVision.Usable(new("""{"targets":[]}""", finish, null, null, null, null, stopped)));

    [Fact]
    public async Task LiveUseDoesNotTakeOverAnOwnedServer()
    {
        var directory = Directory.CreateTempSubdirectory("fixture-model-");
        var token = TestContext.Current.CancellationToken;
        try
        {
            using (await LocalServerHost.AcquireAsync(directory.FullName, wait: false, token))
            {
                await Assert.ThrowsAsync<ModelServerBusyException>(() =>
                    LocalServerHost.AcquireAsync(directory.FullName, wait: false, token));
            }
            using var next = await LocalServerHost.AcquireAsync(directory.FullName, wait: false, token);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static ModelProfile VisionProfile(string name) => new(name, new("http://127.0.0.1:8100"), "fixture-vision",
        [ModelCapabilities.Image], new(null, null, null, null), null, GenerationSettings.Default, null);
}

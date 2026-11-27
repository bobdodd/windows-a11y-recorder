using System.Text.Json.Nodes;
using Recorder.Analysis;

namespace Recorder.Tests;

public sealed class AndroidInteractionVisionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestContainsOnePictureNoTreeAndProviderSpecificStructuredOutput(bool anthropic)
    {
        var profile = anthropic ? AnthropicSettings.ProjectProfile(AnthropicSettings.Models[0], "fixture") :
            new ModelProfile("Fixture vision", new("http://127.0.0.1:8100"), "fixture-vision",
                [ModelCapabilities.Image], new(null, null, null, null), null, GenerationSettings.Default, null);
        var request = JsonNode.Parse(AndroidInteractionVision.Build(profile, [1, 2, 3], 40))!;
        Assert.Equal(profile.Model, request["model"]!.GetValue<string>());
        var messages = request["messages"]!.AsArray();
        var content = messages[^1]!["content"]!.AsArray();
        Assert.Equal("Identify the apparent controls in this app viewport.", content[0]!["text"]!.GetValue<string>());
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
    }

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
}

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Recorder.Analysis;
using Recorder.Analysis.Setup;

namespace Recorder.Tests;

/// <summary>
/// The player's side of a Claude profile (cloud-models.md, "6. The
/// player"): the profile it writes, its prices, "Check the profile", the
/// label "(sends to Anthropic)", and the line of tokens and cost.
/// </summary>
public sealed class ClaudeProfileTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string LocalSettings = """
        { "version": 1, "localServer": { "executable": "C:\\llama\\llama-server.exe", "port": 8080 },
          "profiles": [ { "name": "Qwen", "endpoint": "http://127.0.0.1:8080", "model": "qwen", "capabilities": ["text"],
                          "local": { "modelPath": "C:\\models\\qwen.gguf", "arguments": "" } } ],
          "defaults": { "text": "Qwen" } }
        """;

    private static JsonObject Opus(string? effort = null, ServicePrices? prices = null) =>
        ModelSetup.ClaudeProfile("Claude Opus 5.5", "claude-opus-5-5", effort, 32000, prices ?? AnthropicSettings.Model("claude-opus-5-5")!.Prices, "protected-key");

    [Fact]
    public void A_Claude_profile_written_by_the_player_is_read_back_as_one_that_sends_to_Anthropic_and_is_not_made_a_default()
    {
        var text = ModelSetup.WithClaudeProfile(LocalSettings, Opus("high"));
        var (settings, problems) = AnalysisSettingsFile.Parse(text);

        Assert.Empty(problems);
        var profile = settings.Profile("Claude Opus 5.5")!;
        Assert.True(profile.SendsToAnthropic);
        Assert.Equal(EndpointPolicy.AnthropicEndpoint, profile.Endpoint);
        Assert.Equal("claude-opus-5-5", profile.Model);
        Assert.Equal([ModelCapabilities.Text, ModelCapabilities.Image], profile.Capabilities);
        Assert.Equal("high", profile.Effort);
        Assert.Equal(32000, profile.Generation.MaxTokens);
        Assert.Equal(new ServicePrices(4, 20), profile.Prices);
        Assert.Equal("protected-key", profile.ProtectedApiKey);
        Assert.Equal(new Dictionary<string, string> { [ModelCapabilities.Text] = "Qwen" }, settings.Defaults);
        Assert.Equal("Qwen", AnalysisPlayer.DefaultProfile(settings, ModelCapabilities.Text)!.Name);
    }

    [Fact]
    public void A_Claude_profile_with_the_name_of_another_is_not_added()
    {
        var text = ModelSetup.WithClaudeProfile(LocalSettings, Opus());

        var failure = Assert.Throws<InvalidDataException>(() => ModelSetup.WithClaudeProfile(text, Opus()));

        Assert.Equal("There is already a profile named \"Claude Opus 5.5\".", failure.Message);
    }

    [Fact]
    public void Without_a_default_a_profile_that_sends_to_Anthropic_is_not_offered_first()
    {
        var text = ModelSetup.WithClaudeProfile(AnalysisPlayer.EmptySettings, Opus());
        var (settings, _) = AnalysisSettingsFile.Parse(text);

        Assert.Null(AnalysisPlayer.DefaultProfile(settings, ModelCapabilities.Text));
        Assert.Null(AnalysisPlayer.DefaultProfile(settings, ModelCapabilities.Image));

        var chosen = AnalysisPlayer.WithDefault(text, ModelCapabilities.Image, "Claude Opus 5.5");
        Assert.Equal("Claude Opus 5.5", AnalysisPlayer.DefaultProfile(AnalysisSettingsFile.Parse(chosen).Settings, ModelCapabilities.Image)!.Name);
    }

    [Fact]
    public void A_Claude_profiles_settings_are_changed_and_its_key_replaced_only_when_a_new_one_is_given()
    {
        var text = ModelSetup.WithClaudeProfile(LocalSettings, Opus("high"));

        var changed = ModelSetup.WithClaudeSettings(text, "Claude Opus 5.5", 64000, null, new ServicePrices(5, 25), null);
        var profile = AnalysisSettingsFile.Parse(changed).Settings.Profile("Claude Opus 5.5")!;
        Assert.Equal(64000, profile.Generation.MaxTokens);
        Assert.Null(profile.Effort);
        Assert.Equal(new ServicePrices(5, 25), profile.Prices);
        Assert.Equal("protected-key", profile.ProtectedApiKey);

        var rekeyed = ModelSetup.WithClaudeSettings(changed, "Claude Opus 5.5", 64000, "max", null, "new-key");
        profile = AnalysisSettingsFile.Parse(rekeyed).Settings.Profile("Claude Opus 5.5")!;
        Assert.Equal("max", profile.Effort);
        Assert.Null(profile.Prices);
        Assert.Equal("new-key", profile.ProtectedApiKey);
    }

    [Fact]
    public void A_Claude_profile_is_not_given_a_temperature_and_a_local_profile_no_effort()
    {
        var text = ModelSetup.WithClaudeProfile(LocalSettings, Opus());

        var claude = Assert.Throws<InvalidDataException>(() => ModelSetup.WithProfileSettings(text, "Claude Opus 5.5", "", 2048, 0, 1, false));
        var local = Assert.Throws<InvalidDataException>(() => ModelSetup.WithClaudeSettings(text, "Qwen", 2048, "high", null, null));

        Assert.Contains("sends to Anthropic, so it has no temperature", claude.Message, StringComparison.Ordinal);
        Assert.Equal("Profile \"Qwen\" does not send to Anthropic.", local.Message);
    }

    [Theory]
    [InlineData("""{ "inputPerMillion": 4 }""")]
    [InlineData("""{ "inputPerMillion": -1, "outputPerMillion": 20 }""")]
    [InlineData("""{ "inputPerMillion": "4", "outputPerMillion": 20 }""")]
    [InlineData("""{ "inputPerMillion": 0.1, "outputPerMillion": 0.5, "longPrompt": { "overTokens": 0, "inputPerMillion": 0.5, "outputPerMillion": 2.5 } }""")]
    [InlineData("""{ "inputPerMillion": 0.1, "outputPerMillion": 0.5, "longPrompt": { "inputPerMillion": 0.5, "outputPerMillion": 2.5 } }""")]
    public void Prices_that_are_not_prices_are_reported(string prices)
    {
        var profile = Opus();
        profile["prices"] = JsonNode.Parse(prices);
        var settings = new JsonObject { ["version"] = 1, ["profiles"] = new JsonArray(profile) };

        var (read, problems) = AnalysisSettingsFile.Parse(settings.ToJsonString());

        Assert.Empty(read.Profiles);
        Assert.Contains("gives prices that are not inputPerMillion and outputPerMillion", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_profile_that_sends_to_Anthropic_has_prices()
    {
        var settings = JsonNode.Parse(LocalSettings)!.AsObject();
        settings["profiles"]![0]!["prices"] = new ServicePrices(1, 2).ToJson();

        var (_, problems) = AnalysisSettingsFile.Parse(settings.ToJsonString());

        Assert.Equal("Profile \"Qwen\" gives prices, which only a profile that sends to Anthropic has.", problems[0]);
    }

    [Fact]
    public void A_cost_is_the_tokens_at_the_prices_with_the_long_prompt_prices_over_their_threshold()
    {
        var haiku = AnthropicSettings.Model("claude-haiku-5-5")!.Prices;

        Assert.Equal(((1_000_000 * 4.0) + (100_000 * 20.0)) / 1_000_000, new ServicePrices(4, 20).Cost(1_000_000, 100_000), 12);
        Assert.Equal(((100_000 * 0.10) + (1_000 * 0.50)) / 1_000_000, haiku.Cost(100_000, 1_000), 12);
        Assert.Equal(((100_001 * 0.50) + (1_000 * 2.50)) / 1_000_000, haiku.Cost(100_001, 1_000), 12);
        Assert.Equal(haiku, ServicePrices.FromJson(JsonNode.Parse(haiku.ToJson().ToJsonString())));
    }

    [Fact]
    public void The_profile_recorded_with_an_attempt_holds_its_prices_and_not_its_key()
    {
        var profile = AnalysisSettingsFile.Parse(ModelSetup.WithClaudeProfile(LocalSettings, Opus())).Settings.Profile("Claude Opus 5.5")!;

        var described = AnalysisSettingsFile.Describe(profile);

        Assert.Equal(new ServicePrices(4, 20), ServicePrices.FromJson(described["prices"]));
        Assert.DoesNotContain("protected-key", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_offered_models_are_Anthropics_current_three_with_their_prices_of_2026_11_11()
    {
        Assert.Equal(
            ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"],
            AnthropicSettings.Models.Select(model => model.Id));
        Assert.Equal(new ServicePrices(2, 10), AnthropicSettings.Model("claude-sonnet-5-5")!.Prices);
        Assert.Null(AnthropicSettings.Model("claude-fable-5-2"));
    }

    [Fact]
    public void A_profile_that_sends_to_Anthropic_is_labelled_so_wherever_it_is_chosen()
    {
        var (settings, _) = AnalysisSettingsFile.Parse(ModelSetup.WithClaudeProfile(LocalSettings, Opus()));

        Assert.Equal("Claude Opus 5.5 (sends to Anthropic)", AnalysisPlayer.ProfileLabel(settings.Profile("Claude Opus 5.5")!));
        Assert.Equal("Qwen", AnalysisPlayer.ProfileLabel(settings.Profile("Qwen")!));
    }

    [Fact]
    public void A_tasks_usage_line_gives_its_tokens_and_cost_or_why_there_is_no_cost()
    {
        using var culture = new CultureScope();

        Assert.Equal(
            "Sent to Anthropic: 3 attempts, 123,456 input and 7,890 output tokens, about US$0.65 at the profile's prices. " +
            "Anthropic's bill is the record of what was charged.",
            AnalysisPlayer.UsageLine(new ServiceUsage(3, 123_456, 7_890, 0.651624, 0)));
        Assert.Equal(
            "Sent to Anthropic: 1 attempt, 120 input and 40 output tokens, about US$0.0013 at the profile's prices. " +
            "Anthropic's bill is the record of what was charged.",
            AnalysisPlayer.UsageLine(new ServiceUsage(1, 120, 40, 0.00128, 0)));
        Assert.Equal(
            "Sent to Anthropic: 1 attempt, 120 input and 40 output tokens. No cost estimate: the profile has no prices.",
            AnalysisPlayer.UsageLine(new ServiceUsage(1, 120, 40, null, 0)));
        Assert.Equal(
            "Sent to Anthropic: 2 attempts, 120 input and 40 output tokens. No cost estimate: 1 attempt has no token counts.",
            AnalysisPlayer.UsageLine(new ServiceUsage(2, 120, 40, null, 1)));
    }

    [Fact]
    public void The_check_asks_one_short_question_with_the_profiles_model_and_effort_and_no_recording()
    {
        var profile = AnalysisSettingsFile.Parse(ModelSetup.WithClaudeProfile(LocalSettings, Opus("low"))).Settings.Profile("Claude Opus 5.5")!;

        var body = JsonNode.Parse(AnthropicMessages.CheckBody(profile))!.AsObject();

        Assert.Equal(
            """{"model":"claude-opus-5-5","max_tokens":1024,"messages":[{"role":"user","content":"Answer with the one word: ready."}],"output_config":{"effort":"low"}}""",
            body.ToJsonString());
        Assert.Null(JsonNode.Parse(AnthropicMessages.CheckBody(profile with { Effort = null }))!["output_config"]);
    }

    private sealed class Service(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return answer();
        }
    }

    private sealed class Protector : IApiKeyProtector
    {
        public string Protect(string apiKey) => "p:" + apiKey;

        public string Unprotect(string protectedApiKey) => protectedApiKey[2..];
    }

    private static ModelProfile Checked() =>
        AnalysisSettingsFile.Parse(ModelSetup.WithClaudeProfile(LocalSettings,
            ModelSetup.ClaudeProfile("Claude Opus 5.5", "claude-opus-5-5", null, 32000, new ServicePrices(4, 20), "p:sk-ant-test"))).Settings.Profile("Claude Opus 5.5")!;

    [Fact]
    public async Task The_check_is_sent_to_Anthropic_with_the_key_and_its_answer_shown()
    {
        var service = new Service(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[{"type":"text","text":"Ready."}],"stop_reason":"end_turn","usage":{"input_tokens":16,"output_tokens":4}}""",
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.Add("request-id", "req_check");
            return response;
        });
        using var culture = new CultureScope();

        var response = await new HttpModelEndpoint(new HttpClient(service), new Protector()).CheckAsync(Checked(), Token);

        var (request, body) = Assert.Single(service.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("sk-ant-test", request.Headers.Authorization!.Parameter);
        Assert.Equal(AnthropicMessages.CheckBody(Checked()), body);
        Assert.Equal(
            "Anthropic answered with model claude-opus-5-5, 16 input and 4 output tokens, about US$0.0001, stop reason end_turn, " +
            "request req_check. The answer: Ready.",
            AnalysisPlayer.CheckLine(Checked(), response));
    }

    [Fact]
    public async Task The_check_is_not_asked_again_when_Anthropic_is_busy_and_says_what_it_was_told()
    {
        var service = new Service(() => new HttpResponseMessage((HttpStatusCode)529)
        {
            Content = new StringContent("""{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""", Encoding.UTF8, "application/json")
        });

        var failure = await Assert.ThrowsAsync<ModelEndpointException>(
            () => new HttpModelEndpoint(new HttpClient(service), new Protector(), busyWait: TimeSpan.FromMilliseconds(1)).CheckAsync(Checked(), Token));

        Assert.Single(service.Requests);
        Assert.StartsWith("Anthropic answered 529", failure.Message, StringComparison.Ordinal);
        Assert.Contains("overloaded_error: Overloaded", failure.Message, StringComparison.Ordinal);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}

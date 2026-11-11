using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Analysis;

namespace Recorder.Tests;

/// <summary>
/// Profiles that send to Anthropic: the settings file's rules, the one
/// outside address, the Messages request, and the endpoint against a
/// stand-in for Anthropic's API, so that no key or network is needed. See
/// docs/architecture/cloud-models.md.
/// </summary>
public sealed class AnthropicTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Settings(string profile) =>
        $$"""{ "version": 1, "profiles": [ {{profile}} ] }""";

    private const string ClaudeProfile = """
        { "name": "Claude Opus", "provider": "anthropic", "endpoint": "https://api.anthropic.com", "model": "claude-opus-5-5",
          "capabilities": ["text", "image"], "effort": "high", "protectedApiKey": "p:sk-ant-test" }
        """;

    [Fact]
    public void A_Claude_profile_is_read_with_its_provider_effort_and_default_output_limit()
    {
        var (settings, problems) = AnalysisSettingsFile.Parse(Settings(ClaudeProfile));

        Assert.Empty(problems);
        var profile = Assert.Single(settings.Profiles);
        Assert.True(profile.SendsToAnthropic);
        Assert.Equal(ModelProviders.Anthropic, profile.Provider);
        Assert.Equal("high", profile.Effort);
        Assert.Equal(AnthropicSettings.DefaultMaxTokens, profile.Generation.MaxTokens);
        var described = AnalysisSettingsFile.Describe(profile);
        Assert.Equal("anthropic", (string?)described["provider"]);
        Assert.Equal("high", (string?)described["effort"]);
        Assert.Null(described["file"]);
        Assert.Equal("""{"maxTokens":32000}""", described["generation"]!.ToJsonString());
        Assert.DoesNotContain("sk-ant", described.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_without_a_provider_is_openai_compatible_as_before()
    {
        var (settings, problems) = AnalysisSettingsFile.Parse(Settings(
            """{ "name": "LAN", "endpoint": "http://192.168.1.50:8080", "model": "qwen", "capabilities": ["text"] }"""));

        Assert.Empty(problems);
        Assert.Equal(ModelProviders.OpenAiCompatible, settings.Profiles[0].Provider);
        Assert.False(settings.Profiles[0].SendsToAnthropic);
        Assert.Equal(2048, settings.Profiles[0].Generation.MaxTokens);
    }

    [Theory]
    [InlineData("\"endpoint\": \"https://api.anthropic.com/v1\"", "must have the endpoint https://api.anthropic.com/")]
    [InlineData("\"endpoint\": \"http://api.anthropic.com\"", "must have the endpoint https://api.anthropic.com/")]
    [InlineData("\"endpoint\": \"https://api.anthropic.com:8443\"", "must have the endpoint https://api.anthropic.com/")]
    [InlineData("\"endpoint\": \"https://example.com\"", "must have the endpoint https://api.anthropic.com/")]
    [InlineData("\"endpoint\": \"http://127.0.0.1:8080\"", "must have the endpoint https://api.anthropic.com/")]
    [InlineData("\"generation\": { \"temperature\": 0 }", "takes no temperature setting")]
    [InlineData("\"generation\": { \"seed\": 1 }", "takes no seed setting")]
    [InlineData("\"generation\": { \"thinking\": true }", "takes no thinking setting")]
    [InlineData("\"generation\": { \"sampling\": { \"top_p\": 0.9 } }", "takes no sampling setting")]
    [InlineData("\"file\": { \"name\": \"model.gguf\" }", "has no model file")]
    [InlineData("\"local\": { \"modelPath\": \"C:\\\\model.gguf\" }", "cannot be served by this PC")]
    [InlineData("\"effort\": \"extreme\"", "gives an effort that is not low, medium, high, xhigh, max")]
    [InlineData("\"protectedApiKey\": null", "needs an API key")]
    public void A_Claude_profile_that_breaks_a_rule_is_reported_not_used(string change, string problem)
    {
        var profile = JsonNode.Parse(ClaudeProfile)!.AsObject();
        foreach (var (name, value) in JsonNode.Parse("{" + change + "}")!.AsObject())
        {
            profile[name] = value?.DeepClone();
        }

        var (settings, problems) = AnalysisSettingsFile.Parse(Settings(profile.ToJsonString()));

        Assert.Empty(settings.Profiles);
        Assert.Contains(problems, given => given.Contains(problem, StringComparison.Ordinal));
    }

    [Fact]
    public void Only_a_Claude_profile_has_an_effort_and_providers_are_known()
    {
        var (_, effort) = AnalysisSettingsFile.Parse(Settings(
            """{ "name": "LAN", "endpoint": "http://192.168.1.50:8080", "model": "qwen", "capabilities": ["text"], "effort": "high" }"""));
        var (_, provider) = AnalysisSettingsFile.Parse(Settings(
            """{ "name": "Other", "provider": "openai", "endpoint": "https://api.openai.com", "model": "gpt", "capabilities": ["text"] }"""));

        Assert.Contains(effort, given => given.Contains("only a profile that sends to Anthropic", StringComparison.Ordinal));
        Assert.Contains(provider, given => given.Contains("names a provider that is not openai-compatible or anthropic", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://api.anthropic.com", "anthropic", true)]
    [InlineData("https://api.anthropic.com/", "anthropic", true)]
    [InlineData("https://API.anthropic.com:443/", "anthropic", true)]
    [InlineData("https://api.anthropic.com/v1/messages", "anthropic", false)]
    [InlineData("https://api.anthropic.com/?x=1", "anthropic", false)]
    [InlineData("https://user@api.anthropic.com", "anthropic", false)]
    [InlineData("http://api.anthropic.com", "anthropic", false)]
    [InlineData("https://api.anthropic.com.example.com", "anthropic", false)]
    [InlineData("https://8.8.8.8", "openai-compatible", false)]
    [InlineData("http://192.168.1.50:8080", "openai-compatible", true)]
    public void Anthropics_address_is_the_one_outside_address_and_only_for_its_provider(string endpoint, string provider, bool allowed) =>
        Assert.Equal(allowed, EndpointPolicy.Refusal(new Uri(endpoint), provider) is null);

    [Fact]
    public async Task Anthropics_address_on_another_provider_is_refused_once_resolved()
    {
        // An address literal is not looked up, so no network is needed:
        // Anthropic's address is outside the local network like any other.
        Assert.NotNull(await EndpointPolicy.RefusalAfterResolvingAsync(new Uri("https://160.79.104.10"), ModelProviders.OpenAiCompatible, Token));
        Assert.Null(await EndpointPolicy.RefusalAfterResolvingAsync(EndpointPolicy.AnthropicEndpoint, ModelProviders.Anthropic, Token));
    }

    private static ModelProfile Claude(string? effort = "high") =>
        AnalysisSettingsFile.Parse(Settings(ClaudeProfile)).Settings.Profiles[0] with { Effort = effort };

    private static ModelRequest Request(params ModelImage[] images) =>
        new("stand-in-template", 3, "Describe.", "E1: a fact.", images, ["description"], new HashSet<string> { "E1", "E2" })
        {
            Forms = ["heading", "sentence"]
        };

    [Fact]
    public void A_Claude_request_is_a_messages_request_with_the_step_schema_closed_and_no_sampling()
    {
        var body = ChatRequest.Build(Claude(), Request(new ModelImage("E2", "image/png", [1, 2, 3])));
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(["model", "max_tokens", "system", "messages", "output_config", "stream"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("claude-opus-5-5", root.GetProperty("model").GetString());
        Assert.Equal(32000, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("Describe.", root.GetProperty("system").GetString());
        Assert.True(root.GetProperty("stream").GetBoolean());
        var message = Assert.Single(root.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        var content = message.GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(3, content.Count);
        Assert.Equal("E1: a fact.", content[0].GetProperty("text").GetString());
        Assert.Equal("Picture E2:", content[1].GetProperty("text").GetString());
        Assert.Equal("image", content[2].GetProperty("type").GetString());
        Assert.Equal("base64", content[2].GetProperty("source").GetProperty("type").GetString());
        Assert.Equal("image/png", content[2].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), content[2].GetProperty("source").GetProperty("data").GetString());

        var output = root.GetProperty("output_config");
        Assert.Equal("high", output.GetProperty("effort").GetString());
        Assert.Equal("json_schema", output.GetProperty("format").GetProperty("type").GetString());
        var schema = JsonNode.Parse(output.GetProperty("format").GetProperty("schema").GetRawText())!;
        var objects = Objects(schema).ToList();
        Assert.Equal(2, objects.Count);
        Assert.All(objects, item => Assert.False((bool)item["additionalProperties"]!));
        Assert.DoesNotContain("minLength", schema.ToJsonString(), StringComparison.Ordinal);
        var statement = schema["properties"]!["description"]!["items"]!["items"]!;
        Assert.Equal(["form", "text", "kind", "evidence"], statement["properties"]!.AsObject().Select(property => property.Key));

        foreach (var refused in new[] { "temperature", "seed", "top_p", "top_k", "cache_prompt", "chat_template_kwargs", "response_format" })
        {
            Assert.False(root.TryGetProperty(refused, out _), refused);
        }
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        if (node is JsonObject item)
        {
            if ((string?)item["type"] == "object")
            {
                yield return item;
            }

            foreach (var (_, value) in item)
            {
                foreach (var found in Objects(value))
                {
                    yield return found;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var value in array)
            {
                foreach (var found in Objects(value))
                {
                    yield return found;
                }
            }
        }
    }

    [Fact]
    public void A_Claude_request_without_an_effort_takes_the_models_default_and_records_its_settings()
    {
        var profile = Claude(effort: null);
        using var document = JsonDocument.Parse(ChatRequest.Build(profile, Request()));
        var parameters = ChatRequest.Parameters(profile, Request());

        Assert.False(document.RootElement.GetProperty("output_config").TryGetProperty("effort", out _));
        Assert.Equal(
            """{"provider":"anthropic","anthropic_version":"2026-10-21","max_tokens":32000,"effort":null,"output_format":"json_schema","sections":["description"],"images":0}""",
            parameters.ToJsonString());
        Assert.False(AnalysisWorker.Repeatable(profile));
        Assert.Throws<ArgumentException>(() => ChatRequest.Build(profile, Request(), oneLine: true));
    }

    /// <summary>A stand-in for Anthropic's API: each request is kept, and answered as the test scripts it.</summary>
    private sealed class Service(params Func<HttpRequestMessage, HttpResponseMessage>[] answers) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return answers[Math.Min(Requests.Count, answers.Length) - 1](request);
        }
    }

    private sealed class Protector : IApiKeyProtector
    {
        public string Protect(string apiKey) => "p:" + apiKey;

        public string Unprotect(string protectedApiKey) => protectedApiKey[2..];
    }

    private static string Event(string name, string data) => $"event: {name}\ndata: {data}\n\n";

    // A streamed answer as Anthropic's streaming documentation shows one.
    private static string Stream(string stopReason = "end_turn", bool finished = true, params string[] pieces)
    {
        var text = new StringBuilder();
        text.Append(Event("message_start",
            """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","content":[],"model":"claude-opus-5-5","stop_reason":null,"usage":{"input_tokens":2040,"output_tokens":1}}}"""));
        text.Append(Event("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}"""));
        text.Append(Event("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Cite E1."}}"""));
        text.Append(Event("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"EqQB"}}"""));
        text.Append(Event("content_block_stop", """{"type":"content_block_stop","index":0}"""));
        text.Append(Event("ping", """{"type":"ping"}"""));
        text.Append(Event("content_block_start", """{"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}"""));
        foreach (var piece in pieces.Length == 0 ? ["{\"description\": ", "[]}"] : pieces)
        {
            text.Append(Event("content_block_delta", new JsonObject
            {
                ["type"] = "content_block_delta",
                ["index"] = 1,
                ["delta"] = new JsonObject { ["type"] = "text_delta", ["text"] = piece }
            }.ToJsonString()));
        }

        text.Append(Event("content_block_stop", """{"type":"content_block_stop","index":1}"""));
        if (finished)
        {
            text.Append(Event("message_delta", $$$"""{"type":"message_delta","delta":{"stop_reason":"{{{stopReason}}}","stop_sequence":null},"usage":{"output_tokens":256}}"""));
            text.Append(Event("message_stop", """{"type":"message_stop"}"""));
        }

        return text.ToString();
    }

    private static HttpResponseMessage Streamed(string stream)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8, "text/event-stream") };
        response.Headers.Add("request-id", "req_011CSHoEeqs5C35K2UUqR7Fy");
        return response;
    }

    private static HttpResponseMessage Error(int status, string type, string message, string? retryAfter = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent($$"""{"type":"error","error":{"type":"{{type}}","message":"{{message}}"},"request_id":"req_2"}""", Encoding.UTF8, "application/json")
        };
        response.Headers.Add("request-id", "req_2");
        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = RetryConditionHeaderValue.Parse(retryAfter);
        }

        return response;
    }

    private static HttpModelEndpoint Endpoint(Service service) =>
        new(new HttpClient(service), new Protector(), busyWait: TimeSpan.FromMilliseconds(1));

    [Fact]
    public async Task A_Claude_request_is_sent_as_built_to_Anthropic_and_its_streamed_answer_read()
    {
        var service = new Service(_ => Streamed(Stream()));
        var body = ChatRequest.Build(Claude(), Request());

        var response = await Endpoint(service).CompleteAsync(Claude(), body, Token);

        var (request, sent) = Assert.Single(service.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://api.anthropic.com/v1/messages"), request.RequestUri);
        Assert.Equal(body, sent);
        Assert.Equal("Bearer sk-ant-test", request.Headers.Authorization?.ToString());
        Assert.Equal(["2026-10-21"], request.Headers.GetValues("anthropic-version"));
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("{\"description\": []}", response.Content);
        Assert.Equal("end_turn", response.FinishReason);
        Assert.Equal(2040, response.PromptTokens);
        Assert.Equal(256, response.CompletionTokens);
        Assert.Equal("Cite E1.", response.Reasoning);
        Assert.Equal("req_011CSHoEeqs5C35K2UUqR7Fy", response.RequestId);
        Assert.Equal("claude-opus-5-5", response.ResponseModel);
        Assert.NotNull(response.PromptMilliseconds);
        Assert.NotNull(response.CompletionMilliseconds);
    }

    [Fact]
    public async Task A_Claude_answer_is_watched_as_it_arrives_and_stopped_where_the_watch_says()
    {
        var service = new Service(_ => Streamed(Stream("end_turn", true, "{\"description\": [[", "{\"text\": \"A\"}", "{\"text\": \"B\"}")));
        var seen = new List<string>();

        var response = await Endpoint(service).CompleteAsync(Claude(), "{}", text =>
        {
            seen.Add(text);
            return text.Contains("\"A\"", StringComparison.Ordinal) ? "a loop" : null;
        }, Token);

        Assert.Equal(["{\"description\": [[", "{\"description\": [[{\"text\": \"A\"}"], seen);
        Assert.Equal("a loop", response.StoppedBecause);
        Assert.Null(response.FinishReason);
        Assert.Equal(2040, response.PromptTokens);
        Assert.Equal("Cite E1.", response.Reasoning);
    }

    [Fact]
    public async Task A_busy_Anthropic_is_asked_again_three_times_after_waiting()
    {
        var service = new Service(
            _ => Error(529, "overloaded_error", "Overloaded"),
            _ => Error(429, "rate_limit_error", "Too many requests", retryAfter: "0"),
            _ => Error(500, "api_error", "Internal error"),
            _ => Streamed(Stream()));

        var response = await Endpoint(service).CompleteAsync(Claude(), "{}", Token);

        Assert.Equal(4, service.Requests.Count);
        Assert.Equal("{\"description\": []}", response.Content);
    }

    [Fact]
    public async Task Anthropic_still_busy_after_three_more_tries_fails_saying_what_it_was_told()
    {
        var service = new Service(_ => Error(529, "overloaded_error", "Overloaded"));

        var failure = await Assert.ThrowsAsync<ModelEndpointException>(() => Endpoint(service).CompleteAsync(Claude(), "{}", Token));

        Assert.Equal(4, service.Requests.Count);
        Assert.False(failure.Refused);
        Assert.StartsWith("Anthropic did not answer after 4 tries: Anthropic answered 529", failure.Message, StringComparison.Ordinal);
        Assert.Contains("overloaded_error: Overloaded (request req_2)", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(400, "invalid_request_error", "temperature is not supported")]
    [InlineData(401, "authentication_error", "invalid x-api-key")]
    [InlineData(403, "permission_error", "Your API key does not have permission")]
    public async Task A_request_or_key_Anthropic_refuses_is_not_sent_again(int status, string type, string message)
    {
        var service = new Service(_ => Error(status, type, message));

        var failure = await Assert.ThrowsAsync<ModelEndpointException>(() => Endpoint(service).CompleteAsync(Claude(), "{}", Token));

        Assert.Single(service.Requests);
        Assert.True(failure.Refused);
        Assert.Contains($"Anthropic answered {status}", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"{type}: {message} (request req_2)", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_error_part_way_through_the_answer_or_an_answer_cut_off_fails_the_attempt()
    {
        var withError = Stream(finished: false) + Event("error", """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""");
        var errored = await Assert.ThrowsAsync<ModelEndpointException>(() =>
            Endpoint(new Service(_ => Streamed(withError))).CompleteAsync(Claude(), "{}", Token));
        var cut = await Assert.ThrowsAsync<ModelEndpointException>(() =>
            Endpoint(new Service(_ => Streamed(Stream(finished: false)))).CompleteAsync(Claude(), "{}", Token));

        Assert.False(errored.Refused);
        Assert.Contains("part way through the answer: overloaded_error: Overloaded", errored.Message, StringComparison.Ordinal);
        Assert.StartsWith("Anthropic's answer ended before it was finished.", cut.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_and_the_output_limit_are_given_as_Anthropics_stop_reasons()
    {
        var refused = await Endpoint(new Service(_ => Streamed(Stream("refusal")))).CompleteAsync(Claude(), "{}", Token);
        var limited = await Endpoint(new Service(_ => Streamed(Stream("max_tokens")))).CompleteAsync(Claude(), "{}", Token);

        Assert.Equal("refusal", refused.FinishReason);
        Assert.Equal("max_tokens", limited.FinishReason);
    }

    [Fact]
    public async Task Nothing_is_asked_of_Anthropic_but_the_request()
    {
        var service = new Service(_ => throw new InvalidOperationException("No request was expected."));
        var endpoint = Endpoint(service);

        Assert.Equal("anthropic-version 2026-10-21", await endpoint.ServerBuildAsync(Claude(), Token));
        Assert.Null(await endpoint.ServerSamplingAsync(Claude(), Token));
        await endpoint.LoadAsync(Claude(), Token);
        await endpoint.UnloadAsync(Claude(), Token);

        Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task A_Claude_profile_that_names_another_address_is_not_sent()
    {
        var service = new Service(_ => Streamed(Stream()));
        var elsewhere = Claude() with { Endpoint = new Uri("https://api.anthropic.com.example.com") };

        var failure = await Assert.ThrowsAsync<ModelEndpointException>(() => Endpoint(service).CompleteAsync(elsewhere, "{}", Token));

        Assert.Empty(service.Requests);
        Assert.Contains("must have the endpoint https://api.anthropic.com/", failure.Message, StringComparison.Ordinal);
    }
}

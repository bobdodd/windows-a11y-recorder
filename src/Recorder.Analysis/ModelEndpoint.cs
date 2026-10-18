using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis;

/// <summary>A picture given to a model, with its evidence reference.</summary>
public sealed record ModelImage(string Reference, string MediaType, byte[] Content);

/// <summary>
/// One request a step makes: its prompt template and version, the
/// instructions, the input, the pictures, the sections the output must
/// have, and the evidence references the input contains.
/// </summary>
public sealed record ModelRequest(
    string PromptTemplate,
    int PromptTemplateVersion,
    string Instructions,
    string Input,
    IReadOnlyList<ModelImage> Images,
    IReadOnlyList<string> Sections,
    IReadOnlySet<string> InputReferences)
{
    public string Capability => Images.Count > 0 ? ModelCapabilities.Image : ModelCapabilities.Text;

    /// <summary>
    /// The forms a statement may take, where the step's answer gives each
    /// statement a form, or null where it does not, as for every step but
    /// those of the report's second chapter (recording-summary.md, "The
    /// layered report", "Chapter 2's form, as built").
    /// </summary>
    public IReadOnlyList<string>? Forms { get; init; }

    /// <summary>
    /// The profile's sampling options left out of this step's request. The
    /// steps that write the report leave out the presence penalty, which
    /// is for the steps that describe a moment, where answers looped: on the
    /// target machine on 2026-10-17 the story of a visit, sent with it, came
    /// back with every section empty, and the same request without it gave
    /// 27 statements (owner, the same day).
    /// </summary>
    public IReadOnlySet<string> SamplingLeftOut { get; init; } = NoOptions;

    /// <summary>
    /// Whether the answer must hold at least one statement: an answer with
    /// none is refused, and recovered from as a loop is (owner, 2026-10-17).
    /// </summary>
    public bool NeedsStatements { get; init; }

    private static readonly IReadOnlySet<string> NoOptions = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// What the server answered. <see cref="StoppedBecause"/> is set where the
/// worker stopped the answer as it arrived, saying why; the answer is then
/// what had arrived, with no finish reason, token counts, or timings.
/// </summary>
public sealed record ModelResponse(
    string? Content,
    string? FinishReason,
    int? PromptTokens,
    int? CompletionTokens,
    double? PromptMilliseconds,
    double? CompletionMilliseconds,
    string? StoppedBecause = null);

/// <summary>A failure reported by, or in reaching, a model server.</summary>
public sealed class ModelEndpointException(string message, Exception? inner = null, bool refused = false) : Exception(message, inner)
{
    /// <summary>
    /// Whether the server refused the request itself, as with a request
    /// longer than its context: the same request would be refused again.
    /// </summary>
    public bool Refused { get; } = refused;
}

/// <summary>
/// A server the worker sends requests to: an OpenAI-compatible chat
/// completions endpoint, with llama.cpp's router requests for loading and
/// unloading a model and its properties request for the server's build.
/// </summary>
public interface IModelEndpoint
{
    Task<ModelResponse> CompleteAsync(ModelProfile profile, string body, CancellationToken cancellationToken);

    /// <summary>
    /// Sends the request, giving the output to <paramref name="watch"/> as
    /// it arrives; where the watch gives a reason, the answer is stopped
    /// there. An endpoint that cannot watch its output gives it whole.
    /// </summary>
    Task<ModelResponse> CompleteAsync(
        ModelProfile profile,
        string body,
        Func<string, string?>? watch,
        CancellationToken cancellationToken) =>
        CompleteAsync(profile, body, cancellationToken);

    /// <summary>
    /// The server's default sampling settings for the profile's model, as
    /// it reports them, or null where it does not say.
    /// </summary>
    Task<JsonObject?> ServerSamplingAsync(ModelProfile profile, CancellationToken cancellationToken) =>
        Task.FromResult<JsonObject?>(null);

    /// <summary>The server's build, or null where it does not say.</summary>
    Task<string?> ServerBuildAsync(ModelProfile profile, CancellationToken cancellationToken);

    Task LoadAsync(ModelProfile profile, CancellationToken cancellationToken);

    Task UnloadAsync(ModelProfile profile, CancellationToken cancellationToken);
}

/// <summary>Protects the API key a profile holds.</summary>
public interface IApiKeyProtector
{
    string Protect(string apiKey);

    string Unprotect(string protectedApiKey);
}

/// <summary>
/// Protects an API key for the signed-in Windows user with the Data
/// Protection API, as the catalog password is protected.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CurrentUserApiKeyProtector : IApiKeyProtector
{
    private static readonly byte[] Entropy = "windows-a11y-recorder/model-api-key"u8.ToArray();

    public string Protect(string apiKey) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedApiKey) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedApiKey), Entropy, DataProtectionScope.CurrentUser));
}

/// <summary>
/// Builds the body of a chat completions request. Every request asks for no
/// randomness beyond the profile's settings, a fixed seed, no reuse of the
/// prompt cache, and output held to the step's JSON schema. The body is
/// built once, hashed, and sent exactly as hashed.
/// </summary>
public static class ChatRequest
{
    /// <param name="oneLine">
    /// Whether the answer is held to JSON on one line by the recorder's own
    /// grammar, sent in place of the schema; for a trial only, until it is
    /// tried on the target machine (recording-summary.md, "The layered
    /// report").
    /// </param>
    public static string Build(ModelProfile profile, ModelRequest request, bool oneLine = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        var content = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = request.Input });
        foreach (var image in request.Images)
        {
            content.Add(new JsonObject { ["type"] = "text", ["text"] = $"Picture {image.Reference}:" });
            content.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject
                {
                    ["url"] = $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Content)}"
                }
            });
        }

        var body = new JsonObject
        {
            ["model"] = profile.Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.Instructions },
                new JsonObject { ["role"] = "user", ["content"] = content }),
            ["temperature"] = profile.Generation.Temperature,
            ["seed"] = profile.Generation.Seed,
            ["max_tokens"] = profile.Generation.MaxTokens,
            // Streamed, so that the worker can stop an answer that repeats
            // itself (analysis-runner.md, "Sampling options").
            ["stream"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true },
            ["cache_prompt"] = false,
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_object",
                ["schema"] = ClaimCheck.Schema(request.Sections, request.Forms)
            }
        };
        if (oneLine)
        {
            body.Remove("response_format");
            body["grammar"] = AnswerGrammar.For(request.Sections);
        }

        if (!profile.Generation.Thinking)
        {
            body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
        }

        foreach (var (option, value) in profile.Generation.Sampling)
        {
            body[option] = value;
        }

        return body.ToJsonString();
    }

    /// <summary>The generation settings as recorded with an attempt.</summary>
    public static JsonObject Parameters(ModelProfile profile, ModelRequest request)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        return new JsonObject
        {
            ["temperature"] = profile.Generation.Temperature,
            ["seed"] = profile.Generation.Seed,
            ["max_tokens"] = profile.Generation.MaxTokens,
            ["cache_prompt"] = false,
            ["thinking"] = profile.Generation.Thinking,
            ["sampling"] = AnalysisSettingsFile.GenerationSampling(profile.Generation),
            ["sections"] = new JsonArray([.. request.Sections.Select(section => JsonValue.Create(section))]),
            ["images"] = request.Images.Count
        };
    }

    public static string Sha256(string body) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
}

/// <summary>
/// The model endpoint over HTTP. Before each request, the endpoint's host is
/// resolved and refused if any address is outside the local network.
/// </summary>
/// <param name="loadTimeout">How long a locally served model may take to load; 15 minutes when null.</param>
/// <param name="loadPoll">How often its state is read while it loads; 2 seconds when null.</param>
public sealed class HttpModelEndpoint(
    HttpClient client,
    IApiKeyProtector? keys,
    TimeSpan? loadTimeout = null,
    TimeSpan? loadPoll = null) : IModelEndpoint
{
    /// <summary>How long a locally served model may take to load, unless set.</summary>
    public static readonly TimeSpan DefaultLoadTimeout = TimeSpan.FromMinutes(15);

    private readonly TimeSpan _loadTimeout = loadTimeout ?? DefaultLoadTimeout;
    private readonly TimeSpan _loadPoll = loadPoll ?? TimeSpan.FromSeconds(2);

    public Task<ModelResponse> CompleteAsync(ModelProfile profile, string body, CancellationToken cancellationToken) =>
        CompleteAsync(profile, body, null, cancellationToken);

    public async Task<ModelResponse> CompleteAsync(
        ModelProfile profile,
        string body,
        Func<string, string?>? watch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        using var response = await SendAsync(profile, HttpMethod.Post, "v1/chat/completions", body, cancellationToken)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            return await StreamedAsync(response, watch, cancellationToken).ConfigureAwait(false);
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ModelEndpointException(
                $"The model server answered {(int)response.StatusCode} {response.ReasonPhrase}: {Shorten(text)}",
                refused: response.StatusCode == System.Net.HttpStatusCode.BadRequest);
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var choice = root.GetProperty("choices")[0];
            var message = choice.GetProperty("message");
            var content = message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String
                ? contentElement.GetString()
                : null;
            var finish = choice.TryGetProperty("finish_reason", out var finishElement) && finishElement.ValueKind == JsonValueKind.String
                ? finishElement.GetString()
                : null;
            int? promptTokens = null, completionTokens = null;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                promptTokens = Int(usage, "prompt_tokens");
                completionTokens = Int(usage, "completion_tokens");
            }

            double? promptMs = null, completionMs = null;
            if (root.TryGetProperty("timings", out var timings) && timings.ValueKind == JsonValueKind.Object)
            {
                promptMs = Double(timings, "prompt_ms");
                completionMs = Double(timings, "predicted_ms");
            }

            return new ModelResponse(content, finish, promptTokens, completionTokens, promptMs, completionMs);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new ModelEndpointException($"The model server's answer is not a chat completion: {Shorten(text)}", exception);
        }
    }

    // A streamed answer: server-sent events, each "data: " and a chunk,
    // ending with "data: [DONE]". The content is the chunks' deltas in
    // order; the finish reason, usage, and timings come from the last
    // chunks (llama.cpp's server-task.cpp).
    private static async Task<ModelResponse> StreamedAsync(
        HttpResponseMessage response,
        Func<string, string?>? watch,
        CancellationToken cancellationToken)
    {
        var content = new StringBuilder();
        string? finish = null;
        int? promptTokens = null, completionTokens = null;
        double? promptMs = null, completionMs = null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data == "[DONE]")
            {
                break;
            }

            try
            {
                using var document = JsonDocument.Parse(data);
                var root = document.RootElement;
                if (root.TryGetProperty("error", out var error))
                {
                    throw new ModelEndpointException($"The model server reported an error: {Shorten(error.ToString())}");
                }

                var added = false;
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
                {
                    foreach (var choice in choices.EnumerateArray())
                    {
                        if (choice.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("content", out var piece) && piece.ValueKind == JsonValueKind.String)
                        {
                            content.Append(piece.GetString());
                            added = true;
                        }

                        if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                        {
                            finish = reason.GetString();
                        }
                    }
                }

                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    promptTokens = Int(usage, "prompt_tokens");
                    completionTokens = Int(usage, "completion_tokens");
                }

                if (root.TryGetProperty("timings", out var timings) && timings.ValueKind == JsonValueKind.Object)
                {
                    promptMs = Double(timings, "prompt_ms");
                    completionMs = Double(timings, "predicted_ms");
                }

                if (added && watch?.Invoke(content.ToString()) is { } stop)
                {
                    // Closing the response stops the server generating.
                    return new ModelResponse(content.ToString(), null, null, null, null, null, stop);
                }
            }
            catch (JsonException exception)
            {
                throw new ModelEndpointException($"The model server's answer is not a streamed chat completion: {Shorten(data)}", exception);
            }
        }

        return new ModelResponse(content.ToString(), finish, promptTokens, completionTokens, promptMs, completionMs);
    }

    public async Task<JsonObject?> ServerSamplingAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            using var response = await SendAsync(
                profile,
                HttpMethod.Get,
                "props?model=" + Uri.EscapeDataString(profile.Model) + "&autoload=false",
                null,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var settings = root?["default_generation_settings"];
            return (settings?["params"] ?? settings) is JsonObject found ? (JsonObject)found.DeepClone() : null;
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or ModelEndpointException)
        {
            return null;
        }
    }

    public async Task<string?> ServerBuildAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            using var response = await SendAsync(
                profile,
                HttpMethod.Get,
                "props?model=" + Uri.EscapeDataString(profile.Model) + "&autoload=false",
                null,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("build_info", out var build) &&
                build.ValueKind == JsonValueKind.String
                ? build.GetString()
                : null;
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or ModelEndpointException)
        {
            return null;
        }
    }

    public async Task LoadAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Local is null)
        {
            return;
        }

        using var response = await SendAsync(
            profile,
            HttpMethod.Post,
            "models/load",
            new JsonObject { ["model"] = profile.Model }.ToJsonString(),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new ModelEndpointException($"The model server did not load {profile.Model}: {Shorten(text)}");
        }

        // The router loads the model in the background and answers 503
        // "Loading model" to requests until it is loaded (target machine,
        // 2026-10-14), so the worker waits for the model's state in GET
        // /models to be "loaded" before any request. See
        // docs/architecture/analysis-runner.md, "Implementation status".
        var waited = System.Diagnostics.Stopwatch.StartNew();
        string? last = null;
        while (true)
        {
            var (state, failed, exitCode) = await ModelStateAsync(profile, cancellationToken).ConfigureAwait(false);
            if (state == "loaded")
            {
                return;
            }

            if (failed)
            {
                throw new ModelEndpointException(
                    $"The model server could not load {profile.Model}" +
                    (exitCode is { } code ? $"; its model process exited with code {code.ToString(CultureInfo.InvariantCulture)}." : ".") +
                    " Its log is llama-server.log in the analysis folder.");
            }

            last = state;
            if (waited.Elapsed >= _loadTimeout)
            {
                throw new ModelEndpointException(
                    $"The model server had not loaded {profile.Model} after {_loadTimeout.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes; its state was {last ?? "not given"}.");
            }

            await Task.Delay(_loadPoll, cancellationToken).ConfigureAwait(false);
        }
    }

    // A model's state in the router's list: its value, such as "loading" or
    // "loaded", whether loading failed, and the exit code if so.
    private async Task<(string? State, bool Failed, int? ExitCode)> ModelStateAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(profile, HttpMethod.Get, "models", null, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ModelEndpointException($"The model server's list of models could not be read: {(int)response.StatusCode} {Shorten(text)}");
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            foreach (var model in document.RootElement.GetProperty("data").EnumerateArray())
            {
                if (model.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == profile.Model)
                {
                    if (!model.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object)
                    {
                        return (null, false, null);
                    }

                    var value = status.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.String
                        ? valueElement.GetString()
                        : null;
                    var failed = status.TryGetProperty("failed", out var failedElement) && failedElement.ValueKind == JsonValueKind.True;
                    return (value, failed, Int(status, "exit_code"));
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ModelEndpointException($"The model server's list of models is not as expected: {Shorten(text)}", exception);
        }

        throw new ModelEndpointException($"The model server does not list {profile.Model}.");
    }

    public async Task UnloadAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Local is null)
        {
            return;
        }

        try
        {
            using var response = await SendAsync(
                profile,
                HttpMethod.Post,
                "models/unload",
                new JsonObject { ["model"] = profile.Model }.ToJsonString(),
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            // The router answers at once and stops the model's process in
            // the background; until it has exited the model counts against
            // the server's limit of one model, and a load is refused with
            // "model limit reached, try again later" (target machine,
            // 2026-10-16; llama.cpp b11607, tools/server/server-models.cpp,
            // server_models::unload and server_models::load). So the worker
            // waits for the model to be listed as no longer running.
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (waited.Elapsed < _loadTimeout)
            {
                var (state, _, _) = await ModelStateAsync(profile, cancellationToken).ConfigureAwait(false);
                if (state is not ("loaded" or "loading" or "sleeping"))
                {
                    return;
                }

                await Task.Delay(_loadPoll, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or ModelEndpointException)
        {
            // A model that cannot be unloaded is left for the server to
            // close when the worker stops it; a load that then finds the
            // limit reached fails saying so.
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        ModelProfile profile,
        HttpMethod method,
        string path,
        string? body,
        CancellationToken cancellationToken)
    {
        if (await EndpointPolicy.RefusalAfterResolvingAsync(profile.Endpoint, cancellationToken).ConfigureAwait(false) is { } refusal)
        {
            throw new ModelEndpointException(refusal);
        }

        var baseAddress = profile.Endpoint.ToString().EndsWith('/') ? profile.Endpoint : new Uri(profile.Endpoint + "/");
        using var request = new HttpRequestMessage(method, new Uri(baseAddress, path));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (profile.ProtectedApiKey is { } protectedKey)
        {
            if (keys is null)
            {
                throw new ModelEndpointException($"Profile \"{profile.Name}\" has an API key, but no way to read it.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", keys.Unprotect(protectedKey));
        }

        try
        {
            // Headers first, so that a streamed answer is read as it arrives.
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ModelEndpointException($"The model server at {profile.Endpoint} could not be reached: {exception.Message}", exception);
        }
    }

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static double? Double(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static string Shorten(string text) => text.Length <= 500 ? text : text[..500] + "...";
}

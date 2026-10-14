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
}

/// <summary>What the server answered.</summary>
public sealed record ModelResponse(
    string? Content,
    string? FinishReason,
    int? PromptTokens,
    int? CompletionTokens,
    double? PromptMilliseconds,
    double? CompletionMilliseconds);

/// <summary>A failure reported by, or in reaching, a model server.</summary>
public sealed class ModelEndpointException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A server the worker sends requests to: an OpenAI-compatible chat
/// completions endpoint, with llama.cpp's router requests for loading and
/// unloading a model and its properties request for the server's build.
/// </summary>
public interface IModelEndpoint
{
    Task<ModelResponse> CompleteAsync(ModelProfile profile, string body, CancellationToken cancellationToken);

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
    public static string Build(ModelProfile profile, ModelRequest request)
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
            ["stream"] = false,
            ["cache_prompt"] = false,
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_object",
                ["schema"] = ClaimCheck.Schema(request.Sections)
            }
        };
        if (!profile.Generation.Thinking)
        {
            body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
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
public sealed class HttpModelEndpoint(HttpClient client, IApiKeyProtector? keys) : IModelEndpoint
{
    public async Task<ModelResponse> CompleteAsync(ModelProfile profile, string body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        using var response = await SendAsync(profile, HttpMethod.Post, "v1/chat/completions", body, cancellationToken)
            .ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ModelEndpointException(
                $"The model server answered {(int)response.StatusCode} {response.ReasonPhrase}: {Shorten(text)}");
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
        }
        catch (Exception exception) when (exception is HttpRequestException or ModelEndpointException)
        {
            // A model that cannot be unloaded is left for the server to
            // close when the worker stops it.
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
            return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

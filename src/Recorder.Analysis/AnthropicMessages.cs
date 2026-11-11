using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis;

/// <summary>
/// The request and answer of Anthropic's Messages API, for a profile that
/// sends to Anthropic. The request is built once, hashed, and sent exactly
/// as hashed, as a chat completions request is. See
/// docs/architecture/cloud-models.md, "3. The request" and "4. The answer".
/// </summary>
public static class AnthropicMessages
{
    /// <summary>
    /// The <c>anthropic-version</c> header, fixed in the source so that a
    /// change of version is a change of the recorder: the latest on
    /// 2026-11-11, which removes temperature, top_p and top_k (Anthropic's
    /// versions page, checked that day).
    /// </summary>
    public const string Version = "2026-10-21";

    /// <summary>What the worker records as the server's build for a profile that sends to Anthropic.</summary>
    public const string ServerBuild = "anthropic-version " + Version;

    /// <summary>The path of the Messages API under the endpoint.</summary>
    public const string Path = "v1/messages";

    /// <summary>
    /// The body of a Messages request: the model, the instructions as the
    /// system prompt, one user message with the input and then each picture
    /// after a line "Picture reference:", as a chat request has them, the
    /// output limit, the effort where the profile gives one, the step's
    /// schema as structured output, and a streamed answer. No temperature,
    /// seed or sampling options are sent: the API refuses them.
    /// </summary>
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
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["media_type"] = image.MediaType,
                    ["data"] = Convert.ToBase64String(image.Content)
                }
            });
        }

        var outputConfig = new JsonObject();
        if (profile.Effort is { } effort)
        {
            outputConfig["effort"] = effort;
        }

        outputConfig["format"] = new JsonObject
        {
            ["type"] = "json_schema",
            ["schema"] = Schema(ChatRequest.StepSchema(request))
        };

        return new JsonObject
        {
            ["model"] = profile.Model,
            ["max_tokens"] = profile.Generation.MaxTokens,
            ["system"] = request.Instructions,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
            ["output_config"] = outputConfig,
            // Streamed, so that the worker can stop an answer that repeats
            // itself, as it does a local model's.
            ["stream"] = true
        }.ToJsonString();
    }

    /// <summary>What "Check the profile" asks: one short question, without a recording.</summary>
    public const string CheckQuestion = "Answer with the one word: ready.";

    /// <summary>
    /// The output limit of "Check the profile": enough for the model's
    /// thinking, which is on for Claude 5 models (Anthropic's thinking
    /// documentation, checked 2026-11-11), and a one-word answer.
    /// </summary>
    public const int CheckMaxTokens = 1024;

    /// <summary>
    /// The body of "Check the profile" (cloud-models.md, "6. The player"):
    /// the profile's model and effort, the one short question, and an
    /// answer given whole, so that the key, the model ID and the effort
    /// are checked without sending a recording.
    /// </summary>
    public static string CheckBody(ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var body = new JsonObject
        {
            ["model"] = profile.Model,
            ["max_tokens"] = CheckMaxTokens,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = CheckQuestion })
        };
        if (profile.Effort is { } effort)
        {
            body["output_config"] = new JsonObject { ["effort"] = effort };
        }

        return body.ToJsonString();
    }

    /// <summary>The settings of a Messages request as recorded with an attempt.</summary>
    public static JsonObject Parameters(ModelProfile profile, ModelRequest request)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        return new JsonObject
        {
            ["provider"] = ModelProviders.Anthropic,
            ["anthropic_version"] = Version,
            ["max_tokens"] = profile.Generation.MaxTokens,
            // Null where the model's own default is taken.
            ["effort"] = profile.Effort,
            ["output_format"] = "json_schema",
            ["sections"] = new JsonArray([.. request.Sections.Select(section => JsonValue.Create(section))]),
            ["images"] = request.Images.Count
        };
    }

    /// <summary>
    /// A step's schema as Anthropic's structured output takes it: every
    /// object closed with <c>additionalProperties: false</c>, which it
    /// requires, and <c>minLength</c> removed, which it refuses (structured
    /// outputs documentation, checked 2026-11-11). The worker's own checks
    /// still refuse an empty statement.
    /// </summary>
    public static JsonObject Schema(JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var copy = (JsonObject)schema.DeepClone();
        Close(copy);
        return copy;
    }

    private static void Close(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject item:
                item.Remove("minLength");
                if (item["type"] is JsonValue type && type.TryGetValue<string>(out var name) && name == "object")
                {
                    item["additionalProperties"] = false;
                }

                foreach (var (_, value) in item.ToList())
                {
                    Close(value);
                }

                break;
            case JsonArray array:
                foreach (var value in array)
                {
                    Close(value);
                }

                break;
        }
    }

    /// <summary>
    /// Whether an answer that failed with this HTTP status may be asked for
    /// again after a wait: too many requests (429), Anthropic overloaded
    /// (529), or a fault of its servers (500 and above). Anything else, such
    /// as a request refused (400) or a key refused (401, 403), would fail
    /// again the same way.
    /// </summary>
    public static bool Busy(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>
    /// How long to wait before asking again: the <c>retry-after</c>
    /// header's seconds where it gives them, at most five minutes, or
    /// <paramref name="otherwise"/>.
    /// </summary>
    public static TimeSpan Wait(HttpResponseMessage response, TimeSpan otherwise)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Headers.RetryAfter is { } retry)
        {
            var wait = retry.Delta ?? (retry.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
            if (wait is { } given)
            {
                return given < TimeSpan.Zero ? TimeSpan.Zero : given > MaxWait ? MaxWait : given;
            }
        }

        return otherwise;
    }

    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    /// <summary>The request ID Anthropic gives each response, or null.</summary>
    public static string? RequestId(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.TryGetValues("request-id", out var values) ? values.FirstOrDefault() : null;
    }

    /// <summary>An error answer's text: its type and message, as Anthropic gives them, and its request ID.</summary>
    public static string ErrorText(HttpResponseMessage response, string text)
    {
        ArgumentNullException.ThrowIfNull(response);
        var said = text;
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                said = $"{String(error, "type") ?? "error"}: {String(error, "message") ?? ""}";
            }
        }
        catch (JsonException)
        {
            // Not Anthropic's error form; the text is given as it is.
        }

        return $"Anthropic answered {((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)} {response.ReasonPhrase}: {Shorten(said)}" +
            (RequestId(response) is { } id ? $" (request {id})" : "");
    }

    /// <summary>
    /// Reads a streamed answer: server-sent events, as Anthropic's streaming
    /// documentation gives them (checked 2026-11-11). The answer is the
    /// text deltas in order, given to <paramref name="watch"/> as they
    /// arrive; thinking deltas are kept apart; the input tokens come with
    /// message_start, the stop reason and output tokens with message_delta.
    /// An error event, or a stream that ends before message_stop, fails
    /// the request. The times are measured here: from sending to the first
    /// content block, and from then to the end.
    /// </summary>
    public static async Task<ModelResponse> ReadStreamAsync(
        HttpResponseMessage response,
        Func<string, string?>? watch,
        Stopwatch sent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(sent);
        var requestId = RequestId(response);
        var content = new StringBuilder();
        var thinking = new StringBuilder();
        string? model = null, stopReason = null;
        int? inputTokens = null, outputTokens = null;
        double? firstBlock = null;
        var stopped = false;
        ModelResponse Answer(string? finish, string? stoppedBecause) =>
            new(content.ToString(), finish, inputTokens, outputTokens,
                firstBlock, firstBlock is { } first ? sent.Elapsed.TotalMilliseconds - first : null, stoppedBecause)
            {
                Reasoning = thinking.Length == 0 ? null : thinking.ToString(),
                RequestId = requestId,
                ResponseModel = model
            };

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = line[5..].Trim();
                JsonElement root;
                try
                {
                    using var document = JsonDocument.Parse(data);
                    root = document.RootElement.Clone();
                }
                catch (JsonException exception)
                {
                    throw new ModelEndpointException($"Anthropic's answer is not a streamed message: {Shorten(data)}", exception);
                }

                switch (String(root, "type"))
                {
                    case "message_start" when root.TryGetProperty("message", out var message):
                        model = String(message, "model");
                        if (message.TryGetProperty("usage", out var startUsage))
                        {
                            inputTokens = InputTokens(startUsage);
                            outputTokens = Int(startUsage, "output_tokens") ?? outputTokens;
                        }

                        break;
                    case "content_block_start":
                        firstBlock ??= sent.Elapsed.TotalMilliseconds;
                        break;
                    case "content_block_delta" when root.TryGetProperty("delta", out var delta):
                        switch (String(delta, "type"))
                        {
                            case "text_delta":
                                content.Append(String(delta, "text"));
                                if (watch?.Invoke(content.ToString()) is { } stop)
                                {
                                    // Closing the response stops Anthropic
                                    // writing the answer.
                                    return Answer(null, stop);
                                }

                                break;
                            case "thinking_delta":
                                thinking.Append(String(delta, "thinking"));
                                break;
                        }

                        break;
                    case "message_delta":
                        if (root.TryGetProperty("delta", out var messageDelta) && String(messageDelta, "stop_reason") is { } reason)
                        {
                            stopReason = reason;
                        }

                        if (root.TryGetProperty("usage", out var deltaUsage))
                        {
                            outputTokens = Int(deltaUsage, "output_tokens") ?? outputTokens;
                            inputTokens = InputTokens(deltaUsage) ?? inputTokens;
                        }

                        break;
                    case "message_stop":
                        stopped = true;
                        break;
                    case "error":
                        var error = root.TryGetProperty("error", out var errorElement) ? errorElement : root;
                        throw new ModelEndpointException(
                            $"Anthropic reported an error part way through the answer: {String(error, "type") ?? "error"}: {Shorten(String(error, "message") ?? "")}" +
                            (requestId is { } id ? $" (request {id})" : ""));
                }

                if (stopped)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw new ModelEndpointException(
                $"The connection to Anthropic broke part way through the answer: {exception.Message}" +
                (requestId is { } id ? $" (request {id})" : ""),
                exception);
        }

        if (!stopped)
        {
            throw new ModelEndpointException(
                "Anthropic's answer ended before it was finished." + (requestId is { } id ? $" (request {id})" : ""));
        }

        return Answer(stopReason, null);
    }

    /// <summary>
    /// Reads an answer that was not streamed: a message, whose text blocks
    /// are the answer. The worker always asks for a streamed answer; this
    /// is for a server that gives one whole.
    /// </summary>
    public static ModelResponse ReadWhole(HttpResponseMessage response, string text, double milliseconds)
    {
        ArgumentNullException.ThrowIfNull(response);
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var content = new StringBuilder();
            var thinking = new StringBuilder();
            foreach (var block in root.GetProperty("content").EnumerateArray())
            {
                switch (String(block, "type"))
                {
                    case "text":
                        content.Append(String(block, "text"));
                        break;
                    case "thinking":
                        thinking.Append(String(block, "thinking"));
                        break;
                }
            }

            var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;
            return new ModelResponse(
                content.ToString(),
                String(root, "stop_reason"),
                usage.ValueKind == JsonValueKind.Object ? InputTokens(usage) : null,
                usage.ValueKind == JsonValueKind.Object ? Int(usage, "output_tokens") : null,
                null,
                milliseconds)
            {
                Reasoning = thinking.Length == 0 ? null : thinking.ToString(),
                RequestId = RequestId(response),
                ResponseModel = String(root, "model")
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ModelEndpointException($"Anthropic's answer is not a message: {Shorten(text)}", exception);
        }
    }

    // The input tokens, with those read from or written to the prompt cache,
    // which the worker does not ask for but which are counted apart if used.
    private static int? InputTokens(JsonElement usage)
    {
        if (usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var input = Int(usage, "input_tokens");
        var written = Int(usage, "cache_creation_input_tokens");
        var read = Int(usage, "cache_read_input_tokens");
        return input is null && written is null && read is null ? null : (input ?? 0) + (written ?? 0) + (read ?? 0);
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static string Shorten(string text) => text.Length <= 500 ? text : text[..500] + "...";
}

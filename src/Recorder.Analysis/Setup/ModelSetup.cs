using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis.Setup;

/// <summary>
/// Changes to analysis-models.json made by the installer's first start and
/// the model setup window. Each takes the file's text and gives the new
/// text, with everything it does not change kept as it was, and is checked
/// by reading the result back, so a change never leaves a profile the
/// worker would refuse. See docs/architecture/windows-installer.md.
/// </summary>
public static class ModelSetup
{
    /// <summary>The local server's port when the file names none: the one used since 2026-10-13.</summary>
    public const int DefaultPort = 8091;

    /// <summary>The folder beside the programs that holds the llama.cpp server the installer put there.</summary>
    public const string BundledServerFolder = "llama";

    /// <summary>The image profile's sampling, as "This PC, with images" has had since 2026-10-15.</summary>
    public const double ImagePresencePenalty = 1.5;

    public const string ImagesSuffix = ", with images";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string BundledServer(string baseDirectory) =>
        Path.Combine(baseDirectory, BundledServerFolder, "llama-server.exe");

    /// <summary>
    /// The settings with the local server set to the installed one, when
    /// the installed one exists and the file names no server or one that
    /// is not there; null when nothing changes. A server the owner set that
    /// exists is kept (owner, 2026-11-06: test builds apply the
    /// configuration they need, without settings files copied by hand).
    /// </summary>
    public static string? WithBundledServer(string? text, string bundledExecutable, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        if (!exists(bundledExecutable))
        {
            return null;
        }

        var document = Document(text);
        if (document is null)
        {
            return null;
        }

        var server = document["localServer"] as JsonObject;
        var executable = server?["executable"]?.GetValueKind() == JsonValueKind.String ? server["executable"]!.GetValue<string>() : null;
        if (!string.IsNullOrWhiteSpace(executable) && exists(executable))
        {
            return null;
        }

        if (server is null)
        {
            server = [];
            document["localServer"] = server;
        }

        server["executable"] = bundledExecutable;
        if (server["port"]?.GetValueKind() != JsonValueKind.Number)
        {
            server["port"] = DefaultPort;
        }

        return document.ToJsonString(Indented);
    }

    /// <summary>The local server's port in the settings, or the default.</summary>
    public static int Port(string? text) =>
        Document(text)?["localServer"]?["port"] is JsonValue value && value.TryGetValue<int>(out var port) ? port : DefaultPort;

    /// <summary>
    /// The profiles a model makes: one for text, and with an image add-on
    /// one for images that also takes text, as "This PC" and "This PC, with
    /// images" do.
    /// </summary>
    public static IReadOnlyList<JsonObject> Profiles(
        string name,
        string model,
        string quantization,
        string modelPath,
        string sha256,
        string? imageAddOnPath,
        string arguments,
        GenerationSettings generation,
        int port)
    {
        var profiles = new List<JsonObject>
        {
            Profile(name, model, [ModelCapabilities.Text], quantization, modelPath, sha256, null, arguments, generation, port)
        };
        if (imageAddOnPath is not null)
        {
            var sampling = generation.Sampling.SetItem("presence_penalty", ImagePresencePenalty);
            profiles.Add(Profile(
                name + ImagesSuffix,
                model + "-images",
                [ModelCapabilities.Text, ModelCapabilities.Image],
                quantization,
                modelPath,
                sha256,
                imageAddOnPath,
                arguments,
                generation with { Sampling = sampling },
                port));
        }

        return profiles;
    }

    private static JsonObject Profile(
        string name,
        string model,
        string[] capabilities,
        string quantization,
        string modelPath,
        string sha256,
        string? imageAddOnPath,
        string arguments,
        GenerationSettings generation,
        int port)
    {
        var profile = new JsonObject
        {
            ["name"] = name,
            ["endpoint"] = $"http://127.0.0.1:{port}",
            ["model"] = model,
            ["capabilities"] = new JsonArray([.. capabilities.Select(item => JsonValue.Create(item))]),
            ["file"] = new JsonObject
            {
                ["name"] = Path.GetFileName(modelPath),
                ["quantization"] = quantization,
                ["sha256"] = sha256,
                ["path"] = modelPath
            },
            ["local"] = new JsonObject
            {
                ["modelPath"] = modelPath,
                ["imageAddOnPath"] = imageAddOnPath,
                ["arguments"] = arguments
            },
            ["generation"] = Generation(generation)
        };
        return profile;
    }

    private static JsonObject Generation(GenerationSettings generation)
    {
        var node = new JsonObject
        {
            ["maxTokens"] = generation.MaxTokens,
            ["temperature"] = generation.Temperature,
            ["seed"] = generation.Seed,
            ["thinking"] = generation.Thinking
        };
        if (generation.Sampling.Count > 0)
        {
            node["sampling"] = AnalysisSettingsFile.GenerationSampling(generation);
        }

        return node;
    }

    /// <summary>
    /// The settings with the profiles added, each replacing one of the same
    /// name, and each made the default for what it takes where there is no
    /// default yet.
    /// </summary>
    public static string WithProfiles(string? text, IEnumerable<JsonObject> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var document = Document(text) ?? throw new InvalidDataException("The analysis settings file does not hold a JSON object, so no profile was added.");
        if (document["profiles"] is not JsonArray list)
        {
            list = [];
            document["profiles"] = list;
        }

        if (document["defaults"] is not JsonObject defaults)
        {
            defaults = [];
            document["defaults"] = defaults;
        }

        var added = new List<string>();
        foreach (var profile in profiles)
        {
            var name = profile["name"]!.GetValue<string>();
            var copy = profile.DeepClone().AsObject();
            var index = IndexOf(list, name);
            if (index >= 0)
            {
                list[index] = copy;
            }
            else
            {
                list.Add(copy);
            }

            added.Add(name);
            foreach (var capability in (copy["capabilities"] as JsonArray ?? []).Select(item => item!.GetValue<string>()))
            {
                // The image profile also takes text, but the text default
                // stays with the text profile.
                var isImageProfile = (copy["capabilities"] as JsonArray)!.Count > 1;
                if (defaults[capability] is null && !(capability == ModelCapabilities.Text && isImageProfile))
                {
                    defaults[capability] = name;
                }
            }
        }

        return Checked(document, added);
    }

    /// <summary>The settings without the profile, and without any default that named it.</summary>
    public static string WithoutProfile(string? text, string name)
    {
        var document = Document(text) ?? throw new InvalidDataException("The analysis settings file does not hold a JSON object.");
        if (document["profiles"] is JsonArray list && IndexOf(list, name) is var index and >= 0)
        {
            list.RemoveAt(index);
        }

        if (document["defaults"] is JsonObject defaults)
        {
            foreach (var capability in defaults.Where(pair => pair.Value?.GetValueKind() == JsonValueKind.String && pair.Value.GetValue<string>() == name).Select(pair => pair.Key).ToList())
            {
                defaults.Remove(capability);
            }
        }

        return Checked(document, []);
    }

    /// <summary>
    /// The settings with a local profile's server arguments and generation
    /// settings changed, its sampling options kept.
    /// </summary>
    public static string WithProfileSettings(string? text, string name, string arguments, int maxTokens, double temperature, long seed, bool thinking)
    {
        var document = Document(text) ?? throw new InvalidDataException("The analysis settings file does not hold a JSON object.");
        if (document["profiles"] is not JsonArray list || IndexOf(list, name) is var index && index < 0)
        {
            throw new InvalidDataException($"There is no profile named \"{name}\".");
        }

        var profile = list[index]!.AsObject();
        if (profile["provider"]?.GetValueKind() == JsonValueKind.String && profile["provider"]!.GetValue<string>() == ModelProviders.Anthropic)
        {
            throw new InvalidDataException($"Profile \"{name}\" sends to Anthropic, so it has no temperature, seed or thinking setting.");
        }

        if (profile["local"] is JsonObject local)
        {
            local["arguments"] = arguments;
        }
        else if (!string.IsNullOrWhiteSpace(arguments))
        {
            throw new InvalidDataException($"Profile \"{name}\" is not served by this PC, so it has no server arguments.");
        }

        if (profile["generation"] is not JsonObject generation)
        {
            generation = [];
            profile["generation"] = generation;
        }

        generation["maxTokens"] = maxTokens;
        generation["temperature"] = temperature;
        generation["seed"] = seed;
        generation["thinking"] = thinking;
        return Checked(document, [name]);
    }

    /// <summary>
    /// A profile that sends to Anthropic (cloud-models.md, "6. The
    /// player"): the model ID, text and images, the output limit, the
    /// effort or none for the model's own, the prices or none for no
    /// estimate, and the key, already protected for this Windows user.
    /// </summary>
    public static JsonObject ClaudeProfile(string name, string model, string? effort, int maxTokens, ServicePrices? prices, string protectedApiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedApiKey);
        var profile = new JsonObject
        {
            ["name"] = name.Trim(),
            ["provider"] = ModelProviders.Anthropic,
            ["endpoint"] = EndpointPolicy.AnthropicEndpoint.ToString(),
            ["model"] = model.Trim(),
            ["capabilities"] = new JsonArray(ModelCapabilities.Text, ModelCapabilities.Image),
            ["generation"] = new JsonObject { ["maxTokens"] = maxTokens }
        };
        if (effort is not null)
        {
            profile["effort"] = effort;
        }

        if (prices is not null)
        {
            profile["prices"] = prices.ToJson();
        }

        profile["protectedApiKey"] = protectedApiKey;
        return profile;
    }

    /// <summary>
    /// The settings with a new Claude profile added. Unlike
    /// <see cref="WithProfiles"/>, a profile of the same name is not
    /// replaced, and the profile is not made a default, so that choosing
    /// to send to Anthropic stays a choice made when a task is queued.
    /// </summary>
    public static string WithClaudeProfile(string? text, JsonObject profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var document = Document(text) ?? throw new InvalidDataException("The analysis settings file does not hold a JSON object, so no profile was added.");
        if (document["profiles"] is not JsonArray list)
        {
            list = [];
            document["profiles"] = list;
        }

        var name = profile["name"]!.GetValue<string>();
        if (IndexOf(list, name) >= 0)
        {
            throw new InvalidDataException($"There is already a profile named \"{name}\".");
        }

        list.Add(profile.DeepClone());
        return Checked(document, [name]);
    }

    /// <summary>
    /// The settings with a Claude profile's output limit, effort (null for
    /// the model's own), and prices (null for no estimate) changed, and its
    /// key replaced where a new one, already protected, is given.
    /// </summary>
    public static string WithClaudeSettings(string? text, string name, int maxTokens, string? effort, ServicePrices? prices, string? protectedApiKey)
    {
        var document = Document(text) ?? throw new InvalidDataException("The analysis settings file does not hold a JSON object.");
        if (document["profiles"] is not JsonArray list || IndexOf(list, name) is var index && index < 0)
        {
            throw new InvalidDataException($"There is no profile named \"{name}\".");
        }

        var profile = list[index]!.AsObject();
        if (profile["provider"]?.GetValueKind() != JsonValueKind.String || profile["provider"]!.GetValue<string>() != ModelProviders.Anthropic)
        {
            throw new InvalidDataException($"Profile \"{name}\" does not send to Anthropic.");
        }

        if (profile["generation"] is not JsonObject generation)
        {
            generation = [];
            profile["generation"] = generation;
        }

        generation["maxTokens"] = maxTokens;
        if (effort is null)
        {
            profile.Remove("effort");
        }
        else
        {
            profile["effort"] = effort;
        }

        if (prices is null)
        {
            profile.Remove("prices");
        }
        else
        {
            profile["prices"] = prices.ToJson();
        }

        if (!string.IsNullOrWhiteSpace(protectedApiKey))
        {
            profile["protectedApiKey"] = protectedApiKey;
        }

        return Checked(document, [name]);
    }

    /// <summary>
    /// A model name for the local server from a file name: lower case,
    /// letters, digits, dots and hyphens, without ".gguf".
    /// </summary>
    public static string ModelName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        var name = new StringBuilder();
        foreach (var character in stem)
        {
            name.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-');
        }

        var result = name.ToString().Trim('-');
        return result.Length == 0 ? "model" : result;
    }

    /// <summary>The quantization in a GGUF file name, such as UD-Q4_K_M or Q8_0, or "" when none is seen.</summary>
    public static string Quantization(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var parts = stem.Split('-');
        for (var index = parts.Length - 1; index >= 0; index--)
        {
            var part = parts[index];
            var quantization =
                (part.Length >= 2 && part[0] is 'Q' or 'q' && char.IsAsciiDigit(part[1])) ||
                (part.Length >= 3 && part.StartsWith("IQ", StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(part[2])) ||
                part.StartsWith("MXFP", StringComparison.OrdinalIgnoreCase) ||
                part is "BF16" or "F16" or "F32";
            if (quantization)
            {
                return index > 0 && parts[index - 1] == "UD" ? "UD-" + part : part;
            }
        }

        return "";
    }

    private static int IndexOf(JsonArray list, string name)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (list[index] is JsonObject profile && profile["name"]?.GetValueKind() == JsonValueKind.String && profile["name"]!.GetValue<string>() == name)
            {
                return index;
            }
        }

        return -1;
    }

    private static JsonObject? Document(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return JsonNode.Parse(AnalysisPlayer.EmptySettings)!.AsObject();
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The new text is read back; a problem that names a profile just
    // written, or that was not there before, stops the change.
    private static string Checked(JsonObject document, IReadOnlyList<string> names)
    {
        var text = document.ToJsonString(Indented);
        var (settings, problems) = AnalysisSettingsFile.Parse(text);
        var wrong = problems.Where(problem => names.Any(name => problem.Contains($"\"{name}\"", StringComparison.Ordinal))).ToList();
        foreach (var name in names)
        {
            if (settings.Profile(name) is null && wrong.Count == 0)
            {
                wrong.Add($"Profile \"{name}\" could not be read back.");
            }
        }

        if (wrong.Count > 0)
        {
            throw new InvalidDataException(string.Join(" ", wrong));
        }

        return text;
    }
}

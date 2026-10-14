using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis;

/// <summary>What a model can take as input.</summary>
public static class ModelCapabilities
{
    public const string Text = "text";
    public const string Image = "image";

    public static IReadOnlyList<string> All { get; } = [Text, Image];
}

/// <summary>
/// The model file a profile names, as its publisher gives it. The path is
/// set only when the file is on this PC, so the worker can hash it.
/// </summary>
public sealed record ModelFile(string? Name, string? Quantization, string? Sha256, string? Path);

/// <summary>
/// How the worker serves a profile with the llama.cpp server it starts on
/// this PC: the model file, its image add-on, and the server's settings for
/// it, written as command-line arguments such as
/// <c>-ngl 99 -ncmoe 36 -fa on</c>.
/// </summary>
public sealed record LocalModel(string ModelPath, string? ImageAddOnPath, string Arguments);

/// <summary>The generation settings sent with every request.</summary>
public sealed record GenerationSettings(
    int MaxTokens,
    double Temperature,
    long Seed,
    bool Thinking)
{
    public static GenerationSettings Default { get; } = new(2048, 0, 1, false);
}

/// <summary>
/// One model at one endpoint. A profile with <see cref="Local"/> is served
/// by the llama.cpp server the worker starts; any other is a server the
/// worker only sends requests to.
/// </summary>
public sealed record ModelProfile(
    string Name,
    Uri Endpoint,
    string Model,
    IReadOnlyList<string> Capabilities,
    ModelFile File,
    LocalModel? Local,
    GenerationSettings Generation,
    string? ProtectedApiKey)
{
    public bool Supports(string capability) =>
        Capabilities.Contains(capability, StringComparer.Ordinal);
}

/// <summary>The llama.cpp server the worker starts on this PC.</summary>
public sealed record LocalServerSettings(string Executable, int Port);

/// <summary>
/// The analysis settings file: the model profiles, the default profile for
/// each capability, the local server, and how many attempts a step has.
/// </summary>
public sealed record AnalysisSettings(
    IReadOnlyList<ModelProfile> Profiles,
    IReadOnlyDictionary<string, string> Defaults,
    LocalServerSettings? LocalServer,
    int MaxAttempts)
{
    public const int DefaultMaxAttempts = 3;

    public static AnalysisSettings Empty { get; } =
        new([], new Dictionary<string, string>(), null, DefaultMaxAttempts);

    public ModelProfile? Profile(string name) =>
        Profiles.FirstOrDefault(profile => string.Equals(profile.Name, name, StringComparison.Ordinal));
}

/// <summary>
/// Reads and writes <c>analysis-models.json</c>. A missing file gives no
/// profiles. A file that cannot be read, or a profile that breaks a rule,
/// is reported as a problem rather than ignored, so a task never runs on a
/// profile other than the one asked for.
/// </summary>
public static class AnalysisSettingsFile
{
    public const int Version = 1;

    public static string DefaultPath =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Windows A11y Recorder",
            "analysis-models.json");

    public static (AnalysisSettings Settings, IReadOnlyList<string> Problems) Read(string path)
    {
        if (!File.Exists(path))
        {
            return (AnalysisSettings.Empty, []);
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            return (AnalysisSettings.Empty, [$"The analysis settings file could not be read: {exception.Message}"]);
        }
        catch (UnauthorizedAccessException exception)
        {
            return (AnalysisSettings.Empty, [$"The analysis settings file could not be read: {exception.Message}"]);
        }

        return Parse(text);
    }

    public static (AnalysisSettings Settings, IReadOnlyList<string> Problems) Parse(string text)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException exception)
        {
            return (AnalysisSettings.Empty, [$"The analysis settings file is not valid JSON: {exception.Message}"]);
        }

        if (root is not JsonObject document)
        {
            return (AnalysisSettings.Empty, ["The analysis settings file is not a JSON object."]);
        }

        var problems = new List<string>();
        var version = document["version"]?.GetValueKind() == JsonValueKind.Number
            ? document["version"]!.GetValue<int>()
            : 0;
        if (version != Version)
        {
            return (AnalysisSettings.Empty, [$"The analysis settings file is version {version}; version {Version} is expected."]);
        }

        var maxAttempts = AnalysisSettings.DefaultMaxAttempts;
        if (document["maxAttempts"] is JsonNode attemptsNode)
        {
            if (attemptsNode.GetValueKind() == JsonValueKind.Number &&
                attemptsNode.GetValue<double>() is var attempts &&
                attempts >= 1 && attempts <= 10 && attempts == Math.Floor(attempts))
            {
                maxAttempts = (int)attempts;
            }
            else
            {
                problems.Add("maxAttempts must be a whole number from 1 to 10.");
            }
        }

        LocalServerSettings? localServer = null;
        if (document["localServer"] is JsonObject server)
        {
            var executable = server["executable"]?.GetValue<string>();
            var port = server["port"]?.GetValueKind() == JsonValueKind.Number ? server["port"]!.GetValue<int>() : 0;
            if (string.IsNullOrWhiteSpace(executable) || port is < 1 or > 65535)
            {
                problems.Add("localServer needs an executable and a port from 1 to 65535.");
            }
            else
            {
                localServer = new LocalServerSettings(executable, port);
            }
        }

        var profiles = new List<ModelProfile>();
        if (document["profiles"] is JsonArray profileArray)
        {
            foreach (var node in profileArray)
            {
                if (ReadProfile(node, localServer, problems) is { } profile)
                {
                    if (profiles.Any(existing => existing.Name == profile.Name))
                    {
                        problems.Add($"Two profiles are named \"{profile.Name}\".");
                        continue;
                    }

                    // The local server knows each profile it serves by its
                    // model name, one section of its preset each.
                    if (profile.Local is not null &&
                        profiles.FirstOrDefault(existing => existing.Local is not null && existing.Model == profile.Model) is { } same)
                    {
                        problems.Add($"Profiles \"{same.Name}\" and \"{profile.Name}\" are both served by this PC as model \"{profile.Model}\"; each needs a model name of its own.");
                        continue;
                    }

                    profiles.Add(profile);
                }
            }
        }

        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document["defaults"] is JsonObject defaultObject)
        {
            foreach (var (capability, value) in defaultObject)
            {
                var name = value?.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
                var profile = name is null ? null : profiles.FirstOrDefault(item => item.Name == name);
                if (!ModelCapabilities.All.Contains(capability))
                {
                    problems.Add($"The default for \"{capability}\" names no known capability.");
                }
                else if (profile is null)
                {
                    problems.Add($"The default {capability} profile \"{name}\" is not a profile.");
                }
                else if (!profile.Supports(capability))
                {
                    problems.Add($"The default {capability} profile \"{name}\" cannot take {capability}.");
                }
                else
                {
                    defaults[capability] = profile.Name;
                }
            }
        }

        return (new AnalysisSettings(profiles, defaults, localServer, maxAttempts), problems);
    }

    private static ModelProfile? ReadProfile(JsonNode? node, LocalServerSettings? localServer, List<string> problems)
    {
        if (node is not JsonObject profile)
        {
            problems.Add("A profile is not a JSON object.");
            return null;
        }

        var name = String(profile, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            problems.Add("A profile has no name.");
            return null;
        }

        var model = String(profile, "model");
        if (string.IsNullOrWhiteSpace(model))
        {
            problems.Add($"Profile \"{name}\" names no model.");
            return null;
        }

        if (!Uri.TryCreate(String(profile, "endpoint"), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            problems.Add($"Profile \"{name}\" needs an http or https endpoint.");
            return null;
        }

        if (EndpointPolicy.Refusal(endpoint) is { } refusal)
        {
            problems.Add($"Profile \"{name}\": {refusal}");
            return null;
        }

        var capabilities = new List<string>();
        if (profile["capabilities"] is JsonArray capabilityArray)
        {
            foreach (var item in capabilityArray)
            {
                var capability = item?.GetValueKind() == JsonValueKind.String ? item.GetValue<string>() : null;
                if (capability is null || !ModelCapabilities.All.Contains(capability))
                {
                    problems.Add($"Profile \"{name}\" lists a capability that is not text or image.");
                    return null;
                }

                capabilities.Add(capability);
            }
        }

        if (capabilities.Count == 0)
        {
            problems.Add($"Profile \"{name}\" lists no capabilities.");
            return null;
        }

        var file = profile["file"] as JsonObject;
        var modelFile = new ModelFile(
            file is null ? null : String(file, "name"),
            file is null ? null : String(file, "quantization"),
            file is null ? null : String(file, "sha256")?.ToLowerInvariant(),
            file is null ? null : String(file, "path"));
        if (modelFile.Sha256 is { } sha && (sha.Length != 64 || !sha.All(Uri.IsHexDigit)))
        {
            problems.Add($"Profile \"{name}\" has a file SHA-256 that is not 64 hexadecimal digits.");
            return null;
        }

        LocalModel? local = null;
        if (profile["local"] is JsonObject localObject)
        {
            var modelPath = String(localObject, "modelPath");
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                problems.Add($"Profile \"{name}\" is served locally but names no model file.");
                return null;
            }

            if (localServer is null)
            {
                problems.Add($"Profile \"{name}\" is served locally, but no localServer is set.");
                return null;
            }

            if (!endpoint.IsLoopback || endpoint.Port != localServer.Port)
            {
                problems.Add($"Profile \"{name}\" is served locally, so its endpoint must be the local server's, http://127.0.0.1:{localServer.Port}.");
                return null;
            }

            local = new LocalModel(modelPath, String(localObject, "imageAddOnPath"), String(localObject, "arguments") ?? "");
            if (LocalModelServer.PresetEntries(local.Arguments) is { Problem: { } argumentProblem })
            {
                problems.Add($"Profile \"{name}\": {argumentProblem}");
                return null;
            }

            if (capabilities.Contains(ModelCapabilities.Image) && local.ImageAddOnPath is null)
            {
                problems.Add($"Profile \"{name}\" takes images, so it needs an image add-on file.");
                return null;
            }
        }

        var generation = GenerationSettings.Default;
        if (profile["generation"] is JsonObject generationObject)
        {
            try
            {
                generation = new GenerationSettings(
                    generationObject["maxTokens"]?.GetValue<int>() ?? generation.MaxTokens,
                    generationObject["temperature"]?.GetValue<double>() ?? generation.Temperature,
                    generationObject["seed"]?.GetValue<long>() ?? generation.Seed,
                    generationObject["thinking"]?.GetValue<bool>() ?? generation.Thinking);
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException)
            {
                problems.Add($"Profile \"{name}\" has a generation setting of the wrong type.");
                return null;
            }

            if (generation.MaxTokens < 1 || generation.Temperature < 0)
            {
                problems.Add($"Profile \"{name}\" needs maxTokens of at least 1 and a temperature of at least 0.");
                return null;
            }
        }

        return new ModelProfile(
            name,
            endpoint,
            model,
            capabilities,
            modelFile,
            local,
            generation,
            String(profile, "protectedApiKey"));
    }

    private static string? String(JsonObject node, string name) =>
        node[name]?.GetValueKind() == JsonValueKind.String ? node[name]!.GetValue<string>() : null;

    /// <summary>
    /// The profile as recorded with an attempt: every setting, without the
    /// API key.
    /// </summary>
    public static JsonObject Describe(ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var described = new JsonObject
        {
            ["name"] = profile.Name,
            ["endpoint"] = profile.Endpoint.ToString(),
            ["model"] = profile.Model,
            ["capabilities"] = new JsonArray([.. profile.Capabilities.Select(item => JsonValue.Create(item))]),
            ["file"] = new JsonObject
            {
                ["name"] = profile.File.Name,
                ["quantization"] = profile.File.Quantization,
                ["sha256"] = profile.File.Sha256,
                ["path"] = profile.File.Path
            },
            ["generation"] = new JsonObject
            {
                ["maxTokens"] = profile.Generation.MaxTokens,
                ["temperature"] = profile.Generation.Temperature,
                ["seed"] = profile.Generation.Seed,
                ["thinking"] = profile.Generation.Thinking
            },
            ["hasApiKey"] = profile.ProtectedApiKey is not null
        };
        if (profile.Local is { } local)
        {
            described["local"] = new JsonObject
            {
                ["modelPath"] = local.ModelPath,
                ["imageAddOnPath"] = local.ImageAddOnPath,
                ["arguments"] = local.Arguments
            };
        }

        return described;
    }
}

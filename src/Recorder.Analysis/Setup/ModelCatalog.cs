using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis.Setup;

/// <summary>
/// One file to download: where from, its size in bytes and its SHA-256, so
/// a download is checked against what was published, not against itself.
/// </summary>
public sealed record CatalogFile(string Name, Uri Url, long Size, string Sha256);

/// <summary>
/// A model the recorder offers to download and set up (owner, 2026-11-06:
/// "the install should be as comprehensive as possible including model
/// selection and download"). Each entry makes a text profile and, with its
/// image add-on, a profile for images. See
/// docs/architecture/windows-installer.md, "Model setup".
/// </summary>
public sealed record CatalogModel(
    string Id,
    string Name,
    string Family,
    string Description,
    string Quantization,
    Uri Source,
    string? Licence,
    CatalogFile Model,
    CatalogFile? ImageAddOn,
    string Arguments,
    GenerationSettings Generation,
    string? Note)
{
    /// <summary>The bytes to download for the model, and its image add-on when asked for.</summary>
    public long DownloadSize(bool withImages) => Model.Size + (withImages && ImageAddOn is { } addOn ? addOn.Size : 0);
}

/// <summary>
/// The list of models in model-catalog.json, installed beside the
/// programs. A model not in it can still be added from a file on the PC
/// or from a Hugging Face address.
/// </summary>
public sealed record ModelCatalog(string? LlamaServer, string? Checked, IReadOnlyList<CatalogModel> Models)
{
    public const string FileName = "model-catalog.json";
    public const int Version = 1;

    public static ModelCatalog Empty { get; } = new(null, null, []);

    public static (ModelCatalog Catalog, IReadOnlyList<string> Problems) Read(string path)
    {
        if (!File.Exists(path))
        {
            return (Empty, [$"The list of models, {path}, is missing."]);
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (Empty, [$"The list of models could not be read: {exception.Message}"]);
        }
    }

    public static (ModelCatalog Catalog, IReadOnlyList<string> Problems) Parse(string text)
    {
        JsonObject document;
        try
        {
            document = JsonNode.Parse(text) as JsonObject ?? throw new JsonException("not an object");
        }
        catch (JsonException exception)
        {
            return (Empty, [$"The list of models is not valid JSON: {exception.Message}"]);
        }

        if (Number(document, "version") != Version)
        {
            return (Empty, [$"The list of models is not version {Version}."]);
        }

        var problems = new List<string>();
        var models = new List<CatalogModel>();
        foreach (var node in document["models"] as JsonArray ?? [])
        {
            if (node is not JsonObject entry)
            {
                problems.Add("A model in the list is not a JSON object.");
                continue;
            }

            var id = Text(entry, "id");
            var name = Text(entry, "name");
            if (id is null || name is null)
            {
                problems.Add("A model in the list has no id or name.");
                continue;
            }

            if (models.Any(model => model.Id == id))
            {
                problems.Add($"Two models in the list have the id \"{id}\".");
                continue;
            }

            var file = ReadFile(entry["model"] as JsonObject);
            var addOn = entry["imageAddOn"] is JsonObject addOnObject ? ReadFile(addOnObject) : null;
            if (file is null || (entry["imageAddOn"] is JsonObject && addOn is null))
            {
                problems.Add($"The model \"{name}\" in the list has a file without a name, https address, size or SHA-256.");
                continue;
            }

            var arguments = Text(entry, "arguments") ?? "";
            if (LocalModelServer.PresetEntries(arguments).Problem is { } argumentProblem)
            {
                problems.Add($"The model \"{name}\" in the list: {argumentProblem}");
                continue;
            }

            var generation = GenerationSettings.Default;
            if (entry["generation"] is JsonObject g)
            {
                generation = new GenerationSettings(
                    (int)(Number(g, "maxTokens") ?? generation.MaxTokens),
                    Number(g, "temperature") ?? generation.Temperature,
                    (long)(Number(g, "seed") ?? generation.Seed),
                    g["thinking"]?.GetValueKind() == JsonValueKind.True);
                if (g["sampling"] is JsonObject sampling)
                {
                    var options = System.Collections.Immutable.ImmutableSortedDictionary.CreateBuilder<string, double>(StringComparer.Ordinal);
                    foreach (var (option, value) in sampling)
                    {
                        if (value?.GetValueKind() == JsonValueKind.Number && !GenerationSettings.ReservedFields.Contains(option))
                        {
                            options[option] = value.GetValue<double>();
                        }
                    }

                    generation = generation with { Sampling = options.ToImmutable() };
                }
            }

            models.Add(new CatalogModel(
                id,
                name,
                Text(entry, "family") ?? name,
                Text(entry, "description") ?? "",
                Text(entry, "quantization") ?? "",
                Uri.TryCreate(Text(entry, "source"), UriKind.Absolute, out var source) ? source : file.Url,
                Text(entry, "licence"),
                file,
                addOn,
                arguments,
                generation,
                Text(entry, "note")));
        }

        return (new ModelCatalog(Text(document, "llamaServer"), Text(document, "checked"), models), problems);
    }

    private static CatalogFile? ReadFile(JsonObject? node)
    {
        if (node is null ||
            Text(node, "name") is not { } name ||
            !Uri.TryCreate(Text(node, "url"), UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps ||
            Number(node, "size") is not { } size || size < 1 ||
            Text(node, "sha256")?.ToLowerInvariant() is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit) ||
            name != Path.GetFileName(name))
        {
            return null;
        }

        return new CatalogFile(name, url, (long)size, sha);
    }

    private static string? Text(JsonObject node, string name) =>
        node[name]?.GetValueKind() == JsonValueKind.String ? node[name]!.GetValue<string>() : null;

    private static double? Number(JsonObject node, string name) =>
        node[name]?.GetValueKind() == JsonValueKind.Number ? node[name]!.GetValue<double>() : null;
}

/// <summary>Sizes as the setup window shows them.</summary>
public static class ByteSize
{
    /// <summary>
    /// Gigabytes as Windows shows them, of 1,073,741,824 bytes, to one
    /// place, so a file's size and a graphics card's memory (24 GB on an
    /// RTX 3090) are in the same unit. Hugging Face shows sizes in
    /// gigabytes of 1,000,000,000 bytes, so its figures are about 7%
    /// larger.
    /// </summary>
    public static string Gigabytes(long bytes) =>
        (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
}

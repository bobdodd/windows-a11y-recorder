using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis;

/// <summary>
/// One statement in a model's output. A claim rests on the evidence it
/// cites; an unknown says what the evidence does not say, and need not cite
/// anything.
/// </summary>
public sealed record Claim(string Section, int Paragraph, int Ordinal, string Kind, string Text, IReadOnlyList<string> Evidence)
{
    public const string ClaimKind = "claim";
    public const string UnknownKind = "unknown";
}

/// <summary>
/// The shape every model step answers in, and the recorder's own check of
/// it. The output is a JSON object with one member for each section the
/// step asks for. A section is a list of paragraphs, and a paragraph is a
/// list of statements, each with its kind, its text, and the evidence
/// references it cites. The check confirms that the output parses, that
/// every section is there, that every claim cites at least one reference,
/// and that every reference cited was in the step's input. Output that
/// fails is not repaired: the attempt fails.
/// </summary>
public static class ClaimCheck
{
    /// <summary>The JSON schema the server is asked to hold output to.</summary>
    public static JsonObject Schema(IReadOnlyList<string> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var statement = new JsonObject
        {
            ["type"] = "object",
            // The text comes before the kind, so that the model writes the
            // statement before saying whether it is a claim: the server holds
            // the answer to the schema's order, and twice the model began a
            // statement as a claim and then wrote that something was unknown
            // (owner, 2026-10-14; analysis-runner.md, decision 7).
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Claim.ClaimKind, Claim.UnknownKind) },
                ["evidence"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }
            },
            ["required"] = new JsonArray("text", "kind", "evidence")
        };
        var properties = new JsonObject();
        foreach (var section in sections)
        {
            properties[section] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = statement.DeepClone()
                }
            };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray([.. sections.Select(section => JsonValue.Create(section))])
        };
    }

    /// <summary>
    /// Checks a model's output. Returns its claims, or the reason it fails.
    /// </summary>
    public static (IReadOnlyList<Claim> Claims, JsonElement Output, string? Failure) Check(
        string? output,
        IReadOnlyList<string> sections,
        IReadOnlySet<string> inputReferences)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(inputReferences);
        if (string.IsNullOrWhiteSpace(output))
        {
            return ([], default, "The output is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(output);
        }
        catch (JsonException exception)
        {
            return ([], default, $"The output is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ([], default, "The output is not a JSON object.");
            }

            var claims = new List<Claim>();
            foreach (var section in sections)
            {
                if (!root.TryGetProperty(section, out var paragraphs) || paragraphs.ValueKind != JsonValueKind.Array)
                {
                    return ([], default, $"The output has no \"{section}\" section.");
                }

                var paragraphIndex = 0;
                foreach (var paragraph in paragraphs.EnumerateArray())
                {
                    if (paragraph.ValueKind != JsonValueKind.Array)
                    {
                        return ([], default, $"Paragraph {paragraphIndex + 1} of \"{section}\" is not a list of statements.");
                    }

                    var ordinal = 0;
                    foreach (var statement in paragraph.EnumerateArray())
                    {
                        var place = $"Statement {ordinal + 1} of paragraph {paragraphIndex + 1} of \"{section}\"";
                        if (statement.ValueKind != JsonValueKind.Object ||
                            !statement.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String ||
                            !statement.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String ||
                            !statement.TryGetProperty("evidence", out var evidenceElement) || evidenceElement.ValueKind != JsonValueKind.Array)
                        {
                            return ([], default, $"{place} does not have a kind, a text, and a list of evidence.");
                        }

                        var kind = kindElement.GetString()!;
                        var text = textElement.GetString()!;
                        if (kind is not (Claim.ClaimKind or Claim.UnknownKind))
                        {
                            return ([], default, $"{place} has the kind \"{kind}\", which is neither claim nor unknown.");
                        }

                        if (string.IsNullOrWhiteSpace(text))
                        {
                            return ([], default, $"{place} has no text.");
                        }

                        var evidence = new List<string>();
                        foreach (var reference in evidenceElement.EnumerateArray())
                        {
                            if (reference.ValueKind != JsonValueKind.String)
                            {
                                return ([], default, $"{place} cites evidence that is not a reference.");
                            }

                            var value = reference.GetString()!;
                            if (!inputReferences.Contains(value))
                            {
                                return ([], default, $"{place} cites {value}, which was not in the input.");
                            }

                            evidence.Add(value);
                        }

                        if (kind == Claim.ClaimKind && evidence.Count == 0)
                        {
                            return ([], default, $"{place} is a claim that cites no evidence.");
                        }

                        claims.Add(new Claim(section, paragraphIndex, ordinal, kind, text, evidence));
                        ordinal++;
                    }

                    paragraphIndex++;
                }
            }

            return (claims, root.Clone(), null);
        }
    }
}

/// <summary>
/// Watches an answer as it arrives, and stops it once the model is caught in
/// a loop: when its last statements are a block of one or more statements
/// said word for word three times in a row (owner, 2026-10-14). A model can
/// fall into repeating the same few statements until its output limit; this
/// ends the attempt early instead, and the output limit stays the backstop.
/// A statement said again elsewhere in the answer is not a loop and is let
/// through: two like things, such as cards that each end with the same
/// button, can each be described in the same words, and a model may restate
/// a conclusion in a later section. The rule follows the repetition checks
/// of other model tools, which all look for the same text repeated back to
/// back several times rather than for any repeat. See
/// docs/architecture/analysis-runner.md, "Sampling options".
/// </summary>
public sealed class RepeatWatch
{
    /// <summary>How many copies in a row make a loop.</summary>
    public const int Copies = 3;

    // A statement's text, once its closing quote has arrived. A quote
    // inside a JSON string is escaped, so the match cannot start inside one.
    private static readonly System.Text.RegularExpressions.Regex StatementText = new(
        "\"text\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private readonly List<string> _texts = [];
    private int _scanned;

    /// <summary>The number of statements seen so far.</summary>
    public int Statements => _texts.Count;

    /// <summary>Why the answer should stop, or null to let it go on.</summary>
    public string? Check(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        while (StatementText.Match(content, _scanned) is { Success: true } match)
        {
            _scanned = match.Index + match.Length;
            var text = Unescape(match.Groups[1].Value);
            if (text.Length == 0)
            {
                continue;
            }

            _texts.Add(text);
            if (Loop(_texts) is { } block)
            {
                var count = _texts.Count;
                var first = count - (Copies * block) + 1;
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                return block == 1
                    ? $"The model said the same statement word for word three times in a row, as statements {first.ToString(culture)} to {count.ToString(culture)}, " +
                      $"so the answer was stopped there: \"{text}\""
                    : $"The model said the same {block.ToString(culture)} statements word for word three times in a row, as statements {first.ToString(culture)} to {count.ToString(culture)}, " +
                      $"so the answer was stopped there. The repeated statements begin: \"{_texts[count - block]}\"";
            }
        }

        return null;
    }

    /// <summary>
    /// The size of the block of statements that the given statements end with
    /// three copies of, back to back, or null when they end with no such
    /// block. Any block size is looked for, the smallest first.
    /// </summary>
    public static int? Loop(IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var count = texts.Count;
        for (var block = 1; block * Copies <= count; block++)
        {
            var repeated = true;
            for (var offset = 1; offset <= block && repeated; offset++)
            {
                var last = texts[count - offset];
                for (var copy = 1; copy < Copies && repeated; copy++)
                {
                    repeated = string.Equals(texts[count - offset - (copy * block)], last, StringComparison.Ordinal);
                }
            }

            if (repeated)
            {
                return block;
            }
        }

        return null;
    }

    private static string Unescape(string escaped)
    {
        try
        {
            return JsonSerializer.Deserialize<string>("\"" + escaped + "\"") ?? escaped;
        }
        catch (JsonException)
        {
            return escaped;
        }
    }
}

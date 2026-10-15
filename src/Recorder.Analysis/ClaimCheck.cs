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
/// Watches an answer as it arrives, and stops it once the model is
/// repeating itself: at a statement said word for word a third time, or at
/// the second of two statements in a row that each repeat an earlier one
/// (owner, 2026-10-14). A model can fall into repeating the same few
/// statements until its output limit; this ends the attempt early instead.
/// A statement said once again is let through, since two like things, such
/// as cards that each end with the same button, can each be described in
/// the same words: on the target machine a description of a page's tiles
/// was stopped at "Below the description is a yellow button labeled Read
/// more." See docs/architecture/analysis-runner.md, "Sampling options".
/// </summary>
public sealed class RepeatWatch
{
    // A statement's text, once its closing quote has arrived. A quote
    // inside a JSON string is escaped, so the match cannot start inside one.
    private static readonly System.Text.RegularExpressions.Regex StatementText = new(
        "\"text\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);
    private int _scanned;
    private bool _previousRepeated;

    /// <summary>The number of statements seen so far.</summary>
    public int Statements { get; private set; }

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

            Statements++;
            var times = _seen.TryGetValue(text, out var before) ? before + 1 : 1;
            _seen[text] = times;
            var number = Statements.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (times >= 3)
            {
                return $"The model said statement {number} word for word for the third time in its answer, so the answer was stopped there: \"{text}\"";
            }

            if (times == 2 && _previousRepeated)
            {
                return $"The model repeated statements {(Statements - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)} and {number}, one after the other, " +
                    $"word for word from earlier in its answer, so the answer was stopped there: \"{text}\"";
            }

            _previousRepeated = times == 2;
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

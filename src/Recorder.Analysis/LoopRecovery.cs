using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis;

/// <summary>
/// What is done when a step's answer at temperature 0 is stopped as a loop
/// (owner, 2026-10-15). The step is asked for again at
/// <see cref="Temperature"/>, with <see cref="TopP"/> and <see cref="TopK"/>
/// and the profile's other sampling options, at each of the
/// <see cref="Seeds"/> in turn, and the first answer that passes the checks
/// and is not stopped as a loop is the step's answer; nothing is voted on.
/// A fixed seed gives the same answer on the same machine and server build,
/// as the trials on the target machine showed, so running the task again
/// gives the same answer. When none passes, the loop's answer is kept up to
/// the end of the first copy of the repeated statements, and says that it
/// was cut short there and that what it would have said next is unknown.
/// See docs/architecture/analysis-runner.md, "Decisions (2026-10-15)".
/// </summary>
public static class LoopRecovery
{
    /// <summary>The temperature of the answers sampled after a loop, the model card's for non-thinking mode.</summary>
    public const double Temperature = 0.7;

    /// <summary>The <c>top_p</c> of the answers sampled after a loop.</summary>
    public const double TopP = 0.8;

    /// <summary>The <c>top_k</c> of the answers sampled after a loop.</summary>
    public const double TopK = 20;

    /// <summary>The seeds tried after a loop, in turn.</summary>
    public static readonly ImmutableArray<long> Seeds = [1, 2, 3];

    /// <summary>
    /// The parameter recorded with an attempt made after a loop, beside the
    /// settings it was sent with: <see cref="Sampled"/> or
    /// <see cref="CutShort"/>.
    /// </summary>
    public const string AfterLoopParameter = "after_loop";

    /// <summary>An answer sampled after a loop.</summary>
    public const string Sampled = "sampled";

    /// <summary>The loop's answer kept up to the loop.</summary>
    public const string CutShort = "cut short";

    /// <summary>The unknown that ends an answer cut short at a loop.</summary>
    public const string CutText =
        "The model began repeating itself here, so its answer was cut short: what it would have said next is unknown.";

    /// <summary>The unknown that stands for a section an answer cut short at a loop did not reach.</summary>
    public const string NotReachedText =
        "The model's answer was cut short before this section because it began repeating itself, so what this section would have said is unknown.";

    /// <summary>The profile an answer is sampled with after a loop, at the seed given.</summary>
    public static ModelProfile Sample(ModelProfile profile, long seed)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var sampling = profile.Generation.Sampling.SetItem("top_p", TopP).SetItem("top_k", TopK);
        return profile with
        {
            Generation = profile.Generation with { Temperature = Temperature, Seed = seed, Sampling = sampling }
        };
    }

    /// <summary>Whether an answer is caught in a loop, by the rule of <see cref="RepeatWatch"/>.</summary>
    public static bool IsLoop(string? content) => content is not null && new RepeatWatch().Check(content) is not null;

    /// <summary>
    /// The answer that was stopped as a loop, kept up to the end of the first
    /// copy of the repeated statements, with an unknown saying it was cut
    /// short there, and an unknown for each section it did not reach. Null
    /// when the answer holds no loop.
    /// </summary>
    public static string? CutAtLoop(string content, IReadOnlyList<string> sections)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(sections);
        var statements = Statements(content);

        // The statements kept: those before the loop's second copy, found as
        // the watch finds it, at the first statement that ends three copies.
        var texts = new List<string>();
        int? keep = null;
        for (var index = 0; index < statements.Count; index++)
        {
            if (statements[index].Node["text"] is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length == 0)
            {
                continue;
            }

            texts.Add(text);
            if (RepeatWatch.Loop(texts) is { } block)
            {
                // The statement that ends the first copy, counted in texts.
                var lastKept = texts.Count - ((RepeatWatch.Copies - 1) * block);
                var seen = 0;
                for (var at = 0; at <= index; at++)
                {
                    if (statements[at].Node["text"] is JsonValue keptValue && keptValue.TryGetValue<string>(out var keptText) && keptText.Length > 0 &&
                        ++seen == lastKept)
                    {
                        keep = at + 1;
                        break;
                    }
                }

                break;
            }
        }

        if (keep is not { } count)
        {
            return null;
        }

        var kept = statements.Take(count).ToList();
        var cutSection = kept[^1].Section;
        var written = statements.Select(statement => statement.Section).ToHashSet(StringComparer.Ordinal);
        var cutReached = false;
        var root = new JsonObject();
        foreach (var section in sections)
        {
            var paragraphs = new JsonArray();
            if (cutReached || (!written.Contains(section) && section != cutSection))
            {
                paragraphs.Add(new JsonArray(Unknown(NotReachedText)));
            }
            else
            {
                foreach (var paragraph in kept.Where(statement => statement.Section == section).GroupBy(statement => statement.Paragraph))
                {
                    paragraphs.Add(new JsonArray([.. paragraph.Select(statement => statement.Node.DeepClone())]));
                }

                if (section == cutSection)
                {
                    if (paragraphs.Count == 0)
                    {
                        paragraphs.Add(new JsonArray());
                    }

                    ((JsonArray)paragraphs[^1]!).Add(Unknown(CutText));
                    cutReached = true;
                }
            }

            root[section] = paragraphs;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Unknown(string text) => new()
    {
        ["text"] = text,
        ["kind"] = Claim.UnknownKind,
        ["evidence"] = new JsonArray()
    };

    private sealed record Statement(string Section, int Paragraph, JsonNode Node);

    // The whole statements of an answer that may stop part way, in order,
    // each with its section and paragraph: a section is a list of
    // paragraphs, and a paragraph a list of statements.
    private static List<Statement> Statements(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: false, state: default);
        var statements = new List<Statement>();
        string? section = null;
        var paragraph = -1;
        long start = -1;
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName when reader.CurrentDepth == 1:
                        section = reader.GetString();
                        paragraph = -1;
                        break;
                    case JsonTokenType.StartArray when reader.CurrentDepth == 2:
                        paragraph++;
                        break;
                    case JsonTokenType.StartObject when reader.CurrentDepth == 3:
                        start = reader.TokenStartIndex;
                        break;
                    case JsonTokenType.EndObject when reader.CurrentDepth == 3 && start >= 0 && section is not null:
                        var node = JsonNode.Parse(bytes.AsSpan((int)start, (int)(reader.BytesConsumed - start)));
                        if (node is not null)
                        {
                            statements.Add(new Statement(section, paragraph, node));
                        }

                        start = -1;
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // What came before the fault is kept.
        }

        return statements;
    }
}

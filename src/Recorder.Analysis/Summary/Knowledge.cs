using System.Globalization;
using System.Text;

namespace Recorder.Analysis.Summary;

/// <summary>
/// One statement of a visit's knowledge: its number, which layer gave it
/// (1, the recorded data; 2, a captured frame; 3, the web page as the
/// recorder rebuilt it), at which moment, how it stands to an earlier
/// statement, and the model's statement, its evidence without the numbers
/// of the knowledge's statements. See recording-summary.md, "The layered
/// report".
/// </summary>
public sealed record KnowledgeStatement(int Number, int Layer, int? Moment, string Relation, int? About, Claim Claim)
{
    public const string Recorded = "recorded";

    public const string Elaborates = "elaborates";

    public const string Corrects = "corrects";

    public const string New = "new";

    /// <summary>The statement's reference in a layer's request.</summary>
    public string Reference => Knowledge.Reference(Number);
}

/// <summary>
/// A visit's knowledge: layer 1, the visit's description from the recorded
/// data, and each layer's additions at each moment, each elaborating or
/// correcting a numbered statement, or new, put in place by the recorder,
/// not by the model (owner, 2026-10-15). Statements keep their numbers as
/// statements are added, so that a step's answer is read with the numbers
/// its request gave.
/// </summary>
public sealed class Knowledge
{
    /// <summary>The sections a layer's answer has.</summary>
    public static readonly IReadOnlyList<string> LayerSections = ["elaborations", "corrections", "new"];

    /// <summary>
    /// The sections whose statements name, first, as "about", the statement
    /// they add to (layer templates version 4). ClaimCheck gives it as the
    /// first reference of the statement's evidence, which Add reads.
    /// </summary>
    public static readonly IReadOnlyList<string> AboutSections = ["elaborations", "corrections"];

    private readonly List<KnowledgeStatement> _statements = [];

    public IReadOnlyList<KnowledgeStatement> Statements => _statements;

    /// <summary>A statement's reference, K and its number, as a layer's request gives it.</summary>
    public static string Reference(int number) => "K" + number.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether a reference is a knowledge statement's, not the evidence list's.</summary>
    public static bool IsStatementReference(string reference) =>
        reference is { Length: > 1 } && reference[0] == 'K' && reference.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;

    /// <summary>A visit's knowledge with layer 1, the visit's description from the recorded data.</summary>
    public static Knowledge FromRecorded(IEnumerable<Claim> recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        var knowledge = new Knowledge();
        foreach (var claim in recorded)
        {
            knowledge._statements.Add(new KnowledgeStatement(knowledge._statements.Count + 1, 1, null, KnowledgeStatement.Recorded, null, claim));
        }

        return knowledge;
    }

    /// <summary>
    /// Puts a layer's answer at a moment in place: a statement in
    /// elaborations or corrections is about the first statement of the
    /// knowledge it cites, and one that cites none is taken as new. The
    /// numbers of the knowledge's statements are taken out of each
    /// statement's evidence, which keeps the evidence list's references.
    /// </summary>
    public IReadOnlyList<KnowledgeStatement> Add(IEnumerable<Claim> answer, int layer, int moment)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var known = _statements.Count;
        var added = new List<KnowledgeStatement>();
        foreach (var claim in answer)
        {
            int? about = null;
            foreach (var reference in claim.Evidence.Where(IsStatementReference))
            {
                if (int.TryParse(reference.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= known)
                {
                    about = number;
                    break;
                }
            }

            var relation = claim.Section switch
            {
                "elaborations" when about is not null => KnowledgeStatement.Elaborates,
                "corrections" when about is not null => KnowledgeStatement.Corrects,
                _ => KnowledgeStatement.New
            };
            var statement = new KnowledgeStatement(
                _statements.Count + 1,
                layer,
                moment,
                relation,
                relation == KnowledgeStatement.New ? null : about,
                claim with { Evidence = [.. claim.Evidence.Where(reference => !IsStatementReference(reference))] });
            _statements.Add(statement);
            added.Add(statement);
        }

        return added;
    }

    /// <summary>
    /// The statements in reading order: layer 1 in its order, each
    /// followed by the statements about it, in the order they were added,
    /// each followed in turn by those about it; then the new statements,
    /// in the order they were added, each followed by those about it.
    /// </summary>
    public IReadOnlyList<KnowledgeStatement> InOrder()
    {
        var about = _statements.Where(statement => statement.About is not null).ToLookup(statement => statement.About!.Value);
        var ordered = new List<KnowledgeStatement>();

        void Visit(KnowledgeStatement statement)
        {
            ordered.Add(statement);
            foreach (var child in about[statement.Number])
            {
                Visit(child);
            }
        }

        foreach (var statement in _statements.Where(statement => statement.Relation == KnowledgeStatement.Recorded))
        {
            Visit(statement);
        }

        foreach (var statement in _statements.Where(statement => statement.Relation == KnowledgeStatement.New))
        {
            Visit(statement);
        }

        return ordered;
    }

    /// <summary>What a statement is, as a request gives it: its layer, its moment, and what it is about.</summary>
    public static string Label(KnowledgeStatement statement, bool withNumbers)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var layer = statement.Layer switch
        {
            1 => $"layer 1, the recorded data, {statement.Claim.Section}",
            2 => $"layer 2, the captured frame at moment {statement.Moment}",
            _ => $"layer 3, the web page at moment {statement.Moment}"
        };
        var relation = statement.Relation switch
        {
            KnowledgeStatement.Elaborates => withNumbers ? $", elaborates {Reference(statement.About!.Value)}" : ", elaborating the statement before it",
            KnowledgeStatement.Corrects => withNumbers ? $", corrects {Reference(statement.About!.Value)}" : ", correcting or disagreeing with the statement it follows",
            _ => ""
        };
        return string.Create(CultureInfo.InvariantCulture, $"({layer}{relation}, inferred)");
    }

    /// <summary>
    /// The knowledge as a request gives it, in reading order, each
    /// statement with its label and its evidence, and with its number
    /// where the request's answer may cite it.
    /// </summary>
    public void Append(StringBuilder input, ISet<string> references, bool withNumbers, string indent = "  ")
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(references);
        foreach (var statement in InOrder())
        {
            var depth = 0;
            for (var at = statement; at.About is { } parent; at = _statements[parent - 1])
            {
                depth++;
            }

            input.Append(indent).Append(' ', depth * 2);
            if (withNumbers)
            {
                references.Add(statement.Reference);
                input.Append('[').Append(statement.Reference).Append("] ");
            }

            input.Append(Label(statement, withNumbers)).Append(' ').Append(statement.Claim.Text);
            references.UnionWith(statement.Claim.Evidence);
            if (statement.Claim.Evidence.Count > 0)
            {
                input.Append(' ').Append(string.Join(' ', statement.Claim.Evidence.Select(reference => $"[{reference}]")));
            }

            input.Append('\n');
        }
    }
}

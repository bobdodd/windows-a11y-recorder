using System.Text;

namespace Recorder.Analysis;

/// <summary>
/// The answer's grammar, written by the recorder in llama.cpp's grammar
/// format: the structure of <see cref="ClaimCheck.Schema"/>, with no space
/// or line break allowed between tokens, so that the answer is JSON on one
/// line. A schema sent as response_format is turned by llama.cpp into a
/// grammar that allows up to two line breaks and 20 spaces or tabs between
/// tokens, with no option to change it
/// (https://raw.githubusercontent.com/ggml-org/llama.cpp/master/common/json-schema-to-grammar.cpp,
/// SPACE_RULE), and every answer so far was indented over many lines
/// although the instructions asked for one line. See recording-summary.md,
/// "The layered report", and the grammar format at
/// https://github.com/ggml-org/llama.cpp/blob/master/grammars/README.md.
/// </summary>
public static class AnswerGrammar
{
    public static string For(IReadOnlyList<string> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Count == 0 || sections.Any(section => section.Length == 0 || section.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))))
        {
            throw new ArgumentException("Each section is named with letters, digits, hyphens, and underscores.", nameof(sections));
        }

        var root = new StringBuilder("root ::= \"{\"");
        for (var index = 0; index < sections.Count; index++)
        {
            root.Append(index == 0 ? " " : " \",\" ").Append("\"\\\"").Append(sections[index]).Append("\\\":\" section");
        }

        root.Append(" \"}\"");
        return string.Join('\n',
            root.ToString(),
            "section ::= \"[\" (paragraph (\",\" paragraph)*)? \"]\"",
            "paragraph ::= \"[\" (statement (\",\" statement)*)? \"]\"",
            "statement ::= \"{\\\"text\\\":\" text \",\\\"kind\\\":\" kind \",\\\"evidence\\\":\" evidence \"}\"",
            "text ::= \"\\\"\" char+ \"\\\"\"",
            $"kind ::= \"\\\"{Claim.ClaimKind}\\\"\" | \"\\\"{Claim.UnknownKind}\\\"\"",
            "evidence ::= \"[\" (string (\",\" string)*)? \"]\"",
            "string ::= \"\\\"\" char* \"\\\"\"",
            "char ::= [^\"\\\\\\x7F\\x00-\\x1F] | [\\\\] ([\"\\\\/bfnrt] | \"u\" [0-9a-fA-F]{4})") + "\n";
    }
}

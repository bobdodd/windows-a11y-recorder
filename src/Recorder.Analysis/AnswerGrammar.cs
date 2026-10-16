using System.Text;

namespace Recorder.Analysis;

/// <summary>
/// The answer's grammar, written by the recorder in llama.cpp's grammar
/// format: the structure of <see cref="ClaimCheck.Schema"/>, with no line
/// break allowed between tokens and at most one space, after a colon or a
/// comma, so that the answer is JSON on one line. A first trial, allowing
/// no space at all, gave an empty answer on the target machine on
/// 2026-10-15 (recording-summary.md, "The layered report"). A schema sent as response_format is turned by llama.cpp into a
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
            root.Append(index == 0 ? " " : " \",\" sp ").Append("\"\\\"").Append(sections[index]).Append("\\\":\" sp section");
        }

        root.Append(" \"}\"");
        return string.Join('\n',
            root.ToString(),
            "sp ::= \" \"?",
            "section ::= \"[\" (paragraph (\",\" sp paragraph)*)? \"]\"",
            "paragraph ::= \"[\" (statement (\",\" sp statement)*)? \"]\"",
            "statement ::= \"{\\\"text\\\":\" sp text \",\" sp \"\\\"kind\\\":\" sp kind \",\" sp \"\\\"evidence\\\":\" sp evidence \"}\"",
            "text ::= \"\\\"\" char+ \"\\\"\"",
            $"kind ::= \"\\\"{Claim.ClaimKind}\\\"\" | \"\\\"{Claim.UnknownKind}\\\"\"",
            "evidence ::= \"[\" (string (\",\" sp string)*)? \"]\"",
            "string ::= \"\\\"\" char* \"\\\"\"",
            "char ::= [^\"\\\\\\x7F\\x00-\\x1F] | [\\\\] ([\"\\\\/bfnrt] | \"u\" [0-9a-fA-F]{4})") + "\n";
    }
}

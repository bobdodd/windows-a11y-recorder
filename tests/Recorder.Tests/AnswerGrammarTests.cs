using System.Text.Json.Nodes;
using Recorder.Analysis;

namespace Recorder.Tests;

/// <summary>
/// The answer held to JSON on one line by the recorder's own grammar, for
/// trials: recording-summary.md, "The layered report".
/// </summary>
public sealed class AnswerGrammarTests
{
    [Fact]
    public void The_grammar_has_the_schemas_structure_with_no_line_break_and_at_most_one_space_after_a_colon_or_comma()
    {
        Assert.Equal(
            "root ::= \"{\" \"\\\"page\\\":\" sp section \",\" sp \"\\\"changes\\\":\" sp section \"}\"\n" +
            "sp ::= \" \"?\n" +
            "section ::= \"[\" (paragraph (\",\" sp paragraph)*)? \"]\"\n" +
            "paragraph ::= \"[\" (statement (\",\" sp statement)*)? \"]\"\n" +
            "statement ::= \"{\\\"text\\\":\" sp text \",\" sp \"\\\"kind\\\":\" sp kind \",\" sp \"\\\"evidence\\\":\" sp evidence \"}\"\n" +
            "text ::= \"\\\"\" char+ \"\\\"\"\n" +
            "kind ::= \"\\\"claim\\\"\" | \"\\\"unknown\\\"\" | \"\\\"none\\\"\"\n" +
            "evidence ::= \"[\" (string (\",\" sp string)*)? \"]\"\n" +
            "string ::= \"\\\"\" char* \"\\\"\"\n" +
            "char ::= [^\"\\\\\\x7F\\x00-\\x1F] | [\\\\] ([\"\\\\/bfnrt] | \"u\" [0-9a-fA-F]{4})\n",
            AnswerGrammar.For(["page", "changes"]));
        Assert.Throws<ArgumentException>(() => AnswerGrammar.For(["a \"section\""]));
        Assert.Throws<ArgumentException>(() => AnswerGrammar.For([]));
    }

    [Fact]
    public void A_request_held_to_one_line_sends_the_grammar_in_place_of_the_schema()
    {
        var profile = new ModelProfile("This PC", new Uri("http://127.0.0.1:8080/"), "qwen3.6-35b-a3b", [ModelCapabilities.Text],
            new ModelFile("model.gguf", "Q4_K_M", new string('b', 64), null), null, GenerationSettings.Default, null);
        var request = new ModelRequest("template", 1, "Answer in JSON.", "The input.", [], ["page", "changes"], new HashSet<string> { "E1" });

        var usual = JsonNode.Parse(ChatRequest.Build(profile, request))!.AsObject();
        var oneLine = JsonNode.Parse(ChatRequest.Build(profile, request, oneLine: true))!.AsObject();

        Assert.True(usual.ContainsKey("response_format"));
        Assert.False(usual.ContainsKey("grammar"));
        Assert.False(oneLine.ContainsKey("response_format"));
        Assert.Equal(AnswerGrammar.For(["page", "changes"]), (string)oneLine["grammar"]!);
        usual.Remove("response_format");
        oneLine.Remove("grammar");
        Assert.True(JsonNode.DeepEquals(usual, oneLine));
    }
}

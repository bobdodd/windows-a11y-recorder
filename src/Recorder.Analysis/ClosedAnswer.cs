using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis;

/// <summary>
/// A check's answer to one narrow question (android-vision-checks.md,
/// "Keeping it repeatable", points 1 and 2): one of the question's fixed
/// answers, and one sentence saying what shows it.
/// </summary>
public sealed record ClosedAnswerOf(string Answer, string Reason);

/// <summary>
/// The shape a check's question is answered in, and the recorder's own check
/// of it: a JSON object with an "answer", one of the fixed answers, and a
/// "reason" that is not empty. Output that fails is refused, never repaired.
/// </summary>
public static class ClosedAnswer
{
    public const string AnswerMember = "answer";

    public const string ReasonMember = "reason";

    /// <summary>The JSON schema the server is asked to hold the answer to.</summary>
    public static JsonObject Schema(IReadOnlyList<string> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                [AnswerMember] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray([.. answers.Select(answer => JsonValue.Create(answer))]),
                },
                [ReasonMember] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
            },
            ["required"] = new JsonArray(AnswerMember, ReasonMember),
            ["additionalProperties"] = false,
        };
    }

    /// <summary>The answer, or why it is refused.</summary>
    public static (JsonElement Output, string? Failure) Check(string? output, IReadOnlyList<string> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        if (string.IsNullOrWhiteSpace(output))
        {
            return (default, "The output is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (default, "The output is not a JSON object.");
            }

            if (!root.TryGetProperty(AnswerMember, out var answer) || answer.ValueKind != JsonValueKind.String)
            {
                return (default, "The output has no answer.");
            }

            if (!answers.Contains(answer.GetString(), StringComparer.Ordinal))
            {
                return (default, $"The answer \"{answer.GetString()}\" is not one of the question's answers: {string.Join(", ", answers)}.");
            }

            if (!root.TryGetProperty(ReasonMember, out var reason) || reason.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(reason.GetString()))
            {
                return (default, "The answer gives no reason.");
            }

            foreach (var member in root.EnumerateObject())
            {
                if (member.Name is not (AnswerMember or ReasonMember))
                {
                    return (default, $"The output has a member \"{member.Name}\" the question does not ask for.");
                }
            }

            return (root.Clone(), null);
        }
        catch (JsonException exception)
        {
            return (default, $"The output is not valid JSON: {exception.Message}");
        }
    }

    /// <summary>An accepted answer as the check reads it, or null where the output is not one.</summary>
    public static ClosedAnswerOf? Read(JsonElement output)
    {
        if (output.ValueKind != JsonValueKind.Object ||
            !output.TryGetProperty(AnswerMember, out var answer) || answer.ValueKind != JsonValueKind.String ||
            !output.TryGetProperty(ReasonMember, out var reason) || reason.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return new ClosedAnswerOf(answer.GetString()!, reason.GetString()!);
    }

    /// <summary>
    /// The attempt's recorded settings with the question's answers, where
    /// it has them, so that every other step's settings are as they were and
    /// a run again reuses those steps.
    /// </summary>
    internal static JsonObject WithAnswers(this JsonObject parameters, ModelRequest request)
    {
        if (request.Answers is { } answers)
        {
            parameters["answers"] = new JsonArray([.. answers.Select(answer => JsonValue.Create(answer))]);
        }

        return parameters;
    }
}

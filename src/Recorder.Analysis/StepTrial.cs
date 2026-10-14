using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis;

/// <summary>
/// What a trial of one step gave: the settings sent, with the server's
/// defaults for the others; the answer; and why it would have failed, or
/// null where it passes the checks.
/// </summary>
public sealed record StepTrial(
    Guid TaskId,
    string Kind,
    string StepKey,
    string Profile,
    JsonObject Parameters,
    string? ServerBuild,
    ModelResponse? Response,
    string? Failure,
    int Statements,
    TimeSpan Elapsed)
{
    /// <summary>The trial as a text file for the owner to read.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        var indented = new JsonSerializerOptions { WriteIndented = true };
        text.Append(CultureInfo.InvariantCulture, $"Trial of step {StepKey} of task {TaskId} ({Kind}), with profile {Profile}.\n");
        text.Append("Nothing was written to the catalog.\n\n");
        text.Append("Outcome: ").Append(Failure is null ? "passes the checks." : "fails: " + Failure).Append("\n");
        text.Append(CultureInfo.InvariantCulture, $"Statements: {Statements}\n");
        text.Append(CultureInfo.InvariantCulture, $"Time taken, as the worker measured it: {Elapsed.TotalSeconds:0.0} s\n");
        if (Response is { } response)
        {
            text.Append("Finish reason: ").Append(response.FinishReason ?? "none, the worker stopped the answer").Append("\n");
            text.Append("Prompt tokens: ").Append(Number(response.PromptTokens)).Append("\n");
            text.Append("Output tokens: ").Append(Number(response.CompletionTokens)).Append("\n");
            text.Append("Prompt time, ms: ").Append(Number(response.PromptMilliseconds)).Append("\n");
            text.Append("Output time, ms: ").Append(Number(response.CompletionMilliseconds)).Append("\n");
        }
        else
        {
            text.Append("No answer.\n");
        }

        text.Append("Server build: ").Append(ServerBuild ?? "not given").Append("\n\n");
        text.Append("Settings sent, with the server's defaults for the rest (server_defaults):\n");
        text.Append(Parameters.ToJsonString(indented)).Append("\n\n");
        text.Append("The answer, exactly as it arrived:\n");
        text.Append(Response?.Content ?? "").Append("\n");
        return text.ToString();
    }

    private static string Number(double? value) =>
        value is { } number ? number.ToString("0.###", CultureInfo.InvariantCulture) : "not given";
}

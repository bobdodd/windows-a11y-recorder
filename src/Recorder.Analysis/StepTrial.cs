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
    /// <summary>The SHA-256 of the step's request as the task itself would send it, without the trial's options.</summary>
    public string? RequestSha256 { get; init; }

    /// <summary>The SHA-256 of the request the trial sent.</summary>
    public string? SentSha256 { get; init; }

    /// <summary>The task's attempts at the step.</summary>
    public IReadOnlyList<StepRequest> TaskAttempts { get; init; } = [];

    /// <summary>Whether the answer was held to one line by the recorder's own grammar, sent in place of the schema.</summary>
    public bool OneLine { get; init; }

    /// <summary>Where the trial kept the pictures it took, in the recording's folder.</summary>
    public string? PicturesFolder { get; init; }

    /// <summary>The trial as a text file for the owner to read.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        var indented = new JsonSerializerOptions { WriteIndented = true };
        text.Append(CultureInfo.InvariantCulture, $"Trial of step {StepKey} of task {TaskId} ({Kind}), with profile {Profile}.\n");
        text.Append("Nothing was written to the catalog.\n");
        if (PicturesFolder is { } pictures)
        {
            text.Append("Pictures taken for the trial, if any, are in the recording's folder under ").Append(pictures).Append(", not in the task's.\n");
        }

        if (OneLine)
        {
            text.Append("The answer was held to JSON on one line by the recorder's own grammar, sent as grammar in place of the schema.\n");
        }

        text.Append('\n');
        if (RequestSha256 is { } asTask)
        {
            text.Append("The step's request as the task would send it, without the trial's options: SHA-256 ").Append(asTask).Append('\n');
            text.Append("The request the trial sent: SHA-256 ").Append(SentSha256 ?? "not given")
                .Append(SentSha256 == asTask ? ", the same.\n" : ", with the trial's options.\n");
            if (TaskAttempts.Count == 0)
            {
                text.Append("The task has no attempt at this step.\n");
            }

            foreach (var attempt in TaskAttempts)
            {
                text.Append(CultureInfo.InvariantCulture, $"The task's attempt {attempt.AttemptNumber}, {attempt.Outcome}: SHA-256 {attempt.InputSha256}, ")
                    .Append(attempt.InputSha256 == asTask ? "the same request.\n" : "not the same request.\n");
            }

            text.Append('\n');
        }

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

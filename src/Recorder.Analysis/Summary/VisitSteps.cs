using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Recorder.Analysis.Summary;

/// <summary>A line of the visit in time order: its reference, its clock time, and its text.</summary>
public sealed record StepLine(string Reference, string Clock, string Text);

/// <summary>
/// One numbered step of a visit: an input, or the same input given more
/// than once with nothing found between, counted; and what the recorder
/// found after it and before the next input.
/// </summary>
public sealed record VisitStep(int Number, StepLine Input, int Count, string? LastClock, IReadOnlyList<string> InputReferences, IReadOnlyList<StepLine> Then)
{
    /// <summary>The input as the step gives it, with its count where it was given more than once.</summary>
    public string InputText => Count == 1
        ? $"{Input.Clock} {Input.Text}"
        : string.Create(CultureInfo.InvariantCulture, $"{Input.Clock} to {LastClock}, {Count} times: {Input.Text}");
}

/// <summary>
/// The subsection of a visit's story on what the participant did, written
/// by the recorder from the visit in time order, not by a model: what was
/// found before the first input, each input as a numbered step with what
/// was found after it, the inputs counted, and how the visit ended. The
/// owner, 2026-10-18, of the design review's proposal 4: "We clearly need
/// to reduce the workload of the model, and counts are something models
/// traditionally struggle with." See
/// docs/architecture/model-load-review-2026-10-18.md.
/// </summary>
public sealed partial record VisitSteps(IReadOnlyList<StepLine> Before, IReadOnlyList<VisitStep> Steps, string Counts, string Ending)
{
    /// <summary>The steps of a visit, from its timeline.</summary>
    /// <param name="last">Whether the visit is the recording's last, which ends with the recording.</param>
    /// <param name="recordingEnd">The recording's duration, where it ends.</param>
    public static VisitSteps Of(VisitCollation visit, bool last, long recordingEnd)
    {
        ArgumentNullException.ThrowIfNull(visit);
        var before = new List<StepLine>();
        var steps = new List<(StepLine Input, int Count, string? LastClock, List<string> References, List<StepLine> Then)>();
        foreach (var item in visit.Timeline)
        {
            var line = Parse(item.Line);
            if (!item.Input)
            {
                (steps.Count == 0 ? before : steps[^1].Then).Add(line);
                continue;
            }

            // The same input again, with nothing found since the last: one
            // step, counted.
            if (steps.Count > 0 && steps[^1].Then.Count == 0 && steps[^1].Input.Text == line.Text)
            {
                var same = steps[^1];
                same.References.Add(line.Reference);
                steps[^1] = (same.Input, same.Count + 1, line.Clock, same.References, same.Then);
                continue;
            }

            steps.Add((line, 1, null, [line.Reference], []));
        }

        var numbered = steps.Select((step, index) => new VisitStep(index + 1, step.Input, step.Count, step.LastClock, step.References, step.Then)).ToList();
        var ending = last
            ? $"The recording ended at {SummaryCollator.Time(recordingEnd)}."
            : $"The visit ended at {SummaryCollator.Time(visit.Visit.End)}, when the next visit began.";
        return new VisitSteps(before, numbered, CountsOf(numbered), ending);
    }

    // The inputs counted, by what was given: "Tab, 3 presses".
    private static string CountsOf(IReadOnlyList<VisitStep> steps)
    {
        if (steps.Count == 0)
        {
            return "The recorder recorded no input from the participant during the visit.";
        }

        var counts = new List<(string What, int Count, string Unit)>();
        var total = 0;
        foreach (var step in steps)
        {
            var (what, count, unit) = Kind(step.Input.Text);
            total += step.Count * count;
            var at = counts.FindIndex(item => item.What == what);
            if (at < 0)
            {
                counts.Add((what, step.Count * count, unit));
            }
            else
            {
                counts[at] = (what, counts[at].Count + (step.Count * count), unit);
            }
        }

        var parts = counts.Select(item => string.Create(CultureInfo.InvariantCulture,
            $"{item.What}, {item.Count} {(item.Count == 1 ? item.Unit : item.Unit == "press" ? "presses" : item.Unit + "s")}"));
        return string.Create(CultureInfo.InvariantCulture,
            $"The recorder recorded {total} {(total == 1 ? "input" : "inputs")} from the participant during the visit, in {steps.Count} {(steps.Count == 1 ? "step" : "steps")}: {string.Join("; ", parts)}.");
    }

    // What an input is, how many it counts, and its unit: a key by its name,
    // a run of characters by its keys, a mouse button by its name.
    private static (string What, int Count, string Unit) Kind(string text)
    {
        if (KeyPressed().Match(text) is { Success: true } key)
        {
            return (key.Groups[1].Value, 1, "press");
        }

        if (CharactersTyped().Match(text) is { Success: true } typed)
        {
            return ("characters typed", int.Parse(typed.Groups[1].Value, CultureInfo.InvariantCulture), "key");
        }

        if (MouseButton().Match(text) is { Success: true } mouse)
        {
            return ($"the mouse's {mouse.Groups[1].Value} button", 1, "press");
        }

        return (text, 1, "time");
    }

    /// <summary>The steps as a request gives them, each line with its reference.</summary>
    public void AppendInput(StringBuilder input, ISet<string> references)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(references);
        input.Append("What the participant did, written by the recorder in the report's subsection of that name: the inputs counted, then each step, an input and what the recorder found after it and before the next. ")
            .Append("What follows an input was found after it; that the input caused it is shown only where a fact says so:\n")
            .Append(Counts).Append('\n');
        foreach (var line in Before)
        {
            references.Add(line.Reference);
            input.Append("Before the first input: [").Append(line.Reference).Append("] ").Append(line.Clock).Append(' ').Append(line.Text).Append('\n');
        }

        foreach (var step in Steps)
        {
            references.UnionWith(step.InputReferences);
            input.Append(CultureInfo.InvariantCulture, $"Step {step.Number}: ")
                .Append(string.Join(' ', step.InputReferences.Select(reference => $"[{reference}]"))).Append(' ')
                .Append(step.InputText).Append('\n');
            foreach (var line in step.Then)
            {
                references.Add(line.Reference);
                input.Append("  then: [").Append(line.Reference).Append("] ").Append(line.Clock).Append(' ').Append(line.Text).Append('\n');
            }
        }

        input.Append(Ending).Append("\n\n");
    }

    // A timeline line as the collation writes it: "[E12] 00:00:12.300 Text".
    private static StepLine Parse(string line)
    {
        var match = TimelineLine().Match(line);
        return match.Success
            ? new StepLine(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value)
            : throw new FormatException($"A timeline line is not as the collation writes it: {line}");
    }

    [GeneratedRegex(@"^\[(E\d+)\] (\S+) (.*)$", RegexOptions.Singleline)]
    private static partial Regex TimelineLine();

    [GeneratedRegex(@"^Key pressed: ([^,]+)")]
    private static partial Regex KeyPressed();

    [GeneratedRegex(@"^Characters typed, (\d+) keys")]
    private static partial Regex CharactersTyped();

    [GeneratedRegex(@"^Mouse (\S+) button pressed")]
    private static partial Regex MouseButton();
}

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis;

/// <summary>
/// What the player needs from the analysis library that is not drawing:
/// the names it shows, what changed between two reads of the tasks, the
/// worker's status in words, the settings file, and starting the worker. See docs/architecture/analysis-runner.md,
/// "The player".
/// </summary>
public static class AnalysisPlayer
{
    /// <summary>The worker program, in the player's own folder.</summary>
    public const string WorkerFileName = "Recorder.AnalysisWorker.exe";

    /// <summary>A task kind's name as the player shows it.</summary>
    public static string KindName(string kind) => kind switch
    {
        Summary.RecordingSummaryKind.Name => "Recording summary",
        Summary.RecordingSummaryVisionKind.Name => "Summary with vision",
        Android.AndroidReportKind.Name => "Android report",
        Android.AndroidProjectReportKind.Name => "Android report for the project",
        Android.AndroidSummaryKind.Name => "Android summary",
        _ => kind
    };

    /// <summary>
    /// Whether a kind of task cannot be made without a model: the summaries.
    /// Where the project's settings choose no model for report analysis
    /// they are not queued, as no model means none at all, cloud or local
    /// (owner, 2026-11-15; projects.md, "The project's models"). The
    /// Android reports need none, so are queued either way.
    /// </summary>
    public static bool NeedsModel(string kind) =>
        kind is Summary.RecordingSummaryKind.Name or Summary.RecordingSummaryVisionKind.Name or Android.AndroidSummaryKind.Name;

    /// <summary>
    /// Queues vision for a recording, from the beginning: a summary first,
    /// then vision after it, so that vision is added to a summary written
    /// from the recorder's facts as they are now. Where a summary is queued
    /// or running, vision is added to it and no other is queued; otherwise
    /// a new summary is queued, as a run again of the newest finished one,
    /// so that its steps whose requests are unchanged are reused. Both are
    /// queued with the profile given, the project's model for report
    /// analysis, which must take images (projects.md, "The project's
    /// models"). Returns the summary's and the vision task's IDs (owner, 2026-10-16:
    /// "Either fix this so we can restart or start from ther beginning";
    /// recording-summary.md, "Vision starts from the beginning").
    /// </summary>
    public static async Task<(Guid Summary, Guid Vision, bool SummaryQueued)> QueueVisionAsync(
        AnalysisCatalog catalog,
        Guid recordingId,
        string sessionDirectory,
        string reportProfile,
        Guid? rerunOfTaskId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var summaries = (await catalog.TasksAsync(recordingId, cancellationToken).ConfigureAwait(false))
            .Where(task => task.Kind == Summary.RecordingSummaryKind.Name)
            .OrderByDescending(task => task.QueuedUtc)
            .ToList();
        var summaryVersion = new Summary.RecordingSummaryKind().Version;
        Guid summary;
        var queued = false;
        if (summaries.FirstOrDefault(task => task.State is AnalysisTaskStates.Queued or AnalysisTaskStates.Running) is { } pending)
        {
            summary = pending.TaskId;
        }
        else
        {
            var finished = summaries.FirstOrDefault(task => task.State == AnalysisTaskStates.Done);
            var profiles = new Dictionary<string, string> { [ModelCapabilities.Text] = reportProfile };
            summary = await catalog.QueueTaskAsync(
                recordingId, Summary.RecordingSummaryKind.Name, summaryVersion, profiles,
                sessionDirectory: sessionDirectory, rerunOfTaskId: finished?.TaskId, cancellationToken: cancellationToken).ConfigureAwait(false);
            queued = true;
        }

        var vision = await catalog.QueueTaskAsync(
            recordingId, Summary.RecordingSummaryVisionKind.Name, new Summary.RecordingSummaryVisionKind().Version,
            new Dictionary<string, string> { [ModelCapabilities.Text] = reportProfile, [ModelCapabilities.Image] = reportProfile },
            afterTaskId: summary, sessionDirectory: sessionDirectory, rerunOfTaskId: rerunOfTaskId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return (summary, vision, queued);
    }

    /// <summary>
    /// Where the worker saves the output of the step that failed a task: in
    /// the task's folder, beside where its report would be.
    /// </summary>
    public static string FailedOutputPath(string sessionDirectory, Guid taskId) =>
        Path.Combine(sessionDirectory, "analysis", taskId.ToString("D"), "failed-step-output.txt");

    /// <summary>A task's state as the player shows it.</summary>
    public static string StateName(string state) => state switch
    {
        AnalysisTaskStates.Queued => "Queued",
        AnalysisTaskStates.Running => "Running",
        AnalysisTaskStates.Done => "Finished",
        AnalysisTaskStates.Failed => "Failed",
        AnalysisTaskStates.Cancelled => "Cancelled",
        _ => state
    };

    /// <summary>Whether a task has finished, with a result or without.</summary>
    public static bool Ended(string state) =>
        state is AnalysisTaskStates.Done or AnalysisTaskStates.Failed or AnalysisTaskStates.Cancelled;

    /// <summary>The results not yet opened: finished or failed tasks with no read time.</summary>
    public static int Unread(IEnumerable<AnalysisTaskSummary> tasks) =>
        tasks.Count(task => task.ReadUtc is null && task.State is AnalysisTaskStates.Done or AnalysisTaskStates.Failed);

    /// <summary>
    /// What to tell the auditor about tasks that finished or failed since
    /// the last read of the queue, one message each. A task not in the
    /// last read is told of only when it ended since then, so results
    /// waiting when the player opens are shown as unread, not announced.
    /// </summary>
    public static IReadOnlyList<string> Changes(IReadOnlyList<AnalysisTaskSummary>? previous, IReadOnlyList<AnalysisTaskSummary> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous is null)
        {
            return [];
        }

        var before = previous.ToDictionary(task => task.TaskId);
        var messages = new List<string>();
        foreach (var task in current.OrderBy(task => task.FinishedUtc ?? DateTime.MaxValue))
        {
            if (!before.TryGetValue(task.TaskId, out var earlier) || earlier.State == task.State)
            {
                continue;
            }

            var name = KindName(task.Kind);
            switch (task.State)
            {
                case AnalysisTaskStates.Done:
                    messages.Add($"{name} finished. Its result is in the Analysis tab.");
                    break;
                case AnalysisTaskStates.Failed:
                    messages.Add($"{name} failed: {task.Failure ?? "no reason was recorded"}");
                    break;
                case AnalysisTaskStates.Cancelled:
                    messages.Add($"{name} was cancelled.");
                    break;
            }
        }

        return messages;
    }

    /// <summary>
    /// The worker's line in the Analysis tab, from its status file and
    /// whether its process is running: what it is doing and the model
    /// loaded, or that it has stopped.
    /// </summary>
    public static string WorkerLine(WorkerStatus? status, bool running)
    {
        if (status is null)
        {
            return running ? "The analysis worker is starting." : "The analysis worker is stopped.";
        }

        var time = status.UpdatedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        if (!running)
        {
            return $"The analysis worker is stopped. Its last status, at {time}: {status.Message}";
        }

        var model = status.LoadedProfile is { Length: > 0 } profile ? $" Model loaded: {profile}." : " No model is loaded.";
        return $"The analysis worker is running: {status.Message}{model}";
    }

    /// <summary>Whether the process the status names is still the worker.</summary>
    public static bool WorkerRunning(WorkerStatus? status)
    {
        if (status is null)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(status.ProcessId);
            return !process.HasExited &&
                string.Equals(process.ProcessName, Path.GetFileNameWithoutExtension(WorkerFileName), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts the worker from a folder, hidden. A second worker exits at
    /// once, so starting one while one runs does no harm. Returns why it
    /// could not start, or null.
    /// </summary>
    public static string? StartWorker(string directory)
    {
        var path = Path.Combine(directory, WorkerFileName);
        if (!File.Exists(path))
        {
            return $"The analysis worker is not installed: {path} is not there.";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory
            });
            return process is null ? "The analysis worker did not start." : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"The analysis worker could not start: {exception.Message}";
        }
    }

    /// <summary>The settings file as written when there is none: no profiles.</summary>
    public static string EmptySettings =>
        new JsonObject
        {
            ["version"] = AnalysisSettingsFile.Version,
            ["maxAttempts"] = AnalysisSettings.DefaultMaxAttempts,
            ["profiles"] = new JsonArray()
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Writes the empty settings file when there is none, so it can be opened to edit.</summary>
    public static void EnsureSettingsFile(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, EmptySettings);
        }
    }

    /// <summary>
    /// The choices for a use on the project's settings, in order: no model
    /// first, then each profile that can do what the use needs, by its
    /// label, then the saved choice where no profile of that name can any
    /// longer, so that it is shown and not silently dropped. Each is the
    /// profile's name, null for no model, and its label.
    /// </summary>
    public static IReadOnlyList<(string? Name, string Label)> ModelChoices(AnalysisSettings settings, string use, string? saved)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var capability = ModelUses.Capability(use);
        var choices = new List<(string?, string)> { (null, "No model") };
        choices.AddRange(settings.Profiles.Where(profile => profile.Supports(capability)).Select(profile => ((string?)profile.Name, ProfileLabel(profile))));
        if (saved is not null && !choices.Any(choice => choice.Item1 == saved))
        {
            choices.Add((saved, settings.Profile(saved) is null
                ? $"{saved} (not offered on this PC)"
                : $"{saved} (cannot take images)"));
        }

        return choices;
    }

    /// <summary>
    /// A project's models in words, one use after another, as the
    /// Analysis and Checks tabs show them: each use's profile by its label,
    /// or no model.
    /// </summary>
    public static string ModelsLine(AnalysisSettings settings, ProjectModels models, IEnumerable<string> uses)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(models);
        return string.Join("; ", uses.Select(use => $"{ModelUses.Name(use)}, " +
            (models.For(use) is not { } name ? "no model" : settings.Profile(name) is { } profile ? ProfileLabel(profile) : $"{name} (not offered on this PC)")));
    }

    /// <summary>
    /// A task's models in words, for its row on the Analysis tab: the
    /// profiles it was queued with, by use, or that it was queued with no
    /// model. A summary's profiles are by capability, text and images,
    /// which are both report analysis; a check task's by the check's use.
    /// </summary>
    public static string TaskModelsLine(IReadOnlyDictionary<string, string> stageProfiles, bool aiAnalysis)
    {
        ArgumentNullException.ThrowIfNull(stageProfiles);
        if (!aiAnalysis || stageProfiles.Count == 0)
        {
            return "No model.";
        }

        var parts = new List<string>();
        var report = stageProfiles.Where(pair => pair.Key is ModelCapabilities.Text or ModelCapabilities.Image)
            .Select(pair => pair.Value).Distinct(StringComparer.Ordinal).ToList();
        if (report.Count > 0)
        {
            parts.Add($"{ModelUses.Name(ModelUses.Report)}, {string.Join(" and ", report)}");
        }

        foreach (var use in new[] { ModelUses.TextChecks, ModelUses.VisionChecks })
        {
            if (stageProfiles.TryGetValue(use, out var name))
            {
                parts.Add($"{ModelUses.Name(use)}, {name}");
            }
        }

        return parts.Count == 0 ? "No model." : "Models: " + string.Join("; ", parts) + ".";
    }

    /// <summary>
    /// A profile's name as the player lists it wherever a profile is
    /// chosen: with "(sends to Anthropic)" after it for a profile that does
    /// (cloud-models.md, "6. The player").
    /// </summary>
    public static string ProfileLabel(ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.SendsToAnthropic ? $"{profile.Name} (sends to Anthropic)" : profile.Name;
    }

    /// <summary>
    /// What "Check the profile" shows when Anthropic answered: the model it
    /// names, the tokens used, the estimated cost where the profile has
    /// prices, why it stopped, the request ID, and the answer, shortened.
    /// </summary>
    public static string CheckLine(ModelProfile profile, ModelResponse response)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(response);
        var culture = CultureInfo.CurrentCulture;
        var tokens = response.PromptTokens is { } input && response.CompletionTokens is { } output
            ? $"{input.ToString("N0", culture)} input and {output.ToString("N0", culture)} output tokens" +
              (profile.Prices is { } prices ? $", about US${prices.Cost(input, output).ToString("0.0000", culture)}" : "")
            : "no token counts";
        var answer = (response.Content ?? "").Trim();
        if (answer.Length > 200)
        {
            answer = answer[..200] + "...";
        }

        return $"Anthropic answered with model {response.ResponseModel ?? "(not named)"}, {tokens}, stop reason " +
            $"{response.FinishReason ?? "(none)"}" + (response.RequestId is { } id ? $", request {id}" : "") + ". " +
            (answer.Length == 0 ? "The answer has no text." : $"The answer: {answer}");
    }

    /// <summary>
    /// A task's line on the Analysis tab for what it sent to Anthropic: the
    /// attempts, the tokens, and the estimated cost at the prices its
    /// profiles held, or why there is no estimate.
    /// </summary>
    public static string UsageLine(ServiceUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var culture = CultureInfo.CurrentCulture;
        var attempts = usage.Attempts == 1 ? "1 attempt" : $"{usage.Attempts.ToString("N0", culture)} attempts";
        var line = $"Sent to Anthropic: {attempts}, {usage.InputTokens.ToString("N0", culture)} input and " +
            $"{usage.OutputTokens.ToString("N0", culture)} output tokens";
        if (usage.Cost is { } cost)
        {
            return line + $", about US${cost.ToString(cost < 0.01 && cost > 0 ? "0.0000" : "0.00", culture)} at the profile's prices. " +
                "Anthropic's bill is the record of what was charged.";
        }

        return line + (usage.Uncounted > 0
            ? $". No cost estimate: {(usage.Uncounted == 1 ? "1 attempt has" : $"{usage.Uncounted.ToString("N0", culture)} attempts have")} no token counts."
            : ". No cost estimate: the profile has no prices.");
    }
}

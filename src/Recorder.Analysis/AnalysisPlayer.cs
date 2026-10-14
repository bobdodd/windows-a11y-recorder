using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis;

/// <summary>
/// What the player needs from the analysis library that is not drawing:
/// the names it shows, what changed between two reads of the tasks, the
/// worker's status in words, the default profiles in the settings file,
/// and starting the worker. See docs/architecture/analysis-runner.md,
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
        _ => kind
    };

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

    /// <summary>The settings file as written when there is none: no profiles, no defaults.</summary>
    public static string EmptySettings =>
        new JsonObject
        {
            ["version"] = AnalysisSettingsFile.Version,
            ["maxAttempts"] = AnalysisSettings.DefaultMaxAttempts,
            ["profiles"] = new JsonArray(),
            ["defaults"] = new JsonObject()
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
    /// The settings file's text with the default profile for a capability
    /// set, or removed when null, and everything else as it was.
    /// </summary>
    public static string WithDefault(string settings, string capability, string? profile)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(capability);
        var root = JsonNode.Parse(settings) as JsonObject ??
            throw new InvalidDataException("The analysis settings file does not hold a JSON object.");
        if (root["defaults"] is not JsonObject defaults)
        {
            defaults = [];
            root["defaults"] = defaults;
        }

        if (profile is null)
        {
            defaults.Remove(capability);
        }
        else
        {
            defaults[capability] = profile;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The profile to offer first for a capability: the default where it
    /// names a profile that can do it, else the first that can.
    /// </summary>
    public static ModelProfile? DefaultProfile(AnalysisSettings settings, string capability)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Defaults.TryGetValue(capability, out var name) && settings.Profile(name) is { } named && named.Supports(capability))
        {
            return named;
        }

        return settings.Profiles.FirstOrDefault(profile => profile.Supports(capability));
    }
}

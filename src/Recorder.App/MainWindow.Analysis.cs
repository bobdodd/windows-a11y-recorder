using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Npgsql;
using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Analysis.Summary;

namespace Recorder.App;

// The player's analysis controls (analysis-runner.md, "The player", and
// "Decisions on the player (2026-10-13)"): the Analysis menu, the Analysis
// tab with the session's tasks, the unread count, the notifications, the
// reading view and the capability description. All of
// it works with the mouse alone, every target at least 44 by 44 pixels.
public partial class MainWindow
{
    private static readonly TimeSpan AnalysisInterval = TimeSpan.FromSeconds(3);

    private readonly List<ReportWindow> _reportWindows = [];
    private DispatcherTimer? _analysisTimer;
    private AnalysisCatalog? _analysisCatalog;
    private AnalysisSettings _analysisSettings = AnalysisSettings.Empty;
    private IReadOnlyList<string> _analysisSettingsProblems = [];
    private Guid? _analysisRecordingId;
    private string? _analysisSessionDirectory;
    private IReadOnlyList<AnalysisTaskSummary>? _analysisTasks;
    private IReadOnlyDictionary<Guid, ServiceUsage> _analysisUsage = new Dictionary<Guid, ServiceUsage>();
    private string _analysisTasksShown = "";
    private bool _analysisReading;

    private AnalysisCatalog? AnalysisCatalogOrNull =>
        _analysisCatalog ??= _database is null ? null : AnalysisCatalog.For(_database.Server.DataSource);

    // When the database has started: tasks still waiting from an earlier
    // run, such as before a restart, get a worker.
    private async Task StartAnalysisAsync()
    {
        UseInstalledModelServer();
        ReadAnalysisSettings();
        BindAnalysis();
        if (AnalysisCatalogOrNull is not { } catalog)
        {
            return;
        }

        try
        {
            if (await catalog.AnyQueuedAsync(CancellationToken.None))
            {
                StartWorker();
            }
        }
        catch (NpgsqlException exception)
        {
            ShowAnalysisStatus($"The analysis queue could not be read: {exception.Message}");
        }
    }

    private void ReadAnalysisSettings()
    {
        (_analysisSettings, _analysisSettingsProblems) = AnalysisSettingsFile.Read(AnalysisSettingsFile.DefaultPath);
        BrowserOnlyMenuItem.IsChecked = _analysisSettings.BrowserOnly;
        ShowProjectModels();
    }

    // A recording was opened, or none is: the tab shows its tasks.
    private void BindAnalysis()
    {
        _analysisRecordingId = null;
        _openProjectId = null;
        _openProjectModels = null;
        _projectShownFor = null;
        Title = AppTitle;
        AnalysisProjectTextBlock.Visibility = Visibility.Collapsed;
        _analysisSessionDirectory = _playbackArchive?.SessionDirectory;
        _analysisTasks = null;
        _analysisTasksShown = "";
        AnalysisTasksPanel.Children.Clear();
        if (_analysisTimer is null)
        {
            _analysisTimer = new DispatcherTimer { Interval = AnalysisInterval };
            _analysisTimer.Tick += async (_, _) => await ReadAnalysisAsync();
            _analysisTimer.Start();
        }

        ShowProjectModels();
        UpdateAnalysisControls();
        _ = ReadAnalysisAsync();
    }

    // The models the open recording's project can choose from: the Claude
    // models with its API key, and the models set up on this PC.
    private AnalysisSettings ProjectChoices => (_openProjectModels ?? ProjectModels.None).Choices(_analysisSettings);

    // The open recording's project's model for report analysis, where it
    // can be used, else null (projects.md, "The project's models").
    private string? ReportProfile =>
        _openProjectModels?.Report is { } name && ProjectModels.Problem(ProjectChoices, ModelUses.Report, name) is null ? name : null;

    private void UpdateAnalysisControls()
    {
        var open = _playbackArchive is not null && AnalysisCatalogOrNull is not null;
        // The summaries need a model, so are queued only where the
        // project's settings choose one for report analysis.
        var report = open && ReportProfile is not null;
        QueueSummaryButton.IsEnabled = report;
        SummariseMenuItem.IsEnabled = report;
        // The Android report needs phone screens, and the summary a model
        // too (android-report.md).
        var phone = open && _playbackArchive is { PhoneScreens.Recorded: true };
        QueueAndroidReportButton.IsEnabled = phone;
        AndroidReportMenuItem.IsEnabled = phone;
        QueueAndroidSummaryButton.IsEnabled = phone && report;
        AndroidSummaryMenuItem.IsEnabled = QueueAndroidSummaryButton.IsEnabled;
        // The project's report needs only the open recording's project;
        // the task leaves out its recordings with no phone screens.
        QueueAndroidProjectReportButton.IsEnabled = open && _openProjectId is not null;
        AndroidProjectReportMenuItem.IsEnabled = QueueAndroidProjectReportButton.IsEnabled;
        // Vision is all on the report analysis model, so it must take images.
        VisionMenuItem.IsEnabled = report && ProjectChoices.Profile(ReportProfile!)?.Supports(ModelCapabilities.Image) == true;
        AnalysisProjectSettingsButton.IsEnabled = open && _openProjectId is not null;
        CapabilitiesMenuItem.IsEnabled = open;
    }

    private void AnalysisMenu_SubmenuOpened(object sender, RoutedEventArgs e) => UpdateAnalysisControls();

    // Reads the worker's status and the open recording's tasks, tells the
    // auditor of tasks that ended since the last read, and shows them.
    private async Task ReadAnalysisAsync()
    {
        var status = WorkerStatus.Read(AnalysisLocation.StatusPath);
        WorkerStatusTextBlock.Text = AnalysisPlayer.WorkerLine(status, AnalysisPlayer.WorkerRunning(status)) +
            (_analysisSettingsProblems.Count == 0 ? "" : " Settings problems: " + string.Join(" ", _analysisSettingsProblems));
        if (_analysisReading || _playbackArchive is null || AnalysisCatalogOrNull is not { } catalog)
        {
            if (_playbackArchive is null)
            {
                ShowTasks([], "Open a recording to see its analysis.");
            }

            return;
        }

        _analysisReading = true;
        try
        {
            // The catalog names a recording by its session folder's name, as
            // the database's playback reader finds it.
            _analysisRecordingId ??= await catalog.RecordingIdAsync(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(_playbackArchive.SessionDirectory))),
                CancellationToken.None);
            if (_analysisRecordingId is not { } recording)
            {
                ShowTasks([], "This recording is not in the catalog, so it cannot be analysed.");
                return;
            }

            if (_projectShownFor != recording)
            {
                await ShowOpenProjectAsync(catalog, recording);
            }

            var tasks = await catalog.TasksAsync(recording, CancellationToken.None);
            _analysisUsage = await catalog.ServiceUsageAsync(recording, CancellationToken.None);
            foreach (var message in AnalysisPlayer.Changes(_analysisTasks, tasks))
            {
                ShowAnalysisStatus(message);
                _busy.AnnounceAnalysis(message);
            }

            // A task that asks the model for vision-based checks has kept
            // its answers by now, so the Checks window shows them.
            var answered = _analysisTasks is { } before && tasks.Any(task =>
                task.State == AnalysisTaskStates.Done &&
                task.Kind is AndroidModelChecksKind.Name or AndroidReportKind.Name or AndroidSummaryKind.Name or AndroidProjectReportKind.Name &&
                before.FirstOrDefault(earlier => earlier.TaskId == task.TaskId)?.State is { } was && was != AnalysisTaskStates.Done);
            _analysisTasks = tasks;
            UpdateAskChecksModelButton();
            if (answered)
            {
                _ = ApplyKeptAnswersAsync();
            }

            ShowTasks(tasks, tasks.Count == 0 ? "No analysis has been queued for this recording." : null);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or ObjectDisposedException)
        {
            WorkerStatusTextBlock.Text += $" The analysis queue could not be read: {exception.Message}";
        }
        finally
        {
            _analysisReading = false;
        }
    }

    private void ShowAnalysisStatus(string message)
    {
        AnalysisStatusTextBlock.Text = message;
        AnalysisStatusTextBlock.Visibility = Visibility.Visible;
    }

    // The task rows, made again only when a task changed, so that a row's
    // buttons do not move or lose focus between reads.
    private void ShowTasks(IReadOnlyList<AnalysisTaskSummary> tasks, string? empty)
    {
        var unread = AnalysisPlayer.Unread(tasks);
        var header = unread == 0 ? "Analysis" : $"Analysis ({unread.ToString(CultureInfo.CurrentCulture)} unread)";
        AnalysisWindowToggle.Content = header;
        AutomationProperties.SetName(AnalysisWindowToggle, header);
        // A task's usage is part of what is shown, so that its tokens and
        // cost are kept up to date while it runs.
        var shown = string.Join(";", tasks.Select(task => $"{task.TaskId}:{task.State}:{task.ReadUtc}:{task.FinishedUtc}:" +
            (_analysisUsage.TryGetValue(task.TaskId, out var usage) ? AnalysisPlayer.UsageLine(usage) : ""))) + "|" + empty;
        if (shown == _analysisTasksShown)
        {
            return;
        }

        _analysisTasksShown = shown;
        var focused = (System.Windows.Input.Keyboard.FocusedElement as FrameworkElement)?.Tag as string;
        AnalysisTasksPanel.Children.Clear();
        if (empty is not null)
        {
            AnalysisTasksPanel.Children.Add(new SelectableText { Text = empty, TextWrapping = TextWrapping.Wrap });
        }

        foreach (var task in tasks)
        {
            AnalysisTasksPanel.Children.Add(TaskRow(task));
        }

        if (focused is not null)
        {
            Restore(AnalysisTasksPanel, focused);
        }
    }

    private static void Restore(Panel panel, string tag)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(panel).OfType<FrameworkElement>())
        {
            if (child.Tag as string == tag && child.IsEnabled)
            {
                child.Focus();
                return;
            }

            if (child is Border { Child: Panel inner })
            {
                Restore(inner, tag);
            }
            else if (child is Panel nested)
            {
                Restore(nested, tag);
            }
        }
    }

    private static string Local(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    private FrameworkElement TaskRow(AnalysisTaskSummary task)
    {
        var name = AnalysisPlayer.KindName(task.Kind);
        var queued = Local(task.QueuedUtc);
        var state = AnalysisPlayer.StateName(task.State) +
            (task.ReadUtc is null && task.State is AnalysisTaskStates.Done or AnalysisTaskStates.Failed ? ", unread" : "");
        var lines = new StackPanel();
        lines.Children.Add(new SelectableText { Text = $"{name}: {state}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var times = AnalysisPlayer.TaskModelsLine(task.StageProfiles, task.AiAnalysis) +
            $" Queued {queued}" + (task.FinishedUtc is { } finished ? $", ended {Local(finished)}." : ".");
        lines.Children.Add(new SelectableText { Text = times, TextWrapping = TextWrapping.Wrap });
        if (_analysisUsage.TryGetValue(task.TaskId, out var usage))
        {
            lines.Children.Add(new SelectableText { Text = AnalysisPlayer.UsageLine(usage), TextWrapping = TextWrapping.Wrap });
        }

        if (task.Failure is { Length: > 0 } failure)
        {
            lines.Children.Add(new SelectableText { Text = "Why: " + failure, TextWrapping = TextWrapping.Wrap });
        }

        var label = $"{name} queued {queued}";
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        Button Add(string content, string action, string automationName, bool enabled, Func<Task> click)
        {
            var button = new Button
            {
                Content = content,
                Style = (Style)FindResource("AnalysisButton"),
                IsEnabled = enabled,
                Tag = $"{task.TaskId}:{action}"
            };
            AutomationProperties.SetName(button, automationName);
            button.Click += async (_, _) => await click();
            buttons.Children.Add(button);
            return button;
        }

        Add("Cancel", "cancel", $"Cancel {label}", task.State is AnalysisTaskStates.Queued or AnalysisTaskStates.Running, () => CancelTaskAsync(task));
        Add("Run again", "again", $"Run {label} again, reusing its unchanged steps", AnalysisPlayer.Ended(task.State), () => RunAgainAsync(task, reuse: true));
        Add("Run again from scratch", "scratch", $"Run {label} again from scratch", AnalysisPlayer.Ended(task.State), () => RunAgainAsync(task, reuse: false));
        Add(task.State == AnalysisTaskStates.Failed ? "Why it failed" : "Open result", "open",
            task.State == AnalysisTaskStates.Failed ? $"Why {label} failed" : $"Open the result of {label}",
            task.State is AnalysisTaskStates.Done or AnalysisTaskStates.Failed, () => OpenResultAsync(task));
        if (task.Kind is AndroidReportKind.Name or AndroidSummaryKind.Name or AndroidProjectReportKind.Name)
        {
            Add("Open in Word", "word", $"Open the Word document of {label}", task.State == AnalysisTaskStates.Done, () => OpenInWordAsync(task));
        }

        lines.Children.Add(buttons);
        var row = new Border
        {
            BorderBrush = (System.Windows.Media.Brush)FindResource("PanelBorder"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 8, 0, 8),
            Child = lines
        };
        AutomationProperties.SetName(row, $"{name}, {state}");
        return row;
    }

    // A task is queued for the version of its kind this build has, as the
    // worker runs only that version.
    private static int KindVersion(string kind) =>
        AnalysisTaskKinds.All.FirstOrDefault(item => item.Kind == kind)?.Version ?? 1;

    // The open recording's project's models as they are now, read again
    // before anything is queued, so that a change made in the Projects
    // window is used (owner, 2026-11-15: "you will use the settings I
    // choose").
    private async Task<ProjectModels?> CurrentModelsAsync(AnalysisCatalog catalog)
    {
        _openProjectModels = _openProjectId is { } project ? await catalog.ProjectModelsAsync(project, CancellationToken.None) : null;
        ShowProjectModels();
        UpdateAnalysisControls();
        return _openProjectModels;
    }

    // The profiles a task of a kind is queued with, from the project's
    // settings: the model for report analysis for a summary, which needs
    // one, and the models for checks for a report, which needs none but
    // asks the model for vision-based checks about images of text, text
    // shown as a heading and images with no text alternative where one is
    // chosen; the Android summary has both. Null,
    // with the reason shown, where a summary cannot be queued.
    private IReadOnlyDictionary<string, string>? ProfilesFor(string kind, ProjectModels? models)
    {
        if (!AnalysisPlayer.NeedsModel(kind))
        {
            return models?.ForChecks() ?? new Dictionary<string, string>();
        }

        if (models?.Report is not { } name)
        {
            ShowAnalysisStatus($"{AnalysisPlayer.KindName(kind)} needs a model, so it was not queued: the project's settings choose no model for report analysis. " +
                "Choose one in Project settings, in the Project menu or the Analysis window.");
            return null;
        }

        if (ProjectModels.Problem(models.Choices(_analysisSettings), ModelUses.Report, name) is { } problem)
        {
            ShowAnalysisStatus($"{AnalysisPlayer.KindName(kind)} was not queued. {problem} Choose another in Project settings.");
            return null;
        }

        // The Android summary is the Android report with summaries, so its
        // checks ask the project's models for checks as the report's do
        // (android-vision-checks.md, "Images of text, as built").
        var profiles = new Dictionary<string, string> { [ModelCapabilities.Text] = name };
        if (kind == AndroidSummaryKind.Name)
        {
            foreach (var (use, profile) in models.ForChecks())
            {
                profiles[use] = profile;
            }
        }

        return profiles;
    }

    // What a task was queued with, in words, for the status line.
    private string QueuedWith(string kind, IReadOnlyDictionary<string, string> profiles) =>
        profiles.Count == 0
            ? $"{AnalysisPlayer.KindName(kind)} queued with no model."
            : $"{AnalysisPlayer.KindName(kind)} queued. " + AnalysisPlayer.TaskModelsLine(
                profiles.ToDictionary(pair => pair.Key, pair => ProjectChoices.Profile(pair.Value) is { } profile ? AnalysisPlayer.ProfileLabel(profile) : pair.Value),
                aiAnalysis: true);

    // A recording's task, queued with the project's models as they are now.
    private async Task QueueAsync(string kind, Guid? rerunOfTaskId = null)
    {
        if (AnalysisCatalogOrNull is not { } catalog || _analysisRecordingId is not { } recording || _analysisSessionDirectory is null)
        {
            ShowAnalysisStatus("Open a recording that is in the catalog to queue its analysis.");
            return;
        }

        try
        {
            if (ProfilesFor(kind, await CurrentModelsAsync(catalog)) is not { } profiles)
            {
                return;
            }

            await catalog.QueueTaskAsync(recording, kind, KindVersion(kind), profiles, sessionDirectory: _analysisSessionDirectory, rerunOfTaskId: rerunOfTaskId, aiAnalysis: profiles.Count > 0, cancellationToken: CancellationToken.None);
            ShowAnalysisStatus(QueuedWith(kind, profiles));
            _busy.AnnounceCompleted(AnalysisStatusTextBlock.Text);
            StartWorker();
            await ReadAnalysisAsync();
        }
        catch (NpgsqlException exception)
        {
            ShowAnalysisStatus($"The task could not be queued: {exception.Message}");
        }
    }

    private void StartWorker()
    {
        if (AnalysisPlayer.StartWorker(AppContext.BaseDirectory) is { } problem)
        {
            ShowAnalysisStatus(problem);
        }
    }

    private async void QueueSummaryButton_Click(object sender, RoutedEventArgs e) => await QueueSummaryAsync();

    private async void SummariseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowAnalysisWindow();
        await QueueSummaryAsync();
    }

    private Task QueueSummaryAsync() => QueueAsync(RecordingSummaryKind.Name);

    // The Android report needs no model; it is queued with the project's
    // models for checks, and its images-of-text check, check of text shown
    // as a heading and check of images with no text alternative ask the
    // model for vision-based checks where one is chosen. The summary needs the
    // model for report analysis (android-report.md).
    private async void AndroidReportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowAnalysisWindow();
        await QueueAsync(AndroidReportKind.Name);
    }

    private async void AndroidSummaryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowAnalysisWindow();
        await QueueAsync(AndroidSummaryKind.Name);
    }

    // Opens a task's Word document in the program Windows opens .docx
    // files with (android-report.md, "Opening the report").
    private async Task OpenInWordAsync(AnalysisTaskSummary task)
    {
        if (AnalysisCatalogOrNull is not { } catalog || ReportsFolder(task) is not { } session)
        {
            return;
        }

        var reports = await catalog.ReportsAsync(task.TaskId, CancellationToken.None);
        var document = reports.FirstOrDefault(report => report.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase));
        if (document.RelativePath is null)
        {
            ShowAnalysisStatus("This task wrote no Word document.");
            return;
        }

        var path = Path.Combine(session, document.RelativePath);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            await catalog.MarkReadAsync(task.TaskId, CancellationToken.None);
            await ReadAnalysisAsync();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            ShowAnalysisStatus($"The Word document {path} could not be opened: {exception.Message}");
        }
    }

    private async void VisionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowAnalysisWindow();
        await QueueVisionAsync();
    }

    // Vision starts from the beginning: a summary is queued first, reusing
    // the newest finished summary's unchanged steps, and vision after it,
    // unless a summary is already queued or running, when vision is added
    // to that one; all of it on the project's model for report analysis,
    // which must take images (owner, 2026-10-16; recording-summary.md,
    // "Vision starts from the beginning"; projects.md, "The project's
    // models").
    private async Task QueueVisionAsync(Guid? rerunOfTaskId = null)
    {
        if (AnalysisCatalogOrNull is not { } catalog || _analysisRecordingId is not { } recording || _analysisSessionDirectory is not { } session)
        {
            ShowAnalysisStatus("Open a recording that is in the catalog to queue its analysis.");
            return;
        }

        try
        {
            if (ProfilesFor(RecordingSummaryVisionKind.Name, await CurrentModelsAsync(catalog)) is null)
            {
                return;
            }

            var name = _openProjectModels!.Report!;
            var profile = ProjectChoices.Profile(name)!;
            if (!profile.Supports(ModelCapabilities.Image))
            {
                ShowAnalysisStatus($"Summary with vision was not queued: the project's model for report analysis, {AnalysisPlayer.ProfileLabel(profile)}, cannot take images. " +
                    "Choose one that can in Project settings.");
                return;
            }

            var (_, _, summaryQueued) = await AnalysisPlayer.QueueVisionAsync(
                catalog, recording, session, name, rerunOfTaskId, CancellationToken.None);
            ShowAnalysisStatus(summaryQueued
                ? $"Recording summary queued, and Summary with vision after it, with {AnalysisPlayer.ProfileLabel(profile)}."
                : $"Summary with vision queued with {AnalysisPlayer.ProfileLabel(profile)}, after the summary already queued.");
            _busy.AnnounceCompleted(AnalysisStatusTextBlock.Text);
            StartWorker();
            await ReadAnalysisAsync();
        }
        catch (NpgsqlException exception)
        {
            ShowAnalysisStatus($"The task could not be queued: {exception.Message}");
        }
    }

    private async void RefreshAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        ReadAnalysisSettings();
        UpdateAnalysisControls();
        await ReadAnalysisAsync();
    }

    private async Task CancelTaskAsync(AnalysisTaskSummary task)
    {
        if (AnalysisCatalogOrNull is { } catalog)
        {
            await catalog.RequestCancelAsync(task.TaskId, CancellationToken.None);
            await ReadAnalysisAsync();
        }
    }

    // Run again queues a new task of the same kind, with the project's
    // models as they are now, not those the earlier task was queued with,
    // so that the settings chosen are used (owner, 2026-11-15); the earlier
    // one and its report stay (owner, 2026-10-13).
    // Vision run again starts from the beginning, as from the menu.
    // Run again reuses the earlier task's accepted steps whose requests are
    // unchanged; Run again from scratch runs every step (owner, 2026-10-14;
    // analysis-runner.md, "Reusing steps when run again").
    private Task RunAgainAsync(AnalysisTaskSummary task, bool reuse)
    {
        Guid? rerunOf = reuse ? task.TaskId : null;
        if (task.ProjectId is not null)
        {
            return QueueProjectAsync(task.Kind, rerunOf);
        }

        return task.Kind == RecordingSummaryVisionKind.Name ? QueueVisionAsync(rerunOf) : QueueAsync(task.Kind, rerunOf);
    }

    // A recording's reports are in its session folder, as the player has
    // it open; a project's in the recordings' folder it was queued with.
    private string? ReportsFolder(AnalysisTaskSummary task) =>
        task.ProjectId is not null ? task.SessionDirectory : _analysisSessionDirectory;

    private async Task OpenResultAsync(AnalysisTaskSummary task)
    {
        if (AnalysisCatalogOrNull is not { } catalog || ReportsFolder(task) is not { } session)
        {
            return;
        }

        if (task.State == AnalysisTaskStates.Failed)
        {
            await catalog.MarkReadAsync(task.TaskId, CancellationToken.None);
            var reason = task.Failure ?? "No reason was recorded.";
            var output = AnalysisPlayer.FailedOutputPath(session, task.TaskId);
            if (File.Exists(output))
            {
                // The model's output for the step that failed, opened in
                // Notepad on Yes (owner, 2026-10-16).
                if (MessageBox.Show(this, $"{reason}\n\nThe model's output for that step is saved in {output}. Open it in Notepad?",
                        $"Why {AnalysisPlayer.KindName(task.Kind)} failed", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    try
                    {
                        using var process = Process.Start(new ProcessStartInfo("notepad.exe", $"\"{output}\"") { UseShellExecute = false });
                    }
                    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                        ShowAnalysisStatus($"Notepad could not open {output}: {exception.Message}");
                    }
                }
            }
            else
            {
                MessageBox.Show(this, reason, $"Why {AnalysisPlayer.KindName(task.Kind)} failed", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await ReadAnalysisAsync();
            return;
        }

        var reports = await catalog.ReportsAsync(task.TaskId, CancellationToken.None);
        if (reports.Count == 0)
        {
            ShowAnalysisStatus("This task wrote no report.");
            return;
        }

        var (_, relative, sha256) = reports[^1];
        var path = Path.Combine(session, relative);
        string markdown;
        string? changed = null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), sha256, StringComparison.OrdinalIgnoreCase))
            {
                changed = "This file has changed since the recorder wrote it, so it may not be the task's result.";
            }

            markdown = System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowAnalysisStatus($"The report could not be read: {exception.Message}");
            return;
        }

        var window = new ReportWindow(path, ReportDocument.Parse(markdown), changed, nanoseconds => SeekToEvidence(session, nanoseconds));
        window.Closed += (_, _) => _reportWindows.Remove(window);
        _reportWindows.Add(window);
        window.Show();
        await catalog.MarkReadAsync(task.TaskId, CancellationToken.None);
        await ReadAnalysisAsync();
    }

    // A reference's button moves the playhead to its evidence, when the
    // report's recording is the one open.
    private string? SeekToEvidence(string session, long nanoseconds)
    {
        if (_playbackArchive is null || !string.Equals(
                Path.GetFullPath(_playbackArchive.SessionDirectory).TrimEnd('\\', '/'),
                Path.GetFullPath(session).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
        {
            return "This report's recording is not the one open in the player, so the playhead was not moved.";
        }

        SeekTo(Math.Clamp(nanoseconds, 0, _playbackArchive.DurationNanoseconds), synchronizeAudio: true);
        return null;
    }

    private void ShowAnalysisMenuItem_Click(object sender, RoutedEventArgs e) => ShowAnalysisWindow();

    // Opens the Analysis window, or brings it to the front.
    private void ShowAnalysisWindow() => OpenPanelWindow(AnalysisPanel);

    private async void CapabilitiesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (AnalysisCatalogOrNull is not { } catalog || _analysisRecordingId is not { } recording)
        {
            ShowAnalysisStatus("Open a recording that is in the catalog to describe its participant.");
            return;
        }

        try
        {
            var earlier = await catalog.CapabilitiesAsync(recording, CancellationToken.None);
            var dialog = new CapabilityDialog(this, earlier);
            if (dialog.ShowDialog() == true && dialog.Description is { } description)
            {
                await catalog.SaveCapabilitiesAsync(recording, description, CancellationToken.None);
                ShowAnalysisStatus("The participant's capabilities were saved. Analysis queued from now on uses them.");
                _busy.AnnounceCompleted(AnalysisStatusTextBlock.Text);
            }
        }
        catch (NpgsqlException exception)
        {
            ShowAnalysisStatus($"The participant's capabilities could not be read or saved: {exception.Message}");
        }
    }

    // "Limit the report to the browser while it is in front", saved in
    // the analysis settings file at once. A worker already running keeps
    // the setting it started with; each report says which it used.
    private void BrowserOnlyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var path = AnalysisSettingsFile.DefaultPath;
        var on = BrowserOnlyMenuItem.IsChecked;
        try
        {
            var text = File.Exists(path) ? File.ReadAllText(path) : null;
            if (AnalysisSettingsFile.WithBrowserOnly(text, on) is not { } written)
            {
                throw new IOException("The analysis settings file is not a JSON object, so the setting was not saved.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, written);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "The setting was not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        ReadAnalysisSettings();
        UpdateAnalysisControls();
    }

    private void CloseReportWindows()
    {
        foreach (var window in _reportWindows.ToList())
        {
            window.Close();
        }
    }
}

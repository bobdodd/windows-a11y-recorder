using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Npgsql;
using Recorder.Analysis;
using Recorder.Analysis.Summary;

namespace Recorder.App;

// The player's analysis controls (analysis-runner.md, "The player", and
// "Decisions on the player (2026-10-13)"): the Analysis menu, the Analysis
// tab with the session's tasks, the unread count, the notifications, the
// reading view, the capability description, and the model profiles. All of
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
    private string _analysisTasksShown = "";
    private bool _analysisReading;

    private AnalysisCatalog? AnalysisCatalogOrNull =>
        _analysisCatalog ??= _database is null ? null : AnalysisCatalog.For(_database.Server.DataSource);

    // When the database has started: tasks still waiting from an earlier
    // run, such as before a restart, get a worker.
    private async Task StartAnalysisAsync()
    {
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
        var chosen = (ProfileComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        ProfileComboBox.Items.Clear();
        foreach (var profile in _analysisSettings.Profiles.Where(profile => profile.Supports(ModelCapabilities.Text)))
        {
            var item = new ComboBoxItem { Content = profile.Name, Tag = profile.Name, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(item, profile.Name);
            ProfileComboBox.Items.Add(item);
        }

        var preferred = chosen ?? AnalysisPlayer.DefaultProfile(_analysisSettings, ModelCapabilities.Text)?.Name;
        ProfileComboBox.SelectedItem = ProfileComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == preferred)
            ?? ProfileComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    // A recording was opened, or none is: the tab shows its tasks.
    private void BindAnalysis()
    {
        _analysisRecordingId = null;
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

        UpdateAnalysisControls();
        _ = ReadAnalysisAsync();
    }

    private void UpdateAnalysisControls()
    {
        var open = _playbackArchive is not null && AnalysisCatalogOrNull is not null;
        QueueSummaryButton.IsEnabled = open && ProfileComboBox.Items.Count > 0;
        ProfileComboBox.IsEnabled = open && ProfileComboBox.Items.Count > 0;
        SummariseMenuItem.IsEnabled = QueueSummaryButton.IsEnabled;
        VisionMenuItem.IsEnabled = open && AnalysisPlayer.DefaultProfile(_analysisSettings, ModelCapabilities.Image) is not null;
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

            var tasks = await catalog.TasksAsync(recording, CancellationToken.None);
            foreach (var message in AnalysisPlayer.Changes(_analysisTasks, tasks))
            {
                ShowAnalysisStatus(message);
                _busy.AnnounceAnalysis(message);
            }

            _analysisTasks = tasks;
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
        AnalysisTabHeaderText.Text = header;
        AutomationProperties.SetName(AnalysisTab, header);
        var shown = string.Join(";", tasks.Select(task => $"{task.TaskId}:{task.State}:{task.ReadUtc}:{task.FinishedUtc}")) + "|" + empty;
        if (shown == _analysisTasksShown)
        {
            return;
        }

        _analysisTasksShown = shown;
        var focused = (System.Windows.Input.Keyboard.FocusedElement as FrameworkElement)?.Tag as string;
        AnalysisTasksPanel.Children.Clear();
        if (empty is not null)
        {
            AnalysisTasksPanel.Children.Add(new TextBlock { Text = empty, TextWrapping = TextWrapping.Wrap });
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
        var profiles = task.StageProfiles.Count == 0 ? "no profile" : string.Join(", ", task.StageProfiles.Values);
        var lines = new StackPanel();
        lines.Children.Add(new TextBlock { Text = $"{name}: {state}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var times = $"Profile {profiles}. Queued {queued}" + (task.FinishedUtc is { } finished ? $", ended {Local(finished)}." : ".");
        lines.Children.Add(new TextBlock { Text = times, TextWrapping = TextWrapping.Wrap });
        if (task.Failure is { Length: > 0 } failure)
        {
            lines.Children.Add(new TextBlock { Text = "Why: " + failure, TextWrapping = TextWrapping.Wrap });
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
        Add("Run again", "again", $"Run {label} again", AnalysisPlayer.Ended(task.State), () => RunAgainAsync(task));
        Add(task.State == AnalysisTaskStates.Failed ? "Why it failed" : "Open result", "open",
            task.State == AnalysisTaskStates.Failed ? $"Why {label} failed" : $"Open the result of {label}",
            task.State is AnalysisTaskStates.Done or AnalysisTaskStates.Failed, () => OpenResultAsync(task));
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

    private string? SelectedProfile => (ProfileComboBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private async Task QueueAsync(string kind, IReadOnlyDictionary<string, string> profiles, Guid? afterTaskId = null)
    {
        if (AnalysisCatalogOrNull is not { } catalog || _analysisRecordingId is not { } recording || _analysisSessionDirectory is null)
        {
            ShowAnalysisStatus("Open a recording that is in the catalog to queue its analysis.");
            return;
        }

        try
        {
            await catalog.QueueTaskAsync(recording, kind, 1, profiles, afterTaskId: afterTaskId, sessionDirectory: _analysisSessionDirectory, cancellationToken: CancellationToken.None);
            ShowAnalysisStatus($"{AnalysisPlayer.KindName(kind)} queued with {string.Join(", ", profiles.Values.Distinct())}.");
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
        ShowAnalysisTab();
        await QueueSummaryAsync();
    }

    private async Task QueueSummaryAsync()
    {
        if (SelectedProfile is not { } profile)
        {
            ShowAnalysisStatus("There is no model profile for text. Open Model profiles... from the Analysis menu.");
            return;
        }

        await QueueAsync(RecordingSummaryKind.Name, new Dictionary<string, string> { [ModelCapabilities.Text] = profile });
    }

    private async void VisionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowAnalysisTab();
        await QueueVisionAsync();
    }

    // Vision is added to the newest summary of the recording that is
    // queued, running, or finished, and runs after it; all of it on the
    // image profile (recording-summary.md, "Decisions (2026-10-14)").
    private async Task QueueVisionAsync()
    {
        if (AnalysisPlayer.DefaultProfile(_analysisSettings, ModelCapabilities.Image) is not { } profile)
        {
            ShowAnalysisStatus("There is no model profile for images. Open Model profiles... from the Analysis menu.");
            return;
        }

        if (AnalysisCatalogOrNull is not { } catalog || _analysisRecordingId is not { } recording)
        {
            ShowAnalysisStatus("Open a recording that is in the catalog to queue its analysis.");
            return;
        }

        AnalysisTaskSummary? summary;
        try
        {
            summary = (await catalog.TasksAsync(recording, CancellationToken.None))
                .Where(task => task.Kind == RecordingSummaryKind.Name &&
                    task.State is AnalysisTaskStates.Queued or AnalysisTaskStates.Running or AnalysisTaskStates.Done)
                .OrderByDescending(task => task.QueuedUtc)
                .FirstOrDefault();
        }
        catch (NpgsqlException exception)
        {
            ShowAnalysisStatus($"The task could not be queued: {exception.Message}");
            return;
        }

        if (summary is null)
        {
            ShowAnalysisStatus("Summarise the recording first: vision is added to a summary.");
            return;
        }

        await QueueAsync(
            RecordingSummaryVisionKind.Name,
            new Dictionary<string, string> { [ModelCapabilities.Text] = profile.Name, [ModelCapabilities.Image] = profile.Name },
            summary.TaskId);
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

    // Run again queues a new task of the same kind with the same profiles;
    // the earlier one and its report stay (owner, 2026-10-13).
    // Vision run again is added to the newest summary, as from the menu.
    private Task RunAgainAsync(AnalysisTaskSummary task) =>
        task.Kind == RecordingSummaryVisionKind.Name ? QueueVisionAsync() : QueueAsync(task.Kind, task.StageProfiles);

    private async Task OpenResultAsync(AnalysisTaskSummary task)
    {
        if (AnalysisCatalogOrNull is not { } catalog || _analysisSessionDirectory is not { } session)
        {
            return;
        }

        if (task.State == AnalysisTaskStates.Failed)
        {
            await catalog.MarkReadAsync(task.TaskId, CancellationToken.None);
            MessageBox.Show(this, task.Failure ?? "No reason was recorded.", $"Why {AnalysisPlayer.KindName(task.Kind)} failed",
                MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void ShowAnalysisMenuItem_Click(object sender, RoutedEventArgs e) => ShowAnalysisTab();

    private void ShowAnalysisTab()
    {
        if (_tornTabs.TryGetValue(AnalysisTab, out var window))
        {
            window.Activate();
            return;
        }

        DetailsTabControl.SelectedItem = AnalysisTab;
        AnalysisTab.Focus();
    }

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

    private void ModelProfilesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        new ModelProfilesWindow(this, AnalysisSettingsFile.DefaultPath).ShowDialog();
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

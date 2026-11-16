using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Npgsql;
using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Database;
using Recorder.Session;

namespace Recorder.App;

// Projects with more than one recording (projects.md; owner, 2026-11-14):
// the project new recordings go into, chosen in the side panel and
// remembered between runs; new projects; the Projects window; and the
// Android report of every recording in the open recording's project.
public partial class MainWindow
{
    private Guid? _openProjectId;
    private ProjectModels? _openProjectModels;
    private const string AppTitle = "Windows Accessibility Recorder";
    private Guid? _projectShownFor;
    private bool _showingProjects;

    // The project last chosen, kept beside the player's layout.
    private static string ProjectChoicePath =>
        Path.Combine(Path.GetDirectoryName(PlayerLayout.DefaultPath) ?? ".", "project.txt");

    private Guid? SelectedProjectId => (ProjectComboBox.SelectedItem as ComboBoxItem)?.Tag as Guid?;

    // Lists the projects, choosing the one asked for, else the one last
    // chosen, else the database's default project.
    private async Task ShowProjectChoicesAsync(Guid? select = null)
    {
        if (_database is null)
        {
            ProjectComboBox.IsEnabled = false;
            NewProjectButton.IsEnabled = false;
            ProjectsButton.IsEnabled = false;
            return;
        }

        IReadOnlyList<ProjectSummary> projects;
        try
        {
            projects = await _database.Store.ProjectsAsync();
        }
        catch (NpgsqlException exception)
        {
            StatusTextBlock.Text = "The projects could not be read: " + exception.Message;
            return;
        }

        select ??= SelectedProjectId ?? RememberedProject() ?? _database.ProjectId;
        _showingProjects = true;
        try
        {
            ProjectComboBox.Items.Clear();
            foreach (var project in projects)
            {
                var item = new ComboBoxItem { Content = project.Name, Tag = project.ProjectId, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(item, project.Name);
                ProjectComboBox.Items.Add(item);
            }

            ProjectComboBox.SelectedItem = ProjectComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (Guid)item.Tag == select)
                ?? ProjectComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (Guid)item.Tag == _database.ProjectId);
        }
        finally
        {
            _showingProjects = false;
        }

        var idle = !IsRecording && !_transitioning;
        ProjectComboBox.IsEnabled = idle;
        NewProjectButton.IsEnabled = idle;
        ProjectsButton.IsEnabled = true;
    }

    private static Guid? RememberedProject()
    {
        try
        {
            return File.Exists(ProjectChoicePath) && Guid.TryParse(File.ReadAllText(ProjectChoicePath).Trim(), out var id) ? id : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showingProjects)
        {
            RememberProject();
        }
    }

    private void RememberProject()
    {
        if (SelectedProjectId is not { } project)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProjectChoicePath)!);
            File.WriteAllText(ProjectChoicePath, project.ToString("D"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = "The project chosen could not be remembered: " + exception.Message;
        }
    }

    private async void NewProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_database is null)
        {
            return;
        }

        var dialog = new ProjectNameDialog(this, "New project", "");
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            if (await _database.Store.CreateProjectAsync(dialog.ProjectName) is not { } created)
            {
                MessageBox.Show(this, $"A project named {dialog.ProjectName} already exists. Choose it from the list, or give the new project another name.",
                    "New project", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // A new project is chosen for the recordings that follow.
            await ShowProjectChoicesAsync(created);
            RememberProject();
            StatusTextBlock.Text = $"The project {dialog.ProjectName} was made. New recordings go into it.";
            _busy.AnnounceCompleted(StatusTextBlock.Text);
        }
        catch (NpgsqlException exception)
        {
            StatusTextBlock.Text = "The project could not be made: " + exception.Message;
        }
    }

    private async void ProjectsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_database is null)
        {
            MessageBox.Show(this, DatabaseUnavailableText, "Projects and recordings", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new ProjectsWindow(this, _database.Store, _openProjectId ?? SelectedProjectId, OpenProjectSettings);
        window.ShowDialog();
        if (window.Changed)
        {
            await ShowProjectChoicesAsync();
            _projectShownFor = null;
            await ReadAnalysisAsync();
        }
    }

    // Asks for the name of the recording about to start, in the project
    // chosen for it; null when the auditor cancels, so it does not start.
    private (string? Name, Guid Project)? AskRecordingName()
    {
        if (_database is not { } database)
        {
            return null;
        }

        var project = SelectedProjectId ?? database.ProjectId;
        var projectName = (ProjectComboBox.SelectedItem as ComboBoxItem)?.Content as string ?? SessionDatabase.DefaultProjectName;
        var dialog = new RecordingNameDialog(
            this,
            "Name this recording",
            $"The recording goes into the project {projectName}. Give it a name to know it by, such as the task it records, or leave the name empty to know it by its session key. " +
                "No two recordings in a project can have the same name. You can rename it later, from the Project menu or in Projects and recordings.",
            "",
            "_Start recording",
            name => database.Store.RecordingNameTakenAsync(project, name));
        return dialog.ShowDialog() == true ? (dialog.RecordingName, project) : null;
    }

    // Renames the open recording (recording-names.md), from the Project menu.
    private async void RenameOpenRecordingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_database is not { } database || _analysisRecordingId is not { } recording || _openProjectId is not { } project)
        {
            MessageBox.Show(this, "Open a recording that is in the catalog to rename it.", "Rename the open recording", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var current = await database.Store.RecordingNameAsync(recording);
            var dialog = new RecordingNameDialog(
                this,
                "Rename the open recording",
                "Give the recording a new name, or leave it empty to know it by its session key. Its folder and session key do not change.",
                current,
                "_Save",
                name => database.Store.RecordingNameTakenAsync(project, name, recording));
            if (dialog.ShowDialog() != true || dialog.RecordingName == current)
            {
                return;
            }

            var result = await database.Store.RenameRecordingAsync(recording, dialog.RecordingName);
            var message = result switch
            {
                RecordingRename.Renamed => dialog.RecordingName is { } named ? $"The recording was renamed {named}." : "The recording's name was removed; it is known by its session key.",
                RecordingRename.NameTaken => $"A recording named {dialog.RecordingName} is already in this project, so the recording was not renamed.",
                _ => "The recording is no longer in the catalog.",
            };
            ShowAnalysisStatus(message);
            _busy.AnnounceCompleted(message);
            _projectShownFor = null;
            await ReadAnalysisAsync();
        }
        catch (NpgsqlException exception)
        {
            ShowAnalysisStatus("The recording could not be renamed: " + exception.Message);
        }
    }

    // The open recording's name and project, shown in the window's title
    // and the Analysis tab, whose project's tasks are listed with the
    // recording's.
    private async Task ShowOpenProjectAsync(AnalysisCatalog catalog, Guid recording)
    {
        _projectShownFor = recording;
        var named = await catalog.RecordingAsync(recording, CancellationToken.None);
        Title = named is { } shown ? $"{shown.Label}, {AppTitle}" : AppTitle;
        var project = await catalog.ProjectOfAsync(recording, CancellationToken.None);
        _openProjectId = project?.ProjectId;
        _openProjectModels = project is { } of ? await catalog.ProjectModelsAsync(of.ProjectId, CancellationToken.None) : null;
        ShowProjectModels();
        if (project is not { } found)
        {
            AnalysisProjectTextBlock.Visibility = Visibility.Collapsed;
            UpdateAnalysisControls();
            return;
        }

        var all = await catalog.ProjectAsync(found.ProjectId, CancellationToken.None);
        var count = all?.Recordings.Count ?? 0;
        AnalysisProjectTextBlock.Text = $"This recording, {named?.Label ?? "unnamed"}, is in the project {found.Name}, with {count} {(count == 1 ? "recording" : "recordings")}. " +
            "The project's tasks are listed with this recording's.";
        AnalysisProjectTextBlock.Visibility = Visibility.Visible;
        UpdateAnalysisControls();
    }

    // Every recording in the open recording's project, taken together; the
    // report is written under the recordings' folder, in Projects.
    private async void AndroidProjectReportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowAnalysisTab();
        await QueueProjectAsync(AndroidProjectReportKind.Name);
    }

    // A project's task, queued with the project's models as they are now.
    private async Task QueueProjectAsync(string kind, Guid? rerunOfTaskId = null)
    {
        if (AnalysisCatalogOrNull is not { } catalog || _openProjectId is not { } project || _analysisSessionDirectory is null)
        {
            ShowAnalysisStatus("Open a recording that is in the catalog to queue its project's analysis.");
            return;
        }

        var folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(_analysisSessionDirectory)));
        if (folder is null)
        {
            ShowAnalysisStatus("The recordings' folder could not be found from this recording's folder.");
            return;
        }

        try
        {
            if (ProfilesFor(kind, await CurrentModelsAsync(catalog)) is not { } profiles)
            {
                return;
            }

            await catalog.QueueProjectTaskAsync(project, kind, KindVersion(kind), profiles, folder, rerunOfTaskId, profiles.Count > 0, CancellationToken.None);
            ShowAnalysisStatus($"{QueuedWith(kind, profiles)} It reads the project's recordings from {folder}.");
            _busy.AnnounceCompleted(AnalysisStatusTextBlock.Text);
            StartWorker();
            await ReadAnalysisAsync();
        }
        catch (NpgsqlException exception)
        {
            ShowAnalysisStatus($"The task could not be queued: {exception.Message}");
        }
    }

    // The project's settings (projects.md, "The project's models"): of the
    // open recording's project, else of the project chosen for new
    // recordings, from the Project menu and the Analysis tab.
    private async void ProjectSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_database is null || AnalysisCatalogOrNull is not { } catalog)
        {
            MessageBox.Show(this, DatabaseUnavailableText, "Project settings", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var project = _openProjectId ?? SelectedProjectId ?? _database.ProjectId;
        string name;
        try
        {
            name = (await _database.Store.ProjectsAsync()).FirstOrDefault(item => item.ProjectId == project)?.Name ?? SessionDatabase.DefaultProjectName;
        }
        catch (NpgsqlException exception)
        {
            ShowAnalysisStatus("The projects could not be read: " + exception.Message);
            return;
        }

        if (OpenProjectSettings(this, project, name))
        {
            await CurrentModelsAsync(catalog);
        }
    }

    // Opens a project's settings over a window; true where they were saved.
    private bool OpenProjectSettings(Window owner, Guid project, string name)
    {
        if (AnalysisCatalogOrNull is not { } catalog)
        {
            return false;
        }

        ReadAnalysisSettings();
        var window = new ProjectSettingsWindow(owner, catalog, _analysisSettings, project, name);
        if (window.ShowDialog() != true || window.Saved is not { } saved)
        {
            return false;
        }

        if (project == _openProjectId)
        {
            _openProjectModels = saved;
            ShowProjectModels();
            UpdateAnalysisControls();
        }

        var message = $"The settings of the project {name} were saved. Models: " +
            AnalysisPlayer.ModelsLine(saved.Choices(_analysisSettings), saved, ModelUses.All) + ".";
        ShowAnalysisStatus(message);
        _busy.AnnounceCompleted(message);
        return true;
    }

    // The open recording's project's models, in the Analysis tab and, for
    // the checks, in the Checks tab, which wrap rather than scroll.
    private void ShowProjectModels()
    {
        if (_openProjectModels is not { } models)
        {
            AnalysisModelsTextBlock.Visibility = Visibility.Collapsed;
            ChecksModelsTextBlock.Text = "Open a recording to see the models its project uses for checks.";
            return;
        }

        AnalysisModelsTextBlock.Text = "Models, from the project's settings: " +
            AnalysisPlayer.ModelsLine(models.Choices(_analysisSettings), models, ModelUses.All) + ".";
        AnalysisModelsTextBlock.Visibility = Visibility.Visible;
        ChecksModelsTextBlock.Text = "Models for checks, from the project's settings: " +
            AnalysisPlayer.ModelsLine(models.Choices(_analysisSettings), models, [ModelUses.TextChecks, ModelUses.VisionChecks]) +
            ". The images-of-text check asks the model for vision-based checks about the images its rules leave; no check uses the model for text-based checks yet.";

        // The model may have changed, so its kept answers are read again.
        _ = ApplyKeptAnswersAsync();
    }
}

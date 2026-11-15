using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Npgsql;
using Recorder.Database;
using Recorder.Session;

namespace Recorder.App;

/// <summary>
/// The projects and their recordings (projects.md): a project's recordings
/// listed in the order they were made, by name and session key
/// (recording-names.md), new projects, renaming projects and recordings,
/// and moving recordings from one project to another. Every target is at least 44
/// pixels high, and the text wraps rather than scrolling sideways.
/// </summary>
internal sealed class ProjectsWindow : Window
{
    private readonly RecordingStore _store;
    private readonly ListBox _projects;
    private readonly ListBox _recordings;
    private readonly ComboBox _target;
    private readonly Button _rename;
    private readonly Button _move;
    private readonly Button _renameRecording;
    private IReadOnlyList<ProjectRecordingSummary> _shown = [];
    private readonly SelectableText _status;
    private IReadOnlyList<ProjectSummary> _list = [];

    public ProjectsWindow(Window owner, RecordingStore store, Guid? selected)
    {
        _store = store;
        Owner = owner;
        Title = "Projects and recordings";
        Width = 760;
        Height = 680;
        MinWidth = 420;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(12) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, new GridLength(2, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
        {
            root.RowDefinitions.Add(new RowDefinition { Height = height });
        }

        Add(root, 0, new SelectableText
        {
            Text = "A project holds recordings of the same work, such as one app's audit. New recordings go into the project chosen in the side panel. " +
                "A report can cover one recording, or every recording in its project, with a screen seen in more than one recording given once.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _projects = List("Projects", SelectionMode.Single);
        _projects.SelectionChanged += async (_, _) => await ShowRecordingsAsync();
        Add(root, 1, new Label { Content = "_Projects:", Target = _projects, Padding = new Thickness(0, 0, 0, 4) });
        Add(root, 2, _projects);

        var projectButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var create = Button("_New project...", "Make a new project");
        create.Click += async (_, _) => await CreateAsync();
        _rename = Button("_Rename project...", "Rename the selected project");
        _rename.Click += async (_, _) => await RenameAsync();
        projectButtons.Children.Add(create);
        projectButtons.Children.Add(_rename);
        Add(root, 3, projectButtons);

        _recordings = List("Recordings in the selected project", SelectionMode.Extended);
        _recordings.SelectionChanged += (_, _) => UpdateButtons();
        Add(root, 4, new Label
        {
            Content = new TextBlock { Text = "Re_cordings in the selected project, in the order they were made. Select those to move; hold Ctrl or Shift to select more than one:", TextWrapping = TextWrapping.Wrap },
            Target = _recordings,
            Padding = new Thickness(0, 0, 0, 4)
        });
        Add(root, 5, _recordings);

        var moveRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        _renameRecording = Button("Rename recor_ding...", "Rename the selected recording");
        _renameRecording.Click += async (_, _) => await RenameRecordingAsync();
        _renameRecording.Margin = new Thickness(0, 0, 24, 8);
        moveRow.Children.Add(_renameRecording);
        _target = new ComboBox { MinWidth = 220, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        AutomationProperties.SetName(_target, "Project to move the selected recordings to");
        _target.SelectionChanged += (_, _) => UpdateButtons();
        moveRow.Children.Add(new Label { Content = "_Move the selected recordings to:", Target = _target, VerticalAlignment = VerticalAlignment.Center });
        moveRow.Children.Add(_target);
        _move = Button("Mo_ve", "Move the selected recordings to the chosen project");
        _move.Click += async (_, _) => await MoveAsync();
        moveRow.Children.Add(_move);
        Add(root, 6, moveRow);

        _status = new SelectableText { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_status, "Status");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        Add(root, 7, _status);

        var close = new Button { Content = "Close", MinWidth = 88, MinHeight = 44, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        Add(root, 8, close);
        Content = root;
        Loaded += async (_, _) =>
        {
            await ShowProjectsAsync(selected);
            _projects.Focus();
        };
    }

    /// <summary>Whether a project was made or renamed, or recordings renamed or moved, so the side panel lists them again.</summary>
    public bool Changed { get; private set; }

    private static void Add(Grid grid, int row, UIElement element)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }

    private static ListBox List(string name, SelectionMode mode)
    {
        var list = new ListBox { SelectionMode = mode, MinHeight = 96 };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(list, name);
        return list;
    }

    private static Button Button(string content, string name)
    {
        var button = new Button { Content = content, MinWidth = 44, MinHeight = 44, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 8) };
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static ListBoxItem Item(string text, object tag)
    {
        var item = new ListBoxItem { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, Tag = tag, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(item, text);
        return item;
    }

    private Guid? SelectedProject => (_projects.SelectedItem as ListBoxItem)?.Tag as Guid?;

    private async Task ShowProjectsAsync(Guid? select)
    {
        try
        {
            _list = await _store.ProjectsAsync();
        }
        catch (NpgsqlException exception)
        {
            _status.Text = "The projects could not be read: " + exception.Message;
            return;
        }

        _projects.Items.Clear();
        _target.Items.Clear();
        foreach (var project in _list)
        {
            _projects.Items.Add(Item($"{project.Name}, {Count(project.Recordings, "recording", "recordings")}", project.ProjectId));
            var target = new ComboBoxItem { Content = project.Name, Tag = project.ProjectId, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(target, project.Name);
            _target.Items.Add(target);
        }

        _projects.SelectedItem = _projects.Items.OfType<ListBoxItem>().FirstOrDefault(item => (Guid)item.Tag == select)
            ?? _projects.Items.OfType<ListBoxItem>().FirstOrDefault();
        await ShowRecordingsAsync();
    }

    private async Task ShowRecordingsAsync(Guid? select = null)
    {
        _recordings.Items.Clear();
        _shown = [];
        if (SelectedProject is not { } project)
        {
            UpdateButtons();
            return;
        }

        try
        {
            var recordings = await _store.RecordingsOfAsync(project);
            _shown = recordings;
            foreach (var recording in recordings)
            {
                var started = recording.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
                var length = recording.DurationNanoseconds is { } duration ? ", " + WindowsPreferenceTimeline.ClockOf(duration)[..8] : "";
                var item = Item($"{recording.Label}, started {started}{length}, {Status(recording.Status)}", recording.RecordingId);
                _recordings.Items.Add(item);
                if (recording.RecordingId == select)
                {
                    _recordings.SelectedItem = item;
                }
            }

            if (recordings.Count == 0)
            {
                _status.Text = "This project has no recordings yet.";
            }
        }
        catch (NpgsqlException exception)
        {
            _status.Text = "The project's recordings could not be read: " + exception.Message;
        }

        UpdateButtons();
    }

    private static string Status(RecordingStatus status) => status switch
    {
        RecordingStatus.Recording => "being recorded",
        RecordingStatus.Completed => "completed",
        RecordingStatus.Failed => "failed",
        RecordingStatus.Interrupted => "interrupted",
        _ => "unknown",
    };

    private static string Count(int count, string one, string many) =>
        count.ToString(CultureInfo.CurrentCulture) + " " + (count == 1 ? one : many);

    private void UpdateButtons()
    {
        _rename.IsEnabled = SelectedProject is not null;
        _renameRecording.IsEnabled = _recordings.SelectedItems.Count == 1;
        var target = (_target.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        _move.IsEnabled = _recordings.SelectedItems.Count > 0 && target is not null && target != SelectedProject;
    }

    private async Task CreateAsync()
    {
        var dialog = new ProjectNameDialog(this, "New project", "");
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            if (await _store.CreateProjectAsync(dialog.ProjectName) is not { } created)
            {
                _status.Text = $"A project named {dialog.ProjectName} already exists.";
                return;
            }

            Changed = true;
            _status.Text = $"The project {dialog.ProjectName} was made.";
            await ShowProjectsAsync(created);
        }
        catch (NpgsqlException exception)
        {
            _status.Text = "The project could not be made: " + exception.Message;
        }
    }

    private async Task RenameAsync()
    {
        if (SelectedProject is not { } project || _list.FirstOrDefault(item => item.ProjectId == project) is not { } current)
        {
            return;
        }

        var dialog = new ProjectNameDialog(this, "Rename project", current.Name);
        if (dialog.ShowDialog() != true || dialog.ProjectName == current.Name)
        {
            return;
        }

        try
        {
            if (!await _store.RenameProjectAsync(project, dialog.ProjectName))
            {
                _status.Text = $"A project named {dialog.ProjectName} already exists, so {current.Name} was not renamed.";
                return;
            }

            Changed = true;
            _status.Text = $"{current.Name} was renamed {dialog.ProjectName}.";
            await ShowProjectsAsync(project);
        }
        catch (NpgsqlException exception)
        {
            _status.Text = "The project could not be renamed: " + exception.Message;
        }
    }

    private async Task MoveAsync()
    {
        if (SelectedProject is not { } project || (_target.SelectedItem as ComboBoxItem)?.Tag is not Guid target)
        {
            return;
        }

        var chosen = _recordings.SelectedItems.OfType<ListBoxItem>().Select(item => (Guid)item.Tag).ToList();
        try
        {
            var move = await _store.MoveRecordingsAsync(chosen, target);
            Changed = true;
            var name = _list.FirstOrDefault(item => item.ProjectId == target)?.Name ?? "the chosen project";
            _status.Text = $"{Count(move.Moved, "recording was", "recordings were")} moved to {name}, with their analysis." +
                (move.Refused.Count == 0 ? "" : $" Not moved, as {name} already has a recording of the same name: {string.Join("; ", move.Refused)}. Rename them first to move them.");
            await ShowProjectsAsync(project);
        }
        catch (NpgsqlException exception)
        {
            _status.Text = "The recordings could not be moved: " + exception.Message;
        }
    }

    private async Task RenameRecordingAsync()
    {
        if (SelectedProject is not { } project || _recordings.SelectedItems.Count != 1 ||
            (_recordings.SelectedItem as ListBoxItem)?.Tag is not Guid recording ||
            _shown.FirstOrDefault(item => item.RecordingId == recording) is not { } current)
        {
            return;
        }

        var dialog = new RecordingNameDialog(
            this,
            "Rename recording",
            $"Give {current.Label} a new name, or leave it empty to know it by its session key. Its folder and session key do not change.",
            current.Name,
            "_Save",
            name => _store.RecordingNameTakenAsync(project, name, recording));
        if (dialog.ShowDialog() != true || dialog.RecordingName == current.Name)
        {
            return;
        }

        try
        {
            switch (await _store.RenameRecordingAsync(recording, dialog.RecordingName))
            {
                case RecordingRename.Renamed:
                    Changed = true;
                    _status.Text = dialog.RecordingName is { } named
                        ? $"{current.Label} was renamed {named}."
                        : $"{current.Label} has no name now, and is known by its session key.";
                    break;
                case RecordingRename.NameTaken:
                    _status.Text = $"A recording named {dialog.RecordingName} is already in this project, so {current.Label} was not renamed.";
                    break;
                default:
                    _status.Text = $"{current.Label} is no longer in the catalog.";
                    break;
            }

            await ShowRecordingsAsync(recording);
            _recordings.Focus();
        }
        catch (NpgsqlException exception)
        {
            _status.Text = "The recording could not be renamed: " + exception.Message;
        }
    }
}

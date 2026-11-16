using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Npgsql;
using Recorder.Analysis;

namespace Recorder.App;

/// <summary>
/// A project's settings (projects.md, "The project's models"; owner,
/// 2026-11-15): the model, or no model, for report analysis, for
/// text-based checks and for vision-based checks, and the project's
/// Anthropic API key. Each list offers no model, the Claude models, each
/// the project's own with its defaults, and the models set up on this PC
/// that can do what the use needs. The key is protected for this Windows
/// user before it is saved, and never shown again. Every target is at
/// least 44 pixels high, and the text wraps rather than scrolling
/// sideways.
/// </summary>
internal sealed class ProjectSettingsWindow : Window
{
    private readonly AnalysisCatalog _catalog;
    private readonly AnalysisSettings _settings;
    private readonly Guid _projectId;
    private readonly Dictionary<string, ComboBox> _choices = [];
    private readonly SelectableText _status;
    private readonly Button _save;
    private readonly PasswordBox _key;
    private readonly SelectableText _keyState;
    private ProjectModels? _read;
    private ProjectModels? _saved;

    public ProjectSettingsWindow(Window owner, AnalysisCatalog catalog, AnalysisSettings settings, Guid projectId, string projectName)
    {
        _catalog = catalog;
        _settings = settings;
        _projectId = projectId;
        Owner = owner;
        Title = $"Settings of the project {projectName}";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        MinWidth = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var page = new StackPanel { Margin = new Thickness(12) };
        page.Children.Add(new SelectableText
        {
            Text = $"The models the project {projectName} uses. No model means none is asked, cloud or local, and what needs one is not done: " +
                "a summary cannot be queued, and a check that needs one is listed as not run. " +
                "A task keeps the models it was queued with, but choosing no model stops a task already queued from asking one at its next step. " +
                "A Claude model sends the recording's facts and pictures to Anthropic with the project's API key; choosing one is the permission to send them. " +
                "Models on this PC are set up from the Analysis menu.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        foreach (var (use, label) in new[]
                 {
                     (ModelUses.Report, "Model for _report analysis, the summaries:"),
                     (ModelUses.TextChecks, "Model for _text-based checks:"),
                     (ModelUses.VisionChecks, "Model for _vision-based checks, which must take images:")
                 })
        {
            var box = new ComboBox { MinHeight = 44, MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12), IsEnabled = false };
            AutomationProperties.SetName(box, "Model for " + ModelUses.Name(use));
            page.Children.Add(new Label { Content = new AccessText { Text = label, TextWrapping = TextWrapping.Wrap }, Target = box, Padding = new Thickness(0, 0, 0, 4) });
            page.Children.Add(box);
            _choices[use] = box;
        }

        _key = new PasswordBox { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4), IsEnabled = false };
        AutomationProperties.SetName(_key, "Anthropic API key for the project");
        page.Children.Add(new Label
        {
            Content = new AccessText { Text = "Anthropic API _key, for the Claude models:", TextWrapping = TextWrapping.Wrap },
            Target = _key,
            Padding = new Thickness(0, 0, 0, 4)
        });
        page.Children.Add(_key);
        _keyState = new SelectableText { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(_keyState, "Anthropic API key saved");
        page.Children.Add(_keyState);

        _status = new SelectableText { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Text = "Reading the project's settings." };
        AutomationProperties.SetName(_status, "Status");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        page.Children.Add(_status);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        _save = new Button { Content = "_Save", MinWidth = 88, MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), IsDefault = true, IsEnabled = false };
        _save.Click += async (_, _) => await SaveAsync();
        buttons.Children.Add(_save);
        buttons.Children.Add(new Button { Content = "Cancel", MinWidth = 88, MinHeight = 44, IsCancel = true });
        page.Children.Add(buttons);
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += async (_, _) => await ReadAsync();
    }

    /// <summary>The project's models as saved, or null where they were not changed.</summary>
    public ProjectModels? Saved => _saved;

    private async Task ReadAsync()
    {
        ProjectModels models;
        try
        {
            if (await _catalog.ProjectModelsAsync(_projectId, CancellationToken.None) is not { } found)
            {
                _status.Text = "The project is no longer in the catalog.";
                return;
            }

            models = found;
        }
        catch (NpgsqlException exception)
        {
            _status.Text = "The project's settings could not be read: " + exception.Message;
            return;
        }

        _read = models;
        var choices = models.Choices(_settings);
        foreach (var (use, box) in _choices)
        {
            box.Items.Clear();
            foreach (var (name, label) in AnalysisPlayer.ModelChoices(choices, use, models.For(use)))
            {
                var item = new ComboBoxItem { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, Tag = name, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(item, label);
                box.Items.Add(item);
            }

            box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == models.For(use)) ?? box.Items[0];
            box.IsEnabled = true;
        }

        _key.IsEnabled = true;
        _keyState.Text = models.ProtectedAnthropicKey is null
            ? "No key is saved for this project. Paste one to use a Claude model."
            : "A key is saved for this project, protected for this Windows user. Paste another only to replace it.";
        _save.IsEnabled = true;
        _status.Text = "";
        _choices[ModelUses.Report].Focus();
    }

    private async Task SaveAsync()
    {
        if (_read is null)
        {
            return;
        }

        var models = ProjectModels.None with { ProtectedAnthropicKey = _read.ProtectedAnthropicKey };
        foreach (var (use, box) in _choices)
        {
            models = models.With(use, (box.SelectedItem as ComboBoxItem)?.Tag as string);
        }

        try
        {
            if (_key.Password.Trim().Length > 0)
            {
                models = models with { ProtectedAnthropicKey = new CurrentUserApiKeyProtector().Protect(_key.Password.Trim()) };
            }
        }
        catch (System.Security.Cryptography.CryptographicException exception)
        {
            _status.Text = "The key could not be protected for this Windows user, so nothing was saved: " + exception.Message;
            return;
        }

        var choices = models.Choices(_settings);
        if (ModelUses.All.Select(use => models.For(use)).OfType<string>().FirstOrDefault(name => choices.Profile(name)?.SendsToAnthropic == true) is { } claude &&
            models.ProtectedAnthropicKey is null)
        {
            _status.Text = $"{claude} needs the project's Anthropic API key. Paste it into the key box, then choose Save.";
            _key.Focus();
            return;
        }

        try
        {
            if (!await _catalog.SaveProjectModelsAsync(_projectId, models, CancellationToken.None))
            {
                _status.Text = "The project is no longer in the catalog, so its settings were not saved.";
                return;
            }
        }
        catch (NpgsqlException exception)
        {
            _status.Text = "The project's settings could not be saved: " + exception.Message;
            return;
        }

        _key.Clear();
        _saved = models;
        DialogResult = true;
    }
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Recorder.Analysis;

namespace Recorder.App;

/// <summary>
/// The model profiles (owner, 2026-10-13): each profile with what it can
/// take and its endpoint, the problems with the settings file, the
/// default profile for text and for images, and buttons to open
/// analysis-models.json in Notepad and to read it again. A full profile
/// editor comes later. See docs/architecture/analysis-runner.md,
/// "Decisions on the player (2026-10-13)".
/// </summary>
internal sealed class ModelProfilesWindow : Window
{
    private const string NoDefault = "No default";

    private readonly string _path;
    private readonly TextBox _profiles;
    private readonly ComboBox _text;
    private readonly ComboBox _image;
    private readonly TextBlock _status;

    public ModelProfilesWindow(Window owner, string path)
    {
        Owner = owner;
        _path = path;
        Title = "Model profiles";
        Width = 760;
        Height = 600;
        MinWidth = 460;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(12) };
        for (var row = 0; row < 6; row++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = row == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }

        var file = new TextBlock { Text = "Settings file: " + path, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(file, 0);
        root.Children.Add(file);

        _profiles = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 12)
        };
        var label = new Label { Content = "_Profiles and problems:", Target = _profiles, Padding = new Thickness(0) };
        Grid.SetRow(label, 1);
        root.Children.Add(label);
        Grid.SetRow(_profiles, 2);
        root.Children.Add(_profiles);
        AutomationProperties.SetName(_profiles, "Profiles and problems");

        _text = Choice();
        _image = Choice();
        var defaults = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        defaults.Children.Add(new Label { Content = "Default for _text:", Target = _text, VerticalAlignment = VerticalAlignment.Center });
        defaults.Children.Add(_text);
        defaults.Children.Add(new Label { Content = "Default for _images:", Target = _image, VerticalAlignment = VerticalAlignment.Center });
        defaults.Children.Add(_image);
        AutomationProperties.SetName(_text, "Default profile for text");
        AutomationProperties.SetName(_image, "Default profile for images");
        Grid.SetRow(defaults, 3);
        root.Children.Add(defaults);

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_status, "Model profiles status");
        Grid.SetRow(_status, 4);
        root.Children.Add(_status);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Command("_Save the defaults", SaveDefaults));
        buttons.Children.Add(Command("_Open the file in Notepad", OpenInNotepad));
        buttons.Children.Add(Command("_Read the file again", Read));
        var close = new Button { Content = "Close", MinWidth = 88, MinHeight = 44, IsCancel = true };
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);
        Content = root;
        Read();
    }

    private static ComboBox Choice() =>
        new() { MinWidth = 180, MinHeight = 44, Margin = new Thickness(0, 0, 16, 0), VerticalContentAlignment = VerticalAlignment.Center };

    private static Button Command(string content, Action click)
    {
        var button = new Button { Content = content, MinWidth = 44, MinHeight = 44, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    private void Read()
    {
        var (settings, problems) = AnalysisSettingsFile.Read(_path);
        var lines = new List<string>();
        if (settings.Profiles.Count == 0)
        {
            lines.Add("No profiles. Open the file in Notepad to add one, then read the file again.");
        }

        foreach (var profile in settings.Profiles)
        {
            lines.Add($"{profile.Name}: {profile.Model} at {profile.Endpoint}, takes {string.Join(" and ", profile.Capabilities)}" +
                (profile.Local is null ? "." : ", served by this PC."));
        }

        if (problems.Count > 0)
        {
            lines.Add("");
            lines.Add("Problems:");
            lines.AddRange(problems);
        }

        _profiles.Text = string.Join(Environment.NewLine, lines);
        Fill(_text, settings, ModelCapabilities.Text);
        Fill(_image, settings, ModelCapabilities.Image);
        _status.Text = File.Exists(_path) ? "" : "There is no settings file yet. Opening it in Notepad creates an empty one.";
    }

    private static void Fill(ComboBox box, AnalysisSettings settings, string capability)
    {
        box.Items.Clear();
        box.Items.Add(new ComboBoxItem { Content = NoDefault, Tag = null, MinHeight = 44 });
        foreach (var profile in settings.Profiles.Where(profile => profile.Supports(capability)))
        {
            box.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Name, MinHeight = 44 });
        }

        var chosen = settings.Defaults.GetValueOrDefault(capability);
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == chosen) ?? box.Items[0];
    }

    private void SaveDefaults()
    {
        try
        {
            AnalysisPlayer.EnsureSettingsFile(_path);
            var text = File.ReadAllText(_path);
            text = AnalysisPlayer.WithDefault(text, ModelCapabilities.Text, (_text.SelectedItem as ComboBoxItem)?.Tag as string);
            text = AnalysisPlayer.WithDefault(text, ModelCapabilities.Image, (_image.SelectedItem as ComboBoxItem)?.Tag as string);
            File.WriteAllText(_path, text);
            Read();
            _status.Text = "The defaults were saved. Tasks already queued keep the profiles they were queued with.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _status.Text = $"The defaults could not be saved: {exception.Message}";
        }
    }

    private void OpenInNotepad()
    {
        try
        {
            AnalysisPlayer.EnsureSettingsFile(_path);
            using var process = Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_path}\"") { UseShellExecute = false });
            _status.Text = "Opened in Notepad. Save it there, then choose Read the file again.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _status.Text = $"Notepad could not open the file: {exception.Message}";
        }
    }
}

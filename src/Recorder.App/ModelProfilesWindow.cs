using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Recorder.Analysis;
using Recorder.Analysis.Setup;

namespace Recorder.App;

/// <summary>
/// The model profiles (owner, 2026-10-13; editor 2026-11-06): the
/// profiles, with a form for the selected one's server arguments and
/// generation settings, the problems with the settings file, the default
/// profile for text and for images, model setup, and buttons to open
/// analysis-models.json in Notepad and to read it again. See
/// docs/architecture/windows-installer.md, "Model profiles window".
/// </summary>
internal sealed class ModelProfilesWindow : Window
{
    private const string NoDefault = "No default";

    private readonly string _path;
    private readonly string _baseDirectory;
    private readonly ListBox _list;
    private readonly SelectableText _details;
    private readonly TextBox _arguments;
    private readonly TextBox _maxTokens;
    private readonly TextBox _temperature;
    private readonly TextBox _seed;
    private readonly CheckBox _thinking;
    private readonly Button _saveProfile;
    private readonly Button _removeProfile;
    private readonly SelectableText _problems;
    private readonly ComboBox _text;
    private readonly ComboBox _image;
    private readonly SelectableText _status;
    private AnalysisSettings _settings = AnalysisSettings.Empty;

    public ModelProfilesWindow(Window owner, string path, string baseDirectory)
    {
        Owner = owner;
        _path = path;
        _baseDirectory = baseDirectory;
        Title = "Model profiles";
        Width = 900;
        Height = 760;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var page = new StackPanel { Margin = new Thickness(12) };
        page.Children.Add(new SelectableText { Text = "Settings file: " + path, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

        _list = new ListBox { MinHeight = 120, MaxHeight = 220, Margin = new Thickness(0, 2, 0, 8) };
        AutomationProperties.SetName(_list, "Profiles");
        _list.SelectionChanged += (_, _) => ShowSelected();
        page.Children.Add(new Label { Content = "_Profiles:", Target = _list, Padding = new Thickness(0) });
        page.Children.Add(_list);

        _details = new SelectableText { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_details, "The selected profile");
        page.Children.Add(_details);

        _arguments = Field(page, "llama.cpp server _arguments:", "llama.cpp server arguments");
        page.Children.Add(new SelectableText
        {
            Text = "For example -fa on -c 32768 for flash attention and a 32,768-token context. Without -ngl or -ncmoe, llama.cpp's " +
                "fit places the layers between the graphics card and the PC's memory when it loads the model; with them, they are " +
                "placed as given, such as -ngl 99 -ncmoe 36 on the development PC's RTX 2060.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 6)
        });
        var numbers = new WrapPanel();
        _maxTokens = Number(numbers, "Output _limit, tokens:", "Output limit in tokens");
        _temperature = Number(numbers, "T_emperature:", "Temperature");
        _seed = Number(numbers, "See_d:", "Seed");
        page.Children.Add(numbers);
        _thinking = new CheckBox { Content = "The model thin_ks before it answers", MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        page.Children.Add(_thinking);

        var profileButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
        _saveProfile = Command("Sa_ve the profile's settings", SaveProfile);
        _removeProfile = Command("_Remove the profile", RemoveProfile);
        profileButtons.Children.Add(_saveProfile);
        profileButtons.Children.Add(_removeProfile);
        profileButtons.Children.Add(Command("Set up _models...", SetUpModels));
        page.Children.Add(profileButtons);

        _text = Choice();
        _image = Choice();
        var defaults = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        defaults.Children.Add(new Label { Content = "Default for _text:", Target = _text, VerticalAlignment = VerticalAlignment.Center });
        defaults.Children.Add(_text);
        defaults.Children.Add(new Label { Content = "Default for _images:", Target = _image, VerticalAlignment = VerticalAlignment.Center });
        defaults.Children.Add(_image);
        defaults.Children.Add(Command("_Save the defaults", SaveDefaults));
        AutomationProperties.SetName(_text, "Default profile for text");
        AutomationProperties.SetName(_image, "Default profile for images");
        page.Children.Add(defaults);

        _problems = new SelectableText { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_problems, "Problems with the settings file");
        page.Children.Add(_problems);

        _status = new SelectableText { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_status, "Model profiles status");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        page.Children.Add(_status);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Command("_Open the file in Notepad", OpenInNotepad));
        buttons.Children.Add(Command("Read the file a_gain", Read));
        buttons.Children.Add(new Button { Content = "Close", MinWidth = 88, MinHeight = 44, IsCancel = true });
        page.Children.Add(buttons);
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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

    private static TextBox Field(Panel page, string label, string name)
    {
        var box = new TextBox { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, name);
        page.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0, 4, 0, 2) });
        page.Children.Add(box);
        return box;
    }

    private static TextBox Number(Panel panel, string label, string name)
    {
        var box = new TextBox { MinHeight = 44, MinWidth = 100, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        AutomationProperties.SetName(box, name);
        panel.Children.Add(new Label { Content = label, Target = box, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(box);
        return box;
    }

    private void Read()
    {
        var selected = (_list.SelectedItem as ListBoxItem)?.Tag as string;
        var (settings, problems) = AnalysisSettingsFile.Read(_path);
        _settings = settings;
        _list.Items.Clear();
        foreach (var profile in settings.Profiles)
        {
            var item = new ListBoxItem
            {
                Content = $"{profile.Name}: takes {string.Join(" and ", profile.Capabilities)}" + (profile.Local is null ? $", at {profile.Endpoint}" : ", served by this PC"),
                Tag = profile.Name,
                MinHeight = 44
            };
            _list.Items.Add(item);
        }

        _list.SelectedItem = _list.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.Tag as string == selected) ??
            _list.Items.OfType<ListBoxItem>().FirstOrDefault();
        ShowSelected();
        _problems.Text = problems.Count == 0 ? "" : "Problems: " + string.Join(" ", problems);
        Fill(_text, settings, ModelCapabilities.Text);
        Fill(_image, settings, ModelCapabilities.Image);
        _status.Text = settings.Profiles.Count == 0
            ? "No profiles. Choose Set up models to download a model or add one from this PC."
            : File.Exists(_path) ? "" : "There is no settings file yet.";
    }

    private ModelProfile? Selected => (_list.SelectedItem as ListBoxItem)?.Tag is string name ? _settings.Profile(name) : null;

    private void ShowSelected()
    {
        var profile = Selected;
        _saveProfile.IsEnabled = profile is not null;
        _removeProfile.IsEnabled = profile is not null;
        _arguments.IsEnabled = profile?.Local is not null;
        if (profile is null)
        {
            _details.Text = "No profile is selected.";
            _arguments.Text = _maxTokens.Text = _temperature.Text = _seed.Text = "";
            _thinking.IsChecked = false;
            return;
        }

        var lines = new List<string>
        {
            $"Model \"{profile.Model}\" at {profile.Endpoint}.",
            profile.File.Name is null ? "No model file is recorded." : $"File {profile.File.Name}" +
                (profile.File.Quantization is { Length: > 0 } quantization ? $", {quantization}" : "") +
                (profile.File.Sha256 is null ? "." : $", SHA-256 {profile.File.Sha256}.")
        };
        if (profile.Local is { } local)
        {
            lines.Add($"Model file: {local.ModelPath}" + (File.Exists(local.ModelPath) ? "." : ", which is not there."));
            if (local.ImageAddOnPath is { } addOn)
            {
                lines.Add($"Image add-on: {addOn}" + (File.Exists(addOn) ? "." : ", which is not there."));
            }
        }

        if (profile.Generation.Sampling.Count > 0)
        {
            lines.Add("Sampling: " + string.Join(", ", profile.Generation.Sampling.Select(pair => $"{pair.Key} {pair.Value.ToString(CultureInfo.InvariantCulture)}")) +
                ". Changed in the file.");
        }

        _details.Text = string.Join(Environment.NewLine, lines);
        _arguments.Text = profile.Local?.Arguments ?? "";
        _maxTokens.Text = profile.Generation.MaxTokens.ToString(CultureInfo.InvariantCulture);
        _temperature.Text = profile.Generation.Temperature.ToString(CultureInfo.InvariantCulture);
        _seed.Text = profile.Generation.Seed.ToString(CultureInfo.InvariantCulture);
        _thinking.IsChecked = profile.Generation.Thinking;
    }

    private void SaveProfile()
    {
        if (Selected is not { } profile)
        {
            return;
        }

        if (!int.TryParse(_maxTokens.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxTokens) || maxTokens < 1 ||
            !double.TryParse(_temperature.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature) || temperature < 0 ||
            !long.TryParse(_seed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
        {
            _status.Text = "The output limit must be a whole number of at least 1, the temperature a number of at least 0, such as 0.7, and the seed a whole number.";
            return;
        }

        Change(text => ModelSetup.WithProfileSettings(text, profile.Name, _arguments.Text.Trim(), maxTokens, temperature, seed, _thinking.IsChecked == true),
            $"The settings of \"{profile.Name}\" were saved. Tasks already queued keep the settings they were queued with; " +
            "a worker already running reads the file again when it next starts.");
    }

    private void RemoveProfile()
    {
        if (Selected is not { } profile ||
            MessageBox.Show(this, $"Remove the profile \"{profile.Name}\"? Its model files stay where they are, and reports made with it keep their record of it.",
                "Remove the profile?", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Change(text => ModelSetup.WithoutProfile(text, profile.Name), $"The profile \"{profile.Name}\" was removed.");
    }

    private void SetUpModels()
    {
        new ModelSetupWindow(this, _path, _baseDirectory).ShowDialog();
        Read();
    }

    private void Change(Func<string, string> change, string done)
    {
        try
        {
            AnalysisPlayer.EnsureSettingsFile(_path);
            File.WriteAllText(_path, change(File.ReadAllText(_path)));
            Read();
            _status.Text = done;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _status.Text = $"Not saved: {exception.Message}";
        }
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
        Change(text =>
        {
            text = AnalysisPlayer.WithDefault(text, ModelCapabilities.Text, (_text.SelectedItem as ComboBoxItem)?.Tag as string);
            return AnalysisPlayer.WithDefault(text, ModelCapabilities.Image, (_image.SelectedItem as ComboBoxItem)?.Tag as string);
        }, "The defaults were saved. Tasks already queued keep the profiles they were queued with.");
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

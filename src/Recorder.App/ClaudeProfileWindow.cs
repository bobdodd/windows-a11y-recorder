using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Recorder.Analysis;
using Recorder.Analysis.Setup;

namespace Recorder.App;

/// <summary>
/// "Add a Claude profile" (cloud-models.md, "6. The player"): the model,
/// from the Claude models offered or another model ID typed in, the
/// profile's name, the effort, the output limit, and the API key, in a
/// password box. The key is protected for this Windows user before it is
/// written, and never shown again. The profile is not made a default.
/// </summary>
internal sealed class ClaudeProfileWindow : Window
{
    private const string ModelDefault = "The model's default";

    private readonly string _path;
    private readonly ComboBox _model;
    private readonly TextBox _modelId;
    private readonly TextBox _name;
    private readonly ComboBox _effort;
    private readonly TextBox _maxTokens;
    private readonly PasswordBox _key;
    private readonly SelectableText _prices;
    private readonly SelectableText _status;

    public ClaudeProfileWindow(Window owner, string path)
    {
        Owner = owner;
        _path = path;
        Title = "Add a Claude profile";
        Width = 720;
        Height = 720;
        MinWidth = 480;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var page = new StackPanel { Margin = new Thickness(12) };
        page.Children.Add(Paragraph(
            "A task queued with a Claude profile sends the recording's facts and pictures to Anthropic, through the Anthropic API " +
            "with your API key. Choosing the profile for a task is the permission to send that recording. The profile is listed " +
            "with \"(sends to Anthropic)\" after its name wherever a profile is chosen, and is not made a default."));

        _model = new ComboBox { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 8) };
        foreach (var model in AnthropicSettings.Models)
        {
            _model.Items.Add(new ComboBoxItem { Content = $"{model.Name} ({model.Id})", Tag = model, MinHeight = 44 });
        }

        _model.Items.Add(new ComboBoxItem { Content = "Another model, its ID typed below", Tag = null, MinHeight = 44 });
        AutomationProperties.SetName(_model, "Model");
        page.Children.Add(new Label { Content = "_Model:", Target = _model, Padding = new Thickness(0) });
        page.Children.Add(_model);

        _modelId = Field(page, "Model _ID:", "Model ID");
        _name = Field(page, "Profile _name:", "Profile name");

        _prices = Paragraph("");
        AutomationProperties.SetName(_prices, "Prices");
        page.Children.Add(_prices);

        _effort = new ComboBox { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 8) };
        _effort.Items.Add(new ComboBoxItem { Content = ModelDefault, Tag = null, MinHeight = 44 });
        foreach (var effort in AnthropicSettings.Efforts)
        {
            _effort.Items.Add(new ComboBoxItem { Content = effort, Tag = effort, MinHeight = 44 });
        }

        _effort.SelectedIndex = 0;
        AutomationProperties.SetName(_effort, "Effort");
        page.Children.Add(new Label { Content = "_Effort:", Target = _effort, Padding = new Thickness(0, 4, 0, 2) });
        page.Children.Add(_effort);

        _maxTokens = Field(page, "Output _limit, tokens:", "Output limit in tokens");
        _maxTokens.Text = AnthropicSettings.DefaultMaxTokens.ToString(CultureInfo.InvariantCulture);
        page.Children.Add(Paragraph(
            "The model's thinking counts toward the output limit, so it is larger than a local profile's. An answer that reaches " +
            "it is asked for again, up to the attempts a step has."));

        _key = new PasswordBox { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_key, "Anthropic API key");
        page.Children.Add(new Label { Content = "Anthropic API _key:", Target = _key, Padding = new Thickness(0, 4, 0, 2) });
        page.Children.Add(_key);
        page.Children.Add(Paragraph(
            "The key is kept in the analysis settings file on this PC, protected for this Windows user with the Windows Data " +
            "Protection API, and is not shown again. It is not written to a recording, a report or a log."));

        _status = Paragraph("");
        AutomationProperties.SetName(_status, "Add a Claude profile status");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        page.Children.Add(_status);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var add = new Button { Content = "_Add the profile", MinWidth = 44, MinHeight = 44, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        add.Click += (_, _) => Add();
        buttons.Children.Add(add);
        buttons.Children.Add(new Button { Content = "Cancel", MinWidth = 88, MinHeight = 44, IsCancel = true });
        page.Children.Add(buttons);
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        _model.SelectionChanged += (_, _) => ModelChosen();
        _modelId.TextChanged += (_, _) => ShowPrices();
        _model.SelectedIndex = 0;
    }

    /// <summary>The name of the profile added, or null where none was.</summary>
    public string? Added { get; private set; }

    private static SelectableText Paragraph(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

    private static TextBox Field(Panel page, string label, string name)
    {
        var box = new TextBox { MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(box, name);
        page.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0, 4, 0, 2) });
        page.Children.Add(box);
        return box;
    }

    // A model from the list fills in its ID and a name; "another model"
    // leaves the ID to be typed.
    private void ModelChosen()
    {
        if ((_model.SelectedItem as ComboBoxItem)?.Tag is ClaudeModel model)
        {
            _modelId.Text = model.Id;
            _name.Text = model.Name;
        }
        else
        {
            _modelId.Text = "";
            _name.Text = "";
            _modelId.Focus();
        }

        ShowPrices();
    }

    private ServicePrices? Prices => AnthropicSettings.Model(_modelId.Text.Trim())?.Prices;

    private void ShowPrices()
    {
        _prices.Text = Prices is { } prices
            ? $"Prices for the cost estimate: US${Price(prices.InputPerMillion)} per million input tokens and US${Price(prices.OutputPerMillion)} " +
              "per million output tokens" +
              (prices.LongPrompt is { } longPrompt
                  ? $", or US${Price(longPrompt.InputPerMillion)} and US${Price(longPrompt.OutputPerMillion)} for a prompt over " +
                    $"{longPrompt.OverTokens.ToString("N0", CultureInfo.CurrentCulture)} tokens"
                  : "") +
              ", Anthropic's prices of 2026-11-11. They can be changed in the Model profiles window."
            : "There are no prices for this model ID, so tasks will show tokens without a cost estimate. Prices can be added in the " +
              "Model profiles window.";
    }

    private static string Price(double price) => price.ToString("0.00", CultureInfo.CurrentCulture);

    private void Add()
    {
        var modelId = _modelId.Text.Trim();
        var name = _name.Text.Trim();
        if (modelId.Length == 0 || modelId.Any(char.IsWhiteSpace))
        {
            _status.Text = "Type the model ID, such as claude-opus-5-5, without spaces.";
            return;
        }

        if (name.Length == 0)
        {
            _status.Text = "Type a name for the profile.";
            return;
        }

        if (!int.TryParse(_maxTokens.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxTokens) || maxTokens < 1)
        {
            _status.Text = "The output limit must be a whole number of at least 1, such as 32000.";
            return;
        }

        if (_key.Password.Trim().Length == 0)
        {
            _status.Text = "Paste your Anthropic API key into the key box.";
            return;
        }

        try
        {
            var protectedKey = new CurrentUserApiKeyProtector().Protect(_key.Password.Trim());
            var profile = ModelSetup.ClaudeProfile(name, modelId, (_effort.SelectedItem as ComboBoxItem)?.Tag as string, maxTokens, Prices, protectedKey);
            AnalysisPlayer.EnsureSettingsFile(_path);
            File.WriteAllText(_path, ModelSetup.WithClaudeProfile(File.ReadAllText(_path), profile));
            _key.Clear();
            Added = name;
            DialogResult = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or
            System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException)
        {
            _status.Text = $"Not added: {exception.Message}";
        }
    }
}

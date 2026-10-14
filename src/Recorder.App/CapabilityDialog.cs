using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Recorder.Analysis;

namespace Recorder.App;

/// <summary>
/// The auditor's description of the participant's capabilities, an input
/// to the analysis labelled reported (recording-summary.md, "The
/// participant's capabilities"). Saving adds a new version; the earlier
/// ones are kept and shown, read only.
/// </summary>
internal sealed class CapabilityDialog : Window
{
    private readonly TextBox _text;
    private readonly string _current;

    public CapabilityDialog(Window owner, IReadOnlyList<CapabilityDescription> versions)
    {
        Owner = owner;
        Title = "Describe the participant's capabilities";
        Width = 720;
        Height = 560;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _current = versions.Count == 0 ? "" : versions[0].Description;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var about = new TextBlock
        {
            Text = "What the participant is able to do: for example whether they work by sight, by hearing through a screen reader, " +
                "by magnification, by voice, by switch, or by head pointer or eye tracking. The model is told this is your statement, " +
                "not an observation. With no description, the participant's capabilities are unknown.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(about, 0);
        root.Children.Add(about);

        _text = new TextBox
        {
            Text = _current,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 12)
        };
        var written = versions.Count == 0
            ? "None written yet."
            : $"Current, written {versions[0].WrittenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)}:";
        var label = new Label { Content = "_Description. " + written, Target = _text, Padding = new Thickness(0) };
        Grid.SetRow(label, 1);
        root.Children.Add(label);
        Grid.SetRow(_text, 2);
        root.Children.Add(_text);
        AutomationProperties.SetName(_text, "Description of the participant's capabilities");

        var earlier = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 12),
            Text = versions.Count <= 1
                ? "No earlier versions."
                : string.Join(Environment.NewLine + Environment.NewLine, versions.Skip(1).Select(version =>
                    $"Written {version.WrittenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)}:{Environment.NewLine}{version.Description}"))
        };
        var earlierLabel = new Label { Content = "_Earlier versions, kept as they were:", Target = earlier, Padding = new Thickness(0) };
        Grid.SetRow(earlierLabel, 3);
        root.Children.Add(earlierLabel);
        Grid.SetRow(earlier, 4);
        root.Children.Add(earlier);
        AutomationProperties.SetName(earlier, "Earlier versions");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "_Save", MinWidth = 88, MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), IsDefault = false };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancel", MinWidth = 88, MinHeight = 44, IsCancel = true };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => _text.Focus();
    }

    /// <summary>The description to save, or null when nothing changed.</summary>
    public string? Description { get; private set; }

    private void Save()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0)
        {
            MessageBox.Show(this, "Write a description to save, or choose Cancel.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Description = text == _current.Trim() ? null : text;
        DialogResult = true;
    }
}

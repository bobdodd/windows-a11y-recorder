using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Npgsql;
using Recorder.Database;

namespace Recorder.App;

/// <summary>
/// Asks for a recording's readable name (recording-names.md): when a
/// recording starts, and to rename one later. The name may be left empty,
/// for a recording known by its session key. A name another recording in
/// the project has is refused here, before the dialog closes, so the
/// recording is not started or renamed with it. The name is saved with
/// spaces at either end removed. Every target is at least 44 pixels high,
/// and the text wraps rather than scrolling sideways.
/// </summary>
internal sealed class RecordingNameDialog : Window
{
    private readonly TextBox _name;
    private readonly SelectableText _problem;
    private readonly Button _confirm;
    private readonly Func<string?, Task<bool>> _taken;

    /// <param name="taken">Whether a name is already used in the project, for another recording.</param>
    public RecordingNameDialog(Window owner, string title, string introduction, string? current, string confirm, Func<string?, Task<bool>> taken)
    {
        _taken = taken;
        Owner = owner;
        Title = title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MinWidth = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new SelectableText { Text = introduction, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        _name = new TextBox { Text = current ?? "", MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 8) };
        AutomationProperties.SetName(_name, "Recording name");
        root.Children.Add(new Label { Content = "Recording _name, or empty for none:", Target = _name, Padding = new Thickness(0) });
        root.Children.Add(_name);

        // Why the name was refused, spoken as it appears.
        _problem = new SelectableText { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(_problem, "Problem with the name");
        root.Children.Add(_problem);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        _confirm = new Button { Content = confirm, MinWidth = 88, MinHeight = 44, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        _confirm.Click += async (_, _) => await ConfirmAsync();
        buttons.Children.Add(_confirm);
        buttons.Children.Add(new Button { Content = "Cancel", MinWidth = 88, MinHeight = 44, IsCancel = true });
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    /// <summary>The name typed, with spaces at either end removed, or null when it was left empty.</summary>
    public string? RecordingName { get; private set; }

    private async Task ConfirmAsync()
    {
        var name = RecordingNames.Normalize(_name.Text);
        _confirm.IsEnabled = false;
        try
        {
            if (await _taken(name))
            {
                Refuse($"A recording named {name} is already in this project. Give this recording another name, or leave it empty.");
                return;
            }
        }
        catch (NpgsqlException exception)
        {
            Refuse("Whether the name is free could not be checked: " + exception.Message);
            return;
        }
        finally
        {
            _confirm.IsEnabled = true;
        }

        RecordingName = name;
        DialogResult = true;
    }

    private void Refuse(string text)
    {
        _problem.Text = text;
        _problem.Visibility = Visibility.Visible;
        var peer = UIElementAutomationPeer.FromElement(_problem) ?? UIElementAutomationPeer.CreatePeerForElement(_problem);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantAll, text, "recording-name");
        _name.Focus();
        _name.SelectAll();
    }
}

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Recorder.App;

/// <summary>
/// Asks for a project's name, for a new project or to rename one
/// (projects.md). The name is saved with spaces at either end removed.
/// </summary>
internal sealed class ProjectNameDialog : Window
{
    private readonly TextBox _name;

    public ProjectNameDialog(Window owner, string title, string current)
    {
        Owner = owner;
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MinWidth = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new StackPanel { Margin = new Thickness(12) };
        _name = new TextBox { Text = current, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 12) };
        AutomationProperties.SetName(_name, "Project name");
        root.Children.Add(new Label { Content = "Project _name:", Target = _name, Padding = new Thickness(0) });
        root.Children.Add(_name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "_Save", MinWidth = 88, MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        save.Click += (_, _) => Save();
        buttons.Children.Add(save);
        buttons.Children.Add(new Button { Content = "Cancel", MinWidth = 88, MinHeight = 44, IsCancel = true });
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    /// <summary>The name typed, with spaces at either end removed.</summary>
    public string ProjectName { get; private set; } = "";

    private void Save()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Type a name for the project, or choose Cancel.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ProjectName = name;
        DialogResult = true;
    }
}

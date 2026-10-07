using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Recorder.Database.RecordingFiles;

namespace Recorder.App;

// Asks which recorded page to recreate at a frame: each top-level document
// at the frame, most recently presented first, browser interface pages last.
internal sealed class RecordedDocumentDialog : Window
{
    private readonly ListBox _list;
    private readonly Button _open;

    public RecordedDocumentDialog(Window owner, string frameTime, IReadOnlyList<RecordedDocumentChoice> choices, Func<long, string> formatTime)
    {
        Owner = owner;
        Title = "Inspect page at this frame";
        Width = 720;
        Height = 420;
        MinWidth = 420;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _list = new ListBox { Margin = new Thickness(0, 4, 0, 12) };
        var label = new Label
        {
            Content = $"_Pages at {frameTime}, most recently drawn first:",
            Target = _list,
            Padding = new Thickness(0),
        };
        Grid.SetRow(label, 0);
        root.Children.Add(label);

        foreach (var choice in choices)
        {
            var text = Describe(choice, formatTime);
            var item = new ListBoxItem { Content = text, Tag = choice };
            AutomationProperties.SetName(item, text);
            _list.Items.Add(item);
        }
        AutomationProperties.SetName(_list, "Pages at the frame");
        _list.SelectedIndex = 0;
        _list.MouseDoubleClick += (_, _) => Accept();
        Grid.SetRow(_list, 1);
        root.Children.Add(_list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _open = new Button { Content = "_Open", IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        _open.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        buttons.Children.Add(_open);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) =>
        {
            if (_list.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem first)
            {
                first.Focus();
                Keyboard.Focus(first);
            }
        };
    }

    public RecordedDocumentChoice? Chosen { get; private set; }

    private void Accept()
    {
        if (_list.SelectedItem is ListBoxItem { Tag: RecordedDocumentChoice choice })
        {
            Chosen = choice;
            DialogResult = true;
        }
    }

    private static string Describe(RecordedDocumentChoice choice, Func<long, string> formatTime)
    {
        var basis = choice.Basis.Basis == "presented" && choice.Basis.PresentedTime is { } presented
            ? $"last drawn at {formatTime(presented)}"
            : "not drawn at or before the frame; its state at the frame time";
        var kind = choice.BrowserInterface ? "Browser interface page. " : "";
        return $"{kind}{choice.Url}. {basis}.";
    }

}

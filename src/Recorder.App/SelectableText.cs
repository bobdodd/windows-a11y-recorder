using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Recorder.App;

/// <summary>
/// Text that can be selected and copied as on a web page: press and drag,
/// or click and then Shift with the arrow keys, and Ctrl+C copies it
/// (owner, 2026-10-14: "I want to be able to click and drag/use arrow keys
/// to select any text on screen, just as I would on websites, and most
/// apps"). It is read only, not a tab stop, and looks like a text block.
/// Assistive technology finds it as text named by what it says.
///
/// In a list or tree row, a click that selects nothing is passed on to
/// the row, so the row is selected and its own click handlers run as
/// before; only a press and drag selects text there. See
/// docs/architecture/player-layout.md, "Selecting and copying text".
/// </summary>
public class SelectableText : TextBox
{
    static SelectableText()
    {
        // The template and colours come from Themes/Generic.xaml, so a text
        // box's border, padding, and theme colours do not apply.
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SelectableText), new FrameworkPropertyMetadata(typeof(SelectableText)));
        IsReadOnlyProperty.OverrideMetadata(typeof(SelectableText), new FrameworkPropertyMetadata(true));
        IsTabStopProperty.OverrideMetadata(typeof(SelectableText), new FrameworkPropertyMetadata(false));
        IsUndoEnabledProperty.OverrideMetadata(typeof(SelectableText), new FrameworkPropertyMetadata(false));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SelectableTextAutomationPeer(this);

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (SelectionLength == 0 && ItemContainer() is { } container)
        {
            // A click, not a selection: the row takes it, as it did before
            // its text could be selected.
            switch (container)
            {
                case TreeViewItem item:
                    item.IsSelected = true;
                    break;
                case ListBoxItem item:
                    item.IsSelected = true;
                    break;
            }

            container.Focus();
            e.Handled = false;
        }
    }

    // The list or tree row this text is in, if any.
    private Control? ItemContainer()
    {
        for (DependencyObject? node = VisualTreeHelper.GetParent(this); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListBoxItem or TreeViewItem)
            {
                return (Control)node;
            }

            if (node is ItemsControl)
            {
                return null;
            }
        }

        return null;
    }
}

/// <summary>Text to assistive technology, named by what it says, as a text block is.</summary>
internal sealed class SelectableTextAutomationPeer(SelectableText owner) : TextBoxAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string GetClassNameCore() => nameof(SelectableText);

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? ((SelectableText)Owner).Text : name;
    }
}

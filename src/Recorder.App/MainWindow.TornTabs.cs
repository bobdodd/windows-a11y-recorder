using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Recorder.App;

// The tabs of the panel under the timeline tear out into windows of their
// own, as tabs do in web browsers (player-layout.md, "Tabs that tear out").
// A tab's icon button opens it in its own window with one click, and
// dragging the tab away from the tab strip does the same, opening the
// window where the mouse button is released; closing the window puts the
// tab back in its place. A window opened by its button opens where the
// tab's content was, at its size; each keeps its place and size for the
// rest of the session.
// Agreed with the owner on 2026-10-12.
public partial class MainWindow
{
    // How far, in pixels, the pointer must be dragged above or below a tab
    // to tear it out, so a small movement while clicking does not.
    private const double TearDistance = 40;

    private readonly Dictionary<TabItem, Window> _tornTabs = [];
    private readonly Dictionary<TabItem, Rect> _tornTabBounds = [];
    private TabItem? _draggedTab;

    private void TearOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TabItem tab })
        {
            TearOut(tab, null);
            e.Handled = true;
        }
    }

    private void DetailsTab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only a press on the tab itself starts a drag: a press within its
        // content also reaches the tab, by the logical tree, and released
        // there, below the tab strip, it would tear the tab out (owner,
        // 2026-10-13: "Clicking inside the tab causes it to tear off"). A
        // press on the tab's own button is a click of the button.
        if (sender is not TabItem tab ||
            !IsWithinTab(e.OriginalSource as DependencyObject, tab) ||
            IsWithinButton(e.OriginalSource as DependencyObject, tab))
        {
            return;
        }

        _draggedTab = tab;
        tab.CaptureMouse();
    }

    private void DetailsTab_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedTab is not { } tab || !ReferenceEquals(sender, tab))
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndTabDrag();
            return;
        }

        // Beyond the tear distance, the move cursor shows that releasing
        // the button opens the tab in its own window there; back within
        // it, releasing is a click.
        Mouse.OverrideCursor = IsTearPoint(tab, e.GetPosition(tab)) ? Cursors.SizeAll : null;
    }

    // Tears the tab out where the button is released, when that is beyond
    // the tear distance (owner, 2026-10-12: "Positioning of the new window
    // should be based on mouse up").
    private void DetailsTab_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedTab is not { } tab || !ReferenceEquals(sender, tab))
        {
            EndTabDrag();
            return;
        }

        var point = e.GetPosition(tab);
        EndTabDrag();
        if (IsTearPoint(tab, point))
        {
            TearOut(tab, tab.PointToScreen(point));
            e.Handled = true;
        }
    }

    private static bool IsTearPoint(TabItem tab, Point point) =>
        point.Y < -TearDistance || point.Y > tab.ActualHeight + TearDistance;

    private void DetailsTab_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(sender, _draggedTab))
        {
            _draggedTab = null;
            Mouse.OverrideCursor = null;
        }
    }

    private void EndTabDrag()
    {
        Mouse.OverrideCursor = null;
        if (_draggedTab is { } tab)
        {
            _draggedTab = null;
            tab.ReleaseMouseCapture();
        }
    }

    // Moves a tab's content into a window of its own. The screen point, in
    // device pixels, is where a drag left the tab strip; without one, the
    // window opens where the content was.
    private void TearOut(TabItem tab, Point? screenPoint)
    {
        if (_tornTabs.TryGetValue(tab, out var open))
        {
            open.Activate();
            return;
        }

        if (tab.Content is not FrameworkElement content)
        {
            return;
        }

        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Rect bounds;
        if (_tornTabBounds.TryGetValue(tab, out var kept))
        {
            bounds = kept;
        }
        else
        {
            var size = new Size(Math.Max(content.ActualWidth, 480), Math.Max(content.ActualHeight, 320) + 40);
            var origin = content.IsVisible
                ? fromDevice.Transform(content.PointToScreen(new Point(0, 0)))
                : new Point(Left + 80, Top + 80);
            bounds = new Rect(origin, size);
        }

        // Dragged out: the window's title bar under the pointer where the
        // button was released, kept on the screen.
        if (screenPoint is { } point)
        {
            var at = fromDevice.Transform(point);
            var left = Math.Clamp(
                at.X - 40,
                SystemParameters.VirtualScreenLeft,
                Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - bounds.Width));
            var top = Math.Clamp(
                at.Y - 15,
                SystemParameters.VirtualScreenTop,
                Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60));
            bounds.Location = new Point(left, top);
        }

        var name = AutomationProperties.GetName(tab);
        tab.Content = null;
        var holder = new Border { Padding = new Thickness(8), Child = content };
        var window = new Window
        {
            Title = $"{name}, Windows A11y Recorder",
            Content = holder,
            Background = (Brush)FindResource("PanelBackground"),
            Foreground = Foreground,
            FontFamily = FontFamily,
            FontSize = FontSize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            ShowInTaskbar = true
        };

        // The player's styles and brushes, so the content keeps its look
        // and contrast in the window.
        foreach (var key in Resources.Keys)
        {
            window.Resources[key] = Resources[key];
        }

        AutomationProperties.SetName(window, name);
        window.Closed += (_, _) => PutBack(tab, window, holder, content);
        _tornTabs[tab] = window;
        tab.Visibility = Visibility.Collapsed;
        if (ReferenceEquals(DetailsTabControl.SelectedItem, tab))
        {
            DetailsTabControl.SelectedItem = DetailsTabControl.Items.OfType<TabItem>()
                .FirstOrDefault(item => item.Visibility == Visibility.Visible);
        }

        UpdateTornTabsPlaceholder();
        window.Show();
        window.Activate();
    }

    private void PutBack(TabItem tab, Window window, Border holder, FrameworkElement content)
    {
        _tornTabBounds[tab] = window.RestoreBounds.IsEmpty
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        _tornTabs.Remove(tab);
        holder.Child = null;
        tab.Content = content;
        tab.Visibility = Visibility.Visible;
        if (!_closingTornTabs)
        {
            DetailsTabControl.SelectedItem = tab;
        }

        UpdateTornTabsPlaceholder();
    }

    // With every tab in a window of its own, the panel says so.
    private void UpdateTornTabsPlaceholder()
    {
        var anyShown = DetailsTabControl.Items.OfType<TabItem>().Any(item => item.Visibility == Visibility.Visible);
        DetailsTabControl.Visibility = anyShown ? Visibility.Visible : Visibility.Hidden;
        AllTabsTornOutTextBlock.Visibility = anyShown ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool _closingTornTabs;

    // The tabs' windows close with the player.
    private void CloseTornTabs()
    {
        _closingTornTabs = true;
        foreach (var window in _tornTabs.Values.ToList())
        {
            window.Close();
        }

        _closingTornTabs = false;
    }

    private static bool IsWithinButton(DependencyObject? element, DependencyObject stop)
    {
        for (var current = element; current is not null && !ReferenceEquals(current, stop); current = ParentOf(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    // Whether an element is part of the tab itself, its header, rather than
    // its content: the content's visual parents lead to the tab control's
    // content area, not to the tab.
    private static bool IsWithinTab(DependencyObject? element, TabItem tab)
    {
        for (var current = element; current is not null; current = ParentOf(current))
        {
            if (ReferenceEquals(current, tab))
            {
                return true;
            }

            if (ReferenceEquals(current, tab.Content))
            {
                return false;
            }
        }

        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);
}

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Recorder.App;

// The tabs of the panel under the timeline tear out into windows of their
// own, as tabs do in web browsers (player-layout.md, "Tabs that tear out").
// A tab's icon button opens it in its own window with one click, and
// dragging the tab away from the tab strip does the same; closing the
// window puts the tab back in its place. A window opens where the tab's
// content was, at its size, and keeps its place for the rest of the session.
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
        // A press on the tab's own button is a click of the button, not the
        // start of a drag.
        if (sender is not TabItem tab || IsWithinButton(e.OriginalSource as DependencyObject, tab))
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

        var point = e.GetPosition(tab);
        if (point.Y < -TearDistance || point.Y > tab.ActualHeight + TearDistance)
        {
            var screen = tab.PointToScreen(point);
            EndTabDrag();
            TearOut(tab, screen);
        }
    }

    private void DetailsTab_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndTabDrag();

    private void EndTabDrag()
    {
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

        if (screenPoint is { } point)
        {
            var at = fromDevice.Transform(point);
            bounds.Location = new Point(at.X - 40, at.Y - 20);
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

    private static DependencyObject? ParentOf(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);
}

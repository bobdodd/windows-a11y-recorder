using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Recorder.App;

// The panels under the timeline (the browser navigations, the reading
// position's steps, what NVDA had to announce, the accessibility tree, the
// checks and the analysis) each open in a window of their own from a
// toggle button, as they did not fit in the space under the video (owner,
// 2026-11-18; player-layout.md, "Windows opened by buttons"). A toggle is
// pressed while its window is open: pressing it again closes the window,
// and closing the window itself releases the toggle. While its window is
// closed a panel's content is kept, not shown, in the panel host, so the
// player keeps it up to date. Each window keeps its place and size for
// the rest of the session, and is owned by the player, so it stays in
// front of the player's window (owner, 2026-11-12).
public partial class MainWindow
{
    private const double PanelWindowWidth = 640;
    private const double PanelWindowHeight = 520;

    private readonly Dictionary<ContentControl, Window> _panelWindows = [];
    private readonly Dictionary<ContentControl, Rect> _panelWindowBounds = [];
    private bool _closingPanelWindows;

    private void PanelWindowToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: ContentControl panel } toggle)
        {
            return;
        }

        if (toggle.IsChecked == true)
        {
            OpenPanelWindow(panel);
        }
        else if (_panelWindows.TryGetValue(panel, out var window))
        {
            window.Close();
        }

        e.Handled = true;
    }

    // The toggle that opens a panel's window.
    private ToggleButton? ToggleOf(ContentControl panel) =>
        PanelWindowButtons.Children.OfType<ToggleButton>().FirstOrDefault(toggle => ReferenceEquals(toggle.Tag, panel));

    // Opens a panel's window, or brings it to the front if it is open.
    private void OpenPanelWindow(ContentControl panel)
    {
        if (_panelWindows.TryGetValue(panel, out var open))
        {
            open.Activate();
            return;
        }

        if (panel.Content is not FrameworkElement content)
        {
            return;
        }

        var bounds = _panelWindowBounds.TryGetValue(panel, out var kept) ? kept : FirstBounds(panel);
        var name = AutomationProperties.GetName(panel);
        panel.Content = null;
        var holder = new Border { Child = content };
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
            ShowInTaskbar = true,

            // Owned by the player, so it stays in front of the player's
            // window, and is minimised and restored with it (owner,
            // 2026-11-12: "they keep getting lost behind"). Other
            // programs' windows can still come in front of both.
            Owner = this
        };

        // The player's styles and brushes, so the content keeps its look
        // and contrast in the window.
        foreach (var key in Resources.Keys)
        {
            window.Resources[key] = Resources[key];
        }

        AutomationProperties.SetName(window, name);
        window.Closed += (_, _) => PutBack(panel, window, holder, content);
        _panelWindows[panel] = window;
        if (ToggleOf(panel) is { } toggle)
        {
            toggle.IsChecked = true;
        }

        window.Show();
        window.Activate();
    }

    // Where a panel's window first opens: over the player, each panel a
    // little further down and to the right, at a size that fits the
    // screen.
    private Rect FirstBounds(ContentControl panel)
    {
        var index = Math.Max(0, PanelHost.Children.IndexOf(panel));
        var area = SystemParameters.WorkArea;
        var width = Math.Min(PanelWindowWidth, area.Width);
        var height = Math.Min(PanelWindowHeight, area.Height);
        var origin = WindowState == WindowState.Maximized ? area.TopLeft : new Point(Left, Top);
        var left = Math.Clamp(origin.X + 80 + (index * 32), area.Left, Math.Max(area.Left, area.Right - width));
        var top = Math.Clamp(origin.Y + 80 + (index * 32), area.Top, Math.Max(area.Top, area.Bottom - height));
        return new Rect(left, top, width, height);
    }

    // A panel's window was closed, from the window or by its toggle: its
    // content goes back to the host, and the toggle is released.
    private void PutBack(ContentControl panel, Window window, Border holder, FrameworkElement content)
    {
        _panelWindowBounds[panel] = window.RestoreBounds.IsEmpty
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        _panelWindows.Remove(panel);
        holder.Child = null;
        panel.Content = content;
        if (ToggleOf(panel) is { } toggle)
        {
            toggle.IsChecked = false;
            if (!_closingPanelWindows && IsVisible && toggle.IsVisible)
            {
                // Back to the player, at the toggle that opens it again.
                Activate();
                toggle.Focus();
            }
        }
    }

    // The panels' windows close with the player.
    private void ClosePanelWindows()
    {
        _closingPanelWindows = true;
        foreach (var window in _panelWindows.Values.ToList())
        {
            window.Close();
        }

        _closingPanelWindows = false;
    }
}

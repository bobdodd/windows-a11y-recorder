using System.Windows;
using Recorder.Collectors.Android;
using Recorder.Coordinator;

namespace Recorder.App;

public partial class MainWindow
{
    private AndroidLivePreview? _androidLivePreview;
    private AndroidLiveTree? _androidLiveTree;
    private AndroidLivePreviewWindow? _androidLivePreviewWindow;

    private void EnableAndroidLivePreview()
    {
        var available = _androidLivePreview is not null;
        AndroidLivePreviewMenuItem.IsEnabled = available;
        AndroidLivePreviewButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseAndroidLivePreview()
    {
        _androidLivePreview = null;
        _androidLiveTree = null;
        AndroidLivePreviewMenuItem.IsEnabled = false;
        AndroidLivePreviewButton.Visibility = Visibility.Collapsed;
        _androidLivePreviewWindow?.Close();
    }

    private void AndroidLivePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_transitioning || _coordinator?.State != RecordingSessionState.Recording ||
            _androidLivePreview is not { } preview || _androidLiveTree is not { } tree)
        {
            return;
        }

        if (_androidLivePreviewWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }
            open.Activate();
            return;
        }

        var window = new AndroidLivePreviewWindow(preview, tree) { Owner = this };
        _androidLivePreviewWindow = window;
        window.Closed += (_, _) =>
        {
            _androidLivePreviewWindow = null;
            if (_androidLivePreview is not null && !_transitioning && IsVisible)
            {
                Activate();
                if (AndroidLivePreviewButton.IsVisible)
                {
                    AndroidLivePreviewButton.Focus();
                }
                else
                {
                    SettingsToggle.Focus();
                }
            }
        };
        window.Show();
    }
}

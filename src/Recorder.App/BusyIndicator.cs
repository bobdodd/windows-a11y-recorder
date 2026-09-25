using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace Recorder.App;

/// <summary>
/// Shows that the app is working on a task the user must wait for, in three
/// ways: the wait cursor, a visible working indicator, and a UI Automation
/// notification that screen readers speak when the task starts.
/// </summary>
/// <remarks>
/// Screen readers do not report cursor shape, so the notification is what
/// tells a screen-reader user to wait. The indicator animates only when
/// Windows animations are turned on, and its text is shown either way.
/// Scopes may nest; the cursor and indicator clear when the outermost scope
/// ends, and only the outermost scope announces its start.
/// </remarks>
internal sealed class BusyIndicator
{
    private const string BusyActivityId = "Recorder.App.Busy";
    private const string CollectorHealthActivityId =
        "Recorder.App.CollectorHealth";

    private readonly UIElement _announcer;
    private readonly FrameworkElement _panel;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _messageTextBlock;
    private int _depth;

    public BusyIndicator(
        UIElement announcer,
        FrameworkElement panel,
        ProgressBar progressBar,
        TextBlock messageTextBlock)
    {
        _announcer = announcer;
        _panel = panel;
        _progressBar = progressBar;
        _messageTextBlock = messageTextBlock;
    }

    public IDisposable Begin(string message)
    {
        _depth++;
        _messageTextBlock.Text = message;
        if (_depth == 1)
        {
            var animate = SystemParameters.ClientAreaAnimation;
            _progressBar.IsIndeterminate = animate;
            _progressBar.Visibility = animate ? Visibility.Visible : Visibility.Collapsed;
            _panel.Visibility = Visibility.Visible;
            Mouse.OverrideCursor = Cursors.Wait;
            Announce(
                $"{message} Please wait.",
                AutomationNotificationKind.Other,
                BusyActivityId);
        }

        return new Scope(this);
    }

    /// <summary>
    /// Speaks a task outcome. Outcomes are queued behind earlier
    /// announcements rather than replacing them.
    /// </summary>
    public void AnnounceCompleted(string message) =>
        Announce(message, AutomationNotificationKind.ActionCompleted, BusyActivityId);

    /// <summary>
    /// Speaks a change the user did not ask for and may need to act on, such
    /// as a collector that stopped supplying evidence during a recording.
    /// </summary>
    public void AnnounceAlert(string message) =>
        Announce(
            message,
            AutomationNotificationKind.Other,
            CollectorHealthActivityId);

    private void End()
    {
        if (_depth == 0)
        {
            return;
        }

        _depth--;
        if (_depth == 0)
        {
            Mouse.OverrideCursor = null;
            _progressBar.IsIndeterminate = false;
            _panel.Visibility = Visibility.Collapsed;
            _messageTextBlock.Text = string.Empty;
        }
    }

    private void Announce(
        string message,
        AutomationNotificationKind kind,
        string activityId)
    {
        var peer = UIElementAutomationPeer.FromElement(_announcer) ??
            UIElementAutomationPeer.CreatePeerForElement(_announcer);
        peer?.RaiseNotificationEvent(
            kind,
            AutomationNotificationProcessing.ImportantAll,
            message,
            activityId);
    }

    private sealed class Scope(BusyIndicator owner) : IDisposable
    {
        private BusyIndicator? _owner = owner;

        public void Dispose()
        {
            _owner?.End();
            _owner = null;
        }
    }
}

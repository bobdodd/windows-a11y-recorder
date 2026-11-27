using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using Recorder.Analysis;
using Recorder.Collectors.Android;

namespace Recorder.App;

public partial class AndroidInteractionWindow : Window
{
    private readonly CancellationTokenSource _closed = new();
    private bool _running = true;
    private bool _isClosed;

    public AndroidInteractionWindow(AndroidInteractionExploration exploration, Func<CancellationToken, Task<ModelProfile?>> vision,
        Func<string, bool> marker, Action<Task> track)
    {
        InitializeComponent();
        RunStatus.Text = "Starting the screen investigation. Leave the phone untouched.";
        Closed += (_, _) =>
        {
            _isClosed = true;
            Stop();
        };
        ContentRendered += Start;

        async void Start(object? sender, EventArgs args)
        {
            ContentRendered -= Start;
            if (_isClosed)
            {
                return;
            }
            RunStatus.Focus();
            var progress = new Progress<string>(status =>
            {
                if (!_isClosed && _running)
                {
                    ShowStatus(status);
                }
            });
            var task = exploration.InvestigateAsync(
                (snapshot, token) => AndroidInteractionScan.ReadAsync(snapshot, vision, token), marker, progress, _closed.Token);
            track(task.ContinueWith(_ => { }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
            try
            {
                var result = await task;
                if (!_isClosed)
                {
                    Report.Document = AndroidInteractionReportDocument.Create(result);
                    ShowStatus(result.Restoration is null ? result.Status : result.Status + " " + result.Restoration);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Android investigation stopped: {0}", ex.GetType().Name);
                if (!_isClosed)
                {
                    ShowStatus("The investigation could not finish. Check the phone's TalkBack setting before continuing.");
                }
            }
            finally
            {
                _running = false;
                if (!_isClosed)
                {
                    CloseReport.Content = "_Close report";
                    CloseReport.IsEnabled = true;
                }
            }
        }
    }

    internal void Stop() => _closed.Cancel();

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            Stop();
            CloseReport.IsEnabled = false;
            ShowStatus("Stopping the investigation and restoring the original TalkBack setting.");
        }
        else
        {
            Close();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close_Click(sender, e);
        }
    }

    private void Report_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Copy)
        {
            return;
        }
        // The report still copies selected text and captions, not WPF rich data
        // that could carry the original frame behind a clipped drawing.
        e.Handled = true;
        var text = Report.Selection.Text;
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Trace.TraceWarning("The clipboard was unavailable when copying the Android investigation report.");
            }
        }
    }

    private void ShowStatus(string text)
    {
        RunStatus.Text = text;
        var peer = UIElementAutomationPeer.FromElement(RunStatus) ?? UIElementAutomationPeer.CreatePeerForElement(RunStatus);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}

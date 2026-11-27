using System.Diagnostics;
using System.Text;
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
                    Report.Text = Format(result);
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

    private void ShowStatus(string text)
    {
        RunStatus.Text = text;
        var peer = UIElementAutomationPeer.FromElement(RunStatus) ?? UIElementAutomationPeer.CreatePeerForElement(RunStatus);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static string Format(AndroidInvestigationReport report)
    {
        var text = new StringBuilder();
        if (report.Tree is { } tree)
        {
            text.AppendLine($"Current viewport: Android tree {tree}; " +
                (report.Frame is { } frame ? $"recorded picture {frame}." : "no mapped recorded picture."));
        }
        if (!string.IsNullOrWhiteSpace(report.Coverage))
        {
            text.AppendLine(report.Coverage);
        }
        text.AppendLine().AppendLine($"TalkBack Explore by Touch: {report.TalkBack}")
            .AppendLine($"Keyboard Tab: {report.Tab}").AppendLine($"Direct touch: {report.DirectTouch}");
        if (report.Restoration is { } warning)
        {
            text.AppendLine(warning);
        }
        text.AppendLine().AppendLine("An Android declaration is not proof of activation. A local picture change is separate from an Android click. " +
            "Not found or not tested does not mean inaccessible. This is one viewport; off-screen and gesture-only targets may be missed.");
        if (report.Findings.Count == 0)
        {
            return text.AppendLine().AppendLine("No candidates were reported.").ToString();
        }
        var number = 0;
        foreach (var finding in report.Findings)
        {
            text.AppendLine().AppendLine($"{++number}. {finding.Item.Label}");
            var declared = finding.Item.NodeIndex is { } index ? $" (tree item {index})" : "";
            text.AppendLine($"  Discovery: {finding.Item.Evidence}{declared}.");
            text.AppendLine("  TalkBack touch: " + string.Join(" ", finding.ExploreByTouch));
            text.AppendLine("  Tab: " + string.Join(" ", finding.Tab));
            text.AppendLine("  Direct touch: " + string.Join(" ", finding.DirectTouch));
        }
        return text.ToString();
    }
}

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace UiaLoadSource;

// A validation fixture that raises UI Automation name changes on text
// elements at a set rate, like the application that overflowed the recorder's
// UI Automation queue, so a validation run applies the same load every time.
// Events are raised only while a UI Automation client listens for property
// changes. When the window closes, it writes what it raised as JSON.
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = LoadOptions.Parse(args);
        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new LoadWindow(options);
        application.Run(window);
        return 0;
    }
}

internal sealed record LoadOptions(int RatePerSecond, int Labels, string? SummaryPath)
{
    public static LoadOptions Parse(string[] args)
    {
        var rate = 2_000;
        var labels = 40;
        string? summary = null;
        for (var index = 0; index + 1 < args.Length; index += 2)
        {
            switch (args[index])
            {
                case "--rate":
                    rate = int.Parse(args[index + 1], CultureInfo.InvariantCulture);
                    break;
                case "--labels":
                    labels = int.Parse(args[index + 1], CultureInfo.InvariantCulture);
                    break;
                case "--summary":
                    summary = args[index + 1];
                    break;
                default:
                    throw new ArgumentException($"Unknown option {args[index]}.");
            }
        }

        if (rate <= 0 || labels <= 0)
        {
            throw new ArgumentException("The rate and label count must be positive.");
        }

        return new LoadOptions(rate, labels, summary);
    }
}

internal sealed class LoadWindow : Window
{
    private readonly LoadOptions _options;
    private readonly TextBlock[] _labels;
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<long, long> _raisedBySecond = [];
    private readonly DispatcherTimer _timer;
    private long _due;
    private long _raised;
    private long _next;
    private double _listenedSeconds;
    private double _lastTickSeconds;
    private double? _firstRaisedSeconds;
    private double? _lastRaisedSeconds;

    public LoadWindow(LoadOptions options)
    {
        _options = options;
        Title = "UI Automation load source";
        Width = 240;
        Height = 160;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width;
        Top = area.Bottom - Height;

        var panel = new WrapPanel();
        _labels = new TextBlock[options.Labels];
        for (var index = 0; index < _labels.Length; index++)
        {
            _labels[index] = new TextBlock { Text = "0", Margin = new Thickness(2) };
            panel.Children.Add(_labels[index]);
        }

        Content = panel;

        // The timer fires at the system timer resolution, not every
        // millisecond, so each tick raises every change due since the last.
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(1),
            DispatcherPriority.Background,
            (_, _) => Tick(),
            Dispatcher);
        Loaded += (_, _) =>
        {
            _clock.Start();
            _timer.Start();
        };
        Closing += (_, _) =>
        {
            _timer.Stop();
            WriteSummary();
        };
    }

    private void Tick()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var elapsed = now - _lastTickSeconds;
        _lastTickSeconds = now;
        if (!AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged))
        {
            return;
        }

        _listenedSeconds += elapsed;
        var target = (long)(_listenedSeconds * _options.RatePerSecond);
        for (; _due < target; _due++)
        {
            var label = _labels[_next % _labels.Length];
            var oldName = label.Text;
            var newName = _next.ToString(CultureInfo.InvariantCulture);
            label.Text = newName;
            _next++;

            var peer = UIElementAutomationPeer.CreatePeerForElement(label);
            peer?.RaisePropertyChangedEvent(
                AutomationElementIdentifiers.NameProperty,
                oldName,
                newName);
            _raised++;
            _firstRaisedSeconds ??= now;
            _lastRaisedSeconds = now;
            var second = (long)now;
            _raisedBySecond[second] = _raisedBySecond.GetValueOrDefault(second) + 1;
        }
    }

    private void WriteSummary()
    {
        if (_options.SummaryPath is null)
        {
            return;
        }

        var span = _firstRaisedSeconds is { } first && _lastRaisedSeconds is { } last
            ? last - first
            : 0;
        var summary = new
        {
            processId = Environment.ProcessId,
            targetRatePerSecond = _options.RatePerSecond,
            labels = _options.Labels,
            raised = _raised,
            listenedSeconds = Math.Round(_listenedSeconds, 3),
            raisingSpanSeconds = Math.Round(span, 3),
            meanRaisedPerSecond = span > 0 ? Math.Round(_raised / span, 1) : 0,
            peakRaisedPerSecond = _raisedBySecond.Count > 0 ? _raisedBySecond.Values.Max() : 0
        };
        File.WriteAllText(
            _options.SummaryPath,
            JsonSerializer.Serialize(summary));
    }
}

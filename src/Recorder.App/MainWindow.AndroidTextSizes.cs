using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Recorder.Contracts;

namespace Recorder.App;

public partial class MainWindow
{
    private static string TextSizeSummary(AndroidTextSizeSnapshot snapshot) =>
        $"Read text sizes before recording for {snapshot.PackageName}. " +
        $"{snapshot.Nodes.Count(node => node.TextSizePx.HasValue):N0} of {snapshot.Nodes.Count:N0} nodes reported a size." +
        (snapshot.Status == "partial" ? " Android returned a partial snapshot." : "");

    private void ShowPreRecordingTextSizes(AndroidTextSizeSnapshot? snapshot)
    {
        PreRecordingTextSizeStatus.Text = snapshot is not null ? TextSizeSummary(snapshot) : "";
        PreRecordingTextSizeStatus.Visibility = snapshot is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AndroidTextSizesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var manifest = _playbackArchive?.Manifest;
        var snapshot = manifest?.AndroidPreRecordingTextSizes;
        const string title = "Pre-recording text sizes";
        if (snapshot is null)
        {
            MessageBox.Show(this, manifest is null
                ? "Open a recording to view its pre-recording text sizes."
                : "This recording contains no pre-recording text-size snapshot.",
                title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var text = new SelectableText
        {
            Text = TextSizeSummary(snapshot) + Environment.NewLine +
                $"Read at {snapshot.ReceivedUtc:u}. Source: Android accessibility rendering information." +
                Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, snapshot.Nodes.Select(DescribeTextSizeNode)),
            TextWrapping = TextWrapping.Wrap,
            IsTabStop = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 8)
        };
        AutomationProperties.SetName(text, title);
        var close = new Button
        {
            Content = "_Close", IsCancel = true, MinWidth = 88, MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var content = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(close, Dock.Bottom);
        content.Children.Add(close);
        content.Children.Add(text);
        var window = new Window
        {
            Title = title, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 660, Height = 440, MaxHeight = SystemParameters.WorkArea.Height,
            ShowInTaskbar = false, Content = content
        };
        window.Loaded += (_, _) => text.Focus();
        window.ShowDialog();
    }

    private static string DescribeTextSizeNode(AndroidTextSizeNode node)
    {
        var unit = node.TextSizeUnit switch
        {
            null => "unit not reported",
            0 => "px (0)",
            1 => "dp (1)",
            2 => "sp (2)",
            3 => "pt (3)",
            4 => "in (4)",
            5 => "mm (5)",
            var other => $"Android unit {other}"
        };
        var result = node.TextSizePx is { } size
            ? $"{size.ToString("G", CultureInfo.CurrentCulture)} px rendered; {unit}"
            : node.Status == "not-advertised" ? "size not advertised" : "size unavailable";
        return $"Node {node.Index}, {node.ViewId ?? "no view ID"}: {result}; " +
            $"Android visible to user: {(node.VisibleToUser ? "yes" : "no")}.";
    }
}

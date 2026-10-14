using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Recorder.Analysis;

namespace Recorder.App;

/// <summary>
/// The reading view of an analysis report, in its own window beside the
/// player (owner, 2026-10-13). The prose reads as written, with each
/// reference as plain text, and each paragraph is followed by a row of
/// buttons, at least 44 by 44 pixels, one for each reference it cites with
/// a time in the recording, each moving the playhead to that evidence.
/// In the evidence list, each reference is such a button too. See
/// docs/architecture/analysis-runner.md, "Decisions on the player
/// (2026-10-13)".
/// </summary>
internal sealed class ReportWindow : Window
{
    private readonly ReportDocument _report;
    private readonly Func<long, string?> _seek;
    private readonly TextBlock _status;

    /// <param name="seek">Moves the playhead to a time; returns why it could not, or null.</param>
    public ReportWindow(string path, ReportDocument report, string? warning, Func<long, string?> seek)
    {
        _report = report;
        _seek = seek;
        Title = (report.Title ?? "Analysis report") + ", Windows A11y Recorder";
        Width = 900;
        Height = 760;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap, Foreground = SystemColors.GrayTextBrush });
        if (warning is not null)
        {
            var text = new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
            header.Children.Add(text);
        }

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(_status, "Report status");
        header.Children.Add(_status);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var body = new StackPanel();
        foreach (var block in report.Blocks)
        {
            body.Children.Add(Block(block));
            if (block.Kind != ReportBlockKind.Table && Buttons(block.References) is { } buttons)
            {
                body.Children.Add(buttons);
            }
        }

        root.Children.Add(new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        });
        Content = root;
    }

    private UIElement Block(ReportBlock block)
    {
        switch (block.Kind)
        {
            case ReportBlockKind.Heading:
                var heading = new TextBlock
                {
                    Text = block.Text,
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = block.Level switch { 1 => 26, 2 => 21, 3 => 17, _ => 15 },
                    Margin = new Thickness(0, block.Level <= 2 ? 18 : 12, 0, 6)
                };
                AutomationProperties.SetHeadingLevel(heading, block.Level switch
                {
                    1 => AutomationHeadingLevel.Level1,
                    2 => AutomationHeadingLevel.Level2,
                    3 => AutomationHeadingLevel.Level3,
                    4 => AutomationHeadingLevel.Level4,
                    5 => AutomationHeadingLevel.Level5,
                    _ => AutomationHeadingLevel.Level6
                });
                return heading;
            case ReportBlockKind.ListItem:
                return new TextBlock { Text = "\u2022 " + block.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 2, 0, 2) };
            case ReportBlockKind.Quote:
                return new Border
                {
                    BorderBrush = SystemColors.ActiveBorderBrush,
                    BorderThickness = new Thickness(4, 0, 0, 0),
                    Padding = new Thickness(10, 4, 0, 4),
                    Margin = new Thickness(0, 4, 0, 4),
                    Child = new TextBlock { Text = block.Text, TextWrapping = TextWrapping.Wrap }
                };
            case ReportBlockKind.Table:
                return Table(block);
            default:
                return new TextBlock { Text = block.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        }
    }

    // A paragraph's row of reference buttons, for the references with a
    // time in the recording; null when there are none.
    private WrapPanel? Buttons(IReadOnlyList<string> references)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var reference in references)
        {
            if (Button(reference) is { } button)
            {
                panel.Children.Add(button);
            }
        }

        if (panel.Children.Count == 0)
        {
            return null;
        }

        AutomationProperties.SetName(panel, "Evidence cited");
        return panel;
    }

    private Button? Button(string reference)
    {
        if (!_report.Evidence.TryGetValue(reference, out var entry) || entry.Nanoseconds is not { } time)
        {
            return null;
        }

        var button = new Button
        {
            Content = _report.Label(reference),
            MinWidth = 44,
            MinHeight = 44,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 8, 4),
            ToolTip = entry.Text
        };
        AutomationProperties.SetName(button, $"Go to {reference}, {entry.Time}");
        AutomationProperties.SetHelpText(button, entry.Text);
        button.Click += (_, _) =>
        {
            var problem = _seek(time);
            _status.Text = problem ?? $"The playhead is at {entry.Time}, {reference}: {entry.Text}";
        };
        return button;
    }

    // A table, with the evidence list's references as buttons.
    private Grid Table(ReportBlock block)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 10) };
        var columns = block.Rows.Count == 0 ? 0 : block.Rows.Max(row => row.Count);
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = column == columns - 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
            });
        }

        for (var row = 0; row < block.Rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var column = 0; column < block.Rows[row].Count; column++)
            {
                var text = block.Rows[row][column];
                UIElement cell = row > 0 && column == 0 && Button(text) is { } button
                    ? button
                    : new TextBlock
                    {
                        Text = text,
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = row == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                        Margin = new Thickness(0, 4, 12, 4),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }
}

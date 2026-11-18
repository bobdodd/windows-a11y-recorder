using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Recorder.Session;

namespace Recorder.App;

// The Checks window's Summary tab (owner, 2026-11-18: "The summary for a
// recording take most of the window even on full-screen and is unreadable
// text. The summary needs to be its own tab, and formatted for humans to
// read"). Each section has a heading, exposed as one to UI Automation, its
// result in bold on a line of its own, each count on a line of its own
// with a drawn bullet, and how the check judges after them. The sections
// are laid out again only when what they say changes, not as the playhead
// moves, so a selection in them holds. See android-checks.md, "The
// Summary tab".
public partial class MainWindow
{
    private PhoneSummarySection? _checksModelsSection;
    private string? _checksSummaryKey;

    private void RefreshChecksSummary()
    {
        var sections = new List<PhoneSummarySection>();
        if (_playbackArchive is not { PhoneScreens.Recorded: true } archive)
        {
            sections.Add(new PhoneSummarySection(
                "Recording",
                null,
                [_playbackArchive is null
                    ? "Open a recording of a phone to see a summary of its checks."
                    : "This recording holds no snapshot of a phone's app window, so it has no checks to summarise."]));
            if (_checksModelsSection is { } models)
            {
                sections.Add(models);
            }
        }
        else
        {
            var screens = archive.PhoneScreens;
            sections.Add(new PhoneSummarySection(
                "Recording",
                null,
                [$"{Count(screens.Apps.Count, "app")}, {Count(screens.Screens.Count, "screen")}, {Count(screens.Visits.Count, "visit")}."]));
            var checks = PhoneChecksSummary.Build(new PhoneChecksSummaryInput
            {
                Level = AuditLevel,
                Tree = _phoneChecks,
                TreeProblem = _phoneChecksProblem,
                Contrast = _phoneContrast,
                ContrastProblem = _phoneContrastProblem,
                TextContrast = _phoneTextContrast,
                NonTextContrast = _phoneNonTextContrast,
                ImagesOfText = _phoneImagesOfText,
                HeadingLook = _phoneHeadingLook,
                ImageAlternative = _phoneImageAlternative,
                DescribedImages = _phoneDescribedImages,
                ScreenTitles = _phoneScreenTitles,
                DecorativeImages = _phoneDecorativeImages,
                HeadingSections = _phoneHeadingSections,
                TextNotInTree = _phoneTextNotInTree,
                HeadingNotShown = _phoneHeadingNotShown,
                LabelInName = _phoneLabelInName,
            });

            // The overview and the audit level, then the project's models,
            // then each check.
            sections.AddRange(checks.Take(2));
            if (_checksModelsSection is { } models)
            {
                sections.Add(models);
            }

            sections.AddRange(checks.Skip(2));
        }

        var key = string.Join("\n", sections.Select(section =>
            $"{section.Heading}\t{section.Result}\t{string.Join("\t", section.Points)}\t{section.HowJudged}"));
        if (string.Equals(key, _checksSummaryKey, StringComparison.Ordinal))
        {
            return;
        }

        _checksSummaryKey = key;
        ShowChecksSummary(sections);
    }

    private void ShowChecksSummary(IReadOnlyList<PhoneSummarySection> sections)
    {
        ChecksSummaryPanel.Children.Clear();
        var bullet = ChecksTabControl.Foreground;
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            var heading = new SelectableText
            {
                Text = section.Heading,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold,
                FontSize = 17,
                Margin = new Thickness(0, index == 0 ? 0 : 20, 0, 4)
            };
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
            ChecksSummaryPanel.Children.Add(heading);
            if (section.Result is { } result)
            {
                ChecksSummaryPanel.Children.Add(new SelectableText
                {
                    Text = result + ".",
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            foreach (var point in section.Points)
            {
                ChecksSummaryPanel.Children.Add(Point(point, bullet));
            }

            if (section.HowJudged is { } how)
            {
                ChecksSummaryPanel.Children.Add(new SelectableText
                {
                    Text = how,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }
        }
    }

    // A count on a line of its own, after a drawn bullet: a shape, so a
    // screen reader does not say "bullet" before each, and it is not
    // copied with the text.
    private static Grid Point(string text, Brush bullet)
    {
        var line = new Grid { Margin = new Thickness(4, 2, 0, 2) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dot = new Ellipse
        {
            Width = 5,
            Height = 5,
            Fill = bullet,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 7, 10, 0)
        };
        var words = new SelectableText { Text = text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(words, 1);
        line.Children.Add(dot);
        line.Children.Add(words);
        return line;
    }
}

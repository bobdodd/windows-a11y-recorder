using System.Windows;
using Recorder.Analysis;

namespace Recorder.App;

// The Checks window's toast for "Ask the project's model" (owner,
// 2026-11-18: "We need toasts to say when the checking has started and
// when it is complete"; android-checks.md, "Toasts for the model checks").
// One toast at a time, at the foot of the window: a new one replaces the
// one shown, and it stays until Close is pressed, the next replaces it or
// another recording is opened. Its text is spoken by screen readers as it
// is shown, without moving focus to it.
public partial class MainWindow
{
    // The questions the rules leave that the project's models have not
    // judged, of every check that asks them.
    private int ModelQuestionsUnanswered =>
        _imagesOfTextUnanswered + _headingLookUnanswered + _imageAlternativeUnanswered + _describedImagesUnanswered +
        _titleVisionUnanswered + _decorativeUnanswered + _sectionVisionUnanswered + _textNotInTreeUnanswered +
        _headingNotShownUnanswered + _controlNamesUnanswered + _chartColoursUnanswered +
        _titleTextUnanswered + _sectionTextUnanswered;

    // A toast for each change of the model checks' tasks, in order, so the
    // last one is left shown.
    private void ShowModelChecksToasts(IEnumerable<ModelChecksChange> changes)
    {
        foreach (var change in changes)
        {
            ShowChecksToast(ModelChecksProgress.Message(change, ModelQuestionsUnanswered));
        }
    }

    private void ShowChecksToast(string message)
    {
        ChecksToastText.Text = message;
        ChecksToast.Visibility = Visibility.Visible;
        _busy.AnnounceAnalysis(message);
    }

    private void HideChecksToast()
    {
        ChecksToast.Visibility = Visibility.Collapsed;
        ChecksToastText.Text = string.Empty;
    }

    // Close hides the toast. Focus goes to "Ask the project's model"
    // where it can take it, else to the tab shown, so it is not lost with
    // the button that had it.
    private void ChecksToastCloseButton_Click(object sender, RoutedEventArgs e)
    {
        var hadFocus = ChecksToastCloseButton.IsKeyboardFocusWithin;
        HideChecksToast();
        if (hadFocus)
        {
            if (AskChecksModelButton.IsEnabled)
            {
                AskChecksModelButton.Focus();
            }
            else if (ChecksTabControl.SelectedItem is System.Windows.Controls.TabItem tab)
            {
                tab.Focus();
            }
        }
    }
}

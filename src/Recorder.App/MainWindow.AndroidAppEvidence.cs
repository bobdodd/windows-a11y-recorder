using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using Microsoft.Win32;
using Recorder.Session;

namespace Recorder.App;

public partial class MainWindow
{
    private void ShowAndroidAppEvidence(SessionPlaybackArchive? archive)
    {
        var packages = archive?.PhoneScreens.Apps.Select(app => app.Package).ToList() ?? [];
        if (archive?.Manifest.AndroidPreRecordingTextSizes is { } selected &&
            !packages.Contains(selected.PackageName, StringComparer.Ordinal))
        {
            packages.Add(selected.PackageName);
        }

        AppAccessibilityEvidence.Visibility = packages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AppAccessibilityDetails.IsExpanded = false;
        AppAccessibilityDetails.Visibility = Visibility.Collapsed;
        AppAccessibilityBody.Text = string.Empty;
        AppAccessibilityStatus.Text = string.Empty;
        AppAccessibilityStatus.Visibility = Visibility.Collapsed;
        if (archive is null || packages.Count == 0)
        {
            return;
        }

        var descriptions = new List<string>();
        var fontEvidence = AndroidTextSizeChecksEvidence.DescribeAppFonts(archive.Manifest);
        if (fontEvidence is not null)
        {
            descriptions.Add(fontEvidence);
        }
        var attached = 0;
        foreach (var package in packages)
        {
            try
            {
                if (AndroidAppAccessibilityReviews.Attached(archive, package) is { } review)
                {
                    attached++;
                    descriptions.Add($"{review.Package}, version {review.VersionName} (code {review.VersionCode}). " +
                        $"Reviewed {review.ReviewedUtc:yyyy-MM-dd} (UTC). Method: {review.Method}." +
                        Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine,
                            review.Observations.Select(item => $"{item.Topic}: {item.Evidence} Limit: {item.Limit}")));
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                descriptions.Add($"{package}: the attached review could not be read or did not match the recorded app version.");
            }
        }

        var reviewSummary = attached == 0
            ? "App accessibility review: no matching APK or source review attached to this recording."
            : $"App accessibility review: {attached} of {packages.Count} recorded apps have an attached review. " +
                "This is app-version evidence, not a measurement from a screen or playback time.";
        AppAccessibilitySummary.Text = (fontEvidence is null ? "App-wide APK font inventory: not collected. " :
            "App-wide APK font inventory: an attempt was saved before recording. ") + reviewSummary;
        AppAccessibilityDetails.Visibility = descriptions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AppAccessibilityBody.Text = string.Join(Environment.NewLine + Environment.NewLine, descriptions);
    }

    private void AttachAndroidAppReview_Click(object sender, RoutedEventArgs e)
    {
        if (_playbackArchive is not { } archive)
        {
            return;
        }

        var picker = new OpenFileDialog
        {
            Title = "Attach an app accessibility review to this recording",
            Filter = "App accessibility review (*.json)|*.json",
            CheckFileExists = true
        };
        if (picker.ShowDialog(Window.GetWindow(AppAccessibilityEvidence) ?? this) != true ||
            !ReferenceEquals(archive, _playbackArchive))
        {
            return;
        }

        try
        {
            var review = AndroidAppAccessibilityReviews.Read(picker.FileName);
            AndroidAppAccessibilityReviews.Attach(archive, review);
            ShowAndroidAppEvidence(archive);
            AppAccessibilityDetails.IsExpanded = true;
            SetAppReviewStatus("Review attached to this recording.");
        }
        catch (InvalidDataException error)
        {
            SetAppReviewStatus(error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            SetAppReviewStatus("The review could not be attached. Check the JSON file and recording folder. An existing review is not replaced.");
        }
    }

    private void SetAppReviewStatus(string message)
    {
        AppAccessibilityStatus.Visibility = Visibility.Visible;
        AppAccessibilityStatus.Text = message;
        var peer = UIElementAutomationPeer.FromElement(AppAccessibilityStatus) ??
            UIElementAutomationPeer.CreatePeerForElement(AppAccessibilityStatus);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void ShowRecordedFormEvidence(bool hasPhoneTrees, PhoneCheckRun? run, bool failed = false)
    {
        RecordedForms.Visibility = hasPhoneTrees ? Visibility.Visible : Visibility.Collapsed;
        if (run is null)
        {
            RecordedForms.IsExpanded = false;
        }
        var body = run is null ? (failed ? "Recorded trees could not be reviewed." : "Form evidence has not yet been read.") :
            run.WindowsChecked == 0 ? "No settled application tree was available to read." :
            run.FormEvidence.Count == 0 ? "No visible editable nodes were published in the settled application trees read." :
            "Published form evidence in settled application trees. Field values are not shown. A hint is not a label link." +
            Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine,
                run.FormEvidence.Select(form => $"{form.Visit.Package}, screen {form.Visit.Screen.Number}, visit {form.Visit.Number}, " +
                    $"tree captured at {form.TimeLabel}:" + Environment.NewLine +
                    string.Join(Environment.NewLine, form.Fields)));
        RecordedFormsBody.Text = body;
    }
}

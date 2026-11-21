using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Microsoft.Win32;
using Recorder.Session;

namespace Recorder.App;

public partial class MainWindow
{
    // Rebuild static evidence only when the open archive changes, never
    // while seeking, running models or navigating between the tabs.
    private void BindAndroidAppDetails(SessionPlaybackArchive? archive)
    {
        var available = AndroidAppDetailsEvidence.IsAvailable(archive);
        if (!available && ChecksAppDetailsTab.IsSelected)
        {
            ChecksTabControl.SelectedItem = ChecksFindingsTab;
        }

        ChecksAppDetailsTab.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        AppDetailsOverview.Children.Clear();
        AppDetailsApk.Children.Clear();
        AppDetailsSizes.Children.Clear();
        AppDetailsForms.Children.Clear();
        AppDetailsReviews.Children.Clear();
        AppAccessibilityStatus.Text = string.Empty;
        AppAccessibilityStatus.Visibility = Visibility.Collapsed;
        if (!available || archive is null)
        {
            return;
        }

        var packages = AndroidAppDetailsEvidence.Packages(archive);
        if (packages.Count == 0)
        {
            AddDetail(AppDetailsOverview, "No app package was identified in this recording.");
        }

        foreach (var package in packages)
        {
            AddDetail(AppDetailsOverview, package, AutomationHeadingLevel.Level3);
            AddDetail(AppDetailsOverview, $"App version at recording stop: {archive.PhoneDevice.DescribeVersion(package)}.");
            AddDetail(AppDetailsOverview, archive.PhoneDevice.DescribeFramework(package));
            if (archive.PhoneScreens.Apps.FirstOrDefault(app => app.Package == package) is { } app)
            {
                AddDetail(AppDetailsOverview,
                    $"{Count(app.Screens.Count, "screen")}, {Count(app.Screens.Sum(screen => screen.Visits.Count), "visit")}.");
            }
            else if (archive.Manifest.AndroidPreRecordingTextSizes?.PackageName == package)
            {
                AddDetail(AppDetailsOverview, "Selected for collection before recording. No recorded visits were identified for this app.");
            }
        }

        if (AndroidApkDetails.From(archive.Manifest) is { } apk)
        {
            AddDetail(AppDetailsApk, apk.Summary);
            AddDetail(AppDetailsApk, apk.Context);
            if (apk.Problem is { } problem)
            {
                AddDetail(AppDetailsApk, $"Incomplete: {problem}");
            }
            if (apk.Groups.Count == 0)
            {
                AddDetail(AppDetailsApk, "No APK accessibility entries were saved.");
            }
            foreach (var group in apk.Groups)
            {
                AddDetail(AppDetailsApk, $"{group.Topic}: {group.Source}", AutomationHeadingLevel.Level3);
                AddDetailLines(AppDetailsApk, group.Items);
            }
            if (apk.Coverage.Count > 0)
            {
                AddDetail(AppDetailsApk, "Readers and limits", AutomationHeadingLevel.Level3);
                AddDetailLines(AppDetailsApk, apk.Coverage);
            }
        }
        else
        {
            AddDetail(AppDetailsApk, "No APK accessibility inventory was saved in this recording.");
        }

        var sizes = AndroidTextSizeChecksEvidence.Describe(archive.Manifest, CultureInfo.CurrentCulture);
        AddDetail(AppDetailsSizes, sizes?.Summary ?? "No live text-size snapshot was saved in this recording.");
        if (sizes is { NodeLines.Count: > 0 })
        {
            AddDetail(AppDetailsSizes, "Accessibility nodes", AutomationHeadingLevel.Level3);
            AddDetailLines(AppDetailsSizes, sizes.NodeLines);
        }
        ShowRecordedFormEvidence(archive, null);
        ShowAndroidAppReviews(archive, packages);
        AppDetailsScrollViewer.ScrollToTop();
    }

    private void ShowAndroidAppReviews(SessionPlaybackArchive archive, IReadOnlyList<string> packages)
    {
        AppDetailsReviews.Children.Clear();
        var canAttach = packages.Any(package => archive.PhoneDevice.VersionOf(package) is { Found: true, VersionName.Length: > 0, VersionCode: not null });
        AttachAppReviewButton.IsEnabled = canAttach;
        AppDetailsReviewInstructions.Text = canAttach
            ? "Attach an APK or source review for a package and version saved in this recording. A review is not an observation from a recorded visit."
            : "A review cannot be attached because no complete package version was saved in this recording.";
        var attached = false;
        foreach (var package in packages)
        {
            try
            {
                if (AndroidAppAccessibilityReviews.Attached(archive, package) is { } review)
                {
                    attached = true;
                    AddDetail(AppDetailsReviews, $"{review.Package}, version {review.VersionName} (code {review.VersionCode})",
                        AutomationHeadingLevel.Level3);
                    AddDetail(AppDetailsReviews, FormattableString.Invariant($"Reviewed {review.ReviewedUtc.UtcDateTime:yyyy-MM-dd} (UTC). Method: {review.Method}."));
                    foreach (var item in review.Observations)
                    {
                        AddDetail(AppDetailsReviews, item.Topic, AutomationHeadingLevel.Level4);
                        AddDetail(AppDetailsReviews, item.Evidence);
                        AddDetail(AppDetailsReviews, $"Limit: {item.Limit}");
                    }
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                AddDetail(AppDetailsReviews, $"{package}: the attached review could not be read or did not match the recorded app version.");
            }
        }

        if (!attached)
        {
            AddDetail(AppDetailsReviews, "No matching review is attached to this recording.");
        }
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
        if (picker.ShowDialog(Window.GetWindow(AttachAppReviewButton) ?? this) != true ||
            !ReferenceEquals(archive, _playbackArchive))
        {
            return;
        }

        try
        {
            var review = AndroidAppAccessibilityReviews.Read(picker.FileName);
            AndroidAppAccessibilityReviews.Attach(archive, review);
            ShowAndroidAppReviews(archive, AndroidAppDetailsEvidence.Packages(archive));
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

    private void ShowRecordedFormEvidence(SessionPlaybackArchive archive, PhoneCheckRun? run, bool failed = false)
    {
        var forms = AndroidFormDetails.From(archive, run, failed);
        AppDetailsForms.Children.Clear();
        AddDetail(AppDetailsForms, forms.Summary);
        foreach (var app in forms.Apps)
        {
            AddDetail(AppDetailsForms, app.Package, AutomationHeadingLevel.Level3);
            foreach (var visit in app.Visits)
            {
                AddDetail(AppDetailsForms, $"Screen {visit.Screen}: {visit.ScreenLabel}. Visit {visit.Visit}.", AutomationHeadingLevel.Level4);
                foreach (var tree in visit.Trees)
                {
                    AddDetail(AppDetailsForms, $"Tree captured at {tree.TimeLabel}.");
                    AddDetailLines(AppDetailsForms, tree.Fields);
                }
            }
        }
    }

    private void JumpToAppDetailsSection_Click(object sender, RoutedEventArgs e)
    {
        // The panel moves to an owned window, outside the player's XAML namescope.
        var heading = sender switch
        {
            var button when button == AppDetailsOverviewJump => AppDetailsOverviewHeading,
            var button when button == AppDetailsApkJump => AppDetailsApkHeading,
            var button when button == AppDetailsSizesJump => AppDetailsSizesHeading,
            var button when button == AppDetailsFormsJump => AppDetailsFormsHeading,
            var button when button == AppDetailsReviewsJump => AppDetailsReviewsHeading,
            _ => null
        };
        if (heading is not null)
        {
            var top = heading.TranslatePoint(new Point(0, 0), AppDetailsPanel).Y;
            heading.Focus();
            AppDetailsScrollViewer.ScrollToVerticalOffset(top);
        }
    }

    private static void AddDetail(Panel panel, string text, AutomationHeadingLevel? level = null, double indent = 0)
    {
        var entry = new SelectableText
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(indent, level is not null ? 9 : 0, 0, 4)
        };
        if (level is { } heading)
        {
            AutomationProperties.SetHeadingLevel(entry, heading);
            entry.FontWeight = FontWeights.SemiBold;
        }
        panel.Children.Add(entry);
    }

    private static void AddDetailLines(Panel panel, IEnumerable<string> rows)
    {
        // Keep adjacent entries selectable together, but bound individual
        // text controls for recordings with thousands of APK entries.
        foreach (var chunk in rows.Chunk(64))
        {
            AddDetail(panel, string.Join(Environment.NewLine, chunk), indent: 12);
        }
    }
}

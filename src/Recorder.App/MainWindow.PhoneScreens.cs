using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Npgsql;
using Recorder.Analysis;
using Recorder.Analysis.Android;
using Recorder.Session;

namespace Recorder.App;

// The Checks tab (android-checks.md, "The player", steps 1 and 2): a phone
// recording's display density, then its apps with their versions, then
// each app's screens with how each was named, then each screen's visits
// with their states. A density, visit or state row moves the playhead to
// its start; a screen's or app's row to its first visit. The
// visit at the playhead is marked as playback moves, but the list never
// scrolls by itself, as the reading position steps do not.
//
// Step 3: the checks from the tree run in the background when the
// recording is opened, and each screen's findings follow its visits,
// grouped by check. A finding's row moves the playhead to the first time
// it was seen, shows the item in the "Accessibility tree" tab and outlines
// it on the phone's picture; "Previous time" and "Next time" step through
// the other times.
//
// Step 4c: the text contrast check runs in the background too, after the
// checks of the tree: the colours of each line of text measured on the
// phone's screenshot or the video's frame (step 4b), held to AA or AAA as
// chosen in the tab. Changing the level works the findings out again from
// the same measurement.
public partial class MainWindow
{
    private readonly ObservableCollection<PhoneScreenRowView> _phoneScreenRows = [];
    private PhoneScreenRowView? _currentVisitRow;
    private bool _phoneScreensBound;

    // The checks of the open recording: running, done, or failed.
    private SessionPlaybackArchive? _phoneChecksArchive;
    private CancellationTokenSource? _phoneChecksCancellation;
    private PhoneCheckRun? _phoneChecks;
    private string? _phoneChecksProblem;

    // The contrast measurement of the open recording, and its findings at
    // the chosen level.
    private PhoneContrastRun? _phoneContrast;
    private string? _phoneContrastProblem;
    private PhoneTextContrastRun? _phoneTextContrast;
    private PhoneNonTextContrastRun? _phoneNonTextContrast;
    private PhoneImagesOfTextRun? _phoneImagesOfText;

    // The images-of-text check as its rules left it, before any model's
    // answers, and its questions' requests, made once for it; the shown
    // run above has the project's model's kept answers given
    // (android-vision-checks.md, "Images of text, as built").
    private PhoneImagesOfTextRun? _phoneImagesOfTextRules;
    private (PhoneImagesOfTextRun Rules, IReadOnlyList<ImageOfTextRequest> Requests)? _imagesOfTextRequests;
    private int _imagesOfTextUnanswered;

    // The check of text shown as a heading, the same way: as its rules
    // left it, its requests, and the run shown with any kept answers
    // (android-checks.md, step 19).
    private PhoneHeadingLookRun? _phoneHeadingLook;
    private PhoneHeadingLookRun? _phoneHeadingLookRules;
    private (PhoneHeadingLookRun Rules, IReadOnlyList<HeadingLookRequest> Requests)? _headingLookRequests;
    private int _headingLookUnanswered;

    // The check of images with no text alternative, the same way
    // (android-checks.md, step 20); its kept answers are found by the
    // image's pixels, the text beside it and what a screen reader says on
    // the screen, in the project.
    private PhoneImageAlternativeRun? _phoneImageAlternative;
    private PhoneImageAlternativeRun? _phoneImageAlternativeRules;
    private (PhoneImageAlternativeRun Rules, IReadOnlyList<ImageAlternativeRequest> Requests)? _imageAlternativeRequests;
    private int _imageAlternativeUnanswered;
    private PhoneLabelInNameRun? _phoneLabelInName;
    private PhoneCheckSettings? _checkSettings;

    // The finding chosen in the list, and which of its times is shown.
    private PhoneFinding? _shownFinding;
    private int _shownFindingTime;

    // The level chosen last time, shown as the window opens, so it can be
    // seen and changed before a recording is open.
    private void LoadCheckSettings()
    {
        _checkSettings = PhoneCheckSettings.Load(PhoneCheckSettings.DefaultPath);
        AuditAaRadioButton.IsChecked = _checkSettings.AuditLevel == PhoneAuditLevel.AA;
        AuditAaaRadioButton.IsChecked = _checkSettings.AuditLevel == PhoneAuditLevel.AAA;
        OrientationHoldComboBox.ItemsSource = PhoneOrientationChecks.HoldChoices
            .Select(seconds => new OrientationHoldChoice(seconds))
            .ToList();
        OrientationHoldComboBox.SelectedValue = OrientationHoldSeconds;
    }

    // A choice of how long the phone must be held turned, as the list says it.
    private sealed record OrientationHoldChoice(int Seconds)
    {
        public string Text => Seconds == 1 ? "1 second" : string.Create(CultureInfo.CurrentCulture, $"{Seconds:N0} seconds");

        public override string ToString() => Text;
    }

    private void BindPhoneScreens()
    {
        if (!_phoneScreensBound)
        {
            PhoneScreensListBox.ItemsSource = _phoneScreenRows;
            _phoneScreensBound = true;
        }

        if (!ReferenceEquals(_phoneChecksArchive, _playbackArchive))
        {
            _phoneChecksCancellation?.Cancel();
            _phoneChecksCancellation?.Dispose();
            _phoneChecksCancellation = null;
            _phoneChecksArchive = _playbackArchive;
            _phoneChecks = null;
            _phoneChecksProblem = null;
            _phoneContrast = null;
            _phoneContrastProblem = null;
            _phoneTextContrast = null;
            _phoneNonTextContrast = null;
            _phoneImagesOfText = null;
            _phoneImagesOfTextRules = null;
            _imagesOfTextRequests = null;
            _imagesOfTextUnanswered = 0;
            _phoneHeadingLook = null;
            _phoneHeadingLookRules = null;
            _headingLookRequests = null;
            _headingLookUnanswered = 0;
            _phoneImageAlternative = null;
            _phoneImageAlternativeRules = null;
            _imageAlternativeRequests = null;
            _imageAlternativeUnanswered = 0;
            _phoneLabelInName = null;
            _shownFinding = null;
            if (_playbackArchive is { PhoneScreens.Recorded: true } opened)
            {
                var cancellation = new CancellationTokenSource();
                _phoneChecksCancellation = cancellation;
                _ = RunPhoneChecksAsync(opened, cancellation.Token);
            }
        }

        ShowPhoneScreenRows();
    }

    // Runs the checks from the tree away from the window's thread, and
    // lists their findings when they are done, if the recording is still
    // open.
    private async Task RunPhoneChecksAsync(SessionPlaybackArchive archive, CancellationToken token)
    {
        if (!await RunTreeChecksAsync(archive, token))
        {
            return;
        }

        await RunPhoneContrastAsync(archive, token);
    }

    // The tree's checks at the audit level, away from the window's thread;
    // false when the recording was closed meanwhile. When the level is
    // changed while they run, they are run again at the new level.
    private async Task<bool> RunTreeChecksAsync(SessionPlaybackArchive archive, CancellationToken token)
    {
        PhoneAuditLevel level;
        int hold;
        PhoneCheckRun? run;
        string? problem;
        do
        {
            (level, hold, run, problem) = (AuditLevel, OrientationHoldSeconds, null, null);
            try
            {
                var at = level;
                var held = hold;
                run = await Task.Run(
                    () => PhoneCheckRun.Run(archive.PhoneScreens, archive.PhoneAccessibility, archive.PhoneDevice, archive.RecordSource, PhoneSnapshotPictures.Of(archive), token, at,
                        archive.PhoneTouches, archive.PhoneAnnouncements, archive.PhoneSettings, PhonePictures.Load, archive.PhoneInputs,
                        archive.PhoneOrientation, held),
                    token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or InvalidOperationException or
                    JsonException or UnauthorizedAccessException or DbException)
            {
                problem = exception.Message;
            }

            if (!ReferenceEquals(_playbackArchive, archive) || token.IsCancellationRequested)
            {
                return false;
            }
        }
        while (level != AuditLevel || hold != OrientationHoldSeconds);

        _phoneChecks = run;
        _phoneChecksProblem = problem;
        ShowPhoneScreenRows();
        return true;
    }

    // Measures the colours of the text on each settled snapshot's picture,
    // with Windows' OCR finding the text (android-checks.md, steps 4b and
    // 4c), away from the window's thread.
    private async Task RunPhoneContrastAsync(SessionPlaybackArchive archive, CancellationToken token)
    {
        PhoneContrastRun? run = null;
        string? problem = null;
        try
        {
            run = await Task.Run(
                () => PhoneContrastRun.Run(archive, PhonePictures.Load, new WindowsPhoneTextReader(), token),
                token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException or
                JsonException or UnauthorizedAccessException or DbException)
        {
            problem = exception.Message;
        }

        if (!ReferenceEquals(_playbackArchive, archive) || token.IsCancellationRequested)
        {
            return;
        }

        _phoneContrast = run;
        _phoneContrastProblem = problem;

        // Text shown as a heading is level A, so the same at both audit
        // levels; its kept answers are given with the images' below.
        _phoneHeadingLookRules = run is null ? null : PhoneHeadingLookRun.Run(run, archive.PhoneScreens);
        _phoneHeadingLook = _phoneHeadingLookRules;
        _headingLookUnanswered = _phoneHeadingLookRules?.Questions.Count ?? 0;

        // An image with no text alternative is level A, so the same at both
        // audit levels; its kept answers are given with the images' below.
        _phoneImageAlternativeRules = run is null
            ? null
            : PhoneImageAlternativeRun.Run(run, archive.PhoneScreens, archive.PhoneDevice, archive.TalkBackSpeech, archive.PhoneAccessibility);
        _phoneImageAlternative = _phoneImageAlternativeRules;
        _imageAlternativeUnanswered = _phoneImageAlternativeRules?.Questions.Count ?? 0;
        JudgeTextContrast();

        // Non-text contrast has one level, AA (WCAG 2.2's 1.4.11), so is
        // judged once, not again when the text's level changes.
        _phoneNonTextContrast = run is null ? null : PhoneNonTextContrastRun.Run(run, archive.PhoneScreens);

        // Label in Name is level A, so the same at both audit levels.
        _phoneLabelInName = run is null ? null : PhoneLabelInNameRun.Run(run, archive.PhoneScreens);
        ShowPhoneScreenRows();
    }

    // The text contrast findings at the chosen level, from the measurement,
    // and the images of text, which add WCAG 1.4.9's findings at AAA.
    private void JudgeTextContrast()
    {
        if (_phoneContrast is not { } measured || _playbackArchive is not { } archive)
        {
            _phoneTextContrast = null;
            _phoneImagesOfText = null;
            _phoneImagesOfTextRules = null;
            UpdateAskChecksModelButton();
            return;
        }

        _phoneTextContrast = PhoneTextContrastRun.Run(measured, archive.PhoneScreens, archive.PhoneDevice, AuditLevel);
        _phoneImagesOfTextRules = PhoneImagesOfTextRun.Run(
            measured, archive.PhoneScreens, archive.PhoneDevice, AuditLevel, archive.TalkBackSpeech, archive.PhoneAccessibility);
        _phoneImagesOfText = _phoneImagesOfTextRules;
        _imagesOfTextUnanswered = _phoneImagesOfTextRules.Questions.Count;
        _ = ApplyKeptAnswersAsync();
    }

    // Gives the images-of-text check, the check of text shown as a heading
    // and the check of images with no text alternative the answers the
    // project's model for vision-based checks has given to their
    // questions, in any task, as they are kept by their requests, or for
    // an image with no text alternative by its pixels, the text beside it
    // and what a screen reader says on the screen, in the project; the model is not asked here. With no model chosen, what the
    // rules leave stays a warning, and the summary says why.
    private async Task ApplyKeptAnswersAsync()
    {
        await ApplyKeptImageAnswersAsync();
        await ApplyKeptHeadingAnswersAsync();
        await ApplyKeptImageAlternativeAnswersAsync();
    }

    private async Task ApplyKeptImageAnswersAsync()
    {
        if (_phoneImagesOfTextRules is not { } rules)
        {
            UpdateAskChecksModelButton();
            return;
        }

        if (rules.Questions.Count == 0)
        {
            _imagesOfTextUnanswered = 0;
            UpdateAskChecksModelButton();
            return;
        }

        var name = _openProjectModels?.VisionChecks;
        var problem = name is null
            ? "the project's settings choose no model for vision-based checks"
            : ProjectModels.Problem(ProjectChoices, ModelUses.VisionChecks, name);
        PhoneImagesOfTextRun shown;
        if (problem is not null || AnalysisCatalogOrNull is not { } catalog || ProjectChoices.Profile(name!) is not { } profile)
        {
            shown = rules.Judged(
                new Dictionary<PhoneImageOfTextQuestion, PhoneInference>(),
                AndroidImagesOfText.NotAsked(rules.Questions.Count, problem ?? "the analysis catalog is not open"));
        }
        else
        {
            try
            {
                if (_imagesOfTextRequests is not { } made || !ReferenceEquals(made.Rules, rules))
                {
                    var requests = await Task.Run(() => AndroidImagesOfText.Requests(rules, PhonePictures.Load));
                    made = (rules, requests);
                    _imagesOfTextRequests = made;
                }

                var keys = made.Requests.SelectMany(request => new[] { request.StepKey(1), request.StepKey(2) }).ToList();
                var kept = await catalog.AcceptedAnswersAsync(keys, name!, profile.Model, CancellationToken.None);
                var (complexKept, complexName) = await KeptComplexAnswersAsync(catalog, made.Requests.Select(request => (Func<int, string>)request.StepKey));
                shown = AndroidImagesOfText.FromKept(rules, made.Requests, kept, name!, complexKept, complexName);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                shown = rules.Judged(new Dictionary<PhoneImageOfTextQuestion, PhoneInference>(), $"the model's kept answers could not be read: {exception.Message}");
            }
        }

        // The recording or the level changed while the answers were read.
        if (!ReferenceEquals(rules, _phoneImagesOfTextRules))
        {
            return;
        }

        _phoneImagesOfText = shown;
        _imagesOfTextUnanswered = shown.Findings.Count(finding => finding.Check == PhoneCheckNames.ImageOfText && shown.QuestionOf(finding) is not null);
        if (problem is not null)
        {
            _imagesOfTextUnanswered = 0;
        }

        UpdateAskChecksModelButton();
        ShowPhoneScreenRows();
    }

    private async Task ApplyKeptHeadingAnswersAsync()
    {
        if (_phoneHeadingLookRules is not { } rules)
        {
            UpdateAskChecksModelButton();
            return;
        }

        if (rules.Questions.Count == 0)
        {
            _headingLookUnanswered = 0;
            UpdateAskChecksModelButton();
            return;
        }

        var name = _openProjectModels?.VisionChecks;
        var problem = name is null
            ? "the project's settings choose no model for vision-based checks"
            : ProjectModels.Problem(ProjectChoices, ModelUses.VisionChecks, name);
        PhoneHeadingLookRun shown;
        if (problem is not null || AnalysisCatalogOrNull is not { } catalog || ProjectChoices.Profile(name!) is not { } profile)
        {
            shown = rules.Judged(
                new Dictionary<PhoneHeadingQuestion, PhoneInference>(),
                AndroidHeadingLook.NotAsked(rules.Questions.Count, problem ?? "the analysis catalog is not open"));
        }
        else
        {
            try
            {
                if (_headingLookRequests is not { } made || !ReferenceEquals(made.Rules, rules))
                {
                    var requests = await Task.Run(() => AndroidHeadingLook.Requests(rules, PhonePictures.Load));
                    made = (rules, requests);
                    _headingLookRequests = made;
                }

                var keys = made.Requests.SelectMany(request => new[] { request.StepKey(1), request.StepKey(2) }).ToList();
                var kept = await catalog.AcceptedAnswersAsync(keys, name!, profile.Model, CancellationToken.None);
                var (complexKept, complexName) = await KeptComplexAnswersAsync(catalog, made.Requests.Select(request => (Func<int, string>)request.StepKey));
                shown = AndroidHeadingLook.FromKept(rules, made.Requests, kept, name!, complexKept, complexName);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                shown = rules.Judged(new Dictionary<PhoneHeadingQuestion, PhoneInference>(), $"the model's kept answers could not be read: {exception.Message}");
            }
        }

        // The recording changed while the answers were read.
        if (!ReferenceEquals(rules, _phoneHeadingLookRules))
        {
            return;
        }

        _phoneHeadingLook = shown;
        _headingLookUnanswered = problem is not null ? 0 : shown.Findings.Count(finding => shown.QuestionOf(finding) is not null);
        UpdateAskChecksModelButton();
        ShowPhoneScreenRows();
    }

    private async Task ApplyKeptImageAlternativeAnswersAsync()
    {
        if (_phoneImageAlternativeRules is not { } rules)
        {
            UpdateAskChecksModelButton();
            return;
        }

        if (rules.Questions.Count == 0)
        {
            _imageAlternativeUnanswered = 0;
            UpdateAskChecksModelButton();
            return;
        }

        var name = _openProjectModels?.VisionChecks;
        var problem = name is null
            ? "the project's settings choose no model for vision-based checks"
            : ProjectModels.Problem(ProjectChoices, ModelUses.VisionChecks, name);
        PhoneImageAlternativeRun shown;
        if (problem is not null || AnalysisCatalogOrNull is not { } catalog || ProjectChoices.Profile(name!) is not { } profile)
        {
            shown = rules.Judged(
                new Dictionary<PhoneImageAlternativeQuestion, PhoneInference>(),
                AndroidImageAlternative.NotAsked(rules.Questions.Count, problem ?? "the analysis catalog is not open"));
        }
        else
        {
            try
            {
                if (_imageAlternativeRequests is not { } made || !ReferenceEquals(made.Rules, rules))
                {
                    var requests = await Task.Run(() => AndroidImageAlternative.Requests(rules, PhonePictures.Load));
                    made = (rules, requests);
                    _imageAlternativeRequests = made;
                }

                var signed = await catalog.SignedAnswersAsync(
                    AndroidImageAlternative.Template, AndroidImageAlternative.TemplateVersion, name!, profile.Model,
                    _openProjectId, _analysisRecordingId, CancellationToken.None);
                IReadOnlyList<SignedAnswer>? complexSigned = null;
                var complex = ComplexVisionModel();
                if (complex is not null)
                {
                    complexSigned = await catalog.SignedAnswersAsync(
                        AndroidImageAlternative.Template, AndroidImageAlternative.TemplateVersion, complex.Value.Name, complex.Value.Profile.Model,
                        _openProjectId, _analysisRecordingId, CancellationToken.None);
                }

                shown = AndroidImageAlternative.FromKept(rules, made.Requests, signed, name!, complexSigned, complex?.Name);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                shown = rules.Judged(new Dictionary<PhoneImageAlternativeQuestion, PhoneInference>(), $"the model's kept answers could not be read: {exception.Message}");
            }
        }

        // The recording changed while the answers were read.
        if (!ReferenceEquals(rules, _phoneImageAlternativeRules))
        {
            return;
        }

        _phoneImageAlternative = shown;
        _imageAlternativeUnanswered = problem is not null ? 0 : shown.Findings.Count(finding => shown.QuestionOf(finding) is not null);
        UpdateAskChecksModelButton();
        ShowPhoneScreenRows();
    }

    // The project's model for complex questions of vision-based checks,
    // where a question the model for simple ones does not settle is asked
    // again of another (projects.md, "Simple and complex questions"), and
    // its profile; null where there is none the project can use.
    private (string Name, ModelProfile Profile)? ComplexVisionModel() =>
        _openProjectModels is { } models &&
        CheckQuestions.EscalationProfile(models, ModelUses.VisionChecks) is { } name &&
        ProjectModels.Problem(ProjectChoices, ModelUses.ComplexVisionChecks, name) is null &&
        ProjectChoices.Profile(name) is { } profile
            ? (name, profile)
            : null;

    // The answers the model for complex questions gave to the questions
    // asked again, runs 3 and 4, and its name; none where there is no such
    // model.
    private async Task<(IReadOnlyDictionary<string, KeptAnswer>? Kept, string? Name)> KeptComplexAnswersAsync(AnalysisCatalog catalog, IEnumerable<Func<int, string>> stepKeys)
    {
        if (ComplexVisionModel() is not { } complex)
        {
            return (null, null);
        }

        var keys = stepKeys.SelectMany(key => new[] { key(CheckQuestions.EscalatedRun), key(CheckQuestions.EscalatedRun + 1) }).ToList();
        return (await catalog.AcceptedAnswersAsync(keys, complex.Name, complex.Profile.Model, CancellationToken.None), complex.Name);
    }

    // "Ask the project's model" is for images and text the rules leave that
    // the project's model for vision-based checks has not answered, while no
    // model checks of the recording are queued or running, and only where
    // the project can use that model and, where another is chosen for
    // complex questions, that one too (owner, 2026-11-17).
    private void UpdateAskChecksModelButton()
    {
        var waiting = _analysisTasks?.Any(task => task.Kind == AndroidModelChecksKind.Name &&
            task.State is AnalysisTaskStates.Queued or AnalysisTaskStates.Running) == true;
        AskChecksModelButton.IsEnabled = _imagesOfTextUnanswered + _headingLookUnanswered + _imageAlternativeUnanswered > 0 && !waiting && _analysisRecordingId is not null &&
            _openProjectModels is { VisionChecks: not null } models && models.ChecksProblem(ProjectChoices, ModelUses.VisionChecks) is null;
    }

    private async void AskChecksModelButton_Click(object sender, RoutedEventArgs e)
    {
        AskChecksModelButton.IsEnabled = false;
        await QueueAsync(AndroidModelChecksKind.Name);
        UpdateAskChecksModelButton();
    }

    private PhoneAuditLevel AuditLevel => _checkSettings?.AuditLevel ?? PhoneAuditLevel.AA;

    private int OrientationHoldSeconds => _checkSettings?.OrientationHoldSeconds ?? PhoneOrientationChecks.DefaultHoldSeconds;

    // How long the phone must be held turned for the orientation check
    // (owner, 2026-11-13, a setting): changing it runs the tree's checks
    // again, as the audit level does.
    private void OrientationHoldComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_checkSettings is null || OrientationHoldComboBox.SelectedValue is not int seconds || seconds == OrientationHoldSeconds)
        {
            return;
        }

        _checkSettings = _checkSettings with { OrientationHoldSeconds = seconds };
        _checkSettings.TrySave(PhoneCheckSettings.DefaultPath);
        RerunTreeChecks();
    }

    // The audit level guides the checks (owner, 2026-11-12): changing it
    // works the text contrast findings out again from the same
    // measurement, and runs the tree's checks again, as WCAG 2.5.5's runs
    // only at AAA. The chosen finding goes, with its outline and its time
    // buttons, as the findings are new.
    private void AuditLevelRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        // Setting the buttons from the saved level, as the window opens.
        if (_checkSettings is null)
        {
            return;
        }

        var level = ReferenceEquals(sender, AuditAaaRadioButton) ? PhoneAuditLevel.AAA : PhoneAuditLevel.AA;
        if (level == AuditLevel)
        {
            return;
        }

        _checkSettings = (_checkSettings ?? PhoneCheckSettings.Default) with { AuditLevel = level };
        _checkSettings.TrySave(PhoneCheckSettings.DefaultPath);
        if (!_phoneScreensBound)
        {
            return;
        }

        _shownFinding = null;
        JudgeTextContrast();
        RerunTreeChecks();
    }

    // Runs the tree's checks again with the settings now chosen, if they
    // have run or failed for the open recording. The chosen finding goes,
    // with its outline and its time buttons, as the findings are new.
    private void RerunTreeChecks()
    {
        if (!_phoneScreensBound)
        {
            return;
        }

        _shownFinding = null;
        var rerun = _playbackArchive is { PhoneScreens.Recorded: true } archive &&
            ReferenceEquals(_phoneChecksArchive, archive) &&
            _phoneChecksCancellation is { } cancellation &&
            (_phoneChecks is not null || _phoneChecksProblem is not null);
        if (rerun)
        {
            _phoneChecks = null;
            _phoneChecksProblem = null;
        }

        ShowPhoneScreenRows();
        ShowFindingButtons();
        DisplayTouchesAt(_playbackPositionNanoseconds);
        if (rerun)
        {
            _ = RunTreeChecksAsync(_playbackArchive!, _phoneChecksCancellation!.Token);
        }
    }

    private void ShowPhoneScreenRows()
    {
        _phoneScreenRows.Clear();
        _currentVisitRow = null;
        var screens = _playbackArchive?.PhoneScreens ?? PhoneScreenTimeline.Empty;
        var device = _playbackArchive?.PhoneDevice ?? PhoneDeviceTimeline.Empty;
        // A visit's rows go to its first settled snapshot's picture, as
        // the video can lag the tree (PhoneSnapshotPictures).
        var pictures = _playbackArchive is { } archive ? PhoneSnapshotPictures.Of(archive) : PhoneSnapshotPictures.Empty;
        TimelineControl.SetPhoneVisits(screens.Visits);

        // The density first (android-checks.md, step 2): the checks turn
        // pixels into dp with it.
        if (screens.Recorded || device.Densities.Count > 0)
        {
            if (device.Densities.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(
                    0,
                    "Phone density not recorded: the recording was made before the recorder read it.",
                    0,
                    null));
            }

            foreach (var period in device.Densities)
            {
                var source = period.Source == PhoneDensitySource.Physical
                    ? "the phone's own density"
                    : "set by Display size";
                var foundAtStop = period.FoundAtStop ? ", found only by the read at the stop" : string.Empty;
                _phoneScreenRows.Add(new PhoneScreenRowView(
                    0,
                    string.Create(
                        CultureInfo.CurrentCulture,
                        $"Phone density {period.Dpi:N0} dpi, so 1 dp is {period.PixelsPerDp:0.###} pixels, {source}{foundAtStop}, {period.When}"),
                    period.Start,
                    null));
            }
        }

        foreach (var app in screens.Apps)
        {
            var visits = app.Screens.Sum(screen => screen.Visits.Count);
            _phoneScreenRows.Add(new PhoneScreenRowView(
                0,
                $"App {app.Package}, {device.DescribeVersion(app.Package)}: {Count(app.Screens.Count, "screen")}, {Count(visits, "visit")}",
                pictures.TimeOf(app.Screens[0].Visits[0]),
                null));
            _phoneScreenRows.Add(new PhoneScreenRowView(
                1,
                device.DescribeFramework(app.Package),
                pictures.TimeOf(app.Screens[0].Visits[0]),
                null) { IsMetadata = true });

            // Step 12's warning for an app with no live region in the whole
            // recording, under the app, as it is about all its visits.
            if (_phoneChecks is { } appRun && appRun.AppFindings.Where(finding => finding.Visit.Package == app.Package).ToList() is { Count: > 0 } ofApp)
            {
                AddCheckRows(ofApp, indent: 1);
            }

            foreach (var screen in app.Screens)
            {
                var activity = screen.Activity is null
                    ? "activity not given by a window-state event"
                    : "activity " + PhoneScreen.ShortClass(screen.Activity);
                _phoneScreenRows.Add(new PhoneScreenRowView(
                    1,
                    $"Screen {screen.Number}: {screen.Label}, {screen.NamedBy}, {activity}, {Count(screen.Visits.Count, "visit")}",
                    pictures.TimeOf(screen.Visits[0]),
                    null));
                foreach (var visit in screen.Visits)
                {
                    var settled = visit.SettledSnapshots.Count;
                    var shownAt = pictures.TimeOf(visit);
                    _phoneScreenRows.Add(new PhoneScreenRowView(
                        2,
                        $"Visit {visit.Number}, {visit.When}, started by {visit.CauseText}{(visit.IsReturn ? ", a return" : string.Empty)}; " +
                        $"{Count(visit.Snapshots.Count, "snapshot")}, {settled.ToString("N0", CultureInfo.CurrentCulture)} settled",
                        shownAt,
                        visit));
                    foreach (var state in visit.States)
                    {
                        var from = state.Package is null ? string.Empty : $", from {state.Package}";
                        var named = state.NamedBy == PhoneTitleSource.Untitled ? ", untitled" : $" '{state.Name}'";
                        var shows = state.FixedText is { } fixedText && fixedText != state.Name
                            ? $", first fixed text '{fixedText}'"
                            : string.Empty;
                        _phoneScreenRows.Add(new PhoneScreenRowView(
                            3,
                            $"In {state.Kind}{named}{shows}{from}, {state.When}",
                            state.Start,
                            null));
                    }

                    AddFindingRows(visit, shownAt);
                }
            }
        }

        ShowCurrentVisitButton.IsEnabled = screens.Recorded;
        ShowFindingButtons();
        DisplayPhoneVisitAt(_playbackPositionNanoseconds);
    }

    // A visit's findings, after its row and its states (owner,
    // 2026-11-12: the content can change between visits): a row for each
    // check that found something, saying how many at each level, then a
    // row for each finding. A visit with no settled snapshot is not
    // checked, as both checks run only on settled snapshots.
    private void AddFindingRows(PhoneVisit visit, long shownAt)
    {
        if (visit.SettledSnapshots.Count == 0)
        {
            if (_phoneChecks is not null || _phoneTextContrast is not null || _phoneNonTextContrast is not null || _phoneImagesOfText is not null ||
                _phoneLabelInName is not null || _phoneHeadingLook is not null || _phoneImageAlternative is not null)
            {
                // The status message check follows every snapshot, settled
                // or not, so can find a message in a visit too brief to
                // settle, as the client's loading message was (measured).
                _phoneScreenRows.Add(new PhoneScreenRowView(3, "Not checked, as no snapshot of this visit settled, except for status messages, which are looked for in every snapshot.", shownAt, null)
                {
                    IsCheck = true,
                });
                if (_phoneChecks?.For(visit) is { Count: > 0 } brief)
                {
                    AddCheckRows(brief);
                }
            }

            return;
        }

        if (_phoneChecks is { } run)
        {
            var findings = run.For(visit);
            if (findings.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, "No findings from the checks of the tree in this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings);
            }
        }

        if (_phoneTextContrast is { } contrast)
        {
            var findings = contrast.For(visit);
            var title = PhoneCheckNames.Title(contrast.Check);
            var lines = contrast.LinesIn(visit);
            if (lines == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no text was measured in this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else if (findings.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no findings, from {Count(lines, "line")} measured.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings, $", from {Count(lines, "line")} measured");
            }
        }

        if (_phoneNonTextContrast is { } nonText)
        {
            var findings = nonText.For(visit);
            var title = PhoneCheckNames.Title(PhoneCheckNames.NonTextContrast);
            var controls = nonText.ControlsIn(visit);
            if (controls == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no control was measured in this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else if (findings.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no findings, from {Count(controls, "control")} measured.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings, $", from {Count(controls, "control")} measured");
            }
        }

        // Images of text need OCR's words, so say nothing of a recording OCR
        // did not read; the summary says why.
        if (_phoneImagesOfText is { SnapshotsRead: > 0 } images)
        {
            var findings = images.For(visit);
            var title = PhoneCheckNames.Title(PhoneCheckNames.ImageOfText);
            var count = images.ImagesIn(visit);
            if (!images.Read(visit))
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: not checked, as OCR read no picture of this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else if (count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no image in this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else if (findings.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: no findings, from {Count(count, "image")} read by OCR.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings, $", from {Count(count, "image")} read by OCR");
            }
        }

        if (_phoneHeadingLook is { SnapshotsRead: > 0 } headings)
        {
            var findings = headings.For(visit);
            var title = PhoneCheckNames.Title(PhoneCheckNames.TextShownAsHeading);
            var count = headings.ItemsIn(visit);
            var text = !headings.Read(visit)
                ? $"{title}: not checked, as OCR read no picture of this visit."
                : count == 0
                    ? $"{title}: no text placed by OCR in this visit."
                    : findings.Count == 0
                        ? $"{title}: no findings, from {Count(count, "piece")} of text placed by OCR."
                        : null;
            if (text is not null)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, text, shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings, $", from {Count(count, "piece")} of text placed by OCR");
            }
        }

        if (_phoneImageAlternative is { SnapshotsRead: > 0 } alternatives)
        {
            var findings = alternatives.For(visit);
            var title = PhoneCheckNames.Title(PhoneCheckNames.ImageNoTextAlternative);
            var count = alternatives.ImagesIn(visit);
            var text = !alternatives.Read(visit)
                ? $"{title}: not checked, as OCR read no picture of this visit."
                : count == 0
                    ? $"{title}: no images in this visit."
                    : findings.Count == 0
                        ? $"{title}: no findings, from {Count(count, "image")}."
                        : null;
            if (text is not null)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, text, shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings, $", from {Count(count, "image")}");
            }
        }

        if (_phoneLabelInName is { } labels)
        {
            var findings = labels.For(visit);
            var controls = labels.ControlsIn(visit);
            if (controls == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{PhoneLabelInNameRun.Title}: no control with a label and a content description in this visit.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else if (findings.Count == 0)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{PhoneLabelInNameRun.Title}: no findings, from {Count(controls, "control")} checked.", shownAt, null)
                {
                    IsCheck = true,
                });
            }
            else
            {
                AddCheckRows(findings, $", from {Count(controls, "control")} checked");
            }
        }
    }

    // A row for each check of the findings, saying how many at each level,
    // then a row for each finding.
    private void AddCheckRows(IReadOnlyList<PhoneFinding> findings, string measured = "", int indent = 3)
    {
        foreach (var check in findings.GroupBy(finding => finding.Check))
        {
            _phoneScreenRows.Add(new PhoneScreenRowView(
                indent,
                $"{PhoneCheckNames.Title(check.Key)}: {Levels(check)}{measured}",
                check.First().Times[0].Time,
                null)
            {
                IsCheck = true,
            });
            foreach (var finding in check)
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(indent + 1, finding.Text, finding.Times[0].Time, null)
                {
                    Finding = finding,
                });
            }
        }
    }

    // How many findings at each level: "6 errors, 2 not run".
    private static string Levels(IEnumerable<PhoneFinding> findings) =>
        string.Join(", ", findings
            .GroupBy(finding => finding.Level)
            .OrderBy(level => level.Key)
            .Select(level => level.Key is PhoneCheckLevel.NotRun or PhoneCheckLevel.Info
                ? string.Create(CultureInfo.CurrentCulture, $"{level.Count():N0} {PhoneCheckNames.Word(level.Key)}")
                : Count(level.Count(), PhoneCheckNames.Word(level.Key))));

    private void DisplayPhoneVisitAt(long positionNanoseconds)
    {
        if (!_phoneScreensBound)
        {
            return;
        }

        if (_playbackArchive is not { PhoneScreens.Recorded: true } archive)
        {
            ChecksStatusTextBlock.Text = _playbackArchive is null
                ? "Open a recording of a phone to list its apps, screens and visits."
                : "This recording holds no snapshot of a phone's app window, so it has no screens to list.";
            return;
        }

        var visit = archive.PhoneScreens.VisitAt(positionNanoseconds);
        var current = visit is null ? null : _phoneScreenRows.FirstOrDefault(row => ReferenceEquals(row.Visit, visit));
        if (!ReferenceEquals(current, _currentVisitRow))
        {
            if (_currentVisitRow is not null)
            {
                _currentVisitRow.IsCurrent = false;
            }

            if (current is not null)
            {
                current.IsCurrent = true;
            }

            _currentVisitRow = current;
        }

        var screens = archive.PhoneScreens;
        var totals = $"{Count(screens.Apps.Count, "app")}, {Count(screens.Screens.Count, "screen")}, {Count(screens.Visits.Count, "visit")}.";
        ChecksStatusTextBlock.Text = (visit is null
            ? $"{totals} Before the first visit."
            : $"{totals} Visit {visit.Number}, to {visit.Screen.Label}, at the playhead.") + " " + AuditSummary() + " " + ChecksSummary();
    }

    // The audit level the checks were run at, and what it changes
    // (owner, 2026-11-12: the level guides the checks and their reporting).
    private string AuditSummary() => AuditLevel == PhoneAuditLevel.AAA
        ? "Audit level AAA: text contrast is held to WCAG 1.4.6, targets to 44 by 44 dp (WCAG 2.5.5) as well as 2.5.8, and images of text to 1.4.9 as well as 1.4.5. " + TargetExceptions + " 2.5.5's exceptions for a target sized by Android and an essential presentation are not judged."
        : "Audit level AA: text contrast is held to WCAG 1.4.3, targets to 2.5.8, and images of text to 1.4.5. " + TargetExceptions;

    // The target-size exceptions judged from the tree (owner, 2026-11-16).
    private const string NonTextRules =
        "A text field needs its border, bottom line or background at 3:1 whatever text it shows, and text OCR read counts as a control's only when a word of it is in the item's text in the tree.";

    private const string TargetExceptions =
        "A target on a line of text passes as one in a sentence, and one with an equivalent target of the size and the same name passes, inferred from the name; " +
        "each is information for a person to judge. A target inside or around another target is a warning, as they are one target only if they do the same thing, which the tree does not say.";

    // What the checks found, or why they have not.
    private string ChecksSummary()
    {
        if (_phoneChecksProblem is { } problem)
        {
            return $"The checks of the tree could not be run: {problem} {ContrastSummary()}";
        }

        if (_phoneChecks is not { } run)
        {
            return "Running the checks of the tree.";
        }

        var findings = run.Findings.Count == 0
            ? "no findings"
            : $"{Count(run.Findings.Count, "finding")} ({Levels(run.Findings)})";
        var summary = string.Create(
            CultureInfo.CurrentCulture,
            $"Checks of the tree: {findings}, from {Count(run.SnapshotsChecked, "settled snapshot")}, in {run.Elapsed.TotalSeconds:0.0} seconds.");
        var unfocusable = run.Findings.Where(finding => finding.Check == PhoneCheckNames.ClickableNotFocusable).ToList();
        var unfocusableFindings = unfocusable.Count == 0
            ? "no findings"
            : $"{Count(unfocusable.Count, "finding")} ({Levels(unfocusable)})";
        summary += string.Create(
            CultureInfo.CurrentCulture,
            $" {PhoneCheckNames.Title(PhoneCheckNames.ClickableNotFocusable)}, one of them: {unfocusableFindings}, from {Count(run.Clickable, "clickable item")} checked, {run.ClickableSetAside:N0} set aside inside a focusable clickable item; each is a warning, until a keyboard test confirms it.");
        var fieldChecks = new HashSet<string>(StringComparer.Ordinal)
        {
            PhoneCheckNames.FieldNoLabel, PhoneCheckNames.FieldNoName, PhoneCheckNames.InvalidNoError,
            PhoneCheckNames.ExpandableNoState, PhoneCheckNames.SliderNoRange,
        };
        var fieldFound = run.Findings.Where(finding => fieldChecks.Contains(finding.Check)).ToList();
        var fieldFindings = fieldFound.Count == 0
            ? "no findings"
            : $"{Count(fieldFound.Count, "finding")} ({Levels(fieldFound)})";
        summary += string.Create(
            CultureInfo.CurrentCulture,
            $" Fields and states, five of them: {fieldFindings}, from {Count(run.Fields, "field")}, {run.Invalid:N0} marked invalid, {Count(run.Expandable, "expandable item")} and {Count(run.Sliders, "slider")} checked; a field's label is only an item linked to it as its label (labeledBy), as a hint is not shown once text is typed.");
        if (run.ExpandableNotRecorded > 0)
        {
            summary += $" The expanded state of {Count(run.ExpandableNotRecorded, "expandable item")} was not recorded, as the phone's Android is older than 16, so they were not checked.";
        }

        var statusFound = run.Findings.Where(finding => finding.Check == PhoneCheckNames.StatusNotAnnounced).Concat(run.AppFindings).ToList();
        var statusFindings = statusFound.Count == 0
            ? "no findings"
            : $"{Count(statusFound.Count, "finding")} ({Levels(statusFound)})";
        var brief = run.StatusRun
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{Count(run.BriefMessages, "brief message")} (text shown and gone within 5 seconds, with no touch between), {run.BriefExposed:N0} of them in a live region, announced or in a titled pane that appeared with them")
            : "brief messages not looked for, as no touches were recorded, so text going away by itself cannot be told from text a touch took away";
        summary += string.Create(
            CultureInfo.CurrentCulture,
            $" Status messages (WCAG 4.1.3), two checks: {statusFindings}; {brief}; {Count(run.LiveRegionItems, "item")} in a live region, {Count(run.Announcements, "announcement")} and {Count(run.Toasts, "toast")} in the recording.");
        var timingChecks = new[]
        {
            PhoneCheckNames.TextGoneByItself, PhoneCheckNames.TextGoneAsContentChanged, PhoneCheckNames.TextUpdatingByItself, PhoneCheckNames.MovingByItself,
        };
        var timingFound = run.Findings.Where(finding => timingChecks.Contains(finding.Check)).ToList();
        var timingFindings = timingFound.Count == 0
            ? "no findings"
            : $"{Count(timingFound.Count, "finding")} ({Levels(timingFound)})";
        var timeout = !run.SettingsRecorded
            ? "Time to take action was not recorded, so texts gone within 5 seconds were looked at"
            : run.TimeoutMilliseconds is { } milliseconds
                ? string.Create(CultureInfo.CurrentCulture, $"Time to take action was set, at most {milliseconds / 1000.0:0.##} seconds, so texts gone sooner than it were looked at")
                : "Time to take action was Default, so texts gone within 5 seconds were looked at";
        var timed = run.StatusRun
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{timeout}: {Count(run.TimedMessages, "text")} with no touch between, {run.TimedAlone:N0} of them gone with nothing new shown as they went; {Count(run.UpdatedItems, "item")} whose text changed in place with no touch")
            : "text going away or updating by itself not looked for, as no touches were recorded";
        var moving = run.MotionRun
            ? string.Create(CultureInfo.CurrentCulture, $"{Count(run.MovingRuns, "part")} of the phone's video that kept changing for more than 5 seconds with no touch (from the video, so approximate)")
            : "moving content not looked for, as it needs the phone's video and touches recorded";
        summary += $" Timing (WCAG 2.2.1 and 2.2.2), four checks: {timingFindings}; {timed}; {moving}.";
        var contextChecks = new[]
        {
            PhoneCheckNames.ContextOnFocus, PhoneCheckNames.ContextOnScreenReaderFocus, PhoneCheckNames.ContextOnInput,
        };
        var contextFound = run.Findings.Where(finding => contextChecks.Contains(finding.Check)).ToList();
        var contextFindings = contextFound.Count == 0
            ? "no findings"
            : $"{Count(contextFound.Count, "finding")} ({Levels(contextFound)})";
        var context = run.ContextRun
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{Count(run.FocusMoves, "focus move")} by a key or a tap on a field, {Count(run.ScreenReaderFocusMoves, "TalkBack focus move")} after a gesture or key, and {Count(run.SettingChanges, "setting change")} (typing, checking, choosing or moving a slider); {run.ContextChanges:N0} {(run.ContextChanges == 1 ? "change" : "changes")} of context (a new screen or app, a window that took the focus, a pane, or the focus moving by itself) within 1.5 seconds of one, with nothing touched, pressed or clicked between")
            : "not looked for, as no touches were recorded, so a tap that caused a change cannot be told from none";
        summary += $" Changes of context (WCAG 3.2.1 and 3.2.2), three checks: {contextFindings}; {context}.";
        var orientationFound = run.Findings.Where(finding => finding.Check == PhoneCheckNames.OrientationRestricted).ToList();
        var orientationFindings = orientationFound.Count == 0
            ? "no findings"
            : $"{Count(orientationFound.Count, "finding")} ({Levels(orientationFound)})";
        var orientation = run.OrientationRun
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"the phone was turned between upright and sideways {Count(run.OrientationTurns, "time")}, from the companion app's own sensor, and the screen stayed as it was, with Auto-rotate on, for {Count(run.OrientationHoldSeconds, "second")} or more after {Count(run.OrientationNotFollowed, "turn")} within a visit")
            : $"not looked for, as {run.OrientationNotRun ?? "the checks did not run"}";
        summary += $" Orientation (WCAG 1.3.4), one check: {orientationFindings}; {orientation}; it is looked for only while Auto-rotate is on.";
        var sequenceChecks = new[] { PhoneCheckNames.SpacedLetters, PhoneCheckNames.SpaceColumns, PhoneCheckNames.LineBreakMidSentence };
        var sequenceFound = run.Findings.Where(finding => sequenceChecks.Contains(finding.Check)).ToList();
        var sequenceFindings = sequenceFound.Count == 0
            ? "no findings"
            : $"{Count(sequenceFound.Count, "finding")} ({Levels(sequenceFound)})";
        summary += string.Create(
            CultureInfo.CurrentCulture,
            $" White space used for layout (WCAG 1.3.2), three checks: {sequenceFindings}, from {Count(run.SequenceTexts, "text")} looked at (each shown item's text, content description, hint, state description and pane title, from the tree, not OCR; text typed into a field is not looked at); letters of a word spaced apart (3 or more single letters, 2 or more Han, kana or Hangul, initialisms included), 2 or more lines with gaps of 2 or more spaces or a tab, and a line break before a lowercase letter with no . ! ? : or ; before it. Whether the order of the content makes sense is not looked for.");
        if (run.TextFocusablePackages.Count > 0)
        {
            summary += $" In {string.Join(" and ", run.TextFocusablePackages)}, every item with text is focusable, as in an app drawn with Flutter, whose tree marks focusable what a screen reader can reach, so the tree does not show which items a keyboard reaches.";
        }
        if (run.WithoutDensity > 0)
        {
            summary += $" The touch target checks did not run on {Count(run.WithoutDensity, "settled snapshot")}, as no density was recorded for them.";
        }

        if (run.TargetsSeenWhole > 0)
        {
            summary += $" Touch target findings not run against the edge of a list that scrolls, dropped as another snapshot of the same visit showed the target whole and it was judged there: {run.TargetsSeenWhole:N0}.";
        }

        if (run.DensityChanged > 0)
        {
            summary += $" The touch target checks did not run on {Count(run.DensityChanged, "settled snapshot")} whose items were taken after the density changed during the visit, as they may have been laid out at either density.";
        }

        if (run.Problems.Count > 0)
        {
            summary += $" {Count(run.Problems.Count, "snapshot")} could not be checked: {string.Join("; ", run.Problems.Take(3))}{(run.Problems.Count > 3 ? "; and more" : string.Empty)}.";
        }

        summary += " " + ContrastSummary();
        if (_shownFinding is { } shown)
        {
            summary += string.Create(
                CultureInfo.CurrentCulture,
                $" Showing time {_shownFindingTime + 1:N0} of {shown.Times.Count:N0} of the chosen finding, outlined on the phone's picture while its items are shown.");
        }

        return summary;
    }

    // What the contrast checks found, of text and non-text, or why they have not.
    private string ContrastSummary()
    {
        if (_phoneContrastProblem is { } problem)
        {
            return $"The contrast checks, of text and non-text, could not be run: {problem}";
        }

        if (_phoneContrast is not { } measured || _phoneTextContrast is not { } contrast)
        {
            return "Measuring the contrast of text and non-text.";
        }

        var title = PhoneCheckNames.Title(contrast.Check);
        var findings = contrast.Findings.Count == 0
            ? "no findings"
            : $"{Count(contrast.Findings.Count, "finding")} ({Levels(contrast.Findings)})";
        var summary = string.Create(
            CultureInfo.CurrentCulture,
            $"{title}: {findings}, from {Count(contrast.Lines, "line")} of text measured, {contrast.Passed:N0} passed, on {Count(measured.Snapshots.Count, "settled snapshot")} ({measured.FromScreenshots:N0} from the phone's screenshots, {measured.FromFrames:N0} from the video), in {measured.Elapsed.TotalSeconds:0.0} seconds.");
        if (_phoneNonTextContrast is { } nonText)
        {
            var ofCheck = nonText.Findings.Where(finding => finding.Check == PhoneCheckNames.NonTextContrast).ToList();
            var bottomLines = nonText.Findings.Where(finding => finding.Check == PhoneCheckNames.FieldBottomLineOnly).ToList();
            var nonTextFindings = ofCheck.Count == 0
                ? "no findings"
                : $"{Count(ofCheck.Count, "finding")} ({Levels(ofCheck)})";
            var lineFindings = bottomLines.Count == 0 ? "no findings" : $"{Count(bottomLines.Count, "finding")} ({Levels(bottomLines)})";
            summary += string.Create(
                CultureInfo.CurrentCulture,
                $" {PhoneCheckNames.Title(PhoneCheckNames.NonTextContrast)}, at AA, its only level: {nonTextFindings}, from {Count(nonText.Controls, "control")} measured, {nonText.Passed:N0} passed; graphics with no item in the tree are not checked. " +
                $"{NonTextRules} {PhoneCheckNames.Title(PhoneCheckNames.FieldBottomLineOnly)}: {lineFindings}.");
        }

        if (_phoneImagesOfText is { } images)
        {
            var checks = images.Level == PhoneAuditLevel.AAA
                ? $"{PhoneCheckNames.Title(PhoneCheckNames.ImageOfText)} and {PhoneCheckNames.Title(PhoneCheckNames.ImageOfTextAaa)}"
                : PhoneCheckNames.Title(PhoneCheckNames.ImageOfText);
            var imageFindings = images.Findings.Count == 0
                ? "no findings"
                : $"{Count(images.Findings.Count, "finding")} ({Levels(images.Findings)})";
            summary += images.SnapshotsRead == 0
                ? $" {checks}: not checked, as it needs OCR's words."
                : string.Create(CultureInfo.CurrentCulture, $" {checks}: {imageFindings}, from {Count(images.Images, "image")} read by OCR, {images.Passed:N0} passed; ") +
                    $"the words OCR read inside an image that no item's text holds are a probable image of text. " +
                    $"Before any model, the rules left out {Count(images.CaptionLines, "line")} TalkBack was saying, {Count(images.EdgeLines, "line")} past an image's edge and {Count(images.NeighbourLines, "line")} an item beside the image holds; " +
                    $"{Count(images.OutOfStep, "image")} on a picture out of step with the tree {(images.OutOfStep == 1 ? "was" : "were")} not judged, " +
                    $"{Count(images.ManualChecks, "image")} {(images.ManualChecks == 1 ? "is" : "are")} for a manual check, as the app draws its own text with Flutter, " +
                    $"and {Count(images.Questions.Count, "image")} {(images.Questions.Count == 1 ? "is" : "are")} for the model for vision-based checks, whose answer decides the exceptions" +
                    (images.ModelNote is { } note ? $": {note}." : ".");
        }

        if (_phoneHeadingLook is { } headings)
        {
            var headingTitle = PhoneCheckNames.Title(PhoneCheckNames.TextShownAsHeading);
            var headingFindings = headings.Findings.Count == 0
                ? "no findings"
                : $"{Count(headings.Findings.Count, "finding")} ({Levels(headings.Findings)})";
            summary += headings.SnapshotsRead == 0
                ? $" {headingTitle}: not checked, as it needs OCR's words."
                : $" {headingTitle}: {headingFindings}, from {Count(headings.Items, "piece")} of text placed by OCR; " +
                    $"text not marked as a heading whose letters are at least {PhoneHeadingLookRun.TallerBy.ToString("0.##", CultureInfo.CurrentCulture)} times as tall as the screen's body text, or one short line in capitals, is a probable heading. " +
                    $"The rules left out {Count(headings.InControls, "piece")} of text inside an item that can be tapped or a field, as a control's text is its name, and {Count(headings.FieldLabels, "line")} in capitals directly above a field, as its label; " +
                    $"{Count(headings.Questions.Count, "piece")} of text {(headings.Questions.Count == 1 ? "is" : "are")} for the model for vision-based checks" +
                    (headings.ModelNote is { } headingNote ? $": {headingNote}." : ".");
        }

        if (_phoneImageAlternative is { } alternatives)
        {
            var alternativeTitle = PhoneCheckNames.Title(PhoneCheckNames.ImageNoTextAlternative);
            var alternativeFindings = alternatives.Findings.Count == 0
                ? "no findings"
                : $"{Count(alternatives.Findings.Count, "finding")} ({Levels(alternatives.Findings)})";
            summary += alternatives.SnapshotsRead == 0
                ? $" {alternativeTitle}: not checked, as it needs OCR's words to tell a picture out of step with the tree."
                : $" {alternativeTitle}: {alternativeFindings}, from {Count(alternatives.Images, "image")} on the pictures OCR read; " +
                    "an image that is not a control, with no description, text or label, that no control with a name holds, says nothing to a screen reader. " +
                    $"The rules left out {Count(alternatives.Named, "image")} with a name of {(alternatives.Named == 1 ? "its" : "their")} own, " +
                    $"{Count(alternatives.Controls, "image")} that {(alternatives.Controls == 1 ? "is a control" : "are controls")}, as the tree's checks judge a control's name, " +
                    $"{Count(alternatives.InNamedControls, "image")} inside a control with a name, " +
                    $"{Count(alternatives.Covered, "image")} at least half under or over controls with a name, " +
                    $"and {Count(alternatives.Thin, "image")} under {PhoneImageAlternativeRun.LeastSideDp.ToString("0.##", CultureInfo.CurrentCulture)} dp on the shorter side; " +
                    $"{Count(alternatives.OutOfStep, "image")} on a picture out of step with the tree {(alternatives.OutOfStep == 1 ? "was" : "were")} not judged, " +
                    $"and {Count(alternatives.Questions.Count, "image")} {(alternatives.Questions.Count == 1 ? "is" : "are")} for the model for vision-based checks, whose answer decides whether it gives information" +
                    (alternatives.ModelNote is { } alternativeNote ? $": {alternativeNote}." : ".");
        }

        if (_phoneLabelInName is { } labels)
        {
            var labelFindings = labels.Findings.Count == 0
                ? "no findings"
                : $"{Count(labels.Findings.Count, "finding")} ({Levels(labels.Findings)})";
            var read = labels.UsedOcr ? "the tree's text and, where it has none, OCR's" : "the tree's text";
            summary += string.Create(
                CultureInfo.CurrentCulture,
                $" {PhoneLabelInNameRun.Title}: {labelFindings}, from {Count(labels.Controls, "control")} checked, {labels.Passed:N0} passed, {labels.Unread:N0} with no label read; the label is all the text inside a control, and the text of any item it is labelled by, from {read}, against its content description; text beside a control is not taken as its label.");
        }

        var engine = measured.Snapshots.Select(snapshot => snapshot.Engine).FirstOrDefault(name => !string.IsNullOrEmpty(name));
        summary += measured.OcrUnavailable is { } unavailable
            ? $" OCR was not used, as {unavailable}, so only the app's text was measured, on its items' bounds."
            : engine is null ? string.Empty : $" The text was found with {engine}.";
        if (measured.Problems.Count > 0)
        {
            summary += $" {Count(measured.Problems.Count, "snapshot")} could not be measured: {string.Join("; ", measured.Problems.Take(3))}{(measured.Problems.Count > 3 ? "; and more" : string.Empty)}.";
        }

        return summary;
    }

    private void ShowCurrentVisitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_phoneScreenRows.Count == 0)
        {
            return;
        }

        PhoneScreensListBox.ScrollIntoView(_currentVisitRow ?? _phoneScreenRows[0]);
    }

    private void PhoneScreenRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhoneScreenRowView row })
        {
            GoToPhoneScreenRow(row);
        }
    }

    private void PhoneScreenRow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && sender is FrameworkElement { DataContext: PhoneScreenRowView row })
        {
            GoToPhoneScreenRow(row);
            e.Handled = true;
        }
    }

    private void GoToPhoneScreenRow(PhoneScreenRowView row)
    {
        if (_playbackArchive is null)
        {
            return;
        }

        if (row.Finding is { } finding)
        {
            ShowFindingTime(finding, 0);
            return;
        }

        _shownFinding = null;
        ShowFindingButtons();
        PausePlayback();
        SeekTo(row.Start, synchronizeAudio: false);
    }

    // Moves the playhead to one of a finding's times, shows its item in the
    // "Accessibility tree" tab, and outlines it on the phone's picture.
    private void ShowFindingTime(PhoneFinding finding, int index)
    {
        if (_playbackArchive is null || index < 0 || index >= finding.Times.Count)
        {
            return;
        }

        _shownFinding = finding;
        _shownFindingTime = index;
        ShowFindingButtons();
        PausePlayback();
        var time = finding.Times[index];
        SeekTo(time.Time, synchronizeAudio: false);
        ShowTreeRow(time.TreeKey, select: true);
        DisplayTouchesAt(_playbackPositionNanoseconds);
        DisplayPhoneVisitAt(_playbackPositionNanoseconds);
    }

    private void ShowFindingButtons()
    {
        PreviousFindingTimeButton.IsEnabled = _shownFinding is not null && _shownFindingTime > 0;
        NextFindingTimeButton.IsEnabled = _shownFinding is { } shown && _shownFindingTime < shown.Times.Count - 1;
    }

    private void PreviousFindingTimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shownFinding is { } finding)
        {
            ShowFindingTime(finding, _shownFindingTime - 1);
        }
    }

    private void NextFindingTimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shownFinding is { } finding)
        {
            ShowFindingTime(finding, _shownFindingTime + 1);
        }
    }

    private static string Count(int count, string noun) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? noun : noun + "s")}");
}

/// <summary>A row of the Checks tab: the density, an app, app metadata, a screen, a visit, a state, a check or a finding.</summary>
public sealed class PhoneScreenRowView : INotifyPropertyChanged
{
    private bool _isCurrent;

    public PhoneScreenRowView(int level, string text, long start, PhoneVisit? visit)
    {
        Level = level;
        Text = text;
        Start = start;
        Visit = visit;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>0 for the density or an app, 1 a screen, 2 a visit, 3 a state or a check, 4 a finding.</summary>
    public int Level { get; }

    public string Text { get; }

    /// <summary>Where the row moves the playhead.</summary>
    public long Start { get; }

    /// <summary>The visit, for a visit's row.</summary>
    public PhoneVisit? Visit { get; }

    /// <summary>The finding, for a finding's row.</summary>
    public PhoneFinding? Finding { get; init; }

    /// <summary>Whether the row heads a check's findings, or says a visit has none or was not checked.</summary>
    public bool IsCheck { get; init; }

    /// <summary>App information, shown as normal text rather than a screen heading.</summary>
    public bool IsMetadata { get; init; }

    public bool IsHeading => !IsMetadata && (Level < 2 || IsCheck);

    public Thickness Indent => new(Level * 20, 0, 0, 0);

    /// <summary>Whether the row is the visit at the playhead.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
            {
                return;
            }

            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Spoken)));
        }
    }

    /// <summary>The row's accessible name: its text, and whether it is at the playhead.</summary>
    public string Spoken => Text + (IsCurrent ? ", at the playhead" : string.Empty);
}

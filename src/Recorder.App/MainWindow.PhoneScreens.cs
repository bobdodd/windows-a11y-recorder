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
// recording's optional pre-start text sizes above the findings list, then
// its display density and apps with their versions, then
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
    private PhoneDescribedImageRun? _phoneDescribedImages;
    private PhoneDescribedImageRun? _phoneDescribedImagesRules;
    private (PhoneDescribedImageRun Rules, IReadOnlyList<DescribedImageRequests> Requests)? _describedImageRequests;
    private int _describedImagesUnanswered;

    // The screen title check, the same way (android-checks.md, step 22);
    // its kept answers are found by the title, what a screen reader says,
    // the text OCR read and, where given, the screen's picture, in the
    // project, from the model for text-based checks or, for a screen images
    // cover half of, for vision-based checks.
    private PhoneScreenTitleRun? _phoneScreenTitles;
    private PhoneScreenTitleRun? _phoneScreenTitlesRules;
    private (PhoneScreenTitleRun Rules, IReadOnlyList<ScreenTitleRequest> Requests)? _screenTitleRequests;
    private int _titleTextUnanswered;
    private int _titleVisionUnanswered;

    // The decorative image check, the same way (android-checks.md, step
    // 23); its kept answers are found by the image's pixels, its
    // description and what a screen reader says on the screen.
    private PhoneDecorativeImageRun? _phoneDecorativeImages;
    private PhoneDecorativeImageRun? _phoneDecorativeImagesRules;
    private (PhoneDecorativeImageRun Rules, IReadOnlyList<(PhoneDescribedImageQuestion Question, DescribedImageRequest Request)> Requests)? _decorativeRequests;
    private int _decorativeUnanswered;

    // The check of a heading that does not describe its section, the
    // same way as the screen title check (android-checks.md, step 24).
    // Its rules take the text the check of text shown as a heading judged
    // headings, so are run again from the measurement once that check's
    // kept answers are given; its own are found by the heading, the
    // section's texts and, where given, the screen's picture.
    private PhoneHeadingSectionRun? _phoneHeadingSectionsMeasured;
    private PhoneHeadingSectionRun? _phoneHeadingSectionsRules;
    private PhoneHeadingLookRun? _phoneHeadingSectionsLook;
    private PhoneHeadingSectionRun? _phoneHeadingSections;
    private (PhoneHeadingSectionRun Rules, IReadOnlyList<HeadingSectionRequest> Requests)? _headingSectionRequests;
    private int _sectionTextUnanswered;
    private int _sectionVisionUnanswered;

    // The check of text on screen not in the tree, the same way as the
    // decorative image check (android-checks.md, step 25); its kept
    // answers are found by the line's pixels, its text and item and what a
    // screen reader says on the screen.
    private PhoneTextNotInTreeRun? _phoneTextNotInTree;
    private PhoneTextNotInTreeRun? _phoneTextNotInTreeRules;
    private (PhoneTextNotInTreeRun Rules, IReadOnlyList<(PhoneTextNotInTreeQuestion Question, DescribedImageRequest Request)> Requests)? _textNotInTreeRequests;
    private int _textNotInTreeUnanswered;

    // The check of a marked heading not shown as one, the same way as the
    // check of text shown as a heading (android-checks.md, step 27); its
    // kept answers are found by its requests, and a marked heading judged
    // not a heading by the model for simple questions waits for the model
    // for complex questions to confirm it, where there is one.
    private PhoneHeadingNotShownRun? _phoneHeadingNotShown;
    private PhoneHeadingNotShownRun? _phoneHeadingNotShownRules;
    private (PhoneHeadingNotShownRun Rules, IReadOnlyList<HeadingLookRequest> Requests)? _headingNotShownRequests;
    private int _headingNotShownUnanswered;

    // The check of a control name that does not say what the control does,
    // the same way (android-checks.md, step 28); its kept answers are found
    // by its requests, and a name judged not to say what the control does
    // by the model for simple questions waits for the model for complex
    // questions to confirm it, where there is one.
    private PhoneControlNameRun? _phoneControlNames;
    private PhoneControlNameRun? _phoneControlNamesRules;
    private (PhoneControlNameRun Rules, IReadOnlyList<ControlNameRequest> Requests)? _controlNamesRequests;
    private int _controlNamesUnanswered;
    private PhoneLabelInNameRun? _phoneLabelInName;
    private PhoneColourOnlyRun? _phoneColourOnly;

    // The check of a chart that relies on colour, the same way
    // (android-checks.md, step 32); its kept answers are found by its
    // requests, and an item judged to rely on colour alone by the model
    // for simple questions waits for the model for complex questions to
    // confirm it, where there is one.
    private PhoneChartColourRun? _phoneChartColours;
    private PhoneChartColourRun? _phoneChartColoursRules;
    private (PhoneChartColourRun Rules, IReadOnlyList<ChartColourRequest> Requests)? _chartColoursRequests;
    private int _chartColoursUnanswered;
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
            ShowRecordingTextSizeEvidence(_playbackArchive);
            ShowAndroidAppEvidence(_playbackArchive);
            ShowRecordedFormEvidence(_playbackArchive?.PhoneScreens.Recorded == true, null);
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
            _phoneDescribedImages = null;
            _phoneDescribedImagesRules = null;
            _describedImageRequests = null;
            _describedImagesUnanswered = 0;
            _phoneScreenTitles = null;
            _phoneScreenTitlesRules = null;
            _screenTitleRequests = null;
            _titleTextUnanswered = 0;
            _titleVisionUnanswered = 0;
            _phoneDecorativeImages = null;
            _phoneDecorativeImagesRules = null;
            _decorativeRequests = null;
            _decorativeUnanswered = 0;
            _phoneHeadingSectionsMeasured = null;
            _phoneHeadingSectionsRules = null;
            _phoneHeadingSectionsLook = null;
            _phoneHeadingSections = null;
            _headingSectionRequests = null;
            _sectionTextUnanswered = 0;
            _sectionVisionUnanswered = 0;
            _phoneTextNotInTree = null;
            _phoneTextNotInTreeRules = null;
            _textNotInTreeRequests = null;
            _textNotInTreeUnanswered = 0;
            _phoneHeadingNotShown = null;
            _phoneHeadingNotShownRules = null;
            _headingNotShownRequests = null;
            _headingNotShownUnanswered = 0;
            _phoneControlNames = null;
            _phoneControlNamesRules = null;
            _controlNamesRequests = null;
            _controlNamesUnanswered = 0;
            _phoneLabelInName = null;
            _phoneColourOnly = null;
            _phoneChartColours = null;
            _phoneChartColoursRules = null;
            _chartColoursRequests = null;
            _chartColoursUnanswered = 0;
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

    private void ShowRecordingTextSizeEvidence(SessionPlaybackArchive? archive)
    {
        var evidence = archive is null ? null : AndroidTextSizeChecksEvidence.Describe(archive.Manifest, CultureInfo.CurrentCulture);
        var isPhoneRecording = archive is not null && (archive.PhoneScreens.Recorded || IsPhoneInput(archive));
        var hasDetails = evidence is { NodeLines.Count: > 0 };

        // Separate from the rows that are rebuilt when checks finish. Neither
        // the contents nor the open/closed state changes during playback.
        RecordingTextSizeDetails.IsExpanded = false;
        RecordingTextSizeDetails.Visibility = hasDetails ? Visibility.Visible : Visibility.Collapsed;
        RecordingTextSizeNodeText.Text = hasDetails ? string.Join(Environment.NewLine, evidence!.NodeLines) : string.Empty;
        RecordingTextSizeSummary.Text = evidence?.Summary ?? "Text sizes before recording: no snapshot was saved in this recording.";
        RecordingTextSizeEvidence.Visibility = evidence is not null || isPhoneRecording ? Visibility.Visible : Visibility.Collapsed;
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
        ShowRecordedFormEvidence(archive.PhoneScreens.Recorded, run, failed: problem is not null);
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

        // The icon and description checks are level A, so the same at both
        // audit levels; their kept answers are given with the images' below.
        _phoneDescribedImagesRules = run is null
            ? null
            : PhoneDescribedImageRun.Run(run, archive.PhoneScreens, archive.TalkBackSpeech, archive.PhoneAccessibility);
        _phoneDescribedImages = _phoneDescribedImagesRules;
        _describedImagesUnanswered = _phoneDescribedImagesRules?.Questions.Count ?? 0;

        // The screen title check is level A, so the same at both audit
        // levels; its kept answers are given with the images' below.
        _phoneScreenTitlesRules = run is null ? null : PhoneScreenTitleRun.Run(run, archive.PhoneScreens);
        _phoneScreenTitles = _phoneScreenTitlesRules;
        _titleTextUnanswered = _phoneScreenTitlesRules?.TextQuestions ?? 0;
        _titleVisionUnanswered = _phoneScreenTitlesRules?.PicturedQuestions ?? 0;

        // The decorative image check takes the images the icon and
        // description checks choose, so is the same at both audit levels.
        _phoneDecorativeImagesRules = _phoneDescribedImagesRules is null ? null : PhoneDecorativeImageRun.Run(_phoneDescribedImagesRules);
        _phoneDecorativeImages = _phoneDecorativeImagesRules;
        _decorativeUnanswered = _phoneDecorativeImagesRules?.Questions.Count ?? 0;

        // The check of a heading's section is level AA but has no AAA
        // counterpart, so is the same at both audit levels. Until the
        // check of text shown as a heading is judged, only marked headings
        // are among its headings; its kept answers are given below.
        _phoneHeadingSectionsMeasured = run is null ? null : PhoneHeadingSectionRun.Run(run, archive.PhoneScreens, null);
        _phoneHeadingSectionsRules = _phoneHeadingSectionsMeasured;
        _phoneHeadingSectionsLook = null;
        _phoneHeadingSections = _phoneHeadingSectionsRules;
        _sectionTextUnanswered = _phoneHeadingSectionsRules?.TextQuestions ?? 0;
        _sectionVisionUnanswered = _phoneHeadingSectionsRules?.PicturedQuestions ?? 0;

        // The check of text not in the tree is level A, so the same at both
        // audit levels; its kept answers are given with the images' below.
        _phoneTextNotInTreeRules = run is null
            ? null
            : PhoneTextNotInTreeRun.Run(run, archive.PhoneScreens, archive.TalkBackSpeech, archive.PhoneAccessibility);
        _phoneTextNotInTree = _phoneTextNotInTreeRules;
        _textNotInTreeUnanswered = _phoneTextNotInTreeRules?.Questions.Count ?? 0;

        // The check of a marked heading not shown as one is level A, so the
        // same at both audit levels; its kept answers are given below.
        _phoneHeadingNotShownRules = run is null ? null : PhoneHeadingNotShownRun.Run(run, archive.PhoneScreens);
        _phoneHeadingNotShown = _phoneHeadingNotShownRules;
        _headingNotShownUnanswered = _phoneHeadingNotShownRules?.Questions.Count ?? 0;

        // The check of a control's name runs at both audit levels, as one
        // of its criteria, 1.1.1, is level A; its kept answers are given
        // below.
        _phoneControlNamesRules = run is null ? null : PhoneControlNameRun.Run(run, archive.PhoneScreens);
        _phoneControlNames = _phoneControlNamesRules;
        _controlNamesUnanswered = _phoneControlNamesRules?.Questions.Count ?? 0;

        // The check of a chart that relies on colour (WCAG 1.4.1, level A)
        // runs at both audit levels; its kept answers are given below.
        _phoneChartColoursRules = run is null ? null : PhoneChartColourRun.Run(run, archive.PhoneScreens);
        _phoneChartColours = _phoneChartColoursRules;
        _chartColoursUnanswered = _phoneChartColoursRules?.Questions.Count ?? 0;
        JudgeTextContrast();

        // Non-text contrast has one level, AA (WCAG 2.2's 1.4.11), so is
        // judged once, not again when the text's level changes.
        _phoneNonTextContrast = run is null ? null : PhoneNonTextContrastRun.Run(run, archive.PhoneScreens);

        // Label in Name is level A, so the same at both audit levels.
        _phoneLabelInName = run is null ? null : PhoneLabelInNameRun.Run(run, archive.PhoneScreens);

        // The checks of use of colour are level A, so the same at both audit levels.
        _phoneColourOnly = run is null ? null : PhoneColourOnlyRun.Run(run, archive.PhoneScreens);
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

    // The kept answers shown, then, where a task of the model checks
    // finished, its toast, so that it says how many questions are still
    // not judged once the answers are in the findings.
    private async Task ApplyKeptAnswersAsync(ModelChecksChange? finished)
    {
        await ApplyKeptAnswersAsync();
        if (finished is not null)
        {
            ShowModelChecksToasts([finished]);
        }
    }

    // Gives the images-of-text check, the check of text shown as a heading,
    // the check of images with no text alternative, the icon and
    // description checks and the screen title check the answers the
    // project's model for vision-based checks (or, for a screen title asked
    // with only text, text-based checks) has given to their questions, in
    // any task, as they are kept
    // by their requests, or for an image with no text alternative or a
    // described image by its pixels, its text and what a screen reader
    // says on the screen, in the project; the model is not asked here.
    // With no model chosen, what the rules leave stays a warning, or for
    // a described image is not judged, and the summary says why.
    private async Task ApplyKeptAnswersAsync()
    {
        await ApplyKeptImageAnswersAsync();
        await ApplyKeptHeadingAnswersAsync();
        await ApplyKeptImageAlternativeAnswersAsync();
        await ApplyKeptDescribedImageAnswersAsync();
        await ApplyKeptScreenTitleAnswersAsync();
        await ApplyKeptDecorativeAnswersAsync();
        await ApplyKeptHeadingSectionAnswersAsync();
        await ApplyKeptTextNotInTreeAnswersAsync();
        await ApplyKeptHeadingNotShownAnswersAsync();
        await ApplyKeptControlNameAnswersAsync();
        await ApplyKeptChartColourAnswersAsync();
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

    private async Task ApplyKeptDescribedImageAnswersAsync()
    {
        if (_phoneDescribedImagesRules is not { } rules)
        {
            UpdateAskChecksModelButton();
            return;
        }

        if (rules.Questions.Count == 0)
        {
            _describedImagesUnanswered = 0;
            UpdateAskChecksModelButton();
            return;
        }

        var name = _openProjectModels?.VisionChecks;
        var problem = name is null
            ? "the project's settings choose no model for vision-based checks"
            : ProjectModels.Problem(ProjectChoices, ModelUses.VisionChecks, name);
        var none = new Dictionary<PhoneDescribedImageQuestion, PhoneDescribedImageAnswer>();
        PhoneDescribedImageRun shown;
        if (problem is not null || AnalysisCatalogOrNull is not { } catalog || ProjectChoices.Profile(name!) is not { } profile)
        {
            shown = rules.Judged(none, AndroidDescribedImages.NotAsked(rules.Questions.Count, problem ?? "the analysis catalog is not open"));
        }
        else
        {
            try
            {
                if (_describedImageRequests is not { } made || !ReferenceEquals(made.Rules, rules))
                {
                    var requests = await Task.Run(() => AndroidDescribedImages.Requests(rules, PhonePictures.Load));
                    made = (rules, requests);
                    _describedImageRequests = made;
                }

                string[] templates = [AndroidDescribedImages.KindTemplate, AndroidDescribedImages.IconTemplate, AndroidDescribedImages.DescriptionTemplate];
                var signed = new Dictionary<string, IReadOnlyList<SignedAnswer>>(StringComparer.Ordinal);
                Dictionary<string, IReadOnlyList<SignedAnswer>>? complexSigned = null;
                var complex = ComplexVisionModel();
                foreach (var template in templates)
                {
                    signed[template] = await catalog.SignedAnswersAsync(
                        template, AndroidDescribedImages.TemplateVersion, name!, profile.Model, _openProjectId, _analysisRecordingId, CancellationToken.None);
                    if (complex is not null)
                    {
                        complexSigned ??= new Dictionary<string, IReadOnlyList<SignedAnswer>>(StringComparer.Ordinal);
                        complexSigned[template] = await catalog.SignedAnswersAsync(
                            template, AndroidDescribedImages.TemplateVersion, complex.Value.Name, complex.Value.Profile.Model,
                            _openProjectId, _analysisRecordingId, CancellationToken.None);
                    }
                }

                shown = AndroidDescribedImages.FromKept(rules, made.Requests, signed, name!, complexSigned, complex?.Name);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                shown = rules.Judged(none, $"the model's kept answers could not be read: {exception.Message}");
            }
        }

        // The recording changed while the answers were read.
        if (!ReferenceEquals(rules, _phoneDescribedImagesRules))
        {
            return;
        }

        // A question the model for complex questions has not yet answered
        // is not judged, so is counted with those not yet asked.
        _phoneDescribedImages = shown;
        _describedImagesUnanswered = problem is not null ? 0 : shown.Unanswered;
        UpdateAskChecksModelButton();
        ShowPhoneScreenRows();
    }

    private async Task ApplyKeptScreenTitleAnswersAsync()
    {
        if (_phoneScreenTitlesRules is not { } rules)
        {
            UpdateAskChecksModelButton();
            return;
        }

        if (rules.Questions.Count == 0)
        {
            _titleTextUnanswered = 0;
            _titleVisionUnanswered = 0;
            UpdateAskChecksModelButton();
            return;
        }

        string? Problem(string use, string? name, int questions) =>
            questions == 0
                ? null
                : name is null
                    ? $"the project's settings choose no model for {ModelUses.Name(use)}"
                    : ProjectModels.Problem(ProjectChoices, use, name);
        var textName = _openProjectModels?.TextChecks;
        var visionName = _openProjectModels?.VisionChecks;
        var catalog = AnalysisCatalogOrNull;
        var textProblem = Problem(ModelUses.TextChecks, textName, rules.TextQuestions) ?? (catalog is null && rules.TextQuestions > 0 ? "the analysis catalog is not open" : null);
        var visionProblem = Problem(ModelUses.VisionChecks, visionName, rules.PicturedQuestions) ?? (catalog is null && rules.PicturedQuestions > 0 ? "the analysis catalog is not open" : null);
        var none = new Dictionary<PhoneScreenTitleQuestion, PhoneScreenTitleAnswer>();
        PhoneScreenTitleRun shown;
        if ((rules.TextQuestions == 0 || textProblem is not null) && (rules.PicturedQuestions == 0 || visionProblem is not null))
        {
            shown = rules.Judged(none, AndroidScreenTitles.NotAsked(rules, textProblem, visionProblem));
        }
        else
        {
            try
            {
                if (_screenTitleRequests is not { } made || !ReferenceEquals(made.Rules, rules))
                {
                    var requests = await Task.Run(() => AndroidScreenTitles.Requests(rules, PhonePictures.Load));
                    made = (rules, requests);
                    _screenTitleRequests = made;
                }

                async Task<ScreenTitleKept?> KeptAsync(string template, string? name, string? problem, (string Name, ModelProfile Profile)? complex)
                {
                    if (problem is not null || name is null || ProjectChoices.Profile(name) is not { } profile)
                    {
                        return null;
                    }

                    var signed = await catalog!.SignedAnswersAsync(
                        template, AndroidScreenTitles.TemplateVersion, name, profile.Model, _openProjectId, _analysisRecordingId, CancellationToken.None);
                    var complexSigned = complex is { } other
                        ? await catalog.SignedAnswersAsync(
                            template, AndroidScreenTitles.TemplateVersion, other.Name, other.Profile.Model, _openProjectId, _analysisRecordingId, CancellationToken.None)
                        : null;
                    return new ScreenTitleKept(signed, name, complexSigned, complex?.Name);
                }

                var text = await KeptAsync(AndroidScreenTitles.TextTemplate, textName, textProblem, ComplexTextModel());
                var vision = await KeptAsync(AndroidScreenTitles.PicturedTemplate, visionName, visionProblem, ComplexVisionModel());
                shown = AndroidScreenTitles.FromKept(rules, made.Requests, text, vision, textProblem, visionProblem);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                shown = rules.Judged(none, $"the model's kept answers could not be read: {exception.Message}");
            }
        }

        // The recording changed while the answers were read.
        if (!ReferenceEquals(rules, _phoneScreenTitlesRules))
        {
            return;
        }

        // A question the model for complex questions has not yet answered
        // is not judged, so is counted with those not yet asked.
        _phoneScreenTitles = shown;
        _titleTextUnanswered = textProblem is not null ? 0 : shown.Questions.Count(question => !question.Pictured && !shown.Answers.ContainsKey(question));
        _titleVisionUnanswered = visionProblem is not null ? 0 : shown.Questions.Count(question => question.Pictured && !shown.Answers.ContainsKey(question));
        UpdateAskChecksModelButton();
        ShowPhoneScreenRows();
    }

    private async Task ApplyKeptDecorativeAnswersAsync()
    {
        if (_phoneDecorativeImagesRules is not { } rules)
        {
            UpdateAskChecksModelButton();
            return;
        }

        if (rules.Questions.Count == 0)
        {
            _decorativeUnanswered = 0;
            UpdateAskChecksModelButton();
            return;
        }

        var name = _openProjectModels?.VisionChecks;
        var problem = name is null
            ? $"the project's settings choose no model for {ModelUses.Name(ModelUses.VisionChecks)}"
            : ProjectModels.Problem(ProjectChoices, ModelUses.VisionChecks, name);
        var none = new Dictionary<PhoneDescribedImageQuestion, PhoneDecorativeAnswer>();
        PhoneDecorativeImageRun shown;
        if (problem is not null || AnalysisCatalogOrNull is not { } catalog || ProjectChoices.Profile(name!) is not { } profile)
        {
            shown = rules.Judged(none, AndroidDecorativeImages.NotAsked(rules.Questions.Count, problem ?? "the analysis catalog is not open"));
        }
        else
        {
            try
            {
                if (_decorativeRequests is not { } made || !ReferenceEquals(made.Rules, rules))
                {
                    var requests = await Task.Run(() => AndroidDecorativeImages.Requests(rules, PhonePictures.Load));
                    made = (rules, requests);
                    _decorativeRequests = made;
                }

                var signed = await catalog.SignedAnswersAsync(
                    AndroidDecorativeImages.Template, AndroidDecorativeImages.TemplateVersion, name!, profile.Model, _openProjectId, _analysisRecordingId, CancellationToken.None);
                var complex = ComplexVisionModel();
                var complexSigned = complex is { } other
                    ? await catalog.SignedAnswersAsync(
                        AndroidDecorativeImages.Template, AndroidDecorativeImages.TemplateVersion, other.Name, other.Profile.Model, _openProjectId, _analysisRecordingId, CancellationToken.None)
                    : null;
                shown = AndroidDecorativeImages.FromKept(rules, made.Requests, signed, name!, complexSigned, complex?.Name);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                shown = rules.Judged(none, $"the model's kept answers could not be read: {exception.Message}");
            }
        }

        // The recording changed while the answers were read.
        if (!ReferenceEquals(rules, _phoneDecorativeImagesRules))
        {
            return;
        }

        // A question the model for complex questions has not yet answered
        // is not judged, so is counted with those not yet asked.
        _phoneDecorativeImages = shown;
        _decorativeUnanswered = problem is not null ? 0 : shown.Unanswered;
        UpdateAskChecksModelButton();
        ShowPhoneScreenRows();
    }

    private async Task ApplyKeptTextNotInTreeAnswersAsync()
    {
        if (_phoneTextNotInTreeRules is not { } rules)
        {
            UpdateAskChecksModelButton();
            return;
        }

        if (rules.Questions.Count == 0)
        {
            _textNotInTreeUnanswered = 0;
            UpdateAskChecksModelButton();
            return;
        }

        var name = _openProjectModels?.VisionChecks;
        var problem = name is null
            ? $"the project's settings choose no model for {ModelUses.Name(ModelUses.VisionChecks)}"
            : ProjectModels.Problem(ProjectChoices, ModelUses.VisionChecks, name);
        var none = new Dictionary<PhoneTextNotInTreeQuestion, PhoneTextNotInTreeAnswer>();
        PhoneTextNotInTreeRun shown;
        if (problem is not null || AnalysisCatalogOrNull is not { } catalog || ProjectChoices.Profile(name!) is not { } profile)
        {
            shown = rules.Judged(none, AndroidTextNotInTree.NotAsked(rules.Questions.Count, problem ?? "the analysis catalog is not open"));
        }
        else
        {
            try
            {
                if (_textNotInTreeRequests is not { } made || !ReferenceEquals(made.Rules, rules))
                {
                    var requests = await Task.Run(() => AndroidTextNotInTree.Requests(rules, PhonePictures.Load));
                    made = (rules, requests);
                    _textNotInTreeRequests = made;
                }

                var signed = await catalog.SignedAnswersAsync(
                    AndroidTextNotInTree.Template, AndroidTextNotInTree.TemplateVersion, name!, profile.Model, _openProjectId, _analysisRecordingId, CancellationToken.None);
                var complex = ComplexVisionModel();
                var complexSigned = complex is { } other
                    ? await catalog.SignedAnswersAsync(
                        AndroidTextNotInTree.Template, AndroidTextNotInTree.TemplateVersion, other.Name, other.Profile.Model, _openProjectId, _analysisRecordingId, CancellationToken.None)
                    : null;
                shown = AndroidTextNotInTree.FromKept(rules, made.Requests, signed, name!, complexSigned, complex?.Name);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                shown = rules.Judged(none, $"the model's kept answers could not be read: {exception.Message}");
            }
        }

        // The recording changed while the answers were read.
        if (!ReferenceEquals(rules, _phoneTextNotInTreeRules))
        {
            return;
        }

        // A question the model for complex questions has not yet answered
        // is not judged, so is counted with those not yet asked.
        _phoneTextNotInTree = shown;
        _textNotInTreeUnanswered = problem is not null ? 0 : shown.Unanswered;
        UpdateAskChecksModelButton();
        ShowPhoneScreenRows();
    }

    private async Task ApplyKeptHeadingNotShownAnswersAsync()
    {
        if (_phoneHeadingNotShownRules is not { } rules)
        {
            UpdateAskChecksModelButton();
            return;
        }

        if (rules.Questions.Count == 0)
        {
            _headingNotShownUnanswered = 0;
            UpdateAskChecksModelButton();
            return;
        }

        var name = _openProjectModels?.VisionChecks;
        var problem = name is null
            ? $"the project's settings choose no model for {ModelUses.Name(ModelUses.VisionChecks)}"
            : ProjectModels.Problem(ProjectChoices, ModelUses.VisionChecks, name);
        var none = new Dictionary<PhoneHeadingQuestion, PhoneHeadingNotShownAnswer>();
        PhoneHeadingNotShownRun shown;
        if (problem is not null || AnalysisCatalogOrNull is not { } catalog || ProjectChoices.Profile(name!) is not { } profile)
        {
            shown = rules.Judged(none, AndroidHeadingNotShown.NotAsked(rules.Questions.Count, problem ?? "the analysis catalog is not open"));
        }
        else
        {
            try
            {
                if (_headingNotShownRequests is not { } made || !ReferenceEquals(made.Rules, rules))
                {
                    var requests = await Task.Run(() => AndroidHeadingNotShown.Requests(rules, PhonePictures.Load));
                    made = (rules, requests);
                    _headingNotShownRequests = made;
                }

                var keys = made.Requests.SelectMany(request => new[] { request.StepKey(1), request.StepKey(2) }).ToList();
                var kept = await catalog.AcceptedAnswersAsync(keys, name!, profile.Model, CancellationToken.None);
                var (complexKept, complexName) = await KeptComplexAnswersAsync(catalog, made.Requests.Select(request => (Func<int, string>)request.StepKey));
                shown = AndroidHeadingNotShown.FromKept(rules, made.Requests, kept, name!, complexKept, complexName);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                shown = rules.Judged(none, $"the model's kept answers could not be read: {exception.Message}");
            }
        }

        // The recording changed while the answers were read.
        if (!ReferenceEquals(rules, _phoneHeadingNotShownRules))
        {
            return;
        }

        // A question waiting for the model for complex questions, to settle
        // or to confirm its answer, is not judged, so is counted with those
        // not yet asked.
        _phoneHeadingNotShown = shown;
        _headingNotShownUnanswered = problem is not null ? 0 : shown.Unanswered;
        UpdateAskChecksModelButton();
        ShowPhoneScreenRows();
    }

    private async Task ApplyKeptControlNameAnswersAsync()
    {
        if (_phoneControlNamesRules is not { } rules)
        {
            UpdateAskChecksModelButton();
            return;
        }

        if (rules.Questions.Count == 0)
        {
            _controlNamesUnanswered = 0;
            UpdateAskChecksModelButton();
            return;
        }

        var name = _openProjectModels?.VisionChecks;
        var problem = name is null
            ? $"the project's settings choose no model for {ModelUses.Name(ModelUses.VisionChecks)}"
            : ProjectModels.Problem(ProjectChoices, ModelUses.VisionChecks, name);
        var none = new Dictionary<PhoneControlNameQuestion, PhoneControlNameAnswer>();
        PhoneControlNameRun shown;
        if (problem is not null || AnalysisCatalogOrNull is not { } catalog || ProjectChoices.Profile(name!) is not { } profile)
        {
            shown = rules.Judged(none, AndroidControlNames.NotAsked(rules.Questions.Count, problem ?? "the analysis catalog is not open"));
        }
        else
        {
            try
            {
                if (_controlNamesRequests is not { } made || !ReferenceEquals(made.Rules, rules))
                {
                    var requests = await Task.Run(() => AndroidControlNames.Requests(rules, PhonePictures.Load));
                    made = (rules, requests);
                    _controlNamesRequests = made;
                }

                var keys = made.Requests.SelectMany(request => new[] { request.StepKey(1), request.StepKey(2) }).ToList();
                var kept = await catalog.AcceptedAnswersAsync(keys, name!, profile.Model, CancellationToken.None);
                var (complexKept, complexName) = await KeptComplexAnswersAsync(catalog, made.Requests.Select(request => (Func<int, string>)request.StepKey));
                shown = AndroidControlNames.FromKept(rules, made.Requests, kept, name!, complexKept, complexName);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                shown = rules.Judged(none, $"the model's kept answers could not be read: {exception.Message}");
            }
        }

        // The recording changed while the answers were read.
        if (!ReferenceEquals(rules, _phoneControlNamesRules))
        {
            return;
        }

        // A question waiting for the model for complex questions, to settle
        // or to confirm its answer, is not judged, so is counted with those
        // not yet asked.
        _phoneControlNames = shown;
        _controlNamesUnanswered = problem is not null ? 0 : shown.Unanswered;
        UpdateAskChecksModelButton();
        ShowPhoneScreenRows();
    }

    private async Task ApplyKeptChartColourAnswersAsync()
    {
        if (_phoneChartColoursRules is not { } rules)
        {
            UpdateAskChecksModelButton();
            return;
        }

        if (rules.Questions.Count == 0)
        {
            _chartColoursUnanswered = 0;
            UpdateAskChecksModelButton();
            return;
        }

        var name = _openProjectModels?.VisionChecks;
        var problem = name is null
            ? $"the project's settings choose no model for {ModelUses.Name(ModelUses.VisionChecks)}"
            : ProjectModels.Problem(ProjectChoices, ModelUses.VisionChecks, name);
        var none = new Dictionary<PhoneChartQuestion, PhoneChartAnswer>();
        PhoneChartColourRun shown;
        if (problem is not null || AnalysisCatalogOrNull is not { } catalog || ProjectChoices.Profile(name!) is not { } profile)
        {
            shown = rules.Judged(none, AndroidChartColours.NotAsked(rules.Questions.Count, problem ?? "the analysis catalog is not open"));
        }
        else
        {
            try
            {
                if (_chartColoursRequests is not { } made || !ReferenceEquals(made.Rules, rules))
                {
                    var requests = await Task.Run(() => AndroidChartColours.Requests(rules, PhonePictures.Load));
                    made = (rules, requests);
                    _chartColoursRequests = made;
                }

                var keys = made.Requests.SelectMany(request => new[] { request.StepKey(1), request.StepKey(2) }).ToList();
                var kept = await catalog.AcceptedAnswersAsync(keys, name!, profile.Model, CancellationToken.None);
                var (complexKept, complexName) = await KeptComplexAnswersAsync(catalog, made.Requests.Select(request => (Func<int, string>)request.StepKey));
                shown = AndroidChartColours.FromKept(rules, made.Requests, kept, name!, complexKept, complexName);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                shown = rules.Judged(none, $"the model's kept answers could not be read: {exception.Message}");
            }
        }

        // The recording changed while the answers were read.
        if (!ReferenceEquals(rules, _phoneChartColoursRules))
        {
            return;
        }

        // A question waiting for the model for complex questions, to settle
        // or to confirm its answer, is not judged, so is counted with those
        // not yet asked.
        _phoneChartColours = shown;
        _chartColoursUnanswered = problem is not null ? 0 : shown.Unanswered;
        UpdateAskChecksModelButton();
        ShowPhoneScreenRows();
    }

    private async Task ApplyKeptHeadingSectionAnswersAsync()
    {
        if (_phoneHeadingSectionsMeasured is not { } measured)
        {
            UpdateAskChecksModelButton();
            return;
        }

        // The rules again, with the text the check of text shown as a
        // heading now judges headings (rule 2), where its answers changed.
        if (!ReferenceEquals(_phoneHeadingSectionsLook, _phoneHeadingLook) || _phoneHeadingSectionsRules is null)
        {
            _phoneHeadingSectionsLook = _phoneHeadingLook;
            _phoneHeadingSectionsRules = measured.WithHeadingLook(_phoneHeadingLook);
        }

        var rules = _phoneHeadingSectionsRules;
        if (rules.Questions.Count == 0)
        {
            _phoneHeadingSections = rules;
            _sectionTextUnanswered = 0;
            _sectionVisionUnanswered = 0;
            UpdateAskChecksModelButton();
            ShowPhoneScreenRows();
            return;
        }

        string? Problem(string use, string? name, int questions) =>
            questions == 0
                ? null
                : name is null
                    ? $"the project's settings choose no model for {ModelUses.Name(use)}"
                    : ProjectModels.Problem(ProjectChoices, use, name);
        var textName = _openProjectModels?.TextChecks;
        var visionName = _openProjectModels?.VisionChecks;
        var catalog = AnalysisCatalogOrNull;
        var textProblem = Problem(ModelUses.TextChecks, textName, rules.TextQuestions) ?? (catalog is null && rules.TextQuestions > 0 ? "the analysis catalog is not open" : null);
        var visionProblem = Problem(ModelUses.VisionChecks, visionName, rules.PicturedQuestions) ?? (catalog is null && rules.PicturedQuestions > 0 ? "the analysis catalog is not open" : null);
        var none = new Dictionary<PhoneHeadingSectionQuestion, PhoneHeadingSectionAnswer>();
        PhoneHeadingSectionRun shown;
        if ((rules.TextQuestions == 0 || textProblem is not null) && (rules.PicturedQuestions == 0 || visionProblem is not null))
        {
            shown = rules.Judged(none, AndroidHeadingSections.NotAsked(rules, textProblem, visionProblem));
        }
        else
        {
            try
            {
                if (_headingSectionRequests is not { } made || !ReferenceEquals(made.Rules, rules))
                {
                    var requests = await Task.Run(() => AndroidHeadingSections.Requests(rules, PhonePictures.Load));
                    made = (rules, requests);
                    _headingSectionRequests = made;
                }

                async Task<ScreenTitleKept?> KeptAsync(string template, string? name, string? problem, (string Name, ModelProfile Profile)? complex)
                {
                    if (problem is not null || name is null || ProjectChoices.Profile(name) is not { } profile)
                    {
                        return null;
                    }

                    var signed = await catalog!.SignedAnswersAsync(
                        template, AndroidHeadingSections.TemplateVersion, name, profile.Model, _openProjectId, _analysisRecordingId, CancellationToken.None);
                    var complexSigned = complex is { } other
                        ? await catalog.SignedAnswersAsync(
                            template, AndroidHeadingSections.TemplateVersion, other.Name, other.Profile.Model, _openProjectId, _analysisRecordingId, CancellationToken.None)
                        : null;
                    return new ScreenTitleKept(signed, name, complexSigned, complex?.Name);
                }

                var text = await KeptAsync(AndroidHeadingSections.TextTemplate, textName, textProblem, ComplexTextModel());
                var vision = await KeptAsync(AndroidHeadingSections.PicturedTemplate, visionName, visionProblem, ComplexVisionModel());
                shown = AndroidHeadingSections.FromKept(rules, made.Requests, text, vision, textProblem, visionProblem);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or IOException or ObjectDisposedException)
            {
                shown = rules.Judged(none, $"the model's kept answers could not be read: {exception.Message}");
            }
        }

        // The recording changed, or the rules were run again, while the
        // answers were read.
        if (!ReferenceEquals(rules, _phoneHeadingSectionsRules))
        {
            return;
        }

        // A question the model for complex questions has not yet answered
        // is not judged, so is counted with those not yet asked.
        _phoneHeadingSections = shown;
        _sectionTextUnanswered = textProblem is not null ? 0 : shown.Questions.Count(question => !question.Pictured && !shown.Answers.ContainsKey(question));
        _sectionVisionUnanswered = visionProblem is not null ? 0 : shown.Questions.Count(question => question.Pictured && !shown.Answers.ContainsKey(question));
        UpdateAskChecksModelButton();
        ShowPhoneScreenRows();
    }

    // The project's model for complex questions of text-based checks, as
    // for vision-based checks below; null where there is none the project
    // can use.
    private (string Name, ModelProfile Profile)? ComplexTextModel() =>
        _openProjectModels is { } models &&
        CheckQuestions.EscalationProfile(models, ModelUses.TextChecks) is { } name &&
        ProjectModels.Problem(ProjectChoices, ModelUses.ComplexTextChecks, name) is null &&
        ProjectChoices.Profile(name) is { } profile
            ? (name, profile)
            : null;

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
    // complex questions, that one too (owner, 2026-11-17). A screen title
    // asked with only text is for the model for text-based checks, the
    // same way.
    private void UpdateAskChecksModelButton()
    {
        var waiting = _analysisTasks?.Any(task => task.Kind == AndroidModelChecksKind.Name &&
            task.State is AnalysisTaskStates.Queued or AnalysisTaskStates.Running) == true;
        var models = _openProjectModels;
        var vision = _imagesOfTextUnanswered + _headingLookUnanswered + _imageAlternativeUnanswered + _describedImagesUnanswered + _titleVisionUnanswered + _decorativeUnanswered + _sectionVisionUnanswered +
            _textNotInTreeUnanswered + _headingNotShownUnanswered + _controlNamesUnanswered + _chartColoursUnanswered > 0 &&
            models is { VisionChecks: not null } && models.ChecksProblem(ProjectChoices, ModelUses.VisionChecks) is null;
        var text = _titleTextUnanswered + _sectionTextUnanswered > 0 &&
            models is { TextChecks: not null } && models.ChecksProblem(ProjectChoices, ModelUses.TextChecks) is null;
        AskChecksModelButton.IsEnabled = (vision || text) && !waiting && _analysisRecordingId is not null;
    }

    // The toasts then say when the task waits, starts and ends
    // (ShowModelChecksToasts); where it could not be queued, a toast says
    // why, as the status line saying so is in the Analysis window.
    private async void AskChecksModelButton_Click(object sender, RoutedEventArgs e)
    {
        AskChecksModelButton.IsEnabled = false;
        if (!await QueueAsync(AndroidModelChecksKind.Name, announce: false))
        {
            ShowChecksToast("Model checks were not queued. " + AnalysisStatusTextBlock.Text);
        }

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

        // The density first in the list (android-checks.md, step 2): the checks turn
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
                _phoneLabelInName is not null || _phoneHeadingLook is not null || _phoneImageAlternative is not null ||
                _phoneDescribedImages is not null || _phoneScreenTitles is not null || _phoneDecorativeImages is not null ||
                _phoneHeadingSections is not null || _phoneTextNotInTree is not null || _phoneHeadingNotShown is not null ||
                _phoneControlNames is not null || _phoneColourOnly is not null || _phoneChartColours is not null)
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

        if (_phoneDescribedImages is { SnapshotsRead: > 0 } described)
        {
            var findings = described.For(visit);
            var count = described.ImagesIn(visit);
            foreach (var check in new[] { PhoneCheckNames.IconContradictsName, PhoneCheckNames.DescriptionNotMatchingImage })
            {
                var title = PhoneCheckNames.Title(check);
                var ofCheck = findings.Where(finding => finding.Check == check).ToList();
                var text = !described.Read(visit)
                    ? $"{title}: not checked, as OCR read no picture of this visit."
                    : count == 0
                        ? $"{title}: no described images in this visit."
                        : ofCheck.Count == 0
                            ? $"{title}: no findings, from {Count(count, "described image")}."
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
                    AddCheckRows(ofCheck, $", from {Count(count, "described image")}");
                }
            }
        }

        if (_phoneScreenTitles is { SnapshotsRead: > 0 } titles)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.TitleNotDescriptive);
            var findings = titles.For(visit);
            if (findings.Count > 0)
            {
                AddCheckRows(findings);
            }
            else
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: {ScreenTitleState(titles, visit)}", shownAt, null)
                {
                    IsCheck = true,
                });
            }
        }

        if (_phoneDecorativeImages is { SnapshotsRead: > 0 } decorative)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.DecorativeAnnounced);
            var findings = decorative.For(visit);
            var count = decorative.ImagesIn(visit);
            var text = !decorative.Read(visit)
                ? $"{title}: not checked, as OCR read no picture of this visit."
                : count == 0
                    ? $"{title}: no described images in this visit."
                    : findings.Count == 0
                        ? $"{title}: {DecorativeState(decorative, visit)}"
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
                AddCheckRows(findings, $", from {Count(decorative.AskedIn(visit), "image")} asked about");
            }
        }

        if (_phoneHeadingSections is { SnapshotsRead: > 0 } sections)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.HeadingNotDescriptive);
            var findings = sections.For(visit);
            if (findings.Count > 0)
            {
                AddCheckRows(findings, $", from {Count(sections.Questions.Count(question => question.Visit == visit), "heading")} asked about");
            }
            else
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: {HeadingSectionState(sections, visit)}", shownAt, null)
                {
                    IsCheck = true,
                });
            }
        }

        if (_phoneTextNotInTree is { SnapshotsRead: > 0 } notInTree)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.TextNotInTree);
            var findings = notInTree.For(visit);
            if (findings.Count > 0)
            {
                AddCheckRows(findings, $", from {Count(notInTree.Questions.Count(question => question.Visit == visit), "line")} asked about");
            }
            else
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: {TextNotInTreeState(notInTree, visit)}", shownAt, null)
                {
                    IsCheck = true,
                });
            }
        }

        if (_phoneHeadingNotShown is { SnapshotsRead: > 0 } notShown)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.HeadingNotShown);
            var findings = notShown.For(visit);
            if (findings.Count > 0)
            {
                AddCheckRows(findings, $", from {Count(notShown.Questions.Count(question => question.Visit == visit), "marked heading")} asked about");
            }
            else
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: {HeadingNotShownState(notShown, visit)}", shownAt, null)
                {
                    IsCheck = true,
                });
            }
        }

        if (_phoneControlNames is { SnapshotsRead: > 0 } names)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.ControlNameNotDescriptive);
            var findings = names.For(visit);
            if (findings.Count > 0)
            {
                AddCheckRows(findings, $", from {Count(names.In(visit).Count(), "control name")} in this visit");
            }
            else
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{title}: {ControlNamesState(names, visit)}", shownAt, null)
                {
                    IsCheck = true,
                });
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

        if (_phoneColourOnly is { } colour)
        {
            AddColourOnlyRows(colour, visit, shownAt);
        }

        if (_phoneChartColours is { SnapshotsRead: > 0 } charts)
        {
            var findings = charts.For(visit);
            if (findings.Count > 0)
            {
                AddCheckRows(findings, $", from {Count(charts.In(visit).Count(), "item")} with two or more colours in this visit");
            }
            else
            {
                _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{PhoneCheckNames.Title(PhoneCheckNames.ChartOnlyByColour)}: {ChartColoursState(charts, visit)}", shownAt, null)
                {
                    IsCheck = true,
                });
            }
        }
    }

    // The rows of the two checks of use of colour (android-checks.md, step
    // 29): each check's findings, or a row saying why it has none.
    private void AddColourOnlyRows(PhoneColourOnlyRun colour, PhoneVisit visit, long shownAt)
    {
        var findings = colour.For(visit);
        var links = findings.Where(finding => finding.Check == PhoneCheckNames.LinkOnlyByColour).ToList();
        var selected = findings.Where(finding => finding.Check == PhoneCheckNames.SelectedOnlyByColour).ToList();
        if (links.Count > 0)
        {
            AddCheckRows(links, $", from {Count(colour.LinksIn(visit), "link")} measured");
        }
        else
        {
            var state = colour.LinksNotRecordedIn(visit)
                ? "not checked, as the companion app did not record the links in the items' text before protocol 8."
                : colour.LinksIn(visit) == 0
                    ? "no link measured in this visit."
                    : $"no findings, from {Count(colour.LinksIn(visit), "link")} measured.";
            _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{PhoneCheckNames.Title(PhoneCheckNames.LinkOnlyByColour)}: {state}", shownAt, null)
            {
                IsCheck = true,
            });
        }

        if (selected.Count > 0)
        {
            AddCheckRows(selected, $", from {Count(colour.SelectedIn(visit), "item")} marked selected measured");
        }
        else
        {
            var state = colour.SelectedIn(visit) == 0
                ? "no item marked selected measured in this visit."
                : $"no findings, from {Count(colour.SelectedIn(visit), "item")} marked selected measured.";
            _phoneScreenRows.Add(new PhoneScreenRowView(3, $"{PhoneCheckNames.Title(PhoneCheckNames.SelectedOnlyByColour)}: {state}", shownAt, null)
            {
                IsCheck = true,
            });
        }
    }

    // A row for each check of the findings, saying how many at each level,
    // then a row for each finding.
    // What became of a visit's screen in the screen title check, where it
    // made no finding: each screen is checked once, on its first visit OCR
    // read a picture of (rule 2).
    private static string ScreenTitleState(PhoneScreenTitleRun titles, PhoneVisit visit)
    {
        if (titles.Of(visit.Screen) is not { } of)
        {
            return "not checked, as OCR read no picture of this screen.";
        }

        if (of.Visit != visit)
        {
            return $"checked once for the screen, on visit {of.Visit.Number.ToString(CultureInfo.CurrentCulture)}.";
        }

        switch (of.State)
        {
            case PhoneScreenTitleState.Untitled:
                return "not checked, as the screen has no title.";
            case PhoneScreenTitleState.Matched:
                var match = of.Match switch
                {
                    PhoneTitleMatch.Heading => "a heading on the screen",
                    PhoneTitleMatch.OcrLine => "a line of the text OCR read on the screen",
                    _ => "all a screen reader says for an item on the screen",
                };
                return $"no finding, as the title \"{of.Title}\" is {match}.";
        }

        var question = titles.Questions.FirstOrDefault(item => item.Visit == visit);
        if (question is not null && titles.Answers.TryGetValue(question, out var answer))
        {
            return $"no finding, as {answer.Inference.Text}.";
        }

        return $"the title \"{of.Title}\" is for the model for {(question?.Pictured == true ? "vision-based" : "text-based")} checks, and not yet judged.";
    }

    // What became of a visit's headings in the check of a heading's
    // section, where it made no finding: each screen's headings are
    // checked once, on the visit they were first seen on.
    private static string HeadingSectionState(PhoneHeadingSectionRun sections, PhoneVisit visit)
    {
        if (!sections.Read(visit))
        {
            return "not checked, as OCR read no picture of this visit.";
        }

        var entries = sections.In(visit).ToList();
        if (entries.Count == 0)
        {
            return "no headings first seen on this visit; a screen's headings are checked once, on the visit they are first seen on.";
        }

        int Of(PhoneHeadingSectionState state) => entries.Count(entry => entry.State == state);
        var asked = sections.Questions.Where(question => question.Visit == visit).ToList();
        var described = asked.Count(question => sections.Answers.ContainsKey(question));
        var parts = new List<string>();
        if (described > 0)
        {
            parts.Add($"{Count(described, "heading")} judged to describe {(described == 1 ? "its section" : "their sections")}, not confirmed");
        }

        if (asked.Count - described is > 0 and var waiting)
        {
            parts.Add($"{Count(waiting, "heading")} for the project's models, not yet judged");
        }

        if (Of(PhoneHeadingSectionState.ScreenTitle) is > 0 and var titles)
        {
            parts.Add($"{Count(titles, "heading")} that {(titles == 1 ? "is the screen's title" : "are the screen's title")}, left to the screen title check");
        }

        if (Of(PhoneHeadingSectionState.Empty) is > 0 and var empty)
        {
            parts.Add($"{Count(empty, "heading")} with nothing under {(empty == 1 ? "it" : "them")} on screen or in the tree");
        }

        if (Of(PhoneHeadingSectionState.NotJudgedHeading) is > 0 and var unjudged)
        {
            parts.Add($"{Count(unjudged, "piece")} of text shown as a heading not checked, as not judged a heading");
        }

        return $"no findings, from {Count(entries.Count, "heading")}: {string.Join("; ", parts)}.";
    }

    // What became of a visit's lines in the check of text not in the
    // tree, where it made no finding: each line is checked once a visit,
    // from the first picture in step with the tree it is seen on.
    private static string TextNotInTreeState(PhoneTextNotInTreeRun notInTree, PhoneVisit visit)
    {
        if (!notInTree.Read(visit))
        {
            return "not checked, as OCR read no picture of this visit.";
        }

        var entries = notInTree.In(visit).ToList();
        if (entries.Count == 0)
        {
            return "no findings: OCR read no text that no item holds on the pictures of this visit in step with the tree.";
        }

        int Of(PhoneTextNotInTreeState state) => entries.Count(entry => entry.State == state);
        var asked = notInTree.Questions.Where(question => question.Visit == visit).ToList();
        var answered = asked.Where(question => notInTree.Answers.ContainsKey(question)).Select(question => notInTree.Answers[question].Outcome).ToList();
        var parts = new List<string>();
        void Add(PhoneTextNotInTreeState state, string what)
        {
            if (Of(state) is > 0 and var count)
            {
                parts.Add($"{Count(count, "line")} {what}");
            }
        }

        Add(PhoneTextNotInTreeState.Caption, "of TalkBack's caption");
        Add(PhoneTextNotInTreeState.Image, "inside images, left to the image of text check");
        Add(PhoneTextNotInTreeState.Short, "of fewer than 3 letters or digits");
        Add(PhoneTextNotInTreeState.HeldElsewhere, "held by an item elsewhere on the screen");
        Add(PhoneTextNotInTreeState.Misread, "taken as a small item's own text misread, left to the label in name check");
        Add(PhoneTextNotInTreeState.OtherSnapshot, "held by an item in another snapshot of this visit");
        if (answered.Count(outcome => outcome == PhoneTextNotInTreeOutcome.Covered) is > 0 and var covered)
        {
            parts.Add($"{Count(covered, "line")} judged covered, behind a dialog, sheet or dimmed layer, not confirmed");
        }

        if (answered.Count(outcome => outcome == PhoneTextNotInTreeOutcome.NotText) is > 0 and var notText)
        {
            parts.Add($"{Count(notText, "line")} judged not text, not confirmed");
        }

        if (asked.Count - answered.Count is > 0 and var waiting)
        {
            parts.Add($"{Count(waiting, "line")} for the model for vision-based checks, not yet judged");
        }

        return $"no findings, from {Count(entries.Count, "line")} OCR read in no item's text: {string.Join("; ", parts)}.";
    }

    // What became of a visit's marked headings in the check of a marked
    // heading not shown as one, where it made no finding: how many were
    // left out by rule 3, judged shown as headings, or not yet judged.
    private static string HeadingNotShownState(PhoneHeadingNotShownRun notShown, PhoneVisit visit)
    {
        if (!notShown.Read(visit))
        {
            return "not checked, as OCR read no picture of this visit.";
        }

        var entries = notShown.In(visit).ToList();
        if (entries.Count == 0)
        {
            return "no findings: OCR placed no marked heading on the pictures of this visit.";
        }

        int Of(PhoneHeadingNotShownState state) => entries.Count(entry => entry.State == state);
        var asked = notShown.Questions.Where(question => question.Visit == visit).ToList();
        var answered = asked.Where(question => notShown.Answers.ContainsKey(question)).Select(question => notShown.Answers[question].Outcome).ToList();
        var parts = new List<string>();
        void Add(PhoneHeadingNotShownState state, string what)
        {
            if (Of(state) is > 0 and var count)
            {
                parts.Add($"{Count(count, "marked heading")} {what}");
            }
        }

        Add(PhoneHeadingNotShownState.OcrDiffers, "left out, as OCR's text differs from the app's");
        Add(PhoneHeadingNotShownState.Covered, "left out, as another line covers it");
        Add(PhoneHeadingNotShownState.LineGoesOn, "left out, as OCR's line goes on past it");
        if (answered.Count(outcome => outcome == PhoneHeadingNotShownOutcome.Shown) is > 0 and var shown)
        {
            parts.Add($"{Count(shown, "marked heading")} judged shown as {(shown == 1 ? "a heading" : "headings")}, not confirmed");
        }

        if (asked.Count - answered.Count is > 0 and var waiting)
        {
            parts.Add($"{Count(waiting, "marked heading")} for the model for vision-based checks, not yet judged");
        }

        return $"no findings, from {Count(entries.Count, "marked heading")} placed by OCR: {string.Join("; ", parts)}.";
    }

    // What became of a visit's control names in the check of a control
    // name that does not say what the control does, where it made no
    // finding: how many were judged to say what their controls do, or not
    // yet judged.
    private static string ControlNamesState(PhoneControlNameRun names, PhoneVisit visit)
    {
        if (!names.Read(visit))
        {
            return "not checked, as OCR read no picture of this visit.";
        }

        var entries = names.In(visit).ToList();
        if (entries.Count == 0)
        {
            return "no findings: a screen reader stops on no named control on the pictures of this visit.";
        }

        var described = entries.Count(entry => entry.Question is { } question && names.Answers.TryGetValue(question, out var answer) &&
            answer.Outcome == PhoneControlNameOutcome.Describes);
        var waiting = entries.Count(entry => entry.Question is { } question && !names.Answers.ContainsKey(question));
        var parts = new List<string>();
        if (described > 0)
        {
            parts.Add($"{Count(described, "control name")} judged to say what the control does, not confirmed");
        }

        if (waiting > 0)
        {
            parts.Add($"{Count(waiting, "control name")} for the model for vision-based checks, not yet judged");
        }

        return parts.Count == 0
            ? $"no findings, from {Count(entries.Count, "control name")}."
            : $"no findings, from {Count(entries.Count, "control name")}: {string.Join("; ", parts)}.";
    }

    // What became of a visit's items in the check of a chart that relies on
    // colour, where it made no finding: how many were judged not to be
    // charts or to show what their colours stand for another way too, or
    // not yet judged.
    private static string ChartColoursState(PhoneChartColourRun charts, PhoneVisit visit)
    {
        if (!charts.Read(visit))
        {
            return "not checked, as no screenshot of this visit was measured.";
        }

        var entries = charts.In(visit).ToList();
        if (entries.Count == 0)
        {
            return "no findings: no item with no text of its own holds two or more colours on the screenshots of this visit.";
        }

        int Of(PhoneChartOutcome outcome) => entries.Count(entry => charts.Answers.TryGetValue(entry.Question, out var answer) && answer.Outcome == outcome);
        var notCharts = Of(PhoneChartOutcome.NotAChart);
        var notOnly = Of(PhoneChartOutcome.NotOnlyColour);
        var waiting = entries.Count(entry => !charts.Answers.ContainsKey(entry.Question));
        var parts = new List<string>();
        if (notCharts > 0)
        {
            parts.Add($"{Count(notCharts, "item")} judged not to be charts, not confirmed");
        }

        if (notOnly > 0)
        {
            parts.Add($"{Count(notOnly, "item")} judged to show what their colours stand for another way too, not confirmed");
        }

        if (waiting > 0)
        {
            parts.Add($"{Count(waiting, "item")} for the model for vision-based checks, not yet judged");
        }

        return parts.Count == 0
            ? $"no findings, from {Count(entries.Count, "item")}."
            : $"no findings, from {Count(entries.Count, "item")}: {string.Join("; ", parts)}.";
    }

    // What became of a visit's described images in the decorative image
    // check, where it made no finding: how many were not said, were names
    // of controls or of the items holding them, and were judged or not.
    private static string DecorativeState(PhoneDecorativeImageRun decorative, PhoneVisit visit)
    {
        var images = decorative.In(visit).ToList();
        int Of(PhoneDecorativeState state) => images.Count(image => image.State == state);
        var asked = images.Where(image => image.State == PhoneDecorativeState.Asked).Select(image => image.Image).ToList();
        var informative = asked.Count(image => decorative.Answers.ContainsKey(image));
        var parts = new List<string>();
        if (Of(PhoneDecorativeState.NotSpoken) is > 0 and var notSpoken)
        {
            parts.Add($"{Count(notSpoken, "image")} not said by a screen reader");
        }

        if (Of(PhoneDecorativeState.Control) is > 0 and var controls)
        {
            parts.Add($"{Count(controls, "image")} that {(controls == 1 ? "is a control" : "are controls")}, so {(controls == 1 ? "its description is the control's name" : "their descriptions are the controls' names")}");
        }

        if (Of(PhoneDecorativeState.ItemName) is > 0 and var names)
        {
            parts.Add($"{Count(names, "image")} whose description is all the item holding {(names == 1 ? "it" : "them")} says, so that item's name");
        }

        if (informative > 0)
        {
            parts.Add($"{Count(informative, "image")} judged informative, not confirmed");
        }

        if (asked.Count - informative is > 0 and var waiting)
        {
            parts.Add($"{Count(waiting, "image")} for the model for vision-based checks, not yet judged");
        }

        return $"no findings, from {Count(images.Count, "described image")}: {string.Join("; ", parts)}.";
    }

    private void AddCheckRows(IReadOnlyList<PhoneFinding> findings, string measured = "", int indent = 3)
    {
        foreach (var check in findings.GroupBy(finding => finding.Check))
        {
            _phoneScreenRows.Add(new PhoneScreenRowView(
                indent,
                $"{PhoneCheckNames.Title(check.Key)}: {PhoneChecksSummary.Levels(check)}{measured}",
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
            RefreshChecksSummary();
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
            : $"{totals} Visit {visit.Number}, to {visit.Screen.Label}, at the playhead.") + ChecksState();
        RefreshChecksSummary();
    }

    // Under the Findings tab's buttons, only what changes with the
    // playhead or while the checks run; the counts are in the Summary tab
    // (owner, 2026-11-18: the summary "needs to be its own tab").
    private string ChecksState()
    {
        var state = _phoneChecksProblem is not null
            ? " The checks of the tree could not be run; the Summary tab says why."
            : _phoneChecks is null ? " Running the checks of the tree." : "";
        state += _phoneContrastProblem is not null
            ? " The contrast checks, of text and non-text, could not be run; the Summary tab says why."
            : _phoneContrast is null || _phoneTextContrast is null ? " Measuring the contrast of text and non-text." : "";
        if (_shownFinding is { } shown)
        {
            state += string.Create(
                CultureInfo.CurrentCulture,
                $" Showing time {_shownFindingTime + 1:N0} of {shown.Times.Count:N0} of the chosen finding, outlined on the phone's picture while its items are shown.");
        }

        return state;
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

using System.Text.Json;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// Every Android check run on a phone recording, as the Checks tab runs
/// them (MainWindow.PhoneScreens.cs): the checks from the tree and the
/// recording's events, then the colours measured on each settled snapshot's
/// picture, with OCR finding the text, and the checks judged from that
/// measurement. No model is used here; the questions of the images-of-text
/// check, the check of text shown as a heading, the check of images with
/// no text alternative, the icon and description checks and the screen
/// title check are asked after, where a model may be used
/// (<see cref="AndroidImagesOfText"/>, <see cref="AndroidHeadingLook"/>,
/// <see cref="AndroidImageAlternative"/>, <see cref="AndroidDescribedImages"/>,
/// <see cref="AndroidScreenTitles"/>; android-report.md, "The audit").
/// </summary>
public sealed class AndroidAudit
{
    private AndroidAudit(PhoneCheckRun tree, PhoneAuditLevel level, int holdSeconds)
    {
        Tree = tree;
        Level = level;
        HoldSeconds = holdSeconds;
    }

    // A copy of every check, for the With methods to change one.
    private AndroidAudit(AndroidAudit other)
        : this(other.Tree, other.Level, other.HoldSeconds)
    {
        Measured = other.Measured;
        MeasureProblem = other.MeasureProblem;
        TextContrast = other.TextContrast;
        ImagesOfText = other.ImagesOfText;
        NonTextContrast = other.NonTextContrast;
        LabelInName = other.LabelInName;
        HeadingLook = other.HeadingLook;
        ImageAlternative = other.ImageAlternative;
        DescribedImages = other.DescribedImages;
        ScreenTitles = other.ScreenTitles;
    }

    public PhoneCheckRun Tree { get; }

    public PhoneAuditLevel Level { get; }

    /// <summary>How long the phone had to be held turned for the orientation check, in seconds.</summary>
    public int HoldSeconds { get; }

    /// <summary>The colour measurement; null when it could not be made.</summary>
    public PhoneContrastRun? Measured { get; private init; }

    /// <summary>Why the colour measurement could not be made, when it could not.</summary>
    public string? MeasureProblem { get; private init; }

    public PhoneTextContrastRun? TextContrast { get; private init; }

    public PhoneImagesOfTextRun? ImagesOfText { get; private init; }

    public PhoneNonTextContrastRun? NonTextContrast { get; private init; }

    public PhoneLabelInNameRun? LabelInName { get; private init; }

    /// <summary>Text shown as a heading but not marked as one (android-checks.md, step 19).</summary>
    public PhoneHeadingLookRun? HeadingLook { get; private init; }

    /// <summary>Images with no text alternative (android-checks.md, step 20).</summary>
    public PhoneImageAlternativeRun? ImageAlternative { get; private init; }

    /// <summary>Icons that contradict their controls' names, and descriptions that do not match their images (android-checks.md, step 21).</summary>
    public PhoneDescribedImageRun? DescribedImages { get; private init; }

    /// <summary>Screen titles that do not describe their screens (android-checks.md, step 22).</summary>
    public PhoneScreenTitleRun? ScreenTitles { get; private init; }

    /// <summary>The findings of the whole app, not of one visit, such as no live region in the app.</summary>
    public IReadOnlyList<PhoneFinding> AppFindings => Tree.AppFindings;

    /// <summary>The OCR used on the pictures, each once, such as "Windows OCR, en-CA".</summary>
    public IReadOnlyList<string> OcrEngines =>
        Measured is null ? [] : [.. Measured.Snapshots.Select(snapshot => snapshot.Engine).OfType<string>().Where(engine => engine.Length > 0).Distinct(StringComparer.Ordinal)];

    /// <summary>Every finding of a visit, from every check.</summary>
    public IReadOnlyList<PhoneFinding> For(PhoneVisit visit) =>
    [
        .. Tree.For(visit),
        .. TextContrast?.For(visit) ?? [],
        .. NonTextContrast?.For(visit) ?? [],
        .. ImagesOfText?.For(visit) ?? [],
        .. LabelInName?.For(visit) ?? [],
        .. HeadingLook?.For(visit) ?? [],
        .. ImageAlternative?.For(visit) ?? [],
        .. DescribedImages?.For(visit) ?? [],
        .. ScreenTitles?.For(visit) ?? [],
    ];

    /// <summary>
    /// Whether any finding is a model's inference, so that a report says
    /// AI was used in the testing, and whether a cloud model was
    /// (android-vision-checks.md, decision 2). The icon and description
    /// checks and the screen title check give every answer, as most make
    /// no finding.
    /// </summary>
    public IReadOnlyList<PhoneInference> Inferences =>
    [
        .. (ImagesOfText?.Findings ?? []).Concat(HeadingLook?.Findings ?? []).Concat(ImageAlternative?.Findings ?? [])
            .Select(finding => finding.Inference).OfType<PhoneInference>()
            .Concat(DescribedImages?.Inferences ?? [])
            .Concat(ScreenTitles?.Inferences ?? [])
            .Where(inference => inference.Inferred),
    ];

    /// <summary>
    /// The audit with the images-of-text check judged by a model's answers
    /// (<see cref="AndroidImagesOfText"/>), every other check as it was.
    /// </summary>
    public AndroidAudit WithImagesOfText(PhoneImagesOfTextRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new(this) { ImagesOfText = run };
    }

    /// <summary>
    /// The audit with the check of text shown as a heading judged by a
    /// model's answers (<see cref="AndroidHeadingLook"/>), every other check
    /// as it was.
    /// </summary>
    public AndroidAudit WithHeadingLook(PhoneHeadingLookRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new(this) { HeadingLook = run };
    }

    /// <summary>
    /// The audit with the check of images with no text alternative judged
    /// by a model's answers (<see cref="AndroidImageAlternative"/>), every
    /// other check as it was.
    /// </summary>
    public AndroidAudit WithImageAlternative(PhoneImageAlternativeRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new(this) { ImageAlternative = run };
    }

    /// <summary>
    /// The audit with the icon and description checks judged by a model's
    /// answers (<see cref="AndroidDescribedImages"/>), every other check as
    /// it was.
    /// </summary>
    public AndroidAudit WithDescribedImages(PhoneDescribedImageRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new(this) { DescribedImages = run };
    }

    /// <summary>
    /// The audit with the screen title check judged by a model's answers
    /// (<see cref="AndroidScreenTitles"/>), every other check as it was.
    /// </summary>
    public AndroidAudit WithScreenTitles(PhoneScreenTitleRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new(this) { ScreenTitles = run };
    }

    /// <summary>Every finding, the app's first.</summary>
    public IEnumerable<PhoneFinding> All(PhoneScreenTimeline screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        return AppFindings.Concat(screens.Visits.SelectMany(For));
    }

    /// <summary>Runs every check on a recording.</summary>
    /// <param name="reader">OCR; null to use none.</param>
    public static AndroidAudit Run(
        SessionPlaybackArchive archive,
        PhoneCheckSettings settings,
        IPhoneTextReader? reader,
        Func<string, PhonePixels?> load,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(settings);
        var level = settings.AuditLevel;
        var hold = settings.OrientationHoldSeconds;
        var tree = PhoneCheckRun.Run(
            archive.PhoneScreens, archive.PhoneAccessibility, archive.PhoneDevice, archive.RecordSource, PhoneSnapshotPictures.Of(archive),
            cancellationToken, level, archive.PhoneTouches, archive.PhoneAnnouncements, archive.PhoneSettings, load, archive.PhoneInputs,
            archive.PhoneOrientation, hold);

        PhoneContrastRun? measured = null;
        string? problem = null;
        try
        {
            measured = PhoneContrastRun.Run(archive, load, reader, cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException or JsonException or UnauthorizedAccessException)
        {
            problem = exception.Message;
        }

        return Of(tree, level, hold, measured, problem, archive.PhoneScreens, archive.PhoneDevice, archive.TalkBackSpeech, archive.PhoneAccessibility);
    }

    /// <summary>The audit of a run of the tree's checks and a measurement, judged as the Checks tab judges them.</summary>
    internal static AndroidAudit Of(
        PhoneCheckRun tree,
        PhoneAuditLevel level,
        int holdSeconds,
        PhoneContrastRun? measured,
        string? problem,
        PhoneScreenTimeline screens,
        PhoneDeviceTimeline device,
        TalkBackSpeechTimeline? speech = null,
        PhoneAccessibilityTimeline? accessibility = null) =>
        new(tree, level, holdSeconds)
        {
            Measured = measured,
            MeasureProblem = problem,
            TextContrast = measured is null ? null : PhoneTextContrastRun.Run(measured, screens, device, level),
            ImagesOfText = measured is null ? null : PhoneImagesOfTextRun.Run(measured, screens, device, level, speech, accessibility),
            NonTextContrast = measured is null ? null : PhoneNonTextContrastRun.Run(measured, screens),
            LabelInName = measured is null ? null : PhoneLabelInNameRun.Run(measured, screens),
            HeadingLook = measured is null ? null : PhoneHeadingLookRun.Run(measured, screens),
            ImageAlternative = measured is null ? null : PhoneImageAlternativeRun.Run(measured, screens, device, speech, accessibility),
            DescribedImages = measured is null ? null : PhoneDescribedImageRun.Run(measured, screens, speech, accessibility),
            ScreenTitles = measured is null ? null : PhoneScreenTitleRun.Run(measured, screens),
        };
}

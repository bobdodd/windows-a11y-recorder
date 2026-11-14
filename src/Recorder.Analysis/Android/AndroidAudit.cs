using System.Text.Json;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// Every Android check run on a phone recording, as the Checks tab runs
/// them (MainWindow.PhoneScreens.cs): the checks from the tree and the
/// recording's events, then the colours measured on each settled snapshot's
/// picture, with OCR finding the text, and the checks judged from that
/// measurement. No model is used (android-report.md, "The audit").
/// </summary>
public sealed class AndroidAudit
{
    private AndroidAudit(PhoneCheckRun tree, PhoneAuditLevel level, int holdSeconds)
    {
        Tree = tree;
        Level = level;
        HoldSeconds = holdSeconds;
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
    ];

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

        return Of(tree, level, hold, measured, problem, archive.PhoneScreens, archive.PhoneDevice);
    }

    /// <summary>The audit of a run of the tree's checks and a measurement, judged as the Checks tab judges them.</summary>
    internal static AndroidAudit Of(
        PhoneCheckRun tree,
        PhoneAuditLevel level,
        int holdSeconds,
        PhoneContrastRun? measured,
        string? problem,
        PhoneScreenTimeline screens,
        PhoneDeviceTimeline device) =>
        new(tree, level, holdSeconds)
        {
            Measured = measured,
            MeasureProblem = problem,
            TextContrast = measured is null ? null : PhoneTextContrastRun.Run(measured, screens, device, level),
            ImagesOfText = measured is null ? null : PhoneImagesOfTextRun.Run(measured, screens, device, level),
            NonTextContrast = measured is null ? null : PhoneNonTextContrastRun.Run(measured, screens),
            LabelInName = measured is null ? null : PhoneLabelInNameRun.Run(measured, screens),
        };
}

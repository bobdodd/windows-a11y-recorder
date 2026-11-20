using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// The Android checks that may ask a model, taken together.
/// </summary>
public static class AndroidModelChecks
{
    /// <summary>
    /// The audit with every model check left unasked, and the reason why:
    /// what the Android report gives where the project's settings choose
    /// no model, and what the fixture tests run, so that they test the
    /// rules' findings as a report gives them (android-fixtures.md, "Rule
    /// tests"). The checks are taken in the order the report asks them
    /// (AndroidReportKinds), as heading sections read the judged heading
    /// look.
    /// </summary>
    public static AndroidAudit Unasked(AndroidAudit audit, string reason)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(reason);
        audit = AndroidImagesOfText.Unasked(audit, reason);
        audit = AndroidHeadingLook.Unasked(audit, reason);
        audit = AndroidImageAlternative.Unasked(audit, reason);
        audit = AndroidDescribedImages.Unasked(audit, reason);
        audit = AndroidScreenTitles.Unasked(audit, reason);
        audit = AndroidDecorativeImages.Unasked(audit, reason);
        audit = AndroidHeadingSections.Unasked(audit, reason);
        audit = AndroidTextNotInTree.Unasked(audit, reason);
        audit = AndroidHeadingNotShown.Unasked(audit, reason);
        audit = AndroidControlNames.Unasked(audit, reason);
        return AndroidChartColours.Unasked(audit, reason);
    }
}

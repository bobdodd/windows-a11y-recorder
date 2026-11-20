using System.Globalization;

namespace Recorder.Session;

/// <summary>
/// One section of the Checks window's Summary tab: a heading, what was
/// found, the counts behind it one to a line, and how the check judges.
/// </summary>
/// <param name="Heading">The check, or group of checks, as a heading.</param>
/// <param name="Result">What it found, such as "No findings" or "11 findings: 5 errors, 6 warnings"; null where the section reports no findings of its own.</param>
/// <param name="Points">The counts and states behind the result, each a line of its own.</param>
/// <param name="HowJudged">How the check judges, or what it does not look at; null where there is nothing to say.</param>
public sealed record PhoneSummarySection(string Heading, string? Result, IReadOnlyList<string> Points, string? HowJudged = null);

/// <summary>The runs of the checks a summary is made from, each null until it has run.</summary>
public sealed record PhoneChecksSummaryInput
{
    public PhoneAuditLevel Level { get; init; } = PhoneAuditLevel.AA;

    public PhoneCheckRun? Tree { get; init; }

    public string? TreeProblem { get; init; }

    public PhoneContrastRun? Contrast { get; init; }

    public string? ContrastProblem { get; init; }

    public PhoneTextContrastRun? TextContrast { get; init; }

    public PhoneNonTextContrastRun? NonTextContrast { get; init; }

    public PhoneImagesOfTextRun? ImagesOfText { get; init; }

    public PhoneHeadingLookRun? HeadingLook { get; init; }

    public PhoneImageAlternativeRun? ImageAlternative { get; init; }

    public PhoneDescribedImageRun? DescribedImages { get; init; }

    public PhoneScreenTitleRun? ScreenTitles { get; init; }

    public PhoneDecorativeImageRun? DecorativeImages { get; init; }

    public PhoneHeadingSectionRun? HeadingSections { get; init; }

    public PhoneTextNotInTreeRun? TextNotInTree { get; init; }

    public PhoneHeadingNotShownRun? HeadingNotShown { get; init; }

    public PhoneControlNameRun? ControlNames { get; init; }

    public PhoneLabelInNameRun? LabelInName { get; init; }

    /// <summary>Links told apart only by colour and selected states shown only by colour (android-checks.md, step 29).</summary>
    public PhoneColourOnlyRun? ColourOnly { get; init; }

    /// <summary>A chart or other graphic that relies on colour (android-checks.md, step 32).</summary>
    public PhoneChartColourRun? ChartColours { get; init; }
}

/// <summary>
/// The Checks window's summary of a recording's checks, in sections for a
/// person to read: until 2026-11-18 it was one paragraph above the list,
/// which took most of the window. The owner, 2026-11-18: "The summary
/// needs to be its own tab, and formatted for humans to read". Each
/// check, or group of checks, has its own section, with its result on a
/// line of its own and each count after it on a line of its own; an
/// overview first lists every check with findings, then the audit level. See
/// docs/architecture/android-checks.md, "The Summary tab".
/// </summary>
public static class PhoneChecksSummary
{
    /// <summary>A text field needs a border, bottom line or background at 3:1 (owner, 2026-11-16).</summary>
    public const string NonTextRules =
        "A text field needs its border, bottom line or background at 3:1 whatever text it shows, and text OCR read counts as a control's only when a word of it is in the item's text in the tree.";

    /// <summary>The target-size exceptions judged from the tree (owner, 2026-11-16).</summary>
    public const string TargetExceptions =
        "A target on a line of text passes as one in a sentence, and one with an equivalent target of the size and the same name passes, inferred from the name; " +
        "each is information for a person to judge. A target inside or around another target is a warning, as they are one target only if they do the same thing, which the tree does not say.";

    public static IReadOnlyList<PhoneSummarySection> Build(PhoneChecksSummaryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sections = new List<PhoneSummarySection> { Overview(input), Audit(input.Level) };
        sections.AddRange(TreeSections(input));
        sections.AddRange(ContrastSections(input));
        return sections;
    }

    /// <summary>"No findings", or "11 findings: 5 errors, 6 warnings".</summary>
    public static string Result(IReadOnlyCollection<PhoneFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return findings.Count == 0 ? "No findings" : $"{Count(findings.Count, "finding")}: {Levels(findings)}";
    }

    /// <summary>How many findings at each level: "6 errors, 2 not run".</summary>
    public static string Levels(IEnumerable<PhoneFinding> findings) =>
        string.Join(", ", findings
            .GroupBy(finding => finding.Level)
            .OrderBy(level => level.Key)
            .Select(level => level.Key is PhoneCheckLevel.NotRun or PhoneCheckLevel.Info
                ? F($"{level.Count():N0} {PhoneCheckNames.Word(level.Key)}")
                : Count(level.Count(), PhoneCheckNames.Word(level.Key))));

    private static PhoneSummarySection Audit(PhoneAuditLevel level) => level == PhoneAuditLevel.AAA
        ? new PhoneSummarySection(
            "Audit level AAA",
            null,
            [
                "Text contrast is held to WCAG 1.4.6.",
                "Targets are held to 44 by 44 dp (WCAG 2.5.5) as well as 2.5.8.",
                "Images of text are held to 1.4.9 as well as 1.4.5.",
            ],
            TargetExceptions + " 2.5.5's exceptions for a target sized by Android and an essential presentation are not judged.")
        : new PhoneSummarySection(
            "Audit level AA",
            null,
            [
                "Text contrast is held to WCAG 1.4.3.",
                "Targets are held to 2.5.8.",
                "Images of text are held to 1.4.5.",
            ],
            TargetExceptions);

    // Every check with findings, with how many at each level, by the
    // checks' names; while a check is still running, the result says so.
    private static PhoneSummarySection Overview(PhoneChecksSummaryInput input)
    {
        var findings = AllFindings(input).ToList();
        var running = (input.Tree is null && input.TreeProblem is null) ||
            ((input.Contrast is null || input.TextContrast is null) && input.ContrastProblem is null);
        var result = findings.Count == 0 && running ? "None yet, as the checks are still running" : Result(findings);
        var points = findings
            .GroupBy(finding => finding.Check, StringComparer.Ordinal)
            .OrderBy(check => PhoneCheckNames.Title(check.Key), StringComparer.CurrentCulture)
            .Select(check => $"{PhoneCheckNames.Title(check.Key)}: {Levels(check)}.")
            .ToList();
        if (running && findings.Count > 0)
        {
            points.Add("Some checks are still running, so there may be more.");
        }

        return new PhoneSummarySection("Findings by check", result, points);
    }

    private static IEnumerable<PhoneFinding> AllFindings(PhoneChecksSummaryInput input)
    {
        IEnumerable<PhoneFinding> none = [];
        return none
            .Concat(input.Tree?.Findings ?? [])
            .Concat(input.Tree?.AppFindings ?? [])
            .Concat(input.TextContrast?.Findings ?? [])
            .Concat(input.NonTextContrast?.Findings ?? [])
            .Concat(input.ImagesOfText?.Findings ?? [])
            .Concat(input.HeadingLook?.Findings ?? [])
            .Concat(input.ImageAlternative?.Findings ?? [])
            .Concat(input.DescribedImages?.Findings ?? [])
            .Concat(input.ScreenTitles?.Findings ?? [])
            .Concat(input.DecorativeImages?.Findings ?? [])
            .Concat(input.HeadingSections?.Findings ?? [])
            .Concat(input.TextNotInTree?.Findings ?? [])
            .Concat(input.HeadingNotShown?.Findings ?? [])
            .Concat(input.ControlNames?.Findings ?? [])
            .Concat(input.LabelInName?.Findings ?? [])
            .Concat(input.ColourOnly?.Findings ?? [])
            .Concat(input.ChartColours?.Findings ?? []);
    }

    private static List<PhoneSummarySection> TreeSections(PhoneChecksSummaryInput input)
    {
        if (input.TreeProblem is { } problem)
        {
            return [new PhoneSummarySection("Checks of the tree", "Could not be run", [problem])];
        }

        if (input.Tree is not { } run)
        {
            return [new PhoneSummarySection("Checks of the tree", "Running", [])];
        }

        var sections = new List<PhoneSummarySection>();
        var tree = new List<string>
        {
            F($"{Count(run.SnapshotsChecked, "settled snapshot")} checked, in {run.Elapsed.TotalSeconds:0.0} seconds."),
        };
        foreach (var package in run.TextFocusablePackages)
        {
            tree.Add($"In {package}, every item with text is focusable, as in an app drawn with Flutter, whose tree marks focusable what a screen reader can reach, so the tree does not show which items a keyboard reaches.");
        }

        if (run.WithoutDensity > 0)
        {
            tree.Add($"The touch target checks did not run on {Count(run.WithoutDensity, "settled snapshot")}, as no density was recorded for them.");
        }

        if (run.TargetsSeenWhole > 0)
        {
            tree.Add(F($"Touch target findings not run against the edge of a list that scrolls, dropped as another snapshot of the same visit showed the target whole and it was judged there: {run.TargetsSeenWhole:N0}."));
        }

        if (run.DensityChanged > 0)
        {
            tree.Add($"The touch target checks did not run on {Count(run.DensityChanged, "settled snapshot")} whose items were taken after the density changed during the visit, as they may have been laid out at either density.");
        }

        if (run.Problems.Count > 0)
        {
            tree.Add($"{Count(run.Problems.Count, "snapshot")} could not be checked: {string.Join("; ", run.Problems.Take(3))}{(run.Problems.Count > 3 ? "; and more" : string.Empty)}.");
        }

        sections.Add(new PhoneSummarySection("Checks of the tree", Result(run.Findings), tree));

        sections.Add(new PhoneSummarySection(
            PhoneCheckNames.Title(PhoneCheckNames.ClickableNotFocusable),
            Result(Of(run.Findings, PhoneCheckNames.ClickableNotFocusable)),
            [
                $"{Count(run.Clickable, "clickable item")} checked.",
                F($"{run.ClickableSetAside:N0} set aside inside a focusable clickable item."),
            ],
            "Each finding is a warning until a keyboard test confirms it."));

        var fields = new List<string>
        {
            F($"{Count(run.Fields, "field")} checked, {run.Invalid:N0} marked invalid."),
            $"{Count(run.Expandable, "expandable item")} checked.",
            $"{Count(run.Sliders, "slider")} checked.",
        };
        if (run.ExpandableNotRecorded > 0)
        {
            fields.Add($"The expanded state of {Count(run.ExpandableNotRecorded, "expandable item")} was not recorded, as the phone's Android is older than 16, so they were not checked.");
        }

        sections.Add(new PhoneSummarySection(
            "Fields and states, five checks",
            Result(Of(run.Findings, PhoneCheckNames.FieldNoLabel, PhoneCheckNames.FieldNoName, PhoneCheckNames.InvalidNoError, PhoneCheckNames.ExpandableNoState, PhoneCheckNames.SliderNoRange)),
            fields,
            "A field's label is only an item linked to it as its label (labeledBy), as a hint is not shown once text is typed."));

        var status = new List<string>
        {
            run.StatusRun
                ? F($"{Count(run.BriefMessages, "brief message")} (text shown and gone within 5 seconds, with no touch between), {run.BriefExposed:N0} of them in a live region, announced or in a titled pane that appeared with them.")
                : "Brief messages not looked for, as no touches were recorded, so text going away by itself cannot be told from text a touch took away.",
            $"{Count(run.LiveRegionItems, "item")} in a live region.",
            $"{Count(run.Announcements, "announcement")} in the recording.",
            $"{Count(run.Toasts, "toast")} in the recording.",
        };
        sections.Add(new PhoneSummarySection(
            "Status messages (WCAG 4.1.3), two checks",
            Result([.. Of(run.Findings, PhoneCheckNames.StatusNotAnnounced), .. run.AppFindings]),
            status));

        var timeout = !run.SettingsRecorded
            ? "Time to take action was not recorded, so texts gone within 5 seconds were looked at."
            : run.TimeoutMilliseconds is { } milliseconds
                ? F($"Time to take action was set, at most {milliseconds / 1000.0:0.##} seconds, so texts gone sooner than it were looked at.")
                : "Time to take action was Default, so texts gone within 5 seconds were looked at.";
        var timing = new List<string> { timeout };
        if (run.StatusRun)
        {
            timing.Add(F($"{Count(run.TimedMessages, "text")} gone with no touch between, {run.TimedAlone:N0} of them gone with nothing new shown as they went."));
            timing.Add($"{Count(run.UpdatedItems, "item")} whose text changed in place with no touch.");
        }
        else
        {
            timing.Add("Text going away or updating by itself not looked for, as no touches were recorded.");
        }

        timing.Add(run.MotionRun
            ? $"{Count(run.MovingRuns, "part")} of the phone's video that kept changing for more than 5 seconds with no touch (from the video, so approximate)."
            : "Moving content not looked for, as it needs the phone's video and touches recorded.");
        sections.Add(new PhoneSummarySection(
            "Timing (WCAG 2.2.1 and 2.2.2), four checks",
            Result(Of(run.Findings, PhoneCheckNames.TextGoneByItself, PhoneCheckNames.TextGoneAsContentChanged, PhoneCheckNames.TextUpdatingByItself, PhoneCheckNames.MovingByItself)),
            timing));

        List<string> context = run.ContextRun
            ?
            [
                $"{Count(run.FocusMoves, "focus move")} by a key or a tap on a field.",
                $"{Count(run.ScreenReaderFocusMoves, "TalkBack focus move")} after a gesture or key.",
                $"{Count(run.SettingChanges, "setting change")} (typing, checking, choosing or moving a slider).",
                F($"{run.ContextChanges:N0} {(run.ContextChanges == 1 ? "change" : "changes")} of context within 1.5 seconds of one, with nothing touched, pressed or clicked between."),
            ]
            : ["Not looked for, as no touches were recorded, so a tap that caused a change cannot be told from none."];
        sections.Add(new PhoneSummarySection(
            "Changes of context (WCAG 3.2.1 and 3.2.2), three checks",
            Result(Of(run.Findings, PhoneCheckNames.ContextOnFocus, PhoneCheckNames.ContextOnScreenReaderFocus, PhoneCheckNames.ContextOnInput)),
            context,
            run.ContextRun ? "A change of context is a new screen or app, a window that took the focus, a pane, or the focus moving by itself." : null));

        List<string> orientation = run.OrientationRun
            ?
            [
                $"The phone was turned between upright and sideways {Count(run.OrientationTurns, "time")}, from the companion app's own sensor.",
                $"The screen stayed as it was, with Auto-rotate on, for {Count(run.OrientationHoldSeconds, "second")} or more after {Count(run.OrientationNotFollowed, "turn")} within a visit.",
            ]
            : [$"Not looked for, as {run.OrientationNotRun ?? "the checks did not run"}."];
        sections.Add(new PhoneSummarySection(
            PhoneCheckNames.Title(PhoneCheckNames.OrientationRestricted),
            Result(Of(run.Findings, PhoneCheckNames.OrientationRestricted)),
            orientation,
            "It is looked for only while Auto-rotate is on."));

        sections.Add(new PhoneSummarySection(
            "White space used for layout (WCAG 1.3.2), three checks",
            Result(Of(run.Findings, PhoneCheckNames.SpacedLetters, PhoneCheckNames.SpaceColumns, PhoneCheckNames.LineBreakMidSentence)),
            [$"{Count(run.SequenceTexts, "text")} looked at: each shown item's text, content description, hint, state description and pane title, from the tree, not OCR; text typed into a field is not looked at."],
            "Looked for: letters of a word spaced apart (3 or more single letters, 2 or more Han, kana or Hangul, initialisms included), 2 or more lines with gaps of 2 or more spaces or a tab, and a line break before a lowercase letter with no . ! ? : or ; before it. Whether the order of the content makes sense is not looked for."));

        List<string> links = [$"{Count(run.Links, "link")} looked at, in shown items' text, from the tree, with no model."];
        if (run.LinksNotRecorded > 0)
        {
            links.Add($"{Count(run.LinksNotRecorded, "shown item")} with text not checked, as the companion app did not record the links in the items' text before protocol 8.");
        }

        sections.Add(new PhoneSummarySection(
            "Link purpose (WCAG 2.4.4, and 2.4.9 at AAA), five checks",
            Result(Of(run.Findings, PhoneCheckNames.LinkNoText, PhoneCheckNames.LinkNoContext, PhoneCheckNames.LinkNotSpecific,
                PhoneCheckNames.LinkSameTextAndContext, PhoneCheckNames.LinkSameText)),
            links,
            "Looked for: a link with no letter or digit; a non-specific phrase, such as \"read more\" or \"here\", in English only, alone in its item or with a sentence; and the same words to different recorded addresses. At AA, what fails only 2.4.9 is a warning referring to WCAG 5.2.4. A link's context is the rest of its own item's text. Whether a link's words describe where it leads is not judged."));

        sections.Add(new PhoneSummarySection(
            "Sensory characteristics (WCAG 1.3.3), three checks",
            Result(Of(run.Findings, PhoneCheckNames.InstructionByPlace, PhoneCheckNames.InstructionByShape, PhoneCheckNames.InstructionByColour)),
            [$"{Count(run.Instructions, "instruction")} looked at: a verb such as \"tap\" or \"press\" and the words after it, in shown items' text and content descriptions, from the tree, with no model."],
            "Looked for: an instruction that names a control by its place, such as \"on the right\", its shape, such as \"round\", or its colour, such as \"green\", and by none of the window's controls' names, in English only. Size, sound and orientation are not looked for, nor \"above\" and \"below\"."));
        return sections;
    }

    private static List<PhoneSummarySection> ContrastSections(PhoneChecksSummaryInput input)
    {
        if (input.ContrastProblem is { } problem)
        {
            return [new PhoneSummarySection("Contrast and the checks of text OCR read", "Could not be run", [problem])];
        }

        if (input.Contrast is not { } measured || input.TextContrast is not { } contrast)
        {
            return [new PhoneSummarySection("Contrast and the checks of text OCR read", "Measuring", [])];
        }

        var engine = measured.Snapshots.Select(snapshot => snapshot.Engine).FirstOrDefault(name => !string.IsNullOrEmpty(name));
        var ocr = new List<string>
        {
            measured.OcrUnavailable is { } unavailable
                ? $"OCR was not used, as {unavailable}, so only the app's text was measured, on its items' bounds."
                : engine is null ? "No text was found by OCR." : $"The text was found with {engine}.",
            F($"{Count(measured.Snapshots.Count, "settled snapshot")} measured, {measured.FromScreenshots:N0} from the phone's screenshots and {measured.FromFrames:N0} from the video, in {measured.Elapsed.TotalSeconds:0.0} seconds."),
        };
        if (measured.Problems.Count > 0)
        {
            ocr.Add($"{Count(measured.Problems.Count, "snapshot")} could not be measured: {string.Join("; ", measured.Problems.Take(3))}{(measured.Problems.Count > 3 ? "; and more" : string.Empty)}.");
        }

        var sections = new List<PhoneSummarySection>
        {
            new("Text read on the screens' pictures", null, ocr),
            new(
                PhoneCheckNames.Title(contrast.Check),
                Result(contrast.Findings),
                [F($"{Count(contrast.Lines, "line")} of text measured, {contrast.Passed:N0} passed.")]),
        };

        if (input.NonTextContrast is { } nonText)
        {
            sections.Add(new PhoneSummarySection(
                PhoneCheckNames.Title(PhoneCheckNames.NonTextContrast),
                Result(Of(nonText.Findings, PhoneCheckNames.NonTextContrast)),
                [F($"{Count(nonText.Controls, "control")} measured, {nonText.Passed:N0} passed.")],
                "At AA, its only level. Graphics with no item in the tree are not checked. " + NonTextRules));
            sections.Add(new PhoneSummarySection(
                PhoneCheckNames.Title(PhoneCheckNames.FieldBottomLineOnly),
                Result(Of(nonText.Findings, PhoneCheckNames.FieldBottomLineOnly)),
                []));
        }

        if (input.ImagesOfText is { } images)
        {
            var title = images.Level == PhoneAuditLevel.AAA
                ? $"{PhoneCheckNames.Title(PhoneCheckNames.ImageOfText)} and {PhoneCheckNames.Title(PhoneCheckNames.ImageOfTextAaa)}"
                : PhoneCheckNames.Title(PhoneCheckNames.ImageOfText);
            sections.Add(images.SnapshotsRead == 0
                ? NotChecked(title, "it needs OCR's words")
                : new PhoneSummarySection(
                    title,
                    Result(images.Findings),
                    WithNote(
                        [
                            F($"{Count(images.Images, "image")} read by OCR, {images.Passed:N0} passed."),
                            $"Left out by the rules, before any model: {Count(images.CaptionLines, "line")} TalkBack was saying, {Count(images.EdgeLines, "line")} past an image's edge and {Count(images.NeighbourLines, "line")} an item beside the image holds.",
                            $"{Count(images.OutOfStep, "image")} on a picture out of step with the tree not judged.",
                            $"{Count(images.ManualChecks, "image")} for a manual check, as the app draws its own text with Flutter.",
                            $"{Count(images.Questions.Count, "image")} for the model for vision-based checks, whose answer decides the exceptions.",
                        ],
                        images.ModelNote),
                    "The words OCR read inside an image that no item's text holds are a probable image of text."));
        }

        if (input.HeadingLook is { } headings)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.TextShownAsHeading);
            sections.Add(headings.SnapshotsRead == 0
                ? NotChecked(title, "it needs OCR's words")
                : new PhoneSummarySection(
                    title,
                    Result(headings.Findings),
                    WithNote(
                        [
                            $"{Count(headings.Items, "piece")} of text placed by OCR.",
                            $"Left out by the rules: {Count(headings.InControls, "piece")} of text inside an item that can be tapped or a field, as a control's text is its name, and {Count(headings.FieldLabels, "line")} in capitals directly above a field, as its label.",
                            $"{Count(headings.Questions.Count, "piece")} of text for the model for vision-based checks.",
                        ],
                        headings.ModelNote),
                    F($"Text not marked as a heading whose letters are at least {PhoneHeadingLookRun.TallerBy:0.##} times as tall as the screen's body text, or one short line in capitals, is a probable heading.")));
        }

        if (input.ImageAlternative is { } alternatives)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.ImageNoTextAlternative);
            sections.Add(alternatives.SnapshotsRead == 0
                ? NotChecked(title, "it needs OCR's words to tell a picture out of step with the tree")
                : new PhoneSummarySection(
                    title,
                    Result(alternatives.Findings),
                    WithNote(
                        [
                            $"{Count(alternatives.Images, "image")} on the pictures OCR read.",
                            $"Left out by the rules: {Count(alternatives.Named, "image")} with a name of {(alternatives.Named == 1 ? "its" : "their")} own; " +
                                $"{Count(alternatives.Controls, "image")} that {(alternatives.Controls == 1 ? "is a control" : "are controls")}, as the tree's checks judge a control's name; " +
                                $"{Count(alternatives.InNamedControls, "image")} inside a control with a name; " +
                                $"{Count(alternatives.Covered, "image")} at least half under or over controls with a name; " +
                                F($"{Count(alternatives.Thin, "image")} under {PhoneImageAlternativeRun.LeastSideDp:0.##} dp on the shorter side."),
                            $"{Count(alternatives.OutOfStep, "image")} on a picture out of step with the tree not judged.",
                            $"{Count(alternatives.Questions.Count, "image")} for the model for vision-based checks, whose answer decides whether it gives information.",
                        ],
                        alternatives.ModelNote),
                    "An image that is not a control, with no description, text or label, that no control with a name holds, says nothing to a screen reader."));
        }

        if (input.DescribedImages is { } described)
        {
            var title = $"{PhoneCheckNames.Title(PhoneCheckNames.IconContradictsName)} and {PhoneCheckNames.Title(PhoneCheckNames.DescriptionNotMatchingImage)}";
            sections.Add(described.SnapshotsRead == 0
                ? NotChecked(title, "they need OCR's words to tell a picture out of step with the tree")
                : new PhoneSummarySection(
                    title,
                    Result(described.Findings),
                    WithNote(
                        [
                            $"{Count(described.Images, "described image")} on the pictures OCR read, {Count(described.Controls, "control")} among them.",
                            $"{Count(described.OutOfStep, "image")} on a picture out of step with the tree not judged.",
                            F($"{described.Fit:N0} judged to fit, not confirmed."),
                            $"{Count(described.NotIcons, "control")} whose image was judged not to be an icon.",
                            $"{Count(described.Unanswered, "described image")} not judged.",
                        ],
                        described.ModelNote),
                    "A control's icon is judged against its name, and an image that is not a control against its description, by the model for vision-based checks only."));
        }

        if (input.ScreenTitles is { } titles)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.TitleNotDescriptive);
            sections.Add(titles.SnapshotsRead == 0
                ? NotChecked(title, "it needs the text OCR read on the screens' pictures")
                : new PhoneSummarySection(
                    title,
                    Result(titles.Findings),
                    WithNote(
                        [
                            $"{Count(titles.Screens, "screen")} OCR read a picture of, each once.",
                            F($"{titles.Untitled:N0} with no title, left to the check of a screen with no title."),
                            F($"{titles.Matched:N0} whose title is exactly a heading, a line OCR read or an item a screen reader says on the screen, so describes it."),
                            F($"{titles.Questions.Count:N0} for the project's models: {titles.TextQuestions:N0} with only text, for the model for text-based checks, and {titles.PicturedQuestions:N0} with the screen's picture, as images cover at least half of it, for the model for vision-based checks."),
                            F($"{titles.Described:N0} judged to describe {(titles.Described == 1 ? "its screen" : "their screens")}, not confirmed."),
                            $"{Count(titles.Unanswered, "screen")} not judged.",
                        ],
                        titles.ModelNote),
                    "In Chrome, the web page's title is judged."));
        }

        if (input.DecorativeImages is { } decorative)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.DecorativeAnnounced);
            sections.Add(decorative.SnapshotsRead == 0
                ? NotChecked(title, "it needs OCR's words to tell a picture out of step with the tree")
                : new PhoneSummarySection(
                    title,
                    Result(decorative.Findings),
                    WithNote(
                        [
                            $"{Count(decorative.Images, "described image")} on pictures in step with the tree.",
                            F($"Not asked: {decorative.NotSpoken:N0} not said by a screen reader, {decorative.Controls:N0} that are controls and {decorative.ItemNames:N0} whose description is all the item holding them says, so names."),
                            $"{Count(decorative.Questions.Count, "image")} that a screen reader says, with other text or alone, for the model for vision-based checks to judge informative or decorative.",
                            $"{Count(decorative.OutOfStep, "image")} on a picture out of step with the tree not judged.",
                            F($"{decorative.Informative:N0} judged informative, not confirmed."),
                            $"{Count(decorative.Unanswered, "image")} not judged.",
                        ],
                        decorative.ModelNote)));
        }

        if (input.HeadingSections is { } sectionRun)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.HeadingNotDescriptive);
            sections.Add(sectionRun.SnapshotsRead == 0
                ? NotChecked(title, "it needs the text OCR read on the screens' pictures")
                : new PhoneSummarySection(
                    title,
                    Result(sectionRun.Findings),
                    WithNote(
                        [
                            $"{Count(sectionRun.Marked, "marked heading")} and {Count(sectionRun.Unmarked, "piece")} of text shown as a heading and judged one, each once a screen.",
                            F($"{sectionRun.ScreenTitles:N0} that are their screen's title, left to the screen title check."),
                            F($"{sectionRun.EmptySections:N0} with nothing under them on screen or in the tree."),
                            F($"{sectionRun.Questions.Count:N0} for the project's models: {sectionRun.TextQuestions:N0} with only text, for the model for text-based checks, and {sectionRun.PicturedQuestions:N0} with the screen's picture, as images cover at least half of the section, for the model for vision-based checks."),
                            $"{Count(sectionRun.NotJudgedHeadings, "piece")} of text shown as a heading not checked, as not judged a heading.",
                            F($"{sectionRun.Described:N0} judged to describe {(sectionRun.Described == 1 ? "its section" : "their sections")}, not confirmed."),
                            $"{Count(sectionRun.Unanswered, "heading")} not judged.",
                        ],
                        sectionRun.ModelNote)));
        }

        if (input.TextNotInTree is { } notInTree)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.TextNotInTree);
            sections.Add(notInTree.SnapshotsRead == 0
                ? NotChecked(title, "it needs the text OCR read on the screens' pictures")
                : new PhoneSummarySection(
                    title,
                    Result(notInTree.Findings),
                    WithNote(
                        [
                            $"{Count(notInTree.Entries.Count, "line")} OCR read in no item's text, each once a visit, on {Count(notInTree.SnapshotsRead, "picture")}.",
                            F($"Left out: {notInTree.Captions:N0} of TalkBack's caption; {notInTree.InImages:N0} inside images, left to the image of text check; {notInTree.ShortLines:N0} of fewer than 3 letters or digits; {notInTree.HeldElsewhere:N0} held by an item elsewhere on the screen; {notInTree.Misreads:N0} taken as a small item's own text misread; {notInTree.OtherSnapshots:N0} held by an item in another snapshot of the same visit."),
                            $"{Count(notInTree.Questions.Count, "line")} for the model for vision-based checks.",
                            $"{Count(notInTree.OutOfStepSnapshots, "picture")} out of step with the tree, with {Count(notInTree.OutOfStepLines, "line")} in no item's text, not judged.",
                            F($"{notInTree.Covered:N0} judged covered and {notInTree.NotText:N0} judged not text, not confirmed."),
                            $"{Count(notInTree.Unanswered, "line")} not judged.",
                        ],
                        notInTree.ModelNote)));
        }

        if (input.HeadingNotShown is { } notShown)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.HeadingNotShown);
            sections.Add(notShown.SnapshotsRead == 0
                ? NotChecked(title, "it needs the text OCR read on the screens' pictures")
                : new PhoneSummarySection(
                    title,
                    Result(notShown.Findings),
                    WithNote(
                        [
                            $"{Count(notShown.Entries.Count, "marked heading")} placed by OCR, each once a visit, on {Count(notShown.SnapshotsRead, "picture")}.",
                            F($"Left out: {notShown.OcrDiffers:N0} where OCR's text differs from the app's; {notShown.Covered:N0} covered by another line; {notShown.LineGoesOn:N0} where OCR's line goes on past the heading."),
                            $"{Count(notShown.NotPlaced, "marked heading")} not placed by OCR, not checked.",
                            $"{Count(notShown.Questions.Count, "marked heading")} for the model for vision-based checks.",
                            F($"{notShown.Shown:N0} judged shown as {(notShown.Shown == 1 ? "a heading" : "headings")}, not confirmed."),
                            $"{Count(notShown.Unanswered, "marked heading")} not judged.",
                        ],
                        notShown.ModelNote)));
        }

        if (input.ControlNames is { } names)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.ControlNameNotDescriptive);
            sections.Add(names.SnapshotsRead == 0
                ? NotChecked(title, "it needs the text OCR read on the screens' pictures")
                : new PhoneSummarySection(
                    title,
                    Result(names.Findings),
                    WithNote(
                        [
                            $"{Count(names.Controls, "named control")} a screen reader stops on, each once a visit, on {Count(names.SnapshotsRead, "picture")}.",
                            $"{Count(names.Placeholders, "name")} once a visit that {(names.Placeholders == 1 ? "is a placeholder" : "are placeholders")}, warnings with no model.",
                            $"{Count(names.Questions.Count, "name")} once for each screen for the model for vision-based checks.",
                            F($"{names.Described:N0} judged to say what the control does, not confirmed."),
                            $"{Count(names.Unanswered, "name")} not judged.",
                        ],
                        names.ModelNote)));
        }

        if (input.LabelInName is { } labels)
        {
            var read = labels.UsedOcr ? "the tree's text and, where it has none, OCR's" : "the tree's text";
            sections.Add(new PhoneSummarySection(
                PhoneLabelInNameRun.Title,
                Result(labels.Findings),
                [F($"{Count(labels.Controls, "control")} checked, {labels.Passed:N0} passed, {labels.Unread:N0} with no label read.")],
                $"The label is all the text inside a control, and the text of any item it is labelled by, from {read}, against its content description; text beside a control is not taken as its label."));
        }

        if (input.ColourOnly is { } colour)
        {
            var links = PhoneCheckNames.Title(PhoneCheckNames.LinkOnlyByColour);
            sections.Add(colour.Links == 0 && colour.LinksNotRecorded > 0
                ? NotChecked(links, "the companion app did not record the links in the items' text before protocol 8")
                : new PhoneSummarySection(
                    links,
                    Result(Of(colour.Findings, PhoneCheckNames.LinkOnlyByColour)),
                    [F($"{Count(colour.Links, "link")} measured on screenshots, {colour.LinksPassed:N0} passed.")],
                    "A link's colour is compared with the text around it in its item; an underline, bold strokes, or a difference in lightness of 3:1, in its colour or its background, tells it apart by more than colour. Italics are not detected."));
            sections.Add(new PhoneSummarySection(
                PhoneCheckNames.Title(PhoneCheckNames.SelectedOnlyByColour),
                Result(Of(colour.Findings, PhoneCheckNames.SelectedOnlyByColour)),
                [F($"{Count(colour.Selected, "item")} marked selected measured on screenshots, {colour.SelectedPassed:N0} passed.")],
                "An item the tree marks selected is compared with the items beside it of its class not selected; an underline or a bar they lack, bold strokes, or a difference in lightness of 3:1, in its colour or its background, shows its state by more than colour. An app that does not mark the selected item in the tree is not checked."));
        }

        if (input.ChartColours is { } charts)
        {
            var title = PhoneCheckNames.Title(PhoneCheckNames.ChartOnlyByColour);
            sections.Add(charts.SnapshotsRead == 0
                ? NotChecked(title, "it needs the phone's screenshots, and only frames of the video show these screens")
                : new PhoneSummarySection(
                    title,
                    Result(charts.Findings),
                    WithNote(
                        [
                            $"{Count(charts.Items, "item")} with no text of its own and two or more colours, each once a visit, on {Count(charts.SnapshotsRead, "screenshot")}.",
                            $"{Count(charts.NotMeasured, "item")} once a visit not measured, as something was drawn over {(charts.NotMeasured == 1 ? "it" : "them")} or {(charts.NotMeasured == 1 ? "it was" : "they were")} not wholly on the picture.",
                            $"{Count(charts.Questions.Count, "item")} once for each screen for the model for vision-based checks.",
                            F($"{charts.NotCharts:N0} judged not to be charts and {charts.NotOnlyColour:N0} to show what their colours stand for another way too, not confirmed."),
                            $"{Count(charts.Unanswered, "item")} not judged.",
                        ],
                        charts.ModelNote)));
        }

        return sections;
    }

    private static PhoneSummarySection NotChecked(string title, string why) =>
        new(title, "Not checked", [$"Not checked, as {why}."]);

    // A check's points, then what became of its questions to the model,
    // where it was asked, each part of that note a line of its own.
    private static List<string> WithNote(List<string> points, string? note)
    {
        if (!string.IsNullOrWhiteSpace(note))
        {
            points.AddRange(Parts(note).Select(part => "Model's answers: " + part + "."));
        }

        return points;
    }

    /// <summary>A note's parts, split at each "; " outside brackets, each trimmed.</summary>
    public static IReadOnlyList<string> Parts(string note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < note.Length; index++)
        {
            switch (note[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ';' when depth == 0 && index + 1 < note.Length && note[index + 1] == ' ':
                    parts.Add(note[start..index]);
                    start = index + 2;
                    break;
            }
        }

        parts.Add(note[start..]);
        return parts.Select(part => part.Trim().TrimEnd('.')).Where(part => part.Length > 0).ToList();
    }

    private static List<PhoneFinding> Of(IEnumerable<PhoneFinding> findings, params string[] checks) =>
        findings.Where(finding => checks.Contains(finding.Check, StringComparer.Ordinal)).ToList();

    private static string Count(int count, string noun) =>
        F($"{count:N0} {(count == 1 ? noun : noun + "s")}");

    private static string F(FormattableString text) => text.ToString(CultureInfo.CurrentCulture);
}

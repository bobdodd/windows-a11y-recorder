using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>One question about an item that may be a chart as it is sent: the question, its request, and its step key for each run.</summary>
public sealed record ChartColourRequest(PhoneChartQuestion Question, ModelRequest Request, string Key)
{
    /// <summary>The step key of a run, 1 to 4.</summary>
    public string StepKey(int run) => $"{Key}-{run.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The question of the check of a chart that relies on colour for the
/// model chosen for vision-based checks (android-checks.md, step 32,
/// rules 3 and 4): for each item the rules leave
/// (<see cref="PhoneChartColourRun.Questions"/>), once for each screen,
/// the whole screen with the item outlined and the item with the screen
/// above and below it, where a legend or labels may be; the item's class
/// and description, the colours measured in it with their shares, pieces
/// and contrast ratios, and the text near it from the tree and OCR; and
/// one of four fixed answers, reason first. It is asked twice, once for a
/// local model at temperature 0. A question the first answers do not
/// settle is asked again of the model for complex questions, and so is
/// one they settle as only-colour, to confirm it; that model's answers
/// decide. Each answer is kept by its request, so it is given again only
/// for the same item, text and pictures.
/// </summary>
public static class AndroidChartColours
{
    public const string Template = "android-chart-colour";

    public const int TemplateVersion = 1;

    public const string NotAChart = "not-a-chart";

    public const string OnlyColour = "only-colour";

    public const string NotOnlyColour = "not-only-colour";

    public const string CannotTell = "cannot-tell";

    public static readonly IReadOnlyList<string> Answers = [NotAChart, OnlyColour, NotOnlyColour, CannotTell];

    public const string Instructions =
        "You judge one item in an Android app for WCAG 2.2 success criterion 1.4.1, Use of Color: " +
        "whether it is a chart or other graphic whose colours stand for something, and if so whether colour is the only visual means of telling what they stand for. " +
        "Answer only from what the pictures show and the measurements and text given, with one of the fixed answers.";

    // Picture B's longer side at most, in pixels.
    private const int LongestArea = 1024;

    private const int Outline = 4;

    /// <summary>The question's text (rule 3).</summary>
    public static string Question(PhoneChartQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var description = question.Description is { } said
            ? $" Its description, which a screen reader says but which is not shown, is \"{said}\"."
            : " It has no description.";
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Picture A is the whole screen of the Android app {question.App}, on its screen \"{question.ScreenName}\", with one item outlined in cyan. ")
            .Append("Picture B is the outlined item and the screen above and below it, where a title, a legend or labels may be.\n")
            .Append(CultureInfo.InvariantCulture, $"The outlined item is of class {question.Role}, with no text of its own.{description}\n")
            .Append(CultureInfo.InvariantCulture,
                $"Measured on the phone's screenshot, the item's commonest colour is {PhoneColours.Hex(question.Background)}, and these colours each cover 1% or more of it: {PhoneChartColourRun.FillsText(question.Fills)}.\n")
            .Append(CultureInfo.InvariantCulture, $"Contrast ratios between those colours: {PhoneChartColourRun.RatiosText(question.Fills)}.\n");
        if (question.Near.Count == 0)
        {
            text.Append(question.UsedOcr ? "No text was found near the item.\n" : "No text was found near the item in the tree, and OCR was not used.\n");
        }
        else
        {
            text.Append(question.UsedOcr ? "Text near the item, from OCR and the tree, top to bottom:\n" : "Text near the item, from the tree (OCR was not used), top to bottom:\n");
            foreach (var line in question.Near)
            {
                var colour = line.Colour is { } ink ? $", in {PhoneColours.Hex(ink)}" : "";
                text.Append(CultureInfo.InvariantCulture, $"- \"{line.Text}\", {line.Where}{colour}\n");
            }
        }

        return text
            .Append("WCAG 1.4.1 says colour must not be the only visual means of conveying information. ")
            .Append("Each of these is another visual means, if it lets a person who cannot tell the colours apart match each thing the colours stand for to its part of the graphic: ")
            .Append("words or values on or beside each part; a pattern or hatching; marker shapes that differ; a dashed line or outline against a solid one; ")
            .Append("a difference in lightness, a contrast ratio of 3:1 or more, unless a particular colour itself carries the meaning, such as red for over budget. ")
            .Append("These are not: an outline the same on every part; a legend whose names are told apart only by their colour; a description that is not shown.\n")
            .Append("Does the outlined item rely on colour alone to show what its colours stand for?\n")
            .Append("- not-a-chart: it is not a chart, graph, map, key or other graphic whose colours stand for data, groups or states; it is, for example, a photo, an illustration, a logo or a decoration.\n")
            .Append("- only-colour: it is such a graphic, and at least one thing its colours stand for can be told only by colour.\n")
            .Append("- not-only-colour: it is such a graphic, and everything its colours stand for is also shown another visual way.\n")
            .Append("- cannot-tell: the pictures, measurements and text do not show enough to judge.\n")
            .Append("First write one or two sentences saying what each colour stands for and what else, if anything, shows it, then give the answer.")
            .ToString();
    }

    /// <summary>
    /// The requests of a run's questions, in the questions' order; a
    /// question whose picture cannot be read, or whose item is not on it,
    /// is left out, and not judged.
    /// </summary>
    public static IReadOnlyList<ChartColourRequest> Requests(PhoneChartColourRun run, Func<string, PhonePixels?> load)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(load);
        var requests = new List<ChartColourRequest>();
        var pixelsOf = new Dictionary<string, PhonePixels?>(StringComparer.OrdinalIgnoreCase);
        foreach (var question in run.Questions)
        {
            var path = question.Picture.AbsolutePath;
            if (!pixelsOf.TryGetValue(path, out var pixels))
            {
                pixelsOf[path] = pixels = load(path);
            }

            if (pixels is null || AndroidImageAlternative.Clamp(question.Bounds, pixels) is null)
            {
                continue;
            }

            requests.Add(RequestOf(question, pixels));
        }

        return requests;
    }

    /// <summary>A question's request, from its picture's pixels (rule 3).</summary>
    public static ChartColourRequest RequestOf(PhoneChartQuestion question, PhonePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(pixels);
        var box = AndroidImageAlternative.Clamp(question.Bounds, pixels) ?? throw new ArgumentException("The item is not on the picture.", nameof(question));
        var area = AndroidImageAlternative.Clamp(question.Area, pixels) ?? box;
        var screen = AndroidImagesOfText.Png(AndroidImagesOfText.Screen(pixels, box));
        var around = AndroidImagesOfText.Png(Area(pixels, area, box));
        var input = Question(question);
        var request = new ModelRequest(
            Template,
            TemplateVersion,
            Instructions,
            input,
            [new ModelImage("A", "image/png", screen), new ModelImage("B", "image/png", around)],
            [],
            new HashSet<string>(StringComparer.Ordinal))
        {
            Use = ModelUses.VisionChecks,
            Answers = Answers,
            ReusedAcrossTasks = true,
        };

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{Template}\n{TemplateVersion.ToString(CultureInfo.InvariantCulture)}\n{Instructions}\n{input}\n"));
        hash.AppendData(screen);
        hash.AppendData(around);
        return new ChartColourRequest(question, request, "chart-colour-" + Convert.ToHexStringLower(hash.GetHashAndReset())[..16]);
    }

    /// <summary>
    /// Whether the first answers are to be put to the model for complex
    /// questions (rule 4): they do not settle the question, or they agree
    /// that the item relies on colour alone, which that model confirms.
    /// </summary>
    public static bool AskAgain(GroupAnswers first)
    {
        ArgumentNullException.ThrowIfNull(first);
        return CheckQuestions.Unsettled(first.One, first.Two, CannotTell) || Confirming(first);
    }

    /// <summary>
    /// The audit with no model asked about the items the rules leave, and
    /// the reason why: what <see cref="AskAsync"/> gives where the task may
    /// use no model for vision-based checks, and what the fixture tests run
    /// (android-fixtures.md, "Rule tests").
    /// </summary>
    public static AndroidAudit Unasked(AndroidAudit audit, string reason)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(reason);
        return audit.ChartColours is { } run && run.Questions.Count > 0
            ? audit.WithChartColours(run.Judged(new Dictionary<PhoneChartQuestion, PhoneChartAnswer>(), NotAsked(run.Questions.Count, reason)))
            : audit;
    }

    /// <summary>
    /// Asks the model chosen for vision-based checks about each item the
    /// rules leave in the audit, and gives the audit with the answers
    /// judged. Where the task may use no model for vision-based checks,
    /// nothing is asked and no finding is made. A question whose step fails
    /// is not judged, and the note says why; where the question put to the
    /// model for complex questions fails, the first answers stand.
    /// </summary>
    public static async Task<AndroidAudit> AskAsync(
        AnalysisTaskContext context,
        AndroidAudit audit,
        Func<string, PhonePixels?> load,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(load);
        if (audit.ChartColours is not { } run || run.Questions.Count == 0)
        {
            return audit;
        }

        if (context.NoModel(ModelUses.VisionChecks) is { } none)
        {
            return Unasked(audit, none);
        }

        var requests = Requests(run, load);
        var failures = new List<string>();
        var answers = new Dictionary<PhoneChartQuestion, PhoneChartAnswer>();
        var escalated = 0;
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await AskQuestionAsync(context, request.StepKey, request.Request, failures, cancellationToken).ConfigureAwait(false) is { } asked)
            {
                answers[request.Question] = Judge(asked, request.Question);
                escalated += asked.Escalated is null ? 0 : 1;
            }
        }

        var judged = run.Judged(answers, null);
        var note = Note(judged, $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "was" : "were")} for the model for vision-based checks")
            + AskedAgain(escalated);
        if (requests.Count < run.Questions.Count)
        {
            note += string.Create(CultureInfo.CurrentCulture, $"; {Count(run.Questions.Count - requests.Count)} not asked as the picture could not be read");
        }

        if (failures.Count > 0)
        {
            note += "; not answered: " + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
        }

        return audit.WithChartColours(run.Judged(answers, note));
    }

    /// <summary>
    /// Asks one question (rule 4): twice of the model for vision-based
    /// checks, then, as runs 3 and 4, of the model for complex questions
    /// where there is another and the first answers do not settle it or
    /// agree that the item relies on colour alone. Null where the first
    /// answers could not be had; where the question put to the model for
    /// complex questions fails, the first answers stand.
    /// </summary>
    public static async Task<QuestionAnswers?> AskQuestionAsync(
        AnalysisTaskContext context,
        Func<int, string> stepKey,
        ModelRequest request,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (await CheckQuestions.AskGroupAsync(context, stepKey, request, 1, failures, cancellationToken).ConfigureAwait(false) is not { } first)
        {
            return null;
        }

        GroupAnswers? again = null;
        if (AskAgain(first) && CheckQuestions.EscalationProfile(context, request.Use) is not null)
        {
            again = await CheckQuestions.AskGroupAsync(
                context, stepKey, request with { Use = ModelUses.Complex(request.Use) }, CheckQuestions.EscalatedRun, failures, cancellationToken).ConfigureAwait(false);
        }

        return new QuestionAnswers(first, again);
    }

    /// <summary>The note of questions not asked, as no model may be used for vision-based checks.</summary>
    public static string NotAsked(int questions, string reason) =>
        string.Create(CultureInfo.CurrentCulture,
            $"{Count(questions)} left by the rules {(questions == 1 ? "was" : "were")} not judged, as no model may be used for vision-based checks: {reason}");

    /// <summary>
    /// What the answers make of an item (rule 4), the deciding model's:
    /// agreed not-a-chart or not-only-colour, counted, no finding;
    /// otherwise a warning.
    /// </summary>
    public static PhoneChartAnswer Judge(QuestionAnswers answers, PhoneChartQuestion question)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(question);
        var deciding = answers.Escalated ?? answers.First;
        var agreed = deciding.One.Answer == deciding.Two.Answer ? deciding.One.Answer : null;
        var inference = answers.Escalated is { } escalated
            ? Escalated(Interpret(escalated.One, escalated.Two, escalated.Model, escalated.Cloud, question), answers.First)
            : Interpret(answers.First.One, answers.First.Two, answers.First.Model, answers.First.Cloud, question);
        var outcome = agreed switch
        {
            NotAChart => PhoneChartOutcome.NotAChart,
            NotOnlyColour => PhoneChartOutcome.NotOnlyColour,
            _ => PhoneChartOutcome.Finding,
        };
        return new PhoneChartAnswer(outcome, inference);
    }

    /// <summary>
    /// What two answers make of an item (rule 4): agreed that it relies on
    /// colour alone, a warning for a person to verify, with the model's
    /// reason and the measured ratios; agreed that it is not a chart, or
    /// that it shows what its colours stand for another way too,
    /// information, not a finding; agreed that the pictures do not tell, or
    /// not agreed, a warning to check manually.
    /// </summary>
    public static PhoneInference Interpret(ClosedAnswerOf first, ClosedAnswerOf second, string model, bool cloud, PhoneChartQuestion question)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(question);
        var who = $"{model}, {(cloud ? "a cloud model" : "a local model")},";
        var twice = ReferenceEquals(first, second) ? "" : " twice";
        var colours = $"its colours, measured, {string.Join(", ", question.Fills.Select(fill => fill.Hex))}, with contrast ratios {PhoneChartColourRun.RatiosText(question.Fills)}";
        if (first.Answer != second.Answer)
        {
            return new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} was asked twice whether the item relies on colour alone to show what {colours}, stand for, and gave two answers, {first.Answer} and {second.Answer}: \"{first.Reason}\" and \"{second.Reason}\"; check manually",
                Inferred: true, model, cloud);
        }

        return first.Answer switch
        {
            OnlyColour => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} inferred from the pictures{twice} that the item is a chart or other graphic that shows something only by colour, with {colours}: \"{first.Reason}\"; a person should verify this",
                Inferred: true, model, cloud),
            NotOnlyColour => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice} that the item shows what its colours stand for another visual way too, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            NotAChart => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice} that the item is not a chart or other graphic whose colours stand for something, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            _ => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} could not tell from the pictures{twice} whether the item relies on colour alone, with {colours}: \"{first.Reason}\"; check manually",
                Inferred: true, model, cloud),
        };
    }

    /// <summary>
    /// What kept answers make of a run, for the Checks tab: the answers the
    /// project's model for vision-based checks gave to the run's requests,
    /// by their step keys, with the model for complex questions' where the
    /// first are to be put to it (rule 4). A request with no answer to its
    /// first run is not judged; one waiting for the model for complex
    /// questions is not judged either; the note counts both.
    /// </summary>
    public static PhoneChartColourRun FromKept(
        PhoneChartColourRun run,
        IReadOnlyList<ChartColourRequest> requests,
        IReadOnlyDictionary<string, KeptAnswer> kept,
        string model,
        IReadOnlyDictionary<string, KeptAnswer>? complexKept = null,
        string? complexModel = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(model);
        var answers = new Dictionary<PhoneChartQuestion, PhoneChartAnswer>();
        int waiting = 0, askedAgain = 0;
        foreach (var request in requests)
        {
            if (CheckQuestions.FromKept(kept, request.StepKey, 1, model) is not { } first)
            {
                continue;
            }

            GroupAnswers? escalated = null;
            if (complexModel is not null && AskAgain(first))
            {
                if (CheckQuestions.FromKept(complexKept ?? new Dictionary<string, KeptAnswer>(), request.StepKey, CheckQuestions.EscalatedRun, complexModel) is not { } again)
                {
                    waiting++;
                    continue;
                }

                escalated = again;
                askedAgain++;
            }

            answers[request.Question] = Judge(new QuestionAnswers(first, escalated), request.Question);
        }

        var judged = run.Judged(answers, null);
        var unanswered = run.Questions.Count - answers.Count - waiting;
        var note = Note(judged, $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "is" : "are")} for {model}")
            + AskedAgain(askedAgain)
            + CheckQuestions.Waiting(waiting, Count(waiting))
            + (unanswered > 0 ? string.Create(CultureInfo.CurrentCulture, $", {Count(unanswered)} not yet asked") : "");
        return run.Judged(answers, note);
    }

    // The area of the screen round the item, at most LongestArea pixels on
    // its longer side, with the item outlined in cyan outside its bounds:
    // picture B.
    internal static AndroidImagesOfText.Rgb Area(PhonePixels pixels, PhoneBounds area, PhoneBounds box)
    {
        var (width, height) = (area.Width, area.Height);
        var scale = Math.Min(1, LongestArea / (double)Math.Max(width, height));
        var (outWidth, outHeight) = (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
        var result = new uint[outWidth * outHeight];
        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                result[(y * outWidth) + x] = pixels.At(Math.Min(area.Right - 1, area.Left + (int)(x / scale)), Math.Min(area.Bottom - 1, area.Top + (int)(y / scale)));
            }
        }

        var (boxLeft, boxTop, boxRight, boxBottom) =
            ((int)((box.Left - area.Left) * scale), (int)((box.Top - area.Top) * scale), (int)((box.Right - area.Left) * scale), (int)((box.Bottom - area.Top) * scale));
        for (var y = Math.Max(0, boxTop - Outline); y < Math.Min(outHeight, boxBottom + Outline); y++)
        {
            for (var x = Math.Max(0, boxLeft - Outline); x < Math.Min(outWidth, boxRight + Outline); x++)
            {
                if (!(x >= boxLeft && x < boxRight && y >= boxTop && y < boxBottom))
                {
                    result[(y * outWidth) + x] = ReportPictures.Cyan;
                }
            }
        }

        return new AndroidImagesOfText.Rgb(outWidth, outHeight, result);
    }

    // Whether the first answers agree that the item relies on colour
    // alone, to be confirmed by the model for complex questions.
    private static bool Confirming(GroupAnswers first) =>
        first.One.Answer == OnlyColour && first.Two.Answer == OnlyColour;

    // The model for complex questions' inference, after why it was asked:
    // the first answers did not settle the question, or agreed that the
    // item relies on colour alone, which it was asked to confirm.
    private static PhoneInference Escalated(PhoneInference inference, GroupAnswers first)
    {
        if (!Confirming(first))
        {
            return CheckQuestions.Escalated(inference, first);
        }

        var who = $"{first.Model}, {(first.Cloud ? "a cloud model" : "a local model")}, the project's model for simple questions,";
        var twice = ReferenceEquals(first.One, first.Two) ? "" : " twice";
        return inference with
        {
            Text = $"{who} answered only-colour{twice}, so the question was put to the project's model for complex questions to confirm; {inference.Text}",
        };
    }

    // What became of the questions: how many were answered, and what the
    // answers made of them, with the warnings for each visit.
    private static string Note(PhoneChartColourRun judged, string start) =>
        start + string.Create(CultureInfo.CurrentCulture,
            $"; {Count(judged.Answers.Count)} answered: {judged.NotCharts:N0} judged not to be charts and {judged.NotOnlyColour:N0} to show what their colours stand for another way too, not confirmed, and {judged.Findings.Count:N0} {(judged.Findings.Count == 1 ? "warning" : "warnings")} on the visits with the item");

    private static string AskedAgain(int questions) =>
        questions == 0
            ? ""
            : string.Create(CultureInfo.CurrentCulture,
                $"; {questions:N0} asked again of the model for complex questions, to settle or to confirm the first answers");

    private static string Count(int count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "item" : "items")}");
}

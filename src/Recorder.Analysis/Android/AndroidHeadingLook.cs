using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// One piece of text's question as it is sent: the request, and its step
/// key for each of the two runs.
/// </summary>
public sealed record HeadingLookRequest(PhoneHeadingQuestion Question, ModelRequest Request, string Key)
{
    /// <summary>The step key of a run, 1 or 2.</summary>
    public string StepKey(int run) => $"{Key}-{run.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The question of the check of text shown as a heading for the model
/// chosen for vision-based checks (android-checks.md, step 19, rules 2 and
/// 3): for each piece of text the rules leave
/// (<see cref="PhoneHeadingLookRun.Questions"/>), the whole screen with the
/// text outlined and the part of the screen from the text down, the text
/// as the app gives it, and one of three fixed answers with a reason. It is
/// asked as the images-of-text check asks (<see cref="AndroidImagesOfText"/>):
/// twice, once for a local model at temperature 0, its answers kept by
/// their request. What the answers make of the finding is the model's
/// inference, and says so.
/// </summary>
public static class AndroidHeadingLook
{
    public const string Template = "android-text-shown-as-heading";

    // Version 2 (2026-11-17): the reason asked for before the answer
    // (ClosedAnswer.Schema; android-vision-checks.md, "Keeping it
    // repeatable", point 7).
    public const int TemplateVersion = 2;

    public const string Heading = "heading";

    public const string NotHeading = "not-heading";

    public const string CannotTell = "cannot-tell";

    public static readonly IReadOnlyList<string> Answers = [Heading, NotHeading, CannotTell];

    public const string Instructions =
        "You judge one piece of text in an Android app for WCAG 2.2 success criterion 1.3.1, Info and Relationships: whether it is shown as a heading. " +
        "Answer only from what the pictures show, with one of the fixed answers.";

    private const uint Cyan = 0x00E5FF;

    private const int Outline = 4;

    // Picture B: from a line's height above the text to this share of the
    // screen's height below it, across the whole screen, at full size.
    private const double Below = 0.3;

    /// <summary>The question's text.</summary>
    public static string Question(string app, string screenName, string text) =>
        $"Picture A is the whole screen of the Android app {app}, on its screen \"{screenName}\", with one piece of text outlined in cyan. " +
        "Picture B is the part of the screen from the outlined text down, at full size.\n" +
        $"The outlined text, as the app gives it: \"{text}\". It is not marked as a heading for a screen reader.\n" +
        "Which one is true of the outlined text?\n" +
        "- heading: it is shown as a heading or title, of the screen or of the content after it, by its size, weight, colour, case or place.\n" +
        "- not-heading: it is not a heading, such as a message, a value, the label of a single field or value, a button, a caption or ordinary text.\n" +
        "- cannot-tell: the pictures do not show enough to judge.\n" +
        "First write one sentence saying what in the pictures shows the answer, then give the answer.";

    /// <summary>
    /// The requests of a run's questions, in the questions' order; a
    /// question whose picture cannot be read is left out, and stays a
    /// warning.
    /// </summary>
    public static IReadOnlyList<HeadingLookRequest> Requests(PhoneHeadingLookRun run, Func<string, PhonePixels?> load)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(load);
        var requests = new List<HeadingLookRequest>();
        var pixelsOf = new Dictionary<string, PhonePixels?>(StringComparer.OrdinalIgnoreCase);
        foreach (var question in run.Questions)
        {
            var path = question.Picture.AbsolutePath;
            if (!pixelsOf.TryGetValue(path, out var pixels))
            {
                pixelsOf[path] = pixels = load(path);
            }

            if (pixels is null || question.Lines.IsEmpty)
            {
                continue;
            }

            requests.Add(RequestOf(question, pixels));
        }

        return requests;
    }

    /// <summary>A question's request, from its picture's pixels.</summary>
    public static HeadingLookRequest RequestOf(PhoneHeadingQuestion question, PhonePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(pixels);
        var screen = AndroidImagesOfText.Png(AndroidImagesOfText.Screen(pixels, question.Lines));
        var band = AndroidImagesOfText.Png(Band(pixels, question.Lines));
        var input = Question(question.App, question.ScreenName, question.Text);
        var request = new ModelRequest(
            Template,
            TemplateVersion,
            Instructions,
            input,
            [new ModelImage("A", "image/png", screen), new ModelImage("B", "image/png", band)],
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
        hash.AppendData(band);
        var key = "text-shown-as-heading-" + Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
        return new HeadingLookRequest(question, request, key);
    }

    /// <summary>
    /// Asks the model chosen for vision-based checks about each piece of
    /// text the rules leave in the audit, and gives the audit with its
    /// findings judged. Where the task may use no model for vision-based
    /// checks, nothing is asked and the findings stay warnings, with the
    /// reason. A question whose step fails stays a warning, with why.
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
        if (audit.HeadingLook is not { } run || run.Questions.Count == 0)
        {
            return audit;
        }

        if (context.NoModel(ModelUses.VisionChecks) is { } none)
        {
            return audit.WithHeadingLook(run.Judged(new Dictionary<PhoneHeadingQuestion, PhoneInference>(), NotAsked(run.Questions.Count, none)));
        }

        var requests = Requests(run, load);
        var first = new Dictionary<HeadingLookRequest, AcceptedStep>();
        var failures = new List<string>();
        foreach (var request in requests)
        {
            if (await TryAskAsync(context, request, 1, failures, cancellationToken).ConfigureAwait(false) is { } step)
            {
                first[request] = step;
            }
        }

        var attempts = await context.Catalog.AttemptsAsync(context.Task.TaskId, cancellationToken).ConfigureAwait(false);
        var answers = new Dictionary<PhoneHeadingQuestion, PhoneInference>();
        foreach (var (request, step) in first)
        {
            var attempt = attempts.FirstOrDefault(item => item.StepKey == request.StepKey(1) && item.Outcome == "accepted");
            var model = attempt?.ProfileName ?? context.ModelFor(ModelUses.VisionChecks) ?? "the model";
            var cloud = attempt?.ModelSha256Basis == AnalysisWorker.ServiceBasis;
            var repeatable = attempt is not null && AndroidImagesOfText.Repeatable(attempt.ModelSha256Basis, attempt.Parameters);
            var one = ClosedAnswer.Read(step.Output);
            ClosedAnswerOf? two = one;
            if (!repeatable)
            {
                var second = await TryAskAsync(context, request, 2, failures, cancellationToken).ConfigureAwait(false);
                two = second is null ? null : ClosedAnswer.Read(second.Output);
            }

            if (one is not null && two is not null)
            {
                answers[request.Question] = Interpret(one, two, model, cloud);
            }
        }

        var asked = requests.Count;
        var note = string.Create(CultureInfo.CurrentCulture,
            $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "was" : "were")} for the model for vision-based checks; {Count(answers.Count)} answered");
        if (asked < run.Questions.Count)
        {
            note += string.Create(CultureInfo.CurrentCulture, $", {Count(run.Questions.Count - asked)} not asked as the picture could not be read");
        }

        if (failures.Count > 0)
        {
            note += "; not answered: " + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
        }

        return audit.WithHeadingLook(run.Judged(answers, note));
    }

    /// <summary>The note of questions not asked, as no model may be used for vision-based checks.</summary>
    public static string NotAsked(int questions, string reason) =>
        string.Create(CultureInfo.CurrentCulture,
            $"{Count(questions)} left by the rules {(questions == 1 ? "was" : "were")} not asked about, as no model may be used for vision-based checks: {reason}");

    private static async Task<AcceptedStep?> TryAskAsync(
        AnalysisTaskContext context,
        HeadingLookRequest request,
        int run,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.RunStepAsync(request.StepKey(run), request.Request, cancellationToken).ConfigureAwait(false);
        }
        catch (StepFailedException exception)
        {
            failures.Add(exception.Message);
            return null;
        }
    }

    /// <summary>
    /// What two answers make of a finding (android-checks.md, step 19, rule
    /// 3): agreed that the text is shown as a heading, an error; agreed that
    /// it is not, information; agreed that the pictures do not tell, or not
    /// agreed, a warning to check manually.
    /// </summary>
    public static PhoneInference Interpret(ClosedAnswerOf first, ClosedAnswerOf second, string model, bool cloud)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(model);
        var who = $"{model}, {(cloud ? "a cloud model" : "a local model")},";
        var twice = ReferenceEquals(first, second) ? "" : " twice";
        if (first.Answer != second.Answer)
        {
            return new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} was asked twice and gave two answers, {first.Answer} and {second.Answer}: \"{first.Reason}\" and \"{second.Reason}\"; check manually",
                Inferred: true, model, cloud);
        }

        return first.Answer switch
        {
            Heading => new PhoneInference(PhoneCheckLevel.Error,
                $"{who} inferred from the pictures{twice} that the text is shown as a heading, so it should be marked as one: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            NotHeading => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice} that the text is not shown as a heading, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            _ => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} could not tell from the pictures{twice} whether the text is shown as a heading: \"{first.Reason}\"; check manually",
                Inferred: true, model, cloud),
        };
    }

    /// <summary>
    /// What kept answers make of a run, for the Checks tab: the answers
    /// given by the project's model for vision-based checks to the run's
    /// requests, by their step keys. A request with no answer to its first
    /// run, or none to its second where the model is not repeatable, stays
    /// a warning, and the note counts it.
    /// </summary>
    public static PhoneHeadingLookRun FromKept(
        PhoneHeadingLookRun run,
        IReadOnlyList<HeadingLookRequest> requests,
        IReadOnlyDictionary<string, KeptAnswer> kept,
        string model)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(model);
        var answers = new Dictionary<PhoneHeadingQuestion, PhoneInference>();
        foreach (var request in requests)
        {
            if (!kept.TryGetValue(request.StepKey(1), out var firstKept) || ClosedAnswer.Read(firstKept.Output) is not { } first)
            {
                continue;
            }

            var cloud = firstKept.ModelSha256Basis == AnalysisWorker.ServiceBasis;
            var second = first;
            if (!AndroidImagesOfText.Repeatable(firstKept.ModelSha256Basis, firstKept.Parameters))
            {
                if (!kept.TryGetValue(request.StepKey(2), out var secondKept) || ClosedAnswer.Read(secondKept.Output) is not { } other)
                {
                    continue;
                }

                second = other;
            }

            answers[request.Question] = Interpret(first, second, model, cloud);
        }

        var unanswered = run.Questions.Count - answers.Count;
        var note = string.Create(CultureInfo.CurrentCulture,
            $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "is" : "are")} for {model}; {Count(answers.Count)} answered")
            + (unanswered > 0 ? string.Create(CultureInfo.CurrentCulture, $", {Count(unanswered)} not yet asked") : "");
        return run.Judged(answers, note);
    }

    private static string Count(int count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "piece" : "pieces")} of text");

    // The screen across, from a line's height above the text to 30% of the
    // screen's height below it, at full size, with the text outlined.
    private static AndroidImagesOfText.Rgb Band(PhonePixels pixels, PhoneBounds lines)
    {
        var top = Math.Clamp(lines.Top - lines.Height, 0, pixels.Height - 1);
        var bottom = Math.Clamp(lines.Bottom + (int)(pixels.Height * Below), top + 1, pixels.Height);
        var (width, height) = (pixels.Width, bottom - top);
        var result = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                result[(y * width) + x] = pixels.At(x, top + y);
            }
        }

        var (left, right, boxTop, boxBottom) = (lines.Left, lines.Right, lines.Top - top, lines.Bottom - top);
        for (var y = Math.Max(0, boxTop - Outline); y < Math.Min(height, boxBottom + Outline); y++)
        {
            for (var x = Math.Max(0, left - Outline); x < Math.Min(width, right + Outline); x++)
            {
                if (!(x >= left && x < right && y >= boxTop && y < boxBottom))
                {
                    result[(y * width) + x] = Cyan;
                }
            }
        }

        return new AndroidImagesOfText.Rgb(width, height, result);
    }
}

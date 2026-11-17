using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// One image's question as it is sent: the request, its step key for each
/// of the two runs, and the signature its answer is kept against.
/// </summary>
public sealed record ImageAlternativeRequest(PhoneImageAlternativeQuestion Question, ModelRequest Request, string Key, byte[] Signature)
{
    /// <summary>The step key of a run, 1 or 2.</summary>
    public string StepKey(int run) => $"{Key}-{run.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The question of the check of images with no text alternative for the
/// model chosen for vision-based checks (android-checks.md, step 20, rules
/// 4 and 5): for each image the rules leave
/// (<see cref="PhoneImageAlternativeRun.Questions"/>), the whole screen
/// with the image outlined and a close-up of the image, enlarged when it is
/// small, the text of the items beside it, what a screen reader says on
/// the screen with the image's place marked, and one of three fixed answers
/// with a reason. It is asked as the images-of-text check asks
/// (<see cref="AndroidImagesOfText"/>): twice, once for a model that gives
/// the same answer to the same request. Each answer is kept against the
/// image's own pixels, the text beside it and what a screen reader says on
/// the screen (<see cref="Signature"/>), so
/// an identical image, in the same run or on another recording of the
/// project, is given the first answer again, not asked again (rule 4).
/// What the answers make of the finding is the model's inference, and says
/// so.
/// </summary>
public static class AndroidImageAlternative
{
    public const string Template = "android-image-text-alternative";

    // Version 2 (2026-11-16, the owner: "Pass the screen reader's text for
    // the whole screen to the image check"): what a screen reader says on
    // the screen, and information judged against it, not the text drawn.
    // Version 3 (2026-11-17): the reason asked for before the answer
    // (ClosedAnswer.Schema; android-vision-checks.md, "Keeping it
    // repeatable", point 7).
    public const int TemplateVersion = 3;

    public const string Informative = "informative";

    public const string Decorative = "decorative";

    public const string CannotTell = "cannot-tell";

    public static readonly IReadOnlyList<string> Answers = [Informative, Decorative, CannotTell];

    public const string Instructions =
        "You judge one image in an Android app for WCAG 2.2 success criterion 1.1.1, Non-text Content: whether it gives information. " +
        "Answer only from what the pictures show and what a screen reader says, as given, with one of the fixed answers.";

    private const uint Cyan = 0x00E5FF;

    private const int Outline = 4;

    // Picture B: the image and this many pixels round it, enlarged up to
    // four times so its shorter side is at least 192 pixels, no longer
    // than 1,024 pixels.
    private const int Pad = 48;

    private const int ShortestCloseUp = 192;

    private const double MostEnlarged = 4;

    private const int LongestCloseUp = 1024;

    // The signature: its version, the image's size, the SHA-256 of the
    // text beside it, the SHA-256 of what a screen reader says on the
    // screen, and a 16 by 16 thumbnail of the image's pixels. Version 2
    // (owner, 2026-11-16: "Pass the screen reader text to the image check's
    // rule 4 matching too") added what a screen reader says.
    private const byte SignatureVersion = 2;

    private const int ThumbnailSide = 16;

    private const int SignatureLength = ThumbnailAt + (ThumbnailSide * ThumbnailSide * 3);

    // Where the thumbnail starts: after the version, the size and the two hashes.
    private const int ThumbnailAt = 1 + 4 + 4 + 32 + 32;

    /// <summary>
    /// How far, out of 255, a channel of two thumbnails may differ for the
    /// two to be the same image. Measured on 2026-11-16: six identical icons,
    /// on screenshots and video frames, differed by at most 3; a hash of
    /// the exact or rounded pixels differed between them.
    /// </summary>
    public const int ThumbnailTolerance = 16;

    /// <summary>The question's text.</summary>
    public static string Question(string app, string screenName, string nearby, string screenText) =>
        $"Picture A is the whole screen of the Android app {app}, on its screen \"{screenName}\", with one image outlined in cyan. " +
        "Picture B is the outlined image and a little of the screen around it, enlarged when the image is small.\n" +
        "The image has no text alternative, so a screen reader says nothing for it.\n" +
        (nearby.Length > 0 ? $"The text of the items beside it, as the app gives it: \"{nearby}\".\n" : "No item beside it has text.\n") +
        $"What a screen reader says on this screen, item by item in the app's order, with {PhoneImageAlternativeRun.ImageMark} at the image's place: \"{screenText}\".\n" +
        "Text that can be seen in the pictures but is not in what a screen reader says is not given to a screen reader user.\n" +
        "Which one is true of the outlined image?\n" +
        "- informative: it gives information that what a screen reader says on this screen does not, such as a photograph or illustration of the content, a chart, a status, a logo that names something, or an icon whose meaning what a screen reader says does not give.\n" +
        "- decorative: it gives no information that a screen reader user would miss: it only decorates, repeats what a screen reader already says on this screen, or is a line, a background or a flourish.\n" +
        "- cannot-tell: the pictures do not show enough to judge, such as when nothing can be seen inside the outline.\n" +
        "First write one sentence saying what in the pictures shows the answer, then give the answer.";

    /// <summary>
    /// The requests of a run's questions, in the questions' order; a
    /// question whose picture cannot be read is left out, and stays a
    /// warning.
    /// </summary>
    public static IReadOnlyList<ImageAlternativeRequest> Requests(PhoneImageAlternativeRun run, Func<string, PhonePixels?> load)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(load);
        var requests = new List<ImageAlternativeRequest>();
        var pixelsOf = new Dictionary<string, PhonePixels?>(StringComparer.OrdinalIgnoreCase);
        foreach (var question in run.Questions)
        {
            var path = question.Picture.AbsolutePath;
            if (!pixelsOf.TryGetValue(path, out var pixels))
            {
                pixelsOf[path] = pixels = load(path);
            }

            if (pixels is null || Clamp(question.Bounds, pixels) is null)
            {
                continue;
            }

            requests.Add(RequestOf(question, pixels));
        }

        return requests;
    }

    /// <summary>A question's request, from its picture's pixels.</summary>
    public static ImageAlternativeRequest RequestOf(PhoneImageAlternativeQuestion question, PhonePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(pixels);
        var box = Clamp(question.Bounds, pixels) ?? throw new ArgumentException("The image is not on the picture.", nameof(question));
        var screen = AndroidImagesOfText.Png(AndroidImagesOfText.Screen(pixels, box));
        var closeUp = AndroidImagesOfText.Png(CloseUp(pixels, box));
        var input = Question(question.App, question.ScreenName, question.Nearby, question.ScreenText);
        var signature = Signature(pixels, box, question.Nearby, question.ScreenText);
        var request = new ModelRequest(
            Template,
            TemplateVersion,
            Instructions,
            input,
            [new ModelImage("A", "image/png", screen), new ModelImage("B", "image/png", closeUp)],
            [],
            new HashSet<string>(StringComparer.Ordinal))
        {
            Use = ModelUses.VisionChecks,
            Answers = Answers,
            ReusedAcrossTasks = true,
            Signature = signature,
        };

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{Template}\n{TemplateVersion.ToString(CultureInfo.InvariantCulture)}\n{Instructions}\n{input}\n"));
        hash.AppendData(screen);
        hash.AppendData(closeUp);
        var key = "image-text-alternative-" + Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
        return new ImageAlternativeRequest(question, request, key, signature);
    }

    /// <summary>
    /// What an image's answer is kept against (rule 4): the version, the
    /// image's width and height in pixels, the SHA-256 of the text beside
    /// it, the SHA-256 of what a screen reader says on the screen, with the
    /// image's place marked, and a 16 by 16 thumbnail of its own pixels,
    /// each cell the mean of the pixels it covers.
    /// </summary>
    public static byte[] Signature(PhonePixels pixels, PhoneBounds bounds, string nearby, string screenText)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(nearby);
        ArgumentNullException.ThrowIfNull(screenText);
        var box = Clamp(bounds, pixels) ?? throw new ArgumentException("The image is not on the picture.", nameof(bounds));
        var signature = new byte[SignatureLength];
        signature[0] = SignatureVersion;
        BitConverter.TryWriteBytes(signature.AsSpan(1, 4), box.Width);
        BitConverter.TryWriteBytes(signature.AsSpan(5, 4), box.Height);
        SHA256.HashData(Encoding.UTF8.GetBytes(nearby), signature.AsSpan(9, 32));
        SHA256.HashData(Encoding.UTF8.GetBytes(screenText), signature.AsSpan(41, 32));
        var at = ThumbnailAt;
        for (var cellY = 0; cellY < ThumbnailSide; cellY++)
        {
            var top = box.Top + (cellY * box.Height / ThumbnailSide);
            var bottom = Math.Max(top + 1, box.Top + ((cellY + 1) * box.Height / ThumbnailSide));
            for (var cellX = 0; cellX < ThumbnailSide; cellX++)
            {
                var left = box.Left + (cellX * box.Width / ThumbnailSide);
                var right = Math.Max(left + 1, box.Left + ((cellX + 1) * box.Width / ThumbnailSide));
                long red = 0, green = 0, blue = 0, count = 0;
                for (var y = top; y < bottom; y++)
                {
                    for (var x = left; x < right; x++)
                    {
                        var colour = pixels.At(x, y);
                        red += (colour >> 16) & 0xFF;
                        green += (colour >> 8) & 0xFF;
                        blue += colour & 0xFF;
                        count++;
                    }
                }

                signature[at++] = (byte)(red / count);
                signature[at++] = (byte)(green / count);
                signature[at++] = (byte)(blue / count);
            }
        }

        return signature;
    }

    /// <summary>
    /// Whether two signatures are of the same image: the same version, the
    /// same width and height, the same text beside it, the same that a
    /// screen reader says on the screen, and every channel of
    /// the thumbnails within <see cref="ThumbnailTolerance"/>.
    /// </summary>
    public static bool SameImage(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        if (first.Length != SignatureLength || second.Length != SignatureLength || first[0] != SignatureVersion || second[0] != SignatureVersion ||
            !first[..ThumbnailAt].SequenceEqual(second[..ThumbnailAt]))
        {
            return false;
        }

        for (var at = ThumbnailAt; at < SignatureLength; at++)
        {
            if (Math.Abs(first[at] - second[at]) > ThumbnailTolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Asks the model chosen for vision-based checks about each image the
    /// rules leave in the audit, and gives the audit with its findings
    /// judged. An image the same as one answered before, in this run or on
    /// a task of the project (or, for a recording in no project, of the
    /// recording) by the same model, is given that answer again, copied
    /// into this task (rule 4). Where the task may use no model for
    /// vision-based checks, nothing is asked and the findings stay
    /// warnings, with the reason. A question whose step fails stays a
    /// warning, with why.
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
        if (audit.ImageAlternative is not { } run || run.Questions.Count == 0)
        {
            return audit;
        }

        if (context.NoModel(ModelUses.VisionChecks) is { } none)
        {
            return audit.WithImageAlternative(run.Judged(new Dictionary<PhoneImageAlternativeQuestion, PhoneInference>(), NotAsked(run.Questions.Count, none)));
        }

        var requests = Requests(run, load);
        var profileName = context.ModelFor(ModelUses.VisionChecks);
        var model = profileName is null ? null : context.Settings.ForProject(null).Profile(profileName)?.Model;
        var recording = context.Task.RecordingId;
        var project = context.Task.ProjectId ??
            (recording is { } id ? (await context.Catalog.ProjectOfAsync(id, cancellationToken).ConfigureAwait(false))?.ProjectId : null);
        IReadOnlyList<SignedAnswer> signed = profileName is null || model is null
            ? []
            : await context.Catalog.SignedAnswersAsync(Template, TemplateVersion, profileName, model, project, recording, cancellationToken).ConfigureAwait(false);

        // The model for complex questions, where a question the model for
        // simple ones does not settle is asked again of another, and its
        // answers for the same image (projects.md, "Simple and complex
        // questions").
        var complexName = CheckQuestions.EscalationProfile(context, ModelUses.VisionChecks);
        var complexModel = complexName is null ? null : context.Settings.ForProject(null).Profile(complexName)?.Model;
        IReadOnlyList<SignedAnswer> signedComplex = complexName is null || complexModel is null
            ? []
            : await context.Catalog.SignedAnswersAsync(Template, TemplateVersion, complexName, complexModel, project, recording, cancellationToken).ConfigureAwait(false);

        var answers = new Dictionary<PhoneImageAlternativeQuestion, PhoneInference>();
        var given = new List<(byte[] Signature, QuestionAnswers Answers)>();
        var failures = new List<string>();
        int asked = 0, again = 0, escalated = 0;
        var copied = new HashSet<Guid>();
        var held = (await context.Catalog.AttemptsAsync(context.Task.TaskId, cancellationToken).ConfigureAwait(false))
            .Where(item => item.Outcome == "accepted")
            .Select(item => item.StepKey)
            .ToHashSet(StringComparer.Ordinal);

        // An answer kept on an earlier task, copied into this task.
        async Task CopyAsync(IReadOnlyList<SignedAnswer> sources)
        {
            foreach (var source in sources.Where(source => source.TaskId != context.Task.TaskId && !held.Contains(source.StepKey) && copied.Add(source.AttemptId)))
            {
                var number = await context.Catalog.AttemptCountAsync(context.Task.TaskId, source.StepKey, cancellationToken).ConfigureAwait(false) + 1;
                await context.Catalog.ReuseAttemptAsync(source.AttemptId, context.Task.TaskId, number, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var request in requests)
        {
            // The same image answered earlier in this run.
            if (given.FirstOrDefault(item => SameImage(item.Signature, request.Signature)) is { Signature: not null } earlier)
            {
                answers[request.Question] = CheckQuestions.Judge(earlier.Answers, group => Interpret(group.One, group.Two, group.Model, group.Cloud, again: true));
                again++;
                continue;
            }

            // The same image answered on an earlier task: its first answer,
            // copied into this task.
            GroupAnswers first;
            var reused = false;
            if (Kept(signed, request.Signature) is { } kept)
            {
                await CopyAsync(kept.Sources).ConfigureAwait(false);
                first = new GroupAnswers(kept.One, kept.Two, profileName!, kept.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis);
                reused = true;
            }
            else
            {
                asked++;
                if (await CheckQuestions.AskGroupAsync(context, request.StepKey, request.Request, 1, failures, cancellationToken).ConfigureAwait(false) is not { } answered)
                {
                    continue;
                }

                first = answered;
            }

            // Not settled: the model for complex questions' answers for the
            // same image, kept or asked for.
            GroupAnswers? second = null;
            if (complexName is not null && CheckQuestions.Unsettled(first.One, first.Two, CannotTell))
            {
                if (Kept(signedComplex, request.Signature, CheckQuestions.EscalatedRun) is { } keptComplex)
                {
                    await CopyAsync(keptComplex.Sources).ConfigureAwait(false);
                    second = new GroupAnswers(keptComplex.One, keptComplex.Two, complexName, keptComplex.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis);
                }
                else
                {
                    second = await CheckQuestions.EscalateAsync(context, request.StepKey, request.Request, first, CannotTell, failures, cancellationToken).ConfigureAwait(false);
                }
            }

            var judged = new QuestionAnswers(first, second);
            escalated += second is null ? 0 : 1;
            given.Add((request.Signature, judged));
            answers[request.Question] = CheckQuestions.Judge(judged, group => Interpret(group.One, group.Two, group.Model, group.Cloud, again: reused));
            again += reused ? 1 : 0;
        }

        var note = string.Create(CultureInfo.CurrentCulture,
            $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "was" : "were")} for the model for vision-based checks; {Count(answers.Count)} answered");
        if (again > 0)
        {
            note += string.Create(CultureInfo.CurrentCulture, $", {again:N0} of them with the answer first given for the same image");
        }

        note += CheckQuestions.AskedAgain(escalated);

        if (requests.Count < run.Questions.Count)
        {
            note += string.Create(CultureInfo.CurrentCulture, $", {Count(run.Questions.Count - requests.Count)} not asked as the picture could not be read");
        }

        if (failures.Count > 0)
        {
            note += "; not answered: " + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
        }

        return audit.WithImageAlternative(run.Judged(answers, note));
    }

    /// <summary>The note of questions not asked, as no model may be used for vision-based checks.</summary>
    public static string NotAsked(int questions, string reason) =>
        string.Create(CultureInfo.CurrentCulture,
            $"{Count(questions)} left by the rules {(questions == 1 ? "was" : "were")} not asked about, as no model may be used for vision-based checks: {reason}");

    /// <summary>
    /// The first answer kept for the same image, earliest first, with both
    /// runs' answers where the model is not repeatable: the attempts it is
    /// made of and the two answers; null where there is none.
    /// </summary>
    internal static (IReadOnlyList<SignedAnswer> Sources, ClosedAnswerOf One, ClosedAnswerOf Two)? Kept(IReadOnlyList<SignedAnswer> signed, byte[] signature, int firstRun = 1)
    {
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(signature);
        var firstEnd = "-" + firstRun.ToString(CultureInfo.InvariantCulture);
        foreach (var first in signed)
        {
            if (!first.StepKey.EndsWith(firstEnd, StringComparison.Ordinal) || !SameImage(first.Signature, signature) ||
                ClosedAnswer.Read(first.Answer.Output) is not { } one)
            {
                continue;
            }

            if (AndroidImagesOfText.Repeatable(first.Answer.ModelSha256Basis, first.Answer.Parameters))
            {
                return ([first], one, one);
            }

            var secondKey = first.StepKey[..^firstEnd.Length] + "-" + (firstRun + 1).ToString(CultureInfo.InvariantCulture);
            if (signed.FirstOrDefault(item => item.TaskId == first.TaskId && item.StepKey == secondKey) is { } second &&
                ClosedAnswer.Read(second.Answer.Output) is { } two)
            {
                return ([first, second], one, two);
            }
        }

        return null;
    }

    /// <summary>
    /// What two answers make of a finding (android-checks.md, step 20, rule
    /// 5): agreed that the image gives information, an error; agreed that
    /// it is decorative, information; agreed that the pictures do not tell,
    /// or not agreed, a warning to check manually. Where the answers were
    /// first given for the same image elsewhere, it says so.
    /// </summary>
    public static PhoneInference Interpret(ClosedAnswerOf first, ClosedAnswerOf second, string model, bool cloud, bool again = false)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(model);
        var who = $"{model}, {(cloud ? "a cloud model" : "a local model")},";
        var twice = ReferenceEquals(first, second) ? "" : " twice";
        var where = again ? " for the same image, by its pixels, the text beside it and what a screen reader says on the screen," : "";
        if (first.Answer != second.Answer)
        {
            return new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} was asked twice{where} and gave two answers, {first.Answer} and {second.Answer}: \"{first.Reason}\" and \"{second.Reason}\"; check manually",
                Inferred: true, model, cloud);
        }

        return first.Answer switch
        {
            Informative => new PhoneInference(PhoneCheckLevel.Error,
                $"{who} inferred from the pictures{twice}{where} that the image gives information, so it needs a text alternative: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            Decorative => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice}{where} that the image is decorative, giving no information of its own, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            _ => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} could not tell from the pictures{twice}{where} whether the image gives information: \"{first.Reason}\"; check manually",
                Inferred: true, model, cloud),
        };
    }

    /// <summary>
    /// What kept answers make of a run, for the Checks tab: the first
    /// answers the project's model for vision-based checks gave for the
    /// same image as each request (rule 4), from its signed answers. A
    /// request with none stays a warning, and the note counts it.
    /// </summary>
    public static PhoneImageAlternativeRun FromKept(
        PhoneImageAlternativeRun run,
        IReadOnlyList<ImageAlternativeRequest> requests,
        IReadOnlyList<SignedAnswer> signed,
        string model,
        IReadOnlyList<SignedAnswer>? complexSigned = null,
        string? complexModel = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(model);
        var answers = new Dictionary<PhoneImageAlternativeQuestion, PhoneInference>();
        int waiting = 0, askedAgain = 0;
        foreach (var request in requests)
        {
            if (Kept(signed, request.Signature) is not { } kept)
            {
                continue;
            }

            var cloud = kept.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis;
            var own = kept.Sources[0].StepKey == request.StepKey(1);
            var first = new GroupAnswers(kept.One, kept.Two, model, cloud);
            GroupAnswers? escalated = null;
            if (complexModel is not null && CheckQuestions.Unsettled(kept.One, kept.Two, CannotTell))
            {
                if (Kept(complexSigned ?? [], request.Signature, CheckQuestions.EscalatedRun) is not { } again)
                {
                    waiting++;
                    continue;
                }

                escalated = new GroupAnswers(again.One, again.Two, complexModel, again.Sources[0].Answer.ModelSha256Basis == AnalysisWorker.ServiceBasis);
                askedAgain++;
            }

            answers[request.Question] = CheckQuestions.Judge(new QuestionAnswers(first, escalated), group => Interpret(group.One, group.Two, group.Model, group.Cloud, again: !own));
        }

        var unanswered = run.Questions.Count - answers.Count - waiting;
        var note = string.Create(CultureInfo.CurrentCulture,
            $"{Count(run.Questions.Count)} left by the rules {(run.Questions.Count == 1 ? "is" : "are")} for {model}; {Count(answers.Count)} answered")
            + CheckQuestions.AskedAgain(askedAgain)
            + CheckQuestions.Waiting(waiting, Count(waiting))
            + (unanswered > 0 ? string.Create(CultureInfo.CurrentCulture, $", {Count(unanswered)} not yet asked") : "");
        return run.Judged(answers, note);
    }

    private static string Count(int count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "image" : "images")}");

    // The image's bounds on the picture, or null where none of it is on it.
    private static PhoneBounds? Clamp(PhoneBounds bounds, PhonePixels pixels)
    {
        var left = Math.Clamp(bounds.Left, 0, pixels.Width);
        var top = Math.Clamp(bounds.Top, 0, pixels.Height);
        var right = Math.Clamp(bounds.Right, 0, pixels.Width);
        var bottom = Math.Clamp(bounds.Bottom, 0, pixels.Height);
        return right > left && bottom > top ? new PhoneBounds(left, top, right, bottom) : null;
    }

    // The image and the screen round it, outlined, enlarged up to four
    // times so its shorter side is at least 192 pixels, and made smaller
    // when its longer side would pass 1,024.
    private static AndroidImagesOfText.Rgb CloseUp(PhonePixels pixels, PhoneBounds box)
    {
        var left = Math.Clamp(box.Left - Pad, 0, pixels.Width - 1);
        var top = Math.Clamp(box.Top - Pad, 0, pixels.Height - 1);
        var right = Math.Clamp(box.Right + Pad, left + 1, pixels.Width);
        var bottom = Math.Clamp(box.Bottom + Pad, top + 1, pixels.Height);
        var (width, height) = (right - left, bottom - top);
        var scale = Math.Clamp(ShortestCloseUp / (double)Math.Max(1, Math.Min(box.Width, box.Height)), 1, MostEnlarged);
        scale = Math.Min(scale, LongestCloseUp / (double)Math.Max(width, height));
        var (outWidth, outHeight) = (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
        var result = new uint[outWidth * outHeight];
        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                result[(y * outWidth) + x] = pixels.At(Math.Min(right - 1, left + (int)(x / scale)), Math.Min(bottom - 1, top + (int)(y / scale)));
            }
        }

        var (boxLeft, boxTop, boxRight, boxBottom) =
            ((int)((box.Left - left) * scale), (int)((box.Top - top) * scale), (int)((box.Right - left) * scale), (int)((box.Bottom - top) * scale));
        for (var y = Math.Max(0, boxTop - Outline); y < Math.Min(outHeight, boxBottom + Outline); y++)
        {
            for (var x = Math.Max(0, boxLeft - Outline); x < Math.Min(outWidth, boxRight + Outline); x++)
            {
                if (!(x >= boxLeft && x < boxRight && y >= boxTop && y < boxBottom))
                {
                    result[(y * outWidth) + x] = Cyan;
                }
            }
        }

        return new AndroidImagesOfText.Rgb(outWidth, outHeight, result);
    }
}

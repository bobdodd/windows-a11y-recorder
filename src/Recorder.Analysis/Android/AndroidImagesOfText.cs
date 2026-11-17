using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Recorder.Session;

namespace Recorder.Analysis.Android;

/// <summary>
/// One image's question as it is sent: the request, and its step key for
/// each of the two runs.
/// </summary>
public sealed record ImageOfTextRequest(PhoneImageOfTextQuestion Question, ModelRequest Request, string Key)
{
    /// <summary>The step key of a run, 1 or 2.</summary>
    public string StepKey(int run) => $"{Key}-{run.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// The images-of-text check's question for the model chosen for
/// vision-based checks (android-vision-checks.md, "Images of text with
/// Claude, and four rules", and "Keeping it repeatable"): for each image
/// the four rules leave (<see cref="PhoneImagesOfTextRun.Questions"/>),
/// the whole screen with the image outlined and the image itself, what OCR
/// read in it and its description, and one of five fixed answers with a
/// reason. Each image is asked twice, and once where the model is
/// repeatable, a local model at temperature 0, whose second answer would be
/// the same. Answers are kept by their request, so the same image is not
/// asked about again. What the answers make of the finding is the model's
/// inference, and says so.
/// </summary>
public static class AndroidImagesOfText
{
    public const string Template = "android-image-of-text";

    // Version 2 (2026-11-17): the reason asked for before the answer
    // (ClosedAnswer.Schema; android-vision-checks.md, "Keeping it
    // repeatable", point 7).
    public const int TemplateVersion = 2;

    public const string Logotype = "logotype";

    public const string Essential = "essential";

    public const string PictureWithOtherContent = "picture-with-other-content";

    public const string CouldBeText = "could-be-text";

    public const string CannotTell = "cannot-tell";

    public static readonly IReadOnlyList<string> Answers = [Logotype, Essential, PictureWithOtherContent, CouldBeText, CannotTell];

    public const string Instructions =
        "You judge one image in an Android app for WCAG 2.2 success criterion 1.4.5, Images of Text. " +
        "Answer only from what the pictures show, with one of the fixed answers.";

    // The pictures as measured on 2026-11-15: the screen at half size with
    // a 4-pixel cyan outline, and the image at full size up to 1024 pixels
    // on its longer side.
    private const uint Cyan = 0x00E5FF;

    private const int ScreenDivisor = 2;

    private const int Outline = 4;

    private const int LongestCrop = 1024;

    /// <summary>The question's text, as measured on 2026-11-15.</summary>
    public static string Question(string app, string screenName, string ocr, string? description) =>
        $"Picture A is the whole screen of the Android app {app}, on its screen \"{screenName}\", with the image outlined in cyan. Picture B is the image itself, at full size.\n" +
        $"What OCR read inside the image: {ocr}\n" +
        (string.IsNullOrWhiteSpace(description) ? "The image has no description for a screen reader.\n" : $"The image's description for a screen reader: \"{description}\"\n") +
        "Which one is true of the text drawn in the image?\n" +
        "- logotype: the text is part of a logo or brand name.\n" +
        "- essential: this particular presentation of the text is essential to what is shown, such as a photograph of a sign, a signature, or a sample of a typeface or of a text size.\n" +
        "- picture-with-other-content: the text is part of a picture with significant other visual content, such as a photograph or an illustration, and is not the picture's main content.\n" +
        "- could-be-text: the text could be shown as real text, styled by the app, with the same look; it is an image of text.\n" +
        "- cannot-tell: the pictures do not show enough to judge.\n" +
        "First write one sentence saying what in the pictures shows the answer, then give the answer.";

    /// <summary>
    /// The requests of a run's questions, in the questions' order; a
    /// question whose picture cannot be read is left out, and stays a
    /// warning. Requests the same as one before are given once.
    /// </summary>
    public static IReadOnlyList<ImageOfTextRequest> Requests(PhoneImagesOfTextRun run, Func<string, PhonePixels?> load)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(load);
        var requests = new List<ImageOfTextRequest>();
        var pixelsOf = new Dictionary<string, PhonePixels?>(StringComparer.OrdinalIgnoreCase);
        foreach (var question in run.Questions)
        {
            var path = question.Picture.AbsolutePath;
            if (!pixelsOf.TryGetValue(path, out var pixels))
            {
                pixelsOf[path] = pixels = load(path);
            }

            if (pixels is null || question.Bounds.IsEmpty)
            {
                continue;
            }

            requests.Add(RequestOf(question, pixels));
        }

        return requests;
    }

    /// <summary>A question's request, from its picture's pixels.</summary>
    public static ImageOfTextRequest RequestOf(PhoneImageOfTextQuestion question, PhonePixels pixels)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(pixels);
        var bounds = question.Bounds;
        var screen = Png(Screen(pixels, bounds));
        var crop = Png(Crop(pixels, bounds));
        var input = Question(question.App, question.ScreenName, question.Ocr, question.Description);
        var request = new ModelRequest(
            Template,
            TemplateVersion,
            Instructions,
            input,
            [new ModelImage("A", "image/png", screen), new ModelImage("B", "image/png", crop)],
            [],
            new HashSet<string>(StringComparer.Ordinal))
        {
            Use = ModelUses.VisionChecks,
            Answers = Answers,
            ReusedAcrossTasks = true,
        };

        // The key is the request's: the same picture and words are the same
        // question, whatever recording or task they are in.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{Template}\n{TemplateVersion.ToString(CultureInfo.InvariantCulture)}\n{Instructions}\n{input}\n"));
        hash.AppendData(screen);
        hash.AppendData(crop);
        var key = "image-of-text-" + Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
        return new ImageOfTextRequest(question, request, key);
    }

    /// <summary>
    /// Asks the model chosen for vision-based checks about each image the
    /// rules leave in the audit, and gives the audit with its findings
    /// judged. Where the task may use no model for vision-based checks,
    /// nothing is asked and the findings stay warnings, with the reason.
    /// A question whose step fails stays a warning, with why.
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
        if (audit.ImagesOfText is not { } run || run.Questions.Count == 0)
        {
            return audit;
        }

        if (context.NoModel(ModelUses.VisionChecks) is { } none)
        {
            return audit.WithImagesOfText(run.Judged(new Dictionary<PhoneImageOfTextQuestion, PhoneInference>(), NotAsked(run.Questions.Count, none)));
        }

        // A repeatable model's second answer would be the first: it is not
        // asked for (android-vision-checks.md, "Keeping it repeatable"). A
        // question the model does not settle is asked again of the model
        // for complex questions where that is another (projects.md, "Simple
        // and complex questions").
        var requests = Requests(run, load);
        var failures = new List<string>();
        var answers = new Dictionary<PhoneImageOfTextQuestion, PhoneInference>();
        var escalated = 0;
        foreach (var request in requests)
        {
            if (await CheckQuestions.AskAsync(context, request.StepKey, request.Request, CannotTell, failures, cancellationToken).ConfigureAwait(false) is { } answered)
            {
                answers[request.Question] = CheckQuestions.Judge(answered, group => Interpret(group.One, group.Two, group.Model, group.Cloud));
                escalated += answered.Escalated is null ? 0 : 1;
            }
        }

        var asked = requests.Count;
        var note = string.Create(CultureInfo.CurrentCulture,
            $"{Count(run.Questions.Count, "image")} left by the rules {(run.Questions.Count == 1 ? "was" : "were")} for the model for vision-based checks; {Count(answers.Count, "image")} answered")
            + CheckQuestions.AskedAgain(escalated);
        if (asked < run.Questions.Count)
        {
            note += string.Create(CultureInfo.CurrentCulture, $", {Count(run.Questions.Count - asked, "image")} not asked as the picture could not be read");
        }

        if (failures.Count > 0)
        {
            note += "; not answered: " + string.Join(" ", failures.Distinct(StringComparer.Ordinal));
        }

        return audit.WithImagesOfText(run.Judged(answers, note));
    }

    /// <summary>The note of questions not asked, as no model may be used for vision-based checks.</summary>
    public static string NotAsked(int questions, string reason) =>
        string.Create(CultureInfo.CurrentCulture,
            $"{Count(questions, "image")} left by the rules {(questions == 1 ? "was" : "were")} not asked about, as no model may be used for vision-based checks: {reason}");

    /// <summary>
    /// Whether an attempt's model gives the same answer to the same
    /// request: a local model at temperature 0. A service's model has no
    /// seed and takes no temperature (cloud-models.md, "5. Repeatability").
    /// </summary>
    public static bool Repeatable(string? modelSha256Basis, string parameters)
    {
        if (modelSha256Basis == AnalysisWorker.ServiceBasis || string.IsNullOrEmpty(parameters))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(parameters) is JsonObject settings &&
                settings["temperature"] is JsonValue temperature &&
                temperature.TryGetValue<double>(out var value) && value == 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// What two answers make of an image's findings (android-vision-checks.md,
    /// "Images of text with Claude, and four rules"): agreed that an
    /// exception applies, information; agreed that the text could be real
    /// text, an error, with customizing not judged; agreed that the
    /// pictures do not tell, or not agreed, a warning to check manually.
    /// The 1.4.9 finding of an image takes the same: a logotype is
    /// essential, and text in a picture with significant other visual
    /// content is not an image of text by WCAG's definition.
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
            Logotype => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice} that the text is part of a logo or brand name, so the exception for logotypes applies, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            Essential => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice} that this presentation of the text is essential, so the exception applies, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            PictureWithOtherContent => new PhoneInference(PhoneCheckLevel.Info,
                $"{who} inferred from the pictures{twice} that the text is part of a picture with significant other visual content, so it is not an image of text, not confirmed: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            CouldBeText => new PhoneInference(PhoneCheckLevel.Error,
                $"{who} inferred from the pictures{twice} that the text could be shown as real text with the same look, so it is an image of text; whether a user can customize it is not judged: \"{first.Reason}\"",
                Inferred: true, model, cloud),
            _ => new PhoneInference(PhoneCheckLevel.Warning,
                $"{who} could not tell from the pictures{twice} whether an exception applies: \"{first.Reason}\"; check manually",
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
    public static PhoneImagesOfTextRun FromKept(
        PhoneImagesOfTextRun run,
        IReadOnlyList<ImageOfTextRequest> requests,
        IReadOnlyDictionary<string, KeptAnswer> kept,
        string model,
        IReadOnlyDictionary<string, KeptAnswer>? complexKept = null,
        string? complexModel = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(model);
        var answers = new Dictionary<PhoneImageOfTextQuestion, PhoneInference>();
        int waiting = 0, askedAgain = 0;
        foreach (var request in requests)
        {
            if (CheckQuestions.FromKept(kept, request.StepKey, 1, model) is not { } first)
            {
                continue;
            }

            GroupAnswers? escalated = null;
            if (complexModel is not null && CheckQuestions.Unsettled(first.One, first.Two, CannotTell))
            {
                if (CheckQuestions.FromKept(complexKept ?? new Dictionary<string, KeptAnswer>(), request.StepKey, CheckQuestions.EscalatedRun, complexModel) is not { } again)
                {
                    waiting++;
                    continue;
                }

                escalated = again;
                askedAgain++;
            }

            answers[request.Question] = CheckQuestions.Judge(new QuestionAnswers(first, escalated), group => Interpret(group.One, group.Two, group.Model, group.Cloud));
        }

        var unanswered = run.Questions.Count - answers.Count - waiting;
        var note = string.Create(CultureInfo.CurrentCulture,
            $"{Count(run.Questions.Count, "image")} left by the rules {(run.Questions.Count == 1 ? "is" : "are")} for {model}; {Count(answers.Count, "image")} answered")
            + CheckQuestions.AskedAgain(askedAgain)
            + CheckQuestions.Waiting(waiting, Count(waiting, "image"))
            + (unanswered > 0 ? string.Create(CultureInfo.CurrentCulture, $", {Count(unanswered, "image")} not yet asked") : "");
        return run.Judged(answers, note);
    }

    private static string Count(int count, string one) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {one}{(count == 1 ? "" : "s")}");

    internal sealed record Rgb(int Width, int Height, uint[] Pixels);

    // The image at full size, every divisor-th pixel where its longer side
    // is more than 1024 pixels.
    private static Rgb Crop(PhonePixels pixels, PhoneBounds bounds)
    {
        var left = Math.Clamp(bounds.Left, 0, pixels.Width - 1);
        var top = Math.Clamp(bounds.Top, 0, pixels.Height - 1);
        var right = Math.Clamp(bounds.Right, left + 1, pixels.Width);
        var bottom = Math.Clamp(bounds.Bottom, top + 1, pixels.Height);
        var (width, height) = (right - left, bottom - top);
        var divisor = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / (double)LongestCrop));
        var (outWidth, outHeight) = (Math.Max(1, width / divisor), Math.Max(1, height / divisor));
        var result = new uint[outWidth * outHeight];
        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                result[(y * outWidth) + x] = pixels.At(left + (x * divisor), top + (y * divisor));
            }
        }

        return new Rgb(outWidth, outHeight, result);
    }

    // The whole screen at half size, with the image outlined in cyan
    // outside its bounds.
    internal static Rgb Screen(PhonePixels pixels, PhoneBounds bounds)
    {
        var (outWidth, outHeight, result) = Whole(pixels);
        var (left, top, right, bottom) = (bounds.Left / ScreenDivisor, bounds.Top / ScreenDivisor, bounds.Right / ScreenDivisor, bounds.Bottom / ScreenDivisor);
        for (var y = Math.Max(0, top - Outline); y < Math.Min(outHeight, bottom + Outline); y++)
        {
            for (var x = Math.Max(0, left - Outline); x < Math.Min(outWidth, right + Outline); x++)
            {
                if (!(x >= left && x < right && y >= top && y < bottom))
                {
                    result[(y * outWidth) + x] = Cyan;
                }
            }
        }

        return new Rgb(outWidth, outHeight, result);
    }

    // The whole screen at half size, with nothing outlined: the screen
    // title check's picture A.
    internal static Rgb Screen(PhonePixels pixels)
    {
        var (outWidth, outHeight, result) = Whole(pixels);
        return new Rgb(outWidth, outHeight, result);
    }

    private static (int Width, int Height, uint[] Pixels) Whole(PhonePixels pixels)
    {
        var (outWidth, outHeight) = (Math.Max(1, pixels.Width / ScreenDivisor), Math.Max(1, pixels.Height / ScreenDivisor));
        var result = new uint[outWidth * outHeight];
        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                result[(y * outWidth) + x] = pixels.At(x * ScreenDivisor, y * ScreenDivisor);
            }
        }

        return (outWidth, outHeight, result);
    }

    // An RGB PNG, the same bytes for the same pixels.
    internal static byte[] Png(Rgb image)
    {
        var raw = new byte[((image.Width * 3) + 1) * image.Height];
        var at = 0;
        for (var y = 0; y < image.Height; y++)
        {
            raw[at++] = 0;
            for (var x = 0; x < image.Width; x++)
            {
                var colour = image.Pixels[(y * image.Width) + x];
                raw[at++] = (byte)(colour >> 16);
                raw[at++] = (byte)(colour >> 8);
                raw[at++] = (byte)colour;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, image.Width);
        WriteBigEndian(header, 4, image.Height);
        header[8] = 8;
        header[9] = 2;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, (int)Crc([.. typeBytes, .. data]));
        stream.Write(crc);
    }

    private static uint Crc(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}

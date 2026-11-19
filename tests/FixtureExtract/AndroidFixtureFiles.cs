using System.Text.Encodings.Web;
using System.Text.Json;
using Recorder.Analysis.Android;
using Recorder.Session;

namespace Recorder.FixtureExtract;

/// <summary>
/// The files of an extracted fixture recording (android-fixtures.md, "Rule
/// tests"), shared by the extractor, which writes them on the PC, and the
/// fixture tests, which read them in any build.
/// </summary>
public static class AndroidFixtureFiles
{
    /// <summary>The trimmed recording file.</summary>
    public const string Recording = "recording.mcap";

    /// <summary>What Windows OCR read in each picture the checks read.</summary>
    public const string Ocr = "ocr.json";

    /// <summary>The findings the rules made on the PC, with Windows OCR.</summary>
    public const string Findings = "findings.json";

    /// <summary>The reason given for the model checks, which the fixture tests do not ask.</summary>
    public const string NoModel = "The fixture tests ask no model.";

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>A path inside a fixture folder, relative to it, with forward slashes.</summary>
    public static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>
    /// The rules' findings, as the fixture tests compare them: every visit's,
    /// in the order of the visits, then the app's, with no model asked.
    /// </summary>
    public static IReadOnlyList<FixtureFinding> FindingsOf(SessionPlaybackArchive archive, AndroidAudit audit)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(audit);
        var visits = archive.PhoneScreens.Visits
            .SelectMany(visit => audit.For(visit)
                .OrderBy(finding => finding.Check, StringComparer.Ordinal)
                .ThenBy(finding => finding.Identity, StringComparer.Ordinal)
                .Select(finding => Of(finding, app: false)));
        var app = audit.AppFindings
            .OrderBy(finding => finding.Check, StringComparer.Ordinal)
            .ThenBy(finding => finding.Identity, StringComparer.Ordinal)
            .Select(finding => Of(finding, app: true));
        return [.. visits, .. app];
    }

    private static FixtureFinding Of(PhoneFinding finding, bool app) => new(
        app,
        finding.Visit.Number,
        finding.Screen.Title,
        finding.Screen.Package,
        finding.Check,
        finding.Level.ToString(),
        finding.Identity,
        finding.Label,
        finding.Times.FirstOrDefault()?.Measured);
}

/// <summary>A finding as an extracted fixture keeps it.</summary>
public sealed record FixtureFinding(
    bool App,
    int Visit,
    string? Screen,
    string Package,
    string Check,
    string Level,
    string Identity,
    string Label,
    string? Measured);

/// <summary>What OCR read in the pictures of a fixture recording.</summary>
public sealed record FixtureOcr(int Version, string? Unavailable, IReadOnlyList<FixtureOcrPicture> Pictures);

/// <summary>What OCR read in one picture, by its path in the fixture folder, or why it could not.</summary>
public sealed record FixtureOcrPicture(string Path, string? Engine, IReadOnlyList<PhoneOcrWord>? Words, string? Problem);

/// <summary>
/// A text reader that keeps what another reads, so that the readings can
/// be written with the fixture.
/// </summary>
public sealed class RecordingTextReader(IPhoneTextReader inner, string root) : IPhoneTextReader
{
    private readonly List<FixtureOcrPicture> _pictures = [];

    public string? Unavailable => inner.Unavailable;

    public IReadOnlyList<FixtureOcrPicture> Pictures => _pictures;

    public PhoneOcrReading Read(string path)
    {
        var relative = AndroidFixtureFiles.Relative(root, path);
        try
        {
            var reading = inner.Read(path);
            _pictures.Add(new FixtureOcrPicture(relative, reading.Engine, reading.Words, null));
            return reading;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            _pictures.Add(new FixtureOcrPicture(relative, null, null, exception.Message));
            throw;
        }
    }

    public FixtureOcr ToFile() => new(1, Unavailable, [.. _pictures.OrderBy(picture => picture.Path, StringComparer.Ordinal)]);
}

/// <summary>
/// A text reader that gives back what OCR read on the PC, so the checks
/// run with Windows OCR's readings where Windows OCR is not.
/// </summary>
public sealed class RecordedTextReader : IPhoneTextReader
{
    private readonly string _root;
    private readonly Dictionary<string, FixtureOcrPicture> _pictures;

    public RecordedTextReader(FixtureOcr ocr, string root)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        _root = root;
        Unavailable = ocr.Unavailable;
        _pictures = ocr.Pictures.ToDictionary(picture => picture.Path, StringComparer.Ordinal);
    }

    public string? Unavailable { get; }

    public static RecordedTextReader Load(string folder)
    {
        var ocr = JsonSerializer.Deserialize<FixtureOcr>(File.ReadAllBytes(Path.Combine(folder, AndroidFixtureFiles.Ocr)), AndroidFixtureFiles.Json) ??
            throw new InvalidDataException($"{AndroidFixtureFiles.Ocr} is empty.");
        return new RecordedTextReader(ocr, folder);
    }

    public PhoneOcrReading Read(string path)
    {
        var relative = AndroidFixtureFiles.Relative(_root, path);
        if (!_pictures.TryGetValue(relative, out var picture))
        {
            throw new InvalidDataException($"OCR's reading of {relative} was not kept with the fixture.");
        }

        return picture.Problem is { } problem
            ? throw new InvalidDataException(problem)
            : new PhoneOcrReading(picture.Words ?? [], picture.Engine ?? string.Empty);
    }
}

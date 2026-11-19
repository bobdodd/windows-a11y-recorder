using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Recorder.Analysis.Android;
using Recorder.Session;

namespace Recorder.FixtureExtract;

/// <summary>
/// The files of an extracted fixture recording (android-fixtures.md, "Rule
/// tests"), shared by the extractor, which writes them on the PC, and the
/// fixture tests, which read them in any build.
/// </summary>
public static partial class AndroidFixtureFiles
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

    /// <summary>
    /// What a fixture recording holds in place of the phone's serial number,
    /// which a public repository must not hold (owner, 2026-11-19).
    /// </summary>
    public const string Serial = "fixture-phone";

    /// <summary>
    /// What a fixture recording holds in place of a phone setting that names
    /// another of the phone's apps, such as its preferred speech engine.
    /// </summary>
    public const string LeftOut = "left out";

    /// <summary>
    /// The apps a phone setting in a fixture recording may name: the fixture
    /// app, TalkBack and the recorder's companion app.
    /// </summary>
    public static IReadOnlyList<string> SettingPackages(string fixture) =>
        [fixture, "com.google.android.marvin.talkback", "io.github.bobdodd.a11yrecorder.companion"];

    /// <summary>The phone serial numbers an event's payload names, in its "serial" values.</summary>
    public static void AddSerials(JsonElement payload, ISet<string> serials)
    {
        ArgumentNullException.ThrowIfNull(serials);
        switch (payload.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in payload.EnumerateObject())
                {
                    if (property.Name == "serial" && property.Value.ValueKind == JsonValueKind.String &&
                        property.Value.GetString() is { Length: > 0 } serial && serial != Serial)
                    {
                        serials.Add(serial);
                    }
                    else
                    {
                        AddSerials(property.Value, serials);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in payload.EnumerateArray())
                {
                    AddSerials(item, serials);
                }

                break;
        }
    }

    /// <summary>
    /// An event's payload without the phone's own details: every serial
    /// number named is replaced by <see cref="Serial"/> wherever it appears,
    /// and, in the phone's settings, every value naming an app other than
    /// <paramref name="packages"/> by <see cref="LeftOut"/>. The payload is
    /// returned as it was when there is nothing to replace. The counts of
    /// what was replaced are added to <paramref name="replaced"/>.
    /// </summary>
    public static JsonElement WithoutThePhonesOwn(
        string channel,
        JsonElement payload,
        IReadOnlyCollection<string> serials,
        IReadOnlyCollection<string> packages,
        IDictionary<string, int> replaced)
    {
        ArgumentNullException.ThrowIfNull(serials);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(replaced);
        if (payload.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return payload;
        }

        var settings = channel == "preferences.android";
        var raw = payload.GetRawText();
        if (!serials.Any(serial => raw.Contains(serial, StringComparison.Ordinal)) && !(settings && NamesAnotherApp(raw, packages)))
        {
            return payload;
        }

        var node = JsonNode.Parse(raw)!;
        var counts = new int[2];
        node = Rewrite(node, settings, serials, packages, counts)!;
        if (counts[0] > 0)
        {
            replaced[SerialsReplaced] = replaced.TryGetValue(SerialsReplaced, out var events) ? events + 1 : 1;
        }

        if (counts[1] > 0)
        {
            replaced[SettingsLeftOut] = (replaced.TryGetValue(SettingsLeftOut, out var values) ? values : 0) + counts[1];
        }

        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>The name under which events with a serial number replaced are counted.</summary>
    public const string SerialsReplaced = "events with the phone's serial number replaced";

    /// <summary>The name under which settings naming another app are counted.</summary>
    public const string SettingsLeftOut = "phone settings naming another app left out";

    /// <summary>Whether a text names an app, by its package name, other than <paramref name="packages"/>.</summary>
    public static bool NamesAnotherApp(string text, IReadOnlyCollection<string> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return PackageName().Matches(text ?? string.Empty).Any(match =>
            !packages.Any(package => match.Value == package || match.Value.StartsWith(package + ".", StringComparison.Ordinal)));
    }

    private static JsonNode? Rewrite(JsonNode? node, bool settings, IReadOnlyCollection<string> serials, IReadOnlyCollection<string> packages, int[] counts)
    {
        switch (node)
        {
            case JsonObject item:
                foreach (var name in item.Select(property => property.Key).ToList())
                {
                    var child = item[name];
                    var rewritten = Rewrite(child, settings, serials, packages, counts);
                    if (!ReferenceEquals(rewritten, child))
                    {
                        item[name] = rewritten;
                    }
                }

                return item;
            case JsonArray list:
                for (var index = 0; index < list.Count; index++)
                {
                    var child = list[index];
                    var rewritten = Rewrite(child, settings, serials, packages, counts);
                    if (!ReferenceEquals(rewritten, child))
                    {
                        list[index] = rewritten;
                    }
                }

                return list;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                var text = value.GetValue<string>();
                if (settings && NamesAnotherApp(text, packages))
                {
                    counts[1]++;
                    return JsonValue.Create(LeftOut);
                }

                var without = text;
                foreach (var serial in serials)
                {
                    without = without.Replace(serial, Serial, StringComparison.Ordinal);
                }

                if (without == text)
                {
                    return value;
                }

                counts[0]++;
                return JsonValue.Create(without);
            default:
                return node;
        }
    }

    // A package name, such as com.example.app: two or more dotted parts,
    // each starting with a letter, so that numbers such as 2.0 are not one.
    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+\b")]
    private static partial Regex PackageName();

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

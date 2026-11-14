using System.Security.Cryptography;
using System.Text.Json;

namespace Recorder.Analysis.Android;

/// <summary>One step of the impact scale: its score, 4 down to 1, its word and what it means.</summary>
public sealed record GuidanceImpact(int Score, string Word, string Meaning);

/// <summary>A source the guidance rests on.</summary>
public sealed record GuidanceSource(string Title, string Url);

/// <summary>A WCAG success criterion the guidance names: its name, level and the key of its Understanding page.</summary>
public sealed record GuidanceCriterion(string Name, string Level, string Source);

/// <summary>
/// The fixed text for one check (android-report.md, "Each finding"): its
/// title, what the issue is, why it matters, who it affects, its impact
/// once confirmed, the success criteria it fails and those it relates to,
/// what the check rests on, the remediation for Android, Flutter and React
/// Native, and its sources.
/// </summary>
public sealed record CheckGuidance(
    string Check,
    string Title,
    string Issue,
    string Why,
    IReadOnlyList<string> Affects,
    int Impact,
    IReadOnlyList<string> Criteria,
    IReadOnlyList<string> Related,
    string Basis,
    string Android,
    string Flutter,
    string ReactNative,
    IReadOnlyList<string> Sources);

/// <summary>
/// The guidance for every Android check, read from the file built into the
/// program, android-check-guidance.json, so that every report says the same
/// for the same check and names the file it came from by its SHA-256
/// (android-report.md, "The guidance file").
/// </summary>
public sealed class AndroidGuidance
{
    public const string ResourceName = "Recorder.Analysis.Android.android-check-guidance.json";

    private static readonly Lazy<AndroidGuidance> Builtin = new(() =>
    {
        using var stream = typeof(AndroidGuidance).Assembly.GetManifestResourceStream(ResourceName) ??
            throw new InvalidOperationException("The Android check guidance is not in the program.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Parse(copy.ToArray());
    });

    private readonly Dictionary<string, CheckGuidance> _checks;

    private AndroidGuidance(
        int version,
        string sha256,
        IReadOnlyList<GuidanceImpact> impact,
        IReadOnlyDictionary<string, GuidanceSource> sources,
        IReadOnlyDictionary<string, GuidanceCriterion> criteria,
        IReadOnlyList<CheckGuidance> checks)
    {
        Version = version;
        Sha256 = sha256;
        Impact = impact;
        Sources = sources;
        Criteria = criteria;
        Checks = checks;
        _checks = checks.ToDictionary(check => check.Check, StringComparer.Ordinal);
    }

    /// <summary>The guidance built into the program.</summary>
    public static AndroidGuidance Load() => Builtin.Value;

    public int Version { get; }

    /// <summary>The SHA-256 of the guidance file, in lower-case hexadecimal.</summary>
    public string Sha256 { get; }

    /// <summary>The impact scale, from 4, critical, down to 1, minor.</summary>
    public IReadOnlyList<GuidanceImpact> Impact { get; }

    public IReadOnlyDictionary<string, GuidanceSource> Sources { get; }

    public IReadOnlyDictionary<string, GuidanceCriterion> Criteria { get; }

    public IReadOnlyList<CheckGuidance> Checks { get; }

    /// <summary>A check's guidance, or null for a check the file does not cover.</summary>
    public CheckGuidance? For(string check) => _checks.GetValueOrDefault(check);

    /// <summary>The impact scale's step for a score.</summary>
    public GuidanceImpact ImpactOf(int score) =>
        Impact.FirstOrDefault(step => step.Score == score) ?? new GuidanceImpact(score, "unscored", "Not on the scale.");

    /// <summary>A criterion as the report names it, such as "2.5.8 Target Size (Minimum) (AA)".</summary>
    public string CriterionText(string number) =>
        Criteria.TryGetValue(number, out var criterion) ? $"{number} {criterion.Name} ({criterion.Level})" : number;

    internal static AndroidGuidance Parse(byte[] json)
    {
        var file = JsonSerializer.Deserialize<GuidanceFile>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
            throw new InvalidDataException("The Android check guidance is empty.");
        return new AndroidGuidance(
            file.Version,
            Convert.ToHexStringLower(SHA256.HashData(json)),
            [.. file.Impact.OrderByDescending(step => step.Score)],
            file.Sources,
            file.Criteria,
            file.Checks);
    }

    private sealed record GuidanceFile(
        int Version,
        string Note,
        List<GuidanceImpact> Impact,
        Dictionary<string, GuidanceSource> Sources,
        Dictionary<string, GuidanceCriterion> Criteria,
        List<CheckGuidance> Checks);
}

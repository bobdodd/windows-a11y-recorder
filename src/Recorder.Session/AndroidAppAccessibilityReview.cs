using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>A data-only APK or source review, not a runtime observation or a finding.</summary>
public sealed record AndroidAppAccessibilityReview(
    int SchemaVersion, string Package, string VersionName, long VersionCode,
    DateTimeOffset ReviewedUtc, string Method, IReadOnlyList<AndroidAppAccessibilityObservation> Observations);

public sealed record AndroidAppAccessibilityObservation(string Topic, string Evidence, string Limit);

/// <summary>Attaches a reviewed app version to one recording without altering its capture data.</summary>
public static class AndroidAppAccessibilityReviews
{
    private const int MaxBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, MaxDepth = 12 };

    public static AndroidAppAccessibilityReview Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length is 0 or > MaxBytes)
        {
            throw new InvalidDataException("The app accessibility review must be a JSON file up to 64 KiB.");
        }

        var buffer = new byte[(int)stream.Length];
        stream.ReadExactly(buffer);
        var review = JsonSerializer.Deserialize<AndroidAppAccessibilityReview>(buffer, Json);
        return review is not null && Valid(review)
            ? review
            : throw new InvalidDataException("The app accessibility review is missing required version, date or evidence fields.");
    }

    public static bool Matches(AndroidAppAccessibilityReview review, AndroidAppVersion? version) =>
        version is { Found: true } && review.Package == version.Package &&
        review.VersionName == version.VersionName && review.VersionCode == version.VersionCode;

    public static void Attach(SessionPlaybackArchive archive, AndroidAppAccessibilityReview review)
    {
        if (!Valid(review) || !Matches(review, archive.PhoneDevice.VersionOf(review.Package)))
        {
            throw new InvalidDataException("The review's package, version and version code must match the app version saved in this recording.");
        }

        var data = JsonSerializer.SerializeToUtf8Bytes(review, Json);
        if (data.Length > MaxBytes)
        {
            throw new InvalidDataException("The app accessibility review is too large to attach.");
        }

        var target = PathFor(archive, review.Package);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, target, overwrite: false);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static AndroidAppAccessibilityReview? Attached(SessionPlaybackArchive archive, string package)
    {
        var path = PathFor(archive, package);
        if (!File.Exists(path))
        {
            return null;
        }

        var review = Read(path);
        if (review.Package != package || !Matches(review, archive.PhoneDevice.VersionOf(package)))
        {
            throw new InvalidDataException("An attached app accessibility review no longer matches the recorded package and version.");
        }

        return review;
    }

    private static string PathFor(SessionPlaybackArchive archive, string package) =>
        Path.Combine(archive.SessionDirectory,
            $"android-app-accessibility-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(package)))}.json");

    private static bool Valid(AndroidAppAccessibilityReview review) =>
        review.SchemaVersion == 1 && Text(review.Package, 255) && Text(review.VersionName, 200) &&
        review.VersionCode >= 0 && review.ReviewedUtc != default && Text(review.Method, 600) &&
        review.Observations is { Count: > 0 and <= 64 } &&
        review.Observations.All(item => item is not null && Text(item.Topic, 160) &&
            Text(item.Evidence, 1500) && Text(item.Limit, 1500));

    private static bool Text(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Any(char.IsControl);
}

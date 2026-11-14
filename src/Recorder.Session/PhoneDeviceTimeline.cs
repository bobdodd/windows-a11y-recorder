using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>Where a period's density came from.</summary>
public enum PhoneDensitySource
{
    /// <summary>The phone's own density, as <c>wm density</c> printed it.</summary>
    Physical,

    /// <summary>The "Display size" setting (<c>display_density_forced</c>, or wm density's override).</summary>
    DisplaySize
}

/// <summary>A period in which the phone's display had one density.</summary>
/// <param name="Start">The recording time it starts: 0 for the density read at the start.</param>
/// <param name="End">The recording time the next period starts, or the recording's end.</param>
/// <param name="Dpi">Dots per inch; dp are pixels times 160 over this.</param>
/// <param name="FoundAtStop">True when only the read at the stop showed it, with no time of its own.</param>
public sealed record PhoneDensityPeriod(long Start, long End, int Dpi, PhoneDensitySource Source, bool FoundAtStop)
{
    /// <summary>How many pixels make 1 dp.</summary>
    public double PixelsPerDp => Dpi / (double)AndroidDeviceRecords.BaselineDensity;

    public string When =>
        $"{WindowsPreferenceTimeline.Clock(Start)} to {WindowsPreferenceTimeline.Clock(End)}";
}

/// <summary>
/// The phone's display density through a recording, and the versions of
/// its apps (android-checks.md, step 2), from the device collector's
/// records and the settings collector's <c>display_density_forced</c>
/// changes, as <see cref="AndroidDeviceRecords"/> describes: the density
/// read at the start holds from the recording's start; each change of the
/// "Display size" setting starts a period with its number, or with the
/// physical density when it is cleared or set to it; and a density read at the stop
/// that differs from the last starts one more period there.
/// </summary>
public sealed class PhoneDeviceTimeline
{
    private const string DensitySetting = "secure/display_density_forced";

    private readonly Dictionary<string, AndroidAppVersion> _versions = new(StringComparer.Ordinal);

    /// <param name="device">The device.android records, by recording time.</param>
    /// <param name="settings">The preferences.android records, by recording time.</param>
    /// <param name="duration">The recording's length, where the last period ends.</param>
    public PhoneDeviceTimeline(
        IEnumerable<(long Time, string EventType, JsonElement Payload)> device,
        IEnumerable<(long Time, string EventType, JsonElement Payload)> settings,
        long duration)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(settings);
        AndroidDisplayDensity? start = null;
        AndroidDisplayDensity? stop = null;
        long stopTime = 0;
        foreach (var (time, eventType, payload) in device.OrderBy(record => record.Time))
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (eventType == AndroidDeviceRecords.DensityEventType &&
                Read<AndroidDisplayDensity>(payload) is { InUseDpi: not null } density)
            {
                if (density.Reason == AndroidDeviceRecords.StartReason)
                {
                    start ??= density;
                }
                else
                {
                    stop = density;
                    stopTime = time;
                }
            }
            else if (eventType == AndroidDeviceRecords.AppVersionsEventType &&
                Read<AndroidAppVersions>(payload) is { } versions)
            {
                VersionsRecorded = true;
                foreach (var app in versions.Apps)
                {
                    _versions[app.Package] = app;
                }
            }
        }

        var first = start ?? stop;
        if (first?.InUseDpi is not { } firstDpi)
        {
            return;
        }

        var physical = first.PhysicalDpi ?? firstDpi;
        var periods = new List<PhoneDensityPeriod>
        {
            new(0, duration, firstDpi, first.OverrideDpi is null ? PhoneDensitySource.Physical : PhoneDensitySource.DisplaySize, start is null)
        };

        void Add(long time, int dpi, PhoneDensitySource source, bool foundAtStop)
        {
            var last = periods[^1];
            if (last.Dpi == dpi)
            {
                return;
            }

            time = Math.Clamp(time, last.Start, duration);
            periods[^1] = last with { End = time };
            periods.Add(new PhoneDensityPeriod(time, duration, dpi, source, foundAtStop));
        }

        if (start is not null)
        {
            foreach (var (time, eventType, payload) in settings.OrderBy(record => record.Time))
            {
                if (eventType != AndroidSettingsRecords.ChangeEventType ||
                    payload.ValueKind != JsonValueKind.Object ||
                    !payload.TryGetProperty("setting", out var setting) ||
                    setting.ValueKind != JsonValueKind.String ||
                    setting.GetString() != DensitySetting)
                {
                    continue;
                }

                var value = payload.TryGetProperty("value", out var read) && read.ValueKind == JsonValueKind.String
                    ? read.GetString()
                    : null;
                var foundAtStop = payload.TryGetProperty("source", out var source) &&
                    source.ValueKind == JsonValueKind.String &&
                    source.GetString() == AndroidSettingsRecords.StopSource;
                // A Display size set back to the phone's own density is the
                // phone's own, as wm density then prints no override.
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var forced) && forced > 0 && forced != physical)
                {
                    Add(time, forced, PhoneDensitySource.DisplaySize, foundAtStop);
                }
                else
                {
                    Add(time, physical, PhoneDensitySource.Physical, foundAtStop);
                }
            }

            if (stop?.InUseDpi is { } stopDpi)
            {
                Add(stopTime, stopDpi, stop.OverrideDpi is null ? PhoneDensitySource.Physical : PhoneDensitySource.DisplaySize, true);
            }
        }

        Densities = periods;
    }

    public static PhoneDeviceTimeline Empty { get; } = new([], [], 0);

    /// <summary>The density's periods, in order; empty when the recording holds no density.</summary>
    public IReadOnlyList<PhoneDensityPeriod> Densities { get; } = [];

    /// <summary>Whether the recording holds the apps' versions.</summary>
    public bool VersionsRecorded { get; }

    /// <summary>The density in use at a recording time, or null when none was recorded.</summary>
    public PhoneDensityPeriod? DensityAt(long time)
    {
        PhoneDensityPeriod? found = null;
        foreach (var period in Densities)
        {
            if (period.Start > time)
            {
                break;
            }

            found = period;
        }

        return found ?? (Densities.Count > 0 ? Densities[0] : null);
    }

    /// <summary>A package's version as read at the stop, or null when it was not read.</summary>
    public AndroidAppVersion? VersionOf(string package) =>
        _versions.GetValueOrDefault(package);

    /// <summary>
    /// Framework metadata read at the stop. Unknown is not a native-Android
    /// classification; a match describes bundled code, not every screen.
    /// </summary>
    public string DescribeFramework(string package)
    {
        var result = VersionOf(package)?.Framework;
        if (result is null)
        {
            return "Framework not recorded.";
        }

        var description = result.Status switch
        {
            "detected" when result.Names.Count > 0 =>
                $"Framework: {string.Join(", ", result.Names)} (bundled APK library detected at the stop; use by this screen is not established).",
            "unknown" => "Framework unknown: no supported Flutter or React Native library marker was found.",
            _ => "Framework unavailable."
        };
        description += string.Create(CultureInfo.CurrentCulture, $" APKs read: {result.ApksRead} of {result.ApksTotal}.");
        if (result.Evidence.Count > 0)
        {
            description += " Evidence: " + string.Join("; ", result.Evidence) + ".";
        }

        if (result.Problem is { Length: > 0 } problem)
        {
            description += " Incomplete check: " + problem;
        }

        return description;
    }

    /// <summary>
    /// A package's version in words, such as "version 1.0.3 (code 12),
    /// targets API 35", or why there is none.
    /// </summary>
    public string DescribeVersion(string package)
    {
        if (!VersionsRecorded)
        {
            return "version not recorded";
        }

        if (VersionOf(package) is not { } app)
        {
            return "version not read";
        }

        if (!app.Found)
        {
            return "not found at the stop";
        }

        var parts = new List<string>
        {
            app.VersionName is { Length: > 0 } name ? "version " + name : "no version name"
        };
        if (app.VersionCode is { } code)
        {
            parts[0] += string.Create(CultureInfo.CurrentCulture, $" (code {code})");
        }

        if (app.TargetSdk is { } target)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"targets API {target}"));
        }

        return string.Join(", ", parts);
    }

    private static T? Read<T>(JsonElement payload)
        where T : class
    {
        try
        {
            return payload.Deserialize<T>(JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

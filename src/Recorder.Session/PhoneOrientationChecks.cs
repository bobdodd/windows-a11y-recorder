using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>How the phone was held from a time: a quarter turn, or -1 for flat or unknown.</summary>
public sealed record PhoneHeld(long Time, int Orientation, int Degrees);

/// <summary>The phone display's rotation from a time, with the screen's size as it was turned.</summary>
public sealed record PhoneRotation(long Time, int Rotation, int Width, int Height);

/// <summary>
/// How the phone was held through a recording, from the companion app's
/// accelerometer (android.orientation, android-checks.md, step 15), and how
/// its display was turned, from the companion's display records; the
/// playback index keeps both whole.
/// </summary>
public sealed class PhoneOrientationTimeline
{
    public PhoneOrientationTimeline(
        IEnumerable<(long Time, JsonElement Payload)> orientations,
        IEnumerable<(long Time, JsonElement Payload)> displays)
    {
        ArgumentNullException.ThrowIfNull(orientations);
        ArgumentNullException.ThrowIfNull(displays);
        var held = new List<PhoneHeld>();
        foreach (var (time, payload) in orientations.OrderBy(pair => pair.Time))
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (PhoneScreenEvidence.ReadString(payload, "reason") == "unavailable")
            {
                Unavailable = true;
                continue;
            }

            if (PhoneScreenEvidence.ReadInt32(payload, "orientation") is { } orientation &&
                AndroidCompanionRecords.Orientations.Contains(orientation))
            {
                held.Add(new PhoneHeld(time, orientation, PhoneScreenEvidence.ReadInt32(payload, "degrees") ?? -1));
            }
        }

        var rotations = new List<PhoneRotation>();
        foreach (var (time, payload) in displays.OrderBy(pair => pair.Time))
        {
            if (payload.ValueKind == JsonValueKind.Object &&
                PhoneScreenEvidence.ReadInt32(payload, "rotation") is { } rotation &&
                PhoneScreenEvidence.ReadInt32(payload, "modeWidth") is { } width && width > 0 &&
                PhoneScreenEvidence.ReadInt32(payload, "modeHeight") is { } height && height > 0)
            {
                // The display mode's size is the screen upright; turned a
                // quarter, the screen is as wide as it is tall upright.
                var turned = rotation is 90 or 270;
                rotations.Add(new PhoneRotation(time, rotation, turned ? height : width, turned ? width : height));
            }
        }

        Held = held;
        Rotations = rotations;
    }

    public static PhoneOrientationTimeline Empty { get; } = new([], []);

    /// <summary>How the phone was held, in time order, each from its time to the next.</summary>
    public IReadOnlyList<PhoneHeld> Held { get; }

    /// <summary>The display's rotation, in time order, each from its time to the next.</summary>
    public IReadOnlyList<PhoneRotation> Rotations { get; }

    /// <summary>Whether how the phone was held was recorded: the companion app is protocol 7 or later and could tell.</summary>
    public bool Recorded => Held.Count > 0;

    /// <summary>Whether the companion app said the phone has no sensor to tell how it is held.</summary>
    public bool Unavailable { get; }

    /// <summary>Whether a record is one of how the phone is held, which the playback index keeps whole.</summary>
    public static bool IsOrientationRecord(string channel, string eventType) =>
        channel == AndroidCompanionRecords.OrientationChannel &&
        eventType == AndroidCompanionRecords.OrientationEventType;

    /// <summary>How the phone was held at a time; null before the first reading.</summary>
    public PhoneHeld? HeldAt(long time) => Held.LastOrDefault(held => held.Time <= time);

    /// <summary>The display's rotation at a time; the first before any was recorded, and null when none was.</summary>
    public PhoneRotation? RotationAt(long time) =>
        Rotations.LastOrDefault(rotation => rotation.Time <= time) ?? Rotations.FirstOrDefault();
}

/// <summary>
/// Step 15 of android-checks.md, orientation (WCAG 1.3.4 Orientation,
/// which WCAG2ICT applies to software as written), one check, a warning at
/// both audit levels (owner, 2026-11-13): the phone was turned, and held
/// turned for at least the time chosen in the Checks tab, with Auto-rotate
/// on, and the screen did not turn with it. The phone is held upright at
/// 0 or 180 degrees and sideways at 90 or 270, from the companion app's own
/// sensor; the screen is turned when the display's rotation is 90 or 270.
/// A phone held flat is neither. With Auto-rotate on, Android turns the
/// screen with the phone unless the app in front restricts its orientation
/// (AOSP DisplayRotation.java), so a screen that stays as it was suggests
/// the app restricts it; 1.3.4 allows that when the orientation is
/// essential, which the recording cannot tell, hence a warning. The check
/// is not looked for while Auto-rotate is off or not recorded, or when the
/// recording does not hold how the phone was held (a companion app older
/// than protocol 7). Each visit has at most one finding, with a time for
/// each turn the screen did not follow.
/// </summary>
public static class PhoneOrientationChecks
{
    /// <summary>The Auto-rotate setting, Settings.System.ACCELEROMETER_ROTATION.</summary>
    public const string AutoRotateSetting = AndroidSettingsRecords.SystemNamespace + "/accelerometer_rotation";

    /// <summary>How long the phone must be held turned, by default, in seconds.</summary>
    public const int DefaultHoldSeconds = 2;

    /// <summary>The times the Checks tab offers, in seconds.</summary>
    public static IReadOnlyList<int> HoldChoices { get; } = [1, 2, 3, 4, 5];

    public sealed record Result(
        IReadOnlyList<PhoneFinding> Findings,
        bool Run,
        string? NotRun,
        int Turns,
        int NotFollowed,
        IReadOnlyList<string> Problems);

    private sealed record Span(long Start, long End, bool HeldSideways, PhoneRotation Rotation);

    public static Result Run(
        PhoneScreenTimeline screens,
        PhoneAccessibilityTimeline trees,
        PhoneOrientationTimeline orientation,
        PhoneSettingsTimeline settings,
        ISessionEventRecordSource source,
        int holdSeconds = DefaultHoldSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(trees);
        ArgumentNullException.ThrowIfNull(orientation);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(source);
        var problems = new List<string>();
        string? notRun = !screens.Recorded
            ? "no screens were recorded"
            : orientation.Unavailable && !orientation.Recorded
                ? "the phone has no sensor to tell how it is held"
                : !orientation.Recorded
                    ? "how the phone was held was not recorded: the recording was made with a companion app older than protocol 7"
                    : orientation.Rotations.Count == 0
                        ? "the screen's rotation was not recorded"
                        : !settings.Recorded
                            ? "the phone's settings, and with them Auto-rotate, were not recorded"
                            : null;
        if (notRun is not null)
        {
            return new Result([], false, notRun, 0, 0, problems);
        }

        var hold = (long)Math.Clamp(holdSeconds, HoldChoices[0], HoldChoices[^1]) * 1_000_000_000;
        var spans = Spans(orientation, settings, out var turns);
        var findings = new Dictionary<PhoneVisit, PhoneFinding>();
        var notFollowed = 0;
        foreach (var visit in screens.Visits.OrderBy(visit => visit.Start))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var span in spans)
            {
                var start = Math.Max(span.Start, visit.Start);
                var end = Math.Min(span.End, visit.End);
                if (end - start < hold)
                {
                    continue;
                }

                var snapshot = visit.Snapshots.LastOrDefault(snapshot => snapshot.MonotonicNanoseconds <= start) ??
                    visit.Snapshots.FirstOrDefault();
                if (snapshot is null || trees.TakenBy(snapshot, visit.WindowId) is not { } taking)
                {
                    continue;
                }

                PhoneCheckWindow? window;
                try
                {
                    window = PhoneCheckRun.ReadWindow(source, taking, visit.WindowId);
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or InvalidOperationException or
                        JsonException or KeyNotFoundException or UnauthorizedAccessException)
                {
                    problems.Add($"{WindowsPreferenceTimeline.Clock(start)}: {exception.Message}");
                    continue;
                }

                if (window is null || window.Items.Count == 0)
                {
                    continue;
                }

                notFollowed++;
                var root = window.Items[0];
                if (!findings.TryGetValue(visit, out var finding))
                {
                    findings[visit] = finding = new PhoneFinding(PhoneCheckNames.OrientationRestricted, PhoneCheckLevel.Warning, visit, root.Identity, root.Label);
                }

                finding._times.Add(new PhoneFindingTime(
                    start, visit, snapshot, taking, visit.WindowId, root.Bounds, root.TreeKey, Measured(span, end - start),
                    PhoneCheckRun.StateAt(visit, start)));
            }
        }

        return new Result([.. findings.Values.OrderBy(finding => finding.Times[0].Time)], true, null, turns, notFollowed, problems);
    }

    private static string Measured(Span span, long length)
    {
        var seconds = (length / 1e9).ToString("0.0", CultureInfo.CurrentCulture);
        var held = span.HeldSideways ? "sideways" : "upright";
        var shape = span.Rotation.Width == span.Rotation.Height
            ? span.Rotation.Rotation is 90 or 270 ? "turned" : "upright"
            : span.Rotation.Width < span.Rotation.Height ? "portrait" : "landscape";
        return $"the phone was held {held} for {seconds} seconds with Auto-rotate on, and the screen stayed {shape}, " +
            "as when the app restricts its display to one orientation; look for whether that orientation is essential, " +
            "as for a piano or a bank cheque";
    }

    // The times the phone was held one way, flat aside, with Auto-rotate on,
    // and the screen turned the other: each from when that began to when
    // it ended. Held 90 then 270 is still sideways, so one span.
    private static List<Span> Spans(PhoneOrientationTimeline orientation, PhoneSettingsTimeline settings, out int turns)
    {
        var autoRotateChanges = settings.ChangeTimesOf(PhoneSettingsTimeline.KeyPrefix + AutoRotateSetting) ?? [];
        var times = orientation.Held.Select(held => held.Time)
            .Concat(orientation.Rotations.Select(rotation => rotation.Time))
            .Concat(autoRotateChanges)
            .Where(time => time >= orientation.Held[0].Time)
            .Distinct()
            .Order()
            .ToList();
        var spans = new List<Span>();
        turns = 0;
        bool? lastHeld = null;
        Span? open = null;
        for (var index = 0; index < times.Count; index++)
        {
            var time = times[index];
            var held = orientation.HeldAt(time);
            var rotation = orientation.RotationAt(time);
            bool? sideways = held is null || held.Orientation == AndroidCompanionRecords.FlatOrientation
                ? null
                : held.Orientation is 90 or 270;
            if (sideways is { } now && lastHeld is { } before && now != before)
            {
                turns++;
            }

            lastHeld = sideways ?? lastHeld;
            var mismatched = sideways is { } way && rotation is not null &&
                way != (rotation.Rotation is 90 or 270) &&
                settings.ValueAt(AutoRotateSetting, time) == "1";
            if (open is not null && (!mismatched || open.HeldSideways != sideways))
            {
                spans.Add(open with { End = time });
                open = null;
            }

            if (mismatched && open is null)
            {
                open = new Span(time, long.MaxValue, sideways!.Value, rotation!);
            }
        }

        if (open is not null)
        {
            spans.Add(open);
        }

        return spans;
    }
}

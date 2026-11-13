namespace Recorder.Contracts;

/// <summary>
/// The records of the Android companion collector, slice 2 of
/// docs/architecture/android-implementation-plan.md: the companion app's
/// connection, the phone clock's mapping samples, and the phone display's
/// rotation and size; and, from docs/architecture/android-checks.md, step
/// 15, how the phone itself is held, from its accelerometer.
///
/// The phone's clock domain is <see cref="ClockDomain"/>, CLOCK_MONOTONIC,
/// which the companion app reads as <c>System.nanoTime()</c>. Each clock
/// sample is one request and reply: the recorder's send and receive times
/// on the session clock, and the phone's receive and send times. Its
/// midpoints pair a phone time with a session time, and half the round
/// trip, less the phone's own time between receive and send, bounds its
/// error, as for instrumented Chromium's clock.
/// </summary>
public static class AndroidCompanionRecords
{
    public const string CompanionChannel = "android.companion";
    public const string ClockChannel = "clock.android";
    public const string DisplayChannel = "android.display";

    /// <summary>How the phone itself is held (android-checks.md, step 15).</summary>
    public const string OrientationChannel = "android.orientation";

    public const string ConnectedEventType = "companion-connected";
    public const string UnavailableEventType = "companion-unavailable";
    public const string ClockSampleEventType = "clock-sample";
    public const string DisplayEventType = "display-state";
    public const string OrientationEventType = "phone-orientation";

    /// <summary>What the companion app found and did when asked to raise the Accessibility volume (slice 4, step 4c).</summary>
    public const string AccessibilityVolumeEventType = "accessibility-volume";

    /// <summary>The phone's CLOCK_MONOTONIC, in nanoseconds.</summary>
    public const string ClockDomain = "android-monotonic";

    /// <summary>
    /// The clock mapping of a phone event placed on the session clock by the
    /// line fitted to the clock samples (see PhoneClockMapper).
    /// </summary>
    public const string FittedClockMapping = "android-monotonic-fit";

    /// <summary>
    /// A phone event whose fitted session time fell before the previous
    /// one's on its channel, after the line was fitted again, and was held at
    /// the previous time so the channel's times do not go backward. Its
    /// phone time is kept unchanged.
    /// </summary>
    public const string FitHeldFlag = "clock-fit-held";

    public const string InitialReason = "initial";
    public const string ChangedReason = "changed";

    public static IReadOnlyList<string> DisplayReasons { get; } = [InitialReason, ChangedReason];

    public static IReadOnlyList<int> Rotations { get; } = [0, 90, 180, 270];

    /// <summary>The first reading, a change, or a phone that cannot tell.</summary>
    public static IReadOnlyList<string> OrientationReasons { get; } = ["first", "changed", "unavailable"];

    /// <summary>How the phone is held: a quarter turn, or <see cref="FlatOrientation"/>.</summary>
    public static IReadOnlyList<int> Orientations { get; } = [FlatOrientation, 0, 90, 180, 270];

    /// <summary>The phone is flat, or the companion app cannot tell how it is held.</summary>
    public const int FlatOrientation = -1;

    public static IReadOnlyList<string> AccessibilityVolumeReasons { get; } = ["requested", "services-changed"];

    public static IReadOnlyList<string> AccessibilityVolumeOutcomes { get; } = ["raised", "already", "not-separate", "refused", "failed"];
}

/// <summary>The companion app's greeting when the recorder connects.</summary>
public sealed record AndroidCompanionConnected(
    string Serial,
    int Protocol,
    string VersionName,
    long VersionCode,
    string AndroidRelease,
    int Sdk);

/// <summary>Why the companion app's evidence is missing from part or all of a recording.</summary>
/// <param name="ConnectionLost">True when an open connection ended during the recording.</param>
public sealed record AndroidCompanionUnavailable(string Serial, string Reason, bool ConnectionLost);

/// <summary>One clock request and its reply.</summary>
/// <param name="Segment">
/// Counts from 1, and goes up at each discontinuity, such as the phone
/// sleeping, where CLOCK_MONOTONIC stops while the session clock does not.
/// </param>
/// <param name="Discontinuity">True for the first sample after a discontinuity.</param>
public sealed record AndroidClockSample(
    int Segment,
    bool Discontinuity,
    long RecorderSendNanoseconds,
    long PhoneReceiveNanoseconds,
    long PhoneSendNanoseconds,
    long RecorderReceiveNanoseconds,
    long ErrorBoundNanoseconds);

/// <summary>The phone display's rotation, in degrees, and its display mode's size in pixels.</summary>
public sealed record AndroidDisplayState(string Reason, int Rotation, int ModeWidth, int ModeHeight);

/// <summary>
/// How the phone itself is held, from its accelerometer, as the companion
/// app's PhoneOrientation.java places it: 0 in the phone's natural
/// position, 90 with its left side at the top, 180 upside down, 270 with
/// its right side at the top, or -1 when it is flat or the app cannot tell.
/// The angle is placed in a quarter only within 30 degrees of it, so a
/// phone held between two keeps the last.
/// </summary>
/// <param name="Reason">"first", "changed", or "unavailable" when the phone has no sensor to tell.</param>
/// <param name="Degrees">The sensor's angle that placed it, from 0 to 359, or -1.</param>
public sealed record AndroidPhoneOrientation(string Reason, int Orientation, int Degrees);

/// <summary>
/// What the companion app found and did when asked to raise the phone's
/// Accessibility volume to at least <paramref name="MinimumPercent"/> of its
/// largest step, in volume steps. The volume steps are null when the phone
/// has no audio service.
/// </summary>
/// <param name="Reason">"requested" at the start, or "services-changed" when the volume became separate from the media volume, or stopped being separate, during the recording.</param>
/// <param name="Separate">True when a service that asks for a separate Accessibility volume, such as TalkBack, was on.</param>
/// <param name="Services">Those services, as Android names them.</param>
/// <param name="Outcome">raised, already, not-separate, refused or failed.</param>
public sealed record AndroidAccessibilityVolume(
    string Reason,
    int MinimumPercent,
    bool Separate,
    IReadOnlyList<string> Services,
    int? Max,
    int? Before,
    int? Target,
    int? After,
    string Outcome,
    string? Detail);

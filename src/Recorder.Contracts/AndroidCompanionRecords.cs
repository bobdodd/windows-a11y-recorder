namespace Recorder.Contracts;

/// <summary>
/// The records of the Android companion collector, slice 2 of
/// docs/architecture/android-implementation-plan.md: the companion app's
/// connection, the phone clock's mapping samples, and the phone display's
/// rotation and size.
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

    public const string ConnectedEventType = "companion-connected";
    public const string UnavailableEventType = "companion-unavailable";
    public const string ClockSampleEventType = "clock-sample";
    public const string DisplayEventType = "display-state";

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

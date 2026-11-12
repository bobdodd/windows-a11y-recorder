using System.Globalization;

namespace Recorder.Contracts;

/// <summary>
/// The records of the Android screenshot collector, step 4a of
/// docs/architecture/android-checks.md: screenshots the companion app's
/// service took of the phone's display, without the video's compression,
/// for measuring colours. One is taken when the recording starts and one
/// each time a window has had no change for half a second; each is written
/// as a PNG under <see cref="Folder"/>.
///
/// A screenshot's time is the one Android gives it,
/// <c>ScreenshotResult.getTimestamp()</c>, from
/// <c>SystemClock.uptimeMillis()</c>, the basis of the companion's clock,
/// in whole milliseconds; it is placed on the session clock through the
/// companion collector's clock fit, with a millisecond more uncertainty.
/// A time outside the companion's call is not used: the screenshot is then
/// placed halfway through the call, with half the call as more
/// uncertainty.
/// </summary>
public static class AndroidScreenshotRecords
{
    /// <summary>The screenshots, the ones Android refused, and why they are missing from part or all of a recording.</summary>
    public const string Channel = "graphics.android.screenshots";

    public const string ScreenshotEventType = "android-screenshot";

    /// <summary>A screenshot Android refused, with its code.</summary>
    public const string RefusedEventType = "android-screenshot-refused";

    /// <summary>Why the screenshots are missing from part or all of a recording.</summary>
    public const string UnavailableEventType = "android-screenshots-unavailable";

    /// <summary>The screenshots' folder, relative to the recording's folder.</summary>
    public const string Folder = "screenshots/android";

    /// <summary>The record's time is the screenshot's, as Android gave it.</summary>
    public const string PhoneScreenshotTimeFlag = "phone-screenshot-time";

    /// <summary>
    /// Android's time was outside the companion's call, so the record's
    /// time is halfway through the call.
    /// </summary>
    public const string PhoneCallTimeFlag = "phone-call-time";

    /// <summary>Why a screenshot was taken (Screenshots.java).</summary>
    public static IReadOnlyList<string> Reasons { get; } = ["requested", "settled"];

    /// <summary>
    /// Android's reasons for refusing a screenshot, by code
    /// (AccessibilityService, ERROR_TAKE_SCREENSHOT_*), in words.
    /// </summary>
    public static string RefusalName(int code) => code switch
    {
        1 => "internal-error",
        2 => "no-accessibility-access",
        3 => "interval-time-short",
        4 => "invalid-display",
        5 => "invalid-window",
        6 => "secure-window",
        _ => "unknown"
    };

    /// <summary>What a refusal means, as a sentence for the operator.</summary>
    public static string RefusalText(int code) => code switch
    {
        1 => "Android had an internal error.",
        2 => "Android says the companion's service may not take screenshots; install the companion app again and turn its service on.",
        3 => "it came too soon after the one before; the companion takes it again.",
        4 => "Android did not find the display.",
        5 => "Android did not find the window.",
        6 => "a window on the screen is marked secure, so Android does not allow it to be taken.",
        _ => string.Create(CultureInfo.InvariantCulture, $"Android gave code {code}, which this recorder does not know.")
    };

    /// <summary>A screenshot's file name: its index from the companion, ten digits.</summary>
    public static string ScreenshotPath(long index) =>
        Folder + "/" + index.ToString("D10", CultureInfo.InvariantCulture) + ".png";
}

/// <summary>A screenshot of the phone's display, written as a PNG file.</summary>
/// <param name="Index">The companion's count of screenshots and refusals on the connection, from 1.</param>
/// <param name="Path">The PNG file, relative to the recording's folder.</param>
/// <param name="Reason">"requested", the first, or "settled", a window had no change for half a second.</param>
/// <param name="Width">The PNG's width, the display's in pixels.</param>
/// <param name="Height">The PNG's height, the display's in pixels.</param>
/// <param name="ByteLength">The PNG file's length.</param>
/// <param name="Sha256">The PNG file's SHA-256, in lowercase hexadecimal.</param>
/// <param name="ColourSpace">The colour space Android gave the screenshot, by its name.</param>
/// <param name="Converted">True when the companion converted it to sRGB; the PNG is sRGB either way.</param>
/// <param name="PhoneTimestampMilliseconds">Android's time for the screenshot, <c>SystemClock.uptimeMillis()</c>.</param>
/// <param name="RequestedPhoneNanoseconds">The companion's <c>System.nanoTime()</c> when it asked.</param>
/// <param name="CapturedPhoneNanoseconds">The companion's <c>System.nanoTime()</c> when Android answered.</param>
/// <param name="EncodedPhoneNanoseconds">The companion's <c>System.nanoTime()</c> when the PNG was written.</param>
/// <param name="LastChangePhoneMilliseconds">The time of the last change the companion saw before it asked, <c>SystemClock.uptimeMillis()</c>; null before any.</param>
/// <param name="ReceivedAtNanoseconds">The session time the recorder read the screenshot.</param>
public sealed record AndroidScreenshot(
    string Serial,
    long Index,
    string Path,
    string Reason,
    int Width,
    int Height,
    long ByteLength,
    string Sha256,
    string ColourSpace,
    bool Converted,
    long PhoneTimestampMilliseconds,
    long RequestedPhoneNanoseconds,
    long CapturedPhoneNanoseconds,
    long EncodedPhoneNanoseconds,
    long? LastChangePhoneMilliseconds,
    long ReceivedAtNanoseconds);

/// <summary>A screenshot Android refused.</summary>
/// <param name="Index">The companion's count of screenshots and refusals on the connection, from 1.</param>
/// <param name="Code">Android's code (AccessibilityService, ERROR_TAKE_SCREENSHOT_*).</param>
/// <param name="CodeName">The code's name, as <see cref="AndroidScreenshotRecords.RefusalName"/> gives it.</param>
/// <param name="Explanation">What it means, as <see cref="AndroidScreenshotRecords.RefusalText"/> gives it.</param>
/// <param name="RequestedPhoneNanoseconds">The companion's <c>System.nanoTime()</c> when it asked.</param>
/// <param name="FailedPhoneNanoseconds">The companion's <c>System.nanoTime()</c> when Android refused.</param>
public sealed record AndroidScreenshotRefused(
    string Serial,
    long Index,
    string Reason,
    int Code,
    string CodeName,
    string Explanation,
    long RequestedPhoneNanoseconds,
    long FailedPhoneNanoseconds);

/// <summary>Why the screenshots are missing from part or all of a recording.</summary>
public sealed record AndroidScreenshotsUnavailable(string Serial, string Reason, bool ConnectionLost);

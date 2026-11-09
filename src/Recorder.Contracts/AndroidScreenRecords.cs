namespace Recorder.Contracts;

/// <summary>
/// The records of the Android screen collector, slice 3 of
/// docs/architecture/android-implementation-plan.md: the phone's screen as
/// scrcpy's server encodes it on the phone.
///
/// The stream is stored as it was received, in <see cref="StreamPath"/>,
/// which is the complete record of the picture. Each packet of it has a
/// record on <see cref="VideoChannel"/> giving where its bytes are in that
/// file, its flags, and the phone time the server gave it. A capture session
/// starts when the server starts and again each time the picture changes
/// size, as when the phone turns; each has its own record.
/// </summary>
public static class AndroidScreenRecords
{
    public const string VideoChannel = "graphics.android.video";

    public const string StreamEventType = "video-stream";
    public const string SessionEventType = "video-session";
    public const string PacketEventType = "video-packet";
    public const string UnavailableEventType = "video-unavailable";

    /// <summary>The stream as received, relative to the recording's folder.</summary>
    public const string StreamPath = "android/screen.h264";

    /// <summary>What scrcpy's server printed, relative to the recording's folder.</summary>
    public const string ServerLogPath = "android/scrcpy-server.log";

    public const string H264Codec = "h264";

    /// <summary>
    /// A packet placed at the time the recorder read it, because no phone
    /// clock sample was taken to place its phone time by, or because it is a
    /// configuration packet, which has no phone time.
    /// </summary>
    public const string ReceiptTimeFlag = "receipt-time-stamp";
}

/// <summary>The screen stream's start: what was asked of scrcpy's server, and where the stream is kept.</summary>
/// <param name="Codec">The codec the server reported, as scrcpy names it.</param>
/// <param name="VideoBitRate">The bit rate the phone's encoder was asked for, in bits per second.</param>
public sealed record AndroidVideoStream(
    string Serial,
    string ServerVersion,
    string Codec,
    int VideoBitRate,
    string Path);

/// <summary>A capture session: the picture's size from here until the next session.</summary>
/// <param name="Session">Counts from 1.</param>
/// <param name="ClientResized">scrcpy's flag for a size the client asked for; the recorder never asks, so it is false.</param>
/// <param name="ByteOffset">Where in the stream file the session's first packet will start.</param>
public sealed record AndroidVideoSession(
    int Session,
    int Width,
    int Height,
    bool ClientResized,
    long ByteOffset);

/// <summary>One packet of the stream, stored in the stream file at its byte offset.</summary>
/// <param name="Session">The capture session the packet belongs to.</param>
/// <param name="PresentationMicroseconds">
/// The phone time the server gave the packet, in microseconds of the phone's
/// CLOCK_MONOTONIC; null for a configuration packet.
/// </param>
/// <param name="Config">A configuration packet (the codec's parameter sets), which holds no picture.</param>
/// <param name="ReceivedAtNanoseconds">The session time the recorder had read the whole packet.</param>
public sealed record AndroidVideoPacket(
    int Session,
    long ByteOffset,
    int Size,
    long? PresentationMicroseconds,
    bool Config,
    bool KeyFrame,
    long ReceivedAtNanoseconds);

/// <summary>Why the phone's screen is missing from part or all of a recording.</summary>
/// <param name="ConnectionLost">True when a stream that had started ended during the recording.</param>
public sealed record AndroidVideoUnavailable(string Serial, string Reason, bool ConnectionLost);

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

/// <summary>
/// The records of the phone's frames, step 3b of slice 3 in
/// docs/architecture/android-implementation-plan.md: pictures decoded from
/// the stored stream and written as PNG files, one for each picture whose
/// pixels differ from the picture before it.
/// </summary>
public static class AndroidFrameRecords
{
    public const string FramesChannel = "graphics.android.frames";

    public const string FrameEventType = "device-frame";
    public const string UnavailableEventType = "frames-unavailable";

    /// <summary>The frames' folder, relative to the recording's folder.</summary>
    public const string FramesFolder = "frames/android";

    /// <summary>The picture was compressed by the phone's encoder before the recorder had it.</summary>
    public const string CompressedOnPhoneFlag = "compressed-on-phone";

    /// <summary>
    /// The colour matrix or range was not in the stream, so the recorder
    /// used BT.709 limited range, which the Pixel 10 Pro XL's stream gave.
    /// </summary>
    public const string ColourAssumedFlag = "colour-assumed";

    /// <summary>A frame's file name: its sequence on the frames channel, ten digits.</summary>
    public static string FramePath(ulong sequence) =>
        FramesFolder + "/" + sequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + ".png";
}

/// <summary>A picture of the phone's screen, written as a PNG file.</summary>
/// <param name="Path">The PNG file, relative to the recording's folder.</param>
/// <param name="Width">The picture's width, the capture session's.</param>
/// <param name="Height">The picture's height, the capture session's.</param>
/// <param name="Session">The capture session the picture belongs to.</param>
/// <param name="PacketByteOffset">Where the picture's packet starts in the stream file.</param>
/// <param name="PresentationMicroseconds">The packet's phone time, as the server gave it.</param>
/// <param name="ByteLength">The PNG file's length.</param>
/// <param name="PixelFormat">The PNG's pixels: R8G8B8, 8 bits for each of red, green and blue.</param>
/// <param name="EncodedFormat">png.</param>
/// <param name="ColourMatrix">The YUV matrix the picture was converted with: bt709, bt601 or bt2020.</param>
/// <param name="ColourRange">limited (16 to 235) or full (0 to 255).</param>
/// <param name="ChromaUpsampling">How the half-size colour planes were brought to full size: nearest.</param>
/// <param name="Decoder">The decoder that made the picture: media-foundation-h264 for Windows' Media Foundation H.264 decoder.</param>
/// <param name="DecodedWidth">The decoder's picture width, before the picture was cut to the session's size.</param>
/// <param name="DecodedHeight">The decoder's picture height, before the picture was cut to the session's size.</param>
/// <param name="RepeatsBefore">Decoded pictures since the previous frame that were the same as it, so not written.</param>
/// <param name="WrittenAtNanoseconds">The session time the PNG file was complete; its delay from the packet's arrival shows how far decoding was behind.</param>
public sealed record AndroidDeviceFrame(
    string Path,
    int Width,
    int Height,
    int Session,
    long PacketByteOffset,
    long PresentationMicroseconds,
    long ByteLength,
    string PixelFormat,
    string EncodedFormat,
    string ColourMatrix,
    string ColourRange,
    string ChromaUpsampling,
    string Decoder,
    int DecodedWidth,
    int DecodedHeight,
    long RepeatsBefore,
    long WrittenAtNanoseconds);

/// <summary>Why the phone's frames stopped being made; the stream is still recorded.</summary>
public sealed record AndroidFramesUnavailable(string Reason);

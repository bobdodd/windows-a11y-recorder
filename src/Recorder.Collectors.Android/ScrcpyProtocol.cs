using System.Buffers.Binary;
using System.Globalization;

namespace Recorder.Collectors.Android;

/// <summary>A 12-byte header on scrcpy's video or audio socket. The audio socket has no session headers.</summary>
public abstract record ScrcpyHeader;

/// <summary>A capture session starts: the picture's size from here on.</summary>
public sealed record ScrcpySessionHeader(int Width, int Height, bool ClientResized) : ScrcpyHeader;

/// <summary>The header of a packet of <see cref="Size"/> bytes that follows it.</summary>
/// <param name="PresentationMicroseconds">Null for a configuration packet, which has no time.</param>
public sealed record ScrcpyPacketHeader(long? PresentationMicroseconds, bool Config, bool KeyFrame, int Size) : ScrcpyHeader;

/// <summary>
/// The video socket's protocol in scrcpy 5.0.1, written against that
/// release's server source (Streamer.java, DesktopConnection.java,
/// Options.java at commit a60891aea193d92e7e5c3942700eca63f9d19a5f), as
/// scrcpy's develop.md warns that the protocol changes between releases.
///
/// With the device name not sent and the tunnel reversed, so no dummy byte,
/// the socket starts with the codec id, a big-endian u32. Then come 12-byte
/// headers, big-endian. A header whose top bit is set is a session header:
/// a u32 of flags (bit 0, the client asked for the size), the width and the
/// height. Otherwise it is a packet header: a u64 whose bit 62 marks a
/// configuration packet and bit 61 a key frame, with the presentation time
/// in microseconds in the bits below for a packet that is not configuration,
/// then the packet's size as a u32, then the packet.
/// </summary>
public static class ScrcpyProtocol
{
    public const int HeaderSize = 12;

    /// <summary>"h264".</summary>
    public const uint H264CodecId = 0x68323634;

    /// <summary>"raw": the phone's sound as PCM, not encoded (AudioCodec.java).</summary>
    public const uint RawCodecId = 0x00726177;

    /// <summary>The sound the server's raw codec gives (AudioConfig.java): 48 kHz.</summary>
    public const int SoundSampleRate = 48_000;

    /// <summary>Two channels, interleaved.</summary>
    public const int SoundChannels = 2;

    /// <summary>16-bit little-endian samples.</summary>
    public const int SoundBitsPerSample = 16;

    /// <summary>Bytes per sample frame, both channels.</summary>
    public const int SoundBlockAlign = SoundChannels * SoundBitsPerSample / 8;

    /// <summary>The codec id the server sends when it disables the stream, as when it cannot capture.</summary>
    public const uint DisabledCodecId = 0;

    /// <summary>The codec id the server sends after a configuration error.</summary>
    public const uint ErrorCodecId = 1;

    private const ulong SessionFlag = 1UL << 63;
    private const ulong ConfigFlag = 1UL << 62;
    private const ulong KeyFrameFlag = 1UL << 61;
    private const ulong PresentationMask = KeyFrameFlag - 1;

    /// <summary>A packet larger than this is taken as a broken stream, not read.</summary>
    public const int MaxPacketSize = 64 * 1024 * 1024;

    /// <summary>Where scrcpy's server is pushed on the phone. The server deletes the file once started.</summary>
    public const string DeviceServerPath = "/data/local/tmp/a11yrecorder-scrcpy-server.jar";

    /// <summary>scrcpy's default, kept: 8 Mbit/s.</summary>
    public const int VideoBitRate = 8_000_000;

    public static string SocketName(int scid) =>
        "scrcpy_" + scid.ToString("x8", CultureInfo.InvariantCulture);

    /// <summary>
    /// The arguments to <c>adb -s serial shell</c> that start the server:
    /// H.264 video and the phone's sound, no control, so nothing on the phone
    /// is controlled; the device name is not sent, and no clean-up process is
    /// started, as there is nothing to restore. The sound (slice 4) is the
    /// whole audio output, <c>audio_source=output</c>, which check 1 found
    /// holds TalkBack's speech and which stops the phone playing it itself;
    /// it is sent raw, as PCM, so nothing is lost to an encoder.
    /// </summary>
    public static IReadOnlyList<string> ServerCommand(int scid, string version) =>
    [
        "CLASSPATH=" + DeviceServerPath,
        "app_process",
        "/",
        "com.genymobile.scrcpy.Server",
        version,
        "scid=" + scid.ToString("x8", CultureInfo.InvariantCulture),
        "log_level=info",
        "video=true",
        "audio=true",
        "control=false",
        "video_codec=h264",
        "audio_codec=raw",
        "audio_source=output",
        "video_bit_rate=" + VideoBitRate.ToString(CultureInfo.InvariantCulture),
        "send_device_meta=false",
        "cleanup=false"
    ];

    public static string CodecName(uint id) => id switch
    {
        H264CodecId => "h264",
        RawCodecId => "raw",
        0x68323635 => "h265",
        0x00617631 => "av1",
        DisabledCodecId => "disabled",
        ErrorCodecId => "error",
        _ => "0x" + id.ToString("x8", CultureInfo.InvariantCulture)
    };

    public static uint ParseCodecId(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32BigEndian(bytes);

    /// <exception cref="FormatException">The header cannot be from the 5.0.1 server.</exception>
    public static ScrcpyHeader ParseHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderSize)
        {
            throw new FormatException($"A header is {HeaderSize} bytes, not {header.Length}.");
        }

        var first = BinaryPrimitives.ReadUInt64BigEndian(header);
        if ((first & SessionFlag) != 0)
        {
            var flags = BinaryPrimitives.ReadUInt32BigEndian(header);
            var width = BinaryPrimitives.ReadInt32BigEndian(header[4..]);
            var height = BinaryPrimitives.ReadInt32BigEndian(header[8..]);
            if (width <= 0 || height <= 0)
            {
                throw new FormatException($"A capture session cannot be {width} by {height}.");
            }

            return new ScrcpySessionHeader(width, height, (flags & 1) != 0);
        }

        var size = BinaryPrimitives.ReadInt32BigEndian(header[8..]);
        if (size <= 0 || size > MaxPacketSize)
        {
            throw new FormatException($"A packet of {size} bytes is outside what the server sends.");
        }

        var config = (first & ConfigFlag) != 0;
        var keyFrame = (first & KeyFrameFlag) != 0;
        return new ScrcpyPacketHeader(config ? null : (long)(first & PresentationMask), config, keyFrame, size);
    }

    /// <summary>A session header, as the 5.0.1 server writes it; for tests.</summary>
    public static byte[] SessionHeader(int width, int height, bool clientResized = false)
    {
        var bytes = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 0x8000_0000u | (clientResized ? 1u : 0u));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), height);
        return bytes;
    }

    /// <summary>A packet header, as the 5.0.1 server writes it; for tests.</summary>
    public static byte[] PacketHeader(long? presentationMicroseconds, bool keyFrame, int size)
    {
        var bytes = new byte[HeaderSize];
        var value = presentationMicroseconds is { } pts
            ? (ulong)pts | (keyFrame ? KeyFrameFlag : 0)
            : ConfigFlag;
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), size);
        return bytes;
    }
}

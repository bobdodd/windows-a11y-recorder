using System.Runtime.InteropServices;

namespace Recorder.Export;

/// <summary>
/// Media Foundation could not be started: Windows without its media
/// features, such as an N edition without the Media Feature Pack.
/// </summary>
public sealed class MediaFoundationUnavailableException(string message, Exception? inner)
    : Exception(message, inner);

/// <summary>
/// Writes an MP4 file with Windows' Media Foundation sink writer: one H.264
/// video stream from 32-bit RGB pictures and AAC audio streams from 48 kHz
/// stereo 16-bit PCM. Media Foundation's interfaces are called through
/// their function tables, at the slots their IDL declares; see
/// docs/architecture/mp4-export.md, "Encoding". Not safe for use from more
/// than one thread at a time.
/// </summary>
public sealed unsafe class Mp4Writer : IDisposable
{
    // MF_SDK_VERSION 2, MF_API_VERSION 0x70: Windows 7 and later.
    private const uint MfVersion = 0x0002_0070;
    private const uint MfStartupFull = 0;
    private const int AacBytesPerSecond = 24_000; // 192 kbit/s
    private const uint ProgressiveInterlace = 2;
    private const uint H264ProfileHigh = 100;
    private const uint AacLevel = 0x29;
    private const uint UnconstrainedVbr = 2;

    private static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid AverageBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid Mpeg2Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    private static readonly Guid AllSamplesIndependent = new("c9173739-5e56-461c-b713-46fb995cb95f");
    private static readonly Guid AudioChannels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid AudioSampleRate = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid AudioBitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    private static readonly Guid AudioBytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    private static readonly Guid AudioBlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    private static readonly Guid AacPayloadType = new("bfbabe79-7434-4d1c-94f0-72a3b9e17188");
    private static readonly Guid AacProfileLevel = new("7632f0e6-9538-4d61-acda-ea29c8c14456");
    private static readonly Guid EnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
    private static readonly Guid TranscodeContainerType = new("150ff23f-4abc-478b-ac4f-e1916fba1cca");
    private static readonly Guid ContainerMpeg4 = new("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");
    private static readonly Guid RateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");
    private static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MediaTypeAudio = new("73647561-0000-0010-8000-00aa00389b71");
    private static readonly Guid VideoH264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid VideoRgb32 = new("00000016-0000-0010-8000-00aa00389b71");
    private static readonly Guid AudioAac = new("00001610-0000-0010-8000-00aa00389b71");
    private static readonly Guid AudioPcm = new("00000001-0000-0010-8000-00aa00389b71");

    private readonly VideoFormat _format;
    private readonly int _frameBytes;
    private readonly List<uint> _audioStreams = [];
    private IntPtr _writer;
    private uint _videoStream;
    private bool _mediaFoundation;

    /// <param name="path">The file written; its container is MP4 whatever its name.</param>
    /// <param name="audioTracks">The number of AAC streams after the video.</param>
    /// <param name="hardware">Whether Windows may use the graphics card's encoder.</param>
    /// <param name="encoderSettings">Whether the video encoder is asked for variable bit rate.</param>
    public Mp4Writer(string path, VideoFormat format, int audioTracks, bool hardware, bool encoderSettings)
    {
        ArgumentNullException.ThrowIfNull(path);
        _format = format;
        _frameBytes = checked(format.Width * format.Height * 4);
        Start();
        _mediaFoundation = true;
        try
        {
            var attributes = CreateAttributes();
            try
            {
                SetUInt32(attributes, EnableHardwareTransforms, hardware ? 1u : 0u);
                SetGuid(attributes, TranscodeContainerType, ContainerMpeg4);
                IntPtr writer;
                Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, &writer), "create the MP4 file");
                _writer = writer;
            }
            finally
            {
                Release(attributes);
            }

            AddVideo(encoderSettings);
            for (var track = 0; track < audioTracks; track++)
            {
                AddAudio();
            }

            Check(Call(_writer, 5), "start writing");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// The H.264 bit rate the encoder aims for on average: 0.2 bits per
    /// pixel per recorded frame, from 1 to 20 megabits a second. The
    /// samples that repeat a frame cost next to nothing, so the rate
    /// follows the recorded frames, not the export's frame rate.
    /// </summary>
    public static uint BitrateOf(VideoFormat format) =>
        (uint)Math.Clamp(
            0.2 * format.Width * format.Height * format.RecordedFramesPerSecond,
            1_000_000,
            20_000_000);

    /// <summary>Writes a picture of 32-bit BGRX pixels, top row first.</summary>
    public void WriteVideo(ReadOnlySpan<byte> pixels, long startNanoseconds, long durationNanoseconds)
    {
        if (pixels.Length != _frameBytes)
        {
            throw new ArgumentException("The picture is not the export's size.", nameof(pixels));
        }

        Write(_videoStream, pixels, startNanoseconds, durationNanoseconds);
    }

    /// <summary>Writes 48 kHz stereo 16-bit PCM to an audio track.</summary>
    public void WriteAudio(int track, ReadOnlySpan<byte> pcm, long startNanoseconds, long durationNanoseconds) =>
        Write(_audioStreams[track], pcm, startNanoseconds, durationNanoseconds);

    /// <summary>Completes the file. Without it the file is not playable.</summary>
    public void Finish()
    {
        Check(Call(_writer, 11), "finish the MP4 file");
    }

    public void Dispose()
    {
        if (_writer != IntPtr.Zero)
        {
            Release(_writer);
            _writer = IntPtr.Zero;
        }

        if (_mediaFoundation)
        {
            _ = MFShutdown();
            _mediaFoundation = false;
        }
    }

    private static void Start()
    {
        int result;
        try
        {
            result = MFStartup(MfVersion, MfStartupFull);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new MediaFoundationUnavailableException(
                "Windows Media Foundation is not installed. On an N edition of Windows, install the Media Feature Pack.",
                exception);
        }

        if (result < 0)
        {
            throw new MediaFoundationUnavailableException(
                $"Windows Media Foundation could not be started (0x{result:X8}). On an N edition of Windows, install the Media Feature Pack.",
                Marshal.GetExceptionForHR(result));
        }
    }

    private void AddVideo(bool encoderSettings)
    {
        var size = ((ulong)(uint)_format.Width << 32) | (uint)_format.Height;
        var rate = ((ulong)(uint)_format.FramesPerSecond << 32) | 1u;
        var output = CreateMediaType();
        try
        {
            SetGuid(output, MajorType, MediaTypeVideo);
            SetGuid(output, Subtype, VideoH264);
            SetUInt32(output, AverageBitrate, BitrateOf(_format));
            SetUInt32(output, InterlaceMode, ProgressiveInterlace);
            SetUInt64(output, FrameSize, size);
            SetUInt64(output, FrameRate, rate);
            SetUInt64(output, PixelAspectRatio, (1UL << 32) | 1);
            SetUInt32(output, Mpeg2Profile, H264ProfileHigh);
            uint index;
            Check(((delegate* unmanaged<IntPtr, IntPtr, uint*, int>)Slot(_writer, 3))(_writer, output, &index), "add the video stream");
            _videoStream = index;
        }
        finally
        {
            Release(output);
        }

        var input = CreateMediaType();
        var parameters = IntPtr.Zero;
        try
        {
            SetGuid(input, MajorType, MediaTypeVideo);
            SetGuid(input, Subtype, VideoRgb32);
            SetUInt32(input, InterlaceMode, ProgressiveInterlace);
            SetUInt64(input, FrameSize, size);
            SetUInt64(input, FrameRate, rate);
            SetUInt64(input, PixelAspectRatio, (1UL << 32) | 1);
            // A positive stride: the top row first.
            SetUInt32(input, DefaultStride, (uint)(_format.Width * 4));
            SetUInt32(input, AllSamplesIndependent, 1);
            if (encoderSettings)
            {
                parameters = CreateAttributes();
                SetUInt32(parameters, RateControlMode, UnconstrainedVbr);
            }

            Check(
                ((delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, int>)Slot(_writer, 4))(_writer, _videoStream, input, parameters),
                "set up the video encoder");
        }
        finally
        {
            Release(input);
            if (parameters != IntPtr.Zero)
            {
                Release(parameters);
            }
        }
    }

    private void AddAudio()
    {
        var output = CreateMediaType();
        uint index;
        try
        {
            SetGuid(output, MajorType, MediaTypeAudio);
            SetGuid(output, Subtype, AudioAac);
            SetUInt32(output, AudioBitsPerSample, 16);
            SetUInt32(output, AudioSampleRate, ExportAudioMixer.SampleRate);
            SetUInt32(output, AudioChannels, ExportAudioMixer.Channels);
            SetUInt32(output, AudioBytesPerSecond, AacBytesPerSecond);
            SetUInt32(output, AacPayloadType, 0);
            SetUInt32(output, AacProfileLevel, AacLevel);
            Check(((delegate* unmanaged<IntPtr, IntPtr, uint*, int>)Slot(_writer, 3))(_writer, output, &index), "add an audio stream");
        }
        finally
        {
            Release(output);
        }

        var input = CreateMediaType();
        try
        {
            SetGuid(input, MajorType, MediaTypeAudio);
            SetGuid(input, Subtype, AudioPcm);
            SetUInt32(input, AudioBitsPerSample, 16);
            SetUInt32(input, AudioSampleRate, ExportAudioMixer.SampleRate);
            SetUInt32(input, AudioChannels, ExportAudioMixer.Channels);
            SetUInt32(input, AudioBlockAlignment, ExportAudioMixer.BlockAlign);
            SetUInt32(input, AudioBytesPerSecond, ExportAudioMixer.SampleRate * ExportAudioMixer.BlockAlign);
            SetUInt32(input, AllSamplesIndependent, 1);
            Check(
                ((delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, int>)Slot(_writer, 4))(_writer, index, input, IntPtr.Zero),
                "set up the audio encoder");
        }
        finally
        {
            Release(input);
        }

        _audioStreams.Add(index);
    }

    private void Write(uint stream, ReadOnlySpan<byte> data, long startNanoseconds, long durationNanoseconds)
    {
        IntPtr buffer;
        Check(MFCreateMemoryBuffer((uint)data.Length, &buffer), "allocate a sample");
        IntPtr sample = IntPtr.Zero;
        try
        {
            byte* bytes;
            uint maximum;
            uint current;
            Check(((delegate* unmanaged<IntPtr, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &bytes, &maximum, &current), "lock a sample");
            data.CopyTo(new Span<byte>(bytes, data.Length));
            Check(((delegate* unmanaged<IntPtr, int>)Slot(buffer, 4))(buffer), "unlock a sample");
            Check(((delegate* unmanaged<IntPtr, uint, int>)Slot(buffer, 6))(buffer, (uint)data.Length), "size a sample");

            IntPtr created;
            Check(MFCreateSample(&created), "create a sample");
            sample = created;
            Check(((delegate* unmanaged<IntPtr, IntPtr, int>)Slot(sample, 42))(sample, buffer), "fill a sample");
            // Media Foundation's times are in 100 ns units.
            Check(((delegate* unmanaged<IntPtr, long, int>)Slot(sample, 36))(sample, startNanoseconds / 100), "time a sample");
            Check(((delegate* unmanaged<IntPtr, long, int>)Slot(sample, 38))(sample, Math.Max(1, durationNanoseconds / 100)), "time a sample");
            Check(((delegate* unmanaged<IntPtr, uint, IntPtr, int>)Slot(_writer, 6))(_writer, stream, sample), "write a sample");
        }
        finally
        {
            if (sample != IntPtr.Zero)
            {
                Release(sample);
            }

            Release(buffer);
        }
    }

    private static IntPtr CreateMediaType()
    {
        IntPtr type;
        Check(MFCreateMediaType(&type), "create a media type");
        return type;
    }

    private static IntPtr CreateAttributes()
    {
        IntPtr attributes;
        Check(MFCreateAttributes(&attributes, 4), "create attributes");
        return attributes;
    }

    // IMFAttributes::SetUINT32, SetUINT64, and SetGUID are slots 21, 22,
    // and 24, after IUnknown's 3.
    private static void SetUInt32(IntPtr attributes, Guid key, uint value) =>
        Check(((delegate* unmanaged<IntPtr, Guid*, uint, int>)Slot(attributes, 21))(attributes, &key, value), "set an attribute");

    private static void SetUInt64(IntPtr attributes, Guid key, ulong value) =>
        Check(((delegate* unmanaged<IntPtr, Guid*, ulong, int>)Slot(attributes, 22))(attributes, &key, value), "set an attribute");

    private static void SetGuid(IntPtr attributes, Guid key, Guid value) =>
        Check(((delegate* unmanaged<IntPtr, Guid*, Guid*, int>)Slot(attributes, 24))(attributes, &key, &value), "set an attribute");

    private static int Call(IntPtr instance, int slot) =>
        ((delegate* unmanaged<IntPtr, int>)Slot(instance, slot))(instance);

    private static void Release(IntPtr instance) =>
        _ = ((delegate* unmanaged<IntPtr, uint>)Slot(instance, 2))(instance);

    private static void* Slot(IntPtr instance, int slot) => (*(void***)instance)[slot];

    private static void Check(int result, string action)
    {
        if (result < 0)
        {
            throw new COMException($"Windows Media Foundation could not {action} (0x{result:X8}).", result);
        }
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(IntPtr* mediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateAttributes(IntPtr* attributes, uint initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateSample(IntPtr* sample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMemoryBuffer(uint maxLength, IntPtr* buffer);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, IntPtr* sinkWriter);
}

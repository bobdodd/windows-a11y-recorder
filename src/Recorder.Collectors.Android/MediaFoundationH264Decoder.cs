using System.Runtime.InteropServices;

namespace Recorder.Collectors.Android;

/// <summary>Decodes one capture session's H.264 pictures, in order.</summary>
public interface IH264Decoder : IDisposable
{
    /// <summary>The decoder's name, for the record of which made the frames.</summary>
    string Name { get; }

    /// <summary>
    /// Decodes an access unit in Annex B form, tagged so its picture can be
    /// matched to its packet, and adds the pictures now ready, which may be
    /// of earlier access units.
    /// </summary>
    void Decode(ReadOnlySpan<byte> accessUnit, long tag, List<DecodedPicture> ready);

    /// <summary>Adds the pictures the decoder still holds; no more access units follow.</summary>
    void Drain(List<DecodedPicture> ready);
}

/// <summary>A decoded picture and the tag of the access unit it came from.</summary>
public sealed record DecodedPicture(long Tag, Nv12Picture Picture);

/// <summary>Windows' Media Foundation is missing or its H.264 decoder could not be used.</summary>
public sealed class DecoderUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Windows' Media Foundation H.264 decoder
/// (https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-decoder),
/// in software, with NV12 out and low latency asked for, so each picture
/// comes out as soon as it is decoded. Its interfaces are called through
/// their function tables at the slots their IDL declares, as
/// Recorder.Export's MP4 writer does. COM must be usable from the calling
/// thread; the constructor joins the thread to the multithreaded apartment.
/// One thread at a time.
/// </summary>
public sealed unsafe class MediaFoundationH264Decoder : IH264Decoder
{
    private const uint MfVersion = 0x0002_0070;
    private const int NeedMoreInput = unchecked((int)0xC00D6D72);
    private const int StreamChange = unchecked((int)0xC00D6D61);
    private const int NotAccepting = unchecked((int)0xC00D36B5);
    private const uint ProvidesSamples = 0x100;
    private const uint CanProvideSamples = 0x200;
    private const uint BeginStreaming = 0x1000_0000;
    private const uint StartOfStream = 0x1000_0003;
    private const uint EndOfStream = 0x1000_0002;
    private const uint CommandDrain = 1;

    private static readonly Guid DecoderClass = new("62CE7E72-4C71-4d20-B15D-452831A87D9D");
    private static readonly Guid TransformInterface = new("bf94c121-5b05-4e6f-8000-ba598961414d");
    private static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid YuvMatrix = new("3e23d450-2c75-4d25-a00e-b91670d12327");
    private static readonly Guid NominalRange = new("c21b8ee5-b956-4071-8daf-325edf5cab11");
    private static readonly Guid LowLatency = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
    private static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid VideoH264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid VideoNv12 = new("3231564e-0000-0010-8000-00aa00389b71");

    private IntPtr _transform;
    private bool _mediaFoundation;
    private int _width;
    private int _height;
    private int _stride;
    private ColourMatrix? _matrix;
    private bool? _fullRange;

    /// <param name="width">The capture session's width, for the decoder's first output type.</param>
    /// <param name="height">The capture session's height.</param>
    public MediaFoundationH264Decoder(int width, int height)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DecoderUnavailableException("The phone's frames are made with Windows Media Foundation, which only Windows has.");
        }

        int started;
        try
        {
            // S_FALSE when the thread already is; a thread in a single-threaded
            // apartment can still create the decoder, which is free-threaded.
            _ = CoInitializeEx(IntPtr.Zero, 0);
            started = MFStartup(MfVersion, 0);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new DecoderUnavailableException(
                "Windows Media Foundation is not installed. On an N edition of Windows, install the Media Feature Pack.",
                exception);
        }

        if (started < 0)
        {
            throw new DecoderUnavailableException(
                $"Windows Media Foundation could not be started (0x{started:X8}). On an N edition of Windows, install the Media Feature Pack.");
        }

        _mediaFoundation = true;
        try
        {
            IntPtr transform;
            var decoderClass = DecoderClass;
            var transformInterface = TransformInterface;
            var created = CoCreateInstance(&decoderClass, IntPtr.Zero, 1, &transformInterface, &transform);
            if (created < 0)
            {
                throw new DecoderUnavailableException($"Windows' H.264 decoder could not be created (0x{created:X8}).");
            }

            _transform = transform;
            IntPtr attributes;
            if (((delegate* unmanaged<IntPtr, IntPtr*, int>)Slot(_transform, 8))(_transform, &attributes) >= 0 && attributes != IntPtr.Zero)
            {
                SetUInt32(attributes, LowLatency, 1);
                Release(attributes);
            }

            var input = CreateMediaType();
            try
            {
                SetGuid(input, MajorType, MediaTypeVideo);
                SetGuid(input, Subtype, VideoH264);
                SetUInt64(input, FrameSize, ((ulong)(uint)width << 32) | (uint)height);
                Check(((delegate* unmanaged<IntPtr, uint, IntPtr, uint, int>)Slot(_transform, 15))(_transform, 0, input, 0), "accept H.264");
            }
            finally
            {
                Release(input);
            }

            ChooseOutput();
            Message(BeginStreaming);
            Message(StartOfStream);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string Name => "media-foundation-h264";

    public void Decode(ReadOnlySpan<byte> accessUnit, long tag, List<DecodedPicture> ready)
    {
        ArgumentNullException.ThrowIfNull(ready);
        var sample = CreateSample(accessUnit, tag);
        try
        {
            while (true)
            {
                var result = ((delegate* unmanaged<IntPtr, uint, IntPtr, uint, int>)Slot(_transform, 24))(_transform, 0, sample, 0);
                if (result != NotAccepting)
                {
                    Check(result, "decode a packet");
                    break;
                }

                // Full: its pictures are taken first, then it accepts more.
                if (!Pull(ready))
                {
                    throw new DecoderUnavailableException("Windows' H.264 decoder would neither take a packet nor give a picture.");
                }
            }
        }
        finally
        {
            Release(sample);
        }

        while (Pull(ready))
        {
        }
    }

    public void Drain(List<DecodedPicture> ready)
    {
        ArgumentNullException.ThrowIfNull(ready);
        Message(EndOfStream);
        Message(CommandDrain);
        while (Pull(ready))
        {
        }
    }

    public void Dispose()
    {
        if (_transform != IntPtr.Zero)
        {
            Release(_transform);
            _transform = IntPtr.Zero;
        }

        if (_mediaFoundation)
        {
            _ = MFShutdown();
            _mediaFoundation = false;
        }
    }

    // Takes one picture if the decoder has one; false when it needs more input.
    private bool Pull(List<DecodedPicture> ready)
    {
        while (true)
        {
            OutputStreamInfo info;
            Check(((delegate* unmanaged<IntPtr, uint, OutputStreamInfo*, int>)Slot(_transform, 7))(_transform, 0, &info), "describe its output");
            var provides = (info.Flags & (ProvidesSamples | CanProvideSamples)) != 0;
            var sample = IntPtr.Zero;
            if (!provides)
            {
                IntPtr buffer;
                Check(MFCreateMemoryBuffer(Math.Max(info.Size, 1u), &buffer), "allocate a picture");
                try
                {
                    IntPtr created;
                    Check(MFCreateSample(&created), "create a picture sample");
                    sample = created;
                    Check(((delegate* unmanaged<IntPtr, IntPtr, int>)Slot(sample, 42))(sample, buffer), "fill a picture sample");
                }
                finally
                {
                    Release(buffer);
                }
            }

            var output = new OutputDataBuffer { StreamId = 0, Sample = sample };
            uint status;
            var result = ((delegate* unmanaged<IntPtr, uint, uint, OutputDataBuffer*, uint*, int>)Slot(_transform, 25))(_transform, 0, 1, &output, &status);
            if (output.Events != IntPtr.Zero)
            {
                Release(output.Events);
            }

            try
            {
                if (result == NeedMoreInput)
                {
                    return false;
                }

                if (result == StreamChange)
                {
                    // The stream's parameter sets gave the picture's real
                    // size and colour: choose NV12 again and read them.
                    ChooseOutput();
                    continue;
                }

                Check(result, "decode a picture");
                long time;
                Check(((delegate* unmanaged<IntPtr, long*, int>)Slot(output.Sample, 35))(output.Sample, &time), "read a picture's tag");
                ready.Add(new DecodedPicture(time, Read(output.Sample)));
                return true;
            }
            finally
            {
                if (output.Sample != IntPtr.Zero)
                {
                    Release(output.Sample);
                }
                else if (sample != IntPtr.Zero)
                {
                    Release(sample);
                }
            }
        }
    }

    private Nv12Picture Read(IntPtr sample)
    {
        IntPtr buffer;
        Check(((delegate* unmanaged<IntPtr, IntPtr*, int>)Slot(sample, 41))(sample, &buffer), "join a picture's buffers");
        try
        {
            byte* bytes;
            uint maximum;
            uint length;
            Check(((delegate* unmanaged<IntPtr, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &bytes, &maximum, &length), "read a picture");
            try
            {
                var data = new byte[length];
                new ReadOnlySpan<byte>(bytes, (int)length).CopyTo(data);
                // The luma plane's stored rows: the buffer is 1.5 planes.
                var planeHeight = (int)Math.Max(_height, length * 2 / 3 / (uint)_stride);
                return new Nv12Picture(_width, _height, _stride, planeHeight, data, _matrix, _fullRange);
            }
            finally
            {
                _ = ((delegate* unmanaged<IntPtr, int>)Slot(buffer, 4))(buffer);
            }
        }
        finally
        {
            Release(buffer);
        }
    }

    private void ChooseOutput()
    {
        for (uint index = 0; ; index++)
        {
            IntPtr type;
            var result = ((delegate* unmanaged<IntPtr, uint, uint, IntPtr*, int>)Slot(_transform, 14))(_transform, 0, index, &type);
            if (result < 0)
            {
                throw new DecoderUnavailableException($"Windows' H.264 decoder offers no NV12 output (0x{result:X8}).");
            }

            try
            {
                if (!TryGetGuid(type, Subtype, out var subtype) || subtype != VideoNv12)
                {
                    continue;
                }

                Check(((delegate* unmanaged<IntPtr, uint, IntPtr, uint, int>)Slot(_transform, 16))(_transform, 0, type, 0), "give NV12");
                if (!TryGetUInt64(type, FrameSize, out var size))
                {
                    throw new DecoderUnavailableException("Windows' H.264 decoder gave no picture size.");
                }

                _width = (int)(size >> 32);
                _height = (int)(uint)size;
                _stride = TryGetUInt32(type, DefaultStride, out var stride) && (int)stride >= _width ? (int)stride : _width;
                _matrix = TryGetUInt32(type, YuvMatrix, out var matrix)
                    ? matrix switch
                    {
                        1 => ColourMatrix.Bt709,
                        2 => ColourMatrix.Bt601,
                        4 => ColourMatrix.Bt2020,
                        _ => null,
                    }
                    : null;
                _fullRange = TryGetUInt32(type, NominalRange, out var range)
                    ? range switch
                    {
                        1 => true,
                        2 => false,
                        _ => null,
                    }
                    : null;
                return;
            }
            finally
            {
                Release(type);
            }
        }
    }

    private static IntPtr CreateSample(ReadOnlySpan<byte> data, long tag)
    {
        IntPtr buffer;
        Check(MFCreateMemoryBuffer((uint)data.Length, &buffer), "allocate a packet");
        try
        {
            byte* bytes;
            uint maximum;
            uint current;
            Check(((delegate* unmanaged<IntPtr, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, &bytes, &maximum, &current), "lock a packet");
            data.CopyTo(new Span<byte>(bytes, data.Length));
            Check(((delegate* unmanaged<IntPtr, int>)Slot(buffer, 4))(buffer), "unlock a packet");
            Check(((delegate* unmanaged<IntPtr, uint, int>)Slot(buffer, 6))(buffer, (uint)data.Length), "size a packet");
            IntPtr sample;
            Check(MFCreateSample(&sample), "create a packet sample");
            Check(((delegate* unmanaged<IntPtr, IntPtr, int>)Slot(sample, 42))(sample, buffer), "fill a packet sample");
            // The tag rides as the sample's time and comes back on its picture.
            Check(((delegate* unmanaged<IntPtr, long, int>)Slot(sample, 36))(sample, tag), "tag a packet");
            return sample;
        }
        finally
        {
            Release(buffer);
        }
    }

    private void Message(uint message) =>
        Check(((delegate* unmanaged<IntPtr, uint, nuint, int>)Slot(_transform, 23))(_transform, message, 0), "be told of the stream");

    private static IntPtr CreateMediaType()
    {
        IntPtr type;
        Check(MFCreateMediaType(&type), "create a media type");
        return type;
    }

    // IMFAttributes::GetUINT32 7, GetUINT64 8, GetGUID 10, SetUINT32 21,
    // SetUINT64 22, SetGUID 24. IMFTransform::GetOutputStreamInfo 7,
    // GetAttributes 8, GetOutputAvailableType 14, SetInputType 15,
    // SetOutputType 16, ProcessMessage 23, ProcessInput 24, ProcessOutput 25.
    // IMFSample::GetSampleTime 35, SetSampleTime 36,
    // ConvertToContiguousBuffer 41, AddBuffer 42. IMFMediaBuffer::Lock 3,
    // Unlock 4, SetCurrentLength 6.
    private static void SetUInt32(IntPtr attributes, Guid key, uint value) =>
        Check(((delegate* unmanaged<IntPtr, Guid*, uint, int>)Slot(attributes, 21))(attributes, &key, value), "set an attribute");

    private static void SetUInt64(IntPtr attributes, Guid key, ulong value) =>
        Check(((delegate* unmanaged<IntPtr, Guid*, ulong, int>)Slot(attributes, 22))(attributes, &key, value), "set an attribute");

    private static void SetGuid(IntPtr attributes, Guid key, Guid value) =>
        Check(((delegate* unmanaged<IntPtr, Guid*, Guid*, int>)Slot(attributes, 24))(attributes, &key, &value), "set an attribute");

    private static bool TryGetUInt32(IntPtr attributes, Guid key, out uint value)
    {
        uint read;
        var result = ((delegate* unmanaged<IntPtr, Guid*, uint*, int>)Slot(attributes, 7))(attributes, &key, &read);
        value = read;
        return result >= 0;
    }

    private static bool TryGetUInt64(IntPtr attributes, Guid key, out ulong value)
    {
        ulong read;
        var result = ((delegate* unmanaged<IntPtr, Guid*, ulong*, int>)Slot(attributes, 8))(attributes, &key, &read);
        value = read;
        return result >= 0;
    }

    private static bool TryGetGuid(IntPtr attributes, Guid key, out Guid value)
    {
        Guid read;
        var result = ((delegate* unmanaged<IntPtr, Guid*, Guid*, int>)Slot(attributes, 10))(attributes, &key, &read);
        value = read;
        return result >= 0;
    }

    private static void Release(IntPtr instance) =>
        _ = ((delegate* unmanaged<IntPtr, uint>)Slot(instance, 2))(instance);

    private static void* Slot(IntPtr instance, int slot) => (*(void***)instance)[slot];

    private static void Check(int result, string action)
    {
        if (result < 0)
        {
            throw new DecoderUnavailableException($"Windows' H.264 decoder could not {action} (0x{result:X8}).");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OutputStreamInfo
    {
        public uint Flags;
        public uint Size;
        public uint Alignment;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OutputDataBuffer
    {
        public uint StreamId;
        public IntPtr Sample;
        public uint Status;
        public IntPtr Events;
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(Guid* classId, IntPtr outer, uint context, Guid* interfaceId, IntPtr* instance);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(IntPtr* mediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateSample(IntPtr* sample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMemoryBuffer(uint maxLength, IntPtr* buffer);
}

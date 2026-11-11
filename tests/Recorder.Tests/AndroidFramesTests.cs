using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Slice 3 of docs/architecture/android-implementation-plan.md, step 3b: the
// phone's pictures cut to the session's size and converted to RGB, written
// as PNG, and made from the stored stream with a fake decoder. Windows'
// decoder itself is checked against a recorded stream on Windows.
public sealed class AndroidFramesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "android-frames-" + Guid.NewGuid().ToString("N"));

    public AndroidFramesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // Colour, from the matrices' definitions (ITU-R BT.709 and BT.601)

    [Theory]
    [InlineData(235, 128, 128, 255, 255, 255)]
    [InlineData(16, 128, 128, 0, 0, 0)]
    [InlineData(126, 128, 128, 128, 128, 128)]
    // BT.709's red, green and blue at full intensity, in limited range.
    [InlineData(63, 102, 240, 255, 0, 0)]
    [InlineData(173, 42, 26, 0, 255, 0)]
    [InlineData(32, 240, 118, 0, 0, 255)]
    public void ToRgb_Bt709Limited(byte y, byte cb, byte cr, int r, int g, int b)
    {
        var rgb = Uniform(2, 2, y, cb, cr).ToRgb(ColourMatrix.Bt709, fullRange: false);

        AssertNear(r, g, b, rgb);
    }

    [Theory]
    [InlineData(255, 128, 128, 255, 255, 255)]
    [InlineData(0, 128, 128, 0, 0, 0)]
    // BT.601's red at full intensity, in full range.
    [InlineData(76, 85, 255, 255, 0, 0)]
    public void ToRgb_Bt601Full(byte y, byte cb, byte cr, int r, int g, int b)
    {
        var rgb = Uniform(2, 2, y, cb, cr).ToRgb(ColourMatrix.Bt601, fullRange: true);

        AssertNear(r, g, b, rgb);
    }

    [Fact]
    public void ToRgb_EachTwoByTwoBlockSharesItsColourSample()
    {
        // 4 by 2: the left block is neutral, the right block red.
        var decoded = new Nv12Picture(4, 2, 4, 2, [16, 16, 63, 63, 16, 16, 63, 63, 128, 128, 102, 240], ColourMatrix.Bt709, false);
        var rgb = PhonePicture.Cut(decoded, 4, 2).ToRgb(ColourMatrix.Bt709, false);

        for (var row = 0; row < 2; row++)
        {
            AssertNear(0, 0, 0, rgb.AsSpan((row * 12) + 0, 3).ToArray());
            AssertNear(0, 0, 0, rgb.AsSpan((row * 12) + 3, 3).ToArray());
            AssertNear(255, 0, 0, rgb.AsSpan((row * 12) + 6, 3).ToArray());
            AssertNear(255, 0, 0, rgb.AsSpan((row * 12) + 9, 3).ToArray());
        }
    }

    // Cutting the decoder's padded picture to the session's size

    [Fact]
    public void Cut_TakesTheTopLeftOfEachPlane()
    {
        // A 16 by 4 picture stored with 20-byte rows and 6 luma rows; each
        // luma byte is 10 * row + column, each chroma byte 200 + 10 * row + column.
        const int stride = 20;
        const int planeHeight = 6;
        var data = new byte[(stride * planeHeight) + (stride * 2)];
        for (var row = 0; row < 4; row++)
        {
            for (var x = 0; x < 16; x++)
            {
                data[(row * stride) + x] = (byte)((10 * row) + x);
            }
        }

        for (var row = 0; row < 2; row++)
        {
            for (var x = 0; x < 16; x++)
            {
                data[(stride * planeHeight) + (row * stride) + x] = (byte)(200 + (10 * row) + x);
            }
        }

        // An odd size keeps the chroma sample its last column and row share.
        var picture = PhonePicture.Cut(new Nv12Picture(16, 4, stride, planeHeight, data, null, null), 5, 3);

        Assert.Equal(5, picture.Width);
        Assert.Equal(3, picture.Height);
        Assert.Equal(
            [
                0, 1, 2, 3, 4,
                10, 11, 12, 13, 14,
                20, 21, 22, 23, 24,
                200, 201, 202, 203, 204, 205,
                210, 211, 212, 213, 214, 215
            ],
            picture.Data);
    }

    [Fact]
    public void Cut_PictureSmallerThanTheSession_Throws()
    {
        var decoded = new Nv12Picture(16, 16, 16, 16, new byte[16 * 24], null, null);

        Assert.Throws<FormatException>(() => PhonePicture.Cut(decoded, 17, 16));
        Assert.Throws<FormatException>(() => PhonePicture.Cut(decoded with { Data = new byte[100] }, 16, 16));
    }

    [Fact]
    public void SameAs_ComparesEveryByte()
    {
        var first = Uniform(4, 2, 16, 128, 128);
        var same = Uniform(4, 2, 16, 128, 128);
        var data = (byte[])same.Data.Clone();
        data[^1] = 129;
        var other = PhonePicture.Cut(new Nv12Picture(4, 2, 4, 2, data, null, null), 4, 2);

        Assert.True(first.SameAs(same));
        Assert.False(first.SameAs(other));
        Assert.False(first.SameAs(null));
    }

    // PNG (W3C, Portable Network Graphics, third edition)

    [Fact]
    public void Png_HoldsThePixelsExactly()
    {
        const int width = 3;
        const int height = 4;
        var rgb = new byte[width * height * 3];
        for (var i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (byte)(i * 37);
        }

        var png = PngWriter.Encode(width, height, rgb);

        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        var chunks = Chunks(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(chunk => chunk.Type));
        var header = chunks[0].Data;
        Assert.Equal((uint)width, BinaryPrimitives.ReadUInt32BigEndian(header));
        Assert.Equal((uint)height, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));
        Assert.Equal([8, 2, 0, 0, 0], header[8..]);
        // The empty IEND chunk's CRC is the same in every PNG file.
        Assert.Equal([0xAE, 0x42, 0x60, 0x82], png[^4..]);
        Assert.Equal(rgb, Unfilter(Inflate(chunks[1].Data), width, height));
    }

    [Fact]
    public void Png_WrongPixelCount_Throws() =>
        Assert.Throws<ArgumentException>(() => PngWriter.Encode(2, 2, new byte[11]));

    // Making the frames from the stored stream

    [Fact]
    public async Task Frames_WritesEachChangedPictureInOrder()
    {
        var stream = new StreamFile(_folder);
        var config = stream.Add([9, 9, 9], config: true);
        var first = stream.Add([1]);
        var repeat = stream.Add([1]);
        var changed = stream.Add([2]);
        var made = new List<FakeDecoder>();
        var written = new List<WrittenFrame>();
        var failures = new List<string>();
        var hashes = new ArtifactHashRegistry();
        using var frames = new PhoneFrames(
            _folder,
            stream.Path,
            (width, height) => Add(made, new FakeDecoder(width, height)),
            hashes,
            () => 42,
            written.Add,
            failures.Add,
            workers: 2);

        frames.Session(6, 4);
        frames.Packet(config);
        frames.Packet(first);
        frames.Packet(repeat);
        frames.Packet(changed);
        await frames.CompleteAsync();

        Assert.Empty(failures);
        var decoder = Assert.Single(made);
        Assert.Equal((6, 4), (decoder.Width, decoder.Height));
        // The parameter sets go to the decoder with the picture after them.
        Assert.Equal([9, 9, 9, 1], decoder.AccessUnits[0]);
        Assert.Equal([[1], [2]], decoder.AccessUnits.Skip(1));
        Assert.True(decoder.Disposed);

        Assert.Equal(2, frames.Written);
        Assert.Equal(0, frames.Waiting);
        Assert.Equal([1UL, 2UL], written.Select(frame => frame.Sequence));
        Assert.Same(first, written[0].Packet);
        Assert.Same(changed, written[1].Packet);
        Assert.Equal(0, written[0].Frame.RepeatsBefore);
        Assert.Equal(1, written[1].Frame.RepeatsBefore);
        var frame = written[0].Frame;
        Assert.Equal("frames/android/0000000001.png", frame.Path);
        Assert.Equal((6, 4), (frame.Width, frame.Height));
        Assert.Equal((16, 16), (frame.DecodedWidth, frame.DecodedHeight));
        Assert.Equal((1, first.ByteOffset, first.PresentationMicroseconds), (frame.Session, frame.PacketByteOffset, (long?)frame.PresentationMicroseconds));
        Assert.Equal(("bt709", "limited", "nearest", "fake"), (frame.ColourMatrix, frame.ColourRange, frame.ChromaUpsampling, frame.Decoder));
        Assert.Equal(42, frame.WrittenAtNanoseconds);
        Assert.Equal([AndroidFrameRecords.CompressedOnPhoneFlag], written[0].Flags);

        var file = Path.Combine(_folder, "frames", "android", "0000000001.png");
        var png = File.ReadAllBytes(file);
        Assert.Equal(frame.ByteLength, png.Length);
        Assert.True(hashes.TryGetUnchanged(file, out var hash));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(), hash);
        // Luma 1 in limited range is below black, so the frame is black.
        Assert.All(Unfilter(Inflate(Chunks(png)[1].Data), 6, 4), value => Assert.Equal(0, value));
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "frames", "android"), "*.tmp"));
    }

    [Fact]
    public async Task Frames_NewSession_NewDecoderAndAFrameEvenIfTheSame()
    {
        var stream = new StreamFile(_folder);
        var portrait = stream.Add([5]);
        var landscape = stream.Add([5], session: 2);
        var made = new List<FakeDecoder>();
        var written = new List<WrittenFrame>();
        using var frames = new PhoneFrames(
            _folder,
            stream.Path,
            (width, height) => Add(made, new FakeDecoder(width, height)),
            null,
            () => 0,
            written.Add,
            _ => { },
            workers: 1);

        frames.Session(4, 8);
        frames.Packet(portrait);
        frames.Session(8, 4);
        frames.Packet(landscape);
        await frames.CompleteAsync();

        Assert.Equal([(4, 8), (8, 4)], made.Select(decoder => (decoder.Width, decoder.Height)));
        Assert.All(made, decoder => Assert.True(decoder.Disposed));
        Assert.Equal([(4, 8, 1), (8, 4, 2)], written.Select(frame => (frame.Frame.Width, frame.Frame.Height, frame.Frame.Session)));
    }

    [Fact]
    public async Task Frames_DecoderThatHoldsPictures_DrainedAtTheSessionsEnd()
    {
        var stream = new StreamFile(_folder);
        var packets = new[] { stream.Add([1]), stream.Add([2]), stream.Add([3]) };
        var written = new List<WrittenFrame>();
        using var frames = new PhoneFrames(
            _folder,
            stream.Path,
            (width, height) => new FakeDecoder(width, height) { Holds = 2 },
            null,
            () => 0,
            written.Add,
            _ => { },
            workers: 3);

        frames.Session(4, 4);
        foreach (var packet in packets)
        {
            frames.Packet(packet);
        }

        await frames.CompleteAsync();

        Assert.Equal(packets, written.Select(frame => frame.Packet));
    }

    [Fact]
    public async Task Frames_DecoderUnavailable_FailsOnceAndStops()
    {
        var stream = new StreamFile(_folder);
        var first = stream.Add([1]);
        var second = stream.Add([2]);
        var failures = new List<string>();
        var written = new List<WrittenFrame>();
        using var frames = new PhoneFrames(
            _folder,
            stream.Path,
            (_, _) => throw new DecoderUnavailableException("No decoder here."),
            null,
            () => 0,
            written.Add,
            failures.Add,
            workers: 1);

        frames.Session(4, 4);
        frames.Packet(first);
        frames.Packet(second);
        await frames.CompleteAsync();

        Assert.Equal(["No decoder here."], failures);
        Assert.True(frames.Failed);
        Assert.Empty(written);
        Assert.Equal(0, frames.Waiting);
    }

    [Fact]
    public async Task Frames_PictureForNoPacket_Fails()
    {
        var stream = new StreamFile(_folder);
        var failures = new List<string>();
        using var frames = new PhoneFrames(
            _folder,
            stream.Path,
            (width, height) => new FakeDecoder(width, height) { TagOffset = 100 },
            null,
            () => 0,
            _ => { },
            failures.Add,
            workers: 1);

        frames.Session(4, 4);
        frames.Packet(stream.Add([1]));
        await frames.CompleteAsync();

        Assert.Equal(["The decoder gave a picture for no packet it was given (tag 100)."], failures);
    }

    [Fact]
    public async Task Frames_UnexpectedFault_StopsTheFramesNotTheProcess()
    {
        var stream = new StreamFile(_folder);
        var failures = new List<string>();
        using var frames = new PhoneFrames(
            _folder,
            stream.Path,
            (width, height) => new FakeDecoder(width, height) { Fault = new InvalidOperationException("Broken.") },
            null,
            () => 0,
            _ => { },
            failures.Add,
            workers: 1);

        frames.Session(4, 4);
        frames.Packet(stream.Add([1]));
        await frames.CompleteAsync();

        Assert.Equal(["InvalidOperationException: Broken."], failures);
    }

    [Fact]
    public async Task Frames_ColourNotInTheStream_AssumedAndFlagged()
    {
        var stream = new StreamFile(_folder);
        var written = new List<WrittenFrame>();
        using var frames = new PhoneFrames(
            _folder,
            stream.Path,
            (width, height) => new FakeDecoder(width, height) { Reports = false },
            null,
            () => 0,
            written.Add,
            _ => { },
            workers: 1);

        frames.Session(4, 4);
        frames.Packet(stream.Add([1]));
        await frames.CompleteAsync();

        var frame = Assert.Single(written);
        Assert.Equal(("bt709", "limited"), (frame.Frame.ColourMatrix, frame.Frame.ColourRange));
        Assert.Contains(AndroidFrameRecords.ColourAssumedFlag, frame.Flags);
    }

    // Windows' decoder, against a stream the size of the phone's

    [Fact]
    public async Task MediaFoundation_MakesFramesFromAStreamThePhonesSize()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows' Media Foundation decoder is only on Windows.");
        var data = Path.Combine(AppContext.BaseDirectory, "TestData");
        var stream = new StreamFile(_folder);
        var packets = File.ReadAllLines(Path.Combine(data, "android-screen-sample.lengths"))
            .Aggregate(
                (Bytes: File.ReadAllBytes(Path.Combine(data, "android-screen-sample.h264")), Offset: 0, Packets: new List<FramePacket>()),
                (state, line) =>
                {
                    var length = int.Parse(line, System.Globalization.CultureInfo.InvariantCulture);
                    state.Packets.Add(stream.Add(state.Bytes[state.Offset..(state.Offset + length)]));
                    return (state.Bytes, state.Offset + length, state.Packets);
                })
            .Packets;
        var written = new List<WrittenFrame>();
        var failures = new List<string>();
        using var frames = new PhoneFrames(
            _folder,
            stream.Path,
            (width, height) => new MediaFoundationH264Decoder(width, height),
            null,
            () => 0,
            written.Add,
            failures.Add,
            workers: 2);

        frames.Session(1080, 2404);
        foreach (var packet in packets)
        {
            frames.Packet(packet);
        }

        await frames.CompleteAsync();

        Assert.Empty(failures);
        // Six pictures of one colour, then six of another: two frames.
        Assert.Equal(2, written.Count);
        Assert.Same(packets[0], written[0].Packet);
        Assert.Same(packets[6], written[1].Packet);
        Assert.Equal(5, written[1].Frame.RepeatsBefore);
        Assert.Equal("media-foundation-h264", written[0].Frame.Decoder);
        Assert.True(written[0].Frame.DecodedWidth >= 1080 && written[0].Frame.DecodedHeight >= 2404);
        Assert.Equal((1080, 2404), (written[0].Frame.Width, written[0].Frame.Height));
        Assert.Equal(("bt709", "limited"), (written[0].Frame.ColourMatrix, written[0].Frame.ColourRange));
        AssertCentre(0xC0, 0x30, 0x20, written[0].Frame);
        AssertCentre(0x20, 0x60, 0xA0, written[1].Frame);
    }

    private void AssertCentre(int r, int g, int b, AndroidDeviceFrame frame)
    {
        var png = File.ReadAllBytes(Path.Combine(_folder, frame.Path.Replace('/', Path.DirectorySeparatorChar)));
        var rgb = Unfilter(Inflate(Chunks(png)[1].Data), frame.Width, frame.Height);
        var centre = (((frame.Height / 2) * frame.Width) + (frame.Width / 2)) * 3;
        // Within the rounding of encoding to limited-range YUV and back.
        Assert.InRange(rgb[centre], r - 5, r + 5);
        Assert.InRange(rgb[centre + 1], g - 5, g + 5);
        Assert.InRange(rgb[centre + 2], b - 5, b + 5);
    }

    private static PhonePicture Uniform(int width, int height, byte y, byte cb, byte cr)
    {
        var data = new byte[(width * height) + (width * ((height + 1) / 2))];
        data.AsSpan(0, width * height).Fill(y);
        for (var i = width * height; i < data.Length; i += 2)
        {
            data[i] = cb;
            data[i + 1] = cr;
        }

        return PhonePicture.Cut(new Nv12Picture(width, height, width, height, data, null, null), width, height);
    }

    private static void AssertNear(int r, int g, int b, byte[] rgb)
    {
        for (var i = 0; i < rgb.Length; i += 3)
        {
            Assert.InRange(rgb[i], r - 2, r + 2);
            Assert.InRange(rgb[i + 1], g - 2, g + 2);
            Assert.InRange(rgb[i + 2], b - 2, b + 2);
        }
    }

    private static T Add<T>(List<T> list, T item)
    {
        lock (list)
        {
            list.Add(item);
        }

        return item;
    }

    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        var offset = 8;
        while (offset < png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            chunks.Add((System.Text.Encoding.ASCII.GetString(png, offset + 4, 4), png[(offset + 8)..(offset + 8 + length)]));
            offset += 12 + length;
        }

        return chunks;
    }

    private static byte[] Inflate(byte[] zlib)
    {
        using var input = new ZLibStream(new MemoryStream(zlib), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    // Only the None and Up filters, which are all the writer uses.
    private static byte[] Unfilter(byte[] filtered, int width, int height)
    {
        var rowBytes = width * 3;
        Assert.Equal((rowBytes + 1) * height, filtered.Length);
        var rgb = new byte[rowBytes * height];
        for (var row = 0; row < height; row++)
        {
            var filter = filtered[row * (rowBytes + 1)];
            Assert.Contains(filter, new byte[] { 0, 2 });
            for (var i = 0; i < rowBytes; i++)
            {
                var value = filtered[(row * (rowBytes + 1)) + 1 + i];
                var above = row == 0 || filter == 0 ? 0 : rgb[((row - 1) * rowBytes) + i];
                rgb[(row * rowBytes) + i] = unchecked((byte)(value + above));
            }
        }

        return rgb;
    }

    // The stream file, as the collector writes it: the packets' bytes one
    // after another.
    private sealed class StreamFile
    {
        private long _offset;
        private long _time = 1_000_000;

        public StreamFile(string folder)
        {
            Path = System.IO.Path.Combine(folder, "screen.h264");
            File.WriteAllBytes(Path, []);
        }

        public string Path { get; }

        public FramePacket Add(byte[] bytes, bool config = false, int session = 1)
        {
            using (var file = new FileStream(Path, FileMode.Append))
            {
                file.Write(bytes);
            }

            var packet = new FramePacket(
                session,
                _offset,
                bytes.Length,
                config,
                config ? null : _time,
                _time * 1000,
                _time * 1000,
                null,
                null,
                null,
                []);
            _offset += bytes.Length;
            _time += 16_667;
            return packet;
        }
    }

    // Step 3c: the playback index a recording file holds keeps the phone's
    // frames, so the player built from it shows them.
    [Fact]
    public void PlaybackIndex_KeepsThePhoneFrames_AndTheArchiveShowsThem()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "frames", "android"));
        File.WriteAllBytes(Path.Combine(_folder, AndroidFrameRecords.FramePath(1)), [1]);
        File.WriteAllBytes(Path.Combine(_folder, AndroidFrameRecords.FramePath(2)), [1]);
        var collector = CollectorDescriptor.Create(
            "android.screen", "AndroidScreenCollector", "1", [AndroidFrameRecords.FramesChannel], "test");
        var builder = new PlaybackIndexBuilder(10_000_000, TimeSpan.Zero);
        builder.Add(1, RecorderEventFactory.Create("s", collector, AndroidFrameRecords.FramesChannel, 1, 100_000_000,
            AndroidFrameRecords.FrameEventType, new { path = AndroidFrameRecords.FramePath(1), width = 1080, height = 2404, session = 1 }));
        builder.Add(2, RecorderEventFactory.Create("s", collector, AndroidFrameRecords.FramesChannel, 2, 900_000_000,
            AndroidFrameRecords.FrameEventType, new { path = AndroidFrameRecords.FramePath(2), width = 2404, height = 1080, session = 2 }));
        var index = builder.Build();

        Assert.Equal(25, index.Version);
        Assert.Equal(2, index.Events.Count(item => item.Channel == AndroidFrameRecords.FramesChannel));

        var archiveBuilder = new SessionPlaybackArchiveBuilder(_folder, retainEvents: false);
        archiveBuilder.AddIndex(index);
        var started = DateTimeOffset.UtcNow;
        var archive = archiveBuilder.Build(new SessionManifest(
            "1.1", "s", "completed", started, started.AddSeconds(1), 1_000_000_000, 10_000_000, 1,
            "Windows", ".NET", "X64",
            new SessionRecordingConfiguration(true, true, true, false, 5, true, true),
            [], [], 1, 0, null));

        Assert.Equal(SessionFrameSource.Phone, archive.FrameSource);
        Assert.Equal(
            [(100_000_000L, 1080, 2404), (900_000_000L, 2404, 1080)],
            archive.Frames.Select(frame => (frame.MonotonicNanoseconds, frame.Width, frame.Height)));
    }
}

/// <summary>
/// A decoder whose picture is padded to whole 16-pixel blocks, as H.264's
/// is, with every luma sample the access unit's last byte and neutral
/// colour. It can hold pictures back, as a decoder that reorders does.
/// </summary>
internal sealed class FakeDecoder(int width, int height) : IH264Decoder
{
    private readonly Queue<DecodedPicture> _held = [];

    public int Width { get; } = width;

    public int Height { get; } = height;

    public List<byte[]> AccessUnits { get; } = [];

    public int Holds { get; init; }

    public long TagOffset { get; init; }

    public bool Reports { get; init; } = true;

    public Exception? Fault { get; init; }

    public bool Disposed { get; private set; }

    public string Name => "fake";

    public void Decode(ReadOnlySpan<byte> accessUnit, long tag, List<DecodedPicture> ready)
    {
        if (Fault is not null)
        {
            throw Fault;
        }

        AccessUnits.Add(accessUnit.ToArray());
        var paddedWidth = (Width + 15) / 16 * 16;
        var paddedHeight = (Height + 15) / 16 * 16;
        var data = new byte[paddedWidth * paddedHeight * 3 / 2];
        data.AsSpan(0, paddedWidth * paddedHeight).Fill(accessUnit[^1]);
        data.AsSpan(paddedWidth * paddedHeight).Fill(128);
        _held.Enqueue(new DecodedPicture(
            tag + TagOffset,
            new Nv12Picture(paddedWidth, paddedHeight, paddedWidth, paddedHeight, data, Reports ? ColourMatrix.Bt709 : null, Reports ? false : null)));
        while (_held.Count > Holds)
        {
            ready.Add(_held.Dequeue());
        }
    }

    public void Drain(List<DecodedPicture> ready)
    {
        while (_held.Count > 0)
        {
            ready.Add(_held.Dequeue());
        }
    }

    public void Dispose() => Disposed = true;
}

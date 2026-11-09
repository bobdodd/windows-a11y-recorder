using System.Buffers.Binary;
using System.IO.Compression;

namespace Recorder.Collectors.Android;

/// <summary>The YUV matrix a picture's colours were encoded with.</summary>
public enum ColourMatrix
{
    Bt709,
    Bt601,
    Bt2020,
}

/// <summary>
/// A decoded picture in NV12: a full-size luma plane, then a half-width,
/// half-height plane of interleaved Cb and Cr, each row <see cref="Stride"/>
/// bytes. <see cref="Data"/> holds the luma plane's
/// <see cref="PlaneHeight"/> rows, then the chroma plane's.
/// </summary>
/// <param name="Width">The decoder's picture width, which may be padded past the session's.</param>
/// <param name="Height">The decoder's picture height, which may be padded past the session's.</param>
/// <param name="Stride">Bytes from one row to the next, in both planes.</param>
/// <param name="PlaneHeight">Rows in the luma plane as stored; the chroma plane starts after them.</param>
/// <param name="Matrix">The matrix the decoder reported from the stream, or null if it reported none.</param>
/// <param name="FullRange">Whether the decoder reported full range (0 to 255); null if it reported none.</param>
public sealed record Nv12Picture(
    int Width,
    int Height,
    int Stride,
    int PlaneHeight,
    byte[] Data,
    ColourMatrix? Matrix,
    bool? FullRange);

/// <summary>
/// The phone's picture cut to the capture session's size, in NV12 with
/// no padding: luma rows of <see cref="Width"/> bytes, then chroma rows of
/// <see cref="ChromaRowBytes"/>. Two pictures are the same when their
/// <see cref="Data"/> is.
/// </summary>
public sealed class PhonePicture
{
    private PhonePicture(int width, int height, byte[] data)
    {
        Width = width;
        Height = height;
        Data = data;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Data { get; }

    public int ChromaRowBytes => (Width + 1) / 2 * 2;

    public int ChromaRows => (Height + 1) / 2;

    /// <summary>
    /// The top left <paramref name="width"/> by <paramref name="height"/>
    /// of a decoded picture. H.264 pads a picture to whole 16-pixel blocks
    /// at the right and bottom, so the session's picture is the top left.
    /// </summary>
    public static PhonePicture Cut(Nv12Picture decoded, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        if (width <= 0 || height <= 0 || width > decoded.Width || height > decoded.Height)
        {
            throw new FormatException(
                $"The decoded picture, {decoded.Width} by {decoded.Height}, is smaller than the capture session's {width} by {height}.");
        }

        var chromaRowBytes = (width + 1) / 2 * 2;
        var chromaRows = (height + 1) / 2;
        var chromaStart = (long)decoded.Stride * decoded.PlaneHeight;
        var needed = chromaStart + ((long)decoded.Stride * (chromaRows - 1)) + chromaRowBytes;
        if (decoded.Stride < decoded.Width || decoded.PlaneHeight < decoded.Height || decoded.Data.Length < needed)
        {
            throw new FormatException(
                $"The decoder's picture buffer ({decoded.Data.Length} bytes, {decoded.Stride} bytes a row, {decoded.PlaneHeight} rows) does not hold a {decoded.Width} by {decoded.Height} picture.");
        }

        var data = new byte[((long)width * height) + ((long)chromaRowBytes * chromaRows)];
        var source = decoded.Data.AsSpan();
        var target = data.AsSpan();
        for (var row = 0; row < height; row++)
        {
            source.Slice(row * decoded.Stride, width).CopyTo(target.Slice(row * width, width));
        }

        var chroma = target[(width * height)..];
        for (var row = 0; row < chromaRows; row++)
        {
            source.Slice((int)chromaStart + (row * decoded.Stride), chromaRowBytes)
                .CopyTo(chroma.Slice(row * chromaRowBytes, chromaRowBytes));
        }

        return new PhonePicture(width, height, data);
    }

    public bool SameAs(PhonePicture? other) =>
        other is not null && other.Width == Width && other.Height == Height && other.Data.AsSpan().SequenceEqual(Data);

    /// <summary>
    /// The picture as 8-bit RGB, top row first, three bytes a pixel. Each
    /// 2 by 2 block of pixels shares its chroma sample (nearest, not
    /// interpolated), so no colour is made up between samples.
    /// </summary>
    public byte[] ToRgb(ColourMatrix matrix, bool fullRange)
    {
        var tables = ColourTables.For(matrix, fullRange);
        var rgb = new byte[(long)Width * Height * 3];
        var luma = Data.AsSpan(0, Width * Height);
        var chroma = Data.AsSpan(Width * Height);
        for (var row = 0; row < Height; row++)
        {
            var lumaRow = luma.Slice(row * Width, Width);
            var chromaRow = chroma.Slice(row / 2 * ChromaRowBytes, ChromaRowBytes);
            var output = rgb.AsSpan(row * Width * 3, Width * 3);
            for (var x = 0; x < Width; x++)
            {
                var cb = chromaRow[x & ~1];
                var cr = chromaRow[(x & ~1) + 1];
                var y = tables.Y[lumaRow[x]];
                output[x * 3] = Clamp(y + tables.CrToR[cr]);
                output[(x * 3) + 1] = Clamp(y + tables.CbToG[cb] + tables.CrToG[cr]);
                output[(x * 3) + 2] = Clamp(y + tables.CbToB[cb]);
            }
        }

        return rgb;
    }

    private static byte Clamp(int fixedPoint)
    {
        var value = (fixedPoint + (1 << 15)) >> 16;
        return (byte)Math.Clamp(value, 0, 255);
    }

    // Each table gives a sample's contribution in 16.16 fixed point, from
    // the matrix's Kr and Kb (ITU-R BT.709, BT.601 and BT.2020): Y' scaled
    // to 0 to 255, and R = Y' + 2(1 - Kr)Cr, B = Y' + 2(1 - Kb)Cb,
    // G = Y' - (2Kb(1 - Kb)/Kg)Cb - (2Kr(1 - Kr)/Kg)Cr, with Cb and Cr
    // scaled to -127.5 to 127.5. Limited range is luma 16 to 235 and chroma
    // 16 to 240.
    private sealed class ColourTables
    {
        private static readonly Dictionary<(ColourMatrix, bool), ColourTables> Made = [];

        private ColourTables(ColourMatrix matrix, bool fullRange)
        {
            var (kr, kb) = matrix switch
            {
                ColourMatrix.Bt601 => (0.299, 0.114),
                ColourMatrix.Bt2020 => (0.2627, 0.0593),
                _ => (0.2126, 0.0722),
            };
            var kg = 1 - kr - kb;
            var lumaScale = fullRange ? 1.0 : 255.0 / 219.0;
            var lumaOffset = fullRange ? 0 : 16;
            var chromaScale = fullRange ? 1.0 : 255.0 / 224.0;
            for (var value = 0; value < 256; value++)
            {
                var c = (value - 128) * chromaScale;
                Y[value] = Fixed((value - lumaOffset) * lumaScale);
                CrToR[value] = Fixed(2 * (1 - kr) * c);
                CbToB[value] = Fixed(2 * (1 - kb) * c);
                CbToG[value] = Fixed(-2 * kb * (1 - kb) / kg * c);
                CrToG[value] = Fixed(-2 * kr * (1 - kr) / kg * c);
            }
        }

        public int[] Y { get; } = new int[256];

        public int[] CrToR { get; } = new int[256];

        public int[] CbToB { get; } = new int[256];

        public int[] CbToG { get; } = new int[256];

        public int[] CrToG { get; } = new int[256];

        public static ColourTables For(ColourMatrix matrix, bool fullRange)
        {
            lock (Made)
            {
                if (!Made.TryGetValue((matrix, fullRange), out var tables))
                {
                    tables = new ColourTables(matrix, fullRange);
                    Made[(matrix, fullRange)] = tables;
                }

                return tables;
            }
        }

        private static int Fixed(double value) => (int)Math.Round(value * 65536);
    }
}

/// <summary>
/// Writes 8-bit RGB as PNG (W3C, Portable Network Graphics, third edition):
/// one IDAT of the rows, each with the Up filter, compressed with zlib at
/// its fastest level, as a phone frame must be written while the next
/// arrives. The file is lossless at any level; only its size changes.
/// </summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = MakeCrcTable();

    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgb)
    {
        var rowBytes = checked(width * 3);
        if (rgb.Length != (long)rowBytes * height)
        {
            throw new ArgumentException("The pixels are not the picture's size.", nameof(rgb));
        }

        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
        header[8] = 8; // bits per sample
        header[9] = 2; // colour type: RGB
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // not interlaced
        WriteChunk(output, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var filtered = new byte[rowBytes + 1];
            filtered[0] = 2; // Up: each byte less the byte above it
            for (var row = 0; row < height; row++)
            {
                var current = rgb.Slice(row * rowBytes, rowBytes);
                if (row == 0)
                {
                    current.CopyTo(filtered.AsSpan(1));
                }
                else
                {
                    var above = rgb.Slice((row - 1) * rowBytes, rowBytes);
                    for (var i = 0; i < rowBytes; i++)
                    {
                        filtered[i + 1] = unchecked((byte)(current[i] - above[i]));
                    }
                }

                zlib.Write(filtered);
            }
        }

        WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        var crc = Crc(Crc(0xFFFFFFFFu, type), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        output.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Recorder.Analysis.Summary;

namespace Recorder.Tests;

/// <summary>
/// Writes PNGs for the tests: the same pixels can be written with other
/// filters, compression, colour types, and chunks.
/// </summary>
internal static class TestPng
{
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }

        return c;
    }).ToArray();

    /// <summary>A PNG of red, green, blue, and alpha pixels, 8 bits each, written as colour type 6 or, where every alpha is 255 and asked, type 2.</summary>
    public static byte[] Rgba(int width, int height, byte[] rgba, int filter = 0, CompressionLevel level = CompressionLevel.Optimal,
        bool asRgb = false, IEnumerable<(string Type, byte[] Body)>? chunks = null, int interlace = 0)
    {
        var channels = asRgb ? 3 : 4;
        var rows = new List<byte[]>();
        for (var y = 0; y < height; y++)
        {
            var row = new byte[width * channels];
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    row[x * channels + c] = rgba[(y * width + x) * 4 + c];
                }
            }

            rows.Add(row);
        }

        return Write(width, height, 8, asRgb ? 2 : 6, rows, channels, filter, level, chunks, interlace);
    }

    /// <summary>A PNG of palette entries, one byte each, with the palette and, if given, its alpha.</summary>
    public static byte[] Palette(int width, int height, byte[] entries, byte[] palette, byte[]? alpha = null)
    {
        var rows = Enumerable.Range(0, height).Select(y => entries.AsSpan(y * width, width).ToArray()).ToList();
        var chunks = new List<(string, byte[])> { ("PLTE", palette) };
        if (alpha is not null)
        {
            chunks.Add(("tRNS", alpha));
        }

        return Write(width, height, 8, 3, rows, 1, 0, CompressionLevel.Optimal, chunks, 0);
    }

    private static byte[] Write(int width, int height, int depth, int colour, List<byte[]> rows, int bytesPerPixel, int filter,
        CompressionLevel level, IEnumerable<(string Type, byte[] Body)>? chunks, int interlace)
    {
        using var raw = new MemoryStream();
        var above = new byte[rows[0].Length];
        foreach (var row in rows)
        {
            raw.WriteByte((byte)filter);
            var filtered = new byte[row.Length];
            for (var i = 0; i < row.Length; i++)
            {
                int left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                int up = above[i];
                int corner = i >= bytesPerPixel ? above[i - bytesPerPixel] : 0;
                filtered[i] = filter switch
                {
                    1 => (byte)(row[i] - left),
                    2 => (byte)(row[i] - up),
                    3 => (byte)(row[i] - ((left + up) >> 1)),
                    4 => (byte)(row[i] - Paeth(left, up, corner)),
                    _ => row[i],
                };
            }

            raw.Write(filtered);
            above = row;
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, level, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = (byte)depth;
        header[9] = (byte)colour;
        header[12] = (byte)interlace;
        Chunk(png, "IHDR", header);
        foreach (var (type, body) in chunks ?? [])
        {
            Chunk(png, type, body);
        }

        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>An iCCP chunk's body: the profile's name, then the profile compressed at the level given.</summary>
    public static byte[] IccProfile(string name, byte[] profile, CompressionLevel level)
    {
        using var body = new MemoryStream();
        body.Write(Encoding.ASCII.GetBytes(name));
        body.WriteByte(0);
        body.WriteByte(0);
        using (var zlib = new ZLibStream(body, level, leaveOpen: true))
        {
            zlib.Write(profile);
        }

        return body.ToArray();
    }

    private static int Paeth(int left, int up, int corner)
    {
        var estimate = left + up - corner;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toCorner = Math.Abs(estimate - corner);
        return toLeft <= toUp && toLeft <= toCorner ? left : toUp <= toCorner ? up : corner;
    }

    private static void Chunk(Stream png, string type, byte[] body)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
        png.Write(length);
        var typed = Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
        png.Write(typed);
        var crc = 0xFFFFFFFFu;
        foreach (var b in typed)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        Span<byte> check = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(check, crc ^ 0xFFFFFFFFu);
        png.Write(check);
    }
}

/// <summary>
/// The store of unique images and an image's key: recording-summary.md,
/// "Unique images across the recording", decisions 1 and 2.
/// </summary>
public sealed class ImageStoreTests : IDisposable
{
    private readonly string _session = Path.Combine(Path.GetTempPath(), "recorder-images-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_session))
        {
            Directory.Delete(_session, recursive: true);
        }
    }

    // A picture of 7 by 5 pixels, every pixel different, all opaque.
    private static byte[] Pixels()
    {
        var rgba = new byte[7 * 5 * 4];
        for (var i = 0; i < 7 * 5; i++)
        {
            rgba[i * 4] = (byte)(i * 7);
            rgba[i * 4 + 1] = (byte)(255 - i * 3);
            rgba[i * 4 + 2] = (byte)(i * 11 % 256);
            rgba[i * 4 + 3] = 255;
        }

        return rgba;
    }

    [Fact]
    public void Two_files_of_the_same_pixels_written_differently_have_the_same_key()
    {
        var pixels = Pixels();
        var plain = TestPng.Rgba(7, 5, pixels, filter: 0, level: CompressionLevel.NoCompression);
        var paeth = TestPng.Rgba(7, 5, pixels, filter: 4, level: CompressionLevel.SmallestSize);
        var rgb = TestPng.Rgba(7, 5, pixels, filter: 1, asRgb: true);
        Assert.NotEqual(plain, paeth);

        var key = PngKey.Of(plain);

        Assert.Equal("pixels", key.Basis);
        Assert.Equal(key, PngKey.Of(paeth));
        Assert.Equal(key, PngKey.Of(rgb));
    }

    [Fact]
    public void A_palette_picture_has_the_key_of_the_same_pixels_written_in_full()
    {
        byte[] palette = [10, 20, 30, 200, 100, 50];
        byte[] alpha = [255, 128];
        byte[] entries = [0, 1, 1, 0, 1, 0];
        var rgba = entries.SelectMany(entry => new[] { palette[entry * 3], palette[entry * 3 + 1], palette[entry * 3 + 2], alpha[entry] }).ToArray();

        Assert.Equal(PngKey.Of(TestPng.Rgba(3, 2, rgba)), PngKey.Of(TestPng.Palette(3, 2, entries, palette, alpha)));
    }

    [Fact]
    public void One_pixel_changed_gives_another_key()
    {
        var pixels = Pixels();
        var changed = (byte[])pixels.Clone();
        changed[17 * 4 + 2] ^= 1;

        Assert.NotEqual(PngKey.Of(TestPng.Rgba(7, 5, pixels)).Key, PngKey.Of(TestPng.Rgba(7, 5, changed)).Key);
    }

    [Fact]
    public void A_colour_profile_counts_in_the_key_by_the_profile_not_its_compression()
    {
        var pixels = Pixels();
        var profile = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("a test colour profile ", 40)));
        var fast = TestPng.Rgba(7, 5, pixels, chunks: [("iCCP", TestPng.IccProfile("one", profile, CompressionLevel.Fastest))]);
        var small = TestPng.Rgba(7, 5, pixels, chunks: [("iCCP", TestPng.IccProfile("two", profile, CompressionLevel.SmallestSize))]);
        var srgb = TestPng.Rgba(7, 5, pixels, chunks: [("sRGB", [0])]);

        Assert.Equal(PngKey.Of(fast), PngKey.Of(small));
        Assert.NotEqual(PngKey.Of(TestPng.Rgba(7, 5, pixels)).Key, PngKey.Of(fast).Key);
        Assert.NotEqual(PngKey.Of(TestPng.Rgba(7, 5, pixels)).Key, PngKey.Of(srgb).Key);
    }

    [Fact]
    public void A_picture_whose_pixels_cannot_be_read_is_keyed_by_its_bytes()
    {
        var interlaced = TestPng.Rgba(7, 5, Pixels(), interlace: 1);

        var key = PngKey.Of(interlaced);

        Assert.Equal("bytes", key.Basis);
        Assert.NotEqual(PngKey.Of(TestPng.Rgba(7, 5, Pixels())).Key, key.Key);
        Assert.Equal(key, PngKey.Of((byte[])interlaced.Clone()));
    }

    [Fact]
    public async Task The_store_keeps_the_first_file_with_a_key()
    {
        var store = new ImageStore(_session, "analysis/images");
        var first = TestPng.Rgba(7, 5, Pixels(), filter: 0);
        var second = TestPng.Rgba(7, 5, Pixels(), filter: 2);

        var kept = await store.KeepAsync(first, Token);
        var again = await store.KeepAsync(second, Token);

        Assert.Equal(kept, again);
        Assert.Equal($"analysis/images/{kept.Key}.png", kept.Path);
        Assert.Equal(first, await File.ReadAllBytesAsync(kept.AbsolutePath, Token));
        Assert.Equal([kept.AbsolutePath], Directory.GetFiles(Path.Combine(_session, "analysis", "images")));
    }
}

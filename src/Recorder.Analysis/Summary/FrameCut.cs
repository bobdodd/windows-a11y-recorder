using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;

namespace Recorder.Analysis.Summary;

/// <summary>
/// A frame cut to the report's scope: the bounding rectangle of the
/// windows in scope, with what lies in it outside every one of them painted
/// a plain grey, so that no other window or the desktop is described
/// (report-fixes-2026-10-18.md, part 4). The frame is not changed: the cut
/// is a new picture, kept and keyed as its own.
/// </summary>
public static class FrameCut
{
    /// <summary>The grey painted outside the windows in scope, middle grey.</summary>
    public const byte Grey = 128;

    /// <summary>The cut as a PNG, red, green, blue, and alpha, and whether any of it was painted; null where the frame's pixels cannot be read.</summary>
    public static (byte[] Png, bool Painted)? Cut(byte[] frame, FrameScope scope)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(scope);
        if (!scope.Limited || PngKey.Decode(frame) is not { } pixels)
        {
            return null;
        }

        var crop = scope.Crop.Intersect(new ScreenRect(0, 0, pixels.Width, pixels.Height));
        if (crop.IsEmpty)
        {
            return null;
        }

        var bytes = pixels.Wide ? 8 : 4;
        var rows = new List<byte[]>(crop.Height);
        var painted = false;
        for (var y = crop.Y; y < crop.Bottom; y++)
        {
            var row = pixels.Rows[y].AsSpan(crop.X * bytes, crop.Width * bytes).ToArray();
            for (var x = crop.X; x < crop.Right; x++)
            {
                if (scope.Windows.Any(window => window.Contains(x, y)))
                {
                    continue;
                }

                painted = true;
                var at = (x - crop.X) * bytes;
                if (pixels.Wide)
                {
                    for (var channel = 0; channel < 3; channel++)
                    {
                        row[at + (channel * 2)] = Grey;
                        row[at + (channel * 2) + 1] = Grey;
                    }

                    row[at + 6] = 255;
                    row[at + 7] = 255;
                }
                else
                {
                    row[at] = Grey;
                    row[at + 1] = Grey;
                    row[at + 2] = Grey;
                    row[at + 3] = 255;
                }
            }

            rows.Add(row);
        }

        return (Encode(crop.Width, crop.Height, pixels.Wide, rows, pixels.ColourChunks), painted);
    }

    // A PNG of red, green, blue, and alpha rows, unfiltered, with the
    // frame's colour chunks, so that it shows as the frame does.
    internal static byte[] Encode(int width, int height, bool wide, IReadOnlyList<byte[]> rows, IReadOnlyDictionary<string, byte[]> colour)
    {
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = (byte)(wide ? 16 : 8);
        header[9] = 6;
        Chunk(png, "IHDR", header);
        foreach (var type in new[] { "cICP", "iCCP", "sRGB", "gAMA", "cHRM" })
        {
            if (colour.TryGetValue(type, out var body))
            {
                Chunk(png, type, body);
            }
        }

        using (var data = new MemoryStream())
        {
            using (var deflate = new ZLibStream(data, CompressionLevel.Optimal, leaveOpen: true))
            {
                foreach (var row in rows)
                {
                    deflate.WriteByte(0);
                    deflate.Write(row);
                }
            }

            Chunk(png, "IDAT", data.ToArray());
        }

        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] body)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
        png.Write(length);
        var name = Encoding.ASCII.GetBytes(type);
        png.Write(name);
        png.Write(body);
        var crc = new Crc32();
        crc.Append(name);
        crc.Append(body);
        Span<byte> sum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(sum, crc.GetCurrentHashAsUInt32());
        png.Write(sum);
    }
}

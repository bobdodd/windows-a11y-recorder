using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Recorder.Analysis.Summary;

/// <summary>
/// An image kept in the store: its key, what the key was taken from, and
/// its file, relative to the session folder with forward slashes, and
/// absolute.
/// </summary>
/// <param name="Basis">"pixels", or "bytes" for a picture the recorder could not read the pixels of.</param>
public sealed record StoredImage(string Key, string Basis, string Path, string AbsolutePath);

/// <summary>
/// The store of unique images for a recording: each picture kept once, in
/// one folder, named by its key, the first file with a key the one kept.
/// See recording-summary.md, "Unique images across the recording",
/// decisions 1 and 2.
/// </summary>
public sealed class ImageStore(string sessionFolder, string relativeFolder)
{
    public string RelativeFolder { get; } = relativeFolder;

    public string Folder { get; } = System.IO.Path.Combine([sessionFolder, .. relativeFolder.Split('/')]);

    /// <summary>Keeps a PNG in the store, unless a picture with its key is kept already, and gives the kept file.</summary>
    public async Task<StoredImage> KeepAsync(byte[] png, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(png);
        var (key, basis) = PngKey.Of(png);
        var name = key + ".png";
        var path = System.IO.Path.Combine(Folder, name);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Folder);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporary, png, cancellationToken).ConfigureAwait(false);
            try
            {
                File.Move(temporary, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another wrote a picture with the same key first; it is
                // the one kept.
                File.Delete(temporary);
            }
        }

        return new StoredImage(key, basis, RelativeFolder + "/" + name, path);
    }
}

/// <summary>
/// The key of a picture: the SHA-256 of its width, its height, its pixels
/// as red, green, blue, and alpha, and its colour profile, so that two
/// files of the same pixels written differently have the same key, and two
/// pictures that differ in one pixel or in their colour profile do not
/// (recording-summary.md, "Unique images across the recording", decision
/// 2). A PNG the recorder cannot read the pixels of, such as an interlaced
/// one, is keyed by its bytes instead, which can only match the same file.
/// </summary>
public static class PngKey
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    // The chunks that say how the samples are to be shown, in the order
    // they are added to the key.
    private static readonly string[] ColourChunks = ["iCCP", "sRGB", "gAMA", "cHRM", "cICP"];

    public static (string Key, string Basis) Of(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (TryPixels(png, hash))
        {
            return (Convert.ToHexStringLower(hash.GetHashAndReset()), "pixels");
        }

        using var bytes = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        bytes.AppendData(Encoding.ASCII.GetBytes("png-bytes-1\n"));
        bytes.AppendData(png);
        return (Convert.ToHexStringLower(bytes.GetHashAndReset()), "bytes");
    }

    // Adds the picture's size, pixels, and colour profile to the hash, or
    // says that the PNG cannot be read.
    private static bool TryPixels(byte[] png, IncrementalHash hash)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature))
        {
            return false;
        }

        int width = 0, height = 0, depth = 0, colour = -1, interlace = -1;
        byte[]? palette = null;
        byte[]? transparency = null;
        var chunks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var data = new MemoryStream();
        var at = 8;
        var ended = false;
        while (at + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            if (length < 0 || at + 12L + length > png.Length)
            {
                return false;
            }

            var body = png.AsSpan(at + 8, length);
            switch (type)
            {
                case "IHDR" when length == 13:
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    depth = body[8];
                    colour = body[9];
                    if (body[10] != 0 || body[11] != 0)
                    {
                        return false;
                    }

                    interlace = body[12];
                    break;
                case "PLTE":
                    palette = body.ToArray();
                    break;
                case "tRNS":
                    transparency = body.ToArray();
                    break;
                case "IDAT":
                    data.Write(body);
                    break;
                case "IEND":
                    ended = true;
                    break;
                default:
                    if (Array.IndexOf(ColourChunks, type) >= 0)
                    {
                        chunks[type] = body.ToArray();
                    }

                    break;
            }

            if (ended)
            {
                break;
            }

            at += 12 + length;
        }

        var channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        var depthAllowed = colour switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            _ => depth is 8 or 16,
        };
        if (!ended || width <= 0 || height <= 0 || channels == 0 || !depthAllowed || interlace != 0 || (colour == 3 && palette is null))
        {
            return false;
        }

        var rowBytes = ((long)width * channels * depth + 7) / 8;
        var pixelBytes = Math.Max(1, channels * depth / 8);
        if (rowBytes > int.MaxValue / 2)
        {
            return false;
        }

        var wide = depth == 16;
        hash.AppendData(Encoding.ASCII.GetBytes("png-pixels-1\n"));
        Span<byte> head = stackalloc byte[9];
        BinaryPrimitives.WriteInt32BigEndian(head, width);
        BinaryPrimitives.WriteInt32BigEndian(head[4..], height);
        head[8] = (byte)(wide ? 16 : 8);
        hash.AppendData(head);

        var previous = new byte[rowBytes];
        var current = new byte[rowBytes];
        var output = new byte[(long)width * (wide ? 8 : 4)];
        data.Position = 0;
        try
        {
            using var inflate = new ZLibStream(data, CompressionMode.Decompress);
            for (var row = 0; row < height; row++)
            {
                var filter = inflate.ReadByte();
                if (filter < 0 || filter > 4)
                {
                    return false;
                }

                inflate.ReadExactly(current);
                Unfilter(filter, current, previous, pixelBytes);
                if (!Expand(current, output, width, colour, depth, palette, transparency))
                {
                    return false;
                }

                hash.AppendData(output);
                (previous, current) = (current, previous);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            return false;
        }

        Span<byte> size = stackalloc byte[4];
        foreach (var type in ColourChunks)
        {
            var value = chunks.TryGetValue(type, out var body) ? Profile(type, body) : null;
            hash.AppendData(Encoding.ASCII.GetBytes(type));
            BinaryPrimitives.WriteInt32BigEndian(size, value?.Length ?? -1);
            hash.AppendData(size);
            if (value is not null)
            {
                hash.AppendData(value);
            }
        }

        return true;
    }

    // A colour chunk as it counts in the key: an ICC profile by the profile
    // itself, decompressed, not by its name or its compression.
    private static byte[] Profile(string type, byte[] body)
    {
        if (type != "iCCP")
        {
            return body;
        }

        var end = Array.IndexOf(body, (byte)0);
        if (end < 0 || end + 2 > body.Length)
        {
            return body;
        }

        try
        {
            using var compressed = new MemoryStream(body, end + 2, body.Length - end - 2);
            using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
            using var profile = new MemoryStream();
            inflate.CopyTo(profile);
            return profile.ToArray();
        }
        catch (InvalidDataException)
        {
            return body;
        }
    }

    private static void Unfilter(int filter, byte[] row, byte[] above, int bytesPerPixel)
    {
        for (var index = 0; index < row.Length; index++)
        {
            int left = index >= bytesPerPixel ? row[index - bytesPerPixel] : 0;
            int up = above[index];
            int corner = index >= bytesPerPixel ? above[index - bytesPerPixel] : 0;
            row[index] = filter switch
            {
                1 => (byte)(row[index] + left),
                2 => (byte)(row[index] + up),
                3 => (byte)(row[index] + ((left + up) >> 1)),
                4 => (byte)(row[index] + Paeth(left, up, corner)),
                _ => row[index],
            };
        }
    }

    private static int Paeth(int left, int up, int corner)
    {
        var estimate = left + up - corner;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toCorner = Math.Abs(estimate - corner);
        return toLeft <= toUp && toLeft <= toCorner ? left : toUp <= toCorner ? up : corner;
    }

    // One row as red, green, blue, and alpha: 8 bits each, or 16 bits each,
    // big-endian, for a 16-bit PNG.
    private static bool Expand(byte[] row, byte[] output, int width, int colour, int depth, byte[]? palette, byte[]? transparency)
    {
        var wide = depth == 16;
        for (var x = 0; x < width; x++)
        {
            int red, green, blue, alpha;
            var top = wide ? 65535 : 255;
            switch (colour)
            {
                case 0:
                {
                    var gray = Sample(row, x, depth);
                    var scaled = depth < 8 ? gray * 255 / ((1 << depth) - 1) : gray;
                    red = green = blue = scaled;
                    alpha = transparency is { Length: >= 2 } && gray == BinaryPrimitives.ReadUInt16BigEndian(transparency) ? 0 : top;
                    break;
                }

                case 2:
                    red = Sample(row, x * 3, depth);
                    green = Sample(row, x * 3 + 1, depth);
                    blue = Sample(row, x * 3 + 2, depth);
                    alpha = transparency is { Length: >= 6 } &&
                            red == BinaryPrimitives.ReadUInt16BigEndian(transparency) &&
                            green == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) &&
                            blue == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4)) ? 0 : top;
                    break;
                case 3:
                {
                    var entry = Sample(row, x, depth);
                    if (entry * 3 + 2 >= palette!.Length)
                    {
                        return false;
                    }

                    red = palette[entry * 3];
                    green = palette[entry * 3 + 1];
                    blue = palette[entry * 3 + 2];
                    alpha = transparency is not null && entry < transparency.Length ? transparency[entry] : 255;
                    break;
                }

                case 4:
                    red = green = blue = Sample(row, x * 2, depth);
                    alpha = Sample(row, x * 2 + 1, depth);
                    break;
                default:
                    red = Sample(row, x * 4, depth);
                    green = Sample(row, x * 4 + 1, depth);
                    blue = Sample(row, x * 4 + 2, depth);
                    alpha = Sample(row, x * 4 + 3, depth);
                    break;
            }

            if (wide)
            {
                var at = output.AsSpan(x * 8);
                BinaryPrimitives.WriteUInt16BigEndian(at, (ushort)red);
                BinaryPrimitives.WriteUInt16BigEndian(at[2..], (ushort)green);
                BinaryPrimitives.WriteUInt16BigEndian(at[4..], (ushort)blue);
                BinaryPrimitives.WriteUInt16BigEndian(at[6..], (ushort)alpha);
            }
            else
            {
                output[x * 4] = (byte)red;
                output[x * 4 + 1] = (byte)green;
                output[x * 4 + 2] = (byte)blue;
                output[x * 4 + 3] = (byte)alpha;
            }
        }

        return true;
    }

    // The sample at an index along a row, at the PNG's bit depth.
    private static int Sample(byte[] row, int index, int depth) => depth switch
    {
        16 => BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(index * 2)),
        8 => row[index],
        _ => (row[index * depth / 8] >> (8 - depth - index * depth % 8)) & ((1 << depth) - 1),
    };
}

/// <summary>
/// One tile of one moment in a task's index of images: the visit, the
/// moment and its frame's time, the tile's number, place, and size, and
/// its image's key and file in the store (recording-summary.md, "Unique
/// images across the recording", decision 3).
/// </summary>
public sealed record ImageIndexEntry(
    int Visit,
    int Moment,
    long FrameTime,
    string FrameClock,
    int Tile,
    double Top,
    double Height,
    double Width,
    int PixelWidth,
    int PixelHeight,
    string Key,
    string Basis,
    string File);

/// <summary>A task's index of the images of its tiles, written again whole after each moment.</summary>
public static class ImageIndex
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task WriteAsync(string path, IReadOnlyList<ImageIndexEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entries, Options), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    public static IReadOnlyList<ImageIndexEntry> Read(string path) =>
        JsonSerializer.Deserialize<List<ImageIndexEntry>>(File.ReadAllText(path), Options) ?? [];

    /// <summary>A frame's time as the index gives it beside the nanoseconds.</summary>
    public static string Clock(long time) => SummaryCollator.Time(time);
}

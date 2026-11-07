using System.Buffers.Binary;

namespace Recorder.Export;

/// <summary>
/// Marks an MP4's sound tracks as alternatives of one another, with only
/// the first, the mixed track, enabled. Windows' MP4 writer enables every
/// track, and a player that plays every enabled track, as QuickTime does,
/// would play the mixed track and each sound's own track at once. FFmpeg
/// writes the same marks: every sound track in alternate group 1 and one of
/// them enabled. Each track header is changed in place; nothing moves. See
/// docs/architecture/mp4-export.md, "Tracks".
/// </summary>
internal static class Mp4TrackGroups
{
    private const uint Enabled = 0x1;
    private const short SoundGroup = 1;

    /// <summary>
    /// Marks the file's sound tracks. Returns the number of sound tracks
    /// found; a file without a movie box is left as it is.
    /// </summary>
    public static int Apply(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var (start, length) = FindTopLevel(file, "moov"u8);
        if (length <= 0 || length > int.MaxValue)
        {
            return 0;
        }

        var movie = new byte[length];
        file.Position = start;
        file.ReadExactly(movie);
        var sounds = Mark(movie);
        if (sounds > 0)
        {
            file.Position = start;
            file.Write(movie);
        }

        return sounds;
    }

    /// <summary>Marks the sound tracks of a movie box held in memory.</summary>
    public static int Mark(Span<byte> movie)
    {
        var sounds = 0;
        foreach (var (trackStart, trackLength) in Children(movie, HeaderOf(movie), "trak"u8))
        {
            var track = movie.Slice(trackStart, trackLength);
            if (!IsSound(track))
            {
                continue;
            }

            var header = Children(track, 8, "tkhd"u8).FirstOrDefault();
            if (header.Length < 8 + 48)
            {
                continue;
            }

            var box = track.Slice(header.Start, header.Length);
            var version = box[8];
            var flags = BinaryPrimitives.ReadUInt32BigEndian(box[8..]) & 0x00FFFFFF;
            flags = sounds == 0 ? flags | Enabled : flags & ~Enabled;
            BinaryPrimitives.WriteUInt32BigEndian(box[8..], ((uint)version << 24) | flags);
            // The alternate group follows the layer, 34 bytes after the
            // version in a version 0 header and 46 in a version 1.
            var group = 8 + (version == 1 ? 46 : 34);
            BinaryPrimitives.WriteInt16BigEndian(box[group..], SoundGroup);
            sounds++;
        }

        return sounds;
    }

    // A track is sound when its media's handler is "soun".
    private static bool IsSound(ReadOnlySpan<byte> track)
    {
        foreach (var (mediaStart, mediaLength) in Children(track, 8, "mdia"u8))
        {
            var media = track.Slice(mediaStart, mediaLength);
            foreach (var (handlerStart, handlerLength) in Children(media, 8, "hdlr"u8))
            {
                // Size, type, version and flags, pre-defined, then the type.
                if (handlerLength >= 20 && media.Slice(handlerStart + 16, 4).SequenceEqual("soun"u8))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The bytes of a box's own header: 16 with a 64-bit size, else 8.
    private static int HeaderOf(ReadOnlySpan<byte> box) =>
        BinaryPrimitives.ReadUInt32BigEndian(box) == 1 ? 16 : 8;

    // The boxes of a type directly within a box, from the offset its
    // children start at.
    private static List<(int Start, int Length)> Children(ReadOnlySpan<byte> parent, int offset, ReadOnlySpan<byte> type)
    {
        var found = new List<(int, int)>();
        var at = offset;
        while (at + 8 <= parent.Length)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(parent[at..]);
            if (size == 1)
            {
                if (at + 16 > parent.Length)
                {
                    break;
                }

                size = (long)BinaryPrimitives.ReadUInt64BigEndian(parent[(at + 8)..]);
            }
            else if (size == 0)
            {
                size = parent.Length - at;
            }

            if (size < 8 || at + size > parent.Length)
            {
                break;
            }

            if (parent.Slice(at + 4, 4).SequenceEqual(type))
            {
                found.Add((at, (int)size));
            }

            at += (int)size;
        }

        return found;
    }

    // Where a top-level box of a type is in the file, and its length.
    private static (long Start, long Length) FindTopLevel(FileStream file, ReadOnlySpan<byte> type)
    {
        Span<byte> header = stackalloc byte[16];
        var at = 0L;
        while (at + 8 <= file.Length)
        {
            file.Position = at;
            file.ReadExactly(header[..8]);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (size == 1)
            {
                file.ReadExactly(header[8..16]);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
            }
            else if (size == 0)
            {
                size = file.Length - at;
            }

            if (size < 8 || at + size > file.Length)
            {
                break;
            }

            if (header.Slice(4, 4).SequenceEqual(type))
            {
                return (at, size);
            }

            at += size;
        }

        return (0, 0);
    }
}

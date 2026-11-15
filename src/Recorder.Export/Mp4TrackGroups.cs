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
        var (start, length) = Mp4Boxes.FindTopLevel(file, "moov"u8);
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
        foreach (var (trackStart, trackLength) in Mp4Boxes.Children(movie, Mp4Boxes.HeaderOf(movie), "trak"u8))
        {
            var track = movie.Slice(trackStart, trackLength);
            if (!IsSound(track))
            {
                continue;
            }

            var header = Mp4Boxes.Children(track, 8, "tkhd"u8).FirstOrDefault();
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
        foreach (var (mediaStart, mediaLength) in Mp4Boxes.Children(track, 8, "mdia"u8))
        {
            var media = track.Slice(mediaStart, mediaLength);
            foreach (var (handlerStart, handlerLength) in Mp4Boxes.Children(media, 8, "hdlr"u8))
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
}

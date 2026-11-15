using System.Buffers.Binary;
using System.Text;

namespace Recorder.Export;

/// <summary>
/// Adds a chapter list to an MP4 written by Windows, in the two forms
/// FFmpeg writes and VLC reads: a QuickTime chapter track, a disabled
/// tx3g text track with one sample for each chapter, referred to from the
/// video track by a 'chap' track reference, and a Nero 'chpl' box in the
/// movie's user data. VLC 3 reads the 'chpl' box first and the chapter
/// track only where there is none; VLC 4 reads the chapter track first.
/// The 'chpl' box holds at most 255 chapters, so it is written only where
/// there are no more, and VLC 3 then reads every chapter from the track.
/// The chapters' text is written after the media already in the file, and
/// a new movie box after it, so the sound's and the picture's chunk
/// offsets do not change: where the movie box is the file's last box it
/// is replaced, and otherwise it is renamed 'free' and left where it is.
/// See docs/architecture/mp4-export.md, "Evidence numbers".
/// </summary>
internal static class Mp4Chapters
{
    /// <summary>The most chapters the 'chpl' box holds.</summary>
    public const int MaximumNeroChapters = 255;

    private const uint TextTimescale = 1000;

    // FFmpeg's text sample entry for a chapter track: display flags,
    // justification, background colour, text box, style record, and a
    // font table of one font with no name.
    private static readonly byte[] TextSampleEntry =
    [
        0x00, 0x00, 0x00, 0x01,
        0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x01,
        0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x0D,
        (byte)'f', (byte)'t', (byte)'a', (byte)'b',
        0x00, 0x01,
        0x00, 0x01,
        0x00
    ];

    // The text encoding box FFmpeg ends each chapter's sample with: UTF-8.
    private static readonly byte[] EncodingBox = [0x00, 0x00, 0x00, 0x0C, (byte)'e', (byte)'n', (byte)'c', (byte)'d', 0x00, 0x00, 0x01, 0x00];

    /// <summary>
    /// Adds the chapters to the file. Returns false, leaving the file as
    /// it is, where there are none or the file has no movie box or video
    /// track.
    /// </summary>
    public static bool Apply(string path, IReadOnlyList<Mp4Chapter> chapters)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        if (chapters.Count == 0)
        {
            return false;
        }

        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var (start, length) = Mp4Boxes.FindTopLevel(file, "moov"u8);
        if (length <= 0 || length > int.MaxValue)
        {
            return false;
        }

        var movie = new byte[length];
        file.Position = start;
        file.ReadExactly(movie);
        var last = start + length == file.Length;
        var mediaStart = last ? start : file.Length;
        var (samples, durations) = Samples(chapters, MovieMilliseconds(movie));
        var sampleData = samples.SelectMany(sample => sample).ToArray();
        var updated = Rebuild(movie, chapters, samples, durations, mediaStart + 8);
        if (updated is null)
        {
            return false;
        }

        if (last)
        {
            file.SetLength(start);
        }
        else
        {
            file.Position = start + 4;
            file.Write("free"u8);
        }

        file.Position = mediaStart;
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)(8 + sampleData.Length)));
        "mdat"u8.CopyTo(header.AsSpan(4));
        file.Write(header);
        file.Write(sampleData);
        file.Write(updated);
        file.SetLength(file.Position);
        return true;
    }

    /// <summary>
    /// The movie box with the chapter track, the video track's reference
    /// to it, and, for up to 255 chapters, the 'chpl' box added; null
    /// where it has no movie header or video track. The text samples are
    /// at <paramref name="dataOffset"/> in the file, one after another.
    /// </summary>
    internal static byte[]? Rebuild(byte[] movie, IReadOnlyList<Mp4Chapter> chapters, IReadOnlyList<byte[]> samples, IReadOnlyList<uint> durations, long dataOffset)
    {
        var children = Mp4Boxes.All(movie, Mp4Boxes.HeaderOf(movie));
        var header = children.FirstOrDefault(child => child.Type == "mvhd");
        if (header.Length < 32)
        {
            return null;
        }

        var mvhd = movie.AsSpan(header.Start, header.Length).ToArray();
        var trackId = BinaryPrimitives.ReadUInt32BigEndian(mvhd.AsSpan(mvhd.Length - 4));
        if (trackId == 0 || trackId == uint.MaxValue)
        {
            trackId = 1 + children.Where(child => child.Type == "trak")
                .Select(child => TrackId(movie.AsSpan(child.Start, child.Length))).DefaultIfEmpty(0u).Max();
        }

        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(mvhd.Length - 4), trackId + 1);
        var (movieTimescale, _) = TimescaleAndDuration(mvhd);
        var video = children.FirstOrDefault(child => child.Type == "trak" && Handler(movie.AsSpan(child.Start, child.Length)) == "vide");
        if (video.Length == 0)
        {
            return null;
        }

        var body = new MemoryStream();
        byte[]? userData = null;
        foreach (var child in children)
        {
            var bytes = movie.AsSpan(child.Start, child.Length);
            switch (child.Type)
            {
                case "mvhd":
                    body.Write(mvhd);
                    break;
                case "udta":
                    userData = bytes.ToArray();
                    break;
                case "trak" when child.Start == video.Start:
                    body.Write(WithChapterReference(bytes, trackId));
                    break;
                default:
                    body.Write(bytes);
                    break;
            }
        }

        var totalMilliseconds = durations.Aggregate(0L, (sum, duration) => sum + duration);
        body.Write(TextTrack(trackId, samples, durations, dataOffset, movieTimescale, totalMilliseconds));
        if (chapters.Count <= MaximumNeroChapters || userData is not null)
        {
            body.Write(UserData(userData, chapters.Count <= MaximumNeroChapters ? Nero(chapters) : null));
        }

        return Box("moov", body.ToArray());
    }

    /// <summary>
    /// The chapter track's samples and their lengths in milliseconds: an
    /// empty one from the start where the first chapter is later, which
    /// VLC does not list, then one for each chapter until the next, the
    /// last until the movie's end.
    /// </summary>
    internal static (IReadOnlyList<byte[]> Samples, IReadOnlyList<uint> Durations) Samples(IReadOnlyList<Mp4Chapter> chapters, long movieMilliseconds)
    {
        var samples = new List<byte[]>();
        var starts = new List<long>();
        if (chapters[0].StartMilliseconds > 0)
        {
            samples.Add([0, 0]);
            starts.Add(0);
        }

        foreach (var chapter in chapters)
        {
            var title = Encoding.UTF8.GetBytes(chapter.Title);
            var sample = new byte[2 + title.Length + EncodingBox.Length];
            BinaryPrimitives.WriteUInt16BigEndian(sample, (ushort)title.Length);
            title.CopyTo(sample, 2);
            EncodingBox.CopyTo(sample, 2 + title.Length);
            samples.Add(sample);
            starts.Add(chapter.StartMilliseconds);
        }

        var durations = new List<uint>();
        for (var index = 0; index < starts.Count; index++)
        {
            var end = index + 1 < starts.Count ? starts[index + 1] : Math.Max(movieMilliseconds, starts[index] + 1);
            durations.Add((uint)Math.Clamp(end - starts[index], 1, uint.MaxValue));
        }

        return (samples, durations);
    }

    // The movie's length in milliseconds, from its header.
    private static long MovieMilliseconds(byte[] movie)
    {
        var header = Mp4Boxes.Children(movie, Mp4Boxes.HeaderOf(movie), "mvhd"u8).FirstOrDefault();
        if (header.Length < 32)
        {
            return 0;
        }

        var (timescale, duration) = TimescaleAndDuration(movie.AsSpan(header.Start, header.Length));
        return timescale == 0 ? 0 : (long)((decimal)duration * 1000m / timescale);
    }

    // A movie or media header's timescale and duration, version 0 or 1.
    private static (uint Timescale, ulong Duration) TimescaleAndDuration(ReadOnlySpan<byte> box) =>
        box[8] == 1
            ? (BinaryPrimitives.ReadUInt32BigEndian(box[28..]), BinaryPrimitives.ReadUInt64BigEndian(box[32..]))
            : (BinaryPrimitives.ReadUInt32BigEndian(box[20..]), BinaryPrimitives.ReadUInt32BigEndian(box[24..]));

    // A track's ID, from its header.
    private static uint TrackId(ReadOnlySpan<byte> track)
    {
        var header = Mp4Boxes.Children(track, 8, "tkhd"u8).FirstOrDefault();
        if (header.Length < 32)
        {
            return 0;
        }

        // After the version, flags and creation and modification times.
        return BinaryPrimitives.ReadUInt32BigEndian(track[(header.Start + (track[header.Start + 8] == 1 ? 28 : 20))..]);
    }

    // A track's media handler, such as "vide".
    private static string? Handler(ReadOnlySpan<byte> track)
    {
        foreach (var (mediaStart, mediaLength) in Mp4Boxes.Children(track, 8, "mdia"u8))
        {
            var media = track.Slice(mediaStart, mediaLength);
            foreach (var (handlerStart, handlerLength) in Mp4Boxes.Children(media, 8, "hdlr"u8))
            {
                if (handlerLength >= 20)
                {
                    return Encoding.Latin1.GetString(media.Slice(handlerStart + 16, 4));
                }
            }
        }

        return null;
    }

    // The video track with a 'chap' reference to the chapter track, after
    // its header; an existing chapter reference is replaced.
    private static byte[] WithChapterReference(ReadOnlySpan<byte> track, uint chapterTrack)
    {
        var chap = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(chap, chapterTrack);
        var reference = Box("chap", chap);
        var body = new MemoryStream();
        var children = Mp4Boxes.All(track, Mp4Boxes.HeaderOf(track));
        var hasReferences = children.Any(child => child.Type == "tref");
        var added = false;
        foreach (var child in children)
        {
            var bytes = track.Slice(child.Start, child.Length);
            if (child.Type == "tref")
            {
                var references = new MemoryStream();
                foreach (var kept in Mp4Boxes.All(bytes, 8).Where(kept => kept.Type != "chap"))
                {
                    references.Write(bytes.Slice(kept.Start, kept.Length));
                }

                references.Write(reference);
                body.Write(Box("tref", references.ToArray()));
                added = true;
                continue;
            }

            body.Write(bytes);
            if (child.Type == "tkhd" && !hasReferences)
            {
                // No reference box yet: one follows the header.
                body.Write(Box("tref", reference));
                added = true;
            }
        }

        if (!added)
        {
            body.Write(Box("tref", reference));
        }

        return Box("trak", body.ToArray());
    }

    // The chapter track: disabled, in the movie, with its text samples in
    // one chunk at the offset given.
    private static byte[] TextTrack(uint trackId, IReadOnlyList<byte[]> samples, IReadOnlyList<uint> durations, long dataOffset, uint movieTimescale, long totalMilliseconds)
    {
        var tkhd = new byte[84];
        BinaryPrimitives.WriteUInt32BigEndian(tkhd, 0x00000002);
        BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(12), trackId);
        BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(20), (uint)Math.Min(uint.MaxValue, (decimal)totalMilliseconds * movieTimescale / 1000m));
        WriteMatrix(tkhd.AsSpan(40));

        var mdhd = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(mdhd.AsSpan(12), TextTimescale);
        BinaryPrimitives.WriteUInt32BigEndian(mdhd.AsSpan(16), (uint)Math.Min(uint.MaxValue, totalMilliseconds));
        // "und", each letter less 0x60 in five bits.
        BinaryPrimitives.WriteUInt16BigEndian(mdhd.AsSpan(20), 0x55C4);

        var hdlr = new MemoryStream();
        hdlr.Write(new byte[8]);
        hdlr.Write("text"u8);
        hdlr.Write(new byte[12]);
        hdlr.Write("Chapters\0"u8);

        var url = Box("url ", [0, 0, 0, 1]);
        var dref = Box("dref", [.. new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, .. url]);
        var dinf = Box("dinf", dref);

        var entry = new MemoryStream();
        entry.Write(new byte[6]);
        entry.Write([0, 1]);
        entry.Write(TextSampleEntry);
        var stsd = Box("stsd", [0, 0, 0, 0, 0, 0, 0, 1, .. Box("tx3g", entry.ToArray())]);

        var stts = new MemoryStream();
        stts.Write(new byte[4]);
        WriteUInt32(stts, (uint)samples.Count);
        for (var index = 0; index < samples.Count; index++)
        {
            WriteUInt32(stts, 1);
            WriteUInt32(stts, durations[index]);
        }

        var stsz = new MemoryStream();
        stsz.Write(new byte[8]);
        WriteUInt32(stsz, (uint)samples.Count);
        foreach (var sample in samples)
        {
            WriteUInt32(stsz, (uint)sample.Length);
        }

        var stsc = new MemoryStream();
        stsc.Write(new byte[4]);
        WriteUInt32(stsc, 1);
        WriteUInt32(stsc, 1);
        WriteUInt32(stsc, (uint)samples.Count);
        WriteUInt32(stsc, 1);

        var offsets = new MemoryStream();
        offsets.Write(new byte[4]);
        WriteUInt32(offsets, 1);
        var wide = dataOffset > uint.MaxValue;
        if (wide)
        {
            var offset = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(offset, (ulong)dataOffset);
            offsets.Write(offset);
        }
        else
        {
            WriteUInt32(offsets, (uint)dataOffset);
        }

        var stbl = Box("stbl",
        [
            .. stsd,
            .. Box("stts", stts.ToArray()),
            .. Box("stsz", stsz.ToArray()),
            .. Box("stsc", stsc.ToArray()),
            .. Box(wide ? "co64" : "stco", offsets.ToArray())
        ]);
        var minf = Box("minf", [.. Box("nmhd", new byte[4]), .. dinf, .. stbl]);
        var mdia = Box("mdia", [.. Box("mdhd", mdhd), .. Box("hdlr", hdlr.ToArray()), .. minf]);
        return Box("trak", [.. Box("tkhd", tkhd), .. mdia]);
    }

    // The movie's user data with the 'chpl' box, any earlier one removed.
    private static byte[] UserData(byte[]? existing, byte[]? nero)
    {
        var body = new MemoryStream();
        if (existing is not null)
        {
            foreach (var child in Mp4Boxes.All(existing, Mp4Boxes.HeaderOf(existing)).Where(child => child.Type != "chpl"))
            {
                body.Write(existing.AsSpan(child.Start, child.Length));
            }
        }

        if (nero is not null)
        {
            body.Write(nero);
        }

        return Box("udta", body.ToArray());
    }

    // The Nero chapter list as FFmpeg writes it: version 1, a reserved
    // word, the count, then each chapter's start in 100 ns units and its
    // title, at most 255 bytes.
    internal static byte[] Nero(IReadOnlyList<Mp4Chapter> chapters)
    {
        var body = new MemoryStream();
        WriteUInt32(body, 0x01000000);
        WriteUInt32(body, 0);
        body.WriteByte((byte)Math.Min(chapters.Count, MaximumNeroChapters));
        foreach (var chapter in chapters.Take(MaximumNeroChapters))
        {
            var start = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(start, (ulong)chapter.StartMilliseconds * 10_000UL);
            body.Write(start);
            var title = Encoding.UTF8.GetBytes(ExportAnnotations.Cut(chapter.Title, 255));
            body.WriteByte((byte)title.Length);
            body.Write(title);
        }

        return Box("chpl", body.ToArray());
    }

    private static void WriteMatrix(Span<byte> matrix)
    {
        BinaryPrimitives.WriteUInt32BigEndian(matrix, 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(matrix[16..], 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(matrix[32..], 0x40000000);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static byte[] Box(string type, ReadOnlySpan<byte> body)
    {
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, checked((uint)box.Length));
        Encoding.Latin1.GetBytes(type, box.AsSpan(4, 4));
        body.CopyTo(box.AsSpan(8));
        return box;
    }
}

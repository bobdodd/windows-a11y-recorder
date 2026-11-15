using System.Buffers.Binary;
using System.Text;

namespace Recorder.Export;

/// <summary>
/// The MP4's title and comment, which tie the file to its recording and to
/// the report its evidence numbers came from (owner, 2026-11-15): the title
/// is the recording as lists and reports show it, its name and then its
/// session key, or its session key alone; the comment gives the name, the
/// session key and the report. Neither holds a path, as an MP4 may be given
/// to someone else. See docs/architecture/mp4-export.md, "Title and comment".
/// </summary>
public sealed record Mp4Metadata(string Title, string Comment)
{
    /// <summary>
    /// The title and comment of a recording's export. <paramref name="name"/>
    /// is null for a recording with no name, and <paramref name="nameKnown"/>
    /// false where the catalog could not be read; <paramref name="report"/>
    /// is the report the numbers came from, such as "Android report,
    /// finished 2026-11-15 15:30 UTC", or null for none.
    /// </summary>
    public static Mp4Metadata Of(string sessionKey, string? name, bool nameKnown, string? report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        var named = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var title = named is null ? sessionKey : $"{named} ({sessionKey})";
        var shownName = named ?? (nameKnown ? "none" : "not known");
        var numbers = report is null ? "Evidence numbers: none." : $"Evidence numbers from: {report}.";
        return new Mp4Metadata(title, $"Recording name: {shownName}. Session key: {sessionKey}. {numbers}");
    }
}

/// <summary>
/// Writes an <see cref="Mp4Metadata"/> into an MP4 as FFmpeg does and VLC
/// reads it: '©nam' and '©cmt' items, each with a UTF-8 'data' box, in an
/// 'ilst' within a 'meta' box with an 'mdir' handler, in the movie's user
/// data. Other user data, and other items, are kept. As for the chapters,
/// the new movie box is written at the end of the file, replacing the old
/// one where it was the last box and otherwise renaming it 'free', so no
/// chunk offset changes.
/// </summary>
internal static class Mp4MetadataWriter
{
    private static readonly string TitleItem = "\u00A9nam";
    private static readonly string CommentItem = "\u00A9cmt";

    /// <summary>Writes the title and comment; false, leaving the file as it is, where it has no movie box.</summary>
    public static bool Apply(string path, Mp4Metadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var (start, length) = Mp4Boxes.FindTopLevel(file, "moov"u8);
        if (length <= 0 || length > int.MaxValue)
        {
            return false;
        }

        var movie = new byte[length];
        file.Position = start;
        file.ReadExactly(movie);
        var updated = Rebuild(movie, metadata);
        if (start + length == file.Length)
        {
            file.SetLength(start);
            file.Position = start;
        }
        else
        {
            file.Position = start + 4;
            file.Write("free"u8);
            file.Position = file.Length;
        }

        file.Write(updated);
        file.SetLength(file.Position);
        return true;
    }

    /// <summary>The movie box with the title and comment in its user data.</summary>
    internal static byte[] Rebuild(byte[] movie, Mp4Metadata metadata)
    {
        var body = new MemoryStream();
        byte[]? userData = null;
        foreach (var child in Mp4Boxes.All(movie, Mp4Boxes.HeaderOf(movie)))
        {
            if (child.Type == "udta")
            {
                userData = movie.AsSpan(child.Start, child.Length).ToArray();
                continue;
            }

            body.Write(movie.AsSpan(child.Start, child.Length));
        }

        body.Write(UserData(userData, metadata));
        return Box("moov", body.ToArray());
    }

    // The user data with a new 'meta' box: the earlier one's items, if it
    // was an 'mdir' list, less any title and comment, then the new ones.
    private static byte[] UserData(byte[]? existing, Mp4Metadata metadata)
    {
        var body = new MemoryStream();
        var items = new MemoryStream();
        if (existing is not null)
        {
            foreach (var child in Mp4Boxes.All(existing, Mp4Boxes.HeaderOf(existing)))
            {
                var bytes = existing.AsSpan(child.Start, child.Length);
                if (child.Type != "meta")
                {
                    body.Write(bytes);
                    continue;
                }

                // A full box: its children follow its version and flags.
                foreach (var list in Mp4Boxes.All(bytes, Mp4Boxes.HeaderOf(bytes) + 4).Where(box => box.Type == "ilst"))
                {
                    var listBytes = bytes.Slice(list.Start, list.Length);
                    foreach (var item in Mp4Boxes.All(listBytes, Mp4Boxes.HeaderOf(listBytes))
                        .Where(item => item.Type != TitleItem && item.Type != CommentItem))
                    {
                        items.Write(listBytes.Slice(item.Start, item.Length));
                    }
                }
            }
        }

        items.Write(Item(TitleItem, metadata.Title));
        items.Write(Item(CommentItem, metadata.Comment));

        // FFmpeg's handler for an iTunes list: no pre-defined value, 'mdir',
        // "appl" and zeros reserved, and an empty name.
        var handler = new MemoryStream();
        handler.Write(new byte[8]);
        handler.Write("mdir"u8);
        handler.Write("appl"u8);
        handler.Write(new byte[9]);
        var meta = Box("meta", [0, 0, 0, 0, .. Box("hdlr", handler.ToArray()), .. Box("ilst", items.ToArray())]);
        body.Write(meta);
        return Box("udta", body.ToArray());
    }

    // An item of the list: a 'data' box of type 1, UTF-8 text, with no
    // locale.
    private static byte[] Item(string type, string text) =>
        Box(type, Box("data", [0, 0, 0, 1, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes(text)]));

    private static byte[] Box(string type, ReadOnlySpan<byte> body)
    {
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, checked((uint)box.Length));
        Encoding.Latin1.GetBytes(type, box.AsSpan(4, 4));
        body.CopyTo(box.AsSpan(8));
        return box;
    }
}

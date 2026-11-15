using System.Buffers.Binary;

namespace Recorder.Export;

/// <summary>Finding the boxes of an MP4 file and of a box held in memory.</summary>
internal static class Mp4Boxes
{
    // The bytes of a box's own header: 16 with a 64-bit size, else 8.
    internal static int HeaderOf(ReadOnlySpan<byte> box) =>
        BinaryPrimitives.ReadUInt32BigEndian(box) == 1 ? 16 : 8;

    // Every box directly within a box, from the offset its children start
    // at, with its type.
    internal static List<(int Start, int Length, string Type)> All(ReadOnlySpan<byte> parent, int offset)
    {
        var found = new List<(int, int, string)>();
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

            found.Add((at, (int)size, System.Text.Encoding.Latin1.GetString(parent.Slice(at + 4, 4))));
            at += (int)size;
        }

        return found;
    }

    // The boxes of a type directly within a box, from the offset its
    // children start at.
    internal static List<(int Start, int Length)> Children(ReadOnlySpan<byte> parent, int offset, ReadOnlySpan<byte> type)
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
    internal static (long Start, long Length) FindTopLevel(FileStream file, ReadOnlySpan<byte> type)
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

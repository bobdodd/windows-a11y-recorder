using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Recorder.Collectors.Android;

/// <summary>Read-only, bounded Android binary XML. No XML expansion or recovered files.</summary>
internal static class AndroidCompiledXml
{
    internal sealed record Element(string Name, int Ordinal, string? Id, IReadOnlyList<Attribute> Attributes);
    internal sealed record Attribute(string? Namespace, string Name, string Value, bool IsResourceReference);

    public static IReadOnlyList<Element> Read(ReadOnlySpan<byte> input, Func<string?, string, bool> include)
    {
        if (input.Length < 8 || U16(input, 0) != 3 || U32(input, 4) != input.Length)
        {
            throw Invalid();
        }

        var elements = new List<Element>();
        ReadOnlySpan<byte> strings = default;
        var start = (int)U16(input, 2);
        if (start < 8 || start > input.Length)
        {
            throw Invalid();
        }
        for (var at = start; at < input.Length;)
        {
            var header = U16(input, at + 2);
            var size = U32(input, at + 4);
            if (header < 8 || size < header || size > input.Length - at)
            {
                throw Invalid();
            }
            var chunk = input.Slice(at, (int)size);
            switch (U16(chunk, 0))
            {
                case 1:
                    if (!strings.IsEmpty || header < 28)
                    {
                        throw Invalid();
                    }
                    strings = chunk;
                    break;
                case 0x0102:
                    if (strings.IsEmpty || header < 16 || chunk.Length - header < 20 || elements.Count >= 8192)
                    {
                        throw Invalid();
                    }
                    var data = chunk[header..];
                    var name = StringAt(strings, U32(data, 4)) ?? throw Invalid();
                    var offset = U16(data, 8);
                    var width = U16(data, 10);
                    var count = U16(data, 12);
                    if (offset < 20 || width < 20 || (long)offset + (long)width * count > data.Length)
                    {
                        throw Invalid();
                    }
                    string? id = null;
                    var attrs = new List<Attribute>();
                    for (var index = 0; index < count; index++)
                    {
                        var attr = data.Slice(offset + index * width, width);
                        var ns = StringAt(strings, U32(attr, 0));
                        var key = StringAt(strings, U32(attr, 4)) ?? throw Invalid();
                        var isId = key == "id" && ns == "http://schemas.android.com/apk/res/android";
                        if (!isId && !include(ns, key))
                        {
                            continue;
                        }
                        var value = Value(attr, strings);
                        if (isId)
                        {
                            id = value;
                        }
                        if (value is not null && !isId)
                        {
                            attrs.Add(new(ns, key, value, attr[15] is 1 or 7));
                        }
                    }
                    elements.Add(new(name, elements.Count + 1, id, attrs));
                    break;
            }
            at += (int)size;
        }
        return elements.Count > 0 ? elements : throw Invalid();
    }

    private static string? Value(ReadOnlySpan<byte> attr, ReadOnlySpan<byte> strings)
    {
        if (U16(attr, 12) < 8)
        {
            throw Invalid();
        }
        return DescribeValue(attr[15], U32(attr, 16), strings);
    }

    internal static string? DescribeValue(byte type, uint value, ReadOnlySpan<byte> strings)
    {
        return type switch
        {
            0 => null,
            1 or 7 => $"@0x{value:X8}",
            2 or 8 => $"?0x{value:X8}",
            3 => StringAt(strings, value),
            4 => BitConverter.UInt32BitsToSingle(value).ToString("G", CultureInfo.InvariantCulture),
            5 => Dimension(value),
            16 => unchecked((int)value).ToString(CultureInfo.InvariantCulture),
            18 => value == 0 ? "false" : "true",
            28 or 29 or 30 or 31 => $"#{value:X8}",
            _ => $"0x{value:X8}"
        };
    }

    private static string Dimension(uint value)
    {
        var divisor = (value >> 4 & 3) switch { 0 => 1, 1 => 128, 2 => 32768, _ => 8388608 };
        var amount = (unchecked((int)value) >> 8) / (double)divisor;
        var unit = (value & 15) switch { 0 => "px", 1 => "dp", 2 => "sp", 3 => "pt", 4 => "in", 5 => "mm", _ => " unknown unit" };
        return amount.ToString("G", CultureInfo.InvariantCulture) + unit;
    }

    internal static string? StringAt(ReadOnlySpan<byte> pool, uint index)
    {
        if (index == uint.MaxValue)
        {
            return null;
        }
        var table = U16(pool, 2);
        var count = U32(pool, 8);
        var beginning = U32(pool, 20);
        var styles = U32(pool, 24);
        var end = styles == 0 ? pool.Length : (long)styles;
        if (index >= count || table + 4L * count > beginning || end > pool.Length || beginning >= end)
        {
            throw Invalid();
        }
        var at = (long)beginning + U32(pool, (int)(table + 4L * index));
        if (at >= end)
        {
            throw Invalid();
        }
        var text = pool[(int)at..(int)end];
        var position = 0;
        if ((U32(pool, 16) & 0x100) != 0)
        {
            _ = ReadLength(text, ref position, wide: false);
            var bytes = ReadLength(text, ref position, wide: false);
            if (bytes >= text.Length - position || text[position + bytes] != 0)
            {
                throw Invalid();
            }
            return Encoding.UTF8.GetString(text.Slice(position, bytes));
        }

        var length = ReadLength(text, ref position, wide: true);
        if (2L * length + 2 > text.Length - position || U16(text, position + length * 2) != 0)
        {
            throw Invalid();
        }
        return Encoding.Unicode.GetString(text.Slice(position, length * 2));
    }

    private static int ReadLength(ReadOnlySpan<byte> text, ref int at, bool wide)
    {
        var mark = wide ? 0x8000 : 0x80;
        var first = ReadPart(text, ref at, wide);
        return (first & mark) == 0
            ? first : ((first & (mark - 1)) << (wide ? 16 : 8)) | ReadPart(text, ref at, wide);
    }

    private static int ReadPart(ReadOnlySpan<byte> text, ref int at, bool wide)
    {
        if (wide)
        {
            var result = U16(text, at);
            at += 2;
            return result;
        }
        return at < text.Length ? text[at++] : throw Invalid();
    }

    internal static ushort U16(ReadOnlySpan<byte> input, int at) =>
        at >= 0 && at <= input.Length - 2 ? BinaryPrimitives.ReadUInt16LittleEndian(input[at..]) : throw Invalid();

    internal static uint U32(ReadOnlySpan<byte> input, int at) =>
        at >= 0 && at <= input.Length - 4 ? BinaryPrimitives.ReadUInt32LittleEndian(input[at..]) : throw Invalid();

    private static InvalidDataException Invalid() => new("Invalid or oversized Android binary XML.");
}

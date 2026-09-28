using System.Buffers.Binary;
using System.Text;

namespace Recorder.Database.RecordingFiles;

/// <summary>
/// The parts of the MCAP container format the recording file uses, as
/// defined by the MCAP specification (https://mcap.dev/spec): opcodes, the
/// magic bytes, and the little-endian serialization of records.
/// </summary>
internal static class Mcap
{
    public const byte Header = 0x01;
    public const byte Footer = 0x02;
    public const byte Channel = 0x04;
    public const byte Message = 0x05;
    public const byte Chunk = 0x06;
    public const byte MessageIndex = 0x07;
    public const byte ChunkIndex = 0x08;
    public const byte Attachment = 0x09;
    public const byte AttachmentIndex = 0x0A;
    public const byte Statistics = 0x0B;
    public const byte Metadata = 0x0C;
    public const byte MetadataIndex = 0x0D;
    public const byte SummaryOffset = 0x0E;
    public const byte DataEnd = 0x0F;

    /// <summary>The opcode and the content length that begin every record.</summary>
    public const int RecordPrefixLength = 1 + 8;

    /// <summary>The magic bytes at the start and end of a file, major version 0.</summary>
    public static ReadOnlySpan<byte> Magic => [0x89, (byte)'M', (byte)'C', (byte)'A', (byte)'P', 0x30, (byte)'\r', (byte)'\n'];

    /// <summary>The length of the footer record with its prefix.</summary>
    public const int FooterRecordLength = RecordPrefixLength + 8 + 8 + 4;

    public const string ZstdCompression = "zstd";
    public const string JsonEncoding = "json";
}

/// <summary>Writes MCAP fields into a growing buffer.</summary>
internal sealed class McapBuffer(int initialCapacity = 256)
{
    private byte[] _bytes = new byte[Math.Max(16, initialCapacity)];

    public int Length { get; private set; }

    public ReadOnlySpan<byte> WrittenSpan => _bytes.AsSpan(0, Length);

    public void Clear() => Length = 0;

    public void Byte(byte value) => Take(1)[0] = value;

    public void UInt16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Take(2), value);

    public void UInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Take(4), value);

    public void UInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Take(8), value);

    public void Bytes(ReadOnlySpan<byte> value) => value.CopyTo(Take(value.Length));

    public void String(string value)
    {
        var length = Encoding.UTF8.GetByteCount(value);
        UInt32((uint)length);
        Encoding.UTF8.GetBytes(value, Take(length));
    }

    /// <summary>Writes a <c>Map&lt;string, string&gt;</c>, keys in ordinal order.</summary>
    public void StringMap(IReadOnlyDictionary<string, string> map)
    {
        var lengthAt = Length;
        UInt32(0);
        foreach (var (key, value) in map.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            String(key);
            String(value);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(lengthAt, 4), (uint)(Length - lengthAt - 4));
    }

    /// <summary>
    /// Starts a record: writes its opcode and a placeholder for its content
    /// length, and returns the position to pass to <see cref="EndRecord"/>.
    /// </summary>
    public int BeginRecord(byte opcode)
    {
        Byte(opcode);
        var at = Length;
        UInt64(0);
        return at;
    }

    /// <summary>Fills in the content length of the record begun at <paramref name="lengthAt"/>.</summary>
    public void EndRecord(int lengthAt) =>
        BinaryPrimitives.WriteUInt64LittleEndian(_bytes.AsSpan(lengthAt, 8), (ulong)(Length - lengthAt - 8));

    /// <summary>Overwrites four bytes already written, at <paramref name="at"/>.</summary>
    public void SetUInt32(int at, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(at, 4), value);

    private Span<byte> Take(int count)
    {
        if (Length + count > _bytes.Length)
        {
            Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, Length + count));
        }

        var span = _bytes.AsSpan(Length, count);
        Length += count;
        return span;
    }
}

/// <summary>Reads MCAP fields from a span, failing on a field that runs past its end.</summary>
internal ref struct McapSpanReader(ReadOnlySpan<byte> span)
{
    private readonly ReadOnlySpan<byte> _span = span;

    public int Position { get; private set; }

    public readonly int Remaining => _span.Length - Position;

    public byte Byte() => Take(1)[0];

    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    public string String() => Encoding.UTF8.GetString(Take(checked((int)UInt32())));

    public ReadOnlySpan<byte> Bytes(int length) => Take(length);

    public Dictionary<string, string> StringMap()
    {
        var length = checked((int)UInt32());
        var inner = new McapSpanReader(Take(length));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        while (inner.Remaining > 0)
        {
            var key = inner.String();
            map[key] = inner.String();
        }

        return map;
    }

    private ReadOnlySpan<byte> Take(int length)
    {
        if (length < 0 || length > Remaining)
        {
            throw new InvalidDataException("An MCAP field runs past the end of its record.");
        }

        var taken = _span.Slice(Position, length);
        Position += length;
        return taken;
    }
}

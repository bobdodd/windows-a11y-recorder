using System.Buffers.Binary;
using static Recorder.Collectors.Android.AndroidCompiledXml;

namespace Recorder.Collectors.Android;

/// <summary>Recognises exact defined Dart AOT exports without reading snapshot bodies.</summary>
internal static class AndroidDartSnapshotExports
{
    public static string? Read(ReadOnlySpan<byte> elf, CancellationToken token)
    {
        if (elf.Length < 52 || !elf[..4].SequenceEqual("\u007fELF"u8) ||
            elf[4] is not (1 or 2) || elf[5] != 1 || elf[6] != 1 || U16(elf, 16) != 3 || U32(elf, 20) != 1)
            throw Invalid();
        var elf64 = elf[4] == 2;
        if (elf64 && elf.Length < 64) throw Invalid();
        var width = elf64 ? 64 : 40;
        var offset = Number(elf, elf64 ? 40 : 32, elf64);
        var sectionCount = U16(elf, elf64 ? 60 : 48);
        if (U16(elf, elf64 ? 58 : 46) != width || offset < (elf64 ? 64UL : 52UL) ||
            sectionCount is 0 or > 4096) throw Invalid();
        var sections = Range(elf, offset, (ulong)sectionCount * (uint)width);
        var found = 0;
        var tableSeen = false;
        for (var i = 1; i < sectionCount; i++)
        {
            token.ThrowIfCancellationRequested();
            var section = sections.Slice(i * width, width);
            if (U32(section, 4) != 11) continue; // SHT_DYNSYM, not the static symbol table
            if (tableSeen) throw Invalid();
            tableSeen = true;
            var link = U32(section, elf64 ? 40 : 24);
            var step = elf64 ? 24 : 16;
            if (link == 0 || link >= sectionCount || Number(section, elf64 ? 56 : 36, elf64) != (uint)step)
                throw Invalid();
            var stringSection = sections.Slice((int)link * width, width);
            if (U32(stringSection, 4) != 3) throw Invalid(); // SHT_STRTAB
            var names = Contents(elf, stringSection, elf64);
            var symbols = Contents(elf, section, elf64);
            if (symbols.Length % step != 0 || symbols.Length / step > 65536 || names.IsEmpty || names[0] != 0)
                throw Invalid();
            for (var at = step; at < symbols.Length; at += step)
            {
                token.ThrowIfCancellationRequested();
                var entry = symbols.Slice(at, step);
                var info = entry[elf64 ? 4 : 12];
                var visibility = entry[elf64 ? 5 : 13] & 3;
                var location = U16(entry, elf64 ? 6 : 14);
                if ((info >> 4) is not (1 or 2) || (info & 15) is not (0 or 1 or 2) ||
                    visibility is not (0 or 3) || location == 0 || location >= sectionCount)
                    continue;
                var target = sections.Slice(location * width, width);
                if (U32(target, 4) != 1 || (Number(target, 8, elf64) & 2) == 0) continue; // loadable PROGBITS
                var address = Number(entry, elf64 ? 8 : 4, elf64);
                var start = Number(target, elf64 ? 16 : 12, elf64);
                var contents = Contents(elf, target, elf64);
                if (address < start || address - start >= (ulong)contents.Length) continue;
                var nameOffset = U32(entry, 0);
                if (nameOffset >= names.Length) throw Invalid();
                var name = names[(int)nameOffset..];
                var end = name[..Math.Min(name.Length, 40)].IndexOf((byte)0);
                if (end < 0) continue;
                name = name[..end];
                if (!name.IsEmpty && name[0] == '_') name = name[1..];
                if (name.SequenceEqual("kDartVmSnapshotData"u8)) found |= 1;
                else if (name.SequenceEqual("kDartVmSnapshotInstructions"u8)) found |= 2;
                else if (name.SequenceEqual("kDartIsolateSnapshotData"u8)) found |= 4;
                else if (name.SequenceEqual("kDartIsolateSnapshotInstructions"u8)) found |= 8;
                else if (name.SequenceEqual("kDartSnapshotData"u8)) found |= 16;
                else if (name.SequenceEqual("kDartSnapshotText"u8)) found |= 32;
            }
        }
        if (!tableSeen) throw Invalid();
        return (found & 48) == 48 ? "Dart AOT data and text exports; snapshot code not decoded" :
            (found & 15) == 15 ? "Dart AOT VM and isolate data/instruction exports; snapshot code not decoded" : null;
    }

    private static ReadOnlySpan<byte> Contents(ReadOnlySpan<byte> elf, ReadOnlySpan<byte> section, bool elf64) =>
        Range(elf, Number(section, elf64 ? 24 : 16, elf64), Number(section, elf64 ? 32 : 20, elf64));
    private static ReadOnlySpan<byte> Range(ReadOnlySpan<byte> data, ulong offset, ulong length) =>
        offset <= (ulong)data.Length && length <= (ulong)data.Length - offset
            ? data.Slice((int)offset, (int)length)
            : throw Invalid();
    private static ulong Number(ReadOnlySpan<byte> bytes, int at, bool wide) =>
        wide ? BinaryPrimitives.ReadUInt64LittleEndian(bytes[at..]) : U32(bytes, at);
    private static InvalidDataException Invalid() => new("Unsupported or invalid Dart snapshot ELF export table.");
}

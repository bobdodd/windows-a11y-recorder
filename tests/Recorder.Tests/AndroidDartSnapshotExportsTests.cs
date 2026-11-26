using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Recorder.Collectors.Android;

namespace Recorder.Tests;

public sealed class AndroidDartSnapshotExportsTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ExactDynamicSymbolLayoutsAreBundledEvidence(bool elf64, bool legacy)
    {
        var exports = legacy
            ? new[] { "_kDartVmSnapshotData", "_kDartVmSnapshotInstructions", "_kDartIsolateSnapshotData", "_kDartIsolateSnapshotInstructions" }
            : new[] { "_kDartSnapshotData", "_kDartSnapshotText" };
        using var bytes = new MemoryStream();
        using (var apk = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var output = apk.CreateEntry("lib/arm64-v8a/libapp.so").Open();
            output.Write(Elf(elf64, true, 1, exports));
        }
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
        var inventory = new AndroidApkAccessibilityInventory("org.example.synthetic");
        inventory.Inspect(archive, "split_abi.apk", [], [], false, TestContext.Current.CancellationToken);
        var evidence = inventory.Build(1, 1);
        var marker = Assert.Single(evidence.Items, row => row.Category == "Dart AOT snapshot exports");
        Assert.Equal(("bundled", "app", "split_abi.apk", "lib/arm64-v8a/libapp.so"),
            (marker.Kind, marker.Scope, marker.Split, marker.Locator));
        Assert.Contains(legacy ? "VM and isolate" : "data and text", marker.Value, StringComparison.Ordinal);
        Assert.Contains(evidence.Coverage, row => row.Reader == "Flutter compiled code" && row.Status == "not decoded");
    }

    [Fact]
    public void PrefixesAndUndefinedSymbolsDoNotCountAndInvalidTablesFailClosed()
    {
        var token = TestContext.Current.CancellationToken;
        Assert.Null(AndroidDartSnapshotExports.Read(Elf(true, true, 1, ["_kDartSnapshotDataExtra", "_kDartSnapshotText"]), token));
        Assert.Null(AndroidDartSnapshotExports.Read(Elf(true, true, 0, ["_kDartSnapshotData", "_kDartSnapshotText"]), token));
        Assert.Throws<InvalidDataException>(() => AndroidDartSnapshotExports.Read(
            Elf(true, false, 1, ["_kDartSnapshotData", "_kDartSnapshotText"]), token));
        Assert.Throws<InvalidDataException>(() =>
            AndroidDartSnapshotExports.Read("_kDartSnapshotData\0_kDartSnapshotText\0"u8, token));
    }

    private static byte[] Elf(bool wide, bool dynamic, ushort definition, string[] exports)
    {
        const int sectionTable = 64, symbolTable = 384, stringTable = 576, data = 896;
        var image = new byte[1024];
        "\u007fELF"u8.CopyTo(image);
        image[4] = (byte)(wide ? 2 : 1);
        image[5] = image[6] = 1;
        Half(16, 3);
        Word(20, 1);
        Num(wide ? 40 : 32, sectionTable);
        var width = wide ? 64 : 40;
        var step = wide ? 24 : 16;
        Half(wide ? 58 : 46, (ushort)width);
        Half(wide ? 60 : 48, 4);
        Section(1, 1, 2, data, 16, 0, 0);
        Section(2, dynamic ? 11 : 2, 2, symbolTable, step * (exports.Length + 1), 3, step);
        var used = 1;
        for (var i = 0; i < exports.Length; i++)
        {
            var at = symbolTable + (i + 1) * step;
            Word(at, used);
            used += Encoding.ASCII.GetBytes(exports[i], image.AsSpan(stringTable + used)) + 1;
            image[at + (wide ? 4 : 12)] = 0x11;
            Half(at + (wide ? 6 : 14), definition);
            Num(at + (wide ? 8 : 4), data + i);
        }
        Section(3, 3, 2, stringTable, used, 0, 0);
        return image;

        void Section(int index, int type, int flags, int start, int size, int link, int stride)
        {
            var at = sectionTable + index * width;
            Word(at + 4, type);
            Num(at + 8, flags);
            Num(at + (wide ? 16 : 12), start);
            Num(at + (wide ? 24 : 16), start);
            Num(at + (wide ? 32 : 20), size);
            Word(at + (wide ? 40 : 24), link);
            Num(at + (wide ? 56 : 36), stride);
        }

        void Half(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(at), value);
        void Word(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(at), value);
        void Num(int at, int value)
        {
            if (wide) BinaryPrimitives.WriteInt64LittleEndian(image.AsSpan(at), value);
            else Word(at, value);
        }
    }
}

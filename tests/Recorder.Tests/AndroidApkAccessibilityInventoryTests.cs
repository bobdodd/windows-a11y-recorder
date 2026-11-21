using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Recorder.Collectors.Android;

namespace Recorder.Tests;

public sealed class AndroidApkAccessibilityInventoryTests
{
    [Fact]
    public void MixedApkReadsAllApplicableInputsAndNeverExecutesMarkup()
    {
        var token = TestContext.Current.CancellationToken;
        using var bytes = new MemoryStream();
        using (var apk = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            apk.CreateEntry("lib/arm64-v8a/libflutter.so");
            apk.CreateEntry("lib/arm64-v8a/libapp.so");
            apk.CreateEntry("lib/arm64-v8a/libreactnative.so");
            apk.CreateEntry("META-INF/androidx.compose.ui_ui.version");
            Entry(apk, "assets/index.android.bundle", [0xc6, 0x1f, 0xbc, 0x03, 0xc1, 0x03, 0x19, 0x1f]);
            Entry(apk, "res/layout-land/synthetic.xml", CompiledLayout());
            Entry(apk, "resources.arsc", Styles());
            Entry(apk, "classes2.dex", Dex());
            Entry(apk, "assets/local/form.html", """
                <!doctype html><html lang=en><title>Synthetic</title>
                <h1 tabindex=-1>Details</h1>
                <label for=fictional>Email</label>
                <input id=fictional type=email required aria-describedby=explain value="DO NOT KEEP">
                <input placeholder='Another hint'>
                <div role=status id=explain aria-live=polite></div>
                <meta http-equiv=refresh content='12;url=https://invalid.example/private-path'>
                <script>document.write('<label for="fake">Not real</label>');</script>
                """u8);
            Entry(apk, "assets/illustration.svg", """
                <svg xmlns="http://www.w3.org/2000/svg" role="img" aria-label="Synthetic image"><title>Picture</title></svg>
                """u8);
            Entry(apk, "assets/invalid.svg", "<!DOCTYPE svg><svg/>"u8);
        }
        bytes.Position = 0;
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
        var inventory = new AndroidApkAccessibilityInventory();
        inventory.Inspect(archive, "base.apk", [], [], false, token);
        var evidence = inventory.Build(1, 1);

        Assert.Contains(evidence.Items, item => item.Category == "Typography" && item.Value == "18sp" &&
            item.Locator.Contains("res/layout-land/synthetic.xml", StringComparison.Ordinal));
        Assert.Contains(evidence.Items, item => item.Category == "Form association" && item.Value == "@0x7F020000");
        Assert.Contains(evidence.Items, item => item.Category == "Form label declaration" && item.Value == "@0x7F030000");
        Assert.Contains(evidence.Items, item => item.Category == "Form label declaration" && item.Value == "Email");
        Assert.Contains(evidence.Items, item => item.Category == "Typography" && item.Value == "21sp" &&
            item.Locator.Contains("style/SyntheticStyle", StringComparison.Ordinal));
        Assert.Contains(evidence.Items, item => item.Category == "Resource reference target" && item.Value == "id/Fictional");
        Assert.Contains(evidence.Items, item => item.Category == "Resource reference target" && item.Value == "Synthetic label");
        Assert.Contains(evidence.Items, item => item.Category == "Form association" && item.Value == "fictional");
        Assert.Contains(evidence.Items, item => item.Category == "Timing declaration" && item.Value.StartsWith("12 seconds", StringComparison.Ordinal));
        Assert.Contains(evidence.Items, item => item.Category == "SVG semantics" && item.Value == "Synthetic image");
        Assert.Contains(evidence.Items, item => item.Kind == "symbol" && item.Value == "android.widget.TextView.setTextSize");
        Assert.Contains(evidence.Items, item => item.Kind == "symbol" && item.Value == "androidx.compose.ui.semantics.SemanticsPropertiesKt.heading");
        Assert.Contains(evidence.Items, item => item.Category == "JavaScript bundle candidate" && item.Value.StartsWith("Hermes bytecode", StringComparison.Ordinal));
        Assert.Contains(evidence.Coverage, item => item.Reader == "Packaged SVG accessibility markup" && item.Status == "partial");
        Assert.Contains(evidence.Coverage, item => item.Reader == "Flutter compiled code" && item.Status == "not decoded");
        Assert.DoesNotContain(evidence.Items, item => item.Scope == "screen" || item.Kind == "observed");
        Assert.DoesNotContain(evidence.Items, item => item.Value.Contains("DO NOT KEEP", StringComparison.Ordinal) ||
            item.Value.Contains("Unrelated string", StringComparison.Ordinal) ||
            item.Value.Contains("private-path", StringComparison.Ordinal) || item.Value == "fake");
    }

    [Fact]
    public void TruncatedBinaryXmlMakesCoverageIncompleteWithoutInventingDeclarations()
    {
        var good = CompiledLayout();
        for (var length = 0; length < good.Length; length++)
        {
            var prefix = good[..length];
            Assert.Throws<InvalidDataException>(() => AndroidCompiledXml.Read(prefix, (_, _) => true));
        }
    }

    private static void Entry(ZipArchive zip, string name, ReadOnlySpan<byte> data)
    {
        using var file = zip.CreateEntry(name).Open();
        file.Write(data);
    }

    private static byte[] CompiledLayout()
    {
        var pool = Pool(["http://schemas.android.com/apk/res/android", "TextView", "textSize", "labelFor", "text"]);
        var element = new byte[96];
        Put16(element, 0, 0x0102);
        Put16(element, 2, 16);
        Put32(element, 4, 96);
        Put32(element, 16, uint.MaxValue);
        Put32(element, 20, 1);
        Put16(element, 24, 20);
        Put16(element, 26, 20);
        Put16(element, 28, 3);
        Attribute(36, 2, 5, (18 << 8) | 2);
        Attribute(56, 3, 1, 0x7F020000);
        Attribute(76, 4, 1, 0x7F030000);
        return Chunk(3, 8, pool, element);

        void Attribute(int at, uint name, byte type, uint value)
        {
            Put32(element, at + 4, name);
            Put32(element, at + 8, uint.MaxValue);
            Put16(element, at + 12, 8);
            element[at + 15] = type;
            Put32(element, at + 16, value);
        }
    }

    private static byte[] Styles()
    {
        var types = Pool(["style", "id", "string"]);
        var keys = Pool(["SyntheticStyle", "Fictional", "LabelName", "UnrelatedName"]);
        var style = new byte[28];
        Put16(style, 0, 16);
        Put16(style, 2, 1);
        Put32(style, 12, 1);
        Put32(style, 16, 16842901);
        Put16(style, 20, 8);
        style[23] = 5;
        Put32(style, 24, (21 << 8) | 2);
        var identifier = new byte[8];
        Put16(identifier, 0, 1);
        Put16(identifier, 2, 8);
        var label = new byte[8];
        Put16(label, 0, 2);
        Put16(label, 2, 0x0308);
        var unrelated = new byte[8];
        Put16(unrelated, 0, 3);
        Put16(unrelated, 2, 0x0308);
        Put32(unrelated, 4, 1);
        var package = Chunk(0x0200, 288, types, keys, Type(1, style), Type(2, identifier), Type(3, label, unrelated));
        Put32(package, 8, 0x7F);
        Put32(package, 268, 288);
        Put32(package, 276, (uint)(288 + types.Length));
        return Chunk(2, 12, Pool(["Synthetic label", "Unrelated string"]), package);

        static byte[] Type(byte id, params byte[][] entries)
        {
            var sparse = new byte[4 * entries.Length];
            ushort offset = 0;
            for (var index = 0; index < entries.Length; index++)
            {
                Put16(sparse, index * 4, (ushort)index);
                Put16(sparse, index * 4 + 2, offset);
                offset += (ushort)(entries[index].Length / 4);
            }
            var bytes = Chunk(0x0201, 24, [sparse, .. entries]);
            bytes[8] = id;
            bytes[9] = 1;
            Put32(bytes, 12, (uint)entries.Length);
            Put32(bytes, 16, (uint)(24 + sparse.Length));
            Put32(bytes, 20, 4);
            return bytes;
        }
    }

    private static byte[] Dex()
    {
        var names = new[] { "Landroid/widget/TextView;", "setTextSize",
            "Landroidx/compose/ui/semantics/SemanticsPropertiesKt;", "heading" };
        var bytes = new byte[152 + names.Sum(name => name.Length + 2)];
        "dex\n039\0"u8.CopyTo(bytes);
        Put32(bytes, 32, (uint)bytes.Length);
        Put32(bytes, 36, 112);
        Put32(bytes, 40, 0x12345678);
        Put32(bytes, 56, 4);
        Put32(bytes, 60, 112);
        Put32(bytes, 64, 2);
        Put32(bytes, 68, 128);
        Put32(bytes, 88, 2);
        Put32(bytes, 92, 136);
        Put32(bytes, 132, 2);
        Put32(bytes, 140, 1);
        Put16(bytes, 144, 1);
        Put32(bytes, 148, 3);
        var at = 152;
        for (var index = 0; index < names.Length; index++)
        {
            Put32(bytes, 112 + index * 4, (uint)at);
            bytes[at++] = (byte)names[index].Length;
            at += Encoding.ASCII.GetBytes(names[index], bytes.AsSpan(at)) + 1;
        }
        return bytes;
    }

    private static byte[] Pool(string[] names)
    {
        var table = new byte[names.Length * 4];
        var strings = new byte[names.Sum(name => name.Length + 3)];
        var at = 0;
        for (var index = 0; index < names.Length; index++)
        {
            Put32(table, index * 4, (uint)at);
            strings[at++] = (byte)names[index].Length;
            strings[at++] = (byte)names[index].Length;
            at += Encoding.ASCII.GetBytes(names[index], strings.AsSpan(at)) + 1;
        }
        var result = Chunk(1, 28, table, strings);
        Put32(result, 8, (uint)names.Length);
        Put32(result, 16, 0x100);
        Put32(result, 20, (uint)(28 + table.Length));
        return result;
    }

    private static byte[] Chunk(ushort id, ushort header, params byte[][] children)
    {
        var result = new byte[header + children.Sum(child => child.Length)];
        Put16(result, 0, id);
        Put16(result, 2, header);
        Put32(result, 4, (uint)result.Length);
        var at = (int)header;
        foreach (var child in children)
        {
            child.CopyTo(result, at);
            at += child.Length;
        }
        return result;
    }

    private static void Put16(byte[] bytes, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
    private static void Put32(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
}

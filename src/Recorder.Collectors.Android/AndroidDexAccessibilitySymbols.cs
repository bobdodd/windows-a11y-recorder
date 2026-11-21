using System.Text;
using static Recorder.Collectors.Android.AndroidCompiledXml;

namespace Recorder.Collectors.Android;

/// <summary>DEX method identifiers only. A definition or reference is not proof of a call.</summary>
internal static class AndroidDexAccessibilitySymbols
{
    private static readonly Dictionary<string, string> ViewMethods = new(StringComparer.Ordinal)
    {
        ["setLabelFor"] = "Form API symbol", ["setHint"] = "Form API symbol", ["setError"] = "Form API symbol",
        ["setInputType"] = "Form API symbol", ["setImeOptions"] = "Form API symbol", ["setAutofillHints"] = "Form API symbol",
        ["setImportantForAutofill"] = "Form API symbol", ["setTextSize"] = "Typography API symbol",
        ["setTypeface"] = "Typography API symbol", ["setTextAppearance"] = "Typography API symbol",
        ["setAutoSizeTextTypeWithDefaults"] = "Typography API symbol", ["setLineSpacing"] = "Typography API symbol",
        ["setTextScaleX"] = "Typography API symbol", ["setLetterSpacing"] = "Typography API symbol",
        ["setContentDescription"] = "Semantics API symbol", ["setStateDescription"] = "Semantics API symbol",
        ["setAccessibilityPaneTitle"] = "Semantics API symbol", ["setAccessibilityHeading"] = "Semantics API symbol",
        ["setAccessibilityLiveRegion"] = "Semantics API symbol", ["setImportantForAccessibility"] = "Semantics API symbol",
        ["setScreenReaderFocusable"] = "Semantics API symbol", ["setAccessibilityTraversalAfter"] = "Semantics API symbol",
        ["setAccessibilityTraversalBefore"] = "Semantics API symbol", ["addAccessibilityAction"] = "Semantics API symbol",
        ["replaceAccessibilityAction"] = "Semantics API symbol", ["announceForAccessibility"] = "Semantics API symbol"
    };

    private static readonly Dictionary<string, string> ComposeMethods = new(StringComparer.Ordinal)
    {
        ["semantics"] = "Compose semantics symbol", ["clearAndSetSemantics"] = "Compose semantics symbol",
        ["setContentDescription"] = "Compose semantics symbol", ["setRole"] = "Compose semantics symbol",
        ["setStateDescription"] = "Compose semantics symbol", ["setLiveRegion"] = "Compose semantics symbol",
        ["setTraversalIndex"] = "Compose semantics symbol", ["setCustomActions"] = "Compose semantics symbol",
        ["heading"] = "Compose semantics symbol", ["paneTitle"] = "Compose semantics symbol",
        ["error"] = "Compose semantics symbol", ["onClick"] = "Compose semantics symbol",
        ["onLongClick"] = "Compose semantics symbol", ["setText"] = "Compose semantics symbol"
    };

    public static void Read(ReadOnlySpan<byte> dex, Action<string, string> emit, CancellationToken token)
    {
        if (dex.Length < 112 || !dex[..4].SequenceEqual("dex\n"u8) ||
            !(dex[4] == '0' && dex[5] is >= (byte)'3' and <= (byte)'4' && dex[6] is >= (byte)'0' and <= (byte)'9') ||
            dex[7] != 0 || U32(dex, 32) != dex.Length || U32(dex, 36) != 112 || U32(dex, 40) != 0x12345678)
        {
            throw Invalid();
        }
        var strings = Table(dex, 56, 4, 1_048_576);
        var types = Table(dex, 64, 4, 65536);
        var methods = Table(dex, 88, 8, 65536);
        var owners = new Dictionary<ushort, string?>();
        var seen = new HashSet<(string, string)>();
        for (var at = 0; at < methods.Length; at += 8)
        {
            token.ThrowIfCancellationRequested();
            var type = U16(methods, at);
            if (!owners.TryGetValue(type, out var owner))
            {
                var candidate = ReadName(dex, strings, U32(types, type * 4));
                owner = candidate is not null && IsSupportedOwner(candidate) ? candidate : null;
                owners.Add(type, owner);
            }
            if (owner is null || ReadName(dex, strings, U32(methods, at + 4)) is not { } method ||
                Category(owner, method) is not { } category || !seen.Add((owner, method)))
            {
                continue;
            }
            emit(category, $"{owner[1..^1].Replace('/', '.')}.{method}");
        }
    }

    private static bool IsSupportedOwner(string owner) => owner.EndsWith(';') &&
        (owner.StartsWith("Landroid/view/", StringComparison.Ordinal) ||
         owner.StartsWith("Landroid/widget/", StringComparison.Ordinal) ||
         owner.StartsWith("Landroidx/core/view/", StringComparison.Ordinal) ||
         owner.StartsWith("Landroidx/compose/ui/semantics/", StringComparison.Ordinal) ||
         owner is "Landroid/os/CountDownTimer;" or "Landroid/view/accessibility/AccessibilityManager;");

    private static string? Category(string owner, string method)
    {
        if (owner.StartsWith("Landroidx/compose/ui/semantics/", StringComparison.Ordinal))
        {
            return ComposeMethods.GetValueOrDefault(method.Split('-', 2)[0]);
        }
        if (owner == "Landroid/os/CountDownTimer;" && method is "<init>" or "start" or "cancel" ||
            owner == "Landroid/view/accessibility/AccessibilityManager;" && method == "getRecommendedTimeoutMillis")
        {
            return "Timing API symbol";
        }
        return ViewMethods.GetValueOrDefault(method);
    }

    private static ReadOnlySpan<byte> Table(ReadOnlySpan<byte> dex, int field, int width, int maxEntries)
    {
        var count = U32(dex, field);
        var offset = U32(dex, field + 4);
        return count <= maxEntries && offset + count * (long)width <= dex.Length &&
            (count == 0 ? offset == 0 : offset >= 112)
            ? dex.Slice((int)offset, (int)count * width) : throw Invalid();
    }

    private static string? ReadName(ReadOnlySpan<byte> dex, ReadOnlySpan<byte> strings, uint index)
    {
        if (index >= strings.Length / 4)
        {
            throw Invalid();
        }
        var offset = U32(strings, (int)index * 4);
        if (offset < 112 || offset >= dex.Length)
        {
            throw Invalid();
        }
        var data = dex[(int)offset..];
        // Known names are ASCII. Do not decode, scan or export other DEX strings.
        var size = 0;
        var prefix = 0;
        while (prefix < 5 && prefix < data.Length)
        {
            var part = data[prefix++];
            if (prefix == 5 && (part & 0xF0) != 0)
            {
                throw Invalid();
            }
            size |= (part & 127) << (7 * (prefix - 1));
            if ((part & 128) != 0)
            {
                continue;
            }
            if (size is <= 0 or > 200 || prefix + size >= data.Length || data[prefix + size] != 0)
            {
                return null;
            }
            var text = data.Slice(prefix, size);
            foreach (var character in text)
            {
                if (character < 33 || character > 126)
                {
                    return null;
                }
            }
            return Encoding.ASCII.GetString(text);
        }
        throw Invalid();
    }

    private static InvalidDataException Invalid() => new("Invalid or unsupported Android DEX file.");
}

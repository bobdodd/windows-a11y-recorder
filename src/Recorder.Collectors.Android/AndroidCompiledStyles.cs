using static Recorder.Collectors.Android.AndroidCompiledXml;

namespace Recorder.Collectors.Android;

/// <summary>Retains style bags with their original configuration and parent IDs; does not apply them.</summary>
internal static class AndroidCompiledStyles
{
    // Public Android resource IDs from the Android SDK's R.attr.
    private static readonly Dictionary<uint, string> AndroidAttributes = new()
    {
        [16842901] = "textSize", [16843692] = "fontFamily", [16842903] = "textStyle",
        [16844165] = "textFontWeight", [16842804] = "textAppearance", [16844083] = "fontWeight",
        [16844095] = "fontStyle", [16844082] = "font", [16844085] = "autoSizeTextType",
        [16844088] = "autoSizeMinTextSize", [16844102] = "autoSizeMaxTextSize",
        [16843958] = "letterSpacing", [16843287] = "lineSpacingExtra", [16843288] = "lineSpacingMultiplier",
        [16844159] = "lineHeight", [16843091] = "maxLines", [16842923] = "ellipsize",
        [16842904] = "textColor", [16842906] = "textColorHint", [16843718] = "labelFor",
        [16843088] = "hint", [16843296] = "inputType", [16844118] = "autofillHints",
        [16844120] = "importantForAutofill", [16843364] = "imeOptions", [16843104] = "maxLength",
        [16843100] = "password", [16843379] = "contentDescription", [16844084] = "tooltipText",
        [16844156] = "accessibilityPaneTitle", [16844160] = "accessibilityHeading",
        [16843758] = "accessibilityLiveRegion", [16843985] = "accessibilityTraversalBefore",
        [16843986] = "accessibilityTraversalAfter", [16842970] = "focusable",
        [16842971] = "focusableInTouchMode", [16844148] = "screenReaderFocusable",
        [16842979] = "nextFocusUp", [16842980] = "nextFocusDown", [16842977] = "nextFocusLeft",
        [16842978] = "nextFocusRight", [16843580] = "nextFocusForward", [16843690] = "importantForAccessibility",
        [16843071] = "minWidth", [16843072] = "minHeight", [16842972] = "visibility",
        [16842981] = "clickable", [16842982] = "longClickable"
    };

    public static void Read(ReadOnlySpan<byte> table, Action<string, string, string, bool> emit,
        Func<string, bool> isReferenced, CancellationToken token)
    {
        if (U16(table, 0) != 2 || U16(table, 2) < 12 || U32(table, 4) != table.Length)
        {
            throw Invalid();
        }

        ReadOnlySpan<byte> values = default;
        for (var at = (int)U16(table, 2); at < table.Length;)
        {
            token.ThrowIfCancellationRequested();
            var chunk = Chunk(table, at);
            switch (U16(chunk, 0))
            {
                case 1 when values.IsEmpty:
                    values = Pool(table, at);
                    break;
                case 0x0200:
                    ReadPackage(chunk, values, emit, isReferenced, token);
                    break;
            }
            at += chunk.Length;
        }
    }

    private static void ReadPackage(ReadOnlySpan<byte> package, ReadOnlySpan<byte> values,
        Action<string, string, string, bool> emit, Func<string, bool> isReferenced, CancellationToken token)
    {
        var header = U16(package, 2);
        var packageId = U32(package, 8);
        if (header < 284 || packageId > 255)
        {
            throw Invalid();
        }
        var typeOffset = U32(package, 268);
        var keyOffset = U32(package, 276);
        if (typeOffset == 0 || keyOffset == 0)
        {
            throw Invalid(); // A package inheriting a different package's symbol pools is not supported.
        }
        var types = Pool(package, Offset(typeOffset));
        var keys = Pool(package, Offset(keyOffset));
        var idOffset = header >= 288 ? U32(package, 284) : 0;
        // Read styles before text targets regardless of their type ID.
        foreach (var stringsPass in new[] { false, true })
        {
            for (var at = (int)header; at < package.Length;)
            {
                token.ThrowIfCancellationRequested();
                var chunk = Chunk(package, at);
                if (U16(chunk, 0) == 0x0201)
                {
                    if (chunk.Length < 24 || chunk[8] == 0 || idOffset > 255 - (uint)chunk[8])
                    {
                        throw Invalid();
                    }
                    var type = StringAt(types, (uint)chunk[8] - 1);
                    var id = packageId << 24 | ((uint)chunk[8] + idOffset) << 16;
                    if (stringsPass ? type == "string" : type is "style" or "dimen" or "font" or "id")
                    {
                        ReadType(chunk, type!, id, keys, values, emit, isReferenced, token);
                    }
                }
                at += chunk.Length;
            }
        }
    }

    private static void ReadType(ReadOnlySpan<byte> chunk, string type, uint id, ReadOnlySpan<byte> keys,
        ReadOnlySpan<byte> values, Action<string, string, string, bool> emit, Func<string, bool> isReferenced, CancellationToken token)
    {
        var header = U16(chunk, 2);
        var entries = U32(chunk, 12);
        var data = Offset(U32(chunk, 16));
        var configSize = U32(chunk, 20);
        var sparse = (chunk[9] & 1) != 0;
        var offset16 = !sparse && (chunk[9] & 2) != 0;
        var width = offset16 ? 2 : 4;
        if ((chunk[9] & ~3) != 0 || header < 24 || entries > 65536 ||
            header + width * (long)entries > data || data > chunk.Length || configSize < 4 || 20L + configSize > header)
        {
            throw Invalid();
        }
        var config = chunk.Slice(24, (int)configSize - 4);
        // Keep the encoded configuration so locale, SDK and other variants
        // remain distinct. Do not calculate which one Android would select.
        var qualifier = DescribeConfiguration(config);
        for (var number = 0; number < entries; number++)
        {
            token.ThrowIfCancellationRequested();
            var slot = header + width * number;
            var index = sparse ? U16(chunk, slot) : (uint)number;
            var offset = sparse ? (uint)U16(chunk, slot + 2) * 4 :
                offset16 ? U16(chunk, slot) is ushort.MaxValue ? uint.MaxValue : (uint)U16(chunk, slot) * 4 :
                U32(chunk, slot);
            if (offset == uint.MaxValue)
            {
                continue;
            }
            var entryId = id | index;
            var reference = $"@0x{entryId:X8}";
            if (type == "string" && !isReferenced(reference))
            {
                continue;
            }
            if ((long)data + offset + 8 > chunk.Length)
            {
                throw Invalid();
            }
            var entry = chunk[(data + (int)offset)..];
            var flags = U16(entry, 2);
            var compact = (flags & 8) != 0;
            var key = StringAt(keys, compact ? U16(entry, 0) : U32(entry, 4)) ?? throw Invalid();
            var locator = $"resources.arsc / {type}/{key} ({reference}, {qualifier})";
            if (type == "style" && (flags & 1) != 0 && !compact)
            {
                ReadStyle(entry, locator, values, emit);
            }
            else if ((flags & 1) == 0)
            {
                var decoded = compact ? (DescribeValue((byte)(flags >> 8), U32(entry, 4), values), (flags >> 8) is 1 or 7) :
                    ReadValue(entry, U16(entry, 0), values);
                var (value, isResourceReference) = decoded;
                if (type == "style" && value is { } styleAlias)
                {
                    emit(locator, "style", styleAlias, isResourceReference);
                }
                else if (type is "id" or "font" or "dimen" or "string" or "layout")
                {
                    // Defer unrelated resource entries. Only targets referenced
                    // by retained accessibility declarations reach the recording.
                    emit(locator, $"reference:{reference}", type == "id" ? $"{type}/{key}" : value ?? $"{type}/{key}", false);
                }
            }
        }
    }

    private static string DescribeConfiguration(ReadOnlySpan<byte> config)
    {
        if (config.IndexOfAnyExcept((byte)0) < 0)
        {
            return "default";
        }
        var direction = config.Length > 8 ? config[8] switch
        {
            1 => "port",
            2 => "land",
            3 => "square",
            _ => null
        } : null;
        return (direction is null ? "" : direction + "; ") + $"config {Convert.ToHexString(config)}";
    }

    private static void ReadStyle(ReadOnlySpan<byte> entry, string locator, ReadOnlySpan<byte> values,
        Action<string, string, string, bool> emit)
    {
        var header = U16(entry, 0);
        var parent = U32(entry, 8);
        var count = U32(entry, 12);
        if (header < 16 || header + count * 12L > entry.Length)
        {
            throw Invalid();
        }
        var emitted = false;
        for (var index = 0; index < count; index++)
        {
            var at = header + index * 12;
            if (AndroidAttributes.TryGetValue(U32(entry, at), out var property) &&
                ReadValue(entry, at + 4, values) is ({ } value, var resourceReference))
            {
                emit($"{locator} / android:{property}", property, value, resourceReference);
                emitted = true;
            }
        }
        if (emitted && parent != 0)
        {
            emit($"{locator} / parent", "style", $"@0x{parent:X8}", true);
        }
    }

    private static (string? Value, bool IsResourceReference) ReadValue(ReadOnlySpan<byte> bytes, int at, ReadOnlySpan<byte> values)
    {
        if (at < 0 || at > bytes.Length - 8 || U16(bytes, at) < 8 || U16(bytes, at) > bytes.Length - at)
        {
            throw Invalid();
        }
        return (DescribeValue(bytes[at + 3], U32(bytes, at + 4), values), bytes[at + 3] is 1 or 7);
    }

    private static ReadOnlySpan<byte> Chunk(ReadOnlySpan<byte> input, int at)
    {
        var header = U16(input, at + 2);
        var size = U32(input, at + 4);
        return header >= 8 && size >= header && size <= input.Length - at
            ? input.Slice(at, (int)size) : throw Invalid();
    }

    private static ReadOnlySpan<byte> Pool(ReadOnlySpan<byte> input, int at)
    {
        var chunk = Chunk(input, at);
        return U16(chunk, 0) == 1 && U16(chunk, 2) >= 28 ? chunk : throw Invalid();
    }

    private static int Offset(uint value) => value <= int.MaxValue ? (int)value : throw Invalid();
    private static InvalidDataException Invalid() => new("Invalid or unsupported compiled Android resource table.");
}

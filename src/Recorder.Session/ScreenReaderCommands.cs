using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>A screen reader command from the command data: its name, section, keys, and context.</summary>
public sealed record ScreenReaderCommand(
    string Name,
    string Section,
    string Desktop,
    string Laptop,
    string Context,
    bool Passes);

/// <summary>
/// The key commands of one screen reader version, from a data file in
/// ScreenReaderCommands, with the source of each. See
/// docs/architecture/screen-reader-activity.md, "2c-1, the commands".
/// </summary>
public sealed class ScreenReaderCommandData
{
    private readonly Dictionary<string, List<ScreenReaderCommand>> _desktop = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ScreenReaderCommand>> _laptop = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sectionTitles = new(StringComparer.Ordinal);

    public ScreenReaderCommandData(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Product = root.GetProperty("product").GetString()!;
        Version = root.GetProperty("version").GetString()!;
        Source = root.GetProperty("source").GetString()!;
        foreach (var section in root.GetProperty("sections").EnumerateArray())
        {
            _sectionTitles[section.GetProperty("id").GetString()!] = section.GetProperty("title").GetString()!;
        }

        var commands = new List<ScreenReaderCommand>();
        foreach (var item in root.GetProperty("commands").EnumerateArray())
        {
            var command = new ScreenReaderCommand(
                item.GetProperty("name").GetString()!,
                item.GetProperty("section").GetString()!,
                item.GetProperty("desktop").GetString()!,
                item.GetProperty("laptop").GetString()!,
                item.GetProperty("context").GetString()!,
                item.GetProperty("passes").GetBoolean());
            commands.Add(command);
            Add(_desktop, ScreenReaderGestures.Canonical(command.Desktop), command);
            Add(_laptop, ScreenReaderGestures.Canonical(command.Laptop), command);
        }

        Commands = commands;
    }

    public string Product { get; }

    public string Version { get; }

    public string Source { get; }

    public IReadOnlyList<ScreenReaderCommand> Commands { get; }

    /// <summary>The commands of a gesture in a keyboard layout ("desktop" or "laptop").</summary>
    public IReadOnlyList<ScreenReaderCommand> Of(string gesture, string layout) =>
        (layout == "laptop" ? _laptop : _desktop).GetValueOrDefault(ScreenReaderGestures.Canonical(gesture)) ?? [];

    /// <summary>A command's source: the section of the reference, with its address.</summary>
    public string SourceOf(ScreenReaderCommand command) =>
        $"{Product} {Version} commands quick reference, {_sectionTitles.GetValueOrDefault(command.Section, command.Section)}, {Source}#{command.Section}";

    private static void Add(Dictionary<string, List<ScreenReaderCommand>> map, string key, ScreenReaderCommand command)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        list.Add(command);
    }

    private static readonly Lazy<IReadOnlyList<ScreenReaderCommandData>> Embedded = new(() =>
    {
        var assembly = typeof(ScreenReaderCommandData).Assembly;
        var list = new List<ScreenReaderCommandData>();
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(name => name.Contains(".ScreenReaderCommands.", StringComparison.Ordinal) &&
                         name.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            list.Add(new ScreenReaderCommandData(reader.ReadToEnd()));
        }

        return list;
    });

    /// <summary>The command data in the player.</summary>
    public static IReadOnlyList<ScreenReaderCommandData> All => Embedded.Value;

    /// <summary>
    /// The data for a product's version: its own, or the nearest earlier
    /// version's, with a note saying so; or, for a version before all the
    /// data or not known, the earliest, with a note. Null where the product
    /// has no data.
    /// </summary>
    public static (ScreenReaderCommandData Data, string? Note)? For(
        string product,
        string? version,
        IReadOnlyList<ScreenReaderCommandData>? available = null)
    {
        var data = (available ?? All)
            .Where(data => string.Equals(data.Product, product, StringComparison.Ordinal))
            .OrderBy(data => Parse(data.Version))
            .ToList();
        if (data.Count == 0)
        {
            return null;
        }

        var wanted = version is null ? null : Parse(version);
        if (wanted is null)
        {
            return (data[0], $"the {product} version is not known; using the {data[0].Version} commands");
        }

        var exact = data.FirstOrDefault(item => Parse(item.Version) is { } parsed &&
            parsed.Major == wanted.Major && parsed.Minor == wanted.Minor);
        if (exact is not null)
        {
            return (exact, null);
        }

        var earlier = data.LastOrDefault(item => Parse(item.Version) is { } parsed && parsed < wanted);
        return earlier is not null
            ? (earlier, $"no command data for {product} {version}; using the nearest earlier version's, {earlier.Version}")
            : (data[0], $"no command data for {product} {version} or earlier; using {data[0].Version}");
    }

    // NVDA's versions are year.release, then build numbers.
    private static System.Version? Parse(string version)
    {
        var parts = version.Split('.', StringSplitOptions.TrimEntries);
        return parts.Length >= 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minor)
            ? new System.Version(major, minor)
            : null;
    }
}

/// <summary>
/// Key combinations as NVDA names them (<c>NVDA+f7</c>, <c>shift+h</c>),
/// from the virtual keys the keyboard hook records.
/// </summary>
public static class ScreenReaderGestures
{
    private static readonly string[] ModifierOrder = ["nvda", "control", "shift", "alt", "windows"];

    /// <summary>
    /// A gesture in one form for comparison: lower case, the modifiers in a
    /// fixed order, and "," and "." named as comma and period.
    /// </summary>
    public static string Canonical(string gesture)
    {
        var text = gesture.Trim();
        if (text.StartsWith("kb:", StringComparison.OrdinalIgnoreCase))
        {
            text = text[3..];
        }
        else if (text.StartsWith("kb(", StringComparison.OrdinalIgnoreCase) && text.IndexOf("):", StringComparison.Ordinal) is var end and > 0)
        {
            text = text[(end + 2)..];
        }

        // A key of "+" alone, or ending a combination, is the key itself.
        var parts = new List<string>();
        foreach (var part in text.Split('+'))
        {
            parts.Add(part.Trim().ToLowerInvariant());
        }

        var key = parts[^1] switch
        {
            "," => "comma",
            "." => "period",
            var other => other
        };
        var modifiers = parts.Take(parts.Count - 1)
            .Select(part => part == "ctrl" ? "control" : part)
            .Distinct()
            .OrderBy(part => Array.IndexOf(ModifierOrder, part) is var index and >= 0 ? index : ModifierOrder.Length)
            .ThenBy(part => part, StringComparer.Ordinal);
        return string.Join("+", [.. modifiers, key]);
    }

    /// <summary>The modifier a virtual key is, or null for another key.</summary>
    public static string? Modifier(int virtualKey) => virtualKey switch
    {
        0x10 or 0xA0 or 0xA1 => "shift",
        0x11 or 0xA2 or 0xA3 => "control",
        0x12 or 0xA4 or 0xA5 => "alt",
        0x5B or 0x5C => "windows",
        _ => null
    };

    /// <summary>
    /// Whether a key is an NVDA key under the setting
    /// (<see cref="NvdaSettings.ModifierKeys"/>): Caps Lock, Numpad Insert
    /// (Insert not extended), or Insert (extended).
    /// </summary>
    public static bool IsNvdaKey(int virtualKey, bool extended, int modifierKeys) => virtualKey switch
    {
        0x14 => (modifierKeys & NvdaSettings.CapsLockModifier) != 0,
        0x2D when !extended => (modifierKeys & NvdaSettings.NumpadInsertModifier) != 0,
        0x2D => (modifierKeys & NvdaSettings.ExtendedInsertModifier) != 0,
        _ => false
    };

    /// <summary>The NVDA keys of a setting, named for the details.</summary>
    public static string NvdaKeysText(int modifierKeys)
    {
        var names = new List<string>();
        if ((modifierKeys & NvdaSettings.CapsLockModifier) != 0)
        {
            names.Add("Caps Lock");
        }

        if ((modifierKeys & NvdaSettings.NumpadInsertModifier) != 0)
        {
            names.Add("Numpad Insert");
        }

        if ((modifierKeys & NvdaSettings.ExtendedInsertModifier) != 0)
        {
            names.Add("Insert");
        }

        return string.Join(", ", names);
    }

    /// <summary>
    /// A key's name as NVDA's gestures write it: letters and digits in
    /// lower case, the keys of the number pad as numpad keys (an arrow,
    /// Home, End, Page Up, Page Down, Insert, or Delete that is not
    /// extended is the number pad's), or null for a key with no name here.
    /// </summary>
    public static string? KeyName(int virtualKey, bool extended) => virtualKey switch
    {
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => char.ToLowerInvariant((char)virtualKey).ToString(),
        >= 0x60 and <= 0x69 => "numpad" + (virtualKey - 0x60).ToString(CultureInfo.InvariantCulture),
        >= 0x70 and <= 0x87 => "f" + (virtualKey - 0x6F).ToString(CultureInfo.InvariantCulture),
        0x08 => "backspace",
        0x09 => "tab",
        0x0C => "numpad5",
        0x0D => extended ? "numpadEnter" : "enter",
        0x1B => "escape",
        0x20 => "space",
        0x21 => extended ? "pageUp" : "numpad9",
        0x22 => extended ? "pageDown" : "numpad3",
        0x23 => extended ? "end" : "numpad1",
        0x24 => extended ? "home" : "numpad7",
        0x25 => extended ? "leftArrow" : "numpad4",
        0x26 => extended ? "upArrow" : "numpad8",
        0x27 => extended ? "rightArrow" : "numpad6",
        0x28 => extended ? "downArrow" : "numpad2",
        0x2D => extended ? "insert" : "numpadInsert",
        0x2E => extended ? "delete" : "numpadDelete",
        0x5D => "applications",
        0x6A => "numpadMultiply",
        0x6B => "numpadPlus",
        0x6D => "numpadMinus",
        0x6E => "numpadDecimal",
        0x6F => "numpadDivide",
        0xBC => "comma",
        0xBE => "period",
        _ => null
    };
}

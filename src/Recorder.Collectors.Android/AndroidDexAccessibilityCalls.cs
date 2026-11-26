using System.Globalization;
using static Recorder.Collectors.Android.AndroidCompiledXml;
using static Recorder.Collectors.Android.AndroidDexAccessibilitySymbols;

namespace Recorder.Collectors.Android;

/// <summary>
/// Bounded static DEX inspection, not a decompiler. Uses code-item instruction
/// boundaries and exact API prototypes. Values are traced only in straight-line
/// methods without exception handlers, and never guessed from the string pool.
/// </summary>
internal sealed class AndroidDexAccessibilityCalls(byte[] dex, string package, CancellationToken token)
{
    private const string Activity = "Landroid/app/Activity;";
    private const string Timer = "Landroid/os/CountDownTimer;";
    private const string WebView = "Landroid/webkit/WebView;";
    private const string Manager = "Landroid/view/accessibility/AccessibilityManager;";
    private const string Typeface = "Landroid/graphics/Typeface;";
    private const string LocalAsset = "file:///android_asset/";
    private const int MaxCodeUnits = 2_000_000;
    private const int MaxSites = 1024;
    private readonly string _prefix = "L" + package.Replace('.', '/') + "/";
    private readonly Dictionary<uint, Method> _references = [];
    private readonly Dictionary<uint, string> _types = [];
    private readonly Dictionary<uint, Prototype> _prototypes = [];
    private readonly Dictionary<uint, Body> _bodies = [];
    private readonly Dictionary<string, string?> _parents = new(StringComparer.Ordinal);
    private readonly HashSet<(string Owner, string Name, string Signature)> _declarations = [];
    private readonly Dictionary<uint, (int Duration, int Interval)?> _timerWrappers = [];
    private int _units;
    private bool _partial;

    internal sealed record Site(string Category, string Locator, string Value, string[] Resources);
    internal sealed record Result(IReadOnlyList<Site> Sites, int Methods, int LinearMethods, bool Partial);
    private sealed record Prototype(string[] Parameters, string Return)
    {
        public string Signature { get; } = Parameters.Sum(type => type.Length) + Return.Length < 420
            ? $"({string.Concat(Parameters)}){Return}" : "[type unavailable]";
        public int ArgumentWords { get; } = Parameters.Sum(WordCount);
    }
    private sealed record Method(string Owner, string Name, Prototype Prototype)
    {
        public string Signature => Prototype.Signature;
        public string[] Parameters => Prototype.Parameters;
        public string Locator => $"{Owner[1..^1].Replace('/', '.')}.{Name}{Signature}";
        public string Api => $"{Owner[1..^1].Replace('/', '.')}.{Name}";
    }
    private sealed record Body(uint Id, int Offset, Method Method);
    private sealed record Code(int Registers, int Incoming, bool Linear, ushort[] Words, int[] Positions);
    private enum ValueType { None, Int, Wide, Timeout, WideTimeout, StringIndex, ViewId, FontResource, PlatformTypeface }
    private readonly record struct Value(ValueType Type, long Data);

    private ReadOnlySpan<byte> Strings => Table(dex, 56, 4, 1_048_576);
    private ReadOnlySpan<byte> Types => Table(dex, 64, 4, 65536);
    private ReadOnlySpan<byte> Prototypes => Table(dex, 72, 12, 65536);
    private ReadOnlySpan<byte> Methods => Table(dex, 88, 8, 65536);

    public Result Read(Func<string, bool> bundledHtml)
    {
        Validate(dex);
        var definitions = Table(dex, 96, 32, 65536);
        for (var at = 0; at < definitions.Length; at += 32)
        {
            token.ThrowIfCancellationRequested();
            var owner = TypeName(U32(definitions, at));
            if (!owner.StartsWith(_prefix, StringComparison.Ordinal))
            {
                continue;
            }
            var superclass = U32(definitions, at + 8);
            if (!_parents.TryAdd(owner, superclass == uint.MaxValue ? null : TypeName(superclass)))
            {
                throw Invalid();
            }
            if (U32(definitions, at + 24) is var offset and not 0)
            {
                ReadClassData(Offset(offset), owner);
            }
        }

        var sites = new List<Site>();
        var inspected = 0;
        var linear = 0;
        foreach (var body in _bodies.Values)
        {
            token.ThrowIfCancellationRequested();
            if (_units >= MaxCodeUnits || sites.Count >= MaxSites)
            {
                _partial = true;
                break;
            }
            try
            {
                var code = ReadCode(body);
                var rows = Inspect(body, code, bundledHtml);
                inspected++;
                if (code.Linear) linear++;
                var remaining = MaxSites - sites.Count;
                sites.AddRange(rows.Take(remaining));
                _partial |= rows.Count > remaining;
            }
            catch (InvalidDataException)
            {
                _partial = true;
            }
        }
        return new(sites, inspected, linear, _partial);
    }

    private void ReadClassData(int at, string owner)
    {
        var staticFields = Leb(ref at);
        var instanceFields = Leb(ref at);
        var directMethods = Leb(ref at);
        var virtualMethods = Leb(ref at);
        if ((long)staticFields + instanceFields > 131072 || (long)directMethods + virtualMethods > 65536)
            throw Invalid();
        for (var i = 0L; i < (long)staticFields + instanceFields; i++)
        {
            _ = Leb(ref at);
            _ = Leb(ref at);
        }
        ReadMethods(ref at, directMethods, owner);
        ReadMethods(ref at, virtualMethods, owner);
    }

    private void ReadMethods(ref int at, uint count, string owner)
    {
        long id = 0;
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            id += Leb(ref at);
            _ = Leb(ref at);
            var code = Leb(ref at);
            if (id >= Methods.Length / 8) throw Invalid();
            var method = Reference((uint)id);
            if (method.Owner != owner) throw Invalid();
            if (!_declarations.Add((owner, method.Name, method.Signature)))
            {
                if (method.Name != "(name unavailable)" && !method.Signature.Contains("[type unavailable]", StringComparison.Ordinal))
                    throw Invalid();
                _partial = true;
            }
            if (code != 0 && !_bodies.TryAdd((uint)id, new((uint)id, Offset(code), method))) throw Invalid();
        }
    }

    private Code ReadCode(Body body)
    {
        var start = body.Offset;
        if ((start & 3) != 0 || start > dex.Length - 16) throw Invalid();
        var count = U32(dex, start + 12);
        if (count == 0 || count > 65536 || start + 16L + count * 2L > dex.Length ||
            count > MaxCodeUnits - _units) throw Invalid();
        _units += (int)count;
        var registers = U16(dex, start);
        var incoming = U16(dex, start + 2);
        if (incoming > registers) throw Invalid();
        var words = new ushort[count];
        for (var i = 0; i < words.Length; i++) words[i] = U16(dex, start + 16 + 2 * i);
        var positions = new List<int>();
        var linear = U16(dex, start + 6) == 0;
        for (var pc = 0; pc < words.Length;)
        {
            token.ThrowIfCancellationRequested();
            var word = words[pc];
            var op = word & 255;
            var width = Width(words, pc, op);
            if (width < 1 || width > words.Length - pc) throw Invalid();
            if (word == 0 || op != 0) positions.Add(pc); // Payload bytes are data, not instructions.
            linear &= word == 0 || op != 0;
            linear &= !(op is >= 0x27 and <= 0x2c or >= 0x32 and <= 0x3d ||
                op is >= 0x0e and <= 0x11 && pc + width < words.Length);
            pc += width;
        }
        return new(registers, incoming, linear, words, positions.ToArray());
    }

    private List<Site> Inspect(Body body, Code code, Func<string, bool> bundledHtml)
    {
        var sites = new List<Site>();
        var registers = new Dictionary<int, Value>();
        var pending = default(Value);
        var words = code.Words;
        foreach (var pc in code.Positions)
        {
            token.ThrowIfCancellationRequested();
            var word = words[pc];
            var op = word & 255;
            var a = word >> 8;
            var previous = pending;
            pending = default;
            if (op is >= 0x6e and <= 0x72 or >= 0x74 and <= 0x78)
            {
                var id = words[pc + 1];
                var called = Reference(id);
                var args = Arguments(words, pc, code.Registers);
                var instance = op is not (0x71 or 0x77);
                if (args.Length != called.Prototype.ArgumentWords + (instance ? 1 : 0)) throw Invalid();
                var cursor = instance ? 1 : 0;
                foreach (var param in called.Parameters)
                {
                    var width = WordCount(param);
                    if (width == 2 && args[cursor + 1] != args[cursor] + 1) throw Invalid();
                    cursor += width;
                }
                if (op is 0x6e or 0x6f or 0x74 or 0x75 or 0x70 or 0x76)
                {
                    var resource = Id(At(0), ValueType.ViewId);
                    if (called.Name == "setId" && called.Signature == "(I)V" && IsView(called.Owner))
                    {
                        Write(args[0], Id(At(1)) is not null ? new(ValueType.ViewId, At(1).Data) : default);
                    }
                    else if (called.Name == "findViewById" && called.Signature == "(I)Landroid/view/View;" &&
                        (IsActivity(called.Owner) || IsView(called.Owner)) && Id(At(1)) is not null)
                    {
                        pending = new(ValueType.ViewId, At(1).Data);
                    }
                    else if (called.Name == "setContentView" && called.Signature == "(I)V" && IsActivity(called.Owner))
                    {
                        var layout = Id(At(1));
                        Emit("Native layout call", Describe(called, layout is null ? null : $"layout resource {layout}", null), layout);
                    }
                    else if (called.Name == "setContentView" && called.Signature == "(Landroid/view/View;)V" && IsActivity(called.Owner))
                    {
                        Emit("Native layout call", $"{called.Api} takes a View; the rendered layout was not resolved.");
                    }
                    else if (called.Name == "setTextSize" && IsTextView(called.Owner) && called.Signature is "(F)V" or "(IF)V")
                    {
                        var size = called.Signature == "(F)V" ? TextSize(new(ValueType.Int, 2), At(1)) : TextSize(At(1), At(2));
                        Emit("Native text-size call", Describe(called, size is null ? null : $"text size {size}", resource), resource);
                    }
                    else if (called.Name == "setLabelFor" && called.Signature == "(I)V" && IsView(called.Owner))
                    {
                        var target = Id(At(1));
                        Emit("Native label-association call",
                            Describe(called, target is null ? null : $"label target {target}", resource), resource, target);
                    }
                    else if (called.Name == "getFont" && called is { Owner: "Landroid/content/res/Resources;", Signature: "(I)Landroid/graphics/Typeface;" } &&
                        Id(At(1)) is not null)
                    {
                        pending = new(ValueType.FontResource, At(1).Data);
                    }
                    else if (IsTextView(called.Owner) && called.Name == "setTypeface" &&
                        called.Signature is "(Landroid/graphics/Typeface;)V" or "(Landroid/graphics/Typeface;I)V")
                    {
                        var font = Id(At(1), ValueType.FontResource);
                        var style = At(2) is { Type: ValueType.Int } knownStyle ? TypefaceStyle(knownStyle.Data) : null;
                        var typeface = font is not null ? $"font resource {font}" :
                            At(1) is { Type: ValueType.PlatformTypeface } known ? $"platform Typeface.{PlatformTypeface((int)known.Data)}" : "typeface not resolved";
                        if (called.Parameters.Length == 2) typeface += $"; style {style ?? "not resolved"}";
                        Emit("Native typeface call", Describe(called, typeface, resource), resource, font);
                    }
                    else if (IsTextView(called.Owner) && called.Name == "setTextAppearance" &&
                        called.Signature is "(I)V" or "(Landroid/content/Context;I)V")
                    {
                        var style = Id(At(called.Parameters.Length));
                        Emit("Native text-appearance call", Describe(called, style is null ? null : $"style resource {style}", resource),
                            resource, style);
                    }
                    else if (IsTextView(called.Owner) && called.Name is "setHint" or "setError" &&
                        called.Signature is "(I)V" or "(Ljava/lang/CharSequence;)V")
                    {
                        var declaredText = called.Signature == "(I)V" ? Id(At(1)) : null;
                        Emit("Native form text call", Describe(called, declaredText is null ? null : $"text resource {declaredText}", resource),
                            resource, declaredText);
                    }
                    else if (SemanticsArgument(called, At(1)) is { } semantics)
                    {
                        Emit("Native semantics call", Describe(called, semantics == "" ? null : semantics, resource), resource);
                    }
                    else if (called.Owner == Manager && called.Name == "getRecommendedTimeoutMillis" && called.Signature == "(II)I")
                    {
                        var duration = At(1);
                        var flags = At(2);
                        var baseTime = duration is { Type: ValueType.Int, Data: >= 0 } ? $"{Number(duration.Data)} ms base" : "base not resolved";
                        var content = flags is { Type: ValueType.Int } ? $"content flags {Flags(flags.Data)}" : "flags not resolved";
                        Emit("Native timer call", $"{called.Api}: {baseTime}; {content}.");
                        pending = new(ValueType.Timeout, pc);
                    }
                    else if (op is 0x70 or 0x76 && called.Name == "<init>" &&
                        TimerArguments(id, called) is { } pair)
                    {
                        var duration = At(pair.Duration);
                        var interval = At(pair.Interval);
                        var durationText = duration.Type == ValueType.WideTimeout
                            ? $"duration from getRecommendedTimeoutMillis at +0x{duration.Data:X}"
                            : duration is { Type: ValueType.Wide, Data: >= 0 } ? $"duration argument {Number(duration.Data)} ms" : "duration not resolved";
                        var tickText = interval is { Type: ValueType.Wide, Data: > 0 }
                            ? $"; tick interval argument {Number(interval.Data)} ms" : "; tick interval not resolved";
                        var target = called.Owner == Timer ? "CountDownTimer constructor" : "Constructor forwards to CountDownTimer";
                        Emit("Native timer call", $"{target}: {durationText}{tickText}.");
                    }
                    else if ((called.Owner == Timer || _parents.GetValueOrDefault(called.Owner) == Timer) &&
                        (called.Name == "start" && called.Signature == "()Landroid/os/CountDownTimer;" ||
                         called.Name == "cancel" && called.Signature == "()V"))
                    {
                        Emit("Native timer call", $"Direct CountDownTimer.{called.Name} call; execution not observed.");
                    }
                    else if (called.Owner == WebView && called.Name == "loadUrl" && called.Signature == "(Ljava/lang/String;)V")
                    {
                        var asset = At(1) is { Type: ValueType.StringIndex } literal &&
                            ReadName(dex, Strings, (uint)literal.Data) is { } name && name.StartsWith(LocalAsset, StringComparison.Ordinal)
                                ? "assets/" + name[LocalAsset.Length..] : null;
                        if (asset is not null && IsSafeLocalHtml(asset) && bundledHtml(asset))
                            Emit("Packaged WebView load call", $"WebView.loadUrl refers to bundled {asset}.");
                        else
                            Emit("WebView load call", "Direct WebView.loadUrl call; no matching packaged HTML literal was established.");
                    }
                }
                else if (!instance && called is { Owner: "Landroidx/core/content/res/ResourcesCompat;", Name: "getFont",
                    Signature: "(Landroid/content/Context;I)Landroid/graphics/Typeface;" } && Id(At(1)) is not null)
                {
                    pending = new(ValueType.FontResource, At(1).Data);
                }

                Value At(int index) => code.Linear && index < args.Length ? registers.GetValueOrDefault(args[index]) : default;
            }
            else if (code.Linear)
            {
                switch (op)
                {
                    case 0x00 or 0x1f or >= 0x4b and <= 0x51 or >= 0x59 and <= 0x5f or >= 0x67 and <= 0x6d:
                        // Nop, check-cast and field/array stores do not write registers.
                        break;
                    case 0x01 or 0x04 or 0x07: Move(a & 15, a >> 4, op == 0x04); break;
                    case 0x02 or 0x05 or 0x08: Move(a, words[pc + 1], op == 0x05); break;
                    case 0x03 or 0x06 or 0x09: Move(words[pc + 1], words[pc + 2], op == 0x06); break;
                    case 0x0a: Write(a, previous.Type == ValueType.Timeout ? previous : default); break;
                    case 0x0b: Write(a, default, true); break;
                    case 0x0c: Write(a, previous.Type is ValueType.ViewId or ValueType.FontResource ? previous : default); break;
                    case 0x12: Write(a & 15, new(ValueType.Int, (sbyte)(a & 0xf0) >> 4)); break;
                    case 0x13: Write(a, new(ValueType.Int, (short)words[pc + 1])); break;
                    case 0x14: Write(a, new(ValueType.Int, Bits32(words, pc + 1))); break;
                    case 0x15: Write(a, new(ValueType.Int, words[pc + 1] << 16)); break;
                    case 0x16: Write(a, new(ValueType.Wide, (short)words[pc + 1]), true); break;
                    case 0x17: Write(a, new(ValueType.Wide, Bits32(words, pc + 1)), true); break;
                    case 0x18: Write(a, new(ValueType.Wide,
                        (uint)Bits32(words, pc + 1) | ((long)Bits32(words, pc + 3) << 32)), true); break;
                    case 0x19: Write(a, new(ValueType.Wide, (long)words[pc + 1] << 48), true); break;
                    case 0x1a: Write(a, new(ValueType.StringIndex, words[pc + 1])); break;
                    case 0x1b: Write(a, new(ValueType.StringIndex, (uint)Bits32(words, pc + 1))); break;
                    case 0x22: Write(a, default); break; // new-instance
                    case 0x62: Write(a, ReadTypeface(words[pc + 1])); break;
                    case 0x81: // int-to-long
                        var value = registers.GetValueOrDefault(a >> 4);
                        Write(a & 15, value.Type switch
                        {
                            ValueType.Int => new(ValueType.Wide, value.Data),
                            ValueType.Timeout => new(ValueType.WideTimeout, value.Data),
                            _ => default
                        }, true);
                        break;
                    default:
                        // Unknown effects discard all argument facts, never a best guess.
                        registers.Clear();
                        break;
                }
            }

            void Emit(string category, string value, params string?[] resources)
            {
                if (sites.Count >= MaxSites) { _partial = true; return; }
                var unavailable = body.Method.Name == "(name unavailable)" ||
                    body.Method.Signature.Contains("[type unavailable]", StringComparison.Ordinal);
                var locator = unavailable ? $"{body.Method.Locator} (DEX method #{body.Id})" : body.Method.Locator;
                sites.Add(new(category, $"{locator} +0x{pc:X}", value,
                    resources.OfType<string>().Distinct(StringComparer.Ordinal).ToArray()));
            }
        }
        return sites;

        void Move(int dest, int source, bool wide) => Write(dest, registers.GetValueOrDefault(source), wide);
        void Write(int register, Value value, bool wide = false)
        {
            if (register < 0 || register + (wide ? 1 : 0) >= code.Registers) throw Invalid();
            if (registers.GetValueOrDefault(register - 1).Type is ValueType.Wide or ValueType.WideTimeout)
                registers.Remove(register - 1);
            if (wide) registers.Remove(register + 1);
            if (value.Type == ValueType.None || wide != (value.Type is ValueType.Wide or ValueType.WideTimeout))
                registers.Remove(register);
            else
                registers[register] = value;
        }
    }

    private (int Duration, int Interval)? TimerArguments(uint id, Method called)
    {
        if (called.Owner == Timer) return called.Signature == "(JJ)V" ? (1, 3) : null;
        if (_parents.GetValueOrDefault(called.Owner) != Timer || !_bodies.TryGetValue(id, out var body))
            return null;
        if (_timerWrappers.TryGetValue(id, out var cached)) return cached;
        (int, int)? result = null;
        try
        {
            var code = ReadCode(body);
            if (code.Linear)
            {
                var first = code.Registers - code.Incoming;
                var longs = new HashSet<int>();
                var next = 1;
                foreach (var type in called.Parameters)
                {
                    if (type == "J") longs.Add(next);
                    next += WordCount(type);
                }
                if (code.Incoming != next) throw Invalid();
                foreach (var pc in code.Positions)
                {
                    var op = code.Words[pc] & 255;
                    if (op is 0 or 0x0e or >= 0x59 and <= 0x5f) continue; // nop, return, field stores
                    if (op is not (0x70 or 0x76) || result is not null)
                    {
                        result = null;
                        break;
                    }
                    var target = Reference(code.Words[pc + 1]);
                    var args = Arguments(code.Words, pc, code.Registers);
                    if (target is not { Owner: Timer, Name: "<init>", Signature: "(JJ)V" } || args.Length != 5 ||
                        args[0] != first || args[2] != args[1] + 1 || args[4] != args[3] + 1 ||
                        !longs.Contains(args[1] - first) || !longs.Contains(args[3] - first))
                        break;
                    result = (args[1] - first, args[3] - first);
                }
            }
        }
        catch (InvalidDataException) { _partial = true; }
        _timerWrappers.Add(id, result);
        return result;
    }

    private Method Reference(uint id)
    {
        if (_references.TryGetValue(id, out var saved)) return saved;
        var methods = Methods;
        if (id >= methods.Length / 8) throw Invalid();
        var entry = methods[((int)id * 8)..];
        var owner = TypeName(U16(entry, 0));
        var name = ReadName(dex, Strings, U32(entry, 4), allowSpaces: true) ?? "(name unavailable)";
        var prototype = ReadPrototype(U16(entry, 2));
        if (!owner.StartsWith('L') || !owner.EndsWith(';'))
            owner = "L[unavailable];"; // An array can be the owner of an unrelated method reference.
        var method = new Method(owner, name, prototype);
        _references.Add(id, method);
        return method;
    }

    private Prototype ReadPrototype(uint proto)
    {
        if (_prototypes.TryGetValue(proto, out var saved)) return saved;
        var table = Prototypes;
        if (proto >= table.Length / 12) throw Invalid();
        var entry = (int)proto * 12;
        var returnType = TypeName(U32(table, entry + 4));
        var parameterOffset = U32(table, entry + 8);
        string[] parameters = [];
        if (parameterOffset != 0)
        {
            var at = Offset(parameterOffset);
            if ((at & 3) != 0 || at > dex.Length - 4) throw Invalid();
            var count = U32(dex, at);
            if (count > 255 || at + 4L + count * 2L > dex.Length) throw Invalid();
            parameters = new string[count];
            for (var i = 0; i < parameters.Length; i++) parameters[i] = TypeName(U16(dex, at + 4 + 2 * i));
        }
        var prototype = new Prototype(parameters, returnType);
        _prototypes.Add(proto, prototype);
        return prototype;
    }

    private string TypeName(uint index)
    {
        if (_types.TryGetValue(index, out var saved)) return saved;
        var types = Types;
        if (index >= types.Length / 4) throw Invalid();
        var descriptor = ReadName(dex, Strings, U32(types, (int)index * 4), allowSpaces: true) ?? "[type unavailable]";
        _types.Add(index, descriptor);
        return descriptor;
    }

    private uint Leb(ref int at)
    {
        uint result = 0;
        for (var shift = 0; shift <= 28; shift += 7)
        {
            if (at >= dex.Length) throw Invalid();
            var b = dex[at++];
            if (shift == 28 && b > 15) throw Invalid();
            result |= (uint)(b & 127) << shift;
            if ((b & 128) == 0) return result;
        }
        throw Invalid();
    }

    // Android's standard instruction formats. Unknown or optimized-only opcodes
    // fail the method rather than risking a false instruction boundary.
    private static int Width(ushort[] words, int pc, int op)
    {
        if (op == 0 && words[pc] != 0)
        {
            if (pc + 1 >= words.Length || (pc & 1) != 0) throw Invalid();
            var count = words[pc + 1];
            return words[pc] switch
            {
                0x0100 => 4 + count * 2,
                0x0200 => 2 + count * 4,
                0x0300 when pc + 3 < words.Length && count is 1 or 2 or 4 or 8 =>
                    ArrayPayloadWidth(count, (uint)Bits32(words, pc + 2)),
                _ => throw Invalid()
            };
        }
        return op switch
        {
            0 or 1 or 4 or 7 or >= 0x0a and <= 0x12 or 0x1d or 0x1e or 0x21 or 0x27 or 0x28 or
                >= 0x7b and <= 0x8f or >= 0xb0 and <= 0xcf => 1,
            2 or 5 or 8 or 0x13 or 0x15 or 0x16 or 0x19 or 0x1a or 0x1c or 0x1f or 0x20 or 0x22 or 0x23 or
                0x29 or >= 0x2d and <= 0x3d or >= 0x44 and <= 0x6d or >= 0x90 and <= 0xaf or
                >= 0xd0 and <= 0xe2 or 0xfe or 0xff => 2,
            3 or 6 or 9 or 0x14 or 0x17 or 0x1b or >= 0x24 and <= 0x26 or >= 0x2a and <= 0x2c or
                >= 0x6e and <= 0x72 or >= 0x74 and <= 0x78 or 0xfc or 0xfd => 3,
            0xfa or 0xfb => 4,
            0x18 => 5,
            _ => throw Invalid()
        };
    }

    private static int ArrayPayloadWidth(int elementWidth, uint size)
    {
        var width = 4 + (elementWidth * (long)size + 1) / 2;
        return width <= 65536 ? (int)width : throw Invalid();
    }

    private static int[] Arguments(ushort[] words, int pc, int registers)
    {
        var word = words[pc];
        var range = (word & 255) is >= 0x74 and <= 0x78;
        var count = range ? word >> 8 : word >> 12;
        var source = words[pc + 2];
        if (range ? source + count > registers : count > 5) throw Invalid();
        var args = new int[count];
        for (var i = 0; i < count; i++)
        {
            args[i] = range ? source + i : i == 4 ? (word >> 8) & 15 : (source >> (i * 4)) & 15;
            if (args[i] >= registers) throw Invalid();
        }
        return args;
    }

    private bool IsActivity(string owner) => owner == Activity ||
        _parents.GetValueOrDefault(owner) == Activity &&
        !_declarations.Contains((owner, "setContentView", "(I)V")) &&
        !_declarations.Contains((owner, "setContentView", "(Landroid/view/View;)V"));

    private static bool IsView(string owner) => owner == "Landroid/view/View;" || IsTextView(owner);
    private static bool IsTextView(string owner) => owner is "Landroid/widget/TextView;" or "Landroid/widget/EditText;" or "Landroid/widget/Button;";
    private static string? SemanticsArgument(Method method, Value argument)
    {
        if (!IsView(method.Owner)) return null;
        if (method.Signature == "(Z)V" && method.Name is "setAccessibilityHeading" or "setScreenReaderFocusable")
            return argument is { Type: ValueType.Int, Data: 0 or 1 } ? (argument.Data == 1 ? "true" : "false") : "";
        if (method.Signature == "(Ljava/lang/CharSequence;)V" &&
            method.Name is "setContentDescription" or "setStateDescription" or "setAccessibilityPaneTitle" or "announceForAccessibility")
            return argument is { Type: ValueType.Int, Data: 0 } ? "null argument" : "";
        if (method.Signature != "(I)V") return null;
        var numeric = argument.Type == ValueType.Int ? argument.Data : -1;
        return method.Name switch
        {
            "setImportantForAccessibility" => numeric switch
            {
                0 => "auto", 1 => "yes", 2 => "no", 4 => "no, hide descendants", _ => ""
            },
            "setAccessibilityLiveRegion" => numeric switch { 0 => "none", 1 => "polite", 2 => "assertive", _ => "" },
            _ => null
        };
    }

    private Value ReadTypeface(uint id)
    {
        var fields = Table(dex, 80, 8, 65536);
        if (id >= fields.Length / 8) throw Invalid();
        var field = fields.Slice((int)id * 8, 8);
        if (TypeName(U16(field, 0)) != Typeface || TypeName(U16(field, 2)) != Typeface)
            return default;
        var name = ReadName(dex, Strings, U32(field, 4));
        for (var i = 0; i < 5; i++)
            if (name == PlatformTypeface(i)) return new(ValueType.PlatformTypeface, i);
        return default;
    }

    private static string? PlatformTypeface(int code) => code switch
    {
        0 => "DEFAULT", 1 => "DEFAULT_BOLD", 2 => "SANS_SERIF", 3 => "SERIF", 4 => "MONOSPACE", _ => null
    };

    private static string? TypefaceStyle(long code) => code switch
    {
        0 => "normal", 1 => "bold", 2 => "italic", 3 => "bold italic", _ => null
    };

    private static bool IsSafeLocalHtml(string path) => path.Length <= 200 && path.Split('/').All(part =>
        part.Length > 0 && part is not ("." or "..") && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')) &&
        (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase));

    private static string Describe(Method method, string? argument, string? resource) =>
        $"{method.Api}: {argument ?? "argument not resolved"}" +
        (resource is null ? "." : $"; receiver ID set or looked up as {resource} earlier in this method.");
    private static string? TextSize(Value unit, Value value)
    {
        if (unit.Type != ValueType.Int || value.Type != ValueType.Int) return null;
        var suffix = unit.Data switch { 0 => "px", 1 => "dp", 2 => "sp", 3 => "pt", 4 => "in", 5 => "mm", _ => null };
        var size = BitConverter.Int32BitsToSingle((int)value.Data);
        return suffix is not null && float.IsFinite(size) && size >= 0 ? size.ToString("R", CultureInfo.InvariantCulture) + suffix : null;
    }
    private static string Flags(long value)
    {
        if (value is < 1 or > 7) return Number(value);
        var names = new List<string>();
        if ((value & 1) != 0) names.Add("icons");
        if ((value & 2) != 0) names.Add("text");
        if ((value & 4) != 0) names.Add("controls");
        return $"{Number(value)} ({string.Join(", ", names)})";
    }
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string? Id(Value value, ValueType type = ValueType.Int) => value.Type == type &&
        ((uint)value.Data >> 24) is 1 or >= 0x7f && (((uint)value.Data >> 16) & 255) != 0
            ? $"@0x{(uint)value.Data:X8}" : null;
    private static int WordCount(string descriptor) => descriptor is "J" or "D" ? 2 : 1;
    private static int Bits32(ushort[] words, int at) => words[at] | (words[at + 1] << 16);
    private static int Offset(uint value) => value is >= 112 and <= int.MaxValue ? (int)value : throw Invalid();
}

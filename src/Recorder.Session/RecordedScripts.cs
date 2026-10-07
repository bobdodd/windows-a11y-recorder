using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// One script of a recorded document (slice 4h, protocol 0.54), from its
/// script-parsed record: a script V8 instantiated, or failed to compile, in
/// the document. The world is the record's, or null when it gives none. The
/// recording time is the record's. Size is the byte count of the UTF-8
/// source, whose text is the script-text record of <see cref="Digest"/>.
/// </summary>
public sealed record RecordedScriptState(
    string ScriptId,
    string Kind,
    JsonElement? World,
    string? Url,
    string? SourceUrl,
    string? SourceMapUrl,
    int? Line,
    int? Column,
    string? EvalFromScriptId,
    bool CompileError,
    string Digest,
    long Size,
    bool TextRecorded,
    long RecordedNanoseconds);

/// <summary>
/// The scripts of a recorded document at a recording time, in the order
/// they were recorded, and their texts by digest. Recorded is false when the
/// recording holds no script records, as before protocol 0.54. A text is
/// read only when it is asked for, and only when its bytes match its digest.
/// </summary>
public sealed class RecordedScripts
{
    private readonly Func<string, byte[]?> _bytes;
    private readonly IReadOnlySet<string> _texts;
    private readonly Dictionary<string, string?> _read = new(StringComparer.Ordinal);

    public RecordedScripts(
        IReadOnlyList<RecordedScriptState> scripts,
        bool recorded,
        IReadOnlySet<string> texts,
        Func<string, byte[]?> bytes)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(bytes);
        Scripts = scripts;
        Recorded = recorded;
        _texts = texts;
        _bytes = bytes;
    }

    public static RecordedScripts None { get; } =
        new([], false, new HashSet<string>(StringComparer.Ordinal), _ => null);

    public IReadOnlyList<RecordedScriptState> Scripts { get; }

    public bool Recorded { get; }

    /// <summary>Whether the recording holds the text of the digest at or before the time.</summary>
    public bool HasText(string digest) => _texts.Contains(digest);

    /// <summary>
    /// The text of a script, or null when its record is not held or its
    /// bytes do not match the digest.
    /// </summary>
    public string? Text(string digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (!_texts.Contains(digest))
        {
            return null;
        }
        lock (_read)
        {
            if (_read.TryGetValue(digest, out var cached))
            {
                return cached;
            }
            var bytes = _bytes(digest);
            var text = bytes is not null && Convert.ToHexStringLower(SHA256.HashData(bytes)) == digest
                ? new UTF8Encoding(false, false).GetString(bytes)
                : null;
            _read[digest] = text;
            return text;
        }
    }
}

/// <summary>
/// Reads the scripts of a recorded document (slice 4h) from its
/// script-parsed records and the script-text records of any renderer, given
/// in recording order up to the time asked for. A script is keyed by its
/// browser instance, renderer process, and script ID, and its first record
/// is kept.
/// </summary>
public sealed class RecordedScriptReader
{
    private readonly string _documentToken;
    private readonly List<RecordedScriptState> _scripts = [];
    private readonly HashSet<(string? Instance, long? Process, string ScriptId)> _seen = [];
    private readonly HashSet<string> _texts = new(StringComparer.Ordinal);
    private bool _recorded;

    /// <param name="documentKey">The document's state key, its token and identity.</param>
    public RecordedScriptReader(string documentKey)
    {
        ArgumentNullException.ThrowIfNull(documentKey);
        _documentToken = documentKey.Split(' ', 2)[0];
    }

    /// <summary>Takes one record, in the order recorded; returns whether it is a script-text record.</summary>
    public bool Add(RecorderEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var payload = record.Payload;
        if (record.Channel != BrowserEvidenceChannels.Script || payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        _recorded = true;
        if (record.EventType == BrowserEvidenceEventTypes.ScriptText)
        {
            if (Text(payload, "digest") is { } textDigest)
            {
                _texts.Add(textDigest);
                return true;
            }
            return false;
        }
        if (record.EventType != BrowserEvidenceEventTypes.ScriptParsed)
        {
            return false;
        }
        var context = payload.TryGetProperty("context", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
        if (Text(context, "documentToken") != _documentToken ||
            Text(payload, "scriptId") is not { } scriptId ||
            Text(payload, "kind") is not { } kind ||
            Text(payload, "digest") is not { } digest ||
            !_seen.Add((Text(context, "browserInstanceId"), Int64(context, "processId"), scriptId)))
        {
            return false;
        }
        var world = payload.TryGetProperty("world", out var recordedWorld) && recordedWorld.ValueKind == JsonValueKind.Object
            ? recordedWorld.Clone()
            : (JsonElement?)null;
        _scripts.Add(new RecordedScriptState(
            scriptId,
            kind,
            world,
            Text(payload, "url"),
            Text(payload, "sourceUrl"),
            Text(payload, "sourceMapUrl"),
            (int?)Int64(payload, "line"),
            (int?)Int64(payload, "column"),
            Text(payload, "evalFromScriptId"),
            Flag(payload, "compileError"),
            digest,
            long.TryParse(Text(payload, "size"), NumberStyles.None, CultureInfo.InvariantCulture, out var size) ? size : 0,
            Flag(payload, "textRecorded"),
            record.MonotonicNanoseconds));
        return false;
    }

    /// <param name="bytes">Reads the bytes of a script-text record by digest.</param>
    public RecordedScripts Build(Func<string, byte[]?> bytes) =>
        _recorded ? new RecordedScripts(_scripts.ToArray(), true, new HashSet<string>(_texts, StringComparer.Ordinal), bytes) : RecordedScripts.None;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Int64(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
}

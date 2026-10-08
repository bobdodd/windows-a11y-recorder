using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Recorder.Database.Evidence;

/// <summary>A payload the evidence model cannot store, and why.</summary>
internal sealed class EvidenceMappingException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

/// <summary>A name the mapper has not yet resolved to its key in the names table.</summary>
internal sealed record PendingName(string Value);

/// <summary>A row of a table, with its key columns first, after recording_id.</summary>
internal sealed record EvidenceRow(EvidenceTable Table, object?[] Values);

/// <summary>
/// A recurring value stored once per recording. It is written by the write
/// that claims it, the first to store an event referring to it, and is known
/// to be stored once that write commits. Its rows are then released.
/// </summary>
internal sealed class IdentityRow(EvidenceTable table, long key)
{
    public EvidenceTable Table { get; } = table;

    public long Key { get; } = key;

    public UInt128 ContentHash { get; init; }

    public List<EvidenceRow> Rows { get; } = [];

    public List<IdentityRow> Dependencies { get; } = [];

    public bool Stored { get; set; }

    /// <summary>The write that claimed the identity, while it is not stored.</summary>
    public WriteAttempt? Owner { get; set; }
}

/// <summary>
/// One call that writes a batch. Writes are numbered in the order they are
/// prepared; a write waits only for earlier writes, so waits cannot form a
/// cycle.
/// </summary>
internal sealed class WriteAttempt(long order)
{
    public long Order { get; } = order;

    /// <summary>The identities this write claimed while no write had.</summary>
    public HashSet<IdentityRow> Fresh { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Completed when the write has committed or failed.</summary>
    public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>The rows that store one event's payload.</summary>
internal sealed class EvidenceRows
{
    public List<EvidenceRow> Rows { get; } = [];

    /// <summary>Every identity the rows refer to, stored or not.</summary>
    public List<IdentityRow> Identities { get; } = [];
}

/// <summary>
/// Maps payloads into rows of the evidence tables for one recording. A
/// payload member the catalog does not describe, or a value of the wrong
/// type, refuses the event rather than dropping the member.
/// </summary>
internal sealed class EvidenceMapper
{
    private readonly Dictionary<(EvidenceTable Table, UInt128 Hash), IdentityRow> _identities = [];
    private readonly Dictionary<EvidenceTable, long> _nextKeys;

    /// <param name="nextKeys">
    /// The next unused identity key of each identity table in the recording.
    /// </param>
    public EvidenceMapper(IReadOnlyDictionary<EvidenceTable, long> nextKeys)
    {
        _nextKeys = new Dictionary<EvidenceTable, long>(nextKeys);
    }

    /// <summary>
    /// Maps a payload. Names in the returned rows, including the rows of new
    /// identities, are <see cref="PendingName"/> values until resolved.
    /// </summary>
    public EvidenceRows Map(EvidenceTable table, long eventKey, JsonElement payload)
    {
        var result = new EvidenceRows();
        var created = new List<IdentityRow>();
        try
        {
            AddRow(table, [eventKey], payload, "payload", result.Rows, result.Identities, created);
        }
        catch (EvidenceMappingException)
        {
            // An identity first seen in a refused payload is not kept, so a
            // later payload does not refer to a row that was never checked.
            foreach (var identity in created)
            {
                _identities.Remove((identity.Table, identity.ContentHash));
            }

            throw;
        }

        return result;
    }

    private void AddRow(
        EvidenceTable table,
        object?[] keys,
        JsonElement value,
        string path,
        List<EvidenceRow> rows,
        List<IdentityRow> identities,
        List<IdentityRow> created)
    {
        var values = new object?[keys.Length + table.Columns.Count];
        keys.CopyTo(values, 0);
        var index = keys.Length;
        var row = new EvidenceRow(table, values);
        rows.Add(row);
        var context = new RowContext(table, values, keys, rows, identities, created);
        if (table.ScalarItem)
        {
            var field = table.Fields.Single();
            var present = value.ValueKind != JsonValueKind.Null;
            if (!present && field.Presence == Presence.Required)
            {
                throw new EvidenceMappingException($"payload-member-null:{path}");
            }

            Fill(field, value, present, true, path, context, ref index, string.Empty);
        }
        else
        {
            FillObject(table.Fields, value, true, path, context, ref index, string.Empty, checkMembers: true);
        }
    }

    private void FillObject(
        IReadOnlyList<Field> fields,
        JsonElement value,
        bool present,
        string path,
        RowContext context,
        ref int index,
        string prefix,
        bool checkMembers)
    {
        if (present && value.ValueKind != JsonValueKind.Object)
        {
            throw new EvidenceMappingException($"payload-member-type:{path}");
        }

        if (present && checkMembers)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (!Describes(fields, property.Name))
                {
                    throw new EvidenceMappingException($"payload-member-unmapped:{path}/{property.Name}");
                }
            }
        }

        foreach (var field in fields)
        {
            if (field is GroupField group)
            {
                context.Values[index++] = present
                    ? Identity(group.Identity, value, path, context, group: true).Key
                    : null;
                continue;
            }

            JsonElement member = default;
            var memberPresent = present && Member(value, field, path, out member);
            Fill(field, member, memberPresent, present, $"{path}/{field.Json}", context, ref index, prefix);
        }
    }

    private void Fill(
        Field field,
        JsonElement value,
        bool present,
        bool enclosingPresent,
        string path,
        RowContext context,
        ref int index,
        string prefix)
    {
        var values = context.Values;
        switch (field)
        {
            case ScalarField { Type: ScalarType.Utc }:
                if (present)
                {
                    var instant = ReadUtc(value, path);
                    var remainder = instant.UtcTicks % 10;
                    values[index++] = new DateTime(instant.UtcTicks - remainder, DateTimeKind.Utc);
                    values[index++] = (short)remainder;
                }
                else
                {
                    index += 2;
                }

                break;
            case ScalarField scalar:
                values[index++] = present ? ReadScalar(scalar.Type, value, path) : null;
                break;
            case NameField:
                values[index++] = present ? new PendingName(ReadText(value, path)) : null;
                break;
            case ArrayField array:
                values[index++] = present ? ReadArray(array.Element, value, path) : null;
                break;
            case IdentityField identity:
                values[index++] = present
                    ? Identity(identity.Identity, value, path, context, group: false).Key
                    : null;
                break;
            case InlineField inline:
                if (EvidenceLayout.HasPresenceColumn(inline))
                {
                    values[index++] = enclosingPresent ? present : null;
                }

                FillObject(
                    inline.Fields,
                    value,
                    present,
                    path,
                    context,
                    ref index,
                    prefix + inline.Column + "_",
                    checkMembers: true);
                break;
            case ListField list:
                if (EvidenceLayout.HasPresenceColumn(list))
                {
                    values[index++] = enclosingPresent ? present : null;
                }

                if (present)
                {
                    if (value.ValueKind != JsonValueKind.Array)
                    {
                        throw new EvidenceMappingException($"payload-member-type:{path}");
                    }

                    var ordinal = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        AddRow(
                            list.Child,
                            [.. context.Keys, ordinal],
                            item,
                            $"{path}/{ordinal.ToString(CultureInfo.InvariantCulture)}",
                            context.Rows,
                            context.Identities,
                            context.Created);
                        ordinal++;
                    }
                }

                break;
            case MapField map:
                if (EvidenceLayout.HasPresenceColumn(map))
                {
                    values[index++] = enclosingPresent ? present : null;
                }

                if (present)
                {
                    if (value.ValueKind != JsonValueKind.Object)
                    {
                        throw new EvidenceMappingException($"payload-member-type:{path}");
                    }

                    foreach (var entry in value.EnumerateObject())
                    {
                        AddRow(
                            map.Child,
                            [.. context.Keys, new PendingName(Clean(entry.Name, path))],
                            entry.Value,
                            $"{path}/{entry.Name}",
                            context.Rows,
                            context.Identities,
                            context.Created);
                    }
                }

                break;
            default:
                throw new InvalidOperationException($"Unknown member kind {field.GetType().Name}.");
        }
    }

    private IdentityRow Identity(
        EvidenceTable table,
        JsonElement value,
        string path,
        RowContext context,
        bool group)
    {
        var raw = group ? GroupText(table, value) : value.GetRawText();
        var key = (table, Hash(table, raw));
        if (!_identities.TryGetValue(key, out var identity))
        {
            identity = new IdentityRow(table, _nextKeys.GetValueOrDefault(table)) { ContentHash = key.Item2 };
            _nextKeys[table] = identity.Key + 1;
            _identities[key] = identity;
            context.Created.Add(identity);
            var nested = new List<IdentityRow>();
            if (group)
            {
                var values = new object?[1 + table.Columns.Count];
                values[0] = identity.Key;
                var index = 1;
                identity.Rows.Add(new EvidenceRow(table, values));
                FillObject(
                    table.Fields,
                    value,
                    true,
                    path,
                    new RowContext(table, values, [identity.Key], identity.Rows, nested, context.Created),
                    ref index,
                    string.Empty,
                    checkMembers: false);
            }
            else
            {
                AddRow(table, [identity.Key], value, path, identity.Rows, nested, context.Created);
            }

            identity.Dependencies.AddRange(nested);
        }

        context.Identities.Add(identity);
        return identity;
    }

    private static string GroupText(EvidenceTable table, JsonElement value)
    {
        var builder = new StringBuilder();
        foreach (var field in table.Fields)
        {
            builder.Append(field.Json).Append('=');
            builder.Append(value.TryGetProperty(field.Json, out var member) ? member.GetRawText() : "~");
            builder.Append('\u001f');
        }

        return builder.ToString();
    }

    private static UInt128 Hash(EvidenceTable table, string raw)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(table.Name + "\u001f" + raw), digest);
        return new UInt128(
            BitConverter.ToUInt64(digest[..8]),
            BitConverter.ToUInt64(digest[8..16]));
    }

    private static bool Describes(IReadOnlyList<Field> fields, string name)
    {
        foreach (var field in fields)
        {
            if (field is GroupField group)
            {
                if (group.Identity.Fields.Any(member => member.Json == name))
                {
                    return true;
                }
            }
            else if (field.Json == name)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Member(JsonElement value, Field field, string path, out JsonElement member)
    {
        if (!value.TryGetProperty(field.Json, out member))
        {
            if (field.Presence == Presence.Optional)
            {
                return false;
            }

            throw new EvidenceMappingException($"payload-member-missing:{path}/{field.Json}");
        }

        if (member.ValueKind == JsonValueKind.Null)
        {
            if (field.Presence == Presence.Required)
            {
                throw new EvidenceMappingException($"payload-member-null:{path}/{field.Json}");
            }

            return false;
        }

        return true;
    }

    private static object ReadScalar(ScalarType type, JsonElement value, string path)
    {
        switch (type)
        {
            case ScalarType.Text:
                return ReadText(value, path);
            case ScalarType.Integer when value.ValueKind == JsonValueKind.Number &&
                                         value.TryGetInt32(out var integer):
                return integer;
            case ScalarType.BigInt when value.ValueKind == JsonValueKind.Number &&
                                        value.TryGetInt64(out var bigint):
                return bigint;
            case ScalarType.Double when value.ValueKind == JsonValueKind.Number &&
                                        value.TryGetDouble(out var number) &&
                                        double.IsFinite(number):
                return number;
            case ScalarType.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return value.GetBoolean();
            default:
                throw new EvidenceMappingException($"payload-member-type:{path}");
        }
    }

    private static object ReadArray(ScalarType element, JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new EvidenceMappingException($"payload-member-type:{path}");
        }

        var length = value.GetArrayLength();
        var integers = element == ScalarType.Integer ? new int[length] : null;
        var bigints = element == ScalarType.BigInt ? new long[length] : null;
        var doubles = element == ScalarType.Double ? new double[length] : null;
        var ordinal = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = $"{path}/{ordinal.ToString(CultureInfo.InvariantCulture)}";
            if (item.ValueKind == JsonValueKind.Null)
            {
                throw new EvidenceMappingException($"payload-member-null:{itemPath}");
            }

            var read = ReadScalar(element, item, itemPath);
            if (integers is not null)
            {
                integers[ordinal] = (int)read;
            }
            else if (doubles is not null)
            {
                doubles[ordinal] = (double)read;
            }
            else
            {
                bigints![ordinal] = (long)read;
            }

            ordinal++;
        }

        return (object?)integers ?? (object?)doubles ?? bigints!;
    }

    private static string ReadText(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new EvidenceMappingException($"payload-member-type:{path}");
        }

        return Clean(value.GetString()!, path);
    }

    // PostgreSQL text cannot hold U+0000, so a payload string holding it is
    // refused rather than stored altered.
    private static string Clean(string text, string path) =>
        text.Contains('\0', StringComparison.Ordinal)
            ? throw new EvidenceMappingException($"payload-text-nul:{path}")
            : text;

    private static DateTimeOffset ReadUtc(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var instant))
        {
            return instant;
        }

        throw new EvidenceMappingException($"payload-member-type:{path}");
    }

    private sealed record RowContext(
        EvidenceTable Table,
        object?[] Values,
        object?[] Keys,
        List<EvidenceRow> Rows,
        List<IdentityRow> Identities,
        List<IdentityRow> Created);
}


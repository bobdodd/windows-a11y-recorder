using System.Globalization;
using System.Text;

namespace Recorder.Database.Evidence;

/// <summary>How a payload member may appear.</summary>
internal enum Presence
{
    /// <summary>Present and not null.</summary>
    Required,

    /// <summary>Present, and may be null. Rebuilt as null when null.</summary>
    Nullable,

    /// <summary>May be absent, and may be null. Rebuilt as absent when null.</summary>
    Optional
}

/// <summary>The stored type of a scalar member.</summary>
internal enum ScalarType
{
    Text,
    Integer,
    BigInt,
    Double,
    Boolean,

    /// <summary>
    /// A date-time string, stored as timestamptz to the microsecond with the
    /// remaining tenth of a microsecond in a second column, and rebuilt in
    /// UTC in the form System.Text.Json writes.
    /// </summary>
    Utc
}

internal enum TableKind
{
    /// <summary>One row per event, keyed by the event.</summary>
    Evidence,

    /// <summary>
    /// A value that recurs across events, stored once per recording and
    /// referenced by key.
    /// </summary>
    Identity,

    /// <summary>The items of a list or the entries of a map.</summary>
    Child
}

/// <summary>A payload member and how it is stored.</summary>
internal abstract class Field(string json, Presence presence)
{
    /// <summary>The JSON property name, or empty for a list item or map value itself.</summary>
    public string Json { get; } = json;

    public Presence Presence { get; } = presence;

    /// <summary>The column name, or the column name prefix of an inline object.</summary>
    public virtual string Column => EvidenceNaming.Snake(Json);
}

/// <summary>A string, number, or boolean stored in a typed column.</summary>
internal sealed class ScalarField(string json, ScalarType type, Presence presence, string? column = null)
    : Field(json, presence)
{
    public ScalarType Type { get; } = type;

    public override string Column { get; } = column ?? EvidenceNaming.Snake(json);
}

/// <summary>
/// A string from a small or recurring vocabulary, such as an enumeration
/// value, a class name, or a header name, stored once in the names table and
/// referenced by key.
/// </summary>
internal sealed class NameField(string json, Presence presence, string? column = null)
    : Field(json, presence)
{
    public override string Column { get; } = column ?? EvidenceNaming.Snake(json) + "_name_id";
}

/// <summary>
/// A nested object whose members are columns of the owning table, named with
/// the object's prefix. An object that is not required has a has_ column.
/// </summary>
internal sealed class InlineField(string json, Presence presence, IReadOnlyList<Field> fields, string? column = null)
    : Field(json, presence)
{
    public IReadOnlyList<Field> Fields { get; } = fields;

    public override string Column { get; } = column ?? EvidenceNaming.Snake(json);
}

/// <summary>
/// A nested object, or a string, stored once per recording in an identity
/// table and referenced by key.
/// </summary>
internal sealed class IdentityField(string json, Presence presence, EvidenceTable identity, string? column = null)
    : Field(json, presence)
{
    public EvidenceTable Identity { get; } = identity;

    public override string Column { get; } = column ?? EvidenceNaming.Snake(json) + "_key";
}

/// <summary>
/// Members of the owning object that together describe one recurring
/// entity, stored once per recording in an identity table and referenced by
/// key. The members stay members of the owning object in the payload.
/// </summary>
internal sealed class GroupField(string column, EvidenceTable identity)
    : Field(string.Empty, Presence.Required)
{
    public EvidenceTable Identity { get; } = identity;

    public override string Column { get; } = column;
}

/// <summary>An array, stored as rows of a child table in array order.</summary>
internal sealed class ListField(string json, Presence presence, EvidenceTable child)
    : Field(json, presence)
{
    public EvidenceTable Child { get; } = child;
}

/// <summary>
/// An object whose property names are data, stored as rows of a child table
/// with the property name in the names table.
/// </summary>
internal sealed class MapField(string json, Presence presence, EvidenceTable child)
    : Field(json, presence)
{
    public EvidenceTable Child { get; } = child;
}

/// <summary>A stored column of an evidence table.</summary>
internal sealed record EvidenceColumn(
    string Name,
    string SqlType,
    bool NotNull,
    string? References = null);

/// <summary>
/// A table of the evidence model: the events of one or more event types, a
/// recurring identity, or the items of a list.
/// </summary>
internal sealed class EvidenceTable
{
    private IReadOnlyList<EvidenceColumn>? _columns;

    public EvidenceTable(
        string name,
        TableKind kind,
        IReadOnlyList<Field> fields,
        bool scalarItem = false,
        bool mapEntry = false)
    {
        Name = name;
        Kind = kind;
        Fields = fields;
        ScalarItem = scalarItem;
        MapEntry = mapEntry;
        if (name.Length > 63)
        {
            throw new InvalidOperationException($"Table name {name} is longer than PostgreSQL allows.");
        }
    }

    public string Name { get; }

    public TableKind Kind { get; }

    public IReadOnlyList<Field> Fields { get; }

    /// <summary>
    /// The table whose rows own this child table's rows, set when the
    /// catalog is built.
    /// </summary>
    public EvidenceTable? Owner { get; private set; }

    /// <summary>
    /// For a child table, how many ordinals key its rows: one for a list of
    /// the owner, two for a list inside a list item, and so on. A map entry
    /// is keyed by its name instead of an ordinal at its own level.
    /// </summary>
    public int Depth => Owner is null ? 0 : Owner.Kind == TableKind.Child ? Owner.Depth + 1 : 1;

    /// <summary>Names this table as the owner of its child tables, recursively.</summary>
    public void BindChildren()
    {
        foreach (var child in Children)
        {
            if (child.Owner is not null && child.Owner != this)
            {
                throw new InvalidOperationException($"Child table {child.Name} has two owners.");
            }

            if (Kind == TableKind.Child && MapEntry)
            {
                throw new InvalidOperationException($"Map entry table {Name} cannot own a child table.");
            }

            child.Owner = this;
            child.BindChildren();
        }
    }

    /// <summary>
    /// The value is the JSON value itself rather than an object: a list
    /// item or map value that is a string or number, or a text identity.
    /// </summary>
    public bool ScalarItem { get; }

    /// <summary>A child table holding map entries, keyed by name.</summary>
    public bool MapEntry { get; }

    /// <summary>The key column of an evidence or identity table.</summary>
    public string KeyColumn => Kind switch
    {
        TableKind.Evidence => "event_key",
        TableKind.Identity => "identity_key",
        _ => "owner_key"
    };

    /// <summary>The key columns a child row's parent is found by.</summary>
    public IReadOnlyList<string> ParentKeyColumns =>
        ["owner_key", .. Enumerable.Range(1, Depth - 1).Select(Ordinal)];

    /// <summary>The key columns of this table, after recording_id.</summary>
    public IReadOnlyList<string> KeyColumns => Kind == TableKind.Child
        ? [.. ParentKeyColumns, MapEntry ? "entry_name_id" : Ordinal(Depth)]
        : [KeyColumn];

    /// <summary>The owner table's key columns the parent key refers to.</summary>
    public IReadOnlyList<string> OwnerKeyColumns => Owner!.KeyColumns;

    /// <summary>The data columns, after the key columns, in stored order.</summary>
    public IReadOnlyList<EvidenceColumn> Columns => _columns ??= EvidenceLayout.Columns(this);

    /// <summary>The child tables of lists and maps in this table's members.</summary>
    public IEnumerable<EvidenceTable> Children => EvidenceLayout.Children(Fields);

    public static string Ordinal(int level) =>
        "ordinal_" + level.ToString(CultureInfo.InvariantCulture);

    public override string ToString() => Name;
}

internal static class EvidenceNaming
{
    /// <summary>Converts a camelCase JSON name to a snake_case column name.</summary>
    public static string Snake(string json)
    {
        var builder = new StringBuilder(json.Length + 8);
        foreach (var character in json)
        {
            if (char.IsUpper(character))
            {
                if (builder.Length > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// The column layout of a table, in the order the mapper fills a row. The
/// mapper and the SQL builders walk members in this same order.
/// </summary>
internal static class EvidenceLayout
{
    public static IReadOnlyList<EvidenceColumn> Columns(EvidenceTable table)
    {
        var columns = new List<EvidenceColumn>();
        if (table.ScalarItem)
        {
            var field = table.Fields.Single();
            Add(field, string.Empty, true, columns);
        }
        else
        {
            foreach (var field in table.Fields)
            {
                Add(field, string.Empty, true, columns);
            }
        }

        var tooLong = columns.FirstOrDefault(column => column.Name.Length > 63);
        if (tooLong is not null)
        {
            throw new InvalidOperationException($"Column {tooLong.Name} of table {table.Name} is longer than PostgreSQL allows.");
        }

        var duplicate = columns.GroupBy(column => column.Name).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Table {table.Name} has two columns named {duplicate.Key}.");
        }

        return columns;
    }

    /// <summary>The has_ column of a member that is not required.</summary>
    public static string PresenceColumn(string prefix, Field field) =>
        "has_" + prefix + field.Column;

    public static bool HasPresenceColumn(Field field) =>
        field is InlineField or ListField or MapField && field.Presence != Presence.Required;

    private static void Add(Field field, string prefix, bool parentRequired, List<EvidenceColumn> columns)
    {
        var notNull = parentRequired && field.Presence == Presence.Required;
        switch (field)
        {
            case ScalarField scalar:
                columns.Add(new EvidenceColumn(prefix + scalar.Column, SqlType(scalar.Type), notNull));
                if (scalar.Type == ScalarType.Utc)
                {
                    columns.Add(new EvidenceColumn(prefix + scalar.Column + "_tick", "smallint", notNull));
                }

                break;
            case NameField name:
                columns.Add(new EvidenceColumn(prefix + name.Column, "integer", notNull, "names (name_id)"));
                break;
            case IdentityField identity:
                columns.Add(new EvidenceColumn(prefix + identity.Column, "bigint", notNull, identity.Identity.Name));
                break;
            case GroupField group:
                columns.Add(new EvidenceColumn(prefix + group.Column, "bigint", parentRequired, group.Identity.Name));
                break;
            case InlineField inline:
                var required = parentRequired && inline.Presence == Presence.Required;
                if (HasPresenceColumn(inline))
                {
                    columns.Add(new EvidenceColumn(PresenceColumn(prefix, inline), "boolean", parentRequired));
                }

                foreach (var member in inline.Fields)
                {
                    Add(member, prefix + inline.Column + "_", required, columns);
                }

                break;
            case ListField or MapField:
                if (HasPresenceColumn(field))
                {
                    columns.Add(new EvidenceColumn(PresenceColumn(prefix, field), "boolean", parentRequired));
                }

                break;
            default:
                throw new InvalidOperationException($"Unknown member kind {field.GetType().Name}.");
        }
    }

    public static IEnumerable<EvidenceTable> Children(IEnumerable<Field> fields)
    {
        foreach (var field in fields)
        {
            switch (field)
            {
                case ListField list:
                    yield return list.Child;
                    break;
                case MapField map:
                    yield return map.Child;
                    break;
                case InlineField inline:
                    foreach (var child in Children(inline.Fields))
                    {
                        yield return child;
                    }

                    break;
            }
        }
    }

    public static string SqlType(ScalarType type) => type switch
    {
        ScalarType.Text => "text",
        ScalarType.Integer => "integer",
        ScalarType.BigInt => "bigint",
        ScalarType.Double => "double precision",
        ScalarType.Boolean => "boolean",
        ScalarType.Utc => "timestamptz",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}

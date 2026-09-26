using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Recorder.Database.Evidence;

/// <summary>
/// The SQL generated from the evidence catalog: the expressions that
/// rebuild a payload as jsonb.
/// </summary>
internal static class EvidenceSql
{
    // jsonb_build_object takes at most 100 arguments.
    private const int PairsPerObject = 40;

    private static readonly ConcurrentDictionary<EvidenceTable, string> PayloadExpressions = new();

    /// <summary>
    /// The jsonb expression that rebuilds the payload held by the row of
    /// <paramref name="table"/> aliased t.
    /// </summary>
    public static string PayloadExpression(EvidenceTable table) =>
        PayloadExpressions.GetOrAdd(table, key => new ExpressionBuilder().Row(key, "t", null));

    /// <summary>
    /// The jsonb expression that rebuilds only the named top-level payload
    /// members of the row aliased t.
    /// </summary>
    public static string ProjectionExpression(EvidenceTable table, IReadOnlySet<string> properties) =>
        new ExpressionBuilder().Row(table, "t", properties);

    private sealed class ExpressionBuilder
    {
        private int _aliases;

        public string Row(EvidenceTable table, string alias, IReadOnlySet<string>? properties)
        {
            if (table.ScalarItem)
            {
                return Value(table.Fields.Single(), alias, string.Empty, table);
            }

            return ObjectOf(table.Fields, alias, string.Empty, table, properties);
        }

        private string ObjectOf(
            IReadOnlyList<Field> fields,
            string alias,
            string prefix,
            EvidenceTable table,
            IReadOnlySet<string>? properties)
        {
            var parts = new List<string>();
            var pairs = new List<string>();

            void Flush()
            {
                for (var start = 0; start < pairs.Count; start += PairsPerObject)
                {
                    parts.Add("jsonb_build_object(" +
                        string.Join(", ", pairs.Skip(start).Take(PairsPerObject)) + ")");
                }

                pairs.Clear();
            }

            foreach (var field in fields)
            {
                if (field is GroupField group)
                {
                    var members = group.Identity.Fields.Where(member =>
                        properties is null || properties.Contains(member.Json)).ToArray();
                    if (members.Length == 0)
                    {
                        continue;
                    }

                    var inner = NextAlias();
                    Flush();
                    parts.Add(
                        $"coalesce((SELECT {ObjectOf(members, inner, string.Empty, group.Identity, null)} " +
                        $"FROM {group.Identity.Name} {inner} WHERE {inner}.recording_id = {alias}.recording_id " +
                        $"AND {inner}.identity_key = {alias}.{prefix}{group.Column}), '{{}}'::jsonb)");
                    continue;
                }

                if (properties is not null && !properties.Contains(field.Json))
                {
                    continue;
                }

                var value = Value(field, alias, prefix, table);
                if (field.Presence == Presence.Optional)
                {
                    Flush();
                    parts.Add(
                        $"CASE WHEN ({value}) IS NULL THEN '{{}}'::jsonb " +
                        $"ELSE jsonb_build_object('{field.Json}', {value}) END");
                }
                else
                {
                    pairs.Add($"'{field.Json}', {value}");
                }
            }

            Flush();
            return parts.Count switch
            {
                0 => "'{}'::jsonb",
                1 => parts[0],
                _ => "(" + string.Join(" || ", parts) + ")"
            };
        }

        private string Value(Field field, string alias, string prefix, EvidenceTable table)
        {
            var column = $"{alias}.{prefix}{field.Column}";
            switch (field)
            {
                case ScalarField { Type: ScalarType.Utc }:
                    // The form System.Text.Json writes a UTC DateTimeOffset in:
                    // up to seven fractional digits, without trailing zeros.
                    return $"(to_char({column} AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS') || " +
                        $"rtrim(rtrim('.' || to_char({column} AT TIME ZONE 'UTC', 'US') || {column}_tick::text, " +
                        "'0'), '.') || '+00:00')";
                case ScalarField:
                    return column;
                case NameField:
                    var name = NextAlias();
                    return $"(SELECT {name}.name FROM names {name} WHERE {name}.name_id = {column})";
                case IdentityField identity:
                    var inner = NextAlias();
                    return $"(SELECT {Row(identity.Identity, inner, null)} FROM {identity.Identity.Name} {inner} " +
                        $"WHERE {inner}.recording_id = {alias}.recording_id AND {inner}.identity_key = {column})";
                case InlineField inline:
                    var body = ObjectOf(inline.Fields, alias, prefix + inline.Column + "_", table, null);
                    return Guarded(field, alias, prefix, body);
                case ListField list:
                    var item = NextAlias();
                    return Guarded(
                        field,
                        alias,
                        prefix,
                        $"(SELECT coalesce(jsonb_agg({Row(list.Child, item, null)} " +
                        $"ORDER BY {item}.{EvidenceTable.Ordinal(list.Child.Depth)}), '[]'::jsonb) " +
                        $"FROM {list.Child.Name} {item} WHERE {ChildJoin(list.Child, item, alias)})");
                case MapField map:
                    var entry = NextAlias();
                    var entryName = NextAlias();
                    return Guarded(
                        field,
                        alias,
                        prefix,
                        $"(SELECT coalesce(jsonb_object_agg((SELECT {entryName}.name FROM names {entryName} " +
                        $"WHERE {entryName}.name_id = {entry}.entry_name_id), {Row(map.Child, entry, null)}), " +
                        $"'{{}}'::jsonb) FROM {map.Child.Name} {entry} WHERE {ChildJoin(map.Child, entry, alias)})");
                default:
                    throw new InvalidOperationException($"Unknown member kind {field.GetType().Name}.");
            }
        }

        private static string Guarded(Field field, string alias, string prefix, string body) =>
            EvidenceLayout.HasPresenceColumn(field)
                ? $"CASE WHEN {alias}.{EvidenceLayout.PresenceColumn(prefix, field)} THEN {body} END"
                : body;

        private static string ChildJoin(EvidenceTable child, string childAlias, string ownerAlias)
        {
            var conditions = new List<string> { $"{childAlias}.recording_id = {ownerAlias}.recording_id" };
            var parent = child.ParentKeyColumns;
            var owner = child.OwnerKeyColumns;
            for (var index = 0; index < parent.Count; index++)
            {
                conditions.Add($"{childAlias}.{parent[index]} = {ownerAlias}.{owner[index]}");
            }

            return string.Join(" AND ", conditions);
        }

        private string NextAlias() => "a" + (++_aliases).ToString(CultureInfo.InvariantCulture);
    }
}

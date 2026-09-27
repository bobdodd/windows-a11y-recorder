using System.Collections.Concurrent;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Recorder.Database.Evidence;

/// <summary>Writes evidence rows with binary COPY, and reads identity key state.</summary>
internal static class EvidenceCopy
{
    private static readonly ConcurrentDictionary<EvidenceTable, (string Sql, NpgsqlDbType[] Types)> Statements = new();

    private static readonly Dictionary<EvidenceTable, int> TableOrder = EvidenceCatalog.Tables
        .Select((table, index) => (table, index))
        .ToDictionary(pair => pair.table, pair => pair.index);

    /// <summary>The next unused key of each identity table in a recording.</summary>
    public static async Task<Dictionary<EvidenceTable, long>> NextIdentityKeysAsync(
        NpgsqlConnection connection,
        Guid recordingId,
        CancellationToken cancellationToken)
    {
        var identities = EvidenceCatalog.Tables.Where(table => table.Kind == TableKind.Identity).ToArray();
        var keys = new Dictionary<EvidenceTable, long>();

        // A database opened by the recorder has every table, since its
        // migrations are applied first. The upgrade tests write recordings
        // into the schema an earlier release left, which lacks the tables of
        // later migrations; such a table has no keys yet.
        await using (var present = new NpgsqlCommand(
            "SELECT name FROM unnest($1::text[]) name WHERE to_regclass(name) IS NOT NULL",
            connection))
        {
            present.Parameters.AddWithValue(identities.Select(table => table.Name).ToArray());
            var names = new HashSet<string>(StringComparer.Ordinal);
            await using (var presentReader = await present.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await presentReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    names.Add(presentReader.GetString(0));
                }
            }

            identities = [.. identities.Where(table => names.Contains(table.Name))];
        }

        await using var command = new NpgsqlCommand(
            string.Join(
                " UNION ALL ",
                identities.Select((table, index) =>
                    $"SELECT {index}, coalesce(max(identity_key) + 1, 0) FROM {table.Name} WHERE recording_id = $1")),
            connection);
        command.Parameters.AddWithValue(recordingId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys[identities[reader.GetInt32(0)]] = reader.GetInt64(1);
        }

        return keys;
    }

    /// <summary>
    /// The identities the events refer to that are not yet stored, each once
    /// and after the identities it refers to.
    /// </summary>
    public static List<IdentityRow> UnstoredIdentities(IEnumerable<EvidenceRows> events)
    {
        var ordered = new List<IdentityRow>();
        var seen = new HashSet<IdentityRow>(ReferenceEqualityComparer.Instance);

        void Visit(IdentityRow identity)
        {
            if (identity.Stored || !seen.Add(identity))
            {
                return;
            }

            foreach (var dependency in identity.Dependencies)
            {
                Visit(dependency);
            }

            ordered.Add(identity);
        }

        foreach (var rows in events)
        {
            foreach (var identity in rows.Identities)
            {
                Visit(identity);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Copies rows table by table in catalog order, so every row is written
    /// after the rows it refers to.
    /// </summary>
    public static async Task CopyAsync(
        NpgsqlConnection connection,
        Guid recordingId,
        IEnumerable<EvidenceRow> rows,
        CancellationToken cancellationToken,
        WriterTimings? timings = null)
    {
        foreach (var group in rows.GroupBy(row => row.Table).OrderBy(group => TableOrder[group.Key]))
        {
            var copying = System.Diagnostics.Stopwatch.GetTimestamp();
            var count = 0;
            var (sql, types) = Statements.GetOrAdd(group.Key, Statement);
            await using var importer = await connection.BeginBinaryImportAsync(sql, cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in group)
            {
                count++;
                importer.StartRow();
                importer.Write(recordingId, NpgsqlDbType.Uuid);
                for (var index = 0; index < types.Length; index++)
                {
                    Write(importer, row.Values[index], types[index]);
                }
            }

            await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            timings?.Since("copy." + group.Key.Name, copying, count);
        }
    }

    /// <summary>
    /// Writes rows that may already be stored, keeping the stored ones. Each
    /// table's rows are copied into a temporary table and inserted from it in
    /// key order, so two transactions writing the same rows wait on them in
    /// the same order and cannot deadlock.
    /// </summary>
    public static async Task CopyIgnoringConflictsAsync(
        NpgsqlConnection connection,
        Guid recordingId,
        IEnumerable<EvidenceRow> rows,
        CancellationToken cancellationToken)
    {
        foreach (var group in rows.GroupBy(row => row.Table).OrderBy(group => TableOrder[group.Key]))
        {
            var table = group.Key;
            var staged = string.Create(CultureInfo.InvariantCulture, $"staged_rows_{TableOrder[table]}");
            var columns = "recording_id, " + string.Join(", ", table.KeyColumns.Concat(table.Columns.Select(column => column.Name)));
            await using (var create = new NpgsqlCommand(
                $"CREATE TEMPORARY TABLE {staged} (LIKE {table.Name}) ON COMMIT DROP",
                connection))
            {
                await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var (sql, types) = Statements.GetOrAdd(table, Statement);
            await using (var importer = await connection.BeginBinaryImportAsync(
                "COPY " + staged + sql[(sql.IndexOf(' ', 5))..],
                cancellationToken).ConfigureAwait(false))
            {
                foreach (var row in group)
                {
                    importer.StartRow();
                    importer.Write(recordingId, NpgsqlDbType.Uuid);
                    for (var index = 0; index < types.Length; index++)
                    {
                        Write(importer, row.Values[index], types[index]);
                    }
                }

                await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var insert = new NpgsqlCommand(
                $"INSERT INTO {table.Name} ({columns}) SELECT {columns} FROM {staged} " +
                $"ORDER BY {string.Join(", ", table.KeyColumns)} ON CONFLICT DO NOTHING; DROP TABLE {staged}",
                connection);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static (string, NpgsqlDbType[]) Statement(EvidenceTable table)
    {
        var names = new List<string>(table.KeyColumns);
        var types = new List<NpgsqlDbType>(table.KeyColumns.Select(column =>
            column is "event_key" or "identity_key" or "owner_key" ? NpgsqlDbType.Bigint : NpgsqlDbType.Integer));
        foreach (var column in table.Columns)
        {
            names.Add(column.Name);
            types.Add(column.SqlType switch
            {
                "text" => NpgsqlDbType.Text,
                "integer" => NpgsqlDbType.Integer,
                "bigint" => NpgsqlDbType.Bigint,
                "double precision" => NpgsqlDbType.Double,
                "boolean" => NpgsqlDbType.Boolean,
                "timestamptz" => NpgsqlDbType.TimestampTz,
                "smallint" => NpgsqlDbType.Smallint,
                "integer[]" => NpgsqlDbType.Array | NpgsqlDbType.Integer,
                "bigint[]" => NpgsqlDbType.Array | NpgsqlDbType.Bigint,
                _ => throw new InvalidOperationException($"Unknown column type {column.SqlType}.")
            });
        }

        return (
            $"COPY {table.Name} (recording_id, {string.Join(", ", names)}) FROM STDIN (FORMAT BINARY)",
            [.. types]);
    }

    // Rows are written synchronously: the importer buffers them.
    private static void Write(NpgsqlBinaryImporter importer, object? value, NpgsqlDbType type)
    {
        switch (value)
        {
            case null:
                importer.WriteNull();
                break;
            case string text:
                importer.Write(text, type);
                break;
            case int integer:
                importer.Write(integer, type);
                break;
            case long bigint:
                importer.Write(bigint, type);
                break;
            case short small:
                importer.Write(small, type);
                break;
            case double number:
                importer.Write(number, type);
                break;
            case bool flag:
                importer.Write(flag, type);
                break;
            case DateTime instant:
                importer.Write(instant, type);
                break;
            case int[] integers:
                importer.Write(integers, type);
                break;
            case long[] bigints:
                importer.Write(bigints, type);
                break;
            default:
                throw new InvalidOperationException($"Cannot write a {value.GetType().Name} evidence value.");
        }
    }
}

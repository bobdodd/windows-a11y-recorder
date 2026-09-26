using System.Text.RegularExpressions;
using Npgsql;

namespace Recorder.Database;

/// <summary>
/// Finishes migration 0009: moves each recording's rows out of the
/// partitions of the legacy tables into the ordinary tables, then drops the
/// legacy tables.
/// </summary>
/// <remarks>
/// Dropping thousands of partitions in one transaction locks each of them
/// and every object that depends on them, which is more than the server's
/// lock table holds. Each step here is its own transaction and locks only
/// what it changes: first the foreign keys of the legacy tables are dropped,
/// one at a time, so that no partition depends on another; then each
/// recording's rows are copied and its partitions dropped, one recording at
/// a time; then the empty legacy tables are dropped. Every step can be
/// repeated, so an interrupted run continues the next time the database
/// opens.
/// </remarks>
internal static partial class LegacyPartitions
{
    /// <summary>The migration whose legacy tables this empties.</summary>
    public const int MigrationVersion = 9;

    public static async Task MoveAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (await ScalarAsync(connection, null, "SELECT to_regclass('legacy_partitioned_tables') IS NULL", cancellationToken)
                .ConfigureAwait(false) is true)
        {
            return;
        }

        while (await ScalarAsync(
                   connection,
                   null,
                   "SELECT format('ALTER TABLE %I DROP CONSTRAINT %I', l.legacy_table_name, c.conname) " +
                   "FROM pg_constraint c " +
                   "JOIN legacy_partitioned_tables l ON c.conrelid = to_regclass(l.legacy_table_name) " +
                   "WHERE c.contype = 'f' AND c.conparentid = 0 LIMIT 1",
                   cancellationToken).ConfigureAwait(false) is string dropForeignKey)
        {
            await InTransactionAsync(connection, dropForeignKey, cancellationToken).ConfigureAwait(false);
        }

        while (await ScalarAsync(
                   connection,
                   null,
                   "SELECT substr(c.relname, length(l.partition_prefix) + 2) " +
                   "FROM legacy_partitioned_tables l " +
                   "JOIN pg_inherits i ON i.inhparent = to_regclass(l.legacy_table_name) " +
                   "JOIN pg_class c ON c.oid = i.inhrelid LIMIT 1",
                   cancellationToken).ConfigureAwait(false) is string recording)
        {
            // The suffix is the recording key in 32 hexadecimal digits, as
            // RecordingStore named the partitions; it is checked before it
            // becomes part of the statement.
            if (!RecordingSuffix().IsMatch(recording))
            {
                throw new InvalidOperationException($"A legacy partition has the unexpected suffix {recording}.");
            }

            await InTransactionAsync(connection, MoveRecording(recording), cancellationToken).ConfigureAwait(false);
        }

        await InTransactionAsync(
            connection,
            """
            DO $$
            DECLARE
                legacy record;
            BEGIN
                FOR legacy IN SELECT legacy_table_name FROM legacy_partitioned_tables ORDER BY partition_order DESC
                LOOP
                    EXECUTE format('DROP TABLE IF EXISTS %I', legacy.legacy_table_name);
                END LOOP;

                DROP TABLE legacy_partitioned_tables;
            END
            $$;
            """,
            cancellationToken).ConfigureAwait(false);
    }

    // Copies the recording's rows from each partition into the ordinary
    // table, referenced tables first, then drops the partitions.
    private static string MoveRecording(string recording) =>
        $$"""
        DO $$
        DECLARE
            legacy record;
            partition text;
            columns text;
        BEGIN
            FOR legacy IN SELECT * FROM legacy_partitioned_tables ORDER BY partition_order
            LOOP
                partition := legacy.partition_prefix || '_{{recording}}';
                IF to_regclass(partition) IS NOT NULL THEN
                    SELECT string_agg(quote_ident(a.attname), ', ' ORDER BY a.attnum)
                    INTO columns
                    FROM pg_attribute a
                    WHERE a.attrelid = to_regclass(legacy.table_name) AND a.attnum > 0 AND NOT a.attisdropped;
                    EXECUTE format('INSERT INTO %I (%s) SELECT %s FROM %I', legacy.table_name, columns, columns, partition);
                END IF;
            END LOOP;

            FOR legacy IN SELECT * FROM legacy_partitioned_tables ORDER BY partition_order DESC
            LOOP
                partition := legacy.partition_prefix || '_{{recording}}';
                IF to_regclass(partition) IS NOT NULL THEN
                    EXECUTE format('DROP TABLE %I', partition);
                END IF;
            END LOOP;
        END
        $$;
        """;

    private static async Task InTransactionAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue(DatabaseMigrator.AdvisoryLockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 })
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is DBNull ? null : value;
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex RecordingSuffix();
}

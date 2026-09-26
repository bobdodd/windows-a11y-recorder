using System.Globalization;
using System.Reflection;
using Npgsql;

namespace Recorder.Database;

/// <summary>
/// Applies the schema migrations embedded in this assembly, in order, each
/// in its own transaction. A migration that has been applied is not applied
/// again.
/// </summary>
public static class DatabaseMigrator
{
    internal const long AdvisoryLockKey = 0x5245434F52444552; // "RECORDER"

    public static IReadOnlyList<(int Version, string Name)> Migrations { get; } =
        typeof(DatabaseMigrator).Assembly
            .GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.Ordinal) &&
                name.Contains(".Migrations.", StringComparison.Ordinal))
            .Select(name =>
            {
                var file = name[(name.IndexOf(".Migrations.", StringComparison.Ordinal) +
                    ".Migrations.".Length)..];
                var version = int.Parse(
                    file[..file.IndexOf('_', StringComparison.Ordinal)],
                    CultureInfo.InvariantCulture);
                return (version, name);
            })
            .OrderBy(migration => migration.version)
            .ToArray();

    public static Task ApplyAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(dataSource, int.MaxValue, cancellationToken);

    /// <summary>
    /// Applies the migrations up to and including a version, so a test can
    /// build the schema an earlier release left and then upgrade it.
    /// </summary>
    internal static async Task ApplyAsync(
        NpgsqlDataSource dataSource,
        int throughVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            null,
            "CREATE TABLE IF NOT EXISTS schema_migrations (" +
            "version integer PRIMARY KEY, " +
            "applied_utc timestamptz NOT NULL DEFAULT now())",
            cancellationToken).ConfigureAwait(false);

        foreach (var (version, resource) in Migrations.Where(migration => migration.Version <= throughVersion))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using (var lockCommand = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock($1)",
                connection,
                transaction))
            {
                lockCommand.Parameters.AddWithValue(AdvisoryLockKey);
                await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var applied = new NpgsqlCommand(
                "SELECT 1 FROM schema_migrations WHERE version = $1",
                connection,
                transaction))
            {
                applied.Parameters.AddWithValue(version);
                if (await applied.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                    is not null)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            await ExecuteAsync(connection, transaction, ReadResource(resource), cancellationToken)
                .ConfigureAwait(false);
            await using (var record = new NpgsqlCommand(
                "INSERT INTO schema_migrations (version) VALUES ($1)",
                connection,
                transaction))
            {
                record.Parameters.AddWithValue(version);
                await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        await LegacyPartitions.MoveAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Migration {name} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        // No time limit: a migration that rewrites stored recordings, such as
        // 0009, takes as long as the data it moves.
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

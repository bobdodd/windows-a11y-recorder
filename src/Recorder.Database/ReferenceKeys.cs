using Npgsql;

namespace Recorder.Database;

/// <summary>
/// Finds or adds rows in the closed-vocabulary tables and caches their keys.
/// Each lookup commits on its own, so a key stays valid if a later batch
/// transaction rolls back.
/// </summary>
internal sealed class ReferenceKeys(NpgsqlDataSource dataSource)
{
    public const string Channels = "channels";
    public const string EventTypes = "event_types";
    public const string QualityFlags = "quality_flags";
    public const string EventSchemaVersions = "event_schema_versions";
    public const string TimestampDomains = "timestamp_domains";
    public const string TimestampUnits = "timestamp_units";

    private static readonly Dictionary<string, string> KeyColumns = new(StringComparer.Ordinal)
    {
        [Channels] = "channel_id",
        [EventTypes] = "event_type_id",
        [QualityFlags] = "quality_flag_id",
        [EventSchemaVersions] = "event_schema_version_id",
        [TimestampDomains] = "timestamp_domain_id",
        [TimestampUnits] = "timestamp_unit_id"
    };

    private readonly Dictionary<(string Table, string Name), int> _names = [];
    private readonly Dictionary<(string Type, string Version, string Method), int> _collectorKinds = [];

    public async Task<int> GetAsync(string table, string name, CancellationToken cancellationToken)
    {
        if (_names.TryGetValue((table, name), out var key))
        {
            return key;
        }

        var column = KeyColumns[table];
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"WITH added AS (INSERT INTO {table} (name) VALUES ($1) " +
            $"ON CONFLICT (name) DO NOTHING RETURNING {column}) " +
            $"SELECT {column} FROM added UNION ALL " +
            $"SELECT {column} FROM {table} WHERE name = $1 LIMIT 1",
            connection);
        command.Parameters.AddWithValue(name);
        key = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        _names[(table, name)] = key;
        return key;
    }

    public async Task<int> GetCollectorKindAsync(
        string collectorType,
        string producerVersion,
        string captureMethod,
        CancellationToken cancellationToken)
    {
        var id = (collectorType, producerVersion, captureMethod);
        if (_collectorKinds.TryGetValue(id, out var key))
        {
            return key;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "WITH added AS (INSERT INTO collector_kinds " +
            "(collector_type, producer_version, capture_method) VALUES ($1, $2, $3) " +
            "ON CONFLICT (collector_type, producer_version, capture_method) DO NOTHING " +
            "RETURNING collector_kind_id) " +
            "SELECT collector_kind_id FROM added UNION ALL " +
            "SELECT collector_kind_id FROM collector_kinds " +
            "WHERE collector_type = $1 AND producer_version = $2 AND capture_method = $3 LIMIT 1",
            connection);
        command.Parameters.AddWithValue(collectorType);
        command.Parameters.AddWithValue(producerVersion);
        command.Parameters.AddWithValue(captureMethod);
        key = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        _collectorKinds[id] = key;
        return key;
    }

    /// <summary>
    /// Finds or adds a collector instance in a recording. Returns its key and
    /// the collector kind it was first registered with.
    /// </summary>
    public static async Task<(int RecordingCollectorId, int CollectorKindId)> GetRecordingCollectorAsync(
        NpgsqlDataSource dataSource,
        Guid recordingId,
        string instanceId,
        int collectorKindId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "WITH added AS (INSERT INTO recording_collectors " +
            "(recording_id, instance_id, collector_kind_id) VALUES ($1, $2, $3) " +
            "ON CONFLICT (recording_id, instance_id) DO NOTHING " +
            "RETURNING recording_collector_id, collector_kind_id) " +
            "SELECT recording_collector_id, collector_kind_id FROM added UNION ALL " +
            "SELECT recording_collector_id, collector_kind_id FROM recording_collectors " +
            "WHERE recording_id = $1 AND instance_id = $2 LIMIT 1",
            connection);
        command.Parameters.AddWithValue(recordingId);
        command.Parameters.AddWithValue(instanceId);
        command.Parameters.AddWithValue(collectorKindId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    public static async Task<int> GetClockMappingAsync(
        NpgsqlDataSource dataSource,
        Guid recordingId,
        string name,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "WITH added AS (INSERT INTO clock_mappings (recording_id, name) VALUES ($1, $2) " +
            "ON CONFLICT (recording_id, name) DO NOTHING RETURNING clock_mapping_id) " +
            "SELECT clock_mapping_id FROM added UNION ALL " +
            "SELECT clock_mapping_id FROM clock_mappings " +
            "WHERE recording_id = $1 AND name = $2 LIMIT 1",
            connection);
        command.Parameters.AddWithValue(recordingId);
        command.Parameters.AddWithValue(name);
        return (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }
}

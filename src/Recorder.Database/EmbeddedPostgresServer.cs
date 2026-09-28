using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Recorder.Database;

/// <param name="BinaryDirectory">
/// The directory holding the PostgreSQL programs (`initdb`, `pg_ctl`, and
/// `postgres`) the app ships.
/// </param>
/// <param name="DataDirectory">
/// The directory the app owns for the database: the cluster, the protected
/// password, and the server log.
/// </param>
public sealed record EmbeddedPostgresOptions(
    string BinaryDirectory,
    string DataDirectory,
    IDatabaseSecretProtector SecretProtector)
{
    public string DatabaseName { get; init; } = "recorder";
    public string UserName { get; init; } = "recorder";
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The loopback port to listen on. When null, a free port is chosen each
    /// time the server starts.
    /// </summary>
    public int? Port { get; init; }

    /// <summary>
    /// The server's shared buffers, in megabytes, set each time the server
    /// starts. When null, <see cref="EmbeddedPostgresServer.DefaultSharedBuffersMegabytes"/>
    /// chooses them from the machine's memory.
    /// </summary>
    public int? SharedBuffersMegabytes { get; init; }
}

/// <summary>
/// The PostgreSQL server the app installs and owns. It listens only on the
/// loopback interface, on a port chosen when it starts, and authenticates the
/// app with a password generated when the cluster is created.
/// </summary>
public sealed class EmbeddedPostgresServer : IAsyncDisposable
{
    private const string ClusterDirectoryName = "cluster";
    private const string SecretFileName = "password.protected";
    private const string LogFileName = "server.log";

    private readonly EmbeddedPostgresOptions _options;
    private readonly bool _startedByThisInstance;
    private bool _disposed;

    private EmbeddedPostgresServer(
        EmbeddedPostgresOptions options,
        int port,
        string password,
        bool startedByThisInstance)
    {
        _options = options;
        Port = port;
        _startedByThisInstance = startedByThisInstance;
        ConnectionString = new NpgsqlConnectionStringBuilder
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            Username = options.UserName,
            Password = password,
            Database = options.DatabaseName,
            ApplicationName = "windows-a11y-recorder"
        }.ConnectionString;
        DataSource = NpgsqlDataSource.Create(ConnectionString);
    }

    public int Port { get; }
    public string ConnectionString { get; }
    public NpgsqlDataSource DataSource { get; }

    public string DataDirectory => _options.DataDirectory;
    public string ClusterDirectory => Path.Combine(_options.DataDirectory, ClusterDirectoryName);
    public string LogPath => Path.Combine(_options.DataDirectory, LogFileName);

    /// <summary>
    /// Creates the cluster on first use, starts the server or attaches to one
    /// already running from this data directory, creates the database, and
    /// applies migrations.
    /// </summary>
    public static async Task<EmbeddedPostgresServer> StartAsync(
        EmbeddedPostgresOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Directory.CreateDirectory(options.DataDirectory);
        var cluster = Path.Combine(options.DataDirectory, ClusterDirectoryName);
        var secretPath = Path.Combine(options.DataDirectory, SecretFileName);

        if (!File.Exists(Path.Combine(cluster, "PG_VERSION")))
        {
            await InitializeClusterAsync(options, cluster, secretPath, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!File.Exists(secretPath))
        {
            throw new InvalidOperationException(
                $"The database in {options.DataDirectory} has no stored password, " +
                "so the app cannot connect to it.");
        }

        var password = Encoding.UTF8.GetString(
            options.SecretProtector.Unprotect(
                await File.ReadAllBytesAsync(secretPath, cancellationToken)
                    .ConfigureAwait(false)));

        var runningPort = await FindRunningPortAsync(options, cluster, cancellationToken)
            .ConfigureAwait(false);
        var startedHere = runningPort is null;
        var port = runningPort ?? await StartClusterAsync(options, cluster, cancellationToken)
            .ConfigureAwait(false);

        var server = new EmbeddedPostgresServer(options, port, password, startedHere);
        try
        {
            await server.EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);
            await DatabaseMigrator.ApplyAsync(server.DataSource, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return server;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await DataSource.DisposeAsync().ConfigureAwait(false);
        NpgsqlConnection.ClearAllPools();
        var result = await RunAsync(
            _options,
            "pg_ctl",
            ["stop", "-D", ClusterDirectory, "-m", "fast", "-w", "-t", "60"],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The database server did not stop: {result.Output}");
        }
    }

    /// <summary>
    /// Stops the server if this instance started it. A server this instance
    /// attached to is left running for its owner.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_startedByThisInstance)
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
                return;
            }
            catch (InvalidOperationException)
            {
            }
        }

        await DataSource.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task InitializeClusterAsync(
        EmbeddedPostgresOptions options,
        string cluster,
        string secretPath,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(cluster) && Directory.EnumerateFileSystemEntries(cluster).Any())
        {
            throw new InvalidOperationException(
                $"{cluster} is not an initialized database cluster and is not empty.");
        }

        var password = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var passwordFile = Path.Combine(
            options.DataDirectory,
            $"initdb-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(passwordFile, password, cancellationToken)
                .ConfigureAwait(false);
            var result = await RunAsync(
                options,
                "initdb",
                [
                    "-D", cluster,
                    "-U", options.UserName,
                    "--pwfile", passwordFile,
                    "--auth", "scram-sha-256",
                    "--encoding", "UTF8",
                    "--no-locale"
                ],
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The database could not be created: {result.Output}");
            }
        }
        finally
        {
            File.Delete(passwordFile);
        }

        // Loopback TCP only. The port is given each time the server starts.
        await File.AppendAllTextAsync(
            Path.Combine(cluster, "postgresql.conf"),
            Environment.NewLine +
            "# Set by Windows A11y Recorder." + Environment.NewLine +
            "listen_addresses = '127.0.0.1'" + Environment.NewLine +
            "unix_socket_directories = ''" + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);

        await File.WriteAllBytesAsync(
            secretPath,
            options.SecretProtector.Protect(Encoding.UTF8.GetBytes(password)),
            cancellationToken).ConfigureAwait(false);
    }

    // A server left running by an earlier app instance is reused. PostgreSQL
    // writes its port as the fourth line of postmaster.pid.
    private static async Task<int?> FindRunningPortAsync(
        EmbeddedPostgresOptions options,
        string cluster,
        CancellationToken cancellationToken)
    {
        var pidFile = Path.Combine(cluster, "postmaster.pid");
        if (!File.Exists(pidFile))
        {
            return null;
        }

        var status = await RunAsync(
            options,
            "pg_ctl",
            ["status", "-D", cluster],
            cancellationToken).ConfigureAwait(false);
        if (status.ExitCode != 0)
        {
            return null;
        }

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(pidFile, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            return null;
        }

        return lines.Length >= 4 && int.TryParse(lines[3].Trim(), out var port)
            ? port
            : null;
    }

    /// <summary>
    /// Chooses the server's shared buffers from the machine's memory: one
    /// eighth of it, at least the 128 MB PostgreSQL's own setup chooses and at
    /// most 4 GB. PostgreSQL suggests a quarter of memory as a starting point
    /// for a server that has a machine to itself; the recorder's server
    /// shares its machine with the app, the browser under test, and the
    /// other programs of a test session, so it takes half of that.
    /// </summary>
    public static int DefaultSharedBuffersMegabytes(long memoryBytes) =>
        (int)Math.Clamp(memoryBytes / 8 / (1024 * 1024), 128, 4096);

    private static async Task<int> StartClusterAsync(
        EmbeddedPostgresOptions options,
        string cluster,
        CancellationToken cancellationToken)
    {
        var log = Path.Combine(options.DataDirectory, LogFileName);
        var sharedBuffers = options.SharedBuffersMegabytes ??
            DefaultSharedBuffersMegabytes(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        string? lastFailure = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var port = options.Port ?? FreeLoopbackPort();
            // pg_ctl's output is not redirected: on Windows the server process
            // inherits pg_ctl's handles, and reading a redirected stream would
            // wait until the server exits. Failures are read from the log.
            var exitCode = await RunDetachedAsync(
                options,
                "pg_ctl",
                [
                    "start",
                    "-D", cluster,
                    "-l", log,
                    "-w",
                    "-t", ((int)options.StartTimeout.TotalSeconds).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    "-o", $"-p {port} -c shared_buffers={sharedBuffers}MB"
                ],
                cancellationToken).ConfigureAwait(false);
            if (exitCode == 0)
            {
                return port;
            }

            lastFailure = ReadLogTail(log);
        }

        throw new InvalidOperationException(
            $"The database server did not start: {lastFailure}");
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var exists = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = $1",
            connection);
        exists.Parameters.AddWithValue(_options.DatabaseName);
        if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE {QuoteIdentifier(_options.DatabaseName)} " +
                "ENCODING 'UTF8' TEMPLATE template0",
                connection);
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

    }

    private static string QuoteIdentifier(string name) =>
        "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static int FreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string ReadLogTail(string log)
    {
        try
        {
            using var stream = new FileStream(
                log,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n');
            return string.Join('\n', lines.TakeLast(20)).Trim();
        }
        catch (IOException)
        {
            return "no server log was written";
        }
    }

    private static string Executable(EmbeddedPostgresOptions options, string name)
    {
        var path = Path.Combine(
            options.BinaryDirectory,
            OperatingSystem.IsWindows() ? name + ".exe" : name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The PostgreSQL program {name} was not found in {options.BinaryDirectory}.",
                path);
        }

        return path;
    }

    private static ProcessStartInfo StartInfo(
        EmbeddedPostgresOptions options,
        string program,
        IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(Executable(options, program))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        EmbeddedPostgresOptions options,
        string program,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var info = StartInfo(options, program, arguments);
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"{program} could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, ((await output) + (await error)).Trim());
    }

    private static async Task<int> RunDetachedAsync(
        EmbeddedPostgresOptions options,
        string program,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(StartInfo(options, program, arguments))
            ?? throw new InvalidOperationException($"{program} could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

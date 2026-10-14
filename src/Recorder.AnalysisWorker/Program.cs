using System.Runtime.Versioning;
using Recorder.Analysis;
using Recorder.Database;

// The analysis worker: runs the analysis queue in the catalog until it is
// empty, then exits. One runs at a time for each Windows user; a second
// exits at once. See docs/architecture/analysis-runner.md.

[assembly: SupportedOSPlatform("windows")]

using var mutex = new Mutex(initiallyOwned: true, AnalysisLocation.WorkerMutexName, out var createdNew);
if (!createdNew)
{
    return 0;
}

var (settings, problems) = AnalysisSettingsFile.Read(AnalysisSettingsFile.DefaultPath);
string? loaded = null;
void Status(string message) =>
    WorkerStatus.Write(
        AnalysisLocation.StatusPath,
        new WorkerStatus(message, loaded, problems, DateTime.UtcNow, Environment.ProcessId));

Status("Starting.");
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    shutdown.Cancel();
};

EmbeddedPostgresServer? server = null;
async Task<Npgsql.NpgsqlDataSource> ConnectAsync(CancellationToken cancellationToken)
{
    if (server is not null)
    {
        await server.DisposeAsync().ConfigureAwait(false);
    }

    server = await EmbeddedPostgresServer.StartAsync(RecorderDatabaseLocation.ForCurrentUser(), cancellationToken)
        .ConfigureAwait(false);
    return server.DataSource;
}

using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
await using var localServer = new LocalServerHost(settings, AnalysisLocation.Directory, client);
var catalog = new AnalysisCatalog(ConnectAsync);
AnalysisWorker? worker = null;
worker = new AnalysisWorker(
    catalog,
    settings,
    AnalysisTaskKinds.All,
    new HttpModelEndpoint(client, new CurrentUserApiKeyProtector()),
    localServer,
    status: message =>
    {
        loaded = worker?.LoadedProfile;
        Status(message);
    });

var exitCode = 0;
try
{
    await worker.RunUntilEmptyAsync(shutdown.Token).ConfigureAwait(false);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
}
#pragma warning disable CA1031 // The worker reports any failure in its status file before exiting.
catch (Exception exception)
#pragma warning restore CA1031
{
    loaded = null;
    Status($"Stopped with an error: {exception.Message}");
    exitCode = 1;
}
finally
{
    if (server is not null)
    {
        await server.DisposeAsync().ConfigureAwait(false);
    }
}

if (exitCode == 0)
{
    loaded = null;
    Status("Stopped: the queue is empty.");
}

return exitCode;

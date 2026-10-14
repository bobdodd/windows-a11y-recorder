using System.Runtime.Versioning;
using Recorder.Analysis;
using Recorder.Database;

// The analysis worker: runs the analysis queue in the catalog until it is
// empty, then exits. One runs at a time for each Windows user; a second
// exits at once. See docs/architecture/analysis-runner.md.

[assembly: SupportedOSPlatform("windows")]

// With --trial TASK STEP FILE [OPTION=VALUE ...], the worker instead tries
// one step of a task that has run, with the sampling options given, and
// writes what it gave to FILE; nothing is written to the catalog. See
// docs/architecture/analysis-runner.md, "Trying a step with other sampling
// options".
(Guid Task, string Step, string File, Dictionary<string, double> Sampling)? trial = null;
if (args.Length > 0 && args[0] == "--trial")
{
    var sampling = new Dictionary<string, double>(StringComparer.Ordinal);
    if (args.Length < 4 || !Guid.TryParse(args[1], out var trialTask))
    {
        Console.Error.WriteLine("Give: --trial TASK STEP FILE [OPTION=VALUE ...], for example --trial 01a13a7e-... visit-0001-moment-10 C:\\Users\\Public\\Downloads\\trial.txt presence_penalty=1.5");
        return 2;
    }

    foreach (var pair in args.Skip(4))
    {
        var parts = pair.Split('=', 2);
        if (parts.Length != 2 || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            Console.Error.WriteLine($"Not an option and a number: {pair}");
            return 2;
        }

        sampling[parts[0]] = value;
    }

    trial = (trialTask, args[2], args[3], sampling);
}

using var mutex = new Mutex(initiallyOwned: true, AnalysisLocation.WorkerMutexName, out var createdNew);
if (!createdNew)
{
    if (trial is not null)
    {
        Console.Error.WriteLine("The analysis worker is running. Try again when the player says it is stopped.");
        return 2;
    }

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
    if (trial is { } plan)
    {
        Console.WriteLine($"Trying step {plan.Step}. This can take some minutes.");
        var result = await worker.TrialAsync(plan.Task, plan.Step, plan.Sampling, shutdown.Token).ConfigureAwait(false);
        await File.WriteAllTextAsync(plan.File, result.Describe(), new System.Text.UTF8Encoding(false), shutdown.Token).ConfigureAwait(false);
        Console.WriteLine($"Written: {plan.File}");
        Status("Stopped: the trial has run.");
        return 0;
    }

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
    if (trial is not null)
    {
        Console.Error.WriteLine($"The trial stopped with an error: {exception.Message}");
    }

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

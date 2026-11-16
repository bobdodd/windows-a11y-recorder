using System.Runtime.Versioning;
using Recorder.Analysis;
using Recorder.Database;

// The analysis worker: runs the analysis queue in the catalog until it is
// empty, then exits. One runs at a time for each Windows user; a second
// exits at once. See docs/architecture/analysis-runner.md.

[assembly: SupportedOSPlatform("windows")]

// With --trial TASK STEP FILE [OPTION=VALUE ...], the worker instead tries
// one step of a task that has run, with the sampling options given, and
// writes what it gave to FILE; nothing is written to the catalog. The
// options may include temperature and seed, and --one-line, which holds the
// answer to JSON on one line by the recorder's own grammar. See
// docs/architecture/analysis-runner.md, "Trying a step with other sampling
// options".
(Guid Task, string Step, string File, Dictionary<string, double> Sampling, bool OneLine, string? Profile, bool? Thinking)? trial = null;
if (args.Length > 0 && args[0] == "--trial")
{
    var sampling = new Dictionary<string, double>(StringComparer.Ordinal);
    if (args.Length < 4 || !Guid.TryParse(args[1], out var trialTask))
    {
        Console.Error.WriteLine("Give: --trial TASK STEP FILE [OPTION=VALUE ...] [profile=NAME] [thinking=on|off] [--one-line], for example --trial 01a13a7e-... visit-0001-moment-10 C:\\Users\\Public\\Downloads\\trial.txt presence_penalty=1.5");
        return 2;
    }

    var oneLine = false;
    string? trialProfile = null;
    bool? thinking = null;
    foreach (var pair in args.Skip(4))
    {
        if (pair == "--one-line")
        {
            oneLine = true;
            continue;
        }

        var parts = pair.Split('=', 2);

        // Another profile of the analysis settings for the step, and its
        // thinking on or off, to compare models on the same request.
        if (parts.Length == 2 && parts[0] == "profile" && parts[1].Length > 0)
        {
            trialProfile = parts[1];
            continue;
        }

        if (parts.Length == 2 && parts[0] == "thinking")
        {
            if (parts[1] is not ("on" or "off"))
            {
                Console.Error.WriteLine($"thinking is on or off, not {parts[1]}");
                return 2;
            }

            thinking = parts[1] == "on";
            continue;
        }

        if (parts.Length != 2 || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            Console.Error.WriteLine($"Not an option and a number: {pair}");
            return 2;
        }

        sampling[parts[0]] = value;
    }

    trial = (trialTask, args[2], args[3], sampling, oneLine, trialProfile, thinking);
}

// With --recreation-check RECORDING TIME FOLDER [CHROMIUM], the worker
// instead rebuilds the page at the last captured frame at or before TIME in
// the recording in the folder RECORDING, opens it in the instrumented
// Chromium with a window and then without one, and writes a picture of the
// whole page each time and a report to FOLDER; it does not use the catalog,
// so it may run while the worker runs. See
// docs/architecture/recording-summary.md, decision 7.
if (args.Length > 0 && args[0] == "--recreation-check")
{
    if (args.Length is < 4 or > 5 || !Recorder.Analysis.Summary.RecreationCheck.TryParseTime(args[2], out var checkTime))
    {
        Console.Error.WriteLine("Give: --recreation-check RECORDING TIME FOLDER [CHROMIUM], for example --recreation-check \"C:\\Users\\User\\Documents\\Windows A11y Recorder\\20261013-173155-...\" 00:00:48.950 C:\\Users\\Public\\Downloads\\recreation-check");
        return 2;
    }

    var chromium = args.Length == 5 ? args[4] : Recorder.Recreation.RecreationBrowser.DefaultExecutablePath(AppContext.BaseDirectory);
    try
    {
        Console.WriteLine("Rebuilding the page with a window, then without one. A window opens for a while.");
        var written = await Recorder.Analysis.Summary.RecreationCheck.RunAsync(
            args[1],
            checkTime,
            args[3],
            chromium,
            Path.Combine(AnalysisLocation.Directory, "recreation-check"),
            CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Written: {written}");
        return 0;
    }
#pragma warning disable CA1031 // The check reports any failure before exiting.
    catch (Exception exception)
#pragma warning restore CA1031
    {
        Console.Error.WriteLine($"The check stopped with an error: {exception.Message}");
        return 1;
    }
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
        var result = await worker.TrialAsync(plan.Task, plan.Step, plan.Sampling, shutdown.Token, plan.OneLine, plan.Profile, plan.Thinking).ConfigureAwait(false);
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

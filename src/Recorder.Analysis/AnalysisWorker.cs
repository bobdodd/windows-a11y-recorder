using System.Security.Cryptography;

namespace Recorder.Analysis;

/// <summary>
/// One kind of analysis. Its code runs from the start each time the worker
/// takes the task up; steps already accepted are given back from the
/// catalog without asking the model again, so a task interrupted by a
/// recording, a model switch, or a restart carries on where it stopped.
/// </summary>
public interface IAnalysisTaskKind
{
    string Kind { get; }

    int Version { get; }

    Task RunAsync(AnalysisTaskContext context, CancellationToken cancellationToken);
}

/// <summary>What a task kind's code is given.</summary>
public sealed class AnalysisTaskContext
{
    private readonly AnalysisWorker _worker;

    internal AnalysisTaskContext(AnalysisWorker worker, AnalysisTask task)
    {
        _worker = worker;
        Task = task;
    }

    public AnalysisTask Task { get; }

    public AnalysisCatalog Catalog => _worker.Catalog;

    /// <summary>
    /// Runs one model step, or gives back its accepted attempt. A step
    /// whose input has changed since it was accepted runs again.
    /// </summary>
    public Task<AcceptedStep> RunStepAsync(string stepKey, ModelRequest request, CancellationToken cancellationToken) =>
        _worker.RunStepAsync(Task, stepKey, request, cancellationToken);
}

/// <summary>The worker's timing.</summary>
public sealed record AnalysisWorkerOptions(TimeSpan RecordingPoll, TimeSpan AttemptPoll)
{
    public static AnalysisWorkerOptions Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
}

/// <summary>Starts the local llama.cpp server when a profile needs it.</summary>
public interface ILocalServerHost
{
    Task EnsureStartedAsync(CancellationToken cancellationToken);
}

/// <summary>A step that cannot succeed, which fails its task.</summary>
public sealed class StepFailedException(string message) : Exception(message);

internal sealed class ModelSwitchNeededException(string profile) : Exception($"The task needs profile {profile}.")
{
    public string Profile { get; } = profile;
}

internal sealed class StoppedForRecordingException() : Exception("A recording started.");

internal sealed class TaskCancelledException() : Exception("The task was cancelled.");

/// <summary>
/// Runs the queue until it is empty. It waits while a recording is in
/// progress, with no model loaded. It runs the oldest task it can, except
/// that it first runs every task that uses the model already loaded, so the
/// model is switched only when needed. Each step is one request at a time,
/// and an attempt that fails is run again from the start, up to the
/// settings' limit, with every attempt kept.
/// </summary>
public sealed class AnalysisWorker
{
    private readonly AnalysisSettings _settings;
    private readonly IReadOnlyDictionary<string, IAnalysisTaskKind> _kinds;
    private readonly IModelEndpoint _endpoint;
    private readonly ILocalServerHost? _localServer;
    private readonly AnalysisWorkerOptions _options;
    private readonly Action<string> _status;
    private readonly Dictionary<Guid, string> _waitingFor = [];
    private IReadOnlyList<AnalysisTask> _runnable = [];
    private ModelProfile? _loaded;

    public AnalysisWorker(
        AnalysisCatalog catalog,
        AnalysisSettings settings,
        IEnumerable<IAnalysisTaskKind> kinds,
        IModelEndpoint endpoint,
        ILocalServerHost? localServer = null,
        AnalysisWorkerOptions? options = null,
        Action<string>? status = null)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        Catalog = catalog;
        _settings = settings;
        _kinds = kinds.ToDictionary(kind => kind.Kind, StringComparer.Ordinal);
        _endpoint = endpoint;
        _localServer = localServer;
        _options = options ?? AnalysisWorkerOptions.Default;
        _status = status ?? (_ => { });
    }

    public AnalysisCatalog Catalog { get; }

    /// <summary>The profile whose model is loaded, or null.</summary>
    public string? LoadedProfile => _loaded?.Name;

    public async Task RunUntilEmptyAsync(CancellationToken cancellationToken)
    {
        await Catalog.CloseAbandonedAttemptsAsync(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await Catalog.RecordingInProgressAsync(cancellationToken).ConfigureAwait(false))
            {
                await UnloadAsync(cancellationToken).ConfigureAwait(false);
                _status("Waiting for the recording to end.");
                await Task.Delay(_options.RecordingPoll, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var open = await Catalog.OpenTasksAsync(cancellationToken).ConfigureAwait(false);
            var runnable = new List<AnalysisTask>();
            foreach (var task in open)
            {
                if (task.AfterTaskId is null || task.AfterTaskState == AnalysisTaskStates.Done)
                {
                    runnable.Add(task);
                }
                else if (task.AfterTaskState is AnalysisTaskStates.Failed or AnalysisTaskStates.Cancelled)
                {
                    await Catalog.FinishTaskAsync(
                        task.TaskId,
                        AnalysisTaskStates.Failed,
                        $"The task it follows did not finish: it was {task.AfterTaskState}.",
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (runnable.Count == 0)
            {
                if (open.Count == 0)
                {
                    break;
                }

                // Only tasks waiting on others remain, and those others
                // were just finished as failed; look again shortly.
                await Task.Delay(_options.AttemptPoll, cancellationToken).ConfigureAwait(false);
                continue;
            }

            _runnable = runnable;
            var next = runnable.FirstOrDefault(UsesLoadedModel) ?? runnable[0];
            await RunTaskAsync(next, cancellationToken).ConfigureAwait(false);
        }

        await UnloadAsync(cancellationToken).ConfigureAwait(false);
        _status("The queue is empty.");
    }

    private bool UsesLoadedModel(AnalysisTask task) =>
        _loaded is { } loaded &&
        (_waitingFor.TryGetValue(task.TaskId, out var waiting)
            ? waiting == loaded.Name
            : task.StageProfiles.Values.Contains(loaded.Name, StringComparer.Ordinal));

    private async Task RunTaskAsync(AnalysisTask task, CancellationToken cancellationToken)
    {
        if (!_kinds.TryGetValue(task.Kind, out var kind))
        {
            await Catalog.FinishTaskAsync(task.TaskId, AnalysisTaskStates.Failed,
                $"This worker has no task kind \"{task.Kind}\".", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (kind.Version != task.KindVersion)
        {
            await Catalog.FinishTaskAsync(task.TaskId, AnalysisTaskStates.Failed,
                $"The task was queued for version {task.KindVersion} of \"{task.Kind}\"; this worker has version {kind.Version}.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (task.CancelRequested)
        {
            await Catalog.FinishTaskAsync(task.TaskId, AnalysisTaskStates.Cancelled, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        await Catalog.MarkRunningAsync(task.TaskId, cancellationToken).ConfigureAwait(false);
        _status($"Running {task.Kind}.");
        try
        {
            await kind.RunAsync(new AnalysisTaskContext(this, task), cancellationToken).ConfigureAwait(false);
            _waitingFor.Remove(task.TaskId);
            await Catalog.FinishTaskAsync(task.TaskId, AnalysisTaskStates.Done, null, cancellationToken).ConfigureAwait(false);
            _status($"Finished {task.Kind}.");
        }
        catch (ModelSwitchNeededException exception)
        {
            _waitingFor[task.TaskId] = exception.Profile;
        }
        catch (StoppedForRecordingException)
        {
            // The loop waits for the recording to end, then takes the task
            // up again.
        }
        catch (TaskCancelledException)
        {
            _waitingFor.Remove(task.TaskId);
            await Catalog.FinishTaskAsync(task.TaskId, AnalysisTaskStates.Cancelled, null, cancellationToken).ConfigureAwait(false);
        }
        catch (StepFailedException exception)
        {
            _waitingFor.Remove(task.TaskId);
            await Catalog.FinishTaskAsync(task.TaskId, AnalysisTaskStates.Failed, exception.Message, cancellationToken).ConfigureAwait(false);
            _status($"{task.Kind} failed.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _waitingFor.Remove(task.TaskId);
            await Catalog.FinishTaskAsync(task.TaskId, AnalysisTaskStates.Failed,
                $"The task stopped with an error: {exception.Message}", cancellationToken).ConfigureAwait(false);
            _status($"{task.Kind} failed.");
        }
    }

    internal async Task<AcceptedStep> RunStepAsync(
        AnalysisTask task,
        string stepKey,
        ModelRequest request,
        CancellationToken cancellationToken)
    {
        var capability = request.Capability;
        if (!task.StageProfiles.TryGetValue(capability, out var profileName))
        {
            throw new StepFailedException($"No model profile was chosen for {capability} when the task was queued.");
        }

        var profile = _settings.Profile(profileName) ??
            throw new StepFailedException($"The model profile \"{profileName}\" is not in the analysis settings.");
        if (!profile.Supports(capability))
        {
            throw new StepFailedException($"The model profile \"{profileName}\" cannot take {capability}.");
        }

        var body = ChatRequest.Build(profile, request);
        var inputSha = ChatRequest.Sha256(body);
        var accepted = await Catalog.AcceptedStepAsync(task.TaskId, stepKey, cancellationToken).ConfigureAwait(false);
        if (accepted is not null)
        {
            if (accepted.InputSha256 == inputSha)
            {
                return accepted;
            }

            await Catalog.SupersedeAcceptedAsync(accepted.AttemptId, cancellationToken).ConfigureAwait(false);
        }

        if (await Catalog.CancelRequestedAsync(task.TaskId, cancellationToken).ConfigureAwait(false))
        {
            throw new TaskCancelledException();
        }

        if (await Catalog.RecordingInProgressAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new StoppedForRecordingException();
        }

        if (_loaded is { } loaded && loaded.Name != profile.Name &&
            _runnable.Any(other => other.TaskId != task.TaskId && UsesLoadedModel(other)))
        {
            throw new ModelSwitchNeededException(profile.Name);
        }

        var (modelSha, basis) = await ModelHashAsync(profile, cancellationToken).ConfigureAwait(false);
        await EnsureLoadedAsync(profile, cancellationToken).ConfigureAwait(false);
        var build = await _endpoint.ServerBuildAsync(profile, cancellationToken).ConfigureAwait(false);
        string? lastFailure = null;
        while (true)
        {
            var failed = await Catalog.FailedAttemptCountAsync(task.TaskId, stepKey, cancellationToken).ConfigureAwait(false);
            if (failed >= _settings.MaxAttempts)
            {
                throw new StepFailedException(
                    $"Step {stepKey} failed {failed} times, the limit. The last failure: {lastFailure ?? "see the step's attempts"}");
            }

            var number = await Catalog.AttemptCountAsync(task.TaskId, stepKey, cancellationToken).ConfigureAwait(false) + 1;
            var attemptId = await Catalog.BeginAttemptAsync(
                new AttemptStart(
                    task.TaskId,
                    stepKey,
                    number,
                    profile,
                    modelSha,
                    basis,
                    build,
                    ChatRequest.Parameters(profile, request),
                    request.PromptTemplate,
                    request.PromptTemplateVersion,
                    inputSha,
                    [.. request.InputReferences.Order(StringComparer.Ordinal)]),
                cancellationToken).ConfigureAwait(false);
            _status($"Running {task.Kind}, step {stepKey}, attempt {number}, with {profile.Name}.");

            var stop = await RequestAsync(task.TaskId, profile, body, cancellationToken).ConfigureAwait(false);
            if (stop.Interruption is { } interruption)
            {
                await Catalog.FinishAttemptAsync(attemptId, new AttemptEnd(interruption, null, null, null, []), cancellationToken)
                    .ConfigureAwait(false);
                if (interruption == AttemptOutcomes.StoppedForRecording)
                {
                    await UnloadAsync(cancellationToken).ConfigureAwait(false);
                    throw new StoppedForRecordingException();
                }

                throw new TaskCancelledException();
            }

            var response = stop.Response;
            string? failure = stop.Failure;
            IReadOnlyList<Claim> claims = [];
            System.Text.Json.JsonElement output = default;
            if (failure is null && response!.FinishReason == "length")
            {
                failure = $"The output stopped at the output limit of {profile.Generation.MaxTokens} tokens.";
            }

            if (failure is null)
            {
                (claims, output, failure) = ClaimCheck.Check(response!.Content, request.Sections, request.InputReferences);
            }

            if (failure is not null)
            {
                lastFailure = failure;
                await Catalog.FinishAttemptAsync(
                    attemptId,
                    new AttemptEnd(AttemptOutcomes.Failed, failure, response, null, []),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            await Catalog.FinishAttemptAsync(
                attemptId,
                new AttemptEnd(AttemptOutcomes.Accepted, null, response, output, claims),
                cancellationToken).ConfigureAwait(false);
            return new AcceptedStep(attemptId, inputSha, output, claims);
        }
    }

    private sealed record RequestResult(ModelResponse? Response, string? Failure, string? Interruption);

    // Sends the request while watching for a recording starting or the task
    // being cancelled; either stops the request.
    private async Task<RequestResult> RequestAsync(
        Guid taskId,
        ModelProfile profile,
        string body,
        CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        string? interruption = null;
        Task<ModelResponse> request;
        try
        {
            request = _endpoint.CompleteAsync(profile, body, stop.Token);
        }
        catch (ModelEndpointException exception)
        {
            // An endpoint that fails before it starts is a failed attempt too.
            request = Task.FromException<ModelResponse>(exception);
        }

        while (!request.IsCompleted)
        {
            var finished = await Task.WhenAny(request, Task.Delay(_options.AttemptPoll, cancellationToken)).ConfigureAwait(false);
            if (finished == request)
            {
                break;
            }

            if (await Catalog.RecordingInProgressAsync(cancellationToken).ConfigureAwait(false))
            {
                interruption = AttemptOutcomes.StoppedForRecording;
            }
            else if (await Catalog.CancelRequestedAsync(taskId, cancellationToken).ConfigureAwait(false))
            {
                interruption = AttemptOutcomes.Cancelled;
            }

            if (interruption is not null)
            {
                await stop.CancelAsync().ConfigureAwait(false);
                break;
            }
        }

        try
        {
            var response = await request.ConfigureAwait(false);
            return interruption is null ? new RequestResult(response, null, null) : new RequestResult(null, null, interruption);
        }
        catch (OperationCanceledException) when (interruption is not null)
        {
            return new RequestResult(null, null, interruption);
        }
        catch (ModelEndpointException exception)
        {
            return interruption is null
                ? new RequestResult(null, exception.Message, null)
                : new RequestResult(null, null, interruption);
        }
    }

    private async Task EnsureLoadedAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        if (_loaded?.Name == profile.Name)
        {
            return;
        }

        await UnloadAsync(cancellationToken).ConfigureAwait(false);
        if (profile.Local is not null)
        {
            if (_localServer is null)
            {
                throw new StepFailedException($"Profile \"{profile.Name}\" is served locally, but no local server is set up.");
            }

            try
            {
                await _localServer.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ModelEndpointException exception)
            {
                throw new StepFailedException(exception.Message);
            }
        }

        _status($"Loading {profile.Name}.");
        try
        {
            await _endpoint.LoadAsync(profile, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelEndpointException exception)
        {
            throw new StepFailedException(exception.Message);
        }

        _loaded = profile;
    }

    private async Task UnloadAsync(CancellationToken cancellationToken)
    {
        if (_loaded is { } loaded)
        {
            _loaded = null;
            await _endpoint.UnloadAsync(loaded, cancellationToken).ConfigureAwait(false);
        }
    }

    // The model file's SHA-256: computed by the worker when the file is on
    // this PC, once for each size and change time; otherwise as the profile
    // states it, or unknown. A computed hash that differs from the stated
    // one fails the step, since the file is not the one the profile names.
    private async Task<(string? Sha256, string Basis)> ModelHashAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        var path = profile.Local?.ModelPath ?? profile.File.Path;
        if (path is not null && File.Exists(path))
        {
            var file = new FileInfo(path);
            var hash = await Catalog.FileHashAsync(file.FullName, file.Length, file.LastWriteTimeUtc, cancellationToken).ConfigureAwait(false);
            if (hash is null)
            {
                _status($"Checking the model file {file.Name}.");
                await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
                await Catalog.SaveFileHashAsync(file.FullName, file.Length, file.LastWriteTimeUtc, hash, cancellationToken).ConfigureAwait(false);
            }

            if (profile.File.Sha256 is { } stated && stated != hash)
            {
                throw new StepFailedException(
                    $"The model file {file.FullName} has SHA-256 {hash}, not {stated} as profile \"{profile.Name}\" states.");
            }

            return (hash, "hashed");
        }

        return profile.File.Sha256 is { } statedOnly ? (statedOnly, "stated") : (null, "unknown");
    }
}

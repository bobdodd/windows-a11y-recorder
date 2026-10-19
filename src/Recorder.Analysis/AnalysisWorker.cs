using System.Security.Cryptography;
using System.Text.Json.Nodes;

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

    internal AnalysisTaskContext(AnalysisWorker worker, AnalysisTask task, string? picturesFolder = null)
    {
        _worker = worker;
        Task = task;
        PicturesFolder = picturesFolder ?? $"analysis/{task.TaskId:D}/pages";
        var own = PicturesFolder[..PicturesFolder.LastIndexOf('/')];
        ImagesFolder = picturesFolder is null ? "analysis/images" : own + "/images";
        ImageIndexFile = own + "/images.json";
        MomentIndexFile = own + "/moments.json";
    }

    public AnalysisTask Task { get; }

    /// <summary>
    /// Where in the recording's folder the task's code keeps the pictures it
    /// takes, with forward slashes: the task's own folder, or for a trial a
    /// folder of the trial's own, so that a trial never replaces the
    /// pictures a task's descriptions were written from (owner, 2026-10-14).
    /// </summary>
    public string PicturesFolder { get; }

    /// <summary>
    /// Where in the recording's folder the store of unique images is, with
    /// forward slashes: one store for every task on the recording, or for a
    /// trial a store of the trial's own, beside its pictures
    /// (recording-summary.md, "Unique images across the recording").
    /// </summary>
    public string ImagesFolder { get; }

    /// <summary>The task's index of the images of its tiles, beside its pictures' folder, with forward slashes.</summary>
    public string ImageIndexFile { get; }

    /// <summary>The task's index of its moment records, beside its pictures' folder, with forward slashes (recording-summary.md, "The layered report").</summary>
    public string MomentIndexFile { get; }

    /// <summary>
    /// Says that a step is not sent at this run, and why. A trial of that
    /// step fails saying so, as there is no request of the task's to try.
    /// </summary>
    public void StepNotSent(string stepKey, string reason) => _worker.StepNotSent(stepKey, reason);

    public AnalysisCatalog Catalog => _worker.Catalog;

    /// <summary>The analysis settings the worker runs with, such as whether the report is limited to the browser.</summary>
    public AnalysisSettings Settings => _worker.Settings;

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
/// <summary>A step that failed, with the model's output where it gave one.</summary>
public sealed class StepFailedException(string message, string? output = null) : Exception(message)
{
    /// <summary>The output the checks refused, saved beside the task's report (owner, 2026-10-16).</summary>
    public string? Output { get; } = output;
}

internal sealed class ModelSwitchNeededException(string profile) : Exception($"The task needs profile {profile}.")
{
    public string Profile { get; } = profile;
}

internal sealed class StoppedForRecordingException() : Exception("A recording started.");

internal sealed record TrialPlan(string StepKey, IReadOnlyDictionary<string, double> Sampling, string PicturesFolder, bool OneLine = false);

internal sealed class TrialFinishedException(StepTrial trial) : Exception("The trial step has run.")
{
    public StepTrial Trial { get; } = trial;
}


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

    internal AnalysisSettings Settings => _settings;
    private readonly IReadOnlyDictionary<string, IAnalysisTaskKind> _kinds;
    private readonly IModelEndpoint _endpoint;
    private readonly ILocalServerHost? _localServer;
    private readonly AnalysisWorkerOptions _options;
    private readonly Action<string> _status;
    private readonly Dictionary<Guid, string> _waitingFor = [];
    private IReadOnlyList<AnalysisTask> _runnable = [];
    private ModelProfile? _loaded;
    private TrialPlan? _trial;

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
            SaveFailedOutput(task, exception.Output);
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
        if (_trial is { } trial)
        {
            return await TrialStepAsync(task, stepKey, request, trial, cancellationToken).ConfigureAwait(false);
        }

        var profile = StepProfile(task, request);
        var body = ChatRequest.Build(profile, request);
        var inputSha = ChatRequest.Sha256(body);
        var accepted = await Catalog.AcceptedStepAsync(task.TaskId, stepKey, request.Sections, cancellationToken).ConfigureAwait(false);
        if (accepted is not null)
        {
            // An answer sampled after a loop was sent with its own request
            // (LoopRecovery).
            if (accepted.InputSha256 == inputSha ||
                (Repeatable(profile) && LoopRecovery.Seeds.Any(seed =>
                    ChatRequest.Sha256(ChatRequest.Build(LoopRecovery.Sample(profile, seed), request)) == accepted.InputSha256)))
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

        // The settings sent, with the server's defaults for those not sent,
        // so that each attempt records how it was sampled.
        var parameters = ChatRequest.Parameters(profile, request);
        parameters["server_defaults"] = await _endpoint.ServerSamplingAsync(profile, cancellationToken).ConfigureAwait(false);

        // A task run again reuses the accepted answer of the task it runs
        // again where the request, model file, server build, and settings
        // are unchanged; and where the step failed there with them all
        // unchanged at temperature 0, it fails at once (owner, 2026-10-14).
        // See analysis-runner.md, "Reusing steps when run again".
        if (task.RerunOfTaskId is { } source && modelSha is not null && build is not null)
        {
            var identity = new StepIdentity(inputSha, modelSha, build, (JsonObject)parameters.DeepClone());
            if (await Catalog.ReusableAttemptAsync(source, stepKey, identity, cancellationToken).ConfigureAwait(false) is { } reusable)
            {
                var number = await Catalog.AttemptCountAsync(task.TaskId, stepKey, cancellationToken).ConfigureAwait(false) + 1;
                await Catalog.ReuseAttemptAsync(reusable, task.TaskId, number, cancellationToken).ConfigureAwait(false);
                _status($"Running {task.Kind}, step {stepKey} reused from task {source}.");
                return await Catalog.AcceptedStepAsync(task.TaskId, stepKey, request.Sections, cancellationToken).ConfigureAwait(false) ??
                    throw new StepFailedException($"Step {stepKey} was reused from task {source}, but its answer could not be read back.");
            }

            if (Repeatable(profile) &&
                await Catalog.UnchangedFailureAsync(source, stepKey, identity, cancellationToken).ConfigureAwait(false) is { } failed)
            {
                // A loop is not sent again either, but recovered from, as
                // the task it runs again did or would have (owner,
                // 2026-10-15).
                if (Recoverable(failed.RawOutput, failed.Failure) is { } caught)
                {
                    return await RecoverFromLoopAsync(
                        task, stepKey, request, profile, failed.RawOutput ?? "", inputSha, parameters, modelSha, basis, build,
                        caught, cancellationToken)
                        .ConfigureAwait(false);
                }

                throw new StepFailedException(
                    $"Step {stepKey} failed in task {source} with the same request, model, server build, and settings, " +
                    $"so it was not asked for again: at temperature 0 it would give the same output. It failed: {failed.Failure}",
                    failed.RawOutput);
            }
        }

        // A task that stopped part way through recovering from a loop, as
        // for a recording, carries on where it stopped.
        if (Repeatable(profile) && modelSha is not null && build is not null &&
            await Catalog.UnchangedFailureAsync(
                task.TaskId, stepKey, new StepIdentity(inputSha, modelSha, build, (JsonObject)parameters.DeepClone()), cancellationToken)
                .ConfigureAwait(false) is { } own &&
            Recoverable(own.RawOutput, own.Failure) is { } ownCaught)
        {
            return await RecoverFromLoopAsync(
                task, stepKey, request, profile, own.RawOutput ?? "", inputSha, parameters, modelSha, basis, build,
                ownCaught, cancellationToken)
                .ConfigureAwait(false);
        }

        string? lastFailure = null;
        string? lastOutput = null;
        while (true)
        {
            var failed = await Catalog.FailedAttemptCountAsync(task.TaskId, stepKey, cancellationToken).ConfigureAwait(false);
            if (failed >= _settings.MaxAttempts)
            {
                throw new StepFailedException(
                    $"Step {stepKey} failed {failed} times, the limit. The last failure: {lastFailure ?? "see the step's attempts"}",
                    lastOutput);
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
                    (JsonObject)parameters.DeepClone(),
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
            if (failure is null && response!.StoppedBecause is { } stopped)
            {
                failure = stopped;
            }
            else if (failure is null && response!.FinishReason == "length")
            {
                failure = $"{OutputLimitFailure}{profile.Generation.MaxTokens} tokens.";
            }

            if (failure is null)
            {
                (claims, output, failure) = Check(response!.Content, request);
            }

            if (failure is not null)
            {
                lastFailure = failure;
                lastOutput = response?.Content ?? lastOutput;
                await Catalog.FinishAttemptAsync(
                    attemptId,
                    new AttemptEnd(AttemptOutcomes.Failed, failure, response, null, []),
                    cancellationToken).ConfigureAwait(false);

                // At temperature 0 the same request gives the same output, so
                // output the checks refused is not asked for again; a failure
                // of the server, with no output, is (owner, 2026-10-14). See
                // analysis-runner.md, "8. Failed output is rerun, not
                // repaired".
                if (response is not null && Repeatable(profile))
                {
                    // An answer caught in a loop is recovered from by
                    // sampling, not sent again (owner, 2026-10-15).
                    if (response.StoppedBecause is not null)
                    {
                        return await RecoverFromLoopAsync(
                            task, stepKey, request, profile, response.Content ?? "", inputSha, parameters, modelSha, basis, build, Caught.Loop, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // An answer that reached the output limit is recovered
                    // from the same way, as it may have run on without
                    // repeating itself (target machine, 2026-10-16: a
                    // countdown through the knowledge's statements; owner,
                    // 2026-10-17, "ok, let's try that").
                    if (ReachedLimit(failure))
                    {
                        return await RecoverFromLoopAsync(
                            task, stepKey, request, profile, response.Content ?? "", inputSha, parameters, modelSha, basis, build, Caught.Limit, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // An answer with no statement, for a step that needs
                    // one, the same way (target machine, 2026-10-17: the
                    // story of a visit came back with every section empty;
                    // owner, the same day).
                    if (WasEmpty(failure))
                    {
                        return await RecoverFromLoopAsync(
                            task, stepKey, request, profile, response.Content ?? "", inputSha, parameters, modelSha, basis, build, Caught.Empty, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // An answer whose wording broke the report's rules, the
                    // same way (target report of 88464e7; owner, 2026-10-18,
                    // "yes, build as proposed").
                    if (WasMisworded(failure))
                    {
                        return await RecoverFromLoopAsync(
                            task, stepKey, request, profile, response.Content ?? "", inputSha, parameters, modelSha, basis, build, Caught.Wording, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // An answer with a claim that cites nothing, the same
                    // way: a statement that no evidence was found is of kind
                    // none, and one given as a claim is sampled again
                    // (target machine, 2026-10-19: moment 10 said as a claim
                    // that no facts were given since the moment before;
                    // owner, the same day, "yes, do that").
                    if (WasUncited(failure))
                    {
                        return await RecoverFromLoopAsync(
                            task, stepKey, request, profile, response.Content ?? "", inputSha, parameters, modelSha, basis, build, Caught.Uncited, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    throw new StepFailedException(
                        $"Step {stepKey} failed: {failure} At temperature 0 the same request gives the same output, " +
                        "so it was not asked for again.",
                        response.Content);
                }

                // A request the server refused itself, such as one longer
                // than its context, would be refused again at any
                // temperature, so it is not sent again (owner, 2026-10-14;
                // analysis-runner.md, decision 8).
                if (stop.Refused)
                {
                    throw new StepFailedException(
                        $"Step {stepKey} failed: {failure} The server refused the request itself, so it was not sent again.");
                }

                continue;
            }

            await Catalog.FinishAttemptAsync(
                attemptId,
                new AttemptEnd(AttemptOutcomes.Accepted, null, response, output, claims),
                cancellationToken).ConfigureAwait(false);
            return new AcceptedStep(attemptId, inputSha, output, claims);
        }
    }

    /// <summary>
    /// Recovers from a step's answer at temperature 0 that was stopped as a
    /// loop: the step is asked for at each of LoopRecovery's seeds in turn,
    /// and the first answer that passes the checks is the step's; when none
    /// does, the loop's answer is kept up to the loop and says so. Each
    /// attempt records that it was made after a loop. Where the task it runs
    /// again, or this task before it stopped, made an attempt with the same
    /// request, model, server build, and settings, its answer is reused, or
    /// its failure stands, as a fixed seed gives the same answer. See
    /// docs/architecture/analysis-runner.md, "Decisions (2026-10-15)".
    /// </summary>
    private async Task<AcceptedStep> RecoverFromLoopAsync(
        AnalysisTask task,
        string stepKey,
        ModelRequest request,
        ModelProfile profile,
        string loopContent,
        string inputSha,
        JsonObject parameters,
        string? modelSha,
        string basis,
        string? build,
        Caught kind,
        CancellationToken cancellationToken)
    {
        var after = kind switch
        {
            Caught.Limit => "an answer that reached the output limit",
            Caught.Empty => "an answer with no statement",
            Caught.Wording => "an answer whose wording broke the report's rules",
            Caught.Uncited => "an answer with a claim that cites no evidence",
            _ => "a loop"
        };
        var caught = kind switch
        {
            Caught.Limit => "its answer reached the output limit",
            Caught.Empty => "its answer gave no statement",
            Caught.Wording => "its answer's wording broke the report's rules",
            Caught.Uncited => "its answer gave a claim that cites no evidence",
            _ => "its answer was caught in a loop"
        };
        var failures = new List<string>();
        foreach (var seed in LoopRecovery.Seeds)
        {
            var sample = LoopRecovery.Sample(profile, seed);
            var body = ChatRequest.Build(sample, request);
            var sha = ChatRequest.Sha256(body);
            var sampleParameters = ChatRequest.Parameters(sample, request);
            sampleParameters["server_defaults"] = parameters["server_defaults"]?.DeepClone();
            sampleParameters[LoopRecovery.AfterLoopParameter] = LoopRecovery.Sampled;
            var seedName = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var earlier = await EarlierAttemptAsync(task, stepKey, request, sha, sampleParameters, modelSha, build, cancellationToken).ConfigureAwait(false);
            if (earlier.Accepted is { } reused)
            {
                return reused;
            }

            if (earlier.Failure is { } before)
            {
                failures.Add($"at seed {seedName}, {before}");
                continue;
            }

            var attempt = await AttemptAsync(
                task, stepKey, request, sample, body, sha, sampleParameters, modelSha, basis, build,
                $"answer sampled at seed {seedName} after {after}", cancellationToken).ConfigureAwait(false);
            if (attempt.Failure is null)
            {
                return new AcceptedStep(attempt.AttemptId, sha, attempt.Output, attempt.Claims);
            }

            // A server that gave no answer may give one when the task is run
            // again, so the step fails rather than going on without it.
            if (attempt.Response is null)
            {
                throw new StepFailedException(
                    $"Step {stepKey} failed: {caught}, and the server gave no answer at seed {seedName}: {attempt.Failure}", loopContent);
            }

            failures.Add($"at seed {seedName}, {attempt.Failure}");
        }

        // An answer that reached the output limit, gave no statement, broke
        // the wording rules, or gave a claim that cites nothing has no loop to cut it at, so where no
        // sample passed, the step fails.
        if (kind != Caught.Loop)
        {
            throw new StepFailedException(
                $"Step {stepKey} failed: {caught}, and none of the answers sampled after it passed ({string.Join("; ", failures)}).",
                loopContent);
        }

        var cutParameters = (JsonObject)parameters.DeepClone();
        cutParameters[LoopRecovery.AfterLoopParameter] = LoopRecovery.CutShort;
        var earlierCut = await EarlierAttemptAsync(task, stepKey, request, inputSha, cutParameters, modelSha, build, cancellationToken).ConfigureAwait(false);
        if (earlierCut.Accepted is { } reusedCut)
        {
            return reusedCut;
        }

        var cut = LoopRecovery.CutAtLoop(loopContent, request.Sections);
        var (claims, output, failure) = cut is null
            ? ([], default, "No loop was found in its answer.")
            : Check(cut, request);
        var number = await Catalog.AttemptCountAsync(task.TaskId, stepKey, cancellationToken).ConfigureAwait(false) + 1;
        var attemptId = await Catalog.BeginAttemptAsync(
            new AttemptStart(
                task.TaskId, stepKey, number, profile, modelSha, basis, build, cutParameters,
                request.PromptTemplate, request.PromptTemplateVersion, inputSha,
                [.. request.InputReferences.Order(StringComparer.Ordinal)]),
            cancellationToken).ConfigureAwait(false);

        // The answer as the server gave it is kept beside the answer cut
        // from it.
        var loopResponse = new ModelResponse(loopContent, null, null, null, null, null);
        if (failure is not null)
        {
            await Catalog.FinishAttemptAsync(attemptId, new AttemptEnd(AttemptOutcomes.Failed, failure, loopResponse, null, []), cancellationToken)
                .ConfigureAwait(false);
            throw new StepFailedException(
                $"Step {stepKey} failed: its answer was caught in a loop, none of the answers sampled after it passed " +
                $"({string.Join("; ", failures)}), and its answer up to the loop failed the checks: {failure}");
        }

        await Catalog.FinishAttemptAsync(attemptId, new AttemptEnd(AttemptOutcomes.Accepted, null, loopResponse, output, claims), cancellationToken)
            .ConfigureAwait(false);
        _status($"Running {task.Kind}, step {stepKey}: no answer sampled after a loop passed, so the answer is kept up to the loop.");
        return new AcceptedStep(attemptId, inputSha, output, claims);
    }

    private sealed record EarlierAttempt(AcceptedStep? Accepted, string? Failure);

    // How an attempt that reached the output limit is recorded as failing.
    private const string OutputLimitFailure = "The output stopped at the output limit of ";

    private static bool ReachedLimit(string? failure) =>
        failure is not null && failure.StartsWith(OutputLimitFailure, StringComparison.Ordinal);

    // How an answer with no statement, for a step that needs one, is
    // recorded as failing (owner, 2026-10-17).
    internal const string EmptyFailure = "The answer gave no statement, and this step needs at least one.";

    private static bool WasEmpty(string? failure) => failure == EmptyFailure;

    private static bool WasMisworded(string? failure) =>
        failure is not null && failure.StartsWith(WordingCheck.Failure, StringComparison.Ordinal);

    private static bool WasUncited(string? failure) =>
        failure is not null && failure.EndsWith(ClaimCheck.UncitedFailure, StringComparison.Ordinal);

    // What an answer that failed was caught at, where it is recovered from
    // by sampling: a loop, the output limit, no statement, its wording, or
    // a claim that cites nothing.
    private enum Caught
    {
        Loop,
        Limit,
        Empty,
        Wording,
        Uncited
    }

    private static Caught? Recoverable(string? rawOutput, string? failure) =>
        LoopRecovery.IsLoop(rawOutput) ? Caught.Loop
        : ReachedLimit(failure) ? Caught.Limit
        : WasEmpty(failure) ? Caught.Empty
        : WasMisworded(failure) ? Caught.Wording
        : WasUncited(failure) ? Caught.Uncited
        : null;

    // The checks of an answer: ClaimCheck's, then, for a step that needs a
    // statement, that it gave one, then, for a step whose wording is
    // checked, its wording.
    private static (IReadOnlyList<Claim> Claims, System.Text.Json.JsonElement Output, string? Failure) Check(string? content, ModelRequest request)
    {
        var (claims, output, failure) = ClaimCheck.Check(content, request.Sections, request.InputReferences);
        if (failure is null && request.NeedsStatements && claims.Count == 0)
        {
            return ([], default, EmptyFailure);
        }

        if (failure is null && request.ChecksWording && WordingCheck.Check(claims) is { } misworded)
        {
            return ([], default, misworded);
        }

        return (claims, output, failure);
    }

    // An attempt made before with the same request, model, server build, and
    // settings: in the task this one runs again, whose accepted answer is
    // reused, or in either task, whose failure stands.
    private async Task<EarlierAttempt> EarlierAttemptAsync(
        AnalysisTask task,
        string stepKey,
        ModelRequest request,
        string inputSha,
        JsonObject parameters,
        string? modelSha,
        string? build,
        CancellationToken cancellationToken)
    {
        if (modelSha is null || build is null)
        {
            return new EarlierAttempt(null, null);
        }

        var identity = new StepIdentity(inputSha, modelSha, build, (JsonObject)parameters.DeepClone());
        if (task.RerunOfTaskId is { } source)
        {
            if (await Catalog.ReusableAttemptAsync(source, stepKey, identity, cancellationToken).ConfigureAwait(false) is { } reusable)
            {
                var number = await Catalog.AttemptCountAsync(task.TaskId, stepKey, cancellationToken).ConfigureAwait(false) + 1;
                await Catalog.ReuseAttemptAsync(reusable, task.TaskId, number, cancellationToken).ConfigureAwait(false);
                _status($"Running {task.Kind}, step {stepKey} reused from task {source}.");
                return new EarlierAttempt(
                    await Catalog.AcceptedStepAsync(task.TaskId, stepKey, request.Sections, cancellationToken).ConfigureAwait(false) ??
                        throw new StepFailedException($"Step {stepKey} was reused from task {source}, but its answer could not be read back."),
                    null);
            }

            if (await Catalog.UnchangedFailureAsync(source, stepKey, identity, cancellationToken).ConfigureAwait(false) is { } failed)
            {
                return new EarlierAttempt(null, failed.Failure ?? "it failed");
            }
        }

        return await Catalog.UnchangedFailureAsync(task.TaskId, stepKey, identity, cancellationToken).ConfigureAwait(false) is { } own
            ? new EarlierAttempt(null, own.Failure ?? "it failed")
            : new EarlierAttempt(null, null);
    }

    private sealed record AttemptResult(Guid AttemptId, ModelResponse? Response, string? Failure, IReadOnlyList<Claim> Claims, System.Text.Json.JsonElement Output);

    // One attempt at a step with the profile and request body given,
    // recorded in the catalog, ended by a recording starting or the task
    // being cancelled as any attempt is.
    private async Task<AttemptResult> AttemptAsync(
        AnalysisTask task,
        string stepKey,
        ModelRequest request,
        ModelProfile profile,
        string body,
        string inputSha,
        JsonObject parameters,
        string? modelSha,
        string basis,
        string? build,
        string what,
        CancellationToken cancellationToken)
    {
        if (await Catalog.CancelRequestedAsync(task.TaskId, cancellationToken).ConfigureAwait(false))
        {
            throw new TaskCancelledException();
        }

        if (await Catalog.RecordingInProgressAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new StoppedForRecordingException();
        }

        var number = await Catalog.AttemptCountAsync(task.TaskId, stepKey, cancellationToken).ConfigureAwait(false) + 1;
        var attemptId = await Catalog.BeginAttemptAsync(
            new AttemptStart(
                task.TaskId, stepKey, number, profile, modelSha, basis, build, (JsonObject)parameters.DeepClone(),
                request.PromptTemplate, request.PromptTemplateVersion, inputSha,
                [.. request.InputReferences.Order(StringComparer.Ordinal)]),
            cancellationToken).ConfigureAwait(false);
        _status($"Running {task.Kind}, step {stepKey}, attempt {number}, an {what}, with {profile.Name}.");

        var stop = await RequestAsync(task.TaskId, profile, body, cancellationToken).ConfigureAwait(false);
        if (stop.Interruption is { } interruption)
        {
            await Catalog.FinishAttemptAsync(attemptId, new AttemptEnd(interruption, null, null, null, []), cancellationToken).ConfigureAwait(false);
            if (interruption == AttemptOutcomes.StoppedForRecording)
            {
                await UnloadAsync(cancellationToken).ConfigureAwait(false);
                throw new StoppedForRecordingException();
            }

            throw new TaskCancelledException();
        }

        var response = stop.Response;
        var failure = stop.Failure ?? response!.StoppedBecause ??
            (response!.FinishReason == "length" ? $"The output stopped at the output limit of {profile.Generation.MaxTokens} tokens." : null);
        IReadOnlyList<Claim> claims = [];
        System.Text.Json.JsonElement output = default;
        if (failure is null)
        {
            (claims, output, failure) = Check(response!.Content, request);
        }

        await Catalog.FinishAttemptAsync(
            attemptId,
            failure is null
                ? new AttemptEnd(AttemptOutcomes.Accepted, null, response, output, claims)
                : new AttemptEnd(AttemptOutcomes.Failed, failure, response, null, []),
            cancellationToken).ConfigureAwait(false);
        return new AttemptResult(attemptId, response, failure, claims, output);
    }

    private ModelProfile StepProfile(AnalysisTask task, ModelRequest request)
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

        // The profile's sampling options this step leaves out, such as the
        // presence penalty for the steps that write the report (owner,
        // 2026-10-17). They are neither sent nor recorded with the attempt.
        var left = profile.Generation.Sampling.Where(option => request.SamplingLeftOut.Contains(option.Key)).Select(option => option.Key).ToList();
        return left.Count == 0
            ? profile
            : profile with { Generation = profile.Generation with { Sampling = profile.Generation.Sampling.RemoveRange(left) } };
    }

    /// <summary>
    /// A trial of one step of a task that has run: the task's code runs
    /// again, with its steps before that one given back as accepted, and
    /// that step's request is sent with the sampling options given, on top
    /// of its profile's. Nothing is written to the catalog: the result is
    /// only returned. See docs/architecture/analysis-runner.md, "Trying a
    /// step with other sampling options".
    /// </summary>
    public async Task<StepTrial> TrialAsync(
        Guid taskId,
        string stepKey,
        IReadOnlyDictionary<string, double> sampling,
        CancellationToken cancellationToken,
        bool oneLine = false)
    {
        ArgumentNullException.ThrowIfNull(stepKey);
        ArgumentNullException.ThrowIfNull(sampling);
        var task = await Catalog.TaskAsync(taskId, cancellationToken).ConfigureAwait(false) ??
            throw new StepFailedException($"There is no task {taskId}.");
        if (!_kinds.TryGetValue(task.Kind, out var kind) || kind.Version != task.KindVersion)
        {
            throw new StepFailedException($"This worker cannot run version {task.KindVersion} of \"{task.Kind}\".");
        }

        var pictures = $"analysis/trials/{task.TaskId:D}-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)}/pages";
        _trial = new TrialPlan(stepKey, sampling, pictures, oneLine);
        try
        {
            await kind.RunAsync(new AnalysisTaskContext(this, task, pictures), cancellationToken).ConfigureAwait(false);
        }
        catch (TrialFinishedException finished)
        {
            return finished.Trial;
        }
        finally
        {
            _trial = null;
            await UnloadAsync(cancellationToken).ConfigureAwait(false);
        }

        throw new StepFailedException($"The task ran to its end without a step {stepKey}.");
    }

    internal void StepNotSent(string stepKey, string reason)
    {
        if (_trial is { } trial && trial.StepKey == stepKey)
        {
            throw new StepFailedException($"Step {stepKey} is not sent in this task: {reason}");
        }
    }

    private async Task<AcceptedStep> TrialStepAsync(
        AnalysisTask task,
        string stepKey,
        ModelRequest request,
        TrialPlan trial,
        CancellationToken cancellationToken)
    {
        if (stepKey != trial.StepKey)
        {
            return await Catalog.AcceptedStepAsync(task.TaskId, stepKey, request.Sections, cancellationToken).ConfigureAwait(false) ??
                throw new StepFailedException(
                    $"Step {stepKey} has no accepted attempt in this task, so the trial cannot reach step {trial.StepKey}.");
        }

        var given = StepProfile(task, request);
        var options = given.Generation.Sampling.ToBuilder();
        var generation = given.Generation;
        foreach (var (option, value) in trial.Sampling)
        {
            // A trial may set the temperature and the seed, so that answers
            // sampled above temperature 0 can be tried before they are used
            // (owner, 2026-10-15). A task's own steps keep its profile's.
            if (option == "temperature")
            {
                if (value < 0)
                {
                    throw new StepFailedException("The temperature must be at least 0.");
                }

                generation = generation with { Temperature = value };
                continue;
            }

            if (option == "seed")
            {
                if (value != Math.Floor(value))
                {
                    throw new StepFailedException("The seed must be a whole number.");
                }

                generation = generation with { Seed = (long)value };
                continue;
            }

            if (GenerationSettings.ReservedFields.Contains(option))
            {
                throw new StepFailedException($"The sampling option \"{option}\" is one the worker sets itself.");
            }

            options[option] = value;
        }

        var profile = given with { Generation = generation with { Sampling = options.ToImmutable() } };
        var body = ChatRequest.Build(profile, request, trial.OneLine);

        // The request as the task itself would send it, without the trial's
        // options, beside the task's attempts at the step, so that the trial
        // says whether it sends what the task sent (owner, 2026-10-14).
        var asTask = ChatRequest.Sha256(ChatRequest.Build(given, request));
        var attempts = await Catalog.StepRequestsAsync(task.TaskId, stepKey, cancellationToken).ConfigureAwait(false);
        await EnsureLoadedAsync(given, cancellationToken).ConfigureAwait(false);
        var build = await _endpoint.ServerBuildAsync(profile, cancellationToken).ConfigureAwait(false);
        var parameters = ChatRequest.Parameters(profile, request);
        parameters["server_defaults"] = await _endpoint.ServerSamplingAsync(profile, cancellationToken).ConfigureAwait(false);
        _status($"Trying step {stepKey} of {task.Kind} with {profile.Name}.");

        var watch = new RepeatWatch();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ModelResponse? response = null;
        string? failure;
        string? wordingNotChecked = null;
        try
        {
            response = await _endpoint.CompleteAsync(profile, body, watch.Check, cancellationToken).ConfigureAwait(false);
            failure = response.StoppedBecause ??
                (response.FinishReason == "length" ? $"The output stopped at the output limit of {profile.Generation.MaxTokens} tokens." : null) ??
                Check(response.Content, request).Failure;

            // What the wording check would have found in a step it does not
            // check, so that a trial shows what later steps are given
            // (proposal A).
            if (failure is null && !request.ChecksWording)
            {
                wordingNotChecked = WordingCheck.Check(Check(response.Content, request).Claims) ?? "nothing";
            }
        }
        catch (ModelEndpointException exception)
        {
            failure = exception.Message;
        }

        clock.Stop();
        throw new TrialFinishedException(new StepTrial(
            task.TaskId, task.Kind, stepKey, profile.Name, parameters, build, response, failure, watch.Statements, clock.Elapsed)
        {
            RequestSha256 = asTask,
            SentSha256 = ChatRequest.Sha256(body),
            TaskAttempts = attempts,
            PicturesFolder = trial.PicturesFolder,
            OneLine = trial.OneLine,
            Instructions = request.Instructions,
            Input = request.Input,
            Pictures = [.. request.Images.Select(image =>
                $"{image.Reference}, {image.MediaType}, {image.Content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)} bytes")],
            AnswerForm = AnswerForm(body),
            WordingNotChecked = wordingNotChecked
        });
    }

    // The schema or grammar a request body holds its answer to, indented.
    private static string? AnswerForm(string body)
    {
        var node = JsonNode.Parse(body) as JsonObject;
        var form = node?["response_format"] ?? node?["grammar"] ?? node?["json_schema"];
        return form switch
        {
            null => null,
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            _ => form.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
        };
    }

    /// <summary>Whether a profile's output is repeatable: the same request gives the same output.</summary>
    public static bool Repeatable(ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Generation.Temperature == 0;
    }

    private sealed record RequestResult(ModelResponse? Response, string? Failure, string? Interruption, bool Refused = false);

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
            request = _endpoint.CompleteAsync(profile, body, new RepeatWatch().Check, stop.Token);
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
                ? new RequestResult(null, exception.Message, null, exception.Refused)
                : new RequestResult(null, null, interruption);
        }
    }

    // The output of the step that failed the task, as the model gave it, in
    // the task's folder, so that it can be read (owner, 2026-10-16: "yes
    // build step 1"; analysis-runner.md, "The output of a failed step").
    private static void SaveFailedOutput(AnalysisTask task, string? output)
    {
        if (output is null || task.SessionDirectory is not { } session)
        {
            return;
        }

        try
        {
            var path = AnalysisPlayer.FailedOutputPath(session, task.TaskId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, output, new System.Text.UTF8Encoding(false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The failure is recorded in the catalog whether or not its
            // output could be saved.
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

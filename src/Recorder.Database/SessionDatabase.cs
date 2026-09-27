using Recorder.Contracts;

namespace Recorder.Database;

/// <summary>
/// The app's database: the embedded server, the project recordings belong
/// to, and the recordings written during capture. The app starts one when it
/// starts and disposes it when it exits.
/// </summary>
public sealed class SessionDatabase : IAsyncDisposable
{
    /// <summary>
    /// The project every recording belongs to until the app lets the user
    /// choose one.
    /// </summary>
    public const string DefaultProjectName = "Default project";

    private readonly EmbeddedPostgresServer _server;
    private readonly RecordingStore _store;
    private readonly FileStream _processLock;
    private bool _disposed;

    private SessionDatabase(
        FileStream processLock,
        EmbeddedPostgresServer server,
        RecordingStore store,
        Guid projectId,
        int interruptedRecordings)
    {
        _processLock = processLock;
        _server = server;
        _store = store;
        ProjectId = projectId;
        InterruptedRecordingsAtStart = interruptedRecordings;
    }

    public Guid ProjectId { get; }

    /// <summary>
    /// Recordings an earlier app instance left in the recording state, which
    /// were marked interrupted when this instance started.
    /// </summary>
    public int InterruptedRecordingsAtStart { get; }

    public RecordingStore Store => _store;

    /// <summary>
    /// How long a recording's writer waits, when the recording stops, for its
    /// accepted events to be written.
    /// </summary>
    public TimeSpan WriterCompletionTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public EmbeddedPostgresServer Server => _server;

    /// <summary>
    /// Starts or attaches to the server, marks recordings left open by an
    /// earlier instance as interrupted, and ensures the default project.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Another process is using the database. Only one process at a time
    /// uses a data directory, because starting marks every open recording
    /// interrupted, including one another process is still recording.
    /// </exception>
    public static async Task<SessionDatabase> StartAsync(
        EmbeddedPostgresOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var processLock = AcquireProcessLock(options.DataDirectory);
        EmbeddedPostgresServer server;
        try
        {
            server = await EmbeddedPostgresServer.StartAsync(options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await processLock.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        try
        {
            var store = new RecordingStore(server.DataSource);
            var interrupted = await store.MarkOpenRecordingsInterruptedAsync(cancellationToken)
                .ConfigureAwait(false);
            var projectId = await store.EnsureProjectAsync(DefaultProjectName, cancellationToken)
                .ConfigureAwait(false);
            return new SessionDatabase(processLock, server, store, projectId, interrupted);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            await processLock.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // A file beside the data directory, held open without sharing for as
    // long as this instance runs. The operating system releases it when the
    // process ends, however it ends.
    private static FileStream AcquireProcessLock(string dataDirectory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)) + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "Another Windows A11y Recorder process is using the database.",
                exception);
        }
    }

    /// <summary>
    /// Opens a recording for playback from the database. The session
    /// folder's name is the recording's session key; its frames and audio
    /// are read from the folder.
    /// </summary>
    public Task<DatabasePlaybackResult> OpenRecordingAsync(
        string sessionDirectory,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new DatabasePlaybackReader(_server.DataSource).OpenAsync(
            sessionDirectory,
            cancellationToken);
    }

    /// <summary>
    /// The file a recording's events are spilled to while the database is
    /// not accepting writes. It is in the database's data directory rather
    /// than the session folder, so the session files do not change while the
    /// writer drains it.
    /// </summary>
    public string SpillPathFor(string sessionKey) =>
        Path.Combine(_server.DataDirectory, "spill", sessionKey + ".ndjson");

    /// <summary>
    /// Creates a recording in the recording state and a writer for its
    /// events. Without writer options, the writer uses its defaults and
    /// <see cref="SpillPathFor"/>.
    /// </summary>
    public async Task<DatabaseRecording> BeginRecordingAsync(
        RecordingDefinition definition,
        PostgresEventWriterOptions? writerOptions = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        writerOptions ??= new PostgresEventWriterOptions
        {
            SpillPath = SpillPathFor(definition.SessionKey),
            CompletionTimeout = WriterCompletionTimeout,
            Timings = new WriterTimings()
        };
        var recordingId = await _store.CreateRecordingAsync(ProjectId, definition, cancellationToken)
            .ConfigureAwait(false);
        var writer = new PostgresEventWriter(
            new PostgresEventBatchTarget(_server.DataSource, recordingId, writerOptions.Timings),
            definition.SessionKey,
            writerOptions);
        return new DatabaseRecording(_store, recordingId, writer, writerOptions.Timings);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _server.DisposeAsync().ConfigureAwait(false);
        await _processLock.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// The outcome of writing a recording to the database.
/// </summary>
/// <param name="Writer">What the event writer accepted, wrote, and did not write.</param>
/// <param name="CompletionError">
/// Why the recording's final status could not be stored, or null when it was.
/// A recording whose status was not stored stays in the recording state and
/// is marked interrupted when the app next starts.
/// </param>
public sealed record DatabaseRecordingResult(
    PostgresEventWriterResult Writer,
    string? CompletionError);

/// <summary>
/// One recording being written to the database.
/// </summary>
public sealed class DatabaseRecording : IAsyncDisposable
{
    private readonly RecordingStore _store;
    private PostgresEventWriterResult? _written;

    internal DatabaseRecording(
        RecordingStore store,
        Guid recordingId,
        PostgresEventWriter writer,
        WriterTimings? timings = null)
    {
        _store = store;
        RecordingId = recordingId;
        Writer = writer;
        Timings = timings;
    }

    public Guid RecordingId { get; }

    /// <summary>How long each step of writing took, when it was measured.</summary>
    public WriterTimings? Timings { get; }

    /// <summary>The sink collectors' events are written to.</summary>
    public PostgresEventWriter Writer { get; }

    public Task RegisterCollectorsAsync(
        IEnumerable<CollectorRegistration> collectors,
        CancellationToken cancellationToken = default) =>
        _store.RegisterCollectorsAsync(RecordingId, collectors, cancellationToken);

    /// <summary>
    /// Stops accepting events and waits, up to the writer's completion
    /// timeout, for the accepted events to be written.
    /// </summary>
    public async Task<PostgresEventWriterResult> FinishWritingAsync() =>
        _written ??= await Writer.CompleteAsync().ConfigureAwait(false);

    /// <summary>
    /// Why writing made a recording failed: events that failed their checks
    /// and were not stored, and accepted events the database did not take
    /// in time. Empty when every accepted event was stored.
    /// </summary>
    public static IReadOnlyList<string> WritingFailures(PostgresEventWriterResult written)
    {
        ArgumentNullException.ThrowIfNull(written);
        var reasons = new List<string>();
        if (written.RejectedCount > 0)
        {
            reasons.Add(
                $"{written.RejectedCount} events failed their checks and were not stored. " +
                $"The first was {written.FirstRejection}.");
        }

        if (written.UnwrittenCount > 0)
        {
            reasons.Add(
                $"{written.UnwrittenCount} accepted events were not written to the database " +
                $"and remain in {written.SpillPath}." +
                (written.LastError is null ? string.Empty : $" Last error: {written.LastError}"));
        }

        return reasons;
    }

    /// <summary>
    /// Finishes writing, if that has not been done, checks the references
    /// between the rows written, and stores the recording's final status and
    /// counts. Events that failed their checks, events the writer could not
    /// write, and stored rows that refer to missing rows make a completed
    /// recording failed, with the reasons stated after
    /// <paramref name="failure"/>.
    /// </summary>
    public async Task<DatabaseRecordingResult> CompleteAsync(
        RecordingStatus status,
        DateTimeOffset endedUtc,
        long durationNanoseconds,
        string? failure,
        CancellationToken cancellationToken = default)
    {
        var written = await FinishWritingAsync().ConfigureAwait(false);
        var reasons = new List<string>();
        if (failure is not null)
        {
            reasons.Add(failure);
        }

        var writing = new List<string>(WritingFailures(written));

        // The writer's tables have no foreign keys between them, so the
        // references of the rows it stored are checked once, here.
        var checking = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            writing.AddRange(await _store.CheckReferencesAsync(RecordingId, Timings, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            writing.Add($"The references between the stored rows could not be checked: {exception.Message}");
        }

        Timings?.Since("complete.check-references", checking);
        reasons.AddRange(writing);
        if (writing.Count > 0 && status == RecordingStatus.Completed)
        {
            status = RecordingStatus.Failed;
        }

        try
        {
            await _store.CompleteRecordingAsync(
                RecordingId,
                new RecordingCompletion(
                    status,
                    endedUtc,
                    durationNanoseconds,
                    written.AcceptedCount,
                    written.DroppedCount,
                    reasons.Count == 0 ? null : string.Join(Environment.NewLine, reasons)),
                cancellationToken).ConfigureAwait(false);
            return new DatabaseRecordingResult(written, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DatabaseRecordingResult(written, exception.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Writer.DisposeAsync().ConfigureAwait(false);
        Timings?.Dispose();
    }
}

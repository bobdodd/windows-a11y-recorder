using System.Threading.Channels;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Database;

/// <summary>What the writer did with the events it was given.</summary>
/// <param name="AcceptedCount">Events taken by <see cref="PostgresEventWriter.TryWrite"/>.</param>
/// <param name="DroppedCount">
/// Events refused because the writer's queue was full, and events dropped
/// because the memory buffer and spill file were full while the database was
/// not accepting writes.
/// </param>
/// <param name="RejectedCount">Events refused because they failed a check.</param>
/// <param name="WrittenCount">Events stored in the database.</param>
/// <param name="FirstRejection">
/// The channel, event type, and check code of the first rejected event, or
/// null when no event was rejected.
/// </param>
/// <param name="UnwrittenCount">
/// Accepted events not stored when the writer finished, because the database
/// did not accept them in time. They remain in <paramref name="SpillPath"/>.
/// </param>
public sealed record PostgresEventWriterResult(
    long AcceptedCount,
    long DroppedCount,
    long RejectedCount,
    long WrittenCount,
    long UnwrittenCount,
    string? SpillPath,
    string? LastError,
    string? FirstRejection = null);

/// <summary>
/// Writes a recording's events to the database during capture. Events are
/// checked, queued, and written in batches, several at once, each in a
/// transaction of its own. While the database is not
/// accepting writes, events are held in memory, then in a spill file, both
/// bounded; beyond the bound, events are dropped and the run of dropped
/// events is recorded.
/// </summary>
public sealed class PostgresEventWriter : IRecorderEventSink, IAsyncDisposable
{
    private static readonly Dictionary<string, short> EvidenceClassIds = new(StringComparer.Ordinal)
    {
        [EvidenceClasses.Observed] = 1,
        [EvidenceClasses.Derived] = 2,
        [EvidenceClasses.Inferred] = 3,
        [EvidenceClasses.Unknown] = 4
    };

    private readonly IEventBatchTarget _target;
    private readonly PostgresEventWriterOptions _options;
    private readonly Channel<RecorderEvent> _channel;
    private readonly Queue<BufferedEvent> _memory = new();
    private readonly SpillFile _spill;
    private readonly List<WriterRejection> _rejections = [];
    private readonly List<WriterOmission> _omissions = [];
    private readonly EventRecordValidator _validator;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<InFlightBatch> _inFlight = [];
    private readonly List<BufferedEvent> _unfinished = [];
    private readonly Task _loop;
    private long _memoryBytes;
    private long _nextEventKey;
    private int _nextRejectionOrdinal;
    private int _nextOmissionOrdinal;
    private (long First, long Last, long Count)? _dropRun;
    private long _accepted;
    private long _dropped;
    private long _rejected;
    private long _written;
    private volatile string? _lastError;
    private volatile bool _databaseUnavailable;
    private string? _firstRejection;
    private bool _completed;
    private PostgresEventWriterResult? _result;

    public PostgresEventWriter(
        IEventBatchTarget target,
        string sessionKey,
        PostgresEventWriterOptions options,
        long firstEventKey = 0)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ChannelCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.WriterConnections, 1);
        _target = target;
        _validator = new EventRecordValidator(sessionKey);
        _options = options;
        _nextEventKey = firstEventKey;
        _spill = new SpillFile(options.SpillPath);
        _channel = Channel.CreateBounded<RecorderEvent>(
            new BoundedChannelOptions(options.ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        _loop = Task.Run(RunAsync);
    }

    public long AcceptedCount => Interlocked.Read(ref _accepted);
    public long DroppedCount => Interlocked.Read(ref _dropped);
    public long RejectedCount => Interlocked.Read(ref _rejected);
    public long WrittenCount => Interlocked.Read(ref _written);

    /// <summary>True while the most recent write attempt failed.</summary>
    public bool IsDatabaseUnavailable
    {
        get => _databaseUnavailable;
        private set => _databaseUnavailable = value;
    }

    public bool TryWrite(RecorderEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (_completed || !_channel.Writer.TryWrite(record))
        {
            Interlocked.Increment(ref _dropped);
            return false;
        }

        Interlocked.Increment(ref _accepted);
        return true;
    }

    /// <summary>
    /// Stops accepting events and waits, up to the completion timeout, for
    /// every accepted event to be written.
    /// </summary>
    public async Task<PostgresEventWriterResult> CompleteAsync()
    {
        if (_result is not null)
        {
            return _result;
        }

        _completed = true;
        _channel.Writer.TryComplete();
        var finished = await Task.WhenAny(_loop, Task.Delay(_options.CompletionTimeout))
            .ConfigureAwait(false);
        if (finished != _loop)
        {
            await _stop.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        // Anything not written stays on disk, so it is not lost with the
        // process, and is reported: first the batches whose writes were
        // stopped, then the events that were waiting, oldest first.
        foreach (var buffered in _unfinished)
        {
            _spill.Append(buffered);
        }

        _unfinished.Clear();
        while (_memory.TryDequeue(out var buffered))
        {
            _spill.Append(buffered);
        }

        while (_channel.Reader.TryRead(out var late))
        {
            Accept(late);
        }

        while (_memory.TryDequeue(out var buffered))
        {
            _spill.Append(buffered);
        }

        _memoryBytes = 0;
        _spill.Flush();
        var unwritten = _spill.Count;
        _spill.Dispose();
        _result = new PostgresEventWriterResult(
            AcceptedCount,
            DroppedCount,
            RejectedCount,
            WrittenCount,
            unwritten,
            unwritten > 0 ? _spill.Path : null,
            _lastError,
            _firstRejection);
        return _result;
    }

    public async ValueTask DisposeAsync()
    {
        await CompleteAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        var token = _stop.Token;
        var batchOpened = DateTime.UtcNow;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                Drain();
                Collect();
                var inputDone = _channel.Reader.Completion.IsCompleted;
                // Spilled events are read back once memory is empty, as many
                // as fit in the memory bound. The events of batches being
                // written still count against the bound, so while the store
                // refuses writes the spilled events stay on disk. With no
                // write in flight, at least one event is read, so an event
                // larger than the bound is still written.
                if (_memory.Count == 0 && _spill.HasEvents &&
                    (_inFlight.Count == 0 || !IsDatabaseUnavailable))
                {
                    foreach (var buffered in _spill.Read(
                        _options.BatchSize,
                        _options.MemoryBufferBytes - _memoryBytes,
                        requireOne: _inFlight.Count == 0))
                    {
                        _memory.Enqueue(buffered);
                        _memoryBytes += buffered.EstimatedBytes;
                    }
                }

                if (inputDone)
                {
                    CloseDropRun();
                }

                var pending = _memory.Count > 0 || _rejections.Count > 0 || _omissions.Count > 0;
                if (!pending && _inFlight.Count == 0)
                {
                    if (inputDone)
                    {
                        return;
                    }

                    if (await _channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                    {
                        batchOpened = DateTime.UtcNow;
                    }

                    continue;
                }

                var waited = DateTime.UtcNow - batchOpened;
                var ready = pending &&
                    (inputDone || _spill.HasEvents || _memory.Count >= _options.BatchSize ||
                     waited >= _options.BatchInterval);

                // While the database is not accepting writes, one batch is
                // retried and the rest wait, so batches are stored in order
                // when it returns.
                var canStart = _inFlight.Count < _options.WriterConnections &&
                    (_inFlight.Count == 0 || !IsDatabaseUnavailable);
                if (ready && canStart)
                {
                    Start(token);
                    batchOpened = DateTime.UtcNow;
                    continue;
                }

                await WaitAsync(pending && !ready ? _options.BatchInterval - waited : null, token)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            // A write that has begun to commit finishes; the rest stop at
            // cancellation. What was not stored is kept for the spill file.
            if (_inFlight.Count > 0 && !_stop.IsCancellationRequested)
            {
                await _stop.CancelAsync().ConfigureAwait(false);
            }

            foreach (var batch in _inFlight)
            {
                try
                {
                    await batch.Write.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            Collect();
            foreach (var batch in _inFlight)
            {
                _unfinished.AddRange(batch.Events);
                _memoryBytes -= batch.Events.Sum(buffered => buffered.EstimatedBytes);
                _rejections.InsertRange(0, batch.Rejections);
                _omissions.InsertRange(0, batch.Omissions);
            }

            _inFlight.Clear();
            _unfinished.Sort((left, right) => left.EventKey.CompareTo(right.EventKey));
        }
    }

    // Starts writing the oldest events, with the rejections and omissions
    // recorded so far, on a connection of its own. The write runs off this
    // loop, so the loop keeps taking events while batches are mapped.
    private void Start(CancellationToken token)
    {
        var events = new BufferedEvent[Math.Min(_options.BatchSize, _memory.Count)];
        for (var index = 0; index < events.Length; index++)
        {
            events[index] = _memory.Dequeue();
        }

        var batch = new EventBatch(events, _rejections.ToArray(), _omissions.ToArray());
        _rejections.Clear();
        _omissions.Clear();
        _inFlight.Add(new InFlightBatch(events, batch.Rejections, batch.Omissions, Task.Run(() => WriteUntilStoredAsync(batch, token), CancellationToken.None)));
    }

    // Retries until the batch is stored or the writer is stopped.
    private async Task<IReadOnlyList<StoreRefusal>> WriteUntilStoredAsync(EventBatch batch, CancellationToken token)
    {
        var retryDelay = _options.RetryInitialDelay;
        while (true)
        {
            try
            {
                var refusals = await _target.WriteAsync(batch, token).ConfigureAwait(false);
                IsDatabaseUnavailable = false;
                return refusals;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _lastError = exception.Message;
                IsDatabaseUnavailable = true;
                await Task.Delay(retryDelay, token).ConfigureAwait(false);
                retryDelay = TimeSpan.FromTicks(
                    Math.Min(retryDelay.Ticks * 2, _options.RetryMaximumDelay.Ticks));
            }
        }
    }

    // Takes the results of the writes that have finished, in the order they
    // were started.
    private void Collect()
    {
        for (var position = 0; position < _inFlight.Count;)
        {
            var batch = _inFlight[position];
            if (!batch.Write.IsCompletedSuccessfully)
            {
                position++;
                continue;
            }

            _inFlight.RemoveAt(position);
            _memoryBytes -= batch.Events.Sum(buffered => buffered.EstimatedBytes);
            var refusals = batch.Write.Result;
            foreach (var refusal in refusals)
            {
                Reject(batch.Events[refusal.Index].Event, refusal.Reason);
            }

            Interlocked.Add(ref _written, batch.Events.Length - refusals.Count);
        }
    }

    // Waits for a write to finish, for input, or for the given time, while
    // still taking events from the queue, so producers are not refused.
    private async Task WaitAsync(TimeSpan? delay, CancellationToken token)
    {
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(token);
        var waits = new List<Task>(_inFlight.Count + 2);
        waits.AddRange(_inFlight.Select(batch => (Task)batch.Write));
        if (!_channel.Reader.Completion.IsCompleted)
        {
            waits.Add(_channel.Reader.WaitToReadAsync(wake.Token).AsTask());
        }

        if (delay is { } time && time > TimeSpan.Zero)
        {
            waits.Add(Task.Delay(time, wake.Token));
        }

        if (waits.Count > 0)
        {
            await Task.WhenAny(waits).ConfigureAwait(false);
        }

        await wake.CancelAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }

    private void Drain()
    {
        while (_channel.Reader.TryRead(out var record))
        {
            Accept(record);
        }
    }

    private void Accept(RecorderEvent record)
    {
        var reason = Check(record);
        if (reason is not null)
        {
            Reject(record, reason);
            return;
        }

        var buffered = new BufferedEvent(_nextEventKey++, record, record.Payload.GetRawText());
        if (_spill.HasEvents || _memoryBytes + buffered.EstimatedBytes > _options.MemoryBufferBytes)
        {
            if (!_spill.TryAppend(buffered, _options.SpillFileBytes))
            {
                Drop(record);
                return;
            }

            CloseDropRun();
            return;
        }

        CloseDropRun();
        _memory.Enqueue(buffered);
        _memoryBytes += buffered.EstimatedBytes;
    }

    private void Drop(RecorderEvent record)
    {
        Interlocked.Increment(ref _dropped);
        var time = record.MonotonicNanoseconds;
        _dropRun = _dropRun is { } run
            ? (Math.Min(run.First, time), Math.Max(run.Last, time), run.Count + 1)
            : (time, time, 1);
    }

    private void CloseDropRun()
    {
        if (_dropRun is { } run)
        {
            _omissions.Add(new WriterOmission(_nextOmissionOrdinal++, run.First, run.Last, run.Count));
            _dropRun = null;
        }
    }

    private void Reject(RecorderEvent record, string reason)
    {
        Interlocked.Increment(ref _rejected);
        _firstRejection ??= $"{record.Channel} {record.EventType}: {reason}";
        _rejections.Add(new WriterRejection(
            _nextRejectionOrdinal++,
            reason,
            record.Channel,
            record.EventType,
            record.Sequence));
    }

    // Rejects an event with the code of its first issue.
    private string? Check(RecorderEvent record) =>
        _validator.Validate(record) is [var first, ..] ? first.Code : null;

    internal static short EvidenceClassId(string evidenceClass) => EvidenceClassIds[evidenceClass];

    private sealed record InFlightBatch(
        BufferedEvent[] Events,
        IReadOnlyList<WriterRejection> Rejections,
        IReadOnlyList<WriterOmission> Omissions,
        Task<IReadOnlyList<StoreRefusal>> Write);

}

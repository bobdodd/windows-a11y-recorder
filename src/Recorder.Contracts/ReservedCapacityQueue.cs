using System.Threading.Channels;

namespace Recorder.Contracts;

// A bounded queue for evidence produced in native callbacks, where a producer
// must never wait. Items of ordinary kinds are admitted only while the queue
// holds fewer than capacity minus the reserve, so a flood of them cannot
// displace the reserved kinds, which may use the whole capacity.
//
// Each run of refused items forms a drop episode. The episode is written into
// the queue as a marker, created by the caller's factory, immediately before
// the next admitted item, so the reader sees the loss at its place in time.
// Arrival times are read and items admitted under one lock, so the reader
// receives items and markers in non-decreasing time order.
public sealed class ReservedCapacityQueue<T>
    where T : class
{
    private readonly object _gate = new();
    private readonly Channel<T> _channel;
    private readonly int _capacity;
    private readonly int _ordinaryLimit;
    private readonly Func<long> _clock;
    private readonly Func<DropEpisode, T> _createMarker;
    private readonly Dictionary<string, long> _episodeCounts = new(StringComparer.Ordinal);
    private long _episodeFirst;
    private long _episodeLast;
    private long _episodeTotal;
    private long _totalDropped;
    private bool _closed;

    public ReservedCapacityQueue(
        int capacity,
        int reservedCapacity,
        Func<long> clock,
        Func<DropEpisode, T> createMarker)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(reservedCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(reservedCapacity, capacity);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(createMarker);
        _capacity = capacity;
        _ordinaryLimit = capacity - reservedCapacity;
        _clock = clock;
        _createMarker = createMarker;
        _channel = Channel.CreateBounded<T>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
    }

    public ChannelReader<T> Reader => _channel.Reader;

    public long TotalDropped
    {
        get
        {
            lock (_gate)
            {
                return _totalDropped;
            }
        }
    }

    // Reads the arrival time, then admits the item the factory creates for
    // it or counts the arrival as dropped. Never waits for space.
    public bool TryEnqueue(string kind, bool reserved, Func<long, T> create)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(create);
        lock (_gate)
        {
            if (_closed)
            {
                return false;
            }

            var arrivedAt = _clock();
            var limit = reserved ? _capacity : _ordinaryLimit;
            var needed = _episodeTotal > 0 ? 2 : 1;
            if (_channel.Reader.Count + needed > limit)
            {
                RecordDrop(kind, arrivedAt);
                return false;
            }

            if (_episodeTotal > 0)
            {
                _channel.Writer.TryWrite(_createMarker(TakeEpisode()));
            }

            _channel.Writer.TryWrite(create(arrivedAt));
            return true;
        }
    }

    // Stops admission and writes any open drop episode, waiting up to the
    // timeout for space, then completes the queue. Returns the episode when
    // it could not be written, so the caller can record it another way. Call
    // after every producer has stopped; the wait is outside any native
    // callback.
    public async ValueTask<DropEpisode?> CompleteAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DropEpisode? episode = null;
        lock (_gate)
        {
            _closed = true;
            if (_episodeTotal > 0)
            {
                episode = TakeEpisode();
            }
        }

        try
        {
            if (episode is not null)
            {
                using var timeoutSource =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(timeout);
                try
                {
                    await _channel.Writer.WriteAsync(
                        _createMarker(episode),
                        timeoutSource.Token).ConfigureAwait(false);
                    episode = null;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
            }
        }
        finally
        {
            _channel.Writer.TryComplete();
        }

        return episode;
    }

    // Completes the queue without writing an open episode, for failure paths.
    public void Abandon()
    {
        lock (_gate)
        {
            _closed = true;
        }

        _channel.Writer.TryComplete();
    }

    private void RecordDrop(string kind, long arrivedAt)
    {
        if (_episodeTotal == 0)
        {
            _episodeFirst = arrivedAt;
        }

        _episodeLast = arrivedAt;
        _episodeTotal++;
        _totalDropped++;
        _episodeCounts[kind] = _episodeCounts.GetValueOrDefault(kind) + 1;
    }

    private DropEpisode TakeEpisode()
    {
        var episode = new DropEpisode(
            _episodeFirst,
            _episodeLast,
            _episodeTotal,
            new SortedDictionary<string, long>(_episodeCounts, StringComparer.Ordinal));
        _episodeCounts.Clear();
        _episodeTotal = 0;
        return episode;
    }
}

// A run of consecutive refused arrivals: the arrival times of the first and
// last, the total, and the count of each kind.
public sealed record DropEpisode(
    long FirstDroppedAt,
    long LastDroppedAt,
    long Count,
    IReadOnlyDictionary<string, long> CountsByKind);

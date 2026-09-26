using Recorder.Contracts;
using Recorder.Database;
using static Recorder.Tests.DatabaseTestSupport;

namespace Recorder.Tests;

public sealed class PostgresEventWriterTests : IDisposable
{
    private const string SessionId = "session-a";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task WritesAcceptedEventsInBatchesWithIncreasingKeys()
    {
        var target = new RecordingTarget();
        var collector = Collector();
        var writer = new PostgresEventWriter(target, SessionId, Options(batchSize: 3));

        for (ulong sequence = 1; sequence <= 7; sequence++)
        {
            Assert.True(writer.TryWrite(Event(SessionId, collector, sequence, (long)sequence * 10)));
        }

        var result = await writer.CompleteAsync();

        Assert.Equal(7, result.AcceptedCount);
        Assert.Equal(7, result.WrittenCount);
        Assert.Equal(0, result.UnwrittenCount);
        Assert.All(target.Batches, batch => Assert.InRange(batch.Events.Count, 0, 3));
        Assert.Equal(Enumerable.Range(0, 7).Select(key => (long)key), target.Written.Select(e => e.EventKey));
    }

    [Fact]
    public async Task RejectsEventsThatFailChecksAndWritesTheRest()
    {
        var target = new RecordingTarget();
        var collector = Collector();
        var writer = new PostgresEventWriter(target, SessionId, Options());

        writer.TryWrite(Event(SessionId, collector, 1, 100));
        writer.TryWrite(Event("other-session", collector, 2, 200));
        writer.TryWrite(Event(SessionId, collector, 1, 300));
        writer.TryWrite(Event(SessionId, collector, 3, 50));
        writer.TryWrite(Event(SessionId, collector, 4, 400) with { EventId = "not-derived" });
        writer.TryWrite(Event(SessionId, collector, 5, 500) with { EvidenceClass = "guessed" });
        writer.TryWrite(Event(SessionId, collector, 6, 600));

        var result = await writer.CompleteAsync();

        Assert.Equal(7, result.AcceptedCount);
        Assert.Equal(5, result.RejectedCount);
        Assert.Equal(2, result.WrittenCount);
        Assert.Equal(
            [
                "event-session-mismatch",
                "event-sequence-not-increasing",
                "event-time-regressed",
                "event-id-invalid",
                "evidence-class-invalid"
            ],
            target.Rejections.Select(rejection => rejection.Reason));
        Assert.Equal(Enumerable.Range(0, 5), target.Rejections.Select(rejection => rejection.Ordinal));
    }

    [Fact]
    public async Task HoldsEventsWhileTheStoreIsUnavailableAndWritesThemInOrderWhenItReturns()
    {
        var target = new RecordingTarget { FailuresRemaining = 3 };
        var collector = Collector();
        var writer = new PostgresEventWriter(target, SessionId, Options(batchSize: 2));

        for (ulong sequence = 1; sequence <= 5; sequence++)
        {
            writer.TryWrite(Event(SessionId, collector, sequence, (long)sequence));
        }

        var result = await writer.CompleteAsync();

        Assert.Equal(5, result.WrittenCount);
        Assert.Equal(0, result.DroppedCount);
        Assert.NotNull(result.LastError);
        Assert.Equal([1UL, 2, 3, 4, 5], target.Written.Select(e => e.Event.Sequence));
    }

    [Fact]
    public async Task SpillsToDiskBeyondTheMemoryBoundAndRemovesTheFileWhenDrained()
    {
        var target = new RecordingTarget { FailUntilReleased = true };
        var collector = Collector();
        var options = Options(batchSize: 4) with { MemoryBufferBytes = 3_000 };
        var writer = new PostgresEventWriter(target, SessionId, options);

        for (ulong sequence = 1; sequence <= 20; sequence++)
        {
            writer.TryWrite(Event(SessionId, collector, sequence, (long)sequence));
        }

        await WaitUntil(() => File.Exists(options.SpillPath));
        target.FailUntilReleased = false;
        var result = await writer.CompleteAsync();

        Assert.Equal(20, result.WrittenCount);
        Assert.Equal(0, result.DroppedCount);
        Assert.Equal(Enumerable.Range(1, 20).Select(n => (ulong)n), target.Written.Select(e => e.Event.Sequence));
        Assert.False(File.Exists(options.SpillPath));
    }

    [Fact]
    public async Task DropsBeyondTheSpillBoundAndRecordsTheOmission()
    {
        var target = new RecordingTarget { FailUntilReleased = true };
        var collector = Collector();
        var options = Options(batchSize: 100) with
        {
            MemoryBufferBytes = 2_500,
            SpillFileBytes = 1_000
        };
        var writer = new PostgresEventWriter(target, SessionId, options);

        for (ulong sequence = 1; sequence <= 30; sequence++)
        {
            writer.TryWrite(Event(SessionId, collector, sequence, (long)sequence * 1_000));
        }

        await WaitUntil(() => writer.DroppedCount > 0);
        target.FailUntilReleased = false;
        var result = await writer.CompleteAsync();

        Assert.True(result.DroppedCount > 0);
        Assert.Equal(30, result.WrittenCount + result.DroppedCount);
        var omission = Assert.Single(target.Omissions);
        Assert.Equal(result.DroppedCount, omission.EventCount);
        Assert.True(omission.FirstMonotonicNanoseconds <= omission.LastMonotonicNanoseconds);
        var written = target.Written.Select(e => e.Event.MonotonicNanoseconds).ToHashSet();
        Assert.DoesNotContain(omission.FirstMonotonicNanoseconds, written);
        Assert.DoesNotContain(omission.LastMonotonicNanoseconds, written);
    }

    [Fact]
    public async Task LeavesUnwrittenEventsInTheSpillFileWhenTheStoreDoesNotReturnInTime()
    {
        var target = new RecordingTarget { FailUntilReleased = true };
        var collector = Collector();
        var options = Options() with { CompletionTimeout = TimeSpan.FromMilliseconds(300) };
        var writer = new PostgresEventWriter(target, SessionId, options);

        for (ulong sequence = 1; sequence <= 4; sequence++)
        {
            writer.TryWrite(Event(SessionId, collector, sequence, (long)sequence));
        }

        var result = await writer.CompleteAsync();

        Assert.Equal(0, result.WrittenCount);
        Assert.Equal(4, result.UnwrittenCount);
        Assert.Equal(options.SpillPath, result.SpillPath);
        Assert.Equal(4, File.ReadAllLines(options.SpillPath).Length);
    }

    [Fact]
    public async Task CountsEventsTheStoreRefusesAsRejected()
    {
        var target = new RecordingTarget { RefuseSequence = 2 };
        var collector = Collector();
        var writer = new PostgresEventWriter(target, SessionId, Options());

        for (ulong sequence = 1; sequence <= 3; sequence++)
        {
            writer.TryWrite(Event(SessionId, collector, sequence, (long)sequence));
        }

        var result = await writer.CompleteAsync();

        Assert.Equal(2, result.WrittenCount);
        Assert.Equal(1, result.RejectedCount);
        var rejection = Assert.Single(target.Rejections);
        Assert.Equal("refused-by-test", rejection.Reason);
        Assert.Equal(2UL, rejection.Sequence);
    }

    [Fact]
    public async Task RefusesEventsAfterCompletion()
    {
        var writer = new PostgresEventWriter(new RecordingTarget(), SessionId, Options());
        await writer.CompleteAsync();

        Assert.False(writer.TryWrite(Event(SessionId, Collector(), 1, 1)));
        Assert.Equal(1, writer.DroppedCount);
    }

    private PostgresEventWriterOptions Options(int batchSize = 1_000) => new()
    {
        SpillPath = Path.Combine(_directory, "spill.ndjson"),
        BatchSize = batchSize,
        BatchInterval = TimeSpan.FromMilliseconds(10),
        RetryInitialDelay = TimeSpan.FromMilliseconds(5),
        RetryMaximumDelay = TimeSpan.FromMilliseconds(20),
        CompletionTimeout = TimeSpan.FromSeconds(10)
    };

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(10);
        }
    }

    private sealed class RecordingTarget : IEventBatchTarget
    {
        private readonly object _gate = new();

        public int FailuresRemaining { get; set; }
        public volatile bool FailUntilReleased;
        public ulong? RefuseSequence { get; init; }
        public List<EventBatch> Batches { get; } = [];
        public List<BufferedEvent> Written { get; } = [];
        public List<WriterRejection> Rejections { get; } = [];
        public List<WriterOmission> Omissions { get; } = [];

        public Task<IReadOnlyList<StoreRefusal>> WriteAsync(EventBatch batch, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (FailUntilReleased || FailuresRemaining > 0)
                {
                    FailuresRemaining--;
                    throw new IOException("store unavailable");
                }

                Batches.Add(batch);
                var refusals = new List<StoreRefusal>();
                for (var index = 0; index < batch.Events.Count; index++)
                {
                    if (batch.Events[index].Event.Sequence == RefuseSequence)
                    {
                        refusals.Add(new StoreRefusal(index, "refused-by-test"));
                        continue;
                    }

                    Written.Add(batch.Events[index]);
                }

                Rejections.AddRange(batch.Rejections);
                Omissions.AddRange(batch.Omissions);
                return Task.FromResult<IReadOnlyList<StoreRefusal>>(refusals);
            }
        }
    }
}

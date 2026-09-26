using Recorder.Contracts;

namespace Recorder.Database;

/// <summary>An event the writer refused, with the reason.</summary>
public sealed record WriterRejection(
    int Ordinal,
    string Reason,
    string? Channel,
    string? EventType,
    ulong? Sequence);

/// <summary>A run of events the writer dropped because its buffers were full.</summary>
public sealed record WriterOmission(
    int Ordinal,
    long FirstMonotonicNanoseconds,
    long LastMonotonicNanoseconds,
    long EventCount);

/// <summary>An accepted event with its key in the recording.</summary>
public sealed record BufferedEvent(long EventKey, RecorderEvent Event, string PayloadJson)
{
    // Strings are UTF-16 in memory; the rest covers the envelope.
    public long EstimatedBytes => (PayloadJson.Length * 2L) + 1024;
}

public sealed record EventBatch(
    IReadOnlyList<BufferedEvent> Events,
    IReadOnlyList<WriterRejection> Rejections,
    IReadOnlyList<WriterOmission> Omissions);

/// <summary>An event in a batch the store could not write, and why.</summary>
public sealed record StoreRefusal(int Index, string Reason);

/// <summary>
/// Where the writer stores its batches. A write either stores the batch, less
/// any events it refuses and returns, or throws because the store is not
/// accepting writes; the writer then keeps the batch and tries again.
/// </summary>
public interface IEventBatchTarget
{
    Task<IReadOnlyList<StoreRefusal>> WriteAsync(EventBatch batch, CancellationToken cancellationToken);
}

namespace Recorder.Database;

public sealed record PostgresEventWriterOptions
{
    /// <summary>Events accepted but not yet taken by the writer loop.</summary>
    public int ChannelCapacity { get; init; } = 65_536;

    /// <summary>The most events written in one transaction.</summary>
    public int BatchSize { get; init; } = 5_000;

    /// <summary>The longest an accepted event waits before it is written.</summary>
    public TimeSpan BatchInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The estimated bytes of events held in memory while the database is not
    /// accepting writes. Beyond this, events are written to the spill file.
    /// </summary>
    public long MemoryBufferBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>
    /// The largest the spill file may grow. Beyond this, events are dropped
    /// and the run of dropped events is recorded as a writer omission.
    /// </summary>
    public long SpillFileBytes { get; init; } = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// The file events are written to when the memory buffer is full. The
    /// file is removed when its events have been written to the database.
    /// </summary>
    public required string SpillPath { get; init; }

    /// <summary>The first and longest waits between write attempts that failed.</summary>
    public TimeSpan RetryInitialDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan RetryMaximumDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long disposal waits for buffered events to be written. Events not
    /// written by then stay in the spill file and are reported as unwritten.
    /// </summary>
    public TimeSpan CompletionTimeout { get; init; } = TimeSpan.FromSeconds(60);
}

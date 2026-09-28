using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Recorder.Database;

/// <summary>
/// Measures where the event writer spends its time: checking and buffering
/// events, preparing batches, and each step of storing them. It also samples,
/// once a second, how far writing is behind and the processor time of the
/// app and of the PostgreSQL server processes. The measurements describe the
/// recorder itself, not the recording.
/// </summary>
public sealed class WriterTimings : IDisposable
{
    private static readonly double MillisecondsPerTick = 1000.0 / Stopwatch.Frequency;
    private readonly ConcurrentDictionary<string, Stage> _stages = new(StringComparer.Ordinal);
    private readonly List<Sample> _samples = [];
    private readonly ConcurrentQueue<Note> _notes = new();
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly TimeSpan _appProcessorAtStart;
    private readonly TimeSpan _serverProcessorAtStart;
    private Func<WriterState>? _state;
    private Timer? _timer;

    public WriterTimings()
    {
        _appProcessorAtStart = AppProcessorTime();
        _serverProcessorAtStart = ServerProcessorTime();
    }

    /// <summary>A reading of the writer's counts and buffers.</summary>
    public readonly record struct WriterState(
        long Accepted,
        long Written,
        long InMemory,
        long Spilled,
        int InFlight);

    /// <summary>Begins sampling the writer once a second.</summary>
    public void StartSampling(Func<WriterState> state)
    {
        _state = state;
        _timer = new Timer(_ => TakeSample(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>Adds a measured step: its elapsed stopwatch ticks and the items it handled.</summary>
    public void Add(string stage, long elapsedTicks, long items = 1)
    {
        var entry = _stages.GetOrAdd(stage, _ => new Stage());
        Interlocked.Add(ref entry.Ticks, elapsedTicks);
        Interlocked.Increment(ref entry.Calls);
        Interlocked.Add(ref entry.Items, items);
        long max;
        while (elapsedTicks > (max = Interlocked.Read(ref entry.MaximumTicks)) &&
               Interlocked.CompareExchange(ref entry.MaximumTicks, elapsedTicks, max) != max)
        {
        }
    }

    /// <summary>Adds the time since <paramref name="startTimestamp"/>.</summary>
    public void Since(string stage, long startTimestamp, long items = 1) =>
        Add(stage, Stopwatch.GetTimestamp() - startTimestamp, items);

    /// <summary>
    /// Adds a text the measurements alone cannot carry, such as the plan a
    /// slow query ran with. Notes are written in the order they were added.
    /// </summary>
    public void AddNote(string name, string text) => _notes.Enqueue(new Note(name, text));

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Stops sampling, takes a last sample, and returns the measurements as JSON.</summary>
    public string ToJson()
    {
        Dispose();
        TakeSample();
        var elapsed = (Stopwatch.GetTimestamp() - _started) * MillisecondsPerTick;
        Sample[] samples;
        lock (_samples)
        {
            samples = [.. _samples];
        }

        var report = new
        {
            elapsedMilliseconds = Math.Round(elapsed),
            processorCount = Environment.ProcessorCount,
            gcPauseMilliseconds = Math.Round(GC.GetTotalPauseDuration().TotalMilliseconds),
            gcCollections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
            stages = _stages
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new
                {
                    stage = pair.Key,
                    calls = pair.Value.Calls,
                    items = pair.Value.Items,
                    totalMilliseconds = Math.Round(pair.Value.Ticks * MillisecondsPerTick, 1),
                    maximumMilliseconds = Math.Round(pair.Value.MaximumTicks * MillisecondsPerTick, 1)
                }),
            samples,
            notes = _notes.Select(note => new { name = note.Name, text = note.Text })
        };
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    private void TakeSample()
    {
        if (_state is not { } read)
        {
            return;
        }

        var state = read();
        var sample = new Sample(
            Math.Round((Stopwatch.GetTimestamp() - _started) * MillisecondsPerTick),
            state.Accepted,
            state.Written,
            state.InMemory,
            state.Spilled,
            state.InFlight,
            Math.Round((AppProcessorTime() - _appProcessorAtStart).TotalMilliseconds),
            Math.Round((ServerProcessorTime() - _serverProcessorAtStart).TotalMilliseconds));
        lock (_samples)
        {
            _samples.Add(sample);
        }
    }

    private static TimeSpan AppProcessorTime()
    {
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime;
    }

    // Every process named postgres, which on the recorder's machine are the
    // app's own server; a process that ends between samples is not counted.
    private static TimeSpan ServerProcessorTime()
    {
        var total = TimeSpan.Zero;
        foreach (var process in Process.GetProcessesByName("postgres"))
        {
            try
            {
                total += process.TotalProcessorTime;
            }
            catch (Exception exception) when (exception is InvalidOperationException or
                System.ComponentModel.Win32Exception or NotSupportedException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return total;
    }

    private sealed class Stage
    {
        public long Ticks;
        public long Calls;
        public long Items;
        public long MaximumTicks;
    }

    private sealed record Note(string Name, string Text);

    private sealed record Sample(
        double AtMilliseconds,
        long Accepted,
        long Written,
        long InMemory,
        long Spilled,
        int InFlight,
        double AppProcessorMilliseconds,
        double ServerProcessorMilliseconds);
}

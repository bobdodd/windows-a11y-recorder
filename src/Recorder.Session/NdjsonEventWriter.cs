using System.Text.Json;
using System.Threading.Channels;
using Recorder.Contracts;

namespace Recorder.Session;

public sealed class NdjsonEventWriter : IRecorderEventSink, IAsyncDisposable
{
    private readonly Channel<RecorderEvent> _channel;
    private readonly FileStream _stream;
    private readonly Task _writerTask;
    private readonly JsonSerializerOptions _jsonOptions;
    private long _accepted;
    private long _dropped;
    private bool _completed;

    public NdjsonEventWriter(string path, int capacity = 16_384)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        _channel = Channel.CreateBounded<RecorderEvent>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        _writerTask = WriteLoopAsync();
    }

    public long AcceptedCount => Interlocked.Read(ref _accepted);
    public long DroppedCount => Interlocked.Read(ref _dropped);

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

    public async ValueTask DisposeAsync()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _channel.Writer.TryComplete();
        await _writerTask.ConfigureAwait(false);
        await _stream.FlushAsync().ConfigureAwait(false);
        _stream.Flush(flushToDisk: true);
        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    private async Task WriteLoopAsync()
    {
        var recordsSinceFlush = 0;

        await foreach (var record in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(record, _jsonOptions);
            var line = GC.AllocateUninitializedArray<byte>(json.Length + 1);
            json.CopyTo(line, 0);
            line[^1] = (byte)'\n';
            await _stream.WriteAsync(line).ConfigureAwait(false);
            recordsSinceFlush++;

            if (recordsSinceFlush >= 256)
            {
                await _stream.FlushAsync().ConfigureAwait(false);
                recordsSinceFlush = 0;
            }
        }
    }
}

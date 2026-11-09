using System.Collections.Concurrent;
using System.Security.Cryptography;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>A packet of the stored stream, for the frames to be made from, with the time it was placed at.</summary>
/// <param name="Time">The session time the packet was placed at.</param>
/// <param name="Flags">The packet's quality flags.</param>
public sealed record FramePacket(
    int Session,
    long ByteOffset,
    int Size,
    bool Config,
    long? PresentationMicroseconds,
    long ReceivedAtNanoseconds,
    long Time,
    long? NativeNanoseconds,
    long? UncertaintyNanoseconds,
    string? ClockMapping,
    IReadOnlyList<string> Flags);

/// <summary>A frame written, for its record.</summary>
public sealed record WrittenFrame(ulong Sequence, AndroidDeviceFrame Frame, FramePacket Packet, IReadOnlyList<string> Flags);

/// <summary>
/// Makes the phone's frames from the stored stream while it is recorded
/// (android-implementation-plan.md, slice 3, step 3b).
///
/// The packets are read back from the stream file, not kept in memory, so a
/// decoder that falls behind costs only the packets' small records, and at
/// Stop the frames not yet made are made from the file before the
/// recording is completed. Decoding is in order on one thread, as H.264
/// needs; each picture is cut to its capture session's size and compared
/// with the picture before; one that differs in any byte is converted to
/// RGB and written as PNG by one of a few workers, and its record is given
/// in order. A configuration packet's bytes go before the next picture
/// packet's, as the decoder needs the parameter sets with the key frame.
///
/// If the decoder cannot be used or a frame cannot be written, frames stop
/// being made, the reason is given once, and the stream goes on being
/// recorded.
/// </summary>
public sealed class PhoneFrames : IDisposable
{
    private readonly string _sessionDirectory;
    private readonly string _streamPath;
    private readonly Func<int, int, IH264Decoder> _decoders;
    private readonly IArtifactHashRegistry? _hashes;
    private readonly Func<long> _now;
    private readonly Action<WrittenFrame> _written;
    private readonly Action<string> _failed;
    private readonly BlockingCollection<object> _queue = [];
    private readonly BlockingCollection<Task<WrittenFrame?>> _ordered;
    private readonly SemaphoreSlim _slots;
    private readonly Thread _decoding;
    private readonly Task _emitting;
    private readonly Dictionary<long, FramePacket> _tags = [];
    private readonly List<DecodedPicture> _ready = [];

    private Microsoft.Win32.SafeHandles.SafeFileHandle? _file;
    private IH264Decoder? _decoder;
    private byte[]? _config;
    private PhonePicture? _previous;
    private int _width;
    private int _height;
    private long _nextTag;
    private ulong _sequence;
    private long _repeats;
    private long _writtenCount;
    private long _waiting;
    private int _failedOnce;

    /// <param name="decoders">Makes a decoder for a capture session of the given width and height.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="written">Called in order, from one thread, for each frame written.</param>
    /// <param name="failed">Called once, with the reason, if frames stop being made.</param>
    /// <param name="workers">How many frames are converted and written at once.</param>
    public PhoneFrames(
        string sessionDirectory,
        string streamPath,
        Func<int, int, IH264Decoder> decoders,
        IArtifactHashRegistry? hashes,
        Func<long> now,
        Action<WrittenFrame> written,
        Action<string> failed,
        int workers)
    {
        _sessionDirectory = sessionDirectory;
        _streamPath = streamPath;
        _decoders = decoders;
        _hashes = hashes;
        _now = now;
        _written = written;
        _failed = failed;
        workers = Math.Max(1, workers);
        // Each frame in hand holds a picture of about 4 MB and its RGB of
        // about 8 MB, so the frames in hand are bounded.
        _slots = new SemaphoreSlim(workers * 2);
        _ordered = new BlockingCollection<Task<WrittenFrame?>>(workers * 2);
        _emitting = Task.Factory.StartNew(Emit, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _decoding = new Thread(Decode) { IsBackground = true, Name = "Phone frames" };
        _decoding.Start();
    }

    /// <summary>Frames written so far.</summary>
    public long Written => Interlocked.Read(ref _writtenCount);

    /// <summary>Packets stored but not yet decoded.</summary>
    public long Waiting => Interlocked.Read(ref _waiting);

    /// <summary>Whether frames stopped being made.</summary>
    public bool Failed => Volatile.Read(ref _failedOnce) != 0;

    /// <summary>A capture session begins; the pictures after it are this size.</summary>
    public void Session(int width, int height) => _queue.Add(new SessionStart(width, height));

    /// <summary>A packet now complete in the stream file.</summary>
    public void Packet(FramePacket packet)
    {
        Interlocked.Increment(ref _waiting);
        _queue.Add(packet);
    }

    /// <summary>No more packets: the frames of those given are made, then this returns.</summary>
    public async Task CompleteAsync()
    {
        if (!_queue.IsAddingCompleted)
        {
            _queue.CompleteAdding();
        }

        await Task.Run(_decoding.Join).ConfigureAwait(false);
        await _emitting.ConfigureAwait(false);
    }

    public void Dispose()
    {
        _queue.Dispose();
        _ordered.Dispose();
        _slots.Dispose();
        _file?.Dispose();
    }

    private void Decode()
    {
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                if (item is FramePacket)
                {
                    Interlocked.Decrement(ref _waiting);
                }

                if (Failed)
                {
                    continue;
                }

                try
                {
                    switch (item)
                    {
                        case SessionStart started:
                            EndSession();
                            _width = started.Width;
                            _height = started.Height;
                            break;
                        case FramePacket { Config: true } config:
                            _config = Read(config);
                            break;
                        case FramePacket picture:
                            DecodePicture(picture);
                            break;
                    }
                }
                // Any failure, of the decoder or of the file, stops the frames
                // and not the recording, so every exception is caught here,
                // on the frames' own thread.
                catch (Exception exception)
                {
                    Fail(Reason(exception));
                }
            }

            if (!Failed)
            {
                try
                {
                    EndSession();
                }
                catch (Exception exception)
                {
                    Fail(Reason(exception));
                }
            }
        }
        finally
        {
            _decoder?.Dispose();
            _decoder = null;
            _ordered.CompleteAdding();
        }
    }

    private void DecodePicture(FramePacket packet)
    {
        var bytes = Read(packet);
        if (_config is { } config)
        {
            bytes = [.. config, .. bytes];
            _config = null;
        }

        _decoder ??= _decoders(_width, _height);
        var tag = _nextTag++;
        _tags[tag] = packet;
        _ready.Clear();
        _decoder.Decode(bytes, tag, _ready);
        Take();
    }

    // The session's last pictures, then a new decoder for the next session,
    // whose size and parameter sets differ.
    private void EndSession()
    {
        if (_decoder is not null)
        {
            _ready.Clear();
            _decoder.Drain(_ready);
            Take();
            _decoder.Dispose();
            _decoder = null;
        }

        _tags.Clear();
        _previous = null;
        _config = null;
    }

    private void Take()
    {
        foreach (var decoded in _ready)
        {
            if (!_tags.Remove(decoded.Tag, out var packet))
            {
                throw new FormatException($"The decoder gave a picture for no packet it was given (tag {decoded.Tag}).");
            }

            var picture = PhonePicture.Cut(decoded.Picture, _width, _height);
            if (picture.SameAs(_previous))
            {
                _repeats++;
                continue;
            }

            _previous = picture;
            var sequence = ++_sequence;
            var repeats = _repeats;
            _repeats = 0;
            var source = decoded.Picture;
            var decoder = _decoder!.Name;
            _slots.Wait();
            _ordered.Add(Task.Run(() => Write(sequence, picture, source, decoder, packet, repeats)));
        }

        _ready.Clear();
    }

    private WrittenFrame? Write(ulong sequence, PhonePicture picture, Nv12Picture decoded, string decoder, FramePacket packet, long repeats)
    {
        if (Failed)
        {
            return null;
        }

        List<string> flags = [AndroidFrameRecords.CompressedOnPhoneFlag, .. packet.Flags];
        var matrix = decoded.Matrix ?? ColourMatrix.Bt709;
        var fullRange = decoded.FullRange ?? false;
        if (decoded.Matrix is null || decoded.FullRange is null)
        {
            flags.Add(AndroidFrameRecords.ColourAssumedFlag);
        }

        var png = PngWriter.Encode(picture.Width, picture.Height, picture.ToRgb(matrix, fullRange));
        var relative = AndroidFrameRecords.FramePath(sequence);
        var path = Path.Combine(_sessionDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
        {
            stream.Write(png);
        }

        File.Move(temporary, path);
        _hashes?.Record(path, png.Length, SHA256.HashData(png));
        var frame = new AndroidDeviceFrame(
            relative,
            picture.Width,
            picture.Height,
            packet.Session,
            packet.ByteOffset,
            packet.PresentationMicroseconds ?? 0,
            png.Length,
            "R8G8B8",
            "png",
            matrix switch
            {
                ColourMatrix.Bt601 => "bt601",
                ColourMatrix.Bt2020 => "bt2020",
                _ => "bt709",
            },
            fullRange ? "full" : "limited",
            "nearest",
            decoder,
            decoded.Width,
            decoded.Height,
            repeats,
            _now());
        return new WrittenFrame(sequence, frame, packet, flags);
    }

    private void Emit()
    {
        foreach (var job in _ordered.GetConsumingEnumerable())
        {
            try
            {
                var written = job.GetAwaiter().GetResult();
                if (written is not null && !Failed)
                {
                    Interlocked.Increment(ref _writtenCount);
                    _written(written);
                }
            }
            catch (Exception exception)
            {
                Fail($"A frame could not be written: {exception.Message}");
            }
            finally
            {
                _slots.Release();
            }
        }
    }

    private byte[] Read(FramePacket packet)
    {
        _file ??= File.OpenHandle(_streamPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[packet.Size];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = RandomAccess.Read(_file, bytes.AsSpan(read), packet.ByteOffset + read);
            if (count == 0)
            {
                throw new IOException($"The stream file ends before the packet at byte {packet.ByteOffset}.");
            }

            read += count;
        }

        return bytes;
    }

    // The decoder's and the file's own messages say what failed; any other
    // exception is a fault here, so its type is given too.
    private static string Reason(Exception exception) =>
        exception is DecoderUnavailableException or FormatException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException
            ? exception.Message
            : $"{exception.GetType().Name}: {exception.Message}";

    private void Fail(string reason)
    {
        if (Interlocked.Exchange(ref _failedOnce, 1) == 0)
        {
            _failed(reason);
        }
    }

    private sealed record SessionStart(int Width, int Height);
}

namespace Recorder.Session;

/// <summary>
/// Which time buckets of each channel hold an event, kept while events are
/// added in any order and before the recording's duration is known. The
/// bucket width is the smallest power of two for which
/// <see cref="TimelineOccupancy.BucketCount"/> buckets cover the latest time
/// added; when a later time needs a wider bucket, pairs of buckets are
/// merged. The result equals the grid built from the same events once the
/// duration is known, for any duration at or after the latest time.
/// </summary>
public sealed class OccupancyBitmap
{
    private const int Words = TimelineOccupancy.BucketCount / 64;

    private readonly Dictionary<string, ulong[]> _bits = new(StringComparer.Ordinal);

    /// <summary>The current bucket width, in nanoseconds.</summary>
    public long BucketWidth { get; private set; } = 1;

    /// <summary>The latest time added.</summary>
    public long LatestTime { get; private set; }

    public void Add(string channel, long monotonicNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var time = Math.Max(0, monotonicNanoseconds);
        if (time > LatestTime)
        {
            LatestTime = time;
            var width = TimelineOccupancy.WidthFor(time);
            while (BucketWidth < width)
            {
                Halve();
            }
        }

        if (!_bits.TryGetValue(channel, out var bits))
        {
            bits = new ulong[Words];
            _bits.Add(channel, bits);
        }

        var bucket = time / BucketWidth;
        bits[bucket >> 6] |= 1UL << (int)(bucket & 63);
    }

    /// <summary>
    /// The occupancy grid for a recording of this duration, which must be at
    /// or after <see cref="LatestTime"/>.
    /// </summary>
    public TimelineOccupancy ToOccupancy(long durationNanoseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(durationNanoseconds, LatestTime);
        return new TimelineOccupancy(durationNanoseconds, ToBuckets(durationNanoseconds));
    }

    /// <summary>
    /// Each channel's occupied bucket indices, in ascending order, at the
    /// width for a recording of this duration.
    /// </summary>
    public Dictionary<string, int[]> ToBuckets(long durationNanoseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(durationNanoseconds, LatestTime);
        var width = TimelineOccupancy.WidthFor(durationNanoseconds);
        var shift = System.Numerics.BitOperations.Log2((ulong)(width / BucketWidth));
        var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var (channel, bits) in _bits)
        {
            var buckets = new List<int>();
            for (var word = 0; word < bits.Length; word++)
            {
                var value = bits[word];
                while (value != 0)
                {
                    var bit = System.Numerics.BitOperations.TrailingZeroCount(value);
                    value &= value - 1;
                    var bucket = ((word << 6) | bit) >> shift;
                    if (buckets.Count == 0 || buckets[^1] != bucket)
                    {
                        buckets.Add(bucket);
                    }
                }
            }

            result[channel] = [.. buckets];
        }

        return result;
    }

    /// <summary>
    /// The bitmap as stored in a playback index: its bucket width, latest
    /// time, and each channel's bits, 64 buckets to a little-endian word.
    /// </summary>
    public PlaybackOccupancy Export() =>
        new(
            BucketWidth,
            LatestTime,
            _bits.ToDictionary(
                pair => pair.Key,
                pair => Convert.ToBase64String(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pair.Value.AsSpan())),
                StringComparer.Ordinal));

    /// <summary>Restores a bitmap from <see cref="Export"/>.</summary>
    /// <exception cref="InvalidDataException">The stored bitmap is not valid.</exception>
    public static OccupancyBitmap Import(PlaybackOccupancy stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (stored.BucketWidth < 1 ||
            (stored.BucketWidth & (stored.BucketWidth - 1)) != 0 ||
            stored.LatestTime < 0 ||
            TimelineOccupancy.WidthFor(stored.LatestTime) > stored.BucketWidth)
        {
            throw new InvalidDataException("The stored occupancy's bucket width does not cover its latest time.");
        }

        var bitmap = new OccupancyBitmap
        {
            BucketWidth = stored.BucketWidth,
            LatestTime = stored.LatestTime
        };
        foreach (var (channel, text) in stored.Channels)
        {
            var bytes = Convert.FromBase64String(text);
            if (bytes.Length != Words * sizeof(ulong))
            {
                throw new InvalidDataException($"The stored occupancy of channel {channel} is not {Words * sizeof(ulong)} bytes.");
            }

            var bits = new ulong[Words];
            bytes.CopyTo(System.Runtime.InteropServices.MemoryMarshal.AsBytes(bits.AsSpan()));
            bitmap._bits[channel] = bits;
        }

        return bitmap;
    }

    // Doubles the bucket width: bucket i of the new width is buckets 2i and
    // 2i + 1 of the old.
    private void Halve()
    {
        foreach (var bits in _bits.Values)
        {
            for (var bucket = 0; bucket < TimelineOccupancy.BucketCount / 2; bucket++)
            {
                var occupied =
                    Get(bits, 2 * bucket) || Get(bits, 2 * bucket + 1);
                Set(bits, bucket, occupied);
            }

            for (var word = Words / 2; word < Words; word++)
            {
                bits[word] = 0;
            }
        }

        BucketWidth *= 2;
    }

    private static bool Get(ulong[] bits, int bucket) =>
        (bits[bucket >> 6] & (1UL << (bucket & 63))) != 0;

    private static void Set(ulong[] bits, int bucket, bool value)
    {
        if (value)
        {
            bits[bucket >> 6] |= 1UL << (bucket & 63);
        }
        else
        {
            bits[bucket >> 6] &= ~(1UL << (bucket & 63));
        }
    }
}

/// <summary>An <see cref="OccupancyBitmap"/> as stored in a playback index.</summary>
public sealed record PlaybackOccupancy(
    long BucketWidth,
    long LatestTime,
    IReadOnlyDictionary<string, string> Channels);

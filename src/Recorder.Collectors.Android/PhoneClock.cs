namespace Recorder.Collectors.Android;

/// <summary>
/// One clock request and its reply: the recorder's send and receive times on
/// the session clock, and the phone's CLOCK_MONOTONIC when it read the
/// request and when it wrote the reply, all in nanoseconds.
/// </summary>
public sealed record PhoneClockExchange(
    long RecorderSend,
    long PhoneReceive,
    long PhoneSend,
    long RecorderReceive)
{
    public long PhoneMidpoint => PhoneReceive + (PhoneSend - PhoneReceive) / 2;

    public long SessionMidpoint => RecorderSend + (RecorderReceive - RecorderSend) / 2;

    /// <summary>
    /// The phone's moment between receive and send happened somewhere in the
    /// recorder's round trip, less the time the phone itself took, so the
    /// midpoints pair up to within half of what is left.
    /// </summary>
    public long ErrorBound => Math.Max(0, (RecorderReceive - RecorderSend) - (PhoneSend - PhoneReceive)) / 2;
}

/// <summary>
/// The phone clock as the companion collector fits it, for the other phone
/// collectors to place phone times by.
/// </summary>
public interface IPhoneClock
{
    /// <summary>Completes with true at the first clock sample, or false when there will be none.</summary>
    Task<bool> FirstSample { get; }

    /// <summary>Null until there is a sample to map by.</summary>
    PhoneTimeMapping? Map(long phoneNanoseconds);
}

/// <summary>Where a phone time falls on the session clock, and how far it could be out.</summary>
public sealed record PhoneTimeMapping(long SessionNanoseconds, long UncertaintyNanoseconds, int Segment);

/// <summary>
/// Places the phone's CLOCK_MONOTONIC on the session clock from clock
/// samples (android-implementation-plan.md, slice 2).
///
/// Each segment is a run of samples over which the phone's clock ran with
/// the session clock; its line is the least-squares fit of session midpoint
/// on phone midpoint, each sample weighted by the inverse square of its
/// error bound (plus <see cref="WeightFloorNanoseconds"/>), so that a slow
/// reply moves the line less than a quick one. CLOCK_MONOTONIC stops while
/// the phone sleeps and the session clock does not, so a sample is a
/// discontinuity when the session time since the segment's last sample
/// differs from the phone time since it by more than
/// <see cref="DiscontinuityThresholdNanoseconds"/> plus both samples' error
/// bounds. That is judged from the last sample, not from the line, as two
/// clocks that both run differ in rate by parts per million, which over
/// the quarter second between samples is microseconds, while a line fitted
/// to a few close samples can be steeper or shallower than that. A phone time is mapped by the segment it
/// falls in; its uncertainty is the larger error bound of the samples
/// either side of it.
/// </summary>
public sealed class PhoneClockMapper
{
    public const long DiscontinuityThresholdNanoseconds = 20_000_000;

    public const long WeightFloorNanoseconds = 100_000;

    private readonly List<Segment> _segments = [];
    private readonly object _gate = new();

    public int SampleCount { get; private set; }

    public int SegmentCount
    {
        get
        {
            lock (_gate)
            {
                return _segments.Count;
            }
        }
    }

    public long? LargestErrorBound { get; private set; }

    /// <summary>Adds a sample and returns its segment, counting from 1, and whether it started one after a discontinuity.</summary>
    public (int Segment, bool Discontinuity) Add(PhoneClockExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        if (exchange.RecorderReceive < exchange.RecorderSend || exchange.PhoneSend < exchange.PhoneReceive)
        {
            throw new ArgumentException("A clock sample's times go backward.", nameof(exchange));
        }

        lock (_gate)
        {
            SampleCount++;
            LargestErrorBound = Math.Max(LargestErrorBound ?? 0, exchange.ErrorBound);

            var current = _segments.Count == 0 ? null : _segments[^1];
            var discontinuity = false;
            if (current is not null)
            {
                var last = current.Samples[^1];
                var predicted = last.SessionMidpoint + (exchange.PhoneMidpoint - last.PhoneMidpoint);
                var allowed = DiscontinuityThresholdNanoseconds + exchange.ErrorBound + last.ErrorBound;
                discontinuity = exchange.PhoneMidpoint < last.PhoneMidpoint
                    || Math.Abs(predicted - exchange.SessionMidpoint) > allowed;
            }

            if (current is null || discontinuity)
            {
                current = new Segment(_segments.Count + 1);
                _segments.Add(current);
            }

            current.Add(exchange);
            return (current.Number, discontinuity);
        }
    }

    /// <summary>Null until there is a sample to map by.</summary>
    public PhoneTimeMapping? Map(long phoneNanoseconds)
    {
        lock (_gate)
        {
            if (_segments.Count == 0)
            {
                return null;
            }

            // The last segment that starts at or before the phone time, or
            // the first, for a time before every sample.
            var segment = _segments[0];
            foreach (var candidate in _segments)
            {
                if (candidate.Samples[0].PhoneMidpoint <= phoneNanoseconds)
                {
                    segment = candidate;
                }
            }

            return new PhoneTimeMapping(
                segment.Map(phoneNanoseconds),
                segment.Uncertainty(phoneNanoseconds),
                segment.Number);
        }
    }

    private sealed class Segment(int number)
    {
        // Running weighted means and co-moments about them (West's
        // weighted form of Welford's method), so the fit stays exact to the
        // nanosecond over hours of samples, where sums of squares of
        // nanosecond values would not.
        private double _weight;
        private double _meanX;
        private double _meanY;
        private double _cxx;
        private double _cxy;

        public int Number { get; } = number;

        public List<PhoneClockExchange> Samples { get; } = [];

        public void Add(PhoneClockExchange exchange)
        {
            Samples.Add(exchange);
            var spread = (double)(exchange.ErrorBound + WeightFloorNanoseconds) / WeightFloorNanoseconds;
            var w = 1.0 / (spread * spread);
            _weight += w;
            var x = (double)(exchange.PhoneMidpoint - Samples[0].PhoneMidpoint);
            var y = (double)(exchange.SessionMidpoint - Samples[0].SessionMidpoint);
            var dx = x - _meanX;
            _meanX += w / _weight * dx;
            _meanY += w / _weight * (y - _meanY);
            _cxx += w * dx * (x - _meanX);
            _cxy += w * dx * (y - _meanY);
        }

        public long Map(long phoneNanoseconds)
        {
            // With one sample, or samples at one phone time, the clocks are
            // taken to run at the same rate.
            var slope = _cxx > 0 ? _cxy / _cxx : 1.0;
            var x = (double)(phoneNanoseconds - Samples[0].PhoneMidpoint);
            var y = _meanY + slope * (x - _meanX);
            return Samples[0].SessionMidpoint + (long)Math.Round(y);
        }

        public long Uncertainty(long phoneNanoseconds)
        {
            var index = Samples.BinarySearch(
                new PhoneClockExchange(0, phoneNanoseconds, phoneNanoseconds, 0),
                Comparer<PhoneClockExchange>.Create((a, b) => a.PhoneMidpoint.CompareTo(b.PhoneMidpoint)));
            if (index >= 0)
            {
                return Samples[index].ErrorBound;
            }

            var after = ~index;
            var before = after - 1;
            var bound = 0L;
            if (before >= 0)
            {
                bound = Math.Max(bound, Samples[before].ErrorBound);
            }

            if (after < Samples.Count)
            {
                bound = Math.Max(bound, Samples[after].ErrorBound);
            }

            return bound;
        }
    }
}

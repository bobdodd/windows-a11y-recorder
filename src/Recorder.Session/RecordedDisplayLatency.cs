using System.Globalization;
using System.Text.Json;
using Recorder.Contracts;

namespace Recorder.Session;

/// <summary>
/// The display latency a recreation subtracts from a frame's composition
/// time before it chooses compositor values, scroll positions, and animated
/// image frames (slice 4b, "Sub-step 2b-iii change 2"), so that the frame
/// chosen is the one presented two display refreshes before the
/// composition, each refresh the interval Chromium recorded in the
/// document's latest presentation-feedback record at or before it.
/// Compositions and presentations do not fall at the same instant of a
/// refresh (on the target machine a frame presented two refreshes before a
/// composition was 33.2 ms before it, at 16.666 ms a refresh), so one and a
/// half refreshes are subtracted: the last frame presented at or before that
/// is the one presented two refreshes before.
/// </summary>
/// <param name="Nanoseconds">The time subtracted, one and a half refreshes, in nanoseconds.</param>
/// <param name="IntervalMicroseconds">The recorded refresh interval.</param>
public sealed record RecordedDisplayLatency(long Nanoseconds, long IntervalMicroseconds)
{
    /// <summary>
    /// How many refreshes are subtracted. Two was measured on the target
    /// machine on 2026-10-05 and chosen by the owner; it is inferred from that
    /// machine's measurements, not recorded.
    /// </summary>
    public const int Refreshes = 2;

    /// <summary>What the evidence panel says of the latency.</summary>
    public string Note =>
        $"The compositor values, compositor scroll positions, and animated image frames are those of the compositor frame presented {Refreshes.ToString(CultureInfo.InvariantCulture)} display refreshes before the frame's composition, of {(IntervalMicroseconds / 1e3).ToString("0.000", CultureInfo.InvariantCulture)} ms each, the interval Chromium recorded with the page's latest presentation: the last presented at least {(Nanoseconds / 1e6).ToString("0.0", CultureInfo.InvariantCulture)} ms, one and a half refreshes, before it. That the screen shows a compositor frame {Refreshes.ToString(CultureInfo.InvariantCulture)} refreshes after it is presented is inferred from measurements of one machine's recordings, not recorded, and a captured frame can lag by one refresh more.";

    /// <summary>What the evidence panel says when no interval was recorded.</summary>
    public const string NoIntervalNote =
        "No refresh interval was recorded with the page's presentations at or before the frame, so the compositor values, compositor scroll positions, and animated image frames are chosen as of the frame's composition, with no display latency subtracted.";
}

/// <summary>
/// Reads the refresh intervals of a document's presentation-feedback
/// records, in the order recorded, to give the display latency at a frame.
/// </summary>
public sealed class RecordedDisplayLatencyReader
{
    private readonly string _documentToken;
    private readonly List<(long Time, long IntervalMicroseconds)> _intervals = [];

    /// <param name="documentKey">The document's state key, its token and identity.</param>
    public RecordedDisplayLatencyReader(string documentKey)
    {
        ArgumentNullException.ThrowIfNull(documentKey);
        _documentToken = documentKey.Split(' ', 2)[0];
    }

    /// <summary>The channels whose records the reader reads.</summary>
    public static IReadOnlySet<string> Channels { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "browser.presentation",
    };

    /// <summary>Takes one record, in the order recorded.</summary>
    public void Add(RecorderEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Channel != "browser.presentation" || record.EventType != "presentation-feedback")
        {
            return;
        }
        var payload = record.Payload;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("context", out var context) ||
            context.ValueKind != JsonValueKind.Object ||
            !context.TryGetProperty("documentToken", out var token) ||
            token.ValueKind != JsonValueKind.String ||
            token.GetString() != _documentToken ||
            !payload.TryGetProperty("intervalMicroseconds", out var interval) ||
            Microseconds(interval) is not { } microseconds ||
            microseconds <= 0)
        {
            return;
        }
        _intervals.Add((record.MonotonicNanoseconds, microseconds));
    }

    // The interval as the bridge writes it, a decimal string, or a number.
    private static long? Microseconds(JsonElement interval) => interval.ValueKind switch
    {
        JsonValueKind.String when long.TryParse(interval.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var text) => text,
        JsonValueKind.Number when interval.TryGetInt64(out var number) => number,
        _ => null,
    };

    /// <summary>
    /// The latency at a composition, or null when no refresh interval of the
    /// document was recorded at or before it.
    /// </summary>
    public RecordedDisplayLatency? At(long compositionNanoseconds)
    {
        var latest = _intervals.LastOrDefault(item => item.Time <= compositionNanoseconds);
        return latest.IntervalMicroseconds > 0
            ? new RecordedDisplayLatency((2 * RecordedDisplayLatency.Refreshes - 1) * latest.IntervalMicroseconds * 1_000 / 2, latest.IntervalMicroseconds)
            : null;
    }
}

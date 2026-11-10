using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>
/// The phone display's rotation and size as the companion app reported
/// them, for the touch collector to turn positions by (slice 5).
/// </summary>
public interface IPhoneDisplay
{
    /// <summary>
    /// The display state reported last at or before the phone time, or the
    /// first one for a time before it; null before any.
    /// </summary>
    AndroidDisplayState? DisplayAt(long phoneNanoseconds);
}

/// <summary>The display states in the order they came, each with its phone time.</summary>
public sealed class PhoneDisplayHistory : IPhoneDisplay
{
    private readonly List<(long Time, AndroidDisplayState State)> _states = [];
    private readonly object _gate = new();

    public void Add(long phoneNanoseconds, AndroidDisplayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            _states.Add((phoneNanoseconds, state));
        }
    }

    public AndroidDisplayState? DisplayAt(long phoneNanoseconds)
    {
        lock (_gate)
        {
            if (_states.Count == 0)
            {
                return null;
            }

            var found = _states[0].State;
            foreach (var (time, state) in _states)
            {
                if (time > phoneNanoseconds)
                {
                    break;
                }

                found = state;
            }

            return found;
        }
    }
}

namespace Recorder.Contracts;

// Holds the newest item of a stream of arrivals and releases every item it
// replaces. A capture source offers each arrival as it happens; the consumer
// takes the held item when it polls. The count of items released since the
// previous take states how many arrivals the consumer never saw.
public sealed class NewestArrivalSlot<T> : IDisposable
    where T : class, IDisposable
{
    private readonly object _gate = new();
    private T? _held;
    private long _heldArrivedAt;
    private long _releasedSinceTake;
    private bool _closed;

    // Returns false, and releases the item, once the slot is closed.
    public bool Offer(T item, long arrivedAt)
    {
        ArgumentNullException.ThrowIfNull(item);
        T? replaced;
        lock (_gate)
        {
            if (_closed)
            {
                replaced = item;
            }
            else
            {
                replaced = _held;
                if (replaced is not null)
                {
                    _releasedSinceTake++;
                }

                _held = item;
                _heldArrivedAt = arrivedAt;
            }
        }

        replaced?.Dispose();
        return !ReferenceEquals(replaced, item);
    }

    // The held item, which the caller now owns, or null when nothing arrived
    // since the previous take.
    public NewestArrival<T>? Take()
    {
        lock (_gate)
        {
            if (_held is null)
            {
                return null;
            }

            var taken = new NewestArrival<T>(_held, _heldArrivedAt, _releasedSinceTake);
            _held = null;
            _releasedSinceTake = 0;
            return taken;
        }
    }

    public void Dispose()
    {
        T? held;
        lock (_gate)
        {
            _closed = true;
            held = _held;
            _held = null;
        }

        held?.Dispose();
    }
}

public sealed record NewestArrival<T>(T Item, long ArrivedAt, long ReleasedBeforeTake);

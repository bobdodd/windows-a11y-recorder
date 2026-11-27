using Recorder.Contracts;

namespace Recorder.Collectors.Android;

public enum AndroidLiveTreeState { Waiting, Observed, Unreadable, Unavailable, Stopped }

public sealed record AndroidLiveTreeItems(
    long SnapshotIndex, long ReceivedAtNanoseconds, IReadOnlyList<AndroidTreeNode> Nodes,
    bool Truncated, int MissingChildren);

public sealed record AndroidLiveWindowSnapshot(AndroidAccessibilityWindow Window, AndroidLiveTreeItems? Items);

public sealed record AndroidLiveTreeSnapshot(
    AndroidLiveTreeState State, long? Index, long ReceivedAtNanoseconds,
    IReadOnlyList<AndroidLiveWindowSnapshot> Windows, string? Problem, bool EarlierUpdateMissing);

/// <summary>
/// Read-only view of the latest reported windows and the last nodes taken
/// for each. No queue, protocol requests, archives, or calls into the UI.
/// </summary>
public sealed class AndroidLiveTree
{
    private readonly object _gate = new();
    private AndroidLiveTreeSnapshot _current = new(AndroidLiveTreeState.Waiting, null, 0, [], null, false);

    public AndroidLiveTreeSnapshot Current => Volatile.Read(ref _current);

    internal void Publish(AndroidAccessibilityTree tree)
    {
        lock (_gate)
        {
            var previous = _current;
            if (Ended || tree.Index <= 0 || (previous.Index is { } last && tree.Index <= last))
            {
                return;
            }

            var gap = previous.State == AndroidLiveTreeState.Unreadable ||
                (previous.Index is { } index && tree.Index - index > 1) ||
                (previous.Index is null && tree.Index > 1);
            var old = new Dictionary<(int Display, int Id), AndroidLiveWindowSnapshot>();
            if (!tree.EveryWindow && !gap)
            {
                foreach (var entry in previous.Windows)
                {
                    old.TryAdd((entry.Window.DisplayId, entry.Window.Id), entry);
                }
            }

            var windows = new List<AndroidLiveWindowSnapshot>(tree.Windows.Count);
            foreach (var window in tree.Windows)
            {
                AndroidLiveTreeItems? items = null;
                if (window.Items is { } nodes)
                {
                    items = new(tree.Index, tree.ReceivedAtNanoseconds, nodes, window.Truncated, window.MissingChildren);
                }
                else if (old.TryGetValue((window.DisplayId, window.Id), out var existing) &&
                    existing.Window.Type == window.Type &&
                    existing.Window.TransitionTimeMilliseconds == window.TransitionTimeMilliseconds)
                {
                    items = existing.Items;
                }

                // Keep node lists only on Items, with their own provenance.
                windows.Add(new(window with { Items = null }, items));
            }

            Volatile.Write(ref _current, new(AndroidLiveTreeState.Observed, tree.Index, tree.ReceivedAtNanoseconds,
                windows.AsReadOnly(), tree.Problem, gap));
        }
    }

    internal void Unreadable()
    {
        lock (_gate)
        {
            if (!Ended)
            {
                Volatile.Write(ref _current, _current with { State = AndroidLiveTreeState.Unreadable });
            }
        }
    }

    internal void Unavailable(string reason) => End(AndroidLiveTreeState.Unavailable, reason);

    internal void Stop() => End(AndroidLiveTreeState.Stopped, null);

    private bool Ended => _current.State is AndroidLiveTreeState.Unavailable or AndroidLiveTreeState.Stopped;

    private void End(AndroidLiveTreeState state, string? reason)
    {
        lock (_gate)
        {
            if (!Ended || (state == AndroidLiveTreeState.Stopped && _current.State != AndroidLiveTreeState.Stopped))
            {
                Volatile.Write(ref _current, new(state, null, 0, [], reason, false));
            }
        }
    }
}

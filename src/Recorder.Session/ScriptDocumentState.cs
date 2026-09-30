using System.Text.Json;

namespace Recorder.Session;

/// <summary>A timer scheduled and not yet finished: its scheduling record, when it was scheduled, and when it last ran.</summary>
/// <param name="LastRunTime">For an interval timer, the time of its last recorded run, or null before the first.</param>
public sealed record PendingTimer(JsonElement Scheduled, long ScheduledTime, long? LastRunTime);

/// <summary>
/// The script state of one document, rebuilt from its listener and timer
/// records: the event listeners registered and not removed, and the timers
/// scheduled and not finished. Records are kept as the payloads they were
/// read from. A timeout, animation frame, or idle callback finishes when it
/// runs or is cancelled; an interval timer only when it is cancelled.
/// </summary>
public sealed class ScriptDocumentState
{
    private readonly Dictionary<string, JsonElement> _listeners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingTimer> _timers = new(StringComparer.Ordinal);

    /// <summary>The registration records of the listeners registered and not removed, by listener identity.</summary>
    public IReadOnlyDictionary<string, JsonElement> Listeners => _listeners;

    /// <summary>The timers scheduled and not finished, by timer identity.</summary>
    public IReadOnlyDictionary<string, PendingTimer> Timers => _timers;

    /// <summary>The event key and time of the record that last changed the state, or -1.</summary>
    public long EventKey { get; internal set; } = -1;

    public long Time { get; internal set; }

    /// <summary>Applies one browser.listener or browser.timer record. Returns true when it was a state record.</summary>
    public bool Apply(long eventKey, long time, string eventType, JsonElement payload)
    {
        switch (eventType)
        {
            case "listener-registered" when Text(payload, "listenerId") is { } id:
                _listeners[id] = payload.Clone();
                break;
            case "listener-removed" when Text(payload, "listenerId") is { } id:
                _listeners.Remove(id);
                break;
            case "timer-scheduled" when Text(payload, "timerId") is { } id:
                _timers[id] = new PendingTimer(payload.Clone(), time, null);
                break;
            case "timer-fired" when Text(payload, "timerId") is { } id:
                if (Text(payload, "timerKind") == "interval" && _timers.TryGetValue(id, out var timer))
                {
                    _timers[id] = timer with { LastRunTime = time };
                }
                else
                {
                    _timers.Remove(id);
                }
                break;
            case "timer-cancelled" when Text(payload, "timerId") is { } id:
                _timers.Remove(id);
                break;
            default:
                return false;
        }
        EventKey = eventKey;
        Time = time;
        return true;
    }

    internal void Load(IEnumerable<JsonElement> listeners, IEnumerable<PendingTimer> timers, long eventKey, long time)
    {
        _listeners.Clear();
        foreach (var listener in listeners)
        {
            _listeners[Text(listener, "listenerId")!] = listener.Clone();
        }
        _timers.Clear();
        foreach (var timer in timers)
        {
            _timers[Text(timer.Scheduled, "timerId")!] = timer with { Scheduled = timer.Scheduled.Clone() };
        }
        EventKey = eventKey;
        Time = time;
    }

    internal static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// The accessibility data of one document, rebuilt from its accessibility
/// checkpoint records. A checkpoint is one serialized update batch, not the
/// whole accessibility tree (see the accessibility checkpoint evidence
/// model), so the state is, for each DOM node, the latest node record that
/// named it and the time of its batch. The removal of an accessibility node
/// is not recorded, so a record is kept until a later batch names the DOM
/// node again.
/// </summary>
public sealed class AccessibilityDocumentState
{
    private readonly Dictionary<long, (JsonElement Record, long Time)> _nodes = [];
    private List<JsonElement>? _pending;
    private long _pendingTime;

    /// <summary>The latest accessibility node record of each DOM node, by DOM node identity, with the time of its batch.</summary>
    public IReadOnlyDictionary<long, (JsonElement Record, long Time)> Nodes => _nodes;

    /// <summary>True between a checkpoint's start record and its completion.</summary>
    public bool IsOpen => _pending is not null;

    /// <summary>The event key and time of the last completed checkpoint applied, or -1.</summary>
    public long EventKey { get; internal set; } = -1;

    public long Time { get; internal set; }

    /// <summary>Applies one browser.accessibility record. Returns true when it was a state record.</summary>
    public bool Apply(long eventKey, long time, string eventType, JsonElement payload)
    {
        switch (eventType)
        {
            case "accessibility-checkpoint-started":
                _pending = [];
                _pendingTime = time;
                return true;
            case "accessibility-checkpoint-node":
                _pending?.Add(payload.Clone());
                return true;
            case "accessibility-checkpoint-completed":
                if (_pending is null)
                {
                    return true;
                }
                foreach (var node in _pending)
                {
                    if (node.TryGetProperty("domNodeId", out var dom) && dom.ValueKind == JsonValueKind.Number)
                    {
                        _nodes[dom.GetInt64()] = (node, _pendingTime);
                    }
                }
                _pending = null;
                EventKey = eventKey;
                Time = time;
                return true;
            default:
                return false;
        }
    }

    internal void Load(IEnumerable<(JsonElement Record, long Time)> nodes, long eventKey, long time)
    {
        _pending = null;
        _nodes.Clear();
        foreach (var (record, recordTime) in nodes)
        {
            _nodes[record.GetProperty("domNodeId").GetInt64()] = (record.Clone(), recordTime);
        }
        EventKey = eventKey;
        Time = time;
    }
}

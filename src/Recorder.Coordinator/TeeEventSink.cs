using Recorder.Contracts;

namespace Recorder.Coordinator;

/// <summary>
/// Passes each event to the event log and to the database writer. The event
/// log's answer is returned to the collector, because the event log is still
/// the recording the player reads. The database writer counts the events it
/// refuses itself.
/// </summary>
internal sealed class TeeEventSink(IRecorderEventSink primary, IRecorderEventSink secondary)
    : IRecorderEventSink
{
    public bool TryWrite(RecorderEvent record)
    {
        var accepted = primary.TryWrite(record);
        secondary.TryWrite(record);
        return accepted;
    }
}

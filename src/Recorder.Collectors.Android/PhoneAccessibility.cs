using System.Threading.Channels;

namespace Recorder.Collectors.Android;

/// <summary>
/// The phone's accessibility events, through the companion app's
/// connection (android-implementation-plan.md, slice 7).
/// </summary>
public interface IPhoneAccessibilityEvents
{
    /// <summary>
    /// Asks the companion app for every accessibility event from now on.
    /// The events come in the order they arrived, and the reader completes
    /// when the connection ends. Null when there is no connection to ask.
    /// </summary>
    Task<ChannelReader<ReceivedAccessibilityEvent>?> RequestAccessibilityEventsAsync(CancellationToken cancellation);
}

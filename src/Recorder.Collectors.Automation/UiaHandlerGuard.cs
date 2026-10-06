namespace Recorder.Collectors.Automation;

// Runs one UI Automation event handler's work. UI Automation calls the
// handlers from its own threads; a fault in the recorder's handling of one
// event is reported to the caller, which counts it and states the count when
// the collector stops, so the collector keeps running.
public static class UiaHandlerGuard
{
    public static void Run(Action handle, Action onFault)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(onFault);
        try
        {
            handle();
        }
        catch (Exception)
        {
            onFault();
        }
    }
}

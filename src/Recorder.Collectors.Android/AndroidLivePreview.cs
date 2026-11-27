namespace Recorder.Collectors.Android;

public enum AndroidLivePreviewState
{
    WaitingForFrame,
    ShowingFrames,
    StreamUnavailable,
    FramesUnavailable,
    Stopped
}

/// <summary>Pixels also used by the recording's PNG encoder. Treat the shared buffer as read-only.</summary>
public sealed record AndroidLivePreviewFrame(ulong Sequence, int Width, int Height, ReadOnlyMemory<byte> Rgb)
{
    public long? PhoneNanoseconds { get; init; }
}

public sealed record AndroidLivePreviewSnapshot(AndroidLivePreviewState State, int StreamSession, AndroidLivePreviewFrame? Frame);

/// <summary>
/// Latest image only, not a stream of events or an archive channel. Readers
/// never block the publisher. PNG workers may finish conversion out of order,
/// and neither an old screen size nor a lower sequence can replace the latest.
/// </summary>
public sealed class AndroidLivePreview
{
    private readonly object _gate = new();
    private AndroidLivePreviewSnapshot _current = new(AndroidLivePreviewState.WaitingForFrame, 0, null);

    public AndroidLivePreviewSnapshot Current => Volatile.Read(ref _current);

    internal void BeginSession(int session)
    {
        lock (_gate)
        {
            if (Accepting && session > _current.StreamSession)
            {
                Volatile.Write(ref _current, new(AndroidLivePreviewState.WaitingForFrame, session, null));
            }
        }
    }

    internal void Publish(int session, ulong sequence, int width, int height, byte[] rgb, long? phoneNanoseconds = null)
    {
        lock (_gate)
        {
            if (Accepting && session == _current.StreamSession &&
                (_current.Frame is null || sequence > _current.Frame.Sequence))
            {
                Volatile.Write(ref _current, new(AndroidLivePreviewState.ShowingFrames, session,
                    new AndroidLivePreviewFrame(sequence, width, height, rgb) { PhoneNanoseconds = phoneNanoseconds }));
            }
        }
    }

    internal void StreamUnavailable() => End(AndroidLivePreviewState.StreamUnavailable);

    internal void FramesUnavailable() => End(AndroidLivePreviewState.FramesUnavailable);

    internal void Stop() => End(AndroidLivePreviewState.Stopped);

    private void End(AndroidLivePreviewState state)
    {
        lock (_gate)
        {
            if (Accepting ||
                (_current.State == AndroidLivePreviewState.FramesUnavailable && state == AndroidLivePreviewState.StreamUnavailable) ||
                (_current.State != AndroidLivePreviewState.Stopped && state == AndroidLivePreviewState.Stopped))
            {
                Volatile.Write(ref _current, new(state, _current.StreamSession, null));
            }
        }
    }

    private bool Accepting => _current.State is AndroidLivePreviewState.WaitingForFrame or AndroidLivePreviewState.ShowingFrames;
}

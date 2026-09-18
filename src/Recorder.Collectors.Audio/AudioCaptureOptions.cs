namespace Recorder.Collectors.Audio;

public sealed record AudioCaptureOptions(
    bool CaptureMicrophone,
    bool CaptureSystemAudio)
{
    public static AudioCaptureOptions All { get; } = new(true, true);
}

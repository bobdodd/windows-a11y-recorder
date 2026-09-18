using System.Text.Json;

namespace Recorder.Session;

public sealed record SessionManifest(
    string SchemaVersion,
    string SessionId,
    string Status,
    DateTimeOffset StartedUtc,
    DateTimeOffset? EndedUtc,
    long? DurationNanoseconds,
    long ClockFrequency,
    long ClockOriginTimestamp,
    string OperatingSystem,
    string Runtime,
    string Architecture,
    SessionRecordingConfiguration Configuration,
    IReadOnlyList<SessionCollectorManifest> Collectors,
    IReadOnlyList<SessionArtifact> Artifacts,
    long AcceptedEventCount,
    long DroppedEventCount,
    string? Failure);

public sealed record SessionRecordingConfiguration(
    bool CaptureKeyboardAndMouse,
    bool CaptureUiAutomation,
    bool CaptureForegroundWindow,
    bool CaptureDesktopFrames,
    int FramesPerSecond,
    bool CaptureMicrophone,
    bool CaptureSystemAudio);

public sealed record SessionCollectorManifest(
    string CollectorType,
    string Implementation,
    string ImplementationVersion,
    IReadOnlyList<string> Channels,
    string CaptureMethod,
    string? Capability,
    IReadOnlyList<string> Limitations,
    string Lifecycle,
    string Health);

public sealed record SessionArtifact(
    string Path,
    long SizeBytes,
    string Sha256);

public static class SessionManifestWriter
{
    public static async Task WriteAsync(
        string path,
        SessionManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.Read,
                         bufferSize: 4096,
                         FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                manifest,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true },
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }
}

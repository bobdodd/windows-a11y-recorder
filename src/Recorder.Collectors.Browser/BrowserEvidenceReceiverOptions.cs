namespace Recorder.Collectors.Browser;

public sealed record BrowserEvidenceReceiverOptions
{
    public string PipeName { get; init; } =
        $"windows-a11y-recorder-browser-{Guid.NewGuid():N}";

    public string AuthenticationToken { get; init; } =
        Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public int MaximumMessageBytes { get; init; } = 4 * 1024 * 1024;
    public string BrowserInstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public string? ChromiumExecutablePath { get; init; }
    public string? StartUrl { get; init; }
    public string? ProfileDirectory { get; init; }
}

public sealed record BrowserEvidenceConnectionInfo(
    string PipeName,
    string AuthenticationToken,
    string ProtocolVersion,
    string BrowserInstanceId,
    int MaximumMessageBytes);

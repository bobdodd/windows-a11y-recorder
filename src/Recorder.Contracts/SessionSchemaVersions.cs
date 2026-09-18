namespace Recorder.Contracts;

public static class SessionSchemaVersions
{
    public const string Manifest = "1.1";
    public const string Event = "1.1";
    public const string LegacyEvent = "1.0";

    public static bool IsSupportedEvent(string? version) =>
        version is Event or LegacyEvent;
}

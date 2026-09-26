using System.Text.Json;
using Recorder.Contracts;
using Recorder.Database;

namespace Recorder.Tests;

internal sealed class PassThroughSecretProtector : IDatabaseSecretProtector
{
    public byte[] Protect(byte[] secret) => secret;
    public byte[] Unprotect(byte[] protectedSecret) => protectedSecret;
}

internal static class DatabaseTestSupport
{
    public const string BinaryVariable = "RECORDER_POSTGRES_BIN";

    /// <summary>
    /// The PostgreSQL programs the integration tests run: the directory named
    /// by RECORDER_POSTGRES_BIN, or the binaries fetched into the repository by
    /// scripts/Get-PostgresBinaries.ps1. The tests fail rather than skip when
    /// neither is present, so a run cannot pass without exercising PostgreSQL.
    /// </summary>
    public static string BinaryDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(BinaryVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "windows-a11y-recorder.slnx")))
            {
                var fetched = Path.Combine(directory.FullName, ".postgres", "pgsql", "bin");
                if (Directory.Exists(fetched))
                {
                    return fetched;
                }

                break;
            }
        }

        throw new InvalidOperationException(
            $"PostgreSQL binaries were not found. Run scripts/Get-PostgresBinaries.ps1, " +
            $"or set {BinaryVariable} to a directory holding initdb and pg_ctl.");
    }

    public static CollectorDescriptor Collector(string type = "test.collector", params string[] channels) =>
        new(
            type,
            Guid.NewGuid().ToString("N"),
            "Test collector",
            "1.0.0",
            "1.0",
            channels.Length == 0 ? ["test.channel"] : channels,
            "test-capture");

    public static RecorderEvent Event(
        string sessionId,
        CollectorDescriptor collector,
        ulong sequence,
        long monotonicNanoseconds,
        string channel = "test.channel",
        string eventType = "test-event",
        object? payload = null,
        params string[] qualityFlags) =>
        RecorderEventFactory.Create(
            sessionId,
            collector,
            channel,
            sequence,
            monotonicNanoseconds,
            eventType,
            payload ?? new { value = sequence },
            qualityFlags);

    public static RecordingDefinition Definition(string sessionKey) =>
        new(
            sessionKey,
            new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero),
            10_000_000,
            123_456,
            "Test OS",
            "Test runtime",
            "X64",
            new RecordingCaptureSettings(true, true, true, true, 5, false, false));

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

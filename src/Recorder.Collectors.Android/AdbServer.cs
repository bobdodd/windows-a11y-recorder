using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace Recorder.Collectors.Android;

/// <summary>
/// An adb server found running: the version it answers with, and the
/// adb programs running on the PC, which include the one that started it.
/// A program whose path could not be read is given by name and process ID.
/// </summary>
public sealed record AdbServerInfo(int Version, IReadOnlyList<string> Programs);

/// <summary>
/// Finds out whether an adb server is already running, before the
/// recorder starts its own (android-implementation-plan.md, slice 1, "adb
/// server").
/// </summary>
public interface IAdbServerProbe
{
    /// <summary>The running server, or null when none answers.</summary>
    Task<AdbServerInfo?> ReadAsync(CancellationToken cancellation);
}

/// <summary>
/// Asks adb's server port for its version, as adb's own client does
/// before it uses a server: the request "host:version" with its length in
/// 4 hexadecimal digits, answered by "OKAY" and the version, also as
/// hexadecimal with a length. A client whose version differs stops that
/// server and starts its own, which is what the recorder avoids.
/// </summary>
public sealed class AdbServerProbe : IAdbServerProbe
{
    /// <summary>adb's server port when ANDROID_ADB_SERVER_PORT is not set.</summary>
    public const int DefaultPort = 5037;

    private readonly int _port;

    public AdbServerProbe(int? port = null) => _port = port ?? ConfiguredPort();

    public async Task<AdbServerInfo?> ReadAsync(CancellationToken cancellation)
    {
        int version;
        using (var client = new TcpClient())
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await client.ConnectAsync("127.0.0.1", _port, limit.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException && !cancellation.IsCancellationRequested)
            {
                return null;
            }

            var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(AdbWire.Request("host:version"));
            await stream.WriteAsync(request, limit.Token).ConfigureAwait(false);
            var reply = new StringBuilder();
            var buffer = new byte[64];
            try
            {
                int read;
                while (reply.Length < 12 && (read = await stream.ReadAsync(buffer, limit.Token).ConfigureAwait(false)) > 0)
                {
                    reply.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException && !cancellation.IsCancellationRequested)
            {
                // Something holds the port but did not answer as adb does.
            }

            version = AdbWire.ParseVersionReply(reply.ToString()) ?? 0;
        }

        return new AdbServerInfo(version, RunningAdbPrograms());
    }

    private static int ConfiguredPort() =>
        int.TryParse(Environment.GetEnvironmentVariable("ANDROID_ADB_SERVER_PORT"), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and < 65536
            ? port
            : DefaultPort;

    private static IReadOnlyList<string> RunningAdbPrograms()
    {
        var programs = new List<string>();
        foreach (var process in Process.GetProcessesByName("adb"))
        {
            using (process)
            {
                try
                {
                    programs.Add(process.MainModule?.FileName ?? $"adb (process {process.Id})");
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    programs.Add($"adb (process {process.Id})");
                }
            }
        }

        return programs;
    }
}

/// <summary>The parts of adb's server protocol the probe uses.</summary>
public static class AdbWire
{
    /// <summary>A request with its length as 4 hexadecimal digits in front.</summary>
    public static string Request(string service) =>
        service.Length.ToString("x4", CultureInfo.InvariantCulture) + service;

    /// <summary>
    /// The version in a reply such as "OKAY00040029" (version 41), or null
    /// for a reply that is not adb's.
    /// </summary>
    public static int? ParseVersionReply(string reply)
    {
        if (reply.Length < 12 || !reply.StartsWith("OKAY", StringComparison.Ordinal) ||
            !int.TryParse(reply.AsSpan(4, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var length) ||
            reply.Length < 8 + length ||
            !int.TryParse(reply.AsSpan(8, length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var version))
        {
            return null;
        }

        return version;
    }
}

using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

namespace Recorder.Collectors.Browser;

public sealed class ChromiumLauncher : IAsyncDisposable
{
    public const string LogFileEnvironmentVariable =
        "A11Y_RECORDER_CHROMIUM_LOG_FILE";

    // The instrumented browser reads this variable before Chromium logging
    // starts, which is the only point at which a bridge initialization failure
    // can still be described. The name is declared in the native bridge as
    // kBridgeLogFileEnvironmentWide.
    public const string BridgeLogFileEnvironmentVariable =
        "A11Y_RECORDER_BRIDGE_LOG_FILE";

    // Declared in the native bridge as kBridgeInitializationFailureExitCode. A
    // recorder-launched browser whose bridge cannot initialize exits with this
    // code, so a failed bridge is never reported as a normal browser exit.
    public const int BridgeInitializationFailureExitCode = 0xA11B;

    // Written by the browser startup hook that chromium/integrate.py inserts,
    // immediately before it returns the failure exit code. The two texts must
    // stay identical or the reason cannot be recovered from the log.
    private const string BridgeInitializationFailureMarker =
        "Recorder process bridge initialization failed:";

    private static readonly TimeSpan StartupStabilityWindow =
        TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly Func<bool> _isCurrentProcessElevated;
    private Process? _process;
    private string? _bridgeDiagnosticLogPath;

    public ChromiumLauncher()
        : this(IsCurrentProcessElevated)
    {
    }

    internal ChromiumLauncher(Func<bool> isCurrentProcessElevated)
    {
        ArgumentNullException.ThrowIfNull(isCurrentProcessElevated);
        _isCurrentProcessElevated = isCurrentProcessElevated;
    }

    public Process? Process => _process;

    public async Task<Process> LaunchAsync(
        string executablePath,
        string profileDirectory,
        BrowserEvidenceConnectionInfo connection,
        string? startUrl,
        int? remoteDebuggingPort,
        string? bridgeDiagnosticLogPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        ArgumentNullException.ThrowIfNull(connection);
        if (_process is not null)
        {
            throw new InvalidOperationException(
                "The instrumented browser has already been launched.");
        }
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The bundled instrumented Chromium executable was not found.",
                executablePath);
        }
        if (_isCurrentProcessElevated())
        {
            throw new InvalidOperationException(
                "Instrumented Chromium cannot be launched from an elevated " +
                "recorder process. Start Windows A11y Recorder from a " +
                "standard, non-administrator Windows session.");
        }

        Directory.CreateDirectory(profileDirectory);
        var startInfo = CreateStartInfo(
            executablePath,
            profileDirectory,
            startUrl,
            Environment.GetEnvironmentVariable(LogFileEnvironmentVariable),
            remoteDebuggingPort,
            bridgeDiagnosticLogPath);
        _bridgeDiagnosticLogPath =
            string.IsNullOrWhiteSpace(bridgeDiagnosticLogPath)
                ? null
                : Path.GetFullPath(bridgeDiagnosticLogPath);
        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "Instrumented Chromium did not start.");
        _process = process;

        try
        {
            var bootstrap = new ChromiumBootstrapMessage(
                "a11y-recorder-bootstrap",
                connection.ProtocolVersion,
                connection.PipeName,
                connection.AuthenticationToken,
                connection.BrowserInstanceId,
                connection.MaximumMessageBytes);
            var json = JsonSerializer.Serialize(bootstrap, JsonOptions);
            await process.StandardInput.WriteLineAsync(
                json.AsMemory(),
                cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
            await Task.Delay(StartupStabilityWindow, cancellationToken)
                .ConfigureAwait(false);
            if (process.HasExited)
            {
                throw new BrowserStartupExitException(
                    DescribeStartupExit(
                        process.ExitCode,
                        _bridgeDiagnosticLogPath));
            }
            return process;
        }
        catch (Exception exception)
        {
            int? exitCode = null;
            try
            {
                if (process.HasExited)
                {
                    exitCode = process.ExitCode;
                }
            }
            catch (InvalidOperationException)
            {
            }

            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            if (exitCode.HasValue &&
                exception is not OperationCanceledException &&
                exception is not BrowserStartupExitException)
            {
                throw new BrowserStartupExitException(
                    DescribeStartupExit(
                        exitCode.Value,
                        _bridgeDiagnosticLogPath),
                    exception);
            }
            throw;
        }
    }

    // A bridge initialization failure is reported by its own exit code, so the
    // launcher can name the failure instead of reporting only a number. The
    // reason itself is only available when the browser was able to record it.
    internal static string DescribeStartupExit(
        int exitCode,
        string? bridgeDiagnosticLogPath)
    {
        if (exitCode != BridgeInitializationFailureExitCode)
        {
            return "Instrumented Chromium exited during startup with exit " +
                $"code {exitCode}.";
        }

        var reason = ReadBridgeInitializationFailure(bridgeDiagnosticLogPath);
        if (reason is not null)
        {
            return "Instrumented Chromium could not initialize its recorder " +
                $"bridge: {reason}";
        }
        if (bridgeDiagnosticLogPath is null)
        {
            return "Instrumented Chromium could not initialize its recorder " +
                "bridge. No bridge diagnostic log was configured for this " +
                "session, so the reason was not recorded.";
        }
        return "Instrumented Chromium could not initialize its recorder " +
            "bridge. No reason was recorded in " +
            $"{bridgeDiagnosticLogPath}.";
    }

    private static string? ReadBridgeInitializationFailure(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string? latest = null;
            while (reader.ReadLine() is { } line)
            {
                var marker = line.IndexOf(
                    BridgeInitializationFailureMarker,
                    StringComparison.Ordinal);
                if (marker >= 0)
                {
                    latest = line[marker..].Trim();
                }
            }
            return latest;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ProcessStartInfo CreateStartInfo(
        string executablePath,
        string profileDirectory,
        string? startUrl,
        string? diagnosticLogPath = null,
        int? remoteDebuggingPort = null,
        string? bridgeDiagnosticLogPath = null)
    {
        var result = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = false
        };
        RemoveSensitiveEnvironmentVariables(result);
        if (!string.IsNullOrWhiteSpace(bridgeDiagnosticLogPath))
        {
            var fullBridgeLogPath = Path.GetFullPath(bridgeDiagnosticLogPath);
            var bridgeLogDirectory = Path.GetDirectoryName(fullBridgeLogPath);
            if (!string.IsNullOrEmpty(bridgeLogDirectory))
            {
                Directory.CreateDirectory(bridgeLogDirectory);
            }
            result.Environment[BridgeLogFileEnvironmentVariable] =
                fullBridgeLogPath;
        }
        result.ArgumentList.Add("--a11y-recorder-bootstrap=stdin");
        result.ArgumentList.Add($"--user-data-dir={Path.GetFullPath(profileDirectory)}");
        result.ArgumentList.Add("--no-first-run");
        result.ArgumentList.Add("--no-default-browser-check");
        result.ArgumentList.Add("--disable-background-mode");
        result.ArgumentList.Add("--force-renderer-accessibility");
        if (remoteDebuggingPort is not null)
        {
            if (remoteDebuggingPort is <= 0 or > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(remoteDebuggingPort));
            }
            result.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
            result.ArgumentList.Add(
                $"--remote-debugging-port={remoteDebuggingPort.Value}");
        }
        if (!string.IsNullOrWhiteSpace(diagnosticLogPath))
        {
            var fullLogPath = Path.GetFullPath(diagnosticLogPath);
            var logDirectory = Path.GetDirectoryName(fullLogPath);
            if (!string.IsNullOrEmpty(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
            result.ArgumentList.Add("--enable-logging");
            result.ArgumentList.Add($"--log-file={fullLogPath}");
        }
        if (!string.IsNullOrWhiteSpace(startUrl))
        {
            result.ArgumentList.Add(startUrl);
        }
        return result;
    }

    private static void RemoveSensitiveEnvironmentVariables(
        ProcessStartInfo startInfo)
    {
        var names = startInfo.Environment.Keys.ToArray();
        foreach (var name in names)
        {
            var normalized = name.ToUpperInvariant();
            if (normalized.Contains("TOKEN", StringComparison.Ordinal) ||
                normalized.Contains("SECRET", StringComparison.Ordinal) ||
                normalized.Contains("PASSWORD", StringComparison.Ordinal) ||
                normalized.Contains("AUTHORIZATION", StringComparison.Ordinal) ||
                normalized.Contains("API_KEY", StringComparison.Ordinal) ||
                normalized.Contains("ACCESS_KEY", StringComparison.Ordinal) ||
                normalized.Contains("PRIVATE_KEY", StringComparison.Ordinal))
            {
                startInfo.Environment.Remove(name);
            }
        }
    }

    private static bool IsCurrentProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(
            WindowsBuiltInRole.Administrator);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                _ = process.CloseMainWindow();
                try
                {
                    await process.WaitForExitAsync(cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

// Distinguishes an exit observed inside the startup stability window from any
// other launch failure, so a described exit is never described again.
public sealed class BrowserStartupExitException : InvalidOperationException
{
    public BrowserStartupExitException(string message)
        : base(message)
    {
    }

    public BrowserStartupExitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed record ChromiumBootstrapMessage(
    string Kind,
    string ProtocolVersion,
    string PipeName,
    string AuthenticationToken,
    string BrowserInstanceId,
    int MaximumMessageBytes);

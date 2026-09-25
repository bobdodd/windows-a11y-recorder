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

    // Declared in the native bridge as kPrintProtocolVersionSwitch,
    // kProtocolVersionQueryExitCode, and kProtocolVersionOutputPrefix. The
    // browser is asked which protocol version it was built with, because a file
    // written beside the executable can be separated from the executable it
    // describes.
    public const string ProtocolVersionQuerySwitch =
        "--a11y-recorder-print-protocol-version";

    public const int ProtocolVersionQueryExitCode = 0xA11C;

    public const string ProtocolVersionOutputPrefix =
        "a11y-recorder-protocol-version=";

    private static readonly TimeSpan StartupStabilityWindow =
        TimeSpan.FromMilliseconds(500);

    // A browser built before the query existed treats the switch as unknown and
    // starts normally, so the query is always bounded in time.
    private static readonly TimeSpan ProtocolVersionQueryTimeout =
        TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly Func<bool> _isCurrentProcessElevated;
    private Process? _process;
    private string? _bridgeDiagnosticLogPath;
    private Task? _exitWatch;
    private volatile bool _stopRequested;

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

    // Raised once when a browser that completed startup exits, whether the
    // recorder asked it to or not. Startup exits are reported by the launch
    // failure instead, so a browser that never started is not reported twice.
    public event Action<ChromiumExit>? Exited;

    public async Task<Process> LaunchAsync(
        string executablePath,
        string profileDirectory,
        BrowserEvidenceConnectionInfo connection,
        string? startUrl,
        int? remoteDebuggingPort,
        string? bridgeDiagnosticLogPath,
        CancellationToken cancellationToken,
        string? chromiumLogPath = null)
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
            // A path set by a validation harness is preserved rather than
            // replaced, as it is for the bridge log.
            Environment.GetEnvironmentVariable(LogFileEnvironmentVariable) ??
                chromiumLogPath,
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
        _stopRequested = false;

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
            _exitWatch = WatchForExitAsync(process);
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

    // A browser that answers names the version it speaks. A browser that does
    // not answer is not assumed to agree: its version is unknown, and the
    // bridge remains the thing that reports a mismatch.
    public enum BrowserProtocolVersionOutcome
    {
        Reported,
        Unknown
    }

    public sealed record BrowserProtocolVersionQueryResult(
        BrowserProtocolVersionOutcome Outcome,
        string? Version,
        string Description);

    public static ProcessStartInfo CreateProtocolVersionQueryStartInfo(
        string executablePath,
        string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);

        var result = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        RemoveSensitiveEnvironmentVariables(result);
        result.ArgumentList.Add(ProtocolVersionQuerySwitch);
        // A browser that does not recognize the query switch would otherwise
        // open a window nobody asked for. This switch predates the query, so
        // such a build still honors it.
        result.ArgumentList.Add("--no-startup-window");
        result.ArgumentList.Add(
            $"--user-data-dir={Path.GetFullPath(profileDirectory)}");
        result.ArgumentList.Add("--no-first-run");
        result.ArgumentList.Add("--no-default-browser-check");
        return result;
    }

    public static async Task<BrowserProtocolVersionQueryResult>
        QueryProtocolVersionAsync(
            string executablePath,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The bundled instrumented Chromium executable was not found.",
                executablePath);
        }

        var profileDirectory = Path.Combine(
            Path.GetTempPath(),
            "Windows A11y Recorder",
            "ProtocolVersionQuery",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profileDirectory);
        try
        {
            var startInfo = CreateProtocolVersionQueryStartInfo(
                executablePath,
                profileDirectory);
            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException(
                    "Instrumented Chromium did not start for the protocol " +
                    "version query.");
            var output = process.StandardOutput.ReadToEndAsync(
                cancellationToken);
            using var timeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProtocolVersionQueryTimeout);
            int? exitCode = null;
            try
            {
                await process.WaitForExitAsync(timeout.Token)
                    .ConfigureAwait(false);
                exitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                TerminateProcessTree(process);
            }

            var text = string.Empty;
            try
            {
                text = await output.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
            }
            return InterpretProtocolVersionQuery(exitCode, text);
        }
        finally
        {
            try
            {
                Directory.Delete(profileDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal static BrowserProtocolVersionQueryResult
        InterpretProtocolVersionQuery(int? exitCode, string? output)
    {
        var reported = ReadReportedProtocolVersion(output);
        if (reported is null)
        {
            return new BrowserProtocolVersionQueryResult(
                BrowserProtocolVersionOutcome.Unknown,
                null,
                exitCode is null
                    ? "The browser did not report a protocol version before " +
                        "the query timed out, so it was built before the " +
                        "query existed."
                    : "The browser exited with code " +
                        $"{exitCode.Value} without reporting a protocol " +
                        "version, so it was built before the query existed.");
        }
        if (exitCode != ProtocolVersionQueryExitCode)
        {
            // Reported text without the query exit code means the process did
            // something other than answer and stop, so the answer is not
            // trustworthy on its own.
            return new BrowserProtocolVersionQueryResult(
                BrowserProtocolVersionOutcome.Unknown,
                null,
                $"The browser reported protocol version {reported} but " +
                    (exitCode is null
                        ? "did not exit for the query"
                        : $"exited with code {exitCode.Value} instead of " +
                            $"{ProtocolVersionQueryExitCode}") +
                    ", so the report was not accepted.");
        }
        return new BrowserProtocolVersionQueryResult(
            BrowserProtocolVersionOutcome.Reported,
            reported,
            $"The browser reported protocol version {reported}.");
    }

    // A reported version is only acted on when the browser actually reported
    // one. An unknown version is left to the bridge, which rejects a mismatched
    // bootstrap and names both versions.
    public static void EnsureProtocolVersionIsCompatible(
        BrowserProtocolVersionQueryResult query,
        string expectedVersion,
        string executablePath)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedVersion);

        if (query.Outcome != BrowserProtocolVersionOutcome.Reported ||
            query.Version is null ||
            string.Equals(
                query.Version,
                expectedVersion,
                StringComparison.Ordinal))
        {
            return;
        }

        throw new BrowserProtocolMismatchException(
            "This recorder speaks browser evidence protocol version " +
            $"{expectedVersion}, and {executablePath} speaks " +
            $"{query.Version}. The recorder and the instrumented browser were " +
            "built from different revisions, so no session was started. " +
            "Rebuild the instrumented browser and republish the recorder from " +
            "the same revision.");
    }

    private static string? ReadReportedProtocolVersion(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        foreach (var line in output.Split(
            '\n',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var marker = line.IndexOf(
                ProtocolVersionOutputPrefix,
                StringComparison.Ordinal);
            if (marker < 0)
            {
                continue;
            }
            var value = line[(marker + ProtocolVersionOutputPrefix.Length)..]
                .Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }
        return null;
    }

    // The exit is observed from the operating system, so it is recorded even
    // when the browser could not say anything before it ended. The exit time
    // is the one Windows reports for the process, which can be unavailable.
    private async Task WatchForExitAsync(Process process)
    {
        int processId;
        try
        {
            processId = process.Id;
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        int exitCode;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        DateTimeOffset? exitedUtc = null;
        try
        {
            exitedUtc = new DateTimeOffset(process.ExitTime.ToUniversalTime());
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        try
        {
            Exited?.Invoke(new ChromiumExit(
                processId,
                exitCode,
                exitedUtc,
                _stopRequested));
        }
        catch
        {
            // A failing observer must not leave the exit watch faulted, because
            // stopping the browser waits for it.
        }
    }

    public static string FormatExitCode(int exitCode) =>
        "0x" + unchecked((uint)exitCode).ToString(
            "X8",
            System.Globalization.CultureInfo.InvariantCulture);

    private static void TerminateProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
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
                // Set only for a browser still running, so an exit that came
                // first is not attributed to the recorder's request.
                _stopRequested = true;
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
            // The exit is reported before the process is released, so the
            // exit code is still readable and the record precedes the stop.
            var exitWatch = _exitWatch;
            _exitWatch = null;
            if (exitWatch is not null && process.HasExited)
            {
                await exitWatch.ConfigureAwait(false);
            }
            process.Dispose();
            _process = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

// An exit of a browser that completed startup. RequestedByRecorder is true
// only when the exit followed the recorder's own request to stop the browser.
public sealed record ChromiumExit(
    int ProcessId,
    int ExitCode,
    DateTimeOffset? ExitedUtc,
    bool RequestedByRecorder);

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

// Raised before a browser is launched, when the browser has reported a protocol
// version the recorder does not speak. Distinct from a startup exit, because
// nothing was started and the operator's action is to rebuild or republish.
public sealed class BrowserProtocolMismatchException : InvalidOperationException
{
    public BrowserProtocolMismatchException(string message)
        : base(message)
    {
    }

    public BrowserProtocolMismatchException(
        string message,
        Exception innerException)
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

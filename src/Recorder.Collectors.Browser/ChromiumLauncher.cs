using System.Diagnostics;
using System.Text.Json;

namespace Recorder.Collectors.Browser;

public sealed class ChromiumLauncher : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private Process? _process;

    public Process? Process => _process;

    public async Task<Process> LaunchAsync(
        string executablePath,
        string profileDirectory,
        BrowserEvidenceConnectionInfo connection,
        string? startUrl,
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

        Directory.CreateDirectory(profileDirectory);
        var startInfo = CreateStartInfo(
            executablePath,
            profileDirectory,
            startUrl);
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
            return process;
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public static ProcessStartInfo CreateStartInfo(
        string executablePath,
        string profileDirectory,
        string? startUrl)
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
        result.ArgumentList.Add("--a11y-recorder-bootstrap=stdin");
        result.ArgumentList.Add($"--user-data-dir={Path.GetFullPath(profileDirectory)}");
        result.ArgumentList.Add("--no-first-run");
        result.ArgumentList.Add("--no-default-browser-check");
        result.ArgumentList.Add("--disable-background-mode");
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

public sealed record ChromiumBootstrapMessage(
    string Kind,
    string ProtocolVersion,
    string PipeName,
    string AuthenticationToken,
    string BrowserInstanceId,
    int MaximumMessageBytes);

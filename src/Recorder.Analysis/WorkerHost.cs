using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recorder.Analysis;

/// <summary>Where the worker keeps its files, for the signed-in user.</summary>
public static class AnalysisLocation
{
    public static string Directory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Windows A11y Recorder",
            "Analysis");

    /// <summary>The name of the mutex the running worker holds.</summary>
    public const string WorkerMutexName = @"Local\WindowsA11yRecorder.AnalysisWorker";

    public static string StatusPath => Path.Combine(Directory, "worker-status.json");
}

/// <summary>
/// The worker's state as a small file the player reads: what it is doing,
/// which profile's model is loaded, and the problems in the settings.
/// </summary>
public sealed record WorkerStatus(string Message, string? LoadedProfile, IReadOnlyList<string> SettingsProblems, DateTime UpdatedUtc, int ProcessId)
{
    public static void Write(string path, WorkerStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var document = new JsonObject
        {
            ["message"] = status.Message,
            ["loadedProfile"] = status.LoadedProfile,
            ["settingsProblems"] = new JsonArray([.. status.SettingsProblems.Select(problem => JsonValue.Create(problem))]),
            ["updatedUtc"] = status.UpdatedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["processId"] = status.ProcessId
        };
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, document.ToJsonString());
        File.Move(temporary, path, overwrite: true);
    }

    public static WorkerStatus? Read(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new WorkerStatus(
                root.GetProperty("message").GetString() ?? "",
                root.GetProperty("loadedProfile").ValueKind == JsonValueKind.String ? root.GetProperty("loadedProfile").GetString() : null,
                [.. root.GetProperty("settingsProblems").EnumerateArray().Select(item => item.GetString() ?? "")],
                DateTime.Parse(root.GetProperty("updatedUtc").GetString()!, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                root.GetProperty("processId").GetInt32());
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or
            InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Starts the llama.cpp server for a locally served profile and stops it
/// when its owner stops. The analysis worker and live scans share a lease.
/// </summary>
public sealed class LocalServerHost(AnalysisSettings settings, string directory, HttpClient client, bool waitForLease = true)
    : ILocalServerHost, IAsyncDisposable
{
    private LocalModelServer? _server;
    private FileStream? _lease;

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_server is { HasExited: false })
        {
            return;
        }

        if (_server is not null)
        {
            await DisposeAsync().ConfigureAwait(false);
        }

        var local = settings.LocalServer ??
            throw new ModelEndpointException("No local model server is set in the analysis settings.");
        _lease = await AcquireAsync(directory, waitForLease, cancellationToken).ConfigureAwait(false);
        try
        {
            _server = await LocalModelServer.StartAsync(
                local, settings.Profiles, directory, client, TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_server is not null)
            {
                await _server.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _server = null;
            _lease?.Dispose();
            _lease = null;
        }
    }

    internal static async Task<FileStream> AcquireAsync(string directory, bool wait, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "llama-server.lock");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 11 or 32 or 33)
            {
                if (!wait)
                {
                    throw new ModelServerBusyException(ex);
                }
                await Task.Delay(TimeSpan.FromMilliseconds(300), token).ConfigureAwait(false);
            }
        }
    }
}

public sealed class ModelServerBusyException(IOException inner) : Exception("The managed model server has another owner.", inner);

using System.Runtime.Versioning;

namespace Recorder.Database;

/// <summary>
/// Where the recorder's processes find the database: the PostgreSQL server
/// copied beside the running program, and one data directory for the
/// signed-in user. The app and the capture host use the same database.
/// </summary>
public static class RecorderDatabaseLocation
{
    /// <summary>The server binaries, in pgsql\bin beside the program.</summary>
    public static string BinaryDirectory =>
        Path.Combine(AppContext.BaseDirectory, "pgsql", "bin");

    /// <summary>
    /// The data directory, in the user's local application data, which is
    /// not shared with other users of the PC.
    /// </summary>
    public static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Windows A11y Recorder",
            "Database");

    /// <summary>
    /// The options for the signed-in user's database, with the password
    /// protected for that user.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static EmbeddedPostgresOptions ForCurrentUser() =>
        new(BinaryDirectory, DataDirectory, new CurrentUserSecretProtector());
}

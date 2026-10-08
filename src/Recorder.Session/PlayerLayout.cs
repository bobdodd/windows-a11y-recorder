using System.Text.Json;
using System.Text.Json.Serialization;

namespace Recorder.Session;

/// <summary>
/// The player layout an auditor chose, kept per Windows user and restored
/// at start. See docs/architecture/player-layout.md.
/// </summary>
public sealed record PlayerLayout
{
    /// <summary>The layout used when no layout has been saved.</summary>
    public static PlayerLayout Default { get; } = new();

    /// <summary>Whether the side panel of recorder settings is shown.</summary>
    public bool SettingsOpen { get; init; } = true;

    /// <summary>Whether the details region is shown.</summary>
    public bool DetailsOpen { get; init; } = true;

    /// <summary>
    /// Whether the properties panel, beside the video, is shown.
    /// </summary>
    public bool PropertiesOpen { get; init; } = true;

    /// <summary>
    /// The width in device-independent pixels of the properties panel, or
    /// null for the player's default. Saved as <c>propertiesPanelWidth</c>:
    /// a width saved as <c>propertiesWidth</c>, before the panel's rows had
    /// change buttons, was chosen for a narrower panel, and is read as the
    /// default, so the buttons are not left out of sight.
    /// </summary>
    [JsonPropertyName("propertiesPanelWidth")]
    public double? PropertiesWidth { get; init; }

    /// <summary>
    /// The height in device-independent pixels of the region under the video
    /// while the details are shown, or null for the player's default.
    /// </summary>
    public double? LowerRegionHeight { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// The layout file of the current Windows user.
    /// </summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Windows A11y Recorder",
            "player-layout.json");

    /// <summary>
    /// Reads a layout file. A missing, empty, or unreadable file, or one
    /// that is not a layout, gives the default layout; a height that is not
    /// a positive, finite number is read as the default height.
    /// </summary>
    public static PlayerLayout Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Default;
            }

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Default;
            }

            var layout = JsonSerializer.Deserialize<PlayerLayout>(text, JsonOptions);
            if (layout is null)
            {
                return Default;
            }

            if (layout.LowerRegionHeight is { } height &&
                (!double.IsFinite(height) || height <= 0))
            {
                layout = layout with { LowerRegionHeight = null };
            }

            return layout.PropertiesWidth is { } width &&
                (!double.IsFinite(width) || width <= 0)
                ? layout with { PropertiesWidth = null }
                : layout;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
                NotSupportedException)
        {
            return Default;
        }
    }

    /// <summary>
    /// Writes the layout file, replacing it whole. Returns false, and leaves
    /// any earlier file in place, when it cannot be written; the layout is
    /// a convenience, so failing to keep it does not stop the player.
    /// </summary>
    public bool TrySave(string path)
    {
        var temporary = path + ".partial";
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (
                cleanup is IOException or UnauthorizedAccessException)
            {
            }

            return false;
        }
    }
}

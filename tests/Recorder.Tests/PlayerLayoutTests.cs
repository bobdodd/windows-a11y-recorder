using Recorder.Session;

namespace Recorder.Tests;

public sealed class PlayerLayoutTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "player-layout-tests",
        Guid.NewGuid().ToString("N"));

    private string LayoutPath => Path.Combine(_directory, "player-layout.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void DefaultShowsSettingsAndDetailsAtDefaultHeight()
    {
        var layout = PlayerLayout.Default;

        Assert.True(layout.SettingsOpen);
        Assert.True(layout.DetailsOpen);
        Assert.Null(layout.LowerRegionHeight);
    }

    [Fact]
    public void MissingFileGivesDefault()
    {
        Assert.Equal(PlayerLayout.Default, PlayerLayout.Load(LayoutPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"settingsOpen\": \"yes\"}")]
    public void EmptyOrMalformedFileGivesDefault(string text)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LayoutPath, text);

        Assert.Equal(PlayerLayout.Default, PlayerLayout.Load(LayoutPath));
    }

    [Fact]
    public void SavedLayoutIsRestored()
    {
        var layout = new PlayerLayout
        {
            SettingsOpen = false,
            DetailsOpen = false,
            LowerRegionHeight = 412.5
        };

        Assert.True(layout.TrySave(LayoutPath));

        Assert.Equal(layout, PlayerLayout.Load(LayoutPath));
        Assert.False(File.Exists(LayoutPath + ".partial"));
    }

    [Fact]
    public void SavingReplacesTheEarlierFile()
    {
        Assert.True(new PlayerLayout { DetailsOpen = false }.TrySave(LayoutPath));
        Assert.True(new PlayerLayout { SettingsOpen = false }.TrySave(LayoutPath));

        var layout = PlayerLayout.Load(LayoutPath);

        Assert.False(layout.SettingsOpen);
        Assert.True(layout.DetailsOpen);
    }

    [Fact]
    public void MissingValuesTakeTheirDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LayoutPath, "{\"detailsOpen\": false}");

        var layout = PlayerLayout.Load(LayoutPath);

        Assert.True(layout.SettingsOpen);
        Assert.False(layout.DetailsOpen);
        Assert.Null(layout.LowerRegionHeight);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-20")]
    public void HeightThatIsNotPositiveIsReadAsDefault(string height)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LayoutPath, $"{{\"detailsOpen\": false, \"lowerRegionHeight\": {height}}}");

        var layout = PlayerLayout.Load(LayoutPath);

        Assert.False(layout.DetailsOpen);
        Assert.Null(layout.LowerRegionHeight);
    }

    [Fact]
    public void UnwritableLocationReturnsFalse()
    {
        Directory.CreateDirectory(_directory);
        // A directory where the file should be cannot be replaced by a file.
        Directory.CreateDirectory(LayoutPath);

        Assert.False(PlayerLayout.Default.TrySave(LayoutPath));
        Assert.False(File.Exists(LayoutPath + ".partial"));
    }
}

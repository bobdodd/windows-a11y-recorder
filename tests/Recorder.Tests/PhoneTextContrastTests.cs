using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

// Step 4c of docs/architecture/android-checks.md: the level each measured
// line of text is given, at AA (WCAG 2.2's 1.4.3) or AAA (1.4.6), from
// the phone's screenshot or, approximately, the video; and the setting
// kept between runs. The colours are grey on white, whose ratios were
// worked out with WCAG's formula.
public sealed class PhoneTextContrastTests : IDisposable
{
    private const uint White = 0xFFFFFF;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "phone-text-contrast-" + Guid.NewGuid().ToString("N"));

    public PhoneTextContrastTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static PhoneTextLine Line(PhoneTextPlace place = PhoneTextPlace.AppTextPlacedByOcr, int height = 39) =>
        new(null, null, place, "Save", "Save", new PhoneBounds(0, 0, 100, 50), null, place == PhoneTextPlace.AppTextOnItemBounds ? 0 : height);

    private static (PhoneCheckLevel Level, string Measured)? Judge(
        uint foreground,
        PhonePictureKind kind = PhonePictureKind.Screenshot,
        PhoneContrastLevel level = PhoneContrastLevel.AA,
        PhoneTextLine? line = null,
        int joined = 2) =>
        PhoneTextContrastRun.Judge(line ?? Line(), new PhoneSwatch(White, foreground, 0, joined, joined), kind, "Windows OCR, en-US", level, 2.4375);

    [Theory]
    [InlineData(0x767676u, null)] // 4.54:1
    [InlineData(0x777777u, PhoneCheckLevel.Warning)] // 4.48:1
    [InlineData(0x949494u, PhoneCheckLevel.Warning)] // 3.03:1
    [InlineData(0x959595u, PhoneCheckLevel.Error)] // 2.995:1
    public void Judge_FromTheScreenshot_AtAA_ErrorsBelowTheLargeLimit_AndWarnsBetweenTheLimits(uint foreground, PhoneCheckLevel? expected) =>
        Assert.Equal(expected, Judge(foreground)?.Level);

    [Theory]
    [InlineData(0x595959u, null)] // 7.0:1
    [InlineData(0x5A5A5Au, PhoneCheckLevel.Warning)] // 6.9:1
    [InlineData(0x767676u, PhoneCheckLevel.Warning)] // 4.54:1
    [InlineData(0x777777u, PhoneCheckLevel.Error)] // 4.48:1
    public void Judge_AtAAA_HoldsTextToSevenAndFourAndAHalf(uint foreground, PhoneCheckLevel? expected) =>
        Assert.Equal(expected, Judge(foreground, level: PhoneContrastLevel.AAA)?.Level);

    [Fact]
    public void Judge_ComparesTheRatioUnrounded_AndShowsAThirdPlaceWhenTwoWouldReachTheLimit()
    {
        // #70768E on white is 4.4959:1: 4.50 to two places, which would
        // pass, and ATF allows 0.01 below the limit; WCAG does not.
        var judged = Judge(0x70768E);

        Assert.Equal(PhoneCheckLevel.Warning, judged?.Level);
        Assert.StartsWith("4.496:1, #70768E on #FFFFFF; fails unless the text is large", judged?.Measured, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x737373u, PhoneCheckLevel.Warning, "near the 4.5:1 limit")] // 4.74:1, could be 4.17 to 5.31
    [InlineData(0x6B6B6Bu, null, null)] // 5.33:1, could be 4.69 to 5.97
    [InlineData(0x8B8B8Bu, PhoneCheckLevel.Warning, "fails unless the text is large: 4.5:1 is needed, 3:1 for large text, and it may be below 3:1")] // 3.41:1, could be 2.998 to 3.82
    [InlineData(0x989898u, PhoneCheckLevel.Warning, "and it may be below 3:1")] // 2.88:1, could be 2.54 to 3.23
    [InlineData(0x9F9F9Fu, PhoneCheckLevel.Error, "below 3:1, the limit for large text")] // 2.65:1, could be 2.33 to 2.96
    public void Judge_FromTheVideo_ARatioThatCouldBe12PercentOtherSideOfALimit_IsAWarning(uint foreground, PhoneCheckLevel? expected, string? says)
    {
        var judged = Judge(foreground, PhonePictureKind.Frame);

        Assert.Equal(expected, judged?.Level);
        if (says is not null)
        {
            Assert.Contains(says, judged?.Measured, StringComparison.Ordinal);
            Assert.Contains("approximate, from the video", judged?.Measured, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Judge_OneColourInTheBox_IsNotRun()
    {
        var judged = Judge(White, joined: 1);

        Assert.Equal(PhoneCheckLevel.NotRun, judged?.Level);
        Assert.StartsWith("one colour, #FFFFFF, in the box, so no text was seen there", judged?.Measured, StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_SaysWhereTheColoursAndTheTextCameFrom_AndOnlyMentionsOCRWhenItWasUsed()
    {
        var placed = Judge(0x959595)!.Value.Measured;
        var found = Judge(0x959595, line: Line(PhoneTextPlace.FoundByOcr))!.Value.Measured;
        var bounds = Judge(0x959595, PhonePictureKind.Frame, line: Line(PhoneTextPlace.AppTextOnItemBounds))!.Value.Measured;

        Assert.EndsWith(
            "the words are 16 dp tall in OCR's box; the text's size and weight are not known; from the phone's screenshot; " +
            "the app's text, placed on the screen by OCR (Windows OCR, en-US)",
            placed,
            StringComparison.Ordinal);
        Assert.EndsWith("text found by OCR (Windows OCR, en-US), in no item's text", found, StringComparison.Ordinal);
        Assert.EndsWith(
            "the text's size and weight are not known; approximate, from the video; measured on the item's bounds",
            bounds,
            StringComparison.Ordinal);
        Assert.DoesNotContain("OCR", bounds, StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_WithoutTheDensity_GivesTheWordsHeightInPixels()
    {
        var judged = PhoneTextContrastRun.Judge(Line(), new PhoneSwatch(White, 0x959595, 0, 2, 2), PhonePictureKind.Screenshot, null, PhoneContrastLevel.AA, null);

        Assert.Contains("the words are 39 pixels tall in OCR's box, as no density was recorded;", judged?.Measured, StringComparison.Ordinal);
        Assert.EndsWith("placed on the screen by OCR", judged?.Measured, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_AreKept_AndAMissingOrUnreadableFileIsAA()
    {
        var path = Path.Combine(_folder, "settings", "check-settings.json");

        Assert.Equal(PhoneContrastLevel.AA, PhoneCheckSettings.Load(path).ContrastLevel);
        Assert.True(new PhoneCheckSettings { ContrastLevel = PhoneContrastLevel.AAA }.TrySave(path));
        Assert.Equal(PhoneContrastLevel.AAA, PhoneCheckSettings.Load(path).ContrastLevel);

        File.WriteAllText(path, "{\"contrastLevel\": 7}");
        Assert.Equal(PhoneContrastLevel.AA, PhoneCheckSettings.Load(path).ContrastLevel);
        File.WriteAllText(path, "not json");
        Assert.Equal(PhoneContrastLevel.AA, PhoneCheckSettings.Load(path).ContrastLevel);
    }
}

using System.Text.Json;
using Recorder.FixtureExtract;

namespace Recorder.Tests;

// docs/architecture/android-fixtures.md, "Rule tests": the extractor
// replaces the phone's serial number and any phone setting naming another
// app before a fixture is written, as the repository is public.
public sealed class AndroidFixtureFilesTests
{
    private const string Fixture = "io.github.bobdodd.a11yrecorder.fixtures";
    private static readonly IReadOnlyList<string> Packages = AndroidFixtureFiles.SettingPackages(Fixture);

    [Fact]
    public void TheSerialsNamedAreFound()
    {
        var serials = new SortedSet<string>(StringComparer.Ordinal);
        AndroidFixtureFiles.AddSerials(Json("""{"serial":"TESTPHONE01","inner":[{"serial":"TESTPHONE02"}],"other":"TESTPHONE03"}"""), serials);
        AndroidFixtureFiles.AddSerials(Json($$"""{"serial":"{{AndroidFixtureFiles.Serial}}"}"""), serials);
        Assert.Equal(["TESTPHONE01", "TESTPHONE02"], serials);
    }

    [Fact]
    public void TheSerialIsReplacedWhereverItAppears()
    {
        var replaced = new Dictionary<string, int>(StringComparer.Ordinal);
        var payload = AndroidFixtureFiles.WithoutThePhonesOwn(
            "graphics.android.screenshots",
            Json("""{"serial":"TESTPHONE01","index":3,"lines":["device TESTPHONE01 ready"],"path":"screenshots/android/0000000003.png"}"""),
            ["TESTPHONE01"],
            Packages,
            replaced);
        Assert.Equal(
            $$"""{"serial":"{{AndroidFixtureFiles.Serial}}","index":3,"lines":["device {{AndroidFixtureFiles.Serial}} ready"],"path":"screenshots/android/0000000003.png"}""",
            payload.GetRawText());
        Assert.Equal(1, replaced[AndroidFixtureFiles.SerialsReplaced]);
    }

    [Fact]
    public void ASettingNamingAnotherAppIsLeftOut()
    {
        var replaced = new Dictionary<string, int>(StringComparer.Ordinal);
        var payload = AndroidFixtureFiles.WithoutThePhonesOwn(
            "preferences.android",
            Json("""
                {"serial":"TESTPHONE01","settings":{
                "secure/tts_default_synth":"com.example.speech",
                "secure/enabled_accessibility_services":"io.github.bobdodd.a11yrecorder.companion/.RecorderAccessibilityService:com.google.android.marvin.talkback/.TalkBackService",
                "secure/accessibility_button_targets":"com.google.android.marvin.talkback/com.google.android.marvin.talkback.TalkBackService",
                "secure/accessibility_display_magnification_scale":"2.0",
                "secure/font_weight_adjustment":null}}
                """),
            ["TESTPHONE01"],
            Packages,
            replaced);
        var settings = payload.GetProperty("settings");
        Assert.Equal(AndroidFixtureFiles.LeftOut, settings.GetProperty("secure/tts_default_synth").GetString());
        Assert.StartsWith("io.github.bobdodd.a11yrecorder.companion/", settings.GetProperty("secure/enabled_accessibility_services").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("com.google.android.marvin.talkback/", settings.GetProperty("secure/accessibility_button_targets").GetString(), StringComparison.Ordinal);
        Assert.Equal("2.0", settings.GetProperty("secure/accessibility_display_magnification_scale").GetString());
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("secure/font_weight_adjustment").ValueKind);
        Assert.Equal(AndroidFixtureFiles.Serial, payload.GetProperty("serial").GetString());
        Assert.Equal(1, replaced[AndroidFixtureFiles.SettingsLeftOut]);
    }

    [Fact]
    public void OnlyThePhonesSettingsHaveAppNamesLeftOut()
    {
        // The screens' packages are kept, as the checks read them, and are
        // listed for the owner to read before committing.
        var replaced = new Dictionary<string, int>(StringComparer.Ordinal);
        var original = Json("""{"packageName":"com.example.other","text":"Opening hours"}""");
        var payload = AndroidFixtureFiles.WithoutThePhonesOwn("accessibility.android.events", original, ["TESTPHONE01"], Packages, replaced);
        Assert.Equal(original.GetRawText(), payload.GetRawText());
        Assert.Empty(replaced);
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}

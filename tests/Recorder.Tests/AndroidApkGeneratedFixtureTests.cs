using System.IO.Compression;
using System.Text.Json;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

public sealed class AndroidApkGeneratedFixtureTests
{
    private const string Package = "io.github.bobdodd.a11yrecorder.apkfixtures";
    private const string Base = "base.apk";
    private const string Flutter = "split_flutter.apk";
    private const string Compose = "split_compose.apk";
    private const string React = "split_react.apk";
    private const string Unknown = "split_unknown_bundle.apk";
    private const string Missing = "split_missing.apk";
    private const string DeclarationOnly = "fonts/declaration_only.ttf";
    private static string Input(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "AndroidApk", name);

    [Fact]
    public async Task BuiltNativeAndWebFixtureKeepsDeclarationsSeparateFromCodeAndBundledFonts()
    {
        var snapshot = await Read([Base]);
        Assert.Null(snapshot.Problem);
        Assert.Equal(1, snapshot.ApksRead);
        Assert.Equal(["assets/fonts/apk_unused.ttf", "res/font/apk_block.ttf"], snapshot.FontFiles);
        Assert.Empty(snapshot.FlutterFamilies);
        var evidence = Assert.IsType<AndroidApkAccessibilityEvidence>(snapshot.Accessibility);

        Has(evidence, "declared", "app", "Manifest configuration", Base, "supportsRtl", "true");
        Assert.Contains(evidence.Items, item => item is { Kind: "declared", Scope: "component", Category: "Typography", Split: Base, Value: "18sp" } &&
            item.Locator.Contains("style/ApkFixtureHeading", StringComparison.Ordinal) && item.Locator.Contains(", default)", StringComparison.Ordinal));
        Assert.Contains(evidence.Items, item => item is { Kind: "declared", Scope: "component", Category: "Typography", Split: Base, Value: "26sp" } &&
            item.Locator.Contains("style/ApkFixtureHeading", StringComparison.Ordinal) && item.Locator.Contains(", land; config ", StringComparison.Ordinal));
        Has(evidence, "declared", "component", "Typography", Base, "res/layout/apk_form.xml", "14dp");
        Has(evidence, "declared", "component", "Heading", Base, "/ accessibilityHeading", "true");
        Has(evidence, "declared", "component", "Live region", Base, "/ accessibilityLiveRegion", "1");
        Has(evidence, "declared", "component", "Resource reference target", Base, "resources.arsc", "Fixture email");
        Has(evidence, "declared", "component", "Resource reference target", Base, "resources.arsc", "id/apk_email");
        Has(evidence, "declared", "component", "Resource reference target", Base, "resources.arsc", "res/font/apk_block.ttf");
        Has(evidence, "declared", "component", "Autofill", Base, "res/layout/apk_form.xml", "emailAddress");
        Assert.Single(evidence.Items, item => item.Category == "Form association" && item.Locator.StartsWith("res/layout/", StringComparison.Ordinal));
        Has(evidence, "declared", "component", "Resource reference target", Base, "resources.arsc", "Hint with no linked label");
        Has(evidence, "symbol", "app", "Typography API symbol", Base, "classes.dex", "android.widget.TextView.setTextSize");
        Has(evidence, "symbol", "app", "Form API symbol", Base, "classes.dex", "android.widget.TextView.setLabelFor");
        Has(evidence, "symbol", "app", "Timing API symbol", Base, "classes.dex", "android.os.CountDownTimer.start");
        Has(evidence, "symbol", "app", "Timing API symbol", Base, "classes.dex", "android.view.accessibility.AccessibilityManager.getRecommendedTimeoutMillis");

        Has(evidence, "declared", "component", "Web component", Base, "id: web_fixture_heading)", "h1");
        Has(evidence, "declared", "component", "Form association", Base, "assets/web/form.html", "web_fixture_code");
        Has(evidence, "declared", "component", "Form label declaration", Base, "assets/web/form.html", "Fixture code");
        Has(evidence, "declared", "component", "Web accessibility attribute", Base, "id: web_fixture_code) / required", "(empty attribute)");
        Has(evidence, "declared", "component", "Web accessibility attribute", Base, "id: web_fixture_hint_only) / placeholder", "Web hint with no linked label");
        Has(evidence, "declared", "component", "Web accessibility attribute", Base, "id: web_fixture_status) / role", "status");
        Has(evidence, "declared", "component", "Web accessibility attribute", Base, "id: web_fixture_status) / aria-live", "polite");
        Has(evidence, "declared", "component", "SVG semantics", Base, "assets/web/badge.svg", "Vector fixture badge");
        Has(evidence, "declared", "component", "Timing declaration", Base, "assets/web/timing.html",
            "12 seconds (HTML refresh declaration, not an inactivity timeout)");
        Assert.DoesNotContain(evidence.Items, item => item.Value is "21sp" or "8000" or "Code-created label" or "Code-created hint" or "Unreferenced resource sentinel" ||
            item.Kind != "bundled" && item.Value.Contains("apk_unused.ttf", StringComparison.Ordinal) ||
            item.Value.Contains("sentinel", StringComparison.Ordinal));
        Assert.Contains(evidence.Coverage, row => row.Reader == "Web CSS, scripts and live DOM" && row.Status == "not decoded");
    }

    [Fact]
    public async Task MixedSplitsKeepBundledDeclaredSymbolAndUnavailableEvidenceDistinctInPlayback()
    {
        var snapshot = await Read([Base, Flutter, Compose, React, Unknown, Missing]);
        Assert.Equal((5, 6, 1), (snapshot.ApksRead, snapshot.ApksTotal, snapshot.FlutterManifestsRead));
        Assert.NotNull(snapshot.Problem);
        var evidence = Assert.IsType<AndroidApkAccessibilityEvidence>(snapshot.Accessibility);

        Has(evidence, "bundled", "app", "Framework marker", Flutter, "libflutter.so", "libflutter.so");
        Has(evidence, "bundled", "app", "Compiled library candidate", Flutter, "libapp.so", "libapp.so; contents not decoded");
        Has(evidence, "bundled", "app", "Framework marker", Compose, "androidx.compose.ui_ui.version", "AndroidX Compose package metadata");
        Has(evidence, "bundled", "app", "Framework marker", React, "libreactnative.so", "libreactnative.so");
        Has(evidence, "bundled", "app", "Font file", Flutter, "fonts/fixture_block.ttf", "assets/flutter_assets/fonts/fixture_block.ttf");
        Has(evidence, "declared", "app", "Font family", Flutter, "FontManifest.json",
            $"FixtureDeclarationOnly; asset {DeclarationOnly}; weight 600; style italic");
        Assert.DoesNotContain(snapshot.FontFiles, path => path.EndsWith(DeclarationOnly, StringComparison.Ordinal));
        Has(evidence, "symbol", "app", "Compose semantics symbol", Compose, "classes2.dex", "androidx.compose.ui.semantics.SemanticsPropertiesKt.heading");
        Has(evidence, "symbol", "app", "Compose semantics symbol", Compose, "classes2.dex", "androidx.compose.ui.semantics.SemanticsPropertiesKt.error");
        Has(evidence, "bundled", "app", "JavaScript bundle candidate", React, "index.android.bundle", "Hermes bytecode signature; code not decoded");
        Has(evidence, "bundled", "app", "JavaScript bundle candidate", Unknown, "separate.hbc", "Bundle present; payload format not established");
        Assert.All(evidence.Items, item =>
        {
            Assert.True(item.Kind is "bundled" or "declared" or "symbol");
            Assert.True(item.Scope is "app" or "component");
            Assert.DoesNotContain("sentinel", item.Value, StringComparison.Ordinal);
            Assert.False(item.Split == Missing);
        });
        foreach (var reader in new[] { "Flutter compiled code", "Jetpack Compose compiled code", "React Native / JavaScript" })
        {
            Assert.Contains(evidence.Coverage, row => row.Reader == reader && row.Status == "not decoded");
        }
        Assert.Contains(evidence.Coverage, row => row.Reader == "APK inventory" &&
            row.Status == "partial" && row.Detail == "5 of 6 installed APKs read.");

        var preflight = new AndroidTextSizeSnapshot(Package, DateTimeOffset.UnixEpoch, "unavailable", []) { ApkFonts = snapshot };
        var manifest = new SessionManifest("1.1", "synthetic-apk", "completed", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 0, 1, 0, "test", "test", "test", new(false, false, false, false, 0, false, false),
            [], [], 0, 0, null, preflight);
        var saved = JsonSerializer.Deserialize<SessionManifest>(JsonSerializer.Serialize(manifest))!;
        var details = Assert.IsType<AndroidApkDetails>(AndroidApkDetails.From(saved));
        Assert.Equal("Font file", details.Groups[0].Topic);
        Assert.Contains(details.Groups, group => group is { Topic: "Font family", Source: "Declared, app-wide" } &&
            group.Items.Any(item => item.Contains($"[{Flutter}: assets/flutter_assets/FontManifest.json]", StringComparison.Ordinal)));
        Assert.Contains(details.Groups, group => group is { Topic: "Compose semantics symbol", Source: "API symbol, app-wide" });
        Assert.Contains("A component locator is not a link to a screen visit.", details.Context);
        Assert.Contains(details.Coverage, line => line.Contains("5 of 6 installed APKs read.", StringComparison.Ordinal));
    }

    private static void Has(AndroidApkAccessibilityEvidence evidence, string kind, string scope, string category,
        string split, string locator, string value)
    {
        var candidates = evidence.Items.Where(item => item.Kind == kind && item.Scope == scope && item.Category == category &&
            item.Split == split && item.Locator.Contains(locator, StringComparison.Ordinal)).ToArray();
        Assert.True(candidates.Any(item => item.Value == value),
            $"Expected {kind} {scope} {category} at {split}: {locator} to contain '{value}'. Found: " +
            string.Join("; ", candidates.Select(item => $"{item.Locator} = {item.Value}")));
    }

    private static async Task<AndroidApkFontSnapshot> Read(string[] splits)
    {
        var adb = new FixtureAdb(splits);
        var result = await AndroidApkFonts.ReadAsync(adb, "fixture-phone", Package, TestContext.Current.CancellationToken);
        Assert.Equal(splits.Length, adb.Pulled.Count);
        Assert.All(adb.Pulled, path => Assert.False(File.Exists(path), "A transient APK copy survived preflight."));
        return result;
    }

    private sealed class FixtureAdb(string[] splits) : IAdb
    {
        public List<string> Pulled { get; } = [];
        public string Path => "fixture";
        public Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<AdbResult> RunAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return Task.FromResult(args[2] switch
            {
                "shell" when args[3] == "pm" => new AdbResult(0,
                    string.Join('\n', splits.Select(split => $"package:/data/app/synthetic/{split}")), ""),
                "shell" when args[3] == "dumpsys" => new AdbResult(0, $"Packages:\n  Package [{Package}] (1):\n" +
                    "    versionName=1.0\n    versionCode=1\n    lastUpdateTime=2026-01-01 00:00:00", ""),
                "pull" => Pull(System.IO.Path.GetFileName(args[3]), args[4]),
                _ => throw new InvalidOperationException("Unexpected fixture ADB command.")
            });
        }

        private AdbResult Pull(string split, string destination)
        {
            Pulled.Add(destination);
            if (split == Base)
            {
                File.Copy(Input("native-web.zip"), destination);
                return new(0, "", "");
            }

            using var archive = ZipFile.Open(destination, ZipArchiveMode.Create);
            switch (split)
            {
                case Flutter:
                    archive.CreateEntry("lib/arm64-v8a/libflutter.so");
                    archive.CreateEntry("lib/arm64-v8a/libapp.so");
                    archive.CreateEntryFromFile(Input("FontManifest.json"), "assets/flutter_assets/FontManifest.json");
                    using (var native = ZipFile.OpenRead(Input("native-web.zip")))
                    using (var font = native.GetEntry("res/font/apk_block.ttf")!.Open())
                    using (var target = archive.CreateEntry("assets/flutter_assets/fonts/fixture_block.ttf").Open())
                    {
                        font.CopyTo(target);
                    }
                    break;
                case Compose:
                    archive.CreateEntry("META-INF/androidx.compose.ui_ui.version");
                    archive.CreateEntryFromFile(Input("compose-symbols.dex"), "classes2.dex");
                    break;
                case React:
                    archive.CreateEntry("lib/arm64-v8a/libreactnative.so");
                    archive.CreateEntryFromFile(Input("known-props.hbc"), "assets/index.android.bundle");
                    break;
                case Unknown:
                    using (var stream = archive.CreateEntry("assets/separate.hbc").Open())
                    {
                        stream.Write("A filename is not bytecode."u8);
                    }
                    break;
                case Missing:
                    return new(1, "", "Synthetic failed pull after opening the destination.");
                default:
                    throw new InvalidOperationException("Unexpected fixture split.");
            }
            return new(0, "", "");
        }
    }
}

using System.IO.Compression;
using System.Text.Json;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Session;

namespace Recorder.Tests;

public sealed class AndroidAppAccessibilityEvidenceTests
{
    private const string Package = "org.example.synthetic";

    [Theory]
    [InlineData(null)]
    [InlineData("invalid ZIP")]
    [InlineData("new version")]
    [InlineData("cancelled")]
    public async Task ApkPullIsCleanedAndFontEvidenceRequiresStableVersion(string? issue)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var paths = new List<string>();
        var reads = 0;
        var adb = new FakeAdb(args => args[2] switch
        {
            "shell" when args[3] == "pm" => new(0, "package:/data/app/synthetic/base.apk\n", ""),
            "shell" when args[3] == "dumpsys" => new(0,
                $"Packages:\n  Package [{Package}] (1):\n    versionName=1.0\n    " +
                $"versionCode={(++reads == 2 && issue == "new version" ? 8 : 7)}\n    lastUpdateTime=2026-01-01 00:00:00", ""),
            "pull" => Pull(args[4]),
            _ => throw new InvalidOperationException("Unexpected command.")
        });
        AdbResult Pull(string path)
        {
            paths.Add(path);
            if (issue is "invalid ZIP" or "cancelled")
            {
                File.WriteAllText(path, "incomplete pull");
                if (issue == "cancelled")
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                return new(0, "", "");
            }

            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            zip.CreateEntry("res/font/synthetic.ttf");
            zip.CreateEntry("res/layout/keep-out-of-inventory.xml");
            using var writer = new StreamWriter(zip.CreateEntry("assets/flutter_assets/FontManifest.json").Open());
            writer.Write("""[{"family":"Synthetic","fonts":[{"asset":"fonts/synthetic.ttf","weight":500,"style":"italic"}]}]""");
            return new(0, "", "");
        }

        if (issue == "cancelled")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AndroidApkFonts.ReadAsync(adb, "test", Package, cancellation.Token));
        }
        else
        {
            var result = await AndroidApkFonts.ReadAsync(adb, "test", Package, cancellation.Token);
            Assert.Equal(1, result.ApksTotal);
            Assert.Equal(7L, result.VersionCode);
            if (issue is null)
            {
                Assert.Equal(1, result.ApksRead);
                Assert.Equal(1, result.FlutterManifestsRead);
                Assert.Equal(["res/font/synthetic.ttf"], result.FontFiles);
                Assert.Equal(new AndroidApkFontFamily("Synthetic", "fonts/synthetic.ttf", 500, "italic"), Assert.Single(result.FlutterFamilies));
                Assert.Null(result.Problem);
                var evidence = Assert.IsType<AndroidApkAccessibilityEvidence>(result.Accessibility);
                Assert.Contains(evidence.Items, item => item.Kind == "bundled" && item.Locator == "res/font/synthetic.ttf");
                Assert.Contains(evidence.Items, item => item.Kind == "declared" && item.Value.Contains("weight 500; style italic", StringComparison.Ordinal));
                Assert.Contains(evidence.Coverage, item => item.Reader == "Android layout and font XML" && item.Status == "partial");
            }
            else
            {
                Assert.Equal(0, result.ApksRead);
                Assert.Empty(result.FontFiles);
                Assert.Empty(result.FlutterFamilies);
                Assert.NotNull(result.Problem);
                Assert.Empty(Assert.IsType<AndroidApkAccessibilityEvidence>(result.Accessibility).Items);
            }
        }
        Assert.False(File.Exists(Assert.Single(paths)));
    }

    [Fact]
    public void ReviewIsMatchedToTheExactVersionAndOnlyAppearsInItsRecording()
    {
        var now = DateTimeOffset.UtcNow;
        var path = Directory.CreateTempSubdirectory();
        var archive = new SessionPlaybackArchive(path.FullName,
            new SessionManifest("1.1", "test", "completed", now, now, 0, 1, 0, "", "", "",
                new(false, false, false, false, 0, false, false), [], [], 0, 0, null),
            0, [], [], [], [])
        {
            PhoneDevice = new PhoneDeviceTimeline([(1, AndroidDeviceRecords.AppVersionsEventType,
                JsonSerializer.SerializeToElement(new AndroidAppVersions("test",
                    [new(Package, true, "1.0", 7, null, null, null, null, [])], [], 1, 2),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)))], [], 1)
        };
        var review = new AndroidAppAccessibilityReview(1, Package, "1.0", 7, now.AddDays(-1),
            "Synthetic source review", [new("Font declarations", "Two bundled fonts.", "Runtime font not known.")]);
        var other = path.CreateSubdirectory("other");
        try
        {
            Assert.Throws<InvalidDataException>(() => AndroidAppAccessibilityReviews.Attach(archive, review with { VersionCode = 8 }));
            Assert.Throws<InvalidDataException>(() => AndroidAppAccessibilityReviews.Attach(archive, review with { VersionName = "wrong" }));
            Assert.Null(AndroidAppAccessibilityReviews.Attached(archive, Package));
            AndroidAppAccessibilityReviews.Attach(archive, review);
            var read = Assert.IsType<AndroidAppAccessibilityReview>(AndroidAppAccessibilityReviews.Attached(archive, Package));
            Assert.Equal(review.Observations, read.Observations);
            Assert.Equal(review.ReviewedUtc, read.ReviewedUtc);
            Assert.Null(AndroidAppAccessibilityReviews.Attached(archive with { SessionDirectory = other.FullName }, Package));
            Assert.Throws<IOException>(() => AndroidAppAccessibilityReviews.Attach(archive, review));
            Assert.Single(path.EnumerateFiles());
        }
        finally
        {
            path.Delete(recursive: true);
        }
    }

    [Fact]
    public void RecordedFormEvidenceDistinguishesLabelLinksFromHintsAndDoesNotCopyValues()
    {
        var lines = PhoneFormEvidence.Describe(new PhoneCheckWindow(1, 1,
        [
            Node(1) with { Text = "Synthetic email label" },
            Node(2) with { Editable = true, Text = "entered value", Error = "private error", Hint = "Synthetic hint",
                ContentDescription = "potential value", LabeledBy = [1], FieldRequired = true, ContentInvalid = true },
            Node(3) with { Editable = true, Hint = "Another hint", ShowingHint = true, Text = "Another hint" },
            Node(4) with { Editable = true, VisibleToUser = false, Text = "hidden value" },
            Node(5) with { Editable = true, LabeledBy = [2, 8], LabeledByOutside = 1 }
        ]));

        Assert.Equal(3, lines.Count);
        Assert.Contains("label links: node 1 “Synthetic email label”", lines[0]);
        Assert.Contains("Hint: “Synthetic hint”. Showing hint: no.", lines[0]);
        Assert.Contains("Required: yes.", lines[0]);
        Assert.Contains("Error text present: yes.", lines[0]);
        Assert.Contains("label links: none published. Hint: “Another hint”. Showing hint: yes.", lines[1]);
        Assert.Contains("node 2, node 8 (not in this tree), 1 outside the captured nodes", lines[2]);
        var text = string.Join(' ', lines);
        Assert.DoesNotContain("entered value", text);
        Assert.DoesNotContain("potential value", text);
        Assert.DoesNotContain("private error", text);
        Assert.DoesNotContain("hidden value", text);
    }

    private static AndroidTreeNode Node(int index) => new()
    {
        Index = index, Bounds = new AndroidNodeBounds(0, index * 50, 90, index * 50 + 45),
        Checked = "unchecked", VisibleToUser = true
    };

    private sealed class FakeAdb(Func<IReadOnlyList<string>, AdbResult> run) : IAdb
    {
        public string Path => "adb";
        public Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<AdbResult> RunAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken cancellation) =>
            Task.FromResult(run(args));
    }
}

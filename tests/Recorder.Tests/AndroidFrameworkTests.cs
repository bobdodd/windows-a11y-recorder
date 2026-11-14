using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Collectors.Android;
using Recorder.Contracts;
using Recorder.Database.RecordingFiles;
using Recorder.Session;

namespace Recorder.Tests;

public sealed class AndroidFrameworkTests
{
    private const string Package = "com.example.app";
    private const string Base = "/data/app/~~abc=/com.example.app-xyz==/base.apk";
    private const string Split = "/data/app/~~abc=/com.example.app-xyz==/split_config.arm64_v8a.apk";

    [Fact]
    public async Task ReadsAllSplitsAndKeepsOnlyMatchedNames_WithoutPullOrExtraction()
    {
        var adb = new FakeAdb(args => args switch
        {
            [_, _, "shell", "pm", "path", Package] => Ok($"package:{Base}\r\npackage:{Split}\r\npackage:{Split}\r\n"),
            [_, _, "shell", "unzip", "-l", Base] => Ok(Listing("AndroidManifest.xml", "assets/private.txt")),
            [_, _, "shell", "unzip", "-l", Split] => Ok(Listing("lib/arm64-v8a/libflutter.so", "lib/arm64-v8a/libapp.so")),
            _ => throw new InvalidOperationException(string.Join(' ', args))
        });

        var result = await Detect(adb);

        Assert.Equal("detected", result.Status);
        Assert.Equal(["Flutter"], result.Names);
        Assert.Equal(["split_config.arm64_v8a.apk: lib/arm64-v8a/libflutter.so"], result.Evidence);
        Assert.Equal((2, 2, (string?)null), (result.ApksRead, result.ApksTotal, result.Problem));
        Assert.Equal(3, adb.Calls.Count);
        Assert.All(adb.Calls, args => Assert.Equal("shell", args[2]));
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("libreactnative.so")]
    [InlineData("libreactnativejni.so")]
    public async Task RecognisesMergedAndOlderReactNativeLibraries(string library)
    {
        var result = await Detect(OneApk(Listing($"lib/armeabi-v7a/{library}")));
        Assert.Equal(["React Native"], result.Names);
    }

    [Fact]
    public async Task KeepsMultipleFrameworksRatherThanChoosingOne()
    {
        var result = await Detect(OneApk(Listing("lib/x86_64/libreactnative.so", "lib/x86_64/libflutter.so")));
        Assert.Equal(["Flutter", "React Native"], result.Names);
        Assert.Equal(2, result.Evidence.Count);
    }

    [Theory]
    [InlineData("lib/arm64-v8a/libhermes.so")]
    [InlineData("lib/arm64-v8a/libjsi.so")]
    [InlineData("assets/flutter_assets/AssetManifest.bin")]
    [InlineData("assets/libflutter.so")]
    [InlineData("assets/notes/lib/arm64-v8a/libflutter.so")]
    [InlineData("lib/arm64-v8a/libflutter.so.old")]
    [InlineData("lib/arm64-v8a/libreactnativeblob.so")]
    [InlineData("AndroidManifest.xml")]
    public async Task DoesNotGuessNativeOrAFrameworkFromAmbiguousNames(string entry)
    {
        var result = await Detect(OneApk(Listing(entry)));
        Assert.Equal("unknown", result.Status);
        Assert.Empty(result.Names);
        Assert.Empty(result.Evidence);
        Assert.Null(result.Problem);
    }

    [Theory]
    [InlineData("/data/app/base.apk;reboot")]
    [InlineData("/data/app/$(reboot)/base.apk")]
    [InlineData("/data/app/base.apk\nmalformed output")]
    [InlineData("-d/tmp/target.apk")]
    public async Task UnsafeOrMalformedPathsAreNotPassedToTheShell(string path)
    {
        var adb = new FakeAdb(_ => Ok("package:" + path));
        Assert.Equal("unavailable", (await Detect(adb)).Status);
        Assert.Single(adb.Calls);
    }

    [Theory]
    [InlineData("bad;reboot")]
    [InlineData("com.example.app\nreboot")]
    public async Task UnsafePackageNamesAreNotQueried(string package)
    {
        var adb = new FakeAdb(_ => throw new InvalidOperationException("must not run"));
        var result = await AndroidFrameworkDetector.ReadAsync(adb, "phone", package, CancellationToken.None);
        Assert.Equal("unavailable", result.Status);
        Assert.Empty(adb.Calls);
    }

    [Fact]
    public async Task TheSinglePartAndroidSystemPackageIsSafe()
    {
        var adb = OneApk(Listing("AndroidManifest.xml"));
        var result = await AndroidFrameworkDetector.ReadAsync(adb, "phone", "android", CancellationToken.None);
        Assert.Equal("unknown", result.Status);
    }

    [Fact]
    public async Task PartialReadPreservesPositiveEvidenceAndExplainsMissingCoverage()
    {
        var adb = new FakeAdb(args => args switch
        {
            [_, _, "shell", "pm", "path", _] => Ok($"package:{Base}\npackage:{Split}\n"),
            [_, _, "shell", "unzip", "-l", Base] => Ok(Listing("lib/x86_64/libflutter.so")),
            _ => new AdbResult(1, "", "Permission denied")
        });
        var result = await Detect(adb);
        Assert.Equal("detected", result.Status);
        Assert.Equal(["Flutter"], result.Names);
        Assert.Equal((1, 2), (result.ApksRead, result.ApksTotal));
        Assert.Contains("split_config.arm64_v8a.apk", result.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialReadWithoutMarkersIsUnknownButExplicitlyIncomplete()
    {
        var adb = new FakeAdb(args => args switch
        {
            [_, _, "shell", "pm", "path", _] => Ok($"package:{Base}\npackage:{Split}\n"),
            [_, _, "shell", "unzip", "-l", Base] => Ok(Listing("AndroidManifest.xml")),
            _ => new AdbResult(1, "", "Permission denied")
        });
        var result = await Detect(adb);
        Assert.Equal("unknown", result.Status);
        Assert.Empty(result.Names);
        Assert.Equal((1, 2), (result.ApksRead, result.ApksTotal));
        Assert.NotNull(result.Problem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InternalTimeoutIsMetadataAndPreservesAlreadyReadEvidence(bool afterPositiveMatch)
    {
        // Model the linked deadline's cancellation without waiting 15 seconds.
        var adb = new FakeAdb(args => args switch
        {
            [_, _, "shell", "pm", "path", _] when afterPositiveMatch =>
                Ok($"package:{Base}\npackage:{Split}\n"),
            [_, _, "shell", "unzip", "-l", Base] when afterPositiveMatch =>
                Ok(Listing("lib/x86_64/libflutter.so")),
            _ => throw new OperationCanceledException()
        });
        var result = await Detect(adb);
        Assert.Equal(afterPositiveMatch ? "detected" : "unavailable", result.Status);
        Assert.Equal(afterPositiveMatch ? 1 : 0, result.ApksRead);
        Assert.Equal(afterPositiveMatch ? 2 : 0, result.ApksTotal);
        Assert.Contains("15-second limit", result.Problem!, StringComparison.Ordinal);
        if (afterPositiveMatch)
        {
            Assert.Equal(["Flutter"], result.Names);
            Assert.Single(result.Evidence);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("package manager unavailable")]
    public async Task MissingOrUnrecognisedPackagePathsAreUnavailable(string output)
    {
        var adb = new FakeAdb(_ => Ok(output));
        var result = await Detect(adb);
        Assert.Equal("unavailable", result.Status);
        Assert.NotNull(result.Problem);
        Assert.Single(adb.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unzip: not found")]
    [InlineData("lib/arm64-v8a/libflutter.so")]
    public async Task MalformedListingIsUnavailable_NotANegativeMatch(string output)
    {
        var result = await Detect(OneApk(output));
        Assert.Equal("unavailable", result.Status);
        Assert.Equal(0, result.ApksRead);
        Assert.NotNull(result.Problem);
    }

    [Fact]
    public async Task ACommandFailureIsRecorded_AndExternalCancellationIsHonoured()
    {
        var result = await Detect(new FakeAdb(_ => throw new IOException("offline")));
        Assert.Equal("unavailable", result.Status);
        Assert.NotNull(result.Problem);

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AndroidFrameworkDetector.ReadAsync(OneApk(""), "phone", Package, cancel.Token));
    }

    [Fact]
    public async Task ExternalCancellationDuringACommandPropagatesInsteadOfBecomingUnknown()
    {
        using var cancel = new CancellationTokenSource();
        var adb = new FakeAdb(_ =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AndroidFrameworkDetector.ReadAsync(adb, "phone", Package, cancel.Token));
        Assert.Single(adb.Calls);
    }

    [Fact]
    public async Task MetadataSurvivesTheRecordingCodecAndTimeline_AndValidates()
    {
        var framework = await Detect(OneApk(Listing("lib/arm64-v8a/libreactnative.so")));
        var record = Record(framework);
        Assert.Empty(new EventRecordValidator("framework-test").Validate(record));
        var encoded = new ArrayBufferWriter<byte>();
        RecordingEventCodec.Encode(encoded, 1, record);
        var decoded = RecordingEventCodec.Decode(encoded.WrittenSpan);
        Assert.Equal(record.Payload.GetRawText(), decoded.Event.Payload.GetRawText());
        var timeline = new PhoneDeviceTimeline(
            [(1, record.EventType, decoded.Event.Payload)], [], 1);
        Assert.Equal("React Native", timeline.VersionOf(Package)!.Framework!.Names[0]);
        var text = timeline.DescribeFramework(Package);
        Assert.Contains("Framework: React Native", text, StringComparison.Ordinal);
        Assert.Contains("use by this screen is not established", text, StringComparison.Ordinal);
        Assert.Contains("APKs read: 1 of 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OlderRecordingsStillValidateAndSayFrameworkNotRecorded()
    {
        var record = Record(null);
        var payload = JsonNode.Parse(record.Payload.GetRawText())!;
        payload["apps"]![0]!.AsObject().Remove("framework");
        record = record with { Payload = JsonSerializer.SerializeToElement(payload) };
        Assert.Empty(new EventRecordValidator("framework-test").Validate(record));
        var timeline = new PhoneDeviceTimeline([(1, record.EventType, record.Payload)], [], 1);
        Assert.Equal("Framework not recorded.", timeline.DescribeFramework(Package));
        Assert.Equal("Framework not recorded.", timeline.DescribeFramework("com.example.absent"));
    }

    [Fact]
    public void InvalidNestedMetadataIsRejected()
    {
        var payload = JsonNode.Parse(Record(null).Payload.GetRawText())!;
        payload["apps"]![0]!["framework"] = JsonNode.Parse(
            """{"source":"test","status":"native","names":[],"evidence":[],"apksRead":-1,"apksTotal":1,"problem":null}""");
        var record = Record(null) with { Payload = JsonSerializer.SerializeToElement(payload) };
        var errors = new EventRecordValidator("framework-test").Validate(record);
        Assert.True(errors.Count >= 2);
    }

    [Theory]
    [InlineData("unknown", "Framework unknown")]
    [InlineData("unavailable", "Framework unavailable")]
    public void PlaybackShowsCoverageProblemsAndUnknownWithoutInferringNative(string status, string expected)
    {
        var record = Record(new(AndroidFrameworkDetector.Source, status, [], [], 1, 2, "Could not read a split."));
        var timeline = new PhoneDeviceTimeline([(1, record.EventType, record.Payload)], [], 1);
        var text = timeline.DescribeFramework(Package);
        Assert.StartsWith(expected, text);
        Assert.Contains("APKs read: 1 of 2", text, StringComparison.Ordinal);
        Assert.Contains("Incomplete check: Could not read a split.", text, StringComparison.Ordinal);
    }

    private static RecorderEvent Record(AndroidAppFramework? framework)
    {
        var descriptor = CollectorDescriptor.Create("test", "test", "1.0", [AndroidDeviceRecords.Channel], "test");
        return RecorderEventFactory.Create("framework-test", descriptor, AndroidDeviceRecords.Channel, 0, 1,
            AndroidDeviceRecords.AppVersionsEventType,
            new AndroidAppVersions("phone",
                [new AndroidAppVersion(Package, true, "1.0", 1, 24, 36, null, null, []) { Framework = framework }],
                [], 1, 1));
    }

    private static Task<AndroidAppFramework> Detect(IAdb adb) =>
        AndroidFrameworkDetector.ReadAsync(adb, "phone", Package, CancellationToken.None);

    private static AdbResult Ok(string output) => new(0, output, "");

    private static FakeAdb OneApk(string listing) => new(args =>
        args.Contains("pm", StringComparer.Ordinal) ? Ok("package:" + Base + "\n") : Ok(listing));

    private static string Listing(params string[] names) =>
        "Archive: app.apk\r\n Length Date Time Name\r\n --------- ---------- ----- ----\r\n" +
        string.Join("\r\n", names.Select(name => "  1234  2026-11-13 10:00  " + name)) +
        "\r\n --------- -------\r\n 1234 1 file\r\n";

    private sealed class FakeAdb(Func<IReadOnlyList<string>, AdbResult> answer) : IAdb
    {
        public string Path => "adb.exe";
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation) => Task.FromResult(0);
        public Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Calls.Add(arguments);
            return Task.FromResult(answer(arguments));
        }
    }
}

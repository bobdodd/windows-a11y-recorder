using Recorder.Collectors.Android;

namespace Recorder.Tests;

// Part 1 of docs/architecture/android-implementation-plan.md: the checks
// panel, against a fake adb that answers with what the Pixel 10 Pro XL
// printed in the feasibility checks of 2026-11-08.
public sealed class AndroidDeviceChecksTests
{
    private const string AdbPath = @"C:\Users\User\AppData\Local\Windows A11y Recorder\android\platform-tools\adb.exe";

    private const string Version =
        "Android Debug Bridge version 1.0.41\nVersion 37.0.1-14710014\nInstalled as " + AdbPath + "\nRunning on Windows 10.0.19045\n";

    private const string OnePhone =
        "List of devices attached\n58191FDCQ00ABZ         device usb:1-4 product:mustang model:Pixel_10_Pro_XL device:mustang transport_id:1\n\n";

    private const string TalkBackService =
        "com.google.android.marvin.talkback/com.google.android.marvin.talkback.TalkBackService";

    private static string Details(string services = TalkBackService, string size = "Physical size: 1080x2404") =>
        $"@model\nPixel 10 Pro XL\n@release\n17\n@build\nCP3A.260905.009\n@patch\n2026-09-05\n@size\n{size}\n@services\n{services}\n@end\n";

    [Fact]
    public async Task ConnectedPhone_ShowsModelAndroidScreenAndTalkBack()
    {
        var adb = new FakeAdb().With("version", Version).With("devices -l", OnePhone).WithShell(Details());

        var report = await Checks(adb).CheckAsync(CancellationToken.None);

        Assert.Equal(PhoneState.Connected, report.Phone);
        Assert.Contains("adb: ready, version 1.0.41 (Platform-Tools 37.0.1).", report.Text);
        Assert.Contains("adb server: the recorder's own.", report.Text);
        Assert.Contains("Phone: Pixel 10 Pro XL, connected and allowed (serial 58191FDCQ00ABZ).", report.Text);
        Assert.Contains("System: Android 17, build CP3A.260905.009, security patch 2026-09-05.", report.Text);
        Assert.Contains("Screen: 1080 by 2404 pixels.", report.Text);
        Assert.Contains("TalkBack: on.", report.Text);
        Assert.Contains("Display speech output: the recorder cannot read this setting.", report.Text);
        Assert.Equal(1, adb.ServerStarts);
        Assert.Contains("-s 58191FDCQ00ABZ shell " + AndroidDeviceChecks.DetailsScript, adb.Commands);
    }

    [Fact]
    public async Task NoServicesOn_TalkBackOff()
    {
        // The phone printed "null" for the enabled services with TalkBack off.
        var adb = new FakeAdb().With("version", Version).With("devices -l", OnePhone).WithShell(Details(services: "null"));

        var report = await Checks(adb).CheckAsync(CancellationToken.None);

        Assert.Contains("TalkBack: off. Turn it on in Settings, Accessibility, TalkBack.", report.Text);
    }

    [Fact]
    public async Task AnotherServiceOnly_TalkBackOff()
    {
        var adb = new FakeAdb().With("version", Version).With("devices -l", OnePhone)
            .WithShell(Details(services: "io.github.bobdodd.a11yrecorder.companion/.RecorderAccessibilityService"));

        var report = await Checks(adb).CheckAsync(CancellationToken.None);

        Assert.Contains("TalkBack: off.", report.Text);
    }

    [Fact]
    public async Task ChangedResolution_ShowsBothSizes()
    {
        var adb = new FakeAdb().With("version", Version).With("devices -l", OnePhone)
            .WithShell(Details(size: "Physical size: 1344x2992\nOverride size: 1008x2244"));

        var report = await Checks(adb).CheckAsync(CancellationToken.None);

        Assert.Contains("Screen: 1344 by 2992 pixels, shown at 1008 by 2244.", report.Text);
    }

    [Fact]
    public async Task NoPhone_SaysNotFoundWithTheSteps()
    {
        var adb = new FakeAdb().With("version", Version)
            .With("devices -l", "* daemon not running; starting now at tcp:5037\n* daemon started successfully\nList of devices attached\n\n");

        var report = await Checks(adb).CheckAsync(CancellationToken.None);

        Assert.Equal(PhoneState.NotFound, report.Phone);
        Assert.Contains("Phone: not found.", report.Text);
        Assert.Contains("Step 1: On the phone, turn on USB debugging: Settings, System, Developer options, USB debugging.", report.Text);
        Assert.Contains("\"File transfer\"", report.Text);
        Assert.Contains("Step 4: On the phone, in \"Allow USB debugging?\"", report.Text);
        Assert.DoesNotContain(adb.Commands, command => command.Contains(" shell ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnauthorizedPhone_SaysToAllowThisPc()
    {
        var adb = new FakeAdb().With("version", Version)
            .With("devices -l", "List of devices attached\n58191FDCQ00ABZ         unauthorized usb:1-4 transport_id:2\n");

        var report = await Checks(adb).CheckAsync(CancellationToken.None);

        Assert.Equal(PhoneState.NotAllowed, report.Phone);
        Assert.Contains("Phone: found (serial 58191FDCQ00ABZ) but it has not allowed this PC yet.", report.Text);
    }

    [Fact]
    public async Task OfflinePhone_SaysToReconnect()
    {
        var adb = new FakeAdb().With("version", Version)
            .With("devices -l", "List of devices attached\n58191FDCQ00ABZ         offline usb:1-4 transport_id:3\n");

        var report = await Checks(adb).CheckAsync(CancellationToken.None);

        Assert.Equal(PhoneState.Offline, report.Phone);
        Assert.Contains("Unplug the cable and plug it in again.", report.Text);
    }

    [Fact]
    public async Task TwoDevices_SaysToConnectOnlyThePhone()
    {
        var adb = new FakeAdb().With("version", Version)
            .With("devices -l", OnePhone + "emulator-5554          device product:sdk_gphone64 model:sdk_gphone64_x86_64 device:emu64x transport_id:4\n");

        var report = await Checks(adb).CheckAsync(CancellationToken.None);

        Assert.Equal(PhoneState.MoreThanOne, report.Phone);
        Assert.Contains("Pixel 10 Pro XL, serial 58191FDCQ00ABZ", report.Text);
        Assert.Contains("Connect only the phone to record.", report.Text);
    }

    [Fact]
    public async Task NoAdb_SaysToSetItUp()
    {
        var adb = new FakeAdb();

        var report = await new AndroidDeviceChecks(adb, new FakeServer(null), _ => false).CheckAsync(CancellationToken.None);

        Assert.Equal(PhoneState.NotChecked, report.Phone);
        Assert.Equal(
            "adb: not installed. Open the Android menu and choose \"Set up Android recording...\" to download it.",
            report.Text);
        Assert.Empty(adb.Commands);
    }

    [Fact]
    public async Task OtherServerOfAnotherVersion_IsLeftAloneAndThePhoneNotChecked()
    {
        var adb = new FakeAdb().With("version", Version).With("devices -l", OnePhone);
        var other = new FakeServer(new AdbServerInfo(40, [@"C:\Android\Sdk\platform-tools\adb.exe"]));

        var report = await new AndroidDeviceChecks(adb, other, _ => true).CheckAsync(CancellationToken.None);

        Assert.Equal(PhoneState.NotChecked, report.Phone);
        Assert.Contains(
            @"adb server: another program's adb server is running (from C:\Android\Sdk\platform-tools\adb.exe), version 40. The recorder's adb is version 41",
            report.Text);
        Assert.Equal(["version"], adb.Commands);
        Assert.Equal(0, adb.ServerStarts);
    }

    [Fact]
    public async Task OtherServerOfTheSameVersion_IsUsed()
    {
        var adb = new FakeAdb().With("version", Version).With("devices -l", OnePhone).WithShell(Details());
        var other = new FakeServer(new AdbServerInfo(41, [@"C:\Tools\scrcpy-win64-v5.0.1\adb.exe"]));

        var report = await new AndroidDeviceChecks(adb, other, _ => true).CheckAsync(CancellationToken.None);

        Assert.Equal(PhoneState.Connected, report.Phone);
        Assert.Contains(@"(from C:\Tools\scrcpy-win64-v5.0.1\adb.exe). It is the same version as the recorder's, so the recorder uses it", report.Text);
        Assert.Equal(0, adb.ServerStarts);
    }

    [Fact]
    public async Task OwnServerRunning_IsNotStartedAgain()
    {
        var adb = new FakeAdb().With("version", Version).With("devices -l", OnePhone).WithShell(Details());
        var own = new FakeServer(new AdbServerInfo(41, [AdbPath]));

        var report = await new AndroidDeviceChecks(adb, own, _ => true).CheckAsync(CancellationToken.None);

        Assert.Contains("adb server: the recorder's own.", report.Text);
        Assert.Equal(0, adb.ServerStarts);
    }

    [Fact]
    public async Task VersionIsAskedOnce()
    {
        var adb = new FakeAdb().With("version", Version).With("devices -l", OnePhone).WithShell(Details());
        var checks = Checks(adb);

        await checks.CheckAsync(CancellationToken.None);
        await checks.CheckAsync(CancellationToken.None);

        Assert.Single(adb.Commands, "version");
    }

    [Fact]
    public void VersionReply_IsReadAsAdbsClientReadsIt()
    {
        Assert.Equal("000chost:version", AdbWire.Request("host:version"));
        Assert.Equal(41, AdbWire.ParseVersionReply("OKAY00040029"));
        Assert.Null(AdbWire.ParseVersionReply("FAIL0010unknown service"));
        Assert.Null(AdbWire.ParseVersionReply("HTTP/1.1 400"));
    }

    [Fact]
    public void DevicesWithoutDetails_AreStillFound()
    {
        var devices = AndroidDeviceChecks.ParseDevices("List of devices attached\r\n58191FDCQ00ABZ\tdevice\r\n");

        var device = Assert.Single(devices);
        Assert.Equal("58191FDCQ00ABZ", device.Serial);
        Assert.Equal("device", device.State);
        Assert.Empty(device.Details);
    }

    private static AndroidDeviceChecks Checks(FakeAdb adb) => new(adb, new FakeServer(null), _ => true);

    private sealed class FakeAdb : IAdb
    {
        private readonly Dictionary<string, string> _outputs = new(StringComparer.Ordinal);
        private string? _shell;

        public string Path => AdbPath;

        public List<string> Commands { get; } = [];

        public int ServerStarts { get; private set; }

        public FakeAdb With(string arguments, string output)
        {
            _outputs[arguments] = output;
            return this;
        }

        public FakeAdb WithShell(string output)
        {
            _shell = output;
            return this;
        }

        public Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellation)
        {
            var command = string.Join(' ', arguments);
            Commands.Add(command);
            if (arguments.Count > 2 && arguments[0] == "-s" && arguments[2] == "shell" && _shell is not null)
            {
                return Task.FromResult(new AdbResult(0, _shell, ""));
            }

            return Task.FromResult(_outputs.TryGetValue(command, out var output)
                ? new AdbResult(0, output, "")
                : new AdbResult(1, "", $"unexpected command: {command}"));
        }

        public Task<int> StartServerAsync(TimeSpan timeout, CancellationToken cancellation)
        {
            ServerStarts++;
            return Task.FromResult(0);
        }
    }

    private sealed class FakeServer(AdbServerInfo? info) : IAdbServerProbe
    {
        public Task<AdbServerInfo?> ReadAsync(CancellationToken cancellation) => Task.FromResult(info);
    }
}

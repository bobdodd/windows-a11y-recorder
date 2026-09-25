using Recorder.Collectors.Browser;
using Recorder.Contracts;

namespace Recorder.Tests;

public sealed class ChromiumLauncherTests
{
    [Fact]
    public async Task LaunchFailsWhenBrowserExitsDuringStartup()
    {
        var profile = Path.Combine(
            Path.GetTempPath(),
            "recorder-tests",
            Guid.NewGuid().ToString("N"));
        var connection = new BrowserEvidenceConnectionInfo(
            $"unused-{Guid.NewGuid():N}",
            "test-authentication-token",
            BrowserEvidenceProtocol.CurrentVersion,
            Guid.NewGuid().ToString("N"),
            1024);
        await using var launcher = new ChromiumLauncher(() => false);

        var exception = await Assert.ThrowsAsync<BrowserStartupExitException>(
            () => launcher.LaunchAsync(
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                profile,
                connection,
                "/c",
                remoteDebuggingPort: null,
                bridgeDiagnosticLogPath: null,
                CancellationToken.None));

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        Assert.Contains(
            "exited during startup with exit code",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAnExitTheRecorderDidNotRequest()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "recorder-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // The script outlives the startup stability window, then ends with
        // its own exit code, as a browser that closes unasked would.
        var script = Path.Combine(root, "exits-later.cmd");
        File.WriteAllText(
            script,
            "@ping -n 3 127.0.0.1 >nul\r\n@exit /b 7\r\n");
        var exited = new TaskCompletionSource<ChromiumExit>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var launcher = new ChromiumLauncher(() => false);
        launcher.Exited += exit => exited.TrySetResult(exit);

        try
        {
            var process = await launcher.LaunchAsync(
                script,
                Path.Combine(root, "profile"),
                CreateConnection(),
                startUrl: null,
                remoteDebuggingPort: null,
                bridgeDiagnosticLogPath: null,
                TestContext.Current.CancellationToken);
            var exit = await exited.Task.WaitAsync(
                TimeSpan.FromSeconds(15),
                TestContext.Current.CancellationToken);

            Assert.Equal(process.Id, exit.ProcessId);
            Assert.Equal(7, exit.ExitCode);
            Assert.False(exit.RequestedByRecorder);
            Assert.NotNull(exit.ExitedUtc);
        }
        finally
        {
            await launcher.StopAsync(CancellationToken.None);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsAnExitTheRecorderRequestedBeforeStopReturns()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "recorder-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "runs-until-stopped.cmd");
        File.WriteAllText(script, "@ping -n 60 127.0.0.1 >nul\r\n");
        ChromiumExit? reported = null;
        await using var launcher = new ChromiumLauncher(() => false);
        launcher.Exited += exit => reported = exit;

        try
        {
            await launcher.LaunchAsync(
                script,
                Path.Combine(root, "profile"),
                CreateConnection(),
                startUrl: null,
                remoteDebuggingPort: null,
                bridgeDiagnosticLogPath: null,
                TestContext.Current.CancellationToken);
            await launcher.StopAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(reported);
            Assert.True(reported.RequestedByRecorder);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FormatsExitCodesAsWindowsStatusValues()
    {
        Assert.Equal("0x00000000", ChromiumLauncher.FormatExitCode(0));
        Assert.Equal(
            "0x80000003",
            ChromiumLauncher.FormatExitCode(unchecked((int)0x80000003)));
        Assert.Equal("0x0000A11B", ChromiumLauncher.FormatExitCode(0xA11B));
    }

    private static BrowserEvidenceConnectionInfo CreateConnection() =>
        new(
            $"unused-{Guid.NewGuid():N}",
            "test-authentication-token",
            BrowserEvidenceProtocol.CurrentVersion,
            Guid.NewGuid().ToString("N"),
            1024);

    [Fact]
    public async Task LaunchRejectsElevatedRecorderProcess()
    {
        var executable = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var profile = Path.Combine(
            Path.GetTempPath(),
            "recorder-tests",
            Guid.NewGuid().ToString("N"));
        var connection = new BrowserEvidenceConnectionInfo(
            $"unused-{Guid.NewGuid():N}",
            "test-authentication-token",
            BrowserEvidenceProtocol.CurrentVersion,
            Guid.NewGuid().ToString("N"),
            1024);
        await using var launcher = new ChromiumLauncher(() => true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => launcher.LaunchAsync(
                executable,
                profile,
                connection,
                "/c",
                remoteDebuggingPort: null,
                bridgeDiagnosticLogPath: null,
                CancellationToken.None));

        Assert.Contains(
            "standard, non-administrator Windows session",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Null(launcher.Process);
    }

    [Fact]
    public void LaunchArgumentsContainNoAuthenticationMaterial()
    {
        var executable = Path.Combine(
            Path.GetTempPath(),
            "browser",
            "chrome.exe");
        var profile = Path.Combine(
            Path.GetTempPath(),
            "profiles",
            "session-1");
        var startInfo = ChromiumLauncher.CreateStartInfo(
            executable,
            profile,
            "https://example.test/");

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.Contains(
            "--a11y-recorder-bootstrap=stdin",
            startInfo.ArgumentList);
        Assert.Contains(
            "--force-renderer-accessibility",
            startInfo.ArgumentList);
        Assert.DoesNotContain(
            "--do-not-de-elevate",
            startInfo.ArgumentList);
        Assert.Contains(
            $"--user-data-dir={Path.GetFullPath(profile)}",
            startInfo.ArgumentList);
        Assert.Contains("https://example.test/", startInfo.ArgumentList);
        Assert.DoesNotContain(
            startInfo.ArgumentList,
            argument => argument.Contains(
                "token",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            startInfo.Environment,
            item => item.Key.Contains(
                "token",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExplicitDiagnosticLogPathEnablesChromiumFileLogging()
    {
        var executable = Path.Combine(
            Path.GetTempPath(),
            "browser",
            "chrome.exe");
        var profile = Path.Combine(
            Path.GetTempPath(),
            "profiles",
            "session-1");
        var logPath = Path.Combine(
            Path.GetTempPath(),
            "recorder-diagnostics",
            "chromium.log");

        var startInfo = ChromiumLauncher.CreateStartInfo(
            executable,
            profile,
            "about:blank",
            logPath);

        Assert.Contains("--enable-logging", startInfo.ArgumentList);
        Assert.Contains(
            $"--log-file={Path.GetFullPath(logPath)}",
            startInfo.ArgumentList);
        Assert.DoesNotContain(
            startInfo.ArgumentList,
            argument => argument.Contains(
                "authentication",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BridgeDiagnosticLogPathIsGivenToTheBrowser()
    {
        var executable = Path.Combine(
            Path.GetTempPath(),
            "browser",
            "chrome.exe");
        var profile = Path.Combine(
            Path.GetTempPath(),
            "profiles",
            "session-1");
        var bridgeLogPath = Path.Combine(
            Path.GetTempPath(),
            "recorder-tests",
            Guid.NewGuid().ToString("N"),
            "browser-bridge.log");

        var startInfo = ChromiumLauncher.CreateStartInfo(
            executable,
            profile,
            "about:blank",
            diagnosticLogPath: null,
            remoteDebuggingPort: null,
            bridgeDiagnosticLogPath: bridgeLogPath);

        Assert.Equal(
            Path.GetFullPath(bridgeLogPath),
            startInfo.Environment[
                ChromiumLauncher.BridgeLogFileEnvironmentVariable]);
        Assert.True(
            Directory.Exists(Path.GetDirectoryName(bridgeLogPath)),
            "The bridge cannot create its own log directory.");
    }

    [Fact]
    public void BridgeInitializationFailureIsNamedWithItsRecordedReason()
    {
        var bridgeLogPath = Path.Combine(
            Path.GetTempPath(),
            "recorder-tests",
            Guid.NewGuid().ToString("N"),
            "browser-bridge.log");
        Directory.CreateDirectory(Path.GetDirectoryName(bridgeLogPath)!);
        File.WriteAllLines(
            bridgeLogPath,
            [
                "pid=1 ticks=10 Recorder bridge bootstrap read from stdin",
                "pid=1 ticks=20 Recorder process bridge initialization " +
                    "failed: bootstrap protocol version 0.16 is not 0.17"
            ]);

        var message = ChromiumLauncher.DescribeStartupExit(
            ChromiumLauncher.BridgeInitializationFailureExitCode,
            bridgeLogPath);

        Assert.Contains(
            "could not initialize its recorder bridge",
            message,
            StringComparison.Ordinal);
        Assert.Contains(
            "bootstrap protocol version 0.16 is not 0.17",
            message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BridgeInitializationFailureWithoutAReasonSaysWhereToLook()
    {
        var bridgeLogPath = Path.Combine(
            Path.GetTempPath(),
            "recorder-tests",
            Guid.NewGuid().ToString("N"),
            "browser-bridge.log");

        var message = ChromiumLauncher.DescribeStartupExit(
            ChromiumLauncher.BridgeInitializationFailureExitCode,
            bridgeLogPath);

        Assert.Contains(
            "could not initialize its recorder bridge",
            message,
            StringComparison.Ordinal);
        Assert.Contains(bridgeLogPath, message, StringComparison.Ordinal);
    }

    [Fact]
    public void OtherStartupExitsStillReportTheirExitCode()
    {
        var message = ChromiumLauncher.DescribeStartupExit(3, null);

        Assert.Equal(
            "Instrumented Chromium exited during startup with exit code 3.",
            message);
    }

    [Fact]
    public void ExplicitRemoteDebuggingPortIsLoopbackOnly()
    {
        var executable = Path.Combine(
            Path.GetTempPath(),
            "browser",
            "chrome.exe");
        var profile = Path.Combine(
            Path.GetTempPath(),
            "profiles",
            "session-1");

        var startInfo = ChromiumLauncher.CreateStartInfo(
            executable,
            profile,
            "about:blank",
            remoteDebuggingPort: 9229);

        Assert.Contains(
            "--remote-debugging-address=127.0.0.1",
            startInfo.ArgumentList);
        Assert.Contains(
            "--remote-debugging-port=9229",
            startInfo.ArgumentList);
    }

    [Fact]
    public void ProtocolVersionQueryDoesNotOpenABrowserWindow()
    {
        var executable = Path.Combine(
            Path.GetTempPath(),
            "browser",
            "chrome.exe");
        var profile = Path.Combine(
            Path.GetTempPath(),
            "profiles",
            "protocol-query");

        var startInfo = ChromiumLauncher.CreateProtocolVersionQueryStartInfo(
            executable,
            profile);

        Assert.Contains(
            ChromiumLauncher.ProtocolVersionQuerySwitch,
            startInfo.ArgumentList);
        // A browser built before the query would treat the switch as unknown
        // and start normally, so the query must not be able to open a window.
        Assert.Contains("--no-startup-window", startInfo.ArgumentList);
        Assert.DoesNotContain(
            "--a11y-recorder-bootstrap=stdin",
            startInfo.ArgumentList);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.CreateNoWindow);
    }

    [Fact]
    public void AnAnsweredProtocolVersionQueryIsAccepted()
    {
        var query = ChromiumLauncher.InterpretProtocolVersionQuery(
            ChromiumLauncher.ProtocolVersionQueryExitCode,
            $"{ChromiumLauncher.ProtocolVersionOutputPrefix}0.17\r\n");

        Assert.Equal(
            ChromiumLauncher.BrowserProtocolVersionOutcome.Reported,
            query.Outcome);
        Assert.Equal("0.17", query.Version);
    }

    [Fact]
    public void AReportedVersionWithoutTheQueryExitCodeIsNotAccepted()
    {
        var query = ChromiumLauncher.InterpretProtocolVersionQuery(
            0,
            $"{ChromiumLauncher.ProtocolVersionOutputPrefix}0.17\r\n");

        Assert.Equal(
            ChromiumLauncher.BrowserProtocolVersionOutcome.Unknown,
            query.Outcome);
        Assert.Null(query.Version);
    }

    [Fact]
    public void ABrowserThatCannotAnswerLeavesItsVersionUnknown()
    {
        var exited = ChromiumLauncher.InterpretProtocolVersionQuery(0, "");
        var timedOut = ChromiumLauncher.InterpretProtocolVersionQuery(null, "");

        Assert.Equal(
            ChromiumLauncher.BrowserProtocolVersionOutcome.Unknown,
            exited.Outcome);
        Assert.Equal(
            ChromiumLauncher.BrowserProtocolVersionOutcome.Unknown,
            timedOut.Outcome);
        Assert.Contains(
            "before the query existed",
            timedOut.Description,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMismatchedBrowserIsRefusedBeforeItIsLaunched()
    {
        var query = ChromiumLauncher.InterpretProtocolVersionQuery(
            ChromiumLauncher.ProtocolVersionQueryExitCode,
            $"{ChromiumLauncher.ProtocolVersionOutputPrefix}0.16\r\n");

        var failure = Assert.Throws<BrowserProtocolMismatchException>(
            () => ChromiumLauncher.EnsureProtocolVersionIsCompatible(
                query,
                "0.17",
                @"C:\browser\chrome.exe"));

        Assert.Contains("0.16", failure.Message, StringComparison.Ordinal);
        Assert.Contains("0.17", failure.Message, StringComparison.Ordinal);
        Assert.Contains(
            @"C:\browser\chrome.exe",
            failure.Message,
            StringComparison.Ordinal);
        Assert.IsAssignableFrom<InvalidOperationException>(failure);
    }

    [Fact]
    public void AMatchingOrUnknownVersionIsNotRefused()
    {
        var matching = ChromiumLauncher.InterpretProtocolVersionQuery(
            ChromiumLauncher.ProtocolVersionQueryExitCode,
            $"{ChromiumLauncher.ProtocolVersionOutputPrefix}0.17\r\n");
        var unknown = ChromiumLauncher.InterpretProtocolVersionQuery(0, "");

        ChromiumLauncher.EnsureProtocolVersionIsCompatible(
            matching,
            "0.17",
            "chrome.exe");
        // An unknown version is left to the bridge, which rejects a mismatched
        // bootstrap and names both versions. Refusing here would stop sessions
        // against any browser built before the query existed.
        ChromiumLauncher.EnsureProtocolVersionIsCompatible(
            unknown,
            "0.17",
            "chrome.exe");
    }
}

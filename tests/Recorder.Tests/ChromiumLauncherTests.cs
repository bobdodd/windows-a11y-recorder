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
        await using var launcher = new ChromiumLauncher();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => launcher.LaunchAsync(
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                profile,
                connection,
                "/c",
                CancellationToken.None));

        Assert.Contains(
            "exited during startup with exit code",
            exception.Message,
            StringComparison.Ordinal);
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
}

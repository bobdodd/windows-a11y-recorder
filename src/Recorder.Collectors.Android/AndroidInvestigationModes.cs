using System.Globalization;
using System.Text.RegularExpressions;

namespace Recorder.Collectors.Android;

internal enum AndroidInvestigationInput { Explore, Tab, Tap }

internal interface IAndroidInvestigationModes
{
    Task<string?> SetTalkBackAsync(bool on, CancellationToken cancellation);
    Task<string?> TabAsync(CancellationToken cancellation);
    Task<string?> RestoreAsync();
}

/// <summary>Temporarily changes one known service without changing the other services or the global switch.</summary>
internal sealed partial class AndroidInvestigationModes(IAdb adb, string serial) : IAndroidInvestigationModes
{
    private const string Setting = "enabled_accessibility_services";
    private static readonly TimeSpan CommandLimit = TimeSpan.FromSeconds(4);
    private string? _original;
    private string? _expected;
    private string? _pending;
    private string? _talkBack;
    private string? _user;

    public async Task<string?> SetTalkBackAsync(bool on, CancellationToken cancellation)
    {
        if (_user is null)
        {
            var activeUser = await ShellAsync("am get-current-user", cancellation).ConfigureAwait(false);
            if (activeUser.ExitCode != 0 || !int.TryParse(activeUser.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                return "The active Android user could not be verified. No accessibility settings were changed.";
            }
            _user = id.ToString(CultureInfo.InvariantCulture);
        }
        var current = await ReadAsync(cancellation).ConfigureAwait(false);
        if (current is null || !CompanionApp.IsOn(current))
        {
            return "The enabled accessibility services could not be read with the recorder companion enabled.";
        }
        _expected ??= _original = current;
        if (current != _expected || _pending is not null)
        {
            return "The accessibility service setting changed outside this investigation.";
        }
        var parts = current.Split(':');
        var existing = parts.Where(IsTalkBack).ToArray();
        if (existing.Length > 1)
        {
            return "More than one TalkBack service was enabled; none were changed.";
        }
        if (on == (existing.Length == 1))
        {
            return null;
        }
        _talkBack ??= existing.SingleOrDefault();
        if (on && _talkBack is null)
        {
            var result = await ShellAsync($"cmd package query-services --user {_user} --brief --components -a " +
                "android.accessibilityservice.AccessibilityService -p " + AndroidDeviceChecks.TalkBackPackage, cancellation)
                .ConfigureAwait(false);
            var found = result.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(value => IsTalkBack(value) && Component().IsMatch(value)).Distinct(StringComparer.Ordinal).Take(2).ToArray();
            if (result.ExitCode != 0 || found.Length != 1)
            {
                return "One installed Google TalkBack service could not be identified. No screen-reader substitute was used.";
            }
            _talkBack = found[0];
        }
        var value = string.Join(':', on ? parts.Append(_talkBack!) : parts.Where(part => !IsTalkBack(part)));
        _pending = value; // A cancelled host command might already have written on the phone.
        if (!await ReplaceAsync(current, value, cancellation).ConfigureAwait(false))
        {
            return "The phone did not verify the requested TalkBack mode.";
        }
        _expected = value;
        _pending = null;
        return null;
    }

    public async Task<string?> TabAsync(CancellationToken cancellation)
    {
        if (_user is null)
        {
            return "The active Android user was not verified for Tab.";
        }
        var result = await ShellAsync($"[ \"$(am get-current-user)\" = '{_user}' ] && input -d 0 keyevent KEYCODE_TAB",
            cancellation).ConfigureAwait(false);
        return result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.Text) ? null : "Android did not confirm the Tab key command.";
    }

    public async Task<string?> RestoreAsync()
    {
        if (_original is null)
        {
            return null;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var current = await ReadAsync(timeout.Token).ConfigureAwait(false);
            if (current == _original)
            {
                return null;
            }
            if (current is null || (current != _expected && current != _pending))
            {
                return "The original TalkBack setting could not be restored without overwriting a separate change. Check TalkBack on the phone.";
            }
            return await ReplaceAsync(current, _original, timeout.Token).ConfigureAwait(false) ? null :
                "The phone did not verify restoration of the original TalkBack setting. Check TalkBack on the phone.";
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            return "The original TalkBack setting could not be verified after the run. Check TalkBack on the phone.";
        }
    }

    private async Task<string?> ReadAsync(CancellationToken cancellation)
    {
        var result = await ShellAsync($"settings --user {_user} get secure {Setting}", cancellation).ConfigureAwait(false);
        var value = result.Output.Trim();
        return result.ExitCode == 0 && value.Split(':').All(part => Component().IsMatch(part)) ? value : null;
    }

    private async Task<bool> ReplaceAsync(string from, string to, CancellationToken cancellation)
    {
        var result = await ShellAsync($"[ \"$(settings --user {_user} get secure {Setting})\" = '{from}' ] && " +
            $"settings --user {_user} put secure {Setting} '{to}'", cancellation).ConfigureAwait(false);
        return result.ExitCode == 0 && await ReadAsync(cancellation).ConfigureAwait(false) == to;
    }

    private Task<AdbResult> ShellAsync(string command, CancellationToken cancellation) =>
        adb.RunAsync(["-s", serial, "shell", command], CommandLimit, cancellation);

    private static bool IsTalkBack(string value) =>
        value == AndroidDeviceChecks.TalkBackPackage + "/.TalkBackService" ||
        value == AndroidDeviceChecks.TalkBackPackage + "/" + AndroidDeviceChecks.TalkBackPackage + ".TalkBackService";

    [GeneratedRegex(@"\A[A-Za-z0-9._]+/[A-Za-z0-9._$]+\z")]
    private static partial Regex Component();
}

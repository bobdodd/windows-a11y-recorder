using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Recorder.Collectors.Browser;

internal static class BrowserEvidencePipeFactory
{
    private const string ChromiumLockdownSid = "S-1-0-0";
    private const string UntrustedIntegritySid = "S-1-16-0";

    internal static NamedPipeServerStream Create(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            CreateSecurity(),
            HandleInheritability.None);
    }

    internal static PipeSecurity CreateSecurity()
    {
        var sessionSid = GetCurrentSessionSid();
        var descriptor =
            $"D:P(A;;GRGW;;;{sessionSid.Value})" +
            $"(A;;GRGW;;;{ChromiumLockdownSid})" +
            $"S:(ML;;NW;;;{UntrustedIntegritySid})";
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm(descriptor);
        return security;
    }

    internal static SecurityIdentifier GetCurrentSessionSid()
    {
        using var identity = WindowsIdentity.GetCurrent(
            TokenAccessLevels.Query);
        var logonSid = identity.Groups?
            .OfType<SecurityIdentifier>()
            .SingleOrDefault(sid => sid.IsWellKnown(
                WellKnownSidType.LogonIdsSid));
        return logonSid ?? identity.User ??
            throw new InvalidOperationException(
                "The current Windows access token has no session identity.");
    }
}

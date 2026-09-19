using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Recorder.Collectors.Browser;

internal static class BrowserEvidencePipeFactory
{
    private const uint PipeAccessDuplex = 0x00000003;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint PipeUnlimitedInstances = 255;
    private const uint SddlRevision1 = 1;
    private const string ChromiumLockdownSid = "S-1-0-0";
    private const string UntrustedIntegritySid = "S-1-16-0";

    internal static NamedPipeServerStream Create(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        var descriptor = IntPtr.Zero;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
                CreateSecurityDescriptorSddl(),
                SddlRevision1,
                out descriptor,
                out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = false
            };
            var handle = CreateNamedPipe(
                $@"\\.\pipe\{pipeName}",
                PipeAccessDuplex | FileFlagOverlapped,
                0,
                PipeUnlimitedInstances,
                0,
                0,
                0,
                ref attributes);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error);
            }

            try
            {
                return new NamedPipeServerStream(
                    PipeDirection.InOut,
                    true,
                    false,
                    handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
        finally
        {
            _ = LocalFree(descriptor);
        }
    }

    internal static string CreateSecurityDescriptorSddl()
    {
        var sessionSid = GetCurrentSessionSid();
        return
            $"D:P(A;;GRGW;;;{sessionSid.Value})" +
            $"(A;;GRGW;;;{ChromiumLockdownSid})" +
            $"S:(ML;;;;;{UntrustedIntegritySid})";
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

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        internal bool InheritHandle;
    }

    [DllImport(
        "advapi32.dll",
        EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool
        ConvertStringSecurityDescriptorToSecurityDescriptor(
            string stringSecurityDescriptor,
            uint stringSdRevision,
            out IntPtr securityDescriptor,
            out uint securityDescriptorSize);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateNamedPipeW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(
        string name,
        uint openMode,
        uint pipeMode,
        uint maxInstances,
        uint outBufferSize,
        uint inBufferSize,
        uint defaultTimeout,
        ref SecurityAttributes securityAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}

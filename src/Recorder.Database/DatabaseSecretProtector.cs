using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Recorder.Database;

/// <summary>
/// Protects the database password at rest.
/// </summary>
public interface IDatabaseSecretProtector
{
    byte[] Protect(byte[] secret);
    byte[] Unprotect(byte[] protectedSecret);
}

/// <summary>
/// Protects the database password for the signed-in Windows user with the
/// Windows data protection API, so another user on the same PC cannot read it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CurrentUserSecretProtector : IDatabaseSecretProtector
{
    private static readonly byte[] Entropy =
        "windows-a11y-recorder/database-password"u8.ToArray();

    public byte[] Protect(byte[] secret) =>
        ProtectedData.Protect(secret, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedSecret) =>
        ProtectedData.Unprotect(protectedSecret, Entropy, DataProtectionScope.CurrentUser);
}

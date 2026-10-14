using System.Net;
using System.Net.Sockets;

namespace Recorder.Analysis;

/// <summary>
/// Which model endpoints the worker may send recordings to: this PC, or a
/// machine on the local network. An address outside the local network is
/// refused, because sending recordings to an outside service is a separate
/// decision, not a setting. A host name is checked each time it is
/// resolved, since what it names can change.
/// </summary>
public static class EndpointPolicy
{
    /// <summary>
    /// Why an endpoint is refused before resolving it, or null. A host name
    /// other than localhost passes here and is checked when resolved.
    /// </summary>
    public static string? Refusal(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (string.Equals(endpoint.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return IPAddress.TryParse(endpoint.IdnHost.Trim('[', ']'), out var address) && !IsLocal(address)
            ? RefusalText(endpoint, address)
            : null;
    }

    /// <summary>
    /// Why an endpoint is refused once its host is resolved, or null. Every
    /// address the host resolves to must be on this PC or the local network.
    /// </summary>
    public static async Task<string?> RefusalAfterResolvingAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (Refusal(endpoint) is { } refusal)
        {
            return refusal;
        }

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(endpoint.IdnHost.Trim('[', ']'), out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(endpoint.IdnHost, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException exception)
        {
            return $"The endpoint's host {endpoint.IdnHost} could not be found: {exception.Message}";
        }

        if (addresses.Length == 0)
        {
            return $"The endpoint's host {endpoint.IdnHost} has no address.";
        }

        var outside = addresses.FirstOrDefault(address => !IsLocal(address));
        return outside is null ? null : RefusalText(endpoint, outside);
    }

    /// <summary>
    /// Whether an address is this PC's or the local network's: loopback,
    /// the IPv4 private ranges 10.0.0.0/8, 172.16.0.0/12 and
    /// 192.168.0.0/16, IPv4 link-local 169.254.0.0/16, IPv6 unique local
    /// fc00::/7, and IPv6 link-local fe80::/10.
    /// </summary>
    public static bool IsLocal(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) ||
                (bytes[0] == 169 && bytes[1] == 254);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 &&
            ((bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80));
    }

    private static string RefusalText(Uri endpoint, IPAddress address) =>
        $"The endpoint {endpoint} is at {address}, which is outside this PC and the local network. " +
        "Recordings are not sent outside the local network.";
}

using System.Net;
using System.Net.Sockets;

namespace Recorder.Analysis;

/// <summary>
/// Which model endpoints the worker may send recordings to: this PC, or a
/// machine on the local network. An address outside the local network is
/// refused, because sending recordings to an outside service is a separate
/// decision, not a setting. A host name is checked each time it is
/// resolved, since what it names can change.
///
/// The one outside service is Anthropic's API, for a profile whose provider
/// is Anthropic, at exactly <see cref="AnthropicEndpoint"/> over HTTPS: the
/// owner's decision of 2026-11-11, in code rather than a setting (see
/// docs/architecture/cloud-models.md, "2. One outside address, only for
/// that provider"). That address on a profile of another provider is
/// refused as any outside address is.
/// </summary>
public static class EndpointPolicy
{
    /// <summary>The only endpoint of a profile that sends to Anthropic.</summary>
    public static readonly Uri AnthropicEndpoint = new("https://api.anthropic.com");

    /// <summary>
    /// Why an endpoint is refused before resolving it, or null, for an
    /// OpenAI-compatible profile.
    /// </summary>
    public static string? Refusal(Uri endpoint) => Refusal(endpoint, ModelProviders.OpenAiCompatible);

    /// <summary>
    /// Why an endpoint is refused before resolving it, or null. A host name
    /// other than localhost passes here and is checked when resolved; a
    /// profile that sends to Anthropic must have exactly
    /// <see cref="AnthropicEndpoint"/>.
    /// </summary>
    public static string? Refusal(Uri endpoint, string provider)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (provider == ModelProviders.Anthropic)
        {
            return IsAnthropicEndpoint(endpoint)
                ? null
                : $"A profile that sends to Anthropic must have the endpoint {AnthropicEndpoint}, not {endpoint}.";
        }

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
    public static Task<string?> RefusalAfterResolvingAsync(Uri endpoint, CancellationToken cancellationToken) =>
        RefusalAfterResolvingAsync(endpoint, ModelProviders.OpenAiCompatible, cancellationToken);

    /// <summary>
    /// Why an endpoint is refused once its host is resolved, or null. For a
    /// profile that sends to Anthropic the address is the fixed one, so
    /// what its host resolves to is not checked; for any other, every
    /// address the host resolves to must be on this PC or the local network.
    /// </summary>
    public static async Task<string?> RefusalAfterResolvingAsync(Uri endpoint, string provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (Refusal(endpoint, provider) is { } refusal)
        {
            return refusal;
        }

        if (provider == ModelProviders.Anthropic)
        {
            return null;
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

    // Exactly https://api.anthropic.com: HTTPS on port 443, that host, no
    // user name, path, query or fragment.
    private static bool IsAnthropicEndpoint(Uri endpoint) =>
        endpoint.IsAbsoluteUri &&
        endpoint.Scheme == Uri.UriSchemeHttps &&
        string.Equals(endpoint.IdnHost, AnthropicEndpoint.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        endpoint.Port == 443 &&
        endpoint.UserInfo.Length == 0 &&
        endpoint.AbsolutePath == "/" &&
        endpoint.Query.Length == 0 &&
        endpoint.Fragment.Length == 0;

    private static string RefusalText(Uri endpoint, IPAddress address) =>
        $"The endpoint {endpoint} is at {address}, which is outside this PC and the local network. " +
        "Recordings are not sent outside the local network.";
}

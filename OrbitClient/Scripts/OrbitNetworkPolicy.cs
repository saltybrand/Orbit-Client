using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace OrbitClient.Scripts;

/// <summary>Limits remote HTTP clients to public HTTPS endpoints.</summary>
public static class OrbitNetworkPolicy
{
    /// <summary>Rejects local names, credentials and non-public literal addresses.</summary>
    public static Uri ValidateHttpsUri(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new IOException("A public HTTPS URL without credentials is required.");
        ValidateHost(uri.DnsSafeHost);
        return uri;
    }

    private static void ValidateHost(string host)
    {
        host = host.Trim('[', ']').TrimEnd('.');
        if (IPAddress.TryParse(host, out var address))
        {
            if (!IsPublicAddress(address)) throw new IOException("Local and private network addresses are not allowed.");
            return;
        }
        if (!host.Contains('.') || new[] { ".localhost", ".local", ".internal", ".home", ".lan" }
            .Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("A public internet hostname is required.");
    }

    /// <summary>Excludes private, loopback, link-local and reserved destinations.</summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224
                || b[0] == 100 && b[1] is >= 64 and <= 127
                || b[0] == 169 && b[1] == 254
                || b[0] == 168 && b[1] == 63 && b[2] == 129 && b[3] == 16
                || b[0] == 172 && b[1] is >= 16 and <= 31
                || b[0] == 192 && (b[1] == 168 || b[1] == 0 && b[2] is 0 or 2 || b[1] == 88 && b[2] == 99)
                || b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100)
                || b[0] == 203 && b[1] == 0 && b[2] == 113);
        // Global IPv6 unicast only; exclude documentation and address-transition tunnels.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId == 0
            && (b[0] & 0xe0) == 0x20
            && !(b[0] == 0x20 && b[1] == 0x01 && (b[2] <= 1 || b[2] == 0x0d && b[3] == 0xb8))
            && !(b[0] == 0x20 && b[1] == 0x02);
    }

    /// <summary>Validates DNS results and connects directly to the checked IP to prevent DNS rebinding.</summary>
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        // A proxy could resolve the hostname again and bypass the checked destination.
        UseProxy = false,
        ConnectCallback = async (context, cancellationToken) =>
        {
            ValidateHttpsUri(context.InitialRequestMessage.RequestUri!.AbsoluteUri);
            ValidateHost(context.DnsEndPoint.Host);
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
                throw new IOException("The remote hostname resolves to a private or reserved address.");
            SocketException? lastError = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException error) { socket.Dispose(); lastError = error; }
                catch { socket.Dispose(); throw; }
            }
            throw new IOException("The remote endpoint could not be reached.", lastError);
        }
    };
}

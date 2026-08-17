using System.Net;

namespace QuickShare.Infrastructure;

/// <summary>
/// SSRF guard for server-side URL fetches (issue #57): classifies an IP address as
/// blocked when it points at the local machine, a private/loopback/link-local/CGNAT/
/// benchmark range, or a multicast/reserved block. Public internet addresses pass.
/// </summary>
public static class IpAddressSafety
{
    public static bool IsBlocked(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();

        if (bytes.Length == 4)
        {
            return bytes[0] == 0                                                  // 0.0.0.0/8
                || bytes[0] == 10                                                 // 10.0.0.0/8
                || bytes[0] == 127                                                // 127.0.0.0/8
                || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)         // 100.64.0.0/10 CGNAT
                || (bytes[0] == 169 && bytes[1] == 254)                           // 169.254.0.0/16 link-local
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)          // 172.16.0.0/12
                || (bytes[0] == 192 && bytes[1] == 168)                           // 192.168.0.0/16
                || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)            // 192.0.0.0/24
                || (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))        // 198.18.0.0/15 benchmark
                || bytes[0] >= 224;                                               // multicast + reserved
        }

        if (bytes.Length == 16)
        {
            var allZero = true;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0)
                {
                    allZero = false;
                    break;
                }
            }
            if (allZero) return true;              // :: unspecified
            if ((bytes[0] & 0xfe) == 0xfc) return true; // fc00::/7 ULA
            if (bytes[0] == 0xff) return true;     // multicast
        }

        return false;
    }
}

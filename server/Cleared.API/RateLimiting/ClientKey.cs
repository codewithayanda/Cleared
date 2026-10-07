using System.Net;
using System.Net.Sockets;

namespace Cleared.API.RateLimiting;

// Who a request counts against. One person usually holds a whole IPv6 /64, so counting each
// address on its own would hand them a fresh allowance every time they switch address.
public static class ClientKey
{
    public static string From(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        return $"{Convert.ToHexString(address.GetAddressBytes().AsSpan(0, 8))}::/64";
    }
}

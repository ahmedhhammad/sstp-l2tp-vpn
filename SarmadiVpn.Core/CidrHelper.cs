using System.Net;
using System.Net.Sockets;

namespace SarmadiVpn.Core;

public sealed record CidrNetwork(string Address, int PrefixLength, string Netmask)
{
    public string Cidr => $"{Address}/{PrefixLength}";
}

public static class CidrHelper
{
    public static CidrNetwork Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new FormatException("Enter an IPv4 address or CIDR, such as 192.168.1.0/24.");
        var parts = input.Trim().Split('/', StringSplitOptions.TrimEntries);
        var octets = parts[0].Split('.');
        if (parts.Length > 2 || octets.Length != 4 || octets.Any(x => !byte.TryParse(x, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            throw new FormatException("Enter a valid IPv4 address, optionally followed by /0 through /32.");
        var prefix = 32;
        if (parts.Length == 2 && (!int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out prefix) || prefix is < 0 or > 32))
            throw new FormatException("The prefix length must be between 0 and 32.");

        var value = ToUInt(ip);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var network = value & mask;
        var netmask = new IPAddress(BitConverter.GetBytes(mask).Reverse().ToArray()).ToString();
        return new CidrNetwork(new IPAddress(BitConverter.GetBytes(network).Reverse().ToArray()).ToString(), prefix, netmask);
    }

    private static uint ToUInt(IPAddress ip) => BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray());
}

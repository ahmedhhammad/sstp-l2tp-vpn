using SarmadiVpn.Core;

namespace SarmadiVpn.Tests;

public sealed class CidrHelperTests
{
    [Theory]
    [InlineData("10.22.33.44/0", "0.0.0.0", "0.0.0.0")]
    [InlineData("10.22.33.44/8", "10.0.0.0", "255.0.0.0")]
    [InlineData("192.168.5.77/16", "192.168.0.0", "255.255.0.0")]
    [InlineData("192.168.1.77/24", "192.168.1.0", "255.255.255.0")]
    [InlineData("192.168.1.77/30", "192.168.1.76", "255.255.255.252")]
    [InlineData("192.168.1.77/32", "192.168.1.77", "255.255.255.255")]
    public void ParsesAndNormalizes(string input, string address, string netmask)
    {
        var result = CidrHelper.Parse(input);
        Assert.Equal(address, result.Address);
        Assert.Equal(netmask, result.Netmask);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("300.1.1.1")]
    [InlineData("192.168.1.1/33")]
    [InlineData("192.168.1.1/-1")]
    [InlineData("2001:db8::1/32")]
    [InlineData("127.1")]
    [InlineData("192.168.1.1/+24")]
    [InlineData("192.168.1.1/24/3")]
    public void RejectsInvalid(string input) => Assert.Throws<FormatException>(() => CidrHelper.Parse(input));
}

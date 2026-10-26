using MyVpnApp.Core;

namespace MyVpnApp.Tests;

public sealed class GatewayResolverTests
{
    [Fact]
    public void ParsesGetNetRouteJsonAndOnLinkFallback()
    {
        var result = GatewayResolver.ParsePowerShellJson("{\"InterfaceIndex\":42,\"Gateway\":\"10.99.242.1\",\"Alias\":\"MyVpnApp\"}");
        Assert.Equal(42, result!.InterfaceIndex);
        Assert.Equal("10.99.242.1", result.Gateway);
        var fallback = GatewayResolver.ParsePowerShellJson("{\"InterfaceIndex\":42,\"Gateway\":null}");
        Assert.Equal("0.0.0.0", fallback!.RouteGateway);
    }

    [Fact]
    public void ParsesFormattedGetNetRouteOutput()
    {
        const string sample = "ifIndex DestinationPrefix NextHop RouteMetric ifMetric PolicyStore\r\n42 10.99.242.1/32 10.99.242.1 0 1 ActiveStore\r\n42 0.0.0.0/0 0.0.0.0 0 1 ActiveStore";
        var result = GatewayResolver.ParseGetNetRoute(sample, 42);
        Assert.Equal(new GatewayInfo(42, "10.99.242.1", "Get-NetRoute table"), result);
    }

    [Fact]
    public void ParsesIpconfigPppFallback()
    {
        const string sample = "PPP adapter MyVpnApp:\r\n   Connection-specific DNS Suffix  . :\r\n   IPv4 Address. . . . . . . . . . . : 10.99.242.10\r\n   Subnet Mask . . . . . . . . . . . : 255.255.255.255\r\n   Default Gateway . . . . . . . . . : 10.99.242.1";
        var result = GatewayResolver.ParseIpconfig(sample, "MyVpnApp", 42);
        Assert.Equal(42, result!.InterfaceIndex);
        Assert.Equal("10.99.242.1", result.Gateway);
    }
}

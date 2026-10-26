using MyVpnApp.Core;
using System.Text;

namespace MyVpnApp.Tests;

public sealed class GatewayResolverTests
{
    [Fact]
    public void ParsesGetNetRouteJsonAndOnLinkFallback()
    {
        var result = GatewayResolver.ParsePowerShellJson("{\"InterfaceIndex\":42,\"Gateway\":\"10.99.242.0\",\"Alias\":\"MyVpnApp\"}");
        Assert.Equal(42, result!.InterfaceIndex);
        Assert.Equal("10.99.242.0", result.Gateway);
        var fallback = GatewayResolver.ParsePowerShellJson("{\"InterfaceIndex\":42,\"Gateway\":null}");
        Assert.Equal("0.0.0.0", fallback!.RouteGateway);
    }

    [Fact]
    public void ParsesFormattedGetNetRouteOutput()
    {
        const string sample = "ifIndex DestinationPrefix NextHop RouteMetric ifMetric PolicyStore\r\n42 10.99.242.0/32 10.99.242.0 0 1 ActiveStore\r\n42 0.0.0.0/0 0.0.0.0 0 1 ActiveStore";
        var result = GatewayResolver.ParseGetNetRoute(sample, 42);
        Assert.Equal(new GatewayInfo(42, "10.99.242.0", "Get-NetRoute table"), result);
    }

    [Fact]
    public void ParsesIpconfigPppFallback()
    {
        const string sample = "PPP adapter MyVpnApp:\r\n   Connection-specific DNS Suffix  . :\r\n   IPv4 Address. . . . . . . . . . . : 10.99.242.10\r\n   Subnet Mask . . . . . . . . . . . : 255.255.255.255\r\n   Default Gateway . . . . . . . . . : 10.99.242.0\r\n\r\nPPP adapter OtherVpn:\r\n   IPv4 Address. . . . . . . . . . . : 172.20.0.10\r\n   Default Gateway . . . . . . . . . : 172.20.0.1";
        var result = GatewayResolver.ParseIpconfig(sample, "MyVpnApp", 42);
        Assert.Equal(42, result!.InterfaceIndex);
        Assert.Equal("10.99.242.0", result.Gateway);
    }

    [Fact]
    public void ParsesPppAdapterWithoutPeerForOnLinkFallback()
    {
        const string sample = "PPP adapter MyVpnApp:\r\n   IPv4 Address. . . . . . . . . . . : 10.99.242.10\r\n   Default Gateway . . . . . . . . . :";
        var result = GatewayResolver.ParseIpconfig(sample, "MyVpnApp", 42);
        Assert.Equal("0.0.0.0", result!.RouteGateway);
    }

    [Fact]
    public async Task ResolverMapsPppIpconfigAddressToInterfaceAndUsesReportedPeer()
    {
        var runner = new FakeRunner(_ => new ProcessResult(0,
            "{\"InterfaceIndex\":42,\"Gateway\":\"10.99.242.0\",\"LocalAddress\":\"10.99.242.10\",\"Alias\":\"MyVpnApp\"}", ""));
        var resolver = new GatewayResolver(runner);

        var result = await resolver.ResolveAsync("MyVpnApp");

        Assert.Equal(new GatewayInfo(42, "10.99.242.0", "ipconfig/Get-NetIPAddress/Get-NetRoute"), result);
        var command = Assert.Single(runner.Commands);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(command.Arguments[^1]));
        Assert.Contains("ipconfig.exe /all", script);
        Assert.Contains("Get-NetIPAddress -AddressFamily IPv4 -IPAddress $ip", script);
        Assert.Contains("Get-NetRoute -InterfaceIndex $idx", script);
    }

    private sealed class FakeRunner(Func<ProcessSpec, ProcessResult> respond) : IProcessRunner
    {
        public List<ProcessSpec> Commands { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Commands.Add(spec);
            return Task.FromResult(respond(spec));
        }
    }
}

using System.Text;
using SarmadiVpn.Core;

namespace SarmadiVpn.Tests;

public sealed class VpnServiceTests
{
    [Fact]
    public async Task ProfileUsesBooleanSplitTunnelingAndRedactsCredentials()
    {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var service = new VpnService(runner);
        var settings = new VpnSettings("vpn.example", "alice", VpnProtocol.L2tp, false, null);

        var result = await service.ConnectAsync(settings, "user-password", "psk-secret");

        Assert.True(result.Succeeded);
        var setup = Assert.Single(runner.Commands, x => x.FileName == "powershell.exe");
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(setup.Arguments[^1]));
        Assert.Contains("-SplitTunneling $true", script);
        Assert.Contains("-SplitTunneling:$true", script);
        Assert.DoesNotContain("psk-secret", setup.DisplayCommand);
        var dial = Assert.Single(runner.Commands, x => x.FileName == "rasdial.exe");
        Assert.DoesNotContain("user-password", dial.DisplayCommand);
        Assert.Contains("user-password", dial.SensitiveValues!);
    }

    [Fact]
    public async Task ProfileErrorIsReadableAndDoesNotLookLikeCredentialFailure()
    {
        var runner = new FakeRunner(_ => new ProcessResult(1, "", "Set-VpnConnection : Missing an argument for parameter 'SplitTunneling'."));
        var service = new VpnService(runner);

        var result = await service.ConnectAsync(new VpnSettings("vpn.example", "alice", VpnProtocol.Sstp, false, null), "password");

        Assert.False(result.Succeeded);
        Assert.Contains("profile", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Missing an argument", result.Message);
        Assert.DoesNotContain("Check the username and password", result.Message);
        Assert.Single(runner.Commands);
    }

    [Fact]
    public async Task RasdialErrorUsesRasCodeAndActionableMessage()
    {
        var runner = new FakeRunner(spec => spec.FileName == "powershell.exe"
            ? new ProcessResult(0, "", "")
            : new ProcessResult(1, "Remote Access error 691", ""));
        var service = new VpnService(runner);

        var result = await service.ConnectAsync(new VpnSettings("vpn.example", "alice", VpnProtocol.Sstp, false, null), "password");

        Assert.False(result.Succeeded);
        Assert.Contains("RAS error 691", result.Message);
        Assert.Contains("Check the username and password", result.Message);
    }

    [Fact]
    public async Task SstpRasError778ExplainsServerIdentityCertificateFailure()
    {
        var runner = new FakeRunner(spec => spec.FileName == "powershell.exe"
            ? new ProcessResult(0, "", "")
            : new ProcessResult(778, "Remote Access error 778 - It was not possible to verify the identity of the server.", ""));
        var service = new VpnService(runner);

        var result = await service.ConnectAsync(new VpnSettings("vpn.example", "alice", VpnProtocol.Sstp, false, null), "password");

        Assert.False(result.Succeeded);
        Assert.Contains("RAS error 778", result.Message);
        Assert.Contains("SSTP server identity", result.Message);
        Assert.Contains("subject alternative names", result.Message);
        Assert.Contains("before the credentials are accepted", result.Message);
        Assert.DoesNotContain("Check the username and password", result.Message);
    }

    [Fact]
    public async Task DisconnectUsesRasdialAndWaitsForWindowsToReportDisconnected()
    {
        var statusChecks = 0;
        var runner = new FakeRunner(spec =>
        {
            if (spec.FileName == "rasdial.exe") return new ProcessResult(0, "Disconnected", "");
            if (spec.DisplayCommand?.Contains("check VPN connection status", StringComparison.Ordinal) == true)
                return new ProcessResult(0, ++statusChecks < 3 ? "VPN_STATUS_CONNECTED" : "VPN_STATUS_DISCONNECTED", "");
            return new ProcessResult(0, "", "");
        });
        var service = new VpnService(runner, disconnectPollInterval: TimeSpan.Zero, disconnectPollAttempts: 6);

        var result = await service.DisconnectAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.IsConnected);
        Assert.Equal(3, statusChecks);
        Assert.Contains(runner.Commands, x => x.FileName == "rasdial.exe" && x.Arguments.SequenceEqual([VpnService.ProfileName, "/disconnect"]));
        Assert.All(runner.Commands.Where(x => x.DisplayCommand?.Contains("check VPN connection status", StringComparison.Ordinal) == true),
            x => Assert.True(x.SuppressLogging));
    }

    [Fact]
    public async Task DisconnectRetriesOnceAndDoesNotClaimSuccessWhileStillConnected()
    {
        var disconnectCommands = 0;
        var runner = new FakeRunner(spec =>
        {
            if (spec.FileName == "rasdial.exe") { disconnectCommands++; return new ProcessResult(0, "", ""); }
            if (spec.DisplayCommand?.Contains("check VPN connection status", StringComparison.Ordinal) == true)
                return new ProcessResult(0, "VPN_STATUS_CONNECTED", "");
            return new ProcessResult(0, "", "");
        });
        var service = new VpnService(runner, disconnectPollInterval: TimeSpan.Zero, disconnectPollAttempts: 4);

        var result = await service.DisconnectAsync();

        Assert.False(result.Succeeded);
        Assert.True(result.IsConnected);
        Assert.Equal(2, disconnectCommands);
        Assert.Contains("still reports the VPN connected", result.Message);
    }

    [Fact]
    public void PowerShellCliXmlErrorsAreConvertedToReadableText()
    {
        const string clixml = "#< CLIXML\r\n<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\"><Obj S=\"progress\"><MS><S N=\"Message\">Preparing modules</S></MS></Obj><Obj S=\"Error\"><TN><T>System.Management.Automation.ErrorRecord</T></TN><ToString>Set-VpnConnection : Missing argument for parameter SplitTunneling.</ToString></Obj></Objs>";

        var normalized = PowerShellOutput.Normalize(clixml);

        Assert.Equal("Set-VpnConnection : Missing argument for parameter SplitTunneling.", normalized);
        Assert.DoesNotContain("CLIXML", normalized);
        Assert.DoesNotContain("Preparing modules", normalized);
    }

    private sealed class FakeRunner(Func<ProcessSpec, ProcessResult> respond) : IProcessRunner
    {
        public List<ProcessSpec> Commands { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        { Commands.Add(spec); return Task.FromResult(respond(spec)); }
    }
}

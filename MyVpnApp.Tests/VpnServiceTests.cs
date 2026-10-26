using System.Text;
using MyVpnApp.Core;

namespace MyVpnApp.Tests;

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

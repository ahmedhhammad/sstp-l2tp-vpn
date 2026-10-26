using MyVpnApp.Core;

namespace MyVpnApp.Tests;

public sealed class RouteManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MyVpnApp.Tests", Guid.NewGuid().ToString("N"));
    private string AppliedPath => Path.Combine(_directory, "applied.json");
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public async Task BuildsExpectedAddAndDeleteCommandsAndAppliesOnlyEnabled()
    {
        var runner = new FakeRunner(_ => new ProcessResult(0, "{\"InterfaceIndex\":42,\"Gateway\":\"10.99.242.0\"}", ""));
        var manager = CreateManager(runner);
        var yes = new RouteEntry(Guid.NewGuid(), "192.168.1.77/24", "", true);
        var no = new RouteEntry(Guid.NewGuid(), "10.20.0.0/16", "", false);
        var result = await manager.ApplyEnabledAsync([yes, no]);
        Assert.Single(result);
        Assert.True(result[0].Applied);
        Assert.Contains(runner.Commands, x => x.DisplayCommand == "route.exe ADD 192.168.1.0 MASK 255.255.255.0 10.99.242.0 METRIC 1 IF 42");
        Assert.DoesNotContain(runner.Commands, x => x.DisplayCommand!.Contains("10.20.0.0"));
        Assert.True(await manager.DeleteAsync(yes.Id));
        Assert.Contains(runner.Commands, x => x.DisplayCommand == "route.exe DELETE 192.168.1.0 MASK 255.255.255.0");
        Assert.DoesNotContain(runner.Commands, x => x.DisplayCommand!.Contains("10.20.0.0"));
    }

    [Fact]
    public async Task RetriesAddFailureThenSucceeds()
    {
        var count = 0;
        var runner = new FakeRunner(spec => spec.FileName == "route.exe" && spec.Arguments[0] == "ADD" && ++count == 1
            ? new ProcessResult(2, "", "temporary error")
            : new ProcessResult(0, "{\"InterfaceIndex\":7,\"Gateway\":null}", ""));
        var manager = CreateManager(runner);
        var route = new RouteEntry(Guid.NewGuid(), "10.2.0.0/16", "", true);
        var results = await manager.ApplyEnabledAsync([route]);
        Assert.True(Assert.Single(results).Applied);
        Assert.Equal(2, runner.Commands.Count(x => x.FileName == "route.exe"));
    }

    [Fact]
    public async Task DoesNotDeleteUnappliedAndCleansStaleRecordOnStartup()
    {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var manager = CreateManager(runner);
        Assert.False(await manager.DeleteAsync(Guid.NewGuid()));
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(AppliedPath, "[{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Destination\":\"10.3.0.0\",\"Netmask\":\"255.255.0.0\"}]");
        await manager.CleanupStaleAsync();
        Assert.Contains(runner.Commands, x => x.DisplayCommand == "route.exe DELETE 10.3.0.0 MASK 255.255.0.0");
        Assert.Equal("[]", (await File.ReadAllTextAsync(AppliedPath)).Trim());
    }

    private RouteManager CreateManager(FakeRunner runner) => new(runner, new GatewayResolver(runner), AppliedPath, retryDelay: TimeSpan.Zero);

    private sealed class FakeRunner(Func<ProcessSpec, ProcessResult> respond) : IProcessRunner
    {
        public List<ProcessSpec> Commands { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        { Commands.Add(spec); return Task.FromResult(respond(spec)); }
    }
}

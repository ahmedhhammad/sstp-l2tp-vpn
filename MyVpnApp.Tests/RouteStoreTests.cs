using MyVpnApp.Core;

namespace MyVpnApp.Tests;

public sealed class RouteStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MyVpnApp.Tests", Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_directory, "routes.json");
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public async Task SavesLoadsAddsDeletesAndPersistsAcrossInstances()
    {
        var first = new RouteStore(PathName);
        var route = new RouteEntry(Guid.NewGuid(), "192.168.1.77/24", "office", true);
        var added = await first.AddAsync(route);
        Assert.Equal("192.168.1.0/24", Assert.Single(added).Destination);
        var next = new RouteStore(PathName);
        Assert.Equal("office", Assert.Single(await next.LoadAsync()).Label);
        Assert.Empty(await next.DeleteAsync(route.Id));
        Assert.Empty(await first.LoadAsync());
    }

    [Fact]
    public async Task CorruptFileIsBackedUpAndReturnsEmpty()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(PathName, "not-json");
        Assert.Empty(await new RouteStore(PathName).LoadAsync());
        Assert.False(File.Exists(PathName));
        Assert.Single(Directory.GetFiles(_directory, "routes.json.corrupt-*"));
    }

    [Fact]
    public async Task AtomicSaveLeavesNoTemporaryFilesAndRejectsDuplicatesAndDefaultRoute()
    {
        var store = new RouteStore(PathName);
        var route = new RouteEntry(Guid.NewGuid(), "10.1.2.3/24", "", true);
        await store.SaveAsync([route]);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(route with { Id = Guid.NewGuid(), Destination = "10.1.2.0/24" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(route with { Destination = "0.0.0.0/0" }));
    }

    [Fact]
    public async Task SettingsStoreKeepsSecretsAsProtectedValuesOnly()
    {
        var protector = new CredentialProtector();
        var password = protector.Protect("user-secret-42");
        var psk = protector.Protect("tunnel-key-19");
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        await store.SaveAsync(new VpnSettings("vpn.example", "alice", VpnProtocol.L2tp, true, password, psk));
        var file = await File.ReadAllTextAsync(Path.Combine(_directory, "settings.json"));
        Assert.DoesNotContain("user-secret-42", file);
        Assert.DoesNotContain("tunnel-key-19", file);
        var loaded = await new SettingsStore(Path.Combine(_directory, "settings.json")).LoadAsync();
        Assert.Equal("user-secret-42", protector.Unprotect(loaded.ProtectedPassword!));
        Assert.Equal("tunnel-key-19", protector.Unprotect(loaded.ProtectedL2tpPsk!));
    }
}

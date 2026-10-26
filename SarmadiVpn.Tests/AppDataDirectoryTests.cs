using SarmadiVpn.Core;

namespace SarmadiVpn.Tests;

public sealed class AppDataDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SarmadiVpn.AppData.Tests", Guid.NewGuid().ToString("N"));
    private string CurrentPath => Path.Combine(_root, "SarmadiVpn");
    private string PreviousPath => Path.Combine(_root, "MyVpnApp");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void CopiesExistingDataToBrandedDirectoryAndKeepsPreviousFiles()
    {
        Directory.CreateDirectory(PreviousPath);
        File.WriteAllText(Path.Combine(PreviousPath, "settings.json"), "saved settings");
        File.WriteAllText(Path.Combine(PreviousPath, "routes.json"), "saved routes");
        File.WriteAllText(Path.Combine(PreviousPath, "applied-routes.json"), "applied routes");

        var resolved = AppDataDirectory.Resolve(_root);

        Assert.Equal(CurrentPath, resolved);
        Assert.Equal("saved settings", File.ReadAllText(Path.Combine(CurrentPath, "settings.json")));
        Assert.Equal("saved routes", File.ReadAllText(Path.Combine(CurrentPath, "routes.json")));
        Assert.Equal("applied routes", File.ReadAllText(Path.Combine(CurrentPath, "applied-routes.json")));
        Assert.True(File.Exists(Path.Combine(PreviousPath, "settings.json")));
        Assert.True(File.Exists(Path.Combine(CurrentPath, ".legacy-data-migration-v1")));
    }

    [Fact]
    public void KeepsNewDataAndDoesNotReimportDeletedFilesAfterMigration()
    {
        Directory.CreateDirectory(PreviousPath);
        Directory.CreateDirectory(CurrentPath);
        File.WriteAllText(Path.Combine(PreviousPath, "settings.json"), "old settings");
        File.WriteAllText(Path.Combine(PreviousPath, "routes.json"), "old routes");
        File.WriteAllText(Path.Combine(CurrentPath, "settings.json"), "new settings");

        AppDataDirectory.Resolve(_root);
        File.Delete(Path.Combine(CurrentPath, "routes.json"));

        var resolved = AppDataDirectory.Resolve(_root);

        Assert.Equal(CurrentPath, resolved);
        Assert.Equal("new settings", File.ReadAllText(Path.Combine(CurrentPath, "settings.json")));
        Assert.False(File.Exists(Path.Combine(CurrentPath, "routes.json")));
    }

    [Fact]
    public void ReturnsBrandedDirectoryWhenNoPreviousDataExists()
    {
        Assert.Equal(CurrentPath, AppDataDirectory.Resolve(_root));
        Assert.True(Directory.Exists(CurrentPath));
        Assert.False(File.Exists(Path.Combine(CurrentPath, ".legacy-data-migration-v1")));
    }
}

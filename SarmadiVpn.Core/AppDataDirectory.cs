namespace SarmadiVpn.Core;

public static class AppDataDirectory
{
    private const string CurrentDirectoryName = "SarmadiVpn";
    private const string PreviousDirectoryName = "MyVpnApp";
    private const string MigrationMarkerName = ".legacy-data-migration-v1";
    private static readonly string[] DataFileNames = ["settings.json", "routes.json", "applied-routes.json"];

    public static string GetCurrentUserDirectory(IAppLogger? logger = null)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("Windows did not provide the current user's roaming AppData directory.");

        return Resolve(root, logger);
    }

    public static string Resolve(string applicationDataRoot, IAppLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);

        var currentDirectory = Path.Combine(applicationDataRoot, CurrentDirectoryName);
        Directory.CreateDirectory(currentDirectory);

        var previousDirectory = Path.Combine(applicationDataRoot, PreviousDirectoryName);
        var migrationMarker = Path.Combine(currentDirectory, MigrationMarkerName);
        if (!Directory.Exists(previousDirectory) || File.Exists(migrationMarker))
            return currentDirectory;

        try
        {
            foreach (var fileName in DataFileNames)
            {
                var source = Path.Combine(previousDirectory, fileName);
                var destination = Path.Combine(currentDirectory, fileName);
                if (File.Exists(source) && !File.Exists(destination))
                    CopyAtomically(source, destination);
            }

            WriteMarkerAtomically(migrationMarker);
            logger?.Write("Copied saved settings and routes to the SarmadiVpn data folder. The previous files were kept as a backup.");
            return currentDirectory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.Write($"Could not migrate saved data to the SarmadiVpn folder; continuing with the previous folder. {ex.Message}");
            return previousDirectory;
        }
    }

    private static void CopyAtomically(string source, string destination)
    {
        var temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporaryPath);
            try { File.Move(temporaryPath, destination); }
            catch (IOException) when (File.Exists(destination)) { }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void WriteMarkerAtomically(string markerPath)
    {
        if (File.Exists(markerPath)) return;

        var temporaryPath = markerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, DateTimeOffset.UtcNow.ToString("O"));
            try { File.Move(temporaryPath, markerPath); }
            catch (IOException) when (File.Exists(markerPath)) { }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

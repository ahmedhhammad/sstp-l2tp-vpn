using System.Text.Json;

namespace SarmadiVpn.Core;

public sealed class SettingsStore(string path, IAppLogger? logger = null)
{
    private readonly IAppLogger _logger = logger ?? new NullAppLogger();

    public async Task<VpnSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new VpnSettings("", "", VpnProtocol.Sstp, false, null);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<VpnSettings>(stream, AtomicJsonFile.Options, cancellationToken)
                ?? new VpnSettings("", "", VpnProtocol.Sstp, false, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            var backup = AtomicJsonFile.BackupCorrupt(path);
            _logger.Write($"Could not read settings; moved the file to {backup}. {ex.Message}");
            return new VpnSettings("", "", VpnProtocol.Sstp, false, null);
        }
    }

    public Task SaveAsync(VpnSettings settings, CancellationToken cancellationToken = default) => AtomicJsonFile.WriteAsync(path, settings, cancellationToken);
}

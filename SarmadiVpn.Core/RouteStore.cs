using System.Text.Json;

namespace SarmadiVpn.Core;

public sealed class RouteStore(string path, IAppLogger? logger = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IAppLogger _logger = logger ?? new NullAppLogger();

    public async Task<IReadOnlyList<RouteEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return [];
            try
            {
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                return JsonSerializer.Deserialize<List<RouteEntry>>(bytes, AtomicJsonFile.Options) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                var backup = AtomicJsonFile.BackupCorrupt(path);
                _logger.Write($"Could not read route file; moved it to {backup}. Starting with an empty list. {ex.Message}");
                return [];
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(IEnumerable<RouteEntry> routes, CancellationToken cancellationToken = default)
    {
        var list = routes.ToList();
        var parsed = list.Select(r => (Route: r, Network: CidrHelper.Parse(r.Destination))).ToList();
        if (parsed.Any(x => x.Network.PrefixLength == 0)) throw new InvalidOperationException("The route 0.0.0.0/0 is disabled because it would redirect all traffic.");
        if (parsed.Select(x => x.Network.Cidr).Distinct(StringComparer.OrdinalIgnoreCase).Count() != parsed.Count)
            throw new InvalidOperationException("Duplicate routes are not allowed.");
        var normalized = parsed.Select(x => x.Route with { Destination = x.Network.Cidr }).ToList();
        await _gate.WaitAsync(cancellationToken);
        try { await AtomicJsonFile.WriteAsync(path, normalized, cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<RouteEntry>> AddAsync(RouteEntry route, CancellationToken cancellationToken = default)
    {
        var routes = (await LoadAsync(cancellationToken)).ToList();
        var network = CidrHelper.Parse(route.Destination);
        if (network.PrefixLength == 0) throw new InvalidOperationException("The route 0.0.0.0/0 would redirect all traffic and cannot be added.");
        if (routes.Any(x => CidrHelper.Parse(x.Destination).Cidr.Equals(network.Cidr, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That route already exists.");
        routes.Add(route with { Destination = network.Cidr });
        await SaveAsync(routes, cancellationToken);
        return routes;
    }

    public async Task<IReadOnlyList<RouteEntry>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var routes = (await LoadAsync(cancellationToken)).Where(x => x.Id != id).ToList();
        await SaveAsync(routes, cancellationToken);
        return routes;
    }
}

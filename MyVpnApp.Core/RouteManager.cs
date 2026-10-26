using System.Text.Json;

namespace MyVpnApp.Core;

public sealed class RouteManager(IProcessRunner runner, GatewayResolver gatewayResolver, string appliedRecordPath, IAppLogger? logger = null, TimeSpan? retryDelay = null)
{
    private readonly IAppLogger _logger = logger ?? new NullAppLogger();
    private readonly TimeSpan _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(900);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, AppliedRoute> _applied = [];

    public IReadOnlyCollection<Guid> AppliedIds => _applied.Keys.ToArray();

    public async Task<IReadOnlyList<RouteApplyResult>> ApplyEnabledAsync(IEnumerable<RouteEntry> routes, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var enabled = routes.Where(x => x.Enabled).ToArray();
            if (enabled.Length == 0) return [];
            GatewayInfo? gateway = null;
            for (var attempt = 0; attempt < 3 && gateway is null; attempt++)
            {
                gateway = await gatewayResolver.ResolveAsync(VpnService.ProfileName, cancellationToken);
                if (gateway is null && attempt < 2) await Task.Delay(_retryDelay, cancellationToken);
            }
            if (gateway is null || gateway.InterfaceIndex <= 0)
            {
                const string message = "VPN interface is not ready; no interface index could be discovered.";
                return enabled.Select(x => new RouteApplyResult(x.Destination, false, message)).ToArray();
            }

            var results = new List<RouteApplyResult>();
            foreach (var entry in enabled)
            {
                var network = CidrHelper.Parse(entry.Destination);
                if (network.PrefixLength == 0)
                {
                    results.Add(new(entry.Destination, false, "0.0.0.0/0 is blocked because it would redirect all traffic."));
                    continue;
                }
                if (_applied.ContainsKey(entry.Id))
                {
                    results.Add(new(network.Cidr, true, "Applied."));
                    continue;
                }
                var tracked = new AppliedRoute(network.Address, network.Netmask);
                ProcessResult? command = null;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    command = await AddRouteAsync(tracked, gateway, cancellationToken);
                    if (command.Succeeded) break;
                    if (attempt < 2) await Task.Delay(_retryDelay, cancellationToken);
                }
                if (command?.Succeeded != true)
                {
                    results.Add(new(network.Cidr, false, $"Route add failed (exit {command?.ExitCode ?? -1}): {command?.CombinedOutput.Trim()}"));
                    continue;
                }
                _applied[entry.Id] = tracked;
                try
                {
                    await SaveAppliedAsync(cancellationToken);
                    results.Add(new(network.Cidr, true, $"Applied via {gateway.RouteGateway}, interface {gateway.InterfaceIndex}."));
                }
                catch (Exception ex)
                {
                    _applied.Remove(entry.Id);
                    await DeleteRouteAsync(tracked, cancellationToken);
                    results.Add(new(network.Cidr, false, $"Could not record route for crash cleanup: {ex.Message}"));
                }
            }
            return results;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid routeId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_applied.TryGetValue(routeId, out var applied)) return false;
            var result = await DeleteRouteAsync(applied, cancellationToken);
            if (!result.Succeeded)
            {
                _logger.Write($"Route delete failed for {applied.Destination}: {result.CombinedOutput}");
                return false;
            }
            _applied.Remove(routeId);
            await SaveAppliedAsync(cancellationToken);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task CleanupAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var item in _applied.ToArray())
            {
                var result = await DeleteRouteAsync(item.Value, cancellationToken);
                if (result.Succeeded)
                {
                    _applied.Remove(item.Key);
                    await SaveAppliedAsync(cancellationToken);
                }
                else _logger.Write($"Route cleanup failed for {item.Value.Destination}: {result.CombinedOutput}");
            }
            await SaveAppliedAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task CleanupStaleAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(appliedRecordPath)) return;
            List<AppliedRecord> stale;
            try
            {
                var json = await File.ReadAllTextAsync(appliedRecordPath, cancellationToken);
                stale = JsonSerializer.Deserialize<List<AppliedRecord>>(json, AtomicJsonFile.Options) ?? [];
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                var backup = AtomicJsonFile.BackupCorrupt(appliedRecordPath);
                _logger.Write($"Could not read stale route record; moved it to {backup}. {ex.Message}");
                return;
            }
            var remaining = new List<AppliedRecord>();
            foreach (var item in stale)
            {
                var route = new AppliedRoute(item.Destination, item.Netmask);
                var result = await DeleteRouteAsync(route, cancellationToken);
                if (!result.Succeeded)
                {
                    _logger.Write($"Stale route cleanup failed for {route.Destination}: {result.CombinedOutput}");
                    remaining.Add(item);
                }
            }
            await AtomicJsonFile.WriteAsync(appliedRecordPath, remaining, cancellationToken);
            _logger.Write($"Cleaned {stale.Count} route record(s) from the previous run.");
        }
        finally { _gate.Release(); }
    }

    private Task<ProcessResult> AddRouteAsync(AppliedRoute route, GatewayInfo gateway, CancellationToken token) => runner.RunAsync(
        new ProcessSpec("route.exe", ["ADD", route.Destination, "MASK", route.Netmask, gateway.RouteGateway, "METRIC", "1", "IF", gateway.InterfaceIndex.ToString()],
            $"route.exe ADD {route.Destination} MASK {route.Netmask} {gateway.RouteGateway} METRIC 1 IF {gateway.InterfaceIndex}"), token);

    private Task<ProcessResult> DeleteRouteAsync(AppliedRoute route, CancellationToken token) => runner.RunAsync(
        new ProcessSpec("route.exe", ["DELETE", route.Destination, "MASK", route.Netmask],
            $"route.exe DELETE {route.Destination} MASK {route.Netmask}"), token);

    private Task SaveAppliedAsync(CancellationToken token) => AtomicJsonFile.WriteAsync(appliedRecordPath,
        _applied.Select(x => new AppliedRecord(x.Key, x.Value.Destination, x.Value.Netmask)).ToArray(), token);

    private sealed record AppliedRecord(Guid Id, string Destination, string Netmask);
}

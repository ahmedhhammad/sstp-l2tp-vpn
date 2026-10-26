using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyVpnApp.Core;

public sealed class GatewayResolver(IProcessRunner runner, IAppLogger? logger = null)
{
    private readonly IAppLogger _logger = logger ?? new NullAppLogger();
    private static readonly Regex IpconfigAddress = new(@"(?:PPP adapter|Ethernet adapter)\s+([^:\r\n]+)[\s\S]*?IPv4 Address[^:]*:\s*(\d{1,3}(?:\.\d{1,3}){3})[\s\S]*?Default Gateway[^:]*:\s*(\d{1,3}(?:\.\d{1,3}){3})?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<GatewayInfo?> ResolveAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        var name = PowerShell.Quote(connectionName);
        var script = $"$ErrorActionPreference='Stop'; $name={name}; $adapters=Get-NetAdapter -IncludeHidden | Where-Object {{ $_.Status -eq 'Up' }}; $a=$adapters | Where-Object {{ $_.Name -eq $name -or $_.Name -like ('*'+$name+'*') }} | Select-Object -First 1; if (-not $a) {{ $a=$adapters | Where-Object {{ $_.InterfaceDescription -match 'WAN Miniport.*(SSTP|L2TP)' }} | Select-Object -First 1 }}; if ($a) {{ $r=Get-NetRoute -InterfaceIndex $a.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object {{ $_.NextHop -ne '0.0.0.0' -and $_.NextHop -ne '127.0.0.1' }} | Sort-Object RouteMetric | Select-Object -First 1; [pscustomobject]@{{ InterfaceIndex=$a.ifIndex; Gateway=if($r){{$r.NextHop}}else{{$null}}; Alias=$a.Name }} | ConvertTo-Json -Compress }}";
        var result = await runner.RunAsync(PowerShell.Encoded(script, "discover VPN interface and peer"), cancellationToken);
        if (result.Succeeded)
        {
            var parsed = ParsePowerShellJson(result.StandardOutput);
            if (parsed is not null)
            {
                _logger.Write($"VPN route target: interface {parsed.InterfaceIndex}, gateway {parsed.RouteGateway} ({parsed.Source}).");
                return parsed;
            }
        }
        _logger.Write("Could not find the VPN interface through Get-NetAdapter; checking ipconfig output.");
        var ipconfig = await runner.RunAsync(new ProcessSpec("ipconfig.exe", ["/all"], "ipconfig.exe /all"), cancellationToken);
        var fallback = ParseIpconfig(ipconfig.StandardOutput, connectionName);
        if (fallback is not null) _logger.Write($"VPN route target: interface {fallback.InterfaceIndex}, gateway {fallback.RouteGateway} ({fallback.Source}).");
        return fallback;
    }

    public static GatewayInfo? ParsePowerShellJson(string text)
    {
        var candidate = text.Trim();
        var start = candidate.IndexOf('{');
        var end = candidate.LastIndexOf('}');
        if (start < 0 || end < start) return null;
        try
        {
            using var json = JsonDocument.Parse(candidate[start..(end + 1)]);
            var root = json.RootElement;
            if (!TryInt(root, "InterfaceIndex", out var index) || index <= 0) return null;
            var gateway = GetString(root, "Gateway");
            return new GatewayInfo(index, IsIpv4(gateway) ? gateway : null, "Get-NetRoute");
        }
        catch (JsonException) { return null; }
    }

    public static GatewayInfo? ParseIpconfig(string text, string connectionName, int interfaceIndex = 0)
    {
        foreach (Match match in IpconfigAddress.Matches(text))
        {
            if (!match.Groups[1].Value.Contains(connectionName, StringComparison.OrdinalIgnoreCase)) continue;
            var gateway = match.Groups[3].Value;
            return new GatewayInfo(interfaceIndex, IsIpv4(gateway) ? gateway : null, "ipconfig (on-link fallback if no peer address)");
        }
        return null;
    }

    public static GatewayInfo? ParseGetNetRoute(string text, int? expectedInterfaceIndex = null)
    {
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = Regex.Split(line.Trim(), @"\s+");
            if (fields.Length < 3 || !int.TryParse(fields[0], out var index)) continue;
            if (expectedInterfaceIndex is not null && index != expectedInterfaceIndex.Value) continue;
            if (!fields[1].Contains('/')) continue;
            var gateway = fields[2];
            if (IsIpv4(gateway) && gateway != "0.0.0.0" && gateway != "127.0.0.1")
                return new GatewayInfo(index, gateway, "Get-NetRoute table");
        }
        return null;
    }

    private static bool TryInt(JsonElement root, string key, out int value) => root.TryGetProperty(key, out var e) && int.TryParse(e.ToString(), out value) || (value = 0) != 0;
    private static string? GetString(JsonElement root, string key) => root.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    private static bool IsIpv4(string? value) => System.Net.IPAddress.TryParse(value, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
}

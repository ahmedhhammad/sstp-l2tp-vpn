using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SarmadiVpn.Core;

public sealed class GatewayResolver(IProcessRunner runner, IAppLogger? logger = null)
{
    private readonly IAppLogger _logger = logger ?? new NullAppLogger();
    private static readonly Regex AdapterHeader = new(@"(?im)^(?:PPP|Ethernet) adapter\s+([^:\r\n]+):\s*$", RegexOptions.Compiled);
    private static readonly Regex Ipv4Line = new(@"(?im)IPv4 Address[^\r\n:]*:\s*(?:\([^)]*\)\s*)?(\d{1,3}(?:\.\d{1,3}){3})", RegexOptions.Compiled);
    private static readonly Regex GatewayLine = new(@"(?im)Default Gateway[^\r\n:]*:\s*(\d{1,3}(?:\.\d{1,3}){3})", RegexOptions.Compiled);

    public async Task<GatewayInfo?> ResolveAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        var name = PowerShell.Quote(connectionName);
        var script = $"$ErrorActionPreference='SilentlyContinue'; $name={name}; " +
            "$config = (& ipconfig.exe /all | Out-String); " +
            "$blocks = $config -split '(?m)(?=^(?:PPP|Ethernet) adapter\\s+)'; " +
            "$block = $blocks | Where-Object { $_ -match ('(?im)^(?:PPP|Ethernet) adapter\\s+' + [regex]::Escape($name) + ':') } | Select-Object -First 1; " +
            "$ipMatch = if($block){[regex]::Match($block,'(?im)IPv4 Address[^\\r\\n:]*:\\s*(?:\\([^)]*\\)\\s*)?(\\d{1,3}(?:\\.\\d{1,3}){3})')}; " +
            "$peerMatch = if($block){[regex]::Match($block,'(?im)Default Gateway[^\\r\\n:]*:\\s*(\\d{1,3}(?:\\.\\d{1,3}){3})')}; " +
            "$ip = if($ipMatch.Success){$ipMatch.Groups[1].Value}else{$null}; $peer = if($peerMatch.Success){$peerMatch.Groups[1].Value}else{$null}; " +
            "$a = $null; if($ip){$a = Get-NetIPAddress -AddressFamily IPv4 -IPAddress $ip -ErrorAction SilentlyContinue | Select-Object -First 1}; " +
            "if(-not $a){$a = Get-NetIPInterface -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceAlias -eq $name -and $_.ConnectionState -eq 'Connected' } | Select-Object -First 1}; " +
            "if($a){$idx=$a.InterfaceIndex; if(-not $peer){$r=Get-NetRoute -InterfaceIndex $idx -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.NextHop -ne '0.0.0.0' -and $_.NextHop -ne '127.0.0.1' } | Sort-Object RouteMetric | Select-Object -First 1; if($r){$peer=$r.NextHop}}; " +
            "[pscustomobject]@{InterfaceIndex=$idx; Gateway=$peer; LocalAddress=$ip; Alias=$a.InterfaceAlias} | ConvertTo-Json -Compress}";

        var result = await runner.RunAsync(PowerShell.Encoded(script, "discover PPP interface and peer"), cancellationToken);
        if (result.Succeeded)
        {
            var parsed = ParsePowerShellJson(result.StandardOutput);
            if (parsed is not null)
            {
                _logger.Write($"VPN PPP route target: interface {parsed.InterfaceIndex}, peer {parsed.RouteGateway} ({parsed.Source}).");
                return parsed;
            }
        }

        _logger.Write("PowerShell could not map the PPP adapter; checking ipconfig output and resolving its address to an interface.");
        var ipconfig = await runner.RunAsync(new ProcessSpec("ipconfig.exe", ["/all"], "ipconfig.exe /all"), cancellationToken);
        var details = ParseIpconfigDetails(ipconfig.StandardOutput, connectionName);
        if (details is null) return null;

        var detail = details.Value;
        var fallbackScript = "$ErrorActionPreference='SilentlyContinue'; $ip=" + PowerShell.Quote(detail.LocalAddress) +
            "; $a=Get-NetIPAddress -AddressFamily IPv4 -IPAddress $ip -ErrorAction SilentlyContinue | Select-Object -First 1; " +
            "if($a){[pscustomobject]@{InterfaceIndex=$a.InterfaceIndex; Gateway=" +
            (detail.Gateway is null ? "$null" : PowerShell.Quote(detail.Gateway)) +
            "; LocalAddress=$ip; Alias=$a.InterfaceAlias} | ConvertTo-Json -Compress}";
        var lookup = await runner.RunAsync(PowerShell.Encoded(fallbackScript, "map PPP address to interface"), cancellationToken);
        var mapped = lookup.Succeeded ? ParsePowerShellJson(lookup.StandardOutput) : null;
        if (mapped is not null)
        {
            var resolved = mapped with { Source = "ipconfig/Get-NetIPAddress" };
            _logger.Write($"VPN PPP route target: interface {resolved.InterfaceIndex}, peer {resolved.RouteGateway} ({resolved.Source}).");
            return resolved;
        }

        // Never guess an interface. A zero next hop is safe only after the correct interface is known.
        return new GatewayInfo(0, detail.Gateway, "ipconfig; interface index unavailable");
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
            return new GatewayInfo(index, IsUsableIpv4(gateway) ? gateway : null, "ipconfig/Get-NetIPAddress/Get-NetRoute");
        }
        catch (JsonException) { return null; }
    }

    public static GatewayInfo? ParseIpconfig(string text, string connectionName, int interfaceIndex = 0)
    {
        var details = ParseIpconfigDetails(text, connectionName);
        return details is null ? null : new GatewayInfo(interfaceIndex, details.Value.Gateway, "ipconfig (on-link if no PPP peer is reported)");
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
            if (IsUsableIpv4(gateway)) return new GatewayInfo(index, gateway, "Get-NetRoute table");
        }
        return null;
    }

    private static (string LocalAddress, string? Gateway)? ParseIpconfigDetails(string text, string connectionName)
    {
        var headers = AdapterHeader.Matches(text);
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (!header.Groups[1].Value.Contains(connectionName, StringComparison.OrdinalIgnoreCase)) continue;
            var end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            var section = text[header.Index..end];
            var ip = Ipv4Line.Match(section);
            if (!ip.Success || !IsUsableIpv4(ip.Groups[1].Value)) continue;
            var gatewayMatch = GatewayLine.Match(section);
            var gateway = gatewayMatch.Success && IsUsableIpv4(gatewayMatch.Groups[1].Value) ? gatewayMatch.Groups[1].Value : null;
            return (ip.Groups[1].Value, gateway);
        }
        return null;
    }

    private static bool TryInt(JsonElement root, string key, out int value)
    {
        value = 0;
        return root.TryGetProperty(key, out var element) && int.TryParse(element.ToString(), out value);
    }

    private static string? GetString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static bool IsUsableIpv4(string? value) =>
        IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any) && !IPAddress.IsLoopback(address);
}

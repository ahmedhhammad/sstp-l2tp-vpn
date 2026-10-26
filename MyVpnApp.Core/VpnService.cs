namespace MyVpnApp.Core;

public sealed class VpnService(IProcessRunner runner, IAppLogger? logger = null)
{
    public const string ProfileName = "MyVpnApp";
    private readonly IAppLogger _logger = logger ?? new NullAppLogger();

    public async Task<VpnOperationResult> ConnectAsync(VpnSettings settings, string password, string? l2tpPsk = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Server)) return new(false, -1, "Enter the VPN server address.");
        if (string.IsNullOrWhiteSpace(settings.Username)) return new(false, -1, "Enter the username.");
        if (string.IsNullOrEmpty(password)) return new(false, -1, "Enter the password.");
        if (settings.Protocol == VpnProtocol.L2tp && string.IsNullOrEmpty(l2tpPsk)) return new(false, -1, "Enter the L2TP pre-shared key.");

        var tunnel = settings.Protocol == VpnProtocol.Sstp ? "Sstp" : "L2tp";
        var psk = settings.Protocol == VpnProtocol.L2tp ? $" -L2tpPsk {PowerShell.Quote(l2tpPsk!)} -Force" : "";
        var script = $"$ErrorActionPreference='Stop'; $name={PowerShell.Quote(ProfileName)}; $server={PowerShell.Quote(settings.Server.Trim())}; $existing=Get-VpnConnection -Name $name -ErrorAction SilentlyContinue; if ($null -eq $existing) {{ Add-VpnConnection -Name $name -ServerAddress $server -TunnelType {tunnel}{psk} -AuthenticationMethod MSChapv2 -EncryptionLevel Required -SplitTunneling -Force | Out-Null }} else {{ Set-VpnConnection -Name $name -ServerAddress $server -TunnelType {tunnel}{psk} -AuthenticationMethod MSChapv2 -EncryptionLevel Required -SplitTunneling -Force | Out-Null }}";
        var setup = await runner.RunAsync(PowerShell.Encoded(script, "configure VPN profile"), cancellationToken);
        if (!setup.Succeeded) return new(false, setup.ExitCode, FriendlyError(setup.ExitCode, setup.CombinedOutput, "VPN profile setup failed"));

        var rasdial = await runner.RunAsync(new ProcessSpec("rasdial.exe", [ProfileName, settings.Username, password], "rasdial.exe MyVpnApp <username> <password hidden>"), cancellationToken);
        if (rasdial.Succeeded) return new(true, rasdial.ExitCode, "Connection established.");
        return new(false, rasdial.ExitCode, FriendlyError(rasdial.ExitCode, rasdial.CombinedOutput, "rasdial failed"));
    }

    public async Task<ProcessResult> DisconnectAsync(CancellationToken cancellationToken = default) =>
        await runner.RunAsync(new ProcessSpec("rasdial.exe", [ProfileName, "/disconnect"], "rasdial.exe MyVpnApp /disconnect"), cancellationToken);

    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
    {
        var script = $"$ErrorActionPreference='SilentlyContinue'; $c=Get-VpnConnection -Name {PowerShell.Quote(ProfileName)}; if ($c -and $c.ConnectionStatus -eq 'Connected') {{ 'Connected' }} else {{ 'Disconnected' }}";
        var result = await runner.RunAsync(PowerShell.Encoded(script, "check VPN connection status"), cancellationToken);
        return result.Succeeded && result.StandardOutput.Contains("Connected", StringComparison.OrdinalIgnoreCase) && !result.StandardOutput.Contains("Disconnected", StringComparison.OrdinalIgnoreCase);
    }

    private static string FriendlyError(int code, string output, string fallback)
    {
        var detail = string.IsNullOrWhiteSpace(output) ? fallback : output.Trim();
        var codeText = code < 0 ? "process could not start" : $"exit code {code}";
        var help = detail.ToLowerInvariant() switch
        {
            var s when s.Contains("691") || s.Contains("credentials") || s.Contains("authentication") => "Check the username and password.",
            var s when s.Contains("certificate") || s.Contains("security certificate") => "Check the SSTP server certificate and that its name matches the server address.",
            var s when s.Contains("789") || s.Contains("psk") || s.Contains("pre-shared") => "L2TP/IPsec negotiation failed. Check the pre-shared key and server settings.",
            var s when s.Contains("806") || s.Contains("unreachable") || s.Contains("remote computer") => "The VPN server could not be reached. Check the address, firewall, and network connection.",
            _ => "Check the VPN settings and the command output in the log."
        };
        return $"{fallback} ({codeText}). {help} {detail}";
    }
}

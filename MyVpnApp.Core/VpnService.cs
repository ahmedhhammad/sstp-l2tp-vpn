namespace MyVpnApp.Core;

using System.Text.RegularExpressions;

public sealed class VpnService(IProcessRunner runner, IAppLogger? logger = null, TimeSpan? disconnectPollInterval = null, int disconnectPollAttempts = 12)
{
    public const string ProfileName = "MyVpnApp";
    private readonly IAppLogger _logger = logger ?? new NullAppLogger();
    private readonly TimeSpan _disconnectPollInterval = disconnectPollInterval ?? TimeSpan.FromMilliseconds(500);
    private readonly int _disconnectPollAttempts = Math.Max(1, disconnectPollAttempts);
    private int? _lastStatusFailure;

    public async Task<VpnOperationResult> ConnectAsync(VpnSettings settings, string password, string? l2tpPsk = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Server)) return new(false, -1, "Enter the VPN server address.");
        if (string.IsNullOrWhiteSpace(settings.Username)) return new(false, -1, "Enter the username.");
        if (string.IsNullOrEmpty(password)) return new(false, -1, "Enter the password.");
        if (settings.Protocol == VpnProtocol.L2tp && string.IsNullOrEmpty(l2tpPsk)) return new(false, -1, "Enter the L2TP pre-shared key.");

        var tunnel = settings.Protocol == VpnProtocol.Sstp ? "Sstp" : "L2tp";
        var psk = settings.Protocol == VpnProtocol.L2tp ? $" -L2tpPsk {PowerShell.Quote(l2tpPsk!)} -Force" : "";
        var script = $"$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; $name={PowerShell.Quote(ProfileName)}; $server={PowerShell.Quote(settings.Server.Trim())}; $existing=Get-VpnConnection -Name $name -ErrorAction SilentlyContinue; if ($null -eq $existing) {{ Add-VpnConnection -Name $name -ServerAddress $server -TunnelType {tunnel}{psk} -AuthenticationMethod MSChapv2 -EncryptionLevel Required -SplitTunneling:$true -Force | Out-Null }} else {{ Set-VpnConnection -Name $name -ServerAddress $server -TunnelType {tunnel}{psk} -AuthenticationMethod MSChapv2 -EncryptionLevel Required -SplitTunneling $true -Force | Out-Null }}";
        var sensitiveValues = settings.Protocol == VpnProtocol.L2tp ? new[] { l2tpPsk!, PowerShell.Quote(l2tpPsk!) } : [];
        var setup = await runner.RunAsync(PowerShell.Encoded(script, "configure VPN profile", sensitiveValues), cancellationToken);
        if (!setup.Succeeded) return new(false, setup.ExitCode, ProfileSetupError(setup.ExitCode, setup.CombinedOutput));

        var rasdial = await runner.RunAsync(new ProcessSpec("rasdial.exe", [ProfileName, settings.Username, password], "rasdial.exe MyVpnApp <username> <password hidden>", [password]), cancellationToken);
        if (rasdial.Succeeded) return new(true, rasdial.ExitCode, "Connection established.");
        return new(false, rasdial.ExitCode, FriendlyError(rasdial.ExitCode, rasdial.CombinedOutput, "rasdial failed"));
    }

    public async Task<VpnOperationResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var disconnectSpec = new ProcessSpec("rasdial.exe", [ProfileName, "/disconnect"], "rasdial.exe MyVpnApp /disconnect");
        var command = await runner.RunAsync(disconnectSpec, cancellationToken);
        var lastStatus = (bool?)null;
        var retried = false;

        for (var attempt = 0; attempt < _disconnectPollAttempts; attempt++)
        {
            lastStatus = await GetConnectedStatusAsync(cancellationToken);
            if (lastStatus == false)
            {
                var detail = command.Succeeded ? "VPN disconnected." : $"Windows reports the VPN disconnected, although rasdial returned exit code {command.ExitCode}: {command.CombinedOutput.Trim()}";
                return new(true, command.ExitCode, detail, false);
            }

            if (lastStatus == true && !retried && attempt >= Math.Max(1, _disconnectPollAttempts / 3))
            {
                _logger.Write("VPN still reports connected; retrying rasdial disconnect once.");
                var retry = await runner.RunAsync(disconnectSpec, cancellationToken);
                if (!retry.Succeeded) _logger.Write($"rasdial disconnect retry returned {retry.ExitCode}: {retry.CombinedOutput}");
                retried = true;
            }

            if (attempt + 1 < _disconnectPollAttempts) await Task.Delay(_disconnectPollInterval, cancellationToken);
        }

        var message = lastStatus == true
            ? $"Windows still reports the VPN connected after rasdial disconnect (exit code {command.ExitCode}). Check the activity log, then try Disconnect again. {command.CombinedOutput.Trim()}"
            : $"Windows did not confirm whether the VPN disconnected (rasdial exit code {command.ExitCode}). Routes were kept for safety. {command.CombinedOutput.Trim()}";
        return new(false, command.ExitCode == 0 ? 1 : command.ExitCode, message, lastStatus);
    }

    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
        => await GetConnectedStatusAsync(cancellationToken) == true;

    public async Task<bool?> GetConnectedStatusAsync(CancellationToken cancellationToken = default)
    {
        var script = $"$ErrorActionPreference='SilentlyContinue'; $c=Get-VpnConnection -Name {PowerShell.Quote(ProfileName)} -ErrorAction SilentlyContinue; if ($c -and $c.ConnectionStatus -eq 'Connected') {{ 'VPN_STATUS_CONNECTED' }} else {{ 'VPN_STATUS_DISCONNECTED' }}";
        var statusCommand = PowerShell.Encoded(script, "check VPN connection status");
        var result = await runner.RunAsync(statusCommand with { SuppressLogging = true }, cancellationToken);
        if (!result.Succeeded)
        {
            if (_lastStatusFailure != result.ExitCode)
            {
                _logger.Write($"VPN status check failed (exit {result.ExitCode}): {result.CombinedOutput.Trim()}");
                _lastStatusFailure = result.ExitCode;
            }
            return null;
        }
        if (result.StandardOutput.Contains("VPN_STATUS_CONNECTED", StringComparison.OrdinalIgnoreCase)) { _lastStatusFailure = null; return true; }
        if (result.StandardOutput.Contains("VPN_STATUS_DISCONNECTED", StringComparison.OrdinalIgnoreCase)) { _lastStatusFailure = null; return false; }
        if (_lastStatusFailure != int.MinValue)
        {
            _logger.Write("VPN status check returned no recognized connection state; keeping the last known state.");
            _lastStatusFailure = int.MinValue;
        }
        return null;
    }

    private static string FriendlyError(int code, string output, string fallback)
    {
        var detail = string.IsNullOrWhiteSpace(output) ? fallback : output.Trim();
        var rasCode = Regex.Match(detail, @"(?:RAS\s+)?(?:remote access\s+)?error(?:\s+code)?\s*[:#]?\s*(\d{3,5})", RegexOptions.IgnoreCase);
        var effectiveCode = rasCode.Success && int.TryParse(rasCode.Groups[1].Value, out var parsedCode) ? parsedCode : code;
        var codeText = effectiveCode < 0 ? "process could not start" : rasCode.Success ? $"RAS error {effectiveCode} (rasdial exit code {code})" : $"exit code {code}";
        var help = (effectiveCode.ToString() + " " + detail).ToLowerInvariant() switch
        {
            var s when s.Contains("691") || s.Contains("credentials") || s.Contains("authentication") => "Check the username and password.",
            var s when s.Contains("certificate") || s.Contains("security certificate") => "Check the SSTP server certificate and that its name matches the server address.",
            var s when s.Contains("789") || s.Contains("psk") || s.Contains("pre-shared") => "L2TP/IPsec negotiation failed. Check the pre-shared key and server settings.",
            var s when s.Contains("806") || s.Contains("809") || s.Contains("unreachable") || s.Contains("remote computer") || s.Contains("server did not respond") => "The VPN server could not be reached. Check the address, firewall, and network connection.",
            var s when s.Contains("868") || s.Contains("name could not be resolved") => "Windows could not resolve the VPN server name. Check the address and DNS settings.",
            _ => "Check the VPN settings and the command output in the log."
        };
        return $"{fallback} ({codeText}). {help} {detail}";
    }

    private static string ProfileSetupError(int code, string output)
    {
        var detail = string.IsNullOrWhiteSpace(output) ? "Windows did not provide an error description." : output.Trim();
        var codeText = code < 0 ? "process could not start" : $"exit code {code}";
        return $"Windows could not create or update the VPN profile ({codeText}). Check the server address and VPN profile settings. Details: {detail}";
    }
}

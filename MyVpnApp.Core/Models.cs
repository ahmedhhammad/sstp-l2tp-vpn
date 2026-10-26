namespace MyVpnApp.Core;

public enum VpnProtocol { Sstp, L2tp }
public enum VpnState { Disconnected, Connecting, Connected, Disconnecting, Error }

public sealed record RouteEntry(Guid Id, string Destination, string Label, bool Enabled);
public sealed record VpnSettings(string Server, string Username, VpnProtocol Protocol, bool RememberCredentials, string? ProtectedPassword, string? ProtectedL2tpPsk = null);
public sealed record GatewayInfo(int InterfaceIndex, string? Gateway, string Source)
{
    public string RouteGateway => string.IsNullOrWhiteSpace(Gateway) ? "0.0.0.0" : Gateway;
}
public sealed record AppliedRoute(string Destination, string Netmask);
public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments, string? DisplayCommand = null, IReadOnlyList<string>? SensitiveValues = null, bool SuppressLogging = false);
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
    public string CombinedOutput => string.Join(Environment.NewLine, new[] { StandardOutput, StandardError }.Where(x => !string.IsNullOrWhiteSpace(x)));
}
public sealed record RouteApplyResult(string Destination, bool Applied, string Message);
public sealed record VpnOperationResult(bool Succeeded, int ExitCode, string Message, bool? IsConnected = null);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default);
}

public interface IAppLogger
{
    void Write(string message);
}

public sealed class NullAppLogger : IAppLogger
{
    public void Write(string message) { }
}

public sealed class ProcessRunner(IAppLogger logger) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
    {
        var display = spec.DisplayCommand ?? string.Join(" ", new[] { spec.FileName }.Concat(spec.Arguments));
        if (!spec.SuppressLogging) logger.Write("> " + display);
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = spec.FileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var arg in spec.Arguments) process.StartInfo.ArgumentList.Add(arg);
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Could not start {spec.FileName}.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var standardOutput = await stdout;
            var standardError = await stderr;
            if (Path.GetFileNameWithoutExtension(spec.FileName).Equals("powershell", StringComparison.OrdinalIgnoreCase))
            {
                standardOutput = PowerShellOutput.Normalize(standardOutput);
                standardError = PowerShellOutput.Normalize(standardError);
            }
            foreach (var secret in spec.SensitiveValues ?? [])
            {
                if (string.IsNullOrEmpty(secret)) continue;
                standardOutput = standardOutput.Replace(secret, "<redacted>", StringComparison.Ordinal);
                standardError = standardError.Replace(secret, "<redacted>", StringComparison.Ordinal);
            }
            var result = new ProcessResult(process.ExitCode, standardOutput, standardError);
            if (!spec.SuppressLogging) logger.Write(result.CombinedOutput.Length == 0 ? $"Exit code: {result.ExitCode}" : result.CombinedOutput);
            return result;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            if (!spec.SuppressLogging) logger.Write(ex.Message);
            return new ProcessResult(-1, "", ex.Message);
        }
    }
}

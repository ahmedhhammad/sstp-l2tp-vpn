using System.Text;

namespace MyVpnApp.Core;

internal static class PowerShell
{
    public static ProcessSpec Encoded(string script, string display, IReadOnlyList<string>? sensitiveValues = null) => new("powershell.exe",
        ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
        "powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand " + display, sensitiveValues);

    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}

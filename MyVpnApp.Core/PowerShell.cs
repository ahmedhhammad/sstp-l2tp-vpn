using System.Text;

namespace MyVpnApp.Core;

internal static class PowerShell
{
    public static ProcessSpec Encoded(string script, string display) => new("powershell.exe",
        ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
        "powershell.exe -NoProfile -NonInteractive -EncodedCommand " + display);

    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}

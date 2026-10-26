using System.Xml.Linq;

namespace MyVpnApp.Core;

public static class PowerShellOutput
{
    private const string CliXmlMarker = "#< CLIXML";

    public static string Normalize(string value)
    {
        var marker = value.IndexOf(CliXmlMarker, StringComparison.Ordinal);
        if (marker < 0) return value.Trim();
        var xmlText = value[(marker + CliXmlMarker.Length)..].Trim();
        try
        {
            var document = XDocument.Parse(xmlText);
            var errors = document.Descendants()
                .Where(element => element.Name.LocalName == "Obj" && (string?)element.Attribute("S") == "Error")
                .Select(element => element.Descendants().FirstOrDefault(child => child.Name.LocalName == "ToString")?.Value)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal);
            return string.Join(Environment.NewLine, errors).Trim();
        }
        catch (System.Xml.XmlException)
        {
            return "PowerShell returned an unreadable error response.";
        }
    }
}

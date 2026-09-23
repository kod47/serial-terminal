using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace SerialTerminal.Core;

public sealed record PortInfo(string Name, string Description)
{
    public string Display => string.IsNullOrEmpty(Description) ? Name : $"{Name}  —  {Description}";
}

public static partial class PortEnumerator
{
    /// <summary>Fast (registry based), safe to call every second.</summary>
    public static string[] GetNames() =>
        SerialPort.GetPortNames()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => int.TryParse(n.AsSpan(3), out var num) ? num : int.MaxValue)
            .ThenBy(n => n)
            .ToArray();

    /// <summary>Slow (WMI), adds friendly device names. Call off the UI thread.</summary>
    public static List<PortInfo> GetPorts()
    {
        var names = GetNames();
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (var obj in searcher.Get())
            {
                if (obj["Name"] is not string name) continue;
                var m = ComRegex().Match(name);
                if (m.Success) descriptions[m.Groups[1].Value] = name.Replace(m.Value, "").Trim();
            }
        }
        catch
        {
            // WMI unavailable - names only
        }

        return names.Select(n => new PortInfo(n, descriptions.GetValueOrDefault(n, ""))).ToList();
    }

    [GeneratedRegex(@"\((COM\d+)\)")]
    private static partial Regex ComRegex();
}

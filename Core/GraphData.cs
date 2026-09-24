using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace SerialTerminal.Core;

public sealed class GraphSeries(string name, Color color)
{
    public string Name { get; } = name;
    public Color Color { get; } = color;

    /// <summary>T = seconds since graph start; sorted by T.</summary>
    public List<(double T, double V)> Points { get; } = new();

    public double Last => Points.Count > 0 ? Points[^1].V : double.NaN;
}

/// <summary>
/// Numeric values extracted from received text lines.
/// "temp=23.5 hum:40" gives named series; otherwise plain numbers become #1, #2, ...
/// </summary>
public sealed partial class GraphData
{
    public const int MaxSeries = 8;
    private const int MaxPointsPerSeries = 20_000;

    private static readonly Color[] Colors =
    [
        Color.FromRgb(0x4F, 0xC1, 0xFF), Color.FromRgb(0xF7, 0x6B, 0x15), Color.FromRgb(0x30, 0xA4, 0x6C),
        Color.FromRgb(0xD6, 0x40, 0x9F), Color.FromRgb(0xC9, 0x9A, 0x06), Color.FromRgb(0x8E, 0x4E, 0xC6),
        Color.FromRgb(0xE5, 0x48, 0x4D), Color.FromRgb(0x0E, 0xA5, 0xB7),
    ];

    private DateTime _start = DateTime.Now;

    public List<GraphSeries> Series { get; } = new();

    public double Now => (DateTime.Now - _start).TotalSeconds;

    public void AddLine(string text, DateTime time)
    {
        var values = Parse(text);
        if (values.Count == 0) return;

        double t = (time - _start).TotalSeconds;
        foreach (var (name, value) in values)
        {
            var series = Series.Find(s => s.Name == name);
            if (series == null)
            {
                if (Series.Count >= MaxSeries) continue;
                series = new GraphSeries(name, Colors[Series.Count % Colors.Length]);
                Series.Add(series);
            }
            series.Points.Add((t, value));
            if (series.Points.Count > MaxPointsPerSeries) series.Points.RemoveRange(0, MaxPointsPerSeries / 10);
        }
    }

    public void Clear()
    {
        Series.Clear();
        _start = DateTime.Now;
    }

    private static List<(string Name, double Value)> Parse(string text)
    {
        var result = new List<(string, double)>();
        foreach (Match m in NamedValue().Matches(text))
            if (double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                result.Add((m.Groups[1].Value, v));
        if (result.Count > 0) return result;

        int index = 1;
        foreach (Match m in PlainNumber().Matches(text))
            if (double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                result.Add(($"#{index++}", v));
        return result;
    }

    [GeneratedRegex(@"([A-Za-z_][\w.]*)\s*[:=]\s*([-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)")]
    private static partial Regex NamedValue();

    [GeneratedRegex(@"(?<![\w.])[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?(?![\w.])")]
    private static partial Regex PlainNumber();
}

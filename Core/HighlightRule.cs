using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SerialTerminal.Core;

/// <summary>Lines containing <see cref="Text"/> are shown in <see cref="Color"/>.</summary>
public partial class HighlightRule : ObservableObject
{
    // Mid-brightness colors that stay readable on both dark and light backgrounds.
    private static readonly Dictionary<string, Brush> Palette = new()
    {
        ["Red"] = Make("#E5484D"),
        ["Orange"] = Make("#F76B15"),
        ["Yellow"] = Make("#C99A06"),
        ["Green"] = Make("#30A46C"),
        ["Cyan"] = Make("#0EA5B7"),
        ["Blue"] = Make("#3E8EF7"),
        ["Magenta"] = Make("#D6409F"),
        ["Gray"] = Make("#8B8D98"),
    };

    public static string[] ColorNames { get; } = Palette.Keys.ToArray();

    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _matchCase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Brush))]
    private string _color = "Red";

    public Brush Brush => BrushFor(Color);

    public bool Matches(string line) =>
        Enabled && Text.Length > 0 &&
        line.Contains(Text, MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    public static Brush BrushFor(string? name) =>
        name != null && Palette.TryGetValue(name, out var b) ? b : Palette["Red"];

    private static Brush Make(string hex)
    {
        var b = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

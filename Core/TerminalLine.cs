using System.ComponentModel;
using System.Windows.Media;

namespace SerialTerminal.Core;

public enum LineKind { Rx, Tx, Info, Error }

public enum DisplayMode { Text, Hex, HexAscii, ModbusRtu }

public enum LineEnding { None, CR, LF, CRLF }

/// <summary>One line in the terminal view. Text can grow while the line is still open.</summary>
public sealed class TerminalLine : INotifyPropertyChanged
{
    private string _text = "";
    private Brush? _highlight;

    public TerminalLine(LineKind kind, DateTime time)
    {
        Kind = kind;
        Time = time;
    }

    public LineKind Kind { get; }
    public DateTime Time { get; }
    public string TimeText => Time.ToString("HH:mm:ss.fff");

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }

    /// <summary>Color from a matching highlight rule, or null.</summary>
    public Brush? Highlight
    {
        get => _highlight;
        set
        {
            if (ReferenceEquals(_highlight, value)) return;
            _highlight = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Highlight)));
        }
    }

    public string Tag => Kind switch
    {
        LineKind.Rx => "RX",
        LineKind.Tx => "TX",
        LineKind.Info => "--",
        _ => "!!",
    };

    public string ToLogString() => $"{TimeText} {Tag} {Text}";

    public event PropertyChangedEventHandler? PropertyChanged;
}

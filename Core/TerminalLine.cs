using System.ComponentModel;

namespace SerialTerminal.Core;

public enum LineKind { Rx, Tx, Info, Error }

public enum DisplayMode { Text, Hex, HexAscii }

public enum LineEnding { None, CR, LF, CRLF }

/// <summary>One line in the terminal view. Text can grow while the line is still open.</summary>
public sealed class TerminalLine : INotifyPropertyChanged
{
    private string _text = "";

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

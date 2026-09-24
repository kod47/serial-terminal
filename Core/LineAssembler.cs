using System.Text;

namespace SerialTerminal.Core;

/// <summary>
/// Turns raw byte chunks into display lines.
/// Text mode breaks on '\n'; HEX modes break every N bytes or after an idle gap (packet boundary).
/// </summary>
public sealed class LineAssembler
{
    private readonly StringBuilder _text = new();
    private readonly List<byte> _bytes = new();
    private TerminalLine? _open;
    private DateTime _lastTime;
    private DisplayMode _mode = DisplayMode.Text;

    public int HexBytesPerLine { get; set; } = 16;
    public int MaxPacketBytes { get; set; } = 256;
    public TimeSpan PacketGap { get; set; } = TimeSpan.FromMilliseconds(20);

    /// <summary>The line that is still receiving data, if any.</summary>
    public TerminalLine? OpenLine => _open;

    /// <summary>A new line was created (it may still grow).</summary>
    public event Action<TerminalLine>? LineStarted;

    /// <summary>A line is finished and will not change anymore.</summary>
    public event Action<TerminalLine>? LineCompleted;

    public DisplayMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            Close();
            _mode = value;
        }
    }

    public void Append(LineKind kind, byte[] data, DateTime time)
    {
        if (_open != null && (_open.Kind != kind || (_mode != DisplayMode.Text && time - _lastTime > PacketGap)))
            Close();
        _lastTime = time;

        foreach (byte b in data)
        {
            if (_mode == DisplayMode.Text)
            {
                if (b == '\n')
                {
                    EnsureOpen(kind, time);
                    Close();
                    continue;
                }
                if (b == '\r') continue;
                EnsureOpen(kind, time);
                _text.Append(b switch
                {
                    (byte)'\t' => '\t',
                    < 0x20 => (char)(0x2400 + b), // Unicode "control pictures": ␀ ␁ ... ␛
                    0x7F => '␡',
                    _ => (char)b,
                });
            }
            else
            {
                EnsureOpen(kind, time);
                _bytes.Add(b);
                if (_bytes.Count >= (_mode == DisplayMode.ModbusRtu ? MaxPacketBytes : HexBytesPerLine)) Close();
            }
        }

        if (_open != null) _open.Text = Render();
    }

    public void AddMessage(LineKind kind, string text)
    {
        Close();
        var line = new TerminalLine(kind, DateTime.Now) { Text = text };
        LineStarted?.Invoke(line);
        LineCompleted?.Invoke(line);
    }

    /// <summary>Finishes the currently open line, if any.</summary>
    public void Close()
    {
        if (_open == null) return;
        _open.Text = Render(final: true);
        var line = _open;
        _open = null;
        LineCompleted?.Invoke(line);
    }

    /// <summary>Drops the open line without completing it (used by Clear).</summary>
    public void Reset() => _open = null;

    private void EnsureOpen(LineKind kind, DateTime time)
    {
        if (_open != null) return;
        _text.Clear();
        _bytes.Clear();
        _open = new TerminalLine(kind, time);
        LineStarted?.Invoke(_open);
    }

    private string Render(bool final = false)
    {
        if (_mode == DisplayMode.Text) return _text.ToString();

        var sb = new StringBuilder(_bytes.Count * 3 + 64);
        foreach (var b in _bytes) sb.Append(b.ToString("X2")).Append(' ');
        if (_mode == DisplayMode.Hex) return sb.ToString().TrimEnd();

        if (_mode == DisplayMode.ModbusRtu)
        {
            // decode only a complete frame
            if (final && _open != null) sb.Append("  ").Append(ModbusRtu.Describe(_bytes.ToArray(), _open.Kind));
            return sb.ToString().TrimEnd();
        }

        sb.Append(' ', (HexBytesPerLine - _bytes.Count) * 3 + 1);
        foreach (var b in _bytes) sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        return sb.ToString();
    }
}

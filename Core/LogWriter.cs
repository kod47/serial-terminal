using System.Text;

namespace SerialTerminal.Core;

/// <summary>Appends completed terminal lines to a file while monitoring continues.</summary>
public sealed class LogWriter : IDisposable
{
    private readonly StreamWriter _writer;

    public LogWriter(string path)
    {
        FilePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(path, append: true, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public string FilePath { get; }

    public void Write(TerminalLine line)
    {
        try { _writer.WriteLine(line.ToLogString()); }
        catch { /* disk full / removed - ignore, monitoring must continue */ }
    }

    public void Dispose() => _writer.Dispose();
}

using System.IO.Ports;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SerialTerminal.Core;

public partial class Macro : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _data = "";
    [ObservableProperty] private bool _isHex;
}

public partial class SendLine : ObservableObject
{
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isHex;
    [ObservableProperty] private LineEnding _lineEnding = LineEnding.CRLF;
}

/// <summary>Persisted in %AppData%\SerialTerminal\settings.json.</summary>
public sealed class AppSettings
{
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public Parity Parity { get; set; } = Parity.None;
    public StopBits StopBits { get; set; } = StopBits.One;
    public Handshake Handshake { get; set; } = Handshake.None;
    public bool Dtr { get; set; }
    public bool Rts { get; set; }
    public bool AutoReconnect { get; set; } = true;

    public bool? DarkTheme { get; set; }
    public double FontSize { get; set; } = 14;
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Text;
    public bool ShowTimestamps { get; set; } = true;
    public bool WordWrap { get; set; } = true;
    public bool AutoScroll { get; set; } = true;
    public int MaxLines { get; set; } = 100_000;

    public List<SendLine> SendLines { get; set; } = [];
    public List<string> History { get; set; } = [];
    public List<Macro> Macros { get; set; } = [];

    public string LogFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SerialTerminal Logs");

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SerialTerminal", "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch
        {
            // corrupted file - start with defaults
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // not critical
        }
    }
}

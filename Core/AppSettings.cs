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
    [ObservableProperty] private bool _appendCrc;
}

public partial class SendLine : ObservableObject
{
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isHex;
    [ObservableProperty] private bool _appendCrc;
    [ObservableProperty] private LineEnding _lineEnding = LineEnding.CRLF;
    [ObservableProperty] private int _repeatMs = 1000;

    /// <summary>Periodic sending is never restored on startup.</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _repeat;

    [JsonIgnore] public long NextDueMs { get; set; }
}

/// <summary>Settings of one tab.</summary>
public sealed class SessionSettings
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
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Text;
    public bool ShowGraph { get; set; }
    public double GraphWindowSeconds { get; set; } = 30;
    public bool GraphAutoScale { get; set; } = true;
    public bool GraphIncludeZero { get; set; } = true;
    public double GraphYMin { get; set; }
    public double GraphYMax { get; set; } = 100;
    public string SendFilePath { get; set; } = "";
    public int SendFileChunk { get; set; } = 1024;
    public int SendFileDelayMs { get; set; }
    public List<SendLine> SendLines { get; set; } = [];
}

/// <summary>Persisted in %AppData%\SerialTerminal\settings.json.</summary>
public sealed class AppSettings
{
    public bool? DarkTheme { get; set; }
    public double FontSize { get; set; } = 14;
    public bool ShowTimestamps { get; set; } = true;
    public bool WordWrap { get; set; } = true;
    public bool AutoScroll { get; set; } = true;
    public int MaxLines { get; set; } = 100_000;

    public bool ShowConnectionBar { get; set; } = true;
    public bool ShowDisplayBar { get; set; } = true;
    public bool ShowFilterBar { get; set; } = true;
    public bool ShowMacroBar { get; set; } = true;
    public bool ShowSendBar { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;

    public List<SessionSettings> Sessions { get; set; } = [];
    public int SelectedSession { get; set; }

    public List<string> History { get; set; } = [];
    public List<Macro> Macros { get; set; } = [];
    public List<HighlightRule> HighlightRules { get; set; } =
    [
        new() { Text = "error", Color = "Red" },
        new() { Text = "warn", Color = "Orange" },
    ];

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
        AppSettings? settings = null;
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                // v0.1 kept the port settings at top level - same names as SessionSettings
                if (settings is { Sessions.Count: 0 })
                    settings.Sessions.Add(JsonSerializer.Deserialize<SessionSettings>(json, Options) ?? new());
            }
        }
        catch
        {
            // corrupted file - start with defaults
        }

        settings ??= new AppSettings();
        if (settings.Sessions.Count == 0) settings.Sessions.Add(new SessionSettings());
        return settings;
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

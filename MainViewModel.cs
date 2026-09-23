using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SerialTerminal.Core;

namespace SerialTerminal;

public partial class MainViewModel : ObservableObject
{
    private const int MacroCount = 8;
    private const int HistoryLimit = 50;
    private const int SendLineCount = 2;

    private readonly AppSettings _settings;
    private readonly SerialConnection _connection = new();
    private readonly LineAssembler _assembler = new();

    // Filled from the serial thread (RX) and UI thread (TX); drained by the UI timer so order is preserved.
    private readonly ConcurrentQueue<(LineKind Kind, byte[] Data, DateTime Time)> _incoming = new();

    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _portTimer;
    private string[] _knownPortNames = [];
    private bool _refreshingPorts;
    private PortSettings? _activePort;
    private LogWriter? _log;
    private int _historyIndex = -1;

    public MainViewModel()
    {
        _settings = AppSettings.Load();

        BaudRateText = _settings.BaudRate.ToString();
        DataBits = _settings.DataBits;
        Parity = _settings.Parity;
        StopBits = _settings.StopBits;
        Handshake = _settings.Handshake;
        Dtr = _settings.Dtr;
        Rts = _settings.Rts;
        AutoReconnect = _settings.AutoReconnect;
        IsDarkTheme = _settings.DarkTheme ?? ThemeManager.SystemIsDark();
        ThemeManager.Apply(IsDarkTheme);
        FontSize = _settings.FontSize;
        DisplayMode = _settings.DisplayMode;
        ShowTimestamps = _settings.ShowTimestamps;
        WordWrap = _settings.WordWrap;
        AutoScroll = _settings.AutoScroll;
        LogFolder = _settings.LogFolder;

        Macros = new ObservableCollection<Macro>(_settings.Macros.Take(MacroCount));
        while (Macros.Count < MacroCount) Macros.Add(new Macro { Name = $"M{Macros.Count + 1}" });

        SendLines = new ObservableCollection<SendLine>(_settings.SendLines.Take(SendLineCount));
        while (SendLines.Count < SendLineCount) SendLines.Add(new SendLine());

        _assembler.Mode = DisplayMode;
        _assembler.LineStarted += line => Lines.Add(line);
        _assembler.LineCompleted += line => _log?.Write(line);

        _connection.DataReceived += (data, time) => _incoming.Enqueue((LineKind.Rx, data, time));
        _connection.ConnectionLost += ex => Application.Current.Dispatcher.BeginInvoke(() => OnConnectionLost(ex.Message));

        var dispatcher = Dispatcher.CurrentDispatcher;
        _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Background, (_, _) => ProcessIncoming(), dispatcher);
        _portTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => PollPorts(), dispatcher);

        UpdateStatus();
        _ = RefreshPortsAsync(_settings.PortName);
    }

    #region Collections and option lists

    public ObservableCollection<TerminalLine> Lines { get; } = new();
    public ObservableCollection<PortInfo> Ports { get; } = new();
    public ObservableCollection<Macro> Macros { get; }
    public ObservableCollection<SendLine> SendLines { get; }

    public int[] BaudRates { get; } =
        [300, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200, 128000, 230400, 250000, 256000, 460800, 921600];
    public int[] DataBitsValues { get; } = [5, 6, 7, 8];
    public Parity[] ParityValues { get; } = Enum.GetValues<Parity>();
    public StopBits[] StopBitsValues { get; } = [StopBits.One, StopBits.OnePointFive, StopBits.Two];
    public Handshake[] HandshakeValues { get; } = Enum.GetValues<Handshake>();
    public DisplayMode[] DisplayModes { get; } = Enum.GetValues<DisplayMode>();
    public LineEnding[] LineEndings { get; } = Enum.GetValues<LineEnding>();

    #endregion

    #region Bindable properties

    [ObservableProperty] private PortInfo? _selectedPort;
    [ObservableProperty] private string _baudRateText = "115200";
    [ObservableProperty] private int _dataBits;
    [ObservableProperty] private Parity _parity;
    [ObservableProperty] private StopBits _stopBits;
    [ObservableProperty] private Handshake _handshake;
    [ObservableProperty] private bool _dtr;
    [ObservableProperty] private bool _rts;
    [ObservableProperty] private bool _autoReconnect;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSettings), nameof(ConnectButtonText))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSettings), nameof(ConnectButtonText))]
    private bool _isWaitingReconnect;

    public bool CanEditSettings => !IsConnected && !IsWaitingReconnect;
    public string ConnectButtonText => IsConnected || IsWaitingReconnect ? "Disconnect" : "Connect";

    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private double _fontSize;
    [ObservableProperty] private DisplayMode _displayMode;
    [ObservableProperty] private bool _showTimestamps;
    [ObservableProperty] private bool _wordWrap;
    [ObservableProperty] private bool _autoScroll;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogButtonText))]
    private bool _isLogging;

    public string LogButtonText => IsLogging ? "Stop log" : "Start log";
    [ObservableProperty] private string _logFolder = "";
    [ObservableProperty] private string _logStatus = "";

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private long _rxBytes;
    [ObservableProperty] private long _txBytes;

    partial void OnDtrChanged(bool value) => TrySetLine(() => _connection.SetDtr(value), "DTR");
    partial void OnRtsChanged(bool value) => TrySetLine(() => _connection.SetRts(value), "RTS");
    partial void OnDisplayModeChanged(DisplayMode value) => _assembler.Mode = value;
    partial void OnIsDarkThemeChanged(bool value) => ThemeManager.Apply(value);

    #endregion

    #region Connection

    [RelayCommand]
    private void ToggleConnection()
    {
        if (IsConnected || IsWaitingReconnect)
        {
            _connection.Close();
            IsConnected = false;
            IsWaitingReconnect = false;
            AddMessage(LineKind.Info, "Disconnected");
            UpdateStatus();
            return;
        }

        if (SelectedPort is null)
        {
            AddMessage(LineKind.Error, "No port selected");
            return;
        }
        if (!int.TryParse(BaudRateText, out var baud) || baud <= 0)
        {
            AddMessage(LineKind.Error, $"Invalid baud rate: '{BaudRateText}'");
            return;
        }

        var settings = new PortSettings(SelectedPort.Name, baud, DataBits, Parity, StopBits, Handshake, Dtr, Rts);
        try
        {
            _connection.Open(settings);
        }
        catch (Exception ex)
        {
            AddMessage(LineKind.Error, $"Cannot open {settings.PortName}: {ex.Message}");
            return;
        }

        _activePort = settings;
        IsConnected = true;
        AddMessage(LineKind.Info, $"Connected to {settings.PortName}  {settings.Describe()}");
        UpdateStatus();
    }

    private void OnConnectionLost(string reason)
    {
        if (!IsConnected) return;
        _connection.Close();
        IsConnected = false;
        IsWaitingReconnect = AutoReconnect;
        AddMessage(LineKind.Error, $"Connection lost: {reason}" + (AutoReconnect ? " — waiting for port..." : ""));
        UpdateStatus();
    }

    private void TryReconnect()
    {
        if (_activePort is null) return;
        try
        {
            _connection.Open(_activePort with { Dtr = Dtr, Rts = Rts });
        }
        catch
        {
            return; // device may still be initializing, retry on next tick
        }
        IsWaitingReconnect = false;
        IsConnected = true;
        AddMessage(LineKind.Info, $"Reconnected to {_activePort.PortName}");
        UpdateStatus();
    }

    private void TrySetLine(Action action, string name)
    {
        if (!IsConnected) return;
        try { action(); }
        catch (Exception ex) { AddMessage(LineKind.Error, $"Cannot set {name}: {ex.Message}"); }
    }

    private void PollPorts()
    {
        var names = PortEnumerator.GetNames();
        if (!names.SequenceEqual(_knownPortNames))
            _ = RefreshPortsAsync(_activePort != null && !CanEditSettings ? _activePort.PortName : SelectedPort?.Name);

        if (_activePort is null) return;
        bool present = names.Contains(_activePort.PortName, StringComparer.OrdinalIgnoreCase);
        if (IsConnected && !present) OnConnectionLost("port removed");
        else if (IsWaitingReconnect && present) TryReconnect();
    }

    [RelayCommand]
    private Task RefreshPorts() => RefreshPortsAsync(SelectedPort?.Name);

    private async Task RefreshPortsAsync(string? preferred)
    {
        if (_refreshingPorts) return;
        _refreshingPorts = true;
        try
        {
            var ports = await Task.Run(PortEnumerator.GetPorts);
            _knownPortNames = ports.Select(p => p.Name).ToArray();
            Ports.Clear();
            foreach (var p in ports) Ports.Add(p);
            SelectedPort = Ports.FirstOrDefault(p => string.Equals(p.Name, preferred, StringComparison.OrdinalIgnoreCase))
                           ?? Ports.FirstOrDefault();
        }
        finally
        {
            _refreshingPorts = false;
        }
    }

    private void UpdateStatus()
    {
        StatusText = IsConnected && _activePort != null ? $"{_activePort.PortName}   {_activePort.Describe()}"
            : IsWaitingReconnect && _activePort != null ? $"Waiting for {_activePort.PortName}..."
            : "Disconnected";
    }

    #endregion

    #region Data flow

    private void ProcessIncoming()
    {
        if (_incoming.IsEmpty) return;
        while (_incoming.TryDequeue(out var item))
        {
            if (item.Kind == LineKind.Rx) RxBytes += item.Data.Length;
            else TxBytes += item.Data.Length;
            _assembler.Append(item.Kind, item.Data, item.Time);
        }
        TrimLines();
        OutputChanged?.Invoke();
    }

    /// <summary>Raised after new output was added to <see cref="Lines"/> (used for auto-scroll).</summary>
    public event Action? OutputChanged;

    private void TrimLines()
    {
        // Remove in chunks so we don't do it on every batch.
        int excess = Lines.Count - _settings.MaxLines;
        if (excess < 1000) return;
        for (int i = 0; i < excess; i++) Lines.RemoveAt(0);
    }

    private void AddMessage(LineKind kind, string text)
    {
        ProcessIncoming(); // keep chronological order
        _assembler.AddMessage(kind, text);
        OutputChanged?.Invoke();
    }

    [RelayCommand]
    private void Send(SendLine line)
    {
        var text = line.Text;
        if (string.IsNullOrEmpty(text) && (line.IsHex || line.LineEnding == LineEnding.None)) return;
        if (SendData(text, line.IsHex, line.LineEnding)) AddToHistory(text);
    }

    private bool SendData(string text, bool hex, LineEnding lineEnding)
    {
        if (!IsConnected)
        {
            AddMessage(LineKind.Error, "Not connected");
            return false;
        }

        byte[] data;
        try
        {
            data = hex ? HexParser.Parse(text) : Encoding.UTF8.GetBytes(text + LineEndingChars(lineEnding));
        }
        catch (FormatException ex)
        {
            AddMessage(LineKind.Error, ex.Message);
            return false;
        }
        if (data.Length == 0) return false;

        try
        {
            _connection.Write(data);
        }
        catch (Exception ex)
        {
            AddMessage(LineKind.Error, $"Send failed: {ex.Message}");
            return false;
        }
        _incoming.Enqueue((LineKind.Tx, data, DateTime.Now));
        return true;
    }

    private static string LineEndingChars(LineEnding e) => e switch
    {
        LineEnding.CR => "\r",
        LineEnding.LF => "\n",
        LineEnding.CRLF => "\r\n",
        _ => "",
    };

    [RelayCommand]
    private void SendMacro(Macro macro)
    {
        if (string.IsNullOrEmpty(macro.Data)) EditMacro(macro);
        else SendData(macro.Data, macro.IsHex, SendLines[0].LineEnding);
    }

    public void EditMacro(Macro macro)
    {
        new MacroEditWindow(macro) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    private void AddToHistory(string text)
    {
        _historyIndex = -1;
        if (string.IsNullOrWhiteSpace(text)) return;
        var h = _settings.History;
        h.Remove(text);
        h.Add(text);
        if (h.Count > HistoryLimit) h.RemoveAt(0);
    }

    public void HistoryUp(SendLine line)
    {
        var h = _settings.History;
        if (h.Count == 0) return;
        _historyIndex = _historyIndex < 0 ? h.Count - 1 : Math.Max(0, _historyIndex - 1);
        line.Text = h[_historyIndex];
    }

    public void HistoryDown(SendLine line)
    {
        var h = _settings.History;
        if (_historyIndex < 0) return;
        _historyIndex++;
        if (_historyIndex >= h.Count)
        {
            _historyIndex = -1;
            line.Text = "";
        }
        else
        {
            line.Text = h[_historyIndex];
        }
    }

    public string FormatForCopy(TerminalLine line) => ShowTimestamps ? $"{line.TimeText}  {line.Text}" : line.Text;

    #endregion

    #region View / file commands

    [RelayCommand]
    private void Clear()
    {
        Lines.Clear();
        _assembler.Reset();
    }

    [RelayCommand]
    private void ResetCounters()
    {
        RxBytes = 0;
        TxBytes = 0;
    }

    private string FileBaseName() =>
        $"{_activePort?.PortName ?? SelectedPort?.Name ?? "serial"}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

    [RelayCommand]
    private void SaveAs()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = FileBaseName() + ".log",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllLines(dlg.FileName, Lines.Select(l => l.ToLogString()), new UTF8Encoding(false));
            AddMessage(LineKind.Info, $"Saved to {dlg.FileName}");
        }
        catch (Exception ex)
        {
            AddMessage(LineKind.Error, $"Save failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ToggleLog()
    {
        if (IsLogging)
        {
            var path = _log!.FilePath;
            AddMessage(LineKind.Info, "Log stopped");
            _log.Dispose();
            _log = null;
            IsLogging = false;
            LogStatus = "";
            AddMessage(LineKind.Info, $"Log saved: {path}");
            return;
        }

        var file = Path.Combine(LogFolder, FileBaseName() + ".log");
        try
        {
            _log = new LogWriter(file);
        }
        catch (Exception ex)
        {
            AddMessage(LineKind.Error, $"Cannot create log: {ex.Message}");
            return;
        }
        IsLogging = true;
        LogStatus = $"Logging: {Path.GetFileName(file)}";
        AddMessage(LineKind.Info, $"Logging to {file}");
    }

    [RelayCommand]
    private void ChooseLogFolder()
    {
        var dlg = new OpenFolderDialog { InitialDirectory = LogFolder };
        if (dlg.ShowDialog() == true) LogFolder = dlg.FolderName;
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            Process.Start(new ProcessStartInfo(LogFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddMessage(LineKind.Error, $"Cannot open folder: {ex.Message}");
        }
    }

    #endregion

    public void Shutdown()
    {
        _uiTimer.Stop();
        _portTimer.Stop();
        _connection.Close();
        _log?.Dispose();

        _settings.PortName = _activePort?.PortName ?? SelectedPort?.Name ?? "";
        if (int.TryParse(BaudRateText, out var baud)) _settings.BaudRate = baud;
        _settings.DataBits = DataBits;
        _settings.Parity = Parity;
        _settings.StopBits = StopBits;
        _settings.Handshake = Handshake;
        _settings.Dtr = Dtr;
        _settings.Rts = Rts;
        _settings.AutoReconnect = AutoReconnect;
        _settings.DarkTheme = IsDarkTheme;
        _settings.FontSize = FontSize;
        _settings.DisplayMode = DisplayMode;
        _settings.ShowTimestamps = ShowTimestamps;
        _settings.WordWrap = WordWrap;
        _settings.AutoScroll = AutoScroll;
        _settings.SendLines = SendLines.ToList();
        _settings.LogFolder = LogFolder;
        _settings.Macros = Macros.ToList();
        _settings.Save();
    }
}

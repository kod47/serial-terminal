using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SerialTerminal.Core;

namespace SerialTerminal;

/// <summary>One tab: a port with its terminal output, send lines, log and graph.</summary>
public partial class SessionViewModel : ObservableObject
{
    private const int SendLineCount = 2;

    private readonly SerialConnection _connection = new();
    private readonly LineAssembler _assembler = new();

    // Filled from the serial thread (RX) and UI thread (TX); drained on the UI thread so order is preserved.
    private readonly ConcurrentQueue<(LineKind Kind, byte[] Data, DateTime Time)> _incoming = new();

    private PortSettings? _activePort;
    private string? _preferredPortName;
    private LogWriter? _log;
    private int _historyIndex = -1;

    public SessionViewModel(MainViewModel main, SessionSettings s)
    {
        Main = main;
        _preferredPortName = string.IsNullOrEmpty(s.PortName) ? null : s.PortName;

        BaudRateText = s.BaudRate.ToString();
        DataBits = s.DataBits;
        Parity = s.Parity;
        StopBits = s.StopBits;
        Handshake = s.Handshake;
        Dtr = s.Dtr;
        Rts = s.Rts;
        AutoReconnect = s.AutoReconnect;
        DisplayMode = s.DisplayMode;
        ShowGraph = s.ShowGraph;
        GraphWindowSeconds = s.GraphWindowSeconds;
        GraphAutoScale = s.GraphAutoScale;
        GraphIncludeZero = s.GraphIncludeZero;
        GraphYMin = s.GraphYMin;
        GraphYMax = s.GraphYMax;
        SendFilePath = s.SendFilePath;
        SendFileChunk = s.SendFileChunk;
        SendFileDelayMs = s.SendFileDelayMs;

        SendLines = new ObservableCollection<SendLine>(s.SendLines.Take(SendLineCount));
        while (SendLines.Count < SendLineCount) SendLines.Add(new SendLine());
        foreach (var line in SendLines) line.PropertyChanged += OnSendLinePropertyChanged;

        _assembler.Mode = DisplayMode;
        _assembler.LineStarted += line => Lines.Add(line);
        _assembler.LineCompleted += OnLineCompleted;

        _connection.DataReceived += (data, time) => _incoming.Enqueue((LineKind.Rx, data, time));
        _connection.ConnectionLost += ex => Application.Current.Dispatcher.BeginInvoke(() => OnConnectionLost(ex.Message));

        SelectPreferredPort();
        UpdateStatus();
    }

    public MainViewModel Main { get; }

    /// <summary>Raised after new output was added to <see cref="Lines"/> (used for auto-scroll).</summary>
    public event Action? OutputChanged;

    #region Collections and option lists

    public ObservableCollection<TerminalLine> Lines { get; } = new();
    public ObservableCollection<SendLine> SendLines { get; }
    public GraphData Graph { get; } = new();

    public int[] BaudRates { get; } =
        [300, 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200, 128000, 230400, 250000, 256000, 460800, 921600];
    public int[] DataBitsValues { get; } = [5, 6, 7, 8];
    public Parity[] ParityValues { get; } = Enum.GetValues<Parity>();
    public StopBits[] StopBitsValues { get; } = [StopBits.One, StopBits.OnePointFive, StopBits.Two];
    public Handshake[] HandshakeValues { get; } = Enum.GetValues<Handshake>();
    public DisplayMode[] DisplayModes { get; } = Enum.GetValues<DisplayMode>();
    public LineEnding[] LineEndings { get; } = Enum.GetValues<LineEnding>();
    public double[] GraphWindows { get; } = [10, 30, 60, 120, 300];
    public int[] RepeatIntervals { get; } = [10, 50, 100, 200, 500, 1000, 2000, 5000, 10000];

    #endregion

    #region Bindable properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private PortInfo? _selectedPort;

    [ObservableProperty] private string _baudRateText = "115200";
    [ObservableProperty] private int _dataBits;
    [ObservableProperty] private Parity _parity;
    [ObservableProperty] private StopBits _stopBits;
    [ObservableProperty] private Handshake _handshake;
    [ObservableProperty] private bool _dtr;
    [ObservableProperty] private bool _rts;
    [ObservableProperty] private bool _autoReconnect;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSettings), nameof(ConnectButtonText), nameof(State), nameof(Title))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSettings), nameof(ConnectButtonText), nameof(State), nameof(Title))]
    private bool _isWaitingReconnect;

    public bool CanEditSettings => !IsConnected && !IsWaitingReconnect;
    public string ConnectButtonText => IsConnected || IsWaitingReconnect ? "Disconnect" : "Connect";
    public string State => IsConnected ? "Connected" : IsWaitingReconnect ? "Waiting" : "Disconnected";

    public string Title => !CanEditSettings && _activePort != null
        ? _activePort.PortName
        : SelectedPort?.Name ?? _preferredPortName ?? "New";

    /// <summary>True for the tab currently shown.</summary>
    [ObservableProperty] private bool _isActive;

    [ObservableProperty] private DisplayMode _displayMode;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _filterEnabled;
    [ObservableProperty] private bool _showGraph;
    [ObservableProperty] private double _graphWindowSeconds = 30;
    [ObservableProperty] private bool _graphAutoScale = true;
    [ObservableProperty] private bool _graphIncludeZero = true;
    [ObservableProperty] private double _graphYMin;
    [ObservableProperty] private double _graphYMax = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogButtonText))]
    private bool _isLogging;

    public string LogButtonText => IsLogging ? "Stop log" : "Start log";
    [ObservableProperty] private string _logStatus = "";

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private long _rxBytes;
    [ObservableProperty] private long _txBytes;

    partial void OnSelectedPortChanged(PortInfo? value)
    {
        if (value != null) _preferredPortName = value.Name;
    }

    partial void OnDtrChanged(bool value) => TrySetLine(() => _connection.SetDtr(value), "DTR");
    partial void OnRtsChanged(bool value) => TrySetLine(() => _connection.SetRts(value), "RTS");
    partial void OnDisplayModeChanged(DisplayMode value) => _assembler.Mode = value;

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnFilterEnabledChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private void ToggleFilter() => FilterEnabled = !FilterEnabled;

    [RelayCommand]
    private void SetDisplayMode(DisplayMode mode) => DisplayMode = mode;

    private void ApplyFilter()
    {
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(Lines);
        var value = FilterText;
        if (!FilterEnabled || string.IsNullOrEmpty(value))
        {
            view.IsLiveFiltering = false;
            view.Filter = null;
            return;
        }
        // live filtering re-checks a line while its text is still growing
        view.LiveFilteringProperties.Clear();
        view.LiveFilteringProperties.Add(nameof(TerminalLine.Text));
        view.IsLiveFiltering = true;
        view.Filter = o => ((TerminalLine)o).Text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

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

        // A pause longer than ~5 characters ends a packet (Modbus RTU needs 3.5).
        _assembler.PacketGap = TimeSpan.FromMilliseconds(Math.Max(20, 55_000.0 / baud));
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
            return; // device may still be initializing, retry on next poll
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

    /// <summary>Called by the main view model after the port list was checked.</summary>
    public void OnPortsPolled(string[] names)
    {
        if (SelectedPort is null) SelectPreferredPort();

        if (_activePort is null) return;
        bool present = names.Contains(_activePort.PortName, StringComparer.OrdinalIgnoreCase);
        if (IsConnected && !present) OnConnectionLost("port removed");
        else if (IsWaitingReconnect && present) TryReconnect();
    }

    private void SelectPreferredPort()
    {
        SelectedPort = _preferredPortName != null
            ? Main.Ports.FirstOrDefault(p => string.Equals(p.Name, _preferredPortName, StringComparison.OrdinalIgnoreCase))
            : Main.Ports.FirstOrDefault(p => !Main.IsPortInUse(p.Name, this)) ?? Main.Ports.FirstOrDefault();
    }

    private void UpdateStatus()
    {
        StatusText = IsConnected && _activePort != null ? $"{_activePort.PortName}   {_activePort.Describe()}"
            : IsWaitingReconnect && _activePort != null ? $"Waiting for {_activePort.PortName}..."
            : "Disconnected";
    }

    #endregion

    #region Data flow

    /// <summary>Called every few milliseconds by the main timer.</summary>
    public void Tick()
    {
        ProcessIncoming();
        if (!IsConnected) return;

        long now = Environment.TickCount64;
        foreach (var line in SendLines)
        {
            if (!line.Repeat || now < line.NextDueMs) continue;
            line.NextDueMs = now + Math.Max(10, line.RepeatMs);
            if (!SendFromLine(line)) line.Repeat = false;
        }
    }

    private void OnSendLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is SendLine { Repeat: true } line && e.PropertyName == nameof(SendLine.Repeat))
            line.NextDueMs = 0; // send right away
    }

    private void ProcessIncoming()
    {
        if (_incoming.IsEmpty) return;
        while (_incoming.TryDequeue(out var item))
        {
            if (item.Kind == LineKind.Rx) RxBytes += item.Data.Length;
            else TxBytes += item.Data.Length;
            _assembler.Append(item.Kind, item.Data, item.Time);
        }
        if (_assembler.OpenLine is { } open) Main.ApplyHighlight(open);
        TrimLines();
        OutputChanged?.Invoke();
    }

    private void OnLineCompleted(TerminalLine line)
    {
        _log?.Write(line);
        Main.ApplyHighlight(line);
        if (ShowGraph && line.Kind == LineKind.Rx && DisplayMode == DisplayMode.Text)
            Graph.AddLine(line.Text, line.Time);
    }

    private void TrimLines()
    {
        // Remove in chunks so we don't do it on every batch.
        int excess = Lines.Count - Main.MaxLines;
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
        if (!SendFromLine(line)) return;
        Main.AddToHistory(line.Text);
        _historyIndex = -1;
    }

    private bool SendFromLine(SendLine line)
    {
        if (string.IsNullOrEmpty(line.Text) && (line.IsHex || line.LineEnding == LineEnding.None)) return false;
        return SendData(line.Text, line.IsHex, line.LineEnding, line.AppendCrc);
    }

    private bool SendData(string text, bool hex, LineEnding lineEnding, bool appendCrc)
    {
        if (!IsConnected)
        {
            AddMessage(LineKind.Error, "Not connected");
            return false;
        }

        byte[] data;
        try
        {
            if (hex)
            {
                data = HexParser.Parse(text);
                if (appendCrc && data.Length > 0) data = ModbusRtu.AppendCrc(data);
            }
            else
            {
                data = Encoding.UTF8.GetBytes(text + LineEndingChars(lineEnding));
            }
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
        if (string.IsNullOrEmpty(macro.Data)) Main.EditMacro(macro);
        else SendData(macro.Data, macro.IsHex, SendLines[0].LineEnding, macro.AppendCrc);
    }

    // Last used values in the Send file window
    public string SendFilePath { get; set; } = "";
    public int SendFileChunk { get; set; } = 1024;
    public int SendFileDelayMs { get; set; }

    [RelayCommand]
    private void SendFile()
    {
        new SendFileWindow(this) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    public async Task SendFileAsync(string path, int chunkSize, int delayMs, IProgress<double> progress, CancellationToken ct)
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected");
        var data = await File.ReadAllBytesAsync(path, ct);
        AddMessage(LineKind.Info, $"Sending {Path.GetFileName(path)} ({data.Length} bytes)...");
        try
        {
            await Task.Run(async () =>
            {
                for (int offset = 0; offset < data.Length; offset += chunkSize)
                {
                    ct.ThrowIfCancellationRequested();
                    int end = Math.Min(offset + chunkSize, data.Length);
                    var chunk = data[offset..end];
                    _connection.Write(chunk);
                    _incoming.Enqueue((LineKind.Tx, chunk, DateTime.Now));
                    progress.Report((double)end / data.Length);
                    if (delayMs > 0) await Task.Delay(delayMs, ct);
                }
            }, ct);
            progress.Report(1);
            AddMessage(LineKind.Info, "File sent");
        }
        catch (OperationCanceledException)
        {
            AddMessage(LineKind.Info, "File sending stopped");
            throw;
        }
        catch (Exception ex)
        {
            AddMessage(LineKind.Error, $"Send file failed: {ex.Message}");
            throw;
        }
    }

    public void HistoryUp(SendLine line)
    {
        var h = Main.History;
        if (h.Count == 0) return;
        _historyIndex = _historyIndex < 0 ? h.Count - 1 : Math.Max(0, _historyIndex - 1);
        line.Text = h[_historyIndex];
    }

    public void HistoryDown(SendLine line)
    {
        var h = Main.History;
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

    public string FormatForCopy(TerminalLine line) => Main.ShowTimestamps ? $"{line.TimeText}  {line.Text}" : line.Text;

    #endregion

    #region View / file commands

    [RelayCommand]
    private void Clear()
    {
        Lines.Clear();
        _assembler.Reset();
    }

    [RelayCommand]
    private void ClearGraph() => Graph.Clear();

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

        var file = Path.Combine(Main.LogFolder, FileBaseName() + ".log");
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

    #endregion

    public void Close()
    {
        _connection.Close();
        _log?.Dispose();
        _log = null;
    }

    public SessionSettings ToSettings() => new()
    {
        PortName = _activePort?.PortName ?? SelectedPort?.Name ?? _preferredPortName ?? "",
        BaudRate = int.TryParse(BaudRateText, out var baud) ? baud : 115200,
        DataBits = DataBits,
        Parity = Parity,
        StopBits = StopBits,
        Handshake = Handshake,
        Dtr = Dtr,
        Rts = Rts,
        AutoReconnect = AutoReconnect,
        DisplayMode = DisplayMode,
        ShowGraph = ShowGraph,
        GraphWindowSeconds = GraphWindowSeconds,
        GraphAutoScale = GraphAutoScale,
        GraphIncludeZero = GraphIncludeZero,
        GraphYMin = GraphYMin,
        GraphYMax = GraphYMax,
        SendFilePath = SendFilePath,
        SendFileChunk = SendFileChunk,
        SendFileDelayMs = SendFileDelayMs,
        SendLines = SendLines.ToList(),
    };
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SerialTerminal.Core;

namespace SerialTerminal;

/// <summary>Application-wide state: tabs, shared port list, theme and global options.</summary>
public partial class MainViewModel : ObservableObject
{
    private const int MacroCount = 8;
    private const int HistoryLimit = 50;

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _portTimer;
    private string[] _knownPortNames = [];
    private bool _refreshingPorts;

    public MainViewModel()
    {
        _settings = AppSettings.Load();

        IsDarkTheme = _settings.DarkTheme ?? ThemeManager.SystemIsDark();
        ThemeManager.Apply(IsDarkTheme);
        FontSize = _settings.FontSize;
        ShowTimestamps = _settings.ShowTimestamps;
        WordWrap = _settings.WordWrap;
        AutoScroll = _settings.AutoScroll;
        LogFolder = _settings.LogFolder;
        ShowConnectionBar = _settings.ShowConnectionBar;
        ShowDisplayBar = _settings.ShowDisplayBar;
        ShowFilterBar = _settings.ShowFilterBar;
        ShowMacroBar = _settings.ShowMacroBar;
        ShowSendBar = _settings.ShowSendBar;
        ShowStatusBar = _settings.ShowStatusBar;

        Macros = new ObservableCollection<Macro>(_settings.Macros.Take(MacroCount));
        while (Macros.Count < MacroCount) Macros.Add(new Macro { Name = $"M{Macros.Count + 1}" });
        HighlightRules = new ObservableCollection<HighlightRule>(_settings.HighlightRules);

        foreach (var s in _settings.Sessions) Sessions.Add(new SessionViewModel(this, s));
        SelectedSession = Sessions[Math.Clamp(_settings.SelectedSession, 0, Sessions.Count - 1)];

        var dispatcher = Dispatcher.CurrentDispatcher;
        _tickTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
        _portTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => PollPorts(), dispatcher);

        _ = RefreshPortsAsync();
    }

    public ObservableCollection<PortInfo> Ports { get; } = new();
    public ObservableCollection<SessionViewModel> Sessions { get; } = new();
    public ObservableCollection<Macro> Macros { get; }
    public ObservableCollection<HighlightRule> HighlightRules { get; }
    public List<string> History => _settings.History;
    public int MaxLines => _settings.MaxLines;

    [ObservableProperty] private SessionViewModel? _selectedSession;
    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private double _fontSize;
    [ObservableProperty] private bool _showTimestamps;
    [ObservableProperty] private bool _wordWrap;
    [ObservableProperty] private bool _autoScroll;
    [ObservableProperty] private string _logFolder = "";

    [ObservableProperty] private bool _showConnectionBar;
    [ObservableProperty] private bool _showDisplayBar;
    [ObservableProperty] private bool _showFilterBar;
    [ObservableProperty] private bool _showMacroBar;
    [ObservableProperty] private bool _showSendBar;
    [ObservableProperty] private bool _showStatusBar;

    public string Version => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    [RelayCommand]
    private void ZoomIn() => FontSize = Math.Min(48, FontSize + 1);

    [RelayCommand]
    private void ZoomOut() => FontSize = Math.Max(8, FontSize - 1);

    [RelayCommand]
    private void ZoomReset() => FontSize = 14;

    partial void OnIsDarkThemeChanged(bool value) => ThemeManager.Apply(value);

    partial void OnSelectedSessionChanged(SessionViewModel? oldValue, SessionViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null) newValue.IsActive = true;
    }

    #region Tabs

    [RelayCommand]
    private void AddSession()
    {
        var session = new SessionViewModel(this, new SessionSettings());
        Sessions.Add(session);
        SelectedSession = session;
    }

    [RelayCommand]
    private void CloseSession(SessionViewModel session)
    {
        if (Sessions.Count <= 1) return;
        int index = Sessions.IndexOf(session);
        session.Close();
        Sessions.Remove(session);
        if (SelectedSession == session || SelectedSession == null)
            SelectedSession = Sessions[Math.Min(index, Sessions.Count - 1)];
    }

    public bool IsPortInUse(string name, SessionViewModel except) =>
        Sessions.Any(s => s != except && s.SelectedPort?.Name == name);

    private void Tick()
    {
        foreach (var session in Sessions) session.Tick();
    }

    #endregion

    #region Ports

    private void PollPorts()
    {
        var names = PortEnumerator.GetNames();
        if (!names.SequenceEqual(_knownPortNames))
        {
            _ = RefreshPortsAsync();
            return;
        }
        foreach (var session in Sessions) session.OnPortsPolled(names);
    }

    [RelayCommand]
    private Task RefreshPorts() => RefreshPortsAsync();

    private async Task RefreshPortsAsync()
    {
        if (_refreshingPorts) return;
        _refreshingPorts = true;
        try
        {
            var ports = await Task.Run(PortEnumerator.GetPorts);
            _knownPortNames = ports.Select(p => p.Name).ToArray();
            SyncPorts(ports);
            foreach (var session in Sessions) session.OnPortsPolled(_knownPortNames);
        }
        finally
        {
            _refreshingPorts = false;
        }
    }

    /// <summary>Updates the shared list in place so the tabs keep their selected port.</summary>
    private void SyncPorts(List<PortInfo> fresh)
    {
        for (int i = Ports.Count - 1; i >= 0; i--)
            if (!fresh.Contains(Ports[i])) Ports.RemoveAt(i);

        for (int i = 0; i < fresh.Count; i++)
        {
            if (i < Ports.Count && Ports[i] == fresh[i]) continue;
            int existing = Ports.IndexOf(fresh[i]);
            if (existing >= 0) Ports.Move(existing, i);
            else Ports.Insert(i, fresh[i]);
        }
    }

    #endregion

    #region Shared features

    public void AddToHistory(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var h = _settings.History;
        h.Remove(text);
        h.Add(text);
        if (h.Count > HistoryLimit) h.RemoveAt(0);
    }

    [RelayCommand]
    public void EditMacro(Macro macro)
    {
        new MacroEditWindow(macro) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    public void ApplyHighlight(TerminalLine line)
    {
        if (line.Kind is not (LineKind.Rx or LineKind.Tx)) return;
        Brush? brush = null;
        foreach (var rule in HighlightRules)
        {
            if (!rule.Matches(line.Text)) continue;
            brush = rule.Brush;
            break;
        }
        line.Highlight = brush;
    }

    [RelayCommand]
    private void EditHighlights()
    {
        new HighlightRulesWindow(HighlightRules) { Owner = Application.Current.MainWindow }.ShowDialog();
        foreach (var session in Sessions)
            foreach (var line in session.Lines)
                ApplyHighlight(line);
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
            MessageBox.Show($"Cannot open folder: {ex.Message}", "Serial Terminal");
        }
    }

    [RelayCommand]
    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser */ }
    }

    [RelayCommand]
    private void ShowAbout()
    {
        MessageBox.Show(Application.Current.MainWindow,
            $"Serial Terminal {Version}\n\n" +
            "Free, open-source serial port terminal for Windows.\n" +
            "https://kod47.github.io/serial-terminal/\n\n" +
            "If it saves you time, you can support development via PayPal:\nkod447@gmail.com",
            "About Serial Terminal", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    #endregion

    public void Shutdown()
    {
        _tickTimer.Stop();
        _portTimer.Stop();
        foreach (var session in Sessions) session.Close();

        _settings.DarkTheme = IsDarkTheme;
        _settings.FontSize = FontSize;
        _settings.ShowTimestamps = ShowTimestamps;
        _settings.WordWrap = WordWrap;
        _settings.AutoScroll = AutoScroll;
        _settings.LogFolder = LogFolder;
        _settings.ShowConnectionBar = ShowConnectionBar;
        _settings.ShowDisplayBar = ShowDisplayBar;
        _settings.ShowFilterBar = ShowFilterBar;
        _settings.ShowMacroBar = ShowMacroBar;
        _settings.ShowSendBar = ShowSendBar;
        _settings.ShowStatusBar = ShowStatusBar;
        _settings.Macros = Macros.ToList();
        _settings.HighlightRules = HighlightRules.ToList();
        _settings.Sessions = Sessions.Select(s => s.ToSettings()).ToList();
        _settings.SelectedSession = SelectedSession != null ? Sessions.IndexOf(SelectedSession) : 0;
        _settings.Save();
    }
}

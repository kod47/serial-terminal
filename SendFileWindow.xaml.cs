using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;

namespace SerialTerminal;

public partial class SendFileWindow : Window
{
    private readonly SessionViewModel _session;
    private CancellationTokenSource? _cts;

    public SendFileWindow(SessionViewModel session)
    {
        InitializeComponent();
        _session = session;
        ChunkBox.ItemsSource = new[] { 16, 64, 256, 1024, 4096 };
        DelayBox.ItemsSource = new[] { 0, 1, 5, 10, 50, 100 };
        PathBox.Text = session.SendFilePath;
        ChunkBox.Text = session.SendFileChunk.ToString();
        DelayBox.Text = session.SendFileDelayMs.ToString();
        UpdateInfo();
    }

    private bool IsSending => _cts != null;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "All files (*.*)|*.*" };
        if (File.Exists(PathBox.Text)) dlg.InitialDirectory = Path.GetDirectoryName(PathBox.Text);
        if (dlg.ShowDialog(this) == true) PathBox.Text = dlg.FileName;
    }

    private void PathBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateInfo();

    private void UpdateInfo()
    {
        var path = PathBox.Text.Trim('"', ' ');
        bool exists = File.Exists(path);
        InfoText.Text = exists ? $"{new FileInfo(path).Length:N0} bytes"
            : path.Length > 0 ? "File not found" : "Choose a file to send";
        SendButton.IsEnabled = exists && !IsSending;
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (IsSending) return;
        if (!_session.IsConnected)
        {
            MessageBox.Show(this, "The port is not connected.", "Send file", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var path = PathBox.Text.Trim('"', ' ');
        int chunk = int.TryParse(ChunkBox.Text, out var c) ? Math.Clamp(c, 1, 65536) : 1024;
        int delay = int.TryParse(DelayBox.Text, out var d) ? Math.Clamp(d, 0, 10_000) : 0;
        _session.SendFilePath = path;
        _session.SendFileChunk = chunk;
        _session.SendFileDelayMs = delay;

        _cts = new CancellationTokenSource();
        SendButton.IsEnabled = false;
        CloseButton.Content = "Stop";
        Progress.Value = 0;
        var progress = new Progress<double>(p =>
        {
            Progress.Value = p;
            ProgressText.Text = $"Sending... {p:P0}";
        });

        try
        {
            await _session.SendFileAsync(path, chunk, delay, progress, _cts.Token);
            ProgressText.Text = "Done";
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Stopped";
        }
        catch (Exception ex)
        {
            ProgressText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            CloseButton.Content = "Close";
            UpdateInfo();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (IsSending) _cts!.Cancel();
        else Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => _cts?.Cancel();
}

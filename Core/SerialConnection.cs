using System.IO.Ports;

namespace SerialTerminal.Core;

public sealed record PortSettings(
    string PortName, int BaudRate, int DataBits, Parity Parity, StopBits StopBits,
    Handshake Handshake, bool Dtr, bool Rts)
{
    public bool RtsControlledByHandshake =>
        Handshake is Handshake.RequestToSend or Handshake.RequestToSendXOnXOff;

    /// <summary>E.g. "115200 8N1".</summary>
    public string Describe()
    {
        var parity = Parity switch
        {
            Parity.Odd => "O", Parity.Even => "E", Parity.Mark => "M", Parity.Space => "S", _ => "N",
        };
        var stop = StopBits switch { StopBits.OnePointFive => "1.5", StopBits.Two => "2", _ => "1" };
        var flow = Handshake == Handshake.None ? "" : $" {Handshake}";
        return $"{BaudRate} {DataBits}{parity}{stop}{flow}";
    }
}

/// <summary>Wraps SerialPort with a background read loop.</summary>
public sealed class SerialConnection : IDisposable
{
    private SerialPort? _port;
    private CancellationTokenSource? _cts;

    /// <summary>Raised on a background thread.</summary>
    public event Action<byte[], DateTime>? DataReceived;

    /// <summary>Raised on a background thread when the port fails (e.g. USB unplugged).</summary>
    public event Action<Exception>? ConnectionLost;

    public bool IsOpen => _port?.IsOpen == true;

    public void Open(PortSettings s)
    {
        Close();
        var port = new SerialPort(s.PortName, s.BaudRate, s.Parity, s.DataBits, s.StopBits)
        {
            Handshake = s.Handshake,
            ReadBufferSize = 65536,
            WriteTimeout = 1000,
        };
        try
        {
            port.Open();
            port.DtrEnable = s.Dtr;
            if (!s.RtsControlledByHandshake) port.RtsEnable = s.Rts;
        }
        catch
        {
            port.Dispose();
            throw;
        }

        _port = port;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => ReadLoop(port, token));
    }

    public void Close()
    {
        _cts?.Cancel();
        _cts = null;
        if (_port == null) return;
        try { _port.Close(); } catch { /* port may already be gone */ }
        _port.Dispose();
        _port = null;
    }

    public void Write(byte[] data)
    {
        if (_port == null) throw new InvalidOperationException("Port is not open");
        _port.Write(data, 0, data.Length);
    }

    public void SetDtr(bool value)
    {
        if (_port != null) _port.DtrEnable = value;
    }

    public void SetRts(bool value)
    {
        if (_port != null) _port.RtsEnable = value;
    }

    private async Task ReadLoop(SerialPort port, CancellationToken token)
    {
        var buffer = new byte[4096];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int n = await port.BaseStream.ReadAsync(buffer, token);
                if (n > 0) DataReceived?.Invoke(buffer[..n], DateTime.Now);
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) ConnectionLost?.Invoke(ex);
        }
    }

    public void Dispose() => Close();
}

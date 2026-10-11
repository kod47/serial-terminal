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

    /// <summary>Raised on a background thread for framing/parity/overrun errors.</summary>
    public event Action<SerialError>? LineError;

    public bool IsOpen => _port?.IsOpen == true;

    public void Open(PortSettings s)
    {
        Close();
        var port = new SerialPort(s.PortName, s.BaudRate, s.Parity, s.DataBits, s.StopBits)
        {
            Handshake = s.Handshake,
            ReadBufferSize = 65536,
            // Must be finite: with an infinite timeout the CH340 driver aborts every read.
            // A read still returns as soon as the first byte arrives.
            ReadTimeout = 100,
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

        port.ErrorReceived += (_, e) => LineError?.Invoke(e.EventType);
        _port = port;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // A dedicated thread that lives as long as the port is open: Windows cancels pending
        // reads when the issuing thread exits, which happened with thread-pool threads.
        new Thread(() => ReadLoop(port, token)) { IsBackground = true, Name = $"{s.PortName} reader" }.Start();
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

    private void ReadLoop(SerialPort port, CancellationToken token)
    {
        const int maxAbortsPerSecond = 50;
        var buffer = new byte[4096];
        long windowStart = Environment.TickCount64;
        int aborts = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                // returns as soon as at least one byte arrives, or throws TimeoutException
                int n = port.Read(buffer, 0, buffer.Length);
                if (n > 0) DataReceived?.Invoke(buffer[..n], DateTime.Now);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return; // closed by us
            }
            catch (TimeoutException)
            {
                // no data within ReadTimeout - normal, read again
            }
            catch (IOException ex)
            {
                // Some drivers abort a pending read (e.g. after a framing/parity/overrun error)
                // while the port is still fine: clear the error state and keep reading.
                // The error code varies between drivers, so only "port gone" or a flood counts as lost.
                long now = Environment.TickCount64;
                if (now - windowStart > 1000)
                {
                    windowStart = now;
                    aborts = 0;
                }
                if (++aborts > maxAbortsPerSecond || !IsPresent(port))
                {
                    ConnectionLost?.Invoke(ex);
                    return;
                }
                try
                {
                    _ = port.BytesToRead; // calls ClearCommError
                }
                catch (Exception clearEx)
                {
                    ConnectionLost?.Invoke(clearEx);
                    return;
                }
                Thread.Sleep(5);
            }
            catch (Exception ex)
            {
                ConnectionLost?.Invoke(ex);
                return;
            }
        }
    }

    private static bool IsPresent(SerialPort port) =>
        port.IsOpen && SerialPort.GetPortNames().Contains(port.PortName, StringComparer.OrdinalIgnoreCase);

    public void Dispose() => Close();
}

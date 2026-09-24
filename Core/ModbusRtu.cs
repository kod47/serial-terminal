namespace SerialTerminal.Core;

/// <summary>Modbus RTU CRC and a human-readable frame description.</summary>
public static class ModbusRtu
{
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    /// <summary>Returns data + CRC (low byte first).</summary>
    public static byte[] AppendCrc(byte[] data)
    {
        var crc = Crc16(data);
        return [.. data, (byte)(crc & 0xFF), (byte)(crc >> 8)];
    }

    /// <summary>E.g. "[Slave 1 · Read Holding Registers · start 0, count 10 · CRC OK]".
    /// TX frames are treated as requests and RX frames as responses when the layout is ambiguous.</summary>
    public static string Describe(byte[] f, LineKind kind)
    {
        if (f.Length < 4) return "[too short for Modbus RTU]";

        ushort expected = Crc16(f.AsSpan(0, f.Length - 2));
        ushort actual = (ushort)(f[^2] | f[^1] << 8);
        string crc = expected == actual ? "CRC OK" : $"CRC ERROR, expected {expected & 0xFF:X2} {expected >> 8:X2}";

        byte[] data = f[2..^2];
        return $"[Slave {f[0]} · {DescribePdu(f[1], data, kind)} · {crc}]";
    }

    private static string DescribePdu(byte fc, byte[] d, LineKind kind)
    {
        if ((fc & 0x80) != 0)
            return $"{FunctionName((byte)(fc & 0x7F))} EXCEPTION: {ExceptionName(d.Length > 0 ? d[0] : (byte)0)}";

        string name = FunctionName(fc);
        ushort U(int i) => (ushort)(d[i] << 8 | d[i + 1]);

        switch (fc)
        {
            case 1: case 2: case 3: case 4:
            {
                bool looksRequest = d.Length == 4;
                bool looksResponse = d.Length >= 1 && d.Length == 1 + d[0];
                if (looksRequest && (!looksResponse || kind == LineKind.Tx))
                    return $"{name} · start {U(0)}, count {U(2)}";
                if (looksResponse)
                    return fc <= 2
                        ? $"{name} · bits: {Bits(d[1..])}"
                        : $"{name} · values: {Registers(d, 1, d[0] / 2)}";
                break;
            }
            case 5:
                if (d.Length == 4)
                    return $"{name} · addr {U(0)} = {(U(2) == 0xFF00 ? "ON" : U(2) == 0 ? "OFF" : $"0x{U(2):X4}")}";
                break;
            case 6:
                if (d.Length == 4) return $"{name} · addr {U(0)} = {U(2)}";
                break;
            case 15: case 16:
                if (d.Length == 4) return $"{name} · start {U(0)}, count {U(2)}";
                if (d.Length >= 5 && d.Length == 5 + d[4])
                    return fc == 15
                        ? $"{name} · start {U(0)}, count {U(2)}, bits: {Bits(d[5..])}"
                        : $"{name} · start {U(0)}, values: {Registers(d, 5, d[4] / 2)}";
                break;
        }
        return d.Length > 0 ? $"{name} · {d.Length} data bytes" : name;
    }

    private static string Registers(byte[] d, int offset, int count)
    {
        const int maxShown = 16;
        var values = Enumerable.Range(0, Math.Min(count, maxShown))
            .Select(i => (d[offset + i * 2] << 8 | d[offset + i * 2 + 1]).ToString());
        return string.Join(", ", values) + (count > maxShown ? $" … (+{count - maxShown})" : "");
    }

    private static string Bits(byte[] bytes) =>
        string.Join(" ", bytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));

    private static string FunctionName(byte fc) => fc switch
    {
        1 => "Read Coils",
        2 => "Read Discrete Inputs",
        3 => "Read Holding Registers",
        4 => "Read Input Registers",
        5 => "Write Single Coil",
        6 => "Write Single Register",
        15 => "Write Multiple Coils",
        16 => "Write Multiple Registers",
        23 => "Read/Write Multiple Registers",
        43 => "Encapsulated Interface",
        _ => $"Function {fc} (0x{fc:X2})",
    };

    private static string ExceptionName(byte code) => code switch
    {
        1 => "Illegal function",
        2 => "Illegal data address",
        3 => "Illegal data value",
        4 => "Server device failure",
        5 => "Acknowledge",
        6 => "Server device busy",
        8 => "Memory parity error",
        10 => "Gateway path unavailable",
        11 => "Gateway target failed to respond",
        _ => $"code {code}",
    };
}

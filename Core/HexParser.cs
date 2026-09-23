using System.Globalization;

namespace SerialTerminal.Core;

public static class HexParser
{
    /// <summary>Accepts "01 03 0A", "01030A", "0x01,0x03", "1 2 3".</summary>
    public static byte[] Parse(string text)
    {
        var result = new List<byte>();
        foreach (var raw in text.Split([' ', ',', ';', '-', ':', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw[2..] : raw;
            if (token.Length == 1) token = "0" + token;
            if (token.Length == 0 || token.Length % 2 != 0) throw new FormatException($"Invalid hex: '{raw}'");

            for (int i = 0; i < token.Length; i += 2)
            {
                if (!byte.TryParse(token.AsSpan(i, 2), NumberStyles.HexNumber, null, out var b))
                    throw new FormatException($"Invalid hex: '{raw}'");
                result.Add(b);
            }
        }
        return result.ToArray();
    }
}

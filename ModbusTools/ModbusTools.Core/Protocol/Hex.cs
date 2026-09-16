using System.Globalization;

namespace ModbusTools.Core.Protocol;

public static class Hex
{
    /// <summary>Formats bytes as space-separated uppercase hex, e.g. "01 03 00 00".</summary>
    public static string Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        return string.Create(bytes.Length * 3 - 1, bytes, static (chars, data) =>
        {
            for (var i = 0; i < data.Length; i++)
            {
                var offset = i * 3;
                if (i > 0)
                {
                    chars[offset - 1] = ' ';
                }

                data[i].TryFormat(chars[offset..], out _, "X2", CultureInfo.InvariantCulture);
            }
        });
    }

    /// <summary>
    /// Parses a non-negative integer entered as decimal ("100") or hex with a 0x prefix ("0x64").
    /// Surrounding whitespace is ignored.
    /// </summary>
    public static bool TryParseNumber(string? text, int maxValue, out int value)
    {
        value = 0;
        var trimmed = text.AsSpan().Trim();
        bool parsed;
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            parsed = int.TryParse(trimmed[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }
        else
        {
            parsed = int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        return parsed && value >= 0 && value <= maxValue;
    }
}

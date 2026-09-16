namespace ModbusTools.Core.Protocol;

/// <summary>CRC-16/MODBUS: reflected polynomial 0xA001, initial value 0xFFFF, transmitted low byte first.</summary>
public static class ModbusCrc
{
    private const ushort Polynomial = 0xA001;
    private const ushort InitialValue = 0xFFFF;

    private static readonly ushort[] Table = BuildTable();

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = InitialValue;
        foreach (var b in data)
        {
            crc = (ushort)((crc >> 8) ^ Table[(crc ^ b) & 0xFF]);
        }

        return crc;
    }

    /// <summary>Writes the CRC of <paramref name="data"/> into <paramref name="destination"/>, low byte first.</summary>
    public static void Write(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        var crc = Compute(data);
        destination[0] = (byte)crc;
        destination[1] = (byte)(crc >> 8);
    }

    /// <summary>Reads the CRC stored in the last two bytes of <paramref name="frame"/>.</summary>
    public static ushort ReadStored(ReadOnlySpan<byte> frame) => (ushort)(frame[^2] | (frame[^1] << 8));

    /// <summary>True when the last two bytes of <paramref name="frame"/> are the CRC of the bytes before them.</summary>
    public static bool IsValid(ReadOnlySpan<byte> frame) =>
        frame.Length >= 3 && Compute(frame[..^2]) == ReadStored(frame);

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (var i = 0; i < table.Length; i++)
        {
            var value = (ushort)i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (ushort)((value >> 1) ^ Polynomial) : (ushort)(value >> 1);
            }

            table[i] = value;
        }

        return table;
    }
}

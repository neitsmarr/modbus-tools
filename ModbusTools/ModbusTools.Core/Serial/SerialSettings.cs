namespace ModbusTools.Core.Serial;

public enum Parity
{
    None,
    Even,
    Odd,
}

public enum StopBits
{
    One = 1,
    Two = 2,
}

/// <summary>Character framing and speed of a serial line.</summary>
public sealed record SerialSettings
{
    public static IReadOnlyList<int> CommonBaudRates { get; } = [1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200];

    public SerialSettings(int baudRate, int dataBits = 8, Parity parity = Parity.None, StopBits stopBits = StopBits.One)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(baudRate, 0);
        if (dataBits is not (7 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(dataBits), dataBits, "Data bits must be 7 or 8.");
        }

        if (!Enum.IsDefined(parity))
        {
            throw new ArgumentOutOfRangeException(nameof(parity), parity, null);
        }

        if (!Enum.IsDefined(stopBits))
        {
            throw new ArgumentOutOfRangeException(nameof(stopBits), stopBits, null);
        }

        BaudRate = baudRate;
        DataBits = dataBits;
        Parity = parity;
        StopBits = stopBits;
    }

    public int BaudRate { get; }

    public int DataBits { get; }

    public Parity Parity { get; }

    public StopBits StopBits { get; }

    /// <summary>Bits on the wire per character: start bit, data bits, optional parity bit, stop bits.</summary>
    public int BitsPerCharacter => 1 + DataBits + (Parity == Parity.None ? 0 : 1) + (int)StopBits;

    /// <summary>Single-letter parity code as used in "8N1" notation.</summary>
    public char ParityCode => Parity switch
    {
        Parity.Even => 'E',
        Parity.Odd => 'O',
        _ => 'N',
    };

    /// <summary>Formats as e.g. "9600 8N1".</summary>
    public override string ToString() => $"{BaudRate} {DataBits}{ParityCode}{(int)StopBits}";
}

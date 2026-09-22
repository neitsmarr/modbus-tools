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

    /// <summary>
    /// The largest mismatch between two clocks that a character in this format survives even in theory, in percent.
    /// The receiver times every bit from the start bit's falling edge and samples it in the middle; the last sample is
    /// the first stop bit's, and it lands in the wrong bit once the mismatch has added up to half a bit by then. Real
    /// receivers find the edge and place their samples less precisely, so they tolerate less.
    /// </summary>
    public double MaxRateMismatchPercent
    {
        get
        {
            // Bit times from the start bit's edge to the middle of the first stop bit; later stop bits are not checked.
            var stopBitSample = 1 + DataBits + (Parity == Parity.None ? 0 : 1) + 0.5;
            return 0.5 / stopBitSample * 100;
        }
    }

    /// <summary>Formats the frame format alone, e.g. "8N1".</summary>
    public string FrameFormat => $"{DataBits}{ParityCode}{(int)StopBits}";

    /// <summary>Single-letter parity code as used in "8N1" notation.</summary>
    public char ParityCode => Parity switch
    {
        Parity.Even => 'E',
        Parity.Odd => 'O',
        _ => 'N',
    };

    /// <summary>Formats as e.g. "9600 8N1".</summary>
    public override string ToString() => $"{BaudRate} {FrameFormat}";
}

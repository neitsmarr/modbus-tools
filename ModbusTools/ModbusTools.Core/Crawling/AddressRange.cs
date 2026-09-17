namespace ModbusTools.Core.Crawling;

/// <summary>An inclusive, non-empty range of data addresses within 0-65535.</summary>
public readonly record struct AddressRange
{
    public const int MaxAddress = ushort.MaxValue;

    public AddressRange(ushort from, ushort to)
    {
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "End address must not be below the start address.");
        }

        From = from;
        To = to;
    }

    public ushort From { get; }

    public ushort To { get; }

    public int Count => To - From + 1;

    public static AddressRange Full { get; } = new(0, ushort.MaxValue);

    public bool Contains(int address) => address >= From && address <= To;

    /// <summary>Validates and creates a range from user input.</summary>
    public static bool TryCreate(int from, int to, out AddressRange range, out string? error)
    {
        range = default;
        error = (from, to) switch
        {
            _ when from is < 0 or > MaxAddress || to is < 0 or > MaxAddress => $"Addresses must be between 0 and {MaxAddress}.",
            _ when from > to => "Start address must not be greater than end address.",
            _ => null,
        };

        if (error is not null)
        {
            return false;
        }

        range = new AddressRange((ushort)from, (ushort)to);
        return true;
    }

    /// <summary>Formats as e.g. "100-149", or "7" for a single address.</summary>
    public override string ToString() => From == To ? $"{From}" : $"{From}-{To}";
}

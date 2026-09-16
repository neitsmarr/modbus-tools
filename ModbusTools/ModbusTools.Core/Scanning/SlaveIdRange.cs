namespace ModbusTools.Core.Scanning;

/// <summary>An inclusive, non-empty range of slave addresses to scan.</summary>
public readonly record struct SlaveIdRange
{
    /// <summary>Lowest unicast address. Address 0 is broadcast, which slaves never answer.</summary>
    public const int FirstId = 1;

    /// <summary>Highest address allowed by the Modbus specification.</summary>
    public const int LastStandardId = 247;

    /// <summary>Highest address when the reserved range 248-255 is included (some devices misuse it).</summary>
    public const int LastExtendedId = 255;

    private SlaveIdRange(byte from, byte to)
    {
        From = from;
        To = to;
    }

    public byte From { get; }

    public byte To { get; }

    public int Count => To - From + 1;

    public static SlaveIdRange Standard { get; } = new(FirstId, LastStandardId);

    /// <summary>
    /// Validates and creates a range. <paramref name="includeReserved"/> must be set to allow addresses 248-255.
    /// </summary>
    public static bool TryCreate(int from, int to, bool includeReserved, out SlaveIdRange range, out string? error)
    {
        range = default;
        var lastAllowed = includeReserved ? LastExtendedId : LastStandardId;
        error = (from, to) switch
        {
            _ when from < FirstId => $"Start ID must be at least {FirstId}; address 0 is broadcast and never answered.",
            _ when from > lastAllowed || to > lastAllowed => includeReserved
                ? $"IDs must not exceed {LastExtendedId}."
                : $"IDs above {LastStandardId} are reserved; enable the reserved range to scan them.",
            _ when from > to => "Start ID must not be greater than end ID.",
            _ => null,
        };

        if (error is not null)
        {
            return false;
        }

        range = new SlaveIdRange((byte)from, (byte)to);
        return true;
    }

    public bool IncludesReserved => To > LastStandardId;

    public IReadOnlyList<byte> ToIdList()
    {
        var ids = new byte[Count];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = (byte)(From + i);
        }

        return ids;
    }

    public override string ToString() => From == To ? $"{From}" : $"{From}-{To}";
}

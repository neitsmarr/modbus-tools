using ModbusTools.Core.Protocol;

namespace ModbusTools.Core.Crawling;

/// <summary>A Modbus data table that can be crawled with a read function.</summary>
public enum CrawlTable
{
    HoldingRegisters,
    InputRegisters,
    Coils,
    DiscreteInputs,
}

public static class CrawlTables
{
    /// <summary>All tables, in the order a crawl visits them.</summary>
    public static IReadOnlyList<CrawlTable> All { get; } =
        [CrawlTable.HoldingRegisters, CrawlTable.InputRegisters, CrawlTable.Coils, CrawlTable.DiscreteInputs];

    public static FunctionCode GetFunctionCode(this CrawlTable table) => table switch
    {
        CrawlTable.HoldingRegisters => FunctionCode.ReadHoldingRegisters,
        CrawlTable.InputRegisters => FunctionCode.ReadInputRegisters,
        CrawlTable.Coils => FunctionCode.ReadCoils,
        CrawlTable.DiscreteInputs => FunctionCode.ReadDiscreteInputs,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    /// <summary>Two-letter abbreviation: HR, IR, CO or DI.</summary>
    public static string GetShortName(this CrawlTable table) => table switch
    {
        CrawlTable.HoldingRegisters => "HR",
        CrawlTable.InputRegisters => "IR",
        CrawlTable.Coils => "CO",
        CrawlTable.DiscreteInputs => "DI",
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    public static string GetDisplayName(this CrawlTable table) => table switch
    {
        CrawlTable.HoldingRegisters => "Holding registers",
        CrawlTable.InputRegisters => "Input registers",
        CrawlTable.Coils => "Coils",
        CrawlTable.DiscreteInputs => "Discrete inputs",
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    /// <summary>True for coils and discrete inputs, whose values are single bits.</summary>
    public static bool IsBitTable(this CrawlTable table) => table is CrawlTable.Coils or CrawlTable.DiscreteInputs;

    /// <summary>Largest quantity the specification allows in one read request.</summary>
    public static ushort GetMaxQuantity(this CrawlTable table) =>
        table.IsBitTable() ? ReadBitsProbe.MaxQuantity : ReadRegistersProbe.MaxQuantity;

    /// <summary>Builds the read request for <paramref name="quantity"/> addresses starting at <paramref name="address"/>.</summary>
    public static ProbeRequest CreateReadProbe(this CrawlTable table, ushort address, ushort quantity) =>
        table.IsBitTable()
            ? new ReadBitsProbe(table.GetFunctionCode(), address, quantity)
            : new ReadRegistersProbe(table.GetFunctionCode(), address, quantity);

    /// <summary>Formats a value as hex: "0x04D2" for a register, "0x1" for a bit.</summary>
    public static string FormatValueHex(this CrawlTable table, ushort value) =>
        table.IsBitTable() ? $"0x{value:X1}" : $"0x{value:X4}";

    /// <summary>
    /// Value number <paramref name="index"/> of a response frame classified OK: the register value, or 0 or 1 for a bit.
    /// </summary>
    public static ushort GetValue(this CrawlTable table, ReadOnlySpan<byte> response, int index) =>
        table.IsBitTable()
            ? (ushort)(ReadBitsProbe.GetBit(response, index) ? 1 : 0)
            : ReadRegistersProbe.GetRegister(response, index);
}

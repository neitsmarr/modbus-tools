namespace ModbusTools.Core.Protocol;

public enum FunctionCode : byte
{
    ReadCoils = 0x01,
    ReadDiscreteInputs = 0x02,
    ReadHoldingRegisters = 0x03,
    ReadInputRegisters = 0x04,
    ReportServerId = 0x11,
    EncapsulatedInterfaceTransport = 0x2B,
}

public static class FunctionCodes
{
    /// <summary>Bit set in the function code of an exception response.</summary>
    public const byte ExceptionFlag = 0x80;

    public static string GetName(FunctionCode functionCode) => functionCode switch
    {
        FunctionCode.ReadCoils => "Read Coils",
        FunctionCode.ReadDiscreteInputs => "Read Discrete Inputs",
        FunctionCode.ReadHoldingRegisters => "Read Holding Registers",
        FunctionCode.ReadInputRegisters => "Read Input Registers",
        FunctionCode.ReportServerId => "Report Server ID",
        FunctionCode.EncapsulatedInterfaceTransport => "Read Device Identification",
        _ => $"Function 0x{(byte)functionCode:X2}",
    };
}

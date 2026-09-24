namespace ModbusTools.Core.Protocol;

public enum FunctionCode : byte
{
    ReadCoils = 0x01,
    ReadDiscreteInputs = 0x02,
    ReadHoldingRegisters = 0x03,
    ReadInputRegisters = 0x04,
    WriteSingleCoil = 0x05,
    WriteSingleRegister = 0x06,
    WriteMultipleCoils = 0x0F,
    WriteMultipleRegisters = 0x10,
    ReportServerId = 0x11,
    MaskWriteRegister = 0x16,
    ReadWriteMultipleRegisters = 0x17,
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
        FunctionCode.WriteSingleCoil => "Write Single Coil",
        FunctionCode.WriteSingleRegister => "Write Single Register",
        FunctionCode.WriteMultipleCoils => "Write Multiple Coils",
        FunctionCode.WriteMultipleRegisters => "Write Multiple Registers",
        FunctionCode.ReportServerId => "Report Server ID",
        FunctionCode.MaskWriteRegister => "Mask Write Register",
        FunctionCode.ReadWriteMultipleRegisters => "Read/Write Multiple Registers",
        FunctionCode.EncapsulatedInterfaceTransport => "Read Device Identification",
        _ => $"Function 0x{(byte)functionCode:X2}",
    };

    /// <summary>
    /// Formats a function code as it appears on the wire, e.g. "FC03 Read Holding Registers", or just "FC65" for one
    /// without a name. The exception flag is not part of the function and is ignored.
    /// </summary>
    public static string GetLabel(byte functionCode)
    {
        var function = (byte)(functionCode & ~ExceptionFlag);
        return Enum.IsDefined((FunctionCode)function)
            ? $"FC{function:D2} {GetName((FunctionCode)function)}"
            : $"FC{function:D2}";
    }
}

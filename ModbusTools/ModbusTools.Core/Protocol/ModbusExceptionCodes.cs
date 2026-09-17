namespace ModbusTools.Core.Protocol;

public static class ModbusExceptionCodes
{
    public const byte IllegalFunction = 0x01;
    public const byte IllegalDataAddress = 0x02;

    public static string GetName(byte code) => code switch
    {
        0x01 => "Illegal Function",
        0x02 => "Illegal Data Address",
        0x03 => "Illegal Data Value",
        0x04 => "Server Device Failure",
        0x05 => "Acknowledge",
        0x06 => "Server Device Busy",
        0x0A => "Gateway Path Unavailable",
        0x0B => "Gateway Target Failed to Respond",
        _ => "Unknown Exception",
    };

    /// <summary>Formats as e.g. "02 Illegal Data Address".</summary>
    public static string Format(byte code) => $"{code:X2} {GetName(code)}";
}

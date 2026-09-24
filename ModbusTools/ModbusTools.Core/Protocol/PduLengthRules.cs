namespace ModbusTools.Core.Protocol;

/// <summary>
/// How the length of a PDU (function code onwards, without address or checksum) follows from its first bytes: either
/// it is fixed, or it is a byte count found at a known position plus the bytes around the data.
/// </summary>
public readonly record struct PduLengthRule
{
    private readonly int length;
    private readonly int byteCountIndex;

    private PduLengthRule(int length, int byteCountIndex)
    {
        this.length = length;
        this.byteCountIndex = byteCountIndex;
    }

    /// <summary>A PDU that always has <paramref name="length"/> bytes.</summary>
    public static PduLengthRule Fixed(int length) => new(length, -1);

    /// <summary>
    /// A PDU carrying a byte count at <paramref name="byteCountIndex"/>, followed by that many data bytes;
    /// <paramref name="lengthWithoutData"/> is its length when the byte count is zero.
    /// </summary>
    public static PduLengthRule ByteCount(int byteCountIndex, int lengthWithoutData) => new(lengthWithoutData, byteCountIndex);

    /// <summary>The PDU length, or null while fewer bytes have arrived than are needed to know it.</summary>
    public int? GetLength(ReadOnlySpan<byte> pdu)
    {
        if (byteCountIndex < 0)
        {
            return length;
        }

        return pdu.Length > byteCountIndex ? length + pdu[byteCountIndex] : null;
    }
}

/// <summary>
/// Request and response lengths of the function codes whose frames can be told apart by content alone. A function
/// missing here is still received, but only silence on the line shows where its frames end.
/// </summary>
public static class PduLengthRules
{
    /// <summary>Function code and exception code.</summary>
    public static PduLengthRule ExceptionResponse { get; } = PduLengthRule.Fixed(2);

    // Offsets and lengths are those of the PDU layouts in the Modbus Application Protocol specification.
    private static readonly Dictionary<FunctionCode, (PduLengthRule Request, PduLengthRule Response)> Rules = new()
    {
        // Request: function, address, quantity. Response: function, byte count, data.
        [FunctionCode.ReadCoils] = (PduLengthRule.Fixed(5), PduLengthRule.ByteCount(1, 2)),
        [FunctionCode.ReadDiscreteInputs] = (PduLengthRule.Fixed(5), PduLengthRule.ByteCount(1, 2)),
        [FunctionCode.ReadHoldingRegisters] = (PduLengthRule.Fixed(5), PduLengthRule.ByteCount(1, 2)),
        [FunctionCode.ReadInputRegisters] = (PduLengthRule.Fixed(5), PduLengthRule.ByteCount(1, 2)),

        // Function, address, value; the response echoes the request.
        [FunctionCode.WriteSingleCoil] = (PduLengthRule.Fixed(5), PduLengthRule.Fixed(5)),
        [FunctionCode.WriteSingleRegister] = (PduLengthRule.Fixed(5), PduLengthRule.Fixed(5)),

        // Request: function, address, quantity, byte count, data. Response: function, address, quantity.
        [FunctionCode.WriteMultipleCoils] = (PduLengthRule.ByteCount(5, 6), PduLengthRule.Fixed(5)),
        [FunctionCode.WriteMultipleRegisters] = (PduLengthRule.ByteCount(5, 6), PduLengthRule.Fixed(5)),

        // Function, address, AND mask, OR mask; the response echoes the request.
        [FunctionCode.MaskWriteRegister] = (PduLengthRule.Fixed(7), PduLengthRule.Fixed(7)),

        // Request: function, read address, read quantity, write address, write quantity, byte count, data.
        // Response: function, byte count, data.
        [FunctionCode.ReadWriteMultipleRegisters] = (PduLengthRule.ByteCount(9, 10), PduLengthRule.ByteCount(1, 2)),
    };

    /// <summary>Gets the rules for a function code without the exception flag; false if it has none.</summary>
    public static bool TryGetRules(byte functionCode, out PduLengthRule request, out PduLengthRule response)
    {
        if (Rules.TryGetValue((FunctionCode)functionCode, out var rules))
        {
            (request, response) = rules;
            return true;
        }

        request = default;
        response = default;
        return false;
    }
}

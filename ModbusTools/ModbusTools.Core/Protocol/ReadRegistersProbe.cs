namespace ModbusTools.Core.Protocol;

/// <summary>FC03 Read Holding Registers or FC04 Read Input Registers.</summary>
public sealed record ReadRegistersProbe : ProbeRequest
{
    public const ushort MaxQuantity = 125;

    public ReadRegistersProbe(FunctionCode functionCode, ushort address, ushort quantity)
    {
        if (functionCode is not (FunctionCode.ReadHoldingRegisters or FunctionCode.ReadInputRegisters))
        {
            throw new ArgumentOutOfRangeException(nameof(functionCode), functionCode, "Expected FC03 or FC04.");
        }

        ValidateRange(address, quantity, MaxQuantity);
        FunctionCode = functionCode;
        Address = address;
        Quantity = quantity;
    }

    public override FunctionCode FunctionCode { get; }

    public ushort Address { get; }

    public ushort Quantity { get; }

    public override string Description => $"{FunctionLabel}, address {Address}, quantity {Quantity}";

    public override string? ValidateResponsePdu(ReadOnlySpan<byte> pdu) => ValidateByteCountPayload(pdu, 2 * Quantity);

    /// <summary>
    /// Reads register <paramref name="index"/> (0 = the request's start address) from a complete response frame that
    /// was classified OK: address, function code, byte count, big-endian register data, CRC.
    /// </summary>
    public static ushort GetRegister(ReadOnlySpan<byte> response, int index) =>
        (ushort)((response[ResponseDataOffset + 2 * index] << 8) | response[ResponseDataOffset + 2 * index + 1]);

    protected override byte[] BuildPdu() =>
        [(byte)FunctionCode, (byte)(Address >> 8), (byte)Address, (byte)(Quantity >> 8), (byte)Quantity];

    // Address + FC + byte count + 2 bytes per register + CRC.
    protected override int? PredictNormalResponseLength(ReadOnlySpan<byte> response) => 5 + 2 * Quantity;
}

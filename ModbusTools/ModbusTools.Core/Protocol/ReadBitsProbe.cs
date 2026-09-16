namespace ModbusTools.Core.Protocol;

/// <summary>FC01 Read Coils or FC02 Read Discrete Inputs.</summary>
public sealed record ReadBitsProbe : ProbeRequest
{
    public const ushort MaxQuantity = 2000;

    public ReadBitsProbe(FunctionCode functionCode, ushort address, ushort quantity)
    {
        if (functionCode is not (FunctionCode.ReadCoils or FunctionCode.ReadDiscreteInputs))
        {
            throw new ArgumentOutOfRangeException(nameof(functionCode), functionCode, "Expected FC01 or FC02.");
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

    private int DataByteCount => (Quantity + 7) / 8;

    public override string? ValidateResponsePdu(ReadOnlySpan<byte> pdu) => ValidateByteCountPayload(pdu, DataByteCount);

    protected override byte[] BuildPdu() =>
        [(byte)FunctionCode, (byte)(Address >> 8), (byte)Address, (byte)(Quantity >> 8), (byte)Quantity];

    // Address + FC + byte count + data + CRC.
    protected override int? PredictNormalResponseLength(ReadOnlySpan<byte> response) => 5 + DataByteCount;
}

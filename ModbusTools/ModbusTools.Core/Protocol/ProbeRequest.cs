namespace ModbusTools.Core.Protocol;

/// <summary>
/// A read-only Modbus request used to check whether a slave answers. Each request type knows how to encode itself
/// and what shape a normal response to it has.
/// </summary>
public abstract record ProbeRequest
{
    /// <summary>Address + function code + exception code + CRC.</summary>
    public const int ExceptionResponseLength = 5;

    private protected ProbeRequest()
    {
    }

    public abstract FunctionCode FunctionCode { get; }

    /// <summary>Short human-readable description of the request, e.g. "FC03 Read Holding Registers, address 0, quantity 1".</summary>
    public abstract string Description { get; }

    /// <summary>Builds the complete RTU frame (address, PDU, CRC) addressed to <paramref name="slaveId"/>.</summary>
    public byte[] BuildFrame(byte slaveId)
    {
        var pdu = BuildPdu();
        var frame = new byte[pdu.Length + 3];
        frame[0] = slaveId;
        pdu.CopyTo(frame.AsSpan(1));
        ModbusCrc.Write(frame.AsSpan(0, frame.Length - 2), frame.AsSpan(frame.Length - 2));
        return frame;
    }

    /// <summary>
    /// Predicts the total length (address through CRC) of the response from the bytes received so far.
    /// Returns null when the length cannot be determined yet, or at all (e.g. an unexpected function code).
    /// The prediction is only a hint to finish reading early; it does not imply the response is valid.
    /// </summary>
    public int? PredictResponseLength(ReadOnlySpan<byte> response)
    {
        if (response.Length < 2)
        {
            return null;
        }

        var functionCode = response[1];
        if (functionCode == ((byte)FunctionCode | FunctionCodes.ExceptionFlag))
        {
            return ExceptionResponseLength;
        }

        return functionCode == (byte)FunctionCode ? PredictNormalResponseLength(response) : null;
    }

    /// <summary>
    /// Checks the structure of a normal response PDU (function code onwards, without CRC) whose function code
    /// already matches. Returns null if it is plausible, otherwise a description of the problem.
    /// </summary>
    public abstract string? ValidateResponsePdu(ReadOnlySpan<byte> pdu);

    protected abstract byte[] BuildPdu();

    /// <inheritdoc cref="PredictResponseLength"/>
    protected abstract int? PredictNormalResponseLength(ReadOnlySpan<byte> response);

    private protected string FunctionLabel => $"FC{(byte)FunctionCode:D2} {FunctionCodes.GetName(FunctionCode)}";

    private protected static void ValidateRange(ushort address, ushort quantity, ushort maxQuantity)
    {
        if (quantity == 0 || quantity > maxQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, $"Quantity must be between 1 and {maxQuantity}.");
        }

        if (address + quantity > 0x10000)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Address + quantity must not exceed 65536.");
        }
    }

    /// <summary>Validates the "byte count + data" layout shared by FC01-04 and FC17 responses.</summary>
    private protected static string? ValidateByteCountPayload(ReadOnlySpan<byte> pdu, int? expectedByteCount)
    {
        if (pdu.Length < 2)
        {
            return "Response has no byte count.";
        }

        var byteCount = pdu[1];
        if (expectedByteCount is int expected && byteCount != expected)
        {
            return $"Byte count {byteCount}, expected {expected}.";
        }

        var payloadLength = pdu.Length - 2;
        return payloadLength == byteCount
            ? null
            : $"Byte count {byteCount} but {payloadLength} data byte(s) received.";
    }
}

namespace ModbusTools.Core.Protocol;

/// <summary>FC17 (0x11) Report Server ID. Takes no parameters; the response content is device specific.</summary>
public sealed record ReportServerIdProbe : ProbeRequest
{
    public override FunctionCode FunctionCode => FunctionCode.ReportServerId;

    public override string Description => FunctionLabel;

    public override string? ValidateResponsePdu(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length >= 2 && pdu[1] == 0)
        {
            return "Byte count 0, expected at least the server ID.";
        }

        return ValidateByteCountPayload(pdu, expectedByteCount: null);
    }

    protected override byte[] BuildPdu() => [(byte)FunctionCode];

    // Address + FC + byte count + data + CRC; the length is known once the byte count has arrived.
    protected override int? PredictNormalResponseLength(ReadOnlySpan<byte> response) =>
        response.Length >= 3 ? 5 + response[2] : null;
}

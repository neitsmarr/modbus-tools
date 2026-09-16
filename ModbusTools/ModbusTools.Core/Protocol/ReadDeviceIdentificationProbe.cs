namespace ModbusTools.Core.Protocol;

public enum ReadDeviceIdCode : byte
{
    Basic = 0x01,
    Regular = 0x02,
    Extended = 0x03,
    Specific = 0x04,
}

/// <summary>FC43 (0x2B) / MEI 14 (0x0E) Read Device Identification.</summary>
public sealed record ReadDeviceIdentificationProbe : ProbeRequest
{
    public const byte MeiType = 0x0E;

    // FC, MEI type, read device ID code, conformity level, more follows, next object ID, number of objects.
    private const int PduHeaderLength = 7;

    public ReadDeviceIdentificationProbe(ReadDeviceIdCode readDeviceIdCode = ReadDeviceIdCode.Basic, byte objectId = 0)
    {
        if (!Enum.IsDefined(readDeviceIdCode))
        {
            throw new ArgumentOutOfRangeException(nameof(readDeviceIdCode), readDeviceIdCode, null);
        }

        ReadDeviceIdCode = readDeviceIdCode;
        ObjectId = objectId;
    }

    public override FunctionCode FunctionCode => FunctionCode.EncapsulatedInterfaceTransport;

    public ReadDeviceIdCode ReadDeviceIdCode { get; }

    public byte ObjectId { get; }

    public override string Description =>
        $"FC43/14 Read Device Identification, code {(byte)ReadDeviceIdCode:X2} ({ReadDeviceIdCode}), object ID {ObjectId}";

    public override string? ValidateResponsePdu(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length < PduHeaderLength)
        {
            return $"Response PDU has {pdu.Length} byte(s), expected at least {PduHeaderLength}.";
        }

        if (pdu[1] != MeiType)
        {
            return $"MEI type 0x{pdu[1]:X2}, expected 0x{MeiType:X2}.";
        }

        if (pdu[2] != (byte)ReadDeviceIdCode)
        {
            return $"Read device ID code {pdu[2]:X2}, expected {(byte)ReadDeviceIdCode:X2}.";
        }

        var objectsEnd = FindObjectsEnd(pdu, PduHeaderLength);
        if (objectsEnd is null)
        {
            return $"Object list declares {pdu[6]} object(s) but the response ends early.";
        }

        return objectsEnd == pdu.Length
            ? null
            : $"{pdu.Length - objectsEnd} unexpected byte(s) after the object list.";
    }

    protected override byte[] BuildPdu() => [(byte)FunctionCode, MeiType, (byte)ReadDeviceIdCode, ObjectId];

    protected override int? PredictNormalResponseLength(ReadOnlySpan<byte> response)
    {
        // The object list starts after the address byte and the PDU header.
        var headerEnd = 1 + PduHeaderLength;
        if (response.Length < headerEnd || response[2] != MeiType)
        {
            return null;
        }

        return FindObjectsEnd(response, headerEnd) + 2;
    }

    /// <summary>
    /// Walks the object list (id, length, value) whose count is the byte just before <paramref name="listStart"/>.
    /// Returns the offset just past the last object, or null if <paramref name="buffer"/> ends before it.
    /// </summary>
    private static int? FindObjectsEnd(ReadOnlySpan<byte> buffer, int listStart)
    {
        var objectCount = buffer[listStart - 1];
        var position = listStart;
        for (var i = 0; i < objectCount; i++)
        {
            if (buffer.Length < position + 2)
            {
                return null;
            }

            position += 2 + buffer[position + 1];
        }

        return position <= buffer.Length ? position : null;
    }
}

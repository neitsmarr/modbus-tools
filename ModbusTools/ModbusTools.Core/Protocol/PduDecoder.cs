using System.Buffers.Binary;

namespace ModbusTools.Core.Protocol;

/// <summary>
/// Describes observed PDUs (function code onwards, without address or checksum) in a few words, and checks whether a
/// response fits the request it seems to answer. Only functions listed in <see cref="PduLengthRules"/> are decoded.
/// </summary>
public static class PduDecoder
{
    private const ushort CoilOn = 0xFF00;
    private const ushort CoilOff = 0x0000;

    /// <summary>Short description of a request, e.g. "address 100, quantity 10"; null when it is not decoded.</summary>
    public static string? DescribeRequest(ReadOnlySpan<byte> pdu)
    {
        if (!IsComplete(pdu, isRequest: true))
        {
            return null;
        }

        return (FunctionCode)pdu[0] switch
        {
            FunctionCode.ReadCoils or FunctionCode.ReadDiscreteInputs or FunctionCode.ReadHoldingRegisters
                or FunctionCode.ReadInputRegisters or FunctionCode.WriteMultipleCoils
                or FunctionCode.WriteMultipleRegisters => AddressAndQuantity(pdu),
            FunctionCode.WriteSingleCoil => SingleCoil(pdu),
            FunctionCode.WriteSingleRegister => SingleRegister(pdu),
            FunctionCode.MaskWriteRegister => MaskWrite(pdu),
            FunctionCode.ReadWriteMultipleRegisters =>
                $"read {Word(pdu, 1)}, quantity {Word(pdu, 3)}; write {Word(pdu, 5)}, quantity {Word(pdu, 7)}",
            _ => null,
        };
    }

    /// <summary>Short description of a normal or exception response, e.g. "10 registers"; null when it is not decoded.</summary>
    public static string? DescribeResponse(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length == 2 && (pdu[0] & FunctionCodes.ExceptionFlag) != 0)
        {
            return $"Exception {ModbusExceptionCodes.Format(pdu[1])}";
        }

        if (!IsComplete(pdu, isRequest: false))
        {
            return null;
        }

        return (FunctionCode)pdu[0] switch
        {
            FunctionCode.ReadCoils => $"{Plural(pdu[1], "byte")} of coil status",
            FunctionCode.ReadDiscreteInputs => $"{Plural(pdu[1], "byte")} of input status",
            FunctionCode.ReadHoldingRegisters or FunctionCode.ReadInputRegisters
                or FunctionCode.ReadWriteMultipleRegisters => Registers(pdu[1]),
            FunctionCode.WriteSingleCoil => SingleCoil(pdu),
            FunctionCode.WriteSingleRegister => SingleRegister(pdu),
            FunctionCode.WriteMultipleCoils or FunctionCode.WriteMultipleRegisters => AddressAndQuantity(pdu),
            FunctionCode.MaskWriteRegister => MaskWrite(pdu),
            _ => null,
        };
    }

    /// <summary>
    /// Whether <paramref name="response"/> can be the normal response to <paramref name="request"/>, both with the
    /// same function code: the data it returns matches what was asked for, or it echoes what a write sent. True when
    /// the function is not decoded, since nothing then speaks against it.
    /// </summary>
    public static bool IsConsistentResponse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
    {
        if (request.IsEmpty || response.IsEmpty || request[0] != response[0])
        {
            return false;
        }

        if (!IsComplete(request, isRequest: true) || !IsComplete(response, isRequest: false))
        {
            return !PduLengthRules.TryGetRules(request[0], out _, out _);
        }

        return (FunctionCode)request[0] switch
        {
            FunctionCode.ReadCoils or FunctionCode.ReadDiscreteInputs => response[1] == (Word(request, 3) + 7) / 8,
            // For read/write multiple, the read quantity sits where the other reads keep theirs.
            FunctionCode.ReadHoldingRegisters or FunctionCode.ReadInputRegisters
                or FunctionCode.ReadWriteMultipleRegisters => response[1] == 2 * Word(request, 3),
            FunctionCode.WriteSingleCoil or FunctionCode.WriteSingleRegister or FunctionCode.MaskWriteRegister =>
                response.SequenceEqual(request),
            FunctionCode.WriteMultipleCoils or FunctionCode.WriteMultipleRegisters =>
                response[1..5].SequenceEqual(request[1..5]),
            _ => true,
        };
    }

    /// <summary>True when the PDU has a length rule and exactly the length it prescribes.</summary>
    private static bool IsComplete(ReadOnlySpan<byte> pdu, bool isRequest)
    {
        if (pdu.IsEmpty || !PduLengthRules.TryGetRules(pdu[0], out var request, out var response))
        {
            return false;
        }

        return (isRequest ? request : response).GetLength(pdu) == pdu.Length;
    }

    private static string AddressAndQuantity(ReadOnlySpan<byte> pdu) => $"address {Word(pdu, 1)}, quantity {Word(pdu, 3)}";

    private static string SingleCoil(ReadOnlySpan<byte> pdu)
    {
        var value = Word(pdu, 3);
        var state = value switch
        {
            CoilOn => "ON",
            CoilOff => "OFF",
            _ => $"0x{value:X4} (neither ON nor OFF)",
        };
        return $"coil {Word(pdu, 1)} = {state}";
    }

    private static string SingleRegister(ReadOnlySpan<byte> pdu)
    {
        var value = Word(pdu, 3);
        return $"register {Word(pdu, 1)} = {value} (0x{value:X4})";
    }

    private static string MaskWrite(ReadOnlySpan<byte> pdu) =>
        $"register {Word(pdu, 1)}, AND 0x{Word(pdu, 3):X4}, OR 0x{Word(pdu, 5):X4}";

    private static string Registers(byte byteCount) =>
        byteCount % 2 == 0 ? Plural(byteCount / 2, "register") : $"{Plural(byteCount, "byte")}, not whole registers";

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static ushort Word(ReadOnlySpan<byte> pdu, int offset) => BinaryPrimitives.ReadUInt16BigEndian(pdu[offset..]);
}

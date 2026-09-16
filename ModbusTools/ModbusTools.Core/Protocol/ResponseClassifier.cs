namespace ModbusTools.Core.Protocol;

/// <summary>Outcome of a single probe, ordered from worst to best so that a higher value is a better result.</summary>
public enum ProbeStatus
{
    NoResponse = 0,
    Garbled = 1,
    WrongId = 2,
    Exception = 3,
    Ok = 4,
}

public enum GarbledReason
{
    /// <summary>The CRC does not match and the length gives no better explanation.</summary>
    CrcMismatch,

    /// <summary>Fewer bytes than the shortest possible response.</summary>
    TooShort,

    /// <summary>The CRC fails and fewer bytes arrived than the response header announces.</summary>
    Truncated,

    /// <summary>A frame with a valid CRC was followed by extra bytes.</summary>
    TooLong,

    /// <summary>The CRC is valid but the byte count or payload length is inconsistent.</summary>
    LengthMismatch,

    /// <summary>The CRC and address are valid but the function code is neither the request's nor its exception.</summary>
    UnexpectedFunction,
}

public static class ProbeStatusExtensions
{
    public static bool IsFound(this ProbeStatus status) => status is ProbeStatus.Ok or ProbeStatus.Exception;

    public static string GetDisplayName(this ProbeStatus status) => status switch
    {
        ProbeStatus.Ok => "OK",
        ProbeStatus.Exception => "Exception",
        ProbeStatus.WrongId => "Wrong ID",
        ProbeStatus.Garbled => "Garbled",
        _ => "No response",
    };
}

public readonly record struct ResponseClassification(
    ProbeStatus Status,
    GarbledReason? GarbledReason = null,
    byte? ExceptionCode = null,
    byte? ReplyAddress = null,
    string? Detail = null);

public static class ResponseClassifier
{
    /// <summary>The shortest valid response to any probe: an exception response.</summary>
    public const int MinimumResponseLength = ProbeRequest.ExceptionResponseLength;

    /// <summary>Classifies the bytes received in answer to <paramref name="probe"/> sent to <paramref name="slaveId"/>.</summary>
    public static ResponseClassification Classify(ProbeRequest probe, byte slaveId, ReadOnlySpan<byte> response)
    {
        ArgumentNullException.ThrowIfNull(probe);

        if (response.IsEmpty)
        {
            return new ResponseClassification(ProbeStatus.NoResponse);
        }

        if (response.Length < MinimumResponseLength)
        {
            return Garbled(GarbledReason.TooShort,
                $"{response.Length} byte(s) received, a response has at least {MinimumResponseLength}.");
        }

        if (!ModbusCrc.IsValid(response))
        {
            return ClassifyCrcFailure(probe, response);
        }

        var address = response[0];
        if (address != slaveId)
        {
            return new ResponseClassification(ProbeStatus.WrongId, ReplyAddress: address,
                Detail: $"Reply from address {address}.");
        }

        var functionCode = response[1];
        var requestFunctionCode = (byte)probe.FunctionCode;
        if (functionCode == (requestFunctionCode | FunctionCodes.ExceptionFlag))
        {
            if (response.Length != ProbeRequest.ExceptionResponseLength)
            {
                return Garbled(GarbledReason.LengthMismatch,
                    $"Exception response of {response.Length} bytes, expected {ProbeRequest.ExceptionResponseLength}.");
            }

            var code = response[2];
            return new ResponseClassification(ProbeStatus.Exception, ExceptionCode: code,
                Detail: $"Exception {ModbusExceptionCodes.Format(code)}.");
        }

        if (functionCode != requestFunctionCode)
        {
            return Garbled(GarbledReason.UnexpectedFunction,
                $"Function code 0x{functionCode:X2}, expected 0x{requestFunctionCode:X2}.");
        }

        var problem = probe.ValidateResponsePdu(response[1..^2]);
        return problem is null
            ? new ResponseClassification(ProbeStatus.Ok)
            : Garbled(GarbledReason.LengthMismatch, problem);
    }

    private static ResponseClassification ClassifyCrcFailure(ProbeRequest probe, ReadOnlySpan<byte> response)
    {
        if (probe.PredictResponseLength(response) is int expected)
        {
            if (response.Length < expected)
            {
                return Garbled(GarbledReason.Truncated, $"{response.Length} of {expected} expected bytes received.");
            }

            if (response.Length > expected && ModbusCrc.IsValid(response[..expected]))
            {
                return Garbled(GarbledReason.TooLong,
                    $"Valid {expected}-byte frame followed by {response.Length - expected} extra byte(s).");
            }
        }

        return Garbled(GarbledReason.CrcMismatch,
            $"CRC {ModbusCrc.ReadStored(response):X4} received, {ModbusCrc.Compute(response[..^2]):X4} computed.");
    }

    private static ResponseClassification Garbled(GarbledReason reason, string detail) =>
        new(ProbeStatus.Garbled, GarbledReason: reason, Detail: detail);
}

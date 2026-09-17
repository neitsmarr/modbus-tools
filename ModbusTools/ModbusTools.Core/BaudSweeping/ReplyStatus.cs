using ModbusTools.Core.Protocol;
using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>What one request at a tested baud rate achieved, ordered from worst to best.</summary>
public enum ReplyStatus
{
    /// <summary>Nothing arrived: the device did not recognise the request, so it could not read us.</summary>
    NoReply = 0,

    /// <summary>
    /// Bytes arrived but did not form a valid reply, or the port reported line errors: the device understood the
    /// request and answered, and the answer could not be read back.
    /// </summary>
    BadReply = 1,

    /// <summary>A well-formed reply, normal or an exception: both directions of the link worked.</summary>
    Answered = 2,
}

public static class ReplyStatusExtensions
{
    /// <summary>
    /// Classifies a transaction for the sweep. An exception reply counts as answered: it carries a valid CRC from the
    /// right address, which proves the device decoded the request and the host decoded the answer - all this tool
    /// measures. Nothing at all means the request did not get through; anything else means the reply did not.
    /// </summary>
    public static ReplyStatus GetReplyStatus(this ProbeAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return attempt switch
        {
            _ when attempt.Status.IsFound() => ReplyStatus.Answered,
            { Status: ProbeStatus.NoResponse, LineErrors: 0 } => ReplyStatus.NoReply,
            _ => ReplyStatus.BadReply,
        };
    }

    public static string GetDisplayName(this ReplyStatus status) => status switch
    {
        ReplyStatus.Answered => "Answered",
        ReplyStatus.BadReply => "Bad reply",
        _ => "No reply",
    };

    /// <summary>What a failure of this kind says about the link, for tooltips and legends.</summary>
    public static string GetExplanation(this ReplyStatus status) => status switch
    {
        ReplyStatus.Answered => "Valid reply (normal or exception): both directions worked.",
        ReplyStatus.BadReply => "The device answered, but the reply could not be decoded at this rate.",
        _ => "Nothing arrived: the device could not decode the request at this rate.",
    };
}

using ModbusTools.Core.Protocol;

namespace ModbusTools.Core.Sniffing;

/// <summary>
/// Decides which frames are requests and which are responses, and pairs each response with the request it answers.
/// </summary>
/// <remarks>
/// On a Modbus RTU bus one master asks and at most one slave answers before the next question, so at most one request
/// is waiting at any time. A frame answers it when it comes from the addressed slave, carries the same function and
/// fits what was asked (<see cref="PduDecoder.IsConsistentResponse"/>). That also settles frames whose content fits
/// both directions, such as a read request whose address happens to look like a response's byte count.
/// </remarks>
public sealed class TransactionMatcher(TimeSpan responseTimeout, SniffStatistics statistics)
{
    private SniffEntry? waiting;
    private bool damagedReplySeen;

    /// <summary>Requests settled so far, answered or not; it changes whenever an outcome is decided.</summary>
    public long Settled { get; private set; }

    /// <summary>Turns a frame with a valid CRC into a log entry, deciding its direction and pairing it.</summary>
    public SniffEntry Accept(SplitFrame frame, long number)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Expire(frame.First.Time);

        var bytes = frame.Bytes;
        var address = bytes[0];
        var functionCode = bytes[1];
        var pdu = bytes.AsSpan(1..^2);

        SniffEntry entry;
        if ((functionCode & FunctionCodes.ExceptionFlag) != 0)
        {
            entry = new SniffEntry(number, SniffEntryKind.Exception, bytes, frame.First, frame.Last, PduDecoder.DescribeResponse(pdu));
            TryPair(entry, isException: true);
        }
        else if (frame.Roles.HasFlag(FrameRoles.Response) && Answers(address, pdu))
        {
            entry = new SniffEntry(number, SniffEntryKind.Response, bytes, frame.First, frame.Last, PduDecoder.DescribeResponse(pdu));
            TryPair(entry, isException: false);
        }
        else if (frame.Roles.HasFlag(FrameRoles.Request))
        {
            // A new request means the master has stopped waiting for the previous one.
            Close();
            var kind = address == 0 ? SniffEntryKind.Broadcast : SniffEntryKind.Request;
            entry = new SniffEntry(number, kind, bytes, frame.First, frame.Last, PduDecoder.DescribeRequest(pdu));
            if (kind == SniffEntryKind.Request)
            {
                waiting = entry;
                damagedReplySeen = false;
            }
        }
        else
        {
            entry = new SniffEntry(number, SniffEntryKind.Response, bytes, frame.First, frame.Last, PduDecoder.DescribeResponse(pdu));
        }

        statistics.RecordEntry(entry);
        return entry;
    }

    /// <summary>
    /// Garbled bytes or a line error were received at <paramref name="time"/>. While a request is waiting they are
    /// most likely its reply, damaged on the way; a valid reply can still follow, since a bus turnaround often leaves a
    /// stray byte.
    /// </summary>
    public void NoteDamage(TimeSpan time)
    {
        Expire(time);
        if (waiting is not null)
        {
            damagedReplySeen = true;
        }
    }

    /// <summary>Gives up on the waiting request once the response timeout has passed by <paramref name="now"/>.</summary>
    public void Expire(TimeSpan now)
    {
        if (waiting is not null && now - waiting.Last.Time > responseTimeout)
        {
            Close();
        }
    }

    /// <summary>The capture has stopped at <paramref name="now"/>: settles the waiting request as far as possible.</summary>
    public void End(TimeSpan now)
    {
        Expire(now);
        if (waiting is not null)
        {
            waiting.Outcome = RequestOutcome.CaptureEnded;
            waiting = null;
            Settled++;
        }
    }

    /// <summary>Whether a frame with this address and PDU answers the waiting request.</summary>
    private bool Answers(byte address, ReadOnlySpan<byte> pdu) =>
        waiting is not null && waiting.Address == address && PduDecoder.IsConsistentResponse(waiting.Pdu, pdu);

    private void TryPair(SniffEntry response, bool isException)
    {
        if (waiting is null || waiting.Address != response.Address || waiting.FunctionCode != response.FunctionCode)
        {
            return;
        }

        response.Request = waiting;
        waiting.Reply = response;
        waiting.Outcome = isException ? RequestOutcome.Exception : RequestOutcome.Answered;
        statistics.RecordOutcome(waiting);
        waiting = null;
        Settled++;
    }

    /// <summary>Settles the waiting request without a reply.</summary>
    private void Close()
    {
        if (waiting is null)
        {
            return;
        }

        waiting.Outcome = damagedReplySeen ? RequestOutcome.GarbledReply : RequestOutcome.NoResponse;
        statistics.RecordOutcome(waiting);
        waiting = null;
        Settled++;
    }
}

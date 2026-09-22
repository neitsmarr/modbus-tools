using ModbusTools.Core.Serial;

namespace ModbusTools.Core.Sniffing;

/// <summary>
/// Counters for the whole capture. They keep counting when the oldest log entries are dropped, so they always cover
/// everything since the capture started.
/// </summary>
public sealed class SniffStatistics
{
    private readonly SortedDictionary<byte, SlaveStatistics> slaves = new();

    /// <summary>Every byte received, whether it was part of a frame or not.</summary>
    public long Bytes { get; private set; }

    /// <summary>Frames with a valid CRC.</summary>
    public int Frames { get; private set; }

    public int Requests { get; private set; }

    public int Broadcasts { get; private set; }

    /// <summary>Normal and exception responses.</summary>
    public int Responses { get; private set; }

    /// <summary>Runs of garbled bytes, each one entry in the log.</summary>
    public int GarbledRuns { get; private set; }

    public long GarbledBytes { get; private set; }

    public int LineErrors { get; private set; }

    /// <summary>Slaves that were addressed or answered, by address.</summary>
    public IReadOnlyCollection<SlaveStatistics> Slaves => slaves.Values;

    /// <summary>
    /// Share of <paramref name="elapsed"/> the line spent carrying the received bytes, each taking one character
    /// time; 0 before any time has passed.
    /// </summary>
    public double BusLoad(SerialSettings serial, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(serial);
        return elapsed > TimeSpan.Zero ? RtuTiming.CharacterTime(serial) * Bytes / elapsed : 0;
    }

    internal void RecordBytes(int count) => Bytes += count;

    internal void RecordEntry(SniffEntry entry)
    {
        switch (entry.Kind)
        {
            case SniffEntryKind.Request:
                Frames++;
                Requests++;
                Slave(entry.Address!.Value).Requests++;
                break;
            case SniffEntryKind.Broadcast:
                Frames++;
                Broadcasts++;
                break;
            case SniffEntryKind.Response or SniffEntryKind.Exception:
                Frames++;
                Responses++;
                if (entry.IsUnexpected)
                {
                    Slave(entry.Address!.Value).Unexpected++;
                }

                break;
            case SniffEntryKind.Garbled:
                GarbledRuns++;
                GarbledBytes += entry.Bytes.Length;
                break;
            case SniffEntryKind.LineError:
                LineErrors++;
                break;
        }
    }

    /// <summary>Counts the verdict on a request once it is final.</summary>
    internal void RecordOutcome(SniffEntry request)
    {
        var slave = Slave(request.Address!.Value);
        switch (request.Outcome)
        {
            case RequestOutcome.Answered:
                slave.Answered++;
                slave.RecordResponseTime(request.Reply!);
                break;
            case RequestOutcome.Exception:
                slave.Exceptions++;
                slave.RecordResponseTime(request.Reply!);
                break;
            case RequestOutcome.NoResponse:
                slave.NoResponse++;
                break;
            case RequestOutcome.GarbledReply:
                slave.GarbledReplies++;
                break;
        }
    }

    private SlaveStatistics Slave(byte address)
    {
        if (!slaves.TryGetValue(address, out var slave))
        {
            slave = new SlaveStatistics(address);
            slaves.Add(address, slave);
        }

        return slave;
    }
}

/// <summary>Counters for one slave address.</summary>
public sealed class SlaveStatistics(byte address)
{
    private TimeSpan totalResponseTime;

    public byte Address { get; } = address;

    /// <summary>Requests addressed to this slave.</summary>
    public int Requests { get; internal set; }

    public int Answered { get; internal set; }

    public int Exceptions { get; internal set; }

    public int NoResponse { get; internal set; }

    /// <summary>Requests that only got garbled bytes or line errors back.</summary>
    public int GarbledReplies { get; internal set; }

    /// <summary>Responses from this address that no request was waiting for.</summary>
    public int Unexpected { get; internal set; }

    /// <summary>Replies whose response time could be measured.</summary>
    public int TimedReplies { get; private set; }

    /// <summary>Replies that arrived in the same chunk as the end of their request, so their time is not known.</summary>
    public int RepliesInRequestChunk { get; private set; }

    public TimeSpan? AverageResponseTime => TimedReplies > 0 ? totalResponseTime / TimedReplies : null;

    public TimeSpan? MaxResponseTime { get; private set; }

    internal void RecordResponseTime(SniffEntry reply)
    {
        if (reply.ResponseTime is not TimeSpan time)
        {
            RepliesInRequestChunk++;
            return;
        }

        TimedReplies++;
        totalResponseTime += time;
        if (MaxResponseTime is not TimeSpan max || time > max)
        {
            MaxResponseTime = time;
        }
    }
}

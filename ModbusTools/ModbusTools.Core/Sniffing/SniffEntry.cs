using ModbusTools.Core.Protocol;

namespace ModbusTools.Core.Sniffing;

public enum SniffEntryKind
{
    /// <summary>A request to one slave, which should answer it.</summary>
    Request,

    /// <summary>A request to address 0, which every slave carries out and none answers.</summary>
    Broadcast,

    /// <summary>A normal response.</summary>
    Response,

    /// <summary>An exception response.</summary>
    Exception,

    /// <summary>Bytes that are not part of any valid frame.</summary>
    Garbled,

    /// <summary>The port reported a framing, parity, break or overrun error.</summary>
    LineError,
}

/// <summary>What became of a request to one slave.</summary>
public enum RequestOutcome
{
    /// <summary>No verdict yet: the response timeout has not passed and no other request has started.</summary>
    Waiting,

    /// <summary>A normal response that fits the request followed.</summary>
    Answered,

    /// <summary>An exception response followed.</summary>
    Exception,

    /// <summary>Nothing came back before the timeout or before the next request.</summary>
    NoResponse,

    /// <summary>
    /// Only garbled bytes or line errors came back before the timeout or the next request: most likely the reply,
    /// damaged on the way.
    /// </summary>
    GarbledReply,

    /// <summary>The capture stopped before the timeout passed.</summary>
    CaptureEnded,
}

/// <summary>One line of the sniffer's log: a decoded frame, a run of garbled bytes or a line error.</summary>
public sealed class SniffEntry
{
    internal SniffEntry(long number, SniffEntryKind kind, byte[] bytes, ChunkStamp first, ChunkStamp last, string? summary)
    {
        Number = number;
        Kind = kind;
        Bytes = bytes;
        First = first;
        Last = last;
        Summary = summary;
        if (kind == SniffEntryKind.Request)
        {
            Outcome = RequestOutcome.Waiting;
        }
    }

    /// <summary>Position in the capture, counting from 1.</summary>
    public long Number { get; }

    public SniffEntryKind Kind { get; }

    /// <summary>The frame from address to CRC, or the garbled bytes; empty for a line error.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>Chunk that brought the first byte.</summary>
    public ChunkStamp First { get; }

    /// <summary>Chunk that brought the last byte.</summary>
    public ChunkStamp Last { get; }

    /// <summary>When the first byte was received, from the start of the capture.</summary>
    public TimeSpan Time => First.Time;

    public bool IsFrame => Kind is SniffEntryKind.Request or SniffEntryKind.Broadcast or SniffEntryKind.Response
        or SniffEntryKind.Exception;

    public byte? Address => IsFrame ? Bytes.Span[0] : null;

    /// <summary>The function code without the exception flag.</summary>
    public byte? FunctionCode => IsFrame ? (byte)(Bytes.Span[1] & ~FunctionCodes.ExceptionFlag) : null;

    /// <summary>Function code onwards, without address and CRC; empty unless this is a frame.</summary>
    public ReadOnlySpan<byte> Pdu => IsFrame ? Bytes.Span[1..^2] : ReadOnlySpan<byte>.Empty;

    /// <summary>Short description of the content, or null when the function is not decoded.</summary>
    public string? Summary { get; }

    /// <summary>For a request to one slave, what became of it; null for everything else.</summary>
    public RequestOutcome? Outcome { get; internal set; }

    /// <summary>For a request, the response or exception that answered it.</summary>
    public SniffEntry? Reply { get; internal set; }

    /// <summary>For a response or exception, the request it answers; null when it came unasked.</summary>
    public SniffEntry? Request { get; internal set; }

    /// <summary>For a response that came unasked: nothing was waiting for it.</summary>
    public bool IsUnexpected => Kind is SniffEntryKind.Response or SniffEntryKind.Exception && Request is null;

    /// <summary>
    /// For a paired response, whether it arrived in the chunk that ended its request. Its response time is then
    /// shorter than the time between chunks, and not known.
    /// </summary>
    public bool IsInRequestChunk => Request is not null && First.Chunk == Request.Last.Chunk;

    /// <summary>
    /// For a paired response, the time from receiving the end of the request to receiving the start of the response;
    /// null when unpaired or <see cref="IsInRequestChunk"/>. Both ends are chunk arrival times, so the adapter's and
    /// the browser's buffering shows in it.
    /// </summary>
    public TimeSpan? ResponseTime => Request is not null && !IsInRequestChunk ? Time - Request.Last.Time : null;

    /// <summary>Anything that points at a problem on the bus.</summary>
    public bool IsProblem => Kind is SniffEntryKind.Garbled or SniffEntryKind.LineError or SniffEntryKind.Exception
        || IsUnexpected
        || Outcome is RequestOutcome.NoResponse or RequestOutcome.GarbledReply;
}

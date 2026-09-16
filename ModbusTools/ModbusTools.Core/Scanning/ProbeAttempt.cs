using ModbusTools.Core.Protocol;

namespace ModbusTools.Core.Scanning;

/// <summary>One request/response transaction against a slave ID.</summary>
public sealed record ProbeAttempt
{
    /// <summary>1 for the first attempt, 2 for the first retry, and so on.</summary>
    public required int Number { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required ResponseClassification Classification { get; init; }

    public ProbeStatus Status => Classification.Status;

    public required ReadOnlyMemory<byte> Request { get; init; }

    public required ReadOnlyMemory<byte> Response { get; init; }

    /// <summary>
    /// Bytes that arrived after the response (or after the timeout) while waiting for the line to go quiet,
    /// typically a slow slave's reply. They are never considered as a response to a later request.
    /// </summary>
    public required ReadOnlyMemory<byte> LateBytes { get; init; }

    /// <summary>Bytes already waiting in the input buffer before the request was sent, discarded unread.</summary>
    public required ReadOnlyMemory<byte> StaleBytes { get; init; }

    public required TimeSpan ResponseTimeout { get; init; }

    /// <summary>
    /// First response byte relative to the estimated end of transmission (write time + frame length x character
    /// time). Clamped at zero, since USB and OS buffering can make the estimate land after the reply.
    /// Null without a response.
    /// </summary>
    public required TimeSpan? ResponseTime { get; init; }

    /// <summary>First response byte relative to when the write was issued. Null without a response.</summary>
    public required TimeSpan? RawResponseTime { get; init; }
}

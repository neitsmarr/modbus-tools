using ModbusTools.Core.Protocol;
using ModbusTools.Core.Serial;
using ModbusTools.Core.Transport;

namespace ModbusTools.Core.Scanning;

/// <param name="Serial">Settings the transport was opened with; used to estimate when a request has been sent.</param>
/// <param name="FrameGap">Silence that ends a received frame.</param>
/// <param name="InterRequestDelay">Pause after each transaction before the next request is sent.</param>
/// <param name="MaxFlushDuration">Upper bound on waiting for the line to go silent after a transaction.</param>
public sealed record TransactionTiming(
    SerialSettings Serial,
    TimeSpan FrameGap,
    TimeSpan InterRequestDelay,
    TimeSpan MaxFlushDuration);

/// <summary>Executes single request/response transactions on a transport that is already open.</summary>
/// <remarks>
/// <para>
/// Every transaction follows the same flush discipline so that a slow slave's reply is never taken as the answer to
/// a later request: discard stale input, send, read the response, wait until the line is silent for the frame gap
/// (recording anything received as late bytes), apply the inter-request delay and discard whatever arrived meanwhile
/// into the same late bytes.
/// </para>
/// <para>
/// A transaction is never interrupted part-way, so <see cref="ExecuteAsync"/> takes no cancellation token; callers
/// honour pause and cancellation between transactions with <see cref="WaitBetweenTransactionsAsync"/>.
/// </para>
/// </remarks>
public sealed class ProbeTransactionRunner
{
    private readonly IModbusRtuTransport transport;
    private readonly TimeProvider timeProvider;
    private readonly RtuFrameReader frameReader;

    public ProbeTransactionRunner(IModbusRtuTransport transport, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.transport = transport;
        this.timeProvider = timeProvider;
        frameReader = new RtuFrameReader(transport, timeProvider);
    }

    /// <summary>Honours pause and cancellation requests; returns how long the caller was paused.</summary>
    public async Task<TimeSpan> WaitBetweenTransactionsAsync(PauseToken pauseToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!pauseToken.IsPaused)
        {
            return TimeSpan.Zero;
        }

        var start = timeProvider.GetTimestamp();
        await pauseToken.WaitWhilePausedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return timeProvider.GetElapsedTime(start);
    }

    /// <param name="probe">The request to send.</param>
    /// <param name="slaveId">Address the request is sent to.</param>
    /// <param name="responseTimeout">How long to wait for the first response byte after the request has been sent.</param>
    /// <param name="timing">Frame gap, delays and the serial settings used for transmission time estimates.</param>
    /// <param name="attemptNumber">1 for the first attempt, 2 for the first retry, and so on.</param>
    public async Task<ProbeAttempt> ExecuteAsync(
        ProbeRequest probe, byte slaveId, TimeSpan responseTimeout, TransactionTiming timing, int attemptNumber = 1)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(timing);

        var stale = await transport.DiscardInputAsync();
        var lineErrorsBefore = transport.LineErrorCount;

        var request = probe.BuildFrame(slaveId);
        var startedAt = timeProvider.GetUtcNow();
        var writeTimestamp = timeProvider.GetTimestamp();
        await transport.WriteAsync(request);

        // The write completing only means the bytes were queued; estimate when the last one left the transmitter.
        var transmitEndTimestamp = writeTimestamp +
            ToTimestampDelta(RtuTiming.TransmissionTime(timing.Serial, request.Length));
        var deadline = transmitEndTimestamp + ToTimestampDelta(responseTimeout);

        var frame = await frameReader.ReadResponseAsync(probe, deadline, timing.FrameGap);
        var classification = ResponseClassifier.Classify(probe, slaveId, frame.Bytes.Span);

        TimeSpan? responseTime = null;
        TimeSpan? rawResponseTime = null;
        if (frame.FirstByteTimestamp is long firstByte)
        {
            rawResponseTime = timeProvider.GetElapsedTime(writeTimestamp, firstByte);
            var sinceTransmitEnd = timeProvider.GetElapsedTime(transmitEndTimestamp, firstByte);
            responseTime = sinceTransmitEnd > TimeSpan.Zero ? sinceTransmitEnd : TimeSpan.Zero;
        }

        var late = await frameReader.ReadUntilSilentAsync(timing.FrameGap, timing.MaxFlushDuration);
        if (timing.InterRequestDelay > TimeSpan.Zero)
        {
            await Task.Delay(timing.InterRequestDelay, timeProvider);
        }

        var arrivedDuringDelay = await transport.DiscardInputAsync();

        return new ProbeAttempt
        {
            Number = attemptNumber,
            StartedAt = startedAt,
            Classification = classification,
            Request = request,
            Response = frame.Bytes,
            LateBytes = Concat(late, arrivedDuringDelay),
            StaleBytes = stale,
            LineErrors = transport.LineErrorCount - lineErrorsBefore,
            ResponseTimeout = responseTimeout,
            ResponseTime = responseTime,
            RawResponseTime = rawResponseTime,
        };
    }

    private long ToTimestampDelta(TimeSpan span) => (long)(span.TotalSeconds * timeProvider.TimestampFrequency);

    private static ReadOnlyMemory<byte> Concat(ReadOnlyMemory<byte> first, ReadOnlyMemory<byte> second)
    {
        if (second.IsEmpty)
        {
            return first;
        }

        if (first.IsEmpty)
        {
            return second;
        }

        var combined = new byte[first.Length + second.Length];
        first.Span.CopyTo(combined);
        second.Span.CopyTo(combined.AsSpan(first.Length));
        return combined;
    }
}

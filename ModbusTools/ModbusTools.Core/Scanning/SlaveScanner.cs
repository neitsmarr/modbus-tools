using System.Runtime.CompilerServices;
using ModbusTools.Core.Protocol;
using ModbusTools.Core.Serial;
using ModbusTools.Core.Transport;

namespace ModbusTools.Core.Scanning;

/// <summary>
/// Probes a list of slave IDs one by one and reports each verdict as it becomes final.
/// </summary>
/// <remarks>
/// <para>
/// The transport is owned by the caller and must already be open with <see cref="SlaveScanOptions.Serial"/>, so the
/// same engine can be reused per settings combination by a settings sweep.
/// </para>
/// <para>
/// Every transaction follows the same flush discipline so that a slow slave's reply is never taken as the answer of
/// a later ID: discard stale input, send, read the response, wait until the line is silent for the frame gap
/// (recording anything received as late bytes of this attempt), apply the inter-request delay and discard whatever
/// arrived meanwhile into the same late bytes.
/// </para>
/// <para>
/// Pause and cancellation are only honoured between transactions, never while a frame is being sent or received.
/// </para>
/// </remarks>
public sealed class SlaveScanner
{
    private readonly IModbusRtuTransport transport;
    private readonly TimeProvider timeProvider;
    private readonly RtuFrameReader frameReader;

    public SlaveScanner(IModbusRtuTransport transport, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.transport = transport;
        this.timeProvider = timeProvider;
        frameReader = new RtuFrameReader(transport, timeProvider);
    }

    /// <summary>
    /// Runs the scan. Ends with a <see cref="ScanFinishedEvent"/>, or throws <see cref="OperationCanceledException"/>
    /// when <paramref name="cancellationToken"/> is cancelled, or the transport's exception when the port fails.
    /// </summary>
    public async IAsyncEnumerable<ScanEvent> RunAsync(
        SlaveScanOptions options,
        PauseToken pauseToken = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!transport.IsOpen)
        {
            throw new InvalidOperationException("The transport must be opened with the scan's serial settings first.");
        }

        var ids = options.SlaveIds;
        var estimator = new ScanEstimator();
        var found = 0;
        ScanProgress progress = new(0, ids.Count, 0, TimeSpan.Zero, null);

        await transport.DiscardInputAsync(cancellationToken);

        for (var index = 0; index < ids.Count; index++)
        {
            await WaitBetweenTransactionsAsync(pauseToken, cancellationToken);

            var slaveId = ids[index];
            yield return new ProbeStartedEvent(slaveId, index);

            var started = timeProvider.GetTimestamp();
            var pausedFor = TimeSpan.Zero;
            var attempts = new List<ProbeAttempt>(options.Retries + 1);
            for (var attemptNumber = 1; attemptNumber <= options.Retries + 1; attemptNumber++)
            {
                if (attemptNumber > 1)
                {
                    pausedFor += await WaitBetweenTransactionsAsync(pauseToken, cancellationToken);
                }

                var attempt = await ExecuteAttemptAsync(options, slaveId, attemptNumber);
                options.TimeoutStrategy.OnAttemptCompleted(slaveId, attempt);
                attempts.Add(attempt);
                if (attempt.Status.IsFound())
                {
                    break;
                }
            }

            var duration = timeProvider.GetElapsedTime(started) - pausedFor;
            estimator.Record(duration);

            var result = new SlaveProbeResult(slaveId, attempts, duration);
            if (result.IsFound)
            {
                found++;
            }

            progress = new ScanProgress(index + 1, ids.Count, found, estimator.TotalDuration,
                estimator.EstimateRemaining(ids.Count - index - 1));
            yield return new ProbeCompletedEvent(result, progress);

            if (options.StopAtFirstFound && result.IsFound)
            {
                yield return new ScanFinishedEvent(ScanOutcome.StoppedAtFirstFound, progress);
                yield break;
            }
        }

        yield return new ScanFinishedEvent(ScanOutcome.Completed, progress);
    }

    /// <summary>Honours pause and cancellation requests; returns how long the scan was paused.</summary>
    private async Task<TimeSpan> WaitBetweenTransactionsAsync(PauseToken pauseToken, CancellationToken cancellationToken)
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

    // A transaction is never interrupted part-way, so no cancellation token is passed below.
    private async Task<ProbeAttempt> ExecuteAttemptAsync(SlaveScanOptions options, byte slaveId, int attemptNumber)
    {
        var frameGap = options.EffectiveFrameGap;
        var stale = await transport.DiscardInputAsync();

        var request = options.Probe.BuildFrame(slaveId);
        var timeout = options.TimeoutStrategy.GetResponseTimeout(slaveId, attemptNumber);
        var startedAt = timeProvider.GetUtcNow();
        var writeTimestamp = timeProvider.GetTimestamp();
        await transport.WriteAsync(request);

        // The write completing only means the bytes were queued; estimate when the last one left the transmitter.
        var transmitEndTimestamp = writeTimestamp +
            ToTimestampDelta(RtuTiming.TransmissionTime(options.Serial, request.Length));
        var deadline = transmitEndTimestamp + ToTimestampDelta(timeout);

        var frame = await frameReader.ReadResponseAsync(options.Probe, deadline, frameGap);
        var classification = ResponseClassifier.Classify(options.Probe, slaveId, frame.Bytes.Span);

        TimeSpan? responseTime = null;
        TimeSpan? rawResponseTime = null;
        if (frame.FirstByteTimestamp is long firstByte)
        {
            rawResponseTime = timeProvider.GetElapsedTime(writeTimestamp, firstByte);
            var sinceTransmitEnd = timeProvider.GetElapsedTime(transmitEndTimestamp, firstByte);
            responseTime = sinceTransmitEnd > TimeSpan.Zero ? sinceTransmitEnd : TimeSpan.Zero;
        }

        var late = await frameReader.ReadUntilSilentAsync(frameGap, options.MaxFlushDuration);
        if (options.InterRequestDelay > TimeSpan.Zero)
        {
            await Task.Delay(options.InterRequestDelay, timeProvider);
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
            ResponseTimeout = timeout,
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

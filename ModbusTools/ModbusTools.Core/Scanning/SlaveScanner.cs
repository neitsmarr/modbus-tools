using System.Runtime.CompilerServices;
using ModbusTools.Core.Protocol;
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
/// Transactions follow the flush discipline of <see cref="ProbeTransactionRunner"/>. Pause and cancellation are only
/// honoured between transactions, never while a frame is being sent or received.
/// </para>
/// </remarks>
public sealed class SlaveScanner
{
    private readonly IModbusRtuTransport transport;
    private readonly TimeProvider timeProvider;
    private readonly ProbeTransactionRunner runner;

    public SlaveScanner(IModbusRtuTransport transport, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.transport = transport;
        this.timeProvider = timeProvider;
        runner = new ProbeTransactionRunner(transport, timeProvider);
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
        var timing = new TransactionTiming(
            options.Serial, options.EffectiveFrameGap, options.InterRequestDelay, options.MaxFlushDuration);
        var estimator = new ScanEstimator();
        var found = 0;
        ScanProgress progress = new(0, ids.Count, 0, TimeSpan.Zero, null);

        await transport.DiscardInputAsync(cancellationToken);

        for (var index = 0; index < ids.Count; index++)
        {
            await runner.WaitBetweenTransactionsAsync(pauseToken, cancellationToken);

            var slaveId = ids[index];
            yield return new ProbeStartedEvent(slaveId, index);

            var started = timeProvider.GetTimestamp();
            var pausedFor = TimeSpan.Zero;
            var attempts = new List<ProbeAttempt>(options.Retries + 1);
            for (var attemptNumber = 1; attemptNumber <= options.Retries + 1; attemptNumber++)
            {
                if (attemptNumber > 1)
                {
                    pausedFor += await runner.WaitBetweenTransactionsAsync(pauseToken, cancellationToken);
                }

                var timeout = options.TimeoutStrategy.GetResponseTimeout(slaveId, attemptNumber);
                var attempt = await runner.ExecuteAsync(options.Probe, slaveId, timeout, timing, attemptNumber);
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
}

using System.Runtime.CompilerServices;
using ModbusTools.Core.Scanning;
using ModbusTools.Core.Serial;
using ModbusTools.Core.Transport;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// Tries the same request at one rate after another, as chosen by the options' <see cref="IBaudSweepStrategy"/>, and
/// reports every request as it completes.
/// </summary>
/// <remarks>
/// Unlike the scanner and the crawler, this tool owns the port: a serial port's rate cannot be changed while it is
/// open, so every rate means closing the port, opening it again at the new rate and letting the line settle. A rate
/// the adapter or driver refuses is recorded as such and the sweep carries on, because plenty of adapters only
/// produce a fixed set of rates; only a run of refusals ends the sweep, since that is what a dead port looks like.
/// </remarks>
public sealed class BaudRateSweeper
{
    /// <summary>Rates refused in a row before the sweep gives up: a port that has gone away refuses every rate.</summary>
    public const int MaxConsecutiveRefusals = 5;

    private readonly IModbusRtuTransport transport;
    private readonly TimeProvider timeProvider;
    private readonly ProbeTransactionRunner runner;

    public BaudRateSweeper(IModbusRtuTransport transport, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.transport = transport;
        this.timeProvider = timeProvider;
        runner = new ProbeTransactionRunner(transport, timeProvider);
    }

    /// <summary>
    /// Runs the sweep, recording every request into <paramref name="result"/> as it goes; the strategy reads those
    /// totals to decide where to go next. Ends with a <see cref="BaudSweepFinishedEvent"/>, or throws
    /// <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled, or the
    /// transport's exception when the port fails.
    /// </summary>
    public async IAsyncEnumerable<BaudSweepEvent> RunAsync(
        BaudSweepOptions options,
        BaudSweepResult result,
        PauseToken pauseToken = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(result);
        if (transport.IsOpen)
        {
            throw new InvalidOperationException("The transport must be closed: a sweep opens the port at each rate itself.");
        }

        var planner = options.Strategy.CreatePlanner(options, result);
        var refusals = 0;

        while (true)
        {
            await runner.WaitBetweenTransactionsAsync(pauseToken, cancellationToken);
            if (planner.Next() is not BaudSweepStep step)
            {
                break;
            }

            if (step.BaudRate <= 0 || step.Requests <= 0)
            {
                throw new InvalidOperationException(
                    $"Strategy '{options.Strategy.Id}' planned {step.Requests} request(s) at {step.BaudRate} baud.");
            }

            yield return new BaudRateStartedEvent(step);

            var serial = options.SerialAt(step.BaudRate);
            var rate = result.GetOrAdd(step.BaudRate);
            var visitStarted = timeProvider.GetTimestamp();
            var paused = TimeSpan.Zero;
            if (await TryOpenAsync(serial, cancellationToken) is string openError)
            {
                result.RecordPortError(step.BaudRate, openError);
                result.RecordVisit(new BaudVisit(step, 0, 0));
                planner.OnCompleted(new BaudSweepStepResult(step, 0, rate));
                yield return new BaudRateRefusedEvent(step.BaudRate, openError);
                yield return new BaudRateCompletedEvent(
                    step.BaudRate, 0, timeProvider.GetElapsedTime(visitStarted), planner.ExpectedRequests);

                if (++refusals >= MaxConsecutiveRefusals)
                {
                    throw new IOException(
                        $"The port refused {refusals} baud rates in a row, last {step.BaudRate}: {openError}");
                }

                continue;
            }

            refusals = 0;
            var answered = 0;
            try
            {
                // A quiet line at the new rate lets the device finish any frame it was reading and resynchronise.
                if (options.PortSettleDelay > TimeSpan.Zero)
                {
                    await Task.Delay(options.PortSettleDelay, timeProvider, cancellationToken);
                }

                await transport.DiscardInputAsync(cancellationToken);
                var timing = options.TimingAt(serial);
                for (var request = 1; request <= step.Requests; request++)
                {
                    if (request > 1)
                    {
                        paused += await runner.WaitBetweenTransactionsAsync(pauseToken, cancellationToken);
                    }

                    var attempt = await runner.ExecuteAsync(
                        options.Probe, options.SlaveId, options.ResponseTimeout, timing, request);
                    result.Record(step.BaudRate, attempt);
                    if (attempt.GetReplyStatus() == ReplyStatus.Answered)
                    {
                        answered++;
                    }

                    yield return new BaudRequestCompletedEvent(step.BaudRate, attempt);
                }
            }
            finally
            {
                await transport.CloseAsync();
            }

            result.RecordVisit(new BaudVisit(step, step.Requests, answered));
            planner.OnCompleted(new BaudSweepStepResult(step, answered, rate));
            var visit = timeProvider.GetElapsedTime(visitStarted) - paused;
            yield return new BaudRateCompletedEvent(step.BaudRate, step.Requests, visit, planner.ExpectedRequests);
        }

        yield return new BaudSweepFinishedEvent();
    }

    /// <summary>Opens the port, or returns why it could not be opened at this rate.</summary>
    private async Task<string?> TryOpenAsync(SerialSettings serial, CancellationToken cancellationToken)
    {
        try
        {
            await transport.OpenAsync(serial, cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await transport.CloseAsync();

            // Browser errors arrive as "message\nstack"; only the message is worth keeping.
            return ex.Message.Split('\n', 2)[0];
        }
    }
}

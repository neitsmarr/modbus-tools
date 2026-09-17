using ModbusTools.Core.Scanning;
using ModbusTools.Core.Transport;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// One sweep run with its live state: drives <see cref="BaudRateSweeper"/>, records results and always releases the
/// port afterwards. UI code binds to this object and polls it for display.
/// </summary>
public sealed class BaudSweepSession
{
    private readonly TimeProvider timeProvider;
    private readonly PauseTokenSource pauseSource = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly ScanEstimator estimator = new();

    private TimeSpan activeTimeBeforePause;
    private long? runningSince;
    private int? expectedRequests;

    public BaudSweepSession(BaudSweepOptions options, string portName, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        Options = options;
        PortName = portName;
        this.timeProvider = timeProvider;
        Result = new BaudSweepResult(options.Grid);
        EstimatedRequests = options.EstimateRequests();
        WorstCase = options.EstimateWorstCaseDuration();
    }

    public BaudSweepOptions Options { get; }

    public string PortName { get; }

    public RequestEstimate EstimatedRequests { get; }

    /// <summary>Estimated duration if no request is answered; see <see cref="BaudSweepOptions.EstimateWorstCaseDuration"/>.</summary>
    public TimeSpan WorstCase { get; }

    public ScanSessionState State { get; private set; } = ScanSessionState.NotStarted;

    public SweepOutcome? Outcome { get; private set; }

    /// <summary>The failure that ended the sweep when <see cref="Outcome"/> is <see cref="SweepOutcome.Failed"/>.</summary>
    public Exception? Error { get; private set; }

    /// <summary>Results so far; they stay available after cancellation or failure.</summary>
    public BaudSweepResult Result { get; }

    /// <summary>The rate being probed, or null between rates.</summary>
    public int? CurrentBaudRate { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public bool IsCancellationRequested => cancellation.IsCancellationRequested;

    /// <summary>Time spent running, excluding pauses. Advances live while the sweep runs.</summary>
    public TimeSpan ActiveTime =>
        activeTimeBeforePause + (runningSince is long since ? timeProvider.GetElapsedTime(since) : TimeSpan.Zero);

    public BaudSweepProgress Progress
    {
        get
        {
            var requests = Result.Statistics.Requests;
            var expected = Math.Max(expectedRequests ?? EstimatedRequests.Maximum, requests);
            var estimate = State == ScanSessionState.Finished ? null : estimator.EstimateRemaining(expected - requests);
            return new BaudSweepProgress(requests, expected, Result.TestedRates, estimate);
        }
    }

    /// <summary>
    /// Runs the sweep to completion and leaves the port closed. The sweep opens and closes
    /// <paramref name="transport"/> itself at every rate, so it must be handed over closed. Failures are reported
    /// through <see cref="Outcome"/> and <see cref="Error"/> rather than thrown.
    /// </summary>
    public async Task RunAsync(IModbusRtuTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (State != ScanSessionState.NotStarted)
        {
            throw new InvalidOperationException("A sweep session can only be run once.");
        }

        State = ScanSessionState.Running;
        StartedAt = timeProvider.GetUtcNow();
        runningSince = timeProvider.GetTimestamp();

        try
        {
            var sweeper = new BaudRateSweeper(transport, timeProvider);
            await foreach (var sweepEvent in sweeper.RunAsync(Options, Result, pauseSource.Token, cancellation.Token))
            {
                Apply(sweepEvent);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Outcome = SweepOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            Outcome = SweepOutcome.Failed;
            Error = ex;
        }
        finally
        {
            await transport.CloseAsync();
            StopClock();
            CurrentBaudRate = null;
            FinishedAt = timeProvider.GetUtcNow();
            State = ScanSessionState.Finished;
        }
    }

    /// <summary>Requests a pause; it takes effect after the current transaction.</summary>
    public void Pause()
    {
        if (State != ScanSessionState.Running)
        {
            return;
        }

        pauseSource.Pause();
        StopClock();
        State = ScanSessionState.Paused;
    }

    public void Resume()
    {
        if (State != ScanSessionState.Paused)
        {
            return;
        }

        runningSince = timeProvider.GetTimestamp();
        State = ScanSessionState.Running;
        pauseSource.Resume();
    }

    /// <summary>Requests cancellation; it takes effect after the current transaction, including while paused.</summary>
    public void Cancel()
    {
        if (State is ScanSessionState.Running or ScanSessionState.Paused)
        {
            cancellation.Cancel();
        }
    }

    public BaudSweepReport CreateReport() => new(
        Options,
        PortName,
        StartedAt,
        FinishedAt,
        ActiveTime,
        Outcome,
        Error?.Message,
        Result);

    private void Apply(BaudSweepEvent sweepEvent)
    {
        switch (sweepEvent)
        {
            case BaudRateStartedEvent started:
                CurrentBaudRate = started.Step.BaudRate;
                break;
            case BaudRequestCompletedEvent completed:
                estimator.Record(completed.Duration);
                break;
            case BaudRateCompletedEvent completed:
                expectedRequests = completed.ExpectedRequests;
                CurrentBaudRate = null;
                break;
            case BaudSweepFinishedEvent:
                Outcome = SweepOutcome.Completed;
                break;
        }
    }

    private void StopClock()
    {
        if (runningSince is long since)
        {
            activeTimeBeforePause += timeProvider.GetElapsedTime(since);
            runningSince = null;
        }
    }
}

/// <summary>Snapshot of a sweep's settings, timing and results, as exported.</summary>
public sealed record BaudSweepReport(
    BaudSweepOptions Options,
    string PortName,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    TimeSpan ActiveTime,
    SweepOutcome? Outcome,
    string? ErrorMessage,
    BaudSweepResult Result);

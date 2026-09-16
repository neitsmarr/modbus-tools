using ModbusTools.Core.Transport;

namespace ModbusTools.Core.Scanning;

public enum ScanSessionState
{
    NotStarted,
    Running,
    Paused,
    Finished,
}

/// <summary>
/// One scan run with its live state: opens the transport, drives <see cref="SlaveScanner"/>, collects results and
/// always releases the port afterwards. UI code binds to this object and polls it for display.
/// </summary>
public sealed class ScanSession
{
    private readonly TimeProvider timeProvider;
    private readonly PauseTokenSource pauseSource = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly ScanResultSet results = new();

    private TimeSpan activeTimeBeforePause;
    private long? runningSince;

    public ScanSession(SlaveScanOptions options, string portName, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        Options = options;
        PortName = portName;
        this.timeProvider = timeProvider;
        WorstCase = ScanEstimator.WorstCase(options);
        Progress = new ScanProgress(0, options.SlaveIds.Count, 0, TimeSpan.Zero, null);
    }

    public SlaveScanOptions Options { get; }

    public string PortName { get; }

    public TimeSpan WorstCase { get; }

    public ScanSessionState State { get; private set; } = ScanSessionState.NotStarted;

    public ScanOutcome? Outcome { get; private set; }

    /// <summary>The failure that ended the scan when <see cref="Outcome"/> is <see cref="ScanOutcome.Failed"/>.</summary>
    public Exception? Error { get; private set; }

    public ScanResultSet Results => results;

    public ScanProgress Progress { get; private set; }

    /// <summary>The ID currently being probed, or null when idle.</summary>
    public byte? CurrentSlaveId { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public bool IsCancellationRequested => cancellation.IsCancellationRequested;

    /// <summary>Time spent running, excluding pauses. Advances live while the scan runs.</summary>
    public TimeSpan ActiveTime =>
        activeTimeBeforePause + (runningSince is long since ? timeProvider.GetElapsedTime(since) : TimeSpan.Zero);

    /// <summary>
    /// Opens <paramref name="transport"/>, runs the scan to completion and closes the transport. Scan failures are
    /// reported through <see cref="Outcome"/> and <see cref="Error"/> rather than thrown.
    /// </summary>
    public async Task RunAsync(IModbusRtuTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (State != ScanSessionState.NotStarted)
        {
            throw new InvalidOperationException("A scan session can only be run once.");
        }

        State = ScanSessionState.Running;
        StartedAt = timeProvider.GetUtcNow();
        runningSince = timeProvider.GetTimestamp();

        try
        {
            await transport.OpenAsync(Options.Serial, cancellation.Token);
            var scanner = new SlaveScanner(transport, timeProvider);
            await foreach (var scanEvent in scanner.RunAsync(Options, pauseSource.Token, cancellation.Token))
            {
                Apply(scanEvent);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Outcome = ScanOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            Outcome = ScanOutcome.Failed;
            Error = ex;
        }
        finally
        {
            await transport.CloseAsync();
            StopClock();
            CurrentSlaveId = null;
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

    public ScanReport CreateReport() => new(
        Options,
        PortName,
        StartedAt,
        FinishedAt,
        ActiveTime,
        Outcome,
        Error?.Message,
        results.Results.ToArray());

    private void Apply(ScanEvent scanEvent)
    {
        switch (scanEvent)
        {
            case ProbeStartedEvent started:
                CurrentSlaveId = started.SlaveId;
                break;
            case ProbeCompletedEvent completed:
                results.Add(completed.Result);
                Progress = completed.Progress;
                break;
            case ScanFinishedEvent finished:
                Progress = finished.Progress;
                Outcome = finished.Outcome;
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

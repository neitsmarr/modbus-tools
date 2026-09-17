using ModbusTools.Core.Scanning;
using ModbusTools.Core.Transport;

namespace ModbusTools.Core.Crawling;

/// <summary>
/// One crawl run with its live state: opens the transport, drives <see cref="RegisterMapCrawler"/>, records results
/// and always releases the port afterwards. UI code binds to this object and polls it for display.
/// </summary>
public sealed class CrawlSession
{
    private readonly TimeProvider timeProvider;
    private readonly PauseTokenSource pauseSource = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly ScanEstimator estimator = new();

    private TimeSpan activeTimeBeforePause;
    private long? runningSince;

    public CrawlSession(CrawlOptions options, string portName, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        Options = options;
        PortName = portName;
        this.timeProvider = timeProvider;
        Result = new CrawlResult(options.Tables, options.Range);
        EstimatedRequests = options.EstimateRequests();
        WorstCase = options.EstimateWorstCaseDuration();
    }

    public CrawlOptions Options { get; }

    public string PortName { get; }

    public RequestEstimate EstimatedRequests { get; }

    /// <summary>Estimated duration if no request is answered; see <see cref="CrawlOptions.EstimateWorstCaseDuration"/>.</summary>
    public TimeSpan WorstCase { get; }

    public ScanSessionState State { get; private set; } = ScanSessionState.NotStarted;

    public CrawlOutcome? Outcome { get; private set; }

    /// <summary>The failure that ended the crawl when <see cref="Outcome"/> is <see cref="CrawlOutcome.Failed"/>.</summary>
    public Exception? Error { get; private set; }

    /// <summary>Results so far; they stay available after cancellation or failure.</summary>
    public CrawlResult Result { get; }

    /// <summary>The table being crawled, or null when idle.</summary>
    public CrawlTable? CurrentTable { get; private set; }

    /// <summary>The read in progress, or null between reads.</summary>
    public CrawlRead? CurrentRead { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public bool IsCancellationRequested => cancellation.IsCancellationRequested;

    /// <summary>Time spent running, excluding pauses. Advances live while the crawl runs.</summary>
    public TimeSpan ActiveTime =>
        activeTimeBeforePause + (runningSince is long since ? timeProvider.GetElapsedTime(since) : TimeSpan.Zero);

    public CrawlProgress Progress
    {
        get
        {
            var remaining = Result.TotalAddresses - Result.CheckedCount;
            var estimate = State == ScanSessionState.Finished ? null : estimator.EstimateRemaining(remaining);
            return new CrawlProgress(Result.CheckedCount, Result.TotalAddresses, Result.Statistics.Requests, estimate);
        }
    }

    /// <summary>
    /// Opens <paramref name="transport"/>, runs the crawl to completion and closes the transport. Failures are
    /// reported through <see cref="Outcome"/> and <see cref="Error"/> rather than thrown.
    /// </summary>
    public async Task RunAsync(IModbusRtuTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (State != ScanSessionState.NotStarted)
        {
            throw new InvalidOperationException("A crawl session can only be run once.");
        }

        State = ScanSessionState.Running;
        StartedAt = timeProvider.GetUtcNow();
        runningSince = timeProvider.GetTimestamp();

        try
        {
            await transport.OpenAsync(Options.Serial, cancellation.Token);
            var crawler = new RegisterMapCrawler(transport, timeProvider);
            await foreach (var crawlEvent in crawler.RunAsync(Options, pauseSource.Token, cancellation.Token))
            {
                Apply(crawlEvent);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Outcome = CrawlOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            Outcome = CrawlOutcome.Failed;
            Error = ex;
        }
        finally
        {
            await transport.CloseAsync();
            StopClock();
            CurrentTable = null;
            CurrentRead = null;
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

    public CrawlReport CreateReport() => new(
        Options,
        PortName,
        StartedAt,
        FinishedAt,
        ActiveTime,
        Outcome,
        Error?.Message,
        Result);

    private void Apply(CrawlEvent crawlEvent)
    {
        switch (crawlEvent)
        {
            case CrawlTableStartedEvent tableStarted:
                CurrentTable = tableStarted.Table;
                break;
            case CrawlReadStartedEvent readStarted:
                CurrentRead = readStarted.Read;
                break;
            case CrawlReadCompletedEvent readCompleted:
                Result.Record(readCompleted.Result);
                estimator.Record(readCompleted.Duration);
                CurrentRead = null;
                break;
            case CrawlFinishedEvent:
                Outcome = CrawlOutcome.Completed;
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

/// <summary>Snapshot of a crawl's settings, timing and results, as exported.</summary>
public sealed record CrawlReport(
    CrawlOptions Options,
    string PortName,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    TimeSpan ActiveTime,
    CrawlOutcome? Outcome,
    string? ErrorMessage,
    CrawlResult Result);

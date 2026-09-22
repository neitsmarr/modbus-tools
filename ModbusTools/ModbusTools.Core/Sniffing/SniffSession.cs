using ModbusTools.Core.Scanning;
using ModbusTools.Core.Transport;

namespace ModbusTools.Core.Sniffing;

public enum SniffOutcome
{
    Stopped,
    Failed,
}

/// <summary>
/// One capture run with its live state: opens the transport, reads until stopped, feeds everything into a
/// <see cref="SniffCapture"/> and always releases the port afterwards. It never writes to the port. UI code binds to
/// this object and polls it for display.
/// </summary>
public sealed class SniffSession
{
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource stop = new();
    private long captureStart;

    public SniffSession(SniffOptions options, string portName, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        Options = options;
        PortName = portName;
        this.timeProvider = timeProvider;
        Capture = NewCapture();
    }

    public SniffOptions Options { get; }

    public string PortName { get; }

    public ScanSessionState State { get; private set; } = ScanSessionState.NotStarted;

    /// <summary>Why the capture ended, once it has.</summary>
    public SniffOutcome? Outcome { get; private set; }

    /// <summary>The failure that ended the capture when <see cref="Outcome"/> is <see cref="SniffOutcome.Failed"/>.</summary>
    public Exception? Error { get; private set; }

    /// <summary>What has been received since the start or the last <see cref="Clear"/>.</summary>
    public SniffCapture Capture { get; private set; }

    /// <summary>When <see cref="Capture"/> began: the start of the run, or the last <see cref="Clear"/>.</summary>
    public DateTimeOffset? CaptureStartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public bool IsStopRequested => stop.IsCancellationRequested;

    /// <summary>Time <see cref="Capture"/> covers. Advances live while the capture runs.</summary>
    public TimeSpan CaptureTime => State == ScanSessionState.Running
        ? timeProvider.GetElapsedTime(captureStart)
        : Capture.Elapsed;

    /// <summary>
    /// Opens <paramref name="transport"/>, captures until <see cref="Stop"/> and closes the transport. Failures are
    /// reported through <see cref="Outcome"/> and <see cref="Error"/> rather than thrown.
    /// </summary>
    public async Task RunAsync(IModbusRtuTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (State != ScanSessionState.NotStarted)
        {
            throw new InvalidOperationException("A capture session can only be run once.");
        }

        State = ScanSessionState.Running;
        CaptureStartedAt = timeProvider.GetUtcNow();
        captureStart = timeProvider.GetTimestamp();

        try
        {
            await transport.OpenAsync(Options.Serial, stop.Token);
            var lineErrors = transport.LineErrorCount;
            while (true)
            {
                var bytes = await transport.ReadAsync(Options.FrameGap, stop.Token);
                var time = CaptureTime;

                // A line error cost the bytes it hit, so it comes before whatever the read returned after it.
                for (; lineErrors < transport.LineErrorCount; lineErrors++)
                {
                    Capture.LineError(time);
                }

                if (bytes.IsEmpty)
                {
                    Capture.Silence(time);
                }
                else
                {
                    Capture.Receive(bytes.Span, time);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            Outcome = SniffOutcome.Stopped;
        }
        catch (Exception ex)
        {
            Outcome = SniffOutcome.Failed;
            Error = ex;
        }
        finally
        {
            await transport.CloseAsync();
            Capture.End(CaptureTime);
            FinishedAt = timeProvider.GetUtcNow();
            State = ScanSessionState.Finished;
        }
    }

    /// <summary>Stops capturing; the port is released once the read in progress returns.</summary>
    public void Stop()
    {
        if (State == ScanSessionState.Running)
        {
            stop.Cancel();
        }
    }

    /// <summary>Discards everything received so far and goes on capturing into an empty log, timed from now.</summary>
    public void Clear()
    {
        if (State != ScanSessionState.Running)
        {
            return;
        }

        Capture = NewCapture();
        CaptureStartedAt = timeProvider.GetUtcNow();
        captureStart = timeProvider.GetTimestamp();
    }

    public SniffReport CreateReport() => new(
        Options,
        PortName,
        CaptureStartedAt,
        FinishedAt,
        CaptureTime,
        Outcome,
        Error?.Message,
        Capture);

    private SniffCapture NewCapture() => new(Options, new RtuFrameSplitter());
}

/// <summary>A capture with its settings and timing, as exported.</summary>
public sealed record SniffReport(
    SniffOptions Options,
    string PortName,
    DateTimeOffset? CaptureStartedAt,
    DateTimeOffset? FinishedAt,
    TimeSpan CaptureTime,
    SniffOutcome? Outcome,
    string? ErrorMessage,
    SniffCapture Capture);

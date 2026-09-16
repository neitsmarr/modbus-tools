namespace ModbusTools.Core.Scanning;

public enum ScanOutcome
{
    Completed,
    StoppedAtFirstFound,
    Cancelled,
    Failed,
}

/// <param name="Completed">IDs finished so far.</param>
/// <param name="Total">IDs in the scan.</param>
/// <param name="Found">IDs that answered with OK or an exception.</param>
/// <param name="ActiveDuration">Time spent probing, excluding pauses.</param>
/// <param name="EstimatedRemaining">Moving-average estimate for the remaining IDs; null before the first ID finishes.</param>
public sealed record ScanProgress(int Completed, int Total, int Found, TimeSpan ActiveDuration, TimeSpan? EstimatedRemaining)
{
    public int Remaining => Total - Completed;
}

public abstract record ScanEvent;

/// <summary>Probing of <paramref name="SlaveId"/> is about to start.</summary>
public sealed record ProbeStartedEvent(byte SlaveId, int Index) : ScanEvent;

/// <summary>A slave ID is finished, including flushing of late bytes; its result will not change.</summary>
public sealed record ProbeCompletedEvent(SlaveProbeResult Result, ScanProgress Progress) : ScanEvent;

/// <summary>The scan ended normally or stopped at the first found ID. Cancellation and failures surface as exceptions.</summary>
public sealed record ScanFinishedEvent(ScanOutcome Outcome, ScanProgress Progress) : ScanEvent;

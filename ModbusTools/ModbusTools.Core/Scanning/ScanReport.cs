namespace ModbusTools.Core.Scanning;

/// <summary>Snapshot of a scan's settings, timing and results, as exported.</summary>
public sealed record ScanReport(
    SlaveScanOptions Options,
    string PortName,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    TimeSpan ActiveTime,
    ScanOutcome? Outcome,
    string? ErrorMessage,
    IReadOnlyList<SlaveProbeResult> Results);

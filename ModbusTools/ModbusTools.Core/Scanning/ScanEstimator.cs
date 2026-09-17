using ModbusTools.Core.Serial;

namespace ModbusTools.Core.Scanning;

/// <summary>Scan duration estimates: an up-front worst case and a moving-average ETA while running.</summary>
public sealed class ScanEstimator
{
    public const int DefaultWindowSize = 16;

    private readonly Queue<TimeSpan> window = new();
    private readonly int windowSize;
    private TimeSpan windowSum;

    public ScanEstimator(int windowSize = DefaultWindowSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 1);
        this.windowSize = windowSize;
    }

    public int Samples { get; private set; }

    public TimeSpan TotalDuration { get; private set; }

    /// <summary>Records the time one item (a slave ID, an address) took.</summary>
    public void Record(TimeSpan itemDuration)
    {
        Samples++;
        TotalDuration += itemDuration;
        window.Enqueue(itemDuration);
        windowSum += itemDuration;
        if (window.Count > windowSize)
        {
            windowSum -= window.Dequeue();
        }
    }

    /// <summary>Average duration of the most recent items times <paramref name="remainingItems"/>; null before any sample.</summary>
    public TimeSpan? EstimateRemaining(int remainingItems) =>
        window.Count == 0 ? null : windowSum / window.Count * remainingItems;

    /// <summary>
    /// Longest the scan can take if no ID answers: every attempt waits for the request to be sent, the full
    /// timeout, one frame gap of flushing and the inter-request delay. The frame gap is included because a silent
    /// ID still waits for it after the timeout.
    /// </summary>
    public static TimeSpan WorstCase(SlaveScanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var transmission = RtuTiming.TransmissionTime(options.Serial, options.Probe.BuildFrame(1).Length);
        var perAttemptOverhead = transmission + options.EffectiveFrameGap + options.InterRequestDelay;
        var total = TimeSpan.Zero;
        foreach (var id in options.SlaveIds)
        {
            for (var attempt = 1; attempt <= options.Retries + 1; attempt++)
            {
                total += options.TimeoutStrategy.GetResponseTimeout(id, attempt) + perAttemptOverhead;
            }
        }

        return total;
    }
}

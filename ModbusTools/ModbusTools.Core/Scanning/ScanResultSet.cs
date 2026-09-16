using ModbusTools.Core.Protocol;

namespace ModbusTools.Core.Scanning;

public enum ScanResultFilter
{
    All,

    /// <summary>OK or exception.</summary>
    Responding,

    /// <summary>Garbled, wrong ID, inconsistent attempts or late bytes.</summary>
    Problematic,

    /// <summary>No response.</summary>
    Silent,
}

public enum ScanHintKind
{
    /// <summary>Garbled replies are widespread: serial settings probably do not match the bus.</summary>
    SettingsMismatch,

    /// <summary>Garbled replies are limited to a few IDs: duplicate addresses, or noise on those devices.</summary>
    GarbledCluster,

    /// <summary>Replies came from IDs that were already probed: a slave answers slower than the timeout.</summary>
    SlowResponder,
}

public sealed record ScanHint(ScanHintKind Kind, string Message, IReadOnlyList<byte> SlaveIds);

/// <summary>Results of a scan in probe order, with per-status counts, filtering and diagnostic hints.</summary>
public sealed class ScanResultSet
{
    /// <summary>Garbled IDs needed before a settings mismatch is suggested.</summary>
    public const int SettingsMismatchMinimumCount = 3;

    /// <summary>At most this many garbled IDs is treated as a cluster rather than a settings problem.</summary>
    public const int ClusterMaximumCount = 3;

    private readonly List<SlaveProbeResult> results = [];
    private readonly int[] statusCounts = new int[Enum.GetValues<ProbeStatus>().Length];

    public IReadOnlyList<SlaveProbeResult> Results => results;

    public int Count => results.Count;

    public int FoundCount => CountOf(ProbeStatus.Ok) + CountOf(ProbeStatus.Exception);

    public int ProblematicCount { get; private set; }

    public void Add(SlaveProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        results.Add(result);
        statusCounts[(int)result.Status]++;
        if (result.IsProblematic)
        {
            ProblematicCount++;
        }
    }

    public int CountOf(ProbeStatus status) => statusCounts[(int)status];

    public int CountOf(ScanResultFilter filter) => filter switch
    {
        ScanResultFilter.Responding => FoundCount,
        ScanResultFilter.Problematic => ProblematicCount,
        ScanResultFilter.Silent => CountOf(ProbeStatus.NoResponse),
        _ => Count,
    };

    public IEnumerable<SlaveProbeResult> Filter(ScanResultFilter filter) => filter switch
    {
        ScanResultFilter.Responding => results.Where(r => r.IsFound),
        ScanResultFilter.Problematic => results.Where(r => r.IsProblematic),
        ScanResultFilter.Silent => results.Where(r => r.Status == ProbeStatus.NoResponse),
        _ => results,
    };

    public IReadOnlyList<ScanHint> GetHints()
    {
        var hints = new List<ScanHint>();

        var garbledIds = results
            .Where(r => r.Attempts.Any(a => a.Status == ProbeStatus.Garbled))
            .Select(r => r.SlaveId)
            .ToArray();
        var idsWithBytes = results.Count(r => r.Attempts.Any(a => !a.Response.IsEmpty));

        if (garbledIds.Length >= SettingsMismatchMinimumCount && garbledIds.Length * 2 >= idsWithBytes)
        {
            hints.Add(new ScanHint(ScanHintKind.SettingsMismatch,
                $"{garbledIds.Length} of {idsWithBytes} IDs that sent data replied with garbled frames. " +
                "The baud rate, parity or stop bits probably do not match the bus, or the line is noisy or unterminated.",
                garbledIds));
        }
        else if (garbledIds.Length is > 0 and <= ClusterMaximumCount &&
                 results.Any(r => r.IsFound && !garbledIds.Contains(r.SlaveId)))
        {
            hints.Add(new ScanHint(ScanHintKind.GarbledCluster,
                $"Garbled replies only at ID(s) {FormatIds(garbledIds)} while other devices answer cleanly. " +
                "Two devices may share that address, or that device is noisy or uses different settings.",
                garbledIds));
        }

        var probedBefore = new HashSet<byte>();
        var slowIds = new SortedSet<byte>();
        foreach (var result in results)
        {
            foreach (var attempt in result.Attempts)
            {
                if (attempt.Classification.ReplyAddress is byte replyAddress && probedBefore.Contains(replyAddress))
                {
                    slowIds.Add(replyAddress);
                }
            }

            probedBefore.Add(result.SlaveId);
        }

        if (slowIds.Count > 0)
        {
            hints.Add(new ScanHint(ScanHintKind.SlowResponder,
                $"ID(s) {FormatIds(slowIds)} replied while later IDs were being probed. " +
                "Increase the response timeout and rescan those IDs.",
                slowIds.ToArray()));
        }

        return hints;
    }

    private static string FormatIds(IEnumerable<byte> ids) => string.Join(", ", ids);
}

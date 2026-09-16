using ModbusTools.Core.Protocol;

namespace ModbusTools.Core.Scanning;

/// <summary>Final verdict for one slave ID, derived from all of its attempts.</summary>
public sealed record SlaveProbeResult
{
    public SlaveProbeResult(byte slaveId, IReadOnlyList<ProbeAttempt> attempts, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        if (attempts.Count == 0)
        {
            throw new ArgumentException("At least one attempt is required.", nameof(attempts));
        }

        SlaveId = slaveId;
        Attempts = attempts.ToArray();
        Duration = duration;

        // MaxBy keeps the earliest of equally ranked attempts.
        BestAttempt = Attempts.MaxBy(a => a.Status)!;
        IsInconsistent = Attempts.Any(a => a.Status != BestAttempt.Status);
        LateBytes = Attempts.SelectMany(a => a.LateBytes.ToArray()).ToArray();
        Notes = BuildNotes();
    }

    public byte SlaveId { get; }

    public IReadOnlyList<ProbeAttempt> Attempts { get; }

    /// <summary>Time spent on this ID, including flushing and inter-request delays but excluding pauses.</summary>
    public TimeSpan Duration { get; }

    /// <summary>The attempt with the best status (OK > Exception > Wrong ID > Garbled > No response).</summary>
    public ProbeAttempt BestAttempt { get; }

    public ProbeStatus Status => BestAttempt.Status;

    public bool IsFound => Status.IsFound();

    /// <summary>True when attempts disagreed, e.g. one garbled and one OK.</summary>
    public bool IsInconsistent { get; }

    /// <summary>Late bytes of all attempts, in order.</summary>
    public ReadOnlyMemory<byte> LateBytes { get; }

    /// <summary>Garbled, wrong ID, inconsistent attempts, or late bytes that suggest a slow device.</summary>
    public bool IsProblematic =>
        Status is ProbeStatus.Garbled or ProbeStatus.WrongId || IsInconsistent || !LateBytes.IsEmpty;

    public IReadOnlyList<string> Notes { get; }

    private string[] BuildNotes()
    {
        var notes = new List<string>();
        if (BestAttempt.Classification.Detail is { } detail)
        {
            notes.Add(detail);
        }

        if (IsInconsistent)
        {
            notes.Add("Inconsistent attempts: " + string.Join(", ", Attempts.Select(a => a.Status.GetDisplayName())) + ".");
        }

        foreach (var attempt in Attempts)
        {
            var suffix = Attempts.Count > 1 ? $" (attempt {attempt.Number})" : string.Empty;
            if (!attempt.StaleBytes.IsEmpty)
            {
                notes.Add($"{attempt.StaleBytes.Length} stale byte(s) discarded before the request{suffix}.");
            }

            if (!attempt.LateBytes.IsEmpty)
            {
                notes.Add($"{attempt.LateBytes.Length} late byte(s) after the response window{suffix}.");
            }

            if (attempt.Response.Length >= Transport.RtuFrameReader.MaxFrameLength)
            {
                notes.Add($"Response cut at {Transport.RtuFrameReader.MaxFrameLength} bytes{suffix}.");
            }
        }

        return notes.ToArray();
    }
}

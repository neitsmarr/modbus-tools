using ModbusTools.Core.Protocol;
using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.Crawling;

/// <summary>
/// Decides which reads a crawl sends for one table and in which order. A strategy is an immutable description that
/// can be shown in the UI and exports; the decisions for a table, and any state they need, live in the
/// <see cref="ICrawlPlanner"/> it creates.
/// </summary>
/// <remarks>
/// Crawling options and heuristics (block reads, bisection, skipping ahead after invalid addresses, ...) are added as
/// new strategies. They only change which read comes next: executing reads, recording results, statistics, the UI and
/// the exports are shared by all strategies.
/// </remarks>
public interface ICrawlStrategy
{
    /// <summary>Stable identifier used in exports, e.g. "sequential-ascending".</summary>
    string Id { get; }

    /// <summary>Short name for the UI.</summary>
    string Name { get; }

    /// <summary>One-sentence explanation for the UI.</summary>
    string Description { get; }

    /// <summary>Fewest and most requests the strategy can send to crawl <paramref name="range"/> of one table.</summary>
    RequestEstimate EstimateRequests(AddressRange range);

    ICrawlPlanner CreatePlanner(CrawlTable table, AddressRange range);
}

/// <summary>Plans the reads for one table; created per table by <see cref="ICrawlStrategy.CreatePlanner"/>.</summary>
public interface ICrawlPlanner
{
    /// <summary>
    /// The next read to send, or null when the table is done. After a read is returned, <see cref="OnCompleted"/> is
    /// called with its outcome before this method is called again.
    /// </summary>
    CrawlRead? Next();

    /// <summary>Reports the outcome of the read last returned by <see cref="Next"/>.</summary>
    void OnCompleted(CrawlReadResult result);
}

/// <summary>A read of <paramref name="Quantity"/> consecutive addresses starting at <paramref name="Address"/>.</summary>
public readonly record struct CrawlRead(ushort Address, ushort Quantity = 1)
{
    public int LastAddress => Address + Quantity - 1;

    public override string ToString() => Quantity == 1 ? $"{Address}" : $"{Address}-{LastAddress}";
}

/// <summary>A read that has been carried out, with the transaction that carried it out.</summary>
public sealed record CrawlReadResult(CrawlTable Table, CrawlRead Read, ProbeAttempt Attempt)
{
    public ProbeStatus Status => Attempt.Status;
}

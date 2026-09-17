using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.Crawling;

public enum CrawlDirection
{
    /// <summary>From the start of the range up.</summary>
    Ascending,

    /// <summary>From the end of the range down.</summary>
    Descending,
}

/// <summary>Reads every address of the range on its own, exactly once, whatever the device answers.</summary>
public sealed class SequentialCrawlStrategy : ICrawlStrategy
{
    private SequentialCrawlStrategy(CrawlDirection direction)
    {
        Direction = direction;
    }

    public static SequentialCrawlStrategy Ascending { get; } = new(CrawlDirection.Ascending);

    public static SequentialCrawlStrategy Descending { get; } = new(CrawlDirection.Descending);

    public CrawlDirection Direction { get; }

    public string Id => Direction == CrawlDirection.Ascending ? "sequential-ascending" : "sequential-descending";

    public string Name => Direction == CrawlDirection.Ascending ? "One by one, from start" : "One by one, from end";

    public string Description => Direction == CrawlDirection.Ascending
        ? "Reads each address on its own, from the lowest address up."
        : "Reads each address on its own, from the highest address down.";

    public RequestEstimate EstimateRequests(AddressRange range) => RequestEstimate.Exactly(range.Count);

    public ICrawlPlanner CreatePlanner(CrawlTable table, AddressRange range) => new Planner(range, Direction);

    private sealed class Planner(AddressRange range, CrawlDirection direction) : ICrawlPlanner
    {
        private int sent;

        public CrawlRead? Next()
        {
            if (sent == range.Count)
            {
                return null;
            }

            var address = direction == CrawlDirection.Ascending ? range.From + sent : range.To - sent;
            sent++;
            return new CrawlRead((ushort)address);
        }

        public void OnCompleted(CrawlReadResult result)
        {
        }
    }
}

/// <summary>The strategies offered to the user, in display order.</summary>
public static class CrawlStrategies
{
    public static IReadOnlyList<ICrawlStrategy> All { get; } =
        [SequentialCrawlStrategy.Ascending, SequentialCrawlStrategy.Descending];

    public static ICrawlStrategy? Find(string? id) => All.FirstOrDefault(strategy => strategy.Id == id);
}

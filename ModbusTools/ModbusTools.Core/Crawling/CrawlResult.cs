namespace ModbusTools.Core.Crawling;

/// <summary>Results of a crawl: per-address results for each crawled table, and statistics over all requests.</summary>
public sealed class CrawlResult
{
    private readonly CrawlTableResult[] tables;

    public CrawlResult(IReadOnlyList<CrawlTable> tables, AddressRange range)
    {
        ArgumentNullException.ThrowIfNull(tables);
        Range = range;
        this.tables = tables.Select(table => new CrawlTableResult(table, range)).ToArray();
    }

    public AddressRange Range { get; }

    /// <summary>One result per crawled table, in crawl order.</summary>
    public IReadOnlyList<CrawlTableResult> Tables => tables;

    public CrawlStatistics Statistics { get; } = new();

    public int TotalAddresses => Range.Count * tables.Length;

    public int CheckedCount => tables.Sum(table => table.CheckedCount);

    public CrawlTableResult? Find(CrawlTable table) => tables.FirstOrDefault(result => result.Table == table);

    public void Record(CrawlReadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var table = Find(result.Table) ?? throw new ArgumentException($"{result.Table} is not part of this crawl.", nameof(result));
        table.Record(result);
        Statistics.Record(result.Attempt);
    }
}

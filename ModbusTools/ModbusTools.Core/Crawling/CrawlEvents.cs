namespace ModbusTools.Core.Crawling;

public enum CrawlOutcome
{
    Completed,
    Cancelled,
    Failed,
}

/// <param name="CheckedAddresses">Addresses whose status is known, over all tables.</param>
/// <param name="TotalAddresses">Addresses in the crawl: range size times table count.</param>
/// <param name="Requests">Requests sent so far.</param>
/// <param name="EstimatedRemaining">Moving-average estimate for the unchecked addresses; null before the first read.</param>
public sealed record CrawlProgress(int CheckedAddresses, int TotalAddresses, int Requests, TimeSpan? EstimatedRemaining)
{
    public int RemainingAddresses => TotalAddresses - CheckedAddresses;
}

public abstract record CrawlEvent;

/// <summary>Reads of <paramref name="Table"/> are about to start.</summary>
public sealed record CrawlTableStartedEvent(CrawlTable Table) : CrawlEvent;

/// <summary><paramref name="Read"/> of <paramref name="Table"/> is about to be sent.</summary>
public sealed record CrawlReadStartedEvent(CrawlTable Table, CrawlRead Read) : CrawlEvent;

/// <summary>A read is finished, including flushing of late bytes and the inter-request delay.</summary>
/// <param name="Duration">Time the transaction took; pauses happen between transactions and are not included.</param>
public sealed record CrawlReadCompletedEvent(CrawlReadResult Result, TimeSpan Duration) : CrawlEvent;

/// <summary>Every table has been crawled. Cancellation and failures surface as exceptions instead.</summary>
public sealed record CrawlFinishedEvent : CrawlEvent;

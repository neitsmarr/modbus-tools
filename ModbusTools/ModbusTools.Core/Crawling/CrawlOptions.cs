using ModbusTools.Core.Scanning;
using ModbusTools.Core.Serial;

namespace ModbusTools.Core.Crawling;

/// <summary>Immutable configuration of one crawl of a single slave.</summary>
public sealed record CrawlOptions
{
    public required SerialSettings Serial { get; init; }

    /// <summary>Address of the slave to crawl. Address 0 (broadcast) is not allowed.</summary>
    public required byte SlaveId
    {
        get;
        init
        {
            if (value == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(SlaveId), value, "Address 0 is broadcast and cannot be crawled.");
            }

            field = value;
        }
    }

    /// <summary>Tables to crawl, in order. At least one, without duplicates.</summary>
    public required IReadOnlyList<CrawlTable> Tables
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Count == 0)
            {
                throw new ArgumentException("At least one table is required.", nameof(Tables));
            }

            if (value.Any(table => !Enum.IsDefined(table)) || value.Distinct().Count() != value.Count)
            {
                throw new ArgumentException("Tables must be defined and must not repeat.", nameof(Tables));
            }

            field = value.ToArray();
        }
    }

    /// <summary>Addresses to crawl in every table.</summary>
    public required AddressRange Range { get; init; }

    public required ICrawlStrategy Strategy { get; init; }

    public TimeSpan ResponseTimeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = SlaveScanOptions.DefaultResponseTimeout;

    /// <summary>Pause after each transaction before the next request is sent.</summary>
    public TimeSpan InterRequestDelay
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = SlaveScanOptions.DefaultInterRequestDelay;

    /// <summary>
    /// Silence that ends a received frame. Null uses <see cref="RtuTiming.DefaultFrameGap"/>; see
    /// <see cref="RtuTiming.MinimumFrameGap"/> for why this is longer than t3.5.
    /// </summary>
    public TimeSpan? FrameGap
    {
        get;
        init
        {
            if (value is TimeSpan gap)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(gap, TimeSpan.Zero);
            }

            field = value;
        }
    }

    /// <summary>Upper bound on waiting for the line to go silent after a transaction.</summary>
    public TimeSpan MaxFlushDuration
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = SlaveScanOptions.DefaultMaxFlushDuration;

    public TimeSpan EffectiveFrameGap => FrameGap ?? RtuTiming.DefaultFrameGap(Serial);

    public TransactionTiming Timing => new(Serial, EffectiveFrameGap, InterRequestDelay, MaxFlushDuration);

    /// <summary>Requests the strategy can send over all tables.</summary>
    public RequestEstimate EstimateRequests() =>
        Tables.Aggregate(default(RequestEstimate), (total, _) => total + Strategy.EstimateRequests(Range));

    /// <summary>
    /// Estimated duration if no request is answered: the strategy's maximum number of requests, each waiting for the
    /// request to be sent, the full timeout, one frame gap of flushing and the inter-request delay. Transport and
    /// browser timer overhead come on top, so real crawls can take somewhat longer.
    /// </summary>
    public TimeSpan EstimateWorstCaseDuration()
    {
        var requestLength = Tables[0].CreateReadProbe(Range.From, 1).BuildFrame(SlaveId).Length;
        var perRequest = RtuTiming.TransmissionTime(Serial, requestLength) + ResponseTimeout + EffectiveFrameGap + InterRequestDelay;
        return perRequest * EstimateRequests().Maximum;
    }
}

using System.Runtime.CompilerServices;
using ModbusTools.Core.Scanning;
using ModbusTools.Core.Transport;

namespace ModbusTools.Core.Crawling;

/// <summary>
/// Crawls the data tables of one slave with read requests only, table by table, sending the reads chosen by the
/// options' <see cref="ICrawlStrategy"/> and reporting each one as it completes.
/// </summary>
/// <remarks>
/// The transport is owned by the caller and must already be open with <see cref="CrawlOptions.Serial"/>.
/// Transactions follow the flush discipline of <see cref="ProbeTransactionRunner"/>. Pause and cancellation are only
/// honoured between transactions, never while a frame is being sent or received.
/// </remarks>
public sealed class RegisterMapCrawler
{
    private readonly IModbusRtuTransport transport;
    private readonly TimeProvider timeProvider;
    private readonly ProbeTransactionRunner runner;

    public RegisterMapCrawler(IModbusRtuTransport transport, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.transport = transport;
        this.timeProvider = timeProvider;
        runner = new ProbeTransactionRunner(transport, timeProvider);
    }

    /// <summary>
    /// Runs the crawl. Ends with a <see cref="CrawlFinishedEvent"/>, or throws <see cref="OperationCanceledException"/>
    /// when <paramref name="cancellationToken"/> is cancelled, or the transport's exception when the port fails.
    /// </summary>
    public async IAsyncEnumerable<CrawlEvent> RunAsync(
        CrawlOptions options,
        PauseToken pauseToken = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!transport.IsOpen)
        {
            throw new InvalidOperationException("The transport must be opened with the crawl's serial settings first.");
        }

        var timing = options.Timing;
        await transport.DiscardInputAsync(cancellationToken);

        foreach (var table in options.Tables)
        {
            yield return new CrawlTableStartedEvent(table);
            var planner = options.Strategy.CreatePlanner(table, options.Range);

            while (true)
            {
                await runner.WaitBetweenTransactionsAsync(pauseToken, cancellationToken);
                if (planner.Next() is not CrawlRead read)
                {
                    break;
                }

                if (!options.Range.Contains(read.Address) || !options.Range.Contains(read.LastAddress) ||
                    read.Quantity > table.GetMaxQuantity())
                {
                    throw new InvalidOperationException(
                        $"Strategy '{options.Strategy.Id}' planned read {read} of {table}, outside {options.Range} or above the quantity limit.");
                }

                yield return new CrawlReadStartedEvent(table, read);

                var started = timeProvider.GetTimestamp();
                var probe = table.CreateReadProbe(read.Address, read.Quantity);
                var attempt = await runner.ExecuteAsync(probe, options.SlaveId, options.ResponseTimeout, timing);
                var result = new CrawlReadResult(table, read, attempt);
                planner.OnCompleted(result);

                yield return new CrawlReadCompletedEvent(result, timeProvider.GetElapsedTime(started));
            }
        }

        yield return new CrawlFinishedEvent();
    }
}

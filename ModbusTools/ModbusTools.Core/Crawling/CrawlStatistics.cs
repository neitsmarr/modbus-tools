using ModbusTools.Core.Protocol;
using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.Crawling;

/// <summary>Counters over every request a crawl sent, and the device's response times.</summary>
public sealed class CrawlStatistics
{
    /// <summary>Response times are kept in 1 ms buckets up to this value; slower responses share the last bucket.</summary>
    public const int ResponseTimeHistogramMaxMs = 2000;

    private readonly int[] statusCounts = new int[Enum.GetValues<ProbeStatus>().Length];
    private readonly int[] exceptionCounts = new int[byte.MaxValue + 1];
    private readonly int[] responseTimeHistogram = new int[ResponseTimeHistogramMaxMs + 1];

    public int Requests { get; private set; }

    public int CountOf(ProbeStatus status) => statusCounts[(int)status];

    public int Exceptions => CountOf(ProbeStatus.Exception);

    public int Timeouts => CountOf(ProbeStatus.NoResponse);

    /// <summary>Garbled replies and replies from another slave ID.</summary>
    public int CommErrors => CountOf(ProbeStatus.Garbled) + CountOf(ProbeStatus.WrongId);

    /// <summary>Requests the device answered with a well-formed reply (OK or exception).</summary>
    public int ResponseTimeSamples { get; private set; }

    public TimeSpan? FastestResponse { get; private set; }

    public TimeSpan? SlowestResponse { get; private set; }

    /// <summary>Median response time of well-formed replies, to 1 ms resolution; null before the first reply.</summary>
    public TimeSpan? MedianResponseTime
    {
        get
        {
            if (ResponseTimeSamples == 0)
            {
                return null;
            }

            var middle = (ResponseTimeSamples + 1) / 2;
            var seen = 0;
            for (var bucket = 0; bucket < responseTimeHistogram.Length; bucket++)
            {
                seen += responseTimeHistogram[bucket];
                if (seen >= middle)
                {
                    return TimeSpan.FromMilliseconds(bucket + 0.5);
                }
            }

            return TimeSpan.FromMilliseconds(ResponseTimeHistogramMaxMs);
        }
    }

    /// <summary>Exception codes received, with how often, in ascending code order.</summary>
    public IReadOnlyList<(byte Code, int Count)> GetExceptionCounts()
    {
        var counts = new List<(byte Code, int Count)>();
        for (var code = 0; code < exceptionCounts.Length; code++)
        {
            if (exceptionCounts[code] > 0)
            {
                counts.Add(((byte)code, exceptionCounts[code]));
            }
        }

        return counts;
    }

    public void Record(ProbeAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        Requests++;
        statusCounts[(int)attempt.Status]++;
        if (attempt.Classification.ExceptionCode is byte code)
        {
            exceptionCounts[code]++;
        }

        if (attempt.Status.IsFound() && attempt.ResponseTime is TimeSpan responseTime)
        {
            ResponseTimeSamples++;
            var bucket = (int)Math.Min(responseTime.TotalMilliseconds, ResponseTimeHistogramMaxMs);
            responseTimeHistogram[bucket]++;
            FastestResponse = FastestResponse is TimeSpan fastest && fastest <= responseTime ? fastest : responseTime;
            SlowestResponse = SlowestResponse is TimeSpan slowest && slowest >= responseTime ? slowest : responseTime;
        }
    }
}

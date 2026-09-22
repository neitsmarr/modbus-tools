using ModbusTools.Core.Protocol;
using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>Results of a sweep: one <see cref="BaudRateResult"/> per rate tried, in ascending rate order.</summary>
public sealed class BaudSweepResult
{
    private readonly SortedDictionary<int, BaudRateResult> rates = [];
    private readonly List<BaudVisit> visits = [];

    private int windowVersion = -1;
    private BaudWindow? window;
    private int edgeChecksVersion = -1;
    private IReadOnlyList<BaudEdgeCheck>? edgeChecks;

    public BaudSweepResult(BaudGrid grid)
    {
        Grid = grid;
    }

    public BaudGrid Grid { get; }

    /// <summary>Incremented whenever a result is recorded, so views can cache what they derive from it.</summary>
    public int Version { get; private set; }

    /// <summary>Every rate tried or refused, by ascending baud rate.</summary>
    public IReadOnlyCollection<BaudRateResult> Rates => rates.Values;

    public BaudSweepStatistics Statistics { get; } = new();

    /// <summary>Every rate visit in order, so passes over the same rates can be told apart.</summary>
    public IReadOnlyList<BaudVisit> Visits => visits;

    /// <summary>Rates that have had at least one request.</summary>
    public int TestedRates => rates.Values.Count(rate => rate.Requests > 0);

    public int RefusedRates => rates.Values.Count(rate => rate.IsPortRefused);

    public BaudRateResult? Find(int baudRate) => rates.GetValueOrDefault(baudRate);

    public BaudRateResult GetOrAdd(int baudRate)
    {
        if (rates.TryGetValue(baudRate, out var existing))
        {
            return existing;
        }

        var rate = new BaudRateResult(baudRate, Grid.PercentOf(baudRate));
        rates.Add(baudRate, rate);
        Version++;
        return rate;
    }

    public void Record(int baudRate, ProbeAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        GetOrAdd(baudRate).Record(attempt);
        Statistics.Record(attempt);
        Version++;
    }

    public void RecordPortError(int baudRate, string message)
    {
        GetOrAdd(baudRate).RecordPortError(message);
        Version++;
    }

    /// <summary>Records a finished visit; its requests are recorded one by one with <see cref="Record"/>.</summary>
    public void RecordVisit(BaudVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        visits.Add(visit);
        Version++;
    }

    /// <summary>The working window as the results stand; recomputed only after new results.</summary>
    public BaudWindow GetWindow()
    {
        if (windowVersion != Version || window is null)
        {
            window = BaudWindow.Analyse(this);
            windowVersion = Version;
        }

        return window;
    }

    /// <summary>What the edge passes say about each edge; recomputed only after new results.</summary>
    public IReadOnlyList<BaudEdgeCheck> GetEdgeChecks()
    {
        if (edgeChecksVersion != Version || edgeChecks is null)
        {
            edgeChecks = BaudEdgeCheck.Analyse(this);
            edgeChecksVersion = Version;
        }

        return edgeChecks;
    }
}

/// <summary>One visit to one rate: the step as planned, and what its requests achieved.</summary>
/// <param name="Requests">Requests sent; none when the port refused the rate.</param>
public sealed record BaudVisit(BaudSweepStep Step, int Requests, int Answered)
{
    public RateVotes Votes => new(Step.BaudRate, Answered, Requests);
}

/// <summary>Counters over every request a sweep sent, whatever rate it was sent at.</summary>
public sealed class BaudSweepStatistics
{
    private readonly int[] statusCounts = new int[Enum.GetValues<ReplyStatus>().Length];

    private TimeSpan responseTimeTotal;

    public int Requests { get; private set; }

    /// <summary>Framing, parity, break and overrun errors; expected on the failing side of a sweep.</summary>
    public int LineErrors { get; private set; }

    public int ResponseTimeSamples { get; private set; }

    public TimeSpan? FastestResponse { get; private set; }

    public TimeSpan? SlowestResponse { get; private set; }

    public TimeSpan? AverageResponseTime =>
        ResponseTimeSamples == 0 ? null : responseTimeTotal / ResponseTimeSamples;

    public int CountOf(ReplyStatus status) => statusCounts[(int)status];

    public void Record(ProbeAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        Requests++;
        statusCounts[(int)attempt.GetReplyStatus()]++;
        LineErrors += attempt.LineErrors;
        if (attempt.Status.IsFound() && attempt.ResponseTime is TimeSpan responseTime)
        {
            ResponseTimeSamples++;
            responseTimeTotal += responseTime;
            FastestResponse = FastestResponse is TimeSpan fastest && fastest <= responseTime ? fastest : responseTime;
            SlowestResponse = SlowestResponse is TimeSpan slowest && slowest >= responseTime ? slowest : responseTime;
        }
    }
}

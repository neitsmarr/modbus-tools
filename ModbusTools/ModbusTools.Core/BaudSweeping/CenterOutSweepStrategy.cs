using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// Visits each rate once, working outward from the expected rate and alternating sides, and sends all of
/// <see cref="BaudSweepOptions.RequestsPerRate"/> in that visit.
/// </summary>
/// <remarks>
/// <para>
/// A side stops once a whole dead band - <see cref="BaudSweepOptions.DeadBandPercent"/> worth of consecutive rates -
/// has produced no reply, so a sweep only pays for one dead band past each edge instead of the whole span.
/// </para>
/// <para>
/// Every rate costs a port reopening, which takes about as long as a transaction, so one visit per rate is what
/// keeps the sweep fast. It also means only the first request of each visit follows the reopening, whatever it does
/// to the line. What it gives up is time: requests in one visit see the same conditions, so near an edge they tend to
/// agree with each other.
/// </para>
/// </remarks>
public sealed class CenterOutSweepStrategy : IBaudSweepStrategy
{
    private CenterOutSweepStrategy()
    {
    }

    public static CenterOutSweepStrategy Instance { get; } = new();

    public string Id => "center-out";

    public string Name => "Outward, step by step";

    public string Description =>
        "Starts at the expected rate and steps outward in both directions, sending all of a rate's requests in one " +
        "visit, and stops going further out once a dead band's worth of rates in a row has not answered. Every rate " +
        "in the window gets tested, so the chart shows its whole shape.";

    public string RequestsNote => "Requests sent at each rate in one visit.";

    public string NothingAnsweredNote =>
        "the outward sweep gives up one dead band either side of it, so a device further off is never reached. " +
        "Random order searches the whole span.";

    public BaudSweepEstimate Estimate(BaudSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Best case: nothing answers, so the sweep stops one dead band out on both sides. Worst case: the whole grid.
        var shortestSweep = Math.Min(1 + 2 * options.DeadBandSteps, options.Grid.Count);
        var mostVisits = options.Grid.Count;
        return new BaudSweepEstimate(
            new RequestEstimate(options.RequestsPerRate * shortestSweep, options.RequestsPerRate * mostVisits),
            mostVisits);
    }

    public IBaudSweepPlanner CreatePlanner(BaudSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Planner(options);
    }

    private sealed class Planner(BaudSweepOptions options) : IBaudSweepPlanner
    {
        private readonly BaudGrid grid = options.Grid;
        private readonly int deadBandSteps = options.DeadBandSteps;

        private bool centerDone;
        private int step = 1;
        private int slot;
        private int deadRunLow;
        private int deadRunHigh;
        private bool lowClosed;
        private bool highClosed;
        private int lowReach;
        private int highReach;
        private int pending;
        private int requestsPlanned;

        /// <summary>
        /// Assumes every side still open is about to reach its edge: it still needs a dead band of rates without a
        /// reply, or the rest of the span if that is shorter. Exact once both sides have stopped.
        /// </summary>
        public int? ExpectedRequests => requestsPlanned + OutwardRatesLeft() * options.RequestsPerRate;

        public BaudSweepStep? Next()
        {
            if (!TryTakeIndex(out var index))
            {
                return null;
            }

            pending = index;
            requestsPlanned += options.RequestsPerRate;
            highReach = Math.Max(highReach, index);
            lowReach = Math.Max(lowReach, -index);
            return new BaudSweepStep(grid.RateAt(index), options.RequestsPerRate);
        }

        public void OnCompleted(BaudSweepStepResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            var dead = result.IsDead;
            if (pending >= 0)
            {
                deadRunHigh = dead ? deadRunHigh + 1 : 0;
                highClosed |= deadRunHigh >= deadBandSteps;
            }

            if (pending <= 0)
            {
                deadRunLow = dead ? deadRunLow + 1 : 0;
                lowClosed |= deadRunLow >= deadBandSteps;
            }
        }

        private int OutwardRatesLeft() =>
            RatesLeft(lowClosed, deadRunLow, lowReach) + RatesLeft(highClosed, deadRunHigh, highReach);

        /// <summary>
        /// The fewest rates a side still has to visit: the rest of its dead band, unless the span ends first.
        /// </summary>
        private int RatesLeft(bool closed, int deadRun, int reach) =>
            closed ? 0 : Math.Min(deadBandSteps - deadRun, grid.MaxIndex - reach);

        /// <summary>Walks outward: the expected rate first, then +1, -1, +2, -2 and so on, skipping closed sides.</summary>
        private bool TryTakeIndex(out int index)
        {
            index = 0;
            if (!centerDone)
            {
                centerDone = true;
                return true;
            }

            while (step <= grid.MaxIndex && !(lowClosed && highClosed))
            {
                if (slot == 0)
                {
                    slot = 1;
                    if (!highClosed)
                    {
                        index = step;
                        return true;
                    }
                }

                if (slot == 1)
                {
                    slot = 2;
                    if (!lowClosed)
                    {
                        index = -step;
                        return true;
                    }
                }

                step++;
                slot = 0;
            }

            return false;
        }
    }
}

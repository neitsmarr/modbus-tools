using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// Visits each rate once, working outward from the expected rate and alternating sides, and sends all of
/// <see cref="BaudSweepOptions.RequestsPerRate"/> in that visit. Then it sweeps each edge it found again,
/// <see cref="BaudSweepOptions.VerifyEdgePasses"/> times, to check that the edges have not moved.
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
/// agree with each other. The edge passes put that back where it matters. Each sweeps from half a dead band below an
/// edge to half a dead band above it, as the results stand when the pass starts, so it follows an edge that has moved.
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

    public string RequestsNote => "Requests sent at each rate in one visit, and again at every rate an edge pass covers.";

    public string NothingAnsweredNote =>
        "the outward sweep gives up one dead band either side of it, so a device further off is never reached. " +
        "Random order searches the whole span.";

    public string VerifyEdgesNote =>
        "After the outward sweep, sweeps each edge again from half a dead band below it to half a dead band above " +
        "it, to check that it has not moved since.";

    public BaudSweepEstimate Estimate(BaudSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Best case: nothing answers, so the sweep stops one dead band out on both sides and has no edge to verify.
        // Worst case: the whole grid, then both edges on every edge pass.
        var shortestSweep = Math.Min(1 + 2 * options.DeadBandSteps, options.Grid.Count);
        var edgePass = Math.Min(2 * (options.DeadBandSteps + 1), options.Grid.Count);
        var mostVisits = options.Grid.Count + options.VerifyEdgePasses * edgePass;
        return new BaudSweepEstimate(
            new RequestEstimate(options.RequestsPerRate * shortestSweep, options.RequestsPerRate * mostVisits),
            mostVisits);
    }

    public IBaudSweepPlanner CreatePlanner(BaudSweepOptions options, BaudSweepResult results)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(results);
        return new Planner(options, results);
    }

    private sealed class Planner(BaudSweepOptions options, BaudSweepResult results) : IBaudSweepPlanner
    {
        private readonly BaudGrid grid = options.Grid;
        private readonly int deadBandSteps = options.DeadBandSteps;
        private readonly Queue<int> edgePass = [];

        private bool centerDone;
        private int step = 1;
        private int slot;
        private int deadRunLow;
        private int deadRunHigh;
        private bool lowClosed;
        private bool highClosed;
        private bool outwardDone;
        private int edgePassesStarted;
        private int pending;
        private int requestsPlanned;

        /// <summary>Known once the edge passes start; assumes the ones still to come cover as many rates as this one.</summary>
        public int? ExpectedRequests { get; private set; }

        public BaudSweepStep? Next()
        {
            if (!outwardDone)
            {
                if (TryTakeIndex(out var index))
                {
                    return Take(index);
                }

                outwardDone = true;
            }

            if (edgePass.Count == 0 && !TryStartEdgePass())
            {
                return null;
            }

            return Take(edgePass.Dequeue());
        }

        public void OnCompleted(BaudSweepStepResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (outwardDone)
            {
                return;
            }

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

        private BaudSweepStep Take(int index)
        {
            pending = index;
            requestsPlanned += options.RequestsPerRate;
            return new BaudSweepStep(grid.RateAt(index), options.RequestsPerRate);
        }

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

        /// <summary>
        /// Queues a sweep over each edge found so far, from half a dead band below it to half a dead band above it.
        /// Returns false when the passes are used up or there is no edge to verify.
        /// </summary>
        private bool TryStartEdgePass()
        {
            if (edgePassesStarted >= options.VerifyEdgePasses)
            {
                return false;
            }

            var window = results.GetWindow();

            // A window narrower than the dead band puts both edges' rates on the same steps.
            var rates = EdgeRates(window.Lower).Concat(EdgeRates(window.Upper)).Distinct().ToList();
            if (rates.Count == 0)
            {
                ExpectedRequests = requestsPlanned;
                return false;
            }

            edgePassesStarted++;
            rates.ForEach(edgePass.Enqueue);
            var passesLeft = options.VerifyEdgePasses - edgePassesStarted + 1;
            ExpectedRequests = requestsPlanned + passesLeft * rates.Count * options.RequestsPerRate;
            return true;
        }

        private IEnumerable<int> EdgeRates(BaudWindowEdge? edge)
        {
            if (edge is not BaudWindowEdge found)
            {
                return [];
            }

            var halfDeadBand = options.DeadBandPercent / 2;
            return grid.IndicesBetween(found.OffsetPercent - halfDeadBand, found.OffsetPercent + halfDeadBand);
        }
    }
}

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
/// Every visit is labelled with its pass, so <see cref="BaudEdgeCheck"/> can compare each pass with the outward sweep.
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
        var mostVisits = options.Grid.Count + options.VerifyEdgePasses * MostEdgePassRates(options);
        return new BaudSweepEstimate(
            new RequestEstimate(options.RequestsPerRate * shortestSweep, options.RequestsPerRate * mostVisits),
            mostVisits);
    }

    /// <summary>Most rates one edge pass can cover: a dead band around each of the two edges.</summary>
    private static int MostEdgePassRates(BaudSweepOptions options) =>
        Math.Min(2 * (options.DeadBandSteps + 1), options.Grid.Count);

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
        private readonly Queue<(int Index, BaudEdgeSide Side)> edgePass = [];

        private bool centerDone;
        private int step = 1;
        private int slot;
        private int deadRunLow;
        private int deadRunHigh;
        private bool lowClosed;
        private bool highClosed;
        private int lowReach;
        private int highReach;
        private bool outwardDone;
        private int edgePassesStarted;
        private int pending;
        private int requestsPlanned;
        private int? expectedWithEdgePasses;

        /// <summary>
        /// During the outward sweep, assumes every side still open is about to reach its edge - it still needs a dead
        /// band of rates without a reply, or the rest of the span if that is shorter - and that the edge passes then
        /// cover both edges. Once they start, the passes still to come are assumed to cover as many rates as this one.
        /// </summary>
        public int? ExpectedRequests => outwardDone
            ? expectedWithEdgePasses
            : requestsPlanned +
              (OutwardRatesLeft() + options.VerifyEdgePasses * MostEdgePassRates(options)) * options.RequestsPerRate;

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

            var (next, side) = edgePass.Dequeue();
            return Take(next, new BaudEdgePass(edgePassesStarted, side));
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

        private BaudSweepStep Take(int index, BaudEdgePass? pass = null)
        {
            pending = index;
            requestsPlanned += options.RequestsPerRate;
            if (pass is null)
            {
                highReach = Math.Max(highReach, index);
                lowReach = Math.Max(lowReach, -index);
            }

            return new BaudSweepStep(grid.RateAt(index), options.RequestsPerRate, pass);
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

        /// <summary>
        /// Queues a sweep over each edge found so far, from half a dead band below it to half a dead band above it.
        /// Returns false when the passes are used up or there is no edge to verify.
        /// </summary>
        private bool TryStartEdgePass()
        {
            if (edgePassesStarted >= options.VerifyEdgePasses)
            {
                expectedWithEdgePasses ??= requestsPlanned;
                return false;
            }

            // Each edge is checked on its own, even where a narrow window makes the two ranges overlap, so every pass
            // has a complete set of votes for each edge.
            var window = results.GetWindow();
            var rates = EdgeRates(window.Lower).Select(index => (index, BaudEdgeSide.Lower))
                .Concat(EdgeRates(window.Upper).Select(index => (index, BaudEdgeSide.Upper)))
                .ToList();
            if (rates.Count == 0)
            {
                expectedWithEdgePasses = requestsPlanned;
                return false;
            }

            edgePassesStarted++;
            rates.ForEach(edgePass.Enqueue);
            var passesLeft = options.VerifyEdgePasses - edgePassesStarted + 1;
            expectedWithEdgePasses = requestsPlanned + passesLeft * rates.Count * options.RequestsPerRate;
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

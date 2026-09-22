using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// Tests the expected rate, then on each side halves the gap between the last rate that worked and the first that
/// did not until the two are one grid step apart. Every rate it visits gets all of its requests at once.
/// </summary>
/// <remarks>
/// This is bisection: each probe halves what is still unknown about an edge, so an edge costs a handful of rates
/// instead of a rate every step of the way. The expected rate and the span are the whole starting bracket: the rate
/// just past the end of the span counts as failing, so the end itself is only tested once everything inside it has
/// answered, and if it answers too the edge is beyond the sweep. It is the cheapest way to the numbers, and the one
/// that leaves the emptiest chart - it never tests the middle of the window it has already bracketed. If the expected
/// rate itself does not answer, there is nothing to bisect from and the sweep stops there.
/// </remarks>
public sealed class BisectSweepStrategy : IBaudSweepStrategy
{
    private BisectSweepStrategy()
    {
    }

    public static BisectSweepStrategy Instance { get; } = new();

    public string Id => "bisect";

    public string Name => "Bisection";

    public string Description =>
        "Tests the expected rate, then on each side halves the gap between the last rate that answered and the first " +
        "that did not, starting from the end of the span, until the edge is pinned down to one step. Fewest requests " +
        "and fewest port reopenings, but it only tests what it needs, so the chart stays sparse. Needs the expected " +
        "rate itself to answer.";

    public string NothingAnsweredNote =>
        "bisection starts from it and stops when it does not answer. Outward, step by step searches one dead band " +
        "either side of it, and Random order the whole span.";

    public string RequestsNote => "Requests sent at each rate it visits, all in one go, before it decides.";

    public BaudSweepEstimate Estimate(BaudSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Each side starts with a gap of MaxIndex + 1 steps and halves it until one step is left.
        var perSide = (int)Math.Ceiling(Math.Log2(options.Grid.MaxIndex + 1));
        var mostVisits = 1 + 2 * perSide;
        return new BaudSweepEstimate(
            new RequestEstimate(options.RequestsPerRate, options.RequestsPerRate * mostVisits),
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
        private readonly Side low = new(-1, options.Grid.MaxIndex);
        private readonly Side high = new(1, options.Grid.MaxIndex);

        private bool centerDone;
        private bool centerWorking;
        private bool nextIsHigh = true;
        private Side? pending;

        public BaudSweepStep? Next()
        {
            if (!centerDone)
            {
                return new BaudSweepStep(grid.ExpectedBaudRate, options.RequestsPerRate);
            }

            if (!centerWorking)
            {
                return null;
            }

            // Alternate sides so both edges close in together and the chart grows symmetrically.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var side = nextIsHigh ? high : low;
                nextIsHigh = !nextIsHigh;
                if (side.NextIndex() is int index)
                {
                    pending = side;
                    return new BaudSweepStep(grid.RateAt(index), options.RequestsPerRate);
                }
            }

            return null;
        }

        public void OnCompleted(BaudSweepStepResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (!centerDone)
            {
                centerDone = true;
                centerWorking = result.Rate.IsWorking;
                return;
            }

            pending?.Record(result.Rate.IsWorking);
        }

        /// <summary>
        /// One direction of the search: the gap between the last rate that worked, starting at the expected rate, and
        /// the first that did not, starting just past the end of the span.
        /// </summary>
        private sealed class Side(int sign, int maxIndex)
        {
            private int lastWorking;
            private int firstFailing = sign * (maxIndex + 1);

            /// <summary>The index handed out by the last <see cref="NextIndex"/> call.</summary>
            private int pending;

            /// <summary>The middle of the gap, or null once the two ends are neighbouring rates.</summary>
            public int? NextIndex()
            {
                if (Math.Abs(firstFailing - lastWorking) <= 1)
                {
                    return null;
                }

                pending = (lastWorking + firstFailing) / 2;
                return pending;
            }

            public void Record(bool working)
            {
                if (working)
                {
                    lastWorking = pending;
                }
                else
                {
                    firstFailing = pending;
                }
            }
        }
    }
}

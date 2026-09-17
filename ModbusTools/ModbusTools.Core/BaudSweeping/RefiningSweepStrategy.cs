using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// Walks out from the expected rate in coarse steps of <see cref="BaudSweepOptions.DeadBandPercent"/> until a rate
/// fails, then halves the gap between the last rate that worked and the first that did not until the two are one
/// grid step apart. Every rate it visits gets all of its requests at once.
/// </summary>
/// <remarks>
/// This is bisection: each probe after the coarse walk halves what is still unknown about an edge, so the edges cost
/// a handful of rates instead of a rate every step of the way. It is the cheapest way to the numbers, and the one
/// that leaves the emptiest chart - it never tests the middle of the window it has already bracketed. Because a rate
/// is judged from all its requests in one go, the port is opened once per rate rather than once per request.
/// </remarks>
public sealed class RefiningSweepStrategy : IBaudSweepStrategy
{
    private RefiningSweepStrategy()
    {
    }

    public static RefiningSweepStrategy Instance { get; } = new();

    public string Id => "refining";

    public string Name => "Coarse steps, then bisect";

    public string Description =>
        "Steps out in dead-band-sized jumps until the device stops answering, then halves the remaining gap until " +
        "the edge is pinned down to one step. Fewest requests and fewest port reopenings, but it only tests what it " +
        "needs, so the chart stays sparse. If the expected rate itself gets no reply, it first looks for one within " +
        "the dead band.";

    public string RequestsNote => "Requests sent at each rate it visits, all in one go, before it decides.";

    public RequestEstimate EstimateRequests(BaudSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var coarseSteps = options.DeadBandSteps;
        var coarseProbes = (int)Math.Ceiling((double)options.Grid.MaxIndex / coarseSteps) + 1;
        var bisections = (int)Math.Ceiling(Math.Log2(Math.Max(coarseSteps, 1))) + 1;

        // Worst case adds the search for a rate that answers at all, which covers one dead band on each side.
        var rates = 1 + 2 * (coarseSteps + coarseProbes + bisections);
        return new RequestEstimate(options.RequestsPerRate * 3, options.RequestsPerRate * rates);
    }

    public IBaudSweepPlanner CreatePlanner(BaudSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Planner(options);
    }

    private sealed class Planner : IBaudSweepPlanner
    {
        private readonly BaudSweepOptions options;
        private readonly BaudGrid grid;
        private readonly Side low;
        private readonly Side high;

        /// <summary>Rates tried while looking for the first that answers, and whether they did.</summary>
        private readonly Dictionary<int, bool> searched = [];

        private bool searching = true;
        private int searchStep;
        private bool nextIsHigh = true;
        private int pendingIndex;
        private Side? pending;

        public Planner(BaudSweepOptions options)
        {
            this.options = options;
            grid = options.Grid;
            low = new Side(-1, grid.MaxIndex, options.DeadBandSteps);
            high = new Side(1, grid.MaxIndex, options.DeadBandSteps);
        }

        public BaudSweepStep? Next()
        {
            if (searching)
            {
                return SearchStep();
            }

            // Alternate sides so both edges close in together and the chart grows symmetrically.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var side = nextIsHigh ? high : low;
                nextIsHigh = !nextIsHigh;
                if (side.NextIndex() is int index)
                {
                    pending = side;
                    side.Pending = index;
                    return new BaudSweepStep(grid.RateAt(index), options.RequestsPerRate);
                }
            }

            return null;
        }

        public void OnCompleted(BaudSweepStepResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (searching)
            {
                RecordSearch(result.Rate.IsWorking);
                return;
            }

            pending?.Record(result.Rate.IsWorking);
        }

        /// <summary>
        /// The bisection needs one rate that works to start from. The expected rate is the obvious candidate, but a
        /// device far enough off does not answer there, so the search fans out one step at a time - as far as the
        /// dead band allows, since past that the expected rate was simply wrong.
        /// </summary>
        private BaudSweepStep? SearchStep()
        {
            if (searched.Count == 0)
            {
                pendingIndex = 0;
                searched[0] = false;
                return new BaudSweepStep(grid.ExpectedBaudRate, options.RequestsPerRate);
            }

            if (nextIsHigh)
            {
                searchStep++;
            }

            var index = nextIsHigh ? searchStep : -searchStep;
            nextIsHigh = !nextIsHigh;
            if (searchStep > options.DeadBandSteps || !grid.Contains(index))
            {
                searching = false;
                low.GiveUp();
                high.GiveUp();
                return null;
            }

            pendingIndex = index;
            searched[index] = false;
            return new BaudSweepStep(grid.RateAt(index), options.RequestsPerRate);
        }

        private void RecordSearch(bool working)
        {
            searched[pendingIndex] = working;
            if (!working)
            {
                return;
            }

            // Everything tried nearer the expected rate than this already failed, so those failures bracket the
            // window on the other side and the bisection can start from what is known.
            searching = false;
            nextIsHigh = true;
            low.Anchor(pendingIndex, FirstFailing(below: true));
            high.Anchor(pendingIndex, FirstFailing(below: false));
        }

        private int? FirstFailing(bool below)
        {
            var failures = searched
                .Where(rate => !rate.Value && (below ? rate.Key < pendingIndex : rate.Key > pendingIndex))
                .Select(rate => rate.Key);
            return below ? failures.Max(index => (int?)index) : failures.Min(index => (int?)index);
        }

        /// <summary>One direction of the search: coarse steps out, then bisection of the bracketed gap.</summary>
        /// <summary>
        /// One direction of the search: coarse steps out from the last rate that worked, then bisection of the gap
        /// between that rate and the first one that did not.
        /// </summary>
        private sealed class Side(int sign, int maxIndex, int coarseSteps)
        {
            private int lastWorking;
            private int? firstFailing;
            private bool done;

            /// <summary>The index handed out by the last <see cref="NextIndex"/> call.</summary>
            public int Pending { get; set; }

            /// <summary>Starts from a rate known to work, plus the nearest failure already known beyond it, if any.</summary>
            public void Anchor(int working, int? failing)
            {
                lastWorking = working;
                firstFailing = failing;
                Close();
            }

            public int? NextIndex()
            {
                if (done)
                {
                    return null;
                }

                if (firstFailing is int failing)
                {
                    return (lastWorking + failing) / 2;
                }

                var next = lastWorking + sign * coarseSteps;
                if (Math.Abs(next) <= maxIndex)
                {
                    return next;
                }

                // Past the end of the span while still answering: try the very edge, then give up on this side.
                if (Math.Abs(lastWorking) >= maxIndex)
                {
                    done = true;
                    return null;
                }

                return sign * maxIndex;
            }

            public void Record(bool working)
            {
                if (working)
                {
                    if (IsBeyond(Pending, lastWorking))
                    {
                        lastWorking = Pending;
                    }
                }
                else if (firstFailing is null || IsBeyond(firstFailing.Value, Pending))
                {
                    firstFailing = Pending;
                }

                Close();
            }

            public void GiveUp() => done = true;

            /// <summary>Nothing is left to learn once the two ends of the gap are neighbouring rates.</summary>
            private void Close() =>
                done |= firstFailing is int failing && Math.Abs(failing - lastWorking) <= 1;

            /// <summary>Whether <paramref name="index"/> lies further from the expected rate than <paramref name="other"/>.</summary>
            private bool IsBeyond(int index, int other) => sign > 0 ? index > other : index < other;
        }
    }
}

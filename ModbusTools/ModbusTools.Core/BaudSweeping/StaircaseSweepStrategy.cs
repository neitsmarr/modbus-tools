using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// The up-down staircase: from the expected rate, move further out after a reply and back in after a failure,
/// halving the step at every change of direction, until the walk has turned around often enough.
/// </summary>
/// <remarks>
/// <para>
/// This is the standard way of finding a threshold when the answer is noisy - psychophysics calls it an adaptive
/// staircase, hardware validation calls it margining - and an edge here is exactly that: near it, whether a frame
/// survives depends on the bits it carries, so the same rate answers sometimes and not others. The walk settles
/// around the rate that answers half the time, which is the edge the window analysis looks for, and it spends its
/// requests there instead of on rates whose answer is never in doubt.
/// </para>
/// <para>
/// It is the fastest way to both edges, and it draws the least: the middle of the window is never tested.
/// </para>
/// </remarks>
public sealed class StaircaseSweepStrategy : IBaudSweepStrategy
{
    /// <summary>Turnarounds per side before the walk is considered settled, when requests per rate is left small.</summary>
    public const int MinReversals = 4;

    /// <summary>Requests one side may spend before it stops, as a multiple of its reversal target.</summary>
    public const int RequestBudgetPerReversal = 4;

    private StaircaseSweepStrategy()
    {
    }

    public static StaircaseSweepStrategy Instance { get; } = new();

    public string Id => "staircase";

    public string Name => "Up-down staircase";

    public string Description =>
        "Walks outward while the device answers and back in when it does not, halving the step at every turnaround, " +
        "until it circles the rate that answers half the time. The classic threshold search for noisy answers: " +
        "quickest to both edges, but it only ever probes near them.";

    public string RequestsNote => "Turnarounds each side aims for before it stops; more means a tighter edge.";

    public RequestEstimate EstimateRequests(BaudSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var reversals = ReversalTarget(options);
        return new RequestEstimate(2 * reversals, 2 * RequestBudgetPerReversal * reversals);
    }

    public IBaudSweepPlanner CreatePlanner(BaudSweepOptions options, BaudSweepResult results)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Planner(options);
    }

    private static int ReversalTarget(BaudSweepOptions options) => Math.Max(MinReversals, options.RequestsPerRate);

    private sealed class Planner : IBaudSweepPlanner
    {
        private readonly BaudGrid grid;
        private readonly Side low;
        private readonly Side high;

        private bool nextIsHigh = true;
        private Side? pending;

        public Planner(BaudSweepOptions options)
        {
            grid = options.Grid;
            var reversals = ReversalTarget(options);
            var budget = RequestBudgetPerReversal * reversals;
            low = new Side(-1, grid.MaxIndex, options.DeadBandSteps, reversals, budget);
            high = new Side(1, grid.MaxIndex, options.DeadBandSteps, reversals, budget);
        }

        public BaudSweepStep? Next()
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var side = nextIsHigh ? high : low;
                nextIsHigh = !nextIsHigh;
                if (side.NextIndex() is int index)
                {
                    pending = side;
                    return new BaudSweepStep(grid.RateAt(index), 1);
                }
            }

            return null;
        }

        public void OnCompleted(BaudSweepStepResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            // The walk follows this one request, not the rate's history: that is what makes it converge on the rate
            // that answers half the time rather than on the last rate that ever answered.
            pending?.Record(result.Answered > 0);
        }

        private sealed class Side(int sign, int maxIndex, int startStep, int targetReversals, int requestBudget)
        {
            private int index;
            private int step = startStep;
            private bool movingOut = true;
            private int reversals;
            private int requests;
            private bool done;

            public int? NextIndex() => done ? null : index;

            public void Record(bool answered)
            {
                requests++;
                if (answered != movingOut)
                {
                    reversals++;
                    step = Math.Max(1, step / 2);
                    movingOut = answered;
                }

                if (reversals >= targetReversals || requests >= requestBudget)
                {
                    done = true;
                    return;
                }

                if (!answered && index == 0)
                {
                    // The expected rate itself does not answer, so there is no edge to walk towards on this side.
                    done = true;
                    return;
                }

                var next = index + sign * (answered ? step : -step);
                if (Math.Abs(next) > maxIndex)
                {
                    // Still answering at the end of the span: the edge is further out than the sweep goes.
                    done = true;
                    return;
                }

                // Never cross to the other side of the expected rate; that half is the other staircase's job.
                index = sign > 0 ? Math.Max(next, 0) : Math.Min(next, 0);
            }
        }
    }
}

using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// Sends one request per rate per pass, working outward from the expected rate and alternating sides, and repeats
/// the pass until every rate has had <see cref="BaudSweepOptions.RequestsPerRate"/> requests.
/// </summary>
/// <remarks>
/// <para>
/// A side stops for the pass once a whole dead band - <see cref="BaudSweepOptions.DeadBandPercent"/> worth of
/// consecutive rates - has produced no reply, so a sweep only pays for one dead band past each edge instead of the
/// whole span. A rate counts as dead only while none of its requests in any pass has been answered, so a later pass
/// walks past a rate that answered once and the frontier corrects itself.
/// </para>
/// <para>
/// Passes are what make the picture usable early: the first one already shows roughly where the window ends, and
/// every pass after it sharpens the edges. The price is reopening the port for every single request.
/// </para>
/// </remarks>
public sealed class CenterOutSweepStrategy : IBaudSweepStrategy
{
    private CenterOutSweepStrategy()
    {
    }

    public static CenterOutSweepStrategy Instance { get; } = new();

    public string Id => "center-out";

    public string Name => "Outward in passes";

    public string Description =>
        "Starts at the expected rate and steps outward in both directions, one request per rate, then repeats the " +
        "whole pass. Stops going further out once a dead band's worth of rates in a row has not answered. The first " +
        "pass already shows the shape of the window; later passes sharpen the edges.";

    public string RequestsNote => "One request per rate per pass, so this is also the number of passes.";

    public RequestEstimate EstimateRequests(BaudSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Best case: nothing answers, so every pass stops one dead band out on both sides. Worst case: the whole grid.
        var shortestPass = Math.Min(1 + 2 * options.DeadBandSteps, options.Grid.Count);
        return new RequestEstimate(options.RequestsPerRate * shortestPass, options.RequestsPerRate * options.Grid.Count);
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

        private int pass = 1;
        private int passesDone;
        private int requestsInPass;
        private int lastPassRequests;

        private bool centerDone;
        private int step = 1;
        private int slot;
        private int deadRunLow;
        private int deadRunHigh;
        private bool lowClosed;
        private bool highClosed;
        private int pending;

        /// <summary>Assumes the passes still to come cost what the last finished one cost.</summary>
        public int? ExpectedRequests => passesDone == 0 ? null : lastPassRequests * options.RequestsPerRate;

        public BaudSweepStep? Next()
        {
            while (pass <= options.RequestsPerRate)
            {
                if (TryTakeIndex(out var index))
                {
                    pending = index;
                    requestsInPass++;
                    return new BaudSweepStep(grid.RateAt(index), 1);
                }

                StartNextPass();
            }

            return null;
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

        /// <summary>Walks the pass: the expected rate first, then +1, -1, +2, -2 and so on, skipping closed sides.</summary>
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

        private void StartNextPass()
        {
            lastPassRequests = requestsInPass;
            passesDone++;
            pass++;
            requestsInPass = 0;
            centerDone = false;
            step = 1;
            slot = 0;
            deadRunLow = 0;
            deadRunHigh = 0;
            lowClosed = false;
            highClosed = false;
        }
    }
}

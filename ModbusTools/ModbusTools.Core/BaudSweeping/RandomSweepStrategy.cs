using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// Picks the next rate at random among those that still have requests left, until every rate has had
/// <see cref="BaudSweepOptions.RequestsPerRate"/> of them.
/// </summary>
/// <remarks>
/// Sampling in random order keeps anything that changes during the sweep - the device warming up, a load on the bus -
/// from being mistaken for an effect of the rate, which an ordered sweep would fold into one side of the window. It
/// also fills the whole picture evenly, so stopping early still leaves a usable, unbiased estimate. Rates further
/// than one dead band beyond the outermost rate that has answered are left out, so the sampling concentrates on the
/// region that can still say something.
/// </remarks>
public sealed class RandomSweepStrategy : IBaudSweepStrategy
{
    private RandomSweepStrategy()
    {
    }

    public static RandomSweepStrategy Instance { get; } = new();

    public string Id => "random";

    public string Name => "Random order (Monte Carlo)";

    public string Description =>
        "Visits the rates in random order, one request at a time, until each has had its share. The picture builds " +
        "up evenly over the whole span instead of side by side, so drift during a long sweep cannot masquerade as an " +
        "edge, and stopping early still leaves an unbiased estimate.";

    public string RequestsNote => "Requests each rate ends up with; they are spread over the sweep in random order.";

    public RequestEstimate EstimateRequests(BaudSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var smallest = Math.Min(1 + 2 * options.DeadBandSteps, options.Grid.Count);
        return new RequestEstimate(options.RequestsPerRate * smallest, options.RequestsPerRate * options.Grid.Count);
    }

    public IBaudSweepPlanner CreatePlanner(BaudSweepOptions options, BaudSweepResult results)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Planner(options);
    }

    private sealed class Planner(BaudSweepOptions options) : IBaudSweepPlanner
    {
        private readonly BaudGrid grid = options.Grid;
        private readonly int[] requests = new int[options.Grid.Count];
        private readonly List<int> candidates = [];

        private int lowestAnswered = int.MaxValue;
        private int highestAnswered = int.MinValue;
        private int requestsSent;
        private int pending;

        public int? ExpectedRequests
        {
            get
            {
                CollectCandidates();
                return requestsSent + candidates.Sum(index => options.RequestsPerRate - requests[Offset(index)]);
            }
        }

        public BaudSweepStep? Next()
        {
            CollectCandidates();
            if (candidates.Count == 0)
            {
                return null;
            }

            pending = candidates[Random.Shared.Next(candidates.Count)];
            requests[Offset(pending)]++;
            requestsSent++;
            return new BaudSweepStep(grid.RateAt(pending), 1);
        }

        public void OnCompleted(BaudSweepStepResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (result.IsDead)
            {
                return;
            }

            lowestAnswered = Math.Min(lowestAnswered, pending);
            highestAnswered = Math.Max(highestAnswered, pending);
        }

        /// <summary>
        /// The rates still worth a request: those with requests left, within one dead band of the outermost rate that
        /// has answered. Until something answers there is nothing to centre on, so the whole span stays in play.
        /// </summary>
        private void CollectCandidates()
        {
            var answered = lowestAnswered <= highestAnswered;
            var from = answered ? Math.Max(lowestAnswered - options.DeadBandSteps, -grid.MaxIndex) : -grid.MaxIndex;
            var to = answered ? Math.Min(highestAnswered + options.DeadBandSteps, grid.MaxIndex) : grid.MaxIndex;

            candidates.Clear();
            for (var index = from; index <= to; index++)
            {
                if (requests[Offset(index)] < options.RequestsPerRate)
                {
                    candidates.Add(index);
                }
            }
        }

        private int Offset(int index) => index + grid.MaxIndex;
    }
}

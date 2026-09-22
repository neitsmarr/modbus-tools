using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// Visits the rates in random order, each once, and sends all of <see cref="BaudSweepOptions.RequestsPerRate"/> in
/// that visit.
/// </summary>
/// <remarks>
/// Sampling in random order keeps anything that changes during the sweep - the device warming up, a load on the bus -
/// from being mistaken for an effect of the rate, which an ordered sweep would fold into one side of the window. It
/// also fills the whole picture evenly, so stopping early still leaves a usable, unbiased estimate. With
/// <see cref="BaudSweepOptions.UseDeadBand"/> on, rates further than one dead band beyond the outermost rate that has
/// answered are left out, so the sampling concentrates on the region that can still say something; until something
/// answers, the whole span stays in play either way.
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
        "Visits the rates in random order, sending all of a rate's requests in one visit. The picture builds up " +
        "evenly over the whole span instead of side by side, so drift during a long sweep cannot masquerade as an " +
        "edge, and stopping early still leaves an unbiased estimate. Until something answers it searches the whole " +
        "span, so it also finds a device far from the expected rate.";

    public string RequestsNote => "Requests sent at each rate in one visit; the rates are visited in random order.";

    public string DeadBandNote =>
        "Once something has answered, only rates within one dead band of the outermost reply are visited. " +
        "Unchecked, every rate in the span is.";

    public BaudSweepEstimate Estimate(BaudSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var smallest = options.UseDeadBand ? Math.Min(1 + 2 * options.DeadBandSteps, options.Grid.Count) : options.Grid.Count;
        return new BaudSweepEstimate(
            new RequestEstimate(options.RequestsPerRate * smallest, options.RequestsPerRate * options.Grid.Count),
            options.Grid.Count);
    }

    public IBaudSweepPlanner CreatePlanner(BaudSweepOptions options, BaudSweepResult results)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Planner(options);
    }

    private sealed class Planner(BaudSweepOptions options) : IBaudSweepPlanner
    {
        private readonly BaudGrid grid = options.Grid;
        private readonly bool[] visited = new bool[options.Grid.Count];
        private readonly List<int> candidates = [];

        private int lowestAnswered = int.MaxValue;
        private int highestAnswered = int.MinValue;
        private int requestsPlanned;
        private int pending;

        public int? ExpectedRequests
        {
            get
            {
                CollectCandidates();
                return requestsPlanned + candidates.Count * options.RequestsPerRate;
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
            visited[Offset(pending)] = true;
            requestsPlanned += options.RequestsPerRate;
            return new BaudSweepStep(grid.RateAt(pending), options.RequestsPerRate);
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
        /// The rates not visited yet, within one dead band of the outermost rate that has answered when the dead band
        /// is used. Until something answers there is nothing to centre on, so the whole span stays in play.
        /// </summary>
        private void CollectCandidates()
        {
            var limited = options.UseDeadBand && lowestAnswered <= highestAnswered;
            var from = limited ? Math.Max(lowestAnswered - options.DeadBandSteps, -grid.MaxIndex) : -grid.MaxIndex;
            var to = limited ? Math.Min(highestAnswered + options.DeadBandSteps, grid.MaxIndex) : grid.MaxIndex;

            candidates.Clear();
            for (var index = from; index <= to; index++)
            {
                if (!visited[Offset(index)])
                {
                    candidates.Add(index);
                }
            }
        }

        private int Offset(int index) => index + grid.MaxIndex;
    }
}
